#version 330 core

// Sample the environment cubemap along each fragment's world-space direction.
// Output is HDR (linear) - the tonemap pass in the post-processing chain
// applies exposure + ACES.

in  vec3 vLocalDir;
out vec4 FragColor;

uniform samplerCube u_env;
uniform float       u_intensity;

void main()
{
    vec3 color = texture(u_env, normalize(vLocalDir)).rgb * u_intensity;
    FragColor = vec4(color, 1.0);
}
