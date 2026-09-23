using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Draws every <see cref="Decal"/> component in the scene as a projected OBB
/// after the opaque pass. Requires a sampleable depth texture from the main
/// scene FBO (<see cref="PostProcess.HdrDepth"/>) and the same view/projection
/// matrices the opaque pass used.
///
/// Blends alpha over the HDR color; runs with depth-test disabled so the OBB
/// isn't clipped by nearby geometry (the shader itself discards fragments
/// outside the OBB, and reconstructs the surface hit from depth).
/// </summary>
public class DecalPass : IDisposable
{
    private Shader? _shader;
    /// <summary>Shared unit-cube VAO, borrowed from <see cref="EnvironmentMap"/>-style bake code.</summary>
    private int _cubeVao = -1;
    private int _cubeVbo = -1;
    private bool _disposed;

    public void Init()
    {
        _shader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/decal.vert", "Assets/Shaders/decal.frag");
        BuildUnitCube();
    }

    /// <summary>Draw all decals in the scene. No-op when the scene has none.</summary>
    public void Render(Scene scene, in Matrix4 view, in Matrix4 projection, int sceneDepthTex)
    {
        if (_shader == null || _cubeVao == -1) return;

        // Collect once, avoid enumerating twice.
        List<Decal>? decals = null;
        foreach (var d in scene.FindComponents<Decal>())
        {
            if (d.Texture == null) continue;
            (decals ??= new List<Decal>()).Add(d);
        }
        if (decals == null) return;

        Matrix4 invVP = Matrix4.Invert(view * projection);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);  // camera can be inside or outside the OBB

        _shader.Use();
        _shader.SetMatrix4("u_view", view);
        _shader.SetMatrix4("u_projection", projection);
        _shader.SetMatrix4("u_invViewProj", invVP);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, sceneDepthTex);
        _shader.SetInt("u_sceneDepth", 0);

        GL.BindVertexArray(_cubeVao);

        foreach (var d in decals)
        {
            var worldMatrix = d.Transform.GetWorldModelMatrix();
            _shader.SetMatrix4("u_model", worldMatrix);
            _shader.SetMatrix4("u_invDecalWorld", Matrix4.Invert(worldMatrix));

            _shader.SetFloat("u_softEdge", d.SoftEdge);
            var tint = d.Tint;
            GL.Uniform4(GL.GetUniformLocation(_shader.Handle, "u_tint"), tint.X, tint.Y, tint.Z, tint.W);

            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, d.Texture!.Handle);
            _shader.SetInt("u_decalTexture", 1);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }

        GL.BindVertexArray(0);
        GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
    }

    private void BuildUnitCube()
    {
        // Same 36-vertex position-only cube as EnvironmentMap uses.
        float[] verts =
        {
            -0.5f,-0.5f,-0.5f,  0.5f, 0.5f,-0.5f,  0.5f,-0.5f,-0.5f,
             0.5f, 0.5f,-0.5f, -0.5f,-0.5f,-0.5f, -0.5f, 0.5f,-0.5f,
            -0.5f,-0.5f, 0.5f,  0.5f,-0.5f, 0.5f,  0.5f, 0.5f, 0.5f,
             0.5f, 0.5f, 0.5f, -0.5f, 0.5f, 0.5f, -0.5f,-0.5f, 0.5f,
            -0.5f, 0.5f, 0.5f, -0.5f, 0.5f,-0.5f, -0.5f,-0.5f,-0.5f,
            -0.5f,-0.5f,-0.5f, -0.5f,-0.5f, 0.5f, -0.5f, 0.5f, 0.5f,
             0.5f, 0.5f, 0.5f,  0.5f,-0.5f,-0.5f,  0.5f, 0.5f,-0.5f,
             0.5f,-0.5f,-0.5f,  0.5f, 0.5f, 0.5f,  0.5f,-0.5f, 0.5f,
            -0.5f,-0.5f,-0.5f,  0.5f,-0.5f,-0.5f,  0.5f,-0.5f, 0.5f,
             0.5f,-0.5f, 0.5f, -0.5f,-0.5f, 0.5f, -0.5f,-0.5f,-0.5f,
            -0.5f, 0.5f,-0.5f, -0.5f, 0.5f, 0.5f,  0.5f, 0.5f, 0.5f,
             0.5f, 0.5f, 0.5f,  0.5f, 0.5f,-0.5f, -0.5f, 0.5f,-0.5f,
        };
        _cubeVao = GL.GenVertexArray();
        _cubeVbo = GL.GenBuffer();
        GL.BindVertexArray(_cubeVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _cubeVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_cubeVao != -1) { GL.DeleteVertexArray(_cubeVao); _cubeVao = -1; }
        if (_cubeVbo != -1) { GL.DeleteBuffer(_cubeVbo); _cubeVbo = -1; }
        GC.SuppressFinalize(this);
    }
}
