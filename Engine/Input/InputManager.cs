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
    }

    public bool IsKeyDown(Keys key)
    {
        return _keyboard != null && _keyboard.IsKeyDown(key);
    }

    /// <summary>Convenience: grab the cursor for FPS-style mouse-look.</summary>
    public static void LockCursor(GameWindow window)
    {
        window.CursorState = CursorState.Grabbed;
    }
}
