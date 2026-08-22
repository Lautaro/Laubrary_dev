// LatheWindow.Module — the plug-in Module half of a solid's card. A module needs ZERO editor code: the
// picker lists every concrete LatheModule the loaded assemblies contain (grouped by [LatheModuleInfo]),
// and its public fields are drawn by ZuiReflect — mirrors PyrePlusWindow.Forms.cs applied to LatheModule.
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
        struct ModuleEntry { public Type type; public string group, label; }
        static List<ModuleEntry> _modules;

        static List<ModuleEntry> ModuleCatalog()
        {
            if (_modules != null) return _modules;
            var found = new List<ModuleEntry>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name.EndsWith(".Tests", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(LatheModule).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    var info = (LatheModuleInfoAttribute)Attribute.GetCustomAttribute(t, typeof(LatheModuleInfoAttribute));
                    string label = info?.DisplayName;
                    if (string.IsNullOrEmpty(label))
                    {
                        try { label = ((LatheModule)Activator.CreateInstance(t)).DisplayName; }
                        catch { label = null; }
                    }
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new ModuleEntry { type = t, group = info?.Group ?? "Solids", label = label });
                }
            }
            found.Sort((a, b) =>
            {
                int g = string.CompareOrdinal(a.group, b.group);
                return g != 0 ? g : string.CompareOrdinal(a.label, b.label);
            });
            _modules = found;
            return _modules;
        }

        void BuildModuleBox(VisualElement root, LatheSolid solid)
        {
            var box = Z.Section("Module", "How this solid's geometry is generated — pick a module, then tune its "
                + "own dials below.", "lathe.module", icon: "shapes");

            Button changeBtn = null;
            changeBtn = Z.Button("Change…", "Pick a different generation module for this solid — its current "
                + "dials are replaced (undoable).", () => ShowModulePicker(changeBtn, solid));
            box.Add(Z.Row(
                Z.Text(solid.module?.DisplayName ?? "None", ZuiText.Body,
                    solid.module?.Description ?? "No generation module assigned."),
                Z.Flexible(),
                changeBtn));

            if (solid.module != null)
            {
                var opt = new ZuiReflect.Options
                {
                    OnBeforeChange = () => { if (spec != null) Undo.RecordObject(spec, "Edit Lathe"); },
                    OnChanged = () => { if (spec != null) EditorUtility.SetDirty(spec); preview?.MarkDirtyRepaint(); },
                    OnStructureChanged = Rebuild,
                    TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a {solid.module.DisplayName} parameter.",
                    ControlWidth = 150f,
                };
                ZuiReflect.FlowFields(box, solid.module, opt);
            }
            root.Add(box);
        }

        void ShowModulePicker(VisualElement anchor, LatheSolid solid)
        {
            var menu = Z.Menu(anchor);
            string lastGroup = null;
            foreach (var e in ModuleCatalog())
            {
                if (e.group != lastGroup) { menu.Section(e.group); lastGroup = e.group; }
                var type = e.type;
                string label = e.label;
                menu.Item(label, $"Switch this solid to the {label} module — its current dials are replaced (undoable).", () =>
                {
                    Dirty(() => solid.module = (LatheModule)Activator.CreateInstance(type));
                    Rebuild();
                });
            }
            menu.Show();
        }
    }
}
