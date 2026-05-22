using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

using ArcEngine.Engine.Input;
using ArcEngine.Engine.Lighting;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.SandboxGame;

namespace ArcEngine.Engine.Core;

public class Game : GameWindow
{
    private Renderer _renderer = null!;
    private Scene _scene = null!;
    private InputManager _input = null!;
    private Camera _camera = null!;
    private SandboxScene _sandbox = null!;

    public Game(GameWindowSettings gws, NativeWindowSettings nws)
        : base(gws, nws)
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        InputManager.LockCursor(this);

        _renderer = new Renderer();
        _renderer.Init();

        _scene = new Scene();
        _input = new InputManager();
        _camera = new Camera { Position = new Vector3(0f, 0.5f, 5f) };

        var shader = new Shader(
            "Assets/Shaders/basic.vert",
            "Assets/Shaders/basic.frag");

        _sandbox = new SandboxScene();
        _sandbox.Build(_scene, shader);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        float deltaTime = (float)args.Time;

        _input.Update(KeyboardState, MouseState);
        _camera.Update(_input, deltaTime);

        _sandbox.Update(deltaTime);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        _renderer.Clear();

        float aspectRatio = Size.X / (float)Size.Y;

        var lights = _sandbox.Lights;

        foreach (var obj in _scene.GetObjects())
        {
            _renderer.RenderObject(obj, _camera, lights, aspectRatio);
        }

        SwapBuffers();
    }
}
