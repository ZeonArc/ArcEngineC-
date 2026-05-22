// ArcEngine — Phase 4 sandbox.
//
// Controls:
//   W / A / S / D  — move forward / left / back / right
//   Mouse          — look around
//
// The window opens with the cursor grabbed for FPS-style mouse-look.

using OpenTK.Windowing.Desktop;
using OpenTK.Mathematics;
using ArcEngine.Engine.Core;

class Program
{
    static void Main()
    {
        var nativeSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(800, 600),
            Title = "ArcEngine"
        };

        using (var game = new Game(GameWindowSettings.Default, nativeSettings))
        {
            game.Run();
        }
    }
}
