using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

using ArcEngine.Engine.Input;

namespace ArcEngine.Engine.Core;

public class Camera
{
    public Vector3 Position = new Vector3(0, 0, 3);

    private float _yaw = -90f;
    private float _pitch = 0f;

    private Vector3 _front = -Vector3.UnitZ;
    private Vector3 _up = Vector3.UnitY;
    private Vector3 _right = Vector3.UnitX;

    private const float MoveSpeed = 3f;
    private const float MouseSensitivity = 0.2f;

    public Matrix4 GetViewMatrix()
    {
        return Matrix4.LookAt(Position, Position + _front, _up);
    }

    public Matrix4 GetProjectionMatrix(float aspectRatio)
    {
        return Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(60f),
            aspectRatio,
            0.1f,
            100f
        );
    }

    private void UpdateVectors()
    {
        _front.X = MathF.Cos(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch));
        _front.Y = MathF.Sin(MathHelper.DegreesToRadians(_pitch));
        _front.Z = MathF.Sin(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch));

        _front = Vector3.Normalize(_front);
        _right = Vector3.Normalize(Vector3.Cross(_front, Vector3.UnitY));
        _up = Vector3.Normalize(Vector3.Cross(_right, _front));
    }

    /// <summary>Per-frame update. Reads movement + mouse-look state from <paramref name="input"/>.</summary>
    public void Update(InputManager input, float deltaTime)
    {
        // Movement
        float speed = MoveSpeed * deltaTime;
        if (input.IsKeyDown(Keys.W)) Position += _front * speed;
        if (input.IsKeyDown(Keys.S)) Position -= _front * speed;
        if (input.IsKeyDown(Keys.A)) Position -= _right * speed;
        if (input.IsKeyDown(Keys.D)) Position += _right * speed;

        // Mouse-look (delta is already zero on the first frame)
        var delta = input.MouseDelta;
        if (delta != Vector2.Zero)
        {
            _yaw += delta.X * MouseSensitivity;
            _pitch -= delta.Y * MouseSensitivity;
            _pitch = MathHelper.Clamp(_pitch, -89f, 89f);
            UpdateVectors();
        }
    }
}
