// The style cache — the "never hand-roll EnsureStyles() again" piece. Styles are keyed by their
// visual recipe INCLUDING the scaled font size, so a resolution change naturally produces fresh
// styles and old ones just sit unused. All ZuiRuntime widgets get their GUIStyles here.

using System.Collections.Generic;
using UnityEngine;

namespace ZuiRuntime
{
    public static partial class Zui
    {
        static readonly Dictionary<long, GUIStyle> _styles = new Dictionary<long, GUIStyle>();

        /// <summary>
        /// A cached label style. <paramref name="points"/> is the size at 800p (scaled internally).
        /// </summary>
        public static GUIStyle TextStyle(float points, Color color, TextAnchor align = TextAnchor.UpperLeft,
            bool bold = false, bool wrap = true)
        {
            int px = UIScale.Font(points);
            long key = Key(px, color, align, bold, wrap);
            if (_styles.TryGetValue(key, out var s)) return s;

            s = new GUIStyle(GUI.skin.label)
            {
                fontSize = px,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = align,
                wordWrap = wrap,
                richText = true,
            };
            s.normal.textColor = color;
            s.hover.textColor = color;
            _styles[key] = s;
            return s;
        }

        /// <summary>Line height of a text style — use this to advance a cursor, never a magic number.</summary>
        public static float LineHeight(float points) => UIScale.Font(points) * 1.4f;

        static long Key(int px, Color c, TextAnchor align, bool bold, bool wrap)
        {
            Color32 c32 = c;
            long key = ((long)c32.r << 40) | ((long)c32.g << 32) | ((long)c32.b << 24) | ((long)c32.a << 16);
            key |= (long)px << 48;
            key |= (long)align << 8;
            key |= (bold ? 2L : 0L) | (wrap ? 1L : 0L);
            return key;
        }
    }
}
