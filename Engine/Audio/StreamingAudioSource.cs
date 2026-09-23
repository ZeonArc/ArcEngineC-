using OpenTK.Audio.OpenAL;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Audio;

/// <summary>
/// Long-form audio source. Instead of uploading a full <see cref="AudioClip"/>,
/// this component streams raw PCM from an <see cref="IAudioStream"/> through a
/// small ring of OpenAL buffers (default 3), refilling each buffer as it drains.
/// Suitable for music tracks and other multi-minute audio that shouldn't sit
/// entirely in memory.
///
/// v1 supports <see cref="WavStream"/> only. OGG/MP3 will just implement
/// <see cref="IAudioStream"/> and drop in unchanged.
/// </summary>
public class StreamingAudioSource : Component, IDisposable
{
    /// <summary>Path to a WAV file. Set before <see cref="Awake"/>.</summary>
    public string? StreamPath;

    public float Volume = 1f;
    public bool  Loop   = true;
    public bool  PlayOnAwake = true;
    public string BusName = "Music";

    /// <summary>Number of queued buffers cycled by the stream. 3 is the standard "no gap" count.</summary>
    public int BufferCount = 3;

    /// <summary>Bytes per buffer. Larger = fewer refills, more latency on stop/seek. Default ~180ms of stereo 16-bit @ 44.1kHz.</summary>
    public int BufferBytes = 32 * 1024;

    private IAudioStream? _stream;
    private int _sourceHandle;
    private int[] _buffers = Array.Empty<int>();
    private byte[] _scratch = Array.Empty<byte>();
    private ALFormat _format;
    private bool _handleAllocated;
    private bool _disposed;

    public override void Awake()
    {
        AudioEngine.EnsureInitialized();
        if (!AudioEngine.IsInitialized || StreamPath == null) return;

        // Extension dispatch — same shape as Resources.LoadAudioClip.
        var ext = System.IO.Path.GetExtension(StreamPath).ToLowerInvariant();
        try
        {
            _stream = ext switch
            {
                ".wav" => new WavStream(StreamPath),
                _ => throw new NotSupportedException($"Streaming '{ext}' not yet wired up."),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StreamingAudioSource] Failed to open {StreamPath}: {ex.Message}");
            return;
        }

        _format = ResolveFormat(_stream.Channels, _stream.BitsPerSample);

        _sourceHandle = AL.GenSource();
        _handleAllocated = true;
        _buffers = new int[BufferCount];
        _scratch = new byte[BufferBytes];
        for (int i = 0; i < BufferCount; i++) _buffers[i] = AL.GenBuffer();

        // Fill and queue every buffer up-front.
        for (int i = 0; i < BufferCount; i++)
        {
            if (!RefillBuffer(_buffers[i])) break;
        }

        if (PlayOnAwake) AL.SourcePlay(_sourceHandle);
    }

    public override void Update(float deltaTime)
    {
        if (!_handleAllocated || _stream == null) return;

        // Drive gain / position each frame (positional streams are rare but supported).
        var pos = Transform.Position;
        AL.Source(_sourceHandle, ALSource3f.Position, pos.X, pos.Y, pos.Z);
        AL.Source(_sourceHandle, ALSourcef.Gain, Volume * AudioMixer.Get(BusName).EffectiveVolume);

        // Recycle any buffers OpenAL has finished consuming.
        AL.GetSource(_sourceHandle, ALGetSourcei.BuffersProcessed, out int processed);
        while (processed-- > 0)
        {
            int drained = AL.SourceUnqueueBuffer(_sourceHandle);
            if (!RefillBuffer(drained))
            {
                // End of stream + no loop → let the source drain naturally.
                if (!Loop) break;
            }
        }

        // Underrun guard: if the source stalled while playing, kick it again.
        AL.GetSource(_sourceHandle, ALGetSourcei.SourceState, out int state);
        if (state == (int)ALSourceState.Stopped)
        {
            // Only restart if we still have queued audio and Loop said keep going.
            AL.GetSource(_sourceHandle, ALGetSourcei.BuffersQueued, out int queued);
            if (queued > 0) AL.SourcePlay(_sourceHandle);
        }
    }

    /// <summary>Read the next chunk from the stream (looping if enabled) and queue it. Returns false at final end-of-stream.</summary>
    private bool RefillBuffer(int buffer)
    {
        if (_stream == null) return false;

        int read = _stream.Read(_scratch);
        if (read == 0)
        {
            if (Loop)
            {
                _stream.Rewind();
                read = _stream.Read(_scratch);
            }
            if (read == 0) return false;   // truly empty — bail
        }

        // AL.BufferData ignores anything past `read` bytes when we pass the size.
        AL.BufferData(buffer, _format, new ReadOnlySpan<byte>(_scratch, 0, read), _stream.SampleRate);
        AL.SourceQueueBuffer(_sourceHandle, buffer);
        return true;
    }

    private static ALFormat ResolveFormat(int channels, int bits) => (channels, bits) switch
    {
        (1, 8)  => ALFormat.Mono8,
        (1, 16) => ALFormat.Mono16,
        (2, 8)  => ALFormat.Stereo8,
        (2, 16) => ALFormat.Stereo16,
        _ => throw new NotSupportedException($"Unsupported PCM format: {channels}ch, {bits}bit"),
    };

    public void Play()  { if (_handleAllocated) AL.SourcePlay(_sourceHandle);  }
    public void Stop()  { if (_handleAllocated) AL.SourceStop(_sourceHandle);  }
    public void Pause() { if (_handleAllocated) AL.SourcePause(_sourceHandle); }

    public override void OnDestroy() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handleAllocated && AudioEngine.IsInitialized)
        {
            AL.SourceStop(_sourceHandle);
            AL.DeleteSource(_sourceHandle);
            foreach (var b in _buffers) if (b != 0) AL.DeleteBuffer(b);
        }
        _handleAllocated = false;
        _stream?.Dispose();
        _stream = null;
        GC.SuppressFinalize(this);
    }
}
