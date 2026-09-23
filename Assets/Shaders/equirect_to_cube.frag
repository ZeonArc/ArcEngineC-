#version 330 core

// Sample an equirectangular HDR image using each fragment's world-space
// direction. Used at bake time to convert a 2:1 lat-long HDRI into a cubemap.

in  vec3 vLocalDir;
out vec4 FragColor;

uniform sampler2D u_equirect;

const vec2 INV_ATAN = vec2(0.1591, 0.3183);   // 1/(2?), 1/?

vec2 SampleSphericalMap(vec3 v)
{
    // Standard atan2/asin mapping. y=up.
    vec2 uv = vec2(atan(v.z, v.x), asin(v.y));
    uv *= INV_ATAN;
    uv += 0.5;
    return uv;
}

void main()
{
    vec3 dir = normalize(vLocalDir);
    vec2 uv = SampleSphericalMap(dir);
    vec3 color = texture(u_equirect, uv).rgb;
    FragColor = vec4(color, 1.0);
}
