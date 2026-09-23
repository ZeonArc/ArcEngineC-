using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Root of a UI tree. Attaches to a scene GameObject; the renderer walks every
/// Canvas in the scene each frame, calls <see cref="Layout"/> to resolve the
/// element rects against the framebuffer, then <see cref="Render"/> to emit
/// quads through a shared <see cref="UIBatch"/>.
///
/// v1 is screen-space overlay only — world-space canvases (billboarded onto 3D
/// surfaces) are a follow-up; the plumbing already treats each Canvas as an
/// independent root, so adding a Space enum + a per-canvas projection is small.
/// </summary>
public class Canvas : Component
{
    /// <summary>Rendering order. Higher values draw on top of lower.</summary>
    public int SortOrder = 0;

    /// <summary>Root element — parent of everything the canvas owns.</summary>
    public UIElement Root { get; } = new UIElement { Name = "CanvasRoot", Rect = UIAnchoredRect.Fill };

    /// <summary>The current framebuffer size the canvas laid out against.</summary>
    public Vector2i LastFramebufferSize { get; private set; }

    /// <summary>Resolve every element rect for the current framebuffer size.</summary>
    public void Layout(Vector2i framebufferSize)
    {
        LastFramebufferSize = framebufferSize;
        var screenRect = new UIRect(0, 0, framebufferSize.X, framebufferSize.Y);
        Root.Layout(screenRect);
    }

    /// <summary>Traverse the tree and emit quads into the shared batch.</summary>
    public void Render(UIBatch batch)
    {
        RenderRecursive(Root, batch);
    }

    private static void RenderRecursive(UIElement e, UIBatch batch)
    {
        if (!e.Visible) return;
        e.Draw(batch);
        foreach (var child in e.Children) RenderRecursive(child, batch);
    }

    // ============================================================================
    // Input hit test
    // ============================================================================

    /// <summary>
    /// Depth-first walk returning the topmost interactive element under the
    /// given mouse position, or null if no element covers it.
    /// </summary>
    public UIElement? HitTest(Vector2 mousePos)
    {
        return HitTestRecursive(Root, mousePos);
    }

    private static UIElement? HitTestRecursive(UIElement e, Vector2 mousePos)
    {
        if (!e.Visible || !e.Interactive) return null;
        // Children draw last → they're "on top", check them first.
        for (int i = e.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTestRecursive(e.Children[i], mousePos);
            if (hit != null) return hit;
        }
        // Skip the CanvasRoot itself (Parent == null) — it fills the screen and
        // would swallow every click.
        if (e.Parent != null && e.ResolvedRect.Contains(mousePos)) return e;
        return null;
    }
}
