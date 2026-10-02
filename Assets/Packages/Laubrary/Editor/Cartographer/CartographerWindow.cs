using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.Cartographer.Editor
{
    /// CARTOGRAPHER — the tile-based level editor. THE LEVEL IS THE SUBJECT: the browser lists levels, the
    /// left boxes edit the open level's layers, palette, props and decals, and the right pane's LevelCanvas
    /// is THE ONE edit view (decree 2026-08-03: no workflow may require opening the same level in both this
    /// window and a scene — a LevelInstance in a scene is a live read-only mirror). (The old window's core
    /// defect was being a Prop editor that kept the level as a field inside a box — that window survives as
    /// the prop editor, opened from the Props box.)
    public partial class CartographerWindow : ZuiAssetWindow<LevelAsset>, ITilesetGridHost, ITilesetGridTileRole,
        ITilesetGridBrush
    {
        [MenuItem("Laubrary/Cartographer")]
        static void Open() => GetWindow<CartographerWindow>("Cartographer");

        /// Jump straight to one level — for gameplay-side tooling that wants "edit this level".
        public static void OpenFor(LevelAsset level)
        {
            var w = GetWindow<CartographerWindow>("Cartographer");
            if (level != null) w.SetAsset(level);
        }

        /// THE PROCGEN PREVIEW: a throwaway clone of the open level with the mutators already run on it.
        /// While it exists the whole window READS it — the canvas, the layers, the tag overlay — and every
        /// write path refuses (see <see cref="PreviewBlocks"/>), so a run can be looked at without the asset
        /// ever being touched. Pressing Preview again destroys the clone and the real level is back,
        /// untouched, with nothing to undo. Deliberately NOT "mutate then revert": a revert that goes wrong
        /// costs the author their room, and there is no version of "clone, mutate the clone, throw it away"
        /// that can.
        ///
        /// [NonSerialized] because a DontSave clone does not survive a domain reload — carrying the field
        /// across one would leave a destroyed reference standing in for the level.
        [System.NonSerialized] LevelAsset procgenPreview;

        LevelAsset level => procgenPreview != null ? procgenPreview : Current;

        /// True while what is on screen is the preview clone rather than the asset.
        internal bool Previewing => procgenPreview != null;

        protected override string TypeLabel => "Level";
        protected override string NewAssetName => "Level";
        protected override string DefaultFolder => "Assets/Cartographer/Levels";

        [SerializeField] int activeLayer;
        [SerializeField] LevelRecipe genRecipe;
        [SerializeField] int genSeed = 1234;

        /// What the scene-view tools paint with: a BRUSH of one or more tiles at relative offsets. One entry
        /// is plain painting; several are a temporary clump — a multi-selected palette pattern, a sampled
        /// region, or a real Clump from the tileset. Each entry carries a LAYER SHIFT: 0 paints the active
        /// layer, +1 the next layer in front — how a clump's overhang cells route themselves.
        readonly List<(Vector2Int off, LevelTile tile, int shift)> brush = new();

        /// ☠️ [NonSerialized] IS LOad-BEARING. `Clump` is [Serializable], and Unity's domain-reload
        /// serializer never writes null for a custom serializable class — it hands back a DEFAULT-CONSTRUCTED
        /// one. So after every script recompile this field came back as a phantom clump: displayName "Clump",
        /// zero cells, empty id, belonging to no tileset — and the reserved brush line dutifully announced
        /// "Brush: clump 'Clump' — 0 tiles, stamped as one object" when the author had no brush at all.
        /// Probed live 2026-08-03. Nothing wants this field to survive a reload, so the fix is to say so.
        [System.NonSerialized] Clump brushClump;
        internal IReadOnlyList<(Vector2Int off, LevelTile tile, int shift)> Brush => brush;
        internal LevelTile PaintTile => brush.Count > 0 ? brush[0].tile : null;
        internal Prop StampProp { get; private set; }

        /// The brush's DISTINCT tiles, flat — what the tileset grid marks by (see <see cref="ITilesetGridBrush"/>).
        /// Kept as a rebuilt list rather than derived on demand because the grid reads it on every repaint.
        readonly List<LevelTile> brushTiles = new();

        /// EVERY brush mutation ends here. The reserved brush LINE and the tileset grid's brush MARKS are two
        /// readings of one fact, and a path that refreshed only one of them is exactly how the grid came to
        /// show nothing while the author was painting with something. One door, so a new brush source cannot
        /// forget half of it.
        void AfterBrushChanged()
        {
            brushTiles.Clear();
            foreach (var (_, tile, _) in brush)
                if (tile != null && !brushTiles.Contains(tile)) brushTiles.Add(tile);
            EnsureToolCanPaintTiles();
            RefreshBrushLine();
            // Targeted, never a rebuild: an eyedropper click must not cost the author the grid's scroll
            // position, its cell selection or a pinned actions card.
            tilesetView?.RefreshBrushMarks();
        }

        /// PICKING A TILE MUST NOT RETARGET THE TOOL. Every brush-setting path used to end in
        /// `SetTool(CanvasTool.Paint)`, so choosing a different tile while working with Rect, Line or Fill
        /// snapped the author back to Paint and visibly re-lit a different button on the tool card (user
        /// report, 2026-08-03: "when i choose another tile to paint with, dont reset the settings of the
        /// level editor context card" — the TOOL was the whole of what reset; everything else on the card is
        /// a field that survives the rebuild). The rule now: change the tool ONLY when the current one cannot
        /// use a tile brush at all. Stamp lays PROPS and Decal lays DECALS — neither can put a tile down, so
        /// leaving them armed would make the pick do nothing. Paint, Erase, Line, Rect, Fill and Pick all
        /// work with a tile brush and were chosen deliberately, so they stand.
        ///
        /// Deliberately NOT paired with any "restore the old tool afterwards" cleverness: the fix is to stop
        /// changing it, not to change it and change it back.
        void EnsureToolCanPaintTiles()
        {
            if (brush.Count == 0) return;
            if (tool == CanvasTool.Stamp || tool == CanvasTool.Decal) SetTool(CanvasTool.Paint);
        }

        /// One tile, offset zero — the eyedropper's single pick and the plain tileset click.
        internal void SetBrushSingle(LevelTile tile)
        {
            brush.Clear();
            brushClump = null;
            // notify:false — the grid's own selection-changed hook rebuilds the brush FROM the selection,
            // so a notifying clear here would immediately wipe the brush this method exists to set.
            tilesetView?.ClearSelection(notify: false);
            if (tile != null) brush.Add((Vector2Int.zero, tile, 0));
            StampProp = null;
            AfterBrushChanged();
            RebuildProps();
        }

        /// A sampled arrangement from the level — offsets exactly as they sit in the room.
        internal void SetBrushSample(List<(Vector2Int off, LevelTile tile)> cells)
        {
            brush.Clear();
            brushClump = null;
            tilesetView?.ClearSelection(notify: false);
            foreach (var (off, tile) in cells) brush.Add((off, tile, 0));
            StampProp = null;
            AfterBrushChanged();
            RebuildProps();
        }

        /// A CLUMP is the brush: the whole locked arrangement paints as one gesture, overhang cells
        /// routing themselves one layer further front via their stored shift.
        internal void SetBrushClump(Clump clump)
        {
            brush.Clear();
            brushClump = clump;
            StampProp = null;
            if (clump?.cells != null)
            {
                var b = clump.Bounds;
                foreach (var pc in clump.cells)
                    if (pc.tile != null)
                        // Tileset rows go down the screen, world rows go up — same flip as a pattern.
                        brush.Add((new Vector2Int(pc.offset.x - b.xMin, -(pc.offset.y - b.yMin)), pc.tile, pc.layerShift));
            }
            // No SetTool here: AfterBrushChanged only leaves Stamp/Decal, which cannot lay a clump. Rect,
            // Line and Fill can, and the author picked them.
            AfterBrushChanged();
            RebuildProps();
        }

        /// THE SECOND MEANING OF A SELECTION. The shared grid owns selecting; here that selection also
        /// arms the paint brush — cells selected together form a PATTERN, their positions in the FIXED
        /// tileset grid becoming the brush's offsets (tileset rows go down the screen, world rows go up,
        /// hence the flip). A single selected cell falls out of the same maths as a one-tile brush.
        void RebuildBrushFromSelection()
        {
            brush.Clear();
            var set = ActiveLayer?.tileset;
            var picked = tilesetView?.SelectedIndices;
            if (set == null || picked == null || picked.Count == 0) return;
            int colsP = Mathf.Max(1, set.paletteColumns);

            int minC = int.MaxValue, minR = int.MaxValue;
            foreach (var idx in picked)
            {
                minC = Mathf.Min(minC, idx % colsP);
                minR = Mathf.Min(minR, idx / colsP);
            }
            foreach (var idx in picked)   // already ascending — row-major, the order a pattern reads in
            {
                if (idx < 0 || idx >= set.tiles.Count || set.tiles[idx] == null) continue;
                brush.Add((new Vector2Int(idx % colsP - minC, -(idx / colsP - minR)), set.tiles[idx], 0));
            }
        }

        internal LevelLayer ActiveLayer =>
            level != null && level.layers != null && activeLayer >= 0 && activeLayer < level.layers.Count
                ? level.layers[activeLayer] : null;

        readonly Dictionary<Object, Texture2D> _fieldThumbs = new();
        readonly Dictionary<Object, Texture2D> _gridThumbs = new();

        VisualElement levelBox, layersList, tilesetBody, propsGrid, decalsList, tagsList;

        /// The SHARED tileset editor (the Tileset Builder's grid, extracted 2026-08-03) and the dials it
        /// renders by. This window is a full host: the same view, the same edit suite, the same tabs — the
        /// difference is only what a selection MEANS here, which is the paint brush.
        TilesetGridView tilesetView;
        ZuiBox tilesetBox;
        Label brushLine;
        [SerializeField] TilesetGridOptions tilesetOptions = new();
        float savedTilesetScrollX;

        /// Which layers the author has OPEN, by layer name. Pure VIEW state — not asset data, so no Undo.
        ///
        /// Expansion is deliberately INDEPENDENT of which layer is active: you must be able to open a layer's
        /// settings to read or edit them without first making it the paint target, and several may be open at
        /// once. THE CARET IS THE ONLY THING THAT MAY CHANGE THIS SET — clicking Active picks the paint
        /// target and touches nothing here, neither expanding the layer it activates nor collapsing the ones
        /// it does not. (An intermediate version DID auto-expand on activate; removed 2026-08-03 by user
        /// decree. Do not reintroduce it, and do not compensate for it either — no auto-expanding the first
        /// layer, none when the set happens to be empty. The author's fold state is the author's.) The one
        /// other write is the RENAME carry-over below, which preserves a state rather than deciding one.
        /// Stored as EXPANDED rather than COLLAPSED so a brand-new layer starts closed rather than every
        /// layer in a big stack starting open.
        readonly HashSet<string> expandedLayers = new();

        // ── section toggle bar (T-0084) ─────────────────────────────────────────────────────
        // The roster the shared ZuiSectionToggleBar addresses, rebuilt from scratch on every BuildAsset
        // (same convention as ChunkWindow's _barUnits — see its comment for why: BuildUI/Rebuild() clears
        // and reconstructs the whole tree, so nothing here needs to survive a rebuild except the height
        // reservation below).
        readonly List<(string label, ZuiSection section)> _barUnits = new List<(string label, ZuiSection section)>();

        // Tallest layout the bar has taken at a given width, remembered for the window's lifetime so the
        // bar's own Sections↔Toggle Bar mode switch can never shrink the chrome above the workspace.
        float _barReservedW, _barReservedH;

        protected override void OnEnable()
        {
            RestoreWindowState();   // FIRST — the base build reads these fields (tool box, canvas zoom)
            base.OnEnable();
            Undo.undoRedoPerformed += RefreshInstance;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        protected override void OnDisable()
        {
            EndProcgenPreview();   // the clone is window-lifetime scratch; nothing outlives the window
            SaveWindowState();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Undo.undoRedoPerformed -= RefreshInstance;
            base.OnDisable();
            LauAssetGridGUI.ClearCache(_fieldThumbs);
            LauAssetGridGUI.ClearCache(_gridThumbs);
            canvas?.Dispose();
            canvas = null;
        }

        protected override Texture2D RenderThumbnail(LevelAsset item) => item != null ? item.RenderPreviewTexture() : null;

        protected override void InitializeNewAsset(LevelAsset item)
        {
            item.displayName = item.name;
            // A level with no layers is a wall the user hits immediately; one conventional layer is a start.
            item.layers.Add(new LevelLayer { name = "Terrain" });
        }

        protected override void OnAssetChanged()
        {
            EndProcgenPreview();   // a preview belongs to the level it was cloned from, and that just changed
            activeLayer = 0;
            brush.Clear();
            brushClump = null;
            StampProp = null;
            AfterBrushChanged();   // same door as every other brush change — the marks must empty too
            LoadGuidesForLevel();  // guides are per level: a different level shows its own, or none
            savedTilesetScrollX = 0f;   // a different level is a new context — start the grid at its left
            LauAssetGridGUI.ClearCache(_fieldThumbs);
            LauAssetGridGUI.ClearCache(_gridThumbs);
        }

        /// Workspace stability: a full rebuild recreates the tileset view, so hand its horizontal place to
        /// the successor rather than dumping the user back at column zero.
        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();
            if (tilesetView != null) savedTilesetScrollX = tilesetView.ScrollX;
        }

        /// ☠️ Mid-edit, a project change must NOT rebuild this window — the same lesson the Tileset
        /// Builder paid for, and it applies here the moment the shared grid starts minting tile assets
        /// (every fork-on-copy, paste and Ctrl-drag duplicate writes to disk). The base's full rebuild
        /// would throw away the canvas content, the brush, the marquee cell selection, the canvas
        /// zoom/pan and any pinned card — and it runs in the very tick the import batch lands, where a
        /// just-forked tile still reads as NULL. Instead: one coalesced, deferred, TARGETED refresh a
        /// tick later, when references resolve.
        protected override void OnProjectChanged()
        {
            if (asset == null || IsBrowsing) { base.OnProjectChanged(); return; }
            if (projectRefreshQueued) return;
            projectRefreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                projectRefreshQueued = false;
                if (this == null || asset == null) return;
                tilesetView?.RefreshCells();
                canvas?.RebuildContent();   // targeted: keeps the view transform, the selection and the brush
                RefreshTagOverlay();
                // Deliberately NOT UpdateCanvasStatus(): it would overwrite the feedback an operation
                // just wrote ("Pasted 3 tile(s)…") one tick after every gesture.
            };
        }

        bool projectRefreshQueued;

        // ── the procgen preview ────────────────────────────────────────────────
        /// Show the procgen run, or come back from it. Showing it CLONES the level, runs the mutators on the
        /// clone and renders that; coming back destroys the clone. The asset is never written either way, so
        /// there is no undo step to get right and no half-mutated state to be caught in.
        void ToggleProcgenPreview()
        {
            if (procgenPreview != null) { EndProcgenPreview(); return; }
            if (Current == null) return;

            var clone = LevelProcgen.CloneForRun(Current);
            if (clone == null) return;
            int n = LevelProcgen.Run(clone, genSeed);
            procgenPreview = clone;

            canvas?.RebuildContent();
            RebuildLevelBox();
            RefreshTagOverlay();
            FlashCanvasStatus(n == 0
                ? "Procgen preview: nothing ran — no prop in this level declares a mutator."
                : $"Procgen preview, seed {genSeed}: {n} mutator prop(s) ran. Press Preview again to come back.");
        }

        /// Re-run the preview in place when the seed changes, so the pane always shows the seed the field
        /// says. Silent when nothing is being previewed — changing the seed must not START a preview, or
        /// typing a digit would yank the level out from under an edit.
        void RefreshProcgenPreview()
        {
            if (procgenPreview == null) return;
            EndProcgenPreview();
            ToggleProcgenPreview();
        }

        /// Roll SHOWS the roll. A new seed whose result you cannot see is just a number changing, and the
        /// whole reason to roll is to look at the room it makes — so this starts the preview if one is not
        /// already up rather than only refreshing an existing one.
        void RollAndPreview()
        {
            if (procgenPreview != null) EndProcgenPreview();
            ToggleProcgenPreview();
            RebuildLevelBox();
        }
        void EndProcgenPreview()
        {
            if (procgenPreview == null) return;
            Object.DestroyImmediate(procgenPreview);
            procgenPreview = null;
            canvas?.RebuildContent();
            RebuildLevelBox();
            RefreshTagOverlay();
        }

        /// EVERY write path asks this first. A preview clone is thrown away on the next press, so an edit
        /// made against it would vanish without a trace — refusing and saying why is the only honest answer.
        bool PreviewBlocks()
        {
            if (procgenPreview == null) return false;
            FlashCanvasStatus("A procgen PREVIEW is showing — press Preview again to come back to the level before editing.");
            return true;
        }

        /// The mutator summary as a PERMANENTLY RESERVED single line — fixed height, never wrapping, only
        /// its text changes — so a level that gains a mutator cannot shove the controls above it around.
        VisualElement MutatorLine()
        {
            var line = Z.Text(MutatorSummary(out string tip), ZuiText.Subtle, tip);
            line.style.height = 16f;
            line.style.whiteSpace = WhiteSpace.NoWrap;
            line.style.overflow = Overflow.Hidden;
            return line;
        }

        /// The level's mutators as one reserved line: what a procgen run will actually do, named, before
        /// anyone presses anything. `tooltip` carries each mutator's own sentence.
        string MutatorSummary(out string tooltip)
        {
            var mutators = LevelProcgen.Mutators(level);
            var silent = LevelProcgen.SilentProcgenProps(level);

            var names = new List<string>();
            var lines = new List<string>();
            foreach (var m in mutators)
            {
                if (!names.Contains(m.Name)) names.Add(m.Name);
                string line = m.Name + " — " + m.Description;
                if (!lines.Contains(line)) lines.Add(line);
            }
            foreach (var p in silent)
            {
                string n = string.IsNullOrEmpty(p.displayName) ? p.name : p.displayName;
                names.Add("⚠ " + n);
                lines.Add($"⚠ {n} is marked Procgen but declares no mutator — running the pass will change " +
                          "nothing. A component on its prefab has to implement ILevelMutator.");
            }

            tooltip = lines.Count > 0
                ? string.Join("\n", lines)
                : "Nothing in this level rewrites itself when procgen runs. A prop marked Procgen whose " +
                  "prefab declares a mutator would be listed here.";
            return names.Count == 0 ? "No mutators" : "Mutators: " + string.Join(" · ", names);
        }

        // ── mutation helper — every data edit goes through here ───────────────
        /// Undoable, dirties the asset, and pushes the edit into whatever scene instance previews this level.
        void Dial(string undoLabel, System.Action apply)
        {
            if (level == null || PreviewBlocks()) return;
            Undo.RecordObject(level, undoLabel);
            apply();
            EditorUtility.SetDirty(level);
            RefreshInstance();
            RefreshTagOverlay();
        }

        /// A LOOK-ONLY edit: a layer's visibility or opacity, editing or runtime. Undoable and dirtying like
        /// any Dial, but it re-TINTS instead of re-rendering — these values are dragged, and routing an
        /// opacity slider through Dial re-blits every tile of every layer on every pointer move (and rebuilds
        /// the whole scene mirror with it) for the sake of one colour.
        ///
        /// `mirror: false` for the EDITING-only pair: the scene instance is the mirror of what the game will
        /// show, so it must not learn about editorHidden/editorOpacity at all — that difference is the whole
        /// reason those fields exist.
        /// `rebuildCanvas` for the two VISIBILITY toggles rather than the two alphas: a layer that was not
        /// drawn has no texture in the canvas at all, so re-tinting it would tint nothing.
        void DialLook(string undoLabel, System.Action apply, bool mirror = true, bool rebuildCanvas = false)
        {
            if (level == null || PreviewBlocks()) return;
            Undo.RecordObject(level, undoLabel);
            apply();
            EditorUtility.SetDirty(level);
            if (rebuildCanvas) canvas?.RebuildContent();
            else canvas?.RefreshLayerTints();
            if (mirror) SceneInstance()?.ApplyLayerLook();
            UpdateCanvasStatus();   // the status line names the active layer and says when it is hidden
        }

        /// The open scene's instance of THIS level, if any.
        LevelInstance SceneInstance()
        {
            var all = Object.FindObjectsByType<LevelInstance>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var i in all) if (i != null && i.level == level) return i;
            return null;
        }

        void RefreshInstance()
        {
            if (level == null) return;
            SceneInstance()?.Rebuild();   // the read-only scene mirror, when one exists
            canvas?.RebuildContent();
        }

        // ── the window body ────────────────────────────────────────────────────
        protected override void BuildAsset(VisualElement root, LevelAsset asset)
        {
            root.AddToClassList("lau-tool-shell");

            // T-0084 — the section toggle bar rides at the very top of the per-asset UI, spanning the full
            // window width (same placement as Pyre/Chunks). Its host is added FIRST (empty) and filled LAST,
            // once every section below exists for it to address.
            var barHost = new VisualElement();
            barHost.AddToClassList("lau-tool-shell__chrome");
            if (_barReservedH > 0f) barHost.style.minHeight = _barReservedH;   // space reserved before anything paints
            root.Add(barHost);

            // Point the ruler guides at THIS level's set. Here rather than only in OnAssetChanged because
            // restoring the window's last level fills the `asset` field directly and raises no change event —
            // and BuildAsset is the one method that runs for every level this window ever shows.
            LoadGuidesForLevel();

            var left = new VisualElement();
            left.AddToClassList("lau-tool-shell__side--compact");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("lau-tool-shell__scroll");
            left.Add(scroll);
            var body = scroll.contentContainer;

            // Right pane: THE edit view — the interactive LevelCanvas plus its permanently reserved
            // status line (stable-layout rule: appearing text must never move the canvas).
            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.minWidth = 120f;
            right.style.minHeight = 0f;
            canvas?.Dispose();
            canvas = new LevelCanvas(this);
            right.Add(canvas);
            canvasStatus = (Label)Z.Text("", ZuiText.Subtle,
                "Hovered cell, tool, active layer and zoom — and, in red, why a blocked edit did nothing.");
            canvasFlashSeq = 0;   // fresh label — recapture its theme colour on the next flash
            canvasStatus.style.height = 18f;
            canvasStatus.style.whiteSpace = WhiteSpace.NoWrap;
            canvasStatus.style.overflow = Overflow.Hidden;
            right.Add(canvasStatus);

            root.Add(Z.Split("cartographer", 380f, left, right));
            canvas.RebuildContent();
            RefreshTagOverlay();
            UpdateCanvasStatus();

            _barUnits.Clear();

            // T-0084 — these seven top-level panels were framed `Z.BoxKeyed`s; the shared ZuiSectionToggleBar
            // only drives `ZuiSection`s, so they're converted to `Z.Section` here. Same call Pyre made for its
            // own Layers box when it joined its toggle bar (PyreWindow.cs, BuildLayerList): a green-header
            // Section reads consistently among top-level sections, and the stable state key below keeps each
            // panel's fold state from orphaning on this conversion. `Z.Section` has no trailing-children
            // params the way `Z.BoxKeyed` did, so each panel's content host (and, for Layers, its "+ Add
            // layer" button) is `.Add()`ed onto the section after construction instead — same children, same
            // order, same visible result.
            levelBox = new VisualElement();
            var levelSection = Z.Section("Level", "The open level: its name, biome, extent and scene preview.",
                "cartographer.level");
            levelSection.Add(levelBox);
            body.Add(levelSection);
            _barUnits.Add(("Level", levelSection));
            RebuildLevelBox();

            layersList = new VisualElement();
            var layersSection = Z.Section("Layers", "The level's drawing layers, back to front. The active layer is what painting affects.",
                "cartographer.layers");
            layersSection.Add(layersList);
            layersSection.Add(Z.Button("+ Add layer", "Add a drawing layer in front of the others.",
                () => { Dial("Add layer", () => level.layers.Add(new LevelLayer { name = "Layer " + level.layers.Count })); RebuildLayers(); }));
            body.Add(layersSection);
            _barUnits.Add(("Layers", layersSection));
            RebuildLayers();

            // "Tileset", not "Palette" (renamed 2026-08-03) — but the saved-view KEY stays
            // `cartographer.palette` on purpose: re-keying it would orphan every view the user has stored.
            tilesetBody = new VisualElement();
            tilesetBox = Z.BoxKeyed("Tileset", "The active layer's tileset, as the full editor: click a tile to paint with it, " +
                "marquee or Ctrl-click several to paint the whole pattern, and edit the tileset in place — delete, move, " +
                "copy (which FORKS into independent tiles), flip, tag. The Clumps tab paints locked arrangements as one object.",
                "cartographer.palette", tilesetBody);
            body.Add(tilesetBox);
            RebuildTilesetBox();

            propsGrid = new VisualElement();
            var propsSection = Z.Section("Props", "The biome's reusable structures. Click one to make it the stamp; Edit opens it in the prop editor.",
                "cartographer.props");
            propsSection.Add(propsGrid);
            body.Add(propsSection);
            _barUnits.Add(("Props", propsSection));
            RebuildProps();

            decalsList = new VisualElement();
            var decalsSection = Z.Section("Decals", "Free sprites placed in this level, not bound to the grid.",
                "cartographer.decals");
            decalsSection.Add(decalsList);
            body.Add(decalsSection);
            _barUnits.Add(("Decals", decalsSection));
            RebuildDecals();

            // No Tool box: the tool settings live in a card the canvas raises on right-click, beside the
            // work, instead of in a box the user has to travel to and back from.

            tagsList = new VisualElement();
            var tagsSection = Z.Section("Tags", "Gameplay labels on the ACTIVE layer, and the canvas overlay that makes tagged cells visible — invisible metadata is unverifiable metadata.",
                "cartographer.tags");
            tagsSection.Add(tagsList);
            body.Add(tagsSection);
            _barUnits.Add(("Tags", tagsSection));
            RebuildTagsBox();

            var bar = new ZuiSectionToggleBar("Cartographer", _barUnits.ToArray());
            barHost.Add(bar);
            ReserveBarHeight(barHost, bar);
        }

        /// Stable-workspace rule: chrome ABOVE the workspace must never change the geometry of what is below
        /// it. The bar hides its button strip with `display` when it is in Sections mode, which would collapse
        /// its height and jump the whole window up the moment the user switched modes. So reserve the space:
        /// remember the TALLEST height the bar has laid out at the current width and pin it as the host's
        /// minHeight, so a mode switch (or a solo) can only ever change what is IN the bar, never its size.
        /// A width change resets the reservation — that is the user resizing their own window, not contextual
        /// UI moving under their cursor. Growing only, and measured on the BAR rather than the host, so
        /// writing the host's minHeight cannot feed back into its own measurement. (Verbatim copy of
        /// ChunkWindow.ReserveBarHeight.)
        void ReserveBarHeight(VisualElement barHost, VisualElement bar)
        {
            bar.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float w = bar.resolvedStyle.width, h = bar.resolvedStyle.height;
                if (float.IsNaN(w) || float.IsNaN(h) || h <= 0f) return;
                if (Mathf.Abs(w - _barReservedW) > 0.5f) { _barReservedW = w; _barReservedH = 0f; }
                if (h <= _barReservedH + 0.5f) return;
                _barReservedH = h;
                barHost.style.minHeight = h;
            });
        }

        void RebuildTagsBox()
        {
            if (tagsList == null) return;
            tagsList.Clear();

            var layer = ActiveLayer;
            if (layer == null)
            {
                tagsList.Add(Z.Text("No active layer.", ZuiText.Subtle, "Layer tags attach to the active layer."));
                return;
            }

            for (int i = 0; i < layer.tags.Count; i++)
            {
                int idx = i;
                tagsList.Add(Z.Row(
                    LauAssetElement.Build(layer.tags[idx],
                        picked => { Dial("Set layer tag", () => layer.tags[idx] = picked as TileTag); RebuildTagsBox(); RefreshTagOverlay(); },
                        typeof(TileTag), _fieldThumbs, "Tag", "Assets/Cartographer/Tags",
                        "Gameplay label carried by every cell of this layer."),
                    Z.Button("×", "Remove this tag from the layer.",
                        () => { Dial("Remove layer tag", () => layer.tags.RemoveAt(idx)); RebuildTagsBox(); RefreshTagOverlay(); }).W(24f)));
            }

            tagsList.Add(Z.Row(
                Z.Button("+ Add tag", "Attach a gameplay label to the active layer.",
                    () => { Dial("Add layer tag", () => layer.tags.Add(null)); RebuildTagsBox(); }),
                Z.Flexible(),
                Z.ToggleButton("Show tags", "Tint every tagged cell in the level canvas with its tag's colour, " +
                    "and list the hovered cell's tags in the status line under the canvas.",
                    showTagOverlay, v => { showTagOverlay = v; RefreshTagOverlay(); })));
        }

        // ── Level box ──────────────────────────────────────────────────────────
        void RebuildLevelBox()
        {
            if (levelBox == null) return;
            levelBox.Clear();

            levelBox.Add(Z.Field("Name", "Name shown in browsers and pickers. Independent of the asset's file name.",
                Z.TextInput(level.displayName, "Name shown in browsers and pickers.",
                    v => Dial("Rename level", () => level.displayName = v), 190f)));

            levelBox.Add(Z.Field("Biome", "What this level may be built from: its tilesets feed the palette, its prop list gates what may be placed.",
                LauAssetElement.Build(level.biome,
                    picked => { Dial("Set level biome", () => level.biome = picked as CartographerBiome); Rebuild(); },
                    typeof(CartographerBiome), _fieldThumbs, "Biome", "Assets/Cartographer/Biomes",
                    "Biome this level draws from.")));

            var b = level.bounds;
            levelBox.Add(Z.Row(
                Z.Field("Origin", "Cell the level's bounds start at.",
                    Z.Row(
                        Z.Int(b.x, "Bounds origin X, in cells.", v => Dial("Set bounds", () => { var r = level.bounds; r.x = v; level.bounds = r; }), 46f),
                        Z.Int(b.y, "Bounds origin Y, in cells.", v => Dial("Set bounds", () => { var r = level.bounds; r.y = v; level.bounds = r; }), 46f))),
                Z.HSpace(),
                Z.Field("Size", "The level's extent in cells — where each layer's background fill stops and what the thumbnail frames.",
                    Z.Row(
                        Z.Int(b.width, "Bounds width in cells.", v => Dial("Set bounds", () => { var r = level.bounds; r.width = Mathf.Max(1, v); level.bounds = r; }), 46f),
                        Z.Int(b.height, "Bounds height in cells.", v => Dial("Set bounds", () => { var r = level.bounds; r.height = Mathf.Max(1, v); level.bounds = r; }), 46f)))));

            levelBox.Add(Z.Field("Collision", "How solid layers collide: top-down walls, or side-scroll with one-way platforms.",
                Z.Segmented((int)level.collision, new[] { "Top-down", "Side-scroll" },
                    "Collider setup for this level's solid layers.",
                    i => Dial("Set collision mode", () => level.collision = (LevelCollision)i))));

            var inst = SceneInstance();
            levelBox.Add(Z.Row(
                Z.Text(inst != null ? "Mirrored in scene: " + inst.gameObject.name : "Not in the open scene.",
                    ZuiText.Subtle,
                    "The scene object mirroring this level — READ-ONLY: it rebuilds after every edit here, for gameplay context. All editing happens in this window's canvas."),
                Z.Flexible(),
                Z.Button(inst != null ? "Rebuild mirror" : "Mirror in scene",
                    "Builds (or rebuilds) a read-only scene instance of this level, so the open scene can show it in gameplay context. Editing stays in this window.",
                    () =>
                    {
                        // A mirror of a preview clone would be a scene object pointing at an asset that is
                        // destroyed on the next press — refuse rather than build one.
                        if (PreviewBlocks()) return;
                        var target = SceneInstance();
                        if (target == null)
                        {
                            var go = new GameObject(level.name);
                            Undo.RegisterCreatedObjectUndo(go, "Create level instance");
                            go.AddComponent<Grid>();
                            target = go.AddComponent<LevelInstance>();
                            target.level = level;
                        }
                        target.Rebuild();
                        RebuildLevelBox();
                    })));

            // TWO different jobs, in two boxes, because they were confusing each other. Rolling a room is
            // something you do constantly; generating a whole level from a recipe is a separate feature that
            // this project has never used (zero LevelRecipe assets exist).
            levelBox.Add(Z.Box("Procgen",
                "Roll this room's mutators — aisles get plugged, exits get picked. Always on a THROWAWAY " +
                "copy: the level asset is never written in edit mode.",
                Z.Row(
                    Z.Field("Seed", "Same seed always produces the same room.",
                        Z.Int(genSeed, "Generation seed.", v => { genSeed = v; RefreshProcgenPreview(); }, 80f)),
                    Z.Button("Roll", "Pick a new seed and show it immediately.",
                        () => { genSeed = Random.Range(0, 1_000_000); RollAndPreview(); }),
                    Z.Flexible(),
                    Z.ToggleButton("Preview",
                        "Show what this seed's procgen run looks like, on a THROWAWAY COPY of the level — the " +
                        "asset is not touched and nothing is written. Press again to come back to the real " +
                        "level. Editing is refused while a preview is showing.",
                        procgenPreview != null, _ => ToggleProcgenPreview()).W(72f)),
                MutatorLine()));

            levelBox.Add(Z.Box("From recipe",
                "Build this level's GENERATED content from a recipe — a seed-and-dials description of a KIND " +
                "of level, so a world map can hand out seeds and get reproducible places. Authored paints, " +
                "stamps and decals survive, which is what makes hybrid levels possible.",
                Z.Field("Recipe", "Seed-and-dials description of a KIND of level.",
                    LauAssetElement.Build(genRecipe,
                        picked => { genRecipe = picked as LevelRecipe; RebuildLevelBox(); },
                        typeof(LevelRecipe), _fieldThumbs, "Recipe", "Assets/Cartographer/Recipes",
                        "Recipe to generate from.")),
                Z.Row(
                    Z.Flexible(),
                    Z.Button("Generate", "Clear this level's generated content and rebuild it from the recipe " +
                        "and the Procgen seed above. Writes to the level.",
                        () =>
                        {
                            // Refused while previewing: `level` resolves to the throwaway clone, so this
                            // would generate into something about to be destroyed and look like a no-op.
                            if (genRecipe == null || level == null || PreviewBlocks()) return;
                            int n = 0;
                            Dial("Generate level", () => n = LevelGenerator.Generate(genRecipe, genSeed, level));
                            Rebuild();
                            Debug.Log($"[Cartographer] Generated '{level.name}' from '{genRecipe.name}' seed {genSeed}: {n} props placed.", level);
                        }))));
        }

        // ── Layers box ─────────────────────────────────────────────────────────
        void RebuildLayers()
        {
            if (layersList == null) return;
            layersList.Clear();

            if (level.layers == null || level.layers.Count == 0)
            {
                layersList.Add(Z.Text("No layers yet — add one to start painting.", ZuiText.Subtle,
                    "A level draws nothing until it has at least one layer."));
                return;
            }

            activeLayer = Mathf.Clamp(activeLayer, 0, level.layers.Count - 1);

            for (int i = 0; i < level.layers.Count; i++)
            {
                int idx = i;
                var layer = level.layers[idx];
                bool isActive = idx == activeLayer;

                var card = new VisualElement();
                card.AddToClassList("lau-map-layer__card");

                var grip = Z.Text("≡", ZuiText.Subtle, "Drag to reorder this layer.");
                grip.AddToClassList("lau-map-layer__grip");

                // ANY layer expands, active or not — a stack of six layers is a wall of controls otherwise,
                // and inspecting one must not require re-targeting the brush at it.
                bool expanded = expandedLayers.Contains(layer.name);
                var caret = Z.Button(expanded ? "▾" : "▸",
                    "Show or hide this layer's settings. Independent of which layer is ACTIVE — any layer " +
                    "can be opened, and several at once.",
                    () =>
                    {
                        if (!expandedLayers.Remove(layer.name)) expandedLayers.Add(layer.name);
                        RebuildLayers();
                    }).W(16f);

                var header = Z.Row(
                    caret,
                    grip,
                    Z.ToggleButton("Active", "The active layer is what painting affects and what the Tileset box shows. " +
                        "Separate from expansion: this picks the paint target, the caret shows the settings.",
                        isActive, _ =>
                        {
                            // ACTIVE CHANGES THE PAINT TARGET AND NOTHING ELSE. It used to also expand this
                            // layer's card; removed 2026-08-03 by user decree ("Dont expand layer when layer
                            // is selected. Dont contract layer if other layer is selected."). Fold state is
                            // the author's — the caret is the only thing that may change it.
                            activeLayer = idx;
                            RebuildLayers();
                            RebuildTilesetBox();
                        }).W(54f),
                    Z.TextInput(layer.name, "Name paints and prop cells address this layer by.",
                        v =>
                        {
                            // Expansion is keyed by NAME, so carry the flag across a rename or the card
                            // silently folds itself the moment the user types in it.
                            bool wasOpen = expandedLayers.Remove(layer.name);
                            Dial("Rename layer", () => layer.name = v);
                            if (wasOpen) expandedLayers.Add(layer.name);
                        }, 74f),
                    // THE TWO EDITING-ONLY DIALS, on the header so they are reachable without unfolding
                    // anything — they are what you touch constantly while working (hide a layer to see under
                    // it, dim one to trace over it), unlike the settings inside the card. Icon-only + a short
                    // slider because the header must never wrap; the runtime Visible toggle moved INTO the
                    // card to pay for the width, which is also where it belongs: it ships, these do not.
                    Z.ToggleButton("", "Hide while editing — EDITING ONLY, this never reaches the game. Hides " +
                        "the layer in the canvas so you can see what is underneath. The scene mirror, the " +
                        "level thumbnail and the build all keep drawing it; for 'hidden in the game too', " +
                        "open the card and use \"Visible in game\".",
                        layer.editorHidden,
                        v => DialLook("Hide layer while editing", () => layer.editorHidden = v,
                            mirror: false, rebuildCanvas: true), "eye-slash"),
                    Z.MicroSlider("Alpha", layer.editorOpacity, 0f, 1f,
                        "Editing alpha — EDITING ONLY, this never reaches the game. Dims the layer in the " +
                        "canvas while you work on another one. MULTIPLIED with the layer's game opacity, so " +
                        "a layer that ships at 0.5 dimmed to 0.5 here draws at 0.25 while editing and still " +
                        "ships at 0.5. The scene mirror ignores it on purpose: it shows what the game shows.",
                        v => DialLook("Set editing alpha", () => layer.editorOpacity = v, mirror: false), 58f,
                        showValue: false, defaultValue: 1f),
                    Z.ToggleButton("", "Locked: this layer cannot be painted on. Protects a finished layer " +
                        "from stray clicks.",
                        layer.locked, v => Dial("Toggle layer lock", () => layer.locked = v), "lock-simple"),
                    // THE DESTRUCTIVE ×. It removes the WHOLE LAYER, and it sits one line above a × that
                    // merely clears one field — the user asked outright "what is the difference between
                    // clear and delete?", which is the question two identical glyphs guarantee. This one
                    // keeps the × (it is the delete), is drawn in the warning red every other destructive
                    // affordance here uses, and its tooltip names its SCOPE in the first two words. Its
                    // twin on the Background row is now an eraser glyph and is not tinted. See
                    // BackgroundTileReadout.
                    TilesetGridView.Destructive(Z.Button("×", "Remove this LAYER — the whole layer and its settings go. Its " +
                        "paints stay in the asset until painted over. Not the same as the eraser on the " +
                        "Background row below, which only clears one field.",
                        () => { Dial("Remove layer", () => level.layers.RemoveAt(idx)); RebuildLayers(); RebuildTilesetBox(); })).W(20f));
                // A darker header band is what makes one layer's block read as separate from the next when
                // several are expanded — without it the settings of two layers run together.
                header.style.backgroundColor = new Color(0f, 0f, 0f, isActive ? 0.28f : 0.18f);
                header.style.paddingLeft = header.style.paddingRight = 2f;
                header.style.paddingTop = header.style.paddingBottom = 1f;
                // A DENSE CHROME STRIP, not a form row. Nine controls have to sit on one line that may never
                // wrap, in a pane whose default is 380px — the standard 6px inter-control gutter alone costs
                // more than the editor-alpha slider does. Tightened to 3px here and nowhere else; the
                // controls are distinct shapes (caret, grip, latch, field, two glyphs, ×) so the boundaries
                // still read without the whitespace doing the work.
                for (int h = 0; h < header.childCount; h++)
                {
                    header[h].style.marginLeft = 0f;
                    header[h].style.marginRight = 3f;
                }
                card.Add(header);

                if (expanded)
                {
                    card.Add(Z.Field("Tileset", "The palette this layer paints from.",
                        LauAssetElement.Build(layer.tileset,
                            picked => { Dial("Set layer tileset", () => layer.tileset = picked as Tileset); RebuildTilesetBox(); },
                            typeof(Tileset), _fieldThumbs, "Tileset", "Assets/Cartographer/Tilesets",
                            "Tileset the palette shows while this layer is active.")));

                    // The background readout owns its row: the ORPHAN message ("… — not in tileset") is the
                    // longest thing this card ever says, and it is the one message that must not be clipped.
                    card.Add(BackgroundTileReadout(layer));

                    // THE SHIPPED PAIR, named for the game so it cannot be confused with the editing pair on
                    // the header. Two alphas on one layer is exactly the thing that gets mixed up, so the
                    // LABELS — not just the tooltips — carry the distinction.
                    //
                    // Sort rides along on the end: a short field beside an existing row is free vertical
                    // space, and it deliberately uses a plain Z.Text label rather than a Z.Field one so it
                    // stays OUT of the card's aligned label column (which is sized by "Background" and would
                    // cost this 3-character label 70px of it). The label still scrubs — that is what
                    // ZuiScrub.AttachToLabel gives a hand-built pair.
                    const string sortTip = "Draw order among the level's layers. Higher draws in front.";
                    var sortInt = Z.Int(layer.sortingOrder, sortTip,
                        v => Dial("Set layer sorting", () => layer.sortingOrder = v), 44f);
                    var sortLabel = Z.Text("Sort", ZuiText.Small, sortTip);
                    ZuiScrub.AttachToLabel(sortLabel, sortInt);

                    card.Add(Z.Row(
                        Z.ToggleButton("Visible in game", "Whether the layer draws IN THE GAME. This ships: " +
                            "the built Tilemap's renderer is switched off for a hidden layer. To hide a layer " +
                            "only while authoring, use the eye button on the header row.  ⚠️ It hides, it does " +
                            "not disable: colliders come from 'Solid' and never consult this, so hiding a " +
                            "SOLID layer leaves walls that still block.",
                            layer.visible, v => DialLook("Toggle layer visibility", () => layer.visible = v,
                                rebuildCanvas: true)),
                        Z.MicroSlider("Game opacity", layer.opacity, 0f, 1f,
                            "Layer opacity IN THE GAME, multiplied into every tile — this ships: it is written " +
                            "into the built Tilemap's colour, so a layer left at 0.5 here is 0.5 in the " +
                            "running game. To dim a layer only while authoring, use Alpha on the header row.",
                            v => DialLook("Set game opacity", () => layer.opacity = v), 124f,
                            defaultValue: 1f),
                        sortLabel, sortInt));

                    card.Add(Z.Row(
                        Z.ToggleButton("Solid", "Whether this layer's tiles collide.",
                            layer.solid, v => { Dial("Toggle layer solid", () => layer.solid = v); RebuildLayers(); }),
                        Z.ToggleButton("One-way", "Solid only from above — a side-scrolling platform. Ignored in top-down collision.",
                            layer.oneWay, v => Dial("Toggle one-way", () => layer.oneWay = v)),
                        Z.HSpace(),
                        Z.Segmented(layer.colliderShape == UnityEngine.Tilemaps.Tile.ColliderType.Grid ? 0 : 1,
                            new[] { "Grid", "Sprite" },
                            "Grid: full-square collision. Sprite: the sprite's own outline, for slopes.",
                            s => Dial("Set collider shape", () =>
                                layer.colliderShape = s == 0 ? UnityEngine.Tilemaps.Tile.ColliderType.Grid
                                                             : UnityEngine.Tilemaps.Tile.ColliderType.Sprite))));

                    if (layer.solid)
                        card.Add(Z.Field("Solid tag", "Optional. Only cells whose tile or source prop carries this tag collide — tags narrow a solid layer, never widen one.",
                            LauAssetElement.Build(layer.solidTag,
                                picked => Dial("Set solid tag", () => layer.solidTag = picked as TileTag),
                                typeof(TileTag), _fieldThumbs, "Tag", "Assets/Cartographer/Tags",
                                "Tag that decides which of this layer's cells collide.")));
                }

                layersList.Add(card);
                ZuiReorder.MakeGrip(grip, card, layersList, (from, to) =>
                {
                    Dial("Reorder layers", () =>
                    {
                        var moved = level.layers[from];
                        level.layers.RemoveAt(from);
                        level.layers.Insert(to, moved);
                    });
                    if (activeLayer == from) activeLayer = to;
                    RebuildLayers();
                });
            }
        }

        /// The layer's BACKGROUND TILE, as a READ-OUT rather than a picker. The control it replaces was a
        /// `Z.Object<LevelTile>` asset field, and that was the wrong instrument twice over: a tileset holds
        /// hundreds of tiles, so a flat by-name dropdown is a worse way to choose one than the grid of pixels
        /// already sitting two boxes below (and the author is going to use that grid anyway); and the field
        /// offered every LevelTile in the PROJECT rather than this layer's, which is exactly how a level ends
        /// up with a background tile its own tileset has never heard of. Choosing now happens where the tiles
        /// are — right-click one in the Tileset box — and this line's whole job is to SAY which tile holds the
        /// role, including when that tile is an orphan the grid cannot badge.
        ///
        /// FIXED WIDTH, NoWrap, clipped: the name it shows varies in length, and a control that resized with
        /// its content would shove Sort and Opacity sideways every time the role changed.
        VisualElement BackgroundTileReadout(LevelLayer layer)
        {
            var tile = layer.backgroundTile;
            bool inSet = tile != null && layer.tileset != null && layer.tileset.tiles != null
                         && layer.tileset.tiles.Contains(tile);
            string name = tile == null ? null
                : string.IsNullOrEmpty(tile.displayName) ? tile.name : tile.displayName;

            const string what =
                "THE LAYER'S BACKGROUND. This tile fills every cell of this layer that has nothing painted on " +
                "it, inside the level's bounds — a floor layer then needs no painting at all. It cannot be " +
                "erased cell by cell: painting COVERS it, and erasing that paint uncovers it again.  ";

            string tip = tile == null
                ? what + "Nothing holds the role here, so this layer's unpainted cells are empty. To set one, " +
                         "right-click a tile in the Tileset box below and choose \"Set as background tile\"."
                : inSet
                    ? what + $"'{name}' holds it, and wears a violet corner wedge in the Tileset box below. " +
                             "Right-click another tile there to move the role; the × beside this name drops it."
                    : what + $"'{name}' holds it — but that tile is NOT in " +
                             (layer.tileset != null ? $"this layer's tileset ('{layer.tileset.name}')" : "any tileset, because this layer has none") +
                             ", so no cell in the Tileset box can wear its badge and you cannot point at it " +
                             "there. It still fills the layer. The × beside this name drops it; to replace it, " +
                             "right-click a tile in the Tileset box and choose \"Set as background tile\".";

            // Hand-truncated, not left to the clip: a name that runs past its box is the one failure this
            // read-out cannot afford, because the ORPHAN suffix is the whole message and it sits at the end.
            string shown = tile == null ? "none" : inSet ? Ellipsize(name, 30) : Ellipsize(name, 16) + " — not in tileset";
            var value = Z.Text(shown, ZuiText.Small, tip);
            value.style.width = 190f;
            value.style.whiteSpace = WhiteSpace.NoWrap;
            value.style.overflow = Overflow.Hidden;
            // The orphan is the one state the author has to be TOLD about — the grid cannot show it at all.
            if (tile != null && !inSet) value.style.color = new Color(1f, 0.72f, 0.3f);

            // CLEAR LIVES HERE, not only on the grid's card. The card's Clear hangs off right-clicking a TILE,
            // which needs a cell in the grid to right-click — and the one case where clearing is most needed
            // is precisely the one where no such cell exists: a background tile that is not in this layer's
            // tileset (Canari Playground → Terrain → 'Dungeon Floor', live). That path would strand the
            // author. This one is reachable from the value it clears, always. No confirm: it is one Dial and
            // Ctrl+Z brings it back, and the rule is to confirm only what undo cannot.
            // AN ERASER, NOT AN ×, and not tinted. This is a FIELD RESET — it empties one slot — while the ×
            // one row above deletes the entire layer. They were the same glyph at the same size a line apart,
            // with the destructive one on top, and the only way to tell them apart was to hover: exactly the
            // question the user asked ("What is the difference between clear and delete?"). Different GLYPH
            // (eraser vs ×) and different WEIGHT (plain vs the destructive red) so the answer is visible
            // rather than merely readable, and the tooltip names the scope in its first three words.
            var clear = Z.Button("×",
                tile == null
                    ? "Clear the background TILE of this layer. Nothing to clear — this layer has none. " +
                      "(This only empties this one field; the × on the layer's header row removes the whole layer.)"
                    : $"Clear the background TILE of this layer: '{name}' stops filling its unpainted cells, " +
                      "which go back to empty. The tile asset itself is untouched, the layer is untouched, and " +
                      "Ctrl+Z brings it back. (The × on the layer's header row is the one that removes the " +
                      "whole layer.)",
                () => { SetLayerBackgroundTile(layer, null); RebuildLayers(); });
            var eraserIcon = Z.Icon("eraser", 13f);
            if (eraserIcon != null) { clear.text = ""; clear.Add(eraserIcon); clear.W(22f); }
            else clear.text = "Clear";   // the glyph did not resolve — say it in words rather than reuse ×
            clear.SetEnabled(tile != null);

            // Always present, enabled or not, so the row keeps its shape as the role comes and goes.
            //
            // "Background", not "Background tile": this label SETS the card's aligned label column (every
            // Z.Field label in the box is sized to the widest), and the extra word costs Tileset, Sort and
            // Solid tag 30px of their rows apiece — enough to push the card past the pane. The value beside
            // it names a tile, the tooltip and the Tileset box's own actions both say "background tile", so
            // nothing is lost but the repetition.
            return Z.Field("Background", tip, Z.Row(value, clear));
        }

        /// Shorten to `max` characters with a trailing ellipsis. IMGUI/UITK both happily draw past a fixed
        /// width, so a name that might overflow is cut as a STRING before it is drawn (the truncation rule).
        static string Ellipsize(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, Mathf.Max(1, max - 1)) + "…";

        // ── Tileset box (was "Palette") ────────────────────────────────────────
        /// Rebuild the box body. Only for STRUCTURAL changes — a different active layer, a different
        /// tileset. Setting a brush must NOT come through here: the shared view owns real workspace state
        /// (scroll, tab, selection, a pinned actions card) that a rebuild would throw away.
        void RebuildTilesetBox()
        {
            if (tilesetBody == null) return;
            if (tilesetView != null) savedTilesetScrollX = tilesetView.ScrollX;
            tilesetBody.Clear();
            // ☠️ AND THE HEADER TOO. This method re-creates the box's CONTENT but not the BOX, and the dials
            // live on the BOX's title row — so every call appended a second, third, fourth Zoom/Lines/Alpha/
            // Seamless/Osc-frame strip to a row that must never wrap, until it ran off the pane. It fires on
            // every layer switch and every tileset change, so it was one click away at all times. Caught by
            // eye 2026-08-03; ZuiBox.ClearHeaderContent was added for exactly this.
            tilesetBox?.ClearHeaderContent();
            tilesetView = null;
            brushLine = null;

            var set = ActiveLayer?.tileset;
            if (set == null)
            {
                tilesetBody.Add(Z.Text("The active layer has no tileset — assign one on the layer above.",
                    ZuiText.Subtle, "This box shows the active layer's tileset."));
                tilesetBody.Add(Z.Button("Tileset builder…", "Opens the sheet-curation window: marquee cells on a sprite sheet and pluck them into tiles.",
                    () => TilesetBuilderWindow.OpenFor(null)));
                return;
            }

            // THE SAME control the Tileset Builder hosts — same grid, same tabs, same edit suite. What is
            // different is only what a selection additionally means here, which GridSelectionChanged owns.
            tilesetView = new TilesetGridView(this, savedTilesetScrollX);
            tilesetBody.Add(tilesetView);
            tilesetView.AddHeaderDials(tilesetBox);

            // Stable-layout rule: the brush readout is a permanently reserved line — appearing text must
            // never shove the boxes below it.
            brushLine = (Label)Z.Text("", ZuiText.Small,
                "WHAT YOU ARE PAINTING WITH right now: one tile, a multi-cell pattern (Ctrl-click or marquee " +
                "several tiles above to build one), a whole clump, or a prop stamp. The canvas draws the same " +
                "thing half-transparent under the cursor before you click.");
            brushLine.style.height = 16f;
            brushLine.style.whiteSpace = WhiteSpace.NoWrap;
            brushLine.style.overflow = Overflow.Hidden;
            tilesetBody.Add(brushLine);
            RefreshBrushLine();

            tilesetBody.Add(Z.Button("Tileset builder…", "Opens the sheet-curation window: marquee cells on a sprite sheet and pluck them into tiles of this tileset.",
                () => TilesetBuilderWindow.OpenFor(set)));
        }

        /// The reserved line's TEXT changes; its geometry never does. Cheap enough to call on every brush
        /// change, which is exactly why brush changes no longer rebuild the box.
        void RefreshBrushLine()
        {
            if (brushLine == null) return;
            brushLine.text = BrushSummary();
        }

        /// WHAT AM I PAINTING WITH — in one line, always answered. The ghost shows it under the cursor; this
        /// is the persistent read for when the pointer is not on the canvas at all. Every brush kind names
        /// itself, and "none" says what to do about it rather than going blank (a blank line is the state the
        /// author cannot tell apart from a broken one).
        internal string BrushSummary()
        {
            // `brush.Count > 0` and not just `brushClump != null`: a clump with nothing paintable in it is not
            // a brush, and saying it is was how the phantom reload clump (see the field's note) got announced.
            if (brushClump != null && brush.Count > 0)
                return $"Brush: clump '{NameOf(brushClump.displayName, "clump")}' — {brush.Count} tiles, stamped as one object";
            if (brush.Count == 0)
                return StampProp != null
                    ? $"Brush: prop '{NameOf(StampProp.displayName, StampProp.name)}' — stamped with the Stamp tool"
                    : "Brush: none — click a tile in the Tileset box to pick one";
            if (brush.Count == 1) return "Brush: " + TileName(brush[0].tile);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var (off, _, _) in brush)
            {
                if (off.x < minX) minX = off.x;
                if (off.x > maxX) maxX = off.x;
                if (off.y < minY) minY = off.y;
                if (off.y > maxY) maxY = off.y;
            }
            return $"Brush: {maxX - minX + 1}×{maxY - minY + 1} pattern ({brush.Count} tiles)";
        }

        /// A tile's human name, never blank — an unnamed tile still has to be identifiable in a status line.
        internal static string TileName(LevelTile t) =>
            t == null ? "nothing" : NameOf(t.displayName, t.name);

        static string NameOf(string display, string fallback) =>
            !string.IsNullOrEmpty(display) ? display : string.IsNullOrEmpty(fallback) ? "unnamed" : fallback;

        // ── ITilesetGridHost: this window as the shared grid's second host ─────────────────────────
        Tileset ITilesetGridHost.GridTileset => ActiveLayer?.tileset;
        TilesetGridOptions ITilesetGridHost.GridOptions => tilesetOptions;
        VisualElement ITilesetGridHost.GridCardHost => rootVisualElement;

        void ITilesetGridHost.GridWarn(string message) => FlashCanvasStatus(message);
        void ITilesetGridHost.GridReport(string message) => ReportCanvasStatus(message);

        void ITilesetGridHost.GridContentChanged()
        {
            // A tile the level paints with just changed (pixels flipped, a cell cleared, a fork minted) —
            // the canvas draws from those sprites, so it has to re-read them.
            LauAssetGridGUI.ClearCache(_gridThumbs);
            canvas?.RebuildContent();
            RefreshTagOverlay();
        }

        /// THE SEAM. In the Builder a selection exists to EDIT tiles; here it does that AND arms the paint
        /// brush — which is the whole reason the grid hands selection back instead of keeping it private.
        void ITilesetGridHost.GridSelectionChanged()
        {
            brushClump = null;
            StampProp = null;
            RebuildBrushFromSelection();
            // No SetTool here either — see EnsureToolCanPaintTiles, which AfterBrushChanged calls.
            AfterBrushChanged();
            RebuildProps();
        }

        // ── ITilesetGridBrush: the grid MARKS what the brush holds ─────────────────────────────────────────
        /// The grid highlighted its own SELECTION, and the brush is frequently not the selection: the
        /// eyedropper and the region sampler both call ClearSelection(notify:false) on purpose (a notifying
        /// clear would re-derive the brush from the selection and wipe it), so the author could pick a tile
        /// off the canvas and see nothing at all in the Tileset box. This is the second, independent fact.
        IReadOnlyCollection<LevelTile> ITilesetGridBrush.BrushTiles => brushTiles;

        /// Guarded the same way BrushSummary is: a clump the brush drew NO tiles from cannot be painted, so
        /// reporting it would make the grid claim a brush that does not exist.
        Clump ITilesetGridBrush.BrushClump => brush.Count > 0 ? brushClump : null;

        // ── ITilesetGridTileRole: the active layer's BACKGROUND TILE, as a role the grid shows and sets ────
        /// A layer's background tile fills every unpainted cell inside the level's bounds — so it is the most
        /// consequential tile in the tileset, and it used to be set through a field in the Layers box that
        /// named an asset the author then had to FIND in the grid (and that offered every tile in the project,
        /// including ones this tileset does not contain). The grid owns the choice now: the tile wears a badge,
        /// and the tile under the pointer can be given the role where it lives. The Tileset Builder has no
        /// layers, does not implement this interface, and is unaffected.
        LevelTile ITilesetGridTileRole.RoleTile => ActiveLayer?.backgroundTile;
        string ITilesetGridTileRole.RoleName => "background tile";
        string ITilesetGridTileRole.RoleLabel =>
            ActiveLayer != null ? $"background tile for layer '{ActiveLayer.name}'" : "background tile";
        bool ITilesetGridTileRole.CanSetRole => ActiveLayer != null;

        void ITilesetGridTileRole.SetRoleTile(LevelTile tile)
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            SetLayerBackgroundTile(layer, tile);
            RebuildLayers();   // the Layers box's read-out names the same value; it must agree
            ReportCanvasStatus(tile != null
                ? $"'{(string.IsNullOrEmpty(tile.displayName) ? tile.name : tile.displayName)}' is now the " +
                  $"background of '{layer.name}' — it fills every cell nothing is painted on. Ctrl+Z undoes it."
                : $"'{layer.name}' has no background tile now — its unpainted cells are empty. Ctrl+Z brings it back.");
        }

        /// THE one door every background-tile edit goes through — the tileset grid's actions card is the only
        /// way IN now, but the Layers box read-out and the grid's badge both have to be repainted after a
        /// write, so the write still belongs in one place. Undoable in a single step, like every other Dial.
        void SetLayerBackgroundTile(LevelLayer layer, LevelTile tile)
        {
            if (layer == null) return;
            Dial("Set background tile", () => layer.backgroundTile = tile);   // Dial repaints the canvas: the fill moved
            tilesetView?.RepaintOverlay();
        }

        void ITilesetGridHost.GridClumpPicked(Clump clump)
        {
            if (clump == null)
            {
                brush.Clear();
                brushClump = null;
                AfterBrushChanged();
                return;
            }
            SetBrushClump(clump);
        }

        // ── Props box ─────────────────────────────────────────────────────────
        void RebuildProps()
        {
            if (propsGrid == null) return;
            propsGrid.Clear();

            var props = level.biome != null ? level.biome.props : null;
            if (props == null || props.Count == 0)
            {
                propsGrid.Add(Z.Text("No biome (or an empty one) — pick a biome in the Level box to stamp its props.",
                    ZuiText.Subtle, "The Props box lists the level's biome's structures."));
                return;
            }

            var grid = new VisualElement();
            grid.AddToClassList("lau-tool-shell__row-wrap");
            propsGrid.Add(grid);

            foreach (var prop in props)
            {
                if (prop == null) continue;
                var c = prop;
                string label = string.IsNullOrEmpty(c.displayName) ? c.name : c.displayName;
                string what = c.Explain();
                var sw = Swatch(
                    Thumb(c),
                    c == StampProp,
                    string.IsNullOrEmpty(what) ? label : label + " — " + what,
                    _ =>
                    {
                        StampProp = c;
                        brush.Clear();
                        brushClump = null;
                        tilesetView?.ClearSelection(notify: false);
                        SetTool(CanvasTool.Stamp);
                        RebuildProps();
                        AfterBrushChanged();
                    },
                    c.procgen ? "⚙" : null, 48f);

                // A prop that draws nothing has no thumbnail to identify it. Give it its marker colour and
                // its initials, the same colour the canvas marks its cells with, so the palette and the map
                // agree about which invisible thing is which.
                if (!c.HasVisual)
                {
                    var mc = c.MarkerColor;
                    sw.style.backgroundColor = new Color(mc.r, mc.g, mc.b, 0.35f);
                    sw.style.borderTopColor = sw.style.borderBottomColor =
                        sw.style.borderLeftColor = sw.style.borderRightColor = mc;
                    if (c != StampProp)
                        sw.style.borderTopWidth = sw.style.borderBottomWidth =
                            sw.style.borderLeftWidth = sw.style.borderRightWidth = 1f;
                    var initials = new Label(Initials(label)) { pickingMode = PickingMode.Ignore };
                    initials.style.position = Position.Absolute;
                    initials.style.left = initials.style.right = initials.style.top = initials.style.bottom = 0f;
                    initials.style.unityTextAlign = TextAnchor.MiddleCenter;
                    initials.style.fontSize = 14f;
                    initials.style.color = Color.white;
                    sw.Add(initials);
                }
                grid.Add(sw);
            }

            if (StampProp != null)
            {
                string what = StampProp.Explain();
                propsGrid.Add(Z.Row(
                    Z.Text(StampProp.displayName, ZuiText.Small, "The selected stamp."),
                    Z.Flexible(),
                    Z.Button("Edit prop…", "Opens the selected prop in the prop editor window.",
                        () => PropWindow.OpenFor(StampProp))));

                // What placing this actually does. Reserved line (stable-layout rule) — it must not shove
                // the boxes below when the selection changes to a prop with more to say.
                var explain = (Label)Z.Text(what, ZuiText.Subtle,
                    "What this prop does in the game — the prop's own description, or, failing that, " +
                    "assembled from the PropBehaviours on its prefabs.");
                explain.style.height = 16f;
                explain.style.whiteSpace = WhiteSpace.NoWrap;
                explain.style.overflow = Overflow.Hidden;
                propsGrid.Add(explain);
            }
        }

        // ── Decals box ─────────────────────────────────────────────────────────
        void RebuildDecals()
        {
            if (decalsList == null) return;
            decalsList.Clear();

            if (level.decals == null || level.decals.Count == 0)
            {
                decalsList.Add(Z.Text("No decals yet.", ZuiText.Subtle,
                    "Free sprites placed in the level appear here."));
                return;
            }

            for (int i = 0; i < level.decals.Count; i++)
            {
                int idx = i;
                var d = level.decals[idx];
                decalsList.Add(Z.Row(
                    Z.Text(d.sprite != null ? d.sprite.name : d.IsAnimated ? "(animated)" : "(empty)",
                        ZuiText.Small, "The decal's sprite."),
                    Z.Text($"({d.position.x:0.#}, {d.position.y:0.#}) on {d.layer}", ZuiText.Subtle,
                        "Where the decal sits, and the layer whose sorting it inherits."),
                    Z.Flexible(),
                    Z.Button("×", "Remove this decal.",
                        () => { Dial("Remove decal", () => level.decals.RemoveAt(idx)); RebuildDecals(); }).W(24f)));
            }
        }

        /// Play-mode transitions destroy unflagged runtime textures while this window's retained UI keeps
        /// pointing at them — swatches went dark until a click happened to rebuild them. The thumbnails are
        /// flagged to survive (see Thumb), and the grids rebuild on both transitions anyway so a texture
        /// that was made on the other side of the boundary can never linger dead.
        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode && change != PlayModeStateChange.EnteredPlayMode)
                return;
            LauAssetGridGUI.ClearCache(_gridThumbs);
            tilesetView?.RefreshCells();
            RebuildProps();
        }

        // ── shared swatch helpers ──────────────────────────────────────────────
        Texture2D Thumb(Object item)
        {
            if (item == null) return null;
            if (_gridThumbs.TryGetValue(item, out var cached) && cached != null) return cached;
            var tex = (item as Laubrary.PreviewKit.IVisualPreview)?.RenderPreviewTexture();
            // Editor-preview lifetime: without this, entering/exiting Play destroys the texture under the
            // retained swatch and it renders dark.
            if (tex != null) tex.hideFlags = HideFlags.HideAndDontSave;
            _gridThumbs[item] = tex;
            return tex;
        }

        /// "Aisle Gate" → "AG" — what a swatch shows when the thing it stands for has no pixels.
        static string Initials(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "?";
            var parts = label.Split(new[] { ' ', '_', '-' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0].Substring(0, Mathf.Min(2, parts[0].Length)).ToUpperInvariant();
            return ("" + parts[0][0] + parts[1][0]).ToUpperInvariant();
        }

        static VisualElement Swatch(Texture2D thumb, bool selected, string tooltipText, System.Action<bool> onClick,
            string badge = null, float size = 36f)
        {
            var swatch = new Button { tooltip = tooltipText };
            // The click must know whether Ctrl was held — that is what turns a click into "add to pattern".
            swatch.clickable.clickedWithEventInfo += e =>
                onClick(e is IPointerEvent pe ? pe.ctrlKey : e is IMouseEvent me && me.ctrlKey);
            swatch.style.width = size;
            swatch.style.height = size;
            swatch.AddToClassList("lau-tile-palette__swatch");
            if (thumb != null)
            {
                swatch.style.backgroundImage = Background.FromTexture2D(thumb);
                swatch.AddToClassList("lau-tile-palette__picture");
            }
            swatch.EnableInClassList("lau-tile-palette__choice--selected", selected);
            if (!string.IsNullOrEmpty(badge))
            {
                var b = new Label(badge) { pickingMode = PickingMode.Ignore };
                b.AddToClassList("lau-tile-palette__badge");
                swatch.Add(b);
            }
            return swatch;
        }
    }
}
