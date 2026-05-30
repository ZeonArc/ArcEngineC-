#version 330 core

// Sprint 11 PBR vertex shader -- always-instanced.
// The model matrix comes through a per-instance attribute (mat4 at locations 5-8)
// rather than a uniform, so single-object draws (1 instance) and batched draws
// (N instances of identical Mesh+Material) share one code path.

layout (location = 0) in vec3 aPosition;
layout (location = 2) in vec3 aNormal;
layout (location = 3) in vec2 aUV;
layout (location = 4) in vec3 aTangent;
layout (location = 5) in mat4 aInstanceModel;   // occupies 5,6,7,8

uniform mat4 view;
uniform mat4 projection;
uniform mat4 lightSpaceMatrix;

out vec3 vWorldPos;
out vec3 vWorldNormal;
out vec3 vWorldTangent;
out vec2 vUV;
out vec4 vFragPosLightSpace;

void main()
{
    vec4 worldPos4 = aInstanceModel * vec4(aPosition, 1.0);
    vWorldPos = worldPos4.xyz;

    // For uniform-scale matrices the inverse-transpose collapses to the upper 3x3
    // (the scale factor applies uniformly and is normalised away below). The shader
    // pipeline normalises vWorldNormal, so this works for any uniform scale. If you
    // start using non-uniform scale, swap this for `mat3(transpose(inverse(aInstanceModel)))`.
    mat3 normalMat = mat3(aInstanceModel);
    vWorldNormal  = normalize(normalMat * aNormal);
    vWorldTangent = normalMat * aTangent;

    vUV = aUV;
    vFragPosLightSpace = lightSpaceMatrix * worldPos4;

    gl_Position = projection * view * worldPos4;
}
