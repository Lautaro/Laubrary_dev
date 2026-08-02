using System.Collections.Generic;
using Laubrary.PreviewKit;
using UnityEngine;
using UnityEngine.Serialization;

namespace Laubrary.Cartographer
{
    /// One cell of a clump: a tile at a fixed offset within the clump's locked arrangement.
    [System.Serializable]
    public class ClumpCell
    {
        public Vector2Int offset;
        public LevelTile tile;

        [Tooltip("How many layers ABOVE the painted layer this cell lands when the clump is stamped. " +
                 "0 = the stamped layer itself; 1 = the next layer in front — overhangs and wall caps, " +
                 "which draw over actors and never collide. Relative on purpose: the genre decides what " +
                 "its layers mean, the clump only says 'one further front'.")]
        public int layerShift;
    }

    /// A CLUMP: a group of tiles that is placed like an OBJECT, never re-combined. Either a single
    /// transparent tile meant to layer on top of terrain (an urn), or a multi-tile arrangement that only
    /// ever appears in its authored pattern (a 3×1 door, a 1×3 wall column). Clumps keep object-shaped
    /// content out of the loose-tile palette, where mixed sizes would wreck the position-as-pattern grid.
    [System.Serializable]
    public class Clump
    {
        [Tooltip("Name shown in palettes and pickers.")]
        public string displayName = "Clump";

        [Tooltip("Anchor cell of this clump in the tileset's CLUMP grid — layout state, not level data.")]
        public Vector2Int gridPos;

        [Tooltip("The locked arrangement: each member tile at its offset from the anchor. Painting a clump " +
                 "stamps exactly this pattern.")]
        public List<ClumpCell> cells = new();

        /// The clump's footprint relative to its anchor (offsets can be any shape; this is the bounding box).
        public RectInt Bounds
        {
            get
            {
                if (cells == null || cells.Count == 0) return new RectInt(0, 0, 1, 1);
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var c in cells)
                {
                    minX = Mathf.Min(minX, c.offset.x); minY = Mathf.Min(minY, c.offset.y);
                    maxX = Mathf.Max(maxX, c.offset.x); maxY = Mathf.Max(maxY, c.offset.y);
                }
                return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
        }
    }

    /// The palette a layer paints from: an ordered set of LevelTiles that belong together. Biomes list
    /// tilesets, layers pick one, and the authoring window shows the active layer's tileset as its palette —
    /// that chain is the whole reason this type exists.
    [CreateAssetMenu(menuName = "Laubrary/Cartographer/Tileset", fileName = "Tileset")]
    public class Tileset : ScriptableObject, IVisualPreview
    {
        [Header("Identity")]
        [Tooltip("Name shown in browsers and pickers.")]
        public string displayName = "New Tileset";

        [Header("Tiles")]
        [Tooltip("The tiles this palette offers, in the order the palette shows them. Order is MEANINGFUL: " +
                 "the palette lays tiles out in a fixed grid, so neighbouring tiles form patterns that can " +
                 "be multi-selected and painted together.")]
        public List<LevelTile> tiles = new();

        [Tooltip("Palette grid width, in cells. Fixed on purpose — a stable grid is what lets tile POSITION " +
                 "express spatial patterns. The tiles list is row-major over this grid; a null entry is an " +
                 "EMPTY cell, kept deliberately so patterns can breathe.")]
        [Min(1)] public int paletteColumns = 8;

        [Tooltip("Palette grid height, in cells. Grows automatically when tiles are placed lower.")]
        [Min(1)] public int paletteRows = 8;

        /// Number of rows the grid actually needs: the declared height, or more if content sits lower.
        public int EffectiveRows
        {
            get
            {
                int last = -1;
                if (tiles != null)
                    for (int i = tiles.Count - 1; i >= 0; i--)
                        if (tiles[i] != null) { last = i; break; }
                int cols = Mathf.Max(1, paletteColumns);
                return Mathf.Max(Mathf.Max(1, paletteRows), last < 0 ? 1 : last / cols + 1);
            }
        }

        [Tooltip("The tile a new paint uses when the author has not picked one. A convenience default for " +
                 "the palette — a LAYER's default tile (which fills unpainted cells) is a separate setting " +
                 "on the layer itself.")]
        public LevelTile defaultTile;

        [Header("Clumps")]
        [Tooltip("Object-shaped content: locked tile arrangements placed as one thing (urns, doors, wall " +
                 "columns). Their member tiles live OUTSIDE the loose-tile palette above.")]
        [FormerlySerializedAs("props")] public List<Clump> clumps = new();

        [Tooltip("Clump grid width, in cells — the Clumps tab's own layout grid.")]
        [FormerlySerializedAs("propColumns")] [Min(1)] public int clumpColumns = 8;

        /// Rows the clump grid needs: enough for the lowest clump, plus room to drop the next one.
        public int EffectiveClumpRows
        {
            get
            {
                int max = 3;
                if (clumps != null)
                    foreach (var p in clumps)
                        if (p != null) max = Mathf.Max(max, p.gridPos.y + p.Bounds.yMax + 2);
                return max;
            }
        }

        /// True if `tile` belongs to this palette.
        public bool Contains(LevelTile tile)
        {
            if (tile == null || tiles == null) return false;
            for (int i = 0; i < tiles.Count; i++) if (tiles[i] == tile) return true;
            return false;
        }

        // IVisualPreview — a row of the palette's tiles, through the shared tile-to-pixels path, so two
        // tilesets are tellable apart at a glance in a browser.
        public Texture2D RenderPreviewTexture()
        {
            if (tiles == null || tiles.Count == 0) return null;

            var sprites = new List<Sprite>();
            foreach (var t in tiles)
            {
                if (t == null) continue;
                var s = t.IsAnimated ? t.animation[0] : t.SpriteOfVariant(0);
                if (s != null) sprites.Add(s);
            }
            if (sprites.Count == 0) return null;

            int cell = CartographerPreview.CellPixels(sprites[0]);
            int w = sprites.Count * cell;
            if (w <= 0 || w > CartographerPreview.MaxSide || cell > CartographerPreview.MaxSide) return null;

            var tex = CartographerPreview.NewCanvas(w, cell);
            for (int i = 0; i < sprites.Count; i++) CartographerPreview.Blit(tex, sprites[i], i * cell, 0);
            tex.Apply();
            return tex;
        }

        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
