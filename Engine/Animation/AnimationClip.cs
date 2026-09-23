using OpenTK.Mathematics;

namespace ArcEngine.Engine.Animation;

/// <summary>
/// One typed animation track — an ordered list of (time, value) samples for a
/// specific bone/channel. Times are monotonic and start at 0. Sampling wraps
/// on out-of-range access so a clip in <see cref="AnimationClip.WrapMode.Loop"/>
/// mode just re-hits the first frame at t=Duration.
/// </summary>
public class KeyframeTrack<T> where T : struct
{
    public readonly float[] Times;
    public readonly T[] Values;
    /// <summary>Index into the target <see cref="Skeleton.Bones"/>.</summary>
    public readonly int BoneIndex;

    public KeyframeTrack(int boneIndex, float[] times, T[] values)
    {
        if (times.Length != values.Length)
            throw new ArgumentException("Times / values length mismatch");
        BoneIndex = boneIndex;
        Times = times;
        Values = values;
    }

    /// <summary>
    /// Find the pair of surrounding keyframes for <paramref name="t"/> plus the
    /// interpolation factor between them. Callers use this to do linear or
    /// spherical interpolation on whatever value type the track holds.
    /// </summary>
    public (int lo, int hi, float alpha) FindFrame(float t)
    {
        if (Times.Length == 0) return (0, 0, 0f);
        if (t <= Times[0]) return (0, 0, 0f);
        if (t >= Times[^1]) return (Times.Length - 1, Times.Length - 1, 0f);

        // Small tracks; linear scan is fine — swap for binary search past ~32 keys.
        for (int i = 1; i < Times.Length; i++)
        {
            if (Times[i] >= t)
            {
                float span = Times[i] - Times[i - 1];
                float alpha = span > 0f ? (t - Times[i - 1]) / span : 0f;
                return (i - 1, i, alpha);
            }
        }
        return (Times.Length - 1, Times.Length - 1, 0f);
    }
}

/// <summary>
/// A named clip — a bundle of position/rotation/scale tracks, each keyed to a
/// specific bone. Sampling writes into a <see cref="Skeleton"/>'s current local
/// pose fields; call <see cref="Skeleton.ComputePalette"/> afterwards to build
/// the skinning palette.
/// </summary>
public class AnimationClip
{
    public enum WrapMode { Loop, Once, ClampToLast }

    public string Name;
    public float Duration;
    public WrapMode Wrap;

    public readonly List<KeyframeTrack<Vector3>>    PositionTracks = new();
    public readonly List<KeyframeTrack<Quaternion>> RotationTracks = new();
    public readonly List<KeyframeTrack<Vector3>>    ScaleTracks    = new();

    public AnimationClip(string name, float duration, WrapMode wrap = WrapMode.Loop)
    {
        Name = name;
        Duration = duration;
        Wrap = wrap;
    }

    /// <summary>
    /// Sample every track at <paramref name="time"/> and write results into the
    /// skeleton's bones' local pose fields. Bones without a track for a given
    /// channel keep their current value.
    /// </summary>
    public void Sample(float time, Skeleton skeleton)
    {
        float t = WrapTime(time);

        foreach (var track in PositionTracks)
        {
            var (lo, hi, alpha) = track.FindFrame(t);
            skeleton.Bones[track.BoneIndex].LocalPosition =
                Vector3.Lerp(track.Values[lo], track.Values[hi], alpha);
        }
        foreach (var track in RotationTracks)
        {
            var (lo, hi, alpha) = track.FindFrame(t);
            skeleton.Bones[track.BoneIndex].LocalRotation =
                Quaternion.Slerp(track.Values[lo], track.Values[hi], alpha);
        }
        foreach (var track in ScaleTracks)
        {
            var (lo, hi, alpha) = track.FindFrame(t);
            skeleton.Bones[track.BoneIndex].LocalScale =
                Vector3.Lerp(track.Values[lo], track.Values[hi], alpha);
        }
    }

    private float WrapTime(float time)
    {
        if (Duration <= 0f) return 0f;
        switch (Wrap)
        {
            case WrapMode.Loop:
                float wrapped = time % Duration;
                return wrapped < 0f ? wrapped + Duration : wrapped;
            case WrapMode.Once:
            case WrapMode.ClampToLast:
                return MathHelper.Clamp(time, 0f, Duration);
            default:
                return time;
        }
    }
}
