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
        ZuiTimeline timeline;

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
            card.Add(Z.Field("Pyre", "The mock Pyre repeated by this formation.", pyre));

            card.Add(Z.HGroup(
                Z.MicroSlider("Count", f.count, 1f, 12f, "How many times this mock Pyre is spawned.",
                    v => Change(() => f.count = Mathf.RoundToInt(v)), width: 150f, decimals: 0),
                Z.MicroSlider("Stagger", f.stagger, 0f, 1f,
                    "Seconds between successive spawns. Zero means every spawn starts together.",
                    v => Change(() => f.stagger = v), width: 150f, decimals: 2)));

            card.Add(Z.Field("Pattern", "The spatial arrangement of the repeated spawns.",
                Z.Segmented((int)f.pattern, new[] { "Line", "Ring" },
                    "Line spaces the spawns along one direction. Ring distributes them evenly around the origin.",
                    v => { f.pattern = (MockPattern)v; Rebuild(); })));

            if (f.pattern == MockPattern.Line)
            {
                card.Add(Z.MicroSlider("Direction °", f.direction, 0f, 360f,
                    "The line's direction in the spatial guide. 0 is right and 90 is up.",
                    v => Change(() => f.direction = v), width: 150f, decimals: 0));
            }
            else
            {
                card.Add(Z.MicroSlider("Radius", f.radius, 0.25f, 5f, "The ring's radius in the spatial guide.",
                    v => Change(() => f.radius = v), width: 150f, decimals: 2));
            }

            BuildTiming(card, f);
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
                () => { plan.layers.Add(new MockLayer { name = "New Layer" }); Rebuild(); }).W(100f));
        }

        void BuildPaletteSplash(VisualElement card, MockPaletteSplash splash)
        {
            Button palette = null;
            palette = Z.Button(splash.palette + "…",
                "Choose the mock palette this splash scatters. These names are placeholders, not asset references.",
                () => ShowPaletteMenu(palette, splash)).W(140f);
            card.Add(Z.Field("Palette", "The mock palette this splash scatters.", palette));

            card.Add(Z.HGroup(
                Z.MicroSlider("Swatches", splash.swatches, 1f, 8f, "How many colours the splash throws out.",
                    v => Change(() => splash.swatches = Mathf.RoundToInt(v)), width: 130f, decimals: 0),
                Z.MicroSlider("Spread", splash.spread, 0f, 1f, "How far the swatches land from the origin.",
                    v => Change(() => splash.spread = v), width: 130f, decimals: 2)));

            BuildTiming(card, splash);
        }

        void BuildParticleBurst(VisualElement card, MockParticleBurst burst)
        {
            card.Add(Z.Field("Shape", "The volume the particles are emitted into.",
                Z.Segmented((int)burst.shape, new[] { "Cone", "Disc", "Sphere" },
                    "Cone emits into an arc, Disc into the whole plane, Sphere into a volume.",
                    v => { burst.shape = (MockBurstShape)v; Rebuild(); })));

            card.Add(Z.HGroup(
                Z.MicroSlider("Count", burst.count, 1f, 200f, "How many particles the burst emits.",
                    v => Change(() => burst.count = Mathf.RoundToInt(v)), width: 130f, decimals: 0),
                Z.MicroSlider("Speed", burst.speed, 0f, 20f, "How fast the particles leave the origin.",
                    v => Change(() => burst.speed = v), width: 130f, decimals: 1)));

            card.Add(Z.HGroup(
                Z.MicroSlider("Lifetime", burst.lifetime, 0.05f, 3f, "Seconds each particle survives.",
                    v => Change(() => burst.lifetime = v), width: 130f, decimals: 2),
                Z.MicroSlider("Spread °", burst.spread, 0f, 180f, "The emission arc's opening angle.",
                    v => Change(() => burst.spread = v), width: 130f, decimals: 0)));

            BuildTiming(card, burst);
        }

        /// The two dials that place a capability on the shared timing line. A capability that occupies no
        /// time never shows them, which is why a Layer Plan's card has no timing row at all.
        void BuildTiming(VisualElement card, MockCapability cap)
        {
            if (!cap.Timed) return;
            card.Add(Z.HGroup(
                Z.MicroSlider("Delay", cap.delay, 0f, 2f, "Seconds of dead time before this capability begins.",
                    v => Change(() => cap.delay = v), width: 130f, decimals: 2),
                Z.MicroSlider("Duration", cap.duration, 0.05f, 3f, "Seconds this capability occupies.",
                    v => Change(() => cap.duration = v), width: 130f, decimals: 2)));
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

            // The timeline is SHARED, so it only means anything once two enabled capabilities compete for
            // time. Below the workspace, never above it: appearing must not shove the guide down the pane.
            timeline = null;
            if (EnabledTimed().Count < 2) return;

            var timing = Z.Section("Timing",
                "The order the enabled time-occupying capabilities run in. Drag the playhead to read the sequence.",
                "chunks.mock.timing", icon: "timer");
            timeline = Z.Timeline(0f, "The recipe's shared sequence. Each band is one capability's own stretch of it.");
            timing.Add(timeline);
            root.Add(timing);
            RefreshTimeline();
        }

        List<MockCapability> EnabledTimed()
        {
            EnsureRecipe();
            var list = new List<MockCapability>();
            foreach (var cap in recipe.capabilities)
                if (cap.Enabled && cap.Timed) list.Add(cap);
            return list;
        }

        /// A zero-length band is legal and paints nothing, so the delay gap never has to be omitted.
        void RefreshTimeline()
        {
            if (timeline == null) return;
            var caps = EnabledTimed();
            var segments = new List<ZuiTimelineSegment>(caps.Count * 2);
            foreach (var cap in caps)
            {
                segments.Add(new ZuiTimelineSegment(string.Empty, cap.delay, new Color(0.32f, 0.34f, 0.4f, 0.6f),
                    "Dead time before " + cap.DisplayName + " begins."));
                segments.Add(new ZuiTimelineSegment(cap.DisplayName, cap.duration, cap.BandColor,
                    cap.DisplayName + " occupies this stretch of the recipe."));
            }
            timeline.SetSegments(segments.ToArray());
        }

        /// A value-only edit repaints the guide and re-bands the timeline; it never rebuilds the window,
        /// so a slider drag cannot pull the control out from under the pointer.
        void Change(Action edit)
        {
            edit?.Invoke();
            stage?.MarkDirtyRepaint();
            RefreshTimeline();
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
