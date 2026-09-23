#version 330 core

// Box-blur (4x4) of the raw SSAO buffer. Cheap way to hide sampling noise
// introduced by the small kernel + random rotation in ssao.frag.

in  vec2 vUV;
out vec4 FragColor;

uniform sampler2D u_src;
uniform vec2      u_texelSize;

void main()
{
    float sum = 0.0;
    for (int x = -2; x < 2; ++x)
    for (int y = -2; y < 2; ++y)
    {
        vec2 offset = vec2(float(x), float(y)) * u_texelSize;
        sum += texture(u_src, vUV + offset).r;
    }
    FragColor = vec4(vec3(sum / 16.0), 1.0);
}
