#version 330 core

// Convolve an environment cubemap into an irradiance cubemap: for each output
// texel, integrate the environment's radiance over the hemisphere around the
// texel's normal with a cosine weight. The result is the diffuse ambient
// (Lambertian) contribution used by the main PBR shader.
//
// Sample count: (2? / sampleDelta) * (0.5? / sampleDelta) ? 400 samples at 0.025.
// Cheap enough to run once at load time on a 32x32 cubemap.

in  vec3 vLocalDir;
out vec4 FragColor;

uniform samplerCube u_env;

const float PI = 3.14159265359;

void main()
{
    vec3 N = normalize(vLocalDir);

    // Right/up basis in the tangent plane (arbitrary choice - result is
    // rotation-invariant because we integrate a full hemisphere).
    vec3 up = abs(N.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0);
    vec3 right = normalize(cross(up, N));
    up = cross(N, right);

    vec3 irradiance = vec3(0.0);
    float sampleCount = 0.0;
    const float sampleDelta = 0.025;

    for (float phi = 0.0; phi < 2.0 * PI; phi += sampleDelta)
    for (float theta = 0.0; theta < 0.5 * PI; theta += sampleDelta)
    {
        // Spherical -> cartesian in tangent space.
        vec3 tangentSample = vec3(
            sin(theta) * cos(phi),
            sin(theta) * sin(phi),
            cos(theta));

        // Tangent -> world.
        vec3 sampleDir = tangentSample.x * right + tangentSample.y * up + tangentSample.z * N;

        // Cosine-weight (multiplied by sin(theta) Jacobian for the sphere).
        irradiance += texture(u_env, sampleDir).rgb * cos(theta) * sin(theta);
        sampleCount += 1.0;
    }

    irradiance = PI * irradiance / sampleCount;
    FragColor = vec4(irradiance, 1.0);
}
