using System.Runtime.InteropServices;

using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using SnVec2 = System.Numerics.Vector2;

namespace ArcEngine.Engine.Editor;

/// <summary>
/// Hand-rolled Dear ImGui (Hexa.NET binding) ↔ OpenTK 4 integration. Owns its own
/// GL shader, vertex/index buffers, font atlas texture, and bridges OpenTK input
/// events to ImGui's IO. Standard upstream pattern.
///
/// Lifecycle:
///   ctor(window)  — creates ImGui context, font, shader, buffers; subscribes to events
///   NewFrame(dt, size)  — call once per frame BEFORE issuing any ImGui.* calls
///   Render()  — call after all ImGui.* calls; draws to the currently-bound framebuffer
///   Dispose() — release all GL handles
/// </summary>
public sealed unsafe class ImGuiController : IDisposable
{
    // GL resources
    private int _vao;
    private int _vbo;
    private int _ebo;
    private int _vboSize;
    private int _eboSize;
    private int _shaderProgram;
    private int _attribLocationProj;
    private int _attribLocationTex;
    private int _fontTexture;

    private readonly GameWindow _window;
    private bool _disposed;

    // Mouse-coordinate scale (client → framebuffer). Updated each NewFrame from
    // (framebufferSize / clientSize). On 1.0× displays this is (1,1) and is a no-op;
    // on hi-DPI displays it converts OpenTK's client-space mouse events into the
    // framebuffer-pixel space ImGui uses for hit testing — fixes the "click below
    // the visual position" bug on Windows display scaling.
    private float _mouseScaleX = 1f;
    private float _mouseScaleY = 1f;

    // ----- Embedded GLSL --------------------------------------------------------
    private const string VertexShader = @"#version 330 core
layout (location = 0) in vec2 in_position;
layout (location = 1) in vec2 in_texCoord;
layout (location = 2) in vec4 in_color;

uniform mat4 projection_matrix;

out vec4 color;
out vec2 texCoord;

void main()
{
    gl_Position = projection_matrix * vec4(in_position, 0.0, 1.0);
    color = in_color;
    texCoord = in_texCoord;
}";

    private const string FragmentShader = @"#version 330 core
in vec4 color;
in vec2 texCoord;

uniform sampler2D in_fontTexture;

out vec4 outputColor;

void main()
{
    outputColor = color * texture(in_fontTexture, texCoord);
}";

    public ImGuiController(GameWindow window)
    {
        _window = window;

        var ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);

        // ImGuizmo lives in a separate library but needs to read state from our ImGui context.
        ImGuizmo.SetImGuiContext(ctx);

        var io = ImGui.GetIO();
        io.Fonts.AddFontDefault();
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

        CreateDeviceObjects();
        SubscribeWindowEvents();

        io.DisplaySize = new SnVec2(window.FramebufferSize.X, window.FramebufferSize.Y);
        io.DisplayFramebufferScale = new SnVec2(1f, 1f);

        // Seed the mouse-coord scale from the same ratio NewFrame uses.
        _mouseScaleX = window.ClientSize.X > 0 ? window.FramebufferSize.X / (float)window.ClientSize.X : 1f;
        _mouseScaleY = window.ClientSize.Y > 0 ? window.FramebufferSize.Y / (float)window.ClientSize.Y : 1f;
    }

    // ============================================================================
    // GL resource setup
    // ============================================================================

    private unsafe void CreateDeviceObjects()
    {
        // Shader
        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, VertexShader);
        GL.CompileShader(vs);
        CheckCompile(vs, "ImGui vertex");

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, FragmentShader);
        GL.CompileShader(fs);
        CheckCompile(fs, "ImGui fragment");

        _shaderProgram = GL.CreateProgram();
        GL.AttachShader(_shaderProgram, vs);
        GL.AttachShader(_shaderProgram, fs);
        GL.LinkProgram(_shaderProgram);

        GL.GetProgram(_shaderProgram, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0)
            throw new Exception("[ImGuiController] shader link failed: " + GL.GetProgramInfoLog(_shaderProgram));

        GL.DetachShader(_shaderProgram, vs);
        GL.DetachShader(_shaderProgram, fs);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);

        _attribLocationProj = GL.GetUniformLocation(_shaderProgram, "projection_matrix");
        _attribLocationTex = GL.GetUniformLocation(_shaderProgram, "in_fontTexture");

        // VAO + dynamic VBO/EBO
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);

        int stride = sizeof(ImDrawVert); // 20 bytes (vec2 + vec2 + uint32)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

        // Font atlas
        var io = ImGui.GetIO();
        byte* pixels;
        int width, height, bytesPerPixel;
        io.Fonts.GetTexDataAsRGBA32(&pixels, &width, &height, &bytesPerPixel);

        _fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        io.Fonts.SetTexID(new ImTextureID(_fontTexture));
        io.Fonts.ClearTexData();
    }

    private static void CheckCompile(int shader, string label)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
            throw new Exception($"[ImGuiController] {label} shader compile failed: {GL.GetShaderInfoLog(shader)}");
    }

    // ============================================================================
    // Per-frame
    // ============================================================================

    /// <summary>
    /// Begin a new ImGui frame.
    /// </summary>
    /// <param name="deltaTime">Seconds since last frame.</param>
    /// <param name="clientSize">Window client size in logical/screen coords (matches mouse events).</param>
    /// <param name="framebufferSize">Window framebuffer size in physical pixels (matches GL viewport).</param>
    public void NewFrame(float deltaTime, Vector2i clientSize, Vector2i framebufferSize)
    {
        var io = ImGui.GetIO();

        // Use framebuffer (physical) pixels for DisplaySize so rendering and hit
        // testing share one coordinate system. Then scale incoming mouse events
        // from OpenTK's client coords into this same space (see OnMouseMove).
        io.DisplaySize = new SnVec2(framebufferSize.X, framebufferSize.Y);
        io.DisplayFramebufferScale = new SnVec2(1f, 1f);

        _mouseScaleX = clientSize.X > 0 ? framebufferSize.X / (float)clientSize.X : 1f;
        _mouseScaleY = clientSize.Y > 0 ? framebufferSize.Y / (float)clientSize.Y : 1f;

        io.DeltaTime = deltaTime > 0f ? deltaTime : 1f / 60f;

        ImGui.NewFrame();
    }

    public unsafe void Render()
    {
        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        if (drawData.CmdListsCount == 0) return;

        // Save GL state we touch.
        int prevProgram = GL.GetInteger(GetPName.CurrentProgram);
        int prevVao = GL.GetInteger(GetPName.VertexArrayBinding);
        int prevTexture = GL.GetInteger(GetPName.TextureBinding2D);
        int prevActiveTex = GL.GetInteger(GetPName.ActiveTexture);
        bool prevBlend = GL.IsEnabled(EnableCap.Blend);
        bool prevCullFace = GL.IsEnabled(EnableCap.CullFace);
        bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool prevScissor = GL.IsEnabled(EnableCap.ScissorTest);

        // Setup state for ImGui draws.
        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);

        var io = ImGui.GetIO();
        int fbWidth = (int)(io.DisplaySize.X * io.DisplayFramebufferScale.X);
        int fbHeight = (int)(io.DisplaySize.Y * io.DisplayFramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0) return;

        GL.Viewport(0, 0, fbWidth, fbHeight);

        Matrix4 ortho = Matrix4.CreateOrthographicOffCenter(
            0f, io.DisplaySize.X,
            io.DisplaySize.Y, 0f,
            -1f, 1f);

        GL.UseProgram(_shaderProgram);
        GL.UniformMatrix4(_attribLocationProj, false, ref ortho);
        GL.Uniform1(_attribLocationTex, 0);
        GL.ActiveTexture(TextureUnit.Texture0);

        GL.BindVertexArray(_vao);

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            int vSize = cmdList.VtxBuffer.Size * sizeof(ImDrawVert);
            int iSize = cmdList.IdxBuffer.Size * sizeof(ushort);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            if (vSize > _vboSize)
            {
                _vboSize = System.Math.Max(vSize, _vboSize * 2);
                GL.BufferData(BufferTarget.ArrayBuffer, _vboSize, IntPtr.Zero, BufferUsageHint.StreamDraw);
            }
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vSize, (IntPtr)cmdList.VtxBuffer.Data);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            if (iSize > _eboSize)
            {
                _eboSize = System.Math.Max(iSize, _eboSize * 2);
                GL.BufferData(BufferTarget.ElementArrayBuffer, _eboSize, IntPtr.Zero, BufferUsageHint.StreamDraw);
            }
            GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, iSize, (IntPtr)cmdList.IdxBuffer.Data);

            for (int cmd_i = 0; cmd_i < cmdList.CmdBuffer.Size; cmd_i++)
            {
                ImDrawCmdPtr cmd = new ImDrawCmdPtr(&cmdList.CmdBuffer.Data[cmd_i]);
                if (cmd.UserCallback != null)
                    continue;

                var clip = cmd.ClipRect;
                GL.Scissor(
                    (int)clip.X,
                    (int)(fbHeight - clip.W),
                    (int)(clip.Z - clip.X),
                    (int)(clip.W - clip.Y));

                GL.BindTexture(TextureTarget.Texture2D, (int)cmd.TextureId.Handle);
                GL.DrawElements(
                    PrimitiveType.Triangles,
                    (int)cmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (IntPtr)(cmd.IdxOffset * sizeof(ushort)));
            }
        }

        // Restore GL state.
        GL.UseProgram(prevProgram);
        GL.BindVertexArray(prevVao);
        GL.ActiveTexture((TextureUnit)prevActiveTex);
        GL.BindTexture(TextureTarget.Texture2D, prevTexture);
        if (!prevBlend) GL.Disable(EnableCap.Blend);
        if (prevCullFace) GL.Enable(EnableCap.CullFace);
        if (prevDepthTest) GL.Enable(EnableCap.DepthTest);
        if (!prevScissor) GL.Disable(EnableCap.ScissorTest);
    }

    // ============================================================================
    // Input bridge
    // ============================================================================

    private void SubscribeWindowEvents()
    {
        _window.MouseMove += OnMouseMove;
        _window.MouseDown += OnMouseDown;
        _window.MouseUp += OnMouseUp;
        _window.MouseWheel += OnMouseWheel;
        _window.KeyDown += OnKeyDown;
        _window.KeyUp += OnKeyUp;
        _window.TextInput += OnTextInput;
    }

    private void UnsubscribeWindowEvents()
    {
        _window.MouseMove -= OnMouseMove;
        _window.MouseDown -= OnMouseDown;
        _window.MouseUp -= OnMouseUp;
        _window.MouseWheel -= OnMouseWheel;
        _window.KeyDown -= OnKeyDown;
        _window.KeyUp -= OnKeyUp;
        _window.TextInput -= OnTextInput;
    }

    private void OnMouseMove(MouseMoveEventArgs e)
        => ImGui.GetIO().AddMousePosEvent(e.X * _mouseScaleX, e.Y * _mouseScaleY);

    private static void OnMouseDown(MouseButtonEventArgs e)
        => ImGui.GetIO().AddMouseButtonEvent((int)e.Button, true);

    private static void OnMouseUp(MouseButtonEventArgs e)
        => ImGui.GetIO().AddMouseButtonEvent((int)e.Button, false);

    private static void OnMouseWheel(MouseWheelEventArgs e)
        => ImGui.GetIO().AddMouseWheelEvent(e.OffsetX, e.OffsetY);

    private static void OnKeyDown(KeyboardKeyEventArgs e) => HandleKey(e.Key, true);
    private static void OnKeyUp(KeyboardKeyEventArgs e) => HandleKey(e.Key, false);

    private static void OnTextInput(TextInputEventArgs e)
        => ImGui.GetIO().AddInputCharacter((uint)e.Unicode);

    private static void HandleKey(Keys key, bool down)
    {
        var io = ImGui.GetIO();
        var imguiKey = TranslateKey(key);
        if (imguiKey != ImGuiKey.None) io.AddKeyEvent(imguiKey, down);

        if (key is Keys.LeftControl or Keys.RightControl) io.AddKeyEvent(ImGuiKey.ModCtrl, down);
        if (key is Keys.LeftShift or Keys.RightShift) io.AddKeyEvent(ImGuiKey.ModShift, down);
        if (key is Keys.LeftAlt or Keys.RightAlt) io.AddKeyEvent(ImGuiKey.ModAlt, down);
        if (key is Keys.LeftSuper or Keys.RightSuper) io.AddKeyEvent(ImGuiKey.ModSuper, down);
    }

    private static ImGuiKey TranslateKey(Keys k) => k switch
    {
        Keys.Tab => ImGuiKey.Tab,
        Keys.Left => ImGuiKey.LeftArrow,
        Keys.Right => ImGuiKey.RightArrow,
        Keys.Up => ImGuiKey.UpArrow,
        Keys.Down => ImGuiKey.DownArrow,
        Keys.PageUp => ImGuiKey.PageUp,
        Keys.PageDown => ImGuiKey.PageDown,
        Keys.Home => ImGuiKey.Home,
        Keys.End => ImGuiKey.End,
        Keys.Insert => ImGuiKey.Insert,
        Keys.Delete => ImGuiKey.Delete,
        Keys.Backspace => ImGuiKey.Backspace,
        Keys.Space => ImGuiKey.Space,
        Keys.Enter => ImGuiKey.Enter,
        Keys.Escape => ImGuiKey.Escape,
        Keys.LeftControl => ImGuiKey.LeftCtrl,
        Keys.RightControl => ImGuiKey.RightCtrl,
        Keys.LeftShift => ImGuiKey.LeftShift,
        Keys.RightShift => ImGuiKey.RightShift,
        Keys.LeftAlt => ImGuiKey.LeftAlt,
        Keys.RightAlt => ImGuiKey.RightAlt,
        Keys.A => ImGuiKey.A, Keys.B => ImGuiKey.B, Keys.C => ImGuiKey.C, Keys.D => ImGuiKey.D,
        Keys.E => ImGuiKey.E, Keys.F => ImGuiKey.F, Keys.G => ImGuiKey.G, Keys.H => ImGuiKey.H,
        Keys.I => ImGuiKey.I, Keys.J => ImGuiKey.J, Keys.K => ImGuiKey.K, Keys.L => ImGuiKey.L,
        Keys.M => ImGuiKey.M, Keys.N => ImGuiKey.N, Keys.O => ImGuiKey.O, Keys.P => ImGuiKey.P,
        Keys.Q => ImGuiKey.Q, Keys.R => ImGuiKey.R, Keys.S => ImGuiKey.S, Keys.T => ImGuiKey.T,
        Keys.U => ImGuiKey.U, Keys.V => ImGuiKey.V, Keys.W => ImGuiKey.W, Keys.X => ImGuiKey.X,
        Keys.Y => ImGuiKey.Y, Keys.Z => ImGuiKey.Z,
        Keys.D0 => ImGuiKey.Key0, Keys.D1 => ImGuiKey.Key1, Keys.D2 => ImGuiKey.Key2, Keys.D3 => ImGuiKey.Key3,
        Keys.D4 => ImGuiKey.Key4, Keys.D5 => ImGuiKey.Key5, Keys.D6 => ImGuiKey.Key6, Keys.D7 => ImGuiKey.Key7,
        Keys.D8 => ImGuiKey.Key8, Keys.D9 => ImGuiKey.Key9,
        Keys.F1 => ImGuiKey.F1, Keys.F2 => ImGuiKey.F2, Keys.F3 => ImGuiKey.F3, Keys.F4 => ImGuiKey.F4,
        Keys.F5 => ImGuiKey.F5, Keys.F6 => ImGuiKey.F6, Keys.F7 => ImGuiKey.F7, Keys.F8 => ImGuiKey.F8,
        Keys.F9 => ImGuiKey.F9, Keys.F10 => ImGuiKey.F10, Keys.F11 => ImGuiKey.F11, Keys.F12 => ImGuiKey.F12,
        _ => ImGuiKey.None,
    };

    // ============================================================================
    // Disposal
    // ============================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeWindowEvents();

        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_ebo);
        GL.DeleteVertexArray(_vao);
        GL.DeleteProgram(_shaderProgram);
        GL.DeleteTexture(_fontTexture);
    }
}
