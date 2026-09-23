#version 330 core

// Sprint 11 PBR vertex shader -- always-instanced.
// The model matrix comes through a per-instance attribute (mat4 at locations 5-8)
// rather than a uniform, so single-object draws (1 instance) and batched draws
// (N instances of identical Mesh+Material) share one code path.

layout (location = 0) in vec3 aPosition;
layout (location = 2) in vec3 aNormal;
layout (location = 3) in vec2 aUV;
layout (location = 4) in vec4 aTangent;   // xyz = tangent, w = bitangent sign (?1)
layout (location = 5) in mat4 aInstanceModel;   // occupies 5,6,7,8

uniform mat4 view;
uniform mat4 projection;

out vec3  vWorldPos;
out vec3  vWorldNormal;
out vec3  vWorldTangent;
out float vTangentSign;
out vec2  vUV;
out float vViewZ;   // positive-in-front-of-camera depth, used to pick shadow cascade

void main()
{
    vec4 worldPos4 = aInstanceModel * vec4(aPosition, 1.0);
    vWorldPos = worldPos4.xyz;

    // Full inverse-transpose so non-uniform scale (e.g. a stretched crate) still
    // produces perpendicular world-space normals. Costs a per-vertex 3x3 inverse
    // but keeps lighting correct without a separate CPU-computed normal matrix.
    mat3 normalMat = transpose(inverse(mat3(aInstanceModel)));
    vWorldNormal  = normalize(normalMat * aNormal);
    vWorldTangent = normalize(normalMat * aTangent.xyz);
    vTangentSign  = aTangent.w;

    vUV = aUV;

    // View-space Z is negative in front of the camera in OpenGL; flip to positive.
    vec4 viewPos4 = view * worldPos4;
    vViewZ = -viewPos4.z;

    gl_Position = projection * viewPos4;
}
