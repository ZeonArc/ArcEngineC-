#version 330 core

// Terrain PBR fragment shader - same lighting model as basic.frag, but the
// base color comes from a 4-layer texture splat driven by both a per-vertex
// splat map (RGBA weights) and an automatic slope factor.
//
// Layer roles (default authoring convention):
//   0 = ground / grass base
//   1 = dirt / trail
//   2 = rock (auto-blended by slope)
//   3 = snow / accent (auto-blended by height)

#define MAX_POINT_LIGHTS 4
#define SHADOW_CASCADES  3
const float PI = 3.14159265359;

in vec3  vWorldPos;
in vec3  vWorldNormal;
in vec3  vWorldTangent;
in float vTangentSign;
in vec2  vUV;
in float vViewZ;

out vec4 FragColor;

// ---- Lights (same layout as basic.frag) ----------------------------------
struct DirLight   { vec3 direction; vec3 color; };
struct PointLight { vec3 position;  vec3 color;
                    float constant; float linear; float quadratic; };

uniform DirLight   dirLight;
uniform int        numPointLights;
uniform PointLight pointLights[MAX_POINT_LIGHTS];
uniform vec3       viewPos;

uniform vec3 u_envDiffuse;
uniform vec3 u_envSpecular;

uniform samplerCube u_irradianceMap;
uniform samplerCube u_prefilteredMap;
uniform sampler2D   u_brdfLut;
uniform int         u_iblEnabled;
uniform float       u_prefilteredMipCount;

// ---- Base PBR knobs ------------------------------------------------------
uniform float u_metallic;
uniform float u_roughness;
uniform float u_ao;
uniform float u_normalStrength;

// ---- Terrain-specific ----------------------------------------------------
uniform sampler2D u_layer0;
uniform sampler2D u_layer1;
uniform sampler2D u_layer2;
uniform sampler2D u_layer3;
uniform sampler2D u_splatMap;
uniform int       u_hasSplatMap;
uniform float     u_uvTiling;      // world-space tiling factor per layer
uniform float     u_slopeBias;     // where slope-blend starts biting (0..1 cosine)
uniform float     u_snowHeight;    // world Y at which layer 3 (snow) starts

// ---- Shadow (cascaded) ---------------------------------------------------
uniform sampler2DArray shadowMapArray;
uniform mat4           lightSpaceMatrices[SHADOW_CASCADES];
uniform float          cascadeSplits[SHADOW_CASCADES];
uniform int            dirLightCastsShadows;

// ============================================================================
// Shared BRDF helpers (copied from basic.frag - no include mechanism yet).
// ============================================================================

float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a  = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d);
}
float GeometrySchlickGGX(float NdotV, float r)
{
    float k = ((r + 1.0) * (r + 1.0)) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}
float GeometrySmith(vec3 N, vec3 V, vec3 L, float r)
{
    return GeometrySchlickGGX(max(dot(N, V), 0.0), r) *
           GeometrySchlickGGX(max(dot(N, L), 0.0), r);
}
vec3 FresnelSchlick(float c, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - c, 0.0, 1.0), 5.0);
}

int SelectCascade()
{
    for (int i = 0; i < SHADOW_CASCADES; ++i)
        if (vViewZ < cascadeSplits[i]) return i;
    return SHADOW_CASCADES - 1;
}
float ShadowCalculation(int cascade, vec3 N, vec3 L)
{
    vec4 posLS = lightSpaceMatrices[cascade] * vec4(vWorldPos, 1.0);
    vec3 pc = posLS.xyz / posLS.w;
    pc = pc * 0.5 + 0.5;
    if (pc.z > 1.0) return 0.0;
    float cs = float(cascade + 1);
    float bias = max(0.001 * cs * (1.0 - dot(N, L)), 0.0002 * cs);
    float shadow = 0.0;
    vec2 ts = 1.0 / vec2(textureSize(shadowMapArray, 0).xy);
    for (int x = -1; x <= 1; ++x)
    for (int y = -1; y <= 1; ++y)
    {
        float d = texture(shadowMapArray, vec3(pc.xy + vec2(x, y) * ts, float(cascade))).r;
        shadow += (pc.z - bias) > d ? 1.0 : 0.0;
    }
    return shadow / 9.0;
}
vec3 RadianceFromLight(vec3 L, vec3 lc, vec3 N, vec3 V, vec3 base, float met, float r, vec3 F0)
{
    float NdotL = max(dot(N, L), 0.0);
    if (NdotL <= 0.0) return vec3(0.0);
    vec3 H = normalize(V + L);
    float D = DistributionGGX(N, H, r);
    float G = GeometrySmith(N, V, L, r);
    vec3  F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    vec3 spec = (D * G * F) / (4.0 * max(dot(N, V), 0.0) * NdotL + 1e-4);
    vec3 kS = F;
    vec3 kD = (1.0 - kS) * (1.0 - met);
    return (kD * base / PI + spec) * lc * NdotL;
}

// ============================================================================
// Splat blend
// ============================================================================

vec3 SampleTerrainBaseColor(vec3 N)
{
    // World-tiled UVs so texture density is uniform regardless of terrain size.
    vec2 tuv = vWorldPos.xz * u_uvTiling;

    vec3 c0 = texture(u_layer0, tuv).rgb;
    vec3 c1 = texture(u_layer1, tuv).rgb;
    vec3 c2 = texture(u_layer2, tuv).rgb;
    vec3 c3 = texture(u_layer3, tuv).rgb;

    // Painted weights (RGBA splat), or uniform 1/0/0/0 if no splat map is bound.
    vec4 w = u_hasSplatMap == 1
        ? texture(u_splatMap, vUV)
        : vec4(1.0, 0.0, 0.0, 0.0);

    // Slope factor: 1 on flat ground, 0 on vertical cliff. cos(?) between +Y and N.
    float slopeCos = clamp(N.y, 0.0, 1.0);
    float rockWeight = smoothstep(u_slopeBias, u_slopeBias - 0.2, slopeCos);
    w.b += rockWeight;

    // Height factor: layer 3 rises above u_snowHeight.
    float snowWeight = smoothstep(u_snowHeight - 2.0, u_snowHeight + 2.0, vWorldPos.y);
    w.a += snowWeight;

    // Normalize so contributions sum to 1 regardless of authoring.
    float sum = max(w.r + w.g + w.b + w.a, 1e-4);
    w /= sum;

    return c0 * w.r + c1 * w.g + c2 * w.b + c3 * w.a;
}

// ============================================================================
// Main
// ============================================================================

void main()
{
    vec3 N = normalize(vWorldNormal);
    vec3 V = normalize(viewPos - vWorldPos);

    vec3 baseColor = SampleTerrainBaseColor(N);
    float metallic  = u_metallic;
    float roughness = clamp(u_roughness, 0.04, 1.0);
    float ao = u_ao;

    vec3 F0 = mix(vec3(0.04), baseColor, metallic);

    // Directional light contribution
    vec3 Lo = vec3(0.0);
    {
        vec3 L = normalize(-dirLight.direction);
        vec3 c = RadianceFromLight(L, dirLight.color, N, V, baseColor, metallic, roughness, F0);
        if (dirLightCastsShadows == 1)
        {
            int cascade = SelectCascade();
            float shadow = ShadowCalculation(cascade, N, L);
            c *= (1.0 - shadow);
        }
        Lo += c;
    }

    int npl = min(numPointLights, MAX_POINT_LIGHTS);
    for (int i = 0; i < npl; ++i)
    {
        vec3 toL = pointLights[i].position - vWorldPos;
        float d = length(toL);
        vec3 L = toL / max(d, 1e-5);
        float att = 1.0 / (pointLights[i].constant + pointLights[i].linear * d + pointLights[i].quadratic * d * d);
        Lo += RadianceFromLight(L, pointLights[i].color, N, V, baseColor, metallic, roughness, F0) * att;
    }

    // Ambient - same IBL / fallback split as basic.frag.
    vec3 ambientColor;
    if (u_iblEnabled == 1)
    {
        vec3 F = F0 + (max(vec3(1.0 - roughness), F0) - F0) *
                     pow(clamp(1.0 - max(dot(N, V), 0.0), 0.0, 1.0), 5.0);
        vec3 kS = F;
        vec3 kD = (1.0 - kS) * (1.0 - metallic);
        vec3 diffuse = texture(u_irradianceMap, N).rgb * baseColor;
        vec3 R = reflect(-V, N);
        float mip = roughness * (u_prefilteredMipCount - 1.0);
        vec3 pref = textureLod(u_prefilteredMap, R, mip).rgb;
        vec2 brdf = texture(u_brdfLut, vec2(max(dot(N, V), 0.0), roughness)).rg;
        vec3 spec = pref * (F * brdf.x + brdf.y);
        ambientColor = (kD * diffuse + spec) * ao;
    }
    else
    {
        vec3 envD = u_envDiffuse  * baseColor * (1.0 - metallic);
        vec3 envS = u_envSpecular * F0 * (1.0 - roughness);
        ambientColor = (envD + envS) * ao;
    }

    FragColor = vec4(ambientColor + Lo, 1.0);
}
