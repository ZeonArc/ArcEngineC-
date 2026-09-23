#version 330 core

// Terrain vertex shader. Identical to basic.vert except it also outputs the
// world-space XZ of the vertex, so terrain.frag can compute a slope factor
// and world-tiled UVs without depending on the mesh's UV channel.

layout (location = 0) in vec3 aPosition;
layout (location = 2) in vec3 aNormal;
layout (location = 3) in vec2 aUV;
layout (location = 4) in vec4 aTangent;
layout (location = 5) in mat4 aInstanceModel;

uniform mat4 view;
uniform mat4 projection;

out vec3  vWorldPos;
out vec3  vWorldNormal;
out vec3  vWorldTangent;
out float vTangentSign;
out vec2  vUV;
out float vViewZ;

void main()
{
    vec4 worldPos4 = aInstanceModel * vec4(aPosition, 1.0);
    vWorldPos = worldPos4.xyz;

    mat3 normalMat = transpose(inverse(mat3(aInstanceModel)));
    vWorldNormal  = normalize(normalMat * aNormal);
    vWorldTangent = normalize(normalMat * aTangent.xyz);
    vTangentSign  = aTangent.w;

    vUV = aUV;

    vec4 viewPos4 = view * worldPos4;
    vViewZ = -viewPos4.z;

    gl_Position = projection * viewPos4;
}
