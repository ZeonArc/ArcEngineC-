using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Lighting;

/// <summary>
/// Common base for all light components. Subclasses (<see cref="DirectionalLight"/>,
/// <see cref="PointLight"/>) add type-specific data; the renderer queries the scene for
/// each subtype each frame and uploads the corresponding shader uniforms.
/// </summary>
public abstract class Light : Component
{
    public Vector3 Color = Vector3.One;
    public float Intensity = 1f;

    /// <summary>Pre-multiplied color used by the shader.</summary>
    public Vector3 EffectiveColor => Color * Intensity;
}
