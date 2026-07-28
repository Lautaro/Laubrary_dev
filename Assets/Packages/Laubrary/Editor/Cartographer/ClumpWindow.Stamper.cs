using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace Laubrary.Cartographer.Editor
{
    /// The Stamper half of the Clump Editor: create a level in the open scene, then click in the Scene view to
    /// stamp the current clump into it.
    ///
    /// This is the thing Cartographer adds over vanilla Unity — Tile Palette paints ONE tile at a time, with no
    /// notion of a multi-cell arrangement carrying tags, a spawn hook and named spots. Placement goes through
    /// `CartographerLevel.PlaceClump`, the same call a generator will use, so hand-built and generated levels
    /// stay one code path.
    public partial class ClumpWindow
    {
        [SerializeField] CartographerLevel level;

        bool stamping;
        int stampRotation;      // quarter-turns anticlockwise
        bool stampMirrorX;
        Vector2Int hoverCell;
        bool hoverValid;

        // drag-repeat state: the footprint of the last stamp laid down in this drag
        bool dragging;
        bool dragErasing;
        bool haveLastFootprint;
        RectInt lastFootprint;

        // line mode: nothing is committed until release, so the span can be adjusted while dragging
        bool lineMode;
        Vector2Int lineAnchor;

        void OnEnableStamper() => SceneView.duringSceneGui += OnSceneGUI;
        void OnDisableStamper() => SceneView.duringSceneGui -= OnSceneGUI;

        // ── the Level box (creation lives here, not in a menu command) ────────
        void RebuildLevelBox()
        {
            if (levelBox == null) return;
            levelBox.Clear();

            levelBox.Add(Z.Field("Target", "The CartographerLevel in the open scene that stamping writes into.",
                Z.Object<CartographerLevel>(level, "Level to stamp into.",
                    v => { level = v; RebuildLevelBox(); RebuildLayers(); },   // the brush offers the level's layers too
                    180f, allowSceneObjects: true)));

            if (level == null)
            {
                levelBox.Add(Z.Text("No level in the scene yet.", ZuiText.Subtle,
                    "Stamping needs a level to write into."));
                levelBox.Add(Z.Button("Create level in scene", "Builds a Grid with Terrain and Structures tilemaps, wired to a CartographerLevel, and selects it.",
                    CreateLevelInScene));
                return;
            }

            levelBox.Add(Z.Toggle("Stamp mode", "While on, clicking in the Scene view stamps the current clump. " +
                "Hold Alt to erase the clump under the cursor.", stamping, v => { stamping = v; SceneView.RepaintAll(); }));

            levelBox.Add(Z.Field("Rotation", "Quarter-turns applied to the stamp, anticlockwise.",
                Z.MiniRadio(stampRotation, new[] { "0°", "90°", "180°", "270°" },
                    "Quarter-turns applied to the stamp.", i => { stampRotation = i; SceneView.RepaintAll(); })));

            levelBox.Add(Z.Toggle("Mirror X", "Flip the stamp horizontally before rotating it.",
                stampMirrorX, v => { stampMirrorX = v; SceneView.RepaintAll(); }));

            levelBox.Add(Z.Toggle("Line mode", "Press, drag out a straight run, release to commit it. Nothing is " +
                "placed until you let go, so the span can be adjusted first. Off: each drag stamps as it goes.",
                lineMode, v => { lineMode = v; SceneView.RepaintAll(); }));

            levelBox.Add(Z.Text($"{level.placements.Count} placement(s)", ZuiText.Small,
                "How many clumps have been stamped into this level."));

            levelBox.Add(Z.Row(
                Z.Button("Rebuild", "Clear and re-stamp every recorded placement — use after editing a clump.",
                    () => { Undo.RegisterCompleteObjectUndo(level, "Rebuild level"); level.RebuildFromPlacements(); RebuildLevelBox(); }),
                Z.Button("Build colliders", "Give every solid layer the colliders its collision mode calls for.",
                    () => { level.BuildColliders(); EditorUtility.SetDirty(level); })));

            BuildLayerList();
            BuildRoomList();
            BuildGenerateBox();
        }

        [SerializeField] LevelRecipe recipe;
        [SerializeField] int genSeed = 1234;

        /// Generate the target level from a recipe. Same recipe + same seed always rebuilds the same level, so
        /// a seed is a level's whole identity — worth having in reach while authoring, to check that a recipe
        /// produces good levels across seeds rather than one lucky one.
        void BuildGenerateBox()
        {
            levelBox.Add(Z.Box("Generate", "Fill this level procedurally from a recipe. Replaces its contents.",
                Z.Field("Recipe", "Seed-and-dials description of a KIND of level.",
                    LauAssetElement.Build(recipe,
                        picked => { recipe = picked as LevelRecipe; RebuildLevelBox(); },
                        typeof(LevelRecipe), _fieldThumbs, "Recipe", "Assets/Cartographer/Recipes",
                        "Recipe to generate from.")),
                Z.Row(
                    Z.Field("Seed", "Same recipe and seed always produce the same level.",
                        Z.Int(genSeed, "Generation seed.", v => genSeed = v, 80f)),
                    Z.Button("Roll", "Pick a new random seed.",
                        () => { genSeed = Random.Range(0, 1_000_000); RebuildLevelBox(); })),
                Z.Button("Generate", "Discard this level's contents and rebuild it from the recipe and seed.",
                    () =>
                    {
                        if (recipe == null || level == null) return;
                        RecordLevelForUndo("Generate level");
                        int n = LevelGenerator.Generate(recipe, genSeed, level);
                        level.BuildColliders();
                        AfterLevelChange();
                        Debug.Log($"[Cartographer] Generated '{level.name}' from '{recipe.name}' seed {genSeed}: {n} clumps placed.", level);
                    })));
        }

        /// The level's Rooms — the sections play moves through. Without this the Room schema is unreachable
        /// data: you could design an exit condition and a scroll spec in code and never author one.
        void BuildRoomList()
        {
            var rows = new VisualElement();

            for (int i = 0; i < level.rooms.Count; i++)
            {
                int idx = i;
                var r = level.rooms[idx];
                if (r == null) continue;

                var body = new VisualElement();

                body.Add(Z.Row(
                    Z.TextInput(r.roomName, "Name for this section, for your own reference.",
                        v => LevelEdit("Rename room", () => r.roomName = v), 110f),
                    LauAssetElement.Build(r.biome,
                        picked => { LevelEdit("Set room biome", () => r.biome = picked as CartographerBiome); RebuildLevelBox(); },
                        typeof(CartographerBiome), _fieldThumbs, "Biome", "Assets/Cartographer/Biomes",
                        "Which biome's tiles and clumps this section draws from."),
                    Z.Button("×", "Remove this section.",
                        () => { LevelEdit("Remove room", () => level.rooms.RemoveAt(idx)); RebuildLevelBox(); }).W(24f)));

                body.Add(Z.Field("Ends on", "What finishes this section and moves play into the next.",
                    Z.MiniRadio((int)r.exit, new[] { "Distance", "Time", "Clear enemies" },
                        "Exit condition for this section.",
                        v => { LevelEdit("Set room exit", () => r.exit = (RoomExit)v); RebuildLevelBox(); })));

                if (r.exit == RoomExit.Distance)
                    body.Add(Z.MicroSlider("Distance", r.exitDistance, 1f, 200f,
                        "World units the view travels before this section ends.",
                        v => LevelEdit("Set room distance", () => r.exitDistance = v), 150f));
                else if (r.exit == RoomExit.Time)
                    body.Add(Z.MicroSlider("Seconds", r.exitSeconds, 1f, 120f,
                        "Seconds to survive before this section ends.",
                        v => LevelEdit("Set room seconds", () => r.exitSeconds = v), 150f));

                body.Add(Z.Field("Scroll", "How the view moves through this section.",
                    Z.MiniRadio((int)r.scroll, new[] { "Auto", "Locked", "Player-pushed" },
                        "Scroll behaviour, independent of the exit condition.",
                        v => { LevelEdit("Set room scroll", () => r.scroll = (RoomScroll)v); RebuildLevelBox(); })));

                if (r.scroll == RoomScroll.Auto)
                    body.Add(Z.MicroSlider("Speed", r.scrollSpeed, 0f, 20f, "View speed in world units per second.",
                        v => LevelEdit("Set scroll speed", () => r.scrollSpeed = v), 150f));

                body.Add(Z.Field("Caught by edge", "What happens when an advancing view catches the player.",
                    Z.MiniRadio((int)r.catchUp, new[] { "Lock", "Push", "Kill", "Wait" },
                        "Trailing-edge behaviour for this section.",
                        v => LevelEdit("Set catch-up", () => r.catchUp = (ScrollCatchUp)v), wrap: true)));

                rows.Add(Z.Box($"{idx + 1}. {r.roomName}", "One section of this level.", body));
            }

            if (level.rooms.Count == 0)
                rows.Add(Z.Text("No sections yet — the level is one continuous space.", ZuiText.Subtle,
                    "Rooms are optional; a level with none just has no scroll direction of its own."));

            levelBox.Add(Z.Box("Rooms", "The sections play moves through, in order.",
                rows,
                Z.Button("+ Add room", "Append a section after the last one.",
                    () => { LevelEdit("Add room", () => level.rooms.Add(new CartographerRoom())); RebuildLevelBox(); })));
        }

        /// The level's drawing layers — add, rename, set solid/one-way, remove. Without this a level is stuck
        /// with whatever the scaffold made, and a side-scroller wanting a one-way Platforms layer has nowhere
        /// to declare it.
        void BuildLayerList()
        {
            var rows = new VisualElement();

            for (int i = 0; i < level.layers.Count; i++)
            {
                int idx = i;
                var l = level.layers[idx];
                if (l == null) continue;

                rows.Add(Z.Row(
                    Z.TextInput(l.layerName, "Name clumps address this layer by. Must match the layer a clump's cells target.",
                        v => LevelEdit("Rename layer", () => l.layerName = v), 100f),
                    Z.Toggle("Solid", "Give this layer colliders when Build colliders runs.",
                        l.solid, v => { LevelEdit("Set layer solid", () => l.solid = v); RebuildLevelBox(); }),
                    Z.Toggle("One-way", "Land on it from above, jump up through it. Side-scroll mode only.",
                        l.oneWay, v => LevelEdit("Set layer one-way", () => l.oneWay = v)),
                    Z.Button("×", "Remove this layer and destroy its tilemap. Undoable.",
                        () => RemoveLayer(idx)).W(24f)));

                if (l.solid)
                    rows.Add(Z.Field("  Shape", "Square: every tile collides as a full block. Sprite outline: each " +
                        "tile collides on its own shape, which is how a triangular tile becomes a walkable slope.",
                        Z.MiniRadio(l.colliderShape == UnityEngine.Tilemaps.Tile.ColliderType.Sprite ? 1 : 0,
                            new[] { "Square", "Sprite outline" }, "Collider shape for this layer's tiles.",
                            i => LevelEdit("Set collider shape", () => l.colliderShape = i == 1
                                ? UnityEngine.Tilemaps.Tile.ColliderType.Sprite
                                : UnityEngine.Tilemaps.Tile.ColliderType.Grid))));
            }

            levelBox.Add(Z.Box("Layers", "The level's drawing layers, back to front.",
                rows,
                Z.Field("Collision", "Which collider setup Build colliders applies. One-way platforms only apply in side-scroll.",
                    Z.MiniRadio((int)level.collision, new[] { "Top-down", "Side-scroll" },
                        "Collider setup for this level.",
                        i => LevelEdit("Set collision mode", () => level.collision = (CartographerLevel.CollisionMode)i))),
                Z.Field("Solid tag", "Optional. Set it and only clumps carrying that tag collide, so decorative " +
                    "clumps can share a solid layer without blocking. Leave empty and the whole layer is solid.",
                    LauAssetElement.Build(level.solidTag,
                        picked => { LevelEdit("Set solid tag", () => level.solidTag = picked as ClumpTag); RebuildLevelBox(); },
                        typeof(ClumpTag), _fieldThumbs, "Tag", "Assets/Cartographer/Tags",
                        "Tag that decides which clumps collide.")),
                Z.Button("+ Add layer", "Create a new tilemap layer on this level, in front of the others.",
                    AddLayer)));
        }

        void LevelEdit(string label, System.Action apply)
        {
            Undo.RecordObject(level, label);
            apply();
            EditorUtility.SetDirty(level);
        }

        void AddLayer()
        {
            var go = new GameObject("Layer " + (level.layers.Count + 1));
            Undo.RegisterCreatedObjectUndo(go, "Add level layer");
            Undo.SetTransformParent(go.transform, level.transform, "Add level layer");

            var map = Undo.AddComponent<Tilemap>(go);
            Undo.AddComponent<TilemapRenderer>(go).sortingOrder = level.layers.Count;

            Undo.RecordObject(level, "Add level layer");
            level.layers.Add(new LevelLayer { layerName = go.name, tilemap = map });
            EditorUtility.SetDirty(level);

            Undo.SetCurrentGroupName("Add level layer");
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            RebuildLevelBox();
        }

        void RemoveLayer(int index)
        {
            if (index < 0 || index >= level.layers.Count) return;
            var l = level.layers[index];

            Undo.RecordObject(level, "Remove level layer");
            level.layers.RemoveAt(index);
            EditorUtility.SetDirty(level);
            if (l?.tilemap != null) Undo.DestroyObjectImmediate(l.tilemap.gameObject);

            Undo.SetCurrentGroupName("Remove level layer");
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            RebuildLevelBox();
        }

        /// Build a level in the open scene: a Grid, a Tilemap per conventional layer, and the component wired
        /// to them. Undoable in one step, and selected afterwards so its layers can be configured immediately.
        void CreateLevelInScene()
        {
            var root = new GameObject("Cartographer Level");
            Undo.RegisterCreatedObjectUndo(root, "Create Cartographer level");

            var grid = Undo.AddComponent<Grid>(root);
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var lvl = Undo.AddComponent<CartographerLevel>(root);
            lvl.layers.Clear();

            // Terrain is solid by default (it is the ground); Structures are dressing until told otherwise.
            foreach (var (layerName, solid, order) in new[]
            {
                (CartographerLevel.TerrainLayer, true, 0),
                ("Structures", false, 1),
            })
            {
                var go = new GameObject(layerName);
                Undo.RegisterCreatedObjectUndo(go, "Create Cartographer level");
                Undo.SetTransformParent(go.transform, root.transform, "Create Cartographer level");

                var map = Undo.AddComponent<Tilemap>(go);
                var rend = Undo.AddComponent<TilemapRenderer>(go);
                rend.sortingOrder = order;

                lvl.layers.Add(new LevelLayer { layerName = layerName, tilemap = map, solid = solid });
            }

            Undo.SetCurrentGroupName("Create Cartographer level");
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());

            level = lvl;
            Selection.activeGameObject = root;
            EditorUtility.SetDirty(lvl);
            RebuildLevelBox();
        }

        // ── scene-view stamping ───────────────────────────────────────────────
        void OnSceneGUI(SceneView view)
        {
            if (!stamping || level == null || CurrentClump == null) return;

            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);

            // Take the default control so a click stamps instead of box-selecting the scene.
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

            hoverValid = TryCellUnderMouse(e.mousePosition, out hoverCell);
            if (hoverValid) DrawGhost();

            if (e.type == EventType.MouseMove) { view.Repaint(); return; }
            if (!hoverValid) return;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !e.control:
                    dragging = true;
                    dragErasing = e.alt;
                    haveLastFootprint = false;
                    lineAnchor = hoverCell;
                    if (!lineMode) ApplyStamp(hoverCell);
                    e.Use();
                    break;

                // Drag-repeat: the same gesture keeps laying clumps as the cursor moves. Without this a run of
                // flat ground costs one click per clump width, which was the tool's worst friction.
                // In line mode nothing is committed yet — the ghost shows the run and release commits it.
                case EventType.MouseDrag when dragging && e.button == 0:
                    if (!lineMode) ApplyStamp(hoverCell);
                    view.Repaint();
                    e.Use();
                    break;

                case EventType.MouseUp when dragging:
                    if (lineMode) CommitLine(lineAnchor, hoverCell);
                    dragging = false;
                    haveLastFootprint = false;
                    e.Use();
                    break;
            }
        }

        /// Stamp a straight run from `from` to `to`, spaced by the clump's own footprint so the run tiles
        /// edge-to-edge. Locked to the dominant axis: a run of ground or a wall is what this is for, and a
        /// free-angle line of square stamps is not a thing anyone wants.
        void CommitLine(Vector2Int from, Vector2Int to)
        {
            var origins = new List<Vector2Int>();
            LineOrigins(from, to, origins);

            RecordLevelForUndo(dragErasing ? "Erase line" : "Stamp line");
            foreach (var origin in origins)
            {
                if (dragErasing) EraseAt(origin);
                else level.PlaceClump(CurrentClump, origin, null, stampRotation, stampMirrorX);
            }
            AfterLevelChange();
        }

        /// Stamp (or erase) at `origin`, unless this drag already covered that ground.
        ///
        /// The rule is overlap rejection, not "one per cell": dragging a 8x1 strip clump would otherwise lay a
        /// stamp every single cell and pile eight overlapping copies where one belongs. Skipping any candidate
        /// whose footprint touches the previous one makes a drag tile clumps edge-to-edge in whatever direction
        /// you move, and still stamps every cell for a 1x1 clump.
        void ApplyStamp(Vector2Int origin)
        {
            if (dragErasing) { EraseAt(origin); return; }

            var foot = FootprintAt(origin);
            if (haveLastFootprint && Overlaps(foot, lastFootprint)) return;

            StampAt(origin);
            lastFootprint = foot;
            haveLastFootprint = true;
        }

        /// The cells the current clump would occupy if stamped at `origin`, with rotation/mirror applied.
        RectInt FootprintAt(Vector2Int origin)
        {
            var c = CurrentClump;
            if (c?.cells == null || c.cells.Count == 0) return new RectInt(origin.x, origin.y, 1, 1);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var cell in c.cells)
            {
                if (cell == null) continue;
                var o = CartographerLevel.TransformOffset(cell.offset, stampRotation, stampMirrorX);
                minX = Mathf.Min(minX, o.x); maxX = Mathf.Max(maxX, o.x);
                minY = Mathf.Min(minY, o.y); maxY = Mathf.Max(maxY, o.y);
            }
            if (minX > maxX) return new RectInt(origin.x, origin.y, 1, 1);

            return new RectInt(origin.x + minX, origin.y + minY, maxX - minX + 1, maxY - minY + 1);
        }

        static bool Overlaps(RectInt a, RectInt b) =>
            a.xMin < b.xMax && b.xMin < a.xMax && a.yMin < b.yMax && b.yMin < a.yMax;

        /// Ray-cast the mouse onto the level's own plane and convert to a grid cell.
        bool TryCellUnderMouse(Vector2 mousePosition, out Vector2Int cell)
        {
            cell = default;
            var g = level.Grid;
            if (g == null) return false;

            var t = level.transform;
            var plane = new Plane(t.forward, t.position);
            var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (!plane.Raycast(ray, out float dist)) return false;

            var world = ray.GetPoint(dist);
            var c = g.WorldToCell(world);
            cell = new Vector2Int(c.x, c.y);
            return true;
        }

        /// Outline every cell the gesture would write — one stamp normally, the whole run while a line is being
        /// dragged out. What is outlined is exactly what lands.
        void DrawGhost()
        {
            var g = level.Grid;
            var c = CurrentClump;
            if (g == null || c?.cells == null) return;

            var origins = new List<Vector2Int>();
            if (lineMode && dragging) LineOrigins(lineAnchor, hoverCell, origins);
            else origins.Add(hoverCell);

            var fill = dragErasing && dragging ? new Color(1f, 0.3f, 0.25f, 0.18f) : new Color(1f, 0.75f, 0.2f, 0.18f);
            var edge = dragErasing && dragging ? new Color(1f, 0.3f, 0.25f, 0.9f) : new Color(1f, 0.75f, 0.2f, 0.9f);

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            foreach (var origin in origins)
            {
                foreach (var cellDef in c.cells)
                {
                    if (cellDef == null) continue;
                    var o = CartographerLevel.TransformOffset(cellDef.offset, stampRotation, stampMirrorX);
                    var cellPos = new Vector3Int(origin.x + o.x, origin.y + o.y, 0);

                    var bl = g.CellToWorld(cellPos);
                    var size = g.cellSize;
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

        /// The origins a line from `from` to `to` would stamp at — shared by the ghost and the commit, so the
        /// preview cannot drift from what actually lands.
        void LineOrigins(Vector2Int from, Vector2Int to, List<Vector2Int> into)
        {
            var foot = FootprintAt(from);
            int dx = to.x - from.x, dy = to.y - from.y;
            bool horizontal = Mathf.Abs(dx) >= Mathf.Abs(dy);

            int step = horizontal ? Mathf.Max(1, foot.width) : Mathf.Max(1, foot.height);
            int span = horizontal ? Mathf.Abs(dx) : Mathf.Abs(dy);
            int sign = horizontal ? (dx < 0 ? -1 : 1) : (dy < 0 ? -1 : 1);

            for (int i = 0; i <= span / step; i++)
                into.Add(horizontal
                    ? new Vector2Int(from.x + i * step * sign, from.y)
                    : new Vector2Int(from.x, from.y + i * step * sign));
        }

        void StampAt(Vector2Int origin)
        {
            RecordLevelForUndo("Stamp clump");
            level.PlaceClump(CurrentClump, origin, null, stampRotation, stampMirrorX);
            AfterLevelChange();
        }

        /// Remove the most recent placement whose footprint covers `cell`, then rebuild. Rebuilding rather than
        /// clearing that clump's cells directly is the honest way to do it: two overlapping stamps share cells,
        /// so erasing one by its own footprint would punch holes in the other.
        void EraseAt(Vector2Int cell)
        {
            int found = -1;
            for (int i = level.placements.Count - 1; i >= 0 && found < 0; i--)
            {
                var p = level.placements[i];
                if (p?.clump?.cells == null) continue;
                foreach (var cd in p.clump.cells)
                {
                    if (cd == null) continue;
                    var o = CartographerLevel.TransformOffset(cd.offset, p.rotation, p.mirrorX);
                    if (p.origin + o == cell) { found = i; break; }
                }
            }
            if (found < 0) return;

            RecordLevelForUndo("Erase clump");
            level.placements.RemoveAt(found);
            level.RebuildFromPlacements();
            AfterLevelChange();
        }

        void RecordLevelForUndo(string label)
        {
            Undo.RegisterCompleteObjectUndo(level, label);
            foreach (var l in level.layers)
                if (l?.tilemap != null) Undo.RegisterCompleteObjectUndo(l.tilemap, label);
            Undo.SetCurrentGroupName(label);
        }

        void AfterLevelChange()
        {
            EditorUtility.SetDirty(level);
            foreach (var l in level.layers)
                if (l?.tilemap != null) EditorUtility.SetDirty(l.tilemap);
            RebuildLevelBox();
            SceneView.RepaintAll();
        }
    }
}
