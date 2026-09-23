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

    /// <summary>
    /// Optional level-of-detail slots. When populated, the renderer picks the first
    /// entry whose <see cref="MeshLod.MaxDistance"/> is greater than the camera-to-
    /// renderer distance for the frame. <see cref="Mesh"/> serves as an implicit
    /// LOD0 when this list is empty; when non-empty, LOD0 is the first entry and
    /// <see cref="Mesh"/> is ignored.
    /// </summary>
    public List<MeshLod>? Lods;

    /// <summary>
    /// Opaque reconstruction hint used by the scene serializer. Formats:
    /// <list type="bullet">
    ///   <item><c>primitive:plane:size=X:uv=Y</c> — built via <see cref="Primitives.CreatePlane"/></item>
    /// </list>
    /// Null means the mesh is not reconstructable via <c>MeshSource</c> alone and either
    /// the owning GameObject is rebuilt wholesale from a <c>SourceModelPath</c>, or the
    /// mesh will not survive save/load.
    /// </summary>
    public string? MeshSource;

    /// <summary>
    /// Pick the mesh appropriate for a given camera distance. Returns <see cref="Mesh"/>
    /// when no LODs are configured; otherwise the highest-detail LOD whose
    /// <see cref="MeshLod.MaxDistance"/> covers the distance, falling back to the
    /// last (lowest-detail) LOD past all thresholds.
    /// </summary>
    public Mesh? SelectMesh(float cameraDistance)
    {
        if (Lods == null || Lods.Count == 0) return Mesh;
        for (int i = 0; i < Lods.Count; i++)
        {
            if (cameraDistance <= Lods[i].MaxDistance) return Lods[i].Mesh;
        }
        return Lods[^1].Mesh;
    }
}

/// <summary>
/// One entry in a <see cref="MeshRenderer.Lods"/> list.
/// <see cref="MaxDistance"/> is the camera-space distance (in world units) up to
/// which this level is used; ordering the list from smallest to largest gives the
/// standard "high detail near / low detail far" behaviour.
/// </summary>
public struct MeshLod
{
    public Mesh Mesh;
    public float MaxDistance;

    public MeshLod(Mesh mesh, float maxDistance)
    {
        Mesh = mesh;
        MaxDistance = maxDistance;
    }
}
