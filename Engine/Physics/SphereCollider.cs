using BepuPhysics;
using BepuPhysics.Collidables;

namespace ArcEngine.Engine.Physics;

public class SphereCollider : Collider
{
    public float Radius = 0.5f;

    public override (TypedIndex Shape, BodyInertia Inertia) BuildShape(PhysicsWorld world, float mass)
    {
        var sphere = new Sphere(Radius);
        var shape = world.Simulation.Shapes.Add(sphere);
        var inertia = sphere.ComputeInertia(mass);
        return (shape, inertia);
    }
}
