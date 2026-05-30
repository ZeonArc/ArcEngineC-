using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace ArcEngine.Engine.Input;

/// <summary>
/// Per-frame input snapshot. Wraps OpenTK's <see cref="KeyboardState"/> /
/// <see cref="MouseState"/>, tracks mouse delta + edge-detect events, and exposes
/// cursor-state mutations for things like FPS controllers that grab the cursor while
/// the right mouse button is held.
///
/// Usage:
///   _input.AttachWindow(window);                           // once at startup
///   _input.Update(KeyboardState, MouseState);              // once per update tick
///   _input.IsKeyDown(Keys.W) / IsMouseButtonDown(MouseButton.Right);
///   var d = _input.MouseDelta;
/// </summary>
public class InputManager
{
    private KeyboardState? _keyboard;
    private MouseState? _mouse;
    private GameWindow? _window;

    private Vector2 _lastMousePos;
    private Vector2 _mouseDelta;
    private bool _firstMove = true;

    private readonly HashSet<Keys> _keyDownLastFrame = new();
    private readonly HashSet<Keys> _keyDownThisFrame = new();

    // Mouse-button state (5 buttons cover the common ones).
    private const int ButtonCount = 8;
    private readonly bool[] _mouseDownLastFrame = new bool[ButtonCount];
    private readonly bool[] _mouseDownThisFrame = new bool[ButtonCount];

    /// <summary>Mouse movement (in pixels) since the previous Update call. Zero on the first frame.</summary>
    public Vector2 MouseDelta => _mouseDelta;

    /// <summary>True until the very first mouse position has been recorded.</summary>
    public bool IsMouseFirstMove => _firstMove;

    /// <summary>Attach the GameWindow so cursor state can be mutated from input-driven scripts.</summary>
    public void AttachWindow(GameWindow window) => _window = window;

    /// <summary>Update the snapshot. Call once at the start of each update tick.</summary>
    public void Update(KeyboardState keyboard, MouseState mouse)
    {
        _keyboard = keyboard;
        _mouse = mouse;

        if (_firstMove)
        {
            _lastMousePos = mouse.Position;
            _mouseDelta = Vector2.Zero;
            _firstMove = false;
        }
        else
        {
            _mouseDelta = mouse.Position - _lastMousePos;
            _lastMousePos = mouse.Position;
        }

        // Roll over keyboard edge sets.
        _keyDownLastFrame.Clear();
        foreach (var k in _keyDownThisFrame) _keyDownLastFrame.Add(k);
        _keyDownThisFrame.Clear();

        // Roll over mouse edge state. Sample fresh "this frame" state directly.
        for (int i = 0; i < ButtonCount; i++)
        {
            _mouseDownLastFrame[i] = _mouseDownThisFrame[i];
            _mouseDownThisFrame[i] = mouse.IsButtonDown((MouseButton)i);
        }
    }

    // ---- Keyboard ------------------------------------------------------------

    public bool IsKeyDown(Keys key)
    {
        if (_keyboard == null) return false;
        bool down = _keyboard.IsKeyDown(key);
        if (down) _keyDownThisFrame.Add(key);
        return down;
    }

    /// <summary>True only on the frame the key transitions from up to down.</summary>
    public bool WasKeyPressedThisFrame(Keys key)
    {
        bool downNow = _keyboard != null && _keyboard.IsKeyDown(key);
        if (downNow) _keyDownThisFrame.Add(key);
        return downNow && !_keyDownLastFrame.Contains(key);
    }

    // ---- Mouse buttons -------------------------------------------------------

    public bool IsMouseButtonDown(MouseButton button)
    {
        int i = (int)button;
        if (i < 0 || i >= ButtonCount) return false;
        return _mouseDownThisFrame[i];
    }

    /// <summary>True only on the frame the button transitions from up to down.</summary>
    public bool WasMouseButtonPressedThisFrame(MouseButton button)
    {
        int i = (int)button;
        if (i < 0 || i >= ButtonCount) return false;
        return _mouseDownThisFrame[i] && !_mouseDownLastFrame[i];
    }

    /// <summary>True only on the frame the button transitions from down to up.</summary>
    public bool WasMouseButtonReleasedThisFrame(MouseButton button)
    {
        int i = (int)button;
        if (i < 0 || i >= ButtonCount) return false;
        return !_mouseDownThisFrame[i] && _mouseDownLastFrame[i];
    }

    // ---- Cursor / window mutators -------------------------------------------

    public void SetCursorState(CursorState state)
    {
        if (_window != null) _window.CursorState = state;
    }

    /// <summary>Reset the first-move flag so the next mouse delta is zero (no jump after grabbing).</summary>
    public void ResetMouseFirstMove() => _firstMove = true;

    /// <summary>Convenience: grab the cursor for FPS-style mouse-look (legacy path; prefer instance.SetCursorState).</summary>
    public static void LockCursor(GameWindow window)
    {
        window.CursorState = CursorState.Grabbed;
    }
}
