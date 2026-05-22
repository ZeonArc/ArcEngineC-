using OpenTK.Mathematics;

namespace ArcEngine.Engine.Lighting;

/// <summary>
/// Omnidirectional point light with standard quadratic falloff.
/// Attenuation = 1 / (Constant + Linear * d + Quadratic * d²).
///
/// Default values give a usable range of roughly 50 units (ogre3d/learnopengl table).
/// </summary>
public class PointLight
{
    public Vector3 Position;
    public Vector3 Color = Vector3.One;
    public float Intensity = 1f;

    public float Constant = 1f;
    public float Linear = 0.09f;
    public float Quadratic = 0.032f;

    /// <summary>Pre-multiplied color used by the shader.</summary>
    public Vector3 EffectiveColor => Color * Intensity;
}
