using OpenTK.Mathematics;

namespace ArcEngine.Engine.Lighting;

/// <summary>
/// All lighting state for a frame: a global ambient term, an optional directional "sun",
/// and zero-or-more point lights. The renderer reads this once per frame and uploads
/// matching uniforms to the active shader.
///
/// Maximum point-light count must match <c>MAX_POINT_LIGHTS</c> in <c>basic.frag</c>.
/// </summary>
public class LightSet
{
    /// <summary>Must match MAX_POINT_LIGHTS in basic.frag.</summary>
    public const int MaxPointLights = 4;

    public Vector3 Ambient = new Vector3(0.05f);

    public DirectionalLight? Sun;

    public List<PointLight> Points { get; } = new();
}
