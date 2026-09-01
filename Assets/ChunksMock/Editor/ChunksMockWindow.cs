// ChunksMockWindow is a disposable UI blueprint. It owns only in-memory mock data: it must not become a
// second editor for ChunkSpec or a hidden route into the production runtime. The barrier is structural —
// this file lives in its own assembly (ChunksMock.Editor) that references ZUI and nothing else, and in its
// own namespace outside the Laubrary.Chunks tree, so a production Chunks type cannot even be named here.
using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChunksMock.Editor
{
    /// <summary>
    /// A deliberately non-production experiment for the Chunks authoring workflow. It answers two questions:
    /// can a recipe be assembled from a STACK of independent capabilities, and does a recipe that needs only
    /// one of them stay free of every surface belonging to the others?
    /// </summary>
    public sealed class ChunksMockWindow : ZuiWindow
    {
        [MenuItem("Laubrary/Chunks Mock (Prototype)")]
        public static void Open()
        {
            var w = GetWindow<ChunksMockWindow>("Chunks Mock");
            w.minSize = new Vector2(820f, 480f);
        }

        // Not serialized on purpose: the mock is disposable, so a domain reload legitimately resets it.
        // EnsureRecipe() is what keeps that from being a null-reference instead of a fresh empty recipe.
        MockRecipe recipe;
        MockCapabilityStage stage;
        MockTimingTracks tracks;

        // Rebuild() recreates the left ScrollView, so the offset has to be carried across by hand or the
        // pane snaps to the top and the button the user just pressed moves out from under the pointer.
        ScrollView leftPane;
        Vector2 carriedScroll;
        float carriedSeconds;

        protected override void BuildUI(VisualElement root)
        {
            EnsureRecipe();
            root.style.minHeight = 0f;

            // 460pt is not a taste call: the Layer Plan's row (name · opacity · blend · reorder · remove) is
            // the widest thing this window can hold, and it WRAPS below this — measured, not guessed.
            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 460f;
            left.style.minHeight = 0f;
            BuildControls(left.contentContainer);
            leftPane = left;
            RestoreScroll(left);

            var right = new VisualElement();
            right.style.minWidth = 280f;
            right.style.minHeight = 0f;
            BuildWorkspace(right);

            root.Add(Z.Split("chunks.mock.prototype.split.v2", 480f, left, right));
        }

        void EnsureRecipe()
        {
            if (recipe == null) recipe = new MockRecipe { name = "Untitled Chunk" };
        }

        /// The last thing that runs before the root is cleared, so it is the only place the outgoing
        /// ScrollView's offset can still be read.
        protected override void OnBeforeRebuild()
        {
            if (leftPane != null) carriedScroll = leftPane.scrollOffset;
            if (tracks != null) carriedSeconds = tracks.Seconds;
            leftPane = null;
        }

        /// A ScrollView clamps its offset against its CONTENT height, which is unknown until the panel has
        /// laid out - so the restore has to wait for the first geometry pass and then get out of the way.
        void RestoreScroll(ScrollView view)
        {
            if (carriedScroll == Vector2.zero) return;
            Vector2 wanted = carriedScroll;
            EventCallback<GeometryChangedEvent> once = null;
            once = _ =>
            {
                view.contentContainer.UnregisterCallback(once);
                view.scrollOffset = wanted;
            };
            view.contentContainer.RegisterCallback(once);
        }

        // ── left pane: recipe identity, then the capability stack ──────────────────────────────────────

        void BuildControls(VisualElement root)
        {
            Button recipes = null;
            recipes = Z.Button("Recipes…", "Open a different mock recipe, or start an empty one.",
                () => ShowRecipeMenu(recipes)).W(96f);
            root.Add(Z.HGroup(
                Z.Field("Recipe", "This mock recipe's name. It exists only while this window is open.",
                    Z.TextInput(recipe.name, "This mock recipe's name. It exists only while this window is open.",
                        v => recipe.name = v, 180f)),
                recipes));

            var capabilities = Z.Section("Capabilities",
                "The ordered stack of capabilities in this mock recipe. A capability that is not in the stack has no UI here at all.",
                "chunks.mock.capabilities", icon: "stack");

            foreach (var cap in recipe.capabilities) capabilities.Add(BuildCapabilityCard(cap));

            Button add = null;
            add = Z.Button("Add capability…", "Choose a capability kind to append to this mock recipe.",
                () => ShowCapabilityMenu(add)).W(140f);
            capabilities.Add(add);

            root.Add(capabilities);
        }

        /// One card per capability. The header carries identity plus the two controls every card has — the
        /// enable toggle and the remove × — so the body holds nothing but that capability's own fields.
        VisualElement BuildCapabilityCard(MockCapability cap)
        {
            var card = Z.BoxKeyed(cap.DisplayName, cap.Tooltip, "chunks.mock.cap." + cap.Id, icon: cap.Icon);

            card.AddHeaderContent(Z.ToggleButton("On",
                cap.Enabled
                    ? "Disable this capability. It stays in the recipe but contributes nothing to the guide or the timing."
                    : "Enable this capability so it contributes to the spatial guide and to the timing.",
                cap.Enabled, v => { cap.Enabled = v; Rebuild(); }).W(44f));
            card.AddHeaderContent(Z.Button("×", "Remove this capability from the mock recipe.",
                () => { recipe.capabilities.Remove(cap); Rebuild(); }).W(24f));

            switch (cap)
            {
                case MockFormation f: BuildFormation(card, f); break;
                case MockLayerPlan l: BuildLayerPlan(card, l); break;
                case MockPaletteSplash s: BuildPaletteSplash(card, s); break;
                case MockParticleBurst b: BuildParticleBurst(card, b); break;
            }

            // The fold caret hides the card; THIS is what turns the capability off. A disabled card's body
            // is inert, so nothing in it can be dialled while it contributes nothing.
            card.contentContainer.SetEnabled(cap.Enabled);
            return card;
        }

        void BuildFormation(VisualElement card, MockFormation f)
        {
            Button pyre = null;
            pyre = Z.Button(f.pyreName + "…",
                "Choose the mock Pyre this formation repeats. These names are placeholders, not asset references.",
                () => ShowPyreMenu(pyre, f)).W(170f);

            // Two choices - WHAT is repeated and in WHAT arrangement - on one row: they answer the same
            // question and neither needs a row to itself.
            card.Add(Z.HGroup(
                Z.Field("Pyre", "The mock Pyre repeated by this formation.", pyre),
                Z.Field("Pattern", "The spatial arrangement of the repeated spawns.",
                    Z.Segmented((int)f.pattern, new[] { "Line", "Ring" },
                        "Line spaces the spawns along one direction. Ring distributes them evenly around the origin.",
                        v => { f.pattern = (MockPattern)v; Rebuild(); }))));

            var shape = f.pattern == MockPattern.Line
                ? Z.MicroSlider("Direction °", f.direction, 0f, 360f,
                    "The line's direction in the spatial guide. 0 is right and 90 is up.",
                    v => Change(() => f.direction = v), width: 130f, decimals: 0)
                : Z.MicroSlider("Radius", f.radius, 0.25f, 5f, "The ring's radius in the spatial guide.",
                    v => Change(() => f.radius = v), width: 130f, decimals: 2);

            card.Add(Z.HGroup(
                Z.MicroSlider("Count", f.count, 1f, 12f, "How many times this mock Pyre is spawned.",
                    v => Change(() => f.count = Mathf.RoundToInt(v)), width: 130f, decimals: 0),
                Z.MicroSlider("Stagger", f.stagger, 0f, 1f,
                    "Seconds between successive spawns. Zero means every spawn starts together.",
                    v => Change(() => f.stagger = v), width: 130f, decimals: 2),
                shape));

            AddTimingRow(card, f);
        }

        void BuildLayerPlan(VisualElement card, MockLayerPlan plan)
        {
            for (int i = 0; i < plan.layers.Count; i++)
            {
                int index = i;
                var layer = plan.layers[i];

                var up = Z.Button("▲", "Move this layer one place earlier in the plan.",
                    () => MoveLayer(plan, index, -1)).W(20f);
                up.SetEnabled(index > 0);
                var down = Z.Button("▼", "Move this layer one place later in the plan.",
                    () => MoveLayer(plan, index, 1)).W(20f);
                down.SetEnabled(index < plan.layers.Count - 1);

                // No flexible gap: this row is already at the pane's width, and a flexible gap in a row that
                // can wrap is exactly what pushes the trailing buttons onto a line of their own.
                card.Add(Z.HGroup(
                    Z.TextInput(layer.name, "This layer's name. It is declared here and picked nowhere else in the mock.",
                        v => layer.name = v, 84f),
                    Z.MicroSlider("Opacity", layer.opacity, 0f, 1f, "How strongly this layer reads in the composite.",
                        v => layer.opacity = v, width: 92f, decimals: 2),
                    Z.Segmented(layer.blend, new[] { "Normal", "Add", "Multiply" },
                        "How this layer combines with the layers beneath it.", v => layer.blend = v),
                    up, down,
                    Z.Button("×", "Remove this layer from the plan.",
                        () => { plan.layers.RemoveAt(index); Rebuild(); }).W(20f)));
            }

            card.Add(Z.Button("Add layer", "Append a new layer to the bottom of this plan.",
                // Numbered, because two rows both called "New Layer" are indistinguishable in a plan of five.
                () => { plan.layers.Add(new MockLayer { name = "Layer " + (plan.layers.Count + 1) }); Rebuild(); })
                .W(100f));
        }

        void BuildPaletteSplash(VisualElement card, MockPaletteSplash splash)
        {
            Button palette = null;
            palette = Z.Button(splash.palette + "…",
                "Choose the mock palette this splash scatters. These names are placeholders, not asset references.",
                () => ShowPaletteMenu(palette, splash)).W(140f);

            card.Add(Z.HGroup(
                Z.Field("Palette", "The mock palette this splash scatters.", palette),
                Z.MicroSlider("Swatches", splash.swatches, 1f, 8f, "How many colours the splash throws out.",
                    v => Change(() => splash.swatches = Mathf.RoundToInt(v)), width: 130f, decimals: 0)));

            AddTimingRow(card, splash,
                Z.MicroSlider("Spread", splash.spread, 0f, 1f, "How far the swatches land from the origin.",
                    v => Change(() => splash.spread = v), width: 130f, decimals: 2));
        }

        void BuildParticleBurst(VisualElement card, MockParticleBurst burst)
        {
            card.Add(Z.HGroup(
                Z.Field("Shape", "The volume the particles are emitted into.",
                    Z.Segmented((int)burst.shape, new[] { "Cone", "Disc", "Sphere" },
                        "Cone emits into an arc, Disc into the whole plane, Sphere into a volume.",
                        v => { burst.shape = (MockBurstShape)v; Rebuild(); })),
                Z.MicroSlider("Count", burst.count, 1f, 200f, "How many particles the burst emits.",
                    v => Change(() => burst.count = Mathf.RoundToInt(v)), width: 130f, decimals: 0)));

            card.Add(Z.HGroup(
                Z.MicroSlider("Speed", burst.speed, 0f, 20f, "How fast the particles leave the origin.",
                    v => Change(() => burst.speed = v), width: 130f, decimals: 1),
                Z.MicroSlider("Lifetime", burst.lifetime, 0.05f, 3f, "Seconds each particle survives.",
                    v => Change(() => burst.lifetime = v), width: 130f, decimals: 2),
                Z.MicroSlider("Spread °", burst.spread, 0f, 180f, "The emission arc's opening angle.",
                    v => Change(() => burst.spread = v), width: 130f, decimals: 0)));

            AddTimingRow(card, burst);
        }

        /// The dials that place a capability on the recipe's shared clock, packed onto a row with whatever
        /// `leading` controls the card passes. They appear only once the recipe holds a SECOND timed
        /// capability: with one, there is nothing to be early or late relative to and no clock is drawn, so
        /// a Delay would dial something the window cannot show. A capability that occupies no time never
        /// shows them at all, which is why a Layer Plan's card has no timing row.
        void AddTimingRow(VisualElement card, MockCapability cap, params VisualElement[] leading)
        {
            var row = new List<VisualElement>(leading);
            if (cap.Timed && TimedCapabilities().Count > 1)
            {
                row.Add(Z.MicroSlider("Delay", cap.delay, 0f, 2f,
                    "Seconds from the start of the recipe until this capability begins. Capabilities may overlap.",
                    v => Change(() => cap.delay = v), width: 130f, decimals: 2));
                row.Add(Z.MicroSlider("Duration", cap.duration, 0.05f, 3f, "Seconds this capability occupies.",
                    v => Change(() => cap.duration = v), width: 130f, decimals: 2));
            }
            if (row.Count > 0) card.Add(Z.HGroup(row.ToArray()));
        }

        // ── right pane: the workspace, and the ONE conditional shared timeline beneath it ───────────────

        void BuildWorkspace(VisualElement root)
        {
            var guide = Z.Section("Spatial guide",
                "A fast placement guide for the enabled capabilities. It is not a Mirage-quality visual preview.",
                "chunks.mock.spatial-guide", icon: "arrows-out-cardinal");
            // flexGrow on the stage alone is not enough — a section and its body are the two elements
            // between it and the pane, and neither grows by default, so the stage resolves to its
            // minHeight and leaves bare grey beneath it.
            guide.style.flexGrow = 1f;
            guide.style.minHeight = 0f;
            guide.contentContainer.style.flexGrow = 1f;
            guide.contentContainer.style.minHeight = 0f;

            stage = new MockCapabilityStage(recipe);
            stage.style.flexGrow = 1f;
            stage.style.minHeight = 200f;
            guide.Add(stage);
            root.Add(guide);

            // The clock is SHARED, so it only means anything once two capabilities compete for time.
            // Below the workspace, never above it: appearing must not shove the guide down the pane.
            tracks = null;
            if (TimedCapabilities().Count < 2) return;

            var timing = Z.Section("Timing",
                "When each time-occupying capability runs, on one shared clock. Drag the playhead to read the recipe.",
                "chunks.mock.timing", icon: "timer");
            tracks = new MockTimingTracks(recipe, carriedSeconds);
            timing.Add(tracks);
            root.Add(timing);
            tracks.Refresh();
        }

        /// Every capability that occupies time, ENABLED OR NOT. Deliberately unfiltered by Enabled: the
        /// timing surface's presence and its lane count must not change when a capability is toggled, or the
        /// spatial guide above it resizes and re-centres the picture the user is reading.
        List<MockCapability> TimedCapabilities()
        {
            EnsureRecipe();
            var list = new List<MockCapability>();
            foreach (var cap in recipe.capabilities)
                if (cap.Timed) list.Add(cap);
            return list;
        }

        /// A value-only edit repaints the guide and re-bands the clock; it never rebuilds the window,
        /// so a slider drag cannot pull the control out from under the pointer.
        void Change(Action edit)
        {
            edit?.Invoke();
            stage?.MarkDirtyRepaint();
            tracks?.Refresh();
        }

        // ── menus ──────────────────────────────────────────────────────────────────────────────────────

        void ShowRecipeMenu(VisualElement anchor)
        {
            Z.Menu(anchor)
                .Section("Mock recipes")
                .Item("Untitled Chunk", "Open the empty mock recipe.",
                    () => LoadRecipe("Untitled Chunk"), recipe.name == "Untitled Chunk")
                .Item("Crate Smash", "Open a mock recipe with a layer plan, a formation and a particle burst.",
                    () => LoadRecipe("Crate Smash"), recipe.name == "Crate Smash")
                .Item("Barrel Pop", "Open a mock recipe that needs one formation and nothing else.",
                    () => LoadRecipe("Barrel Pop"), recipe.name == "Barrel Pop")
                .Separator()
                .Item("New empty recipe", "Discard this mock recipe and start an empty one.",
                    () => LoadRecipe("Untitled Chunk"))
                .Show();
        }

        void ShowCapabilityMenu(VisualElement anchor)
        {
            Z.Menu(anchor)
                .Section("Capability kinds")
                .Item("Pyre Formation", "Repeat one mock Pyre at a line or ring of spawn positions.",
                    () => AddCapability(new MockFormation()))
                .Item("Layer Plan", "Stack named layers with their own opacity and blend.",
                    () => AddCapability(new MockLayerPlan()))
                .Item("Palette Splash", "Throw a mock palette's colours out from the origin.",
                    () => AddCapability(new MockPaletteSplash()))
                .Item("Generic Particle Burst", "Emit plain particles — no Pyre, no sprite sheet.",
                    () => AddCapability(new MockParticleBurst()))
                .Show();
        }

        void ShowPyreMenu(VisualElement anchor, MockFormation f)
        {
            var menu = Z.Menu(anchor).Section("Mock Pyres");
            foreach (string option in MockPyres)
            {
                string pick = option;
                menu.Item(pick, "Repeat the " + pick + " mock Pyre.",
                    () => { f.pyreName = pick; Rebuild(); }, f.pyreName == pick);
            }
            menu.Show();
        }

        void ShowPaletteMenu(VisualElement anchor, MockPaletteSplash splash)
        {
            var menu = Z.Menu(anchor).Section("Mock palettes");
            foreach (string option in MockPalettes)
            {
                string pick = option;
                menu.Item(pick, "Splash the " + pick + " mock palette.",
                    () => { splash.palette = pick; Rebuild(); }, splash.palette == pick);
            }
            menu.Show();
        }

        // ── mutations ──────────────────────────────────────────────────────────────────────────────────

        void AddCapability(MockCapability cap)
        {
            recipe.capabilities.Add(cap);
            Rebuild();
        }

        void MoveLayer(MockLayerPlan plan, int index, int delta)
        {
            int target = index + delta;
            if (target < 0 || target >= plan.layers.Count) return;
            var moved = plan.layers[index];
            plan.layers.RemoveAt(index);
            plan.layers.Insert(target, moved);
            Rebuild();
        }

        /// "Barrel Pop" exists to be the counter-example: one capability, so no shared timeline, no layer
        /// plan, no palette surface and no particle surface anywhere in the window.
        void LoadRecipe(string name)
        {
            recipe = new MockRecipe { name = name };
            if (name == "Crate Smash")
            {
                var plan = new MockLayerPlan();
                plan.layers.Add(new MockLayer { name = "Splinters" });
                plan.layers.Add(new MockLayer { name = "Dust", opacity = 0.65f, blend = 1 });
                recipe.capabilities.Add(plan);
                recipe.capabilities.Add(new MockFormation { pattern = MockPattern.Ring, count = 8 });
                recipe.capabilities.Add(new MockParticleBurst { delay = 0.15f, duration = 0.8f });
            }
            else if (name == "Barrel Pop")
            {
                recipe.capabilities.Add(new MockFormation());
            }
            Rebuild();
        }

        static readonly string[] MockPyres = { "Green Lantern", "Sparkle Burst", "Smoke Puff" };
        static readonly string[] MockPalettes = { "Ember", "Ash", "Verdigris", "Bone" };

        // ── mock data (in-memory only; never serialized, never written to disk) ────────────────────────

        enum MockPattern { Line, Ring }

        enum MockBurstShape { Cone, Disc, Sphere }

        sealed class MockRecipe
        {
            public string name;
            public readonly List<MockCapability> capabilities = new List<MockCapability>();
        }

        abstract class MockCapability
        {
            /// Unique per instance so two cards of the same kind fold independently.
            public readonly string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            public bool Enabled = true;
            public float delay;
            public float duration = 0.5f;

            public abstract string DisplayName { get; }
            public abstract string Tooltip { get; }
            public abstract string Icon { get; }
            /// Whether this capability occupies a stretch of the recipe's shared timeline.
            public abstract bool Timed { get; }
            public abstract Color BandColor { get; }
        }

        sealed class MockFormation : MockCapability
        {
            public string pyreName = "Green Lantern";
            public int count = 5;
            public MockPattern pattern = MockPattern.Line;
            public float stagger = 0.12f;
            public float direction;
            public float radius = 2f;

            public override string DisplayName => "Pyre Formation";
            public override string Tooltip => "Repeats one mock Pyre at several authored positions.";
            public override string Icon => "shapes";
            public override bool Timed => true;
            public override Color BandColor => new Color(0.95f, 0.5f, 0.2f, 1f);
        }

        sealed class MockLayerPlan : MockCapability
        {
            public readonly List<MockLayer> layers = new List<MockLayer>();

            public override string DisplayName => "Layer Plan";
            public override string Tooltip => "Stacks named layers, bottom of the list on top of the composite.";
            public override string Icon => "stack-simple";
            public override bool Timed => false;
            public override Color BandColor => new Color(0.55f, 0.55f, 0.6f, 1f);
        }

        sealed class MockLayer
        {
            public string name;
            public float opacity = 1f;
            public int blend;
        }

        sealed class MockPaletteSplash : MockCapability
        {
            public string palette = "Ember";
            public int swatches = 4;
            public float spread = 0.5f;

            public override string DisplayName => "Palette Splash";
            public override string Tooltip => "Throws a mock palette's colours out from the origin.";
            public override string Icon => "palette";
            public override bool Timed => true;
            public override Color BandColor => new Color(0.45f, 0.85f, 0.55f, 1f);
        }

        sealed class MockParticleBurst : MockCapability
        {
            public MockBurstShape shape = MockBurstShape.Cone;
            public int count = 40;
            public float speed = 8f;
            public float lifetime = 0.8f;
            public float spread = 60f;

            public override string DisplayName => "Generic Particle Burst";
            public override string Tooltip => "Emits plain particles — deliberately not a Pyre and not a sprite sheet.";
            public override string Icon => "sparkle";
            public override bool Timed => true;
            public override Color BandColor => new Color(0.35f, 0.7f, 1f, 1f);
        }

        /// <summary>The recipe's clock: one LANE per time-occupying capability, every lane on the SAME
        /// absolute ruler, so two capabilities that overlap are drawn overlapping and a Delay of 0.15s starts
        /// its band at 0.15s. This is deliberately not <c>Z.Timeline</c>: that control is by contract a single
        /// strip of CONSECUTIVE bands, which can only express a chain — and a chunk's sparks and its debris
        /// fire together, so a chain is the wrong model. Hand-rolled here because the mock is disposable; the
        /// blueprint carries the multi-lane control as a ZUI requirement rather than leaving it in one
        /// window. A disabled capability keeps its lane, drawn dim: losing the lane would change this
        /// surface's height and move the guide above it.</summary>
        sealed class MockTimingTracks : VisualElement
        {
            const float LaneHeight = 16f;
            const float LaneGap = 3f;
            const float RulerHeight = 14f;
            const float TickFontSize = 9f;
            // Below this, a band cannot hold a legible name; the colour and the tooltip still identify it.
            const float MinBandForName = 52f;

            static readonly Color Track = new Color(0.14f, 0.15f, 0.18f, 1f);
            static readonly Color Playhead = new Color(1f, 0.85f, 0.3f, 0.95f);

            readonly MockRecipe recipe;
            readonly VisualElement bar;    // the painted lanes; local x IS the time axis
            readonly VisualElement ruler;  // the tick numbers and the playhead's own readout
            readonly Label playLabel;
            readonly List<Label> tickLabels = new List<Label>();
            readonly List<Label> bandLabels = new List<Label>();
            readonly List<MockCapability> lanes = new List<MockCapability>();

            float total = 1f;
            float seconds;
            bool dragging;
            float laidOutWidth = -1f;

            /// Where the playhead is. The host reads it back before a rebuild and hands it to the next
            /// instance, so toggling a capability does not throw the playhead back to zero.
            public float Seconds => seconds;

            public MockTimingTracks(MockRecipe recipe, float seconds)
            {
                this.recipe = recipe;
                this.seconds = Mathf.Max(0f, seconds);
                tooltip = "One lane per time-occupying capability, all on the same clock. A lane starts at its capability's Delay and runs for its Duration, so lanes that overlap run together. Click or drag anywhere to move the playhead.";
                style.flexDirection = FlexDirection.Column;
                style.flexGrow = 1f;
                style.minWidth = 180f;

                bar = new VisualElement();
                bar.style.flexGrow = 0f;
                bar.style.flexShrink = 0f;
                bar.style.overflow = Overflow.Hidden;
                bar.generateVisualContent += Paint;
                Add(bar);

                ruler = new VisualElement();
                ruler.style.height = RulerHeight;
                ruler.style.flexGrow = 0f;
                ruler.style.flexShrink = 0f;
                ruler.pickingMode = PickingMode.Ignore;   // the numbers are a ruler, not a second hit target
                Add(ruler);

                playLabel = new Label("0.00s") { pickingMode = PickingMode.Ignore };
                playLabel.style.position = Position.Absolute;
                playLabel.style.top = 0f;
                playLabel.style.fontSize = TickFontSize;
                playLabel.style.unityTextAlign = TextAnchor.UpperCenter;
                playLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                playLabel.style.color = new Color(1f, 1f, 1f, 0.95f);
                ruler.Add(playLabel);

                // The gestures live on the ROOT so a press anywhere in the control scrubs, including the
                // ruler — a ruler you cannot click reads as broken. x is always resolved against the BAR.
                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
                RegisterCallback<GeometryChangedEvent>(OnGeometry);
            }

            /// Re-read the recipe: which lanes exist, how long the clock is, and where every band sits.
            public void Refresh()
            {
                lanes.Clear();
                total = 0f;
                if (recipe != null)
                {
                    foreach (var cap in recipe.capabilities)
                    {
                        if (!cap.Timed) continue;
                        lanes.Add(cap);
                        // Disabled lanes count towards the length too, or toggling one off rescales the
                        // ruler under the user and every remaining band jumps sideways.
                        total = Mathf.Max(total, cap.delay + cap.duration);
                    }
                }
                if (total <= 0.01f) total = 1f;
                seconds = Mathf.Clamp(seconds, 0f, total);

                bar.style.height = Mathf.Max(LaneHeight, lanes.Count * (LaneHeight + LaneGap) - LaneGap);
                Relayout();
            }

            float BarWidth
            {
                get
                {
                    float w = bar.contentRect.width;
                    return float.IsNaN(w) ? 0f : w;
                }
            }

            float X(float t) => total > 0f ? Mathf.Clamp01(t / total) * BarWidth : 0f;

            float T(float x) => total > 0f ? Mathf.Clamp01(x / Mathf.Max(1f, BarWidth)) * total : 0f;

            void OnDown(PointerDownEvent e)
            {
                if (e.button != 0) return;
                dragging = true;
                this.CapturePointer(e.pointerId);
                Scrub(e.position);
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (!dragging) return;
                Scrub(e.position);
                e.StopPropagation();
            }

            void OnUp(PointerUpEvent e)
            {
                if (!dragging) return;
                dragging = false;
                this.ReleasePointer(e.pointerId);
                e.StopPropagation();
            }

            /// Resolved against the BAR, not the event target: a pointer that lands on an absolutely
            /// positioned band label would otherwise report x in that label's own space.
            void Scrub(Vector2 panelPosition)
            {
                seconds = T(bar.WorldToLocal(panelPosition).x);
                PlacePlayhead();
                bar.MarkDirtyRepaint();
            }

            /// Every label position is a function of the bar's width, unknown until the panel lays out. The
            /// width guard is load-bearing: GeometryChangedEvent bubbles, so the labels this creates would
            /// otherwise re-enter it forever.
            void OnGeometry(GeometryChangedEvent _)
            {
                float w = BarWidth;
                if (Mathf.Abs(w - laidOutWidth) < 0.5f) return;
                laidOutWidth = w;
                Relayout();
            }

            void Relayout()
            {
                PlaceBandLabels();
                BuildTicks();
                PlacePlayhead();
                bar.MarkDirtyRepaint();
            }

            void PlaceBandLabels()
            {
                foreach (var l in bandLabels) l.RemoveFromHierarchy();
                bandLabels.Clear();
                if (BarWidth < 1f) return;

                for (int i = 0; i < lanes.Count; i++)
                {
                    var cap = lanes[i];
                    float left = X(cap.delay);
                    float width = X(cap.delay + cap.duration) - left;
                    if (width < MinBandForName) continue;

                    var l = new Label(cap.DisplayName) { pickingMode = PickingMode.Ignore };
                    l.style.position = Position.Absolute;
                    l.style.left = left + 4f;
                    l.style.top = i * (LaneHeight + LaneGap) + 1f;
                    l.style.width = width - 8f;
                    l.style.fontSize = 9f;
                    l.style.overflow = Overflow.Hidden;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    l.style.textOverflow = TextOverflow.Ellipsis;
                    l.style.color = new Color(0.06f, 0.06f, 0.08f, cap.Enabled ? 0.95f : 0.5f);
                    l.style.unityFontStyleAndWeight = FontStyle.Bold;
                    bar.Add(l);
                    bandLabels.Add(l);
                }
            }

            /// The ruler. A tick whose text would sit under the playhead's own readout is DROPPED rather
            /// than drawn behind it — a number printed on top of another number is worse than no number.
            void BuildTicks()
            {
                foreach (var l in tickLabels) l.RemoveFromHierarchy();
                tickLabels.Clear();
                float w = BarWidth;
                if (w < 40f) return;

                float step = NiceStep(total, w);
                float playLeft = X(seconds) - 22f;
                float playRight = X(seconds) + 22f;

                for (float t = 0f; t <= total + 0.0001f; t += step)
                {
                    float x = X(t);
                    if (x > playLeft && x < playRight) continue;   // occluded by the playhead readout
                    var l = new Label(t.ToString("0.00") + "s") { pickingMode = PickingMode.Ignore };
                    l.style.position = Position.Absolute;
                    l.style.top = 0f;
                    // Pulled inside the bar rather than centred on its tick: the first and last numbers are
                    // the ones a reader needs most, and half of "0.00s" hanging off the edge reads as ".00s".
                    l.style.left = Mathf.Clamp(x - 20f, 0f, Mathf.Max(0f, w - 40f));
                    l.style.width = 40f;
                    l.style.fontSize = TickFontSize;
                    l.style.unityTextAlign = TextAnchor.UpperCenter;
                    l.style.color = new Color(1f, 1f, 1f, 0.42f);
                    ruler.Add(l);
                    tickLabels.Add(l);
                }
            }

            /// A step from the 1/2/5 family that lands about five ticks on the bar, so the ruler never
            /// prints numbers closer together than they can be read.
            static float NiceStep(float span, float width)
            {
                float raw = span / Mathf.Max(2f, width / 70f);
                float[] steps = { 0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1f, 2f, 5f };
                foreach (float s in steps) if (raw <= s) return s;
                return 10f;
            }

            void PlacePlayhead()
            {
                playLabel.text = seconds.ToString("0.00") + "s";
                playLabel.style.left = Mathf.Clamp(X(seconds) - 22f, 0f, Mathf.Max(0f, BarWidth - 44f));
                playLabel.style.width = 44f;
                BuildTicks();
            }

            void Paint(MeshGenerationContext mgc)
            {
                var view = bar.contentRect;
                if (view.width < 10f || view.height < 4f) return;
                var p = mgc.painter2D;

                for (int i = 0; i < lanes.Count; i++)
                {
                    var cap = lanes[i];
                    float top = i * (LaneHeight + LaneGap);
                    Fill(p, new Rect(0f, top, view.width, LaneHeight), Track);

                    float left = X(cap.delay);
                    float width = Mathf.Max(2f, X(cap.delay + cap.duration) - left);
                    var c = cap.BandColor;
                    if (!cap.Enabled) c = new Color(c.r, c.g, c.b, 0.22f);
                    Fill(p, new Rect(left, top, width, LaneHeight), c);
                }

                float x = X(seconds);
                Fill(p, new Rect(x - 1f, 0f, 2f, view.height), Playhead);
            }

            static void Fill(Painter2D p, Rect rect, Color color)
            {
                p.fillColor = color;
                p.BeginPath();
                p.MoveTo(new Vector2(rect.x, rect.y));
                p.LineTo(new Vector2(rect.xMax, rect.y));
                p.LineTo(new Vector2(rect.xMax, rect.yMax));
                p.LineTo(new Vector2(rect.x, rect.yMax));
                p.ClosePath();
                p.Fill();
            }
        }

        /// <summary>A custom Painter2D stage is the sanctioned raw island: it visualises placement, not a UI
        /// control ZUI provides. It draws only the ENABLED capabilities, which is what makes the enable
        /// toggle mean something instead of merely folding a card.</summary>
        sealed class MockCapabilityStage : VisualElement
        {
            readonly MockRecipe recipe;

            static readonly Color Grid = new Color(1f, 1f, 1f, 0.08f);
            static readonly Color Origin = new Color(1f, 0.85f, 0.3f, 0.95f);
            static readonly Color First = new Color(0.35f, 0.8f, 1f, 1f);
            static readonly Color Last = new Color(1f, 0.45f, 0.35f, 1f);
            static readonly Color Rail = new Color(0.6f, 0.75f, 0.9f, 0.35f);

            public MockCapabilityStage(MockRecipe recipe)
            {
                this.recipe = recipe;
                AddToClassList("zui-stage");
                style.overflow = Overflow.Hidden;
                tooltip = "Rough spatial guide for the enabled capabilities. On a formation, blue is the first spawn and orange the last, and the small arrows show their order.";
                generateVisualContent += Paint;
            }

            void Paint(MeshGenerationContext mgc)
            {
                var view = contentRect;
                if (view.width < 10f || view.height < 10f) return;

                var p = mgc.painter2D;
                Fill(p, view, new Color(0.04f, 0.05f, 0.07f, 1f));
                DrawGrid(p, view);
                Vector2 origin = view.center;
                Cross(p, origin, 9f, Origin, 1.5f);

                if (recipe == null) return;
                foreach (var cap in recipe.capabilities)
                {
                    if (!cap.Enabled) continue;
                    switch (cap)
                    {
                        case MockFormation f: DrawFormation(p, view, origin, f); break;
                        case MockPaletteSplash s: DrawSplash(p, origin, s); break;
                        case MockParticleBurst b: DrawBurst(p, view, origin, b); break;
                        // A Layer Plan is not spatial, so it deliberately draws nothing at all.
                    }
                }
            }

            static void DrawFormation(Painter2D p, Rect view, Vector2 origin, MockFormation f)
            {
                int count = Mathf.Max(1, f.count);
                float extent = Mathf.Min(view.width, view.height) * 0.32f;
                var positions = new Vector2[count];

                if (f.pattern == MockPattern.Line)
                {
                    float radians = f.direction * Mathf.Deg2Rad;
                    var axis = new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
                    for (int i = 0; i < count; i++)
                    {
                        float t = count == 1 ? 0f : i / (float)(count - 1);
                        positions[i] = origin + axis * Mathf.Lerp(-extent, extent, t);
                    }
                    Line(p, positions[0], positions[count - 1], Rail, 1f);
                }
                else
                {
                    float r = Mathf.Max(10f, extent * Mathf.Clamp01(f.radius / 5f));
                    DrawCircle(p, origin, r, Rail);
                    for (int i = 0; i < count; i++)
                    {
                        float a = i / (float)count * Mathf.PI * 2f - Mathf.PI * 0.5f;
                        positions[i] = origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    float t = count == 1 ? 0f : i / (float)(count - 1);
                    var c = Color.Lerp(First, Last, t);
                    Dot(p, positions[i], 12f, c);
                    if (i > 0) Arrow(p, positions[i - 1], positions[i], new Color(c.r, c.g, c.b, 0.55f));
                }
            }

            static void DrawSplash(Painter2D p, Vector2 origin, MockPaletteSplash s)
            {
                int count = Mathf.Max(1, s.swatches);
                float r = Mathf.Lerp(14f, 78f, Mathf.Clamp01(s.spread));
                for (int i = 0; i < count; i++)
                {
                    float a = i / (float)count * Mathf.PI * 2f;
                    var at = origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    var c = Color.Lerp(s.BandColor, Color.white, i / (float)count * 0.45f);
                    Fill(p, new Rect(at.x - 4f, at.y - 4f, 8f, 8f), c);
                }
            }

            static void DrawBurst(Painter2D p, Rect view, Vector2 origin, MockParticleBurst b)
            {
                int rays = Mathf.Clamp(b.count, 1, 40);
                float length = Mathf.Lerp(8f, Mathf.Min(view.width, view.height) * 0.4f, Mathf.Clamp01(b.speed / 20f));
                var c = b.BandColor;

                switch (b.shape)
                {
                    case MockBurstShape.Cone:
                        Rays(p, origin, rays, -b.spread * 0.5f, b.spread, length, c);
                        break;
                    case MockBurstShape.Disc:
                        Rays(p, origin, rays, 0f, 360f, length, c);
                        break;
                    default:
                        // Two shorter inner fans behind the outer one read as depth, so a Sphere does not
                        // look identical to a Disc in a flat guide.
                        Rays(p, origin, rays, 0f, 360f, length, c);
                        Rays(p, origin, Mathf.Max(6, rays / 2), 15f, 360f, length * 0.65f,
                            new Color(c.r, c.g, c.b, 0.7f));
                        Rays(p, origin, Mathf.Max(5, rays / 3), 30f, 360f, length * 0.35f,
                            new Color(c.r, c.g, c.b, 0.45f));
                        break;
                }
            }

            static void Rays(Painter2D p, Vector2 origin, int count, float startDegrees, float rangeDegrees,
                float length, Color color)
            {
                for (int i = 0; i < count; i++)
                {
                    float t = count == 1 ? 0.5f : i / (float)(count - 1);
                    float a = (startDegrees + rangeDegrees * t) * Mathf.Deg2Rad;
                    Line(p, origin, origin + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * length, color, 1.2f);
                }
            }

            static void DrawGrid(Painter2D p, Rect r)
            {
                for (float x = r.x + 20f; x < r.xMax; x += 20f) Line(p, new Vector2(x, r.y), new Vector2(x, r.yMax), Grid, 1f);
                for (float y = r.y + 20f; y < r.yMax; y += 20f) Line(p, new Vector2(r.x, y), new Vector2(r.xMax, y), Grid, 1f);
            }

            static void DrawCircle(Painter2D p, Vector2 center, float radius, Color color)
            {
                const int Steps = 48;
                var points = new Vector2[Steps + 1];
                for (int i = 0; i <= Steps; i++)
                {
                    float a = i / (float)Steps * Mathf.PI * 2f;
                    points[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                }
                Poly(p, points, color, 1f);
            }

            static void Arrow(Painter2D p, Vector2 from, Vector2 to, Color color)
            {
                Vector2 delta = to - from;
                if (delta.sqrMagnitude < 400f) return;
                Vector2 dir = delta.normalized;
                Vector2 tip = Vector2.Lerp(from, to, 0.72f);
                Vector2 side = new Vector2(-dir.y, dir.x);
                Line(p, from, tip, color, 1.2f);
                Line(p, tip, tip - dir * 8f + side * 4f, color, 1.2f);
                Line(p, tip, tip - dir * 8f - side * 4f, color, 1.2f);
            }

            static void Cross(Painter2D p, Vector2 center, float size, Color color, float width)
            {
                Line(p, center + Vector2.left * size, center + Vector2.right * size, color, width);
                Line(p, center + Vector2.up * size, center + Vector2.down * size, color, width);
            }

            static void Dot(Painter2D p, Vector2 center, float size, Color color) => Fill(p,
                new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), color);

            static void Line(Painter2D p, Vector2 from, Vector2 to, Color color, float width) => Poly(p,
                new[] { from, to }, color, width);

            static void Poly(Painter2D p, Vector2[] points, Color color, float width)
            {
                if (points.Length < 2) return;
                p.strokeColor = color;
                p.lineWidth = width;
                p.lineJoin = LineJoin.Round;
                p.BeginPath();
                p.MoveTo(points[0]);
                for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
                p.Stroke();
            }

            static void Fill(Painter2D p, Rect rect, Color color)
            {
                p.fillColor = color;
                p.BeginPath();
                p.MoveTo(new Vector2(rect.x, rect.y));
                p.LineTo(new Vector2(rect.xMax, rect.y));
                p.LineTo(new Vector2(rect.xMax, rect.yMax));
                p.LineTo(new Vector2(rect.x, rect.yMax));
                p.ClosePath();
                p.Fill();
            }
        }
    }
}
