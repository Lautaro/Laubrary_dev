// ZUISheet.cs
// Runtime entry point for drawing Zheet (ZUIStyleSheetAsset) styles into runtime IMGUI (OnGUI). The
// style definitions live in the Runtime assembly, so a box — its background AND its title/content
// text, styled by the sheet — can be painted in a player without the editor.

using UnityEngine;

public static class ZUISheet
{
    /// <summary>
    /// Draws a box style from a sheet into rect. If <paramref name="title"/>/<paramref name="content"/>
    /// are given, they're drawn using the box's resolved title/content text styles (colour, size,
    /// style from the Zheet) — so those properties are testable at runtime, not just in the editor.
    /// Falls back to the sheet's "Default" box when the name is missing.
    /// </summary>
    public static void DrawBox(ZUIStyleSheetAsset sheet, string styleName, Rect rect,
        string title = null, string content = null)
    {
        if (sheet == null) return;
        ZUIStyleSheetAsset.Active = sheet;              // ambient sheet for palette / 9-slice resolution
        var def = sheet.FindBox(styleName);             // sets def.ownerSheet, falls back to Default
        if (def == null) return;
        def.DrawBackground(rect);
#if UNITY_EDITOR
        ZUIFlash.DrawOverlayIfNeeded(rect, def.name, ZUIFlashKind.Box, sheet);
#endif

        var inner = new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 20);
        if (!string.IsNullOrEmpty(title))
        {
            var ts = TextStyle(def.GetResolvedTitleText(), sheet, TextAnchor.UpperCenter, 18);
            float th = ts.CalcHeight(new GUIContent(title), inner.width);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, th), title, ts);
            inner.y += th + 4f; inner.height -= th + 4f;
        }
        if (!string.IsNullOrEmpty(content))
        {
            var cs = TextStyle(def.GetResolvedContentText(), sheet, TextAnchor.UpperCenter, 14);
            GUI.Label(inner, content, cs);
        }
    }

    /// <summary>
    /// An interactive button from a Zheet: draws the state's visual (9-slice frame if the style has one
    /// via nineSliceNormal, otherwise the same procedural fill/border/corner-radius/shadow/glow renderer
    /// the editor toolkit uses) + styled label, and returns true on click. Hover/press pick the
    /// hover/active visuals either way.
    /// </summary>
    public static bool Button(ZUIStyleSheetAsset sheet, string styleName, Rect rect, string label)
    {
        if (sheet == null) return false;
        ZUIStyleSheetAsset.Active = sheet;
        var def = sheet.FindButton(styleName);
        if (def == null) return false;

        var e = Event.current;
        bool over = rect.Contains(e.mousePosition);
        int id = GUIUtility.GetControlID(FocusType.Passive, rect);
        bool clicked = false;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (over && e.button == 0) { GUIUtility.hotControl = id; e.Use(); }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; if (over) clicked = true; e.Use(); }
                break;
        }
        bool pressed = GUIUtility.hotControl == id && over;
        var state = pressed ? ZUIButtonDrawState.Active : over ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal;

        if (def.UsesNineSlice)
        {
            var frame = sheet.FindNineSlice(def.GetNineSliceId(state));
            if (frame != null) frame.DrawFrame(rect);
        }
        else
        {
            // Same call convention as the editor toolkit's own button drawing (see ZUIButton.cs) — nothing
            // editor-only in DrawVisual/GetResolvedCornerRadius, so this was a wiring gap, not a capability one.
            def.DrawVisual(rect, state, def.GetResolvedCornerRadius());
        }
#if UNITY_EDITOR
        ZUIFlash.DrawOverlayIfNeeded(rect, def.name, ZUIFlashKind.Button, sheet);
#endif

        var ls = TextStyle(def.text, sheet, TextAnchor.MiddleCenter, 15);
        GUI.Label(rect, label, ls);
        return clicked;
    }

    // Build a GUIStyle from a Zheet text def (colour, font style, font size). Size 0 = inherit → the
    // supplied fallback. Font FACE still comes from the skin until font overrides are wired.
    static GUIStyle TextStyle(ZUITextDef td, ZUIStyleSheetAsset sheet, TextAnchor anchor, int fallbackSize)
    {
        var s = new GUIStyle(GUI.skin.label) { alignment = anchor, wordWrap = true, richText = true };
        td?.Apply(s, sheet);
        if (s.fontSize <= 0) s.fontSize = fallbackSize;
        return s;
    }
}
