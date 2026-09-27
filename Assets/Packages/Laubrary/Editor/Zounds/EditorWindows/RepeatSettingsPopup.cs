using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Repeater settings of one Zequence track (ZoundEntry): how many times or for how long the track
    /// repeats, the spacing, whether spacing counts from each repeat's end, and whether repeats re-roll
    /// the zound's random pitch and volume. Opened from the track's Repeat toggle (right-click).
    /// </summary>
    internal class RepeatSettingsPopup : PopupWindowContent {

        private CompositeZound.ZoundEntry entry;
        private const float RowH = 22f;
        private const float LabelW = 70f;
        private const float ControlW = 180f;
        private bool dragOpen;

        public static void Show(Rect activator, CompositeZound.ZoundEntry entry) {
            PopupWindow.Show(activator, new RepeatSettingsPopup { entry = entry });
        }

        public override Vector2 GetWindowSize() => new Vector2(LabelW + ControlW + 24f, RowH * 5 + 12f);

        private void Set(string undo, System.Action a) => ZoundsWindow.ModifyZoundsProject(undo, a);
        private void Drag(string undo, System.Action a) {
            if (!dragOpen) { dragOpen = true; ZoundsWindow.BeginDragUndo(undo); }
            a();
            EditorUtility.SetDirty(ZoundsProject.Instance);
        }

        public override void OnGUI(Rect rect) {
            using var _sheet = ZUI.UseSheet("Zounds");
            var e = Event.current;
            float y = rect.y + 6f;
            float lx = rect.x + 8f, cx = lx + LabelW;

            // Mode: count or duration
            GUI.Label(new Rect(lx, y, LabelW, RowH), new GUIContent("Repeat", "Repeat a fixed number of times, or keep repeating for a fixed duration."));
            bool count = entry.repeatMode == CompositeZound.RepeatMode.FixedCount;
            if (ZUI.Toggle(new Rect(cx, y + 1f, 90f, RowH - 2f), count, new GUIContent("Times", "A fixed number of plays."), ZUI.Style.RichToggle, null, ZUICornerMask.Left) && !count)
                Set("repeat mode", () => entry.repeatMode = CompositeZound.RepeatMode.FixedCount);
            if (ZUI.Toggle(new Rect(cx + 90f, y + 1f, 90f, RowH - 2f), !count, new GUIContent("For", "Keeps repeating while a repeat still fits inside the duration."), ZUI.Style.RichToggle, null, ZUICornerMask.Right) && count)
                Set("repeat mode", () => entry.repeatMode = CompositeZound.RepeatMode.FixedDuration);
            y += RowH;

            if (count) {
                GUI.Label(new Rect(lx, y, LabelW, RowH), new GUIContent("Count", "Plays in total, including the first."));
                float c = ZUI.MicroSlider(new Rect(cx, y + 1f, ControlW, RowH - 2f), entry.repeatCount, 1f, 32f, "Count", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelAndValue, 3f);
                int ci = Mathf.RoundToInt(c);
                if (ci != entry.repeatCount) Drag("repeat count", () => entry.repeatCount = ci);
            }
            else {
                GUI.Label(new Rect(lx, y, LabelW, RowH), new GUIContent("Duration", "Total time the repeats may cover, in seconds."));
                float d = ZUI.MicroSlider(new Rect(cx, y + 1f, ControlW, RowH - 2f), entry.repeatTotalDuration, 0.1f, 30f, "Seconds", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelAndValue, 2f);
                if (!Mathf.Approximately(d, entry.repeatTotalDuration)) Drag("repeat duration", () => entry.repeatTotalDuration = d);
            }
            y += RowH;

            GUI.Label(new Rect(lx, y, LabelW, RowH), new GUIContent("Interval", entry.repeatSpaceFromEnd ? "Seconds of silence between a repeat's end and the next start." : "Seconds between the starts of consecutive repeats."));
            float iv = ZUI.MicroSlider(new Rect(cx, y + 1f, ControlW, RowH - 2f), entry.repeatInterval, 0f, 5f, "Seconds", ZUI.SliderStyle.Default, false, ZUI.MicroSliderLabelMode.LabelAndValue, 0.25f);
            if (!Mathf.Approximately(iv, entry.repeatInterval)) Drag("repeat interval", () => entry.repeatInterval = iv);
            y += RowH;

            GUI.Label(new Rect(lx, y, LabelW, RowH), new GUIContent("Spacing", "Where the interval is measured from."));
            bool fromEnd = entry.repeatSpaceFromEnd;
            if (ZUI.Toggle(new Rect(cx, y + 1f, 90f, RowH - 2f), !fromEnd, new GUIContent("From start", "Interval counted from each repeat's start (steady rhythm even when repeats differ in length)."), ZUI.Style.RichToggle, null, ZUICornerMask.Left) && fromEnd)
                Set("repeat spacing", () => entry.repeatSpaceFromEnd = false);
            if (ZUI.Toggle(new Rect(cx + 90f, y + 1f, 90f, RowH - 2f), fromEnd, new GUIContent("From end", "Interval counted from the previous repeat's end (needed when re-rolled repeats vary in length)."), ZUI.Style.RichToggle, null, ZUICornerMask.Right) && !fromEnd)
                Set("repeat spacing", () => entry.repeatSpaceFromEnd = true);
            y += RowH;

            GUI.Label(new Rect(lx, y, LabelW, RowH), new GUIContent("Each repeat", "Identical repeats replay the same pitch and volume; re-rolled repeats draw new random values within the zound's ranges."));
            bool re = entry.repeatRetrigger;
            if (ZUI.Toggle(new Rect(cx, y + 1f, 90f, RowH - 2f), !re, new GUIContent("Identical", "Every repeat sounds the same as the first."), ZUI.Style.RichToggle, null, ZUICornerMask.Left) && re)
                Set("repeat retrigger", () => entry.repeatRetrigger = false);
            if (ZUI.Toggle(new Rect(cx + 90f, y + 1f, 90f, RowH - 2f), re, new GUIContent("Re-rolled", "Each repeat re-rolls the zound's random pitch and volume."), ZUI.Style.RichToggle, null, ZUICornerMask.Right) && !re)
                Set("repeat retrigger", () => entry.repeatRetrigger = true);

            if (e.rawType == EventType.MouseUp && dragOpen) { dragOpen = false; ZoundsWindow.EndDragUndo(); }
        }

        public override void OnClose() {
            if (dragOpen) { dragOpen = false; ZoundsWindow.EndDragUndo(); }
        }

        /// <summary>Short label for the track's Repeat toggle: "×3 0.25s" or "2.0s 0.25s".</summary>
        public static string Summary(CompositeZound.ZoundEntry entry) {
            string a = entry.repeatMode == CompositeZound.RepeatMode.FixedCount ? "×" + entry.repeatCount : entry.repeatTotalDuration.ToString("0.0") + "s";
            return a + " " + entry.repeatInterval.ToString("0.00") + "s";
        }
    }

}
