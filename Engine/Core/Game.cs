using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;

using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using ArcEngine.Engine.Editor;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.SandboxGame;

namespace ArcEngine.Engine.Core;

public class Game : GameWindow
{
    private Renderer _renderer = null!;
    private Scene _scene = null!;
    private InputManager _input = null!;
    private ImGuiController _imgui = null!;

    /// <summary>
    /// Scene state captured the moment Play mode was entered, restored on Exit.
    /// Null when in Edit mode. JSON string keyed on each GameObject's Name.
    /// </summary>
    private string? _playSnapshot;

    public Game(GameWindowSettings gws, NativeWindowSettings nws)
        : base(gws, nws)
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        // Default to Edit mode: cursor visible.
        CursorState = CursorState.Normal;

        _renderer = new Renderer();
        _renderer.Init();

        _scene = new Scene();
        _input = new InputManager();
        _imgui = new ImGuiController(this);

        EditorState.PlayModeChanged += isPlay =>
        {
            CursorState = isPlay ? CursorState.Grabbed : CursorState.Normal;

            if (isPlay)
            {
                // Snapshot the scene's current state so Stop returns it here.
                _playSnapshot = ArcEngine.Engine.Serialization.SceneSerializer.SaveToString(_scene);
            }
            else if (_playSnapshot != null)
            {
                // Restore the snapshot taken at Play entry.
                ArcEngine.Engine.Serialization.SceneSerializer.LoadFromString(_scene, _playSnapshot);

                // Push the restored Transforms back into BepuPhysics bodies, otherwise
                // physics would keep evolving from the play-time pose on the next step.
                foreach (var rb in _scene.FindComponents<ArcEngine.Engine.Physics.Rigidbody>())
                    rb.SyncToTransform();

                _playSnapshot = null;
            }
        };

        var shader = ArcEngine.Engine.Resources.Resources.LoadShader(
            "Assets/Shaders/basic.vert",
            "Assets/Shaders/basic.frag");

        new SandboxScene().Build(_scene, shader, _input);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        float deltaTime = (float)args.Time;

        _input.Update(KeyboardState, MouseState);

        // Editor hotkeys — only in Edit mode and only when ImGui isn't capturing keyboard.
        var io = ImGui.GetIO();
        bool kbAllowed = !io.WantCaptureKeyboard;
        if (kbAllowed)
        {
            // F1: toggle Edit / Play mode.
            if (_input.WasKeyPressedThisFrame(Keys.F1))
                EditorState.IsPlayMode = !EditorState.IsPlayMode;

            if (!EditorState.IsPlayMode)
            {
                if (_input.WasKeyPressedThisFrame(Keys.W)) EditorState.GizmoMode = GizmoMode.Translate;
                if (_input.WasKeyPressedThisFrame(Keys.E)) EditorState.GizmoMode = GizmoMode.Rotate;
                if (_input.WasKeyPressedThisFrame(Keys.R)) EditorState.GizmoMode = GizmoMode.Scale;
                if (_input.WasKeyPressedThisFrame(Keys.G))
                    EditorState.GizmoSpace = EditorState.GizmoSpace == GizmoSpace.World
                        ? GizmoSpace.Local : GizmoSpace.World;

                // Ctrl+S / Ctrl+O — Save / Load scene.
                bool ctrl = io.KeyCtrl;
                if (ctrl && _input.WasKeyPressedThisFrame(Keys.S)) MainMenu.SaveScene(_scene);
                if (ctrl && _input.WasKeyPressedThisFrame(Keys.O)) MainMenu.LoadScene(_scene);
            }
        }

        _scene.Update(deltaTime);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        // 1) 3D scene — use the physical framebuffer size for correct viewport on hi-DPI displays.
        _renderer.RenderScene(_scene, FramebufferSize);

        // 2) Editor UI overlay.
        // Mouse events arrive in logical (ClientSize) coords, GL renders at physical (FramebufferSize)
        // pixels — pass both so ImGui's DisplayFramebufferScale is set correctly.
        _imgui.NewFrame((float)args.Time, ClientSize, FramebufferSize);
        ImGuizmo.BeginFrame();

        var camera = _scene.FindComponent<Camera>();
        EditorUI.Render(_scene, camera, FramebufferSize);

        _imgui.Render();

        SwapBuffers();
    }

    protected override void OnUnload()
    {
        _imgui?.Dispose();
        base.OnUnload();
    }
}
