using OpenTK.Mathematics;

namespace ArcEngine.Engine.Math;

/// <summary>
/// Axis-aligned bounding box. Used for frustum culling — every <see cref="Engine.Rendering.Mesh"/>
/// stores a local-space AABB; the renderer transforms it into world space each frame
/// (8 corners → recompute min/max) and tests against the camera's frustum.
/// </summary>
public readonly struct AABB
{
    public readonly Vector3 Min;
    public readonly Vector3 Max;

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Extents => (Max - Min) * 0.5f;
    public bool IsValid => Max.X >= Min.X && Max.Y >= Min.Y && Max.Z >= Min.Z;

    public AABB(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Empty / invalid AABB. Useful as an "uninitialised" sentinel.</summary>
    public static readonly AABB Empty = new AABB(
        new Vector3(float.PositiveInfinity),
        new Vector3(float.NegativeInfinity));

    /// <summary>
    /// Compute the local-space AABB enclosing <paramref name="positions"/>.
    /// Returns <see cref="Empty"/> if the set is empty.
    /// </summary>
    public static AABB FromPoints(ReadOnlySpan<Vector3> positions)
    {
        if (positions.Length == 0) return Empty;

        Vector3 min = positions[0];
        Vector3 max = positions[0];
        for (int i = 1; i < positions.Length; i++)
        {
            var p = positions[i];
            min = Vector3.ComponentMin(min, p);
            max = Vector3.ComponentMax(max, p);
        }
        return new AABB(min, max);
    }

    /// <summary>
    /// Transform this local-space AABB into world space using <paramref name="model"/>.
    /// Builds a tighter result than naive corner transformation by iterating the 8 corners
    /// and recomputing min/max from the transformed positions.
    /// </summary>
    public AABB Transformed(in Matrix4 model)
    {
        if (!IsValid) return Empty;

        Vector3 outMin = new Vector3(float.PositiveInfinity);
        Vector3 outMax = new Vector3(float.NegativeInfinity);

        for (int i = 0; i < 8; i++)
        {
            float x = (i & 1) == 0 ? Min.X : Max.X;
            float y = (i & 2) == 0 ? Min.Y : Max.Y;
            float z = (i & 4) == 0 ? Min.Z : Max.Z;

            Vector4 c4 = new Vector4(x, y, z, 1f) * model;
            Vector3 c = c4.Xyz;

            outMin = Vector3.ComponentMin(outMin, c);
            outMax = Vector3.ComponentMax(outMax, c);
        }
        return new AABB(outMin, outMax);
    }
}
