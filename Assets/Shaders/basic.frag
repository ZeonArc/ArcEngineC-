#version 330 core

#define MAX_POINT_LIGHTS 4

in vec3 normal;
in vec3 fragPos;
in vec2 uv;
in vec4 fragPosLightSpace;

out vec4 FragColor;

struct DirLight {
    vec3 direction;   // direction the light is travelling; we negate to get to-light
    vec3 color;       // already pre-multiplied by intensity on the CPU side
};

struct PointLight {
    vec3 position;
    vec3 color;       // already pre-multiplied by intensity
    float constant;
    float linear;
    float quadratic;
};

uniform DirLight   dirLight;
uniform int        numPointLights;
uniform PointLight pointLights[MAX_POINT_LIGHTS];

uniform vec3 viewPos;
uniform vec3 ambient;

uniform vec3 materialColor;
uniform sampler2D texture0;     // diffuse map (default white when absent)
uniform sampler2D specularMap;  // specular map (default white when absent)
uniform float shininess;

// Shadow uniforms (Sprint 5b).
uniform sampler2D shadowMap;
uniform int dirLightCastsShadows;   // 0 / 1 -- bool as int for driver portability

vec3 ApplyBlinnPhong(vec3 lightDir, vec3 lightColor, vec3 norm, vec3 viewDir,
                     vec3 diffSample, vec3 specSample)
{
    float diff = max(dot(norm, lightDir), 0.0);

    vec3 halfVec = normalize(lightDir + viewDir);
    float spec = pow(max(dot(norm, halfVec), 0.0), shininess);

    vec3 diffuse  = diff * diffSample;
    vec3 specular = spec * specSample;
    return (diffuse + specular) * lightColor;
}

// 3x3 PCF, with slope-scaled + constant minimum bias to combat shadow acne.
// Returns 0.0 (fully lit) ... 1.0 (fully shadowed).
float ShadowCalculation(vec4 posLS, vec3 norm, vec3 lightDir)
{
    // Perspective divide (no-op for the orthographic dir-light projection but standard form).
    vec3 projCoords = posLS.xyz / posLS.w;

    // Clip-space [-1, 1] to texture-space [0, 1].
    projCoords = projCoords * 0.5 + 0.5;

    // Outside the shadow frustum's far plane: not shadowed.
    if (projCoords.z > 1.0) return 0.0;

    float currentDepth = projCoords.z;

    // Slope-scaled bias: surfaces nearly parallel to the light get more bias.
    float bias = max(0.005 * (1.0 - dot(norm, lightDir)), 0.0005);

    // 3x3 PCF kernel.
    float shadow = 0.0;
    vec2 texelSize = 1.0 / vec2(textureSize(shadowMap, 0));
    for (int x = -1; x <= 1; ++x)
    {
        for (int y = -1; y <= 1; ++y)
        {
            float pcfDepth = texture(shadowMap, projCoords.xy + vec2(x, y) * texelSize).r;
            shadow += currentDepth - bias > pcfDepth ? 1.0 : 0.0;
        }
    }
    return shadow / 9.0;
}

vec3 CalcDirLight(DirLight L, vec3 norm, vec3 viewDir,
                  vec3 diffSample, vec3 specSample, vec4 posLS)
{
    vec3 lightDir = normalize(-L.direction);
    vec3 contribution = ApplyBlinnPhong(lightDir, L.color, norm, viewDir, diffSample, specSample);

    // Shadow factor only when the dir light's shadow map is bound + valid.
    if (dirLightCastsShadows == 1)
    {
        float shadow = ShadowCalculation(posLS, norm, lightDir);
        contribution *= (1.0 - shadow);
    }
    return contribution;
}

vec3 CalcPointLight(PointLight L, vec3 norm, vec3 worldPos, vec3 viewDir,
                    vec3 diffSample, vec3 specSample)
{
    vec3  toLight  = L.position - worldPos;
    float distance = length(toLight);
    vec3  lightDir = toLight / max(distance, 1e-5);

    float attenuation = 1.0 / (L.constant + L.linear * distance + L.quadratic * distance * distance);

    vec3 contribution = ApplyBlinnPhong(lightDir, L.color, norm, viewDir, diffSample, specSample);
    return contribution * attenuation;
}

void main()
{
    vec3 norm    = normalize(normal);
    vec3 viewDir = normalize(viewPos - fragPos);

    vec3 diffSample = texture(texture0,    uv).rgb * materialColor;
    vec3 specSample = texture(specularMap, uv).rgb;

    // Ambient is unaffected by shadows.
    vec3 result = ambient * diffSample;

    result += CalcDirLight(dirLight, norm, viewDir, diffSample, specSample, fragPosLightSpace);

    int n = min(numPointLights, MAX_POINT_LIGHTS);
    for (int i = 0; i < n; ++i)
    {
        result += CalcPointLight(pointLights[i], norm, fragPos, viewDir, diffSample, specSample);
    }

    FragColor = vec4(result, 1.0);
}
