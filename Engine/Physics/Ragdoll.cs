using OpenTK.Mathematics;

using ArcEngine.Engine.Animation;
using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// One entry in a <see cref="Ragdoll"/> mapping — pairs a skeleton bone index
/// with the GameObject that carries the matching physics body / collider /
/// (optionally) joint components.
/// </summary>
public struct RagdollBone
{
    /// <summary>Index into the target <see cref="Skeleton.Bones"/>.</summary>
    public int BoneIndex;

    /// <summary>Scene GameObject holding a <see cref="Rigidbody"/> + <see cref="Collider"/>.</summary>
    public GameObject Body;

    public RagdollBone(int boneIndex, GameObject body)
    {
        BoneIndex = boneIndex;
        Body = body;
    }
}

/// <summary>
/// Bridges a skinned skeleton to a network of scene rigidbodies so a character
/// can transition between animator-driven and physics-driven motion.
///
/// Two modes, controlled by <see cref="IsActive"/>:
/// <list type="bullet">
///   <item><b>Inactive</b> — the sibling <see cref="Animator"/> drives the skeleton.
///         This component pushes each bone's world pose out to its rigidbody every
///         frame via <see cref="Rigidbody.SyncToTransform"/>, keeping physics
///         representation aligned to the animation.</item>
///   <item><b>Active</b> — physics drives the bones. Each frame, we read each
///         rigidbody's world pose and convert it back to the skeleton bone's local
///         pose (inverse of the parent chain).</item>
/// </list>
///
/// Setup (v1 is manual): the user places one rigidbody GameObject per limb in the
/// scene, connects adjacent limbs with joints (<see cref="HingeJoint"/>, etc.),
/// then populates <see cref="Bones"/> with (boneIndex → limbGameObject) pairs.
/// </summary>
public class Ragdoll : Component
{
    public Skeleton? Skeleton;
    public List<RagdollBone> Bones = new();

    /// <summary>Toggle physics-driven mode. See class docs for what each state does.</summary>
    public bool IsActive = false;

    public override void LateUpdate(float deltaTime)
    {
        if (Skeleton == null) return;

        if (IsActive)
        {
            // Physics → skeleton. Convert each rigidbody's world matrix into the
            // bone's local space by multiplying by the parent bone's inverse
            // world matrix. Bones must be in parent-before-child order (enforced
            // by Skeleton ctor validation).
            var worldMatrices = new Matrix4[Skeleton.Bones.Length];
            for (int i = 0; i < Skeleton.Bones.Length; i++) worldMatrices[i] = Matrix4.Identity;

            foreach (var rb in Bones)
            {
                if (rb.Body == null || (uint)rb.BoneIndex >= (uint)Skeleton.Bones.Length) continue;
                worldMatrices[rb.BoneIndex] = rb.Body.Transform.GetWorldModelMatrix();
            }

            for (int i = 0; i < Skeleton.Bones.Length; i++)
            {
                Matrix4 local = worldMatrices[i];
                int parent = Skeleton.Bones[i].ParentIndex;
                if (parent >= 0)
                {
                    var parentInv = Matrix4.Invert(worldMatrices[parent]);
                    local = local * parentInv;
                }
                DecomposeTRS(local, out var pos, out var rot, out var scale);
                Skeleton.Bones[i].LocalPosition = pos;
                Skeleton.Bones[i].LocalRotation = rot;
                Skeleton.Bones[i].LocalScale    = scale;
            }
        }
        else
        {
            // Skeleton → physics. Push each mapped bone's current world pose
            // into its rigidbody so the physics representation stays aligned
            // with the animation.
            Skeleton.ComputePalette();   // ensure world matrices are current
            foreach (var rb in Bones)
            {
                if (rb.Body == null || (uint)rb.BoneIndex >= (uint)Skeleton.Bones.Length) continue;

                // Placeholder: the Skeleton class only surfaces the palette
                // (world * inverseBind); direct world matrices aren't exposed.
                // Recompute bone world matrix here for the sync.
                Matrix4 world = ComputeBoneWorld(Skeleton, rb.BoneIndex);

                DecomposeTRS(world, out var pos, out var rot, out _);
                rb.Body.Transform.Position = pos;
                rb.Body.Transform.Rotation = QuatToEulerDegrees(rot);
                rb.Body.GetComponent<Rigidbody>()?.SyncToTransform();
            }
        }
    }

    private static Matrix4 ComputeBoneWorld(Skeleton skeleton, int boneIndex)
    {
        var chain = new Stack<int>();
        for (int i = boneIndex; i >= 0; i = skeleton.Bones[i].ParentIndex) chain.Push(i);
        var world = Matrix4.Identity;
        foreach (var i in chain)
        {
            ref var b = ref skeleton.Bones[i];
            world = Matrix4.CreateScale(b.LocalScale)
                  * Matrix4.CreateFromQuaternion(b.LocalRotation)
                  * Matrix4.CreateTranslation(b.LocalPosition)
                  * world;
        }
        return world;
    }

    private static void DecomposeTRS(Matrix4 m, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        position = m.ExtractTranslation();
        scale = m.ExtractScale();
        rotation = m.ExtractRotation();
    }

    private static Vector3 QuatToEulerDegrees(Quaternion q)
    {
        // Roll/pitch/yaw (XYZ) matching the engine's Euler-degree convention.
        float sinr = 2f * (q.W * q.X + q.Y * q.Z);
        float cosr = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float roll = MathF.Atan2(sinr, cosr);

        float sinp = 2f * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);

        float siny = 2f * (q.W * q.Z + q.X * q.Y);
        float cosy = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float yaw = MathF.Atan2(siny, cosy);

        return new Vector3(
            MathHelper.RadiansToDegrees(roll),
            MathHelper.RadiansToDegrees(pitch),
            MathHelper.RadiansToDegrees(yaw));
    }
}
