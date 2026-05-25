using OpenTK.Mathematics;

using BepuPhysics;
using BepuPhysics.Collidables;

namespace ArcEngine.Engine.Physics;

/// <summary>Axis-aligned box collider. Size is the full extent (not half-extent).</summary>
public class BoxCollider : Collider
{
    public Vector3 Size = Vector3.One;

    public override (TypedIndex Shape, BodyInertia Inertia) BuildShape(PhysicsWorld world, float mass)
    {
        var box = new Box(Size.X, Size.Y, Size.Z);
        var shape = world.Simulation.Shapes.Add(box);
        var inertia = box.ComputeInertia(mass);
        return (shape, inertia);
    }
}
