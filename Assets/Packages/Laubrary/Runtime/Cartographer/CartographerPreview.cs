using UnityEngine;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// The single tile-to-pixels path every Cartographer preview goes through — Prop thumbnails, Biome
    /// thumbnails, and (later) the authoring windows' live previews. Kept in one place on purpose: the package
    /// rule is that preview, bake and runtime never fork into separate renderers, so anything that needs to
    /// show tiles as pixels calls in here rather than growing its own copy.
    public static class CartographerPreview
    {
        /// Largest thumbnail this will build, per side. A prop big enough to exceed it gets no thumbnail
        /// rather than a multi-megabyte allocation on every browser repaint.
        public const int MaxSide = 1024;

        /// The sprite a tile draws with, or null if it can't be determined without a live Tilemap.
        /// Plain Tiles answer directly; rule tiles are asked for their default data and may decline, since
        /// their real sprite depends on neighbours that don't exist in a thumbnail.
        public static Sprite SpriteOf(TileBase tile)
        {
            if (tile == null) return null;
            if (tile is Tile plain) return plain.sprite;

            try
            {
                var data = new TileData();
                tile.GetTileData(Vector3Int.zero, null, ref data);
                return data.sprite;
            }
            catch
            {
                return null;
            }
        }

        /// Pixel size of one grid cell, taken from the first sprite that can supply one. Falls back to 16 so a
        /// prop of tiles that all declined still produces a sensibly-shaped (if empty) thumbnail.
        ///
        /// ☠️ `rect`, NOT `textureRect`. Unity's importer defaults to a TIGHT sprite mesh, which trims
        /// transparent borders: a 32×32 tile whose art occupies the bottom 21 rows reports `rect` 32×32 but
        /// `textureRect` 32×21. Sizing a cell from the trimmed rect makes the cell as small as the art in it,
        /// so every tile that does not touch all four edges of its cell lands at a different scale and
        /// position from its neighbours. `rect` is the DECLARED cell and is what the grid is made of.
        public static int CellPixels(Sprite reference)
        {
            if (reference == null) return 16;
            int w = Mathf.RoundToInt(reference.rect.width);
            int h = Mathf.RoundToInt(reference.rect.height);
            int side = Mathf.Max(w, h);
            return side > 0 ? side : 16;
        }

        /// A transparent RGBA32 canvas at point filter — the shape every Cartographer thumbnail starts from.
        public static Texture2D NewCanvas(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear = new Color32[width * height];
            tex.SetPixels32(clear);
            return tex;
        }

        /// Draw one sprite into `target` with its DECLARED cell's bottom-left corner at (x, y), skipping fully
        /// transparent source pixels so overlapping layers composite instead of punching holes in each other.
        /// A non-CPU-readable texture — the DEFAULT for imported art — is read through PreviewTex's
        /// RenderTexture path instead of being silently skipped, so thumbnails survive meeting real content.
        ///
        /// ☠️ `rect`, NEVER `textureRect`. This is a GRID compositor: every cell is the same size and each
        /// tile owns exactly one, so the source must be the sprite's DECLARED slice — margins included —
        /// placed at the cell's corner. `textureRect` is the tight-mesh OUTLINE's bounding box, and with the
        /// importer's default mesh Unity returns it FRACTIONAL and trigonometric (real values from this
        /// project: 27.92388 = 32·cos22.5°, 21.70711 = 32·(1/√2)+…). Reading that rect crops the art; adding
        /// `textureRectOffset` back only re-places a crop that should never have been taken, and rounds a
        /// value that was never a whole pixel. A shelf composed that way comes out stepped and gappy while
        /// the same tiles render correctly in a Tilemap. Diagnosed 2026-08-03 from the MetaMapper clump
        /// backdrop; the bug was in every level, prop, clump and tileset preview.
        /// `cellPx` > 0 scales the tile to fill exactly that square, nearest-neighbour.
        ///
        /// ☠️ A TILE IS ONE CELL WHATEVER ITS RESOLUTION. A level may legitimately mix a 16-pixel tileset
        /// with a 32-pixel one — at runtime each sprite carries its own PPU, so both occupy one cell and it
        /// looks right. A composite preview has ONE pixel size for the whole image, so blitting each tile at
        /// its NATIVE size puts a 16px tile into the corner of a 32px block and leaves three quarters of the
        /// cell empty. Reported 2026-08-03 as "a group of blue-ish tiles… now have 75% transparent area":
        /// 54 of that level's cells were 16px art in a 32px canvas. Scale, do not centre and do not pad —
        /// the cell is the unit.
        public static void Blit(Texture2D target, Sprite sprite, int x, int y, int cellPx = 0)
        {
            if (target == null || sprite == null || sprite.texture == null) return;

            var r = sprite.rect;
            int sw = Mathf.RoundToInt(r.width), sh = Mathf.RoundToInt(r.height);
            if (sw <= 0 || sh <= 0) return;

            Color[] src;
            if (sprite.texture.isReadable)
            {
                try { src = sprite.texture.GetPixels(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), sw, sh); }
                catch { return; }
            }
            else
            {
                // Read/Write is OFF by default on imported art, so this is the NORMAL path, not the edge
                // case. ReadRect takes the declared rect verbatim — CropSprite would hand back the tight
                // outline again and reintroduce the crop.
                var block = Laubrary.PreviewKit.PreviewTex.ReadRect(sprite.texture, r);
                if (block == null) return;
                sw = block.width;
                sh = block.height;
                src = block.GetPixels();
                Object.DestroyImmediate(block);
            }

            // Destination extent: the cell when one was given, otherwise the sprite's own size.
            int dw = cellPx > 0 ? cellPx : sw;
            int dh = cellPx > 0 ? cellPx : sh;

            for (int py = 0; py < dh; py++)
            {
                int ty = y + py;
                if (ty < 0 || ty >= target.height) continue;
                int sy = dh == sh ? py : py * sh / dh;      // nearest-neighbour: pixel art never interpolates

                for (int px = 0; px < dw; px++)
                {
                    int tx = x + px;
                    if (tx < 0 || tx >= target.width) continue;
                    int sx = dw == sw ? px : px * sw / dw;

                    var c = src[sy * sw + sx];
                    if (c.a <= 0f) continue;
                    target.SetPixel(tx, ty, c);
                }
            }
        }

        /// The sprite a resolved cell draws with — a LevelTile answers for its stored variant (or its first
        /// animation frame), anything else through SpriteOf.
        public static Sprite SpriteOfResolved(in ResolvedCell cell)
        {
            if (cell.tile is LevelTile lt)
                return lt.IsAnimated ? lt.animation[0] : lt.SpriteOfVariant(cell.variant);
            return SpriteOf(cell.tile);
        }

        /// Rasterise a level for its thumbnail: visible layers in sorting order, each cell's sprite at 1:1.
        /// A level too large to fit MaxSide renders a CENTRED CROP at full cell size rather than scaling the
        /// whole thing to mush — a legible corner identifies a level; a grey smear does not.
        public static Texture2D RenderLevel(LevelAsset level)
        {
            if (level == null || level.layers == null || level.layers.Count == 0) return null;

            var ordered = new System.Collections.Generic.List<LevelLayer>();
            foreach (var l in level.layers) if (l != null && l.visible) ordered.Add(l);
            if (ordered.Count == 0) return null;
            ordered.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));

            var resolved = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<Vector2Int, ResolvedCell>>();
            Sprite reference = null;
            foreach (var l in ordered)
            {
                var cells = level.ResolveLayer(l);
                resolved.Add(cells);
                if (reference == null)
                    foreach (var rc in cells.Values)
                    {
                        reference = SpriteOfResolved(rc);
                        if (reference != null) break;
                    }
            }
            if (reference == null) return null;

            int cellPx = CellPixels(reference);
            var b = level.bounds;
            int maxCells = Mathf.Max(1, MaxSide / Mathf.Max(1, cellPx));
            int cw = Mathf.Min(b.width, maxCells), ch = Mathf.Min(b.height, maxCells);
            if (cw <= 0 || ch <= 0) return null;
            var window = new RectInt(b.xMin + (b.width - cw) / 2, b.yMin + (b.height - ch) / 2, cw, ch);

            var tex = NewCanvas(cw * cellPx, ch * cellPx);
            foreach (var cells in resolved)
            {
                foreach (var kv in cells)
                {
                    if (!window.Contains(kv.Key)) continue;
                    var s = SpriteOfResolved(kv.Value);
                    if (s == null) continue;
                    Blit(tex, s, (kv.Key.x - window.xMin) * cellPx, (kv.Key.y - window.yMin) * cellPx);
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
