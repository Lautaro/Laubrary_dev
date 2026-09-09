// LatheMoldWindow — the authoring window for LatheMoldAsset: a node list (primitives + Union/Subtract +
// blend), each node's fields via ZuiReflect, and a live orbit preview. Mirrors LatheWindow's own layout
// conventions (solids-list-style node list, reflected per-item card) at a smaller scale — a Mold asset has
// no turntable/bake/skeleton/gizmo of its own, just the CSG stack itself.
//
// The preview reuses LathePreview wholesale rather than a second render pipeline: a throwaway 1-solid
// LatheSpec wraps a MoldedShapeModule pointed at the asset being edited, so every frame just re-points that
// module and calls the SAME Render() the main Lathe window uses.
using System;
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Lathe.Editor
{
    public class LatheMoldWindow : ZuiAssetWindow<LatheMoldAsset>
    {
        [MenuItem("Laubrary/Lathe Mold")]
        public static void Open() => GetWindow<LatheMoldWindow>("Lathe Mold");

        LatheMoldAsset moldAsset => Current;
        protected override string TypeLabel => "Lathe Mold";
        protected override string NewAssetName => "New Lathe Mold";
        protected override string DefaultFolder => "Assets/Lathe/Molds";

        IMGUIContainer preview;
        LathePreview previewRenderer;
        float orbitYaw = 35f, orbitPitch = -20f, orbitDist = 4f;
        int nodeSel = -1;
        VisualElement nodeListHost;

        LatheSpec previewSpec;
        MoldedShapeModule previewModule;

        protected override void OnDisable()
        {
            base.OnDisable();
            previewRenderer?.Dispose(); previewRenderer = null;
            if (previewSpec != null) { UnityEngine.Object.DestroyImmediate(previewSpec); previewSpec = null; }
        }

        protected override void OnAssetChanged() => nodeSel = -1;

        protected override void BuildAsset(VisualElement root, LatheMoldAsset asset)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 320f;
            var col = left.contentContainer;
            col.style.flexGrow = 1f;

            BuildSettingsBox(col, asset);
            BuildNodeList(col, asset);
            if (asset.nodes != null && nodeSel >= 0 && nodeSel < asset.nodes.Count)
                col.Add(BuildNodeBox(asset.nodes[nodeSel]));

            var rightPane = new VisualElement();
            rightPane.style.flexGrow = 1f;
            rightPane.style.minWidth = 260f;
            rightPane.style.minHeight = 0f;

            preview = new IMGUIContainer(() => DrawPreview(asset));
            preview.style.flexGrow = 1f;
            preview.style.minHeight = 260f;
            preview.AddToClassList("zui-stage");
            preview.tooltip = "Drag to orbit, scroll to zoom.";
            rightPane.Add(preview);

            root.Add(Z.Split("lathemold.split", 340f, left, rightPane));
        }

        void BuildSettingsBox(VisualElement root, LatheMoldAsset asset)
        {
            var box = Z.Section("Settings", "Bake resolution and grid size — the voxel grid that turns the "
                + "combined shapes into a real mesh.", "lathemold.settings", icon: "frame-corners");
            box.Add(Z.HGroup(
                Z.MicroSlider("Resolution", asset.resolution, 8f, 48f,
                    "Voxel grid resolution — higher is smoother but slower to bake. Baking is cached, so this "
                    + "only costs anything right after an edit, not on every repaint.",
                    v => Dirty(() => asset.resolution = Mathf.Clamp(Mathf.RoundToInt(v), 8, 48)), 150f, showValue: true, decimals: 0),
                Z.MicroSlider("Bounds", asset.boundsSize, 0.5f, 6f,
                    "World size of the bake grid, centred at the origin — must comfortably contain every node "
                    + "or they'll be clipped at the grid edge.",
                    v => Dirty(() => asset.boundsSize = v), 150f, showValue: true)));
            root.Add(box);
        }

        void BuildNodeList(VisualElement root, LatheMoldAsset asset)
        {
            var box = Z.Section("Nodes", "Primitives combined in list order — each fuses (∪ Union) with, or "
                + "cuts into (− Subtract), everything above it. Blend > 0 softens the seam instead of a clean cut.",
                "lathemold.nodes", icon: "stack");
            nodeListHost = new VisualElement();
            box.Add(nodeListHost);
            RebuildNodeList(asset);
            box.Add(Z.HGroup(
                Z.Button("+ Add node", "Append a new node (undoable).", () => AddNode(asset)),
                Z.Button("Duplicate", "Duplicate the selected node (undoable).", () => DuplicateNode(asset))));
            root.Add(box);
        }

        void RebuildNodeList(LatheMoldAsset asset)
        {
            if (nodeListHost == null) return;
            nodeListHost.Clear();
            asset.nodes ??= new List<LatheMoldNode>();
            for (int i = 0; i < asset.nodes.Count; i++)
                nodeListHost.Add(BuildNodeRow(asset, i));
        }

        VisualElement BuildNodeRow(LatheMoldAsset asset, int i)
        {
            var node = asset.nodes[i];
            bool sel = i == nodeSel;
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this node.");
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, row, nodeListHost, (from, to) =>
            {
                Dirty(() =>
                {
                    var m = asset.nodes[from];
                    asset.nodes.RemoveAt(from);
                    asset.nodes.Insert(to, m);
                });
                nodeSel = to;
                Rebuild();
            });
            row.Add(grip);

            row.Add(Z.Toggle("", "Enable or disable this node without removing it.", node.enabled,
                v => Dirty(() => node.enabled = v)));
            row.Add(Z.Button(sel ? "●" : "○", "Select this node to edit it below.", () => { nodeSel = i; Rebuild(); }).W(24f));
            row.Add(Z.Text(node.op == MoldOp.Union ? "∪" : "−", ZuiText.Body,
                node.op == MoldOp.Union ? "Union — fuses with the result so far." : "Subtract — cuts into the result so far.").W(18f));

            var name = Z.TextInput(node.name ?? "", "This node's name.", v => Dirty(() => node.name = v), 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.minWidth = 50f;
            // The rulebook's own exception — a NAME field may take the row's slack rather than truncate the
            // name — declared to the audit instead of showing up as an unexplained stretched control.
            name.AddToClassList("zui-audit-allow-stretch");
            name.RegisterCallback<PointerDownEvent>(_ => { if (nodeSel != i) { nodeSel = i; Rebuild(); } });
            row.Add(name);

            row.Add(Z.Button("×", "Remove this node (undoable).", () =>
            {
                Dirty(() => asset.nodes.RemoveAt(i));
                nodeSel = Mathf.Clamp(nodeSel, -1, asset.nodes.Count - 1);
                Rebuild();
            }).W(22f));
            return row;
        }

        void AddNode(LatheMoldAsset asset)
        {
            if (asset == null) return;
            Dirty(() =>
            {
                asset.nodes ??= new List<LatheMoldNode>();
                asset.nodes.Add(new LatheMoldNode { name = "Node " + (asset.nodes.Count + 1) });
                nodeSel = asset.nodes.Count - 1;
            });
            Rebuild();
        }

        void DuplicateNode(LatheMoldAsset asset)
        {
            if (asset == null || nodeSel < 0 || asset.nodes == null || nodeSel >= asset.nodes.Count) return;
            Dirty(() =>
            {
                var copy = asset.nodes[nodeSel].Clone();
                asset.nodes.Insert(nodeSel + 1, copy);
                nodeSel += 1;
            });
            Rebuild();
        }

        VisualElement BuildNodeBox(LatheMoldNode node)
        {
            var box = Z.Section("Node", "This node's shape, placement, operation and blend.", "lathemold.node", icon: "cube");
            var opt = new ZuiReflect.Options
            {
                OnBeforeChange = () => { if (moldAsset != null) Undo.RecordObject(moldAsset, "Edit Lathe Mold"); },
                OnChanged = () => { if (moldAsset != null) EditorUtility.SetDirty(moldAsset); preview?.MarkDirtyRepaint(); },
                OnStructureChanged = Rebuild,
                TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a mold node parameter.",
                ControlWidth = 150f,
            };
            ZuiReflect.FlowFields(box, node, opt);
            return box;
        }

        void Dirty(Action edit)
        {
            if (moldAsset == null) return;
            Undo.RecordObject(moldAsset, "Edit Lathe Mold");
            edit();
            EditorUtility.SetDirty(moldAsset);
            preview?.MarkDirtyRepaint();
        }

        void DrawPreview(LatheMoldAsset asset)
        {
            var rect = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (rect.width < 2f || rect.height < 2f) return;
            HandleOrbitInput(rect);

            previewRenderer ??= new LathePreview();
            if (previewSpec == null)
            {
                previewSpec = ScriptableObject.CreateInstance<LatheSpec>();
                previewSpec.hideFlags = HideFlags.HideAndDontSave;
                previewSpec.solids.Clear();
                previewModule = new MoldedShapeModule();
                previewSpec.solids.Add(new LatheSolid { name = "Preview", module = previewModule, tint = new Color(0.75f, 0.75f, 0.8f) });
            }
            previewModule.mold = asset;
            previewSpec.previewBackground = new Color(0.16f, 0.16f, 0.18f, 1f);

            var tex = previewRenderer.Render(previewSpec, rect, 0f, orbitYaw, orbitPitch, orbitDist);
            if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
        }

        void HandleOrbitInput(Rect rect)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive, rect);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition) && (e.button == 0 || e.button == 2)) { GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        orbitYaw += e.delta.x * 0.5f;
                        orbitPitch = Mathf.Clamp(orbitPitch - e.delta.y * 0.5f, -89f, 89f);
                        preview?.MarkDirtyRepaint();
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        orbitDist = Mathf.Clamp(orbitDist + e.delta.y * 0.3f, 0.5f, 20f);
                        preview?.MarkDirtyRepaint();
                        e.Use();
                    }
                    break;
            }
        }
    }
}
