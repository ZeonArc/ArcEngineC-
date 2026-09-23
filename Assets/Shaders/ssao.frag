#version 330 core

// Screen-space Ambient Occlusion (SSAO). Reconstructs view-space position from
// the depth texture, derives a view-space normal from screen-space derivatives
// (no G-buffer needed), then samples a rotated hemisphere kernel around each
// fragment. Output is a single-channel occlusion factor in [0..1] - 1 = fully lit,
// 0 = fully occluded.

in  vec2 vUV;
out vec4 FragColor;

uniform sampler2D u_depth;
uniform sampler2D u_noise;         // 4x4 tiled RGB rotation vectors (xy in tangent plane)
uniform mat4      u_projection;
uniform mat4      u_invProjection;
uniform vec2      u_noiseScale;    // screenSize / noiseSize (usually screen/4)
uniform vec3      u_samples[32];
uniform int       u_sampleCount;
uniform float     u_radius;
uniform float     u_bias;
uniform float     u_intensity;

// Reconstruct view-space position from window-space depth.
vec3 ReconstructViewPos(vec2 uv, float depth)
{
    // NDC -> clip -> view
    vec4 ndc = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 view = u_invProjection * ndc;
    return view.xyz / view.w;
}

void main()
{
    float depth = texture(u_depth, vUV).r;
    if (depth >= 1.0) { FragColor = vec4(1.0); return; }   // skybox / no geometry

    vec3 fragPos = ReconstructViewPos(vUV, depth);

    // Derive normal from screen-space derivatives of the reconstructed position.
    // Faceted but works for a first-cut SSAO without needing an extra G-buffer target.
    vec3 dpdx = dFdx(fragPos);
    vec3 dpdy = dFdy(fragPos);
    vec3 N = normalize(cross(dpdx, dpdy));

    // Random rotation vector from the noise tile (breaks up banding).
    vec3 rnd = normalize(texture(u_noise, vUV * u_noiseScale).xyz * 2.0 - 1.0);

    // Build a TBN in view space with rnd as the tangent direction.
    vec3 T = normalize(rnd - N * dot(rnd, N));
    vec3 B = cross(N, T);
    mat3 TBN = mat3(T, B, N);

    float occlusion = 0.0;
    for (int i = 0; i < u_sampleCount; ++i)
    {
        vec3 samplePos = TBN * u_samples[i];
        samplePos = fragPos + samplePos * u_radius;

        // Project to screen space so we can sample the depth texture at the same pixel.
        vec4 proj = u_projection * vec4(samplePos, 1.0);
        proj.xyz /= proj.w;
        vec2 sampleUV = proj.xy * 0.5 + 0.5;

        // Fade contribution near screen edges to avoid banding around the frame.
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 ||
            sampleUV.y < 0.0 || sampleUV.y > 1.0) continue;

        float sceneDepth = texture(u_depth, sampleUV).r;
        vec3 sceneView = ReconstructViewPos(sampleUV, sceneDepth);

        // Range check: don't count occluders that are far in front/behind (background).
        float rangeCheck = smoothstep(0.0, 1.0, u_radius / abs(fragPos.z - sceneView.z));

        // The sampled point is in front (view-space Z is greater, i.e. closer to camera
        // since view-Z is negative) -> sample is occluded.
        if (sceneView.z >= samplePos.z + u_bias)
            occlusion += rangeCheck;
    }

    occlusion = 1.0 - (occlusion / float(u_sampleCount)) * u_intensity;
    occlusion = clamp(occlusion, 0.0, 1.0);
    FragColor = vec4(vec3(occlusion), 1.0);
}
