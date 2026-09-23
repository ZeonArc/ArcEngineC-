using OpenTK.Mathematics;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Horizontal drag-to-set slider. <see cref="Value"/> in [<see cref="Min"/>..<see cref="Max"/>].
/// Fires <see cref="OnValueChanged"/> whenever the drag moves the value.
///
/// Rendering: a full-width "track" rect, then a shorter "fill" rect scaled
/// to the current normalized value, then a fixed-size "thumb" positioned at
/// the fill's leading edge.
/// </summary>
public class Slider : UIElement
{
    public float Min = 0f;
    public float Max = 1f;
    public float Value = 0f;

    public Vector4 TrackColor = new(0.15f, 0.16f, 0.20f, 1f);
    public Vector4 FillColor  = new(0.30f, 0.55f, 0.95f, 1f);
    public Vector4 ThumbColor = new(0.90f, 0.90f, 0.95f, 1f);

    public float ThumbWidth = 8f;

    /// <summary>Fired on any user-initiated value change (drag).</summary>
    public Action<float>? OnValueChanged;

    public float Normalized
    {
        get => Max - Min > 0f ? (Value - Min) / (Max - Min) : 0f;
        set
        {
            float clamped = MathHelper.Clamp(value, 0f, 1f);
            var newValue = Min + clamped * (Max - Min);
            if (newValue == Value) return;
            Value = newValue;
            OnValueChanged?.Invoke(Value);
        }
    }

    public override void OnPress(Vector2 mousePos) => UpdateValueFromMouse(mousePos);
    public override void OnDrag(Vector2 mousePos)  => UpdateValueFromMouse(mousePos);

    private void UpdateValueFromMouse(Vector2 mousePos)
    {
        float span = ResolvedRect.Width;
        if (span <= 0f) return;
        float t = (mousePos.X - ResolvedRect.Min.X) / span;
        Normalized = t;
    }

    public override void Draw(UIBatch batch)
    {
        batch.QuadColor(ResolvedRect, TrackColor);

        float t = Normalized;
        var fillRect = new UIRect(
            ResolvedRect.Min.X,
            ResolvedRect.Min.Y,
            ResolvedRect.Width * t,
            ResolvedRect.Height);
        batch.QuadColor(fillRect, FillColor);

        float thumbX = ResolvedRect.Min.X + ResolvedRect.Width * t - ThumbWidth * 0.5f;
        var thumbRect = new UIRect(thumbX, ResolvedRect.Min.Y, ThumbWidth, ResolvedRect.Height);
        batch.QuadColor(thumbRect, ThumbColor);
    }
}
