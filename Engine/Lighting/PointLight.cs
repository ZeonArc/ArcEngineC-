using OpenTK.Graphics.OpenGL4;

namespace ArcEngine.Engine.Lighting;

/// <summary>
/// Omnidirectional point light with standard quadratic falloff.
/// Attenuation = 1 / (Constant + Linear * d + Quadratic * d²).
/// Position is sourced from the sibling <see cref="Engine.Core.Transform"/>.
///
/// Shadow support: set <see cref="CastsShadows"/> to true and the renderer
/// will allocate a <see cref="ShadowMapCube"/> (six R16F faces) and re-render
/// the scene into each face every frame. v1 supports one shadow-casting point
/// light per scene; additional shadow-casting lights are ignored for now.
/// </summary>
public class PointLight : Light
{
    public float Constant = 1f;
    public float Linear = 0.09f;
    public float Quadratic = 0.032f;

    /// <summary>Whether this point light casts a shadow cubemap.</summary>
    public bool CastsShadows = false;

    /// <summary>Far plane used for the shadow cube's 90° perspective — world units at which occlusion cuts off.</summary>
    public float ShadowFarPlane = 25f;

    /// <summary>Face resolution of the shadow cubemap. 512² is the balance point for interactive fill rates.</summary>
    public int ShadowMapSize = 512;

    // -1 sentinel = "not allocated yet".
    internal int ShadowMapCube = -1;
    internal readonly int[] ShadowMapFbos = new int[6];
    internal int ShadowMapDepthRbo = -1;

    public PointLight()
    {
        for (int i = 0; i < 6; i++) ShadowMapFbos[i] = -1;
    }

    /// <summary>Lazily allocate the shadow cubemap + one FBO per face. Idempotent.</summary>
    internal void EnsureShadowResources()
    {
        if (ShadowMapCube != -1) return;

        ShadowMapCube = GL.GenTexture();
        GL.BindTexture(TextureTarget.TextureCubeMap, ShadowMapCube);
        for (int i = 0; i < 6; i++)
        {
            GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, PixelInternalFormat.R16f,
                ShadowMapSize, ShadowMapSize, 0, PixelFormat.Red, PixelType.Float, IntPtr.Zero);
        }
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

        // Shared depth buffer — reused across all six FBOs so we don't burn 6x memory.
        ShadowMapDepthRbo = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, ShadowMapDepthRbo);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24,
            ShadowMapSize, ShadowMapSize);

        for (int face = 0; face < 6; face++)
        {
            ShadowMapFbos[face] = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, ShadowMapFbos[face]);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.TextureCubeMapPositiveX + face, ShadowMapCube, 0);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer, ShadowMapDepthRbo);
            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
                Console.WriteLine($"[PointLight] Shadow face {face} FBO incomplete: {status}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public override void OnDestroy()
    {
        for (int i = 0; i < 6; i++)
        {
            if (ShadowMapFbos[i] != -1) { GL.DeleteFramebuffer(ShadowMapFbos[i]); ShadowMapFbos[i] = -1; }
        }
        if (ShadowMapDepthRbo != -1) { GL.DeleteRenderbuffer(ShadowMapDepthRbo); ShadowMapDepthRbo = -1; }
        if (ShadowMapCube != -1) { GL.DeleteTexture(ShadowMapCube); ShadowMapCube = -1; }
    }
}
