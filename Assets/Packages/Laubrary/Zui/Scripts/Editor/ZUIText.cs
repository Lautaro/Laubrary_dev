// ZUIText.cs

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ── Box context stack ──────────────────────────────────────────────────────
    // BoxScope pushes/pops so that ZUI.Label() can inherit the box's content style.

    static readonly Stack<ZUIBoxDef> _boxStack = new Stack<ZUIBoxDef>();
    static System.Text.StringBuilder _gradientSB;

    internal static void PushBoxContext(ZUIBoxDef def) => _boxStack.Push(def);

    internal static void PopBoxContext()
    {
        if (_boxStack.Count > 0) _boxStack.Pop();
    }

    static ZUIBoxDef CurrentBoxDef => _boxStack.Count > 0 ? _boxStack.Peek() : null;

    // ── Text drawing with shadow, outline, and gradient ──────────────────────
    // Draw order: shadow → outline → main text (or gradient text).

    internal static void DrawLabel(Rect rect, GUIContent content, GUIStyle style, ZUITextDef textDef)
    {
        DrawLabel(rect, content, style, textDef, ActiveSheet);
    }

    internal static void DrawLabel(Rect rect, GUIContent content, GUIStyle style, ZUITextDef textDef, ZUIStyleSheetAsset sheet)
    {
        if (Event.current.type != EventType.Repaint) return;

        // Shadow pass
        if (textDef != null && textDef.shadow.enabled && textDef.GetResolvedShadowColor(sheet).a > 0f)
        {
            var sr = new Rect(rect.x + textDef.shadow.offset.x, rect.y + textDef.shadow.offset.y,
                              rect.width, rect.height);
            var shadowStyle = new GUIStyle(style);
            Color sc = textDef.GetResolvedShadowColor(sheet);
            shadowStyle.normal.textColor = sc;
            shadowStyle.Draw(sr, content, false, false, false, false);
        }

        // Outline pass (4 or 8 directions behind main text)
        if (textDef != null && textDef.outlineEnabled && textDef.GetResolvedOutlineColor(sheet).a > 0f)
        {
            var outlineStyle = new GUIStyle(style);
            outlineStyle.normal.textColor = textDef.GetResolvedOutlineColor(sheet);

            int w = Mathf.Max(1, textDef.outlineWidth);
            // Cardinal directions (always)
            for (int d = 1; d <= w; d++)
            {
                outlineStyle.Draw(new Rect(rect.x + d, rect.y, rect.width, rect.height), content, false, false, false, false);
                outlineStyle.Draw(new Rect(rect.x - d, rect.y, rect.width, rect.height), content, false, false, false, false);
                outlineStyle.Draw(new Rect(rect.x, rect.y + d, rect.width, rect.height), content, false, false, false, false);
                outlineStyle.Draw(new Rect(rect.x, rect.y - d, rect.width, rect.height), content, false, false, false, false);
            }
            // Diagonal directions (8-pass mode)
            if (textDef.outlinePasses >= 8)
            {
                for (int d = 1; d <= w; d++)
                {
                    outlineStyle.Draw(new Rect(rect.x + d, rect.y + d, rect.width, rect.height), content, false, false, false, false);
                    outlineStyle.Draw(new Rect(rect.x - d, rect.y - d, rect.width, rect.height), content, false, false, false, false);
                    outlineStyle.Draw(new Rect(rect.x + d, rect.y - d, rect.width, rect.height), content, false, false, false, false);
                    outlineStyle.Draw(new Rect(rect.x - d, rect.y + d, rect.width, rect.height), content, false, false, false, false);
                }
            }
        }

        // Main text pass (with optional horizontal gradient)
        if (textDef != null && textDef.gradientEnabled && !string.IsNullOrEmpty(content.text))
        {
            DrawGradientText(rect, content, style, textDef);
        }
        else
        {
            style.Draw(rect, content, false, false, false, false);
        }
    }

    // ── Gradient text via per-character rich text ──────────────────────────────
    // Assigns each character a color lerped between color (A) and colorB (B).
    // Horizontal gradient: character index / (total - 1).

    static void DrawGradientText(Rect rect, GUIContent content, GUIStyle style, ZUITextDef textDef)
    {
        string text = content.text;
        if (text.Length == 0) return;

        Color a = textDef.GetResolvedColor(ActiveSheet);
        Color b = textDef.GetResolvedColorB(ActiveSheet);

        if (_gradientSB == null) _gradientSB = new System.Text.StringBuilder(256);
        _gradientSB.Clear();
        if (_gradientSB.Capacity < text.Length * 24) _gradientSB.Capacity = text.Length * 24;
        var sb = _gradientSB;
        int len = text.Length;
        // Count non-space characters for gradient mapping so spaces don't eat gradient range
        int visibleCount = 0;
        foreach (char c in text) if (c != ' ') visibleCount++;

        int visibleIdx = 0;
        for (int i = 0; i < len; i++)
        {
            char c = text[i];
            if (c == ' ')
            {
                sb.Append(' ');
                continue;
            }
            float t = visibleCount > 1 ? (float)visibleIdx / (visibleCount - 1) : 0f;
            Color col = Color.Lerp(a, b, t);
            sb.Append("<color=#");
            sb.Append(ColorUtility.ToHtmlStringRGBA(col));
            sb.Append('>');
            sb.Append(c);
            sb.Append("</color>");
            visibleIdx++;
        }

        var gradientStyle = new GUIStyle(style);
        gradientStyle.richText = true;
        gradientStyle.Draw(rect, new GUIContent(sb.ToString(), content.image, content.tooltip),
                           false, false, false, false);
    }

    // ── Label API — inherits box content style when called inside a BoxScope ───

    public static void Label(string text, params GUILayoutOption[] options)
    {
        var boxDef = CurrentBoxDef;
        if (boxDef != null)
        {
            DrawLayoutLabel(new GUIContent(text), boxDef.GetResolvedContentText(),
                            boxDef.GetContentStyle(), options);
            return;
        }
        GUILayout.Label(text, options);
    }

    public static GUIStyle GetTextStyle(ZTextStyle style)
    {
        var sheet = ActiveSheet;
        if (sheet != null)
        {
            var def = sheet.FindText(style.ToString());
            if (def != null) return def.GetStyle(sheet);
        }
        return TextStyleRegistry.Get(style);
    }

    public static void Label(string text, ZTextStyle style, params GUILayoutOption[] options)
    {
        var sheet = ActiveSheet;
        if (sheet != null)
        {
            var def = sheet.FindText(style.ToString());
            if (def != null)
            {
                DrawLayoutLabel(new GUIContent(text), def.text, def.GetStyle(sheet), options, style, def);
                return;
            }
        }
        DrawLayoutLabel(new GUIContent(text), null, TextStyleRegistry.Get(style), options, style, null);
    }

    public static void Label(string text, ZUITextStyleDef def, params GUILayoutOption[] options)
        => DrawLayoutLabel(new GUIContent(text), def.text, def.GetStyle(ActiveSheet), options);

    // ── Box-aware text — pairs ────────────────────────────────────────────────
    // ZUI.TitleText / ZUI.Text let a consumer write box-contextual text without
    // knowing the current box's skin. Inside a ZUI.Box scope they use that
    // box's titleText / contentText. Outside a box they fall back to the
    // sheet's ZTextStyle.Title / ZTextStyle.Default entries. Both paths route
    // through the flash hook so the Style Editor's flash works against either
    // the box name (when the text inherits the box's own title/content field)
    // or the referenced text style name (when titleTextStyleId / contentTextStyleId
    // points at a ZUITextStyleDef on the sheet).

    /// <summary>
    /// Draws a title-styled label. Inside a ZUI.Box, uses that box's resolved
    /// titleText. Outside a box, falls back to ZTextStyle.Title.
    /// </summary>
    public static void TitleText(string text, params GUILayoutOption[] options)
    {
        var boxDef = CurrentBoxDef;
        if (boxDef != null)
        {
            var content = new GUIContent(text);
            var style = boxDef.GetTitleStyle();
            var rect = GUILayoutUtility.GetRect(content, style, options);
            if (CheckDebugContextClick(rect)) return;
            DrawLabel(rect, content, style, boxDef.GetResolvedTitleText());
            // Flash on the box name so flashing the Box def lights up its title text too.
            DrawFlashOverlayIfNeeded(rect, boxDef.name, 0, FlashDefType.Box);
            // Flash on the referenced text style if the box points at one.
            if (!string.IsNullOrEmpty(boxDef.titleTextStyleId))
                DrawFlashOverlayIfNeeded(rect, boxDef.titleTextStyleId, 0, FlashDefType.Text);
            return;
        }
        Label(text, ZTextStyle.Title, options);
    }

    /// <summary>
    /// Draws a content-styled label. Inside a ZUI.Box, uses that box's resolved
    /// contentText. Outside a box, falls back to ZTextStyle.Default.
    /// </summary>
    public static void Text(string text, params GUILayoutOption[] options)
    {
        var boxDef = CurrentBoxDef;
        if (boxDef != null)
        {
            var content = new GUIContent(text);
            var style = boxDef.GetContentStyle();
            var rect = GUILayoutUtility.GetRect(content, style, options);
            if (CheckDebugContextClick(rect)) return;
            DrawLabel(rect, content, style, boxDef.GetResolvedContentText());
            DrawFlashOverlayIfNeeded(rect, boxDef.name, 0, FlashDefType.Box);
            if (!string.IsNullOrEmpty(boxDef.contentTextStyleId))
                DrawFlashOverlayIfNeeded(rect, boxDef.contentTextStyleId, 0, FlashDefType.Text);
            return;
        }
        Label(text, ZTextStyle.Default, options);
    }

    public static void Label(Rect rect, string text)
    {
        var boxDef = CurrentBoxDef;
        DrawLabel(rect, new GUIContent(text),
                  boxDef?.GetContentStyle() ?? EditorStyles.label,
                  boxDef?.GetResolvedContentText());
    }

    public static void Label(Rect rect, string text, ZTextStyle style)
    {
        var sheet = ActiveSheet;
        ZUITextDef textDef = null;
        GUIStyle guiStyle;
        ZUITextStyleDef styleDef = null;
        if (sheet != null)
        {
            styleDef = sheet.FindText(style.ToString());
            if (styleDef != null) { textDef = styleDef.text; guiStyle = styleDef.GetStyle(sheet); }
            else guiStyle = TextStyleRegistry.Get(style);
        }
        else guiStyle = TextStyleRegistry.Get(style);
        if (CheckDebugContextClick(rect)) { CollectTextDebugInfo(styleDef, style, textDef, rect); return; }
        DrawLabel(rect, new GUIContent(text), guiStyle, textDef);
        DrawFlashOverlayIfNeeded(rect, styleDef != null ? styleDef.name : style.ToString(), 0, FlashDefType.Text);
    }

    public static void Label(Rect rect, string text, ZUITextStyleDef def)
    {
        DrawLabel(rect, new GUIContent(text), def.GetStyle(ActiveSheet), def.text);
        DrawFlashOverlayIfNeeded(rect, def.name, 0, FlashDefType.Text);
    }

    static void DrawLayoutLabel(GUIContent content, ZUITextDef textDef, GUIStyle style,
                                GUILayoutOption[] options,
                                ZTextStyle debugStyle = ZTextStyle.Default, ZUITextStyleDef debugDef = null)
    {
        var rect = GUILayoutUtility.GetRect(content, style, options);
        if (CheckDebugContextClick(rect))
        {
            if (debugDef != null) CollectTextDebugInfo(debugDef, debugStyle, rect);
            else CollectTextDebugInfo(textDef, debugStyle, rect);
            return;
        }
        DrawLabel(rect, content, style, textDef);

        // Flash overlay — resolve the style name so the Style Editor's flash targets this label.
        string styleName = debugDef != null ? debugDef.name : debugStyle.ToString();
        DrawFlashOverlayIfNeeded(rect, styleName, 0, FlashDefType.Text);
    }

    // ── ZTextStyle enum ────────────────────────────────────────────────────────

    public enum ZTextStyle
    {
        Default,
        Title,
        Header,
        Subheader,
        SectionHeader,
        Small,
        Subtle,
        Accent,
    }

    // ── TextStyleRegistry ─────────────────────────────────────────────────────
    // Fallback when no sheet is loaded. Lazy init.

    static class TextStyleRegistry
    {
        static Dictionary<ZTextStyle, GUIStyle> _styles;

        public static GUIStyle Get(ZTextStyle key)
        {
            if (_styles == null) Build();
            if (_styles.TryGetValue(key, out var s)) return s;
            return _styles[ZTextStyle.Default];
        }

        static void Build()
        {
            _styles = new Dictionary<ZTextStyle, GUIStyle>
            {
                { ZTextStyle.Default,   Make(new Color(.88f, .88f, .88f, 1f), 0,  FontStyle.Normal) },
                { ZTextStyle.Title,     MakeTitle()                                                  },
                { ZTextStyle.Header,    Make(new Color(.95f, .95f, .95f, 1f), 14, FontStyle.Bold)   },
                { ZTextStyle.Subheader, Make(new Color(.90f, .90f, .90f, 1f), 0,  FontStyle.Bold)   },
                { ZTextStyle.SectionHeader, Make(new Color(.85f, .85f, .85f, 1f), 12, FontStyle.Bold) },
                { ZTextStyle.Small,     Make(new Color(.70f, .70f, .70f, 1f), 9,  FontStyle.Normal) },
                { ZTextStyle.Subtle,    Make(new Color(.55f, .55f, .55f, 1f), 0,  FontStyle.Normal) },
                { ZTextStyle.Accent,    Make(new Color(.70f, .88f, 1f,   1f), 0,  FontStyle.Normal) },
            };
        }

        static GUIStyle Make(Color color, int fontSize, FontStyle fontStyle)
        {
            var s = new GUIStyle(EditorStyles.label) { wordWrap = true };
            s.normal.textColor = color;
            s.fontStyle = fontStyle;
            if (fontSize > 0) s.fontSize = fontSize;
            return s;
        }

        static GUIStyle MakeTitle()
        {
            var s = new GUIStyle(EditorStyles.label)
            {
                wordWrap  = false,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize  = 18,
            };
            s.normal.textColor = new Color(.98f, .98f, .98f, 1f);
            return s;
        }
    }
}
