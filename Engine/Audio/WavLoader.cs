using System.Text;

namespace ArcEngine.Engine.Audio;

/// <summary>
/// Minimal RIFF/WAVE parser. Handles the common cases: uncompressed PCM
/// (format code 1) at 8 or 16 bits per sample, mono or stereo, any sample
/// rate. Rejects float / ADPCM / compressed variants — those need a codec.
///
/// Reference: <see href="http://soundfile.sapp.org/doc/WaveFormat/"/>.
/// </summary>
public static class WavLoader
{
    /// <summary>Parse a WAV file into a runtime <see cref="AudioClip"/>.</summary>
    public static AudioClip? Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[WavLoader] File not found: {path}");
            return null;
        }

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (ReadFourCC(reader) != "RIFF") return LogAndFail(path, "missing RIFF header");
        reader.ReadInt32(); // total size — ignored
        if (ReadFourCC(reader) != "WAVE") return LogAndFail(path, "missing WAVE tag");

        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        byte[]? pcmData = null;

        // Chunk walk. WAV files can have ancillary chunks (LIST, cue, etc.); we skip them.
        while (stream.Position < stream.Length)
        {
            var id = ReadFourCC(reader);
            int size = reader.ReadInt32();
            long chunkEnd = stream.Position + size;

            if (id == "fmt ")
            {
                int format = reader.ReadInt16();
                if (format != 1)
                    return LogAndFail(path, $"unsupported format code {format} (only PCM=1 is handled)");
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();               // byte rate — derivable
                reader.ReadInt16();               // block align — derivable
                bitsPerSample = reader.ReadInt16();
            }
            else if (id == "data")
            {
                pcmData = reader.ReadBytes(size);
            }

            // Chunks are padded to even byte counts.
            stream.Position = chunkEnd + (size & 1);
        }

        if (pcmData == null || channels == 0)
            return LogAndFail(path, "missing 'fmt ' or 'data' chunk");

        return new AudioClip(pcmData, channels, sampleRate, bitsPerSample, sourcePath: path);
    }

    private static string ReadFourCC(BinaryReader r) => Encoding.ASCII.GetString(r.ReadBytes(4));

    private static AudioClip? LogAndFail(string path, string reason)
    {
        Console.WriteLine($"[WavLoader] '{path}' rejected: {reason}");
        return null;
    }
}
