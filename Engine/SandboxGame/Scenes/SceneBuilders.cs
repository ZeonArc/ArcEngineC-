using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Lighting;
using ArcEngine.Engine.Physics;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.SandboxGame.Scripts;

namespace ArcEngine.Engine.SandboxGame.Scenes;

/// <summary>
/// Reusable builders shared across demo scenes — so each demo focuses on its own
/// feature instead of repeating boilerplate (camera, sun, ground plane, etc.).
/// </summary>
public static class SceneBuilders
{
    public static GameObject CreatePhysicsWorld(Scene scene)
    {
        var go = new GameObject { Name = "Physics" };
        go.AddComponent<PhysicsWorld>();
        scene.Add(go);
        return go;
    }

    public static GameObject CreateCamera(Scene scene, InputManager input, Vector3? position = null)
    {
        var go = new GameObject { Name = "Main Camera" };
        go.Transform.Position = position ?? new Vector3(0f, 1f, 6f);
        go.AddComponent<Camera>();
        var ctrl = go.AddComponent<FpsCameraController>();
        ctrl.Input = input;
        scene.Add(go);
        return go;
    }

    public static GameObject CreateSun(Scene scene, bool castsShadows = true, float intensity = 0.7f)
    {
        var go = new GameObject { Name = "Sun" };
        var dl = go.AddComponent<DirectionalLight>();
        dl.Direction = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f));
        dl.Color = new Vector3(1.0f, 0.96f, 0.9f);
        dl.Intensity = intensity;
        dl.CastsShadows = castsShadows;
        scene.Add(go);
        return go;
    }

    public static GameObject CreateGroundVisual(Scene scene, Shader shader, float size = 20f, float uvTiling = 4f)
    {
        var go = new GameObject { Name = "Ground (visual)" };
        var mr = go.AddComponent<MeshRenderer>();
        mr.Mesh = Primitives.CreatePlane(size, uvTiling);
        mr.Material = new Material(shader)
        {
            Color = new Vector3(0.45f, 0.45f, 0.5f),
            Metallic = 0f,
            Roughness = 0.85f,
            AmbientOcclusion = 1f,
        };
        go.Transform.Position = new Vector3(0f, -1f, 0f);
        scene.Add(go);
        return go;
    }

    public static GameObject CreateGroundCollider(Scene scene)
    {
        var go = new GameObject { Name = "Ground (collider)" };
        go.Transform.Position = new Vector3(0f, -1.1f, 0f);
        var bc = go.AddComponent<BoxCollider>();
        bc.Size = new Vector3(20f, 0.2f, 20f);
        var rb = go.AddComponent<Rigidbody>();
        rb.IsStatic = true;
        scene.Add(go);
        return go;
    }
}
