// Aliases to keep this file readable — both namespaces declare types named
// Vector3, Quaternion, etc.
using OtkVec3 = OpenTK.Mathematics.Vector3;
using OtkQuat = OpenTK.Mathematics.Quaternion;
using SnVec3 = System.Numerics.Vector3;
using SnQuat = System.Numerics.Quaternion;

using OpenTK.Mathematics;

namespace ArcEngine.Engine.Math;

/// <summary>
/// Helpers for moving vectors and quaternions between OpenTK (rendering) and
/// System.Numerics (BepuPhysics). All conversions are component-wise — the two
/// libraries agree on coordinate conventions for these basic types.
/// </summary>
public static class MathConversions
{
    public static SnVec3 ToNumerics(this OtkVec3 v) => new(v.X, v.Y, v.Z);
    public static OtkVec3 ToOpenTK(this SnVec3 v) => new(v.X, v.Y, v.Z);

    public static SnQuat ToNumerics(this OtkQuat q) => new(q.X, q.Y, q.Z, q.W);
    public static OtkQuat ToOpenTK(this SnQuat q) => new(q.X, q.Y, q.Z, q.W);

    /// <summary>
    /// Convert an Euler-angle rotation in <b>degrees</b> (Transform.Rotation convention)
    /// to a unit quaternion. Order matches Transform's S * Rz * Ry * Rx * T model
    /// matrix convention: the resulting orientation, when applied to a vector, is
    /// equivalent to rotating around X first, then Y, then Z.
    /// </summary>
    public static OtkQuat EulerDegreesToQuaternion(OtkVec3 eulerDegrees)
    {
        float rx = MathHelper.DegreesToRadians(eulerDegrees.X);
        float ry = MathHelper.DegreesToRadians(eulerDegrees.Y);
        float rz = MathHelper.DegreesToRadians(eulerDegrees.Z);

        // Build component quaternions and compose: q = qz * qy * qx (X first).
        var qx = OtkQuat.FromAxisAngle(OtkVec3.UnitX, rx);
        var qy = OtkQuat.FromAxisAngle(OtkVec3.UnitY, ry);
        var qz = OtkQuat.FromAxisAngle(OtkVec3.UnitZ, rz);
        return qz * qy * qx;
    }

    /// <summary>
    /// Convert a unit quaternion back to Euler degrees in the same X→Y→Z order.
    /// Used to write physics orientation back into <c>Transform.Rotation</c>.
    /// </summary>
    public static OtkVec3 QuaternionToEulerDegrees(OtkQuat q)
    {
        // Standard YXZ-friendly extraction adapted to our XYZ convention.
        // Reference: Shoemake / Wikipedia "Conversion between quaternions and Euler angles".
        float x, y, z;

        // Pitch (X)
        float sinp = 2f * (q.W * q.X - q.Y * q.Z);
        if (System.MathF.Abs(sinp) >= 1f)
            x = System.MathF.CopySign(System.MathF.PI / 2f, sinp); // gimbal lock at the poles
        else
            x = System.MathF.Asin(sinp);

        // Yaw (Y)
        float siny_cosp = 2f * (q.W * q.Y + q.X * q.Z);
        float cosy_cosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        y = System.MathF.Atan2(siny_cosp, cosy_cosp);

        // Roll (Z)
        float sinr_cosp = 2f * (q.W * q.Z + q.X * q.Y);
        float cosr_cosp = 1f - 2f * (q.X * q.X + q.Z * q.Z);
        z = System.MathF.Atan2(sinr_cosp, cosr_cosp);

        return new OtkVec3(
            MathHelper.RadiansToDegrees(x),
            MathHelper.RadiansToDegrees(y),
            MathHelper.RadiansToDegrees(z));
    }
}
