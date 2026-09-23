using OpenTK.Graphics.OpenGL4;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Post-process bloom — bright-pass extracts pixels above a luminance threshold into
/// a downscaled RGBA16F buffer, then a separable Gaussian blur is applied via
/// ping-pong. The final blurred buffer is exposed as <see cref="OutputTexture"/> for
/// the tonemap pass to composite additively.
///
/// Cost: one bright-pass draw + 2N ping-pong draws for N blur iterations, all at
/// quarter resolution.
/// </summary>
public class BloomPass : IDisposable
{
    /// <summary>Threshold below which HDR pixels contribute nothing to bloom.</summary>
    public float Threshold = 1.0f;

    /// <summary>Soft-knee width (0..1). Higher = wider smooth ramp below the threshold.</summary>
    public float SoftKnee = 0.5f;

    /// <summary>Number of ping-pong blur iterations. Each iteration = 1 H + 1 V pass.</summary>
    public int BlurIterations = 4;

    /// <summary>Enable/disable bloom without freeing GPU resources.</summary>
    public bool Enabled = true;

    /// <summary>The final blurred bloom texture. -1 if not yet built for this frame.</summary>
    public int OutputTexture { get; private set; } = -1;

    private int _widthHi;   // full-res dims (for u_texelSize normalization)
    private int _heightHi;

    private int _widthLo;   // quarter-res dims (bloom buffers)
    private int _heightLo;

    // Ping-pong RGBA16F targets at quarter res.
    private int _fbo0 = -1, _tex0 = -1;
    private int _fbo1 = -1, _tex1 = -1;

    private Shader? _bright;
    private Shader? _blur;
    private int _emptyVao = -1;

    private bool _disposed;

    public void Init()
    {
        _bright = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/postprocess.vert", "Assets/Shaders/bloom_bright.frag");
        _blur = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/postprocess.vert", "Assets/Shaders/bloom_blur.frag");

        _emptyVao = GL.GenVertexArray();
    }

    /// <summary>
    /// Re-allocate the ping-pong buffers if the target size changed. Bloom operates
    /// at quarter resolution (each axis halved) to keep the blur cheap and give it
    /// a natural low-frequency look.
    /// </summary>
    public void EnsureSize(int width, int height)
    {
        if (width == _widthHi && height == _heightHi && _fbo0 != -1) return;
        DisposeBuffers();

        _widthHi = width; _heightHi = height;
        _widthLo = System.Math.Max(1, width / 2);
        _heightLo = System.Math.Max(1, height / 2);

        (_fbo0, _tex0) = CreateHdrTarget(_widthLo, _heightLo);
        (_fbo1, _tex1) = CreateHdrTarget(_widthLo, _heightLo);
    }

    /// <summary>
    /// Extract bright pixels from <paramref name="hdrSource"/>, blur them
    /// <see cref="BlurIterations"/> times, and leave the result in
    /// <see cref="OutputTexture"/>. Returns -1 (and does not draw) when disabled.
    /// </summary>
    public int Render(int hdrSource)
    {
        if (!Enabled || _bright == null || _blur == null)
        {
            OutputTexture = -1;
            return -1;
        }

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
        GL.Viewport(0, 0, _widthLo, _heightLo);
        GL.BindVertexArray(_emptyVao);

        // 1) Bright pass — HDR source → _tex0.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo0);
        _bright.Use();
        _bright.SetFloat("u_threshold", Threshold);
        _bright.SetFloat("u_softKnee", SoftKnee);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, hdrSource);
        _bright.SetInt("u_hdr", 0);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // 2) Ping-pong blur — alternate H and V passes between _tex0 ↔ _tex1.
        var texel = new OpenTK.Mathematics.Vector2(1f / _widthLo, 1f / _heightLo);
        var axisH = new OpenTK.Mathematics.Vector2(1f, 0f);
        var axisV = new OpenTK.Mathematics.Vector2(0f, 1f);

        int srcTex = _tex0;
        for (int i = 0; i < BlurIterations; i++)
        {
            srcTex = BlurStep(srcTex, texel, axisH);
            srcTex = BlurStep(srcTex, texel, axisV);
        }

        GL.BindVertexArray(0);
        GL.Enable(EnableCap.DepthTest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        OutputTexture = srcTex;
        return OutputTexture;
    }

    /// <summary>One blur draw. Reads <paramref name="srcTex"/>, writes to the
    /// other ping-pong target, returns its texture.</summary>
    private int BlurStep(int srcTex, OpenTK.Mathematics.Vector2 texel, OpenTK.Mathematics.Vector2 axis)
    {
        int dstTex = (srcTex == _tex0) ? _tex1 : _tex0;
        int dstFbo = (dstTex == _tex0) ? _fbo0 : _fbo1;
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, dstFbo);
        _blur!.Use();
        _blur.SetVector2("u_texelSize", texel);
        _blur.SetVector2("u_axis", axis);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, srcTex);
        _blur.SetInt("u_src", 0);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        return dstTex;
    }

    private static (int fbo, int tex) CreateHdrTarget(int w, int h)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f,
            w, h, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        int fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, tex, 0);
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            Console.WriteLine($"[BloomPass] FBO incomplete: {status}");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return (fbo, tex);
    }

    private void DisposeBuffers()
    {
        if (_fbo0 != -1) { GL.DeleteFramebuffer(_fbo0); _fbo0 = -1; }
        if (_tex0 != -1) { GL.DeleteTexture(_tex0);     _tex0 = -1; }
        if (_fbo1 != -1) { GL.DeleteFramebuffer(_fbo1); _fbo1 = -1; }
        if (_tex1 != -1) { GL.DeleteTexture(_tex1);     _tex1 = -1; }
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
