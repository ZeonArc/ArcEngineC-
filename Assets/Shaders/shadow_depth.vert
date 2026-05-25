#version 330 core

// Depth-only vertex shader used by the shadow pass.
// Position attribute only; normals/UVs are irrelevant to depth.
layout (location = 0) in vec3 aPosition;

uniform mat4 lightSpaceMatrix;
uniform mat4 model;

void main()
{
    gl_Position = lightSpaceMatrix * model * vec4(aPosition, 1.0);
}
