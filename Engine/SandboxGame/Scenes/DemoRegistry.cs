using ArcEngine.Engine.Core;
using ArcEngine.Engine.Editor;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.SandboxGame.Scenes;

/// <summary>
/// Registry of available demo scenes + the scene-swap helper. <c>MainMenu</c> uses
/// these to populate its "Scenes" submenu.
/// </summary>
public static class DemoRegistry
{
    /// <summary>All registered demos, in display order.</summary>
    public static readonly IReadOnlyList<IDemoScene> All = new IDemoScene[]
    {
        new ModelsDemo(),
        new LightingDemo(),
        new PhysicsDemo(),
        new ParticlesDemo(),
    };

    /// <summary>Default scene loaded at startup.</summary>
    public static IDemoScene Default => All[0];

    /// <summary>The most recently loaded demo (null at startup until something switches).</summary>
    public static IDemoScene? Current { get; internal set; }

    /// <summary>
    /// Tear down the current scene, build the new demo. Editor state (selection,
    /// play mode) is reset so we don't carry over stale references.
    /// </summary>
    public static void Switch(IDemoScene demo, Scene scene, Shader sharedShader,
                              InputManager input, Renderer renderer)
    {
        // Force back to Edit mode (drops the snapshot, restores cursor) before tearing
        // down so play-mode-only refs don't outlive the rebuild.
        EditorState.IsPlayMode = false;

        scene.Clear();
        Selection.Clear();
        renderer.OnSceneSwitched();

        demo.Build(scene, sharedShader, input);
        Current = demo;
    }
}
