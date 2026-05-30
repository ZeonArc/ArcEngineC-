using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// CPU-side vertex used by the indexed <see cref="Mesh"/> path and by the loader pipeline.
/// Layout matches the engine's GPU contract: pos3 + normal3 + uv2 + tangent3
/// (11 floats / 44 bytes). Tangent is required for PBR normal mapping; if a loader
/// doesn't produce one, leave it zero — the shader treats a zero-length tangent as
/// "no normal map" via a separate uniform flag.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public Vector3 Tangent;

    /// <summary>Size of one vertex in bytes (must match the GL stride).</summary>
    public const int SizeInBytes = 11 * sizeof(float);

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Tangent = Vector3.Zero;
    }

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv, Vector3 tangent)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Tangent = tangent;
    }
}
