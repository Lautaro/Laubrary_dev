using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// UI-free core of Track B's assisted-manual "Region Slicer". An IRREGULAR display
    /// sheet (e.g. a Spriters-Resource rip) has no single uniform grid: different
    /// vertically-stacked animation groups use different sprite sizes. The user marquees
    /// each group as a box and gives it its OWN cols/rows; every box expands into a set of
    /// equally-sized cell rects. This class owns (a) expanding one <see cref="RegionSpec"/>
    /// into cell rects and (b) applying an accumulated UNION of rects to a texture importer
    /// via the modern ISpriteEditorDataProvider — exactly like <see cref="GridSlicer.Slice"/>.
    ///
    /// CRITICAL: SetSpriteRects REPLACES the full set, so callers must pass the union of
    /// every committed region in one <see cref="Apply"/> call (the window keeps the master
    /// accumulated list and passes it whole).
    /// </summary>
    public static class RegionSlicer
    {
        /// <summary>
        /// An optional "background color key": some ripped sheets have no alpha — they use a SOLID colour
        /// (magenta, black, cyan…) as the background. When <see cref="enabled"/>, any pixel whose RGB is
        /// within <see cref="tolerance"/> (per channel) of <see cref="color"/> is treated as transparent by
        /// every content test below (and baked out to real transparency by <see cref="AtlasBaker"/>).
        /// </summary>
        public struct ColorKey
        {
            public bool enabled;
            public Color32 color;
            public int tolerance;   // per-channel max abs difference (0..255)

            public bool IsBackground(Color32 p)
                => enabled
                   && Mathf.Abs(p.r - color.r) <= tolerance
                   && Mathf.Abs(p.g - color.g) <= tolerance
                   && Mathf.Abs(p.b - color.b) <= tolerance;
        }

        /// <summary>A pixel counts as sprite CONTENT when it is opaque enough AND not the background key.</summary>
        public static bool IsContent(Color32 p, int alphaThreshold, ColorKey key)
            => p.a > alphaThreshold && !key.IsBackground(p);

        /// <summary>
        /// One marquee'd region plus its own grid. Coordinates are TEXTURE PIXELS with a
        /// bottom-left origin (matching sprite Rect convention), so a region built from
        /// screen interaction must already be converted to bottom-left space.
        /// </summary>
        /// <summary>How a region's box is divided into cells.</summary>
        public enum GridMode
        {
            FixedColsRows,  // divide the box into cols × rows equal cells
            FixedCellSize,  // tile the box with cells of a fixed pixel size (fit as many as possible)
        }

        public struct RegionSpec
        {
            public int boxX;        // left, texture px (bottom-left origin)
            public int boxY;        // bottom, texture px (bottom-left origin)
            public int boxW;        // box width, px
            public int boxH;        // box height, px
            public GridMode mode;
            public int cols;        // FixedColsRows: >= 1
            public int rows;        // FixedColsRows: >= 1
            public int cellW;       // FixedCellSize: cell width px, >= 1
            public int cellH;       // FixedCellSize: cell height px, >= 1
            public int spacingPx;   // gutter BETWEEN cells  (was "inner padding")
            public int paddingPx;   // shrink on every side of each cell  (was "inset")
        }

        /// <summary>
        /// Expand one region into its grid of cell rects (texture px, bottom-left origin).
        /// Uses FLOOR cell size after removing inner padding; any leftover px on the right/top
        /// are ignored (expected/fine for ragged sheets). Cells are emitted ROW-MAJOR in
        /// visual top-to-bottom, left-to-right order (top row first) so naming reads naturally.
        /// Degenerate boxes (zero/negative size) yield an empty list.
        /// </summary>
        public static List<Rect> ExpandRegion(RegionSpec spec)
        {
            var rects = new List<Rect>();
            if (spec.boxW <= 0 || spec.boxH <= 0) return rects;

            int spacing = Mathf.Max(0, spec.spacingPx);
            int padding = Mathf.Max(0, spec.paddingPx);

            int cols, rows, cellW, cellH;

            if (spec.mode == GridMode.FixedCellSize)
            {
                // Fixed cell size: tile as many cellW×cellH cells (with spacing gutters) as the box
                // holds; any leftover px on the right/top are ignored.
                cellW = Mathf.Max(1, spec.cellW);
                cellH = Mathf.Max(1, spec.cellH);
                cols = (spec.boxW + spacing) / (cellW + spacing);
                rows = (spec.boxH + spacing) / (cellH + spacing);
                if (cols <= 0 || rows <= 0) return rects;
            }
            else
            {
                // Fixed cols×rows: divide the box into equal cells after carving out the gutters (floored).
                cols = Mathf.Max(1, spec.cols);
                rows = Mathf.Max(1, spec.rows);
                int innerW = spec.boxW - spacing * (cols - 1);
                int innerH = spec.boxH - spacing * (rows - 1);
                if (innerW <= 0 || innerH <= 0) return rects;
                cellW = innerW / cols;
                cellH = innerH / rows;
                if (cellW <= 0 || cellH <= 0) return rects;
            }

            // Emit ROW-MAJOR, visual top-to-bottom, left-to-right. boxY is the bottom of the box
            // (bottom-left origin), so the TOP row's bottom edge is boxY + boxH - cellH.
            for (int r = 0; r < rows; r++)
            {
                int cellBottom = spec.boxY + spec.boxH - (r + 1) * cellH - r * spacing;
                for (int c = 0; c < cols; c++)
                {
                    int cellLeft = spec.boxX + c * (cellW + spacing);

                    float x = cellLeft + padding;
                    float y = cellBottom + padding;
                    float w = cellW - padding * 2;
                    float h = cellH - padding * 2;
                    if (w <= 0 || h <= 0) continue;

                    rects.Add(new Rect(x, y, w, h));
                }
            }

            return rects;
        }

        /// <summary>
        /// Configure the importer (Sprite / Multiple / Point / readable / no-mip / PPU) and
        /// write the FULL accumulated rect set in one SetSpriteRects call (it REPLACES the
        /// existing set). Names follow <paramref name="naming"/> which receives the rect's
        /// running index. Returns the resulting Sprite sub-assets (row-major).
        ///
        /// Every sprite gets ONE shared pivot (resolved from <paramref name="pivot"/>). For
        /// PER-FRAME registration (each rect its own pivot, so a ripped animation aligns), use
        /// the <see cref="Apply(string,List{Rect},List{Vector2},float,System.Func{int,string})"/>
        /// overload instead.
        /// </summary>
        public static List<Sprite> Apply(
            string assetPath,
            List<Rect> allRects,
            float ppu,
            GridSlicer.PivotMode pivot,
            Vector2 customPivot,
            System.Func<int, string> naming)
        {
            Vector2 pv = ResolvePivot(pivot, customPivot);
            return Apply(assetPath, allRects, null, ppu, naming, pv);
        }

        /// <summary>
        /// Per-frame-pivot overload. Writes <paramref name="allRects"/> where rect <c>i</c> gets
        /// pivot <c>perCellPivots[i]</c> (normalized 0..1 WITHIN that rect; bottom-left origin).
        /// This is the registration step that aligns frames ripped from an UNALIGNED sheet: each
        /// frame's pivot is nudged so its anchor (feet) lands at the same spot every frame, killing
        /// playback jitter. <paramref name="perCellPivots"/> may be null (falls back to
        /// <paramref name="fallbackPivot"/> for all) or shorter than <paramref name="allRects"/>
        /// (missing entries use the fallback). Alignment is always Custom.
        /// </summary>
        public static List<Sprite> Apply(
            string assetPath,
            List<Rect> allRects,
            List<Vector2> perCellPivots,
            float ppu,
            System.Func<int, string> naming,
            Vector2 fallbackPivot)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                throw new System.InvalidOperationException($"No TextureImporter at '{assetPath}'.");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.spritePixelsPerUnit = ppu <= 0 ? 16f : ppu;
            importer.mipmapEnabled = false;
            importer.isReadable = true;

            // CRITICAL: force FULL-RECT sprite meshes. Unity defaults to Tight, which builds a per-frame polygon
            // hugging the content — and those meshes round independently, so frames drift ~1px against each other
            // when swapped (the classic sprite-swap "wobble"). FullRect makes every frame a plain quad registered
            // purely by its pivot, matching AtlasBaker.BakeInMemory's Sprite.Create(FullRect) exactly — so the
            // baked asset (what the GAME and Reel Browser play) is identical to the in-memory preview.
            var texSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(texSettings);
            texSettings.spriteMeshType = SpriteMeshType.FullRect;
            texSettings.spriteExtrude = 0;
            importer.SetTextureSettings(texSettings);
            importer.SaveAndReimport();

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            // Preserve sprite IDs across re-slices: a sprite's GUID is what AnimationDef.frames reference, so
            // re-slicing this sheet to edit an animation must REUSE the prior GUID for a rect whose name
            // matches — otherwise every existing animation's frames dangle. Read the current name→GUID map and
            // reuse it; only mint new GUIDs for genuinely new names.
            var existingIds = new Dictionary<string, GUID>();
            var nameIdProviderRead = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameIdProviderRead != null)
                foreach (var pair in nameIdProviderRead.GetNameFileIdPairs())
                    existingIds[pair.name] = pair.GetFileGUID();

            var spriteRects = new List<SpriteRect>();
            for (int i = 0; i < allRects.Count; i++)
            {
                string name = naming != null
                    ? naming(i)
                    : $"{System.IO.Path.GetFileNameWithoutExtension(assetPath)}_{i:000}";
                Vector2 pv = (perCellPivots != null && i < perCellPivots.Count)
                    ? perCellPivots[i]
                    : fallbackPivot;
                spriteRects.Add(new SpriteRect
                {
                    name = name,
                    rect = allRects[i],
                    pivot = pv,
                    alignment = SpriteAlignment.Custom,
                    spriteID = existingIds.TryGetValue(name, out var prior) ? prior : GUID.Generate()
                });
            }

            provider.SetSpriteRects(spriteRects.ToArray());
            provider.Apply();

            // Persist name<->id mapping (required by the data provider contract).
            var nameIdProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameIdProvider != null)
            {
                var pairs = new List<SpriteNameFileIdPair>();
                foreach (var sr in spriteRects)
                    pairs.Add(new SpriteNameFileIdPair(sr.name, sr.spriteID));
                nameIdProvider.SetNameFileIdPairs(pairs);
            }

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            return GridSlicer.LoadSprites(assetPath);
        }

        /// <summary>
        /// Shrink a cell rect to the TIGHT bounding box of its non-transparent pixels. Scans only
        /// the pixels inside <paramref name="cell"/> (clamped to texture bounds), finding the bbox of
        /// pixels whose alpha &gt; <paramref name="alphaThreshold"/>. If the cell holds no such pixel,
        /// <paramref name="empty"/> is set true and the input cell is returned unchanged. Otherwise the
        /// returned rect is the trimmed box in texture px, bottom-left origin, integer-valued.
        ///
        /// <paramref name="px"/> is the texture's pixel buffer from <c>GetPixels32()</c>: row-major,
        /// bottom-left origin, index = y * <paramref name="texW"/> + x.
        /// </summary>
        public static Rect TrimToContent(Color32[] px, int texW, int texH, Rect cell, int alphaThreshold, out bool empty, ColorKey key = default)
        {
            empty = false;

            // Clamp the cell to texture bounds (integer px). yMax/xMax are EXCLUSIVE here.
            int x0 = Mathf.Clamp(Mathf.FloorToInt(cell.xMin), 0, texW);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(cell.yMin), 0, texH);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(cell.xMax), 0, texW);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(cell.yMax), 0, texH);

            if (px == null || x1 <= x0 || y1 <= y0)
            {
                empty = true;
                return cell;
            }

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int y = y0; y < y1; y++)
            {
                int rowBase = y * texW;
                for (int x = x0; x < x1; x++)
                {
                    if (IsContent(px[rowBase + x], alphaThreshold, key))
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX)
            {
                empty = true;
                return cell;
            }

            // bbox is inclusive of the max pixel, so width/height add 1.
            return new Rect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
        }

        /// <summary>
        /// Compute the per-cell pivot (normalized 0..1 within <paramref name="cell"/>, bottom-left
        /// origin) equal to the cell's CONTENT BASELINE — the horizontal center of the FEET (the bottom
        /// band of non-transparent pixels) at the content's bottom edge. This is the "align feet"
        /// registration: every frame's pivot is set to where its feet are, and because the renderer
        /// anchors each frame BY its pivot at the same screen point, the feet land identically — even
        /// when the frames differ in width (e.g. an arm reaching out). Centering on the WHOLE silhouette
        /// would let an asymmetric arm drag the anchor off the feet, so we deliberately use only the
        /// bottom band, which is the stable legs/feet that don't move between standing frames.
        ///
        /// Returns false (and leaves <paramref name="pivot"/> unchanged) when the cell holds no pixel
        /// above <paramref name="alphaThreshold"/> or is unreadable. <paramref name="px"/> is the
        /// <c>GetPixels32()</c> buffer: row-major, bottom-left origin, index = y*texW + x.
        /// </summary>
        public static bool ContentBaselinePivot(
            Color32[] px, int texW, int texH, Rect cell, int alphaThreshold, out Vector2 pivot, ColorKey key = default)
        {
            pivot = new Vector2(0.5f, 0f);
            Rect bbox = TrimToContent(px, texW, texH, cell, alphaThreshold, out bool empty, key);
            if (empty) return false;

            float w = Mathf.Max(1f, cell.width);
            float h = Mathf.Max(1f, cell.height);

            // Feet band: the bottom ~quarter of the content (min 2 rows). Its horizontal extent is the
            // legs/feet only, so its center is the feet center — independent of arm width higher up.
            int bbB = Mathf.FloorToInt(bbox.y);
            int bbH = Mathf.Max(1, Mathf.RoundToInt(bbox.height));
            int band = Mathf.Clamp(Mathf.RoundToInt(bbox.height * 0.25f), 2, bbH);
            int bandTop = Mathf.Min(texH, bbB + band);          // exclusive
            int bx0 = Mathf.Clamp(Mathf.FloorToInt(bbox.x), 0, texW);
            int bx1 = Mathf.Clamp(Mathf.CeilToInt(bbox.xMax), 0, texW);

            int feetMinX = int.MaxValue, feetMaxX = int.MinValue;
            for (int y = Mathf.Max(0, bbB); y < bandTop; y++)
            {
                int rowBase = y * texW;
                for (int x = bx0; x < bx1; x++)
                    if (IsContent(px[rowBase + x], alphaThreshold, key))
                    {
                        if (x < feetMinX) feetMinX = x;
                        if (x > feetMaxX) feetMaxX = x;
                    }
            }

            // Center of the feet band (fall back to whole-bbox center if the band somehow found nothing).
            float feetCx = feetMaxX >= feetMinX
                ? (feetMinX + feetMaxX + 1) * 0.5f
                : bbox.x + bbox.width * 0.5f;
            float contentBottom = bbox.y;
            pivot = new Vector2(
                Mathf.Clamp01((feetCx - cell.x) / w),
                Mathf.Clamp01((contentBottom - cell.y) / h));
            return true;
        }

        /// <summary>Pivot at the content's TOP-center (align heads): same as the baseline but anchored to the
        /// top edge of the trimmed content instead of the bottom. Useful for hanging/climbing where the head
        /// should stay put.</summary>
        public static bool ContentTopPivot(
            Color32[] px, int texW, int texH, Rect cell, int alphaThreshold, out Vector2 pivot, ColorKey key = default)
        {
            pivot = new Vector2(0.5f, 1f);
            Rect bbox = TrimToContent(px, texW, texH, cell, alphaThreshold, out bool empty, key);
            if (empty) return false;

            float w = Mathf.Max(1f, cell.width);
            float h = Mathf.Max(1f, cell.height);

            float contentCx = bbox.x + bbox.width * 0.5f;
            float contentTop = bbox.yMax; // bottom-left origin: yMax is the top edge
            pivot = new Vector2(
                Mathf.Clamp01((contentCx - cell.x) / w),
                Mathf.Clamp01((contentTop - cell.y) / h));
            return true;
        }

        /// <summary>
        /// Flood-fill the connected component of non-transparent pixels starting at
        /// (<paramref name="seedX"/>,<paramref name="seedY"/>) and return its tight bounding box
        /// (texture px, bottom-left origin, integer-valued). The fill is BOUNDED to
        /// <paramref name="box"/> (a marquee in texture px, bottom-left origin): pixels outside the
        /// box are never visited, so a single click extracts one sprite from a group without bleeding
        /// into neighbours. "Connected" uses 4-neighbour adjacency over pixels whose alpha &gt;
        /// <paramref name="alphaThreshold"/>.
        ///
        /// If the seed pixel itself is transparent (or the seed/box is out of range), the nearest
        /// content pixel is NOT searched — <paramref name="empty"/> is set true and a 1×1 rect at the
        /// seed is returned so the caller can decide what to do. <paramref name="px"/> is the buffer
        /// from <c>GetPixels32()</c>: row-major, bottom-left origin, index = y*<paramref name="texW"/>+x.
        /// </summary>
        public static Rect FloodFillBBox(
            Color32[] px, int texW, int texH, Rect box, int seedX, int seedY, int alphaThreshold, out bool empty, ColorKey key = default)
        {
            empty = false;

            // Clamp the bounding box to the texture (integer px, xMax/yMax EXCLUSIVE).
            int bx0 = Mathf.Clamp(Mathf.FloorToInt(box.xMin), 0, texW);
            int by0 = Mathf.Clamp(Mathf.FloorToInt(box.yMin), 0, texH);
            int bx1 = Mathf.Clamp(Mathf.CeilToInt(box.xMax), 0, texW);
            int by1 = Mathf.Clamp(Mathf.CeilToInt(box.yMax), 0, texH);

            bool SeedInvalid()
                => px == null || bx1 <= bx0 || by1 <= by0
                   || seedX < bx0 || seedX >= bx1 || seedY < by0 || seedY >= by1
                   || !IsContent(px[seedY * texW + seedX], alphaThreshold, key);

            if (SeedInvalid())
            {
                empty = true;
                return new Rect(seedX, seedY, 1, 1);
            }

            int boxW = bx1 - bx0;
            int boxH = by1 - by0;
            var visited = new bool[boxW * boxH];

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

            // Iterative 4-neighbour flood fill over the bounded region (explicit stack — sheets can be
            // large and recursion would risk a stack overflow).
            var stack = new System.Collections.Generic.Stack<int>(); // packs (y*texW + x)
            int VisIdx(int x, int y) => (y - by0) * boxW + (x - bx0);

            stack.Push(seedY * texW + seedX);
            visited[VisIdx(seedX, seedY)] = true;

            while (stack.Count > 0)
            {
                int packed = stack.Pop();
                int x = packed % texW;
                int y = packed / texW;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                // 4 neighbours, each bounded to the box + above-threshold + not yet visited.
                TryPush(x - 1, y);
                TryPush(x + 1, y);
                TryPush(x, y - 1);
                TryPush(x, y + 1);
            }

            void TryPush(int x, int y)
            {
                if (x < bx0 || x >= bx1 || y < by0 || y >= by1) return;
                int vi = VisIdx(x, y);
                if (visited[vi]) return;
                if (!IsContent(px[y * texW + x], alphaThreshold, key)) return;
                visited[vi] = true;
                stack.Push(y * texW + x);
            }

            // bbox is inclusive of the max pixel, so width/height add 1.
            return new Rect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
        }

        /// <summary>
        /// Heuristically guess a SOLID background colour for a rip that has no alpha. Returns false when the
        /// image already uses real transparency (so no colour key is needed) or when no single colour clearly
        /// dominates the border. The guess is the most common colour around the texture's 1px border — for a
        /// sprite sheet that border is the background almost by definition. <paramref name="px"/> is a
        /// <c>GetPixels32()</c> buffer (row-major, bottom-left origin); the result's alpha is forced to 255.
        /// </summary>
        /// <param name="alphaThreshold">Pixels with alpha ≤ this count as transparent.</param>
        /// <param name="borderDominance">The mode colour must be at least this fraction of border pixels (0..1).</param>
        /// <param name="maxTransparentFraction">If more than this fraction of the whole image is already
        /// transparent, the sheet uses real alpha and we return false (no key wanted).</param>
        public static bool TryDetectBackgroundColor(
            Color32[] px, int texW, int texH, out Color32 color,
            int alphaThreshold = 8, float borderDominance = 0.5f, float maxTransparentFraction = 0.02f)
        {
            color = new Color32(0, 0, 0, 255);
            if (px == null || texW <= 0 || texH <= 0 || px.Length < texW * texH) return false;

            // 1) Already has meaningful transparency? Then the background is alpha — don't impose a colour key.
            long transparent = 0;
            long total = (long)texW * texH;
            for (int i = 0; i < total; i++) if (px[i].a <= alphaThreshold) transparent++;
            if (transparent > total * maxTransparentFraction) return false;

            // 2) Tally colours around the 1px border; its mode is the background.
            var counts = new Dictionary<int, int>();
            int border = 0;
            void Tally(int x, int y)
            {
                Color32 p = px[y * texW + x];
                int key = (p.r << 16) | (p.g << 8) | p.b;
                counts.TryGetValue(key, out int c); counts[key] = c + 1; border++;
            }
            for (int x = 0; x < texW; x++) { Tally(x, 0); if (texH > 1) Tally(x, texH - 1); }
            for (int y = 1; y < texH - 1; y++) { Tally(0, y); if (texW > 1) Tally(texW - 1, y); }
            if (border == 0) return false;

            int bestKey = 0, bestCount = 0;
            foreach (var kv in counts) if (kv.Value > bestCount) { bestCount = kv.Value; bestKey = kv.Key; }
            if (bestCount < border * borderDominance) return false; // no clear background

            color = new Color32((byte)((bestKey >> 16) & 0xFF), (byte)((bestKey >> 8) & 0xFF), (byte)(bestKey & 0xFF), 255);
            return true;
        }

        /// <summary>Map a <see cref="GridSlicer.PivotMode"/> to a normalized pivot.</summary>
        public static Vector2 ResolvePivot(GridSlicer.PivotMode mode, Vector2 custom)
        {
            switch (mode)
            {
                case GridSlicer.PivotMode.BottomCenter: return new Vector2(0.5f, 0f);
                case GridSlicer.PivotMode.TopLeft: return new Vector2(0f, 1f);
                case GridSlicer.PivotMode.Custom: return custom;
                default: return new Vector2(0.5f, 0.5f);
            }
        }
    }
}
