#version 330 core

// Separable Gaussian blur (9-tap). Direction is chosen by the caller via a
// uniform axis vector - same shader for horizontal (vec2(1,0)) and vertical
// (vec2(0,1)) passes.

in  vec2 vUV;
out vec4 FragColor;

uniform sampler2D u_src;
uniform vec2      u_texelSize;   // 1.0 / textureSize
uniform vec2      u_axis;        // (1,0) or (0,1) - direction to blur along

// Weights for a sigma?2 Gaussian, normalized.
const float w0 = 0.227027;
const float w1 = 0.194594;
const float w2 = 0.121621;
const float w3 = 0.054054;
const float w4 = 0.016216;

void main()
{
    vec2 step = u_axis * u_texelSize;

    vec3 sum = texture(u_src, vUV).rgb * w0;
    sum += texture(u_src, vUV + step * 1.0).rgb * w1;
    sum += texture(u_src, vUV - step * 1.0).rgb * w1;
    sum += texture(u_src, vUV + step * 2.0).rgb * w2;
    sum += texture(u_src, vUV - step * 2.0).rgb * w2;
    sum += texture(u_src, vUV + step * 3.0).rgb * w3;
    sum += texture(u_src, vUV - step * 3.0).rgb * w3;
    sum += texture(u_src, vUV + step * 4.0).rgb * w4;
    sum += texture(u_src, vUV - step * 4.0).rgb * w4;

    FragColor = vec4(sum, 1.0);
}
