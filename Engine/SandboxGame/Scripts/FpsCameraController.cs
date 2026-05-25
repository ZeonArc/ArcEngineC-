using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

using Hexa.NET.ImGui;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Editor;
using ArcEngine.Engine.Input;

namespace ArcEngine.Engine.SandboxGame.Scripts;

/// <summary>
/// FPS-style controller. Gated two ways:
/// <list type="bullet">
///   <item>Only runs when <see cref="EditorState.IsPlayMode"/> is true (Play mode toggle).</item>
///   <item>Skips while ImGui is consuming the mouse or keyboard (e.g. an open text input).</item>
/// </list>
/// </summary>
public class FpsCameraController : Script
{
    /// <summary>Set by SandboxScene before the script's first Update.</summary>
    public InputManager? Input;

    public float MoveSpeed = 3f;
    public float MouseSensitivity = 0.2f;

    private Camera? _camera;

    public override void Start()
    {
        _camera = GameObject.GetComponent<Camera>();
        if (_camera == null)
            Console.WriteLine("[FpsCameraController] Warning: no sibling Camera component found.");
    }

    public override void Update(float deltaTime)
    {
        if (Input == null || _camera == null) return;

        // Editor mode → camera frozen entirely.
        if (!EditorState.IsPlayMode) return;

        // Even in Play mode, defer to ImGui if it's eating the input
        // (e.g. some modal text input is focused).
        var io = ImGui.GetIO();
        if (io.WantCaptureMouse || io.WantCaptureKeyboard) return;

        // Movement.
        float speed = MoveSpeed * deltaTime;
        if (Input.IsKeyDown(Keys.W)) Transform.Position += _camera.Front * speed;
        if (Input.IsKeyDown(Keys.S)) Transform.Position -= _camera.Front * speed;
        if (Input.IsKeyDown(Keys.A)) Transform.Position -= _camera.Right * speed;
        if (Input.IsKeyDown(Keys.D)) Transform.Position += _camera.Right * speed;

        // Mouse-look.
        var delta = Input.MouseDelta;
        if (delta != Vector2.Zero)
        {
            _camera.Yaw += delta.X * MouseSensitivity;
            _camera.Pitch -= delta.Y * MouseSensitivity;
            _camera.Pitch = MathHelper.Clamp(_camera.Pitch, -89f, 89f);
        }
    }
}
