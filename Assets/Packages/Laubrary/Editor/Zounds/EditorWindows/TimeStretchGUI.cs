using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// The old IMGUI Klip editor's speed strip, kept for comparison (T-0481; the UI Toolkit twin, TimeStretchTK, is the
    /// main one and documents the design). Row one: Live speed and -- whenever the live stretcher runs for this sound --
    /// its window, Keep hits and algorithm, with the length a play will actually have at the right. Row two, only for a
    /// Klip still carrying an old Uniform / Region / Curve stretch: what it does (it is now heard, through the live
    /// stretcher), Convert and Remove. The old settings are no longer edited here.
    /// </summary>
    internal class TimeStretchGUI {

        private const float RowH = 20f;
        private bool dragOpen;

        public bool isDragging => dragOpen;

        private void Set(string undo, System.Action a) => ZoundsWindow.ModifyAndSaveZoundsProject(undo, a);

        private void Drag(string undo, System.Action a) {
            if (!dragOpen) { dragOpen = true; ZoundsWindow.BeginDragUndo(undo); }
            a();
            EditorUtility.SetDirty(ZoundsProject.Instance);
        }

        public void Draw(Klip klip, AudioClip clip, System.Action<int> sourceParamMenu = null) {
            var ts = klip.timeStretch;
            if (ts == null) { ts = klip.timeStretch = new ZoundTimeStretch(); }
            DrawLive(klip, ts, sourceParamMenu);

            string old = KlipChainEnvelopes.DescribeLegacyStretch(klip);
            if (old != null) {
                GUILayout.BeginHorizontal(GUILayout.Height(RowH));
                GUILayout.Label(new GUIContent("Old stretch: " + old, "This sound carries a stretch setting from before the live stretcher. It used to be computed only for the editor and was never heard when the sound played; it is now played through the live stretcher at every play. Convert moves it into the Speed slider (Uniform) or the time curve on the waveform (Region and Curve), where it can be edited."),
                                EditorStyles.label, GUILayout.Width(300f), GUILayout.Height(RowH));
                GUILayout.Space(6f);
                if (ZUI.Button(new GUIContent("Convert", "Makes it permanent in the current controls, sounding the same. The old setting is then switched off."), ZUI.Style.RichButton, ZUICornerMask.Left, GUILayout.Width(70f), GUILayout.Height(RowH - 2f)))
                    Set("convert old stretch", () => KlipChainEnvelopes.ConvertLegacyStretch(klip));
                if (ZUI.Button(new GUIContent("Remove", "Switches the old stretch setting off: the sound plays at its own length again."), ZUI.Style.RichButton, ZUICornerMask.Right, GUILayout.Width(70f), GUILayout.Height(RowH - 2f)))
                    Set("remove old stretch", () => { ts.enabled = false; ZoundDspPlayback.InvalidateLayout(klip); });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            if (Event.current.rawType == EventType.MouseUp && dragOpen) { dragOpen = false; ZoundsWindow.EndDragUndo(); }
        }

        private void DrawLive(Klip klip, ZoundTimeStretch ts, System.Action<int> sourceParamMenu) {
            var evt = Event.current;
            bool runs = Uitk.TimeStretchTK.StretcherRuns(klip, out string why);
            string from = runs ? " (The live stretcher runs for this sound because " + why + ".)" : "";
            GUILayout.BeginHorizontal(GUILayout.Height(RowH));
            {
                bool on = ZUI.Toggle(ts.liveEnabled, new GUIContent("Live speed", ts.liveEnabled
                        ? "Stop using this sound's own Speed and game code's speed. The live stretcher still runs if a time curve, keep length or an old stretch setting needs it."
                        : "Give this sound its own speed, and let game code change it while it plays (a bullet-time slowdown) -- without changing its pitch. Costs about one percent of a CPU core per playing copy."),
                    ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Width(84f), GUILayout.Height(RowH));
                if (on != ts.liveEnabled) Set(on ? "enable live speed" : "disable live speed", () => ts.liveEnabled = on);

                if (ts.liveEnabled) {
                    GUILayout.Space(6f);
                    var spd = ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Speed];
                    float lmin = Mathf.Log(spd.min), lmax = Mathf.Log(spd.max);
                    float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Clamp(ts.liveSpeed, spd.min, spd.max)));
                    var rect = GUILayoutUtility.GetRect(150f, RowH - 2f, GUILayout.Width(150f));
                    if (evt.type == EventType.MouseDown && evt.button == 1 && rect.Contains(evt.mousePosition) && sourceParamMenu != null) {
                        sourceParamMenu(SourceStageParam.Speed); evt.Use();
                    }
                    float nt = ZUI.MicroSlider(rect, t, 0f, 1f, "Speed ×" + ts.liveSpeed.ToString("0.00"), ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, Mathf.InverseLerp(lmin, lmax, 0f));
                    GUI.Label(rect, new GUIContent("", "How fast the sound moves through its source, without changing its pitch: 0.5 is half speed (twice as long), 2 is double. Heard immediately, even on a sound already playing. Game code's speed and the time curve multiply on top. Right-click to drive it with a modifier. Double-click resets to 1."));
                    if (!Mathf.Approximately(nt, t)) {
                        float s = Mathf.Exp(Mathf.Lerp(lmin, lmax, nt));
                        Drag("live speed", () => ts.liveSpeed = s);
                        SapVoiceRegistry.PushAuthoredSpeed(klip, s);
                    }
                }

                if (runs) {
                    GUILayout.Space(6f);
                    var wrect = GUILayoutUtility.GetRect(120f, RowH - 2f, GUILayout.Width(120f));
                    float w = ZUI.MicroSlider(wrect, ts.liveWindowMs, 10f, 100f, "Window " + ts.liveWindowMs.ToString("0") + " ms", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelOnly, 30f);
                    GUI.Label(wrect, new GUIContent("", "Length of the pieces the sound is cut into to stretch it. 20-30 ms suits speech and hits; 40-50 ms suits pads, chords and engines. Applies from the next play." + from));
                    if (!Mathf.Approximately(w, ts.liveWindowMs)) Drag("live stretch window", () => ts.liveWindowMs = Mathf.Round(w));

                    GUILayout.Space(6f);
                    bool keep = ZUI.Toggle(ts.liveKeepHits, new GUIContent("Keep hits", (ts.liveKeepHits
                            ? "Stretch hits too: attacks are then smeared, and at slow speeds can repeat like a machine gun. Applies from the next play."
                            : "Play each hit (attack) once, whole, at normal speed, and stretch only the material between hits, so shots and clicks stay sharp and never double. Applies from the next play.") + from),
                        ZUI.Style.RichToggle, ZUICornerMask.All, GUILayout.Width(72f), GUILayout.Height(RowH));
                    if (keep != ts.liveKeepHits) Set("live stretch keep hits", () => ts.liveKeepHits = keep);

                    GUILayout.Space(6f);
                    string[] names = { "WSOLA", "Granular" };
                    string[] tips = {
                        "Lines each piece up with the previous one, so tones stay clean and pitch stays exact. The right choice for almost everything. Applies from the next play.",
                        "Overlaps pieces without lining them up: rougher and grainier, which can suit magic, roars and textures as a deliberate character. Applies from the next play."
                    };
                    for (int a = 0; a < 2; a++) {
                        bool sel = (int)ts.liveAlgorithm == a;
                        var corner = a == 0 ? ZUICornerMask.Left : ZUICornerMask.Right;
                        if (ZUI.Toggle(sel, new GUIContent(names[a], tips[a] + from), ZUI.Style.RichToggle, corner, GUILayout.Width(a == 0 ? 60f : 70f), GUILayout.Height(RowH)) && !sel) {
                            var alg = (LiveStretchAlgorithm)a;
                            Set("live stretch algorithm", () => ts.liveAlgorithm = alg);
                        }
                    }
                }
                GUILayout.FlexibleSpace();
                if (Uitk.TimeStretchTK.PlayLengthText(klip, out string text, out string tip))
                    GUILayout.Label(new GUIContent(text, tip), EditorStyles.miniLabel, GUILayout.Width(110f));
            }
            GUILayout.EndHorizontal();
        }
    }

}
