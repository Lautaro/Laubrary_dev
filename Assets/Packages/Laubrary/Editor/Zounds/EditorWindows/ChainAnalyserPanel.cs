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
        readonly ChainDisplayVoice display = new ChainDisplayVoice();
        readonly System.Collections.Generic.Dictionary<long, ChainSpectrumProbe.Snapshot> snapshots =
            new System.Collections.Generic.Dictionary<long, ChainSpectrumProbe.Snapshot>();
        ChainSpectrumProbe.Snapshot shown;
        float[] seenMin, seenMax;
        float[] nowPositions;
        float lastLoopTime = -1f;
        int snapshotsTaken;

        /// <summary>Releases the display voice's native memory. The host calls this when its window closes.</summary>
        public void Dispose() { display.Dispose(); }

        /// <summary>
        /// Used when a sound's length cannot be worked out (its source is not loaded). Only then — every sound that can
        /// be played is measured over its own real length.
        /// </summary>
        const float FallbackPlaySeconds = 1.5f;

        /// <summary>A short rest at the end of each loop while nothing is playing, so the loop's seam is visible as a pause
        /// rather than reading as part of what the chain does.</summary>
        const float LoopRestSeconds = 0.4f;

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
            if (!empty && Event.current.type == EventType.Repaint) UpdateLive(zound, chain, playTime, following, true);

            bool still = shown.db != null && (measurement.lanes == null || measurement.lanes.Length == 0) && MaxAbs(shown) < 0.05f;
            string status =
                  empty ? "Nothing to measure yet — add an effect above."
                : shown.db == null ? "Measuring…"
                : still ? "This chain measurably changes nothing — every effect is off or sitting at a neutral setting."
                : null;
            StatusLine(status);

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
            string time = shown.db == null ? null
                : playTime.ToString("0.00") + " s of " + measurement.playSeconds.ToString("0.00") + " s"
                  + (measurement.truncated ? " (the first " + ChainSpectrumProbe.MaxSeconds.ToString("0") + " s)" : "")
                  + (following ? "  ·  live, from the sound playing now" : "  ·  live, from a silent run of this chain's modifiers; press play to follow the real sound");
            StatusLine(time, time == null ? null
                : "The bars show what the chain does to each part of the sound at this very moment, redrawn every frame. "
                  + (following ? "The moment is taken from the sound as it plays: its parameters are read from the engine while you listen. "
                               : "Nothing is playing, so the moment comes from a silent run of this chain's modifiers, looping the play in real time. Modifiers that keep running between plays (an oscillator set to Always, a timed step list with Retrigger off) run on their own clocks here exactly as they would for a real play. ")
                  + "To get the whole range of hearing down to the deepest bass, each picture is measured with the chain held still at that moment's settings.");
        }

        static float MaxAbs(ChainSpectrumProbe.Snapshot s) {
            float m = 0f;
            for (int b = 0; b < s.db.Length; b++) if (s.measurable[b]) m = Mathf.Max(m, Mathf.Abs(s.db[b]));
            return m;
        }

        /// <summary>
        /// Which moment of the play to show. While the sound is playing, the moment it has actually reached — so what is
        /// drawn is what is being heard right now. Otherwise the play is looped at real speed, with a short rest at the end.
        /// </summary>
        float PlayTime(Zound zound, out bool following) {
            following = false;
            float len = measurement.playSeconds > 0f ? measurement.playSeconds : FallbackPlaySeconds;
            if (Dsp.SapVoiceRegistry.TryReadPlayPosition(zound, out float elapsed, out float duration) && duration > 0f) {
                following = true;
                // By fraction of the play, so a play that was pitched up or down still lines up start to end.
                float t = measurement.truncated ? elapsed : elapsed / duration * len;
                return Mathf.Clamp(t, 0f, len);
            }
            double period = len + LoopRestSeconds;
            float loop = (float)((EditorApplication.timeSinceStartup - measuredAt) % period);
            return Mathf.Min(loop, len);
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
            var lanes = measurement.lanes;
            float len = measurement.playSeconds > 0f ? measurement.playSeconds : FallbackPlaySeconds;
            var labelStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Ellipsis };
            for (int r = 0; r < rows; r++) {
                var row = GUILayoutUtility.GetRect(10f, LaneH, GUILayout.ExpandWidth(true));
                var labelRect = new Rect(row.x, row.y, LaneLabelW, row.height);
                var g = new Rect(row.x + LaneLabelW, row.y + 1f, row.width - LaneLabelW, row.height - 2f);
                bool have = lanes != null && r < lanes.Length && lanes[r].position01 != null && lanes[r].position01.Length > 0;
                string label = have ? lanes[r].label : "";
                GUI.Label(labelRect, new GUIContent(label, have
                    ? label + ", across one play from start (left) to end (right). The line is where the engine actually puts it, as a position along its slider; the faint level is where you set it; the upright line is the moment shown in the bars below."
                      + (following ? " The dot is its value in the playing sound right now; the bars below show the chain at exactly that value." : " The dot is its value right now in a silent run of the modifiers (press play to follow the real sound); the bars below show the chain at exactly that value.")
                      + (RunsAlways(lanes[r].modifierIndex)
                          ? " Its modifier keeps running between plays (an oscillator set to Always, or a timed step list with Retrigger off), so a real play picks it up wherever it has got to; this line shows a play that happened to start at the beginning, and the dot shows where a real one is."
                          : "")
                    : ""), labelStyle);
                if (Event.current.type != EventType.Repaint) continue;

                EditorGUI.DrawRect(g, new Color(0.10f, 0.10f, 0.12f));
                if (!have) continue;
                var lane = lanes[r];
                float yOf(float p) => g.yMax - 1f - p * (g.height - 2f);

                // Where it was set.
                float ya = yOf(lane.authored01);
                EditorGUI.DrawRect(new Rect(g.x, ya, g.width, 1f), new Color(0.55f, 0.55f, 0.62f, 0.45f));

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
                    float p = SampleLane(vals, t / step);
                    float y = yOf(p);
                    float x = g.x + c * px;
                    float top = Mathf.Min(y, ya), bot = Mathf.Max(y, ya);
                    EditorGUI.DrawRect(new Rect(x, top, px, Mathf.Max(px, bot - top)), new Color(0.45f, 0.75f, 1f, 0.18f));
                    float lt = Mathf.Min(y, prevY), lb = Mathf.Max(y, prevY);
                    EditorGUI.DrawRect(new Rect(x, lt - 0.5f, px, Mathf.Max(1.2f, lb - lt + 1f)), new Color(0.55f, 0.82f, 1f, 0.95f));
                    prevY = y;
                }

                // Now.
                float xNow = g.x + Mathf.Clamp01(playTime / len) * g.width;
                EditorGUI.DrawRect(new Rect(xNow - 0.5f, g.y, 1.5f, g.height), new Color(1f, 1f, 1f, following ? 0.9f : 0.45f));
                if (nowPositions != null && r < nowPositions.Length && nowPositions[r] >= 0f) {
                    float yl = yOf(nowPositions[r]);
                    EditorGUI.DrawRect(new Rect(xNow - 3f, yl - 3f, 6f, 6f), new Color(1f, 0.85f, 0.35f));
                }
            }
        }

        bool RunsAlways(int modifierIndex) {
            var mods = measuredChain?.modifiers;
            if (mods == null || modifierIndex < 0 || modifierIndex >= mods.Count) return false;
            var m = mods[modifierIndex];
            if (m.p == null) return false;
            if (m.type == ZoundModifierType.Lfo) return m.p.Length > 3 && m.p[3] < 0.5f;
            if (m.type == ZoundModifierType.Step) return m.p.Length > 4 && (int)m.p[0] == (int)StepTiming.PerInterval && m.p[4] < 0.5f;
            return false;
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
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.14f));
            float mid = area.y + area.height * 0.5f;
            EditorGUI.DrawRect(new Rect(area.x, mid - 1f, area.width, 2f), new Color(0.3f, 0.3f, 0.34f));
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
            display.Dispose();
            if (chain == null || chain.IsEmpty) { measurement = default; shown = default; dbRange = 12f; return; }
            if (!Dsp.ZoundSapPlayback.TryGetPlayLength(zound, out float play)) play = FallbackPlaySeconds;
            measurement = ChainSpectrumProbe.MeasureModulation(chain, play);
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
        void UpdateLive(Zound zound, ZoundEffectChain chain, float playTime, bool following, bool wantPicture) {
            var lanes = measurement.lanes;
            int n = lanes != null ? lanes.Length : 0;
            if (nowPositions == null || nowPositions.Length != n) nowPositions = new float[n];
            var values = new float[n];
            var have = new bool[n];

            if (!following && n > 0) {
                // Restart the silent run at the start of each loop, so per-play modifiers restart as a real play would and
                // running clocks are joined afresh.
                if (!display.isRunning || playTime < lastLoopTime) display.Start(chain, measurement.playSeconds, zound);
                display.AdvanceTo(playTime);
            }
            lastLoopTime = playTime;

            for (int i = 0; i < n; i++) {
                var lane = lanes[i];
                bool ok = following
                    ? Dsp.ZoundDspPlayback.TryReadLiveParam(zound, lane.nodeIndex, lane.paramIndex, out values[i])
                    : display.TryGetParam(lane.nodeIndex, lane.paramIndex, out values[i]);
                have[i] = ok && lane.nodeIndex >= 0;
                nowPositions[i] = ok ? PositionOf(chain, lane, values[i]) : -1f;
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
            if (bandInfo == null || bandInfo.Length != BANDS) bandInfo = ChainSpectrumProbe.SnapshotBands(BANDS);
            var bands = bandInfo;
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.14f));

            float mid = area.y + area.height * 0.5f;
            // Faint uprights at the labelled frequencies, so a bar can be placed without counting.
            for (int t = 0; t < freqTicks.Length; t++) {
                float x = area.x + ChainSpectrumProbe.FrequencyPosition01(freqTicks[t]) * area.width;
                if (x > area.x + 1f && x < area.xMax - 1f) EditorGUI.DrawRect(new Rect(x, area.y, 1f, area.height), new Color(1f, 1f, 1f, 0.05f));
            }
            EditorGUI.DrawRect(new Rect(area.x, mid - 1f, area.width, 2f), new Color(0.45f, 0.45f, 0.5f));

            var evt = Event.current;
            int hovered = area.Contains(evt.mousePosition) ? Mathf.Clamp((int)((evt.mousePosition.x - area.x) / area.width * bands.Length), 0, bands.Length - 1) : -1;

            float slot = area.width / bands.Length;
            int unmeasurable = 0;
            for (int b = 0; b < bands.Length; b++) {
                float x = area.x + b * slot;
                float bw = Mathf.Max(1f, slot - 1f);
                if (!shown.measurable[b]) {
                    unmeasurable++;
                    EditorGUI.DrawRect(new Rect(x, mid - 1f, bw, 2f), new Color(0.32f, 0.32f, 0.36f));
                    continue;
                }
                if (b == hovered) EditorGUI.DrawRect(new Rect(x, area.y, bw, area.height), new Color(1f, 1f, 1f, 0.06f));

                if (seenMin != null) {
                    float top = mid - Mathf.Clamp(seenMax[b] / dbRange, -1f, 1f) * area.height * 0.5f;
                    float bot = mid - Mathf.Clamp(seenMin[b] / dbRange, -1f, 1f) * area.height * 0.5f;
                    EditorGUI.DrawRect(new Rect(x, top, bw, Mathf.Max(1f, bot - top)), new Color(0.3f, 0.45f, 0.6f, 0.35f));
                }

                float db = shown.db[b];
                float h = Mathf.Abs(Mathf.Clamp(db / dbRange, -1f, 1f)) * area.height * 0.5f;
                var colour = db >= 0f ? new Color(0.45f, 0.8f, 0.5f) : new Color(0.85f, 0.5f, 0.4f);
                EditorGUI.DrawRect(db >= 0f ? new Rect(x, mid - h, bw, h) : new Rect(x, mid, bw, h), colour);
            }

            // The scale, on the graph's own edge: what full height means, and that the middle is "unchanged".
            var scaleStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft };
            scaleStyle.normal.textColor = new Color(0.7f, 0.7f, 0.76f);
            GUI.Label(new Rect(area.x + 3f, area.y + 1f, 70f, 14f), "+" + dbRange.ToString("0") + " dB", scaleStyle);
            GUI.Label(new Rect(area.x + 3f, mid - 15f, 70f, 14f), "0 dB", scaleStyle);
            GUI.Label(new Rect(area.x + 3f, area.yMax - 15f, 70f, 14f), "−" + dbRange.ToString("0") + " dB", scaleStyle);

            // The band under the mouse, named in plain numbers, at the top-right of the graph.
            if (hovered >= 0) {
                var hb = bands[hovered];
                string text = FormatHz(hb.lowHz) + "–" + FormatHz(hb.highHz) + ":  "
                            + (shown.measurable[hovered]
                                ? (shown.db[hovered] >= 0f ? "+" : "") + shown.db[hovered].ToString("0.0") + " dB now"
                                  + (seenMin != null ? ",  " + seenMin[hovered].ToString("0") + " to " + seenMax[hovered].ToString("0") + " dB so far" : "")
                                : "too low to measure");
                var hs = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperRight };
                hs.normal.textColor = new Color(0.95f, 0.95f, 1f);
                GUI.Label(new Rect(area.xMax - 360f, area.y + 1f, 356f, 14f), text, hs);
            }

            GUI.Label(area, new GUIContent("", "What this chain is doing to each part of the sound's frequency range RIGHT NOW, redrawn every frame, from the deepest bass on the left (20 Hz) to the highest treble on the right (20 kHz). "
                + "Middle line = unchanged. Green above it = louder, red below it = quieter; full height is " + dbRange.ToString("0") + " dB. "
                + "As modifiers move the chain's settings, the bars move with them — a step list jumps from one shape to the next, an oscillator sweeps. "
                + "The faint block behind each bar is every level that band has reached since the chain was last edited. "
                + (unmeasurable > 0 ? "A flat grey stub is a band this measurement cannot resolve. " : "")
                + "Point at a bar to read its numbers."));

            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
            style.normal.textColor = new Color(0.6f, 0.6f, 0.65f);
            var labels = GUILayoutUtility.GetRect(10f, 13f, GUILayout.ExpandWidth(true));
            for (int t = 0; t < freqTicks.Length; t++) {
                float x = labels.x + ChainSpectrumProbe.FrequencyPosition01(freqTicks[t]) * labels.width;
                var r = new Rect(x - 20f, labels.y, 40f, labels.height);
                if (r.x < labels.x) r.x = labels.x;
                if (r.xMax > labels.xMax) r.x = labels.xMax - r.width;
                GUI.Label(r, FormatHz(freqTicks[t]), style);
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
                var fidelity = ChainEffectFidelity.Of(node.type);
                var colour = fidelity == EffectFidelity.Exact ? new Color(0.55f, 0.85f, 0.6f)
                           : fidelity == EffectFidelity.Moving ? new Color(0.6f, 0.75f, 0.95f)
                           : fidelity == EffectFidelity.LevelDependent ? new Color(0.9f, 0.85f, 0.5f)
                           : fidelity == EffectFidelity.TimeSmeared ? new Color(0.7f, 0.6f, 0.9f)
                           : new Color(0.95f, 0.55f, 0.5f);

                // Exactly one line per effect, always. The verdict sentences run from six words to thirty, so wrapping them
                // made this list's height depend on which effects were in the chain AND on how wide the panel happened to
                // be — the panel would grow and shrink as effects were added, reordered or the window resized. Clipped to
                // one line with the whole sentence on hover, the list is a fixed number of fixed-height rows.
                var row = GUILayoutUtility.GetRect(10f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                var dot = new Rect(row.x, row.y + (row.height - 9f) * 0.5f, 9f, 9f);
                EditorGUI.DrawRect(dot, node.enabled ? colour : colour * 0.45f);

                var desc = Dsp.ZoundEffectDescriptors.Get(node.type);
                string name = desc != null && !string.IsNullOrEmpty(desc.displayName) ? desc.displayName : node.type.ToString();
                // Only the combined view needs a verdict per effect, because only it INTERPRETS them. The live views read the
                // real output, where every effect is present by definition and there is nothing to qualify — so they show
                // the names alone. Repeating one identical sentence down every row, as a first attempt did, filled the space
                // without adding anything and made four different effects look like four copies of the same thing.
                string text = name + (node.enabled ? "" : " (off)")
                            + (verdicts ? " — " + ChainEffectFidelity.Explain(node.type) : "");
                string tip = name + " — " + ChainEffectFidelity.Explain(node.type);

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
            StatusLine(playing && !string.IsNullOrEmpty(sourceName) ? "reading: " + sourceName : null);
            float playTime = PlayTime(zound, out bool following);
            if (chain != null && !chain.IsEmpty && Event.current.type == EventType.Repaint) UpdateLive(zound, chain, playTime, following, false);
            DrawLanes(zound, CountModulatedParams(chain), playTime, following);
            var mode = view == View.LiveSpectrum ? LiveOutputView.Mode.Spectrum
                     : view == View.LiveOverTime ? LiveOutputView.Mode.Spectrogram
                     : LiveOutputView.Mode.Waveform;
            live.Draw(GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true)), mode, playing, sourceName);
            // Matches the combined view's footprint: a legend row plus a status row, both fixed. The two messages below are
            // very different lengths, and swapping between them the moment playback starts or stops is precisely the kind of
            // reflow that would jog the panel while somebody is auditioning a sound.
            GUILayoutUtility.GetRect(10f, 13f, GUILayout.ExpandWidth(true));
            StatusLine(playing
                ? "The real signal, with nothing interpreted — when this and the combined view disagree, this one is right."
                : "Press play on this sound and it fills in. It reads the real output, so it needs something to read.");
        }
    }
}
