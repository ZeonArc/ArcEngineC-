using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Lighting;

/// <summary>
/// Infinitely distant light (e.g. the sun). Has a direction and color but no position.
/// <see cref="Direction"/> is the direction the light is travelling; the shader negates it
/// internally to get the "to-light" vector used for diffuse/specular calculations.
///
/// When <see cref="CastsShadows"/> is true the renderer runs a cascaded-shadow-map
/// depth pass each frame using an array of shadow-map layers (one per cascade) allocated
/// lazily by <see cref="EnsureShadowResources"/>. Each cascade fits its own portion of
/// the camera's view frustum, so distant geometry gets low-res coverage while nearby
/// geometry gets high-res detail without paying for both everywhere.
/// </summary>
public class DirectionalLight : Light
{
    /// <summary>Number of shadow-map cascades. Matches CASCADES in basic.frag.</summary>
    public const int CascadeCount = 3;

    /// <summary>The direction the light is travelling. Convention: pointing away from the sun.</summary>
    public Vector3 Direction = -Vector3.UnitY;

    /// <summary>Whether this light contributes shadow maps. False by default.</summary>
    public bool CastsShadows = false;

    /// <summary>Edge length of each cascade shadow map layer.</summary>
    public int ShadowMapSize = 2048;

    // Shadow GPU resources. -1 sentinel = "not allocated yet".
    internal int ShadowMapArray = -1;
    internal readonly int[] ShadowMapFbos = new int[CascadeCount];

    /// <summary>Per-cascade light-space matrix (view * ortho). Recomputed each frame.</summary>
    internal readonly Matrix4[] LightSpaceMatrices = new Matrix4[CascadeCount];

    /// <summary>
    /// View-space depth (positive, camera → forward) marking the far end of each cascade.
    /// Recomputed each frame from the camera's near/far.
    /// </summary>
    internal readonly float[] CascadeSplitsViewZ = new float[CascadeCount];

    public DirectionalLight()
    {
        for (int i = 0; i < CascadeCount; i++) ShadowMapFbos[i] = -1;
    }

    /// <summary>
    /// Allocate the shadow-map texture array + one FBO per cascade layer if not already
    /// done. Idempotent. Called by the renderer just before the first shadow pass.
    /// </summary>
    internal void EnsureShadowResources()
    {
        if (ShadowMapArray != -1) return;

        // One GL_TEXTURE_2D_ARRAY with CascadeCount layers of DEPTH_COMPONENT24.
        ShadowMapArray = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DArray, ShadowMapArray);
        GL.TexImage3D(TextureTarget.Texture2DArray, 0,
            PixelInternalFormat.DepthComponent24,
            ShadowMapSize, ShadowMapSize, CascadeCount, 0,
            PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);

        // Linear filtering enables hardware bilinear PCF.
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        float[] borderColor = { 1f, 1f, 1f, 1f };
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBorderColor, borderColor);

        // One FBO per cascade, each attaches a single array layer.
        for (int i = 0; i < CascadeCount; i++)
        {
            ShadowMapFbos[i] = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, ShadowMapFbos[i]);
            GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment, ShadowMapArray, 0, i);
            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);

            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
                Console.WriteLine($"[DirectionalLight] Cascade {i} FBO incomplete: {status}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public override void OnDestroy()
    {
        for (int i = 0; i < CascadeCount; i++)
        {
            if (ShadowMapFbos[i] != -1)
            {
                GL.DeleteFramebuffer(ShadowMapFbos[i]);
                ShadowMapFbos[i] = -1;
            }
        }
        if (ShadowMapArray != -1)
        {
            GL.DeleteTexture(ShadowMapArray);
            ShadowMapArray = -1;
        }
    }
}
