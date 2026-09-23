using BepuPhysics;
using BepuPhysics.Constraints;

using OtkVec3 = OpenTK.Mathematics.Vector3;
using SnVec3  = System.Numerics.Vector3;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Constrains body B to move only along a single axis relative to body A —
/// Bepu's <c>LinearAxisServo</c>. Useful for drawers, pistons, elevators.
///
/// Note: this constraint controls linear motion only; rotational freedom is not
/// removed. Pair with a hinge or angular constraint if you need to lock B's
/// orientation to A.
/// </summary>
public class SliderJoint : Joint
{
    public OtkVec3 LocalOffsetA = OtkVec3.Zero;
    public OtkVec3 LocalOffsetB = OtkVec3.Zero;

    /// <summary>Sliding axis in A's local frame (unit vector).</summary>
    public OtkVec3 Axis = OtkVec3.UnitX;

    /// <summary>Signed target offset along <see cref="Axis"/> that the servo pulls toward.</summary>
    public float TargetOffset = 0f;

    protected override ConstraintHandle Configure(Simulation sim, BodyHandle a, BodyHandle b)
    {
        var axis = OtkVec3.Normalize(Axis);
        var servo = new LinearAxisServo
        {
            LocalPlaneNormal = new SnVec3(axis.X, axis.Y, axis.Z),
            LocalOffsetA = new SnVec3(LocalOffsetA.X, LocalOffsetA.Y, LocalOffsetA.Z),
            LocalOffsetB = new SnVec3(LocalOffsetB.X, LocalOffsetB.Y, LocalOffsetB.Z),
            TargetOffset = TargetOffset,
            SpringSettings = Spring,
            ServoSettings = new ServoSettings { MaximumSpeed = float.MaxValue, BaseSpeed = 0f, MaximumForce = float.MaxValue },
        };
        return sim.Solver.Add(a, b, servo);
    }
}
