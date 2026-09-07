using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.ZoetropePyre
{
    /// Shared "read the Zoe's own on-screen sprite, live, off its renderer" logic — the one place both
    /// <see cref="SpawnChunkFx"/> and <see cref="PyreChunksFx"/> pull a live colour palette (and, since T-0252,
    /// the live sprite itself for a Chunks Sampled-visual Debris Scatter to cut pieces from) off a Zoe's
    /// CURRENT lauminary frame, never authored art. One shared implementation means the two effects can never
    /// silently drift on what "the Zoe's current sprite" means.
    public static class ZoeLiveSampler
    {
        static bool _warnedUnreadable;

        /// The renderer's current sprite, or null. What a Chunks Sampled-visual Debris Scatter cuts pieces from
        /// when this is forwarded as a burst's ChunkModuleContext.SampleSourceOverride.
        public static Sprite LiveSprite(SpriteRenderer sr) => sr != null ? sr.sprite : null;

        /// Grab up to <paramref name="count"/> opaque colours from the renderer's CURRENT sprite (its live
        /// lauminary frame), pre-multiplied by the renderer's tint so the debris matches what's on screen.
        /// Returns null when there is nothing sampleable (no renderer/sprite, or an unreadable texture) — the
        /// caller then falls back to its own authored colours. Deterministic stride sampling, so the same frame
        /// yields the same palette.
        public static List<Color32> SampleColours(SpriteRenderer sr, int count)
        {
            if (sr == null || sr.sprite == null) return null;
            var sprite = sr.sprite;
            var tex = sprite.texture;
            if (tex == null) return null;
            if (!tex.isReadable)
            {
                if (!_warnedUnreadable)
                {
                    _warnedUnreadable = true;
                    Debug.LogWarning($"[ZoeLiveSampler] Sprite texture '{tex.name}' is not Read/Write enabled — cannot " +
                                     "sample its pixels for debris colours, so the burst uses the spec's own colours. " +
                                     "Tick 'Read/Write Enabled' on the sprite's import settings.");
                }
                return null;
            }

            Rect tr = sprite.textureRect;
            int x = Mathf.RoundToInt(tr.x), y = Mathf.RoundToInt(tr.y);
            int w = Mathf.RoundToInt(tr.width), h = Mathf.RoundToInt(tr.height);
            if (w <= 0 || h <= 0) return null;

            Color32[] block;
            if (x == 0 && y == 0 && w == tex.width && h == tex.height)
            {
                block = tex.GetPixels32();   // whole-texture fast path
            }
            else
            {
                Color[] cols = tex.GetPixels(x, y, w, h);   // atlased sub-rect
                block = new Color32[cols.Length];
                for (int i = 0; i < cols.Length; i++) block[i] = (Color32)cols[i];
            }
            if (block.Length == 0) return null;

            Color tint = sr.color;
            var list = new List<Color32>(count);
            int stride = Mathf.Max(1, block.Length / (count * 8));
            for (int i = 0; i < block.Length && list.Count < count; i += stride)
            {
                Color32 c = block[i];
                if (c.a <= 40) continue;           // skip (near-)transparent pixels
                list.Add((Color32)((Color)c * tint));
            }
            return list.Count > 0 ? list : null;
        }
    }
}
