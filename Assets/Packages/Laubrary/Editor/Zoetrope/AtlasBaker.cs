using System.Collections.Generic;
using System.IO;
using Laubrary.Zoetrope;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// Bakes an animation's <see cref="FrameRef"/> recipe into a single self-contained, UNIFORM-size atlas
    /// texture and slices it into one sprite per frame. Every frame is trimmed to its content, then placed
    /// so its registration pivot lands at the SAME point in a common frame box — so all baked frames share
    /// one size and one pivot and play back perfectly aligned with no size "wobble". The result is portable
    /// (no dependency on the source rip sheet to play) and independent per animation (re-slicing a source
    /// sheet, or baking another animation, can never disturb these frames).
    /// </summary>
    public static class AtlasBaker
    {
        private const int ContentAlpha = 0; // a pixel counts as content when alpha > 0

        /// <summary>Round a pivot→content offset to whole source pixels with a SINGLE, consistent rule
        /// (round-half-UP). Used everywhere a registration offset is snapped — the bake AND the editor preview —
        /// so they are byte-identical. Crucially it is consistent: <see cref="Mathf.RoundToInt"/> is banker's
        /// rounding (half-to-even), so a 0.5px offset (frames of differing width parity) snaps in different
        /// directions depending on the integer part, leaving feet 1px apart that nudging can't reconcile. This
        /// always snaps the same way, so identical-feet frames register on the exact same pixel.</summary>
        public static int SnapOffset(float v) => Mathf.FloorToInt(v + 0.5f);

        /// <summary>
        /// How the uniform frame is sized. Default (<c>fixedSize</c> false) = AUTO: the frame grows to fit
        /// every frame's content around a shared registration point (the original behaviour). When
        /// <c>fixedSize</c> is true, every frame bakes into an exact <c>w</c>×<c>h</c> box with all content
        /// registered at <c>pivot</c> (normalized, bottom-left origin); content outside the box is clipped.
        /// </summary>
        public struct FrameBox
        {
            public bool fixedSize;
            public int w, h;
            public Vector2 pivot;
        }

        /// <summary>
        /// Bake <paramref name="recipe"/> into a horizontal-strip atlas PNG at <paramref name="atlasAssetPath"/>,
        /// slice it into uniform frames at <paramref name="ppu"/>, and return the resulting sprites in frame
        /// order. Returns null (with <paramref name="error"/> set) if there is nothing to bake or a source is
        /// unreadable. <paramref name="spriteBaseName"/> names the sub-sprites (<c>name_000</c>, …).
        /// </summary>
        public static List<Sprite> Bake(
            List<FrameRef> recipe, string atlasAssetPath, string spriteBaseName, float ppu, out string error,
            RegionSlicer.ColorKey key = default, FrameBox box = default)
        {
            error = null;
            if (!Compose(recipe, key, box, out var comp, out error)) return null;
            int stripW = comp.stripW, stripH = comp.stripH, fw = comp.fw, fh = comp.fh;
            var strip = comp.strip;
            Vector2 uniformPivot = comp.uniformPivot;

            // Write the PNG, import it, and slice it into uniform frames with the shared pivot.
            var tex = new Texture2D(stripW, stripH, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(strip);
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                string sysPath = ToSystemPath(atlasAssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(sysPath));
                File.WriteAllBytes(sysPath, png);
            }
            finally { Object.DestroyImmediate(tex); }

            AssetDatabase.ImportAsset(atlasAssetPath, ImportAssetOptions.ForceSynchronousImport);

            var rects = new List<Rect>(recipe.Count);
            var names = new List<string>(recipe.Count);
            var pivots = new List<Vector2>(recipe.Count);
            for (int i = 0; i < recipe.Count; i++)
            {
                rects.Add(new Rect(i * fw, 0, fw, fh));
                names.Add($"{spriteBaseName}_{i:000}");
                pivots.Add(uniformPivot);
            }

            RegionSlicer.Apply(atlasAssetPath, rects, pivots, ppu, idx => names[idx], uniformPivot);

            // Return in frame order (resolve by the names we just wrote).
            var byName = new Dictionary<string, Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(atlasAssetPath))
                if (o is Sprite sp) byName[sp.name] = sp;

            var result = new List<Sprite>(recipe.Count);
            for (int i = 0; i < names.Count; i++)
                result.Add(byName.TryGetValue(names[i], out var s) ? s : null);
            return result;
        }

        /// <summary>
        /// The CANONICAL per-frame registration used by the bake — exposed so editor PREVIEWS can render a
        /// frame identically (WYSIWYG). Trims <paramref name="cell"/> to its content (<paramref name="content"/>)
        /// and returns the pivot's offset from the content's bottom-left, in source px
        /// (<paramref name="pivotOffset"/>). The bake places content at this offset, ROUNDED to whole source
        /// pixels, from the shared anchor — so any preview that wants to match the bake must round the same way.
        /// A live preview that positions in screen space without this whole-source-pixel snap shows sub-pixel
        /// misalignment the baked atlas never has, which is exactly why the editor and the Zoe Browser
        /// preview disagreed. <paramref name="px"/> may be null (unreadable source) — then no trim happens.
        /// </summary>
        public static void FrameRegistration(
            Color32[] px, int texW, int texH, Rect cell, Vector2 pivot,
            RegionSlicer.ColorKey key, out Rect content, out Vector2 pivotOffset)
        {
            Rect b = cell;
            if (px != null)
            {
                b = RegionSlicer.TrimToContent(px, texW, texH, cell, ContentAlpha, out bool empty, key);
                if (empty) b = cell;
            }
            content = b;
            pivotOffset = new Vector2(
                cell.x + pivot.x * cell.width - b.x,
                cell.y + pivot.y * cell.height - b.y);
        }

        /// <summary>The composed uniform-frame strip plus its geometry — the output of the registration +
        /// trimming + packing that both the on-disk <see cref="Bake"/> and the in-memory
        /// <see cref="BakeInMemory"/> share, so a live editor preview is byte-identical to the shipped atlas.</summary>
        public struct Composition
        {
            public Color32[] strip; // bottom-left origin, row-major (matches Get/SetPixels32)
            public int stripW, stripH, fw, fh;
            public Vector2 uniformPivot; // normalized, shared by every frame
        }

        /// <summary>
        /// THE single registration+composition pass. Trims each frame to content, registers them around a
        /// shared anchor (auto-sized, or a fixed box) at whole-source-pixel offsets, and composes a uniform
        /// horizontal strip. This is the one place frame registration is decided — <see cref="Bake"/> writes it
        /// to a PNG, <see cref="BakeInMemory"/> turns it into runtime sprites; neither re-implements any of it.
        /// </summary>
        public static bool Compose(
            List<FrameRef> recipe, RegionSlicer.ColorKey key, FrameBox box, out Composition comp, out string error)
        {
            comp = default; error = null;
            if (recipe == null || recipe.Count == 0) { error = "no frames"; return false; }

            var pixelCache = new Dictionary<string, (Color32[] px, int w, int h)>();
            int n = recipe.Count;
            var blocks = new Color32[n][];   // each frame's (keyed, transformed) pixel block
            var blockW = new int[n];
            var blockH = new int[n];
            var bbox = new Rect[n];          // trimmed content bbox, in BLOCK coords
            var off = new Vector2[n];        // pivot offset from content bottom-left, block px
            float leftMax = 0, rightMax = 0, belowMax = 0, aboveMax = 0;
            bool any = false;

            for (int i = 0; i < n; i++)
            {
                var f = recipe[i];
                if (!TryGetPixels(f.sourceTextureGuid, pixelCache, out var px, out int w, out int h))
                { error = $"frame {i}: source texture {f.sourceTextureGuid} unreadable."; return false; }

                // Build this frame's block. Identity transform = the keyed cell pixels (byte-identical to the
                // old path); a non-identity transform mirrors/rotates/scales the pixels and maps the pivot.
                var block = TransformCell(px, w, h, f.cell, f.transform, key, f.pivot, out int tw, out int th, out Vector2 pvN);
                blocks[i] = block; blockW[i] = tw; blockH[i] = th;

                Rect b = TrimBlock(block, tw, th, out bool empty);
                if (empty) b = new Rect(0, 0, tw, th);
                bbox[i] = b;
                Vector2 o = new Vector2(pvN.x * tw - b.x, pvN.y * th - b.y);
                off[i] = o;

                leftMax = Mathf.Max(leftMax, o.x);
                rightMax = Mathf.Max(rightMax, b.width - o.x);
                belowMax = Mathf.Max(belowMax, o.y);
                aboveMax = Mathf.Max(aboveMax, b.height - o.y);
                any = true;
            }
            if (!any) { error = "no usable frames"; return false; }

            int fw, fh, anchorX, anchorY;
            if (box.fixedSize)
            {
                fw = Mathf.Max(1, box.w);
                fh = Mathf.Max(1, box.h);
                anchorX = SnapOffset(box.pivot.x * fw);
                anchorY = SnapOffset(box.pivot.y * fh);
            }
            else
            {
                fw = Mathf.Max(1, Mathf.CeilToInt(leftMax + rightMax));
                fh = Mathf.Max(1, Mathf.CeilToInt(belowMax + aboveMax));
                anchorX = SnapOffset(leftMax);
                anchorY = SnapOffset(belowMax);
            }

            int stripW = fw * n, stripH = fh;
            var strip = new Color32[stripW * stripH]; // default (0,0,0,0) transparent

            for (int i = 0; i < n; i++)
            {
                var block = blocks[i]; int tw = blockW[i], th = blockH[i];
                Rect b = bbox[i];
                int bx = Mathf.RoundToInt(b.x), by = Mathf.RoundToInt(b.y);
                int bcw = Mathf.RoundToInt(b.width), bch = Mathf.RoundToInt(b.height);

                int frameLeft = i * fw;
                int frameX0 = frameLeft + (anchorX - SnapOffset(off[i].x));
                int frameY0 = anchorY - SnapOffset(off[i].y);

                for (int y = 0; y < bch; y++)
                {
                    int sy = by + y, dy = frameY0 + y;
                    if (sy < 0 || sy >= th || dy < 0 || dy >= stripH) continue;
                    for (int x = 0; x < bcw; x++)
                    {
                        int sx = bx + x, dx = frameX0 + x;
                        if (sx < 0 || sx >= tw || dx < frameLeft || dx >= frameLeft + fw) continue;
                        Color32 c = block[sy * tw + sx]; // block already keyed → transparent = background
                        if (c.a > 0) strip[dy * stripW + dx] = c;
                    }
                }
            }

            comp = new Composition
            {
                strip = strip, stripW = stripW, stripH = stripH, fw = fw, fh = fh,
                uniformPivot = new Vector2((float)anchorX / fw, (float)anchorY / fh)
            };
            return true;
        }

        /// <summary>
        /// Extract a frame's source cell (keying the background out to transparency) and apply its
        /// <see cref="CellTransform"/> — flip, rotate (lossless 90° steps + optional arbitrary angle) and
        /// squash/stretch — returning the resulting RGBA block plus the pivot mapped into that block (so a
        /// "feet" pivot stays on the feet after the edit). Identity returns the keyed cell unchanged, so the
        /// no-transform path is byte-identical to before. Flips and exact 90° turns sample exactly (no blur);
        /// only an arbitrary angle or non-integer scale resamples, via nearest-neighbor (crisp) or, when
        /// <see cref="CellTransform.smooth"/> is set, bilinear.
        /// </summary>
        public static Color32[] TransformCell(
            Color32[] sheet, int texW, int texH, Rect cell, CellTransform t, RegionSlicer.ColorKey key,
            Vector2 pivotIn, out int outW, out int outH, out Vector2 pivotOut)
        {
            int cw = Mathf.Max(1, Mathf.RoundToInt(cell.width));
            int ch = Mathf.Max(1, Mathf.RoundToInt(cell.height));
            int cx = Mathf.RoundToInt(cell.x), cy = Mathf.RoundToInt(cell.y);

            var src = new Color32[cw * ch];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    int sx = cx + x, sy = cy + y;
                    Color32 c = (sx >= 0 && sx < texW && sy >= 0 && sy < texH) ? sheet[sy * texW + sx] : default;
                    src[y * cw + x] = key.IsBackground(c) ? new Color32(0, 0, 0, 0) : c;
                }
            if (key.enabled) src = ErodeKeyFringe(src, cw, ch);

            if (t.IsIdentity) { outW = cw; outH = ch; pivotOut = pivotIn; return src; }

            // One affine map handles flip + rot90 + arbitrary angle + scale (cell-centered, y-up).
            float sxScale = t.SX, syScale = t.SY;
            float fx = t.flipX ? -1f : 1f, fy = t.flipY ? -1f : 1f;
            int rot = ((t.rot90 % 4) + 4) % 4;
            float theta = (t.angle + 90f * rot) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(theta), sin = Mathf.Sin(theta);
            float hcw = cw * 0.5f, hch = ch * 0.5f;

            // forward: cell-centered (u,v) -> output-centered (X,Y)
            Vector2 Fwd(float u, float v)
            {
                float ax = u * fx * sxScale, ay = v * fy * syScale;
                return new Vector2(ax * cos - ay * sin, ax * sin + ay * cos);
            }
            Vector2 c0 = Fwd(-hcw, -hch), c1 = Fwd(hcw, -hch), c2 = Fwd(hcw, hch), c3 = Fwd(-hcw, hch);
            float minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
            float maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
            float minY = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y));
            float maxY = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y));
            outW = Mathf.Max(1, Mathf.CeilToInt(maxX - minX));
            outH = Mathf.Max(1, Mathf.CeilToInt(maxY - minY));

            // inverse maps (RGBA) for each output pixel back to a source pixel.
            float invSX = 1f / (fx * sxScale), invSY = 1f / (fy * syScale);
            var outPx = new Color32[outW * outH];
            for (int oy = 0; oy < outH; oy++)
                for (int ox = 0; ox < outW; ox++)
                {
                    float X = minX + ox + 0.5f, Y = minY + oy + 0.5f;     // output-centered
                    float rx = X * cos + Y * sin, ry = -X * sin + Y * cos; // R(-θ)
                    float u = rx * invSX, v = ry * invSY;                  // undo scale+flip
                    float sxF = u + hcw - 0.5f, syF = v + hch - 0.5f;      // source pixel coords
                    outPx[oy * outW + ox] = t.smooth
                        ? SampleBilinear(src, cw, ch, sxF, syF)
                        : SampleNearest(src, cw, ch, sxF, syF);
                }

            // map the pivot through the same forward transform
            float pu = pivotIn.x * cw - hcw, pv = pivotIn.y * ch - hch;
            Vector2 pf = Fwd(pu, pv);
            pivotOut = new Vector2((pf.x - minX) / outW, (pf.y - minY) / outH);
            return outPx;
        }

        /// <summary>
        /// Softens the 1-pixel-wide contaminated ring a hard colour-key cutoff leaves on anti-aliased source
        /// art: most ripped sheets have a rim of pixels around every sprite that are a genuine RGB blend of
        /// the sprite and the background colour, not a clean match to either — a hard key leaves them fully
        /// opaque and visibly tinted toward the background (the reported "bright outline"). Rather than trying
        /// to colour-correct that blend (which needs the true foreground colour and breaks down badly on
        /// high-contrast art — a saturated colour blended even slightly toward white already reads as very
        /// far from either endpoint), this fades the ring's ALPHA out proportionally to how close each pixel
        /// sits to a background pixel (Chebyshev distance, radius 1) — a purely spatial fix, independent of
        /// the sheet's actual colours, so it works the same regardless of contrast. A pixel exactly adjacent
        /// to background drops to ~1/3 alpha; the next ring in is untouched. Genuine interior content (no
        /// background neighbour within radius) is never touched.
        /// </summary>
        private static Color32[] ErodeKeyFringe(Color32[] px, int w, int h)
        {
            const int radius = 1;
            var result = (Color32[])px.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (px[i].a == 0) continue; // already background — nothing to fade

                    int minD = int.MaxValue;
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                            if (px[ny * w + nx].a == 0)
                            {
                                int d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                                if (d < minD) minD = d;
                            }
                        }
                    if (minD == int.MaxValue) continue; // no background within radius — untouched interior

                    float t = (float)minD / (radius + 1); // in (0, 1) since 1 <= minD <= radius
                    var c = px[i];
                    result[i] = new Color32(c.r, c.g, c.b, (byte)Mathf.RoundToInt(c.a * t));
                }
            return result;
        }

        private static Color32 SampleNearest(Color32[] px, int w, int h, float fx, float fy)
        {
            int x = Mathf.RoundToInt(fx), y = Mathf.RoundToInt(fy);
            return (x >= 0 && x < w && y >= 0 && y < h) ? px[y * w + x] : new Color32(0, 0, 0, 0);
        }

        private static Color32 SampleBilinear(Color32[] px, int w, int h, float fx, float fy)
        {
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            Color32 P(int x, int y) => (x >= 0 && x < w && y >= 0 && y < h) ? px[y * w + x] : new Color32(0, 0, 0, 0);
            Color32 a = P(x0, y0), b = P(x0 + 1, y0), c = P(x0, y0 + 1), d = P(x0 + 1, y0 + 1);
            float L(float c00, float c10, float c01, float c11) =>
                Mathf.Lerp(Mathf.Lerp(c00, c10, tx), Mathf.Lerp(c01, c11, tx), ty);
            return new Color32(
                (byte)Mathf.Clamp(L(a.r, b.r, c.r, d.r), 0, 255),
                (byte)Mathf.Clamp(L(a.g, b.g, c.g, d.g), 0, 255),
                (byte)Mathf.Clamp(L(a.b, b.b, c.b, d.b), 0, 255),
                (byte)Mathf.Clamp(L(a.a, b.a, c.a, d.a), 0, 255));
        }

        /// <summary>Tight content bbox of a standalone block (alpha &gt; 0; background already keyed out).</summary>
        private static Rect TrimBlock(Color32[] px, int w, int h, out bool empty)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > ContentAlpha)
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            if (maxX < minX) { empty = true; return new Rect(0, 0, w, h); }
            empty = false;
            return new Rect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
        }

        /// <summary>
        /// Bake <paramref name="recipe"/> to runtime sprites IN MEMORY (no asset written) — identical
        /// registration/trimming/packing to <see cref="Bake"/>, so an editor preview that plays these is
        /// pixel-identical to the shipped atlas and to in-game playback. The caller OWNS the returned texture
        /// and must <c>DestroyImmediate</c> it (and discard the sprites) when rebuilding. Returns null on error.
        /// </summary>
        public static List<Sprite> BakeInMemory(
            List<FrameRef> recipe, float ppu, out string error, out Texture2D atlas,
            RegionSlicer.ColorKey key = default, FrameBox box = default)
        {
            atlas = null;
            if (!Compose(recipe, key, box, out var c, out error)) return null;

            var tex = new Texture2D(c.stripW, c.stripH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(c.strip);
            tex.Apply();

            float p = ppu <= 0f ? 16f : ppu;
            var frames = new List<Sprite>(recipe.Count);
            for (int i = 0; i < recipe.Count; i++)
            {
                var sp = Sprite.Create(tex, new Rect(i * c.fw, 0, c.fw, c.fh), c.uniformPivot, p, 0, SpriteMeshType.FullRect);
                sp.name = $"preview_{i:000}";
                frames.Add(sp);
            }
            atlas = tex;
            return frames;
        }

        private static bool TryGetPixels(
            string guid, Dictionary<string, (Color32[] px, int w, int h)> cache, out Color32[] px, out int w, out int h)
        {
            px = null; w = h = 0;
            if (string.IsNullOrEmpty(guid)) return false;
            if (cache.TryGetValue(guid, out var hit)) { px = hit.px; w = hit.w; h = hit.h; return px != null; }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) { cache[guid] = (null, 0, 0); return false; }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && !importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) { cache[guid] = (null, 0, 0); return false; }
            try { px = tex.GetPixels32(); } catch { px = null; }
            w = tex.width; h = tex.height;
            cache[guid] = (px, w, h);
            return px != null;
        }

        private static string ToSystemPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
