// LatheWindow.Modifiers — the per-solid mesh-modifier stack (mirrors PyreWindow.Modifiers.cs's
// add-menu + reflected card pattern, applied to LatheMeshModifier instead of PyreModifier).
using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Lathe.Editor
{
    public partial class LatheWindow
    {
        struct ModifierEntry { public Type type; public string group, label; }
        static List<ModifierEntry> _modifiers;

        static List<ModifierEntry> ModifierCatalog()
        {
            if (_modifiers != null) return _modifiers;
            var found = new List<ModifierEntry>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name.EndsWith(".Tests", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(LatheMeshModifier).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    var info = (LatheModifierInfoAttribute)Attribute.GetCustomAttribute(t, typeof(LatheModifierInfoAttribute));
                    string label = info?.DisplayName;
                    if (string.IsNullOrEmpty(label))
                    {
                        try { label = ((LatheMeshModifier)Activator.CreateInstance(t)).DisplayName; }
                        catch { label = null; }
                    }
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new ModifierEntry { type = t, group = info?.Group ?? "Modifiers", label = label });
                }
            }
            found.Sort((a, b) =>
            {
                int g = string.CompareOrdinal(a.group, b.group);
                return g != 0 ? g : string.CompareOrdinal(a.label, b.label);
            });
            _modifiers = found;
            return _modifiers;
        }

        void BuildModifiersBox(VisualElement root, LatheSolid solid)
        {
            var box = Z.Section("Modifiers", "Post-process this solid's generated mesh — mirror across a plane, "
                + "and more as they're added.", "lathe.modifiers", icon: "stack");
            solid.modifiers ??= new List<LatheMeshModifier>();

            for (int i = 0; i < solid.modifiers.Count; i++)
            {
                int idx = i;
                var m = solid.modifiers[i];
                if (m == null) continue;
                var card = Z.BoxKeyed(m.DisplayName, m.Description, $"lathe.modifier.{solid.name}.{idx}");
                card.Add(Z.Row(
                    Z.Toggle("On", "Enable or disable this modifier without removing it.", m.enabled,
                        v => Dirty(() => m.enabled = v)),
                    Z.Flexible(),
                    Z.Button("Remove", $"Remove the {m.DisplayName} modifier from this solid (undoable).", () =>
                    {
                        Dirty(() => solid.modifiers.RemoveAt(idx));
                        Rebuild();
                    })));

                var opt = new ZuiReflect.Options
                {
                    OnBeforeChange = () => { if (spec != null) Undo.RecordObject(spec, "Edit Lathe"); },
                    OnChanged = () => { if (spec != null) EditorUtility.SetDirty(spec); preview?.MarkDirtyRepaint(); },
                    OnStructureChanged = Rebuild,
                    TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a {m.DisplayName} parameter.",
                    ControlWidth = 150f,
                };
                ZuiReflect.FlowFields(card, m, opt);
                box.Add(card);
            }

            Button addBtn = null;
            addBtn = Z.Button("+ Add modifier", "Add a mesh modifier to this solid's stack (undoable).",
                () => ShowModifierPicker(addBtn, solid));
            box.Add(addBtn);
            root.Add(box);
        }

        void ShowModifierPicker(VisualElement anchor, LatheSolid solid)
        {
            var menu = Z.Menu(anchor);
            string lastGroup = null;
            foreach (var e in ModifierCatalog())
            {
                if (e.group != lastGroup) { menu.Section(e.group); lastGroup = e.group; }
                var type = e.type;
                string label = e.label;
                menu.Item(label, $"Add the {label} modifier to this solid's stack.", () =>
                {
                    Dirty(() =>
                    {
                        solid.modifiers ??= new List<LatheMeshModifier>();
                        solid.modifiers.Add((LatheMeshModifier)Activator.CreateInstance(type));
                    });
                    Rebuild();
                });
            }
            menu.Show();
        }
    }
}
