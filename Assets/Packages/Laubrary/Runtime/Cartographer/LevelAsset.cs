using System.Collections.Generic;
using Laubrary.PreviewKit;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// Who put an element into the level. Recorded per paint, placement and decal — not per level — because a
    /// hybrid level is one where a human placed some things and a generator placed the rest, and regenerating
    /// must replace ONLY the generated ones. One byte now is the difference between hybrid levels being a
    /// feature and being a rewrite.
    public enum Origin
    {
        Authored,
        Generated,
    }

    /// One painted cell: which tile, on which layer, and which of the tile's variants the paint resolved to.
    /// The variant is chosen at paint time and stored — never re-rolled at draw time, where a chunk refresh
    /// would make random tiles flicker and sequence policies have no "previous cell" to consult.
    [System.Serializable]
    public class TilePaint
    {
        [Tooltip("The level layer this paint targets, by name.")]
        public string layer = "Terrain";

        [Tooltip("The painted cell.")]
        public Vector2Int cell;

        [Tooltip("The tile painted there.")]
        public LevelTile tile;

        [Tooltip("Which of the tile's variants this paint resolved to when it was made.")]
        public byte variant;

        [Tooltip("Whether a human painted this cell or a generator did.")]
        public Origin origin = Origin.Authored;
    }

    /// A prop as it was actually placed. A Tilemap only stores tiles, so once a prop is stamped the fact
    /// that THOSE cells came from THAT prop is gone — and with it the prop's tags, its prefab hook, and any
    /// hope of rebuilding or regenerating the level. Recording each placement keeps that knowledge.
    [System.Serializable]
    public class PropPlacement
    {
        [Tooltip("The prop that was stamped.")]
        [FormerlySerializedAs("clump")] public Prop prop;

        [Tooltip("Cell the prop's own origin was stamped at.")]
        public Vector2Int cell;

        [Tooltip("Quarter-turns anticlockwise applied when it was stamped.")]
        public int rotation;

        [Tooltip("Whether it was mirrored along X when stamped.")]
        public bool mirrorX;

        [Tooltip("Which of the prop's prefab choices this placement resolved to, decided when it was " +
                 "placed. -1 means the first usable entry. Stored so a rebuild never re-rolls it — the same " +
                 "determinism rule paints follow for variants.")]
        public int prefabChoice = -1;

        [Tooltip("Whether a human placed this prop or a generator did.")]
        public Origin origin = Origin.Authored;
    }

    /// One cell of a resolved layer: what actually shows there once default fill, placements and paints have
    /// all had their say. `source` is the prop that supplied the tile, or null for a paint or the default
    /// fill — kept because collision narrowing needs to know which placement a cell came from.
    public struct ResolvedCell
    {
        public TileBase tile;
        public int variant;
        public Prop source;
    }

    /// How a level's solid layers are collided with. One enum on the level rather than two level types,
    /// because only the collider setup differs.
    public enum LevelCollision
    {
        /// Solid terrain merged into one composite outline — walls you cannot cross from any direction.
        TopDown,
        /// Same, plus layers marked one-way get a platform effector so you can jump up through them.
        SideScroll,
    }

    /// A playable level: the authoring truth and the thing a game references. The asset is edited by the
    /// Cartographer window and consumed by a LevelInstance, which builds the actual Grid and Tilemaps —
    /// there is exactly one source of truth and it is this object.
    ///
    /// Both paints and placements are stored, deliberately: single-tile painting must not be forced through
    /// a fake 1x1 prop, and a stamped prop keeps its identity (tags, prefab hook, named spots) instead of
    /// dissolving into anonymous tiles.
    [CreateAssetMenu(menuName = "Laubrary/Cartographer/Level", fileName = "Level")]
    public class LevelAsset : ScriptableObject, IVisualPreview
    {
        [Header("Identity")]
        [Tooltip("Name shown in browsers and pickers.")]
        public string displayName = "New Level";

        [Tooltip("What this level may be built from: the biome's tilesets feed the palette and its prop " +
                 "list gates what may be placed.")]
        public CartographerBiome biome;

        [Tooltip("The level's extent in cells. Explicit rather than auto-grown: a default-fill layer needs " +
                 "to know where to stop and a thumbnail needs to know what to frame.")]
        public RectInt bounds = new RectInt(0, 0, 32, 18);

        [Tooltip("Which collider setup the level's solid layers get when an instance builds them.")]
        public LevelCollision collision = LevelCollision.SideScroll;

        [Header("Content")]
        [Tooltip("The level's drawing layers, in back-to-front order.")]
        public List<LevelLayer> layers = new();

        [Tooltip("Where ACTORS render within the layer stack: layers sorting below this number draw " +
                 "behind them, layers above draw over them (overhangs, wall caps). The level is the " +
                 "authority — spawners read it via LevelInstance.ApplyActorSorting, so no character " +
                 "prefab is ever hand-configured.")]
        public int actorSortingOrder = 15;

        [Tooltip("Every painted cell, in paint order.")]
        public List<TilePaint> paints = new();

        [Tooltip("Every prop stamped into this level, in placement order.")]
        public List<PropPlacement> placements = new();

        [Tooltip("Every free sprite placed in this level.")]
        public List<Decal> decals = new();

        [Header("Rooms")]
        [Tooltip("The sections play moves through, in order. On the asset so a level is playable as-is; a " +
                 "LevelInstance may override them when one level needs a second pacing.")]
        public List<CartographerRoom> rooms = new();

        /// The layer named `layerName`, or null.
        public LevelLayer GetLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName) || layers == null) return null;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] != null && layers[i].name == layerName) return layers[i];
            return null;
        }

        /// Paint one cell, resolving the tile's variant policy here and now — the editor and the generator
        /// both come through this, so every paint is stored with its resolved variant and a level never
        /// re-rolls itself.
        public TilePaint Paint(string layer, Vector2Int cell, LevelTile tile, Origin origin = Origin.Authored)
            => SetPaint(layer, cell, tile, PaintVariants.Resolve(this, tile, cell), origin);

        /// Paint one cell, replacing any earlier paint of the same cell on the same layer. The variant is
        /// whatever the caller already resolved — Paint() is the everyday entry; this one exists for a
        /// hand-override of a single cell's variant.
        public TilePaint SetPaint(string layer, Vector2Int cell, LevelTile tile, byte variant,
            Origin origin = Origin.Authored)
        {
            ErasePaint(layer, cell);
            var p = new TilePaint { layer = layer, cell = cell, tile = tile, variant = variant, origin = origin };
            paints.Add(p);
            return p;
        }

        /// Remove the paint at `cell` on `layer`, if any. Returns whether one was removed.
        public bool ErasePaint(string layer, Vector2Int cell)
        {
            if (paints == null) return false;
            for (int i = paints.Count - 1; i >= 0; i--)
            {
                var p = paints[i];
                if (p != null && p.layer == layer && p.cell == cell) { paints.RemoveAt(i); return true; }
            }
            return false;
        }

        /// Record a prop placement. The editor and the generator both come through here — that is what
        /// keeps a generated level indistinguishable from a hand-built one.
        public PropPlacement Place(Prop prop, Vector2Int cell, int rotation = 0, bool mirrorX = false,
            Origin origin = Origin.Authored)
        {
            var p = new PropPlacement { prop = prop, cell = cell, rotation = rotation, mirrorX = mirrorX, origin = origin };
            placements.Add(p);
            return p;
        }

        /// Remove every generated paint, placement and decal, leaving authored content untouched. This is
        /// the hybrid-level seam: regeneration is "clear generated, run the rules again".
        public void ClearGenerated()
        {
            paints?.RemoveAll(p => p != null && p.origin == Origin.Generated);
            placements?.RemoveAll(p => p != null && p.origin == Origin.Generated);
            decals?.RemoveAll(d => d != null && d.origin == Origin.Generated);
        }

        /// What ONE cell of `layer` shows — the single-cell twin of ResolveLayer, with identical semantics
        /// (paints beat placements beat default fill; among equals the later record wins). Exists so painting
        /// can refresh one cell without re-resolving the level.
        public bool ResolveCell(LevelLayer layer, Vector2Int cell, out ResolvedCell result)
        {
            result = default;
            if (layer == null) return false;

            if (paints != null)
                for (int i = paints.Count - 1; i >= 0; i--)
                {
                    var p = paints[i];
                    if (p == null || p.tile == null || p.cell != cell || p.layer != layer.name) continue;
                    result = new ResolvedCell { tile = p.tile, variant = p.variant, source = null };
                    return true;
                }

            if (placements != null)
                for (int i = placements.Count - 1; i >= 0; i--)
                {
                    var p = placements[i];
                    if (p?.prop?.cells == null) continue;
                    // Backwards within the prop too, mirroring ResolveLayer's later-write-wins exactly.
                    for (int j = p.prop.cells.Count - 1; j >= 0; j--)
                    {
                        var c = p.prop.cells[j];
                        if (c == null || c.tile == null) continue;
                        var wanted = string.IsNullOrEmpty(c.layer) ? "Terrain" : c.layer;
                        if (wanted != layer.name) continue;
                        if (p.cell + Prop.TransformOffset(c.offset, p.rotation, p.mirrorX) != cell) continue;
                        result = new ResolvedCell { tile = c.tile, variant = 0, source = p.prop };
                        return true;
                    }
                }

            if (layer.defaultTile != null && bounds.Contains(cell))
            {
                result = new ResolvedCell { tile = layer.defaultTile, variant = 0, source = null };
                return true;
            }

            return false;
        }

        /// What each cell of `layer` actually shows, honouring the rebuild order: default fill first, then
        /// placements in placement order, then paints — so a hand touch-up always wins over the structure
        /// beneath it. The LevelInstance build and the thumbnail both consume this; neither owns a second
        /// interpretation of the data.
        public Dictionary<Vector2Int, ResolvedCell> ResolveLayer(LevelLayer layer)
        {
            var cells = new Dictionary<Vector2Int, ResolvedCell>();
            if (layer == null) return cells;

            if (layer.defaultTile != null)
            {
                foreach (var pos in bounds.allPositionsWithin)
                    cells[pos] = new ResolvedCell { tile = layer.defaultTile, variant = 0, source = null };
            }

            if (placements != null)
            {
                foreach (var p in placements)
                {
                    if (p?.prop?.cells == null) continue;
                    foreach (var c in p.prop.cells)
                    {
                        if (c == null || c.tile == null) continue;
                        var wanted = string.IsNullOrEmpty(c.layer) ? "Terrain" : c.layer;
                        if (wanted != layer.name) continue;
                        var o = Prop.TransformOffset(c.offset, p.rotation, p.mirrorX);
                        cells[p.cell + o] = new ResolvedCell { tile = c.tile, variant = 0, source = p.prop };
                    }
                }
            }

            if (paints != null)
            {
                foreach (var p in paints)
                {
                    if (p == null || p.tile == null || p.layer != layer.name) continue;
                    cells[p.cell] = new ResolvedCell { tile = p.tile, variant = p.variant, source = null };
                }
            }

            return cells;
        }

        /// Every tag the cell carries on `layerName`: its resolved tile's tags, its source prop's tags, and
        /// the layer's own tags. Cartographer stores and answers; it never interprets — "Untraversable means
        /// enemies won't path here" is the game's sentence to finish.
        public List<TileTag> TagsAt(Vector2Int cell, string layerName)
        {
            var result = new List<TileTag>();
            var layer = GetLayer(layerName);
            if (layer == null || !ResolveCell(layer, cell, out var rc)) return result;

            void AddAll(List<TileTag> tags)
            {
                if (tags == null) return;
                foreach (var t in tags) if (t != null && !result.Contains(t)) result.Add(t);
            }

            if (rc.tile is LevelTile lt) AddAll(lt.tags);
            if (rc.source != null) AddAll(rc.source.tags);
            AddAll(layer.tags);
            return result;
        }

        /// True if any layer's content at `cell` carries `tag`.
        public bool HasTag(Vector2Int cell, TileTag tag)
        {
            if (tag == null || layers == null) return false;
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                var tags = TagsAt(cell, layer.name);
                for (int i = 0; i < tags.Count; i++) if (tags[i] == tag) return true;
            }
            return false;
        }

        /// Every cell, on any layer, whose content carries `tag`. Authoring-time convenience — runtime code
        /// with an instance should ask the instance, which reads its already-resolved cells.
        public IEnumerable<Vector2Int> CellsWith(TileTag tag)
        {
            if (tag == null || layers == null) yield break;
            var seen = new HashSet<Vector2Int>();
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                bool layerTagged = layer.tags != null && layer.tags.Contains(tag);
                foreach (var kv in ResolveLayer(layer))
                {
                    if (seen.Contains(kv.Key)) continue;
                    bool tagged = layerTagged
                        || (kv.Value.tile is LevelTile lt && lt.HasTag(tag))
                        || (kv.Value.source != null && kv.Value.source.HasTag(tag));
                    if (tagged) { seen.Add(kv.Key); yield return kv.Key; }
                }
            }
        }

        // IVisualPreview — the level itself, rasterised through the shared tile-to-pixels path. A level too
        // large to read at thumbnail size renders a centred 1:1 crop rather than scaling to mush.
        public Texture2D RenderPreviewTexture() => CartographerPreview.RenderLevel(this);

        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }

    /// Resolves which variant a paint lands, at PAINT time. Every policy is a pure function of the asset's
    /// own data — the sequence policies read the existing paint records instead of keeping a hidden cursor —
    /// so identical edit histories give identical levels, nothing needs serialising beyond the paints
    /// themselves, and nothing is lost to a domain reload.
    public static class PaintVariants
    {
        public static byte Resolve(LevelAsset level, LevelTile tile, Vector2Int cell)
        {
            int count = tile != null && tile.variants != null ? tile.variants.Count : 0;
            if (count <= 1) return 0;

            switch (tile.variantPolicy)
            {
                case VariantPolicy.RoundRobin:
                    return (byte)(CountPaintsOf(level, tile) % count);

                case VariantPolicy.NeverRepeatLast:
                {
                    int last = LastVariantOf(level, tile);
                    int pick = Hash(cell) % (last < 0 ? count : count - 1);
                    if (last >= 0 && pick >= last) pick++;
                    return (byte)pick;
                }

                case VariantPolicy.Weighted:
                {
                    float total = 0f;
                    for (int i = 0; i < count; i++) total += WeightOf(tile, i);
                    if (total <= 0f) return 0;
                    // Hash-derived fraction keeps the roll stable per cell: repainting the same cell with
                    // the same tile always lands the same variant.
                    float roll = (Hash(cell) % 10007) / 10007f * total;
                    for (int i = 0; i < count; i++)
                    {
                        roll -= WeightOf(tile, i);
                        if (roll < 0f) return (byte)i;
                    }
                    return (byte)(count - 1);
                }

                default:
                    return (byte)(Hash(cell) % count);
            }
        }

        static float WeightOf(LevelTile tile, int i) =>
            tile.weights != null && i < tile.weights.Count ? Mathf.Max(0f, tile.weights[i]) : 1f;

        static int CountPaintsOf(LevelAsset level, LevelTile tile)
        {
            int n = 0;
            if (level != null && level.paints != null)
                for (int i = 0; i < level.paints.Count; i++)
                    if (level.paints[i] != null && level.paints[i].tile == tile) n++;
            return n;
        }

        static int LastVariantOf(LevelAsset level, LevelTile tile)
        {
            if (level != null && level.paints != null)
                for (int i = level.paints.Count - 1; i >= 0; i--)
                    if (level.paints[i] != null && level.paints[i].tile == tile) return level.paints[i].variant;
            return -1;
        }

        /// Explicit stable mix rather than Vector2Int.GetHashCode — a paint's roll must never depend on an
        /// engine implementation detail that could change between versions.
        static int Hash(Vector2Int cell) =>
            unchecked(((cell.x * 73856093) ^ (cell.y * 19349663)) & 0x7fffffff);
    }
}
