// ZUIWindow.Draw.cs
// Instance-method wrappers for the ZUI stable API.
//
// Each wrapper enters a per-call UseSheet(ResolvedSheet) scope before delegating to the
// existing static ZUI.X(...) implementation. This guarantees that this.X(...) inside a
// ZUIWindow subclass always resolves against the window's own sheet regardless of ambient
// state — cross-window sheet leakage is architecturally impossible when consumers use these
// wrappers instead of the static API.
//
// Each signature mirrors its static counterpart exactly. If you need a static overload that
// isn't mirrored here yet, either add it (same one-line pattern) or call ZUI.X(...) directly
// inside a `using (ZUI.UseSheet(ResolvedSheet)) { ... }` block.
//
// NOTE: Zounds' Box/AreaBox API uses `string styleName` whereas ZTracker's uses the `ZUIStyle`
// enum. This file matches Zounds' signatures. When the projects converge, the two Draw.cs
// files can merge.

using System;
using System.Collections.Generic;
using UnityEngine;

public abstract partial class ZUIWindow
{
    // ===== Button =============================================================

    protected bool Button(string label, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(label, style, options); }

    protected bool Button(string label, string style, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(label, style, cornerMask, options); }

    protected bool Button(string label, string style, string tint, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(label, style, tint, options); }

    protected bool Button(string label, string style, string tint, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(label, style, tint, cornerMask, options); }

    protected bool Button(GUIContent content, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(content, style, options); }

    protected bool Button(GUIContent content, string style, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(content, style, cornerMask, options); }

    protected bool Button(GUIContent content, string style, string tint, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(content, style, tint, options); }

    protected bool Button(GUIContent content, string style, string tint, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(content, style, tint, cornerMask, options); }

    protected bool Button(Rect rect, string label, string style = ZUI.Style.Default,
                          ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(rect, label, style, cornerMask); }

    protected bool Button(Rect rect, GUIContent content, string style = ZUI.Style.Default,
                          ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(rect, content, style, cornerMask); }

    protected bool Button(Rect rect, string label, string style, string tint,
                          ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(rect, label, style, tint, cornerMask); }

    protected bool Button(Rect rect, GUIContent content, string style, string tint,
                          ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(rect, content, style, tint, cornerMask); }

    protected bool Button(string label, Texture2D icon, ZIconPlacement placement = ZIconPlacement.LeftOfLabel,
                          string style = ZUI.Style.Default, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(label, icon, placement, style, options); }

    protected bool Button(Rect rect, string label, Texture2D icon, ZIconPlacement placement = ZIconPlacement.LeftOfLabel,
                          string style = ZUI.Style.Default)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(rect, label, icon, placement, style); }

    protected bool Button(string label, string iconId, ZIconPlacement placement, string style = ZUI.Style.Default,
                          params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(label, iconId, placement, style, options); }

    protected bool Button(Rect rect, string label, string iconId, ZIconPlacement placement,
                          string style = ZUI.Style.Default)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(rect, label, iconId, placement, style); }

    protected bool Button(string label, ZUIButtonDef def, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(label, def, options); }

    protected bool Button(GUIContent content, ZUIButtonDef def, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(content, def, options); }

    protected bool Button(string label, ZUIButtonDef def, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(label, def, cornerMask, options); }

    protected bool Button(GUIContent content, ZUIButtonDef def, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(content, def, cornerMask, options); }

    protected bool Button(Rect rect, string label, ZUIButtonDef def, ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(rect, label, def, cornerMask); }

    protected bool Button(Rect rect, GUIContent content, ZUIButtonDef def, ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Button(rect, content, def, cornerMask); }

    // ===== Toggle =============================================================

    protected bool Toggle(bool value, string label, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, label, style, options); }

    protected bool Toggle(bool value, GUIContent content, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, content, style, options); }

    protected bool Toggle(bool value, string label, string style, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, label, style, cornerMask, options); }

    protected bool Toggle(bool value, GUIContent content, string style, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, content, style, cornerMask, options); }

    protected bool Toggle(bool value, string label, string style, string tint, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, label, style, tint, options); }

    protected bool Toggle(bool value, string label, string style, string tint, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, label, style, tint, cornerMask, options); }

    protected bool Toggle(bool value, GUIContent content, string style, string tint, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, content, style, tint, options); }

    protected bool Toggle(bool value, GUIContent content, string style, string tint, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, content, style, tint, cornerMask, options); }

    protected bool Toggle(bool value, string label, Texture offIcon, Texture onIcon,
                          string style = ZUI.Style.Default, ZUICornerMask cornerMask = ZUICornerMask.None,
                          params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, label, offIcon, onIcon, style, cornerMask, options); }

    protected bool Toggle(Rect rect, bool value, string label, string style = ZUI.Style.Default,
                          Color? onColor = null, ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(rect, value, label, style, onColor, cornerMask); }

    protected bool Toggle(Rect rect, bool value, GUIContent content, string style = ZUI.Style.Default,
                          Color? onColor = null, ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(rect, value, content, style, onColor, cornerMask); }

    protected bool Toggle(bool value, string label, ZUIButtonDef def, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, label, def, options); }

    protected bool Toggle(bool value, string label, ZUIButtonDef def, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, label, def, cornerMask, options); }

    protected bool Toggle(bool value, GUIContent content, ZUIButtonDef def, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(value, content, def, options); }

    protected bool Toggle(Rect rect, bool value, string label, ZUIButtonDef def, ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(rect, value, label, def, cornerMask); }

    protected bool Toggle(Rect rect, bool value, GUIContent content, ZUIButtonDef def, ZUICornerMask cornerMask = ZUICornerMask.None)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Toggle(rect, value, content, def, cornerMask); }

    // ===== Label ==============================================================

    protected void Label(string text, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.Label(text, options); }

    protected void Label(string text, ZUI.ZTextStyle style, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.Label(text, style, options); }

    protected void Label(string text, ZUITextStyleDef def, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.Label(text, def, options); }

    protected void Label(Rect rect, string text)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.Label(rect, text); }

    protected void Label(Rect rect, string text, ZUI.ZTextStyle style)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.Label(rect, text, style); }

    protected void Label(Rect rect, string text, ZUITextStyleDef def)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.Label(rect, text, def); }

    protected void TitleText(string text, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.TitleText(text, options); }

    protected void Text(string text, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.Text(text, options); }

    // ===== Slider =============================================================

    protected float Slider(float value, float min, float max,
                           string label = "",
                           string style = ZUI.SliderStyle.Default,
                           float? defaultValue = null,
                           params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Slider(value, min, max, label, style, defaultValue, options); }

    protected float Slider(Rect rect, float value, float min, float max,
                           string label = "",
                           string style = ZUI.SliderStyle.Default,
                           float? defaultValue = null,
                           bool suppressValueField = false)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Slider(rect, value, min, max, label, style, defaultValue, suppressValueField); }

    protected float Slider(float value, float min, float max,
                           string label, ZUISliderDef def,
                           float? defaultValue = null,
                           params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Slider(value, min, max, label, def, defaultValue, options); }

    protected float Slider(Rect rect, float value, float min, float max,
                           string label, ZUISliderDef def,
                           float? defaultValue = null,
                           bool suppressValueField = false)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Slider(rect, value, min, max, label, def, defaultValue, suppressValueField); }

    protected float SliderStacked(float value, float min, float max,
                                   string label = "",
                                   string style = ZUI.SliderStyle.Default,
                                   float? defaultValue = null,
                                   params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.SliderStacked(value, min, max, label, style, defaultValue, options); }

    protected float SliderVertical(float value, float min, float max,
                                    string label = "",
                                    string style = ZUI.SliderStyle.Default,
                                    float? defaultValue = null,
                                    params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.SliderVertical(value, min, max, label, style, defaultValue, options); }

    protected float SliderVertical(Rect rect, float value, float min, float max,
                                    string label = "",
                                    string style = ZUI.SliderStyle.Default,
                                    float? defaultValue = null)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.SliderVertical(rect, value, min, max, label, style, defaultValue); }

    protected float SliderVertical(float value, float min, float max,
                                    string label, ZUISliderDef def,
                                    float? defaultValue = null,
                                    params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.SliderVertical(value, min, max, label, def, defaultValue, options); }

    protected float SliderVertical(Rect rect, float value, float min, float max,
                                    string label, ZUISliderDef def,
                                    float? defaultValue = null)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.SliderVertical(rect, value, min, max, label, def, defaultValue); }

    protected void SliderRange(ref float minVal, ref float maxVal,
                                float absMin, float absMax,
                                string label = "",
                                string style = ZUI.SliderStyle.Default,
                                params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.SliderRange(ref minVal, ref maxVal, absMin, absMax, label, style, options); }

    protected void SliderRange(Rect rect, ref float minVal, ref float maxVal,
                                float absMin, float absMax,
                                string label = "",
                                string style = ZUI.SliderStyle.Default)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.SliderRange(rect, ref minVal, ref maxVal, absMin, absMax, label, style); }

    protected void SliderRange(ref float minVal, ref float maxVal,
                                float absMin, float absMax,
                                string label, ZUISliderDef def,
                                params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.SliderRange(ref minVal, ref maxVal, absMin, absMax, label, def, options); }

    protected float MicroSlider(float value, float min, float max,
                                 string label = "",
                                 string style = ZUI.SliderStyle.Default,
                                 bool showInputField = false,
                                 float? defaultValue = null,
                                 params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.MicroSlider(value, min, max, label, style, showInputField, defaultValue, options); }

    protected float MicroSlider(Rect rect, float value, float min, float max,
                                 string label = "",
                                 string style = ZUI.SliderStyle.Default,
                                 bool showInputField = false,
                                 float? defaultValue = null)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.MicroSlider(rect, value, min, max, label, style, showInputField, defaultValue); }

    protected float MicroSlider(Rect rect, float value, float min, float max,
                                 string label,
                                 string style,
                                 bool showInputField,
                                 ZUI.MicroSliderLabelMode labelMode,
                                 float? defaultValue = null)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.MicroSlider(rect, value, min, max, label, style, showInputField, labelMode, defaultValue); }

    protected void MicroMinMax(ref float minVal, ref float maxVal,
                                float absMin, float absMax,
                                string label = "",
                                string style = ZUI.SliderStyle.Default,
                                bool showInputFields = false,
                                ZUI.MicroMinMaxLabelMode labelMode = ZUI.MicroMinMaxLabelMode.Auto,
                                params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.MicroMinMax(ref minVal, ref maxVal, absMin, absMax, label, style, showInputFields, labelMode, options); }

    protected void MicroMinMax(Rect rect, ref float minVal, ref float maxVal,
                                float absMin, float absMax,
                                string label = "",
                                string style = ZUI.SliderStyle.Default,
                                bool showInputFields = false,
                                ZUI.MicroMinMaxLabelMode labelMode = ZUI.MicroMinMaxLabelMode.Auto)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.MicroMinMax(rect, ref minVal, ref maxVal, absMin, absMax, label, style, showInputFields, labelMode); }

    protected Vector2 Slider2D(Vector2 value, Vector2 min, Vector2 max,
                                float size = 100f,
                                string style = ZUI.SliderStyle.Default,
                                string labelX = null, string labelY = null,
                                Vector2? defaultValue = null,
                                bool flipY = false)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Slider2D(value, min, max, size, style, labelX, labelY, defaultValue, flipY); }

    protected Vector2 Slider2D(Rect rect, Vector2 value, Vector2 min, Vector2 max,
                                string style = ZUI.SliderStyle.Default,
                                string labelX = null, string labelY = null,
                                Vector2? defaultValue = null,
                                bool flipY = false)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Slider2D(rect, value, min, max, style, labelX, labelY, defaultValue, flipY); }

    // ===== Envelope ===========================================================

    protected bool Envelope(List<ZUIEnvelopePoint> points, ZUIColorRef curveColor,
                             string styleName, ZUIEnvelopeRuntime rt,
                             float minWidth = 100f, float height = 80f, int stateKey = 0,
                             params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Envelope(points, curveColor, styleName, rt, minWidth, height, stateKey, options); }

    protected bool Envelope(List<ZUIEnvelopePoint> points, ZUIColorRef curveColor,
                             ZUIEnvelopeDef def, ZUIEnvelopeRuntime rt,
                             float minWidth = 100f, float height = 80f, int stateKey = 0,
                             params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Envelope(points, curveColor, def, rt, minWidth, height, stateKey, options); }

    protected bool Envelope(Rect rect, List<ZUIEnvelopePoint> points, ZUIColorRef curveColor,
                             ZUIEnvelopeDef def, ZUIEnvelopeRuntime rt, int stateKey = 0)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.Envelope(rect, points, curveColor, def, rt, stateKey); }

    // ===== Radio / Cycle ======================================================

    protected int CycleButton(int selected, string[] labels,
                               string style = ZUI.Style.Default, params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.CycleButton(selected, labels, style, options); }

    protected int CycleButton(Rect rect, int selected, string[] labels, string style = ZUI.Style.Default)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.CycleButton(rect, selected, labels, style); }

    protected int CycleArrows(int selected, string[] labels,
                               string style = ZUI.Style.Default, string arrowStyle = "",
                               params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.CycleArrows(selected, labels, style, arrowStyle, options); }

    protected int CycleArrows(Rect rect, int selected, string[] labels,
                               string style = ZUI.Style.Default, string arrowStyle = "")
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.CycleArrows(rect, selected, labels, style, arrowStyle); }

    protected int MiniRadio(int selected, string[] labels,
                             string style = ZUI.Style.Default, bool shaped = true,
                             params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.MiniRadio(selected, labels, style, shaped, options); }

    protected int MiniRadio(Rect rect, int selected, string[] labels, string style = ZUI.Style.Default)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.MiniRadio(rect, selected, labels, style); }

    protected int MiniRadioVertical(int selected, string[] labels,
                                     string style = ZUI.Style.Default, bool shaped = true,
                                     params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.MiniRadioVertical(selected, labels, style, shaped, options); }

    protected int MicroRadio(int selected, string[] labels,
                              string style = ZUI.Style.Default,
                              string activeTextStyle = "",
                              string sideTextStyle = "",
                              bool wrap = false,
                              int sideMaxChars = 5,
                              params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.MicroRadio(selected, labels, style, activeTextStyle, sideTextStyle, wrap, sideMaxChars, options); }

    protected int MicroRadio(Rect rect, int selected, string[] labels,
                              string style = ZUI.Style.Default,
                              string activeTextStyle = "",
                              string sideTextStyle = "",
                              bool wrap = false,
                              int sideMaxChars = 5)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.MicroRadio(rect, selected, labels, style, activeTextStyle, sideTextStyle, wrap, sideMaxChars); }

    // ===== Box (Zounds: string-based styleName, NOT ZUIStyle enum) ============

    // Return IDisposable (not the concrete BoxScope struct) so we can pair the inner box scope
    // with the outer sheet scope via CombinedScope. Consumers use `using (Box(...)) { }` and
    // don't care about the concrete type.

    protected IDisposable Box(string title, string styleName = ZUI.ZUIStyle.Default)
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.Box(title, styleName), outer);
    }

    protected IDisposable Box()
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.Box(), outer);
    }

    protected IDisposable Box(ZUIBoxDef def)
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.Box(def), outer);
    }

    protected IDisposable Box(string title, ZUIBoxDef def)
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.Box(title, def), outer);
    }

    protected IDisposable BoxNamed(string styleName)
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.BoxNamed(styleName), outer);
    }

    protected IDisposable AreaBox(Rect rect, string title = null, string styleName = ZUI.ZUIStyle.Default)
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.AreaBox(rect, title, styleName), outer);
    }

    protected IDisposable AreaBox(Rect rect, ZUIBoxDef def)
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.AreaBox(rect, def), outer);
    }

    protected IDisposable AreaBox(Rect rect, string title, ZUIBoxDef def)
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.AreaBox(rect, title, def), outer);
    }

    protected IDisposable FoldoutBox(string title, string styleName, bool open)
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.FoldoutBox(title, styleName, open), outer);
    }

    protected IDisposable FoldoutBox(string title, ZUIBoxDef def, bool open, string key = null)
    {
        var outer = ZUI.UseSheet(ResolvedSheet);
        return new CombinedScope(ZUI.FoldoutBox(title, def, open, key), outer);
    }

    // ===== Spacing ============================================================

    protected void VerticalSpace()
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.VerticalSpace(); }

    protected void VerticalSpace(float scale)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.VerticalSpace(scale); }

    protected void VerticalSpace(string scaleName)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.VerticalSpace(scaleName); }

    protected void HorizontalSpace()
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.HorizontalSpace(); }

    protected void HorizontalSpace(float scale)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.HorizontalSpace(scale); }

    protected void HorizontalSpace(string scaleName)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.HorizontalSpace(scaleName); }

    protected void RowSpace()
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.RowSpace(); }

    protected void RowSpace(float scale)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.RowSpace(scale); }

    // ===== Blocks =============================================================

    // Blocks returns ZUIBlocksScope directly (not wrapped) so consumers can call
    // blocks.Cell(...) fluently. Cells draw via this.X wrappers which carry their own
    // per-call UseSheet scope; Blocks itself doesn't need an outer sheet scope.
    protected ZUIBlocksScope Blocks(string key = null) => ZUI.Blocks(key);

    // ===== Form ===============================================================
    // Form/Row/Control build up a deferred draw (form.Draw() runs later). Wrapping
    // construction alone isn't enough — the sheet must be active at draw time. Use DrawForm,
    // which holds the sheet scope across configure + Draw.

    protected bool DrawForm(Action<ZUIForm> configure, string id = null)
    {
        using (ZUI.UseSheet(ResolvedSheet))
        {
            var form = ZUI.Form(id);
            configure?.Invoke(form);
            return form.Draw();
        }
    }

    // ===== ToggleRow ==========================================================

    protected bool[] ToggleRow(bool[] values, string[] labels, string style = ZUI.Style.Default,
                                params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.ToggleRow(values, labels, style, options); }

    protected bool[] ToggleRow(bool[] values, GUIContent[] labels, string style = ZUI.Style.Default,
                                params GUILayoutOption[] options)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.ToggleRow(values, labels, style, options); }

    // ===== Palette / misc =====================================================

    protected Color PaletteColor(string name, Color fallback)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.PaletteColor(name, fallback); }

    protected ZUIPaletteColor FindPaletteColor(string name)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.FindPaletteColor(name); }

    protected void FillRect(Rect rect, string paletteColorName)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.FillRect(rect, paletteColorName); }

    protected void FillRect(Rect rect, string paletteColorName, Color fallback)
    { using (ZUI.UseSheet(ResolvedSheet)) ZUI.FillRect(rect, paletteColorName, fallback); }

    protected GUIStyle GetButtonStyle(string style = ZUI.Style.Default)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.GetButtonStyle(style); }

    protected GUIStyle GetTextStyle(ZUI.ZTextStyle style)
    { using (ZUI.UseSheet(ResolvedSheet)) return ZUI.GetTextStyle(style); }

    // ===== Scope helper =======================================================
    // Pairs an inner IDisposable (box/foldout/blocks/area scope) with the outer sheet scope,
    // so the caller's using-block disposes both in order.
    readonly struct CombinedScope : IDisposable
    {
        readonly IDisposable _inner;
        readonly ZUI.SheetScope _outer;
        public CombinedScope(IDisposable inner, ZUI.SheetScope outer) { _inner = inner; _outer = outer; }
        public void Dispose() { _inner?.Dispose(); _outer.Dispose(); }
    }
}
