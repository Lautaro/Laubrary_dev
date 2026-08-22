// TapestryWindow.Layers — the layer stack list (mirrors LatheWindow.Solids/PyrePlusWindow's layer-list
// chrome: reorder grip, enable toggle, select, rename-in-place, remove, + add/duplicate). Layer 0 = bottom
// of the stack, matching PyrePlus's own list-order-is-draw-order convention.
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Tapestry.Editor
{
    public partial class TapestryWindow
    {
        VisualElement layersListHost;

        void BuildLayersList(VisualElement root, TapestrySpec s)
        {
            var box = Z.Section("Layers", "Every generator layer in this texture, back (bottom of list) to "
                + "front — click ● to select it (its Generator/Modifiers appear below), drag the grip to reorder.",
                "tapestry.layers", icon: "stack");
            layersListHost = new VisualElement();
            box.Add(layersListHost);
            RebuildLayersList();
            box.Add(Z.HGroup(
                Z.Button("+ Add layer", "Append a new layer to the stack (undoable).", AddLayer),
                Z.Button("Duplicate", "Duplicate the selected layer, inserted right after it (undoable).", DuplicateSelectedLayer)));
            root.Add(box);
        }

        void RebuildLayersList()
        {
            if (layersListHost == null || spec == null || spec.layers == null) return;
            layersListHost.Clear();
            for (int i = 0; i < spec.layers.Count; i++)
                layersListHost.Add(BuildLayerRow(layersListHost, i));
        }

        VisualElement BuildLayerRow(VisualElement listHost, int i)
        {
            var layer = spec.layers[i];
            bool sel = i == layerSel;

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this layer in the stack.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
            {
                Dirty(() =>
                {
                    var m = spec.layers[from];
                    spec.layers.RemoveAt(from);
                    spec.layers.Insert(to, m);
                });
                layerSel = to;
                Rebuild();
            });
            row.Add(grip);

            row.Add(Z.Toggle("", "Show or hide this layer in the preview.", layer.enabled,
                v => Dirty(() => layer.enabled = v)));

            row.Add(Z.Button(sel ? "●" : "○", "Select this layer to edit it below.", () =>
            {
                layerSel = i;
                Rebuild();
            }).W(24f));

            var name = Z.TextInput(layer.name ?? "", "This layer's name — rename it right here.",
                v => Dirty(() => layer.name = v), 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.minWidth = 50f;
            name.AddToClassList("zui-audit-allow-stretch");
            name.RegisterCallback<PointerDownEvent>(_ => { if (layerSel != i) { layerSel = i; Rebuild(); } });
            row.Add(name);

            if (!layer.enabled) row.Add(Z.Text("off", ZuiText.Small, "This layer is currently hidden."));

            row.Add(Z.Button("×", "Remove this layer from the stack (undoable).", () =>
            {
                Dirty(() => spec.layers.RemoveAt(i));
                layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, spec.layers.Count - 1));
                Rebuild();
            }).W(22f));

            return row;
        }

        void AddLayer()
        {
            if (spec == null) return;
            Dirty(() => spec.layers.Add(new TapestryLayer
            {
                name = $"Layer {spec.layers.Count + 1}",
                generator = new TapestryPanelsGenerator(),
            }));
            layerSel = spec.layers.Count - 1;
            Rebuild();
        }

        void DuplicateSelectedLayer()
        {
            var sel = SelLayer;
            if (sel == null || spec == null) return;
            Dirty(() =>
            {
                var copy = sel.Clone();
                int idx = spec.layers.IndexOf(sel);
                spec.layers.Insert(idx + 1, copy);
                layerSel = idx + 1;
            });
            Rebuild();
        }
    }
}
