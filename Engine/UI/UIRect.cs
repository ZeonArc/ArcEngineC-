using OpenTK.Mathematics;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Axis-aligned pixel-space rectangle. Origin at top-left, +Y down — matches
/// the Canvas' orthographic projection.
/// </summary>
public struct UIRect
{
    public Vector2 Min;
    public Vector2 Max;

    public float X => Min.X;
    public float Y => Min.Y;
    public float Width => Max.X - Min.X;
    public float Height => Max.Y - Min.Y;
    public Vector2 Center => (Min + Max) * 0.5f;

    public UIRect(float x, float y, float width, float height)
    {
        Min = new Vector2(x, y);
        Max = new Vector2(x + width, y + height);
    }

    public UIRect(Vector2 min, Vector2 max) { Min = min; Max = max; }

    public bool Contains(Vector2 p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;
}

/// <summary>
/// Anchor-driven rect used by <see cref="UIElement"/>. Anchors are normalized
/// coordinates on the parent rect ([0..1]); offsets are pixel deltas applied
/// after anchoring. This mirrors the Unity/UGUI convention:
/// <list type="bullet">
///   <item>Fill the parent: anchorMin=(0,0), anchorMax=(1,1), offsets=0.</item>
///   <item>Fixed 200×80 at top-left: anchor min=max=(0,0), offsetMin=(10,10), offsetMax=(210,90).</item>
///   <item>Centered anchor: anchor min=max=(0.5, 0.5), offsetMin=(-100,-40), offsetMax=(100,40).</item>
/// </list>
/// </summary>
public struct UIAnchoredRect
{
    public Vector2 AnchorMin;
    public Vector2 AnchorMax;
    public Vector2 OffsetMin;
    public Vector2 OffsetMax;

    /// <summary>Resolve into an absolute pixel rect given a parent rect.</summary>
    public UIRect Resolve(in UIRect parent)
    {
        Vector2 aMin = parent.Min + new Vector2(parent.Width * AnchorMin.X, parent.Height * AnchorMin.Y) + OffsetMin;
        Vector2 aMax = parent.Min + new Vector2(parent.Width * AnchorMax.X, parent.Height * AnchorMax.Y) + OffsetMax;
        return new UIRect(aMin, aMax);
    }

    // ---- Common presets -----------------------------------------------------

    public static UIAnchoredRect Fill => new()
    {
        AnchorMin = Vector2.Zero,
        AnchorMax = Vector2.One,
    };

    public static UIAnchoredRect TopLeft(float x, float y, float w, float h) => new()
    {
        AnchorMin = Vector2.Zero,
        AnchorMax = Vector2.Zero,
        OffsetMin = new Vector2(x, y),
        OffsetMax = new Vector2(x + w, y + h),
    };

    public static UIAnchoredRect Centered(float w, float h) => new()
    {
        AnchorMin = new Vector2(0.5f, 0.5f),
        AnchorMax = new Vector2(0.5f, 0.5f),
        OffsetMin = new Vector2(-w * 0.5f, -h * 0.5f),
        OffsetMax = new Vector2( w * 0.5f,  h * 0.5f),
    };
}
