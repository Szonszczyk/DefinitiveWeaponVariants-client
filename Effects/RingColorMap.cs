using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal readonly struct RingColor
    {
        internal readonly byte R, G, B, A;
        internal RingColor(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
    }

    internal static class RingColorMap
    {
        internal static Dictionary<string, RingColor> Parse(string json)
        {
            return Parse(JObject.Parse(json, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            }));
        }

        internal static Dictionary<string, RingColor> Parse(JObject entries)
        {
            var result = new Dictionary<string, RingColor>(StringComparer.Ordinal);
            foreach (var entry in entries.Properties())
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name != entry.Name.Trim())
                    throw new FormatException("Weapon IDs must be nonempty and have no surrounding whitespace.");
                result.Add(entry.Name, ParseColor(entry.Value));
            }
            return result;
        }

        internal static RingColor ParseColor(JToken value)
        {
            string hex = value.Type == JTokenType.String ? (string)value : null;
            if (hex == null || (hex.Length != 7 && hex.Length != 9) || hex[0] != '#')
                throw new FormatException("Colors must be #RRGGBB or #RRGGBBAA.");
            byte Read(int offset) => byte.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new RingColor(Read(1), Read(3), Read(5), hex.Length == 9 ? Read(7) : (byte)255);
        }
    }
}
