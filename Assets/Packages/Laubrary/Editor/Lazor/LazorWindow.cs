// LazorWindow.cs — the Lazor authoring window (plain EditorWindow; ZUI is not present in this Laubrary copy,
// matching the RulesEditorWindow convention). Left: tools, layer stack, and the selected layer's style +
// mirror settings. Right: a zoomable/pannable grid canvas you draw vector strokes on, with live per-layer
// mirror symmetry. Shapes are LazorShape assets, created/duplicated/renamed/deleted and browsed here.
//
// This file holds window state, lifecycle, the top bar, and asset CRUD. Canvas drawing/input is in
// LazorWindow.Canvas.cs, the layer panel in LazorWindow.Layers.cs, and the browser in LazorWindow.Browser.cs.

using System.IO;
using UnityEditor;
using UnityEngine;
using Laubrary.Lazor;

namespace Laubrary.Lazor.Editor
{
    public partial class LazorWindow : EditorWindow
    {
        [MenuItem("Laubrary/Lazor/Lazor")]
        public static void Open() => GetWindow<LazorWindow>("Lazor");

        enum Tool { Pen, Edit, Erase }

        [SerializeField] LazorShape shape;
        [SerializeField] float leftWidth = 288f;
        [SerializeField] Tool tool = Tool.Pen;
        [SerializeField] bool snap = true;
        [SerializeField] bool showGrid = true;

        // Canvas view (persisted so it survives domain reloads).
        [SerializeField] float zoom = 24f;          // pixels per grid unit
        [SerializeField] Vector2 pan = Vector2.zero; // canvas-local offset
        [SerializeField] bool zoomInitialized = false;

        // Transient interaction state.
        int layerSel = 0;
        int activePath = -1;         // stroke currently being drawn with the Pen tool (-1 = none)
        int dragVertex = -1;         // vertex being dragged with the Edit tool
        int dragVertexPath = -1;
        bool draggingPan = false;
        bool draggingSplit = false;
        Vector2 leftScroll;

        // Inline asset prompts (create / rename), matching Pyre's inline-prompt-over-modal convention.
        bool creating, renaming;
        string nameBuffer = "";

        const string DefaultFolder = "Assets/Lazor";

        void OnEnable()
        {
            wantsMouseMove = true;
            EnsureWhiteTex();
            EditorApplication.projectChanged += OnProjectChanged;
        }

        void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            ClearBrowseThumbs();
            if (_white != null) { DestroyImmediate(_white); _white = null; }
        }

        void OnGUI()
        {
            DrawTopBar();

            if (browsing)
            {
                DrawBrowser();
                return;
            }

            if (shape == null)
            {
                EditorGUILayout.HelpBox("Pick or create a Lazor Shape to start drawing.", MessageType.Info);
                return;
            }

            layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, shape.layers.Count - 1));

            float top = EditorGUIUtility.singleLineHeight + 8 + ((creating || renaming) ? 26f : 0f);
            Rect body = new Rect(0, top, position.width, position.height - top);
            Rect leftRect = new Rect(body.x, body.y, leftWidth, body.height);
            Rect splitRect = new Rect(body.x + leftWidth, body.y, 5f, body.height);
            Rect canvasRect = new Rect(body.x + leftWidth + 5f, body.y, body.width - leftWidth - 5f, body.height);

            DrawLeftPanel(leftRect);
            DrawVerticalSplitter(splitRect);
            DrawCanvas(canvasRect);

            if (GUI.changed) Repaint();
        }

        // ---- Top bar ----

        void DrawTopBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                var picked = (LazorShape)EditorGUILayout.ObjectField(shape, typeof(LazorShape), false, GUILayout.Width(200));
                if (EditorGUI.EndChangeCheck()) { shape = picked; layerSel = 0; activePath = -1; }

                if (GUILayout.Button(new GUIContent("New", "Create a new Lazor Shape asset."), EditorStyles.toolbarButton, GUILayout.Width(40)))
                { creating = true; renaming = false; nameBuffer = "LazorShape"; }

                using (new EditorGUI.DisabledScope(shape == null))
                {
                    if (GUILayout.Button(new GUIContent("Dup", "Duplicate this shape into a new asset."), EditorStyles.toolbarButton, GUILayout.Width(40)))
                        DuplicateAsset();
                    if (GUILayout.Button(new GUIContent("Rename", "Rename this shape's asset file."), EditorStyles.toolbarButton, GUILayout.Width(56)))
                    { renaming = true; creating = false; nameBuffer = shape.name; }
                    if (GUILayout.Button(new GUIContent("Delete", "Delete this shape's asset file."), EditorStyles.toolbarButton, GUILayout.Width(56)))
                        DeleteAsset();
                }

                GUILayout.FlexibleSpace();
                browsing = GUILayout.Toggle(browsing, new GUIContent("Browse", "Browse all Lazor Shapes in the project."), EditorStyles.toolbarButton, GUILayout.Width(60));
            }

            if (creating) DrawNamePrompt("Create shape:", name => CreateAssetNamed(name));
            if (renaming && shape != null) DrawNamePrompt("Rename to:", name => RenameAsset(name));
        }

        void DrawNamePrompt(string label, System.Action<string> commit)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(label, GUILayout.Width(90));
                GUI.SetNextControlName("LazorNamePrompt");
                nameBuffer = EditorGUILayout.TextField(nameBuffer);
                bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                if (GUILayout.Button("OK", GUILayout.Width(40)) || enter)
                {
                    if (!string.IsNullOrWhiteSpace(nameBuffer)) commit(nameBuffer.Trim());
                    creating = renaming = false;
                    GUI.FocusControl(null);
                    if (enter) Event.current.Use();
                }
                if (GUILayout.Button("Cancel", GUILayout.Width(60))) { creating = renaming = false; GUI.FocusControl(null); }
            }
        }

        // ---- Asset CRUD ----

        void CreateAssetNamed(string niceName)
        {
            var s = CreateInstance<LazorShape>();
            s.AddExampleContent();
            string dir = shape != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(shape))
                ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(shape))
                : DefaultFolder;
            EnsureFolder(dir);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{niceName}.asset");
            AssetDatabase.CreateAsset(s, path);
            AssetDatabase.SaveAssets();
            shape = s; layerSel = 0; activePath = -1;
        }

        void DuplicateAsset()
        {
            string path = AssetDatabase.GetAssetPath(shape);
            if (string.IsNullOrEmpty(path)) { CreateAssetNamed(shape.name + " Copy"); return; }
            string copy = AssetDatabase.GenerateUniqueAssetPath(path);
            if (AssetDatabase.CopyAsset(path, copy))
            {
                AssetDatabase.SaveAssets();
                shape = AssetDatabase.LoadAssetAtPath<LazorShape>(copy);
                layerSel = 0; activePath = -1;
            }
        }

        void RenameAsset(string newName)
        {
            string path = AssetDatabase.GetAssetPath(shape);
            if (string.IsNullOrEmpty(path)) { shape.name = newName; EditorUtility.SetDirty(shape); return; }
            string err = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(err)) Debug.LogWarning($"Lazor rename failed: {err}");
            AssetDatabase.SaveAssets();
        }

        void DeleteAsset()
        {
            string path = AssetDatabase.GetAssetPath(shape);
            if (string.IsNullOrEmpty(path)) { shape = null; return; }
            if (EditorUtility.DisplayDialog("Delete Lazor Shape", $"Delete '{shape.name}'? This cannot be undone.", "Delete", "Cancel"))
            {
                AssetDatabase.DeleteAsset(path);
                shape = null; activePath = -1;
            }
        }

        static void EnsureFolder(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = Path.GetDirectoryName(dir);
            string leaf = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, leaf);
        }

        void DrawVerticalSplitter(Rect r)
        {
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.35f));
            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { draggingSplit = true; e.Use(); }
            if (draggingSplit)
            {
                leftWidth = Mathf.Clamp(e.mousePosition.x, 220f, position.width - 200f);
                Repaint();
                if (e.type == EventType.MouseUp) { draggingSplit = false; e.Use(); }
            }
        }

        void RecordShape(string label)
        {
            if (shape != null) Undo.RecordObject(shape, label);
        }

        void MarkDirty()
        {
            if (shape != null) EditorUtility.SetDirty(shape);
        }
    }
}
