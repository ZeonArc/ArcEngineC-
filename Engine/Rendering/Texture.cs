using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace ArcEngine.Engine.Rendering;

public class Texture
{
    public int Handle;

    /// <summary>Load a texture from a file path on disk.</summary>
    public Texture(string path)
    {
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        UploadFromImage(image);
    }

    /// <summary>Load a texture from raw image bytes (e.g. GLB-embedded PNG/JPEG).</summary>
    public Texture(byte[] imageBytes)
    {
        var image = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);
        UploadFromImage(image);
    }

    private void UploadFromImage(ImageResult image)
    {
        Handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, Handle);

        GL.TexImage2D(TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            image.Width,
            image.Height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            image.Data);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
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
