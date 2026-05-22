using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Rendering;

public class Material
{
    public Shader Shader;

    /// <summary>Diffuse / albedo texture. Bound to texture unit 0.</summary>
    public Texture? Texture;

    /// <summary>Specular map. Bound to texture unit 1. Null → 1×1 white fallback.</summary>
    public Texture? SpecularTexture;

    /// <summary>Diffuse tint (multiplied with the diffuse texture sample).</summary>
    public Vector3 Color = new Vector3(1f, 1f, 1f);

    /// <summary>Blinn-Phong specular exponent. Higher = tighter highlight. Typical range 8–256.</summary>
    public float Shininess = 32f;

    private static int s_whiteTextureHandle = -1;

    public Material(Shader shader)
    {
        Shader = shader;
    }

    public void Apply()
    {
        Shader.Use();
        Shader.SetVector3("materialColor", Color);
        Shader.SetFloat("shininess", Shininess);

        // Diffuse → unit 0
        GL.ActiveTexture(TextureUnit.Texture0);
        if (Texture != null)
            Texture.Use();
        else
            GL.BindTexture(TextureTarget.Texture2D, GetOrCreateWhiteTexture());
        Shader.SetInt("texture0", 0);

        // Specular → unit 1
        GL.ActiveTexture(TextureUnit.Texture1);
        if (SpecularTexture != null)
            SpecularTexture.Use();
        else
            GL.BindTexture(TextureTarget.Texture2D, GetOrCreateWhiteTexture());
        Shader.SetInt("specularMap", 1);
    }

    /// <summary>
    /// Lazily-created 1×1 white fallback shared by both samplers.
    /// White lets the corresponding term (diffuse or specular) be driven entirely
    /// by the material color / shininess uniforms when no real texture is set.
    /// </summary>
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
