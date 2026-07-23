using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Launimator;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Editor for a <see cref="SpriteCatalog"/>: pick a source sheet, auto-grid (or hand-place) cells, name every
    /// sprite, and slice — reusing the same registration pipeline (<see cref="RegionSlicer"/>) as animations, so the
    /// resulting named sprites are usable directly by the game and pickable from the Animation Builder. The stage
    /// shows the sheet with every cell outlined and labelled. Asset browse + CRUD come from ZuiAssetWindow.
    ///
    /// UI TOOLKIT PORT: every control is a Z.* control; each entry row's thumbnail is a native <c>Image</c> (it was
    /// only a texture blit). The STAGE stays an IMGUIContainer — it is bespoke canvas painting (sheet blit + a
    /// per-cell outline/label overlay drawn with Handles), which no retained element expresses.
    /// Undo: every edit now records Undo on the catalog (the IMGUI original recorded none).
    /// </summary>
    public class SpriteCatalogWindow : ZuiAssetWindow<SpriteCatalog>
    {
        [MenuItem("Laubrary/Sprite Catalog")]
        public static void Open() => GetWindow<SpriteCatalogWindow>("Sprite Catalog");

        SpriteCatalog catalog => Current;   // the base owns the current asset; alias for the editor body
        int gridCols = 8, gridRows = 8;

        VisualElement controlsHost;         // rebuilt when the entry list changes
        IMGUIContainer stage;

        protected override string TypeLabel => "Sprite Catalog";
        protected override string NewAssetName => "SpriteCatalog";
        protected override string DefaultFolder => "Assets/Launimator";
        // No RenderThumbnail override: the browser falls back to Unity's asset icon. (Do NOT return catalog.sheet —
        // it's a Unity-owned asset and the base destroys the textures it's handed.)

        protected override void OnBeforeRebuild() { base.OnBeforeRebuild(); controlsHost = null; stage = null; }

        // ── mutation helper (the Undo contract every edit routes through) ────────────────────
        void Edit(string undoLabel, System.Action apply)
        {
            Undo.RecordObject(catalog, undoLabel);
            apply();
            EditorUtility.SetDirty(catalog);
            stage?.MarkDirtyRepaint();
        }

        /// Structural change (an entry added/removed) → rebuild the control column too.
        void EditAndRebuild(string undoLabel, System.Action apply)
        {
            Edit(undoLabel, apply);
            if (controlsHost == null) return;
            controlsHost.Clear();
            BuildControls(controlsHost);
        }

        protected override void BuildAsset(VisualElement root, SpriteCatalog asset)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            root.Add(Z.Field("Sheet", "The source sheet these sprites are sliced from.",
                Z.Object<Texture2D>(asset.sheet, "The source sheet these sprites are sliced from.",
                    v => { Edit("Catalog sheet", () => asset.sheet = v); Rebuild(); }, 220f)));
            root.Add(Z.Field("Pixels per unit", "Pixels-per-unit stamped onto every sprite this catalog slices.",
                Z.Float(asset.pixelsPerUnit, "Pixels-per-unit stamped onto every sprite this catalog slices.",
                    v => Edit("Catalog PPU", () => asset.pixelsPerUnit = Mathf.Max(1f, v)), 70f)));

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;
            root.Add(split);

            var left = new VisualElement();
            left.style.width = 320f;
            left.style.flexShrink = 0f;
            left.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            controlsHost = scroll.contentContainer;
            BuildControls(controlsHost);
            left.Add(scroll);
            split.Add(left);

            stage = new IMGUIContainer(DrawStageGUI)
            {
                tooltip = "The sheet with every catalog cell outlined and named — a read-only overview of what Slice will produce."
            };
            stage.style.flexGrow = 1f;
            stage.style.minWidth = 0f;
            stage.style.marginLeft = 4f;
            split.Add(stage);
        }

        // ── left: controls ───────────────────────────────────────────────────────────────────
        void BuildControls(VisualElement root)
        {
            root.Add(Z.Text("Auto-grid", ZuiText.Section,
                "Split the whole sheet into an even grid of cells with default names."));
            root.Add(Z.Row(
                Z.Field("Cols", "How many columns the generated grid has.",
                    Z.Int(gridCols, "How many columns the generated grid has.",
                        v => gridCols = Mathf.Max(1, v), 46f)),
                Z.Field("Rows", "How many rows the generated grid has.",
                    Z.Int(gridRows, "How many rows the generated grid has.",
                        v => gridRows = Mathf.Max(1, v), 46f))));

            var gen = Z.Button("Generate grid cells",
                "Replace the entry list with Cols×Rows evenly-spaced cells covering the whole sheet (undoable).",
                () => EditAndRebuild("Generate grid cells", GenerateGrid));
            gen.SetEnabled(catalog.sheet != null);
            root.Add(gen);
            root.Add(Z.Text("Splits the whole sheet into Cols×Rows cells with default names. Rename below, then Slice.",
                ZuiText.Subtle, "What 'Generate grid cells' does."));

            root.Add(Z.VSpace());
            root.Add(Z.Text($"Sprites ({catalog.entries.Count})", ZuiText.Section,
                "Every named cell this catalog will slice out of the sheet."));

            for (int i = 0; i < catalog.entries.Count; i++)
                root.Add(BuildEntryRow(i));

            root.Add(Z.VSpace());
            var slice = Z.Button("Slice & apply",
                "Re-slice the source sheet into these named sprites and rebind every entry to its sliced sprite.",
                Slice);
            slice.SetEnabled(catalog.sheet != null && catalog.entries.Count > 0);
            root.Add(slice);
        }

        VisualElement BuildEntryRow(int index)
        {
            var e = catalog.entries[index];
            var row = Z.Row();

            // Native Image instead of an IMGUI blit — this "preview" was only ever GUI.DrawTexture.
            var thumbBox = new VisualElement { tooltip = $"Preview of '{e.name}' once sliced." };
            thumbBox.style.width = 24f;
            thumbBox.style.height = 24f;
            thumbBox.style.flexShrink = 0f;
            thumbBox.style.backgroundColor = new Color(0f, 0f, 0f, 0.2f);
            var tex = e.sprite != null ? AssetPreview.GetAssetPreview(e.sprite) : null;
            if (tex != null)
            {
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                img.style.flexGrow = 1f;
                thumbBox.Add(img);
            }
            row.Add(thumbBox);

            row.Add(Z.TextInput(e.name, "This sprite's name — how the game looks it up.",
                v => Edit("Rename catalog sprite", () => e.name = v), 200f));

            row.Add(Z.Button("×", "Remove this sprite from the catalog (undoable).",
                () => EditAndRebuild("Remove catalog sprite", () => catalog.entries.RemoveAt(index))).W(22f));
            return row;
        }

        // ── right: stage (the ONE IMGUI island — bespoke sheet + cell-outline painting) ───────
        void DrawStageGUI()
        {
            if (stage == null) return;
            Rect view = new Rect(0f, 0f, stage.layout.width, stage.layout.height);
            if (!(view.width > 10f) || !(view.height > 10f)) return;

            EditorGUI.DrawRect(view, new Color(0.12f, 0.12f, 0.14f));
            var sheet = catalog != null ? catalog.sheet : null;
            if (sheet == null) { EditorGUI.DropShadowLabel(view, "Assign a sheet"); return; }

            float scale = Mathf.Min((view.width - 12) / sheet.width, (view.height - 12) / sheet.height);
            var texRect = new Rect(view.x + 6, view.y + 6, sheet.width * scale, sheet.height * scale);
            GUI.DrawTexture(texRect, sheet, ScaleMode.ScaleToFit, true);

            // cell → screen: texture px are bottom-left origin; screen y is top-down.
            Handles.BeginGUI();
            foreach (var e in catalog.entries)
            {
                var c = e.cell;
                float sx = texRect.x + c.x * scale;
                float sy = texRect.y + (sheet.height - c.y - c.height) * scale;
                var r = new Rect(sx, sy, c.width * scale, c.height * scale);
                Handles.color = new Color(0.3f, 0.9f, 1f, 0.9f);
                Handles.DrawAAPolyLine(1.5f, new Vector3(r.x, r.y), new Vector3(r.xMax, r.y), new Vector3(r.xMax, r.yMax), new Vector3(r.x, r.yMax), new Vector3(r.x, r.y));
                if (r.width > 26) GUI.Label(new Rect(r.x + 1, r.y, r.width, 14), e.name, EditorStyles.miniLabel);
            }
            Handles.EndGUI();
        }

        void GenerateGrid()
        {
            var sheet = catalog.sheet;
            var spec = new RegionSlicer.RegionSpec
            {
                boxX = 0, boxY = 0, boxW = sheet.width, boxH = sheet.height,
                mode = RegionSlicer.GridMode.FixedColsRows, cols = gridCols, rows = gridRows,
            };
            var rects = RegionSlicer.ExpandRegion(spec);
            catalog.entries.Clear();
            for (int i = 0; i < rects.Count; i++)
                catalog.entries.Add(new CatalogEntry { name = $"sprite_{i:000}", cell = rects[i], pivot = new Vector2(0.5f, 0.5f) });
        }

        void Slice()
        {
            string path = AssetDatabase.GetAssetPath(catalog.sheet);
            if (string.IsNullOrEmpty(path)) { Debug.LogWarning("[SpriteCatalog] Sheet has no asset path."); return; }

            var rects = new List<Rect>(catalog.entries.Count);
            var pivots = new List<Vector2>(catalog.entries.Count);
            foreach (var e in catalog.entries) { rects.Add(e.cell); pivots.Add(e.pivot); }

            RegionSlicer.Apply(path, rects, pivots, catalog.pixelsPerUnit, i => catalog.entries[i].name, new Vector2(0.5f, 0.5f));

            var byName = new Dictionary<string, Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Sprite sp) byName[sp.name] = sp;

            Undo.RecordObject(catalog, "Slice catalog");
            foreach (var e in catalog.entries)
                e.sprite = byName.TryGetValue(e.name, out var s) ? s : null;

            catalog.InvalidateLookup();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpriteCatalog] Sliced {catalog.entries.Count} sprites from {path}.");
            Rebuild();
        }
    }
}
