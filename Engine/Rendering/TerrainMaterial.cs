using OpenTK.Graphics.OpenGL4;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// PBR material specialised for terrain — 4 diffuse layer textures blended by
/// an optional splat map (RGBA weights per texel of the terrain's UVs), plus a
/// world-space tiling factor and thresholds for the auto-slope / auto-snow
/// blending done by <c>terrain.frag</c>.
///
/// Layer 0..3 are the same base color slots the shader samples; the standard
/// PBR knobs (metallic, roughness, AO, normal strength) are inherited from
/// <see cref="Material"/>.
/// </summary>
public class TerrainMaterial : Material
{
    public Texture? Layer0;
    public Texture? Layer1;
    public Texture? Layer2;
    public Texture? Layer3;
    public Texture? SplatMap;

    /// <summary>World-space tiling multiplier. Higher = finer detail per unit.</summary>
    public float UvTiling = 0.25f;

    /// <summary>Slope cosine below which layer 2 (rock) starts taking over. 1.0 = flat, 0 = vertical.</summary>
    public float SlopeBias = 0.85f;

    /// <summary>World-space Y at which layer 3 (snow / accent) blends in.</summary>
    public float SnowHeight = 12f;

    public TerrainMaterial(Shader shader) : base(shader) { }

    public override void Apply()
    {
        Shader.Use();

        // Reuse the base PBR uniforms — baseColor tint stays available even
        // though the splat blend usually drives the color.
        Shader.SetFloat("u_metallic", Metallic);
        Shader.SetFloat("u_roughness", Roughness);
        Shader.SetFloat("u_ao", AmbientOcclusion);
        Shader.SetFloat("u_normalStrength", NormalStrength);

        // Splat layers on units 0..3, splat map on 4.
        BindOrFallback(0, Layer0, "u_layer0");
        BindOrFallback(1, Layer1, "u_layer1");
        BindOrFallback(2, Layer2, "u_layer2");
        BindOrFallback(3, Layer3, "u_layer3");
        BindOrFallback(4, SplatMap, "u_splatMap");
        Shader.SetInt("u_hasSplatMap", SplatMap != null ? 1 : 0);

        Shader.SetFloat("u_uvTiling", UvTiling);
        Shader.SetFloat("u_slopeBias", SlopeBias);
        Shader.SetFloat("u_snowHeight", SnowHeight);
    }
}
