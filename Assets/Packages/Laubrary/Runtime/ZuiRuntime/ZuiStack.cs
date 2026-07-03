// ZuiStack — the measuring layout cursor, and the reason labels can't clip and rows can't misalign:
// every control MEASURES its text first (CalcHeight/CalcSize) and the stack advances by the real
// height, owning the baseline for everything it draws. Created from any rect (usually a Zui.Panel
// content rect); pair Zui.BeginScrollStack/EndScrollStack for content that may outgrow its box —
// the stack remembers last frame's realized height per key, so a too-tall list simply starts
// scrolling. No Vector2 fields, no height estimates, no clipped rows.

using System.Collections.Generic;
using UnityEngine;

namespace ZuiRuntime
{
    public struct ZuiStack
    {
        Rect _area;
        float _y;
        float _gap;
        internal string ScrollKey;
        internal bool Scrolling;

        public ZuiStack(Rect area, float gapPts = 6f)
        {
            _area = area;
            _y = area.y;
            _gap = UIScale.S(gapPts);
            ScrollKey = null;
            Scrolling = false;
        }

        public float UsedHeight => _y - _area.y;
        public float Remaining => _area.yMax - _y;

        /// <summary>A text block sized to its content — it cannot clip, it wraps and takes the height it needs.</summary>
        public void Label(string text, float pts = 15f, Color? color = null, bool bold = false,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            var style = Zui.TextStyle(pts, color ?? Color.white, align, bold, wrap: true);
            var content = new GUIContent(text);
            float h = style.CalcHeight(content, _area.width);
            GUI.Label(Next(h), content, style);
        }

        public void Header(string text, float pts = 18f, Color? color = null)
            => Label(text, pts, color, bold: true);

        /// <summary>A button sized from its label text — the label cannot be truncated.</summary>
        public bool Button(string label, float pts = 15f)
        {
            var style = Zui.ButtonStyle(pts);
            var size = style.CalcSize(new GUIContent(label));
            float w = Mathf.Min(size.x + UIScale.S(16f), _area.width);
            var r = Next(size.y + UIScale.S(6f));
            return GUI.Button(new Rect(r.x, r.y, w, r.height), label, style);
        }

        /// <summary>A row of equal-width buttons on ONE baseline. Returns the clicked index, or -1.</summary>
        public int Buttons(float pts, params string[] labels)
        {
            var style = Zui.ButtonStyle(pts);
            float h = style.CalcSize(new GUIContent("Ay")).y + UIScale.S(6f);
            var row = Next(h);
            float gap = UIScale.S(6f);
            float w = (row.width - gap * (labels.Length - 1)) / labels.Length;
            int clicked = -1;
            for (int i = 0; i < labels.Length; i++)
                if (GUI.Button(new Rect(row.x + i * (w + gap), row.y, w, h), labels[i], style)) clicked = i;
            return clicked;
        }

        public bool Toggle(string label, bool value, float pts = 15f)
        {
            var style = Zui.ToggleStyle(pts);
            float h = Mathf.Max(style.CalcSize(new GUIContent(label)).y, UIScale.Font(pts) * 1.2f);
            return GUI.Toggle(Next(h), value, label, style);
        }

        /// <summary>Label left, live value right, slider filling the row beneath one shared baseline.</summary>
        public float Slider(string label, float value, float min, float max, float pts = 14f, string format = "0.##")
        {
            var style = Zui.TextStyle(pts, Color.white, TextAnchor.MiddleLeft, false, wrap: false);
            float lh = Zui.LineHeight(pts);
            var row = Next(lh);
            float labelW = style.CalcSize(new GUIContent(label)).x + UIScale.S(8f);
            float valueW = style.CalcSize(new GUIContent(max.ToString(format))).x + UIScale.S(10f);
            GUI.Label(new Rect(row.x, row.y, labelW, lh), label, style);
            GUI.Label(new Rect(row.xMax - valueW, row.y, valueW, lh), value.ToString(format),
                Zui.TextStyle(pts, Color.white, TextAnchor.MiddleRight, false, wrap: false));
            float sliderX = row.x + labelW;
            float sliderW = Mathf.Max(20f, row.width - labelW - valueW - UIScale.S(6f));
            return GUI.HorizontalSlider(new Rect(sliderX, row.y + lh * 0.28f, sliderW, lh * 0.5f), value, min, max);
        }

        public void Space(float pts = 6f) => _y += UIScale.S(pts);

        /// <summary>Reserves a content-sized row and returns it — escape hatch for custom drawing on the stack's baseline.</summary>
        public Rect Next(float height)
        {
            var r = new Rect(_area.x, _y, _area.width, height);
            _y += height + _gap;
            return r;
        }
    }

    public static partial class Zui
    {
        static readonly Dictionary<int, GUIStyle> _buttonStyles = new Dictionary<int, GUIStyle>();
        static readonly Dictionary<int, GUIStyle> _toggleStyles = new Dictionary<int, GUIStyle>();
        static readonly Dictionary<string, Vector2> _scrollPos = new Dictionary<string, Vector2>();
        static readonly Dictionary<string, float> _scrollContentH = new Dictionary<string, float>();

        public static GUIStyle ButtonStyle(float pts)
        {
            int px = UIScale.Font(pts);
            if (!_buttonStyles.TryGetValue(px, out var s))
                _buttonStyles[px] = s = new GUIStyle(GUI.skin.button) { fontSize = px, wordWrap = false };
            return s;
        }

        public static GUIStyle ToggleStyle(float pts)
        {
            int px = UIScale.Font(pts);
            if (!_toggleStyles.TryGetValue(px, out var s))
                _toggleStyles[px] = s = new GUIStyle(GUI.skin.toggle) { fontSize = px };
            return s;
        }

        public static ZuiStack BeginStack(Rect area, float gapPts = 6f) => new ZuiStack(area, gapPts);

        /// <summary>
        /// A stack that scrolls the moment its content outgrows <paramref name="outer"/>. Content height
        /// is remembered from the previous frame per <paramref name="key"/> — no estimates needed.
        /// Always pair with <see cref="EndScrollStack"/>.
        /// </summary>
        public static ZuiStack BeginScrollStack(string key, Rect outer, float gapPts = 6f)
        {
            _scrollContentH.TryGetValue(key, out float contentH);
            bool scrolls = contentH > outer.height;
            Rect inner = outer;
            if (scrolls)
            {
                _scrollPos.TryGetValue(key, out var pos);
                float barW = UIScale.S(16f);
                inner = new Rect(0, 0, outer.width - barW, contentH);
                // Horizontal bar suppressed: a stack only ever grows downward.
                _scrollPos[key] = GUI.BeginScrollView(outer, pos, inner, GUIStyle.none, GUI.skin.verticalScrollbar);
            }
            var stack = new ZuiStack(inner, gapPts);
            stack.ScrollKey = key;
            stack.Scrolling = scrolls;
            return stack;
        }

        public static void EndScrollStack(ref ZuiStack stack)
        {
            if (stack.ScrollKey != null) _scrollContentH[stack.ScrollKey] = stack.UsedHeight;
            if (stack.Scrolling) GUI.EndScrollView();
        }
    }
}
