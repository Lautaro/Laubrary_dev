// ZuiRuntime.cs
// The reusable ZUI *runtime* UI toolkit — the runtime sibling of the editor ZUI (which is
// UnityEditor-only and stripped from builds). This code is pure UnityEngine and ships in players. It is
// IMMEDIATE-MODE (OnGUI / IMGUI), matching how prototypes commonly draw HUDs and overlays: no scene
// objects, prefabs, or Canvas required — call a draw method from any OnGUI and it paints.
//
// Adopted from TrueEye's nucleus (Zui primitives + FaceButtons) and grown with the trap-aware helpers:
// every helper either MEASURES (text sized before drawn), CLAMPS (rects forced on-screen, overflow
// scrolls), or OWNS STATE (baselines, scroll positions) — so the classic prototype-UI bugs are
// inexpressible. See ClaudeUI's Docs/ClaudeUI-Roadmap.md, Phase 2b.
//
// Namespace is `ZuiRuntime` (NOT `ZUI`) on purpose: the editor toolkit is the global type `ZUI`, and a
// namespace segment named `ZUI` would collide with that type in editor assemblies that reference both.

using UnityEngine;

namespace ZuiRuntime
{
    /// <summary>Low-level runtime IMGUI primitives: cached fill textures and tinted rect/disc fills.</summary>
    public static partial class Zui
    {
        static Texture2D _white;
        static Texture2D _disc;

        /// <summary>A 1×1 opaque-white texture, tinted via <see cref="GUI.color"/> when drawn.</summary>
        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave };
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                }
                return _white;
            }
        }

        /// <summary>A white anti-aliased filled circle, tinted via <see cref="GUI.color"/> when drawn.</summary>
        public static Texture2D Disc
        {
            get
            {
                if (_disc == null) _disc = BuildDisc(128);
                return _disc;
            }
        }

        static Texture2D BuildDisc(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            float r = size * 0.5f - 0.5f;
            var c = new Vector2(r, r);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), c);
                    float a = Mathf.Clamp01(r - dist + 0.5f); // 1px anti-aliased edge
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Fills a rectangle with a solid color (Repaint-only; safe in tight loops).</summary>
        public static void FillRect(Rect rect, Color color)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = prev;
        }

        /// <summary>Fills a circle inscribed in <paramref name="rect"/> with a solid color (Repaint-only).</summary>
        public static void FillDisc(Rect rect, Color color)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Disc);
            GUI.color = prev;
        }

        /// <summary>A readable text color (near-black or white) for a glyph sitting on <paramref name="bg"/>.</summary>
        public static Color ContrastText(Color bg)
        {
            float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return lum > 0.62f ? new Color(0.10f, 0.10f, 0.10f) : Color.white;
        }
    }

    /// <summary>Standard Xbox / Steam Deck face-button colors (A green, B red, X blue, Y yellow).</summary>
    public static class ControllerColors
    {
        public static readonly Color A = new Color(0.30f, 0.74f, 0.32f); // green
        public static readonly Color B = new Color(0.88f, 0.22f, 0.20f); // red
        public static readonly Color X = new Color(0.18f, 0.52f, 0.92f); // blue
        public static readonly Color Y = new Color(0.97f, 0.78f, 0.12f); // yellow
    }
}
