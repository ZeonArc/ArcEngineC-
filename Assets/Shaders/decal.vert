#version 330 core

// Decal vertex shader. Draws a unit cube (the decal's OBB), passing the
// clip-space position through to the fragment shader together with the
// decal-space bounds so the FS can reconstruct which surface pixel it hits.

layout (location = 0) in vec3 aPosition;

uniform mat4 u_model;         // decal OBB in world space (scale = full extents)
uniform mat4 u_view;
uniform mat4 u_projection;

out vec4 vClipPos;

void main()
{
    vClipPos = u_projection * u_view * u_model * vec4(aPosition, 1.0);
    gl_Position = vClipPos;
}
