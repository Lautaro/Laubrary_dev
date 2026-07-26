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
using Laubrary.SpriteFx;
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
        // The section itself (persists across body refills) — held so its collapsed-count header suffix can be
        // refreshed when the SELECTED layer (whose enabled-modifier count it shows) changes under a folded section.
        ZuiSection modifiersSection;

        void BuildModifiers(VisualElement root, PyrePlusSpec s)
        {
            var sec = Z.Section("Modifiers",
                "Opt-in effects, reusing Pyre's own modifier stack. Geometry modifiers bend each disc, pixel " +
                "modifiers recolour or drop lit pixels, and post passes (Bloom / Outline / Kaleidoscope) run over " +
                "the whole finished frame — all applied top-to-bottom in the order listed.",
                icon: "sliders-horizontal");
            modifiersSection = sec;
            // Folded, this section hides its modifier stack. Surface the count of ENABLED modifiers (task #63) so a
            // collapsed "Modifiers (2)" tells you two active effects are hidden below.
            sec.SetHeaderSuffix(EnabledModifierSuffix);
            modifiersBody = new VisualElement();
            sec.Add(modifiersBody);
            root.Add(sec);
            RebuildModifiers();
        }

        // The " (N)" suffix the COLLAPSED Modifiers header shows — N = the SELECTED layer's ENABLED modifier count
        // (0 ⇒ "", no suffix). Reads live so it stays correct across layer selection and enable/disable.
        string EnabledModifierSuffix()
        {
            var s = SelLayer;
            int n = 0;
            if (s?.modifiers != null)
                foreach (var m in s.modifiers) if (m != null && m.enabled) n++;
            return n > 0 ? $" ({n})" : "";
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
                listHost.Add(BuildModifierBlock(listHost, list, i, RebuildModifiers));
            }

            Button addModBtn = null;
            addModBtn = Z.Button("+ Add modifier", "Add a geometry, pixel or post modifier to the stack.",
                () => ShowAddModifierMenu(addModBtn, list, RebuildModifiers));
            modifiersBody.Add(WrapRow(addModBtn));

            // Simulation slot (slice 7): the layer's OWN stateful "always last" SimulationModifier — a SINGLE
            // polymorphic slot separate from the modifier list above (it retains frame-to-frame state and replays on
            // scrub, so it runs LAST within the layer, after every stateless modifier). Reuses the same reflection
            // drawer + Undo/dirty wiring as a modifier block. Rebuilt inside RebuildModifiers so it re-points on layer
            // selection with no extra wiring.
            modifiersBody.Add(BuildSimSlot(s));

            // Keep the collapsed-header count right when the section stays folded across a layer switch / an
            // enable-toggle rebuild (the section instance persists; only its body is refilled here).
            modifiersSection?.RefreshHeaderSuffix();
        }

        // ── spec-wide Global Modifiers (task #56) ────────────────────────────────────────────────────────────────
        // The SAME modifier-stack UI as the per-layer Modifiers section above (drag-reorder grip, enable, fold, the
        // "+ Add modifier" reflection catalog, ZuiReflect bodies, Undo-safe), but editing the SPEC's globalModifiers
        // list — which the renderer applies to EVERY layer (each layer's own stack, wrapped by these in Pyre1's
        // order). Placed right after the Layers section (see PyrePlusWindow.BuildAsset). Not selection-bound (spec-
        // wide), so it is NOT re-pointed by RebuildAllForSelection — BuildAsset rebuilds it fresh on an asset change.
        // No simulation slot here: the blast-wide SimulationModifier is a deferred follow-up (see PyrePlusRenderer).
        VisualElement globalModifiersBody;
        ZuiSection globalModifiersSection;   // persists across body refills — held so its collapsed count can refresh

        void BuildGlobalModifiers(VisualElement root, PyrePlusSpec s)
        {
            var sec = Z.Section("Global Modifiers",
                "Spec-wide modifiers applied to EVERY layer, on top of each layer's own stack (ported 1:1 from " +
                "Pyre). Geometry warps wrap OUTERMOST — a global Rotate spins the whole animation as one — pixel " +
                "effects run after each layer's own, and post passes run over the whole finished frame. Empty = no " +
                "change; each layer renders exactly as its own Modifiers section dictates.",
                icon: "globe");
            globalModifiersSection = sec;
            // Folded, this section hides the spec-wide stack — surface the count of ENABLED global modifiers (#63).
            sec.SetHeaderSuffix(EnabledGlobalModifierSuffix);
            globalModifiersBody = new VisualElement();
            sec.Add(globalModifiersBody);
            root.Add(sec);
            RebuildGlobalModifiers();
        }

        // The " (N)" suffix the COLLAPSED Global Modifiers header shows — N = enabled spec-wide modifier count.
        string EnabledGlobalModifierSuffix()
        {
            int n = 0;
            if (spec?.globalModifiers != null)
                foreach (var m in spec.globalModifiers) if (m != null && m.enabled) n++;
            return n > 0 ? $" ({n})" : "";
        }

        void RebuildGlobalModifiers()
        {
            if (spec == null || globalModifiersBody == null) { globalModifiersBody?.Clear(); return; }
            spec.globalModifiers ??= new List<PyreModifier>();
            globalModifiersBody.Clear();
            var list = spec.globalModifiers;

            // A dedicated host for the rows so ZuiReorder's insertion line + index math only ever see modifier
            // blocks, never the "+ Add" button below (mirrors the per-layer stack + PyreWindow's listHost split).
            var listHost = new VisualElement();
            globalModifiersBody.Add(listHost);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) { int at = i; Dirty(() => list.RemoveAt(at)); RebuildGlobalModifiers(); return; }
                listHost.Add(BuildModifierBlock(listHost, list, i, RebuildGlobalModifiers));
            }

            Button addGlobalModBtn = null;
            addGlobalModBtn = Z.Button("+ Add modifier", "Add a geometry, pixel or post modifier applied to every layer.",
                () => ShowAddModifierMenu(addGlobalModBtn, list, RebuildGlobalModifiers));
            globalModifiersBody.Add(WrapRow(addGlobalModBtn));

            globalModifiersSection?.RefreshHeaderSuffix();   // keep the collapsed count right across body refills
        }

        // The layer's single stateful simulation slot. Null ⇒ an "+ Add simulation" affordance; otherwise an
        // enable/clear header plus the modifier's own reflected fields (drawn exactly like a modifier block).
        VisualElement BuildSimSlot(PyrePlusLayer s)
        {
            // BoxKeyed (not Z.Box): this box is captured by the saved-views bar like every other stable PyrePlus
            // box, so it needs an explicit stable key — an unkeyed box falls back to keying its view-state by
            // title+tooltip (ZuiBox.cs), which orphans saved views the moment the title/tooltip is reworded.
            var box = Z.BoxKeyed("Simulation (always last)",
                "A stateful simulation that runs LAST on this layer — after its modifiers, before any matte. It " +
                "keeps state frame-to-frame and replays deterministically on scrub. One per layer.",
                "pyreplus.sim");
            // Folded, the box hides an active simulation — mark it (task #63) so an enabled sim isn't invisible.
            box.SetHeaderSuffix(() => (s.simulationModifier != null && s.simulationModifier.enabled) ? " (on)" : "");
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
            var enableToggle = Z.Toggle("",
                "Enable or disable this simulation (disabled = the layer renders without it).",
                sim.enabled, v =>
            {
                Dirty(() => sim.enabled = v);
                RebuildModifiers();   // rebuild so the body appears / disappears
            });
            header.Add(enableToggle);
            header.Add(Z.Text(sim.DisplayName, ZuiText.Body, sim.DisplayName + " simulation."));
            header.Add(Z.Flexible());
            var removeBtn = Z.Button("X", "Remove this simulation from the layer (undoable).", () =>
            {
                Dirty(() => s.simulationModifier = null);
                RebuildModifiers();
            }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            // Body: every editable field of the sim modifier, drawn generically by the shared reflection drawer —
            // the SAME Undo/dirty/rebuild contract a modifier block uses (its ZUIValue params surface as their static
            // value, the accepted prototype limitation). In its own container so the sim card FOLDS to just its
            // header (task #52), keyed by the sim instance. Only when enabled (a disabled sim has nothing to fold).
            VisualElement body = null;
            if (sim.enabled)
            {
                body = new VisualElement();
                ZuiReflect.BuildFields(body, sim, ModifierDrawerOptions(sim, RebuildModifiers));
                box.Add(body);
            }
            ZuiFoldCard.Wire(sim, header, body, enableToggle, removeBtn);

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

        // Build one modifier card. `rebuild` is the section rebuild to run on a structural edit (reorder / enable /
        // remove / nested-list change) — RebuildModifiers for the per-layer stack, RebuildGlobalModifiers for the
        // spec-wide Global Modifiers stack. Everything else is list-agnostic, so both stacks share this verbatim.
        VisualElement BuildModifierBlock(VisualElement listHost, List<PyreModifier> list, int index, Action rebuild)
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
                rebuild();
            });
            header.Add(grip);

            var enableToggle = Z.Toggle("", "Enable or disable this modifier.", m.enabled, v =>
            {
                Dirty(() => m.enabled = v);
                rebuild();   // rebuild so the body appears / disappears
            });
            header.Add(enableToggle);
            header.Add(Z.Text(m.DisplayName, ZuiText.Body, m.DisplayName + " modifier."));
            header.Add(Z.Flexible());
            var removeBtn = Z.Button("X", "Remove this modifier (undoable).", () =>
            {
                int at = list.IndexOf(m);
                if (at >= 0) { Dirty(() => list.RemoveAt(at)); rebuild(); }
            }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            // Body: every editable field of THIS modifier, drawn generically by the toolkit's reflection drawer,
            // in its own container so the card can FOLD to just the header (task #52). Only when enabled (a
            // disabled modifier is header-only, matching Pyre's own list — and has nothing to fold).
            VisualElement body = null;
            if (m.enabled)
            {
                body = new VisualElement();
                ZuiReflect.BuildFields(body, m, ModifierDrawerOptions(m, rebuild));
                box.Add(body);
            }

            // Clicking the header folds the field body away, leaving the grip / enable / name / ✕ visible;
            // fold state is kept PER MODIFIER INSTANCE so it survives this window's rebuilds (undo / reorder /
            // layer selection). The grip already guards its own drag; the toggle / ✕ must not fold on click.
            ZuiFoldCard.Wire(m, header, body, enableToggle, removeBtn);

            return box;
        }

        // The reflection drawer's Undo / dirty / rebuild contract for a modifier's fields — the same wiring
        // Val/Val2D give the Shape/Swarm controls (record the asset before a mutation, mark it dirty + repaint
        // after), plus a rebuild when a nested list gains/loses an element.
        ZuiReflect.Options ModifierDrawerOptions(PyreModifier m, Action rebuild) => new ZuiReflect.Options
        {
            OnBeforeChange = () => { if (spec != null) Undo.RecordObject(spec, "Edit Pyre Plus modifier"); },
            OnChanged = () => { if (spec != null) EditorUtility.SetDirty(spec); MarkDirty(); },
            OnStructureChanged = rebuild,
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

        // The add-modifier catalog, as a ZUI menu anchored to the "+ Add modifier" button (task #68 — the
        // GenericMenu stand-in). AddableModifiers() is sorted group-then-label, so the Geometry/Pixel/Post
        // groups that GenericMenu drew as slash-nested submenus become flat, always-visible SECTION headings
        // here — one fewer click and the whole catalog scannable at a glance. Behaviour is otherwise
        // identical: each row adds one modifier (undoable) and rebuilds the stack; the menu dismisses on the
        // pick, an outside click, or Esc.
        void ShowAddModifierMenu(VisualElement anchor, List<PyreModifier> list, Action rebuild)
        {
            var menu = Z.Menu(anchor);
            string lastGroup = null;
            foreach (var e in AddableModifiers())
            {
                if (e.group != lastGroup) { menu.Section(e.group); lastGroup = e.group; }
                var type = e.type;
                string label = e.label, group = e.group;
                menu.Item(label, $"Add the {label} {group.ToLowerInvariant()} modifier to the stack.", () =>
                {
                    Dirty(() => list.Add((PyreModifier)Activator.CreateInstance(type)));
                    rebuild();
                });
            }
            menu.Show();
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
