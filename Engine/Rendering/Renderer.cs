using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Lighting;
using ArcEngine.Engine.Math;

namespace ArcEngine.Engine.Rendering;

public class Renderer
{
    /// <summary>Must match MAX_POINT_LIGHTS in basic.frag.</summary>
    public const int MaxPointLights = 4;

    /// <summary>
    /// Texture unit reserved for the shadow map sampler. Sits past the four
    /// PBR material maps (units 0-3) and an unused unit 4 (legacy specular slot).
    /// </summary>
    private const int ShadowMapTextureUnit = 5;

    /// <summary>Texture units for IBL — irradiance / prefiltered / BRDF LUT.</summary>
    private const int IrradianceUnit  = 6;
    private const int PrefilteredUnit = 7;
    private const int BrdfLutUnit     = 8;

    /// <summary>Texture unit for the (single) shadow-casting point light's cube.</summary>
    private const int PointShadowUnit = 9;

    private Shader? _depthShader;
    private Shader? _pointShadowShader;
    private Shader? _particleShader;
    private int _particleTexture = -1;

    /// <summary>
    /// HDR + tonemap post-processing chain. Public so the editor can tweak exposure /
    /// bloom strength via the inspector.
    /// </summary>
    public readonly PostProcess PostProcess = new();

    /// <summary>Bloom bright-pass + separable-Gaussian blur, composited by <see cref="PostProcess"/>.</summary>
    public readonly BloomPass Bloom = new();

    /// <summary>Fullscreen FXAA on the tonemapped LDR image. Toggle via <c>Fxaa.Enabled</c>.</summary>
    public readonly FxaaPass Fxaa = new();

    /// <summary>Screen-space ambient occlusion composited into tonemap. Toggle via <c>Ssao.Enabled</c>.</summary>
    public readonly SsaoPass Ssao = new();

    /// <summary>
    /// Active IBL environment. Assign via <see cref="LoadEnvironment"/>; null means
    /// the shader falls back to the hand-tuned <c>u_envDiffuse/u_envSpecular</c> split
    /// and no skybox is drawn.
    /// </summary>
    public EnvironmentMap? Environment;

    /// <summary>Projected-decal pass. Renders every <see cref="Decal"/> in the scene.</summary>
    public readonly DecalPass Decals = new();

    /// <summary>Convenience: load an HDR file into <see cref="Environment"/>. Runs the full IBL bake.</summary>
    public bool LoadEnvironment(string hdrPath)
    {
        var env = new EnvironmentMap();
        if (!env.LoadHdr(hdrPath)) { env.Dispose(); return false; }
        Environment?.Dispose();
        Environment = env;
        return true;
    }

    /// <summary>Per-frame culling stats. Read by the editor's status overlay.</summary>
    public int LastDrawnObjects;
    public int LastCulledObjects;
    public int LastDrawCalls;

    // ---- Reused-across-frames scratch buffers --------------------------------
    //
    // Cleared at the start of each frame, populated by BuildBatches, drained by
    // the draw loop. Lists are reused frame-to-frame so the GC doesn't see the
    // per-batch matrix churn.
    private readonly Dictionary<(Mesh, Material), List<Matrix4>> _batches = new();
    private readonly List<PointLight> _pointLights = new();

    // Lighting uniforms are identical across all batches in a frame, but the same
    // shader is bound by every Material.Apply(). Track which shaders have already
    // had their per-frame uniforms set so we skip the redundant uploads.
    private readonly HashSet<Shader> _frameUniformsSet = new();

    public void Init()
    {
        GL.Enable(EnableCap.DepthTest);

        _depthShader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/shadow_depth.vert",
            "Assets/Shaders/shadow_depth.frag");

        _pointShadowShader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/point_shadow.vert",
            "Assets/Shaders/point_shadow.frag");

        _particleShader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/particles.vert",
            "Assets/Shaders/particles.frag");

        _particleTexture = CreateSoftCircleTexture(64);

        PostProcess.Init();
        Bloom.Init();
        Fxaa.Init();
        Ssao.Init();
        Decals.Init();
    }

    /// <summary>
    /// Generate a 64x64 R8 soft-circle texture procedurally. Center = 1.0, edge = 0.0,
    /// quadratic falloff. Used by every <see cref="ParticleSystem"/>.
    /// </summary>
    private static int CreateSoftCircleTexture(int size)
    {
        byte[] pixels = new byte[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            float r = MathF.Sqrt(dx * dx + dy * dy);
            float falloff = MathF.Max(0f, 1f - r);
            falloff = falloff * falloff;
            pixels[y * size + x] = (byte)(falloff * 255f);
        }

        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8,
            size, size, 0, PixelFormat.Red, PixelType.UnsignedByte, pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return tex;
    }

    public void Clear()
    {
        GL.ClearColor(0.05f, 0.06f, 0.08f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    /// <summary>
    /// Forget every cached <c>(Mesh, Material)</c> batch. Call when switching scenes —
    /// otherwise the dictionary holds onto stale Mesh / Material refs and prevents GC.
    /// </summary>
    public void OnSceneSwitched()
    {
        _batches.Clear();
    }

    /// <summary>
    /// Render the entire scene. Pulls the active camera, all lights, and all
    /// <see cref="MeshRenderer"/>s from the scene each frame. Runs a depth-only pre-pass
    /// for any directional light with <c>CastsShadows</c>, then the main lit pass,
    /// then particles.
    /// </summary>
    public void RenderScene(Scene scene, Vector2i windowSize)
    {
        var camera = scene.FindComponent<Camera>();
        if (camera == null) return;

        // Snapshot lights once per frame.
        var sun = scene.FindComponent<DirectionalLight>();
        _pointLights.Clear();
        foreach (var p in scene.FindComponents<PointLight>())
        {
            _pointLights.Add(p);
            if (_pointLights.Count >= MaxPointLights) break;
        }

        // 0) Ensure HDR target matches viewport and bind it as the main render target.
        PostProcess.EnsureSize(windowSize.X, windowSize.Y);

        // 1) Shadow depth pass — writes into the light's shadow FBO, so bind order
        //    below (HDR) restores the main target afterwards.
        bool sunHasShadow = sun != null && sun.CastsShadows;
        if (sunHasShadow) RenderDepthPass(scene, sun!, camera, windowSize);

        // 1b) Point-light shadow cube for the first shadow-casting point light.
        PointLight? shadowPoint = null;
        int shadowPointIndex = -1;
        for (int i = 0; i < _pointLights.Count; i++)
        {
            if (_pointLights[i].CastsShadows)
            {
                shadowPoint = _pointLights[i];
                shadowPointIndex = i;
                break;
            }
        }
        if (shadowPoint != null) RenderPointShadowDepth(scene, shadowPoint);

        // 2) Main lit pass into the HDR framebuffer.
        PostProcess.BeginScene();
        Clear();

        float aspectRatio = windowSize.X / (float)windowSize.Y;
        var viewPos = camera.Transform.Position;
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(aspectRatio);
        var cameraFrustum = Frustum.FromViewProjection(view * projection);

        LastDrawnObjects = 0;
        LastCulledObjects = 0;
        LastDrawCalls = 0;

        BuildBatches(scene, cameraFrustum, frustumCull: true, cameraPos: viewPos);

        _frameUniformsSet.Clear();

        foreach (var (key, models) in _batches)
        {
            if (models.Count == 0) continue;

            var (mesh, material) = key;
            material.Apply();

            var shader = material.Shader;

            // Per-frame uniforms (view / projection / lights / shadow) are identical
            // across all batches; set them once per shader, not per batch.
            if (_frameUniformsSet.Add(shader))
            {
                shader.SetMatrix4("view", view);
                shader.SetMatrix4("projection", projection);
                ApplyLighting(shader, scene.Ambient, sun, _pointLights, viewPos);

                if (sunHasShadow)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + ShadowMapTextureUnit);
                    GL.BindTexture(TextureTarget.Texture2DArray, sun!.ShadowMapArray);
                    shader.SetInt("shadowMapArray", ShadowMapTextureUnit);
                    for (int i = 0; i < DirectionalLight.CascadeCount; i++)
                    {
                        shader.SetMatrix4($"lightSpaceMatrices[{i}]", sun.LightSpaceMatrices[i]);
                        shader.SetFloat  ($"cascadeSplits[{i}]",     sun.CascadeSplitsViewZ[i]);
                    }
                    shader.SetInt("dirLightCastsShadows", 1);
                }
                else
                {
                    shader.SetInt("dirLightCastsShadows", 0);
                }

                // IBL: bind cubemaps + LUT if an environment is loaded, otherwise
                // the fragment shader keeps using the hand-tuned envDiffuse/envSpecular.
                if (Environment != null && Environment.IsBaked)
                {
                    Environment.BindForShading(shader, IrradianceUnit, PrefilteredUnit, BrdfLutUnit);
                }
                else
                {
                    shader.SetInt("u_iblEnabled", 0);
                }

                // Point-light shadows for the first shadow-casting point light.
                if (shadowPoint != null)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + PointShadowUnit);
                    GL.BindTexture(TextureTarget.TextureCubeMap, shadowPoint.ShadowMapCube);
                    shader.SetInt("pointShadowMap", PointShadowUnit);
                    shader.SetVector3("pointShadowLightPos", shadowPoint.Transform.Position);
                    shader.SetFloat("pointShadowFarPlane", shadowPoint.ShadowFarPlane);
                    shader.SetInt("pointShadowLightIndex", shadowPointIndex);
                }
                else
                {
                    shader.SetInt("pointShadowLightIndex", -1);
                }
            }

            mesh.DrawInstanced(models);
            LastDrawnObjects += models.Count;
            LastDrawCalls++;
        }

        // 3a-pre) Skinned meshes — drawn one at a time (no instancing) into the
        //         same HDR target with their skinning palette uploaded per draw.
        RenderSkinnedMeshes(scene, view, projection, viewPos, sun, sunHasShadow, shadowPoint, shadowPointIndex);

        // 3a) Skybox — draws where opaque geometry left the far-plane depth intact.
        Environment?.RenderSky(view, projection);

        // 3b) Decals — projected onto the opaque scene via depth-reconstruction.
        //     Must precede particles so decals don't fight additive transparent passes.
        Decals.Render(scene, view, projection, PostProcess.HdrDepth);

        // 3c) Particles pass (still into HDR).
        RenderParticles(scene, view, projection);

        // 4) SSAO from the HDR FBO's depth texture.
        Ssao.EnsureSize(windowSize.X, windowSize.Y);
        Ssao.Render(PostProcess.HdrDepth, projection);
        int ssaoTex = Ssao.Enabled ? Ssao.OutputTexture : -1;

        // 5) Bloom: extract + blur bright pixels from HDR (quarter-res buffers).
        Bloom.EnsureSize(windowSize.X, windowSize.Y);
        int bloomTex = Bloom.Render(PostProcess.HdrColor);

        // 6) Tonemap HDR (+ optional bloom / SSAO composite). When FXAA is on,
        //    tonemap into an LDR intermediate first; otherwise straight to backbuffer.
        if (Fxaa.Enabled)
        {
            Fxaa.EnsureSize(windowSize.X, windowSize.Y);
            PostProcess.Present(bloomTex, ssaoTex, Fxaa.LdrFbo);
            Fxaa.Render(Fxaa.LdrColor, targetFbo: 0);
        }
        else
        {
            PostProcess.Present(bloomTex, ssaoTex, targetFbo: 0);
        }
    }

    /// <summary>
    /// Walk the scene's MeshRenderers, transform their AABBs into world space, optionally
    /// frustum-cull, and group surviving instances by (Mesh, Material) reference identity.
    /// Mutates <see cref="_batches"/>: clears all lists in place (keys + List instances
    /// are reused frame-to-frame), then re-fills.
    /// </summary>
    private void BuildBatches(Scene scene, in Frustum frustum, bool frustumCull, Vector3 cameraPos)
    {
        // Clear list contents but keep keys + backing arrays.
        foreach (var kv in _batches) kv.Value.Clear();

        foreach (var go in scene.GetObjects())
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null || mr.Material == null) continue;

            var world = go.Transform.GetWorldModelMatrix();
            var worldPos = new Vector3(world.M41, world.M42, world.M43);
            float camDist = (worldPos - cameraPos).Length;

            var mesh = mr.SelectMesh(camDist);
            if (mesh == null) continue;

            if (frustumCull)
            {
                var worldAABB = mesh.LocalAABB.Transformed(world);
                if (!frustum.Intersects(worldAABB))
                {
                    LastCulledObjects++;
                    continue;
                }
            }

            var key = (mesh, mr.Material);
            if (!_batches.TryGetValue(key, out var list))
            {
                list = new List<Matrix4>();
                _batches[key] = list;
            }
            list.Add(world);
        }
    }

    /// <summary>
    /// Compute per-cascade split distances (view-space, positive) using the classic
    /// "practical splits" scheme: a blend between uniform and logarithmic partitioning.
    /// Lambda=0 is fully uniform (linear falloff), lambda=1 is fully logarithmic
    /// (biased toward the camera). 0.5 is the standard middle-ground.
    /// </summary>
    private static void ComputeCascadeSplits(float near, float far, int cascadeCount, float lambda, float[] outSplits)
    {
        for (int i = 0; i < cascadeCount; i++)
        {
            float p = (i + 1) / (float)cascadeCount;
            float logSplit = near * MathF.Pow(far / near, p);
            float uniSplit = near + (far - near) * p;
            outSplits[i] = lambda * logSplit + (1f - lambda) * uniSplit;
        }
    }

    /// <summary>
    /// Fit a tight light-space orthographic matrix to the camera's view sub-frustum
    /// bounded by <paramref name="nearZ"/> and <paramref name="farZ"/>. Returns the
    /// combined lightView * lightProj matrix.
    /// </summary>
    private static Matrix4 ComputeCascadeLightMatrix(
        Camera camera, float aspect, float nearZ, float farZ, Vector3 lightDir)
    {
        // Sub-frustum in world space: 8 corners derived by inverse-transforming the
        // NDC cube through a projection using this cascade's near/far, plus the camera's view.
        var view = camera.GetViewMatrix();
        var subProj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(camera.Fov), aspect, nearZ, farZ);
        Matrix4 invVP = Matrix4.Invert(view * subProj);

        Span<Vector3> ndcCorners = stackalloc Vector3[]
        {
            new(-1f, -1f, -1f), new(1f, -1f, -1f), new(1f, 1f, -1f), new(-1f, 1f, -1f),
            new(-1f, -1f,  1f), new(1f, -1f,  1f), new(1f, 1f,  1f), new(-1f, 1f,  1f),
        };
        Span<Vector3> worldCorners = stackalloc Vector3[8];
        Vector3 center = Vector3.Zero;
        for (int i = 0; i < 8; i++)
        {
            var v = new Vector4(ndcCorners[i], 1f) * invVP;   // OpenTK: row-vector convention
            v /= v.W;
            worldCorners[i] = v.Xyz;
            center += v.Xyz;
        }
        center /= 8f;

        // Light view: look at frustum center from far behind along -lightDir.
        // Distance is picked so the light view's near plane sits well behind any caster.
        var dir = Vector3.Normalize(lightDir);
        var up = MathF.Abs(dir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        // We don't know the caster extent yet; use frustum radius as a proxy and add margin.
        float radius = 0f;
        for (int i = 0; i < 8; i++)
            radius = MathF.Max(radius, (worldCorners[i] - center).Length);
        float lightEyeDist = radius + 50f;
        var lightEye = center - dir * lightEyeDist;
        var lightView = Matrix4.LookAt(lightEye, center, up);

        // Fit an AABB of the world corners in light space, then build an ortho from it.
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var v = new Vector4(worldCorners[i], 1f) * lightView;
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.Z < minZ) minZ = v.Z;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
            if (v.Z > maxZ) maxZ = v.Z;
        }

        // Light looks down its -Z; near = distance to nearest light-space Z (= -maxZ),
        // far = distance to farthest (= -minZ). Extend near back to catch shadow casters
        // that sit BETWEEN the light and the frustum.
        float lightNear = -maxZ - 50f;
        float lightFar  = -minZ;
        if (lightNear >= lightFar) lightNear = lightFar - 0.1f;

        var lightProj = Matrix4.CreateOrthographicOffCenter(minX, maxX, minY, maxY, lightNear, lightFar);
        return lightView * lightProj;
    }

    /// <summary>
    /// Standard 6-face LookAt basis for cubemap rendering. Public so IBL can
    /// share the same convention if we ever consolidate.
    /// </summary>
    private static readonly (Vector3 forward, Vector3 up)[] s_cubeFaceViews =
    {
        (new(  1,  0,  0), new(0, -1,  0)),   // +X
        (new( -1,  0,  0), new(0, -1,  0)),   // -X
        (new(  0,  1,  0), new(0,  0,  1)),   // +Y
        (new(  0, -1,  0), new(0,  0, -1)),   // -Y
        (new(  0,  0,  1), new(0, -1,  0)),   // +Z
        (new(  0,  0, -1), new(0, -1,  0)),   // -Z
    };

    private void RenderSkinnedMeshes(
        Scene scene, Matrix4 view, Matrix4 projection, Vector3 viewPos,
        DirectionalLight? sun, bool sunHasShadow,
        PointLight? shadowPoint, int shadowPointIndex)
    {
        // Reused across renderers on the same shader.
        var seenShaders = new HashSet<Shader>();

        foreach (var smr in scene.FindComponents<SkinnedMeshRenderer>())
        {
            if (smr.Mesh == null || smr.Material == null) continue;

            var shader = smr.Material.Shader;

            // Per-shader per-frame uniforms — same set the opaque pass sends. Only
            // the first skinned renderer on this shader pays for the upload.
            if (seenShaders.Add(shader))
            {
                smr.Material.Apply();
                shader.SetMatrix4("view", view);
                shader.SetMatrix4("projection", projection);
                ApplyLighting(shader, scene.Ambient, sun, _pointLights, viewPos);

                if (sunHasShadow)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + ShadowMapTextureUnit);
                    GL.BindTexture(TextureTarget.Texture2DArray, sun!.ShadowMapArray);
                    shader.SetInt("shadowMapArray", ShadowMapTextureUnit);
                    for (int i = 0; i < DirectionalLight.CascadeCount; i++)
                    {
                        shader.SetMatrix4($"lightSpaceMatrices[{i}]", sun.LightSpaceMatrices[i]);
                        shader.SetFloat  ($"cascadeSplits[{i}]",     sun.CascadeSplitsViewZ[i]);
                    }
                    shader.SetInt("dirLightCastsShadows", 1);
                }
                else
                {
                    shader.SetInt("dirLightCastsShadows", 0);
                }

                if (Environment != null && Environment.IsBaked)
                    Environment.BindForShading(shader, IrradianceUnit, PrefilteredUnit, BrdfLutUnit);
                else
                    shader.SetInt("u_iblEnabled", 0);

                if (shadowPoint != null)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + PointShadowUnit);
                    GL.BindTexture(TextureTarget.TextureCubeMap, shadowPoint.ShadowMapCube);
                    shader.SetInt("pointShadowMap", PointShadowUnit);
                    shader.SetVector3("pointShadowLightPos", shadowPoint.Transform.Position);
                    shader.SetFloat("pointShadowFarPlane", shadowPoint.ShadowFarPlane);
                    shader.SetInt("pointShadowLightIndex", shadowPointIndex);
                }
                else
                {
                    shader.SetInt("pointShadowLightIndex", -1);
                }
            }
            else
            {
                // Material's per-texture bindings still need to be reapplied per renderer.
                smr.Material.Apply();
            }

            // Per-renderer uniforms: world transform + bone palette.
            shader.SetMatrix4("u_model", smr.Transform.GetWorldModelMatrix());

            var animator = smr.GameObject.GetComponent<ArcEngine.Engine.Animation.Animator>();
            if (animator?.Skeleton is { } skeleton)
            {
                // Palette lives as IReadOnlyList<Matrix4>; copy into an array once per draw.
                var palette = new Matrix4[skeleton.Bones.Length];
                for (int i = 0; i < palette.Length; i++) palette[i] = skeleton.Palette[i];
                shader.SetMatrix4Array("u_bonePalette", palette);
            }

            smr.Mesh.Draw();
            LastDrawnObjects++;
            LastDrawCalls++;
        }
    }

    private void RenderPointShadowDepth(Scene scene, PointLight light)
    {
        if (_pointShadowShader == null) return;

        light.EnsureShadowResources();

        var proj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(90f), 1f, 0.1f, light.ShadowFarPlane);
        var lightPos = light.Transform.Position;

        _pointShadowShader.Use();
        _pointShadowShader.SetVector3("u_lightPos", lightPos);
        _pointShadowShader.SetFloat("u_farPlane", light.ShadowFarPlane);

        for (int face = 0; face < 6; face++)
        {
            var (forward, up) = s_cubeFaceViews[face];
            var view = Matrix4.LookAt(lightPos, lightPos + forward, up);
            _pointShadowShader.SetMatrix4("u_lightViewProj", view * proj);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, light.ShadowMapFbos[face]);
            GL.Viewport(0, 0, light.ShadowMapSize, light.ShadowMapSize);
            GL.ClearColor(1f, 1f, 1f, 1f);           // "no occluder" = full farPlane distance
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var faceFrustum = Frustum.FromViewProjection(view * proj);
            BuildBatches(scene, faceFrustum, frustumCull: true, cameraPos: lightPos);
            foreach (var (key, models) in _batches)
            {
                if (models.Count == 0) continue;
                key.Item1.DrawInstanced(models);
            }
        }
    }

    private void RenderDepthPass(Scene scene, DirectionalLight light, Camera camera, Vector2i windowSize)
    {
        if (_depthShader == null) return;

        light.EnsureShadowResources();

        float aspect = windowSize.X / (float)windowSize.Y;
        ComputeCascadeSplits(camera.NearClip, camera.FarClip, DirectionalLight.CascadeCount, 0.5f, light.CascadeSplitsViewZ);

        _depthShader.Use();

        float prevSplit = camera.NearClip;
        for (int i = 0; i < DirectionalLight.CascadeCount; i++)
        {
            float split = light.CascadeSplitsViewZ[i];
            light.LightSpaceMatrices[i] = ComputeCascadeLightMatrix(camera, aspect, prevSplit, split, light.Direction);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, light.ShadowMapFbos[i]);
            GL.Viewport(0, 0, light.ShadowMapSize, light.ShadowMapSize);
            GL.Clear(ClearBufferMask.DepthBufferBit);

            _depthShader.SetMatrix4("lightSpaceMatrix", light.LightSpaceMatrices[i]);

            var cascadeFrustum = Frustum.FromViewProjection(light.LightSpaceMatrices[i]);
            // For the shadow pass use the camera position so LOD picks track the
            // final rendered LOD (avoids self-shadowing artefacts from LOD mismatch).
            BuildBatches(scene, cascadeFrustum, frustumCull: true, cameraPos: camera.Transform.Position);
            foreach (var (key, models) in _batches)
            {
                if (models.Count == 0) continue;
                key.Item1.DrawInstanced(models);
            }

            prevSplit = split;
        }
    }

    private void RenderParticles(Scene scene, Matrix4 view, Matrix4 projection)
    {
        if (_particleShader == null) return;

        bool any = false;
        foreach (var ps in scene.FindComponents<ParticleSystem>())
        {
            if (ps.AliveCount == 0) continue;

            if (!any)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                GL.DepthMask(false);

                _particleShader.Use();
                _particleShader.SetMatrix4("view", view);
                _particleShader.SetMatrix4("projection", projection);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _particleTexture);
                _particleShader.SetInt("u_particleTex", 0);

                any = true;
            }

            ps.EnsureGpuResources();
            ps.Draw();
            LastDrawCalls++;
        }

        if (any)
        {
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }
    }

    private static void ApplyLighting(
        Shader shader,
        Vector3 ambient,
        DirectionalLight? sun,
        IReadOnlyList<PointLight> pointLights,
        Vector3 viewPos)
    {
        shader.SetVector3("viewPos", viewPos);
        shader.SetVector3("ambient", ambient);
        shader.SetVector3("u_envDiffuse",  ambient);
        shader.SetVector3("u_envSpecular", ambient * 1.5f);

        if (sun != null)
        {
            shader.SetVector3("dirLight.direction", sun.Direction);
            shader.SetVector3("dirLight.color", sun.EffectiveColor);
        }
        else
        {
            shader.SetVector3("dirLight.direction", new Vector3(0f, -1f, 0f));
            shader.SetVector3("dirLight.color", Vector3.Zero);
        }

        int n = System.Math.Min(pointLights.Count, MaxPointLights);
        shader.SetInt("numPointLights", n);

        for (int i = 0; i < n; i++)
        {
            var p = pointLights[i];
            shader.SetVector3($"pointLights[{i}].position",  p.Transform.Position);
            shader.SetVector3($"pointLights[{i}].color",     p.EffectiveColor);
            shader.SetFloat  ($"pointLights[{i}].constant",  p.Constant);
            shader.SetFloat  ($"pointLights[{i}].linear",    p.Linear);
            shader.SetFloat  ($"pointLights[{i}].quadratic", p.Quadratic);
        }
    }
}
