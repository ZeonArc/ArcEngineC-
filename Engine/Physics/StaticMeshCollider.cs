using BepuPhysics;
using BepuPhysics.Collidables;

using OtkVec3 = OpenTK.Mathematics.Vector3;
using SnVec3  = System.Numerics.Vector3;

using EngineMesh = ArcEngine.Engine.Rendering.Mesh;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Static triangle-mesh collider. Feeds every triangle of a given mesh into
/// BepuPhysics's <see cref="Mesh"/> shape. STATIC ONLY — a triangle mesh cannot
/// be used as a dynamic collider (Bepu's design decision, since concave meshes
/// have no inertia analytics). Make sure the sibling <see cref="Rigidbody"/>
/// has <c>IsStatic = true</c>.
///
/// v1 assumes an axis-aligned mesh; a per-axis <see cref="Scale"/> is applied
/// at shape build time.
/// </summary>
public class StaticMeshCollider : Collider
{
    /// <summary>Vertex positions in mesh-local space.</summary>
    public OtkVec3[] Positions = System.Array.Empty<OtkVec3>();

    /// <summary>Triangle indices, 3 per triangle. Winding is irrelevant for collision.</summary>
    public uint[] Indices = System.Array.Empty<uint>();

    /// <summary>Uniform / per-axis scale applied at shape construction.</summary>
    public OtkVec3 Scale = OtkVec3.One;

    /// <summary>
    /// Convenience: initialise from an engine <see cref="EngineMesh"/>. Caller must
    /// still populate <see cref="Positions"/> / <see cref="Indices"/> since the GPU
    /// mesh doesn't retain them — use this to plug in a source you already have.
    /// </summary>
    public void SetTriangles(OtkVec3[] positions, uint[] indices)
    {
        Positions = positions;
        Indices = indices;
    }

    public override (TypedIndex Shape, BodyInertia Inertia) BuildShape(PhysicsWorld world, float mass)
    {
        int triCount = Indices.Length / 3;
        if (Positions.Length < 3 || triCount < 1)
        {
            Console.WriteLine($"[StaticMeshCollider] '{GameObject.Name}': no triangles; using a unit box fallback.");
            var box = new Box(1f, 1f, 1f);
            return (world.Simulation.Shapes.Add(box), default);
        }

        world.BufferPool.Take<Triangle>(triCount, out var buffer);
        for (int t = 0; t < triCount; t++)
        {
            var a = Positions[Indices[t * 3 + 0]];
            var b = Positions[Indices[t * 3 + 1]];
            var c = Positions[Indices[t * 3 + 2]];
            buffer[t] = new Triangle(
                new SnVec3(a.X, a.Y, a.Z),
                new SnVec3(b.X, b.Y, b.Z),
                new SnVec3(c.X, c.Y, c.Z));
        }

        var mesh = new Mesh(buffer, new SnVec3(Scale.X, Scale.Y, Scale.Z), world.BufferPool);
        var shape = world.Simulation.Shapes.Add(mesh);
        // Static bodies ignore inertia; Bepu accepts `default` for statics via BodyInertia(default).
        return (shape, default);
    }
}
