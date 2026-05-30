using ArcEngine.Engine.Loaders;
using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.Resources;

/// <summary>
/// Central asset cache. Every call site that loads a texture / shader / model file
/// should go through this rather than constructing the resource directly so we
/// don't waste GPU memory or re-parse files for duplicate requests.
///
/// Caches are keyed on the *normalised* (full / case-insensitive) path so relative
/// and absolute paths to the same file dedupe correctly. The byte[] texture path
/// (used for GLB-embedded images) is intentionally not cached — those rarely repeat
/// across a scene and lack a stable identity without hashing.
///
/// v1 limitations: no reference counting; <see cref="Clear"/> only drops the dictionary
/// references — it does not dispose underlying GL handles. Add disposal hooks alongside
/// editor hot-reload when that lands.
/// </summary>
public static class Resources
{
    private static readonly Dictionary<(string path, bool sRGB), Texture> s_textures = new();

    private static readonly Dictionary<(string vert, string frag), Shader> s_shaders = new();

    private static readonly Dictionary<string, ModelData> s_models =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Load a 2D texture from disk, returning the cached instance on duplicate requests.
    /// Returns null with a logged warning if the file doesn't exist.
    /// </summary>
    /// <param name="path">Texture file path.</param>
    /// <param name="sRGB">
    /// True for color textures (base color / albedo) — uploaded as <c>SrgbAlpha</c>
    /// so shader samples auto-linearize. False for data textures (normal, MR, AO).
    /// </param>
    public static Texture? LoadTexture(string path, bool sRGB = false)
    {
        var keyPath = NormalizePath(path);
        var key = (keyPath, sRGB);

        if (s_textures.TryGetValue(key, out var cached)) return cached;

        if (!File.Exists(keyPath))
        {
            Console.WriteLine($"[Resources] Warning: texture not found: {path}");
            return null;
        }

        var tex = new Texture(keyPath, sRGB);
        s_textures[key] = tex;
        return tex;
    }

    /// <summary>
    /// Load a vertex+fragment shader pair, returning the cached instance on duplicate requests.
    /// Throws on compile/link error (via <see cref="Shader"/>'s ctor).
    /// </summary>
    public static Shader LoadShader(string vertexPath, string fragmentPath)
    {
        var key = (NormalizePath(vertexPath), NormalizePath(fragmentPath));

        if (s_shaders.TryGetValue(key, out var cached)) return cached;

        var sh = new Shader(key.Item1, key.Item2);
        s_shaders[key] = sh;
        return sh;
    }

    /// <summary>
    /// Load a model file and return its parsed <see cref="ModelData"/>, dispatching
    /// based on file extension. Cached by path on duplicate requests.
    /// Supported: .obj, .gltf, .glb.
    /// </summary>
    public static ModelData LoadModelData(string path)
    {
        var key = NormalizePath(path);

        if (s_models.TryGetValue(key, out var cached)) return cached;

        var ext = Path.GetExtension(key).ToLowerInvariant();
        ModelData data = ext switch
        {
            ".obj"  => ObjLoader.Load(key),
            ".gltf" => GltfLoader.Load(key),
            ".glb"  => GltfLoader.Load(key),
            _ => throw new NotSupportedException(
                     $"[Resources] Unsupported model format '{ext}' for '{path}'.")
        };

        s_models[key] = data;
        return data;
    }

    /// <summary>
    /// Drop all cached references. Future hot-reload hook for the editor.
    /// Note: does NOT delete GPU handles in v1 — assets remain alive for any
    /// scene/component still referencing them.
    /// </summary>
    public static void Clear()
    {
        s_textures.Clear();
        s_shaders.Clear();
        s_models.Clear();
    }

    private static string NormalizePath(string path)
    {
        // GetFullPath resolves relative paths against the working directory and
        // canonicalises separators. Combined with case-insensitive dictionary
        // comparers, this dedupes "Assets/foo.png" vs "Assets\\foo.png".
        return Path.GetFullPath(path);
    }
}
