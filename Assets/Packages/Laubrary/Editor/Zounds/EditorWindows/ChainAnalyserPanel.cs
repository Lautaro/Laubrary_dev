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
        float dbRange = 12f;
        bool showRange = true;

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
                    showRange = ZUI.Toggle(showRange, "Range", ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Width(56f));
                    if (ZUI.Button("Re-measure", ZUI.Style.RichButton, ZUICornerMask.All, GUILayout.Width(86f))) {
                        measuredVersion = int.MinValue;
                    }
                }
                else {
                    ZUI.Label("gain", ZUI.ZTextStyle.Subtle);
                    live.gain = EditorGUILayout.Slider(live.gain, 0.25f, 16f, GUILayout.Width(90f));
                }
            }

            if (view == View.Combined) DrawCombined(zound, chain, height);
            else DrawLive(height);
        }

        void Tab(View which, string label) {
            bool on = view == which;
            if (ZUI.Toggle(on, label, ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Width(74f)) != on) view = which;
        }

        // ───────────────────────────── the combined per-band view ─────────────────────────────

        void DrawCombined(Zound zound, ZoundEffectChain chain, float height) {
            EnsureMeasured(chain);

            if (chain == null || chain.IsEmpty) {
                ZUI.Label("Nothing to measure yet — add an effect above.", ZUI.ZTextStyle.Subtle);
                return;
            }
            if (measurement.bands == null) {
                ZUI.Label("Measuring…", ZUI.ZTextStyle.Subtle);
                return;
            }
            if (measuredVersion != chain.version) {
                // Still showing the previous result while the edits settle. Said out loud, because a stale reading that
                // looks current is exactly the kind of quiet dishonesty this whole panel is trying to avoid.
                ZUI.Label("edited — re-measuring in a moment", ZUI.ZTextStyle.Subtle);
            }
            if (measurement.silentOrUnchanged) {
                ZUI.Label("This chain measurably changes nothing — every effect is off or sitting at a neutral setting.",
                          ZUI.ZTextStyle.Subtle);
            }

            DrawBars(GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true)));
            DrawFidelity(chain);
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
            measurement = chain == null || chain.IsEmpty ? default : ChainSpectrumProbe.Measure(chain, 24, seconds);
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
            for (int b = 0; b < bands.Length; b++) {
                float x = area.x + b * slot;
                float bw = Mathf.Max(2f, slot - 2f);

                if (showRange) {
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
            ZUI.Label("Middle line = unchanged. Full height = " + dbRange.ToString("0")
                      + " dB. Faint block = the range each band moves through.", ZUI.ZTextStyle.Subtle);
        }

        /// <summary>
        /// Names each effect and how far its contribution can be trusted. Not decoration: a per-band reading is the right
        /// shape of answer for a filter and the wrong shape for a waveshaper, and a display that prints a confident number
        /// either way gets believed and tuned against.
        /// </summary>
        void DrawFidelity(ZoundEffectChain chain) {
            if (chain?.nodes == null) return;
            for (int i = 0; i < chain.nodes.Count; i++) {
                var node = chain.nodes[i];
                var fidelity = ChainEffectFidelity.Of(node.type);
                var colour = fidelity == EffectFidelity.Exact ? new Color(0.55f, 0.85f, 0.6f)
                           : fidelity == EffectFidelity.Moving ? new Color(0.6f, 0.75f, 0.95f)
                           : fidelity == EffectFidelity.LevelDependent ? new Color(0.9f, 0.85f, 0.5f)
                           : fidelity == EffectFidelity.TimeSmeared ? new Color(0.7f, 0.6f, 0.9f)
                           : new Color(0.95f, 0.55f, 0.5f);
                using (new EditorGUILayout.HorizontalScope()) {
                    var dot = GUILayoutUtility.GetRect(9f, 9f, GUILayout.Width(9f), GUILayout.Height(9f));
                    dot.y += 4f;
                    EditorGUI.DrawRect(dot, colour);
                    var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                    if (!node.enabled) style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    var desc = Dsp.ZoundEffectDescriptors.Get(node.type);
                    string name = desc != null && !string.IsNullOrEmpty(desc.displayName) ? desc.displayName : node.type.ToString();
                    EditorGUILayout.LabelField(name + (node.enabled ? "" : " (off)") + " — "
                                               + ChainEffectFidelity.Explain(node.type), style);
                }
            }
        }

        // ───────────────────────────── the raw-signal views ─────────────────────────────

        void DrawLive(float height) {
            bool playing = live.Sample(out string sourceName);
            var mode = view == View.LiveSpectrum ? LiveOutputView.Mode.Spectrum
                     : view == View.LiveOverTime ? LiveOutputView.Mode.Spectrogram
                     : LiveOutputView.Mode.Waveform;
            live.Draw(GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true)), mode, playing, sourceName);
            ZUI.Label(playing
                ? "The real signal, with nothing interpreted — when this and the combined view disagree, this one is right."
                : "Press play on this sound and it fills in. It reads the real output, so it needs something to read.",
                ZUI.ZTextStyle.Subtle);
        }
    }
}
