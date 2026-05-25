using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Component that says "this GameObject draws a mesh with this material".
/// Replaces the previous direct <c>GameObject.Mesh</c> + <c>GameObject.Material</c> fields.
/// Pure data — the <see cref="Renderer"/> queries the scene for these and draws them.
/// </summary>
public class MeshRenderer : Component
{
    public Mesh? Mesh;
    public Material? Material;
}
