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
#define SHADOW_CASCADES 3
const float PI = 3.14159265359;

// ---- Varyings (from basic.vert) ------------------------------------------
in vec3  vWorldPos;
in vec3  vWorldNormal;
in vec3  vWorldTangent;
in float vTangentSign;
in vec2  vUV;
in float vViewZ;

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

// Environment ambient split - hand-tuned fallback when no IBL environment is bound.
uniform vec3 u_envDiffuse;
uniform vec3 u_envSpecular;

// IBL - real image-based lighting when u_iblEnabled == 1.
uniform samplerCube u_irradianceMap;    // diffuse ambient
uniform samplerCube u_prefilteredMap;   // GGX-prefiltered specular mip chain
uniform sampler2D   u_brdfLut;          // split-sum scale/bias LUT
uniform int         u_iblEnabled;
uniform float       u_prefilteredMipCount;

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

// ---- Shadow (cascaded) ---------------------------------------------------
uniform sampler2DArray shadowMapArray;
uniform mat4           lightSpaceMatrices[SHADOW_CASCADES];
uniform float          cascadeSplits[SHADOW_CASCADES];   // view-Z (positive) at end of each cascade
uniform int            dirLightCastsShadows;

// ---- Shadow (point) ------------------------------------------------------
// Only one point light gets a shadow cube in v1. pointShadowLightIndex says
// which entry of pointLights[] it applies to (-1 = no point shadow).
uniform samplerCube pointShadowMap;
uniform vec3        pointShadowLightPos;
uniform float       pointShadowFarPlane;
uniform int         pointShadowLightIndex;

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

// Pick the tightest cascade whose split still covers this fragment. Falls back
// to the last (largest) cascade for anything past all splits.
int SelectCascade()
{
    for (int i = 0; i < SHADOW_CASCADES; ++i)
    {
        if (vViewZ < cascadeSplits[i]) return i;
    }
    return SHADOW_CASCADES - 1;
}

/// Point-light cubemap shadow. Returns 1 = fully shadowed, 0 = lit.
float PointShadowCalculation(vec3 fragWorldPos)
{
    vec3 toFrag = fragWorldPos - pointShadowLightPos;
    float currentDist = length(toFrag) / pointShadowFarPlane;
    float sampledDist = texture(pointShadowMap, normalize(toFrag)).r;
    float bias = 0.005;
    return currentDist - bias > sampledDist ? 1.0 : 0.0;
}

float ShadowCalculation(int cascade, vec3 N, vec3 L)
{
    vec4 posLS = lightSpaceMatrices[cascade] * vec4(vWorldPos, 1.0);
    vec3 projCoords = posLS.xyz / posLS.w;
    projCoords = projCoords * 0.5 + 0.5;
    if (projCoords.z > 1.0) return 0.0;

    float currentDepth = projCoords.z;
    // Scale bias by cascade - larger cascades cover more world per texel, need bigger bias.
    float cascadeScale = float(cascade + 1);
    float bias = max(0.001 * cascadeScale * (1.0 - dot(N, L)), 0.0002 * cascadeScale);

    float shadow = 0.0;
    vec2 texelSize = 1.0 / vec2(textureSize(shadowMapArray, 0).xy);
    for (int x = -1; x <= 1; ++x)
    for (int y = -1; y <= 1; ++y)
    {
        vec2 uv = projCoords.xy + vec2(x, y) * texelSize;
        float pcfDepth = texture(shadowMapArray, vec3(uv, float(cascade))).r;
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
        // Re-orthogonalize T against N, then build TBN. The bitangent sign carried
        // from the vertex layout (glTF's TANGENT.w) flips B for mirrored UVs so the
        // normal map samples the right handedness.
        vec3 T = normalize(vWorldTangent - N * dot(N, vWorldTangent));
        vec3 B = cross(N, T) * vTangentSign;
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
            int cascade = SelectCascade();
            float shadow = ShadowCalculation(cascade, N, L);
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

        if (i == pointShadowLightIndex)
        {
            float shadow = PointShadowCalculation(vWorldPos);
            contribution *= (1.0 - shadow);
        }

        Lo += contribution * attenuation;
    }

    // ---- Ambient ---------------------------------------------------------
    // IBL split-sum when an environment is bound; hand-tuned fallback otherwise.
    vec3 ambientColor;
    if (u_iblEnabled == 1)
    {
        // Fresnel for the ambient dir with a roughness-aware widening (Sebastien
        // Lagarde) so grazing angles on rough surfaces don't over-brighten.
        vec3 F = F0 + (max(vec3(1.0 - roughness), F0) - F0) *
                     pow(clamp(1.0 - max(dot(N, V), 0.0), 0.0, 1.0), 5.0);

        vec3 kS = F;
        vec3 kD = (1.0 - kS) * (1.0 - metallic);

        vec3 irradiance = texture(u_irradianceMap, N).rgb;
        vec3 diffuse    = irradiance * baseColor;

        vec3 R = reflect(-V, N);
        float mip = roughness * (u_prefilteredMipCount - 1.0);
        vec3 prefilteredColor = textureLod(u_prefilteredMap, R, mip).rgb;
        vec2 brdf = texture(u_brdfLut, vec2(max(dot(N, V), 0.0), roughness)).rg;
        vec3 specular = prefilteredColor * (F * brdf.x + brdf.y);

        ambientColor = (kD * diffuse + specular) * ao;
    }
    else
    {
        vec3 envDiffuse  = u_envDiffuse  * baseColor * (1.0 - metallic);
        vec3 envSpecular = u_envSpecular * F0 * (1.0 - roughness);
        ambientColor = (envDiffuse + envSpecular) * ao;
    }

    vec3 color = ambientColor + Lo;

    // Output linear HDR - the post-processing chain (tonemap.frag) handles
    // ACES + gamma at the end of the frame so bloom/exposure can operate on
    // scene-referred values.
    FragColor = vec4(color, 1.0);
}
