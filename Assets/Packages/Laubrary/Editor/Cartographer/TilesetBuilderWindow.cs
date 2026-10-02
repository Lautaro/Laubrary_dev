using System.Collections.Generic;
using System.IO;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace Laubrary.Cartographer.Editor
{
    /// TILESET BUILDER — curation by PLUCKING, never by slicing. The source sheet is only a picture: the
    /// builder reads its pixels straight from the file (which can live anywhere on disk, project member or
    /// not), lays a selection grid over it, and when YOU marquee cells and say what they mean — one tile
    /// with variants, an animated tile, one tile per cell — those cells' pixels are COPIED into a small
    /// per-tileset atlas. Only curated art ever becomes an asset: a 13,000-cell bought pack costs the
    /// project exactly the twenty tiles plucked from it. Cells already plucked are tinted (tracked by the
    /// sheet's content hash), so across a big pack you always see what is still uncurated.
    ///
    /// The shape is borrowed from Launimator's Laumination Builder, whose own history settled the design
    /// question: whole-sheet auto-grouping was tried and removed as a dead end; what survived is a human
    /// AIMING a marquee and the machine doing the mechanical half inside it.
    public class TilesetBuilderWindow : ZuiAssetWindow<Tileset>
    {
        // No menu item on purpose: reached from the Cartographer window's Palette box, the same way the
        // Prop Editor is reached from its Props box.
        public static void OpenFor(Tileset target)
        {
            var w = GetWindow<TilesetBuilderWindow>("Tileset Builder");
            if (target != null) w.SetAsset(target);
        }

        Tileset set => Current;

        protected override string TypeLabel => "Tileset";
        protected override string NewAssetName => "Tileset";
        protected override string DefaultFolder => "Assets/Cartographer/Tilesets";
        protected override Texture2D RenderThumbnail(Tileset item) => item != null ? item.RenderPreviewTexture() : null;
        protected override void InitializeNewAsset(Tileset item) => item.displayName = item.name;

        // The sheet's identity is its PATH ON DISK (plus a content hash for memory) — deliberately not an
        // asset reference, so bought packs can be curated straight from wherever they were unzipped.
        [SerializeField] string sheetPath;
        [SerializeField] Texture2D sheetAsset;   // set only when the path is inside this project — ObjectField display
        Texture2D sheet;                         // readable working copy built from the FILE: display + pixel source
        string sheetMd5;
        Color32[] sheetPx;
        [SerializeField] int tileSize = 16;
        [SerializeField] int spacingX;
        [SerializeField] int spacingY;
        [SerializeField] int originX;
        [SerializeField] int originY;
        [SerializeField] bool gridOscillate = true;
        [SerializeField] float gridOscSpeed = 1.2f;      // cycles per second
        [SerializeField] float gridOscAmplitude = 1f;    // 0 = static mid-grey, 1 = full black↔white swing
        [SerializeField] float gridBrightness = 0.75f;   // static grid: 0 = black, 1 = white
        [SerializeField] float gridAlpha = 0.8f;         // static grid opacity
        [SerializeField] bool overwriteOnDrop;           // grid editor: dropping onto occupied cells
        [SerializeField] float tilesetCellZoom = 40f;    // VIEW-only cell size of the grid editor
        [SerializeField] float tilesetGridBrightness = 0.75f;   // grid editor lattice: white ↔ black
        [SerializeField] float tilesetGridAlpha = 0.35f;        // grid editor lattice opacity
        [SerializeField] bool tilesetSeamless;                  // gapless view — tiles butt together, no lattice
        [SerializeField] bool tilesetSeamlessMarks = true;      // seamless: oscillating hover/selection frames
        [SerializeField] int libraryTab;                        // 0 = loose tiles, 1 = clumps
        [SerializeField] int clumpEditMode;                     // 0 = Select, 1 = Layers, 2 = Collision
        [SerializeField] int tileEditMode;                      // 0 = Select, 1 = Collision
        [SerializeField] bool showAnimated;                     // grid: every animated tile plays its frames
        [SerializeField] bool cycleRandoms;                     // grid: every Random tile cycles its variants
        [SerializeField] TileTag activeTag;                     // the tag Tags mode paints and tints with
        [SerializeField] float sheetZoom = 1f;           // sheet canvas: 1 = fit the pane, wheel-driven up to 16×
        Vector2 sheetPan;                                // view offset while zoomed; Layout clamps it and zeroes it at fit
        [SerializeField] string nextName = "Tile";
        [SerializeField] VariantPolicy nextPolicy = VariantPolicy.Random;
        [SerializeField] float animFps = 6f;

        readonly HashSet<Vector2Int> filled = new();   // cells with any visible pixel — the pluckable set
        readonly HashSet<Vector2Int> used = new();     // cells the target tileset already consumed
        readonly List<Vector2Int> selection = new();   // in reading/click order — order = variant/frame order
        int cols, rows;

        SheetStage stage;
        Label statusLine;
        VisualElement tilesStrip;
        Button btnAdd, btnRandom, btnAnimated;
        Image animPreview;
        Texture2D previewTex;                                    // reused canvas for the animation preview
        Vector2Int previewShown = new(int.MinValue, int.MinValue);
        readonly Dictionary<Object, Texture2D> tileThumbs = new();

        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // The path string survives domain reloads; the readable pixel copy does not — rebuild it.
            if (!string.IsNullOrEmpty(sheetPath)) LoadSheet();
        }

        protected override void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            base.OnDisable();
            LauAssetGridGUI.ClearCache(tileThumbs);
            if (sheet != null) DestroyImmediate(sheet);
            if (previewTex != null) DestroyImmediate(previewTex);
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            // Same retained-tree lesson as the Cartographer window: transitions kill unflagged textures.
            if (change != PlayModeStateChange.EnteredEditMode && change != PlayModeStateChange.EnteredPlayMode) return;
            LauAssetGridGUI.ClearCache(tileThumbs);
            RebuildTilesStrip();
        }

        protected override void OnAssetChanged()
        {
            selection.Clear();
            LauAssetGridGUI.ClearCache(tileThumbs);

            // A tileset that already has tiles knows its sheet — reopen it so the window starts mid-flow
            // instead of empty. Provenance first (works for sheets outside the project); the legacy
            // texture-of-the-first-variant heuristic only for pre-provenance tilesets, and never the
            // tileset's own atlas — that is a product of curation, not a source of it.
            if (string.IsNullOrEmpty(sheetPath) && set != null && set.tiles != null)
            {
                foreach (var t in set.tiles)
                    if (t != null && !string.IsNullOrEmpty(t.sourceSheetPath) && File.Exists(t.sourceSheetPath))
                    {
                        SetSheetPath(t.sourceSheetPath, rebuild: false);
                        return;
                    }
                foreach (var t in set.tiles)
                {
                    var s = t != null && t.variants != null && t.variants.Count > 0 ? t.variants[0] : null;
                    if (s == null || s.texture == null) continue;
                    string p = AssetDatabase.GetAssetPath(s.texture);
                    if (string.IsNullOrEmpty(p) || TilesetAtlas.IsAtlasOf(set, p)) continue;
                    SetSheetPath(Path.GetFullPath(p), rebuild: false);
                    return;
                }
            }

            RefreshUsed();
        }

        protected override void BuildAsset(VisualElement root, Tileset asset)
        {
            root.AddToClassList("lau-tool-shell");

            var left = new VisualElement();
            left.AddToClassList("lau-tileset__controls");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("lau-tool-shell__scroll");
            BuildControls(scroll.contentContainer);
            left.Add(scroll);

            stage = new SheetStage(this);

            // The standard tool shape: controls left, workspace right, divider draggable and remembered.
            root.Add(Z.Split("tileset-builder", 320f, left, stage));

            if (sheet == null && !string.IsNullOrEmpty(sheetPath)) LoadSheet();
            RefreshUsed();
            stage.Refresh();
            UpdateStatus();   // after LoadSheet — the status must describe the loaded sheet, not the pre-load void
        }

        void BuildControls(VisualElement root)
        {
            root.Add(Z.Box("Sheet", "The sheet being curated — a PICTURE this window reads pixels from, never an asset that needs slicing. It can live anywhere on disk; only the cells you pluck become project assets. A sheet seen before restores its own grid settings.",
                Z.Row(
                    Z.Field("Texture", "A sheet already in the project. For files elsewhere on disk, use Open sheet… — nothing is ever copied in either way.",
                        Z.Object<Texture2D>(sheetAsset, "Project sheet to curate.", v =>
                        {
                            if (v == null) { ClearSheet(); return; }
                            SetSheetFromAsset(v);
                        }, 170f)),
                    Z.Flexible(),
                    recentBtn = Z.Button("Recent…", "Sheets used before, wherever they live — pick one and its saved grid settings load with it.",
                        () => ShowRecentSheets()),
                    Z.Button("Open sheet…", "Pick a sheet file from ANYWHERE on disk. It is read in place — never copied into the project — and remembered by content, so its settings survive renames and moves.",
                        OpenSheet)),
                Z.Row(
                    Z.Field("Tile size", "Grid cell size in pixels — the size of a plucked tile.",
                        Z.Int(tileSize, "Grid cell size in pixels.", v => { tileSize = Mathf.Max(4, v); ReGrid(); }, 46f)),
                    Z.Flexible()),
                Z.Row(
                    Z.Field("Spacing", "Pixel gap BETWEEN tiles, horizontal then vertical — for sheets with padding between cells.",
                        Z.Row(
                            Z.Int(spacingX, "Horizontal gap between tiles, in pixels.", v => { spacingX = Mathf.Max(0, v); ReGrid(); }, 40f),
                            Z.Int(spacingY, "Vertical gap between tiles, in pixels.", v => { spacingY = Mathf.Max(0, v); ReGrid(); }, 40f))),
                    Z.HSpace(),
                    Z.Field("Origin", "Where the grid STARTS, in pixels from the sheet's top-left corner — for sheets with a margin.",
                        Z.Row(
                            Z.Int(originX, "Grid start X, pixels from the left edge.", v => { originX = Mathf.Max(0, v); ReGrid(); }, 40f),
                            Z.Int(originY, "Grid start Y, pixels from the top edge.", v => { originY = Mathf.Max(0, v); ReGrid(); }, 40f)))),
                OscillationRow()));

            statusLine = (Label)Z.Text("", ZuiText.Subtle, "Sheet and selection state.");
            // Stable-layout rule: the status is ONE reserved line; long text truncates rather than wraps,
            // because a wrapping status would shove the whole column below it.
            statusLine.AddToClassList("lau-tileset__status");
            root.Add(statusLine);

            root.Add(Z.Box("Make tiles", "Turn the selected sheet cells into tiles. Right-drag a marquee on the sheet to select; selection order is variant/frame order. Selected cells can also be DRAGGED straight onto the tileset grid.",
                Z.Field("Name", "Name for the next tile this window mints.",
                    nameInput = Z.TextInput(nextName, "Name for the next minted tile.", v => nextName = v, 150f)),
                Z.Row(
                    btnAdd = Z.Button("Add", "", () => Mint(MintMode.PerCell)),
                    btnRandom = Z.Button("Random", "", () => Mint(MintMode.Variants)),
                    btnAnimated = Z.Button("Animated", "", () => Mint(MintMode.Animation))),
                Z.Foldout("Settings", "Policy for Random tiles and frame rate for Animated ones.", false,
                    Z.Field("Policy", "How painting picks among a Random tile's variants.",
                        Z.MiniRadio((int)nextPolicy, new[] { "Random", "RoundRobin", "NoRepeat", "Weighted" },
                            "Variant policy for the next Random tile.", i => nextPolicy = (VariantPolicy)i, wrap: true)),
                    Z.MicroSlider("FPS", animFps, 0.5f, 24f, "Frame rate for the next Animated tile — and for the preview below.",
                        v => animFps = v, 150f))));

            animPreview = new Image { scaleMode = ScaleMode.ScaleToFit };
            animPreview.AddToClassList("lau-tileset__animation-preview");
            animPreview.tooltip = "The selected sheet cells, cycling at the Settings FPS — how this group would look as an animated tile.";
            root.Add(Z.Box("Animation preview", "Plays the current sheet selection as frames, at the Settings FPS.",
                animPreview));
            animPreview.schedule.Execute(() =>
            {
                if (selection.Count == 0 || sheet == null) { animPreview.image = null; return; }
                int frame = (int)(EditorApplication.timeSinceStartup * animFps) % selection.Count;
                var cell = selection[frame];
                // One reused texture, refilled straight from the sheet's pixels when the frame changes —
                // the preview needs no sprites, because nothing here is sliced anymore.
                if (previewTex == null || previewTex.width != tileSize)
                {
                    if (previewTex != null) DestroyImmediate(previewTex);
                    previewTex = new Texture2D(tileSize, tileSize, TextureFormat.RGBA32, false)
                        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                    previewShown = new Vector2Int(int.MinValue, int.MinValue);
                }
                if (cell != previewShown)
                {
                    previewTex.SetPixels32(CellPixels(cell));
                    previewTex.Apply();
                    previewShown = cell;
                }
                animPreview.image = previewTex;
            }).Every(50);

            tilesStrip = new VisualElement();
            var libraryBox = Z.Box("Tileset", "The tileset AS A GRID — positions are the patterns the Cartographer palette shows. Tiles and Clumps are its two tabs. Click / Ctrl-click / marquee to select; drag a selection to move it; Ctrl-drag duplicates; right-click for actions.",
                tilesStrip);

            // View dials live ON the header line (the user's own mockup, 2026-08-01): they govern how the
            // grids display, so they cost no body row and stay put across tab switches.
            libraryBox.AddHeaderContent(Z.MicroSlider("Zoom", tilesetCellZoom, 20f, 72f,
                "Display size of the grid's cells — a view convenience only, tile data is untouched.",
                v => { tilesetCellZoom = v; tilesGrid?.RebuildCells(); clumpsGrid?.RebuildCells(); }, 90f));
            libraryBox.AddHeaderContent(Z.MicroSlider("Lines", tilesetGridBrightness, 0f, 1f,
                "Grid line colour, black to white — same dial as the sheet canvas.",
                v => { tilesetGridBrightness = v; tilesGrid?.RepaintOverlay(); clumpsGrid?.RepaintOverlay(); }, 70f));
            libraryBox.AddHeaderContent(Z.MicroSlider("Alpha", tilesetGridAlpha, 0f, 1f,
                "Grid line opacity.",
                v => { tilesetGridAlpha = v; tilesGrid?.RepaintOverlay(); clumpsGrid?.RepaintOverlay(); }, 70f));
            var marksToggle = Z.ToggleButton("Osc frame",
                "In seamless view: pulse an oscillating frame around the hovered and selected cells, so " +
                "editing stays possible without the grid. Off = a pure, uninterrupted preview.",
                tilesetSeamlessMarks, v => { tilesetSeamlessMarks = v; tilesGrid?.RepaintOverlay(); clumpsGrid?.RepaintOverlay(); });
            libraryBox.AddHeaderContent(Z.ToggleButton("Seamless",
                "Remove the gaps and grid entirely — adjacent tiles butt together and preview exactly as " +
                "they would paint into a level.",
                tilesetSeamless, v =>
                {
                    tilesetSeamless = v;
                    marksToggle.SetEnabled(v);
                    tilesGrid?.RebuildCells();
                    tilesGrid?.RepaintOverlay();
                    clumpsGrid?.RebuildCells();
                    clumpsGrid?.RepaintOverlay();
                }));
            marksToggle.SetEnabled(tilesetSeamless);
            libraryBox.AddHeaderContent(marksToggle);

            root.Add(libraryBox);
            RebuildTilesStrip();
            UpdateStatus();
        }

        VisualElement gridToolbar;
        TilesetGrid tilesGrid;
        ClumpsGrid clumpsGrid;

        /// The live sheet-drag payload, readable by whichever canvas is about to accept the drop.
        internal List<(Vector2Int off, Vector2Int cell)> SheetDragCells => sheetDragCells;

        /// The tileset as an editable 2D GRID: positions ARE the pattern data the Cartographer palette
        /// mirrors. Selection + toolbar replace per-tile buttons; nothing here deletes assets, only cells.
        void RebuildTilesStrip()
        {
            if (tilesStrip == null) return;
            tilesStrip.Clear();
            if (set == null) return;

            gridToolbar = new VisualElement();
            gridToolbar.AddToClassList("lau-tileset__toolbar");
            tilesStrip.Add(gridToolbar);

            // One row for both switches (the mockup layout): WHAT you look at (Tiles|Clumps) and HOW a
            // click acts (Select|…|Collision), side by side. View dials live on the box header above.
            var switchRow = new VisualElement();
            switchRow.AddToClassList("lau-tileset__switch-row");
            switchRow.Add(Z.MiniRadio(libraryTab, new[] { "Tiles", "Clumps" },
                "Tiles: loose tiles, one per cell, arranged into paintable patterns. Clumps: locked " +
                "multi-tile objects (urns, doors, wall columns) placed and painted as one thing.",
                i => { libraryTab = i; RebuildTilesStrip(); }));
            switchRow.Add(Z.HSpace());
            tilesStrip.Add(switchRow);

            // Wide grids (high zoom × many columns) scroll horizontally inside their own box instead of
            // clipping — vertical stays with the pane's own scroller. Middle-drag moves both at once.
            var gridScroll = new ScrollView(ScrollViewMode.Horizontal);
            gridScroll.AddToClassList("lau-tool-shell__chrome");

            if (libraryTab == 0)
            {
                clumpsGrid = null;
                // Tiles carry their own collision (auto-guessed at pluck); layer is a paint-time choice
                // and deliberately NOT tile data — so this tab has no Layers mode.
                switchRow.Add(Z.MiniRadio(tileEditMode, new[] { "Select", "Collision", "Tags" },
                    "Select: normal selection, moving and dragging. Collision: tiles tint by blocking — " +
                    "red = full square, yellow = sprite outline, no tint = pass-through; click a tile to cycle. " +
                    "Tags: tiles carrying the picked tag tint in its colour; click a tile to toggle the tag.",
                    i => { tileEditMode = i; RebuildTilesStrip(); }));
                tilesStrip.Add(gridScroll);
                tilesGrid = new TilesetGrid(this);
                gridScroll.Add(tilesGrid);
                RefreshGridToolbar();
                tilesGrid.RebuildCells();
            }
            else
            {
                tilesGrid = null;
                // Edit modes: Select handles whole clumps; Layers and Collision PAINT per-cell properties,
                // with coloured translucent overlays so a clump's routing reads at a glance.
                switchRow.Add(Z.MiniRadio(clumpEditMode, new[] { "Select", "Layers", "Collision", "Tags" },
                    "Select: click and drag whole clumps. Layers: cells tint by target layer — blue = the " +
                    "stamped layer, orange = one layer in front (overhangs); click a cell to toggle. " +
                    "Collision: cells tint by blocking — red = full square, yellow = sprite outline, " +
                    "no tint = pass-through; click a cell to cycle. Tags: cells carrying the picked tag " +
                    "tint in its colour; click a cell to toggle the tag on its tile.",
                    i => { clumpEditMode = i; RebuildTilesStrip(); }));
                tilesStrip.Add(gridScroll);
                clumpsGrid = new ClumpsGrid(this);
                gridScroll.Add(clumpsGrid);
                RefreshGridToolbar();
                clumpsGrid.RebuildCells();
            }

            // Tag controls: always present on the row (reserved space), enabled only in a Tags mode.
            bool tagsMode = libraryTab == 0 ? tileEditMode == 2 : clumpEditMode == 3;
            tagPickBtn = Z.Button(activeTag != null ? activeTag.name : "Pick tag…",
                "The Tile Tag that Tags mode paints with — cells carrying it tint in the tag's own colour.",
                ShowTagPicker);
            tagPickBtn.SetEnabled(tagsMode);
            switchRow.Add(tagPickBtn);
            tagManageBtn = Z.Button("Tags…",
                "Create, rename and delete Tile Tag assets — the open-ended gameplay labels tiles carry.",
                ShowTagManager);
            tagManageBtn.SetEnabled(tagsMode);
            switchRow.Add(tagManageBtn);
        }

        // ── Tile Tags: picker + CRUD ───────────────────────────────────────────
        const string TagsFolder = "Assets/Cartographer/Tags";

        void ShowTagPicker()
        {
            var menu = Z.Menu(tagPickBtn);
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:TileTag"))
            {
                var tag = AssetDatabase.LoadAssetAtPath<TileTag>(AssetDatabase.GUIDToAssetPath(guid));
                if (tag == null) continue;
                n++;
                var tg = tag;
                menu.Item(tag.name,
                    string.IsNullOrEmpty(tag.description) ? "Paint with this tag." : tag.description,
                    () => { activeTag = tg; RebuildTilesStrip(); });
            }
            if (n == 0)
                menu.Item("(no Tile Tags yet — use Tags… to create one)",
                    "Tags are tiny named assets; gameplay decides what they mean.", () => { });
            menu.Show();
        }

        /// The CRUD card: every Tile Tag as a rename-in-place row with a delete, plus New. Renames commit
        /// on Enter/blur (never per keystroke — each rename is an asset operation).
        void ShowTagManager()
        {
            var menu = Z.Menu(tagManageBtn);
            menu.Custom((body, close) =>
            {
                foreach (var guid in AssetDatabase.FindAssets("t:TileTag"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var tag = AssetDatabase.LoadAssetAtPath<TileTag>(path);
                    if (tag == null) continue;
                    var tg = tag;
                    string p2 = path;
                    var nameField = Z.TextInput(tag.name,
                        "Rename this tag — the asset renames with it. Commit with Enter.",
                        v =>
                        {
                            if (string.IsNullOrWhiteSpace(v) || v.Trim() == tg.name) return;
                            AssetDatabase.RenameAsset(p2, v.Trim());
                            RebuildTilesStrip();
                        }, 130f);
                    nameField.isDelayed = true;
                    body.Add(Z.Row(
                        nameField,
                        Z.Button("×", "Delete this tag asset. Tiles that carried it simply lose the label.", () =>
                        {
                            if (!EditorUtility.DisplayDialog("Delete tag?",
                                    $"Delete Tile Tag '{tg.name}'? Tiles that carry it just lose the label.",
                                    "Delete", "Cancel")) return;
                            if (activeTag == tg) activeTag = null;
                            AssetDatabase.DeleteAsset(p2);
                            close();
                            RebuildTilesStrip();
                        })));
                }
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
                    activeTag = tag;
                    close();
                    RebuildTilesStrip();
                    ShowTagManager();   // reopen with the new row ready to rename
                }));
            });
            menu.Show();
        }

        /// The contextual toolbar. STABLE-LAYOUT RULE: every control exists at all times in one non-wrapping
        /// fixed-height row; what varies is VISIBILITY, never geometry — the workspace below must never
        /// jump because a selection appeared. (Hidden keeps its space; that is the whole point.)
        internal void RefreshGridToolbar()
        {
            if (gridToolbar == null || set == null) return;
            gridToolbar.Clear();
            gridToolbar.AddToClassList("lau-tileset__toolbar--contextual");

            gridToolbar.Add(Z.Field("Grid", "The tileset grid's width and height, in cells. Height grows on its own when tiles are placed lower.",
                Z.Row(
                    Z.Int(set.paletteColumns, "Grid width in cells. Changing it re-reads every position.", v =>
                    {
                        Undo.RecordObject(set, "Tileset grid width");
                        set.paletteColumns = Mathf.Max(1, v);
                        EditorUtility.SetDirty(set);
                        RebuildTilesStrip();
                    }, 36f),
                    Z.Int(set.paletteRows, "Grid height in cells.", v =>
                    {
                        Undo.RecordObject(set, "Tileset grid height");
                        set.paletteRows = Mathf.Max(1, v);
                        EditorUtility.SetDirty(set);
                        RebuildTilesStrip();
                    }, 36f))));

            gridToolbar.Add(Z.ToggleButton("Overwrite", "When ON, dropping or pasting tiles replaces whatever the target cells hold. " +
                "When OFF, a drop only lands if every target cell is empty (hold Alt to PUSH occupants aside instead).",
                overwriteOnDrop, v => overwriteOnDrop = v));

            gridToolbar.Add(Z.ToggleButton("Show animated",
                "Play every animated tile's frames right in the grid, so the tileset previews alive. " +
                "Off: animated tiles hold their first frame; hovering one still previews it.",
                showAnimated, v => { showAnimated = v; tilesGrid?.RebuildCells(); }));

            gridToolbar.Add(Z.ToggleButton("Cycle randoms",
                "Cycle every Random tile through its variants in the grid, so a group reads as a group. " +
                "Off: each shows its first variant; hovering one still previews it.",
                cycleRandoms, v => { cycleRandoms = v; tilesGrid?.RebuildCells(); }));

            int selCount = tilesGrid != null ? tilesGrid.SelectionCount : 0;

            // The action buttons live in the RIGHT-CLICK card, not here — chrome stays out of the window.
            var cancel = ToolbarButton("x", "Cancel paste", "Stop pasting.", () => tilesGrid.CancelPaste());
            SetShown(cancel, tilesGrid != null && tilesGrid.Pasting);
            gridToolbar.Add(cancel);

            // Last in the row on purpose: its text length varies, and nothing sits after it to be moved.
            var count = (Label)Z.Text(selCount > 0 ? $"{selCount} selected · right-click for actions" : "", ZuiText.Small,
                "Cells currently selected in the grid. Copy/Delete/Paste live on the right-click menu.");
            gridToolbar.Add(count);
        }

        /// Visibility, not display: a hidden control KEEPS its space, so toggling it cannot move anything.
        static void SetShown(VisualElement e, bool shown) =>
            e.style.visibility = shown ? Visibility.Visible : Visibility.Hidden;

        /// Checkerboard styling shared by the sheet canvas and the grid editor: transparency must read as
        /// "nothing here" on BOTH sides of the pluck, or black art and holes stay indistinguishable.
        /// Screen-fixed 16px squares (image-editor convention), 2×2 texture tiled by the GPU.
        internal static void StyleAsChecker(VisualElement e, ref Texture2D tex)
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

        /// An icon button that degrades to its label when the icon name doesn't resolve.
        static VisualElement ToolbarButton(string icon, string label, string tooltip, System.Action onClick)
        {
            var b = Z.Button(label, tooltip, onClick);
            var ic = Z.Icon(icon, 14f);
            if (ic != null)
            {
                b.text = "";
                ic.AddToClassList("lau-tileset__action-icon");
                b.Add(ic);
                b.AddToClassList("lau-tileset__icon-action");
            }
            return b;
        }

        void UpdateStatus()
        {
            if (statusLine == null) return;
            // The canvas fit-scales every sheet, so the eye cannot tell a 256px strip from a 2496px poster
            // — the status line must say what the eye cannot.
            statusLine.text = sheet == null
                ? string.IsNullOrEmpty(sheetPath)
                    ? "Step 1: open a sheet above, or drag one onto the canvas."
                    : $"Sheet file not found: {sheetPath}"
                : $"{Path.GetFileName(sheetPath)} · {sheet.width}×{sheet.height}px · {cols}×{rows} cells (shown at {(int)((stage != null ? stage.DisplayScale : 1f) * 100f)}%) · " +
                  $"{filled.Count} art cells · {used.Count} plucked into '{(set != null ? set.displayName : "?")}' · {selection.Count} selected";

            // The buttons themselves say why they are waiting — a dead click teaches nothing. All three are
            // ALWAYS present (stable-layout rule); availability is enabledness.
            bool any = sheet != null && selection.Count > 0;
            bool several = sheet != null && selection.Count > 1;
            string whyNone = sheet == null ? "Open a sheet first."
                : "Select cells on the sheet first — right-drag a marquee.";
            SetMint(btnAdd, any, "A plain tile for each selected cell (one cell = one tile, several = Name_1, Name_2, …).", whyNone);
            SetMint(btnRandom, several, "ONE tile whose variants are the selected cells, picked by the Settings policy.",
                any ? "Needs more than one selected cell — variants are a group." : whyNone);
            SetMint(btnAnimated, several, "ONE tile cycling the selected cells as frames at the Settings FPS.",
                any ? "Needs more than one selected cell — an animation is a sequence." : whyNone);
        }

        static void SetMint(Button b, bool ready, string does, string why)
        {
            if (b == null) return;
            b.SetEnabled(ready);
            b.tooltip = ready ? does : does + " Disabled: " + why;
        }

        // ── grid geometry: size + spacing + origin, one source of truth ────────
        /// Pixel X of a column's left edge; origin measured from the sheet's top-left, the way humans read sheets.
        int CellPxX(int c) => originX + c * (tileSize + spacingX);

        /// Pixel Y of a row's top edge, measured from the TOP of the sheet.
        int CellPxTop(int r) => originY + r * (tileSize + spacingY);

        void GridDims(int texW, int texH, out int c, out int r)
        {
            int stepX = tileSize + spacingX, stepY = tileSize + spacingY;
            c = Mathf.Max(1, (texW - originX + spacingX) / stepX);
            r = Mathf.Max(1, (texH - originY + spacingY) / stepY);
        }

        Button recentBtn;
        TextField nameInput;
        Button tagPickBtn, tagManageBtn;

        /// One row, four dials, two visible: Speed/Amount while oscillating, Colour/Alpha when static.
        /// They swap IN PLACE (visibility, same slots), so toggling never reflows the pane.
        VisualElement OscillationRow()
        {
            var speed = Z.MicroSlider("Speed", gridOscSpeed, 0.2f, 4f, "Grid oscillation speed, cycles per second.",
                v => gridOscSpeed = v, 110f);
            var amount = Z.MicroSlider("Amount", gridOscAmplitude, 0f, 1f, "How far the oscillation swings: 0 stays mid-grey, 1 swings fully black to white.",
                v => gridOscAmplitude = v, 110f);
            var colour = Z.MicroSlider("Colour", gridBrightness, 0f, 1f, "Static grid brightness: 0 is black, 1 is white.",
                v => { gridBrightness = v; stage?.Refresh(); }, 110f);
            var alpha = Z.MicroSlider("Alpha", gridAlpha, 0f, 1f, "Static grid opacity.",
                v => { gridAlpha = v; stage?.Refresh(); }, 110f);

            void Sync(bool osc)
            {
                SetShown(speed, osc);
                SetShown(amount, osc);
                SetShown(colour, !osc);
                SetShown(alpha, !osc);
            }
            Sync(gridOscillate);

            // Oscillating pair and static pair occupy the SAME slots — overlay them per slot.
            var slotA = new VisualElement();
            slotA.Add(speed);
            colour.AddToClassList("lau-authoring__overlay-origin");
            slotA.Add(colour);
            var slotB = new VisualElement();
            slotB.Add(amount);
            alpha.AddToClassList("lau-authoring__overlay-origin");
            slotB.Add(alpha);

            return Z.Row(
                Z.ToggleButton("Oscillate grid", "Pulse the canvas grid between black and white so it reads on any art. Off = a static grid with its own colour and alpha.",
                    gridOscillate, v => { gridOscillate = v; Sync(v); stage?.Refresh(); }),
                slotA,
                slotB);
        }

        /// Every sheet the builder has opened, wherever it lives — pick one and its settings come back.
        void ShowRecentSheets()
        {
            var menu = Z.Menu(recentBtn);
            int n = 0;
            var listed = new HashSet<string>();
            foreach (var recent in SheetProvenance.Recents())
            {
                if (!File.Exists(recent.path) || !listed.Add(Path.GetFullPath(recent.path).ToLowerInvariant())) continue;
                string p = recent.path;
                n++;
                menu.Item(Path.GetFileName(p), "Open this sheet with its remembered grid settings.",
                    () => SetSheetPath(p));
            }
            // Legacy: project sheets that earned a provenance sidecar before the recents list existed.
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (SheetProvenance.Load(assetPath) == null) continue;
                string full = Path.GetFullPath(assetPath);
                if (!listed.Add(full.ToLowerInvariant())) continue;
                n++;
                menu.Item(Path.GetFileNameWithoutExtension(assetPath), "Open this sheet with its remembered grid settings.",
                    () => SetSheetPath(full));
            }
            if (n == 0) menu.Item("(no sheets remembered yet)", "Sheets are remembered as soon as they are opened here.", () => { });
            menu.Show();
        }

        // ── sheet memory: settings follow the CONTENT HASH, so they survive renames and moves ──
        void ApplyRememberedSettings()
        {
            var src = SheetProvenance.LoadByMd5(sheetMd5);
            if (src == null && sheetAsset != null) src = SheetProvenance.Load(sheetAsset);   // pre-pluck sidecars
            if (src == null || !src.hasTilesetSettings) return;
            tileSize = src.tileSize;
            spacingX = src.spacingX;
            spacingY = src.spacingY;
            originX = src.originX;
            originY = src.originY;
        }

        void SaveSheetMemory()
        {
            if (string.IsNullOrEmpty(sheetMd5)) return;
            var src = SheetProvenance.LoadByMd5(sheetMd5) ?? new SheetSource
            {
                origin = sheetPath,
                originalFileName = Path.GetFileName(sheetPath),
                md5 = sheetMd5,
                importedUtc = System.DateTime.UtcNow.ToString("o"),
            };
            src.hasTilesetSettings = true;
            src.tileSize = tileSize;
            src.spacingX = spacingX;
            src.spacingY = spacingY;
            src.originX = originX;
            src.originY = originY;
            SheetProvenance.SaveByMd5(sheetMd5, src);
            // Project sheets keep their sidecar current too — other tools read those.
            if (sheetAsset != null) SheetProvenance.Save(AssetDatabase.GetAssetPath(sheetAsset), src);
        }

        /// Point the builder at a sheet file. THE single assignment path: identity, memory, pixels, UI.
        internal void SetSheetPath(string absolutePath, bool rebuild = true)
        {
            sheetPath = absolutePath;
            sheetAsset = null;
            if (!string.IsNullOrEmpty(absolutePath))
            {
                // Resolve project membership so the ObjectField can show the asset — purely cosmetic.
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
                string full = Path.GetFullPath(absolutePath).Replace('\\', '/');
                if (full.StartsWith(projectRoot + "/", System.StringComparison.OrdinalIgnoreCase))
                    sheetAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(full.Substring(projectRoot.Length + 1));
            }

            sheetMd5 = SheetProvenance.Md5OfFile(sheetPath);
            ApplyRememberedSettings();   // before LoadSheet — the grid scan needs the right cell size
            LoadSheet();
            if (sheet != null)
            {
                SheetProvenance.PushRecent(sheetPath, sheetMd5);
                SaveSheetMemory();
            }
            selection.Clear();
            RefreshUsed();
            if (rebuild) Rebuild();
            else { stage?.Refresh(); UpdateStatus(); }
        }

        internal void SetSheetFromAsset(Texture2D tex)
        {
            string p = AssetDatabase.GetAssetPath(tex);
            if (!string.IsNullOrEmpty(p)) SetSheetPath(Path.GetFullPath(p));
        }

        void ClearSheet()
        {
            sheetPath = null;
            sheetAsset = null;
            sheetMd5 = null;
            if (sheet != null) DestroyImmediate(sheet);
            sheet = null;
            sheetPx = null;
            filled.Clear();
            selection.Clear();
            cols = rows = 0;
            RefreshUsed();
            Rebuild();
        }

        /// Pick a sheet file from anywhere on disk — read in place, never copied into the project.
        void OpenSheet()
        {
            string picked = EditorUtility.OpenFilePanel("Open sprite sheet", "", "png");
            if (!string.IsNullOrEmpty(picked)) SetSheetPath(picked);
        }

        /// Grid settings changed: rescan which cells hold art and remember the settings for this sheet.
        void ReGrid()
        {
            selection.Clear();
            if (sheet != null)
            {
                RefreshFilled();
                SaveSheetMemory();
            }
            RefreshUsed();
            stage?.Refresh();
            UpdateStatus();
        }

        // ── the sheet as pixels (nothing here is ever sliced) ─────────────────
        /// Build the readable working copy from the FILE bytes. LoadImage ignores import settings and
        /// works for files outside the project — the two properties this whole model stands on.
        void LoadSheet()
        {
            if (sheet != null) DestroyImmediate(sheet);
            sheet = null;
            sheetPx = null;
            filled.Clear();
            cols = rows = 0;
            if (string.IsNullOrEmpty(sheetPath) || !File.Exists(sheetPath)) return;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            if (!tex.LoadImage(File.ReadAllBytes(sheetPath))) { DestroyImmediate(tex); return; }
            sheet = tex;
            sheetPx = tex.GetPixels32();
            RefreshFilled();
        }

        /// Rescan which grid cells contain any visible pixel — the cells worth selecting and plucking.
        void RefreshFilled()
        {
            filled.Clear();
            cols = rows = 0;
            if (sheet == null || sheetPx == null) return;
            GridDims(sheet.width, sheet.height, out cols, out rows);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int px0 = CellPxX(c), pyTop = CellPxTop(r);
                    if (px0 + tileSize > sheet.width || pyTop + tileSize > sheet.height) continue;
                    int py = sheet.height - (pyTop + tileSize);   // pixel rows are bottom-up
                    bool any = false;
                    for (int y = 0; y < tileSize && !any; y++)
                        for (int x = 0; x < tileSize && !any; x++)
                            if (sheetPx[(py + y) * sheet.width + px0 + x].a > 0) any = true;
                    if (any) filled.Add(new Vector2Int(c, r));
                }
        }

        /// One cell's pixels, bottom-up as SetPixels32 wants them — the block the atlas receives.
        Color32[] CellPixels(Vector2Int cell)
        {
            var block = new Color32[tileSize * tileSize];
            if (sheet == null || sheetPx == null) return block;
            int px0 = CellPxX(cell.x);
            int py = sheet.height - (CellPxTop(cell.y) + tileSize);
            if (px0 < 0 || py < 0 || px0 + tileSize > sheet.width || py + tileSize > sheet.height) return block;
            for (int y = 0; y < tileSize; y++)
                System.Array.Copy(sheetPx, (py + y) * sheet.width + px0, block, y * tileSize, tileSize);
            return block;
        }

        /// Which cells of THIS sheet the target tileset already consumed — the "what is left to curate" tint.
        /// Plucked tiles are matched by the sheet's CONTENT HASH; tiles from the pre-pluck era (which still
        /// reference sub-sprites of a project sheet) by texture, so old tilesets keep their tint.
        void RefreshUsed()
        {
            used.Clear();
            if (set == null || sheet == null) return;

            void MarkTile(LevelTile tile)
            {
                if (tile == null) return;
                if (!string.IsNullOrEmpty(tile.sourceSheetMd5) && tile.sourceSheetMd5 == sheetMd5 &&
                    tile.sourceCells != null)
                    foreach (var c in tile.sourceCells) used.Add(c);

                if (sheetAsset == null) return;
                void Mark(Sprite s)
                {
                    if (s == null || s.texture != sheetAsset) return;
                    var r = s.rect;
                    int stepX = tileSize + spacingX, stepY = tileSize + spacingY;
                    int dx = (int)r.x - originX;
                    int topPx = sheetAsset.height - (int)(r.y + r.height) - originY;
                    if (dx < 0 || topPx < 0 || dx % stepX != 0 || topPx % stepY != 0) return;
                    used.Add(new Vector2Int(dx / stepX, topPx / stepY));
                }
                if (tile.variants != null) foreach (var s in tile.variants) Mark(s);
                if (tile.animation != null) foreach (var s in tile.animation) Mark(s);
            }

            if (set.tiles != null) foreach (var tile in set.tiles) MarkTile(tile);
            // Clump member tiles consume sheet cells exactly like loose tiles — the tint must know.
            if (set.clumps != null)
                foreach (var clump in set.clumps)
                    if (clump?.cells != null)
                        foreach (var pc in clump.cells) MarkTile(pc.tile);
        }

        // ── minting (the curated half) ─────────────────────────────────────────
        enum MintMode { Variants, Animation, PerCell }

        void Mint(MintMode mode)
        {
            if (set == null || sheet == null) return;
            var cells = new List<Vector2Int>();
            foreach (var cell in selection)
                if (filled.Contains(cell)) cells.Add(cell);
            if (cells.Count == 0) { statusLine.text = "Nothing selected — marquee cells on the sheet first."; return; }

            // The PLUCK: the selected cells' pixels leave the sheet and enter the tileset's atlas — one
            // file write, one import, however many cells.
            var blocks = new List<Color32[]>();
            foreach (var cell in cells) blocks.Add(CellPixels(cell));
            var picked = TilesetAtlas.AddCells(set, tileSize, blocks, nextName);
            if (picked == null) { statusLine.text = "Could not write the tileset's atlas — is the tileset saved?"; return; }

            string folder = TilesFolder();
            var minted = new List<LevelTile>();

            if (mode == MintMode.PerCell)
            {
                for (int i = 0; i < picked.Length; i++)
                    minted.Add(CreateTile(picked.Length == 1 ? nextName : $"{nextName}_{i + 1}",
                        new List<Sprite> { picked[i] }, null, folder, new List<Vector2Int> { cells[i] }, blocks[i]));
            }
            else if (mode == MintMode.Animation)
            {
                minted.Add(CreateTile(nextName, new List<Sprite> { picked[0] }, new List<Sprite>(picked), folder, cells, blocks[0]));
            }
            else
            {
                minted.Add(CreateTile(nextName, new List<Sprite>(picked), null, folder, cells, blocks[0]));
            }

            Undo.RecordObject(set, "Add tiles to tileset");
            foreach (var t in minted) set.tiles.Add(t);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            selection.Clear();
            BumpName();
            RefreshUsed();
            RebuildTilesStrip();
            stage?.Refresh();
            UpdateStatus();
            statusLine.text = $"Minted {minted.Count} tile(s) into '{set.displayName}'. " + statusLine.text;
        }

        LevelTile CreateTile(string tileName, List<Sprite> variants, List<Sprite> animation, string folder,
            List<Vector2Int> srcCells, Color32[] coverage = null)
        {
            var t = ScriptableObject.CreateInstance<LevelTile>();
            t.displayName = tileName;
            t.variantPolicy = nextPolicy;
            // Collision default straight from the art: a mostly-full cell blocks as a square, a sliver
            // (wall caps, overhangs) passes through, anything between follows its own outline. A guess,
            // not a law — the clump editor's Collision mode overrides per cell.
            if (coverage != null && coverage.Length > 0)
            {
                int solid = 0;
                for (int i = 0; i < coverage.Length; i++) if (coverage[i].a > 0) solid++;
                float cov = solid / (float)coverage.Length;
                t.colliderShape = cov >= 0.9f ? Tile.ColliderType.Grid
                    : cov <= 0.25f ? Tile.ColliderType.None
                    : Tile.ColliderType.Sprite;
            }
            t.variants.AddRange(variants);
            if (animation != null) { t.animation.AddRange(animation); t.animationFps = animFps; }
            // Provenance: the used-cells tint and "reopen the sheet this set came from" both hang off this.
            t.sourceSheetMd5 = sheetMd5;
            t.sourceSheetPath = sheetPath;
            if (srcCells != null) t.sourceCells.AddRange(srcCells);
            AssetDatabase.CreateAsset(t, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{tileName}.asset"));
            return t;
        }

        /// Tiles land in a folder beside their tileset, named after it.
        string TilesFolder()
        {
            string setPath = AssetDatabase.GetAssetPath(set);
            string dir = Path.GetDirectoryName(setPath)?.Replace('\\', '/');
            string folder = $"{dir}/{set.name} Tiles";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(dir, $"{set.name} Tiles");
            return folder;
        }

        void BumpName()
        {
            int i = nextName.Length;
            while (i > 0 && char.IsDigit(nextName[i - 1])) i--;
            if (i < nextName.Length && int.TryParse(nextName.Substring(i), out int n))
                nextName = nextName.Substring(0, i) + (n + 1);
            // In-place, NOT Rebuild(): rebuilding the whole window after every mint threw away the sheet
            // canvas (and with it the user's zoom/pan position) just to refresh one text field.
            nameInput?.SetValueWithoutNotify(nextName);
        }

        // ── sheet-selection → tileset-grid drag ────────────────────────────────
        internal const string SheetDragKey = "laubrary.sheet.cells";
        List<(Vector2Int off, Vector2Int cell)> sheetDragCells;

        /// Start an editor drag carrying the sheet selection (offsets normalised, arrangement preserved).
        internal void BeginSheetDrag()
        {
            if (selection.Count == 0) return;
            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var c in selection) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); }
            sheetDragCells = new List<(Vector2Int, Vector2Int)>();
            foreach (var c in selection)
                if (filled.Contains(c))
                    sheetDragCells.Add((new Vector2Int(c.x - minX, c.y - minY), c));
            if (sheetDragCells.Count == 0) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[0];
            DragAndDrop.SetGenericData(SheetDragKey, this);
            DragAndDrop.StartDrag($"{sheetDragCells.Count} tile(s) from sheet");
        }

        /// Dropping the dragged sheet cells on the grid PLUCKS them (one plain tile per cell) and places the
        /// arrangement at the drop cell — same Overwrite/Alt-push rules as any placement. Fully atomic: a
        /// blocked drop deletes the just-minted assets AND their just-plucked atlas cells again.
        internal void MintDropFromSheet(Vector2Int anchor, bool push, Vector2Int pushDir)
        {
            if (sheetDragCells == null || sheetDragCells.Count == 0 || set == null || sheet == null) return;

            var blocks = new List<Color32[]>();
            foreach (var (_, cell) in sheetDragCells) blocks.Add(CellPixels(cell));
            var plucked = TilesetAtlas.AddCells(set, tileSize, blocks, nextName);
            if (plucked == null)
            {
                statusLine.text = "Could not write the tileset's atlas — is the tileset saved?";
                sheetDragCells = null;
                return;
            }

            string folder = TilesFolder();
            var pattern = new List<(Vector2Int off, LevelTile tile)>();
            var made = new List<LevelTile>();
            for (int i = 0; i < sheetDragCells.Count; i++)
            {
                var (off, cell) = sheetDragCells[i];
                var t = CreateTile(sheetDragCells.Count == 1 ? nextName : $"{nextName}_{i + 1}",
                    new List<Sprite> { plucked[i] }, null, folder, new List<Vector2Int> { cell }, blocks[i]);
                made.Add(t);
                pattern.Add((off, t));
            }

            if (tilesGrid != null && tilesGrid.PlaceMinted(pattern, anchor, push, pushDir))
            {
                BumpName();
                selection.Clear();
                RefreshUsed();
                stage?.Refresh();
                UpdateStatus();
                statusLine.text = $"Plucked {made.Count} new tile(s) into the grid. " + statusLine.text;
            }
            else
            {
                foreach (var t in made) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(t));
                TilesetAtlas.RemoveCells(set, tileSize, plucked);
                AssetDatabase.SaveAssets();
                statusLine.text = "Drop blocked — target cells are occupied. Hold Alt to push, enable Overwrite, or drop on empty cells.";
            }
            sheetDragCells = null;
        }

        /// Dropping dragged sheet cells on the CLUMPS grid plucks them as ONE clump: the arrangement is the
        /// object, locked at birth. Member tiles become assets but never join the loose-tile palette.
        internal void MintClumpFromSheet(Vector2Int anchor)
        {
            if (sheetDragCells == null || sheetDragCells.Count == 0 || set == null || sheet == null) return;

            // Occupancy first — clumps neither overwrite nor push; an object either fits or it doesn't.
            var taken = new HashSet<Vector2Int>();
            foreach (var pr in set.clumps)
                if (pr?.cells != null)
                    foreach (var pc in pr.cells) taken.Add(pr.gridPos + pc.offset);
            foreach (var (off, _) in sheetDragCells)
            {
                var c = anchor + off;
                if (c.x < 0 || c.y < 0 || c.x >= Mathf.Max(1, set.clumpColumns) || taken.Contains(c))
                {
                    statusLine.text = "Drop blocked — the clump needs free cells inside the clump grid.";
                    sheetDragCells = null;
                    return;
                }
            }

            var blocks = new List<Color32[]>();
            foreach (var (_, cell) in sheetDragCells) blocks.Add(CellPixels(cell));
            var plucked = TilesetAtlas.AddCells(set, tileSize, blocks, nextName);
            if (plucked == null)
            {
                statusLine.text = "Could not write the tileset's atlas — is the tileset saved?";
                sheetDragCells = null;
                return;
            }

            string folder = TilesFolder();
            var clump = new Clump { displayName = nextName, gridPos = anchor };
            for (int i = 0; i < sheetDragCells.Count; i++)
            {
                var (off, cell) = sheetDragCells[i];
                var t = CreateTile(sheetDragCells.Count == 1 ? nextName : $"{nextName}_{i + 1}",
                    new List<Sprite> { plucked[i] }, null, folder, new List<Vector2Int> { cell }, blocks[i]);
                clump.cells.Add(new ClumpCell { offset = off, tile = t });
            }

            Undo.RecordObject(set, "Add clump");
            set.clumps.Add(clump);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            BumpName();
            selection.Clear();
            RefreshUsed();
            clumpsGrid?.RebuildCells();
            stage?.Refresh();
            UpdateStatus();
            statusLine.text = $"Plucked clump '{clump.displayName}' ({clump.cells.Count} tile(s)). " + statusLine.text;
            sheetDragCells = null;
        }

        /// Delete a clump AND its member tile assets and atlas cells — members are exclusively the clump's,
        /// so unlike loose-tile cell deletion this really destroys assets. Hence the confirmation.
        internal void DeleteClump(Clump clump)
        {
            if (clump == null || set == null) return;
            if (!EditorUtility.DisplayDialog("Delete clump?",
                    $"'{clump.displayName}' and its {clump.cells.Count} member tile asset(s) will be deleted. " +
                    "Levels already painted with them will lose those cells.", "Delete", "Cancel")) return;

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

            RefreshUsed();
            clumpsGrid?.RebuildCells();
            stage?.Refresh();
            UpdateStatus();
        }

        // ── the selection actions card: a right-click popover, pinnable and draggable ──
        VisualElement actionsCard;
        bool actionsPinned;
        EventCallback<PointerDownEvent> actionsOutsideHandler;

        internal void ShowGridActions(Vector2 panelPos)
        {
            CloseGridActions();
            var root = rootVisualElement;
            var local = root.WorldToLocal(panelPos);

            var card = new VisualElement();
            card.AddToClassList("lau-tool-shell__overlay");
            card.style.left = local.x;
            card.style.top = local.y;
            card.AddToClassList("lau-tileset__selection-menu");

            var header = Z.Row(
                Z.Text("Selection", ZuiText.Small, "Actions for the grid selection. Drag this bar to move the menu."),
                Z.Flexible(),
                Z.ToggleButton("Pin", "Keep this menu open after actions and outside clicks.", actionsPinned,
                    v => actionsPinned = v),
                Z.Button("×", "Close this menu.", CloseGridActions).W(20f));
            bool draggingCard = false;
            Vector2 dragOff = default;
            header.RegisterCallback<PointerDownEvent>(e =>
            {
                draggingCard = true;
                dragOff = (Vector2)e.position - new Vector2(card.resolvedStyle.left, card.resolvedStyle.top);
                header.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            header.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!draggingCard) return;
                var p = (Vector2)e.position - dragOff;
                card.style.left = p.x;
                card.style.top = p.y;
            });
            header.RegisterCallback<PointerUpEvent>(e =>
            {
                draggingCard = false;
                header.ReleasePointer(e.pointerId);
            });
            card.Add(header);

            int selCount = tilesGrid != null ? tilesGrid.SelectionCount : 0;
            var copy = Z.Button("Copy", "Copy the selected tile(s) as a pattern.",
                () => { tilesGrid.CopySelection(); AfterCardAction(); });
            var del = Z.Button("Delete", "Clear the selected cell(s); the tile assets stay on disk. Undoable.",
                () => { tilesGrid.DeleteSelection(); AfterCardAction(); });
            var paste = Z.Button("Paste", "Paste the copied pattern: click a grid cell to drop it. Right-click cancels; Alt pushes.",
                () => { tilesGrid.BeginPaste(); AfterCardAction(); });
            copy.SetEnabled(selCount > 0);
            del.SetEnabled(selCount > 0);
            paste.SetEnabled(TilesetGrid.ClipboardCount > 0);
            card.Add(copy);
            card.Add(del);
            card.Add(paste);

            actionsOutsideHandler = e =>
            {
                if (actionsPinned || actionsCard == null) return;
                if (!actionsCard.worldBound.Contains((Vector2)e.position)) CloseGridActions();
            };
            root.RegisterCallback(actionsOutsideHandler, TrickleDown.TrickleDown);
            root.Add(card);
            card.BringToFront();
            actionsCard = card;
        }

        void AfterCardAction()
        {
            if (!actionsPinned) CloseGridActions();
        }

        internal void CloseGridActions()
        {
            if (actionsCard == null) return;
            if (actionsOutsideHandler != null)
                rootVisualElement.UnregisterCallback(actionsOutsideHandler, TrickleDown.TrickleDown);
            actionsCard.RemoveFromHierarchy();
            actionsCard = null;
        }

        internal Texture2D TileThumb(LevelTile tile)
        {
            if (tile == null) return null;
            if (tileThumbs.TryGetValue(tile, out var thumb) && thumb != null) return thumb;
            thumb = tile.RenderPreviewTexture();
            if (thumb != null) thumb.hideFlags = HideFlags.HideAndDontSave;
            tileThumbs[tile] = thumb;
            return thumb;
        }

        // ── the tileset grid editor ────────────────────────────────────────────
        /// The tileset laid out as the 2D grid it IS: 1px gaps, marquee/Ctrl selection, drag to move a
        /// selection, Ctrl-drag to duplicate it, and empty cells as first-class citizens. Cell edits only —
        /// tile assets are never deleted here.
        internal class TilesetGrid : VisualElement
        {
            // Seamless view removes the gaps entirely: adjacent tiles butt together and preview exactly as
            // they would paint into a level — the whole point of position-as-pattern.
            float Gap => w.tilesetSeamless ? 0f : 1f;
            float Cell => Mathf.Clamp(w.tilesetCellZoom, 20f, 72f);   // VIEW zoom only — tile data unaffected

            readonly TilesetBuilderWindow w;
            readonly VisualElement thumbs;    // Image children
            readonly VisualElement overlay;   // selection/marquee/ghost — IN FRONT, always

            readonly HashSet<int> sel = new();
            static readonly List<(Vector2Int off, LevelTile tile)> clipboard = new();

            int hoverIdx = -1;
            bool marqueeing, dragPending, draggingTiles, dupDrag, ctrlAtDown, altHeld;
            int pendingToggle = -1;
            Vector2 downPos;
            int anchorIdx = -1;
            bool pasting;

            public int SelectionCount => sel.Count;
            public static int ClipboardCount => clipboard.Count;
            public bool Pasting => pasting;

            Tileset Set => w.set;
            int Cols => Mathf.Max(1, Set.paletteColumns);
            int Rows => Set.EffectiveRows;

            public TilesetGrid(TilesetBuilderWindow window)
            {
                w = window;
                AddToClassList("lau-tileset__grid");

                // Behind the thumbs: the same checkerboard as the sheet canvas — a tile's transparent
                // pixels are its layering promise, and the grid must show them as such.
                checker = new VisualElement { pickingMode = PickingMode.Ignore };
                checker.AddToClassList("lau-authoring__overlay-origin");
                Add(checker);

                thumbs = new VisualElement { pickingMode = PickingMode.Ignore };
                thumbs.AddToClassList("lau-tool-shell__overlay");
                Add(thumbs);

                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.AddToClassList("lau-authoring__canvas-overlay");
                overlay.generateVisualContent += PaintOverlay;
                Add(overlay);

                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);

                // A sheet selection dragged over lands here: dropping MINTS the cells as tiles at the drop
                // position, arrangement preserved — same rules as any placement (Overwrite / Alt-push).
                RegisterCallback<DragUpdatedEvent>(e =>
                {
                    if (DragAndDrop.GetGenericData(TilesetBuilderWindow.SheetDragKey) == null) return;
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    hoverIdx = CellAt(this.WorldToLocal(e.mousePosition));
                    overlay.MarkDirtyRepaint();
                });
                RegisterCallback<DragPerformEvent>(e =>
                {
                    if (DragAndDrop.GetGenericData(TilesetBuilderWindow.SheetDragKey) == null) return;
                    DragAndDrop.AcceptDrag();
                    DragAndDrop.SetGenericData(TilesetBuilderWindow.SheetDragKey, null);
                    var local = this.WorldToLocal(e.mousePosition);
                    int idx = CellAt(local);
                    if (idx < 0) return;
                    w.MintDropFromSheet(IdxToCell(idx), e.altKey, PushDirAt(local, idx));
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
                AddToClassList("lau-tool-shell__chrome");

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
                    img.tooltip = tile.displayName;
                    img.AddToClassList("lau-tool-shell__overlay");
                    img.style.left = r.xMin;
                    img.style.top = r.yMin;
                    img.style.width = Cell;
                    img.style.height = Cell;
                    // The real FULL-CELL sprite, never the browser thumbnail — thumbnails crop to visible
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
                (t.IsAnimated ? w.showAnimated
                    : w.cycleRandoms && t.variants != null && t.variants.Count > 1);

            void TickThumbAnimation()
            {
                // Seamless osc frames pulse on this same tick — repaint only while something shows one.
                if (w.tilesetSeamless && w.tilesetSeamlessMarks && (hoverIdx >= 0 || sel.Count > 0))
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
                if (w.showAnimated || w.cycleRandoms)
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

            // ── selection + toolbar ops ─────────────────────────────────────────
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
                w.RefreshGridToolbar();
            }

            public void DeleteSelection()
            {
                if (sel.Count == 0) return;
                Undo.RecordObject(Set, "Clear tileset cells");
                foreach (var i in sel) if (i < Set.tiles.Count) Set.tiles[i] = null;
                sel.Clear();
                Commit();
            }

            public void BeginPaste() { pasting = clipboard.Count > 0; w.RefreshGridToolbar(); overlay.MarkDirtyRepaint(); }
            public void CancelPaste() { pasting = false; w.RefreshGridToolbar(); overlay.MarkDirtyRepaint(); }

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
                    if (occupied && !w.overwriteOnDrop && !push) return false;
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

                var sources = duplicate ? null : new HashSet<int>(sel);
                var anchor = new Vector2Int(minX, minY) + delta;

                if (!duplicate)
                {
                    // Clear sources inside the same undo step; TryPlace records first, so group the two.
                    Undo.RecordObject(Set, "Move tiles");
                    foreach (var i in sel) Set.tiles[i] = null;
                    if (!TryPlace(pattern, anchor, sources, "Move tiles", push, pushDir))
                    {
                        // Blocked: put the sources back exactly as they were.
                        int k = 0;
                        foreach (var i in sel) { Set.tiles[i] = pattern[k].tile; k++; }
                        w.statusLine.text = "Move blocked — target cells are occupied. Hold Alt to push, enable Overwrite, or drop on empty cells.";
                        return;
                    }
                }
                else if (!TryPlace(pattern, anchor, null, "Duplicate tiles", push, pushDir))
                {
                    w.statusLine.text = "Duplicate blocked — target cells are occupied. Hold Alt to push, enable Overwrite, or drop on empty cells.";
                }
            }

            void Commit()
            {
                EditorUtility.SetDirty(Set);
                AssetDatabase.SaveAssets();
                w.RefreshUsed();
                w.stage?.Refresh();
                RebuildCells();
                w.RefreshGridToolbar();
                w.UpdateStatus();
            }

            // ── pointers ────────────────────────────────────────────────────────
            bool midPanning;
            Vector3 midLast;

            void OnDown(PointerDownEvent e)
            {
                // Middle button scrolls the grid's scrollers (horizontal box + the pane's vertical) —
                // works in every mode, including mid-paste.
                if (e.button == 2)
                {
                    midPanning = true;
                    midLast = e.position;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }

                int idx = CellAt(e.localPosition);
                hoverIdx = idx;

                if (pasting)
                {
                    if (e.button == 1 || idx < 0) CancelPaste();
                    else if (TryPlace(clipboard, IdxToCell(idx), null, "Paste tiles",
                                 e.altKey, PushDirAt(e.localPosition, idx))) pasting = false;
                    else w.statusLine.text = "Paste blocked — target cells are occupied. Hold Alt to push, enable Overwrite, or pick an empty spot.";
                    w.RefreshGridToolbar();
                    e.StopPropagation();
                    return;
                }
                if (e.button == 1)
                {
                    // The selection actions live in a right-click card, not in the window chrome.
                    w.ShowGridActions(e.position);
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;

                // Collision mode: a left click cycles the tile's own blocking shape — no selection, no drag.
                if (w.tileEditMode == 1)
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
                if (w.tileEditMode == 2)
                {
                    var tt = TileAt(idx);
                    if (tt != null && w.activeTag != null)
                    {
                        Undo.RecordObject(tt, "Tile tag");
                        if (!tt.tags.Remove(w.activeTag)) tt.tags.Add(w.activeTag);
                        EditorUtility.SetDirty(tt);
                        overlay.MarkDirtyRepaint();
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
                    else if (e.ctrlKey) { sel.Add(idx); overlay.MarkDirtyRepaint(); w.RefreshGridToolbar(); }
                    else if (!sel.Contains(idx)) { sel.Clear(); sel.Add(idx); overlay.MarkDirtyRepaint(); w.RefreshGridToolbar(); }
                    dragPending = true;
                }
                else
                {
                    if (!e.ctrlKey && sel.Count > 0) { sel.Clear(); overlay.MarkDirtyRepaint(); w.RefreshGridToolbar(); }
                    marqueeing = true;
                }
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (midPanning)
                {
                    // Content follows the pointer; each enclosing scroller clamps its own axis, so pushing
                    // both axes at every level is safe. Panel-space delta — local space scrolls under us.
                    var d = (Vector2)(e.position - midLast);
                    midLast = e.position;
                    for (var sv = this.GetFirstAncestorOfType<ScrollView>(); sv != null;
                         sv = sv.GetFirstAncestorOfType<ScrollView>())
                        sv.scrollOffset -= d;
                    return;
                }

                int idx = CellAt(e.localPosition);
                if (idx != hoverIdx || altHeld != e.altKey) { hoverIdx = idx; altHeld = e.altKey; overlay.MarkDirtyRepaint(); }

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
                if (midPanning)
                {
                    midPanning = false;
                    this.ReleasePointer(e.pointerId);
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
                    w.RefreshGridToolbar();
                }
                else if (marqueeing && anchorIdx >= 0 && idx >= 0)
                {
                    var a = IdxToCell(anchorIdx);
                    var b = IdxToCell(idx);
                    if (!ctrlAtDown) sel.Clear();
                    for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
                        for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
                        {
                            int i = CellToIdx(new Vector2Int(x, y));
                            if (TileAt(i) != null) sel.Add(i);
                        }
                    w.RefreshGridToolbar();
                }

                marqueeing = dragPending = draggingTiles = dupDrag = false;
                pendingToggle = -1;
                overlay.MarkDirtyRepaint();
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
                if (!w.tilesetSeamless)
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
                    float b = w.tilesetGridBrightness;
                    p.strokeColor = new Color(b, b, b, w.tilesetGridAlpha);
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
                if (w.tileEditMode == 1)
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
                if (w.tileEditMode == 2 && w.activeTag != null)
                    for (int ti = 0; ti < Set.tiles.Count; ti++)
                    {
                        var tt = Set.tiles[ti];
                        if (tt == null || !tt.HasTag(w.activeTag)) continue;
                        var c = w.activeTag.editorColor;
                        p.fillColor = new Color(c.r, c.g, c.b, 0.4f);
                        Path(CellRect(IdxToCell(ti)));
                        p.Fill();
                    }

                // Oscillating white↔black on the sheet's own clock — one pulse rules every canvas.
                float osc = 0.5f + 0.5f * Mathf.Sin((float)(EditorApplication.timeSinceStartup * w.gridOscSpeed * Mathf.PI * 2.0));

                if (!w.tilesetSeamless)
                    foreach (var i in sel)
                    {
                        var r = CellRect(IdxToCell(i));
                        p.fillColor = new Color(1f, 0.75f, 0.2f, 0.25f);
                        Path(r); p.Fill();
                        p.strokeColor = Color.black; p.lineWidth = 3f; Path(r); p.Stroke();
                        p.strokeColor = new Color(1f, 0.75f, 0.2f); p.lineWidth = 1.5f; Path(r); p.Stroke();
                    }
                else if (w.tilesetSeamlessMarks)
                    foreach (var i in sel)
                    {
                        var r = CellRect(IdxToCell(i));
                        p.strokeColor = new Color(osc, osc, osc, 1f); p.lineWidth = 2f; Path(r); p.Stroke();
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
                        bool blocked = c.y < Rows && TileAt(CellToIdx(c)) != null && !w.overwriteOnDrop
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
                    if (!w.tilesetSeamless)
                    {
                        p.strokeColor = new Color(1f, 1f, 1f, 0.6f); p.lineWidth = 1f; Path(r); p.Stroke();
                    }
                    else if (w.tilesetSeamlessMarks)
                    {
                        p.strokeColor = new Color(osc, osc, osc, 0.9f); p.lineWidth = 1.5f; Path(r); p.Stroke();
                    }
                }
            }
        }

        // ── the clumps grid: object-shaped content, every interaction whole-clump ──
        /// A clump's arrangement is LOCKED by definition, so hover, selection and movement all act on the
        /// whole clump — grab any member cell and you hold the object. Creation is drag-in from the sheet;
        /// deletion is on the right-click menu; content editing is a later workflow, deliberately.
        internal class ClumpsGrid : VisualElement
        {
            readonly TilesetBuilderWindow w;
            readonly VisualElement checker;
            Texture2D checkerTex;
            readonly VisualElement thumbs;
            readonly VisualElement overlay;

            int hoverClump = -1, selClump = -1;
            Vector2Int hoverCell = new(-1, -1);
            bool dragPending, draggingClump, sheetDropHover;
            Vector2 downPos;
            Vector2Int grabOff;             // grabbed cell relative to the dragged clump's anchor
            bool midPanning;
            Vector3 midLast;

            readonly Dictionary<Vector2Int, int> occupancy = new();

            Tileset Set => w.set;
            float Gap => w.tilesetSeamless ? 0f : 1f;
            float Cell => Mathf.Clamp(w.tilesetCellZoom, 20f, 72f);
            int Cols => Mathf.Max(1, Set.clumpColumns);
            int Rows => Set.EffectiveClumpRows;

            public ClumpsGrid(TilesetBuilderWindow window)
            {
                w = window;
                AddToClassList("lau-tileset__grid");

                checker = new VisualElement { pickingMode = PickingMode.Ignore };
                checker.AddToClassList("lau-authoring__overlay-origin");
                Add(checker);

                thumbs = new VisualElement { pickingMode = PickingMode.Ignore };
                thumbs.AddToClassList("lau-tool-shell__overlay");
                Add(thumbs);

                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.AddToClassList("lau-authoring__canvas-overlay");
                overlay.generateVisualContent += PaintOverlay;
                Add(overlay);

                tooltip = "Clumps: locked tile arrangements placed as one object. Drag a sheet selection " +
                          "here to pluck it as a clump; drag a clump to move it; right-click for actions.";

                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
                RegisterCallback<PointerLeaveEvent>(_ => { hoverClump = -1; sheetDropHover = false; overlay.MarkDirtyRepaint(); });

                RegisterCallback<DragUpdatedEvent>(e =>
                {
                    if (DragAndDrop.GetGenericData(TilesetBuilderWindow.SheetDragKey) == null) return;
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    sheetDropHover = CellAt(this.WorldToLocal(e.mousePosition), out hoverCell);
                    overlay.MarkDirtyRepaint();
                });
                RegisterCallback<DragPerformEvent>(e =>
                {
                    if (DragAndDrop.GetGenericData(TilesetBuilderWindow.SheetDragKey) == null) return;
                    DragAndDrop.AcceptDrag();
                    DragAndDrop.SetGenericData(TilesetBuilderWindow.SheetDragKey, null);
                    sheetDropHover = false;
                    if (CellAt(this.WorldToLocal(e.mousePosition), out var cell)) w.MintClumpFromSheet(cell);
                });
                RegisterCallback<DragExitedEvent>(_ => { sheetDropHover = false; overlay.MarkDirtyRepaint(); });

                // Seamless osc frames pulse here too — repaint only while something shows one.
                schedule.Execute(() =>
                {
                    if (w.tilesetSeamless && w.tilesetSeamlessMarks && (hoverClump >= 0 || selClump >= 0))
                        overlay.MarkDirtyRepaint();
                }).Every(80);
            }

            internal void RepaintOverlay() => overlay.MarkDirtyRepaint();

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
                AddToClassList("lau-tool-shell__chrome");

                StyleAsChecker(checker, ref checkerTex);
                checker.style.width = gw;
                checker.style.height = gh;

                thumbs.Clear();
                foreach (var pr in Set.clumps)
                {
                    if (pr?.cells == null) continue;
                    foreach (var pc in pr.cells)
                    {
                        if (pc.tile == null) continue;
                        var r = CellRect(pr.gridPos + pc.offset);
                        var img = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
                        img.tooltip = pr.displayName;
                        img.AddToClassList("lau-tool-shell__overlay");
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
                if (e.button == 2)
                {
                    midPanning = true;
                    midLast = e.position;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }

                bool onGrid = CellAt(e.localPosition, out var cell);
                int idx = onGrid ? ClumpAt(cell) : -1;

                if (e.button == 1)
                {
                    if (idx >= 0)
                    {
                        selClump = idx;
                        overlay.MarkDirtyRepaint();
                        var clump = Set.clumps[idx];
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent($"Delete '{clump.displayName}'"), false, () => w.DeleteClump(clump));
                        menu.ShowAsContext();
                    }
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;

                // Layers / Collision modes: a left click PAINTS the property of the exact member cell
                // under the cursor — no selection, no dragging, just assessment and correction.
                if (w.clumpEditMode != 0)
                {
                    if (idx >= 0)
                    {
                        var pr = Set.clumps[idx];
                        foreach (var pc in pr.cells)
                        {
                            if (pr.gridPos + pc.offset != cell) continue;
                            if (w.clumpEditMode == 1)
                            {
                                Undo.RecordObject(Set, "Clump cell layer");
                                pc.layerShift = pc.layerShift == 0 ? 1 : 0;
                                EditorUtility.SetDirty(Set);
                            }
                            else if (w.clumpEditMode == 2 && pc.tile != null)
                            {
                                Undo.RecordObject(pc.tile, "Clump cell collision");
                                pc.tile.colliderShape =
                                    pc.tile.colliderShape == Tile.ColliderType.Grid ? Tile.ColliderType.Sprite
                                    : pc.tile.colliderShape == Tile.ColliderType.Sprite ? Tile.ColliderType.None
                                    : Tile.ColliderType.Grid;
                                EditorUtility.SetDirty(pc.tile);
                            }
                            else if (w.clumpEditMode == 3 && pc.tile != null && w.activeTag != null)
                            {
                                Undo.RecordObject(pc.tile, "Tile tag");
                                if (!pc.tile.tags.Remove(w.activeTag)) pc.tile.tags.Add(w.activeTag);
                                EditorUtility.SetDirty(pc.tile);
                            }
                            break;
                        }
                        overlay.MarkDirtyRepaint();
                    }
                    e.StopPropagation();
                    return;
                }

                selClump = idx;
                if (idx >= 0)
                {
                    grabOff = cell - Set.clumps[idx].gridPos;
                    dragPending = true;
                    downPos = e.localPosition;
                    this.CapturePointer(e.pointerId);
                }
                overlay.MarkDirtyRepaint();
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (midPanning)
                {
                    var d = (Vector2)(e.position - midLast);
                    midLast = e.position;
                    for (var sv = this.GetFirstAncestorOfType<ScrollView>(); sv != null;
                         sv = sv.GetFirstAncestorOfType<ScrollView>())
                        sv.scrollOffset -= d;
                    return;
                }

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
                if (midPanning)
                {
                    midPanning = false;
                    this.ReleasePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }

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
                    else w.statusLine.text = "Move blocked — the clump needs free cells inside the clump grid.";
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

                if (!w.tilesetSeamless)
                {
                    // DISCONNECTED SEGMENTS — same Painter2D miter-join crash rule as every canvas here.
                    float b = w.tilesetGridBrightness;
                    p.strokeColor = new Color(b, b, b, w.tilesetGridAlpha);
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

                // Layers / Collision assessment tints: every member cell of every clump gets a coloured
                // translucent overlay, so the whole tab's routing is readable in one glance.
                if (w.clumpEditMode != 0)
                    foreach (var pr in Set.clumps)
                    {
                        if (pr?.cells == null) continue;
                        foreach (var pc in pr.cells)
                        {
                            Color tint;
                            if (w.clumpEditMode == 1)
                                tint = pc.layerShift > 0
                                    ? new Color(1f, 0.6f, 0.15f, 0.45f)     // orange: one layer in front
                                    : new Color(0.25f, 0.5f, 1f, 0.28f);    // blue: the stamped layer
                            else if (w.clumpEditMode == 2)
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
                                if (w.activeTag == null || pc.tile == null || !pc.tile.HasTag(w.activeTag)) continue;
                                var c = w.activeTag.editorColor;
                                tint = new Color(c.r, c.g, c.b, 0.4f);
                            }
                            p.fillColor = tint;
                            Path(CellRect(pr.gridPos + pc.offset));
                            p.Fill();
                        }
                    }

                float osc = 0.5f + 0.5f * Mathf.Sin((float)(EditorApplication.timeSinceStartup * w.gridOscSpeed * Mathf.PI * 2.0));

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
                    if (!w.tilesetSeamless)
                    {
                        var pr = Set.clumps[selClump];
                        if (pr?.cells != null)
                            foreach (var pc in pr.cells)
                            {
                                var r = CellRect(pr.gridPos + pc.offset);
                                p.fillColor = new Color(1f, 0.75f, 0.2f, 0.25f);
                                Path(r); p.Fill();
                            }
                        StrokeClump(selClump, new Color(1f, 0.75f, 0.2f), 1.5f, false);
                    }
                    else if (w.tilesetSeamlessMarks)
                        StrokeClump(selClump, new Color(1f, 1f, 1f, 1f), 2f, true);
                }

                // Hover: whole clump, unless we are dragging one around.
                if (!draggingClump && hoverClump >= 0 && hoverClump != selClump)
                {
                    if (!w.tilesetSeamless) StrokeClump(hoverClump, new Color(1f, 1f, 1f, 0.6f), 1f, false);
                    else if (w.tilesetSeamlessMarks) StrokeClump(hoverClump, new Color(1f, 1f, 1f, 0.9f), 1.5f, true);
                }

                // Move ghost: the clump's footprint at the target, green when it fits, red when blocked.
                if (draggingClump && selClump >= 0)
                {
                    var pr = Set.clumps[selClump];
                    var target = hoverCell - grabOff;
                    bool ok = CanPlace(pr, target, selClump);
                    p.strokeColor = ok ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.25f, 0.2f);
                    p.lineWidth = 2f;
                    foreach (var pc in pr.cells) { Path(CellRect(target + pc.offset)); p.Stroke(); }
                }

                // Sheet-drop ghost: where the plucked clump would land.
                if (sheetDropHover && w.SheetDragCells != null)
                    foreach (var (off, _) in w.SheetDragCells)
                    {
                        var c = hoverCell + off;
                        bool ok = c.x >= 0 && c.y >= 0 && c.x < Cols && ClumpAt(c) < 0;
                        p.strokeColor = ok ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.25f, 0.2f);
                        p.lineWidth = 2f;
                        Path(CellRect(c)); p.Stroke();
                    }
            }
        }

        // ── the sheet canvas ───────────────────────────────────────────────────
        internal class SheetStage : VisualElement
        {
            readonly TilesetBuilderWindow w;
            readonly Image image;
            float scale = 1f;
            Vector2 origin;
            bool dragging;
            Vector2Int dragStart, hover = new(-1, -1);

            readonly Label guide;

            // Painter2D content draws BENEATH an element's children — the exact trap the old PropStage
            // documented. The sheet Image is a child, so the grid/selection must live on their own overlay
            // child kept IN FRONT of it, or they render invisibly behind the art.
            readonly VisualElement overlay;

            public SheetStage(TilesetBuilderWindow window)
            {
                w = window;
                AddToClassList("zui-stage");
                AddToClassList("lau-tool-shell__column");
                AddToClassList("lau-tool-shell__clip");
                tooltip = "The sheet. Right-drag a marquee to select cells; Ctrl-click toggles one. Green = " +
                          "already plucked by this tileset. Wheel zooms toward the pointer; middle-drag pans " +
                          "while zoomed.";

                // Behind the sheet: a checkerboard, the universal "nothing here". Without it, transparent
                // regions show the dark stage background and read as BLACK ART — indistinguishable from
                // actual black pixels (a real user question, 2026-08-01).
                checker = new VisualElement { pickingMode = PickingMode.Ignore };
                checker.AddToClassList("lau-tool-shell__overlay");
                Add(checker);

                image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                image.AddToClassList("lau-tool-shell__overlay");
                Add(image);

                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.AddToClassList("lau-authoring__canvas-overlay");
                overlay.generateVisualContent += PaintOverlay;
                Add(overlay);

                // The canvas explains itself — an empty void teaches nothing.
                guide = new Label { pickingMode = PickingMode.Ignore };
                guide.AddToClassList("lau-authoring__sheet-guide-overlay");
                guide.AddToClassList("lau-tileset__sheet-guide");
                Add(guide);

                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
                RegisterCallback<WheelEvent>(OnWheel);
                RegisterCallback<GeometryChangedEvent>(_ => Refresh());

                // The grid oscillates black↔white (optional, speed + amount dialled in the Sheet box) so it
                // reads against ANY art — a fixed colour always loses to some sheet somewhere. 10 Hz is
                // plenty for a pulse and keeps the editor light.
                schedule.Execute(() => { if (w.gridOscillate) overlay.MarkDirtyRepaint(); }).Every(100);

                // A texture dragged from the Project window IS the sheet assignment — and so is a .png
                // dragged straight from Explorer, because sheets no longer need to be project assets.
                RegisterCallback<DragUpdatedEvent>(e =>
                {
                    foreach (var o in DragAndDrop.objectReferences)
                        if (o is Texture2D) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; return; }
                    foreach (var p in DragAndDrop.paths)
                        if (p != null && p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                        { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; return; }
                });
                RegisterCallback<DragPerformEvent>(e =>
                {
                    foreach (var o in DragAndDrop.objectReferences)
                        if (o is Texture2D tex)
                        {
                            DragAndDrop.AcceptDrag();
                            w.SetSheetFromAsset(tex);
                            return;
                        }
                    foreach (var p in DragAndDrop.paths)
                        if (p != null && p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                        {
                            DragAndDrop.AcceptDrag();
                            w.SetSheetPath(Path.GetFullPath(p));
                            return;
                        }
                });
            }

            /// How far the fit-scale shrinks the sheet on screen — the number the status line reports.
            public float DisplayScale => scale;

            public void Refresh()
            {
                Layout();
                guide.text = w.sheet == null
                    ? "Drop a sprite sheet here — from the project or straight from Explorer\n(or open one in the Sheet box on the left)"
                    : w.filled.Count == 0
                        ? "No art found on this grid — check the tile size and origin on the left."
                        : w.selection.Count == 0
                            ? "Right-drag to select cells · Ctrl-click toggles one · wheel zooms · middle-drag pans · green = already plucked"
                            : "";
                MarkDirtyRepaint();
                overlay.MarkDirtyRepaint();
                overlay.BringToFront();
                guide.BringToFront();
            }

            void Layout()
            {
                if (w.sheet == null)
                {
                    image.style.display = DisplayStyle.None;
                    checker.style.display = DisplayStyle.None;
                    return;
                }
                // A rebuilt stage lays out before it knows its size. The clamp below WRITES BACK into the
                // pan, so running it against degenerate geometry would destroy the user's view position —
                // the "canvas snaps somewhere else after a drop" bug. Wait for real geometry.
                if (float.IsNaN(contentRect.width) || contentRect.width < 20f || contentRect.height < 20f) return;
                float availW = Mathf.Max(1f, contentRect.width - 8f);
                float availH = Mathf.Max(1f, contentRect.height - 8f);
                float fit = Mathf.Min(availW / w.sheet.width, availH / w.sheet.height);
                scale = fit * Mathf.Clamp(w.sheetZoom, 1f, 16f);
                float pw = w.sheet.width * scale, ph = w.sheet.height * scale;

                // Centered at fit; once a side overflows the pane, the pan takes that axis over — clamped
                // so the sheet's edge never detaches from the pane's edge, and written back so it cannot
                // accumulate off-screen.
                float cx = (contentRect.width - pw) * 0.5f, cy = (contentRect.height - ph) * 0.5f;
                float ox = cx, oy = cy;
                if (pw > contentRect.width)
                {
                    ox = Mathf.Clamp(cx + w.sheetPan.x, contentRect.width - pw - 4f, 4f);
                    w.sheetPan.x = ox - cx;
                }
                else w.sheetPan.x = 0f;
                if (ph > contentRect.height)
                {
                    oy = Mathf.Clamp(cy + w.sheetPan.y, contentRect.height - ph - 4f, 4f);
                    w.sheetPan.y = oy - cy;
                }
                else w.sheetPan.y = 0f;
                origin = new Vector2(ox, oy);

                image.style.display = DisplayStyle.Flex;
                image.image = w.sheet;
                image.style.left = origin.x;
                image.style.top = origin.y;
                image.style.width = pw;
                image.style.height = ph;

                EnsureChecker();
                checker.style.display = DisplayStyle.Flex;
                checker.style.left = origin.x;
                checker.style.top = origin.y;
                checker.style.width = pw;
                checker.style.height = ph;
            }

            readonly VisualElement checker;
            Texture2D checkerTex;

            /// Lazy so a dock/tab detach that killed the texture heals on the next layout.
            void EnsureChecker() => StyleAsChecker(checker, ref checkerTex);

            Rect CellRect(Vector2Int cell) => new(
                origin.x + w.CellPxX(cell.x) * scale,
                origin.y + w.CellPxTop(cell.y) * scale,
                w.tileSize * scale, w.tileSize * scale);

            bool CellAt(Vector2 p, out Vector2Int cell)
            {
                float stepX = (w.tileSize + w.spacingX) * scale;
                float stepY = (w.tileSize + w.spacingY) * scale;
                cell = new Vector2Int(
                    Mathf.FloorToInt((p.x - origin.x - w.originX * scale) / Mathf.Max(0.0001f, stepX)),
                    Mathf.FloorToInt((p.y - origin.y - w.originY * scale) / Mathf.Max(0.0001f, stepY)));
                return w.sheet != null && cell.x >= 0 && cell.y >= 0 && cell.x < w.cols && cell.y < w.rows;
            }

            void PaintOverlay(MeshGenerationContext ctx)
            {
                if (w.sheet == null) return;
                Layout();
                var p = ctx.painter2D;

                void CellPath(Rect r)
                {
                    p.BeginPath();
                    p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin));
                    p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax));
                    p.ClosePath();
                }

                // The oscillating grid: a smooth black↔white swing whose reach is the Amount dial. Off =
                // a steady light grey. ONE path, one stroke — per-cell strokes at this cell count made the
                // whole editor feel busy.
                float osc = w.gridOscillate
                    ? 0.5f + 0.5f * w.gridOscAmplitude *
                      Mathf.Sin((float)(EditorApplication.timeSinceStartup * w.gridOscSpeed * Mathf.PI * 2.0))
                    : w.gridBrightness;   // static mode: the user's own colour; selections follow it too
                // DISCONNECTED SEGMENTS on purpose — a single-segment subpath has no joins, and Painter2D's
                // native miter-join tessellation CRASHES on manually-closed subpaths at this count (the
                // 2026-08-01 launch-crash loop: StrokeMiteredJoin→ConnectInnerJoins). Never stroke closed
                // shapes in bulk here.
                p.strokeColor = new Color(osc, osc, osc, w.gridOscillate ? 0.8f : w.gridAlpha);
                p.lineWidth = 1f;
                p.BeginPath();
                if (w.spacingX == 0 && w.spacingY == 0)
                {
                    // Gapless grid: CONTINUOUS full-length lines — cols+rows segments instead of 4-per-cell.
                    // A 13k-cell sheet was 52k segments per repaint, which read as a hung editor.
                    float x0 = origin.x + w.originX * scale, y0 = origin.y + w.originY * scale;
                    float wpx = w.cols * w.tileSize * scale, hpx = w.rows * w.tileSize * scale;
                    for (int c = 0; c <= w.cols; c++)
                    {
                        p.MoveTo(new Vector2(x0 + c * w.tileSize * scale, y0));
                        p.LineTo(new Vector2(x0 + c * w.tileSize * scale, y0 + hpx));
                    }
                    for (int r = 0; r <= w.rows; r++)
                    {
                        p.MoveTo(new Vector2(x0, y0 + r * w.tileSize * scale));
                        p.LineTo(new Vector2(x0 + wpx, y0 + r * w.tileSize * scale));
                    }
                }
                else
                {
                    // Spaced grid: per-cell disconnected segments (padded sheets are far smaller).
                    for (int r = 0; r < w.rows; r++)
                        for (int c = 0; c < w.cols; c++)
                        {
                            var cr = CellRect(new Vector2Int(c, r));
                            p.MoveTo(new Vector2(cr.xMin, cr.yMin)); p.LineTo(new Vector2(cr.xMax, cr.yMin));
                            p.MoveTo(new Vector2(cr.xMax, cr.yMin)); p.LineTo(new Vector2(cr.xMax, cr.yMax));
                            p.MoveTo(new Vector2(cr.xMax, cr.yMax)); p.LineTo(new Vector2(cr.xMin, cr.yMax));
                            p.MoveTo(new Vector2(cr.xMin, cr.yMax)); p.LineTo(new Vector2(cr.xMin, cr.yMin));
                        }
                }
                p.Stroke();

                foreach (var cell in w.used)
                {
                    p.fillColor = new Color(0.2f, 0.9f, 0.35f, 0.25f);
                    CellPath(CellRect(cell));
                    p.Fill();
                }

                // Selection: an OSCILLATING translucent fill (white↔black, same clock as the grid) plus a
                // double outline — visible on any art, and clearly "live".
                foreach (var cell in w.selection)
                {
                    var r = CellRect(cell);
                    p.fillColor = new Color(osc, osc, osc, 0.30f);
                    CellPath(r); p.Fill();
                    p.strokeColor = Color.black; p.lineWidth = 3f; CellPath(r); p.Stroke();
                    p.strokeColor = new Color(osc, osc, osc, 1f); p.lineWidth = 1.5f;
                    CellPath(r); p.Stroke();
                }

                if (dragging)
                {
                    // Live marquee: the rect between anchor and hover, double-outlined like the selection.
                    var a = CellRect(dragStart);
                    var b = CellRect(hover);
                    var rect = Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                        Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
                    p.strokeColor = Color.black; p.lineWidth = 3.5f; CellPath(rect); p.Stroke();
                    p.strokeColor = Color.white; p.lineWidth = 1.5f;
                    CellPath(rect); p.Stroke();
                }
                else if (hover.x >= 0)
                {
                    var r = CellRect(hover);
                    p.strokeColor = Color.black; p.lineWidth = 2.5f; CellPath(r); p.Stroke();
                    p.strokeColor = Color.white; p.lineWidth = 1f; CellPath(r); p.Stroke();
                }
            }

            bool sheetDragPending;
            Vector2 downPos;
            bool panning;
            Vector3 panLast;

            /// Wheel zooms toward the pointer: the sheet pixel under the cursor stays under the cursor,
            /// so zooming dives into what you are looking at, not into the sheet's centre.
            void OnWheel(WheelEvent e)
            {
                if (w.sheet == null) return;
                float old = Mathf.Clamp(w.sheetZoom, 1f, 16f);
                float zoom = Mathf.Clamp(old * (e.delta.y < 0 ? 1.25f : 0.8f), 1f, 16f);
                if (!Mathf.Approximately(zoom, old))
                {
                    Vector2 p = e.localMousePosition;
                    Vector2 pixel = (p - origin) / Mathf.Max(0.0001f, scale);
                    float ns = scale / old * zoom;
                    float pw = w.sheet.width * ns, ph = w.sheet.height * ns;
                    var center = new Vector2((contentRect.width - pw) * 0.5f, (contentRect.height - ph) * 0.5f);
                    w.sheetPan = p - pixel * ns - center;
                    w.sheetZoom = zoom;
                    Refresh();
                    w.UpdateStatus();   // the "shown at %" readout must track the wheel
                }
                e.StopPropagation();
            }

            void OnDown(PointerDownEvent e)
            {
                // Middle button pans the zoomed sheet — the canvas has no scrollbars, this IS its scrolling.
                if (e.button == 2)
                {
                    if (w.sheet == null) return;
                    panning = true;
                    panLast = e.position;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }

                bool onCell = CellAt(e.localPosition, out var cell);
                if (onCell) hover = cell;

                // RIGHT-drag = marquee. The left button is for selecting and for dragging a selection OUT
                // (onto the tileset grid), so it cannot double as the marquee — a click on a tile would be
                // ambiguous. Same right-drag convention as the Laumination Builder's auto-marquee.
                if (e.button == 1)
                {
                    if (!onCell) return;
                    dragging = true;
                    dragStart = cell;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;

                if (e.ctrlKey && onCell)
                {
                    // Toggle a single cell in or out, keeping selection order for the kept ones.
                    if (!w.selection.Remove(cell) && w.filled.Contains(cell)) w.selection.Add(cell);
                    w.UpdateStatus();
                    Refresh();
                    e.StopPropagation();
                    return;
                }

                if (onCell && w.filled.Contains(cell))
                {
                    if (!w.selection.Contains(cell))
                    {
                        w.selection.Clear();
                        w.selection.Add(cell);
                        w.UpdateStatus();
                        Refresh();
                    }
                    // A left-drag from here becomes an editor drag of the selection, droppable on the grid.
                    sheetDragPending = true;
                    downPos = e.localPosition;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                }
                else if (w.selection.Count > 0)
                {
                    w.selection.Clear();
                    w.UpdateStatus();
                    Refresh();
                }
            }

            void OnMove(PointerMoveEvent e)
            {
                if (panning)
                {
                    // Panel-space delta, because localPosition shifts under the element as it pans.
                    var d = (Vector2)(e.position - panLast);
                    panLast = e.position;
                    w.sheetPan += d;
                    Refresh();
                    return;
                }

                CellAt(e.localPosition, out var cell);
                cell.x = Mathf.Clamp(cell.x, 0, Mathf.Max(0, w.cols - 1));
                cell.y = Mathf.Clamp(cell.y, 0, Mathf.Max(0, w.rows - 1));
                hover = cell;

                if (sheetDragPending && (e.localPosition - (Vector3)downPos).sqrMagnitude > 16f)
                {
                    sheetDragPending = false;
                    this.ReleasePointer(e.pointerId);
                    w.BeginSheetDrag();
                }
                MarkDirtyRepaint();
            }

            void OnUp(PointerUpEvent e)
            {
                if (panning)
                {
                    panning = false;
                    this.ReleasePointer(e.pointerId);
                    return;
                }

                sheetDragPending = false;
                if (!dragging) return;
                dragging = false;
                this.ReleasePointer(e.pointerId);

                // Marquee → the rect's ART-BEARING cells in reading order (that order becomes
                // variant/frame order, so it must be predictable). Ctrl adds to the selection.
                if (!e.ctrlKey) w.selection.Clear();
                for (int r = Mathf.Min(dragStart.y, hover.y); r <= Mathf.Max(dragStart.y, hover.y); r++)
                    for (int c = Mathf.Min(dragStart.x, hover.x); c <= Mathf.Max(dragStart.x, hover.x); c++)
                    {
                        var cell = new Vector2Int(c, r);
                        if (w.filled.Contains(cell) && !w.selection.Contains(cell)) w.selection.Add(cell);
                    }
                w.UpdateStatus();
                Refresh();
                e.StopPropagation();
            }
        }
    }
}
