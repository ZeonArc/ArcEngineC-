#version 330 core

// Skinned vertex shader - companion to basic.frag. Reads 4 bone indices + 4
// weights per vertex and blends the matching palette matrices to produce the
// skinned world position + world-space normal + tangent.

#define MAX_BONES 128

layout (location = 0)  in vec3 aPosition;
layout (location = 2)  in vec3 aNormal;
layout (location = 3)  in vec2 aUV;
layout (location = 4)  in vec4 aTangent;
layout (location = 9)  in vec4 aBoneIndices;
layout (location = 10) in vec4 aBoneWeights;

uniform mat4 u_model;         // world-space transform of the whole character
uniform mat4 view;
uniform mat4 projection;
uniform mat4 u_bonePalette[MAX_BONES];

out vec3  vWorldPos;
out vec3  vWorldNormal;
out vec3  vWorldTangent;
out float vTangentSign;
out vec2  vUV;
out float vViewZ;

void main()
{
    // Weighted-blend the four bone matrices. Skip bones with zero weight so
    // uninitialized indices don't corrupt the sum.
    mat4 skin = mat4(0.0);
    if (aBoneWeights.x > 0.0) skin += u_bonePalette[int(aBoneIndices.x)] * aBoneWeights.x;
    if (aBoneWeights.y > 0.0) skin += u_bonePalette[int(aBoneIndices.y)] * aBoneWeights.y;
    if (aBoneWeights.z > 0.0) skin += u_bonePalette[int(aBoneIndices.z)] * aBoneWeights.z;
    if (aBoneWeights.w > 0.0) skin += u_bonePalette[int(aBoneIndices.w)] * aBoneWeights.w;

    // If a vertex is somehow un-weighted (all zeroes) fall back to identity so
    // it doesn't collapse to the origin.
    float totalWeight = aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w;
    if (totalWeight <= 0.0) skin = mat4(1.0);

    // Apply skin in mesh-space, then push through the object's world matrix.
    vec4 skinned = skin * vec4(aPosition, 1.0);
    vec4 worldPos4 = u_model * skinned;
    vWorldPos = worldPos4.xyz;

    mat3 normalMat = transpose(inverse(mat3(u_model * skin)));
    vWorldNormal  = normalize(normalMat * aNormal);
    vWorldTangent = normalize(normalMat * aTangent.xyz);
    vTangentSign  = aTangent.w;

    vUV = aUV;

    vec4 viewPos4 = view * worldPos4;
    vViewZ = -viewPos4.z;

    gl_Position = projection * viewPos4;
}
