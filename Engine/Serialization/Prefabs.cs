using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Serialization;

/// <summary>
/// Prefab support — a "prefab" is a saved GameObject subtree that can be instantiated
/// any number of times into a Scene, at runtime or in the editor. The on-disk format
/// is a JSON file produced by <see cref="SceneSerializer"/> containing only the
/// selected subtree (as if it were a mini scene).
///
/// v1 scope:
/// <list type="bullet">
///   <item><see cref="Save"/> — write a GameObject subtree to a .prefab.json file.</item>
///   <item><see cref="Instantiate"/> — spawn a fresh copy of a prefab into a scene.</item>
/// </list>
/// Override tracking (per-instance property diffs vs the prefab) and apply/revert
/// workflows are not yet implemented; each instantiation is a plain copy.
/// </summary>
public static class Prefabs
{
    public const string Extension = ".prefab.json";

    /// <summary>
    /// Serialize <paramref name="subtreeRoot"/> and every descendant to
    /// <paramref name="path"/>. The root's world Transform is captured as-is;
    /// instantiating restores it to the same absolute pose.
    /// </summary>
    public static void Save(GameObject subtreeRoot, string path)
    {
        // Trick: build a scratch Scene containing just this subtree, then serialize it
        // with the standard scene serializer. We don't Scene.Add — that would reparent
        // the GameObject away from its current scene; instead we walk the tree ourselves
        // and hand a filtered snapshot to a private overload.
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        var virtualScene = new Scene();
        var collected = new List<GameObject>();
        Collect(subtreeRoot, collected);

        // Move ownership: temporarily set Scene to the virtual scene so SerializeGameObjects
        // walks it. We restore it after saving so the live scene keeps its objects.
        var originalScenes = new Scene?[collected.Count];
        for (int i = 0; i < collected.Count; i++)
        {
            originalScenes[i] = collected[i].Scene;
            collected[i].Scene = virtualScene;
        }
        var sceneField = typeof(Scene).GetField("_gameObjects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var listRef = (List<GameObject>)sceneField.GetValue(virtualScene)!;
        listRef.AddRange(collected);

        try
        {
            SceneSerializer.Save(virtualScene, path);
        }
        finally
        {
            // Restore live-scene ownership so the source GameObject isn't left dangling.
            listRef.Clear();
            for (int i = 0; i < collected.Count; i++)
                collected[i].Scene = originalScenes[i];
        }
    }

    /// <summary>
    /// Load the prefab at <paramref name="path"/> and add its GameObjects to
    /// <paramref name="target"/>. Returns the newly-created root GameObject, or null
    /// if the file is missing / malformed. When the prefab has more than one root
    /// (rare), the first root in file order is returned; all roots are added to the scene.
    /// </summary>
    public static GameObject? Instantiate(string path, Scene target)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[Prefabs] File not found: {path}");
            return null;
        }

        // Snapshot the target scene's current roots so we can identify the newly-added ones.
        var before = new HashSet<GameObject>(target.GetObjects());

        // LoadFromString does a Scene.Clear() — that's fine for whole-scene loads but
        // wrong here. We route through a scratch scene, then move the roots over.
        var scratch = new Scene();
        SceneSerializer.LoadFromString(scratch, File.ReadAllText(path));

        var newRoots = new List<GameObject>();
        foreach (var go in scratch.GetObjects())
        {
            if (go.Transform.Parent != null) continue;
            newRoots.Add(go);
        }

        // Clear scratch's list without destroying the components (we're transferring
        // ownership). The GameObject.Scene ref is fixed below.
        var sceneField = typeof(Scene).GetField("_gameObjects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((List<GameObject>)sceneField.GetValue(scratch)!).Clear();

        foreach (var root in newRoots)
        {
            // Recursively clear Scene back-refs so target.Add can re-set them.
            ClearSceneRefs(root);
            target.Add(root);
        }

        return newRoots.Count > 0 ? newRoots[0] : null;
    }

    // ============================================================================
    // Internal walk helpers
    // ============================================================================

    private static void Collect(GameObject go, List<GameObject> into)
    {
        into.Add(go);
        foreach (var childT in go.Transform.Children)
            Collect(childT.GameObject, into);
    }

    private static void ClearSceneRefs(GameObject go)
    {
        go.Scene = null;
        foreach (var childT in go.Transform.Children)
            ClearSceneRefs(childT.GameObject);
    }
}
