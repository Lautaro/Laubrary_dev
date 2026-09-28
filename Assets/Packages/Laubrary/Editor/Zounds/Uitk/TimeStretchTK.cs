using Laubrary.Zounds.Dsp;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// UI Toolkit twin of TimeStretchGUI, the Klip editor's time-stretch strip (T-0461): row one (Stretch, the algorithm
    /// strip, Uniform/Region/Curve, the resulting length), the Live speed row, and — while Stretch is on — the factor,
    /// region and algorithm parameters. Same controls, same sizes, same order, same edits through the same project
    /// paths (and the same one-step Undo per drag). The speed curve's editor arrives with the envelope twin (T-0459).
    /// </summary>
    public class TimeStretchTK : VisualElement {

        const float RowH = 20f;
        readonly Klip klip;

        public TimeStretchTK(Klip klip) {
            this.klip = klip;
            style.flexShrink = 0;
            Build();
        }

        ZoundTimeStretch TS {
            get { if (klip.timeStretch == null) klip.timeStretch = new ZoundTimeStretch(); return klip.timeStretch; }
        }

        void Set(string undo, System.Action a) {
            ZoundsWindow.ModifyAndSaveZoundsProject(undo, () => { a(); ZoundTimeStretcher.Clear(); });
            Prewarm();
            Rebuild();
        }

        /// <summary>Renders the stretched buffer now, as the old strip does after a change or at the end of a drag, so the
        /// cost does not land on the next Play press.</summary>
        void Prewarm() => TimeStretchGUI.Prewarm(klip, ZoundSapPlayback.LoadSourceClip(klip));

        void Rebuild() { Clear(); Build(); }

        static VisualElement Row() {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.height = RowH; r.style.flexShrink = 0;
            return r;
        }
        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }
        static VisualElement Flex() { var e = new VisualElement(); e.style.flexGrow = 1; return e; }

        void Build() {
            var ts = TS;
            ts.EnsureParams();
            var clip = ZoundSapPlayback.LoadSourceClip(klip);
            float clipLength = clip != null ? clip.length : 0f;
            float trimStart = klip.trimEnabled ? klip.trimStart : 0f;
            float trimEnd = klip.trimEnabled && klip.trimEnd > klip.trimStart ? Mathf.Min(klip.trimEnd, clipLength) : clipLength;

            // ── row one ──
            var r1 = Row();
            r1.Add(ZS.Toggle("Stretch", ts.enabled ? "Stop changing the duration; the source plays at its own length." : "Change how long the source lasts without changing its pitch (computed once per setting, ahead of the effect chain).",
                             ts.enabled, on => Set(on ? "enable time stretch" : "disable time stretch", () => ts.enabled = on), "RichToggle", ZUICornerMask.All, 64f, RowH));
            r1.Add(Gap(6f));
            for (int a = 0; a < TimeStretchDescriptors.Count; a++) {
                var d = TimeStretchDescriptors.Get((TimeStretchAlgorithm)a);
                var corner = a == 0 ? ZUICornerMask.Left : a == TimeStretchDescriptors.Count - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
                var alg = d.algorithm;
                var t = ZS.Toggle(d.displayName, d.summary, ts.algorithm == d.algorithm, on => {
                    if (ts.algorithm != alg && d.available) Set("time stretch algorithm", () => { ts.algorithm = alg; ts.algorithmParams = new float[0]; ts.EnsureParams(); });
                    else Rebuild();
                }, "RichToggle", corner, -1f, RowH);
                t.FitToText(16f);
                t.SetEnabled(d.available);
                r1.Add(t);
            }
            r1.Add(Gap(6f));
            string[] modes = { "Uniform", "Region", "Curve" };
            string[] modeTips = {
                "One factor over the whole (trimmed) source.",
                "Only a span of the source is stretched by the factor; the rest plays unchanged.",
                "A speed curve over the source: 1 = unchanged, 0.5 = half speed (twice as long), 2 = double speed."
            };
            for (int m = 0; m < 3; m++) {
                var corner = m == 0 ? ZUICornerMask.Left : m == 2 ? ZUICornerMask.Right : ZUICornerMask.None;
                var mode = (TimeStretchMode)m;
                r1.Add(ZS.Toggle(modes[m], modeTips[m], (int)ts.mode == m, _ => {
                    if (ts.mode != mode) Set("time stretch mode", () => { ts.mode = mode; if (mode == TimeStretchMode.Region && ts.regionEnd <= ts.regionStart) { ts.regionStart = trimStart; ts.regionEnd = trimEnd; } });
                    else Rebuild();
                }, "RichToggle", corner, 62f, RowH));
            }
            r1.Add(Flex());
            if (ts.enabled && clip != null && ts.IsEffective(clipLength)) {
                var len = new Label((trimEnd - trimStart).ToString("0.00") + " s → " + EstimateLength(ts, trimStart, trimEnd).ToString("0.00") + " s") { tooltip = "Source length → stretched length at pitch 1." };
                len.AddToClassList("zs-minilabel");
                len.style.width = 110f;
                r1.Add(len);
            }
            Add(r1);

            Add(BuildLive(ts));

            if (!ts.enabled) return;

            // ── row two: factor / region / algorithm parameters ──
            var r2 = Row();
            if (ts.mode != TimeStretchMode.Envelope) {
                float lmin = Mathf.Log(0.25f), lmax = Mathf.Log(4f);
                float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Clamp(ts.factor, 0.25f, 4f)));
                ZuiSkinSlider s = null;
                var undo = ZS.DragUndo(r2, "time stretch factor", Prewarm);
                s = ZS.Slider("Length ×" + ts.factor.ToString("0.00"), t, 0f, 1f, "Duration multiplier: 2 = twice as long, 0.5 = half as long. Double-click resets to 1.",
                    nt => { undo(); float f = Mathf.Exp(Mathf.Lerp(lmin, lmax, nt)); ts.factor = f; ZoundTimeStretcher.Clear(); EditorUtility.SetDirty(ZoundsProject.Instance); s.text = "Length ×" + f.ToString("0.00"); },
                    ZuiSkinSlider.LabelMode.LabelOnly, 0.5f, "Default", 160f, RowH - 2f);
                r2.Add(s);
                r2.Add(Gap(8f));
            }
            if (ts.mode == TimeStretchMode.Region && clipLength > 0f) {
                var undo = ZS.DragUndo(r2, "time stretch region", Prewarm);
                r2.Add(ZS.MinMax("Region s", Mathf.Clamp(ts.regionStart, trimStart, trimEnd), Mathf.Clamp(ts.regionEnd, trimStart, trimEnd), trimStart, trimEnd,
                    "The span (seconds into the source) that is stretched; everything outside plays unchanged.",
                    (lo, hi) => { undo(); ts.regionStart = lo; ts.regionEnd = hi; ZoundTimeStretcher.Clear(); EditorUtility.SetDirty(ZoundsProject.Instance); },
                    "Default", ZuiSkinMinMax.LabelMode.LabelAndValues, false, 220f, RowH - 2f));
                r2.Add(Gap(8f));
            }
            var desc = TimeStretchDescriptors.Get(ts.algorithm);
            for (int k = 0; k < desc.parameters.Length; k++) {
                var pd = desc.parameters[k];
                int pk = k;
                float v = ts.algorithmParams[k];
                var undo = ZS.DragUndo(r2, "time stretch parameter", Prewarm);
                ZuiSkinSlider s = null;
                if (pd.curve == ParamCurve.Logarithmic) {
                    float lmin = Mathf.Log(pd.min), lmax = Mathf.Log(pd.max);
                    float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Max(v, pd.min)));
                    s = ZS.Slider(pd.name + " " + v.ToString("0") + " " + pd.unit, t, 0f, 1f, ParamTip(ts.algorithm, k),
                        nt => { undo(); float nv = Mathf.Exp(Mathf.Lerp(lmin, lmax, nt)); ts.algorithmParams[pk] = nv; ZoundTimeStretcher.Clear(); EditorUtility.SetDirty(ZoundsProject.Instance); s.text = pd.name + " " + nv.ToString("0") + " " + pd.unit; },
                        ZuiSkinSlider.LabelMode.LabelOnly, Mathf.InverseLerp(lmin, lmax, Mathf.Log(pd.def)), "Default", 120f, RowH - 2f);
                }
                else {
                    s = ZS.Slider(pd.name, v, pd.min, pd.max, ParamTip(ts.algorithm, k),
                        nv => { undo(); ts.algorithmParams[pk] = nv; ZoundTimeStretcher.Clear(); EditorUtility.SetDirty(ZoundsProject.Instance); },
                        ZuiSkinSlider.LabelMode.LabelAndValue, pd.def, "Default", 120f, RowH - 2f);
                }
                r2.Add(s);
                r2.Add(Gap(4f));
            }
            r2.Add(Flex());
            Add(r2);

            if (ts.mode == TimeStretchMode.Envelope) {
                // The speed curve (56 px, full width), drawn by the envelope editor's twin in the pitch colour.
                if (ts.speedEnvelope == null) ts.speedEnvelope = new Envelope(0.25f, 4f);
                var curve = new EnvelopeTK(ts.speedEnvelope, ZoundsProject.Instance.projectSettings.editorStyle.pitchEnvelopeColor) {
                    tooltip = "Playback speed over the source (left = start, right = end): 1 = unchanged, below 1 = slower and longer, above 1 = faster and shorter. Drag points; double-click to add one."
                };
                curve.style.height = 56f; curve.style.flexShrink = 0;
                var curveUndo = ZS.DragUndo(curve, "edit stretch curve", Prewarm);
                curve.onBegin = curveUndo;
                curve.onChanged = () => { ZoundTimeStretcher.Clear(); EditorUtility.SetDirty(ZoundsProject.Instance); };
                Add(curve);
            }
        }

        /// <summary>The Live speed row (TimeStretchGUI.DrawLive).</summary>
        VisualElement BuildLive(ZoundTimeStretch ts) {
            var r = Row();
            r.Add(ZS.Toggle("Live speed", ts.liveEnabled
                    ? "Stop the live stretcher: the sound reads its source directly again, and Speed, game code's speed and any modifier on Speed are no longer heard."
                    : "Let this sound's speed change while it plays without changing its pitch — from the Speed setting, a modifier on Speed, or game code (a bullet-time slowdown). Costs about one percent of a CPU core per playing copy.",
                ts.liveEnabled, on => Set(on ? "enable live speed" : "disable live speed", () => ts.liveEnabled = on), "RichToggle", ZUICornerMask.All, 84f, RowH));
            if (ts.liveEnabled) {
                r.Add(Gap(6f));
                var spd = ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Speed];
                float lmin = Mathf.Log(spd.min), lmax = Mathf.Log(spd.max);
                float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Clamp(ts.liveSpeed, spd.min, spd.max)));
                var undo = ZS.DragUndo(r, "live speed", Prewarm);
                ZuiSkinSlider s = null;
                s = ZS.Slider("Speed ×" + ts.liveSpeed.ToString("0.00"), t, 0f, 1f,
                    "How fast the sound moves through its source, without changing its pitch: 0.5 is half speed (twice as long), 2 is double. Heard immediately, even on a sound already playing. Game code's speed multiplies on top. Right-click to drive it with a modifier. Double-click resets to 1.",
                    nt => { undo(); float sp = Mathf.Exp(Mathf.Lerp(lmin, lmax, nt)); ts.liveSpeed = sp; EditorUtility.SetDirty(ZoundsProject.Instance); SapVoiceRegistry.PushAuthoredSpeed(klip, sp); s.text = "Speed ×" + sp.ToString("0.00"); },
                    ZuiSkinSlider.LabelMode.LabelOnly, Mathf.InverseLerp(lmin, lmax, 0f), "Default", 150f, RowH - 2f);
                r.Add(s);
                r.Add(Gap(6f));
                var wundo = ZS.DragUndo(r, "live stretch window", Prewarm);
                ZuiSkinSlider ws = null;
                ws = ZS.Slider("Window " + ts.liveWindowMs.ToString("0") + " ms", ts.liveWindowMs, 10f, 100f,
                    "Length of the pieces the sound is cut into to stretch it. 20-30 ms suits speech and hits; 40-50 ms suits pads, chords and engines. Applies from the next play.",
                    w => { wundo(); ts.liveWindowMs = Mathf.Round(w); EditorUtility.SetDirty(ZoundsProject.Instance); ws.text = "Window " + ts.liveWindowMs.ToString("0") + " ms"; },
                    ZuiSkinSlider.LabelMode.LabelOnly, 30f, "Default", 120f, RowH - 2f);
                r.Add(ws);
                r.Add(Gap(6f));
                r.Add(ZS.Toggle("Keep hits", ts.liveKeepHits
                        ? "Stretch hits too: attacks are then smeared, and at slow speeds can repeat like a machine gun. Applies from the next play."
                        : "Play each hit (attack) once, whole, at normal speed, and stretch only the material between hits, so shots and clicks stay sharp and never double. Applies from the next play.",
                    ts.liveKeepHits, on => Set("live stretch keep hits", () => ts.liveKeepHits = on), "RichToggle", ZUICornerMask.All, 72f, RowH));
                r.Add(Gap(6f));
                string[] names = { "WSOLA", "Granular" };
                string[] tips = {
                    "Lines each piece up with the previous one, so tones stay clean and pitch stays exact. The right choice for almost everything. Applies from the next play.",
                    "Overlaps pieces without lining them up: rougher and grainier, which can suit magic, roars and textures as a deliberate character. Applies from the next play."
                };
                for (int a = 0; a < 2; a++) {
                    var alg = (LiveStretchAlgorithm)a;
                    r.Add(ZS.Toggle(names[a], tips[a], (int)ts.liveAlgorithm == a, _ => {
                        if (ts.liveAlgorithm != alg) Set("live stretch algorithm", () => ts.liveAlgorithm = alg); else Rebuild();
                    }, "RichToggle", a == 0 ? ZUICornerMask.Left : ZUICornerMask.Right, a == 0 ? 60f : 70f, RowH));
                }
            }
            r.Add(Flex());
            return r;
        }

        const string KeepHitsTip = "Every hit (attack) is kept intact for this long and only the material between hits is stretched, so a percussive sound is not repeated like a machine gun. 0 stretches everything.";

        static string ParamTip(TimeStretchAlgorithm a, int k) {
            if (a == TimeStretchAlgorithm.Granular) {
                switch (k) {
                    case 0: return "Grain length. Short grains follow fast material; long grains sound smoother on sustained sounds.";
                    case 1: return "How much consecutive grains overlap. More overlap is smoother and costlier.";
                    case 2: return "Random offset of each grain's read position, to break the buzz long stretches get at the grain rate.";
                    default: return KeepHitsTip;
                }
            }
            switch (k) {
                case 0: return "Analysis window. Longer keeps low frequencies intact; shorter follows transients.";
                case 1: return "How far each window may shift to line up with the previous one.";
                default: return KeepHitsTip;
            }
        }

        static float EstimateLength(ZoundTimeStretch ts, float trimStart, float trimEnd) {
            float len = trimEnd - trimStart;
            switch (ts.mode) {
                case TimeStretchMode.Uniform: return len * ts.factor;
                case TimeStretchMode.Region: {
                    float region = Mathf.Max(0f, Mathf.Min(ts.regionEnd, trimEnd) - Mathf.Max(ts.regionStart, trimStart));
                    return len - region + region * ts.factor;
                }
                default: {
                    double sum = 0; const int n = 200;
                    for (int i = 0; i < n; i++) sum += (len / n) / Mathf.Clamp(ts.speedEnvelope.Evaluate((i + 0.5f) / n), 0.05f, 20f);
                    return (float)sum;
                }
            }
        }
    }
}
