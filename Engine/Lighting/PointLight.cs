namespace ArcEngine.Engine.Lighting;

/// <summary>
/// Omnidirectional point light with standard quadratic falloff.
/// Attenuation = 1 / (Constant + Linear * d + Quadratic * d²).
/// Position is sourced from the sibling <see cref="Engine.Core.Transform"/>.
/// </summary>
public class PointLight : Light
{
    public float Constant = 1f;
    public float Linear = 0.09f;
    public float Quadratic = 0.032f;
}
