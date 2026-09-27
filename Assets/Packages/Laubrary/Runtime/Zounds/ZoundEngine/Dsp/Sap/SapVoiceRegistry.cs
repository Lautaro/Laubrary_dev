using System.Collections.Generic;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Every sound currently being played through the audio graph, so that something which invalidates what
    /// those sounds are made of can silence them first.
    ///
    /// **Why this has to exist.** Clearing the engine's caches throws away the decoded audio and the resolved
    /// effect chains. Nothing was stopping the sounds already playing from those, so after a cache clear the
    /// game could still be playing from data the engine believes it has dropped. With the old plugin there was
    /// a step that stopped every voice first; it went away with the plugin and this is it coming back.
    ///
    /// **Today this is a correctness nicety. Later it becomes a memory-safety requirement.** Each playing sound
    /// currently holds its own private copy of the audio it is reading, which is wasteful but makes its lifetime
    /// trivially safe: nothing is shared, so nothing can be freed out from under a reader. The intended saving is
    /// one shared copy per cached sound instead of one per voice — and at that point freeing the cache while a
    /// voice is still reading stops being an inconsistency and becomes reading freed memory. **So the sharing
    /// must not be attempted until stopping is not just requested but CONFIRMED COMPLETE, which needs a barrier
    /// this does not yet have.** Asking every voice to stop is not the same as knowing they have.
    ///
    /// Registration is deliberately forgiving. A sound may end without telling anyone, because the graph
    /// disposes it on its own thread, so entries can go stale. Every pass therefore drops entries that have
    /// gone away rather than assuming the list is exact, and nothing here treats the count as authoritative.
    /// </summary>
    public static class SapVoiceRegistry {

        private static readonly List<ZoundSapVoiceGenerator> live = new List<ZoundSapVoiceGenerator>();

        /// <summary>How many sounds are believed to be playing. Diagnostics only — see the note on staleness.</summary>
        public static int Count => live.Count;

        internal static void Register(ZoundSapVoiceGenerator generator) {
            if (generator == null) return;
            if (!live.Contains(generator)) live.Add(generator);
        }

        internal static void Unregister(ZoundSapVoiceGenerator generator) {
            if (generator == null) return;
            live.Remove(generator);
        }

        /// <summary>
        /// Asks every playing sound to stop hard, and returns how many were asked.
        ///
        /// This REQUESTS a stop; it does not wait for one. The request reaches a sound at the start of its next
        /// block, so for a moment afterwards sounds are still being rendered. That is fine for keeping the game
        /// consistent with the engine, and NOT fine as protection for freeing memory a sound is reading — see
        /// the note on this class.
        /// </summary>
        public static int StopAll() {
            int asked = 0;
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) { live.RemoveAt(i); continue; }
                if (g.StopLive()) asked++;
                else live.RemoveAt(i);   // nothing playing any more; the entry was stale
            }
            return asked;
        }

        /// <summary>Drops entries whose sound has ended or whose object has been destroyed.</summary>
        public static void Prune() {
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null || !g.IsPlaying) live.RemoveAt(i);
            }
        }
    }
}
