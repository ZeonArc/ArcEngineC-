using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Loader-agnostic submesh: a chunk of indexed geometry with one assigned material slot.
/// Produced by every loader (OBJ, GLTF) and consumed by <see cref="ModelBuilder"/>.
/// </summary>
public class MeshData
{
    public Vertex[] Vertices { get; set; } = Array.Empty<Vertex>();
    public uint[] Indices { get; set; } = Array.Empty<uint>();

    /// <summary>Index into <see cref="ModelData.Materials"/>. Defaults to 0 (the first / fallback material).</summary>
    public int MaterialIndex { get; set; } = 0;

    public string? Name { get; set; }
}
