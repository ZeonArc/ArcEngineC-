#version 330 core

// Depth-pass fragment shader for point-light shadow cubemap. Writes linear
// distance-to-light (normalized by the light's far plane) into R16F. The
// main shader compares this against its own light-to-fragment distance.

in vec3 vWorldPos;
out vec4 FragColor;

uniform vec3  u_lightPos;
uniform float u_farPlane;

void main()
{
    float dist = length(vWorldPos - u_lightPos) / u_farPlane;
    FragColor = vec4(dist, 0.0, 0.0, 1.0);
}
