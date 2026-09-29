using System.Collections.Generic;

namespace Laubrary.Zounds {

    /// <summary>
    /// Where ZPOC ids are declared: on a modifier of a Zound's effect chain (a Code modifier, or any modifier exposed
    /// through its ZPOC id), and on a Zequence track. An id belongs to the Zound that declares it and only has to be unique
    /// there; a call on a play reaches every declaration anywhere in the tree that play plays, so sibling Klips sharing an
    /// id are reached together on purpose.
    ///
    /// These answers depend only on authored data, never on what happens to be playing, which is what lets a request for
    /// a part that is not sounding right now (the track a random Zequence did not pick, a track still waiting out its
    /// delay) count as found rather than as a typo.
    /// </summary>
    public static class ZpocIndex {

        /// <summary>Whether <paramref name="zound"/>, or anything it plays at any depth, declares <paramref name="key"/>
        /// (a key from <see cref="ZpocKeys.Key"/>). Walks the authored tree without allocating.</summary>
        public static bool Declares(Zound zound, string key) {
            if (zound == null || key == null) return false;
            return Declares(zound, key, 0);
        }

        static bool Declares(Zound zound, string key, int depth) {
            if (zound == null || depth > 16) return false;   // a cycle would be a data error elsewhere; never recurse forever
            if (ChainDeclares(Dsp.ZoundDspPlayback.ResolveChain(zound, out _), key)) return true;
            if (zound is CompositeZound composite && composite.zoundEntries != null) {
                var entries = composite.zoundEntries;
                for (int i = 0; i < entries.Count; i++) {
                    var e = entries[i];
                    if (e == null) continue;
                    if (!string.IsNullOrEmpty(e.zpocId) && ZpocKeys.Key(e.zpocId) == key) return true;
                    if (composite.TryGetEntryZound(e, out var child) && Declares(child, key, depth + 1)) return true;
                }
            }
            return false;
        }

        static bool ChainDeclares(ZoundEffectChain chain, string key) {
            if (chain?.modifiers == null) return false;
            for (int m = 0; m < chain.modifiers.Count; m++) {
                var mod = chain.modifiers[m];
                if (mod != null && mod.HasZpoc && ZpocKeys.Key(mod.zpocId) == key) return true;
            }
            return false;
        }

        // Whole-project answers, for project-wide values: worked out once per key and remembered until an edit.
        static readonly Dictionary<string, bool> anywhere = new Dictionary<string, bool>();

        /// <summary>Whether any Zound in the project declares <paramref name="key"/>.</summary>
        public static bool DeclaredAnywhere(string key) {
            if (key == null) return false;
            if (anywhere.TryGetValue(key, out bool known)) return known;
            bool found = false;
            var project = ZoundsProject.Instance;
            if (project != null && project.zoundLibrary != null) {
                foreach (var z in project.zoundLibrary.GetAllZounds()) {
                    if (ChainDeclares(Dsp.ZoundDspPlayback.ResolveChain(z, out _), key)) { found = true; break; }
                    if (z is CompositeZound c && c.zoundEntries != null) {
                        foreach (var e in c.zoundEntries)
                            if (e != null && !string.IsNullOrEmpty(e.zpocId) && ZpocKeys.Key(e.zpocId) == key) { found = true; break; }
                        if (found) break;
                    }
                }
            }
            anywhere[key] = found;
            return found;
        }

        /// <summary>Forgets remembered whole-project answers; called whenever an edit may have declared or removed an id.</summary>
        public static void Invalidate() { anywhere.Clear(); }
    }
}
