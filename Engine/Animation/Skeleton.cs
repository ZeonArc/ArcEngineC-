using OpenTK.Mathematics;

namespace ArcEngine.Engine.Animation;

/// <summary>
/// Ordered list of bones sharing one skinning hierarchy. Bones MUST appear before
/// their descendants in the <see cref="Bones"/> array so <see cref="ComputePalette"/>
/// can build world matrices in a single pass without recursion.
/// </summary>
public class Skeleton
{
    public readonly Bone[] Bones;

    /// <summary>Per-bone world matrix (bone-local → world). Reused every frame.</summary>
    private readonly Matrix4[] _worldMatrices;

    /// <summary>Skinning palette: worldMatrix * inverseBindMatrix. Sent to the vertex shader.</summary>
    private readonly Matrix4[] _palette;

    public IReadOnlyList<Matrix4> Palette => _palette;

    /// <summary>Maximum number of bones addressable from the vertex shader; matches MAX_BONES in skinned.vert.</summary>
    public const int MaxBones = 128;

    public Skeleton(Bone[] bones)
    {
        if (bones.Length > MaxBones)
            throw new ArgumentException($"Skeleton has {bones.Length} bones but the shader supports at most {MaxBones}.");
        Bones = bones;
        _worldMatrices = new Matrix4[bones.Length];
        _palette       = new Matrix4[bones.Length];
    }

    /// <summary>
    /// Compose each bone's current local pose into a world matrix, then multiply by
    /// its inverse-bind to produce the skinning palette that gets uploaded to the
    /// vertex shader. Call once per frame after sampling a clip.
    /// </summary>
    public void ComputePalette()
    {
        for (int i = 0; i < Bones.Length; i++)
        {
            ref var b = ref Bones[i];
            Matrix4 local =
                  Matrix4.CreateScale(b.LocalScale)
                * Matrix4.CreateFromQuaternion(b.LocalRotation)
                * Matrix4.CreateTranslation(b.LocalPosition);

            _worldMatrices[i] = b.ParentIndex >= 0
                ? local * _worldMatrices[b.ParentIndex]
                : local;

            _palette[i] = b.InverseBindMatrix * _worldMatrices[i];
        }
    }
}
