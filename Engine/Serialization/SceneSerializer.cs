using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using OpenTK.Mathematics;

using ArcEngine.Engine.Audio;
using ArcEngine.Engine.Core;
using ArcEngine.Engine.Editor;
using ArcEngine.Engine.Lighting;
using ArcEngine.Engine.Loaders;
using ArcEngine.Engine.Physics;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.SandboxGame.Scripts;

namespace ArcEngine.Engine.Serialization;

/// <summary>
/// JSON save/load for the scene's structure AND mutable state. Serializes the full
/// GameObject graph (name, parent link, transform, components) into a flat array with
/// ParentIndex links; on load it clears the current scene and reconstructs every
/// GameObject and component from scratch. This means GameObjects added at runtime
/// (e.g. via the Asset Browser) survive save/load, unlike the earlier overlay-only
/// serializer.
///
/// Mesh/material reconstruction:
/// <list type="bullet">
///   <item>If a GameObject has <see cref="GameObject.SourceModelPath"/>, its entire
///         subtree is rebuilt by re-running <see cref="ModelBuilder"/> — the serialized
///         entry stores only the root Transform + top-level component overrides.</item>
///   <item>Otherwise, <see cref="MeshRenderer.MeshSource"/> is used to reconstruct
///         primitive meshes (currently: <c>primitive:plane:size=X:uv=Y</c>).</item>
///   <item>Materials are captured inline (PBR params + optional texture paths).</item>
/// </list>
///
/// Used by File → Save/Load and by the Edit/Play snapshot system.
/// </summary>
public static class SceneSerializer
{
    /// <summary>Bumped when the file format changes in a breaking way.</summary>
    private const int Version = 2;

    private static readonly JsonSerializerOptions s_writeOpts = new() { WriteIndented = true };

    // ============================================================================
    // Public API
    // ============================================================================

    public static void Save(Scene scene, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, SaveToString(scene));
    }

    public static void Load(Scene scene, string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[SceneSerializer] Scene file not found: {path}");
            return;
        }
        LoadFromString(scene, File.ReadAllText(path));
    }

    public static string SaveToString(Scene scene)
    {
        var root = new JsonObject
        {
            ["Version"] = Version,
            ["Ambient"] = SerializeVec3(scene.Ambient),
            ["GameObjects"] = SerializeGameObjects(scene),
        };
        return root.ToJsonString(s_writeOpts);
    }

    public static void LoadFromString(Scene scene, string json)
    {
        var doc = JsonNode.Parse(json) as JsonObject;
        if (doc == null) { Console.WriteLine("[SceneSerializer] Invalid JSON root."); return; }

        int version = doc["Version"] is JsonValue v ? v.GetValue<int>() : 1;
        if (version < 2)
        {
            Console.WriteLine($"[SceneSerializer] Unsupported version {version}: this file was produced by the pre-Phase-5 overlay-only serializer and cannot be structurally loaded. Rewrite it by re-saving in the current editor.");
            return;
        }

        // Fresh rebuild. Every existing GameObject is destroyed first so we don't leak
        // physics bodies / GL resources from the previous scene state.
        scene.Clear();

        if (doc["Ambient"] is JsonArray amb)
            scene.Ambient = DeserializeVec3(amb);

        if (doc["GameObjects"] is not JsonArray gos) return;

        var shader = EditorContext.SharedShader
                     ?? throw new InvalidOperationException("[SceneSerializer] SharedShader not set in EditorContext.");

        // Two-pass reconstruction:
        //   Pass 1: create every root GameObject (rebuilding model subtrees where required)
        //           and index them by their JSON position so ParentIndex links resolve.
        //   Pass 2: apply Transform + components, then re-parent by ParentIndex.
        var roots = new GameObject?[gos.Count];

        for (int i = 0; i < gos.Count; i++)
        {
            if (gos[i] is not JsonObject entry) continue;
            roots[i] = BuildGameObject(entry, shader);
        }

        // Second pass: transforms + components + parenting.
        for (int i = 0; i < gos.Count; i++)
        {
            if (gos[i] is not JsonObject entry) continue;
            var go = roots[i];
            if (go == null) continue;

            ApplyTransform(go.Transform, entry);

            if (entry["Components"] is JsonArray comps)
            {
                foreach (var c in comps)
                {
                    if (c is JsonObject co) ApplyComponent(go, co, shader);
                }
            }

            if (entry["ParentIndex"] is JsonValue pv)
            {
                int parentIndex = pv.GetValue<int>();
                if (parentIndex >= 0 && parentIndex < roots.Length)
                {
                    var parent = roots[parentIndex];
                    if (parent != null) go.Transform.SetParent(parent.Transform);
                }
            }
        }

        // Finally, drop every root into the scene. Add order = JSON order — scene
        // authors should put a PhysicsWorld-carrying GameObject first (SceneBuilders does).
        // Model-loaded subtrees already had their children scene.Add'd via AddRecursive.
        foreach (var go in roots)
        {
            if (go == null) continue;
            if (go.Transform.Parent != null) continue;   // child of another root; scene.Add(root) will pull it in
            if (go.Scene != null) continue;              // already added (model subtree root added inline)
            scene.Add(go);
        }
    }

    // ============================================================================
    // Save side
    // ============================================================================

    private static JsonArray SerializeGameObjects(Scene scene)
    {
        // Build a stable index map for parent links. Model-subtree children are NOT
        // serialized individually (we rebuild them wholesale on load), so exclude any
        // GameObject whose Transform ancestry hits a SourceModelPath root.
        var serializable = new List<GameObject>();
        var indexOf = new Dictionary<GameObject, int>();

        foreach (var go in scene.GetObjects())
        {
            if (IsInsideModelSubtree(go)) continue;
            indexOf[go] = serializable.Count;
            serializable.Add(go);
        }

        var arr = new JsonArray();
        foreach (var go in serializable)
        {
            arr.Add(SerializeGameObject(go, indexOf));
        }
        return arr;
    }

    /// <summary>
    /// True if this GameObject sits below a <see cref="GameObject.SourceModelPath"/>-rooted
    /// GameObject (i.e., it belongs to a model subtree that gets rebuilt as one unit).
    /// The root itself returns false; only descendants are hidden.
    /// </summary>
    private static bool IsInsideModelSubtree(GameObject go)
    {
        for (var t = go.Transform.Parent; t != null; t = t.Parent)
        {
            if (!string.IsNullOrEmpty(t.GameObject.SourceModelPath)) return true;
        }
        return false;
    }

    private static JsonObject SerializeGameObject(GameObject go, IReadOnlyDictionary<GameObject, int> indexOf)
    {
        var obj = new JsonObject
        {
            ["Name"]     = go.Name,
            ["Position"] = SerializeVec3(go.Transform.Position),
            ["Rotation"] = SerializeVec3(go.Transform.Rotation),
            ["Scale"]    = SerializeVec3(go.Transform.Scale),
        };

        if (!string.IsNullOrEmpty(go.SourceModelPath))
        {
            obj["SourceModelPath"] = go.SourceModelPath;
            if (File.Exists(go.SourceModelPath))
            {
                var mGuid = ArcEngine.Engine.Resources.MetaRegistry.EnsureGuid(go.SourceModelPath);
                obj["SourceModelGuid"] = mGuid.ToString();
            }
        }

        // Parent link: -1 = root. Only serialized when the parent is itself serializable.
        if (go.Transform.Parent?.GameObject is { } parentGo && indexOf.TryGetValue(parentGo, out int parentIdx))
            obj["ParentIndex"] = parentIdx;

        var comps = new JsonArray();
        foreach (var c in go.Components)
        {
            var co = SerializeComponent(c);
            if (co != null) comps.Add(co);
        }
        obj["Components"] = comps;
        return obj;
    }

    private static JsonObject? SerializeComponent(Component c)
    {
        switch (c)
        {
            // Transform is captured at the GameObject level, skip here.
            case Transform: return null;

            case MeshRenderer mr:
                return SerializeMeshRenderer(mr);

            case Camera cam:
                return new JsonObject
                {
                    ["Type"]     = "Camera",
                    ["Yaw"]      = cam.Yaw,
                    ["Pitch"]    = cam.Pitch,
                    ["Fov"]      = cam.Fov,
                    ["NearClip"] = cam.NearClip,
                    ["FarClip"]  = cam.FarClip,
                };

            case DirectionalLight dl:
                return new JsonObject
                {
                    ["Type"]          = "DirectionalLight",
                    ["Direction"]     = SerializeVec3(dl.Direction),
                    ["Color"]         = SerializeVec3(dl.Color),
                    ["Intensity"]     = dl.Intensity,
                    ["CastsShadows"]  = dl.CastsShadows,
                };

            case PointLight pl:
                return new JsonObject
                {
                    ["Type"]           = "PointLight",
                    ["Color"]          = SerializeVec3(pl.Color),
                    ["Intensity"]      = pl.Intensity,
                    ["Constant"]       = pl.Constant,
                    ["Linear"]         = pl.Linear,
                    ["Quadratic"]      = pl.Quadratic,
                    ["CastsShadows"]   = pl.CastsShadows,
                    ["ShadowFarPlane"] = pl.ShadowFarPlane,
                };

            case Rigidbody rb:
                return new JsonObject
                {
                    ["Type"]      = "Rigidbody",
                    ["Mass"]      = rb.Mass,
                    ["IsStatic"]  = rb.IsStatic,
                    ["Layer"]     = rb.Layer,
                    ["IsTrigger"] = rb.IsTrigger,
                };

            case BoxCollider bc:
                return new JsonObject
                {
                    ["Type"] = "BoxCollider",
                    ["Size"] = SerializeVec3(bc.Size),
                };

            case SphereCollider sc:
                return new JsonObject
                {
                    ["Type"]   = "SphereCollider",
                    ["Radius"] = sc.Radius,
                };

            case CapsuleCollider cc:
                return new JsonObject
                {
                    ["Type"]   = "CapsuleCollider",
                    ["Radius"] = cc.Radius,
                    ["Length"] = cc.Length,
                };

            case PhysicsWorld:
                return new JsonObject { ["Type"] = "PhysicsWorld" };

            case ParticleSystem ps:
                return new JsonObject
                {
                    ["Type"]           = "ParticleSystem",
                    ["EmissionRate"]   = ps.EmissionRate,
                    ["MaxParticles"]   = ps.MaxParticles,
                    ["StartLifetime"]  = ps.StartLifetime,
                    ["StartSize"]      = ps.StartSize,
                    ["EndSize"]        = ps.EndSize,
                    ["StartColor"]     = SerializeVec4(ps.StartColor),
                    ["EndColor"]       = SerializeVec4(ps.EndColor),
                    ["StartVelocity"]  = SerializeVec3(ps.StartVelocity),
                    ["VelocityRandom"] = SerializeVec3(ps.VelocityRandom),
                    ["Gravity"]        = SerializeVec3(ps.Gravity),
                    ["Damping"]        = ps.Damping,
                };

            case ModelSpinner ms:
                return new JsonObject
                {
                    ["Type"]              = "ModelSpinner",
                    ["RotationDegPerSec"] = SerializeVec3(ms.RotationDegPerSec),
                };

            case OrbitLight ol:
                return new JsonObject
                {
                    ["Type"]        = "OrbitLight",
                    ["Radius"]      = ol.Radius,
                    ["Speed"]       = ol.Speed,
                    ["Height"]      = ol.Height,
                    ["PhaseOffset"] = ol.PhaseOffset,
                };

            case FpsCameraController fc:
                return new JsonObject
                {
                    ["Type"]             = "FpsCameraController",
                    ["MoveSpeed"]        = fc.MoveSpeed,
                    ["MouseSensitivity"] = fc.MouseSensitivity,
                };

            case AudioSource asrc:
                return new JsonObject
                {
                    ["Type"]              = "AudioSource",
                    ["ClipPath"]          = asrc.Clip?.SourcePath,
                    ["Volume"]            = asrc.Volume,
                    ["Pitch"]             = asrc.Pitch,
                    ["Loop"]              = asrc.Loop,
                    ["PlayOnAwake"]       = asrc.PlayOnAwake,
                    ["MaxDistance"]       = asrc.MaxDistance,
                    ["ReferenceDistance"] = asrc.ReferenceDistance,
                    ["BusName"]           = asrc.BusName,
                };

            case AudioListener alis:
                return new JsonObject
                {
                    ["Type"]       = "AudioListener",
                    ["MasterGain"] = alis.MasterGain,
                };

            default:
                // Unknown / unserializable component — record its type for visibility.
                return new JsonObject { ["Type"] = c.GetType().Name + " (unsupported)" };
        }
    }

    private static JsonObject SerializeMeshRenderer(MeshRenderer mr)
    {
        var obj = new JsonObject { ["Type"] = "MeshRenderer" };
        if (!string.IsNullOrEmpty(mr.MeshSource)) obj["MeshSource"] = mr.MeshSource;
        if (mr.Material != null) obj["Material"] = SerializeMaterial(mr.Material);
        return obj;
    }

    private static JsonObject SerializeMaterial(Material m)
    {
        var obj = new JsonObject
        {
            ["Color"]            = SerializeVec3(m.Color),
            ["Metallic"]         = m.Metallic,
            ["Roughness"]        = m.Roughness,
            ["AmbientOcclusion"] = m.AmbientOcclusion,
            ["NormalStrength"]   = m.NormalStrength,
            ["Shininess"]        = m.Shininess,
        };

        AddTexturePath(obj, "BaseColorTexture", m.Texture);
        AddTexturePath(obj, "MetallicRoughnessTexture", m.MetallicRoughnessTexture);
        AddTexturePath(obj, "NormalTexture", m.NormalTexture);
        AddTexturePath(obj, "OcclusionTexture", m.OcclusionTexture);

        return obj;
    }

    private static void AddTexturePath(JsonObject obj, string key, Texture? tex)
    {
        if (tex?.SourcePath == null) return;
        obj[key] = tex.SourcePath;
        obj[key + "_sRGB"] = tex.IsSrgb;
        // GUID reference — preferred on load; path is fallback for scenes edited outside the engine.
        var guid = ArcEngine.Engine.Resources.MetaRegistry.EnsureGuid(tex.SourcePath);
        obj[key + "_guid"] = guid.ToString();
    }

    // ============================================================================
    // Load side
    // ============================================================================

    /// <summary>
    /// Pass 1 construction: create the GameObject shell. For model-rooted entries this
    /// runs the full model loader so the subtree exists before pass 2 wires the parent
    /// and applies component overrides.
    /// </summary>
    private static GameObject BuildGameObject(JsonObject entry, Shader shader)
    {
        string? name = entry["Name"]?.GetValue<string>();
        string? sourceModel = entry["SourceModelPath"]?.GetValue<string>();

        // Prefer the GUID reference if present — survives file renames.
        if (entry["SourceModelGuid"] is JsonValue gv &&
            Guid.TryParse(gv.GetValue<string>(), out var mGuid))
        {
            var resolved = ArcEngine.Engine.Resources.MetaRegistry.ResolveGuid(mGuid);
            if (resolved != null) sourceModel = resolved;
        }

        if (!string.IsNullOrEmpty(sourceModel))
        {
            try
            {
                var data = ArcEngine.Engine.Resources.Resources.LoadModelData(sourceModel);
                var go = ModelBuilder.Build(data, shader);
                if (!string.IsNullOrEmpty(name)) go.Name = name;
                go.SourceModelPath = sourceModel;
                return go;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SceneSerializer] Failed to rebuild model '{sourceModel}': {ex.Message}. Falling back to empty GameObject.");
            }
        }

        return new GameObject { Name = name ?? "GameObject" };
    }

    private static void ApplyTransform(Transform t, JsonObject obj)
    {
        if (obj["Position"] is JsonArray p) t.Position = DeserializeVec3(p);
        if (obj["Rotation"] is JsonArray r) t.Rotation = DeserializeVec3(r);
        if (obj["Scale"]    is JsonArray s) t.Scale    = DeserializeVec3(s);
    }

    private static void ApplyComponent(GameObject go, JsonObject co, Shader shader)
    {
        string? type = co["Type"]?.GetValue<string>();
        if (type == null) return;

        switch (type)
        {
            case "MeshRenderer": ApplyMeshRenderer(go, co, shader); break;

            case "Camera":
                var cam = go.GetComponent<Camera>() ?? go.AddComponent<Camera>();
                if (co["Yaw"]      is JsonValue y) cam.Yaw      = y.GetValue<float>();
                if (co["Pitch"]    is JsonValue p) cam.Pitch    = p.GetValue<float>();
                if (co["Fov"]      is JsonValue f) cam.Fov      = f.GetValue<float>();
                if (co["NearClip"] is JsonValue n) cam.NearClip = n.GetValue<float>();
                if (co["FarClip"]  is JsonValue ff) cam.FarClip = ff.GetValue<float>();
                break;

            case "DirectionalLight":
                var dl = go.GetComponent<DirectionalLight>() ?? go.AddComponent<DirectionalLight>();
                if (co["Direction"]    is JsonArray d) dl.Direction    = DeserializeVec3(d);
                if (co["Color"]        is JsonArray cc) dl.Color       = DeserializeVec3(cc);
                if (co["Intensity"]    is JsonValue i) dl.Intensity    = i.GetValue<float>();
                if (co["CastsShadows"] is JsonValue cs) dl.CastsShadows = cs.GetValue<bool>();
                break;

            case "PointLight":
                var pl = go.GetComponent<PointLight>() ?? go.AddComponent<PointLight>();
                if (co["Color"]          is JsonArray cc2) pl.Color          = DeserializeVec3(cc2);
                if (co["Intensity"]      is JsonValue i2)  pl.Intensity      = i2.GetValue<float>();
                if (co["Constant"]       is JsonValue cn)  pl.Constant       = cn.GetValue<float>();
                if (co["Linear"]         is JsonValue ln)  pl.Linear         = ln.GetValue<float>();
                if (co["Quadratic"]      is JsonValue q)   pl.Quadratic      = q.GetValue<float>();
                if (co["CastsShadows"]   is JsonValue pcs) pl.CastsShadows   = pcs.GetValue<bool>();
                if (co["ShadowFarPlane"] is JsonValue pfp) pl.ShadowFarPlane = pfp.GetValue<float>();
                break;

            case "Rigidbody":
                var rb = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
                if (co["Mass"]      is JsonValue m)   rb.Mass      = m.GetValue<float>();
                if (co["IsStatic"]  is JsonValue s2)  rb.IsStatic  = s2.GetValue<bool>();
                if (co["Layer"]     is JsonValue rbL) rb.Layer     = rbL.GetValue<int>();
                if (co["IsTrigger"] is JsonValue rbT) rb.IsTrigger = rbT.GetValue<bool>();
                break;

            case "BoxCollider":
                var bc = go.GetComponent<BoxCollider>() ?? go.AddComponent<BoxCollider>();
                if (co["Size"] is JsonArray sz) bc.Size = DeserializeVec3(sz);
                break;

            case "SphereCollider":
                var sc = go.GetComponent<SphereCollider>() ?? go.AddComponent<SphereCollider>();
                if (co["Radius"] is JsonValue rr) sc.Radius = rr.GetValue<float>();
                break;

            case "CapsuleCollider":
                var capsuleCol = go.GetComponent<CapsuleCollider>() ?? go.AddComponent<CapsuleCollider>();
                if (co["Radius"] is JsonValue capR) capsuleCol.Radius = capR.GetValue<float>();
                if (co["Length"] is JsonValue capL) capsuleCol.Length = capL.GetValue<float>();
                break;

            case "PhysicsWorld":
                if (go.GetComponent<PhysicsWorld>() == null) go.AddComponent<PhysicsWorld>();
                break;

            case "ParticleSystem":
                var ps = go.GetComponent<ParticleSystem>() ?? go.AddComponent<ParticleSystem>();
                if (co["EmissionRate"]   is JsonValue pEr) ps.EmissionRate   = pEr.GetValue<float>();
                if (co["MaxParticles"]   is JsonValue pMp) ps.MaxParticles   = pMp.GetValue<int>();
                if (co["StartLifetime"]  is JsonValue pSl) ps.StartLifetime  = pSl.GetValue<float>();
                if (co["StartSize"]      is JsonValue pSs) ps.StartSize      = pSs.GetValue<float>();
                if (co["EndSize"]        is JsonValue pEs) ps.EndSize        = pEs.GetValue<float>();
                if (co["StartColor"]     is JsonArray pSc) ps.StartColor     = DeserializeVec4(pSc);
                if (co["EndColor"]       is JsonArray pEc) ps.EndColor       = DeserializeVec4(pEc);
                if (co["StartVelocity"]  is JsonArray pSv) ps.StartVelocity  = DeserializeVec3(pSv);
                if (co["VelocityRandom"] is JsonArray pVr) ps.VelocityRandom = DeserializeVec3(pVr);
                if (co["Gravity"]        is JsonArray pG)  ps.Gravity        = DeserializeVec3(pG);
                if (co["Damping"]        is JsonValue pD)  ps.Damping        = pD.GetValue<float>();
                break;

            case "ModelSpinner":
                var ms = go.GetComponent<ModelSpinner>() ?? go.AddComponent<ModelSpinner>();
                if (co["RotationDegPerSec"] is JsonArray rps) ms.RotationDegPerSec = DeserializeVec3(rps);
                break;

            case "OrbitLight":
                var ol = go.GetComponent<OrbitLight>() ?? go.AddComponent<OrbitLight>();
                if (co["Radius"]      is JsonValue or) ol.Radius      = or.GetValue<float>();
                if (co["Speed"]       is JsonValue os) ol.Speed       = os.GetValue<float>();
                if (co["Height"]      is JsonValue oh) ol.Height      = oh.GetValue<float>();
                if (co["PhaseOffset"] is JsonValue op) ol.PhaseOffset = op.GetValue<float>();
                break;

            case "FpsCameraController":
                var fc = go.GetComponent<FpsCameraController>() ?? go.AddComponent<FpsCameraController>();
                if (co["MoveSpeed"]        is JsonValue msp) fc.MoveSpeed        = msp.GetValue<float>();
                if (co["MouseSensitivity"] is JsonValue mse) fc.MouseSensitivity = mse.GetValue<float>();
                fc.Input ??= EditorContext.Input;
                break;

            case "AudioSource":
                var asrc = go.GetComponent<AudioSource>() ?? go.AddComponent<AudioSource>();
                if (co["ClipPath"]          is JsonValue acp && acp.GetValue<string>() is { Length: > 0 } path)
                    asrc.Clip = ArcEngine.Engine.Resources.Resources.LoadAudioClip(path);
                if (co["Volume"]            is JsonValue avol) asrc.Volume            = avol.GetValue<float>();
                if (co["Pitch"]             is JsonValue apit) asrc.Pitch             = apit.GetValue<float>();
                if (co["Loop"]              is JsonValue alp)  asrc.Loop              = alp.GetValue<bool>();
                if (co["PlayOnAwake"]       is JsonValue apoa) asrc.PlayOnAwake       = apoa.GetValue<bool>();
                if (co["MaxDistance"]       is JsonValue amd)  asrc.MaxDistance       = amd.GetValue<float>();
                if (co["ReferenceDistance"] is JsonValue ard)  asrc.ReferenceDistance = ard.GetValue<float>();
                if (co["BusName"]           is JsonValue abn)  asrc.BusName           = abn.GetValue<string>();
                break;

            case "AudioListener":
                var alis = go.GetComponent<AudioListener>() ?? go.AddComponent<AudioListener>();
                if (co["MasterGain"] is JsonValue amg) alis.MasterGain = amg.GetValue<float>();
                break;

            default:
                // Silent — unknown types are tolerable forward-compat.
                break;
        }
    }

    private static void ApplyMeshRenderer(GameObject go, JsonObject co, Shader shader)
    {
        // A GameObject rebuilt from SourceModelPath already has one or more MeshRenderers
        // on its subtree; the JSON entry for the model root itself is not expected to
        // carry a MeshRenderer, but if it did we'd overwrite the top-level one. Prefer
        // "attach if missing" so model roots keep their generated MeshRenderers intact.
        var mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
        string? meshSource = co["MeshSource"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(meshSource))
        {
            mr.MeshSource = meshSource;
            mr.Mesh = ReconstructMesh(meshSource);
        }

        if (co["Material"] is JsonObject matObj)
            mr.Material = ReconstructMaterial(matObj, shader);
    }

    private static Mesh? ReconstructMesh(string source)
    {
        // Format: primitive:plane:size=X:uv=Y
        if (source.StartsWith("primitive:plane:", StringComparison.Ordinal))
        {
            float size = 1f, uv = 1f;
            foreach (var part in source.Substring("primitive:plane:".Length).Split(':'))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var k = part.Substring(0, eq);
                var vs = part.Substring(eq + 1);
                if (!float.TryParse(vs, NumberStyles.Float, CultureInfo.InvariantCulture, out var vf)) continue;
                if (k == "size") size = vf;
                else if (k == "uv") uv = vf;
            }
            return Primitives.CreatePlane(size, uv);
        }

        Console.WriteLine($"[SceneSerializer] Unknown MeshSource '{source}' — mesh will be missing.");
        return null;
    }

    private static Material ReconstructMaterial(JsonObject obj, Shader shader)
    {
        var mat = new Material(shader);
        if (obj["Color"]            is JsonArray c)  mat.Color            = DeserializeVec3(c);
        if (obj["Metallic"]         is JsonValue m)  mat.Metallic         = m.GetValue<float>();
        if (obj["Roughness"]        is JsonValue r)  mat.Roughness        = r.GetValue<float>();
        if (obj["AmbientOcclusion"] is JsonValue a)  mat.AmbientOcclusion = a.GetValue<float>();
        if (obj["NormalStrength"]   is JsonValue ns) mat.NormalStrength   = ns.GetValue<float>();
        if (obj["Shininess"]        is JsonValue sh) mat.Shininess        = sh.GetValue<float>();

        mat.Texture                  = LoadTextureSlot(obj, "BaseColorTexture");
        mat.MetallicRoughnessTexture = LoadTextureSlot(obj, "MetallicRoughnessTexture");
        mat.NormalTexture            = LoadTextureSlot(obj, "NormalTexture");
        mat.OcclusionTexture         = LoadTextureSlot(obj, "OcclusionTexture");

        return mat;
    }

    private static Texture? LoadTextureSlot(JsonObject obj, string key)
    {
        bool sRgb = obj[key + "_sRGB"] is JsonValue sv && sv.GetValue<bool>();

        // Prefer GUID lookup — survives renames and moves.
        if (obj[key + "_guid"] is JsonValue gv &&
            Guid.TryParse(gv.GetValue<string>(), out var guid))
        {
            var byGuid = ArcEngine.Engine.Resources.Resources.LoadTextureByGuid(guid, sRgb);
            if (byGuid != null) return byGuid;
            Console.WriteLine($"[SceneSerializer] Texture GUID {guid} for slot '{key}' didn't resolve; falling back to path.");
        }

        if (obj[key] is JsonValue pv)
        {
            string? path = pv.GetValue<string>();
            if (!string.IsNullOrEmpty(path))
                return ArcEngine.Engine.Resources.Resources.LoadTexture(path, sRgb);
        }
        return null;
    }

    // ============================================================================
    // Vec helpers
    // ============================================================================

    private static JsonArray SerializeVec3(Vector3 v) => new() { v.X, v.Y, v.Z };
    private static JsonArray SerializeVec4(Vector4 v) => new() { v.X, v.Y, v.Z, v.W };

    private static Vector3 DeserializeVec3(JsonArray a)
    {
        float x = a.Count > 0 ? a[0]!.GetValue<float>() : 0f;
        float y = a.Count > 1 ? a[1]!.GetValue<float>() : 0f;
        float z = a.Count > 2 ? a[2]!.GetValue<float>() : 0f;
        return new Vector3(x, y, z);
    }

    private static Vector4 DeserializeVec4(JsonArray a)
    {
        float x = a.Count > 0 ? a[0]!.GetValue<float>() : 0f;
        float y = a.Count > 1 ? a[1]!.GetValue<float>() : 0f;
        float z = a.Count > 2 ? a[2]!.GetValue<float>() : 0f;
        float w = a.Count > 3 ? a[3]!.GetValue<float>() : 0f;
        return new Vector4(x, y, z, w);
    }
}
