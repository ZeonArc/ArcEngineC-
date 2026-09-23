using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Collections.Generic;
using System.IO;

namespace ArcEngine.Engine.Rendering;

public class Shader
{
    public int Handle;
    private Dictionary<string, int> _uniformLocations = new();

    public Shader(string vertexPath, string fragmentPath)
    {
        string vertexSource = StripNonAscii(File.ReadAllText(vertexPath));
        string fragmentSource = StripNonAscii(File.ReadAllText(fragmentPath));

        int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource, vertexPath);
        int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource, fragmentPath);

        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vertexShader);
        GL.AttachShader(Handle, fragmentShader);
        GL.LinkProgram(Handle);

        // Check link status — silent failures here produce a "valid" program that draws nothing.
        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = GL.GetProgramInfoLog(Handle);
            throw new Exception($"[Shader] Program link failed for ({vertexPath} + {fragmentPath}):\n{log}");
        }

        GL.DetachShader(Handle, vertexShader);
        GL.DetachShader(Handle, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        // Cache uniforms.
        GL.GetProgram(Handle, GetProgramParameterName.ActiveUniforms, out int count);
        for (int i = 0; i < count; i++)
        {
            var key = GL.GetActiveUniform(Handle, i, out _, out _);
            var location = GL.GetUniformLocation(Handle, key);
            _uniformLocations[key] = location;
        }
    }

    /// <summary>
    /// Replace any non-ASCII code point with '?' before compilation. Some GLSL
    /// drivers reject multi-byte UTF-8 sequences (em-dashes, smart quotes,
    /// non-Latin glyphs) even when they appear only inside comments and abort
    /// with a cryptic "unexpected EOF" instead of naming the offending byte.
    /// Doing the strip here keeps a stray typo from bringing down the whole
    /// engine at startup.
    /// </summary>
    private static string StripNonAscii(string source)
    {
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] > 127) return StripNonAsciiSlow(source);
        }
        return source;
    }

    private static string StripNonAsciiSlow(string source)
    {
        var sb = new System.Text.StringBuilder(source.Length);
        foreach (var c in source) sb.Append(c > 127 ? '?' : c);
        return sb.ToString();
    }

    private static int CompileShader(ShaderType type, string source, string path)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new Exception($"[Shader] {type} compile failed for {path}:\n{log}");
        }
        return shader;
    }

    public void Use()
    {
        GL.UseProgram(Handle);
    }

    public void SetInt(string name, int value)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
            GL.Uniform1(location, value);
    }

    public void SetFloat(string name, float value)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
            GL.Uniform1(location, value);
    }

    public void SetMatrix4(string name, Matrix4 value)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
            GL.UniformMatrix4(location, false, ref value);
    }

    public void SetVector3(string name, Vector3 value)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
            GL.Uniform3(location, value);
    }

    public void SetVector2(string name, Vector2 value)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
            GL.Uniform2(location, value);
    }

    /// <summary>
    /// Upload a contiguous array of mat4 uniforms in one call. The GLSL declaration
    /// should be <c>uniform mat4 name[N]</c>. Cached location is looked up under
    /// <c>name[0]</c>.
    /// </summary>
    public void SetMatrix4Array(string name, Matrix4[] values)
    {
        if (!_uniformLocations.TryGetValue(name + "[0]", out int location))
            _uniformLocations.TryGetValue(name, out location);
        if (location < 0) return;
        var flat = new float[values.Length * 16];
        for (int i = 0; i < values.Length; i++)
        {
            var m = values[i];
            int b = i * 16;
            flat[b + 0] = m.M11; flat[b + 1] = m.M12; flat[b + 2] = m.M13; flat[b + 3] = m.M14;
            flat[b + 4] = m.M21; flat[b + 5] = m.M22; flat[b + 6] = m.M23; flat[b + 7] = m.M24;
            flat[b + 8] = m.M31; flat[b + 9] = m.M32; flat[b + 10]= m.M33; flat[b + 11]= m.M34;
            flat[b + 12]= m.M41; flat[b + 13]= m.M42; flat[b + 14]= m.M43; flat[b + 15]= m.M44;
        }
        GL.UniformMatrix4(location, values.Length, false, flat);
    }

    /// <summary>
    /// Upload a contiguous array of vec3 uniforms in one call. The GLSL declaration
    /// should be <c>uniform vec3 name[N]</c>. The cached location is looked up under
    /// <c>name[0]</c> — the standard GLSL convention for array-uniform introspection.
    /// </summary>
    public void SetVector3Array(string name, Vector3[] values)
    {
        if (!_uniformLocations.TryGetValue(name + "[0]", out int location))
            _uniformLocations.TryGetValue(name, out location);
        if (location < 0) return;
        // Flatten to floats — GL.Uniform3 with a location + array-length overload is
        // easiest to feed from managed code.
        var flat = new float[values.Length * 3];
        for (int i = 0; i < values.Length; i++)
        {
            flat[i * 3 + 0] = values[i].X;
            flat[i * 3 + 1] = values[i].Y;
            flat[i * 3 + 2] = values[i].Z;
        }
        GL.Uniform3(location, values.Length, flat);
    }
}
