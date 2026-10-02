// Procedural keyboard-and-mouse binding legend. It deliberately owns no textures: a normal desktop
// layout is drawn from key rectangles, while bound keys become vivid and carry their action caption.

using System;
using UnityEngine;

namespace ZuiRuntime
{
    [Serializable]
    public struct KeyboardKeyPrompt
    {
        public string Key;
        public string Caption;
        public KeyboardKeyPrompt(string key, string caption) { Key = key; Caption = caption; }
    }

    [Serializable]
    public struct KeyboardMap
    {
        public KeyboardKeyPrompt[] Keys;
        public string MouseLeft, MouseMiddle, MouseRight, MouseMove, MouseScroll;
    }

    /// <summary>Draws a conventional keyboard and mouse with every supplied binding in its physical place.</summary>
    public static class ZuiKeyboard
    {
        readonly struct KeyDef
        {
            public readonly string Path, Glyph;
            public readonly float Width;
            public KeyDef(string path, string glyph, float width = 1f) { Path = path; Glyph = glyph; Width = width; }
        }

        static readonly KeyDef[][] Rows =
        {
            new[] { K("escape","Esc"), K("digit1","1"), K("digit2","2"), K("digit3","3"), K("digit4","4"), K("digit5","5"), K("digit6","6"), K("digit7","7"), K("digit8","8"), K("digit9","9"), K("digit0","0"), K("minus","-"), K("equals","="), K("backspace","Back",1.6f) },
            new[] { K("tab","Tab",1.35f), K("q","Q"), K("w","W"), K("e","E"), K("r","R"), K("t","T"), K("y","Y"), K("u","U"), K("i","I"), K("o","O"), K("p","P"), K("leftBracket","["), K("rightBracket","]"), K("backslash","\\",1.25f) },
            new[] { K("capsLock","Caps",1.6f), K("a","A"), K("s","S"), K("d","D"), K("f","F"), K("g","G"), K("h","H"), K("j","J"), K("k","K"), K("l","L"), K("semicolon",";"), K("quote","'"), K("enter","Enter",1.8f) },
            new[] { K("leftShift","Shift",2.05f), K("z","Z"), K("x","X"), K("c","C"), K("v","V"), K("b","B"), K("n","N"), K("m","M"), K("comma",","), K("period","."), K("slash","/"), K("rightShift","Shift",2.15f) },
            new[] { K("leftCtrl","Ctrl",1.35f), K("leftAlt","Alt",1.35f), K("space","Space",6.2f), K("rightAlt","Alt",1.35f), K("rightCtrl","Ctrl",1.35f), K("leftArrow","←"), K("downArrow","↓"), K("upArrow","↑"), K("rightArrow","→") }
        };

        static KeyDef K(string path, string glyph, float width = 1f) => new KeyDef(path, glyph, width);

        public static void DrawFullMap(Rect area, KeyboardMap map, GUIStyle glyphStyle, GUIStyle captionStyle)
        {
            float gap = Mathf.Max(2f, area.width * 0.004f);
            float mouseWidth = Mathf.Clamp(area.width * 0.19f, 110f, 220f);
            Rect keyboard = new Rect(area.x, area.y, Mathf.Max(100f, area.width - mouseWidth - gap * 4f), area.height);
            Rect mouse = new Rect(keyboard.xMax + gap * 4f, area.y + area.height * 0.10f, mouseWidth, area.height * 0.72f);
            float rowHeight = (keyboard.height - gap * (Rows.Length - 1)) / Rows.Length;

            for (int row = 0; row < Rows.Length; row++)
            {
                var defs = Rows[row];
                float units = 0f;
                for (int i = 0; i < defs.Length; i++) units += defs[i].Width;
                float unit = (keyboard.width - gap * (defs.Length - 1)) / units;
                float x = keyboard.x;
                for (int i = 0; i < defs.Length; i++)
                {
                    float width = unit * defs[i].Width;
                    DrawKey(new Rect(x, keyboard.y + row * (rowHeight + gap), width, rowHeight), defs[i], Caption(map.Keys, defs[i].Path), glyphStyle, captionStyle);
                    x += width + gap;
                }
            }

            DrawMouse(mouse, map, glyphStyle, captionStyle);
        }

        static void DrawKey(Rect rect, KeyDef key, string caption, GUIStyle glyphStyle, GUIStyle captionStyle)
        {
            bool active = !string.IsNullOrWhiteSpace(caption);
            Color color = active ? new Color(0.28f, 0.48f, 0.72f, 1f) : new Color(0.25f, 0.27f, 0.31f, 0.35f);
            Zui.FillRect(new Rect(rect.x, rect.y + 2f, rect.width, rect.height), new Color(0f, 0f, 0f, 0.28f));
            Zui.FillRect(rect, color);

            float glyphHeight = active ? rect.height * 0.43f : rect.height;
            var glyphRect = new Rect(rect.x + 2f, rect.y, rect.width - 4f, glyphHeight);
            var oldAlign = glyphStyle.alignment;
            var oldColor = glyphStyle.normal.textColor;
            glyphStyle.alignment = TextAnchor.MiddleCenter;
            glyphStyle.normal.textColor = active ? Color.white : new Color(1f, 1f, 1f, 0.48f);
            GUI.Label(glyphRect, new GUIContent(key.Glyph, active ? caption : null), glyphStyle);
            glyphStyle.alignment = oldAlign;
            glyphStyle.normal.textColor = oldColor;

            if (!active) return;
            var captionRect = new Rect(rect.x + 2f, rect.y + glyphHeight - 1f, rect.width - 4f, rect.height - glyphHeight);
            DrawFittedKeyCaption(captionRect, caption, captionStyle);
        }

        static void DrawMouse(Rect rect, KeyboardMap map, GUIStyle glyphStyle, GUIStyle captionStyle)
        {
            float top = rect.height * 0.43f;
            float third = rect.width / 3f;
            var left = new Rect(rect.x, rect.y, third, top);
            var middle = new Rect(rect.x + third, rect.y, third, top);
            var right = new Rect(rect.x + third * 2f, rect.y, third, top);
            MouseButton(left, "LMB", map.MouseLeft, glyphStyle, captionStyle);
            MouseButton(middle, "MMB", Join(map.MouseMiddle, map.MouseScroll), glyphStyle, captionStyle);
            MouseButton(right, "RMB", map.MouseRight, glyphStyle, captionStyle);

            bool active = !string.IsNullOrWhiteSpace(map.MouseMove);
            Rect body = new Rect(rect.x, rect.y + top, rect.width, rect.height - top);
            Zui.FillRect(body, active ? new Color(0.28f, 0.48f, 0.72f, 1f) : new Color(0.25f, 0.27f, 0.31f, 0.35f));
            DrawCaption(body, active ? map.MouseMove : "Mouse", captionStyle, TextAnchor.MiddleCenter);
        }

        static void MouseButton(Rect rect, string glyph, string caption, GUIStyle glyphStyle, GUIStyle captionStyle)
        {
            bool active = !string.IsNullOrWhiteSpace(caption);
            Zui.FillRect(rect, active ? new Color(0.28f, 0.48f, 0.72f, 1f) : new Color(0.25f, 0.27f, 0.31f, 0.35f));
            var oldAlign = glyphStyle.alignment;
            glyphStyle.alignment = TextAnchor.UpperCenter;
            GUI.Label(rect, new GUIContent(glyph, active ? caption : null), glyphStyle);
            glyphStyle.alignment = oldAlign;
            if (active) DrawCaption(new Rect(rect.x + 2f, rect.y + rect.height * 0.35f, rect.width - 4f, rect.height * 0.65f), caption, captionStyle, TextAnchor.MiddleCenter);
        }

        static void DrawCaption(Rect rect, string caption, GUIStyle style, TextAnchor alignment)
        {
            var oldAlign = style.alignment;
            bool oldWrap = style.wordWrap;
            style.alignment = alignment;
            style.wordWrap = true;
            GUI.Label(rect, caption, style);
            style.alignment = oldAlign;
            style.wordWrap = oldWrap;
        }

        // Keycaps can be much narrower than their player-facing captions. Prefer the largest font that keeps
        // whole words intact and fits all wrapped lines inside the reserved caption area. If even the minimum
        // readable size cannot fit, draw one cleanly clipped line; the GUIContent tooltip and the guide's detail
        // list still carry the complete caption. Every shared-style property touched here is restored afterwards.
        static void DrawFittedKeyCaption(Rect rect, string caption, GUIStyle style)
        {
            var oldAlign = style.alignment;
            var oldClipping = style.clipping;
            bool oldWrap = style.wordWrap;
            int oldFontSize = style.fontSize;

            style.alignment = TextAnchor.MiddleCenter;
            style.clipping = TextClipping.Clip;
            style.wordWrap = true;

            int preferred = oldFontSize > 0 ? oldFontSize : 10;
            int minimum = Mathf.Min(preferred, 7);
            var content = new GUIContent(caption, caption);
            bool fitted = false;
            for (int size = preferred; size >= minimum; size--)
            {
                style.fontSize = size;
                if (LongestWordWidth(caption, style) <= rect.width && style.CalcHeight(content, rect.width) <= rect.height)
                {
                    fitted = true;
                    break;
                }
            }

            if (!fitted)
            {
                style.fontSize = minimum;
                style.wordWrap = false;
            }

            GUI.Label(rect, content, style);

            style.alignment = oldAlign;
            style.clipping = oldClipping;
            style.wordWrap = oldWrap;
            style.fontSize = oldFontSize;
        }

        static float LongestWordWidth(string text, GUIStyle style)
        {
            float widest = 0f;
            var words = text.Split(new[] { ' ', '/', '-', '\u2013', '\u2014' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
                widest = Mathf.Max(widest, style.CalcSize(new GUIContent(words[i])).x);
            return widest;
        }

        static string Caption(KeyboardKeyPrompt[] prompts, string key)
        {
            if (prompts == null) return null;
            for (int i = 0; i < prompts.Length; i++)
                if (string.Equals(prompts[i].Key, key, StringComparison.OrdinalIgnoreCase)) return prompts[i].Caption;
            return null;
        }

        static string Join(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a)) return b;
            if (string.IsNullOrWhiteSpace(b) || a.Contains(b)) return a;
            return a + " / " + b;
        }
    }
}
