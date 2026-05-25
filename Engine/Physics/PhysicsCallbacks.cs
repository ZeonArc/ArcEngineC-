using System.Numerics;
using System.Runtime.CompilerServices;

using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Bepu narrow-phase callbacks. Allows all collision pairs and configures default
/// material properties (friction + restitution + spring stiffness for contact).
/// Standard minimal form from BepuPhysics demos.
/// </summary>
public struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public SpringSettings ContactSpringiness;
    public float MaximumRecoveryVelocity;
    public float FrictionCoefficient;

    public NarrowPhaseCallbacks(
        SpringSettings contactSpringiness,
        float maximumRecoveryVelocity = 2f,
        float frictionCoefficient = 1f)
    {
        ContactSpringiness = contactSpringiness;
        MaximumRecoveryVelocity = maximumRecoveryVelocity;
        FrictionCoefficient = frictionCoefficient;
    }

    public void Initialize(Simulation simulation)
    {
        // Sensible defaults if the caller used the parameterless ctor.
        if (ContactSpringiness.AngularFrequency == 0f && ContactSpringiness.TwiceDampingRatio == 0f)
        {
            ContactSpringiness = new SpringSettings(30f, 1f);
            MaximumRecoveryVelocity = 2f;
            FrictionCoefficient = 1f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
        => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold<TManifold>(
        int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial.FrictionCoefficient = FrictionCoefficient;
        pairMaterial.MaximumRecoveryVelocity = MaximumRecoveryVelocity;
        pairMaterial.SpringSettings = ContactSpringiness;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
        => true;

    public void Dispose() { }
}

/// <summary>
/// Bepu pose-integrator callbacks. Applies a global gravity vector and optional
/// linear / angular damping each substep. Standard minimal form from Bepu demos.
/// </summary>
public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;
    public float LinearDamping;
    public float AngularDamping;

    private Vector3Wide _gravityWideDt;
    private Vector<float> _linearDampingDt;
    private Vector<float> _angularDampingDt;

    public PoseIntegratorCallbacks(Vector3 gravity, float linearDamping = 0.03f, float angularDamping = 0.03f) : this()
    {
        Gravity = gravity;
        LinearDamping = linearDamping;
        AngularDamping = angularDamping;
    }

    public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public bool AllowSubstepsForUnconstrainedBodies => false;
    public bool IntegrateVelocityForKinematics => false;

    public void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt)
    {
        // Pre-multiply the per-substep gravity + damping factors so IntegrateVelocity
        // can do branch-free SIMD-friendly application.
        _linearDampingDt = new Vector<float>(System.MathF.Pow(MathHelper.Clamp(1f - LinearDamping, 0f, 1f), dt));
        _angularDampingDt = new Vector<float>(System.MathF.Pow(MathHelper.Clamp(1f - AngularDamping, 0f, 1f), dt));
        _gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IntegrateVelocity(
        Vector<int> bodyIndices,
        Vector3Wide position, QuaternionWide orientation,
        BodyInertiaWide localInertia, Vector<int> integrationMask,
        int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        velocity.Linear  = (velocity.Linear  + _gravityWideDt) * _linearDampingDt;
        velocity.Angular =  velocity.Angular                    * _angularDampingDt;
    }
}
