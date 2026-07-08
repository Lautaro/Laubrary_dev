using System.Collections.Generic;
using Laubrary.Zoetrope;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// Editor for a <see cref="SpriteCatalog"/>: pick a source sheet, auto-grid (or hand-place) cells, name every
    /// sprite, and slice — reusing the same registration pipeline (<see cref="RegionSlicer"/>) as animations, so the
    /// resulting named sprites are usable directly by the game and pickable from the Animation Builder. The stage
    /// shows the sheet with every cell outlined and labelled.
    /// </summary>
    public class SpriteCatalogWindow : EditorWindow
    {
        [MenuItem("Laubrary/Sprite Catalog")]
        public static void Open() => GetWindow<SpriteCatalogWindow>("Sprite Catalog");

        [SerializeField] SpriteCatalog catalog;
        Vector2 listScroll;
        int gridCols = 8, gridRows = 8;

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                catalog = (SpriteCatalog)EditorGUILayout.ObjectField(catalog, typeof(SpriteCatalog), false, GUILayout.Width(240));
                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(50))) CreateCatalog();
                GUILayout.FlexibleSpace();
                if (catalog != null) GUILayout.Label($"{catalog.entries.Count} sprites", EditorStyles.miniLabel);
            }

            if (catalog == null)
            {
                EditorGUILayout.HelpBox("Pick or create a Sprite Catalog. It slices a sheet into NAMED sprites — use them " +
                    "directly (catalog.Get(\"name\")) or pick them from the Animation Builder. Not just for animations.", MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();
            catalog.sheet = (Texture2D)EditorGUILayout.ObjectField("Sheet", catalog.sheet, typeof(Texture2D), false);
            catalog.pixelsPerUnit = EditorGUILayout.FloatField("Pixels per unit", catalog.pixelsPerUnit);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(catalog);

            EditorGUILayout.BeginHorizontal();
            DrawControls(GUILayout.Width(320));
            DrawStage();
            EditorGUILayout.EndHorizontal();
        }

        void DrawControls(params GUILayoutOption[] opt)
        {
            EditorGUILayout.BeginVertical(opt);

            EditorGUILayout.LabelField("Auto-grid", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                gridCols = Mathf.Max(1, EditorGUILayout.IntField("Cols", gridCols));
                gridRows = Mathf.Max(1, EditorGUILayout.IntField("Rows", gridRows));
            }
            using (new EditorGUI.DisabledScope(catalog.sheet == null))
                if (GUILayout.Button("Generate grid cells")) GenerateGrid();
            EditorGUILayout.HelpBox("Splits the whole sheet into Cols×Rows cells with default names. Rename below, then Slice.", MessageType.None);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Sprites ({catalog.entries.Count})", EditorStyles.boldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            int remove = -1;
            for (int i = 0; i < catalog.entries.Count; i++)
            {
                var e = catalog.entries[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var tex = e.sprite != null ? AssetPreview.GetAssetPreview(e.sprite) : null;
                    var box = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
                    if (tex != null) GUI.DrawTexture(box, tex, ScaleMode.ScaleToFit);
                    else EditorGUI.DrawRect(box, new Color(0, 0, 0, 0.2f));
                    EditorGUI.BeginChangeCheck();
                    e.name = EditorGUILayout.TextField(e.name);
                    if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(catalog);
                    if (GUILayout.Button("×", GUILayout.Width(22))) remove = i;
                }
            }
            EditorGUILayout.EndScrollView();
            if (remove >= 0) { catalog.entries.RemoveAt(remove); EditorUtility.SetDirty(catalog); }

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(catalog.sheet == null || catalog.entries.Count == 0))
                if (GUILayout.Button("Slice & apply", GUILayout.Height(26))) Slice();

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

        void CreateCatalog()
        {
            string path = EditorUtility.SaveFilePanelInProject("New Sprite Catalog", "SpriteCatalog", "asset", "");
            if (string.IsNullOrEmpty(path)) return;
            var c = CreateInstance<SpriteCatalog>();
            AssetDatabase.CreateAsset(c, path);
            AssetDatabase.SaveAssets();
            catalog = c;
        }
    }
}
