using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using ArcEngine.Engine.Math;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// GPU mesh for skinned geometry. Layout differs from <see cref="Mesh"/>:
/// vertices carry bone indices and weights (locations 9 and 10) in addition
/// to the standard position/normal/uv/tangent layout, and there is no
/// per-instance model matrix stream — skinned meshes draw one instance at a
/// time using a plain uniform model matrix.
/// </summary>
public class SkinnedMesh : IDisposable
{
    private int _vao;
    private int _vbo;
    private int _ebo;
    private int _indexCount;
    private bool _disposed;

    public AABB LocalAABB { get; private set; } = AABB.Empty;

    public SkinnedMesh(SkinnedVertex[] vertices, uint[] indices)
    {
        _indexCount = indices.Length;

        if (vertices.Length > 0)
        {
            var pts = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) pts[i] = vertices[i].Position;
            LocalAABB = AABB.FromPoints(pts);
        }

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer,
            vertices.Length * SkinnedVertex.SizeInBytes,
            vertices, BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer,
            indices.Length * sizeof(uint),
            indices, BufferUsageHint.StaticDraw);

        const int stride = SkinnedVertex.SizeInBytes; // 80 (20 floats)

        // Match standard layout for positions (0), normal (2), uv (3), tangent (4).
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);

        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(3);

        GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
        GL.EnableVertexAttribArray(4);

        // Skinning-specific slots: bone indices (9), bone weights (10).
        GL.VertexAttribPointer(9,  4, VertexAttribPointerType.Float, false, stride, 12 * sizeof(float));
        GL.EnableVertexAttribArray(9);

        GL.VertexAttribPointer(10, 4, VertexAttribPointerType.Float, false, stride, 16 * sizeof(float));
        GL.EnableVertexAttribArray(10);

        GL.BindVertexArray(0);
    }

    /// <summary>Draw one instance of the skinned mesh. Caller has already bound the shader + uniforms.</summary>
    public void Draw()
    {
        GL.BindVertexArray(_vao);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_ebo);
        GL.DeleteVertexArray(_vao);
        GC.SuppressFinalize(this);
    }
}
