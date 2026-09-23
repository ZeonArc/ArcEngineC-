using BepuPhysics;
using BepuPhysics.Collidables;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Per-frame trigger overlap tracker. Populated during BepuPhysics narrowphase
/// callbacks (when either side of a pair has <see cref="Rigidbody.IsTrigger"/>
/// set), then diffed against the previous frame in <see cref="Flush"/> to fire
/// OnTriggerEnter / OnTriggerStay / OnTriggerExit callbacks on components.
///
/// Global (single-threaded physics for now; if we ever multi-thread the sim
/// this needs per-worker buffers merged post-step).
/// </summary>
internal static class TriggerBookkeeping
{
    /// <summary>Compact key identifying one collidable — body/static handle + kind bit.</summary>
    private readonly struct Key : IEquatable<Key>
    {
        public readonly int  Handle;
        public readonly bool IsStatic;
        public Key(int handle, bool isStatic) { Handle = handle; IsStatic = isStatic; }
        public bool Equals(Key other) => Handle == other.Handle && IsStatic == other.IsStatic;
        public override bool Equals(object? obj) => obj is Key k && Equals(k);
        public override int GetHashCode() => (Handle * 397) ^ (IsStatic ? 1 : 0);
    }

    /// <summary>Ordered pair (small key first) to give the set stable membership.</summary>
    private readonly struct Pair : IEquatable<Pair>
    {
        public readonly Key A, B;
        public Pair(Key a, Key b)
        {
            if (a.Handle < b.Handle || (a.Handle == b.Handle && !a.IsStatic && b.IsStatic))
            { A = a; B = b; }
            else
            { A = b; B = a; }
        }
        public bool Equals(Pair other) => A.Equals(other.A) && B.Equals(other.B);
        public override bool Equals(object? obj) => obj is Pair p && Equals(p);
        public override int GetHashCode() => A.GetHashCode() ^ (B.GetHashCode() * 31);
    }

    private static readonly HashSet<Pair> s_current  = new();
    private static readonly HashSet<Pair> s_previous = new();

    /// <summary>
    /// Called by the narrowphase whenever an AABB-overlapping pair has at least
    /// one trigger side. Returns whether the pair should still generate contact
    /// (always false for a trigger — the flag suppresses contact forces).
    /// </summary>
    internal static void ReportOverlap(CollidableReference a, CollidableReference b)
    {
        var ka = new Key(a.Mobility == CollidableMobility.Static ? a.StaticHandle.Value : a.BodyHandle.Value,
                         a.Mobility == CollidableMobility.Static);
        var kb = new Key(b.Mobility == CollidableMobility.Static ? b.StaticHandle.Value : b.BodyHandle.Value,
                         b.Mobility == CollidableMobility.Static);
        s_current.Add(new Pair(ka, kb));
    }

    /// <summary>
    /// Fire OnTriggerEnter/Stay/Exit callbacks based on the diff between last
    /// frame's overlap set and this frame's. Call once per physics step after
    /// <see cref="Simulation.Timestep"/> returns.
    /// </summary>
    internal static void Flush(PhysicsWorld world)
    {
        // Enter + Stay
        foreach (var pair in s_current)
        {
            var rbA = ResolveRigidbody(world, pair.A);
            var rbB = ResolveRigidbody(world, pair.B);
            if (rbA == null || rbB == null) continue;

            bool isNew = !s_previous.Contains(pair);
            DispatchTriggerPair(rbA, rbB, isNew ? Kind.Enter : Kind.Stay);
        }

        // Exit — pairs in previous but not in current.
        foreach (var pair in s_previous)
        {
            if (s_current.Contains(pair)) continue;
            var rbA = ResolveRigidbody(world, pair.A);
            var rbB = ResolveRigidbody(world, pair.B);
            if (rbA == null || rbB == null) continue;
            DispatchTriggerPair(rbA, rbB, Kind.Exit);
        }

        // Rotate the buffers so next frame diffs against this one.
        s_previous.Clear();
        foreach (var p in s_current) s_previous.Add(p);
        s_current.Clear();
    }

    /// <summary>Drop all overlap state — call when the scene reloads / physics resets.</summary>
    internal static void Reset()
    {
        s_current.Clear();
        s_previous.Clear();
    }

    private enum Kind { Enter, Stay, Exit }

    private static void DispatchTriggerPair(Rigidbody a, Rigidbody b, Kind kind)
    {
        // Only fire the callback on the TRIGGER side's owner — non-trigger side
        // receives no callback (matches Unity's convention).
        if (a.IsTrigger) FireOn(a, b, kind);
        if (b.IsTrigger) FireOn(b, a, kind);
    }

    private static void FireOn(Rigidbody trigger, Rigidbody other, Kind kind)
    {
        var comps = trigger.GameObject.Components;
        for (int i = 0; i < comps.Count; i++)
        {
            var c = comps[i];
            switch (kind)
            {
                case Kind.Enter: c.OnTriggerEnter(other); break;
                case Kind.Stay:  c.OnTriggerStay(other);  break;
                case Kind.Exit:  c.OnTriggerExit(other);  break;
            }
        }
    }

    private static Rigidbody? ResolveRigidbody(PhysicsWorld world, Key key)
    {
        return key.IsStatic
            ? world.RigidbodyFor(new StaticHandle(key.Handle))
            : world.RigidbodyFor(new BodyHandle(key.Handle));
    }
}
