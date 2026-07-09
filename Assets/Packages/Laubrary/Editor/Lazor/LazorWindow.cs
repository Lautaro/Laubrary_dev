// LazorWindow.cs — the Lazor authoring window. It builds on LaubraryAssetWindow<LazorShape>, the shared ZUI
// AssetKit base every single-asset Laubrary editor uses, so the toolbar (assign / New / Duplicate / Rename /
// Delete / Browse), the auto-refreshing thumbnail browser, and "show the browser when nothing's selected" all
// come for free and look like Pyre / Larder / the rest. This window adds the per-asset body: left a ZUI panel
// (tools, layer stack, the selected layer's style + mirror), right a zoomable/pannable grid canvas you draw
// vector strokes on with live per-layer mirror symmetry.
//
// This file holds window state, lifecycle, layout, and the tiny asset hooks. Canvas drawing/input is in
// LazorWindow.Canvas.cs and the left panel in LazorWindow.Layers.cs. (The browser lives in the base now — the
// old hand-rolled LazorWindow.Browser.cs was removed.)

using UnityEditor;
using UnityEngine;
using Laubrary.Lazor;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Lazor.Editor
{
    public partial class LazorWindow : LaubraryAssetWindow<LazorShape>
    {
        [MenuItem("Laubrary/Lazor/Lazor")]
        public static void Open() => GetWindow<LazorWindow>("Lazor");

        enum Tool { Pen, Edit, Erase }

        // The base owns the current asset; `shape` is an alias so the canvas / layer code reads naturally.
        LazorShape shape => Current;

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

        // ── AssetKit hooks ─────────────────────────────────────────────────────
        protected override string TypeLabel => "Lazor Shape";
        protected override string NewAssetName => "New Lazor Shape";
        protected override string DefaultFolder => "Assets/Lazor";
        protected override void InitializeNewAsset(LazorShape item) => item.AddExampleContent();
        protected override void OnAssetChanged() { layerSel = 0; activePath = -1; zoomInitialized = false; }
        protected override Texture2D RenderThumbnail(LazorShape item)
            => LazorRasterizer.Render(item, 96, new Color(0.05f, 0.06f, 0.08f, 1f));

        protected override void OnZUIEnable()
        {
            EnsureWhiteTex();   // wantsMouseMove is already set by ZUIWindow
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        protected override void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            base.OnDisable();   // AssetKit unhooks projectChanged + clears browser thumbnails
            if (_white != null) { DestroyImmediate(_white); _white = null; }
            if (_disc != null) { DestroyImmediate(_disc); _disc = null; }
        }

        // After an undo/redo the shape's serialized data is restored under us — clamp the transient selection to the
        // restored layer count and repaint so the canvas actually reflects it (else undo looks like it did nothing).
        void OnUndoRedo()
        {
            if (shape != null)
                layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, shape.layers.Count - 1));
            activePath = -1;
            Repaint();
        }

        // ── per-asset body: left ZUI panel | drag-splitter | grid canvas ──────────
        protected override void DrawAsset(LazorShape asset)
        {
            layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, asset.layers.Count - 1));

            // Claim the whole area below the toolbar for the split view. Reserve a real full-width strip (not a 1px
            // sliver) via the GUILayout flow: its yMax sits cleanly below the toolbar so the left panel can't ride up
            // over the toolbar buttons, and its x/width give the true content bounds. The Y is stable across
            // Layout/Repaint, so the manually-carved canvas rect is valid on every event pass.
            Rect strip = GUILayoutUtility.GetRect(10f, 6f, GUILayout.ExpandWidth(true));
            float pad = strip.x;
            Rect body = new Rect(strip.x, strip.yMax, strip.width, position.height - strip.yMax - pad);

            Rect leftRect = new Rect(body.x, body.y, leftWidth, body.height);
            Rect splitRect = new Rect(body.x + leftWidth, body.y, 5f, body.height);
            Rect canvasRect = new Rect(body.x + leftWidth + 5f, body.y, body.width - leftWidth - 5f, body.height);

            DrawLeftPanel(leftRect);
            DrawVerticalSplitter(splitRect);
            DrawCanvas(canvasRect);

            if (GUI.changed) Repaint();
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
