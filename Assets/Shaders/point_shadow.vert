#version 330 core

// Depth-pass vertex shader for a single point-light cubemap face. The scene
// is drawn six times per shadow-casting point light, once per face, with
// lightViewProj configured for the current face.

layout (location = 0) in vec3 aPosition;
layout (location = 5) in mat4 aInstanceModel;

uniform mat4 u_lightViewProj;

out vec3 vWorldPos;

void main()
{
    vec4 wp = aInstanceModel * vec4(aPosition, 1.0);
    vWorldPos = wp.xyz;
    gl_Position = u_lightViewProj * wp;
}
