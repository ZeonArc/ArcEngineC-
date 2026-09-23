using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// CPU-side vertex used by the indexed <see cref="Mesh"/> path and by the loader pipeline.
/// Layout matches the engine's GPU contract: pos3 + normal3 + uv2 + tangent4
/// (12 floats / 48 bytes). Tangent is a vec4 — <c>xyz</c> is the tangent direction,
/// <c>w</c> is the bitangent sign (±1) per the glTF convention; the vertex shader
/// reconstructs bitangent as <c>B = sign * cross(N, T)</c>. Callers that don't need
/// mirrored-UV support can leave <c>w = 1</c>; loaders that produce mirrored UVs
/// (glTF's TANGENT.w) MUST propagate the sign.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public Vector4 Tangent;

    /// <summary>Size of one vertex in bytes (must match the GL stride).</summary>
    public const int SizeInBytes = 12 * sizeof(float);

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Tangent = new Vector4(0f, 0f, 0f, 1f);
    }

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv, Vector3 tangent)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Tangent = new Vector4(tangent, 1f);
    }

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv, Vector4 tangent)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Tangent = tangent;
    }
}
