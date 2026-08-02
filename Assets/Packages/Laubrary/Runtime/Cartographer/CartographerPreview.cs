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
        public static int CellPixels(Sprite reference)
        {
            if (reference == null) return 16;
            int w = Mathf.RoundToInt(reference.textureRect.width);
            int h = Mathf.RoundToInt(reference.textureRect.height);
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

        /// Draw one sprite into `target` with its bottom-left corner at (x, y), skipping fully transparent
        /// source pixels so overlapping layers composite instead of punching holes in each other.
        /// A non-CPU-readable texture — the DEFAULT for imported art — is read through PreviewTex's
        /// RenderTexture path instead of being silently skipped, so thumbnails survive meeting real content.
        public static void Blit(Texture2D target, Sprite sprite, int x, int y)
        {
            if (target == null || sprite == null || sprite.texture == null) return;

            var r = sprite.textureRect;
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
                var crop = Laubrary.PreviewKit.PreviewTex.CropSprite(sprite);
                if (crop == null) return;
                src = crop.GetPixels();
                Object.DestroyImmediate(crop);
            }

            for (int py = 0; py < sh; py++)
            {
                int ty = y + py;
                if (ty < 0 || ty >= target.height) continue;

                for (int px = 0; px < sw; px++)
                {
                    int tx = x + px;
                    if (tx < 0 || tx >= target.width) continue;

                    var c = src[py * sw + px];
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
