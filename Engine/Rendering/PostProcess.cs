using OpenTK.Graphics.OpenGL4;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Owns the HDR offscreen framebuffer and the fullscreen tonemap pass. The main
/// scene renders into <see cref="HdrColor"/> as linear RGBA16F, then a call to
/// <see cref="Present"/> flushes it to the default framebuffer through ACES filmic
/// tonemapping + gamma.
///
/// Resizes lazily whenever <see cref="EnsureSize"/> is called with a new viewport.
///
/// GL objects owned:
/// <list type="bullet">
///   <item><see cref="HdrFbo"/> — main render target</item>
///   <item><see cref="HdrColor"/> — RGBA16F color attachment, sampled by the tonemap pass</item>
///   <item><see cref="HdrDepth"/> — depth24 renderbuffer</item>
///   <item>an empty VAO — the fullscreen triangle vertex shader generates its own positions</item>
/// </list>
/// </summary>
public class PostProcess : IDisposable
{
    public int HdrFbo   { get; private set; } = -1;
    public int HdrColor { get; private set; } = -1;
    /// <summary>Depth attachment as a TEXTURE (not a renderbuffer) so post passes such as SSAO can sample it.</summary>
    public int HdrDepth { get; private set; } = -1;

    public int Width  { get; private set; }
    public int Height { get; private set; }

    /// <summary>Linear multiplier applied before tonemapping. 1.0 = neutral.</summary>
    public float Exposure = 1.0f;

    /// <summary>Additive bloom strength when a bloom source is bound (0 = no bloom).</summary>
    public float BloomStrength = 0.04f;

    private Shader? _tonemapShader;
    private int _emptyVao = -1;
    private bool _disposed;

    public void Init()
    {
        _tonemapShader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/postprocess.vert",
            "Assets/Shaders/tonemap.frag");

        _emptyVao = GL.GenVertexArray();
    }

    /// <summary>
    /// (Re)allocate the HDR FBO if its size doesn't match. Called at the top of
    /// <c>RenderScene</c> before binding <see cref="HdrFbo"/>.
    /// </summary>
    public void EnsureSize(int width, int height)
    {
        if (width == Width && height == Height && HdrFbo != -1) return;
        DisposeHdrResources();

        Width = width;
        Height = height;

        HdrColor = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, HdrColor);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f,
            width, height, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // Depth as a texture (sampled by SSAO), 32F for reconstruction accuracy.
        HdrDepth = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, HdrDepth);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent32f,
            width, height, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        HdrFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, HdrFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, HdrColor, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, HdrDepth, 0);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            Console.WriteLine($"[PostProcess] HDR FBO incomplete: {status}");

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Bind the HDR framebuffer and set the viewport to its size. Call before the
    /// scene draw loop; call <see cref="Present"/> once the scene is complete.
    /// </summary>
    public void BeginScene()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, HdrFbo);
        GL.Viewport(0, 0, Width, Height);
    }

    /// <summary>
    /// Blit the HDR image through the tonemap shader into <paramref name="targetFbo"/>
    /// (0 = default framebuffer). <paramref name="bloomTex"/> and <paramref name="ssaoTex"/>,
    /// when non-negative, are composited (additive bloom, multiplicative SSAO); pass -1 to
    /// disable either.
    /// </summary>
    public void Present(int bloomTex = -1, int ssaoTex = -1, int targetFbo = 0)
    {
        if (_tonemapShader == null || HdrFbo == -1) return;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
        GL.Viewport(0, 0, Width, Height);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);

        _tonemapShader.Use();
        _tonemapShader.SetFloat("u_exposure", Exposure);
        _tonemapShader.SetFloat("u_bloomStrength", BloomStrength);
        _tonemapShader.SetInt("u_bloomEnabled", bloomTex >= 0 ? 1 : 0);
        _tonemapShader.SetInt("u_ssaoEnabled",  ssaoTex  >= 0 ? 1 : 0);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, HdrColor);
        _tonemapShader.SetInt("u_hdr", 0);

        if (bloomTex >= 0)
        {
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, bloomTex);
            _tonemapShader.SetInt("u_bloom", 1);
        }

        if (ssaoTex >= 0)
        {
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, ssaoTex);
            _tonemapShader.SetInt("u_ssao", 2);
        }

        GL.BindVertexArray(_emptyVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.DepthTest);
    }

    private void DisposeHdrResources()
    {
        if (HdrFbo   != -1) { GL.DeleteFramebuffer(HdrFbo); HdrFbo   = -1; }
        if (HdrColor != -1) { GL.DeleteTexture(HdrColor);   HdrColor = -1; }
        if (HdrDepth != -1) { GL.DeleteTexture(HdrDepth);   HdrDepth = -1; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeHdrResources();
        if (_emptyVao != -1) { GL.DeleteVertexArray(_emptyVao); _emptyVao = -1; }
        GC.SuppressFinalize(this);
    }
}
