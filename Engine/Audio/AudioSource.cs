using OpenTK.Audio.OpenAL;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Audio;

/// <summary>
/// A 3D-positional audio emitter attached to a GameObject. Owns one OpenAL
/// source handle; assign a <see cref="Clip"/> and call <see cref="Play"/> to
/// start playback. Position tracks the sibling Transform every frame so moving
/// GameObjects sound correctly panned/attenuated relative to the listener.
/// </summary>
public class AudioSource : Component, IDisposable
{
    public AudioClip? Clip;

    /// <summary>Per-source volume in [0..1]. Multiplied by the bus's effective volume.</summary>
    public float Volume = 1f;

    /// <summary>Playback rate; 1 = normal, &gt;1 = higher pitch, &lt;1 = lower.</summary>
    public float Pitch = 1f;

    /// <summary>Loop the clip when playback reaches its end.</summary>
    public bool Loop = false;

    /// <summary>Play from the moment the component is added to the scene.</summary>
    public bool PlayOnAwake = false;

    /// <summary>Distance beyond which volume rolls off linearly to zero.</summary>
    public float MaxDistance = 20f;

    /// <summary>Reference distance for the inverse-clamped attenuation curve.</summary>
    public float ReferenceDistance = 1f;

    /// <summary>Which mixer bus this source routes through. See <see cref="AudioMixer"/>.</summary>
    public string BusName = "SFX";

    private int _handle;
    private bool _handleAllocated;
    private bool _disposed;

    public override void Awake()
    {
        AudioEngine.EnsureInitialized();
        if (!AudioEngine.IsInitialized) return;

        _handle = AL.GenSource();
        _handleAllocated = true;

        if (PlayOnAwake && Clip != null) Play();
    }

    public override void Update(float deltaTime)
    {
        if (!_handleAllocated) return;

        // Keep the OpenAL source in sync with the Transform each frame.
        var pos = Transform.Position;
        AL.Source(_handle, ALSource3f.Position, pos.X, pos.Y, pos.Z);
        AL.Source(_handle, ALSourcef.Gain, Volume * AudioMixer.Get(BusName).EffectiveVolume);
        AL.Source(_handle, ALSourcef.Pitch, Pitch);
    }

    /// <summary>(Re)start the current clip.</summary>
    public void Play()
    {
        if (!_handleAllocated || Clip == null || Clip.Handle == 0) return;

        AL.Source(_handle, ALSourcei.Buffer, Clip.Handle);
        AL.Source(_handle, ALSourceb.Looping, Loop);
        AL.Source(_handle, ALSourcef.ReferenceDistance, ReferenceDistance);
        AL.Source(_handle, ALSourcef.MaxDistance, MaxDistance);
        // Mono clips get positional falloff; stereo clips play flat regardless of position.
        AL.SourcePlay(_handle);
        AudioEngine.CheckError("AudioSource.Play");
    }

    /// <summary>Halt playback and rewind to the start.</summary>
    public void Stop()
    {
        if (_handleAllocated) AL.SourceStop(_handle);
    }

    /// <summary>Pause without rewinding.</summary>
    public void Pause()
    {
        if (_handleAllocated) AL.SourcePause(_handle);
    }

    public bool IsPlaying
    {
        get
        {
            if (!_handleAllocated) return false;
            AL.GetSource(_handle, ALGetSourcei.SourceState, out int state);
            return state == (int)ALSourceState.Playing;
        }
    }

    public override void OnDestroy() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handleAllocated && AudioEngine.IsInitialized)
        {
            AL.SourceStop(_handle);
            AL.DeleteSource(_handle);
        }
        _handleAllocated = false;
        GC.SuppressFinalize(this);
    }
}
