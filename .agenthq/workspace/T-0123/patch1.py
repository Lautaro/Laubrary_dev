import io
p = 'Assets/ChunksMock/Editor/ChunksMockWindow.cs'
s = io.open(p, encoding='utf-8').read()


def rep(old, new, n=1):
    global s
    assert s.count(old) == n, "MISS(%d!=%d): %r" % (s.count(old), n, old[:90])
    s = s.replace(old, new, n)


# A: fields
rep(
"""        MockRecipe recipe;
        MockCapabilityStage stage;
        ZuiTimeline timeline;
""",
"""        MockRecipe recipe;
        MockCapabilityStage stage;
        MockTimingTracks tracks;

        // Rebuild() recreates the left ScrollView, so the offset has to be carried across by hand or the
        // pane snaps to the top and the button the user just pressed moves out from under the pointer.
        ScrollView leftPane;
        Vector2 carriedScroll;
""")

# B: BuildUI keeps the left pane and restores its offset
rep(
"""            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 460f;
            left.style.minHeight = 0f;
            BuildControls(left.contentContainer);
""",
"""            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 460f;
            left.style.minHeight = 0f;
            BuildControls(left.contentContainer);
            leftPane = left;
            RestoreScroll(left);
""")

# C: capture/restore the scroll offset around a rebuild
rep(
"""        void EnsureRecipe()
        {
            if (recipe == null) recipe = new MockRecipe { name = "Untitled Chunk" };
        }
""",
"""        void EnsureRecipe()
        {
            if (recipe == null) recipe = new MockRecipe { name = "Untitled Chunk" };
        }

        /// The last thing that runs before the root is cleared, so it is the only place the outgoing
        /// ScrollView's offset can still be read.
        protected override void OnBeforeRebuild()
        {
            if (leftPane != null) carriedScroll = leftPane.scrollOffset;
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
""")

# D: Formation - 5 rows packed into 3
rep(
"""            card.Add(Z.Field("Pyre", "The mock Pyre repeated by this formation.", pyre));

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

            BuildTiming(card, f);""",
"""
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

            AddTimingRow(card, f);""")

# E: layer default name is numbered
rep(
"""                () => { plan.layers.Add(new MockLayer { name = "New Layer" }); Rebuild(); }).W(100f));""",
"""                // Numbered, because two rows both called "New Layer" are indistinguishable in a plan of five.
                () => { plan.layers.Add(new MockLayer { name = "Layer " + (plan.layers.Count + 1) }); Rebuild(); })
                .W(100f));""")

# F: Palette Splash - 3 rows into 2
rep(
"""            card.Add(Z.Field("Palette", "The mock palette this splash scatters.", palette));

            card.Add(Z.HGroup(
                Z.MicroSlider("Swatches", splash.swatches, 1f, 8f, "How many colours the splash throws out.",
                    v => Change(() => splash.swatches = Mathf.RoundToInt(v)), width: 130f, decimals: 0),
                Z.MicroSlider("Spread", splash.spread, 0f, 1f, "How far the swatches land from the origin.",
                    v => Change(() => splash.spread = v), width: 130f, decimals: 2)));

            BuildTiming(card, splash);""",
"""
            card.Add(Z.HGroup(
                Z.Field("Palette", "The mock palette this splash scatters.", palette),
                Z.MicroSlider("Swatches", splash.swatches, 1f, 8f, "How many colours the splash throws out.",
                    v => Change(() => splash.swatches = Mathf.RoundToInt(v)), width: 130f, decimals: 0)));

            AddTimingRow(card, splash,
                Z.MicroSlider("Spread", splash.spread, 0f, 1f, "How far the swatches land from the origin.",
                    v => Change(() => splash.spread = v), width: 130f, decimals: 2));""")

# G: Particle Burst - 4 rows into 3
rep(
"""            card.Add(Z.Field("Shape", "The volume the particles are emitted into.",
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

            BuildTiming(card, burst);""",
"""            card.Add(Z.HGroup(
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

            AddTimingRow(card, burst);""")

io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print("patch1 ok")
