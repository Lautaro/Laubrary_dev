using System.Collections.Generic;

namespace Laubrary.Zounds {

    /// <summary>
    /// Turns a ZPOC id into the key it is matched by. The rule is exactly the one Zound names are looked up with
    /// (<see cref="ZoundDictionary.ZoundNameToKey"/>), so "Rotor Speed", "rotor-speed" and "rotorSpeed" reach the same
    /// ZPOC, and a project learns one rule rather than two.
    ///
    /// Game code calls with the same string every frame, and working the key out afresh would allocate every time (the
    /// rule lowercases and strips characters). So each distinct string is worked out once and remembered; after that a
    /// call costs one dictionary lookup on a string that already has its hash cached, and allocates nothing. Main thread
    /// only, like everything else that talks to a token.
    /// </summary>
    public static class ZpocKeys {

        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

        /// <summary>The matching key for an id; null for a null or empty id, which never matches anything.</summary>
        public static string Key(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            if (cache.TryGetValue(id, out var key)) return key;
            key = ZoundDictionary.ZoundNameToKey(id);
            if (key.Length == 0) key = null;
            // A runaway caller building ids on the fly must not grow this forever; past a generous size it starts over.
            if (cache.Count > 4096) cache.Clear();
            cache[id] = key;
            return key;
        }

        /// <summary>Whether two ids would reach the same ZPOC.</summary>
        public static bool Same(string a, string b) {
            var ka = Key(a);
            return ka != null && ka == Key(b);
        }
    }
}
