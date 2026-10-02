using System;
using System.Collections.Generic;
using Laubrary.Zounds.Dsp;
using Laubrary.Zounds.EditorTools;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// UI Toolkit twin of the chain analyser model (T-0467). It holds an ordinary <see cref="ChainAnalyserPanel"/> as its
    /// model and asks it for everything: the per-frame state (<c>Step</c>), the words (status, time, lane and band texts,
    /// roster) and the shapes (<c>PaintLane</c>, <c>PaintBars</c>, the live views' <c>Paint</c>), which it draws through
    /// <see cref="ZuiRectCanvas"/> — the same geometry the IMGUI model draws through EditorGUI.DrawRect. Layout follows the
    /// old model row for row: the header (Analyse toggle, view tabs, band note or gain), one status line, a lane per
    /// modulated parameter, the 170 px graph, the frequency labels, the time line, and the effect roster.
    /// </summary>
    public class ChainAnalyserTK : VisualElement {

        const float GraphH = 170f;
        static readonly Color NoteColour = new Color(0.62f, 0.62f, 0.68f);

        readonly Zound zound;
        readonly ChainAnalyserPanel model = new ChainAnalyserPanel();   // the old panel, used as the model
        readonly List<Action> perFrame = new List<Action>();
        string builtSig;
        float playTime; bool following;
        int hoveredBand = -1;

        public ChainAnalyserTK(Zound zound) {
            this.zound = zound;
            AddToClassList("zs-chain-analyser__root");
            schedule.Execute(Tick).Every(33);
            RegisterCallback<AttachToPanelEvent>(_ => Tick());
        }

        ZoundEffectChain Chain => ZoundDspPlayback.ResolveChain(zound, out _);

        void Tick() {
            if (base.panel == null) return;
            var chain = Chain;
            string sig = Signature(chain);
            if (sig != builtSig) { builtSig = sig; Build(chain); }
            if (!model.open) return;
            model.Step(zound, chain, model.view == ChainAnalyserPanel.View.Combined, out playTime, out following);
            foreach (var a in perFrame) a();
        }

        string Signature(ZoundEffectChain chain) {
            var sb = new System.Text.StringBuilder();
            sb.Append(model.open).Append((int)model.view).Append('|').Append(ChainAnalyserPanel.LaneCount(chain)).Append('|');
            if (chain?.nodes != null) foreach (var n in chain.nodes) sb.Append((int)n.type).Append(n.enabled ? '+' : '-');
            sb.Append(chain == null || chain.IsEmpty ? 'E' : 'F');
            return sb.ToString();
        }

        // ─────────────────────────── helpers ───────────────────────────

        static VisualElement HRow(float h) {
            var r = new VisualElement();
            r.AddToClassList("zs-chain-analyser__row");
            if (h > 0f) r.style.height = h;
            return r;
        }

        static Label Mini(string text, Color? colour = null, TextAnchor align = TextAnchor.MiddleLeft) {
            var l = new Label(text);
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-mini");
            if (colour.HasValue) l.style.color = colour.Value;
            l.style.unityTextAlign = align;
            return l;
        }

        /// <summary>The old StatusLine: a reserved single-line row, the message clipped into it with the whole text on hover.</summary>
        Label StatusLine(Func<string> text, Func<string> tip = null) {
            var l = Mini("", NoteColour);
            l.style.height = EditorGUIUtility.singleLineHeight; l.AddToClassList("zs-chain-analyser__status-line-label");
            perFrame.Add(() => { var t = text(); l.text = t ?? ""; l.tooltip = t == null ? "" : tip?.Invoke() ?? t; });
            return l;
        }

        // ─────────────────────────── build ───────────────────────────

        void Build(ZoundEffectChain chain) {
            Clear();
            perFrame.Clear();
            Add(Header());
            if (!model.open) return;

            bool combined = model.view == ChainAnalyserPanel.View.Combined;
            if (combined) Add(StatusLine(() => model.CombinedStatus(Chain)));
            else Add(StatusLine(() => ChainAnalyserPanel.LiveStatus(liveplaying, liveSource)));

            int lanes = ChainAnalyserPanel.LaneCount(chain);
            for (int r = 0; r < lanes; r++) Add(Lane(r));

            if (combined) {
                Add(BarsGraph());
                Add(FrequencyLabels());
                Add(StatusLine(() => model.TimeText(playTime, following), () => model.TimeTip(following)));
            }
            else {
                Add(LiveGraph());
                var spacer = new VisualElement(); spacer.AddToClassList("zs-chain-analyser__spacer");
                Add(spacer);
                Add(StatusLine(() => ChainAnalyserPanel.LiveFooter(liveplaying)));
            }
            Roster(chain, combined);
        }

        VisualElement Header() {
            var r = HRow(-1f);
            var toggle = ZS.Toggle(model.open ? "Analyse ▾" : "Analyse ▸", model.open ? "Hide the chain's measurement graphs." : "Show the chain's measured processing and live output graphs.", model.open, v => { model.open = v; Tick(); },
                                   "RichToggle", ZUICornerMask.All, 90f, 30.52f);
            r.Add(toggle);
            if (!model.open) {
                var l = new Label("see what this chain is doing");
                l.AddToClassList("zs-lbl"); l.AddToClassList("zs-text-default"); l.AddToClassList("zs-subtle");
                r.Add(l);
                return r;
            }
            var gap = new VisualElement(); gap.AddToClassList("zs-chain-analyser__header-gap"); r.Add(gap);
            Tab(r, ChainAnalyserPanel.View.Combined, "Combined");
            Tab(r, ChainAnalyserPanel.View.LiveSpectrum, "Spectrum");
            Tab(r, ChainAnalyserPanel.View.LiveOverTime, "Over time");
            Tab(r, ChainAnalyserPanel.View.LiveWaveform, "Waveform");
            var flex = new VisualElement(); flex.AddToClassList("zs-chain-analyser__header-spacer"); r.Add(flex);
            if (model.view == ChainAnalyserPanel.View.Combined) {
                var note = Mini(ChainAnalyserPanel.Bands + " bands, 20 Hz–20 kHz", NoteColour);
                note.AddToClassList("zs-chain-analyser__header-note"); note.style.height = EditorGUIUtility.singleLineHeight;
                note.tooltip = "The whole range of hearing, 20 Hz to 20 kHz, in " + ChainAnalyserPanel.Bands + " bands, with the vertical scale fitted to whatever was "
                             + "measured. The bars are live: every frame they show what the chain does at that instant.";
                r.Add(note);
            }
            else {
                var l = new Label("gain");
                l.AddToClassList("zs-lbl"); l.AddToClassList("zs-text-default"); l.AddToClassList("zs-subtle");
                r.Add(l);
                // EditorGUILayout.Slider: UI Toolkit's own slider with its number box, 90 px, 0.25 to 16.
                var s = new Slider(0.25f, 16f) { value = model.Live.gain, showInputField = true, tooltip = "Magnify the live graph without changing the sound's loudness." };
                s.AddToClassList("zs-chain-analyser__mode-selector");   // IMGUI's 3 px field margin
                s.AddToClassList("zs-narrowslider");
                s.RegisterValueChangedCallback(e => model.Live.gain = e.newValue);
                r.Add(s);
            }
            return r;
        }

        void Tab(VisualElement row, ChainAnalyserPanel.View which, string label) {
            bool on = model.view == which;
            string tip = which == ChainAnalyserPanel.View.Combined ? "Inspect the combined effect chain's measured response."
                : which == ChainAnalyserPanel.View.LiveSpectrum ? "Inspect the frequencies in the currently playing output."
                : which == ChainAnalyserPanel.View.LiveOverTime ? "Inspect how the playing output's frequencies change over time."
                : "Inspect the currently playing output waveform.";
            ZuiToggleButton t = null;
            t = ZS.Toggle(label, tip, on, v => { if (model.view == which) { t.SetValueWithoutNotify(true); return; } model.view = which; Tick(); },
                          "RichToggle", ZUICornerMask.All, 74f, 30.52f);
            row.Add(t);
        }

        VisualElement Lane(int r) {
            var row = HRow(ChainAnalyserPanel.LaneHeight);
            var label = Mini("");
            label.AddToClassList("zs-chain-analyser__lane-label");
            var canvas = new ZuiRectCanvas((area, fill) => model.PaintLane(r, area, playTime, following, fill));
            canvas.AddToClassList("zs-chain-analyser__lane-canvas");
            row.Add(label); row.Add(canvas);
            perFrame.Add(() => {
                model.LaneText(r, following, out string text, out string tip);
                label.text = text; label.tooltip = tip;
                canvas.MarkDirtyRepaint();
            });
            return row;
        }

        VisualElement BarsGraph() {
            var holder = new VisualElement();
            holder.AddToClassList("zs-chain-analyser__bars-graph-holder");
            int unmeasurable = 0;
            var canvas = new ZuiRectCanvas((area, fill) => {
                if (Chain == null || Chain.IsEmpty || !model.HasPicture) { ChainAnalyserPanel.PaintEmptyGraph(area, fill); unmeasurable = 0; }
                else unmeasurable = model.PaintBars(area, hoveredBand, fill);
            });
            canvas.AddToClassList("zs-chain-analyser__bars-graph-canvas");
            holder.Add(canvas);
            Label Scale(float top, bool fromBottom) {
                var l = Mini("", new Color(0.7f, 0.7f, 0.76f), TextAnchor.UpperLeft);
                l.AddToClassList("zs-chain-analyser__axis-label");
                if (fromBottom) l.AddToClassList("zs-chain-analyser__axis-label--bottom"); else l.style.top = top;
                holder.Add(l);
                return l;
            }
            var top = Scale(1f, false);
            var zero = Scale(GraphH * 0.5f - 15f, false);
            var bottom = Scale(0f, true);
            var hover = Mini("", new Color(0.95f, 0.95f, 1f), TextAnchor.UpperRight);
            hover.AddToClassList("zs-chain-analyser__bars-graph-hover");
            holder.Add(hover);
            holder.RegisterCallback<PointerMoveEvent>(e => hoveredBand = ChainAnalyserPanel.BandAt(e.localPosition.x / Mathf.Max(1f, holder.layout.width)));
            holder.RegisterCallback<PointerLeaveEvent>(_ => hoveredBand = -1);
            perFrame.Add(() => {
                bool bars = Chain != null && !Chain.IsEmpty && model.HasPicture;
                top.text = bars ? model.ScaleTop : ""; zero.text = bars ? "0 dB" : ""; bottom.text = bars ? model.ScaleBottom : "";
                hover.text = bars && hoveredBand >= 0 ? model.BandHoverText(hoveredBand) : "";
                holder.tooltip = bars ? model.BarsTip(unmeasurable) : "";
                canvas.MarkDirtyRepaint();
            });
            return holder;
        }

        VisualElement FrequencyLabels() {
            var row = new VisualElement();
            row.AddToClassList("zs-chain-analyser__frequency-labels-row");
            var labels = new List<(Label l, float x01)>();
            foreach (var (text, x01) in ChainAnalyserPanel.FrequencyLabels()) {
                var l = Mini(text, new Color(0.6f, 0.6f, 0.65f), TextAnchor.UpperCenter);
                l.AddToClassList("zs-chain-analyser__frequency-labels-label");
                row.Add(l); labels.Add((l, x01));
            }
            // Only when there is a picture: the empty graph reserves the row but draws no labels, as the old one does.
            row.RegisterCallback<GeometryChangedEvent>(_ => {
                float w = row.layout.width;
                foreach (var (l, x01) in labels) {
                    float x = x01 * w - 20f;
                    if (x < 0f) x = 0f;
                    if (x + 40f > w) x = w - 40f;
                    l.style.left = x;
                }
            });
            perFrame.Add(() => {
                bool show = Chain != null && !Chain.IsEmpty && model.HasPicture;
                foreach (var (l, _) in labels) l.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            });
            return row;
        }

        bool liveplaying; string liveSource;

        VisualElement LiveGraph() {
            var holder = new VisualElement();
            holder.AddToClassList("zs-chain-analyser__live-graph-holder");
            var mode = model.view == ChainAnalyserPanel.View.LiveSpectrum ? LiveOutputView.Mode.Spectrum
                     : model.view == ChainAnalyserPanel.View.LiveOverTime ? LiveOutputView.Mode.Spectrogram
                     : LiveOutputView.Mode.Waveform;
            var canvas = new ZuiRectCanvas((area, fill) => model.Live.Paint(area, mode, fill));
            canvas.AddToClassList("zs-chain-analyser__live-graph-canvas");
            holder.Add(canvas);
            var peak = Mini("", null, TextAnchor.UpperRight);
            peak.AddToClassList("zs-chain-analyser__live-graph-peak");
            var state = Mini("", null, TextAnchor.UpperLeft);
            state.AddToClassList("zs-chain-analyser__live-graph-state");
            holder.Add(peak); holder.Add(state);
            perFrame.Insert(0, () => { liveplaying = model.Live.Sample(out liveSource); });
            perFrame.Add(() => {
                canvas.MarkDirtyRepaint();
                state.text = LiveOutputView.StateText(liveplaying, liveSource);
                state.style.color = LiveOutputView.StateColour(liveplaying);
                bool wave = mode == LiveOutputView.Mode.Waveform;
                peak.style.display = wave ? DisplayStyle.Flex : DisplayStyle.None;
                if (wave) {
                    peak.text = model.Live.PeakText;
                    peak.style.color = model.Live.clipping ? new Color(1f, 0.5f, 0.45f) : new Color(0.6f, 0.6f, 0.65f);
                }
            });
            return holder;
        }

        void Roster(ZoundEffectChain chain, bool verdicts) {
            if (chain?.nodes == null) return;
            for (int i = 0; i < chain.nodes.Count; i++) {
                int ni = i;
                var row = new VisualElement();
                row.style.height = EditorGUIUtility.singleLineHeight; row.AddToClassList("zs-chain-analyser__roster-row");
                var dot = new VisualElement();
                dot.AddToClassList("zs-chain-analyser__roster-dot");
                dot.style.top = (EditorGUIUtility.singleLineHeight - 9f) * 0.5f;
                var text = Mini("");
                text.AddToClassList("zs-chain-analyser__roster-text");
                row.Add(dot); row.Add(text);
                Add(row);
                perFrame.Add(() => {
                    var ch = Chain;
                    if (ch == null || ni >= ch.nodes.Count) return;
                    var node = ch.nodes[ni];
                    ChainAnalyserPanel.RosterRow(node, verdicts, out Color c, out string t, out string tip);
                    dot.style.backgroundColor = c;
                    text.text = t; text.tooltip = tip;
                    text.style.color = node.enabled ? StyleKeyword.Null : new StyleColor(new Color(0.5f, 0.5f, 0.5f));
                });
            }
        }
    }
}
