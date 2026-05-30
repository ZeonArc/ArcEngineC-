using Hexa.NET.ImGui;

using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Editor;
using ArcEngine.Engine.Input;

namespace ArcEngine.Engine.SandboxGame.Scripts;

/// <summary>
/// FPS-style camera controller. Two modes:
/// <list type="bullet">
///   <item><b>Edit mode</b> — hold the right mouse button to look + WASD to move
///         (Unity / Unreal viewport convention). Releasing the button unlocks the
///         cursor so the user can interact with the inspector again.</item>
///   <item><b>Play mode</b> — always-on, cursor stays grabbed by Game.cs.</item>
/// </list>
/// In both modes the script defers when ImGui is hovering UI / consuming keyboard.
/// </summary>
public class FpsCameraController : Script
{
    /// <summary>Set by SandboxScene before the script's first Update.</summary>
    public InputManager? Input;

    public float MoveSpeed = 3f;
    public float MouseSensitivity = 0.2f;

    private Camera? _camera;
    private bool _editLookActive;   // RMB held last frame in Edit mode

    public override void Start()
    {
        _camera = GameObject.GetComponent<Camera>();
        if (_camera == null)
            Console.WriteLine("[FpsCameraController] Warning: no sibling Camera component found.");
    }

    public override void Update(float deltaTime)
    {
        if (Input == null || _camera == null) return;

        var io = ImGui.GetIO();

        bool cameraActive;
        if (EditorState.IsPlayMode)
        {
            // Play mode: always-on (game owns the cursor; ImGui still suppresses if hovering UI).
            cameraActive = !io.WantCaptureMouse && !io.WantCaptureKeyboard;
        }
        else
        {
            // Edit mode: hold RMB to engage. Cursor toggles to Grabbed on press,
            // back to Normal on release. ImGui hover still suppresses.
            bool rmb = Input.IsMouseButtonDown(MouseButton.Right);
            bool rmbBlockedByUI = rmb && !_editLookActive && io.WantCaptureMouse; // ignore press over UI

            if (rmb && !_editLookActive && !rmbBlockedByUI)
            {
                Input.SetCursorState(CursorState.Grabbed);
                Input.ResetMouseFirstMove();
                _editLookActive = true;
            }
            else if (!rmb && _editLookActive)
            {
                Input.SetCursorState(CursorState.Normal);
                _editLookActive = false;
            }

            cameraActive = _editLookActive;
        }

        if (!cameraActive) return;

        // Movement.
        float speed = MoveSpeed * deltaTime;
        if (Input.IsKeyDown(Keys.W)) Transform.Position += _camera.Front * speed;
        if (Input.IsKeyDown(Keys.S)) Transform.Position -= _camera.Front * speed;
        if (Input.IsKeyDown(Keys.A)) Transform.Position -= _camera.Right * speed;
        if (Input.IsKeyDown(Keys.D)) Transform.Position += _camera.Right * speed;
        if (Input.IsKeyDown(Keys.E)) Transform.Position += Vector3.UnitY * speed;
        if (Input.IsKeyDown(Keys.Q)) Transform.Position -= Vector3.UnitY * speed;

        // Mouse-look.
        var delta = Input.MouseDelta;
        if (delta != Vector2.Zero)
        {
            _camera.Yaw   += delta.X * MouseSensitivity;
            _camera.Pitch -= delta.Y * MouseSensitivity;
            _camera.Pitch = MathHelper.Clamp(_camera.Pitch, -89f, 89f);
        }
    }
}
