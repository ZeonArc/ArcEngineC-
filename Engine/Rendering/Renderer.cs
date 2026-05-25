using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Lighting;

namespace ArcEngine.Engine.Rendering;

public class Renderer
{
    /// <summary>Must match MAX_POINT_LIGHTS in basic.frag.</summary>
    public const int MaxPointLights = 4;

    /// <summary>Texture unit reserved for the shadow map sampler. Must match basic.frag.</summary>
    private const int ShadowMapTextureUnit = 2;

    private Shader? _depthShader;

    public void Init()
    {
        GL.Enable(EnableCap.DepthTest);

        _depthShader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/shadow_depth.vert",
            "Assets/Shaders/shadow_depth.frag");
    }

    public void Clear()
    {
        GL.ClearColor(0.05f, 0.06f, 0.08f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    /// <summary>
    /// Render the entire scene. Pulls the active camera, all lights, and all
    /// <see cref="MeshRenderer"/>s from the scene each frame. Runs a depth-only pre-pass
    /// for any directional light with <c>CastsShadows</c>, then the main lit pass.
    /// </summary>
    public void RenderScene(Scene scene, Vector2i windowSize)
    {
        var camera = scene.FindComponent<Camera>();
        if (camera == null) return;

        // Snapshot lights once per frame.
        var sun = scene.FindComponent<DirectionalLight>();
        var pointLights = new List<PointLight>();
        foreach (var p in scene.FindComponents<PointLight>())
        {
            pointLights.Add(p);
            if (pointLights.Count >= MaxPointLights) break;
        }

        // 1) Shadow depth pass (only if the sun casts shadows).
        bool sunHasShadow = sun != null && sun.CastsShadows;
        if (sunHasShadow)
        {
            RenderDepthPass(scene, sun!);
            // Restore main-pass framebuffer + viewport.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, windowSize.X, windowSize.Y);
        }

        // 2) Main lit pass.
        Clear();

        float aspectRatio = windowSize.X / (float)windowSize.Y;
        var viewPos = camera.Transform.Position;
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(aspectRatio);

        foreach (var go in scene.GetObjects())
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null || mr.Mesh == null || mr.Material == null) continue;

            mr.Material.Apply();

            var shader = mr.Material.Shader;
            shader.SetMatrix4("model", go.Transform.GetWorldModelMatrix());
            shader.SetMatrix4("view", view);
            shader.SetMatrix4("projection", projection);

            ApplyLighting(shader, scene.Ambient, sun, pointLights, viewPos);

            // Shadow uniforms — bind shadow map to its dedicated unit if available.
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

            mr.Mesh.Draw();
        }
    }

    /// <summary>
    /// Depth-only pass from <paramref name="light"/>'s POV. Lazily allocates the light's
    /// shadow FBO + depth texture, computes its light-space matrix, and renders all opaque
    /// geometry with the depth shader. Caller is responsible for restoring the main-pass
    /// framebuffer + viewport afterwards.
    /// </summary>
    private void RenderDepthPass(Scene scene, DirectionalLight light)
    {
        if (_depthShader == null) return;

        light.EnsureShadowResources();

        // Light-space matrix: orthographic projection covering ±10 around origin, depth 40.
        // View from -direction × 15 looking at origin with world-up.
        var proj = Matrix4.CreateOrthographic(20f, 20f, 0.1f, 40f);
        var eye = -light.Direction * 15f;
        var lightView = Matrix4.LookAt(eye, Vector3.Zero, Vector3.UnitY);
        light.LightSpaceMatrix = lightView * proj;

        // Bind the light's FBO and configure viewport for the depth texture's resolution.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, light.ShadowMapFbo);
        GL.Viewport(0, 0, light.ShadowMapSize, light.ShadowMapSize);
        GL.Clear(ClearBufferMask.DepthBufferBit);

        _depthShader.Use();
        _depthShader.SetMatrix4("lightSpaceMatrix", light.LightSpaceMatrix);

        foreach (var go in scene.GetObjects())
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null || mr.Mesh == null) continue;

            _depthShader.SetMatrix4("model", go.Transform.GetWorldModelMatrix());
            mr.Mesh.Draw();
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
