using OpenTK.Mathematics;

namespace ArcEngine.Engine.UI;

/// <summary>
/// String rendered with a <see cref="BitmapFont"/>. Draws one glyph quad per
/// character starting at the widget's top-left, wrapping at newlines.
///
/// Font sizing: each glyph is rendered at (<see cref="GlyphSize"/>) pixels
/// (X = width, Y = height). Aspect is fixed by the atlas — glyphs from a
/// 16×16 grid all draw as tall as they are wide, regardless of what's baked.
/// </summary>
public class Text : UIElement
{
    public BitmapFont? Font;
    public string Value = "";
    public Vector4 Color = Vector4.One;
    public Vector2 GlyphSize = new(10f, 16f);

    /// <summary>Extra spacing added between consecutive glyphs on the same line.</summary>
    public float LetterSpacing = 0f;

    /// <summary>Line advance = GlyphSize.Y + LineSpacing.</summary>
    public float LineSpacing = 2f;

    public override void Draw(UIBatch batch)
    {
        if (Font == null || string.IsNullOrEmpty(Value)) return;

        float x = ResolvedRect.Min.X;
        float y = ResolvedRect.Min.Y;

        foreach (var ch in Value)
        {
            if (ch == '\n')
            {
                x = ResolvedRect.Min.X;
                y += GlyphSize.Y + LineSpacing;
                continue;
            }
            var uv = Font.GlyphUV(ch);
            var rect = new UIRect(x, y, GlyphSize.X, GlyphSize.Y);
            batch.QuadTexturedUV(rect, Font.Texture.Handle, uv, Color);
            x += GlyphSize.X + LetterSpacing;
        }
    }
}
