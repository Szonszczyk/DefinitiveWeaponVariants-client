using BepInEx.Configuration;

namespace DefinitiveWeaponVariants.Client.Effects
{
    // Configuration Manager inspects these tag fields by name, as in UIFixes.
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? IsAdvanced;
        public bool? ShowRangeAsPercent;
    }

    internal sealed class RingSettings
    {
        internal readonly ConfigEntry<bool> Enabled, IncludeAttachments, DebugLogging, IdleEnabled;
        internal readonly ConfigEntry<float> Duration, ReturnMultiplier, Width, Opacity;
        internal readonly ConfigEntry<float> IdleDelay, IdleSlowdown, IdleStart, IdleEnd;

        internal RingAnimationOptions AnimationOptions => new RingAnimationOptions(Duration.Value, ReturnMultiplier.Value,
            Width.Value, IdleEnabled.Value, IdleDelay.Value, IdleSlowdown.Value, IdleStart.Value, IdleEnd.Value);

        internal RingSettings(ConfigFile config)
        {
            const string section = "Variant firing ring";
            Enabled = config.Bind(section, "Enabled", true,
                Description("Enable firing and idle rings on your equipped first-person weapon. Weapon colors are supplied by the DWV server mod at game startup.", 100));
            Duration = config.Bind(section, "Sweep duration (seconds)", 0.35f,
                Description("Time for one muzzle-to-stock sweep. Changes apply immediately.", 90, new AcceptableValueRange<float>(0.08f, 2f)));
            ReturnMultiplier = config.Bind(section, "Return speed multiplier", 6f,
                Description("Firing again sends the existing ring back to the muzzle this many times faster, then starts its forward sweep. No second ring is created.", 80, new AcceptableValueRange<float>(1.1f, 20f)));
            Width = config.Bind(section, "Band width", 0.08f,
                Description("Band width as a fraction of the assembled weapon length.", 70, new AcceptableValueRange<float>(0.01f, 0.4f), true));
            Opacity = config.Bind(section, "Opacity", 0.3f,
                Description("Overall opacity, multiplied by the optional alpha channel in the server-provided color.", 60, new AcceptableValueRange<float>(0f, 1f), true));
            IncludeAttachments = config.Bind(section, "Include attachments", true,
                Description("Include opaque attachment bodies in both modes. Lenses, reticles and transparent/effect materials are excluded. Changing this rebuilds the overlay without resetting the idle timer.", 50));
            DebugLogging = config.Bind(section, "Debug logging", false,
                new ConfigDescription("Log weapon template IDs, selected shader, mesh coverage and skipped meshes to BepInEx/LogOutput.log.", null,
                    new ConfigurationManagerAttributes { Order = 0, IsAdvanced = true }));

            const string idleSection = "Variant idle ring";
            IdleEnabled = config.Bind(idleSection, "Enabled", true,
                Description("Show the same ring after a period without firing. Uses the firing ring's color, width, opacity and attachment settings. Re-enabling starts a fresh countdown.", 100));
            IdleDelay = config.Bind(idleSection, "Delay after firing (seconds)", 15f,
                Description("Seconds since the last successful shot, or since equipping an eligible weapon. Reloading and aiming do not reset this timer. Idle waits for any firing sweep to finish.", 90, new AcceptableValueRange<float>(0f, 300f)));
            IdleSlowdown = config.Bind(idleSection, "Slowdown multiplier", 10f,
                Description("Idle movement is this many times slower than the firing sweep. Also controls the initial movement from the muzzle to the lower idle limit.", 80, new AcceptableValueRange<float>(1.1f, 100f)));
            IdleStart = config.Bind(idleSection, "Lower travel limit", 0.25f,
                Description("Idle ring center's lower limit: 0% is the muzzle, 100% the end of the stock. The ring enters here from the muzzle. Crossed limits are automatically ordered; equal limits hold the ring still.", 70, new AcceptableValueRange<float>(0f, 1f), true));
            IdleEnd = config.Bind(idleSection, "Upper travel limit", 0.6f,
                Description("Idle ring center's upper limit. It moves back and forth between the two limits until firing resumes. Shared band width may extend beyond the center limits.", 60, new AcceptableValueRange<float>(0f, 1f), true));
        }

        private static ConfigDescription Description(string text, int order, AcceptableValueBase range = null, bool percent = false) =>
            new ConfigDescription(text, range, new ConfigurationManagerAttributes { Order = order, ShowRangeAsPercent = percent });
    }
}
