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
        string vertexSource = File.ReadAllText(vertexPath);
        string fragmentSource = File.ReadAllText(fragmentPath);

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
}
