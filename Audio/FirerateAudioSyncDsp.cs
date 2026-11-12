using System;
using System.Threading;
using SoundTouch;

namespace DefinitiveWeaponVariants.Client.Audio
{
    // Pure managed PCM processing. No Unity object may enter this worker.
    internal static class FirerateAudioSyncDsp
    {
        internal static float[] Stretch(float[] input, int channels, int sampleRate, double ratio, CancellationToken cancellation)
        {
            if (input == null || input.Length == 0 || (channels != 1 && channels != 2) || input.Length % channels != 0 ||
                sampleRate < 8000 || sampleRate > 192000 || double.IsNaN(ratio) || double.IsInfinity(ratio) ||
                ratio < FirerateAudioSyncEntry.MinimumTempoRatio || ratio > FirerateAudioSyncEntry.MaximumTempoRatio)
                throw new ArgumentException("Invalid FirerateAudioSync PCM format or tempo.");
            cancellation.ThrowIfCancellationRequested();
            var processor = new SoundTouchProcessor
            {
                Channels = channels,
                SampleRate = sampleRate,
                TempoChange = (ratio - 1) * 100,
                PitchSemiTones = 0,
                RateChange = 0
            };
            int expectedFrames = Math.Max(1, (int)Math.Round(input.Length / channels / ratio));
            var result = new float[checked(expectedFrames * channels)];
            var buffer = new float[2048 * channels];
            int written = 0;
            int receivedTotal = 0;
            // Feed bounded blocks and drain incrementally, rather than buffering a
            // complete second copy of the processed loop inside SoundTouch.
            for (int offset = 0; offset < input.Length; offset += buffer.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                int count = Math.Min(buffer.Length, input.Length - offset);
                processor.PutSamples(input.AsSpan(offset, count), count / channels);
                Drain();
            }
            processor.Flush();
            Drain();
            cancellation.ThrowIfCancellationRequested();
            if (Math.Abs(receivedTotal - result.Length) > Math.Max(channels * 2, result.Length * 0.01))
                throw new InvalidOperationException("SoundTouch output does not match the expected loop duration.");
            // Flush rounds sample counts. A maximum of 1% padding/trimming keeps
            // every distance layer on its expected, exact frame count.
            return result;

            void Drain()
            {
                int frames;
                while ((frames = processor.ReceiveSamples(buffer.AsSpan(), 2048)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int samples = frames * channels;
                    int copy = Math.Min(samples, result.Length - written);
                    Array.Copy(buffer, 0, result, written, copy);
                    written += copy;
                    receivedTotal += samples;
                }
            }
        }
    }
}
