using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public static class EditorFieldsUtility {

        private static GUIContent tempGUIContent = new GUIContent();
        private static GUIStyle _compactLabelStyle;

        /// <summary>Whether VPC sliders show 0-100 integer values instead of 0.0-1.0 floats.</summary>
        public static bool VpcPercentage => ZoundsProject.Instance != null && ZoundsProject.Instance.browserSettings.vpcPercentage;

        /// <summary>Whether VPC sliders show a compact label instead of editable input fields.</summary>
        public static bool VpcCompactLabel => ZoundsProject.Instance != null && ZoundsProject.Instance.browserSettings.vpcCompactLabel;

        private const float k_CompactLabelLeftPad = 10f;
        private const float k_CompactLabelRightPad = 5f;
        private static float _compactLabelFixedWidth = -1f;

        static GUIStyle CompactLabelStyle {
            get {
                if (_compactLabelStyle == null) {
                    _compactLabelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
                    _compactLabelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
                }
                return _compactLabelStyle;
            }
        }

        /// <summary>Returns a constant width for the compact label, wide enough for the worst-case value.</summary>
        static float CompactLabelFixedWidth {
            get {
                if (_compactLabelFixedWidth < 0f) {
                    // Measure worst-case widths for both modes
                    float pctW = CompactLabelStyle.CalcSize(new GUIContent("99-100")).x;
                    float fracW = CompactLabelStyle.CalcSize(new GUIContent("0.98-0.99")).x + 10f;
                    _compactLabelFixedWidth = k_CompactLabelLeftPad + Mathf.Max(pctW, fracW) + k_CompactLabelRightPad;
                }
                return _compactLabelFixedWidth;
            }
        }

        static string FormatCompactValue(float min, float max, float scale) {
            string sMin, sMax;
            if (VpcPercentage) {
                sMin = Mathf.RoundToInt(min * scale).ToString();
                sMax = Mathf.RoundToInt(max * scale).ToString();
            } else {
                sMin = (min * scale).ToString("G4");
                sMax = (max * scale).ToString("G4");
            }
            return sMin == sMax ? sMin : $"{sMin}-{sMax}";
        }

        static float MeasureCompactLabel(float min, float max, float scale) {
            return CompactLabelFixedWidth;
        }

        static void BeginCompact(float min, float max, float scale) {
            ZUI.SuppressSliderValueFields = true;
            ZUI.CompactLabelWidth = MeasureCompactLabel(min, max, scale);
        }

        static void EndCompact() {
            ZUI.SuppressSliderValueFields = false;
            ZUI.CompactLabelWidth = 0f;
        }

        public static void DrawMinMaxSliderLayout(GUIContent labelContent, float currentMin, System.Action<float> minSetter, float currentMax, System.Action<float> maxSetter, float leftValue, float rightValue) {
            string label = labelContent?.text ?? "";
            float scale = VpcPercentage ? 100f : 1f;
            bool compact = VpcCompactLabel;
            if (compact) BeginCompact(currentMin, currentMax, scale);
            float min = currentMin * scale;
            float max = currentMax * scale;
            ZUI.SliderRange(ref min, ref max, leftValue * scale, rightValue * scale, label, ZUI.SliderStyle.MinMax);
            if (compact) EndCompact();
            min /= scale; max /= scale;
            if (VpcPercentage) { min = Mathf.Round(min * 100f) / 100f; max = Mathf.Round(max * 100f) / 100f; }
            if (!Mathf.Approximately(min, currentMin)) minSetter(min);
            if (!Mathf.Approximately(max, currentMax)) maxSetter(max);
        }

        public static void DrawMinMaxSlider(Rect rect, GUIContent labelContent, float currentMin, System.Action<float> minSetter, float currentMax, System.Action<float> maxSetter, float leftValue, float rightValue) {
            string label = labelContent?.text ?? "";
            float scale = VpcPercentage ? 100f : 1f;
            bool compact = VpcCompactLabel;
            if (compact) BeginCompact(currentMin, currentMax, scale);
            float min = currentMin * scale;
            float max = currentMax * scale;
            ZUI.SliderRange(rect, ref min, ref max, leftValue * scale, rightValue * scale, label, ZUI.SliderStyle.MinMax);
            if (compact) EndCompact();
            min /= scale; max /= scale;
            if (VpcPercentage) { min = Mathf.Round(min * 100f) / 100f; max = Mathf.Round(max * 100f) / 100f; }
            if (!Mathf.Approximately(min, currentMin)) minSetter(min);
            if (!Mathf.Approximately(max, currentMax)) maxSetter(max);

            if (compact) DrawCompactRangeLabel(rect, currentMin, currentMax, scale);
        }

        /// <summary>Draws a compact "min-max" label in the reserved space on the right of the slider rect.</summary>
        public static void DrawCompactRangeLabel(Rect sliderRect, float min, float max, float scale = 1f) {
            if (Event.current.type != EventType.Repaint) return;
            string text = FormatCompactValue(min, max, scale);
            float fixedW = CompactLabelFixedWidth;
            // Left-aligned within the reserved area: 10px gap after track, then text, then 5px right pad
            var labelRect = new Rect(sliderRect.xMax - fixedW + k_CompactLabelLeftPad, sliderRect.y,
                                     fixedW - k_CompactLabelLeftPad - k_CompactLabelRightPad, sliderRect.height);
            GUI.Label(labelRect, text, CompactLabelStyle);
        }

    }

}
