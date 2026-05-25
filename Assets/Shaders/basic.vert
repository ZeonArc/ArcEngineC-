#version 330 core

layout (location = 0) in vec3 aPosition;
layout (location = 2) in vec3 aNormal;
layout (location = 3) in vec2 aUV;

out vec3 normal;
out vec3 fragPos;
out vec2 uv;
out vec4 fragPosLightSpace;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform mat4 lightSpaceMatrix;

void main()
{
    fragPos = vec3(model * vec4(aPosition, 1.0));
    normal = mat3(transpose(inverse(model))) * aNormal;
    uv = aUV;

    // Project the world-space fragment position into the directional light's clip space.
    // The fragment shader does the perspective divide + [0,1] remap and uses this to
    // sample the shadow map.
    fragPosLightSpace = lightSpaceMatrix * vec4(fragPos, 1.0);

    gl_Position = projection * view * vec4(fragPos, 1.0);
}
