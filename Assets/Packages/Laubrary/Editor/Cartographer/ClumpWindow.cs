using System.Collections.Generic;
using System.Linq;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace Laubrary.Cartographer.Editor
{
    /// Clump Editor: paint a Clump's cells on a grid instead of hand-typing offsets into a list.
    /// Left = the palette, target layer, tags and spots; right = the paint grid, which draws the clump's real
    /// tile sprites so what you paint is what the stamp will place.
    ///
    /// Follows ChoreographerWindow's shape (the UI Toolkit pilot): ZuiAssetWindow for the asset toolbar and
    /// thumbnail browser, a Painter2D custom element for the stage, and one Dial() helper every data edit
    /// routes through so the whole window is undoable.
    public class ClumpWindow : ZuiAssetWindow<Clump>
    {
        [MenuItem("Laubrary/Cartographer/Clump Editor")]
        public static void Open() => GetWindow<ClumpWindow>("Clump Editor");

        /// Jump straight into one clump's editor — the entry point a future Biome window's Edit button uses.
        public static void OpenFor(Clump clump)
        {
            var w = GetWindow<ClumpWindow>("Clump Editor");
            if (clump != null) w.SetAsset(clump);
        }

        Clump clump => Current;

        protected override string TypeLabel => "Clump";
        protected override string NewAssetName => "Clump";
        protected override string DefaultFolder => "Assets/Cartographer/Clumps";

        // palette + brush
        [SerializeField] CartographerBiome paletteSource;
        [SerializeField] List<TileBase> palette = new();
        int brushIndex;
        string targetLayer = CartographerLevel.TerrainLayer;
        bool erasing;

        // grid view
        int gridW = 8, gridH = 8;

        ClumpStage stage;
        VisualElement paletteRow, layerRow, tagList, spotList;

        protected override Texture2D RenderThumbnail(Clump item) => item != null ? item.RenderPreviewTexture() : null;

        protected override void InitializeNewAsset(Clump item)
        {
            item.displayName = item.name;
        }

        protected override void OnAssetChanged()
        {
            // Re-seed rather than accumulate: a palette still holding the previous clump's tiles reads as if
            // this clump used them.
            brushIndex = 0;
            palette.Clear();
        }

        // ── mutation helper — every data edit goes through here ───────────────
        /// Undoable, dirties the asset, repaints the grid and refreshes the browser thumbnail.
        void Dial(string undoLabel, System.Action apply)
        {
            if (clump == null) return;
            Undo.RecordObject(clump, undoLabel);
            apply();
            EditorUtility.SetDirty(clump);
            stage?.Refresh();
        }

        protected override void BuildAsset(VisualElement root, Clump asset)
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

            stage = new ClumpStage(this);
            split.Add(stage);
            stage.Refresh();
        }

        // ── left: palette, brush, tags, spots ─────────────────────────────────
        void BuildControls(VisualElement root)
        {
            root.Add(Z.Field("Name", "Name shown in browsers and pickers. Independent of the asset's file name.",
                Z.TextInput(clump.displayName, "Name shown in browsers and pickers.",
                    v => Dial("Rename clump", () => clump.displayName = v), 190f)));

            root.Add(Z.VSpace());

            // Palette — tiles you can paint with, sourced from a biome or added one at a time.
            paletteRow = new VisualElement();
            var paletteBox = Z.Box("Palette", "The tiles this clump can be painted with. Pull a biome's tiles in, or add one directly.",
                Z.Field("From biome", "Fills the palette with this biome's terrain tiles. The clump is not bound to the biome.",
                    Z.Object<CartographerBiome>(paletteSource, "Biome whose terrain tiles fill the palette.",
                        v => { paletteSource = v; RebuildPalette(); }, 180f)),
                Z.Field("Add tile", "Adds a single tile to the palette without going through a biome.",
                    Z.Object<TileBase>(null, "Tile to add to the palette.", v =>
                    {
                        if (v != null && !palette.Contains(v)) { palette.Add(v); RebuildPalette(); }
                    }, 180f)),
                paletteRow);
            root.Add(paletteBox);
            RebuildPalette();   // seeds from the biome (if any) plus whatever tiles this clump already uses

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
            root.Add(Z.Box("Tags", "Gameplay labels this clump carries. Cartographer stores them; the game decides what they mean.",
                tagList,
                Z.Button("+ Add tag", "Append an empty tag slot.",
                    () => Dial("Add clump tag", () => clump.tags.Add(null)))));
            RebuildTags();

            spotList = new VisualElement();
            root.Add(Z.Box("Spots", "Named points this clump publishes to the level, for gameplay and scripted sequences to find by name.",
                spotList,
                Z.Button("+ Add spot", "Append a named point at the clump's origin.",
                    () => Dial("Add clump spot", () => clump.spots.Add(new ClumpSpot())))));
            RebuildSpots();
        }

        void RebuildPalette()
        {
            if (paletteSource != null)
                foreach (var t in paletteSource.terrainTiles)
                    if (t != null && !palette.Contains(t)) palette.Add(t);

            // Anything already used by the clump belongs in the palette too, so an existing asset is editable
            // the moment it opens rather than needing its own tiles re-added by hand.
            if (clump?.cells != null)
                foreach (var c in clump.cells)
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

        /// The conventional two layers, plus any others this clump already writes to.
        string[] KnownLayers()
        {
            var set = new List<string> { CartographerLevel.TerrainLayer, "Structures" };
            if (clump?.cells != null)
                foreach (var c in clump.cells)
                    if (c != null && !string.IsNullOrEmpty(c.layer) && !set.Contains(c.layer)) set.Add(c.layer);
            if (!set.Contains(targetLayer)) set.Add(targetLayer);
            return set.ToArray();
        }

        void RebuildTags()
        {
            if (tagList == null) return;
            tagList.Clear();
            for (int i = 0; i < clump.tags.Count; i++)
            {
                int idx = i;
                tagList.Add(Z.Row(
                    Z.Object<ClumpTag>(clump.tags[i], "Gameplay label carried by this clump.",
                        v => Dial("Set clump tag", () => clump.tags[idx] = v), 200f),
                    Z.Button("×", "Remove this tag.",
                        () => { Dial("Remove clump tag", () => clump.tags.RemoveAt(idx)); RebuildTags(); }).W(24f)));
            }
        }

        void RebuildSpots()
        {
            if (spotList == null) return;
            spotList.Clear();
            for (int i = 0; i < clump.spots.Count; i++)
            {
                int idx = i;
                var s = clump.spots[idx];
                spotList.Add(Z.Row(
                    Z.TextInput(s.spotName, "The name a sequence or gameplay script looks this point up by.",
                        v => Dial("Rename spot", () => s.spotName = v), 110f),
                    Z.Float(s.offset.x, "Offset from the clump origin, in cells, along X.",
                        v => { Dial("Move spot", () => s.offset.x = v); }, 46f),
                    Z.Float(s.offset.y, "Offset from the clump origin, in cells, along Y.",
                        v => { Dial("Move spot", () => s.offset.y = v); }, 46f),
                    Z.Button("×", "Remove this spot.",
                        () => { Dial("Remove spot", () => clump.spots.RemoveAt(idx)); RebuildSpots(); }).W(24f)));
            }
        }

        // ── painting ──────────────────────────────────────────────────────────
        /// Write the current brush into `cell`, or clear it when erasing. One cell per layer: painting the same
        /// layer twice replaces rather than stacking, which is what makes click-drag painting behave.
        internal void PaintCell(Vector2Int cell)
        {
            if (clump == null) return;

            int existing = clump.cells.FindIndex(c => c != null && c.offset == cell && c.layer == targetLayer);

            if (erasing)
            {
                if (existing >= 0) Dial("Erase cell", () => clump.cells.RemoveAt(existing));
                return;
            }

            if (brushIndex < 0 || brushIndex >= palette.Count) return;
            var tile = palette[brushIndex];
            if (tile == null) return;

            if (existing >= 0)
            {
                if (clump.cells[existing].tile == tile) return;   // no-op: don't spam the undo stack while dragging
                Dial("Paint cell", () => clump.cells[existing].tile = tile);
            }
            else
            {
                Dial("Paint cell", () => clump.cells.Add(new ClumpCell { offset = cell, tile = tile, layer = targetLayer }));
            }
        }

        internal int GridW => gridW;
        internal int GridH => gridH;
        internal Clump CurrentClump => clump;

        // ── right: the paint grid (Painter2D + an Image pool for the tile sprites) ────────────
        class ClumpStage : VisualElement
        {
            readonly ClumpWindow w;
            readonly List<Image> tilePool = new();

            // Painter2D content draws BENEATH an element's children, so the tile Images would bury anything the
            // stage painted. Markers that must stay readable go on this overlay child instead, kept in front.
            readonly VisualElement overlay;

            float cellPx = 32f;
            Vector2 origin;          // pixel position of cell (0,0)'s bottom-left corner
            bool painting;
            Vector2Int lastPainted = new(int.MinValue, int.MinValue);

            public ClumpStage(ClumpWindow window)
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
                var c = w.CurrentClump;
                Layout();

                var visible = new List<ClumpCell>();
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
                var c = w.CurrentClump;

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
                var c = w.CurrentClump;

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
