using OpenTK.Mathematics;

namespace ArcEngine.Engine.Core;

/// <summary>
/// Spatial component: position / rotation / scale, plus a parent/children hierarchy and
/// the world-matrix walk used by the renderer. Auto-added to every <see cref="GameObject"/>
/// in its constructor — every GameObject has exactly one Transform.
/// </summary>
public class Transform : Component
{
    public Vector3 Position = Vector3.Zero;
    public Vector3 Rotation = Vector3.Zero;
    public Vector3 Scale = Vector3.One;

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

        // Row-vector convention: v * (S * Rz * Ry * Rx * T).
        return scale * rotationZ * rotationY * rotationX * translation;
    }

    /// <summary>Backward-compat alias.</summary>
    public Matrix4 GetModelMatrix() => GetLocalModelMatrix();

    /// <summary>World model matrix; walks up the parent chain.</summary>
    public Matrix4 GetWorldModelMatrix()
    {
        var local = GetLocalModelMatrix();
        return Parent == null ? local : local * Parent.GetWorldModelMatrix();
    }
}
