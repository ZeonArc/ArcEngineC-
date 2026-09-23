using BepuPhysics;
using BepuPhysics.Constraints;

using OtkVec3 = OpenTK.Mathematics.Vector3;
using OtkQuat = OpenTK.Mathematics.Quaternion;
using SnVec3  = System.Numerics.Vector3;
using SnQuat  = System.Numerics.Quaternion;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Rigid "weld" between two bodies — no relative motion allowed. Handy for
/// snapping props together or attaching a helmet to a head.
/// </summary>
public class FixedJoint : Joint
{
    /// <summary>Offset from body A's frame where the weld is anchored.</summary>
    public OtkVec3 LocalOffset = OtkVec3.Zero;

    /// <summary>Local orientation body B should hold, relative to A.</summary>
    public OtkQuat LocalOrientation = OtkQuat.Identity;

    protected override ConstraintHandle Configure(Simulation sim, BodyHandle a, BodyHandle b)
    {
        var weld = new Weld
        {
            LocalOffset = new SnVec3(LocalOffset.X, LocalOffset.Y, LocalOffset.Z),
            LocalOrientation = new SnQuat(LocalOrientation.X, LocalOrientation.Y, LocalOrientation.Z, LocalOrientation.W),
            SpringSettings = Spring,
        };
        return sim.Solver.Add(a, b, weld);
    }
}
