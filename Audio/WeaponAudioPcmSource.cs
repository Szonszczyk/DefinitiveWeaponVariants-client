using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DefinitiveWeaponVariants.Client.Audio
{
    // Pure managed worker pipeline: stock bundle -> bounded PCM -> SoundTouch ->
    // persistent PCM cache. No Unity calls or per-frame/per-shot disk operations.
    internal sealed class WeaponAudioPcmSource
    {
        internal sealed class Request
        {
            internal readonly string Name;
            internal readonly int Channels, Frequency, Frames;
            internal Request(string name, int channels, int frequency, int frames)
            { Name = name; Channels = channels; Frequency = frequency; Frames = frames; }
        }

        internal sealed class Catalog
        {
            public string Fingerprint { get; set; }
            public List<WeaponAudioBundleReader.Clip> Clips { get; set; }
        }

        private const int MaximumIndexBytes = 8 * 1024 * 1024;
        private const long MaximumDiskBytes = 512L * 1024 * 1024;
        private const string ProcessorVersion = "pcm-v1-soundtouch-2.3.2";
        private const long CacheMagic = 0x314d435053415744; // DWV_ASPCM v1 identity
        private readonly string[] _audioRoots;
        private readonly string _cacheRoot;
        private readonly object _gate = new object();
        private Task<Catalog> _index;
        private int _indexBuilds, _decoded, _transformed, _diskHits;
        internal int IndexedClipCount { get; private set; }
        internal int IndexBuildCount => _indexBuilds;
        internal int DecodeCount => _decoded;
        internal int TransformCount => _transformed;
        internal int DiskHits => _diskHits;
        internal string CacheWarning { get; private set; }
        internal string IndexWarnings { get; private set; }

        internal WeaponAudioPcmSource(string audioRoot, string cacheRoot, params string[] additionalAudioRoots)
        {
            _audioRoots = new[] { audioRoot }.Concat(additionalAudioRoots)
                .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            _cacheRoot = Path.GetFullPath(cacheRoot);
        }

        internal Task BeginIndex(CancellationToken cancellation) => EnsureIndex(cancellation);

        private Task<Catalog> EnsureIndex(CancellationToken cancellation)
        {
            lock (_gate)
            {
                if (_index == null)
                {
                    _index = AudioPreparationWorker.Run(() => BuildIndex(cancellation), cancellation);
                    ObserveFault(_index);
                }
                return _index;
            }
        }

        private Catalog BuildIndex(CancellationToken cancellation)
        {
            foreach (string root in _audioRoots)
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Weapon audio bundle directory is missing: " + root);
            // Recent weapons can embed their AudioClips in sound-bank bundles,
            // even in a different weapon's bank (MK47 clips live in mp5.bundle).
            var files = _audioRoots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                .Where(path => Path.GetExtension(path).Equals(".bundle", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path) == "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new FileInfo(path)).OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
            string manifest = string.Join("\n", files.Select(file => file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks));
            string fingerprint = HashText("Unity2022.3-AudioClip-v1\n" + string.Join("\n", _audioRoots) + "\n" + manifest);
            string path = Path.Combine(_cacheRoot, "index-v1.json");
            var catalog = TryReadIndex(path, fingerprint, files);
            if (catalog == null)
            {
                Interlocked.Increment(ref _indexBuilds);
                var clips = new List<WeaponAudioBundleReader.Clip>();
                var failures = new List<string>();
                foreach (var file in files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try { clips.AddRange(WeaponAudioBundleReader.Inspect(file.FullName, cancellation)); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { if (failures.Count < 8) failures.Add(file.Name + ": " + error.Message); }
                    if (clips.Count > 50000) throw new InvalidDataException("Weapon audio index exceeds its size limit.");
                }
                if (clips.Count == 0) throw new InvalidDataException("No weapon AudioClips could be indexed. " + string.Join("; ", failures));
                IndexWarnings = failures.Count == 0 ? null : string.Join("; ", failures);
                catalog = new Catalog { Fingerprint = fingerprint, Clips = clips };
                // Do not persist incomplete scans: transient I/O failures should
                // be retried at the next game start, rather than cached forever.
                if (failures.Count == 0) TryWrite(path, stream =>
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                        writer.Write(JsonConvert.SerializeObject(catalog));
                });
            }
            cancellation.ThrowIfCancellationRequested();
            IndexedClipCount = catalog.Clips.Count;
            return catalog;
        }

        private Catalog TryReadIndex(string path, string fingerprint, FileInfo[] files)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaximumIndexBytes) return null;
                var catalog = JsonConvert.DeserializeObject<Catalog>(File.ReadAllText(path), new JsonSerializerSettings { MaxDepth = 16 });
                if (catalog?.Fingerprint != fingerprint || catalog.Clips == null || catalog.Clips.Count == 0 || catalog.Clips.Count > 50000) return null;
                var stamps = files.ToDictionary(file => file.FullName, StringComparer.OrdinalIgnoreCase);
                foreach (var clip in catalog.Clips)
                {
                    if (clip == null || clip.BundlePath == null || !stamps.TryGetValue(clip.BundlePath, out var file) ||
                        clip.BundleLength != file.Length || clip.BundleWriteTicks != file.LastWriteTimeUtc.Ticks ||
                        string.IsNullOrEmpty(clip.Name) || clip.Name.Length > 4096 ||
                        string.IsNullOrEmpty(clip.ResourceName) || clip.ResourceName != Path.GetFileName(clip.ResourceName) ||
                        clip.Offset < 0 || clip.Size <= 0 || clip.Size > 32 * 1024 * 1024 ||
                        (clip.Channels != 1 && clip.Channels != 2) || clip.Frequency < 8000 || clip.Frequency > 192000 ||
                        float.IsNaN(clip.Length) || float.IsInfinity(clip.Length) || clip.Length <= 0 || clip.Subsound < 0)
                        return null;
                }
                return catalog;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException || error is ArgumentException)
            { return null; }
        }

        internal float[] Prepare(Request request, double ratio, CancellationToken cancellation)
        {
            if (request == null || string.IsNullOrEmpty(request.Name) || (request.Channels != 1 && request.Channels != 2) ||
                request.Frequency < 8000 || request.Frequency > 192000 || request.Frames <= 0 ||
                (long)request.Frames * request.Channels > FsbPcmDecoder.MaximumSamples ||
                double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < FirerateAudioSyncEntry.MinimumTempoRatio || ratio > FirerateAudioSyncEntry.MaximumTempoRatio)
                throw new ArgumentException("Invalid stock audio request.");
            cancellation.ThrowIfCancellationRequested();
            var catalog = EnsureIndex(cancellation).GetAwaiter().GetResult();
            cancellation.ThrowIfCancellationRequested();
            var matches = catalog.Clips.Where(clip => clip.Name == request.Name && clip.Channels == request.Channels &&
                clip.Frequency == request.Frequency && Math.Abs(Math.Round((double)clip.Length * clip.Frequency) - request.Frames) <= 2).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException(matches.Length == 0 ? "No matching stock audio resource for '" + request.Name + "'." :
                    "Ambiguous stock audio resource for '" + request.Name + "'; refusing to guess between bundles.");
            var source = matches[0];
            string identity = source.BundlePath + "|" + source.BundleLength + "|" + source.BundleWriteTicks + "|" + source.PathId + "|" +
                source.ResourceName + "|" + source.Offset + "|" + source.Size + "|" + source.Subsound + "|" +
                request.Channels + "|" + request.Frequency + "|" + request.Frames + "|" + ratio.ToString("R", CultureInfo.InvariantCulture);
            string cachePath = Path.Combine(_cacheRoot, HashText(ProcessorVersion + "|" + identity) + ".pcm");
            int outputFrames = Math.Max(1, (int)Math.Round(request.Frames / ratio));
            var hit = TryReadPcm(cachePath, request.Channels, request.Frequency, outputFrames, cancellation);
            if (hit != null) { Interlocked.Increment(ref _diskHits); return hit; }
            var decoded = FsbPcmDecoder.Decode(WeaponAudioBundleReader.ReadEncoded(source, cancellation), source.Subsound, cancellation);
            Interlocked.Increment(ref _decoded);
            if (decoded.Channels != request.Channels || decoded.Frequency != request.Frequency || Math.Abs(decoded.Frames - request.Frames) > 2)
                throw new InvalidDataException("Decoded stock audio format differs from the live AudioClip.");
            var input = decoded.Samples;
            if (decoded.Frames != request.Frames) Array.Resize(ref input, request.Frames * request.Channels);
            var processed = FirerateAudioSyncDsp.Stretch(input, request.Channels, request.Frequency, ratio, cancellation);
            Interlocked.Increment(ref _transformed);
            cancellation.ThrowIfCancellationRequested();
            TryWritePcm(cachePath, processed, request.Channels, request.Frequency, outputFrames);
            return processed;
        }

        private float[] TryReadPcm(string path, int channels, int frequency, int frames, CancellationToken cancellation)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var reader = new BinaryReader(File.OpenRead(path)))
                {
                    long bytes = (long)frames * channels * sizeof(float);
                    if (reader.BaseStream.Length != 8 + 12 + 32 + bytes || reader.ReadInt64() != CacheMagic ||
                        reader.ReadInt32() != channels || reader.ReadInt32() != frequency || reader.ReadInt32() != frames) return null;
                    byte[] digest = reader.ReadBytes(32), data = reader.ReadBytes((int)bytes);
                    cancellation.ThrowIfCancellationRequested();
                    using (var sha = SHA256.Create()) if (!sha.ComputeHash(data).SequenceEqual(digest)) return null;
                    var pcm = new float[frames * channels];
                    Buffer.BlockCopy(data, 0, pcm, 0, data.Length);
                    foreach (float sample in pcm) if (float.IsNaN(sample) || float.IsInfinity(sample)) return null;
                    return pcm;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return null; }
        }

        private void TryWritePcm(string path, float[] pcm, int channels, int frequency, int frames)
        {
            var data = new byte[pcm.Length * sizeof(float)];
            Buffer.BlockCopy(pcm, 0, data, 0, data.Length);
            TryWrite(path, stream =>
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                using (var sha = SHA256.Create())
                {
                    writer.Write(CacheMagic); writer.Write(channels); writer.Write(frequency); writer.Write(frames);
                    writer.Write(sha.ComputeHash(data)); writer.Write(data);
                }
            });
            try
            {
                var files = new DirectoryInfo(_cacheRoot).GetFiles("*.pcm").OrderBy(file => file.LastWriteTimeUtc).ToArray();
                long total = files.Sum(file => file.Length);
                foreach (var file in files)
                {
                    if (total <= MaximumDiskBytes) break;
                    if (file.FullName == path) continue;
                    long length = file.Length;
                    file.Delete(); total -= length;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { CacheWarning = error.Message; }
        }

        private void TryWrite(string path, Action<Stream> write)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(_cacheRoot);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) write(stream);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { CacheWarning = error.Message; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string HashText(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        private static void ObserveFault(Task task) => _ = task.ContinueWith(completed => { _ = completed.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
