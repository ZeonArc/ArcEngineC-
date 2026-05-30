#version 330 core

// Sprint 5c PBR fragment shader.
//   * Material model: metallic-roughness (glTF convention).
//   * BRDF: Cook-Torrance (GGX NDF + Smith geometry + Schlick Fresnel).
//   * Normal mapping via tangent-space sample (TBN built from interpolated tangent).
//   * Ambient via env-color split (no IBL): diffuse + specular contributions
//     blended by metallic/roughness.
//   * Shadow factor (existing 3x3 PCF) applied to the directional light only.
//   * Linear-space lighting throughout; final Reinhard tonemap + 1/2.2 gamma.

#define MAX_POINT_LIGHTS 4
const float PI = 3.14159265359;

// ---- Varyings (from basic.vert) ------------------------------------------
in vec3 vWorldPos;
in vec3 vWorldNormal;
in vec3 vWorldTangent;
in vec2 vUV;
in vec4 vFragPosLightSpace;

out vec4 FragColor;

// ---- Lights --------------------------------------------------------------
struct DirLight {
    vec3 direction;
    vec3 color;
};

struct PointLight {
    vec3  position;
    vec3  color;
    float constant;
    float linear;
    float quadratic;
};

uniform DirLight   dirLight;
uniform int        numPointLights;
uniform PointLight pointLights[MAX_POINT_LIGHTS];

uniform vec3 viewPos;

// Environment ambient split (no real IBL -- single colors per channel).
uniform vec3 u_envDiffuse;
uniform vec3 u_envSpecular;

// ---- PBR material params -------------------------------------------------
uniform vec3  u_baseColor;
uniform float u_metallic;
uniform float u_roughness;
uniform float u_ao;
uniform float u_normalStrength;

uniform int u_hasBaseColorMap;
uniform int u_hasMRMap;
uniform int u_hasNormalMap;
uniform int u_hasOcclusionMap;

uniform sampler2D u_baseColorMap;
uniform sampler2D u_metallicRoughnessMap;
uniform sampler2D u_normalMap;
uniform sampler2D u_occlusionMap;

// ---- Shadow --------------------------------------------------------------
uniform sampler2D shadowMap;
uniform int       dirLightCastsShadows;

// ============================================================================
// BRDF helpers
// ============================================================================

// GGX / Trowbridge-Reitz normal distribution.
float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a  = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denom = NdotH2 * (a2 - 1.0) + 1.0;
    return a2 / (PI * denom * denom);
}

// Smith with Schlick-GGX, k tuned for direct lighting (r+1)^2/8.
float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    return GeometrySchlickGGX(NdotV, roughness) *
           GeometrySchlickGGX(NdotL, roughness);
}

// Schlick approximation of Fresnel.
vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// ============================================================================
// Shadow (3x3 PCF + slope-scaled bias) -- same as Sprint 5b.
// ============================================================================

float ShadowCalculation(vec4 posLS, vec3 N, vec3 L)
{
    vec3 projCoords = posLS.xyz / posLS.w;
    projCoords = projCoords * 0.5 + 0.5;
    if (projCoords.z > 1.0) return 0.0;

    float currentDepth = projCoords.z;
    float bias = max(0.005 * (1.0 - dot(N, L)), 0.0005);

    float shadow = 0.0;
    vec2 texelSize = 1.0 / vec2(textureSize(shadowMap, 0));
    for (int x = -1; x <= 1; ++x)
    for (int y = -1; y <= 1; ++y)
    {
        float pcfDepth = texture(shadowMap, projCoords.xy + vec2(x, y) * texelSize).r;
        shadow += (currentDepth - bias) > pcfDepth ? 1.0 : 0.0;
    }
    return shadow / 9.0;
}

// ============================================================================
// Per-light Cook-Torrance contribution.
// ============================================================================

vec3 RadianceFromLight(vec3 L, vec3 lightColor,
                       vec3 N, vec3 V,
                       vec3 baseColor, float metallic, float roughness, vec3 F0)
{
    float NdotL = max(dot(N, L), 0.0);
    if (NdotL <= 0.0) return vec3(0.0);

    vec3  H = normalize(V + L);

    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);
    vec3  F = FresnelSchlick(max(dot(H, V), 0.0), F0);

    float NdotV = max(dot(N, V), 0.0);
    vec3  numerator   = D * G * F;
    float denominator = 4.0 * NdotV * NdotL + 1e-4;
    vec3  specular = numerator / denominator;

    vec3 kS = F;
    vec3 kD = (1.0 - kS) * (1.0 - metallic);   // metals have no diffuse term

    return (kD * baseColor / PI + specular) * lightColor * NdotL;
}

// ============================================================================
// Main
// ============================================================================

void main()
{
    // ---- Sample maps -----------------------------------------------------
    vec3 baseColor = u_baseColor;
    if (u_hasBaseColorMap == 1)
        baseColor *= texture(u_baseColorMap, vUV).rgb;   // texture is sRGB -> auto linear

    float metallic  = u_metallic;
    float roughness = u_roughness;
    if (u_hasMRMap == 1)
    {
        // glTF packing: G = roughness, B = metallic.
        vec3 mr = texture(u_metallicRoughnessMap, vUV).rgb;
        roughness *= mr.g;
        metallic  *= mr.b;
    }
    roughness = clamp(roughness, 0.04, 1.0);

    float ao = u_ao;
    if (u_hasOcclusionMap == 1)
        ao *= texture(u_occlusionMap, vUV).r;

    // ---- Build TBN, optionally perturb normal ----------------------------
    vec3 N = normalize(vWorldNormal);

    if (u_hasNormalMap == 1 && length(vWorldTangent) > 1e-4)
    {
        // Re-orthogonalize T against N, then build TBN.
        vec3 T = normalize(vWorldTangent - N * dot(N, vWorldTangent));
        vec3 B = cross(N, T);
        mat3 TBN = mat3(T, B, N);

        vec3 sampledN = texture(u_normalMap, vUV).rgb * 2.0 - 1.0;
        // Strength applies to XY only (Z keeps its [-1,1] range).
        sampledN.xy *= u_normalStrength;
        N = normalize(TBN * sampledN);
    }

    vec3 V = normalize(viewPos - vWorldPos);

    // ---- F0 (specular reflectance at normal incidence) -------------------
    // Dielectrics ~ 4% (vec3(0.04)); metals tint it by their albedo.
    vec3 F0 = mix(vec3(0.04), baseColor, metallic);

    // ---- Per-light radiance ---------------------------------------------
    vec3 Lo = vec3(0.0);

    // Directional light (with optional shadow factor on contribution).
    {
        vec3 L = normalize(-dirLight.direction);
        vec3 contribution = RadianceFromLight(L, dirLight.color, N, V,
                                              baseColor, metallic, roughness, F0);
        if (dirLightCastsShadows == 1)
        {
            float shadow = ShadowCalculation(vFragPosLightSpace, N, L);
            contribution *= (1.0 - shadow);
        }
        Lo += contribution;
    }

    // Point lights.
    int npl = min(numPointLights, MAX_POINT_LIGHTS);
    for (int i = 0; i < npl; ++i)
    {
        vec3  toLight = pointLights[i].position - vWorldPos;
        float dist    = length(toLight);
        vec3  L       = toLight / max(dist, 1e-5);

        float attenuation = 1.0 / (pointLights[i].constant
                                 + pointLights[i].linear * dist
                                 + pointLights[i].quadratic * dist * dist);

        vec3 contribution = RadianceFromLight(L, pointLights[i].color, N, V,
                                              baseColor, metallic, roughness, F0);
        Lo += contribution * attenuation;
    }

    // ---- Ambient (env-color split, hand-tuned non-IBL approximation) ----
    // Diffuse env: scaled by (1-metallic) since metals lack a diffuse term.
    vec3 envDiffuse  = u_envDiffuse  * baseColor * (1.0 - metallic);
    // Specular env: F0 colors the ambient highlight; (1-roughness) brightens
    // smooth surfaces vs rough ones.
    vec3 envSpecular = u_envSpecular * F0 * (1.0 - roughness);

    vec3 ambientColor = (envDiffuse + envSpecular) * ao;

    vec3 color = ambientColor + Lo;

    // ---- Tonemap (Reinhard) + gamma -------------------------------------
    color = color / (color + vec3(1.0));
    color = pow(color, vec3(1.0 / 2.2));

    FragColor = vec4(color, 1.0);
}
