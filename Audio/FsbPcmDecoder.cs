using System;
using System.IO;
using System.Threading;

namespace DefinitiveWeaponVariants.Client.Audio
{
    // Bounded decoder for the PCM FSB5 containers used by EFT's firing loops.
    // No native decoder, real-time playback capture, or general Vorbis stack.
    internal static class FsbPcmDecoder
    {
        internal sealed class Pcm
        {
            internal readonly float[] Samples;
            internal readonly int Channels, Frequency;
            internal int Frames => Samples.Length / Channels;
            internal Pcm(float[] samples, int channels, int frequency)
            { Samples = samples; Channels = channels; Frequency = frequency; }
        }

        private static readonly int[] Frequencies = { 4000, 8000, 11000, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };
        internal const int MaximumSamples = 4 * 1024 * 1024;

        internal static Pcm Decode(byte[] bytes, int subsound, CancellationToken cancellation)
        {
            if (bytes == null || bytes.Length < 60 || bytes.Length > 32 * 1024 * 1024)
                throw new InvalidDataException("Invalid FSB resource length.");
            using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != 0x35425346) throw new InvalidDataException("Weapon audio resource is not FSB5.");
                uint version = reader.ReadUInt32(), count = reader.ReadUInt32();
                uint headerBytes = reader.ReadUInt32(), nameBytes = reader.ReadUInt32(), dataBytes = reader.ReadUInt32();
                int codec = reader.ReadInt32();
                int fixedHeader = version == 0 ? 64 : 60;
                long dataStart = fixedHeader + (long)headerBytes + nameBytes;
                long headerEnd = fixedHeader + (long)headerBytes;
                if (version > 1 || count == 0 || count > 1024 || subsound < 0 || subsound >= count ||
                    headerBytes < count * 8 || dataStart > bytes.Length || dataBytes > bytes.Length - dataStart)
                    throw new InvalidDataException("Invalid FSB5 header or subsound.");
                // Every stock recording verified for SPT 4.1.6 uses PCM16.
                // Do not guess the layout of unverified custom codecs.
                const int width = 2;
                if (codec != 2) throw new NotSupportedException($"FSB codec {codec} is unsupported; stock PCM16 firing loops are required.");
                reader.BaseStream.Position = fixedHeader;
                long chosenOffset = -1, chosenEnd = dataBytes;
                int chosenChannels = 0, chosenFrequency = 0, chosenFrames = 0;
                for (int sample = 0; sample < count; sample++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (reader.BaseStream.Position > headerEnd - 8) throw new InvalidDataException("Truncated FSB sample header.");
                    ulong packed = reader.ReadUInt64();
                    long offset = (long)((packed >> 7) & 0x7ffffff) * 32;
                    int channelCode = (int)((packed >> 5) & 3);
                    int channels = channelCode == 0 ? 1 : channelCode == 1 ? 2 : channelCode == 2 ? 6 : 8;
                    int frequencyCode = (int)((packed >> 1) & 15);
                    int frequency = frequencyCode < Frequencies.Length ? Frequencies[frequencyCode] : 0;
                    int frames = (int)(packed >> 34);
                    bool more = (packed & 1) != 0;
                    while (more)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (reader.BaseStream.Position > headerEnd - 4) throw new InvalidDataException("Truncated FSB sample chunk.");
                        uint chunk = reader.ReadUInt32();
                        more = (chunk & 1) != 0;
                        int size = (int)((chunk >> 1) & 0xffffff), type = (int)(chunk >> 25);
                        long end = reader.BaseStream.Position + size;
                        if (end > headerEnd) throw new InvalidDataException("Invalid FSB sample chunk length.");
                        if (type == 1 && size == 1) channels = reader.ReadByte();
                        else if (type == 2 && size == 4) frequency = reader.ReadInt32();
                        reader.BaseStream.Position = end;
                    }
                    if (offset > dataBytes) throw new InvalidDataException("FSB sample offset exceeds data.");
                    if (sample == subsound)
                    {
                        chosenOffset = offset; chosenChannels = channels; chosenFrequency = frequency; chosenFrames = frames;
                    }
                    else if (sample == subsound + 1) chosenEnd = offset;
                }
                long total = (long)chosenFrames * chosenChannels;
                if ((chosenChannels != 1 && chosenChannels != 2) || chosenFrequency < 8000 || chosenFrequency > 192000 ||
                    total <= 0 || total > MaximumSamples || chosenOffset < 0 || chosenEnd < chosenOffset ||
                    total * width > chosenEnd - chosenOffset)
                    throw new InvalidDataException("Invalid FSB PCM format, frame count or data range.");
                var output = new float[(int)total];
                int cursor = checked((int)(dataStart + chosenOffset));
                for (int i = 0; i < output.Length; i++, cursor += width)
                {
                    if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                    output[i] = (short)(bytes[cursor] | bytes[cursor + 1] << 8) / 32768f;
                }
                return new Pcm(output, chosenChannels, chosenFrequency);
            }
        }
    }
}
