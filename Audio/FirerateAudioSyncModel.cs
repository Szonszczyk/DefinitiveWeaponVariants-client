using System;
using System.Collections.Generic;

namespace DefinitiveWeaponVariants.Client.Audio
{
    internal sealed class FirerateAudioProfile
    {
        internal string Id { get; }
        internal string SourceTemplateId { get; }
        internal double SourceFireRate { get; }
        internal bool IsLoop { get; }

        internal FirerateAudioProfile(string id, string sourceTemplateId, double sourceFireRate, bool isLoop)
        {
            Id = id;
            SourceTemplateId = sourceTemplateId;
            SourceFireRate = sourceFireRate;
            IsLoop = isLoop;
        }

        internal bool Matches(FirerateAudioProfile other) => Id == other.Id &&
            SourceTemplateId == other.SourceTemplateId && SourceFireRate == other.SourceFireRate && IsLoop == other.IsLoop;
    }

    internal sealed class FirerateAudioSyncEntry
    {
        // Audio limits, not weapon stat limits. Extremely fast weapons deliberately
        // become an approximation; slow loops are bounded to avoid huge PCM buffers.
        internal const double MaximumAudioRpm = 1800;
        internal const double MaximumTempoRatio = 2.5;
        internal const double MinimumTempoRatio = 0.5;
        internal double BeforeFireRate { get; }
        internal double AfterFireRate { get; }
        internal string SourceTemplateId { get; }
        internal FirerateAudioProfile Profile { get; }
        internal double AudioFireRate => Profile == null ? AfterFireRate : Math.Max(Profile.SourceFireRate * MinimumTempoRatio,
            Math.Min(AfterFireRate, Math.Min(MaximumAudioRpm, Profile.SourceFireRate * MaximumTempoRatio)));
        internal double Ratio => Profile == null ? 1 : AudioFireRate / Profile.SourceFireRate;
        internal bool NeedsProcessing => Profile != null && Profile.IsLoop && Math.Abs(Ratio - 1) > 0.0001;
        internal bool IsCapped => Profile != null && Profile.IsLoop && Math.Abs(AudioFireRate - AfterFireRate) > 0.01;

        internal FirerateAudioSyncEntry(double beforeFireRate, double afterFireRate, FirerateAudioProfile profile)
        {
            BeforeFireRate = beforeFireRate;
            AfterFireRate = afterFireRate;
            Profile = profile;
            SourceTemplateId = profile.SourceTemplateId;
        }

        internal FirerateAudioSyncEntry(double beforeFireRate, double afterFireRate, string sourceTemplateId)
        {
            BeforeFireRate = beforeFireRate;
            AfterFireRate = afterFireRate;
            SourceTemplateId = sourceTemplateId;
        }

        // Each sound player resolves its own immutable entry. The catalog entry
        // stays unresolved, so one player's prefab cannot determine another's mode.
        internal FirerateAudioSyncEntry ResolvePlayback(bool isLoop, double bodyLoopLength)
        {
            if (Profile != null) return this; // Optional explicit override for custom assets.
            double sourceRate = BeforeFireRate;
            if (isLoop)
            {
                if (double.IsNaN(bodyLoopLength) || double.IsInfinity(bodyLoopLength) || bodyLoopLength <= 0)
                    throw new InvalidOperationException("Cannot determine the source cadence from this body bank's loop length.");
                // EFT's standard automatic banks contain sixteen beats.
                sourceRate = 16 * 60 / bodyLoopLength;
                if (double.IsInfinity(sourceRate) || sourceRate <= 0 || sourceRate > MaximumAudioRpm)
                    throw new InvalidOperationException("The detected source cadence is outside the supported audio range.");
            }
            var profile = new FirerateAudioProfile("auto." + SourceTemplateId, SourceTemplateId, sourceRate, isLoop);
            return new FirerateAudioSyncEntry(BeforeFireRate, AfterFireRate, profile);
        }

        internal static double BoundToRecordings(double requestedRpm, IEnumerable<double> loopLengths)
        {
            if (double.IsNaN(requestedRpm) || double.IsInfinity(requestedRpm) || requestedRpm <= 0)
                throw new ArgumentException("Invalid requested audio RPM.");
            double lower = 0, upper = MaximumAudioRpm;
            int count = 0;
            foreach (double length in loopLengths)
            {
                if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0)
                    throw new InvalidOperationException("A body recording has no valid sixteen-beat duration.");
                double recordedRpm = 16 * 60 / length;
                lower = Math.Max(lower, recordedRpm * MinimumTempoRatio);
                upper = Math.Min(upper, recordedRpm * MaximumTempoRatio);
                count++;
            }
            if (count == 0 || lower > upper) throw new InvalidOperationException("Body recordings have incompatible tempo ranges.");
            return Math.Max(lower, Math.Min(requestedRpm, upper));
        }

        internal static double RecordingRatio(double targetRpm, int frames, int frequency)
        {
            if (frames <= 0 || frequency <= 0 || double.IsNaN(targetRpm) || double.IsInfinity(targetRpm) || targetRpm <= 0)
                throw new ArgumentException("Invalid recording timing.");
            double ratio = targetRpm * frames / (16 * 60.0 * frequency);
            if (ratio < MinimumTempoRatio - 1e-9 || ratio > MaximumTempoRatio + 1e-9)
                throw new InvalidOperationException("Recording tempo exceeds the supported range.");
            return Math.Max(MinimumTempoRatio, Math.Min(MaximumTempoRatio, ratio));
        }
    }
}
