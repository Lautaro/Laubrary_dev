using System.Collections.Generic;

namespace SaveGame
{
    /// <summary>Typed accessor helpers for MetadataProperties dictionaries.</summary>
    public static class MetadataExtensions
    {
        /// <summary>Returns the string value for <paramref name="key"/>, or <paramref name="fallback"/> when missing.</summary>
        public static string Get(this Dictionary<string, string> dict, string key, string fallback = null)
            => dict != null && dict.TryGetValue(key, out string v) ? v : fallback;

        /// <summary>Returns the int value for <paramref name="key"/>, or <paramref name="fallback"/> when missing or unparseable.</summary>
        public static int GetInt(this Dictionary<string, string> dict, string key, int fallback = 0)
            => dict != null && dict.TryGetValue(key, out string v) && int.TryParse(v, out int i) ? i : fallback;

        /// <summary>Returns the float value for <paramref name="key"/>, or <paramref name="fallback"/> when missing or unparseable.</summary>
        public static float GetFloat(this Dictionary<string, string> dict, string key, float fallback = 0f)
            => dict != null && dict.TryGetValue(key, out string v) && float.TryParse(v, out float f) ? f : fallback;

        /// <summary>Returns the bool value for <paramref name="key"/>, or <paramref name="fallback"/> when missing or unparseable.</summary>
        public static bool GetBool(this Dictionary<string, string> dict, string key, bool fallback = false)
            => dict != null && dict.TryGetValue(key, out string v) && bool.TryParse(v, out bool b) ? b : fallback;
    }
}
