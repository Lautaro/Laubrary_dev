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

        /// <summary>
        /// Tears every playing sound down through the graph, and returns how many were torn down.
        ///
        /// Use this, not <see cref="StopAll"/>, before anything that FREES what sounds are reading. Stopping is a
        /// request that takes effect a block later; tearing down ends with the graph having released the sound.
        /// The difference is the whole reason both exist.
        /// </summary>
        public static int DestroyAll() {
            int destroyed = 0;
            // Backwards, because tearing a sound down removes it from this list.
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) { live.RemoveAt(i); continue; }
                if (g.DestroyNow()) destroyed++;
                else if (i < live.Count && live[i] == g) live.RemoveAt(i);
            }
            return destroyed;
        }

        /// <summary>
        /// Delivers one live parameter change to every voice currently playing <paramref name="zound"/>, and
        /// returns how many received it. Zero is a normal answer — it means nothing is playing that sound right
        /// now, so there is nothing to update and the change simply applies the next time it plays.
        ///
        /// The change is addressed by which effect in the chain and which of its parameters, because that is what
        /// an editor knows. Turning that into the single flat position the engine uses is done per voice, from the
        /// layout THAT VOICE started with — deliberately not from the sound's current layout, which an edit may
        /// already have rebuilt underneath us. Using the wrong one would move the right value into the wrong slot.
        ///
        /// A parameter belonging to the source stage rather than to an effect is addressed with an effect index of
        /// -1, matching how bindings refer to it.
        /// </summary>
        public static int PushLiveParam(Zound zound, int nodeIndex, int paramIndex, float value) {
            if (zound == null) return 0;
            int delivered = 0;
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) { live.RemoveAt(i); continue; }
                if (!ReferenceEquals(g.playingZound, zound)) continue;
                if (!g.IsPlaying) continue;

                int flat = FlatIndexOf(g.playingLayout, nodeIndex, paramIndex);
                if (flat < 0) continue;
                if (g.SetParameterLive(flat, value)) delivered++;
            }
            return delivered;
        }

        /// <summary>
        /// Where an effect's parameter sits in the flat parameter block, or -1 when the pair does not name anything
        /// in this layout (an effect that has since been removed, for instance).
        /// </summary>
        public static int FlatIndexOf(ChainLayout layout, int nodeIndex, int paramIndex) {
            if (layout == null || paramIndex < 0) return -1;
            // The source stage's own parameters sit at the very start of the block, before any effect's.
            if (nodeIndex < 0) return paramIndex < SourceStageParam.Count ? paramIndex : -1;
            if (nodeIndex >= layout.nodeCount) return -1;
            if (paramIndex >= layout.paramCountOf[nodeIndex]) return -1;
            return layout.paramOffset[nodeIndex] + paramIndex;
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
