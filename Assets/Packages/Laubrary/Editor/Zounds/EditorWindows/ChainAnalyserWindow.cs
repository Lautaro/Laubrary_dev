using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.EditorTools;

namespace Laubrary.Zounds {

    /// <summary>
    /// Shows what a whole effect chain does to each part of the frequency range, and how that changes over time.
    ///
    /// **The question this answers.** Once a chain has several effects stacked up, each nudging overlapping parts of the
    /// spectrum, it stops being obvious what the combination is doing. A bar per band, animating, answers "where is this
    /// chain adding and where is it taking away" in a way that reading a list of effect names cannot.
    ///
    /// **It is measured, not modelled.** Real audio goes through the real chain and the result is compared with the
    /// input, so anything the chain does turns up — including things nobody thought to describe.
    ///
    /// **It says which effects it is NOT describing, and that is deliberate.** A per-band loudness reading is the right
    /// shape of answer for a filter and the wrong shape for a waveshaper, a bitcrusher, a delay or a reverb. Rather than
    /// print a confident number for those and let somebody tune against it, the panel at the bottom names each effect in
    /// the chain and how far to trust its contribution. A display that quietly fabricates is worse than one that admits
    /// a gap, because a fabrication gets believed.
    /// </summary>
    public class ChainAnalyserWindow : EditorWindow {

        [MenuItem("Laubrary/Zounds/Chain analyser")]
        public static void Open() {
            var w = GetWindow<ChainAnalyserWindow>("Chain analyser");
            w.minSize = new Vector2(520f, 420f);
            w.Show();
        }

        private int selected;
        private ChainSpectrumProbe.Measurement measurement;
        private ZoundEffectChain measuredChain;
        private int measuredVersion = -1;
        private double measuredAt;
        private float seconds = 2f;
        private bool animate = true;
        private bool showRange = true;
        private float dbRange = 12f;

        /// <summary>Which question the window is answering. The first is interpreted; the rest are the raw signal.</summary>
        private enum View { WhatTheChainDoes, LiveSpectrum, LiveSpectrogram, LiveWaveform }
        private View view = View.WhatTheChainDoes;
        private readonly LiveOutputView live = new LiveOutputView();

        private readonly List<Zound> candidates = new List<Zound>();
        private readonly List<string> candidateNames = new List<string>();

        private void OnEnable() {
            EditorApplication.update += Tick;
        }

        private void OnDisable() {
            EditorApplication.update -= Tick;
        }

        /// <summary>Repaints while animating, so the bars move without the mouse having to.</summary>
        private void Tick() {
            // The live views must repaint constantly or they are not live; the chain view only needs to when animating.
            if (view != View.WhatTheChainDoes) { Repaint(); return; }
            if (animate && measurement.bands != null) Repaint();
        }

        private void OnGUI() {
            RefreshCandidates();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                DrawViewTab(View.WhatTheChainDoes, "What the chain does");
                DrawViewTab(View.LiveSpectrum, "Live spectrum");
                DrawViewTab(View.LiveSpectrogram, "Live over time");
                DrawViewTab(View.LiveWaveform, "Live waveform");
                GUILayout.FlexibleSpace();
                if (view != View.WhatTheChainDoes) {
                    GUILayout.Label("gain", EditorStyles.miniLabel);
                    live.gain = EditorGUILayout.Slider(live.gain, 0.25f, 16f, GUILayout.Width(110f));
                    live.logFrequency = GUILayout.Toggle(live.logFrequency, "By octave", EditorStyles.toolbarButton,
                                                        GUILayout.Width(74f));
                }
            }

            if (view != View.WhatTheChainDoes) { DrawLive(); return; }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                if (candidates.Count == 0) {
                    GUILayout.Label("No sound in this project has an effect chain yet.", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                }
                else {
                    selected = Mathf.Clamp(selected, 0, candidates.Count - 1);
                    int pick = EditorGUILayout.Popup(selected, candidateNames.ToArray(), EditorStyles.toolbarPopup,
                                                     GUILayout.Width(200f));
                    if (pick != selected) { selected = pick; measuredVersion = -1; }

                    GUILayout.Space(8f);
                    GUILayout.Label("listen for", EditorStyles.miniLabel);
                    seconds = EditorGUILayout.Slider(seconds, 0.25f, 6f, GUILayout.Width(120f));
                    GUILayout.Label("s", EditorStyles.miniLabel);

                    GUILayout.FlexibleSpace();
                    animate = GUILayout.Toggle(animate, "Animate", EditorStyles.toolbarButton, GUILayout.Width(64f));
                    showRange = GUILayout.Toggle(showRange, "Range", EditorStyles.toolbarButton, GUILayout.Width(56f));
                    if (GUILayout.Button("Measure", EditorStyles.toolbarButton, GUILayout.Width(64f))) measuredVersion = -1;
                }
            }

            if (candidates.Count == 0) {
                EditorGUILayout.HelpBox("Add an effect to a sound's chain, then come back. This window measures what the "
                                      + "whole chain does to each part of the frequency range.", MessageType.Info);
                return;
            }

            var zound = candidates[selected];
            var chain = Dsp.ZoundDspPlayback.ResolveChain(zound, out _);
            EnsureMeasured(zound, chain);

            if (measurement.bands == null) {
                EditorGUILayout.HelpBox("Nothing to measure — this chain has no effects in it.", MessageType.Info);
                DrawFidelityPanel(chain);
                return;
            }

            if (measurement.silentOrUnchanged) {
                EditorGUILayout.HelpBox("This chain measurably changed nothing. Every effect in it is either disabled or "
                                      + "sitting at a setting that does nothing.", MessageType.Warning);
            }

            DrawBars();
            DrawScaleNote();
            DrawFidelityPanel(chain);
        }

        private void DrawViewTab(View which, string label) {
            bool on = view == which;
            if (GUILayout.Toggle(on, label, EditorStyles.toolbarButton) != on) view = which;
        }

        /// <summary>
        /// The raw signal, with nothing interpreted. Worth reaching for whenever the chain view's honesty panel says it
        /// cannot describe an effect: this one always can, because it is not describing anything — it is the sound.
        /// </summary>
        private void DrawLive() {
            bool playing = live.Sample(out string sourceName);
            var area = GUILayoutUtility.GetRect(10f, 260f, GUILayout.ExpandWidth(true));
            var mode = view == View.LiveSpectrum ? LiveOutputView.Mode.Spectrum
                     : view == View.LiveSpectrogram ? LiveOutputView.Mode.Spectrogram
                     : LiveOutputView.Mode.Waveform;
            live.Draw(area, mode, playing, sourceName);

            EditorGUILayout.Space(4f);
            switch (view) {
                case View.LiveSpectrum:
                    EditorGUILayout.LabelField("Where the energy is right now. Height is loudness on a decibel scale; "
                        + "the floor is 80 dB below full.", EditorStyles.miniLabel);
                    break;
                case View.LiveSpectrogram:
                    EditorGUILayout.LabelField("The same thing written out as it happens, newest on the right and low "
                        + "frequencies at the bottom. This is the view that reveals a pattern — a repeating modulation, "
                        + "a tail decaying — which a single instant cannot.", EditorStyles.miniLabel);
                    break;
                default:
                    EditorGUILayout.LabelField("The waveform itself. The peak reading falls back slowly so a brief "
                        + "overload is still visible a moment after it happened.", EditorStyles.miniLabel);
                    break;
            }
            if (!playing) {
                EditorGUILayout.HelpBox("Nothing is playing. Press play on a sound and this fills in — it reads the real "
                    + "output, so it needs something to read.", MessageType.Info);
            }
        }

        /// <summary>Re-measures when the chain has been edited, so the display follows the editor without a button press.</summary>
        private void EnsureMeasured(Zound zound, ZoundEffectChain chain) {
            int version = chain == null ? -2 : chain.version;
            bool stale = measuredVersion != version || !ReferenceEquals(measuredChain, chain);
            if (!stale) return;

            measuredChain = chain;
            measuredVersion = version;
            measuredAt = EditorApplication.timeSinceStartup;
            measurement = chain == null || chain.IsEmpty
                ? default
                : ChainSpectrumProbe.Measure(chain, 24, seconds);
        }

        private void DrawBars() {
            var bands = measurement.bands;
            int windows = Mathf.Max(1, measurement.windows);

            // Which measured moment to show. Looping through them in real time is what turns a modulated effect from a
            // static average into something you can see moving.
            int w = 0;
            if (animate && measurement.secondsPerWindow > 0f) {
                double elapsed = EditorApplication.timeSinceStartup - measuredAt;
                w = (int)(elapsed / measurement.secondsPerWindow) % windows;
            }
            else w = windows / 2;

            var area = GUILayoutUtility.GetRect(10f, 220f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.15f));

            float mid = area.y + area.height * 0.5f;
            // The unchanged line. Everything is read relative to this, so it is drawn brightest.
            EditorGUI.DrawRect(new Rect(area.x, mid - 1f, area.width, 2f), new Color(0.45f, 0.45f, 0.5f));
            for (int g = 1; g <= 2; g++) {
                float off = area.height * 0.5f * (g / 3f);
                var faint = new Color(0.25f, 0.25f, 0.28f);
                EditorGUI.DrawRect(new Rect(area.x, mid - off, area.width, 1f), faint);
                EditorGUI.DrawRect(new Rect(area.x, mid + off, area.width, 1f), faint);
            }

            float slot = area.width / bands.Length;
            for (int b = 0; b < bands.Length; b++) {
                float x = area.x + b * slot;
                float bw = Mathf.Max(2f, slot - 2f);

                if (showRange) {
                    // The whole span this band travelled across the measurement, behind the current value: it shows at a
                    // glance which bands are being modulated and which are merely shifted.
                    float top = mid - Mathf.Clamp(bands[b].maxDb / dbRange, -1f, 1f) * area.height * 0.5f;
                    float bot = mid - Mathf.Clamp(bands[b].minDb / dbRange, -1f, 1f) * area.height * 0.5f;
                    if (bot < top) (top, bot) = (bot, top);
                    EditorGUI.DrawRect(new Rect(x, top, bw, Mathf.Max(1f, bot - top)), new Color(0.3f, 0.45f, 0.6f, 0.35f));
                }

                float db = bands[b].overTime[Mathf.Clamp(w, 0, bands[b].overTime.Length - 1)];
                float norm = Mathf.Clamp(db / dbRange, -1f, 1f);
                float h = Mathf.Abs(norm) * area.height * 0.5f;
                var colour = db >= 0f ? new Color(0.45f, 0.8f, 0.5f) : new Color(0.85f, 0.5f, 0.4f);
                var bar = db >= 0f ? new Rect(x, mid - h, bw, h) : new Rect(x, mid, bw, h);
                EditorGUI.DrawRect(bar, colour);
            }

            // Frequency labels, sparse enough to stay readable.
            var labelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
            labelStyle.normal.textColor = new Color(0.6f, 0.6f, 0.65f);
            var labels = GUILayoutUtility.GetRect(10f, 14f, GUILayout.ExpandWidth(true));
            for (int b = 0; b < bands.Length; b += 4) {
                float x = labels.x + b * slot;
                float hz = bands[b].centreHz;
                string text = hz >= 1000f ? (hz / 1000f).ToString("0.#") + "k" : hz.ToString("0");
                GUI.Label(new Rect(x - slot, labels.y, slot * 3f, labels.height), text, labelStyle);
            }
        }

        private void DrawScaleNote() {
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(
                    "Middle line = unchanged. Full height = " + dbRange.ToString("0") + " dB louder or quieter. "
                    + "Faint block = the whole range this band moved through.", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("scale", EditorStyles.miniLabel);
                dbRange = EditorGUILayout.Slider(dbRange, 3f, 36f, GUILayout.Width(110f));
            }
            EditorGUILayout.LabelField(
                "Treat the leftmost bands with suspicion: below roughly 60 Hz a short measurement window holds barely a "
                + "cycle, so the reading there is unstable regardless of what the chain is doing.", EditorStyles.miniLabel);
            if (measurement.broadbandDbOverTime != null && measurement.broadbandDbOverTime.Length > 0) {
                float sum = 0f;
                foreach (var v in measurement.broadbandDbOverTime) sum += v;
                EditorGUILayout.LabelField("Overall level change across everything: "
                    + (sum / measurement.broadbandDbOverTime.Length).ToString("+0.0;-0.0;0.0") + " dB",
                    EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// Names every effect in the chain and how far its contribution can be trusted here. This is the part that keeps
        /// the window honest, and it is not optional decoration.
        /// </summary>
        private void DrawFidelityPanel(ZoundEffectChain chain) {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("What this view is and is not describing", EditorStyles.boldLabel);
            if (chain == null || chain.nodes == null || chain.nodes.Count == 0) {
                EditorGUILayout.LabelField("No effects in this chain.", EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < chain.nodes.Count; i++) {
                var node = chain.nodes[i];
                var fidelity = ChainEffectFidelity.Of(node.type);
                var colour = fidelity == EffectFidelity.Exact ? new Color(0.55f, 0.85f, 0.6f)
                           : fidelity == EffectFidelity.Moving ? new Color(0.6f, 0.75f, 0.95f)
                           : fidelity == EffectFidelity.LevelDependent ? new Color(0.9f, 0.85f, 0.5f)
                           : fidelity == EffectFidelity.TimeSmeared ? new Color(0.7f, 0.6f, 0.9f)
                           : new Color(0.95f, 0.55f, 0.5f);

                using (new EditorGUILayout.HorizontalScope()) {
                    var dot = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(10f));
                    dot.y += 4f;
                    EditorGUI.DrawRect(dot, colour);
                    var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                    if (!node.enabled) style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    EditorGUILayout.LabelField(
                        ZoundEffectDescriptorsName(node.type) + (node.enabled ? "" : "  (off)")
                        + " — " + ChainEffectFidelity.Explain(node.type), style);
                }
            }
        }

        static string ZoundEffectDescriptorsName(ZoundEffectType type) {
            var desc = Dsp.ZoundEffectDescriptors.Get(type);
            return desc != null && !string.IsNullOrEmpty(desc.displayName) ? desc.displayName : type.ToString();
        }

        private void RefreshCandidates() {
            candidates.Clear();
            candidateNames.Clear();
            var project = ZoundsProject.Instance;
            if (project == null || project.zoundLibrary == null) return;

            var klips = project.zoundLibrary.klips;
            for (int i = 0; i < klips.Count; i++) {
                var k = klips[i];
                if (k == null) continue;
                var chain = Dsp.ZoundDspPlayback.ResolveChain(k, out _);
                if (chain == null || chain.IsEmpty) continue;
                candidates.Add(k);
                candidateNames.Add(k.name + "   (" + chain.nodes.Count + " effect"
                                   + (chain.nodes.Count == 1 ? "" : "s") + ")");
            }
        }
    }
}
