using OpenTK.Audio.OpenAL;

namespace ArcEngine.Engine.Audio;

/// <summary>
/// One decoded audio asset uploaded to OpenAL. Owns the <c>AL Buffer</c> handle
/// so callers just reference the clip; disposal frees the GPU-side (well,
/// OpenAL-side) memory.
///
/// Mono clips are positional (respect the listener's transform); stereo clips
/// play flat regardless of source position — OpenAL treats them as 2D. Use
/// mono for SFX, stereo for music.
/// </summary>
public class AudioClip : IDisposable
{
    public int Handle { get; private set; }

    /// <summary>1 for mono, 2 for stereo. Determined at load time.</summary>
    public int Channels { get; }

    public int SampleRate { get; }
    public int SampleCount { get; }
    public int BitsPerSample { get; }

    /// <summary>Total duration in seconds.</summary>
    public float Duration => SampleCount / (float)SampleRate;

    /// <summary>Path this clip was loaded from, or null for byte-loaded clips.</summary>
    public string? SourcePath { get; }

    private bool _disposed;

    public AudioClip(byte[] pcmData, int channels, int sampleRate, int bitsPerSample, string? sourcePath = null)
    {
        AudioEngine.EnsureInitialized();
        if (!AudioEngine.IsInitialized)
        {
            Handle = 0;
            Channels = channels;
            SampleRate = sampleRate;
            BitsPerSample = bitsPerSample;
            SampleCount = pcmData.Length / (channels * (bitsPerSample / 8));
            SourcePath = sourcePath;
            return;
        }

        Channels = channels;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        SampleCount = pcmData.Length / (channels * (bitsPerSample / 8));
        SourcePath = sourcePath;

        Handle = AL.GenBuffer();
        var format = ResolveFormat(channels, bitsPerSample);
        AL.BufferData(Handle, format, pcmData, sampleRate);
        AudioEngine.CheckError("AudioClip upload");
    }

    private static ALFormat ResolveFormat(int channels, int bits) => (channels, bits) switch
    {
        (1, 8)  => ALFormat.Mono8,
        (1, 16) => ALFormat.Mono16,
        (2, 8)  => ALFormat.Stereo8,
        (2, 16) => ALFormat.Stereo16,
        _ => throw new NotSupportedException($"Unsupported PCM format: {channels}ch, {bits}bit"),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Handle != 0 && AudioEngine.IsInitialized) AL.DeleteBuffer(Handle);
        Handle = 0;
        GC.SuppressFinalize(this);
    }
}
