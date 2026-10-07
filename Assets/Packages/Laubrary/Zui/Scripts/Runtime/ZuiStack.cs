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
            var rect = Next(h);
            GUI.Label(rect, content, style);
        }

        public void Header(string text, float pts = 18f, Color? color = null)
            => Label(text, pts, color, bold: true);

        /// <summary>Measures and draws a label using its authored runtime semantic role.</summary>
        public void Label(string text, ZuiRuntimeSkin skin, ZuiTextRole role)
        {
            var style = Zui.TextStyle(skin, role);
            var content = new GUIContent(text);
            var rect = Next(style.CalcHeight(content, _area.width));
            GUI.Label(rect, content, style);
        }

        /// <summary>
        /// Draw text into an EXPLICIT rect on the stack's baseline — the escape hatch that does NOT
        /// auto-size, so text can clip if the rect is too small.
        /// </summary>
        public void LabelIn(Rect rect, string text, float pts = 15f, Color? color = null, bool wrap = true,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            var style = Zui.TextStyle(pts, color ?? Color.white, align, false, wrap);
            var content = new GUIContent(text);
            GUI.Label(rect, content, style);
        }

        /// <summary>A button sized from its label text — the label cannot be truncated.</summary>
        public bool Button(string label, float pts = 15f)
        {
            var style = Zui.ButtonStyle(pts);
            var size = style.CalcSize(new GUIContent(label));
            float w = Mathf.Min(size.x + UIScale.S(16f), _area.width);
            var r = Next(size.y + UIScale.S(6f));
            var rect = new Rect(r.x, r.y, w, r.height);
            bool clicked = GUI.Button(rect, label, style);
            return clicked;
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
            {
                var rect = new Rect(row.x + i * (w + gap), row.y, w, h);
                if (GUI.Button(rect, labels[i], style)) clicked = i;
            }
            return clicked;
        }

        /// <summary>A choice between a few options on ONE row, every option visible and the chosen one lit. Returns the
        /// selection, changed when an option is clicked.</summary>
        public int Segmented(int selected, float pts, params string[] labels)
        {
            var style = Zui.ButtonStyle(pts);
            float h = style.CalcSize(new GUIContent("Ay")).y + UIScale.S(6f);
            var row = Next(h);
            float gap = UIScale.S(2f);
            float w = (row.width - gap * (labels.Length - 1)) / labels.Length;
            int result = selected;
            for (int i = 0; i < labels.Length; i++)
            {
                var rect = new Rect(row.x + i * (w + gap), row.y, w, h);
                if (i == selected)
                {
                    // The chosen option is a solid fill with contrasting text, so it reads at a glance.
                    Color fill = SegmentedSelected; if (!GUI.enabled) fill.a *= 0.35f;
                    Zui.FillRect(rect, fill);
                    GUI.Label(rect, labels[i], Zui.SelectedLabelStyle(pts, style));
                }
                else if (GUI.Button(rect, labels[i], style)) result = i;
            }
            return result;
        }

        /// <summary>Fill of the chosen option in <see cref="Segmented"/> and of a slider's filled part.</summary>
        public static Color SegmentedSelected = new Color(0.2f, 0.55f, 0.85f, 1f);
        /// <summary>Unfilled part of a slider's track.</summary>
        public static Color SliderTrack = new Color(1f, 1f, 1f, 0.2f);

        public bool Toggle(string label, bool value, float pts = 15f)
        {
            var style = Zui.ToggleStyle(pts);
            float h = Mathf.Max(style.CalcSize(new GUIContent(label)).y, UIScale.Font(pts) * 1.2f);
            var rect = Next(h);
            bool result = GUI.Toggle(rect, value, label, style);
            return result;
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
            // A visible track with the filled part up to the value: the default runtime skin draws only the thumb.
            float trackH = Mathf.Max(2f, UIScale.S(3f)), trackY = row.y + lh * 0.5f - trackH * 0.5f;
            float t = max > min ? Mathf.Clamp01((value - min) / (max - min)) : 0f;
            Zui.FillRect(new Rect(sliderX, trackY, sliderW, trackH), SliderTrack);
            Zui.FillRect(new Rect(sliderX, trackY, sliderW * t, trackH), SegmentedSelected);
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
        static readonly Dictionary<string, float> _scrollViewH = new Dictionary<string, float>();
        static readonly Dictionary<string, float> _scrollContentW = new Dictionary<string, float>();
        static readonly Dictionary<string, float> _scrollViewW = new Dictionary<string, float>();

        public static GUIStyle ButtonStyle(float pts)
        {
            int px = UIScale.Font(pts);
            if (!_buttonStyles.TryGetValue(px, out var s))
                _buttonStyles[px] = s = new GUIStyle(GUI.skin.button) { fontSize = px, wordWrap = false };
            return s;
        }

        static readonly Dictionary<int, GUIStyle> _selectedLabelStyles = new Dictionary<int, GUIStyle>();

        /// <summary>Text for a chosen option drawn over its own fill: the button's metrics and alignment, no
        /// background of its own (a button background would cover the fill), bold white text.</summary>
        public static GUIStyle SelectedLabelStyle(float pts, GUIStyle button)
        {
            int px = UIScale.Font(pts);
            if (!_selectedLabelStyles.TryGetValue(px, out var s))
                _selectedLabelStyles[px] = s = new GUIStyle
                {
                    fontSize = px, fontStyle = FontStyle.Bold, alignment = button.alignment, padding = button.padding,
                    wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = Color.white },
                };
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
            _scrollViewH[key] = outer.height;
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
            // A stack never overflows horizontally (content is laid to the viewport width), so its
            // horizontal max is 0 — right-stick-X does nothing here. When true horizontal-scroll regions
            // arrive (container work) they set a wider content width and ScrollBy's X starts to bite.
            _scrollViewW[key] = inner.width;
            _scrollContentW[key] = inner.width;
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

        /// <summary>Is this scroll region currently taller than its box (i.e. showing a scrollbar)?</summary>
        public static bool IsScrolling(string key)
        {
            if (key == null) return false;
            _scrollContentH.TryGetValue(key, out float contentH);
            _scrollViewH.TryGetValue(key, out float viewH);
            return contentH > viewH + 0.5f;
        }

        // Largest valid scroll offsets for a region (0 on an axis that doesn't overflow).
        static float MaxScroll(string key)
        {
            _scrollContentH.TryGetValue(key, out float contentH);
            _scrollViewH.TryGetValue(key, out float viewH);
            return Mathf.Max(0f, contentH - viewH);
        }

        static float MaxScrollX(string key)
        {
            _scrollContentW.TryGetValue(key, out float contentW);
            _scrollViewW.TryGetValue(key, out float viewW);
            return Mathf.Max(0f, contentW - viewW);
        }

        /// <summary>
        /// Nudge a scroll region by a pixel delta (e.g. from the right stick), clamped per axis to what
        /// actually overflows — so X only moves where a horizontal scrollbar exists, Y only where vertical.
        /// </summary>
        public static void ScrollBy(string key, Vector2 delta)
        {
            if (key == null) return;
            _scrollPos.TryGetValue(key, out var pos);
            pos.y = Mathf.Clamp(pos.y + delta.y, 0f, MaxScroll(key));
            pos.x = Mathf.Clamp(pos.x + delta.x, 0f, MaxScrollX(key));
            _scrollPos[key] = pos;
        }

        /// <summary>
        /// Scroll the region just enough to bring a content-space rect fully into view. Used for
        /// focus-follows-scroll: a menu reveals its focused item when navigation lands on it.
        /// </summary>
        public static void ScrollToReveal(string key, Rect contentRect)
        {
            if (key == null) return;
            _scrollViewH.TryGetValue(key, out float viewH);
            _scrollPos.TryGetValue(key, out var pos);
            if (contentRect.y < pos.y) pos.y = contentRect.y;                       // above the viewport → scroll up
            else if (contentRect.yMax > pos.y + viewH) pos.y = contentRect.yMax - viewH; // below → scroll down
            pos.y = Mathf.Clamp(pos.y, 0f, MaxScroll(key));
            _scrollPos[key] = pos;
        }
    }
}
