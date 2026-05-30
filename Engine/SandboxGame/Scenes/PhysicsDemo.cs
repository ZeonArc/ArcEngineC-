using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Loaders;
using ArcEngine.Engine.Physics;
using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.SandboxGame.Scenes;

/// <summary>
/// Physics demo: a stack of crates dropped on the ground. Hit Play (F1 or the menu)
/// to see them topple. Hit Stop to reset.
/// </summary>
public class PhysicsDemo : IDemoScene
{
    public string Name => "Physics (Falling Crate Stack)";

    public void Build(Scene scene, Shader sharedShader, InputManager input)
    {
        scene.Ambient = new Vector3(0.18f);

        SceneBuilders.CreatePhysicsWorld(scene);

        var crateData = ArcEngine.Engine.Resources.Resources.LoadModelData("Assets/Models/crate.obj");

        // 3x3 base, stacked 3 high, with slight horizontal jitter for a more dynamic
        // tumble when the lowest layer settles.
        var rng = new Random(42);
        const int W = 3, H = 4;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        for (int z = 0; z < W; z++)
        {
            var go = ModelBuilder.Build(crateData, sharedShader);
            float jx = (float)(rng.NextDouble() - 0.5) * 0.05f;
            float jz = (float)(rng.NextDouble() - 0.5) * 0.05f;
            go.Transform.Position = new Vector3(
                (x - 1) * 1.05f + jx,
                4f + y * 1.05f,
                (z - 1) * 1.05f + jz);
            go.Name = $"Crate [{x},{y},{z}]";

            var col = go.AddComponent<BoxCollider>();
            col.Size = new Vector3(1f, 1f, 1f);

            var rb = go.AddComponent<Rigidbody>();
            rb.Mass = 1f;
            rb.IsStatic = false;

            scene.Add(go);
        }

        SceneBuilders.CreateGroundVisual(scene, sharedShader);
        SceneBuilders.CreateGroundCollider(scene);
        SceneBuilders.CreateCamera(scene, input, new Vector3(0f, 2.5f, 8f));
        SceneBuilders.CreateSun(scene, castsShadows: true);
    }
}
