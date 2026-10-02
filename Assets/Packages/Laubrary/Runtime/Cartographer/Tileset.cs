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
        [Tooltip("STABLE OPAQUE IDENTITY — a GUID minted once, never shown, never edited, never reused. This " +
                 "is what a ClumpPlacement records and what MetaMapper names a subject by, which is precisely " +
                 "why two clumps may both be called 'Shelf' without either one stealing the other's stamps or " +
                 "metadata. Empty on a clump authored before ids existed; EnsureId() mints one the first time " +
                 "anything touches it, and a placement with no id keeps resolving by name until then.")]
        public string id = "";

        [Tooltip("Name shown in palettes and pickers. PURELY COSMETIC since ids exist — duplicates are legal " +
                 "and harmless.")]
        public string displayName = "Clump";

        [Tooltip("Anchor cell of this clump in the tileset's CLUMP grid — layout state, not level data.")]
        public Vector2Int gridPos;

        [Tooltip("THE FOOTPRINT THIS CLUMP'S METADATA WAS AUTHORED AGAINST — the drift guard for `meta`. Marks " +
                 "and masks are stored relative to the footprint's own corner, so growing the clump a row on " +
                 "top moves every authored mark onto different art. Comparing this against Bounds is the only " +
                 "way to know WHICH WAY the art moved (a size change alone cannot tell 'a row on top' from 'a " +
                 "row at the bottom'). Width 0 = never recorded; the first touch adopts the current bounds.")]
        public RectInt metaFootprint;

        /// This clump's stable id, minting one if it has never had it. Returns the id; safe to call every
        /// time. Callers that persist assets should dirty the owning Tileset when <see cref="HasId"/> was
        /// false beforehand.
        public string EnsureId()
        {
            if (string.IsNullOrEmpty(id)) id = System.Guid.NewGuid().ToString("N");
            return id;
        }

        public bool HasId => !string.IsNullOrEmpty(id);

        [Tooltip("The locked arrangement: each member tile at its offset from the anchor. Painting a clump " +
                 "stamps exactly this pattern.")]
        public List<ClumpCell> cells = new();

        [Tooltip("Spatial metadata about this clump: what it IS and where things happen on it. A layer named " +
                 "'LootShelf' is what makes this clump a loot shelf; Points marks on that layer are where the " +
                 "wares sit. Cartographer stores it and never interprets it — the same line the tag system " +
                 "draws.")]
        // EMBEDDED, not a reference to a MetaMap asset. MetaMapper deliberately ships two shapes of the same
        // model (design §3): MetaMapData embeds in any asset and its subject is implicit — it IS its owner —
        // while the MetaMap ScriptableObject exists only for subjects that cannot own one (a bare Sprite).
        // A clump lives inside a Tileset, which IS an asset, so it owns its metadata directly: nothing to
        // create, nothing to name, nothing to go looking for, and it can never be pointed at the wrong clump.
        // Named `meta` to match the AnimationDef.meta the Launimator migration already plans.
        public Laubrary.MetaMapper.MetaMapData meta = new();

        /// True if this clump declares `layerId` — the IDENTITY question, and the cheapest one: a layer that
        /// merely EXISTS is already a useful answer, so a clump can be marked as a shelf without anyone
        /// authoring a single mark on it.
        public bool Declares(string layerId) => meta != null && meta.HasLayer(layerId);

        /// True when this clump declares ANY meaning at all — what the Clumps tab's identity badge marks.
        public bool DeclaresAnything => meta != null && meta.LayerCount > 0;

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
                 "the palette — a LAYER's BACKGROUND TILE (which fills its unpainted cells) is a separate " +
                 "setting on the layer itself.")]
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

        /// THE identity lookup: the clump carrying this stable id, or null. Exact, ordinal, unambiguous —
        /// unlike a name, an id cannot be shared by two clumps, so this can never hand back "whichever one
        /// happened to be first".
        public Clump GetClumpById(string clumpId)
        {
            if (string.IsNullOrEmpty(clumpId) || clumps == null) return null;
            foreach (var c in clumps)
                if (c != null && string.Equals(c.id, clumpId, System.StringComparison.Ordinal)) return c;
            return null;
        }

        /// The clump named `displayName`, or null — the LEGACY / user-facing lookup, kept because a record
        /// written before ids existed has nothing else to go on, and because a human typing a name means the
        /// name. Case-insensitive, and answers the FIRST match: two clumps may legitimately share a name, so
        /// nothing that needs to identify ONE clump should come through here.
        public Clump GetClump(string displayName)
        {
            if (string.IsNullOrEmpty(displayName) || clumps == null) return null;
            foreach (var c in clumps)
                if (c != null && string.Equals(c.displayName, displayName, System.StringComparison.OrdinalIgnoreCase))
                    return c;
            return null;
        }

        /// THE RESOLUTION RULE every stored reference to a clump follows: the id wins, and the name is only
        /// consulted when there is no id at all. That fallback is the entire migration story — a placement
        /// recorded before ids existed keeps working, unchanged, forever if need be — and it is deliberately
        /// NOT a fallback for an id that fails to resolve: an id that no longer exists means that clump is
        /// gone, and quietly substituting a same-named one is exactly the silent mis-resolution ids were
        /// added to end.
        public Clump ResolveClump(string clumpId, string displayName)
            => string.IsNullOrEmpty(clumpId) ? GetClump(displayName) : GetClumpById(clumpId);

        /// Mint an id for every clump that has none, returning how many were minted. Idempotent and silent —
        /// the migration is meant to be invisible. The CALLER dirties the asset when this returns > 0.
        public int EnsureClumpIds()
        {
            if (clumps == null) return 0;
            int minted = 0;
            foreach (var c in clumps)
                if (c != null && !c.HasId) { c.EnsureId(); minted++; }
            return minted;
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

            // A GRID of the first few tiles, not a row of all of them.
            //
            // ☠️ This used to lay every tile in ONE ROW and bail when that exceeded MaxSide — so a tileset
            // of 256 tiles asked for an 8192px strip, got null, and showed a BLANK square forever. Every
            // real tileset is past that limit, which meant the thumbnail worked only on toy data: exactly
            // the failure mode the "a thumbnail is a promise" rule in the UI guide exists to forbid.
            // A square-ish sample identifies a palette at a glance, which is all a thumbnail owes anyone.
            const int MaxTilesShown = 16;
            int shown = Mathf.Min(sprites.Count, MaxTilesShown);
            int cols = Mathf.CeilToInt(Mathf.Sqrt(shown));
            int rows = Mathf.CeilToInt(shown / (float)cols);

            // A tileset may legitimately mix cell sizes (16px and 32px plucks side by side) — the canvas
            // must fit the LARGEST, or bigger sprites overrun their block.
            int cell = 0;
            for (int i = 0; i < shown; i++) cell = Mathf.Max(cell, CartographerPreview.CellPixels(sprites[i]));
            if (cell <= 0) return null;
            // Shrink the cell rather than give up: the promise is a picture, not a picture at native size.
            cell = Mathf.Min(cell, CartographerPreview.MaxSide / Mathf.Max(cols, rows));
            if (cell <= 0) return null;

            var tex = CartographerPreview.NewCanvas(cols * cell, rows * cell);
            for (int i = 0; i < shown; i++)
            {
                int cx = i % cols;
                int cy = rows - 1 - i / cols;      // fill top-left first; texture rows run up
                CartographerPreview.Blit(tex, sprites[i], cx * cell, cy * cell, cell);
            }
            tex.Apply();
            return tex;
        }

        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
