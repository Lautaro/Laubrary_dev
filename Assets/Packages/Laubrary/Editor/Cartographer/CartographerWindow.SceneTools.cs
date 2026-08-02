using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Cartographer.Editor
{
    /// The Scene-view half of the Cartographer window: the Scene view is the canvas, this file is the brush.
    /// Ghost the tool's footprint at the hovered cell, click to apply, drag to continue, press-drag-release
    /// for a line or rect, Alt to erase, Ctrl to pick — the interaction ported from the old stamper, which
    /// was the best-tested part of the previous tool. Every edit lands on the ASSET through an undo record;
    /// the scene instance just refreshes.
    public partial class CartographerWindow
    {
        internal enum SceneTool { Paint, Erase, Line, Rect, Pick, Stamp, Decal }

        [SerializeField] SceneTool tool = SceneTool.Paint;
        [SerializeField] bool sceneEditing = true;
        [SerializeField] bool showTagOverlay;
        [SerializeField] int stampRotation;      // quarter-turns anticlockwise
        [SerializeField] bool stampMirrorX;
        [SerializeField] int variantOverride = -1;
        [SerializeField] Sprite decalSprite;

        VisualElement toolBox;

        // gesture state
        bool dragging;
        bool dragErasing;
        Vector2Int lastApplied = new(int.MinValue, int.MinValue);
        bool haveLastFootprint;
        RectInt lastFootprint;
        Vector2Int anchor;               // line/rect start
        Vector2Int hoverCell;
        Vector3 hoverGridPos;            // fractional grid coords, for decals
        bool hoverValid;

        LevelInstance cachedInstance;

        // tag-overlay cache: rebuilt on toggle and after every edit, never per-repaint
        readonly List<(Vector2Int cell, List<Color> colors)> tagOverlay = new();

        void OnEnableSceneTools() => SceneView.duringSceneGui += OnSceneGUI;
        void OnDisableSceneTools() => SceneView.duringSceneGui -= OnSceneGUI;

        /// Recompute which cells the overlay tints. Called on toggle and from Dial after edits.
        internal void RefreshTagOverlay()
        {
            tagOverlay.Clear();
            if (!showTagOverlay || level == null || level.layers == null) return;

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
            SceneView.RepaintAll();
        }

        void DrawTagOverlay(LevelInstance inst)
        {
            var g = inst.Grid;
            if (g == null || tagOverlay.Count == 0) return;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var size = g.cellSize;
            foreach (var (cell, colors) in tagOverlay)
            {
                var bl = g.CellToWorld(new Vector3Int(cell.x, cell.y, 0));
                for (int i = 0; i < colors.Count; i++)
                {
                    // First tag fills; additional tags draw inset outlines so a multi-tagged cell reads.
                    float inset = 0.08f * i * size.x;
                    var verts = new[]
                    {
                        bl + new Vector3(inset, inset, 0f),
                        bl + new Vector3(size.x - inset, inset, 0f),
                        bl + new Vector3(size.x - inset, size.y - inset, 0f),
                        bl + new Vector3(inset, size.y - inset, 0f),
                    };
                    var c = colors[i];
                    Handles.DrawSolidRectangleWithOutline(verts,
                        i == 0 ? new Color(c.r, c.g, c.b, 0.22f) : Color.clear,
                        new Color(c.r, c.g, c.b, 0.9f));
                }
            }

            // The "what is tagged under the cursor" half of the design's Tags box.
            if (hoverValid && level != null)
            {
                var names = new List<string>();
                foreach (var layer in level.layers)
                {
                    if (layer == null) continue;
                    foreach (var t in level.TagsAt(hoverCell, layer.name))
                        if (!names.Contains(t.name)) names.Add(t.name);
                }
                if (names.Count > 0)
                {
                    var pos = g.CellToWorld(new Vector3Int(hoverCell.x, hoverCell.y + 1, 0));
                    Handles.Label(pos, string.Join(", ", names));
                }
            }
        }

        internal void SetTool(SceneTool t)
        {
            tool = t;
            RebuildToolBox();
            SceneView.RepaintAll();
        }

        LevelInstance EditInstance()
        {
            if (cachedInstance != null && cachedInstance.level == level) return cachedInstance;
            cachedInstance = SceneInstance();
            return cachedInstance;
        }

        // ── the Tool box ───────────────────────────────────────────────────────
        void RebuildToolBox()
        {
            if (toolBox == null) return;
            toolBox.Clear();

            toolBox.Add(Z.ToggleButton("Edit in Scene view", "While on, clicks in the Scene view apply the tool " +
                "below instead of selecting objects. Alt always erases; Ctrl always picks.",
                sceneEditing, v => { sceneEditing = v; SceneView.RepaintAll(); }));

            toolBox.Add(Z.MiniRadio((int)tool,
                new[] { "Paint", "Erase", "Line", "Rect", "Pick", "Stamp", "Decal" },
                "Paint: one cell per click or drag. Erase: clear a cell's paint, or a whole stamped prop. " +
                "Line / Rect: press, drag the span out, release to commit. Pick: eyedrop a painted tile. " +
                "Stamp: place the selected prop. Decal: drop the sprite below at the exact click point.",
                i => SetTool((SceneTool)i), wrap: true));

            if (tool == SceneTool.Stamp)
            {
                toolBox.Add(Z.Field("Rotation", "Quarter-turns applied to the stamp, anticlockwise.",
                    Z.MiniRadio(stampRotation, new[] { "0°", "90°", "180°", "270°" },
                        "Quarter-turns applied to the stamp.", i => { stampRotation = i; SceneView.RepaintAll(); })));
                toolBox.Add(Z.ToggleButton("Mirror X", "Flip the stamp horizontally before rotating it.",
                    stampMirrorX, v => { stampMirrorX = v; SceneView.RepaintAll(); }));
            }

            if (tool == SceneTool.Paint || tool == SceneTool.Line || tool == SceneTool.Rect)
                toolBox.Add(Z.Field("Variant", "-1 lets the tile's own policy pick a variant per cell; 0 and " +
                    "up forces that variant for every cell this tool paints.",
                    Z.Int(variantOverride, "Forced variant index, or -1 for the tile's policy.",
                        v => variantOverride = Mathf.Max(-1, v), 46f)));

            if (tool == SceneTool.Decal)
                toolBox.Add(Z.Field("Sprite", "The sprite the next click places as a decal on the active layer.",
                    Z.Object<Sprite>(decalSprite, "Sprite the Decal tool places.",
                        v => decalSprite = v, 170f)));

            var inst = EditInstance();
            if (inst == null)
                toolBox.Add(Z.Text("No scene preview — press 'Preview in scene' in the Level box first.",
                    ZuiText.Subtle, "Scene-view editing needs a LevelInstance of this level in the open scene."));
        }

        // ── scene GUI ──────────────────────────────────────────────────────────
        void OnSceneGUI(SceneView view)
        {
            if (level == null) return;
            var inst = EditInstance();
            if (inst == null) return;

            if (showTagOverlay)
            {
                hoverValid = TryCellUnderMouse(inst, Event.current.mousePosition, out hoverCell, out hoverGridPos);
                DrawTagOverlay(inst);
            }
            if (!sceneEditing) return;

            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

            hoverValid = TryCellUnderMouse(inst, e.mousePosition, out hoverCell, out hoverGridPos);
            if (hoverValid) DrawGhost(inst);
            if (e.type == EventType.MouseMove) { view.Repaint(); return; }
            if (!hoverValid) return;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !e.control:
                    dragging = true;
                    dragErasing = e.alt || tool == SceneTool.Erase;
                    haveLastFootprint = false;
                    lastApplied = new Vector2Int(int.MinValue, int.MinValue);
                    anchor = hoverCell;
                    if (tool != SceneTool.Line && tool != SceneTool.Rect && tool != SceneTool.Pick)
                        ApplyAt(inst, hoverCell);
                    e.Use();
                    break;

                case EventType.MouseDown when e.button == 0 && e.control:
                    PickAt(hoverCell);
                    e.Use();
                    break;

                case EventType.MouseDrag when dragging && e.button == 0:
                    if (tool != SceneTool.Line && tool != SceneTool.Rect && tool != SceneTool.Pick)
                        ApplyAt(inst, hoverCell);
                    view.Repaint();
                    e.Use();
                    break;

                case EventType.MouseUp when dragging:
                    if (tool == SceneTool.Line) CommitCells(inst, LineCells(anchor, hoverCell), dragErasing ? "Erase line" : "Paint line");
                    else if (tool == SceneTool.Rect) CommitCells(inst, RectCells(anchor, hoverCell), dragErasing ? "Erase rect" : "Fill rect");
                    else if (tool == SceneTool.Pick) PickRegion(anchor, hoverCell);
                    dragging = false;
                    haveLastFootprint = false;
                    e.Use();
                    break;
            }
        }

        // ── applying ───────────────────────────────────────────────────────────
        void ApplyAt(LevelInstance inst, Vector2Int cell)
        {
            switch (tool)
            {
                case SceneTool.Stamp when !dragErasing:
                    ApplyStamp(inst, cell);
                    break;

                case SceneTool.Decal when !dragErasing:
                    if (cell == lastApplied) return;
                    lastApplied = cell;
                    PlaceDecal(inst);
                    break;

                default:
                    if (cell == lastApplied) return;   // a drag across one cell must not spam the undo stack
                    lastApplied = cell;
                    if (dragErasing) EraseAt(inst, cell);
                    else PaintAt(inst, cell);
                    break;
            }
        }

        /// Paint the whole BRUSH anchored at `cell` — one tile is plain painting, several are the temporary
        /// prop (a multi-selected palette pattern or a sampled region) stamped as one gesture.
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

        void PaintAt(LevelInstance inst, Vector2Int cell)
        {
            var layer = ActiveLayer;
            if (layer == null || layer.locked || Brush.Count == 0) return;

            Undo.RecordObject(level, "Paint");
            var touched = new List<(string lyr, Vector2Int at)>();
            foreach (var (off, tile, shift) in Brush)
            {
                if (tile == null) continue;
                var target = ShiftedLayer(layer, shift);
                if (variantOverride >= 0) level.SetPaint(target.name, cell + off, tile, (byte)variantOverride);
                else level.Paint(target.name, cell + off, tile);
                touched.Add((target.name, cell + off));
            }
            EditorUtility.SetDirty(level);
            foreach (var (lyr, at) in touched) inst.RefreshCell(lyr, at);
        }

        /// Erase the active layer's paint at the cell; when there is none, remove the most recent stamped
        /// prop covering it instead (rebuilding, because two overlapping stamps share cells and erasing one
        /// by footprint would punch holes in the other — the old tool's hard-won rule).
        void EraseAt(LevelInstance inst, Vector2Int cell)
        {
            var layer = ActiveLayer;
            if (layer == null || layer.locked) return;

            // Record BEFORE any mutation, so both the paint and the placement paths sit inside the record.
            Undo.RecordObject(level, "Erase");
            bool removedPaint = false;
            if (level.paints != null)
                for (int i = level.paints.Count - 1; i >= 0; i--)
                {
                    var p = level.paints[i];
                    if (p != null && p.layer == layer.name && p.cell == cell) { level.paints.RemoveAt(i); removedPaint = true; break; }
                }

            if (removedPaint)
            {
                EditorUtility.SetDirty(level);
                inst.RefreshCell(layer.name, cell);
                return;
            }

            int found = -1;
            for (int i = level.placements.Count - 1; i >= 0 && found < 0; i--)
            {
                var p = level.placements[i];
                if (p?.prop?.cells == null) continue;
                foreach (var cd in p.prop.cells)
                {
                    if (cd == null) continue;
                    if (p.cell + Prop.TransformOffset(cd.offset, p.rotation, p.mirrorX) == cell) { found = i; break; }
                }
            }
            if (found < 0) return;

            level.placements.RemoveAt(found);
            EditorUtility.SetDirty(level);
            inst.Rebuild();
            RebuildLevelBox();
        }

        void PickAt(Vector2Int cell)
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            if (!level.ResolveCell(layer, cell, out var rc) || rc.tile is not LevelTile lt) return;
            SetBrushSingle(lt);
            if (tool == SceneTool.Pick) SetTool(SceneTool.Paint);
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
            if (cells.Count == 0) return;

            SetBrushSample(cells);
            SetTool(SceneTool.Paint);
        }

        void CommitCells(LevelInstance inst, List<Vector2Int> cells, string label)
        {
            var layer = ActiveLayer;
            if (layer == null || layer.locked || cells.Count == 0) return;
            if (!dragErasing && Brush.Count == 0) return;

            Undo.RecordObject(level, label);
            var touched = new HashSet<(string lyr, Vector2Int at)>();
            foreach (var cell in cells)
            {
                if (dragErasing)
                {
                    for (int i = level.paints.Count - 1; i >= 0; i--)
                    {
                        var p = level.paints[i];
                        if (p != null && p.layer == layer.name && p.cell == cell) { level.paints.RemoveAt(i); break; }
                    }
                    touched.Add((layer.name, cell));
                }
                else foreach (var (off, tile, shift) in Brush)
                {
                    if (tile == null) continue;
                    var target = ShiftedLayer(layer, shift);
                    if (variantOverride >= 0) level.SetPaint(target.name, cell + off, tile, (byte)variantOverride);
                    else level.Paint(target.name, cell + off, tile);
                    touched.Add((target.name, cell + off));
                }
            }
            EditorUtility.SetDirty(level);
            foreach (var (lyr, at) in touched) inst.RefreshCell(lyr, at);
        }

        void ApplyStamp(LevelInstance inst, Vector2Int cell)
        {
            if (StampProp == null) return;

            // Overlap rejection, not one-per-cell: a drag tiles stamps edge-to-edge in whatever direction
            // the cursor moves, and still stamps every cell for a 1x1 prop.
            var foot = FootprintAt(cell);
            if (haveLastFootprint && Overlaps(foot, lastFootprint)) return;

            Undo.RecordObject(level, "Stamp prop");
            level.Place(StampProp, cell, stampRotation, stampMirrorX);
            EditorUtility.SetDirty(level);
            lastFootprint = foot;
            haveLastFootprint = true;
            inst.Rebuild();
            RebuildLevelBox();
        }

        void PlaceDecal(LevelInstance inst)
        {
            if (decalSprite == null || ActiveLayer == null) return;
            Undo.RecordObject(level, "Place decal");
            level.decals.Add(new Decal
            {
                sprite = decalSprite,
                position = new Vector2(hoverGridPos.x, hoverGridPos.y),
                layer = ActiveLayer.name,
            });
            EditorUtility.SetDirty(level);
            inst.Rebuild();
            RebuildDecals();
        }

        // ── geometry ───────────────────────────────────────────────────────────
        bool TryCellUnderMouse(LevelInstance inst, Vector2 mousePosition, out Vector2Int cell, out Vector3 gridPos)
        {
            cell = default;
            gridPos = default;
            var g = inst.Grid;
            if (g == null) return false;

            var t = inst.transform;
            var plane = new Plane(t.forward, t.position);
            var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (!plane.Raycast(ray, out float dist)) return false;

            var world = ray.GetPoint(dist);
            var c = g.WorldToCell(world);
            cell = new Vector2Int(c.x, c.y);
            gridPos = g.LocalToCellInterpolated(g.transform.InverseTransformPoint(world));
            return true;
        }

        RectInt FootprintAt(Vector2Int origin)
        {
            var c = StampProp;
            if (c?.cells == null || c.cells.Count == 0) return new RectInt(origin.x, origin.y, 1, 1);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var cd in c.cells)
            {
                if (cd == null) continue;
                var o = Prop.TransformOffset(cd.offset, stampRotation, stampMirrorX);
                minX = Mathf.Min(minX, o.x); maxX = Mathf.Max(maxX, o.x);
                minY = Mathf.Min(minY, o.y); maxY = Mathf.Max(maxY, o.y);
            }
            if (minX > maxX) return new RectInt(origin.x, origin.y, 1, 1);
            return new RectInt(origin.x + minX, origin.y + minY, maxX - minX + 1, maxY - minY + 1);
        }

        static bool Overlaps(RectInt a, RectInt b) =>
            a.xMin < b.xMax && b.xMin < a.xMax && a.yMin < b.yMax && b.yMin < a.yMax;

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

        static List<Vector2Int> RectCells(Vector2Int a, Vector2Int b)
        {
            var cells = new List<Vector2Int>();
            for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
                for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
                    cells.Add(new Vector2Int(x, y));
            return cells;
        }

        // ── ghost ──────────────────────────────────────────────────────────────
        /// Outline exactly what the gesture would write — the preview and the commit share the same cell
        /// lists, so they cannot drift apart.
        void DrawGhost(LevelInstance inst)
        {
            var g = inst.Grid;
            if (g == null) return;

            List<Vector2Int> cells;
            if (tool == SceneTool.Stamp && StampProp?.cells != null)
            {
                cells = new List<Vector2Int>();
                foreach (var cd in StampProp.cells)
                {
                    if (cd == null) continue;
                    cells.Add(hoverCell + Prop.TransformOffset(cd.offset, stampRotation, stampMirrorX));
                }
            }
            else if (tool == SceneTool.Line && dragging) cells = LineCells(anchor, hoverCell);
            else if ((tool == SceneTool.Rect || tool == SceneTool.Pick) && dragging) cells = RectCells(anchor, hoverCell);
            else if (tool == SceneTool.Paint && Brush.Count > 1)
            {
                // The pattern brush ghosts its whole arrangement — what is outlined is what lands.
                cells = new List<Vector2Int>();
                foreach (var (off, _, _) in Brush) cells.Add(hoverCell + off);
            }
            else cells = new List<Vector2Int> { hoverCell };

            bool erasing = tool == SceneTool.Erase || (dragging && dragErasing);
            var fill = erasing ? new Color(1f, 0.3f, 0.25f, 0.18f) : new Color(1f, 0.75f, 0.2f, 0.18f);
            var edge = erasing ? new Color(1f, 0.3f, 0.25f, 0.9f) : new Color(1f, 0.75f, 0.2f, 0.9f);

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var size = g.cellSize;
            foreach (var cell in cells)
            {
                var bl = g.CellToWorld(new Vector3Int(cell.x, cell.y, 0));
                var verts = new[]
                {
                    bl,
                    bl + new Vector3(size.x, 0f, 0f),
                    bl + new Vector3(size.x, size.y, 0f),
                    bl + new Vector3(0f, size.y, 0f),
                };
                Handles.DrawSolidRectangleWithOutline(verts, fill, edge);
            }
        }
    }
}
