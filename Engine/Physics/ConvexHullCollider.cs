using BepuPhysics;
using BepuPhysics.Collidables;

using OtkVec3 = OpenTK.Mathematics.Vector3;
using SnVec3  = System.Numerics.Vector3;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Convex-hull collider. Given an arbitrary point cloud, builds the smallest
/// convex volume that contains every point and registers that as the collision
/// shape. Suitable for dynamic bodies (unlike triangle-mesh colliders, which
/// must be static). Use for irregular props whose true shape isn't captured
/// well by boxes or capsules.
/// </summary>
public class ConvexHullCollider : Collider
{
    /// <summary>Points defining the hull. Duplicates + interior points are fine — the helper only keeps the outer surface.</summary>
    public OtkVec3[] Points = System.Array.Empty<OtkVec3>();

    public override (TypedIndex Shape, BodyInertia Inertia) BuildShape(PhysicsWorld world, float mass)
    {
        if (Points.Length < 4)
        {
            Console.WriteLine($"[ConvexHullCollider] '{GameObject.Name}': need at least 4 points for a hull; using a unit sphere fallback.");
            var sphere = new Sphere(0.5f);
            return (world.Simulation.Shapes.Add(sphere), sphere.ComputeInertia(mass));
        }

        // Convert to a Buffer<Vector3> from Bepu's pool.
        world.BufferPool.Take<SnVec3>(Points.Length, out var buffer);
        for (int i = 0; i < Points.Length; i++)
            buffer[i] = new SnVec3(Points[i].X, Points[i].Y, Points[i].Z);

        ConvexHullHelper.CreateShape(buffer.Slice(0, Points.Length), world.BufferPool, out _, out var hull);
        var shape = world.Simulation.Shapes.Add(hull);
        var inertia = hull.ComputeInertia(mass);

        world.BufferPool.Return(ref buffer);
        return (shape, inertia);
    }
}
