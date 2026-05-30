using Hexa.NET.ImGui;

using OpenTK.Mathematics;

using ArcEngine.Engine.Core;
using ArcEngine.Engine.Lighting;
using ArcEngine.Engine.Physics;
using ArcEngine.Engine.Rendering;

using SnVec3 = System.Numerics.Vector3;
using SnVec4 = System.Numerics.Vector4;

namespace ArcEngine.Engine.Editor;

/// <summary>
/// Per-component editor renderers, dispatched by runtime type. Keeps the editor's
/// concerns out of the engine runtime: components themselves don't know about ImGui.
/// </summary>
public static class ComponentInspector
{
    public static void Draw(Component c)
    {
        switch (c)
        {
            case Transform t:           DrawTransform(t); break;
            case MeshRenderer mr:       DrawMeshRenderer(mr); break;
            case Camera cam:            DrawCamera(cam); break;
            case DirectionalLight dl:   DrawDirectionalLight(dl); break;
            case PointLight pl:         DrawPointLight(pl); break;
            case Rigidbody rb:          DrawRigidbody(rb); break;
            case BoxCollider bc:        DrawBoxCollider(bc); break;
            case SphereCollider sc:     DrawSphereCollider(sc); break;
            case ParticleSystem psys:   DrawParticleSystem(psys); break;
            case Script:                ImGui.TextDisabled("(custom script — no fields exposed yet)"); break;
            default:                    ImGui.TextDisabled("(no editor for this component)"); break;
        }
    }

    // ------------------------------------------------------------------------
    // Transform
    // ------------------------------------------------------------------------

    private static void DrawTransform(Transform t)
    {
        var pos = ToSn(t.Position);
        if (ImGui.DragFloat3("Position", ref pos, 0.05f))
            t.Position = ToOtk(pos);

        var rot = ToSn(t.Rotation);
        if (ImGui.DragFloat3("Rotation", ref rot, 1.0f))
            t.Rotation = ToOtk(rot);

        var scl = ToSn(t.Scale);
        if (ImGui.DragFloat3("Scale", ref scl, 0.05f, 0.001f, 1000f))
            t.Scale = ToOtk(scl);
    }

    // ------------------------------------------------------------------------
    // MeshRenderer / Material
    // ------------------------------------------------------------------------

    private static void DrawMeshRenderer(MeshRenderer mr)
    {
        ImGui.TextDisabled($"Mesh: {(mr.Mesh != null ? "<set>" : "<null>")}");
        ImGui.TextDisabled($"Material: {(mr.Material != null ? "<set>" : "<null>")}");

        if (mr.Material != null)
        {
            ImGui.Separator();
            ImGui.Text("Material (PBR)");

            var col = ToSn(mr.Material.Color);
            if (ImGui.ColorEdit3("Base Color", ref col))
                mr.Material.Color = ToOtk(col);

            float metallic = mr.Material.Metallic;
            if (ImGui.SliderFloat("Metallic", ref metallic, 0f, 1f))
                mr.Material.Metallic = metallic;

            float roughness = mr.Material.Roughness;
            if (ImGui.SliderFloat("Roughness", ref roughness, 0.04f, 1f))
                mr.Material.Roughness = roughness;

            float ao = mr.Material.AmbientOcclusion;
            if (ImGui.SliderFloat("AO", ref ao, 0f, 1f))
                mr.Material.AmbientOcclusion = ao;

            float ns = mr.Material.NormalStrength;
            if (ImGui.SliderFloat("Normal Strength", ref ns, 0f, 2f))
                mr.Material.NormalStrength = ns;

            ImGui.TextDisabled($"BaseColor map: {(mr.Material.Texture != null ? "<set>" : "<none>")}");
            ImGui.TextDisabled($"MR map:        {(mr.Material.MetallicRoughnessTexture != null ? "<set>" : "<none>")}");
            ImGui.TextDisabled($"Normal map:    {(mr.Material.NormalTexture != null ? "<set>" : "<none>")}");
            ImGui.TextDisabled($"Occlusion map: {(mr.Material.OcclusionTexture != null ? "<set>" : "<none>")}");
        }
    }

    // ------------------------------------------------------------------------
    // Camera
    // ------------------------------------------------------------------------

    private static void DrawCamera(Camera cam)
    {
        ImGui.BeginDisabled();
        float yaw = cam.Yaw;
        ImGui.DragFloat("Yaw", ref yaw);
        float pitch = cam.Pitch;
        ImGui.DragFloat("Pitch", ref pitch);
        ImGui.EndDisabled();
        ImGui.TextDisabled("(yaw/pitch driven by FpsCameraController)");

        float fov = cam.Fov;
        if (ImGui.DragFloat("FOV", ref fov, 0.5f, 10f, 120f))
            cam.Fov = fov;

        float nearZ = cam.NearClip;
        if (ImGui.DragFloat("Near", ref nearZ, 0.01f, 0.001f, cam.FarClip - 0.01f))
            cam.NearClip = nearZ;

        float farZ = cam.FarClip;
        if (ImGui.DragFloat("Far", ref farZ, 1f, cam.NearClip + 0.01f, 10000f))
            cam.FarClip = farZ;
    }

    // ------------------------------------------------------------------------
    // Lights
    // ------------------------------------------------------------------------

    private static void DrawDirectionalLight(DirectionalLight dl)
    {
        var dir = ToSn(dl.Direction);
        if (ImGui.DragFloat3("Direction", ref dir, 0.01f, -1f, 1f))
        {
            // Renormalize so any tiny drag still produces a unit vector.
            var v = ToOtk(dir);
            if (v.LengthSquared > 1e-6f) dl.Direction = Vector3.Normalize(v);
        }

        var color = ToSn(dl.Color);
        if (ImGui.ColorEdit3("Color", ref color))
            dl.Color = ToOtk(color);

        float intensity = dl.Intensity;
        if (ImGui.DragFloat("Intensity", ref intensity, 0.05f, 0f, 10f))
            dl.Intensity = intensity;

        bool castsShadows = dl.CastsShadows;
        if (ImGui.Checkbox("Casts Shadows", ref castsShadows))
            dl.CastsShadows = castsShadows;
    }

    private static void DrawPointLight(PointLight pl)
    {
        ImGui.TextDisabled("(position is driven by Transform)");

        var color = ToSn(pl.Color);
        if (ImGui.ColorEdit3("Color", ref color))
            pl.Color = ToOtk(color);

        float intensity = pl.Intensity;
        if (ImGui.DragFloat("Intensity", ref intensity, 0.05f, 0f, 10f))
            pl.Intensity = intensity;

        float c = pl.Constant;
        if (ImGui.DragFloat("Constant",  ref c, 0.01f, 0f, 10f)) pl.Constant = c;
        float lin = pl.Linear;
        if (ImGui.DragFloat("Linear",    ref lin, 0.01f, 0f, 10f)) pl.Linear = lin;
        float quad = pl.Quadratic;
        if (ImGui.DragFloat("Quadratic", ref quad, 0.001f, 0f, 10f)) pl.Quadratic = quad;
    }

    // ------------------------------------------------------------------------
    // Physics
    // ------------------------------------------------------------------------

    private static void DrawRigidbody(Rigidbody rb)
    {
        float mass = rb.Mass;
        if (ImGui.DragFloat("Mass", ref mass, 0.1f, 0.001f, 10000f))
            rb.Mass = mass;

        bool isStatic = rb.IsStatic;
        if (ImGui.Checkbox("Static", ref isStatic))
            rb.IsStatic = isStatic;

        ImGui.TextDisabled("(changes after Awake won't update the live body)");
    }

    private static void DrawBoxCollider(BoxCollider bc)
    {
        var size = ToSn(bc.Size);
        if (ImGui.DragFloat3("Size", ref size, 0.05f, 0.001f, 1000f))
            bc.Size = ToOtk(size);
        ImGui.TextDisabled("(changes after Awake won't update the live body)");
    }

    private static void DrawSphereCollider(SphereCollider sc)
    {
        float r = sc.Radius;
        if (ImGui.DragFloat("Radius", ref r, 0.05f, 0.001f, 1000f))
            sc.Radius = r;
        ImGui.TextDisabled("(changes after Awake won't update the live body)");
    }

    // ------------------------------------------------------------------------
    // ParticleSystem
    // ------------------------------------------------------------------------

    private static void DrawParticleSystem(ParticleSystem ps)
    {
        ImGui.TextDisabled($"Alive: {ps.AliveCount} / {ps.MaxParticles}");
        ImGui.Separator();

        float emit = ps.EmissionRate;
        if (ImGui.DragFloat("Emission Rate", ref emit, 1f, 0f, 5000f))
            ps.EmissionRate = emit;

        int max = ps.MaxParticles;
        if (ImGui.DragInt("Max Particles", ref max, 16f, 1, 16384))
            ps.MaxParticles = max;

        float lt = ps.StartLifetime;
        if (ImGui.DragFloat("Lifetime", ref lt, 0.05f, 0.05f, 30f))
            ps.StartLifetime = lt;

        ImGui.Separator();

        float startSize = ps.StartSize;
        if (ImGui.DragFloat("Start Size", ref startSize, 0.01f, 0.001f, 10f))
            ps.StartSize = startSize;

        float endSize = ps.EndSize;
        if (ImGui.DragFloat("End Size", ref endSize, 0.01f, 0.001f, 10f))
            ps.EndSize = endSize;

        var startCol = new SnVec4(ps.StartColor.X, ps.StartColor.Y, ps.StartColor.Z, ps.StartColor.W);
        if (ImGui.ColorEdit4("Start Color", ref startCol))
            ps.StartColor = new Vector4(startCol.X, startCol.Y, startCol.Z, startCol.W);

        var endCol = new SnVec4(ps.EndColor.X, ps.EndColor.Y, ps.EndColor.Z, ps.EndColor.W);
        if (ImGui.ColorEdit4("End Color", ref endCol))
            ps.EndColor = new Vector4(endCol.X, endCol.Y, endCol.Z, endCol.W);

        ImGui.Separator();

        var startVel = ToSn(ps.StartVelocity);
        if (ImGui.DragFloat3("Start Velocity", ref startVel, 0.05f))
            ps.StartVelocity = ToOtk(startVel);

        var velRand = ToSn(ps.VelocityRandom);
        if (ImGui.DragFloat3("Velocity Random", ref velRand, 0.05f, 0f, 100f))
            ps.VelocityRandom = ToOtk(velRand);

        var grav = ToSn(ps.Gravity);
        if (ImGui.DragFloat3("Gravity", ref grav, 0.05f))
            ps.Gravity = ToOtk(grav);

        float damp = ps.Damping;
        if (ImGui.SliderFloat("Damping", ref damp, 0f, 5f))
            ps.Damping = damp;
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private static SnVec3 ToSn(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 ToOtk(SnVec3 v) => new(v.X, v.Y, v.Z);
}
