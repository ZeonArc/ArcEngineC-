using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Editor;

/// <summary>
/// Editor's set of currently-selected GameObjects. Supports single-click,
/// Ctrl-toggle, and Shift-range selection.
/// </summary>
public static class Selection
{
    /// <summary>The current selection. Last-clicked is always at the end.</summary>
    public static List<GameObject> Selected { get; } = new();

    /// <summary>Anchor for Shift-range selection (the last single-click target).</summary>
    private static GameObject? s_anchor;

    public static bool IsSelected(GameObject go) => Selected.Contains(go);

    /// <summary>Single-click: clear selection and add this one.</summary>
    public static void Set(GameObject go)
    {
        Selected.Clear();
        Selected.Add(go);
        s_anchor = go;
    }

    /// <summary>Ctrl-click: toggle membership; updates the anchor.</summary>
    public static void Toggle(GameObject go)
    {
        if (Selected.Remove(go))
        {
            // If we removed the anchor, fall back to the last item (or null).
            if (s_anchor == go) s_anchor = Selected.Count > 0 ? Selected[^1] : null;
        }
        else
        {
            Selected.Add(go);
            s_anchor = go;
        }
    }

    /// <summary>
    /// Shift-click: select the inclusive range from the anchor to <paramref name="target"/>
    /// using <paramref name="ordered"/> (the hierarchy's traversal order) for index lookup.
    /// </summary>
    public static void SelectRange(GameObject target, IReadOnlyList<GameObject> ordered)
    {
        if (s_anchor == null)
        {
            Set(target);
            return;
        }

        int a = ordered.IndexOf(s_anchor);
        int b = ordered.IndexOf(target);
        if (a < 0 || b < 0)
        {
            Set(target);
            return;
        }

        if (a > b) (a, b) = (b, a);

        Selected.Clear();
        for (int i = a; i <= b; i++) Selected.Add(ordered[i]);
        // Keep the anchor where it was — Shift+click again extends from the same anchor.
    }

    public static void Clear()
    {
        Selected.Clear();
        s_anchor = null;
    }
}

internal static class ListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> list, T item) where T : class
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }
}
