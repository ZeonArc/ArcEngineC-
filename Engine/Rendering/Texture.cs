using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace ArcEngine.Engine.Rendering;

public class Texture
{
    public int Handle;

    /// <summary>
    /// Load a texture from a file path on disk.
    /// </summary>
    /// <param name="path">File path to a .png/.jpg/etc. (anything stb can decode).</param>
    /// <param name="sRGB">
    /// True for color textures (base color / albedo) — uploaded as <c>SrgbAlpha8</c> so
    /// shader samples are auto-converted to linear space. False for data textures
    /// (normal, MR, AO) which must stay linear.
    /// </param>
    public Texture(string path, bool sRGB = false)
    {
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        UploadFromImage(image, sRGB);
    }

    /// <summary>
    /// Load a texture from raw image bytes (e.g. GLB-embedded PNG/JPEG).
    /// Same sRGB rule as the path constructor.
    /// </summary>
    public Texture(byte[] imageBytes, bool sRGB = false)
    {
        var image = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);
        UploadFromImage(image, sRGB);
    }

    private void UploadFromImage(ImageResult image, bool sRGB)
    {
        Handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, Handle);

        var internalFormat = sRGB ? PixelInternalFormat.SrgbAlpha : PixelInternalFormat.Rgba;

        GL.TexImage2D(TextureTarget.Texture2D,
            0,
            internalFormat,
            image.Width,
            image.Height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            image.Data);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
    }

    public void Use()
    {
        GL.BindTexture(TextureTarget.Texture2D, Handle);
    }
}
