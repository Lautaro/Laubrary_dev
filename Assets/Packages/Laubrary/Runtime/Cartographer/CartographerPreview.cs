using UnityEngine;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// The single tile-to-pixels path every Cartographer preview goes through — Clump thumbnails, Biome
    /// thumbnails, and (later) the authoring windows' live previews. Kept in one place on purpose: the package
    /// rule is that preview, bake and runtime never fork into separate renderers, so anything that needs to
    /// show tiles as pixels calls in here rather than growing its own copy.
    public static class CartographerPreview
    {
        /// Largest thumbnail this will build, per side. A clump big enough to exceed it gets no thumbnail
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
        /// clump of tiles that all declined still produces a sensibly-shaped (if empty) thumbnail.
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
        /// Does nothing if the sprite's texture is not CPU-readable — an unreadable atlas is a normal import
        /// setting, not an error worth throwing over.
        public static void Blit(Texture2D target, Sprite sprite, int x, int y)
        {
            if (target == null || sprite == null || sprite.texture == null) return;
            if (!sprite.texture.isReadable) return;

            var r = sprite.textureRect;
            int sw = Mathf.RoundToInt(r.width), sh = Mathf.RoundToInt(r.height);
            if (sw <= 0 || sh <= 0) return;

            Color[] src;
            try { src = sprite.texture.GetPixels(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), sw, sh); }
            catch { return; }

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
    }
}
