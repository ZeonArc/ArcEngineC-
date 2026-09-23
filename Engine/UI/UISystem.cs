using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Per-frame driver for every <see cref="Canvas"/> in the scene. The renderer
/// calls <see cref="Render"/> after the tonemap pass so UI ends up on top of
/// the final LDR image; <see cref="UpdateInput"/> runs before Scene.Update so
/// widgets that dispatch actions (buttons, sliders) see input in the same
/// frame it arrives.
/// </summary>
public class UISystem : IDisposable
{
    private readonly UIBatch _batch = new();
    private Shader? _shader;

    /// <summary>The UI element currently under the mouse (updated each frame).</summary>
    public UIElement? Hovered { get; private set; }

    /// <summary>The element the primary mouse button was pressed on. Cleared on release.</summary>
    public UIElement? PressedTarget { get; private set; }

    /// <summary>True while the UI consumed the current mouse position — the 3D scene should ignore that click.</summary>
    public bool MouseCapturedThisFrame { get; private set; }

    public void Init()
    {
        _batch.Init();
        _shader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/ui.vert", "Assets/Shaders/ui.frag");
    }

    /// <summary>
    /// Walk every canvas, route mouse to the topmost element under the cursor,
    /// dispatch hover/press/release/drag callbacks. Called once per frame BEFORE
    /// scene component updates so scripts driven by UI events pick them up.
    /// </summary>
    public void UpdateInput(Scene scene, InputManager input, Vector2i framebufferSize, Vector2 mousePosLogical, Vector2i clientSize)
    {
        MouseCapturedThisFrame = false;
        Hovered = null;

        // Mouse arrives in logical pixels; the canvas laid out in framebuffer pixels.
        // Scale from logical→framebuffer for hit tests on hi-DPI displays.
        Vector2 mouseFb = new(
            mousePosLogical.X * framebufferSize.X / MathF.Max(1, clientSize.X),
            mousePosLogical.Y * framebufferSize.Y / MathF.Max(1, clientSize.Y));

        // Collect + sort canvases by SortOrder (higher wins).
        var canvases = new List<Canvas>();
        foreach (var c in scene.FindComponents<Canvas>()) canvases.Add(c);
        canvases.Sort((a, b) => b.SortOrder.CompareTo(a.SortOrder));

        foreach (var canvas in canvases)
        {
            canvas.Layout(framebufferSize);
            var hit = canvas.HitTest(mouseFb);
            if (hit != null) { Hovered = hit; break; }
        }

        if (Hovered != null)
        {
            MouseCapturedThisFrame = true;
            Hovered.OnHover(mouseFb);
        }

        // Press / release lifecycle.
        bool downNow = input.IsMouseButtonDown(MouseButton.Left);
        bool pressedThisFrame = input.WasMouseButtonPressedThisFrame(MouseButton.Left);
        bool releasedThisFrame = input.WasMouseButtonReleasedThisFrame(MouseButton.Left);

        if (pressedThisFrame && Hovered != null)
        {
            PressedTarget = Hovered;
            PressedTarget.OnPress(mouseFb);
        }
        else if (downNow && PressedTarget != null)
        {
            PressedTarget.OnDrag(mouseFb);
            MouseCapturedThisFrame = true;
        }
        else if (releasedThisFrame && PressedTarget != null)
        {
            bool isClick = PressedTarget == Hovered;
            PressedTarget.OnRelease(mouseFb, isClick);
            PressedTarget = null;
        }
    }

    /// <summary>Draw every canvas into the currently-bound framebuffer.</summary>
    public void Render(Scene scene, Vector2i framebufferSize)
    {
        if (_shader == null) return;

        var canvases = new List<Canvas>();
        foreach (var c in scene.FindComponents<Canvas>()) canvases.Add(c);
        canvases.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));   // low first — later canvases draw on top

        _batch.Begin();
        foreach (var canvas in canvases)
        {
            // Layout may already be up-to-date from UpdateInput, but resolving twice is cheap
            // and covers cases where the Canvas was added after the input pass ran.
            canvas.Layout(framebufferSize);
            canvas.Render(_batch);
        }
        _batch.Flush(_shader, framebufferSize.X, framebufferSize.Y);
    }

    public void Dispose() => _batch.Dispose();
}
