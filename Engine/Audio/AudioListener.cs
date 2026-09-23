using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Audio;

/// <summary>
/// Marks the "ears" of the scene — usually attached to the same GameObject as
/// the main <see cref="Camera"/>. Each frame, pushes this GameObject's world
/// position + orientation into OpenAL so positional AudioSources fall off
/// correctly around the listener.
///
/// Only one listener should be active per scene; if multiple are present, the
/// last one to LateUpdate wins.
/// </summary>
public class AudioListener : Component
{
    /// <summary>Master listener gain (0..1). Multiplies every source's final volume.</summary>
    public float MasterGain = 1f;

    public override void Awake()
    {
        AudioEngine.EnsureInitialized();
    }

    public override void LateUpdate(float deltaTime)
    {
        if (!AudioEngine.IsInitialized) return;

        var pos = Transform.Position;
        AL.Listener(ALListener3f.Position, pos.X, pos.Y, pos.Z);

        // Derive forward/up from the Transform's rotation. If a sibling Camera exists,
        // use its Front/Up (matches what the renderer sees).
        Vector3 forward, up;
        var cam = GameObject.GetComponent<Camera>();
        if (cam != null)
        {
            forward = cam.Front;
            up = cam.Up;
        }
        else
        {
            var rotDeg = Transform.Rotation;
            var q = Quaternion.FromEulerAngles(
                MathHelper.DegreesToRadians(rotDeg.X),
                MathHelper.DegreesToRadians(rotDeg.Y),
                MathHelper.DegreesToRadians(rotDeg.Z));
            forward = Vector3.Transform(-Vector3.UnitZ, q);
            up      = Vector3.Transform( Vector3.UnitY, q);
        }

        AL.Listener(ALListenerfv.Orientation, ref forward, ref up);
        AL.Listener(ALListenerf.Gain, MasterGain);
    }
}
