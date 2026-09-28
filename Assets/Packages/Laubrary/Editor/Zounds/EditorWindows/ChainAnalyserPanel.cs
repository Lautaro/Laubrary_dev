using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.EditorTools {

    /// <summary>
    /// The four ways of looking at a chain, drawn for whichever chain it is handed.
    ///
    /// **Why this is a panel rather than a window.** The first version of this was a separate window with a dropdown to
    /// pick a sound, and it did not work — not because the measuring was wrong but because it was pointed at whatever the
    /// dropdown happened to select, from a list that omitted sounds nested inside a sequence altogether. Analysing "some
    /// sound" is close to useless; the only chain anybody wants to understand is the one they are editing right now. So
    /// this takes the chain as an argument and has no opinion about where it came from, which lets it live directly
    /// underneath the effect list it describes.
    ///
    /// **Measuring is not free, so it is cached against the chain's own revision counter.** The chain bumps that counter on
    /// every edit, so the display refreshes exactly when something changed and never on an idle repaint. Without that it
    /// would re-render several seconds of audio every frame.
    /// </summary>
    public class ChainAnalyserPanel {

        public enum View { Combined, LiveSpectrum, LiveOverTime, LiveWaveform }

        /// <summary>The sound whose chain was drawn most recently, so a detached window can follow the editor.</summary>
        public static Zound lastAnalysed;

        public View view = View.Combined;
        public bool open;

        readonly LiveOutputView live = new LiveOutputView();
        ChainSpectrumProbe.Measurement measurement;
        ZoundEffectChain measuredChain;
        Zound measuredZound;
        int measuredVersion = int.MinValue;
        double measuredAt;
        Zound drawnZound;

        // ── the live picture: what the chain does at this instant (T-0434) ──
        // The one play every display reads from while nothing is playing: re-taken at the start of every loop, and the
        // moment a real play starts, so running clocks are joined afresh each time (T-0443).
        Zound standIn;
        int loopIndex = -1;
        double lastPlayStart = double.MinValue;
        ZoundEffectChain seriesChain;
        // The chain as it behaves between plays: only modifiers that keep running without a play still move anything
        // (T-0446). Everything that fires on a play — an envelope, a per-play oscillator or step list, a random value — is
        // held at none, because nothing has fired it.
        ZoundEffectChain idleChain;
        // Whether the series on screen came from a real play (and so includes what that play fired), not from idleChain.
        bool seriesFromPlay;
        float nominalPlay;
        readonly System.Collections.Generic.Dictionary<long, ChainSpectrumProbe.Snapshot> snapshots =
            new System.Collections.Generic.Dictionary<long, ChainSpectrumProbe.Snapshot>();
        ChainSpectrumProbe.Snapshot shown;
        float[] seenMin, seenMax;
        float[] nowPositions;
        float lastLoopTime = -1f;
        int snapshotsTaken;

        /// <summary>Kept for the hosts that call it on close. The panel no longer holds anything native between redraws.</summary>
        public void Dispose() { }

        /// <summary>
        /// Used when a sound's length cannot be worked out (its source is not loaded). Only then — every sound that can
        /// be played is measured over its own real length.
        /// </summary>
        const float FallbackPlaySeconds = 1.5f;


        /// <summary>
        /// The vertical scale, worked out from the measurement itself rather than set by hand.
        ///
        /// **There used to be a control for this, and it had to go.** A display whose accuracy depends on the reader first
        /// tuning it is not a measurement, it is a drawing — and a fixed scale is the same fault with the tuning frozen at
        /// one guess. At twelve decibels, which is what it was fixed at, almost any real chain drove most bands hard
        /// against the top of the graph, so a filter that cut a band by fifteen decibels and one that annihilated it by
        /// sixty looked exactly alike. Fitting the scale to the strongest thing actually measured means the picture is
        /// always the true one, and the number it was fitted to is printed underneath so the scale is never a mystery.
        /// </summary>
        float dbRange = 12f;

        /// <summary>
        /// True while the host must keep redrawing: a live view is only live if it is repainted, and a measurement waiting
        /// for edits to settle needs at least one more frame to actually happen. Without the second case a measurement
        /// could sit pending indefinitely after a drag ended, until the mouse happened to move.
        /// </summary>
        public bool wantsContinuousRepaint =>
            open && (view != View.Combined || measuredVersion != pendingVersion || CombinedMoves
                     || Dsp.SapVoiceRegistry.IsPlaying(drawnZound));

        /// <summary>
        /// Whether the combined view has anything that moves by itself. It used to be treated as a still picture, so once a
        /// measurement had finished nothing asked for another redraw: the animation advanced only when something else
        /// happened to redraw the window, which is what made it look stuck or jerky. A chain that genuinely changes nothing
        /// over time still stops redrawing, so an idle editor still costs nothing.
        /// </summary>
        bool CombinedMoves => measurement.lanes != null && measurement.lanes.Length > 0;

        public void Draw(Zound zound, ZoundEffectChain chain, float height = 170f) {
            lastAnalysed = zound;
            drawnZound = zound;

            using (new EditorGUILayout.HorizontalScope()) {
                open = ZUI.Toggle(open, open ? "Analyse ▾" : "Analyse ▸", ZUI.Style.RichToggle,
                                  ZUICornerMask.All, GUILayout.Width(90f));
                if (!open) {
                    ZUI.Label("see what this chain is doing", ZUI.ZTextStyle.Subtle);
                    GUILayout.FlexibleSpace();
                    return;
                }
                GUILayout.Space(6f);
                Tab(View.Combined, "Combined");
                Tab(View.LiveSpectrum, "Spectrum");
                Tab(View.LiveOverTime, "Over time");
                Tab(View.LiveWaveform, "Waveform");
                GUILayout.FlexibleSpace();
                if (view == View.Combined) {
                    // Nothing to set. The two controls that used to sit here — a toggle for the range block and a button to
                    // measure again — both asked the reader to manage the display in order to trust it. The range block is
                    // always drawn now because it is information, not decoration, and measuring again happens by itself
                    // whenever the chain changes, which is the only time the answer can differ.
                    // Fixed width and a single line. Left to size itself, this wrapped onto a second line in a narrow
                    // panel and grew the toolbar by a row — which the layout-stability check caught immediately, having
                    // been built for exactly this. The detail moves to the hover text, where length costs nothing.
                    int bandCount = BANDS;
                    var note = GUILayoutUtility.GetRect(150f, EditorGUIUtility.singleLineHeight,
                                                        GUILayout.Width(150f), GUILayout.ExpandWidth(false));
                    var noteStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Ellipsis };
                    noteStyle.normal.textColor = new Color(0.62f, 0.62f, 0.68f);
                    GUI.Label(note, new GUIContent(bandCount + " bands, 20 Hz–20 kHz",
                        "The whole range of hearing, 20 Hz to 20 kHz, in " + bandCount + " bands, with the vertical scale fitted to whatever was "
                      + "measured. The bars are live: every frame they show what the chain does at that instant."), noteStyle);
                }
                else {
                    ZUI.Label("gain", ZUI.ZTextStyle.Subtle);
                    live.gain = EditorGUILayout.Slider(live.gain, 0.25f, 16f, GUILayout.Width(90f));
                }
            }

            // Measured whatever the view, because the modulation lanes below are drawn in every view: they are what the
            // modifiers are doing, which is as useful above the live spectrum as above the combined one. Drawing them in
            // only one view would also make the panel change height on a tab switch — see the roster note below.
            EnsureMeasured(zound, chain);
            if (view == View.Combined) DrawCombined(zound, chain, height);
            else DrawLive(zound, chain, height);

            // Drawn in EVERY view, not just the combined one, so switching tabs cannot change the panel's height. When the
            // panel is embedded in a scrolling editor, a height change on a tab flip drags the content under the reader's
            // cursor; keeping the footprint identical across views removes that entirely.
            DrawChainRoster(chain);
        }

        /// <summary>
        /// One fixed-height line for a message that may or may not be there.
        ///
        /// The slot is reserved whether or not there is anything to say, and the text is clipped to it rather than wrapped.
        /// Both halves of that matter. A line that appears and disappears pushes everything below it down and lets it snap
        /// back — and the message that does this most is "re-measuring", which appears WHILE a slider is being dragged, so
        /// the graph would hop up and down under the hand that is adjusting it. Clipping rather than wrapping keeps the
        /// height fixed at narrow widths too, where a sentence that fits on one line in a wide panel would take three.
        /// </summary>
        static void StatusLine(string message, string tooltip = null) {
            var row = GUILayoutUtility.GetRect(10f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            if (string.IsNullOrEmpty(message)) return;
            var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Ellipsis };
            style.normal.textColor = new Color(0.62f, 0.62f, 0.68f);
            GUI.Label(row, new GUIContent(message, tooltip ?? message), style);
        }

        /// <summary>How many bands the spectrum is divided into. Three times what it was, on the owner's ask for detail.</summary>
        const int BANDS = 72;

        /// <summary>
        /// A vertical scale that fits what was actually measured, rounded up to a tidy number.
        ///
        /// Only bands the measurement could actually speak about are considered, so a blank band cannot stretch the scale.
        /// The floor of six decibels exists so that a chain doing almost nothing is not magnified into looking dramatic —
        /// without it, a half-decibel ripple would be drawn full height and read as a huge effect, which is the same
        /// dishonesty as the fixed scale, just pointing the other way.
        /// </summary>
        float FitScale() {
            if (seenMin == null || shown.measurable == null) return 12f;
            float worst = 0f;
            for (int b = 0; b < seenMin.Length; b++) {
                if (!shown.measurable[b]) continue;
                worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(seenMin[b]), Mathf.Abs(seenMax[b])));
            }
            if (worst < 6f) return 6f;
            // Up to the next sensible step, and never shrinking while the chain is unchanged, so the scale does not
            // twitch as the bars move.
            float[] steps = { 6f, 12f, 18f, 24f, 36f, 48f, 60f, 80f };
            for (int i = 0; i < steps.Length; i++) if (worst <= steps[i]) return steps[i];
            return Mathf.Ceil(worst / 20f) * 20f;
        }

        void Tab(View which, string label) {
            bool on = view == which;
            if (ZUI.Toggle(on, label, ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Width(74f)) != on) view = which;
        }

        // ───────────────────────────── the combined per-band view ─────────────────────────────

        void DrawCombined(Zound zound, ZoundEffectChain chain, float height) {

            // Exactly one status line, always occupying the same row, whichever of these is worth saying. There is
            // deliberately no "edited, re-measuring" message: the owner's judgement is that it is not worth its line.
            bool empty = chain == null || chain.IsEmpty;
            float playTime = PlayTime(zound, out bool following);
            if (!empty && Event.current.type == EventType.Repaint) SafeUpdateLive(zound, chain, playTime, following, true);

            StatusLine(CombinedStatus(chain));

            // One lane per modulated parameter, sized from the CHAIN rather than from the measurement, so the panel keeps
            // the same height while a measurement is in flight and does not jump when it lands.
            int laneRows = CountModulatedParams(chain);
            DrawLanes(zound, laneRows, playTime, following);

            // The graph's slot is reserved even with nothing to draw in it, so adding the first effect does not make the
            // panel jump to a different size.
            var area = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
            if (empty || shown.db == null) DrawEmptyGraph(area);
            else DrawBars(area);

            // Which moment the bars are showing, and where it comes from.
            string time = TimeText(playTime, following);
            StatusLine(time, time == null ? null : TimeTip(following));
        }

        // ── shared with the UI Toolkit twin (T-0467): the words and shapes the panel shows, independent of how it draws ──

        /// <summary>The combined view's single status line, or null when there is nothing worth saying.</summary>
        internal string CombinedStatus(ZoundEffectChain chain) {
            bool empty = chain == null || chain.IsEmpty;
            bool still = shown.db != null && (measurement.lanes == null || measurement.lanes.Length == 0) && MaxAbs(shown) < 0.05f;
            return empty ? "Nothing to measure yet — add an effect above."
                 : shown.db == null ? "Measuring…"
                 : still ? "This chain measurably changes nothing — every effect is off or sitting at a neutral setting."
                 : null;
        }

        /// <summary>Which moment the bars are showing, and where it comes from; null before the first picture.</summary>
        internal string TimeText(float playTime, bool following) => shown.db == null ? null
            : playTime.ToString("0.00") + " s of " + Mathf.Max(measurement.totalSeconds, measurement.playSeconds).ToString("0.00") + " s"
              + (measurement.totalSeconds > measurement.playSeconds + 0.01f ? " (source " + measurement.playSeconds.ToString("0.00") + " s, then its tail)" : "")
              + (measurement.truncated ? " (the first " + ChainSpectrumProbe.MaxSeconds.ToString("0") + " s)" : "")
              + (following ? "  ·  live, from the sound playing now"
                 : RunsBetweenPlays(seriesChain) ? "  ·  not playing: only what keeps running between plays moves; press play to see the rest"
                 : "  ·  not playing: nothing moves until the sound is played");

        internal string TimeTip(bool following) =>
            "The bars show what the chain does to each part of the sound at this very moment, redrawn every frame. "
            + (following ? "The moment is taken from the sound as it plays: its parameters are read from the engine while you listen. "
                         : RunsBetweenPlays(seriesChain)
                             ? "Nothing is playing, so nothing that is fired by a play is worked out at all — an envelope, an oscillator or step list set to run per play, a random value all sit at none, leaving their parameters where they were set. Only what keeps running between plays (an oscillator set to Always, a timed step list with Retrigger off) still moves, followed in real time where its clock has got to. Press play to see everything a play does. "
                             : "Nothing is playing, and everything in this chain is fired by a play (an envelope, an oscillator or step list set to run per play, a random value), so nothing is worked out and nothing moves: every parameter sits where it was set, and the bars show the chain at those settings. Press play to see what a play does. ")
            + "To get the whole range of hearing down to the deepest bass, each picture is measured with the chain held still at that moment's settings.";

        /// <summary>
        /// One frame of the panel's own state, for a host that is not IMGUI: re-measures after edits, works out the moment
        /// shown (following a playing voice, or looping), and takes that moment's picture. The IMGUI panel does the same
        /// inside its drawing; a UI Toolkit host calls this once per tick instead.
        /// </summary>
        internal void Step(Zound zound, ZoundEffectChain chain, bool wantPicture, out float playTime, out bool following) {
            lastAnalysed = zound;
            drawnZound = zound;
            EnsureMeasured(zound, chain);
            playTime = PlayTime(zound, out following);
            if (chain != null && !chain.IsEmpty) SafeUpdateLive(zound, chain, playTime, following, wantPicture);
        }

        internal static int LaneCount(ZoundEffectChain chain) => CountModulatedParams(chain);
        internal const int Bands = BANDS;
        internal LiveOutputView Live => live;
        internal static float LaneHeight => LaneH;
        internal static float LaneLabelWidth => LaneLabelW;
        internal bool HasPicture => shown.db != null;

        /// <summary>A lane's label and hover text (empty while that lane has not been measured yet).</summary>
        internal void LaneText(int r, bool following, out string label, out string tip) {
            var lanes = measurement.lanes;
            bool have = lanes != null && r < lanes.Length && lanes[r].position01 != null && lanes[r].position01.Length > 0;
            label = have ? lanes[r].label : "";
            tip = have
                ? label + ", across one play from start (left) to end (right), including the tail the chain rings on for after its source ends (the faint upright mark is where the source ends). The line is where the engine actually puts it, as a position along its slider; the faint level is where you set it; the upright line is the moment shown in the bars below."
                  + (following ? " The dot is its value in the playing sound right now; the bars below show the chain at exactly that value."
                               : " Nothing is playing, so only modifiers that keep running between plays move it here; anything fired by a play (an envelope, a per-play oscillator or step list, a random value) is left out until the sound is played, and a lane driven only by those stays flat where it was set. The dot is its value right now; the bars below show the chain at exactly that value.")
                  + (RunsAlways(lanes[r].modifierIndex)
                      ? " Its modifier keeps running between plays (an oscillator set to Always, or a timed step list with Retrigger off), so a real play picks it up wherever it has got to; this line shows a play that happened to start at the beginning, and the dot shows where a real one is."
                      : "")
                : "";
        }

        /// <summary>
        /// Paints lane <paramref name="r"/>'s graph into <paramref name="g"/> through <paramref name="fill"/>: the dark field,
        /// where the source ends, where the parameter was set, where the engine put it (one column per device pixel, filled
        /// back to the set level), the moment shown and its dot.
        /// </summary>
        internal void PaintLane(int r, Rect g, float playTime, bool following, System.Action<Rect, Color> fill) {
            bool still = !following && !RunsBetweenPlays(seriesChain);
            var lanes = measurement.lanes;
            float len = measurement.totalSeconds > 0f ? measurement.totalSeconds : measurement.playSeconds > 0f ? measurement.playSeconds : FallbackPlaySeconds;
            float sourceEnds = measurement.playSeconds;
            bool have = lanes != null && r < lanes.Length && lanes[r].position01 != null && lanes[r].position01.Length > 0;
            fill(g, new Color(0.10f, 0.10f, 0.12f));
            if (!have) return;
            var lane = lanes[r];
            float yOf(float p) => g.yMax - 1f - p * (g.height - 2f);

            // Where the source ends and the chain's tail rings on — the engine keeps moving the parameter through it.
            if (sourceEnds > 0f && sourceEnds < len - 0.01f) {
                float xs = g.x + sourceEnds / len * g.width;
                fill(new Rect(xs, g.y, 1f, g.height), new Color(1f, 1f, 1f, 0.18f));
            }

            // Where it was set.
            float ya = yOf(lane.authored01);
            fill(new Rect(g.x, ya, g.width, 1f), new Color(0.55f, 0.55f, 0.62f, 0.45f));

            // Where the engine put it, one column per pixel, filled back to where it was set so the swing reads as area.
            var vals = lane.position01;
            float step = measurement.laneStepSeconds > 0f ? measurement.laneStepSeconds : len / vals.Length;
            // One column per real screen pixel, not per interface point: on a high-density display a point is two
            // or three pixels, and columns a point wide alternately overlap and leave gaps, which drew the line dashed.
            float px = 1f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            int cols = Mathf.Max(1, Mathf.FloorToInt(g.width / px));
            float prevY = yOf(vals[0]);
            for (int c = 0; c < cols; c++) {
                float t = (c + 0.5f) / cols * len;
                // Value i describes the end of engine step i, i.e. time (i + 1) x step; the dot reads it the same way.
                float p = SampleLane(vals, t / step - 1f);
                float y = yOf(p);
                float x = g.x + c * px;
                float top = Mathf.Min(y, ya), bot = Mathf.Max(y, ya);
                fill(new Rect(x, top, px, Mathf.Max(px, bot - top)), new Color(0.45f, 0.75f, 1f, 0.18f));
                float lt = Mathf.Min(y, prevY), lb = Mathf.Max(y, prevY);
                fill(new Rect(x, lt - 0.5f, px, Mathf.Max(1.2f, lb - lt + 1f)), new Color(0.55f, 0.82f, 1f, 0.95f));
                prevY = y;
            }

            // Now — hidden when nothing moves, since there is then no moment to point at.
            float xNow = g.x + Mathf.Clamp01(playTime / len) * g.width;
            if (!still) fill(new Rect(xNow - 0.5f, g.y, 1.5f, g.height), new Color(1f, 1f, 1f, following ? 0.9f : 0.45f));
            if (nowPositions != null && r < nowPositions.Length && nowPositions[r] >= 0f) {
                float yl = yOf(nowPositions[r]);
                fill(new Rect(xNow - 3f, yl - 3f, 6f, 6f), new Color(1f, 0.85f, 0.35f));
            }
        }

        internal static void PaintEmptyGraph(Rect area, System.Action<Rect, Color> fill) {
            fill(area, new Color(0.12f, 0.12f, 0.14f));
            float mid = area.y + area.height * 0.5f;
            fill(new Rect(area.x, mid - 1f, area.width, 2f), new Color(0.3f, 0.3f, 0.34f));
        }

        /// <summary>The band under a pointer at <paramref name="x01"/> across the graph (0..1), or -1 outside it.</summary>
        internal static int BandAt(float x01) => x01 < 0f || x01 > 1f ? -1 : Mathf.Clamp((int)(x01 * BANDS), 0, BANDS - 1);

        /// <summary>Paints the per-band bars, their reached-range blocks, the tick uprights and the middle line. Returns how
        /// many bands could not be measured (for the hover text).</summary>
        internal int PaintBars(Rect area, int hovered, System.Action<Rect, Color> fill) {
            if (bandInfo == null || bandInfo.Length != BANDS) bandInfo = ChainSpectrumProbe.SnapshotBands(BANDS);
            var bands = bandInfo;
            fill(area, new Color(0.12f, 0.12f, 0.14f));
            float mid = area.y + area.height * 0.5f;
            // Faint uprights at the labelled frequencies, so a bar can be placed without counting.
            for (int t = 0; t < freqTicks.Length; t++) {
                float x = area.x + ChainSpectrumProbe.FrequencyPosition01(freqTicks[t]) * area.width;
                if (x > area.x + 1f && x < area.xMax - 1f) fill(new Rect(x, area.y, 1f, area.height), new Color(1f, 1f, 1f, 0.05f));
            }
            fill(new Rect(area.x, mid - 1f, area.width, 2f), new Color(0.45f, 0.45f, 0.5f));
            float slot = area.width / bands.Length;
            int unmeasurable = 0;
            for (int b = 0; b < bands.Length; b++) {
                float x = area.x + b * slot;
                float bw = Mathf.Max(1f, slot - 1f);
                if (!shown.measurable[b]) {
                    unmeasurable++;
                    fill(new Rect(x, mid - 1f, bw, 2f), new Color(0.32f, 0.32f, 0.36f));
                    continue;
                }
                if (b == hovered) fill(new Rect(x, area.y, bw, area.height), new Color(1f, 1f, 1f, 0.06f));
                if (seenMin != null) {
                    float top = mid - Mathf.Clamp(seenMax[b] / dbRange, -1f, 1f) * area.height * 0.5f;
                    float bot = mid - Mathf.Clamp(seenMin[b] / dbRange, -1f, 1f) * area.height * 0.5f;
                    fill(new Rect(x, top, bw, Mathf.Max(1f, bot - top)), new Color(0.3f, 0.45f, 0.6f, 0.35f));
                }
                float db = shown.db[b];
                float h = Mathf.Abs(Mathf.Clamp(db / dbRange, -1f, 1f)) * area.height * 0.5f;
                var colour = db >= 0f ? new Color(0.45f, 0.8f, 0.5f) : new Color(0.85f, 0.5f, 0.4f);
                fill(db >= 0f ? new Rect(x, mid - h, bw, h) : new Rect(x, mid, bw, h), colour);
            }
            return unmeasurable;
        }

        internal string ScaleTop => "+" + dbRange.ToString("0") + " dB";
        internal string ScaleBottom => "−" + dbRange.ToString("0") + " dB";

        internal string BandHoverText(int hovered) {
            if (hovered < 0 || bandInfo == null || shown.db == null) return "";
            var hb = bandInfo[hovered];
            return FormatHz(hb.lowHz) + "–" + FormatHz(hb.highHz) + ":  "
                 + (shown.measurable[hovered]
                     ? (shown.db[hovered] >= 0f ? "+" : "") + shown.db[hovered].ToString("0.0") + " dB now"
                       + (seenMin != null ? ",  " + seenMin[hovered].ToString("0") + " to " + seenMax[hovered].ToString("0") + " dB so far" : "")
                     : "too low to measure");
        }

        internal string BarsTip(int unmeasurable) =>
            "What this chain is doing to each part of the sound's frequency range RIGHT NOW, redrawn every frame, from the deepest bass on the left (20 Hz) to the highest treble on the right (20 kHz). "
            + "Middle line = unchanged. Green above it = louder, red below it = quieter; full height is " + dbRange.ToString("0") + " dB. "
            + "As modifiers move the chain's settings, the bars move with them — a step list jumps from one shape to the next, an oscillator sweeps. "
            + "The faint block behind each bar is every level that band has reached since the chain was last edited. "
            + (unmeasurable > 0 ? "A flat grey stub is a band this measurement cannot resolve. " : "")
            + "Point at a bar to read its numbers.";

        /// <summary>The frequency labels under the graph and where each sits across it (0..1).</summary>
        internal static (string text, float x01)[] FrequencyLabels() {
            var r = new (string, float)[freqTicks.Length];
            for (int t = 0; t < freqTicks.Length; t++) r[t] = (FormatHz(freqTicks[t]), ChainSpectrumProbe.FrequencyPosition01(freqTicks[t]));
            return r;
        }

        /// <summary>One roster row: the effect's dot colour, its text and its hover text.</summary>
        internal static void RosterRow(ZoundEffectNode node, bool verdicts, out Color dot, out string text, out string tip) {
            var fidelity = ChainEffectFidelity.Of(node.type);
            var colour = fidelity == EffectFidelity.Exact ? new Color(0.55f, 0.85f, 0.6f)
                       : fidelity == EffectFidelity.Moving ? new Color(0.6f, 0.75f, 0.95f)
                       : fidelity == EffectFidelity.LevelDependent ? new Color(0.9f, 0.85f, 0.5f)
                       : fidelity == EffectFidelity.TimeSmeared ? new Color(0.7f, 0.6f, 0.9f)
                       : new Color(0.95f, 0.55f, 0.5f);
            dot = node.enabled ? colour : colour * 0.45f;
            var desc = Dsp.ZoundEffectDescriptors.Get(node.type);
            string name = desc != null && !string.IsNullOrEmpty(desc.displayName) ? desc.displayName : node.type.ToString();
            text = name + (node.enabled ? "" : " (off)") + (verdicts ? " — " + ChainEffectFidelity.Explain(node.type) : "");
            tip = name + " — " + ChainEffectFidelity.Explain(node.type);
        }

        internal static string LiveStatus(bool playing, string sourceName) => playing && !string.IsNullOrEmpty(sourceName) ? "reading: " + sourceName : null;
        internal static string LiveFooter(bool playing) => playing
            ? "The real signal, with nothing interpreted — when this and the combined view disagree, this one is right."
            : "Press play on this sound and it fills in. It reads the real output, so it needs something to read.";

        static float MaxAbs(ChainSpectrumProbe.Snapshot s) {
            float m = 0f;
            for (int b = 0; b < s.db.Length; b++) if (s.measurable[b]) m = Mathf.Max(m, Mathf.Abs(s.db[b]));
            return m;
        }

        /// <summary>
        /// Which moment of the play to show, keeping the series every display reads from in step with it.
        ///
        /// While the sound is playing: the moment it has reached. Otherwise the play is looped back to back at real speed —
        /// with no rest between loops (a first version paused at the end of each loop, which the owner rightly read as the
        /// plot stalling), and a fresh series at the start of every loop, because a real play started then would join the
        /// running clocks at a new place (T-0443).
        /// </summary>
        float PlayTime(Zound zound, out bool following) {
            following = false;
            double now = EditorApplication.timeSinceStartup;
            if (Dsp.SapVoiceRegistry.TryReadPlayPosition(zound, out float elapsed, out float duration) && duration > 0f) {
                following = true;
                // A new real play: take the series now, as that play started, and for THAT play's length — a play pitched
                // up or down lasts a different time, while timed modifiers run in real seconds, so reading a nominal-length
                // series by fraction would put the line out of step with what is heard.
                // Joined at the moment the real play was started, when its modifiers joined their clocks — not when this
                // panel happened to notice it, nor when its audio began (they differ by the audio's start-up delay).
                if (Dsp.SapVoiceRegistry.TryReadPlayStart(zound, out double started) && System.Math.Abs(started - lastPlayStart) > 0.001) {
                    lastPlayStart = started;
                    RetakeSeries(zound, duration, started, fromPlay: true);
                    loopIndex = -1;
                }
                float life = measurement.totalSeconds > 0f ? measurement.totalSeconds : duration;
                return Mathf.Clamp(elapsed, 0f, life);
            }

            // Nothing is playing. The owner's rule (T-0446): while a sound is not playing, nothing that fires on a play is
            // worked out at all — no envelope, no per-play oscillator or step list, no random value — because nothing has
            // fired them. Only modifiers that keep running between plays still move anything, and the series is taken from
            // a copy of the chain with the rest held at none. The series a real play left behind contains everything that
            // play fired, so it is dropped the moment the play ends; left in place it went on looping, envelope included.
            if (seriesFromPlay) { RetakeSeries(zound, nominalPlay); loopIndex = -1; measuredAt = now; }

            // Nothing runs between plays: nothing moves, and the parameters sit where they were set.
            if (!RunsBetweenPlays(seriesChain)) return 0f;

            // Something keeps running: follow it in real time, a fresh series per loop so its clock is joined afresh.
            float len = measurement.totalSeconds > 0f ? measurement.totalSeconds : nominalPlay > 0f ? nominalPlay : FallbackPlaySeconds;
            double since = now - measuredAt;
            int loop = (int)System.Math.Floor(since / len);
            if (loop != loopIndex) {
                if (loopIndex >= 0) RetakeSeries(zound, nominalPlay);
                loopIndex = loop;
            }
            return (float)(since - loop * len);
        }

        /// <summary>
        /// Takes the one series every display of this panel reads: this chain's modifiers, run by the engine for one whole
        /// play started NOW, joining their clocks as a real play would — as a stand-in carrying the sound's name, so the
        /// real sound's own remembered state (a per-play step list's position) is never advanced by the analyser. The
        /// earlier live view ran as the real sound and did advance it, once per loop.
        /// </summary>
        /// <param name="fromPlay">True for the series of a real play, which runs the whole chain, everything the play fires
        /// included. Otherwise the series is taken from <see cref="idleChain"/>, where only what keeps running between plays
        /// moves anything.</param>
        void RetakeSeries(Zound zound, float seconds, double? triggeredAt = null, bool fromPlay = false) {
            var chain = fromPlay ? seriesChain : idleChain;
            seriesFromPlay = fromPlay;
            if (chain == null || chain.IsEmpty) return;
            var fresh = ChainSpectrumProbe.MeasureModulation(chain, seconds, StandIn(zound), triggeredAt);
            if (fresh.lanes != null) measurement = fresh;
        }

        /// <summary>
        /// Whether anything in the chain keeps running between plays — an oscillator set to Always, a timed step list with
        /// Retrigger off. Only then does the idle view animate, because only then does anything actually change while
        /// nothing is playing — and then only those modifiers move (see <see cref="BetweenPlays"/>).
        /// </summary>
        static bool RunsBetweenPlays(ZoundEffectChain chain) {
            if (chain?.modifiers == null) return false;
            for (int m = 0; m < chain.modifiers.Count; m++) {
                var mod = chain.modifiers[m];
                if (!mod.enabled || !RunsFreely(mod)) continue;
                foreach (var b in chain.bindings) if (b.modifierIndex == m) return true;
            }
            return false;
        }

        /// <summary>Whether this modifier keeps running while nothing plays, rather than being fired by a play.</summary>
        static bool RunsFreely(ZoundModifier mod) {
            if (mod?.p == null) return false;
            if (mod.type == ZoundModifierType.Lfo) return mod.p.Length > 3 && mod.p[3] < 0.5f;
            if (mod.type == ZoundModifierType.Step) return mod.p.Length > 4 && (int)mod.p[0] == (int)StepTiming.PerInterval && mod.p[4] < 0.5f;
            return false;
        }

        /// <summary>
        /// The chain as it is between plays: a copy in which every binding of a modifier that is fired by a play has no
        /// effect (depth none means exactly the value that was set, in every combining mode). The bindings are kept rather
        /// than removed so every lane stays in its place, drawn flat where it was set.
        /// </summary>
        static ZoundEffectChain BetweenPlays(ZoundEffectChain chain) {
            if (chain == null) return null;
            var copy = chain.DeepCopy();
            foreach (var b in copy.bindings)
                if (b.modifierIndex < 0 || b.modifierIndex >= copy.modifiers.Count || !RunsFreely(copy.modifiers[b.modifierIndex]))
                    b.depth = 0f;
            return copy;
        }

        Zound StandIn(Zound zound) {
            if (zound == null) return null;
            if (standIn == null || standIn.name != zound.name) standIn = new Klip(-424242) { name = zound.name };
            return standIn;
        }

        static int CountModulatedParams(ZoundEffectChain chain) {
            if (chain?.bindings == null) return 0;
            int n = 0;
            for (int i = 0; i < chain.bindings.Count; i++) {
                var b = chain.bindings[i];
                if (b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count || !chain.modifiers[b.modifierIndex].enabled) continue;
                bool seen = false;
                for (int j = 0; j < i; j++) {
                    var o = chain.bindings[j];
                    if (o.nodeIndex == b.nodeIndex && o.paramIndex == b.paramIndex
                        && o.modifierIndex >= 0 && o.modifierIndex < chain.modifiers.Count && chain.modifiers[o.modifierIndex].enabled) seen = true;
                }
                if (!seen) n++;
            }
            return n;
        }

        const float LaneH = 30f;
        const float LaneLabelW = 150f;

        /// <summary>
        /// Every modulated parameter, followed across one play: where the engine actually put it (the line), where it was
        /// set (the faint level), and where the play is now (the upright line). While the sound plays, a dot marks the value
        /// the engine is using at this moment, read straight from the playing voice.
        ///
        /// **This is the overview of what the modifiers are doing, and every kind of modifier lands in it the same way.**
        /// An oscillator, an envelope, an oscillator's strength-over-the-play curve, a step list: each only ever matters
        /// through what it does to a parameter, so that is what is drawn — the combined result, which is what the effect
        /// receives. A strength curve therefore shows here as the swing of the line growing and shrinking over the play,
        /// not as a separate graph living somewhere else.
        ///
        /// It is also where fast movement and a modulator's shape can be seen at all. The frequency bars below have to
        /// average over a third of a second to read the bass; these lines are the engine's own values at its own update
        /// rate, so a ten-per-second wobble shows as exactly the shape the listener gets — including where a swing is so
        /// deep it pins the parameter against the end of its range.
        /// </summary>
        void DrawLanes(Zound zound, int rows, float playTime, bool following) {
            if (rows <= 0) return;
            var labelStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Ellipsis };
            for (int r = 0; r < rows; r++) {
                var row = GUILayoutUtility.GetRect(10f, LaneH, GUILayout.ExpandWidth(true));
                var labelRect = new Rect(row.x, row.y, LaneLabelW, row.height);
                var g = new Rect(row.x + LaneLabelW, row.y + 1f, row.width - LaneLabelW, row.height - 2f);
                LaneText(r, following, out string label, out string tip);
                GUI.Label(labelRect, new GUIContent(label, tip), labelStyle);
                if (Event.current.type != EventType.Repaint) continue;
                PaintLane(r, g, playTime, following, EditorGUI.DrawRect);
            }
        }

        bool RunsAlways(int modifierIndex) {
            var mods = measuredChain?.modifiers;
            if (mods == null || modifierIndex < 0 || modifierIndex >= mods.Count) return false;
            return RunsFreely(mods[modifierIndex]);
        }

        /// <summary>A lane's value at a fractional block index, joining neighbouring blocks with a straight line — which is
        /// exactly how the engine itself moves a parameter between one block and the next.</summary>
        static float SampleLane(float[] v, float index) {
            if (index <= 0f) return v[0];
            int i = Mathf.FloorToInt(index);
            if (i >= v.Length - 1) return v[v.Length - 1];
            return Mathf.Lerp(v[i], v[i + 1], index - i);
        }

        static void DrawEmptyGraph(Rect area) {
            PaintEmptyGraph(area, EditorGUI.DrawRect);
            // The frequency-label slot is reserved here too, for the same reason the graph is.
            GUILayoutUtility.GetRect(10f, 13f, GUILayout.ExpandWidth(true));
        }

        int pendingVersion = int.MinValue;
        double pendingSince;

        /// <summary>
        /// Re-reads the chain's modulation after an edit, once the edits have stopped for a moment, and forgets every
        /// picture taken of the old settings. The bars themselves are not measured here any more — they are taken live, per
        /// frame, in <see cref="UpdateLive"/>.
        /// </summary>
        void EnsureMeasured(Zound zound, ZoundEffectChain chain) {
            const double settleSeconds = 0.25;
            int version = chain == null ? int.MinValue + 1 : chain.version;

            bool same = measuredVersion == version && ReferenceEquals(measuredChain, chain) && ReferenceEquals(measuredZound, zound);
            if (same) return;

            double now = EditorApplication.timeSinceStartup;
            if (pendingVersion != version) { pendingVersion = version; pendingSince = now; return; }
            if (now - pendingSince < settleSeconds && shown.db != null) return;

            measuredChain = chain;
            measuredZound = zound;
            measuredVersion = version;
            measuredAt = now;
            snapshots.Clear();
            seenMin = seenMax = null;
            lastLoopTime = -1f;
            if (chain == null || chain.IsEmpty) { measurement = default; shown = default; dbRange = 12f; seriesChain = idleChain = null; return; }
            if (!Dsp.ZoundSapPlayback.TryGetPlayLength(zound, out float play)) play = FallbackPlaySeconds;
            seriesChain = chain;
            idleChain = BetweenPlays(chain);
            nominalPlay = play;
            seriesFromPlay = false;
            measurement = ChainSpectrumProbe.MeasureModulation(idleChain, play, StandIn(zound));
            loopIndex = -1;
            lastPlayStart = double.MinValue;
        }

        /// <summary>
        /// Works out the parameter values the chain has at this instant and shows the chain's response at those values.
        ///
        /// **Where "this instant" comes from.** While the sound plays, the values are read from the engine's own playing
        /// voice — exactly what is being heard. Otherwise a silent display voice runs this chain's modifiers in real time,
        /// looping the play, so the picture still moves as it would; modifiers that keep running between plays join their
        /// own clocks there, as a real play would. Either way the values come from the engine, never re-derived here.
        ///
        /// **Why a picture per set of values, cached.** The response of a chain whose settings are held still is fixed, so
        /// it is measured once per distinct set of values and re-used whenever those values come round again — a three-step
        /// list needs three pictures in total, an oscillator a few dozen positions per sweep. At most one new picture is
        /// taken per frame, so the editor never stalls; until a position has been measured, the nearest earlier picture
        /// stays on screen for that frame.
        /// </summary>
        int liveFailedVersion = int.MinValue;

        /// <summary>
        /// <see cref="UpdateLive"/>, made unable to take the window down with it.
        ///
        /// An exception thrown from inside a window's drawing leaves Unity's layout half-built, so the whole window breaks
        /// and the error repeats on every redraw — the owner's "some fx ui causes constant exceptions" (T-0442) was exactly
        /// that. Whatever the cause, the live picture is an extra: if taking it fails, it is reported ONCE, with the full
        /// exception, and not attempted again until the chain is edited; the rest of the editor keeps working.
        /// </summary>
        void SafeUpdateLive(Zound zound, ZoundEffectChain chain, float playTime, bool following, bool wantPicture) {
            int version = chain != null ? chain.version : int.MinValue + 1;
            if (liveFailedVersion == version) return;
            try { UpdateLive(zound, chain, playTime, following, wantPicture); }
            catch (System.Exception e) {
                liveFailedVersion = version;
                Debug.LogWarning("[Zounds] The analyser's live picture failed and is paused until this chain is edited. " + e);
            }
        }

        void UpdateLive(Zound zound, ZoundEffectChain chain, float playTime, bool following, bool wantPicture) {
            var lanes = measurement.lanes;
            int n = lanes != null ? lanes.Length : 0;
            if (nowPositions == null || nowPositions.Length != n) nowPositions = new float[n];
            var values = new float[n];
            var have = new bool[n];

            lastLoopTime = playTime;
            float step = measurement.laneStepSeconds > 0f ? measurement.laneStepSeconds : 0.01f;

            for (int i = 0; i < n; i++) {
                var lane = lanes[i];
                bool ok;
                if (following) {
                    ok = Dsp.ZoundDspPlayback.TryReadLiveParam(zound, lane.nodeIndex, lane.paramIndex, out values[i]);
                    nowPositions[i] = ok ? PositionOf(chain, lane, values[i]) : -1f;
                }
                else {
                    // Read off the very series the line is drawn from, at the very moment the line's playhead marks — so the
                    // dot sits on the line and the bars show the chain at exactly that point of it.
                    ok = lane.position01 != null && lane.position01.Length > 0;
                    nowPositions[i] = ok ? SampleLane(lane.position01, playTime / step - 1f) : -1f;
                    values[i] = ok ? ValueAt(chain, lane, nowPositions[i]) : 0f;
                }
                have[i] = ok && lane.nodeIndex >= 0;
            }
            if (!wantPicture) return;

            // The picture's identity: each modulated parameter's position along its slider, to a thousandth. Closer than
            // that no one can see, and it bounds how many pictures a sweep can ask for.
            long key = 17;
            for (int i = 0; i < n; i++) key = key * 1000003L + (have[i] ? Mathf.RoundToInt(nowPositions[i] * 1000f) : -1);

            if (!snapshots.TryGetValue(key, out var snap)) {
                // At most one new picture per redraw, which this is called once per. (A first version limited it with
                // Unity's frame counter, which does not advance in the editor outside Play mode, so after the first
                // picture every later one was refused and the bars froze — caught by watching them, T-0434.)
                snapshotsTaken++;
                var frozen = chain.DeepCopy();
                frozen.modifiers.Clear();
                frozen.bindings.Clear();
                for (int i = 0; i < n; i++) {
                    if (!have[i]) continue;
                    var node = frozen.nodes[lanes[i].nodeIndex];
                    node.EnsureParams();
                    node.p[lanes[i].paramIndex] = values[i];
                }
                snap = ChainSpectrumProbe.MeasureFrozen(frozen, BANDS);
                if (snap.db == null) return;
                if (snapshots.Count > 4000) snapshots.Clear();
                snapshots[key] = snap;
            }
            shown = snap;

            // Every band's lowest and highest reading among the pictures shown since the last edit: the faint block.
            if (seenMin == null) { seenMin = (float[])snap.db.Clone(); seenMax = (float[])snap.db.Clone(); dbRange = FitScale(); }
            bool grew = false;
            for (int b = 0; b < snap.db.Length; b++) {
                if (snap.db[b] < seenMin[b]) { seenMin[b] = snap.db[b]; grew = true; }
                if (snap.db[b] > seenMax[b]) { seenMax[b] = snap.db[b]; grew = true; }
            }
            if (grew) dbRange = Mathf.Max(dbRange, FitScale());
        }

        /// <summary>The value at a position along a parameter's slider — the inverse of <see cref="PositionOf"/>.</summary>
        static float ValueAt(ZoundEffectChain chain, ChainSpectrumProbe.Lane lane, float position01) {
            if (chain == null || lane.nodeIndex < 0 || lane.nodeIndex >= chain.nodes.Count) return 0f;
            var d = Dsp.ZoundEffectDescriptors.Get(chain.nodes[lane.nodeIndex].type);
            if (d == null || lane.paramIndex >= d.parameters.Length) return 0f;
            var pd = d.parameters[lane.paramIndex];
            return Dsp.ModulationMath.FromPosition(position01, pd.min, pd.max, Dsp.ModulationMath.IsRatioSpaced(pd.curve));
        }

        static float PositionOf(ZoundEffectChain chain, ChainSpectrumProbe.Lane lane, float value) {
            Dsp.ParamDesc pd;
            if (lane.nodeIndex < 0) {
                if (lane.paramIndex < 0 || lane.paramIndex >= SourceStageParam.Count) return -1f;
                pd = Dsp.ZoundEffectDescriptors.SourceStageParams[lane.paramIndex];
            }
            else {
                if (chain == null || lane.nodeIndex >= chain.nodes.Count) return -1f;
                var d = Dsp.ZoundEffectDescriptors.Get(chain.nodes[lane.nodeIndex].type);
                if (d == null || lane.paramIndex >= d.parameters.Length) return -1f;
                pd = d.parameters[lane.paramIndex];
            }
            return ChainSpectrumProbe.Position01(pd, value);
        }

        /// <summary>The frequencies worth a label: round numbers anyone can place, rather than the bands' own centres.</summary>
        static readonly float[] freqTicks = { 20f, 50f, 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f, 20000f };
        static ChainSpectrumProbe.Band[] bandInfo;

        void DrawBars(Rect area) {
            var evt = Event.current;
            int hovered = area.Contains(evt.mousePosition) ? BandAt((evt.mousePosition.x - area.x) / area.width) : -1;
            int unmeasurable = PaintBars(area, hovered, EditorGUI.DrawRect);
            float mid = area.y + area.height * 0.5f;

            // The scale, on the graph's own edge: what full height means, and that the middle is "unchanged".
            var scaleStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft };
            scaleStyle.normal.textColor = new Color(0.7f, 0.7f, 0.76f);
            GUI.Label(new Rect(area.x + 3f, area.y + 1f, 70f, 14f), ScaleTop, scaleStyle);
            GUI.Label(new Rect(area.x + 3f, mid - 15f, 70f, 14f), "0 dB", scaleStyle);
            GUI.Label(new Rect(area.x + 3f, area.yMax - 15f, 70f, 14f), ScaleBottom, scaleStyle);

            // The band under the mouse, named in plain numbers, at the top-right of the graph.
            if (hovered >= 0) {
                var hs = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperRight };
                hs.normal.textColor = new Color(0.95f, 0.95f, 1f);
                GUI.Label(new Rect(area.xMax - 360f, area.y + 1f, 356f, 14f), BandHoverText(hovered), hs);
            }

            GUI.Label(area, new GUIContent("", BarsTip(unmeasurable)));

            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
            style.normal.textColor = new Color(0.6f, 0.6f, 0.65f);
            var labels = GUILayoutUtility.GetRect(10f, 13f, GUILayout.ExpandWidth(true));
            foreach (var (text, x01) in FrequencyLabels()) {
                float x = labels.x + x01 * labels.width;
                var r = new Rect(x - 20f, labels.y, 40f, labels.height);
                if (r.x < labels.x) r.x = labels.x;
                if (r.xMax > labels.xMax) r.x = labels.xMax - r.width;
                GUI.Label(r, text, style);
            }
        }

        static string FormatHz(float hz) => hz >= 1000f ? (hz / 1000f).ToString("0.#") + "k" : hz.ToString("0");

        /// <summary>
        /// Names each effect and how far its contribution can be trusted. Not decoration: a per-band reading is the right
        /// shape of answer for a filter and the wrong shape for a waveshaper, and a display that prints a confident number
        /// either way gets believed and tuned against.
        /// </summary>
        void DrawChainRoster(ZoundEffectChain chain) {
            if (chain?.nodes == null) return;
            bool verdicts = view == View.Combined;
            for (int i = 0; i < chain.nodes.Count; i++) {
                var node = chain.nodes[i];
                RosterRow(node, verdicts, out Color colour, out string text, out string tip);
                // Exactly one line per effect, always. The verdict sentences run from six words to thirty, so wrapping them
                // made this list's height depend on which effects were in the chain AND on how wide the panel happened to
                // be — the panel would grow and shrink as effects were added, reordered or the window resized. Clipped to
                // one line with the whole sentence on hover, the list is a fixed number of fixed-height rows.
                var row = GUILayoutUtility.GetRect(10f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                var dot = new Rect(row.x, row.y + (row.height - 9f) * 0.5f, 9f, 9f);
                EditorGUI.DrawRect(dot, colour);
                // Only the combined view needs a verdict per effect, because only it INTERPRETS them (see RosterRow).
                var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Ellipsis };
                if (!node.enabled) style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUI.Label(new Rect(row.x + 13f, row.y, row.width - 13f, row.height), new GUIContent(text, tip), style);
            }
        }

        // ───────────────────────────── the raw-signal views ─────────────────────────────

        void DrawLive(Zound zound, ZoundEffectChain chain, float height) {
            bool playing = live.Sample(out string sourceName);
            // A leading status row, matching the combined view's, so switching tabs does not shift the panel by a line. It
            // earns the space by naming what is being listened to, which is the one thing these views cannot show.
            StatusLine(LiveStatus(playing, sourceName));
            float playTime = PlayTime(zound, out bool following);
            if (chain != null && !chain.IsEmpty && Event.current.type == EventType.Repaint) SafeUpdateLive(zound, chain, playTime, following, false);
            DrawLanes(zound, CountModulatedParams(chain), playTime, following);
            var mode = view == View.LiveSpectrum ? LiveOutputView.Mode.Spectrum
                     : view == View.LiveOverTime ? LiveOutputView.Mode.Spectrogram
                     : LiveOutputView.Mode.Waveform;
            live.Draw(GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true)), mode, playing, sourceName);
            // Matches the combined view's footprint: a legend row plus a status row, both fixed. The two messages below are
            // very different lengths, and swapping between them the moment playback starts or stops is precisely the kind of
            // reflow that would jog the panel while somebody is auditioning a sound.
            GUILayoutUtility.GetRect(10f, 13f, GUILayout.ExpandWidth(true));
            StatusLine(LiveFooter(playing));
        }
    }
}
