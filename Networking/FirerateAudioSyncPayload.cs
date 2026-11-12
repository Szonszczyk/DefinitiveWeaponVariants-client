using System;
using System.Collections.Generic;
using DefinitiveWeaponVariants.Client.Audio;
using Newtonsoft.Json.Linq;

namespace DefinitiveWeaponVariants.Client.Networking
{
    internal sealed class FirerateAudioSyncPayload
    {
        internal string ServerModVersion { get; }
        internal IReadOnlyDictionary<string, FirerateAudioSyncEntry> Weapons { get; }

        private FirerateAudioSyncPayload(string version, Dictionary<string, FirerateAudioSyncEntry> weapons)
        {
            ServerModVersion = version;
            Weapons = weapons;
        }

        internal static FirerateAudioSyncPayload Parse(string json)
        {
            var entries = ColorsPayloadEnvelope.Parse(json, "firerate-audio-sync", "firerateAudioSync", out string version);
            if (entries.Count > 4096) throw new FormatException("FirerateAudioSync catalog exceeds 4096 weapons.");
            var weapons = new Dictionary<string, FirerateAudioSyncEntry>(StringComparer.Ordinal);
            var profiles = new Dictionary<string, FirerateAudioProfile>(StringComparer.Ordinal);
            foreach (var entry in entries.Properties())
            {
                if (!(entry.Value is JObject weapon))
                    throw new FormatException($"FirerateAudioSync {entry.Name}: expected a weapon object.");
                if (weapon["sourceAudioProfile"] == null)
                {
                    // The client determines playback mode and recorded cadence.
                    string sourceId = RequiredString(weapon, "sourceTemplateId");
                    ValidateTemplateId(sourceId);
                    weapons.Add(entry.Name, new FirerateAudioSyncEntry(RequiredRate(weapon, "beforeFireRate"),
                        RequiredRate(weapon, "afterFireRate"), sourceId));
                    continue;
                }
                // Explicit profiles remain optional overrides for custom assets.
                if (!(weapon["sourceAudioProfile"] is JObject profile))
                    throw new FormatException($"FirerateAudioSync {entry.Name}: invalid sourceAudioProfile override.");
                string id = RequiredString(profile, "id");
                if (id.Length > 128 || Array.Exists(id.ToCharArray(), char.IsWhiteSpace))
                    throw new FormatException("Audio profile IDs must be at most 128 characters without whitespace.");
                string source = RequiredString(profile, "sourceTemplateId");
                ValidateTemplateId(source);
                if (weapon["sourceTemplateId"] != null && RequiredString(weapon, "sourceTemplateId") != source)
                    throw new FormatException("sourceTemplateId and sourceAudioProfile.sourceTemplateId must agree.");
                string playback = RequiredString(profile, "playbackMode");
                if (playback != "loop" && playback != "perShot")
                    throw new FormatException("Audio playbackMode must be 'loop' or 'perShot'.");
                double sourceRate = RequiredRate(profile, "sourceFireRate");
                if (playback == "loop" && sourceRate > FirerateAudioSyncEntry.MaximumAudioRpm)
                    throw new FormatException("Source loop cadence exceeds the prototype's 1800 RPM limit.");
                var audioProfile = new FirerateAudioProfile(id, source, sourceRate, playback == "loop");
                if (profiles.TryGetValue(id, out var previous) && !previous.Matches(audioProfile))
                    throw new FormatException($"Conflicting definitions of audio profile '{id}'.");
                profiles[id] = audioProfile;
                weapons.Add(entry.Name, new FirerateAudioSyncEntry(RequiredRate(weapon, "beforeFireRate"),
                    RequiredRate(weapon, "afterFireRate"), audioProfile));
            }
            return new FirerateAudioSyncPayload(version, weapons);
        }

        private static void ValidateTemplateId(string source)
        {
            if (source.Length != 24 || !Array.TrueForAll(source.ToCharArray(), Uri.IsHexDigit))
                throw new FormatException($"Invalid audio source template ID '{source}'.");
        }

        private static string RequiredString(JObject value, string name)
        {
            var token = value[name];
            if (token?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token))
                throw new FormatException($"Missing audio field '{name}'.");
            return (string)token;
        }

        private static double RequiredRate(JObject value, string name)
        {
            var token = value[name];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new FormatException($"Audio field '{name}' must be a resolved numeric RPM.");
            double rate = token.Value<double>();
            if (double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0 || rate > 1000000)
                throw new FormatException($"Invalid audio RPM '{name}'.");
            return rate;
        }
    }
}
