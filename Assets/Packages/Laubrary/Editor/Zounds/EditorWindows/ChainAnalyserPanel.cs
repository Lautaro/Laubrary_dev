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
        int measuredVersion = int.MinValue;
        double measuredAt;
        float seconds = 1.5f;

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
            open && (view != View.Combined || measuredVersion != pendingVersion);

        public void Draw(Zound zound, ZoundEffectChain chain, float height = 170f) {
            lastAnalysed = zound;

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
                    int bandCount = measurement.bands != null ? measurement.bands.Length : BANDS;
                    var note = GUILayoutUtility.GetRect(150f, EditorGUIUtility.singleLineHeight,
                                                        GUILayout.Width(150f), GUILayout.ExpandWidth(false));
                    var noteStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Ellipsis };
                    noteStyle.normal.textColor = new Color(0.62f, 0.62f, 0.68f);
                    GUI.Label(note, new GUIContent(bandCount + " bands, auto scale",
                        "The whole spectrum in " + bandCount + " bands, with the vertical scale fitted to whatever was "
                      + "measured. There is nothing to adjust: it re-measures itself whenever the chain changes."), noteStyle);
                }
                else {
                    ZUI.Label("gain", ZUI.ZTextStyle.Subtle);
                    live.gain = EditorGUILayout.Slider(live.gain, 0.25f, 16f, GUILayout.Width(90f));
                }
            }

            if (view == View.Combined) DrawCombined(zound, chain, height);
            else DrawLive(height);

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
        static void StatusLine(string message) {
            var row = GUILayoutUtility.GetRect(10f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            if (string.IsNullOrEmpty(message)) return;
            var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Ellipsis };
            style.normal.textColor = new Color(0.62f, 0.62f, 0.68f);
            GUI.Label(row, new GUIContent(message, message), style);
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
        static float FitScale(ChainSpectrumProbe.Measurement m) {
            if (m.bands == null) return 12f;
            float worst = 0f;
            for (int b = 0; b < m.bands.Length; b++) {
                if (!m.bands[b].measurable) continue;
                float lo = Mathf.Abs(m.bands[b].minDb), hi = Mathf.Abs(m.bands[b].maxDb);
                if (lo > worst) worst = lo;
                if (hi > worst) worst = hi;
            }
            if (worst < 6f) return 6f;
            // Up to the next sensible step, so the scale does not twitch by a decibel on every re-measure.
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
            EnsureMeasured(chain);

            // Exactly one status line, always occupying the same row, whichever of these is worth saying. Ordered by which
            // matters most to see: an empty chain, then a first measurement in flight, then a stale reading, then a chain
            // that provably does nothing. A stale reading is called out rather than shown silently — a reading that looks
            // current when it is not is the one dishonesty this whole panel exists to avoid.
            bool empty = chain == null || chain.IsEmpty;
            string status =
                  empty ? "Nothing to measure yet — add an effect above."
                : measurement.bands == null ? "Measuring…"
                : measuredVersion != chain.version ? "edited — re-measuring in a moment"
                : measurement.silentOrUnchanged ? "This chain measurably changes nothing — every effect is off or sitting at a neutral setting."
                : null;
            StatusLine(status);

            // The graph's slot is reserved even with nothing to draw in it, so adding the first effect does not make the
            // panel jump to a different size, and neither does the brief moment while the first measurement runs.
            var area = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
            if (empty || measurement.bands == null) DrawEmptyGraph(area);
            else DrawBars(area);
        }

        static void DrawEmptyGraph(Rect area) {
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.14f));
            float mid = area.y + area.height * 0.5f;
            EditorGUI.DrawRect(new Rect(area.x, mid - 1f, area.width, 2f), new Color(0.3f, 0.3f, 0.34f));
            // The legend slot is reserved here too, for the same reason the graph is.
            GUILayoutUtility.GetRect(10f, 13f, GUILayout.ExpandWidth(true));
            StatusLine(null);
        }

        int pendingVersion = int.MinValue;
        double pendingSince;

        /// <summary>
        /// Re-measures after an edit, but only once the edits have STOPPED for a moment.
        ///
        /// This matters more than it looks. The chain bumps its revision counter on every frame of a slider drag, and
        /// measuring means rendering a second or two of audio twice over and transforming every window of it. Measuring on
        /// each bump would make dragging a slider crawl — the visualiser would ruin the very editing it exists to support.
        /// Waiting for a short lull instead costs nothing noticeable and turns a whole drag into one measurement at the end.
        /// </summary>
        void EnsureMeasured(ZoundEffectChain chain) {
            const double settleSeconds = 0.25;
            int version = chain == null ? int.MinValue + 1 : chain.version;

            bool same = measuredVersion == version && ReferenceEquals(measuredChain, chain);
            if (same) return;

            double now = EditorApplication.timeSinceStartup;
            if (pendingVersion != version) { pendingVersion = version; pendingSince = now; return; }
            if (now - pendingSince < settleSeconds) return;

            measuredChain = chain;
            measuredVersion = version;
            measuredAt = now;
            measurement = chain == null || chain.IsEmpty ? default : ChainSpectrumProbe.Measure(chain, BANDS, seconds);
            dbRange = FitScale(measurement);
        }

        void DrawBars(Rect area) {
            var bands = measurement.bands;
            int windows = Mathf.Max(1, measurement.windows);
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.14f));

            int w = measurement.secondsPerWindow > 0f
                ? (int)((EditorApplication.timeSinceStartup - measuredAt) / measurement.secondsPerWindow) % windows
                : 0;

            float mid = area.y + area.height * 0.5f;
            EditorGUI.DrawRect(new Rect(area.x, mid - 1f, area.width, 2f), new Color(0.45f, 0.45f, 0.5f));

            float slot = area.width / bands.Length;
            int unmeasurable = 0;
            for (int b = 0; b < bands.Length; b++) {
                float x = area.x + b * slot;
                float bw = Mathf.Max(2f, slot - 2f);

                // A band the analysis cannot resolve gets a flat grey stub instead of a value.
                //
                // This is the other half of the jitter fix. The lowest bands are narrower in hertz than the analysis can
                // distinguish, so whatever appeared in them was not their own content — it was loud neighbouring content
                // bleeding in, which moves with the signal and made those bars swing wildly while the effect sat still.
                // Drawing zero would be a lie in the opposite direction, implying "unaffected" where the honest answer is
                // "too low for this measurement to speak about."
                if (!bands[b].measurable) {
                    unmeasurable++;
                    EditorGUI.DrawRect(new Rect(x, mid - 1f, bw, 2f), new Color(0.32f, 0.32f, 0.36f));
                    continue;
                }

                {
                    float top = mid - Mathf.Clamp(bands[b].maxDb / dbRange, -1f, 1f) * area.height * 0.5f;
                    float bot = mid - Mathf.Clamp(bands[b].minDb / dbRange, -1f, 1f) * area.height * 0.5f;
                    if (bot < top) (top, bot) = (bot, top);
                    EditorGUI.DrawRect(new Rect(x, top, bw, Mathf.Max(1f, bot - top)),
                                       new Color(0.3f, 0.45f, 0.6f, 0.35f));
                }

                float db = bands[b].overTime[Mathf.Clamp(w, 0, bands[b].overTime.Length - 1)];
                float h = Mathf.Abs(Mathf.Clamp(db / dbRange, -1f, 1f)) * area.height * 0.5f;
                var colour = db >= 0f ? new Color(0.45f, 0.8f, 0.5f) : new Color(0.85f, 0.5f, 0.4f);
                EditorGUI.DrawRect(db >= 0f ? new Rect(x, mid - h, bw, h) : new Rect(x, mid, bw, h), colour);
            }

            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
            style.normal.textColor = new Color(0.6f, 0.6f, 0.65f);
            var labels = GUILayoutUtility.GetRect(10f, 13f, GUILayout.ExpandWidth(true));
            for (int b = 0; b < bands.Length; b += 4) {
                float hz = bands[b].centreHz;
                GUI.Label(new Rect(labels.x + b * slot - slot, labels.y, slot * 3f, labels.height),
                          hz >= 1000f ? (hz / 1000f).ToString("0.#") + "k" : hz.ToString("0"), style);
            }
            StatusLine("Middle line = unchanged. Full height = " + dbRange.ToString("0")
                       + " dB. Faint block = the range each band moves through."
                       + (unmeasurable > 0 ? "  Grey stub = too low a frequency for this measurement to resolve." : ""));
        }

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

        void DrawLive(float height) {
            bool playing = live.Sample(out string sourceName);
            // A leading status row, matching the combined view's, so switching tabs does not shift the panel by a line. It
            // earns the space by naming what is being listened to, which is the one thing these views cannot show.
            StatusLine(playing && !string.IsNullOrEmpty(sourceName) ? "reading: " + sourceName : null);
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
