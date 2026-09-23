#version 330 core

// FXAA 3.11 (Fast Approximate Anti-Aliasing), Timothy Lottes - trimmed
// implementation adequate for a single-shader post-process pass. Input is
// expected to be a tonemapped LDR image sampled with bilinear filtering.

in  vec2 vUV;
out vec4 FragColor;

uniform sampler2D u_ldr;
uniform vec2      u_texelSize;   // 1.0 / textureSize(u_ldr, 0)

const float EDGE_THRESHOLD_MIN = 0.0312;
const float EDGE_THRESHOLD_MAX = 0.125;
const float SUBPIXEL_QUALITY   = 0.75;

float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

void main()
{
    vec3 rgbM = texture(u_ldr, vUV).rgb;
    float lumaM = luma(rgbM);

    float lumaN = luma(texture(u_ldr, vUV + vec2( 0.0, -u_texelSize.y)).rgb);
    float lumaS = luma(texture(u_ldr, vUV + vec2( 0.0,  u_texelSize.y)).rgb);
    float lumaW = luma(texture(u_ldr, vUV + vec2(-u_texelSize.x, 0.0)).rgb);
    float lumaE = luma(texture(u_ldr, vUV + vec2( u_texelSize.x, 0.0)).rgb);

    float lumaMin = min(lumaM, min(min(lumaN, lumaS), min(lumaW, lumaE)));
    float lumaMax = max(lumaM, max(max(lumaN, lumaS), max(lumaW, lumaE)));
    float lumaRange = lumaMax - lumaMin;

    // Not an edge - skip.
    if (lumaRange < max(EDGE_THRESHOLD_MIN, lumaMax * EDGE_THRESHOLD_MAX))
    {
        FragColor = vec4(rgbM, 1.0);
        return;
    }

    // 3x3 luma neighborhood for sub-pixel jitter estimate.
    float lumaNW = luma(texture(u_ldr, vUV + vec2(-u_texelSize.x, -u_texelSize.y)).rgb);
    float lumaNE = luma(texture(u_ldr, vUV + vec2( u_texelSize.x, -u_texelSize.y)).rgb);
    float lumaSW = luma(texture(u_ldr, vUV + vec2(-u_texelSize.x,  u_texelSize.y)).rgb);
    float lumaSE = luma(texture(u_ldr, vUV + vec2( u_texelSize.x,  u_texelSize.y)).rgb);

    float edgeHoriz =
        abs(-2.0 * lumaW + lumaNW + lumaSW) +
        abs(-2.0 * lumaM + lumaN  + lumaS ) * 2.0 +
        abs(-2.0 * lumaE + lumaNE + lumaSE);
    float edgeVert =
        abs(-2.0 * lumaN + lumaNW + lumaNE) +
        abs(-2.0 * lumaM + lumaW  + lumaE ) * 2.0 +
        abs(-2.0 * lumaS + lumaSW + lumaSE);

    bool horiz = edgeHoriz >= edgeVert;

    // Sub-pixel offset toward the higher-contrast side.
    vec3 rgbBlur;
    if (horiz)
    {
        float lumaAvg = (lumaN + lumaS) * 0.5;
        float lumaSub = lumaN > lumaS ? lumaN : lumaS;
        float dir = (lumaN > lumaS) ? -1.0 : 1.0;
        rgbBlur = texture(u_ldr, vUV + vec2(0.0, dir * u_texelSize.y)).rgb;
    }
    else
    {
        float lumaAvg = (lumaW + lumaE) * 0.5;
        float dir = (lumaW > lumaE) ? -1.0 : 1.0;
        rgbBlur = texture(u_ldr, vUV + vec2(dir * u_texelSize.x, 0.0)).rgb;
    }

    // Sub-pixel blend factor (a smoothed contrast ratio).
    float subPixelBlend = smoothstep(0.0, 1.0, (lumaMax - lumaM) / lumaRange) * SUBPIXEL_QUALITY;
    FragColor = vec4(mix(rgbM, rgbBlur, subPixelBlend), 1.0);
}
