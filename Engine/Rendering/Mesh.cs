using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using ArcEngine.Engine.Math;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// GPU mesh wrapper, always-instanced model.
///
/// Vertex layout (interleaved <see cref="Vertex"/>, 44 B):
/// <list type="bullet">
///   <item>0 = position</item>
///   <item>2 = normal</item>
///   <item>3 = uv</item>
///   <item>4 = tangent</item>
/// </list>
/// Per-instance attribute (separate VBO, divisor 1):
/// <list type="bullet">
///   <item>5..8 = mat4 model (4 vec4 attribute slots)</item>
/// </list>
/// </summary>
public class Mesh : IDisposable
{
    private int _vao;
    private int _vbo;
    private int _ebo;
    private int _instanceVbo;     // dynamic per-instance model matrices
    private int _instanceCapacity;// in matrices, not bytes
    private int _vertexCount;
    private int _indexCount;
    private bool _indexed;
    private bool _disposed;

    /// <summary>Local-space AABB enclosing this mesh's vertex positions.</summary>
    public AABB LocalAABB { get; private set; } = AABB.Empty;

    /// <summary>
    /// Build a non-indexed mesh from a flat float array using the legacy
    /// 8-floats-per-vertex layout (pos3, normal3, uv2). Tangent attribute disabled
    /// (location 4 not enabled). Used only by the legacy procedural path; almost
    /// nothing in the current engine takes this route.
    /// </summary>
    public Mesh(float[] vertices)
    {
        _vertexCount = vertices.Length / 8;
        _indexed = false;

        // AABB from positions (every 8th, 8th+1, 8th+2 entry).
        if (_vertexCount > 0)
        {
            var pts = new Vector3[_vertexCount];
            for (int i = 0; i < _vertexCount; i++)
                pts[i] = new Vector3(vertices[i*8 + 0], vertices[i*8 + 1], vertices[i*8 + 2]);
            LocalAABB = AABB.FromPoints(pts);
        }

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        SetupLegacyAttributes();
        SetupInstanceAttribute();
        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Build an indexed mesh from a <see cref="Vertex"/> array and a uint index array.
    /// This is the path used by all model loaders (OBJ, GLTF) and procedural primitives.
    /// </summary>
    public Mesh(Vertex[] vertices, uint[] indices)
    {
        _vertexCount = vertices.Length;
        _indexCount = indices.Length;
        _indexed = true;

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
        GL.BufferData(
            BufferTarget.ArrayBuffer,
            vertices.Length * Vertex.SizeInBytes,
            vertices,
            BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(
            BufferTarget.ElementArrayBuffer,
            indices.Length * sizeof(uint),
            indices,
            BufferUsageHint.StaticDraw);

        SetupIndexedAttributes();
        SetupInstanceAttribute();

        GL.BindVertexArray(0);
    }

    /// <summary>Indexed-path layout: pos / normal / uv / tangent at locations 0, 2, 3, 4.</summary>
    private static void SetupIndexedAttributes()
    {
        const int stride = Vertex.SizeInBytes; // 44

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);

        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(3);

        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
        GL.EnableVertexAttribArray(4);
    }

    /// <summary>Legacy float[] layout (8 floats / 32 B): no tangent.</summary>
    private static void SetupLegacyAttributes()
    {
        const int stride = 8 * sizeof(float);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);

        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(3);
    }

    /// <summary>
    /// Per-instance attribute setup: a mat4 occupies 4 attribute slots (5..8). Stride = 64 bytes;
    /// each row is a vec4. Divisor 1 means "advance once per instance" (vs once per vertex).
    /// </summary>
    private void SetupInstanceAttribute()
    {
        _instanceVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
        // Allocate space for 1 matrix initially; grows lazily in DrawInstanced.
        _instanceCapacity = 1;
        GL.BufferData(BufferTarget.ArrayBuffer, _instanceCapacity * 64, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        const int stride = 64; // mat4 = 4 vec4
        for (int i = 0; i < 4; i++)
        {
            GL.VertexAttribPointer(5 + i, 4, VertexAttribPointerType.Float, false, stride, i * 16);
            GL.EnableVertexAttribArray(5 + i);
            GL.VertexAttribDivisor(5 + i, 1);
        }
    }

    /// <summary>
    /// Draw <paramref name="models"/>.Count copies of this mesh, one per matrix.
    /// Uses <c>CollectionsMarshal.AsSpan</c> + fixed-pointer GL upload so callers can
    /// reuse pooled <see cref="List{Matrix4}"/> instances frame-to-frame without
    /// copying through a temporary array.
    /// </summary>
    public unsafe void DrawInstanced(System.Collections.Generic.List<Matrix4> models)
    {
        int count = models.Count;
        if (count == 0) return;

        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
        if (count > _instanceCapacity)
        {
            _instanceCapacity = System.Math.Max(count, _instanceCapacity * 2);
            GL.BufferData(BufferTarget.ArrayBuffer, _instanceCapacity * 64, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        }

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(models);
        fixed (Matrix4* ptr = span)
        {
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, count * 64, (IntPtr)ptr);
        }

        if (_indexed)
            GL.DrawElementsInstanced(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, IntPtr.Zero, count);
        else
            GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, _vertexCount, count);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        GL.DeleteBuffer(_vbo);
        if (_indexed) GL.DeleteBuffer(_ebo);
        GL.DeleteBuffer(_instanceVbo);
        GL.DeleteVertexArray(_vao);
        GC.SuppressFinalize(this);
    }
}
