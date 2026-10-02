using System.Collections.Generic;
using System.IO;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace Laubrary.Cartographer.Editor
{
    /// The VIEW/EDIT dials a TilesetGridView renders by. Plain data the HOST owns and persists, because
    /// "how big are the cells" and "is the lattice on" are preferences of the window the grid sits in, not
    /// of the tileset — two windows showing the same tileset legitimately show it differently.
    [System.Serializable]
    public class TilesetGridOptions
    {
        [Tooltip("Display size of a grid cell, in pixels. VIEW only — tile data is untouched.")]
        public float cellZoom = 40f;

        [Tooltip("Lattice colour, 0 black … 1 white.")]
        public float lineBrightness = 0.75f;

        [Tooltip("Lattice opacity.")]
        public float lineAlpha = 0.35f;

        [Tooltip("Gapless view: adjacent tiles butt together and no lattice draws.")]
        public bool seamless;

        [Tooltip("In seamless view, pulse an oscillating frame around the hovered/selected cells.")]
        public bool seamlessMarks = true;

        [Tooltip("Dropping or pasting onto occupied cells replaces their contents.")]
        public bool overwriteOnDrop;

        [Tooltip("Every animated tile plays its frames right in the grid.")]
        public bool showAnimated;

        [Tooltip("Every Random tile cycles its variants right in the grid.")]
        public bool cycleRandoms;

        [Tooltip("Oscillation speed of the seamless hover/selection frames, cycles per second.")]
        public float oscSpeed = 1.2f;

        [Tooltip("Which tab is showing: 0 = Tiles, 1 = Clumps.")]
        public int tab;

        [Tooltip("Tiles tab click mode: 0 Select, 1 Collision, 2 Tags.")]
        public int tileEditMode;

        [Tooltip("Clumps tab click mode: 0 Select, 1 Layers, 2 Collision, 3 Tags.")]
        public int clumpEditMode;

        [Tooltip("The Tile Tag that Tags mode paints and tints with.")]
        public TileTag activeTag;

        /// Values a restored preferences blob could have corrupted, brought back inside their ranges.
        public void Clamp()
        {
            cellZoom = Mathf.Clamp(cellZoom, 20f, 72f);
            lineBrightness = Mathf.Clamp01(lineBrightness);
            lineAlpha = Mathf.Clamp01(lineAlpha);
            oscSpeed = Mathf.Clamp(oscSpeed, 0.2f, 4f);
            tab = Mathf.Clamp(tab, 0, 1);
            tileEditMode = Mathf.Clamp(tileEditMode, 0, 2);
            clumpEditMode = Mathf.Clamp(clumpEditMode, 0, 3);
        }
    }

    /// What a TilesetGridView needs FROM the window hosting it, and what it hands BACK. Deliberately
    /// narrow: the grid owns the SELECTION and the EDIT ACTIONS; the host decides what a selection
    /// additionally MEANS (in the Tileset Builder: nothing beyond the edits — in the Cartographer: the
    /// paint brush), and owns the reserved status line the grid speaks through.
    public interface ITilesetGridHost
    {
        /// The tileset on show. May be null — the grid then draws its own "nothing to show" note.
        Tileset GridTileset { get; }

        /// The view dials. The same instance every call; the grid reads it live and never replaces it.
        TilesetGridOptions GridOptions { get; }

        /// Something window-sized and UNCLIPPED for the floating actions card to live in —
        /// `rootVisualElement`, never the element the click came from (ZuiPinCard's contract).
        VisualElement GridCardHost { get; }

        /// A BLOCKED operation. Must be unmissable — the host flashes it red on its reserved status line.
        void GridWarn(string message);

        /// An informational result ("Pasted 3 tile(s)…"), reported on the same reserved line.
        void GridReport(string message);

        /// Cells, tiles or atlas pixels changed. The host refreshes whatever ELSE of its own shows this
        /// tileset (the Builder's sheet tint and status; the Cartographer's canvas and brush thumbnails).
        void GridContentChanged();

        /// The grid's cell selection changed. THE seam where a selection acquires a second meaning.
        void GridSelectionChanged();

        /// A clump was picked in the Clumps tab (null when the clump selection was dropped).
        void GridClumpPicked(Clump clump);
    }

    /// The OPTIONAL half of the host seam: a window that also owns a source SHEET, so cells can be dragged
    /// out of it and PLUCKED straight into the grid. Structural rather than a boolean capability flag —
    /// a host that cannot supply sheet cells simply does not implement it, and the grid's drop handlers
    /// then never arm, which is a compile-time guarantee instead of a runtime promise.
    public interface ITilesetGridSheetSource
    {
        /// The live drag payload (normalised offset, source cell), or null when nothing is in flight.
        IReadOnlyList<(Vector2Int off, Vector2Int cell)> GridSheetDragCells { get; }

        /// Sheet cells dropped on the TILES grid: pluck them as loose tiles at `anchor`.
        void GridSheetDropTiles(Vector2Int anchor, bool push, Vector2Int pushDir);

        /// Sheet cells dropped on the CLUMPS grid: pluck the whole arrangement as ONE clump.
        void GridSheetDropClump(Vector2Int anchor);
    }

    /// The OPTIONAL half for a host where ONE tile of the tileset carries an extra ROLE in the host's own
    /// context. The Cartographer's active layer fills every unpainted cell with its BACKGROUND TILE, so "which
    /// tile is that" is a fact about the grid the author is looking at — and a fact they cannot see, until
    /// the grid shows it. Structural rather than a nullable property, exactly like
    /// <see cref="ITilesetGridSheetSource"/>: the Tileset Builder has no layers, does not implement this, and
    /// therefore has neither the badge nor the card actions — a compile-time guarantee, not a runtime promise.
    ///
    /// Deliberately named for a ROLE and not for "background tile": the grid must not learn what a level layer
    /// is, any more than it learned what a sprite sheet is. The host names the role; the grid only shows it.
    public interface ITilesetGridTileRole
    {
        /// The tile currently holding the role, or null when nothing does.
        LevelTile RoleTile { get; }

        /// The role as a SHORT constant noun for a button label — "background tile". Constant on purpose: it
        /// sits on the actions card, and a label that grew and shrank as the author switched layers would
        /// resize the card under their cursor (the stable-workspace rule).
        string RoleName { get; }

        /// The role IN CONTEXT, for tooltips and reports — "background tile for layer 'Terrain'". This is the
        /// half that names WHOSE role it is, which is the whole point of showing it.
        string RoleLabel { get; }

        /// False when there is nothing to set the role on (no active layer) — the actions then disable
        /// rather than vanish, so the card keeps its shape.
        bool CanSetRole { get; }

        /// Give the role to `tile`, or clear it with null. The host owns the Undo step.
        void SetRoleTile(LevelTile tile);
    }

    /// The OPTIONAL half for a host that PAINTS with what this grid shows. A SELECTION and a BRUSH are two
    /// different facts and must not be conflated: the eyedropper, a sampled region and a picked clump all set
    /// the brush while deliberately clearing the selection (a NOTIFYING clear would re-derive the brush FROM
    /// the selection and wipe the very brush they exist to set — which is why that divergence is correct and
    /// stays). Measured live 2026-08-03: Ctrl-clicking three cells gave brush 3 / selection 3, but an
    /// eyedropper gave brush 1 / selection 0 and a sampled region brush 2 / selection 0 — so in exactly the
    /// cases where the author did NOT pick in this grid, the grid showed nothing at all. This interface is the
    /// second, independent marking: whatever set the brush, the grid marks what is IN it.
    ///
    /// Structural rather than a nullable property, exactly like <see cref="ITilesetGridSheetSource"/> and
    /// <see cref="ITilesetGridTileRole"/>: the Tileset Builder paints nothing, does not implement this, and
    /// therefore has no brush marking whatsoever — a compile-time guarantee, not a runtime promise.
    public interface ITilesetGridBrush
    {
        /// The tiles currently loaded in the paint brush. The grid marks them so the author can always see
        /// WHAT they are painting with, including when the brush came from somewhere other than a click in
        /// this grid — an eyedropper on the canvas, a sampled region, a clump.
        IReadOnlyCollection<LevelTile> BrushTiles { get; }

        /// The CLUMP the brush IS, or null when the brush is loose tiles. Explicit rather than inferred back
        /// out of <see cref="BrushTiles"/>, because a clump's member tiles live inside the clump and are not
        /// in `set.tiles` at all: there is no cell in the Tiles tab that could ever carry their mark, so the
        /// Clumps tab has to be TOLD which clump to mark. Inferring it ("the clump whose tiles are all in the
        /// brush") would also mark a second clump that happened to share them, which is a different lie.
        Clump BrushClump { get; }
    }

    /// The tileset-editing operations that need no UI at all — forking, clump deletion, the tiles folder.
    /// Extracted alongside the grid so BOTH windows perform them identically; every one of them works off
    /// nothing but the Tileset asset, which is why none of them needed the window they used to live in.
    public static class TilesetForge
    {
        /// Everything one fork gesture minted, so a blocked placement can revert ATOMICALLY. `map` is
        /// source→fork; a null (or missing) fork means the caller should fall back to the source reference.
        public sealed class ForkBatch
        {
            public readonly Dictionary<LevelTile, LevelTile> map = new();
            public readonly List<LevelTile> created = new();
            public readonly Dictionary<int, List<Sprite>> addedBySize = new();   // fresh atlas sprites, per cell size
            public int skippedSprites;
        }

        /// Tiles land in a folder beside their tileset, named after it.
        public static string TilesFolder(Tileset set)
        {
            string setPath = AssetDatabase.GetAssetPath(set);
            string dir = Path.GetDirectoryName(setPath)?.Replace('\\', '/');
            string folder = $"{dir}/{set.name} Tiles";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(dir, $"{set.name} Tiles");
            return folder;
        }

        /// A clump display name no OTHER clump in this tileset is using: "Shelf", "Shelf 2", "Shelf 3"…
        ///
        /// A COURTESY AT CREATION TIME, and nothing more. It used to be load-bearing — a clump's name was its
        /// only identity handle, so two "Shelf"s meant every stamp of the second rendered the first and
        /// opening the second's metadata edited the first's. Identity is now <see cref="Clump.id"/>, a stable
        /// opaque GUID, so duplicate names are harmless; this survives only so a tileset does not fill up with
        /// six clumps called "Tile". NOTHING may force it on a name the user typed (see RenameClump).
        public static string UniqueClumpName(Tileset set, string basis, Clump except = null)
        {
            string root = string.IsNullOrWhiteSpace(basis) ? "Clump" : basis.Trim();
            if (set?.clumps == null) return root;

            bool Taken(string candidate)
            {
                foreach (var c in set.clumps)
                    if (c != null && !ReferenceEquals(c, except)
                        && string.Equals(c.displayName, candidate, System.StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }

            if (!Taken(root)) return root;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = root + " " + n;
                if (!Taken(candidate)) return candidate;
            }
            return root + " " + System.Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// Rename a clump. The name the user typed, verbatim — never uniquified, because two clumps may
        /// legitimately both be called "Shelf" and silently making one of them "Shelf 2" is the tool editing
        /// the user's data to suit itself. Returns the name taken, or null when nothing changed.
        ///
        /// A rename RE-POINTS NOTHING any more, and that is the point: a stamp records
        /// <see cref="Clump.id"/>, so the link survives a rename by construction — as does the clump's
        /// metadata, which is embedded in the clump itself. The one sweep still here is a MIGRATION: a
        /// placement recorded before ids existed resolves by name, so it would be orphaned by a rename. Those
        /// get re-pointed AND handed the id, so each one is fixed exactly once, forever.
        public static string RenameClump(Tileset set, Clump clump, string wanted, out int repointedPlacements)
        {
            repointedPlacements = 0;
            if (set == null || clump == null || string.IsNullOrWhiteSpace(wanted)) return null;
            string taken = wanted.Trim();
            string old = clump.displayName;
            if (string.Equals(taken, old, System.StringComparison.Ordinal)) return null;

            Undo.RecordObject(set, "Rename clump");
            string id = clump.EnsureId();
            clump.displayName = taken;
            EditorUtility.SetDirty(set);

            if (!string.IsNullOrEmpty(old))
                foreach (var guid in AssetDatabase.FindAssets("t:LevelAsset"))
                {
                    var lv = AssetDatabase.LoadAssetAtPath<LevelAsset>(AssetDatabase.GUIDToAssetPath(guid));
                    if (lv?.clumpPlacements == null) continue;
                    bool touched = false;
                    foreach (var p in lv.clumpPlacements)
                    {
                        if (p == null || p.tileset != set) continue;
                        if (!string.IsNullOrEmpty(p.clumpId)) continue;      // id-linked: nothing to re-point
                        if (!string.Equals(p.clumpName, old, System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (!touched) Undo.RecordObject(lv, "Rename clump");
                        p.clumpId = id;                                      // and never legacy again
                        p.clumpName = taken;
                        touched = true;
                        repointedPlacements++;
                    }
                    if (touched) EditorUtility.SetDirty(lv);
                }

            return taken;
        }

        /// Throw away everything a clump declares. Undoable in one step, and separated from any UI so the
        /// effect can be exercised without a human clicking through a dialog.
        public static void ClearClumpMeta(Tileset set, Clump clump)
        {
            if (set == null || clump == null) return;
            Undo.RecordObject(set, "Clear clump metadata");
            clump.meta = new Laubrary.MetaMapper.MetaMapData();
            clump.metaFootprint = default;      // no metadata, no footprint to have drifted from
            EditorUtility.SetDirty(set);
        }

        /// Fork tiles for one gesture: every source's pixels are COPIED into fresh atlas cells and a fresh
        /// LevelTile asset is minted per source — a genuinely independent tile, so pixel edits (flips) to
        /// either side never affect the other. One fork per DISTINCT source per gesture: cells that shared
        /// a tile inside the copied pattern keep sharing the single fork, so the pattern stays a pattern —
        /// independence is from the SOURCE, per paste/duplicate, not per cell. Legacy sheet-resident
        /// sprites are read from their own texture file, so a fork is also the moment a legacy tile becomes
        /// atlas-native. All new cells batch into ONE AddCells per cell size (one write + one import).
        /// Sprites whose source file is genuinely unreadable are counted in `skippedSprites`; those slots
        /// keep the source's sprite reference, and a tile with NO readable sprites maps back to itself.
        public static ForkBatch ForkTiles(Tileset set, IEnumerable<LevelTile> sources)
        {
            var batch = new ForkBatch();
            if (set == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(set))) return batch;

            // Distinct sources, each with its distinct sprites in order — an animated tile's variants[0]
            // IS its animation[0]; it forks once and wires back into both slots.
            var jobs = new List<(LevelTile src, List<Sprite> sprites)>();
            foreach (var src in sources)
            {
                if (src == null || batch.map.ContainsKey(src)) continue;
                batch.map[src] = null;
                var list = new List<Sprite>();
                var seen = new HashSet<Sprite>();
                if (src.variants != null) foreach (var s in src.variants) if (s != null && seen.Add(s)) list.Add(s);
                if (src.animation != null) foreach (var s in src.animation) if (s != null && seen.Add(s)) list.Add(s);
                jobs.Add((src, list));
            }
            if (jobs.Count == 0) return batch;

            // Fork display names first — the atlas cells are named after them.
            var taken = new HashSet<string>();
            foreach (var t in set.tiles) if (t != null && !string.IsNullOrEmpty(t.displayName)) taken.Add(t.displayName);
            var forkNames = new List<string>();
            foreach (var (src, _) in jobs)
            {
                string baseName = string.IsNullOrEmpty(src.displayName) ? src.name : src.displayName;
                string name = baseName + " copy";
                for (int n = 2; !taken.Add(name); n++) name = $"{baseName} copy {n}";
                forkNames.Add(name);
            }

            // Read every source block — one decode per texture file for the whole gesture.
            var flat = new List<Sprite>();
            foreach (var (_, sprites) in jobs) flat.AddRange(sprites);
            var blocks = TilesetAtlas.ReadCells(flat, out int unreadable);
            batch.skippedSprites += unreadable;

            // One AddCells per cell size for the whole gesture: one file write + one import each.
            var groups = new Dictionary<int, (List<Color32[]> blocks, List<string> names, List<(int job, int slot)> slots)>();
            int flatIdx = 0;
            for (int j = 0; j < jobs.Count; j++)
            {
                var sprites = jobs[j].sprites;
                for (int k = 0; k < sprites.Count; k++, flatIdx++)
                {
                    var block = blocks[flatIdx];
                    if (block == null) continue;   // unreadable — counted; the fork keeps the old reference
                    var r = sprites[k].rect;
                    int cs = Mathf.RoundToInt(r.width);
                    if (cs <= 0 || Mathf.RoundToInt(r.height) != cs) { batch.skippedSprites++; continue; }
                    if (!groups.TryGetValue(cs, out var g))
                        groups[cs] = g = (new List<Color32[]>(), new List<string>(), new List<(int, int)>());
                    g.blocks.Add(block);
                    g.names.Add(sprites.Count == 1 ? forkNames[j] : $"{forkNames[j]}_{k + 1}");
                    g.slots.Add((j, k));
                }
            }

            var newSprites = new Sprite[jobs.Count][];
            for (int j = 0; j < jobs.Count; j++) newSprites[j] = new Sprite[jobs[j].sprites.Count];
            foreach (var kv in groups)
            {
                var added = TilesetAtlas.AddCells(set, kv.Key, kv.Value.blocks, kv.Value.names);
                if (added == null) { batch.skippedSprites += kv.Value.blocks.Count; continue; }
                if (!batch.addedBySize.TryGetValue(kv.Key, out var l)) batch.addedBySize[kv.Key] = l = new List<Sprite>();
                for (int i = 0; i < added.Length; i++)
                {
                    if (added[i] == null) { batch.skippedSprites++; continue; }
                    l.Add(added[i]);
                    var (j, k) = kv.Value.slots[i];
                    newSprites[j][k] = added[i];
                }
            }

            // Mint the fork assets — the source's data verbatim, pointing at the new atlas cells.
            string folder = null;
            for (int j = 0; j < jobs.Count; j++)
            {
                var (src, sprites) = jobs[j];
                var remap = new Dictionary<Sprite, Sprite>();
                for (int k = 0; k < sprites.Count; k++)
                    if (newSprites[j][k] != null) remap[sprites[k]] = newSprites[j][k];
                if (sprites.Count > 0 && remap.Count == 0)
                {
                    // Nothing readable at all — a "fork" would just be a second name for the same pixels.
                    // Fall back to the source reference; the skip count already told the user.
                    batch.map[src] = src;
                    continue;
                }
                Sprite Map(Sprite s) => s != null && remap.TryGetValue(s, out var ns) ? ns : s;

                var t = ScriptableObject.CreateInstance<LevelTile>();
                t.displayName = forkNames[j];
                t.variantPolicy = src.variantPolicy;
                if (src.weights != null) t.weights.AddRange(src.weights);
                t.colliderShape = src.colliderShape;
                if (src.tags != null) t.tags.AddRange(src.tags);
                t.animationFps = src.animationFps;
                t.sourceSheetMd5 = src.sourceSheetMd5;
                t.sourceSheetPath = src.sourceSheetPath;
                if (src.sourceCells != null) t.sourceCells.AddRange(src.sourceCells);
                if (src.variants != null) foreach (var s in src.variants) t.variants.Add(Map(s));
                if (src.animation != null) foreach (var s in src.animation) t.animation.Add(Map(s));
                folder ??= TilesFolder(set);
                AssetDatabase.CreateAsset(t, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{forkNames[j]}.asset"));
                batch.map[src] = t;
                batch.created.Add(t);
            }
            return batch;
        }

        /// Fork ONE tile — see ForkTiles for the semantics. Gestures use the batch form so N forks still
        /// cost one atlas write per cell size; this is the single-tile convenience.
        public static LevelTile ForkTile(Tileset set, LevelTile source)
        {
            var batch = ForkTiles(set, new[] { source });
            return source != null && batch.map.TryGetValue(source, out var f) ? f : null;
        }

        /// Undo a just-made ForkTiles ATOMICALLY: the fork assets and their fresh atlas cells vanish again
        /// — the same rollback contract a blocked sheet pluck honours.
        public static void RollbackForks(Tileset set, ForkBatch batch)
        {
            if (batch == null) return;
            foreach (var kv in batch.addedBySize) TilesetAtlas.RemoveCells(set, kv.Key, kv.Value);
            foreach (var t in batch.created)
                if (t != null) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(t));
            if (batch.created.Count > 0 || batch.addedBySize.Count > 0) AssetDatabase.SaveAssets();
        }

        /// Ask, then delete. THE DIALOG STAYS HERE and only here: this is the one clump action undo cannot take
        /// back — it deletes member tile ASSETS off disk and frees their atlas cells — which is exactly the
        /// line drawn everywhere else in this pass (undoable ⇒ just do it and say so; irreversible ⇒ confirm).
        /// The DECISION and the EFFECT are separate methods on purpose, so the effect can be exercised without
        /// a human clicking a modal (a dialog hangs a headless driver, and "untestable" is itself a defect).
        /// Returns false when the user cancelled or there was nothing to do.
        public static bool DeleteClump(Tileset set, Clump clump)
        {
            if (clump == null || set == null) return false;
            if (!EditorUtility.DisplayDialog("Delete clump?",
                    $"'{clump.displayName}' and its {clump.cells.Count} member tile asset(s) will be deleted. " +
                    "Levels already painted with them will lose those cells.\n\n" +
                    "This one is NOT undoable: the tile assets leave the disk.", "Delete", "Cancel")) return false;
            DeleteClumpNow(set, clump);
            return true;
        }

        /// The EFFECT of "Delete clump", with no question attached — see <see cref="DeleteClump"/> for why the
        /// two are separate. Destroys the clump's member tile assets and atlas cells (they are exclusively
        /// this clump's) and drops the clump from the tileset.
        public static bool DeleteClumpNow(Tileset set, Clump clump)
        {
            if (clump == null || set == null) return false;

            var bySize = new Dictionary<int, List<Sprite>>();
            foreach (var pc in clump.cells)
            {
                var s = pc.tile != null && pc.tile.variants != null && pc.tile.variants.Count > 0
                    ? pc.tile.variants[0] : null;
                if (s != null)
                {
                    int cs = (int)s.rect.width;
                    if (!bySize.TryGetValue(cs, out var list)) bySize[cs] = list = new List<Sprite>();
                    list.Add(s);
                }
            }
            // Free the atlas cells BEFORE the sprites' tile assets go away.
            foreach (var kv in bySize) TilesetAtlas.RemoveCells(set, kv.Key, kv.Value);
            foreach (var pc in clump.cells)
                if (pc.tile != null) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(pc.tile));

            Undo.RecordObject(set, "Delete clump");
            set.clumps.Remove(clump);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return true;
        }
    }

    /// THE TILESET EDITOR, as a control. The tileset laid out as the 2D grid it IS — Tiles and Clumps
    /// tabs, marquee/Ctrl selection, drag to move, Ctrl-drag to duplicate as independent FORKS, delete,
    /// copy/paste (a paste always forks), flip H/V including ARRANGEMENT mirroring, per-cell Collision /
    /// Layers / Tags painting, and the whole thing viewable seamlessly so a tileset previews exactly as it
    /// will paint.
    ///
    /// It grew inside the Tileset Builder and was extracted 2026-08-03 because the Cartographer's palette
    /// wanted the same thing and a second copy of a 900-line editor is not a palette, it is a fork waiting
    /// to drift. The seam is `ITilesetGridHost`: the grid owns the selection and the edit actions, the host
    /// owns the tileset, the view dials, the status line — and decides what a selection additionally MEANS
    /// (the Builder: nothing more; the Cartographer: the paint brush).
    public sealed class TilesetGridView : VisualElement
    {
        /// The DragAndDrop generic-data key a sheet-cell drag carries. Public because the source of the
        /// drag (a sheet canvas) and its target (this grid) live in different classes.
        public const string SheetDragKey = "laubrary.sheet.cells";

        const string TagsFolder = "Assets/Cartographer/Tags";

        readonly ITilesetGridHost host;
        readonly ITilesetGridSheetSource sheets;   // null when the host owns no source sheet
        readonly ITilesetGridTileRole role;        // null when no tile of this tileset carries a host role
        readonly ITilesetGridBrush brushSrc;       // null when the host paints with nothing (the Builder)

        VisualElement toolbar;
        ScrollView gridScroll;
        TilesGrid tilesGrid;
        ClumpsGrid clumpsGrid;
        Button tagPickBtn, tagManageBtn;
        Label emptyNote;

        /// The Clumps tab's permanently-reserved metadata row.
        VisualElement metaRow;

        ZuiPinCard actionsCard;
        ZuiPinCard clumpCard;
        float scrollX;
        bool notifying;   // re-entrancy guard: a host reacting to a selection must not re-enter the grid

        Tileset Set => host.GridTileset;
        TilesetGridOptions O => host.GridOptions;

        /// `initialScrollX` gives back the horizontal place a PREVIOUS instance had, so a full window
        /// rebuild (which necessarily recreates this control) does not cost the user their position.
        public TilesetGridView(ITilesetGridHost host, float initialScrollX = 0f)
        {
            this.host = host;
            sheets = host as ITilesetGridSheetSource;
            role = host as ITilesetGridTileRole;
            brushSrc = host as ITilesetGridBrush;
            scrollX = initialScrollX;
            style.minHeight = 0f;
            SyncBrushTiles();   // BEFORE the build: a freshly recreated view must already show the brush
            Rebuild();
        }

        // ── what the host can ask of the grid ───────────────────────────────────
        /// Horizontal scroll position, for a host capturing its workspace before a full rebuild.
        public float ScrollX => gridScroll != null ? gridScroll.scrollOffset.x : scrollX;

        /// The tiles grid's width in cells — the divisor that turns a selected index into a grid position.
        public int Columns => Set != null ? Mathf.Max(1, Set.paletteColumns) : 1;

        /// The selected cells as tiles-list indices, ascending. Positions in the FIXED grid are pattern
        /// data, so a host building a pattern brush reads these against `Columns`.
        public IReadOnlyList<int> SelectedIndices => tilesGrid != null ? tilesGrid.SortedSelection() : System.Array.Empty<int>();

        public int SelectionCount => tilesGrid != null ? tilesGrid.SelectionCount : 0;

        /// The picked clump, or null. Setting it does NOT notify — a host syncing its own state back is
        /// not a user pick, and treating it as one is how a two-way binding becomes an infinite loop.
        public Clump SelectedClump
        {
            get
            {
                int i = clumpsGrid != null ? clumpsGrid.SelectedClumpIndex : -1;
                return Set != null && i >= 0 && i < Set.clumps.Count ? Set.clumps[i] : null;
            }
        }

        /// Re-select a clump by its stable id — how a HOST hands the user's selection back to a freshly
        /// constructed view. It has to be an id, not an index or a name: an index shifts when clumps are
        /// added or reordered and a name is not unique.
        ///
        /// Why this exists: this control is recreated whenever its host window rebuilds, and a window
        /// rebuilds on every undo/redo and every domain reload. Without it, one Ctrl+Z deselects the clump
        /// the author was working on — and since the whole Metadata row (Edit meta…, Clear, the "declares:"
        /// readout, the drift warning) describes the SELECTED clump, the row goes blank and disabled for no
        /// reason the author can see. Setting it does NOT notify: a host restoring its own state is not a
        /// user pick.
        public void SelectClumpById(string clumpId)
        {
            if (clumpsGrid == null || Set?.clumps == null || string.IsNullOrEmpty(clumpId)) return;
            for (int i = 0; i < Set.clumps.Count; i++)
            {
                var c = Set.clumps[i];
                if (c == null || !string.Equals(c.id, clumpId, System.StringComparison.Ordinal)) continue;
                clumpsGrid.SelectedClumpIndex = i;
                RefreshMetaRow();
                RefreshToolbar();
                return;
            }
        }

        /// Drop the cell selection. `notify: false` for a host CLEARING it as part of setting some other
        /// brush — otherwise the notification would immediately undo what the host just did.
        public void ClearSelection(bool notify = true)
        {
            tilesGrid?.ClearSelection();
            if (clumpsGrid != null) clumpsGrid.SelectedClumpIndex = -1;
            RefreshToolbar();
            RefreshMetaRow();
            if (notify) NotifySelectionChanged();
        }

        /// Rebuild the whole control: tabs, mode switch, toolbar, grids. Carries the user's scroll place
        /// and selection over to the successors — a refresh must never cost the user their position.
        public void Rebuild()
        {
            if (gridScroll != null) scrollX = gridScroll.scrollOffset.x;
            var keepSel = tilesGrid?.SnapshotSelection();
            int keepClump = clumpsGrid != null ? clumpsGrid.SelectedClumpIndex : -1;
            Clear();
            tilesGrid = null;
            clumpsGrid = null;
            gridScroll = null;
            toolbar = null;
            metaRow = null;
            tagPickBtn = tagManageBtn = null;

            if (Set == null)
            {
                emptyNote = (Label)Z.Text("No tileset to show.", ZuiText.Subtle,
                    "This grid edits a tileset; there is none in context right now.");
                Add(emptyNote);
                return;
            }

            toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.flexWrap = Wrap.NoWrap;
            Add(toolbar);

            // One row for both switches (the mockup layout): WHAT you look at (Tiles|Clumps) and HOW a
            // click acts (Select|…|Collision), side by side. View dials live on the box header above.
            var switchRow = new VisualElement();
            switchRow.style.flexDirection = FlexDirection.Row;
            switchRow.style.alignItems = Align.Center;
            switchRow.Add(Z.MiniRadio(O.tab, new[] { "Tiles", "Clumps" },
                "Tiles: loose tiles, one per cell, arranged into paintable patterns. Clumps: locked " +
                "multi-tile objects (urns, doors, wall columns) placed and painted as one thing.",
                i => { O.tab = i; Rebuild(); }));
            switchRow.Add(Z.HSpace());
            Add(switchRow);

            // Wide grids (high zoom × many columns) scroll horizontally inside their own box instead of
            // clipping — vertical stays with the pane's own scroller. Middle-drag moves both at once.
            var gs = new ScrollView(ScrollViewMode.Horizontal);
            gs.style.flexShrink = 0f;
            gridScroll = gs;
            // Reapply the captured offset once the fresh scroller has geometry: setting it now clamps
            // against a zero-sized layout and silently becomes 0 (the "pane snaps to the start" bug).
            if (scrollX > 0.5f)
            {
                float want = scrollX;
                gs.contentContainer.RegisterCallbackOnce<GeometryChangedEvent>(_ => gs.scrollOffset = new Vector2(want, 0f));
            }

            if (O.tab == 0)
            {
                // Tiles carry their own collision (auto-guessed at pluck); layer is a paint-time choice
                // and deliberately NOT tile data — so this tab has no Layers mode.
                switchRow.Add(Z.MiniRadio(O.tileEditMode, new[] { "Select", "Collision", "Tags" },
                    "Select: normal selection, moving and dragging. Collision: tiles tint by blocking — " +
                    "red = full square, yellow = sprite outline, no tint = pass-through; click a tile to cycle. " +
                    "Tags: tiles carrying the picked tag tint in its colour; click a tile to toggle the tag.",
                    i => { O.tileEditMode = i; Rebuild(); }));
                Add(gridScroll);
                tilesGrid = new TilesGrid(this);
                gridScroll.Add(tilesGrid);
                if (keepSel != null) tilesGrid.RestoreSelection(keepSel);
                RefreshToolbar();
                tilesGrid.RebuildCells();
            }
            else
            {
                // Edit modes: Select handles whole clumps; Layers and Collision PAINT per-cell properties,
                // with coloured translucent overlays so a clump's routing reads at a glance.
                switchRow.Add(Z.MiniRadio(O.clumpEditMode, new[] { "Select", "Layers", "Collision", "Tags" },
                    "Select: click and drag whole clumps. Layers: cells tint by target layer — blue = the " +
                    "stamped layer, orange = one layer in front (overhangs); click a cell to toggle. " +
                    "Collision: cells tint by blocking — red = full square, yellow = sprite outline, " +
                    "no tint = pass-through; click a cell to cycle. Tags: cells carrying the picked tag " +
                    "tint in its colour; click a cell to toggle the tag on its tile.",
                    i => { O.clumpEditMode = i; Rebuild(); }));
                // The selected clump's METADATA, above the grid and permanently reserved — see BuildMetaRow.
                Add(BuildMetaRow());
                Add(gridScroll);
                clumpsGrid = new ClumpsGrid(this);
                gridScroll.Add(clumpsGrid);
                if (keepClump >= 0) clumpsGrid.SelectedClumpIndex = keepClump;
                RefreshToolbar();
                clumpsGrid.RebuildCells();
                RefreshMetaRow();   // the row was built before the grid existed; now the selection is back

            }

            // Tag controls: always present on the row (reserved space), enabled only in a Tags mode.
            bool tagsMode = O.tab == 0 ? O.tileEditMode == 2 : O.clumpEditMode == 3;
            tagPickBtn = Z.Button("Pick tag…",
                "The Tile Tag that Tags mode paints with — cells carrying it tint in the tag's own colour.",
                ShowTagPicker);
            tagPickBtn.SetEnabled(tagsMode);
            switchRow.Add(tagPickBtn);
            RefreshTagButton();   // name + colour chip, in one place, so a rename can refresh it alone
            tagManageBtn = Z.Button("Tags…",
                "Create, rename and delete Tile Tag assets — the open-ended gameplay labels tiles carry.",
                ShowTagManager);
            tagManageBtn.SetEnabled(tagsMode);
            switchRow.Add(tagManageBtn);
        }

        /// Refresh the CELLS of whichever grid is showing, in place. Cheaper than Rebuild and — crucially —
        /// keeps the scroller, the selection and any pinned card exactly where they were.
        public void RefreshCells()
        {
            tilesGrid?.RebuildCells();
            clumpsGrid?.RebuildCells();
        }

        public void RepaintOverlay()
        {
            tilesGrid?.RepaintOverlay();
            clumpsGrid?.RepaintOverlay();
        }

        // ── the BRUSH marking ───────────────────────────────────────────────────
        /// The brush's tiles as a SET, cached. Membership is asked once per cell per repaint, so the host's
        /// collection is copied when the brush CHANGES rather than searched linearly on every cell.
        readonly HashSet<LevelTile> brushTiles = new();

        /// The brush changed — repaint the marks and re-word the reserved note. Deliberately NOT a Rebuild
        /// and not even a RefreshCells: an eyedropper click on the canvas fires this, and it must not cost
        /// the author their scroll position, their selection, or a pinned actions card.
        public void RefreshBrushMarks()
        {
            SyncBrushTiles();
            tilesGrid?.RepaintOverlay();
            clumpsGrid?.RepaintOverlay();
            RefreshToolbar();   // the note lives on the toolbar's reserved last line
        }

        void SyncBrushTiles()
        {
            brushTiles.Clear();
            var src = brushSrc?.BrushTiles;
            if (src == null) return;
            foreach (var t in src) if (t != null) brushTiles.Add(t);
        }

        /// The brush clump, but only when it belongs to the tileset ON SHOW. A brush outlives a layer switch,
        /// and marking cells of a clump that is not in this grid would mark whatever else happens to sit at
        /// those coordinates.
        Clump BrushClumpHere()
        {
            var c = brushSrc?.BrushClump;
            if (c == null || Set?.clumps == null) return null;
            foreach (var x in Set.clumps) if (ReferenceEquals(x, c)) return c;
            return null;
        }

        /// WHAT THE MARKS CANNOT SAY, said in words. The marks answer "which of these am I painting with";
        /// they cannot answer "why is nothing marked", and an unmarked grid is indistinguishable from an empty
        /// brush — the one reading that must never be wrong. Two states produce it: a CLUMP brush (its member
        /// tiles live inside the clump, so no cell of the Tiles tab could ever carry a mark), and a brush of
        /// tiles this tileset does not hold (a brush that outlived a layer switch).
        /// `full` = the sentence, for the label's TOOLTIP; otherwise the terse form that has to survive in a
        /// non-wrapping row with ~150px left in it. Measured 2026-08-03: the sentence form truncated mid-word
        /// at the pane's edge, which loses precisely the half that says where to look — so the row carries the
        /// short form and the hover carries the whole thing. No clump NAME in the short form on purpose: the
        /// reserved brush line directly beneath the grid already names it, and a name is the one part whose
        /// length the author controls.
        string BrushNote(bool full = false)
        {
            if (brushSrc == null) return "";
            var clump = brushSrc.BrushClump;
            if (O.tab == 0)
            {
                if (clump != null)
                    return full
                        ? $"The brush is the clump '{ClumpLabel(clump)}'. Its tiles live inside the clump, not in this " +
                          "tile list, so no cell here can be marked — the Clumps tab marks the clump itself."
                        : "clump brush · Clumps tab";
                if (brushTiles.Count == 0 || MarkedCellCount() > 0) return "";
                return full
                    ? $"The brush holds {brushTiles.Count} tile(s) that this tileset does not contain, so nothing " +
                      "here is marked. It was picked up while another tileset was active."
                    : "brush not in this tileset";
            }
            if (clump == null && brushTiles.Count > 0)
                return full
                    ? $"The brush holds {brushTiles.Count} loose tile(s), not a clump — they are marked over on the Tiles tab."
                    : $"{brushTiles.Count}-tile brush · Tiles tab";
            if (clump != null && BrushClumpHere() == null)
                return full
                    ? $"The brush is the clump '{ClumpLabel(clump)}', which belongs to a different tileset than the " +
                      "one shown here, so nothing is marked."
                    : "brush not in this tileset";
            return "";
        }

        int MarkedCellCount()
        {
            if (Set?.tiles == null || brushTiles.Count == 0) return 0;
            int n = 0;
            foreach (var t in Set.tiles) if (t != null && brushTiles.Contains(t)) n++;
            return n;
        }

        static string ClumpLabel(Clump c) =>
            c == null ? "clump" : string.IsNullOrEmpty(c.displayName) ? "unnamed" : c.displayName;

        /// THE BRUSH MARK, in one place so both tabs draw the identical thing: a TEAL frame set INSIDE the
        /// cell, over a dark backing so it reads on any art.
        ///
        /// Why this shape and this colour — it has to be unmistakable against everything already on a cell.
        /// It differs on TWO axes at once, so neither has to carry it alone:
        ///   • POSITION/FORM — it is INSET, floating clear of the cell's edge. Every other frame in this
        ///     control sits ON the boundary (selection, hover, marquee, the drop/paste ghost, the seamless
        ///     osc frames), and the remaining marks are either full-cell FILLS (collision red/yellow, tag
        ///     tints, the selection's orange wash) or CORNER GLYPHS (the violet role wedge top-left, the
        ///     clump identity badge top-right). Nothing else is an inset frame.
        ///   • COLOUR — teal-green, away from orange (selection), white (hover/marquee), violet (role),
        ///     red/yellow (collision) and blue (clump layers). It is deliberately GREENER than the transient
        ///     Alt-push ghost's cyan, which is in any case a boundary frame that only exists mid-drag.
        /// The colour cannot be load-bearing on its own, because TAG tints are arbitrary author-chosen
        /// colours — which is exactly why the inset FORM is the primary signal and the colour the reinforcer.
        /// A single closed subpath per Stroke, like every other frame here: bulk-stroking closed subpaths
        /// crashes Painter2D's miter-join tessellator.
        static void PaintBrushMark(Painter2D p, Rect cell, float cellSize)
        {
            float inset = Mathf.Clamp(cellSize * 0.13f, 2.5f, 6f);
            var r = new Rect(cell.xMin + inset, cell.yMin + inset,
                cell.width - inset * 2f, cell.height - inset * 2f);
            if (r.width <= 1f || r.height <= 1f) return;

            void Frame(float w, Color c)
            {
                p.strokeColor = c;
                p.lineWidth = w;
                p.BeginPath();
                p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin));
                p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax));
                p.ClosePath();
                p.Stroke();
            }

            Frame(3.5f, new Color(0f, 0f, 0f, 0.85f));
            Frame(1.75f, new Color(0.15f, 1f, 0.78f));
        }

        /// The Tileset Builder's sheet-drop entry point: place an already-minted pattern in the tiles grid.
        public bool PlaceMinted(List<(Vector2Int off, LevelTile tile)> pattern, Vector2Int anchor,
            bool push, Vector2Int pushDir)
            => tilesGrid != null && tilesGrid.PlaceMinted(pattern, anchor, push, pushDir);

        /// The VIEW dials, hosted ON a box's title row (the user's own mockup, 2026-08-01): they govern how
        /// the grid displays, so they cost no body row and stay put across tab switches.
        ///
        /// MEASURED BUDGET, 2026-08-03: these five come to ~380px, and the title row has ~475px to give in
        /// the Cartographer's pane. Adding to this list is not free — seven dials measured 494px and
        /// started overflowing the row into the "?" icon. If something new must live up here, something
        /// else has to leave.
        public void AddHeaderDials(ZuiBox box)
        {
            if (box == null) return;
            box.AddHeaderContent(Z.MicroSlider("Zoom", O.cellZoom, 20f, 72f,
                "Display size of the grid's cells — a view convenience only, tile data is untouched.",
                v => { O.cellZoom = v; RefreshCells(); }, 90f));
            box.AddHeaderContent(Z.MicroSlider("Lines", O.lineBrightness, 0f, 1f,
                "Grid line colour, black to white.",
                v => { O.lineBrightness = v; RepaintOverlay(); }, 70f));
            box.AddHeaderContent(Z.MicroSlider("Alpha", O.lineAlpha, 0f, 1f,
                "Grid line opacity.",
                v => { O.lineAlpha = v; RepaintOverlay(); }, 70f));
            var marksToggle = Z.ToggleButton("Osc frame",
                "In seamless view: pulse an oscillating frame around the hovered and selected cells, so " +
                "editing stays possible without the grid. Off = a pure, uninterrupted preview.",
                O.seamlessMarks, v => { O.seamlessMarks = v; RepaintOverlay(); });
            box.AddHeaderContent(Z.ToggleButton("Seamless",
                "Remove the gaps and grid entirely — adjacent tiles butt together and preview exactly as " +
                "they would paint into a level.",
                O.seamless, v =>
                {
                    O.seamless = v;
                    marksToggle.SetEnabled(v);
                    RefreshCells();
                    RepaintOverlay();
                }));
            marksToggle.SetEnabled(O.seamless);
            box.AddHeaderContent(marksToggle);
        }

        // ── the contextual toolbar ──────────────────────────────────────────────
        /// STABLE-LAYOUT RULE: every control exists at all times in one non-wrapping fixed-height row;
        /// what varies is VISIBILITY, never geometry — the workspace below must never jump because a
        /// selection appeared. (Hidden keeps its space; that is the whole point.)
        public void RefreshToolbar()
        {
            if (toolbar == null || Set == null) return;
            toolbar.Clear();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.flexWrap = Wrap.NoWrap;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.minHeight = 26f;
            toolbar.style.height = 26f;

            var set = Set;
            toolbar.Add(Z.Field("Grid", "The tileset grid's width and height, in cells. Height grows on its own when tiles are placed lower.",
                Z.Row(
                    Z.Int(set.paletteColumns, "Grid width in cells. Changing it re-reads every position.", v =>
                    {
                        Undo.RecordObject(set, "Tileset grid width");
                        set.paletteColumns = Mathf.Max(1, v);
                        EditorUtility.SetDirty(set);
                        Rebuild();
                    }, 36f),
                    Z.Int(set.paletteRows, "Grid height in cells.", v =>
                    {
                        Undo.RecordObject(set, "Tileset grid height");
                        set.paletteRows = Mathf.Max(1, v);
                        EditorUtility.SetDirty(set);
                        Rebuild();
                    }, 36f))));

            toolbar.Add(Z.ToggleButton("Overwrite", "When ON, dropping or pasting tiles replaces whatever the target cells hold. " +
                "When OFF, a drop only lands if every target cell is empty (hold Alt to PUSH occupants aside instead).",
                O.overwriteOnDrop, v => O.overwriteOnDrop = v));

            // "Animate" / "Cycle", not "Show animated" / "Cycle randoms": those two labels alone measured
            // 368px and pushed this non-wrapping row to 595px inside a 583px pane (2026-08-03). The label
            // is the affordance, the tooltip is the explanation — that is the rule that buys the width.
            toolbar.Add(Z.ToggleButton("Animate",
                "Play every animated tile's frames right in the grid, so the tileset previews alive. " +
                "Off: animated tiles hold their first frame; hovering one still previews it.",
                O.showAnimated, v => { O.showAnimated = v; tilesGrid?.RebuildCells(); }));

            toolbar.Add(Z.ToggleButton("Cycle",
                "Cycle every Random tile through its variants in the grid, so a group reads as a group. " +
                "Off: each shows its first variant; hovering one still previews it.",
                O.cycleRandoms, v => { O.cycleRandoms = v; tilesGrid?.RebuildCells(); }));

            int selCount = tilesGrid != null ? tilesGrid.SelectionCount : 0;

            // The action buttons live in the RIGHT-CLICK card, not here — chrome stays out of the window.
            var cancel = ToolbarButton("x", "Cancel paste", "Stop pasting.", () => tilesGrid?.CancelPaste());
            SetShown(cancel, tilesGrid != null && tilesGrid.Pasting);
            toolbar.Add(cancel);

            // Last in the row on purpose: its text length varies, and nothing sits after it to be moved.
            // ONE variable-width label, not two — a second one after it would be shoved every time the first
            // changed length, which is the stable-workspace rule stated the other way round.
            string status = selCount > 0 ? $"{selCount} selected · right-click for actions" : "";
            string note = BrushNote();
            if (note.Length > 0) status = status.Length > 0 ? status + " · " + note : note;
            string noteFull = BrushNote(true);
            var statusLabel = Z.Text(status, ZuiText.Small,
                "Cells currently selected in the grid — Copy/Delete/Paste/Flip live on the right-click card."
                + (noteFull.Length > 0 ? "\n\n" + noteFull : ""));
            // Truncate rather than widen: this row must not wrap and must not push the pane into a horizontal
            // scrollbar just because a clump has a long name.
            statusLabel.style.flexShrink = 1f;
            statusLabel.style.minWidth = 0f;
            statusLabel.style.overflow = Overflow.Hidden;
            statusLabel.style.whiteSpace = WhiteSpace.NoWrap;
            toolbar.Add(statusLabel);
        }

        /// Visibility, not display: a hidden control KEEPS its space, so toggling it cannot move anything.
        static void SetShown(VisualElement e, bool shown) =>
            e.style.visibility = shown ? Visibility.Visible : Visibility.Hidden;

        // ── the META MAP row (Clumps tab) ───────────────────────────────────────
        /// A clump's spatial metadata, reachable FROM WHERE THE CLUMP LIVES — the one-click path from "I have
        /// a clump" to "I am authoring its metadata", in the shared control so BOTH hosts (the Tileset Builder
        /// and the Cartographer's Tileset box) get it for free.
        ///
        /// STABLE-LAYOUT RULE, same as the toolbar above it: ONE fixed-height, non-wrapping row that always
        /// exists. What changes between "no clump selected", "clump with no map" and "clump with a map" is the
        /// row's CONTENTS and its controls' enabled state — never its geometry, so the grid under the user's
        /// cursor can never jump. The variable-length note goes LAST, where nothing follows it to be pushed.
        VisualElement BuildMetaRow()
        {
            metaRow = new VisualElement();
            metaRow.style.flexDirection = FlexDirection.Row;
            metaRow.style.flexWrap = Wrap.NoWrap;
            metaRow.style.alignItems = Align.Center;
            metaRow.style.height = 26f;
            metaRow.style.minHeight = 26f;
            metaRow.style.flexShrink = 0f;
            RefreshMetaRow();
            return metaRow;
        }

        const string MetaRowTip =
            "The selected clump's METADATA: what this clump IS, and where things happen on it. A layer named " +
            "'LootShelf' is what makes the clump a loot shelf — the layer merely EXISTING is already the whole " +
            "answer; Points marks on it are where the wares sit. It lives ON the clump, inside this tileset " +
            "asset: there is no separate file to create, name or lose.";

        /// Rebuild the row's contents in place. Never touches the row's own height, and never adds or removes
        /// the row itself.
        internal void RefreshMetaRow()
        {
            if (metaRow == null) return;
            metaRow.Clear();

            var set = Set;
            var clump = SelectedClump;
            bool declares = clump != null && clump.DeclaresAnything;

            var label = Z.Text("Metadata", ZuiText.Small, MetaRowTip);
            label.style.width = 62f;
            label.style.flexShrink = 0f;
            metaRow.Add(label);

            var edit = Z.Button("Edit meta…",
                "Open this clump's metadata in MetaMapper, drawn on the clump's OWN art — so a dot you place " +
                "on a board is on that board in every level the clump is stamped into. Nothing to create " +
                "first: the metadata is part of the clump.",
                () => OpenMetaForSelectedClump());
            edit.style.width = 86f;
            edit.SetEnabled(clump != null);
            metaRow.Add(edit);

            var clear = Z.Button("Clear",
                "Throw away everything this clump declares — every layer and every mark on it. Happens " +
                "immediately, no confirmation: Ctrl+Z brings it all back.",
                () => ClearMetaOfSelectedClump());
            clear.style.width = 50f;
            clear.SetEnabled(declares);
            metaRow.Add(clear);

            // LAST, because its length varies with the content: nothing sits after it to be shoved.
            string note;
            if (set == null) note = "";
            else if (clump == null) note = "Select a clump below to say what it is.";
            else if (!declares) note = $"'{clump.displayName}' declares nothing yet.";
            else
            {
                note = $"'{clump.displayName}' declares: {string.Join(", ", clump.meta.LayerIds)}";
                // Drift is surfaced HERE too, not only inside MetaMapper: the author who just added a cell to
                // this clump is looking at THIS grid, and finding out later — from marks that quietly moved to
                // different boards — is the failure mode.
                if (ClumpMetaSubject.DriftOf(clump) != null)
                    note += "  ⚠ the clump changed shape since — open Edit meta… to place it";
            }
            var noteLabel = Z.Text(note, ZuiText.Small, MetaRowTip);
            noteLabel.style.flexShrink = 1f;
            noteLabel.style.minWidth = 0f;
            noteLabel.style.marginLeft = 6f;
            noteLabel.style.overflow = Overflow.Hidden;
            noteLabel.style.whiteSpace = WhiteSpace.NoWrap;
            metaRow.Add(noteLabel);
        }

        /// Open the selected clump's metadata. NOTHING ELSE — no rename, no dialog, no surprise.
        ///
        /// This used to uniquify the clump's display name first, because a clump's identity WAS its name and
        /// two "Shelf"s meant opening one edited the other's metadata. That was treating the symptom: pressing
        /// "Edit meta…" renamed the user's clump out from under them. Identity is now a stable opaque id
        /// (<see cref="Clump.id"/>), so duplicate display names are simply harmless and the fix is gone rather
        /// than made politer.
        void OpenMetaForSelectedClump()
        {
            var set = Set;
            var clump = SelectedClump;
            if (set == null || clump == null) return;
            // onChanged keeps the identity badge and the row's "declares:" note live while the author works in
            // the other window — the §7 contract's whole point is that the host stays in step.
            ClumpMetaSubject.Open(set, clump, () => { if (panel != null) AfterMetaChanged(); });
        }

        /// NO CONFIRM DIALOG: clearing metadata is one undoable edit, so a modal question buys nothing but an
        /// interruption (and makes the action impossible to exercise outside a human's hands). The report line
        /// says what went and that Ctrl+Z brings it back. Confirmation is reserved for what undo cannot fix —
        /// "Delete clump", which erases tile assets from disk.
        void ClearMetaOfSelectedClump()
        {
            var set = Set;
            var clump = SelectedClump;
            if (set == null || clump == null || !clump.DeclaresAnything) return;
            string had = string.Join(", ", clump.meta.LayerIds);
            TilesetForge.ClearClumpMeta(set, clump);
            AfterMetaChanged();
            host.GridReport($"'{clump.displayName}' declares nothing now — was: {had}. Ctrl+Z brings it back.");
        }

        /// A clump's metadata changed: the row's readout, the grid's identity badges and the host's own views
        /// all describe it. Deliberately NOT a Rebuild — that would cost the user their scroll and selection.
        void AfterMetaChanged()
        {
            RefreshMetaRow();
            clumpsGrid?.RebuildCells();
            RepaintOverlay();
            host.GridContentChanged();
        }

        /// Mark a control as the one that DESTROYS something, so WEIGHT — not only wording — tells it apart
        /// from the harmless control beside it. Added 2026-08-03 after the user asked outright "What is the
        /// difference between clear and delete?": the layer card carried a × that removes the whole LAYER one
        /// row above a × that merely empties one FIELD, same glyph, same size, and the destructive one on top.
        /// Lives here rather than in each window because both hosts' cards need the same signal; if a third
        /// caller appears it should become a ZUI control rather than a fourth copy.
        public static T Destructive<T>(T control) where T : VisualElement
        {
            control.style.color = new Color(1f, 0.45f, 0.4f);
            control.style.unityFontStyleAndWeight = FontStyle.Bold;
            return control;
        }

        /// An icon button that degrades to its label when the icon name doesn't resolve.
        static VisualElement ToolbarButton(string icon, string label, string tooltip, System.Action onClick)
        {
            var b = Z.Button(label, tooltip, onClick);
            var ic = Z.Icon(icon, 14f);
            if (ic != null)
            {
                b.text = "";
                ic.style.marginLeft = ic.style.marginRight = 4f;
                b.Add(ic);
                b.style.minWidth = 26f;
            }
            return b;
        }

        // ── host notifications ──────────────────────────────────────────────────
        void NotifySelectionChanged()
        {
            if (notifying) return;
            notifying = true;
            try { host.GridSelectionChanged(); }
            finally { notifying = false; }
        }

        void NotifyClumpPicked(Clump clump)
        {
            if (notifying) return;
            notifying = true;
            try { host.GridClumpPicked(clump); }
            finally { notifying = false; }
        }

        // ── Tile Tags: picker + CRUD ────────────────────────────────────────────
        /// A TileTag has a COLOUR but no picture, so it gets a colour chip where a visual asset would get a
        /// thumbnail — the ui-layout-rules "a thumbnail is a promise" ruling (2026-08-03). It is the same
        /// colour the canvas overlay tints that tag's cells with, so the picker and the map agree, and it
        /// costs exactly the width a blank thumbnail would have wasted.
        static VisualElement TagChip(TileTag tag)
        {
            var chip = new VisualElement { pickingMode = PickingMode.Ignore };
            chip.style.width = chip.style.height = 11f;
            chip.style.marginRight = 4f;
            chip.style.flexShrink = 0f;
            var c = tag != null ? tag.editorColor : new Color(0.5f, 0.5f, 0.5f, 1f);
            chip.style.backgroundColor = new Color(c.r, c.g, c.b, 1f);
            chip.style.borderTopWidth = chip.style.borderBottomWidth =
                chip.style.borderLeftWidth = chip.style.borderRightWidth = 1f;
            var edge = new Color(0f, 0f, 0f, 0.6f);
            chip.style.borderTopColor = chip.style.borderBottomColor =
                chip.style.borderLeftColor = chip.style.borderRightColor = edge;
            return chip;
        }

        static IEnumerable<TileTag> AllTags()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TileTag"))
            {
                var tag = AssetDatabase.LoadAssetAtPath<TileTag>(AssetDatabase.GUIDToAssetPath(guid));
                if (tag != null) yield return tag;
            }
        }

        /// Re-label the active-tag button in place. A rename must NOT rebuild the whole view: the view owns
        /// `tagManageBtn`, which is the open menu's ANCHOR, and destroying it out from under a live popover
        /// is how the second rename in one visit used to break.
        void RefreshTagButton()
        {
            if (tagPickBtn == null) return;
            tagPickBtn.text = O.activeTag != null ? O.activeTag.name : "Pick tag…";
            for (int i = tagPickBtn.childCount - 1; i >= 0; i--) tagPickBtn.RemoveAt(i);
            if (O.activeTag != null) tagPickBtn.Insert(0, TagChip(O.activeTag));
            RepaintOverlay();
        }

        /// Pick the tag Tags mode paints with. Rows are built by hand rather than as menu Items so each can
        /// carry its colour chip.
        void ShowTagPicker()
        {
            var menu = Z.Menu(tagPickBtn);
            menu.Custom((body, close) =>
            {
                int n = 0;
                foreach (var tag in AllTags())
                {
                    n++;
                    var tg = tag;
                    var row = Z.Button(tag.name,
                        string.IsNullOrEmpty(tag.description) ? "Paint with this tag." : tag.description,
                        () => { O.activeTag = tg; close(); RefreshTagButton(); });
                    row.style.unityTextAlign = TextAnchor.MiddleLeft;
                    row.Insert(0, TagChip(tg));
                    body.Add(row);
                }
                if (n == 0)
                    body.Add(Z.Text("No Tile Tags yet — use Tags… to create one.", ZuiText.Subtle,
                        "Tags are tiny named assets; gameplay decides what they mean."));
            });
            menu.Show();
        }

        /// The CRUD card: every Tile Tag as a rename-in-place row with a colour chip and a delete, plus New.
        ///
        /// Renames commit on Enter or blur (never per keystroke — each one is an asset operation), and the
        /// path is resolved AT COMMIT TIME from the tag object. Capturing the path when the row was built
        /// was the 2026-08-03 "I can add tags but I can't name them" bug: after one rename the asset had
        /// moved, so every later rename in the same open card addressed a path that no longer existed and
        /// failed — silently, because RenameAsset's error string was being discarded.
        void ShowTagManager()
        {
            var menu = Z.Menu(tagManageBtn);
            menu.Custom((body, close) =>
            {
                int n = 0;
                foreach (var tag in AllTags())
                {
                    n++;
                    var tg = tag;
                    var nameField = Z.TextInput(tag.name,
                        "Rename this tag — the asset renames with it. Commits on Enter, or when you click away.",
                        v => CommitTagRename(tg, v), 130f);
                    nameField.isDelayed = true;
                    // Safety net: a modal popover can be torn down by an outside click BEFORE the delayed
                    // field ever blurs, and the typed name would vanish with it.
                    nameField.RegisterCallback<DetachFromPanelEvent>(_ => CommitTagRename(tg, nameField.value));
                    body.Add(Z.Row(
                        TagChip(tg),
                        nameField,
                        Z.Button("×", "Delete this tag asset. Tiles that carried it simply lose the label.", () =>
                        {
                            if (!EditorUtility.DisplayDialog("Delete tag?",
                                    $"Delete Tile Tag '{tg.name}'? Tiles that carry it just lose the label.",
                                    "Delete", "Cancel")) return;
                            if (O.activeTag == tg) O.activeTag = null;
                            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(tg));
                            close();
                            Rebuild();
                        })));
                }
                if (n == 0)
                    body.Add(Z.Text("No Tile Tags yet.", ZuiText.Subtle,
                        "Tags are tiny named assets; gameplay decides what they mean."));

                body.Add(Z.Button("+ New tag", "Create a Tile Tag asset in " + TagsFolder + " and make it active.", () =>
                {
                    if (!AssetDatabase.IsValidFolder(TagsFolder))
                    {
                        if (!AssetDatabase.IsValidFolder("Assets/Cartographer"))
                            AssetDatabase.CreateFolder("Assets", "Cartographer");
                        AssetDatabase.CreateFolder("Assets/Cartographer", "Tags");
                    }
                    var tag = ScriptableObject.CreateInstance<TileTag>();
                    AssetDatabase.CreateAsset(tag, AssetDatabase.GenerateUniqueAssetPath(TagsFolder + "/New TileTag.asset"));
                    O.activeTag = tag;
                    close();
                    Rebuild();
                    ShowTagManager();   // reopen with the new row ready to rename
                }));
            });
            menu.Show();
        }

        /// One rename, resolved fresh and reported honestly. Returns quietly for a no-op edit so the
        /// blur-safety-net can call it as often as it likes.
        void CommitTagRename(TileTag tag, string wanted)
        {
            if (tag == null || string.IsNullOrWhiteSpace(wanted)) return;
            wanted = wanted.Trim();
            if (wanted == tag.name) return;
            string path = AssetDatabase.GetAssetPath(tag);
            if (string.IsNullOrEmpty(path)) { host.GridWarn("That tag has no asset on disk to rename."); return; }
            string error = AssetDatabase.RenameAsset(path, wanted);
            if (!string.IsNullOrEmpty(error)) { host.GridWarn($"Could not rename the tag: {error}"); return; }
            RefreshTagButton();
            host.GridReport($"Renamed the tag to '{tag.name}'.");
        }

        // ── the selection actions card ──────────────────────────────────────────
        /// The edit suite, on the right-click card rather than in window chrome. ZuiPinCard's `existing`
        /// overload is what makes a PINNED card stay where the user parked it instead of teleporting to
        /// each new right-click.
        internal void ShowActions(Vector2 panelPos)
        {
            var cardHost = host.GridCardHost;
            if (cardHost == null) return;
            actionsCard = ZuiPinCard.Show(actionsCard, cardHost, panelPos, "Selection",
                "Actions for the grid selection. Drag this bar to move the card; pin it to keep it open.",
                BuildActionsBody);
        }

        internal void CloseActions()
        {
            actionsCard?.Close();
            actionsCard = null;
        }

        /// The CLUMP's own right-click card. It replaced a two-item GenericMenu because a clump had no way to
        /// be RENAMED at all. A menu cannot host a text field; this card can, and it is the same ZuiPinCard
        /// chrome the tiles grid's actions already use.
        internal void ShowClumpCard(Vector2 panelPos, Clump clump)
        {
            var cardHost = host.GridCardHost;
            if (cardHost == null || clump == null) return;
            clumpCard = ZuiPinCard.Show(clumpCard, cardHost, panelPos, "Clump",
                "This clump's identity and its metadata. Drag this bar to move the card; pin it to keep it open.",
                body => BuildClumpCardBody(body, clump));
        }

        void BuildClumpCardBody(VisualElement body, Clump clump)
        {
            var set = Set;
            if (set == null) return;

            var nameField = Z.TextInput(clump.displayName,
                "The clump's name — a LABEL, nothing more. Two clumps may share one: a stamp and this clump's " +
                "metadata are both linked by a hidden stable id, so renaming can never orphan them and nothing " +
                "will rename this for you. Commits on Enter, or when you click away.",
                v =>
                {
                    string taken = TilesetForge.RenameClump(set, clump, v, out int repointed);
                    if (taken == null) return;
                    clumpsGrid?.RebuildCells();
                    RefreshMetaRow();
                    host.GridContentChanged();
                    host.GridReport(repointed > 0
                        ? $"Renamed to '{taken}'; {repointed} pre-id stamp(s) migrated to the id link."
                        : $"Renamed to '{taken}'.");
                }, 150f);
            nameField.isDelayed = true;
            body.Add(Z.Field("Name", "What this clump is called. Duplicates are allowed — identity is a " +
                                     "hidden id, not the name.", nameField));

            body.Add(Z.Button("Edit meta…",
                "Open this clump's metadata in MetaMapper, drawn on the clump's own art. Same as the " +
                "Metadata row above the grid — here too because this card is where a clump's identity lives.",
                () => { OpenMetaForSelectedClump(); clumpCard?.AfterAction(); }));

            // THE one irreversible action in this whole toolset — tinted, so it does not read like the
            // "Clear …" buttons a card away that merely empty a setting.
            body.Add(Destructive(Z.Button("Delete clump",
                $"DELETE the clump '{clump.displayName}' and its member tile assets, off the disk. Levels " +
                "painted with them lose those cells. Asks first, because this one is NOT undoable — unlike " +
                "every \"Clear …\" here, which only empties a setting or a cell.",
                () =>
                {
                    if (!TilesetForge.DeleteClump(set, clump)) return;
                    if (clumpsGrid != null) clumpsGrid.SelectedClumpIndex = -1;
                    clumpsGrid?.RebuildCells();
                    RefreshMetaRow();
                    host.GridContentChanged();
                    NotifyClumpPicked(null);
                    clumpCard?.Close();
                })));
        }

        void AfterCardAction() => actionsCard?.AfterAction();

        void BuildActionsBody(VisualElement body)
        {
            int selCount = tilesGrid != null ? tilesGrid.SelectionCount : 0;
            var copy = Z.Button("Copy", "Copy the selected tile(s) as a pattern. Pasting creates INDEPENDENT copies " +
                "— new tile assets with their own atlas pixels — so editing (e.g. flipping) a pasted tile never " +
                "affects the original.",
                () => { tilesGrid?.CopySelection(); AfterCardAction(); });
            // "Clear cells", not "Delete". It was "Delete" beside a "Clear background tile" two buttons
            // down, and one bare verb next to one scoped verb is the same ambiguity the layer card's two ×
            // buttons had ("What is the difference between clear and delete?", user, 2026-08-03). It is not
            // tinted destructive, because it is not: the cells empty, the tile assets stay on disk, Ctrl+Z
            // brings it back. The only genuinely irreversible action in this family is "Delete clump", which
            // IS tinted — see BuildClumpCardBody.
            var del = Z.Button("Clear cells", "Clear the selected CELL(S) of this tileset's grid — they go " +
                "empty. The tile assets stay on disk and Ctrl+Z brings the cells back. Nothing is deleted.",
                () => { tilesGrid?.DeleteSelection(); AfterCardAction(); });
            var paste = Z.Button("Paste", "Paste the copied pattern as INDEPENDENT copies: each paste mints new tile " +
                "assets with their own atlas pixels, so editing (e.g. flipping) a pasted tile never affects the " +
                "original. Cells that shared a tile within the pattern share ONE new copy per paste — the pattern " +
                "stays intact. Click a grid cell to drop it. Right-click cancels; Alt pushes.",
                () => { tilesGrid?.BeginPaste(); AfterCardAction(); });
            var flipH = Z.Button("Flip H", "Mirror the selected tile(s) left-right — every variant and animation frame. " +
                "With SEVERAL tiles selected, their ARRANGEMENT mirrors too, across the selection's own bounds. " +
                "Permanent for pixels: they rewrite in the tileset's atlas (every level using the tile shows the flip) " +
                "and are NOT in Undo — Undo restores only the arrangement. Flip again to restore everything exactly, " +
                "positions and pixels.",
                () => { tilesGrid?.FlipSelection(true); AfterCardAction(); });
            var flipV = Z.Button("Flip V", "Mirror the selected tile(s) top-bottom — every variant and animation frame. " +
                "With SEVERAL tiles selected, their ARRANGEMENT mirrors too, across the selection's own bounds. " +
                "Permanent for pixels: they rewrite in the tileset's atlas (every level using the tile shows the flip) " +
                "and are NOT in Undo — Undo restores only the arrangement. Flip again to restore everything exactly, " +
                "positions and pixels.",
                () => { tilesGrid?.FlipSelection(false); AfterCardAction(); });
            copy.SetEnabled(selCount > 0);
            del.SetEnabled(selCount > 0);
            paste.SetEnabled(TilesGrid.ClipboardCount > 0);
            flipH.SetEnabled(selCount > 0);
            flipV.SetEnabled(selCount > 0);
            body.Add(copy);
            body.Add(del);
            body.Add(paste);
            body.Add(flipH);
            body.Add(flipV);

            // The ROLE actions, when the host has a role to give (the Cartographer's active layer has a
            // background tile; the Tileset Builder has no layers and does not implement the interface, so
            // nothing below exists there at all). LAST and ALWAYS PRESENT, both of them: appending keeps
            // Copy/Delete/Paste/Flip exactly where the author's muscle memory left them, and building both
            // rows every time — enabled or not — means the card is the same height whatever the selection
            // is, so it cannot resize under the pointer.
            if (role == null) return;

            var single = selCount == 1 && tilesGrid != null ? TileAtIndex(tilesGrid.SortedSelection()[0]) : null;
            var roleTile = role.RoleTile;

            body.Add(Z.VSpace());
            var setRole = Z.Button($"Set as {role.RoleName}",
                $"Make the selected tile the {role.RoleLabel} — it takes effect immediately, and Ctrl+Z " +
                "undoes it. Select exactly one tile; the tile already holding the role cannot be re-set.",
                () =>
                {
                    role.SetRoleTile(single);
                    RepaintOverlay();
                    AfterCardAction();
                });
            setRole.SetEnabled(role.CanSetRole && single != null && single != roleTile);
            body.Add(setRole);

            var clearRole = Z.Button($"Clear {role.RoleName}",
                $"Clear the {role.RoleLabel} — nothing holds the role any more. This empties ONE SETTING; no " +
                "tile and no cell is touched. Immediate, and Ctrl+Z brings it back.",
                () =>
                {
                    role.SetRoleTile(null);
                    RepaintOverlay();
                    AfterCardAction();
                });
            clearRole.SetEnabled(role.CanSetRole && roleTile != null);
            body.Add(clearRole);
        }

        /// The tile at a tiles-grid index, for callers outside the grid class (the actions card).
        LevelTile TileAtIndex(int idx)
        {
            var set = Set;
            return set != null && idx >= 0 && idx < set.tiles.Count ? set.tiles[idx] : null;
        }

        // ── shared painting helper ──────────────────────────────────────────────
        /// Checkerboard styling shared by every canvas in this toolset: transparency must read as
        /// "nothing here", or black art and holes stay indistinguishable. Screen-fixed 16px squares
        /// (image-editor convention), 2×2 texture tiled by the GPU.
        public static void StyleAsChecker(VisualElement e, ref Texture2D tex)
        {
            if (tex == null)
            {
                var dark = new Color32(52, 52, 52, 255);
                var light = new Color32(68, 68, 68, 255);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                tex.SetPixels32(new[] { dark, light, light, dark });
                tex.Apply();
            }
            e.style.backgroundImage = Background.FromTexture2D(tex);
            e.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
            e.style.backgroundSize = new BackgroundSize(new Length(16f, LengthUnit.Pixel), new Length(16f, LengthUnit.Pixel));
        }

        // ══════════════════════════════════════════════════════════════════════
        // THE TILES GRID
        // ══════════════════════════════════════════════════════════════════════
        /// The tileset laid out as the 2D grid it IS: 1px gaps, marquee/Ctrl selection, drag to move a
        /// selection (reference-preserving), Ctrl-drag to duplicate it as independent FORKS (see
        /// TilesetForge.ForkTiles — since tiles are pixel-editable in place, a copy that shared atlas cells
        /// would be a trap), and empty cells as first-class citizens. Cell edits only — tile assets are
        /// never deleted here.
        internal sealed class TilesGrid : VisualElement
        {
            // Seamless view removes the gaps entirely: adjacent tiles butt together and preview exactly as
            // they would paint into a level — the whole point of position-as-pattern.
            float Gap => O.seamless ? 0f : 1f;
            float Cell => Mathf.Clamp(O.cellZoom, 20f, 72f);   // VIEW zoom only — tile data unaffected

            readonly TilesetGridView v;
            readonly VisualElement thumbs;    // Image children
            readonly VisualElement overlay;   // selection/marquee/ghost — IN FRONT, always
            readonly ZuiScrollPan scrollPan;  // middle-drag panning, shared with every scrolling canvas

            readonly HashSet<int> sel = new();
            static readonly List<(Vector2Int off, LevelTile tile)> clipboard = new();

            int hoverIdx = -1;
            bool marqueeing, dragPending, draggingTiles, dupDrag, ctrlAtDown, altHeld;
            int pendingToggle = -1;
            Vector2 downPos;
            int anchorIdx = -1;
            bool pasting;

            /// A right press is in flight and still AMBIGUOUS: it becomes the actions card if it never moves
            /// off its anchor cell, and a marquee if it does. It cannot be decided at press time, which is
            /// why the card no longer opens there.
            bool rightDown;

            /// Where the right press landed, in panel space — the card opens THERE on release, not at
            /// wherever the pointer drifted to.
            Vector2 rightDownPanelPos;

            public int SelectionCount => sel.Count;
            public static int ClipboardCount => clipboard.Count;
            public bool Pasting => pasting;

            TilesetGridOptions O => v.O;
            Tileset Set => v.Set;
            int Cols => Mathf.Max(1, Set.paletteColumns);
            int Rows => Set.EffectiveRows;

            public TilesGrid(TilesetGridView view)
            {
                v = view;
                style.marginTop = 4f;

                // Behind the thumbs: the same checkerboard as every other canvas here — a tile's
                // transparent pixels are its layering promise, and the grid must show them as such.
                checker = new VisualElement { pickingMode = PickingMode.Ignore };
                checker.style.position = Position.Absolute;
                checker.style.left = checker.style.top = 0f;
                Add(checker);

                thumbs = new VisualElement { pickingMode = PickingMode.Ignore };
                thumbs.style.position = Position.Absolute;
                Add(thumbs);

                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.style.position = Position.Absolute;
                overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0f;
                overlay.generateVisualContent += PaintOverlay;
                Add(overlay);

                // Middle-drag panning of the enclosing scrollers, and Ctrl+wheel over the cell zoom —
                // ZuiPanZoom's SCROLLVIEW-OFFSET mode. Registered first so its handlers see the pointer
                // before the selection gestures do.
                scrollPan = ZuiScrollPan.Attach(this, step =>
                {
                    O.cellZoom = Mathf.Clamp(O.cellZoom * (step > 0f ? 1.15f : 0.87f), 20f, 72f);
                    RebuildCells();
                });

                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);

                // A per-cell tooltip cannot live on the cell Images: they are pickingMode = Ignore, so UITK
                // never picks them and their `tooltip` string is never read (the grid carried a dead
                // `img.tooltip = displayName` for exactly that reason). The picked element is THIS grid, so
                // the grid answers, using the cell it already knows is hovered.
                //
                // ORDER MATTERS, and it is the whole reason these two lines are the way round they are:
                // setting `tooltip` below is what makes UITK register ITS OWN TooltipEvent handler, so
                // registering ours FIRST is what puts ours ahead of it — and ours stops the event
                // IMMEDIATELY, so the generic string cannot overwrite a per-tile answer. When the pointer
                // is NOT over a tile ours returns without stopping, and the generic one below answers.
                RegisterCallback<TooltipEvent>(OnTooltip);

                // The gestures, on the surface they act on — the fallback whenever no tile is under the
                // pointer, and the only place the right-drag marquee announces itself.
                tooltip = "Tiles: click one to pick it, RIGHT-DRAG to marquee a group (left-drag on a tile " +
                          "MOVES it, so the marquee lives on the right button), Ctrl-click to add, Ctrl-drag " +
                          "to duplicate. Right-click without dragging SELECTS the tile and opens the actions " +
                          "card on it — right-clicking inside a group you already selected keeps the group." +
                          // Only where there IS a brush: the Tileset Builder paints nothing, so naming a mark
                          // it can never draw would be inventing a feature in a tooltip.
                          (v.brushSrc != null
                              ? " A TEAL FRAME INSIDE a cell means that tile is loaded in the paint brush — " +
                                "however it got there (clicked here, eyedropped or sampled on the canvas). " +
                                "The orange border is the selection; the two are not the same thing."
                              : "");

                // A sheet selection dragged over lands here: dropping MINTS the cells as tiles at the drop
                // position, arrangement preserved — same rules as any placement (Overwrite / Alt-push).
                RegisterCallback<DragUpdatedEvent>(e =>
                {
                    if (v.sheets == null || DragAndDrop.GetGenericData(SheetDragKey) == null) return;
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    hoverIdx = CellAt(this.WorldToLocal(e.mousePosition));
                    overlay.MarkDirtyRepaint();
                });
                RegisterCallback<DragPerformEvent>(e =>
                {
                    if (v.sheets == null || DragAndDrop.GetGenericData(SheetDragKey) == null) return;
                    DragAndDrop.AcceptDrag();
                    DragAndDrop.SetGenericData(SheetDragKey, null);
                    var local = this.WorldToLocal(e.mousePosition);
                    int idx = CellAt(local);
                    if (idx < 0) return;
                    v.sheets.GridSheetDropTiles(IdxToCell(idx), e.altKey, PushDirAt(local, idx));
                });

                schedule.Execute(TickThumbAnimation).Every(80);
            }

            internal bool PlaceMinted(List<(Vector2Int off, LevelTile tile)> pattern, Vector2Int anchor,
                bool push, Vector2Int pushDir)
                => TryPlace(pattern, anchor, null, "Drop tiles from sheet", push, pushDir);

            // ── geometry ────────────────────────────────────────────────────────
            void EnsureListSize()
            {
                int n = Cols * Rows;
                while (Set.tiles.Count < n) Set.tiles.Add(null);
            }

            Vector2Int IdxToCell(int idx) => new(idx % Cols, idx / Cols);
            int CellToIdx(Vector2Int c) => c.y * Cols + c.x;

            Rect CellRect(Vector2Int c) => new(
                Gap + c.x * (Cell + Gap), Gap + c.y * (Cell + Gap), Cell, Cell);

            int CellAt(Vector2 p)
            {
                int x = Mathf.FloorToInt((p.x - Gap) / (Cell + Gap));
                int y = Mathf.FloorToInt((p.y - Gap) / (Cell + Gap));
                if (x < 0 || y < 0 || x >= Cols || y >= Rows) return -1;
                return y * Cols + x;
            }

            LevelTile TileAt(int idx) =>
                idx >= 0 && idx < Set.tiles.Count ? Set.tiles[idx] : null;

            /// The Alt-push direction: which side of the hovered cell's CENTRE the pointer sits on decides
            /// where displaced tiles go — drop left-of-centre and occupants shove left, and so on.
            Vector2Int PushDirAt(Vector2 pointer, int cellIdx)
            {
                if (cellIdx < 0) return Vector2Int.zero;
                var d = pointer - CellRect(IdxToCell(cellIdx)).center;
                return Mathf.Abs(d.x) >= Mathf.Abs(d.y)
                    ? new Vector2Int(d.x >= 0 ? 1 : -1, 0)
                    : new Vector2Int(0, d.y >= 0 ? 1 : -1);
            }

            // ── building ────────────────────────────────────────────────────────
            readonly VisualElement checker;
            Texture2D checkerTex;

            internal void RepaintOverlay() => overlay.MarkDirtyRepaint();

            public void RebuildCells()
            {
                EnsureListSize();
                float gw = Cols * (Cell + Gap) + Gap, gh = Rows * (Cell + Gap) + Gap;
                style.width = gw;
                style.height = gh;
                style.flexShrink = 0f;

                StyleAsChecker(checker, ref checkerTex);
                checker.style.width = gw;
                checker.style.height = gh;

                thumbs.Clear();
                cellImages.Clear();
                lastFrame.Clear();
                for (int i = 0; i < Set.tiles.Count; i++)
                {
                    var tile = Set.tiles[i];
                    if (tile == null) continue;
                    var r = CellRect(IdxToCell(i));
                    var img = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
                    img.style.position = Position.Absolute;
                    img.style.left = r.xMin;
                    img.style.top = r.yMin;
                    img.style.width = Cell;
                    img.style.height = Cell;
                    // The real FULL-CELL sprite, never a cropped thumbnail — thumbnails crop to visible
                    // pixels, which re-centres partial art and scrambles any multi-cell arrangement
                    // (the 2026-08-01 "my shelf clump is broken" report — it was only the preview).
                    img.sprite = tile.IsAnimated ? tile.animation[0] : tile.SpriteOfVariant(0);
                    thumbs.Add(img);
                    cellImages[i] = img;
                }
                overlay.BringToFront();
                overlay.MarkDirtyRepaint();
            }

            // ── live multi-tile preview: hovering (or single-selecting) a variant/animated tile cycles it ──
            readonly Dictionary<int, Image> cellImages = new();
            int animatingIdx = -1;

            readonly Dictionary<int, int> lastFrame = new();   // cell → last drawn frame, so unchanged frames cost nothing

            /// True when the grid-wide toggles keep this tile playing without hover.
            bool PlaysUnattended(LevelTile t) => t != null &&
                (t.IsAnimated ? O.showAnimated
                    : O.cycleRandoms && t.variants != null && t.variants.Count > 1);

            void TickThumbAnimation()
            {
                // Seamless osc frames pulse on this same tick — repaint only while something shows one.
                if (O.seamless && O.seamlessMarks && (hoverIdx >= 0 || sel.Count > 0))
                    overlay.MarkDirtyRepaint();

                int target = hoverIdx >= 0 && IsMulti(hoverIdx) ? hoverIdx
                    : sel.Count == 1 && IsMulti(First(sel)) ? First(sel) : -1;

                if (animatingIdx >= 0 && animatingIdx != target)
                {
                    // Only snap back to the static frame if the grid-wide toggles won't keep it playing.
                    if (!PlaysUnattended(TileAt(animatingIdx))) RestoreThumb(animatingIdx);
                    lastFrame.Remove(animatingIdx);
                }
                animatingIdx = target;

                // Grid-wide playback (Show animated / Cycle randoms): the whole tileset previews alive
                // instead of frame-one-of-everything.
                if (O.showAnimated || O.cycleRandoms)
                    foreach (var kv in cellImages)
                    {
                        if (kv.Key == target) continue;   // the hover/selection preview owns that cell
                        var t = TileAt(kv.Key);
                        if (PlaysUnattended(t)) AnimateCell(kv.Value, t, kv.Key);
                    }

                if (target < 0 || !cellImages.TryGetValue(target, out var img)) return;
                AnimateCell(img, TileAt(target), target);
            }

            void AnimateCell(Image img, LevelTile tile, int idx)
            {
                var frames = tile.IsAnimated ? tile.animation : tile.variants;
                if (frames == null || frames.Count == 0) return;
                float fps = tile.IsAnimated ? tile.animationFps : 4f;
                int f = (int)(EditorApplication.timeSinceStartup * fps) % frames.Count;
                if (lastFrame.TryGetValue(idx, out int prev) && prev == f) return;
                lastFrame[idx] = f;
                img.image = null;
                img.sprite = frames[f];
            }

            bool IsMulti(int idx)
            {
                var t = TileAt(idx);
                return t != null && (t.IsAnimated || (t.variants != null && t.variants.Count > 1));
            }

            static int First(HashSet<int> s) { foreach (var i in s) return i; return -1; }

            void RestoreThumb(int idx)
            {
                if (!cellImages.TryGetValue(idx, out var img)) return;
                var t = TileAt(idx);
                img.image = null;
                img.sprite = t != null ? (t.IsAnimated ? t.animation[0] : t.SpriteOfVariant(0)) : null;
            }

            // ── selection + actions ─────────────────────────────────────────────
            /// The selection as plain data, so a rebuild (which recreates this grid) can hand the user's
            /// selection to the successor — a refresh must never cost the user their place.
            public List<int> SnapshotSelection() => new(sel);

            public List<int> SortedSelection()
            {
                var list = new List<int>(sel);
                list.Sort();
                return list;
            }

            public void RestoreSelection(List<int> indices)
            {
                sel.Clear();
                if (indices != null)
                    foreach (var i in indices)
                        if (TileAt(i) != null) sel.Add(i);
                overlay.MarkDirtyRepaint();
            }

            public void ClearSelection()
            {
                if (sel.Count == 0) return;
                sel.Clear();
                overlay.MarkDirtyRepaint();
            }

            /// Everything that changes the selection ends here: the toolbar's count and the host's own
            /// reading of the selection (the Cartographer's paint brush) must never drift apart.
            void SelectionChanged()
            {
                v.RefreshToolbar();
                v.NotifySelectionChanged();
            }

            public void CopySelection()
            {
                clipboard.Clear();
                if (sel.Count == 0) return;
                int minX = int.MaxValue, minY = int.MaxValue;
                foreach (var i in sel) { var c = IdxToCell(i); minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); }
                foreach (var i in sel)
                {
                    var tile = TileAt(i);
                    if (tile == null) continue;
                    var c = IdxToCell(i);
                    clipboard.Add((new Vector2Int(c.x - minX, c.y - minY), tile));
                }
                v.RefreshToolbar();
            }

            public void DeleteSelection()
            {
                if (sel.Count == 0) return;
                Undo.RecordObject(Set, "Clear tileset cells");
                foreach (var i in sel) if (i < Set.tiles.Count) Set.tiles[i] = null;
                sel.Clear();
                Commit();
            }

            /// Flip every sprite of every selected tile — all variants plus all animation frames — in the
            /// tileset's atlas itself, via TilesetAtlas.FlipCells. Cells only hold references, so the same
            /// tile in several cells flips ONCE. Pixels are not undoable (a PNG rewrite); exactly
            /// self-inverse instead. With SEVERAL cells selected the ARRANGEMENT mirrors too: every
            /// selected cell relocates across the selection's own bounding box. Mirroring is an involution,
            /// so the target set is exactly the selection's mirror and never leaves the box (no grid
            /// growth); empty selected cells ride the permutation naturally. Blocked atomically — pixels
            /// included — when a mirrored target holds an UNSELECTED tile. The cell permutation IS
            /// undoable like any grid mutation; flip again = full restore, positions AND pixels.
            public void FlipSelection(bool horizontal)
            {
                if (sel.Count == 0) return;
                var tiles = new HashSet<LevelTile>();
                foreach (var i in sel)
                {
                    var t = TileAt(i);
                    if (t != null) tiles.Add(t);
                }
                if (tiles.Count == 0) return;

                // Multi-cell selection: compute the position mirror FIRST, so a blocked arrangement
                // blocks the WHOLE operation — no pixels flip either, no partial state ever.
                List<(int from, int to)> moves = null;
                if (sel.Count > 1)
                {
                    int minC = int.MaxValue, maxC = int.MinValue, minR = int.MaxValue, maxR = int.MinValue;
                    foreach (var i in sel)
                    {
                        var c = IdxToCell(i);
                        minC = Mathf.Min(minC, c.x); maxC = Mathf.Max(maxC, c.x);
                        minR = Mathf.Min(minR, c.y); maxR = Mathf.Max(maxR, c.y);
                    }
                    moves = new List<(int from, int to)>();
                    foreach (var i in sel)
                    {
                        var c = IdxToCell(i);
                        int to = CellToIdx(horizontal ? new Vector2Int(minC + maxC - c.x, c.y)
                            : new Vector2Int(c.x, minR + maxR - c.y));
                        // Targets inside the selection permute among themselves; a target OUTSIDE it
                        // must be empty, or the mirror would eat a bystander.
                        if (!sel.Contains(to) && TileAt(to) != null)
                        {
                            v.host.GridWarn("Flip blocked — the mirrored area overlaps unselected tiles.");
                            return;
                        }
                        moves.Add((i, to));
                    }
                }

                int flippedTiles = 0;
                var sprites = new List<Sprite>();
                foreach (var t in tiles)
                {
                    bool owned = false;
                    if (t.variants != null)
                        foreach (var s in t.variants) { sprites.Add(s); owned |= TilesetAtlas.OwnsSprite(Set, s); }
                    if (t.animation != null)
                        foreach (var s in t.animation) { sprites.Add(s); owned |= TilesetAtlas.OwnsSprite(Set, s); }
                    if (owned) flippedTiles++;
                }

                TilesetAtlas.FlipCells(Set, sprites, horizontal, out int skipped);

                // The position mirror: snapshot every source, clear all sources, write all targets — a
                // clean permutation, because the target set is exactly the selection's mirror. Undoable
                // like every other grid mutation (the pixel half above is not — self-inverse instead).
                if (moves != null)
                {
                    Undo.RecordObject(Set, horizontal ? "Flip tile arrangement horizontally" : "Flip tile arrangement vertically");
                    EnsureListSize();
                    var landed = new List<(int to, LevelTile tile)>();
                    foreach (var (from, to) in moves) landed.Add((to, TileAt(from)));
                    foreach (var (from, _) in moves) if (from < Set.tiles.Count) Set.tiles[from] = null;
                    foreach (var (to, tile) in landed) Set.tiles[to] = tile;
                    // The same tiles stay selected, at their mirrored cells.
                    sel.Clear();
                    foreach (var (to, _) in landed) sel.Add(to);
                    EditorUtility.SetDirty(Set);
                    AssetDatabase.SaveAssets();
                }

                // The grids render live sprites, so the reimport already carries the new pixels into every
                // cell Image on the next rebuild — the host's own baked thumbnails would not know, which
                // is what GridContentChanged is for.
                RebuildCells();
                v.host.GridContentChanged();
                SelectionChanged();
                string skippedNote = skipped > 0
                    ? $" {skipped} sprite(s) skipped (not on this tileset's atlas — re-pluck to make them flippable)."
                    : "";
                v.host.GridReport(flippedTiles == 0
                    ? (moves != null ? "Mirrored the arrangement — no pixels flipped." + skippedNote
                        : skipped > 0 ? "Nothing flipped —" + skippedNote
                        : "Nothing flipped — the selection has no sprites.")
                    : moves != null
                        ? $"Flipped {flippedTiles} tile(s) and mirrored their arrangement." + skippedNote
                        : $"Flipped {flippedTiles} tile(s) {(horizontal ? "horizontally" : "vertically")}." + skippedNote);
            }

            public void BeginPaste() { pasting = clipboard.Count > 0; v.RefreshToolbar(); overlay.MarkDirtyRepaint(); }
            public void CancelPaste() { pasting = false; v.RefreshToolbar(); overlay.MarkDirtyRepaint(); }

            /// Write a pattern at `anchor`. Shared by paste and drop; enforces the Overwrite toggle
            /// atomically — a placement either fully lands or nothing changes. With `push`, occupants of
            /// target cells are shoved one cell along `pushDir` instead (cascading; the grid grows when
            /// pushing down), so a selection can land in a crowd without overwriting anything.
            bool TryPlace(List<(Vector2Int off, LevelTile tile)> pattern, Vector2Int anchor,
                HashSet<int> ignoreOccupied, string undoLabel, bool push = false, Vector2Int pushDir = default)
            {
                var targets = new List<(int idx, LevelTile tile)>();
                foreach (var (off, tile) in pattern)
                {
                    var c = anchor + off;
                    if (c.x < 0 || c.x >= Cols || c.y < 0) return false;
                    if (c.y >= Rows)
                    {
                        Set.paletteRows = c.y + 1;    // the grid grows downward on demand
                        EnsureListSize();
                    }
                    int idx = CellToIdx(c);
                    EnsureListSize();
                    bool occupied = TileAt(idx) != null && (ignoreOccupied == null || !ignoreOccupied.Contains(idx));
                    if (occupied && !O.overwriteOnDrop && !push) return false;
                    targets.Add((idx, tile));
                }

                if (push && pushDir != Vector2Int.zero)
                {
                    // Simulate the shoves on a copy so the whole drop is atomic.
                    var sim = new List<LevelTile>(Set.tiles);
                    int simRows = Rows;
                    var targetSet = new HashSet<int>();
                    foreach (var (idx, _) in targets) targetSet.Add(idx);

                    // Farthest target along the push direction moves its chain first, so cascades don't
                    // trample each other.
                    var occupiedTargets = new List<int>();
                    foreach (var (idx, _) in targets)
                        if (sim[idx] != null && (ignoreOccupied == null || !ignoreOccupied.Contains(idx)))
                            occupiedTargets.Add(idx);
                    occupiedTargets.Sort((a, b) =>
                    {
                        var ca = IdxToCell(a); var cb = IdxToCell(b);
                        return (cb.x * pushDir.x + cb.y * pushDir.y).CompareTo(ca.x * pushDir.x + ca.y * pushDir.y);
                    });

                    foreach (var start in occupiedTargets)
                    {
                        if (sim[start] == null) continue;   // an earlier chain already moved it

                        // The contiguous occupied run from the target along the push direction.
                        var run = new List<int> { start };
                        while (true)
                        {
                            var nextCell = IdxToCell(run[run.Count - 1]) + pushDir;
                            if (nextCell.x < 0 || nextCell.x >= Cols || nextCell.y < 0) return false;
                            if (nextCell.y >= simRows)
                            {
                                if (pushDir.y <= 0) return false;
                                simRows = nextCell.y + 1;
                                while (sim.Count < Cols * simRows) sim.Add(null);
                            }
                            int nextIdx = nextCell.y * Cols + nextCell.x;
                            if (targetSet.Contains(nextIdx)) return false;   // pushing INTO the drop = ambiguous, refuse
                            if (sim[nextIdx] == null) { run.Add(nextIdx); break; }
                            run.Add(nextIdx);
                        }

                        // Shift the run one step, far end first; the start cell empties.
                        for (int i = run.Count - 1; i > 0; i--) sim[run[i]] = sim[run[i - 1]];
                        sim[run[0]] = null;
                    }

                    Undo.RecordObject(Set, undoLabel);
                    Set.tiles.Clear();
                    Set.tiles.AddRange(sim);
                    Set.paletteRows = Mathf.Max(Set.paletteRows, simRows);
                    foreach (var (idx, tile) in targets) Set.tiles[idx] = tile;
                    sel.Clear();
                    foreach (var (idx, _) in targets) sel.Add(idx);
                    Commit();
                    return true;
                }

                Undo.RecordObject(Set, undoLabel);
                foreach (var (idx, tile) in targets) Set.tiles[idx] = tile;
                sel.Clear();
                foreach (var (idx, _) in targets) sel.Add(idx);
                Commit();
                return true;
            }

            void MoveOrDuplicateSelection(Vector2Int delta, bool duplicate, bool push, Vector2Int pushDir)
            {
                if (sel.Count == 0 || delta == Vector2Int.zero && !duplicate) { return; }

                var pattern = new List<(Vector2Int off, LevelTile tile)>();
                int minX = int.MaxValue, minY = int.MaxValue;
                foreach (var i in sel) { var c = IdxToCell(i); minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); }
                foreach (var i in sel)
                {
                    var tile = TileAt(i);
                    if (tile == null) continue;
                    var c = IdxToCell(i);
                    pattern.Add((new Vector2Int(c.x - minX, c.y - minY), tile));
                }
                if (pattern.Count == 0) return;

                var sourceCells = duplicate ? null : new HashSet<int>(sel);
                var anchor = new Vector2Int(minX, minY) + delta;

                if (!duplicate)
                {
                    // Plain move stays REFERENCE-preserving — the same tiles, new cells, nothing minted.
                    // Clear sources inside the same undo step; TryPlace records first, so group the two.
                    Undo.RecordObject(Set, "Move tiles");
                    foreach (var i in sel) Set.tiles[i] = null;
                    if (!TryPlace(pattern, anchor, sourceCells, "Move tiles", push, pushDir))
                    {
                        // Blocked: put the sources back exactly as they were.
                        int k = 0;
                        foreach (var i in sel) { Set.tiles[i] = pattern[k].tile; k++; }
                        v.host.GridWarn("Move blocked — target cells are occupied. Hold Alt to push, enable Overwrite, or drop on empty cells.");
                        return;
                    }
                }
                else
                {
                    // Ctrl-drag duplicate FORKS — same rule as paste: independent copies of the sources,
                    // one fork per distinct source tile per gesture, shared by cells that shared a source.
                    var sources = new List<LevelTile>();
                    foreach (var (_, t) in pattern)
                        if (!sources.Contains(t)) sources.Add(t);
                    var batch = TilesetForge.ForkTiles(Set, sources);
                    var forked = new List<(Vector2Int off, LevelTile tile)>();
                    foreach (var (off, t) in pattern)
                        forked.Add((off, batch.map.TryGetValue(t, out var f) && f != null ? f : t));

                    if (TryPlace(forked, anchor, null, "Duplicate tiles", push, pushDir))
                        v.host.GridReport($"Duplicated {forked.Count} tile(s) as independent copies." +
                            (batch.skippedSprites > 0
                                ? $" {batch.skippedSprites} sprite source(s) unreadable — those still share the original pixels."
                                : ""));
                    else
                    {
                        TilesetForge.RollbackForks(Set, batch);
                        v.host.GridWarn("Duplicate blocked — target cells are occupied. Hold Alt to push, enable Overwrite, or drop on empty cells.");
                    }
                }
            }

            void Commit()
            {
                EditorUtility.SetDirty(Set);
                AssetDatabase.SaveAssets();
                RebuildCells();
                v.host.GridContentChanged();
                SelectionChanged();
            }

            // ── pointers ────────────────────────────────────────────────────────
            void OnDown(PointerDownEvent e)
            {
                if (e.button == 2) return;   // middle button belongs to ZuiScrollPan

                int idx = CellAt(e.localPosition);
                hoverIdx = idx;

                if (pasting)
                {
                    if (e.button == 1 || idx < 0) CancelPaste();
                    else
                    {
                        // COPY means FORK: a paste mints fresh tile assets with their own atlas pixels, so
                        // editing (e.g. flipping) a pasted tile can never touch the original. One fork per
                        // DISTINCT clipboard tile per paste — cells that shared a tile keep sharing the
                        // single fork, so the copied pattern stays a pattern. Every paste forks anew.
                        var sources = new List<LevelTile>();
                        foreach (var (_, t) in clipboard)
                            if (t != null && !sources.Contains(t)) sources.Add(t);
                        var batch = TilesetForge.ForkTiles(Set, sources);
                        var pattern = new List<(Vector2Int off, LevelTile tile)>();
                        foreach (var (off, t) in clipboard)
                            pattern.Add((off, t != null && batch.map.TryGetValue(t, out var f) && f != null ? f : t));

                        if (TryPlace(pattern, IdxToCell(idx), null, "Paste tiles",
                                e.altKey, PushDirAt(e.localPosition, idx)))
                        {
                            pasting = false;
                            v.host.GridReport($"Pasted {pattern.Count} tile(s) as independent copies." +
                                (batch.skippedSprites > 0
                                    ? $" {batch.skippedSprites} sprite source(s) unreadable — those still share the original pixels."
                                    : ""));
                        }
                        else
                        {
                            // Blocked placement: the forks minted for it must vanish again, atomically.
                            TilesetForge.RollbackForks(Set, batch);
                            v.host.GridWarn("Paste blocked — target cells are occupied. Hold Alt to push, enable Overwrite, or pick an empty spot.");
                        }
                    }
                    v.RefreshToolbar();
                    e.StopPropagation();
                    return;
                }
                if (e.button == 1)
                {
                    // THE RIGHT BUTTON DOES TWO THINGS, and which one is not knowable yet. A click opens the
                    // actions card (as it always has); a DRAG is a marquee — because the left button cannot
                    // marquee here at all once the grid is packed: a left drag starting on an occupied cell
                    // is a drag-MOVE, so a left marquee can only be born on an empty cell, and a full grid
                    // has none. Same press-is-ambiguous-until-it-moves disambiguation the level canvas uses.
                    rightDown = true;
                    rightDownPanelPos = e.position;
                    ctrlAtDown = e.ctrlKey;      // Ctrl ADDS to the selection, exactly as the left marquee does
                    downPos = e.localPosition;
                    anchorIdx = idx;
                    // A RIGHT PRESS SELECTS, exactly as a left press does (user, 2026-08-03: "when i right
                    // click on a tile, select it just like when left clicking"). Without it the card that
                    // opens on release carries Clear cells and both Flips and acts on whatever was selected
                    // BEFORE — a card aimed at a different tile than the one under the cursor. Done on
                    // PRESS, not release, so the feedback is immediate and matches the left button; a press
                    // that turns into a marquee is harmless, because CommitMarquee rewrites the selection
                    // from scratch anyway.
                    ClickSelect(idx, e.ctrlKey);
                    // Capture on the right button too, so a fast drag that leaves the grid mid-gesture still
                    // gets its PointerUp here and completes instead of stranding the marquee on screen.
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;

                // Collision mode: a left click cycles the tile's own blocking shape — no selection, no drag.
                if (O.tileEditMode == 1)
                {
                    var ct = TileAt(idx);
                    if (ct != null)
                    {
                        Undo.RecordObject(ct, "Tile collision");
                        ct.colliderShape = ct.colliderShape == Tile.ColliderType.Grid ? Tile.ColliderType.Sprite
                            : ct.colliderShape == Tile.ColliderType.Sprite ? Tile.ColliderType.None
                            : Tile.ColliderType.Grid;
                        EditorUtility.SetDirty(ct);
                        overlay.MarkDirtyRepaint();
                    }
                    e.StopPropagation();
                    return;
                }

                // Tags mode: a left click toggles the active tag on the tile.
                if (O.tileEditMode == 2)
                {
                    var tt = TileAt(idx);
                    if (tt != null && O.activeTag != null)
                    {
                        Undo.RecordObject(tt, "Tile tag");
                        if (!tt.tags.Remove(O.activeTag)) tt.tags.Add(O.activeTag);
                        EditorUtility.SetDirty(tt);
                        overlay.MarkDirtyRepaint();
                        v.host.GridContentChanged();
                    }
                    e.StopPropagation();
                    return;
                }

                ctrlAtDown = e.ctrlKey;
                downPos = e.localPosition;
                pendingToggle = -1;
                anchorIdx = idx;

                if (idx >= 0 && TileAt(idx) != null)
                {
                    if (e.ctrlKey && sel.Contains(idx)) pendingToggle = idx;        // maybe toggle-off, maybe dup-drag
                    else if (e.ctrlKey) { sel.Add(idx); overlay.MarkDirtyRepaint(); SelectionChanged(); }
                    else if (!sel.Contains(idx)) { sel.Clear(); sel.Add(idx); overlay.MarkDirtyRepaint(); SelectionChanged(); }
                    dragPending = true;
                }
                else
                {
                    if (!e.ctrlKey && sel.Count > 0) { sel.Clear(); overlay.MarkDirtyRepaint(); SelectionChanged(); }
                    marqueeing = true;
                }
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
            }

            /// The selection a CLICK makes — the RIGHT button's version of what the left button's press does
            /// inline. Two deliberate differences from a naive "select the cell":
            ///   • A cell ALREADY IN a multi-cell selection leaves the selection alone. Right-clicking one
            ///     tile of a group is "act on this group"; collapsing it to one cell would be worse than the
            ///     bug this fixes, since the card's actions would then hit a single cell instead of the group.
            ///   • Ctrl TOGGLES, matching Ctrl+left-click.
            /// Empty space clears, again matching the left button — and there it matters, because the card
            /// opens anyway and must not offer Clear cells / Flip aimed at cells the author is not pointing at.
            void ClickSelect(int idx, bool ctrl)
            {
                if (idx >= 0 && TileAt(idx) != null)
                {
                    if (ctrl) { if (!sel.Remove(idx)) sel.Add(idx); }
                    else if (sel.Contains(idx)) return;   // the group stands
                    else { sel.Clear(); sel.Add(idx); }
                }
                else
                {
                    if (ctrl || sel.Count == 0) return;
                    sel.Clear();
                }
                overlay.MarkDirtyRepaint();
                SelectionChanged();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (scrollPan.IsPanning) return;

                int idx = CellAt(e.localPosition);
                if (idx != hoverIdx || altHeld != e.altKey) { hoverIdx = idx; altHeld = e.altKey; overlay.MarkDirtyRepaint(); }

                // The right press commits to being a MARQUEE the moment it reaches a different cell — a
                // whole cell of travel, not a few pixels, so the jitter of an ordinary right-click can never
                // cost the user their selection and their card. From here it IS the left marquee: same
                // `marqueeing` flag, same overlay, same commit.
                if (rightDown && !marqueeing && anchorIdx >= 0 && idx >= 0 && idx != anchorIdx)
                    marqueeing = true;

                if (dragPending && !draggingTiles && (e.localPosition - (Vector3)downPos).sqrMagnitude > 16f)
                {
                    draggingTiles = true;
                    dupDrag = ctrlAtDown;
                    pendingToggle = -1;
                }
                if (draggingTiles || marqueeing || pasting) overlay.MarkDirtyRepaint();
            }

            void OnUp(PointerUpEvent e)
            {
                if (e.button == 2) return;   // ZuiScrollPan's

                // The right press resolves HERE, where it is finally knowable: it moved, so it was a
                // marquee; it did not, so it was the click that opens the actions card.
                if (e.button == 1 && rightDown)
                {
                    this.ReleasePointer(e.pointerId);
                    rightDown = false;
                    if (marqueeing)
                    {
                        marqueeing = false;
                        CommitMarquee(CellAt(e.localPosition));
                    }
                    else v.ShowActions(rightDownPanelPos);
                    overlay.MarkDirtyRepaint();
                    e.StopPropagation();
                    return;
                }

                this.ReleasePointer(e.pointerId);
                int idx = CellAt(e.localPosition);

                if (draggingTiles && anchorIdx >= 0 && idx >= 0)
                    MoveOrDuplicateSelection(IdxToCell(idx) - IdxToCell(anchorIdx), dupDrag,
                        e.altKey, PushDirAt(e.localPosition, idx));
                else if (pendingToggle >= 0)
                {
                    sel.Remove(pendingToggle);
                    SelectionChanged();
                }
                else if (marqueeing) CommitMarquee(idx);

                marqueeing = dragPending = draggingTiles = dupDrag = false;
                pendingToggle = -1;
                overlay.MarkDirtyRepaint();
            }

            /// Turn the rectangle from `anchorIdx` to `idx` into the selection. ONE implementation, shared by
            /// the left marquee (which can only be born on an empty cell) and the right one (which can start
            /// anywhere, tiles included) — two copies would be two behaviours, and Ctrl-adds is exactly the
            /// kind of detail that would drift out of one of them.
            void CommitMarquee(int idx)
            {
                if (anchorIdx < 0 || idx < 0) return;
                var a = IdxToCell(anchorIdx);
                var b = IdxToCell(idx);
                if (!ctrlAtDown) sel.Clear();
                for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
                    for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
                    {
                        int i = CellToIdx(new Vector2Int(x, y));
                        if (TileAt(i) != null) sel.Add(i);
                    }
                SelectionChanged();
            }

            /// The hovered tile's name — and, when it is the one holding the host's role, WHICH role, named
            /// with the thing it belongs to ("background tile for layer 'Terrain'"). The badge says "this tile
            /// is special"; this is the half that says what special means, which is the difference between a
            /// mark someone can act on and a mark they have to ask about.
            void OnTooltip(TooltipEvent e)
            {
                var t = TileAt(hoverIdx);
                if (t == null) return;   // over empty space — the grid's own gesture tooltip stands
                string text = string.IsNullOrEmpty(t.displayName) ? t.name : t.displayName;
                var r = v.role;
                if (r != null && r.RoleTile == t) text += " — " + r.RoleLabel;
                e.tooltip = text;
                e.rect = this.LocalToWorld(CellRect(IdxToCell(hoverIdx)));
                e.StopImmediatePropagation();   // ours wins outright — see the registration order above
            }

            // ── overlay ─────────────────────────────────────────────────────────
            void PaintOverlay(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;

                void Path(Rect r)
                {
                    p.BeginPath();
                    p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin));
                    p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax));
                    p.ClosePath();
                }

                // Seamless view: no fills, no lattice — the art IS the display. Hover/selection get their
                // own oscillating frames below (optional), everything else stays out of the way.
                if (!O.seamless)
                {
                    // Empty-cell fills batch fine (fill tessellation has no joins) — but each subpath must
                    // be PROPERLY closed with ClosePath.
                    p.fillColor = new Color(1f, 1f, 1f, 0.04f);
                    p.BeginPath();
                    for (int y = 0; y < Rows; y++)
                        for (int x = 0; x < Cols; x++)
                        {
                            var c = new Vector2Int(x, y);
                            if (TileAt(CellToIdx(c)) != null) continue;
                            var r = CellRect(c);
                            p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin));
                            p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax));
                            p.ClosePath();
                        }
                    p.Fill();

                    // DISCONNECTED SEGMENTS on purpose — bulk-stroking closed subpaths crashes Painter2D's
                    // miter-join tessellator (the 2026-08-01 launch-crash loop). Single-segment subpaths
                    // have no joins, so the crashing code never runs.
                    float b = O.lineBrightness;
                    p.strokeColor = new Color(b, b, b, O.lineAlpha);
                    p.lineWidth = 1f;
                    p.BeginPath();
                    for (int y = 0; y < Rows; y++)
                        for (int x = 0; x < Cols; x++)
                        {
                            var r = CellRect(new Vector2Int(x, y));
                            p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin));
                            p.MoveTo(new Vector2(r.xMax, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMax));
                            p.MoveTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax));
                            p.MoveTo(new Vector2(r.xMin, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMin));
                        }
                    p.Stroke();
                }

                // Collision assessment: occupied cells tint by blocking — red square, yellow outline,
                // nothing at all for pass-through. Same colour language as the Clumps tab.
                if (O.tileEditMode == 1)
                    for (int ti = 0; ti < Set.tiles.Count; ti++)
                    {
                        var tt = Set.tiles[ti];
                        if (tt == null || tt.colliderShape == Tile.ColliderType.None) continue;
                        p.fillColor = tt.colliderShape == Tile.ColliderType.Grid
                            ? new Color(1f, 0.25f, 0.2f, 0.38f)
                            : new Color(1f, 0.85f, 0.2f, 0.35f);
                        Path(CellRect(IdxToCell(ti)));
                        p.Fill();
                    }

                // Tags assessment: cells carrying the active tag tint in the tag's OWN colour.
                if (O.tileEditMode == 2 && O.activeTag != null)
                    for (int ti = 0; ti < Set.tiles.Count; ti++)
                    {
                        var tt = Set.tiles[ti];
                        if (tt == null || !tt.HasTag(O.activeTag)) continue;
                        var c = O.activeTag.editorColor;
                        p.fillColor = new Color(c.r, c.g, c.b, 0.4f);
                        Path(CellRect(IdxToCell(ti)));
                        p.Fill();
                    }

                // Oscillating white↔black on one shared clock — the same pulse every canvas here uses.
                float osc = 0.5f + 0.5f * Mathf.Sin((float)(EditorApplication.timeSinceStartup * O.oscSpeed * Mathf.PI * 2.0));

                if (!O.seamless)
                    foreach (var i in sel)
                    {
                        var r = CellRect(IdxToCell(i));
                        p.fillColor = new Color(1f, 0.75f, 0.2f, 0.25f);
                        Path(r); p.Fill();
                        p.strokeColor = Color.black; p.lineWidth = 3f; Path(r); p.Stroke();
                        p.strokeColor = new Color(1f, 0.75f, 0.2f); p.lineWidth = 1.5f; Path(r); p.Stroke();
                    }
                else if (O.seamlessMarks)
                    foreach (var i in sel)
                    {
                        var r = CellRect(IdxToCell(i));
                        p.strokeColor = new Color(osc, osc, osc, 1f); p.lineWidth = 2f; Path(r); p.Stroke();
                    }

                // THE BRUSH MARK — the tiles LOADED IN THE PAINT BRUSH. NOT the selection: the eyedropper, a
                // sampled region and a picked clump all set the brush and deliberately clear the selection,
                // so before this the grid showed nothing at all in exactly those cases. Drawn AFTER the
                // selection so a cell that is both (the common case) still shows both — the selection's
                // boundary frame outside, this inset frame within it. Painted in seamless view too: what you
                // are painting with is a fact about state, not about a view mode. See PaintBrushMark for why
                // an inset teal frame cannot be confused with anything else on a cell.
                if (v.brushTiles.Count > 0)
                    for (int ti = 0; ti < Set.tiles.Count; ti++)
                    {
                        // Every cell holding the tile, like the role badge: the same tile may sit in several
                        // cells and marking one of them would be a lie about the others.
                        var bt = Set.tiles[ti];
                        if (bt == null || !v.brushTiles.Contains(bt)) continue;
                        PaintBrushMark(p, CellRect(IdxToCell(ti)), Cell);
                    }

                // THE ROLE BADGE — which tile is the host's background fill, visible without clicking anything.
                // Drawn AFTER the selection so it survives being selected, and in three ways at once that
                // the selection is not: a filled WEDGE (not a frame), in the TOP-LEFT (the clumps tab's
                // identity badge is a square in the top-right), in VIOLET (selection is orange, hover white,
                // collision red/yellow, ghosts green/red/cyan). Painted in seamless view too — whose tile
                // this is is a fact about the data, not a view mode.
                var roleTile = v.role?.RoleTile;
                if (roleTile != null)
                    for (int ti = 0; ti < Set.tiles.Count; ti++)
                    {
                        // Every cell holding it, not just the first: the same tile may sit in several cells,
                        // and badging one of them would be a lie about the others.
                        if (Set.tiles[ti] != roleTile) continue;
                        var rr = CellRect(IdxToCell(ti));
                        float s = Mathf.Clamp(Cell * 0.36f, 9f, 20f);

                        void Wedge(float inset, float size)
                        {
                            p.BeginPath();
                            p.MoveTo(new Vector2(rr.xMin + inset, rr.yMin + inset));
                            p.LineTo(new Vector2(rr.xMin + inset + size, rr.yMin + inset));
                            p.LineTo(new Vector2(rr.xMin + inset, rr.yMin + inset + size));
                            p.ClosePath();
                            p.Fill();
                        }

                        p.fillColor = new Color(0f, 0f, 0f, 0.85f);   // backing, so it reads over any art
                        Wedge(0f, s + 2f);
                        p.fillColor = new Color(0.72f, 0.5f, 1f, 1f);
                        Wedge(1f, s);
                    }

                if (marqueeing && anchorIdx >= 0 && hoverIdx >= 0)
                {
                    var a = CellRect(IdxToCell(anchorIdx));
                    var b = CellRect(IdxToCell(hoverIdx));
                    var rect = Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                        Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
                    p.strokeColor = Color.black; p.lineWidth = 3f; Path(rect); p.Stroke();
                    p.strokeColor = Color.white; p.lineWidth = 1f; Path(rect); p.Stroke();
                }

                // Drop/paste ghost: each landing cell outlined, red when it would be blocked.
                List<(Vector2Int off, LevelTile tile)> ghost = null;
                Vector2Int ghostAnchor = default;
                HashSet<int> ignore = null;
                if (draggingTiles && anchorIdx >= 0 && hoverIdx >= 0 && sel.Count > 0)
                {
                    ghost = new List<(Vector2Int, LevelTile)>();
                    int minX = int.MaxValue, minY = int.MaxValue;
                    foreach (var i in sel) { var c = IdxToCell(i); minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); }
                    foreach (var i in sel)
                    {
                        var c = IdxToCell(i);
                        ghost.Add((new Vector2Int(c.x - minX, c.y - minY), TileAt(i)));
                    }
                    ghostAnchor = new Vector2Int(minX, minY) + (IdxToCell(hoverIdx) - IdxToCell(anchorIdx));
                    if (!dupDrag) ignore = sel;
                }
                else if (pasting && hoverIdx >= 0)
                {
                    ghost = clipboard;
                    ghostAnchor = IdxToCell(hoverIdx);
                }

                if (ghost != null)
                    foreach (var (off, _) in ghost)
                    {
                        var c = ghostAnchor + off;
                        if (c.x < 0 || c.x >= Cols || c.y < 0) continue;
                        bool blocked = c.y < Rows && TileAt(CellToIdx(c)) != null && !O.overwriteOnDrop
                                       && (ignore == null || !ignore.Contains(CellToIdx(c)));
                        var r = CellRect(c);
                        // Cyan = "occupied, but Alt is held: the occupant will be PUSHED aside, not blocked."
                        p.strokeColor = blocked
                            ? (altHeld ? new Color(0.25f, 0.85f, 1f) : new Color(1f, 0.25f, 0.2f))
                            : new Color(0.3f, 1f, 0.4f);
                        p.lineWidth = 2f;
                        Path(r); p.Stroke();
                    }

                if (!marqueeing && !draggingTiles && hoverIdx >= 0)
                {
                    var r = CellRect(IdxToCell(hoverIdx));
                    if (!O.seamless)
                    {
                        p.strokeColor = new Color(1f, 1f, 1f, 0.6f); p.lineWidth = 1f; Path(r); p.Stroke();
                    }
                    else if (O.seamlessMarks)
                    {
                        p.strokeColor = new Color(osc, osc, osc, 0.9f); p.lineWidth = 1.5f; Path(r); p.Stroke();
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // THE CLUMPS GRID
        // ══════════════════════════════════════════════════════════════════════
        /// A clump's arrangement is LOCKED by definition, so hover, selection and movement all act on the
        /// whole clump — grab any member cell and you hold the object. Creation is drag-in from a sheet;
        /// deletion is on the right-click menu; content editing is a later workflow, deliberately.
        internal sealed class ClumpsGrid : VisualElement
        {
            readonly TilesetGridView v;
            readonly VisualElement checker;
            Texture2D checkerTex;
            readonly VisualElement thumbs;
            readonly VisualElement overlay;
            readonly ZuiScrollPan scrollPan;

            int hoverClump = -1, selClump = -1;
            Vector2Int hoverCell = new(-1, -1);
            bool dragPending, draggingClump, sheetDropHover;
            Vector2 downPos;
            Vector2Int grabOff;             // grabbed cell relative to the dragged clump's anchor

            readonly Dictionary<Vector2Int, int> occupancy = new();

            TilesetGridOptions O => v.O;
            Tileset Set => v.Set;
            float Gap => O.seamless ? 0f : 1f;
            float Cell => Mathf.Clamp(O.cellZoom, 20f, 72f);
            int Cols => Mathf.Max(1, Set.clumpColumns);
            int Rows => Set.EffectiveClumpRows;

            public ClumpsGrid(TilesetGridView view)
            {
                v = view;
                style.marginTop = 4f;

                checker = new VisualElement { pickingMode = PickingMode.Ignore };
                checker.style.position = Position.Absolute;
                checker.style.left = checker.style.top = 0f;
                Add(checker);

                thumbs = new VisualElement { pickingMode = PickingMode.Ignore };
                thumbs.style.position = Position.Absolute;
                Add(thumbs);

                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.style.position = Position.Absolute;
                overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0f;
                overlay.generateVisualContent += PaintOverlay;
                Add(overlay);

                tooltip = "Clumps: locked tile arrangements placed as one object. Drag a sheet selection " +
                          "here to pluck it as a clump; drag a clump to move it; right-click picks it (same " +
                          "as a left click) and opens its actions card." +
                          (v.brushSrc != null
                              ? " A TEAL FRAME INSIDE the cells means that clump is loaded in the paint brush."
                              : "");

                scrollPan = ZuiScrollPan.Attach(this, step =>
                {
                    O.cellZoom = Mathf.Clamp(O.cellZoom * (step > 0f ? 1.15f : 0.87f), 20f, 72f);
                    RebuildCells();
                });

                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
                RegisterCallback<PointerLeaveEvent>(_ => { hoverClump = -1; sheetDropHover = false; overlay.MarkDirtyRepaint(); });

                RegisterCallback<DragUpdatedEvent>(e =>
                {
                    if (v.sheets == null || DragAndDrop.GetGenericData(SheetDragKey) == null) return;
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    sheetDropHover = CellAt(this.WorldToLocal(e.mousePosition), out hoverCell);
                    overlay.MarkDirtyRepaint();
                });
                RegisterCallback<DragPerformEvent>(e =>
                {
                    if (v.sheets == null || DragAndDrop.GetGenericData(SheetDragKey) == null) return;
                    DragAndDrop.AcceptDrag();
                    DragAndDrop.SetGenericData(SheetDragKey, null);
                    sheetDropHover = false;
                    if (CellAt(this.WorldToLocal(e.mousePosition), out var cell)) v.sheets.GridSheetDropClump(cell);
                });
                RegisterCallback<DragExitedEvent>(_ => { sheetDropHover = false; overlay.MarkDirtyRepaint(); });

                // Seamless osc frames pulse here too — repaint only while something shows one.
                schedule.Execute(() =>
                {
                    if (O.seamless && O.seamlessMarks && (hoverClump >= 0 || selClump >= 0))
                        overlay.MarkDirtyRepaint();
                }).Every(80);
            }

            internal void RepaintOverlay() => overlay.MarkDirtyRepaint();

            /// Selection carried across rebuilds — same stability contract as the tiles grid. Setting it
            /// never notifies the host: only a USER pick is a pick.
            internal int SelectedClumpIndex
            {
                get => selClump;
                set { selClump = value >= 0 && value < Set.clumps.Count ? value : -1; overlay.MarkDirtyRepaint(); }
            }

            Rect CellRect(Vector2Int c) => new(
                Gap + c.x * (Cell + Gap), Gap + c.y * (Cell + Gap), Cell, Cell);

            bool CellAt(Vector2 p, out Vector2Int cell)
            {
                cell = new Vector2Int(
                    Mathf.FloorToInt((p.x - Gap) / (Cell + Gap)),
                    Mathf.FloorToInt((p.y - Gap) / (Cell + Gap)));
                return cell.x >= 0 && cell.y >= 0 && cell.x < Cols && cell.y < Rows;
            }

            int ClumpAt(Vector2Int cell) => occupancy.TryGetValue(cell, out var i) ? i : -1;

            public void RebuildCells()
            {
                // THE SILENT MIGRATION, at the one moment every clump in this tileset is definitely in hand:
                // a clump authored before ids existed gets one. No dialog, no rename, no undo entry — an id is
                // bookkeeping the author never chose. Idempotent, so this costs nothing from the second run on,
                // and it dirties only when it actually minted something.
                if (Set != null && Set.EnsureClumpIds() > 0) EditorUtility.SetDirty(Set);

                occupancy.Clear();
                for (int i = 0; i < Set.clumps.Count; i++)
                {
                    var pr = Set.clumps[i];
                    if (pr?.cells == null) continue;
                    foreach (var pc in pr.cells) occupancy[pr.gridPos + pc.offset] = i;
                }

                float gw = Cols * (Cell + Gap) + Gap, gh = Rows * (Cell + Gap) + Gap;
                style.width = gw;
                style.height = gh;
                style.flexShrink = 0f;

                StyleAsChecker(checker, ref checkerTex);
                checker.style.width = gw;
                checker.style.height = gh;

                thumbs.Clear();
                foreach (var pr in Set.clumps)
                {
                    if (pr?.cells == null) continue;
                    string tip = DescribeClump(pr);
                    foreach (var pc in pr.cells)
                    {
                        if (pc.tile == null) continue;
                        var r = CellRect(pr.gridPos + pc.offset);
                        var img = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
                        img.tooltip = tip;
                        img.style.position = Position.Absolute;
                        img.style.left = r.xMin;
                        img.style.top = r.yMin;
                        img.style.width = Cell;
                        img.style.height = Cell;
                        // Full-cell sprite, never the cropped thumbnail — a clump preview must compose
                        // exactly like the level will render it.
                        img.sprite = pc.tile.IsAnimated ? pc.tile.animation[0] : pc.tile.SpriteOfVariant(0);
                        thumbs.Add(img);
                    }
                }
                overlay.BringToFront();
                overlay.MarkDirtyRepaint();
            }

            /// What a clump IS, in one hover line: its name, and — the point of the badge — the meanings it
            /// declares. "These three are shelves" has to be readable without opening anything.
            static string DescribeClump(Clump pr)
            {
                if (pr == null) return "";
                return pr.DeclaresAnything
                    ? $"{pr.displayName} — declares: {string.Join(", ", pr.meta.LayerIds)}"
                    : $"{pr.displayName} — declares nothing. Select it and hit \"Edit meta…\" above the grid " +
                      "to say what it is (that is how a shelf gets told apart from any other wall-shaped thing).";
            }

            bool CanPlace(Clump clump, Vector2Int anchor, int selfIdx)
            {
                foreach (var pc in clump.cells)
                {
                    var c = anchor + pc.offset;
                    if (c.x < 0 || c.y < 0 || c.x >= Cols) return false;
                    int at = ClumpAt(c);
                    if (at >= 0 && at != selfIdx) return false;
                }
                return true;
            }

            void OnDown(PointerDownEvent e)
            {
                if (e.button == 2) return;   // ZuiScrollPan's

                bool onGrid = CellAt(e.localPosition, out var cell);
                int idx = onGrid ? ClumpAt(cell) : -1;

                if (e.button == 1)
                {
                    if (idx >= 0)
                    {
                        // A RIGHT CLICK PICKS THE CLUMP, exactly as a left click does — same selection, same
                        // metadata row, and the same NotifyClumpPicked so the host's brush arms too (user,
                        // 2026-08-03). Without the notify, right-clicking a clump highlighted it and opened a
                        // card while the brush still held whatever was picked before. There is no ambiguity to
                        // wait out here the way the tiles grid has: this grid's marquee does not exist, so the
                        // press can decide immediately.
                        bool pickChanged = selClump != idx;
                        selClump = idx;
                        overlay.MarkDirtyRepaint();
                        v.RefreshMetaRow();
                        v.NotifyClumpPicked(Set.clumps[idx]);
                        if (pickChanged) v.RefreshToolbar();
                        v.ShowClumpCard(e.position, Set.clumps[idx]);
                    }
                    // Right-clicking empty space deliberately changes NOTHING: no card opens here, so
                    // dropping the pick would cost the author their brush and buy nothing. (The tiles grid
                    // DOES clear on empty, because there a card opens and must not aim at stale cells.)
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;

                // Layers / Collision / Tags modes: a left click PAINTS the property of the exact member
                // cell under the cursor — no selection, no dragging, just assessment and correction.
                if (O.clumpEditMode != 0)
                {
                    if (idx >= 0)
                    {
                        var pr = Set.clumps[idx];
                        foreach (var pc in pr.cells)
                        {
                            if (pr.gridPos + pc.offset != cell) continue;
                            if (O.clumpEditMode == 1)
                            {
                                Undo.RecordObject(Set, "Clump cell layer");
                                pc.layerShift = pc.layerShift == 0 ? 1 : 0;
                                EditorUtility.SetDirty(Set);
                            }
                            else if (O.clumpEditMode == 2 && pc.tile != null)
                            {
                                Undo.RecordObject(pc.tile, "Clump cell collision");
                                pc.tile.colliderShape =
                                    pc.tile.colliderShape == Tile.ColliderType.Grid ? Tile.ColliderType.Sprite
                                    : pc.tile.colliderShape == Tile.ColliderType.Sprite ? Tile.ColliderType.None
                                    : Tile.ColliderType.Grid;
                                EditorUtility.SetDirty(pc.tile);
                            }
                            else if (O.clumpEditMode == 3 && pc.tile != null && O.activeTag != null)
                            {
                                Undo.RecordObject(pc.tile, "Tile tag");
                                if (!pc.tile.tags.Remove(O.activeTag)) pc.tile.tags.Add(O.activeTag);
                                EditorUtility.SetDirty(pc.tile);
                            }
                            break;
                        }
                        overlay.MarkDirtyRepaint();
                        v.host.GridContentChanged();
                    }
                    e.StopPropagation();
                    return;
                }

                bool changed = selClump != idx;
                selClump = idx;
                if (idx >= 0)
                {
                    grabOff = cell - Set.clumps[idx].gridPos;
                    dragPending = true;
                    downPos = e.localPosition;
                    this.CapturePointer(e.pointerId);
                }
                overlay.MarkDirtyRepaint();
                // The reserved meta row always describes the SELECTED clump — including "nothing selected".
                v.RefreshMetaRow();
                // A clump click is a PICK — the host may turn it into a brush. Fires even when the same
                // clump is re-clicked, because re-picking is how a user re-arms a brush they replaced.
                v.NotifyClumpPicked(idx >= 0 ? Set.clumps[idx] : null);
                if (changed) v.RefreshToolbar();
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (scrollPan.IsPanning) return;

                bool onGrid = CellAt(e.localPosition, out var cell);
                hoverCell = cell;
                int idx = onGrid ? ClumpAt(cell) : -1;
                if (idx != hoverClump) { hoverClump = idx; overlay.MarkDirtyRepaint(); }

                if (dragPending && !draggingClump && (e.localPosition - (Vector3)downPos).sqrMagnitude > 16f)
                    draggingClump = true;
                if (draggingClump) overlay.MarkDirtyRepaint();
            }

            void OnUp(PointerUpEvent e)
            {
                if (e.button == 2) return;   // ZuiScrollPan's

                this.ReleasePointer(e.pointerId);
                if (draggingClump && selClump >= 0 && CellAt(e.localPosition, out var cell))
                {
                    var clump = Set.clumps[selClump];
                    var target = cell - grabOff;
                    if (CanPlace(clump, target, selClump))
                    {
                        Undo.RecordObject(Set, "Move clump");
                        clump.gridPos = target;
                        EditorUtility.SetDirty(Set);
                        RebuildCells();
                    }
                    else v.host.GridWarn("Move blocked — the clump needs free cells inside the clump grid.");
                }
                dragPending = draggingClump = false;
                overlay.MarkDirtyRepaint();
            }

            void PaintOverlay(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;

                void Path(Rect r)
                {
                    p.BeginPath();
                    p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin));
                    p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax));
                    p.ClosePath();
                }

                if (!O.seamless)
                {
                    // DISCONNECTED SEGMENTS — same Painter2D miter-join crash rule as every canvas here.
                    float b = O.lineBrightness;
                    p.strokeColor = new Color(b, b, b, O.lineAlpha);
                    p.lineWidth = 1f;
                    p.BeginPath();
                    for (int y = 0; y < Rows; y++)
                        for (int x = 0; x < Cols; x++)
                        {
                            var r = CellRect(new Vector2Int(x, y));
                            p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin));
                            p.MoveTo(new Vector2(r.xMax, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMax));
                            p.MoveTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax));
                            p.MoveTo(new Vector2(r.xMin, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMin));
                        }
                    p.Stroke();
                }

                // Layers / Collision / Tags assessment tints: every member cell of every clump gets a
                // coloured translucent overlay, so the whole tab's routing is readable in one glance.
                if (O.clumpEditMode != 0)
                    foreach (var pr in Set.clumps)
                    {
                        if (pr?.cells == null) continue;
                        foreach (var pc in pr.cells)
                        {
                            Color tint;
                            if (O.clumpEditMode == 1)
                                tint = pc.layerShift > 0
                                    ? new Color(1f, 0.6f, 0.15f, 0.45f)     // orange: one layer in front
                                    : new Color(0.25f, 0.5f, 1f, 0.28f);    // blue: the stamped layer
                            else if (O.clumpEditMode == 2)
                            {
                                var shape = pc.tile != null ? pc.tile.colliderShape : Tile.ColliderType.Grid;
                                // No collision = NO TINT — absence must read as absence.
                                if (shape == Tile.ColliderType.None) continue;
                                tint = shape == Tile.ColliderType.Grid ? new Color(1f, 0.25f, 0.2f, 0.38f)
                                    : new Color(1f, 0.85f, 0.2f, 0.35f);
                            }
                            else
                            {
                                // Tags mode: only cells carrying the active tag tint, in the tag's colour.
                                if (O.activeTag == null || pc.tile == null || !pc.tile.HasTag(O.activeTag)) continue;
                                var c = O.activeTag.editorColor;
                                tint = new Color(c.r, c.g, c.b, 0.4f);
                            }
                            p.fillColor = tint;
                            Path(CellRect(pr.gridPos + pc.offset));
                            p.Fill();
                        }
                    }

                // IDENTITY BADGES — a corner mark on every clump whose map declares at least one meaning, in
                // that layer's own colour. The user's real task is "these three clumps are shelves"; without
                // this, that fact is invisible until you click each one, which is how a property nobody can
                // see stops being authored at all (the same reasoning that gave tile-less props marker
                // colours). The tooltip names the declared layers; this says only "there is something here".
                foreach (var pr in Set.clumps)
                {
                    if (pr == null || !pr.DeclaresAnything) continue;
                    var pb = pr.Bounds;
                    // Top-right of the footprint IN GRID TERMS: the clump grid's y runs DOWN the screen, so
                    // the visual top row is the LOWEST y.
                    var corner = CellRect(new Vector2Int(pr.gridPos.x + pb.xMax - 1, pr.gridPos.y + pb.yMin));
                    var badge = new Rect(corner.xMax - 8f, corner.yMin + 2f, 6f, 6f);
                    var first = pr.meta.LayerAt(0);
                    p.fillColor = new Color(0f, 0f, 0f, 0.8f);
                    Path(new Rect(badge.xMin - 1f, badge.yMin - 1f, badge.width + 2f, badge.height + 2f));
                    p.Fill();
                    p.fillColor = first != null ? first.color : Color.white;
                    Path(badge);
                    p.Fill();
                }

                float osc = 0.5f + 0.5f * Mathf.Sin((float)(EditorApplication.timeSinceStartup * O.oscSpeed * Mathf.PI * 2.0));

                void StrokeClump(int idx, Color normal, float width, bool oscillate)
                {
                    if (idx < 0 || idx >= Set.clumps.Count || Set.clumps[idx]?.cells == null) return;
                    var pr = Set.clumps[idx];
                    p.strokeColor = oscillate ? new Color(osc, osc, osc, normal.a) : normal;
                    p.lineWidth = width;
                    foreach (var pc in pr.cells) { Path(CellRect(pr.gridPos + pc.offset)); p.Stroke(); }
                }

                // Selection: whole clump, always. Orange like the tiles grid, osc frame in seamless.
                if (selClump >= 0)
                {
                    if (!O.seamless)
                    {
                        var pr = selClump < Set.clumps.Count ? Set.clumps[selClump] : null;
                        if (pr?.cells != null)
                            foreach (var pc in pr.cells)
                            {
                                var r = CellRect(pr.gridPos + pc.offset);
                                p.fillColor = new Color(1f, 0.75f, 0.2f, 0.25f);
                                Path(r); p.Fill();
                            }
                        StrokeClump(selClump, new Color(1f, 0.75f, 0.2f), 1.5f, false);
                    }
                    else if (O.seamlessMarks)
                        StrokeClump(selClump, new Color(1f, 1f, 1f, 1f), 2f, true);
                }

                // THE BRUSH MARK — see PaintBrushMark. A clump brush is the only brush this tab can show, and
                // this tab is the only place it CAN be shown: a clump's member tiles live inside the clump,
                // not in the tileset's tile list, so no cell of the Tiles tab could ever carry the mark (the
                // Tiles tab says so in words instead — see BrushNote). Drawn after the selection so a clump
                // that is both picked and armed shows both.
                var brushClump = v.BrushClumpHere();
                if (brushClump?.cells != null)
                    foreach (var pc in brushClump.cells)
                        PaintBrushMark(p, CellRect(brushClump.gridPos + pc.offset), Cell);

                // Hover: whole clump, unless we are dragging one around.
                if (!draggingClump && hoverClump >= 0 && hoverClump != selClump)
                {
                    if (!O.seamless) StrokeClump(hoverClump, new Color(1f, 1f, 1f, 0.6f), 1f, false);
                    else if (O.seamlessMarks) StrokeClump(hoverClump, new Color(1f, 1f, 1f, 0.9f), 1.5f, true);
                }

                // Move ghost: the clump's footprint at the target, green when it fits, red when blocked.
                if (draggingClump && selClump >= 0 && selClump < Set.clumps.Count)
                {
                    var pr = Set.clumps[selClump];
                    var target = hoverCell - grabOff;
                    bool ok = CanPlace(pr, target, selClump);
                    p.strokeColor = ok ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.25f, 0.2f);
                    p.lineWidth = 2f;
                    foreach (var pc in pr.cells) { Path(CellRect(target + pc.offset)); p.Stroke(); }
                }

                // Sheet-drop ghost: where the plucked clump would land.
                var dragCells = v.sheets?.GridSheetDragCells;
                if (sheetDropHover && dragCells != null)
                    foreach (var (off, _) in dragCells)
                    {
                        var c = hoverCell + off;
                        bool ok = c.x >= 0 && c.y >= 0 && c.x < Cols && ClumpAt(c) < 0;
                        p.strokeColor = ok ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.25f, 0.2f);
                        p.lineWidth = 2f;
                        Path(CellRect(c)); p.Stroke();
                    }
            }
        }
    }
}
