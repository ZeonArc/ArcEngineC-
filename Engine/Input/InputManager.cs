using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace ArcEngine.Engine.Input;

/// <summary>
/// Per-frame input snapshot. Wraps OpenTK's <see cref="KeyboardState"/> / <see cref="MouseState"/>
/// and tracks mouse delta so callers (Camera, gameplay code, etc.) don't have to manage
/// "first move" bookkeeping themselves.
///
/// Usage:
///   _input.Update(KeyboardState, MouseState);   // once per frame, from Game.OnUpdateFrame
///   _input.IsKeyDown(Keys.W);
///   var d = _input.MouseDelta;
/// </summary>
public class InputManager
{
    private KeyboardState? _keyboard;
    private Vector2 _lastMousePos;
    private Vector2 _mouseDelta;
    private bool _firstMove = true;

    private readonly HashSet<Keys> _downLastFrame = new();
    private readonly HashSet<Keys> _downThisFrame = new();

    /// <summary>Mouse movement (in pixels) since the previous Update call. Zero on the first frame.</summary>
    public Vector2 MouseDelta => _mouseDelta;

    /// <summary>True until the very first mouse position has been recorded.</summary>
    public bool IsMouseFirstMove => _firstMove;

    /// <summary>Update the snapshot. Call once at the start of each update tick.</summary>
    public void Update(KeyboardState keyboard, MouseState mouse)
    {
        _keyboard = keyboard;

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

        // Maintain edge-detection sets. Cheap because the set of keys we care about is small.
        _downLastFrame.Clear();
        foreach (var k in _downThisFrame) _downLastFrame.Add(k);
        _downThisFrame.Clear();
    }

    public bool IsKeyDown(Keys key)
    {
        if (_keyboard == null) return false;
        bool down = _keyboard.IsKeyDown(key);
        if (down) _downThisFrame.Add(key);
        return down;
    }

    /// <summary>True only on the frame the key transitions from up to down.</summary>
    public bool WasKeyPressedThisFrame(Keys key)
    {
        // Force the "this frame" set to include this key if it's down (in case caller
        // never queried IsKeyDown for it).
        bool downNow = _keyboard != null && _keyboard.IsKeyDown(key);
        if (downNow) _downThisFrame.Add(key);
        return downNow && !_downLastFrame.Contains(key);
    }

    /// <summary>Convenience: grab the cursor for FPS-style mouse-look.</summary>
    public static void LockCursor(GameWindow window)
    {
        window.CursorState = CursorState.Grabbed;
    }
}
