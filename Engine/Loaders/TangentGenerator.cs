using OpenTK.Mathematics;

using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Computes per-vertex tangents from triangle UV gradients, in-place on a
/// <see cref="Vertex"/> array.
///
/// Algorithm (standard / "MikkTSpace-lite"):
/// <list type="number">
///   <item>For each triangle, compute the triangle-space tangent T such that going +U in
///         texture space corresponds to +T in world space, given the two edge vectors.</item>
///   <item>Accumulate that T into each of the triangle's three vertices (sum, no average yet).</item>
///   <item>After all triangles, Gram-Schmidt orthonormalize each accumulated T against the
///         vertex's existing normal: <c>T = normalize(T - N * dot(N, T))</c>.</item>
/// </list>
///
/// Vertices with degenerate UVs (collinear in texture space) keep a zero tangent — the
/// shader handles that case by skipping the normal-map perturbation.
/// </summary>
internal static class TangentGenerator
{
    public static void GenerateInPlace(Vertex[] vertices, uint[] indices)
    {
        if (vertices.Length == 0 || indices.Length < 3) return;

        // 1) Reset existing tangents (they're zero-initialised but be explicit
        //    in case this is called twice on the same buffer).
        for (int i = 0; i < vertices.Length; i++)
            vertices[i].Tangent = Vector4.Zero;

        // 2) Walk triangles, accumulate.
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            uint i0 = indices[t + 0];
            uint i1 = indices[t + 1];
            uint i2 = indices[t + 2];

            ref var v0 = ref vertices[i0];
            ref var v1 = ref vertices[i1];
            ref var v2 = ref vertices[i2];

            Vector3 e1 = v1.Position - v0.Position;
            Vector3 e2 = v2.Position - v0.Position;

            Vector2 dUv1 = v1.UV - v0.UV;
            Vector2 dUv2 = v2.UV - v0.UV;

            float det = dUv1.X * dUv2.Y - dUv2.X * dUv1.Y;
            if (System.MathF.Abs(det) < 1e-8f) continue; // degenerate UVs

            float inv = 1f / det;
            Vector3 tangent = inv * (dUv2.Y * e1 - dUv1.Y * e2);

            v0.Tangent += new Vector4(tangent, 0f);
            v1.Tangent += new Vector4(tangent, 0f);
            v2.Tangent += new Vector4(tangent, 0f);
        }

        // 3) Orthonormalise against each vertex's existing normal. Bitangent sign
        //    defaults to +1 for generated tangents (no UV mirroring info here — the
        //    glTF loader is the only path that preserves the true sign).
        for (int i = 0; i < vertices.Length; i++)
        {
            ref var v = ref vertices[i];
            var acc = v.Tangent.Xyz;
            if (acc.LengthSquared < 1e-12f)
            {
                v.Tangent = new Vector4(0f, 0f, 0f, 1f);
                continue;
            }

            // Gram-Schmidt: T_orth = T - N * (N . T)
            Vector3 n = v.Normal;
            acc -= n * Vector3.Dot(n, acc);

            acc = acc.LengthSquared > 1e-12f ? Vector3.Normalize(acc) : Vector3.Zero;
            v.Tangent = new Vector4(acc, 1f);
        }
    }
}
