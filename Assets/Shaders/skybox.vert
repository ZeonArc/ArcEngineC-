#version 330 core

// Skybox vertex shader. Draws a unit cube from inside; the view matrix has its
// translation stripped so the cube stays centered on the camera. Depth is
// forced to 1.0 in the fragment shader (via depth-equal test) so the skybox
// only appears where nothing else has drawn.

layout (location = 0) in vec3 aPosition;

uniform mat4 u_view;         // rotation-only (no translation)
uniform mat4 u_projection;

out vec3 vLocalDir;

void main()
{
    vLocalDir = aPosition;
    vec4 clip = u_projection * u_view * vec4(aPosition, 1.0);
    // Force z = w so post-perspective depth = 1.0 (the far plane).
    gl_Position = clip.xyww;
}
