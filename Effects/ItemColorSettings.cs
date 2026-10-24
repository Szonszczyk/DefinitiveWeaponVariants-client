using BepInEx.Configuration;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal sealed class ItemColorSettings
    {
        internal readonly ConfigEntry<bool> Enabled;
        internal readonly ConfigEntry<float> Opacity;
        private const string CacheNotice = " Inspect previews update immediately. Cached item miniatures update only after using 'Clean Temp Files' and restarting the client.";

        internal ItemColorSettings(ConfigFile config)
        {
            const string section = "Item colors";
            Enabled = config.Bind(section, "Enabled", true,
                new ConfigDescription("Apply static server-provided colors to item inspect previews and newly generated miniatures. One color covers the whole item; two create a gradient." + CacheNotice,
                    null, new ConfigurationManagerAttributes { Order = 100 }));
            Opacity = config.Bind(section, "Opacity", 0.5f,
                new ConfigDescription("Tint strength, multiplied by the optional alpha channel in each server color. Does not make the item itself transparent." + CacheNotice,
                    new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 90, ShowRangeAsPercent = true }));
        }
    }
}
