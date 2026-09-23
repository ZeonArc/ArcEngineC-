using ArcEngine.Engine.Animation;
using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Draws a <see cref="SkinnedMesh"/> using a skinning material (typically the
/// engine's <c>skinned.vert + basic.frag</c> pair). The renderer resolves the
/// bone palette by looking for an <see cref="Animator"/> sibling — if absent
/// or with no skeleton, the mesh draws in its bind pose.
/// </summary>
public class SkinnedMeshRenderer : Component
{
    public SkinnedMesh? Mesh;
    public Material?    Material;
}
