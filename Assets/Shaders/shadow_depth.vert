#version 330 core

// Sprint 11 depth-only shadow vertex shader, always-instanced.
// Position attribute only; the per-instance model matrix lives at locations 5-8
// (mat4). Normal/UV/tangent are present in the VAO but unused here.

layout (location = 0) in vec3 aPosition;
layout (location = 5) in mat4 aInstanceModel;

uniform mat4 lightSpaceMatrix;

void main()
{
    gl_Position = lightSpaceMatrix * aInstanceModel * vec4(aPosition, 1.0);
}
