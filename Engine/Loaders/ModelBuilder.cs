using ArcEngine.Engine.Core;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.Resources;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Converts a <see cref="ModelData"/> (loader output) into a runtime GameObject hierarchy:
/// one empty parent GameObject with one child GameObject per submesh, each carrying a
/// <see cref="MeshRenderer"/> component referencing its own <see cref="Mesh"/> and <see cref="Material"/>.
///
/// Texture sRGB rule: BaseColor uses sRGB upload (auto-linearized on sample); all other
/// PBR maps (MetallicRoughness, Normal, Occlusion) use linear upload.
/// </summary>
public static class ModelBuilder
{
    public static GameObject Build(ModelData data, Shader shader)
    {
        var materials = new Material[data.Materials.Count];
        for (int i = 0; i < data.Materials.Count; i++)
            materials[i] = BuildMaterial(data.Materials[i], shader);

        Material fallback = new Material(shader) { Color = OpenTK.Mathematics.Vector3.One };

        var root = new GameObject
        {
            Name = string.IsNullOrEmpty(data.SourcePath) ? "Model" : Path.GetFileName(data.SourcePath),
            SourceModelPath = string.IsNullOrEmpty(data.SourcePath) ? null : data.SourcePath,
        };

        foreach (var sub in data.Submeshes)
        {
            var child = new GameObject { Name = sub.Name ?? "Submesh" };

            var mr = child.AddComponent<MeshRenderer>();
            mr.Mesh = new Mesh(sub.Vertices, sub.Indices);
            mr.Material = (sub.MaterialIndex >= 0 && sub.MaterialIndex < materials.Length)
                ? materials[sub.MaterialIndex]
                : fallback;

            child.Transform.SetParent(root.Transform);
        }

        return root;
    }

    private static Material BuildMaterial(MaterialData md, Shader shader)
    {
        var mat = new Material(shader)
        {
            Color = md.DiffuseColor,
            Metallic = md.Metallic,
            Roughness = md.Roughness,
            AmbientOcclusion = md.AmbientOcclusion,
            NormalStrength = md.NormalStrength,
            Shininess = md.Shininess,                  // legacy; unused in PBR shader
        };

        // ---- BaseColor (sRGB) ------------------------------------------------
        if (md.DiffuseMapBytes != null && md.DiffuseMapBytes.Length > 0)
            mat.Texture = new Texture(md.DiffuseMapBytes, sRGB: true);
        else if (!string.IsNullOrEmpty(md.DiffuseMapPath))
            mat.Texture = ArcEngine.Engine.Resources.Resources.LoadTexture(md.DiffuseMapPath, sRGB: true);

        // ---- MetallicRoughness (linear) -------------------------------------
        if (md.MetallicRoughnessMapBytes != null && md.MetallicRoughnessMapBytes.Length > 0)
            mat.MetallicRoughnessTexture = new Texture(md.MetallicRoughnessMapBytes, sRGB: false);
        else if (!string.IsNullOrEmpty(md.MetallicRoughnessMapPath))
            mat.MetallicRoughnessTexture = ArcEngine.Engine.Resources.Resources.LoadTexture(md.MetallicRoughnessMapPath, sRGB: false);

        // ---- Normal (linear) ------------------------------------------------
        if (md.NormalMapBytes != null && md.NormalMapBytes.Length > 0)
            mat.NormalTexture = new Texture(md.NormalMapBytes, sRGB: false);
        else if (!string.IsNullOrEmpty(md.NormalMapPath))
            mat.NormalTexture = ArcEngine.Engine.Resources.Resources.LoadTexture(md.NormalMapPath, sRGB: false);

        // ---- Occlusion (linear) ---------------------------------------------
        if (md.OcclusionMapBytes != null && md.OcclusionMapBytes.Length > 0)
            mat.OcclusionTexture = new Texture(md.OcclusionMapBytes, sRGB: false);
        else if (!string.IsNullOrEmpty(md.OcclusionMapPath))
            mat.OcclusionTexture = ArcEngine.Engine.Resources.Resources.LoadTexture(md.OcclusionMapPath, sRGB: false);

        // ---- Legacy specular slot (Sprint 5a back-compat) -------------------
        if (!string.IsNullOrEmpty(md.SpecularMapPath))
            mat.SpecularTexture = ArcEngine.Engine.Resources.Resources.LoadTexture(md.SpecularMapPath, sRGB: false);

        return mat;
    }
}
