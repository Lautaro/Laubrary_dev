using UnityEngine;

namespace Laubrary.PreviewKit
{
    /// Turning a Sprite into a preview texture, safely.
    ///
    /// The naive version — `texture.GetPixels(...)` — THROWS on any sprite whose importer has Read/Write
    /// disabled, which is the DEFAULT for imported art. So it works fine on textures a tool generated itself
    /// and blows up on the artist's actual sprite sheet, which is the worst possible way round: previews look
    /// healthy right up until they meet real content.
    ///
    /// Blitting through a temporary RenderTexture reads anything the GPU can sample, readable or not.
    public static class PreviewTex
    {
        /// A fresh texture holding EXACTLY `area` of `source`, readable or not. Caller owns it.
        ///
        /// Exists because `Sprite.textureRect` is not a reliable pixel rect. With the importer's default
        /// TIGHT mesh, Unity derives it from the generated outline, so it comes back FRACTIONAL and
        /// trigonometric — real values seen in this project: 27.92388 (32·cos22.5°) and 21.70711 (·1/√2).
        /// Anything compositing a GRID wants the sprite's DECLARED `rect` instead, which is always the exact
        /// slice, and needs to read that rect verbatim rather than the trimmed outline's bounding box.
        public static Texture2D ReadRect(Texture source, Rect area)
        {
            if (source == null) return null;
            int w = Mathf.Max(1, Mathf.RoundToInt(area.width)), h = Mathf.Max(1, Mathf.RoundToInt(area.height));

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                // ReadPixels' origin is the RT's bottom-left — the same convention sprite rects use.
                tex.ReadPixels(new Rect(area.x, area.y, w, h), 0, 0);
                tex.Apply();
            }
            catch
            {
                Object.DestroyImmediate(tex);
                tex = null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            return tex;
        }

        /// A fresh texture holding just this sprite's own rect. Caller owns it. Null if there is nothing to read.
        public static Texture2D CropSprite(Sprite s)
        {
            if (s == null || s.texture == null) return null;
            var r = s.textureRect;
            int w = Mathf.Max(1, (int)r.width), h = Mathf.Max(1, (int)r.height);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            if (!BlitInto(tex, s)) { Object.DestroyImmediate(tex); return null; }
            return tex;
        }

        /// Overwrite `dest` with this sprite's rect. Used for animated previews, where the same texture is
        /// reused frame after frame rather than reallocated. Returns false if the sizes disagree — a caller
        /// mid-repaint wants a skipped frame, not an exception every frame.
        public static bool BlitInto(Texture2D dest, Sprite s)
        {
            if (dest == null || s == null || s.texture == null) return false;
            var r = s.textureRect;
            if ((int)r.width != dest.width || (int)r.height != dest.height) return false;

            var rt = RenderTexture.GetTemporary(s.texture.width, s.texture.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(s.texture, rt);
                RenderTexture.active = rt;
                // ReadPixels' origin is the RT's bottom-left, which is the same convention textureRect uses.
                dest.ReadPixels(new Rect(r.x, r.y, dest.width, dest.height), 0, 0);
                dest.Apply();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            return true;
        }
    }
}
