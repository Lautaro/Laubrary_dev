using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace Laubrary.Cartographer.Editor
{
    /// The canvas half of the Cartographer window: the right pane IS the level editor — the window's one
    /// and only edit view (the Scene-view editing path was removed by decree 2026-08-03; a LevelInstance
    /// in a scene is a live READ-ONLY mirror now). The gesture model (same session, second decree):
    /// LEFT-CLICK applies the tool at the cell; LEFT-DRAG marquees a SELECTION — click inside it and the
    /// tool applies to every selected cell (the multi-edit); Line/Rect/Pick keep their press-drag-release
    /// spans; RIGHT-CLICK opens the pinnable Tool card; Alt erases and Ctrl picks from any tool; Fill
    /// flood-fills the contiguous same-content region. Ghosts and commits share the same cell-list
    /// methods, every edit lands on the ASSET through an undo record, the pane refreshes per cell, and
    /// the scene mirror (when one exists) refreshes exactly as before.
    public partial class CartographerWindow
    {
        internal enum CanvasTool { Paint, Erase, Line, Rect, Pick, Stamp, Decal, Fill }

        static readonly string[] ToolNames = { "Paint", "Erase", "Line", "Rect", "Pick", "Stamp", "Decal", "Fill" };

        /// One Phosphor glyph per tool, same order. Verified to resolve — an icon-only control with an
        /// unresolved name would draw nothing at all, so these are checked rather than guessed.
        static readonly string[] ToolIcons =
            { "paint-brush", "eraser", "line-segment", "rectangle", "eyedropper", "stamp", "sticker", "paint-bucket" };

        [SerializeField] CanvasTool tool = CanvasTool.Paint;
        [SerializeField] bool showTagOverlay;
        [SerializeField] bool showGridLines = true;
        [SerializeField] bool showPropMarkers = true;
        [SerializeField] float canvasZoom = 1f;
        [SerializeField] int stampRotation;      // quarter-turns anticlockwise
        [SerializeField] bool stampMirrorX;
        [SerializeField] int variantOverride = -1;
        [SerializeField] Sprite decalSprite;

        LevelCanvas canvas;
        Label canvasStatus;

        // gesture state
        bool dragging;                   // the left button is down on the canvas
        bool marqueeing;                 // ...and it has left its anchor cell, so the drag is a selection
        bool dragErasing;
        Vector2Int anchor;               // span / marquee start
        Vector2Int hoverCell;
        Vector2 hoverGridPos;            // fractional grid coords, for decals
        bool hoverValid;

        /// The marquee selection: the multi-edit target. Click inside it and Paint/Erase apply to every
        /// selected cell; Fill treats it as the flood domain. Transient view state, never serialized.
        readonly HashSet<Vector2Int> cellSelection = new();

        LevelInstance cachedInstance;

        // tag-overlay cache: rebuilt on toggle and after every edit, never per-repaint
        readonly List<(Vector2Int cell, List<Color> colors)> tagOverlay = new();

        /// Recompute which cells the overlay tints. Called on toggle, from Dial after edits, and at the
        /// end of a paint gesture (the per-cell fast path goes around Dial).
        internal void RefreshTagOverlay()
        {
            tagOverlay.Clear();
            if (!showTagOverlay || level == null || level.layers == null)
            {
                canvas?.RepaintOverlay();
                return;
            }

            var byCell = new Dictionary<Vector2Int, List<Color>>();
            foreach (var layer in level.layers)
            {
                if (layer == null) continue;
                foreach (var kv in level.ResolveLayer(layer))
                {
                    List<TileTag> tags = null;
                    void AddAll(List<TileTag> src)
                    {
                        if (src == null || src.Count == 0) return;
                        tags ??= new List<TileTag>();
                        foreach (var t in src) if (t != null && !tags.Contains(t)) tags.Add(t);
                    }
                    if (kv.Value.tile is LevelTile lt) AddAll(lt.tags);
                    if (kv.Value.source != null) AddAll(kv.Value.source.tags);
                    AddAll(layer.tags);
                    if (tags == null) continue;

                    if (!byCell.TryGetValue(kv.Key, out var colors))
                        byCell[kv.Key] = colors = new List<Color>();
                    foreach (var t in tags) if (!colors.Contains(t.editorColor)) colors.Add(t.editorColor);
                }
            }
            foreach (var kv in byCell) tagOverlay.Add((kv.Key, kv.Value));
            canvas?.RepaintOverlay();
        }

        internal void SetTool(CanvasTool t)
        {
            tool = t;
            toolCard?.Rebuild();   // no-op unless the card is open — it refills in place, pin intact
            canvas?.RepaintOverlay();
            UpdateCanvasStatus();
        }

        /// The scene's instance of this level, when one exists — a live READ-ONLY mirror the paint paths
        /// keep refreshed. Editing never requires one.
        LevelInstance EditInstance()
        {
            if (cachedInstance != null && cachedInstance.level == level) return cachedInstance;
            cachedInstance = SceneInstance();
            return cachedInstance;
        }

        // ── the Tool card: a right-click card on the canvas, pinnable and draggable ───────────────────
        // The chrome (frame, drag, pin, outside-click dismissal) is the shared ZuiPinCard — extracted this
        // pass from the Tileset Builder grid's actions card, which was the pattern's first home.
        ZuiPinCard toolCard;

        internal void ShowToolCard(Vector2 panelPos)
        {
            // Pass the card we already have: ZuiPinCard leaves a PINNED one exactly where the user parked
            // it and only refreshes+raises it, rather than following the pointer.
            toolCard = ZuiPinCard.Show(toolCard, rootVisualElement, panelPos, "Tool",
                "The canvas tool and its settings. Drag this bar to move the card; pin it to keep it open, " +
                "and a later right-click will leave it where you put it.",
                BuildToolCardBody);
        }

        /// Dismiss the card but KEEP the reference: a closed card still remembers whether the user pinned
        /// it, and nulling the field here would quietly reset that preference on the next right-click.
        internal void CloseToolCard() => toolCard?.Close();

        void AfterToolCardAction() => toolCard?.AfterAction();

        /// The card's content, rebuilt in place on tool switches so the card (and its pin) stays put.
        /// Setting controls keep the card open (the ZuiMenu rule); only ACTION buttons close it unpinned.
        void BuildToolCardBody(VisualElement toolCardBody)
        {
            // TWO strips of four glyphs, both wrap:false. A floating card is content-sized, so a wide row
            // widens the CARD — it must never FOLD to a narrower width (the menu rule in the UI guide), which
            // is why the split is authored as two rows rather than left to flex-wrap. Two deliberate rows of
            // four also keep the card roughly square instead of a long thin pill, and they read in pairs:
            // the four everyday tools on top, the four occasional ones beneath.
            //
            // Two MiniRadios means two radio GROUPS, so the one that does not own the current tool is handed
            // index -1 (no button lit). That is honest rather than clever: SetTool rebuilds this body, so the
            // lit button always lands in the group that actually owns the tool.
            int t = (int)tool;
            const string toolTip =
                "Paint · Erase · Line · Rect  /  Pick · Stamp · Decal · Fill.  Line and Rect are press-drag-" +
                "release. On the canvas: LEFT is the tool (drag to keep painting), RIGHT-DRAG marquees, " +
                "RIGHT-CLICK reopens this card, Alt erases, Ctrl picks, middle-drag pans.";
            var topNames = new[] { ToolNames[0], ToolNames[1], ToolNames[2], ToolNames[3] };
            var botNames = new[] { ToolNames[4], ToolNames[5], ToolNames[6], ToolNames[7] };
            var topIcons = new[] { ToolIcons[0], ToolIcons[1], ToolIcons[2], ToolIcons[3] };
            var botIcons = new[] { ToolIcons[4], ToolIcons[5], ToolIcons[6], ToolIcons[7] };
            toolCardBody.Add(Z.MiniRadio(t < 4 ? t : -1, topNames, toolTip,
                i => SetTool((CanvasTool)i), wrap: false, icons: topIcons));
            toolCardBody.Add(Z.MiniRadio(t >= 4 ? t - 4 : -1, botNames, toolTip,
                i => SetTool((CanvasTool)(i + 4)), wrap: false, icons: botIcons));

            if (tool == CanvasTool.Stamp)
            {
                toolCardBody.Add(Z.Field("Rotation", "Quarter-turns applied to the stamp, anticlockwise.",
                    Z.MiniRadio(stampRotation, new[] { "0°", "90°", "180°", "270°" },
                        "Quarter-turns applied to the stamp.", i => { stampRotation = i; canvas?.RepaintOverlay(); })));
                toolCardBody.Add(Z.ToggleButton("Mirror X", "Flip the stamp horizontally before rotating it.",
                    stampMirrorX, v => { stampMirrorX = v; canvas?.RepaintOverlay(); }));
            }

            if (tool == CanvasTool.Decal)
                toolCardBody.Add(Z.Field("Sprite", "The sprite the next click places as a decal on the active layer.",
                    Z.Object<Sprite>(decalSprite, "Sprite the Decal tool places.",
                        v => decalSprite = v, 170f)));

            // The DIALS row: the two view toggles and Variant together. Variant belongs here rather than on a
            // row of its own because it is the same KIND of thing — a switch that changes how the canvas
            // behaves without being a tool — and because a lone 46px field on its own line is wasted height.
            //
            // ALWAYS PRESENT, enabled or not (the stable-workspace rule): the card must not change shape as
            // the author moves between tools or tiles, or the control they were reaching for slides away.
            bool variantTool = tool == CanvasTool.Paint || tool == CanvasTool.Line
                            || tool == CanvasTool.Rect || tool == CanvasTool.Fill;
            var brushTile = PaintTile;
            int variantCount = brushTile?.variants?.Count ?? 0;
            bool variantUsable = variantTool && variantCount > 1;

            // NOT a multi-TILE brush — a multi-VARIANT tile. Worth saying outright, because the two are easy
            // to conflate and the control is useless until you know which one it is about.
            const string variantWhat =
                "VARIANTS are several interchangeable sprites on ONE tile (four grass tufts, three cracked " +
                "flagstones) — they are NOT the several tiles of a pattern brush. -1 lets the tile's own " +
                "policy pick one per cell; 0 and up forces that one for every cell this tool paints.  ";
            string variantTip = variantUsable
                ? variantWhat + $"'{TileName(brushTile)}' has {variantCount} of them, so 0…{variantCount - 1} are valid."
                : !variantTool
                    ? variantWhat + $"Disabled: {ToolNames[(int)tool]} paints no cells, so there is no variant to force. " +
                                    "Switch to Paint, Line, Rect or Fill."
                    : brushTile == null
                        ? variantWhat + "Disabled: no brush tile yet. Click a tile in the Tileset box."
                        : variantWhat + $"Disabled: '{TileName(brushTile)}' has only one look, so there is nothing to choose between.";

            int variantMax = Mathf.Max(0, variantCount - 1);
            var variantField = Z.Field("Variant", variantTip,
                Z.Int(variantOverride, variantTip,
                    v => variantOverride = Mathf.Clamp(v, -1, variantMax), 46f));
            variantField.SetEnabled(variantUsable);
            // The tooltip has to survive the disable — a disabled control that cannot say WHY is the thing
            // this row is trying to fix — so the wrapper carries it too.
            variantField.tooltip = variantTip;

            toolCardBody.Add(Z.Row(
                Z.ToggleButton("", "Grid lines: draw the cell lattice over the canvas. View only — it hides " +
                    "on its own when cells get too small to read.",
                    showGridLines, v => { showGridLines = v; canvas?.RepaintOverlay(); }, "grid-four"),
                Z.ToggleButton("", "Prop markers: tint the cells of props that OCCUPY the map without drawing " +
                    "anything — spawn points, triggers, procgen mutators — in the prop's own colour, so they " +
                    "can be seen and moved. A marker hides with its cell's layer.",
                    showPropMarkers, v => { showPropMarkers = v; canvas?.RepaintOverlay(); }, "eye"),
                Z.HSpace(),
                variantField));

            // The alignment aid, on the row below the view dials because it is the same KIND of thing (a
            // switch that changes how the canvas behaves) but needs its controls NAMED — see GuidesRow.
            toolCardBody.Add(GuidesRow());

            // The multi-edit actions: operate on the marquee selection. Always present (stable card
            // height per tool); enabled-ness carries the availability story. Icon-only and horizontal,
            // so the strip does not widen the card past the work it sits beside — the tooltips carry the
            // names, which is why they are written as whole sentences rather than keywords.
            int selCount = cellSelection.Count;
            var paintSel = Z.Button("", "Paint every selected cell with the brush's tile — one undo step.",
                () => { ApplySelection(false); AfterToolCardAction(); }, "paint-brush");
            var eraseSel = Z.Button("", "Clear every selected cell's paint — one undo step.",
                () => { ApplySelection(true); AfterToolCardAction(); }, "eraser");
            var clearSel = Z.Button("", "Drop the marquee selection. Esc on the canvas, or any left click, does the same.",
                () => { ClearCellSelection(); AfterToolCardAction(); }, "selection-slash");
            paintSel.SetEnabled(selCount > 0 && PaintTile != null);
            eraseSel.SetEnabled(selCount > 0);
            clearSel.SetEnabled(selCount > 0);
            toolCardBody.Add(Z.Row(paintSel, eraseSel, clearSel));

            // Variable-width content goes last in its row — the stable-workspace rule. The RATIO as well as
            // the count, because "6 cells selected" cannot tell a 3×2 block from a 6×1 strip and the shape is
            // the half you are actually looking at while dragging a marquee.
            var count = (Label)Z.Text(SelectionSummary(), ZuiText.Subtle,
                "The marquee selection the buttons above act on: its shape (columns × rows of its bounding " +
                "box) and how many cells it holds. Right-drag the canvas to make one; Esc drops it.");
            count.style.height = 15f;
            count.style.whiteSpace = WhiteSpace.NoWrap;
            count.style.overflow = Overflow.Hidden;
            toolCardBody.Add(count);
        }

        /// The marquee selection as one reserved line: its bounding-box SHAPE and its cell count. The shape is
        /// not decoration — a count alone cannot tell a 3×2 block from a 6×1 strip, and while dragging a
        /// marquee the shape is the thing being aimed. Reads "(3×2) 6 cells selected".
        internal string SelectionSummary()
        {
            int n = cellSelection.Count;
            if (n == 0) return "No selection";
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in cellSelection)
            {
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.y > maxY) maxY = c.y;
            }
            return $"({maxX - minX + 1}×{maxY - minY + 1}) {n} cell{(n == 1 ? "" : "s")} selected";
        }

        // ── applying ───────────────────────────────────────────────────────────
        /// One click, one application — strokes are gone by decree: area work is marquee + multi-edit,
        /// spans are Line/Rect, regions are Fill.
        void ApplyAt(Vector2Int cell)
        {
            if (PreviewBlocks()) return;   // the canvas is showing a throwaway clone; an edit here would vanish

            // Two tools are handled WHOLE rather than per mirror image: Fill commits through the list paths,
            // which reflect themselves, and a Decal is a free sprite at a fractional point that the mirror
            // deliberately leaves alone (it is not on the grid, so there is no boundary to reflect it about).
            if (tool == CanvasTool.Fill) { FillAt(cell, dragErasing); return; }
            if (tool == CanvasTool.Decal && !dragErasing) { PlaceDecal(); return; }

            var images = EditImages(cell);
            int group = Undo.GetCurrentGroup();
            foreach (var im in images)
            {
                if (tool == CanvasTool.Stamp && !dragErasing) ApplyStamp(im);
                else if (dragErasing) EraseAt(im.cell);
                else PaintAt(im);
            }
            // ONE UNDO STEP for the whole reflected edit. Records made in the same event already land in one
            // group, so this is belt and braces — but "Ctrl+Z left half of my mirrored wall standing" is
            // precisely the failure that must be impossible, so make it structural rather than incidental.
            if (images.Count > 1) Undo.CollapseUndoOperations(group);
        }

        /// The cells the current stamp would cover if dropped at `cell` — the bounding box of the prop's
        /// transformed footprint, or the single cell for a prop that has none. A drag advances by this
        /// rather than by one cell, so dragging a 3-wide prop lays it edge-to-edge instead of three deep.
        internal RectInt StampFootprintAt(Vector2Int cell)
        {
            var p = StampProp;
            if (p?.cells == null || p.cells.Count == 0) return new RectInt(cell.x, cell.y, 1, 1);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var cd in p.cells)
            {
                if (cd == null) continue;
                var at = cell + Prop.TransformOffset(cd.offset, stampRotation, stampMirrorX);
                if (at.x < minX) minX = at.x;
                if (at.x > maxX) maxX = at.x;
                if (at.y < minY) minY = at.y;
                if (at.y > maxY) maxY = at.y;
            }
            if (minX > maxX) return new RectInt(cell.x, cell.y, 1, 1);
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// The layer a shifted brush cell lands on: `shift` steps further FRONT in the level's
        /// back-to-front layer list, clamped. A clump's overhang says "one in front" and this resolves
        /// what that means HERE — the level's own stack decides, not the clump.
        LevelLayer ShiftedLayer(LevelLayer baseLayer, int shift)
        {
            if (shift == 0 || level == null || level.layers == null || level.layers.Count == 0) return baseLayer;
            int idx = level.layers.IndexOf(baseLayer);
            if (idx < 0) return baseLayer;
            return level.layers[Mathf.Clamp(idx + shift, 0, level.layers.Count - 1)];
        }

        /// Paint the whole BRUSH anchored at `cell` — one tile is plain painting, several are the temporary
        /// prop (a multi-selected palette pattern or a sampled region) stamped as one gesture.
        /// `im` carries WHERE (already reflected, when a mirror axis is live) and whether the brush's own
        /// arrangement is reversed getting there. An unmirrored paint is the primary image and reads exactly
        /// as it always did.
        void PaintAt(EditImage im)
        {
            var cell = im.cell;
            var layer = ActiveLayer;
            if (layer == null) { FlashCanvasStatus("Add a layer in the Layers box first — the level has none."); return; }
            if (layer.locked) { FlashCanvasStatus($"Layer '{layer.name}' is locked — unlock it in the Layers box."); return; }
            if (Brush.Count == 0)
            {
                FlashCanvasStatus(layer.tileset == null
                    ? "The active layer has no tileset — assign one in the Layers box."
                    : "Pick a tile in the palette first.");
                return;
            }

            if (!BrushFitsAt(cell))
            {
                FlashCanvasStatus($"Outside the level's {level.bounds.width}x{level.bounds.height} bounds — "
                                  + "resize it in the Level box to paint here.");
                return;
            }

            var inst = EditInstance();
            var touched = new List<(string lyr, Vector2Int at)>();

            // A CLUMP stamps as an OBJECT, not as loose tiles. Painting its cells individually would render
            // identical pixels and throw the identity away — and identity is the whole basis of metadata
            // gameplay: a level that has forgotten a clump was stamped can never consult that clump's
            // MetaMap, no matter what the map says. So the clump brush records a ClumpPlacement and lets
            // resolution derive the cells, through the ONE canonical geometry (LevelAsset.ClumpCellsOf).
            if (brushClump != null)
            {
                var set = layer.tileset;
                // MEMBERSHIP BY IDENTITY, not by name: two clumps may share a display name, so a name lookup
                // could say "yes, that's in this tileset" about a DIFFERENT clump entirely and stamp the wrong
                // pattern. The brush holds the clump itself, so the honest question is whether the tileset
                // still lists this one.
                if (set?.clumps == null || !set.clumps.Contains(brushClump))
                {
                    // The brush outlived its tileset (layer switched under it). Better to say so than to
                    // silently fall back to loose paints and produce a shelf nothing can recognise.
                    FlashCanvasStatus($"'{brushClump.displayName}' is not in the active layer's tileset — " +
                                      "switch to the layer that owns it, or pick a tile.");
                    return;
                }
                // PlaceClump mints an id for a pre-id clump, and that id is the link the placement stores — so
                // the TILESET has to persist it too, or a reload leaves the level pointing at nothing.
                if (!brushClump.HasId) { brushClump.EnsureId(); EditorUtility.SetDirty(set); }

                Undo.RecordObject(level, "Stamp clump");
                // A mirrored clump reflects for real: `mirrorX` (plus the half-turn the composition needs)
                // reverses the ARRANGEMENT of its cells, and a clump's cells are plain tiles, so nothing is
                // asked of the art that the tile model cannot give. See MirrorStamp for the composition.
                MirrorStamp(0, false, im.flipX, im.flipY, out int crot, out bool cmir);
                var placement = level.PlaceClump(set, brushClump, cell, layer.name, crot, cmir);
                EditorUtility.SetDirty(level);
                if (placement != null)
                    foreach (var (at, _, lyr) in level.ClumpCellsOf(placement)) touched.Add((lyr, at));
                foreach (var (lyr, at) in touched) inst?.RefreshCell(lyr, at);
                canvas?.RefreshCells(touched);
                return;
            }

            Undo.RecordObject(level, "Paint");
            foreach (var (off0, tile, shift) in Brush)
            {
                if (tile == null) continue;
                var off = FlipOffset(off0, im.flipX, im.flipY);
                var target = ShiftedLayer(layer, shift);
                if (variantOverride >= 0) level.SetPaint(target.name, cell + off, tile, (byte)variantOverride);
                else level.Paint(target.name, cell + off, tile);
                touched.Add((target.name, cell + off));
            }
            EditorUtility.SetDirty(level);
            foreach (var (lyr, at) in touched) inst?.RefreshCell(lyr, at);   // the scene mirror rides along
            canvas?.RefreshCells(touched);
        }

        /// Erase the active layer's paint at the cell; when there is none, remove the most recent stamped
        /// prop covering it instead (rebuilding, because two overlapping stamps share cells and erasing one
        /// by footprint would punch holes in the other — the old tool's hard-won rule).
        /// What one cell's worth of erasing actually removed.
        enum ErasedKind { Nothing, Paint, Clump, Prop }

        /// Erase whatever is SHOWING at `cell` on `layer` — and nothing else. Records no undo and refreshes
        /// nothing: the caller owns both, which is what lets a selection erase be ONE undo step and ONE
        /// refresh instead of hundreds of each.
        ///
        /// `touched` collects every cell whose content changed, INCLUDING the other cells of a removed
        /// stamp, so the caller can repaint exactly those. That list is the whole reason deleting a clump
        /// no longer costs a full level rebuild.
        ErasedKind EraseOne(Vector2Int cell, LevelLayer layer,
                            List<(string lyr, Vector2Int at)> touched, out string name)
        {
            name = null;

            if (level.paints != null)
                for (int i = level.paints.Count - 1; i >= 0; i--)
                {
                    var p = level.paints[i];
                    if (p == null || p.layer != layer.name || p.cell != cell) continue;
                    level.paints.RemoveAt(i);
                    touched.Add((layer.name, cell));
                    return ErasedKind.Paint;
                }

            // CLUMP stamps before PROP stamps, matching resolution's own precedence
            // (background < props < clumps < paints) — erase must undo whatever is actually showing, or
            // clicking a visible shelf cell would delete the prop hidden beneath it. Whole-object, because
            // unpicking a stamp cell-by-cell would punch holes in whatever overlaps it.
            foreach (var cp in level.clumpPlacements ?? new List<ClumpPlacement>())
            {
                if (cp == null) continue;
                bool hit = false;
                foreach (var (at, _, lyr) in level.ClumpCellsOf(cp))
                    if (at == cell && lyr == layer.name) { hit = true; break; }
                if (!hit) continue;

                // Collect BEFORE removing — afterwards the placement can no longer say where it was.
                foreach (var (at, _, lyr) in level.ClumpCellsOf(cp)) touched.Add((lyr, at));
                name = cp.clumpName;
                level.clumpPlacements.Remove(cp);
                return ErasedKind.Clump;
            }

            // Then PROP placements. A prop cell that DRAWS belongs to its own layer, so it may only be
            // erased from there — that is what stopped an erase on Terrain deleting a prop drawing on Walls.
            // A cell that draws NOTHING, or a prop with no cells at all (a procgen director), is not content
            // ON a layer at all; the canvas marks it whatever layer you are on, so erase reaches it from
            // anywhere. Drawing cells are matched first so nothing that renders dies from the wrong layer.
            int found = -1;
            for (int pass = 0; pass < 2 && found < 0; pass++)
                for (int i = level.placements.Count - 1; i >= 0 && found < 0; i--)
                {
                    var p = level.placements[i];
                    if (p?.prop == null) continue;
                    if (p.prop.cells == null || p.prop.cells.Count == 0)
                    {
                        if (pass == 1 && p.cell == cell) found = i;
                        continue;
                    }
                    foreach (var cd in p.prop.cells)
                    {
                        if (cd == null) continue;
                        bool draws = cd.tile != null;
                        if (pass == 0 ? !(draws && cd.layer == layer.name) : draws) continue;
                        if (p.cell + Prop.TransformOffset(cd.offset, p.rotation, p.mirrorX) == cell) { found = i; break; }
                    }
                }
            if (found < 0) return ErasedKind.Nothing;

            var doomed = level.placements[found];
            name = doomed.prop == null ? "prop"
                 : string.IsNullOrEmpty(doomed.prop.displayName) ? doomed.prop.name : doomed.prop.displayName;
            if (doomed.prop?.cells != null)
                foreach (var cd in doomed.prop.cells)
                {
                    if (cd == null) continue;
                    touched.Add((string.IsNullOrEmpty(cd.layer) ? "Terrain" : cd.layer,
                                 doomed.cell + Prop.TransformOffset(cd.offset, doomed.rotation, doomed.mirrorX)));
                }
            else touched.Add((layer.name, doomed.cell));
            level.placements.RemoveAt(found);
            return ErasedKind.Prop;
        }

        /// Push a batch of erased cells back into the canvas and the scene mirror.
        ///
        /// ☠️ Per CELL, never a whole-level rebuild. Removing a clump used to call `inst.Rebuild()` plus
        /// `canvas.RebuildContent()` — destroying and rebuilding every Tilemap and every layer texture in
        /// the level to account for a handful of cells — which is why deleting one clump made the editor
        /// hitch ("Deleting a single clump makes the UI lag. Why? Shouldnt have to.", 2026-08-04). The
        /// placement knows exactly which cells it occupied, and RefreshCells re-resolves each from scratch,
        /// so whatever was underneath correctly reappears without touching anything else.
        void RefreshErased(LevelInstance inst, List<(string lyr, Vector2Int at)> touched)
        {
            EditorUtility.SetDirty(level);
            // The marker refresh runs even for an EMPTY touched list, and must: erasing a cell-less prop
            // removes a placement while touching no cells, so an early-out on count would leave its marker
            // painted on the canvas as a ghost of something that no longer exists.
            canvas?.RefreshMarkers();
            if (touched.Count == 0) return;
            foreach (var (lyr, at) in touched) inst?.RefreshCell(lyr, at);
            canvas?.RefreshCells(touched);
        }

        void EraseAt(Vector2Int cell)
        {
            var layer = ActiveLayer;
            if (layer == null) { FlashCanvasStatus("Add a layer in the Layers box first — the level has none."); return; }
            if (layer.locked) { FlashCanvasStatus($"Layer '{layer.name}' is locked — unlock it in the Layers box."); return; }

            var inst = EditInstance();
            Undo.RecordObject(level, "Erase");
            var touched = new List<(string lyr, Vector2Int at)>();
            var kind = EraseOne(cell, layer, touched, out string name);

            if (kind == ErasedKind.Nothing)
            {
                // Silent no-ops are the thing this pane kills everywhere else — say why nothing happened.
                FlashCanvasStatus($"Nothing on '{layer.name}' here to erase.");
                return;
            }

            RefreshErased(inst, touched);
            // Removing a stamp removes ALL of it, across every layer it occupies. That is a bigger edit
            // than the one click suggests, so it announces itself.
            if (kind == ErasedKind.Clump)
                FlashCanvasStatus($"Removed clump '{name}' — a stamped clump erases as one piece.");
            else if (kind == ErasedKind.Prop)
            {
                RebuildLevelBox();
                FlashCanvasStatus($"Removed prop '{name}' — a stamped prop erases as one piece.");
            }
        }

        void PickAt(Vector2Int cell)
        {
            var layer = ActiveLayer;
            if (layer == null) { FlashCanvasStatus("Add a layer in the Layers box first — the level has none."); return; }
            if (!level.ResolveCell(layer, cell, out var rc) || rc.tile is not LevelTile lt)
            {
                FlashCanvasStatus("Nothing on the active layer here to pick.");
                return;
            }
            SetBrushSingle(lt);
            if (tool == CanvasTool.Pick) SetTool(CanvasTool.Paint);
        }

        /// The picker's marquee: sample every LevelTile in the rect from the active layer, offsets exactly
        /// as they sit in the room, and make the arrangement the brush — a temporary prop lifted straight
        /// off the level.
        void PickRegion(Vector2Int a, Vector2Int b)
        {
            var layer = ActiveLayer;
            if (layer == null) return;

            if (a == b) { PickAt(a); return; }

            var min = new Vector2Int(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
            var cells = new List<(Vector2Int off, LevelTile tile)>();
            for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
                for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
                {
                    var c = new Vector2Int(x, y);
                    if (level.ResolveCell(layer, c, out var rc) && rc.tile is LevelTile lt)
                        cells.Add((c - min, lt));
                }
            if (cells.Count == 0)
            {
                FlashCanvasStatus("No tiles in that region on the active layer.");
                return;
            }

            SetBrushSample(cells);
            SetTool(CanvasTool.Paint);
        }

        // ── the selection (multi-edit) and the flood fill ──────────────────────
        internal void ClearCellSelection()
        {
            if (cellSelection.Count == 0) return;
            cellSelection.Clear();
            OnSelectionChanged();
        }

        /// The marquee's result: every cell of the dragged rect becomes the multi-edit target.
        internal void SetCellSelection(Vector2Int a, Vector2Int b)
        {
            cellSelection.Clear();
            foreach (var c in RectCells(a, b)) cellSelection.Add(c);
            OnSelectionChanged();
        }

        void OnSelectionChanged()
        {
            toolCard?.Rebuild();   // the count and the actions' enabled-ness both track the selection
            canvas?.RepaintOverlay();
            UpdateCanvasStatus();
        }

        /// The multi-edit: apply the tool to EVERY selected cell in one undoable gesture. Deterministic
        /// order (row-major) so variant policies land the same for the same selection.
        internal void ApplySelection(bool erase)
        {
            if (cellSelection.Count == 0 || PreviewBlocks()) return;
            var cells = new List<Vector2Int>(cellSelection);
            cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            if (erase) { EraseSelection(cells); return; }
            PaintCellsSingle(cells, "Paint selection", false);
        }

        /// Erase everything the selection covers — paints AND stamps.
        ///
        /// It used to remove only paints, so a marquee dragged over a shelf cleared the loose tiles around
        /// it and left the clump sitting there ("Selecting and erasing should remove clumps under the
        /// selection as well", 2026-08-04). It now runs the SAME per-cell erase a single click runs, which
        /// is the point of sharing `EraseOne`: the rules about what a layer owns, which stamp is on top, and
        /// whole-object removal cannot drift between the two paths, because there is only one copy of them.
        ///
        /// A stamp only needs one of its cells inside the marquee to go, and it goes whole — the same rule
        /// as clicking it. Once it is gone its other cells simply find nothing, so overlapping selections
        /// cost nothing extra.
        void EraseSelection(List<Vector2Int> cells)
        {
            var layer = ActiveLayer;
            if (layer == null) { FlashCanvasStatus("Add a layer in the Layers box first — the level has none."); return; }
            if (layer.locked) { FlashCanvasStatus($"Layer '{layer.name}' is locked — unlock it in the Layers box."); return; }

            var inst = EditInstance();
            Undo.RecordObject(level, "Erase selection");   // ONE record for the whole marquee
            var touched = new List<(string lyr, Vector2Int at)>();
            int paints = 0, clumps = 0, props = 0;

            foreach (var cell in cells)
                switch (EraseOne(cell, layer, touched, out _))
                {
                    case ErasedKind.Paint: paints++; break;
                    case ErasedKind.Clump: clumps++; break;
                    case ErasedKind.Prop: props++; break;
                }

            if (paints + clumps + props == 0)
            {
                FlashCanvasStatus($"Nothing on '{layer.name}' inside the selection to erase.");
                return;
            }

            RefreshErased(inst, touched);
            if (props > 0) RebuildLevelBox();

            // Name the stamps: they reach beyond the marquee, so a count of cells alone would understate
            // what just happened.
            var said = new List<string>();
            if (paints > 0) said.Add(paints + (paints == 1 ? " cell" : " cells"));
            if (clumps > 0) said.Add(clumps + (clumps == 1 ? " clump" : " clumps"));
            if (props > 0) said.Add(props + (props == 1 ? " prop" : " props"));
            FlashCanvasStatus("Erased " + string.Join(", ", said) +
                              (clumps + props > 0 ? " — stamps erase whole, including cells outside the selection." : "."));
        }

        /// Bulk single-tile painting/erasing — the shared commit under Fill and the selection multi-edit.
        /// Deliberately paints the brush's FIRST tile per cell (classic fill semantics), not the whole
        /// pattern arrangement per cell the way Line/Rect do.
        void PaintCellsSingle(List<Vector2Int> cells, string label, bool erase)
        {
            if (!erase) cells = cells.FindAll(InBounds);   // erasing must still reach legacy out-of-bounds content
            var layer = ActiveLayer;
            if (layer == null) { FlashCanvasStatus("Add a layer in the Layers box first — the level has none."); return; }
            if (layer.locked) { FlashCanvasStatus($"Layer '{layer.name}' is locked — unlock it in the Layers box."); return; }
            if (cells == null || cells.Count == 0) return;
            var tile = PaintTile;
            if (!erase && tile == null)
            {
                FlashCanvasStatus(layer.tileset == null
                    ? "The active layer has no tileset — assign one in the Layers box."
                    : "Pick a tile in the palette first.");
                return;
            }

            var inst = EditInstance();
            Undo.RecordObject(level, label);
            var touched = new List<(string lyr, Vector2Int at)>();
            // One record, so the reflection is inside the same undo step by construction. Single-tile work,
            // so there is no arrangement to flip — only the positions reflect.
            foreach (var cell in MirrorCells(cells))
            {
                if (erase) level.ErasePaint(layer.name, cell);
                else if (variantOverride >= 0) level.SetPaint(layer.name, cell, tile, (byte)variantOverride);
                else level.Paint(layer.name, cell, tile);
                touched.Add((layer.name, cell));
            }
            EditorUtility.SetDirty(level);
            foreach (var (lyr, at) in touched) inst?.RefreshCell(lyr, at);
            canvas?.RefreshCells(touched);
            if (showTagOverlay) RefreshTagOverlay();
        }

        /// The flood region under `start`: 4-way contiguous cells whose ACTIVE-layer resolved tile is the
        /// same as the start cell's (null = empty counts as content too, so fill works on blank ground).
        /// Domain: the selection when the start sits inside one (fill-within-selection), else the canvas
        /// view rect — always finite. Ghost and commit both call this, so they cannot drift.
        internal List<Vector2Int> FillRegion(Vector2Int start)
        {
            var result = new List<Vector2Int>();
            var layer = ActiveLayer;
            if (layer == null || canvas == null) return result;
            bool inSelection = cellSelection.Count > 0 && cellSelection.Contains(start);
            // The bounds, not the padded view: a flood is the one tool that can reach the whole open
            // area at once, so letting it use the margin would fill in cells the level does not have.
            var domain = level.bounds;
            bool InDomain(Vector2Int c) => inSelection ? cellSelection.Contains(c) : domain.Contains(c);
            TileBase KeyAt(Vector2Int c) => level.ResolveCell(layer, c, out var rc) ? rc.tile : null;

            var key = KeyAt(start);
            var seen = new HashSet<Vector2Int> { start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                result.Add(c);
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (seen.Contains(n) || !InDomain(n)) continue;
                    seen.Add(n);
                    if (KeyAt(n) == key) queue.Enqueue(n);
                }
            }
            return result;
        }

        /// The Fill tool's commit: flood from the clicked cell and paint (or Alt-erase) the whole region.
        internal void FillAt(Vector2Int start, bool erase)
        {
            var region = FillRegion(start);
            if (region.Count == 0) return;

            // A MULTI-CELL brush tiles the region instead of stamping once per cell: thin the region to
            // non-overlapping anchors and lay the whole arrangement at each. A single tile keeps the
            // classic one-tile-per-cell fill, which is also what an ERASE always wants — erasing must clear
            // every cell of the region, not every third one.
            if (!erase && BrushFootprint() is { width: > 1 } or { height: > 1 })
            {
                CommitCells(ThinToFootprint(region), "Flood fill");
                return;
            }
            PaintCellsSingle(region, erase ? "Erase fill" : "Flood fill", erase);
        }

        void CommitCells(List<Vector2Int> cells, string label)
        {
            if (PreviewBlocks()) return;   // Line/Rect land here directly, not through ApplyAt

            // A span may legitimately be dragged past the edge; clip it rather than refusing the whole
            // stroke, because the author's intent ("fill to the wall") is unambiguous.
            if (!dragErasing) cells = cells.FindAll(BrushFitsAt);
            var layer = ActiveLayer;
            if (layer == null) { FlashCanvasStatus("Add a layer in the Layers box first — the level has none."); return; }
            if (layer.locked) { FlashCanvasStatus($"Layer '{layer.name}' is locked — unlock it in the Layers box."); return; }
            if (cells.Count == 0) return;
            if (!dragErasing && Brush.Count == 0)
            {
                FlashCanvasStatus(layer.tileset == null
                    ? "The active layer has no tileset — assign one in the Layers box."
                    : "Pick a tile in the palette first.");
                return;
            }

            var inst = EditInstance();
            Undo.RecordObject(level, label);
            var touched = new HashSet<(string lyr, Vector2Int at)>();
            // One record covering the primary span AND its reflections, so a mirrored Line or Rect is one
            // undo step. Each image carries its own arrangement flip: a pattern laid along a mirrored span
            // reads reversed, which is the whole point of mirroring a pattern rather than repeating it.
            foreach (var im in MirrorImagesOf(cells))
            {
                var cell = im.cell;
                if (dragErasing)
                {
                    for (int i = level.paints.Count - 1; i >= 0; i--)
                    {
                        var p = level.paints[i];
                        if (p != null && p.layer == layer.name && p.cell == cell) { level.paints.RemoveAt(i); break; }
                    }
                    touched.Add((layer.name, cell));
                }
                else foreach (var (off0, tile, shift) in Brush)
                {
                    if (tile == null) continue;
                    var off = FlipOffset(off0, im.flipX, im.flipY);
                    var target = ShiftedLayer(layer, shift);
                    if (variantOverride >= 0) level.SetPaint(target.name, cell + off, tile, (byte)variantOverride);
                    else level.Paint(target.name, cell + off, tile);
                    touched.Add((target.name, cell + off));
                }
            }
            EditorUtility.SetDirty(level);
            var list = new List<(string lyr, Vector2Int at)>(touched);
            foreach (var (lyr, at) in list) inst?.RefreshCell(lyr, at);
            canvas?.RefreshCells(list);
        }

        void ApplyStamp(EditImage im)
        {
            if (StampProp == null) { FlashCanvasStatus("Pick a prop in the Props box first."); return; }

            var cell = im.cell;
            // The bounds are the level's size: refuse a stamp that would hang over the edge rather
            // than silently enlarging the working area. All-or-nothing, because half a prop is worse
            // than a refusal that says how to get more room.
            if (!BoundsContain(StampFootprintAt(cell)))
            {
                FlashCanvasStatus($"Outside the level's {level.bounds.width}x{level.bounds.height} bounds — "
                                  + "resize it in the Level box to stamp here.");
                return;
            }
            // A mirrored stamp reflects its FOOTPRINT exactly, through the placement's own rotation+mirrorX
            // (see MirrorStamp for why the two compose the way they do). ⚠️ The art inside those cells does
            // NOT flip — a LevelTile has no flip — so a mirrored corner lands in the right cell wearing the
            // wrong corner. That limit is the tile model's, not this code's.
            MirrorStamp(stampRotation, stampMirrorX, im.flipX, im.flipY, out int rot, out bool mir);

            var inst = EditInstance();
            Undo.RecordObject(level, "Stamp prop");
            // A cell-less prop has no cells to borrow a layer from, so it takes the ACTIVE one. Without this
            // it belonged to no layer at all: it could not be hidden with anything, and there was no answer
            // to "which layer is that on?" — both of which read as bugs rather than as a deliberate model.
            level.Place(StampProp, cell, rot, mir, Origin.Authored, ActiveLayer?.name);
            EditorUtility.SetDirty(level);
            inst?.Rebuild();
            // Pane refresh is per-cell — the placement's cells resolve through the same ResolveCell the
            // canvas paints from, so no full re-render mid drag-tiling.
            var touched = new List<(string lyr, Vector2Int at)>();
            if (StampProp.cells != null)
                foreach (var cd in StampProp.cells)
                    if (cd != null)
                        touched.Add((string.IsNullOrEmpty(cd.layer) ? "Terrain" : cd.layer,
                            cell + Prop.TransformOffset(cd.offset, rot, mir)));
            canvas?.RefreshCells(touched);
            // Unconditional, and NOT folded into the touched-cells path: a placement's markers are a
            // different thing from its art. A cell-less prop touches no cells at all, so a refresh keyed on
            // touched cells would do nothing and the stamp would appear to fail.
            canvas?.RefreshMarkers();
            RebuildLevelBox();
        }

        void PlaceDecal()
        {
            if (ActiveLayer == null) { FlashCanvasStatus("Add a layer in the Layers box first — the level has none."); return; }
            if (decalSprite == null) { FlashCanvasStatus("Pick a sprite in the Tool box first."); return; }

            var inst = EditInstance();
            Undo.RecordObject(level, "Place decal");
            level.decals.Add(new Decal
            {
                sprite = decalSprite,
                position = hoverGridPos,
                layer = ActiveLayer.name,
            });
            EditorUtility.SetDirty(level);
            inst?.Rebuild();
            RebuildDecals();
            canvas?.RebuildContent();
        }

        // ── geometry ───────────────────────────────────────────────────────────
        static List<Vector2Int> LineCells(Vector2Int from, Vector2Int to)
        {
            // Bresenham — a tile line is any-angle, unlike the stamp line, which steps by footprint.
            var cells = new List<Vector2Int>();
            int x = from.x, y = from.y;
            int dx = Mathf.Abs(to.x - from.x), dy = -Mathf.Abs(to.y - from.y);
            int sx = from.x < to.x ? 1 : -1, sy = from.y < to.y ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                cells.Add(new Vector2Int(x, y));
                if (x == to.x && y == to.y) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
            return cells;
        }

        /// The level's bounds are its SIZE, not a hint. Painting outside them used to silently GROW the
        /// working area, so an edge stroke made the level bigger than the author had set it — "Painting the
        /// edges makes the grid area grow? It shouldnt. If the area is set to a certain size it should be
        /// kept." (2026-08-04). Out-of-bounds cells are also second-class in every other way: the
        /// background fill stops at the bounds and the thumbnail frames them, so content beyond was already
        /// half-real. Resize the level in the Level box to get more room.
        ///
        /// ERASING is deliberately NOT gated by this — a level that already carries content beyond its
        /// bounds must remain cleanable.
        bool InBounds(Vector2Int c) => level != null && level.bounds.Contains(c);

        /// Every cell of `r` inside the level. RectInt.Overlaps is not enough — a stamp half over the
        /// edge is exactly the case being refused.
        bool BoundsContain(RectInt r) =>
            level != null && level.bounds.Contains(new Vector2Int(r.xMin, r.yMin))
                          && level.bounds.Contains(new Vector2Int(r.xMax - 1, r.yMax - 1));

        /// True when every cell the brush would write at `anchor` is inside the bounds. A stamp is
        /// all-or-nothing on purpose: half a shelf at the edge is worse than a refusal that says why.
        bool BrushFitsAt(Vector2Int anchor)
        {
            if (brush.Count == 0) return InBounds(anchor);
            foreach (var (off, tile, _) in brush)
                if (tile != null && !InBounds(anchor + off)) return false;
            return true;
        }

        /// The brush's footprint as a rect around its own origin — 1×1 for a single tile, the bounding box
        /// of the offsets for a pattern or a clump.
        internal RectInt BrushFootprint()
        {
            if (brush.Count == 0) return new RectInt(0, 0, 1, 1);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var (off, tile, _) in brush)
            {
                if (tile == null) continue;
                if (off.x < minX) minX = off.x;
                if (off.x > maxX) maxX = off.x;
                if (off.y < minY) minY = off.y;
                if (off.y > maxY) maxY = off.y;
            }
            if (minX > maxX) return new RectInt(0, 0, 1, 1);
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// Thin a span down to non-overlapping ANCHORS for the current brush.
        ///
        /// A Line, Rect or Fill used to stamp the whole brush at EVERY cell it covered, which is right for a
        /// single tile and nonsense for anything wider: a 3-wide clump dragged across a rect was laid three
        /// times over itself in every position, so all you saw was the last one and the level filled with
        /// overlapping junk. "If its 3 wide it should paint every 3 tiles" (2026-08-03).
        ///
        /// Greedy, in the order the span was generated: keep a cell if its footprint clears everything kept
        /// so far. That one rule tiles a Rect into a neat grid, spaces a Line by the brush's own width along
        /// whatever angle it runs, and packs a Fill's arbitrary region — without any of the three needing to
        /// know the brush's shape. A 1×1 brush overlaps nothing, so it keeps every cell and behaves exactly
        /// as before.
        internal List<Vector2Int> ThinToFootprint(List<Vector2Int> cells)
        {
            var foot = BrushFootprint();
            if (cells == null || cells.Count == 0 || (foot.width <= 1 && foot.height <= 1)) return cells;

            var kept = new List<Vector2Int>();
            var taken = new List<RectInt>();
            foreach (var c in cells)
            {
                var r = new RectInt(c.x + foot.xMin, c.y + foot.yMin, foot.width, foot.height);
                bool clash = false;
                foreach (var t in taken) if (t.Overlaps(r)) { clash = true; break; }
                if (clash) continue;
                taken.Add(r);
                kept.Add(c);
            }
            return kept;
        }

        static List<Vector2Int> RectCells(Vector2Int a, Vector2Int b)
        {
            var cells = new List<Vector2Int>();
            for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
                for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
                    cells.Add(new Vector2Int(x, y));
            return cells;
        }

        // ── ghost ──────────────────────────────────────────────────────────────
        /// The cells the CURRENT gesture would write. The overlay outlines this list and the commits use
        /// the SAME methods (LineCells / RectCells / the brush offsets / the stamp's transformed offsets),
        /// so preview and commit cannot drift — the ported discipline, kept.
        internal List<Vector2Int> GhostCells()
        {
            // A marquee in flight outranks the tool's own ghost: what the release will do is SELECT, and
            // showing a paint ghost over it would promise an edit that is not coming — including a mirrored
            // one, which is why the reflection sits OUTSIDE this branch.
            if (marqueeing) return RectCells(anchor, hoverCell);
            // Pick TAKES and Decal drops a free sprite off the grid: neither writes a mirrored cell, so
            // neither may outline one. The ghost promises exactly what the commit does, mirror included.
            if (tool == CanvasTool.Pick || tool == CanvasTool.Decal) return GhostCellsCore();
            return MirrorCells(GhostCellsCore());
        }

        List<Vector2Int> GhostCellsCore()
        {
            var cells = new List<Vector2Int>();
            if (tool == CanvasTool.Stamp && StampProp?.cells != null)
            {
                foreach (var cd in StampProp.cells)
                {
                    if (cd == null) continue;
                    cells.Add(hoverCell + Prop.TransformOffset(cd.offset, stampRotation, stampMirrorX));
                }
            }
            // Span ghosts are thinned exactly as the commit will be, so what is outlined is what lands.
            // Pick is NOT thinned — it samples a region, it does not stamp one.
            else if (tool == CanvasTool.Line && dragging) return ThinToFootprint(LineCells(anchor, hoverCell));
            else if (tool == CanvasTool.Rect && dragging) return ThinToFootprint(RectCells(anchor, hoverCell));
            else if (tool == CanvasTool.Pick && dragging) return RectCells(anchor, hoverCell);
            else if (tool == CanvasTool.Fill && hoverValid) return ThinToFootprint(FillRegion(hoverCell));
            else if (tool == CanvasTool.Paint && Brush.Count > 1)
            {
                // The pattern brush ghosts its whole arrangement — what is outlined is what lands.
                foreach (var (off, _, _) in Brush) cells.Add(hoverCell + off);
            }
            else cells.Add(hoverCell);
            return cells;
        }

        /// How many ghost sprites the canvas will draw at once. A Fill over open ground or a Rect span with a
        /// nine-tile pattern can nominate thousands of cells, and a thousand `Image` elements repositioned on
        /// every PointerMoveEvent is a stutter, not a preview.
        ///
        /// Over the cap the art is dropped ENTIRELY and the classic outline-only ghost stands. Deliberately
        /// all-or-nothing: drawing the first 400 cells of a 3000-cell flood would say, in the clearest visual
        /// language available, that the fill stops there — a preview that lies about extent is worse than one
        /// that shows extent alone.
        const int GhostArtCap = 400;

        /// THE GHOST'S ART: which sprite lands in which cell if the gesture commits right now. Its twin
        /// <see cref="GhostCells"/> answers WHERE (the outline); this answers WHAT, and the two are drawn
        /// together because neither alone is legible — an outline cannot show which tile a nine-tile pattern
        /// is about to lay down, and art alone cannot show an eraser or an empty brush.
        ///
        /// Every branch mirrors the COMMIT path it previews, so preview and commit cannot drift: Stamp reads
        /// the prop's own transformed cells, Fill and the selection multi-edit paint the brush's FIRST tile
        /// per cell, Line/Rect lay the whole brush arrangement at every cell of the span, and plain Paint lays
        /// the whole brush at the hover cell — including a clump brush's layer-shifted cells, which are
        /// ordinary entries of `brush` carrying a shift, and therefore ordinary art here.
        internal List<(Vector2Int cell, Sprite sprite)> GhostSprites() => MirrorArt(GhostSpritesCore());

        List<(Vector2Int cell, Sprite sprite)> GhostSpritesCore()
        {
            var art = new List<(Vector2Int, Sprite)>();
            if (level == null) return art;

            // A marquee SELECTS; it writes nothing, so promising paint under it would be a lie.
            if (marqueeing) return art;
            // An eraser has no art by definition, and the red outline already says what it will take away.
            if (tool == CanvasTool.Erase || (dragging && dragErasing)) return art;

            if (tool == CanvasTool.Stamp)
            {
                var p = StampProp;
                if (p?.cells == null) return art;
                foreach (var cd in p.cells)
                {
                    if (cd?.tile == null) continue;   // a metadata-only cell draws nothing; its marker speaks
                    var s = SpriteOfTile(cd.tile, -1);
                    if (s != null)
                        art.Add((hoverCell + Prop.TransformOffset(cd.offset, stampRotation, stampMirrorX), s));
                }
                return art;
            }

            // Pick takes, it does not give; Decal drops a free sprite at a fractional point, which the
            // overlay's cross marks far more honestly than a cell-sized ghost would.
            if (tool == CanvasTool.Pick || tool == CanvasTool.Decal) return art;
            if (brush.Count == 0) return art;

            if (tool == CanvasTool.Fill)
            {
                if (!hoverValid) return art;
                var region = ThinToFootprint(FillRegion(hoverCell));
                var foot = BrushFootprint();

                // Mirror FillAt exactly: a multi-cell brush TILES the region with its whole arrangement, a
                // single tile fills cell by cell. Ghost and commit must agree about which of those it is.
                if (foot.width > 1 || foot.height > 1)
                {
                    if (region.Count * brush.Count > GhostArtCap) return art;
                    foreach (var c in region)
                        foreach (var (off, tile, _) in brush)
                        {
                            var s = SpriteOfTile(tile, variantOverride);
                            if (s != null) art.Add((c + off, s));
                        }
                    return art;
                }

                var first = SpriteOfTile(brush[0].tile, variantOverride);
                if (first == null) return art;
                if (region.Count > GhostArtCap) return art;   // outline-only, all or nothing — see the cap's note
                foreach (var c in region) art.Add((c, first));
                return art;
            }

            if ((tool == CanvasTool.Line || tool == CanvasTool.Rect) && dragging)
            {
                var span = ThinToFootprint(tool == CanvasTool.Line ? LineCells(anchor, hoverCell)
                                                                   : RectCells(anchor, hoverCell));
                if (span.Count * brush.Count > GhostArtCap) return art;
                foreach (var c in span)
                    foreach (var (off, tile, _) in brush)
                    {
                        var s = SpriteOfTile(tile, variantOverride);
                        if (s != null) art.Add((c + off, s));
                    }
                return art;
            }

            foreach (var (off, tile, _) in brush)
            {
                var s = SpriteOfTile(tile, variantOverride);
                if (s != null) art.Add((hoverCell + off, s));
            }
            return art;
        }

        /// The sprite a brush entry (or a prop cell, which may be any TileBase) would draw with. An animated
        /// tile shows its first frame — what the canvas itself draws — otherwise the forced variant if one is
        /// set, else variant zero; `SpriteOfVariant` clamps, so a stale forced index degrades to a real sprite
        /// instead of a hole. Anything that is not a LevelTile goes through the shared `SpriteOf`, exactly as
        /// <see cref="CartographerPreview.SpriteOfResolved"/> does, so the ghost and the canvas agree.
        static Sprite SpriteOfTile(TileBase tile, int variant)
        {
            if (tile is LevelTile lt)
                return lt.IsAnimated ? lt.animation[0] : lt.SpriteOfVariant(variant >= 0 ? variant : 0);
            return CartographerPreview.SpriteOf(tile);
        }

        // ── the status line: the pane's permanently reserved feedback channel ─────────────────────────
        int canvasFlashSeq;
        Color canvasFlashBase;
        double canvasStatusHoldUntil;

        /// The passive readout: hovered cell, tool, active layer, zoom, and the hovered cell's tags while
        /// the overlay is on (the ported scene readout). Never runs while a flash message holds the line.
        internal void UpdateCanvasStatus()
        {
            if (canvasStatus == null) return;
            if (EditorApplication.timeSinceStartup < canvasStatusHoldUntil) return;
            if (level == null) { canvasStatus.text = ""; return; }

            string zoom = canvas != null ? $"{Mathf.RoundToInt(canvas.DisplayPercent)}%" : "";
            // A hidden ACTIVE layer is the one state where painting looks like it does nothing — the paint
            // lands, the canvas just is not drawing that layer. Say so on the line that is always there,
            // rather than flashing a warning on every stroke.
            var al = ActiveLayer;
            string layerName = al == null ? "no layer"
                : al.editorHidden ? al.name + " (hidden while editing)"
                : !al.visible ? al.name + " (hidden in game)"
                : al.name;
            string s = hoverValid
                ? $"({hoverCell.x}, {hoverCell.y}) · {ToolNames[(int)tool]} on {layerName} · {zoom}"
                : $"{ToolNames[(int)tool]} on {layerName} · {zoom}";

            // A preview looks EXACTLY like the level — that is what makes it useful and what makes it
            // dangerous. The always-there line is where "you are not looking at your asset" belongs.
            if (Previewing) s = "PROCGEN PREVIEW (read-only) · " + s;

            // THE TEACHING SLOT. A ruler under the pointer says what to do with it, a guide under the pointer
            // says every gesture it takes, and with neither it falls back to naming the live mirror axis.
            // This is how someone who was never told about guides finds them: the same reserved line that
            // already names a prop marker on hover.
            string guideHint = canvas?.GuideHint();
            if (!string.IsNullOrEmpty(guideHint)) s += " · " + guideHint;

            if (cellSelection.Count > 0) s += $" · {cellSelection.Count} selected";

            // A metadata-only prop under the pointer NAMES itself and says what it does — the marker tint
            // shows that something is there, this says what.
            string markerLayer = null;
            var marker = canvas == null ? null : canvas.MarkerAt(hoverCell, hoverValid, out markerLayer);
            if (marker != null)
            {
                s += " · " + (string.IsNullOrEmpty(marker.displayName) ? marker.name : marker.displayName);
                // Name the layer, always. "Which layer is this on?" was unanswerable from the canvas, and a
                // marker with NO layer is the case worth calling out loudest — it is the one that will not
                // hide when its neighbours do.
                s += string.IsNullOrEmpty(markerLayer) ? " [no layer — re-stamp to give it one]"
                                                       : $" [{markerLayer}]";
                string what = marker.Explain();
                if (!string.IsNullOrEmpty(what)) s += ": " + what;
            }

            if (showTagOverlay && hoverValid && level.layers != null)
            {
                var names = new List<string>();
                foreach (var layer in level.layers)
                {
                    if (layer == null) continue;
                    foreach (var t in level.TagsAt(hoverCell, layer.name))
                        if (!names.Contains(t.name)) names.Add(t.name);
                }
                if (names.Count > 0) s += " · " + string.Join(", ", names);
            }
            canvasStatus.text = s;
        }

        /// A RESULT, not a problem: the shared tileset grid reporting what an edit did ("Pasted 3 tile(s)…").
        /// Same reserved line, same hold so the next hover update cannot swallow it — but NO red, because
        /// red is the channel that means "your edit did nothing" and spending it on successes blunts it.
        internal void ReportCanvasStatus(string message)
        {
            if (canvasStatus == null) return;
            canvasStatus.text = message;
            canvasStatusHoldUntil = EditorApplication.timeSinceStartup + 2.5;
        }

        /// A BLOCKED edit must be unmissable — silent no-ops die here. Same reserved line, text + colour
        /// only (the stable-layout rule), landing in warning red and fading back; the message holds the
        /// line briefly so the next hover update cannot overwrite the answer before it is read.
        /// GridKit-fodder: still duplicated from TilesetBuilderWindow.FlashStatus — the reserved-line +
        /// flash pattern wants extracting next to the shared grid, but it is not this pass's job.
        internal void FlashCanvasStatus(string message)
        {
            if (canvasStatus == null) return;
            canvasStatus.text = message;
            canvasStatusHoldUntil = EditorApplication.timeSinceStartup + 2.5;
            if (canvasFlashSeq == 0)
            {
                var c = canvasStatus.resolvedStyle.color;
                canvasFlashBase = c.a > 0.01f ? c : new Color(0.6f, 0.6f, 0.6f);
            }
            int seq = ++canvasFlashSeq;
            var red = new Color(1f, 0.38f, 0.32f);
            canvasStatus.style.color = red;
            double t0 = EditorApplication.timeSinceStartup;
            const float fade = 1.6f;
            canvasStatus.schedule.Execute(() =>
            {
                if (seq != canvasFlashSeq) return;   // a newer flash owns the line
                float k = Mathf.Clamp01((float)(EditorApplication.timeSinceStartup - t0) / fade);
                if (k >= 1f) canvasStatus.style.color = new StyleColor(StyleKeyword.Null);   // hand colour back to the stylesheet
                else canvasStatus.style.color = Color.Lerp(red, canvasFlashBase, k);
            }).Every(60).ForDuration((long)(fade * 1000f) + 200);
        }

        // ── window-state persistence: the dials survive close/reopen, not just domain reloads ─────────
        // The Tileset Builder's targeted-DTO pattern, mirrored on purpose (never EditorJsonUtility.ToJson(this):
        // whole-window JSON stores asset references as session-local instanceIDs and stomps base internals).
        // The decal sprite is deliberately NOT persisted — a sprite is a sub-asset (GUID alone is ambiguous)
        // and a stale decal brush is worth less than the plumbing to restore one.
        static string CartographerStateKey => "Laubrary.Cartographer.State." + PlayerSettings.productGUID;

        [System.Serializable]
        class WindowState
        {
            public int tool = -1, stampRotation, variantOverride = -1, activeLayer;
            public bool stampMirrorX, showTagOverlay;
            public bool showGridLines = true;
            public bool showPropMarkers = true;
            public float canvasZoom = 1f;
            public string levelGuid;

            // The shared tileset grid's dials. Flat fields rather than a nested object, for the same
            // reason the rest of this DTO is flat: EditorJsonUtility writes an object reference as a
            // session-local instanceID, so the active TAG has to travel as a GUID like every other
            // reference here.
            public float tsCellZoom = 40f, tsLineBrightness = 0.75f, tsLineAlpha = 0.35f, tsOscSpeed = 1.2f;
            public bool tsSeamless, tsSeamlessMarks = true, tsOverwrite, tsShowAnimated, tsCycleRandoms;
            public int tsTab, tsTileEditMode, tsClumpEditMode;
            public string tsActiveTagGuid;

            // RULER GUIDES. The three switches are a working STYLE and so are global; the LINES are per
            // level and travel as a bounded list keyed by GUID (see the file header on CartographerWindow.
            // Guides.cs for why they are here and not on the LevelAsset). Defaults matter: guides start ON
            // so the rulers are visible and the feature can be found at all, and snapping starts on too —
            // harmlessly, since a level with no guides has nothing to snap to.
            public bool showGuides = true;
            public bool snapToGuides = true;
            public bool mirrorGuides;
            public List<GuideSet> guideSets = new();
        }

        static string GuidOf(Object o)
        {
            string p = o != null ? AssetDatabase.GetAssetPath(o) : null;
            return string.IsNullOrEmpty(p) ? "" : AssetDatabase.AssetPathToGUID(p);
        }

        void SaveWindowState()
        {
            TrimGuideSets();   // the bound applies to what is WRITTEN, so it is enforced on the way out
            var s = new WindowState
            {
                showGuides = showGuides,
                snapToGuides = snapToGuides,
                mirrorGuides = mirrorGuides,
                guideSets = guideSets,
                tool = (int)tool,
                stampRotation = stampRotation,
                variantOverride = variantOverride,
                activeLayer = activeLayer,
                stampMirrorX = stampMirrorX,
                showTagOverlay = showTagOverlay,
                showGridLines = showGridLines,
                showPropMarkers = showPropMarkers,
                canvasZoom = canvasZoom,
                levelGuid = GuidOf(level),
                tsCellZoom = tilesetOptions.cellZoom,
                tsLineBrightness = tilesetOptions.lineBrightness,
                tsLineAlpha = tilesetOptions.lineAlpha,
                tsOscSpeed = tilesetOptions.oscSpeed,
                tsSeamless = tilesetOptions.seamless,
                tsSeamlessMarks = tilesetOptions.seamlessMarks,
                tsOverwrite = tilesetOptions.overwriteOnDrop,
                tsShowAnimated = tilesetOptions.showAnimated,
                tsCycleRandoms = tilesetOptions.cycleRandoms,
                tsTab = tilesetOptions.tab,
                tsTileEditMode = tilesetOptions.tileEditMode,
                tsClumpEditMode = tilesetOptions.clumpEditMode,
                tsActiveTagGuid = GuidOf(tilesetOptions.activeTag),
            };
            EditorPrefs.SetString(CartographerStateKey, EditorJsonUtility.ToJson(s));
        }

        void RestoreWindowState()
        {
            string json = EditorPrefs.GetString(CartographerStateKey, "");
            if (string.IsNullOrEmpty(json)) return;
            var s = new WindowState();
            try { EditorJsonUtility.FromJsonOverwrite(json, s); }
            catch { return; }

            if (s.tool >= 0 && s.tool < ToolNames.Length) tool = (CanvasTool)s.tool;
            stampRotation = Mathf.Clamp(s.stampRotation, 0, 3);
            variantOverride = Mathf.Max(-1, s.variantOverride);
            activeLayer = Mathf.Max(0, s.activeLayer);
            stampMirrorX = s.stampMirrorX;
            showTagOverlay = s.showTagOverlay;
            showGridLines = s.showGridLines;
            showPropMarkers = s.showPropMarkers;
            canvasZoom = Mathf.Clamp(s.canvasZoom, 1f, 16f);

            showGuides = s.showGuides;
            snapToGuides = s.snapToGuides;
            mirrorGuides = s.mirrorGuides;
            // Before the level is known: RestoreGuideSets points the active set at whatever `level` already
            // resolves to, and BuildAsset re-points it once the window actually opens one.
            RestoreGuideSets(s.guideSets);

            tilesetOptions.cellZoom = s.tsCellZoom;
            tilesetOptions.lineBrightness = s.tsLineBrightness;
            tilesetOptions.lineAlpha = s.tsLineAlpha;
            tilesetOptions.oscSpeed = s.tsOscSpeed;
            tilesetOptions.seamless = s.tsSeamless;
            tilesetOptions.seamlessMarks = s.tsSeamlessMarks;
            tilesetOptions.overwriteOnDrop = s.tsOverwrite;
            tilesetOptions.showAnimated = s.tsShowAnimated;
            tilesetOptions.cycleRandoms = s.tsCycleRandoms;
            tilesetOptions.tab = s.tsTab;
            tilesetOptions.tileEditMode = s.tsTileEditMode;
            tilesetOptions.clumpEditMode = s.tsClumpEditMode;
            tilesetOptions.Clamp();

            // Unity's own layout serialization restores an already-open window (editor restart) with its
            // reference intact — never stomp a live value with the prefs copy; only fill the fresh-open void.
            if (asset == null && !string.IsNullOrEmpty(s.levelGuid))
                asset = AssetDatabase.LoadAssetAtPath<LevelAsset>(AssetDatabase.GUIDToAssetPath(s.levelGuid));
            if (tilesetOptions.activeTag == null && !string.IsNullOrEmpty(s.tsActiveTagGuid))
                tilesetOptions.activeTag = AssetDatabase.LoadAssetAtPath<TileTag>(AssetDatabase.GUIDToAssetPath(s.tsActiveTagGuid));
        }

        // ── the canvas element ─────────────────────────────────────────────────
        /// THE edit view: the level rendered as per-layer textures (point-filtered, checkerboard beneath,
        /// decals interleaved by sorting), an overlay for grid/tags/ghost, and the ported gesture suite on
        /// top. View maths (fit, wheel-zoom toward pointer, middle-drag pan, degenerate-geometry guard)
        /// live in the shared ZuiPanZoom — this class is its first consumer.
        internal partial class LevelCanvas : VisualElement
        {
            const int ViewPad = 2;        // cells of editable margin around the content
            const int MaxTexSide = 2048;  // per-layer texture cap; cell resolution shrinks before this grows

            readonly CartographerWindow w;
            readonly VisualElement checker;
            readonly VisualElement content;
            readonly VisualElement ghost;
            readonly VisualElement overlay;
            /// The empty-canvas teaching line. Named `hint`, not `guide`: this window now also has RULER
            /// GUIDES, and two unrelated things a letter apart is a bug waiting to be typed.
            readonly Label hint;
            internal readonly ZuiPanZoom view;

            /// The ghost's ART elements, POOLED. ☠️ `Painter2D` cannot draw a texture (stated flat out at
            /// PropWindow.cs:476), and the ghost overlay IS Painter2D — so the tiles the brush is about to lay
            /// down have to be real `Image` elements, exactly like the layer textures and the decals beside
            /// them. Allocating those on every PointerMoveEvent would stutter, so they are reused: `SyncGhost`
            /// fills the ones it needs and hides the rest, and the list only ever grows to the largest ghost
            /// the session has drawn.
            readonly List<Image> ghostPool = new();
            bool syncingGhost;

            /// How many ghost images are showing. The overlay reads it to weaken its own tint when there is
            /// art beneath — a locator over a picture, instead of a wash over one.
            int ghostArtCount;

            /// Live counts for verification: how many pooled elements exist versus how many are in use. A
            /// ghost that allocates per pointer move is a stutter, so "does the pool grow?" has to be
            /// answerable without guessing.
            internal int GhostPoolSize => ghostPool.Count;
            internal int GhostArtCount => ghostArtCount;

            Texture2D checkerTex;
            RectInt viewRect;
            int cellPx = 16;
            Color32[] clearBlock;
            bool pendingGrow;

            // Left-stroke dedupe: the same cell twice running is one edit, and a stamp advances by
            // FOOTPRINT so a drag tiles props edge-to-edge rather than stacking one per cell.
            Vector2Int lastApplied = new(int.MinValue, int.MinValue);
            bool haveLastFootprint;
            RectInt lastFootprint;

            // Right-gesture disambiguation: a press is a card until the pointer moves, then it is a marquee.
            bool rightDown, rightMoved;
            Vector2 rightStart;

            /// The cells the canvas can address — bounds plus everything painted beyond them, plus the pad.
            /// The flood fill uses it as its outer domain so a fill on empty ground is always finite.
            internal RectInt ViewRect => viewRect;

            /// Placements that draw NO tile in a given cell, by cell — the markers that keep metadata-only
            /// props from being invisible. Rebuilt with the content.
            ///
            /// Each entry remembers the LAYER its cell was stamped onto (`PropCell.layer`), because a marker
            /// is not layer-less chrome: it stands in for a cell of a real layer, and must hide when that
            /// layer is hidden (2026-08-03 — they used to draw unconditionally, so hiding a layer left its
            /// invisible props still glowing). A prop with no CELLS takes the layer its placement recorded
            /// when it was stamped (2026-08-04 — before that it had none at all, so it could not be hidden
            /// with anything and there was no answer to "which layer is that on?").
            readonly Dictionary<Vector2Int, (PropPlacement placement, string layer)> markers = new();

            /// TWO VISIBILITIES, and the canvas obeys ONLY the editor half. `visible`/`opacity` are what the
            /// GAME shows and they ship; `editorHidden`/`editorOpacity` are authoring aids that never leave
            /// this window (LevelInstance, the level thumbnail and the build all ignore them).
            ///
            /// These used to COMPOSE — a layer drew here only if the game would draw it too — on the theory
            /// that the canvas should show what ships. That theory is wrong, and a marker layer is the proof:
            /// an Aisle layer that exists purely to declare structure for a procgen pass is deliberately
            /// `visible = false`, and composing made it impossible to SEE the very cells you have to paint.
            /// A runtime-invisible layer is not an unimportant layer; it is often the one being worked on.
            ///
            /// So: the editor controls govern the editor, full stop. What ships is reported in the layer row
            /// and in the status line rather than enforced on the canvas, because an editing view whose
            /// content can vanish for reasons outside the editing controls is not an editing view.
            static bool DrawnHere(LevelLayer l) => l != null && !l.editorHidden;
            static Color EditorTint(LevelLayer l) =>
                new Color(1f, 1f, 1f, l == null ? 1f : l.editorOpacity);

            /// Re-tint the layer images in place. Opacity is DRAGGED, and a full RebuildContent per pointer
            /// move would re-blit every tile of every layer for the sake of one colour.
            internal void RefreshLayerTints()
            {
                foreach (var lv in layerViews)
                    if (lv.img != null) lv.img.tintColor = EditorTint(lv.layer);
            }

            /// Whether a marked cell should be drawn at all: the global toggle, then its layer's own
            /// visibility — including the editor-only hide, because a marker is drawn ON this canvas and a
            /// layer the author has hidden to see underneath must not leave its markers glowing on top. A
            /// marker naming a layer the level no longer has is stale — hide it rather than draw a cell
            /// nothing can account for.
            bool MarkerVisible(string layerName)
            {
                if (!w.showPropMarkers) return false;
                // An empty layer name now means only ONE thing: a placement made before placements carried a
                // layer. Keep drawing it — a marker that cannot hide is annoying, a marker that silently
                // vanishes is lost work — and let the status line say it has no layer so it can be fixed.
                if (string.IsNullOrEmpty(layerName)) return true;
                return DrawnHere(w.level?.GetLayer(layerName));
            }

            /// The metadata-only prop occupying `cell`, if any — what the status line names on hover. Only
            /// reports a marker the author can actually SEE; naming a hidden one would be a ghost.
            internal Prop MarkerAt(Vector2Int cell, bool valid) => MarkerAt(cell, valid, out _);

            /// The marker under the pointer, and the LAYER it belongs to — the status line reports both,
            /// because a coloured cell that cannot be attributed to a layer is a thing the author can see
            /// and not act on.
            internal Prop MarkerAt(Vector2Int cell, bool valid, out string layerName)
            {
                layerName = null;
                if (!valid || !markers.TryGetValue(cell, out var m) || !MarkerVisible(m.layer)) return null;
                layerName = m.layer;
                return m.placement.prop;
            }

            class LayerView
            {
                public LevelLayer layer;
                public Texture2D tex;
                public Image img;
                public bool dirty;
            }
            readonly List<LayerView> layerViews = new();
            readonly Dictionary<string, LayerView> layerByName = new();

            Vector2 ContentPx => new(viewRect.width * cellPx, viewRect.height * cellPx);
            internal float DisplayPercent => view.Scale * 100f;

            public LevelCanvas(CartographerWindow window)
            {
                w = window;
                AddToClassList("zui-stage");
                style.flexGrow = 1f;
                style.minHeight = 0f;
                style.overflow = Overflow.Hidden;
                tooltip = "The level — THE edit view. Left-click applies the tool (drag to continue); " +
                          "Alt erases from any tool; Ctrl picks; wheel zooms toward the pointer; " +
                          "middle-drag pans while zoomed; right-click opens the Tool card.  " +
                          "The RULERS along the top and left edges number the cell boundaries: drag out of " +
                          "one to make an alignment GUIDE, drag a guide to move it, click it to make it a " +
                          "mirror axis, and drag it back onto its ruler to delete it. The status line under " +
                          "the canvas names whatever the pointer is on.";

                // View maths delegated to the shared utility; created FIRST so its pointer handlers run
                // before ours and IsPanning is already honest when our handlers fire.
                view = new ZuiPanZoom(this) { Zoom = Mathf.Clamp(w.canvasZoom, 1f, 16f) };
                view.ViewChanged += () =>
                {
                    w.canvasZoom = view.Zoom;
                    ApplyView();
                    w.UpdateCanvasStatus();
                };

                // Behind the level: a checkerboard, the universal "nothing here" — transparent cells must
                // never read as black art. GridKit-fodder: styling shared by CALLING the builder's helper
                // (same assembly); extract with the rest of the canvas kit.
                checker = new VisualElement { pickingMode = PickingMode.Ignore };
                checker.style.position = Position.Absolute;
                Add(checker);

                // The level's pixels: per-layer Images (+ decal Images) in a container scaled as one
                // transform, so zooming never rebuilds a texture.
                content = new VisualElement { pickingMode = PickingMode.Ignore };
                content.style.position = Position.Absolute;
                content.style.transformOrigin = new TransformOrigin(0f, 0f);
                Add(content);

                // The GHOST'S ART: half-transparent copies of the tiles the next click would lay down, in
                // front of the level and behind the overlay — so the outline that says WHERE always reads on
                // top of the art that says WHAT. Its own child rather than part of `content` because it is not
                // level data: it must never be caught by a texture rebuild, a layer's opacity or the decal
                // sort, and it has to be repositioned per pointer move without touching any of that.
                ghost = new VisualElement { pickingMode = PickingMode.Ignore };
                ghost.style.position = Position.Absolute;
                ghost.style.left = ghost.style.top = ghost.style.right = ghost.style.bottom = 0f;
                ghost.style.overflow = Overflow.Hidden;   // a ghost cell just off the pane must not paint the chrome
                Add(ghost);

                // Painter2D content draws BENEATH an element's children — the paid-for SheetStage trap.
                // Grid, tag tints and the ghost outline live on their own overlay child kept in front.
                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.style.position = Position.Absolute;
                overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0f;
                overlay.generateVisualContent += PaintOverlay;
                Add(overlay);

                // The ruler NUMBERS, in front of the Painter2D overlay that draws the strips they sit on.
                BuildRulerLayer();

                // The canvas explains itself — an empty void teaches nothing.
                hint = new Label { pickingMode = PickingMode.Ignore };
                hint.style.position = Position.Absolute;
                hint.style.left = hint.style.right = 0f;
                hint.style.top = 12f;
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                hint.style.fontSize = 13f;
                hint.style.color = new Color(1f, 1f, 1f, 0.55f);
                hint.style.whiteSpace = WhiteSpace.Normal;
                Add(hint);

                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
                RegisterCallback<PointerLeaveEvent>(OnLeave);
                // ONLY our own geometry. GeometryChangedEvent bubbles, so an unfiltered handler here also
                // fires for every child — and since the repaint path now MOVES children (the ghost pool),
                // an unfiltered one would be a feedback loop: reposition a ghost → child geometry changed →
                // ApplyView → repaint → reposition a ghost.
                RegisterCallback<GeometryChangedEvent>(e => { if (e.target == this) ApplyView(); });

                // Esc drops the marquee. Focusable so the key reaches us at all; focus is taken on press,
                // which is the same gesture that would create a selection in the first place.
                focusable = true;
                RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode != KeyCode.Escape) return;
                    // A guide in the hand outranks the marquee: Esc means "abandon what I am doing now".
                    if (CancelGuideDrag()) { e.StopPropagation(); return; }
                    if (w.cellSelection.Count > 0) { w.ClearCellSelection(); e.StopPropagation(); }
                });
            }

            /// THE one "the preview changed" call. It has two halves that must never be made separately: the
            /// Painter2D overlay (grid, tints, the ghost OUTLINE) is invalidated for the next repaint, and the
            /// ghost's Image ART is repositioned NOW. Every `overlay.MarkDirtyRepaint()` inside this class was
            /// replaced by this, so a future caller cannot accidentally move the outline without the art.
            internal void RepaintOverlay()
            {
                overlay.MarkDirtyRepaint();
                SyncGhost();
                SyncRulers();
            }

            /// One cell's rect in canvas-local pixels. Shared by the overlay painter and the ghost art layer
            /// on purpose: they draw the same cells, and two copies of this arithmetic would drift.
            Rect CellRectOf(Vector2Int cell)
            {
                float step = cellPx * view.Scale;
                return new Rect(
                    view.Origin.x + (cell.x - viewRect.xMin) * step,
                    view.Origin.y + (viewRect.yMax - cell.y - 1) * step,
                    step, step);
            }

            /// Place the ghost's art: one pooled `Image` per (cell, sprite) the current gesture would write.
            /// Elements are REUSED — the pool only grows — and the surplus is hidden rather than removed, so a
            /// pointer move costs a handful of style writes and no allocation.
            ///
            /// ☠️ `image` + `sourceRect`, NOT `sprite`. A UITK `Image` fed a `sprite` renders the sprite's own
            /// (by default TIGHT) mesh stretched to fill the element, which crops the tile's transparent
            /// margins away and lands the art at a different size and offset from the canvas beneath it — the
            /// exact trap `CartographerPreview.Blit` documents. Feeding the raw texture with the sprite's
            /// DECLARED `rect` draws the cell as authored. The rect is flipped in Y because Unity's sprite
            /// rects count from the texture's bottom-left while `sourceRect` counts from its top-left.
            void SyncGhost()
            {
                if (syncingGhost) return;
                syncingGhost = true;
                try
                {
                    int used = 0;
                    bool live = w.level != null && viewRect.width > 0 && (w.hoverValid || w.dragging)
                                && cellPx * view.Scale > 0.001f;
                    if (live)
                        foreach (var (cell, sprite) in w.GhostSprites())
                        {
                            if (!viewRect.Contains(cell)) continue;
                            var tex = sprite != null ? sprite.texture : null;
                            if (tex == null) continue;

                            var img = GhostElement(used++);
                            var r = CellRectOf(cell);
                            img.style.left = r.x;
                            img.style.top = r.y;
                            img.style.width = r.width;
                            img.style.height = r.height;
                            var sr = sprite.rect;
                            img.image = tex;
                            img.sourceRect = new Rect(sr.x, tex.height - sr.yMax, sr.width, sr.height);
                            img.style.display = DisplayStyle.Flex;
                        }

                    for (int i = used; i < ghostPool.Count; i++)
                        if (ghostPool[i].style.display != DisplayStyle.None)
                        {
                            ghostPool[i].style.display = DisplayStyle.None;
                            ghostPool[i].image = null;   // never hold a texture the level may destroy
                        }
                    ghostArtCount = used;
                }
                finally { syncingGhost = false; }
            }

            /// The pooled ghost image at `i`, minted on first use. 50% alpha is the whole point of the layer:
            /// solid art would read as already-painted and there would be no way to see the level under it.
            Image GhostElement(int i)
            {
                while (ghostPool.Count <= i)
                {
                    var made = new Image
                    {
                        scaleMode = ScaleMode.StretchToFill,
                        pickingMode = PickingMode.Ignore,
                    };
                    made.style.position = Position.Absolute;
                    made.style.opacity = 0.5f;
                    ghost.Add(made);
                    ghostPool.Add(made);
                }
                return ghostPool[i];
            }

            internal void Dispose()
            {
                foreach (var lv in layerViews) if (lv.tex != null) DestroyImmediate(lv.tex);
                layerViews.Clear();
                layerByName.Clear();
                foreach (var g in ghostPool) g.image = null;   // the pool never owns a texture, only borrows
                ghostPool.Clear();
                ghost.Clear();
                if (checkerTex != null) DestroyImmediate(checkerTex);
                checkerTex = null;
            }

            // ── content: the level as textures ─────────────────────────────────
            /// Full re-render: per visible layer, one texture blitted through the SAME tile-to-pixels path
            /// every Cartographer preview uses (CartographerPreview). Called on open, structural edits
            /// (Dial), undo/redo, stamp/decal changes — pointer-drag painting goes through RefreshCells
            /// instead and never pays for this.
            internal void RebuildContent()
            {
                foreach (var lv in layerViews) if (lv.tex != null) DestroyImmediate(lv.tex);
                layerViews.Clear();
                layerByName.Clear();
                content.Clear();

                var level = w.level;
                if (level == null)
                {
                    hint.text = "";
                    checker.style.display = DisplayStyle.None;
                    RepaintOverlay();
                    return;
                }

                // Mid-gesture rebuilds (a stamp drag, an erase that removed a placement) keep the current
                // view rect — recomputing could shrink the canvas and shift the cell under the cursor.
                if (!w.dragging || viewRect.width <= 0) viewRect = ComputeViewRect(level);

                var ordered = new List<LevelLayer>();
                if (level.layers != null)
                    foreach (var l in level.layers) if (DrawnHere(l)) ordered.Add(l);
                ordered.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));

                // Resolve once; the first resolvable sprite fixes the cell pixel size, exactly like
                // CartographerPreview.RenderLevel.
                var resolved = new List<Dictionary<Vector2Int, ResolvedCell>>();
                Sprite reference = null;
                foreach (var l in ordered)
                {
                    var cells = level.ResolveLayer(l);
                    resolved.Add(cells);
                    if (reference == null)
                        foreach (var rc in cells.Values)
                        {
                            reference = CartographerPreview.SpriteOfResolved(rc);
                            if (reference != null) break;
                        }
                }
                cellPx = CartographerPreview.CellPixels(reference);
                int longest = Mathf.Max(viewRect.width, viewRect.height, 1);
                cellPx = Mathf.Clamp(cellPx, 1, Mathf.Max(1, MaxTexSide / longest));
                clearBlock = new Color32[cellPx * cellPx];

                var items = new List<(float key, int seq, VisualElement el)>();
                int seq = 0;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var layer = ordered[i];
                    // Every visible layer gets a texture even while empty — the paint fast path needs
                    // somewhere to land without a full rebuild.
                    var tex = new Texture2D(viewRect.width * cellPx, viewRect.height * cellPx,
                        TextureFormat.RGBA32, false)
                        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                    tex.SetPixels32(new Color32[tex.width * tex.height]);
                    foreach (var kv in resolved[i])
                    {
                        if (!viewRect.Contains(kv.Key)) continue;
                        var s = CartographerPreview.SpriteOfResolved(kv.Value);
                        if (s == null) continue;
                        CartographerPreview.Blit(tex, s,
                            (kv.Key.x - viewRect.xMin) * cellPx, (kv.Key.y - viewRect.yMin) * cellPx, cellPx);
                    }
                    tex.Apply();

                    var img = new Image { image = tex, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                    img.style.position = Position.Absolute;
                    img.style.left = 0f;
                    img.style.top = 0f;
                    img.style.width = tex.width;
                    img.style.height = tex.height;
                    img.tintColor = EditorTint(layer);

                    layerViews.Add(new LayerView { layer = layer, tex = tex, img = img });
                    layerByName[layer.name] = layerViews[layerViews.Count - 1];
                    items.Add((layer.sortingOrder, seq++, img));
                }

                if (level.decals != null)
                    foreach (var d in level.decals)
                    {
                        var el = BuildDecal(d);
                        if (el == null) continue;
                        var layer = level.GetLayer(d.layer);
                        // Nudged in front of its own layer, like the built SpriteRenderer sorts.
                        float key = (layer != null ? layer.sortingOrder : 0) + d.sortingOrder + 0.25f;
                        items.Add((key, seq++, el));
                    }

                items.Sort((a, b) => a.key != b.key ? a.key.CompareTo(b.key) : a.seq.CompareTo(b.seq));
                foreach (var it in items) content.Add(it.el);

                RebuildMarkers(level);
                hint.text = ordered.Count == 0 ? "Add a layer in the Layers box to start painting." : "";
                ApplyView();
                RepaintOverlay();
            }

            /// Find every cell a placement OCCUPIES but does not DRAW. A procgen mutator, a spawn point, a
            /// trigger volume — these carry no tile, so without this they are invisible on the map and the
            /// author has no way to see, move or even remember them. Each such cell gets its prop's marker
            /// colour in the overlay.
            ///
            /// The rule is per-CELL, not per-prop: a prop that draws some cells and not others gets marked
            /// exactly on the ones that would otherwise vanish. A prop with no cells at all is marked at its
            /// placement cell, since that is the whole of where it is.
            void RebuildMarkers(LevelAsset level)
            {
                markers.Clear();
                if (level.placements == null) return;
                foreach (var p in level.placements)
                {
                    if (p?.prop == null) continue;
                    if (p.prop.cells == null || p.prop.cells.Count == 0)
                    {
                        // The placement's OWN layer, recorded when it was stamped. Placements made before
                        // that field existed carry none, and fall back to unhideable-but-visible rather
                        // than vanishing — losing track of a marker is worse than one that will not hide.
                        markers[p.cell] = (p, p.layer);
                        continue;
                    }
                    foreach (var cd in p.prop.cells)
                    {
                        if (cd == null || cd.tile != null) continue;
                        markers[p.cell + Prop.TransformOffset(cd.offset, p.rotation, p.mirrorX)] = (p, cd.layer);
                    }
                }
            }

            /// A decal drawn at its fractional grid position: sized by its sprite's PPU against the cell
            /// size, rotated and scaled around its pivot — the same convention LevelInstance.SpawnDecals
            /// builds, so the pane and the mirror agree.
            VisualElement BuildDecal(Decal d)
            {
                var sprite = d == null ? null : d.IsAnimated ? d.animation[0] : d.sprite;
                if (sprite == null) return null;
                float ppu = Mathf.Max(0.0001f, sprite.pixelsPerUnit);
                float wPx = sprite.rect.width / ppu * cellPx;
                float hPx = sprite.rect.height / ppu * cellPx;
                var pivot = new Vector2(
                    sprite.pivot.x / Mathf.Max(1f, sprite.rect.width),
                    sprite.pivot.y / Mathf.Max(1f, sprite.rect.height));

                var img = new Image { sprite = sprite, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                img.style.position = Position.Absolute;
                img.style.width = wPx;
                img.style.height = hPx;
                img.style.left = (d.position.x - viewRect.xMin) * cellPx - pivot.x * wPx;
                img.style.top = (viewRect.yMax - d.position.y) * cellPx - (1f - pivot.y) * hPx;
                img.style.transformOrigin = new TransformOrigin(
                    Length.Percent(pivot.x * 100f), Length.Percent((1f - pivot.y) * 100f));
                if (!Mathf.Approximately(d.rotation, 0f))
                    img.style.rotate = new Rotate(new Angle(-d.rotation, AngleUnit.Degree));   // world CCW → UI CW
                if (d.scale != Vector2.one)
                    img.style.scale = new Scale(new Vector3(d.scale.x, d.scale.y, 1f));
                return img;
            }

            /// The paint fast path: re-resolve just the touched cells into their layer textures — the
            /// pane's twin of LevelInstance.RefreshCell. A cell within a cell of the rim flags a GROW,
            /// honoured only after the gesture ends (a mid-drag refit would shift the cell under the
            /// cursor). Known limit shared with RenderLevel: a tile whose sprite is larger than the cell
            /// spills past its cleared block, so erasing one leaves the spill until the next full render.
            /// Re-derive the metadata-only markers and repaint them, WITHOUT the full texture rebuild.
            ///
            /// Needed because `markers` was only ever repopulated by RebuildContent, while a stamp refreshes
            /// per-cell for speed — so a freshly placed prop's marker did not appear until something else
            /// forced a full rebuild. For a prop that also draws tiles the tile showed up and hid the bug;
            /// for a CELL-LESS prop (a spawn point, a procgen director) absolutely nothing appeared and the
            /// stamp looked like it had failed, though the placement was in the asset all along.
            internal void RefreshMarkers()
            {
                if (w.level == null) return;
                RebuildMarkers(w.level);
                RepaintOverlay();
            }

            internal void RefreshCells(List<(string lyr, Vector2Int at)> touched)
            {
                if (w.level == null || touched == null || touched.Count == 0) return;
                foreach (var (lyr, at) in touched)
                {
                    if (at.x <= viewRect.xMin || at.y <= viewRect.yMin ||
                        at.x >= viewRect.xMax - 1 || at.y >= viewRect.yMax - 1)
                        pendingGrow = true;
                    if (!layerByName.TryGetValue(lyr, out var lv) || lv.tex == null) continue;
                    if (!viewRect.Contains(at)) continue;
                    int x = (at.x - viewRect.xMin) * cellPx, y = (at.y - viewRect.yMin) * cellPx;
                    lv.tex.SetPixels32(x, y, cellPx, cellPx, clearBlock);
                    if (w.level.ResolveCell(lv.layer, at, out var rc))
                    {
                        var s = CartographerPreview.SpriteOfResolved(rc);
                        if (s != null) CartographerPreview.Blit(lv.tex, s, x, y, cellPx);
                    }
                    lv.dirty = true;
                }
                foreach (var lv in layerViews)
                    if (lv.dirty) { lv.tex.Apply(); lv.dirty = false; }
                RepaintOverlay();
                if (pendingGrow && !w.dragging) { pendingGrow = false; RebuildContent(); }
            }

            /// The canvas covers the level's bounds plus everything painted/stamped/dropped beyond them
            /// (nothing in the asset may be invisible), padded so the level can grow outward by painting.
            static RectInt ComputeViewRect(LevelAsset level)
            {
                var b = level.bounds;
                int minX = b.xMin, minY = b.yMin, maxX = b.xMax - 1, maxY = b.yMax - 1;
                void Grow(Vector2Int c)
                {
                    if (c.x < minX) minX = c.x;
                    if (c.x > maxX) maxX = c.x;
                    if (c.y < minY) minY = c.y;
                    if (c.y > maxY) maxY = c.y;
                }
                if (level.paints != null)
                    foreach (var p in level.paints) if (p != null && p.tile != null) Grow(p.cell);
                if (level.placements != null)
                    foreach (var p in level.placements)
                    {
                        if (p?.prop?.cells == null) continue;
                        foreach (var cd in p.prop.cells)
                            if (cd != null) Grow(p.cell + Prop.TransformOffset(cd.offset, p.rotation, p.mirrorX));
                    }
                if (level.decals != null)
                    foreach (var d in level.decals)
                        if (d != null) Grow(new Vector2Int(Mathf.FloorToInt(d.position.x), Mathf.FloorToInt(d.position.y)));
                return new RectInt(minX - ViewPad, minY - ViewPad,
                    maxX - minX + 1 + 2 * ViewPad, maxY - minY + 1 + 2 * ViewPad);
            }

            // ── view: position the content per ZuiPanZoom's verdict ─────────────
            void ApplyView()
            {
                if (w.level == null || viewRect.width <= 0)
                {
                    checker.style.display = DisplayStyle.None;
                    return;
                }
                var sizePx = ContentPx;
                // The ruler gutter is reserved BEFORE the fit, so the strips sit beside the level instead of
                // on top of it — chrome that hides the cells it measures is worse than no chrome.
                view.InsetLeft = view.InsetTop = RulerSize;
                if (!view.Layout(sizePx)) return;   // degenerate geometry — keep the previous view intact
                hint.style.top = 12f + RulerSize;

                content.style.left = view.Origin.x;
                content.style.top = view.Origin.y;
                content.style.width = sizePx.x;
                content.style.height = sizePx.y;
                content.style.scale = new Scale(new Vector3(view.Scale, view.Scale, 1f));

                TilesetGridView.StyleAsChecker(checker, ref checkerTex);
                checker.style.display = DisplayStyle.Flex;
                checker.style.left = view.Origin.x;
                checker.style.top = view.Origin.y;
                checker.style.width = sizePx.x * view.Scale;
                checker.style.height = sizePx.y * view.Scale;
                RepaintOverlay();
            }

            // ── hit testing ─────────────────────────────────────────────────────
            /// Pointer → cell + fractional grid position (grid Y up, canvas Y down — the flip lives here
            /// and nowhere else).
            bool CellAt(Vector2 local, out Vector2Int cell, out Vector2 gridPos)
            {
                cell = default;
                gridPos = default;
                float step = cellPx * view.Scale;
                if (step <= 0.0001f || viewRect.width <= 0) return false;
                float fx = (local.x - view.Origin.x) / step + viewRect.xMin;
                float fy = viewRect.yMax - (local.y - view.Origin.y) / step;
                gridPos = new Vector2(fx, fy);
                cell = new Vector2Int(Mathf.FloorToInt(fx), Mathf.FloorToInt(fy));
                return viewRect.Contains(cell);
            }

            void UpdateHover(Vector2 local)
            {
                bool valid = CellAt(local, out var cell, out var gp);
                w.hoverValid = valid;
                if (!valid) return;

                // GUIDE SNAP, applied HERE and nowhere else. Everything downstream — the readout, the ghost's
                // outline and art, the span anchors, every commit — reads `hoverCell`, so snapping the one
                // value is what makes "what you see is what lands" survive the feature. Not while a guide is
                // in the hand (it is not in the set to snap to) and not for a right-drag marquee, which
                // places nothing and so has no footprint to butt against a line.
                if (!guideDrag && w.SnapActive)
                {
                    var snapped = w.SnapCellToGuides(cell, suppress: rightDown);
                    if (viewRect.Contains(snapped)) cell = snapped;
                }

                w.hoverCell = cell;         // kept at the last VALID cell, so a release just off the
                w.hoverGridPos = gp;        // edge still commits the span the ghost showed
            }

            // ── gestures: the ported scene-tool interaction, verbatim semantics ─
            /// True while a span tool (Line/Rect/Pick) owns the drag; the other tools marquee instead.
            bool SpanTool => w.tool == CanvasTool.Line || w.tool == CanvasTool.Rect || w.tool == CanvasTool.Pick;

            // THE BUTTON CONTRACT (decree 2026-08-03: "we need left click to be uncluttered").
            // LEFT is the TOOL and nothing else — press applies, drag keeps applying, so painting a wall is
            // one stroke again. RIGHT-DRAG marquees; RIGHT-CLICK (no movement) opens the Tool card, whose
            // own buttons act on the marquee. Modifiers stay on left: Alt erases, Ctrl picks. MIDDLE pans.
            // The multi-edit deliberately moved OUT of "left-click inside a selection" and into the card's
            // explicit Paint/Erase-selection buttons: with left painting on press, a click inside a
            // selection could not mean two things without guessing, and guessing is what cluttered it.
            void OnDown(PointerDownEvent e)
            {
                if (e.button == 2 || w.level == null) return;   // middle button belongs to ZuiPanZoom

                // GUIDES GET FIRST REFUSAL, and refuse almost always: only a plain left press inside a ruler
                // strip or within a few pixels of a guide line is theirs. Everywhere else this returns false
                // in one comparison and the tool keeps the gesture, which is the whole contract — a guide
                // that ate the paint gesture would make the feature a net loss.
                if (TryBeginGuide(e)) return;
                // A LEFT press on the ruler chrome must never fall through and paint. It matters because a
                // zoomed-in canvas really does have cells underneath the strips — the author cannot see what
                // they would be hitting, so hitting nothing is the only honest answer. A RIGHT press still
                // goes through to raise the Tool card, which is exactly where the Guides switches live.
                if (e.button == 0 && PointerInRuler(e.localPosition)) { e.StopPropagation(); return; }

                UpdateHover(e.localPosition);

                if (e.button == 1)
                {
                    // Do NOT open the card yet — a right press is ambiguous until the pointer moves.
                    rightDown = true;
                    rightMoved = false;
                    rightStart = e.position;
                    w.anchor = w.hoverCell;
                    w.marqueeing = false;
                    Focus();                       // so Esc can drop the selection this may create
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }

                if (!w.hoverValid) return;

                if (e.button == 0 && e.ctrlKey)
                {
                    // Ctrl always picks, from any tool — the ported modifier.
                    w.PickAt(w.hoverCell);
                    RepaintOverlay();
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;

                w.dragging = true;
                w.dragErasing = e.altKey || w.tool == CanvasTool.Erase;
                w.anchor = w.hoverCell;
                lastApplied = new Vector2Int(int.MinValue, int.MinValue);
                haveLastFootprint = false;
                Focus();
                // Touching the canvas with the TOOL drops the marquee. The selection exists to feed the
                // card's Paint/Erase-selection buttons; the moment freehand editing starts it is stale, and
                // a blue rectangle that outlives its purpose and cannot be dismissed by clicking is exactly
                // what "clearing a selection doesn't work" felt like.
                w.ClearCellSelection();
                if (!SpanTool) ApplyStroke(w.hoverCell);   // paint on press: a stroke starts where you pressed
                this.CapturePointer(e.pointerId);
                RepaintOverlay();
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (view.IsPanning) { w.UpdateCanvasStatus(); return; }
                if (UpdateGuideDrag(e)) return;   // a guide in the hand owns the pointer until it is dropped
                UpdateHover(e.localPosition);
                UpdateGuideHover(e.localPosition);

                if (rightDown)
                {
                    // A cell threshold, not a pixel one — the grid is the unit the user thinks in. The pixel
                    // guard is only there so a shaky click on one cell is still a click.
                    if (!rightMoved && (w.hoverCell != w.anchor ||
                                        ((Vector2)e.position - rightStart).sqrMagnitude > 16f))
                        rightMoved = true;
                    w.marqueeing = rightMoved;
                    RepaintOverlay();
                    w.UpdateCanvasStatus();
                    return;
                }

                // Left held: keep applying. The per-cell dedupe is what stops one drag filling the undo
                // stack with a hundred identical records.
                if (w.dragging && (e.pressedButtons & 1) != 0 && w.hoverValid && !SpanTool)
                    ApplyStroke(w.hoverCell);

                RepaintOverlay();
                w.UpdateCanvasStatus();
            }

            /// One cell of a left stroke, deduped: the same cell twice in a row is one edit, and a stamp
            /// tiles by FOOTPRINT so dragging lays props edge-to-edge instead of one per cell.
            void ApplyStroke(Vector2Int cell)
            {
                if (w.tool == CanvasTool.Stamp && !w.dragErasing)
                {
                    var foot = w.StampFootprintAt(cell);
                    if (haveLastFootprint && foot.Overlaps(lastFootprint)) return;
                    lastFootprint = foot;
                    haveLastFootprint = true;
                }
                else
                {
                    if (cell == lastApplied) return;
                    lastApplied = cell;
                }
                w.ApplyAt(cell);
            }

            void OnUp(PointerUpEvent e)
            {
                if (e.button == 2) return;
                if (EndGuideDrag(e)) return;

                if (e.button == 1 && rightDown)
                {
                    this.ReleasePointer(e.pointerId);
                    rightDown = false;
                    if (rightMoved && w.hoverValid) w.SetCellSelection(w.anchor, w.hoverCell);
                    else w.ShowToolCard(e.position);     // never moved → it was a click, so it is the card
                    rightMoved = false;
                    w.marqueeing = false;
                    RepaintOverlay();
                    w.UpdateCanvasStatus();
                    e.StopPropagation();
                    return;
                }

                if (!w.dragging) return;
                this.ReleasePointer(e.pointerId);
                w.dragging = false;

                if (SpanTool)
                {
                    // Span tools commit on release, against the same cell lists the ghost drew.
                    if (w.tool == CanvasTool.Line)
                        w.CommitCells(w.ThinToFootprint(LineCells(w.anchor, w.hoverCell)),
                                      w.dragErasing ? "Erase line" : "Paint line");
                    else if (w.tool == CanvasTool.Rect)
                        w.CommitCells(w.ThinToFootprint(RectCells(w.anchor, w.hoverCell)),
                                      w.dragErasing ? "Erase rect" : "Fill rect");
                    else
                        w.PickRegion(w.anchor, w.hoverCell);
                }

                haveLastFootprint = false;
                // The per-cell fast path goes around Dial, so tagged tiles painted during the gesture
                // refresh their tint here, once per gesture.
                if (w.showTagOverlay) w.RefreshTagOverlay();
                if (pendingGrow) { pendingGrow = false; RebuildContent(); }
                RepaintOverlay();
                w.UpdateCanvasStatus();
                e.StopPropagation();
            }

            void OnLeave(PointerLeaveEvent e)
            {
                w.hoverValid = false;
                ClearGuideHover();
                RepaintOverlay();
                w.UpdateCanvasStatus();
            }

            // ── overlay: grid, bounds, tag tints, ghost ─────────────────────────
            void PaintOverlay(MeshGenerationContext ctx)
            {
                if (w.level == null || viewRect.width <= 0) return;
                float step = cellPx * view.Scale;
                if (step <= 0.001f) return;
                var p = ctx.painter2D;
                p.lineJoin = LineJoin.Bevel;   // never bulk-stroke mitered closed paths — the Painter2D crash lesson

                Rect CellRect(Vector2Int cell) => CellRectOf(cell);   // shared with the ghost art layer

                void CellPath(Rect r)
                {
                    p.BeginPath();
                    p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin));
                    p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax));
                    p.ClosePath();
                }

                // Grid lattice: continuous full-length lines (the gapless-grid economy lesson from the
                // sheet canvas — never 4 segments per cell), static grey, gone when cells get tiny.
                if (w.showGridLines && step >= 4f)
                {
                    p.strokeColor = new Color(0.75f, 0.75f, 0.75f, 0.35f);
                    p.lineWidth = 1f;
                    p.BeginPath();
                    float x0 = view.Origin.x, y0 = view.Origin.y;
                    float wpx = viewRect.width * step, hpx = viewRect.height * step;
                    for (int c = 0; c <= viewRect.width; c++)
                    {
                        p.MoveTo(new Vector2(x0 + c * step, y0));
                        p.LineTo(new Vector2(x0 + c * step, y0 + hpx));
                    }
                    for (int r = 0; r <= viewRect.height; r++)
                    {
                        p.MoveTo(new Vector2(x0, y0 + r * step));
                        p.LineTo(new Vector2(x0 + wpx, y0 + r * step));
                    }
                    p.Stroke();
                }

                // The level's bounds — where the background fill stops. The canvas shows a margin beyond them,
                // so the true extent must stay visible.
                var b = w.level.bounds;
                var tl = CellRect(new Vector2Int(b.xMin, b.yMax - 1));
                var br = CellRect(new Vector2Int(b.xMax - 1, b.yMin));
                p.strokeColor = new Color(1f, 1f, 1f, 0.45f);
                p.lineWidth = 1.5f;
                CellPath(Rect.MinMaxRect(tl.xMin, tl.yMin, br.xMax, br.yMax));
                p.Stroke();

                // Metadata-only props: cells that occupy the map without drawing on it. Marked in the prop's
                // own colour, and brightened under the pointer so the author can tell WHICH invisible thing
                // they are touching when several overlap.
                var hoveredMarker = w.hoverValid && markers.TryGetValue(w.hoverCell, out var hm)
                    && MarkerVisible(hm.layer) ? hm.placement : null;
                foreach (var kv in markers)
                {
                    if (!viewRect.Contains(kv.Key)) continue;
                    if (!MarkerVisible(kv.Value.layer)) continue;   // hidden layer, or markers switched off
                    var r = CellRect(kv.Key);
                    var mc = kv.Value.placement.prop.MarkerColor;
                    bool hot = hoveredMarker != null && ReferenceEquals(kv.Value.placement, hoveredMarker);
                    p.fillColor = new Color(mc.r, mc.g, mc.b, hot ? 0.42f : 0.20f);
                    CellPath(r);
                    p.Fill();
                    p.strokeColor = new Color(mc.r, mc.g, mc.b, hot ? 1f : 0.75f);
                    p.lineWidth = hot ? 2f : 1f;
                    CellPath(r);
                    p.Stroke();
                    // A diagonal slash reads as "nothing is drawn here" even where the tint is subtle or
                    // the author is colour-blind to that hue.
                    p.strokeColor = new Color(mc.r, mc.g, mc.b, hot ? 0.9f : 0.5f);
                    p.lineWidth = 1f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(r.xMin + 2f, r.yMax - 2f));
                    p.LineTo(new Vector2(r.xMax - 2f, r.yMin + 2f));
                    p.Stroke();
                }

                // The marquee selection: the multi-edit target, held between gestures so it must read as a
                // persistent state rather than a passing ghost.
                if (w.cellSelection.Count > 0)
                {
                    p.fillColor = new Color(0.35f, 0.7f, 1f, 0.16f);
                    p.strokeColor = new Color(0.45f, 0.8f, 1f, 0.55f);
                    p.lineWidth = 1f;
                    foreach (var cell in w.cellSelection)
                    {
                        if (!viewRect.Contains(cell)) continue;
                        var r = CellRect(cell);
                        CellPath(r);
                        p.Fill();
                        CellPath(r);
                        p.Stroke();
                    }
                }

                // Tag overlay: first tag fills, additional tags draw inset outlines so a multi-tagged
                // cell reads — the scene overlay's convention, ported.
                if (w.showTagOverlay)
                    foreach (var (cell, colors) in w.tagOverlay)
                    {
                        if (!viewRect.Contains(cell)) continue;
                        var r = CellRect(cell);
                        for (int i = 0; i < colors.Count; i++)
                        {
                            float inset = 0.08f * i * step;
                            var rr = new Rect(r.x + inset, r.y + inset, r.width - 2f * inset, r.height - 2f * inset);
                            var c = colors[i];
                            if (i == 0)
                            {
                                p.fillColor = new Color(c.r, c.g, c.b, 0.22f);
                                CellPath(rr);
                                p.Fill();
                            }
                            p.strokeColor = new Color(c.r, c.g, c.b, 0.9f);
                            p.lineWidth = 1f;
                            CellPath(rr);
                            p.Stroke();
                        }
                    }

                // Ghost: outline exactly what the gesture would write — same cell lists as the commits. The
                // ART for those same cells is drawn by SyncGhost on the layer beneath this one; the two are a
                // pair, and the division of labour is that the art says WHAT and the outline says WHERE. The
                // outline is never dropped, because it is the only thing an eraser or an empty brush has.
                if (w.hoverValid || w.dragging)
                {
                    var cells = w.GhostCells();
                    bool erasing = !w.marqueeing && (w.tool == CanvasTool.Erase || (w.dragging && w.dragErasing));
                    // With art underneath, the tint is a locator, not the picture — at the old strength it
                    // washes the very tiles it is supposed to be previewing.
                    float fa = ghostArtCount > 0 ? 0.06f : 0.18f;
                    var fill = w.marqueeing ? new Color(0.35f, 0.7f, 1f, 0.20f)
                             : erasing ? new Color(1f, 0.3f, 0.25f, fa) : new Color(1f, 0.75f, 0.2f, fa);
                    var edge = w.marqueeing ? new Color(0.55f, 0.85f, 1f, 0.95f)
                             : erasing ? new Color(1f, 0.3f, 0.25f, 0.9f) : new Color(1f, 0.75f, 0.2f, 0.9f);
                    foreach (var cell in cells)
                    {
                        var r = CellRect(cell);
                        p.fillColor = fill;
                        CellPath(r);
                        p.Fill();
                        p.strokeColor = edge;
                        p.lineWidth = 1.5f;
                        CellPath(r);
                        p.Stroke();
                    }

                    // The decal tool drops at the exact fractional point — cross it inside the cell.
                    if (w.tool == CanvasTool.Decal && w.hoverValid)
                    {
                        float px = view.Origin.x + (w.hoverGridPos.x - viewRect.xMin) * step;
                        float py = view.Origin.y + (viewRect.yMax - w.hoverGridPos.y) * step;
                        p.strokeColor = edge;
                        p.lineWidth = 1f;
                        p.BeginPath();
                        p.MoveTo(new Vector2(px - 5f, py)); p.LineTo(new Vector2(px + 5f, py));
                        p.MoveTo(new Vector2(px, py - 5f)); p.LineTo(new Vector2(px, py + 5f));
                        p.Stroke();
                    }
                }

                // LAST: the guides and their ruler strips. Last because the strips are chrome in a reserved
                // gutter and must cover anything that spilled into it — a ghost tile at the level's edge, a
                // guide line's own end cap.
                PaintGuides(p);
            }
        }
    }
}
