using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Lighting;
using ArcEngine.Engine.Loaders;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.SandboxGame.Scripts;

namespace ArcEngine.Engine.SandboxGame.Scenes;

/// <summary>
/// Lighting demo: a row of identical crate models with varied (Metallic, Roughness)
/// values to show off the PBR shading model, plus the directional sun (shadows on)
/// and two orbiting colored point lights.
/// </summary>
public class LightingDemo : IDemoScene
{
    public string Name => "Lighting (PBR + Shadows + Multi-light)";

    public void Build(Scene scene, Shader sharedShader, InputManager input)
    {
        scene.Ambient = new Vector3(0.18f);

        // Five crates left-to-right with roughness 1.0 -> 0.05.
        var crateData = ArcEngine.Engine.Resources.Resources.LoadModelData("Assets/Models/crate.obj");
        const int N = 5;
        for (int i = 0; i < N; i++)
        {
            float t = i / (float)(N - 1);
            float roughness = MathHelper.Lerp(1.0f, 0.05f, t);
            // Alternate metallic on/off so the user sees both ends of the spectrum.
            float metallic = (i % 2 == 0) ? 0f : 1f;

            var go = ModelBuilder.Build(crateData, sharedShader);
            go.Name = $"Crate r={roughness:F2} m={metallic}";
            go.Transform.Position = new Vector3(-3.0f + i * 1.5f, 0f, 0f);

            // Override the material PBR values on every submesh.
            foreach (var mr in CollectMeshRenderers(go))
            {
                if (mr.Material != null)
                {
                    mr.Material.Roughness = roughness;
                    mr.Material.Metallic = metallic;
                }
            }
            scene.Add(go);
        }

        SceneBuilders.CreateGroundVisual(scene, sharedShader);
        SceneBuilders.CreateCamera(scene, input, new Vector3(0f, 1.5f, 7f));
        SceneBuilders.CreateSun(scene, castsShadows: true);

        // Two orbiting point lights.
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

    private static IEnumerable<MeshRenderer> CollectMeshRenderers(GameObject root)
    {
        // Direct + children (the model loader puts MeshRenderers on submesh children).
        var mr = root.GetComponent<MeshRenderer>();
        if (mr != null) yield return mr;
        foreach (var childT in root.Transform.Children)
        {
            var cmr = childT.GameObject.GetComponent<MeshRenderer>();
            if (cmr != null) yield return cmr;
        }
    }
}
