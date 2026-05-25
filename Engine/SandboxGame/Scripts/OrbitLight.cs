using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.SandboxGame.Scripts;

/// <summary>
/// Moves the owning Transform along a horizontal circle around the world origin.
/// Used to make the demo's point lights orbit the scene visibly.
/// </summary>
public class OrbitLight : Script
{
    public float Radius = 2.5f;
    public float Speed = 1.0f;          // radians per second
    public float Height = 0.6f;
    public float PhaseOffset = 0f;      // radians

    private float _t;

    public override void Update(float deltaTime)
    {
        _t += deltaTime;
        float a = _t * Speed + PhaseOffset;
        Transform.Position = new Vector3(
            MathF.Cos(a) * Radius,
            Height,
            MathF.Sin(a) * Radius);
    }
}
