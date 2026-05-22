#version 330 core

#define MAX_POINT_LIGHTS 4

in vec3 normal;
in vec3 fragPos;
in vec2 uv;

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

vec3 CalcDirLight(DirLight L, vec3 norm, vec3 viewDir, vec3 diffSample, vec3 specSample)
{
    vec3 lightDir = normalize(-L.direction);
    return ApplyBlinnPhong(lightDir, L.color, norm, viewDir, diffSample, specSample);
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

    vec3 result = ambient * diffSample;

    result += CalcDirLight(dirLight, norm, viewDir, diffSample, specSample);

    int n = min(numPointLights, MAX_POINT_LIGHTS);
    for (int i = 0; i < n; ++i)
    {
        result += CalcPointLight(pointLights[i], norm, fragPos, viewDir, diffSample, specSample);
    }

    FragColor = vec4(result, 1.0);
}
