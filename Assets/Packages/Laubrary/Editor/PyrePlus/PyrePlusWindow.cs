// PyrePlusWindow — the authoring window for the PyrePlus prototype (see PYREPLUS_DESIGN.md).
//
// Built directly on the UI Toolkit toolkit (ZuiAssetWindow + Z.* controls + ZuiSection folding), NOT on
// Zolumn — the UITK migration already gives collapsible bordered sections and grow-to-fill layout, so the
// design's Zolumn dependency is dropped. SLICE 1: the Shape section + a live preview. Swarm and Modifiers
// sections follow. Deliberately a separate menu item and asset type from real Pyre, which is untouched.
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyrePlus.Editor
{
    // Split across partials: PyrePlusWindow.cs = shell + left-pane dials; PyrePlusWindow.Preview.cs = the
    // IMGUI preview island + the Swarm authoring overlay (shape outline, spawn dots, drag handles).
    public partial class PyrePlusWindow : ZuiAssetWindow<PyrePlusSpec>
    {
        [MenuItem("Laubrary/Pyre Plus")]
        public static void Open() => GetWindow<PyrePlusWindow>("Pyre Plus");

        PyrePlusSpec spec => Current;
        protected override string TypeLabel => "Pyre Plus";
        protected override string NewAssetName => "New Pyre Plus";
        protected override string DefaultFolder => "Assets/PyrePlus";

        protected override Texture2D RenderThumbnail(PyrePlusSpec item)
        {
            int mid = Mathf.Clamp(item.frameCount / 2, 0, Mathf.Max(0, item.frameCount - 1));
            return PyrePlusRenderer.RenderFrameTexture(item, mid);
        }
        protected override bool AnimateThumbnails => true;
        protected override void UpdateAnimatedThumbnail(PyrePlusSpec item, Texture2D tex, double time)
        {
            if (item == null || item.frameCount <= 1) return;
            float fps = Mathf.Max(1f, item.previewFps);
            int f = Mathf.FloorToInt((float)(time * fps)) % item.frameCount;
            tex.SetPixels32(PyrePlusRenderer.RenderFrame(item, f));
            tex.Apply();
        }

        // preview state
        IMGUIContainer preview;
        Texture2D previewTex;
        double lastTime;
        float acc;
        bool playing = true;
        int frame;

        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.update += Tick;
        }
        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= Tick;
            if (previewTex != null) { DestroyImmediate(previewTex); previewTex = null; }
        }
        protected override void OnAssetChanged() { frame = 0; previewDirty = true; }

        void Tick()
        {
            if (!playing || spec == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            acc += dt * Mathf.Max(1f, spec.previewFps);
            bool advanced = false;
            while (acc >= 1f) { acc -= 1f; frame = (frame + 1) % Mathf.Max(1, spec.frameCount); advanced = true; }
            if (advanced) { previewDirty = true; preview?.MarkDirtyRepaint(); }
        }

        bool previewDirty = true;
        int lastRenderedFrame = -1;

        void MarkDirty() { previewDirty = true; preview?.MarkDirtyRepaint(); }

        protected override void BuildAsset(VisualElement root, PyrePlusSpec s)
        {
            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;

            // ── left: dials ──────────────────────────────────────────────────────
            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.width = 360f;
            left.style.flexShrink = 0f;
            var dials = left.contentContainer;

            // Saved-views bar rides at the very top of the dials pane. Its capture/apply aggregate every
            // ZuiBox under this asset root (Canvas / Solid / Transform / Modifiers): a "view" is their fold,
            // gear-open and shown-control state only, never an authored value. The query root is `root` (the
            // BuildAsset host), not rootVisualElement — `split` is added to `root` below BEFORE RestoreLast
            // runs, but the window has not yet parented `root` to its own rootVisualElement at this point.
            var viewBar = BuildViewBar(root);
            dials.Add(viewBar);

            BuildCanvas(dials, s);
            BuildShape(dials, s);
            BuildSwarm(dials, s);
            BuildModifiers(dials, s);   // PyrePlusWindow.Modifiers.cs

            // ── right: preview ───────────────────────────────────────────────────
            preview = new IMGUIContainer(() => DrawPreview(s));
            preview.style.flexGrow = 1f;
            preview.style.minWidth = 200f;
            preview.AddToClassList("zui-stage");

            split.Add(left);
            split.Add(preview);
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            root.Add(split);

            // Whole tree is now under `root`; re-apply the view the user left this window in.
            viewBar.RestoreLast();
        }

        // ── saved views (Z2 — the shared ZuiViewBar + committed ZuiViewStore) ──────────
        const string ViewStorePath = "Assets/PyrePlus/PyrePlusViews.asset";
        const string ViewPrefsKey = "PyrePlus.lastView";

        // Build the views bar. Capture/apply aggregate every ZuiBox under `paneRoot` via CaptureView/
        // ApplyView, so a view round-trips the Canvas / Solid / Transform / Modifiers boxes' fold + gear +
        // shown-control state. The store asset is committed (shared); only the per-user "last view" pointer
        // is in EditorPrefs. The bar never touches AssetDatabase — the host mints the asset (below).
        ZuiViewBar BuildViewBar(VisualElement paneRoot)
        {
            Dictionary<string, bool> Capture()
            {
                var d = new Dictionary<string, bool>();
                foreach (var b in paneRoot.Query<ZuiBox>().ToList()) b.CaptureView(d);
                return d;
            }
            void Apply(IReadOnlyDictionary<string, bool> from)
            {
                foreach (var b in paneRoot.Query<ZuiBox>().ToList()) b.ApplyView(from);
            }
            return new ZuiViewBar(
                () => AssetDatabase.LoadAssetAtPath<ZuiViewStore>(ViewStorePath),
                CreateViewStore,
                Capture,
                Apply,
                ViewPrefsKey);
        }

        // Mint the PyrePlus views asset — only ever called from the bar's Save-as when none exists yet.
        // Undo-registered per the Laubrary Undo rule; creates the folder if missing.
        ZuiViewStore CreateViewStore()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PyrePlus"))
                AssetDatabase.CreateFolder("Assets", "PyrePlus");
            var store = ScriptableObject.CreateInstance<ZuiViewStore>();
            AssetDatabase.CreateAsset(store, ViewStorePath);
            Undo.RegisterCreatedObjectUndo(store, "Create Pyre Plus Views");
            return store;
        }

        void BuildCanvas(VisualElement root, PyrePlusSpec s)
        {
            var box = Z.BoxKeyed("Canvas", "The output resolution, frame count, seed and backdrop.", "pyreplus.canvas");
            box.Add(WrapRow(
                Z.Field("Size", "Square canvas size in pixels.",
                    Z.Int(s.canvasSize, "Square canvas size in pixels.", v => Dirty(() => s.canvasSize = Mathf.Max(1, v)), 60f)),
                Z.Field("PPU", "Pixels per unit for the baked sprite.",
                    Z.Float(s.pixelsPerUnit, "Pixels per unit.", v => Dirty(() => s.pixelsPerUnit = Mathf.Max(1f, v)), 60f))));
            box.Add(WrapRow(
                Z.Field("Frames", "How many frames the animation bakes to.",
                    Z.SliderInt(s.frameCount, 1, 64, "Frame count.", v => Dirty(() => s.frameCount = v), 150f)),
                Z.Field("Seed", "Random seed — every particle's randomness derives from it.",
                    Z.Int(s.seed, "Random seed.", v => Dirty(() => s.seed = v), 70f))));
            root.Add(box);
        }

        // The Shape section is a stable header + a body container we clear/refill whenever the Advanced gate
        // flips — the same mechanism BuildSwarm uses for swarmBody/RebuildSwarm, so the opt-in Travel/Spin
        // controls appear/disappear without rebuilding the whole window.
        VisualElement shapeBody;

        void BuildShape(VisualElement root, PyrePlusSpec s)
        {
            var sec = Z.Section("Shape", "The particle's own look — colour, opacity and size over its life.");
            shapeBody = new VisualElement();
            sec.Add(shapeBody);
            root.Add(sec);
            RebuildShape();
        }

        static readonly List<string> ShapeFormChoices = new List<string> { "Disc", "Gem", "Crescent", "Sparkle", "Sprite", "Box", "Pyramid", "Can", "Orb", "Ring", "Text" };
        static readonly List<string> TextFillModeChoices = new List<string> { "Per-char gradient", "Per-char step", "Text gradient" };

        void RebuildShape()
        {
            var s = spec;
            if (s == null || shapeBody == null) return;
            shapeBody.Clear();

            s.alpha ??= new ZUIValue(1f);

            // FORM selector — Disc / Gem / Crescent / Sparkle / Sprite / Box / Pyramid / Can. A Dropdown, NOT an
            // 8-wide Segmented row: content-sized segments (ZuiSegmented is flex-shrink:0 and never wraps) would
            // overflow the 360px pane into a horizontal scrollbar (ui-layout-rules: "A horizontal scrollbar is a
            // smell"); this also matches the Swarm section's own shape-kind Dropdown. Rebuild so the form-specific
            // rows swap in/out.
            shapeBody.Add(Z.Field("Form", "The particle's rendered form.",
                Z.Dropdown((int)s.shapeForm, ShapeFormChoices,
                    "Disc = a flat soft disc. Gem = a true-3D lit crystal. Crescent = a disc with an offset bite. "
                    + "Sparkle = twinkling lit cells. Sprite = a stamped image. Box / Pyramid / Can = true-3D lit "
                    + "solids sharing the Gem's facet lighting (tilt, light, edge lines, glows). Orb = a lit "
                    + "sphere; Ring = a flat tilted annulus (a Saturn ring) — both reuse that same lighting "
                    + "analytically. Text = a string as extruded SDF letters (one particle per character; the "
                    + "particle count follows the string).",
                    // Rebuild BOTH sections: Text swaps in its own Shape box AND hides the Swarm's Count field.
                    v => { Dirty(() => s.shapeForm = (ShapeForm)v); RebuildShape(); RebuildSwarm(); }, 150f)));

            // Shared rows. For the Gem, Colour is its material tint and Size is its girdle radius; for the Sprite,
            // Colour is the optional tint. TEXT takes its colour from its own Fill / Border gradients instead, so
            // the Colour row is hidden for it (the Text box's Fill row tooltip says so). Size is the scale driver
            // for every form — Text reads it as the character HEIGHT.
            if (s.shapeForm != ShapeForm.Text)
                shapeBody.Add(Z.Field("Colour",
                    "Colour over the particle's life (0 = birth, 1 = death). For the 3D solid forms (Gem/Box/Pyramid/"
                    + "Can) this is the material tint — a blue gradient reads as a sapphire.",
                    GradientField("Colour", () => s.colorOverLife, g => Dirty(() => s.colorOverLife = g))));
            shapeBody.Add(Val("Alpha", "Opacity over the particle's own life (multiplies the final output alpha).", s.alpha, 0f, 1f));
            shapeBody.Add(Val("Size (px)",
                "Radius in pixels over the particle's own life. For the 3D solid forms it is the base size R that "
                + "Aspect / Depth (or the Gem's Crown / Pavilion) scale from. For Text it is the character HEIGHT.",
                s.size, 0f, 32f));

            // Form-specific rows. Edge softness applies to Disc (its rim) and Crescent (BOTH rims); Gem/Sparkle/
            // Sprite don't use it, so it's hidden for them. (EdgeRow is a bare MicroSlider — its own caption is
            // the "Edge" label, so no redundant Z.Field label wrapping it.)
            switch (s.shapeForm)
            {
                case ShapeForm.Gem:
                case ShapeForm.Box:
                case ShapeForm.Pyramid:
                case ShapeForm.Can:
                case ShapeForm.Orb:
                case ShapeForm.Ring:
                    BuildSolidBox(s);
                    break;
                case ShapeForm.Disc:
                    shapeBody.Add(EdgeRow(s, "Soft rim (1) vs a hard pixel edge (0)."));
                    break;
                case ShapeForm.Crescent:
                    BuildCrescentRows(s);
                    break;
                case ShapeForm.Sparkle:
                    BuildSparkleRows(s);
                    break;
                case ShapeForm.Sprite:
                    BuildSpriteRows(s);
                    break;
                case ShapeForm.Text:
                    BuildTextBox(s);
                    break;
            }

            // Advanced gate: the particle's OWN motion after birth (opt-in). Toggling rebuilds just this section.
            shapeBody.Add(Z.Toggle("Advanced",
                "Per-particle motion on the particle's own life clock: a travel path added to its spawn position, "
                + "and an in-place spin.",
                s.shapeAdvanced, v => { Dirty(() => s.shapeAdvanced = v); RebuildShape(); }));
            if (!s.shapeAdvanced) return;

            float half = Mathf.Max(1f, s.canvasSize * 0.5f);
            s.particlePathX ??= new ZUIValue(0f);
            s.particlePathY ??= new ZUIValue(0f);
            s.particleSpin ??= new ZUIValue(0f);

            shapeBody.Add(Val2D("Travel",
                "The particle's own path after birth: canvas-pixel offsets ADDED to its spawn position, sampled on "
                + "the particle's OWN life (0 = birth, 1 = death). Author it as a Curve to make the particle "
                + "drift/arc as it lives; Static 0 = no travel.",
                s.particlePathX, s.particlePathY,
                new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)));
            shapeBody.Add(Val("Spin °",
                "Degrees the particle rotates over its own life. DISC / SPARKLE form: pixels spin IN PLACE (2D) — a "
                + "plain disc or an even sparkle field is radially symmetric so it shows little (add a geometry/"
                + "texture Modifier so the disc's spin reads). CRESCENT: rotates the whole crescent, on top of its "
                + "own bite Angle. SPRITE: rotates the stamped image. GEM / BOX / PYRAMID / CAN (3D solids): the "
                + "solid's 3D YAW about its vertical axis, turning it so its facets sweep past the light. ORB: rolls "
                + "the lit hotspot left/right around the sphere (its silhouette never changes). RING: rolls the "
                + "annulus in its own plane. TEXT: each letter yaws about its OWN centre (its 3D letter-box turns to "
                + "face the light); paired with the letters' tilt for the extruded look. (The pseudo-3D tilt of a "
                + "whole SWARM lives on the Swarm shape transform, not here.)",
                s.particleSpin, -720f, 720f));
        }

        // The 3D-solid controls — one framed box inside the Shape body, shown for Gem / Box / Pyramid / Can (all
        // true-3D convex facet solids sharing one renderer). Colour, Alpha and Size stay above (shared); this box
        // adds the per-form geometry then the SHARED tilt / lighting / lines / glows every 3D solid uses. Titled
        // "Solid" (not "Gem") since it now serves four forms. All plain sliders are label-inside MicroSliders; the
        // two glows are animatable ZUIValues (Val), each packed with its own colour; Specular packs with its colour.
        void BuildSolidBox(PyrePlusSpec s)
        {
            s.gemTilt ??= new ZUIValue(18f);
            s.gemEdgeGlow ??= new ZUIValue(0.5f);     // defensive; the real anti-phase defaults come from the spec factories
            s.gemInnerGlow ??= new ZUIValue(0.5f);

            // BoxKeyed: view presets persist under this stable key — retitling the box or rewording
            // its tooltip must never orphan saved views.
            var box = Z.BoxKeyed("Solid", "A true-3D form lit per-pixel by one key light, with light-catching hard edge "
                + "lines and two staggered glows. Its material colour is the shared Colour above (a blue gradient = "
                + "a sapphire); its base size is the shared Size. Gem = an octahedral crystal; Box / Pyramid / Can = "
                + "a cuboid / square pyramid / cylinder; Orb = a sphere (no geometry rows — a ball needs none); "
                + "Ring = a flat two-sided tilted annulus (a Saturn ring).", "pyreplus.solid");

            // ── per-form geometry ──
            if (s.shapeForm == ShapeForm.Gem)
            {
                box.Add(Z.MicroSlider("Sides", s.gemSides, 3f, 8f,
                    "Girdle vertex count — 4 is the classic octahedral gem; more sides make a rounder crystal.",
                    v => Dirty(() => s.gemSides = Mathf.Clamp(Mathf.RoundToInt(v), 3, 8)), 150f, showValue: true, decimals: 0));

                box.Add(WrapRow(
                    Z.MicroSlider("Crown", s.gemCrown, 0.2f, 2.5f,
                        "Crown height (the top point) as a fraction of the gem's radius.",
                        v => Dirty(() => s.gemCrown = v), 150f, showValue: true),
                    Z.MicroSlider("Pavilion", s.gemPavilion, 0.2f, 2.5f,
                        "Pavilion depth (the bottom point) as a fraction of the gem's radius.",
                        v => Dirty(() => s.gemPavilion = v), 150f, showValue: true)));
            }
            else if (s.shapeForm == ShapeForm.Box || s.shapeForm == ShapeForm.Pyramid || s.shapeForm == ShapeForm.Can)
            {
                // Box / Pyramid / Can — Aspect (height) always applies. Depth (front-to-back) applies to Box and
                // Pyramid; the Can is a circular cross-section, so its Depth is meaningless — HIDDEN rather than
                // shown-disabled (a dead control is clutter per the layout rules), leaving Aspect alone.
                var geo = new List<VisualElement>
                {
                    Z.MicroSlider("Aspect", s.solidAspect, 0.3f, 3f,
                        "Height as a fraction of width (1 = as tall as wide). Box height, Pyramid apex height, Can "
                        + "height.",
                        v => Dirty(() => s.solidAspect = v), 150f, showValue: true),
                };
                if (s.shapeForm != ShapeForm.Can)
                    geo.Add(Z.MicroSlider("Depth", s.solidDepth, 0.2f, 2f,
                        "Depth (front-to-back) as a fraction of width. Box: its third dimension; Pyramid: its base "
                        + "front-to-back (1 = the square base).",
                        v => Dirty(() => s.solidDepth = v), 150f, showValue: true));
                box.Add(WrapRow(geo.ToArray()));
            }
            else if (s.shapeForm == ShapeForm.Ring)
            {
                // Ring — one geometry row: the hole radius. (Orb has NO geometry rows — a sphere needs none — so it
                // falls straight through to the shared tilt / light / lines / glows below.)
                box.Add(Z.MicroSlider("Inner", s.ringInner, 0.1f, 0.92f,
                    "Inner radius as a fraction of the outer radius — the size of the ring's hole (0.1 = a nearly "
                    + "solid disc, 0.92 = a thin hoop).",
                    v => Dirty(() => s.ringInner = v), 150f, showValue: true));
            }

            // ── shared: tilt / light / lines / glows (every 3D solid) ──
            box.Add(Val("Tilt °",
                "World tilt about the horizontal axis, in degrees, over the particle's OWN life — tips the solid "
                + "toward/away from the viewer so its facets catch the light differently. Orb: the sphere's "
                + "silhouette never changes, so this instead rolls the lit hotspot up/down across the ball. Ring: "
                + "this opens or closes the ellipse — 0° = face-on (a full circle), ±90° = edge-on (a sliver).",
                s.gemTilt, -1440f, 1440f));

            // Light / Lines / Glow rows opt into the box's ⚙ gear (Tilt above stays mandatory). Each group
            // toggle flips its whole cluster at once; keys are STABLE "solid.*" strings (never a display
            // label) so a saved view survives a relabel. A saved "view" round-trips these on/off states.
            box.ToggleGroup("Light", "Light");
            box.ToggleGroup("Lines", "Lines");
            box.ToggleGroup("Glow", "Glow");

            box.Add(Z.Divider("Light", "The single key light: where it sits and how the facets respond."));
            box.Add(box.Toggleable(WrapRow(
                Z.MicroSlider("Light yaw", s.gemLightYaw, -180f, 180f,
                    "Direction the key light comes FROM, left/right, in degrees.",
                    v => Dirty(() => s.gemLightYaw = v), 150f, showValue: true),
                Z.MicroSlider("Light pitch", s.gemLightPitch, 0f, 85f,
                    "Height of the key light above the horizon, in degrees.",
                    v => Dirty(() => s.gemLightPitch = v), 150f, showValue: true)),
                "solid.light.angles", "Angle", "Light"));
            box.Add(box.Toggleable(WrapRow(
                Z.MicroSlider("Ambient", s.gemAmbient, 0f, 1f,
                    "Fill light on faces turned away from the key — near zero keeps the solid contrasty.",
                    v => Dirty(() => s.gemAmbient = v), 150f, showValue: true),
                Z.MicroSlider("Specular", s.gemSpecular, 0f, 2f,
                    "Strength of the Blinn-Phong highlight (the bright hot spot).",
                    v => Dirty(() => s.gemSpecular = v), 150f, showValue: true),
                Z.Field("Spec colour", "Tint of the Blinn-Phong highlight.",
                    Z.Color(s.gemSpecularColor, "Tint of the Blinn-Phong highlight.",
                        c => Dirty(() => s.gemSpecularColor = c), 60f, showAlpha: false))),
                "solid.light.response", "Response", "Light"));

            box.Add(Z.Divider("Lines", "The hard facet edge lines that catch the light."));
            box.Add(box.Toggleable(WrapRow(
                Z.MicroSlider("Line width", s.gemLineWidth, 0f, 3f,
                    "Width of the hard facet edge lines in pixels (0 = no lines). The lines catch the key light.",
                    v => Dirty(() => s.gemLineWidth = v), 150f, showValue: true),
                Z.Field("Line colour", "Colour of the facet edge lines.",
                    Z.Color(s.gemLineColor, "Colour of the facet edge lines.",
                        c => Dirty(() => s.gemLineColor = c), 60f, showAlpha: false))),
                "solid.lines", "Width & colour", "Lines"));

            box.Add(Z.Divider("Glow", "Two staggered glows, anti-phase by default — each with its own tint."));
            box.Add(box.Toggleable(WrapRow(
                Val("Edge glow",
                    "Strength (0-1) of the halo around the edge lines, over the particle's OWN life; it spills "
                    + "OUTSIDE the solid's silhouette. Default: two anti-phase pulses (it peaks while the inner glow "
                    + "rests) — reshape freely.",
                    s.gemEdgeGlow, 0f, 1f),
                Z.Field("Edge colour", "Tint of the edge-line halo glow.",
                    Z.Color(s.gemEdgeGlowColor, "Tint of the edge-line halo glow.",
                        c => Dirty(() => s.gemEdgeGlowColor = c), 60f, showAlpha: false))),
                "solid.glow.edge", "Edge", "Glow"));
            box.Add(box.Toggleable(WrapRow(
                Val("Inner glow",
                    "Strength (0-1) of the emissive glow rising from the facet interiors, over the particle's OWN "
                    + "life; interior only. Default: two anti-phase pulses (it peaks while the edge glow rests) — "
                    + "reshape freely.",
                    s.gemInnerGlow, 0f, 1f),
                Z.Field("Inner colour", "Tint of the facet inner glow.",
                    Z.Color(s.gemInnerGlowColor, "Tint of the facet inner glow.",
                        c => Dirty(() => s.gemInnerGlowColor = c), 60f, showAlpha: false))),
                "solid.glow.inner", "Inner", "Glow"));

            shapeBody.Add(box);
        }

        // The Edge-softness slider — its own MicroSlider caption is the "Edge" label, so it isn't wrapped in a
        // Z.Field (that would print "Edge" twice). Shared by Disc (its single rim) and Crescent (both rims), each
        // passing its own tooltip.
        ZuiMicroSlider EdgeRow(PyrePlusSpec s, string tooltip) =>
            Z.MicroSlider("Edge", s.edgeSoftness, 0f, 1f, tooltip,
                v => Dirty(() => s.edgeSoftness = v), 150f, showValue: true);

        // Crescent form rows — the shared Edge row (drives BOTH rims), then the bite: size + facing packed, and
        // the push-out offset.
        void BuildCrescentRows(PyrePlusSpec s)
        {
            s.crescentBite ??= new ZUIValue(0.55f);
            s.crescentAngle ??= new ZUIValue(0f);

            shapeBody.Add(EdgeRow(s,
                "Soft rim (1) vs a hard pixel edge (0). For the Crescent it feathers BOTH rims — the outer disc "
                + "edge and the bite edge."));
            shapeBody.Add(WrapRow(
                Val("Bite",
                    "Size of the disc bitten out of the main disc, as a fraction of its radius (0 = no bite, a "
                    + "full disc; 1 = a bite as wide as the disc), over the particle's own life.",
                    s.crescentBite, 0f, 1f),
                Val("Angle °",
                    "Which way the bite faces, in degrees, over the particle's own life — swings the crescent's "
                    + "opening around.",
                    s.crescentAngle, -360f, 360f)));
            shapeBody.Add(Z.MicroSlider("Offset", s.crescentOffset, 0f, 1f,
                "How far the bite disc is pushed out from the centre, as a fraction of the radius. Larger = a "
                + "thinner sliver of a crescent; 0 = the bite sits dead centre (a hole/ring).",
                v => Dirty(() => s.crescentOffset = v), 150f, showValue: true));
        }

        // Sparkle form rows — no Edge row (sparkles are hard pixels). Density (animatable) + the pixel block size.
        void BuildSparkleRows(PyrePlusSpec s)
        {
            s.sparkleDensity ??= new ZUIValue(0.35f);
            shapeBody.Add(WrapRow(
                Val("Density",
                    "Fraction of the disc's cells that sparkle, 0..1, over the particle's own life — a rising "
                    + "curve makes the sparkles ignite as it lives. Each lit cell also twinkles on/off per frame.",
                    s.sparkleDensity, 0f, 1f),
                Z.MicroSlider("Size px", s.sparkleSize, 1f, 4f,
                    "Size of each lit sparkle block in pixels (1 = single pixels, up to 4).",
                    v => Dirty(() => s.sparkleSize = Mathf.Clamp(Mathf.RoundToInt(v), 1, 4)), 150f,
                    showValue: true, decimals: 0)));
        }

        // Sprite form rows — no Edge row. The stamped image picker + the tint toggle, packed.
        void BuildSpriteRows(PyrePlusSpec s)
        {
            shapeBody.Add(WrapRow(
                Z.Field("Sprite",
                    "The image stamped at each particle. Its texture MUST have Read/Write enabled in its import "
                    + "settings, or it can't be sampled and the particle falls back to a plain disc.",
                    Z.Object<Sprite>(s.spriteImage,
                        "The stamped image — its texture needs Read/Write enabled (import settings), else the "
                        + "particle renders a disc fallback.",
                        v => Dirty(() => s.spriteImage = v), 160f)),
                Z.Toggle("Tint",
                    "Multiply the sprite by the Colour gradient at the particle's own life. Off = the sprite's own "
                    + "raw colours.",
                    s.spriteTint, v => Dirty(() => s.spriteTint = v))));
        }

        // Text form box — the string, the SDF font, spacing, the fill (mode + angle + gradient), the border (width
        // + gradient), and the 3D extrusion (Solid + Depth). Depth shows only when Solid, so the Solid toggle
        // rebuilds the Shape body. Text takes its colour from the Fill / Border gradients here — the shared Colour
        // row above is hidden for it. Size (above) is the character height; Tilt is the shared gemTilt (default);
        // per-letter Spin lives in the Advanced section.
        void BuildTextBox(PyrePlusSpec s)
        {
            s.textFillGradient ??= new Gradient();
            s.textBorderGradient ??= new Gradient();

            var box = Z.BoxKeyed("Text",
                "Every character of the string is one particle, rendered from a TMP SDF font atlas: a spatial "
                + "gradient fill, an optional border, and optional 3D extrusion. Needs a font with a READABLE "
                + "atlas — leave the Font empty to auto-pick one. When the Swarm is off the letters lay out as one "
                + "centred line; when it's on, each letter rides a swarm position.", "pyreplus.text");

            box.Add(Z.Field("Text",
                "The characters to render — one particle per character. The particle count follows the string "
                + "length (the Swarm's Count is hidden for Text).",
                Z.TextInput(s.textString ?? "", "The characters to render (one particle per character).",
                    v => Dirty(() => s.textString = v), 200f)));

            box.Add(Z.Field("Font",
                "The TMP SDF font asset. Its atlas must be Read/Write-enabled (a Dynamic SDF font works). Leave "
                + "empty to auto-pick the first readable font in the project; a missing/non-readable font falls back "
                + "to a plain disc per character.",
                Z.Object<TMP_FontAsset>(s.textFont,
                    "SDF font — needs a readable atlas; leave empty to auto-pick.",
                    v => Dirty(() => s.textFont = v), 200f)));

            box.Add(Z.MicroSlider("Spacing", s.textSpacing, 0.6f, 1.6f,
                "Letter advance multiplier for the centred line layout — below 1 tightens the letters, above 1 "
                + "spreads them apart. (Only affects the swarm-off line; a swarm places letters by its own shape.)",
                v => Dirty(() => s.textSpacing = v), 150f, showValue: true));

            box.Add(WrapRow(
                Z.Field("Fill",
                    "How the fill gradient is applied. Per-char gradient = each letter contains the whole gradient. "
                    + "Per-char step = each letter one flat colour along the gradient, by index. Text gradient = "
                    + "one gradient swept across the whole line (degrades to per-char step when the Swarm is on — "
                    + "there's no line to sweep). Text takes its colour from here, NOT the Colour gradient above.",
                    Z.Dropdown((int)s.textFillMode, TextFillModeChoices,
                        "Per-char gradient / per-char step / one gradient across the whole line.",
                        v => Dirty(() => s.textFillMode = (TextFillMode)v), 130f)),
                Z.MicroSlider("Angle", s.textGradientAngle, -180f, 180f,
                    "Rotates the fill axis. 0 = vertical bottom→top for per-char gradient; 0 = left→right across "
                    + "the line for text gradient. (Per-char step is index-based and ignores it.)",
                    v => Dirty(() => s.textGradientAngle = v), 150f, showValue: true)));

            box.Add(Z.Field("Fill grad",
                "The spatial fill ramp — this is where Text's colour comes from (the Colour gradient above is "
                + "unused for Text).",
                GradientField("Fill", () => s.textFillGradient, g => Dirty(() => s.textFillGradient = g))));

            box.Add(WrapRow(
                Z.MicroSlider("Border px", s.textBorderWidth, 0f, 4f,
                    "Letter outline width in screen pixels (0 = no border). Drawn as an SDF band just inside each "
                    + "glyph edge, coloured from the Border gradient.",
                    v => Dirty(() => s.textBorderWidth = v), 150f, showValue: true),
                Z.Field("Border grad",
                    "The border colour, sampled the same way as the fill. A single colour = a solid outline.",
                    GradientField("Border", () => s.textBorderGradient, g => Dirty(() => s.textBorderGradient = g)))));

            box.Add(Z.Toggle("Solid",
                "Extrude each letter into a 3D box (a lit front face + darker extrusion sides). Off = a flat 2D "
                + "letter plane.",
                s.textSolid, v => { Dirty(() => s.textSolid = v); RebuildShape(); }));
            if (s.textSolid)
                box.Add(Z.MicroSlider("Depth", s.textDepth, 0.05f, 1f,
                    "Extrusion depth as a fraction of the character size — how deep the 3D letter boxes are.",
                    v => Dirty(() => s.textDepth = v), 150f, showValue: true));

            shapeBody.Add(box);
        }

        // ── Swarm ──────────────────────────────────────────────────────────────────
        // The section is a stable header + a body container we clear/refill on every toggle/mode/kind
        // change, so the conditional controls appear/disappear without rebuilding the whole window.
        VisualElement swarmBody;
        static readonly string[] SwarmModeLabels = { "Area", "Path" };

        void BuildSwarm(VisualElement root, PyrePlusSpec s)
        {
            var swarmSection = Z.Section("Swarm", "Place many particles in a shape instead of one centred particle.");
            swarmBody = new VisualElement();
            swarmSection.Add(swarmBody);
            root.Add(swarmSection);
            RebuildSwarm();
        }

        void RebuildSwarm()
        {
            var s = spec;
            if (s == null || swarmBody == null) return;
            swarmBody.Clear();

            // The single gate: off ⇒ exactly one centred particle (Shape alone); nothing else shown. For the Text
            // form the count is the STRING LENGTH, so its Count field is hidden below.
            swarmBody.Add(Z.Toggle("Swarm",
                "Off = one centred particle (the Shape section alone; Text = one centred line). On = Count particles "
                + "placed in a shape. For the Text form the particle count is the number of characters, so Count is "
                + "hidden — each letter rides one swarm position.",
                s.swarmEnabled, v => { Dirty(() => s.swarmEnabled = v); RebuildSwarm(); }));
            if (!s.swarmEnabled) return;

            float half = Mathf.Max(1f, s.canvasSize * 0.5f);

            // Count + the two timing sliders (label + value INSIDE each MicroSlider), packed to reflow. Text hides
            // Count (the string length rules it) but keeps the spawn window + particle life.
            var timingRow = new List<VisualElement>();
            if (s.shapeForm != ShapeForm.Text)
                timingRow.Add(Z.Field("Count", "How many particles the swarm places (at least 2).",
                    Z.Int(s.swarmCount, "How many particles the swarm places (at least 2).",
                        v => Dirty(() => s.swarmCount = Mathf.Max(2, v)), 60f)));
            timingRow.Add(Z.MicroSlider("Spawn window", s.swarmSpawnWindow, 0f, 1f,
                "The slice of the timeline during which new particles appear (0 = all at frame 1, "
                + "1 = spawning continues to the last frame). WHEN each one appears inside the window is set "
                + "by Spawn timing.",
                v => Dirty(() => s.swarmSpawnWindow = v), 150f, showValue: true));
            timingRow.Add(Z.MicroSlider("Particle life", s.swarmParticleLife, 0.05f, 1f,
                "How long each particle lives, as a fraction of the timeline. Its colour/alpha/size envelopes "
                + "always play over ITS OWN life, not the timeline.",
                v => Dirty(() => s.swarmParticleLife = v), 150f, showValue: true));
            swarmBody.Add(WrapRow(timingRow.ToArray()));

            // WHEN each particle spawns inside the window — the particle-number → spawn-moment remap.
            swarmBody.Add(Val("Spawn timing",
                "Remaps WHEN each particle spawns inside the Spawn window. The X axis is WHICH particle "
                + "(0 = the first spawned, 1 = the last); the value is WHEN it spawns (0 = the window's start, "
                + "1 = its end). Linear = evenly spread (the default); ease it for a burst then a trickle; a flat "
                + "Static value spawns them all together at that moment; MinMax gives every particle a random moment.",
                s.swarmSpawnTiming, 0f, 1f));

            // Placement geometry: mode (Area vs Path) + the shape kind, packed together.
            string modeTip = "Area = particles fill the shape's interior; Path = particles ride along its outline.";
            var kindChoices = new List<string> { "Circle", "Triangle", "Square", "Pentagon", "Hexagon" };
            if (s.swarmSpawnMode == SwarmSpawnMode.Path) kindChoices.Add("Custom");   // Custom is Path-only
            string kindTip = "The swarm's outline — a regular polygon by side count (Circle = ∞ sides)"
                + (s.swarmSpawnMode == SwarmSpawnMode.Path ? ", or a hand-drawn Custom path." : ".");

            swarmBody.Add(WrapRow(
                Z.Field("Mode", modeTip,
                    Z.Segmented((int)s.swarmSpawnMode, SwarmModeLabels, modeTip, v =>
                    {
                        Dirty(() =>
                        {
                            s.swarmSpawnMode = (SwarmSpawnMode)v;
                            // Custom has no meaning in Area — snap it back to Circle so data + renderer agree.
                            if (s.swarmSpawnMode == SwarmSpawnMode.Area && s.swarmShapeKind == SwarmShapeKind.Custom)
                                s.swarmShapeKind = SwarmShapeKind.Circle;
                        });
                        RebuildSwarm();
                    })),
                Z.Field("Shape", kindTip,
                    Z.Dropdown((int)s.swarmShapeKind, kindChoices, kindTip,
                        v => { Dirty(() => s.swarmShapeKind = (SwarmShapeKind)v); RebuildSwarm(); }, 120f))));

            // Path-only: the progress envelope, sampled per-particle at its OWN spawn frame → a trail.
            if (s.swarmSpawnMode == SwarmSpawnMode.Path)
            {
                swarmBody.Add(Val("Spawn travel",
                    "Where the spawn point sits along the outline at each moment of the timeline (0 = start, "
                    + "1 = once around; values above 1 = more laps on closed shapes). Each particle LOCKS its spot "
                    + "at the moment it spawns — ease/hold/rewind this curve to cluster, stall or retrace the trail.",
                    s.swarmProgress, 0f, 8f));

                // Custom-only: the hand-drawn path — paired X/Y envelopes over progress, canvas-pixel offsets.
                if (s.swarmShapeKind == SwarmShapeKind.Custom)
                    swarmBody.Add(Val2D("Path",
                        "The hand-drawn path: numbered points in XY canvas-pixel offsets from the shape centre. "
                        + "Point order is progress around the path; each particle reads its spot by its own "
                        + "spawn-frame Progress.",
                        s.swarmCustomX, s.swarmCustomY,
                        new ZuiValue2DControl.Options()
                            .WithRange(-half, half, -half, half)
                            .WithDefault(Vector2.zero)
                            .Expanded()));
            }

            // Shared shape transform — every field a per-spawn snapshot (see the box tooltip).
            var xform = Z.BoxKeyed("Transform",
                "Offset, size, rotation and pseudo-3D tilt of the whole shape. Every field is a per-spawn "
                + "SNAPSHOT: each particle reads it at its OWN spawn moment and keeps that value for life. "
                + "Animating a field therefore does NOT move particles already placed — it spreads a TRAIL of new "
                + "spawns along the curve (rotate past 360, or travel past once-around, for several laps).",
                "pyreplus.transform");
            xform.Add(Val2D("Offset",
                "Shape-centre offset in canvas pixels — drag to move the whole shape off the origin. Animating it "
                + "does NOT slide placed particles; each takes the offset at its own spawn moment, leaving a trail.",
                s.shapeOffsetX, s.shapeOffsetY,
                new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)));
            xform.Add(WrapRow(
                Val("Scale (px)", "The shape's radius in canvas pixels. Animating this does NOT resize placed "
                    + "particles — each takes the radius at its own spawn moment, so a growing curve leaves a "
                    + "trail of expanding rings.", s.shapeScale, 0f, 64f),
                Z.Field("Snap", "Round the evaluated scale to the nearest multiple of this, so placements land on "
                    + "fixed radii. 0 = off.",
                    Z.Float(s.shapeScaleSnap,
                        "Round the evaluated scale to the nearest multiple of this (0 = off).",
                        v => Dirty(() => s.shapeScaleSnap = Mathf.Max(0f, v)), 50f))));
            xform.Add(Val("Rotation °",
                "Spin the whole shape in the canvas plane, in degrees. Animating this does NOT spin placed "
                + "particles — each particle takes the value at its own spawn moment, so a rising curve spreads "
                + "spawns around the shape (several laps if the curve goes past 360).",
                s.shapeRotation, -1440f, 1440f));
            xform.Add(Val2D("Tilt °",
                "Pseudo-3D tilt of the whole shape, in degrees: drag X to yaw (turn left/right), Y to pitch "
                + "(tip up/down). Nearer parts of the tilted shape render bigger and brighter. Animating it does "
                + "NOT re-tilt placed particles; each takes the tilt at its own spawn moment.",
                s.shapeYaw, s.shapePitch,
                new ZuiValue2DControl.Options().WithRange(-1440f, 1440f, -1440f, 1440f)
                    .WithDefault(Vector2.zero).WithAxisLabels("Yaw", "Pitch")));
            swarmBody.Add(xform);
        }

        // ── helpers ──────────────────────────────────────────────────────────────
        static VisualElement WrapRow(params VisualElement[] kids)
        {
            var r = Z.Row(kids); r.style.flexWrap = Wrap.Wrap; return r;
        }

        VisualElement Val(string label, string tooltip, ZUIValue v, float lo, float hi)
        {
            var o = new ZuiValueControl.Options
            {
                absMin = lo, absMax = hi,
                hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                controlWidth = 170f, grow = true,
            };
            return Z.Value(label, v, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre Plus"));
        }

        // 2D analog of Val — an animatable XY pair, same Undo-record + preview-dirty wiring.
        VisualElement Val2D(string label, string tooltip, ZUIValue x, ZUIValue y, ZuiValue2DControl.Options o)
            => Z.Value2D(label, x, y, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre Plus"));

        VisualElement GradientField(string label, System.Func<Gradient> get, System.Action<Gradient> set)
        {
            var gf = new UnityEditor.UIElements.GradientField { value = get() ?? new Gradient(), tooltip = label };
            gf.style.width = 200f;
            gf.RegisterValueChangedCallback(e => set(e.newValue));
            return gf;
        }

        void Dirty(System.Action apply)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Pyre Plus");
            apply();
            EditorUtility.SetDirty(spec);
            MarkDirty();
        }

        // DrawPreview + the Swarm authoring overlay live in PyrePlusWindow.Preview.cs.
    }
}
