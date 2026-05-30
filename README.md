# ArcEngine

A 3D game engine built from scratch in C# with OpenTK 4 — featuring a real-time PBR renderer, a Unity-style component system, BepuPhysics-driven rigid-body dynamics, an in-engine editor with translate/rotate/scale gizmos, scene save/load, and a GPU-instanced batch renderer with frustum culling.

> Educational / hobby project. Not production-ready, not a Unity replacement. Built one feature at a time, designed to be readable rather than maximally optimized.

---

## Status

| | |
|---|---|
| Language | C# 12 |
| Runtime | .NET 9 |
| Graphics | OpenGL 3.3 Core (via OpenTK 4) |
| Physics | BepuPhysics 2 |
| UI | Dear ImGui (Hexa.NET.ImGui binding) |
| Platform | Windows (tested); Linux/macOS likely work — untested |
| License | MIT |
| Build | `dotnet build` — 0 errors, 0 warnings |

---

## Highlights

### Rendering
- **Physically-based shading** — Cook-Torrance BRDF (GGX NDF + Smith geometry + Schlick Fresnel)
- **Metallic-roughness material model** with normal maps, AO maps, and combined MR maps (matches glTF convention)
- **Soft shadow mapping** for directional lights — 3×3 PCF with slope-scaled bias, 2048² depth FBO per light
- **Multi-light** — 1 directional + up to 4 point lights with quadratic attenuation
- **GPU-instanced rendering** — every draw call is `glDrawElementsInstanced`; identical `(Mesh, Material)` pairs batch automatically
- **Frustum culling** — per-mesh AABB tested against the camera frustum (and the light frustum during the shadow pass) using the positive-vertex method
- **Linear-space pipeline** — sRGB textures auto-linearize on sample, Reinhard tonemap + 1/2.2 gamma at frag-shader output
- **CPU-simulated billboard particle system** with additive blending and procedural soft-circle texture

### Asset Loading
- **Wavefront OBJ + MTL** (hand-written parser) — multi-submesh support, tangent generation from UV gradients, polygon fan triangulation
- **glTF / GLB** (via SharpGLTF) — base color, MR, normal, and occlusion channels; PBR factors; embedded textures; falls back to computing tangents when not provided

### Component System (Unity-style)
- `Component` base with `Awake / Start / Update / LateUpdate / OnDestroy` lifecycle driven by `Scene.Update(dt)`
- `Transform` hierarchy with parent / children
- `MeshRenderer` / `Camera` / `Light` (directional + point) / `Rigidbody` / `BoxCollider` / `SphereCollider` / `ParticleSystem` / `Script`
- `GameObject.AddComponent<T>()` / `GetComponent<T>()` / `RemoveComponent`
- Multiple components of the same type per GameObject (Unity rules)

### Physics (BepuPhysics 2.4.0)
- Rigid bodies (dynamic + static) with box and sphere colliders
- Fixed 60 Hz timestep with accumulator + max-steps-per-frame catchup
- Snap-back on Stop — physics state is captured on Play, restored on Stop

### In-Engine Editor (Dear ImGui)
- **Hierarchy** — every GameObject in the scene; multi-select with Shift / Ctrl
- **Inspector** — editable per-component (Transform / Material PBR sliders / Light / Rigidbody / Collider / ParticleSystem)
- **3D Gizmos** (ImGuizmo) — translate / rotate / scale handles on the selected GameObject
- **Asset Browser** — folder tree + file list of `Assets/`; right-click models to load into the scene
- **Scene Save/Load** — JSON via `System.Text.Json`; Ctrl+S / Ctrl+O hotkeys
- **Edit / Play modes** — F1 toggles; Play snapshots the scene state, Stop restores it
- **Edit-mode camera** — hold right mouse button to look + WASD/QE to fly; release to interact with UI

### Performance
- Always-instanced render path — `(Mesh, Material)` batches reuse list backing arrays frame-to-frame
- Per-shader-per-frame uniform cache — view / projection / lights set once per shader, not per batch
- `CollectionsMarshal.AsSpan` + fixed pointer for instance-VBO upload (no per-frame array copies)
- Path-keyed resource cache — same texture / shader / model file loads exactly once

---

## Quick Start

### Prerequisites

- **.NET 9 SDK** — [download here](https://dotnet.microsoft.com/download)
- A GPU with **OpenGL 3.3** support (basically anything from 2010+)
- Windows, Linux, or macOS

### Build & Run

```bash
git clone <your-fork-url> ArcEngine
cd ArcEngine
dotnet run --project ArcEngineC#.csproj
```

A window opens into the **Models** demo scene. Use the editor menu (top of the window) to switch scenes.

### Controls

| Action | Edit Mode (default) | Play Mode |
|---|---|---|
| Move camera | Hold **RMB** + WASD | WASD always |
| Vertical move | Hold **RMB** + Q / E | Q / E always |
| Look around | Hold **RMB** + mouse | Mouse always |
| Toggle Edit / Play | F1 | F1 |
| Gizmo: Translate | W (when not flying) | — |
| Gizmo: Rotate | E (when not flying) | — |
| Gizmo: Scale | R (when not flying) | — |
| Gizmo: World ↔ Local | G (when not flying) | — |
| Save scene | Ctrl+S | — |
| Load scene | Ctrl+O | — |

Gizmo hotkeys are suppressed while the editor camera is engaged (so pressing W to fly forward doesn't also flip the gizmo to Translate).

---

## Sample Scenes

Switch via **Scenes** menu. Each demo focuses on a single feature so the rendering pipeline isn't taxed by everything at once.

| Scene | Focus | Contents |
|---|---|---|
| **Models** | Asset loading & PBR | Crate (OBJ + MTL) + GLB sample on a ground plane, both spinning, lit by sun |
| **Lighting** | PBR + shadows + multi-light | 5 crates with varying metallic + roughness (rough → mirror), sun + 2 orbiting colored point lights, soft shadows |
| **Physics** | Rigid-body dynamics | 3×3×4 stack of falling crates on a static ground; press F1 / Play to drop |
| **Particles** | Particle system | 3 systems running simultaneously — warm fire, blue falling sparks, purple plume; tunable from inspector |

---

## Project Structure

```
Engine/
├── Core/                 — GameObject, Component base, Scene, Transform, Camera, Game (entry)
├── Editor/               — ImGuiController, EditorUI, MainMenu, ComponentInspector, AssetBrowser, gizmo hooks
├── Input/                — InputManager (keyboard + mouse + cursor mutation)
├── Lighting/             — Light base, DirectionalLight (with shadow FBO), PointLight
├── Loaders/              — ObjLoader, MtlLoader, GltfLoader, ModelBuilder, TangentGenerator
├── Math/                 — AABB, Frustum, MathConversions (OpenTK ↔ System.Numerics)
├── Physics/              — PhysicsWorld, Rigidbody, Collider base, BoxCollider, SphereCollider, callbacks
├── Rendering/            — Renderer, Mesh, Material, Texture, Shader, MeshRenderer, ParticleSystem, Primitives, Vertex
├── Resources/            — central asset cache (textures, shaders, model data)
├── SandboxGame/
│   ├── Scenes/           — IDemoScene + 4 demo implementations + DemoRegistry + SceneBuilders
│   └── Scripts/          — FpsCameraController, ModelSpinner, OrbitLight
└── Serialization/        — SceneSerializer (JSON state save/load)

Assets/
├── Models/               — cube.obj, multimesh.obj, crate.obj + crate.mtl, sample.glb
├── Shaders/              — basic.vert/.frag (PBR), shadow_depth.vert/.frag, particles.vert/.frag
├── Textures/             — test.png
└── Scenes/               — JSON scene saves go here
```

---

## Architecture

```
GameObject (= container of Components)
  ├── Transform                       (auto-added; parent/children/world matrix)
  ├── MeshRenderer                    (Mesh + Material; AABB-culled, instance-batched)
  ├── Camera                          (Yaw / Pitch / FOV / clip)
  ├── DirectionalLight                (with optional shadow FBO + 2048² depth tex)
  ├── PointLight                      (with attenuation)
  ├── Rigidbody + BoxCollider         (driven by BepuPhysics)
  ├── ParticleSystem                  (CPU sim; billboard quads; additive)
  └── Script (your gameplay code)     (FpsCameraController, ModelSpinner, OrbitLight, ...)

Scene drives Awake → Start → Update → LateUpdate → OnDestroy lifecycle
Renderer pulls the Camera + Lights + MeshRenderers + ParticleSystems from the scene each frame

Render pipeline (per frame):
  1. Shadow depth pre-pass     (3×3 PCF, frustum-culled to LIGHT frustum)
  2. Main lit pass             (PBR Cook-Torrance, frustum-culled, instanced batches)
  3. Particles pass            (additive billboards, depth-write off)
  4. ImGui + ImGuizmo overlay  (linear → sRGB tonemap + gamma at end of frag shader)
```

---

## Built With

| Library | Version | Purpose |
|---|---|---|
| [OpenTK](https://github.com/opentk/opentk) | 4.9.4 | Windowing, OpenGL, math primitives |
| [BepuPhysics](https://github.com/bepu/bepuphysics2) | 2.4.0 | Rigid-body physics |
| [SharpGLTF.Core](https://github.com/vpenades/SharpGLTF) | 1.0.6 | glTF / GLB loading |
| [Hexa.NET.ImGui](https://github.com/HexaEngine/Hexa.NET.ImGui) | 2.1.10 | Dear ImGui binding for the editor |
| [Hexa.NET.ImGuizmo](https://github.com/HexaEngine/Hexa.NET.ImGui) | 2.1.10 | 3D translate/rotate/scale gizmos |
| [StbImageSharp](https://github.com/StbSharp/StbImageSharp) | 2.30.15 | PNG / JPEG / etc. image decoding |

---

## Roadmap

Implemented:
- ✅ Window + GL context + game loop + FPS camera
- ✅ OBJ / MTL / GLTF / GLB asset loading
- ✅ Component system with full lifecycle
- ✅ Blinn-Phong → migrated to PBR (Cook-Torrance + metallic-roughness)
- ✅ Soft shadow mapping (directional, 3×3 PCF)
- ✅ Multi-light (1 directional + 4 point)
- ✅ Rigid-body physics (BepuPhysics)
- ✅ Resource cache (textures / shaders / model data)
- ✅ In-engine editor (Hierarchy + Inspector + Gizmos + Asset browser)
- ✅ Scene save / load (JSON state)
- ✅ Play / Stop with state snapshot
- ✅ GPU instancing + frustum culling
- ✅ CPU-simulated particle system

Not implemented (each is its own multi-week project):
- ⬜ ECS architecture (Component System covers it for now)
- ⬜ Capsule colliders / mesh colliders / joints / constraints
- ⬜ Raycast wiring (`Simulation.RayCast` is available, just not exposed)
- ⬜ Image-Based Lighting (IBL) for proper PBR ambient
- ⬜ Cascaded shadow maps
- ⬜ Point-light shadows (cubemap depth)
- ⬜ Networking / multiplayer
- ⬜ Terrain system
- ⬜ Compute shaders
- ⬜ Skeletal animation / skinning
- ⬜ Audio

---

## Known Limitations

- **PBR ambient is not real IBL** — uses a hand-tuned environment-color split (`u_envDiffuse` + `u_envSpecular = ambient * 1.5`). Metals look reasonable but don't truly reflect their environment.
- **Inspector edits to `Rigidbody.Mass` / `Collider.Size` after Awake don't propagate to the live BepuPhysics body.** Editing them only affects components added later, not bodies already registered.
- **`SceneSerializer` doesn't capture `ParticleSystem` state** — saving and reloading drops particle systems. Adding a per-type block to `SerializeComponent` / `ApplyComponent` would fix this in ~30 lines.
- **`SceneSerializer` is state-only**, not structural — Save/Load matches GameObjects by Name and overlays state onto an already-built scene. Adding a model via the asset browser then saving will not persist that new GameObject across sessions.
- **Gizmo writes world-space TRS into the local Transform** — fine for flat-hierarchy GameObjects, but parented submeshes inside loaded models would need parent-inverse math.
- **Vertex shader assumes uniform scale** — non-uniform scale will bend normals (the inverse-transpose of the model matrix is skipped as a perf optimization).
- **glTF tangent W-component (bitangent sign) is ignored** — most models work fine; some with mirrored UVs may show inverted normal mapping.

---

## Performance Notes

On a Dell G15 5530 (i5-13450HX + RTX 3050 Mobile + Intel UHD), the engine runs the demo scenes at the monitor's refresh rate. If you see lower-than-expected performance on a hybrid-GPU laptop:

1. Open **NVIDIA Control Panel** → **Manage 3D Settings** → **Program Settings**
2. Add `bin\Debug\net9.0\ArcEngineC#.exe`
3. Set **OpenGL rendering GPU** and **Power management mode** to your dedicated card

OpenGL apps default to the iGPU on hybrid laptops more often than DirectX apps do. Forcing the dGPU usually 5-10×'s perf for OpenGL rendering.

---

## Building from Source

```bash
# Restore + build
dotnet build

# Run
dotnet run --project ArcEngineC#.csproj

# Build a self-contained release for Windows x64
dotnet publish -c Release -r win-x64 --self-contained
```

The `Assets/` directory must sit alongside the executable at runtime (the `.csproj` copies it automatically with `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`).

---

## File Conventions

- **Shaders are ASCII-only.** NVIDIA's GLSL parser rejects non-ASCII characters in source, including in comments. Don't paste em-dashes or arrows into `.vert` / `.frag` files.
- **Model paths are relative to the working directory** (typically `bin/Debug/net9.0/`). The `Resources` cache normalizes paths via `Path.GetFullPath` so different relative spellings of the same file dedupe correctly.

---

## Contributing

This is primarily a personal learning project, but PRs welcome for bug fixes or completing items in the roadmap. Please match the existing code style (file-scoped namespaces, XML doc comments on public surface, expression-bodied members where they read clearly).

---

## License

[MIT](LICENSE.txt) — do anything you want with it. No warranty.

---

## Acknowledgments

- **[learnopengl.com](https://learnopengl.com/)** — reference for the PBR shader, shadow mapping, and many specific techniques
- **[BepuPhysics](https://github.com/bepu/bepuphysics2)** — the cleanest pure-C# physics engine
- **[Khronos glTF Sample Assets](https://github.com/KhronosGroup/glTF-Sample-Assets)** — the BoxTextured.glb shipped in `Assets/Models/` came from here
- **Gribb & Hartmann (2001)** — frustum plane extraction technique
