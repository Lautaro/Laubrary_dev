// TapestryWindow.Generators — the plug-in Generator half of a layer's card. A generator needs ZERO editor
// code: the picker lists every concrete TapestryGenerator the loaded assemblies contain (grouped by
// [TapestryGeneratorInfo]), and its public fields are drawn by ZuiReflect — mirrors LatheWindow.Module.cs
// applied to TapestryGenerator instead of LatheModule.
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
        struct GeneratorEntry { public Type type; public string group, label; }
        static List<GeneratorEntry> _generators;

        static List<GeneratorEntry> GeneratorCatalog()
        {
            if (_generators != null) return _generators;
            var found = new List<GeneratorEntry>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name.EndsWith(".Tests", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(TapestryGenerator).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    var info = (TapestryGeneratorInfoAttribute)Attribute.GetCustomAttribute(t, typeof(TapestryGeneratorInfoAttribute));
                    string label = info?.DisplayName;
                    if (string.IsNullOrEmpty(label))
                    {
                        try { label = ((TapestryGenerator)Activator.CreateInstance(t)).DisplayName; }
                        catch { label = null; }
                    }
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new GeneratorEntry { type = t, group = info?.Group ?? "Generators", label = label });
                }
            }
            found.Sort((a, b) =>
            {
                int g = string.CompareOrdinal(a.group, b.group);
                return g != 0 ? g : string.CompareOrdinal(a.label, b.label);
            });
            _generators = found;
            return _generators;
        }

        void BuildGeneratorBox(VisualElement root, TapestryLayer layer)
        {
            var box = Z.Section("Generator", "How this layer's content is generated — pick a generator, then "
                + "tune its own dials below.", "tapestry.generator", icon: "shapes");

            Button changeBtn = null;
            changeBtn = Z.Button("Change…", "Pick a different generator for this layer — its current dials "
                + "are replaced (undoable).", () => ShowGeneratorPicker(changeBtn, layer));
            box.Add(Z.Row(
                Z.Text(layer.generator?.DisplayName ?? "None", ZuiText.Body,
                    layer.generator?.Description ?? "No generator assigned."),
                Z.Flexible(),
                changeBtn));

            if (layer.generator != null)
            {
                var opt = new ZuiReflect.Options
                {
                    OnBeforeChange = () => { if (spec != null) Undo.RecordObject(spec, "Edit Tapestry"); },
                    OnChanged = () => { if (spec != null) EditorUtility.SetDirty(spec); bakeDirty = true; preview?.MarkDirtyRepaint(); },
                    OnStructureChanged = Rebuild,
                    TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a {layer.generator.DisplayName} parameter.",
                    ControlWidth = 150f,
                };
                ZuiReflect.FlowFields(box, layer.generator, opt);
            }
            root.Add(box);
        }

        void ShowGeneratorPicker(VisualElement anchor, TapestryLayer layer)
        {
            var menu = Z.Menu(anchor);
            string lastGroup = null;
            foreach (var e in GeneratorCatalog())
            {
                if (e.group != lastGroup) { menu.Section(e.group); lastGroup = e.group; }
                var type = e.type;
                string label = e.label;
                menu.Item(label, $"Switch this layer to the {label} generator — its current dials are replaced (undoable).", () =>
                {
                    Dirty(() => layer.generator = (TapestryGenerator)Activator.CreateInstance(type));
                    Rebuild();
                });
            }
            menu.Show();
        }
    }
}
