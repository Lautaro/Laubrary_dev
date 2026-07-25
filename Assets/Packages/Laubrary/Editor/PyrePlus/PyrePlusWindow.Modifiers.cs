// PyrePlusWindow.Modifiers — the Modifiers section (see PYREPLUS_DESIGN.md, SLICE 3). Reuses Pyre's own
// PyreModifier stack directly: the same enable / drag-reorder / "+ Add modifier" loop Pyre's own modifier UI
// draws (ZuiReorder grip, a GenericMenu add flow), but per-modifier bodies go through the toolkit's shared
// reflection drawer (ZuiReflect) rather than a hand-written per-type switch — so every Geometry/Pixel/Post
// modifier Pyre ships is editable here with ZERO per-type code, and PyrePlusRenderer applies them.
//
// Why the generic drawer instead of literally reusing Pyre's bodies: Pyre's PyreWindow.BuildModBody is a ~350-
// line switch, and several of its cases are wired into Pyre's preview-overlay authoring state (pin/vortex/stroke
// editing lives in PyreWindow.Preview.cs). Those aren't reusable across the assembly boundary (they're private,
// and PyrePlus has no such overlay), which is exactly why the design points at a shared drawer. The trade-off is
// that a modifier's animatable ZUIValue params render as their STATIC value only (not a full Static/MinMax/Curve
// control) — an accepted prototype limitation ("don't fight the shared drawer" in the design brief).
using System;
using System.Collections.Generic;
using System.Reflection;
using Laubrary.Pyre;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyrePlus.Editor
{
    public partial class PyrePlusWindow
    {
        // Stable section header + a body cleared/refilled on every add / remove / reorder / enable — the same
        // rebuild granularity BuildSwarm uses, so a structural change repaints just this section.
        VisualElement modifiersBody;

        void BuildModifiers(VisualElement root, PyrePlusSpec s)
        {
            var sec = Z.Section("Modifiers",
                "Opt-in effects, reusing Pyre's own modifier stack. Geometry modifiers bend each disc, pixel " +
                "modifiers recolour or drop lit pixels, and post passes (Bloom / Outline / Kaleidoscope) run over " +
                "the whole finished frame — all applied top-to-bottom in the order listed.");
            modifiersBody = new VisualElement();
            sec.Add(modifiersBody);
            root.Add(sec);
            RebuildModifiers();
        }

        void RebuildModifiers()
        {
            var s = SelLayer;   // the Modifiers section edits the SELECTED layer's own modifier stack
            if (s == null || modifiersBody == null) { modifiersBody?.Clear(); return; }
            s.modifiers ??= new List<PyreModifier>();
            modifiersBody.Clear();
            var list = s.modifiers;

            // A dedicated host for the rows so ZuiReorder's insertion line + index math only ever see modifier
            // blocks, never the "+ Add" button below (mirrors PyreWindow's own listHost split).
            var listHost = new VisualElement();
            modifiersBody.Add(listHost);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) { int at = i; Dirty(() => list.RemoveAt(at)); RebuildModifiers(); return; }
                listHost.Add(BuildModifierBlock(listHost, list, i));
            }

            modifiersBody.Add(WrapRow(
                Z.Button("+ Add modifier", "Add a geometry, pixel or post modifier to the stack.",
                    () => ShowAddModifierMenu(list))));

            // Simulation slot (slice 7): the layer's OWN stateful "always last" SimulationModifier — a SINGLE
            // polymorphic slot separate from the modifier list above (it retains frame-to-frame state and replays on
            // scrub, so it runs LAST within the layer, after every stateless modifier). Reuses the same reflection
            // drawer + Undo/dirty wiring as a modifier block. Rebuilt inside RebuildModifiers so it re-points on layer
            // selection with no extra wiring.
            modifiersBody.Add(BuildSimSlot(s));
        }

        // The layer's single stateful simulation slot. Null ⇒ an "+ Add simulation" affordance; otherwise an
        // enable/clear header plus the modifier's own reflected fields (drawn exactly like a modifier block).
        VisualElement BuildSimSlot(PyrePlusLayer s)
        {
            var box = Z.Box("Simulation (always last)",
                "A stateful simulation that runs LAST on this layer — after its modifiers, before any matte. It " +
                "keeps state frame-to-frame and replays deterministically on scrub. One per layer.");
            var sim = s.simulationModifier;
            if (sim == null)
            {
                box.Add(WrapRow(Z.Button("+ Add simulation",
                    "Attach a stateful simulation modifier (e.g. Pixel fluid) that advects and erodes this layer's " +
                    "own pixels over the frames.",
                    () => ShowAddSimMenu(s))));
                return box;
            }

            var header = new VisualElement();
            header.AddToClassList("zui-row");
            header.Add(Z.Toggle("", "Enable or disable this simulation (disabled = the layer renders without it).",
                sim.enabled, v =>
            {
                Dirty(() => sim.enabled = v);
                RebuildModifiers();   // rebuild so the body appears / disappears
            }));
            header.Add(Z.Text(sim.DisplayName, ZuiText.Body, sim.DisplayName + " simulation."));
            header.Add(Z.Flexible());
            header.Add(Z.Button("X", "Remove this simulation from the layer (undoable).", () =>
            {
                Dirty(() => s.simulationModifier = null);
                RebuildModifiers();
            }).W(22f));
            box.Add(header);

            // Body: every editable field of the sim modifier, drawn generically by the shared reflection drawer —
            // the SAME Undo/dirty/rebuild contract a modifier block uses (its ZUIValue params surface as their static
            // value, the accepted prototype limitation). Only when enabled.
            if (sim.enabled)
                ZuiReflect.BuildFields(box, sim, ModifierDrawerOptions(sim));

            return box;
        }

        void ShowAddSimMenu(PyrePlusLayer s)
        {
            var menu = new GenericMenu();
            foreach (var e in AddableSims())
            {
                var type = e.type;
                menu.AddItem(new GUIContent(e.label), false, () =>
                {
                    Dirty(() => s.simulationModifier = (SimulationModifier)Activator.CreateInstance(type));
                    RebuildModifiers();
                });
            }
            menu.ShowAsContext();
        }

        // Every concrete SimulationModifier PyrePlus can drive (a parameterless-constructible SimulationModifier
        // subclass — today only PixelFluidModifier), discovered by reflection so the slot tracks Pyre's set with zero
        // hand-maintained catalog. Cached — the scan runs once per domain (mirrors AddableModifiers).
        static List<AddEntry> _addableSims;
        static IEnumerable<AddEntry> AddableSims()
        {
            if (_addableSims != null) return _addableSims;
            var found = new List<AddEntry>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }   // skip dynamic / partially-loaded assemblies
                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(SimulationModifier).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    string label;
                    try { label = ((PyreModifier)Activator.CreateInstance(t)).DisplayName; }
                    catch { label = null; }
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new AddEntry { type = t, group = "Simulation", label = label });
                }
            }
            found.Sort((a, b) => string.CompareOrdinal(a.label, b.label));
            _addableSims = found;
            return _addableSims;
        }

        VisualElement BuildModifierBlock(VisualElement listHost, List<PyreModifier> list, int index)
        {
            var m = list[index];
            var box = Z.Box(null, null);

            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder — a modifier's position IS its apply order.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
            {
                Dirty(() =>
                {
                    var mm = list[from];
                    list.RemoveAt(from);
                    list.Insert(to, mm);
                });
                RebuildModifiers();
            });
            header.Add(grip);

            header.Add(Z.Toggle("", "Enable or disable this modifier.", m.enabled, v =>
            {
                Dirty(() => m.enabled = v);
                RebuildModifiers();   // rebuild so the body appears / disappears
            }));
            header.Add(Z.Text(m.DisplayName, ZuiText.Body, m.DisplayName + " modifier."));
            header.Add(Z.Flexible());
            header.Add(Z.Button("X", "Remove this modifier (undoable).", () =>
            {
                int at = list.IndexOf(m);
                if (at >= 0) { Dirty(() => list.RemoveAt(at)); RebuildModifiers(); }
            }).W(22f));
            box.Add(header);

            // Body: every editable field of THIS modifier, drawn generically by the toolkit's reflection drawer.
            // Only when enabled (a disabled modifier is header-only, matching Pyre's own list).
            if (m.enabled)
                ZuiReflect.BuildFields(box, m, ModifierDrawerOptions(m));

            return box;
        }

        // The reflection drawer's Undo / dirty / rebuild contract for a modifier's fields — the same wiring
        // Val/Val2D give the Shape/Swarm controls (record the asset before a mutation, mark it dirty + repaint
        // after), plus a rebuild when a nested list gains/loses an element.
        ZuiReflect.Options ModifierDrawerOptions(PyreModifier m) => new ZuiReflect.Options
        {
            OnBeforeChange = () => { if (spec != null) Undo.RecordObject(spec, "Edit Pyre Plus modifier"); },
            OnChanged = () => { if (spec != null) EditorUtility.SetDirty(spec); MarkDirty(); },
            OnStructureChanged = RebuildModifiers,
            // The enable toggle lives in the header row, so hide the base `enabled` field the drawer would
            // otherwise surface.
            Skip = f => f.Name == "enabled",
            // Surface a ZUIValue-style param as its plain static value (keeps the drawer type-agnostic). This is
            // the sole place a modifier's animatable params render — as a static float, not a full
            // Static/MinMax/Curve control (the accepted prototype limitation noted in the file header).
            FloatWrapperProperty = StaticValueProp,
            TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a {m.DisplayName} parameter.",
        };

        // The public `float staticValue { get; set; }` of a ZUIValue-like wrapper, or null. Mirrors Rulesets'
        // RuleParams.StaticValueProp (the reference FloatWrapperProperty wiring) without depending on Rulesets.
        static PropertyInfo StaticValueProp(Type t)
        {
            if (t == null || t.IsPrimitive || t == typeof(string) || t.IsEnum) return null;
            var p = t.GetProperty("staticValue", BindingFlags.Public | BindingFlags.Instance);
            return (p != null && p.PropertyType == typeof(float) && p.CanRead && p.CanWrite) ? p : null;
        }

        void ShowAddModifierMenu(List<PyreModifier> list)
        {
            var menu = new GenericMenu();
            foreach (var e in AddableModifiers())
            {
                var type = e.type;
                menu.AddItem(new GUIContent($"{e.group}/{e.label}"), false, () =>
                {
                    Dirty(() => list.Add((PyreModifier)Activator.CreateInstance(type)));
                    RebuildModifiers();
                });
            }
            menu.ShowAsContext();
        }

        // Every concrete PyreModifier PyrePlus can actually apply — a Geometry, Pixel or Post modifier with a
        // parameterless constructor — discovered by reflection, so the add-menu tracks Pyre's modifier set with
        // zero hand-maintained catalog (Pyre's own menu hand-lists them; there is no shared registry to call).
        // EdgeModifier and SimulationModifier subclasses are deliberately EXCLUDED: PyrePlus's disc raster has no
        // silhouette-edge stage and no iterative-simulation/replay stage, so offering them would add pure no-ops.
        // Cached — the scan runs once per domain.
        struct AddEntry { public Type type; public string group, label; }
        static List<AddEntry> _addable;
        static IEnumerable<AddEntry> AddableModifiers()
        {
            if (_addable != null) return _addable;
            var found = new List<AddEntry>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }   // skip dynamic / partially-loaded assemblies
                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(PyreModifier).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    string group;
                    if (typeof(GeometryModifier).IsAssignableFrom(t)) group = "Geometry";
                    else if (typeof(PixelModifier).IsAssignableFrom(t)) group = "Pixel";
                    else if (typeof(PostModifier).IsAssignableFrom(t)) group = "Post";
                    else continue;   // Edge / Simulation / anything else PyrePlus cannot apply
                    string label;
                    try { label = ((PyreModifier)Activator.CreateInstance(t)).DisplayName; }
                    catch { label = null; }
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new AddEntry { type = t, group = group, label = label });
                }
            }
            found.Sort((a, b) =>
            {
                int g = string.CompareOrdinal(a.group, b.group);
                return g != 0 ? g : string.CompareOrdinal(a.label, b.label);
            });
            _addable = found;
            return _addable;
        }
    }
}
