using ArcEngine.Engine.Core;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.SandboxGame.Scenes;

/// <summary>
/// One demo scene. Implementations populate a fresh <see cref="Scene"/> with whatever
/// GameObjects + components they want to demonstrate.
///
/// Demos are listed in <see cref="DemoRegistry.All"/> and invoked from the editor's
/// "Scenes" menu (or at startup as the default).
/// </summary>
public interface IDemoScene
{
    /// <summary>Display name for the editor menu.</summary>
    string Name { get; }

    /// <summary>Build content into the (already-cleared) scene.</summary>
    void Build(Scene scene, Shader sharedShader, InputManager input);
}
