using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Lighting;

namespace ArcEngine.Engine.Rendering;

public class Renderer
{
    public void Init()
    {
        GL.Enable(EnableCap.DepthTest);
    }

    public void Clear()
    {
        GL.ClearColor(0.05f, 0.06f, 0.08f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    /// <summary>
    /// Render a single GameObject. Walks the world transform; uploads model/view/projection +
    /// light state + view position to the object's shader before drawing.
    /// </summary>
    public void RenderObject(GameObject obj, Camera camera, LightSet lights, float aspectRatio)
    {
        // Empty root nodes (e.g. a Model's parent GameObject) have no mesh/material — skip.
        if (obj.Mesh == null || obj.Material == null)
            return;

        obj.Material.Apply();

        var shader = obj.Material.Shader;

        var model = obj.Transform.GetWorldModelMatrix();
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(aspectRatio);

        shader.SetMatrix4("model", model);
        shader.SetMatrix4("view", view);
        shader.SetMatrix4("projection", projection);

        ApplyLighting(shader, lights, camera.Position);

        obj.Mesh.Draw();
    }

    /// <summary>
    /// Push <see cref="LightSet"/> + camera position into the shader's lighting uniforms.
    /// Safe to call multiple times per frame; uniform locations are cached inside <see cref="Shader"/>.
    /// </summary>
    public static void ApplyLighting(Shader shader, LightSet lights, Vector3 viewPos)
    {
        shader.SetVector3("viewPos", viewPos);
        shader.SetVector3("ambient", lights.Ambient);

        // Directional light: when absent, send a black color so the shader's CalcDirLight
        // contributes nothing. (Cheaper than branching in GLSL.)
        if (lights.Sun != null)
        {
            shader.SetVector3("dirLight.direction", lights.Sun.Direction);
            shader.SetVector3("dirLight.color", lights.Sun.EffectiveColor);
        }
        else
        {
            shader.SetVector3("dirLight.direction", new Vector3(0f, -1f, 0f));
            shader.SetVector3("dirLight.color", Vector3.Zero);
        }

        // Point lights.
        int n = Math.Min(lights.Points.Count, LightSet.MaxPointLights);
        shader.SetInt("numPointLights", n);

        for (int i = 0; i < n; i++)
        {
            var p = lights.Points[i];
            shader.SetVector3($"pointLights[{i}].position",  p.Position);
            shader.SetVector3($"pointLights[{i}].color",     p.EffectiveColor);
            shader.SetFloat  ($"pointLights[{i}].constant",  p.Constant);
            shader.SetFloat  ($"pointLights[{i}].linear",    p.Linear);
            shader.SetFloat  ($"pointLights[{i}].quadratic", p.Quadratic);
        }
    }
}
