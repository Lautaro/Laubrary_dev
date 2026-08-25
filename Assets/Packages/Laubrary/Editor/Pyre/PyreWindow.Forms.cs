// PyreWindow.Forms — the plug-in form half of the Shape section (PyreForm, Runtime/Pyre/PyreForm.cs).
//
// A form needs ZERO editor code: the picker lists every concrete PyreForm the loaded assemblies contain (grouped by
// its [PyreFormInfo]), and a picked form's card is its public fields drawn by ZuiReflect — [Range] makes a slider,
// [Tooltip] the hover text, a ZUIValue the full animatable control — with the same Undo / dirty wiring the modifier
// cards use. The catalog scan is the modifier catalog's pattern (AddableModifiers) applied to PyreForm.
using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Pyre.Editor
{
    public partial class PyreWindow
    {
        struct FormEntry { public Type type; public string group, label, icon; }
        static List<FormEntry> _forms;

        // Every concrete PyreForm with a parameterless constructor, from every loaded assembly (a family asmdef the
        // core never references is found the same way), sorted group-then-label. Cached per domain.
        static List<FormEntry> FormCatalog()
        {
            if (_forms != null) return _forms;
            var found = new List<FormEntry>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Test fixtures (Laubrary.Pyre.Tests and the like) declare throwaway forms; they must never
                // reach the user's picker.
                if (asm.GetName().Name.EndsWith(".Tests", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(PyreForm).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    var info = (PyreFormInfoAttribute)Attribute.GetCustomAttribute(t, typeof(PyreFormInfoAttribute));
                    string label = info?.DisplayName;
                    if (string.IsNullOrEmpty(label))
                    {
                        try { label = ((PyreForm)Activator.CreateInstance(t)).DisplayName; }
                        catch { label = null; }
                    }
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new FormEntry { type = t, group = info?.Group ?? "Forms", label = label, icon = info?.Icon });
                }
            }
            found.Sort((a, b) =>
            {
                int g = string.CompareOrdinal(a.group, b.group);
                return g != 0 ? g : string.CompareOrdinal(a.label, b.label);
            });
            _forms = found;
            return _forms;
        }

        // The picker columns for the plug-in forms — one column per [PyreFormInfo] group, beside the built-in
        // 3D / 2D / Special columns. Picking creates a fresh form (defaults) on the layer; the current one is checked.
        void AddFormColumns(VisualElement row, PyreLayer s, Action close)
        {
            var catalog = FormCatalog();
            if (catalog.Count == 0) return;
            string group = null;
            VisualElement col = null;
            foreach (var e in catalog)
            {
                if (e.group != group)
                {
                    group = e.group;
                    col = new VisualElement();
                    col.style.flexDirection = FlexDirection.Column;
                    col.style.marginRight = 12;
                    col.style.minWidth = 104;
                    var head = new Label(group)
                    {
                        tooltip = $"{group} — plug-in forms (each is its own module with its own dials; the Swarm places whole-layer forms, one instance per particle).",
                        pickingMode = PickingMode.Ignore,
                    };
                    head.AddToClassList("zui-menu__section");
                    col.Add(head);
                    row.Add(col);
                }
                var type = e.type;
                var item = new VisualElement { tooltip = $"{e.label}: switch this layer to the {e.label} form (its dials appear in the Shape section; the current form's settings are replaced — undoable)." };
                item.AddToClassList("zui-menu__item");
                item.style.flexDirection = FlexDirection.Row;
                item.style.alignItems = Align.Center;
                var check = new Label(s.form != null && s.form.GetType() == type ? "✓" : "") { pickingMode = PickingMode.Ignore };
                check.AddToClassList("zui-menu__check");
                item.Add(check);
                var img = e.icon != null ? Z.Icon(e.icon, 14f) : null;
                if (img != null) { img.pickingMode = PickingMode.Ignore; img.style.marginRight = 5f; item.Add(img); }
                var lbl = new Label(e.label) { pickingMode = PickingMode.Ignore };
                lbl.AddToClassList("zui-menu__label");
                item.Add(lbl);
                item.AddManipulator(new Clickable(() =>
                {
                    if (s.form == null || s.form.GetType() != type)
                        Dirty(() => s.form = (PyreForm)Activator.CreateInstance(type));
                    RebuildShape(); RebuildSwarm();
                    close?.Invoke();
                }));
                col.Add(item);
            }
        }

        // The form's card: its fields, flowed (short dials share a row, a curve takes a line), under a box titled
        // with the form's name whose tooltip is the form's own description.
        void BuildFormCard(PyreLayer s)
        {
            var form = s.form;
            var box = Z.BoxKeyed(form.DisplayName, form.Description, "pyreplus.form");
            var body = new VisualElement();
            ZuiReflect.FlowFields(body, form, FormDrawerOptions(s));
            box.Add(body);
            shapeBody.Add(box);
        }

        ZuiReflect.Options FormDrawerOptions(PyreLayer s) => new ZuiReflect.Options
        {
            OnBeforeChange = () => { if (spec != null) Undo.RecordObject(spec, "Edit Pyre Plus form"); },
            OnChanged = () => { if (spec != null) EditorUtility.SetDirty(spec); MarkDirty(); },
            OnStructureChanged = RebuildShape,
            // A dial that only acts when the swarm ignites several instances is hidden while the swarm is off.
            Skip = f => !s.swarmEnabled && Attribute.IsDefined(f, typeof(PyreSwarmOnlyAttribute)),
            // A whole-layer form's dials Eval frame-global, so Min-Max would resolve to one constant for the whole
            // animation — hidden, like the modifier cards; the runtime Duration/Warmup/Loop row is meaningless on the
            // frame-baked timeline — hidden too. Frame markers on the curve, like the hand-written Val rows. The
            // value controls keep the SAME fixed width as the plain sliders (no grow) so a card of mixed dials flows
            // as one even grid — a grown value claims a whole line and leaves the next dial's row half empty.
            ConfigureValue = (f, vopt) =>
            {
                vopt.allowMinMax = false; vopt.hideCurveTiming = true; vopt.hideCurveRange = true; vopt.hideLiveReadout = true;
                vopt.controlWidth = 150f;
                vopt.frameCount = spec != null ? spec.frameCount : 0;
            },
            ControlWidth = 150f,
        };
    }
}
