using ArcEngine.Engine.Core;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.Resources;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Converts a <see cref="ModelData"/> (loader output) into a runtime GameObject hierarchy:
/// one empty parent GameObject with one child GameObject per submesh, each carrying a
/// <see cref="MeshRenderer"/> component referencing its own <see cref="Mesh"/> and <see cref="Material"/>.
///
/// Path-keyed texture caching is delegated to <see cref="Resources.LoadTexture"/> so duplicate
/// diffuse / specular maps across multiple model loads share one GL texture.
/// </summary>
public static class ModelBuilder
{
    /// <summary>
    /// Build a GameObject tree from a <see cref="ModelData"/>.
    /// </summary>
    public static GameObject Build(ModelData data, Shader shader)
    {
        // 1) Build all materials up front.
        var materials = new Material[data.Materials.Count];

        for (int i = 0; i < data.Materials.Count; i++)
        {
            materials[i] = BuildMaterial(data.Materials[i], shader);
        }

        // Fallback material in case a submesh's MaterialIndex is out of range.
        Material fallback = new Material(shader) { Color = OpenTK.Mathematics.Vector3.One };

        // 2) Parent root.
        var root = new GameObject
        {
            Name = string.IsNullOrEmpty(data.SourcePath) ? "Model" : Path.GetFileName(data.SourcePath)
        };

        // 3) One child per submesh, each with its own MeshRenderer.
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
            Shininess = md.Shininess
        };

        // Diffuse: prefer embedded bytes (GLB) over a file path (OBJ/MTL).
        if (md.DiffuseMapBytes != null && md.DiffuseMapBytes.Length > 0)
        {
            mat.Texture = new Texture(md.DiffuseMapBytes);
        }
        else if (!string.IsNullOrEmpty(md.DiffuseMapPath))
        {
            mat.Texture = ArcEngine.Engine.Resources.Resources.LoadTexture(md.DiffuseMapPath);
        }

        // Specular: file-path only (GLB's PBR channel is not currently translated into a spec map).
        if (!string.IsNullOrEmpty(md.SpecularMapPath))
        {
            mat.SpecularTexture = ArcEngine.Engine.Resources.Resources.LoadTexture(md.SpecularMapPath);
        }

        return mat;
    }
}
