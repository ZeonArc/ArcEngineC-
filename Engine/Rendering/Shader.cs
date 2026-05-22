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

        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexSource);
        GL.CompileShader(vertexShader);

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragmentSource);
        GL.CompileShader(fragmentShader);

        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vertexShader);
        GL.AttachShader(Handle, fragmentShader);
        GL.LinkProgram(Handle);

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        // Cache uniforms
        GL.GetProgram(Handle, GetProgramParameterName.ActiveUniforms, out int count);

        for (int i = 0; i < count; i++)
        {
            var key = GL.GetActiveUniform(Handle, i, out _, out _);
            var location = GL.GetUniformLocation(Handle, key);
            _uniformLocations[key] = location;
        }
    }

    public void Use()
    {
        GL.UseProgram(Handle);
    }
    public void SetInt(string name, int value)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
        {
            GL.Uniform1(location, value);
        }
    }

    public void SetFloat(string name, float value)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
        {
            GL.Uniform1(location, value);
        }
    }

    public void SetMatrix4(string name, Matrix4 value)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
        {
            GL.UniformMatrix4(location, false, ref value);
        }
    }

    public void SetVector3(string name, Vector3 value)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
        {
            GL.Uniform3(location, value);
        }
    }
}