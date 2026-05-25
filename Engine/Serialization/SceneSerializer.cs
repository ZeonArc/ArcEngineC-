using System.Text.Json;
using System.Text.Json.Nodes;

using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Lighting;
using ArcEngine.Engine.Physics;
using ArcEngine.Engine.Rendering;
using ArcEngine.Engine.SandboxGame.Scripts;

namespace ArcEngine.Engine.Serialization;

/// <summary>
/// JSON save/load for the scene's mutable STATE — Transform values + a hand-coded
/// per-component-type field set. Does <i>not</i> rebuild structure: assumes the same
/// set of GameObjects (matched by Name) already exists in the target scene. The
/// sandbox demo is the canonical scene; calling <see cref="Save"/> + <see cref="Load"/>
/// on it round-trips Position/Rotation/Scale, light colours, material colours, etc.
///
/// Used by File → Save/Load and by the Edit/Play snapshot system.
/// </summary>
public static class SceneSerializer
{
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
            ["Ambient"] = SerializeVec3(scene.Ambient),
            ["GameObjects"] = SerializeGameObjects(scene),
        };
        return root.ToJsonString(s_writeOpts);
    }

    public static void LoadFromString(Scene scene, string json)
    {
        var doc = JsonNode.Parse(json) as JsonObject;
        if (doc == null) { Console.WriteLine("[SceneSerializer] Invalid JSON root."); return; }

        if (doc["Ambient"] is JsonArray amb)
            scene.Ambient = DeserializeVec3(amb);

        // Match GameObjects by Name; index existing scene objects.
        var byName = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        foreach (var go in scene.GetObjects())
        {
            // Only the FIRST GameObject with a given name wins on duplicates.
            byName.TryAdd(go.Name, go);
        }

        if (doc["GameObjects"] is not JsonArray gos) return;
        foreach (var node in gos)
        {
            if (node is not JsonObject obj) continue;
            string? name = obj["Name"]?.GetValue<string>();
            if (name == null || !byName.TryGetValue(name, out var go)) continue;

            ApplyTransform(go.Transform, obj);

            if (obj["Components"] is JsonArray comps)
            {
                foreach (var c in comps)
                {
                    if (c is JsonObject co) ApplyComponent(go, co);
                }
            }
        }
    }

    // ============================================================================
    // Save side
    // ============================================================================

    private static JsonArray SerializeGameObjects(Scene scene)
    {
        var arr = new JsonArray();
        foreach (var go in scene.GetObjects())
        {
            arr.Add(SerializeGameObject(go));
        }
        return arr;
    }

    private static JsonObject SerializeGameObject(GameObject go)
    {
        var obj = new JsonObject
        {
            ["Name"]     = go.Name,
            ["Position"] = SerializeVec3(go.Transform.Position),
            ["Rotation"] = SerializeVec3(go.Transform.Rotation),
            ["Scale"]    = SerializeVec3(go.Transform.Scale),
        };

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
                var mrObj = new JsonObject { ["Type"] = "MeshRenderer" };
                if (mr.Material != null)
                {
                    mrObj["MaterialColor"] = SerializeVec3(mr.Material.Color);
                    mrObj["Shininess"]     = mr.Material.Shininess;
                }
                return mrObj;

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
                    ["Type"]      = "PointLight",
                    ["Color"]     = SerializeVec3(pl.Color),
                    ["Intensity"] = pl.Intensity,
                    ["Constant"]  = pl.Constant,
                    ["Linear"]    = pl.Linear,
                    ["Quadratic"] = pl.Quadratic,
                };

            case Rigidbody rb:
                return new JsonObject
                {
                    ["Type"]     = "Rigidbody",
                    ["Mass"]     = rb.Mass,
                    ["IsStatic"] = rb.IsStatic,
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

            default:
                // Unknown / unserializable component — record its type for visibility.
                return new JsonObject { ["Type"] = c.GetType().Name + " (unsupported)" };
        }
    }

    // ============================================================================
    // Load side
    // ============================================================================

    private static void ApplyTransform(Transform t, JsonObject obj)
    {
        if (obj["Position"] is JsonArray p) t.Position = DeserializeVec3(p);
        if (obj["Rotation"] is JsonArray r) t.Rotation = DeserializeVec3(r);
        if (obj["Scale"]    is JsonArray s) t.Scale    = DeserializeVec3(s);
    }

    private static void ApplyComponent(GameObject go, JsonObject co)
    {
        string? type = co["Type"]?.GetValue<string>();
        if (type == null) return;

        switch (type)
        {
            case "MeshRenderer":
                var mr = go.GetComponent<MeshRenderer>();
                if (mr?.Material != null)
                {
                    if (co["MaterialColor"] is JsonArray mc) mr.Material.Color = DeserializeVec3(mc);
                    if (co["Shininess"] is JsonValue sh)     mr.Material.Shininess = sh.GetValue<float>();
                }
                break;

            case "Camera":
                var cam = go.GetComponent<Camera>();
                if (cam == null) break;
                if (co["Yaw"]      is JsonValue y) cam.Yaw      = y.GetValue<float>();
                if (co["Pitch"]    is JsonValue p) cam.Pitch    = p.GetValue<float>();
                if (co["Fov"]      is JsonValue f) cam.Fov      = f.GetValue<float>();
                if (co["NearClip"] is JsonValue n) cam.NearClip = n.GetValue<float>();
                if (co["FarClip"]  is JsonValue ff) cam.FarClip = ff.GetValue<float>();
                break;

            case "DirectionalLight":
                var dl = go.GetComponent<DirectionalLight>();
                if (dl == null) break;
                if (co["Direction"]    is JsonArray d) dl.Direction    = DeserializeVec3(d);
                if (co["Color"]        is JsonArray cc) dl.Color       = DeserializeVec3(cc);
                if (co["Intensity"]    is JsonValue i) dl.Intensity    = i.GetValue<float>();
                if (co["CastsShadows"] is JsonValue cs) dl.CastsShadows = cs.GetValue<bool>();
                break;

            case "PointLight":
                var pl = go.GetComponent<PointLight>();
                if (pl == null) break;
                if (co["Color"]     is JsonArray cc2) pl.Color     = DeserializeVec3(cc2);
                if (co["Intensity"] is JsonValue i2)  pl.Intensity = i2.GetValue<float>();
                if (co["Constant"]  is JsonValue cn)  pl.Constant  = cn.GetValue<float>();
                if (co["Linear"]    is JsonValue ln)  pl.Linear    = ln.GetValue<float>();
                if (co["Quadratic"] is JsonValue q)   pl.Quadratic = q.GetValue<float>();
                break;

            case "Rigidbody":
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) break;
                if (co["Mass"]     is JsonValue m)  rb.Mass     = m.GetValue<float>();
                if (co["IsStatic"] is JsonValue s2) rb.IsStatic = s2.GetValue<bool>();
                break;

            case "BoxCollider":
                var bc = go.GetComponent<BoxCollider>();
                if (bc != null && co["Size"] is JsonArray sz)
                    bc.Size = DeserializeVec3(sz);
                break;

            case "SphereCollider":
                var sc = go.GetComponent<SphereCollider>();
                if (sc != null && co["Radius"] is JsonValue rr)
                    sc.Radius = rr.GetValue<float>();
                break;

            case "ModelSpinner":
                var ms = go.GetComponent<ModelSpinner>();
                if (ms != null && co["RotationDegPerSec"] is JsonArray rps)
                    ms.RotationDegPerSec = DeserializeVec3(rps);
                break;

            case "OrbitLight":
                var ol = go.GetComponent<OrbitLight>();
                if (ol == null) break;
                if (co["Radius"]      is JsonValue or) ol.Radius      = or.GetValue<float>();
                if (co["Speed"]       is JsonValue os) ol.Speed       = os.GetValue<float>();
                if (co["Height"]      is JsonValue oh) ol.Height      = oh.GetValue<float>();
                if (co["PhaseOffset"] is JsonValue op) ol.PhaseOffset = op.GetValue<float>();
                break;

            case "FpsCameraController":
                var fc = go.GetComponent<FpsCameraController>();
                if (fc == null) break;
                if (co["MoveSpeed"]        is JsonValue msp) fc.MoveSpeed        = msp.GetValue<float>();
                if (co["MouseSensitivity"] is JsonValue mse) fc.MouseSensitivity = mse.GetValue<float>();
                break;

            default:
                // Silent — unknown types are tolerable forward-compat.
                break;
        }
    }

    // ============================================================================
    // Vec3 helpers
    // ============================================================================

    private static JsonArray SerializeVec3(Vector3 v) => new() { v.X, v.Y, v.Z };

    private static Vector3 DeserializeVec3(JsonArray a)
    {
        float x = a.Count > 0 ? a[0]!.GetValue<float>() : 0f;
        float y = a.Count > 1 ? a[1]!.GetValue<float>() : 0f;
        float z = a.Count > 2 ? a[2]!.GetValue<float>() : 0f;
        return new Vector3(x, y, z);
    }
}
