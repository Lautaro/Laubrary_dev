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
    /// boxes edit the open level's layers, palette, props and decals, and the Scene view is the canvas a
    /// LevelInstance previews through. (The old window's core defect was being a Prop editor that kept the
    /// level as a field inside a box — that window survives as the prop editor, opened from the Props box.)
    public partial class CartographerWindow : ZuiAssetWindow<LevelAsset>
    {
        [MenuItem("Laubrary/Cartographer")]
        static void Open() => GetWindow<CartographerWindow>("Cartographer");

        /// Jump straight to one level — for gameplay-side tooling that wants "edit this level".
        public static void OpenFor(LevelAsset level)
        {
            var w = GetWindow<CartographerWindow>("Cartographer");
            if (level != null) w.SetAsset(level);
        }

        LevelAsset level => Current;

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
        readonly HashSet<int> paletteSel = new();
        Clump brushClump;
        internal IReadOnlyList<(Vector2Int off, LevelTile tile, int shift)> Brush => brush;
        internal LevelTile PaintTile => brush.Count > 0 ? brush[0].tile : null;
        internal Prop StampProp { get; private set; }

        /// One tile, offset zero — the eyedropper's single pick and the plain palette click.
        internal void SetBrushSingle(LevelTile tile)
        {
            brush.Clear();
            paletteSel.Clear();
            brushClump = null;
            if (tile != null) brush.Add((Vector2Int.zero, tile, 0));
            StampProp = null;
            RebuildPalette();
            RebuildProps();
        }

        /// A sampled arrangement from the level — offsets exactly as they sit in the room.
        internal void SetBrushSample(List<(Vector2Int off, LevelTile tile)> cells)
        {
            brush.Clear();
            paletteSel.Clear();
            brushClump = null;
            foreach (var (off, tile) in cells) brush.Add((off, tile, 0));
            StampProp = null;
            RebuildPalette();
            RebuildProps();
        }

        /// A CLUMP is the brush: the whole locked arrangement paints as one gesture, overhang cells
        /// routing themselves one layer further front via their stored shift.
        internal void SetBrushClump(Clump clump)
        {
            brush.Clear();
            paletteSel.Clear();
            brushClump = clump;
            StampProp = null;
            if (clump?.cells != null)
            {
                var b = clump.Bounds;
                foreach (var pc in clump.cells)
                    if (pc.tile != null)
                        // Builder rows go down the screen, world rows go up — same flip as the palette.
                        brush.Add((new Vector2Int(pc.offset.x - b.xMin, -(pc.offset.y - b.yMin)), pc.tile, pc.layerShift));
            }
            SetTool(SceneTool.Paint);
            RebuildPalette();
            RebuildProps();
        }

        /// Palette cells selected together form a pattern: their positions in the FIXED palette grid become
        /// the brush's offsets (palette rows go down the screen, world rows go up — hence the flip).
        void RebuildBrushFromPalette()
        {
            brush.Clear();
            var set = ActiveLayer?.tileset;
            if (set == null || paletteSel.Count == 0) return;
            int colsP = Mathf.Max(1, set.paletteColumns);

            int minC = int.MaxValue, minR = int.MaxValue;
            foreach (var idx in paletteSel)
            {
                minC = Mathf.Min(minC, idx % colsP);
                minR = Mathf.Min(minR, idx / colsP);
            }
            var ordered = new List<int>(paletteSel);
            ordered.Sort();
            foreach (var idx in ordered)
            {
                if (idx < 0 || idx >= set.tiles.Count || set.tiles[idx] == null) continue;
                brush.Add((new Vector2Int(idx % colsP - minC, -(idx / colsP - minR)), set.tiles[idx], 0));
            }
        }

        void TogglePaletteCell(int idx, bool additive)
        {
            if (!additive) { paletteSel.Clear(); paletteSel.Add(idx); }
            else if (!paletteSel.Remove(idx)) paletteSel.Add(idx);
            StampProp = null;
            brushClump = null;
            RebuildBrushFromPalette();
            SetTool(SceneTool.Paint);
            RebuildPalette();
            RebuildProps();
        }
        internal LevelLayer ActiveLayer =>
            level != null && level.layers != null && activeLayer >= 0 && activeLayer < level.layers.Count
                ? level.layers[activeLayer] : null;

        readonly Dictionary<Object, Texture2D> _fieldThumbs = new();
        readonly Dictionary<Object, Texture2D> _gridThumbs = new();

        VisualElement levelBox, layersList, paletteGrid, propsGrid, decalsList, tagsList;

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
            base.OnEnable();
            Undo.undoRedoPerformed += RefreshInstance;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            OnEnableSceneTools();
        }

        protected override void OnDisable()
        {
            OnDisableSceneTools();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Undo.undoRedoPerformed -= RefreshInstance;
            base.OnDisable();
            LauAssetGridGUI.ClearCache(_fieldThumbs);
            LauAssetGridGUI.ClearCache(_gridThumbs);
            if (_levelPreviewTex != null) DestroyImmediate(_levelPreviewTex);
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
            activeLayer = 0;
            brush.Clear();
            paletteSel.Clear();
            StampProp = null;
            LauAssetGridGUI.ClearCache(_fieldThumbs);
            LauAssetGridGUI.ClearCache(_gridThumbs);
        }

        // ── mutation helper — every data edit goes through here ───────────────
        /// Undoable, dirties the asset, and pushes the edit into whatever scene instance previews this level.
        void Dial(string undoLabel, System.Action apply)
        {
            if (level == null) return;
            Undo.RecordObject(level, undoLabel);
            apply();
            EditorUtility.SetDirty(level);
            RefreshInstance();
            RefreshTagOverlay();
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
            SceneInstance()?.Rebuild();
            RefreshLevelPreview();
        }

        Texture2D _levelPreviewTex;

        void RefreshLevelPreview()
        {
            if (levelPreview == null || level == null) return;
            if (_levelPreviewTex != null) DestroyImmediate(_levelPreviewTex);
            _levelPreviewTex = CartographerPreview.RenderLevel(level);
            if (_levelPreviewTex != null) _levelPreviewTex.hideFlags = HideFlags.HideAndDontSave;
            levelPreview.image = _levelPreviewTex;
        }

        // ── the window body ────────────────────────────────────────────────────
        Image levelPreview;

        protected override void BuildAsset(VisualElement root, LevelAsset asset)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            // T-0084 — the section toggle bar rides at the very top of the per-asset UI, spanning the full
            // window width (same placement as Pyre/Chunks). Its host is added FIRST (empty) and filled LAST,
            // once every section below exists for it to address.
            var barHost = new VisualElement();
            barHost.style.flexShrink = 0f;
            if (_barReservedH > 0f) barHost.style.minHeight = _barReservedH;   // space reserved before anything paints
            root.Add(barHost);

            var left = new VisualElement();
            left.style.minWidth = 260f;
            left.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            left.Add(scroll);
            var body = scroll.contentContainer;

            // Right pane: a live rasterised preview of the level. The Scene view stays the CANVAS; this is
            // the at-a-glance whole-level read beside the tools, refreshed on every edit.
            var previewPane = new VisualElement();
            previewPane.AddToClassList("zui-stage");
            previewPane.style.flexGrow = 1f;
            previewPane.style.minWidth = 120f;
            previewPane.tooltip = "The whole level, rasterised live. Paint in the Scene view; glance here.";
            levelPreview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            levelPreview.style.flexGrow = 1f;
            previewPane.Add(levelPreview);

            root.Add(Z.Split("cartographer", 380f, left, previewPane));
            RefreshLevelPreview();

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

            paletteGrid = new VisualElement();
            var paletteSection = Z.Section("Palette", "The active layer's tileset. Click a tile to make it the paint tile; a badge marks a tile with interchangeable variants.",
                "cartographer.palette");
            paletteSection.Add(paletteGrid);
            body.Add(paletteSection);
            _barUnits.Add(("Palette", paletteSection));
            RebuildPalette();

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

            toolBox = new VisualElement();
            var toolSection = Z.Section("Tool", "What a click in the Scene view does, and its settings.",
                "cartographer.tool");
            toolSection.Add(toolBox);
            body.Add(toolSection);
            _barUnits.Add(("Tool", toolSection));
            RebuildToolBox();

            tagsList = new VisualElement();
            var tagsSection = Z.Section("Tile tags", "Gameplay labels on the ACTIVE layer, and the scene overlay that makes tagged cells visible — invisible metadata is unverifiable metadata.",
                "cartographer.tags");
            tagsSection.Add(tagsList);
            body.Add(tagsSection);
            _barUnits.Add(("Tile tags", tagsSection));
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
                Z.ToggleButton("Show in scene", "Tint every tagged cell in the Scene view with its tag's colour, " +
                    "and list the hovered cell's tags at the cursor.",
                    showTagOverlay, v => { showTagOverlay = v; RefreshTagOverlay(); SceneView.RepaintAll(); })));
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
                Z.Field("Size", "The level's extent in cells — where default fill stops and what the thumbnail frames.",
                    Z.Row(
                        Z.Int(b.width, "Bounds width in cells.", v => Dial("Set bounds", () => { var r = level.bounds; r.width = Mathf.Max(1, v); level.bounds = r; }), 46f),
                        Z.Int(b.height, "Bounds height in cells.", v => Dial("Set bounds", () => { var r = level.bounds; r.height = Mathf.Max(1, v); level.bounds = r; }), 46f)))));

            levelBox.Add(Z.Field("Collision", "How solid layers collide: top-down walls, or side-scroll with one-way platforms.",
                Z.Segmented((int)level.collision, new[] { "Top-down", "Side-scroll" },
                    "Collider setup for this level's solid layers.",
                    i => Dial("Set collision mode", () => level.collision = (LevelCollision)i))));

            var inst = SceneInstance();
            levelBox.Add(Z.Row(
                Z.Text(inst != null ? "Previewing in scene: " + inst.gameObject.name : "Not in the open scene.",
                    ZuiText.Subtle,
                    "The scene object this level is previewed through. Painting edits the asset; the instance rebuilds."),
                Z.Flexible(),
                Z.Button(inst != null ? "Rebuild preview" : "Preview in scene",
                    "Builds (or rebuilds) a scene instance of this level so the Scene view can show it.",
                    () =>
                    {
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

            levelBox.Add(Z.Box("Generate", "Fill this level from a recipe. Replaces GENERATED content only — authored paints, stamps and decals survive, which is what makes hybrid levels possible.",
                Z.Field("Recipe", "Seed-and-dials description of a KIND of level.",
                    LauAssetElement.Build(genRecipe,
                        picked => { genRecipe = picked as LevelRecipe; RebuildLevelBox(); },
                        typeof(LevelRecipe), _fieldThumbs, "Recipe", "Assets/Cartographer/Recipes",
                        "Recipe to generate from.")),
                Z.Row(
                    Z.Field("Seed", "Same recipe and seed always produce the same level.",
                        Z.Int(genSeed, "Generation seed.", v => genSeed = v, 80f)),
                    Z.Button("Roll", "Pick a new random seed.",
                        () => { genSeed = Random.Range(0, 1_000_000); RebuildLevelBox(); }),
                    Z.Flexible(),
                    Z.Button("Generate", "Clear this level's generated content and rebuild it from the recipe and seed.",
                        () =>
                        {
                            if (genRecipe == null || level == null) return;
                            int n = 0;
                            Dial("Generate level", () => n = LevelGenerator.Generate(genRecipe, genSeed, level));
                            Rebuild();
                            Debug.Log($"[Cartographer] Generated '{level.name}' from '{genRecipe.name}' seed {genSeed}: {n} props placed.", level);
                        })),
                Z.Row(
                    Z.Text("Procgen", ZuiText.Subtle,
                        "The second pass: props marked Procgen mutate the level from the seed above — gates close, exits get picked. Replaces generated content only; authored cells survive a re-run."),
                    Z.Flexible(),
                    Z.Button("Run procgen", "Clear this level's generated content and run every procgen prop's pass with the seed above.",
                        () =>
                        {
                            if (level == null) return;
                            int n = 0;
                            Dial("Run procgen", () => n = LevelProcgen.Run(level, genSeed));
                            Rebuild();
                            Debug.Log($"[Cartographer] Procgen on '{level.name}' seed {genSeed}: {n} procgen props ran.", level);
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
                card.style.marginBottom = 4f;

                var grip = Z.Text("≡", ZuiText.Subtle, "Drag to reorder this layer.");
                grip.style.width = 14f;

                var header = Z.Row(
                    grip,
                    Z.ToggleButton("Active", "The active layer is what painting affects and what the palette shows.",
                        isActive, _ => { activeLayer = idx; RebuildLayers(); RebuildPalette(); }),
                    Z.TextInput(layer.name, "Name paints and prop cells address this layer by.",
                        v => { Dial("Rename layer", () => layer.name = v); }, 110f),
                    Z.ToggleButton("Visible", "Whether the layer draws, in the editor and at runtime.",
                        layer.visible, v => Dial("Toggle layer visibility", () => layer.visible = v)),
                    Z.ToggleButton("Lock", "A locked layer cannot be painted on.",
                        layer.locked, v => Dial("Toggle layer lock", () => layer.locked = v)),
                    Z.Flexible(),
                    Z.Button("×", "Remove this layer. Its paints stay in the asset until painted over.",
                        () => { Dial("Remove layer", () => level.layers.RemoveAt(idx)); RebuildLayers(); RebuildPalette(); }).W(24f));
                card.Add(header);

                if (isActive)
                {
                    card.Add(Z.Field("Tileset", "The palette this layer paints from.",
                        LauAssetElement.Build(layer.tileset,
                            picked => { Dial("Set layer tileset", () => layer.tileset = picked as Tileset); RebuildPalette(); },
                            typeof(Tileset), _fieldThumbs, "Tileset", "Assets/Cartographer/Tilesets",
                            "Tileset the palette shows while this layer is active.")));

                    // The tile picker is the WIDE control here (label + type icon + an asset name that varies
                    // with whatever is bound), so it takes the line and the two short dials share the next —
                    // the card-layout rule. Packed onto one row it needed 438.7px of a 396.4px pane and hung
                    // the Opacity slider 42.2px out over the divider, measured at the 900px window.
                    card.Add(Z.Field("Default tile", "Fills every unpainted cell inside the level's bounds — a floor layer needs no painting at all.",
                        Z.Object<LevelTile>(layer.defaultTile, "Tile shown in every unpainted cell within bounds.",
                            v => Dial("Set default tile", () => layer.defaultTile = v), 150f)));
                    card.Add(Z.Row(
                        Z.Field("Sort", "Draw order among the level's layers. Higher draws in front.",
                            Z.Int(layer.sortingOrder, "Draw order among the level's layers. Higher draws in front.",
                                v => Dial("Set layer sorting", () => layer.sortingOrder = v), 46f)),
                        Z.MicroSlider("Opacity", layer.opacity, 0f, 1f, "Layer opacity, multiplied into every tile.",
                            v => Dial("Set layer opacity", () => layer.opacity = v), 110f)));

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

        // ── Palette box ────────────────────────────────────────────────────────
        // Composed clump thumbnails, rebuilt with the palette — cheap (a clump is a handful of cells)
        // and freed on every rebuild so nothing leaks.
        readonly List<Texture2D> _clumpThumbs = new();

        /// The whole clump as ONE image, cells blitted at their true offsets — a shelf swatch must look
        /// like a shelf, not like whichever fragment happens to be its first cell.
        Texture2D ComposeClumpThumb(Clump cl)
        {
            if (cl?.cells == null || cl.cells.Count == 0) return null;
            int cellPx = 0;
            foreach (var pc in cl.cells)
            {
                var s = CellSprite(pc.tile);
                if (s != null) cellPx = Mathf.Max(cellPx, CartographerPreview.CellPixels(s));
            }
            var b = cl.Bounds;
            if (cellPx <= 0 || b.width * cellPx > CartographerPreview.MaxSide ||
                b.height * cellPx > CartographerPreview.MaxSide) return null;

            var tex = CartographerPreview.NewCanvas(b.width * cellPx, b.height * cellPx);
            foreach (var pc in cl.cells)
            {
                var s = CellSprite(pc.tile);
                if (s == null) continue;
                // Builder offsets grow downward; texture rows grow upward.
                int x = (pc.offset.x - b.xMin) * cellPx;
                int y = (b.yMin + b.height - 1 - pc.offset.y) * cellPx;
                CartographerPreview.Blit(tex, s, x, y);
            }
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _clumpThumbs.Add(tex);
            return tex;
        }

        void RebuildPalette()
        {
            if (paletteGrid == null) return;
            paletteGrid.Clear();
            foreach (var t in _clumpThumbs) if (t != null) DestroyImmediate(t);
            _clumpThumbs.Clear();

            var layer = ActiveLayer;
            var set = layer?.tileset;
            if (set == null || set.tiles == null || set.tiles.Count == 0)
            {
                paletteGrid.Add(Z.Text("The active layer has no tileset — assign one on the layer above.",
                    ZuiText.Subtle, "The palette shows the active layer's tileset."));
                paletteGrid.Add(Z.Button("Tileset builder…", "Opens the sheet-curation window: marquee cells on a sprite sheet and pluck them into tiles.",
                    () => TilesetBuilderWindow.OpenFor(set)));
                return;
            }

            // The EXACT grid the Tileset Builder edits — same columns, same rows, same empty cells, 1px
            // gaps — because positions ARE the pattern data: Ctrl-selected neighbours paint together as a
            // temporary prop, and that only reads if both windows show one identical layout.
            int colsP = Mathf.Max(1, set.paletteColumns);
            int rowsP = set.EffectiveRows;
            VisualElement row = null;
            for (int i = 0; i < colsP * rowsP; i++)
            {
                if (i % colsP == 0)
                {
                    row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    paletteGrid.Add(row);
                }
                var t = i < set.tiles.Count ? set.tiles[i] : null;
                int idx = i;
                if (t == null)
                {
                    var spacer = new VisualElement();
                    spacer.style.width = 37f;
                    spacer.style.height = 37f;
                    row.Add(spacer);
                    continue;
                }
                row.Add(Swatch(
                    CellSprite(t),
                    paletteSel.Contains(idx),
                    (string.IsNullOrEmpty(t.displayName) ? t.name : t.displayName) +
                        (t.variants.Count > 1 ? $" — {t.variants.Count} variants, {t.variantPolicy}" : "") +
                        ". Ctrl-click to add to a multi-tile pattern.",
                    ctrl => TogglePaletteCell(idx, ctrl),
                    t.variants.Count > 1 ? "×" + t.variants.Count : null));
            }

            // Stable-layout rule: the pattern readout is a permanently reserved line — appearing text must
            // never shove the boxes below it.
            var patternLine = (Label)Z.Text(brush.Count > 1 ? $"Pattern brush: {brush.Count} tiles paint together." : "",
                ZuiText.Small, "The Ctrl-selected cells form a temporary clump — painting stamps the whole arrangement.");
            patternLine.style.height = 16f;
            patternLine.style.whiteSpace = WhiteSpace.NoWrap;
            patternLine.style.overflow = Overflow.Hidden;
            paletteGrid.Add(patternLine);

            // Clumps: the tileset's locked arrangements — click one and the whole OBJECT is the brush,
            // overhang cells landing one layer in front on their own.
            if (set.clumps != null && set.clumps.Count > 0)
            {
                paletteGrid.Add(Z.Text("Clumps", ZuiText.Small,
                    "Locked tile arrangements from this tileset. Painting stamps the whole object; cells " +
                    "marked 'one layer in front' in the builder route themselves there."));
                var wrap = new VisualElement();
                wrap.style.flexDirection = FlexDirection.Row;
                wrap.style.flexWrap = Wrap.Wrap;
                foreach (var cl in set.clumps)
                {
                    if (cl?.cells == null || cl.cells.Count == 0) continue;
                    var c2 = cl;
                    // Swatch sized to the clump's aspect so a 2×4 shelf reads as a tall thing, not a square.
                    var bounds = cl.Bounds;
                    float sw = 36f * Mathf.Clamp(bounds.width / (float)bounds.height, 0.5f, 2f);
                    wrap.Add(Swatch(
                        ComposeClumpThumb(cl),
                        brushClump == cl,
                        $"{cl.displayName} — {cl.cells.Count} tile(s), stamped as one object.",
                        _ => SetBrushClump(c2),
                        cl.cells.Count > 1 ? "×" + cl.cells.Count : null,
                        sw));
                }
                paletteGrid.Add(wrap);
            }

            paletteGrid.Add(Z.Button("Tileset builder…", "Opens the sheet-curation window: marquee cells on a sprite sheet and pluck them into tiles of this tileset.",
                () => TilesetBuilderWindow.OpenFor(set)));
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
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            propsGrid.Add(grid);

            foreach (var prop in props)
            {
                if (prop == null) continue;
                var c = prop;
                grid.Add(Swatch(
                    Thumb(c),
                    c == StampProp,
                    string.IsNullOrEmpty(c.displayName) ? c.name : c.displayName,
                    _ =>
                    {
                        StampProp = c;
                        brush.Clear();
                        paletteSel.Clear();
                        SetTool(SceneTool.Stamp);
                        RebuildProps();
                        RebuildPalette();
                    },
                    null, 48f));
            }

            if (StampProp != null)
                propsGrid.Add(Z.Row(
                    Z.Text(StampProp.displayName, ZuiText.Small, "The selected stamp."),
                    Z.Flexible(),
                    Z.Button("Edit prop…", "Opens the selected prop in the prop editor window.",
                        () => PropWindow.OpenFor(StampProp))));
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
            RebuildPalette();
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

        /// Sprite-backed swatch: renders the FULL sprite rect, transparent padding included — the palette
        /// grid must compose exactly like the level will render, and cropped thumbnails re-centre partial
        /// art (the "broken shelf clump" lesson). Browser-style cropping stays for single-object cards.
        static VisualElement Swatch(Sprite sprite, bool selected, string tooltipText, System.Action<bool> onClick,
            string badge = null, float size = 36f)
        {
            var swatch = Swatch((Texture2D)null, selected, tooltipText, onClick, badge, size);
            if (sprite != null)
            {
                swatch.style.backgroundImage = Background.FromSprite(sprite);
                swatch.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }
            return swatch;
        }

        /// The tile sprite a grid cell should show: first frame or first variant, full rect.
        static Sprite CellSprite(LevelTile t) =>
            t == null ? null : t.IsAnimated && t.animation.Count > 0 ? t.animation[0] : t.SpriteOfVariant(0);

        static VisualElement Swatch(Texture2D thumb, bool selected, string tooltipText, System.Action<bool> onClick,
            string badge = null, float size = 36f)
        {
            var swatch = new Button { tooltip = tooltipText };
            // The click must know whether Ctrl was held — that is what turns a click into "add to pattern".
            swatch.clickable.clickedWithEventInfo += e =>
                onClick(e is IPointerEvent pe ? pe.ctrlKey : e is IMouseEvent me && me.ctrlKey);
            swatch.style.width = size;
            swatch.style.height = size;
            swatch.style.marginRight = 1f;
            swatch.style.marginBottom = 1f;
            if (thumb != null)
            {
                swatch.style.backgroundImage = Background.FromTexture2D(thumb);
                swatch.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }
            if (selected)
            {
                swatch.style.borderTopWidth = swatch.style.borderBottomWidth =
                    swatch.style.borderLeftWidth = swatch.style.borderRightWidth = 2f;
                var c = new Color(1f, 0.75f, 0.2f);
                swatch.style.borderTopColor = swatch.style.borderBottomColor =
                    swatch.style.borderLeftColor = swatch.style.borderRightColor = c;
            }
            if (!string.IsNullOrEmpty(badge))
            {
                var b = new Label(badge) { pickingMode = PickingMode.Ignore };
                b.style.position = Position.Absolute;
                b.style.right = 1f;
                b.style.bottom = 1f;
                b.style.fontSize = 9f;
                b.style.color = Color.white;
                b.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
                swatch.Add(b);
            }
            return swatch;
        }
    }
}
