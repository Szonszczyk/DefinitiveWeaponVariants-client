using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AssetsTools.NET.Extra;

namespace DefinitiveWeaponVariants.Client.Audio
{
    // Worker-only. Reads stock UnityFS assets without loading an AssetBundle in
    // Unity, changing importer flags, or modifying any game file.
    internal static class WeaponAudioBundleReader
    {
        internal sealed class Clip
        {
            public string BundlePath { get; set; }
            public long BundleLength { get; set; }
            public long BundleWriteTicks { get; set; }
            public long PathId { get; set; }
            public string Name { get; set; }
            public int Channels { get; set; }
            public int Frequency { get; set; }
            public float Length { get; set; }
            public int LoadType { get; set; }
            public int Compression { get; set; }
            public int Subsound { get; set; }
            public string ResourceName { get; set; }
            public long Offset { get; set; }
            public long Size { get; set; }
        }

        internal static List<Clip> Inspect(string path, CancellationToken cancellation)
        {
            var file = new FileInfo(path);
            using (var probe = File.OpenRead(path))
            {
                var magic = new byte[7];
                if (probe.Read(magic, 0, magic.Length) != magic.Length || Encoding.ASCII.GetString(magic) != "UnityFS")
                    return new List<Clip>();
            }
            var result = new List<Clip>();
            var manager = new AssetsManager();
            try
            {
                var bundle = manager.LoadBundleFile(path);
                for (int index = 0; index < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; index++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!bundle.file.IsAssetsFile(index)) continue;
                    var assets = manager.LoadAssetsFileFromBundle(bundle, index);
                    if (!assets.file.Metadata.UnityVersion.StartsWith("2022.3.", StringComparison.Ordinal) || assets.file.Reader.BigEndian)
                        throw new NotSupportedException("Weapon audio assets must use Unity 2022.3 little-endian serialization.");
                    foreach (var info in assets.file.Metadata.AssetInfos)
                    {
                        if (info.TypeId != 83) continue;
                        cancellation.ThrowIfCancellationRequested();
                        if (info.ByteSize > 65536) throw new InvalidDataException("Oversized AudioClip metadata.");
                        assets.file.Reader.Position = info.GetAbsoluteByteOffset(assets.file);
                        using (var reader = new BinaryReader(new MemoryStream(assets.file.Reader.ReadBytes((int)info.ByteSize))))
                        {
                            // Unity 2022.3 AudioClip's serialized scalar/StreamedResource
                            // layout; independent of stripped type trees or classdata.tpk.
                            string name = ReadAlignedText(reader);
                            int load = reader.ReadInt32(), channels = reader.ReadInt32(), frequency = reader.ReadInt32();
                            reader.ReadInt32(); // bits per sample
                            float length = reader.ReadSingle();
                            reader.ReadBoolean(); Align(reader);
                            int subsound = reader.ReadInt32();
                            reader.ReadBytes(3); Align(reader);
                            string resource = ReadAlignedText(reader);
                            long offset = reader.ReadInt64(), size = reader.ReadInt64();
                            int compression = reader.ReadInt32();
                            if ((channels != 1 && channels != 2) || frequency < 8000 || frequency > 192000 ||
                                length <= 0 || float.IsNaN(length) || float.IsInfinity(length) ||
                                offset < 0 || size <= 0 || size > 32 * 1024 * 1024 || subsound < 0)
                                continue;
                            if (string.IsNullOrEmpty(resource)) continue; // Unsupported inline/custom layout.
                            result.Add(new Clip { BundlePath = file.FullName, BundleLength = file.Length,
                                BundleWriteTicks = file.LastWriteTimeUtc.Ticks, PathId = info.PathId, Name = name,
                                LoadType = load, Channels = channels, Frequency = frequency, Length = length,
                                Subsound = subsound, ResourceName = resource.Replace('\\', '/').Split('/').Last(),
                                Offset = offset, Size = size, Compression = compression });
                        }
                    }
                }
            }
            finally { manager.UnloadAll(true); }
            return result;
        }

        internal static byte[] ReadEncoded(Clip clip, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var info = new FileInfo(clip.BundlePath);
            if (info.Length != clip.BundleLength || info.LastWriteTimeUtc.Ticks != clip.BundleWriteTicks)
                throw new IOException("Weapon audio bundle changed after indexing; restart the game to refresh it.");
            var manager = new AssetsManager();
            try
            {
                var bundle = manager.LoadBundleFile(clip.BundlePath);
                int index = bundle.file.BlockAndDirInfo.DirectoryInfos.FindIndex(x => x.Name == clip.ResourceName);
                if (index < 0) throw new InvalidDataException("Audio resource is not contained in the indexed bundle.");
                bundle.file.GetFileRange(index, out long start, out long length);
                if (clip.Offset < 0 || clip.Size <= 0 || clip.Size > 32 * 1024 * 1024 || clip.Offset > length - clip.Size)
                    throw new InvalidDataException("Audio resource range is invalid.");
                bundle.file.DataReader.Position = start + clip.Offset;
                byte[] bytes = bundle.file.DataReader.ReadBytes((int)clip.Size);
                cancellation.ThrowIfCancellationRequested();
                if (bytes.Length != clip.Size) throw new EndOfStreamException("Incomplete weapon audio resource.");
                return bytes;
            }
            finally { manager.UnloadAll(true); }
        }

        private static void Align(BinaryReader reader) => reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3L;
        private static string ReadAlignedText(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 4096 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid AudioClip metadata string.");
            string value = Encoding.UTF8.GetString(reader.ReadBytes(length));
            Align(reader);
            return value;
        }
    }
}
