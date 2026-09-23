using BepuPhysics;
using BepuPhysics.Constraints;

using OtkVec3 = OpenTK.Mathematics.Vector3;
using SnVec3  = System.Numerics.Vector3;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Elastic tether between two bodies — Bepu's <c>DistanceServo</c> tuned as a
/// spring. Keeps the local anchor points at a target distance, softly.
/// </summary>
public class SpringJoint : Joint
{
    public OtkVec3 LocalOffsetA = OtkVec3.Zero;
    public OtkVec3 LocalOffsetB = OtkVec3.Zero;

    /// <summary>Rest-length distance (world units).</summary>
    public float TargetDistance = 1f;

    /// <summary>Max positive/negative impulse the servo can apply per frame.</summary>
    public float MaxImpulse = float.MaxValue;

    protected override ConstraintHandle Configure(Simulation sim, BodyHandle a, BodyHandle b)
    {
        var servo = new DistanceServo
        {
            LocalOffsetA = new SnVec3(LocalOffsetA.X, LocalOffsetA.Y, LocalOffsetA.Z),
            LocalOffsetB = new SnVec3(LocalOffsetB.X, LocalOffsetB.Y, LocalOffsetB.Z),
            TargetDistance = TargetDistance,
            SpringSettings = Spring,
            ServoSettings = new ServoSettings { MaximumSpeed = float.MaxValue, BaseSpeed = 0f, MaximumForce = MaxImpulse },
        };
        return sim.Solver.Add(a, b, servo);
    }
}
