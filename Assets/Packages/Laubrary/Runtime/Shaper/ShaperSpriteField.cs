// T-0175 — a Sprite primitive's alpha becomes coverage + a real signed edge distance, baked once per authoring
// state and cached. Mirrors PyrePrepassCache<T>'s shape (Runtime/Pyre/PyrePrepassCache.cs) — a ConditionalWeakTable
// keyed by owner identity, holding one immutable snapshot swapped under a content-hash key — rewritten against
// Shaper's own types rather than PyreForm/PyreFormCtx, which Runtime/Shaper does not depend on (T-0112's own
// stated reason for IShaperCompositeSource not referencing PyreForm applies here too).
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// One Sprite primitive's baked picture: a signed distance raster in the node's own local units, negative
    /// inside, covering <c>[-halfExtentX, halfExtentX] x [-halfExtentY, halfExtentY]</c> — the same "bulk data
    /// referenced by index, never a managed object in the op" shape <see cref="ShaperCompiledComposite"/> already
    /// uses for a composite generator's raster.
    /// </summary>
    public sealed class ShaperCompiledSpriteField
    {
        public int width, height;
        public float halfExtentX, halfExtentY;

        /// <summary>Signed distance in LOCAL units, row-major, row 0 = bottom (matches ShaperCompiledComposite's
        /// own convention). Negative inside, from an exact Euclidean distance transform of the threshold+softness
        /// mask, so — unlike a composite's coverage-inverted pseudo-distance — this is a real distance everywhere
        /// on the raster, not merely within one texel of the edge.</summary>
        public float[] distance;
    }

    /// <summary>
    /// T-0175 — builds and caches a Sprite primitive's <see cref="ShaperCompiledSpriteField"/>. Copies
    /// <c>PyrePrepassCache&lt;T&gt;</c>'s pattern (identity key + content-hash snapshot, one build under lock)
    /// rather than referencing it, for the asmdef-boundary reason above.
    /// </summary>
    public static class ShaperSpritePrepassCache
    {
        sealed class Snapshot { public readonly int key; public readonly ShaperCompiledSpriteField value; public Snapshot(int k, ShaperCompiledSpriteField v) { key = k; value = v; } }
        sealed class Entry { public volatile Snapshot snap; }
        static readonly ConditionalWeakTable<ShaperPrimitiveDef, Entry> _entries = new ConditionalWeakTable<ShaperPrimitiveDef, Entry>();

        /// <summary>Longer side of the bake grid, in texels. Fixed rather than authored (unlike a Composite
        /// node's bakeWidth/Height) — a Sprite primitive has no other dials competing for the same "how sharp"
        /// authoring surface, and an exact distance transform's cost is dominated by this square regardless of
        /// the source sprite's own resolution.</summary>
        const int BakeMaxDim = 128;
        const int MinBakeDim = 8;

        /// <summary>Softness blur radius cap, in texels — bounds the box-blur pass's cost regardless of how large
        /// a softness value is authored; beyond this the mask has already lost all but its lowest frequency.</summary>
        const int MaxBlurRadiusTexels = 8;

        /// <summary>
        /// The cached raster for <paramref name="def"/> at the RESOLVED dial values <paramref name="halfW"/>/
        /// <paramref name="halfH"/>/<paramref name="threshold"/>/<paramref name="softness"/> (already sampled by
        /// the caller at the compile's own phase/seed — this method never reads a ZUIValue). Null when
        /// <see cref="ShaperPrimitiveDef.spriteAsset"/> is unassigned: a legal, if useless, authoring state, same
        /// posture as an unassigned Texture/Gradient fill (FC-6.5) or a Composite node with no source.
        /// </summary>
        public static ShaperCompiledSpriteField Get(ShaperPrimitiveDef def, float halfW, float halfH,
            float threshold, float softness, ShaperSpriteFitMode fitMode)
        {
            if (def == null || def.spriteAsset == null) return null;

            int key = KeyOf(def.spriteAsset, halfW, halfH, threshold, softness, fitMode);
            var e = _entries.GetOrCreateValue(def);
            var s = e.snap;
            if (s != null && s.key == key) return s.value;
            lock (e)
            {
                s = e.snap;
                if (s != null && s.key == key) return s.value;
                var v = Build(def.spriteAsset, halfW, halfH, threshold, softness, fitMode);
                e.snap = new Snapshot(key, v);
                return v;
            }
        }

        /// <summary>Drop the cached entry for one def (the next Get rebuilds). Exposed for the probe/audit surface
        /// — ordinary authoring never needs to call this, since the content hash already invalidates on edit.</summary>
        public static void Invalidate(ShaperPrimitiveDef def) => _entries.Remove(def);

        static int KeyOf(Sprite sprite, float halfW, float halfH, float threshold, float softness, ShaperSpriteFitMode fitMode)
        {
            unchecked
            {
                int h = sprite.GetInstanceID();
                h = (h ^ Mathf.RoundToInt(halfW * 100f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(halfH * 100f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(threshold * 1000f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(softness * 100f)) * 16777619;
                h = (h ^ (int)fitMode) * 16777619;
                return h;
            }
        }

        static ShaperCompiledSpriteField Build(Sprite sprite, float halfW, float halfH, float threshold,
            float softness, ShaperSpriteFitMode fitMode)
        {
            Texture2D srcTex = sprite.texture;
            Rect srcRect = sprite.textureRect;   // pixel rect within srcTex, bottom-left origin — GL convention.
            float srcAspect = srcRect.height > 0f ? srcRect.width / srcRect.height : 1f;

            // The AUTHORED box the sprite maps onto — Uniform fits the sprite's own aspect inside it (letterboxed);
            // Stretch is the box itself, unmodified. Either way bx/by is the raster's own OWN half-extent, which
            // may be smaller than the authored halfW/halfH under Uniform — a point between the fitted raster and
            // the authored box's edge samples the raster CLAMPED at its own boundary (ShaperEvaluator's
            // SampleSpriteDistance), the same posture EmitComposite's clamp already takes for its own box.
            float bx, by;
            if (fitMode == ShaperSpriteFitMode.Stretch) { bx = halfW; by = halfH; }
            else
            {
                float boxAspect = halfH > 1e-6f ? halfW / halfH : 1f;
                if (srcAspect >= boxAspect) { bx = halfW; by = srcAspect > 1e-6f ? halfW / srcAspect : halfH; }
                else { by = halfH; bx = halfH * srcAspect; }
            }
            bx = Mathf.Max(1e-4f, bx);
            by = Mathf.Max(1e-4f, by);

            // Bake resolution chosen so the grid's texels are SQUARE in local units (bw/bh ~= bx/by) — required
            // for the distance transform, which is run in texel space and then converted to local units by one
            // scalar texel size; unequal texel aspect would make that single scalar wrong on one axis.
            int bw, bh;
            if (bx >= by) { bw = BakeMaxDim; bh = Mathf.Clamp(Mathf.RoundToInt(BakeMaxDim * (by / bx)), MinBakeDim, BakeMaxDim); }
            else { bh = BakeMaxDim; bw = Mathf.Clamp(Mathf.RoundToInt(BakeMaxDim * (bx / by)), MinBakeDim, BakeMaxDim); }

            float[] alpha = ReadAlpha(srcTex, srcRect, bw, bh);

            // Softness blurs the ALPHA before thresholding — it softens the boundary the distance transform is
            // built from, not just a display-time antialiasing band (which ShaperField.Coverage/HalfBand already
            // supplies generically at render time regardless of source kind).
            float texelSize = 2f * bx / bw;   // ~= 2*by/bh by construction above.
            int blurRadius = softness > 0f ? Mathf.Clamp(Mathf.RoundToInt(softness / Mathf.Max(1e-6f, texelSize)), 1, MaxBlurRadiusTexels) : 0;
            if (blurRadius > 0) alpha = BoxBlur(alpha, bw, bh, blurRadius);

            bool[] inside = new bool[bw * bh];
            for (int i = 0; i < inside.Length; i++) inside[i] = alpha[i] >= threshold;

            float[] toInside = EDT2D(inside, bw, bh, true);    // 0 at inside pixels, distance-to-inside elsewhere
            float[] toOutside = EDT2D(inside, bw, bh, false);  // 0 at outside pixels, distance-to-outside elsewhere

            var distance = new float[bw * bh];
            for (int i = 0; i < distance.Length; i++)
                distance[i] = (Mathf.Sqrt(toInside[i]) - Mathf.Sqrt(toOutside[i])) * texelSize;

            return new ShaperCompiledSpriteField { width = bw, height = bh, halfExtentX = bx, halfExtentY = by, distance = distance };
        }

        /// <summary>
        /// Reads <paramref name="srcRect"/> of <paramref name="srcTex"/>'s alpha channel, resampled to
        /// <paramref name="bw"/>x<paramref name="bh"/>, via a blit to a readable RenderTexture rather than
        /// <c>GetPixels</c> on the source — the source is very often import-marked non-readable (an atlas'd
        /// sprite sheet, almost always), and <c>Graphics.Blit</c> needs no CPU read access on its SOURCE, only
        /// the temporary destination this method creates and reads back itself.
        /// </summary>
        static float[] ReadAlpha(Texture2D srcTex, Rect srcRect, int bw, int bh)
        {
            var result = new float[bw * bh];
            if (srcTex == null) return result;

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(bw, bh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            rt.filterMode = FilterMode.Bilinear;
            Vector2 uvScale = new Vector2(srcRect.width / srcTex.width, srcRect.height / srcTex.height);
            Vector2 uvOffset = new Vector2(srcRect.x / srcTex.width, srcRect.y / srcTex.height);
            Graphics.Blit(srcTex, rt, uvScale, uvOffset);

            Texture2D readTex = new Texture2D(bw, bh, TextureFormat.RGBA32, false, false);
            RenderTexture.active = rt;
            readTex.ReadPixels(new Rect(0, 0, bw, bh), 0, 0, false);
            readTex.Apply(false, false);
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            Color32[] px = readTex.GetPixels32();
            Object.DestroyImmediate(readTex);

            for (int i = 0; i < px.Length && i < result.Length; i++) result[i] = px[i].a / 255f;
            return result;
        }

        /// <summary>Separable box blur, clamped at the raster edge — deterministic, no allocation beyond the two
        /// pass buffers, and never bilinear-samples outside the grid.</summary>
        static float[] BoxBlur(float[] src, int w, int h, int radius)
        {
            var tmp = new float[w * h];
            var dst = new float[w * h];
            int span = radius * 2 + 1;

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                        sum += src[row + Mathf.Clamp(x + k, 0, w - 1)];
                    tmp[row + x] = sum / span;
                }
            }
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                        sum += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
                    dst[y * w + x] = sum / span;
                }
            }
            return dst;
        }

        /// <summary>
        /// Exact squared-then-square-rooted 2D Euclidean distance transform of a binary mask, Felzenszwalt &amp;
        /// Huttenlocher's two-pass lower-envelope-of-parabolas method (each pass exactly Lipschitz-1 in texel
        /// units by construction — the classical result this algorithm is chosen for). Seeds at
        /// <paramref name="seedValue"/> pixels (<c>f = 0</c>), infinity elsewhere; returns the SQUARED distance
        /// (callers take the square root once, after combining the two runs).
        /// </summary>
        static float[] EDT2D(bool[] mask, int w, int h, bool seedValue)
        {
            const float Inf = 1e20f;
            var f = new float[w * h];
            for (int i = 0; i < f.Length; i++) f[i] = mask[i] == seedValue ? 0f : Inf;

            var g = new float[w * h];
            var row = new float[Mathf.Max(w, h)];
            var rowOut = new float[Mathf.Max(w, h)];

            // Pass 1: transform each ROW along X.
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) row[x] = f[y * w + x];
                EDT1D(row, w, rowOut);
                for (int x = 0; x < w; x++) g[y * w + x] = rowOut[x];
            }
            // Pass 2: transform each COLUMN along Y, over pass 1's result.
            var outArr = new float[w * h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) row[y] = g[y * w + x];
                EDT1D(row, h, rowOut);
                for (int y = 0; y < h; y++) outArr[y * w + x] = rowOut[y];
            }
            return outArr;
        }

        /// <summary>The 1D lower-envelope pass. <paramref name="f"/>'s first <paramref name="n"/> entries are the
        /// input; the squared transform is written into <paramref name="outArr"/>'s first <paramref name="n"/>.</summary>
        static void EDT1D(float[] f, int n, float[] outArr)
        {
            var v = new int[n];
            var z = new float[n + 1];
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s;
                while (true)
                {
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                    if (s <= z[k]) { k--; if (k < 0) { k = 0; break; } }
                    else break;
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                float dq = q - v[k];
                outArr[q] = dq * dq + f[v[k]];
            }
        }
    }
}
