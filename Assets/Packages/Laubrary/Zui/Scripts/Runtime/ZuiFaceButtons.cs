// ZuiFaceButtons.cs
// A reusable runtime control: the four gamepad face buttons (Y/X/B/A) drawn in their PHYSICAL diamond
// layout, each a colored disc with its letter, and a caption beside it telling the player what it does.
// Colors match the Xbox / Steam Deck convention (Y yellow, X blue, B red, A green).
//
//          [Y caption]            captions:  Y — above the button, centered
//             ( Y )                          A — below  the button, centered
//   [X cap] ( X )  ( B ) [B cap]             X — left  of the button, right-aligned
//             ( A )                          B — right of the button, left-aligned
//          [A caption]
//
// Pure IMGUI: call DrawCluster() from any OnGUI. Captions are arbitrary strings supplied by the caller,
// so the same control serves deploy palettes, ability prompts, menu hints, etc.

using UnityEngine;

namespace ZuiRuntime
{
    /// <summary>The four captions for an XYAB cluster (null/empty hides that caption).</summary>
    public struct FaceButtonPrompt
    {
        public string Y, X, B, A;
        public FaceButtonPrompt(string y, string x, string b, string a) { Y = y; X = x; B = b; A = a; }
    }

    public static class FaceButtons
    {
        /// <summary>
        /// Draws the XYAB cluster centered at <paramref name="center"/>.
        /// </summary>
        /// <param name="center">Cluster center, in the current GUI space (e.g. inside a BeginArea).</param>
        /// <param name="buttonDiameter">Diameter of each face-button disc, in pixels.</param>
        /// <param name="spread">Distance from the cluster center to each button's center.</param>
        /// <param name="labels">The four captions.</param>
        /// <param name="glyphStyle">Style for the letter inside each disc (its color is set per-button for contrast).</param>
        /// <param name="captionStyle">Style for the captions (its alignment is set per-caption).</param>
        /// <param name="captionGap">Gap between a disc edge and its caption.</param>
        /// <param name="captionWidth">Reserved width for side captions (X/B) and the top/bottom captions.</param>
        public static void DrawCluster(Vector2 center, float buttonDiameter, float spread,
            FaceButtonPrompt labels, GUIStyle glyphStyle, GUIStyle captionStyle,
            float captionGap = 8f, float captionWidth = 160f)
        {
            float r = buttonDiameter * 0.5f;
            float lh = CaptionHeight(captionStyle);

            Vector2 yc = new Vector2(center.x, center.y - spread);
            Vector2 ac = new Vector2(center.x, center.y + spread);
            Vector2 xc = new Vector2(center.x - spread, center.y);
            Vector2 bc = new Vector2(center.x + spread, center.y);

            // Discs + letters. A button with no caption is "unused": drawn dim so the XYAB shape still
            // reads, but the captioned (in-use) buttons stay vivid and dominant.
            DrawButton(yc, buttonDiameter, ControllerColors.Y, "Y", glyphStyle, !string.IsNullOrEmpty(labels.Y));
            DrawButton(ac, buttonDiameter, ControllerColors.A, "A", glyphStyle, !string.IsNullOrEmpty(labels.A));
            DrawButton(xc, buttonDiameter, ControllerColors.X, "X", glyphStyle, !string.IsNullOrEmpty(labels.X));
            DrawButton(bc, buttonDiameter, ControllerColors.B, "B", glyphStyle, !string.IsNullOrEmpty(labels.B));

            // Captions, per the alignment spec.
            // Y — above, centered.
            Caption(labels.Y, new Rect(yc.x - captionWidth * 0.5f, yc.y - r - captionGap - lh, captionWidth, lh),
                    TextAnchor.MiddleCenter, captionStyle);
            // A — below, centered.
            Caption(labels.A, new Rect(ac.x - captionWidth * 0.5f, ac.y + r + captionGap, captionWidth, lh),
                    TextAnchor.MiddleCenter, captionStyle);
            // X — left of the button, right-aligned (text flush against the disc).
            Caption(labels.X, new Rect(xc.x - r - captionGap - captionWidth, xc.y - lh * 0.5f, captionWidth, lh),
                    TextAnchor.MiddleRight, captionStyle);
            // B — right of the button, left-aligned.
            Caption(labels.B, new Rect(bc.x + r + captionGap, bc.y - lh * 0.5f, captionWidth, lh),
                    TextAnchor.MiddleLeft, captionStyle);
        }

        /// <summary>The total height a cluster occupies (button + both captions), for reserving layout space.</summary>
        public static float ClusterHeight(float buttonDiameter, float spread, GUIStyle captionStyle, float captionGap = 8f)
        {
            float lh = CaptionHeight(captionStyle);
            return 2f * (spread + buttonDiameter * 0.5f + captionGap + lh);
        }

        static void DrawButton(Vector2 c, float d, Color color, string glyph, GUIStyle glyphStyle, bool active)
        {
            float r = d * 0.5f;
            if (active)
            {
                Zui.FillDisc(new Rect(c.x - r, c.y - r + 2f, d, d), new Color(0f, 0f, 0f, 0.28f)); // drop shadow
                Zui.FillDisc(new Rect(c.x - r, c.y - r, d, d), color);                              // base disc
            }
            else
            {
                Zui.FillDisc(new Rect(c.x - r, c.y - r, d, d), Dim(color)); // unused: faint, no shadow
            }

            var prevAlign = glyphStyle.alignment;
            var prevColor = glyphStyle.normal.textColor;
            glyphStyle.alignment = TextAnchor.MiddleCenter;
            var letter = active ? Zui.ContrastText(color) : Color.white;
            if (!active) letter.a = 0.55f;
            glyphStyle.normal.textColor = letter;
            GUI.Label(new Rect(c.x - r, c.y - r, d, d), glyph, glyphStyle);
            glyphStyle.alignment = prevAlign;
            glyphStyle.normal.textColor = prevColor;
        }

        // Desaturated, translucent version of a face color for an unused button.
        static Color Dim(Color c)
        {
            float g = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            var d = Color.Lerp(c, new Color(g, g, g), 0.6f);
            d.a = 0.30f;
            return d;
        }

        static void Caption(string text, Rect rect, TextAnchor align, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            var prev = style.alignment;
            style.alignment = align;
            GUI.Label(rect, text, style);
            style.alignment = prev;
        }

        static float CaptionHeight(GUIStyle style)
        {
            if (style != null && style.lineHeight > 0f) return style.lineHeight;
            int fs = style != null && style.fontSize > 0 ? style.fontSize : 14;
            return fs * 1.35f;
        }
    }
}
