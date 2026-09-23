namespace ArcEngine.Engine.Audio;

/// <summary>
/// Streaming audio source. A large track (e.g. a music file) implements this
/// so <see cref="StreamingAudioSource"/> can pull chunks lazily instead of
/// decoding the entire thing into memory up-front. Compressed formats (OGG/MP3)
/// will implement this once decoders are wired up; <see cref="WavStream"/> is
/// the reference PCM implementation.
/// </summary>
public interface IAudioStream : IDisposable
{
    int Channels      { get; }
    int SampleRate    { get; }
    int BitsPerSample { get; }

    /// <summary>Total sample count across all channels. -1 for unknown-length streams.</summary>
    long TotalSamples { get; }

    /// <summary>
    /// Fill <paramref name="buffer"/> with the next <c>Length</c> bytes of raw PCM
    /// and return how many were written. Returns 0 at end-of-stream.
    /// </summary>
    int Read(byte[] buffer);

    /// <summary>Rewind to the start. Used for looping playback.</summary>
    void Rewind();
}
