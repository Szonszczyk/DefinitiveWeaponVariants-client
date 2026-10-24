using System;

namespace DefinitiveWeaponVariants.Client.Effects
{
    // Independent of Unity so burst-fire/retrigger behavior can be tested without the game.
    internal sealed class RingMotion
    {
        internal bool Active { get; private set; }
        internal bool Returning { get; private set; }
        internal float Progress { get; private set; }

        internal void Fire()
        {
            if (!Active)
            {
                Active = true;
                Progress = 0f;
                Returning = false;
            }
            else if (Progress > 0f)
            {
                Returning = true;
            }
        }

        internal void TakeOver(float progress)
        {
            Active = true;
            Progress = Math.Max(0f, Math.Min(1f, progress));
            Returning = Progress > 0f;
        }

        internal float SecondsUntilFinished(float duration, float returnMultiplier) => !Active ? 0f :
            Returning ? Progress * duration / returnMultiplier + duration : (1f - Progress) * duration;

        internal void Advance(float seconds, float duration, float returnMultiplier)
        {
            if (!Active || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            duration = Math.Max(0.01f, duration);
            returnMultiplier = Math.Max(1.01f, returnMultiplier);
            if (Returning)
            {
                float returnSeconds = Progress * duration / returnMultiplier;
                if (seconds < returnSeconds)
                {
                    Progress -= seconds * returnMultiplier / duration;
                    return;
                }
                seconds -= returnSeconds;
                Progress = 0f;
                Returning = false;
            }
            Progress = Math.Min(1f, Progress + seconds / duration);
            if (Progress >= 1f) Active = false;
        }

        internal void Stop()
        {
            Active = false;
            Returning = false;
            Progress = 0f;
        }
    }
}
