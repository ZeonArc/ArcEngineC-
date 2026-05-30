using OpenTK.Mathematics;

namespace ArcEngine.Engine.Math;

/// <summary>
/// View frustum represented as 6 planes (left/right/bottom/top/near/far), with each
/// plane's outward normal pointing OUTSIDE the frustum (so a positive signed distance
/// means "outside"). Extracted from a combined view-projection matrix using Gribb-Hartmann.
///
/// <see cref="Intersects(in AABB)"/> performs a fast n-vertex (positive-vertex) AABB test:
/// for each plane, find the AABB corner furthest along the plane's normal; if it's still
/// outside, the AABB cannot intersect. Conservative — may report some not-actually-visible
/// AABBs as visible (false positives near edges), never the reverse.
/// </summary>
public readonly struct Frustum
{
    private readonly Vector4 _l, _r, _b, _t, _n, _f;

    public Frustum(Vector4 l, Vector4 r, Vector4 b, Vector4 t, Vector4 n, Vector4 f)
    {
        _l = l; _r = r; _b = b; _t = t; _n = n; _f = f;
    }

    /// <summary>Build a frustum from a combined view-projection matrix.</summary>
    public static Frustum FromViewProjection(in Matrix4 vp)
    {
        // Gribb-Hartmann assumes a column-major matrix (clip = M * world). OpenTK is
        // row-major (clip = world * M); the same logical M is stored as its transpose
        // in OpenTK. Transposing the VP before reading rows recovers the math rows
        // that Gribb-Hartmann actually wants.
        Matrix4 m = Matrix4.Transpose(vp);

        Vector4 row0 = m.Row0;
        Vector4 row1 = m.Row1;
        Vector4 row2 = m.Row2;
        Vector4 row3 = m.Row3;

        // Gribb-Hartmann gives "inside" planes (positive distance = inside);
        // flip sign so we get the "outside" convention (positive distance = outside).
        Vector4 left   = -(row3 + row0);
        Vector4 right  = -(row3 - row0);
        Vector4 bottom = -(row3 + row1);
        Vector4 top    = -(row3 - row1);
        Vector4 near   = -(row3 + row2);
        Vector4 far    = -(row3 - row2);

        return new Frustum(
            Normalize(left),
            Normalize(right),
            Normalize(bottom),
            Normalize(top),
            Normalize(near),
            Normalize(far));
    }

    private static Vector4 Normalize(Vector4 plane)
    {
        // Plane is (a,b,c,d) with normal (a,b,c) and offset d. Normalise by the
        // length of the normal so signed-distance = dot(point.xyz, normal) + d.
        float len = plane.Xyz.Length;
        if (len < 1e-8f) return plane;
        return plane / len;
    }

    /// <summary>
    /// Conservative AABB-vs-frustum test: returns false only when the AABB is entirely
    /// outside at least one plane. Uses the "positive vertex" trick.
    /// </summary>
    public bool Intersects(in AABB box)
    {
        if (!box.IsValid) return false;

        return PlaneTest(_l, box) && PlaneTest(_r, box)
            && PlaneTest(_b, box) && PlaneTest(_t, box)
            && PlaneTest(_n, box) && PlaneTest(_f, box);
    }

    private static bool PlaneTest(in Vector4 plane, in AABB box)
    {
        // For each axis, pick the AABB corner that's furthest in the plane's normal direction.
        // This is the "positive vertex" — the worst case for being outside the plane.
        Vector3 n = plane.Xyz;
        Vector3 pVertex = new Vector3(
            n.X >= 0f ? box.Max.X : box.Min.X,
            n.Y >= 0f ? box.Max.Y : box.Min.Y,
            n.Z >= 0f ? box.Max.Z : box.Min.Z);

        // Plane: ax + by + cz + d = 0; "outside" if positive (we built planes that way).
        // If even the positive vertex is on the inside (signed dist <= 0), the box is inside
        // or straddles this plane.
        float signedDistance = Vector3.Dot(n, pVertex) + plane.W;
        return signedDistance <= 0f;
    }
}
