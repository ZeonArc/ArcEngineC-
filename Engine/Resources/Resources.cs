using ArcEngine.Engine.Audio;
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

    private static readonly Dictionary<string, AudioClip> s_audioClips =
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
        MetaRegistry.EnsureGuid(keyPath);
        return tex;
    }

    /// <summary>
    /// Load a texture by GUID. Resolves the GUID to its current on-disk path via
    /// <see cref="MetaRegistry"/>, then defers to <see cref="LoadTexture(string,bool)"/>.
    /// Returns null if the GUID doesn't resolve to an existing asset.
    /// </summary>
    public static Texture? LoadTextureByGuid(Guid guid, bool sRGB = false)
    {
        var path = MetaRegistry.ResolveGuid(guid);
        return path == null ? null : LoadTexture(path, sRGB);
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
        MetaRegistry.EnsureGuid(key);
        return data;
    }

    /// <summary>
    /// Load model data by GUID. Resolves via <see cref="MetaRegistry"/>, then defers to
    /// <see cref="LoadModelData(string)"/>. Returns null if the GUID doesn't resolve.
    /// </summary>
    public static ModelData? LoadModelDataByGuid(Guid guid)
    {
        var path = MetaRegistry.ResolveGuid(guid);
        return path == null ? null : LoadModelData(path);
    }

    /// <summary>
    /// Load an audio clip. Dispatches by file extension:
    /// <list type="bullet">
    ///   <item><c>.wav</c> — parsed by <see cref="WavLoader"/> (uncompressed PCM).</item>
    ///   <item><c>.ogg</c> — reserved; needs an OGG/Vorbis decoder to be wired up.</item>
    /// </list>
    /// Returns null with a logged warning when the file is missing or unsupported.
    /// </summary>
    public static AudioClip? LoadAudioClip(string path)
    {
        var key = NormalizePath(path);
        if (s_audioClips.TryGetValue(key, out var cached)) return cached;

        var ext = Path.GetExtension(key).ToLowerInvariant();
        AudioClip? clip = ext switch
        {
            ".wav" => WavLoader.Load(key),
            ".ogg" => LogOggUnsupported(path),
            _ => LogAudioUnsupported(path, ext),
        };

        if (clip != null)
        {
            s_audioClips[key] = clip;
            MetaRegistry.EnsureGuid(key);
        }
        return clip;
    }

    private static AudioClip? LogOggUnsupported(string path)
    {
        Console.WriteLine($"[Resources] OGG loading not yet wired up: {path}. Add an OGG/Vorbis decoder (e.g. NVorbis) to enable.");
        return null;
    }

    private static AudioClip? LogAudioUnsupported(string path, string ext)
    {
        Console.WriteLine($"[Resources] Unsupported audio format '{ext}' for {path}");
        return null;
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
        s_audioClips.Clear();
    }

    private static string NormalizePath(string path)
    {
        // GetFullPath resolves relative paths against the working directory and
        // canonicalises separators. Combined with case-insensitive dictionary
        // comparers, this dedupes "Assets/foo.png" vs "Assets\\foo.png".
        return Path.GetFullPath(path);
    }
}
