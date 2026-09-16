// ChunkWindow.Recipe — the stack of capability cards, and everything every card shares.
//
// One card per capability, keyed by the capability's own id rather than by its position, so a reorder moves
// the card without moving what the card REMEMBERS (its fold, its saved view). The card framework here is the
// whole of what a per-kind card partial has to know: it is handed a body element and its capability, and it
// writes rows into that body through Dial. It never touches the stack, the clock, undo or the preview.
using System;
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        VisualElement stackHost;   // the cards + the Add button; rebuilt on any structural change
        readonly Dictionary<string, ZuiBox> cards = new Dictionary<string, ZuiBox>();
        readonly Dictionary<string, VisualElement> cardBodies = new Dictionary<string, VisualElement>();
        // Each card's Delay field, by capability id — what a Timing band drag writes back into.
        readonly Dictionary<string, BaseField<float>> delayFields = new Dictionary<string, BaseField<float>>();
        // Each card's colour chip, by capability id — its text is the card's firing numbers, which any delay or
        // pattern edit can change without the card being rebuilt.
        readonly Dictionary<string, Label> cardChips = new Dictionary<string, Label>();
        static readonly List<int> NumberScratch = new List<int>();

        // The nine kinds, in the catalogue's order — the order the Add menu lists them and the order a reader
        // of the design doc expects. A kind is a name, an icon and a way to make one; everything else about
        // it lives in its own card partial and in its own capability class.
        static readonly (string label, string icon, Func<ChunkCapability> make)[] Kinds =
        {
            ("Debris Scatter",    "star",     () => new DebrisScatter()),
            ("Fragment Fracture", "scissors", () => new FragmentFracture()),
            ("Palette Splash",    "palette",  () => new PaletteSplash()),
            ("Pyre Blast",        "flame",    () => new PyreBlast()),
            // Born migrated (T-0368/2): a freshly-added Fling has no legacy inheritBurstDirection choice to
            // convert, so MarkBornMigrated() stops MigrateLegacyCurves() from ever touching it — the user's
            // Direction-mode pick then survives every domain reload instead of reverting to Burst.
            ("Fling",             "path",     () => { var t = new Trajectory(); t.MarkBornMigrated(); return t; }),
            ("Trail",             "cloud",    () => new Trail()),
            ("Hits",              "target",   () => new Hits()),
            ("Layer Plan",        "stack",    () => new LayerPlan()),
            ("Cues",              "clock",    () => new Cues()),
        };

        static string IconFor(ChunkCapability cap)
        {
            for (int i = 0; i < Kinds.Length; i++)
                if (Kinds[i].label == cap.KindName) return Kinds[i].icon;
            return null;
        }

        // ── the section ───────────────────────────────────────────────────────────────────────────────────

        void BuildRecipeSection(VisualElement parent, ChunkSpec c)
        {
            recipeSection = Z.Section("Recipe",
                "The capabilities this recipe is composed of, in the order they are authored — which is also " +
                "the order they draw in when no Layer Plan says otherwise.", "Chunks.recipe", "list");
            stackHost = new VisualElement();
            recipeSection.Add(stackHost);
            FillStack(c);
            parent.Add(recipeSection);
        }

        void FillStack(ChunkSpec c)
        {
            stackHost.Clear();
            cards.Clear();
            cardBodies.Clear();
            delayFields.Clear();
            blastOffsetSyncs.Clear();
            cardChips.Clear();

            // The cards get a host of their own, so the drag-reorder's drop index counts cards and nothing
            // else (not the Add button below them).
            var cardList = new VisualElement();
            stackHost.Add(cardList);

            var shown = new List<ChunkCapability>();
            var stack = c.capabilities;
            if (stack != null)
                for (int i = 0; i < stack.Count; i++)
                    if (stack[i] != null) shown.Add(stack[i]);
            for (int i = 0; i < shown.Count; i++)
                cardList.Add(BuildCard(c, shown[i], cardList, shown));
            RefreshCardChips();

            var add = Z.Button("Add capability…",
                "Put another capability in this recipe — a producer that throws something, a modifier that " +
                "decorates what a producer made, or a coordinator that arranges them.",
                () => ShowAddMenu(c));
            add.style.width = 160f;
            add.style.alignSelf = Align.FlexStart;
            stackHost.Add(Z.VSpace(6f));
            stackHost.Add(add);
        }

        /// Rebuild the CARDS after a structural change (add, remove, reorder) without touching the rest of
        /// the window: the preview keeps its size and its clock, and the pane keeps its scroll position.
        void RebuildStack()
        {
            var c = Current;
            if (c == null || stackHost == null) { Rebuild(); return; }
            var offset = leftPane != null ? leftPane.scrollOffset : Vector2.zero;
            FillStack(c);
            RestoreScroll(offset);
            SyncTiming();
        }

        // ── one card ──────────────────────────────────────────────────────────────────────────────────────

        VisualElement BuildCard(ChunkSpec c, ChunkCapability cap, VisualElement cardList,
                                List<ChunkCapability> shown)
        {
            string id = cap.EnsureId();

            var box = Z.BoxKeyed(cap.Title, KindTooltip(cap), "Chunks.card." + id, IconFor(cap));
            var colour = ChunkCardColors.For(c, cap);
            box.SetAccent(colour);

            // The header carries identity and the one control every card has, and nothing else. Identity
            // leads — the grip, then the chip in the card's own colour carrying its firing numbers — and the
            // actions trail: on/off, duplicate, remove. A dial in a header would be a dial the fold cannot hide.
            var grip = Z.Text("≡", ZuiText.Body,
                "Drag to move this card in the recipe. Its place is also its drawing order: a card lower " +
                "down draws in front of the ones above it, unless a Layer Plan puts it in a slot.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 14f;
            grip.style.unityTextAlign = TextAnchor.MiddleCenter;
            ZuiReorder.MakeGrip(grip, box, cardList, (from, to) => MoveShown(c, shown, from, to));
            box.AddHeaderLead(grip);
            box.AddHeaderLead(CardChip(cap, colour));

            box.AddHeaderContent(Z.Toggle("On",
                "Take part in this recipe. Off keeps every value here but puts nothing on screen.",
                cap.enabled, v => Dial("Toggle Capability", () =>
                {
                    cap.enabled = v;
                    SetCardEnabled(id, v);
                })));

            box.AddHeaderContent(IconButton("copy", "⧉",
                "Duplicate this card: a copy with every value, placed right after it, in a colour of its own " +
                "(undoable).", () => Duplicate(c, cap)));
            box.AddHeaderContent(SmallButton("×", "Remove this capability from the recipe (undoable).",
                true, () => Remove(c, cap)));

            var body = new VisualElement();
            box.Add(body);
            cards[id] = box;
            cardBodies[id] = body;

            FillCardBody(body, c, cap);
            body.SetEnabled(cap.enabled);
            return box;
        }

        static Button SmallButton(string glyph, string tooltip, bool enabled, Action onClick)
        {
            var b = Z.Button(glyph, tooltip, onClick);
            b.style.width = 22f;
            b.SetEnabled(enabled);
            return b;
        }

        /// A header-sized button showing a ZUI icon, or the glyph when the icon set has no such name.
        static Button IconButton(string icon, string fallbackGlyph, string tooltip, Action onClick)
        {
            var b = SmallButton("", tooltip, true, onClick);
            var glyph = Z.Icon(icon, 12f);
            if (glyph == null) { b.text = fallbackGlyph; return b; }
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
            b.Add(glyph);
            return b;
        }

        /// The card's identity chip: a swatch in the colour its lane and its preview outline wear, carrying the
        /// numbers its blasts go off as on the preview. A card that sets off no blasts shows the colour alone.
        Label CardChip(ChunkCapability cap, Color colour)
        {
            var chip = new Label();
            chip.AddToClassList("chunks-card-chip");
            chip.style.backgroundColor = colour;
            chip.style.color = ChunkCardColors.InkOn(colour);
            chip.style.minWidth = 14f;
            chip.style.height = 14f;
            chip.style.flexShrink = 0f;
            chip.style.marginLeft = 2f;
            chip.style.marginRight = 5f;
            chip.style.paddingLeft = 3f;
            chip.style.paddingRight = 3f;
            chip.style.paddingTop = 0f;
            chip.style.paddingBottom = 0f;
            chip.style.fontSize = 10f;
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.style.unityTextAlign = TextAnchor.MiddleCenter;
            chip.style.borderTopLeftRadius = 3f;
            chip.style.borderTopRightRadius = 3f;
            chip.style.borderBottomLeftRadius = 3f;
            chip.style.borderBottomRightRadius = 3f;
            chip.tooltip = cap is PyreBlast
                ? "This card's colour — its Timing lane and its outlines on the preview wear it too. The " +
                  "numbers are the order its blasts go off in across the whole recipe, the same numbers the " +
                  "preview prints on them."
                : "This card's colour — its Timing lane and what it draws on the preview wear it too.";
            cardChips[cap.EnsureId()] = chip;
            return chip;
        }

        /// Rewrite every card chip's numbers from the recipe as it is now — a delay or a pattern edit reorders
        /// the whole recipe's firing, not just the card that was edited.
        internal void RefreshCardChips()
        {
            var c = Current;
            if (c == null || cardChips.Count == 0) return;
            ChunkPreviewSim.RankFirings(c);
            foreach (var pair in cardChips)
            {
                var cap = Find(c, pair.Key);
                if (cap == null || pair.Value == null) continue;
                ChunkPreviewSim.FiringNumbers(cap, NumberScratch);
                string text = FormatNumbers(NumberScratch);
                if (pair.Value.text != text) pair.Value.text = text;
            }
        }

        /// "3", "1–6" for a run, "2…9" for a scattered set (the preview shows each one).
        static string FormatNumbers(List<int> sorted)
        {
            if (sorted == null || sorted.Count == 0) return "";
            int first = sorted[0], last = sorted[sorted.Count - 1];
            if (first == last) return first.ToString();
            bool run = true;
            for (int i = 1; i < sorted.Count && run; i++)
                run = sorted[i] - sorted[i - 1] <= 1;
            return run ? first + "–" + last : first + "…" + last;
        }

        /// Bring one card into view: unfold it, scroll it to the top of the recipe pane and light it for a
        /// moment, so the eye lands on the right card in a stack of look-alikes.
        internal void RevealCard(string capabilityId)
        {
            if (string.IsNullOrEmpty(capabilityId) || leftPane == null) return;
            if (!cards.TryGetValue(capabilityId, out var box) || box == null) return;
            box.IsOpen = true;

            var pane = leftPane;
            // One frame late: an unfolded card has no laid-out position until the pane has run its layout.
            pane.schedule.Execute(() =>
            {
                if (box.panel == null || pane.panel == null) return;
                float y = box.ChangeCoordinatesTo(pane.contentContainer, Vector2.zero).y;
                pane.scrollOffset = new Vector2(pane.scrollOffset.x, Mathf.Max(0f, y - 4f));
                box.AddToClassList("zui-card--gizmo");
                box.schedule.Execute(() => box.RemoveFromClassList("zui-card--gizmo")).ExecuteLater(1200);
            }).ExecuteLater(0);
        }

        /// Rebuild ONE card's body — what a dial that changes which controls exist calls, so a mode switch
        /// costs the card and nothing around it.
        internal void RebuildCard(string capabilityId)
        {
            var c = Current;
            if (c == null) return;
            var cap = Find(c, capabilityId);
            if (cap == null || !cardBodies.TryGetValue(capabilityId, out var body)) { RebuildStack(); return; }

            var offset = leftPane != null ? leftPane.scrollOffset : Vector2.zero;
            body.Clear();
            FillCardBody(body, c, cap);
            body.SetEnabled(cap.enabled);
            RestoreScroll(offset);
        }

        void SetCardEnabled(string capabilityId, bool on)
        {
            if (cardBodies.TryGetValue(capabilityId, out var body)) body.SetEnabled(on);
            SyncTiming();
        }

        // The body of every card, in one place: the shared rows first, then the kind's own. A kind that has
        // no card yet still gets its delay row, so the stack reads consistently while the rest lands.
        void FillCardBody(VisualElement body, ChunkSpec c, ChunkCapability cap)
        {
            body.Add(IdentityRow(c, cap));

            switch (cap)
            {
                case DebrisScatter d:    BuildDebrisScatterCard(body, c, d); break;
                case FragmentFracture f: BuildFragmentFractureCard(body, c, f); break;
                case PaletteSplash p:    BuildPaletteSplashCard(body, c, p); break;
                case PyreBlast b:        BuildPyreBlastCard(body, c, b); break;
                case Trajectory t:       BuildTrajectoryCard(body, c, t); break;
                case Trail t:            BuildTrailCard(body, c, t); break;
                case Hits h:             BuildHitsCard(body, c, h); break;
                case LayerPlan l:        BuildLayerPlanCard(body, c, l); break;
                case Cues q:             BuildCuesCard(body, c, q); break;
            }
        }

        static string KindTooltip(ChunkCapability cap)
        {
            switch (cap)
            {
                case DebrisScatter _:    return "Throws a spray of pieces from the recipe's origin.";
                case FragmentFracture _: return "Cuts a picture into pieces and flings them.";
                case PaletteSplash _:    return "Sprays small particles in the colours of a picture.";
                case PyreBlast _:        return "Spawns an effect — one, or a whole pattern of them.";
                case Trajectory _:       return "Flings what a Pyre Blast spawned along an arc.";
                case Trail _:            return "Leaves puffs behind the pieces another capability throws.";
                case Hits _:             return "Lets the pieces another capability throws deal damage.";
                case LayerPlan _:        return "The named depth slots this recipe's output draws in.";
                case Cues _:             return "Named instants on the recipe's clock — a sound, or something game code listens for.";
            }
            return cap.KindName;
        }

        static ChunkCapability Find(ChunkSpec c, string id)
        {
            var stack = c != null ? c.capabilities : null;
            if (stack == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] != null && stack[i].id == id) return stack[i];
            return null;
        }

        // ── structural edits ──────────────────────────────────────────────────────────────────────────────
        //
        // A stack edit changes the SHAPE of the serialized reference list, not a value inside one entry, so
        // it is recorded as a complete snapshot rather than a diff: the managed-reference ids are what a
        // modifier's target and a card's saved view are keyed by, and a diff-based undo of a list whose
        // entries are managed references is exactly where those ids have been lost before.

        void Add(ChunkSpec c, ChunkCapability cap)
        {
            if (c == null || cap == null) return;
            Undo.RegisterCompleteObjectUndo(c, "Add " + cap.KindName);
            cap.EnsureId();
            c.capabilities ??= new List<ChunkCapability>();
            cap.colorSlot = ChunkCardColors.NextFree(c);
            c.capabilities.Add(cap);
            EditorUtility.SetDirty(c);
            RebuildStack();
            InvalidatePreview();
        }

        /// A copy of one card, right after it, as ONE undo step. The copy gets its own id (so its fold, its
        /// lane and any modifier's target are its own) and its own colour; every value, every picked asset
        /// and every managed reference inside it is copied, not shared. A modifier aimed at the original
        /// stays aimed at the original.
        void Duplicate(ChunkSpec c, ChunkCapability cap)
        {
            var stack = c != null ? c.capabilities : null;
            int at = stack != null ? stack.IndexOf(cap) : -1;
            if (at < 0) return;

            var copy = ChunkCapabilityCopy.Of(cap);
            if (copy == null) return;

            Undo.RegisterCompleteObjectUndo(c, "Duplicate " + cap.KindName);
            copy.id = ChunkCapability.NewId();
            copy.colorSlot = ChunkCardColors.NextFree(c);
            if (!string.IsNullOrEmpty(copy.displayName)) copy.displayName += " copy";
            stack.Insert(at + 1, copy);
            EditorUtility.SetDirty(c);
            RebuildStack();
            InvalidatePreview();
        }

        void Remove(ChunkSpec c, ChunkCapability cap)
        {
            if (c == null || cap == null || c.capabilities == null) return;
            Undo.RegisterCompleteObjectUndo(c, "Remove " + cap.KindName);
            // Pinned first, so the cards that stay keep the colours they had on screen.
            ChunkCardColors.AssignMissing(c);
            c.capabilities.Remove(cap);
            // A modifier pointed at what just left would otherwise keep a target that cannot be reached from
            // any picker — silently applying to nothing, or to everything, depending on which. Clearing it
            // back to "everything compatible" is the state the picker can actually show.
            for (int i = 0; i < c.capabilities.Count; i++)
                if (c.capabilities[i] is ChunkModifier m && m.targetId == cap.id) m.targetId = "";
            EditorUtility.SetDirty(c);
            RebuildStack();
            InvalidatePreview();
        }

        /// A drag-reorder drop: <paramref name="from"/> and <paramref name="to"/> index the cards as they were
        /// SHOWN, which skips any empty entry the stack may hold. The shown order is rewritten into the
        /// stack's non-empty positions, so an empty entry stays where it was rather than shifting under it.
        void MoveShown(ChunkSpec c, List<ChunkCapability> shown, int from, int to)
        {
            var stack = c != null ? c.capabilities : null;
            if (stack == null || shown == null || from < 0 || to < 0 || from >= shown.Count || to >= shown.Count)
                return;

            // The cards on screen must still be the recipe's cards; anything else (an undo landed between the
            // build and the drop) is answered by showing the recipe as it now is.
            int live = 0;
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] != null)
                {
                    if (live >= shown.Count || !ReferenceEquals(stack[i], shown[live])) { RebuildStack(); return; }
                    live++;
                }
            if (live != shown.Count) { RebuildStack(); return; }

            Undo.RegisterCompleteObjectUndo(c, "Reorder Capability");
            // Pinned first: a colour still resolved from stack order would otherwise change with the move.
            ChunkCardColors.AssignMissing(c);
            var order = new List<ChunkCapability>(shown);
            var moved = order[from];
            order.RemoveAt(from);
            order.Insert(to, moved);
            int k = 0;
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] != null) stack[i] = order[k++];
            EditorUtility.SetDirty(c);
            RebuildStack();
            InvalidatePreview();
        }

        // ── the Add menu ──────────────────────────────────────────────────────────────────────────────────

        void ShowAddMenu(ChunkSpec c)
        {
            var menu = Z.Menu(stackHost).Width(240f);
            for (int i = 0; i < Kinds.Length; i++)
            {
                var kind = Kinds[i];
                var probe = kind.make();
                string why = WhyNot(c, probe);
                menu.Item(kind.label, why ?? KindTooltip(probe),
                          () => Add(c, kind.make()), false, why == null, kind.icon);
            }
            menu.Show();
        }

        /// Why this kind cannot be added right now, or null when it can. A modifier decorates what a producer
        /// made, so offering one with nothing to decorate would author a capability that does nothing and
        /// says nothing about why — the reason belongs on the greyed row, where it is read at the moment the
        /// question is asked.
        static string WhyNot(ChunkSpec c, ChunkCapability probe)
        {
            if (probe is Cues && HasKind<Cues>(c))
                return "This recipe already has its cues — a recipe has one list of instants, not several " +
                       "competing ones. Add markers to the Cues card instead.";

            if (probe is LayerPlan && HasKind<LayerPlan>(c))
                return "This recipe already has a layer plan — only the first one would order anything, so a " +
                       "second would be authored and never read. Add slots to the Layer Plan card instead.";

            if (probe is ChunkModifier mod)
            {
                var stack = c != null ? c.capabilities : null;
                if (stack != null)
                    for (int i = 0; i < stack.Count; i++)
                        if (stack[i] != null && mod.CanTarget(stack[i])) return null;

                return mod is Trajectory
                    ? "Nothing to fling yet — a fling moves what a Pyre Blast spawned, so add a Pyre Blast first."
                    : "Nothing to decorate yet — this acts on the pieces a Debris Scatter or a Fragment " +
                      "Fracture throws, so add one of those first.";
            }
            return null;
        }

        static bool HasKind<T>(ChunkSpec c) where T : ChunkCapability
        {
            var stack = c != null ? c.capabilities : null;
            if (stack == null) return false;
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] is T) return true;
            return false;
        }

        // ── rows every card can use ───────────────────────────────────────────────────────────────────────

        /// What this capability is CALLED, and when it fires — the two answers that are about the card itself
        /// rather than about what it produces, so they share the first row of every card.
        ///
        /// The name is a text field because this is where it is DECLARED: a card carries an optional name of
        /// its own, and a recipe with three Pyre Blasts is unreadable until they are "Flash", "Ring" and
        /// "Aftershock". Empty means the kind's own name, which is why the field shows the kind as its
        /// placeholder rather than pre-filling it — typing nothing must not authored a name. It commits on
        /// Enter or blur, since the card's header title is rebuilt from it.
        internal VisualElement IdentityRow(ChunkSpec c, ChunkCapability cap)
        {
            var name = Z.TextInput(cap.displayName,
                "A name of your own for this card. Empty uses the kind's name (" + cap.KindName + ").",
                v =>
                {
                    Dial("Rename Capability", () => cap.displayName = v ?? "");
                    // The header title is written from this, so the stack is rebuilt — one frame late,
                    // because the field raising this callback is one of the elements about to be destroyed.
                    stackHost?.schedule.Execute(RebuildStack).ExecuteLater(0);
                }, 150f);
            name.isDelayed = true;

            var row = Z.Row(Z.Field("Name",
                "A name of your own for this card. Empty uses the kind's name.", name));
            var delay = DelayRow(c, cap);
            if (delay != null) { row.Add(Z.HSpace(6f)); row.Add(delay); }
            return row;
        }

        /// When this capability fires, in seconds from the start of the recipe — or NULL when it has no moment
        /// of its own. A lone capability owns the whole clock, so a Delay dial there would offer to move the
        /// only thing on screen relative to nothing at all: the dial is then built HIDDEN rather than left out,
        /// so its space is already held and the card does not reflow when a second card brings the clock in.
        ///
        /// It is a number rather than a slider on purpose: a delay's ceiling is the clock's own length, which
        /// this dial is one of the things that decides, so any range would clamp exactly the edit that was
        /// meant to extend it. The field scrubs on drag like every other ZUI number.
        internal VisualElement DelayRow(ChunkSpec c, ChunkCapability cap)
        {
            if (cap == null || !cap.OccupiesTime) return null;
            var field = Z.Float(cap.delay,
                "Seconds from the start of the recipe before this fires. You can also drag its band on the " +
                "Timing lanes.",
                v => Dial("Edit Delay", () => cap.delay = Mathf.Max(0f, v)), 70f);
            // Remembered so a band drag on the Timing lanes can move this number with it, live.
            delayFields[cap.EnsureId()] = field;
            var row = Z.Field("Delay",
                "Seconds from the start of the recipe before this fires. Capabilities overlap freely.", field);
            if (!ChunkClock.NeedsTimingSurface(c)) row.style.visibility = Visibility.Hidden;
            return row;
        }

        /// Which named depth slot this capability draws in — or NULL when the recipe has no Layer Plan, in
        /// which case there are no slots to pick from and everything draws in stack order anyway.
        /// `(stack order)` is offered first because it is the honest name for an empty slot: unslotted output
        /// takes the emitter's own draw order plus its place in the stack, which sits IN FRONT of the plan's
        /// slots rather than behind them.
        internal VisualElement LayerSlotRow(ChunkSpec c, Func<string> get, Action<string> set)
        {
            var plan = FirstOfKind<LayerPlan>(c);
            var layers = plan != null && plan.layers != null ? plan.layers.layers : null;
            if (layers == null || layers.Count == 0) return null;

            var options = new string[layers.Count + 1];
            options[0] = "(stack order)";
            for (int i = 0; i < layers.Count; i++) options[i + 1] = layers[i];

            string current = get();
            int index = 0;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] == current) { index = i + 1; break; }

            return Z.Field("Layer",
                "Which named depth slot this draws in. (stack order) leaves it out of the plan: it draws in " +
                "front of every slotted output, in the order the recipe is authored.",
                Z.MiniRadio(index, options,
                    "Which named depth slot this draws in.",
                    i => Dial("Set Layer Slot", () => set(i <= 0 ? "" : layers[i - 1])), true));
        }

        /// Which producer a modifier acts on — `Everything` first, because that is what a modifier with no
        /// target means and what every recipe migrated from the old fixed slots carries.
        internal VisualElement TargetRow(ChunkSpec c, ChunkModifier mod)
        {
            var candidates = new List<ChunkCapability>();
            var stack = c != null ? c.capabilities : null;
            if (stack != null)
                for (int i = 0; i < stack.Count; i++)
                    if (stack[i] != null && mod.CanTarget(stack[i])) candidates.Add(stack[i]);

            var options = new string[candidates.Count + 1];
            options[0] = "Everything";
            for (int i = 0; i < candidates.Count; i++) options[i + 1] = candidates[i].Title;

            int index = 0;
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].id == mod.targetId) { index = i + 1; break; }

            return Z.Field("Target",
                "Which capability this acts on. Everything means every compatible one in the recipe.",
                Z.MiniRadio(index, options, "Which capability this acts on.",
                    i => Dial("Set Modifier Target",
                             () => mod.targetId = i <= 0 ? "" : candidates[i - 1].id), true));
        }

        /// A reference to another asset, as a chip that browses — never a typed name.
        internal VisualElement AssetPicker(UnityEngine.Object current, Action<UnityEngine.Object> onPick,
                                           Type constraint, string suggestedName, string tooltip)
            => LauAssetElement.Build(current, onPick, constraint, thumbCache, suggestedName,
                                     DefaultFolder, tooltip);

        internal static T FirstOfKind<T>(ChunkSpec c) where T : ChunkCapability
        {
            var stack = c != null ? c.capabilities : null;
            if (stack == null) return null;
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] is T t) return t;
            return null;
        }
    }
}
