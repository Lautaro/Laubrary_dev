// T-0174 — a Text primitive's glyphs become a real signed distance field, baked once per authoring state and
// cached. Copies T-0175's ShaperSpriteField pattern exactly (ConditionalWeakTable keyed by owner identity,
// one immutable snapshot swapped under a content-hash key, an exact Euclidean distance transform of a binary
// mask) because a Text source is the same KIND of thing as a Sprite source: a picture with no analytic SDF.
//
// Why text is a SHAPE SOURCE and not a composite: a glyph is a region of the plane, so once its signed distance
// exists every downstream stage — fills, borders, the height stage, the light rig — applies to it unchanged and
// with no per-stage knowledge that the shape came from a font. Pyre's Text form had to reimplement fill, border
// and extrusion for itself (Pyre.cs:397-425 authors textFill/textSolid/textDepth beside the shared ones);
// nothing here does.
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace Laubrary.Shaper
{
    /// <summary>How a multi-line string's lines sit relative to each other. APPEND-ONLY (serialized as an int).</summary>
    public enum ShaperTextAlign
    {
        Left = 0,
        Centre = 1,
        Right = 2,
    }

    /// <summary>
    /// One Text primitive's baked picture: a signed distance raster in the node's own local units, negative
    /// inside, covering <c>[-halfExtentX, halfExtentX] x [-halfExtentY, halfExtentY]</c> — the same "bulk data
    /// referenced by index, never a managed object in the op" shape <see cref="ShaperCompiledSpriteField"/> and
    /// <see cref="ShaperCompiledComposite"/> already use.
    /// </summary>
    public sealed class ShaperCompiledTextField
    {
        public int width, height;
        public float halfExtentX, halfExtentY;

        /// <summary>Signed distance in LOCAL units, row-major, row 0 = bottom. Negative inside. From an exact
        /// Euclidean distance transform, so it is a real distance everywhere on the raster — which is what lets
        /// a border and a wide soft-combine work on text, unlike a composite's saturating pseudo-distance.</summary>
        public float[] distance;

        /// <summary>
        /// Which GLYPH each texel belongs to, in draw order (0..<see cref="glyphCount"/>−1), or −1 for a texel no
        /// glyph's own SDF cell reaches. This is the "per-glyph instance index published" the task asks for: it is
        /// the raster channel a per-character fill (Pyre's PerCharStep, or an IndexedStrip parameterised by
        /// character rather than by angle/projection) selects its palette slot from.
        ///
        /// <b>Status, stated plainly.</b> The fill stage's per-sample inputs are the nine declared quantities of
        /// <see cref="ShaperQuantitySet"/> (ShaperFillContract.cs:401-419) and character index is not one of them,
        /// so no fill kind consumes this channel TODAY — publishing it is what this task delivers, and a tenth
        /// quantity plus its ShaperFillOps case is what would consume it. A per-character GRADIENT needs nothing
        /// new: Pyre's own PerCharGradient is a gradient swept in SPACE across the line (Pyre.cs:404-410), which
        /// an ordinary Linear/Angular Gradient fill over this node's local box already is.
        /// </summary>
        public int[] glyphIndex;

        /// <summary>How many glyphs were actually drawn (whitespace advances the pen but draws nothing).</summary>
        public int glyphCount;

        /// <summary>How many lines the string laid out onto.</summary>
        public int lineCount;

        /// <summary>The glyph index at a LOCAL-frame point, or −1 outside every glyph's cell. Nearest-texel, not
        /// interpolated: an index is a label, and lerping two labels names a glyph that is not there.</summary>
        public int GlyphIndexAt(float lx, float ly)
        {
            if (glyphIndex == null || width <= 0 || height <= 0) return -1;
            float u = halfExtentX > 1e-9f ? (lx + halfExtentX) / (2f * halfExtentX) : 0.5f;
            float v = halfExtentY > 1e-9f ? (ly + halfExtentY) / (2f * halfExtentY) : 0.5f;
            int ix = Mathf.Clamp(Mathf.FloorToInt(u * width), 0, width - 1);
            int iy = Mathf.Clamp(Mathf.FloorToInt(v * height), 0, height - 1);
            return glyphIndex[iy * width + ix];
        }
    }

    /// <summary>
    /// The exact Euclidean distance transform, as a named shared utility rather than a private copy inside one
    /// source's prepass. Felzenszwalb &amp; Huttenlocher's two-pass lower-envelope-of-parabolas method: exact,
    /// deterministic, and Lipschitz-1 in texel units by construction, which is the property every raster-backed
    /// shape source's declared bound rests on.
    /// </summary>
    public static class ShaperDistanceTransform
    {
        /// <summary>
        /// Signed distance, in the caller's units, of the binary <paramref name="inside"/> mask. Negative inside.
        /// One texel is <paramref name="texelSize"/> units on BOTH axes — the caller must bake a grid whose texels
        /// are square, because the transform runs in texel space and converts back with this one scalar.
        /// </summary>
        public static float[] Signed(bool[] inside, int w, int h, float texelSize)
        {
            float[] toInside = Squared(inside, w, h, true);    // 0 at inside texels, distance-to-inside elsewhere
            float[] toOutside = Squared(inside, w, h, false);  // 0 at outside texels, distance-to-outside elsewhere
            var d = new float[w * h];
            for (int i = 0; i < d.Length; i++)
                d[i] = (Mathf.Sqrt(toInside[i]) - Mathf.Sqrt(toOutside[i])) * texelSize;
            return d;
        }

        /// <summary>The SQUARED distance in texels to the nearest texel whose mask equals <paramref name="seedValue"/>.</summary>
        public static float[] Squared(bool[] mask, int w, int h, bool seedValue)
        {
            const float Inf = 1e20f;
            var f = new float[w * h];
            for (int i = 0; i < f.Length; i++) f[i] = mask[i] == seedValue ? 0f : Inf;

            var g = new float[w * h];
            var row = new float[Mathf.Max(w, h)];
            var rowOut = new float[Mathf.Max(w, h)];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) row[x] = f[y * w + x];
                Pass1D(row, w, rowOut);
                for (int x = 0; x < w; x++) g[y * w + x] = rowOut[x];
            }
            var outArr = new float[w * h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) row[y] = g[y * w + x];
                Pass1D(row, h, rowOut);
                for (int y = 0; y < h; y++) outArr[y * w + x] = rowOut[y];
            }
            return outArr;
        }

        static void Pass1D(float[] f, int n, float[] outArr)
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

    /// <summary>
    /// T-0174 — builds and caches a Text primitive's <see cref="ShaperCompiledTextField"/>.
    /// </summary>
    public static class ShaperTextPrepassCache
    {
        sealed class Snapshot { public readonly int key; public readonly ShaperCompiledTextField value; public Snapshot(int k, ShaperCompiledTextField v) { key = k; value = v; } }
        sealed class Entry { public volatile Snapshot snap; }
        static readonly ConditionalWeakTable<ShaperPrimitiveDef, Entry> _entries = new ConditionalWeakTable<ShaperPrimitiveDef, Entry>();

        /// <summary>Longer side of the bake grid, in texels. Larger than the Sprite path's 128 because a string
        /// spreads its detail across a wide raster — "BOOM" at 128 gives each letter about 30 texels of width,
        /// which visibly rounds a stem — and the transform's cost is linear in the texel count.</summary>
        const int BakeMaxDim = 192;
        const int MinBakeDim = 8;

        /// <summary>The blank margin baked around the text, as a fraction of the character size. The raster is the
        /// only place a distance exists (outside it the evaluator clamps at the edge), so a border, a shell or a
        /// soft combine can only reach as far as this margin. Half a character height is roughly the widest border
        /// anyone dials on text before it closes the counters of an O.</summary>
        const float MarginFraction = 0.5f;

        /// <summary>
        /// The cached raster for <paramref name="def"/> at the RESOLVED dial values (already sampled by the caller
        /// at the compile's own phase/seed — this method never reads a ZUIValue). Null when there is no font, no
        /// string, or no glyph in the string the font can draw: a legal, if useless, authoring state, the same
        /// posture <see cref="ShaperSpritePrepassCache"/> takes for an unassigned sprite.
        /// </summary>
        public static ShaperCompiledTextField Get(ShaperPrimitiveDef def, float size, float letterSpacing,
            float lineSpacing, float weight, ShaperTextAlign align)
        {
            if (def == null) return null;
            TMP_FontAsset font = ResolveFont(def.textFont);
            if (font == null || string.IsNullOrEmpty(def.textString)) return null;

            int key = KeyOf(font, def.textString, size, letterSpacing, lineSpacing, weight, align);
            var e = _entries.GetOrCreateValue(def);
            var s = e.snap;
            if (s != null && s.key == key) return s.value;
            lock (e)
            {
                s = e.snap;
                if (s != null && s.key == key) return s.value;
                var v = Build(font, def.textString, size, letterSpacing, lineSpacing, weight, align);
                e.snap = new Snapshot(key, v);
                return v;
            }
        }

        /// <summary>The authored font, or the project's TMP default when none is assigned — the same "a shape
        /// source with nothing picked is still worth drawing" convenience Pyre's Text form has (Pyre.cs:400-403),
        /// but resolved through TMP_Settings rather than an editor-only AssetDatabase scan, so it behaves the same
        /// in a build as in the window.</summary>
        public static TMP_FontAsset ResolveFont(TMP_FontAsset authored)
        {
            if (authored != null) return authored;
            return TMP_Settings.defaultFontAsset;
        }

        /// <summary>Drop the cached entry for one def (the next Get rebuilds). For the probe/audit surface only —
        /// ordinary authoring invalidates through the content hash.</summary>
        public static void Invalidate(ShaperPrimitiveDef def) => _entries.Remove(def);

        static int KeyOf(TMP_FontAsset font, string text, float size, float letterSpacing, float lineSpacing,
                         float weight, ShaperTextAlign align)
        {
            unchecked
            {
                int h = font.GetInstanceID();
                h = (h ^ StableHash(text)) * 16777619;
                h = (h ^ Mathf.RoundToInt(size * 100f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(letterSpacing * 100f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(lineSpacing * 100f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(weight * 1000f)) * 16777619;
                h = (h ^ (int)align) * 16777619;
                return h;
            }
        }

        /// <summary>
        /// FNV-1a over the string's chars. Written out rather than calling <c>string.GetHashCode</c> because that
        /// is explicitly not guaranteed stable across runtimes or runs, and a shape's cache key must mean the same
        /// thing every session — the same reason the rest of Shaper hashes its own values.
        /// </summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
                return (int)h;
            }
        }

        /// <summary>One glyph, already placed in text-local units. The RECT is the glyph's SDF cell — the bitmap
        /// rect grown by the atlas padding on all four sides — because that padding band is where the atlas holds
        /// the signed distance just OUTSIDE the glyph, and cropping to the bitmap rect would clip every stem.</summary>
        struct Placed
        {
            public int atlasIndex;
            public float x0, y0, x1, y1;              // local-unit cell
            public float ax0, ay0, ax1, ay1;          // atlas-texel cell (bottom-left origin)
        }

        static ShaperCompiledTextField Build(TMP_FontAsset font, string text, float size, float letterSpacing,
                                             float lineSpacing, float weight, ShaperTextAlign align)
        {
            FaceInfo face = font.faceInfo;
            float pointSize = face.pointSize > 0f ? face.pointSize : 1f;

            // `size` is the EM height in canvas units — the same meaning Pyre's Text form gives its own `size`
            // (Pyre.cs:398: "size is the character HEIGHT in pixels"), so a Pyre value ports across unchanged.
            float scale = size / pointSize;
            float pad = font.atlasPadding;
            float lineAdvance = (face.lineHeight > 0f ? face.lineHeight : pointSize) * scale + lineSpacing;

            var placed = new List<Placed>();
            var lineStart = new List<int>();   // index into `placed` of each line's first glyph
            var lineEnd = new List<int>();     // exclusive
            var lineMinX = new List<float>();
            var lineMaxX = new List<float>();

            float penX = 0f, penY = 0f;
            int lineFirst = 0;
            float minX = float.MaxValue, maxX = float.MinValue;

            for (int i = 0; i <= text.Length; i++)
            {
                bool endOfText = i == text.Length;
                char c = endOfText ? '\n' : text[i];

                if (c == '\r') continue;
                if (c == '\n')
                {
                    lineStart.Add(lineFirst);
                    lineEnd.Add(placed.Count);
                    lineMinX.Add(minX == float.MaxValue ? 0f : minX);
                    lineMaxX.Add(maxX == float.MinValue ? 0f : maxX);
                    if (endOfText) break;
                    lineFirst = placed.Count;
                    minX = float.MaxValue; maxX = float.MinValue;
                    penX = 0f;
                    penY -= lineAdvance;
                    continue;
                }

                if (!font.characterLookupTable.TryGetValue(c, out TMP_Character ch) || ch == null || ch.glyph == null)
                    continue;

                Glyph g = ch.glyph;
                GlyphMetrics gm = g.metrics;
                GlyphRect gr = g.glyphRect;
                float gs = scale * (g.scale > 0f ? g.scale : 1f);

                if (gm.width > 0f && gm.height > 0f)
                {
                    // The bitmap box in local units, then grown by the padding band on all four sides. One atlas
                    // texel is one font unit at the atlas' own point size, so the same `gs` converts both.
                    float bx0 = penX + gm.horizontalBearingX * gs;
                    float by0 = penY + (gm.horizontalBearingY - gm.height) * gs;
                    float bx1 = bx0 + gm.width * gs;
                    float by1 = by0 + gm.height * gs;

                    placed.Add(new Placed
                    {
                        atlasIndex = g.atlasIndex,
                        x0 = bx0 - pad * gs, y0 = by0 - pad * gs,
                        x1 = bx1 + pad * gs, y1 = by1 + pad * gs,
                        ax0 = gr.x - pad, ay0 = gr.y - pad,
                        ax1 = gr.x + gr.width + pad, ay1 = gr.y + gr.height + pad,
                    });

                    if (bx0 < minX) minX = bx0;
                    if (bx1 > maxX) maxX = bx1;
                }

                penX += gm.horizontalAdvance * gs + letterSpacing;
            }

            if (placed.Count == 0) return null;

            // ── alignment: shift each line so the chosen edge of every line agrees ────────────────────────────
            float widest = 0f;
            for (int L = 0; L < lineMinX.Count; L++) widest = Mathf.Max(widest, lineMaxX[L] - lineMinX[L]);
            for (int L = 0; L < lineStart.Count; L++)
            {
                float lineWidth = lineMaxX[L] - lineMinX[L];
                float shift = align == ShaperTextAlign.Left ? -lineMinX[L]
                            : align == ShaperTextAlign.Right ? (widest - lineWidth) - lineMinX[L]
                            : (widest - lineWidth) * 0.5f - lineMinX[L];
                for (int k = lineStart[L]; k < lineEnd[L]; k++)
                {
                    var p = placed[k];
                    p.x0 += shift; p.x1 += shift;
                    placed[k] = p;
                }
            }

            // ── the raster's box: every glyph cell, plus a blank margin the border/height stages can reach into ──
            float bbMinX = float.MaxValue, bbMinY = float.MaxValue, bbMaxX = float.MinValue, bbMaxY = float.MinValue;
            for (int k = 0; k < placed.Count; k++)
            {
                bbMinX = Mathf.Min(bbMinX, placed[k].x0); bbMaxX = Mathf.Max(bbMaxX, placed[k].x1);
                bbMinY = Mathf.Min(bbMinY, placed[k].y0); bbMaxY = Mathf.Max(bbMaxY, placed[k].y1);
            }
            float margin = size * MarginFraction;
            bbMinX -= margin; bbMaxX += margin; bbMinY -= margin; bbMaxY += margin;

            float cx = 0.5f * (bbMinX + bbMaxX);
            float cy = 0.5f * (bbMinY + bbMaxY);
            float bx = Mathf.Max(1e-4f, 0.5f * (bbMaxX - bbMinX));
            float by = Mathf.Max(1e-4f, 0.5f * (bbMaxY - bbMinY));

            // The node's own local ORIGIN is the text's centre, so rotating or scaling a Text node turns it about
            // the string rather than about wherever the first pen position happened to land.
            for (int k = 0; k < placed.Count; k++)
            {
                var p = placed[k];
                p.x0 -= cx; p.x1 -= cx; p.y0 -= cy; p.y1 -= cy;
                placed[k] = p;
            }

            int bw, bh;
            if (bx >= by) { bw = BakeMaxDim; bh = Mathf.Clamp(Mathf.RoundToInt(BakeMaxDim * (by / bx)), MinBakeDim, BakeMaxDim); }
            else { bh = BakeMaxDim; bw = Mathf.Clamp(Mathf.RoundToInt(BakeMaxDim * (bx / by)), MinBakeDim, BakeMaxDim); }
            float texelSize = 2f * bx / bw;

            // ── rasterise: the MAX of every glyph's own SDF, and which glyph won ──────────────────────────────
            // Max rather than a painter's-algorithm overwrite because two overlapping glyph cells (a tight
            // letterSpacing, an italic face) must UNION into one shape; taking the last one written would carve a
            // rectangular notch out of the earlier letter along its neighbour's cell edge.
            var best = new float[bw * bh];
            var owner = new int[bw * bh];
            for (int i = 0; i < best.Length; i++) { best[i] = 0f; owner[i] = -1; }

            for (int k = 0; k < placed.Count; k++)
            {
                Placed p = placed[k];
                float[] atlas = AtlasCache.Get(font, p.atlasIndex, out int aw, out int ah);
                if (atlas == null) continue;

                int ix0 = Mathf.Max(0, Mathf.FloorToInt((p.x0 + bx) / texelSize));
                int ix1 = Mathf.Min(bw - 1, Mathf.CeilToInt((p.x1 + bx) / texelSize));
                int iy0 = Mathf.Max(0, Mathf.FloorToInt((p.y0 + by) / texelSize));
                int iy1 = Mathf.Min(bh - 1, Mathf.CeilToInt((p.y1 + by) / texelSize));

                float cellW = Mathf.Max(1e-6f, p.x1 - p.x0);
                float cellH = Mathf.Max(1e-6f, p.y1 - p.y0);

                for (int iy = iy0; iy <= iy1; iy++)
                {
                    float ly = -by + (iy + 0.5f) * texelSize;
                    float v = (ly - p.y0) / cellH;
                    if (v < 0f || v > 1f) continue;
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        float lx = -bx + (ix + 0.5f) * texelSize;
                        float u = (lx - p.x0) / cellW;
                        if (u < 0f || u > 1f) continue;

                        float ax = Mathf.Lerp(p.ax0, p.ax1, u);
                        float ay = Mathf.Lerp(p.ay0, p.ay1, v);
                        float s = SampleBilinear(atlas, aw, ah, ax, ay);
                        int idx = iy * bw + ix;
                        if (s > best[idx]) { best[idx] = s; owner[idx] = k; }
                    }
                }
            }

            // `weight` is the SDF cut: TMP writes 0.5 at the glyph outline, so below 0.5 fattens the letters and
            // above 0.5 thins them. That is a real authoring dial (a free bold/light), not a technicality — and
            // it is why the atlas is thresholded rather than its distance read out directly: a TMP SDF saturates
            // a few texels past its padding band, whereas the transform below is exact across the whole raster,
            // which is what a border and the height stage need.
            var inside = new bool[bw * bh];
            for (int i = 0; i < inside.Length; i++) inside[i] = best[i] >= weight;

            return new ShaperCompiledTextField
            {
                width = bw,
                height = bh,
                halfExtentX = bx,
                halfExtentY = by,
                distance = ShaperDistanceTransform.Signed(inside, bw, bh, texelSize),
                glyphIndex = owner,
                glyphCount = placed.Count,
                lineCount = Mathf.Max(1, lineStart.Count),
            };
        }

        static float SampleBilinear(float[] a, int w, int h, float px, float py)
        {
            float fx = px - 0.5f, fy = py - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, h - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, w - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, h - 1);
            float tx = Mathf.Clamp01(fx - x0);
            float ty = Mathf.Clamp01(fy - y0);
            float top = Mathf.Lerp(a[y0 * w + x0], a[y0 * w + x1], tx);
            float bot = Mathf.Lerp(a[y1 * w + x0], a[y1 * w + x1], tx);
            return Mathf.Lerp(top, bot, ty);
        }

        /// <summary>
        /// One readback of each TMP atlas page, kept alive as long as the texture is. Read through a
        /// <c>Graphics.Blit</c> to a temporary RenderTexture rather than <c>GetPixels</c>, for the same reason
        /// <see cref="ShaperSpritePrepassCache"/> does it: a font atlas is very often import-marked non-readable,
        /// and Blit needs no CPU read access on its SOURCE.
        /// </summary>
        static class AtlasCache
        {
            sealed class Page { public float[] data; public int w, h; }
            static readonly ConditionalWeakTable<Texture, Page> _pages = new ConditionalWeakTable<Texture, Page>();

            public static float[] Get(TMP_FontAsset font, int atlasIndex, out int w, out int h)
            {
                w = h = 0;
                Texture2D tex = null;
                if (font.atlasTextures != null && atlasIndex >= 0 && atlasIndex < font.atlasTextures.Length)
                    tex = font.atlasTextures[atlasIndex];
                if (tex == null) tex = font.atlasTexture;
                if (tex == null) return null;

                var page = _pages.GetValue(tex, Read);
                if (page.data == null) return null;
                w = page.w; h = page.h;
                return page.data;
            }

            static Page Read(Texture key)
            {
                var tex = (Texture2D)key;
                int w = tex.width, h = tex.height;
                var page = new Page { w = w, h = h };

                RenderTexture prevActive = RenderTexture.active;
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                rt.filterMode = FilterMode.Point;
                Graphics.Blit(tex, rt);

                var readTex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                RenderTexture.active = rt;
                readTex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                readTex.Apply(false, false);
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);

                Color32[] px = readTex.GetPixels32();
                Object.DestroyImmediate(readTex);

                // Which channel carries the SDF depends on how the atlas was imported: an Alpha8 page holds it in
                // alpha, an R8/RGBA page in red. Read the SOURCE texture's format rather than guessing from the
                // blit result, whose unused channels are whatever the blit shader wrote.
                bool useAlpha = tex.format == TextureFormat.Alpha8;
                var data = new float[w * h];
                for (int i = 0; i < px.Length && i < data.Length; i++)
                    data[i] = (useAlpha ? px[i].a : px[i].r) / 255f;
                page.data = data;
                return page;
            }
        }
    }
}
