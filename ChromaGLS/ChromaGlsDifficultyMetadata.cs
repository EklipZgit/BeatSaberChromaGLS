using System;
using System.Collections.Generic;
using CustomJSONData.CustomBeatmap;

namespace ChromaGLS
{
    internal static class ChromaGlsDifficultyMetadata
    {
        private const string ChromaGls = "ChromaGLS";

        internal static bool OptsIntoChromaGls(CustomData? data, bool includeUnprefixedKeys)
        {
            return ContainsCapability(data, "_requirements", ChromaGls)
                || ContainsCapability(data, "_suggestions", ChromaGls)
                || (includeUnprefixedKeys && (ContainsCapability(data, "requirements", ChromaGls)
                    || ContainsCapability(data, "suggestions", ChromaGls)));
        }

        internal static bool ContainsCapability(CustomData? data, string key, string capability)
        {
            if (data == null || !data.TryGetValue(key, out object? value) || value is not IEnumerable<object> entries)
            {
                return false;
            }

            // CustomJSONData reads JSON arrays as lists. Index them without boxing an enumerator.
            if (entries is IReadOnlyList<object> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (Matches(list[i], capability))
                    {
                        return true;
                    }
                }

                return false;
            }

            foreach (object? entry in entries)
            {
                if (Matches(entry, capability))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Matches(object? entry, string capability) =>
            entry is string name && string.Equals(name, capability, StringComparison.OrdinalIgnoreCase);
    }
}
