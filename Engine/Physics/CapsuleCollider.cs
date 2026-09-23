using BepuPhysics;
using BepuPhysics.Collidables;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Capsule-shaped collider. <see cref="Radius"/> is the hemispherical cap radius;
/// <see cref="Length"/> is the length of the cylindrical middle section (i.e., the
/// distance between the two cap centers). Total height along the Y axis is
/// <c>Length + 2 * Radius</c>. Capsules are the standard shape for character
/// controllers and thin upright props.
/// </summary>
public class CapsuleCollider : Collider
{
    public float Radius = 0.5f;
    public float Length = 1.0f;

    public override (TypedIndex Shape, BodyInertia Inertia) BuildShape(PhysicsWorld world, float mass)
    {
        var capsule = new Capsule(Radius, Length);
        var shape = world.Simulation.Shapes.Add(capsule);
        var inertia = capsule.ComputeInertia(mass);
        return (shape, inertia);
    }
}
