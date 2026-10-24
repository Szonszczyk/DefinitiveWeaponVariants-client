using System;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal enum RingMode { Hidden, Firing, IdleEntry, IdleLoop }

    internal readonly struct RingAnimationOptions
    {
        internal readonly float FireDuration, ReturnMultiplier, BandWidth;
        internal readonly bool IdleEnabled;
        internal readonly float IdleDelay, IdleSlowdown, IdleStart, IdleEnd;

        internal RingAnimationOptions(float fireDuration, float returnMultiplier, float bandWidth,
            bool idleEnabled, float idleDelay, float idleSlowdown, float idleStart, float idleEnd)
        {
            FireDuration = Math.Max(0.01f, Finite(fireDuration, 0.35f));
            ReturnMultiplier = Math.Max(1.01f, Finite(returnMultiplier, 6f));
            BandWidth = Clamp(Finite(bandWidth, 0.08f), 0f, 1f);
            IdleEnabled = idleEnabled;
            IdleDelay = Math.Max(0f, Finite(idleDelay, 15f));
            IdleSlowdown = Math.Max(1.01f, Finite(idleSlowdown, 10f));
            float first = Clamp(Finite(idleStart, 0.25f), 0f, 1f);
            float second = Clamp(Finite(idleEnd, 0.6f), 0f, 1f);
            IdleStart = Math.Min(first, second);
            IdleEnd = Math.Max(first, second);
        }

        private static float Finite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        internal static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }

    // Owns the one logical ring. No renderer creation or game clock is needed here;
    // equip/disable reset it, while reloading only rebuilds the renderer surfaces.
    internal sealed class RingAnimation
    {
        private readonly RingMotion _fire = new RingMotion();
        private float _idlePosition;
        private bool _idleForward = true;
        private double _quietSeconds;
        internal RingMode Mode { get; private set; }
        internal bool Active => Mode != RingMode.Hidden;
        internal bool IsIdle => Mode == RingMode.IdleEntry || Mode == RingMode.IdleLoop;
        internal bool Returning => Mode == RingMode.Firing && _fire.Returning;

        // Fractions refer to the band center. Firing extends half a band beyond each
        // end, preserving the original sweep and allowing it to disappear completely.
        internal float Center(RingAnimationOptions options) => Mode == RingMode.Firing
            ? _fire.Progress * (1f + options.BandWidth) - options.BandWidth * 0.5f : _idlePosition;

        internal void Fire(RingAnimationOptions options)
        {
            if (IsIdle)
            {
                float progress = (_idlePosition + options.BandWidth * 0.5f) / (1f + options.BandWidth);
                _fire.TakeOver(progress);
            }
            else _fire.Fire();
            Mode = RingMode.Firing;
            _quietSeconds = 0d;
        }

        internal void Advance(float seconds, RingAnimationOptions options)
        {
            if (!options.IdleEnabled)
            {
                _quietSeconds = 0d;
                if (IsIdle) { Mode = RingMode.Hidden; _idlePosition = 0f; }
            }
            if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            if (options.IdleEnabled) _quietSeconds += seconds;
            float availableSeconds = seconds;
            if (Mode == RingMode.Firing)
            {
                float remaining = _fire.SecondsUntilFinished(options.FireDuration, options.ReturnMultiplier);
                _fire.Advance(seconds, options.FireDuration, options.ReturnMultiplier);
                if (_fire.Active) return;
                Mode = RingMode.Hidden;
                availableSeconds = Math.Max(0f, seconds - remaining);
            }
            if (!options.IdleEnabled) return;
            if (Mode == RingMode.Hidden)
            {
                if (_quietSeconds < options.IdleDelay) return;
                Mode = RingMode.IdleEntry;
                _idlePosition = 0f;
                _idleForward = true;
                availableSeconds = Math.Min(availableSeconds, (float)(_quietSeconds - options.IdleDelay));
            }
            AdvanceIdle(availableSeconds, options);
        }

        private void AdvanceIdle(float seconds, RingAnimationOptions options)
        {
            // Firing travels 1 + bandWidth normalized units in FireDuration seconds.
            // Use the same physical speed divided by the idle slowdown multiplier.
            double distance = seconds * ((1d + options.BandWidth) / options.FireDuration / options.IdleSlowdown);
            if (Mode == RingMode.IdleEntry)
            {
                double toStart = Math.Abs(options.IdleStart - _idlePosition);
                if (distance < toStart)
                {
                    _idlePosition += (float)(Math.Sign(options.IdleStart - _idlePosition) * distance);
                    return;
                }
                distance -= toStart;
                _idlePosition = options.IdleStart;
                _idleForward = true;
                Mode = RingMode.IdleLoop;
            }
            float lower = options.IdleStart, upper = options.IdleEnd;
            double span = upper - lower;
            if (span < 0.000001d) { _idlePosition = lower; return; }
            _idlePosition = RingAnimationOptions.Clamp(_idlePosition, lower, upper);
            double offset = _idlePosition - lower;
            double phase = _idleForward ? offset : 2d * span - offset;
            phase = (phase + distance) % (2d * span);
            _idleForward = phase < span;
            _idlePosition = lower + (float)(_idleForward ? phase : 2d * span - phase);
        }

        internal void Reset()
        {
            _fire.Stop();
            Mode = RingMode.Hidden;
            _idlePosition = 0f;
            _idleForward = true;
            _quietSeconds = 0d;
        }
    }
}
