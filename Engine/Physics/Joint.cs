using BepuPhysics;
using BepuPhysics.Constraints;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Base class for constraint-backed joints between two rigidbodies. Concrete
/// subclasses (<see cref="FixedJoint"/>, <see cref="HingeJoint"/>, etc.) supply
/// the actual BepuPhysics constraint config; this class handles the shared
/// bookkeeping: finding both rigidbodies, adding the constraint on Awake,
/// removing it on OnDestroy.
///
/// Both endpoints must already have a <see cref="Rigidbody"/> registered with
/// the simulation. The joint waits one Awake pass so sibling Rigidbodies are
/// guaranteed to be added first.
/// </summary>
public abstract class Joint : Component
{
    /// <summary>The "other" GameObject this joint connects to. Must have a Rigidbody.</summary>
    public GameObject? ConnectedBody;

    /// <summary>Contact stiffness / damping settings shared by every constraint variant.</summary>
    public SpringSettings Spring = new(30f, 1f);

    protected PhysicsWorld? World;
    protected ConstraintHandle Handle;
    protected bool IsRegistered;

    public override void Awake()
    {
        var selfRb = GameObject.GetComponent<Rigidbody>();
        var otherRb = ConnectedBody?.GetComponent<Rigidbody>();
        if (selfRb == null || otherRb == null)
        {
            Console.WriteLine($"[Joint] '{GameObject.Name}': both ends need a Rigidbody. Skipping.");
            return;
        }

        World = GameObject.Scene?.FindComponent<PhysicsWorld>();
        if (World == null) return;

        var aHandle = selfRb.GetBodyHandle();
        var bHandle = otherRb.GetBodyHandle();
        if (aHandle == null || bHandle == null)
        {
            Console.WriteLine($"[Joint] '{GameObject.Name}': one or both bodies are static or unregistered. Skipping.");
            return;
        }

        Handle = Configure(World.Simulation, aHandle.Value, bHandle.Value);
        IsRegistered = true;
    }

    /// <summary>
    /// Add the concrete constraint to the simulation and return its handle. Called
    /// from <see cref="Awake"/> once both endpoint body handles are known.
    /// </summary>
    protected abstract ConstraintHandle Configure(Simulation sim, BodyHandle a, BodyHandle b);

    public override void OnDestroy()
    {
        if (IsRegistered && World != null)
        {
            World.Simulation.Solver.Remove(Handle);
            IsRegistered = false;
        }
    }
}
