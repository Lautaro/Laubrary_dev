using System.Collections.Generic;

namespace Laubrary.Zounds {

    /// <summary>
    /// Project-wide ZPOC values: one value per id that every play follows unless its own token has set that id. For each
    /// ZPOC in each play the value used is the token's own (or, for a track inside a Zequence, the value set on the
    /// Zequence's token), else the project-wide value, else the ZPOC's authored resting value. A token can clear its own
    /// value to go back to following the project-wide one.
    ///
    /// Runtime-only, like every ZPOC value: never saved into any Zound, and forgotten when the engine is torn down (leaving
    /// Play mode). Reached through <see cref="ZoundEngine.SetGlobalZpoc"/>.
    /// </summary>
    internal static class ZpocGlobals {

        static readonly Dictionary<string, float> values = new Dictionary<string, float>();

        internal static bool TryGet(string key, out float value) {
            if (key != null && values.TryGetValue(key, out value)) return true;
            value = 0f;
            return false;
        }

        internal static void Set(string id, float value) {
            var key = ZpocKeys.Key(id);
            if (key == null) {
                ZoundDiagnostics.Report(ZoundDiagnostics.Kind.UndeclaredGlobalZpoc, "", id ?? "",
                    "A project-wide ZPOC value was set with an empty id. Nothing was changed.");
                return;
            }
            if (!ZpocIndex.DeclaredAnywhere(key)) {
                if (!ZoundDiagnostics.Count(ZoundDiagnostics.Kind.UndeclaredGlobalZpoc, "", key))
                    ZoundDiagnostics.Report(ZoundDiagnostics.Kind.UndeclaredGlobalZpoc, "", key,
                        "A project-wide ZPOC value was set for '" + id + "', but no Zound in the project declares that id. " +
                        "It is kept, in case a Zound that declares it is added, but nothing hears it now.", id);
            }
            value = value < 0f ? 0f : value > 1f ? 1f : value;
            if (values.TryGetValue(key, out float old) && old == value) return;
            values[key] = value;
            Refresh(key);
        }

        internal static void Clear(string id) {
            var key = ZpocKeys.Key(id);
            if (key != null && values.Remove(key)) Refresh(key);
        }

        internal static void ClearAll() { values.Clear(); }

        static void Refresh(string key) {
            var tokens = ZoundEngine.LiveTokens;
            if (tokens == null) return;
            for (int i = 0; i < tokens.Count; i++) {
                var t = tokens[i];
                // A track's token follows its parent's, which forwards the change down.
                if (t != null && !t.isChildZound) t.RefreshZpoc(key);
            }
        }
    }
}
