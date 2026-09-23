using OpenTK.Mathematics;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Colored/textured rect that fires <see cref="OnClick"/> when the user
/// presses AND releases the primary mouse button while over it. Hover +
/// pressed states swap the background color for visual feedback.
/// </summary>
public class Button : UIElement
{
    public Vector4 NormalColor  = new(0.20f, 0.22f, 0.28f, 1f);
    public Vector4 HoverColor   = new(0.30f, 0.35f, 0.45f, 1f);
    public Vector4 PressedColor = new(0.10f, 0.12f, 0.18f, 1f);

    /// <summary>Fired when the button transitions from pressed → released while still hovered.</summary>
    public Action? OnClick;

    private bool _hovered;
    private bool _pressed;

    public override void OnHover(Vector2 mousePos) => _hovered = true;

    public override void OnPress(Vector2 mousePos)
    {
        _pressed = true;
    }

    public override void OnRelease(Vector2 mousePos, bool wasClick)
    {
        _pressed = false;
        if (wasClick) OnClick?.Invoke();
    }

    public override void Layout(in UIRect parentRect)
    {
        _hovered = false;    // reset — set again by OnHover if applicable
        base.Layout(parentRect);
    }

    public override void Draw(UIBatch batch)
    {
        var color = _pressed ? PressedColor : (_hovered ? HoverColor : NormalColor);
        batch.QuadColor(ResolvedRect, color);
    }
}
