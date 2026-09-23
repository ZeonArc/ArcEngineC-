#version 330 core

// Projected decal (screen-space) fragment shader.
//
// Reads the scene depth buffer to recover the world position underneath this
// decal-OBB fragment, transforms that position into the decal's local space,
// discards anything outside a unit cube, then samples the decal's texture
// using the local XZ plane as UV. Alpha-blended over the HDR scene color.
//
// Runs per decal, in a pass drawn AFTER the opaque scene has been written.

in  vec4 vClipPos;
out vec4 FragColor;

uniform sampler2D u_sceneDepth;
uniform sampler2D u_decalTexture;
uniform mat4      u_invViewProj;   // inverse(projection * view) at draw time
uniform mat4      u_invDecalWorld; // inverse of decal OBB world matrix
uniform vec4      u_tint;          // multiplied over the sampled decal color
uniform float     u_softEdge;      // fade width near the OBB faces, 0..0.5

void main()
{
    // Screen-space UV for this fragment.
    vec2 screenUV = (vClipPos.xy / vClipPos.w) * 0.5 + 0.5;
    float sceneDepth = texture(u_sceneDepth, screenUV).r;

    // Reconstruct world position of the surface underneath the decal at this pixel.
    vec4 ndc = vec4(screenUV * 2.0 - 1.0, sceneDepth * 2.0 - 1.0, 1.0);
    vec4 world = u_invViewProj * ndc;
    world /= world.w;

    // Transform into decal-local coords ([-0.5..0.5]^3 for a unit OBB with scale =
    // extents). Anything outside the box is discarded.
    vec4 local4 = u_invDecalWorld * vec4(world.xyz, 1.0);
    vec3 local = local4.xyz;
    if (any(greaterThan(abs(local), vec3(0.5))))
        discard;

    // Soft-edge fade toward the OBB faces.
    vec3 dist = 0.5 - abs(local);
    float edgeFade = clamp(min(min(dist.x, dist.y), dist.z) / max(u_softEdge, 1e-3), 0.0, 1.0);

    // Project onto local XZ so decals lie "on the floor" by default. Swap this
    // mapping in a variant shader for wall-projected decals.
    vec2 decalUV = local.xz + 0.5;
    vec4 texel = texture(u_decalTexture, decalUV) * u_tint;

    FragColor = vec4(texel.rgb, texel.a * edgeFade);
}
