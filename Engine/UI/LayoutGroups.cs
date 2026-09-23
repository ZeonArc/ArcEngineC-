using OpenTK.Mathematics;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Base class for auto-layout groups. During <see cref="UIElement.Layout"/>,
/// resolves its own rect as usual, then bypasses each child's anchor-based
/// resolve to place them along a fixed axis / grid instead.
/// </summary>
public abstract class LayoutGroup : UIElement
{
    /// <summary>Inner padding on each side.</summary>
    public float PaddingLeft, PaddingRight, PaddingTop, PaddingBottom;

    /// <summary>Space between adjacent children.</summary>
    public float Spacing = 4f;

    /// <summary>Content rect after padding.</summary>
    protected UIRect InnerRect => new(
        ResolvedRect.Min.X + PaddingLeft,
        ResolvedRect.Min.Y + PaddingTop,
        ResolvedRect.Width - PaddingLeft - PaddingRight,
        ResolvedRect.Height - PaddingTop - PaddingBottom);
}

/// <summary>
/// Lay children left-to-right at a fixed <see cref="ChildWidth"/> apiece; each
/// child fills the group's inner height.
/// </summary>
public class HorizontalLayoutGroup : LayoutGroup
{
    public float ChildWidth = 100f;

    public override void Layout(in UIRect parentRect)
    {
        // Resolve self first (children handled below).
        ResolvedRect = Rect.Resolve(parentRect);
        var inner = InnerRect;

        float x = inner.Min.X;
        foreach (var child in Children)
        {
            var slot = new UIRect(x, inner.Min.Y, ChildWidth, inner.Height);
            child.ResolvedRect = slot;
            foreach (var grandchild in child.Children) grandchild.Layout(slot);
            x += ChildWidth + Spacing;
        }
    }
}

/// <summary>Lay children top-to-bottom at a fixed <see cref="ChildHeight"/>.</summary>
public class VerticalLayoutGroup : LayoutGroup
{
    public float ChildHeight = 32f;

    public override void Layout(in UIRect parentRect)
    {
        ResolvedRect = Rect.Resolve(parentRect);
        var inner = InnerRect;

        float y = inner.Min.Y;
        foreach (var child in Children)
        {
            var slot = new UIRect(inner.Min.X, y, inner.Width, ChildHeight);
            child.ResolvedRect = slot;
            foreach (var grandchild in child.Children) grandchild.Layout(slot);
            y += ChildHeight + Spacing;
        }
    }
}

/// <summary>Lay children into a fixed-size grid, row-major.</summary>
public class GridLayoutGroup : LayoutGroup
{
    public Vector2 CellSize = new(64f, 64f);
    public int Columns = 4;

    public override void Layout(in UIRect parentRect)
    {
        ResolvedRect = Rect.Resolve(parentRect);
        var inner = InnerRect;

        for (int i = 0; i < Children.Count; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            float x = inner.Min.X + col * (CellSize.X + Spacing);
            float y = inner.Min.Y + row * (CellSize.Y + Spacing);
            var slot = new UIRect(x, y, CellSize.X, CellSize.Y);
            Children[i].ResolvedRect = slot;
            foreach (var grandchild in Children[i].Children) grandchild.Layout(slot);
        }
    }
}
