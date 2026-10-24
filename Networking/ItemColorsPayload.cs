using System;
using System.Collections.Generic;
using DefinitiveWeaponVariants.Client.Effects;
using Newtonsoft.Json.Linq;

namespace DefinitiveWeaponVariants.Client.Networking
{
    internal sealed class ItemColorsPayload
    {
        internal string ServerModVersion { get; }
        internal IReadOnlyDictionary<string, ItemPalette> Colors { get; }

        private ItemColorsPayload(string serverModVersion, Dictionary<string, ItemPalette> colors)
        {
            ServerModVersion = serverModVersion;
            Colors = colors;
        }

        internal static ItemColorsPayload Parse(string json)
        {
            var entries = ColorsPayloadEnvelope.Parse(json, "item-colors", "itemColors", out string version);
            var result = new Dictionary<string, ItemPalette>(StringComparer.Ordinal);
            foreach (var entry in entries.Properties())
            {
                if (!(entry.Value is JArray colors) || colors.Count < 1 || colors.Count > 2)
                    throw new FormatException($"Item '{entry.Name}' must have a list of one or two hex colors.");
                var first = RingColorMap.ParseColor(colors[0]);
                result.Add(entry.Name, new ItemPalette(first, colors.Count == 1 ? first : RingColorMap.ParseColor(colors[1])));
            }
            return new ItemColorsPayload(version, result);
        }
    }
}
