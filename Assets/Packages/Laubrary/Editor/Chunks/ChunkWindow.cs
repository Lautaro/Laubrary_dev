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
        public static void Open() => GetWindow<ChunkWindow>("Chunks");

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
        }

        protected override void OnAssetChanged() => LauAssetGridGUI.ClearCache(_visualThumbs);

        // ── mutation helpers (the Undo contract every dial routes through) ───────────────────
        void Dial(string undoLabel, System.Action apply)
        {
            var c = Spec;
            if (c == null) return;
            Undo.RecordObject(c, undoLabel);
            apply();
            EditorUtility.SetDirty(c);
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

        protected override void BuildAsset(VisualElement root, ChunkSpec c)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            var body = scroll.contentContainer;

            BuildEmission(body, c);
            BuildPhysics(body, c);
            BuildLifeAndLook(body, c);
            BuildFloor(body, c);
            BuildSampled(body, c);
            BuildAnimatedContent(body, c);
            BuildHitDetection(body, c);
            BuildTrail(body, c);

            root.Add(scroll);
        }

        void BuildEmission(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Emission", "How many chunks fly out, how fast, and in which direction.");
            s.Add(Z.Row(
                Int2("Count Min", "Fewest chunks a burst spawns.", c.countMin, v => c.countMin = Mathf.Max(0, v)),
                Z.HSpace(),
                Int2("Max", "Most chunks a burst spawns (inclusive). Each burst picks a random count in between.",
                    c.countMax, v => c.countMax = Mathf.Max(c.countMin, v))));
            s.Add(Z.Row(
                Num2("Speed Min", "Slowest initial launch speed, world units per second.", c.speedMin,
                    v => c.speedMin = Mathf.Max(0f, v)),
                Z.HSpace(),
                Num2("Max", "Fastest initial launch speed, world units per second.", c.speedMax,
                    v => c.speedMax = Mathf.Max(c.speedMin, v))));
            s.Add(Z.Row(
                Num2("Direction °", "Centre direction of the cone. 0 = right, 90 = up.", c.directionDeg,
                    v => c.directionDeg = v),
                Z.HSpace(),
                Z.Field("Spread °", "Cone half-angle around the direction. 0 = a tight jet, 180 = a full circle.",
                    Z.Slider(c.spreadDeg, 0f, 180f, "Cone half-angle around the direction. 0 = a tight jet, 180 = a full circle.",
                        v => Dial("Spread", () => c.spreadDeg = v), Wide))));
            s.Add(Num2("Upward Bias", "Extra initial upward velocity added to every chunk, so even a radial burst pops up.",
                c.upwardBias, v => c.upwardBias = v));
            root.Add(s);
        }

        void BuildPhysics(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Physics", "How chunks arc, slow down and spin.");
            s.Add(Z.Row(
                Num2("Gravity", "Downward acceleration. Higher = snappier arcs that fall fast.", c.gravity,
                    v => c.gravity = Mathf.Max(0f, v)),
                Z.HSpace(),
                Z.Field("Drag", "Air resistance. 0 = none, ~1 = noticeable, ~3 = soupy.",
                    Z.Slider(c.drag, 0f, 5f, "Air resistance. 0 = none, ~1 = noticeable, ~3 = soupy.",
                        v => Dial("Drag", () => c.drag = v), Wide))));
            s.Add(Z.Row(
                Num2("Spin Min", "Slowest spin, degrees per second.", c.angularSpeedMin,
                    v => c.angularSpeedMin = Mathf.Max(0f, v)),
                Z.HSpace(),
                Num2("Max", "Fastest spin, degrees per second (each chunk's direction is randomised).",
                    c.angularSpeedMax, v => c.angularSpeedMax = Mathf.Max(c.angularSpeedMin, v))));
            s.Add(Z.Toggle("Face Velocity", "Point each chunk along its travel direction instead of spinning it freely.",
                c.faceVelocity, v => Dial("Face velocity", () => c.faceVelocity = v)));
            root.Add(s);
        }

        void BuildLifeAndLook(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Life / Look", "How long chunks last, how big they are, and how they fade.");
            s.Add(Z.Row(
                Num2("Life Min", "Shortest lifetime, seconds.", c.lifeMin, v => c.lifeMin = Mathf.Max(0.01f, v)),
                Z.HSpace(),
                Num2("Max", "Longest lifetime, seconds.", c.lifeMax, v => c.lifeMax = Mathf.Max(c.lifeMin, v))));
            s.Add(Z.Row(
                Num2("Size Min", "Smallest chunk size, world units.", c.sizeMin, v => c.sizeMin = Mathf.Max(0.001f, v)),
                Z.HSpace(),
                Num2("Max", "Largest chunk size, world units.", c.sizeMax, v => c.sizeMax = Mathf.Max(c.sizeMin, v))));

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
            var listField = new PropertyField(so.FindProperty("sprites"), "Sprites")
            {
                tooltip = "Chunk sprites picked from at random. Leave empty to use a procedural tinted pixel-square instead.",
            };
            listField.Bind(so);
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
            var s = Z.Section("Floor / Collision", "Bounce chunks off a horizontal floor — cheap, no Physics2D involved.");
            s.Add(Z.Toggle("Use Floor", "Bounce chunks off a horizontal floor at the height below.", c.useFloor,
                v => DialAndRebuild("Use floor", () => c.useFloor = v)));
            if (c.useFloor)
            {
                s.Add(Z.Row(
                    Num2("Floor Y", "World height of the floor the chunks land on.", c.floorY, v => c.floorY = v),
                    Z.HSpace(),
                    Z.Field("Bounciness", "How much speed survives a floor hit. 0 = dead stop, 1 = full bounce.",
                        Z.Slider(c.bounciness, 0f, 1f, "How much speed survives a floor hit. 0 = dead stop, 1 = full bounce.",
                            v => Dial("Bounciness", () => c.bounciness = v), Wide))));
                s.Add(Z.Field("Friction", "Horizontal speed lost on each floor hit. 0 = frictionless slide, 1 = stops sliding at once.",
                    Z.Slider(c.floorFriction, 0f, 1f, "Horizontal speed lost on each floor hit. 0 = frictionless slide, 1 = stops sliding at once.",
                        v => Dial("Floor friction", () => c.floorFriction = v), Wide)));
                s.Add(Z.Toggle("Rest On Floor", "Settle a slow chunk on the floor until it fades, instead of despawning it.",
                    c.restOnFloor, v => Dial("Rest on floor", () => c.restOnFloor = v)));
            }
            root.Add(s);
        }

        void BuildSampled(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Sampled Pseudo-3D Debris",
                "Cuts small chunks directly out of the exploding object's own sprite and tumbles them (squash + shade) " +
                "instead of using a flat or authored shape. Doesn't modify the source sprite — only reads pixels from " +
                "it, so its texture needs Read/Write Enabled.");
            const string srcTip = "The sprite chunks are cut out of. Leave empty for plain debris.";
            s.Add(Z.Field("Sample Source", srcTip,
                Z.Object<Sprite>(c.sampleSource, srcTip,
                    v => DialAndRebuild("Sample source", () => c.sampleSource = v), 200f)));

            if (c.sampleSource == null) { root.Add(s); return; }

            s.Add(Z.Row(
                Int2("Sample Px Min", "Smallest sampled chunk size, in source-texture pixels.", c.samplePxMin,
                    v => c.samplePxMin = Mathf.Max(1, v)),
                Z.HSpace(),
                Int2("Max", "Largest sampled chunk size, in source-texture pixels.", c.samplePxMax,
                    v => c.samplePxMax = Mathf.Max(c.samplePxMin, v))));
            s.Add(Z.Toggle("Tumble", "Simulate a turning 3D fragment (squash + shade) instead of a flat 2D spin.",
                c.tumble, v => DialAndRebuild("Tumble", () => c.tumble = v)));
            if (c.tumble)
            {
                s.Add(Z.Row(
                    Num2("Tumble Min", "Slowest simulated tumble rate, degrees per second.", c.tumbleSpeedMin,
                        v => c.tumbleSpeedMin = Mathf.Max(0f, v)),
                    Z.HSpace(),
                    Num2("Max", "Fastest simulated tumble rate, degrees per second.", c.tumbleSpeedMax,
                        v => c.tumbleSpeedMax = Mathf.Max(c.tumbleSpeedMin, v))));
                s.Add(Z.Field("Shade Strength", "How strong the light/dark swing is as a chunk turns. 0 = squash only, 1 = full swing.",
                    Z.Slider(c.tumbleShadeStrength, 0f, 1f,
                        "How strong the light/dark swing is as a chunk turns. 0 = squash only, 1 = full swing.",
                        v => Dial("Shade strength", () => c.tumbleShadeStrength = v), Wide)));
            }

            var tint = Z.Box("Tint",
                "Recolours sampled debris as it's cut. Whole = every opaque pixel; Edges only = just the rim, for a " +
                "burned/glowing edge; Excluding edges = a scorched interior with a clean edge.");
            tint.Add(Z.Field("Mode", "Which pixels of a cut chunk get recoloured.",
                Z.MiniRadio((int)c.tintMode, new[] { "None", "Whole", "Edges only", "Excluding edges" },
                    "Which pixels of a cut chunk get recoloured.",
                    v => DialAndRebuild("Tint mode", () => c.tintMode = (ChunkTintMode)v), wrap: true)));
            if (c.tintMode != ChunkTintMode.None)
            {
                tint.Add(Z.Field("Colour", "The colour blended onto the cut pixels.",
                    Z.Color(c.tintColor, "The colour blended onto the cut pixels.",
                        v => Dial("Tint colour", () => c.tintColor = v), 110f)));
                tint.Add(Z.Field("Strength", "How strongly the tint blends onto the source pixel. 0 = invisible, 1 = fully replaced.",
                    Z.Slider(c.tintStrength, 0f, 1f,
                        "How strongly the tint blends onto the source pixel. 0 = invisible, 1 = fully replaced.",
                        v => Dial("Tint strength", () => c.tintStrength = v), Wide)));
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
                "Every chunk plays this instead of a static or procedural sprite. Loses to Sample Source if that's also set.");
            s.Add(LauAssetElement.Build(c.animationSource,
                picked => DialAndRebuild("Animation source", () => c.animationSource = picked),
                typeof(IChunkAnimation), _visualThumbs, c.name, "Assets/Chunks/AnimationSources",
                "The animation every chunk plays — a Pyre blast, a Reel animation, anything a chunk can play."));
            root.Add(s);
        }

        void BuildHitDetection(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Hit Detection",
                "Gives each chunk a trigger collider + a Combat2D hitbox while it's alive, so debris can damage what it " +
                "touches. Cheap circle approximation only, not pixel-perfect.");
            s.Add(Z.Toggle("Enabled", "Let chunks damage what they touch while they fly.", c.useHitDetection,
                v => DialAndRebuild("Hit detection", () => c.useHitDetection = v)));
            if (c.useHitDetection)
            {
                s.Add(Z.Row(
                    Num2("Damage", "Damage a single chunk deals, once per target.", c.hitDamage,
                        v => c.hitDamage = Mathf.Max(0f, v)),
                    Z.HSpace(),
                    Z.Field("Radius Scale", "Collider radius as a multiple of the chunk's own current size.",
                        Z.Slider(c.hitRadiusScale, 0.1f, 3f,
                            "Collider radius as a multiple of the chunk's own current size.",
                            v => Dial("Hit radius scale", () => c.hitRadiusScale = v), Wide))));
            }
            root.Add(s);
        }

        void BuildTrail(VisualElement root, ChunkSpec c)
        {
            var s = Z.Section("Trail",
                "A puff spawned at each chunk's own position on a timer while it flies — e.g. a Pyre fire→smoke blast.");
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
