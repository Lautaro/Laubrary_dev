using UnityEngine;
using UnityEditor;

namespace Laubrary.BackSplash.Editor
{
    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Paints a <see cref="BackSplashSettings"/> into an IMGUI viewport rect — the colour fill and, over
    /// it, the image at its authored zoom, position and tint.
    ///
    /// This exists because the same twenty lines were written three times. Pyre's and PyrePlus's copies had drifted
    /// only in their fallback colour, but TextSplash's had drifted in a way nobody could see: it painted the fill,
    /// the sprite and the tint, and silently never read <c>imageZoom</c> or <c>imagePos</c> — so that window's Zoom
    /// slider and Position pad moved, saved, and did nothing at all, under a comment claiming it worked "exactly
    /// like Pyre's viewport". A control that does nothing is the most expensive kind of bug: it looks finished.
    ///
    /// <see cref="BackSplashZui"/> already made the AUTHORING side single-source for the same reason. This is the
    /// rendering half of that argument.
    ///
    /// It deliberately does NOT own the viewport, the layout, or when it is called — a caller composites its own
    /// subject over this in its own paint order, and Mirage doesn't use it at all (its backdrop is a real Camera
    /// and SpriteRenderer, not a blit).</summary>
    public static class BackSplashPainter
    {
        /// <summary>Fill <paramref name="view"/> with the backdrop. <paramref name="fallback"/> is used when there
        /// are no settings at all, so each window keeps the empty-state colour it always had.</summary>
        public static void Draw(Rect view, BackSplashSettings bs, Color fallback)
        {
            if (bs == null) { EditorGUI.DrawRect(view, fallback); return; }

            EditorGUI.DrawRect(view, bs.cameraColor);
            if (bs.image == null || bs.image.texture == null) return;

            // Draw the sprite's own rect out of its atlas page, not the whole texture — a BackSplash image is
            // often one sprite in a packed sheet.
            var tex = bs.image.texture;
            var r = bs.image.textureRect;
            var tc = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);

            var prevCol = GUI.color;
            GUI.color = bs.imageTint;
            // Clipped so a zoomed-in image cannot paint outside the viewport it belongs to, and so the rect below
            // can be expressed in view-local coordinates (which is what makes the centring read).
            GUI.BeginClip(view);
            float w = view.width * bs.imageZoom, h = view.height * bs.imageZoom;
            // Centred, then offset. `imagePos.y` is NEGATED because the pad authors +y as UP while IMGUI's rects
            // measure +y DOWN.
            var imgRect = new Rect((view.width - w) * 0.5f + bs.imagePos.x,
                                   (view.height - h) * 0.5f - bs.imagePos.y, w, h);
            GUI.DrawTextureWithTexCoords(imgRect, tex, tc, true);
            GUI.EndClip();
            GUI.color = prevCol;
        }
    }
}
