using System;
using Newtonsoft.Json.Linq;

namespace DefinitiveWeaponVariants.Client.Networking
{
    // Both server routes use RouterModelDWV metadata; only the data property differs.
    internal static class ColorsPayloadEnvelope
    {
        internal static JObject Parse(string json, string endpoint, string dataProperty, out string serverModVersion)
        {
            var root = JObject.Parse(json, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            var schema = root["schemaVersion"];
            if (schema == null || schema.Type != JTokenType.Integer || schema.Value<long>() != 1)
                throw new FormatException($"Unsupported or missing {endpoint} schemaVersion (expected 1).");
            var version = root["serverModVersion"];
            if (version == null || version.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)version))
                throw new FormatException($"Missing serverModVersion in {endpoint} response.");
            if (!(root[dataProperty] is JObject entries))
                throw new FormatException($"Missing {dataProperty} object in server response.");

            foreach (var entry in entries.Properties())
            {
                if (entry.Name.Length != 24)
                    throw new FormatException($"Invalid item template ID '{entry.Name}' in server response.");
                foreach (char c in entry.Name)
                    if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                        throw new FormatException($"Invalid item template ID '{entry.Name}' in server response.");
            }

            serverModVersion = (string)version;
            return entries;
        }
    }
}
