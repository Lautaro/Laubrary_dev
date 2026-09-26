using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Forensics for "machine gun" retriggers: every play of a zound is timestamped per zound; when a
    /// zound is played <see cref="BurstCount"/> or more times inside <see cref="WindowSeconds"/> the
    /// burst is classified by where each play came from (the call stack, captured only once a burst is
    /// suspected) and a full engine dump goes to the log — voices, groups, repeat state, buses, and the
    /// origin of every play in the burst. A human mashing a preview button is not a fault: a burst whose
    /// plays all come from editor input (an IMGUI event path) is only logged when it is faster than a
    /// human can press (<see cref="HumanLimitCount"/> in the window). Everything else in a burst — a play
    /// issued from a callback, a timer, another zound's completion, a handler — is logged as suspicious.
    /// Off the hot path: one dictionary lookup and a ring write per play; stacks are captured only while
    /// a burst is suspected.
    /// </summary>
    public static class ZoundTriggerWatch {

        public const int BurstCount = 4;          // plays of one zound inside the window that make a burst
        public const float WindowSeconds = 1f;
        public const int HumanLimitCount = 8;     // more editor-input plays than this per window cannot be a human
        private const int RING = 16;
        private const float REPORT_COOLDOWN = 3f; // one dump per zound per burst, not one per play

        public static bool enabled = true;
        /// <summary>Number of bursts reported since startup (probes read it).</summary>
        public static int burstsReported;

        private class History {
            public readonly float[] times = new float[RING];
            public readonly string[] origins = new string[RING];
            public readonly bool[] fromInput = new bool[RING];
            public readonly long[] tokenIds = new long[RING];
            public readonly string[] zoundNames = new string[RING];
            public int head, count;
            public float lastReportAt = -100f;
        }

        private static readonly Dictionary<Zound, History> histories = new Dictionary<Zound, History>();

        // ── main-thread timeline: engine update gaps (a stall shows as one big gap followed by catch-up) ──
        private const int GAPS = 24;
        private static readonly float[] gapAt = new float[GAPS];
        private static readonly float[] gapLen = new float[GAPS];
        private static int gapHead, gapCount;
        private static float lastUpdateAt = -1f;
        private static int lastGc0, lastGc1, lastGc2;
        public const float StallSeconds = 0.3f;   // an update gap this long counts as a main-thread stall
        public const float CatchUpSeconds = 0.5f; // plays this soon after a stall are treated as its catch-up burst

        /// <summary>Called once per engine update (any mode) so a burst can be placed against the main thread's own timeline.</summary>
        internal static void OnEngineUpdate() {
            float now = Time.realtimeSinceStartup;
            if (lastUpdateAt >= 0f) {
                gapAt[gapHead] = now; gapLen[gapHead] = now - lastUpdateAt;
                gapHead = (gapHead + 1) % GAPS; if (gapCount < GAPS) gapCount++;
            }
            lastUpdateAt = now;
        }

        private static float LastStallAge(float now, out float stallLen) {
            stallLen = 0f; float best = float.MaxValue;
            for (int n = 0; n < gapCount; n++) {
                int k = (gapHead - 1 - n + GAPS) % GAPS;
                if (gapLen[k] >= StallSeconds) { float age = now - gapAt[k]; if (age < best) { best = age; stallLen = gapLen[k]; } }
            }
            return best;
        }

        internal static void AppendMainThreadTimeline(StringBuilder sb, float now) {
            sb.Append("main thread: engine update gaps, newest first (s ago: gap) —");
            for (int n = 0; n < gapCount && n < 12; n++) {
                int k = (gapHead - 1 - n + GAPS) % GAPS;
                sb.Append(' ').Append((now - gapAt[k]).ToString("F2")).Append(':').Append((gapLen[k] * 1000f).ToString("F0")).Append("ms");
            }
            float stallAge = LastStallAge(now, out float stallLen);
            sb.Append("\n  last stall (gap ≥ ").Append(StallSeconds).Append(" s): ").Append(stallAge == float.MaxValue ? "none in the window" : stallLen.ToString("F3") + " s long, " + stallAge.ToString("F2") + " s before this burst");
            int g0 = System.GC.CollectionCount(0), g1 = System.GC.CollectionCount(1), g2 = System.GC.CollectionCount(2);
            sb.Append("\n  GC collections since the previous report: gen0 +").Append(g0 - lastGc0).Append(" gen1 +").Append(g1 - lastGc1).Append(" gen2 +").Append(g2 - lastGc2);
            lastGc0 = g0; lastGc1 = g1; lastGc2 = g2;
#if UNITY_EDITOR
            sb.Append("\n  editor: isCompiling=").Append(UnityEditor.EditorApplication.isCompiling).Append(" isUpdating=").Append(UnityEditor.EditorApplication.isUpdating)
              .Append(" isPlaying=").Append(Application.isPlaying).Append(" timeSinceStartup=").Append(UnityEditor.EditorApplication.timeSinceStartup.ToString("F2"));
#endif
            sb.Append('\n');
        }

        // A burst can be spread over several zounds that share one sound file (a Zequence's local
        // klips, duplicates of a coin), which the per-zound history never sees: track the file too.
        private static readonly Dictionary<string, History> clipHistories = new Dictionary<string, History>();
        private static readonly Dictionary<string, Zound> clipRepresentative = new Dictionary<string, Zound>();

        private static string ClipKey(Zound zound) {
            if (zound is Klip klip) return klip.GetAudioClipPath();
            if (zound is ClipZound cz) return cz.audioClip != null ? cz.audioClip.name : null;
            return null;
        }

        /// <summary>Called by ZoundEngine.PlayZound for every play that produced a token (children included).</summary>
        internal static void OnPlay(Zound zound, ZoundToken token, in ZoundArgs args) {
            if (!enabled || zound == null) return;
            if (!histories.TryGetValue(zound, out var h)) { h = new History(); histories[zound] = h; }
            Track(zound, h, token, in args, null);
            string clipKey = ClipKey(zound);
            if (!string.IsNullOrEmpty(clipKey)) {
                if (!clipHistories.TryGetValue(clipKey, out var ch)) { ch = new History(); clipHistories[clipKey] = ch; clipRepresentative[clipKey] = zound; }
                if (ch != h) Track(zound, ch, token, in args, clipKey);
            }
        }

        private static void Track(Zound zound, History h, ZoundToken token, in ZoundArgs args, string clipKey) {
            float now = Time.realtimeSinceStartup;

            int recent = CountRecent(h, now);
            // The origin is captured only while a burst is forming (from the second play in the window on):
            // a stack walk allocates tens of KB, and in the editor every allocation brings the next full
            // collection closer — the collections that stall the audio callback (see ZoundGcGuard).
            string origin = null; bool fromInput = false;
            if (recent >= 1) {
                var st = new System.Diagnostics.StackTrace(2, false);
                origin = Origin(st, out fromInput);
            }
            int i = h.head;
            h.times[i] = now; h.origins[i] = origin; h.fromInput[i] = fromInput; h.tokenIds[i] = token != null ? token.GetHashCode() : 0; h.zoundNames[i] = clipKey != null ? zound.name : null;
            h.head = (i + 1) % RING; if (h.count < RING) h.count++;

            recent++;
            if (recent < BurstCount) return;
            if (now - h.lastReportAt < REPORT_COOLDOWN) return;

            int inputPlays = 0, otherPlays = 0;
            ForEachRecent(h, now, k => { if (h.origins[k] == null || h.fromInput[k]) inputPlays++; else otherPlays++; });
            // Input-only bursts a human could have produced are fine — unless they land right after a
            // main-thread stall, where queued events draining in one frame is the very thing being hunted.
            float stallAge = LastStallAge(now, out _);
            bool afterStall = stallAge <= CatchUpSeconds;
            bool humanMashing = otherPlays == 0 && recent <= HumanLimitCount && !afterStall;
            if (humanMashing) return;

            h.lastReportAt = now;
            burstsReported++;
            Debug.LogWarning(BuildReport(zound, h, now, recent, inputPlays, otherPlays, afterStall, args, clipKey));
        }

        private static int CountRecent(History h, float now) {
            int c = 0;
            ForEachRecent(h, now, _ => c++);
            return c;
        }

        private static void ForEachRecent(History h, float now, System.Action<int> visit) {
            for (int n = 0; n < h.count; n++) {
                int k = (h.head - 1 - n + RING) % RING;
                if (now - h.times[k] > WindowSeconds) break;
                visit(k);
            }
        }

        // The first frame outside the engine's own play path, plus whether an editor input event is on the stack.
        private static string Origin(System.Diagnostics.StackTrace st, out bool fromInput) {
            fromInput = false;
            string first = null;
            for (int f = 0; f < st.FrameCount; f++) {
                var m = st.GetFrame(f).GetMethod();
                if (m == null) continue;
                string type = m.DeclaringType != null ? m.DeclaringType.FullName : "?";
                string name = m.Name;
                if (type.Contains("ZoundTriggerWatch") || (type.EndsWith("ZoundEngine") && name == "PlayZound")) continue;
                if (first == null) first = type + "." + name;
                if (name == "OnGUI" || name == "OnZUI" || type.Contains("GUIUtility") || type.Contains("EditorWindow") || type.Contains("HostView") || type.Contains("DockArea")) fromInput = true;
            }
            return first ?? "(unknown)";
        }

        private static string BuildReport(Zound zound, History h, float now, int recent, int inputPlays, int otherPlays, bool afterStall, in ZoundArgs args, string clipKey) {
            var sb = new StringBuilder();
            sb.Append("[Zounds] RETRIGGER BURST: ");
            if (clipKey != null) sb.Append("sound file '").Append(System.IO.Path.GetFileName(clipKey)).Append("' (last through '").Append(zound.name).Append("')");
            else sb.Append('\'').Append(zound.name).Append('\'');
            sb.Append(" played ").Append(recent).Append("× within ").Append(WindowSeconds).Append(" s (")
              .Append(inputPlays).Append(" from editor input, ").Append(otherPlays).Append(" from code").Append(afterStall ? ", right after a main-thread stall" : "").Append(") at t=").Append(now.ToString("F3")).Append('\n');
            AppendMainThreadTimeline(sb, now);
            sb.Append("last play args: startImmediately=").Append(args.startImmediately).Append(" delay=").Append(args.delay).Append(" isChild=").Append(args.isChild)
              .Append(" repeatEntry=").Append(args.repeatEntry != null ? (args.repeatEntry.repeatEnabled ? "on count=" + args.repeatEntry.repeatCount + " interval=" + args.repeatEntry.repeatInterval : "off") : "none").Append('\n');
            sb.Append("plays in the window (newest first):\n");
            ForEachRecent(h, now, k => {
                sb.Append("  -").Append((now - h.times[k]).ToString("F3")).Append(" s  ").Append(h.origins[k] == null ? "[?]     (origin not captured: first play of the window, player build)" : (h.fromInput[k] ? "[input] " : "[code]  ") + h.origins[k]);
                if (h.zoundNames[k] != null) sb.Append("  via '").Append(h.zoundNames[k]).Append('\'');
                sb.Append('\n');
            });
            if (ZoundEngine.CullingGroups.TryGetValue(zound, out var tokens)) {
                sb.Append("live tokens of this zound: ").Append(tokens.Count).Append('\n');
                foreach (var t in tokens) if (t != null) sb.Append("  state=").Append(t.state).Append(" time=").Append(t.time.ToString("F3")).Append(" duration=").Append(t.duration.ToString("F3")).Append('\n');
            }
            sb.Append("full stack of the play that completed the burst:\n").Append(new System.Diagnostics.StackTrace(3, true));
            return sb.ToString();
        }
    }

}
