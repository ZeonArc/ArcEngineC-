using OpenTK.Mathematics;

namespace ArcEngine.Engine.Core;

/// <summary>
/// Perspective camera component. Pure view/projection math + yaw/pitch state.
/// Position is sourced from the sibling <see cref="Transform"/>; movement and mouse-look
/// live in a controller component (see <c>FpsCameraController</c>).
/// </summary>
public class Camera : Component
{
    public float Yaw = -90f;
    public float Pitch = 0f;

    public float Fov = 60f;
    public float NearClip = 0.1f;
    public float FarClip = 100f;

    /// <summary>Forward (look) direction in world space, derived from <see cref="Yaw"/> / <see cref="Pitch"/>.</summary>
    public Vector3 Front
    {
        get
        {
            float yawRad = MathHelper.DegreesToRadians(Yaw);
            float pitchRad = MathHelper.DegreesToRadians(Pitch);
            return Vector3.Normalize(new Vector3(
                MathF.Cos(yawRad) * MathF.Cos(pitchRad),
                MathF.Sin(pitchRad),
                MathF.Sin(yawRad) * MathF.Cos(pitchRad)));
        }
    }

    /// <summary>Right vector in world space (Front × world-up, normalized).</summary>
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));

    /// <summary>Up vector in world space (Right × Front, normalized).</summary>
    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Front));

    public Matrix4 GetViewMatrix()
    {
        var pos = Transform.Position;
        return Matrix4.LookAt(pos, pos + Front, Vector3.UnitY);
    }

    public Matrix4 GetProjectionMatrix(float aspectRatio)
    {
        return Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(Fov),
            aspectRatio,
            NearClip,
            FarClip);
    }
}
