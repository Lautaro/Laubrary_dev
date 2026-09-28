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

        public void Draw(Klip klip, AudioClip clip, System.Action<int> sourceParamMenu = null) {
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

            DrawLive(klip, ts, sourceParamMenu);

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

        /// <summary>
        /// Live speed (T-0409): one row. The switch, then — when on — the speed (heard immediately, even on a sound already
        /// playing; right-click to drive it with a modifier), the window, Keep hits and the algorithm (those three apply
        /// from the next play, since they size the stretcher when a play starts).
        /// </summary>
        private void DrawLive(Klip klip, ZoundTimeStretch ts, System.Action<int> sourceParamMenu) {
            var evt = Event.current;
            GUILayout.BeginHorizontal(GUILayout.Height(RowH));
            {
                bool on = ZUI.Toggle(ts.liveEnabled, new GUIContent("Live speed", ts.liveEnabled
                        ? "Stop the live stretcher: the sound reads its source directly again, and Speed, game code's speed and any modifier on Speed are no longer heard."
                        : "Let this sound's speed change while it plays without changing its pitch — from the Speed setting, a modifier on Speed, or game code (a bullet-time slowdown). Costs about one percent of a CPU core per playing copy."),
                    ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Width(84f), GUILayout.Height(RowH));
                if (on != ts.liveEnabled) Set(klip, on ? "enable live speed" : "disable live speed", () => ts.liveEnabled = on);

                if (ts.liveEnabled) {
                    GUILayout.Space(6f);
                    // Speed: log slider 0.1x .. 4x, the same range as the Speed parameter a modifier drives.
                    var spd = ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Speed];
                    float lmin = Mathf.Log(spd.min), lmax = Mathf.Log(spd.max);
                    float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Clamp(ts.liveSpeed, spd.min, spd.max)));
                    var rect = GUILayoutUtility.GetRect(150f, RowH - 2f, GUILayout.Width(150f));
                    if (evt.type == EventType.MouseDown && evt.button == 1 && rect.Contains(evt.mousePosition) && sourceParamMenu != null) {
                        sourceParamMenu(SourceStageParam.Speed); evt.Use();
                    }
                    float nt = ZUI.MicroSlider(rect, t, 0f, 1f, "Speed ×" + ts.liveSpeed.ToString("0.00"), ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, Mathf.InverseLerp(lmin, lmax, 0f));
                    GUI.Label(rect, new GUIContent("", "How fast the sound moves through its source, without changing its pitch: 0.5 is half speed (twice as long), 2 is double. Heard immediately, even on a sound already playing. Game code's speed multiplies on top. Right-click to drive it with a modifier. Double-click resets to 1."));
                    if (!Mathf.Approximately(nt, t)) {
                        float s = Mathf.Exp(Mathf.Lerp(lmin, lmax, nt));
                        Drag(klip, "live speed", () => ts.liveSpeed = s);
                        SapVoiceRegistry.PushAuthoredSpeed(klip, s);
                    }

                    GUILayout.Space(6f);
                    var wrect = GUILayoutUtility.GetRect(120f, RowH - 2f, GUILayout.Width(120f));
                    float w = ZUI.MicroSlider(wrect, ts.liveWindowMs, 10f, 100f, "Window " + ts.liveWindowMs.ToString("0") + " ms", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, 30f);
                    GUI.Label(wrect, new GUIContent("", "Length of the pieces the sound is cut into to stretch it. 20-30 ms suits speech and hits; 40-50 ms suits pads, chords and engines. Applies from the next play."));
                    if (!Mathf.Approximately(w, ts.liveWindowMs)) Drag(klip, "live stretch window", () => ts.liveWindowMs = Mathf.Round(w));

                    GUILayout.Space(6f);
                    bool keep = ZUI.Toggle(ts.liveKeepHits, new GUIContent("Keep hits", ts.liveKeepHits
                            ? "Stretch hits too: attacks are then smeared, and at slow speeds can repeat like a machine gun. Applies from the next play."
                            : "Play each hit (attack) once, whole, at normal speed, and stretch only the material between hits, so shots and clicks stay sharp and never double. Applies from the next play."),
                        ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Width(72f), GUILayout.Height(RowH));
                    if (keep != ts.liveKeepHits) Set(klip, "live stretch keep hits", () => ts.liveKeepHits = keep);

                    GUILayout.Space(6f);
                    string[] names = { "WSOLA", "Granular" };
                    string[] tips = {
                        "Lines each piece up with the previous one, so tones stay clean and pitch stays exact. The right choice for almost everything. Applies from the next play.",
                        "Overlaps pieces without lining them up: rougher and grainier, which can suit magic, roars and textures as a deliberate character. Applies from the next play."
                    };
                    for (int a = 0; a < 2; a++) {
                        bool sel = (int)ts.liveAlgorithm == a;
                        var corner = a == 0 ? ZUICornerMask.Left : ZUICornerMask.Right;
                        if (ZUI.Toggle(sel, new GUIContent(names[a], tips[a]), ZUI.Style.RichToggle, corner, GUILayout.Width(a == 0 ? 60f : 70f), GUILayout.Height(RowH)) && !sel) {
                            var alg = (LiveStretchAlgorithm)a;
                            Set(klip, "live stretch algorithm", () => ts.liveAlgorithm = alg);
                        }
                    }
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
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
