using OpenTK.Mathematics;

using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Solid-colored rect (<see cref="Panel"/>) or a texture-modulated rect.
/// Same widget class handles both — leaving <see cref="Texture"/> null gives
/// you a filled color quad.
/// </summary>
public class Image : UIElement
{
    /// <summary>Tint. For colored panels this IS the color; for textured images it's a multiplier.</summary>
    public Vector4 Color = Vector4.One;

    /// <summary>Optional texture. Sampled with (0,0) at top-left, matching the UI coord space.</summary>
    public Texture? Texture;

    public override void Draw(UIBatch batch)
    {
        if (Texture != null) batch.QuadTextured(ResolvedRect, Texture.Handle, Color);
        else                 batch.QuadColor(ResolvedRect, Color);
    }
}

/// <summary>Simple colored background panel. Alias for a texture-less <see cref="Image"/>.</summary>
public class Panel : Image { }
