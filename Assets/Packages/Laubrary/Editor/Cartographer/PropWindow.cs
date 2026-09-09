using System.Collections.Generic;
using System.Linq;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.Cartographer.Editor
{
    /// THE CLUMP EDITOR — authors one Prop: paint its cells, tag it, name its spots, curate the biome that
    /// offers it. The LEVEL editor is CartographerWindow; this window is its satellite, opened from the
    /// Props box. (Historically this window WAS "Cartographer" and kept the level as a field inside a box —
    /// the exact subject inversion the 2026-07-31 redesign exists to fix. Parts of it are still being
    /// harvested: the stamper interaction is porting to the new window, and the legacy scene-level plumbing
    /// here dies when CartographerLevel is retired.)
    /// Left = the palette, target layer, tags and spots; right = the paint grid, which draws the prop's real
    /// tile sprites so what you paint is what the stamp will place.
    ///
    /// Follows ChoreographerWindow's shape (the UI Toolkit pilot): ZuiAssetWindow for the asset toolbar and
    /// thumbnail browser, a Painter2D custom element for the stage, and one Dial() helper every data edit
    /// routes through so the whole window is undoable.
    public partial class PropWindow : ZuiAssetWindow<Prop>
    {
        // No menu item on purpose: the tool's main entry is CartographerWindow (the LEVEL editor), and this
        // window is reached from its Props box — the fix for the old "the level editor asks which prop you
        // want" defect.
        /// Jump straight into one prop's editor — the entry point the Cartographer window's Edit button uses.
        public static void OpenFor(Prop prop)
        {
            var w = GetWindow<PropWindow>("Prop Editor");
            if (prop != null) w.SetAsset(prop);
        }

        Prop prop => Current;

        protected override string TypeLabel => "Prop";
        protected override string NewAssetName => "Prop";
        protected override string DefaultFolder => "Assets/Cartographer/Props";

        // palette + brush
        [SerializeField] CartographerBiome paletteSource;
        [SerializeField] List<TileBase> palette = new();
        int brushIndex;
        string targetLayer = CartographerLevel.TerrainLayer;
        bool erasing;

        // grid view
        int gridW = 8, gridH = 8;

        PropStage stage;
        VisualElement paletteRow, layerRow, tagList, spotList, levelBox, biomeBox;

        // Thumbnail cache for the LauAsset picker rows (biome/prop/tag/recipe fields). Cleared on disable and
        // when the edited asset changes — the same lifecycle every other LauAssetElement host uses.
        readonly Dictionary<Object, Texture2D> _fieldThumbs = new();

        protected override void OnEnable() { base.OnEnable(); OnEnableStamper(); }
        protected override void OnDisable() { OnDisableStamper(); base.OnDisable(); LauAssetGridGUI.ClearCache(_fieldThumbs); }

        protected override Texture2D RenderThumbnail(Prop item) => item != null ? item.RenderPreviewTexture() : null;

        protected override void InitializeNewAsset(Prop item)
        {
            item.displayName = item.name;
        }

        protected override void OnAssetChanged()
        {
            // Re-seed rather than accumulate: a palette still holding the previous prop's tiles reads as if
            // this prop used them.
            brushIndex = 0;
            palette.Clear();
            LauAssetGridGUI.ClearCache(_fieldThumbs);
        }

        // ── mutation helper — every data edit goes through here ───────────────
        /// Undoable, dirties the asset, repaints the grid and refreshes the browser thumbnail.
        void Dial(string undoLabel, System.Action apply)
        {
            if (prop == null) return;
            Undo.RecordObject(prop, undoLabel);
            apply();
            EditorUtility.SetDirty(prop);
            stage?.Refresh();
        }

        protected override void BuildAsset(VisualElement root, Prop asset)
        {
            root.style.flexGrow = 1f;

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.alignItems = Align.Stretch;
            split.style.minHeight = 0f;   // flexbox: content height must not become a floor, or the column overflows
            root.Add(split);

            var left = new VisualElement();
            left.style.width = 330f;
            left.style.flexShrink = 0f;
            left.style.marginRight = 4f;
            left.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            BuildControls(scroll.contentContainer);
            left.Add(scroll);
            split.Add(left);

            stage = new PropStage(this);
            split.Add(stage);
            stage.Refresh();
        }

        // ── left: palette, brush, tags, spots ─────────────────────────────────
        void BuildControls(VisualElement root)
        {
            root.Add(Z.Field("Name", "Name shown in browsers and pickers. Independent of the asset's file name.",
                Z.TextInput(prop.displayName, "Name shown in browsers and pickers.",
                    v => Dial("Rename prop", () => prop.displayName = v), 190f)));

            root.Add(Z.VSpace());

            // Palette — tiles you can paint with, sourced from a biome or added one at a time.
            paletteRow = new VisualElement();
            var paletteBox = Z.Box("Palette", "The tiles this prop can be painted with. Pull a biome's tiles in, or add one directly.",
                Z.Field("From biome", "Fills the palette with this biome's terrain tiles. The prop is not bound to the biome.",
                    LauAssetElement.Build(paletteSource,
                        picked => { paletteSource = picked as CartographerBiome; RebuildPalette(); RebuildBiomeBox(); },
                        typeof(CartographerBiome), _fieldThumbs, "Biome", "Assets/Cartographer/Biomes",
                        "Biome whose terrain tiles fill the palette.")),
                Z.Field("Add tile", "Adds a single tile to the palette without going through a biome.",
                    Z.Object<TileBase>(null, "Tile to add to the palette.", v =>
                    {
                        if (v != null && !palette.Contains(v)) { palette.Add(v); RebuildPalette(); }
                    }, 180f)),
                paletteRow);
            root.Add(paletteBox);
            RebuildPalette();   // seeds from the biome (if any) plus whatever tiles this prop already uses

            biomeBox = new VisualElement();
            root.Add(Z.Box("Biome", "Edit the biome selected above — what may appear in it, and how it looks.", biomeBox));
            RebuildBiomeBox();

            // Brush: which layer painted cells land on, and paint-vs-erase.
            layerRow = new VisualElement();
            root.Add(Z.Box("Brush", "What a click on the grid does, and which layer it writes to.",
                layerRow,
                Z.MiniRadio(erasing ? 1 : 0, new[] { "Paint", "Erase" },
                    "Paint writes the selected tile to the target layer; Erase clears that layer's cell.",
                    i => erasing = i == 1)));
            RebuildLayers();

            root.Add(Z.Box("Grid", "Size of the visible painting area, in cells. Cells outside it are kept but not shown.",
                Z.MicroSlider("Width", gridW, 1, 32, "Visible grid width in cells.",
                    v => { gridW = Mathf.RoundToInt(v); stage?.Refresh(); }, 150f, decimals: 0),
                Z.MicroSlider("Height", gridH, 1, 32, "Visible grid height in cells.",
                    v => { gridH = Mathf.RoundToInt(v); stage?.Refresh(); }, 150f, decimals: 0)));

            tagList = new VisualElement();
            root.Add(Z.Box("Tile tags", "Gameplay labels this prop carries. Cartographer stores them; the game decides what they mean.",
                tagList,
                Z.Button("+ Add tag", "Append an empty tag slot.",
                    () => Dial("Add prop tag", () => prop.tags.Add(null)))));
            RebuildTags();

            spotList = new VisualElement();
            root.Add(Z.Box("Spots", "Named points this prop publishes to the level, for gameplay and scripted sequences to find by name.",
                spotList,
                Z.Button("+ Add spot", "Append a named point at the prop's origin.",
                    () => Dial("Add prop spot", () => prop.spots.Add(new PropSpot())))));
            RebuildSpots();

            levelBox = new VisualElement();
            root.Add(Z.Box("Level", "The level in the open scene this prop is stamped into.", levelBox));
            RebuildLevelBox();
        }

        void RebuildPalette()
        {
            if (paletteSource != null)
                foreach (var t in paletteSource.terrainTiles)
                    if (t != null && !palette.Contains(t)) palette.Add(t);

            // Anything already used by the prop belongs in the palette too, so an existing asset is editable
            // the moment it opens rather than needing its own tiles re-added by hand.
            if (prop?.cells != null)
                foreach (var c in prop.cells)
                    if (c?.tile != null && !palette.Contains(c.tile)) palette.Add(c.tile);

            if (paletteRow == null) return;
            paletteRow.Clear();
            paletteRow.style.flexDirection = FlexDirection.Row;
            paletteRow.style.flexWrap = Wrap.Wrap;

            for (int i = 0; i < palette.Count; i++)
            {
                int idx = i;
                var sprite = CartographerPreview.SpriteOf(palette[i]);
                var swatch = new Button(() => { brushIndex = idx; RebuildPalette(); })
                {
                    tooltip = palette[i] != null ? palette[i].name : "(missing tile)"
                };
                swatch.style.width = 36f;
                swatch.style.height = 36f;
                swatch.style.marginRight = 2f;
                swatch.style.marginBottom = 2f;
                if (sprite != null && sprite.texture != null)
                {
                    swatch.style.backgroundImage = Background.FromTexture2D(sprite.texture);
                    swatch.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                }
                if (idx == brushIndex)
                {
                    swatch.style.borderTopWidth = swatch.style.borderBottomWidth =
                        swatch.style.borderLeftWidth = swatch.style.borderRightWidth = 2f;
                    var c = new Color(1f, 0.75f, 0.2f);
                    swatch.style.borderTopColor = swatch.style.borderBottomColor =
                        swatch.style.borderLeftColor = swatch.style.borderRightColor = c;
                }
                paletteRow.Add(swatch);
            }

            if (palette.Count == 0)
                paletteRow.Add(Z.Text("No tiles yet — pick a biome or add a tile above.", ZuiText.Subtle,
                    "The palette is empty, so painting has nothing to place."));
        }

        /// Inline editor for the biome picked in the Palette box. Deliberately NOT its own window: a second
        /// window would need its own menu entry, and a biome is a short enough asset that it reads better beside
        /// the palette it feeds than in a tool of its own.
        void RebuildBiomeBox()
        {
            if (biomeBox == null) return;
            biomeBox.Clear();

            if (paletteSource == null)
            {
                biomeBox.Add(Z.Text("Pick a biome above to edit it.", ZuiText.Subtle,
                    "The Biome box edits whichever biome the palette is drawing from."));
                return;
            }

            var b = paletteSource;
            void BiomeEdit(string label, System.Action apply)
            {
                Undo.RecordObject(b, label);
                apply();
                EditorUtility.SetDirty(b);
            }

            biomeBox.Add(Z.Field("Name", "Name shown in browsers and pickers.",
                Z.TextInput(b.displayName, "Name shown in browsers and pickers.",
                    v => BiomeEdit("Rename biome", () => b.displayName = v), 160f)));

            biomeBox.Add(Z.Field("Tint", "Multiplied into this biome's tiles when the level is built.",
                Z.Color(b.tint, "Biome tint.", v => BiomeEdit("Set biome tint", () => b.tint = v), 130f)));

            // Terrain tiles — the pool generation fills ground and walls from.
            var tileRows = new VisualElement();
            for (int i = 0; i < b.terrainTiles.Count; i++)
            {
                int idx = i;
                tileRows.Add(Z.Row(
                    Z.Object<TileBase>(b.terrainTiles[idx], "A tile generation may use here.",
                        v => BiomeEdit("Set biome tile", () => b.terrainTiles[idx] = v), 170f),
                    Z.Button("×", "Remove this tile from the biome.",
                        () => { BiomeEdit("Remove biome tile", () => b.terrainTiles.RemoveAt(idx)); RebuildBiomeBox(); }).W(24f)));
            }
            biomeBox.Add(Z.Box("Terrain tiles", "The tiles generation may use to fill ground and walls here.",
                tileRows,
                Z.Button("+ Add tile", "Append an empty tile slot.",
                    () => { BiomeEdit("Add biome tile", () => b.terrainTiles.Add(null)); RebuildBiomeBox(); })));

            // Allowed props — a prop not listed here is never placed, however the generator is tuned.
            var propRows = new VisualElement();
            for (int i = 0; i < b.props.Count; i++)
            {
                int idx = i;
                propRows.Add(Z.Row(
                    LauAssetElement.Build(b.props[idx],
                        picked => { BiomeEdit("Set biome prop", () => b.props[idx] = picked as Prop); RebuildBiomeBox(); },
                        typeof(Prop), _fieldThumbs, "Prop", "Assets/Cartographer/Props",
                        "A prop allowed to appear in this biome."),
                    Z.Button("×", "Remove this prop from the biome.",
                        () => { BiomeEdit("Remove biome prop", () => b.props.RemoveAt(idx)); RebuildBiomeBox(); }).W(24f)));
            }
            biomeBox.Add(Z.Box("Props", "Only these props may be placed in this biome.",
                propRows,
                Z.Row(
                    Z.Button("+ Add prop", "Append an empty prop slot.",
                        () => { BiomeEdit("Add biome prop", () => b.props.Add(null)); RebuildBiomeBox(); }),
                    Z.Button("+ This prop", "Add the prop currently open in this window.",
                        () =>
                        {
                            if (prop == null || b.props.Contains(prop)) return;
                            BiomeEdit("Add biome prop", () => b.props.Add(prop));
                            RebuildBiomeBox();
                        }))));
        }

        void RebuildLayers()
        {
            if (layerRow == null) return;
            layerRow.Clear();

            var names = KnownLayers();
            int cur = Mathf.Max(0, System.Array.IndexOf(names, targetLayer));
            layerRow.Add(Z.Field("Layer", "Which of the level's named layers painted cells are written to.",
                Z.MiniRadio(cur, names, "Target layer for painted cells.", i => targetLayer = names[i], wrap: true)));

            layerRow.Add(Z.Field("New layer", "Adds a layer name, for a level that uses more than the usual two.",
                Z.TextInput("", "Type a layer name and press Enter to start painting onto it.", v =>
                {
                    if (!string.IsNullOrWhiteSpace(v)) { targetLayer = v.Trim(); RebuildLayers(); }
                }, 150f)));
        }

        /// The conventional two layers, plus every layer the target level declares, plus any others this prop
        /// already writes to. The level's layers matter: painting onto a layer the level does not have drops
        /// those cells (with a warning), so the brush should offer exactly what is actually paintable.
        string[] KnownLayers()
        {
            var set = new List<string> { CartographerLevel.TerrainLayer, "Structures" };

            if (level != null)
                foreach (var l in level.layers)
                    if (l != null && !string.IsNullOrEmpty(l.layerName) && !set.Contains(l.layerName)) set.Add(l.layerName);

            if (prop?.cells != null)
                foreach (var c in prop.cells)
                    if (c != null && !string.IsNullOrEmpty(c.layer) && !set.Contains(c.layer)) set.Add(c.layer);

            if (!set.Contains(targetLayer)) set.Add(targetLayer);
            return set.ToArray();
        }

        void RebuildTags()
        {
            if (tagList == null) return;
            tagList.Clear();
            for (int i = 0; i < prop.tags.Count; i++)
            {
                int idx = i;
                tagList.Add(Z.Row(
                    LauAssetElement.Build(prop.tags[i],
                        picked => { Dial("Set prop tag", () => prop.tags[idx] = picked as TileTag); RebuildTags(); },
                        typeof(TileTag), _fieldThumbs, "Tag", "Assets/Cartographer/Tags",
                        "Gameplay label carried by this prop."),
                    Z.Button("×", "Remove this tag.",
                        () => { Dial("Remove prop tag", () => prop.tags.RemoveAt(idx)); RebuildTags(); }).W(24f)));
            }
        }

        void RebuildSpots()
        {
            if (spotList == null) return;
            spotList.Clear();
            for (int i = 0; i < prop.spots.Count; i++)
            {
                int idx = i;
                var s = prop.spots[idx];
                spotList.Add(Z.Row(
                    Z.TextInput(s.spotName, "The name a sequence or gameplay script looks this point up by.",
                        v => Dial("Rename spot", () => s.spotName = v), 110f),
                    Z.Float(s.offset.x, "Offset from the prop origin, in cells, along X.",
                        v => { Dial("Move spot", () => s.offset.x = v); }, 46f),
                    Z.Float(s.offset.y, "Offset from the prop origin, in cells, along Y.",
                        v => { Dial("Move spot", () => s.offset.y = v); }, 46f),
                    Z.Button("×", "Remove this spot.",
                        () => { Dial("Remove spot", () => prop.spots.RemoveAt(idx)); RebuildSpots(); }).W(24f)));
            }
        }

        // ── painting ──────────────────────────────────────────────────────────
        /// Write the current brush into `cell`, or clear it when erasing. One cell per layer: painting the same
        /// layer twice replaces rather than stacking, which is what makes click-drag painting behave.
        internal void PaintCell(Vector2Int cell)
        {
            if (prop == null) return;

            int existing = prop.cells.FindIndex(c => c != null && c.offset == cell && c.layer == targetLayer);

            if (erasing)
            {
                if (existing >= 0) Dial("Erase cell", () => prop.cells.RemoveAt(existing));
                return;
            }

            if (brushIndex < 0 || brushIndex >= palette.Count) return;
            var tile = palette[brushIndex];
            if (tile == null) return;

            if (existing >= 0)
            {
                if (prop.cells[existing].tile == tile) return;   // no-op: don't spam the undo stack while dragging
                Dial("Paint cell", () => prop.cells[existing].tile = tile);
            }
            else
            {
                Dial("Paint cell", () => prop.cells.Add(new PropCell { offset = cell, tile = tile, layer = targetLayer }));
            }
        }

        internal int GridW => gridW;
        internal int GridH => gridH;
        internal Prop CurrentProp => prop;

        // ── right: the paint grid (Painter2D + an Image pool for the tile sprites) ────────────
        class PropStage : VisualElement
        {
            readonly PropWindow w;
            readonly List<Image> tilePool = new();

            // Painter2D content draws BENEATH an element's children, so the tile Images would bury anything the
            // stage painted. Markers that must stay readable go on this overlay child instead, kept in front.
            readonly VisualElement overlay;

            float cellPx = 32f;
            Vector2 origin;          // pixel position of cell (0,0)'s bottom-left corner
            bool painting;
            Vector2Int lastPainted = new(int.MinValue, int.MinValue);

            public PropStage(PropWindow window)
            {
                w = window;
                AddToClassList("zui-stage");
                style.flexGrow = 1f;
                style.overflow = Overflow.Hidden;
                tooltip = "The paint grid. Click or drag to paint the selected tile; hold Alt (or use the right " +
                          "button) to erase. Spots show as orange rings.";

                generateVisualContent += Paint;
                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<GeometryChangedEvent>(_ => Refresh());

                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.style.position = Position.Absolute;
                overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0f;
                overlay.generateVisualContent += PaintOverlay;
                Add(overlay);
            }

            public void Refresh()
            {
                MarkDirtyRepaint();
                SyncTileImages();
                overlay?.MarkDirtyRepaint();
            }

            void Layout()
            {
                float availW = Mathf.Max(1f, contentRect.width - 16f);
                float availH = Mathf.Max(1f, contentRect.height - 16f);
                cellPx = Mathf.Max(6f, Mathf.Min(availW / Mathf.Max(1, w.GridW), availH / Mathf.Max(1, w.GridH)));
                float gridPxW = cellPx * w.GridW, gridPxH = cellPx * w.GridH;
                origin = new Vector2((contentRect.width - gridPxW) * 0.5f, (contentRect.height + gridPxH) * 0.5f);
            }

            /// Cell (cx,cy) → its top-left pixel. Y is flipped: cell Y grows upward, pixels grow downward.
            Vector2 CellToPixel(int cx, int cy) => new(origin.x + cx * cellPx, origin.y - (cy + 1) * cellPx);

            bool PixelToCell(Vector2 p, out Vector2Int cell)
            {
                int cx = Mathf.FloorToInt((p.x - origin.x) / cellPx);
                int cy = Mathf.FloorToInt((origin.y - p.y) / cellPx);
                cell = new Vector2Int(cx, cy);
                return cx >= 0 && cy >= 0 && cx < w.GridW && cy < w.GridH;
            }

            /// One Image per visible painted cell, drawn under the Painter2D overlay. Painter2D cannot draw a
            /// texture, so the real tile sprites are pooled child elements — the same trick the Choreographer
            /// stage uses for its sample sprites.
            void SyncTileImages()
            {
                var c = w.CurrentProp;
                Layout();

                var visible = new List<PropCell>();
                if (c?.cells != null)
                    foreach (var cell in c.cells)
                        if (cell != null && cell.tile != null &&
                            cell.offset.x >= 0 && cell.offset.y >= 0 &&
                            cell.offset.x < w.GridW && cell.offset.y < w.GridH)
                            visible.Add(cell);

                // Terrain first so Structures composite on top, matching how the level stacks its layers.
                visible = visible.OrderBy(v => v.layer == CartographerLevel.TerrainLayer ? 0 : 1).ToList();

                while (tilePool.Count < visible.Count)
                {
                    var img = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
                    img.style.position = Position.Absolute;
                    tilePool.Add(img);
                    Add(img);
                }

                for (int i = 0; i < tilePool.Count; i++)
                {
                    var img = tilePool[i];
                    if (i >= visible.Count) { img.style.display = DisplayStyle.None; continue; }

                    var sprite = CartographerPreview.SpriteOf(visible[i].tile);
                    if (sprite == null) { img.style.display = DisplayStyle.None; continue; }

                    var tl = CellToPixel(visible[i].offset.x, visible[i].offset.y);
                    img.style.display = DisplayStyle.Flex;
                    img.sprite = sprite;
                    img.style.left = tl.x;
                    img.style.top = tl.y;
                    img.style.width = cellPx;
                    img.style.height = cellPx;
                }

                overlay?.BringToFront();   // the pool grows behind it; keep the markers on top
            }

            void Paint(MeshGenerationContext ctx)
            {
                Layout();
                var p = ctx.painter2D;
                var c = w.CurrentProp;

                // grid lines
                p.strokeColor = new Color(1f, 1f, 1f, 0.12f);
                p.lineWidth = 1f;
                p.BeginPath();
                for (int x = 0; x <= w.GridW; x++)
                {
                    p.MoveTo(new Vector2(origin.x + x * cellPx, origin.y));
                    p.LineTo(new Vector2(origin.x + x * cellPx, origin.y - w.GridH * cellPx));
                }
                for (int y = 0; y <= w.GridH; y++)
                {
                    p.MoveTo(new Vector2(origin.x, origin.y - y * cellPx));
                    p.LineTo(new Vector2(origin.x + w.GridW * cellPx, origin.y - y * cellPx));
                }
                p.Stroke();
            }

            /// Markers that must stay readable on top of painted tiles.
            void PaintOverlay(MeshGenerationContext ctx)
            {
                Layout();
                var p = ctx.painter2D;
                var c = w.CurrentProp;

                // origin cell, so (0,0) is findable at any grid size
                p.strokeColor = new Color(0.4f, 0.8f, 1f, 0.85f);
                p.lineWidth = 2f;
                p.BeginPath();
                var o = CellToPixel(0, 0);
                p.MoveTo(o); p.LineTo(new Vector2(o.x + cellPx, o.y));
                p.LineTo(new Vector2(o.x + cellPx, o.y + cellPx)); p.LineTo(new Vector2(o.x, o.y + cellPx));
                p.ClosePath();
                p.Stroke();

                if (c?.spots == null) return;
                foreach (var s in c.spots)
                {
                    if (s == null) continue;
                    var centre = new Vector2(origin.x + (s.offset.x + 0.5f) * cellPx,
                                             origin.y - (s.offset.y + 0.5f) * cellPx);
                    // dark backing ring first, so the marker reads against any tile colour underneath
                    p.strokeColor = new Color(0f, 0f, 0f, 0.7f);
                    p.lineWidth = 5f;
                    p.BeginPath(); p.Arc(centre, cellPx * 0.28f, 0f, 360f); p.Stroke();

                    p.strokeColor = new Color(1f, 0.65f, 0.15f, 1f);
                    p.lineWidth = 2f;
                    p.BeginPath(); p.Arc(centre, cellPx * 0.28f, 0f, 360f); p.Stroke();
                }
            }

            void OnPointerDown(PointerDownEvent e)
            {
                if (!PixelToCell(e.localPosition, out var cell)) return;
                painting = true;
                lastPainted = new Vector2Int(int.MinValue, int.MinValue);
                this.CapturePointer(e.pointerId);
                ApplyAt(cell, e.altKey || e.button == 1);
                e.StopPropagation();
            }

            void OnPointerMove(PointerMoveEvent e)
            {
                if (!painting) return;
                if (!PixelToCell(e.localPosition, out var cell)) return;
                ApplyAt(cell, e.altKey || e.button == 1);
                e.StopPropagation();
            }

            void OnPointerUp(PointerUpEvent e)
            {
                if (!painting) return;
                painting = false;
                this.ReleasePointer(e.pointerId);
                e.StopPropagation();
            }

            /// Paint one cell, skipping repeats so a drag across a single cell doesn't fill the undo stack.
            void ApplyAt(Vector2Int cell, bool eraseOverride)
            {
                if (cell == lastPainted) return;
                lastPainted = cell;

                bool wasErasing = w.erasing;
                if (eraseOverride) w.erasing = true;
                w.PaintCell(cell);
                w.erasing = wasErasing;

                Refresh();
            }
        }
    }
}
