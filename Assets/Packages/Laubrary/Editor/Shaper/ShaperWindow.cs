// ShaperWindow — the real Shaper authoring window. THIS FILE IS THE SHELL.
//
// Split as a partial class, mirroring how PyreWindow is split (PyreWindow.cs / .Forms.cs / .Preview.cs /
// .Modifiers.cs / .CherryFraming.cs). This file owns:
//   • the window, its one menu item, and the ZuiAssetWindow chrome (library browser, New/Duplicate/Rename/
//     Delete, Tags) that binds it to a document
//   • the layout skeleton (toggle bar + split, left = authoring, right = preview/transport)
//   • Canvas, Layers, and the selected layer's Shape/Transform cards
//   • THE SHARED HELPERS every later file must use: Change / Dial / Val / RefreshPreview / Current*
// Later slices add ShaperWindow.Sections.cs (Fill, Border, Shell, Sweep, Swarm, Light response, Height,
// Composite, Effects) and the preview chrome (backdrop, cherry framing).
//
// ── The helper contract, stated once ─────────────────────────────────────────────────────────────────────
// EVERY authored edit goes through Change(...) or through Dial/Val, never straight onto a field. That is what
// makes the tool Undo-safe, and Undo-safety is mandatory for every Laubrary tool. Change() records Undo on the
// DOCUMENT (the ScriptableObject) BEFORE mutating, which is the only ordering that works: RecordObject
// snapshots the pre-edit state, so recording after the mutation stores the value you just wrote and Ctrl+Z
// appears to do nothing.
//
// ── Why the mock's data model does NOT come across ───────────────────────────────────────────────────────
// ShaperMockWindow is a layout blueprint that survived many rounds of owner review, so its SHAPE is worth
// copying exactly. Its DATA MODEL is not the engine's, and three measured corrections override it:
//   • ZUIValue vs plain float is a PER-FIELD fact, never a category rule. The engine mixes both inside one
//     struct — ShaperPrimitiveDef's starLength/starBaseWidth/starSkew are ZUIValue (ShaperPrimitives.cs:39-43)
//     while every other primitive dimension beside them is a plain float. Check the field, then pick Val vs Dial.
//   • response / height / zOffset are LAYER fields (ShaperLayer, ShaperLightRig.cs), not node fields.
//   • The document is a ScriptableObject with no custom `name` field — use Object.name.
using System;
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.PyreShaper;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    // On ZuiAssetWindow, the same base Pyre's window uses: it owns the asset chrome — a thumbnail browser of
    // every document in the project, New into a conventional folder, Duplicate, Rename, Delete-behind-a-
    // confirm, and a Tags section — so opening Shaper offers a library rather than an empty object field.
    public partial class ShaperWindow : ZuiAssetWindow<ShaperDocument>
    {
        // Exactly ONE menu item, named for the tool. Nothing speculative — the project has repeatedly had to
        // hunt down and delete unrequested Laubrary/ entries.
        [MenuItem("Laubrary/Shaper")]
        public static void Open()
        {
            var w = GetWindow<ShaperWindow>("Shaper");
            w.minSize = new Vector2(820f, 520f);
        }

        // The edited document is the BASE's asset — one binding, serialized there, so the browser, New,
        // Duplicate, Rename and Delete all act on the same thing this window draws. Every partial reads
        // `document`, so the whole tool follows the browser with no other change.
        // The setter exists so an outside entry point can bind a document by writing the same name the rest of
        // the tool reads; it routes to the base's SetAsset, which is what makes the browser, the toolbar and
        // OnAssetChanged all agree about what is being edited.
        ShaperDocument document { get => Current; set => SetAsset(value); }
        [SerializeField] int selectedLayer;
        [SerializeField] int currentFrame;

        // ── the asset chrome (ZuiAssetWindow contract) ───────────────────────────────────────────────────

        protected override string TypeLabel => "Shaper";
        protected override string NewAssetName => "New Shaper";
        protected override string DefaultFolder => "Assets/Shaper";
        // T-0187 — place TagsSection ourselves, below the toggle bar, instead of the base's default
        // above-everything placement (see BuildAsset). Pyre and every other ZuiAssetWindow subclass leaves
        // this true and is unaffected.
        protected override bool AutoInsertTagsSection => false;

        /// A document with no layer has nothing to render and no affordance that suggests what to do, so a
        /// new one starts with a single primitive layer — the first run is the only run every user gets.
        protected override void InitializeNewAsset(ShaperDocument item)
        {
            if (item == null) return;
            item.layers.Add(NewLayer("Layer 1", item));
        }

        /// Browsing to another document must not carry the previous one's layer selection, frame or a running
        /// playback loop — a stale layer index would land on a different shape, and a live tick would keep
        /// driving the old asset's clock.
        protected override void OnAssetChanged()
        {
            selectedLayer = 0;
            currentFrame = 0;
            playing = false;
            EditorApplication.update -= PlaybackTick;
        }

        /// The browser cell. Rendered through the SAME document renderer as the preview and the bake, WITH
        /// the effect applier the bridge assembly supplies — so a document whose look depends on its effects
        /// is shown as it actually is. (ShaperDocument's own IVisualPreview cannot reach the applier from
        /// Runtime; that path is the fallback for consumers outside this assembly.)
        protected override Texture2D RenderThumbnail(ShaperDocument item)
        {
            if (item == null) return null;
            int mid = Mathf.Clamp(item.frameCount / 2, 0, Mathf.Max(0, item.frameCount - 1));
            return RenderDocumentInto(item, mid, null);
        }

        // A Shaper frame costs tens of milliseconds to evaluate, so this is deliberately paired with the
        // base's default "hover to preview" mode rather than animating every cell at once.
        protected override bool AnimateThumbnails => true;

        protected override void UpdateAnimatedThumbnail(ShaperDocument item, Texture2D tex, double time)
        {
            if (item == null || tex == null || item.frameCount <= 1) return;
            int f = ShaperClock.WrapFrame(
                Mathf.Abs(Mathf.FloorToInt((float)(time * Mathf.Max(1f, item.frameRate)))), item.frameCount);
            RenderDocumentInto(item, f, tex);
        }

        /// Render one frame into <paramref name="into"/>, or into a fresh texture when it is null (which the
        /// base then owns and destroys). Point-filtered and unmipped, matching the preview stage and the
        /// bake's own importer settings — a filtered thumbnail would lie about a pixel-art asset.
        static Texture2D RenderDocumentInto(ShaperDocument doc, int frame, Texture2D into)
        {
            int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight);
            var px = ShaperDocumentRenderer.RenderFrame(doc, frame, ShaperEffectApplier.Instance);
            if (px == null || px.Length != w * h) return into;

            var tex = into;
            if (tex == null)
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            else if (tex.width != w || tex.height != h) tex.Reinitialize(w, h);

            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        // ── transport ────────────────────────────────────────────────────────────────────────────────────
        bool playing;
        Button playButton;
        ZuiMicroSlider scrubber;
        double lastPlayTick;
        float playAcc;

        // T-0190 — the transport lives in a host that can be REFILLED without rebuilding the window. Raising
        // Frames has to widen the scrubber's range, re-tick the cache strip and re-tile the filmstrip, and a
        // ZuiMicroSlider's range is fixed at construction; a whole-window Rebuild would do it but would also
        // pull the Frames dial out from under the pointer mid-drag. Refilling only the right pane's transport
        // leaves the control being dragged untouched.
        VisualElement transportHost;
        Label frameReadout;

        // T-0190 — the cherry playhead. Under cherry framing the Frame slider is a SOURCE-frame index that
        // only ever visits the frames the sequence names, which looks like a transport that has stopped; the
        // playhead says where playback really is, as (slot, beat).
        ZuiMicroSlider cherryScrubber;

        // T-0190 — Pyre's Strip mode (Editor/Pyre/PyreWindow.cs:542-552). Window state, not document state,
        // for the same reason previewZoom is: it is a view preference, and a preference that dirties an
        // authored asset every time somebody reclaims some height is worse than one that resets. Defaults ON
        // because that is what the tool already shipped — the toggle is here so the owner can take the height
        // back, not to hide a feature that was visible yesterday.
        [SerializeField] bool previewStrip = true;
        [SerializeField] float previewStripTile = 40f;

        // T-0190 — how long playback may hold, waiting for the next frame to finish caching, before it steps
        // on regardless. Holding is what keeps a fresh document smooth (see PlaybackTick); holding WITHOUT a
        // bound is what pinned the transport when a sustained dial-drag re-invalidated the cache faster than
        // the pre-baker could refill it, which is the checklist's row 4.3.
        const double MaxCacheHoldSeconds = 0.25;
        double holdingSince = -1.0;

        // T-0190 — the plain loop's blank gap. Wall-clock, because a gap is a duration and not a frame; while
        // it runs, previewFrame reports the engine's own blank sentinel so the stage shows nothing rather than
        // a held frame, which is the same honesty the cherry gap already had.
        double plainLoopBlankUntil = -1.0;

        // T-0190 — the preview-overlay toggle strip's PERMANENTLY reserved row (authoring.md §15). Built once
        // and only ever refilled, so it cannot reflow the preview above it when a swarm is enabled.
        VisualElement overlayStripHost;
        List<ShaperPreviewOverlays.Entry> overlayEntries = new List<ShaperPreviewOverlays.Entry>();

        // T-0165 — the cached-frame ticks + "N/M cached" readout under the scrubber. Owned here (not
        // ShaperWindow.Preview.cs) because they live inside BuildTransport, which this file owns.
        ShaperCacheTickStrip cacheTickStrip;
        Label cacheReadoutLabel;

        // T-0176 — the filmstrip contact sheet, same ownership reasoning as cacheTickStrip above (built
        // inside BuildTransport). Its own cache (ShaperFilmstrip.cs) — see that file's header for why it does
        // not share ShaperPreviewStage's.
        ShaperFilmstripElement filmstrip;

        // ── chrome ───────────────────────────────────────────────────────────────────────────────────────
        ShaperPreviewStage stage;
        VisualElement layerListHost, toggleBarHost;
        ZuiSection canvasSection, layersSection, shapeSection, transformSection;
        ScrollView leftPane;
        Vector2 carriedScroll;

        // ── lifecycle ────────────────────────────────────────────────────────────────────────────────────

        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();
            if (leftPane != null) carriedScroll = leftPane.scrollOffset;
            leftPane = null;
            // The stage owns a Texture2D. A rebuild drops the element without detaching it in every path, so
            // dispose explicitly rather than relying on DetachFromPanel alone.
            stage?.Dispose();
            stage = null;
            filmstrip?.Dispose();
            filmstrip = null;
            DisposeCherryThumbs();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= PlaybackTick;
            stage?.Dispose();
            filmstrip?.Dispose();
            DisposeCherryThumbs();
        }

        /// The per-document editor. The base owns everything above it — the toolbar, the New/Rename prompts,
        /// the Tags section and the browser — and only calls this once a document is selected, so the empty
        /// state IS the library browser rather than a disabled workbench.
        protected override void BuildAsset(VisualElement root, ShaperDocument item)
        {
            root.style.minHeight = 0f;

            toggleBarHost = new VisualElement();
            root.Add(toggleBarHost);

            // T-0187 — Tags renders BELOW the toggle bar, in its own permanently-placed row, so folding it
            // never moves the bar above it (the owner: "the toggle bar jumps up and down if you toggle the
            // tags"). The base builds TagsSection but, because AutoInsertTagsSection is overridden false
            // below, leaves placing it to us instead of adding it above the toolbar/host split itself.
            if (TagsSection != null) root.Add(TagsSection);

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 320f;
            left.style.minHeight = 0f;
            leftPane = left;

            // T-0182 — the dial pane is ONE width-driven column flow, as Pyre's is (PyreWindow.cs:328-334).
            // Each Build* below adds exactly one top-level unit, in the order they read down column 1, then
            // column 2, …; dragging the splitter past 2×360 splits the stack into two columns, past 3×360
            // into three, up to four. Nothing reaches inside a unit, so every section's own rebuild helper
            // keeps working wherever its card lands. The ScrollView's content container is content-sized by
            // default — stretching it is what lets the flow see the pane's width and ever split at all.
            left.contentContainer.style.flexGrow = 1f;
            var flow = Z.ColumnFlow(360f);
            left.contentContainer.Add(flow);

            BuildViewsSection(flow);    // T-0190 — saved views, Pyre's own bar (Editor/Pyre/PyreWindow.cs:453)
            BuildCanvasSection(flow);
            BuildLightsSection(flow);   // ShaperWindow.Lights.cs (T-0164) — document-level, sits beside Canvas
            BuildLayersSection(flow);
            BuildSelectedLayerSections(flow);
            RestoreScroll(left);
            // After every card exists, or the bar would restore a view against half a window and silently
            // drop the folds of the sections that had not been built yet.
            viewBar?.RestoreLast();

            var right = new VisualElement();
            right.style.minWidth = 260f;
            right.style.minHeight = 0f;
            BuildRight(right);

            // 560 initial divider, matching the mock's own measured reasoning: Shaper is a dense workbench and
            // the packed dial rows only pay off once the left pane opens wide enough to show more than a pair
            // per row, without forcing the user to discover the drag-to-widen affordance first.
            root.Add(Z.Split("shaper.window.split.v1", 560f, left, right));
            RefreshToggleBar();
        }

        // T-0187 — fixed roster and order: "the toggle bar should only have sections that almost all cases
        // use" (owner). Tags no longer appears here at all — it renders below the bar via its own header
        // fold (see BuildAsset) instead of being bar-controlled. Height and Mask no longer appear here
        // either — they moved into the Layers section as per-layer cards (BuildLayersSection). Built as a
        // label→section lookup rather than the old fixed interleave, so the exact order below is the only
        // thing that decides what the bar shows and in what sequence, regardless of which file owns which
        // section instance.
        static readonly string[] ToggleBarOrder =
        {
            "Canvas", "Layers", "Shape", "Transform", "Fill", "Border",
            "SpriteFX", "Global SpriteFX", "Swarm", "Lights", "Lighting",
        };

        void RefreshToggleBar()
        {
            if (toggleBarHost == null) return;
            toggleBarHost.Clear();
            var byLabel = new Dictionary<string, ZuiSection>
            {
                ["Canvas"] = canvasSection,
                ["Lights"] = lightsSection,   // ShaperWindow.Lights.cs (T-0164)
                ["Layers"] = layersSection,
                ["Shape"] = shapeSection,
                ["Transform"] = transformSection,
            };
            foreach (var (label, section) in SectionBarEntries())   // the cards ShaperWindow.Sections.cs owns
                byLabel[label] = section;

            // Null section entries are skipped by the bar itself, so an absent card costs nothing here —
            // which is what lets the shell list every card unconditionally while the absence rule decides
            // at build time which ones actually exist for the current node kind.
            var entries = new List<(string, ZuiSection)>(ToggleBarOrder.Length);
            foreach (var label in ToggleBarOrder)
                entries.Add((label, byLabel.TryGetValue(label, out var s) ? s : null));
            toggleBarHost.Add(new ZuiSectionToggleBar("ShaperWindow", entries.ToArray()));
        }

        // ── saved views (T-0190, checklist 11.2 — the shared ZuiViewBar + a committed ZuiViewStore) ──────
        // Shaper is the densest workbench in the package: nine-plus cards over ~775 authored fields, most of
        // which any given session does not touch. Pyre answers that with named view presets and Shaper had
        // nothing, so the only way to get a workable layout was to re-fold the same cards every time the
        // window rebuilt. Same store class, same bar, same shape — a second dialect of "saved views" would be
        // the mistake, not a second store.
        const string ViewStorePath = "Assets/Shaper/ShaperViews.asset";
        const string ViewPrefsKey = "Shaper.lastView";
        ZuiSection viewsSection;
        ZuiViewBar viewBar;

        void BuildViewsSection(VisualElement root)
        {
            viewBar = BuildViewBar(root);
            viewsSection = Z.Section("Views",
                "Save and recall named layouts of this window — which cards are folded open and which optional "
                + "controls are shown. View state only: no authored value is ever stored in a view.",
                "shaper.window.views", icon: "bookmarks-simple");
            viewsSection.Add(viewBar);
            root.Add(viewsSection);
        }

        /// Capture/apply aggregate every ZuiBox under <paramref name="paneRoot"/>, so a view round-trips every
        /// card's fold + gear + shown-control state without this window listing its own cards — a list that
        /// would go stale the first time a section was added. The store asset is committed and shared; only
        /// the per-user "last view" pointer is in EditorPrefs. The bar never touches AssetDatabase itself.
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

        /// Mint the Shaper views asset — only ever called from the bar's Save-as, and only when none exists.
        /// Undo-registered per the Laubrary Undo rule; creates the conventional folder if it is missing, which
        /// is the same folder a new document is written into (DefaultFolder above).
        ZuiViewStore CreateViewStore()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Shaper"))
                AssetDatabase.CreateFolder("Assets", "Shaper");
            var store = ScriptableObject.CreateInstance<ZuiViewStore>();
            AssetDatabase.CreateAsset(store, ViewStorePath);
            Undo.RegisterCreatedObjectUndo(store, "Create Shaper Views");
            return store;
        }

        void RestoreScroll(ScrollView view)
        {
            if (carriedScroll == Vector2.zero) return;
            Vector2 wanted = carriedScroll;
            view.schedule.Execute(() => view.scrollOffset = wanted).ExecuteLater(0);
        }

        // ── document seeding ─────────────────────────────────────────────────────────────────────────────

        /// A layer with a null root resolves to nothing. FC-3.2 also guarantees a layer ROOT always owns a
        /// fill (the resolver substitutes ShaperFillDef.DefaultRootFill when the slot is empty), so a bare
        /// node is already renderable without a Fill card existing yet.
        ///
        /// The primitive is SIZED TO THE CANVAS rather than left on its engine defaults, and that is a
        /// first-run finding rather than a preference. ShaperPrimitiveDef defaults to a Rect with 50/50
        /// half-extents — 100x100 canvas units — while a default document is 96x64. Walking the tool cold
        /// showed the result: the very first thing a new user sees after "New document -> Add layer" is a
        /// FEATURELESS GREY BLOCK filling the entire canvas, with no visible edge, no sense of scale, and no
        /// way to tell the tool is even working. Every individual part passed its probe; the task still
        /// produced nothing legible, which is exactly the failure an API-level check cannot see.
        ///
        /// A quarter-canvas rect leaves margin on every side at any canvas size, so the first render reads
        /// as a shape sitting ON a canvas. Engine defaults are deliberately NOT touched — this is the
        /// window's seeding choice, and a document authored by any other route keeps the engine's own values.
        ///
        /// ── T-0190: a fresh layer MOVES ─────────────────────────────────────────────────────────────────
        /// Checklist row 5.3. Every engine default is a Static ZUIValue (ShaperPrimitives.cs), so before this
        /// a new document played sixteen identical pictures: Play worked perfectly and looked broken. Pyre
        /// solves the same problem by defaulting two dials to CURVES — `alpha` (fade in, hold, fade out) and
        /// `size` (grow, overshoot, settle), Pyre.cs:325-326 and its DefaultAlpha/DefaultSize. The two are
        /// chosen for a reason worth restating: SIZE is the motion you can see at any colour, and ALPHA is the
        /// motion you can see at any size, so between them a first render animates whatever else the author
        /// then does to it.
        ///
        /// Seeded here, and only here, exactly like the quarter-canvas sizing above: this is the WINDOW's
        /// first-run choice, so the engine defaults stay Static and a document authored by any other route —
        /// a script, a test, a bake — is bit-identical to what it always was. Switching an existing node to
        /// another shape does NOT re-seed either, because that would overwrite dials the author has tuned.
        static ShaperLayer NewLayer(string name, ShaperDocument doc)
        {
            var layer = new ShaperLayer
            {
                name = name,
                enabled = true,
                root = new ShaperNode { name = "Shape" },
            };
            var p = layer.root.primitive;
            if (doc != null && p != null)
            {
                p.EnsureDials();
                float hw = Mathf.Max(2f, doc.canvasWidth * 0.25f);
                float hh = Mathf.Max(2f, doc.canvasHeight * 0.25f);
                // Grow in, overshoot, settle — Pyre's DefaultSize shape, expressed against THIS canvas rather
                // than Pyre's fixed 0..24 pixel radius, so the seeded animation is in proportion at 32×32 and
                // at 256×256 alike. The settle point is the same quarter-canvas rect the still default was, so
                // the shape a user sees at rest is unchanged by this seeding.
                p.rectHalfWDial = SeededGrowth(hw);
                p.rectHalfHDial = SeededGrowth(hh);
            }
            // The layer ROOT's fill is normally left null (the resolver substitutes DefaultRootFill), but a
            // null fill has no dial to animate — so the seed materialises that same default and puts Pyre's
            // alpha envelope on its veil. Identical in colour and compositing to the substituted default; the
            // only difference is that it now fades.
            var fill = ShaperFillDef.DefaultRootFill();
            fill.veil = SeededVeil();
            layer.root.fill = fill;
            return layer;
        }

        /// Pyre's DefaultSize curve shape (grow, overshoot, settle), rescaled to a canvas-relative settle
        /// value: 0.55 → overshoot 1.15 → settle 1.0, as fractions of <paramref name="settle"/>.
        ///
        /// Pyre starts its own size curve at a sixth of its peak, which on a particle reads as a spark
        /// appearing. Measured here on a fresh 96×64 document that gave frame 0 sixty lit pixels out of six
        /// thousand — an animation that begins, correctly, with almost nothing on screen, which is the same
        /// "did it work?" first run this seeding exists to fix. Starting at just over half keeps the growth
        /// plainly visible (frame 0 to the peak is still a doubling) while frame 0 is unmistakably a shape.
        static ZUIValue SeededGrowth(float settle)
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = Mathf.Max(1f, settle * 1.25f) };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, settle * 0.55f));
            v.points.Add(new ZUIEnvelopePoint(0.45f, settle * 1.15f));
            v.points.Add(new ZUIEnvelopePoint(1f, settle));
            return v;
        }

        /// Pyre's DefaultAlpha envelope (fade in fast, hold, fade out) on the root fill's veil — Shaper's own
        /// "how much of this fill reaches the picture" dial.
        ///
        /// One deliberate departure from Pyre's exact numbers: the ends are 0.45 and 0.5, not 0. A PARTICLE
        /// may be born from nothing and die to nothing, because the frame it is invisible on is one of
        /// thousands. A DOCUMENT's frame 0 is the first and often the only thing a user sees — it is the
        /// thumbnail, it is what a paused transport shows, it is what a still bake writes. Seeding a fade from
        /// zero would fix "the animation does not move" by replacing it with "the picture is empty", which is
        /// the same first-run failure wearing a different hat.
        static ZUIValue SeededVeil()
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0.45f));
            v.points.Add(new ZUIEnvelopePoint(0.15f, 1f));
            v.points.Add(new ZUIEnvelopePoint(0.7f, 1f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0.5f));
            return v;
        }

        // ── Canvas ───────────────────────────────────────────────────────────────────────────────────────

        void BuildCanvasSection(VisualElement root)
        {
            var box = canvasSection = Z.Section("Canvas",
                "The document's own resolution, sampling density, layer spacing, animation clock and seed.",
                "shaper.window.canvas", icon: "frame-corners");

            // One continuous HGroup rather than several: an overflowing field then lands beside the NEXT
            // field instead of alone on a line of its own.
            box.Add(Z.HGroup(
                Dial("Width", "Canvas width in samples. B9's authorable range is 32–256.",
                    document.canvasWidth, 32f, 256f, v => document.canvasWidth = Mathf.RoundToInt(v), decimals: 0),
                Dial("Height", "Canvas height in samples.",
                    document.canvasHeight, 32f, 256f, v => document.canvasHeight = Mathf.RoundToInt(v), decimals: 0),
                Dial("Pixel size", "Canvas units per sample. 1 makes a \"canvas pixel\" in a dial equal one "
                    + "sample (LR-1.5).", document.pixelSize, 0.1f, 8f, v => document.pixelSize = v),
                Dial("Layer spacing", "Canvas pixels between consecutive layers' base planes, and so how far "
                    + "apart in depth they sit: layers are composited by which surface is nearest, and a layer "
                    + "whose height rises more than this above the one below it breaks through it. 0 puts "
                    + "every base plane together, where list order decides.",
                    document.layerSpacing, 0f, 8f, v => document.layerSpacing = v)));

            box.Add(Z.HGroup(
                // T-0190 (checklist 2.2) — the frame axis is the one dial the TRANSPORT is built from, so it
                // refills the transport as it moves: a new scrubber range, re-shaped cache ticks, a re-tiled
                // filmstrip and a corrected "frame N/M" readout, immediately rather than at the next window
                // rebuild. FillTransport touches only the right pane, so the dial being dragged is untouched.
                Dial("Frames", "How many frames this document resolves to. 1 is a still document, where every "
                    + "frame maps to phase 0 — the transport stays, with Play disabled.",
                    document.frameCount, 1f, 120f,
                    v =>
                    {
                        document.frameCount = Mathf.Max(1, Mathf.RoundToInt(v));
                        FillTransport();
                    }, decimals: 0),
                Dial("Rate", "Playback rate in frames per second — how fast frames are shown here and how fast "
                    + "a baked clip plays. Nothing in the render pipeline reads it; the render is driven by "
                    + "phase, not by a wall clock.", document.frameRate,
                    ShaperClock.MinFrameRate, ShaperClock.MaxFrameRate, v => document.frameRate = v, decimals: 0),
                Z.Field("Seed",
                    "The seed every deterministic draw in this document derives from — Min-Max light dials and "
                    + "cherry-frame picks. Same seed, same result, every time.\n\n"
                    + "The document stores this as a uint; this control covers 0…2147483647, so the top half of "
                    + "that range is not reachable from here. That is deliberate: a scrub-draggable number is "
                    + "far more useful for a seed than a hex box, and no authored workflow needs the high half.",
                    // Math.Min on uint, NOT Mathf.Min: Mathf.Min would route the value through float, whose
                    // 24-bit mantissa silently corrupts any seed above ~16.7 million — and a seed that changes
                    // when you merely look at it is the worst possible bug in a determinism feature.
                    Z.Int((int)Math.Min(document.seed, (uint)int.MaxValue),
                        "Seed for this document's deterministic draws (0…2147483647).",
                        v => Change(() => document.seed = (uint)Mathf.Max(0, v)), 80f))));

            box.Add(Z.HGroup(
                Z.Field("Background",
                    "Composited UNDER every layer, so a bake, a GIF and a baked clip all carry it. Transparent "
                    + "by default. Distinct from the preview-only backdrop below — this one reaches the shipped "
                    + "picture, that one never does.",
                    Z.Color(document.background,
                        "Composited UNDER every layer, so a bake, a GIF and a baked clip all carry it. "
                        + "Transparent by default.",
                        v => Change(() => document.background = v))),
                Dial("PPU", "Screen pixels per world unit for a baked sprite — the same convention Pyre's own "
                    + "pixelsPerUnit carries. Not the same as Pixel size above, which is a sampling density, not "
                    + "a display scale.", document.pixelsPerUnit, 1f, 64f,
                    v => document.pixelsPerUnit = Mathf.Clamp(Mathf.RoundToInt(v), 1, 64), decimals: 0)));

            root.Add(box);
        }

        // ── Layers ───────────────────────────────────────────────────────────────────────────────────────

        void BuildLayersSection(VisualElement root)
        {
            var box = layersSection = Z.Section("Layers",
                "The document's layers, bottom-most first. Order is authored data — no stage reorders it, so "
                + "what you see here is the stacking order.",
                "shaper.window.layers", icon: "stack");

            layerListHost = new VisualElement();
            box.Add(layerListHost);
            RebuildLayerList();

            box.Add(Z.HGroup(
                Z.Button("+ Add layer", "Add a new layer above the current top layer.", () =>
                {
                    Change(() => document.layers.Add(NewLayer("Layer " + (document.layers.Count + 1), document)));
                    selectedLayer = document.layers.Count - 1;
                    Rebuild();
                }),
                Z.Button("Duplicate", "Duplicate the selected layer just after itself (undoable).", () =>
                {
                    var src = CurrentLayer;
                    if (src == null) return;
                    Change(() =>
                    {
                        var copy = src.Clone();
                        copy.name = (src.name ?? "Layer") + " copy";
                        int at = Mathf.Clamp(selectedLayer + 1, 0, document.layers.Count);
                        document.layers.Insert(at, copy);
                        selectedLayer = at;
                    });
                    Rebuild();
                })));

            // T-0192 — the SELECTED layer's Z (depth) dial, on its own row below the list rather than crammed
            // into every layer's row, where it overflowed a 3-column dial pane (see BuildLayerRow's comment
            // for the width arithmetic). Not gated on frameCount — depth ordering matters on a still document
            // too, unlike Lifetime below which is meaningless without a frame axis.
            if (CurrentLayer != null)
            {
                var zLay = CurrentLayer;
                box.Add(Val("Z", "Moves the selected layer (“" + (zLay.name ?? "Layer") + "”) in depth, in "
                    + "canvas pixels, on top of its place in the list (layer index × layer spacing). It "
                    + "re-orders as well as shades: push a layer back far enough and the ones below it come "
                    + "through, and two raised shapes at different depths intersect along a curve instead of "
                    + "one hiding the other.", zLay.zOffset, -256f, 256f));
            }

            // T-0166 — the SELECTED layer's lifetime window, on its own row below the list rather than crammed
            // into every layer's row (PM by-eye vet, pm-vet-wave2-light-crop.png: a bare unlabeled slider was
            // overlapping the Dup button). Gated on frameCount > 1 like the transport — a still document has no
            // frame axis for "outside its window" to mean anything against.
            if (document.frameCount > 1 && CurrentLayer != null)
            {
                var lay = CurrentLayer;
                int lo = lay.startFrame;
                int hi = lay.endFrame < 0 ? document.frameCount - 1 : lay.endFrame;
                box.Add(Z.Field("Lifetime",
                    "The SELECTED layer's (“" + (lay.name ?? "Layer") + "”) frame lifetime window — "
                    + "the frames it contributes to. Outside this range the layer renders nothing, exactly as a "
                    + "disabled layer does.",
                    Z.MicroMinMax("Lifetime", lo, hi, 0f, document.frameCount - 1,
                        "The selected layer's frame lifetime window — the frames it contributes to.",
                        (newLo, newHi) => Change(() =>
                        {
                            lay.startFrame = Mathf.RoundToInt(newLo);
                            // Only write a real endFrame when it no longer means "the last frame" — keeps an
                            // unauthored window at its -1 default through a later frameCount change.
                            int rh = Mathf.RoundToInt(newHi);
                            lay.endFrame = rh >= document.frameCount - 1 ? -1 : rh;
                        }), 200f, decimals: 0)));
            }

            // T-0187 — Height and Mask stop being their own toggle-bar sections ("I get the feeling you
            // shouldn't make them into sections... perhaps part of the layer item in the layer list", owner
            // feedback) and become foldable cards here instead, right beside the Lifetime card, for the
            // SELECTED layer only. Built in ShaperWindow.Sections.cs (same file that used to own them as
            // sections) — this call site only decides WHERE they land now.
            if (CurrentLayer != null)
            {
                BuildHeightSection(box, CurrentLayer);
                BuildMaskSection(box, CurrentLayer);
            }

            root.Add(box);
        }

        void RebuildLayerList()
        {
            if (layerListHost == null) return;
            layerListHost.Clear();
            for (int i = 0; i < document.layers.Count; i++)
                layerListHost.Add(BuildLayerRow(layerListHost, i));
        }

        VisualElement BuildLayerRow(VisualElement listHost, int index)
        {
            var layer = document.layers[index];
            int li = index;
            bool sel = index == Mathf.Clamp(selectedLayer, 0, document.layers.Count - 1);

            var row = new VisualElement();
            row.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this layer. Layer order is the stacking "
                + "order and is authored data.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, row, listHost, (from, to) =>
            {
                Change(() =>
                {
                    var l = document.layers[from];
                    document.layers.RemoveAt(from);
                    document.layers.Insert(to, l);
                });
                selectedLayer = to;
                Rebuild();
            });
            row.Add(grip);

            row.Add(Z.Toggle("", "Show or hide this layer. A disabled layer paints nothing.",
                layer.enabled, v => { Change(() => layer.enabled = v); RefreshPreview(); }));

            row.Add(Z.Button(sel ? "●" : "○", "Select this layer to edit its shape below.",
                () => { selectedLayer = li; Rebuild(); }).W(24f));

            row.Add(Z.TextInput(layer.name ?? "", "This layer's name.",
                v => Change(() => layer.name = v), 150f));

            // T-0166 — the lifetime window used to live HERE as a bare, unlabeled slider, and PM's by-eye vet
            // (pm-vet-wave2-light-crop.png) caught it: no label, no readable min/max, running underneath the
            // Dup button. The row is already tight (grip/toggle/select/name/Dup/×), so per ui-layout-rules'
            // "Card layout" a control this wide earns its OWN row instead of being crammed into every layer's —
            // see BuildLayersSection's "Lifetime" card below the list, which edits the SELECTED layer only.
            //
            // T-0192 — Z (zOffset) used to sit HERE too, on every row (row.Add(Val("Z", ...))). It is a real
            // Z.Value row (zOffset is ZUIValue, not a plain float), and even at its 170px norm the row —
            // grip(16) + toggle + select(24) + name(150) + Z(170) + Dup(40) + ×(22), ~440px of fixed content
            // before any gap — cannot fit the ≥360px column ui-layout-rules requires once the dial pane
            // reflows to 3 ColumnFlow columns: Z clipped and Dup/× painted over the neighbouring column
            // (PM by-eye, workspace/T-0180/walk2-03-strip-off.png). Moved beside Lifetime below, for the
            // SELECTED layer only — the same move T-0166 already made for Lifetime, for the same reason,
            // and it needs no frameCount gate since depth ordering means something even on a still document.

            row.Add(Z.Flexible());

            // Per-row Duplicate (T-0166, Pyre parity — PyreWindow.cs:887). Deep-clones THIS row's layer,
            // inserts the copy just after it, and selects the copy. The Layers toolbar's own "Duplicate"
            // button duplicates the SELECTED layer; this is the per-layer one, by the row it belongs to.
            row.Add(Z.Button("Dup", "Duplicate this layer just after itself (undoable).", () =>
            {
                Change(() =>
                {
                    var copy = layer.Clone();
                    copy.name = (layer.name ?? "Layer") + " copy";
                    int at = Mathf.Clamp(li + 1, 0, document.layers.Count);
                    document.layers.Insert(at, copy);
                    selectedLayer = at;
                });
                Rebuild();
            }).W(40f));

            row.Add(Z.Button("×",
                document.layers.Count <= 1
                    ? "This is the document's only layer, so it cannot be removed — a document with no layers "
                      + "renders nothing and offers no way back. Add another layer first."
                    : "Remove this layer.",
                () =>
                {
                    // T-0190 (checklist 3.6, Pyre's own guard at Editor/Pyre/PyreWindow.cs:902). A zero-layer
                    // document is renderable-as-nothing and has no affordance suggesting what to do — exactly
                    // the dead end InitializeNewAsset seeds a layer to avoid, reachable again through delete.
                    // Refused with a notification rather than a dialog: the answer is "not this one", not a
                    // decision the user has to make.
                    if (document.layers.Count <= 1)
                    {
                        ShowNotification(new GUIContent("A Shaper document needs at least one layer."));
                        return;
                    }
                    Change(() => document.layers.Remove(layer));
                    selectedLayer = Mathf.Clamp(selectedLayer, 0, Mathf.Max(0, document.layers.Count - 1));
                    Rebuild();
                }).W(22f));

            return row;
        }

        // ── the selected layer's node ────────────────────────────────────────────────────────────────────

        /// The layer currently being edited, or null when the document has none.
        internal ShaperLayer CurrentLayer =>
            document == null || document.layers.Count == 0
                ? null
                : document.layers[Mathf.Clamp(selectedLayer, 0, document.layers.Count - 1)];

        /// The node the authoring cards edit. Phase C2 added bag drill-down, so this now resolves through the
        /// drill path (ShaperWindow.Sections.cs) instead of always returning the layer root — exactly the
        /// swap this property's own comment anticipated, and every card that reads it kept working unchanged.
        internal ShaperNode CurrentNode => ResolveCurrentNode();

        void BuildSelectedLayerSections(VisualElement root)
        {
            var node = CurrentNode;
            if (node == null) return;

            BuildShapeSection(root, node);
            BuildTransformSection(root, node);
            BuildAuthoringSections(root, node);
        }

        void BuildShapeSection(VisualElement root, ShaperNode node)
        {
            // ONE section, ONE question (T-0182). "Shape" and "Generator" were two cards asking the same
            // thing — what does this node draw? — so the kind/primitive radios and the separate Generator
            // card are gone, replaced by a single picker (ShaperShapePicker.cs) listing every source there
            // is. What the picker chose then decides which dials appear below it: the absence rule, so a
            // node never shows the dials of a shape it is not.
            var box = shapeSection = Z.Section("Shape",
                "What this node draws, and the dials that shape it.",
                "shaper.window.shape", icon: "shapes");

            // The suffix is what a FOLDED Shape card says — without it a collapsed card hides the single
            // most important fact about the node. The caret opens the same picker as the button below, so
            // the choice is reachable whether the card is open or shut.
            box.SetHeaderSuffix(() => " — " + (ShaperShapeCatalog.Current(node)?.Label ?? "(none)"));
            box.SetHeaderMenu("caret-down", "Choose what this node draws (or right-click the title).",
                anchor => ShowShapeMenu(node, anchor));

            box.Add(BuildShapePickerRow(node));

            switch (node.kind)
            {
                case ShaperNodeKind.Primitive: BuildPrimitiveBody(box, node.primitive); break;
                case ShaperNodeKind.Solid: BuildSolidBody(box, node); break;
                case ShaperNodeKind.Composite: BuildCompositeBody(box, node); break;
                case ShaperNodeKind.Bag: BuildChildrenBody(box, node); break;
            }

            // The combine op and the join dials only mean something for a node that has siblings to combine
            // WITH, so they are drawn only for a bag member; the sweep and shell carve this node's own
            // geometry and apply wherever it sits, so they are not gated. (Both used to live in a card
            // called "Modifiers", which they never were — a modifier is an effect on the picture, these
            // are part of the shape.)
            BuildShapeOpsBody(box, node);

            root.Add(box);
        }

        void BuildPrimitiveBody(VisualElement box, ShaperPrimitiveDef p)
        {
            // Only the selected primitive's own dials are shown — the others do not apply, and showing every
            // shape's dials at once would bury the four that matter. Ranges are in canvas pixels, so they are
            // sized against the canvas rather than a fixed guess.
            float ext = Mathf.Max(document.canvasWidth, document.canvasHeight);

            p.EnsureDials();

            switch (p.kind)
            {
                case ShaperPrimitiveKind.Rect:
                    box.Add(Z.HGroup(
                        Val("Half width", "Half the rectangle's width, canvas pixels.", p.rectHalfWDial, 1f, ext),
                        Val("Half height", "Half the rectangle's height, canvas pixels.", p.rectHalfHDial, 1f, ext),
                        Val("Corner", "Corner radius, canvas pixels. 0 is a sharp corner.", p.rectCornerRadiusDial, 0f, ext * 0.5f)));
                    break;

                case ShaperPrimitiveKind.Ellipse:
                    box.Add(Z.HGroup(
                        Val("Radius X", "Horizontal radius, canvas pixels.", p.ellipseRxDial, 1f, ext),
                        Val("Radius Y", "Vertical radius, canvas pixels.", p.ellipseRyDial, 1f, ext)));
                    break;

                case ShaperPrimitiveKind.Diamond:
                    box.Add(Z.HGroup(
                        Val("Radius X", "Horizontal vertex distance from centre, canvas pixels.", p.diamondRxDial, 1f, ext),
                        Val("Radius Y", "Vertical vertex distance from centre, canvas pixels.", p.diamondRyDial, 1f, ext)));
                    break;

                case ShaperPrimitiveKind.Triangle:
                    box.Add(Z.HGroup(
                        Val("Base", "Base width, canvas pixels.", p.triangleBaseDial, 1f, ext * 2f),
                        Val("Height", "Height from base to apex, canvas pixels.", p.triangleHeightDial, 1f, ext * 2f)));
                    break;

                case ShaperPrimitiveKind.Capsule:
                    box.Add(Z.HGroup(
                        Val("Half length", "Half the length of the capsule's centre segment, canvas pixels.", p.capsuleHalfLengthDial, 0f, ext),
                        Val("Radius", "Cap radius, canvas pixels.", p.capsuleRadiusDial, 1f, ext * 0.5f)));
                    break;

                case ShaperPrimitiveKind.NGon:
                    // Side count is the one plain dial here: it picks one of a discrete family of polygons
                    // rather than measuring one, so a curve through it would pop rather than animate.
                    box.Add(Z.HGroup(
                        Dial("Sides", "How many sides the polygon has.", p.ngonSides, 3f, 64f, v => p.ngonSides = Mathf.RoundToInt(v), decimals: 0),
                        Val("Radius", "Circumradius, canvas pixels.", p.ngonRadiusDial, 1f, ext),
                        Val("Rotation", "Rotation of the polygon, degrees.", p.ngonRotationDial, -360f, 360f, cyclic: true, decimals: 0),
                        Val("Corner", "Corner radius, canvas pixels.", p.ngonCornerRadiusDial, 0f, ext * 0.5f)));
                    break;

                case ShaperPrimitiveKind.Star:
                    box.Add(Z.HGroup(
                        Dial("Arms", "How many points the star has.", p.starArms, 2f, 20f, v => p.starArms = Mathf.RoundToInt(v), decimals: 0),
                        Val("Radius", "Outer radius, canvas pixels.", p.starRadiusDial, 1f, ext),
                        Val("Length", "How far the arms reach relative to the outer radius.", p.starLength, 0f, 1f),
                        Val("Base width", "How wide each arm is at its base.", p.starBaseWidth, 0f, 2f),
                        Val("Skew", "Twists the arms.", p.starSkew, -1f, 1f)));
                    break;

                case ShaperPrimitiveKind.Sprite:
                    // T-0175 — a Sprite's alpha becomes the shape. The asset itself is a picker (never a typed
                    // reference), the box it maps onto is two dials like every other primitive's half-extents,
                    // and threshold/softness are the two dials that shape the mask the distance transform reads.
                    box.Add(Z.Field("Sprite", "The sprite whose alpha becomes this shape's coverage and edge.",
                        Z.Object<Sprite>(p.spriteAsset, "The sprite whose alpha becomes this shape's coverage and edge.",
                            s => { Change(() => p.spriteAsset = s); Rebuild(); }, 200f)));
                    box.Add(Z.Field("Fit", "How the sprite's own pixel aspect maps onto the box below. Uniform "
                        + "keeps its proportions; Stretch fills the box exactly.",
                        Z.MiniRadio((int)p.spriteFitMode, Enum.GetNames(typeof(ShaperSpriteFitMode)),
                            "How the sprite's own pixel aspect maps onto the box below. Uniform keeps its "
                            + "proportions (letterboxed); Stretch fills the box exactly.",
                            v => Change(() => p.spriteFitMode = (ShaperSpriteFitMode)v))));
                    box.Add(Z.HGroup(
                        Val("Half width", "Half the width of the box the sprite maps onto, canvas pixels.", p.spriteHalfWDial, 1f, ext),
                        Val("Half height", "Half the height of the box the sprite maps onto, canvas pixels.", p.spriteHalfHDial, 1f, ext),
                        Val("Threshold", "Alpha cutoff, below which a pixel counts as outside the shape.", p.spriteThresholdDial, 0f, 1f),
                        Val("Softness", "Blurs the sprite's alpha before thresholding, softening the shape's own edge.", p.spriteSoftnessDial, 0f, ext * 0.25f)));
                    break;

                case ShaperPrimitiveKind.Text:
                    // T-0174 — glyphs are the shape, so everything downstream (fill, border, height, lights)
                    // needs no text-specific dial of its own. Two raw islands here, both sanctioned: the font is
                    // an object picker (never a typed asset name), and the string is a text field because it
                    // DECLARES content rather than referencing something by name.
                    box.Add(Z.Field("Font", "The SDF font whose glyphs become this shape. Empty uses the "
                        + "project's default TextMeshPro font.",
                        Z.Object<TMPro.TMP_FontAsset>(p.textFont, "The SDF font whose glyphs become this shape. "
                            + "Empty uses the project's default TextMeshPro font.",
                            f => { Change(() => p.textFont = f); Rebuild(); }, 200f)));
                    box.Add(Z.Field("Text", "The characters this shape draws. A new line starts another line of text.",
                        Z.TextInput(p.textString, "The characters this shape draws. A new line starts another "
                            + "line of text.", s => Change(() => p.textString = s), 200f)));
                    box.Add(Z.Field("Align", "How the lines line up with each other when the text runs to more "
                        + "than one line.",
                        Z.MiniRadio((int)p.textAlign, Enum.GetNames(typeof(ShaperTextAlign)),
                            "How the lines line up with each other when the text runs to more than one line.",
                            v => Change(() => p.textAlign = (ShaperTextAlign)v))));
                    box.Add(Z.HGroup(
                        Val("Size", "Character height, canvas pixels. The shape sizes itself from the font's own "
                            + "metrics, so there is no box to set.", p.textSizeDial, 4f, ext),
                        Val("Letter spacing", "Adds space after every character. Negative tightens the word up.",
                            p.textLetterSpacingDial, -ext * 0.1f, ext * 0.25f),
                        Val("Line spacing", "Adds space between lines, on top of the font's own line height.",
                            p.textLineSpacingDial, -ext * 0.1f, ext * 0.5f),
                        Val("Weight", "Where the letter's edge is cut. Below 0.5 fattens the letters, above 0.5 "
                            + "thins them.", p.textWeightDial, 0.05f, 0.95f)));
                    break;
            }
        }

        void BuildTransformSection(VisualElement root, ShaperNode node)
        {
            // Closes the inventory's worst gap: ShaperTransformBlock (ShaperMatrix.cs) is on EVERY node and had
            // no UI anywhere, which meant a shape could not be positioned at all.
            var t = node.transform;
            var box = transformSection = Z.Section("Transform",
                "Where this node's content sits, and how it is oriented. Applied to the node's whole content — "
                + "for a bag, to the whole assembly before its members.",
                "shaper.window.transform", icon: "move");

            float ext = Mathf.Max(document.canvasWidth, document.canvasHeight);
            t.EnsureDials();

            // Spatial X/Y pairs are one 2D control, never two packed float fields — dragging two 1D fields to
            // aim one 2D value is the ergonomics problem, and packing them into a row fixes only the width.
            // Value2D rather than Pad, because each axis is its own animatable dial: right-click either to
            // author a Curve and the node travels, grows or leans over the document's frames.
            box.Add(Z.HGroup(
                Val2D("Translate", "Move this node's content, in canvas pixels. Animate it to make the node "
                    + "travel across the canvas over the document's frames.",
                    t.translateX, t.translateY,
                    // T-0186 — no plot-size override: Pyre's own Val2D (PyreWindow.cs:2607) never overrides it
                    // either, so this now matches Pyre's default 140px plot (ZuiValue2DControl.Options.plotSize)
                    // instead of the squished 110px this row used to pass. ZuiHGroup wraps, so a 360px column
                    // still shows 2 envelopes per line rather than overflowing at the wider size.
                    new ZuiValue2DControl.Options().WithRange(-ext, ext, -ext, ext)
                        .WithPrefKey("shaper.transform.translate")),
                Val2D("Origin", "The point this node rotates and scales around, in canvas pixels.",
                    t.originX, t.originY,
                    new ZuiValue2DControl.Options().WithRange(-ext, ext, -ext, ext)
                        .WithPrefKey("shaper.transform.origin")),
                Val2D("Scale", "Scale this node's content on each axis. 1 is unscaled. Animate it to make the "
                    + "node grow or shrink over the document's frames.",
                    t.scaleX, t.scaleY,
                    new ZuiValue2DControl.Options().WithRange(0.05f, 4f, 0.05f, 4f)
                        .WithDefault(Vector2.one).WithPrefKey("shaper.transform.scale")),
                Val2D("Skew", "Slant this node's content on each axis, in degrees.",
                    t.skewX, t.skewY,
                    new ZuiValue2DControl.Options().WithRange(-80f, 80f, -80f, 80f)
                        .WithPrefKey("shaper.transform.skew"))));

            box.Add(Val("Rotation", "Rotate this node's content around its origin, in degrees. Animate it to "
                + "make the node spin over the document's frames.",
                t.rotationDegrees, -720f, 720f, cyclic: true, decimals: 0));

            root.Add(box);
        }

        // ── right pane: preview, transport, bake ─────────────────────────────────────────────────────────

        void BuildRight(VisualElement root)
        {
            var previewSection = Z.Section("Preview",
                "The whole document rendered at the current frame, through the same renderer the bake uses.",
                "shaper.window.preview", icon: "eye");
            previewSection.style.flexGrow = 1f;
            previewSection.style.minHeight = 0f;
            previewSection.contentContainer.style.flexGrow = 1f;
            previewSection.contentContainer.style.minHeight = 0f;

            // `previewFrame` rather than `currentFrame`: under cherry framing the frame on screen is the
            // one the beat sequencer resolved, which may be ShaperCherry.BlankFrame (a deliberate gap).
            stage = new ShaperPreviewStage(() => document, () => previewFrame);
            stage.style.flexGrow = 1f;
            // T-0165 — repaint the cache tick strip / "N/M cached" readout whenever the background pre-baker
            // makes progress, without touching the (expensive) preview image itself.
            stage.CacheProgressed += RefreshCacheReadout;

            // T-0168 — the on-canvas position handle. The stage draws and drags it; which node it belongs to,
            // and what an edit costs in Undo, stay the window's business.
            stage.SelectedNode = () => CurrentNode;
            stage.SelectedLayerRoot = () => CurrentLayer?.root;
            stage.RecordUndo = () => { if (document != null) Undo.RecordObject(document, "Move Shaper Node"); };
            stage.Changed = () =>
            {
                if (document != null) EditorUtility.SetDirty(document);
                stage.InvalidateFrameCache();
                RefreshPreview();
            };
            // Only once the drag has settled: the Transform card's own numeric readout has to catch up with
            // where the handle was dropped, and rebuilding the panel mid-gesture would pull the control out
            // from under the pointer.
            stage.DragCommitted = Rebuild;

            // T-0190 — preview overlays. The stage owes them a buffer and nothing else; which features have
            // marks to draw, and which are switched on, is decided here (ShaperPreviewOverlays).
            stage.WantsOverlays = AnyOverlayOn;
            stage.DrawOverlays = (px, w, h, frame) =>
                ShaperPreviewOverlays.Draw(overlayEntries, px, w, h, document,
                    document.PhaseOfFrame(Mathf.Max(0, frame)));

            previewSection.Add(stage);
            ApplyPreviewChromeToStage();

            previewSection.Add(BuildPreviewChrome());

            // T-0190 — the overlay toggle strip: a PERMANENTLY reserved row, built once and only refilled
            // (authoring.md §15). It sits LAST above the transport so the one direction it can grow has the
            // transport, not the picture, after it.
            overlayStripHost = new VisualElement();
            overlayStripHost.style.minHeight = 18f;
            previewSection.Add(overlayStripHost);
            RefreshOverlayStrip();

            // T-0190 — the transport is ALWAYS built, never gated on frameCount. Gating it was the single
            // biggest reason a new document looked like a tool that did not work: a fresh document had one
            // frame, so there was no Play button, no scrubber, no filmstrip and no cherry panel until the user
            // found a dial they had no reason to look for. A still document now shows the same transport with
            // Play disabled and a tooltip that says why — an explained dead control beats an absent one.
            transportHost = new VisualElement();
            previewSection.Add(transportHost);
            FillTransport();

            // T-0188/T-0190 — the status line gets its OWN permanently reserved row UNDER the transport. It
            // used to share the Frame-border/Zoom row, where a long cherry message ran over the Zoom slider.
            previewSection.Add(BuildTransportStatus());

            root.Add(previewSection);

            // Backdrop and cherry framing sit below the preview, in Pyre's own order (backdrop chrome, then
            // the cherry panel), so the things that change what the preview LOOKS like are grouped together.
            // The cherry panel is ungated for the same reason the transport is: it is how a user discovers
            // that cherry framing exists at all.
            BuildBackdropPanel(root);
            root.Add(BuildCherryPanel());

            // T-0177 — destination + PPU + per-output toggles (Sprite sheet PNG / AnimationClip / ShaperClip /
            // GIF), each honest about whether it preserves cherry framing. Built in ShaperWindow.Bake.cs.
            root.Add(BuildBakeBox());
        }

        /// What the preview should actually show. Under cherry framing that is the beat sequencer's resolved
        /// frame (possibly a blank); during the PLAIN loop's own gap it is the same blank sentinel; otherwise
        /// it is the transport's own frame.
        int previewFrame
        {
            get
            {
                if (document == null) return currentFrame;
                if (document.cherryEnabled && cherryRunning) return cherryState.frame;
                // The plain loop's blank gap (T-0190). ShaperCherry.BlankFrame is reused rather than a second
                // sentinel invented: the stage already renders it as nothing, and a gap is a gap whichever
                // loop produced it.
                if (plainLoopBlankUntil > 0.0 && EditorApplication.timeSinceStartup < plainLoopBlankUntil)
                    return ShaperCherry.BlankFrame;
                return currentFrame;
            }
        }

        /// Whether ANY collected overlay is switched on — the stage asks before paying for the buffer copy.
        bool AnyOverlayOn()
        {
            for (int i = 0; i < overlayEntries.Count; i++)
                if (ShaperPreviewOverlays.IsOn(overlayEntries[i].Overlay)) return true;
            return false;
        }

        /// Re-collect the document's overlays and refill their reserved strip. Cheap enough to call on every
        /// authored edit: it walks the node tree, which is the same walk a single frame's compile already does
        /// many times over.
        internal void RefreshOverlayStrip()
        {
            if (overlayStripHost == null) return;
            overlayEntries = ShaperPreviewOverlays.Collect(document,
                document != null ? document.PhaseOfFrame(Mathf.Max(0, currentFrame)) : 0f);
            ShaperPreviewOverlays.FillStrip(overlayStripHost, overlayEntries, RefreshPreview);
        }

        /// Rebuild the transport's controls in place. Called on build and whenever the FRAME AXIS changes —
        /// a ZuiMicroSlider's range is fixed at construction, so raising Frames has to make a new scrubber,
        /// and the tick strip and filmstrip have to re-shape with it (checklist 2.2). Refilling this host
        /// rather than rebuilding the window is what keeps the Frames dial under the user's pointer.
        void FillTransport()
        {
            if (transportHost == null || document == null) return;
            transportHost.Clear();
            filmstrip?.Dispose();
            filmstrip = null;

            bool animated = document.frameCount > 1;
            int max = Mathf.Max(0, document.frameCount - 1);
            currentFrame = Mathf.Clamp(currentFrame, 0, max);

            playButton = Z.Button(playing ? "❚❚ Pause" : "▶ Play",
                animated
                    ? "Play or pause the looping preview."
                    : "This document is one frame long, so there is nothing to play. Raise Frames in the "
                      + "Canvas card and this starts the loop.",
                () =>
                {
                    playing = !playing;
                    playButton.text = playing ? "❚❚ Pause" : "▶ Play";
                    if (playing)
                    {
                        lastPlayTick = EditorApplication.timeSinceStartup;
                        playAcc = 0f;
                        holdingSince = -1.0;
                        plainLoopBlankUntil = -1.0;
                        // Start the cherry sequence from its first slot on every press, so Play always means
                        // the same thing rather than resuming a half-finished loop from whenever you paused.
                        ResetCherryPlayback();
                        EditorApplication.update -= PlaybackTick;
                        EditorApplication.update += PlaybackTick;
                    }
                });
            // Disabled rather than absent: the control stays where the user learned it is, and its tooltip
            // says the one thing they need to change. A missing button teaches nothing.
            playButton.SetEnabled(animated);

            transportHost.Add(Z.HGroup(
                playButton,
                Dial("Rate", "Playback rate in frames per second.", document.frameRate,
                    ShaperClock.MinFrameRate, ShaperClock.MaxFrameRate, v => document.frameRate = v, decimals: 0),
                Dial("Loop gap", "Seconds of blank between one pass through the frames and the next. 0 loops "
                    + "with no gap. The gap plays as nothing on screen, not as a held frame, and is preview "
                    + "playback only — a baked sheet is frames and has nowhere to put a pause.",
                    document.loopDelaySeconds, 0f, 4f,
                    v => document.loopDelaySeconds = Mathf.Max(0f, v), decimals: 2),
                Z.Toggle("Strip",
                    previewStrip
                        ? "Showing the whole animation as a contact sheet of every frame under the transport. "
                          + "Click to reclaim that height."
                        : "Show the whole animation as a contact sheet of every frame under the transport; "
                          + "click a tile to jump the transport there.",
                    previewStrip, v => { previewStrip = v; FillTransport(); })));

            // GIF export moved into the Bake box (T-0177 PM vet — one home for the feature instead of two):
            // the toggle, scale and dither controls now live beside the other output toggles in
            // ShaperWindow.Bake.cs, and the Bake button writes the GIF when that toggle is on.

            // A MicroSlider carries its own caption inside the track (matching Dial() above), so it is added
            // directly rather than wrapped in Z.Field — a Field label would just duplicate "Frame". The
            // "frame N/M" readout goes LAST in the row (variable-width content last), and is the one place
            // that answers "where is playback" without the reader decoding a slider's fill.
            scrubber = Z.MicroSlider("Frame", currentFrame, 0, max,
                animated
                    ? "Scrub to an exact frame. Dragging pauses playback and holds that frame."
                    : "This document is one frame long, so there is nothing to scrub through. Raise Frames in "
                      + "the Canvas card to give it a timeline.",
                v =>
                {
                    currentFrame = Mathf.RoundToInt(v);
                    playing = false;
                    // Hand control back to the scrubber. Without this the cherry sequencer would still be
                    // "running", so previewFrame would keep returning its last resolved beat and the scrub
                    // would appear to do nothing — the drag would move the handle and not the picture.
                    cherryRunning = false;
                    plainLoopBlankUntil = -1.0;
                    if (playButton != null) playButton.text = "▶ Play";
                    RefreshFrameReadout();
                    RefreshPreview();
                }, decimals: 0);
            scrubber.style.flexGrow = 1f;
            // Under cherry framing the SOURCE frame is the sequence's to choose, not the user's: dragging this
            // would fight the sequencer for the same value and lose on the next beat. It stays visible (it is
            // still the honest readout of which source frame is on screen) and stops accepting a drag, with
            // the cherry playhead below taking over as the control.
            scrubber.SetEnabled(animated && !document.cherryEnabled);

            frameReadout = Z.Text("", ZuiText.Body,
                "Which frame is on screen, out of how many this document has. Counts from 1, like the "
                + "filmstrip's own tiles.");
            frameReadout.style.width = 74f;
            frameReadout.style.unityTextAlign = TextAnchor.MiddleRight;
            transportHost.Add(Z.HGroup(scrubber, frameReadout));
            RefreshFrameReadout();

            // T-0190 — the cherry playhead. Only when there is a sequence to follow (the absence rule): with
            // cherry off this control would report a position in a sequence nobody authored.
            if (animated && document.cherryEnabled)
            {
                int slots = Mathf.Max(1, document.cherryFrames?.Count ?? 0);
                cherryScrubber = Z.MicroSlider("Cherry slot", Mathf.Clamp(cherryState.slot, 0, slots - 1),
                    0, slots - 1,
                    "Where playback is in the CHERRY sequence — which slot, not which source frame. Drag it to "
                    + "park playback on a slot and see the frame that slot names.",
                    v =>
                    {
                        int slot = Mathf.RoundToInt(v);
                        playing = false;
                        if (playButton != null) playButton.text = "▶ Play";
                        JumpToCherrySlot(slot);
                    }, decimals: 0);
                cherryScrubber.style.flexGrow = 1f;
                transportHost.Add(cherryScrubber);
            }
            else cherryScrubber = null;

            // T-0165 — cached-frame ticks + "N/M cached" readout, in a PERMANENTLY reserved row (stable-
            // workspace rule): this row is built once here and only ever has its paint/text updated
            // (ShaperCacheTickStrip.Refresh / RefreshCacheReadout), never added or removed, so it cannot
            // reflow the scrubber above it. The tick strip takes the growable space; the "N/M cached" count
            // goes last, matching the "variable-width content goes LAST in its row" rule.
            cacheTickStrip = new ShaperCacheTickStrip(
                () => document != null ? document.frameCount : 1,
                i => stage != null && stage.IsFrameCached(i),
                () => currentFrame);
            cacheReadoutLabel = Z.Text("", ZuiText.Body,
                "How many of this document's frames already have their picture cached.");
            cacheReadoutLabel.style.width = 90f;
            transportHost.Add(Z.HGroup(cacheTickStrip, cacheReadoutLabel));
            RefreshCacheReadout();

            // T-0176 — filmstrip contact sheet: click a tile to jump the transport there (Pyre parity).
            // T-0190 — now a MODE, with Pyre's own Tile-px slider beside its toggle above.
            if (!previewStrip) return;

            transportHost.Add(Z.MicroSlider("Tile px", previewStripTile, 24f, 128f,
                "How big each frame tile in the contact sheet is. Only changes how the strip is drawn — no "
                + "frame is re-rendered and no bake is affected.",
                v => { previewStripTile = Mathf.Clamp(v, 24f, 128f); FillTransport(); }, 150f, decimals: 0));

            filmstrip = new ShaperFilmstripElement(() => document,
                () => currentFrame,
                jumpTo =>
                {
                    currentFrame = Mathf.Clamp(jumpTo, 0, Mathf.Max(0, document.frameCount - 1));
                    playing = false;
                    cherryRunning = false;
                    plainLoopBlankUntil = -1.0;
                    if (playButton != null) playButton.text = "▶ Play";
                    if (scrubber != null) scrubber.value = currentFrame;
                    RefreshFrameReadout();
                    RefreshPreview();
                },
                previewStripTile);
            transportHost.Add(filmstrip);
            filmstrip.Rebuild();
        }

        /// The "frame N/M" readout (checklist 8.9). 1-based, matching the filmstrip's own tile tooltips, and
        /// updated from every path that moves the playhead — playback, scrub, filmstrip click — so the three
        /// can never disagree about where the transport is.
        void RefreshFrameReadout()
        {
            if (frameReadout == null || document == null) return;
            int shown = previewFrame;
            frameReadout.text = shown < 0
                ? "gap"
                : $"frame {Mathf.Clamp(shown, 0, Mathf.Max(0, document.frameCount - 1)) + 1}/{Mathf.Max(1, document.frameCount)}";
        }

        /// Park cherry playback on one slot and show the frame it names — what dragging the cherry playhead
        /// does. Begins a fresh sequence state and walks it forward, rather than writing a slot index straight
        /// in, so the beat counter and the loop-gap state stay consistent with the engine's own rule.
        void JumpToCherrySlot(int slot)
        {
            if (document == null || document.cherryFrames == null || document.cherryFrames.Count == 0) return;
            slot = Mathf.Clamp(slot, 0, document.cherryFrames.Count - 1);
            cherryState = ShaperCherry.Begin(document);
            cherryRunning = true;
            // Guarded: a slot with a length multiplier takes several beats to leave, so stepping "one slot"
            // is stepping until the slot index changes — with a bound, because a degenerate sequence must not
            // spin the editor.
            for (int guard = 0; guard < 4096 && cherryState.slot != slot; guard++)
                cherryState = ShaperCherry.AdvanceOneBeat(cherryState, document);
            if (cherryState.frame >= 0)
            {
                currentFrame = Mathf.Clamp(cherryState.frame, 0, Mathf.Max(0, document.frameCount - 1));
                if (scrubber != null) scrubber.value = currentFrame;
            }
            RefreshFrameReadout();
            RefreshPreview();
        }

        /// Repaint the tick strip and update the "N/M cached" text. Cheap — no layout pass, just a
        /// MarkDirtyRepaint and a label text write — so it is safe to call on every pre-baker tick.
        void RefreshCacheReadout()
        {
            cacheTickStrip?.Refresh();
            if (cacheReadoutLabel == null || stage == null) return;
            cacheReadoutLabel.text = $"{stage.CountCachedFrames()}/{stage.CachedFrameCount} cached";
        }

        void PlaybackTick()
        {
            // `this == null`: EditorApplication.update iterates a snapshot of its invocation list, so this can
            // still fire once on a destroyed window after OnDisable unsubscribed it.
            if (this == null || document == null || !playing)
            {
                EditorApplication.update -= PlaybackTick;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastPlayTick);
            lastPlayTick = now;

            // T-0190 — the plain loop's blank gap. Held on the wall clock rather than on beats, because it is
            // authored in seconds; the accumulator is reset on the way out so the first frame after the gap
            // gets a whole frame's worth of time rather than the leftovers of the gap.
            if (plainLoopBlankUntil > 0.0)
            {
                if (now < plainLoopBlankUntil) { RefreshPreview(); return; }
                plainLoopBlankUntil = -1.0;
                playAcc = 0f;
                RefreshFrameReadout();
                RefreshPreview();
                return;
            }

            // The engine owns the stepping rule; this window does not keep a second accumulator.
            int steps = ShaperClock.AdvanceFrames(ref playAcc, dt, document.frameRate);
            if (steps <= 0) return;

            if (document.cherryEnabled)
            {
                // Cherry framing replaces the frame ORDER, not the clock: the same beats elapse, but which
                // frame each beat shows comes from the authored sequence. AdvanceCherry may return
                // ShaperCherry.BlankFrame, which the preview stage renders as nothing — the loop gap and an
                // empty sequence are both real authored states, not error cases to substitute frame 0 for.
                int shown = AdvanceCherry(steps);

                // Keep the scrubber meaningful: it tracks the SOURCE frame being shown, and simply holds
                // its last position through a blank beat rather than jumping to 0 and back.
                if (shown >= 0)
                {
                    currentFrame = Mathf.Clamp(shown, 0, Mathf.Max(0, document.frameCount - 1));
                    if (scrubber != null) scrubber.value = currentFrame;
                }
                if (cherryScrubber != null) cherryScrubber.value = cherryState.slot;
                RefreshFrameReadout();
                RefreshPreview();
                return;
            }

            int frames = Mathf.Max(1, document.frameCount);
            int nextFrame = ShaperClock.WrapFrame(currentFrame + steps, frames);

            // T-0165 — playback prefers to step onto CACHED frames (Pyre's own behaviour): advancing onto an
            // uncached one means a synchronous ~30ms render mid-tick, exactly the stutter a background
            // pre-baker exists to avoid. Holding lets playback catch up once the pre-baker fills the frame in.
            //
            // T-0190 — but the hold is now BOUNDED, and that is the whole of checklist row 4.3. Change()
            // invalidates the entire frame cache on every authored edit, so a sustained dial-drag re-empties
            // the cache faster than the pre-baker can refill it and the unbounded hold parked the transport
            // for as long as the drag lasted — the owner's "I pressed play and the transport did not move".
            // After MaxCacheHoldSeconds playback steps on and pays for the render, which stutters; a stutter
            // is a tool working hard, a frozen playhead is a tool that looks broken. The picture never blanks
            // either way: ShaperPreviewStage holds the last good image when a frame is not ready.
            if (stage != null && !stage.IsFrameCached(nextFrame))
            {
                if (holdingSince < 0.0) holdingSince = now;
                if (now - holdingSince < MaxCacheHoldSeconds) return;
            }
            holdingSince = -1.0;

            // A wrap is what a loop gap sits in: the sequence has just finished a pass, so the gap goes here,
            // before the first frame of the next one is shown.
            bool wrapped = nextFrame <= currentFrame && steps > 0 && frames > 1;
            if (wrapped && document.loopDelaySeconds > 0f)
                plainLoopBlankUntil = now + document.loopDelaySeconds;

            currentFrame = nextFrame;
            if (scrubber != null) scrubber.value = currentFrame;
            RefreshFrameReadout();
            RefreshPreview();
        }

        // ExportGif and DoBake moved to ShaperWindow.Bake.cs (T-0177 PM vet) — GIF export now lives in the
        // Bake box only (one home for the feature), not also as a separate transport quick-export.

        // ── SHARED HELPERS — the contract for every ShaperWindow.*.cs file ───────────────────────────────

        /// Wrap EVERY authored mutation. Records Undo on the document BEFORE the mutation (RecordObject
        /// snapshots the pre-edit state, so recording afterwards would store the value just written and make
        /// Ctrl+Z appear to do nothing), then dirties the asset and refreshes the preview.
        internal void Change(Action apply)
        {
            if (document != null) Undo.RecordObject(document, "Edit Shaper Document");
            apply();
            if (document != null) EditorUtility.SetDirty(document);
            // T-0165 — every authored edit invalidates the preview frame cache before refreshing, so the
            // scrubber never shows a frame cached from before the edit. Pure VIEW changes (scrub, zoom, frame
            // border, backdrop) call RefreshPreview() directly instead and deliberately do NOT invalidate —
            // that is the whole point of the cache existing.
            stage?.InvalidateFrameCache();
            filmstrip?.Invalidate();
            InvalidateCherryThumbs();
            // T-0190 — an edit can change WHICH features have marks to show (enabling a swarm, giving it a
            // shape), so the reserved overlay strip is re-collected here rather than only on a full rebuild:
            // a toggle that appears one window-rebuild after the feature it belongs to reads as not existing.
            RefreshOverlayStrip();
            RefreshFrameReadout();
            RefreshPreview();
        }

        /// A plain-float dial. Use for a field the engine declares as `float`/`int`.
        internal VisualElement Dial(string label, string tooltip, float value, float min, float max,
            Action<float> set, int decimals = -1)
            => Z.MicroSlider(label, value, min, max, tooltip, v => Change(() => set(v)), 140f, decimals: decimals);

        /// The ZUIValue analog of Dial — use for a field the engine declares as `ZUIValue`, which is what makes
        /// it animatable over the document's phase (right-click opens Static / Min-Max / Curve). Curve timing,
        /// range and live readout are hidden so a packed row stays compact, matching Pyre's own Val().
        internal VisualElement Val(string label, string tooltip, ZUIValue v, float lo, float hi,
            bool cyclic = false, int decimals = -1)
        {
            var o = new ZuiValueControl.Options
            {
                absMin = lo, absMax = hi,
                hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                // T-0186 — controlWidth 170f / grow true, matching Pyre's own Val() (PyreWindow.cs:2589-2593,
                // pyre_1col.png: a solo envelope (Alpha, Radius) fills the row it has to itself, exactly like
                // grow is meant to). T-0192 tried turning `grow` off here and confirmed by eye against Pyre's
                // own reference captures that it undershoots Pyre's parity size — reverted; `grow` stays ON,
                // matching Pyre. The real T-0192 overflow (a card's Z dial fighting Dup/× for space, PM
                // by-eye workspace/T-0180/walk2-03-strip-off.png) was the layer row's own width budget, fixed
                // by moving Z off every row (BuildLayerRow's comment), not by fighting Val's own sizing.
                controlWidth = 170f, grow = true, cyclic = cyclic, decimals = decimals,
                frameCount = document != null ? document.frameCount : 0,
            };
            return Z.Value(label, v, o, tooltip,
                () =>
                {
                    if (document != null) EditorUtility.SetDirty(document);
                    // T-0165 — same reasoning as Change(): a ZUIValue edit is a data edit, so it invalidates
                    // the frame cache too. Val() never routes through Change() itself (Z.Value manages its own
                    // Undo timing via the second callback below), so this is hooked here instead.
                    stage?.InvalidateFrameCache();
                    filmstrip?.Invalidate();
                    InvalidateCherryThumbs();
                    RefreshPreview();
                },
                () => { if (document != null) Undo.RecordObject(document, "Edit Shaper Document"); });
        }

        /// The 2D analog of Val — one control over an X/Y PAIR of animatable dials, so a spatial value is aimed
        /// by dragging a point rather than by nudging two sliders. Same Undo/dirty/cache contract as Val.
        internal VisualElement Val2D(string label, string tooltip, ZUIValue x, ZUIValue y,
                                     ZuiValue2DControl.Options o)
            => Z.Value2D(label, x, y, o, tooltip,
                () =>
                {
                    if (document != null) EditorUtility.SetDirty(document);
                    stage?.InvalidateFrameCache();
                    RefreshPreview();
                },
                () => { if (document != null) Undo.RecordObject(document, "Edit Shaper Document"); });

        /// Re-render the preview. T-0165 — this now reads through ShaperPreviewStage's own frame cache
        /// (ShaperPreviewFrameCache), so a frame already visited this session is a dictionary hit rather than
        /// a ~30ms recompute. Deliberately does NOT invalidate anything itself — Change()/Val() invalidate at
        /// the point of an actual data edit, and every other caller of this method (scrub, zoom, frame
        /// border, backdrop, playback) is a pure view change that must NOT throw the cache away.
        internal void RefreshPreview()
        {
            stage?.Refresh();
            // Repaint (never invalidate) the filmstrip's current-frame highlight on every view change — a
            // scrub or playback tick moves which tile is "current" without touching any tile's pixels.
            filmstrip?.RefreshTiles();
        }
    }
}
