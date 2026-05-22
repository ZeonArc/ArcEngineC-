namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Top-level loader output: a list of submeshes plus a parallel list of materials.
/// Each <see cref="MeshData.MaterialIndex"/> indexes into <see cref="Materials"/>.
/// </summary>
public class ModelData
{
    public List<MeshData> Submeshes { get; } = new();
    public List<MaterialData> Materials { get; } = new();

    /// <summary>Path the model was loaded from. Used for diagnostics and resolving relative texture paths.</summary>
    public string SourcePath { get; set; } = "";

    /// <summary>OBJ-only: path to the .mtl referenced via <c>mtllib</c>, resolved relative to the OBJ. Null otherwise.</summary>
    public string? MtlLibPath { get; set; }
}
