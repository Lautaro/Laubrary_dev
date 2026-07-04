// ZUISlider.cs
// Fully custom slider drawn from ZUISliderDef.
//
// Single-value horizontal: ZUI.Slider(value, min, max, label, style, ...)
// Single-value vertical:   ZUI.SliderVertical(value, min, max, label, style, ...)
// Range (min/max handles): ZUI.SliderRange(minVal, maxVal, absMin, absMax, label, style, ...)
//
// Layout (horizontal): [label] [trackFill | track] [value field]
//   label     -auto-sized to the text content (font-size aware). 0 = no label.
//                labelPosition = Above/Below draws it on a separate row with configurable alignment.
//   track     -groove, split at thumb into fill (left) + empty (right)
//   thumb     -ZUIButtonDef, full Normal/Hover/Active gradient + border + corners
//   value field-optional editable float on the right
//
// Double-clicking a single slider (not a range slider) resets it to defaultValue
// if a defaultValue was supplied when calling the API (use the float? overloads).

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    /// <summary>When true, range slider value fields are suppressed for the next draw call.
    /// If CompactLabelWidth > 0, that much space is reserved on the right for a compact label instead.</summary>
    public static bool SuppressSliderValueFields { get; set; }

    /// <summary>When SuppressSliderValueFields is true, reserve this much width on the right for a compact label.</summary>
    public static float CompactLabelWidth { get; set; }

    // ===== Slider style name constants ========================================

    public static class SliderStyle
    {
        public const string Default     = "Default";
        public const string BigSlider   = "BigSlider";
        public const string SmallSlider = "SmallSlider";
        public const string MinMax       = "MinMax";
        public const string MinMaxPitch  = "MinMaxPitch";
        public const string Chance       = "Chance";
    }

    // ===== Single-value horizontal slider API =================================

    /// <summary>Draws a horizontal single-value slider. Double-click resets to defaultValue if provided.</summary>
    public static float Slider(float value, float min, float max,
                                string label = "",
                                string style = SliderStyle.Default,
                                float? defaultValue = null,
                                params GUILayoutOption[] options)
    {
        var   def  = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        float h    = SliderTotalHeight(def, vertical: false);
        var   rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, AppendHeight(options, h));
        return DrawManualSlider(rect, value, min, max, label, def, style, defaultValue);
    }

    public static float Slider(Rect rect, float value, float min, float max,
                                string label = "",
                                string style = SliderStyle.Default,
                                float? defaultValue = null,
                                bool suppressValueField = false)
    {
        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        return DrawManualSlider(rect, value, min, max, label, def, style, defaultValue, suppressValueField);
    }

    public static float Slider(float value, float min, float max,
                                string label, ZUISliderDef def,
                                float? defaultValue = null,
                                params GUILayoutOption[] options)
    {
        if (def == null) def = new ZUISliderDef();
        float h    = SliderTotalHeight(def, vertical: false);
        var   rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, AppendHeight(options, h));
        return DrawManualSlider(rect, value, min, max, label, def, def.name, defaultValue);
    }

    public static float Slider(Rect rect, float value, float min, float max,
                                string label, ZUISliderDef def,
                                float? defaultValue = null,
                                bool suppressValueField = false)
    {
        if (def == null) def = new ZUISliderDef();
        return DrawManualSlider(rect, value, min, max, label, def, def.name, defaultValue, suppressValueField);
    }

    /// <summary>
    /// Slider variant that takes an explicit value-field format override. Use when the style's
    /// default format would round to whole numbers but the control wants decimals (e.g., ZUI
    /// Style Editor tuning sliders where 1.5 should be a valid value). Separate method name
    /// rather than an overload so no existing call site can ambiguously bind here.
    /// </summary>
    public static float SliderFormatted(float value, float min, float max,
                                         string valueFormat,
                                         string label = "",
                                         string style = SliderStyle.Default,
                                         float? defaultValue = null,
                                         params GUILayoutOption[] options)
    {
        var   def  = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        float h    = SliderTotalHeight(def, vertical: false);
        var   rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, AppendHeight(options, h));
        return DrawManualSlider(rect, value, min, max, label, def, style, defaultValue, suppressValueField: false, valueFormatOverride: valueFormat);
    }

    // ===== Single-value vertical slider API ===================================

    // ===== Stacked slider (label+value on top, track below) ====================

    /// <summary>
    /// Two-row slider: Row 1 = label (left) + editable value field (right).
    /// Row 2 = track-only slider spanning full width. Returns the new value.
    /// </summary>
    public static float SliderStacked(float value, float min, float max,
                                       string label = "",
                                       string style = SliderStyle.Default,
                                       float? defaultValue = null,
                                       params GUILayoutOption[] options)
    {
        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        float trackH = Mathf.Max(def.trackHeight, def.thumbHeight > 0f ? def.thumbHeight : 6f);
        float labelH = 16f;
        float gap = 2f;
        float valueW = def.valueWidth > 0f ? def.valueWidth : 40f;
        float labelW = 0f;
        if (!string.IsNullOrEmpty(label))
            labelW = EditorStyles.miniLabel.CalcSize(new GUIContent(label)).x + 4f;
        float totalW = labelW + valueW;

        GUILayout.BeginVertical(GUILayout.Width(totalW));

        // Row 1: label + value field (rect-based for precise positioning)
        var row1Rect = GUILayoutUtility.GetRect(totalW, labelH, GUILayout.Width(totalW));
        if (!string.IsNullOrEmpty(label))
            EditorGUI.LabelField(new Rect(row1Rect.x, row1Rect.y, labelW, labelH), label, EditorStyles.miniLabel);
        var fieldRect = new Rect(row1Rect.x + labelW, row1Rect.y, valueW, labelH);
        EditorGUI.BeginChangeCheck();
        bool isInt = (max - min) >= 1f && Mathf.Approximately(min, Mathf.Round(min)) && Mathf.Approximately(max, Mathf.Round(max));
        if (isInt)
        {
            int iv = EditorGUI.IntField(fieldRect, Mathf.RoundToInt(value));
            value = Mathf.Clamp(iv, min, max);
        }
        else
        {
            float fv = EditorGUI.FloatField(fieldRect, value);
            value = Mathf.Clamp(fv, min, max);
        }
        if (EditorGUI.EndChangeCheck()) { /* value already set */ }

        GUILayout.Space(gap);

        // Row 2: track, explicit same width as row 1
        var trackRect = GUILayoutUtility.GetRect(totalW, trackH, GUILayout.Width(totalW));
        value = DrawManualSlider(trackRect, value, min, max, "", def, style, defaultValue, suppressValueField: true);

        GUILayout.EndVertical();


        return value;
    }

    /// <summary>Draws a vertical single-value slider. Double-click resets to defaultValue if provided.</summary>
    public static float SliderVertical(float value, float min, float max,
                                        string label = "",
                                        string style = SliderStyle.Default,
                                        float? defaultValue = null,
                                        params GUILayoutOption[] options)
    {
        var   def  = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        float w    = SliderTotalHeight(def, vertical: true); // height field is used as width for vertical
        var   rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, AppendWidth(options, w));
        return DrawManualSliderVertical(rect, value, min, max, label, def, style, defaultValue);
    }

    public static float SliderVertical(Rect rect, float value, float min, float max,
                                        string label = "",
                                        string style = SliderStyle.Default,
                                        float? defaultValue = null)
    {
        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        return DrawManualSliderVertical(rect, value, min, max, label, def, style, defaultValue);
    }

    public static float SliderVertical(float value, float min, float max,
                                        string label, ZUISliderDef def,
                                        float? defaultValue = null,
                                        params GUILayoutOption[] options)
    {
        if (def == null) def = new ZUISliderDef();
        float w    = SliderTotalHeight(def, vertical: true);
        var   rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, AppendWidth(options, w));
        return DrawManualSliderVertical(rect, value, min, max, label, def, def.name, defaultValue);
    }

    public static float SliderVertical(Rect rect, float value, float min, float max,
                                        string label, ZUISliderDef def,
                                        float? defaultValue = null)
    {
        if (def == null) def = new ZUISliderDef();
        return DrawManualSliderVertical(rect, value, min, max, label, def, def.name, defaultValue);
    }

    // ===== Range slider API ==================================================
    // Two thumbs: minVal (left) and maxVal (right). Both are draggable.

    public static void SliderRange(ref float minVal, ref float maxVal,
                                    float absMin, float absMax,
                                    string label = "",
                                    string style = SliderStyle.Default,
                                    params GUILayoutOption[] options)
    {
        var   def  = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        float h    = SliderTotalHeight(def, vertical: false);
        var   rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, AppendHeight(options, h));
        DrawManualRangeSlider(rect, ref minVal, ref maxVal, absMin, absMax, label, def, style);
    }

    public static void SliderRange(Rect rect, ref float minVal, ref float maxVal,
                                    float absMin, float absMax,
                                    string label = "",
                                    string style = SliderStyle.Default)
    {
        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        DrawManualRangeSlider(rect, ref minVal, ref maxVal, absMin, absMax, label, def, style);
    }

    public static void SliderRange(ref float minVal, ref float maxVal,
                                    float absMin, float absMax,
                                    string label, ZUISliderDef def,
                                    params GUILayoutOption[] options)
    {
        if (def == null) def = new ZUISliderDef();
        float h    = SliderTotalHeight(def, vertical: false);
        var   rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, AppendHeight(options, h));
        DrawManualRangeSlider(rect, ref minVal, ref maxVal, absMin, absMax, label, def, def.name);
    }

    // =========================================================================
    // Single-value horizontal core draw
    // =========================================================================

    static float DrawManualSlider(Rect totalRect, float value, float min, float max,
                                   string label, ZUISliderDef def, string styleName = "",
                                   float? defaultValue = null, bool suppressValueField = false,
                                   string valueFormatOverride = null)
    {
        Debug.Assert(min <= max, $"[ZUI.Slider] min ({min}) must be <= max ({max})");
        value = Mathf.Clamp(value, min, max);

        // Carve label + value rects
        Rect labelRect, sliderRect, valueRect;
        CarveSliderLayout(totalRect, label, def, out labelRect, out sliderRect, out valueRect, suppressValueField);

        // Geometry
        float thumbW    = Mathf.Max(4f, def.thumbWidth);
        float thumbH    = def.thumbHeight > 0f ? def.thumbHeight : sliderRect.height;
        float trackH    = Mathf.Min(def.trackHeight, sliderRect.height);
        float travelMin = sliderRect.x + thumbW * 0.5f;
        float travelMax = sliderRect.xMax - thumbW * 0.5f;

        float t       = InverseLerpSafe(min, max, value);
        float thumbCx = travelMin + t * Mathf.Max(1f, travelMax - travelMin);

        // Input
        int  id     = GUIUtility.GetControlID(FocusType.Passive, sliderRect);
        var  ev     = Event.current;
        bool isDrag = GUIUtility.hotControl == id;

        // Double-click to reset (must come before the main switch to avoid being consumed)
        if (defaultValue.HasValue
            && ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2
            && sliderRect.Contains(ev.mousePosition))
        {
            value = Mathf.Clamp(defaultValue.Value, min, max);
            GUIUtility.hotControl = 0;
            GUI.changed = true;
            ev.Use();
        }

        switch (ev.type)
        {
            case EventType.MouseDown:
                if (sliderRect.Contains(ev.mousePosition) && ev.button == 0)
                {
                    GUIUtility.hotControl = id;
                    value = SamplePosition(ev.mousePosition.x, travelMin, travelMax, min, max);
                    GUI.changed = true;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (isDrag)
                {
                    value = SamplePosition(ev.mousePosition.x, travelMin, travelMax, min, max);
                    GUI.changed = true;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (isDrag) { GUIUtility.hotControl = 0; ev.Use(); }
                break;
        }

        // Recompute after input
        t       = InverseLerpSafe(min, max, value);
        thumbCx = travelMin + t * Mathf.Max(1f, travelMax - travelMin);

        if (ev.type == EventType.Repaint)
        {
            float trackY    = sliderRect.y + (sliderRect.height - trackH) * 0.5f;
            var   fillRect  = new Rect(sliderRect.x, trackY, thumbCx - sliderRect.x, trackH);
            var   emptyRect = new Rect(thumbCx, trackY, sliderRect.xMax - thumbCx, trackH);
            float thumbY    = sliderRect.y + (sliderRect.height - thumbH) * 0.5f;
            var   thumbRect = new Rect(thumbCx - thumbW * 0.5f, thumbY, thumbW, thumbH);

            if (fillRect.width  > 0f) def.trackFill?.DrawBackground(fillRect);
            if (emptyRect.width > 0f) def.track?.DrawBackground(emptyRect);

            bool isHover  = thumbRect.Contains(ev.mousePosition);
            var  state    = isDrag ? ZUIButtonDrawState.Active : isHover ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal;
            int  cr       = def.thumb?.GetResolvedCornerRadius() ?? 0;
            def.thumb?.DrawVisual(thumbRect, state, cr);

            DrawSliderLabel(labelRect, label, def);
        }

        if (StyleDebugMode && IsDebugHit(sliderRect))
            CollectSliderDebugInfo(def, styleName, sliderRect, isRange: false);

        DrawFlashOverlayIfNeeded(sliderRect, styleName, 0, FlashDefType.Slider);

        // Value field
        if (!suppressValueField && def.showValueField && def.valueWidth > 0f)
        {
            EditorGUI.BeginChangeCheck();
            string fmt = !string.IsNullOrEmpty(valueFormatOverride)
                ? valueFormatOverride
                : !string.IsNullOrEmpty(def.valueFormat)
                    ? def.valueFormat
                    : AutoFormat(min, max);
            float newVal = EditorGUI.FloatField(valueRect,
                float.Parse(value.ToString(fmt), System.Globalization.CultureInfo.InvariantCulture),
                def.GetValueStyle(ActiveSheet));
            if (EditorGUI.EndChangeCheck())
                value = Mathf.Clamp(newVal, min, max);
        }

        return value;
    }

    // =========================================================================
    // Single-value vertical core draw
    // =========================================================================

    static float DrawManualSliderVertical(Rect totalRect, float value, float min, float max,
                                           string label, ZUISliderDef def, string styleName = "",
                                           float? defaultValue = null)
    {
        value = Mathf.Clamp(value, min, max);

        // Carve label + slider rects for vertical layout
        Rect labelRect, sliderRect;
        CarveVerticalSliderLayout(totalRect, label, def, out labelRect, out sliderRect);

        // Geometry-for vertical: thumbWidth = horizontal size, thumbHeight = knob travel height
        float thumbW    = Mathf.Max(4f, def.thumbWidth);   // knob width (across track)
        float thumbH    = def.thumbHeight > 0f ? def.thumbHeight : thumbW; // knob height along travel
        float trackW    = Mathf.Min(def.trackHeight, sliderRect.width);    // track groove width
        float travelMin = sliderRect.yMax - thumbH * 0.5f; // top of travel = max value
        float travelMax = sliderRect.y    + thumbH * 0.5f; // bottom of travel = min value
        // Note: y increases downward, so max value is at top (small y)
        float travelTop    = sliderRect.y    + thumbH * 0.5f;  // pixel y for max
        float travelBottom = sliderRect.yMax - thumbH * 0.5f;  // pixel y for min

        // t=0 → bottom (min), t=1 → top (max)
        float t       = InverseLerpSafe(min, max, value);
        float thumbCy = travelBottom - t * Mathf.Max(1f, travelBottom - travelTop);

        // Input
        int  id     = GUIUtility.GetControlID(FocusType.Passive, sliderRect);
        var  ev     = Event.current;
        bool isDrag = GUIUtility.hotControl == id;

        // Double-click to reset
        if (defaultValue.HasValue
            && ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2
            && sliderRect.Contains(ev.mousePosition))
        {
            value = Mathf.Clamp(defaultValue.Value, min, max);
            GUIUtility.hotControl = 0;
            GUI.changed = true;
            ev.Use();
        }

        switch (ev.type)
        {
            case EventType.MouseDown:
                if (sliderRect.Contains(ev.mousePosition) && ev.button == 0)
                {
                    GUIUtility.hotControl = id;
                    value = SamplePositionVertical(ev.mousePosition.y, travelTop, travelBottom, min, max);
                    GUI.changed = true;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (isDrag)
                {
                    value = SamplePositionVertical(ev.mousePosition.y, travelTop, travelBottom, min, max);
                    GUI.changed = true;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (isDrag) { GUIUtility.hotControl = 0; ev.Use(); }
                break;
        }

        // Recompute after input
        t       = InverseLerpSafe(min, max, value);
        thumbCy = travelBottom - t * Mathf.Max(1f, travelBottom - travelTop);

        if (ev.type == EventType.Repaint)
        {
            float trackX = sliderRect.x + (sliderRect.width - trackW) * 0.5f;

            // trackFill = the filled portion from bottom up to the thumb (represents value level)
            // track     = the empty groove above the thumb
            var fillRect  = new Rect(trackX, thumbCy, trackW, sliderRect.yMax - thumbCy);
            var emptyRect = new Rect(trackX, sliderRect.y, trackW, thumbCy - sliderRect.y);

            float thumbX    = sliderRect.x + (sliderRect.width - thumbW) * 0.5f;
            var   thumbRect = new Rect(thumbX, thumbCy - thumbH * 0.5f, thumbW, thumbH);

            if (fillRect.height  > 0f) def.trackFill?.DrawBackground(fillRect);
            if (emptyRect.height > 0f) def.track?.DrawBackground(emptyRect);

            bool isHover = thumbRect.Contains(ev.mousePosition);
            var  state   = isDrag ? ZUIButtonDrawState.Active : isHover ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal;
            int  cr      = def.thumb?.GetResolvedCornerRadius() ?? 0;
            def.thumb?.DrawVisual(thumbRect, state, cr);

            DrawSliderLabelVertical(labelRect, label, def);
        }

        if (StyleDebugMode && IsDebugHit(sliderRect))
            CollectSliderDebugInfo(def, styleName, sliderRect, isRange: false);

        DrawFlashOverlayIfNeeded(sliderRect, styleName, 0, FlashDefType.Slider);

        return value;
    }

    // =========================================================================
    // Range slider core draw
    // =========================================================================

    static readonly int s_RangeMinHash  = "ZUIRangeMin".GetHashCode();
    static readonly int s_RangeMaxHash  = "ZUIRangeMax".GetHashCode();
    static readonly int s_RangeFillHash = "ZUIRangeFill".GetHashCode();

    // Drag-pan state for the fill region
    static float _rangeDragAnchorMin;
    static float _rangeDragAnchorMax;
    static float _rangeDragAnchorX;

    static void DrawManualRangeSlider(Rect totalRect, ref float minVal, ref float maxVal,
                                       float absMin, float absMax,
                                       string label, ZUISliderDef def, string styleName = "")
    {
        minVal = Mathf.Clamp(minVal, absMin, absMax);
        maxVal = Mathf.Clamp(maxVal, minVal, absMax);

        Rect labelRect, sliderRect, valueRect;
        CarveSliderLayout(totalRect, label, def, out labelRect, out sliderRect, out valueRect, rangeMode: true);

        float thumbW    = Mathf.Max(4f, def.thumbWidth);
        float thumbH    = def.thumbHeight > 0f ? def.thumbHeight : sliderRect.height;
        float trackH    = Mathf.Min(def.trackHeight, sliderRect.height);
        float travelMin = sliderRect.x + thumbW * 0.5f;
        float travelMax = sliderRect.xMax - thumbW * 0.5f;
        float travelLen = Mathf.Max(1f, travelMax - travelMin);

        float tMin = InverseLerpSafe(absMin, absMax, minVal);
        float tMax = InverseLerpSafe(absMin, absMax, maxVal);
        float cxMin = travelMin + tMin * travelLen;
        float cxMax = travelMin + tMax * travelLen;

        int  idMin     = GUIUtility.GetControlID(s_RangeMinHash,  FocusType.Passive, sliderRect);
        int  idMax     = GUIUtility.GetControlID(s_RangeMaxHash,  FocusType.Passive, sliderRect);
        int  idFill    = GUIUtility.GetControlID(s_RangeFillHash, FocusType.Passive, sliderRect);
        var  ev        = Event.current;
        bool dragMin   = GUIUtility.hotControl == idMin;
        bool dragMax   = GUIUtility.hotControl == idMax;
        bool dragFill  = GUIUtility.hotControl == idFill;

        float trackY    = sliderRect.y + (sliderRect.height - trackH) * 0.5f;

        // Thumbs are "fused" when their pixel positions overlap (gap < thumbW)
        bool fused = (cxMax - cxMin) < thumbW;

        // When fused, render them side-by-side: min at its natural position, max immediately right.
        float drawCxMin = cxMin;
        float drawCxMax = fused ? cxMin + thumbW : cxMax;

        var thumbRectMin = ThumbRect(drawCxMin, sliderRect, thumbW, thumbH);
        var thumbRectMax = ThumbRect(drawCxMax, sliderRect, thumbW, thumbH);

        // Fill hit rect spans full control height for easy grabbing
        var fillHitRect  = new Rect(cxMin, sliderRect.y, cxMax - cxMin, sliderRect.height);
        var fillDrawRect = new Rect(cxMin, trackY, cxMax - cxMin, trackH);

        // Collapsed detection uses formatted-string comparison so visual collapse matches the
        // displayed label (and matches MicroMinMax's rule for behavior parity across flavors).
        string fmtRange = !string.IsNullOrEmpty(def.valueFormat) ? def.valueFormat : AutoFormat(absMin, absMax);
        bool collapsed = minVal.ToString(fmtRange) == maxVal.ToString(fmtRange);

        // Bipolar center — position of the neutral marker in track-pixel coords.
        float bipCenterVal = def.bipolar ? ResolveBipolarCenter(def, absMin, absMax) : 0f;
        float bipCenterX   = def.bipolar ? travelMin + InverseLerpSafe(absMin, absMax, bipCenterVal) * travelLen : 0f;
        const float bipCenterGrab = 6f;

        // ── Bipolar center-thumb double-click: toggle between collapsed-at-center and
        //    symmetric spread around center. Checked before the shared helper so it wins.
        if (def.bipolar && ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2
            && Mathf.Abs(ev.mousePosition.x - bipCenterX) <= bipCenterGrab
            && sliderRect.Contains(ev.mousePosition))
        {
            bool alreadyAtCenter = collapsed && minVal.ToString(fmtRange) == bipCenterVal.ToString(fmtRange);
            if (alreadyAtCenter)
            {
                float range = absMax - absMin;
                float half  = range * k_RangeExpandFraction * 0.5f;
                minVal = Mathf.Max(absMin, bipCenterVal - half);
                maxVal = Mathf.Min(absMax, bipCenterVal + half);
            }
            else
            {
                minVal = maxVal = bipCenterVal;
            }
            GUIUtility.hotControl = 0;
            GUI.changed = true;
            ev.Use();
        }
        // ── Double-click toggle: collapsed → expand, otherwise → collapse (shared across all
        //    MinMax slider flavors). Must run before the switch so ev.Use() in the single-click
        //    branch doesn't consume it first.
        else if (TryHandleRangeDoubleClick(ev, sliderRect, (drawCxMin + drawCxMax) * 0.5f, ref minVal, ref maxVal, absMin, absMax, collapsed))
        {
            // Event consumed. Skip the rest of input handling for this frame.
        }
        else switch (ev.type)
        {
            case EventType.MouseDown when ev.button == 0:
            {
                if (!sliderRect.Contains(ev.mousePosition)) break;

                float mx = ev.mousePosition.x;

                if (fused)
                {
                    // Fused: whole block pans together.
                    // Clicking outside the block on the empty track separates by moving the
                    // nearer thumb-handled by the outer-track branch below.
                    var fusedHitRect = new Rect(drawCxMin - thumbW * 0.5f, sliderRect.y,
                                                thumbW * 2f, sliderRect.height);
                    if (fusedHitRect.Contains(ev.mousePosition))
                    {
                        // Click is on the fused block-pan drag
                        GUIUtility.hotControl = idFill;
                        _rangeDragAnchorMin   = minVal;
                        _rangeDragAnchorMax   = maxVal;
                        _rangeDragAnchorX     = mx;
                    }
                    else
                    {
                        // Click is outside fused block on empty track-separate by moving nearer thumb
                        bool goLeft = mx < cxMin;
                        GUIUtility.hotControl = goLeft ? idMin : idMax;
                        ApplyRangeDrag(mx, travelMin, travelMax, absMin, absMax,
                                       ref minVal, ref maxVal, dragMin: goLeft);
                    }
                }
                else
                {
                    bool hitMin  = thumbRectMin.Contains(ev.mousePosition);
                    bool hitMax  = thumbRectMax.Contains(ev.mousePosition);
                    bool hitFill = !hitMin && !hitMax && fillHitRect.width > 0f && fillHitRect.Contains(ev.mousePosition);

                    if (hitFill)
                    {
                        GUIUtility.hotControl = idFill;
                        _rangeDragAnchorMin   = minVal;
                        _rangeDragAnchorMax   = maxVal;
                        _rangeDragAnchorX     = mx;
                    }
                    else
                    {
                        if (!hitMin && !hitMax)
                            hitMin = Mathf.Abs(mx - cxMin) <= Mathf.Abs(mx - cxMax);
                        GUIUtility.hotControl = hitMin ? idMin : idMax;
                        ApplyRangeDrag(mx, travelMin, travelMax, absMin, absMax,
                                       ref minVal, ref maxVal, dragMin: GUIUtility.hotControl == idMin);
                    }
                }
                GUI.changed = true;
                ev.Use();
                break;
            }
            case EventType.MouseDrag when dragFill:
            {
                float dx   = ev.mousePosition.x - _rangeDragAnchorX;
                float dVal = dx / travelLen * (absMax - absMin);
                float span = _rangeDragAnchorMax - _rangeDragAnchorMin;
                float nMin = Mathf.Clamp(_rangeDragAnchorMin + dVal, absMin, absMax - span);
                minVal = nMin;
                maxVal = nMin + span;
                GUI.changed = true;
                ev.Use();
                break;
            }
            case EventType.MouseDrag when dragMin || dragMax:
                ApplyRangeDrag(ev.mousePosition.x, travelMin, travelMax, absMin, absMax,
                               ref minVal, ref maxVal, dragMin: dragMin);
                GUI.changed = true;
                ev.Use();
                break;
            case EventType.MouseUp when dragMin || dragMax || dragFill:
                GUIUtility.hotControl = 0;
                ev.Use();
                break;
        }

        // Recompute after input
        minVal = Mathf.Clamp(minVal, absMin, absMax);
        maxVal = Mathf.Clamp(maxVal, minVal, absMax);
        tMin  = InverseLerpSafe(absMin, absMax, minVal);
        tMax  = InverseLerpSafe(absMin, absMax, maxVal);
        cxMin = travelMin + tMin * travelLen;
        cxMax = travelMin + tMax * travelLen;
        trackY = sliderRect.y + (sliderRect.height - trackH) * 0.5f;

        fused        = (cxMax - cxMin) < thumbW;
        drawCxMin    = cxMin;
        drawCxMax    = fused ? cxMin + thumbW : cxMax;
        thumbRectMin = ThumbRect(drawCxMin, sliderRect, thumbW, thumbH);
        thumbRectMax = ThumbRect(drawCxMax, sliderRect, thumbW, thumbH);
        fillHitRect  = new Rect(cxMin, sliderRect.y, cxMax - cxMin, sliderRect.height);
        fillDrawRect = new Rect(cxMin, trackY, cxMax - cxMin, trackH);

        if (ev.type == EventType.Repaint)
        {
            if (def.bipolar && collapsed)
            {
                // Bipolar + collapsed: fill spans from center to current value, both sides empty.
                float fillL = Mathf.Min(bipCenterX, cxMin);
                float fillR = Mathf.Max(bipCenterX, cxMin);
                var emptyL   = new Rect(sliderRect.x, trackY, fillL - sliderRect.x, trackH);
                var fillRect = new Rect(fillL, trackY, fillR - fillL, trackH);
                var emptyR   = new Rect(fillR, trackY, sliderRect.xMax - fillR, trackH);

                if (emptyL.width   > 0f) def.track?.DrawBackground(emptyL);
                if (fillRect.width > 0f) def.trackFill?.DrawBackground(fillRect);
                if (emptyR.width   > 0f) def.track?.DrawBackground(emptyR);
            }
            else
            {
                var emptyLeft  = new Rect(sliderRect.x, trackY, cxMin - sliderRect.x, trackH);
                // Empty right starts after the drawn max thumb position
                var emptyRight = new Rect(drawCxMax, trackY, sliderRect.xMax - drawCxMax, trackH);

                if (emptyLeft.width  > 0f) def.track?.DrawBackground(emptyLeft);

                // Fill between logical thumb positions (zero-width when fused)
                if (fillDrawRect.width > 0f)
                {
                    def.trackFill?.DrawBackground(fillDrawRect);
                    bool fillHovered = !fused && (dragFill ||
                        (!dragMin && !dragMax && fillHitRect.Contains(ev.mousePosition)));
                    if (fillHovered)
                        EditorGUI.DrawRect(fillDrawRect, new Color(1f, 1f, 1f, 0.12f));
                }

                if (emptyRight.width > 0f) def.track?.DrawBackground(emptyRight);
            }

            if (fused)
            {
                // Both thumbs move as one: show fill-drag hover state on both
                var fusedHoverRect = new Rect(drawCxMin - thumbW * 0.5f, sliderRect.y,
                                              thumbW * 2f, sliderRect.height);
                bool fusedHovered = dragFill || fusedHoverRect.Contains(ev.mousePosition);
                var  fusedState   = fusedHovered ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal;
                if (dragFill) fusedState = ZUIButtonDrawState.Active;
                int crMin = def.thumb?.GetResolvedCornerRadius() ?? 0;
                int crMax = (def.thumbMax ?? def.thumb)?.GetResolvedCornerRadius() ?? 0;
                def.thumb?.DrawVisual(thumbRectMin, fusedState, crMin);
                (def.thumbMax ?? def.thumb)?.DrawVisual(thumbRectMax, fusedState, crMax);
            }
            else
            {
                DrawRangeThumb(thumbRectMin, def.thumb,                 idMin, ev);
                DrawRangeThumb(thumbRectMax, def.thumbMax ?? def.thumb, idMax, ev);
            }

            // Bipolar center marker — non-draggable thumb at the neutral position. Hover state
            // hints that double-clicking it does something. Drawn after min/max so it sits on
            // top when a range straddles center.
            if (def.bipolar)
            {
                var cDef = def.thumbCenter ?? def.thumb;
                var cRect = ThumbRect(bipCenterX, sliderRect, thumbW, thumbH);
                bool hoverC = Mathf.Abs(ev.mousePosition.x - bipCenterX) <= bipCenterGrab;
                var  cState = hoverC ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal;
                int  cCR    = cDef?.GetResolvedCornerRadius() ?? 0;
                cDef?.DrawVisual(cRect, cState, cCR);
            }

            DrawSliderLabel(labelRect, label, def);
        }

        if (StyleDebugMode && IsDebugHit(sliderRect))
            CollectSliderDebugInfo(def, styleName, sliderRect, isRange: true);

        DrawFlashOverlayIfNeeded(sliderRect, styleName, 0, FlashDefType.Slider);

        // Value fields for range: show min/max as two fields normally, or one wide field spanning
        // the full reserved area when the range is collapsed (both values equal by format). The
        // total width stays the same so surrounding layout doesn't jump when toggling collapse.
        if (!SuppressSliderValueFields && def.showValueField && def.valueWidth > 0f && valueRect.width > 0f)
        {
            var   vs  = def.GetValueStyle(ActiveSheet);
            string fmt  = !string.IsNullOrEmpty(def.valueFormat) ? def.valueFormat : AutoFormat(absMin, absMax);
            if (collapsed)
            {
                EditorGUI.BeginChangeCheck();
                float newVal = EditorGUI.FloatField(valueRect,
                    float.Parse(minVal.ToString(fmt), System.Globalization.CultureInfo.InvariantCulture), vs);
                if (EditorGUI.EndChangeCheck())
                {
                    float clamped = Mathf.Clamp(newVal, absMin, absMax);
                    minVal = clamped;
                    maxVal = clamped;
                }
            }
            else
            {
                float halfW = (valueRect.width - 2f) * 0.5f;
                var   minFR = new Rect(valueRect.x,             valueRect.y, halfW, valueRect.height);
                var   maxFR = new Rect(valueRect.x + halfW + 2f, valueRect.y, halfW, valueRect.height);
                EditorGUI.BeginChangeCheck();
                float newMin = EditorGUI.FloatField(minFR,
                    float.Parse(minVal.ToString(fmt), System.Globalization.CultureInfo.InvariantCulture), vs);
                if (EditorGUI.EndChangeCheck()) minVal = Mathf.Clamp(newMin, absMin, absMax);
                EditorGUI.BeginChangeCheck();
                float newMax = EditorGUI.FloatField(maxFR,
                    float.Parse(maxVal.ToString(fmt), System.Globalization.CultureInfo.InvariantCulture), vs);
                if (EditorGUI.EndChangeCheck()) maxVal = Mathf.Clamp(newMax, minVal, absMax);
            }
        }
    }

    // =========================================================================
    // Shared helpers
    // =========================================================================

    // Measures auto label width from the label string using the def's label style + font size.
    static float MeasureLabelWidth(string label, ZUISliderDef def)
    {
        if (string.IsNullOrEmpty(label)) return 0f;
        var ls = def.GetLabelStyle(ActiveSheet);
        return ls.CalcSize(new GUIContent(label)).x + 4f; // +4 right padding
    }

    static float MeasureLabelHeight(string label, ZUISliderDef def)
    {
        if (string.IsNullOrEmpty(label)) return 0f;
        var ls = def.GetLabelStyle(ActiveSheet);
        return ls.CalcSize(new GUIContent(label)).y + 2f; // +2 bottom padding
    }

    // Horizontal slider layout: supports Inline (label left), Above, Below.
    static void CarveSliderLayout(Rect totalRect, string label, ZUISliderDef def,
                                   out Rect labelRect, out Rect sliderRect, out Rect valueRect,
                                   bool suppressValueField = false,
                                   bool rangeMode = false)
    {
        labelRect  = Rect.zero;
        valueRect  = Rect.zero;
        sliderRect = totalRect;

        bool hasLabel = !string.IsNullOrEmpty(label);

        if (hasLabel && def.labelPosition != ZUILabelPosition.Inline)
        {
            float lh = MeasureLabelHeight(label, def);

            if (def.labelPosition == ZUILabelPosition.Above)
            {
                labelRect  = new Rect(totalRect.x, totalRect.y, totalRect.width, lh);
                sliderRect = new Rect(totalRect.x, totalRect.y + lh,
                                      totalRect.width, totalRect.height - lh);
            }
            else // Below
            {
                sliderRect = new Rect(totalRect.x, totalRect.y,
                                      totalRect.width, totalRect.height - lh);
                labelRect  = new Rect(totalRect.x, sliderRect.yMax, totalRect.width, lh);
            }
        }
        else if (hasLabel)
        {
            // Inline: label to the left
            float lw = def.labelWidth > 0f
                ? Mathf.Max(def.labelWidth, MeasureLabelWidth(label, def))
                : MeasureLabelWidth(label, def);
            labelRect  = new Rect(totalRect.x, totalRect.y, lw, totalRect.height);
            sliderRect = new Rect(totalRect.x + lw, totalRect.y,
                                  totalRect.width - lw, totalRect.height);
        }

        if (!suppressValueField && SuppressSliderValueFields && CompactLabelWidth > 0f && def.labelPosition == ZUILabelPosition.Inline)
        {
            // Reserve space for the compact label but don't create a value field
            float lw = CompactLabelWidth;
            valueRect  = new Rect(sliderRect.xMax - lw, sliderRect.y, lw, sliderRect.height);
            sliderRect = new Rect(sliderRect.x, sliderRect.y,
                                  sliderRect.width - lw, sliderRect.height);
        }
        else if (!suppressValueField && !SuppressSliderValueFields && def.showValueField && def.valueWidth > 0f && def.labelPosition == ZUILabelPosition.Inline)
        {
            // Range-mode splits the value area into two half-width fields; we shrink the total
            // area to 60% of valueWidth so each half stays compact (matches MicroMinMax's
            // k_RangeFieldWidthFactor). Each range field = def.valueWidth * 0.3, two fields total.
            float vw  = rangeMode ? def.valueWidth * 0.6f : def.valueWidth;
            valueRect  = new Rect(sliderRect.xMax - vw, sliderRect.y, vw, sliderRect.height);
            sliderRect = new Rect(sliderRect.x, sliderRect.y,
                                  sliderRect.width - vw, sliderRect.height);
        }
    }

    // Vertical slider layout: label carved from top or bottom of the rect.
    static void CarveVerticalSliderLayout(Rect totalRect, string label, ZUISliderDef def,
                                           out Rect labelRect, out Rect sliderRect)
    {
        labelRect  = Rect.zero;
        sliderRect = totalRect;

        if (string.IsNullOrEmpty(label)) return;

        float lh = MeasureLabelHeight(label, def);

        // For vertical sliders: Above = label at top of rect (above track), Below = label at bottom.
        // Default (Inline) falls back to Below so the track gets maximum height.
        bool above = def.labelPosition == ZUILabelPosition.Above;

        if (above)
        {
            labelRect  = new Rect(totalRect.x, totalRect.y, totalRect.width, lh);
            sliderRect = new Rect(totalRect.x, totalRect.y + lh,
                                  totalRect.width, totalRect.height - lh);
        }
        else
        {
            sliderRect = new Rect(totalRect.x, totalRect.y,
                                  totalRect.width, totalRect.height - lh);
            labelRect  = new Rect(totalRect.x, sliderRect.yMax, totalRect.width, lh);
        }
    }

    // Draws label text with shadow support (EditorGUI.LabelField ignores GUIStyle shadow).
    static void DrawSliderLabel(Rect labelRect, string label, ZUISliderDef def)
    {
        if (labelRect.width <= 0f || string.IsNullOrEmpty(label)) return;
        var ls = def.GetLabelStyle(ActiveSheet);

        if (def.labelPosition == ZUILabelPosition.Inline)
        {
            ls.alignment = TextAnchor.MiddleLeft;
        }
        else
        {
            ls.alignment = def.labelAlignment switch
            {
                ZUILabelAlignment.Center => TextAnchor.UpperCenter,
                ZUILabelAlignment.Right  => TextAnchor.UpperRight,
                _                        => TextAnchor.UpperLeft,
            };
        }

        DrawLabelWithShadow(labelRect, label, def, ls);
    }

    // Draws label text for vertical sliders.
    static void DrawSliderLabelVertical(Rect labelRect, string label, ZUISliderDef def)
    {
        if (labelRect.width <= 0f || string.IsNullOrEmpty(label)) return;
        var ls = def.GetLabelStyle(ActiveSheet);
        ls.alignment = def.labelAlignment switch
        {
            ZUILabelAlignment.Center => TextAnchor.UpperCenter,
            ZUILabelAlignment.Right  => TextAnchor.UpperRight,
            _                        => TextAnchor.UpperLeft,
        };
        DrawLabelWithShadow(labelRect, label, def, ls);
    }

    static void DrawLabelWithShadow(Rect labelRect, string label, ZUISliderDef def, GUIStyle ls)
    {
        // Route through the central DrawLabel for shadow, outline, and gradient support.
        ZUI.DrawLabel(labelRect, new GUIContent(label), ls, def.labelText);
    }

    static Rect ThumbRect(float cx, Rect sliderRect, float thumbW, float thumbH)
    {
        float thumbY = sliderRect.y + (sliderRect.height - thumbH) * 0.5f;
        return new Rect(cx - thumbW * 0.5f, thumbY, thumbW, thumbH);
    }

    static void DrawRangeThumb(Rect thumbRect, ZUIButtonDef thumbDef, int id, Event ev)
    {
        bool isHover = thumbRect.Contains(ev.mousePosition);
        bool isDrag  = GUIUtility.hotControl == id;
        var  state   = isDrag ? ZUIButtonDrawState.Active : isHover ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal;
        int  cr      = thumbDef?.GetResolvedCornerRadius() ?? 0;
        thumbDef?.DrawVisual(thumbRect, state, cr);
    }

    static void ApplyRangeDrag(float mouseX, float travelMin, float travelMax,
                                float absMin, float absMax,
                                ref float minVal, ref float maxVal, bool dragMin)
    {
        float v = SamplePosition(mouseX, travelMin, travelMax, absMin, absMax);
        if (dragMin) minVal = Mathf.Clamp(v, absMin, maxVal);
        else         maxVal = Mathf.Clamp(v, minVal, absMax);
    }

    // Fraction of the absolute range to expand symmetrically on double-click when
    // collapsed. 0.1f = ±10% of (absMax - absMin) around the current value.
    const float k_RangeExpandFraction = 0.1f;

    // Resolves a slider def's bipolar center value. NaN (unset) returns the midpoint of
    // [absMin, absMax]. Clamped to the absolute range so callers never produce a centerX
    // outside the track.
    static float ResolveBipolarCenter(ZUISliderDef def, float absMin, float absMax)
    {
        float c = def.bipolarCenter;
        if (float.IsNaN(c)) c = (absMin + absMax) * 0.5f;
        return Mathf.Clamp(c, absMin, absMax);
    }

    // Shared double-click toggle for all MinMax slider flavors.
    // - Collapsed (min == max formatted): expand symmetrically around current value by
    //   k_RangeExpandFraction × absRange, clamped to [absMin, absMax].
    // - Not collapsed: collapse toward the clicked side — left half → max = min,
    //   right half → min = max.
    // Returns true if the event was consumed.
    static bool TryHandleRangeDoubleClick(Event ev, Rect hitRect, float valueCenterX,
                                          ref float minVal, ref float maxVal,
                                          float absMin, float absMax, bool collapsed)
    {
        if (ev.type != EventType.MouseDown || ev.button != 0 || ev.clickCount != 2) return false;
        if (!hitRect.Contains(ev.mousePosition)) return false;

        if (collapsed)
        {
            float range = absMax - absMin;
            if (range <= 0f) return false;
            float half = range * k_RangeExpandFraction * 0.5f;
            float center = minVal; // == maxVal in collapsed state
            float nMin = Mathf.Max(absMin, center - half);
            float nMax = Mathf.Min(absMax, center + half);
            // If one side clipped, expand the other to preserve total spread where possible.
            float spread = range * k_RangeExpandFraction;
            if (nMin == absMin) nMax = Mathf.Min(absMax, absMin + spread);
            else if (nMax == absMax) nMin = Mathf.Max(absMin, absMax - spread);
            minVal = nMin;
            maxVal = nMax;
        }
        else
        {
            // Collapse toward the clicked side.
            if (ev.mousePosition.x <= valueCenterX) maxVal = minVal;
            else                                    minVal = maxVal;
        }
        GUIUtility.hotControl = 0;
        GUI.changed = true;
        ev.Use();
        return true;
    }

    // =========================================================================
    // MicroSlider-a button/box whose fill acts as a slider track. No thumb.
    // Hold-click and drag to change value.
    //
    // Label options (MicroSliderLabelMode):
    //   Auto            → LabelOnly when showInputField is true (value lives in the field);
    //                     LabelAndValue otherwise (legacy default "Label: 42").
    //   None            → nothing drawn in the track.
    //   LabelOnly       → just `label` centered.
    //   ValueOnly       → just the formatted value centered.
    //   LabelAndValue   → "Label: value" centered.
    // =========================================================================

    public enum MicroSliderLabelMode { Auto, None, LabelOnly, ValueOnly, LabelAndValue }

    public static float MicroSlider(float value, float min, float max,
                                     string label = "",
                                     string style = SliderStyle.Default,
                                     bool showInputField = false,
                                     float? defaultValue = null,
                                     params GUILayoutOption[] options)
    {
        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        float h = Mathf.Max(def.trackHeight, 18f);
        var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, AppendHeight(options, h));
        return DrawMicroSlider(rect, value, min, max, label, def, style, showInputField, defaultValue, MicroSliderLabelMode.Auto);
    }

    public static float MicroSlider(Rect rect, float value, float min, float max,
                                     string label = "",
                                     string style = SliderStyle.Default,
                                     bool showInputField = false,
                                     float? defaultValue = null)
    {
        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        return DrawMicroSlider(rect, value, min, max, label, def, style, showInputField, defaultValue, MicroSliderLabelMode.Auto);
    }

    /// <summary>MicroSlider variant that accepts an explicit label mode. Mirrors the label-mode
    /// API on MicroMinMax so consumers can switch single-value and range sliders to the same
    /// display convention.</summary>
    public static float MicroSlider(Rect rect, float value, float min, float max,
                                     string label,
                                     string style,
                                     bool showInputField,
                                     MicroSliderLabelMode labelMode,
                                     float? defaultValue = null)
    {
        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        return DrawMicroSlider(rect, value, min, max, label, def, style, showInputField, defaultValue, labelMode);
    }

    static float DrawMicroSlider(Rect totalRect, float value, float min, float max,
                                  string label, ZUISliderDef def, string styleName,
                                  bool showInputField, float? defaultValue,
                                  MicroSliderLabelMode labelMode)
    {
        value = Mathf.Clamp(value, min, max);

        // Carve input field if needed
        Rect trackRect = totalRect;
        Rect fieldRect = default;
        if (showInputField && def.valueWidth > 0f)
        {
            float gap = 4f;
            trackRect = new Rect(totalRect.x, totalRect.y,
                                  totalRect.width - def.valueWidth - gap, totalRect.height);
            fieldRect = new Rect(trackRect.xMax + gap, totalRect.y,
                                  def.valueWidth, totalRect.height);
        }

        // Input-drag anywhere on the track
        int  id     = GUIUtility.GetControlID(FocusType.Passive, trackRect);
        var  ev     = Event.current;
        bool isDrag = GUIUtility.hotControl == id;
        bool isHover = trackRect.Contains(ev.mousePosition);

        // Double-click to reset
        if (defaultValue.HasValue
            && ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2
            && trackRect.Contains(ev.mousePosition))
        {
            value = Mathf.Clamp(defaultValue.Value, min, max);
            GUIUtility.hotControl = 0;
            GUI.changed = true;
            ev.Use();
        }

        switch (ev.type)
        {
            case EventType.MouseDown:
                if (trackRect.Contains(ev.mousePosition) && ev.button == 0)
                {
                    GUIUtility.hotControl = id;
                    value = SamplePosition(ev.mousePosition.x, trackRect.x, trackRect.xMax, min, max);
                    GUI.changed = true;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (isDrag)
                {
                    value = SamplePosition(ev.mousePosition.x, trackRect.x, trackRect.xMax, min, max);
                    GUI.changed = true;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (isDrag) { GUIUtility.hotControl = 0; ev.Use(); }
                break;
        }

        // Recompute fill after input
        float t = InverseLerpSafe(min, max, value);

        if (ev.type == EventType.Repaint)
        {
            float splitX = trackRect.x + t * trackRect.width;
            var fillRect  = new Rect(trackRect.x, trackRect.y, splitX - trackRect.x, trackRect.height);
            var emptyRect = new Rect(splitX, trackRect.y, trackRect.xMax - splitX, trackRect.height);

            if (fillRect.width  > 0f) def.trackFill?.DrawBackground(fillRect);
            if (emptyRect.width > 0f) def.track?.DrawBackground(emptyRect);

            // Draw label + value text on top of track. Auto resolves to LabelOnly when an input
            // field is shown (value lives in the field) or LabelAndValue otherwise (legacy).
            string fmt = !string.IsNullOrEmpty(def.valueFormat) ? def.valueFormat : AutoFormat(min, max);
            string valueStr = value.ToString(fmt);
            var resolvedMode = labelMode == MicroSliderLabelMode.Auto
                ? (showInputField ? MicroSliderLabelMode.LabelOnly : MicroSliderLabelMode.LabelAndValue)
                : labelMode;

            string displayText = null;
            switch (resolvedMode)
            {
                case MicroSliderLabelMode.None: break;
                case MicroSliderLabelMode.LabelOnly:
                    displayText = label;
                    break;
                case MicroSliderLabelMode.ValueOnly:
                    displayText = valueStr;
                    break;
                case MicroSliderLabelMode.LabelAndValue:
                    displayText = string.IsNullOrEmpty(label) ? valueStr : $"{label}: {valueStr}";
                    break;
            }

            if (!string.IsNullOrEmpty(displayText))
            {
                var textStyle = def.GetLabelStyle(ActiveSheet);
                textStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(trackRect, displayText, textStyle);
            }
        }

        // Value input field
        if (showInputField && def.valueWidth > 0f)
        {
            EditorGUI.BeginChangeCheck();
            float next = EditorGUI.FloatField(fieldRect, value, def.GetValueStyle(ActiveSheet));
            if (EditorGUI.EndChangeCheck())
                value = Mathf.Clamp(next, min, max);
        }

        if (StyleDebugMode && IsDebugHit(trackRect))
            CollectSliderDebugInfo(def, styleName, trackRect, isRange: false);

        DrawFlashOverlayIfNeeded(trackRect, styleName, 0, FlashDefType.Slider);

        return value;
    }

    // =========================================================================
    // MicroMinMax-MicroSlider variant with two edges (min, max).
    // No thumbs: edges are thin 2px vertical lines; whole rect is the track.
    // Click in fill zone pans both; click near an edge drags that edge; click on
    // empty rail moves the nearer edge. Label (and optionally two value inputs)
    // render inside/next to the track, matching MicroSlider's chromeless look.
    //
    // Label options (MicroMinMaxLabelMode):
    //   Auto            → LabelAndValues when no fields, LabelOnly when fields shown (legacy default).
    //   None            → nothing drawn in the track.
    //   LabelOnly       → just `label` centered.
    //   ValuesOnly      → just `min-max` centered.
    //   LabelAndValues  → `label min-max` centered (space-separated, no colon).
    // =========================================================================

    public enum MicroMinMaxLabelMode { Auto, None, LabelOnly, ValuesOnly, LabelAndValues }

    static readonly int s_MmmMinHash  = "ZUIMmmMin".GetHashCode();
    static readonly int s_MmmMaxHash  = "ZUIMmmMax".GetHashCode();
    static readonly int s_MmmFillHash = "ZUIMmmFill".GetHashCode();

    public static void MicroMinMax(ref float minVal, ref float maxVal,
                                    float absMin, float absMax,
                                    string label = "",
                                    string style = SliderStyle.Default,
                                    bool showInputFields = false,
                                    MicroMinMaxLabelMode labelMode = MicroMinMaxLabelMode.Auto,
                                    params GUILayoutOption[] options)
    {
        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        float h = Mathf.Max(def.trackHeight, 18f);
        var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, AppendHeight(options, h));
        DrawMicroMinMax(rect, ref minVal, ref maxVal, absMin, absMax, label, def, style, showInputFields, labelMode);
    }

    public static void MicroMinMax(Rect rect, ref float minVal, ref float maxVal,
                                    float absMin, float absMax,
                                    string label = "",
                                    string style = SliderStyle.Default,
                                    bool showInputFields = false,
                                    MicroMinMaxLabelMode labelMode = MicroMinMaxLabelMode.Auto)
    {
        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        DrawMicroMinMax(rect, ref minVal, ref maxVal, absMin, absMax, label, def, style, showInputFields, labelMode);
    }

    // Shared drag-pan anchors-separate from SliderRange to avoid cross-control interference.
    static float _mmmDragAnchorMin;
    static float _mmmDragAnchorMax;
    static float _mmmDragAnchorX;

    static void DrawMicroMinMax(Rect totalRect, ref float minVal, ref float maxVal,
                                 float absMin, float absMax,
                                 string label, ZUISliderDef def, string styleName,
                                 bool showInputFields,
                                 MicroMinMaxLabelMode labelMode)
    {
        Debug.Assert(absMin <= absMax, $"[ZUI.MicroMinMax] absMin ({absMin}) must be <= absMax ({absMax})");
        minVal = Mathf.Clamp(minVal, absMin, absMax);
        maxVal = Mathf.Clamp(maxVal, minVal, absMax);

        // Carve optional input fields off the right. In range mode each field displays just
        // one edge of the range (smaller number), so each gets 60% of the style's valueWidth.
        // (Collapsed mode uses the combined area for a single field — see field-draw block below.)
        const float k_RangeFieldWidthFactor = 0.6f;
        Rect trackRect = totalRect;
        Rect fieldMinRect = default, fieldMaxRect = default;
        if (showInputFields && def.valueWidth > 0f)
        {
            const float gap = 4f;
            float perField = def.valueWidth * k_RangeFieldWidthFactor;
            float fieldsW = perField * 2f + gap;
            trackRect = new Rect(totalRect.x, totalRect.y,
                                  totalRect.width - fieldsW - gap, totalRect.height);
            fieldMinRect = new Rect(trackRect.xMax + gap, totalRect.y, perField, totalRect.height);
            fieldMaxRect = new Rect(fieldMinRect.xMax + gap, totalRect.y, perField, totalRect.height);
        }

        float travelMin = trackRect.x;
        float travelMax = trackRect.xMax;
        float travelLen = Mathf.Max(1f, travelMax - travelMin);

        float tMin = InverseLerpSafe(absMin, absMax, minVal);
        float tMax = InverseLerpSafe(absMin, absMax, maxVal);
        float xMin = travelMin + tMin * travelLen;
        float xMax = travelMin + tMax * travelLen;

        int  idMin   = GUIUtility.GetControlID(s_MmmMinHash,  FocusType.Passive, trackRect);
        int  idMax   = GUIUtility.GetControlID(s_MmmMaxHash,  FocusType.Passive, trackRect);
        int  idFill  = GUIUtility.GetControlID(s_MmmFillHash, FocusType.Passive, trackRect);
        var  ev      = Event.current;
        bool dragMin = GUIUtility.hotControl == idMin;
        bool dragMax = GUIUtility.hotControl == idMax;
        bool dragFil = GUIUtility.hotControl == idFill;

        const float edgeGrab = 6f; // hit tolerance around each edge line

        // Collapsed state: both values formatted identically → render & interact like a
        // single-value MicroSlider. Uses formatted-string comparison so the visual matches the
        // label (e.g. 57.4 and 57.6 both display as "57" and collapse visually too).
        string fmt = !string.IsNullOrEmpty(def.valueFormat) ? def.valueFormat : AutoFormat(absMin, absMax);
        bool collapsed = minVal.ToString(fmt) == maxVal.ToString(fmt);

        // Bipolar center — position of the neutral marker in track-pixel coords.
        float bipCenterVal = def.bipolar ? ResolveBipolarCenter(def, absMin, absMax) : 0f;
        float bipCenterX   = def.bipolar ? travelMin + InverseLerpSafe(absMin, absMax, bipCenterVal) * travelLen : 0f;

        // Bipolar center-thumb double-click — checked before the shared helper so it wins over
        // the standard "collapse to side" behavior when the click lands on the center marker.
        if (def.bipolar && ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2
            && Mathf.Abs(ev.mousePosition.x - bipCenterX) <= edgeGrab
            && trackRect.Contains(ev.mousePosition))
        {
            bool alreadyAtCenter = collapsed && minVal.ToString(fmt) == bipCenterVal.ToString(fmt);
            if (alreadyAtCenter)
            {
                // Second double-click: expand symmetrically around center.
                float range = absMax - absMin;
                float half  = range * k_RangeExpandFraction * 0.5f;
                minVal = Mathf.Max(absMin, bipCenterVal - half);
                maxVal = Mathf.Min(absMax, bipCenterVal + half);
            }
            else
            {
                // First double-click: collapse to center.
                minVal = maxVal = bipCenterVal;
            }
            GUIUtility.hotControl = 0;
            GUI.changed = true;
            ev.Use();
        }
        // Standard double-click: collapsed → expand symmetrically, otherwise → collapse toward
        // clicked side. Shared across all MinMax flavors.
        else if (TryHandleRangeDoubleClick(ev, trackRect, (xMin + xMax) * 0.5f, ref minVal, ref maxVal, absMin, absMax, collapsed))
        {
            // Event consumed; skip further input this frame.
        }
        else if (ev.type == EventType.MouseDown && ev.button == 0 && trackRect.Contains(ev.mousePosition))
        {
            float mx = ev.mousePosition.x;

            if (collapsed)
            {
                // Single-value behavior: click snaps both to the clicked position, drag pans.
                float v = SamplePosition(mx, travelMin, travelMax, absMin, absMax);
                minVal = maxVal = Mathf.Clamp(v, absMin, absMax);
                GUIUtility.hotControl = idFill;
                _mmmDragAnchorMin = minVal;
                _mmmDragAnchorMax = maxVal;
                _mmmDragAnchorX   = mx;
                GUI.changed = true;
                ev.Use();
            }
            else
            {
                bool nearMin = Mathf.Abs(mx - xMin) <= edgeGrab;
                bool nearMax = Mathf.Abs(mx - xMax) <= edgeGrab;
                bool inFill  = !nearMin && !nearMax && mx > xMin && mx < xMax;

                if (inFill)
                {
                    GUIUtility.hotControl = idFill;
                    _mmmDragAnchorMin = minVal;
                    _mmmDragAnchorMax = maxVal;
                    _mmmDragAnchorX   = mx;
                }
                else
                {
                    // If ambiguously near both (overlap), pick by side: left half → min, right half → max.
                    bool grabMin = nearMin;
                    if (nearMin && nearMax) grabMin = mx <= (xMin + xMax) * 0.5f;
                    else if (!nearMin && !nearMax)
                        grabMin = Mathf.Abs(mx - xMin) <= Mathf.Abs(mx - xMax);

                    GUIUtility.hotControl = grabMin ? idMin : idMax;
                    ApplyRangeDrag(mx, travelMin, travelMax, absMin, absMax,
                                   ref minVal, ref maxVal, dragMin: grabMin);
                }
                GUI.changed = true;
                ev.Use();
            }
        }
        else if (ev.type == EventType.MouseDrag && (dragMin || dragMax))
        {
            ApplyRangeDrag(ev.mousePosition.x, travelMin, travelMax, absMin, absMax,
                           ref minVal, ref maxVal, dragMin: dragMin);
            GUI.changed = true;
            ev.Use();
        }
        else if (ev.type == EventType.MouseDrag && dragFil)
        {
            float dx   = ev.mousePosition.x - _mmmDragAnchorX;
            float dVal = dx / travelLen * (absMax - absMin);
            float span = _mmmDragAnchorMax - _mmmDragAnchorMin;
            float nMin = Mathf.Clamp(_mmmDragAnchorMin + dVal, absMin, absMax - span);
            minVal = nMin;
            maxVal = nMin + span;
            GUI.changed = true;
            ev.Use();
        }
        else if (ev.type == EventType.MouseUp && (dragMin || dragMax || dragFil))
        {
            GUIUtility.hotControl = 0;
            ev.Use();
        }

        // Recompute after input
        minVal = Mathf.Clamp(minVal, absMin, absMax);
        maxVal = Mathf.Clamp(maxVal, minVal, absMax);
        tMin = InverseLerpSafe(absMin, absMax, minVal);
        tMax = InverseLerpSafe(absMin, absMax, maxVal);
        xMin = travelMin + tMin * travelLen;
        xMax = travelMin + tMax * travelLen;
        collapsed = minVal.ToString(fmt) == maxVal.ToString(fmt);

        if (ev.type == EventType.Repaint)
        {
            if (collapsed)
            {
                if (def.bipolar)
                {
                    // Bipolar + collapsed: fill originates at center and extends toward the value.
                    // Value at center → no visible fill. Value above center → fill right of center.
                    // Value below center → fill left of center.
                    float splitX = xMin; // xMin == xMax when collapsed
                    float fillL  = Mathf.Min(bipCenterX, splitX);
                    float fillR  = Mathf.Max(bipCenterX, splitX);

                    var leftEmpty  = new Rect(trackRect.x, trackRect.y, fillL - trackRect.x, trackRect.height);
                    var fillRect   = new Rect(fillL, trackRect.y, fillR - fillL, trackRect.height);
                    var rightEmpty = new Rect(fillR, trackRect.y, trackRect.xMax - fillR, trackRect.height);

                    if (leftEmpty.width  > 0f) def.track?.DrawBackground(leftEmpty);
                    if (fillRect.width   > 0f) def.trackFill?.DrawBackground(fillRect);
                    if (rightEmpty.width > 0f) def.track?.DrawBackground(rightEmpty);
                }
                else
                {
                    // Collapsed (min == max): render as a MicroSlider at the value — fill grows from
                    // trackStart to the value position, empty rest. No edge lines.
                    float splitX = xMin; // xMin == xMax when collapsed
                    var fillRect  = new Rect(trackRect.x, trackRect.y, splitX - trackRect.x, trackRect.height);
                    var emptyRect = new Rect(splitX, trackRect.y, trackRect.xMax - splitX, trackRect.height);
                    if (fillRect.width  > 0f) def.trackFill?.DrawBackground(fillRect);
                    if (emptyRect.width > 0f) def.track?.DrawBackground(emptyRect);
                }
            }
            else
            {
                var leftEmpty  = new Rect(trackRect.x, trackRect.y, xMin - trackRect.x, trackRect.height);
                var fillRect   = new Rect(xMin, trackRect.y, xMax - xMin, trackRect.height);
                var rightEmpty = new Rect(xMax, trackRect.y, trackRect.xMax - xMax, trackRect.height);

                if (leftEmpty.width  > 0f) def.track?.DrawBackground(leftEmpty);
                if (fillRect.width   > 0f) def.trackFill?.DrawBackground(fillRect);
                if (rightEmpty.width > 0f) def.track?.DrawBackground(rightEmpty);

                // Edge thumbs — full ZUI thumb visual routed through the sheet's thumb defs so
                // colors, border, corner radius, and hover/active states all come from the style.
                // Width is def.thumbWidth (0 = no visible edge; clicks still work via the fixed
                // edgeGrab proximity tolerance on MouseDown). Height is the full track height.
                float edgeW = Mathf.Max(0f, def.thumbWidth);
                if (edgeW > 0f)
                {
                    var minRect = new Rect(xMin - edgeW * 0.5f, trackRect.y, edgeW, trackRect.height);
                    var maxRect = new Rect(xMax - edgeW * 0.5f, trackRect.y, edgeW, trackRect.height);

                    var minDef = def.thumb;
                    var maxDef = def.thumbMax ?? def.thumb;

                    bool hoverMin = minRect.Contains(ev.mousePosition);
                    bool hoverMax = maxRect.Contains(ev.mousePosition);
                    var  minState = dragMin ? ZUIButtonDrawState.Active : hoverMin ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal;
                    var  maxState = dragMax ? ZUIButtonDrawState.Active : hoverMax ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal;
                    // At tiny widths the corner-rounded draw path insets the fill by the border width
                    // on each side, which eats the visible area for edgeW <= 2. Fall through to the
                    // unrounded branch by passing cornerRadius=0 when the edge is thin.
                    bool tinyEdge = edgeW <= 2f;
                    int  minCR    = tinyEdge ? 0 : (minDef?.GetResolvedCornerRadius() ?? 0);
                    int  maxCR    = tinyEdge ? 0 : (maxDef?.GetResolvedCornerRadius() ?? 0);
                    minDef?.DrawVisual(minRect, minState, minCR);
                    maxDef?.DrawVisual(maxRect, maxState, maxCR);
                }
            }

            // Center marker (bipolar only) — non-draggable thumb at centerX. Hover state reacts
            // to mouse proximity to hint that double-clicking it does something. Drawn last so it
            // sits on top of the fill when the range straddles center.
            if (def.bipolar)
            {
                float cEdgeW = Mathf.Max(0f, def.thumbWidth);
                if (cEdgeW > 0f)
                {
                    var cRect = new Rect(bipCenterX - cEdgeW * 0.5f, trackRect.y, cEdgeW, trackRect.height);
                    var cDef  = def.thumbCenter ?? def.thumb;
                    bool hoverC = Mathf.Abs(ev.mousePosition.x - bipCenterX) <= edgeGrab;
                    var  cState = hoverC ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal;
                    bool tinyC  = cEdgeW <= 2f;
                    int  cCR    = tinyC ? 0 : (cDef?.GetResolvedCornerRadius() ?? 0);
                    cDef?.DrawVisual(cRect, cState, cCR);
                }
            }

            // Label/value readout inside the track. Resolve Auto → concrete mode based on whether
            // external input fields are shown (same behavior as the legacy implicit path).
            var textStyle = def.GetLabelStyle(ActiveSheet);
            textStyle.alignment = TextAnchor.MiddleCenter;
            var resolvedMode = labelMode == MicroMinMaxLabelMode.Auto
                ? (showInputFields ? MicroMinMaxLabelMode.LabelOnly : MicroMinMaxLabelMode.LabelAndValues)
                : labelMode;

            string display = null;
            switch (resolvedMode)
            {
                case MicroMinMaxLabelMode.None: break;
                case MicroMinMaxLabelMode.LabelOnly:
                    display = label;
                    break;
                case MicroMinMaxLabelMode.ValuesOnly:
                {
                    string vMin = minVal.ToString(fmt);
                    string vMax = maxVal.ToString(fmt);
                    display = vMin == vMax ? vMin : $"{vMin}-{vMax}";
                    break;
                }
                case MicroMinMaxLabelMode.LabelAndValues:
                {
                    string minStr = minVal.ToString(fmt);
                    string maxStr = maxVal.ToString(fmt);
                    string values = minStr == maxStr ? minStr : $"{minStr}-{maxStr}";
                    display = string.IsNullOrEmpty(label) ? values : $"{label} {values}";
                    break;
                }
            }
            if (!string.IsNullOrEmpty(display))
                GUI.Label(trackRect, display, textStyle);
        }

        // Value input fields. When collapsed, collapse the two reserved boxes into a single wide
        // field spanning the same total area so layout stays identical across collapsed/expanded.
        if (showInputFields && def.valueWidth > 0f)
        {
            var valueStyle = def.GetValueStyle(ActiveSheet);

            if (collapsed)
            {
                var combinedRect = new Rect(fieldMinRect.x, fieldMinRect.y,
                                             fieldMaxRect.xMax - fieldMinRect.x, fieldMinRect.height);
                EditorGUI.BeginChangeCheck();
                float newVal = EditorGUI.FloatField(combinedRect,
                    float.Parse(minVal.ToString(fmt), System.Globalization.CultureInfo.InvariantCulture),
                    valueStyle);
                if (EditorGUI.EndChangeCheck())
                {
                    float clamped = Mathf.Clamp(newVal, absMin, absMax);
                    minVal = clamped;
                    maxVal = clamped;
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                float newMin = EditorGUI.FloatField(fieldMinRect,
                    float.Parse(minVal.ToString(fmt), System.Globalization.CultureInfo.InvariantCulture),
                    valueStyle);
                if (EditorGUI.EndChangeCheck())
                    minVal = Mathf.Clamp(newMin, absMin, maxVal);

                EditorGUI.BeginChangeCheck();
                float newMax = EditorGUI.FloatField(fieldMaxRect,
                    float.Parse(maxVal.ToString(fmt), System.Globalization.CultureInfo.InvariantCulture),
                    valueStyle);
                if (EditorGUI.EndChangeCheck())
                    maxVal = Mathf.Clamp(newMax, minVal, absMax);
            }
        }

        if (StyleDebugMode && IsDebugHit(trackRect))
            CollectSliderDebugInfo(def, styleName, trackRect, isRange: true);

        DrawFlashOverlayIfNeeded(trackRect, styleName, 0, FlashDefType.Slider);
    }

    static float InverseLerpSafe(float min, float max, float v)
    {
        if (Mathf.Approximately(min, max)) return 0f;
        if (max > min) return Mathf.InverseLerp(min, max, v);
        // Inverted range: flip and return (e.g. min=20, max=-20, v=10 → t=0.25)
        return Mathf.InverseLerp(max, min, v);
    }

    static float SamplePosition(float mouseX, float travelMin, float travelMax, float min, float max)
    {
        float t = Mathf.InverseLerp(travelMin, travelMax, mouseX);
        return Mathf.Lerp(min, max, Mathf.Clamp01(t));
    }

    // Vertical: mouseY maps to value. travelTop = max value pixel, travelBottom = min value pixel.
    static float SamplePositionVertical(float mouseY, float travelTop, float travelBottom, float min, float max)
    {
        float t = Mathf.InverseLerp(travelBottom, travelTop, mouseY);
        return Mathf.Lerp(min, max, Mathf.Clamp01(t));
    }

    // For horizontal sliders: height is derived from thumbHeight / trackHeight.
    // For vertical sliders: we return thumbWidth as the "size" dimension for GUILayoutUtility.
    static float SliderTotalHeight(ZUISliderDef def, bool vertical)
    {
        if (vertical)
            return Mathf.Max(def.thumbWidth > 0f ? def.thumbWidth : 20f, def.trackHeight);
        // Ensure total height is at least singleLineHeight so inline labels aren't clipped
        float h = Mathf.Max(def.thumbHeight > 0f ? def.thumbHeight : 20f, def.trackHeight);
        return Mathf.Max(h, EditorGUIUtility.singleLineHeight);
    }

    /// <summary>Auto-format based on slider range. Small range = more decimals.</summary>
    static string AutoFormat(float min, float max)
    {
        float range = Mathf.Abs(max - min);
        if (range <= 1f)   return "F2";
        if (range <= 10f)  return "F1";
        return "F0";
    }

    // ===== 2D Slider (XY Pad) ==================================================
    // A square pad for editing two float values simultaneously.
    // Reuses the active slider style's track for background and trackFill for the indicator.
    // Default Y axis: bottom = min, top = max (mathematical orientation).
    // Pass flipY = true for screen-space orientation (top = min, bottom = max).

    public static Vector2 Slider2D(Vector2 value, Vector2 min, Vector2 max,
                                    float size = 100f,
                                    string style = SliderStyle.Default,
                                    string labelX = null, string labelY = null,
                                    Vector2? defaultValue = null,
                                    bool flipY = false)
    {
        var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
        return DrawSlider2D(rect, value, min, max, style, labelX, labelY, defaultValue, flipY);
    }

    public static Vector2 Slider2D(Rect rect, Vector2 value, Vector2 min, Vector2 max,
                                    string style = SliderStyle.Default,
                                    string labelX = null, string labelY = null,
                                    Vector2? defaultValue = null,
                                    bool flipY = false)
    {
        return DrawSlider2D(rect, value, min, max, style, labelX, labelY, defaultValue, flipY);
    }

    static Vector2 DrawSlider2D(Rect rect, Vector2 value, Vector2 min, Vector2 max,
                                 string styleName, string labelX, string labelY,
                                 Vector2? defaultValue, bool flipY = false)
    {
        value.x = Mathf.Clamp(value.x, Mathf.Min(min.x, max.x), Mathf.Max(min.x, max.x));
        value.y = Mathf.Clamp(value.y, Mathf.Min(min.y, max.y), Mathf.Max(min.y, max.y));
        var def = ActiveSheet?.FindSlider(styleName) ?? new ZUISliderDef();

        int  id      = GUIUtility.GetControlID(FocusType.Passive, rect);
        var  ev      = Event.current;
        bool isDrag  = GUIUtility.hotControl == id;
        bool isHover = rect.Contains(ev.mousePosition);

        // Double-click to reset
        if (defaultValue.HasValue
            && ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2
            && rect.Contains(ev.mousePosition))
        {
            value = new Vector2(
                Mathf.Clamp(defaultValue.Value.x, Mathf.Min(min.x, max.x), Mathf.Max(min.x, max.x)),
                Mathf.Clamp(defaultValue.Value.y, Mathf.Min(min.y, max.y), Mathf.Max(min.y, max.y)));
            GUIUtility.hotControl = 0;
            GUI.changed = true;
            ev.Use();
        }

        switch (ev.type)
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.mousePosition) && ev.button == 0)
                {
                    GUIUtility.hotControl = id;
                    value = SamplePosition2D(ev.mousePosition, rect, min, max, flipY);
                    GUI.changed = true;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (isDrag)
                {
                    value = SamplePosition2D(ev.mousePosition, rect, min, max, flipY);
                    GUI.changed = true;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (isDrag) { GUIUtility.hotControl = 0; ev.Use(); }
                break;
        }

        if (ev.type == EventType.Repaint)
        {
            float tx = InverseLerpSafe(min.x, max.x, value.x);
            float ty = InverseLerpSafe(min.y, max.y, value.y);
            float px = rect.x + tx * rect.width;
            float py = flipY
                ? rect.y + ty * rect.height       // flipY: top = min, down = increase
                : rect.yMax - ty * rect.height;   // default: bottom = min, up = increase

            // Background
            def.track?.DrawBackground(rect);

            // Grid lines (subtle)
            var gridColor = new Color(1f, 1f, 1f, 0.06f);
            float midX = rect.x + rect.width * 0.5f;
            float midY = rect.y + rect.height * 0.5f;
            EditorGUI.DrawRect(new Rect(midX - 0.5f, rect.y, 1f, rect.height), gridColor);
            EditorGUI.DrawRect(new Rect(rect.x, midY - 0.5f, rect.width, 1f), gridColor);

            // Crosshair lines
            var crossColor = (isDrag || isHover)
                ? new Color(1f, 1f, 1f, 0.5f)
                : new Color(1f, 1f, 1f, 0.25f);
            EditorGUI.DrawRect(new Rect(px - 0.5f, rect.y, 1f, rect.height), crossColor);
            EditorGUI.DrawRect(new Rect(rect.x, py - 0.5f, rect.width, 1f), crossColor);

            // Indicator dot
            float dotR = isDrag ? 5f : 4f;
            var dotColor = (isDrag || isHover)
                ? new Color(1f, 1f, 1f, 0.9f)
                : new Color(1f, 1f, 1f, 0.6f);
            var fillColor = def.trackFill != null
                ? def.trackFill.background.GetColorA(def.trackFill.ownerSheet)
                : new Color(0.3f, 0.6f, 1f, 1f);
            // Outer ring
            EditorGUI.DrawRect(new Rect(px - dotR, py - dotR, dotR * 2f, dotR * 2f), dotColor);
            // Inner fill
            EditorGUI.DrawRect(new Rect(px - dotR + 1f, py - dotR + 1f, dotR * 2f - 2f, dotR * 2f - 2f), fillColor);

            // Value readout
            string fmtX = AutoFormat(min.x, max.x);
            string fmtY = AutoFormat(min.y, max.y);
            string readout = $"{value.x.ToString(fmtX)}, {value.y.ToString(fmtY)}";
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) },
                alignment = TextAnchor.LowerRight,
                fontSize = 9,
                padding = new RectOffset(2, 4, 0, 2),
            };
            GUI.Label(rect, readout, labelStyle);

            // Axis labels
            if (!string.IsNullOrEmpty(labelX))
            {
                var xStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 1f, 1f, 0.3f) },
                    alignment = TextAnchor.LowerCenter,
                    fontSize = 9,
                };
                GUI.Label(new Rect(rect.x, rect.yMax - 14f, rect.width, 14f), labelX, xStyle);
            }
            if (!string.IsNullOrEmpty(labelY))
            {
                var yStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 1f, 1f, 0.3f) },
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 9,
                };
                // Draw rotated would be ideal, but for now just draw at top-left
                GUI.Label(new Rect(rect.x + 2f, rect.y, 40f, 14f), labelY, yStyle);
            }

            DrawFlashOverlayIfNeeded(rect, styleName, 0, FlashDefType.Slider);
        }

        return value;
    }

    static Vector2 SamplePosition2D(Vector2 mouse, Rect rect, Vector2 min, Vector2 max, bool flipY)
    {
        float tx = Mathf.InverseLerp(rect.x, rect.xMax, mouse.x);
        float ty = flipY
            ? Mathf.InverseLerp(rect.y, rect.yMax, mouse.y)       // flipY: top = min
            : Mathf.InverseLerp(rect.yMax, rect.y, mouse.y);      // default: bottom = min
        return new Vector2(
            Mathf.Lerp(min.x, max.x, Mathf.Clamp01(tx)),
            Mathf.Lerp(min.y, max.y, Mathf.Clamp01(ty)));
    }

    static GUILayoutOption[] AppendHeight(GUILayoutOption[] options, float height)
    {
        var result = new GUILayoutOption[options.Length + 1];
        options.CopyTo(result, 0);
        result[options.Length] = GUILayout.Height(height);
        return result;
    }

    static GUILayoutOption[] AppendWidth(GUILayoutOption[] options, float width)
    {
        var result = new GUILayoutOption[options.Length + 1];
        options.CopyTo(result, 0);
        result[options.Length] = GUILayout.Width(width);
        return result;
    }

}

