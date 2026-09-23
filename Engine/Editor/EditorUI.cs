using System.Linq;
using System.Reflection;

using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;

using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

using SnVec2 = System.Numerics.Vector2;
using SnVec3 = System.Numerics.Vector3;

namespace ArcEngine.Engine.Editor;

/// <summary>
/// Issues all per-frame ImGui calls for the editor: a Hierarchy window on the left,
/// an Inspector window (editable) on the right, a gizmo-mode toolbar, the 3D
/// translate/rotate/scale gizmo (ImGuizmo) over the viewport, and a status bar.
/// </summary>
public static class EditorUI
{
    private const float HierarchyWidth = 280f;
    private const float InspectorWidth = 340f;
    private const float ToolbarHeight = 40f;
    private const float StatusHeight = 32f;

    /// <summary>
    /// All Component subclasses we can let the user "Add Component". Reflection-cached.
    /// Excludes Transform (mandatory) and abstract / no-default-ctor types.
    /// </summary>
    private static readonly Type[] s_addableComponentTypes =
        typeof(Component).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                     && typeof(Component).IsAssignableFrom(t)
                     && t != typeof(Transform)
                     && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.Name)
            .ToArray();

    /// <summary>Render every editor window. Call after ImGui.NewFrame and ImGuizmo.BeginFrame.</summary>
    public static void Render(Scene scene, Camera? camera, Vector2i windowSize)
    {
        MainMenu.Render(scene);
        DrawToolbar();
        DrawHierarchy(scene);
        DrawInspector();
        AssetBrowser.Render(scene);
        AudioMixerWindow.Render();
        DrawGizmo(camera, windowSize);
        DrawStatusBar();
    }

    private static float MenuOffset => MainMenu.HeightPixels;
    private static float BottomReserved => AssetBrowser.HeightPixels;

    // ------------------------------------------------------------------------
    // Hierarchy
    // ------------------------------------------------------------------------

    private static void DrawHierarchy(Scene scene)
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new SnVec2(0f, ToolbarHeight + MenuOffset), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new SnVec2(HierarchyWidth, io.DisplaySize.Y - ToolbarHeight - MenuOffset - BottomReserved), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        if (!ImGui.Begin("Hierarchy", flags)) { ImGui.End(); return; }

        if (ImGui.Button("+ Create Empty"))
            UndoStack.Execute(new CreateGameObjectCommand(scene));

        ImGui.SameLine();
        ImGui.BeginDisabled(!UndoStack.CanUndo);
        if (ImGui.SmallButton("Undo")) UndoStack.Undo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!UndoStack.CanRedo);
        if (ImGui.SmallButton("Redo")) UndoStack.Redo();
        ImGui.EndDisabled();

        ImGui.Text($"GameObjects ({scene.GetObjects().Count})");
        ImGui.Separator();

        var objects = scene.GetObjects();
        for (int i = 0; i < objects.Count; i++)
        {
            var go = objects[i];
            bool isSelected = Selection.IsSelected(go);

            if (ImGui.Selectable($"{go.Name}##{i}", isSelected))
            {
                if (io.KeyShift)     Selection.SelectRange(go, objects);
                else if (io.KeyCtrl) Selection.Toggle(go);
                else                 Selection.Set(go);
            }
        }

        ImGui.End();
    }

    // ------------------------------------------------------------------------
    // Inspector — editable
    // ------------------------------------------------------------------------

    private static void DrawInspector()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new SnVec2(io.DisplaySize.X - InspectorWidth, ToolbarHeight + MenuOffset), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new SnVec2(InspectorWidth, io.DisplaySize.Y - ToolbarHeight - MenuOffset - BottomReserved), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        if (!ImGui.Begin("Inspector", flags)) { ImGui.End(); return; }

        var sel = Selection.Selected;
        if (sel.Count == 0)
        {
            ImGui.TextDisabled("Nothing selected.");
            ImGui.End();
            return;
        }

        if (sel.Count > 1)
        {
            ImGui.Text($"Multiple selected ({sel.Count})");
            ImGui.Separator();
            foreach (var go in sel) ImGui.BulletText(go.Name);
            ImGui.End();
            return;
        }

        // Single-selection details.
        var target = sel[0];
        ImGui.Text(target.Name);
        ImGui.SameLine();
        if (ImGui.SmallButton("Save as Prefab"))
        {
            try
            {
                string safeName = string.Join("_", target.Name.Split(Path.GetInvalidFileNameChars()));
                string prefabPath = $"Assets/Prefabs/{safeName}{ArcEngine.Engine.Serialization.Prefabs.Extension}";
                ArcEngine.Engine.Serialization.Prefabs.Save(target, prefabPath);
                Console.WriteLine($"[Inspector] Saved prefab → {prefabPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Inspector] Failed to save prefab: {ex.Message}");
            }
        }
        ImGui.Separator();

        // Iterate components. Track to remove (avoid mutating during iteration).
        List<Component>? toRemove = null;
        int idx = 0;
        foreach (var c in target.Components.ToArray())
        {
            string name = c.GetType().Name;

            // Remove (X) button — skip Transform.
            if (c is not Transform)
            {
                if (ImGui.SmallButton($"X##rem{idx}"))
                {
                    toRemove ??= new();
                    toRemove.Add(c);
                }
                ImGui.SameLine();
            }

            if (ImGui.CollapsingHeader($"{name}##cmp{idx}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.PushID(idx);
                ComponentInspector.Draw(c);
                ImGui.PopID();
            }

            idx++;
        }

        if (toRemove != null)
            foreach (var c in toRemove) UndoStack.Execute(new RemoveComponentCommand(target, c));

        // Add Component dropdown.
        ImGui.Separator();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##AddComp", "Add Component..."))
        {
            foreach (var t in s_addableComponentTypes)
            {
                if (ImGui.Selectable(t.Name))
                    UndoStack.Execute(new AddComponentCommand(target, t));
            }
            ImGui.EndCombo();
        }

        ImGui.End();
    }

    // ------------------------------------------------------------------------
    // Toolbar — gizmo mode + space
    // ------------------------------------------------------------------------

    private static void DrawToolbar()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new SnVec2(0f, MenuOffset), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new SnVec2(io.DisplaySize.X, ToolbarHeight), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoResize
                  | ImGuiWindowFlags.NoMove
                  | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoSavedSettings
                  | ImGuiWindowFlags.NoDecoration;

        if (!ImGui.Begin("##toolbar", flags)) { ImGui.End(); return; }

        ToolbarToggle("Translate (W)", EditorState.GizmoMode == GizmoMode.Translate, () => EditorState.GizmoMode = GizmoMode.Translate);
        ImGui.SameLine();
        ToolbarToggle("Rotate (E)",    EditorState.GizmoMode == GizmoMode.Rotate,    () => EditorState.GizmoMode = GizmoMode.Rotate);
        ImGui.SameLine();
        ToolbarToggle("Scale (R)",     EditorState.GizmoMode == GizmoMode.Scale,     () => EditorState.GizmoMode = GizmoMode.Scale);

        ImGui.SameLine();
        ImGui.Text("  |  ");
        ImGui.SameLine();

        string spaceLabel = EditorState.GizmoSpace == GizmoSpace.World ? "World (G)" : "Local (G)";
        if (ImGui.Button(spaceLabel))
            EditorState.GizmoSpace = EditorState.GizmoSpace == GizmoSpace.World ? GizmoSpace.Local : GizmoSpace.World;

        ImGui.End();
    }

    private static void ToolbarToggle(string label, bool active, Action onClick)
    {
        if (active)
        {
            // Highlight when active.
            ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.30f, 0.55f, 0.95f, 1f));
            if (ImGui.Button(label)) onClick();
            ImGui.PopStyleColor();
        }
        else
        {
            if (ImGui.Button(label)) onClick();
        }
    }

    // ------------------------------------------------------------------------
    // Gizmo (ImGuizmo)
    // ------------------------------------------------------------------------

    private static void DrawGizmo(Camera? camera, Vector2i windowSize)
    {
        if (camera == null) return;
        if (Selection.Selected.Count == 0) return;

        var target = Selection.Selected[0];
        // Don't gizmo the camera itself — that's confusing.
        if (target.GetComponent<Camera>() != null) return;

        // Wrap the gizmo in a fullscreen invisible window so ImGuizmo has a draw list.
        // NoMouseInputs lets clicks fall through to the gizmo handles + viewport.
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new SnVec2(0f, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(io.DisplaySize, ImGuiCond.Always);
        var overlayFlags = ImGuiWindowFlags.NoTitleBar
                         | ImGuiWindowFlags.NoResize
                         | ImGuiWindowFlags.NoMove
                         | ImGuiWindowFlags.NoScrollbar
                         | ImGuiWindowFlags.NoBackground
                         | ImGuiWindowFlags.NoSavedSettings
                         | ImGuiWindowFlags.NoBringToFrontOnFocus
                         | ImGuiWindowFlags.NoFocusOnAppearing
                         | ImGuiWindowFlags.NoNav
                         | ImGuiWindowFlags.NoMouseInputs;
        ImGui.Begin("##gizmo_overlay", overlayFlags);

        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetRect(0f, 0f, windowSize.X, windowSize.Y);

        // ImGuizmo wants column-major float[16] matrices. OpenTK's Matrix4 stores rows
        // contiguously; transposing converts to column-major.
        float[] view = ToColumnMajor(camera.GetViewMatrix());
        float aspect = windowSize.X / (float)windowSize.Y;
        float[] proj = ToColumnMajor(camera.GetProjectionMatrix(aspect));
        float[] model = ToColumnMajor(target.Transform.GetWorldModelMatrix());

        ImGuizmoOperation op = EditorState.GizmoMode switch
        {
            GizmoMode.Translate => ImGuizmoOperation.Translate,
            GizmoMode.Rotate    => ImGuizmoOperation.Rotate,
            GizmoMode.Scale     => ImGuizmoOperation.Scale,
            _                   => ImGuizmoOperation.Translate,
        };

        ImGuizmoMode mode = EditorState.GizmoSpace == GizmoSpace.World
            ? ImGuizmoMode.World
            : ImGuizmoMode.Local;

        if (ImGuizmo.Manipulate(ref view[0], ref proj[0], op, mode, ref model[0]))
        {
            // Decompose back to TRS and write to Transform.
            // (Caveat: writes world-space TRS into the local Transform — fine for our
            //  flat-hierarchy demo objects; would need parent-inverse math for nested trees.)
            float[] outT = new float[3];
            float[] outR = new float[3];
            float[] outS = new float[3];
            ImGuizmo.DecomposeMatrixToComponents(ref model[0], ref outT[0], ref outR[0], ref outS[0]);

            target.Transform.Position = new Vector3(outT[0], outT[1], outT[2]);
            target.Transform.Rotation = new Vector3(outR[0], outR[1], outR[2]);
            target.Transform.Scale    = new Vector3(outS[0], outS[1], outS[2]);
        }

        ImGui.End();
    }

    private static float[] ToColumnMajor(Matrix4 m)
    {
        // OpenTK Matrix4 layout: Row0..Row3 with .X .Y .Z .W. We want column-major float[16].
        return new float[]
        {
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44,
        };
    }

    // ------------------------------------------------------------------------
    // Status bar
    // ------------------------------------------------------------------------

    private static void DrawStatusBar()
    {
        var io = ImGui.GetIO();
        const float w = 320f;
        ImGui.SetNextWindowPos(new SnVec2((io.DisplaySize.X - w) * 0.5f, io.DisplaySize.Y - StatusHeight - 8f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new SnVec2(w, StatusHeight), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoResize
                  | ImGuiWindowFlags.NoMove
                  | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoSavedSettings
                  | ImGuiWindowFlags.NoBringToFrontOnFocus
                  | ImGuiWindowFlags.NoFocusOnAppearing;

        ImGui.SetNextWindowBgAlpha(0.6f);
        if (ImGui.Begin("##status", flags))
        {
            string mode = EditorState.IsPlayMode ? "PLAY" : "EDIT";
            ImGui.Text($"Mode: {mode}    [F1 toggle]    W/E/R: gizmo   G: space");
        }
        ImGui.End();
    }
}
