using System.Collections.Generic;
using UnityEngine;
using Laubrary.SpriteFx;

namespace Laubrary.Chunks
{
    /// Where a sampled debris tint applies. Whole = every opaque pixel. EdgesOnly/ExcludingEdges split on
    /// whether a pixel is near the cut's own boundary OR a transparent (silhouette) neighbour — either one
    /// counts as "edge", since a cut piece's rectangle border reads as its edge just as much as a genuine
    /// alpha transition does. EdgesOnly reads as a burned/glowing rim; ExcludingEdges keeps the rim clean
    /// and tints only the interior (a scorched core, clean edge).
    public enum ChunkTintMode
    {
        None,
        Whole,
        EdgesOnly,
        ExcludingEdges,
    }

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

        // The life the modifier stack's animatable params resolve at. The pass is a ONE-TIME still bake at
        // spawn (it styles the cut debris, it does not animate over the chunk's life), so it reads the spawn
        // instant. (A per-frame animated pass over each chunk's life is possible but costly — deferred.)
        const float SpawnLife = 0f;

        /// Cuts a random square sub-rect (minPx–maxPx wide, clamped to the sprite's own size) out of
        /// source's texture, biased toward opaque pixels so chunks aren't blank cut-outs of empty space.
        /// Returns null if source is null, its texture isn't readable, or every sampled attempt came back
        /// (almost) fully transparent. tintMode/tintColor/tintStrength/edgeThicknessPx optionally recolour
        /// the cut pixels before the sprite is built — see ChunkTintMode for what each mode covers.
        /// modifiers, if any of them are shaped + enabled, run as a one-time SpriteFx pass baked into the cut
        /// texture at spawn (see ApplyModifiers) — an empty/null/all-inert stack is skipped, byte-identically.
        public static Sprite Sample(Sprite source, int minPx, int maxPx, float pixelsPerUnit,
            ChunkTintMode tintMode = ChunkTintMode.None, Color tintColor = default, float tintStrength = 0f,
            int edgeThicknessPx = 1, IReadOnlyList<PixelModifier> modifiers = null)
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
                {
                    ApplyTint(pixels, size, tintMode, tintColor, tintStrength, edgeThicknessPx);
                    return Build(pixels, size, pixelsPerUnit, source.name, modifiers);
                }

                if (coverage > bestCoverage) { bestCoverage = coverage; best = pixels; }
            }

            // Every attempt landed on mostly-empty space — fall back to the best of the attempts rather
            // than a guaranteed-blank chunk, unless even that was essentially nothing.
            if (bestCoverage <= 0.02f) return null;
            ApplyTint(best, size, tintMode, tintColor, tintStrength, edgeThicknessPx);
            return Build(best, size, pixelsPerUnit, source.name, modifiers);
        }

        static float AlphaCoverage(Color[] pixels)
        {
            if (pixels == null || pixels.Length == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < pixels.Length; i++) sum += pixels[i].a;
            return sum / pixels.Length;
        }

        // A pixel counts as "edge" if it sits within `thickness` of the cut rectangle's own border (the cut
        // boundary reads as an edge of this piece even where the source pixels were interior/opaque) OR has
        // a transparent neighbour within that same radius (a genuine alpha-silhouette edge). Cheap brute-force
        // neighbour scan — fine at the small sizes debris sampling actually uses (single-digit to ~20px).
        static bool IsEdgePixel(Color[] px, int size, int x, int y, int thickness)
        {
            if (x < thickness || y < thickness || x >= size - thickness || y >= size - thickness) return true;
            for (int dy = -thickness; dy <= thickness; dy++)
                for (int dx = -thickness; dx <= thickness; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                    if (px[ny * size + nx].a < 0.5f) return true;
                }
            return false;
        }

        // Mutates px in place, RGB only (alpha untouched) — safe to do while IsEdgePixel is still reading
        // neighbour alpha for later pixels in the same pass, since alpha never changes here.
        static void ApplyTint(Color[] px, int size, ChunkTintMode mode, Color tint, float strength, int edgeThickness)
        {
            if (mode == ChunkTintMode.None || strength <= 0f || px == null) return;
            edgeThickness = Mathf.Max(1, edgeThickness);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    if (px[i].a <= 0.001f) continue;
                    bool isEdge = (mode == ChunkTintMode.EdgesOnly || mode == ChunkTintMode.ExcludingEdges)
                        && IsEdgePixel(px, size, x, y, edgeThickness);
                    bool applies = mode == ChunkTintMode.Whole
                        || (mode == ChunkTintMode.EdgesOnly && isEdge)
                        || (mode == ChunkTintMode.ExcludingEdges && !isEdge);
                    if (!applies) continue;
                    var c = px[i];
                    px[i] = Color.Lerp(c, new Color(tint.r, tint.g, tint.b, c.a), Mathf.Clamp01(strength));
                }
        }

        static Sprite Build(Color[] pixels, int size, float pixelsPerUnit, string sourceName,
            IReadOnlyList<PixelModifier> modifiers = null)
        {
            ApplyModifiers(pixels, size, modifiers);

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

        // A ONE-TIME per-chunk SpriteFx pass baked into the cut pixels at spawn — styles the debris look
        // (tint/posterise/dither/dissolve/…) cheaply, once, over this small texture. Runs the SAME shaped
        // stack a SpriteFxFilter would, through SpriteFxFilter.Apply (which resolves the stack and dispatches
        // to SpriteFxStack.RunInline for the inline path) — so what a Chunk bakes matches what that filter plays.
        //
        // The empty / no-op path is a hard SKIP (returns before touching the pixels or the RNG), so a spec
        // with no modifiers — or only disabled / non-shaped ones — leaves the cut pixels byte-identical to a
        // chunk built without this call at all. Only shaped, enabled PixelModifiers apply (RunInline runs the
        // gather-free family: Tint/Contrast/Brightness/Saturation/Posterize/OrderedDither/LayerDissolve/
        // AlphaMask); a VoronoiCrack or geometry/post modifier is silently a no-op here, matching SpriteFxFilter.
        static void ApplyModifiers(Color[] pixels, int size, IReadOnlyList<PixelModifier> modifiers)
        {
            if (pixels == null || pixels.Length == 0 || modifiers == null || modifiers.Count == 0) return;

            bool anyShaped = false;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var m = modifiers[i];
                if (m != null && m.enabled && SpriteFxStack.IsShaped(m)) { anyShaped = true; break; }
            }
            if (!anyShaped) return;   // nothing the inline stack would apply — leave the pixels untouched

            var px32 = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) px32[i] = pixels[i];

            // A per-chunk seed so hashing modifiers (LayerDissolve scatter/erase, AlphaMask noise, any MinMax
            // param) give each cut fragment its own pattern instead of a uniform stamp.
            int seed = Random.Range(int.MinValue, int.MaxValue);
            SpriteFxFilter.Apply(px32, size, size, modifiers, 0, SpawnLife, seed, useBurst: false);

            for (int i = 0; i < pixels.Length; i++) pixels[i] = px32[i];
        }
    }
}
