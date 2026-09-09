using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.Chunks.Editor
{
    /// The dedicated authoring window for a ChunkSpec — the recipe for one debris burst. Same AssetKit base
    /// every other Laubrary tool uses (browse/create/duplicate/rename/delete for free), fields grouped to
    /// mirror ChunkSpec's own [Header] sections. No live burst preview yet (Pyre's Play/Scrub transport is a
    /// bigger, separate piece of work) — this is the field editor.
    ///
    /// UI TOOLKIT PORT (ZUI → UI Toolkit migration): fully native, no IMGUI island. Every dial is a Z control
    /// and every one of them now records Undo — the IMGUI original took a single blanket Undo.RecordObject at
    /// the top of its per-frame draw, which coalesced every edit of a session into one step. Two Z controls
    /// were added to the toolkit for this window's sake (Z.Curve, Z.Gradient); the sprite list stays a bound
    /// PropertyField, which is Unity's own list UI and not something ZUI should be reimplementing.
    public partial class ChunkWindow : ZuiAssetWindow<ChunkSpec>
    {
        [MenuItem("Laubrary/Chunks")]
        public static void Open()
        {
            var w = GetWindow<ChunkWindow>("Chunks");
            w.minSize = new Vector2(820f, 520f);   // the same floor Pyre and Shaper declare (T-0306)
        }

        /// Same entry-point shape as PyreWindow.OpenFor/MirageWindow.OpenFor — lets a LauAssetField's Edit
        /// button jump straight into this ChunkSpec's own editor.
        public static void OpenFor(ChunkSpec spec)
        {
            var w = GetWindow<ChunkWindow>("Chunks");
            if (spec != null) w.SetAsset(spec);
        }

        protected override string TypeLabel => "Chunk";
        protected override string NewAssetName => "Chunks";
        protected override string DefaultFolder => "Assets/Chunks";

        const float Num = 70f;
        const float Wide = 150f;

        readonly Dictionary<Object, Texture2D> _visualThumbs = new Dictionary<Object, Texture2D>();

        ChunkSpec Spec => Current;

        protected override void OnDisable()
        {
            base.OnDisable();
            LauAssetGridGUI.ClearCache(_visualThumbs);
            DisposeChunkPreview();
        }

        protected override void OnAssetChanged()
        {
            LauAssetGridGUI.ClearCache(_visualThumbs);
            DisposeChunkPreview();   // the stage/debris textures belong to the previous spec's subject
        }

        // ── mutation helpers (the Undo contract every dial routes through) ───────────────────
        void Dial(string undoLabel, System.Action apply)
        {
            var c = Spec;
            if (c == null) return;
            Undo.RecordObject(c, undoLabel);
            apply();
            EditorUtility.SetDirty(c);
            RefreshChunkPreview();   // the slicing preview tracks every dial live (same cuts — seeded)
        }

        /// A change that shows/hides other dials → rebuild the panel too.
        void DialAndRebuild(string undoLabel, System.Action apply)
        {
            Dial(undoLabel, apply);
            Rebuild();
        }

        VisualElement Num2(string label, string tooltip, float value, System.Action<float> set, float width = Num)
            => Z.Field(label, tooltip, Z.Float(value, tooltip, v => Dial(label, () => set(v)), width));

        VisualElement Int2(string label, string tooltip, int value, System.Action<int> set, float width = Num)
            => Z.Field(label, tooltip, Z.Int(value, tooltip, v => Dial(label, () => set(v)), width));

        // ── section toggle bar (D-13) ───────────────────────────────────────────────────────
        // The roster the shared ZuiSectionToggleBar addresses, rebuilt from scratch on every BuildAsset.
        // Populated by Unit() as each section is built, so a builder that lives in another partial file
        // joins the bar without that file needing a single edit.
        readonly List<(string label, ZuiSection section)> _barUnits = new List<(string label, ZuiSection section)>();

        // Tallest layout the bar has taken at a given width, remembered for the window's lifetime so the
        // bar's own Sections↔Toggle Bar mode switch can never shrink the chrome above the workspace.
        float _barReservedW, _barReservedH;

        /// Run one section builder and register whatever top-level ZuiSection it added under `label`, so the
        /// toggle bar can address it. Every Build* adds exactly ONE section to `body`; the two that add none
        /// (BuildMiragePreview adds an action row, BuildFragmentSlicer bails out on a null module) register
        /// nothing rather than parking a dead button in the bar. Reading the section back off the container
        /// instead of having each builder return it is deliberate: the composed-module builders live in
        /// sibling partial files, and this keeps the whole toggle-bar change inside the shell.
        void Unit(VisualElement body, ChunkSpec c, string label, System.Action<VisualElement, ChunkSpec> build)
        {
            int before = body.childCount;
            build(body, c);
            for (int i = before; i < body.childCount; i++)
                if (body[i] is ZuiSection sec) { _barUnits.Add((label, sec)); return; }
        }

        protected override void BuildAsset(VisualElement root, ChunkSpec c)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            // The toggle bar rides at the very top of the per-asset UI, spanning the full window width
            // (Pyre's placement, PyreWindow.cs:400-403). Its host is added FIRST and filled LAST — the bar
            // can only be constructed once every section it addresses exists.
            var barHost = new VisualElement();
            barHost.style.flexShrink = 0f;
            if (_barReservedH > 0f) barHost.style.minHeight = _barReservedH;   // space reserved before anything paints
            root.Add(barHost);

            // TagsSection was parented under the WINDOW root by ZuiAssetWindow.BuildUI, which runs before
            // this. VisualElement.Add detaches before it reattaches, so re-adding it here pulls it out from
            // ABOVE the bar and drops it back in right below it, where it reads as one more toggleable
            // section rather than chrome sitting above the tool's own controls. (Same move as PyreWindow.)
            if (TagsSection != null) root.Add(TagsSection);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            root.Add(scroll);
            var body = scroll.contentContainer;

            _barUnits.Clear();
            if (TagsSection != null) _barUnits.Add(("Tags", TagsSection));

            // ── the COMPOSED EFFECT first ────────────────────────────────────────
            // Reading order is the order the user's own sentence is authored in: "this character shatters
            // (Slicer), one explosion behind the pieces and several between and in front (Blasts, and the
            // Layer Stack that decides which is which), on this schedule (Timeline)" — then see it (Mirage).
            // This block used to sit UNDERNEATH the nine plain-debris sections, which meant a user chasing
            // that sentence scrolled past nine sections of debris physics before reaching anything that did
            // what they asked. Every module here builds NO body until it is switched on, so a plain-debris
            // spec pays for this ordering with seven collapsed header rows and nothing else.
            Unit(body, c, "Slicer", BuildFragmentSlicer);
            Unit(body, c, "Blasts", BuildPyreSpawn);
            Unit(body, c, "Movement", BuildPyreMotion);       // how the blasts above travel — kept next to them
            Unit(body, c, "Formation", BuildSpawnFormation);  // supersedes the first blast, so it stays beside it
            Unit(body, c, "Layers", BuildLayerStack);         // the depth the blasts' Layer slot picks from
            Unit(body, c, "Splash", BuildParticleSplash);
            Unit(body, c, "Timeline", BuildTimeline);         // the backbone the design doc docks at the BOTTOM
            Unit(body, c, "Mirage", BuildMiragePreview);      // "see it" — directly after the Timeline, as before

            // ── then the plain-debris dials ──────────────────────────────────────
            Unit(body, c, "Emission", BuildEmission);
            Unit(body, c, "Physics", BuildPhysics);
            Unit(body, c, "Life", BuildLifeAndLook);
            Unit(body, c, "Floor", BuildFloor);
            Unit(body, c, "Sampled", BuildSampled);
            // The cut preview visualises the SAMPLED cuts above it (it re-cuts through the same
            // SampledChunkSprites.Sample using samplePx / tint / modifiers), so it stays directly under them.
            Unit(body, c, "Cut Preview", BuildPreview);
            Unit(body, c, "Animated", BuildAnimatedContent);
            Unit(body, c, "Hits", BuildHitDetection);
            Unit(body, c, "Trail", BuildTrail);

            var bar = new ZuiSectionToggleBar("Chunks", _barUnits.ToArray());
            barHost.Add(bar);
            ReserveBarHeight(barHost, bar);
        }

        /// Stable-workspace rule: chrome ABOVE the workspace must never change the geometry of what is below
        /// it. The bar hides its button strip with `display` when it is in Sections mode, which would collapse
        /// its height and jump the whole window up the moment the user switched modes. So reserve the space:
        /// remember the TALLEST height the bar has laid out at the current width and pin it as the host's
        /// minHeight, so a mode switch (or a solo) can only ever change what is IN the bar, never its size.
        /// A width change resets the reservation — that is the user resizing their own window, not contextual
        /// UI moving under their cursor. Growing only, and measured on the BAR rather than the host, so
        /// writing the host's minHeight cannot feed back into its own measurement.
        void ReserveBarHeight(VisualElement barHost, VisualElement bar)
        {
            bar.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float w = bar.resolvedStyle.width, h = bar.resolvedStyle.height;
                if (float.IsNaN(w) || float.IsNaN(h) || h <= 0f) return;
                if (Mathf.Abs(w - _barReservedW) > 0.5f) { _barReservedW = w; _barReservedH = 0f; }
                if (h <= _barReservedH + 0.5f) return;
                _barReservedH = h;
                barHost.style.minHeight = h;
            });
        }

        void BuildEmission(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Emission", "How many chunks fly out, how fast, and in which direction.",
                "chunks.emission");

            // Count/Speed are min/max PAIRS — one two-handle range each, never two fields (same control and
            // the same bounds Particle Splash already uses for the identical values).
            const string countTip = "How many chunks a burst spawns. Each burst picks one random count in between.";
            s.Add(Z.Field("Count", countTip,
                Z.MinMax(c.countMin, c.countMax, 0f, 64f, countTip,
                    (lo, hi) => Dial("Count", () =>
                    {
                        c.countMin = Mathf.RoundToInt(lo);
                        c.countMax = Mathf.RoundToInt(hi);
                    }), 140f, isInt: true)));

            const string speedTip = "Initial launch speed, world units per second. Each chunk picks one random " +
                "speed in between.";
            const string biasTip = "Extra initial upward velocity added to every chunk, so even a radial burst pops up.";
            // Upward Bias is short and belongs to the same launch velocity as Speed, so it fills the space
            // beside it rather than claiming a full row of its own.
            s.Add(Z.Row(
                Z.Field("Speed", speedTip,
                    Z.MinMax(c.speedMin, c.speedMax, 0f, 20f, speedTip,
                        (lo, hi) => Dial("Speed", () => { c.speedMin = lo; c.speedMax = hi; }), 140f)),
                Z.HSpace(),
                Num2("Upward Bias", biasTip, c.upwardBias, v => c.upwardBias = v)));

            s.Add(Z.Row(
                Z.MicroSlider("Direction °", c.directionDeg, 0f, 360f,
                    "Centre direction of the cone. 0 = right, 90 = up.",
                    v => Dial("Direction", () => c.directionDeg = v), Wide, showValue: true, decimals: 0),
                Z.HSpace(),
                Z.MicroSlider("Spread °", c.spreadDeg, 0f, 180f,
                    "Cone half-angle around the direction. 0 = a tight jet, 180 = a full circle.",
                    v => Dial("Spread", () => c.spreadDeg = v), Wide, showValue: true)));
            root.Add(s);
        }

        void BuildPhysics(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Physics", "How chunks arc, slow down and spin.", "chunks.physics");
            s.Add(Z.Row(
                Num2("Gravity", "Downward acceleration. Higher = snappier arcs that fall fast.", c.gravity,
                    v => c.gravity = Mathf.Max(0f, v)),
                Z.HSpace(),
                Z.MicroSlider("Drag", c.drag, 0f, 5f,
                    "Air resistance. 0 = none, ~1 = noticeable, ~3 = soupy.",
                    v => Dial("Drag", () => c.drag = v), Wide, showValue: true)));
            const string spinTip = "Spin rate, degrees per second. Each chunk picks its own rate in between — " +
                "and its own direction.";
            s.Add(Z.Field("Spin °/s", spinTip,
                Z.MinMax(c.angularSpeedMin, c.angularSpeedMax, 0f, 720f, spinTip,
                    (lo, hi) => Dial("Spin", () => { c.angularSpeedMin = lo; c.angularSpeedMax = hi; }), 140f)));
            s.Add(Z.Toggle("Face Velocity", "Point each chunk along its travel direction instead of spinning it freely.",
                c.faceVelocity, v => Dial("Face velocity", () => c.faceVelocity = v)));
            root.Add(s);
        }

        void BuildLifeAndLook(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Life / Look", "How long chunks last, how big they are, and how they fade.",
                "chunks.lifelook");
            const string lifeTip = "Chunk lifetime, seconds. Each chunk picks its own, then fades out on the " +
                "curves below.";
            s.Add(Z.Field("Life (s)", lifeTip,
                Z.MinMax(c.lifeMin, c.lifeMax, 0.05f, 8f, lifeTip,
                    (lo, hi) => Dial("Life", () => { c.lifeMin = lo; c.lifeMax = hi; }), 140f)));

            const string sizeRangeTip = "On-screen chunk size, world units. Each chunk picks its own size in " +
                "between, then rides the Size over life curve from there.";
            s.Add(Z.Field("Size", sizeRangeTip,
                Z.MinMax(c.sizeMin, c.sizeMax, 0.01f, 2f, sizeRangeTip,
                    (lo, hi) => Dial("Size", () => { c.sizeMin = lo; c.sizeMax = hi; }), 140f)));

            const string sizeTip = "Size multiplier across a chunk's life, left (spawn) to right (death).";
            s.Add(Z.Field("Size over life", sizeTip,
                Z.Curve(c.sizeOverLife, sizeTip, v => Dial("Size over life", () => c.sizeOverLife = v))));
            const string alphaTip = "Opacity across a chunk's life, left (spawn) to right (death).";
            s.Add(Z.Field("Alpha over life", alphaTip,
                Z.Curve(c.alphaOverLife, alphaTip, v => Dial("Alpha over life", () => c.alphaOverLife = v))));
            const string colTip = "Tint multiplied onto each chunk across its life, left (spawn) to right (death).";
            s.Add(Z.Field("Colour over life", colTip,
                Z.Gradient(c.colorOverLife, colTip, v => Dial("Colour over life", () => c.colorOverLife = v))));

            // The sprite list stays Unity's own list UI — a bound PropertyField. ZUI has no reorderable-list
            // control, and this is one place where reimplementing one buys nothing.
            var so = new SerializedObject(c);
            var spritesProp = so.FindProperty("sprites");
            const string spritesTip = "Chunk sprites picked from at random. Leave empty to use a procedural tinted pixel-square instead.";
            var listField = new PropertyField(spritesProp, "Sprites") { tooltip = spritesTip };
            listField.Bind(so);
            // T-0317 — Unity's own ListView disables its foldout header (and, cascading, the size field
            // inside it) when the bound list has zero elements — there's nothing to fold or type a size for.
            // Both used to inherit listField's tooltip verbatim, which describes what the list is FOR, never
            // why the header/size controls are currently dead. Kept live via TrackPropertyValue since growing
            // the list (via the ListView's own + footer button) doesn't rebuild this window.
            // T-0318 — setting listField.tooltip alone reached the size field and NOT the foldout header:
            // a PropertyField copies its own tooltip onto the ListView it generates when it binds, and
            // ChunkSpec.sprites carries a [Tooltip] besides, so the header and its Toggle resolved that
            // nearer tooltip instead and went on describing what the list is FOR. The inner ListView has to
            // be told too, and it only exists after Bind, so the walk runs on every update rather than once.
            void UpdateSpritesTooltip(SerializedProperty p)
            {
                string tip = p.arraySize == 0
                    ? "The list is empty, so there is nothing here to fold or resize by typing — use the + below to add a sprite."
                    : spritesTip;
                listField.tooltip = tip;
                listField.Query<VisualElement>().ForEach(e => { if (e is ListView || e is Foldout) e.tooltip = tip; });
            }
            UpdateSpritesTooltip(spritesProp);
            listField.TrackPropertyValue(spritesProp, UpdateSpritesTooltip);
            // A PropertyField builds its inner ListView when it binds, which is after this method returns,
            // so the one-time call above can only ever reach the PropertyField itself. Re-run once the
            // generated tree actually exists — the first layout is the earliest point at which it does.
            listField.RegisterCallback<GeometryChangedEvent>(_ => UpdateSpritesTooltip(spritesProp));
            // A PropertyField's inner ListView is not a BaseField, so the stylesheet's flex-grow:0 guard
            // doesn't reach it and ZuiAudit's stretch check doesn't see it — left alone it spans the whole
            // window and parks its size field against the far edge. Cap it like any other control.
            listField.style.width = 360f;
            listField.style.flexShrink = 0f;
            s.Add(listField);
            s.Add(Num2("Pixels/Unit", "Pixels-per-unit for the procedural fallback sprite (only used when the sprite list is empty).",
                c.pixelsPerUnit, v => c.pixelsPerUnit = Mathf.Max(1f, v)));
            root.Add(s);
        }

        void BuildFloor(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Floor / Collision", "Bounce chunks off a horizontal floor — cheap, no Physics2D involved.",
                "chunks.floor");
            // The section's own gate lives on its HEADER, not as a body toggle repeating the title (the shape
            // every Chunks 2.0 module section uses).
            s.SetHeaderToggle(c.useFloor, "Bounce chunks off a horizontal floor at the height below.",
                v => DialAndRebuild("Use floor", () => c.useFloor = v));
            root.Add(s);
            if (!c.useFloor) return;   // off → build no body at all

            s.Add(Z.Row(
                Num2("Floor Y", "World height of the floor the chunks land on.", c.floorY, v => c.floorY = v),
                Z.HSpace(),
                Z.MicroSlider("Bounciness", c.bounciness, 0f, 1f,
                    "How much speed survives a floor hit. 0 = dead stop, 1 = full bounce.",
                    v => Dial("Bounciness", () => c.bounciness = v), Wide, showValue: true)));
            s.Add(Z.MicroSlider("Friction", c.floorFriction, 0f, 1f,
                "Horizontal speed lost on each floor hit. 0 = frictionless slide, 1 = stops sliding at once.",
                v => Dial("Floor friction", () => c.floorFriction = v), Wide, showValue: true));
            s.Add(Z.Toggle("Rest On Floor", "Settle a slow chunk on the floor until it fades, instead of despawning it.",
                c.restOnFloor, v => Dial("Rest on floor", () => c.restOnFloor = v)));
        }

        void BuildSampled(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Sampled Pseudo-3D Debris",
                "Cuts small chunks directly out of the exploding object's own sprite and tumbles them (squash + shade) " +
                "instead of using a flat or authored shape. Doesn't modify the source sprite — only reads pixels from " +
                "it, so its texture needs Read/Write Enabled.",
                "chunks.sampled");
            const string srcTip = "The sprite chunks are cut out of. Leave empty for plain debris.";
            s.Add(Z.Field("Sample Source", srcTip,
                Z.Object<Sprite>(c.sampleSource, srcTip,
                    v => DialAndRebuild("Sample source", () => c.sampleSource = v), 200f)));

            if (c.sampleSource == null) { root.Add(s); return; }

            const string pxTip = "Size of each cut, in source-texture pixels. Every cut picks its own size in " +
                "between; anything wider than the source sprite is trimmed to it.";
            s.Add(Z.Field("Sample Px", pxTip,
                Z.MinMax(c.samplePxMin, c.samplePxMax, 1f, 64f, pxTip,
                    (lo, hi) => Dial("Sample px", () =>
                    {
                        c.samplePxMin = Mathf.RoundToInt(lo);
                        c.samplePxMax = Mathf.RoundToInt(hi);
                    }), 140f, isInt: true)));
            s.Add(Z.Toggle("Tumble", "Simulate a turning 3D fragment (squash + shade) instead of a flat 2D spin.",
                c.tumble, v => DialAndRebuild("Tumble", () => c.tumble = v)));
            if (c.tumble)
            {
                // Rate and shading are the two halves of one tumble, and both are short — one row.
                const string tumbleTip = "Simulated tumble rate, degrees per second. Each chunk picks its own " +
                    "rate in between, and its own direction.";
                s.Add(Z.Row(
                    Z.Field("Tumble °/s", tumbleTip,
                        Z.MinMax(c.tumbleSpeedMin, c.tumbleSpeedMax, 0f, 720f, tumbleTip,
                            (lo, hi) => Dial("Tumble speed",
                                () => { c.tumbleSpeedMin = lo; c.tumbleSpeedMax = hi; }), 140f)),
                    Z.HSpace(),
                    Z.MicroSlider("Shade Strength", c.tumbleShadeStrength, 0f, 1f,
                        "How strong the light/dark swing is as a chunk turns. 0 = squash only, 1 = full swing.",
                        v => Dial("Shade strength", () => c.tumbleShadeStrength = v), Wide, showValue: true)));
            }

            var tint = Z.BoxKeyed("Tint",
                "Recolours sampled debris as it's cut. Whole = every opaque pixel; Edges only = just the rim, for a " +
                "burned/glowing edge; Excluding edges = a scorched interior with a clean edge.",
                "chunks.sampled.tint");
            tint.Add(Z.Field("Mode", "Which pixels of a cut chunk get recoloured.",
                Z.MiniRadio((int)c.tintMode, new[] { "None", "Whole", "Edges only", "Excluding edges" },
                    "Which pixels of a cut chunk get recoloured.",
                    v => DialAndRebuild("Tint mode", () => c.tintMode = (ChunkTintMode)v), wrap: true)));
            if (c.tintMode != ChunkTintMode.None)
            {
                // Colour and its strength are one decision and both are short — one row, not two.
                tint.Add(Z.Row(
                    Z.Field("Colour", "The colour blended onto the cut pixels.",
                        Z.Color(c.tintColor, "The colour blended onto the cut pixels.",
                            v => Dial("Tint colour", () => c.tintColor = v), 110f)),
                    Z.HSpace(),
                    Z.MicroSlider("Strength", c.tintStrength, 0f, 1f,
                        "How strongly the tint blends onto the source pixel. 0 = invisible, 1 = fully replaced.",
                        v => Dial("Tint strength", () => c.tintStrength = v), Wide, showValue: true)));
                if (c.tintMode != ChunkTintMode.Whole)
                    tint.Add(Int2("Edge px", "How many pixels in from the rim count as 'edge'.",
                        c.edgeThicknessPx, v => c.edgeThicknessPx = Mathf.Max(1, v)));
            }
            s.Add(tint);
            BuildModifiers(s, c);
            root.Add(s);
        }

        void BuildAnimatedContent(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Animated Content",
                "Every chunk plays this instead of a static or procedural sprite. Loses to Sample Source if that's also set.",
                "chunks.animated");
            s.Add(LauAssetElement.Build(c.animationSource,
                picked => DialAndRebuild("Animation source", () => c.animationSource = picked),
                typeof(IChunkAnimation), _visualThumbs, c.name, "Assets/Chunks/AnimationSources",
                "The animation every chunk plays — a Pyre blast, a Lauminary animation, anything a chunk can play."));
            root.Add(s);
        }

        void BuildHitDetection(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Hit Detection",
                "Gives each chunk a trigger collider + a Combat2D hitbox while it's alive, so debris can damage what it " +
                "touches. Cheap circle approximation only, not pixel-perfect.",
                "chunks.hits");
            // The gate goes on the header — a body toggle labelled "Enabled" under a section called "Hit
            // Detection" said nothing the title hadn't already said.
            s.SetHeaderToggle(c.useHitDetection, "Let chunks damage what they touch while they fly.",
                v => DialAndRebuild("Hit detection", () => c.useHitDetection = v));
            root.Add(s);
            if (!c.useHitDetection) return;   // off → build no body at all

            s.Add(Z.Row(
                Num2("Damage", "Damage a single chunk deals, once per target.", c.hitDamage,
                    v => c.hitDamage = Mathf.Max(0f, v)),
                Z.HSpace(),
                Z.MicroSlider("Radius Scale", c.hitRadiusScale, 0.1f, 3f,
                    "Collider radius as a multiple of the chunk's own current size.",
                    v => Dial("Hit radius scale", () => c.hitRadiusScale = v), Wide, showValue: true)));
        }

        void BuildTrail(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Trail",
                "A puff spawned at each chunk's own position on a timer while it flies — e.g. a Pyre fire→smoke blast.",
                "chunks.trail");
            s.Add(LauAssetElement.Build(c.trailSource,
                picked => DialAndRebuild("Trail source", () => c.trailSource = picked),
                typeof(IChunkTrailSource), _visualThumbs, c.name, "Assets/Chunks/TrailSources",
                "What each chunk leaves behind it while it flies. Leave empty for no trail."));
            if (c.trailSource != null)
                s.Add(Num2("Interval (s)", "Seconds between trail puffs.", c.trailInterval,
                    v => c.trailInterval = Mathf.Max(0.01f, v)));
            root.Add(s);
        }
    }
}
