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
        SliderInt scrubber;
        double lastPlayTick;
        float playAcc;

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

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.minWidth = 320f;
            left.style.minHeight = 0f;
            leftPane = left;

            BuildCanvasSection(left.contentContainer);
            BuildLightsSection(left.contentContainer);   // ShaperWindow.Lights.cs (T-0164) — document-level, sits beside Canvas
            BuildLayersSection(left.contentContainer);
            BuildSelectedLayerSections(left.contentContainer);
            RestoreScroll(left);

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

        void RefreshToggleBar()
        {
            if (toggleBarHost == null) return;
            toggleBarHost.Clear();
            // Null section entries are skipped by the bar itself, so an absent card costs nothing here —
            // which is what lets the shell list every card unconditionally while the absence rule decides
            // at build time which ones actually exist for the current node kind.
            var entries = new List<(string, ZuiSection)>
            {
                // The base's Tags section is one of this tool's sections, so it folds from the same bar as
                // the rest rather than sitting outside the tool's own chrome.
                ("Tags", TagsSection),
                ("Canvas", canvasSection),
                ("Lights", lightsSection),   // ShaperWindow.Lights.cs (T-0164)
                ("Layers", layersSection),
                ("Shape", shapeSection),
                ("Transform", transformSection),
            };
            entries.AddRange(SectionBarEntries());   // the cards ShaperWindow.Sections.cs owns
            toggleBarHost.Add(new ZuiSectionToggleBar("ShaperWindow", entries.ToArray()));
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
        static ShaperLayer NewLayer(string name, ShaperDocument doc)
        {
            var layer = new ShaperLayer
            {
                name = name,
                enabled = true,
                root = new ShaperNode { name = "Shape" },
            };
            if (doc != null && layer.root.primitive != null)
            {
                layer.root.primitive.rectHalfW = Mathf.Max(2f, doc.canvasWidth * 0.25f);
                layer.root.primitive.rectHalfH = Mathf.Max(2f, doc.canvasHeight * 0.25f);
            }
            return layer;
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
                Dial("Layer spacing", "Canvas pixels between consecutive layers' base planes. With a layer's "
                    + "own Z offset this gives base(i) = i × spacing + zOffset(i), the layer's only Z "
                    + "contributor (HS-7.2).", document.layerSpacing, 0f, 8f, v => document.layerSpacing = v)));

            box.Add(Z.HGroup(
                Dial("Frames", "How many frames this document resolves to. 1 is a still document, where every "
                    + "frame maps to phase 0.", document.frameCount, 1f, 120f,
                    v => document.frameCount = Mathf.Max(1, Mathf.RoundToInt(v)), decimals: 0),
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
                    Z.MinMax(lo, hi, 0f, document.frameCount - 1,
                        "The selected layer's frame lifetime window — the frames it contributes to.",
                        (newLo, newHi) => Change(() =>
                        {
                            lay.startFrame = Mathf.RoundToInt(newLo);
                            // Only write a real endFrame when it no longer means "the last frame" — keeps an
                            // unauthored window at its -1 default through a later frameCount change.
                            int rh = Mathf.RoundToInt(newHi);
                            lay.endFrame = rh >= document.frameCount - 1 ? -1 : rh;
                        }), 200f, isInt: true)));
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

            // zOffset is ZUIValue on the real ShaperLayer, so it is a real Z.Value row, not a plain slider.
            row.Add(Val("Z", "This layer's own Z-position offset in canvas pixels, added to the ordering base "
                + "(layer index × layer spacing) to give its base plane. Signed — it can pull a layer forward "
                + "as well as push it back.", layer.zOffset, -256f, 256f));

            // T-0166 — the lifetime window used to live HERE as a bare, unlabeled slider, and PM's by-eye vet
            // (pm-vet-wave2-light-crop.png) caught it: no label, no readable min/max, running underneath the
            // Dup button. The row is already tight (grip/toggle/select/name/Z/Dup/×), so per ui-layout-rules'
            // "Card layout" a control this wide earns its OWN row instead of being crammed into every layer's —
            // see BuildLayersSection's "Lifetime" card below the list, which edits the SELECTED layer only.

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

            row.Add(Z.Button("×", "Remove this layer.", () =>
            {
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
            var box = shapeSection = Z.Section("Shape",
                "What this layer's root node generates.",
                "shaper.window.shape", icon: "shapes");

            // Enum names come from the enum itself rather than a hardcoded list: ShaperNodeKind is append-only,
            // so a future kind appears here automatically instead of silently missing.
            box.Add(Z.Field("Kind", "Whether this node is a single primitive, a bag combining children, a "
                + "composite generator, or a pseudo-3D solid.",
                Z.MiniRadio((int)node.kind, Enum.GetNames(typeof(ShaperNodeKind)),
                    "Whether this node is a single primitive, a bag combining children, a composite "
                    + "generator, or a pseudo-3D solid.",
                    v => { Change(() => node.kind = (ShaperNodeKind)v); Rebuild(); })));

            if (node.kind == ShaperNodeKind.Primitive)
                BuildPrimitiveBody(box, node.primitive);

            root.Add(box);
        }

        void BuildPrimitiveBody(VisualElement box, ShaperPrimitiveDef p)
        {
            box.Add(Z.Field("Primitive", "Which primitive shape this node generates.",
                Z.MiniRadio((int)p.kind, Enum.GetNames(typeof(ShaperPrimitiveKind)),
                    "Which primitive shape this node generates. Switching it changes the dials below.",
                    v => { Change(() => p.kind = (ShaperPrimitiveKind)v); Rebuild(); })));

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
                    new ZuiValue2DControl.Options().WithRange(-ext, ext, -ext, ext).WithPlotSize(110f)
                        .WithPrefKey("shaper.transform.translate")),
                Val2D("Origin", "The point this node rotates and scales around, in canvas pixels.",
                    t.originX, t.originY,
                    new ZuiValue2DControl.Options().WithRange(-ext, ext, -ext, ext).WithPlotSize(110f)
                        .WithPrefKey("shaper.transform.origin")),
                Val2D("Scale", "Scale this node's content on each axis. 1 is unscaled. Animate it to make the "
                    + "node grow or shrink over the document's frames.",
                    t.scaleX, t.scaleY,
                    new ZuiValue2DControl.Options().WithRange(0.05f, 4f, 0.05f, 4f).WithPlotSize(110f)
                        .WithDefault(Vector2.one).WithPrefKey("shaper.transform.scale")),
                Val2D("Skew", "Slant this node's content on each axis, in degrees.",
                    t.skewX, t.skewY,
                    new ZuiValue2DControl.Options().WithRange(-80f, 80f, -80f, 80f).WithPlotSize(110f)
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

            previewSection.Add(stage);
            ApplyPreviewChromeToStage();

            previewSection.Add(BuildPreviewChrome());

            // Transport only exists for an animated document — a still one has nothing to scrub or play.
            if (document.frameCount > 1) previewSection.Add(BuildTransport());

            root.Add(previewSection);

            // Backdrop and cherry framing sit below the preview, in Pyre's own order (backdrop chrome, then
            // the cherry panel), so the things that change what the preview LOOKS like are grouped together.
            BuildBackdropPanel(root);
            if (document.frameCount > 1) root.Add(BuildCherryPanel());

            root.Add(Z.Button("Bake",
                "Bake this document to a sprite sheet PNG, an AnimationClip and a ShaperClip, beside the "
                + "document's own asset. Never overwrites an existing bake — a repeat bake is versioned.",
                DoBake));
        }

        /// What the preview should actually show. Under cherry framing that is the beat sequencer's resolved
        /// frame (possibly a blank); otherwise it is the transport's own frame.
        int previewFrame => document != null && document.cherryEnabled && cherryRunning
            ? cherryState.frame
            : currentFrame;

        VisualElement BuildTransport()
        {
            var host = new VisualElement();
            int max = Mathf.Max(0, document.frameCount - 1);
            currentFrame = Mathf.Clamp(currentFrame, 0, max);

            playButton = Z.Button(playing ? "❚❚ Pause" : "▶ Play", "Play or pause the looping preview.", () =>
            {
                playing = !playing;
                playButton.text = playing ? "❚❚ Pause" : "▶ Play";
                if (playing)
                {
                    lastPlayTick = EditorApplication.timeSinceStartup;
                    playAcc = 0f;
                    // Start the cherry sequence from its first slot on every press, so Play always means the
                    // same thing rather than resuming a half-finished loop from whenever you last paused.
                    ResetCherryPlayback();
                    EditorApplication.update -= PlaybackTick;
                    EditorApplication.update += PlaybackTick;
                }
            });

            host.Add(Z.HGroup(
                playButton,
                Dial("Rate", "Playback rate in frames per second.", document.frameRate,
                    ShaperClock.MinFrameRate, ShaperClock.MaxFrameRate, v => document.frameRate = v, decimals: 0)));

            // GIF export (T-0160, Pyre parity — PyreWindow.cs:557-584): the button opens a Save dialog and
            // writes an animated GIF over the SAME playback order the Bake button writes to the sheet/clip
            // (ShaperBaker.PlaybackOrder, cherry framing and blanks included). Scale/dither are window state
            // (previewGifScale/previewGifDither, ShaperWindow.Preview.cs) — cosmetic to the export only, never
            // read by the renderer, so they need no Undo/Change wrapper.
            host.Add(Z.HGroup(
                Z.Button("GIF…",
                    "Export the whole animation as an animated GIF — transparent background, loops forever, at "
                    + "the frame rate above. Opens a Save dialog for the file location.",
                    ExportGif),
                Z.MicroSlider("GIF scale", previewGifScale, 1f, 8f,
                    "Nearest-neighbour upscale applied ONLY to the exported GIF (1–8×) — it does NOT change the "
                    + "live preview, only the pixel size of the saved .gif file.",
                    v => previewGifScale = Mathf.Clamp(Mathf.RoundToInt(v), 1, 8), 150f,
                    showValue: true, decimals: 0),
                Z.Toggle("GIF dither",
                    "GIF transparency is one bit — every pixel is either fully opaque or fully invisible, so a "
                    + "soft edge has to be kept or dropped. On (recommended) stipples the partly-transparent "
                    + "band so soft rims and fades still read as fading; off cuts them at 50% opacity, which "
                    + "turns a feathered edge into a hard silhouette. Export only — the live preview is unaffected.",
                    previewGifDither, v => previewGifDither = v)));

            scrubber = Z.SliderInt(currentFrame, 0, max,
                "Scrub to an exact frame. Dragging pauses playback and holds that frame.", v =>
                {
                    currentFrame = v;
                    playing = false;
                    // Hand control back to the scrubber. Without this the cherry sequencer would still be
                    // "running", so previewFrame would keep returning its last resolved beat and the scrub
                    // would appear to do nothing — the drag would move the handle and not the picture.
                    cherryRunning = false;
                    if (playButton != null) playButton.text = "▶ Play";
                    RefreshPreview();
                }, 220f);
            host.Add(Z.Field("Frame", "Scrub to an exact frame.", scrubber));

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
            host.Add(Z.HGroup(cacheTickStrip, cacheReadoutLabel));
            RefreshCacheReadout();

            // T-0176 — filmstrip contact sheet: click a tile to jump the transport there (Pyre parity).
            filmstrip = new ShaperFilmstripElement(() => document,
                () => currentFrame,
                jumpTo =>
                {
                    currentFrame = Mathf.Clamp(jumpTo, 0, Mathf.Max(0, document.frameCount - 1));
                    playing = false;
                    cherryRunning = false;
                    if (playButton != null) playButton.text = "▶ Play";
                    scrubber?.SetValueWithoutNotify(currentFrame);
                    RefreshPreview();
                });
            host.Add(filmstrip);
            filmstrip.Rebuild();

            return host;
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
                    scrubber?.SetValueWithoutNotify(currentFrame);
                }
                RefreshPreview();
                return;
            }

            int nextFrame = ShaperClock.WrapFrame(currentFrame + steps, Mathf.Max(1, document.frameCount));

            // T-0165 — playback only steps onto CACHED frames (Pyre's own behaviour). Advancing onto an
            // uncached frame would mean a synchronous ~30ms render mid-tick, which is exactly the stutter a
            // background pre-baker exists to avoid. Instead, hold the currently-shown frame: the accumulator
            // above has already been debited for this tick's beats, so playback simply catches up once the
            // pre-baker (running via InvalidateFrameCache/Refresh) fills the frame in — no synchronous block,
            // and no picture shown that was never actually requested to render.
            if (stage != null && !stage.IsFrameCached(nextFrame)) return;

            currentFrame = nextFrame;
            scrubber?.SetValueWithoutNotify(currentFrame);
            RefreshPreview();
        }

        // GIF export (T-0160, Pyre parity — PyreWindow.cs:665-672). The path comes from a user Save dialog
        // (cancel = empty path = no-op); RevealInFinder opens the result folder. No AssetDatabase work here —
        // if the user saves inside Assets/ the caller owns any import-refresh implications, matching Pyre.
        void ExportGif()
        {
            if (document == null) return;
            string path = EditorUtility.SaveFilePanel("Export GIF", "", (document.name ?? "Shaper") + ".gif", "gif");
            if (string.IsNullOrEmpty(path)) return;
            ShaperGif.Export(document, path, Mathf.Clamp(previewGifScale, 1, 8), previewGifDither);
            EditorUtility.RevealInFinder(path);
        }

        void DoBake()
        {
            var result = ShaperBaker.Bake(document, pixelsPerUnit: document.pixelsPerUnit);
            if (!result.ok)
            {
                Debug.LogError("[Shaper] Bake failed: " + result.message, document);
                return;
            }

            Debug.Log($"[Shaper] Baked {result.sheetFrames} frame(s) → {result.sheetPath}", document);
            var sheet = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.sheetPath);
            if (sheet != null) EditorGUIUtility.PingObject(sheet);
        }

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
                controlWidth = 130f, grow = false, cyclic = cyclic, decimals = decimals,
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
