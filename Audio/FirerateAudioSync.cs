using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Configuration;
using BepInEx.Logging;
using DefinitiveWeaponVariants.Client.Networking;
using EFT;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DefinitiveWeaponVariants.Client.Audio
{
    internal sealed class FirerateAudioSync : MonoBehaviour
    {
        internal static FirerateAudioSync Instance { get; private set; }
        private static readonly AccessTools.FieldRef<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge> Bridge =
            AccessTools.FieldRefAccess<BaseSoundPlayer, BaseSoundPlayer.IObserverToPlayerBridge>("playersBridge");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, bool> IsFiring =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, bool>("_isFiring");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, SuperBetterAudioQueue> Queue =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, SuperBetterAudioQueue>("_queue");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, float> LoopLength =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, float>("_firingLoopLength");
        private static readonly AccessTools.FieldRef<WeaponSoundPlayer, double> ReleaseTime =
            AccessTools.FieldRefAccess<WeaponSoundPlayer, double>("_releaseTime");
        private const long MaximumCacheBytes = 128L * 1024 * 1024;
        private const int MaximumClipSamples = 4 * 1024 * 1024;
        private const int MaximumBanks = 128;
        private readonly Dictionary<WeaponSoundPlayer, Binding> _bindings = new Dictionary<WeaponSoundPlayer, Binding>();
        private readonly Dictionary<(SoundBank, double), SoundBank> _banks = new Dictionary<(SoundBank, double), SoundBank>();
        private readonly Dictionary<(AudioClip, double), AudioClip> _clips = new Dictionary<(AudioClip, double), AudioClip>();
        private readonly HashSet<(SoundBank, double)> _failedBanks = new HashSet<(SoundBank, double)>();
        private readonly Queue<Binding> _pending = new Queue<Binding>();
        private readonly List<WeaponSoundPlayer> _removed = new List<WeaponSoundPlayer>();
        private readonly HashSet<string> _notices = new HashSet<string>(StringComparer.Ordinal);
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private IReadOnlyDictionary<string, FirerateAudioSyncEntry> _weapons;
        private ConfigEntry<bool> _enabled;
        private ManualLogSource _log;
        private Coroutine _worker;
        private long _cacheBytes;
        private double _cleanupAfter;
        private bool _unavailable;
        private WeaponAudioPcmSource _pcmSource;

        private sealed class Binding
        {
            internal WeaponSoundPlayer Player;
            internal string TemplateId;
            internal SoundBank Body, Silenced, ReadyBody, ReadySilenced;
            internal FirerateAudioSyncEntry Entry;
            internal double AudioRpm;
            internal bool Requested, Failed, Applied, Removed;
        }

        internal void Initialize(ConfigFile config, ManualLogSource log)
        {
            Instance = this;
            _log = log;
            _pcmSource = new WeaponAudioPcmSource(
                Path.Combine(Application.streamingAssetsPath, "Windows", "assets", "content", "audio", "weapons"),
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "BepInEx", "cache", "DefinitiveWeaponVariants", "FirerateAudioSync"),
                Path.Combine(Application.streamingAssetsPath, "Windows", "assets", "content", "audio", "banks"));
            _enabled = config.Bind("FirerateAudioSync", "Enabled", true,
                "Synchronize supported DWV weapon firing loops with server RPM changes. Audio is capped at 1800 RPM and 2.5x the source cadence. Changes apply after the current firing sound finishes.");
        }

        internal IEnumerator Load(Func<string, Task<string>> getJson)
        {
            var load = new FirerateAudioSyncLoader(getJson).LoadOnceAsync();
            while (!load.IsCompleted) yield return null;
            if (load.IsFaulted || load.IsCanceled)
            {
                _unavailable = true;
                _bindings.Clear();
                _log.LogWarning($"FirerateAudioSync: server data unavailable; audio synchronization inactive this session. {load.Exception?.GetBaseException().Message}");
                yield break;
            }
            var payload = load.GetAwaiter().GetResult();
            _weapons = payload.Weapons;
            _unavailable = _weapons.Count == 0;
            if (_unavailable) _bindings.Clear();
            _log.LogInfo($"FirerateAudioSync: loaded {_weapons.Count} weapon profiles from DWV server {payload.ServerModVersion}.");
            if (!_unavailable && _enabled.Value) StartCoroutine(ReportAudioIndex(_pcmSource.BeginIndex(_shutdown.Token)));
            foreach (var binding in _bindings.Values) ResolveEntry(binding);
        }

        private IEnumerator ReportAudioIndex(Task index)
        {
            while (!index.IsCompleted) yield return null;
            if (index.IsFaulted || index.IsCanceled)
            {
                Notice("index", "Stock PCM indexing failed: " + (index.Exception?.GetBaseException().Message ?? "cancelled"));
                yield break;
            }
            _log.LogInfo($"FirerateAudioSync: indexed {_pcmSource.IndexedClipCount} stock audio clips on a background worker ({(_pcmSource.IndexBuildCount == 0 ? "cached index" : "new index")}).");
            if (_pcmSource.IndexWarnings != null) Notice("index-partial", _pcmSource.IndexWarnings);
        }

        // Also runs for AI/observed players; eligibility belongs to the weapon ID.
        internal void Track(WeaponSoundPlayer player)
        {
            if (_unavailable) return;
            try
            {
                Untrack(player);
                string templateId = Bridge(player)?.Weapon?.Item?.TemplateId;
                if (string.IsNullOrEmpty(templateId)) return;
                var binding = new Binding { Player = player, TemplateId = templateId, Body = player.Body, Silenced = player.BodySilenced };
                _bindings[player] = binding;
                ResolveEntry(binding);
            }
            catch (Exception error) { Notice("track", "Cannot inspect this game's weapon audio API: " + error.Message); }
        }

        private void ResolveEntry(Binding binding)
        {
            if (_weapons == null || !_weapons.TryGetValue(binding.TemplateId, out var entry)) return;
            try
            {
                entry = entry.ResolvePlayback(binding.Player.IsAutoWeapon, binding.Body ? binding.Body.ClipLength : 0);
            }
            catch (InvalidOperationException error)
            {
                binding.Failed = true;
                Notice("detect:" + binding.TemplateId, binding.TemplateId + ": " + error.Message + " Using original sounds.");
                return;
            }
            binding.Entry = entry;
            if (entry.Profile.IsLoop && !binding.Player.IsAutoWeapon)
                Notice("oneshot:" + binding.TemplateId, binding.TemplateId + ": this sound prefab plays per shot; loop processing skipped.");
        }

        internal void BeforeShot(WeaponSoundPlayer player, bool multiShot)
        {
            if (_unavailable) return;
            if (!_bindings.TryGetValue(player, out var binding))
            {
                Track(player);
                _bindings.TryGetValue(player, out binding);
            }
            if (binding == null) return;
            // Fixed doublet recordings need their own authoring, outside this prototype.
            if (multiShot)
            {
                binding.Failed = true;
                if (!Busy(player)) Restore(binding);
                Notice("doublet:" + binding.TemplateId, binding.TemplateId + ": doublet audio is unsupported; using original sounds.");
            }
            // F12 changes can arrive between Update and this shot.
            if (!_enabled.Value && !Busy(player)) Restore(binding);
        }

        internal void Untrack(WeaponSoundPlayer player)
        {
            if (ReferenceEquals(player, null) || !_bindings.TryGetValue(player, out var binding)) return;
            binding.Removed = true;
            if (player)
            {
                // OnDisable has already handed its queue back with a delayed release.
                _cleanupAfter = Math.Max(_cleanupAfter, Math.Max(AudioSettings.dspTime + 1, ReleaseTime(player) + 1));
                Restore(binding);
            }
            _bindings.Remove(player);
        }

        private static bool Busy(WeaponSoundPlayer player) => IsFiring(player) || Queue(player) != null;

        private void Update()
        {
            _removed.Clear();
            foreach (var binding in _bindings.Values)
            {
                if (!binding.Player) { _removed.Add(binding.Player); continue; }
                try { UpdateBinding(binding); }
                catch (Exception error)
                {
                    binding.Failed = true;
                    Notice("binding:" + binding.TemplateId, binding.TemplateId + ": audio profile failed: " + error.Message);
                }
            }
            foreach (var player in _removed) Untrack(player);
            if (_worker == null && _pending.Count > 0) _worker = StartCoroutine(PreparePending());
            if (_bindings.Count == 0 && _worker == null && _pending.Count == 0 && AudioSettings.dspTime > _cleanupAfter)
                ClearCache();
        }

        private void UpdateBinding(Binding binding)
        {
            if (binding.Entry == null) return;
            bool eligible = _enabled.Value && !binding.Failed && binding.Entry.Profile.IsLoop && binding.Player.IsAutoWeapon;
            if (!eligible)
            {
                if (!Busy(binding.Player)) Restore(binding);
                return;
            }
            if (!binding.Requested)
            {
                binding.Requested = true;
                _pending.Enqueue(binding);
            }
            if (binding.Applied || !binding.ReadyBody || !binding.ReadySilenced || Busy(binding.Player)) return;
            // Another mod may replace a bank after initialization. Do not overwrite it.
            if (binding.Player.Body != binding.Body || binding.Player.BodySilenced != binding.Silenced)
            {
                binding.Failed = true;
                Notice("bank-change:" + binding.TemplateId, binding.TemplateId + ": another mod changed its audio banks; skipping synchronization.");
                return;
            }
            binding.Player.Body = binding.ReadyBody;
            binding.Player.BodySilenced = binding.ReadySilenced;
            LoopLength(binding.Player) = (binding.Player.IsSilenced ? binding.ReadySilenced : binding.ReadyBody).ClipLength;
            binding.Applied = true;
            _log.LogInfo($"FirerateAudioSync: ready {binding.TemplateId}, profile {binding.Entry.Profile.Id}, audio {binding.AudioRpm:0.##} RPM. Stock PCM decoded={_pcmSource.DecodeCount}, disk cache hits={_pcmSource.DiskHits}.");
            if (_pcmSource.CacheWarning != null) Notice("cache-io", "Disk cache unavailable; prepared audio still works this session. " + _pcmSource.CacheWarning);
        }

        private static void Restore(Binding binding)
        {
            if (!binding.Applied || !binding.Player) return;
            // Restore only fields we still own.
            if (binding.Player.Body == binding.ReadyBody) binding.Player.Body = binding.Body;
            if (binding.Player.BodySilenced == binding.ReadySilenced) binding.Player.BodySilenced = binding.Silenced;
            var bank = binding.Player.IsSilenced ? binding.Player.BodySilenced : binding.Player.Body;
            if (bank) LoopLength(binding.Player) = bank.ClipLength;
            binding.Applied = false;
        }

        private IEnumerator PreparePending()
        {
            // StartCoroutine advances immediately: yield once so _worker is assigned
            // before this routine can complete and reset it.
            yield return null;
            try
            {
                while (_pending.Count > 0)
                {
                    var binding = _pending.Dequeue();
                    if (binding.Removed || binding.Failed) continue;
                    if (!_enabled.Value) { binding.Requested = false; continue; }
                    var body = binding.Body;
                    var silenced = binding.Silenced ? binding.Silenced : body;
                    if (!body || !silenced || !MatchesCadence(body, binding.Entry.Profile.SourceFireRate) ||
                        !MatchesCadence(silenced, binding.Entry.Profile.SourceFireRate))
                    {
                        binding.Failed = true;
                        Notice("cadence:" + binding.TemplateId,
                            $"{binding.TemplateId}: sourceAudioProfile expects {16 * 60 / binding.Entry.Profile.SourceFireRate:0.####}s per 16-beat loop; body={(body ? body.ClipLength : 0):0.####}s, suppressed={(silenced ? silenced.ClipLength : 0):0.####}s. Using original sounds.");
                        continue;
                    }
                    // Stock bank metadata is approximate, and normal/suppressed
                    // recordings can have different real durations. Give every
                    // sixteen-beat recording the SAME target period, with a coherent
                    // cap that every required layer can reach without extreme DSP.
                    try
                    {
                        var recordings = CollectClips(body);
                        recordings.UnionWith(CollectClips(silenced));
                        binding.AudioRpm = FirerateAudioSyncEntry.BoundToRecordings(binding.Entry.AfterFireRate,
                            recordings.Select(clip => clip.samples / (double)clip.frequency));
                    }
                    catch (Exception error)
                    {
                        binding.Failed = true;
                        Notice("timing:" + binding.TemplateId, binding.TemplateId + ": " + error.Message);
                        continue;
                    }
                    if (Math.Abs(binding.AudioRpm - binding.Entry.AfterFireRate) > 0.01)
                        Notice("cap:" + binding.TemplateId,
                            $"{binding.TemplateId}: weapon {binding.Entry.BeforeFireRate:0.##} -> {binding.Entry.AfterFireRate:0.##} RPM; audio limited to {binding.AudioRpm:0.##} RPM across all body recordings.");
                    yield return PrepareBankSafely(body, binding.AudioRpm);
                    if (!binding.Removed) yield return PrepareBankSafely(silenced, binding.AudioRpm);
                    if (binding.Removed) continue;
                    if (!_banks.TryGetValue((body, binding.AudioRpm), out binding.ReadyBody) ||
                        !_banks.TryGetValue((silenced, binding.AudioRpm), out binding.ReadySilenced))
                        binding.Failed = true;
                }
            }
            finally { _worker = null; }
        }

        private IEnumerator PrepareBankSafely(SoundBank source, double audioRpm)
        {
            var iterator = PrepareBank(source, audioRpm);
            try
            {
                while (true)
                {
                    object next;
                    try
                    {
                        if (!iterator.MoveNext()) yield break;
                        next = iterator.Current;
                    }
                    catch (Exception error)
                    {
                        FailBank(source, audioRpm, error.Message);
                        yield break;
                    }
                    yield return next;
                }
            }
            finally { (iterator as IDisposable)?.Dispose(); }
        }

        private static bool MatchesCadence(SoundBank bank, double sourceRpm)
        {
            double expected = 16 * 60 / sourceRpm;
            return bank.ClipLength > 0 && Math.Abs(bank.ClipLength - expected) <= expected * 0.05;
        }

        private IEnumerator PrepareBank(SoundBank source, double audioRpm)
        {
            var key = (source, audioRpm);
            if (_banks.ContainsKey(key) || _failedBanks.Contains(key)) yield break;
            if (_banks.Count >= MaximumBanks) { FailBank(source, audioRpm, "bank cache limit reached"); yield break; }
            var sources = CollectClips(source);
            if (sources.Count == 0) { FailBank(source, audioRpm, "body bank contains no audio clips"); yield break; }
            foreach (var clip in sources)
            {
                double ratio = FirerateAudioSyncEntry.RecordingRatio(audioRpm, clip.samples, clip.frequency);
                if (_clips.ContainsKey((clip, ratio))) continue;
                var cancellation = _shutdown.Token;
                Task<float[]> work;
                int channels = clip.channels, frequency = clip.frequency;
                long count = (long)clip.samples * channels;
                long outputCount = (long)Math.Ceiling(count / ratio) + channels;
                if ((channels != 1 && channels != 2) || count <= 0 || count > MaximumClipSamples ||
                    _cacheBytes + (count + outputCount) * sizeof(float) > MaximumCacheBytes)
                {
                    FailBank(source, audioRpm, "PCM format/size or 128 MiB cache budget exceeded");
                    yield break;
                }
                if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    // Snapshot Unity metadata only. Extraction, decoding, cache I/O
                    // and time stretching all happen on the lower-priority worker.
                    var request = new WeaponAudioPcmSource.Request(clip.name, channels, frequency, clip.samples);
                    work = AudioPreparationWorker.Run(() => _pcmSource.Prepare(request, ratio, cancellation), cancellation);
                }
                else
                {
                    // Already-readable/custom clips keep the direct Unity path.
                    clip.LoadAudioData();
                    float deadline = Time.realtimeSinceStartup + 2;
                    while (clip && clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < deadline) yield return null;
                    if (!clip || clip.loadState != AudioDataLoadState.Loaded)
                    { FailBank(source, audioRpm, "body clip could not be loaded"); yield break; }
                    if (!TrySnapshot(clip, ratio, out var samples, out channels, out frequency, out string snapshotFailure))
                    { FailBank(source, audioRpm, snapshotFailure); yield break; }
                    work = AudioPreparationWorker.Run(() => FirerateAudioSyncDsp.Stretch(samples, channels, frequency, ratio, cancellation), cancellation);
                }
                _ = work.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                while (!work.IsCompleted) yield return null;
                if (work.IsFaulted || work.IsCanceled)
                { FailBank(source, audioRpm, work.Exception?.GetBaseException().Message ?? "processing cancelled"); yield break; }
                if (!TryCreateClip(clip, ratio, work.Result, channels, frequency, out string failure))
                { FailBank(source, audioRpm, failure); yield break; }
                yield return null;
            }
            if (TryCloneBank(source, audioRpm, out var replacement, out string reason)) _banks.Add(key, replacement);
            else FailBank(source, audioRpm, reason);
        }

        private static HashSet<AudioClip> CollectClips(SoundBank source)
        {
            var clips = new HashSet<AudioClip>();
            if (source.Environments == null) return clips;
            foreach (var environment in source.Environments)
            {
                if (environment?.Clips == null) continue;
                foreach (var distance in environment.Clips)
                {
                    if (distance?.Clips == null) continue;
                    foreach (var clip in distance.Clips) if (clip) clips.Add(clip);
                }
            }
            return clips;
        }

        private bool TrySnapshot(AudioClip clip, double ratio, out float[] data, out int channels, out int frequency, out string reason)
        {
            data = null;
            channels = clip.channels;
            frequency = clip.frequency;
            reason = null;
            long count = (long)clip.samples * channels;
            long outputCount = (long)Math.Ceiling(count / ratio) + channels;
            if ((channels != 1 && channels != 2) || count <= 0 || count > MaximumClipSamples ||
                _cacheBytes + (count + outputCount) * sizeof(float) > MaximumCacheBytes)
            { reason = "PCM format/size or 128 MiB cache budget exceeded"; return false; }
            try
            {
                data = new float[(int)count];
                if (!clip.GetData(data, 0)) { reason = $"GetData failed for '{clip.name}'"; return false; }
                return true;
            }
            catch (Exception error) { reason = error.Message; return false; }
        }

        private bool TryCreateClip(AudioClip original, double ratio, float[] data, int channels, int frequency, out string reason)
        {
            AudioClip clip = null;
            reason = null;
            try
            {
                clip = AudioClip.Create(original.name + "_DWV_FirerateAudioSync", data.Length / channels, channels, frequency, false);
                if (!clip.SetData(data, 0)) throw new InvalidOperationException("AudioClip.SetData failed.");
                _clips.Add((original, ratio), clip);
                _cacheBytes += (long)data.Length * sizeof(float);
                return true;
            }
            catch (Exception error)
            {
                if (clip) Object.Destroy(clip);
                reason = error.Message;
                return false;
            }
        }

        private bool TryCloneBank(SoundBank source, double audioRpm, out SoundBank result, out string reason)
        {
            result = null;
            reason = null;
            try
            {
                result = Object.Instantiate(source);
                result.name = source.name + "_DWV_FirerateAudioSync";
                result.CustomLength = (float)(16 * 60 / audioRpm);
                result.ClipLength = result.CustomLength;
                // Unity serialization/Instantiate behavior must not decide whether
                // the original nested managed arrays are shared. Copy every level.
                result.Environments = new EnvironmentVariety[source.Environments.Length];
                for (int e = 0; e < source.Environments.Length; e++)
                {
                    var environment = source.Environments[e];
                    if (environment?.Clips == null) continue;
                    var distances = new DistanceVarity[environment.Clips.Length];
                    result.Environments[e] = new EnvironmentVariety { Clips = distances };
                    for (int d = 0; d < distances.Length; d++)
                    {
                        var originals = environment.Clips[d]?.Clips;
                        if (originals == null) continue;
                        var replacements = new AudioClip[originals.Length];
                        distances[d] = new DistanceVarity { Clips = replacements };
                        for (int c = 0; c < originals.Length; c++)
                            replacements[c] = originals[c] ? _clips[(originals[c],
                                FirerateAudioSyncEntry.RecordingRatio(audioRpm, originals[c].samples, originals[c].frequency))] : null;
                    }
                }
                return true;
            }
            catch (Exception error)
            {
                if (result) Object.Destroy(result);
                result = null;
                reason = error.Message;
                return false;
            }
        }

        private void FailBank(SoundBank bank, double ratio, string reason)
        {
            _failedBanks.Add((bank, ratio));
            if (!bank) { Notice("unloaded", "Source bank unloaded during preparation; using original sounds."); return; }
            Notice("bank:" + bank.GetInstanceID() + ":" + ratio,
                $"Cannot prepare '{bank.name}': {reason.TrimEnd('.')}. Original clips and timing retained.");
        }

        private void Notice(string key, string text)
        {
            if (_notices.Count < 4096 && _notices.Add(key)) _log.LogWarning("FirerateAudioSync: " + text);
        }

        private void ClearCache()
        {
            foreach (var bank in _banks.Values) if (bank) Object.Destroy(bank);
            foreach (var clip in _clips.Values) if (clip) Object.Destroy(clip);
            _banks.Clear();
            _clips.Clear();
            _failedBanks.Clear();
            _cacheBytes = 0;
        }

        private void OnDestroy()
        {
            _shutdown.Cancel();
            StopAllCoroutines();
            foreach (var binding in _bindings.Values)
            {
                if (binding.Applied && binding.Player) binding.Player.Release(0);
                Restore(binding);
            }
            _bindings.Clear();
            ClearCache();
            _shutdown.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
