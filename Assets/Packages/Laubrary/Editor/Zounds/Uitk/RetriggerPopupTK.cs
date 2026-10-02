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
            int savedCount = zound.retriggerCount < 2 ? 4 : Mathf.Clamp(zound.retriggerCount, 2, 32);
            float savedGap = zound.retriggerCount < 2 ? 0.5f : Mathf.Max(0f, zound.retriggerGap);
            var count = ZS.Slider("Plays", savedCount, 2f, 32f,
                "Total plays when this sound is triggered, including the first.",
                v => Change("change retrigger count", () => zound.retriggerCount = Mathf.Clamp(Mathf.RoundToInt(v), 2, 32), changed),
                ZuiSkinSlider.LabelMode.LabelAndValue, 4f, "Default", 96f, RowH - 2f, v => Mathf.RoundToInt(v).ToString());
            row.Add(count);
            row.Add(Gap(6f));
            var gap = ZS.Slider("Gap", savedGap, 0f, 5f,
                "The time between retriggers. Its reference point is selected at the right.",
                v => Change("change retrigger gap", () => zound.retriggerGap = Mathf.Max(0f, v), changed),
                ZuiSkinSlider.LabelMode.LabelAndValue, 0.5f, "Default", 120f, RowH - 2f, v => v.ToString("0.00") + " s");
            row.Add(gap);
            row.Add(Gap(6f));
            var mode = Z.Segmented((int)zound.retriggerGapMode, new[] { "Steady", "From start", "From end" },
                "How the gap is counted.\n\nSteady: the first play's actual length plus the gap establishes a fixed rhythm.\n\nFrom start: the gap counts from one play starting to the next; sounds may overlap.\n\nFrom end: the gap is silence after the previous play finishes.",
                i => Change("change retrigger timing", () => zound.retriggerGapMode = (Zound.RetriggerGap)i, changed));
            mode.AddToClassList("zs-audition-popup__mode");
            row.Add(mode);
            panel.Add(row);
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
