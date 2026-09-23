#version 330 core

// Shared vertex shader for the IBL bake passes (equirect->cube, irradiance
// convolution, prefiltered specular). The unit cube's positions are handed
// to the fragment shader as the world-space sample direction so the FS can
// integrate over the hemisphere / sample the equirectangular map / etc.

layout (location = 0) in vec3 aPosition;

uniform mat4 u_view;
uniform mat4 u_projection;

out vec3 vLocalDir;

void main()
{
    vLocalDir = aPosition;
    gl_Position = u_projection * u_view * vec4(aPosition, 1.0);
}
