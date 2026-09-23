using System.Numerics;

using BepuPhysics;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Component that owns the BepuPhysics <see cref="Simulation"/> and drives it with a
/// fixed-timestep accumulator. Exactly one PhysicsWorld is expected per scene; the
/// <see cref="Rigidbody"/> component finds it via <c>Scene.FindComponent</c> in Awake.
///
/// Add a PhysicsWorld GameObject to the scene <i>before</i> any GameObject with a
/// Rigidbody — Awake order follows scene insertion order.
/// </summary>
public class PhysicsWorld : Component
{
    /// <summary>Simulation step size (seconds). 60 Hz is standard for stable rigid-body sim.</summary>
    public const float FixedTimestep = 1f / 60f;

    /// <summary>Maximum number of physics steps per frame to avoid spiral-of-death after a hitch.</summary>
    public const int MaxStepsPerFrame = 4;

    public Simulation Simulation = null!;

    /// <summary>Bepu buffer pool exposed for shape construction (ConvexHullHelper, Mesh, etc.).</summary>
    internal BufferPool BufferPool => _bufferPool;

    private BufferPool _bufferPool = null!;
    private ThreadDispatcher _threadDispatcher = null!;
    private float _accumulator;

    // Reverse lookup: which Rigidbody component owns each Bepu handle. Populated by
    // Rigidbody.Awake and cleared by Rigidbody.OnDestroy. Used by raycast/overlap
    // queries to map hits back to a GameObject.
    private readonly Dictionary<int, Rigidbody> _bodyOwners = new();
    private readonly Dictionary<int, Rigidbody> _staticOwners = new();

    internal void RegisterBody(BodyHandle handle, Rigidbody rb) => _bodyOwners[handle.Value] = rb;
    internal void RegisterStatic(StaticHandle handle, Rigidbody rb) => _staticOwners[handle.Value] = rb;
    internal void UnregisterBody(BodyHandle handle) => _bodyOwners.Remove(handle.Value);
    internal void UnregisterStatic(StaticHandle handle) => _staticOwners.Remove(handle.Value);

    internal Rigidbody? RigidbodyFor(BodyHandle handle) =>
        _bodyOwners.TryGetValue(handle.Value, out var rb) ? rb : null;
    internal Rigidbody? RigidbodyFor(StaticHandle handle) =>
        _staticOwners.TryGetValue(handle.Value, out var rb) ? rb : null;

    public override void Awake()
    {
        _bufferPool = new BufferPool();

        // Single-threaded for now — plenty for this sprint's demo scope.
        _threadDispatcher = new ThreadDispatcher(1);

        var narrowphase = new NarrowPhaseCallbacks(
            contactSpringiness: new SpringSettings(30f, 1f),
            maximumRecoveryVelocity: 2f,
            frictionCoefficient: 1f);

        var poseIntegrator = new PoseIntegratorCallbacks(
            gravity: new Vector3(0f, -9.81f, 0f),
            linearDamping: 0.03f,
            angularDamping: 0.03f);

        Simulation = Simulation.Create(
            _bufferPool,
            narrowphase,
            poseIntegrator,
            new SolveDescription(velocityIterationCount: 8, substepCount: 1));
    }

    public override void Update(float deltaTime)
    {
        // Cap delta to avoid huge catch-ups after a debugger pause.
        if (deltaTime > 0.25f) deltaTime = 0.25f;

        _accumulator += deltaTime;
        int steps = 0;
        while (_accumulator >= FixedTimestep && steps < MaxStepsPerFrame)
        {
            Simulation.Timestep(FixedTimestep, _threadDispatcher);
            _accumulator -= FixedTimestep;
            steps++;
        }

        // Fire OnTriggerEnter/Stay/Exit on the pairs the narrowphase collected
        // during Timestep. Only one dispatch per PhysicsWorld.Update tick even
        // when the sim took multiple substeps — matches typical game engine
        // semantics of "one trigger event per frame".
        if (steps > 0) TriggerBookkeeping.Flush(this);
    }

    public override void OnDestroy()
    {
        TriggerBookkeeping.Reset();
        Simulation?.Dispose();
        _threadDispatcher?.Dispose();
        _bufferPool?.Clear();
    }
}
