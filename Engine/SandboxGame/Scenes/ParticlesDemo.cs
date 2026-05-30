using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Input;
using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.SandboxGame.Scenes;

/// <summary>
/// Particle demo: three particle systems with different parameters — a tall fire
/// in the center, blue sparks on the left, a soft purple plume on the right.
/// Tweak parameters via the inspector.
/// </summary>
public class ParticlesDemo : IDemoScene
{
    public string Name => "Particles (Fire / Sparks / Plume)";

    public void Build(Scene scene, Shader sharedShader, InputManager input)
    {
        // Slightly darker scene so particles read brighter.
        scene.Ambient = new Vector3(0.06f);

        SceneBuilders.CreateGroundVisual(scene, sharedShader);
        SceneBuilders.CreateCamera(scene, input, new Vector3(0f, 1.5f, 5f));
        SceneBuilders.CreateSun(scene, castsShadows: true, intensity: 0.4f);

        // ---- Tall warm fire (center) -------------------------------------
        var fire = new GameObject { Name = "Fire" };
        fire.Transform.Position = new Vector3(0f, -0.5f, 0f);
        var fireSys = fire.AddComponent<ParticleSystem>();
        fireSys.EmissionRate    = 100f;
        fireSys.MaxParticles    = 600;
        fireSys.StartLifetime   = 1.5f;
        fireSys.StartSize       = 0.35f;
        fireSys.EndSize         = 0.05f;
        fireSys.StartColor      = new Vector4(1.0f, 0.7f, 0.25f, 1.0f);
        fireSys.EndColor        = new Vector4(0.6f, 0.05f, 0.0f, 0.0f);
        fireSys.StartVelocity   = new Vector3(0f, 1.6f, 0f);
        fireSys.VelocityRandom  = new Vector3(0.3f, 0.2f, 0.3f);
        fireSys.Gravity         = new Vector3(0f, 0.8f, 0f);
        fireSys.Damping         = 0.5f;
        scene.Add(fire);

        // ---- Blue sparks (left, falling) ---------------------------------
        var sparks = new GameObject { Name = "Sparks (blue)" };
        sparks.Transform.Position = new Vector3(-2.5f, 1.2f, 0f);
        var sparkSys = sparks.AddComponent<ParticleSystem>();
        sparkSys.EmissionRate    = 60f;
        sparkSys.MaxParticles    = 300;
        sparkSys.StartLifetime   = 1.0f;
        sparkSys.StartSize       = 0.06f;
        sparkSys.EndSize         = 0.02f;
        sparkSys.StartColor      = new Vector4(0.4f, 0.8f, 1.0f, 1.0f);
        sparkSys.EndColor        = new Vector4(0.1f, 0.3f, 0.6f, 0.0f);
        sparkSys.StartVelocity   = new Vector3(0f, 0.5f, 0f);
        sparkSys.VelocityRandom  = new Vector3(1.2f, 0.6f, 1.2f);
        sparkSys.Gravity         = new Vector3(0f, -3.0f, 0f);  // sparks fall
        sparkSys.Damping         = 0.1f;
        scene.Add(sparks);

        // ---- Purple plume (right, gentle drift) --------------------------
        var plume = new GameObject { Name = "Plume (purple)" };
        plume.Transform.Position = new Vector3(2.5f, -0.3f, 0f);
        var plumeSys = plume.AddComponent<ParticleSystem>();
        plumeSys.EmissionRate    = 40f;
        plumeSys.MaxParticles    = 250;
        plumeSys.StartLifetime   = 2.5f;
        plumeSys.StartSize       = 0.5f;
        plumeSys.EndSize         = 0.2f;
        plumeSys.StartColor      = new Vector4(0.6f, 0.3f, 0.9f, 0.7f);
        plumeSys.EndColor        = new Vector4(0.2f, 0.1f, 0.4f, 0.0f);
        plumeSys.StartVelocity   = new Vector3(0f, 0.6f, 0f);
        plumeSys.VelocityRandom  = new Vector3(0.4f, 0.1f, 0.4f);
        plumeSys.Gravity         = new Vector3(0f, 0.2f, 0f);
        plumeSys.Damping         = 0.8f;
        scene.Add(plume);
    }
}
