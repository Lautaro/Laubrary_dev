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
            => Sample(source, minPx, maxPx, pixelsPerUnit, out _, tintMode, tintColor, tintStrength,
                      edgeThicknessPx, modifiers);

        /// Same cut, but ALSO reports WHERE it came from: <paramref name="cutRect"/> is the chosen sub-rect in
        /// the sprite's OWN pixel space (relative to its textureRect, bottom-left origin), or default when the
        /// sample fails. This exists for the Chunk editor's preview-subject stage, which outlines the cuts on
        /// the subject sprite — sharing this one routine keeps "where the preview says a slice comes from"
        /// identical to where a runtime slice actually comes from (the one-shared-core rule).
        public static Sprite Sample(Sprite source, int minPx, int maxPx, float pixelsPerUnit, out RectInt cutRect,
            ChunkTintMode tintMode = ChunkTintMode.None, Color tintColor = default, float tintStrength = 0f,
            int edgeThicknessPx = 1, IReadOnlyList<PixelModifier> modifiers = null)
        {
            cutRect = default;
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
            int bestOx = 0, bestOy = 0;

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
                    cutRect = new RectInt(ox - texX, oy - texY, size, size);
                    ApplyTint(pixels, size, tintMode, tintColor, tintStrength, edgeThicknessPx);
                    return Build(pixels, size, pixelsPerUnit, source.name, modifiers);
                }

                if (coverage > bestCoverage) { bestCoverage = coverage; best = pixels; bestOx = ox; bestOy = oy; }
            }

            // Every attempt landed on mostly-empty space — fall back to the best of the attempts rather
            // than a guaranteed-blank chunk, unless even that was essentially nothing.
            if (bestCoverage <= 0.02f) return null;
            cutRect = new RectInt(bestOx - texX, bestOy - texY, size, size);
            ApplyTint(best, size, tintMode, tintColor, tintStrength, edgeThicknessPx);
            return Build(best, size, pixelsPerUnit, source.name, modifiers);
        }

        /// The square cut rect, in the sprite's OWN pixel space (relative to its textureRect, bottom-left
        /// origin), centred as closely as possible on the pixel (<paramref name="centreX"/>,
        /// <paramref name="centreY"/>). A square that would run off the sprite is SHIFTED back inside, not
        /// shrunk — a cut taken next to the silhouette's edge stays exactly the size the recipe asked for, so
        /// "1-5 px" never silently becomes 1 px near a border. Only a sprite smaller than the requested size
        /// shrinks it, and then to the sprite's own smaller dimension. Returns a zero-size rect for a null
        /// source, which <see cref="SampleAt"/> treats as "no cut".
        public static RectInt CutRectAround(Sprite source, int centreX, int centreY, int size)
        {
            if (source == null || source.texture == null) return default;
            var texRect = source.textureRect;
            int texW = Mathf.Max(1, Mathf.FloorToInt(texRect.width));
            int texH = Mathf.Max(1, Mathf.FloorToInt(texRect.height));

            size = Mathf.Clamp(size, 1, Mathf.Min(texW, texH));
            int x = Mathf.Clamp(centreX - size / 2, 0, texW - size);
            int y = Mathf.Clamp(centreY - size / 2, 0, texH - size);
            return new RectInt(x, y, size, size);
        }

        /// Cuts the EXPLICIT sub-rect <paramref name="cut"/> (sprite-own pixel space, as
        /// <see cref="CutRectAround"/> returns) out of source's texture. The caller chooses where and how big,
        /// which is the whole reason this exists beside <see cref="Sample"/>: Sample rolls its own size and
        /// position off <c>UnityEngine.Random</c>, and a seeded caller (PaletteSplash, whose editor preview has
        /// to reproduce a burst draw-for-draw off one authored seed) cannot inherit a generator it does not
        /// own. Nothing in here touches any random generator — same pixels in, same pixels out, always.
        ///
        /// tintMode/tintColor/tintStrength/edgeThicknessPx recolour the cut exactly as <see cref="Sample"/>
        /// does. <paramref name="edgeMaskStrength"/> then optionally eats the cut's own rectangle away toward
        /// transparency (see <see cref="ApplyEdgeMask"/>) so a sampled fragment reads as an organic scrap rather
        /// than a crisp little rectangle of somebody's art; 0 skips that pass entirely and leaves the pixels
        /// byte-identical to an unmasked cut.
        ///
        /// Null when the source is missing, the rect is empty, or the texture is not Read/Write enabled — the
        /// caller is expected to fall back to whatever it would have drawn without a cut, never to nothing.
        public static Sprite SampleAt(Sprite source, RectInt cut, float pixelsPerUnit,
            ChunkTintMode tintMode = ChunkTintMode.None, Color tintColor = default, float tintStrength = 0f,
            int edgeThicknessPx = 1, float edgeMaskStrength = 0f, float edgeMaskJitter = 0f, int maskSeed = 0,
            IReadOnlyList<PixelModifier> modifiers = null)
        {
            if (source == null || source.texture == null) return null;
            int size = Mathf.Min(cut.width, cut.height);
            if (size < 1) return null;

            var texRect = source.textureRect;
            int ox = Mathf.FloorToInt(texRect.x) + cut.x;
            int oy = Mathf.FloorToInt(texRect.y) + cut.y;

            Color[] pixels;
            // GetPixels32 has no sub-rect overload (only whole-texture); GetPixels does.
            try { pixels = source.texture.GetPixels(ox, oy, size, size, 0); }
            catch (UnityException) { return null; }   // texture not Read/Write enabled — nothing we can do
            if (pixels == null || pixels.Length < size * size) return null;

            ApplyTint(pixels, size, tintMode, tintColor, tintStrength, edgeThicknessPx);
            ApplyEdgeMask(pixels, size, edgeMaskStrength, edgeMaskJitter, maskSeed);
            return Build(pixels, size, pixelsPerUnit, source.name, modifiers);
        }

        /// Fades the cut's own square outline away toward transparency, so the fragment reads as a torn scrap
        /// instead of a rectangle of somebody's art. Alpha only — the RGB the source art supplied is never
        /// touched, so a masked cut is still made of the real pixels.
        ///
        /// A pixel's distance from the cut's centre, normalised so 1.0 is the inscribed circle, drives the
        /// falloff: full alpha inside <c>inner</c>, linearly to zero at 1.0, where inner walks from 1.0 (only
        /// the corners clipped) down to 0.15 (barely a dot left) as <paramref name="strength"/> goes 0 → 1.
        /// <paramref name="jitter"/> then pushes each pixel's distance by up to ±jitter/2 from a HASH of its own
        /// coordinates and <paramref name="maskSeed"/> — never a random generator, so the same crop masks the
        /// same way every run and a seeded preview can reproduce it — which is what keeps the boundary ragged
        /// rather than a machined circle.
        ///
        /// ⚠ It can never mask a cut out of existence: if the pass would leave nothing visible, the cut's
        /// most central originally-opaque pixel is put back at its original alpha. A fully-masked crop would be
        /// an invisible particle that still costs a GameObject — the silent no-op this module's whole source
        /// waterfall exists to avoid.
        static void ApplyEdgeMask(Color[] px, int size, float strength, float jitter, int maskSeed)
        {
            if (px == null || size <= 1 || strength <= 0f) return;
            strength = Mathf.Clamp01(strength);
            jitter = Mathf.Clamp01(jitter);

            float radius = size * 0.5f;
            float inner = Mathf.Lerp(1f, 0.15f, strength);
            float band = Mathf.Max(0.0001f, 1f - inner);

            int centreIdx = -1;
            float centreDistSq = float.MaxValue, centreAlpha = 0f, maxAlpha = 0f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float a0 = px[i].a;
                    if (a0 <= 0.001f) continue;

                    float dx = (x + 0.5f) - radius, dy = (y + 0.5f) - radius;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < centreDistSq) { centreDistSq = distSq; centreIdx = i; centreAlpha = a0; }

                    float d = Mathf.Sqrt(distSq) / radius + (Hash01(x, y, maskSeed) - 0.5f) * jitter;
                    float keep = d <= inner ? 1f : 1f - Mathf.Clamp01((d - inner) / band);

                    px[i].a = a0 * keep;
                    if (px[i].a > maxAlpha) maxAlpha = px[i].a;
                }

            if (maxAlpha < 0.08f && centreIdx >= 0) px[centreIdx].a = centreAlpha;
        }

        /// A deterministic 0..1 value from two pixel coordinates and a seed — the same murmur3-style finalising
        /// avalanche ChunkRng uses, so neighbouring pixels (and neighbouring seeds) land in completely different
        /// places instead of producing a visible diagonal pattern across the cut.
        static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 16; h *= 0x7FEB352Du;
                h ^= h >> 15; h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
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
