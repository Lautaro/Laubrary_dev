using System.Collections.Generic;
using Laubrary.Launimator;
using Laubrary.AssetKit.Editor;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Editor for a <see cref="SpriteCatalog"/>: pick a source sheet, auto-grid (or hand-place) cells, name every
    /// sprite, and slice — reusing the same registration pipeline (<see cref="RegionSlicer"/>) as animations, so the
    /// resulting named sprites are usable directly by the game and pickable from the Animation Builder. The stage
    /// shows the sheet with every cell outlined and labelled. Asset browse + CRUD come from LaubraryAssetWindow.
    /// </summary>
    public class SpriteCatalogWindow : LaubraryAssetWindow<SpriteCatalog>
    {
        [MenuItem("Laubrary/Sprite Catalog")]
        public static void Open() => GetWindow<SpriteCatalogWindow>("Sprite Catalog");

        SpriteCatalog catalog => Current;   // the base owns the current asset; alias for the editor body
        Vector2 listScroll;
        int gridCols = 8, gridRows = 8;

        protected override string TypeLabel => "Sprite Catalog";
        protected override string NewAssetName => "SpriteCatalog";
        protected override string DefaultFolder => "Assets/Launimator";
        // No RenderThumbnail override: the browser falls back to Unity's asset icon. (Do NOT return catalog.sheet —
        // it's a Unity-owned asset and the base destroys the textures it's handed.)

        protected override void DrawAsset(SpriteCatalog asset)
        {
            EditorGUI.BeginChangeCheck();
            asset.sheet = ObjectField("Sheet", asset.sheet);
            asset.pixelsPerUnit = ZUI.FloatField("Pixels per unit", asset.pixelsPerUnit, 70f, 1f);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(asset);

            EditorGUILayout.BeginHorizontal();
            DrawControls(GUILayout.Width(320));
            DrawStage();
            EditorGUILayout.EndHorizontal();
        }

        void DrawControls(params GUILayoutOption[] opt)
        {
            EditorGUILayout.BeginVertical(opt);

            Label("Auto-grid", ZUI.ZTextStyle.SectionHeader);
            using (ZUI.HRow())
            {
                gridCols = Mathf.Max(1, ZUI.IntField("Cols", gridCols, 46f, 1));
                gridRows = Mathf.Max(1, ZUI.IntField("Rows", gridRows, 46f, 1));
            }
            using (new EditorGUI.DisabledScope(catalog.sheet == null))
                if (Button("Generate grid cells")) GenerateGrid();
            Label("Splits the whole sheet into Cols×Rows cells with default names. Rename below, then Slice.", ZUI.ZTextStyle.Subtle);

            VerticalSpace();
            Label($"Sprites ({catalog.entries.Count})", ZUI.ZTextStyle.SectionHeader);

            int remove = -1;
            using (ScrollView(ref listScroll))
            {
                for (int i = 0; i < catalog.entries.Count; i++)
                {
                    var e = catalog.entries[i];
                    using (var row = ZUI.HRow())
                    {
                        var tex = e.sprite != null ? AssetPreview.GetAssetPreview(e.sprite) : null;
                        var box = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
                        if (tex != null) GUI.DrawTexture(box, tex, ScaleMode.ScaleToFit);
                        else EditorGUI.DrawRect(box, new Color(0, 0, 0, 0.2f));
                        EditorGUI.BeginChangeCheck();
                        e.name = TextField(e.name, 200f);
                        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(catalog);
                        if (row.Button("×", ZUI.Style.Default, GUILayout.Width(22))) remove = i;
                    }
                }
            }
            if (remove >= 0) { catalog.entries.RemoveAt(remove); EditorUtility.SetDirty(catalog); }

            VerticalSpace();
            using (new EditorGUI.DisabledScope(catalog.sheet == null || catalog.entries.Count == 0))
                if (Button("Slice & apply")) Slice();

            EditorGUILayout.EndVertical();
        }

        void DrawStage()
        {
            Rect view = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(view, new Color(0.12f, 0.12f, 0.14f));
            var sheet = catalog.sheet;
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
            EditorUtility.SetDirty(catalog);
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
            foreach (var e in catalog.entries)
                e.sprite = byName.TryGetValue(e.name, out var s) ? s : null;

            catalog.InvalidateLookup();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpriteCatalog] Sliced {catalog.entries.Count} sprites from {path}.");
        }
    }
}
