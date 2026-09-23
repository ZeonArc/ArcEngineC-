using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Screen-space ambient occlusion pass. Reads the main HDR FBO's depth texture,
/// samples a rotated hemisphere kernel per pixel, and writes a single-channel
/// occlusion factor into <see cref="OutputTexture"/>. A separate blur pass
/// smooths the noisy raw output before it's composited into the tonemap.
///
/// Runs entirely from the depth texture — no G-buffer required — by
/// reconstructing view-space normals from screen-space derivatives.
/// </summary>
public class SsaoPass : IDisposable
{
    public const int KernelSize = 32;

    public bool  Enabled   = true;
    public float Radius    = 0.5f;
    public float Bias      = 0.025f;
    public float Intensity = 1.5f;

    public int OutputTexture => _blurTex;

    public int Width  { get; private set; }
    public int Height { get; private set; }

    private int _rawFbo = -1, _rawTex = -1;
    private int _blurFbo = -1, _blurTex = -1;
    private int _noiseTex = -1;

    private Shader? _ssaoShader;
    private Shader? _blurShader;
    private int _emptyVao = -1;

    private readonly Vector3[] _kernel = new Vector3[KernelSize];
    private bool _disposed;

    public void Init()
    {
        _ssaoShader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/postprocess.vert", "Assets/Shaders/ssao.frag");
        _blurShader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/postprocess.vert", "Assets/Shaders/ssao_blur.frag");

        _emptyVao = GL.GenVertexArray();

        BuildKernel();
        BuildNoiseTexture();
    }

    /// <summary>
    /// Hemisphere kernel with samples biased toward the fragment. Reused every frame.
    /// </summary>
    private void BuildKernel()
    {
        var rng = new Random(1234);
        for (int i = 0; i < KernelSize; i++)
        {
            Vector3 sample = new(
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)rng.NextDouble());              // z ≥ 0 → hemisphere
            sample = sample.LengthSquared > 0f ? Vector3.Normalize(sample) : Vector3.UnitZ;
            sample *= (float)rng.NextDouble();

            // Scale samples so more sit near the origin (accelerating interpolant).
            float t = i / (float)KernelSize;
            float scale = MathHelper.Lerp(0.1f, 1.0f, t * t);
            _kernel[i] = sample * scale;
        }
    }

    /// <summary>
    /// 4x4 tiled RGB texture of random tangent-plane rotations. Uploaded once and
    /// sampled with GL_REPEAT so the same tile scrolls across the screen.
    /// </summary>
    private void BuildNoiseTexture()
    {
        var rng = new Random(4321);
        float[] noise = new float[4 * 4 * 3];
        for (int i = 0; i < 16; i++)
        {
            noise[i * 3 + 0] = (float)(rng.NextDouble() * 2.0 - 1.0);
            noise[i * 3 + 1] = (float)(rng.NextDouble() * 2.0 - 1.0);
            noise[i * 3 + 2] = 0f;   // rotation is in the tangent plane
        }

        _noiseTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _noiseTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f,
            4, 4, 0, PixelFormat.Rgb, PixelType.Float, noise);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
    }

    /// <summary>(Re)allocate output textures for the given target size. Full-res.</summary>
    public void EnsureSize(int width, int height)
    {
        if (width == Width && height == Height && _rawFbo != -1) return;
        DisposeBuffers();

        Width = width; Height = height;
        (_rawFbo, _rawTex) = CreateR8Target(width, height);
        (_blurFbo, _blurTex) = CreateR8Target(width, height);
    }

    /// <summary>
    /// Run SSAO + blur. <paramref name="depthTex"/> is the scene depth (typically
    /// <c>PostProcess.HdrDepth</c>). Requires the projection matrix used for the main
    /// pass so view-space coords can be reconstructed.
    /// </summary>
    public void Render(int depthTex, Matrix4 projection)
    {
        if (!Enabled || _ssaoShader == null || _blurShader == null) return;

        Matrix4 invProj = Matrix4.Invert(projection);
        var noiseScale = new Vector2(Width / 4f, Height / 4f);

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
        GL.BindVertexArray(_emptyVao);

        // 1) Raw SSAO
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _rawFbo);
        GL.Viewport(0, 0, Width, Height);
        _ssaoShader.Use();

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, depthTex);
        _ssaoShader.SetInt("u_depth", 0);

        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _noiseTex);
        _ssaoShader.SetInt("u_noise", 1);

        _ssaoShader.SetMatrix4("u_projection", projection);
        _ssaoShader.SetMatrix4("u_invProjection", invProj);
        _ssaoShader.SetVector2("u_noiseScale", noiseScale);
        _ssaoShader.SetVector3Array("u_samples", _kernel);
        _ssaoShader.SetInt("u_sampleCount", KernelSize);
        _ssaoShader.SetFloat("u_radius", Radius);
        _ssaoShader.SetFloat("u_bias", Bias);
        _ssaoShader.SetFloat("u_intensity", Intensity);

        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // 2) 4x4 box-blur to hide sampling noise
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _blurFbo);
        _blurShader.Use();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _rawTex);
        _blurShader.SetInt("u_src", 0);
        _blurShader.SetVector2("u_texelSize", new Vector2(1f / Width, 1f / Height));
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.BindVertexArray(0);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Enable(EnableCap.DepthTest);
    }

    private static (int fbo, int tex) CreateR8Target(int w, int h)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8,
            w, h, 0, PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
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
            Console.WriteLine($"[SsaoPass] FBO incomplete: {status}");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return (fbo, tex);
    }

    private void DisposeBuffers()
    {
        if (_rawFbo   != -1) { GL.DeleteFramebuffer(_rawFbo);  _rawFbo   = -1; }
        if (_rawTex   != -1) { GL.DeleteTexture(_rawTex);      _rawTex   = -1; }
        if (_blurFbo  != -1) { GL.DeleteFramebuffer(_blurFbo); _blurFbo  = -1; }
        if (_blurTex  != -1) { GL.DeleteTexture(_blurTex);     _blurTex  = -1; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeBuffers();
        if (_noiseTex != -1) { GL.DeleteTexture(_noiseTex);    _noiseTex = -1; }
        if (_emptyVao != -1) { GL.DeleteVertexArray(_emptyVao); _emptyVao = -1; }
        GC.SuppressFinalize(this);
    }
}
