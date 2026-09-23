using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// PBR metallic-roughness material. Texture units assigned by the engine:
/// <list type="bullet">
///   <item>Unit 0 — BaseColor (sRGB)</item>
///   <item>Unit 1 — MetallicRoughness (linear; G=rough, B=metal)</item>
///   <item>Unit 2 — Normal (linear, tangent space)</item>
///   <item>Unit 3 — Occlusion (linear; R=AO)</item>
///   <item>Unit 5 — Shadow map (bound by Renderer)</item>
/// </list>
/// Unit 4 is reserved (was specular under Blinn-Phong; freed up for future use).
/// Unset textures fall back to a 1×1 white pixel so the per-channel scalar uniform
/// drives the look entirely.
/// </summary>
public class Material
{
    public Shader Shader;

    /// <summary>Base color (a.k.a. albedo). Multiplied with the BaseColor texture sample.</summary>
    public Vector3 Color = Vector3.One;

    /// <summary>0 = dielectric, 1 = metal.</summary>
    public float Metallic = 0f;

    /// <summary>0 = mirror, 1 = perfectly rough.</summary>
    public float Roughness = 0.7f;

    /// <summary>Scalar AO when no occlusion map is bound (1 = no occlusion).</summary>
    public float AmbientOcclusion = 1f;

    /// <summary>Strength of normal-map perturbation (0 disables, 1 = full effect).</summary>
    public float NormalStrength = 1f;

    /// <summary>BaseColor texture (sRGB). Bound to texture unit 0.</summary>
    public Texture? Texture;

    /// <summary>Metallic-Roughness combined map (linear). Bound to texture unit 1.</summary>
    public Texture? MetallicRoughnessTexture;

    /// <summary>Normal map (linear, tangent space). Bound to texture unit 2.</summary>
    public Texture? NormalTexture;

    /// <summary>Occlusion map (linear). Bound to texture unit 3.</summary>
    public Texture? OcclusionTexture;

    /// <summary>Legacy Blinn-Phong slot; unused in PBR shader. Kept so old code compiles.</summary>
    public Texture? SpecularTexture;

    /// <summary>Legacy Blinn-Phong shininess; unused in PBR shader.</summary>
    public float Shininess = 32f;

    private static int s_whiteTextureHandle = -1;

    public Material(Shader shader)
    {
        Shader = shader;
    }

    public virtual void Apply()
    {
        Shader.Use();

        Shader.SetVector3("u_baseColor", Color);
        Shader.SetFloat("u_metallic", Metallic);
        Shader.SetFloat("u_roughness", Roughness);
        Shader.SetFloat("u_ao", AmbientOcclusion);
        Shader.SetFloat("u_normalStrength", NormalStrength);

        // Tell the shader whether each map is real (so the scalar fallback applies otherwise).
        Shader.SetInt("u_hasBaseColorMap", Texture != null ? 1 : 0);
        Shader.SetInt("u_hasMRMap", MetallicRoughnessTexture != null ? 1 : 0);
        Shader.SetInt("u_hasNormalMap", NormalTexture != null ? 1 : 0);
        Shader.SetInt("u_hasOcclusionMap", OcclusionTexture != null ? 1 : 0);

        BindOrFallback(0, Texture, "u_baseColorMap");
        BindOrFallback(1, MetallicRoughnessTexture, "u_metallicRoughnessMap");
        BindOrFallback(2, NormalTexture, "u_normalMap");
        BindOrFallback(3, OcclusionTexture, "u_occlusionMap");

        // -------- Backward-compat (Blinn-Phong) shader uniforms --------
        // The legacy basic.frag (pre-PBR) uses these names. Once Sprint 5c's PBR
        // shader replaces it, these are simply ignored (Shader.SetX silently no-ops
        // on missing uniforms).
        Shader.SetVector3("materialColor", Color);
        Shader.SetFloat("shininess", Shininess);
        Shader.SetInt("texture0", 0);
        Shader.SetInt("specularMap", 1);
    }

    protected void BindOrFallback(int unit, Texture? tex, string samplerName)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        if (tex != null) tex.Use();
        else GL.BindTexture(TextureTarget.Texture2D, GetOrCreateWhiteTexture());
        Shader.SetInt(samplerName, unit);
    }

    /// <summary>1×1 white texture used whenever a slot has no real texture bound.</summary>
    private static int GetOrCreateWhiteTexture()
    {
        if (s_whiteTextureHandle != -1) return s_whiteTextureHandle;

        s_whiteTextureHandle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, s_whiteTextureHandle);

        byte[] whitePixel = { 255, 255, 255, 255 };
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, whitePixel);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        return s_whiteTextureHandle;
    }
}
