using OpenTK.Mathematics;

namespace ArcEngine.Engine.Loaders;

/// <summary>
/// Loader-agnostic material description — PBR metallic-roughness model.
/// Both path-based (OBJ/MTL) and embedded-bytes (GLB) texture sources are supported.
/// <see cref="ModelBuilder"/> prefers bytes if present, otherwise falls back to a path.
///
/// All textures are uploaded as either sRGB (color) or linear (data). The renderer's
/// shader expects:
/// <list type="bullet">
///   <item><b>BaseColor</b> texture is sRGB (auto-linearized on sample).</item>
///   <item><b>MetallicRoughness</b> texture is linear; G channel = roughness, B channel = metallic (glTF convention).</item>
///   <item><b>Normal</b> texture is linear, in tangent space, [-1,1] encoded as [0,1].</item>
///   <item><b>Occlusion</b> texture is linear; R channel = AO factor.</item>
/// </list>
/// </summary>
public class MaterialData
{
    public string Name { get; set; } = "default";

    // ---- Base color (a.k.a. albedo / diffuse) -------------------------------

    /// <summary>Base color factor multiplied with the BaseColor texture sample.</summary>
    public Vector3 DiffuseColor { get; set; } = Vector3.One;

    public string? DiffuseMapPath { get; set; }
    public byte[]? DiffuseMapBytes { get; set; }

    // ---- Metallic / roughness scalars + map ---------------------------------

    /// <summary>0 = dielectric (plastic / wood / stone), 1 = metal.</summary>
    public float Metallic { get; set; } = 0f;

    /// <summary>0 = mirror smooth, 1 = perfectly rough/diffuse. 0.7 ≈ matte default.</summary>
    public float Roughness { get; set; } = 0.7f;

    /// <summary>Combined MR map (glTF packing: G=roughness, B=metallic).</summary>
    public string? MetallicRoughnessMapPath { get; set; }
    public byte[]? MetallicRoughnessMapBytes { get; set; }

    // ---- Normal map ---------------------------------------------------------

    public string? NormalMapPath { get; set; }
    public byte[]? NormalMapBytes { get; set; }

    /// <summary>Strength scalar applied to the perturbation; 0 disables, 1 = full effect.</summary>
    public float NormalStrength { get; set; } = 1f;

    // ---- Ambient occlusion --------------------------------------------------

    /// <summary>AO scalar multiplier when no AO map is present (1 = no occlusion).</summary>
    public float AmbientOcclusion { get; set; } = 1f;

    public string? OcclusionMapPath { get; set; }
    public byte[]? OcclusionMapBytes { get; set; }

    // ---- Legacy (Sprint 5a Blinn-Phong) — kept readable so old MTL files still parse --

    /// <summary>Legacy specular map (Sprint 5a). Unused in PBR but parsed for backward compat.</summary>
    public string? SpecularMapPath { get; set; }

    /// <summary>Legacy Blinn-Phong shininess (Sprint 5a). Unused in PBR.</summary>
    public float Shininess { get; set; } = 32f;
}
