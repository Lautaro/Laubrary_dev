using System.Collections.Generic;
using UnityEngine;

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
    /// voice is still reading stops being an inconsistency and becomes reading freed memory. Asking every voice to
    /// stop is not the same as knowing they have.
    ///
    /// **That barrier now exists: <see cref="DestroyAllAndConfirm"/>.** It tears every playing sound down and then
    /// waits until the audio side has been observed to stop rendering, and it reports whether it actually saw that
    /// happen rather than assuming it. Three points about it are worth knowing before relying on it:
    ///
    /// - It is an OBSERVATION, not a promise extracted from the graph. Each voice bumps a counter on its way into
    ///   and out of every block, in memory the voice does not own so that it outlives the voice; quiet means that
    ///   counter has stopped moving and is not mid-block. Nothing here reasons about what the graph's teardown
    ///   guarantees, because on this branch confident reasoning about this engine has been wrong three times and
    ///   each time only measurement caught it.
    /// - It can fail, and says so. A caller freeing memory must treat "not confirmed" as "do not free yet", not as
    ///   a warning to log and carry on past.
    /// - It costs a wait of a couple of audio blocks, paid once per call rather than per voice, and only at the rare
    ///   moments something invalidates the library. It is not on any playback path.
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
        /// Tears every playing sound down, and returns how many were torn down. Whether they were all actually seen to
        /// stop is discarded — use <see cref="DestroyAllAndConfirm"/> when anything is about to be freed.
        /// </summary>
        public static int DestroyAll() => DestroyAllAndConfirm(out _);

        // ───────────────────────── confirmed stopped, not merely asked ─────────────────────────

        /// <summary>
        /// How long a voice has to have been between blocks before it counts as finished, by default: a little over two
        /// of the audio output's own blocks.
        ///
        /// **Why it is measured in blocks rather than being a fixed number of milliseconds.** The only thing that can
        /// make the counter move again is another block starting, and blocks arrive one output buffer apart. Waiting
        /// two of them means a block would have had to start and be missed twice over. A fixed millisecond figure
        /// would be either wasteful on a small buffer or unsafe on a large one, and buffer size is a user setting.
        ///
        /// Clamped at both ends so that an unreported or absurd buffer size cannot turn this into either a
        /// meaningless zero wait or a visible stall.
        /// </summary>
        public static double DefaultSettleSeconds {
            get {
                AudioSettings.GetDSPBufferSize(out int bufferLength, out int _);
                int rate = AudioSettings.outputSampleRate;
                double block = rate > 0 && bufferLength > 0 ? (double)bufferLength / rate : 0.021d;
                double settle = block * 2.2d;
                if (settle < 0.005d) settle = 0.005d;
                if (settle > 0.060d) settle = 0.060d;
                return settle;
            }
        }

        /// <summary>
        /// Tears every playing sound down and then waits until none of them is rendering any more, reporting through
        /// <paramref name="confirmed"/> whether that was actually observed. Returns how many were torn down.
        ///
        /// **This is the call to use before freeing anything a sound might be reading.** <see cref="DestroyAll"/> asks
        /// the graph to let go and returns; this one additionally watches until the audio side has visibly stopped.
        /// The distinction is small in the ordinary case and total in the bad one, and there is no way to tell which
        /// case you are in without looking.
        ///
        /// A false <paramref name="confirmed"/> means one of two things, neither of which is safe to free past: a
        /// voice is still being rendered after being torn down, or one stopped in the middle of a block and never
        /// came out. Either way the honest response is to keep the memory.
        /// </summary>
        public static int DestroyAllAndConfirm(out bool confirmed, double settleSeconds = 0d,
                                               double timeoutSeconds = 0.5d) {
            // Phase one: silence everything, so that nothing is still pulling on a sound that is about to go away.
            // Snapshotted as we go, because destroying a sound removes it from this list and the question afterwards is
            // precisely about the ones that were removed.
            snapshot.Clear();
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) { live.RemoveAt(i); continue; }
                if (g.SilenceForTeardown()) snapshot.Add(g);
                else live.RemoveAt(i);   // nothing playing any more; the entry was stale
            }

            // Phase two: ONE wait for all of them. Waiting per sound would cost a settle window each, so a cache clear
            // with twenty sounds playing would stall for twenty of them instead of one.
            confirmed = WaitUntilQuiet(snapshot, settleSeconds, timeoutSeconds);

            // Phase three: destroy, now that nothing is reading. A refusal is honoured for every sound rather than
            // sound by sound, because the wait cannot say WHICH one is still going, only that one of them is — and
            // destroying the innocent ones would not make the guilty one safe.
            int destroyed = 0;
            for (int i = 0; i < snapshot.Count; i++) {
                if (confirmed) { if (snapshot[i].DestroyAfterQuiet()) destroyed++; }
                else snapshot[i].WarnNotQuiet();
            }
            snapshot.Clear();
            return destroyed;
        }

        /// <summary>
        /// Waits until every voice in <paramref name="voices"/> has stopped rendering, or until the timeout. True means
        /// quiet was observed; false means it was not, within the time allowed.
        ///
        /// **What "quiet" means and why one reading is never enough.** Each voice's counter is odd while a block is
        /// running and even between blocks, so a single even reading only says no block was running at that instant —
        /// another could start immediately after. Quiet therefore requires the counters to be even AND unchanged for a
        /// settle window, which is long enough that a block would have had to begin within it if any were still coming.
        ///
        /// **It sleeps rather than spins, and that is not a detail.** An earlier measurement on this engine concluded
        /// nothing was being rendered when in fact it had held the main thread at full tilt and starved the thing it
        /// was watching. Sleeping in short slices leaves the machine free to carry on, so what is observed is the
        /// engine's behaviour rather than the observer's interference. Whether the audio side runs at all while the
        /// main thread sleeps is itself something the kept check measures, because the barrier would be worthless if
        /// waiting were what caused the quiet.
        /// </summary>
        public static bool WaitUntilQuiet(List<ZoundSapVoiceGenerator> voices, double settleSeconds = 0d,
                                          double timeoutSeconds = 0.5d) {
            if (voices == null || voices.Count == 0) return true;
            if (settleSeconds <= 0d) settleSeconds = DefaultSettleSeconds;

            int n = voices.Count;
            var last = new long[n];
            for (int i = 0; i < n; i++) last[i] = TicketOf(voices[i]);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            double quietSince = -1d;

            while (true) {
                bool still = true;
                for (int i = 0; i < n; i++) {
                    long now = TicketOf(voices[i]);
                    // Moved, or caught mid-block: either way this is not quiet, and the new reading becomes the
                    // baseline the settle window is measured from.
                    if (now != last[i] || (now & 1L) != 0L) { still = false; last[i] = now; }
                }

                double elapsed = clock.Elapsed.TotalSeconds;
                if (!still) quietSince = -1d;
                else if (quietSince < 0d) quietSince = elapsed;
                else if (elapsed - quietSince >= settleSeconds) return true;

                if (elapsed >= timeoutSeconds) return false;
                System.Threading.Thread.Sleep(1);
            }
        }

        /// <summary>Waits on a single voice. Same meaning, same caveats.</summary>
        public static bool WaitUntilQuiet(ZoundSapVoiceGenerator voice, double settleSeconds = 0d,
                                          double timeoutSeconds = 0.5d) {
            if (voice == null) return true;
            var one = new List<ZoundSapVoiceGenerator>(1) { voice };
            return WaitUntilQuiet(one, settleSeconds, timeoutSeconds);
        }

        /// <summary>
        /// A voice's block counter, with "never started one" reported as a quiet, unchanging zero rather than as the
        /// -1 the component uses to mean "nothing to report" — which, being odd, would otherwise read as permanently
        /// mid-block and make every wait time out.
        /// </summary>
        private static long TicketOf(ZoundSapVoiceGenerator generator) {
            if (generator == null) return 0L;
            long ticket = generator.RenderTicket;
            return ticket < 0L ? 0L : ticket;
        }

        /// <summary>
        /// The voices a teardown-and-confirm is watching. Reused rather than allocated per call, because the call
        /// happens at moments when the editor is already doing something expensive and adding garbage to it is free
        /// to avoid.
        /// </summary>
        private static readonly List<ZoundSapVoiceGenerator> snapshot = new List<ZoundSapVoiceGenerator>();

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

        /// <summary>Delivers a new authored speed to every voice playing <paramref name="zound"/> with live speed (T-0409).</summary>
        public static int PushAuthoredSpeed(Zound zound, float speed) {
            if (zound == null) return 0;
            int delivered = 0;
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) { live.RemoveAt(i); continue; }
                if (!ReferenceEquals(g.playingZound, zound) || !g.IsPlaying) continue;
                if (g.SetAuthoredSpeedLive(speed)) delivered++;
            }
            return delivered;
        }

        /// <summary>Re-sends every live-speed voice its combined speed, after the global speed changed.</summary>
        public static int RefreshAllSpeeds() {
            int delivered = 0;
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) { live.RemoveAt(i); continue; }
                if (g.HasLiveSpeed && g.IsPlaying && g.RefreshSpeed()) delivered++;
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

        /// <summary>
        /// The value a playing voice is currently using for one of <paramref name="zound"/>'s effect parameters, after its
        /// modifiers. False means nothing is playing that sound, which is the normal answer most of the time.
        ///
        /// Addressed the same way a live change is, and translated through the layout THAT VOICE started with for the same
        /// reason: an edit may already have rebuilt the sound's current layout, and the two can disagree about where a
        /// parameter sits.
        ///
        /// When several voices of the same sound overlap this answers with the newest, because a display can only show one
        /// number and the newest is the one whose start the listener just heard. It is a display aid, so "one of them,
        /// predictably chosen" is the right trade rather than refusing to answer.
        /// </summary>
        public static bool TryReadLiveParam(Zound zound, int nodeIndex, int paramIndex, out float value) {
            value = 0f;
            if (zound == null) return false;
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) { live.RemoveAt(i); continue; }
                if (!ReferenceEquals(g.playingZound, zound)) continue;
                if (!g.IsPlaying) continue;

                int flat = FlatIndexOf(g.playingLayout, nodeIndex, paramIndex);
                if (flat < 0) continue;
                if (g.TryReadLiveParam(flat, out value)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether any voice is currently playing <paramref name="zound"/>. Cheap enough to ask every editor tick, which is
        /// what it is for: a window has to know a sound STARTED before it can know to keep redrawing, and it cannot learn
        /// that from its own previous drawing, which may well have happened a moment before the sound began.
        /// </summary>
        public static bool IsPlaying(Zound zound) {
            if (zound == null) return false;
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) { live.RemoveAt(i); continue; }
                if (ReferenceEquals(g.playingZound, zound) && g.IsPlaying) return true;
            }
            return false;
        }

        /// <summary>
        /// How far the most recently started play of <paramref name="zound"/> has got: seconds since it started, and the
        /// play's length. False when nothing is playing it. When several plays overlap, the newest one answers, because
        /// that is the one the listener just triggered.
        /// </summary>
        public static bool TryReadPlayPosition(Zound zound, out float elapsedSeconds, out float durationSeconds) {
            elapsedSeconds = 0f; durationSeconds = 0f;
            if (zound == null) return false;
            ZoundSapVoiceGenerator newest = null;
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) { live.RemoveAt(i); continue; }
                if (!ReferenceEquals(g.playingZound, zound) || !g.IsPlaying) continue;
                if (newest == null || g.playStartedAt > newest.playStartedAt) newest = g;
            }
            if (newest == null) return false;
            // The play's own clock (frames the engine has rendered) when there is one; the wall clock since the play was
            // handed over only as a fallback. They differ by however long the audio took to start, and by up to a block.
            elapsedSeconds = newest.TryReadRenderedSeconds(out double rendered)
                ? (float)rendered
                : (float)(UnityEngine.Time.realtimeSinceStartupAsDouble - newest.playStartedAt);
            durationSeconds = newest.playDuration;
            return true;
        }

        /// <summary>
        /// When the newest play of <paramref name="zound"/> was started, on the real-time clock — the moment its modifiers
        /// joined their running clocks. False when nothing is playing it.
        /// </summary>
        public static bool TryReadPlayStart(Zound zound, out double startedAt) {
            startedAt = 0d;
            if (zound == null) return false;
            ZoundSapVoiceGenerator newest = null;
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) continue;
                if (!ReferenceEquals(g.playingZound, zound) || !g.IsPlaying) continue;
                if (newest == null || g.playStartedAt > newest.playStartedAt) newest = g;
            }
            if (newest == null) return false;
            startedAt = newest.playStartedAt;
            return true;
        }

        /// <summary>The live output of one modifier on the newest play of <paramref name="zound"/>, for a display.</summary>
        public static bool TryReadLiveModifier(Zound zound, int modifierIndex, out float value) {
            value = 0f;
            if (zound == null) return false;
            ZoundSapVoiceGenerator newest = null;
            for (int i = live.Count - 1; i >= 0; i--) {
                var g = live[i];
                if (g == null) continue;
                if (!ReferenceEquals(g.playingZound, zound) || !g.IsPlaying) continue;
                if (newest == null || g.playStartedAt > newest.playStartedAt) newest = g;
            }
            return newest != null && newest.TryReadLiveModifier(modifierIndex, out value);
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
