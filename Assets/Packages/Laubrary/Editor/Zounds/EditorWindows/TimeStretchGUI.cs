using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// The Klip editor's time-stretch strip: on/off, algorithm, mode, and the mode's own control
    /// (factor, region, or a speed curve) plus the algorithm's parameters, packed into as few rows as
    /// the pane width allows. The resulting play length is shown inline.
    /// </summary>
    internal class TimeStretchGUI {

        private const float RowH = 20f;
        private EnvelopeGUI curveGui;
        private bool dragOpen;
        private static readonly GUIContent tmp = new GUIContent();

        public bool isDragging => dragOpen;

        private AudioClip lastClip;

        private void Set(Klip klip, string undo, System.Action a) {
            ZoundsWindow.ModifyAndSaveZoundsProject(undo, () => { a(); ZoundTimeStretcher.Clear(); });
            Prewarm(klip);
        }

        private void Drag(Klip klip, string undo, System.Action a) {
            if (!dragOpen) { dragOpen = true; ZoundsWindow.BeginDragUndo(undo); }
            a();
            ZoundTimeStretcher.Clear();
            EditorUtility.SetDirty(ZoundsProject.Instance);
        }

        // The stretched buffer is rendered here, when a setting changes, instead of on the next Play press:
        // WSOLA on a short clip took ~200 ms and that pause landed on the play button (the "UI briefly
        // locks up when it happens" in the 2026-09-18 report).
        private void Prewarm(Klip klip) {
            var ts = klip.timeStretch;
            if (ts == null || lastClip == null || !ts.enabled) return;
            float clipLength = lastClip.length;
            if (!ts.IsEffective(clipLength)) return;
            var src = ZoundPcmCache.Get(lastClip);
            if (src == null || !src.valid) return;
            float trimStart = klip.trimEnabled ? klip.trimStart : 0f;
            float trimEnd = klip.trimEnabled && klip.trimEnd > klip.trimStart ? Mathf.Min(klip.trimEnd, clipLength) : clipLength;
            ZoundTimeStretcher.Get(src, ts, trimStart, trimEnd);
        }

        public void Draw(Klip klip, AudioClip clip) {
            lastClip = clip;
            var ts = klip.timeStretch;
            if (ts == null) { ts = klip.timeStretch = new ZoundTimeStretch(); }
            ts.EnsureParams();
            float clipLength = clip != null ? clip.length : 0f;
            float trimStart = klip.trimEnabled ? klip.trimStart : 0f;
            float trimEnd = klip.trimEnabled && klip.trimEnd > klip.trimStart ? Mathf.Min(klip.trimEnd, clipLength) : clipLength;
            var evt = Event.current;

            GUILayout.BeginHorizontal(GUILayout.Height(RowH));
            {
                bool on = ZUI.Toggle(ts.enabled, new GUIContent("Stretch", ts.enabled ? "Stop changing the duration; the source plays at its own length." : "Change how long the source lasts without changing its pitch (computed once per setting, ahead of the effect chain)."), ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Width(64f), GUILayout.Height(RowH));
                if (on != ts.enabled) Set(klip, on ? "enable time stretch" : "disable time stretch", () => ts.enabled = on);

                GUILayout.Space(6f);
                for (int a = 0; a < TimeStretchDescriptors.Count; a++) {
                    var d = TimeStretchDescriptors.Get((TimeStretchAlgorithm)a);
                    var corner = a == 0 ? ZUICornerMask.Left : a == TimeStretchDescriptors.Count - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
                    var prev = GUI.enabled; GUI.enabled = prev && d.available;
                    bool sel = ts.algorithm == d.algorithm;
                    tmp.text = d.displayName; tmp.tooltip = d.summary;
                    float w = EditorStyles.label.CalcSize(tmp).x + 16f;
                    if (ZUI.Toggle(sel, tmp, ZUI.Style.RichToggle, corner, GUILayout.Width(w), GUILayout.Height(RowH)) && !sel && d.available) {
                        var alg = d.algorithm;
                        Set(klip, "time stretch algorithm", () => { ts.algorithm = alg; ts.algorithmParams = new float[0]; ts.EnsureParams(); });
                    }
                    GUI.enabled = prev;
                }

                GUILayout.Space(6f);
                string[] modes = { "Uniform", "Region", "Curve" };
                string[] modeTips = {
                    "One factor over the whole (trimmed) source.",
                    "Only a span of the source is stretched by the factor; the rest plays unchanged.",
                    "A speed curve over the source: 1 = unchanged, 0.5 = half speed (twice as long), 2 = double speed."
                };
                for (int m = 0; m < 3; m++) {
                    var corner = m == 0 ? ZUICornerMask.Left : m == 2 ? ZUICornerMask.Right : ZUICornerMask.None;
                    bool sel = (int)ts.mode == m;
                    if (ZUI.Toggle(sel, new GUIContent(modes[m], modeTips[m]), ZUI.Style.RichToggle, corner, GUILayout.Width(62f), GUILayout.Height(RowH)) && !sel) {
                        var mode = (TimeStretchMode)m;
                        Set(klip, "time stretch mode", () => { ts.mode = mode; if (mode == TimeStretchMode.Region && ts.regionEnd <= ts.regionStart) { ts.regionStart = trimStart; ts.regionEnd = trimEnd; } });
                    }
                }

                GUILayout.FlexibleSpace();
                if (ts.enabled && clip != null && ts.IsEffective(clipLength)) {
                    float outLen = EstimateLength(ts, trimStart, trimEnd);
                    tmp.text = (trimEnd - trimStart).ToString("0.00") + " s → " + outLen.ToString("0.00") + " s";
                    tmp.tooltip = "Source length → stretched length at pitch 1.";
                    GUILayout.Label(tmp, EditorStyles.miniLabel, GUILayout.Width(110f));
                }
            }
            GUILayout.EndHorizontal();

            if (!ts.enabled) return;

            GUILayout.BeginHorizontal(GUILayout.Height(RowH));
            {
                if (ts.mode != TimeStretchMode.Envelope) {
                    // Factor: log slider 0.25x .. 4x with the real value in the label.
                    float lmin = Mathf.Log(0.25f), lmax = Mathf.Log(4f);
                    float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Clamp(ts.factor, 0.25f, 4f)));
                    var rect = GUILayoutUtility.GetRect(160f, RowH - 2f, GUILayout.Width(160f));
                    float nt = ZUI.MicroSlider(rect, t, 0f, 1f, "Length ×" + ts.factor.ToString("0.00"), ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, 0.5f);
                    GUI.Label(rect, new GUIContent("", "Duration multiplier: 2 = twice as long, 0.5 = half as long. Double-click resets to 1."));
                    if (!Mathf.Approximately(nt, t)) { float f = Mathf.Exp(Mathf.Lerp(lmin, lmax, nt)); Drag(klip, "time stretch factor", () => ts.factor = f); }
                    GUILayout.Space(8f);
                }
                if (ts.mode == TimeStretchMode.Region && clipLength > 0f) {
                    float lo = Mathf.Clamp(ts.regionStart, trimStart, trimEnd), hi = Mathf.Clamp(ts.regionEnd, trimStart, trimEnd);
                    var rect = GUILayoutUtility.GetRect(220f, RowH - 2f, GUILayout.Width(220f));
                    ZUI.MicroMinMax(rect, ref lo, ref hi, trimStart, trimEnd, "Region s");
                    GUI.Label(rect, new GUIContent("", "The span (seconds into the source) that is stretched; everything outside plays unchanged."));
                    if (!Mathf.Approximately(lo, ts.regionStart) || !Mathf.Approximately(hi, ts.regionEnd)) Drag(klip, "time stretch region", () => { ts.regionStart = lo; ts.regionEnd = hi; });
                    GUILayout.Space(8f);
                }
                // Algorithm parameters, packed on the same row.
                var desc = TimeStretchDescriptors.Get(ts.algorithm);
                for (int k = 0; k < desc.parameters.Length; k++) {
                    var pd = desc.parameters[k];
                    int pk = k;
                    var rect = GUILayoutUtility.GetRect(120f, RowH - 2f, GUILayout.Width(120f));
                    float v = ts.algorithmParams[k];
                    float nv;
                    if (pd.curve == ParamCurve.Logarithmic) {
                        float lmin = Mathf.Log(pd.min), lmax = Mathf.Log(pd.max);
                        float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Max(v, pd.min)));
                        float nt = ZUI.MicroSlider(rect, t, 0f, 1f, pd.name + " " + v.ToString("0") + " " + pd.unit, ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, Mathf.InverseLerp(lmin, lmax, Mathf.Log(pd.def)));
                        nv = Mathf.Approximately(nt, t) ? v : Mathf.Exp(Mathf.Lerp(lmin, lmax, nt));
                    }
                    else {
                        nv = ZUI.MicroSlider(rect, v, pd.min, pd.max, pd.name, ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelAndValue, pd.def);
                    }
                    GUI.Label(rect, new GUIContent("", ParamTip(ts.algorithm, k)));
                    if (!Mathf.Approximately(nv, v)) Drag(klip, "time stretch parameter", () => ts.algorithmParams[pk] = nv);
                    GUILayout.Space(4f);
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();

            if (ts.mode == TimeStretchMode.Envelope) {
                if (ts.speedEnvelope == null) ts.speedEnvelope = new Envelope(0.25f, 4f);
                if (curveGui == null) curveGui = new EnvelopeGUI { name = "stretch" };
                var rect = GUILayoutUtility.GetRect(200f, 56f, GUILayout.ExpandWidth(true));
                GUI.Label(rect, new GUIContent("", "Playback speed over the source (left = start, right = end): 1 = unchanged, below 1 = slower and longer, above 1 = faster and shorter. Drag points; double-click to add one."));
                if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition) && !dragOpen) { dragOpen = true; ZoundsWindow.BeginDragUndo("edit stretch curve"); }
                if (curveGui.Draw(rect, ts.speedEnvelope, ZoundsProject.Instance.projectSettings.editorStyle.pitchEnvelopeColor, 1.5f, true, true)) {
                    ZoundTimeStretcher.Clear();
                    EditorUtility.SetDirty(ZoundsProject.Instance);
                }
            }

            if (evt.rawType == EventType.MouseUp && dragOpen) { dragOpen = false; ZoundsWindow.EndDragUndo(); Prewarm(klip); }
        }

        private const string KeepHitsTip = "Every hit (attack) is kept intact for this long and only the material between hits is stretched, so a percussive sound is not repeated like a machine gun. 0 stretches everything.";

        private static string ParamTip(TimeStretchAlgorithm a, int k) {
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

        private static float EstimateLength(ZoundTimeStretch ts, float trimStart, float trimEnd) {
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
