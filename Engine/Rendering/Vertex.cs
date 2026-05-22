using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// CPU-side vertex used by the indexed <see cref="Mesh"/> path and by the loader pipeline.
/// Layout matches the engine's GPU contract: pos3 + normal3 + uv2 (8 floats / 32 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;

    /// <summary>Size of one vertex in bytes (must match the GL stride).</summary>
    public const int SizeInBytes = 8 * sizeof(float);

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Position = position;
        Normal = normal;
        UV = uv;
    }
}
