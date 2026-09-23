using OpenTK.Mathematics;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Base class for every widget in a <see cref="Canvas"/>. Owns its anchored
/// rect + a parent-child hierarchy, delegates drawing to subclasses via
/// <see cref="Draw"/>, and pipes mouse events through <see cref="OnHover"/>,
/// <see cref="OnPress"/>, <see cref="OnRelease"/>.
///
/// The tree is walked top-down each frame so child rects can layout against
/// the freshly-resolved parent rect. <see cref="ResolvedRect"/> is the last
/// per-frame result — layout groups mutate it after the base resolve, which
/// is why children ask their PARENT for the current rect (not themselves).
/// </summary>
public class UIElement
{
    public string Name = "UIElement";

    public UIAnchoredRect Rect = UIAnchoredRect.Fill;

    /// <summary>Draw + input-hit toggle. Skips this element and its children when false.</summary>
    public bool Visible = true;

    /// <summary>Non-visible elements still exist but don't draw or receive input.</summary>
    public bool Interactive = true;

    public UIElement? Parent { get; private set; }
    public List<UIElement> Children { get; } = new();

    /// <summary>Last frame's resolved pixel rect. Populated during the layout pass.</summary>
    public UIRect ResolvedRect { get; internal set; }

    public void Add(UIElement child)
    {
        if (child.Parent != null) child.Parent.Children.Remove(child);
        child.Parent = this;
        Children.Add(child);
    }

    public void Remove(UIElement child)
    {
        if (child.Parent != this) return;
        Children.Remove(child);
        child.Parent = null;
    }

    /// <summary>
    /// Compute this element's resolved rect against its parent. Layout groups
    /// override this to redistribute their children's rects.
    /// </summary>
    public virtual void Layout(in UIRect parentRect)
    {
        ResolvedRect = Rect.Resolve(parentRect);
        foreach (var child in Children) child.Layout(ResolvedRect);
    }

    /// <summary>Emit draw calls for this element (batch takes them). Base does nothing — subclasses override.</summary>
    public virtual void Draw(UIBatch batch) { }

    /// <summary>Called each frame while the mouse is inside this element's rect.</summary>
    public virtual void OnHover(Vector2 mousePos) { }

    /// <summary>Called on the frame the primary mouse button is pressed while hovering.</summary>
    public virtual void OnPress(Vector2 mousePos) { }

    /// <summary>Called on the frame the primary mouse button is released after having been pressed on this element.</summary>
    public virtual void OnRelease(Vector2 mousePos, bool wasClick) { }

    /// <summary>Called each frame the mouse button remains held after an initial press.</summary>
    public virtual void OnDrag(Vector2 mousePos) { }
}
