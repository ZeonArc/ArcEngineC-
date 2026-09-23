using OpenTK.Mathematics;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Procedurally-generated meshes used for debug/demo geometry that doesn't warrant
/// a model file (ground planes, gizmos, sky-spheres, etc.).
/// </summary>
public static class Primitives
{
    /// <summary>
    /// Build a flat XZ-plane centered at the origin, normal +Y, two triangles.
    /// </summary>
    /// <param name="size">Edge length of the square plane.</param>
    /// <param name="uvTiling">UV repeat count across the plane (e.g. 4 = texture tiles 4×4).</param>
    /// <summary>Reconstruction hint tag written by the scene serializer for a plane mesh.</summary>
    public static string PlaneMeshSource(float size, float uvTiling) =>
        $"primitive:plane:size={size.ToString(System.Globalization.CultureInfo.InvariantCulture)}:uv={uvTiling.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static Mesh CreatePlane(float size, float uvTiling = 1f)
    {
        float h = size * 0.5f;
        var n = Vector3.UnitY;

        var vertices = new[]
        {
            new Vertex(new Vector3(-h, 0f, -h), n, new Vector2(0f,        0f       )),
            new Vertex(new Vector3( h, 0f, -h), n, new Vector2(uvTiling,  0f       )),
            new Vertex(new Vector3( h, 0f,  h), n, new Vector2(uvTiling,  uvTiling)),
            new Vertex(new Vector3(-h, 0f,  h), n, new Vector2(0f,        uvTiling)),
        };

        // Counter-clockwise viewed from +Y (standard front-face winding).
        var indices = new uint[] { 0, 2, 1, 0, 3, 2 };

        return new Mesh(vertices, indices);
    }
}
