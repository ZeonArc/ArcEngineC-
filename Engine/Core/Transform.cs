using OpenTK.Mathematics;

namespace ArcEngine.Engine.Core;

public class Transform
{
    public Vector3 Position = Vector3.Zero;
    public Vector3 Rotation = Vector3.Zero;
    public Vector3 Scale = Vector3.One;

    /// <summary>The GameObject that owns this transform. Set by <see cref="GameObject"/>'s ctor.</summary>
    public GameObject? Owner;

    /// <summary>Parent transform in the scene hierarchy. Null means root-level.</summary>
    public Transform? Parent { get; private set; }

    /// <summary>Direct children. Use <see cref="SetParent"/> to mutate the hierarchy.</summary>
    public List<Transform> Children { get; } = new();

    /// <summary>Reparent this transform. Pass null to detach.</summary>
    public void SetParent(Transform? newParent)
    {
        if (Parent == newParent) return;

        Parent?.Children.Remove(this);
        Parent = newParent;
        Parent?.Children.Add(this);
    }

    /// <summary>Local model matrix (this transform's own translation/rotation/scale, ignoring parent).</summary>
    public Matrix4 GetLocalModelMatrix()
    {
        Matrix4 translation = Matrix4.CreateTranslation(Position);
        Matrix4 rotationX = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(Rotation.X));
        Matrix4 rotationY = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(Rotation.Y));
        Matrix4 rotationZ = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(Rotation.Z));
        Matrix4 scale = Matrix4.CreateScale(Scale);

        // Row-vector convention: v * (S * Rz * Ry * Rx * T) → scale, then rotate, then translate.
        return scale * rotationZ * rotationY * rotationX * translation;
    }

    /// <summary>Backward-compat alias kept for callers that haven't moved to world-aware rendering yet.</summary>
    public Matrix4 GetModelMatrix() => GetLocalModelMatrix();

    /// <summary>
    /// World model matrix, walking up the parent chain.
    /// Row-vector convention: <c>local * parentWorld</c> so a point first goes through the
    /// local transform, then the parent's world transform.
    /// </summary>
    public Matrix4 GetWorldModelMatrix()
    {
        var local = GetLocalModelMatrix();
        return Parent == null ? local : local * Parent.GetWorldModelMatrix();
    }
}
