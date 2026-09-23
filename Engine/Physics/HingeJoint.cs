using BepuPhysics;
using BepuPhysics.Constraints;

using OtkVec3 = OpenTK.Mathematics.Vector3;
using SnVec3  = System.Numerics.Vector3;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Constrains two bodies to share a single axis of relative rotation — the
/// classic "door hinge". Both bodies keep their positional constraint at the
/// hinge point (offset from each body's origin) and are free to rotate about
/// <see cref="HingeAxis"/>.
/// </summary>
public class HingeJoint : Joint
{
    /// <summary>Hinge attachment in body A's local frame.</summary>
    public OtkVec3 LocalOffsetA = OtkVec3.Zero;

    /// <summary>Hinge attachment in body B's local frame.</summary>
    public OtkVec3 LocalOffsetB = OtkVec3.Zero;

    /// <summary>Rotation axis (both bodies' local space; unit vector).</summary>
    public OtkVec3 HingeAxis = OtkVec3.UnitY;

    protected override ConstraintHandle Configure(Simulation sim, BodyHandle a, BodyHandle b)
    {
        var axis = OtkVec3.Normalize(HingeAxis);
        var hinge = new Hinge
        {
            LocalHingeAxisA = new SnVec3(axis.X, axis.Y, axis.Z),
            LocalHingeAxisB = new SnVec3(axis.X, axis.Y, axis.Z),
            LocalOffsetA    = new SnVec3(LocalOffsetA.X, LocalOffsetA.Y, LocalOffsetA.Z),
            LocalOffsetB    = new SnVec3(LocalOffsetB.X, LocalOffsetB.Y, LocalOffsetB.Z),
            SpringSettings  = Spring,
        };
        return sim.Solver.Add(a, b, hinge);
    }
}
