// LatheWindow.Solids — the solid stack list (mirrors PyreWindow's layer-list chrome: reorder grip,
// enable toggle, select, rename-in-place, remove, + add/duplicate).
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Lathe.Editor
{
    public partial class LatheWindow
    {
        VisualElement solidsListHost;

        void BuildSolidsList(VisualElement root, LatheSpec s)
        {
            var box = Z.Section("Solids", "Every solid sharing this scene — click ● to select it (its Transform/"
                + "Module/Modifiers appear below), drag the grip to reorder.", "lathe.solids", icon: "stack");
            solidsListHost = new VisualElement();
            box.Add(solidsListHost);
            RebuildSolidsList();
            box.Add(Z.HGroup(
                Z.Button("+ Add solid", "Append a new default solid to the scene (undoable).", AddSolid),
                Z.Button("Duplicate", "Duplicate the selected solid, inserted right after it (undoable).", DuplicateSelectedSolid)));
            root.Add(box);
        }

        void RebuildSolidsList()
        {
            if (solidsListHost == null || spec == null || spec.solids == null) return;
            solidsListHost.Clear();
            for (int i = 0; i < spec.solids.Count; i++)
                solidsListHost.Add(BuildSolidRow(solidsListHost, i));
        }

        VisualElement BuildSolidRow(VisualElement listHost, int i)
        {
            var solid = spec.solids[i];
            bool sel = i == solidSel;

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this solid in the stack.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
            {
                Dirty(() =>
                {
                    var m = spec.solids[from];
                    spec.solids.RemoveAt(from);
                    spec.solids.Insert(to, m);
                });
                solidSel = to;
                Rebuild();
            });
            row.Add(grip);

            row.Add(Z.Toggle("", "Show or hide this solid in the preview.", solid.enabled,
                v => Dirty(() => solid.enabled = v)));

            row.Add(Z.Button(sel ? "●" : "○", "Select this solid to edit it below.", () =>
            {
                solidSel = i;
                Rebuild();
            }).W(24f));

            var name = Z.TextInput(solid.name ?? "", "This solid's name — rename it right here.",
                v => Dirty(() => solid.name = v), 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.minWidth = 50f;
            name.AddToClassList("zui-audit-allow-stretch");
            name.RegisterCallback<PointerDownEvent>(_ => { if (solidSel != i) { solidSel = i; Rebuild(); } });
            row.Add(name);

            if (!solid.enabled) row.Add(Z.Text("off", ZuiText.Small, "This solid is currently hidden."));

            row.Add(Z.Button("×", "Remove this solid from the scene (undoable).", () =>
            {
                Dirty(() => spec.solids.RemoveAt(i));
                solidSel = Mathf.Clamp(solidSel, 0, Mathf.Max(0, spec.solids.Count - 1));
                Rebuild();
            }).W(22f));

            return row;
        }

        void AddSolid()
        {
            if (spec == null) return;
            Dirty(() => spec.solids.Add(new LatheSolid { name = $"Solid {spec.solids.Count + 1}" }));
            solidSel = spec.solids.Count - 1;
            Rebuild();
        }

        void DuplicateSelectedSolid()
        {
            var sel = SelSolid;
            if (sel == null || spec == null) return;
            Dirty(() =>
            {
                var copy = sel.Clone();
                int idx = spec.solids.IndexOf(sel);
                spec.solids.Insert(idx + 1, copy);
                solidSel = idx + 1;
            });
            Rebuild();
        }
    }
}
