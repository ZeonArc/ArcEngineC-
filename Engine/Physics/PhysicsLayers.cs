using BepuPhysics;
using BepuPhysics.Collidables;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Global collision-matrix + per-handle layer registry consulted by the
/// narrowphase callbacks. Rigidbodies opt into layers via <c>Rigidbody.Layer</c>;
/// the matrix says which layer pairs actually collide.
///
/// Default matrix is fully permissive — every layer collides with every layer.
/// Configure exceptions by calling <see cref="SetPair"/> during scene setup.
///
/// Layers are indices 0..31, matching Unity's convention. There's no name
/// registry yet; add one if the editor ever needs a dropdown.
/// </summary>
public static class PhysicsLayers
{
    public const int LayerCount = 32;

    private static readonly bool[,] s_matrix = new bool[LayerCount, LayerCount];

    // Handle → layer index. Populated by Rigidbody.Awake, cleared in OnDestroy.
    private static readonly Dictionary<int, int> s_bodyLayer   = new();
    private static readonly Dictionary<int, int> s_staticLayer = new();

    // Trigger flags — mirrors <see cref="Rigidbody.IsTrigger"/> so the narrowphase
    // callback (which can't hold managed references) can consult it in-loop.
    private static readonly HashSet<int> s_bodyTriggers   = new();
    private static readonly HashSet<int> s_staticTriggers = new();

    static PhysicsLayers()
    {
        for (int i = 0; i < LayerCount; i++)
        for (int j = 0; j < LayerCount; j++)
            s_matrix[i, j] = true;
    }

    /// <summary>Set whether layers <paramref name="a"/> and <paramref name="b"/> collide (symmetric).</summary>
    public static void SetPair(int a, int b, bool collides)
    {
        if ((uint)a >= LayerCount || (uint)b >= LayerCount) return;
        s_matrix[a, b] = collides;
        s_matrix[b, a] = collides;
    }

    /// <summary>Reset the entire matrix to "everything collides with everything".</summary>
    public static void ResetMatrix()
    {
        for (int i = 0; i < LayerCount; i++)
        for (int j = 0; j < LayerCount; j++)
            s_matrix[i, j] = true;
    }

    // ============================================================================
    // Handle registration — called by Rigidbody at (un)register time.
    // ============================================================================

    internal static void RegisterBody(BodyHandle h, int layer, bool isTrigger = false)
    {
        s_bodyLayer[h.Value] = layer;
        if (isTrigger) s_bodyTriggers.Add(h.Value); else s_bodyTriggers.Remove(h.Value);
    }
    internal static void RegisterStatic(StaticHandle h, int layer, bool isTrigger = false)
    {
        s_staticLayer[h.Value] = layer;
        if (isTrigger) s_staticTriggers.Add(h.Value); else s_staticTriggers.Remove(h.Value);
    }
    internal static void UnregisterBody(BodyHandle h)
    {
        s_bodyLayer.Remove(h.Value);
        s_bodyTriggers.Remove(h.Value);
    }
    internal static void UnregisterStatic(StaticHandle h)
    {
        s_staticLayer.Remove(h.Value);
        s_staticTriggers.Remove(h.Value);
    }

    /// <summary>True if the given collidable belongs to a trigger-flagged Rigidbody.</summary>
    internal static bool IsTrigger(CollidableReference r)
    {
        return r.Mobility == CollidableMobility.Static
            ? s_staticTriggers.Contains(r.StaticHandle.Value)
            : s_bodyTriggers.Contains(r.BodyHandle.Value);
    }

    internal static int LayerOf(CollidableReference r)
    {
        if (r.Mobility == CollidableMobility.Static)
            return s_staticLayer.TryGetValue(r.StaticHandle.Value, out var l) ? l : 0;
        return s_bodyLayer.TryGetValue(r.BodyHandle.Value, out var lb) ? lb : 0;
    }

    /// <summary>True if the two collidables' layers currently allow collision.</summary>
    internal static bool CanCollide(CollidableReference a, CollidableReference b)
    {
        int la = LayerOf(a);
        int lb = LayerOf(b);
        if ((uint)la >= LayerCount || (uint)lb >= LayerCount) return true;
        return s_matrix[la, lb];
    }
}
