using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Loaders;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.SandboxGame.Scripts;

namespace ArcEngine.Engine.SandboxGame.Scenes;

/// <summary>
/// Models-only demo: loads an OBJ + a GLB and parks them on the ground. Shows the
/// asset-loading + PBR-shading path without physics, point lights, or particles.
/// </summary>
public class ModelsDemo : IDemoScene
{
    public string Name => "Models (OBJ + GLB)";

    public void Build(Scene scene, Shader sharedShader, InputManager input)
    {
        scene.Ambient = new Vector3(0.18f);

        // OBJ + MTL pipeline.
        var crateData = ArcEngine.Engine.Resources.Resources.LoadModelData("Assets/Models/crate.obj");
        var crate = ModelBuilder.Build(crateData, sharedShader);
        crate.Transform.Position = new Vector3(-1.5f, -0.5f, 0f);
        crate.Name = "Crate (OBJ+MTL)";
        crate.AddComponent<ModelSpinner>().RotationDegPerSec = new Vector3(0f, 25f, 0f);
        scene.Add(crate);

        // GLTF/GLB pipeline.
        var glbData = ArcEngine.Engine.Resources.Resources.LoadModelData("Assets/Models/sample.glb");
        var glb = ModelBuilder.Build(glbData, sharedShader);
        glb.Transform.Position = new Vector3(1.5f, 0f, 0f);
        glb.Name = "Sample (GLB)";
        glb.AddComponent<ModelSpinner>().RotationDegPerSec = new Vector3(0f, -25f, 0f);
        scene.Add(glb);

        SceneBuilders.CreateGroundVisual(scene, sharedShader);
        SceneBuilders.CreateCamera(scene, input);
        SceneBuilders.CreateSun(scene);
    }
}
