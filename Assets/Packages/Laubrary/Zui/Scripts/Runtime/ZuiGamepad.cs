// ZuiGamepad — the gamepad-prompt visualiser family, the sibling of FaceButtons (XYAB). Draw just the
// controls a situation needs, or a full controller map. Every piece follows the FaceButtons idiom: a
// captioned control is "in use" (vivid); an uncaptioned one is dim, so the shape still reads without
// shouting. Pure IMGUI — call from any OnGUI. Colours are neutral (the dpad/sticks/shoulders aren't
// colour-coded like the face buttons), and the face cluster reuses FaceButtons.
//
//   ZuiGamepad.DrawDpad(center, unit, new DpadPrompt(up:"Aim", down:"Crouch", left:null, right:null), captionStyle);
//   ZuiGamepad.DrawStick(center, d, "L", "Move", glyph, caption);
//   ZuiGamepad.DrawShoulder(center, w, h, new ShoulderPrompt("LB","Prev","LT","Brake"), glyph, caption, captionsLeft:true);
//   ZuiGamepad.DrawFullMap(rect, map, glyph, caption);   // the whole controller

using UnityEngine;

namespace ZuiRuntime
{
    /// <summary>Captions for the four d-pad directions (null/empty = that direction is unused/dim).</summary>
    public struct DpadPrompt
    {
        public string Up, Down, Left, Right;
        public DpadPrompt(string up, string down, string left, string right) { Up = up; Down = down; Left = left; Right = right; }
    }

    /// <summary>One shoulder side: a bumper (LB/RB) and a trigger (LT/RT) with their captions.</summary>
    public struct ShoulderPrompt
    {
        public string Bumper, BumperCaption, Trigger, TriggerCaption;
        public ShoulderPrompt(string bumper, string bumperCaption, string trigger, string triggerCaption)
        { Bumper = bumper; BumperCaption = bumperCaption; Trigger = trigger; TriggerCaption = triggerCaption; }
    }

    /// <summary>Everything a full controller map shows. Any empty caption dims that control.</summary>
    public struct GamepadMap
    {
        public FaceButtonPrompt Face;
        public DpadPrompt Dpad;
        public string LeftStick, RightStick;   // captions ("" = dim)
        public ShoulderPrompt Left, Right;      // LB/LT, RB/RT
        public string Select, Start;            // captions ("" = dim)
    }

    public static class ZuiGamepad
    {
        static readonly Color Chip = new Color(0.30f, 0.32f, 0.38f);
        static readonly Color ChipEdge = new Color(0f, 0f, 0f, 0.30f);

        // ── D-pad: a plus of four pads, each an outward arrow; active dirs vivid + captioned. ──────────
        public static void DrawDpad(Vector2 center, float unit, DpadPrompt p, GUIStyle captionStyle,
            float captionGap = 8f, float captionWidth = 130f)
        {
            float g = captionGap, lh = CaptionHeight(captionStyle);
            float pad = unit, off = unit * 1.05f;
            var up = new Vector2(center.x, center.y - off);
            var dn = new Vector2(center.x, center.y + off);
            var lt = new Vector2(center.x - off, center.y);
            var rt = new Vector2(center.x + off, center.y);

            Pad(center, pad * 0.9f, true, 0f, false);       // hub (always dim, no arrow)
            Pad(up, pad, !Empty(p.Up), 0f, true);
            Pad(dn, pad, !Empty(p.Down), 180f, true);
            Pad(lt, pad, !Empty(p.Left), 270f, true);
            Pad(rt, pad, !Empty(p.Right), 90f, true);

            float r = pad * 0.5f;
            Caption(p.Up, new Rect(up.x - captionWidth * 0.5f, up.y - r - g - lh, captionWidth, lh), TextAnchor.MiddleCenter, captionStyle);
            Caption(p.Down, new Rect(dn.x - captionWidth * 0.5f, dn.y + r + g, captionWidth, lh), TextAnchor.MiddleCenter, captionStyle);
            Caption(p.Left, new Rect(lt.x - r - g - captionWidth, lt.y - lh * 0.5f, captionWidth, lh), TextAnchor.MiddleRight, captionStyle);
            Caption(p.Right, new Rect(rt.x + r + g, rt.y - lh * 0.5f, captionWidth, lh), TextAnchor.MiddleLeft, captionStyle);
        }

        // ── Stick (left or right): a ring + hub, "L"/"R" glyph, caption below. ────────────────────────
        public static void DrawStick(Vector2 center, float diameter, string label, string caption,
            GUIStyle glyphStyle, GUIStyle captionStyle, float captionGap = 8f, float captionWidth = 160f)
        {
            bool active = !Empty(caption);
            float r = diameter * 0.5f;
            var col = active ? Chip : Dim(Chip);
            Zui.FillDisc(new Rect(center.x - r, center.y - r + 2f, diameter, diameter), ChipEdge); // shadow
            Zui.FillDisc(new Rect(center.x - r, center.y - r, diameter, diameter), col);            // ring
            float ir = r * 0.62f;
            Zui.FillDisc(new Rect(center.x - ir, center.y - ir, ir * 2f, ir * 2f), Lighten(col, 0.12f)); // hub

            var prevA = glyphStyle.alignment; var prevC = glyphStyle.normal.textColor;
            glyphStyle.alignment = TextAnchor.MiddleCenter;
            glyphStyle.normal.textColor = active ? Zui.ContrastText(col) : new Color(1, 1, 1, 0.5f);
            GUI.Label(new Rect(center.x - r, center.y - r, diameter, diameter), label, glyphStyle);
            glyphStyle.alignment = prevA; glyphStyle.normal.textColor = prevC;

            float lh = CaptionHeight(captionStyle);
            Caption(caption, new Rect(center.x - captionWidth * 0.5f, center.y + r + captionGap, captionWidth, lh),
                TextAnchor.MiddleCenter, captionStyle);
        }

        // ── Shoulder side: bumper (LB/RB) above trigger (LT/RT); captions on the outer edge. ──────────
        public static void DrawShoulder(Vector2 center, float w, float h, ShoulderPrompt p,
            GUIStyle glyphStyle, GUIStyle captionStyle, bool captionsLeft, float captionGap = 8f, float captionWidth = 150f)
        {
            float gap = h * 0.35f;
            var bumper = new Rect(center.x - w * 0.5f, center.y - h - gap * 0.5f, w, h);
            var trigger = new Rect(center.x - w * 0.5f, center.y + gap * 0.5f, w, h);
            Chiplet(bumper, p.Bumper, !Empty(p.BumperCaption), glyphStyle);
            Chiplet(trigger, p.Trigger, !Empty(p.TriggerCaption), glyphStyle);

            float lh = CaptionHeight(captionStyle);
            var align = captionsLeft ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            Rect BCap(Rect chip) => captionsLeft
                ? new Rect(chip.x - captionGap - captionWidth, chip.center.y - lh * 0.5f, captionWidth, lh)
                : new Rect(chip.xMax + captionGap, chip.center.y - lh * 0.5f, captionWidth, lh);
            Caption(p.BumperCaption, BCap(bumper), align, captionStyle);
            Caption(p.TriggerCaption, BCap(trigger), align, captionStyle);
        }

        // ── Full controller map. Lays every control in a controller-like arrangement inside area. ─────
        public static void DrawFullMap(Rect area, GamepadMap map, GUIStyle glyphStyle, GUIStyle captionStyle)
        {
            float w = area.width, h = area.height;
            float unit = Mathf.Min(w, h) * 0.052f;
            float disc = unit * 2.2f;

            // Shoulders hug the top edge.
            DrawShoulder(new Vector2(area.x + w * 0.20f, area.y + h * 0.12f), unit * 2.6f, unit * 0.9f,
                map.Left, glyphStyle, captionStyle, captionsLeft: true);
            DrawShoulder(new Vector2(area.x + w * 0.80f, area.y + h * 0.12f), unit * 2.6f, unit * 0.9f,
                map.Right, glyphStyle, captionStyle, captionsLeft: false);

            // Left stick upper-left, d-pad lower-left.
            DrawStick(new Vector2(area.x + w * 0.22f, area.y + h * 0.40f), disc, "L", map.LeftStick, glyphStyle, captionStyle);
            DrawDpad(new Vector2(area.x + w * 0.22f, area.y + h * 0.74f), unit * 1.2f, map.Dpad, captionStyle);

            // Face buttons upper-right, right stick lower-right.
            FaceButtons.DrawCluster(new Vector2(area.x + w * 0.78f, area.y + h * 0.40f), unit * 1.8f, unit * 2.4f,
                map.Face, glyphStyle, captionStyle);
            DrawStick(new Vector2(area.x + w * 0.78f, area.y + h * 0.74f), disc, "R", map.RightStick, glyphStyle, captionStyle);

            // Select / Start in the middle.
            var selC = new Vector2(area.x + w * 0.44f, area.y + h * 0.46f);
            var staC = new Vector2(area.x + w * 0.56f, area.y + h * 0.46f);
            SmallChip(selC, unit * 1.1f, "◧", map.Select, glyphStyle, captionStyle);
            SmallChip(staC, unit * 1.1f, "☰", map.Start, glyphStyle, captionStyle);
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────────
        static void Pad(Vector2 c, float size, bool active, float arrowDeg, bool arrow)
        {
            float r = size * 0.5f;
            var col = active ? Chip : Dim(Chip);
            Zui.FillRect(new Rect(c.x - r, c.y - r + 1f, size, size), ChipEdge);
            Zui.FillRect(new Rect(c.x - r, c.y - r, size, size), col);
            if (arrow) DrawTriangle(new Rect(c.x - r * 0.5f, c.y - r * 0.5f, r, r),
                active ? Zui.ContrastText(col) : new Color(1, 1, 1, 0.45f), arrowDeg);
        }

        static void Chiplet(Rect rect, string label, bool active, GUIStyle glyphStyle)
        {
            var col = active ? Chip : Dim(Chip);
            Zui.FillRect(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), ChipEdge);
            Zui.FillRect(rect, col);
            var prevA = glyphStyle.alignment; var prevC = glyphStyle.normal.textColor;
            glyphStyle.alignment = TextAnchor.MiddleCenter;
            glyphStyle.normal.textColor = active ? Zui.ContrastText(col) : new Color(1, 1, 1, 0.5f);
            GUI.Label(rect, label, glyphStyle);
            glyphStyle.alignment = prevA; glyphStyle.normal.textColor = prevC;
        }

        static void SmallChip(Vector2 center, float size, string glyph, string caption, GUIStyle glyphStyle, GUIStyle captionStyle)
        {
            bool active = !Empty(caption);
            var rect = new Rect(center.x - size * 0.9f, center.y - size * 0.5f, size * 1.8f, size);
            Chiplet(rect, glyph, active, glyphStyle);
            float lh = CaptionHeight(captionStyle);
            Caption(caption, new Rect(center.x - size * 1.6f, rect.yMax + 4f, size * 3.2f, lh), TextAnchor.MiddleCenter, captionStyle);
        }

        static Texture2D _tri;
        static Texture2D Tri
        {
            get
            {
                if (_tri == null) _tri = BuildTriangle(64);
                return _tri;
            }
        }

        static Texture2D BuildTriangle(int size)
        {
            // Upward-pointing filled triangle, anti-aliased on the two slanted edges.
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float t = y / (float)(size - 1);          // 0 at bottom, 1 at top
                float halfWidth = (1f - t) * 0.5f * size;  // narrows toward the apex
                float mid = size * 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float d = halfWidth - Mathf.Abs(x - mid);
                    float a = Mathf.Clamp01(d);            // 1px AA edge
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }

        static void DrawTriangle(Rect rect, Color color, float angleDeg)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            var m = GUI.matrix;
            if (angleDeg != 0f) GUIUtility.RotateAroundPivot(angleDeg, rect.center);
            var prev = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect, Tri);
            GUI.color = prev;
            GUI.matrix = m;
        }

        static void Caption(string text, Rect rect, TextAnchor align, GUIStyle style)
        {
            if (Empty(text)) return;
            var prev = style.alignment;
            style.alignment = align;
            GUI.Label(rect, text, style);
            style.alignment = prev;
        }

        static bool Empty(string s) => string.IsNullOrEmpty(s);

        static Color Dim(Color c) { var g = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b; var d = Color.Lerp(c, new Color(g, g, g), 0.5f); d.a = 0.35f; return d; }
        static Color Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);

        static float CaptionHeight(GUIStyle style)
        {
            if (style != null && style.lineHeight > 0f) return style.lineHeight;
            int fs = style != null && style.fontSize > 0 ? style.fontSize : 14;
            return fs * 1.35f;
        }
    }
}
