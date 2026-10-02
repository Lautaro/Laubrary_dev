// LazorWindow.cs — the Lazor authoring window. It builds on ZuiAssetWindow<LazorShape>, the shared UI Toolkit
// AssetKit base every other Laubrary single-asset editor uses, so the toolbar (assign / New / Duplicate /
// Rename / Delete / Browse), the Tags section, the auto-refreshing thumbnail browser, and "show the browser
// when nothing's selected" all come for free and look like Pyre / DotGen / the rest. This window adds the
// per-asset body: left a real UI Toolkit panel (tools, layer stack, the selected layer's style + mirror),
// right a zoomable/pannable grid canvas you draw vector strokes on with live per-layer mirror symmetry.
//
// Retained mode, not a per-frame draw loop: BuildAsset runs once per rebuild and the controls hold their own
// state afterwards. The ONE exception is the drawing canvas — bespoke 2D painting with its own event handling,
// which stays raw IMGUI inside an IMGUIContainer. Everything around it (splitter, zoom readout, Fit) is ZUI.
//
// This file holds window state, lifecycle, layout, and the tiny asset hooks. Canvas drawing/input is in
// LazorWindow.Canvas.cs and the left panel in LazorWindow.Layers.cs. (The browser lives in the base — the
// old hand-rolled LazorWindow.Browser.cs was removed.)

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Lazor;
using Laubrary.Zui;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Lazor.Editor
{
    public partial class LazorWindow : ZuiAssetWindow<LazorShape>
    {
        [MenuItem("Laubrary/Lazor/Lazor")]
        public static void Open() => GetWindow<LazorWindow>("Lazor");

        /// Same entry-point shape as PyreWindow.OpenFor/MirageWindow.OpenFor — lets a reference chip's "Open in its editor" card item
        /// button jump straight into this LazorShape's own editor.
        public static void OpenFor(LazorShape s)
        {
            var w = GetWindow<LazorWindow>("Lazor");
            if (s != null) w.SetAsset(s);
        }

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

        // Retained elements rebuilt by BuildAsset; the canvas host is the IMGUI island the drawing surface lives in.
        IMGUIContainer canvasHost;
        Label zoomLabel;
        Rect lastCanvasRect;

        // ── AssetKit hooks ─────────────────────────────────────────────────────
        protected override string TypeLabel => "Lazor Shape";
        protected override string NewAssetName => "New Lazor Shape";
        protected override string DefaultFolder => "Assets/Lazor";
        protected override void InitializeNewAsset(LazorShape item) => item.AddExampleContent();
        protected override void OnAssetChanged() { layerSel = 0; activePath = -1; zoomInitialized = false; }
        protected override Texture2D RenderThumbnail(LazorShape item)
            => LazorRasterizer.Render(item, 96, new Color(0.05f, 0.06f, 0.08f, 1f));

        protected override void OnEnable()
        {
            base.OnEnable();
            wantsMouseMove = true;   // the canvas needs hover feedback; the old IMGUI ZUIWindow base set this for us
            EnsureWhiteTex();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        protected override void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            base.OnDisable();   // AssetKit unhooks projectChanged + clears browser thumbnails
            if (_white != null) { DestroyImmediate(_white); _white = null; }
            if (_disc != null) { DestroyImmediate(_disc); _disc = null; }
            CleanupPreview();   // tears down the hidden preview camera + RT (no-op when Shapes isn't present)
        }

        // After an undo/redo the shape's serialized data is restored under us — clamp the transient selection to the
        // restored layer count and repaint so the canvas actually reflects it (else undo looks like it did nothing).
        // ZuiWindow separately rebuilds the retained controls on undo, which is what refreshes the left panel.
        void OnUndoRedo()
        {
            if (shape != null)
                layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, shape.layers.Count - 1));
            activePath = -1;
            canvasHost?.MarkDirtyRepaint();
            Repaint();
        }

        // ── per-asset body: left ZUI panel | drag-splitter | grid canvas ──────────
        // A flex row that fills what the base's toolbar/Tags rows leave. The left panel keeps an explicit width the
        // splitter drives; the canvas pane takes the remaining space (minHeight:0 everywhere so content height can
        // never become a floor and overflow the window).
        protected override void BuildAsset(VisualElement root, LazorShape asset)
        {
            layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, asset.layers.Count - 1));

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;

            var left = BuildLeftPanel(asset);          // LazorWindow.Layers.cs
            split.Add(left);
            split.Add(BuildVerticalSplitter(left));
            split.Add(BuildCanvasPane());
            root.Add(split);
        }

        // The canvas pane: real ZUI chrome (zoom readout + Fit) above the IMGUI drawing island. The readout used to
        // be painted into the canvas itself as a floating HUD — it belongs in the window's own chrome, where it is
        // a normal element the layout owns and ZuiAudit can see.
        VisualElement BuildCanvasPane()
        {
            var col = Z.Column();
            col.style.flexGrow = 1f;
            col.style.minWidth = 200f;
            col.style.minHeight = 0f;

            zoomLabel = Z.Text($"{Mathf.RoundToInt(zoom)} px/cell", ZuiText.Small,
                "Current canvas zoom, in screen pixels per grid cell.");
            var chrome = Z.Row(
                Z.Flexible(),
                zoomLabel,
                Z.Button("Fit", "Frame the whole grid in the canvas.",
                    () => { FitView(lastCanvasRect); canvasHost?.MarkDirtyRepaint(); }));
            chrome.style.flexShrink = 0f;
            col.Add(chrome);

            canvasHost = new IMGUIContainer(() =>
            {
                // Inside an IMGUIContainer the GUI origin is the container itself, so the canvas rect is simply
                // (0,0,size) — which is exactly the clip-local space every transform in Canvas.cs already works in.
                lastCanvasRect = new Rect(Vector2.zero, canvasHost.contentRect.size);
                if (shape != null) DrawCanvas(lastCanvasRect);
            });
            canvasHost.style.flexGrow = 1f;
            canvasHost.style.minHeight = 0f;
            canvasHost.tooltip = "The drawing surface. Scroll to zoom toward the cursor, middle-drag or space-drag to pan, "
                               + "hold Ctrl to invert snapping.";
            canvasHost.focusable = true;             // so Enter / Backspace / Escape / Space reach the pen tools
            canvasHost.AddToClassList("zui-stage");
            canvasHost.RegisterCallback<PointerMoveEvent>(_ => canvasHost.MarkDirtyRepaint());
            col.Add(canvasHost);
            return col;
        }

        // Pointer-capture splitter (the DotGenWindow pattern): a 6px grab strip that widens/narrows the left panel.
        VisualElement BuildVerticalSplitter(VisualElement left)
        {
            var s = new VisualElement { tooltip = "Drag to resize the left panel (wider = more room for layer names)." };
            s.style.width = 6f;
            s.style.flexShrink = 0f;
            s.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            s.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0) { s.CapturePointer(e.pointerId); e.StopPropagation(); }
            });
            s.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!s.HasPointerCapture(e.pointerId)) return;
                leftWidth = Mathf.Clamp(leftWidth + e.deltaPosition.x, 220f, Mathf.Max(220f, position.width - 200f));
                left.style.width = leftWidth;
                e.StopPropagation();
            });
            s.RegisterCallback<PointerUpEvent>(e =>
            {
                if (s.HasPointerCapture(e.pointerId)) s.ReleasePointer(e.pointerId);
            });
            return s;
        }

        // The scroll-wheel zoom happens inside the IMGUI island, where nothing pushes a new value into the retained
        // readout — so DrawCanvas calls this at the end of every pass to keep the label honest.
        void SyncZoomLabel()
        {
            string s = $"{Mathf.RoundToInt(zoom)} px/cell";
            if (zoomLabel != null && zoomLabel.text != s) zoomLabel.text = s;
        }

        // An IMGUIContainer does not necessarily redraw from EditorWindow.Repaint() alone — it has to be marked
        // dirty itself. Every canvas-side repaint goes through here.
        void RepaintCanvas()
        {
            canvasHost?.MarkDirtyRepaint();
            Repaint();
        }

        void RecordShape(string label)
        {
            if (shape != null) Undo.RecordObject(shape, label);
        }

        void MarkDirty()
        {
            if (shape != null) EditorUtility.SetDirty(shape);
        }

        // A dial edit: the data changed but the panel's structure didn't, so only the canvas needs redrawing.
        // Rebuilding here would be wrong as well as wasteful — it would destroy focus mid-typing in a name field.
        void EditedValue()
        {
            MarkDirty();
            canvasHost?.MarkDirtyRepaint();
        }

        // A structural edit (layer added / removed / reordered / selected): the retained controls were built from
        // the old list, so the panel has to be rebuilt from the new one.
        void EditedStructure()
        {
            MarkDirty();
            Rebuild();
        }
    }
}
