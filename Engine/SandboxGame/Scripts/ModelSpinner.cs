using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.SandboxGame.Scripts;

/// <summary>
/// Rotates the owning GameObject's Transform every frame.
/// Replaces the hard-coded model-rotation logic that previously lived in SandboxScene.Update.
/// </summary>
public class ModelSpinner : Script
{
    /// <summary>Degrees per second around each axis. Default spins on Y at 30°/s.</summary>
    public Vector3 RotationDegPerSec = new Vector3(0f, 30f, 0f);

    public override void Update(float deltaTime)
    {
        Transform.Rotation += RotationDegPerSec * deltaTime;
    }
}
