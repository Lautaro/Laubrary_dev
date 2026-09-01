// ShaperMockWindow is a disposable UI blueprint for the FUTURE real Shaper editor (T-0130, first vertical
// slice against SHAPER-UI-VISION-AND-DESIGN.md, T-0126). It owns only in-memory mock data — never
// ShaperNode / ShaperFillDef / any other committed Shaper production runtime type, and it cannot even
// name one: this file lives in its own assembly (ShaperMock.Editor), in its own namespace outside the
// Laubrary.Shaper tree, referencing only Laubrary.Zui.
//
// Demonstrates, end to end, one cold workflow against the design doc: a left ScrollView of Z.Sections
// (Canvas, Layers, then the selected node's Shape + Fill), a right pane with a breadcrumb bar above a
// live preview, a shape picker that rebuilds the Shape section's dial set per primitive kind, and the new
// Shaper-native fill control — with Undo wired through the Val()/FillRow() pattern (design doc §J1) from
// the very first control, not retrofitted after.
using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaperMock.Editor
{
    public sealed class ShaperMockWindow : ZuiWindow
    {
        [MenuItem("Laubrary/Shaper Mock (Prototype)")]
        public static void Open()
        {
            var w = GetWindow<ShaperMockWindow>("Shaper Mock");
            w.minSize = new Vector2(760f, 480f);
        }

        // Not serialized on purpose: the mock is disposable, so a domain reload legitimately resets it —
        // EnsureDocument() is what keeps that from being a null-reference instead of a fresh seeded doc.
        // The document IS a real ScriptableObject (see ShaperMockData.cs) purely so Undo.RecordObject has
        // something real to record against, exactly like Pyre's own `spec`.
        ShaperMockDocument document;
        int selectedLayer;

        VisualElement layerListHost;
        VisualElement nodeBody;         // the selected layer's own Shape + Fill cards (§B2)
        ZuiBreadcrumb breadcrumb;
        ShaperMockPreviewStage stage;

        // Rebuild() recreates the left ScrollView, so its offset has to be carried across by hand or the
        // pane snaps to the top under the control the user was just using (mirrors ChunksMockWindow).
        ScrollView leftPane;
        Vector2 carriedScroll;

        protected override void BuildUI(VisualElement root)
        {
            EnsureDocument();
            root.style.minHeight = 0f;

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 300f;
            left.style.minHeight = 0f;
            BuildLeft(left.contentContainer);
            leftPane = left;
            RestoreScroll(left);

            var right = new VisualElement();
            right.style.minWidth = 260f;
            right.style.minHeight = 0f;
            BuildRight(right);

            root.Add(Z.Split("shaper.mock.split.v1", 380f, left, right));
        }

        protected override void OnBeforeRebuild()
        {
            if (leftPane != null) carriedScroll = leftPane.scrollOffset;
            leftPane = null;
        }

        void RestoreScroll(ScrollView view)
        {
            if (carriedScroll == Vector2.zero) return;
            Vector2 wanted = carriedScroll;
            EventCallback<GeometryChangedEvent> once = null;
            once = _ =>
            {
                view.contentContainer.UnregisterCallback(once);
                view.scrollOffset = wanted;
            };
            view.contentContainer.RegisterCallback(once);
        }

        void EnsureDocument()
        {
            if (document == null) document = ShaperMockDocument.CreateSeeded();
            if (selectedLayer < 0 || selectedLayer >= document.layers.Count) selectedLayer = 0;
        }

        ShaperMockLayer Selected => document != null && selectedLayer >= 0 && selectedLayer < document.layers.Count
            ? document.layers[selectedLayer] : null;

        // ── left pane: Canvas, Layers, then the selected node's own cards ───────────────────────────

        void BuildLeft(VisualElement root)
        {
            BuildCanvas(root);
            BuildLayers(root);

            nodeBody = new VisualElement();
            RebuildNodeBody();
            root.Add(nodeBody);
        }

        void BuildCanvas(VisualElement root)
        {
            var box = Z.Section("Canvas", "The output resolution, frame count and seed.", "shaper.mock.canvas",
                icon: "frame-corners");
            box.Add(Z.HGroup(
                Dial("Width", "Canvas width in pixels.", document.canvas.width, 8f, 256f,
                    v => document.canvas.width = Mathf.RoundToInt(v), decimals: 0),
                Dial("Height", "Canvas height in pixels.", document.canvas.height, 8f, 256f,
                    v => document.canvas.height = Mathf.RoundToInt(v), decimals: 0)));
            box.Add(Z.HGroup(
                Dial("Frames", "How many frames the document bakes to.", document.canvas.frameCount, 1f, 64f,
                    v => document.canvas.frameCount = Mathf.RoundToInt(v), decimals: 0),
                Z.Field("Seed", "Random seed — every dial's randomness (once this mock has any) derives from it.",
                    Z.Int(document.canvas.seed, "Random seed.",
                        v => Change(() => document.canvas.seed = v), 70f))));
            root.Add(box);
        }

        void BuildLayers(VisualElement root)
        {
            var box = Z.Section("Layers",
                "The document's layers. Click a layer to edit its Shape/Fill below; drag the grip to reorder.",
                "shaper.mock.layers", icon: "stack");
            layerListHost = new VisualElement();
            box.Add(layerListHost);
            RebuildLayerList();
            box.Add(Z.Button("+ Add layer", "Append a new Primitive layer to the document (undoable).",
                AddLayer).W(110f));
            root.Add(box);
        }

        void RebuildLayerList()
        {
            if (layerListHost == null) return;
            layerListHost.Clear();
            for (int i = 0; i < document.layers.Count; i++)
                layerListHost.Add(BuildLayerRow(layerListHost, i));
        }

        VisualElement BuildLayerRow(VisualElement listHost, int li)
        {
            var layer = document.layers[li];
            bool sel = li == selectedLayer;

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this layer.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
            {
                Change(() =>
                {
                    var l = document.layers[from];
                    document.layers.RemoveAt(from);
                    document.layers.Insert(to, l);
                });
                selectedLayer = to;
                RebuildLayerList();
                RebuildNodeBody();
                RefreshBreadcrumb();
            });
            row.Add(grip);

            row.Add(Z.Toggle("", "Show or hide this layer in the preview.", layer.enabled, v =>
            {
                Change(() => layer.enabled = v);
                RebuildLayerList();
            }));

            row.Add(Z.Button(sel ? "●" : "○", "Select this layer to edit it below.", () => SelectLayer(li)).W(24f));

            var name = Z.TextInput(layer.name ?? "", "This layer's name — rename it right here.", v =>
            {
                Undo.RecordObject(document, "Rename layer");
                layer.name = v;
                EditorUtility.SetDirty(document);
                if (sel) RefreshBreadcrumb();
            }, 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.minWidth = 50f;
            name.AddToClassList("zui-audit-allow-stretch");   // the rulebook's name-field stretch exception
            name.RegisterCallback<PointerDownEvent>(_ => { if (selectedLayer != li) SelectLayer(li); });
            row.Add(name);

            var remove = Z.Button("×", "Remove this layer (undoable).", () => RemoveLayer(li)).W(20f);
            remove.SetEnabled(document.layers.Count > 1);   // always keep at least one layer to edit
            row.Add(remove);
            return row;
        }

        void SelectLayer(int li)
        {
            if (li == selectedLayer) return;
            selectedLayer = li;
            RebuildLayerList();
            RebuildNodeBody();
            RefreshBreadcrumb();
        }

        void AddLayer()
        {
            Undo.RecordObject(document, "Add layer");
            document.layers.Add(new ShaperMockLayer { name = "Layer " + (document.layers.Count + 1) });
            EditorUtility.SetDirty(document);
            selectedLayer = document.layers.Count - 1;
            RebuildLayerList();
            RebuildNodeBody();
            RefreshBreadcrumb();
        }

        void RemoveLayer(int li)
        {
            if (document.layers.Count <= 1) return;
            Undo.RecordObject(document, "Remove layer");
            document.layers.RemoveAt(li);
            EditorUtility.SetDirty(document);
            if (selectedLayer >= document.layers.Count) selectedLayer = document.layers.Count - 1;
            RebuildLayerList();
            RebuildNodeBody();
            RefreshBreadcrumb();
        }

        // ── the selected node's own card stack: Shape, then Fill (design doc §B4) ───────────────────

        void RebuildNodeBody()
        {
            if (nodeBody == null) return;
            nodeBody.Clear();
            var layer = Selected;
            if (layer == null || layer.root == null) return;

            nodeBody.Add(BuildShapeSection(layer.root));
            nodeBody.Add(BuildFillSection(layer.root));
        }

        VisualElement BuildShapeSection(ShaperMockNode node)
        {
            var box = Z.Section("Shape",
                "The primitive's own geometry — the dial set shown depends entirely on its kind.",
                "shaper.mock.shape", icon: "shapes");

            var kindBody = new VisualElement();
            void RebuildKindBody()
            {
                kindBody.Clear();
                switch (node.shapeKind)
                {
                    case ShaperMockShapeKind.Disc:
                        kindBody.Add(Dial("Radius", "The disc's radius.", node.discRadius, 0.02f, 1f,
                            v => node.discRadius = v));
                        break;

                    case ShaperMockShapeKind.Ngon:
                        kindBody.Add(Z.HGroup(
                            Dial("Sides", "How many sides the polygon has.", node.ngonSides, 3f, 16f,
                                v => node.ngonSides = Mathf.RoundToInt(v), decimals: 0),
                            Dial("Radius", "The polygon's circumradius.", node.ngonRadius, 0.02f, 1f,
                                v => node.ngonRadius = v)));
                        kindBody.Add(Z.HGroup(
                            Dial("Rotation", "The polygon's rotation, in degrees.", node.ngonRotation, 0f, 360f,
                                v => node.ngonRotation = v, decimals: 0),
                            Dial("Corner radius", "Rounds each corner by this fraction.", node.ngonCornerRadius,
                                0f, 1f, v => node.ngonCornerRadius = v)));
                        break;

                    case ShaperMockShapeKind.Star:
                        kindBody.Add(Z.HGroup(
                            Dial("Arms", "How many points the star has.", node.starArms, 3f, 12f,
                                v => node.starArms = Mathf.RoundToInt(v), decimals: 0),
                            Dial("Radius", "The star's outer radius.", node.starRadius, 0.02f, 1f,
                                v => node.starRadius = v)));
                        kindBody.Add(Z.HGroup(
                            Dial("Length", "How far the arms reach, relative to the outer radius.",
                                node.starLength, 0.05f, 1f, v => node.starLength = v),
                            Dial("Base width", "How wide each arm's base is.", node.starBaseWidth, 0.02f, 1f,
                                v => node.starBaseWidth = v)));
                        kindBody.Add(Dial("Skew", "Twists the arms, in degrees.", node.starSkew, -180f, 180f,
                            v => node.starSkew = v, decimals: 0));
                        break;
                }
            }
            RebuildKindBody();

            var picker = Z.MiniRadio((int)node.shapeKind, new[] { "Disc", "N-gon", "Star" },
                "Which primitive this node generates. Switching kind rebuilds the dial set below.",
                v => Change(() =>
                {
                    node.shapeKind = (ShaperMockShapeKind)v;
                    RebuildKindBody();
                }));
            box.Add(Z.Field("Kind", "Which primitive this node generates.", picker));
            box.Add(kindBody);
            return box;
        }

        VisualElement BuildFillSection(ShaperMockNode node)
        {
            var box = Z.Section("Fill", "How this node is painted.", "shaper.mock.fill", icon: "palette");
            var control = new ShaperMockFillControl("Fill", node.fill,
                "How this node is painted — right-click to choose Solid or Gradient.")
            {
                OnBeforeMutate = () => Undo.RecordObject(document, "Edit Shaper Mock"),
                OnChanged = () => { EditorUtility.SetDirty(document); RefreshPreview(); },
            };
            box.Add(control);
            return box;
        }

        // ── right pane: breadcrumb + preview ─────────────────────────────────────────────────────────

        void BuildRight(VisualElement root)
        {
            breadcrumb = Z.Breadcrumb(new List<string>(), null);
            root.Add(breadcrumb);
            RefreshBreadcrumb();

            var previewSection = Z.Section("Preview",
                "A pixel-exact live render of the selected layer's shape and fill — not a Mirage-quality preview.",
                "shaper.mock.preview", icon: "eye");
            previewSection.style.flexGrow = 1f;
            previewSection.style.minHeight = 0f;
            previewSection.contentContainer.style.flexGrow = 1f;
            previewSection.contentContainer.style.minHeight = 0f;

            stage = new ShaperMockPreviewStage(document, () => Selected);
            stage.style.flexGrow = 1f;
            stage.style.minHeight = 200f;
            previewSection.Add(stage);
            root.Add(previewSection);
        }

        // Nothing to drill into yet in this slice (no Bag/Composite node exists), so the path is always
        // exactly one segment — the selected layer's name. ZuiBreadcrumb already makes the LAST segment
        // non-interactive, which is correct here: there is nowhere clicking it could go.
        void RefreshBreadcrumb()
        {
            if (breadcrumb == null) return;
            var layer = Selected;
            string name = layer != null && !string.IsNullOrEmpty(layer.name) ? layer.name : "(unnamed layer)";
            breadcrumb.SetPath(new List<string> { name }, null);
        }

        void RefreshPreview() => stage?.Refresh();

        // ── Undo/dirty wiring — the Val()/FillRow() pattern (design doc §J1), from the very first dial ──

        VisualElement Dial(string label, string tooltip, float value, float min, float max, Action<float> set,
            int decimals = -1)
            => Z.MicroSlider(label, value, min, max, tooltip, v => Change(() => set(v)), 140f, decimals: decimals);

        void Change(Action apply)
        {
            if (document != null) Undo.RecordObject(document, "Edit Shaper Mock");
            apply();
            if (document != null) EditorUtility.SetDirty(document);
            RefreshPreview();
        }
    }
}
