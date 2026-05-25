namespace ArcEngine.Engine.Editor;

public enum GizmoMode
{
    Translate,
    Rotate,
    Scale,
}

public enum GizmoSpace
{
    World,
    Local,
}

/// <summary>
/// Global editor toggle state. The engine starts in Edit mode (cursor visible,
/// FPS controller disabled, ImGui interactive). F1 flips to Play mode (cursor
/// grabbed, FPS controller active, ImGui visible-but-non-interactive).
/// </summary>
public static class EditorState
{
    private static bool s_isPlayMode;

    /// <summary>Fired when <see cref="IsPlayMode"/> changes value.</summary>
    public static event Action<bool>? PlayModeChanged;

    public static bool IsPlayMode
    {
        get => s_isPlayMode;
        set
        {
            if (s_isPlayMode == value) return;
            s_isPlayMode = value;
            PlayModeChanged?.Invoke(value);
        }
    }

    /// <summary>Active gizmo operation. Hotkeys: W = Translate, E = Rotate, R = Scale.</summary>
    public static GizmoMode GizmoMode { get; set; } = GizmoMode.Translate;

    /// <summary>World vs Local space for the gizmo. Hotkey: G to toggle.</summary>
    public static GizmoSpace GizmoSpace { get; set; } = GizmoSpace.World;

    /// <summary>Current scene file path. File → Save writes here; File → Load reads here.</summary>
    public static string CurrentScenePath { get; set; } = "Assets/Scenes/sandbox.scene.json";
}
