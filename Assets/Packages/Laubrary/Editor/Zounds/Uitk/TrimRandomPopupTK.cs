using System;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// A trim edge's random settings (owner, 2026-10-09), opened by right-clicking an edge of the trim on the waveform
    /// (the Klip editor's and a track's alike), the way a curve point's are: Random on or off, how far each play may move
    /// the edge either way, and the bias (where in that range it tends to land). One row; every change made while it is
    /// open is one Undo step.
    /// </summary>
    internal static class TrimRandomPopupTK {

        const float RowH = 20f;

        /// <summary>The longest range offered: a second either way, or half the file when it is shorter.</summary>
        internal static float MaxRange(float fileLength) => Mathf.Clamp(fileLength * 0.5f, 0.01f, 1f);

        public static void Show(VisualElement anchor, Klip klip, bool end, float fileLength, Action changed) {
            if (anchor == null || klip == null) return;
            bool begun = false;
            void Change(Action a) {
                if (!begun) { begun = true; ZoundsWindow.BeginDragUndo("random trim edge"); }
                a();
                Dsp.ZoundDspPlayback.InvalidateLayout(klip);
                UnityEditor.EditorUtility.SetDirty(ZoundsProject.Instance);
                changed?.Invoke();
            }
            float max = MaxRange(fileLength);
            Z.Popover(anchor, panel => Build(panel), new ZuiPopover.Options {
                preferredSide = ZuiPopover.Side.Below,
                onClosed = () => { if (begun) ZoundsWindow.EndDragUndo(); },
            });

            void Build(VisualElement panel) {
                panel.Clear();
                var row = new VisualElement();
                row.AddToClassList("zs-audition-popup__row");
                row.AddToClassList("zs-trim-random");
                string which = end ? "end" : "start";
                float R() => end ? klip.trimEndRandom : klip.trimStartRandom;
                float B() => end ? klip.trimEndRandomBias : klip.trimStartRandomBias;
                void SetR(float v) { if (end) klip.trimEndRandom = v; else klip.trimStartRandom = v; }
                void SetB(float v) { if (end) klip.trimEndRandomBias = v; else klip.trimStartRandomBias = v; }
                bool on = R() > 0f;
                row.Add(ZS.Toggle("Random", on
                        ? "Make the trim " + which + " fixed again: every play " + (end ? "ends" : "starts") + " exactly where it is set."
                        : "Let every play move the trim " + which + " somewhere within a range either side of where it is set (a different place each play, the same for the whole of one play). The range is drawn around the edge.",
                    on, v => { Change(() => { SetR(v ? Mathf.Min(0.02f, max) : 0f); if (B() <= 0f) SetB(0.5f); }); Build(panel); },
                    "RichToggle", ZUICornerMask.All, 72f, RowH));
                if (on) {
                    row.Add(Gap(8f));
                    ZuiSkinSlider range = null;
                    range = ZS.Slider("Range ±" + Ms(R()), R(), 0.0005f, max,
                        "How far each play may move the trim " + which + ", either way, in seconds of the file. It never leaves the file, and the two edges never cross.",
                        v => { Change(() => SetR(v)); range.text = "Range ±" + Ms(v); },
                        ZuiSkinSlider.LabelMode.LabelOnly, Mathf.Min(0.02f, max), "Default", 160f, RowH - 2f);
                    row.Add(range);
                    row.Add(Gap(6f));
                    row.Add(ZS.Slider("Bias", B(), 0f, 1f,
                        "Where in the range the edge tends to land. 0.5: anywhere. Towards 0: mostly near where it is set. Towards 1: mostly out near the ends of the range.",
                        v => Change(() => SetB(v)), ZuiSkinSlider.LabelMode.LabelAndValue, 0.5f, "Default", 120f, RowH - 2f));
                }
                panel.Add(row);
            }
        }

        static string Ms(float seconds) => seconds < 1f ? (seconds * 1000f).ToString("0.#") + " ms" : seconds.ToString("0.00") + " s";

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.AddToClassList("zs-audition-popup__gap"); return e; }
    }
}
