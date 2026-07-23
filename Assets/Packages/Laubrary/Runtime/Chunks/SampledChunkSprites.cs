using UnityEngine;

namespace Laubrary.Chunks
{
    /// Builds small debris sprites by cutting a random sub-rect directly out of an exploding object's own
    /// sprite/texture, instead of a flat-tinted procedural shape or a hand-authored asset — the "sample a
    /// small chunk of the thing that broke" trick. Pairs with ChunkTumble's squash+shade to read as a
    /// tumbling 3D fragment using only the object's own pixels, no extra art needed.
    ///
    /// Requires the source texture's Read/Write Enabled import setting — like any runtime GetPixels call,
    /// this throws otherwise. Sample() catches that and returns null rather than propagating, since a
    /// missing import flag on someone's source art shouldn't crash a burst.
    public static class SampledChunkSprites
    {
        const int MaxAttempts = 8;
        const float MinAcceptableAlphaCoverage = 0.35f;

        /// Cuts a random square sub-rect (minPx–maxPx wide, clamped to the sprite's own size) out of
        /// source's texture, biased toward opaque pixels so chunks aren't blank cut-outs of empty space.
        /// Returns null if source is null, its texture isn't readable, or every sampled attempt came back
        /// (almost) fully transparent.
        public static Sprite Sample(Sprite source, int minPx, int maxPx, float pixelsPerUnit)
        {
            if (source == null || source.texture == null) return null;

            var texRect = source.textureRect; // the sprite's own packed region within a possibly-shared atlas texture
            int texX = Mathf.FloorToInt(texRect.x), texY = Mathf.FloorToInt(texRect.y);
            int texW = Mathf.Max(1, Mathf.FloorToInt(texRect.width)), texH = Mathf.Max(1, Mathf.FloorToInt(texRect.height));

            minPx = Mathf.Max(1, minPx);
            maxPx = Mathf.Max(minPx, maxPx);
            int size = Mathf.Min(Random.Range(minPx, maxPx + 1), Mathf.Min(texW, texH));
            if (size < 1) return null;

            Color[] best = null;
            float bestCoverage = -1f;

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int ox = texX + Random.Range(0, Mathf.Max(1, texW - size + 1));
                int oy = texY + Random.Range(0, Mathf.Max(1, texH - size + 1));

                Color[] pixels;
                // GetPixels32 has no sub-rect overload (only whole-texture); GetPixels does.
                try { pixels = source.texture.GetPixels(ox, oy, size, size, 0); }
                catch (UnityException) { return null; } // texture not Read/Write enabled — nothing we can do

                float coverage = AlphaCoverage(pixels);
                if (coverage >= MinAcceptableAlphaCoverage)
                    return Build(pixels, size, pixelsPerUnit, source.name);

                if (coverage > bestCoverage) { bestCoverage = coverage; best = pixels; }
            }

            // Every attempt landed on mostly-empty space — fall back to the best of the attempts rather
            // than a guaranteed-blank chunk, unless even that was essentially nothing.
            return bestCoverage > 0.02f ? Build(best, size, pixelsPerUnit, source.name) : null;
        }

        static float AlphaCoverage(Color[] pixels)
        {
            if (pixels == null || pixels.Length == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < pixels.Length; i++) sum += pixels[i].a;
            return sum / pixels.Length;
        }

        static Sprite Build(Color[] pixels, int size, float pixelsPerUnit, string sourceName)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = $"ChunkSample_{sourceName}",
            };
            tex.SetPixels(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), Mathf.Max(1f, pixelsPerUnit));
            sprite.name = tex.name;
            return sprite;
        }
    }
}
