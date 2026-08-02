using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// One named drawing layer of the LEGACY scene-bound level, bound to the Tilemap that renders it.
    /// Superseded by LevelLayer on LevelAsset; alive only until the old CartographerLevel path is retired.
    /// (Renaming the class is safe for scenes: a plain serializable class stores fields, not its name.)
    [System.Serializable]
    public class LegacyLevelLayer
    {
        [Tooltip("Name props address this layer by, e.g. Terrain or Structures.")]
        public string layerName = CartographerLevel.TerrainLayer;

        [Tooltip("The Tilemap this layer draws into.")]
        public Tilemap tilemap;

        [Tooltip("Whether this layer's tiles are solid. Drives the colliders the level builds for it.")]
        public bool solid;

        [Tooltip("Solid only from above — you land on it but jump up through it. Side-scrolling platforms.")]
        public bool oneWay;

        [Tooltip("Grid: every solid tile collides as a full square. Sprite: each tile collides on its own " +
                 "sprite outline, which is how a triangular tile becomes a walkable slope.")]
        public Tile.ColliderType colliderShape = Tile.ColliderType.Grid;
    }

    /// A prop as it was actually placed. A Tilemap only stores tiles, so once a prop is stamped the fact
    /// that THOSE cells came from THAT prop is gone — and with it the prop's tags, its prefab hook, and any
    /// hope of rebuilding or regenerating the level. Recording each placement keeps that knowledge.
    [System.Serializable]
    public class LevelPlacement
    {
        [FormerlySerializedAs("clump")] public Prop prop;

        [Tooltip("Cell the prop's own origin was stamped at.")]
        public Vector2Int origin;

        [Tooltip("Quarter-turns anticlockwise applied when it was stamped.")]
        public int rotation;

        [Tooltip("Whether it was mirrored along X when stamped.")]
        public bool mirrorX;
    }

    /// The runtime side of a Cartographer level: the Grid, its named Tilemap layers, the Rooms play moves
    /// through, and the named spots placed content has published.
    ///
    /// Hand-authored levels and generated ones both end up here — that is the point of having one runtime
    /// representation. A generator fills this in from a seed; an authored level was saved with it already full.
    [RequireComponent(typeof(Grid))]
    public class CartographerLevel : MonoBehaviour
    {
        /// The layer name a cell targets when its author didn't say otherwise.
        public const string TerrainLayer = "Terrain";

        /// How a level's solid layers are collided with. The data model is shared between the two; only the
        /// collider setup differs, which is why this is one enum on the level rather than two level types.
        public enum CollisionMode
        {
            /// Solid terrain merged into one composite outline — walls you cannot cross from any direction.
            TopDown,
            /// Same, plus layers marked one-way get a platform effector so you can jump up through them.
            SideScroll,
        }

        [Header("Collision")]
        [Tooltip("Which collider setup Build Colliders applies to this level's solid layers.")]
        public CollisionMode collision = CollisionMode.SideScroll;

        [Tooltip("Optional. When set, only cells stamped from a prop carrying this tag actually collide — so " +
                 "decorative props can sit on a solid layer without blocking. Leave empty and the whole layer " +
                 "is solid, which is the simpler default.")]
        public TileTag solidTag;

        [Header("Layers")]
        [Tooltip("The level's drawing layers, in back-to-front order.")]
        public List<LegacyLevelLayer> layers = new();

        [Header("Rooms")]
        [Tooltip("The sections play moves through, in order.")]
        public List<CartographerRoom> rooms = new();

        [Header("Placements")]
        [Tooltip("Every prop stamped into this level, in placement order. Kept so the level can be rebuilt, " +
                 "and so a stamped prop's tags and prefab hook survive the trip into the Tilemap.")]
        public List<LevelPlacement> placements = new();

        readonly Dictionary<string, Vector3> spots = new();

        Grid grid;
        public Grid Grid => grid != null ? grid : grid = GetComponent<Grid>();

        /// The Tilemap registered under `layerName`, or null if this level has no such layer.
        public Tilemap GetLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName) || layers == null) return null;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] != null && layers[i].layerName == layerName) return layers[i].tilemap;
            return null;
        }

        /// Publish a named point at a world position. Placement code calls this for every spot on every prop it
        /// stamps, so whoever wants "landing-pad" can find it without knowing which prop supplied it.
        /// A repeated name overwrites the earlier one — last placement wins.
        public void RegisterSpot(string spotName, Vector3 worldPosition)
        {
            if (string.IsNullOrEmpty(spotName)) return;
            spots[spotName] = worldPosition;
        }

        /// Look up a named point published by placed content.
        public bool TryGetSpot(string spotName, out Vector3 worldPosition)
        {
            worldPosition = default;
            return !string.IsNullOrEmpty(spotName) && spots.TryGetValue(spotName, out worldPosition);
        }

        /// Every spot name currently published, for tooling and for diagnosing a sequence that asked for a spot
        /// this level does not have.
        public IEnumerable<string> SpotNames => spots.Keys;

        /// Drop every published spot. Called before a level is rebuilt or regenerated.
        public void ClearSpots() => spots.Clear();

        /// Kept for legacy callers; the canonical copy lives on Prop.
        public static Vector2Int TransformOffset(Vector2Int offset, int rotation, bool mirrorX)
            => Prop.TransformOffset(offset, rotation, mirrorX);

        /// Same transform for a fractional (spot) offset.
        public static Vector2 TransformOffset(Vector2 offset, int rotation, bool mirrorX)
            => Prop.TransformOffset(offset, rotation, mirrorX);

        /// Stamp a prop's cells into this level's layers, publishing its spots, recording the placement, and
        /// returning the prefab choice that was rolled (null when the prop spawns nothing). `pick` picks an
        /// index from a weight list, which is how a generator keeps placement deterministic from its own seed;
        /// pass null to take the first non-zero-weight entry.
        public PropPrefabChoice PlaceProp(Prop prop, Vector2Int origin,
            System.Func<IList<float>, int> pick = null, int rotation = 0, bool mirrorX = false, bool record = true)
        {
            if (prop == null) return null;

            if (prop.cells != null)
            {
                foreach (var c in prop.cells)
                {
                    if (c == null || c.tile == null) continue;
                    var wanted = string.IsNullOrEmpty(c.layer) ? TerrainLayer : c.layer;
                    var map = GetLayer(wanted);
                    if (map == null)
                    {
                        // Layer names are free text on both sides, so a typo (or a prop authored for a level
                        // that has more layers than this one) would otherwise drop cells with no trace at all.
                        Debug.LogWarning($"[Cartographer] '{prop.name}' wants layer '{wanted}', which this level " +
                                         $"does not have — those cells were not placed. Level layers: " +
                                         $"{string.Join(", ", layers.ConvertAll(l => l != null ? l.layerName : "<null>"))}", this);
                        continue;
                    }
                    var o = TransformOffset(c.offset, rotation, mirrorX);
                    map.SetTile(new Vector3Int(origin.x + o.x, origin.y + o.y, 0), c.tile);
                }
            }

            if (prop.spots != null)
            {
                foreach (var s in prop.spots)
                {
                    if (s == null || string.IsNullOrEmpty(s.spotName)) continue;

                    // +0.5 puts the spot at the centre of its cell rather than the corner, so a spot authored at
                    // a whole-number offset lands where the tile looks like it is.
                    var so = TransformOffset(s.offset, rotation, mirrorX);
                    var cell = new Vector3(origin.x + so.x + 0.5f, origin.y + so.y + 0.5f, 0f);
                    var g = Grid;
                    RegisterSpot(s.spotName, g != null ? g.LocalToWorld(g.CellToLocalInterpolated(cell)) : cell);
                }
            }

            if (record)
                placements.Add(new LevelPlacement { prop = prop, origin = origin, rotation = rotation, mirrorX = mirrorX });

            return ChoosePrefab(prop, pick);
        }

        /// Rebuild every layer's tiles and spots from the recorded placements — the inverse of "the Tilemap
        /// forgot where its tiles came from". Used after an undo, a regeneration, or a prop edit.
        public void RebuildFromPlacements()
        {
            foreach (var l in layers) l?.tilemap?.ClearAllTiles();
            ClearSpots();

            var recorded = new List<LevelPlacement>(placements);
            placements.Clear();
            foreach (var p in recorded)
            {
                if (p?.prop == null) continue;
                PlaceProp(p.prop, p.origin, null, p.rotation, p.mirrorX);
            }
        }

        /// Give every solid layer the colliders its collision mode calls for, and strip them from layers that
        /// are no longer solid. Idempotent — safe to call after any edit, which is what makes it usable as a
        /// "rebuild" button rather than a one-shot setup step.
        public void BuildColliders()
        {
            foreach (var l in layers)
            {
                if (l?.tilemap == null) continue;
                var go = l.tilemap.gameObject;

                var tc = go.GetComponent<TilemapCollider2D>();
                var rb = go.GetComponent<Rigidbody2D>();
                var cc = go.GetComponent<CompositeCollider2D>();
                var pe = go.GetComponent<PlatformEffector2D>();

                if (!l.solid)
                {
                    // Order matters: the composite depends on the rigidbody, so it has to go first.
                    if (cc != null) DestroyComponent(cc);
                    if (rb != null) DestroyComponent(rb);
                    if (tc != null) DestroyComponent(tc);
                    if (pe != null) DestroyComponent(pe);
                    continue;
                }

                if (tc == null) tc = go.AddComponent<TilemapCollider2D>();
                if (rb == null) rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Static;
                if (cc == null) cc = go.AddComponent<CompositeCollider2D>();
                cc.geometryType = CompositeCollider2D.GeometryType.Polygons;
                tc.compositeOperation = Collider2D.CompositeOperation.Merge;

                // A one-way platform only makes sense side-on; in a top-down level "up" is not a direction you
                // fall from, so the effector is dropped rather than silently doing nothing.
                bool wantEffector = l.oneWay && collision == CollisionMode.SideScroll;
                if (wantEffector)
                {
                    if (pe == null) pe = go.AddComponent<PlatformEffector2D>();
                    pe.useOneWay = true;
                    cc.usedByEffector = true;
                }
                else
                {
                    cc.usedByEffector = false;
                    if (pe != null) DestroyComponent(pe);
                }

                ApplyColliderShapes(l);
            }
        }

        /// Set each cell's collider type on a solid layer: the layer's own shape everywhere, then None on cells
        /// belonging to props that lack `solidTag`.
        ///
        /// Per-CELL rather than per-tile-asset on purpose — the same Ground tile can be structural in one prop
        /// and dressing in another, so the answer belongs to the placement, not to the tile.
        void ApplyColliderShapes(LegacyLevelLayer layer)
        {
            var map = layer.tilemap;
            if (map == null) return;

            foreach (var pos in map.cellBounds.allPositionsWithin)
            {
                if (!map.HasTile(pos)) continue;
                map.SetTileFlags(pos, TileFlags.None);      // tiles lock their collider type by default
                map.SetColliderType(pos, layer.colliderShape);
            }

            if (solidTag == null) return;

            foreach (var p in placements)
            {
                if (p?.prop?.cells == null || p.prop.HasTag(solidTag)) continue;
                foreach (var c in p.prop.cells)
                {
                    if (c == null || c.tile == null) continue;
                    var wanted = string.IsNullOrEmpty(c.layer) ? TerrainLayer : c.layer;
                    if (wanted != layer.layerName) continue;

                    var o = TransformOffset(c.offset, p.rotation, p.mirrorX);
                    var pos = new Vector3Int(p.origin.x + o.x, p.origin.y + o.y, 0);
                    if (!map.HasTile(pos)) continue;
                    map.SetTileFlags(pos, TileFlags.None);
                    map.SetColliderType(pos, Tile.ColliderType.None);
                }
            }
        }

        static void DestroyComponent(Component c)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEditor.Undo.DestroyObjectImmediate(c); return; }
#endif
            Destroy(c);
        }

        static PropPrefabChoice ChoosePrefab(Prop prop, System.Func<IList<float>, int> pick)
        {
            var choices = prop.prefabChoices;
            if (choices == null || choices.Count == 0) return null;

            var weights = new List<float>(choices.Count);
            for (int i = 0; i < choices.Count; i++) weights.Add(choices[i] != null ? Mathf.Max(0f, choices[i].weight) : 0f);

            if (pick != null)
            {
                int i = pick(weights);
                return i >= 0 && i < choices.Count ? choices[i] : null;
            }

            for (int i = 0; i < choices.Count; i++) if (weights[i] > 0f) return choices[i];
            return null;
        }
    }
}
