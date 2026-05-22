using OpenTK.Graphics.OpenGL4;

namespace ArcEngine.Engine.Rendering;

/// <summary>
/// GPU mesh wrapper. Vertex layout is interleaved: 3 floats position, 3 floats normal, 2 floats UV (8 floats per vertex, 32 bytes).
/// Attribute locations match <c>basic.vert</c>: 0 = position, 2 = normal, 3 = UV.
///
/// Two construction paths are supported:
/// <list type="bullet">
///   <item><c>Mesh(float[])</c> — non-indexed; draws via <see cref="GL.DrawArrays"/>.</item>
///   <item><c>Mesh(Vertex[], uint[])</c> — indexed; draws via <see cref="GL.DrawElements"/>. This is the path used by all model loaders.</item>
/// </list>
/// </summary>
public class Mesh : IDisposable
{
    private int _vao;
    private int _vbo;
    private int _ebo;
    private int _vertexCount;
    private int _indexCount;
    private bool _indexed;
    private bool _disposed;

    /// <summary>
    /// Build a non-indexed mesh from a flat float array using the engine's
    /// 8-floats-per-vertex layout (pos3, normal3, uv2).
    /// </summary>
    public Mesh(float[] vertices)
    {
        // Stride is 8 floats per vertex (pos3 + normal3 + uv2), NOT 3.
        _vertexCount = vertices.Length / 8;
        _indexed = false;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        SetupAttributes();

        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Build an indexed mesh from a <see cref="Vertex"/> array and a uint index array.
    /// This is the path used by all model loaders (OBJ, GLTF).
    /// </summary>
    public Mesh(Vertex[] vertices, uint[] indices)
    {
        _vertexCount = vertices.Length;
        _indexCount = indices.Length;
        _indexed = true;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();

        GL.BindVertexArray(_vao);

        // VBO
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(
            BufferTarget.ArrayBuffer,
            vertices.Length * Vertex.SizeInBytes,
            vertices,
            BufferUsageHint.StaticDraw);

        // EBO
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(
            BufferTarget.ElementArrayBuffer,
            indices.Length * sizeof(uint),
            indices,
            BufferUsageHint.StaticDraw);

        SetupAttributes();

        // NOTE: the EBO binding is part of the VAO state, so we leave it bound here;
        //       only the ARRAY_BUFFER and VAO are unbound.
        GL.BindVertexArray(0);
    }

    private static void SetupAttributes()
    {
        const int stride = Vertex.SizeInBytes; // 32

        // Position: location 0, 3 floats, offset 0
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);

        // Normal: location 2, 3 floats, offset 12
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        // UV: location 3, 2 floats, offset 24
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(3);
    }

    public void Draw()
    {
        GL.BindVertexArray(_vao);

        if (_indexed)
        {
            GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
        }
        else
        {
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        GL.DeleteBuffer(_vbo);
        if (_indexed) GL.DeleteBuffer(_ebo);
        GL.DeleteVertexArray(_vao);
        GC.SuppressFinalize(this);
    }
}
