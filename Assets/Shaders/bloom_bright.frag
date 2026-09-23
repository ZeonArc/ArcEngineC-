#version 330 core

// Bright-pass: writes only the portion of the HDR image above a luminance
// threshold, with soft knee falloff for values just below the cut so the
// blur input doesn't have hard edges around bright objects.

in  vec2 vUV;
out vec4 FragColor;

uniform sampler2D u_hdr;
uniform float     u_threshold;   // luma cutoff (linear space; ~1.0 for tonemapped bright)
uniform float     u_softKnee;    // 0..1, width of the smooth ramp below the cutoff

void main()
{
    vec3 c = texture(u_hdr, vUV).rgb;

    // Rec709 luma weights.
    float luma = dot(c, vec3(0.2126, 0.7152, 0.0722));

    float knee = max(u_threshold * u_softKnee, 1e-5);
    float soft = clamp((luma - u_threshold + knee) / (2.0 * knee), 0.0, 1.0);
    soft = soft * soft * (3.0 - 2.0 * soft);   // smoothstep
    float weight = max(luma - u_threshold, 0.0) + soft;
    weight = weight / max(luma, 1e-5);

    FragColor = vec4(c * weight, 1.0);
}
