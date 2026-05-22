using OpenTK.Mathematics;

namespace ArcEngine.Engine.Lighting;

/// <summary>
/// Infinitely distant light (e.g. the sun). Has a direction and color but no position.
/// <see cref="Direction"/> is the direction the light is travelling; the shader negates it
/// internally to get the "to-light" vector used for diffuse/specular calculations.
/// </summary>
public class DirectionalLight
{
    /// <summary>The direction the light is travelling. Convention: pointing away from the sun.</summary>
    public Vector3 Direction = -Vector3.UnitY;

    public Vector3 Color = Vector3.One;
    public float Intensity = 1f;

    /// <summary>Pre-multiplied color used by the shader.</summary>
    public Vector3 EffectiveColor => Color * Intensity;
}
