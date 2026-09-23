using OpenTK.Mathematics;

using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Minimal fixed-grid bitmap font. The atlas texture is divided into
/// <see cref="Columns"/> × <see cref="Rows"/> cells of identical size; glyphs
/// are indexed by <c>char - <see cref="FirstChar"/></c>. Every glyph
/// advances the same width — no kerning, no variable pitch.
///
/// Good enough for HUDs and debug overlays. For proper typography (kerning,
/// crisp scaling, kerned Chinese/Japanese/Korean text) the roadmap flags an
/// SDF text upgrade as a follow-up.
/// </summary>
public class BitmapFont
{
    public Texture Texture { get; }
    public int Columns { get; }
    public int Rows { get; }
    public char FirstChar { get; }

    /// <summary>Cell width in atlas UV space [0..1].</summary>
    public float CellU => 1f / Columns;
    /// <summary>Cell height in atlas UV space [0..1].</summary>
    public float CellV => 1f / Rows;

    public BitmapFont(Texture texture, int columns, int rows, char firstChar = ' ')
    {
        Texture = texture;
        Columns = columns;
        Rows = rows;
        FirstChar = firstChar;
    }

    /// <summary>Compute UV rect for a single glyph, or the full-white "space" cell for anything out of range.</summary>
    public UIRect GlyphUV(char c)
    {
        int index = c - FirstChar;
        if (index < 0 || index >= Columns * Rows) return new UIRect(0f, 0f, 0f, 0f);
        int col = index % Columns;
        int row = index / Columns;
        float u0 = col * CellU;
        float v0 = row * CellV;
        return new UIRect(u0, v0, CellU, CellV);
    }
}
