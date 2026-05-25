using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Lighting;
using ArcEngine.Engine.Physics;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.SandboxGame.Scripts;

namespace ArcEngine.Engine.SandboxGame;

/// <summary>
/// Phase-9a demo scene: textured OBJ crate falls under gravity onto a static ground
/// plane, while the GLB sample stays as visual only. Lit by a directional sun (with
/// shadows) plus two orbiting colored point lights. FPS camera navigation.
/// </summary>
public class SandboxScene
{
    public void Build(Scene scene, Shader sharedShader, InputManager input)
    {
        scene.Ambient = new Vector3(0.08f);

        // ---- PhysicsWorld first so it Awakes before any Rigidbody ------------------
        var physicsGo = new GameObject { Name = "Physics" };
        physicsGo.AddComponent<PhysicsWorld>();
        scene.Add(physicsGo);

        // ---- Models ----------------------------------------------------------------
        // OBJ + MTL pipeline; dropped from height with physics.
        var crateData = ArcEngine.Engine.Resources.Resources.LoadModelData("Assets/Models/crate.obj");
        var crate = ArcEngine.Engine.Loaders.ModelBuilder.Build(crateData, sharedShader);
        crate.Transform.Position = new Vector3(-1.5f, 4f, 0f);
        crate.Name = "Crate (OBJ+MTL)";

        var crateCollider = crate.AddComponent<BoxCollider>();
        crateCollider.Size = new Vector3(1f, 1f, 1f);

        var crateRb = crate.AddComponent<Rigidbody>();
        crateRb.Mass = 1f;
        crateRb.IsStatic = false;

        scene.Add(crate);

        // GLTF/GLB pipeline — visual-only, kept as a spinner for contrast.
        var glbData = ArcEngine.Engine.Resources.Resources.LoadModelData("Assets/Models/sample.glb");
        var glb = ArcEngine.Engine.Loaders.ModelBuilder.Build(glbData, sharedShader);
        glb.Transform.Position = new Vector3(1.5f, 0f, 0f);
        glb.Name = "Sample (GLB)";
        var glbSpinner = glb.AddComponent<ModelSpinner>();
        glbSpinner.RotationDegPerSec = new Vector3(0f, -30f, 0f);
        scene.Add(glb);

        // ---- Ground (visual mesh) -------------------------------------------------
        // Plane mesh rendered at y = -1.
        var ground = new GameObject { Name = "Ground (visual)" };
        var groundMr = ground.AddComponent<MeshRenderer>();
        groundMr.Mesh = Primitives.CreatePlane(20f, 4f);
        groundMr.Material = new Material(sharedShader)
        {
            Color = new Vector3(0.45f, 0.45f, 0.5f),
            Shininess = 8f,
        };
        ground.Transform.Position = new Vector3(0f, -1f, 0f);
        scene.Add(ground);

        // ---- Ground (physics body) -------------------------------------------------
        // A separate static body whose top surface aligns with the visual plane at y=-1.
        // Box of size 20 × 0.2 × 20 centered at (0, -1.1, 0): top = -1.0, bottom = -1.2.
        var groundCollider = new GameObject { Name = "Ground (collider)" };
        groundCollider.Transform.Position = new Vector3(0f, -1.1f, 0f);
        var gc = groundCollider.AddComponent<BoxCollider>();
        gc.Size = new Vector3(20f, 0.2f, 20f);
        var grb = groundCollider.AddComponent<Rigidbody>();
        grb.IsStatic = true;
        scene.Add(groundCollider);

        // ---- Camera ----------------------------------------------------------------
        var cameraGo = new GameObject { Name = "Main Camera" };
        cameraGo.Transform.Position = new Vector3(0f, 1f, 6f);
        cameraGo.AddComponent<Camera>();
        var controller = cameraGo.AddComponent<FpsCameraController>();
        controller.Input = input;
        scene.Add(cameraGo);

        // ---- Lights ----------------------------------------------------------------
        var sunGo = new GameObject { Name = "Sun" };
        var sun = sunGo.AddComponent<DirectionalLight>();
        sun.Direction = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f));
        sun.Color = new Vector3(1.0f, 0.96f, 0.9f);
        sun.Intensity = 0.7f;
        sun.CastsShadows = true;
        scene.Add(sunGo);

        var lightAgo = new GameObject { Name = "OrbitLight A (warm)" };
        var lightA = lightAgo.AddComponent<PointLight>();
        lightA.Color = new Vector3(1.0f, 0.6f, 0.3f);
        lightA.Intensity = 1.5f;
        var orbitA = lightAgo.AddComponent<OrbitLight>();
        orbitA.PhaseOffset = 0f;
        scene.Add(lightAgo);

        var lightBgo = new GameObject { Name = "OrbitLight B (cool)" };
        var lightB = lightBgo.AddComponent<PointLight>();
        lightB.Color = new Vector3(0.3f, 0.6f, 1.0f);
        lightB.Intensity = 1.5f;
        var orbitB = lightBgo.AddComponent<OrbitLight>();
        orbitB.PhaseOffset = MathF.PI;
        scene.Add(lightBgo);
    }
}
