// TapestryWindow.Modifiers — the modifier stack, reused for BOTH scopes TapestryLayerModifier serves: a
// layer's own local stack (post-processes just that layer's buffer before it's blended into the composite)
// and the spec-wide global stack (post-processes the whole finished composite, after every layer) — same
// "one picker, two target lists" pattern PyrePlusWindow.Modifiers.cs uses for its own layer-local vs
// spec.globalModifiers lists.
using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Tapestry.Editor
{
    public partial class TapestryWindow
    {
        struct TapModifierEntry { public Type type; public string group, label; }
        static List<TapModifierEntry> _modifiers;

        static List<TapModifierEntry> ModifierCatalog()
        {
            if (_modifiers != null) return _modifiers;
            var found = new List<TapModifierEntry>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name.EndsWith(".Tests", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(TapestryLayerModifier).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    var info = (TapestryModifierInfoAttribute)Attribute.GetCustomAttribute(t, typeof(TapestryModifierInfoAttribute));
                    string label = info?.DisplayName;
                    if (string.IsNullOrEmpty(label))
                    {
                        try { label = ((TapestryLayerModifier)Activator.CreateInstance(t)).DisplayName; }
                        catch { label = null; }
                    }
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new TapModifierEntry { type = t, group = info?.Group ?? "Modifiers", label = label });
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

        void BuildLayerModifiersBox(VisualElement root, TapestryLayer layer)
        {
            layer.modifiers ??= new List<TapestryLayerModifier>();
            BuildModifierStack(root, "Modifiers", "Post-process THIS layer's own buffer, before it's blended "
                + "into the stack below it.", "tapestry.modifiers." + layer.name, layer.modifiers);
        }

        void BuildGlobalModifiersBox(VisualElement root, TapestrySpec s)
        {
            s.globalModifiers ??= new List<TapestryLayerModifier>();
            BuildModifierStack(root, "Global Modifiers", "Post-process the WHOLE finished composite, after "
                + "every layer above has been drawn and blended — wraps the entire stack.",
                "tapestry.globalmodifiers", s.globalModifiers);
        }

        void BuildModifierStack(VisualElement root, string title, string tooltip, string keyPrefix, List<TapestryLayerModifier> list)
        {
            var box = Z.Section(title, tooltip, keyPrefix, icon: "stack");

            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var m = list[i];
                if (m == null) continue;
                var card = Z.BoxKeyed(m.DisplayName, m.Description, $"{keyPrefix}.{idx}");
                card.Add(Z.Row(
                    Z.Toggle("On", "Enable or disable this modifier without removing it.", m.enabled,
                        v => Dirty(() => m.enabled = v)),
                    Z.Flexible(),
                    Z.Button("Remove", $"Remove the {m.DisplayName} modifier from this stack (undoable).", () =>
                    {
                        Dirty(() => list.RemoveAt(idx));
                        Rebuild();
                    })));

                var opt = new ZuiReflect.Options
                {
                    OnBeforeChange = () => { if (spec != null) Undo.RecordObject(spec, "Edit Tapestry"); },
                    OnChanged = () => { if (spec != null) EditorUtility.SetDirty(spec); bakeDirty = true; preview?.MarkDirtyRepaint(); },
                    OnStructureChanged = Rebuild,
                    TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a {m.DisplayName} parameter.",
                    ControlWidth = 150f,
                };
                ZuiReflect.FlowFields(card, m, opt);
                box.Add(card);
            }

            Button addBtn = null;
            addBtn = Z.Button("+ Add modifier", "Add a modifier to this stack (undoable).",
                () => ShowModifierPicker(addBtn, list));
            box.Add(addBtn);
            root.Add(box);
        }

        void ShowModifierPicker(VisualElement anchor, List<TapestryLayerModifier> list)
        {
            var menu = Z.Menu(anchor);
            string lastGroup = null;
            foreach (var e in ModifierCatalog())
            {
                if (e.group != lastGroup) { menu.Section(e.group); lastGroup = e.group; }
                var type = e.type;
                string label = e.label;
                menu.Item(label, $"Add the {label} modifier to this stack.", () =>
                {
                    Dirty(() => list.Add((TapestryLayerModifier)Activator.CreateInstance(type)));
                    Rebuild();
                });
            }
            menu.Show();
        }
    }
}
