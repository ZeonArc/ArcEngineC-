using System.Globalization;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Hand-written Wavefront MTL (material library) parser.
/// Recognised keywords: <c>newmtl</c>, <c>Kd</c>, <c>Ka</c>, <c>Ks</c>, <c>Ns</c>,
/// <c>d</c>/<c>Tr</c>, <c>map_Kd</c>, <c>map_Ks</c>, <c>map_Bump</c>/<c>bump</c>.
///
/// Texture paths are resolved relative to the .mtl file's directory.
/// Other keywords are ignored (we're a Phase-4 diffuse/specular renderer, not full PBR).
/// </summary>
public static class MtlLoader
{
    public static List<MaterialData> Load(string mtlPath)
    {
        var result = new List<MaterialData>();
        var inv = CultureInfo.InvariantCulture;
        var dir = Path.GetDirectoryName(mtlPath) ?? "";

        MaterialData? current = null;

        foreach (var rawLine in File.ReadAllLines(mtlPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            switch (tokens[0])
            {
                case "newmtl":
                    current = new MaterialData
                    {
                        Name = tokens.Length > 1 ? tokens[1] : "unnamed"
                    };
                    result.Add(current);
                    break;

                case "Kd" when current != null && tokens.Length >= 4:
                    current.DiffuseColor = new Vector3(
                        float.Parse(tokens[1], inv),
                        float.Parse(tokens[2], inv),
                        float.Parse(tokens[3], inv));
                    break;

                case "Ka":
                    // Ambient — parsed for completeness but unused by the current shader.
                    break;

                case "Ks":
                    // Specular color — unused by the current shader (Phase 5 will add it).
                    break;

                case "Ns" when current != null && tokens.Length >= 2:
                    current.Shininess = float.Parse(tokens[1], inv);
                    break;

                case "d":
                case "Tr":
                    // Opacity / transparency — parsed for completeness but unused by the current shader.
                    break;

                case "map_Kd" when current != null && tokens.Length >= 2:
                    current.DiffuseMapPath = ResolveTexturePath(dir, tokens);
                    break;

                case "map_Ks" when current != null && tokens.Length >= 2:
                    current.SpecularMapPath = ResolveTexturePath(dir, tokens);
                    break;

                case "map_Bump":
                case "bump":
                    // Normal/bump map — parsed for completeness but unused by the current shader.
                    break;

                default:
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Texture-path tokens may include MTL options like "-bm 2 -clamp on filename.png".
    /// We take the last token as the filename — this handles the common case and the
    /// "filename with no spaces, no options" case correctly. (Filenames with spaces are
    /// uncommon in MTL and not worth special-casing here.)
    /// </summary>
    private static string ResolveTexturePath(string mtlDir, string[] tokens)
    {
        var rel = tokens[tokens.Length - 1];
        return Path.Combine(mtlDir, rel);
    }
}
