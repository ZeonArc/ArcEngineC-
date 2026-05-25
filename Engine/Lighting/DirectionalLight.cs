using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Lighting;

/// <summary>
/// Infinitely distant light (e.g. the sun). Has a direction and color but no position.
/// <see cref="Direction"/> is the direction the light is travelling; the shader negates it
/// internally to get the "to-light" vector used for diffuse/specular calculations.
///
/// When <see cref="CastsShadows"/> is true the renderer runs a depth-only pre-pass each
/// frame using the FBO + depth texture allocated lazily by <see cref="EnsureShadowResources"/>.
/// </summary>
public class DirectionalLight : Light
{
    /// <summary>The direction the light is travelling. Convention: pointing away from the sun.</summary>
    public Vector3 Direction = -Vector3.UnitY;

    /// <summary>Whether this light contributes a shadow map. False by default.</summary>
    public bool CastsShadows = false;

    /// <summary>Edge length of the shadow map (square). 2048 is the recommended default for the demo scene.</summary>
    public int ShadowMapSize = 2048;

    // Shadow GPU resources. -1 sentinel = "not allocated yet".
    internal int ShadowMapFbo = -1;
    internal int ShadowMapTexture = -1;

    /// <summary>Light-space matrix (view * ortho). Recomputed each frame by the renderer.</summary>
    internal Matrix4 LightSpaceMatrix = Matrix4.Identity;

    /// <summary>
    /// Allocate the shadow FBO + depth texture if not already done. Idempotent.
    /// Called by the renderer just before the first shadow pass for this light.
    /// </summary>
    internal void EnsureShadowResources()
    {
        if (ShadowMapFbo != -1) return;

        // Depth texture.
        ShadowMapTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, ShadowMapTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0,
            PixelInternalFormat.DepthComponent24,
            ShadowMapSize, ShadowMapSize, 0,
            PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);

        // Linear filtering enables hardware bilinear interpolation inside PCF.
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        // Outside the shadow frustum: sample depth = 1.0 (= "no shadow").
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        float[] borderColor = { 1f, 1f, 1f, 1f };
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, borderColor);

        // FBO with depth attachment, no color attachments.
        ShadowMapFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, ShadowMapFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, ShadowMapTexture, 0);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            Console.WriteLine($"[DirectionalLight] Shadow FBO incomplete: {status}");
        }

        // Restore default framebuffer.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public override void OnDestroy()
    {
        if (ShadowMapTexture != -1)
        {
            GL.DeleteTexture(ShadowMapTexture);
            ShadowMapTexture = -1;
        }
        if (ShadowMapFbo != -1)
        {
            GL.DeleteFramebuffer(ShadowMapFbo);
            ShadowMapFbo = -1;
        }
    }
}
