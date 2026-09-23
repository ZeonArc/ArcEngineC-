using System.Numerics;

using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;

using ArcEngine.Engine.Core;

using OtkVec3 = OpenTK.Mathematics.Vector3;
using SnVec3  = System.Numerics.Vector3;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Result of a raycast or shape-sweep. Coordinates and normal are in engine (OpenTK)
/// space; <see cref="GameObject"/> is the owning object of whichever collider was hit
/// (null if the collider had no registered <see cref="Rigidbody"/>).
/// </summary>
public struct RaycastHit
{
    public GameObject? GameObject;
    public Rigidbody?  Rigidbody;
    public OtkVec3     Point;
    public OtkVec3     Normal;
    public float       Distance;
}

/// <summary>
/// Raycast + overlap query helpers on <see cref="PhysicsWorld"/>. Kept in extension
/// methods so the core <c>PhysicsWorld</c> class stays focused on simulation lifecycle
/// and the Bepu callback plumbing (which needs unmanaged struct handlers) stays
/// self-contained here.
/// </summary>
public static class PhysicsQueries
{
    // ============================================================================
    // Raycast — closest hit within maxDistance along a normalized direction.
    // ============================================================================

    /// <summary>
    /// Cast a ray from <paramref name="origin"/> along <paramref name="direction"/> up
    /// to <paramref name="maxDistance"/> units. Returns true and populates
    /// <paramref name="hit"/> with the closest collider along the ray; false if the
    /// ray hits nothing. <paramref name="direction"/> does not need to be normalized —
    /// distance is measured in ray-parameter units, which matches world distance when
    /// the direction is unit-length.
    /// </summary>
    public static bool Raycast(
        this PhysicsWorld world,
        OtkVec3 origin,
        OtkVec3 direction,
        float maxDistance,
        out RaycastHit hit)
    {
        var handler = new ClosestRayHitHandler
        {
            HitT = float.MaxValue,
            HitNormal = default,
            HitCollidable = default,
            AnyHit = false,
        };

        world.Simulation.RayCast(
            new SnVec3(origin.X, origin.Y, origin.Z),
            new SnVec3(direction.X, direction.Y, direction.Z),
            maxDistance,
            ref handler);

        if (!handler.AnyHit)
        {
            hit = default;
            return false;
        }

        Rigidbody? rb = ResolveOwner(world, handler.HitCollidable);
        var dir = new OtkVec3(direction.X, direction.Y, direction.Z);
        hit = new RaycastHit
        {
            GameObject = rb?.GameObject,
            Rigidbody  = rb,
            Distance   = handler.HitT,
            Point      = origin + dir * handler.HitT,
            Normal     = new OtkVec3(handler.HitNormal.X, handler.HitNormal.Y, handler.HitNormal.Z),
        };
        return true;
    }

    private struct ClosestRayHitHandler : IRayHitHandler
    {
        public float HitT;
        public SnVec3 HitNormal;
        public CollidableReference HitCollidable;
        public bool AnyHit;

        public bool AllowTest(CollidableReference collidable) => true;
        public bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnRayHit(
            in RayData ray, ref float maximumT, float t,
            in SnVec3 normal, CollidableReference collidable, int childIndex)
        {
            if (t >= HitT) return;
            HitT = t;
            HitNormal = normal;
            HitCollidable = collidable;
            AnyHit = true;
            // Shrink maximumT so Bepu prunes farther candidates for us.
            maximumT = t;
        }
    }

    // ============================================================================
    // Sphere overlap — center-to-center test against every registered rigidbody.
    // ============================================================================

    /// <summary>
    /// Collect every registered <see cref="Rigidbody"/> whose current body-center
    /// position lies within <paramref name="radius"/> of <paramref name="center"/>.
    /// Populates <paramref name="results"/> without allocating; caller may preserve or
    /// reuse the list across calls.
    /// <para>
    /// This is a center-point test, not a shape-vs-shape overlap: a body's actual
    /// collision volume can extend past the sphere while its pose sits outside it,
    /// and vice versa. Adequate for coarse proximity queries (AI perception ranges,
    /// pickup radii); use a proper shape sweep once one is added for tight overlaps.
    /// </para>
    /// </summary>
    public static void OverlapSphere(
        this PhysicsWorld world,
        OtkVec3 center,
        float radius,
        List<GameObject> results)
    {
        results.Clear();
        if (radius <= 0f) return;
        float r2 = radius * radius;

        // Dynamic bodies — check pose.Position against the sphere.
        foreach (var rb in world.GameObject.Scene?.FindComponents<Rigidbody>() ?? Array.Empty<Rigidbody>())
        {
            var p = rb.Transform.Position - center;
            if (p.LengthSquared <= r2 && rb.GameObject != null)
                results.Add(rb.GameObject);
        }
    }

    // ============================================================================
    // Internal helpers
    // ============================================================================

    private static Rigidbody? ResolveOwner(PhysicsWorld world, CollidableReference reference)
    {
        return reference.Mobility switch
        {
            CollidableMobility.Static => world.RigidbodyFor(reference.StaticHandle),
            _ => world.RigidbodyFor(reference.BodyHandle),   // Dynamic + Kinematic
        };
    }
}
