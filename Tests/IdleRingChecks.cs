using System;
using System.Collections.Generic;
using DefinitiveWeaponVariants.Client.Effects;

internal static class IdleRingChecks
{
    private static RingAnimationOptions Options(float duration = 1f, float width = 0f, bool idle = true,
        float delay = 15f, float slowdown = 10f, float lower = 0.25f, float upper = 0.6f) =>
        new RingAnimationOptions(duration, 6f, width, idle, delay, slowdown, lower, upper);
    private static void Check(bool value) { if (!value) throw new Exception("Idle ring assertion failed."); }
    private static void Equal(float expected, float actual)
    {
        if (float.IsNaN(actual) || Math.Abs(expected - actual) > 0.0001f)
            throw new Exception($"Expected {expected}, got {actual}.");
    }

    internal static Dictionary<string, Action> Cases => new Dictionary<string, Action>
    {
        ["Idle waits after equip and enters exactly at the muzzle"] = () =>
        {
            var r = new RingAnimation(); var o = Options();
            r.Advance(14f, o); Check(!r.Active);
            r.Advance(1f, o); Check(r.Mode == RingMode.IdleEntry); Equal(0f, r.Center(o));
        },
        ["Idle entry uses slowdown and then continues into loop"] = () =>
        {
            var r = new RingAnimation(); var o = Options();
            r.Advance(15f, o); r.Advance(1f, o); Equal(0.1f, r.Center(o));
            r.Advance(2f, o); Check(r.Mode == RingMode.IdleLoop); Equal(0.3f, r.Center(o));
        },
        ["Idle bounces at both configured center limits"] = () =>
        {
            var r = new RingAnimation(); var o = Options(lower: 0.25f, upper: 0.75f, slowdown: 2f);
            r.Advance(15.5f, o); Equal(0.25f, r.Center(o));
            r.Advance(1f, o); Equal(0.75f, r.Center(o));
            r.Advance(0.5f, o); Equal(0.5f, r.Center(o));
            r.Advance(0.5f, o); Equal(0.25f, r.Center(o));
            r.Advance(0.5f, o); Equal(0.5f, r.Center(o));
        },
        ["Large idle frame matches multiple short frames across bounces"] = () =>
        {
            var big = new RingAnimation(); var small = new RingAnimation();
            var o = Options(lower: 0.25f, upper: 0.75f, slowdown: 2f);
            big.Advance(22f, o);
            for (int i = 0; i < 88; i++) small.Advance(0.25f, o);
            Check(big.Mode == small.Mode); Equal(big.Center(o), small.Center(o)); Equal(0.5f, big.Center(o));
        },
        ["Fire takes over idle without changing the visible position"] = () =>
        {
            var r = new RingAnimation(); var o = Options(width: 0.2f);
            r.Advance(18.5f, o); float before = r.Center(o);
            r.Fire(o); Check(r.Mode == RingMode.Firing && r.Returning); Equal(before, r.Center(o));
            r.Advance(0.02f, o); Check(r.Center(o) < before);
            r.Advance(0.2f, o); Check(!r.Returning && r.Mode == RingMode.Firing);
        },
        ["Fire can take over initial muzzle-to-idle movement"] = () =>
        {
            var r = new RingAnimation(); var o = Options(width: 0.08f);
            r.Advance(16f, o); Check(r.Mode == RingMode.IdleEntry);
            float before = r.Center(o); r.Fire(o); Equal(before, r.Center(o)); Check(r.Returning);
        },
        ["Repeated firing during idle takeover retains the same return"] = () =>
        {
            var r = new RingAnimation(); var o = Options(width: 0.2f);
            r.Advance(18f, o); r.Fire(o); r.Advance(0.01f, o);
            float before = r.Center(o); r.Fire(o); Equal(before, r.Center(o));
            r.Advance(0.01f, o); Check(r.Returning && r.Center(o) < before);
        },
        ["Last successful shot restarts the complete idle wait"] = () =>
        {
            var r = new RingAnimation(); var o = Options();
            r.Fire(o); r.Advance(10f, o); Check(!r.Active);
            r.Fire(o); r.Advance(1f, o); r.Advance(13f, o); Check(!r.Active);
            r.Advance(1f, o); Check(r.Mode == RingMode.IdleEntry); Equal(0, r.Center(o));
        },
        ["Firing sweep has priority even when idle delay has elapsed"] = () =>
        {
            var r = new RingAnimation(); var o = Options(delay: 0.2f);
            r.Fire(o); r.Advance(0.5f, o); Check(r.Mode == RingMode.Firing);
            r.Advance(0.7f, o); Check(r.Mode == RingMode.IdleEntry); Equal(0.02f, r.Center(o));
        },
        ["Firing disappears normally before idle becomes due"] = () =>
        {
            var r = new RingAnimation(); var o = Options();
            r.Fire(o); Equal(0, r.Center(o)); r.Advance(0.5f, o); Equal(0.5f, r.Center(o));
            r.Advance(0.5f, o); Check(!r.Active);
        },
        ["Zero delay waits for firing completion without a second ring"] = () =>
        {
            var r = new RingAnimation(); var o = Options(delay: 0);
            r.Fire(o); r.Advance(0.5f, o); Check(r.Mode == RingMode.Firing);
            r.Advance(0.5f, o); Check(r.Mode == RingMode.IdleEntry); Equal(0, r.Center(o));
        },
        ["Disabling idle hides it even while paused and restarts its wait"] = () =>
        {
            var r = new RingAnimation(); var on = Options(); var off = Options(idle: false);
            r.Advance(18f, on); Check(r.IsIdle);
            r.Advance(0f, off); Check(!r.Active);
            r.Advance(100f, off); Check(!r.Active);
            r.Advance(14f, on); Check(!r.Active);
            r.Advance(1f, on); Check(r.Mode == RingMode.IdleEntry); Equal(0, r.Center(on));
        },
        ["Idle disabled leaves existing firing behavior available"] = () =>
        {
            var r = new RingAnimation(); var o = Options(idle: false);
            r.Fire(o); r.Advance(0.4f, o); Check(r.Mode == RingMode.Firing); Equal(0.4f, r.Center(o));
            r.Fire(o); Check(r.Returning); r.Advance(20f, o); Check(!r.Active);
        },
        ["Pause and invalid frame deltas advance neither idle timer nor position"] = () =>
        {
            var r = new RingAnimation(); var o = Options();
            r.Advance(14f, o);
            r.Advance(0f, o); r.Advance(-10f, o); r.Advance(float.NaN, o); r.Advance(float.PositiveInfinity, o);
            Check(!r.Active); r.Advance(1f, o); Equal(0, r.Center(o));
            r.Advance(1f, o); float before = r.Center(o); r.Advance(0f, o); Equal(before, r.Center(o));
        },
        ["Switching or globally disabling resets activity and countdown"] = () =>
        {
            var r = new RingAnimation(); var o = Options();
            r.Advance(20f, o); Check(r.IsIdle); r.Reset(); Check(!r.Active && !r.Returning);
            r.Advance(14f, o); Check(!r.Active); r.Advance(1f, o); Equal(0, r.Center(o));
        },
        ["Live slowdown changes apply without restarting idle entry"] = () =>
        {
            var r = new RingAnimation(); var slow = Options(); var fast = Options(slowdown: 2f);
            r.Advance(16f, slow); Equal(0.1f, r.Center(slow));
            r.Advance(0.2f, fast); Equal(0.2f, r.Center(fast)); Check(r.Mode == RingMode.IdleEntry);
        },
        ["Idle speed is the firing speed divided by its multiplier"] = () =>
        {
            var idle = new RingAnimation(); var fire = new RingAnimation(); var o = Options(duration: 2f, width: 0.2f, slowdown: 5f);
            idle.Advance(15f, o); idle.Advance(1f, o);
            fire.Fire(o); float start = fire.Center(o); fire.Advance(1f, o);
            Equal((fire.Center(o) - start) / 5f, idle.Center(o));
        },
        ["Idle percentage limits apply to center independently of band width"] = () =>
        {
            var r = new RingAnimation(); var o = Options(width: 0.4f, lower: 0.25f, upper: 0.25f);
            r.Advance(25f, o); Check(r.Mode == RingMode.IdleLoop); Equal(0.25f, r.Center(o));
        },
        ["Crossed limits are normalized and equal limits remain stationary"] = () =>
        {
            var crossed = Options(lower: 0.6f, upper: 0.25f); Equal(0.25f, crossed.IdleStart); Equal(0.6f, crossed.IdleEnd);
            var r = new RingAnimation(); var equal = Options(lower: 0.5f, upper: 0.5f);
            r.Advance(30f, equal); Equal(0.5f, r.Center(equal)); r.Advance(10000f, equal); Equal(0.5f, r.Center(equal));
        },
        ["Changed idle limits clamp the loop into its new range"] = () =>
        {
            var r = new RingAnimation(); var o = Options(); r.Advance(18f, o);
            var narrowed = Options(lower: 0.4f, upper: 0.5f); r.Advance(0.1f, narrowed);
            Check(r.Center(narrowed) >= 0.4f && r.Center(narrowed) <= 0.5f);
        },
        ["Lowering idle delay applies on the next active frame"] = () =>
        {
            var r = new RingAnimation(); r.Advance(12f, Options());
            var sooner = Options(delay: 10f); r.Advance(0.5f, sooner);
            Check(r.Mode == RingMode.IdleEntry); Equal(0.05f, r.Center(sooner));
        },
        ["Nonfinite settings fall back to safe animation defaults"] = () =>
        {
            var o = new RingAnimationOptions(float.NaN, float.PositiveInfinity, float.NaN, true,
                float.NaN, float.PositiveInfinity, float.NaN, float.NaN);
            var r = new RingAnimation(); r.Advance(16f, o); Check(r.Active);
            Check(!float.IsNaN(r.Center(o)) && !float.IsInfinity(r.Center(o)));
        }
    };
}
