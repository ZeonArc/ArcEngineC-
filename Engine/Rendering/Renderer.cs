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

    private Shader? _depthShader;
    private Shader? _particleShader;
    private int _particleTexture = -1;

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

        _particleShader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/particles.vert",
            "Assets/Shaders/particles.frag");

        _particleTexture = CreateSoftCircleTexture(64);
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

        // 1) Shadow depth pass.
        bool sunHasShadow = sun != null && sun.CastsShadows;
        if (sunHasShadow)
        {
            RenderDepthPass(scene, sun!);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, windowSize.X, windowSize.Y);
        }

        // 2) Main lit pass.
        Clear();

        float aspectRatio = windowSize.X / (float)windowSize.Y;
        var viewPos = camera.Transform.Position;
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(aspectRatio);
        var cameraFrustum = Frustum.FromViewProjection(view * projection);

        LastDrawnObjects = 0;
        LastCulledObjects = 0;
        LastDrawCalls = 0;

        BuildBatches(scene, cameraFrustum, frustumCull: true);

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
                    GL.BindTexture(TextureTarget.Texture2D, sun!.ShadowMapTexture);
                    shader.SetInt("shadowMap", ShadowMapTextureUnit);
                    shader.SetMatrix4("lightSpaceMatrix", sun.LightSpaceMatrix);
                    shader.SetInt("dirLightCastsShadows", 1);
                }
                else
                {
                    shader.SetInt("dirLightCastsShadows", 0);
                }
            }

            mesh.DrawInstanced(models);
            LastDrawnObjects += models.Count;
            LastDrawCalls++;
        }

        // 3) Particles pass.
        RenderParticles(scene, view, projection);
    }

    /// <summary>
    /// Walk the scene's MeshRenderers, transform their AABBs into world space, optionally
    /// frustum-cull, and group surviving instances by (Mesh, Material) reference identity.
    /// Mutates <see cref="_batches"/>: clears all lists in place (keys + List instances
    /// are reused frame-to-frame), then re-fills.
    /// </summary>
    private void BuildBatches(Scene scene, in Frustum frustum, bool frustumCull)
    {
        // Clear list contents but keep keys + backing arrays.
        foreach (var kv in _batches) kv.Value.Clear();

        foreach (var go in scene.GetObjects())
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null || mr.Mesh == null || mr.Material == null) continue;

            var world = go.Transform.GetWorldModelMatrix();

            if (frustumCull)
            {
                var worldAABB = mr.Mesh.LocalAABB.Transformed(world);
                if (!frustum.Intersects(worldAABB))
                {
                    LastCulledObjects++;
                    continue;
                }
            }

            var key = (mr.Mesh, mr.Material);
            if (!_batches.TryGetValue(key, out var list))
            {
                list = new List<Matrix4>();
                _batches[key] = list;
            }
            list.Add(world);
        }
    }

    private void RenderDepthPass(Scene scene, DirectionalLight light)
    {
        if (_depthShader == null) return;

        light.EnsureShadowResources();

        var proj = Matrix4.CreateOrthographic(20f, 20f, 0.1f, 40f);
        var eye = -light.Direction * 15f;
        var lightView = Matrix4.LookAt(eye, Vector3.Zero, Vector3.UnitY);
        light.LightSpaceMatrix = lightView * proj;

        var lightFrustum = Frustum.FromViewProjection(light.LightSpaceMatrix);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, light.ShadowMapFbo);
        GL.Viewport(0, 0, light.ShadowMapSize, light.ShadowMapSize);
        GL.Clear(ClearBufferMask.DepthBufferBit);

        _depthShader.Use();
        _depthShader.SetMatrix4("lightSpaceMatrix", light.LightSpaceMatrix);

        BuildBatches(scene, lightFrustum, frustumCull: true);
        foreach (var (key, models) in _batches)
        {
            if (models.Count == 0) continue;
            key.Item1.DrawInstanced(models);
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
