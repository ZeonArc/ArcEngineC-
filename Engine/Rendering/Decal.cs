using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// A projected decal — bullet hole, blood splat, painted logo. The GameObject's
/// <see cref="Transform"/> defines the oriented bounding box the decal is
/// projected into: position = center, rotation orients the box, scale is the
/// full extents on each axis.
///
/// Renderer draws each decal as a unit cube after the opaque scene; the fragment
/// shader reads the depth buffer to know which surface pixel it hits, discards
/// anything outside the OBB, and blends the decal texture over the underlying color.
/// </summary>
public class Decal : Component
{
    /// <summary>Decal image (typically with an alpha channel for the shape mask).</summary>
    public Texture? Texture;

    /// <summary>Multiplied over the sampled decal color. Alpha scales opacity.</summary>
    public Vector4 Tint = Vector4.One;

    /// <summary>Width of the fade near the OBB faces (in local units, 0..0.5).</summary>
    public float SoftEdge = 0.05f;
}
