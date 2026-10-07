using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// The ONE way to render a baked animation frame in an editor window. It is the IMGUI equivalent of the
    /// in-game SpriteRenderer: it draws a baked sprite anchored by the sprite's OWN baked pivot at a scale
    /// shared across the clip. Because baked frames are uniform-size with one shared pivot, there is no
    /// size/registration "wobble". Both the Lauminary Browser and the Laumination Builder draw through here, so
    /// their previews are identical to each other and to what the game shows. Do not add a second frame
    /// drawer — extend this one.
    /// </summary>
    public static class FramePreview
    {
        /// <summary>Draw <c>frames[frame]</c> into <paramref name="box"/>, anchored by its baked pivot at
        /// (<paramref name="anchorNX"/> across, <paramref name="anchorNYTop"/> down-from-top). A single scale
        /// (every frame's pivot-relative extents fit the box) is used for every frame so the clip never resizes. Returns the anchor
        /// screen point so callers can overlay a registration crosshair on it.</summary>
        public static Vector2 DrawClip(Rect box, IList<Sprite> frames, int frame,
            float anchorNX = 0.5f, float anchorNYTop = 0.6f, float maxScale = 8f)
        {
            float cx = box.x + box.width * anchorNX, cy = box.y + box.height * anchorNYTop;
            if (frames == null || frames.Count == 0) return new Vector2(cx, cy);

            float scale = FitScale(box, RelativeBounds(frames), new Vector2(cx, cy), maxScale);
            GUI.BeginClip(box);
            try
            {
                if (frame >= 0 && frame < frames.Count)
                    DrawSpriteAtAnchor(frames[frame], cx - box.x, cy - box.y, scale, 1f);
            }
            finally { GUI.EndClip(); }
            return new Vector2(cx, cy);
        }

        /// <summary>Screen-oriented source-pixel bounds around the registration point, shared across all frames.</summary>
        public static Rect RelativeBounds(IList<Sprite> frames)
        {
            Rect bounds = new Rect(0, 0, 0, 0);
            if (frames == null) return bounds;
            foreach (var sprite in frames)
            {
                if (sprite == null) continue;
                var rect = sprite.rect;
                bounds = Union(bounds, new Rect(-sprite.pivot.x, sprite.pivot.y - rect.height, rect.width, rect.height));
            }
            return bounds;
        }

        public static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin),
            Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        /// <summary>Fit each side of the content against the space on that side of a fixed registration point.
        /// Small viewports may need sub-pixel zoom; forcing a minimum zoom would silently crop Fit.</summary>
        public static float FitScale(Rect box, Rect relativeBounds, Vector2 anchor, float maxScale = 8f, float padding = 8f)
        {
            float scale = Mathf.Max(0.0001f, maxScale);
            if (relativeBounds.xMin < 0) scale = Mathf.Min(scale, Mathf.Max(0f, anchor.x - box.xMin - padding) / -relativeBounds.xMin);
            if (relativeBounds.xMax > 0) scale = Mathf.Min(scale, Mathf.Max(0f, box.xMax - anchor.x - padding) / relativeBounds.xMax);
            if (relativeBounds.yMin < 0) scale = Mathf.Min(scale, Mathf.Max(0f, anchor.y - box.yMin - padding) / -relativeBounds.yMin);
            if (relativeBounds.yMax > 0) scale = Mathf.Min(scale, Mathf.Max(0f, box.yMax - anchor.y - padding) / relativeBounds.yMax);
            return Mathf.Max(0.0001f, scale);
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
