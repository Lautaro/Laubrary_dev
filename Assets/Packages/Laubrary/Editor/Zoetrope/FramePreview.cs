using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The ONE way to render a baked animation frame in an editor window. It is the IMGUI equivalent of the
    /// in-game SpriteRenderer: it draws a baked sprite anchored by the sprite's OWN baked pivot at a scale
    /// shared across the clip. Because baked frames are uniform-size with one shared pivot, there is no
    /// size/registration "wobble". Both the Zoe Browser and the Animation Builder draw through here, so
    /// their previews are identical to each other and to what the game shows. Do not add a second frame
    /// drawer — extend this one.
    /// </summary>
    public static class FramePreview
    {
        /// <summary>Draw <c>frames[frame]</c> into <paramref name="box"/>, anchored by its baked pivot at
        /// (<paramref name="anchorNX"/> across, <paramref name="anchorNYTop"/> down-from-top). A single scale
        /// (largest frame fits the box) is used for every frame so the clip never resizes. Returns the anchor
        /// screen point so callers can overlay a registration crosshair on it.</summary>
        public static Vector2 DrawClip(Rect box, IList<Sprite> frames, int frame,
            float anchorNX = 0.5f, float anchorNYTop = 0.6f, float maxScale = 8f)
        {
            float cx = box.x + box.width * anchorNX, cy = box.y + box.height * anchorNYTop;
            if (frames == null || frames.Count == 0) return new Vector2(cx, cy);

            float maxW = 1f, maxH = 1f;
            foreach (var s in frames)
            {
                if (s == null) continue;
                maxW = Mathf.Max(maxW, s.rect.width);
                maxH = Mathf.Max(maxH, s.rect.height);
            }
            float scale = Mathf.Clamp(Mathf.Min((box.width - 16f) / maxW, (box.height - 16f) / maxH), 0.25f, maxScale);

            if (frame >= 0 && frame < frames.Count)
                DrawSpriteAtAnchor(frames[frame], cx, cy, scale, 1f);
            return new Vector2(cx, cy);
        }

        /// <summary>Draw a single baked sprite anchored by its OWN baked pivot at (<paramref name="cx"/>,
        /// <paramref name="cy"/>), at <paramref name="scale"/> screen-px per source-px. This is the atomic
        /// "show a baked frame" op — used both for clip playback and (for alignment) the registration canvas,
        /// so every surface that shows a baked frame lands it on the exact same pixel.</summary>
        public static void DrawSpriteAtAnchor(Sprite sp, float cx, float cy, float scale, float alpha)
        {
            if (sp == null || sp.texture == null) return;
            Rect r = sp.rect; var tex = sp.texture;
            if (tex.filterMode != FilterMode.Point) tex.filterMode = FilterMode.Point; // crisp pixel-art
            float w = r.width * scale, h = r.height * scale;
            Vector2 pivN = new Vector2(sp.pivot.x / Mathf.Max(1f, r.width), sp.pivot.y / Mathf.Max(1f, r.height));
            // Round to whole screen pixels so the magnified frame lands on the pixel grid.
            Rect draw = new Rect(Mathf.Round(cx - pivN.x * w), Mathf.Round(cy - (1f - pivN.y) * h), w, h);
            Rect uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
            Color prev = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(draw, tex, uv, true);
            GUI.color = prev;
        }
    }
}
