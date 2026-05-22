using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Lighting;
using ArcEngine.Engine.Loaders;
using ArcEngine.Engine.Rendering;

namespace ArcEngine.Engine.SandboxGame;

/// <summary>
/// Phase-5a demo scene: textured OBJ crate + GLB sample sit on a tiled ground plane,
/// lit by a directional "sun" plus two colored point lights orbiting around them.
/// Acts as the integration test across loaders, lighting, materials, and Mesh/Material/
/// Transform-hierarchy pipeline.
/// </summary>
public class SandboxScene
{
    private GameObject? _crate;
    private GameObject? _glb;
    private GameObject? _ground;

    private PointLight? _orbitLightA;
    private PointLight? _orbitLightB;
    private float _orbitTime;

    /// <summary>Lighting used to render this scene. Populated in <see cref="Build"/>.</summary>
    public LightSet Lights { get; } = new();

    /// <summary>
    /// Loads both demo models, builds the ground plane, configures lighting,
    /// and adds everything to the scene.
    /// </summary>
    public void Build(Scene scene, Shader sharedShader)
    {
        // 1) OBJ + MTL pipeline: textured crate from our hand-written parser.
        var crateData = ObjLoader.Load("Assets/Models/crate.obj");
        _crate = ModelBuilder.Build(crateData, sharedShader);
        _crate.Transform.Position = new Vector3(-1.5f, 0f, 0f);
        _crate.Name = "Crate (OBJ+MTL)";
        scene.Add(_crate);

        // 2) GLTF/GLB pipeline: SharpGLTF-backed loader.
        var glbData = GltfLoader.Load("Assets/Models/sample.glb");
        _glb = ModelBuilder.Build(glbData, sharedShader);
        _glb.Transform.Position = new Vector3(1.5f, 0f, 0f);
        _glb.Name = "Sample (GLB)";
        scene.Add(_glb);

        // 3) Ground plane: 20×20 with 4×4 UV tiling (texture-less but the material's
        //    diffuse color + Blinn-Phong spec respond visibly to the lights).
        _ground = new GameObject
        {
            Name = "Ground",
            Mesh = Primitives.CreatePlane(20f, 4f),
            Material = new Material(sharedShader)
            {
                Color = new Vector3(0.45f, 0.45f, 0.5f),
                Shininess = 8f,
            },
        };
        _ground.Transform.Position = new Vector3(0f, -1f, 0f);
        scene.Add(_ground);

        // 4) Lighting setup.
        Lights.Ambient = new Vector3(0.08f);

        Lights.Sun = new DirectionalLight
        {
            Direction = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f)),
            Color = new Vector3(1.0f, 0.96f, 0.9f),
            Intensity = 0.7f,
        };

        // Two orbiting point lights — warm orange and cool blue.
        // Stored as fields so Update() can move them each frame.
        _orbitLightA = new PointLight
        {
            Color = new Vector3(1.0f, 0.6f, 0.3f),
            Intensity = 1.5f,
        };
        _orbitLightB = new PointLight
        {
            Color = new Vector3(0.3f, 0.6f, 1.0f),
            Intensity = 1.5f,
        };
        Lights.Points.Add(_orbitLightA);
        Lights.Points.Add(_orbitLightB);
    }

    /// <summary>Per-frame update: spin both demo models and orbit the two point lights.</summary>
    public void Update(float deltaTime)
    {
        if (_crate != null) _crate.Transform.Rotation.Y += 30f * deltaTime;
        if (_glb   != null) _glb.Transform.Rotation.Y   -= 30f * deltaTime;

        // Orbit both point lights at radius 2.5 around the origin, half a turn apart,
        // floating slightly above the floor.
        _orbitTime += deltaTime;
        const float radius = 2.5f;
        const float speed = 1.0f; // radians per second
        const float height = 0.6f;

        if (_orbitLightA != null)
        {
            float a = _orbitTime * speed;
            _orbitLightA.Position = new Vector3(MathF.Cos(a) * radius, height, MathF.Sin(a) * radius);
        }
        if (_orbitLightB != null)
        {
            float a = _orbitTime * speed + MathF.PI;
            _orbitLightB.Position = new Vector3(MathF.Cos(a) * radius, height, MathF.Sin(a) * radius);
        }
    }
}
