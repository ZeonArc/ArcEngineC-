using BepuPhysics;
using BepuPhysics.Collidables;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Component that describes a collision shape. Subclasses register their concrete
/// Bepu shape with the <see cref="PhysicsWorld"/>'s <c>Simulation.Shapes</c> on demand;
/// <see cref="Rigidbody"/> queries this in its Awake to register a body or static.
/// </summary>
public abstract class Collider : Component
{
    /// <summary>
    /// Register this collider's shape with the physics world's shape registry and
    /// compute the per-mass inertia tensor (only used for dynamic bodies).
    /// </summary>
    public abstract (TypedIndex Shape, BodyInertia Inertia) BuildShape(PhysicsWorld world, float mass);
}
