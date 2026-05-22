using OpenTK.Mathematics;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Loader-agnostic material description. Both path-based (OBJ/MTL) and embedded-bytes
/// (GLB) texture sources are supported. <see cref="ModelBuilder"/> prefers bytes if present,
/// otherwise falls back to <see cref="DiffuseMapPath"/>.
/// </summary>
public class MaterialData
{
    public string Name { get; set; } = "default";

    public Vector3 DiffuseColor { get; set; } = Vector3.One;

    /// <summary>Absolute or working-directory-relative path to the diffuse texture (OBJ/MTL pipeline).</summary>
    public string? DiffuseMapPath { get; set; }

    /// <summary>Embedded diffuse texture bytes (GLB pipeline). Preferred over <see cref="DiffuseMapPath"/> if both are set.</summary>
    public byte[]? DiffuseMapBytes { get; set; }

    public string? SpecularMapPath { get; set; }

    public float Shininess { get; set; } = 32f;
}
