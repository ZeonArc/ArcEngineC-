using ArcEngine.Engine.Core;
using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Converts a <see cref="ModelData"/> (loader output) into a runtime GameObject hierarchy:
/// one empty parent GameObject with one child GameObject per submesh, each carrying its
/// own <see cref="Mesh"/> and <see cref="Material"/>.
///
/// The parent contains no mesh/material — the Renderer skips empty GameObjects.
/// </summary>
public static class ModelBuilder
{
    /// <summary>
    /// Build a GameObject tree from a <see cref="ModelData"/>.
    /// </summary>
    /// <param name="data">The loaded model description.</param>
    /// <param name="shader">Shader shared across all materials produced.</param>
    /// <returns>The parent GameObject. Add it to the scene with <c>scene.Add(go)</c>; children are registered automatically.</returns>
    public static GameObject Build(ModelData data, Shader shader)
    {
        // 1) Build all materials up front (with a per-build texture cache to avoid duplicate uploads).
        var textureCache = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        var materials = new Material[data.Materials.Count];

        for (int i = 0; i < data.Materials.Count; i++)
        {
            materials[i] = BuildMaterial(data.Materials[i], shader, textureCache);
        }

        // Fallback material in case a submesh's MaterialIndex is out of range
        // (e.g. OBJ used `usemtl X` but no .mtl file was provided).
        Material fallback = new Material(shader) { Color = OpenTK.Mathematics.Vector3.One };

        // 2) Parent root.
        var root = new GameObject
        {
            Name = string.IsNullOrEmpty(data.SourcePath) ? "Model" : Path.GetFileName(data.SourcePath)
        };

        // 3) One child per submesh.
        foreach (var sub in data.Submeshes)
        {
            var child = new GameObject
            {
                Name = sub.Name ?? "Submesh",
                Mesh = new Mesh(sub.Vertices, sub.Indices),
                Material = (sub.MaterialIndex >= 0 && sub.MaterialIndex < materials.Length)
                    ? materials[sub.MaterialIndex]
                    : fallback
            };
            child.Transform.SetParent(root.Transform);
        }

        return root;
    }

    private static Material BuildMaterial(MaterialData md, Shader shader, Dictionary<string, Texture> cache)
    {
        var mat = new Material(shader)
        {
            Color = md.DiffuseColor,
            Shininess = md.Shininess
        };

        // Diffuse: prefer embedded bytes (GLB) over a file path (OBJ/MTL).
        if (md.DiffuseMapBytes != null && md.DiffuseMapBytes.Length > 0)
        {
            // Bytes-keyed caching is awkward; for in-memory textures we just create per material.
            // (GLB models rarely share so many embedded images that this matters.)
            mat.Texture = new Texture(md.DiffuseMapBytes);
        }
        else if (!string.IsNullOrEmpty(md.DiffuseMapPath))
        {
            mat.Texture = LoadOrCacheTexture(md.DiffuseMapPath, cache);
        }

        // Specular: file-path only for now (GLB's PBR channel is a different concept and is
        // not currently translated into specular maps).
        if (!string.IsNullOrEmpty(md.SpecularMapPath))
        {
            mat.SpecularTexture = LoadOrCacheTexture(md.SpecularMapPath, cache);
        }

        return mat;
    }

    private static Texture? LoadOrCacheTexture(string path, Dictionary<string, Texture> cache)
    {
        if (cache.TryGetValue(path, out var tex)) return tex;

        if (!File.Exists(path))
        {
            Console.WriteLine($"[ModelBuilder] Warning: texture not found: {path}");
            return null;
        }

        tex = new Texture(path);
        cache[path] = tex;
        return tex;
    }
}
