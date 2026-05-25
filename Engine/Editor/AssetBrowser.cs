using Hexa.NET.ImGui;

using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Loaders;
using ArcEngine.Engine.Resources;

using SnVec2 = System.Numerics.Vector2;

namespace ArcEngine.Engine.Editor;

/// <summary>
/// Bottom-panel asset browser. Walks the <see cref="AssetsRoot"/> folder, shows a folder
/// tree on the left and the selected folder's files on the right. Right-click a model
/// file (.obj / .gltf / .glb) for "Load into scene".
/// </summary>
public static class AssetBrowser
{
    /// <summary>Height of the panel in framebuffer pixels.</summary>
    public const float HeightPixels = 200f;

    /// <summary>Root directory scanned for assets.</summary>
    public const string AssetsRoot = "Assets";

    private const float TreeColumnWidth = 220f;

    private static string s_selectedFolder = AssetsRoot;
    private static string? s_selectedFile;

    public static void Render(Scene scene)
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new SnVec2(0f, io.DisplaySize.Y - HeightPixels), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new SnVec2(io.DisplaySize.X, HeightPixels), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        if (!ImGui.Begin("Assets", flags)) { ImGui.End(); return; }

        // Two-column layout: folder tree | file list.
        ImGui.BeginChild("##folders", new SnVec2(TreeColumnWidth, 0f), ImGuiChildFlags.Borders);
        DrawFolderTree(AssetsRoot);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##files", new SnVec2(0f, 0f), ImGuiChildFlags.Borders);
        DrawFileList(scene);
        ImGui.EndChild();

        ImGui.End();
    }

    // ------------------------------------------------------------------------
    // Folder tree (left column)
    // ------------------------------------------------------------------------

    private static void DrawFolderTree(string path)
    {
        if (!Directory.Exists(path)) return;

        string folderName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(folderName)) folderName = path;

        // Default-open the root + folders containing the current selection so the user
        // can see where they are.
        var nodeFlags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;
        if (path == AssetsRoot) nodeFlags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (s_selectedFolder == path) nodeFlags |= ImGuiTreeNodeFlags.Selected;

        bool open = ImGui.TreeNodeEx(folderName + "##" + path, nodeFlags);

        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
        {
            s_selectedFolder = path;
            s_selectedFile = null;
        }

        if (open)
        {
            string[] subdirs;
            try { subdirs = Directory.GetDirectories(path); }
            catch { subdirs = Array.Empty<string>(); }

            Array.Sort(subdirs, StringComparer.OrdinalIgnoreCase);
            foreach (var sub in subdirs) DrawFolderTree(sub);

            ImGui.TreePop();
        }
    }

    // ------------------------------------------------------------------------
    // File list (right column)
    // ------------------------------------------------------------------------

    private static void DrawFileList(Scene scene)
    {
        ImGui.Text(s_selectedFolder);
        ImGui.Separator();

        if (!Directory.Exists(s_selectedFolder))
        {
            ImGui.TextDisabled("(folder does not exist)");
            return;
        }

        string[] files;
        try { files = Directory.GetFiles(s_selectedFolder); }
        catch { files = Array.Empty<string>(); }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string name = Path.GetFileName(file);
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string icon = IconFor(ext);

            bool isSelected = s_selectedFile == file;
            if (ImGui.Selectable($"{icon} {name}##file{file}", isSelected))
                s_selectedFile = file;

            if (ImGui.BeginPopupContextItem("##ctx" + file))
            {
                if (IsModel(ext))
                {
                    if (ImGui.MenuItem("Load into scene"))
                        LoadModelIntoScene(scene, file);
                }
                else
                {
                    ImGui.TextDisabled("(no actions for this type)");
                }
                ImGui.EndPopup();
            }
        }

        // Detail strip at the bottom.
        if (s_selectedFile != null && File.Exists(s_selectedFile))
        {
            ImGui.Separator();
            var info = new FileInfo(s_selectedFile);
            ImGui.TextDisabled($"Path: {s_selectedFile}");
            ImGui.TextDisabled($"Size: {info.Length:N0} bytes   Type: {info.Extension}");
        }
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private static bool IsModel(string ext) => ext is ".obj" or ".gltf" or ".glb";

    private static string IconFor(string ext) => ext switch
    {
        ".obj" or ".gltf" or ".glb" => "[M]",
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" => "[T]",
        ".vert" or ".frag" or ".glsl" => "[S]",
        ".mtl"  => "[m]",
        ".json" => "[J]",
        _       => "[F]",
    };

    private static void LoadModelIntoScene(Scene scene, string path)
    {
        try
        {
            var data = Resources.LoadModelData(path);
            var shader = Resources.LoadShader(
                "Assets/Shaders/basic.vert",
                "Assets/Shaders/basic.frag");

            var go = ModelBuilder.Build(data, shader);
            // Position right where the user is looking (origin works for the demo).
            go.Transform.Position = new Vector3(0f, 1f, 0f);
            scene.Add(go);

            Console.WriteLine($"[AssetBrowser] Loaded into scene: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetBrowser] Failed to load '{path}': {ex.Message}");
        }
    }
}
