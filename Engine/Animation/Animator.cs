using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Animation;

/// <summary>
/// Component that drives a <see cref="Skeleton"/> from an <see cref="AnimationClip"/>.
/// Advances an internal time each frame, samples the current clip into the
/// skeleton's local pose, then rebuilds the skinning palette.
///
/// The renderer picks up the ready palette from <see cref="Skeleton.Palette"/>
/// and uploads it to <c>skinned.vert</c>.
/// </summary>
public class Animator : Component
{
    public Skeleton? Skeleton;
    public AnimationClip? Clip;

    /// <summary>Playback speed multiplier. 1 = real-time; 0 pauses.</summary>
    public float Speed = 1f;

    /// <summary>Current playback time in seconds. Set to jump to a frame.</summary>
    public float Time = 0f;

    public override void Update(float deltaTime)
    {
        if (Skeleton == null) return;

        if (Clip != null)
        {
            Time += deltaTime * Speed;
            Clip.Sample(Time, Skeleton);
        }

        Skeleton.ComputePalette();
    }
}
