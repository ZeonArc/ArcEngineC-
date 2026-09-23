#version 330 core

// Tonemap pass - reads the HDR scene texture, applies exposure, ACES filmic
// tonemapping, and gamma correction, writes the LDR result to the default
// framebuffer. Replaces the in-lighting-shader Reinhard from Phase 4.

in  vec2 vUV;
out vec4 FragColor;

uniform sampler2D u_hdr;
uniform float     u_exposure;   // linear multiplier applied before tonemapping

// Optional bloom bleed - enabled once the bloom pass is wired up.
uniform sampler2D u_bloom;
uniform int       u_bloomEnabled;
uniform float     u_bloomStrength;

// Optional SSAO occlusion mask (single-channel [0..1]) multiplied into HDR.
uniform sampler2D u_ssao;
uniform int       u_ssaoEnabled;

// ACES filmic tonemapping (Narkowicz 2015 fit). Cheap, high-quality curve.
vec3 ACESFilm(vec3 x)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec3 hdr = texture(u_hdr, vUV).rgb;

    if (u_ssaoEnabled == 1)
        hdr *= texture(u_ssao, vUV).r;

    if (u_bloomEnabled == 1)
        hdr += texture(u_bloom, vUV).rgb * u_bloomStrength;

    vec3 mapped = ACESFilm(hdr * u_exposure);

    // Gamma correction (linear -> sRGB) for a straight-8-bit backbuffer.
    mapped = pow(mapped, vec3(1.0 / 2.2));

    FragColor = vec4(mapped, 1.0);
}
