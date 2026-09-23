using OpenTK.Mathematics;

using StbImageSharp;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Resources;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// Heightmap-driven terrain. Builds a regular XZ grid mesh from a grayscale
/// image (any StbImage-supported format), sizing it to <see cref="WorldSize"/>
/// and scaling heights by <see cref="HeightScale"/>. Attaches a <see cref="MeshRenderer"/>
/// with a <see cref="TerrainMaterial"/> using <c>terrain.vert/frag</c>.
///
/// Slope-based rock blending and height-based snow blending are handled inside
/// the shader from the vertex normal / world Y — pass 1..4 diffuse layers plus
/// an optional splat map to the <see cref="Material"/> to control the mix.
/// </summary>
public class Terrain : Component
{
    /// <summary>Path to the grayscale heightmap image on disk.</summary>
    public string? HeightmapPath;

    /// <summary>Total XZ extent of the terrain in world units. The heightmap is stretched to fit.</summary>
    public float WorldSize = 100f;

    /// <summary>Max world-space height (a heightmap value of 1.0 becomes this Y offset from Y=0).</summary>
    public float HeightScale = 10f;

    /// <summary>Optional built material. If null on Awake, a default is created + attached.</summary>
    public TerrainMaterial? Material;

    /// <summary>The generated mesh, exposed for gizmo/debug or LOD swaps.</summary>
    public Mesh? Mesh { get; private set; }

    public override void Awake()
    {
        if (string.IsNullOrEmpty(HeightmapPath))
        {
            Console.WriteLine($"[Terrain] '{GameObject.Name}' has no HeightmapPath — no mesh generated.");
            return;
        }
        if (!System.IO.File.Exists(HeightmapPath))
        {
            Console.WriteLine($"[Terrain] Heightmap not found: {HeightmapPath}");
            return;
        }

        // Load the heightmap as R8 grayscale (StbImage picks the closest matching channel set).
        int width, height;
        float[] heights;
        using (var stream = System.IO.File.OpenRead(HeightmapPath))
        {
            var img = ImageResult.FromStream(stream, ColorComponents.Grey);
            width = img.Width;
            height = img.Height;
            heights = new float[width * height];
            for (int i = 0; i < heights.Length; i++)
                heights[i] = img.Data[i] / 255f;
        }

        Mesh = BuildMesh(heights, width, height, WorldSize, HeightScale);

        // Ensure a MeshRenderer exists + reference our mesh/material.
        var mr = GameObject.GetComponent<MeshRenderer>() ?? GameObject.AddComponent<MeshRenderer>();
        mr.Mesh = Mesh;
        if (Material == null)
        {
            var shader = ArcEngine.Engine.Resources.Resources.LoadShader(
                "Assets/Shaders/terrain.vert", "Assets/Shaders/terrain.frag");
            Material = new TerrainMaterial(shader)
            {
                Color = new Vector3(0.55f, 0.55f, 0.45f),
                Roughness = 0.95f,
                Metallic = 0f,
            };
        }
        mr.Material = Material;
    }

    /// <summary>
    /// Sample the terrain height at a world-space (x, z). Returns 0 if outside the
    /// terrain footprint or if no heightmap has been loaded yet.
    /// </summary>
    public float SampleHeight(float worldX, float worldZ)
    {
        if (Mesh == null) return 0f;
        float half = WorldSize * 0.5f;
        float u = (worldX + half) / WorldSize;
        float v = (worldZ + half) / WorldSize;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;
        // The mesh's own AABB gives us max Y; use it as a coarse fallback since we
        // don't cache the source heightmap.
        return Mesh.LocalAABB.Max.Y * (u * v);   // placeholder — v1 skips accurate CPU sampling
    }

    // ============================================================================
    // Mesh generation
    // ============================================================================

    private static Mesh BuildMesh(float[] heights, int hmW, int hmH, float worldSize, float heightScale)
    {
        var verts = new Vertex[hmW * hmH];
        float halfW = worldSize * 0.5f;
        float halfH = worldSize * 0.5f;

        // Positions + UVs (temporary normals; recomputed below).
        for (int y = 0; y < hmH; y++)
        for (int x = 0; x < hmW; x++)
        {
            float fx = x / (float)(hmW - 1);
            float fy = y / (float)(hmH - 1);
            float h = heights[y * hmW + x] * heightScale;
            verts[y * hmW + x] = new Vertex(
                new Vector3(fx * worldSize - halfW, h, fy * worldSize - halfH),
                Vector3.UnitY,
                new Vector2(fx, fy));
        }

        // Central-difference normals — flat where neighbors are missing.
        for (int y = 0; y < hmH; y++)
        for (int x = 0; x < hmW; x++)
        {
            float hL = heights[y * hmW + System.Math.Max(x - 1, 0)];
            float hR = heights[y * hmW + System.Math.Min(x + 1, hmW - 1)];
            float hD = heights[System.Math.Max(y - 1, 0) * hmW + x];
            float hU = heights[System.Math.Min(y + 1, hmH - 1) * hmW + x];

            // Convert texel-space slope back into world-space using the terrain's cell size.
            float cellSizeX = worldSize / (hmW - 1);
            float cellSizeZ = worldSize / (hmH - 1);
            float dx = (hR - hL) * heightScale / (2f * cellSizeX);
            float dz = (hU - hD) * heightScale / (2f * cellSizeZ);

            var n = Vector3.Normalize(new Vector3(-dx, 1f, -dz));
            verts[y * hmW + x].Normal = n;
        }

        // Two triangles per cell.
        var indices = new uint[(hmW - 1) * (hmH - 1) * 6];
        int k = 0;
        for (int y = 0; y < hmH - 1; y++)
        for (int x = 0; x < hmW - 1; x++)
        {
            uint tl = (uint)(y * hmW + x);
            uint tr = tl + 1;
            uint bl = tl + (uint)hmW;
            uint br = bl + 1;
            // CCW when viewed from +Y (standard front-face winding).
            indices[k++] = tl; indices[k++] = bl; indices[k++] = tr;
            indices[k++] = tr; indices[k++] = bl; indices[k++] = br;
        }

        return new Mesh(verts, indices);
    }
}
