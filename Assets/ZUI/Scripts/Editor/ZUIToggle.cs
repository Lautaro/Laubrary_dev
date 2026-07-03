// ZUIToggle.cs
// A button-style toggle control. Fully styleable via ZUIButtonDef.
// Visual states: Off = Normal, Hover = Hover, On = Active.
// Returns the new value (toggled when clicked).
//
// Dual-icon toggles: pass offIcon + onIcon to show a different texture per state.
// Any combination of text / single icon / dual icon is supported, for both button and toggle.

using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ===== Toggle API — string style name =====================================

    public static bool Toggle(bool value, string label, string style = Style.Default, params GUILayoutOption[] options)
    {
        var sheet = ZUI.ActiveSheet;
        if (sheet != null)
        {
            var def  = sheet.FindButton(style);
            var rect = GUILayoutUtility.GetRect(MakeContent(label, def), def.GetLabelStyle(), options);
            return DrawManualToggle(rect, new GUIContent(label), value, def);
        }
        return DrawFallbackToggle(value, label, style, options);
    }

    public static bool Toggle(bool value, GUIContent content, string style = Style.Default, params GUILayoutOption[] options)
    {
        var sheet = ZUI.ActiveSheet;
        if (sheet != null)
        {
            var def      = sheet.FindButton(style);
            bool icoOnly = IsIconOnly(content, def, null);
            var rect     = GUILayoutUtility.GetRect(content, def.GetLabelStyle(iconOnly: icoOnly), options);
            return DrawManualToggle(rect, content, value, def);
        }
        return DrawFallbackToggle(value, content.text, style, options);
    }

    public static bool Toggle(bool value, GUIContent content, string style, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    {
        var sheet = ZUI.ActiveSheet;
        if (sheet != null)
        {
            var def      = sheet.FindButton(style);
            bool icoOnly = IsIconOnly(content, def, null);
            var rect     = GUILayoutUtility.GetRect(content, def.GetLabelStyle(iconOnly: icoOnly), options);
            return DrawManualToggle(rect, content, value, def, cornerMask: cornerMask);
        }
        return DrawFallbackToggle(value, content.text, style, options);
    }

    public static bool Toggle(bool value, string label, string style, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    {
        var sheet = ZUI.ActiveSheet;
        if (sheet != null)
        {
            var def  = sheet.FindButton(style);
            var rect = GUILayoutUtility.GetRect(MakeContent(label, def), def.GetLabelStyle(), options);
            return DrawManualToggle(rect, new GUIContent(label), value, def, cornerMask: cornerMask);
        }
        return DrawFallbackToggle(value, label, style, options);
    }

    // ===== Toggle API — with semantic tint ====================================
    // Tint swaps the bg color for this one draw, same semantics as Button's tint.
    // Differs from the `onColor` overloads: tint applies regardless of state,
    // onColor applies only when the toggle is on (Active state).

    public static bool Toggle(bool value, string label, string style, string tint, params GUILayoutOption[] options)
    {
        if (!string.IsNullOrEmpty(tint)) OverrideButtonBgPaletteRef = tint;
        return Toggle(value, label, style, options);
    }

    public static bool Toggle(bool value, string label, string style, string tint, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    {
        if (!string.IsNullOrEmpty(tint)) OverrideButtonBgPaletteRef = tint;
        return Toggle(value, label, style, cornerMask, options);
    }

    public static bool Toggle(bool value, GUIContent content, string style, string tint, params GUILayoutOption[] options)
    {
        if (!string.IsNullOrEmpty(tint)) OverrideButtonBgPaletteRef = tint;
        return Toggle(value, content, style, options);
    }

    public static bool Toggle(bool value, GUIContent content, string style, string tint, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    {
        if (!string.IsNullOrEmpty(tint)) OverrideButtonBgPaletteRef = tint;
        return Toggle(value, content, style, cornerMask, options);
    }

    // Dual-icon toggle: shows offIcon when value=false, onIcon when value=true.
    public static bool Toggle(bool value, string label, Texture offIcon, Texture onIcon,
                              string style = Style.Default, ZUICornerMask cornerMask = ZUICornerMask.None,
                              params GUILayoutOption[] options)
    {
        var sheet = ZUI.ActiveSheet;
        if (sheet != null)
        {
            var def      = sheet.FindButton(style);
            var content  = MakeIconContent(label, offIcon); // layout with offIcon for consistent sizing
            bool icoOnly = IsIconOnly(content, def, offIcon as Texture2D);
            var rect     = GUILayoutUtility.GetRect(content, def.GetLabelStyle(iconOnly: icoOnly), options);
            return DrawManualToggle(rect, new GUIContent(label), value, def,
                                   offIcon: offIcon, onIcon: onIcon, cornerMask: cornerMask);
        }
        return DrawFallbackToggle(value, label, style, options);
    }

    public static bool Toggle(Rect rect, bool value, string label, string style = Style.Default,
                              Color? onColor = null, ZUICornerMask cornerMask = ZUICornerMask.None)
    {
        var sheet = ZUI.ActiveSheet;
        if (sheet != null)
            return DrawManualToggle(rect, new GUIContent(label), value, sheet.FindButton(style), onColor, cornerMask);
        return DrawFallbackToggle(rect, value, label, style, onColor);
    }

    public static bool Toggle(Rect rect, bool value, GUIContent content, string style = Style.Default,
                              Color? onColor = null, ZUICornerMask cornerMask = ZUICornerMask.None)
    {
        var sheet = ZUI.ActiveSheet;
        if (sheet != null)
            return DrawManualToggle(rect, content, value, sheet.FindButton(style), onColor, cornerMask);
        return DrawFallbackToggle(rect, value, content.text, style, onColor);
    }

    // ===== Toggle API — ZUIButtonDef (named style def) ========================

    public static bool Toggle(bool value, string label, ZUIButtonDef def, params GUILayoutOption[] options)
    {
        var rect = GUILayoutUtility.GetRect(MakeContent(label, def), def.GetLabelStyle(), options);
        return DrawManualToggle(rect, new GUIContent(label), value, def);
    }

    public static bool Toggle(bool value, string label, ZUIButtonDef def, ZUICornerMask cornerMask, params GUILayoutOption[] options)
    {
        var rect = GUILayoutUtility.GetRect(MakeContent(label, def), def.GetLabelStyle(), options);
        return DrawManualToggle(rect, new GUIContent(label), value, def, cornerMask: cornerMask);
    }

    public static bool Toggle(bool value, GUIContent content, ZUIButtonDef def, params GUILayoutOption[] options)
    {
        bool icoOnly = IsIconOnly(content, def, null);
        var rect = GUILayoutUtility.GetRect(content, def.GetLabelStyle(iconOnly: icoOnly), options);
        return DrawManualToggle(rect, content, value, def);
    }

    // Dual-icon toggle with ZUIButtonDef.
    public static bool Toggle(bool value, string label, Texture offIcon, Texture onIcon,
                              ZUIButtonDef def, ZUICornerMask cornerMask = ZUICornerMask.None,
                              params GUILayoutOption[] options)
    {
        var content  = MakeIconContent(label, offIcon);
        bool icoOnly = IsIconOnly(content, def, offIcon as Texture2D);
        var rect     = GUILayoutUtility.GetRect(content, def.GetLabelStyle(iconOnly: icoOnly), options);
        return DrawManualToggle(rect, new GUIContent(label), value, def,
                               offIcon: offIcon, onIcon: onIcon, cornerMask: cornerMask);
    }

    public static bool Toggle(Rect rect, bool value, string label, ZUIButtonDef def, ZUICornerMask cornerMask = ZUICornerMask.None)
        => DrawManualToggle(rect, new GUIContent(label), value, def, cornerMask: cornerMask);

    public static bool Toggle(Rect rect, bool value, GUIContent content, ZUIButtonDef def, ZUICornerMask cornerMask = ZUICornerMask.None)
        => DrawManualToggle(rect, content, value, def, cornerMask: cornerMask);

    // ── Content helper ────────────────────────────────────────────────────────

    static GUIContent MakeIconContent(string label, Texture icon)
        => icon != null ? new GUIContent(label, icon as Texture2D) : new GUIContent(label);

    // ── Core draw ─────────────────────────────────────────────────────────────
    // On  = Active visual state
    // Off = Normal visual state
    // Hover is intentionally not shown so the On/Off state reads clearly.
    // Clicking toggles value and previews the toggled-to state while held.
    //
    // offIcon / onIcon: when provided, the icon switches per draw state.
    //   If only one texture path is needed, pass the same icon via GUIContent.image instead.
    // onColor: overrides the Active-state background with a flat solid color.

    static bool DrawManualToggle(Rect rect, GUIContent content, bool value, ZUIButtonDef def,
                                 Color? onColor = null, ZUICornerMask cornerMask = ZUICornerMask.None,
                                 Texture offIcon = null, Texture onIcon = null)
    {
        // ── Style debug ───────────────────────────────────────────────────────
        if (CheckDebugContextClick(rect))
        {
            CollectButtonDebugInfo(def, def.name, rect);
            return value;
        }

        // Resolve icon for the current toggle state (true = onIcon, false = offIcon).
        // Falls back to content.image when no dual-icon pair is provided.
        Texture ResolveIcon(bool isOn)
        {
            if (offIcon != null || onIcon != null)
                return isOn ? onIcon : offIcon;
            return content.image;
        }

        bool iconOnly = string.IsNullOrEmpty(content.text) && (offIcon != null || onIcon != null || content.image != null);

        if (!GUI.enabled)
        {
            if (Event.current.type == EventType.Repaint)
            {
                int r    = SimulateLegacyCorners ? 0 : def.GetResolvedCornerRadius();
                var prev = GUI.color;
                GUI.color = new Color(prev.r, prev.g, prev.b, prev.a * 0.4f);
                var drawState = value ? ZUIButtonDrawState.ToggleOn : ZUIButtonDrawState.Normal;
                DrawToggleVisual(rect, def, drawState, r, value ? onColor : null, cornerMask);
                var ico = ResolveIcon(value) as Texture2D;
                DrawButtonLabel(rect, content, def.GetLabelStyle(drawState, iconOnly), ico, ZIconPlacement.LeftOfLabel, def, def.GetText(drawState));
                GUI.color = prev;
            }
            return value;
        }

        int id = GUIUtility.GetControlID(FocusType.Passive, rect);
        var ev = Event.current;
        bool isHover  = rect.Contains(ev.mousePosition);
        bool isActive = GUIUtility.hotControl == id;
        bool clicked  = false;

        switch (ev.type)
        {
            case EventType.MouseDown:
                if (isHover && ev.button == 0)
                {
                    GUIUtility.hotControl = id;
                    ev.Use();
                }
                break;

            case EventType.MouseUp:
                if (isActive)
                {
                    GUIUtility.hotControl = 0;
                    ev.Use();
                    if (isHover) clicked = true;
                }
                break;
        }

        // Drive hover/click tween state for off-state hover effect
        ZUI.TweenNotifyHover(id, isHover && !value, def);
        ZUI.TweenNotifyActive(id, isActive, def);

        if (ev.type == EventType.Repaint)
        {
            int r = SimulateLegacyCorners ? 0 : def.GetResolvedCornerRadius();
            ZUIButtonDrawState drawState;
            bool showOnColor;
            bool displayOn; // which icon / text def to use for this repaint frame
            if (isActive)
            {
                // While pressed, preview the toggled-to state.
                drawState    = value ? ZUIButtonDrawState.Normal : ZUIButtonDrawState.ToggleOn;
                showOnColor  = !value;
                displayOn    = !value;
            }
            else if (!value && isHover)
            {
                drawState   = ZUIButtonDrawState.Hover;
                showOnColor = false;
                displayOn   = false;
            }
            else
            {
                drawState   = value ? ZUIButtonDrawState.ToggleOn : ZUIButtonDrawState.Normal;
                showOnColor = value;
                displayOn   = value;
            }

            // When toggle is off, use animated hover/click like a normal button
            if (!value && (def.hoverAnimEnabled || def.clickAnimEnabled))
            {
                float hoverT = ZUI.TweenGetHoverT(id);
                float clickT = ZUI.TweenGetClickT(id);

                if (clickT > 0f && def.clickAnimEnabled)
                    ZUI.DrawAnimatedVisual(rect, def, ZUIButtonDrawState.Hover, ZUIButtonDrawState.ToggleOn, clickT, r, cornerMask);
                else if (hoverT > 0f && def.hoverAnimEnabled)
                    ZUI.DrawAnimatedVisual(rect, def, ZUIButtonDrawState.Normal, ZUIButtonDrawState.Hover, hoverT, r, cornerMask);
                else
                    DrawToggleVisual(rect, def, drawState, r, showOnColor ? onColor : null, cornerMask);
            }
            else
            {
                DrawToggleVisual(rect, def, drawState, r, showOnColor ? onColor : null, cornerMask);
            }

            ZUI.DrawFlashOverlayIfNeeded(rect, def.name, r, ZUI.FlashDefType.Button);
            if (def.HasBoxStyle)
                ZUI.DrawFlashOverlayIfNeeded(rect, def.boxStyle, r, ZUI.FlashDefType.Box);
            var drawIco = ResolveIcon(displayOn) as Texture2D;
            DrawButtonLabel(rect, content, def.GetLabelStyle(drawState, iconOnly), drawIco, ZIconPlacement.LeftOfLabel, def, def.GetText(drawState));
        }

        return clicked ? !value : value;
    }

    // Draws the button background. When onColorOverride is set, draws a flat solid rect instead
    // of the def's active gradient — used to tint individual toggles without a dedicated style.
    static void DrawToggleVisual(Rect rect, ZUIButtonDef def, ZUIButtonDrawState drawState, int cornerRadius,
                                 Color? onColorOverride, ZUICornerMask cornerMask = ZUICornerMask.None)
    {
        if (onColorOverride.HasValue && drawState == ZUIButtonDrawState.ToggleOn)
        {
            float r = Mathf.Min(cornerRadius, rect.width * 0.5f, rect.height * 0.5f);
            Vector4 crVec;
            if (cornerMask != ZUICornerMask.None && cornerRadius > 0)
            {
                var (tl, tr, bl, br) = ZUI.ResolveCornerMask(def, cornerMask);
                crVec = ZUI.CornerMaskToVector(tl, tr, bl, br, r);
            }
            else
            {
                crVec = def.GetCornerVector(r);
            }
#if UNITY_2021_2_OR_NEWER
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, onColorOverride.Value, Vector4.zero, crVec);
#else
            EditorGUI.DrawRect(rect, onColorOverride.Value);
#endif
        }
        else if (cornerMask != ZUICornerMask.None && cornerRadius > 0)
        {
            var (tl, tr, bl, br) = ZUI.ResolveCornerMask(def, cornerMask);
            def.DrawVisualWithCorners(rect, drawState, cornerRadius, tl, tr, bl, br);
        }
        else
        {
            def.DrawVisual(rect, drawState, cornerRadius);
        }
    }

    // ── Fallback (no sheet) ───────────────────────────────────────────────────

    static bool DrawFallbackToggle(bool value, string label, string style, GUILayoutOption[] options)
    {
        var s = ToggleStyleRegistry.Get(style, value);
        if (GUILayout.Button(label, s, options)) return !value;
        return value;
    }

    static bool DrawFallbackToggle(Rect rect, bool value, string label, string style, Color? onColor = null)
    {
        var s = ToggleStyleRegistry.Get(style, value);
        if (GUI.Button(rect, label, s)) return !value;
        return value;
    }

    // ===== ToggleStyleRegistry ================================================
    // Fallback when no sheet is loaded. Maps style names to button fallback colors.

    static class ToggleStyleRegistry
    {
        public static GUIStyle Get(string style, bool value)
        {
            var buttonStyle = style switch
            {
                Style.RichButton          => value ? Style.Active   : Style.RichButton,
                Style.Confirm             => value ? Style.Confirm  : Style.Default,
                Style.Danger              => value ? Style.Danger   : Style.Default,
                Style.Active              => value ? Style.Active   : Style.Default,
                Style.ZoundBtnFlatToggle  => value ? Style.Active   : Style.RichButton,
                _                         => value ? Style.Active   : Style.Default,
            };
            return ButtonStyleRegistry.Get(buttonStyle);
        }
    }
}
