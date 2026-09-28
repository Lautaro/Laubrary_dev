using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// The centred label drawn inside a skinned slider track, fitted exactly as the IMGUI ZUI fits it
    /// (ZUISlider.DrawFittedTrackLabel): the full text at the style's size if it fits; otherwise the size is reduced a
    /// point at a time down to 70 % (never below 8); otherwise the same for the fallback text (the value alone);
    /// otherwise the fallback at the smallest size, clipped. Shared by ZuiSkinSlider and ZuiSkinMinMax.
    internal static class ZuiSkinTrackLabel
    {
        public static Label Create()
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("zui-skinslider__label");
            l.style.position = Position.Absolute;
            l.style.left = 0; l.style.right = 0; l.style.top = 0; l.style.bottom = 0;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.marginLeft = 0; l.style.marginRight = 0; l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0; l.style.paddingTop = 0; l.style.paddingBottom = 0;
            l.style.overflow = Overflow.Hidden;
            return l;
        }

        /// Sets the label's text and size for a track <paramref name="width"/> wide. Widths are measured at the label's
        /// resolved size and scaled with the font size, which is how glyph advances scale.
        public static void Fit(Label label, string text, string fallback, float width)
        {
            label.style.fontSize = StyleKeyword.Null;
            label.text = text ?? "";
            if (string.IsNullOrEmpty(text) || width <= 0f) return;
            float baseSize = label.resolvedStyle.fontSize > 0f ? label.resolvedStyle.fontSize : 12f;
            int size0 = Mathf.RoundToInt(baseSize);
            int floor = Mathf.Max(8, Mathf.FloorToInt(size0 * 0.7f));
            float WidthAt(string s, int size) =>
                label.MeasureTextSize(s, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x * size / baseSize;

            if (WidthAt(text, size0) <= width) return;
            bool Shrink(string s)
            {
                for (int size = size0; size >= floor; size--)
                    if (WidthAt(s, size) <= width) { label.text = s; label.style.fontSize = size; return true; }
                return false;
            }
            if (Shrink(text)) return;
            if (!string.IsNullOrEmpty(fallback) && fallback != text && Shrink(fallback)) return;
            label.text = string.IsNullOrEmpty(fallback) ? text : fallback;
            label.style.fontSize = floor;
        }

        /// The IMGUI ZUI's automatic number format for a slider's range, used when the style names none.
        public static string AutoFormat(float min, float max)
        {
            float range = Mathf.Abs(max - min);
            if (range <= 1f) return "F2";
            if (range <= 10f) return "F1";
            return "F0";
        }
    }
}
