using OpenTK.Mathematics;
using SharpGLTF.Schema2;

using ArcEngine.Engine.Rendering;

using GltfMaterial = SharpGLTF.Schema2.Material;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Static-mesh GLTF/GLB loader backed by <c>SharpGLTF.Core</c>. Reads each mesh primitive's
/// POSITION/NORMAL/TEXCOORD_0/TANGENT vertex streams and the optional index buffer, and
/// converts the PBR metallic-roughness material channels (BaseColor + MetallicRoughness +
/// Normal + Occlusion) into <see cref="MaterialData"/> for the engine.
///
/// Skinning, morph targets, and animations remain out of scope (static meshes only).
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

                // glTF TANGENT is vec4 (xyz = tangent, w = bitangent sign ±1).
                // The sign gets carried into the engine's vertex layout so mirrored-UV
                // meshes sample the normal map with the correct handedness.
                var tangentAccessor = prim.GetVertexAccessor("TANGENT");
                var tangents = tangentAccessor?.AsVector4Array();

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
                    var tg = tangents != null
                        ? tangents[i]
                        : new System.Numerics.Vector4(0f, 0f, 0f, 1f);

                    verts[i] = new Vertex(
                        new Vector3(p.X, p.Y, p.Z),
                        new Vector3(n.X, n.Y, n.Z),
                        new Vector2(u.X, u.Y),
                        new Vector4(tg.X, tg.Y, tg.Z, tg.W));
                }

                // Indices (glTF spec: 0-based, GL-friendly already).
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

                // Fallback: glTF didn't provide TANGENT — compute from UV gradients.
                if (tangents == null)
                    TangentGenerator.GenerateInPlace(verts, indices);

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

        if (data.Materials.Count == 0)
            data.Materials.Add(new MaterialData { Name = "default" });

        return data;
    }

    private static MaterialData ConvertMaterial(GltfMaterial mat)
    {
        var md = new MaterialData { Name = mat.Name ?? "material" };

        // ---- Base color (sRGB) -------------------------------------------
        var baseColor = mat.FindChannel("BaseColor");
        if (baseColor.HasValue)
        {
            var ch = baseColor.Value;
            var c = ch.Color;
            md.DiffuseColor = new Vector3(c.X, c.Y, c.Z);
            md.DiffuseMapBytes = ExtractTextureBytes(ch.Texture);
        }

        // ---- Metallic-roughness factors + map (linear) -------------------
        var mr = mat.FindChannel("MetallicRoughness");
        if (mr.HasValue)
        {
            var ch = mr.Value;
            md.Metallic = (float?)ch.GetFactor("MetallicFactor") ?? 1f;
            md.Roughness = (float?)ch.GetFactor("RoughnessFactor") ?? 1f;
            md.MetallicRoughnessMapBytes = ExtractTextureBytes(ch.Texture);
        }

        // ---- Normal map (linear, tangent space) --------------------------
        var nrm = mat.FindChannel("Normal");
        if (nrm.HasValue)
        {
            var ch = nrm.Value;
            md.NormalStrength = (float?)ch.GetFactor("NormalScale") ?? 1f;
            md.NormalMapBytes = ExtractTextureBytes(ch.Texture);
        }

        // ---- Occlusion (linear) ------------------------------------------
        var occ = mat.FindChannel("Occlusion");
        if (occ.HasValue)
        {
            var ch = occ.Value;
            md.AmbientOcclusion = (float?)ch.GetFactor("OcclusionStrength") ?? 1f;
            md.OcclusionMapBytes = ExtractTextureBytes(ch.Texture);
        }

        return md;
    }

    private static byte[]? ExtractTextureBytes(SharpGLTF.Schema2.Texture? tex)
    {
        if (tex == null || tex.PrimaryImage == null) return null;
        var memImg = tex.PrimaryImage.Content;
        if (!memImg.IsValid) return null;
        return memImg.Content.ToArray();
    }
}
