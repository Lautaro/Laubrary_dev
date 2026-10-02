using System.Collections.Generic;
using Laubrary.MetaMapper;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// Marks a GameObject as built by a LevelInstance, so a rebuild knows exactly what it owns and never
    /// touches anything a user parented under the level by hand.
    [AddComponentMenu("")]
    public class LevelInstancePart : MonoBehaviour { }

    /// The scene side of a level: takes a LevelAsset and builds the Grid's Tilemap layers, decals and
    /// colliders, in the editor and at runtime. Everything it builds is derived, unsaved and rebuilt from
    /// the asset — editing writes to the ASSET, the instance rebuilds, and there is exactly one source of
    /// truth. (That is the deliberate reversal of the old scene-owned CartographerLevel.)
    [ExecuteAlways]
    [RequireComponent(typeof(Grid))]
    public class LevelInstance : MonoBehaviour
    {
        [Tooltip("The level this instance builds and plays.")]
        public LevelAsset level;

        [Tooltip("Optional. When non-empty, these rooms replace the asset's own — for the rare level that " +
                 "needs a second pacing without duplicating the asset.")]
        public List<CartographerRoom> roomsOverride = new();

        [Tooltip("Rebuild automatically whenever this component loads or its level reference changes in " +
                 "the Inspector. Leave on; turn off only for a script that wants to control build timing.")]
        public bool autoBuild = true;

        readonly Dictionary<string, Vector3> spots = new();
        readonly List<Tilemap> builtMaps = new();

        sealed class BuiltLayer
        {
            public LevelLayer layer;
            public Tilemap map;
            public Dictionary<Vector2Int, ResolvedCell> cells;   // the live dict VariantSource reads
        }
        readonly Dictionary<string, BuiltLayer> builtLayers = new();

        Grid grid;
        public Grid Grid => grid != null ? grid : grid = GetComponent<Grid>();

        /// The rooms play should use: the override when one is authored, the asset's otherwise.
        public List<CartographerRoom> EffectiveRooms =>
            roomsOverride != null && roomsOverride.Count > 0 ? roomsOverride
            : level != null ? level.rooms : null;

        void OnEnable()
        {
            if (autoBuild) Rebuild();
        }

        void OnDisable() => UnregisterMaps();

        void OnValidate()
        {
            // Deferred: OnValidate runs mid-serialization, where DestroyImmediate is illegal.
            if (!autoBuild || !isActiveAndEnabled) return;
#if UNITY_EDITOR
            // ⚠️ Only in the play state it was queued in (OutBurner 2026-09-25). A recompile in an UNFOCUSED editor
            // runs OnValidate but does not tick delayCall until Play starts (HANDOVER trap 7.11) — so the edit-mode
            // rebuild landed AFTER the scene's Start and rebuilt the live level from scratch, discarding whatever
            // the game had done to the built layers. Measured in Scavenge: the Walls moved back to Default, so the
            // flashlight lost every occluder and saw through all shelves on that Play session.
            bool queuedInPlay = Application.isPlaying;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled && Application.isPlaying == queuedInPlay) Rebuild();
            };
#endif
        }

        /// Tear down everything previously built and build the level fresh from the asset. Idempotent, and
        /// safe to call after any asset edit — this IS the "apply the edit" operation.
        public void Rebuild()
        {
            Clear();
            if (level == null) return;

            var layersRoot = NewPart("Layers", transform);

            foreach (var layer in level.layers)
            {
                if (layer == null) continue;
                var go = NewPart(string.IsNullOrEmpty(layer.name) ? "Layer" : layer.name, layersRoot.transform);
                var map = go.AddComponent<Tilemap>();
                var renderer = go.AddComponent<TilemapRenderer>();
                // A Y-SORTED layer joins the actor band instead of claiming its own draw order, and this is
                // the part that is easy to get wrong: Unity compares sorting layer, THEN sorting order, and
                // only then the transparency sort axis. A layer sitting at 20 against actors at 15 wins on
                // ORDER, so position is never consulted and no amount of sort-axis configuration helps.
                // Sharing the band is what hands the decision to Y.
                //
                // Individual mode matters just as much: the default (Chunk) sorts the whole tilemap as ONE
                // unit, so the entire layer would flip in front of or behind the player as a block instead
                // of per tile.
                if (layer.ySort)
                {
                    renderer.mode = TilemapRenderer.Mode.Individual;
                    renderer.sortingOrder = ActorSortingOrder;
                }
                else renderer.sortingOrder = layer.sortingOrder;
                renderer.enabled = layer.visible;
                map.color = new Color(1f, 1f, 1f, layer.opacity);

                var cells = level.ResolveLayer(layer);
                // A marker-only prop's tiles exist for the author, not the player: shown while editing, dropped
                // in Play (Prop.markerOnly). Filtered here, before the tiles land and the colliders are built,
                // so the cell is simply absent in Play rather than painted and then erased.
                if (Application.isPlaying)
                {
                    List<Vector2Int> markers = null;
                    foreach (var kv in cells)
                        if (kv.Value.source != null && kv.Value.source.markerOnly) (markers ??= new List<Vector2Int>()).Add(kv.Key);
                    if (markers != null) foreach (var c in markers) cells.Remove(c);
                }
                builtLayers[layer.name] = new BuiltLayer { layer = layer, map = map, cells = cells };
                if (cells.Count > 0)
                {
                    // Registered BEFORE the tiles land so the very first GetTileData already sees the
                    // per-cell variants the paints stored.
                    LevelTileVariants.Register(map, new VariantSource(cells));
                    builtMaps.Add(map);

                    var positions = new Vector3Int[cells.Count];
                    var tiles = new TileBase[cells.Count];
                    int i = 0;
                    foreach (var kv in cells)
                    {
                        positions[i] = new Vector3Int(kv.Key.x, kv.Key.y, 0);
                        tiles[i] = kv.Value.tile;
                        i++;
                    }
                    map.SetTiles(positions, tiles);
                }

                BuildColliders(layer, map, cells);
                if (layer.ySort && cells.Count > 0) GroupTallStamps(layer, map, renderer, cells, layersRoot.transform);
            }

            PublishSpots();
            SpawnPlacementPrefabs();
            SpawnDecals();
        }

        /// Re-read every layer's LOOK — its runtime visibility and opacity — into the already-built objects,
        /// without rebuilding anything. The fast path for dragging an opacity slider in the editor, where a
        /// full Rebuild() per pointer move would re-resolve and re-lay every cell of the level for the sake
        /// of one colour.
        ///
        /// Deliberately reads `visible` and `opacity` ONLY. `editorHidden` / `editorOpacity` are authoring
        /// state and this object is the mirror of what the GAME shows — a mirror that honoured them would
        /// stop being evidence.
        public void ApplyLayerLook()
        {
            foreach (var built in builtLayers.Values)
            {
                if (built?.map == null || built.layer == null) continue;
                var r = built.map.GetComponent<TilemapRenderer>();
                if (r != null) r.enabled = built.layer.visible;
                built.map.color = new Color(1f, 1f, 1f, built.layer.opacity);
            }
        }

        /// Re-resolve ONE cell of one layer from the asset and update the built tilemap in place — the fast
        /// path a painting drag uses instead of a full rebuild. The variant dict it updates is the same
        /// object the tile's variant lookup reads, so the render agrees immediately.
        public void RefreshCell(string layerName, Vector2Int cell)
        {
            if (level == null || string.IsNullOrEmpty(layerName)) return;
            if (!builtLayers.TryGetValue(layerName, out var built) || built.map == null) return;

            var pos = new Vector3Int(cell.x, cell.y, 0);
            if (level.ResolveCell(built.layer, cell, out var rc))
            {
                built.cells[cell] = rc;
                built.map.SetTile(pos, rc.tile);
                built.map.RefreshTile(pos);   // same tile asset, new variant — Unity won't re-pull tile data otherwise
                if (built.layer.solid)
                {
                    built.map.SetTileFlags(pos, TileFlags.None);
                    built.map.SetColliderType(pos, EffectiveCollider(built.layer, rc));
                }
            }
            else
            {
                built.cells.Remove(cell);
                built.map.SetTile(pos, null);
            }
        }

        /// Destroy every built child and registration. The inverse of Rebuild.
        public void Clear()
        {
            UnregisterMaps();
            spots.Clear();
            builtLayers.Clear();
            stampGroupOf.Clear();

            var parts = GetComponentsInChildren<LevelInstancePart>(true);
            foreach (var p in parts)
            {
                if (p == null || p.gameObject == gameObject) continue;
                // Children of a destroyed part die with it; guard against double-destroy.
                if (p.transform.parent != null && p.transform.parent.GetComponentInParent<LevelInstancePart>() != null
                    && p.transform.parent != transform) continue;
                DestroyPart(p.gameObject);
            }
        }

        /// Look up a named point published by placed content.
        public bool TryGetSpot(string spotName, out Vector3 worldPosition)
        {
            worldPosition = default;
            return !string.IsNullOrEmpty(spotName) && spots.TryGetValue(spotName, out worldPosition);
        }

        /// Every spot name currently published, for tooling and diagnostics.
        public IEnumerable<string> SpotNames => spots.Keys;

        /// Every tag `cell` carries on `layerName` — read from the already-resolved build, so gameplay can
        /// ask per-frame without re-resolving the level.
        public List<TileTag> TagsAt(Vector2Int cell, string layerName)
        {
            var result = new List<TileTag>();
            if (!builtLayers.TryGetValue(layerName, out var built)) return result;

            void AddAll(List<TileTag> tags)
            {
                if (tags == null) return;
                foreach (var t in tags) if (t != null && !result.Contains(t)) result.Add(t);
            }

            if (!built.cells.TryGetValue(cell, out var rc)) return result;
            if (rc.tile is LevelTile lt) AddAll(lt.tags);
            if (rc.source != null) AddAll(rc.source.tags);
            AddAll(built.layer.tags);
            return result;
        }

        /// True if any layer's content at `cell` carries `tag`.
        public bool HasTag(Vector2Int cell, TileTag tag)
        {
            if (tag == null) return false;
            foreach (var built in builtLayers.Values)
            {
                if (!built.cells.TryGetValue(cell, out var rc)) continue;
                if (built.layer.tags != null && built.layer.tags.Contains(tag)) return true;
                if (rc.tile is LevelTile lt && lt.HasTag(tag)) return true;
                if (rc.source != null && rc.source.HasTag(tag)) return true;
            }
            return false;
        }

        /// Every built cell, on any layer, whose content carries `tag`.
        public IEnumerable<Vector2Int> CellsWith(TileTag tag)
        {
            if (tag == null) yield break;
            var seen = new HashSet<Vector2Int>();
            foreach (var built in builtLayers.Values)
            {
                bool layerTagged = built.layer.tags != null && built.layer.tags.Contains(tag);
                foreach (var kv in built.cells)
                {
                    if (seen.Contains(kv.Key)) continue;
                    bool tagged = layerTagged
                        || (kv.Value.tile is LevelTile lt && lt.HasTag(tag))
                        || (kv.Value.source != null && kv.Value.source.HasTag(tag));
                    if (tagged) { seen.Add(kv.Key); yield return kv.Key; }
                }
            }
        }

        /// Where actors render within this level's stack — between its behind-layers and above-layers.
        public int ActorSortingOrder => level != null ? level.actorSortingOrder : 15;

        /// Shift an actor's renderers into the level's actor band, PRESERVING internal offsets —
        /// Launimator's layered views stack +1 per layer above their base renderer, and a flat overwrite
        /// would squash that. Spawners call this on whatever they instantiate; no prefab needs hand-set
        /// sorting, and the same character sorts correctly in any level's stack. Y-sorted levels compare an
        /// actor at its authored pivot (normally its feet), not at the centre of its sprite: otherwise a tall
        /// actor flips behind a shelf when its torso crosses the shelf rather than when its feet do.
        public void ApplyActorSorting(GameObject actor)
        {
            if (actor == null) return;
            var renderers = actor.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;
            int min = int.MaxValue;
            foreach (var r in renderers)
            {
                r.spriteSortPoint = SpriteSortPoint.Pivot;
                min = Mathf.Min(min, r.sortingOrder);
            }
            int delta = ActorSortingOrder - min;
            if (delta != 0)
                foreach (var r in renderers) r.sortingOrder += delta;

            // ★ A LAYERED RIG SORTS AS ONE OBJECT (OutBurner 2026-09-25, T-0041). Keeping the +1-per-layer offsets means
            // a composite character's upper body sits at band + 1 — above a y-sorted shelf at the band wherever the
            // character stands, so ProtoGuy behind a shelf showed his legs behind it and his torso over it. A
            // SortingGroup on the root at the band makes the whole rig sort by its root (the same point its body
            // collides at), and the offsets keep layering the parts INSIDE the group. Overlays far above the band
            // (an aim pointer, at +45) get their own sortAtRoot group so they stay on top of the world as before.
            bool layered = false;
            foreach (var r in renderers) if (r.sortingOrder != ActorSortingOrder) { layered = true; break; }
            if (!layered) return;
            var group = actor.GetComponent<UnityEngine.Rendering.SortingGroup>();
            if (group == null) group = actor.AddComponent<UnityEngine.Rendering.SortingGroup>();
            group.sortingLayerID = renderers[0].sortingLayerID;
            group.sortingOrder = ActorSortingOrder;
            foreach (var r in renderers)
            {
                if (r.sortingOrder < ActorSortingOrder + OverlayOffset || r.gameObject == actor) continue;
                var own = r.GetComponent<UnityEngine.Rendering.SortingGroup>();
                if (own == null) own = r.gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
                own.sortAtRoot = true;
                own.sortingLayerID = r.sortingLayerID;
                own.sortingOrder = r.sortingOrder;
            }
        }

        /// A renderer this far above the actor band is an overlay (aim pointer, marker), not a body layer.
        const int OverlayOffset = 20;

        /// The draw order of things LYING on the floor: one below the actor band, so it is above every floor layer
        /// drawn at its own lower order and below everything that stands.
        public int FloorBodySortingOrder => ActorSortingOrder - 1;

        /// Lay an actor down: from now on it draws as part of the floor, under every standing actor, instead of
        /// y-sorting among them. Call it when a body goes down (a corpse, a knocked-out character); the game decides
        /// when, the level owns where.
        ///
        /// Why a y-sort cannot do this (OutBurner 2026-10-02): a standing actor sorts at its feet, and a corpse keeps
        /// those feet — but its picture now lies across the floor BEHIND them. Anyone standing on that picture has
        /// their feet further back than the corpse's, so the y-sort drew the corpse over them. A body on the floor is
        /// under whoever stands on it wherever they stand, which is a draw order, not a position.
        ///
        /// Trade-off: y-sorted level layers share the actor band, so a corpse also draws under them — one that falls
        /// against the front of a tall stamp tucks under its picture rather than over it.
        public void ApplyFloorSorting(GameObject actor)
        {
            if (actor == null) return;
            var group = actor.GetComponent<UnityEngine.Rendering.SortingGroup>();
            if (group != null) { group.sortingOrder = FloorBodySortingOrder; return; }
            // Not layered (ApplyActorSorting added no group): every body renderer sits on the band itself. Overlays far
            // above it keep their order.
            foreach (var r in actor.GetComponentsInChildren<SpriteRenderer>(true))
                if (r.sortingOrder >= ActorSortingOrder && r.sortingOrder < ActorSortingOrder + OverlayOffset)
                    r.sortingOrder -= ActorSortingOrder - FloorBodySortingOrder;
        }

        public Vector3 CellToWorld(Vector2Int cell) =>
            Grid.LocalToWorld(Grid.CellToLocalInterpolated(new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f)));

        public Vector2Int WorldToCell(Vector3 world)
        {
            var c = Grid.WorldToCell(world);
            return new Vector2Int(c.x, c.y);
        }

        // ── build steps ─────────────────────────────────────────────────────────────

        void BuildColliders(LevelLayer layer, Tilemap map, Dictionary<Vector2Int, ResolvedCell> cells)
        {
            if (layer == null || map == null) return;
            var go = map.gameObject;

            if (!layer.solid) return;    // freshly built objects — nothing stale to strip

            var tc = go.AddComponent<TilemapCollider2D>();
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            var cc = go.AddComponent<CompositeCollider2D>();
            cc.geometryType = CompositeCollider2D.GeometryType.Polygons;
            tc.compositeOperation = Collider2D.CompositeOperation.Merge;

            // A one-way platform only makes sense side-on; in a top-down level "up" is not a direction
            // you fall from, so the effector is dropped rather than silently doing nothing.
            if (layer.oneWay && level.collision == LevelCollision.SideScroll)
            {
                var pe = go.AddComponent<PlatformEffector2D>();
                pe.useOneWay = true;
                cc.usedByEffector = true;
            }

            foreach (var kv in cells)
            {
                var pos = new Vector3Int(kv.Key.x, kv.Key.y, 0);
                map.SetTileFlags(pos, TileFlags.None);
                map.SetColliderType(pos, EffectiveCollider(layer, kv.Value));
            }
        }

        /// What a solid layer's cell actually collides as: the TILE'S OWN shape — pluck-time auto-defaults
        /// and the builder's Collision mode author per-tile shapes (a wall blocks square, a barrel on its
        /// outline, an overhang not at all), and the layer must not flatten that authorship. The layer's
        /// shape is the fallback for plain TileBase content; solidTag still NARROWS a solid layer (cells
        /// whose tile and source prop both lack it stop colliding), never widens a non-solid one.
        static Tile.ColliderType EffectiveCollider(LevelLayer layer, in ResolvedCell cell)
        {
            if (layer.solidTag != null)
            {
                bool tagged = (cell.tile is LevelTile t && t.HasTag(layer.solidTag)) ||
                              (cell.source != null && cell.source.HasTag(layer.solidTag));
                if (!tagged) return Tile.ColliderType.None;
            }

            if (cell.tile is LevelTile lt) return lt.colliderShape;

            return layer.colliderShape;
        }

        void PublishSpots()
        {
            if (level.placements == null) return;
            foreach (var p in level.placements)
            {
                if (p?.prop?.spots == null) continue;
                foreach (var s in p.prop.spots)
                {
                    if (s == null || string.IsNullOrEmpty(s.spotName)) continue;
                    // +0.5 puts the spot at the centre of its cell rather than the corner, so a spot
                    // authored at a whole-number offset lands where the tile looks like it is.
                    var so = Prop.TransformOffset(s.offset, p.rotation, p.mirrorX);
                    var cell = new Vector3(p.cell.x + so.x + 0.5f, p.cell.y + so.y + 0.5f, 0f);
                    spots[s.spotName] = Grid.LocalToWorld(Grid.CellToLocalInterpolated(cell));
                }
            }
        }

        /// <summary>
        /// Every clump placement's shapes on the Shapes layer <paramref name="layerId"/>, in WORLD space — a
        /// stamped shelf's floor footprint, ready for game code to turn into colliders or overlap tests. Reads
        /// each placed clump's own embedded metadata and places it through <see cref="MetaMapCells.ShapeToWorld"/>,
        /// the same mirror-then-quarter-turn the stamp applied to the clump's cells, so a footprint drawn on the
        /// clump's base row lands on that row in every orientation.
        ///
        /// Nothing calls this on its own: what a layer means, and whether it becomes physics, is the game's call.
        /// Fills the caller's list (cleared first) in placement order, then authored shape order; returns the count.
        /// </summary>
        public int GetClumpShapes(string layerId, List<(ClumpPlacement placement, MetaWorldShape shape)> results,
            int frame = 0)
        {
            if (results == null) return 0;
            results.Clear();
            if (level == null || level.clumpPlacements == null || string.IsNullOrEmpty(layerId)) return 0;

            var shapes = new List<MetaShape>();
            foreach (var cp in level.clumpPlacements)
            {
                var meta = cp?.Resolve()?.meta;
                if (meta == null || meta.space != MapSpace.GridCells) continue;
                if (meta.GetShapes(layerId, shapes, frame) == 0) continue;
                foreach (var sh in shapes)
                    results.Add((cp, MetaMapCells.ShapeToWorld(sh, cp.cell, cp.rotation, cp.mirrorX, Grid)));
            }
            return results.Count;
        }

        void SpawnPlacementPrefabs()
        {
            // Gameplay prefabs come alive in Play only; an editing scene shows the level, not the fight.
            if (!Application.isPlaying || level.placements == null) return;

            GameObject root = null;
            foreach (var p in level.placements)
            {
                var choice = ResolvePrefabChoice(p);
                if (choice?.prefab == null) continue;
                if (root == null) root = NewPart("Props", transform);
                var inst = Instantiate(choice.prefab, CellToWorld(p.cell), Quaternion.identity, root.transform);
                inst.name = choice.prefab.name;

                // Hand every prop behaviour its context — this is what lets a stamped prefab ACT on the
                // part of the level it was stamped into.
                foreach (var b in inst.GetComponentsInChildren<PropBehaviour>(true))
                    b.Attach(this, p);
            }
        }

        /// The prefab entry a placement resolved to when it was placed; -1 falls back to the first entry
        /// with any weight, which is also what a freshly stamped prop gets.
        static PropPrefabChoice ResolvePrefabChoice(PropPlacement p)
        {
            var choices = p?.prop?.prefabChoices;
            if (choices == null || choices.Count == 0) return null;
            if (p.prefabChoice >= 0 && p.prefabChoice < choices.Count) return choices[p.prefabChoice];
            foreach (var c in choices) if (c != null && c.weight > 0f) return c;
            return null;
        }

        void SpawnDecals()
        {
            if (level.decals == null || level.decals.Count == 0) return;
            var root = NewPart("Decals", transform);

            foreach (var d in level.decals)
            {
                if (d == null) continue;

                var world = Grid.LocalToWorld(new Vector3(d.position.x, d.position.y, 0f));
                var rot = Quaternion.Euler(0f, 0f, d.rotation);

                if (d.prefab != null)
                {
                    if (!Application.isPlaying) continue;    // props are gameplay; scenery below is not
                    var inst = Instantiate(d.prefab, world, rot, root.transform);
                    inst.name = d.prefab.name;
                    inst.transform.localScale = new Vector3(d.scale.x, d.scale.y, 1f);
                    continue;
                }

                var sprite = d.IsAnimated ? d.animation[0] : d.sprite;
                if (sprite == null) continue;

                var go = NewPart("Decal", root.transform);
                go.transform.SetPositionAndRotation(world, rot);
                go.transform.localScale = new Vector3(d.scale.x, d.scale.y, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                var layer = level.GetLayer(d.layer);
                sr.sortingOrder = (layer != null ? layer.sortingOrder : 0) + d.sortingOrder;

                if (d.IsAnimated)
                {
                    var anim = go.AddComponent<DecalAnimator>();
                    anim.frames = d.animation;
                    anim.fps = d.animationFps;
                }
            }
        }

        // ── tall stamps: one object, one sort point ───────────────────────────────────

        readonly Dictionary<Vector2Int, Transform> stampGroupOf = new();

        /// The SortingGroup a multi-row stamp of a y-sorted layer renders in, for anything that must draw AS PART OF
        /// that object (a shelf's wares): parent it under the returned transform and give it a sortingOrder above 0
        /// (the stamp's tiles draw at 0 inside the group). Null when the cell is not part of such a stamp.
        public Transform SortGroupAt(Vector2Int cell) => stampGroupOf.TryGetValue(cell, out var t) ? t : null;

        /// ★ A TALL STAMP SORTS AS ONE OBJECT, AT ITS BASE (OutBurner 2026-09-25, T-0041).
        ///
        /// Individual mode sorts every TILE by its own cell. For a one-row wall that is the object's own sorting, but a
        /// shelf is a stamp drawn 4 rows tall and solid only on its bottom row: its upper rows are the shelf's HEIGHT
        /// drawn up the screen, not floor further back. Sorted per tile, an imp standing in the walkable cells behind
        /// the base sorted IN FRONT of those upper rows and was drawn standing on the shelf (measured with
        /// ScavengeShelfProbe.SortScan once the y-sort actually took effect). So each multi-row stamp's tiles render
        /// from their own small tilemap inside a SortingGroup whose pivot is the centre of the stamp's footprint row:
        /// the whole shelf is in front of anything whose feet are in front of that row and behind anything whose feet
        /// are behind it — and a body cannot stand inside it, it is solid.
        ///
        /// The layer's own tilemap KEEPS every tile, only tinted clear, so its colliders, collider-type reads
        /// (ScavengeGame's shot silhouettes) and cell queries are exactly what they were; nothing else changes.
        void GroupTallStamps(LevelLayer layer, Tilemap map, TilemapRenderer mainRenderer,
                             Dictionary<Vector2Int, ResolvedCell> cells, Transform parent)
        {
            if (level == null || level.clumpPlacements == null) return;
            GameObject root = null;
            foreach (var cp in level.clumpPlacements)
            {
                if (cp == null) continue;
                List<Vector2Int> mine = null;
                foreach (var (at, _, lyr) in level.ClumpCellsOf(cp))
                    if (lyr == layer.name && cells.ContainsKey(at) && !stampGroupOf.ContainsKey(at)) (mine ??= new List<Vector2Int>()).Add(at);
                if (mine == null) continue;

                int minY = int.MaxValue, maxY = int.MinValue, minX = int.MaxValue, maxX = int.MinValue, footY = int.MaxValue;
                foreach (var c in mine)
                {
                    minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
                    minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                    if (map.GetColliderType(new Vector3Int(c.x, c.y, 0)) != Tile.ColliderType.None) footY = Mathf.Min(footY, c.y);
                }
                if (maxY == minY) continue;              // one row tall: per-tile sorting already IS the object's
                if (footY == int.MaxValue) footY = minY; // nothing solid: its lowest row stands in for the footprint

                root ??= NewPart(layer.name + " (tall stamps)", parent);
                var go = NewPart($"stamp {minX},{minY}", root.transform);
                var a = CellToWorld(new Vector2Int(minX, footY));
                var b = CellToWorld(new Vector2Int(maxX, footY));
                go.transform.position = (a + b) * 0.5f;
                var group = go.AddComponent<UnityEngine.Rendering.SortingGroup>();
                group.sortingLayerID = mainRenderer.sortingLayerID;
                group.sortingOrder = mainRenderer.sortingOrder;

                var tilesGo = NewPart("tiles", go.transform);
                tilesGo.transform.SetPositionAndRotation(map.transform.position, map.transform.rotation);
                var sub = tilesGo.AddComponent<Tilemap>();
                sub.tileAnchor = map.tileAnchor;
                sub.color = map.color;
                var subR = tilesGo.AddComponent<TilemapRenderer>();
                subR.sharedMaterial = mainRenderer.sharedMaterial;
                subR.sortingLayerID = mainRenderer.sortingLayerID;
                subR.sortingOrder = 0;
                subR.enabled = mainRenderer.enabled;
                LevelTileVariants.Register(sub, new VariantSource(cells));
                builtMaps.Add(sub);

                foreach (var c in mine)
                {
                    var c3 = new Vector3Int(c.x, c.y, 0);
                    sub.SetTile(c3, cells[c].tile);
                    map.SetColor(c3, Color.clear);   // stays for physics; drawn by the stamp's own map instead
                    stampGroupOf[c] = go.transform;
                }
            }
        }

        // ── plumbing ────────────────────────────────────────────────────────────────

        sealed class VariantSource : LevelTileVariants.ISource
        {
            readonly Dictionary<Vector2Int, ResolvedCell> cells;
            public VariantSource(Dictionary<Vector2Int, ResolvedCell> cells) => this.cells = cells;

            public bool TryGetVariant(Vector3Int cell, out int variantIndex)
            {
                if (cells.TryGetValue(new Vector2Int(cell.x, cell.y), out var rc))
                {
                    variantIndex = rc.variant;
                    return true;
                }
                variantIndex = 0;
                return false;
            }
        }

        void UnregisterMaps()
        {
            foreach (var m in builtMaps) LevelTileVariants.Unregister(m);
            builtMaps.Clear();
        }

        static GameObject NewPart(string partName, Transform parent)
        {
            // Derived content is never saved with the scene — the asset is the only source of truth, and
            // a stale built copy in the scene file would be a second one.
            var go = new GameObject(partName) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.AddComponent<LevelInstancePart>();
            return go;
        }

        static void DestroyPart(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
