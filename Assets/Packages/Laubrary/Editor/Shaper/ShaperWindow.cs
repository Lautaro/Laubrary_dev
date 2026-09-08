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
        ///
        /// T-0257 — and with ONE ENABLED KEY LIGHT. `ShaperLightRig.lights` defaults to an empty list, and an
        /// empty rig is not "dim", it is a hard gate: `ShaperLightCompiler.CompileResponse` forces every
        /// Silhouette layer's `receive` to 0 (ShaperLightCompiler.cs:399), so intensity, rim, specular, spec
        /// power and spec tint — the whole Lighting card — do nothing at all until somebody adds a light. A
        /// first-run author has no way to know that, and the six dials are right there.
        ///
        /// SEEDED HERE AND NOT ON THE RIG'S OWN FIELD, deliberately: a field initializer runs for every
        /// ShaperLightRig anyone constructs, including the one an old asset's deserialization builds before
        /// its authored (possibly empty, possibly deliberate) list is read back over it. Seeding at asset
        /// CREATION cannot reach a document that already exists, which is the whole requirement.
        protected override void InitializeNewAsset(ShaperDocument item)
        {
            if (item == null) return;
            item.layers.Add(NewLayer("Layer 1", item));

            // The rig's own defaults are Pyre's relief-light numbers (ShaperLightRig.cs:121-133 and
            // ShaperLight's yaw/pitch/intensity), so an unmodified ShaperLight IS the key light this wants —
            // up, left and toward the viewer. Named rather than left as "Light" so the Lights list reads as
            // something authored rather than something that appeared.
            item.lightRig ??= new ShaperLightRig();
            item.lightRig.lights ??= new List<ShaperLight>();
            if (item.lightRig.lights.Count == 0)
                item.lightRig.lights.Add(new ShaperLight { name = "Key", enabled = true });
        }

        /// <summary>
        /// T-0257 — a height stage that can be SEEN the moment it is switched on.
        ///
        /// <c>ShaperHeightDef</c>'s own defaults are depth 0 and profile Flat, and depth is the body every
        /// other dial on the card is multiplied by (<c>ShaperHeightOp.body</c>, ShaperHeight.cs:145), so
        /// ticking Height used to add a stage that changed nothing and six dials that could not change
        /// anything either. Both halves are seeded here, at the one place this window creates a stage, rather
        /// than on the type's fields — the same reasoning as the key light above: a field default would also
        /// apply to every stage the engine or a deserializer constructs, a creation-time seed cannot reach an
        /// authored document.
        ///
        /// Dome rather than Flat because Flat's profile is <c>E ≡ 1</c> (ShaperHeight.cs:455-457) — a raised
        /// slab with a flat top, whose surface direction is the same everywhere, so it reads as a Z shift and
        /// not as relief. 8 canvas pixels is a visible rise at the 32–256 canvas sizes this tool authors.
        static ShaperHeightDef NewHeightStage() => new ShaperHeightDef
        {
            technique = ShaperExtrusionTechnique.Dome,
            depth = new ZUIValue(8f),
        };

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
        VisualElement layerListHost, toggleBarHost, selectedLayerCardsHost;
        ZuiSection canvasSection, layersSection, shapeSection;
        // T-0258 — the ex-Transform card, now the Shape card's "Position" box. Still held because the preview's
        // pivot cross is drawn only while it is open (see stage.ShowOrigin in BuildRight): a box the author
        // folded away has no business leaving a mark on the picture.
        ZuiBox positionBox;
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

            // T-0187 — Tags is PLACED below the toggle bar, in its own row, so folding it never moves the bar
            // above it (the owner: "the toggle bar jumps up and down if you toggle the tags"). The base builds
            // TagsSection but, because AutoInsertTagsSection is overridden false below, leaves placing it to
            // us instead of adding it above the toolbar/host split itself.
            //
            // T-0204 — placement and bar membership are separate questions. Tags stays physically pinned here
            // (still true above), but now ALSO joins RefreshToggleBar's roster, so the bar's own button can
            // show/hide it in bulk like every other section — "Tags and View sections cannot be toggled in the
            // taskbar. Why?" (owner). ZuiSectionToggleBar toggles a section's VISIBILITY wherever it already
            // sits in the tree; it never moves it, so Tags folding via the bar still can't reposition the bar.
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

            // T-0195 — a ScrollView, not a plain VisualElement: once the preview island has a user-draggable
            // height (BuildPreviewResizeBar, ShaperWindow.Preview.cs) it can be sized taller than the window,
            // and the transport + Bake box that follow it must stay reachable rather than being clipped off
            // the bottom. Scrolling is the escape hatch; nothing below is skipped when it fires.
            var right = new ScrollView(ScrollViewMode.Vertical);
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
        // use" (owner). Height and Mask no longer appear here — they moved into the Layers section as
        // per-layer cards/toggles (BuildLayersSection). Built as a label→section lookup rather than the old
        // fixed interleave, so the exact order below is the only thing that decides what the bar shows and in
        // what sequence, regardless of which file owns which section instance.
        //
        // T-0204 — Tags and Views REJOIN the roster: both used to be permanently-placed, un-toggleable rows,
        // and the owner asked why ("Tags and View sections cannot be toggled in the taskbar"). They are
        // ordinary ZuiSections like every other bar entry — Tags happens to also stay physically placed BELOW
        // the bar (BuildAsset) so folding it can never move the bar itself, which is a layout choice, not a
        // reason to keep it out of the bar's own show/hide roster.
        //
        // T-0258 — THIRTEEN ENTRIES DOWN TO EIGHT, which is Pyre's own count (Tags, Views, Canvas, Layers,
        // Global Mod, Shape, Swarm, Modifiers — Editor/Pyre/PyreWindow.cs:488-499). Five of the thirteen were
        // not sections at all, they were parts of another section wearing a bar segment, and each one has gone
        // back where it belongs rather than being deleted:
        //   • Transform  → the Shape card's "Position" box (a node's placement is part of what it draws)
        //   • Border     → the Fill card's "Edge" box (a border is a second fill, on the edge)
        //   • Lighting   → a folded box on the selected layer's row, beside Height and Mask (it is a LAYER
        //                  property, and those two already moved there in T-0187/T-0204)
        //   • Global SpriteFX → merged into SpriteFX; an entry's "Whole picture" toggle moves it between the
        //                  layer list and the document list, so one card holds both
        //   • Swarm      → stayed a section after all (owner, 2026-09-08): Pyre keeps it in its bar, and its
        //                  on/off checkbox belongs on a green section header, not a box title
        // A bar segment is the promise that a card is a place you go; a part of a card is not.
        static readonly string[] ToggleBarOrder =
        {
            "Views", "Canvas", "Layers", "Shape", "Fill", "Swarm", "SpriteFX", "Lights", "Tags",
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
                ["Views"] = viewsSection,
                ["Tags"] = TagsSection,   // base ZuiAssetWindow field — see BuildAsset's own placement comment
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
        ///
        /// W6.3: a border does not exist yet at this point (a fresh layer's root has none), so a border added
        /// LATER cannot be seeded here — see <see cref="SeededBorderFill"/> (ShaperWindow.Sections.cs), which
        /// re-seeds a matching fade onto a border's own veil at the moment its fill is created, so it moves
        /// with a fill this method already put a fade on.
        // internal (was private): W6.3's T0196_FillDefaultsProbe calls this directly so its fade-together
        // check exercises the SAME seeding code the window itself runs, rather than a second copy that could
        // silently drift from what actually ships. Widening private -> internal only, no behaviour change.
        internal static ShaperLayer NewLayer(string name, ShaperDocument doc)
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
        internal static ZUIValue SeededVeil()
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
                "The document's own resolution, sampling density, depth between layers, frame count and seed.",
                "shaper.window.canvas", icon: "frame-corners");

            // One continuous HGroup rather than several: an overflowing field then lands beside the NEXT
            // field instead of alone on a line of its own.
            box.Add(Z.HGroup(
                Dial("Width", "Canvas width in samples. B9's authorable range is 32–256.",
                    document.canvasWidth, 32f, 256f, v => document.canvasWidth = Mathf.RoundToInt(v), decimals: 0),
                Dial("Height", "Canvas height in samples.",
                    document.canvasHeight, 32f, 256f, v => document.canvasHeight = Mathf.RoundToInt(v), decimals: 0),
                // T-0257 — "Pixel size" read as "how big is a pixel"; it is how much canvas one sample covers.
                Dial("Canvas scale", "Canvas units per sample. 1 makes a \"canvas pixel\" in a dial equal one "
                    + "sample (LR-1.5).", document.pixelSize, 0.1f, 8f, v => document.pixelSize = v),
                // T-0257 — "Layer spacing" never said what it spaced them along; the per-layer Z dial in the
                // Layers card is the other end of this same axis. Given the extra width its own name needs,
                // since the group wraps rather than clipping.
                Dial("Depth between layers", "Canvas pixels between consecutive layers' base planes, and so "
                    + "how far apart in depth they sit: layers are composited by which surface is nearest, and "
                    + "a layer whose height rises more than this above the one below it breaks through it. 0 "
                    + "puts every base plane together, where list order decides.",
                    document.layerSpacing, 0f, 8f, v => document.layerSpacing = v, width: 190f)));

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
                // T-0257 — "Rate" was here AND in the transport, same label, same field, two cards. The
                // transport is the natural home (it is where playback lives), so this copy is gone.
                Z.Field("Document seed",
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
                        v => Change(() => document.seed = (uint)Mathf.Max(0, v)), 80f)),
                // T-0257 — Background moved up into this row. It used to share a row with a "PPU" dial that is
                // gone (the same field was ALSO in the Bake box, spelled out, whose own tooltip admitted the
                // duplication — "shown here too because it is a bake setting"); it is a bake setting, so Bake
                // is now its one home, and leaving Background alone on a row of its own would have spent a
                // whole line on one colour chip.
                Z.Field("Background",
                    "Composited UNDER every layer, so a bake, a GIF and a baked clip all carry it. Transparent "
                    + "by default. Distinct from the preview-only backdrop below — this one reaches the shipped "
                    + "picture, that one never does.",
                    Z.Color(document.background,
                        "Composited UNDER every layer, so a bake, a GIF and a baked clip all carry it. "
                        + "Transparent by default.",
                        v => Change(() => document.background = v)))));

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

            // T-0197 — the SELECTED layer's Z/Lifetime/Height/Mask cards live in their own host so picking a
            // different layer (BuildLayerRow's select button) can refresh just this pane via
            // RefreshSelectedLayerCards() instead of a full window Rebuild() — a window Rebuild() is what was
            // dropping ZuiSectionToggleBar's solo/quick-view state on every layer click before this task.
            selectedLayerCardsHost = new VisualElement();
            box.Add(selectedLayerCardsHost);
            RefreshSelectedLayerCards();

            root.Add(box);
        }

        /// Rebuilds only the SELECTED layer's Lifetime/Z row and its Height/Mask cards (the pane below the
        /// layer list), without touching the rest of the window — the layer-list row selection dot is
        /// refreshed separately via RebuildLayerList(). Call both together when selection changes; call this
        /// alone when only a card's own content needs to reflect a data edit that doesn't change WHICH layer
        /// is selected.
        ///
        /// T-0204 — copies Pyre's own Layers-section model (Editor/Pyre/PyreWindow.cs:779-926): ONE row for
        /// the selected layer, always visible and editable — Lifetime and Z stacked HORIZONTALLY ("Lifetime
        /// and Z should stack horizontally", owner) — with Height and Mask as TOGGLES on that same row rather
        /// than always-present cards ("Both mask and height create a whole section each without being used.
        /// Make them a toggle right after the layer input box; if enabled the sections can show", owner). The
        /// Height/Mask CARDS below the row exist only while their toggle is on — the same absence rule every
        /// other card in this window already follows.
        void RefreshSelectedLayerCards()
        {
            if (selectedLayerCardsHost == null) return;
            selectedLayerCardsHost.Clear();
            var lay = CurrentLayer;
            if (lay == null) return;

            var row = new List<VisualElement>();

            // T-0166/T-0185 — the SELECTED layer's lifetime window. Gated on frameCount > 1 like the transport
            // — a still document has no frame axis for "outside its window" to mean anything against.
            if (document.frameCount > 1)
            {
                int lo = lay.startFrame;
                int hi = lay.endFrame < 0 ? document.frameCount - 1 : lay.endFrame;
                row.Add(Z.Field("Lifetime",
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

            // T-0192 — the SELECTED layer's Z (depth) dial. Not gated on frameCount — depth ordering matters
            // on a still document too, unlike Lifetime above which is meaningless without a frame axis.
            row.Add(Val("Z", "Moves the selected layer (“" + (lay.name ?? "Layer") + "”) in "
                + "depth, in canvas pixels, on top of its place in the list (layer index × layer spacing). It "
                + "re-orders as well as shades: push a layer back far enough and the ones below it come "
                + "through, and two raised shapes at different depths intersect along a curve instead of "
                + "one hiding the other.", lay.zOffset, -256f, 256f));

            // T-0187/T-0204 — Height and Mask are TOGGLES on this row now, not their own always-present cards.
            // The card each one summons (BuildHeightSection/BuildMaskSection, ShaperWindow.Sections.cs)
            // appears ONLY while its toggle is on — see the calls below.
            row.Add(Z.Toggle("Height", "Extrude this layer's silhouette into relief. The card below appears "
                + "while this is on.", lay.height != null,
                v => { Change(() => lay.height = v ? NewHeightStage() : null); RefreshSelectedLayerCards(); }));

            row.Add(Z.Toggle("Mask", "Cut this layer with another layer of the same document. Turning this on "
                + "opens the source picker; the card below appears once a source is picked.",
                lay.mask != null && lay.mask.IsSet,
                v =>
                {
                    if (v) { ShowMaskSourceMenu(lay); return; }
                    Change(() => { if (lay.mask != null) lay.mask.sourceLayerId = 0; });
                    RefreshSelectedLayerCards();
                }));

            // T-0258 — Lighting joins Height and Mask on this row, and stops being a toggle-bar section. It is
            // a LAYER property exactly as those two are (ShaperLayer.response, ShaperLightRig.cs:352), so it
            // belongs where the layer is edited rather than in the bar, which is for places you GO.
            //
            // The toggle is the response's own `receiveLighting` flag rather than a second view-only switch:
            // the card already refused to draw a dial while that flag was off, so the flag WAS the card's
            // show/hide condition — same shape as Height (a stage exists or does not) and Mask (a source is
            // picked or is not), and it leaves exactly one control for the one idea instead of a bar segment,
            // a header and an in-card toggle all saying it.
            var resp = lay.response ?? (lay.response = new ShaperLightResponse());
            row.Add(Z.Toggle("Lighting", "Let the document's light rig light this layer. The card below "
                + "appears while this is on; with it off the layer paints its own colours flat.",
                resp.receiveLighting,
                v => { Change(() => resp.receiveLighting = v); RefreshSelectedLayerCards(); }));

            // T-0204 — relocated out of the (now conditional) Mask card: whether this layer draws into the
            // picture is a property of it being used as a mask SOURCE by some OTHER layer, unrelated to
            // whether it has a mask of its own, so it must stay visible even when this layer's own Mask card
            // is absent.
            row.Add(Z.Toggle("Draws into the picture",
                "Turn this off to make THIS layer a pure mask: it still resolves, and other layers may still "
                + "be cut by it, but it never paints into the picture itself. Disabling the layer instead "
                + "turns it off as a mask source too.",
                lay.contributesToPicture, v => Change(() => lay.contributesToPicture = v)));

            selectedLayerCardsHost.Add(Z.HGroup(row.ToArray()));

            if (lay.height != null) BuildHeightSection(selectedLayerCardsHost, lay);
            if (lay.mask != null && lay.mask.IsSet) BuildMaskSection(selectedLayerCardsHost, lay);
            // T-0258 — same absence rule as the two above, driven by the same row's toggle.
            if (resp.receiveLighting) BuildLightingBox(selectedLayerCardsHost, lay);
        }

        /// Opens the "which other layer masks this one" menu directly, for the Mask toggle's on-click
        /// (T-0204): a mask cannot exist without a chosen source (the "never type a reference string" rule —
        /// there is no meaningful "on but nothing picked" state), so turning the toggle on goes straight to
        /// picking one instead of opening a card with nothing in it yet. The picker inside BuildMaskSection's
        /// own card (ShaperWindow.Sections.cs) offers the same menu again, for switching to a different source
        /// once one is already set.
        void ShowMaskSourceMenu(ShaperLayer layer)
        {
            if (layer.mask == null) layer.mask = new ShaperLayerMask();
            var m = layer.mask;

            bool any = false;
            for (int i = 0; i < document.layers.Count; i++)
                if (document.layers[i] != null && document.layers[i] != layer) any = true;
            if (!any)
            {
                ShowNotification(new GUIContent(
                    "This document has no other layer to mask with. Add a second layer first."));
                return;
            }

            var anchor = selectedLayerCardsHost;
            var menu = Z.Menu(anchor).Width(240f);
            for (int i = 0; i < document.layers.Count; i++)
            {
                var cand = document.layers[i];
                if (cand == null || cand == layer) continue;
                var captured = cand;
                string nm = string.IsNullOrEmpty(cand.name) ? "Layer " + (i + 1) : cand.name;
                menu.Item(nm, "Cut this layer with “" + nm + "”. " + ShaperLayerMask.SourceIsReadUnmasked,
                    () => { Change(() => m.sourceLayerId = document.IdOf(captured)); RefreshSelectedLayerCards(); },
                    @checked: captured.id != 0 && captured.id == m.sourceLayerId);
            }
            menu.Show();
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

            // T-0197 — selection alone doesn't add/remove/reorder a layer, so it refreshes only the layer
            // list's own selection dots and the selected-layer cards pane, not the whole window: a full
            // Rebuild() here is what dropped ZuiSectionToggleBar's solo/quick-view choice on every click.
            row.Add(Z.Button(sel ? "●" : "○", "Select this layer to edit its shape below.",
                () => { selectedLayer = li; RebuildLayerList(); RefreshSelectedLayerCards(); }).W(24f));

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

            // T-0258 — Position (was the "Transform" section) sits directly under the shape's own dials,
            // ahead of the optional carves below it: every node has a placement, while Combine/Sweep/Shell
            // and Swarm are things a node may or may not do. Pyre groups the same fields into one collapsible
            // "Position" box on its Shape card (Editor/Pyre/PyreShapeCards.cs:232), which is what this is.
            // A Solid node places, turns and sizes itself with its own dials (Centre, Size, Yaw/Tilt/Roll), and
            // ShaperSolids writes its silhouette over the node's shape program in full — so Position, Sweep and
            // Shell, which act on that program, change nothing on it (measured: 0 pixels for translate, rotate,
            // scale, a 180° sweep and a 4 px shell). Absent rather than greyed: they are not "inert right now",
            // they do not apply to this kind of node at all, the same rule a composite gets for Fill.
            if (node.kind != ShaperNodeKind.Solid)
            {
                BuildPositionBox(box, node);

                // The combine op and the join dials only mean something for a node that has siblings to combine
                // WITH, so they are drawn only for a bag member; the sweep and shell carve this node's own
                // geometry and apply wherever it sits, so they are not gated. (Both used to live in a card
                // called "Modifiers", which they never were — a modifier is an effect on the picture, these
                // are part of the shape.)
                BuildShapeOpsBody(box, node);
            }

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
                    box.Add(Z.Field("Fit", "How the sprite's own pixel aspect maps onto the box below.",
                        Z.MiniRadio((int)p.spriteFitMode, ShaperWords.Names(typeof(ShaperSpriteFitMode)),
                            "“Keep proportions” letterboxes the sprite inside the box; “Stretch to fit” "
                            + "fills the box exactly.",
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
                        Z.MiniRadio((int)p.textAlign, ShaperWords.Names(typeof(ShaperTextAlign)),
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

        /// <summary>
        /// Where this node's content sits and how it is turned — the ex-"Transform" section, now a folded box
        /// inside the Shape card (T-0258), exactly as Pyre groups the same fields into its own "Position" box
        /// (Editor/Pyre/PyreShapeCards.cs:232). Same controls, same ranges, same Undo path; only its home and
        /// its title changed. The state key is DELIBERATELY still "shaper.window.transform" — a key names the
        /// data, never the label, so a saved view that folded this away keeps folding it away.
        /// </summary>
        void BuildPositionBox(VisualElement parent, ShaperNode node)
        {
            // Closes the inventory's worst gap: ShaperTransformBlock (ShaperMatrix.cs) is on EVERY node and had
            // no UI anywhere, which meant a shape could not be positioned at all.
            var t = node.transform;
            var box = positionBox = Z.BoxKeyed("Position",
                "Where this node's content sits, and how it is oriented. Applied to the node's whole content — "
                + "for a bag, to the whole assembly before its members.",
                "shaper.window.transform", "move");

            float ext = Mathf.Max(document.canvasWidth, document.canvasHeight);
            t.EnsureDials();

            // Spatial X/Y pairs are one 2D control, never two packed float fields — dragging two 1D fields to
            // aim one 2D value is the ergonomics problem, and packing them into a row fixes only the width.
            // Value2D rather than Pad, because each axis is its own animatable dial: right-click either to
            // author a Curve and the node travels, grows or leans over the document's frames.
            box.Add(Z.HGroup(
                Val2D("Translate", "Moves the shape, in canvas pixels. Animate it to make the node "
                    + "travel across the canvas over the document's frames.",
                    t.translateX, t.translateY,
                    // T-0186 — no plot-size override: Pyre's own Val2D (PyreWindow.cs:2607) never overrides it
                    // either, so this now matches Pyre's default 140px plot (ZuiValue2DControl.Options.plotSize)
                    // instead of the squished 110px this row used to pass. ZuiHGroup wraps, so a 360px column
                    // still shows 2 envelopes per line rather than overflowing at the wider size.
                    new ZuiValue2DControl.Options().WithRange(-ext, ext, -ext, ext)
                        .WithPrefKey("shaper.transform.translate")),
                // T-0220 — "moves like Translate, in the opposite direction" was the owner's read of this
                // dial with no visual to explain it: the pivot is drawn as a small cross on the preview
                // (ShaperPreviewStage.LayoutOriginCross) exactly so this tooltip's claim is checkable by eye.
                // ShaperMatrix.ToMatrix already composes T·origin·R·S·K·origin⁻¹ (verified invariant at
                // identity rotation/scale/skew, T-0220 probe) — moving Origin alone never moves the shape;
                // it only relocates what Rotation/Scale/Skew turn around.
                Val2D("Origin", "The pivot rotation and scale turn around, in canvas pixels. Moving it does "
                    + "not move the shape by itself.",
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

            // T-0220 — folding the card away turns the pivot cross off too (ShowOrigin reads IsOpen); this is
            // the header-click half of keeping that in sync, since the fold itself doesn't touch the preview.
            box.ViewChanged += RefreshPreview;

            parent.Add(box);
        }

        // ── right pane: preview, transport, bake ─────────────────────────────────────────────────────────

        void BuildRight(VisualElement root)
        {
            var previewSection = Z.Section("Preview",
                "The whole document rendered at the current frame, through the same renderer the bake uses.",
                "shaper.window.preview", icon: "eye");
            // T-0195 — flexGrow:1 here used to be safe because `right` (this section's parent) had a definite
            // height handed down from the Split. Now that `right` is a ScrollView (added this same task, so the
            // Bake box stays reachable at a tall previewHeight), the content main-axis is unbounded, and a
            // flexGrow child of an unbounded flex container resolves to ZERO height instead of "fill what's
            // left" — every row below the stage (Frame/Zoom, transport, status, backdrop, cherry) collapsed
            // onto the same Y and rendered stacked on top of one another (T-0199's walk caught this). The
            // stage already carries its own explicit height, so nothing here needs to grow any more — the
            // whole card is content-sized like everything else in a scroll view, and overflow scrolls instead.
            previewSection.style.flexGrow = 0f;
            previewSection.style.flexShrink = 0f;
            previewSection.style.minHeight = 0f;
            previewSection.contentContainer.style.flexGrow = 0f;
            previewSection.contentContainer.style.minHeight = 0f;

            // `previewFrame` rather than `currentFrame`: under cherry framing the frame on screen is the
            // one the beat sequencer resolved, which may be ShaperCherry.BlankFrame (a deliberate gap).
            stage = new ShaperPreviewStage(() => document, () => previewFrame);
            // T-0195 — an explicit, user-draggable height (BuildPreviewResizeBar, ShaperWindow.Preview.cs),
            // not flexGrow: Pyre's preview island is sized the same way (PyreWindow.cs:96-98, 371-375) and the
            // task asked for parity. flexGrow/flexShrink off so nothing but the drag bar ever changes it.
            stage.style.flexGrow = 0f;
            stage.style.flexShrink = 0f;
            stage.style.height = Mathf.Clamp(previewHeight, PreviewHeightMin, PreviewHeightMax);
            // T-0165 — repaint the cache tick strip / "N/M cached" readout whenever the background pre-baker
            // makes progress, without touching the (expensive) preview image itself.
            stage.CacheProgressed += RefreshCacheReadout;

            // T-0168 — the on-canvas position handle. The stage draws and drags it; which node it belongs to,
            // and what an edit costs in Undo, stay the window's business.
            stage.SelectedNode = () => CurrentNode;
            stage.SelectedLayerRoot = () => CurrentLayer?.root;
            stage.RecordUndo = () => { if (document != null) Undo.RegisterCompleteObjectUndo(document, "Move Shaper Node"); };
            stage.Changed = AfterEdit;
            // Only once the drag has settled: the Transform card's own numeric readout has to catch up with
            // where the handle was dropped, and rebuilding the panel mid-gesture would pull the control out
            // from under the pointer.
            stage.DragCommitted = Rebuild;
            // T-0220 — the pivot cross only means anything while the Position box is the thing being looked
            // at; a box the owner folded away has no business leaving a mark on the picture. (T-0258 — the
            // box is the ex-Transform section, now inside the Shape card; the rule is unchanged.)
            stage.ShowOrigin = () => positionBox != null && positionBox.IsOpen;

            // T-0190 — preview overlays. The stage owes them a buffer and nothing else; which features have
            // marks to draw, and which are switched on, is decided here (ShaperPreviewOverlays).
            stage.WantsOverlays = AnyOverlayOn;
            stage.DrawOverlays = (px, w, h, frame) =>
                ShaperPreviewOverlays.Draw(overlayEntries, px, w, h, document,
                    document.PhaseOfFrame(Mathf.Max(0, frame)));

            previewSection.Add(stage);
            ApplyPreviewChromeToStage();

            // T-0195 — the vertical drag bar, directly under the stage (mirrors Pyre's BuildPreviewResizeBar,
            // PyreWindow.cs:435-451). Sits between the picture and the chrome so it reads as the picture's own
            // bottom edge, not a divider inside the chrome.
            previewSection.Add(BuildPreviewResizeBar());

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
                // T-0257 — the ONE home for this field now (it used to be here and in the Canvas card under
                // the same label), so the tooltip carries the whole story rather than half of it in each.
                Dial("Rate", "Playback rate in frames per second — how fast frames are shown here and how "
                    + "fast a baked clip plays. Nothing in the render pipeline reads it; the render is driven "
                    + "by phase, not by a wall clock.", document.frameRate,
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
            // T-0192 — deliberate, unlike a dial-pane MicroSlider: this is the transport scrubber, packed
            // beside the fixed-width frame readout (not alone in its row), and growing to fill the transport
            // is the whole point — same as Pyre's own Frame scrubber (pyre_1col.png). ZuiAudit's stretch
            // check now also covers bare MicroSliders (they used to be invisible to it entirely), so this
            // needs the same sanctioned opt-out a legitimately-flexing name field gets.
            scrubber.AddToClassList("zui-audit-allow-stretch");
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
                // T-0192 — same deliberate transport-row exception as the Frame scrubber above.
                cherryScrubber.AddToClassList("zui-audit-allow-stretch");
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

            // T-0257 — "Tile px" was an abbreviation of a unit, not a name for the thing it sizes.
            transportHost.Add(Z.MicroSlider("Strip tile size", previewStripTile, 24f, 128f,
                "How big each frame tile in the contact sheet is, in screen pixels. Only changes how the "
                + "strip is drawn — no frame is re-rendered and no bake is affected.",
                v => { previewStripTile = Mathf.Clamp(v, 24f, 128f); FillTransport(); }, 185f, decimals: 0));

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

        /// Wrap EVERY authored mutation. Records Undo on the document BEFORE the mutation (the snapshot is of
        /// the pre-edit state, so recording afterwards would store the value just written and make Ctrl+Z
        /// appear to do nothing), then dirties the asset and refreshes the preview.
        ///
        /// <b>T-0198 — this MUST be RegisterCompleteObjectUndo, not RecordObject.</b> A Shaper document is built
        /// almost entirely out of <c>[SerializeReference]</c> graphs: every node, every fill, every composite
        /// source, every effect and each layer's height stage (<c>ShaperLightRig.cs:388</c>). RecordObject
        /// snapshots an object's serialized VALUES but not its managed-reference registry, so undoing an edit
        /// restored the document with references the snapshot never held — they came back null, the entries
        /// were dropped from the asset's <c>references:</c> block on the next save, and the authored data was
        /// gone with no error anywhere. That is how the demo document lost a layer's whole height stage. The
        /// complete-object undo costs a full snapshot per edit, which for a document asset is nothing next to
        /// silently deleting a stage the author built.
        internal void Change(Action apply)
        {
            if (document != null) Undo.RegisterCompleteObjectUndo(document, "Edit Shaper Document");
            apply();
            // T-0190 — an edit can change WHICH features have marks to show (enabling a swarm, giving it a
            // shape), so the reserved overlay strip is re-collected here rather than only on a full rebuild:
            // a toggle that appears one window-rebuild after the feature it belongs to reads as not existing.
            RefreshOverlayStrip();
            AfterEdit();
        }

        /// <summary>
        /// <b>The one thing that must happen after ANY authored mutation, wherever it was made.</b> Dirties the
        /// asset, drops the caches the edit could have made stale, and re-renders. Every control that writes
        /// authored data lands here — <see cref="Change"/> and <see cref="Val"/> for the hand-built cards, and
        /// the reflection-drawn dials (a hosted generator's, a hosted Pyre layer's, an effect modifier's) and
        /// the gradient control through their own hooks.
        ///
        /// <b>T-0201 — why this is a method and not four copies.</b> Those four reflection/gradient hooks did
        /// <c>SetDirty</c> + <see cref="RefreshPreview"/> and nothing else. RefreshPreview reads THROUGH
        /// <see cref="ShaperPreviewFrameCache"/>, so it re-served the frame rendered before the edit: an Orb or
        /// Plasma Bloom dial, or any dial on a hosted Pyre layer, moved the data and left the picture exactly as
        /// it was, until some unrelated edit that did route through <see cref="Change"/> flushed the cache and
        /// every held-back edit appeared at once (the owner's "didn't make any difference until I disabled and
        /// re-enabled Auto Exposure"). Invalidation is not a per-control decision; it belongs to the act of
        /// editing, which is why it now has exactly one home.
        ///
        /// T-0165's distinction still holds and is the reason this is NOT folded into
        /// <see cref="RefreshPreview"/>: a pure VIEW change (scrub, zoom, frame border, backdrop, playback)
        /// calls RefreshPreview alone and must NOT invalidate, or the cache would never hold anything.
        /// </summary>
        internal void AfterEdit()
        {
            if (document != null) EditorUtility.SetDirty(document);
            stage?.InvalidateFrameCache();
            filmstrip?.Invalidate();
            InvalidateCherryThumbs();
            RefreshFrameReadout();
            RefreshPreview();
        }

        /// A plain-float dial. Use for a field the engine declares as `float`/`int`.
        ///
        /// T-0257 — <paramref name="width"/> exists because a MicroSlider draws its caption INSIDE its track
        /// beside the value, so a dial whose honest name is longer than roughly fourteen characters
        /// ("Depth between layers", "Pixels per unit") ellipsises at the 140px default and is once again
        /// unreadable. Widening the one control is the fix; shortening the name back into jargon is not.
        internal VisualElement Dial(string label, string tooltip, float value, float min, float max,
            Action<float> set, int decimals = -1, float width = 140f)
            => Z.MicroSlider(label, value, min, max, tooltip, v => Change(() => set(v)), width, decimals: decimals);

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
                currentFrame = () => currentFrame,   // a folded envelope reads out the value at the playhead
            };
            // T-0165 — a ZUIValue edit is a data edit, so it takes the same post-edit path as Change(). Val()
            // never routes through Change() itself (Z.Value manages its own Undo timing via the second callback
            // below), so AfterEdit is hooked here instead.
            return Z.Value(label, v, o, tooltip, AfterEdit,
                () => { if (document != null) Undo.RegisterCompleteObjectUndo(document, "Edit Shaper Document"); });
        }

        /// The 2D analog of Val — one control over an X/Y PAIR of animatable dials, so a spatial value is aimed
        /// by dragging a point rather than by nudging two sliders. Same Undo/dirty/cache contract as Val.
        internal VisualElement Val2D(string label, string tooltip, ZUIValue x, ZUIValue y,
                                     ZuiValue2DControl.Options o)
            => Z.Value2D(label, x, y, o, tooltip, AfterEdit,
                () => { if (document != null) Undo.RegisterCompleteObjectUndo(document, "Edit Shaper Document"); });

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
