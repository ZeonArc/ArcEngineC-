using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArcEngine.Engine.Resources;

/// <summary>
/// Asset ↔ GUID mapping. Every on-disk asset (texture, mesh, model, shader) gets a
/// companion <c>&lt;asset&gt;.meta</c> file the first time it's touched by the engine.
/// The .meta file stores a stable GUID that scenes reference instead of file paths,
/// so renaming or moving an asset doesn't break the references.
///
/// Layout of a .meta file:
/// <code>
/// {
///   "Guid": "3f2a1c04-9e0a-4d8e-8f1a-5e2c7f9d1234"
/// }
/// </code>
///
/// Lookups:
/// <list type="bullet">
///   <item><see cref="EnsureGuid"/> — given a path, returns its GUID (creates .meta if missing).</item>
///   <item><see cref="ResolveGuid"/> — given a GUID, returns the current on-disk path (walks
///         <c>Assets/</c> lazily on the first miss).</item>
/// </list>
/// </summary>
public static class MetaRegistry
{
    /// <summary>Root folder scanned when the guid→path index is (re)built.</summary>
    public const string AssetsRoot = "Assets";

    private static readonly Dictionary<string, Guid> s_pathToGuid =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Guid, string> s_guidToPath = new();

    private static bool s_indexBuilt;

    /// <summary>
    /// Read (or create) the GUID for <paramref name="assetPath"/>. Idempotent: a
    /// second call for the same asset returns the same GUID.
    /// </summary>
    public static Guid EnsureGuid(string assetPath)
    {
        string norm = NormalizePath(assetPath);
        if (s_pathToGuid.TryGetValue(norm, out var cached)) return cached;

        string metaPath = MetaPathFor(norm);
        Guid guid;

        if (File.Exists(metaPath))
        {
            guid = ReadGuidFromMeta(metaPath) ?? WriteFreshMeta(metaPath);
        }
        else
        {
            guid = WriteFreshMeta(metaPath);
        }

        s_pathToGuid[norm] = guid;
        s_guidToPath[guid] = norm;
        return guid;
    }

    /// <summary>
    /// Look up the current on-disk path for <paramref name="guid"/>, or null if no
    /// asset with that GUID is present under <see cref="AssetsRoot"/>. Builds the
    /// reverse index on first call.
    /// </summary>
    public static string? ResolveGuid(Guid guid)
    {
        if (s_guidToPath.TryGetValue(guid, out var cached)) return cached;
        if (!s_indexBuilt) BuildIndex();
        return s_guidToPath.TryGetValue(guid, out cached) ? cached : null;
    }

    /// <summary>
    /// Force a full walk of <see cref="AssetsRoot"/> to populate the guid ↔ path
    /// tables. Callable manually after external file operations (moves/renames);
    /// otherwise the first <see cref="ResolveGuid"/> miss triggers it.
    /// </summary>
    public static void BuildIndex()
    {
        s_indexBuilt = true;
        if (!Directory.Exists(AssetsRoot)) return;

        // Walk every .meta file; pair each with its source asset (drop trailing ".meta").
        foreach (var meta in Directory.EnumerateFiles(AssetsRoot, "*.meta", SearchOption.AllDirectories))
        {
            string assetPath = meta.Substring(0, meta.Length - ".meta".Length);
            if (!File.Exists(assetPath)) continue;    // orphaned .meta — ignore

            var guid = ReadGuidFromMeta(meta);
            if (guid == null) continue;

            string norm = NormalizePath(assetPath);
            s_pathToGuid[norm] = guid.Value;
            s_guidToPath[guid.Value] = norm;
        }
    }

    /// <summary>
    /// Drop cached lookups. Next call rebuilds from disk.
    /// </summary>
    public static void Clear()
    {
        s_pathToGuid.Clear();
        s_guidToPath.Clear();
        s_indexBuilt = false;
    }

    // ============================================================================
    // Internals
    // ============================================================================

    private static string MetaPathFor(string assetPath) => assetPath + ".meta";

    private static Guid? ReadGuidFromMeta(string metaPath)
    {
        try
        {
            var doc = JsonNode.Parse(File.ReadAllText(metaPath)) as JsonObject;
            if (doc?["Guid"] is JsonValue gv && Guid.TryParse(gv.GetValue<string>(), out var g))
                return g;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MetaRegistry] Failed to read '{metaPath}': {ex.Message}");
        }
        return null;
    }

    private static Guid WriteFreshMeta(string metaPath)
    {
        var guid = Guid.NewGuid();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(metaPath) ?? ".");
            var doc = new JsonObject { ["Guid"] = guid.ToString() };
            File.WriteAllText(metaPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MetaRegistry] Failed to write '{metaPath}': {ex.Message}");
        }
        return guid;
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);
}
