// ChunkWindow.Layers.cs — the Layer Stack panel (AgentHQ T-0037).
//
// This is the authoring UI for `ChunkSpec.layers` (a Laubrary.Layering.LayerSpec). It does three things and
// nothing else:
//   1. the STACK — an ordered list of named slots, each showing the concrete sortingOrder it resolves to,
//      renameable, removable, drag-reorderable;
//   2. WHERE the stack lives — the one Unity Sorting Layer, plus baseOrder / step;
//   3. WHO sits in which slot — one row per ENABLED Chunks 2.0 module, each picking a declared slot. That
//      includes every entry of ChunkSpec.blastGroups: the extra blast groups all fire at the same instant, so
//      their slot is the ONLY thing deciding which of them draws behind the fragments and which in front.
//
// Job 3 lives HERE rather than inside each module's own panel on purpose: the whole point of a stack is that
// the composed draw order is readable in ONE place. Reading six separate panels to reconstruct "what is in
// front of what" is exactly the problem LayerSpec was written to kill.
using System.Collections.Generic;
using Laubrary.Layering;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        // The design doc's worked example, offered as one-click items in the "+ Add slot…" catalog. Not a
        // hard-coded schema — a stack can be any names in any order; these are just the ones the doc names.
        static readonly string[] LsStandardSlots =
            { "Blast", "Fragment1", "Smoke", "Fragment2", "Fireball", "Fragment3" };

        const float LsGrip = 16f;
        const float LsNameW = 130f;
        const float LsOrderW = 46f;
        // Wide enough for the longest label this box draws, which is a blast group's own DisplayName (its
        // authored label, or its blast asset's name — T-0081 D-03: no ordinal prefix any more) and not the
        // four fixed module names. A column sized for "Pyre Spawn" would ellipsize every group down to nothing.
        const float LsModuleW = 152f;
        const int LsGroupNameChars = 14;   // hand-truncation budget for a group's name inside that column
        const float LsAddW = 118f;

        // Hosts held so a structural edit refills only its OWN list instead of rebuilding the whole window.
        // That is the "stable workspace" rule made concrete: renaming or re-slotting a module must not move
        // the slot rows the user may be mid-drag on, and a full Rebuild() would also drop keyboard focus out
        // of the name field on every keystroke-adjacent edit.
        VisualElement _lsSortHost;
        VisualElement _lsSlotHost;      // ONLY slot rows may live here — ZuiReorder indexes container children
        VisualElement _lsAssignHost;
        readonly List<Label> _lsOrderLabels = new List<Label>();
        int _lsFocusSlot = -1;          // slot index whose name field should take focus after the next refill

        // ── the section ─────────────────────────────────────────────────────────────────────────────────
        void BuildLayerStack(VisualElement root, ChunkSpec c)
        {
            if (c == null || c.layers == null) return;
            c.layers.layers ??= new List<string>();   // null-repair only; not an authored change, so no Undo

            // NO header toggle here, unlike every module section. An EMPTY stack already IS "layering off" —
            // ChunkModuleContext.OrderFor falls back to the emitter's flat sortingOrder the moment a slot
            // isn't declared — so an on/off toggle would be a second, contradictory way to say the same
            // thing, and the two could disagree (stack full, toggle off: what draws where?).
            var sec = Z.Section("Layer Stack",
                "Which piece of this composed burst draws in front of which. Each named slot resolves to a "
                + "concrete sortingOrder — index 0 furthest back, each following slot one step in front — and "
                + "every module picks the slot it draws in. Leave the stack empty for no layering at all: "
                + "every module then falls back to the emitter's own flat sorting order.",
                "chunks.layers", icon: "stack");

            _lsSortHost = new VisualElement();
            sec.Add(_lsSortHost);
            RebuildLayerSorting(c);

            sec.Add(Z.Row(
                Z.Field("Base Order", LsBaseTip(c),
                    Z.Int(c.layers.baseOrder, LsBaseTip(c),
                        v => { Dial("Base order", () => c.layers.baseOrder = v); RefreshLayerOrders(c); }, Num)),
                Z.HSpace(),
                Z.Field("Step", LsStepTip(c),
                    Z.Int(c.layers.step, LsStepTip(c),
                        v => { Dial("Layer step", () => c.layers.step = v); RefreshLayerOrders(c); }, Num))));

            // Keyed, like every titled box in the window: an unkeyed one keys its saved view state by
            // title+tooltip, so rewording either would orphan the views already captured against it.
            var slots = Z.BoxKeyed("Slots",
                "The ordered draw slots. Index 0 draws furthest back; the number on each row is the concrete "
                + "sortingOrder that slot resolves to right now. Drag a row's grip to reorder it.",
                "chunks.layers.slots");
            _lsSlotHost = new VisualElement();
            slots.Add(_lsSlotHost);
            // The add button sits OUTSIDE _lsSlotHost: ZuiReorder's insertion line and (from, to) maths count
            // the container's direct children, so anything in there that is not a slot row corrupts the index.
            Button addBtn = null;
            addBtn = Z.Button("+ Add slot…",
                "Open the slot catalog — this recipe's own module slots, the standard six, or a custom one.",
                () => ShowAddLayerSlotMenu(addBtn, c)).W(LsAddW);
            slots.Add(Z.Row(addBtn));
            sec.Add(slots);
            RebuildLayerSlots(c);

            var assign = Z.BoxKeyed("Modules",
                "Which slot each enabled module — and each extra blast group — draws in. These are REFERENCES "
                + "to a slot declared above, so they are picked, never typed; a module pointing at a slot that "
                + "no longer exists silently degrades to the flat base order at runtime. Two modules sharing "
                + "one slot draw at the same depth: give one of them its own slot to separate them.",
                "chunks.layers.modules");
            _lsAssignHost = new VisualElement();
            assign.Add(_lsAssignHost);
            sec.Add(assign);
            RebuildLayerAssignments(c);

            root.Add(sec);
        }

        string LsBaseTip(ChunkSpec c)
            => "sortingOrder of slot 0 — and the value an unknown slot name degrades to. "
             + $"Slot 0 currently resolves to {c.layers.OrderAt(0)}.";

        string LsStepTip(ChunkSpec c)
            => "sortingOrder distance between consecutive slots. The gap is deliberate: it leaves room for a "
             + "module to sub-order several renderers inside one slot. "
             + $"Currently {c.layers.step} between slots.";

        // ── job 2a: the Unity Sorting Layer, PICKED not typed ───────────────────────────────────────────
        // "Never type a reference string" applies with full force: a typo here is invisible (LayerSpec.Apply
        // refuses to assign an unknown layer, so the renderer just silently keeps whatever it had) and it
        // degrades every piece of the effect at once. The option set is a DYNAMIC string list, not an enum,
        // so the enum→radios rule doesn't strictly govern it — but a project's Sorting Layers are a short,
        // stable set, which is exactly what Z.MiniRadio is for. wrap:true keeps a project with many layers
        // from running off the side of the pane.
        void RebuildLayerSorting(ChunkSpec c)
        {
            if (_lsSortHost == null) return;
            _lsSortHost.Clear();

            var values = new List<string> { string.Empty };
            var labels = new List<string> { "(leave alone)" };
            foreach (var l in SortingLayer.layers) { values.Add(l.name); labels.Add(l.name); }

            string cur = c.layers.sortingLayerName ?? string.Empty;
            int sel = values.IndexOf(cur);
            if (sel < 0)
            {
                // A layer that was renamed or deleted in Project Settings since this asset was authored.
                // Show it as the selected-but-broken option rather than quietly snapping to something else —
                // the user must be able to SEE what the asset actually stores before they replace it.
                values.Add(cur);
                labels.Add(cur + " (missing)");
                sel = values.Count - 1;
            }

            string tip = "The ONE Unity Sorting Layer this whole composed effect lives in; the slots below only "
                       + "order renderers WITHIN it. \"(leave alone)\" means don't touch the renderer's sorting "
                       + "layer at all."
                       + (LayerSpec.IsValidSortingLayer(cur) || cur.Length == 0
                            ? ""
                            : $" \"{cur}\" is not a Sorting Layer in Project Settings, so it is ignored at runtime.");

            _lsSortHost.Add(Z.Field("Sorting Layer", tip,
                Z.MiniRadio(sel, labels.ToArray(), tip, i =>
                {
                    string picked = values[i];
                    Dial("Sorting layer", () => c.layers.sortingLayerName = picked);
                    RebuildLayerSorting(c);   // drops the "(missing)" option once a real layer is chosen
                }, wrap: true)));
        }

        // ── job 1: the stack itself ─────────────────────────────────────────────────────────────────────
        void RebuildLayerSlots(ChunkSpec c)
        {
            if (_lsSlotHost == null) return;
            _lsSlotHost.Clear();
            _lsOrderLabels.Clear();
            var names = c.layers.layers;
            for (int i = 0; i < names.Count; i++)
                _lsSlotHost.Add(BuildLayerSlotRow(c, i));
        }

        // One slot = one row. Card-layout rule: a card whose whole content is a name plus one readout IS a
        // single row — grip, name, the universal per-card readout, a flexible gap, then the remove ×, and the
        // row never wraps (a flexible gap in a wrapping row throws the × onto a line of its own).
        VisualElement BuildLayerSlotRow(ChunkSpec c, int index)
        {
            var names = c.layers.layers;
            string current = names[index] ?? string.Empty;

            var row = Z.Row();
            row.style.flexWrap = Wrap.NoWrap;
            row.style.alignItems = Align.Center;

            // Drag-reorder uses ZuiReorder.MakeGrip, NOT ZuiThumbGrid. The design doc names ZuiThumbGrid, but
            // that is the THUMBNAIL reorder shared by Pyre's CherryFraming and the Laumination Builder — it
            // reorders a grid of pictures. A layer slot has no thumbnail (it is a name and a number), so the
            // generic row-grip helper is the right primitive; same insertion line, same (from, to) contract.
            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this slot. The topmost slot draws furthest back.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = LsGrip;
            grip.style.flexShrink = 0f;
            ZuiReorder.MakeGrip(grip, row, _lsSlotHost, (from, to) =>
            {
                Dial("Reorder layer slot", () => c.layers.Move(from, to));
                RebuildLayerSlots(c);        // names moved, so every resolved order below changed
                RebuildLayerAssignments(c);  // and each module's picker must re-latch on its new position
            });
            row.Add(grip);

            // The ONE place a slot's name is DECLARED — so a text field is correct here, and only here.
            // isDelayed: commit on Enter/blur, not per keystroke, so a rename is one Undo step and the
            // uniquing/cascade below runs against a finished name rather than "B", "Bl", "Bla"…
            var nameField = Z.TextInput(current,
                "This slot's name — rename it here. Every module pointing at the old name follows the rename "
                + "automatically, and a name already used by another slot is made unique.",
                v => RenameLayerSlot(c, index, v), LsNameW);
            nameField.isDelayed = true;
            row.Add(nameField);
            if (_lsFocusSlot == index)
            {
                _lsFocusSlot = -1;
                nameField.schedule.Execute(() => nameField.Focus()).ExecuteLater(1);
            }

            // The resolved concrete sortingOrder. This is the whole point of authoring a stack — the order is
            // a number the user can SEE, not one buried in the resolver — so it is never hidden behind a fold.
            var order = Z.Text(c.layers.OrderAt(index).ToString(), ZuiText.Body, LsOrderTip(c, index));
            order.style.width = LsOrderW;
            order.style.flexShrink = 0f;
            order.style.unityTextAlign = TextAnchor.MiddleRight;
            _lsOrderLabels.Add(order);
            row.Add(order);

            row.Add(Z.Flexible());
            row.Add(Z.Button("×", $"Remove the \"{current}\" slot from the stack. Any module still pointing at "
                                + "it will read as unresolved below (undoable).",
                () =>
                {
                    Dial("Remove layer slot", () => c.layers.layers.RemoveAt(index));
                    RebuildLayerSlots(c);
                    RebuildLayerAssignments(c);
                }).W(22f));
            return row;
        }

        string LsOrderTip(ChunkSpec c, int index)
            => $"Draws at sortingOrder {c.layers.OrderAt(index)} — base {c.layers.baseOrder} + index {index} "
             + $"× step {c.layers.step}. Lower draws further back.";

        // Base/step edits change every resolved number at once. Refresh the readouts IN PLACE rather than
        // refilling the list: a refill on each keystroke would tear the field the user is typing in out of
        // the tree, and moving rows under a live edit is exactly what the stable-workspace rule forbids.
        void RefreshLayerOrders(ChunkSpec c)
        {
            for (int i = 0; i < _lsOrderLabels.Count; i++)
            {
                _lsOrderLabels[i].text = c.layers.OrderAt(i).ToString();
                _lsOrderLabels[i].tooltip = LsOrderTip(c, i);
            }
        }

        void RenameLayerSlot(ChunkSpec c, int index, string typed)
        {
            var names = c.layers.layers;
            if (index < 0 || index >= names.Count) return;
            string prev = names[index] ?? string.Empty;

            // UniqueName scans the WHOLE list, this slot included, so "rename to what it already is" would
            // otherwise come back as "Blast 2". Blank the slot first so it can't collide with itself.
            names[index] = null;
            string unique = c.layers.UniqueName(typed);
            names[index] = prev;   // restore before the Dial, so Undo records the real previous state
            if (unique == prev) { RebuildLayerSlots(c); return; }

            Dial("Rename layer slot", () =>
            {
                names[index] = unique;
                // Cascade to every reference. Without this a rename silently orphans each module pointing at
                // the old name, and an orphaned module degrades to the flat base order at RUNTIME — far from
                // where the rename happened, which is precisely the failure the pick-don't-type rule exists
                // to prevent.
                RepointModuleLayer(c, prev, unique);
            });
            RebuildLayerSlots(c);
            RebuildLayerAssignments(c);
        }

        static void RepointModuleLayer(ChunkSpec c, string from, string to)
        {
            if (string.IsNullOrEmpty(from)) return;
            if (c.particleSplash != null && c.particleSplash.layerName == from) c.particleSplash.layerName = to;
            if (c.pyreSpawn != null && c.pyreSpawn.layerName == from) c.pyreSpawn.layerName = to;
            if (c.fragmentSlicer != null && c.fragmentSlicer.layerName == from) c.fragmentSlicer.layerName = to;
            if (c.spawnFormation != null && c.spawnFormation.layerName == from) c.spawnFormation.layerName = to;
            // Every further blast group follows the rename too. Missing these would be the same silent orphan
            // the cascade exists to prevent, only worse: a blast group has NO other reason to exist than the
            // slot it sits in, so an orphaned one collapses straight back onto the flat order it was created
            // to escape.
            if (c.blastGroups != null)
                for (int i = 0; i < c.blastGroups.Count; i++)
                {
                    var g = c.blastGroups[i];
                    if (g != null && g.layerName == from) g.layerName = to;
                }
        }

        void AddLayerSlot(ChunkSpec c, string desired, bool focusIt)
        {
            string unique = c.layers.UniqueName(desired);
            Dial("Add layer slot", () => c.layers.layers.Add(unique));
            if (focusIt) _lsFocusSlot = c.layers.layers.Count - 1;
            RebuildLayerSlots(c);
            RebuildLayerAssignments(c);
        }

        // The add catalog, as the house Envelope-style popover card (Z.Menu → Z.Popover), the same shape
        // PyreWindow.Modifiers.cs's "+ Add modifier" uses. An EMPTY stack is the first screen every user
        // gets, so it leads with a one-click way to a working stack rather than an empty box.
        void ShowAddLayerSlotMenu(VisualElement anchor, ChunkSpec c)
        {
            var menu = Z.Menu(anchor);
            var names = c.layers.layers;

            if (names.Count == 0)
            {
                menu.Section("Start from");
                menu.Item("The standard stack (6 slots)",
                    "Add Blast, Fragment1, Smoke, Fragment2, Fireball and Fragment3 in that order (undoable).",
                    () =>
                    {
                        Dial("Add standard layer stack", () =>
                        {
                            foreach (var n in LsStandardSlots) c.layers.layers.Add(c.layers.UniqueName(n));
                        });
                        RebuildLayerSlots(c);
                        RebuildLayerAssignments(c);
                    });
                menu.Separator();
            }

            // This recipe's own unresolved module slots first — one click here makes a module that is
            // currently degrading to the flat order actually resolve, which is the most common reason to be
            // in this menu at all.
            bool wroteWanted = false;
            foreach (var want in WantedModuleSlots(c))
            {
                if (c.layers.Has(want)) continue;
                if (!wroteWanted) { menu.Section("Wanted by this recipe"); wroteWanted = true; }
                string w = want;
                menu.Item(w, $"Add a \"{w}\" slot so the module already pointing at it resolves (undoable).",
                    () => AddLayerSlot(c, w, false));
            }
            if (wroteWanted) menu.Separator();

            menu.Section("Standard slots");
            foreach (var n in LsStandardSlots)
            {
                string s = n;
                bool already = c.layers.Has(s);
                menu.Item(s, already ? $"\"{s}\" is already a slot in this stack."
                                     : $"Add a \"{s}\" slot to the end of the stack (undoable).",
                    () => AddLayerSlot(c, s, false), @checked: already, enabled: !already);
            }

            menu.Separator();
            // "…" is honest here: activating this lands the user in the new slot's name field to type into,
            // rather than performing a self-contained named action.
            menu.Item("Custom…", "Add a blank slot and start typing its name.",
                () => AddLayerSlot(c, "Layer", true));

            menu.Show();
        }

        static IEnumerable<string> WantedModuleSlots(ChunkSpec c)
        {
            if (c.particleSplash != null && c.particleSplash.Enabled
                && !string.IsNullOrEmpty(c.particleSplash.layerName)) yield return c.particleSplash.layerName;
            if (c.fragmentSlicer != null && c.fragmentSlicer.Enabled
                && !string.IsNullOrEmpty(c.fragmentSlicer.layerName)) yield return c.fragmentSlicer.layerName;
            if (c.pyreSpawn != null && c.pyreSpawn.Enabled
                && !string.IsNullOrEmpty(c.pyreSpawn.layerName)) yield return c.pyreSpawn.layerName;
            if (c.spawnFormation != null && c.spawnFormation.Enabled
                && !string.IsNullOrEmpty(c.spawnFormation.layerName)) yield return c.spawnFormation.layerName;
            if (c.blastGroups != null)
                for (int i = 0; i < c.blastGroups.Count; i++)
                {
                    var g = c.blastGroups[i];
                    if (g != null && g.Enabled && !string.IsNullOrEmpty(g.layerName)) yield return g.layerName;
                }
        }

        /// How many ENABLED modules currently point at one slot. Only the "New slot…" tooltip uses it, and
        /// only so that it can say something true: sharing is the reason to want a new slot, but it is not
        /// always what is happening.
        static int LsSlotUsers(ChunkSpec c, string slot)
        {
            if (string.IsNullOrEmpty(slot)) return 0;
            int n = 0;
            foreach (var want in WantedModuleSlots(c))
                if (want == slot) n++;
            return n;
        }

        // ── job 3: which module sits in which slot ──────────────────────────────────────────────────────
        void RebuildLayerAssignments(ChunkSpec c)
        {
            if (_lsAssignHost == null) return;
            _lsAssignHost.Clear();
            bool any = false;

            if (c.particleSplash != null && c.particleSplash.Enabled)
            {
                any = true;
                _lsAssignHost.Add(BuildLayerAssignRow(c, "Splash",
                    "Which slot the particle splash's particles draw in.",
                    c.particleSplash.layerName, v => c.particleSplash.layerName = v));
            }
            if (c.fragmentSlicer != null && c.fragmentSlicer.Enabled)
            {
                any = true;
                _lsAssignHost.Add(BuildLayerAssignRow(c, "Fragments",
                    "Which slot the sliced big fragments draw in.",
                    c.fragmentSlicer.layerName, v => c.fragmentSlicer.layerName = v));
            }
            if (c.pyreSpawn != null && c.pyreSpawn.Enabled)
            {
                any = true;
                _lsAssignHost.Add(BuildLayerAssignRow(c, "Pyre Spawn",
                    "Which slot the spawned Pyre blast draws in.",
                    c.pyreSpawn.layerName, v => c.pyreSpawn.layerName = v));
            }
            if (c.spawnFormation != null && c.spawnFormation.Enabled)
            {
                any = true;
                _lsAssignHost.Add(BuildLayerAssignRow(c, "Formation",
                    "Which slot every blast the formation spawns draws in.",
                    c.spawnFormation.layerName, v => c.spawnFormation.layerName = v));
            }

            // Every FURTHER blast group. This is the row the "one blast BEHIND the fractured character, several
            // more between and in front of the pieces" recipe is actually authored in — the groups all fire at
            // once, so their slot is the ONLY thing that decides depth. A group missing from this box would
            // mean the feature does not exist, however complete its own panel looks.
            //
            // Labelled by the group's own DisplayName (its authored label, or its blast asset's name) — never
            // an ordinal. A "Blast N" position number goes stale the instant a group is reordered or another is
            // removed, and it never matched what the group's own section header calls it anyway, since there is
            // no "Blast 1" in the product at all (T-0081 D-03; mirrors how a Pyre modifier card is labelled by
            // its own DisplayName, never by its position in the stack — PyreWindow.Modifiers.cs:295). NOTE: two
            // groups CAN legitimately hold the same blast at two different depths and would then show the same
            // label here — each row's own tooltip (the resolved sortingOrder) is what tells them apart in that
            // case, same as it already does whenever a name alone is ambiguous.
            if (c.blastGroups != null)
            {
                for (int i = 0; i < c.blastGroups.Count; i++)
                {
                    var group = c.blastGroups[i];   // a local, not the loop variable: the setter below closes over it
                    if (group == null || !group.Enabled) continue;
                    any = true;
                    string name = group.DisplayName;
                    _lsAssignHost.Add(BuildLayerAssignRow(c,
                        LsEllipsize(name, LsGroupNameChars),
                        $"Which slot \"{name}\" draws in. Every blast group fires alongside the others, so "
                        + "this slot is the only thing putting it behind or in front of the fragments.",
                        group.layerName, v => group.layerName = v, name));
                }
            }

            if (!any)
                _lsAssignHost.Add(Z.Text("No modules enabled.", ZuiText.Subtle,
                    "Switch a module on in its own section above and it appears here to be given a slot."));
        }

        /// `newSlotName` is what a slot created FROM this row is named — the module's own idea of itself. For a
        /// blast group this is its DisplayName passed in raw by the caller, distinct from `moduleLabel` (which
        /// may be hand-truncated for the column) so a long name isn't chopped before it becomes a slot name.
        /// Defaults to `moduleLabel` for the four fixed modules, where the two already match.
        VisualElement BuildLayerAssignRow(ChunkSpec c, string moduleLabel, string tooltip,
                                          string stored, System.Action<string> set, string newSlotName = null)
        {
            var names = c.layers.layers;
            string cur = stored ?? string.Empty;
            int sel = c.layers.IndexOf(cur);   // -1 = unresolved: not one of the declared slots

            var row = Z.Row();
            row.style.flexWrap = Wrap.NoWrap;
            row.style.alignItems = Align.Center;

            string resolvedTip = sel >= 0
                ? tooltip + $" Currently \"{cur}\", drawing at sortingOrder {c.layers.OrderAt(sel)}."
                : tooltip + (cur.Length == 0
                    ? " No slot chosen — this module falls back to the emitter's flat sorting order."
                    : $" \"{cur}\" is not a slot in this stack, so this module falls back to the emitter's "
                      + "flat sorting order at runtime.");

            var label = Z.Text(moduleLabel, ZuiText.Body, resolvedTip);
            label.style.width = LsModuleW;
            label.style.flexShrink = 0f;
            row.Add(label);

            // A REFERENCE to a declared slot → always picked, never typed. With no slots declared the radio
            // is simply empty and the button beside it is the way in — degrading to a text field here is the
            // failure mode, not the graceful fallback.
            //
            // T-0081 D-06: when `cur` is stored but does not match any declared slot, `sel` is -1 and a bare
            // Z.MiniRadio(sel, names, ...) would render with nothing latched — reading as "no depth chosen"
            // when a depth IS stored, it just isn't in the list any more. Mirrors ChunkWindow.Slicer.cs's
            // BuildSlicerLayerPick: show the stored value as an extra, selected option instead of hiding it.
            // Only THIS row's own radio gets the synthetic entry — the real stack (`names`) is untouched.
            var radioNames = names;
            int radioSel = sel;
            if (sel < 0 && names.Count > 0)
            {
                radioNames = new List<string>(names) { cur.Length == 0 ? "(none)" : cur };
                radioSel = radioNames.Count - 1;
            }
            row.Add(Z.MiniRadio(radioSel, radioNames.ToArray(), resolvedTip, i =>
            {
                // The synthetic stale/none entry isn't one of the real declared slots — re-picking it is a
                // no-op rather than writing the placeholder text itself back into the module.
                if (i >= names.Count) return;
                string picked = names[i];
                Dial("Module layer slot", () => set(picked));
                RebuildLayerAssignments(c);
            }, wrap: true));

            row.Add(Z.Flexible());

            // Never added/removed — the row keeps identical geometry however this module resolves, so clicking
            // through the slots never shifts the radio out from under the pointer. It is also the LAST thing
            // in the row, which is where a control whose width changes with its text belongs.
            //
            // CHANGED (blast groups): it used to be Hidden once the module resolved. That left the second half
            // of the depth workflow with no affordance at all — a resolved module still needs a way to say
            // "no, give this one its OWN depth", which is exactly what two blast groups holding the same blast
            // want, and hunting for it meant leaving this box for the Slots catalog, adding, then coming back
            // to pick. The reserved space was already there; it now always says what pressing it does.
            bool blank = cur.Length == 0;
            bool resolved = sel >= 0;
            string desiredNew = string.IsNullOrEmpty(newSlotName) ? moduleLabel : newSlotName;
            // Label = action: with a concrete name to declare, the button DOES the add on press; when it opens
            // further UI to choose in, it is named for that destination and ends in "…", never a bare verb.
            string fixLabel = resolved ? "New slot…"
                            : blank ? "+ Add slot…"
                                    : $"+ Add \"{LsEllipsize(cur, 14)}\"";
            // The tooltip states what is TRUE RIGHT NOW, not a situation that may not be happening: a
            // conditional tooltip that describes sharing while nothing is shared is the "reads for the wrong
            // state" bug, so the sharing sentence only appears when something really does share this slot.
            int sharing = resolved ? LsSlotUsers(c, cur) : 0;
            string fixTip = resolved
                ? (sharing > 1
                    ? $"{sharing} modules currently share \"{cur}\", so they all draw at the same depth. Give "
                      + "this one a NEW slot of its own, at the back or the front of the stack — that is how "
                      + "\"this blast behind the pieces, that one in front\" is said."
                    : $"Give this module a NEW slot of its own, at the back or the front of the stack, instead "
                      + $"of \"{cur}\" where it sits now.")
                : blank
                    ? "Open the slot catalog and declare a slot for this module."
                    : $"Declare \"{cur}\" as a slot in the stack so this module resolves (undoable).";
            Button fix = null;
            fix = Z.Button(fixLabel, fixTip, () =>
            {
                if (resolved) ShowNewSlotForModuleMenu(fix, c, desiredNew, set);
                else if (blank) ShowAddLayerSlotMenu(fix, c);
                else AddLayerSlot(c, cur, false);
            });
            fix.style.flexShrink = 0f;
            row.Add(fix);
            return row;
        }

        /// One click from the row that wants it: declare a NEW slot at the chosen END of the stack and put
        /// this module in it, in ONE undo step, then drop focus into that slot's name field so it can be
        /// named for what it is ("Back Blast") instead of being left as a duplicate of the blast's own name.
        ///
        /// Back/front rather than a numeric index because that is the sentence the user is actually saying —
        /// "behind the pieces", "in front of the pieces". Anything more precise is a drag on the slot's grip,
        /// which already exists a few rows above and reorders the whole stack visibly.
        void ShowNewSlotForModuleMenu(VisualElement anchor, ChunkSpec c, string desired,
                                      System.Action<string> set)
        {
            Z.Menu(anchor)
                .Section("New slot for this module")
                .Item("Behind everything",
                    "Add a new slot at the BACK of the stack (index 0, the lowest sortingOrder), put this "
                    + "module in it and open its name for editing (undoable).",
                    () => AddSlotForModule(c, desired, set, front: false))
                .Item("In front of everything",
                    "Add a new slot at the FRONT of the stack (the highest sortingOrder), put this module in "
                    + "it and open its name for editing (undoable).",
                    () => AddSlotForModule(c, desired, set, front: true))
                .Show();
        }

        /// Declaring the slot and assigning it are ONE Dial: half of the pair on its own is not a state the
        /// user ever asked for, so it is not a state Undo should be able to stop in either.
        void AddSlotForModule(ChunkSpec c, string desired, System.Action<string> set, bool front)
        {
            string unique = c.layers.UniqueName(string.IsNullOrEmpty(desired) ? "Layer" : desired);
            Dial(front ? "Add front layer slot" : "Add back layer slot", () =>
            {
                // Inserting at 0 renumbers every slot below it, and that is harmless BY DESIGN: assignments
                // are stored as NAMES, so every other module keeps the slot it had and simply resolves one
                // step further forward — which is what "put this one behind everything" means.
                if (front) c.layers.layers.Add(unique);
                else c.layers.layers.Insert(0, unique);
                set(unique);
            });
            _lsFocusSlot = front ? c.layers.layers.Count - 1 : 0;
            RebuildLayerSlots(c);
            RebuildLayerAssignments(c);
        }

        /// Hand-truncate before drawing, rather than letting a long slot name draw past a fixed-width button.
        static string LsEllipsize(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
