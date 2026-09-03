// DotGenWindow.Cards — the four module sections: Placement, Selectors, Mutators, Drawers.
//
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE CARD CONTRACT — what the shell provides, and what the four Rebuild* methods below build on it.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
//
// The shell (DotGenWindow.cs) builds the four sections ONCE, in `BuildCards`, and never touches their
// bodies again. Each section owns a body host element; the four Rebuild* methods below clear their own host
// and refill it. That is the whole contract: rebuild a BODY, never the flow, so the pane the author is
// working in cannot move under them when a selection changes or a card is added.
//
//  Sections and their body hosts (created by BuildCards, already added to the column flow and already
//  listed in the section toggle bar — do not create, re-parent or re-register them):
//      ZuiSection placementSection   → VisualElement placementHost
//      ZuiSection selectorsSection   → VisualElement selectorsHost
//      ZuiSection mutatorsSection    → VisualElement mutatorsHost
//      ZuiSection drawersSection     → VisualElement drawersHost
//
//  The four rebuild entry points (called by the shell on load, on selection change, and after any
//  structural edit):
//      void RebuildPlacement()
//      void RebuildSelectors()
//      void RebuildMutators()
//      void RebuildDrawers()
//  All four are called from `RefillSelectionSections()` (DotGenWindow.cs) in that order, followed by
//  MarkDirty(). Each must tolerate `doc == null`, a null selected generator, and being called twice.
//
//  Hover / selection state for the gizmo pass (window-only, never persisted — POC §19.1):
//      string hoveredModuleId   — the module under the pointer, or null. Set from a card root's
//                                 PointerEnter/PointerLeave via HoverModule(id) / HoverModule(null).
//      string selectedModuleId  — the module whose card was last clicked, or null for "the generator area".
//                                 Set via SelectModule(id). The shell clears it whenever the selected
//                                 GENERATOR changes, so a stale id can never point into another generator.
//  Both are read by DotGenWindow.Gizmos.cs; neither means anything to the document.
//
//  Editing helpers the shell already owns (use these — never mutate the document directly):
//      void Dirty(Action apply, string label = "Edit DotGen")            — record, apply, dirty, invalidate
//      void DirtyRepaintOnly(Action apply, string label = "Edit DotGen") — for edits that change only how the
//                                                                          document is looked at
//      void MarkDirty()                                                  — the picture is stale
//      Action Record(string label)                                       — the `onBeforeMutate` half, for a
//                                                                          control that brackets its own drag
//      void Applied()                                                    — the commit half of the same pair
//      ZuiMicroSlider Dial(label, value, min, max, tooltip, set, defaultValue, decimals, width)
//      void RefillSelectionSections()                                    — all four bodies + the generator card
//      void RebuildTree()                                                — the hierarchy rows
//      DotGen doc                                                        — the document (may be null)
//
//  Module ids are minted by the DOCUMENT, never by a module: `doc.NewId("place" | "sel" | "mut" | "draw")`.
//  A new module instance comes from `DotModuleRegistry.Create(entry)`, which sets its default name and
//  leaves the id blank.
//
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
//  HOW A CARD IS BUILT (one shape for all four sections — `BuildCard`)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
//
//  header (never wraps):  ▾ caret · ≡ grip · enable · name · category · flexible gap · ×
//  body:                  the selector picker (mutators/drawers) then the module's own reflected fields
//
//  Only the parts a module can actually use are built: the single active placement has no grip, no enable
//  and no remove, because there is always exactly one and it cannot be turned off; selectors carry no grip
//  because they are referenced by id and their list position means nothing, while a mutator's and a
//  drawer's position IS its order and both are draggable.
//
//  The fields come from `ZuiReflect.FlowFields`, so a module type's dials are whatever its fields say they
//  are and `[ZUIShowIf]` gating works with no per-card code. A gate field's edit calls OnStructureChanged →
//  the section's own rebuild, so a mode switch redraws the card; an ordinary slider drag rebuilds nothing.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Reflection;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.DotGen.Editor
{
    public partial class DotGenWindow
    {
        ZuiSection placementSection, selectorsSection, mutatorsSection, drawersSection;
        VisualElement placementHost, selectorsHost, mutatorsHost, drawersHost;

        /// The module under the pointer, and the module whose card was last clicked. Both are properties of
        /// looking at the document, so they live on the window and are never saved.
        string hoveredModuleId;
        string selectedModuleId;

        /// Which cards are folded shut, by module id. Session-only by design (POC §19.1): a fold is a
        /// property of this window right now, not of the composition, so it is neither serialized on the
        /// window nor written to the asset — it survives every rebuild of a section body and nothing else.
        readonly HashSet<string> collapsedModules = new HashSet<string>();

        /// Marks a card so the outline pass can find every one of them without the window keeping a list
        /// that would have to be pruned on each rebuild.
        const string CardClass = "dotgen-card";

        /// The cyan gizmo-target outline (rule in ZuiToolkit.uss). A card wearing it is the card whose
        /// process overlays are being drawn, so the card and the overlay read as one thing.
        const string CardActiveClass = "zui-card--gizmo";

        void BuildCards(VisualElement host, DotGen d)
        {
            placementSection = Z.Section("Placement",
                "How the selected generator fills its area with dots. Placement creates the dots — and, for a "
                + "grid or a row of boxes, the cells a child or a drawer can be measured against.",
                "dotgen.placement");
            placementHost = new VisualElement();
            placementSection.Add(placementHost);
            host.Add(placementSection);

            selectorsSection = Z.Section("Selectors",
                "Reusable strengths over this generator's dots. A selector changes nothing by itself — a "
                + "mutator or a drawer decides what its weight means.",
                "dotgen.selectors");
            selectorsHost = new VisualElement();
            selectorsSection.Add(selectorsHost);
            host.Add(selectorsSection);

            mutatorsSection = Z.Section("Mutators",
                "Moves and removals applied to the dots, in the order listed. Each one reads a selector, or "
                + "every dot equally.",
                "dotgen.mutators");
            mutatorsHost = new VisualElement();
            mutatorsSection.Add(mutatorsHost);
            host.Add(mutatorsSection);

            drawersSection = Z.Section("Drawers",
                "Shapes painted from this generator's areas or cells, after the dots are final. A drawer never "
                + "moves a dot and never changes what spawns.",
                "dotgen.drawers");
            drawersHost = new VisualElement();
            drawersSection.Add(drawersHost);
            host.Add(drawersSection);
        }

        /// Set the module the gizmo pass should follow in Hovered mode. Null clears it.
        void HoverModule(string moduleId)
        {
            if (hoveredModuleId == moduleId) return;
            hoveredModuleId = moduleId;
            RefreshCardOutlines();
            if (doc != null && doc.gizmoMode == DotGizmoMode.Hovered) preview?.MarkDirtyRepaint();
        }

        /// Set the module the gizmo pass should follow in Selected mode. Null means the generator's own area.
        void SelectModule(string moduleId)
        {
            if (selectedModuleId == moduleId) return;
            selectedModuleId = moduleId;
            RefreshCardOutlines();
            if (doc != null && doc.gizmoMode == DotGizmoMode.Selected) preview?.MarkDirtyRepaint();
        }

        /// Light the hovered and selected cards, and only those. Queried from the four hosts rather than
        /// from a bookkept list: a section rebuild throws its cards away, and a list of them would have to be
        /// pruned in four places to stay honest.
        void RefreshCardOutlines()
        {
            RefreshCardOutlines(placementHost);
            RefreshCardOutlines(selectorsHost);
            RefreshCardOutlines(mutatorsHost);
            RefreshCardOutlines(drawersHost);
        }

        void RefreshCardOutlines(VisualElement host)
        {
            if (host == null) return;
            foreach (var card in host.Query<VisualElement>(className: CardClass).ToList())
            {
                string id = card.userData as string;
                bool lit = !string.IsNullOrEmpty(id) && (id == hoveredModuleId || id == selectedModuleId);
                card.EnableInClassList(CardActiveClass, lit);
            }
        }

        // ── Placement ─────────────────────────────────────────────────────────────────────────

        void RebuildPlacement()
        {
            if (placementHost == null) return;
            placementHost.Clear();
            if (doc == null) return;

            var g = doc.Selected;
            if (g == null)
            {
                placementHost.Add(Z.Text("No generator selected.", ZuiText.Subtle,
                    "Pick a generator in the Hierarchy section to choose how it places its dots."));
                return;
            }

            var entries = DotModuleRegistry.Placements;
            if (entries.Count == 0) return;

            var labels = new string[entries.Count];
            int sel = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                labels[i] = entries[i].displayName;
                if (entries[i].typeId == g.placementTypeId) sel = i;
            }

            // The tooltip has to answer the question the switch actually raises — "do I lose my grid if I
            // try radial?" — and it can only answer it truthfully because the generator keeps one instance
            // per method (DotGenerator.placementBank).
            string methodTip = "How this generator turns its area into dots. Currently " + labels[sel]
                + ". Each method keeps its own settings, so switching away and back brings them with it.";

            placementHost.Add(Z.Field("Method", methodTip,
                Choice(sel, labels, methodTip, i => SetPlacementMethod(g, entries[i]))));

            var p = g.ActivePlacement;
            if (p == null)
            {
                placementHost.Add(Z.Text("No placement.", ZuiText.Subtle,
                    "This generator has no placement instance yet — pick a method above."));
                return;
            }

            string category = p.Meta?.DisplayName ?? "Placement";
            // No title on this card: the Method row directly above it already names the placement, and a card
            // headed "Grid" under a radio reading "Grid" says the same thing twice.
            placementHost.Add(BuildCard(p, category,
                category + " — the one placement this generator is running. A generator has exactly one, so "
                + "this card cannot be turned off or removed; choose a different method above instead.",
                listHost: null, rebuild: RebuildPlacement, onMoved: null, onRemove: null,
                canEnable: false, canRename: false,
                buildBody: body =>
                {
                    var o = ModuleFieldOptions(RebuildPlacement, null);
                    o.DefaultFor = f => RolePlacementDefault(g, p, f);
                    ZuiReflect.FlowFields(body, p, o);
                },
                showCategory: false));
        }

        /// What a double-click on a placement dial restores. Almost always the type's own initializer (null
        /// hands the question back to the reflection drawer) — but a Grid is 8x7 on a root generator and 4x4 on
        /// a child, and the generator, not the Grid type, is what knows which. Resolved from a FRESH instance
        /// put through the same role pass a new placement gets, so the reset and the creation can never drift.
        float? RolePlacementDefault(DotGenerator g, DotPlacement active, System.Reflection.FieldInfo f)
        {
            if (g == null || !(active is DotGridPlacement)) return null;
            if (f.Name != "columns" && f.Name != "rows") return null;
            var fresh = new DotGridPlacement();
            g.ApplyRolePlacementDefaults(fresh);
            return f.Name == "columns" ? fresh.columns : fresh.rows;
        }

        /// Switch method. `UsePlacement` activates the bank's existing instance for that type, or creates it
        /// from its own field initializers (plus the generator's role defaults) the first time it is chosen —
        /// so a first visit arrives on the registered defaults and a return visit arrives on the author's own
        /// numbers.
        void SetPlacementMethod(DotGenerator g, DotModuleRegistry.Entry e)
        {
            if (doc == null || g == null || e.typeId == g.placementTypeId) return;

            string activeId = null;
            Dirty(() =>
            {
                var p = g.UsePlacement(e.typeId);
                if (p == null) return;
                if (string.IsNullOrEmpty(p.id)) p.id = doc.NewId("place");
                activeId = p.id;
            }, "Set DotGen placement method");

            SelectModule(activeId);
            RebuildPlacement();
        }

        // ── Selectors ─────────────────────────────────────────────────────────────────────────

        void RebuildSelectors()
        {
            if (selectorsHost == null) return;
            selectorsHost.Clear();
            if (doc == null) return;

            var g = doc.Selected;
            if (g == null)
            {
                selectorsHost.Add(Z.Text("No generator selected.", ZuiText.Subtle,
                    "Pick a generator in the Hierarchy section to give it selectors."));
                return;
            }
            if (g.selectors == null) g.selectors = new List<DotSelector>();

            for (int i = 0; i < g.selectors.Count; i++)
            {
                var s = g.selectors[i];
                if (s == null) continue;
                string category = s.Meta?.DisplayName ?? "Selector";
                var self = s;
                // No grip: a selector is reached by name, never by position, so a drag handle would promise
                // an order that changes nothing.
                selectorsHost.Add(BuildCard(self, category,
                    category + " — what it weighs is fixed by its kind; add another selector for a different "
                    + "kind. Every mutator and drawer can read this one by name.",
                    listHost: null, rebuild: RebuildSelectors, onMoved: null,
                    onRemove: () => RemoveSelector(g, self),
                    canEnable: true, canRename: true,
                    buildBody: body => ZuiReflect.FlowFields(body, self,
                        ModuleFieldOptions(RebuildSelectors, null))));
            }

            selectorsHost.Add(BuildAddRow(DotModuleRegistry.Selectors, "Selector",
                "a reusable weight over this generator's dots", e => AddSelector(g, e)));
        }

        void AddSelector(DotGenerator g, DotModuleRegistry.Entry e)
        {
            if (doc == null || g == null) return;
            string newId = null;
            Dirty(() =>
            {
                var s = DotModuleRegistry.Create(e) as DotSelector;
                if (s == null) return;
                s.id = doc.NewId("sel");
                g.selectors.Add(s);
                newId = s.id;
            }, "Add DotGen selector");
            if (newId == null) return;

            SelectModule(newId);
            RebuildSelectors();
            // Every mutator and drawer picker gains the new name.
            RebuildMutators();
            RebuildDrawers();
        }

        /// Removing a selector retargets everything that referenced it to All dots — a reference left
        /// pointing at a deleted selector would resolve to the same weight of 1 anyway, but silently, and the
        /// card would show a picker with nothing selected.
        void RemoveSelector(DotGenerator g, DotSelector s)
        {
            if (doc == null || g == null || s == null) return;
            string sid = s.id;

            Dirty(() =>
            {
                g.selectors.Remove(s);
                if (g.mutators != null)
                    for (int i = 0; i < g.mutators.Count; i++)
                        if (g.mutators[i] != null && g.mutators[i].selectorId == sid) g.mutators[i].selectorId = "";
                if (g.drawers != null)
                    for (int i = 0; i < g.drawers.Count; i++)
                        if (g.drawers[i] != null && g.drawers[i].selectorId == sid) g.drawers[i].selectorId = "";
            }, "Remove DotGen selector");

            ForgetModule(sid);
            RebuildSelectors();
            RebuildMutators();
            RebuildDrawers();
        }

        // ── Mutators ──────────────────────────────────────────────────────────────────────────

        void RebuildMutators()
        {
            if (mutatorsHost == null) return;
            mutatorsHost.Clear();
            if (doc == null) return;

            var g = doc.Selected;
            if (g == null)
            {
                mutatorsHost.Add(Z.Text("No generator selected.", ZuiText.Subtle,
                    "Pick a generator in the Hierarchy section to give it mutators."));
                return;
            }
            if (g.mutators == null) g.mutators = new List<DotMutator>();

            // The cards live in their own container so the drag-reorder helper sees the list and nothing
            // else — an add button among them would be a drop target.
            var list = new VisualElement();
            mutatorsHost.Add(list);

            for (int i = 0; i < g.mutators.Count; i++)
            {
                var m = g.mutators[i];
                if (m == null) continue;
                var self = m;
                string category = m.Meta?.DisplayName ?? "Mutator";
                list.Add(BuildCard(self, category,
                    category + " — mutators run top to bottom, each one seeing what the one above it left, so "
                    + "dragging this card changes the result.",
                    listHost: list, rebuild: RebuildMutators,
                    onMoved: (from, to) => MoveModule(g.mutators, from, to, RebuildMutators,
                        "Reorder DotGen mutator"),
                    onRemove: () => RemoveModule(g.mutators, self, RebuildMutators, "Remove DotGen mutator"),
                    canEnable: true, canRename: true,
                    buildBody: body =>
                    {
                        body.Add(SelectorPickerRow(g, self.selectorId,
                            id => self.selectorId = id, RebuildMutators));
                        ZuiReflect.FlowFields(body, self, ModuleFieldOptions(RebuildMutators, null));
                    }));
            }

            mutatorsHost.Add(BuildAddRow(DotModuleRegistry.Mutators, "Mutator",
                "a move or a removal applied to this generator's dots", e => AddMutator(g, e)));
        }

        void AddMutator(DotGenerator g, DotModuleRegistry.Entry e)
        {
            if (doc == null || g == null) return;
            string newId = null;
            Dirty(() =>
            {
                var m = DotModuleRegistry.Create(e) as DotMutator;
                if (m == null) return;
                m.id = doc.NewId("mut");
                g.mutators.Add(m);
                newId = m.id;
            }, "Add DotGen mutator");
            if (newId == null) return;

            SelectModule(newId);
            RebuildMutators();
        }

        // ── Drawers ───────────────────────────────────────────────────────────────────────────

        void RebuildDrawers()
        {
            if (drawersHost == null) return;
            drawersHost.Clear();
            if (doc == null) return;

            var g = doc.Selected;
            if (g == null)
            {
                drawersHost.Add(Z.Text("No generator selected.", ZuiText.Subtle,
                    "Pick a generator in the Hierarchy section to give it drawers."));
                return;
            }
            if (g.drawers == null) g.drawers = new List<DotDrawer>();

            var list = new VisualElement();
            drawersHost.Add(list);

            for (int i = 0; i < g.drawers.Count; i++)
            {
                var d = g.drawers[i];
                if (d == null) continue;
                var self = d;
                string category = d.Meta?.DisplayName ?? "Drawer";
                list.Add(BuildCard(self, category,
                    category + " — drawers paint top to bottom, so a later one covers an earlier one. Dragging "
                    + "this card changes what ends up on top.",
                    listHost: list, rebuild: RebuildDrawers,
                    onMoved: (from, to) => MoveModule(g.drawers, from, to, RebuildDrawers,
                        "Reorder DotGen drawer"),
                    onRemove: () => RemoveModule(g.drawers, self, RebuildDrawers, "Remove DotGen drawer"),
                    canEnable: true, canRename: true,
                    buildBody: body => BuildDrawerBody(body, g, self)));
            }

            drawersHost.Add(BuildAddRow(DotModuleRegistry.Drawers, "Drawer",
                "paint over this generator's areas or cells", e => AddDrawer(g, e)));
        }

        /// A drawer's body. The Fill drawer's paint is drawn by hand rather than reflected: a `ZuiFill` has no
        /// reflected control (it would come out as a nested box of raw fields) and the fill LIST needs order
        /// and a protected last entry, which no generic list drawer can know about.
        void BuildDrawerBody(VisualElement body, DotGenerator g, DotDrawer d)
        {
            var fill = d as DotFillDrawer;

            // The selector only decides which CELLS get painted; painting whole generator areas never reads
            // it, so the picker appears exactly where it does something (POC §11.1).
            bool usesSelector = fill == null || fill.drawTarget == DotDrawTargetKind.PlacementCells;
            if (usesSelector)
                body.Add(SelectorPickerRow(g, d.selectorId, id => d.selectorId = id, RebuildDrawers));

            if (fill == null)
            {
                ZuiReflect.FlowFields(body, d, ModuleFieldOptions(RebuildDrawers, null));
                return;
            }

            ZuiReflect.FlowFields(body, fill,
                ModuleFieldOptions(RebuildDrawers, f => f.Name == "fill" || f.Name == "fills"));
            BuildFillBody(body, fill, RebuildDrawers);
        }

        void AddDrawer(DotGenerator g, DotModuleRegistry.Entry e)
        {
            if (doc == null || g == null) return;
            string newId = null;
            Dirty(() =>
            {
                var d = DotModuleRegistry.Create(e) as DotDrawer;
                if (d == null) return;
                d.id = doc.NewId("draw");
                g.drawers.Add(d);
                newId = d.id;
            }, "Add DotGen drawer");
            if (newId == null) return;

            SelectModule(newId);
            RebuildDrawers();
        }

        // ── the shared card ───────────────────────────────────────────────────────────────────

        /// One module card. Every part is optional because the four categories genuinely differ: the active
        /// placement has no grip/enable/remove, a selector has no grip, a mutator and a drawer have all of it.
        VisualElement BuildCard(DotModule m, string category, string categoryTip,
            VisualElement listHost, Action rebuild, Action<int, int> onMoved, Action onRemove,
            bool canEnable, bool canRename, Action<VisualElement> buildBody, bool showCategory = true)
        {
            var card = Z.Box(null, null);
            card.AddToClassList(CardClass);
            card.userData = m.id;
            // With no title in the header there is nothing left to hover for an explanation, so the card
            // itself carries the one the label would have had.
            if (!showCategory) card.tooltip = categoryTip;

            var header = Z.Row();
            header.style.flexWrap = Wrap.NoWrap;

            // Controls that must never fold the card out from under the pointer (POC §16.6).
            var nonFolding = new List<VisualElement>();

            var caret = new Label(IsCardOpen(m.id) ? "▾" : "▸") { pickingMode = PickingMode.Ignore };
            caret.AddToClassList("zui-box__caret");
            header.Add(caret);

            if (onMoved != null && listHost != null)
            {
                var grip = Z.Text("≡", ZuiText.Body,
                    "Drag to reorder — this card's position IS the order it runs in.");
                grip.style.unityFontStyleAndWeight = FontStyle.Bold;
                grip.style.width = 16f;
                grip.style.flexShrink = 0f;
                ZuiReorder.MakeGrip(grip, card, listHost, onMoved);
                header.Add(grip);
            }

            if (canEnable)
            {
                var enable = Z.Toggle("", m.enabled
                        ? "On. Turn it off to stop it doing anything while keeping its settings."
                        : "Off — it does nothing and its settings are kept. Turn it on to run it again.",
                    m.enabled, v =>
                    {
                        Dirty(() => m.enabled = v, "Toggle DotGen module");
                        rebuild();
                    });
                enable.style.flexShrink = 0f;
                header.Add(enable);
                nonFolding.Add(enable);
            }

            if (canRename)
            {
                // A name is a DECLARATION — the one place a text field is right. Every reference to it
                // elsewhere is a picker.
                var nameField = Z.TextInput(m.name ?? "",
                    "This " + category.ToLowerInvariant() + "'s name, as every picker that can point at it "
                    + "will show it.",
                    v =>
                    {
                        if (doc == null) return;
                        Undo.RecordObject(doc, "Rename DotGen module");
                        m.name = v;
                        EditorUtility.SetDirty(doc);
                        OnModuleRenamed(m);
                    }, 0f);
                // The name is the header's variable-width content, so it takes the slack itself rather than
                // sharing it with a flexible spacer — a field sized to half the slack clipped the last letter
                // of an ordinary default name ("Edge margin" → "Edge margi"), which reads as a data bug.
                nameField.style.flexGrow = 1f;
                nameField.style.flexShrink = 1f;
                nameField.style.minWidth = 90f;
                nameField.AddToClassList("zui-audit-allow-stretch");
                header.Add(nameField);
                nonFolding.Add(nameField);

                if (showCategory)
                {
                    var cat = Z.Text(category, ZuiText.Small, categoryTip);
                    cat.style.flexShrink = 0f;
                    cat.style.whiteSpace = WhiteSpace.NoWrap;
                    header.Add(cat);
                }
            }
            else if (showCategory)
            {
                var cat = Z.Text(category, ZuiText.Body, categoryTip);
                cat.style.flexShrink = 0f;
                cat.style.whiteSpace = WhiteSpace.NoWrap;
                header.Add(cat);
            }

            // A card whose name field already eats the slack needs no spacer; one without a name field does,
            // or its × would sit against the caret instead of hard right.
            if (!canRename) header.Add(Z.Flexible());

            if (onRemove != null)
            {
                var remove = Z.Button("×", "Remove this " + category.ToLowerInvariant() + " (undoable).",
                    onRemove).W(22f);
                header.Add(remove);
                nonFolding.Add(remove);
            }

            card.Add(header);

            var body = new VisualElement();
            if (canEnable && !m.enabled)
                body.Add(Z.Text("Disabled — settings kept.", ZuiText.Subtle,
                    "This module is switched off, so its dials are put away rather than offered. Turn it back "
                    + "on in the header and every setting is exactly where it was."));
            else
                buildBody?.Invoke(body);
            card.Add(body);

            foreach (var c in nonFolding) c.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            body.style.display = IsCardOpen(m.id) ? DisplayStyle.Flex : DisplayStyle.None;

            // Clicking the header does BOTH: it makes this the gizmo target and folds/unfolds the card
            // (POC §13.1). A Clickable, not a raw PointerDownEvent — a header inside a ScrollView does not
            // receive a bare pointer-down reliably.
            string id = m.id;
            header.AddManipulator(new Clickable(() =>
            {
                SelectModule(id);
                bool open = !IsCardOpen(id);
                SetCardOpen(id, open);
                caret.text = open ? "▾" : "▸";
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }));

            card.RegisterCallback<PointerEnterEvent>(_ => HoverModule(id));
            card.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (hoveredModuleId == id) HoverModule(null);
            });

            card.EnableInClassList(CardActiveClass,
                !string.IsNullOrEmpty(id) && (id == hoveredModuleId || id == selectedModuleId));
            return card;
        }

        /// A renamed selector has to relabel every picker pointing at it, live. Only the OTHER sections are
        /// rebuilt, so the field being typed in is never taken away mid-word.
        void OnModuleRenamed(DotModule m)
        {
            if (m is DotSelector)
            {
                RebuildMutators();
                RebuildDrawers();
            }
        }

        bool IsCardOpen(string id) => string.IsNullOrEmpty(id) || !collapsedModules.Contains(id);

        void SetCardOpen(string id, bool open)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (open) collapsedModules.Remove(id);
            else collapsedModules.Add(id);
        }

        /// Drop every trace of a module the document no longer has, so a recycled id can never inherit a
        /// stale fold or keep drawing gizmos for something that is gone.
        void ForgetModule(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            collapsedModules.Remove(id);
            if (selectedModuleId == id) SelectModule(null);
            if (hoveredModuleId == id) HoverModule(null);
        }

        /// The reflection drawer's Undo / dirty / rebuild contract, identical for every module: record before
        /// the write, mark and re-evaluate after it, and redraw the card only when a `[ZUIShowIf]` gate moved.
        ZuiReflect.Options ModuleFieldOptions(Action rebuild, Func<FieldInfo, bool> alsoSkip)
            => new ZuiReflect.Options
            {
                OnBeforeChange = Record("Edit DotGen"),
                OnChanged = Applied,
                OnStructureChanged = rebuild,
                // id is internal, and the name and enable live in the card header — the drawer would
                // otherwise surface all three again inside the body.
                Skip = f => f.Name == "id" || f.Name == "name" || f.Name == "enabled"
                            || (alsoSkip != null && alsoSkip(f)),
            };

        // ── the selector picker ───────────────────────────────────────────────────────────────

        /// The one control a mutator or a drawer uses to point at a selector: a pick from the names this
        /// generator actually declares, never a typed id. "All dots" is first because it is the default and
        /// because a generator with no selectors must still read as a working card, not a dead end.
        VisualElement SelectorPickerRow(DotGenerator g, string currentId, Action<string> set, Action rebuild)
        {
            var sels = g.selectors ?? new List<DotSelector>();
            var labels = new string[sels.Count + 1];
            labels[0] = "All dots";
            int sel = 0;
            for (int i = 0; i < sels.Count; i++)
            {
                var s = sels[i];
                string name = s == null || string.IsNullOrEmpty(s.name) ? "(unnamed)" : s.name;
                // A disabled selector resolves to All dots, so say so rather than let the card claim a
                // weight that is not being applied.
                labels[i + 1] = s != null && !s.enabled ? name + " (off)" : name;
                if (s != null && s.id == currentId) sel = i + 1;
            }

            string tip = sels.Count == 0
                ? "Which selector's weight this reads. This generator declares none yet, so every dot counts "
                  + "fully — add one in the Selectors section and it appears here."
                : sel == 0
                    ? "Which selector's weight this reads. Currently All dots: every dot counts fully."
                    : "Which selector's weight this reads. Currently " + labels[sel]
                      + ". All dots gives every dot a weight of one.";

            return Z.Field("Selector", tip,
                Z.MiniRadio(sel, labels, tip, i =>
                {
                    string id = i <= 0 ? "" : (sels[i - 1] != null ? sels[i - 1].id : "");
                    Dirty(() => set(id), "Set DotGen selector reference");
                    // Redrawn so the tooltip reads for the state it is now in.
                    rebuild();
                }, wrap: true));
        }

        // ── add / reorder / remove ────────────────────────────────────────────────────────────

        /// The add affordance for one category. With several kinds to choose from it opens a menu of them;
        /// with exactly one it names that kind and adds it on the press, because a button labelled with an
        /// action must perform it rather than open somewhere else to perform it.
        VisualElement BuildAddRow(IReadOnlyList<DotModuleRegistry.Entry> entries, string kindLabel,
            string kindBlurb, Action<DotModuleRegistry.Entry> add)
        {
            Button btn = null;
            if (entries != null && entries.Count == 1)
            {
                var only = entries[0];
                btn = Z.Button("+ " + only.displayName,
                    "Add " + kindBlurb + ".", () => add(only));
            }
            else
            {
                btn = Z.Button("+ " + kindLabel,
                    "Add " + kindBlurb + " — pick which kind.",
                    () => ShowAddMenu(btn, entries, kindBlurb, add));
            }

            var row = Z.Row(btn);
            row.style.flexWrap = Wrap.NoWrap;
            return row;
        }

        void ShowAddMenu(VisualElement anchor, IReadOnlyList<DotModuleRegistry.Entry> entries,
            string kindBlurb, Action<DotModuleRegistry.Entry> add)
        {
            if (entries == null || entries.Count == 0) return;
            var menu = Z.Menu(anchor);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                menu.Item(e.displayName, "Add " + kindBlurb + ", as a " + e.displayName.ToLowerInvariant() + ".",
                    () => add(e));
            }
            menu.Show();
        }

        void MoveModule<T>(List<T> list, int from, int to, Action rebuild, string label) where T : DotModule
        {
            if (list == null || from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return;
            Dirty(() =>
            {
                var item = list[from];
                list.RemoveAt(from);
                list.Insert(to, item);
            }, label);
            rebuild();
        }

        void RemoveModule<T>(List<T> list, T m, Action rebuild, string label) where T : DotModule
        {
            if (list == null || m == null) return;
            Dirty(() => list.Remove(m), label);
            ForgetModule(m.id);
            rebuild();
        }
    }
}
