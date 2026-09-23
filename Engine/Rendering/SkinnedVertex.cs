using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Skinned-mesh vertex — extends the standard <see cref="Vertex"/> layout with
/// four bone indices + four bone weights (glTF convention). Vertex shader uses
/// these to blend up to four bones' world matrices from the skinning palette.
///
/// Weights are expected to sum to 1.0. Loaders must normalize if the source
/// weights are unnormalized.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SkinnedVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public Vector4 Tangent;
    /// <summary>Indices into the skinning palette (<see cref="Animation.Skeleton.Palette"/>).</summary>
    public Vector4 BoneIndices;
    public Vector4 BoneWeights;

    /// <summary>Size of one skinned vertex in bytes (must match the GL stride).</summary>
    public const int SizeInBytes = 20 * sizeof(float);

    public SkinnedVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector4 tangent,
                         Vector4 boneIndices, Vector4 boneWeights)
    {
        Position    = position;
        Normal      = normal;
        UV          = uv;
        Tangent     = tangent;
        BoneIndices = boneIndices;
        BoneWeights = boneWeights;
    }
}
