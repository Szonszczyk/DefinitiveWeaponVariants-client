using System.Collections.Generic;
using DefinitiveWeaponVariants.Client.Effects;

namespace DefinitiveWeaponVariants.Client.Networking
{
    internal sealed class VariantColorsPayload
    {
        internal string ServerModVersion { get; }
        internal IReadOnlyDictionary<string, RingColor> Colors { get; }

        private VariantColorsPayload(string serverModVersion, Dictionary<string, RingColor> colors)
        {
            ServerModVersion = serverModVersion;
            Colors = colors;
        }

        internal static VariantColorsPayload Parse(string json)
        {
            var entries = ColorsPayloadEnvelope.Parse(json, "variant-colors", "variantColors", out string version);

            // Validate the entire map before exposing any colors to the effect.
            return new VariantColorsPayload(version, RingColorMap.Parse(entries));
        }
    }
}
