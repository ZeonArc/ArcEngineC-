using OpenTK.Mathematics;
using SharpGLTF.Schema2;

using ArcEngine.Engine.Rendering;

using GltfMaterial = SharpGLTF.Schema2.Material;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Static-mesh GLTF/GLB loader backed by <c>SharpGLTF.Core</c>. Reads each mesh primitive's
/// POSITION/NORMAL/TEXCOORD_0 vertex streams and the optional index buffer, and converts the
/// PBR base-color channel (factor + texture) into the engine's diffuse <see cref="MaterialData"/>.
///
/// Skinning, morph targets, and animations are intentionally out of scope (Phase 4 = static meshes).
/// </summary>
public static class GltfLoader
{
    public static ModelData Load(string path)
    {
        var data = new ModelData { SourcePath = path };
        var model = ModelRoot.Load(path);

        var materialIndexMap = new Dictionary<GltfMaterial, int>();

        foreach (var mesh in model.LogicalMeshes)
        {
            foreach (var prim in mesh.Primitives)
            {
                var posAccessor = prim.GetVertexAccessor("POSITION");
                if (posAccessor == null) continue;

                var positions = posAccessor.AsVector3Array();
                int vCount = positions.Count;

                var normalsAccessor = prim.GetVertexAccessor("NORMAL");
                var normals = normalsAccessor?.AsVector3Array();

                var uvAccessor = prim.GetVertexAccessor("TEXCOORD_0");
                var uvs = uvAccessor?.AsVector2Array();

                var verts = new Vertex[vCount];
                for (int i = 0; i < vCount; i++)
                {
                    var p = positions[i];
                    var n = normals != null
                        ? normals[i]
                        : new System.Numerics.Vector3(0, 0, 1);
                    var u = uvs != null
                        ? uvs[i]
                        : System.Numerics.Vector2.Zero;

                    verts[i] = new Vertex(
                        new Vector3(p.X, p.Y, p.Z),
                        new Vector3(n.X, n.Y, n.Z),
                        new Vector2(u.X, u.Y));
                }

                // Indices (GLTF spec: 0-based, GL-friendly already).
                uint[] indices;
                var idxAcc = prim.IndexAccessor;
                if (idxAcc != null)
                {
                    var src = idxAcc.AsIndicesArray();
                    indices = new uint[src.Count];
                    for (int i = 0; i < src.Count; i++) indices[i] = src[i];
                }
                else
                {
                    indices = new uint[vCount];
                    for (int i = 0; i < vCount; i++) indices[i] = (uint)i;
                }

                // Materials are deduplicated across primitives that share one.
                int matIdx;
                if (prim.Material != null)
                {
                    if (!materialIndexMap.TryGetValue(prim.Material, out matIdx))
                    {
                        data.Materials.Add(ConvertMaterial(prim.Material));
                        matIdx = data.Materials.Count - 1;
                        materialIndexMap[prim.Material] = matIdx;
                    }
                }
                else
                {
                    if (data.Materials.Count == 0)
                        data.Materials.Add(new MaterialData { Name = "default" });
                    matIdx = 0;
                }

                data.Submeshes.Add(new MeshData
                {
                    Vertices = verts,
                    Indices = indices,
                    MaterialIndex = matIdx,
                    Name = mesh.Name ?? $"primitive_{data.Submeshes.Count}"
                });
            }
        }

        // Ensure ModelBuilder always has at least one material to fall back on.
        if (data.Materials.Count == 0)
            data.Materials.Add(new MaterialData { Name = "default" });

        return data;
    }

    private static MaterialData ConvertMaterial(GltfMaterial mat)
    {
        var md = new MaterialData { Name = mat.Name ?? "material" };

        var baseColor = mat.FindChannel("BaseColor");
        if (baseColor.HasValue)
        {
            var ch = baseColor.Value;

            // Base-color factor (RGBA); we drop alpha to drop into the engine's Vector3 diffuse.
            var c = ch.Color;
            md.DiffuseColor = new Vector3(c.X, c.Y, c.Z);

            // Embedded base-color texture, if present.
            var tex = ch.Texture;
            if (tex != null && tex.PrimaryImage != null)
            {
                var memImg = tex.PrimaryImage.Content;
                if (memImg.IsValid)
                {
                    md.DiffuseMapBytes = memImg.Content.ToArray();
                }
            }
        }

        return md;
    }
}
