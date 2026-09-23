using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace ArcEngine.Engine.UI;

/// <summary>
/// Collects UI quads (colored or textured) and flushes them as a single
/// interleaved draw call per texture. Vertices are pixel-space; the Canvas
/// installs an orthographic projection at flush time.
///
/// One vertex is 9 floats: pos.xy, uv.xy, color.rgba, textureMode.
/// </summary>
public class UIBatch : IDisposable
{
    /// <summary>How many vertices we can queue before growing the CPU buffer.</summary>
    private const int InitialCapacity = 1024;

    /// <summary>Sentinel: no texture — quads emitted in this segment are solid colored.</summary>
    public const int NoTexture = -1;

    private int _vao = -1;
    private int _vbo = -1;
    private int _vboCapacityVerts;

    /// <summary>Interleaved vertex data. Grows as needed. Reused every frame.</summary>
    private float[] _verts = new float[InitialCapacity * VertexFloats];

    /// <summary>Current write cursor into <see cref="_verts"/>.</summary>
    private int _vertexCount;

    /// <summary>Segments: (textureHandle, vertexCount). Flushed one glDraw per segment.</summary>
    private readonly List<(int tex, int vertCount)> _segments = new();
    private int _currentTexture = NoTexture;
    private int _currentSegmentStartVerts;

    private const int VertexFloats = 9;
    private const int VertexBytes = VertexFloats * sizeof(float);

    public void Init()
    {
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        _vboCapacityVerts = InitialCapacity;
        GL.BufferData(BufferTarget.ArrayBuffer, _vboCapacityVerts * VertexBytes, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        // pos (vec2, loc 0), uv (vec2, loc 1), color (vec4, loc 2), textureMode (float, loc 3)
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VertexBytes, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VertexBytes, 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, VertexBytes, 4 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, VertexBytes, 8 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        GL.BindVertexArray(0);
    }

    /// <summary>Reset the frame's queue. Call at the top of each frame.</summary>
    public void Begin()
    {
        _vertexCount = 0;
        _segments.Clear();
        _currentTexture = NoTexture;
        _currentSegmentStartVerts = 0;
    }

    // ============================================================================
    // Quad emission
    // ============================================================================

    /// <summary>Colored (no texture) rect.</summary>
    public void QuadColor(UIRect rect, Vector4 color)
    {
        SwitchTexture(NoTexture);
        WriteQuad(rect, new UIRect(Vector2.Zero, Vector2.Zero), color, textureMode: 0f);
    }

    /// <summary>Textured rect. Full-atlas UV; use <see cref="QuadTexturedUV"/> for a sub-rect.</summary>
    public void QuadTextured(UIRect rect, int textureHandle, Vector4 tint)
    {
        SwitchTexture(textureHandle);
        WriteQuad(rect, new UIRect(new Vector2(0, 0), new Vector2(1, 1)), tint, textureMode: 1f);
    }

    /// <summary>Textured rect with an explicit UV sub-rect (glyph atlas, sprite sheet, etc.).</summary>
    public void QuadTexturedUV(UIRect rect, int textureHandle, UIRect uv, Vector4 tint)
    {
        SwitchTexture(textureHandle);
        WriteQuad(rect, uv, tint, textureMode: 1f);
    }

    private void SwitchTexture(int tex)
    {
        if (_currentTexture == tex) return;
        // Close the previous segment.
        if (_vertexCount > _currentSegmentStartVerts)
            _segments.Add((_currentTexture, _vertexCount - _currentSegmentStartVerts));
        _currentTexture = tex;
        _currentSegmentStartVerts = _vertexCount;
    }

    private void WriteQuad(UIRect rect, UIRect uv, Vector4 color, float textureMode)
    {
        EnsureVertexCapacity(6);
        // Two triangles: TL, TR, BR / TL, BR, BL. Winding: CCW in screen space (top-left origin).
        WriteVertex(rect.Min.X, rect.Min.Y, uv.Min.X, uv.Min.Y, color, textureMode);
        WriteVertex(rect.Max.X, rect.Min.Y, uv.Max.X, uv.Min.Y, color, textureMode);
        WriteVertex(rect.Max.X, rect.Max.Y, uv.Max.X, uv.Max.Y, color, textureMode);

        WriteVertex(rect.Min.X, rect.Min.Y, uv.Min.X, uv.Min.Y, color, textureMode);
        WriteVertex(rect.Max.X, rect.Max.Y, uv.Max.X, uv.Max.Y, color, textureMode);
        WriteVertex(rect.Min.X, rect.Max.Y, uv.Min.X, uv.Max.Y, color, textureMode);
    }

    private void WriteVertex(float x, float y, float u, float v, Vector4 c, float mode)
    {
        int b = _vertexCount * VertexFloats;
        _verts[b + 0] = x;     _verts[b + 1] = y;
        _verts[b + 2] = u;     _verts[b + 3] = v;
        _verts[b + 4] = c.X;   _verts[b + 5] = c.Y; _verts[b + 6] = c.Z; _verts[b + 7] = c.W;
        _verts[b + 8] = mode;
        _vertexCount++;
    }

    private void EnsureVertexCapacity(int needed)
    {
        if (_vertexCount + needed <= _verts.Length / VertexFloats) return;
        int newSize = System.Math.Max((_vertexCount + needed) * VertexFloats, _verts.Length * 2);
        Array.Resize(ref _verts, newSize);
    }

    // ============================================================================
    // Flush
    // ============================================================================

    public void Flush(Rendering.Shader shader, int screenWidth, int screenHeight)
    {
        // Close any open segment.
        if (_vertexCount > _currentSegmentStartVerts)
            _segments.Add((_currentTexture, _vertexCount - _currentSegmentStartVerts));
        _currentTexture = NoTexture;

        if (_vertexCount == 0) return;

        // Orthographic: origin top-left, +Y down.
        var proj = Matrix4.CreateOrthographicOffCenter(0f, screenWidth, screenHeight, 0f, -1f, 1f);

        shader.Use();
        shader.SetMatrix4("u_projection", proj);
        shader.SetInt("u_texture", 0);

        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        if (_vertexCount > _vboCapacityVerts)
        {
            _vboCapacityVerts = System.Math.Max(_vertexCount, _vboCapacityVerts * 2);
            GL.BufferData(BufferTarget.ArrayBuffer, _vboCapacityVerts * VertexBytes, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        }
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _vertexCount * VertexBytes, _verts);

        int cursor = 0;
        foreach (var (tex, count) in _segments)
        {
            if (count == 0) { continue; }
            if (tex != NoTexture)
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, tex);
            }
            GL.DrawArrays(PrimitiveType.Triangles, cursor, count);
            cursor += count;
        }

        GL.BindVertexArray(0);
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        if (_vbo != -1) { GL.DeleteBuffer(_vbo); _vbo = -1; }
        if (_vao != -1) { GL.DeleteVertexArray(_vao); _vao = -1; }
    }
}
