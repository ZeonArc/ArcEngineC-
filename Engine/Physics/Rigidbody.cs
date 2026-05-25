using OpenTK.Mathematics;

using BepuPhysics;
using BepuPhysics.Collidables;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Math;

using SnVec3 = System.Numerics.Vector3;
using SnQuat = System.Numerics.Quaternion;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Rigid-body component. In <see cref="Awake"/>, finds the sibling <see cref="Collider"/>
/// and the scene's <see cref="PhysicsWorld"/>, then registers either a dynamic body or a
/// static depending on <see cref="IsStatic"/>. In <see cref="LateUpdate"/>, dynamic bodies
/// pull their pose from the simulation and write it back to the GameObject's Transform.
/// </summary>
public class Rigidbody : Component
{
    /// <summary>Body mass (ignored when <see cref="IsStatic"/> is true).</summary>
    public float Mass = 1f;

    /// <summary>If true, the body is fixed (e.g. ground, wall) and does not move.</summary>
    public bool IsStatic = false;

    private PhysicsWorld? _world;
    private bool _isDynamicRegistered;
    private bool _isStaticRegistered;
    private BodyHandle _bodyHandle;
    private StaticHandle _staticHandle;

    public override void Awake()
    {
        var collider = GameObject.GetComponent<Collider>();
        if (collider == null)
        {
            Console.WriteLine($"[Rigidbody] '{GameObject.Name}' has no Collider sibling — body not registered.");
            return;
        }

        _world = GameObject.Scene?.FindComponent<PhysicsWorld>();
        if (_world == null)
        {
            Console.WriteLine($"[Rigidbody] '{GameObject.Name}': no PhysicsWorld in scene — body not registered.");
            return;
        }

        // Initial pose taken from the GameObject's Transform.
        SnVec3 position = Transform.Position.ToNumerics();
        SnQuat orientation = MathConversions.EulerDegreesToQuaternion(Transform.Rotation).ToNumerics();
        var pose = new RigidPose(position, orientation);

        // Bepu wants per-mass inertia for dynamics — Collider computes it from its shape.
        var (shape, inertia) = collider.BuildShape(_world, Mass);

        if (IsStatic)
        {
            _staticHandle = _world.Simulation.Statics.Add(new StaticDescription(pose, shape));
            _isStaticRegistered = true;
        }
        else
        {
            var description = BodyDescription.CreateDynamic(
                pose,
                inertia,
                new CollidableDescription(shape, 0.1f),
                new BodyActivityDescription(0.01f));
            _bodyHandle = _world.Simulation.Bodies.Add(description);
            _isDynamicRegistered = true;
        }
    }

    public override void LateUpdate(float deltaTime)
    {
        // Only dynamic bodies move; statics keep their authored Transform.
        if (!_isDynamicRegistered || _world == null) return;

        var pose = _world.Simulation.Bodies[_bodyHandle].Pose;
        Transform.Position = pose.Position.ToOpenTK();
        Transform.Rotation = MathConversions.QuaternionToEulerDegrees(pose.Orientation.ToOpenTK());
    }

    public override void OnDestroy()
    {
        if (_world == null) return;

        if (_isDynamicRegistered) _world.Simulation.Bodies.Remove(_bodyHandle);
        if (_isStaticRegistered) _world.Simulation.Statics.Remove(_staticHandle);

        _isDynamicRegistered = false;
        _isStaticRegistered = false;
    }

    /// <summary>
    /// Push the current <see cref="Transform"/> pose into the underlying BepuPhysics body
    /// and zero its velocity. Used by the editor's Play→Stop restore so the crate (etc.)
    /// snaps back to its pre-Play position instead of continuing from where physics left it.
    /// No-op for statics or for bodies that never registered.
    /// </summary>
    public void SyncToTransform()
    {
        if (!_isDynamicRegistered || _world == null) return;

        SnVec3 pos = Transform.Position.ToNumerics();
        SnQuat orient = MathConversions.EulerDegreesToQuaternion(Transform.Rotation).ToNumerics();

        var bodyRef = _world.Simulation.Bodies[_bodyHandle];
        bodyRef.Pose = new RigidPose(pos, orient);
        bodyRef.Velocity = new BodyVelocity(default, default);
        bodyRef.Awake = true;
    }
}
