using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>Saved retrigger settings for a Klip or Zequence. This mirrors the audition card's burst timing, but edits the Zound itself.</summary>
    internal static class RetriggerPopupTK {

        const float RowH = 20f;

        public static void Show(VisualElement anchor, Zound zound, System.Action changed) {
            if (anchor == null || zound == null) return;
            Z.Popover(anchor, panel => Build(panel, zound, changed), new ZuiPopover.Options { preferredSide = ZuiPopover.Side.Above });
        }

        static void Build(VisualElement panel, Zound zound, System.Action changed) {
            var row = new VisualElement();
            row.AddToClassList("zs-audition-popup__row");
            Fill(row, zound, changed);
            panel.Add(row);
        }

        /// <summary>
        /// The retrigger options as a row of their own, pinned into a window while Retrigger is on (owner, 2026-10-09): on
        /// the left of the row under the Retrigger toggle, the way the audition card's options sit on the right. Returns the
        /// row and its sync (call it on the window's tick, so an undo or an edit made elsewhere shows).
        /// </summary>
        public static VisualElement Inline(Zound zound, System.Action changed, out System.Action sync) {
            var row = new VisualElement();
            row.AddToClassList("zs-audition-popup__row");
            row.AddToClassList("zs-retrigger-inline");
            sync = Fill(row, zound, changed);
            return row;
        }

        static System.Action Fill(VisualElement row, Zound zound, System.Action changed) {
            int Count() => zound.retriggerCount < 2 ? 4 : Mathf.Clamp(zound.retriggerCount, 2, 32);
            float GapOf() => zound.retriggerCount < 2 ? 0.5f : Mathf.Max(0f, zound.retriggerGap);
            var count = ZS.Slider("Plays", Count(), 2f, 32f,
                "Total plays when this sound is triggered, including the first.",
                v => Change("change retrigger count", () => zound.retriggerCount = Mathf.Clamp(Mathf.RoundToInt(v), 2, 32), changed),
                ZuiSkinSlider.LabelMode.LabelAndValue, 4f, "Default", 96f, RowH - 2f, v => Mathf.RoundToInt(v).ToString());
            row.Add(count);
            row.Add(Gap(6f));
            var gap = ZS.Slider("Gap", GapOf(), 0f, 5f,
                "The time between retriggers. How it is counted is chosen on the right.",
                v => Change("change retrigger gap", () => zound.retriggerGap = Mathf.Max(0f, v), changed),
                ZuiSkinSlider.LabelMode.LabelAndValue, 0.5f, "Default", 120f, RowH - 2f, v => v.ToString("0.00") + " s");
            row.Add(gap);
            row.Add(Gap(6f));
            var mode = Z.Segmented((int)zound.retriggerGapMode, new[] { "Steady", "From start", "From end" },
                "How the gap is counted.\n\nSteady: the first play's actual length plus the gap establishes a fixed rhythm.\n\nFrom start: the gap counts from one play starting to the next; sounds may overlap.\n\nFrom end: the gap is silence after the previous play finishes.",
                i => Change("change retrigger timing", () => zound.retriggerGapMode = (Zound.RetriggerGap)i, changed));
            mode.AddToClassList("zs-audition-popup__mode");
            row.Add(mode);
            return () => {
                count.SetValueWithoutNotify(Count());
                gap.SetValueWithoutNotify(GapOf());
                mode.SetOn(i => i == (int)zound.retriggerGapMode);
            };
        }

        static VisualElement Gap(float width) { var e = new VisualElement(); e.style.width = width; e.AddToClassList("zs-audition-popup__gap"); return e; }

        static void Change(string undo, System.Action edit, System.Action changed) {
            ZoundsWindow.ModifyZoundsProject(undo, edit);
            changed?.Invoke();
        }

        public static string Summary(Zound zound) {
            int count = zound.retriggerCount < 2 ? 4 : Mathf.Clamp(zound.retriggerCount, 2, 32);
            float gap = zound.retriggerCount < 2 ? 0.5f : Mathf.Max(0f, zound.retriggerGap);
            return "×" + count + " " + gap.ToString("0.00") + "s";
        }
    }
}
