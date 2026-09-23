using System.Text;

namespace ArcEngine.Engine.Audio;

/// <summary>
/// PCM-WAV backed <see cref="IAudioStream"/>. Parses the WAV header on open,
/// seeks to the data chunk, and then lets callers pull raw PCM in chunks.
/// The file stays open for the life of the stream.
/// </summary>
public class WavStream : IAudioStream
{
    public int Channels      { get; }
    public int SampleRate    { get; }
    public int BitsPerSample { get; }
    public long TotalSamples { get; }

    private readonly FileStream _file;
    private readonly long _dataStart;
    private readonly long _dataLength;

    public WavStream(string path)
    {
        _file = File.OpenRead(path);
        using var reader = new BinaryReader(_file, Encoding.ASCII, leaveOpen: true);

        if (ReadFourCC(reader) != "RIFF") throw new InvalidDataException($"{path}: missing RIFF header");
        reader.ReadInt32();
        if (ReadFourCC(reader) != "WAVE") throw new InvalidDataException($"{path}: missing WAVE tag");

        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        long dataStart = -1, dataLength = 0;

        while (_file.Position < _file.Length)
        {
            var id = ReadFourCC(reader);
            int size = reader.ReadInt32();
            long chunkEnd = _file.Position + size;

            if (id == "fmt ")
            {
                int format = reader.ReadInt16();
                if (format != 1)
                    throw new NotSupportedException($"{path}: only uncompressed PCM (format=1) is supported for streaming");
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
            }
            else if (id == "data")
            {
                dataStart = _file.Position;
                dataLength = size;
                break;   // stop scanning — playback reads from here on
            }

            _file.Position = chunkEnd + (size & 1);
        }

        if (dataStart < 0) throw new InvalidDataException($"{path}: no data chunk found");

        Channels = channels;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        _dataStart = dataStart;
        _dataLength = dataLength;
        TotalSamples = dataLength / (channels * (bitsPerSample / 8));

        _file.Position = dataStart;
    }

    public int Read(byte[] buffer)
    {
        long remaining = _dataStart + _dataLength - _file.Position;
        if (remaining <= 0) return 0;
        int toRead = (int)System.Math.Min(buffer.Length, remaining);
        return _file.Read(buffer, 0, toRead);
    }

    public void Rewind() => _file.Position = _dataStart;

    public void Dispose() => _file.Dispose();

    private static string ReadFourCC(BinaryReader r) => Encoding.ASCII.GetString(r.ReadBytes(4));
}
