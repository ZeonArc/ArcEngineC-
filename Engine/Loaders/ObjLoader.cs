using System.Globalization;
using OpenTK.Mathematics;

using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Hand-written Wavefront OBJ parser.
///
/// Geometry: <c>v</c>, <c>vt</c>, <c>vn</c>, <c>f</c>. Polygon faces are fan-triangulated.
/// 1-based and negative (relative-from-end) indices are supported. Vertex deduplication
/// merges identical (position, uv, normal) tuples into a single GPU vertex.
///
/// Multi-submesh: <c>o</c>, <c>g</c>, and <c>usemtl</c> all act as submesh boundaries.
/// Each submesh has its own dedupe map and material slot.
///
/// Materials: <c>mtllib</c> is recorded on <see cref="ModelData.MtlLibPath"/> for
/// <see cref="MtlLoader"/> to consume in Task 9. <c>usemtl</c> registers a placeholder
/// <see cref="MaterialData"/> by name; the placeholder is upgraded to a real material
/// (with diffuse color/texture) once the .mtl file is parsed.
/// </summary>
public static class ObjLoader
{
    public static ModelData Load(string path)
    {
        var data = new ModelData { SourcePath = path };

        // Always seed a "default" material at index 0 so submeshes that never see `usemtl` still resolve.
        data.Materials.Add(new MaterialData { Name = "default" });

        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();

        // Per-submesh accumulators. Reset on every o/g/usemtl boundary.
        var verts = new List<Vertex>();
        var idxs = new List<uint>();
        var dedupe = new Dictionary<(int p, int t, int n), uint>();

        string? currentSubmeshName = null;
        int currentMaterialIndex = 0;

        // Material-name → MaterialData index. Placeholders get upgraded by MtlLoader (Task 9).
        var materialNameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        int faceCounter = 0;
        var inv = CultureInfo.InvariantCulture;

        void FlushSubmesh()
        {
            if (idxs.Count == 0) return;

            var vArr = verts.ToArray();
            var iArr = idxs.ToArray();

            // Per-submesh: compute per-vertex tangents from triangle UV gradients.
            // This must happen before the submesh is handed off because vArr is a value-copy.
            TangentGenerator.GenerateInPlace(vArr, iArr);

            data.Submeshes.Add(new MeshData
            {
                Vertices = vArr,
                Indices = iArr,
                MaterialIndex = currentMaterialIndex,
                Name = currentSubmeshName ?? $"submesh_{data.Submeshes.Count}"
            });

            verts.Clear();
            idxs.Clear();
            dedupe.Clear();
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            switch (tokens[0])
            {
                case "v":
                    positions.Add(new Vector3(
                        float.Parse(tokens[1], inv),
                        float.Parse(tokens[2], inv),
                        float.Parse(tokens[3], inv)));
                    break;

                case "vt":
                    uvs.Add(new Vector2(
                        float.Parse(tokens[1], inv),
                        tokens.Length > 2 ? float.Parse(tokens[2], inv) : 0f));
                    break;

                case "vn":
                    normals.Add(new Vector3(
                        float.Parse(tokens[1], inv),
                        float.Parse(tokens[2], inv),
                        float.Parse(tokens[3], inv)));
                    break;

                case "f":
                    ParseFace(tokens, positions, uvs, normals, verts, idxs, dedupe, faceCounter);
                    faceCounter++;
                    break;

                case "o":
                case "g":
                    FlushSubmesh();
                    currentSubmeshName = tokens.Length > 1 ? tokens[1] : null;
                    // Group changes don't reset the active material.
                    break;

                case "usemtl":
                {
                    FlushSubmesh();
                    var matName = tokens.Length > 1 ? tokens[1] : "default";
                    if (!materialNameToIndex.TryGetValue(matName, out int matIdx))
                    {
                        // Placeholder until MtlLoader (Task 9) fills in real diffuse color/texture.
                        data.Materials.Add(new MaterialData { Name = matName });
                        matIdx = data.Materials.Count - 1;
                        materialNameToIndex[matName] = matIdx;
                    }
                    currentMaterialIndex = matIdx;
                    break;
                }

                case "mtllib":
                {
                    if (tokens.Length > 1)
                    {
                        // Filenames may contain spaces — rejoin everything after the directive.
                        var mtlRel = string.Join(' ', tokens, 1, tokens.Length - 1);
                        var dir = Path.GetDirectoryName(path) ?? "";
                        data.MtlLibPath = Path.Combine(dir, mtlRel);
                    }
                    break;
                }

                default:
                    // s (smoothing groups), other unknowns: ignored.
                    break;
            }
        }

        // Final submesh.
        FlushSubmesh();

        // Pathological: file had no faces at all → still emit a single empty submesh
        // so downstream code (ModelBuilder) doesn't choke on a zero-submesh model.
        if (data.Submeshes.Count == 0)
        {
            data.Submeshes.Add(new MeshData
            {
                Vertices = Array.Empty<Vertex>(),
                Indices = Array.Empty<uint>(),
                MaterialIndex = 0,
                Name = Path.GetFileNameWithoutExtension(path)
            });
        }

        // Resolve mtllib: replace placeholder MaterialData (created by `usemtl`) with
        // the real ones loaded from the .mtl file. Match by Name. Placeholders not
        // present in the MTL keep their default white color.
        if (!string.IsNullOrEmpty(data.MtlLibPath) && File.Exists(data.MtlLibPath))
        {
            var loaded = MtlLoader.Load(data.MtlLibPath);
            var byName = new Dictionary<string, MaterialData>(StringComparer.Ordinal);
            foreach (var m in loaded) byName[m.Name] = m;

            for (int i = 0; i < data.Materials.Count; i++)
            {
                if (byName.TryGetValue(data.Materials[i].Name, out var real))
                {
                    data.Materials[i] = real;
                }
            }
        }
        else if (!string.IsNullOrEmpty(data.MtlLibPath))
        {
            Console.WriteLine($"[ObjLoader] Warning: mtllib '{data.MtlLibPath}' not found; using default materials.");
        }

        return data;
    }

    private static void ParseFace(
        string[] tokens,
        List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals,
        List<Vertex> outVertices, List<uint> outIndices,
        Dictionary<(int p, int t, int n), uint> dedupe,
        int faceCounter)
    {
        int n = tokens.Length - 1;
        if (n < 3) return;

        var tuples = new (int p, int t, int nrm)[n];
        for (int i = 0; i < n; i++)
        {
            tuples[i] = ParseFaceVertex(tokens[i + 1], positions.Count, uvs.Count, normals.Count);
        }

        Vector3 faceNormal = Vector3.UnitZ;
        bool needSynth = false;
        for (int i = 0; i < n; i++)
        {
            if (tuples[i].nrm < 0) { needSynth = true; break; }
        }
        if (needSynth)
        {
            var p0 = positions[tuples[0].p];
            var p1 = positions[tuples[1].p];
            var p2 = positions[tuples[2].p];
            var fn = Vector3.Cross(p1 - p0, p2 - p0);
            if (fn.LengthSquared > 0f) faceNormal = Vector3.Normalize(fn);
        }

        for (int i = 1; i < n - 1; i++)
        {
            EmitVertex(tuples[0], faceNormal, faceCounter, positions, uvs, normals, outVertices, outIndices, dedupe);
            EmitVertex(tuples[i], faceNormal, faceCounter, positions, uvs, normals, outVertices, outIndices, dedupe);
            EmitVertex(tuples[i + 1], faceNormal, faceCounter, positions, uvs, normals, outVertices, outIndices, dedupe);
        }
    }

    private static void EmitVertex(
        (int p, int t, int nrm) tup, Vector3 faceNormal, int faceCounter,
        List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals,
        List<Vertex> outVertices, List<uint> outIndices,
        Dictionary<(int p, int t, int n), uint> dedupe)
    {
        // Per-face disambiguator for synthesized normals so adjacent faces sharing pos+uv
        // but with different synthesized normals don't collapse to one vertex.
        int normKey = tup.nrm >= 0 ? tup.nrm : -(faceCounter + 1);

        var key = (tup.p, tup.t, normKey);
        if (!dedupe.TryGetValue(key, out uint idx))
        {
            var pos = positions[tup.p];
            var uv = tup.t >= 0 ? uvs[tup.t] : Vector2.Zero;
            var nrm = tup.nrm >= 0 ? normals[tup.nrm] : faceNormal;

            outVertices.Add(new Vertex(pos, nrm, uv));
            idx = (uint)(outVertices.Count - 1);
            dedupe[key] = idx;
        }
        outIndices.Add(idx);
    }

    private static (int p, int t, int nrm) ParseFaceVertex(string token, int posCount, int uvCount, int nrmCount)
    {
        var parts = token.Split('/');
        int p = ParseObjIndex(parts[0], posCount);
        int t = (parts.Length > 1 && parts[1].Length > 0) ? ParseObjIndex(parts[1], uvCount) : -1;
        int nrm = (parts.Length > 2 && parts[2].Length > 0) ? ParseObjIndex(parts[2], nrmCount) : -1;
        return (p, t, nrm);
    }

    private static int ParseObjIndex(string s, int count)
    {
        int v = int.Parse(s, CultureInfo.InvariantCulture);
        return v > 0 ? v - 1 : count + v;
    }
}
