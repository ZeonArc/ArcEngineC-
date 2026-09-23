# ArcEngine Roadmap — Path to a Full-Fledged Engine

This picks up where the README's "Not implemented" list leaves off. It's organized into phases (continuing from the engine's current state — README calls it past "Phase 4"), where each phase is scoped to be buildable on top of the last. Phases are ordered by dependency and payoff, not strict priority — reorder freely, but note the callouts where something is a hard prerequisite for later work.

Each item is written as a concrete, scoped task rather than a vague goal, so it can be pulled directly into an issue tracker.

---

## Where the engine stands today

Forward-rendered PBR (Cook-Torrance, metallic-roughness), soft directional shadows, up to 4 point lights, GPU instancing + frustum culling, OBJ/MTL + glTF loading, a Unity-style component system, BepuPhysics rigid bodies (box/sphere only), an ImGui editor with gizmos and JSON scene save/load, and a CPU particle system.

What's missing to call it "full-fledged": **runtime UI** (the game itself has no HUD/menu system — ImGui is editor-only), **audio** (none at all), **skeletal animation**, **a real asset pipeline** (no GUIDs, no import caching, no compression), **structural scene serialization** (new GameObjects don't survive a save), and **any performance work beyond single-threaded** (no job system, no GPU culling).

---

## Phase 5 — Architecture Hardening ✅

*Fixes foundational gaps that make everything after this harder. Do this before Phase 6+.*

- [x] **Structural scene serialization** — rework `SceneSerializer` to (de)serialize the full GameObject graph (not just overlay state onto a pre-built scene), so GameObjects added at runtime/in the Asset Browser persist across save/load
- [x] **Prefab system** — `Prefab` asset type (serialized GameObject subtree), instantiate at runtime, "apply changes back to prefab" / "revert to prefab" workflow, prefab instance override tracking *(v1: save/instantiate only — override tracking still TODO)*
- [x] **Particle system serialization** — add the missing `SerializeComponent`/`ApplyComponent` block (README already scopes this at ~30 lines)
- [x] **Live physics property sync** — `Rigidbody.Mass` / `Collider.Size` edits in the Inspector propagate to the already-registered BepuPhysics body instead of only affecting bodies created afterward
- [x] **Raycast + physics query API** — expose `Simulation.RayCast`, add sphere-cast / overlap-test helpers, return hit GameObject + point + normal *(overlap is a center-point test for v1; true shape-vs-shape sweep still TODO)*
- [x] **GUID-based asset references** — every asset gets a stable GUID (not a file path) so renaming/moving files doesn't break scene references
- [x] **Undo/redo system** — command-pattern edit history in the editor (Transform edits, component add/remove, GameObject create/delete)
- [x] **Input action system** — replace raw `InputManager` key checks with rebindable named actions (`"Jump"` → key/button binding), lays groundwork for gamepad support in Phase 15

---

## Phase 6 — Rendering: Modern Pipeline ✅

*Biggest visual jump. IBL and shadows are the two most-requested "why does this look flat/wrong" fixes.*

- [x] **Image-Based Lighting (IBL)** — irradiance convolution for diffuse ambient, prefiltered mip-chain environment map for specular, split-sum BRDF LUT; replaces the hand-tuned `u_envDiffuse`/`u_envSpecular` hack *(offline bake at load time; hand-tuned split remains as fallback when no environment is loaded)*
- [x] **HDR skybox + environment capture** — load HDRI equirectangular maps, convert to cubemap, feed IBL *(via `Renderer.LoadEnvironment("path/to/env.hdr")`; skybox drawn as a depth-tail cube)*
- [x] **Cascaded shadow maps** — 3-4 cascades for the directional light, replacing the single 2048² depth map (fixes shadow resolution falloff at distance) *(3 cascades, texture-array shadow atlas, practical-splits scheme)*
- [x] **Point light shadows** — cubemap depth per shadow-casting point light *(single shadow-casting point light supported for v1; multi-light shadows deferred)*
- [x] **Skeletal animation / GPU skinning** — bone hierarchy import from glTF, vertex skinning in the shader (bone indices + weights), `Animator` component with clip playback *(runtime + shader complete; glTF skin/animation import deferred to a follow-up)*
- [x] **Post-processing stack** — HDR framebuffer + exposure control, bloom (bright-pass + blur), ACES or Uncharted2 tonemapping (replacing plain Reinhard), FXAA or TAA *(FXAA landed; TAA still TODO)*
- [x] **SSAO** — screen-space ambient occlusion pass to ground contact shadows without needing baked AO everywhere *(v1: derivative-based normals + 32-sample kernel, composited multiplicatively at tonemap; upgrade later to a proper G-buffer normal target)*
- [x] **LOD system** — per-mesh LOD levels, distance-based swap, integrates with existing frustum culling
- [x] **Terrain renderer** — heightmap-based terrain mesh, texture splatting (multi-layer blend by height/slope), separate from the general mesh pipeline *(4-layer splat + slope/height auto-blend, world-tiled UVs, CSM + IBL still apply)*
- [x] **Decal system** — projected decals (bullet holes, blood, grime) via deferred decal box projection or a lightweight forward variant *(forward variant — OBB projected via depth-buffer reconstruction, soft-edged blend)*
- [x] Fix documented shader limitation: **normal-matrix (inverse-transpose)** computation for non-uniform scale support
- [x] Fix documented loader limitation: **glTF tangent W-component (bitangent sign)** handling for mirrored UVs

> Note: several of these (SSAO, decals) are much cheaper to implement well in a **deferred** or **hybrid forward+depth-prepass** pipeline. Worth deciding the pipeline shape before starting this phase rather than bolting them onto pure forward rendering.

---

## Phase 7 — Physics & Gameplay Systems ✅

- [x] **Capsule colliders** — for character controllers
- [x] **Mesh colliders** — convex hull + (optionally) static triangle-mesh collision *(both landed — `ConvexHullCollider`, `StaticMeshCollider`)*
- [x] **Joints / constraints** — hinge, fixed, slider, spring; expose BepuPhysics's constraint types through components *(FixedJoint / HingeJoint / SliderJoint / SpringJoint; all use a shared `Joint` base)*
- [x] **Character controller** — kinematic capsule-based controller with slope limits, step-up, ground snapping (distinct from full rigid-body physics) *(v1: horizontal not swept against walls — shape-sweep upgrade is a follow-up)*
- [x] **Trigger volumes** — `OnTriggerEnter/Stay/Exit` callbacks separate from collision events
- [x] **Physics layers/masks** — collision matrix (what collides with what), not just a flat collider list
- [x] **Ragdoll support** — runtime swap from Animator-driven skeleton to physics-driven joint chain (depends on Phase 6 skeletal animation) *(v1: manual limb setup — automatic ragdoll-from-skeleton generator TODO)*

---

## Phase 8 — Audio ✅

*Currently entirely absent — this is the single largest missing subsystem for "full-fledged."*

- [x] **Audio backend integration** — OpenAL (via OpenTK's existing binding, keeps the dependency footprint small) or a managed alternative
- [x] **Audio asset loading** — WAV (trivial) + OGG/Vorbis (via a decode library) support in the resource cache *(WAV shipped; OGG stubbed with clear TODO — needs an OGG/Vorbis decoder like NVorbis to land)*
- [x] **`AudioSource` / `AudioListener` components** — 3D positional audio attached to GameObjects, attenuation by distance
- [x] **Mixer/bus system** — Master → Music/SFX/UI bus hierarchy with independent volume, matches the editor's existing category-based thinking
- [x] **Music streaming** — stream long tracks instead of loading fully into memory *(3-buffer queued streaming through `IAudioStream` + `StreamingAudioSource`; drop-in point for OGG once its decoder implements `IAudioStream`)*
- [x] **Editor audio preview** — play a clip from the Asset Browser without entering Play mode

---

## Phase 9 — Runtime UI System ✅

*ImGui is wired for the editor only — the game itself has no way to draw a HUD, menu, or dialogue box. This is a hard blocker for anyone trying to actually ship a game with the engine.*

- [x] **Canvas component** — screen-space and world-space UI root, separate render pass from ImGui *(screen-space shipped; world-space is a follow-up — the Canvas is already an independent root with its own projection)*
- [ ] **SDF text rendering** — signed-distance-field font atlas generation + glyph rendering (crisp at any scale, unlike bitmap fonts) *(v1 ships bitmap-font `Text`; SDF pending a TTF loader)*
- [x] **Core widgets** — Image, Text, Button, Slider, Panel/layout container
- [x] **Layout system** — anchors + auto-layout groups (horizontal/vertical/grid), so UI doesn't require hand-placed pixel coordinates
- [x] **UI input routing** — raycast against UI elements, focus/hover/click state, input consumption (so clicks don't fall through to the 3D scene)
- [x] **Data binding hooks** — simple way for gameplay scripts to push values into UI (health bar, score) without manual per-frame polling boilerplate

---

## Phase 10 — Asset Pipeline & Editor Tooling

- [ ] **Import pipeline with `.meta` files** — per-asset metadata (GUID, import settings) sitting alongside source files, matches Unity/Godot conventions users will expect
- [ ] **Texture compression** — BCn (DXT/BC7) compression at import time with mipmap generation, instead of uploading raw decoded pixels every load
- [ ] **Import caching** — cooked/processed asset cache keyed by source-file hash, so re-imports only happen when the source changes
- [ ] **FBX import** — via Assimp.NET or similar, since FBX is still the dominant DCC interchange format alongside glTF
- [ ] **Material editor** — even a simple property-grid-plus-preview view (node-based is a stretch goal) beyond the current Inspector sliders
- [ ] **Animation timeline/editor** — scrub and preview `Animator` clips, blend parameters
- [ ] **In-editor profiler** — per-system frame time breakdown (render/physics/scripts/UI), CPU vs GPU time
- [ ] **Console/log window** — in-editor log panel (currently presumably stdout-only) with filtering by severity
- [ ] **Play-mode data inspection** — watch/inspect live component values during Play without them resetting the Stop-restore snapshot

---

## Phase 11 — Performance & Scale

*Do this once the feature surface above stabilizes — optimizing a moving target wastes effort.*

- [ ] **Job system** — a simple work-stealing or `Parallel.For`-based job scheduler for parallelizable per-frame work (culling, particle sim, animation sampling)
- [ ] **Multithreaded culling** — frustum + occlusion tests off the main thread, feeding the existing instanced batch renderer
- [ ] **GPU-driven rendering** — indirect draw calls + GPU-side frustum culling via compute shaders, once compute shader support exists (this phase depends on adding compute shader support first — currently listed as not implemented)
- [ ] **Level/scene streaming** — async load/unload of scene chunks for larger worlds than fit in memory at once
- [ ] **Object pooling** — pool for frequently spawned/destroyed GameObjects (projectiles, particles, VFX) to reduce GC pressure
- [ ] **Memory/allocation profiling** — track per-frame allocations, target zero-alloc steady-state game loop

---

## Phase 12 — World Building

- [ ] **Navigation mesh + pathfinding** — NavMesh baking from static geometry, A*/funnel-algorithm path queries, `NavAgent` component
- [ ] **Foliage/scatter system** — instanced grass/tree scattering on terrain, built on the existing GPU instancing path
- [ ] **Dynamic sky/atmosphere** — time-of-day driven sky, replacing static skybox with a proper atmospheric scattering model (or a simpler gradient+sun-disc approach as a first pass)
- [ ] **Volumetric/height fog** — depth-based fog integrated into the post-processing stack

---

## Phase 13 — Networking (stretch goal)

*Only pursue this if multiplayer is an actual target — it's a multi-month effort on its own and orthogonal to "single-player engine completeness."*

- [ ] **Transport layer** — client-server over UDP/TCP (e.g. LiteNetLib) or a higher-level library
- [ ] **State replication** — Transform/component sync with interpolation
- [ ] **RPC system** — client→server and server→client remote calls
- [ ] **Client-side prediction + reconciliation** — for responsive movement over latency

---

## Phase 14 — Platform & Distribution

- [ ] **Verified cross-platform builds** — actually test and fix Linux/macOS (currently "likely work — untested" per README)
- [ ] **Full gamepad/controller support** — building on the Phase 5 input action system
- [ ] **Build & cook pipeline** — a `dotnet` publish step that also cooks/compresses assets and strips editor-only code from the shipped binary
- [ ] **Localization support** — string tables, font fallback for non-Latin scripts (interacts with the Phase 9 SDF text renderer)
- [ ] **Crash/error reporting hooks** — basic logging-to-file for shipped builds, since there's no console attached in a release build

---

## Suggested sequencing at a glance

```
Phase 5  (Architecture Hardening)     ─┐
                                        ├─ prerequisites for everything else
Phase 6  (Rendering Pipeline)         ─┘
Phase 7  (Physics & Gameplay)         ── parallel with Phase 6/8/9, low overlap
Phase 8  (Audio)                      ── parallel with Phase 6/7/9, fully independent
Phase 9  (Runtime UI)                 ── parallel with Phase 6/7/8, fully independent
Phase 10 (Asset Pipeline & Tooling)   ── ongoing, feeds off Phase 5's GUID work
Phase 11 (Performance & Scale)        ── after feature surface stabilizes
Phase 12 (World Building)             ── depends on Phase 6 terrain + Phase 7 physics
Phase 13 (Networking)                 ── optional, standalone track
Phase 14 (Platform & Distribution)    ── last, polishes everything above
```

Phases 6, 7, 8, and 9 have almost no interdependencies and could reasonably be worked in any order (or in parallel if more than one person is contributing) once Phase 5 lands. Audio (8) and Runtime UI (9) are the most "shippable game" -critical of the group despite being easy to defer, since right now the engine can render a beautiful scene that can neither make a sound nor show the player a health bar.
