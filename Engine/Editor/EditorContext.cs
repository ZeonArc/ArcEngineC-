using ArcEngine.Engine.Core;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.Editor;

/// <summary>
/// Static back-references the editor needs in places where threading them through
/// every method signature would be noisy. Populated once by <c>Game.OnLoad</c>.
/// </summary>
public static class EditorContext
{
    public static Scene? Scene;
    public static Shader? SharedShader;
    public static InputManager? Input;
    public static Renderer? Renderer;
}
