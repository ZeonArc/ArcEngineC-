using OpenTK.Mathematics;

namespace ArcEngine.Engine.Animation;

/// <summary>
/// One joint in a <see cref="Skeleton"/>. Bones are stored as a flat, parent-index
/// array — the skeleton walks the parent chain to build world matrices in a single
/// pre-order pass. Local pose (<see cref="LocalPosition"/> / <see cref="LocalRotation"/> /
/// <see cref="LocalScale"/>) is the current per-frame pose; <see cref="InverseBindMatrix"/>
/// is baked at import time from the mesh's bind pose.
/// </summary>
public struct Bone
{
    public string Name;

    /// <summary>Index into the owning <see cref="Skeleton.Bones"/> array, or -1 for the root.</summary>
    public int ParentIndex;

    // Current local pose — sampled from an animation clip each frame.
    public Vector3    LocalPosition;
    public Quaternion LocalRotation;
    public Vector3    LocalScale;

    /// <summary>Bone-space → mesh-space inverse-bind. Baked from the skin at import.</summary>
    public Matrix4 InverseBindMatrix;

    public Bone(string name, int parentIndex, Matrix4 inverseBind)
    {
        Name = name;
        ParentIndex = parentIndex;
        LocalPosition = Vector3.Zero;
        LocalRotation = Quaternion.Identity;
        LocalScale    = Vector3.One;
        InverseBindMatrix = inverseBind;
    }
}
