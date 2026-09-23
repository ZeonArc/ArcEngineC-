using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Fullscreen FXAA 3.11 pass. Reads a tonemapped LDR image and writes an
/// antialiased copy to the given destination framebuffer.
///
/// The pass owns its own LDR intermediate target (RGBA8) so <see cref="PostProcess"/>
/// can hand off a tonemapped image without touching the default framebuffer prematurely.
/// </summary>
public class FxaaPass : IDisposable
{
    /// <summary>Enable/disable FXAA without freeing GPU resources.</summary>
    public bool Enabled = true;

    public int LdrFbo   { get; private set; } = -1;
    public int LdrColor { get; private set; } = -1;

    public int Width  { get; private set; }
    public int Height { get; private set; }

    private Shader? _shader;
    private int _emptyVao = -1;
    private bool _disposed;

    public void Init()
    {
        _shader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/postprocess.vert", "Assets/Shaders/fxaa.frag");
        _emptyVao = GL.GenVertexArray();
    }

    /// <summary>Allocate or resize the LDR intermediate target. Called each frame.</summary>
    public void EnsureSize(int width, int height)
    {
        if (width == Width && height == Height && LdrFbo != -1) return;
        DisposeBuffers();

        Width = width;
        Height = height;

        LdrColor = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, LdrColor);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        LdrFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, LdrFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, LdrColor, 0);
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            Console.WriteLine($"[FxaaPass] LDR FBO incomplete: {status}");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Apply FXAA on <paramref name="ldrSource"/> and write into
    /// <paramref name="targetFbo"/> (0 = default).
    /// </summary>
    public void Render(int ldrSource, int targetFbo = 0)
    {
        if (_shader == null) return;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
        GL.Viewport(0, 0, Width, Height);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);

        _shader.Use();
        _shader.SetVector2("u_texelSize", new Vector2(1f / Width, 1f / Height));
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, ldrSource);
        _shader.SetInt("u_ldr", 0);

        GL.BindVertexArray(_emptyVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.DepthTest);
    }

    private void DisposeBuffers()
    {
        if (LdrFbo   != -1) { GL.DeleteFramebuffer(LdrFbo);  LdrFbo   = -1; }
        if (LdrColor != -1) { GL.DeleteTexture(LdrColor);    LdrColor = -1; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeBuffers();
        if (_emptyVao != -1) { GL.DeleteVertexArray(_emptyVao); _emptyVao = -1; }
        GC.SuppressFinalize(this);
    }
}
