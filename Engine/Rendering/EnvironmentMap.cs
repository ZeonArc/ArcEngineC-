using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using StbImageSharp;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// One image-based lighting (IBL) environment. Owns the four GL resources needed
/// for both skybox rendering and PBR ambient shading:
/// <list type="bullet">
///   <item><b>EnvCubemap</b> — the source HDR cubemap (baked from a lat-long HDRI).</item>
///   <item><b>IrradianceCubemap</b> — cosine-weighted convolution for the diffuse ambient term.</item>
///   <item><b>PrefilteredCubemap</b> — mipped GGX-convolved cubemap for specular reflections.</item>
///   <item><b>BrdfLut</b> — 2D scale/bias LUT for the split-sum specular integration (shared/static).</item>
/// </list>
///
/// Consumers:
/// <list type="bullet">
///   <item><see cref="BindForShading"/> attaches the three sampling targets + sets uniform flags.</item>
///   <item><see cref="RenderSky"/> draws the environment cubemap as a background skybox.</item>
/// </list>
/// </summary>
public class EnvironmentMap : IDisposable
{
    /// <summary>Face resolution of the source environment cubemap.</summary>
    public const int EnvFaceSize = 512;
    /// <summary>Face resolution of the irradiance cubemap (small — result is very smooth).</summary>
    public const int IrradianceFaceSize = 32;
    /// <summary>Face resolution of the prefiltered specular cubemap mip 0.</summary>
    public const int PrefilterFaceSize = 128;
    /// <summary>Mip level count in the prefiltered cubemap (roughness = level / (Mips-1)).</summary>
    public const int PrefilterMipCount = 5;
    /// <summary>Edge length of the 2D BRDF LUT.</summary>
    public const int BrdfLutSize = 512;

    public int EnvCubemap        { get; private set; } = -1;
    public int IrradianceCubemap { get; private set; } = -1;
    public int PrefilteredCubemap{ get; private set; } = -1;
    public int BrdfLut           { get; private set; } = -1;

    /// <summary>Skybox brightness multiplier (background only — does not affect IBL shading).</summary>
    public float SkyIntensity = 1.0f;

    /// <summary>True once <see cref="LoadHdr"/> has completed successfully.</summary>
    public bool IsBaked { get; private set; }

    // Shared static resources across all EnvironmentMap instances.
    private static int s_cubeVao = -1;
    private static int s_cubeVbo = -1;
    private static int s_quadVao = -1;
    private static Shader? s_equirectShader, s_irradianceShader, s_prefilterShader, s_brdfLutShader;
    private static Shader? s_skyboxShader;
    private static int s_sharedBrdfLut = -1;

    private bool _disposed;

    // ============================================================================
    // Bake
    // ============================================================================

    /// <summary>
    /// Load an HDR (.hdr / RGBE) equirectangular file and run the full IBL bake:
    /// equirect→cube, irradiance convolution, prefiltered specular, BRDF LUT.
    /// The GL context must be current; blocks until every bake pass completes.
    /// </summary>
    public bool LoadHdr(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine($"[EnvironmentMap] HDR file not found: {path}");
            return false;
        }

        EnsureSharedResources();

        // 1) Load the equirectangular HDR into a 2D RGB16F texture.
        int equirectTex;
        using (var stream = System.IO.File.OpenRead(path))
        {
            var img = ImageResultFloat.FromStream(stream, ColorComponents.RedGreenBlue);
            equirectTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, equirectTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f,
                img.Width, img.Height, 0, PixelFormat.Rgb, PixelType.Float, img.Data);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        // Capture the current viewport so we can restore it after the bake passes.
        int[] prevViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, prevViewport);

        // 2) Bake equirect → env cubemap.
        EnvCubemap = BakeEquirectToCube(equirectTex, EnvFaceSize);

        // 3) Bake irradiance cubemap from the env cubemap.
        IrradianceCubemap = BakeIrradiance(EnvCubemap, IrradianceFaceSize);

        // 4) Bake prefiltered specular cubemap (mipped).
        PrefilteredCubemap = BakePrefilteredSpecular(EnvCubemap, PrefilterFaceSize, PrefilterMipCount);

        // 5) Bake / reuse the split-sum BRDF LUT (shared across environments).
        BrdfLut = EnsureBrdfLut();

        GL.DeleteTexture(equirectTex);
        GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);

        IsBaked = true;
        Console.WriteLine($"[EnvironmentMap] Baked IBL from {path}");
        return true;
    }

    // ============================================================================
    // Runtime binding
    // ============================================================================

    /// <summary>
    /// Bind irradiance/prefiltered cubemaps + BRDF LUT to the given texture units,
    /// then set the corresponding samplers + <c>u_iblEnabled</c> on <paramref name="shader"/>.
    /// </summary>
    public void BindForShading(Shader shader, int irradianceUnit, int prefilteredUnit, int brdfUnit)
    {
        if (!IsBaked)
        {
            shader.SetInt("u_iblEnabled", 0);
            return;
        }

        GL.ActiveTexture(TextureUnit.Texture0 + irradianceUnit);
        GL.BindTexture(TextureTarget.TextureCubeMap, IrradianceCubemap);
        shader.SetInt("u_irradianceMap", irradianceUnit);

        GL.ActiveTexture(TextureUnit.Texture0 + prefilteredUnit);
        GL.BindTexture(TextureTarget.TextureCubeMap, PrefilteredCubemap);
        shader.SetInt("u_prefilteredMap", prefilteredUnit);

        GL.ActiveTexture(TextureUnit.Texture0 + brdfUnit);
        GL.BindTexture(TextureTarget.Texture2D, BrdfLut);
        shader.SetInt("u_brdfLut", brdfUnit);

        shader.SetInt("u_iblEnabled", 1);
        shader.SetFloat("u_prefilteredMipCount", PrefilterMipCount);
    }

    /// <summary>
    /// Draw the environment cubemap as a background skybox. Call after the opaque
    /// scene pass with depth-func = LESS_EQUAL — the skybox shader forces depth = 1
    /// so it only appears where no closer geometry has been rendered.
    /// </summary>
    public void RenderSky(in Matrix4 view, in Matrix4 projection)
    {
        if (!IsBaked || s_skyboxShader == null) return;

        // Strip translation — the sky is fixed relative to the camera direction.
        var viewNoTranslate = view;
        viewNoTranslate.M41 = viewNoTranslate.M42 = viewNoTranslate.M43 = 0f;

        GL.DepthFunc(DepthFunction.Lequal);
        s_skyboxShader.Use();
        s_skyboxShader.SetMatrix4("u_view", viewNoTranslate);
        s_skyboxShader.SetMatrix4("u_projection", projection);
        s_skyboxShader.SetFloat("u_intensity", SkyIntensity);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.TextureCubeMap, EnvCubemap);
        s_skyboxShader.SetInt("u_env", 0);

        GL.BindVertexArray(s_cubeVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        GL.BindVertexArray(0);

        GL.DepthFunc(DepthFunction.Less);
    }

    // ============================================================================
    // Bake passes
    // ============================================================================

    private static int BakeEquirectToCube(int equirectTex, int faceSize)
    {
        int cubemap = CreateCubemap(faceSize, PixelInternalFormat.Rgb16f, mipmapped: true);
        int fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.Viewport(0, 0, faceSize, faceSize);

        s_equirectShader!.Use();
        s_equirectShader.SetMatrix4("u_projection", CaptureProjection);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, equirectTex);
        s_equirectShader.SetInt("u_equirect", 0);

        for (int face = 0; face < 6; face++)
        {
            s_equirectShader.SetMatrix4("u_view", CaptureViews[face]);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.TextureCubeMapPositiveX + face, cubemap, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.BindVertexArray(s_cubeVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }

        // Generate mips so the prefilter pass can sample lower-frequency source levels.
        GL.BindTexture(TextureTarget.TextureCubeMap, cubemap);
        GL.GenerateMipmap(GenerateMipmapTarget.TextureCubeMap);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteFramebuffer(fbo);
        return cubemap;
    }

    private static int BakeIrradiance(int envCubemap, int faceSize)
    {
        int cubemap = CreateCubemap(faceSize, PixelInternalFormat.Rgb16f, mipmapped: false);
        int fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.Viewport(0, 0, faceSize, faceSize);

        s_irradianceShader!.Use();
        s_irradianceShader.SetMatrix4("u_projection", CaptureProjection);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.TextureCubeMap, envCubemap);
        s_irradianceShader.SetInt("u_env", 0);

        for (int face = 0; face < 6; face++)
        {
            s_irradianceShader.SetMatrix4("u_view", CaptureViews[face]);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.TextureCubeMapPositiveX + face, cubemap, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.BindVertexArray(s_cubeVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteFramebuffer(fbo);
        return cubemap;
    }

    private static int BakePrefilteredSpecular(int envCubemap, int mip0Size, int mipCount)
    {
        int cubemap = CreateCubemap(mip0Size, PixelInternalFormat.Rgb16f, mipmapped: true);
        GL.BindTexture(TextureTarget.TextureCubeMap, cubemap);
        // Trilinear across mips is required for the runtime shader.
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.GenerateMipmap(GenerateMipmapTarget.TextureCubeMap);

        int fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

        s_prefilterShader!.Use();
        s_prefilterShader.SetMatrix4("u_projection", CaptureProjection);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.TextureCubeMap, envCubemap);
        s_prefilterShader.SetInt("u_env", 0);
        s_prefilterShader.SetFloat("u_envResolution", EnvFaceSize);

        for (int mip = 0; mip < mipCount; mip++)
        {
            int mipSize = mip0Size >> mip;
            GL.Viewport(0, 0, mipSize, mipSize);

            float roughness = mip / (float)(mipCount - 1);
            s_prefilterShader.SetFloat("u_roughness", roughness);

            for (int face = 0; face < 6; face++)
            {
                s_prefilterShader.SetMatrix4("u_view", CaptureViews[face]);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.TextureCubeMapPositiveX + face, cubemap, mip);
                GL.Clear(ClearBufferMask.ColorBufferBit);
                GL.BindVertexArray(s_cubeVao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
            }
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteFramebuffer(fbo);
        return cubemap;
    }

    private static int EnsureBrdfLut()
    {
        if (s_sharedBrdfLut != -1) return s_sharedBrdfLut;

        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rg16f,
            BrdfLutSize, BrdfLutSize, 0, PixelFormat.Rg, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        int fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, tex, 0);

        GL.Viewport(0, 0, BrdfLutSize, BrdfLutSize);
        s_brdfLutShader!.Use();
        GL.BindVertexArray(s_quadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteFramebuffer(fbo);

        s_sharedBrdfLut = tex;
        return tex;
    }

    // ============================================================================
    // Shared GL setup
    // ============================================================================

    /// <summary>Perspective projection with 90° FOV used for all cube-face captures.</summary>
    private static readonly Matrix4 CaptureProjection =
        Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90f), 1f, 0.1f, 10f);

    /// <summary>Six LookAt views (one per cubemap face) used to render into each face.</summary>
    private static readonly Matrix4[] CaptureViews =
    {
        Matrix4.LookAt(Vector3.Zero, new( 1,  0,  0), new(0, -1,  0)), // +X
        Matrix4.LookAt(Vector3.Zero, new(-1,  0,  0), new(0, -1,  0)), // -X
        Matrix4.LookAt(Vector3.Zero, new( 0,  1,  0), new(0,  0,  1)), // +Y
        Matrix4.LookAt(Vector3.Zero, new( 0, -1,  0), new(0,  0, -1)), // -Y
        Matrix4.LookAt(Vector3.Zero, new( 0,  0,  1), new(0, -1,  0)), // +Z
        Matrix4.LookAt(Vector3.Zero, new( 0,  0, -1), new(0, -1,  0)), // -Z
    };

    private static void EnsureSharedResources()
    {
        if (s_cubeVao == -1) CreateCubeMesh();
        if (s_quadVao == -1) s_quadVao = GL.GenVertexArray();
        s_equirectShader   ??= ArcEngine.Engine.Resources.Resources.LoadShader("Assets/Shaders/cubemap_project.vert", "Assets/Shaders/equirect_to_cube.frag");
        s_irradianceShader ??= ArcEngine.Engine.Resources.Resources.LoadShader("Assets/Shaders/cubemap_project.vert", "Assets/Shaders/irradiance_convolve.frag");
        s_prefilterShader  ??= ArcEngine.Engine.Resources.Resources.LoadShader("Assets/Shaders/cubemap_project.vert", "Assets/Shaders/prefilter_specular.frag");
        s_brdfLutShader    ??= ArcEngine.Engine.Resources.Resources.LoadShader("Assets/Shaders/postprocess.vert",     "Assets/Shaders/brdf_lut.frag");
        s_skyboxShader     ??= ArcEngine.Engine.Resources.Resources.LoadShader("Assets/Shaders/skybox.vert",          "Assets/Shaders/skybox.frag");
    }

    private static int CreateCubemap(int faceSize, PixelInternalFormat internalFormat, bool mipmapped)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.TextureCubeMap, tex);
        for (int i = 0; i < 6; i++)
        {
            GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, internalFormat,
                faceSize, faceSize, 0, PixelFormat.Rgb, PixelType.Float, IntPtr.Zero);
        }
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter,
            (int)(mipmapped ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear));
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        if (mipmapped)
            GL.GenerateMipmap(GenerateMipmapTarget.TextureCubeMap);
        return tex;
    }

    private static void CreateCubeMesh()
    {
        // 36 vertices, position-only. Order + winding chosen so the outer faces
        // draw with counter-clockwise winding from outside (glCullFace default).
        float[] verts =
        {
            // back
            -1,-1,-1,   1, 1,-1,   1,-1,-1,
             1, 1,-1,  -1,-1,-1,  -1, 1,-1,
            // front
            -1,-1, 1,   1,-1, 1,   1, 1, 1,
             1, 1, 1,  -1, 1, 1,  -1,-1, 1,
            // left
            -1, 1, 1,  -1, 1,-1,  -1,-1,-1,
            -1,-1,-1,  -1,-1, 1,  -1, 1, 1,
            // right
             1, 1, 1,   1,-1,-1,   1, 1,-1,
             1,-1,-1,   1, 1, 1,   1,-1, 1,
            // bottom
            -1,-1,-1,   1,-1,-1,   1,-1, 1,
             1,-1, 1,  -1,-1, 1,  -1,-1,-1,
            // top
            -1, 1,-1,  -1, 1, 1,   1, 1, 1,
             1, 1, 1,   1, 1,-1,  -1, 1,-1,
        };

        s_cubeVao = GL.GenVertexArray();
        s_cubeVbo = GL.GenBuffer();
        GL.BindVertexArray(s_cubeVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, s_cubeVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    // ============================================================================
    // Cleanup
    // ============================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (EnvCubemap         != -1) { GL.DeleteTexture(EnvCubemap);         EnvCubemap         = -1; }
        if (IrradianceCubemap  != -1) { GL.DeleteTexture(IrradianceCubemap);  IrradianceCubemap  = -1; }
        if (PrefilteredCubemap != -1) { GL.DeleteTexture(PrefilteredCubemap); PrefilteredCubemap = -1; }
        // BrdfLut is shared and static; don't delete it here.
        GC.SuppressFinalize(this);
    }
}
