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
    ///
    /// The tileset GRID half of this window is not its own: `TilesetGridView` is the shared control the
    /// Cartographer's Tileset box hosts too (extracted 2026-08-03), and this window is its host —
    /// supplying the tileset, the view dials, the status line, and the source SHEET that only exists here.
    public class TilesetBuilderWindow : ZuiAssetWindow<Tileset>, ITilesetGridHost, ITilesetGridSheetSource
    {
        // No menu item on purpose: reached from the Cartographer window's Tileset box, the same way the
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
        [SerializeField] float gridOscAmplitude = 1f;    // 0 = static mid-grey, 1 = full black↔white swing
        [SerializeField] float gridBrightness = 0.75f;   // static grid: 0 = black, 1 = white
        [SerializeField] float gridAlpha = 0.8f;         // static grid opacity
        // Every dial the SHARED grid editor renders by, in the one object TilesetGridView reads. The
        // oscillation SPEED lives in here too even though the sheet canvas also uses it: one clock, dialled
        // once, so both canvases pulse together instead of drifting apart.
        [SerializeField] TilesetGridOptions gridOptions = new();
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
        Button btnAdd, btnRandom, btnAnimated;
        Image animPreview;
        Texture2D previewTex;                                    // reused canvas for the animation preview
        Vector2Int previewShown = new(int.MinValue, int.MinValue);
        readonly Dictionary<Object, Texture2D> tileThumbs = new();

        protected override void OnEnable()
        {
            RestoreWindowState();   // FIRST — everything below (and the base build) feeds off these fields
            base.OnEnable();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // The path string survives domain reloads; the readable pixel copy does not — rebuild it.
            // Neither does the md5 (plain field) — recompute it, or the used-cells tint and the sheet
            // memory silently stop matching after every reload/reopen.
            if (!string.IsNullOrEmpty(sheetPath))
            {
                if (string.IsNullOrEmpty(sheetMd5)) sheetMd5 = SheetProvenance.Md5OfFile(sheetPath);
                LoadSheet();
            }
        }

        protected override void OnDisable()
        {
            SaveWindowState();
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
            tilesetView?.Rebuild();
        }

        // ── window-state persistence: the dials survive close/reopen, not just domain reloads ─────────
        // A targeted DTO on purpose, NOT EditorJsonUtility.ToJson(this): whole-window JSON stores the
        // asset/sheet/tag REFERENCES as session-local instanceIDs (dead after an editor restart, wrong
        // object in the worst case) and would overwrite EditorWindow/ZuiAssetWindow serialized internals
        // behind the base's back (`asset` outside SetAsset, window chrome state). Plain values travel
        // as-is; the two asset references travel as GUIDs, which are stable forever. Key is per-project —
        // EditorPrefs is machine-global and Laubrary runs in many projects at once.
        static string StateKey => "Laubrary.TilesetBuilder.State." + PlayerSettings.productGUID;

        [System.Serializable]
        class WindowState
        {
            public int tileSize, spacingX, spacingY, originX, originY;
            public bool gridOscillate;
            public float gridOscSpeed, gridOscAmplitude, gridBrightness, gridAlpha;
            public bool overwriteOnDrop;
            public float tilesetCellZoom, tilesetGridBrightness, tilesetGridAlpha;
            public bool tilesetSeamless, tilesetSeamlessMarks;
            public int libraryTab, clumpEditMode, tileEditMode;
            public bool showAnimated, cycleRandoms;
            public float sheetZoom;
            public string nextName;
            public int nextPolicy;
            public float animFps;
            public string sheetPath;
            public string tilesetGuid, activeTagGuid;
        }

        static string GuidOf(Object o)
        {
            string p = o != null ? AssetDatabase.GetAssetPath(o) : null;
            return string.IsNullOrEmpty(p) ? "" : AssetDatabase.AssetPathToGUID(p);
        }

        // The JSON field names are deliberately UNCHANGED by the 2026-08-03 extraction, even though the
        // values now live inside `gridOptions` — a renamed key silently resets every dial the user had set.
        void SaveWindowState()
        {
            var s = new WindowState
            {
                tileSize = tileSize, spacingX = spacingX, spacingY = spacingY, originX = originX, originY = originY,
                gridOscillate = gridOscillate, gridOscSpeed = gridOptions.oscSpeed, gridOscAmplitude = gridOscAmplitude,
                gridBrightness = gridBrightness, gridAlpha = gridAlpha,
                overwriteOnDrop = gridOptions.overwriteOnDrop, tilesetCellZoom = gridOptions.cellZoom,
                tilesetGridBrightness = gridOptions.lineBrightness, tilesetGridAlpha = gridOptions.lineAlpha,
                tilesetSeamless = gridOptions.seamless, tilesetSeamlessMarks = gridOptions.seamlessMarks,
                libraryTab = gridOptions.tab, clumpEditMode = gridOptions.clumpEditMode, tileEditMode = gridOptions.tileEditMode,
                showAnimated = gridOptions.showAnimated, cycleRandoms = gridOptions.cycleRandoms,
                sheetZoom = sheetZoom, nextName = nextName, nextPolicy = (int)nextPolicy, animFps = animFps,
                sheetPath = sheetPath, tilesetGuid = GuidOf(set), activeTagGuid = GuidOf(gridOptions.activeTag),
            };
            EditorPrefs.SetString(StateKey, EditorJsonUtility.ToJson(s));
        }

        void RestoreWindowState()
        {
            string json = EditorPrefs.GetString(StateKey, "");
            if (string.IsNullOrEmpty(json)) return;
            var s = new WindowState();
            try { EditorJsonUtility.FromJsonOverwrite(json, s); }
            catch { return; }

            tileSize = Mathf.Max(4, s.tileSize);
            spacingX = Mathf.Max(0, s.spacingX);
            spacingY = Mathf.Max(0, s.spacingY);
            originX = Mathf.Max(0, s.originX);
            originY = Mathf.Max(0, s.originY);
            gridOscillate = s.gridOscillate;
            gridOscAmplitude = s.gridOscAmplitude;
            gridBrightness = s.gridBrightness;
            gridAlpha = s.gridAlpha;
            gridOptions.oscSpeed = s.gridOscSpeed;
            gridOptions.overwriteOnDrop = s.overwriteOnDrop;
            gridOptions.cellZoom = s.tilesetCellZoom;
            gridOptions.lineBrightness = s.tilesetGridBrightness;
            gridOptions.lineAlpha = s.tilesetGridAlpha;
            gridOptions.seamless = s.tilesetSeamless;
            gridOptions.seamlessMarks = s.tilesetSeamlessMarks;
            gridOptions.tab = s.libraryTab;
            gridOptions.clumpEditMode = s.clumpEditMode;
            gridOptions.tileEditMode = s.tileEditMode;
            gridOptions.showAnimated = s.showAnimated;
            gridOptions.cycleRandoms = s.cycleRandoms;
            gridOptions.Clamp();   // a prefs blob from an older build can carry values out of range
            sheetZoom = s.sheetZoom;
            if (!string.IsNullOrEmpty(s.nextName)) nextName = s.nextName;
            nextPolicy = (VariantPolicy)s.nextPolicy;
            animFps = s.animFps;

            // Unity's own layout serialization restores an already-open window (editor restart) with its
            // references intact — never stomp a live value with the prefs copy; only fill the fresh-open void.
            if (string.IsNullOrEmpty(sheetPath) && !string.IsNullOrEmpty(s.sheetPath))
            {
                sheetPath = s.sheetPath;
                ResolveSheetAsset();
            }
            if (asset == null && !string.IsNullOrEmpty(s.tilesetGuid))
                asset = AssetDatabase.LoadAssetAtPath<Tileset>(AssetDatabase.GUIDToAssetPath(s.tilesetGuid));
            if (gridOptions.activeTag == null && !string.IsNullOrEmpty(s.activeTagGuid))
                gridOptions.activeTag = AssetDatabase.LoadAssetAtPath<TileTag>(AssetDatabase.GUIDToAssetPath(s.activeTagGuid));
        }

        // ── ITilesetGridHost / ITilesetGridSheetSource: this window as the shared grid's host ─────────
        Tileset ITilesetGridHost.GridTileset => set;
        TilesetGridOptions ITilesetGridHost.GridOptions => gridOptions;
        VisualElement ITilesetGridHost.GridCardHost => rootVisualElement;

        void ITilesetGridHost.GridWarn(string message) => FlashStatus(message);

        /// The grid's own report goes in FRONT of the standing sheet readout, so "Pasted 3 tile(s)" is
        /// read first and the context it happened in survives underneath it.
        void ITilesetGridHost.GridReport(string message)
        {
            if (statusLine == null) return;
            statusLine.text = message + " " + statusLine.text;
        }

        void ITilesetGridHost.GridContentChanged()
        {
            // Baked thumbnails would keep showing pre-edit pixels after an atlas flip; the sheet's
            // used-cells tint and the status line both describe state the grid just changed.
            InvalidateTileThumbs();
            RefreshUsed();
            stage?.Refresh();
            UpdateStatus();
        }

        /// The Builder's selection means exactly one thing — what the edit actions act on — and the grid
        /// already owns that. Nothing further to do here; the Cartographer is where this seam earns its keep.
        void ITilesetGridHost.GridSelectionChanged() { }

        /// Picking a clump here selects it for moving, deleting and METADATA — and the pick is remembered by
        /// id so a rebuild can hand it back (see savedClumpId).
        void ITilesetGridHost.GridClumpPicked(Clump clump)
            => savedClumpId = clump != null ? clump.EnsureId() : "";

        IReadOnlyList<(Vector2Int off, Vector2Int cell)> ITilesetGridSheetSource.GridSheetDragCells => sheetDragCells;
        void ITilesetGridSheetSource.GridSheetDropTiles(Vector2Int anchor, bool push, Vector2Int pushDir) =>
            MintDropFromSheet(anchor, push, pushDir);
        void ITilesetGridSheetSource.GridSheetDropClump(Vector2Int anchor) => MintClumpFromSheet(anchor);

        protected override void OnAssetChanged()
        {
            selection.Clear();
            resetScrollOnBuild = true;   // a different tileset is a new context — start at the top
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
            leftScroll = scroll;
            // Give back the place OnBeforeRebuild captured. Only AFTER the fresh content has real
            // geometry — setting the offset now clamps against a zero-sized layout and silently
            // becomes 0 (the "pane snaps to the top" bug).
            if (savedLeftScroll.y > 0.5f)
            {
                var want = savedLeftScroll;
                scroll.contentContainer.RegisterCallbackOnce<GeometryChangedEvent>(_ => scroll.scrollOffset = want);
            }
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
            statusFlashSeq = 0;   // fresh label — recapture its theme colour on the next flash
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

            // The SHARED tileset editor — the same control the Cartographer's Tileset box hosts.
            tilesetView = new TilesetGridView(this, savedGridScrollX);
            // Hand the clump selection back. A new TilesetGridView starts with nothing selected, and this
            // window is recreated on every undo/redo and every domain reload — so without this one line, a
            // single Ctrl+Z blanks and disables the whole Metadata row (Edit meta…, Clear, the "declares:"
            // readout and the drift warning all describe the SELECTED clump) with no visible cause.
            tilesetView.SelectClumpById(savedClumpId);
            var libraryBox = Z.Box("Tileset", "The tileset AS A GRID — positions are the patterns the Cartographer's Tileset box shows. Tiles and Clumps are its two tabs. Click / Ctrl-click / marquee to select; drag a selection to move it; Ctrl-drag duplicates as INDEPENDENT copies (editing one never affects the other); right-click for actions.",
                tilesetView);

            // View dials live ON the header line (the user's own mockup, 2026-08-01): they govern how the
            // grids display, so they cost no body row and stay put across tab switches.
            tilesetView.AddHeaderDials(libraryBox);

            root.Add(libraryBox);
            UpdateStatus();
        }

        TilesetGridView tilesetView;

        // ── workspace stability: rebuilds must never cost the user their place ────────────────────
        ScrollView leftScroll;        // the left pane's vertical scroller — recreated by full rebuilds
        Vector2 savedLeftScroll;      // captured before a rebuild, reapplied once the new pane has geometry
        float savedGridScrollX;       // the shared grid view's horizontal place, handed to its successor
        [SerializeField] string savedClumpId = "";   // the picked clump, BY ID — survives rebuilds AND reloads
        bool resetScrollOnBuild;      // an asset SWITCH is a new context — restore nothing then
        bool projectRefreshQueued;

        /// Full rebuilds (undo/redo, asset chrome ops, external project changes) recreate both
        /// scrollers at offset 0 — capture the user's place first so the rebuilt pane can give it back.
        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();
            if (resetScrollOnBuild)
            {
                resetScrollOnBuild = false;
                savedLeftScroll = Vector2.zero;
                savedGridScrollX = 0f;
                return;
            }
            if (leftScroll != null) savedLeftScroll = leftScroll.scrollOffset;
            if (tilesetView != null) savedGridScrollX = tilesetView.ScrollX;
        }

        /// Mid-edit, a project change must NOT rebuild the window. The base's full rebuild does two
        /// kinds of damage here (both proven live, 2026-08-03): it recreates the left pane, the grid
        /// and its scrollers from scratch — offset 0, empty selection, pinned action cards gone (the
        /// "copy/paste snaps the pane to the top" bug) — and it runs in the very tick the import batch
        /// lands, where a just-forked tile asset still reads as NULL out of Set.tiles, so the fresh
        /// grid skips that cell's Image and the pasted tile renders EMPTY until the next refresh.
        /// Instead: one deferred, targeted refresh a tick later, when references resolve — the
        /// workspace stays exactly where the user left it.
        protected override void OnProjectChanged()
        {
            if (asset == null || IsBrowsing) { base.OnProjectChanged(); return; }
            if (projectRefreshQueued) return;
            projectRefreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                projectRefreshQueued = false;
                if (this == null || asset == null) return;
                RefreshUsed();
                tilesetView?.RefreshCells();
                stage?.Refresh();
                // Deliberately NOT UpdateStatus(): it would overwrite the feedback an operation just
                // wrote ("Pasted 3 tile(s)…") one tick after every gesture.
            };
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

        // ── blocked-operation emphasis ─────────────────────────────────────────
        int statusFlashSeq;
        Color statusFlashBase;

        /// A BLOCKED operation must be unmissable — a user who misses the quiet grey line concludes
        /// the feature is broken (the 2026-08-03 "flip doesn't mirror" report). Same reserved status
        /// line, same geometry (the stable-layout rule leaves text and COLOUR as the only free
        /// channels): the message lands in warning red and fades back to the label's own colour.
        internal void FlashStatus(string message)
        {
            if (statusLine == null) return;
            statusLine.text = message;
            if (statusFlashSeq == 0)
            {
                // First flash on this label: its resolved colour IS the theme's subtle grey.
                var c = statusLine.resolvedStyle.color;
                statusFlashBase = c.a > 0.01f ? c : new Color(0.6f, 0.6f, 0.6f);
            }
            int seq = ++statusFlashSeq;
            var red = new Color(1f, 0.38f, 0.32f);
            statusLine.style.color = red;
            double t0 = EditorApplication.timeSinceStartup;
            const float fade = 1.6f;
            statusLine.schedule.Execute(() =>
            {
                if (seq != statusFlashSeq) return;   // a newer flash owns the line
                float k = Mathf.Clamp01((float)(EditorApplication.timeSinceStartup - t0) / fade);
                if (k >= 1f) statusLine.style.color = new StyleColor(StyleKeyword.Null);   // hand colour back to the stylesheet
                else statusLine.style.color = Color.Lerp(red, statusFlashBase, k);
            }).Every(60).ForDuration((long)(fade * 1000f) + 200);
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

        /// Visibility, not display: a hidden control KEEPS its space, so toggling it cannot move anything.
        static void SetShown(VisualElement e, bool shown) =>
            e.style.visibility = shown ? Visibility.Visible : Visibility.Hidden;

        /// One row, four dials, two visible: Speed/Amount while oscillating, Colour/Alpha when static.
        /// They swap IN PLACE (visibility, same slots), so toggling never reflows the pane.
        VisualElement OscillationRow()
        {
            var speed = Z.MicroSlider("Speed", gridOptions.oscSpeed, 0.2f, 4f,
                "Oscillation speed, cycles per second — one clock for the sheet grid AND the tileset grid's seamless marks.",
                v => gridOptions.oscSpeed = v, 110f);
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

        /// Resolve project membership so the ObjectField can show the asset — purely cosmetic.
        void ResolveSheetAsset()
        {
            sheetAsset = null;
            if (string.IsNullOrEmpty(sheetPath)) return;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            string full = Path.GetFullPath(sheetPath).Replace('\\', '/');
            if (full.StartsWith(projectRoot + "/", System.StringComparison.OrdinalIgnoreCase))
                sheetAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(full.Substring(projectRoot.Length + 1));
        }

        /// Point the builder at a sheet file. THE single assignment path: identity, memory, pixels, UI.
        internal void SetSheetPath(string absolutePath, bool rebuild = true)
        {
            sheetPath = absolutePath;
            ResolveSheetAsset();

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
            if (cells.Count == 0) { FlashStatus("Nothing selected — marquee cells on the sheet first."); return; }

            // The PLUCK: the selected cells' pixels leave the sheet and enter the tileset's atlas — one
            // file write, one import, however many cells.
            var blocks = new List<Color32[]>();
            foreach (var cell in cells) blocks.Add(CellPixels(cell));
            var picked = TilesetAtlas.AddCells(set, tileSize, blocks, nextName);
            if (picked == null) { FlashStatus("Could not write the tileset's atlas — is the tileset saved?"); return; }

            string folder = TilesetForge.TilesFolder(set);
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
            // Cells changed, not the strip's structure — RebuildCells refreshes them in place. The
            // full strip rebuild would replace the grid and its scroller, costing the user's place.
            tilesetView?.RefreshCells();
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
            DragAndDrop.SetGenericData(TilesetGridView.SheetDragKey, this);
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
                FlashStatus("Could not write the tileset's atlas — is the tileset saved?");
                sheetDragCells = null;
                return;
            }

            string folder = TilesetForge.TilesFolder(set);
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

            if (tilesetView != null && tilesetView.PlaceMinted(pattern, anchor, push, pushDir))
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
                FlashStatus("Drop blocked — target cells are occupied. Hold Alt to push, enable Overwrite, or drop on empty cells.");
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
                    FlashStatus("Drop blocked — the clump needs free cells inside the clump grid.");
                    sheetDragCells = null;
                    return;
                }
            }

            var blocks = new List<Color32[]>();
            foreach (var (_, cell) in sheetDragCells) blocks.Add(CellPixels(cell));
            var plucked = TilesetAtlas.AddCells(set, tileSize, blocks, nextName);
            if (plucked == null)
            {
                FlashStatus("Could not write the tileset's atlas — is the tileset saved?");
                sheetDragCells = null;
                return;
            }

            string folder = TilesetForge.TilesFolder(set);
            // The id is IDENTITY (a stamp and this clump's metadata both link by it, and it is unique by
            // construction); the uniquified name is only a courtesy, so a tileset does not fill up with six
            // clumps called "Tile". Duplicate names are harmless now — nothing resolves through them.
            var clump = new Clump { displayName = TilesetForge.UniqueClumpName(set, nextName), gridPos = anchor };
            clump.EnsureId();
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
            tilesetView?.RefreshCells();
            stage?.Refresh();
            UpdateStatus();
            statusLine.text = $"Plucked clump '{clump.displayName}' ({clump.cells.Count} tile(s)). " + statusLine.text;
            sheetDragCells = null;
        }

        /// Drop every cached RenderPreviewTexture copy. For pixel-level atlas edits (flips): the live
        /// sprites reimport in place, but these baked thumbs would keep showing the pre-edit pixels.
        internal void InvalidateTileThumbs() => LauAssetGridGUI.ClearCache(tileThumbs);

        internal Texture2D TileThumb(LevelTile tile)
        {
            if (tile == null) return null;
            if (tileThumbs.TryGetValue(tile, out var thumb) && thumb != null) return thumb;
            thumb = tile.RenderPreviewTexture();
            if (thumb != null) thumb.hideFlags = HideFlags.HideAndDontSave;
            tileThumbs[tile] = thumb;
            return thumb;
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
            void EnsureChecker() => TilesetGridView.StyleAsChecker(checker, ref checkerTex);

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
                      Mathf.Sin((float)(EditorApplication.timeSinceStartup * w.gridOptions.oscSpeed * Mathf.PI * 2.0))
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
