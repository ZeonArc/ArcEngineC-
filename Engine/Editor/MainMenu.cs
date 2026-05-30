using Hexa.NET.ImGui;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Serialization;

namespace ArcEngine.Engine.Editor;

/// <summary>
/// Top-of-window main menu bar: File menu (Save / Load) and a Play/Stop toggle
/// pinned to the right side. Sits at y = 0 and pushes everything else down by
/// <see cref="HeightPixels"/>.
/// </summary>
public static class MainMenu
{
    /// <summary>Approx height of the rendered menu bar (used to offset other windows).</summary>
    public const float HeightPixels = 22f;

    public static void Render(Scene scene)
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Save Scene", "Ctrl+S"))
                SaveScene(scene);

            if (ImGui.MenuItem("Load Scene", "Ctrl+O"))
                LoadScene(scene);

            ImGui.Separator();
            ImGui.TextDisabled(EditorState.CurrentScenePath);

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Scenes"))
        {
            foreach (var demo in ArcEngine.Engine.SandboxGame.Scenes.DemoRegistry.All)
            {
                bool isCurrent = ReferenceEquals(demo, ArcEngine.Engine.SandboxGame.Scenes.DemoRegistry.Current);
                if (ImGui.MenuItem(demo.Name, "", isCurrent) && !isCurrent)
                {
                    if (EditorContext.SharedShader != null && EditorContext.Input != null && EditorContext.Renderer != null)
                    {
                        ArcEngine.Engine.SandboxGame.Scenes.DemoRegistry.Switch(
                            demo, scene, EditorContext.SharedShader, EditorContext.Input, EditorContext.Renderer);
                    }
                }
            }
            ImGui.EndMenu();
        }

        // Play / Stop button — right-aligned. ImGui doesn't have a built-in right-align,
        // but we can compute the position manually.
        string label = EditorState.IsPlayMode ? "[ Stop ]" : "[ Play ]";
        var labelSize = ImGui.CalcTextSize(label);
        float available = ImGui.GetContentRegionAvail().X;
        ImGui.SameLine(ImGui.GetCursorPosX() + available - labelSize.X - 16f);

        if (EditorState.IsPlayMode)
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f));
        else
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.4f, 1f, 0.5f, 1f));

        if (ImGui.MenuItem(label))
            EditorState.IsPlayMode = !EditorState.IsPlayMode;

        ImGui.PopStyleColor();

        ImGui.EndMainMenuBar();
    }

    /// <summary>Save the current scene to <see cref="EditorState.CurrentScenePath"/>.</summary>
    public static void SaveScene(Scene scene)
    {
        try
        {
            SceneSerializer.Save(scene, EditorState.CurrentScenePath);
            Console.WriteLine($"[MainMenu] Saved scene → {EditorState.CurrentScenePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainMenu] Save failed: {ex.Message}");
        }
    }

    /// <summary>Load scene state from <see cref="EditorState.CurrentScenePath"/>.</summary>
    public static void LoadScene(Scene scene)
    {
        try
        {
            SceneSerializer.Load(scene, EditorState.CurrentScenePath);
            Console.WriteLine($"[MainMenu] Loaded scene ← {EditorState.CurrentScenePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainMenu] Load failed: {ex.Message}");
        }
    }
}
