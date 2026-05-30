#version 330 core

// Sprint 12 particles fragment shader.
// Samples the procedural soft-circle texture (R channel = falloff 0..1), multiplies
// by the per-particle color. With additive blending in the renderer, black background
// = invisible.

in vec2 vUV;
in vec4 vColor;

uniform sampler2D u_particleTex;

out vec4 FragColor;

void main()
{
    float falloff = texture(u_particleTex, vUV).r;
    vec3 rgb = vColor.rgb * falloff * vColor.a;
    FragColor = vec4(rgb, falloff * vColor.a);
}
