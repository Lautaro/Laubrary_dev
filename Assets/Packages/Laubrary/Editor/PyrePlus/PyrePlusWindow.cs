// PyrePlusWindow — the authoring window for the PyrePlus prototype (see PYREPLUS_DESIGN.md).
//
// Built directly on the UI Toolkit toolkit (ZuiAssetWindow + Z.* controls + ZuiSection folding), NOT on
// Zolumn — the UITK migration already gives collapsible bordered sections and grow-to-fill layout, so the
// design's Zolumn dependency is dropped. SLICE 1: the Shape section + a live preview. Swarm and Modifiers
// sections follow. Deliberately a separate menu item and asset type from real Pyre, which is untouched.
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.BackSplash.Editor;
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

        // Dial-pane width, draggable via the vertical splitter (mirrors Pyre1). Persists across domain
        // reloads. The ColumnFlow reads this width, so dragging the divider wider adds columns.
        [SerializeField] float leftPaneWidth = 360f;
        ScrollView leftPane;

        // preview state
        IMGUIContainer preview;
        Texture2D previewTex;
        double lastTime;
        float acc;
        bool playing = true;
        int frame;
        Button playButton;
        SliderInt scrubSlider;   // frame scrubber (transport parity with Pyre1); drives `frame`, follows playback
        Label frameReadout;      // "frame N/M" readout beside Zoom/Speed, kept in sync with `frame`
        VisualElement backdropHost;
        // The preview backdrop is an editor-only BackSplash (camera colour + one image), held on the WINDOW — not
        // the runtime spec, since PyrePlus's runtime asmdef doesn't reference BackSplash. Lazily created; a cosmetic
        // authoring aid, never baked. Mirrors how PyreWindow holds its BackSplash, but window-scoped rather than
        // per-asset.
        Laubrary.BackSplash.BackSplashSettings _backSplash;
        Laubrary.BackSplash.BackSplashSettings backSplash => _backSplash ??= new Laubrary.BackSplash.BackSplashSettings();

        // ── layer selection (window state, never serialized on the spec) ─────────────
        // Which layer the Shape / Swarm / Matte / Modifiers sections below edit. Defaults to the LAST layer on
        // asset load; a click in the layer list re-points it. SelLayer clamps so a stale index is always safe.
        int layerSel;
        PyrePlusLayer SelLayer
        {
            get
            {
                if (spec == null || spec.layers == null || spec.layers.Count == 0) return null;
                layerSel = Mathf.Clamp(layerSel, 0, spec.layers.Count - 1);
                return spec.layers[layerSel];
            }
        }
        VisualElement layerListHost;   // refilled by RebuildLayerList on any list change (rows + any enabled per-layer matte box)

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
            DestroyStripCache();
        }
        protected override void OnAssetChanged()
        {
            frame = 0; previewDirty = true; DestroyStripCache();
            layerSel = int.MaxValue;   // a NEW asset defaults to its last layer (BuildAsset clamps)
            NormalizeSolidDefaultFills();   // FIX 1 (initial build): once-per-load steady-fill migration for solids
        }

        // FIX 1 (initial build) — on asset LOAD only (never on undo or a plain rebuild, so it can't fight Undo),
        // migrate any pre-existing 3D-solid layer that still carries the EXACT pristine OverLife fire default to a
        // steady Solid material. Undo-safe (one Dirty), and only records/dirties when something actually converts,
        // so selecting an already-steady asset stays clean. New assets start as Disc, so in practice this only ever
        // touches legacy solid layers authored before this fix.
        void NormalizeSolidDefaultFills()
        {
            if (spec == null || spec.layers == null) return;
            bool need = false;
            foreach (var l in spec.layers)
                if (l != null && IsSolidForm(l.shapeForm) && PyrePlusLayer.IsPristineDefaultShapeFill(l.shapeFill)) { need = true; break; }
            if (!need) return;
            Dirty(() => { foreach (var l in spec.layers) SteadyDefaultFillForSolid(l); });
        }

        void Tick()
        {
            if (!playing || spec == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            acc += dt * Mathf.Max(1f, spec.previewFps);
            bool advanced = false;
            while (acc >= 1f) { acc -= 1f; frame = (frame + 1) % Mathf.Max(1, spec.frameCount); advanced = true; }
            // In Strip mode the frames themselves don't change as playback advances — only the highlighted tile
            // moves — so DON'T set previewDirty (that would needlessly re-render every tile); just repaint. In
            // single-frame mode previewDirty forces the new frame to render.
            if (advanced) { if (!spec.previewStrip) previewDirty = true; preview?.MarkDirtyRepaint(); RefreshTransportReadout(); }
        }

        bool previewDirty = true;
        int lastRenderedFrame = -1;

        // Coalesced canvas-size range refresh (Bug 1). The Canvas Size slider's derived-range Rebuild is deferred
        // to drag-commit and scheduled onto the next frame; this holds the pending item so rapid commits coalesce
        // into one Rebuild instead of stacking several.
        UnityEngine.UIElements.IVisualElementScheduledItem rangeRebuildPending;

        // Any authored edit routes through here (Dirty → MarkDirty). Refreshing the transport readout alongside
        // keeps the scrubber's range + value and the "frame N/M" label in sync when e.g. the frame count changes.
        void MarkDirty() { previewDirty = true; preview?.MarkDirtyRepaint(); RefreshTransportReadout(); }

        protected override void BuildAsset(VisualElement root, PyrePlusSpec s)
        {
            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;

            // ── left: dials ──────────────────────────────────────────────────────
            // A FIXED-width pane the user resizes by dragging the vertical splitter (mirrors Pyre1). The width
            // (leftPaneWidth, persisted) drives the ColumnFlow: drag the divider past 720px and the dial stack
            // splits into two columns, past 1080 into three, and so on (up to the maxWidth four-column cap).
            var left = new ScrollView(ScrollViewMode.Vertical);
            leftPane = left;
            left.style.width = Mathf.Clamp(leftPaneWidth, 360f, 4f * 360f + 3f * 6f);
            left.style.flexShrink = 0f;                  // fixed — the preview takes the remaining width
            left.style.minHeight = 0f;
            var dials = left.contentContainer;
            // The ScrollView's content container is content-sized by default; stretch it so the flow fills the pane
            // width (otherwise the flow would size to its widest unit, never the pane, and never split).
            dials.style.flexGrow = 1f;

            // The whole dial stack is ONE width-driven column flow: a single 360px column that splits into 2–4
            // contiguous columns as the pane widens. Each Build* below adds EXACTLY ONE top-level unit (a box or a
            // section), in the order they read down column 1, then column 2, … The flow never reaches inside a
            // unit, so the per-section rebuild helpers (RebuildShape/RebuildSwarm/…) keep working wherever their
            // section lands — they mutate section BODIES, never the flow. Never call flow.Clear(); rebuild instead.
            var flow = Z.ColumnFlow(360f);
            dials.Add(flow);

            // Saved-views bar rides at the very top of the dials pane. Its capture/apply aggregate every
            // ZuiBox under this asset root (Canvas / Solid / Transform / Modifiers): a "view" is their fold,
            // gear-open and shown-control state only, never an authored value. The query root is `root` (the
            // BuildAsset host), not rootVisualElement — `split` is added to `root` below BEFORE RestoreLast
            // runs, but the window has not yet parented `root` to its own rootVisualElement at this point. The
            // boxes still resolve through the flow's columns (Query walks the live tree), so the flow is transparent.
            var viewBar = BuildViewBar(root);
            flow.Add(viewBar);

            // Selection is STICKY across rebuilds — only clamped to a valid index. (Defaulting to the
            // last layer on every rebuild silently jumped the overlay/sections to another layer after
            // any undo or structural edit; a fresh ASSET picks its last layer via OnAssetChanged.)
            if (s.layers != null) layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, s.layers.Count - 1));

            BuildLayerList(flow);      // the compact layer stack — under the views bar, above Canvas
            BuildGlobalModifiers(flow, s);   // task #56 — spec-wide modifiers applied to EVERY layer (right after Layers)
            BuildCanvas(flow, s);
            BuildShape(flow, s);
            BuildSwarm(flow, s);
            BuildModifiers(flow, s);   // PyrePlusWindow.Modifiers.cs
            BuildImport(flow);         // PyrePlusWindow.Import.cs — "Import from Pyre…" converter (slice 9)
            // No standalone Matte section: matte is a property of a layer, authored per-row in the layer list
            // above (BuildMatteBox, folded under each row) — mirroring Pyre1, where a matte reads as belonging
            // to the layer in the STACK (where it acts) rather than as a dial in a separate section.

            // ── right: preview + transport + backdrop ────────────────────────────
            // The dial pane is a fixed width; the preview takes whatever remains (flexGrow 1).
            var rightPane = new VisualElement();
            rightPane.style.flexGrow = 1f;
            rightPane.style.minWidth = 260f;
            rightPane.style.minHeight = 0f;

            preview = new IMGUIContainer(() => DrawPreview(s));
            preview.style.flexGrow = 1f;
            preview.style.minWidth = 200f;
            preview.style.minHeight = 160f;
            preview.AddToClassList("zui-stage");
            rightPane.Add(preview);

            // Transport (Play/Pause + Frame border) and the shared BackSplash backdrop panel sit below the preview.
            var chrome = new VisualElement();
            chrome.style.flexShrink = 0f;
            BuildTransport(chrome, s);
            BuildBackdropPanel(chrome);
            rightPane.Add(chrome);

            split.Add(left);
            split.Add(BuildVerticalSplitter());   // drag to resize the dial pane (and change its column count)
            split.Add(rightPane);
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            root.Add(split);

            // Whole tree is now under `root`; re-apply the view the user left this window in.
            viewBar.RestoreLast();
        }

        // A 6px draggable divider between the dial pane and the preview (mirrors Pyre1's splitter). Dragging sets
        // leftPaneWidth + the pane's fixed width live; the ColumnFlow reads that width, so a wider pane = more
        // columns. Clamped between one column (360) and the four-column cap (or the window width, whichever is less).
        VisualElement BuildVerticalSplitter()
        {
            var s = new VisualElement { tooltip = "Drag to resize the dial pane (wider = more control columns)." };
            s.style.width = 6f;
            s.style.flexShrink = 0f;
            s.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            s.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { s.CapturePointer(e.pointerId); e.StopPropagation(); } });
            s.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!s.HasPointerCapture(e.pointerId)) return;
                float cap = Mathf.Min(4f * 360f + 3f * 6f, Mathf.Max(360f, position.width - 260f));
                leftPaneWidth = Mathf.Clamp(leftPaneWidth + e.deltaPosition.x, 360f, cap);
                if (leftPane != null) leftPane.style.width = leftPaneWidth;
                e.StopPropagation();
            });
            s.RegisterCallback<PointerUpEvent>(e => { if (s.HasPointerCapture(e.pointerId)) s.ReleasePointer(e.pointerId); });
            return s;
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

        // Preview transport: a Play/Pause button (flips its own label) + the Frame-border toggle + the Strip
        // (filmstrip / contact-sheet) toggle and, when Strip is on, a Tile-px size slider. Lives in a host that
        // rebuilds on the Strip toggle so the Tile-px slider appears/disappears (the same show/hide idiom the
        // Shape/Swarm sections use). The minimal mirror of PyreWindow's transport row.
        VisualElement transportHost;

        void BuildTransport(VisualElement root, PyrePlusSpec s)
        {
            transportHost = new VisualElement();
            root.Add(transportHost);
            RebuildTransport(s);
        }

        void RebuildTransport(PyrePlusSpec s)
        {
            if (transportHost == null) return;
            transportHost.Clear();
            playButton = Z.Button(playing ? "❚❚ Pause" : "▶ Play", "Play or pause the looping preview.", () =>
            {
                playing = !playing;
                playButton.text = playing ? "❚❚ Pause" : "▶ Play";
            });
            var kids = new List<VisualElement>
            {
                playButton,
                Z.Toggle("Frame",
                    "Draw a thin border around the canvas edge (cosmetic only — never baked). In Strip mode it "
                    + "outlines every frame tile.",
                    s.previewShowFrame, v => Dirty(() => s.previewShowFrame = v)),
                Z.Toggle("Strip",
                    "Show the whole animation as a contact sheet of every frame instead of one zoomed frame. Tiles "
                    + "lay out left-to-right and wrap to more rows when they overflow the width; click a tile to "
                    + "jump the transport to that frame. (No scrolling — a block taller than the view is clipped.)",
                    s.previewStrip, v => { Dirty(() => s.previewStrip = v); RebuildTransport(s); }),
            };
            if (s.previewStrip)
                kids.Add(Z.MicroSlider("Tile px", s.previewStripSize, 32f, 256f,
                    "Size of each frame tile in the contact sheet, in pixels (32–256). Only changes how the strip is "
                    + "laid out — the frames aren't re-rendered.",
                    v => DirtyRepaintOnly(() => s.previewStripSize = Mathf.Clamp(v, 32f, 256f)), 150f,
                    showValue: true, decimals: 0));
            // GIF export (G3): the button opens a Save dialog and writes an animated GIF; the packed scale field is
            // its nearest-neighbour upscale. The scale is a cosmetic spec field — record Undo + SetDirty, no
            // re-render (it doesn't touch the preview frames).
            kids.Add(Z.Button("GIF…",
                "Export the whole animation as an animated GIF — transparent background, loops forever, at the "
                + "frame rate above. Opens a Save dialog for the file location.",
                ExportGif));
            kids.Add(Z.MicroSlider("Scale", s.previewGifScale, 1f, 8f,
                "Nearest-neighbour upscale applied to the exported GIF (1–8×). Bigger = a larger file with the "
                + "same crisp pixels.",
                v =>
                {
                    if (spec == null) return;
                    Undo.RecordObject(spec, "Edit Pyre Plus");
                    s.previewGifScale = Mathf.Clamp(Mathf.RoundToInt(v), 1, 8);
                    EditorUtility.SetDirty(spec);
                }, 150f, showValue: true, decimals: 0));
            transportHost.Add(Z.HGroup(kids.ToArray()));   // Play + Frame/Strip/Tile/GIF/Scale as one wrapping unit-row

            // ── frame scrubber (transport parity with Pyre1's scrub field) ──────────────────────
            // A 1-based int slider over the whole frame range that drives the transport `frame`. Dragging it PAUSES
            // playback and holds that frame — mirroring PyreWindow's scrub (playing = false + reset the Play label).
            // It works in filmstrip mode too: there it just moves the highlighted tile, and since clicking a tile
            // also sets `frame` + calls RefreshTransportReadout, slider ↔ tile stay in sync. No previewDirty — the
            // frame change alone re-renders the single-frame view (DrawPreview: cur != lastRenderedFrame).
            int fcHigh = Mathf.Max(1, s.frameCount);
            scrubSlider = Z.SliderInt(Mathf.Clamp(frame, 0, fcHigh - 1) + 1, 1, fcHigh,
                "Scrub to an exact frame — dragging pauses playback and holds that frame (works in the filmstrip too).",
                v =>
                {
                    frame = Mathf.Clamp(v - 1, 0, Mathf.Max(0, s.frameCount - 1));
                    playing = false;
                    if (playButton != null) playButton.text = "▶ Play";
                    preview?.MarkDirtyRepaint();
                    RefreshTransportReadout();
                }, 200f);
            transportHost.Add(Z.Field("Frame",
                "Scrub to an exact frame — dragging pauses playback and holds that frame.", scrubSlider));

            // ── zoom + speed + the frame readout, grouped exactly like PyreWindow's transport ───
            // Zoom magnifies the single-frame view only (cosmetic layout, never re-renders the frames or the
            // filmstrip → DirtyRepaintOnly), rounded to an integer like Pyre1. Speed drives the preview frame rate
            // (previewFps) — an absolute fps rather than Pyre1's 0.1–3× multiplier, since previewFps IS PyrePlus's
            // playback rate; it also sets the exported GIF's rate. The readout mirrors Pyre1's "frame N/M" label.
            var zoomMs = Z.MicroSlider("Zoom", s.previewZoom, 1f, 16f,
                "Magnification of the single-frame preview (canvas pixels × zoom). Does not affect the filmstrip or "
                + "the baked frames.",
                v => DirtyRepaintOnly(() => s.previewZoom = Mathf.Max(1f, Mathf.Round(v))), 150f,
                showValue: true, decimals: 0);
            var speedMs = Z.MicroSlider("Speed", s.previewFps, 1f, 30f,
                "Preview playback rate in frames per second — how fast the loop plays (also the exported GIF's rate).",
                v => DirtyRepaintOnly(() => s.previewFps = Mathf.Clamp(Mathf.Round(v), 1f, 30f)), 150f,
                showValue: true, decimals: 0);
            frameReadout = Z.Text("", ZuiText.Subtle, "The frame currently shown / the total frame count.");
            transportHost.Add(WrapRow(zoomMs, speedMs, frameReadout));

            RefreshTransportReadout();
        }

        // Keep the scrubber value + range and the "frame N/M" readout in sync with the transport `frame` and the
        // current frame count. Called after every frame change (playback tick, a strip-tile click, a scrub drag)
        // and at the end of RebuildTransport. Null-guarded so it's safe before the transport is built / rebuilt.
        void RefreshTransportReadout()
        {
            if (spec == null) return;
            int fc = Mathf.Max(1, spec.frameCount);
            int cur = Mathf.Clamp(frame, 0, fc - 1);
            if (scrubSlider != null)
            {
                scrubSlider.highValue = fc;
                scrubSlider.SetValueWithoutNotify(cur + 1);
            }
            if (frameReadout != null) frameReadout.text = $"frame {cur + 1}/{fc}";
        }

        // GIF export (G3): render every frame and write an animated GIF at the chosen nearest-neighbour scale. The
        // path comes from a user Save dialog (cancel = empty path = no-op); RevealInFinder opens the result folder.
        // No AssetDatabase work here — if the user saves inside Assets/ the orchestrator owns the import refresh.
        void ExportGif()
        {
            if (spec == null) return;
            string path = EditorUtility.SaveFilePanel("Export GIF", "", (spec.name ?? "PyrePlus") + ".gif", "gif");
            if (string.IsNullOrEmpty(path)) return;
            PyrePlusGif.Export(spec, path, Mathf.Clamp(spec.previewGifScale, 1, 8));
            EditorUtility.RevealInFinder(path);
        }

        // The shared BackSplash backdrop panel (also used by Pyre / Mirage) — a cosmetic preview aid, never baked.
        // onStructureChanged rebuilds it when the backdrop image is picked or cleared (it adds/removes controls).
        void BuildBackdropPanel(VisualElement root)
        {
            backdropHost = new VisualElement();
            root.Add(backdropHost);
            FillBackdropPanel();
        }

        void FillBackdropPanel()
        {
            if (backdropHost == null) return;
            backdropHost.Clear();
            backdropHost.Add(BackSplashZui.Build(backSplash, "Preview backdrop",
                "A cosmetic backdrop for the preview only — a solid colour plus one optional image. Never baked and "
                + "has no effect on the render. A private copy: Recall copies values FROM a preset, Save writes them TO one.",
                onChanged: () => preview?.MarkDirtyRepaint(),
                onStructureChanged: () => { preview?.MarkDirtyRepaint(); FillBackdropPanel(); }));
        }

        void BuildCanvas(VisualElement root, PyrePlusSpec s)
        {
            var box = Z.BoxKeyed("Canvas", "The output resolution, frame count, seed and background.", "pyreplus.canvas");
            // Canvas Size drives the RANGES of every pixel-scaled control (Shape Size, Scale, offsets, Streak
            // length, Travel…), so a change must refresh those ranges — but a full Rebuild() recreates THIS very
            // slider, and doing it per drag-delta destroyed the pointer capture mid-gesture, aborting the drag after
            // one step (Bug 1, 2026-07-26). So during the drag we only apply the value + repaint (Dirty); the
            // range-refreshing Rebuild is deferred to drag-COMMIT (pointer up) and coalesced onto the next frame, so
            // the tree is torn down AFTER the event finishes dispatching, never under the live gesture. The preview
            // still updates live throughout the drag (Dirty repaints).
            var sizeSlider = Z.MicroSlider("Size", s.canvasSize, 16f, 256f,
                "Square canvas size in pixels. Every pixel-ranged dial (Shape size, shape Scale, offsets, "
                + "Streak length…) scales its maximum off this, so releasing the drag refreshes those ranges.",
                v => Dirty(() => s.canvasSize = Mathf.Clamp(Mathf.RoundToInt(v), 16, 256)), 150f,
                showValue: true, decimals: 0);
            sizeSlider.RegisterCallback<PointerUpEvent>(_ => ScheduleRangeRebuild());
            box.Add(WrapRow(
                sizeSlider,
                Z.MicroSlider("PPU", s.pixelsPerUnit, 1f, 64f,
                    "Pixels per unit for the baked sprite.",
                    v => Dirty(() => s.pixelsPerUnit = Mathf.Clamp(v, 1f, 64f)), 150f, showValue: true)));
            box.Add(WrapRow(
                // Frames is a label-inside int MicroSlider (NOT a thumbed SliderInt): a thumbed slider here read as a
                // frame scrubber and the user kept grabbing it by mistake. The transport's frame SCRUBBER stays a
                // thumbed Z.SliderInt on purpose (Pyre1 parity) — this "how many frames to bake" count does not.
                Z.MicroSlider("Frames", s.frameCount, 1f, 64f,
                    "How many frames the animation bakes to.",
                    v => Dirty(() => s.frameCount = Mathf.Clamp(Mathf.RoundToInt(v), 1, 64)), 150f,
                    showValue: true, decimals: 0),
                Z.Field("Seed", "Random seed — every particle's randomness derives from it.",
                    Z.Int(s.seed, "Random seed.", v => Dirty(() => s.seed = v), 70f))));
            box.Add(BackgroundFillRow(s));
            root.Add(box);
        }

        // The background clear, as a ZuiFill — Solid transparent by default (which bakes exactly as the old flat
        // clear). Editing it in ANY way flips backgroundUseFill on, so the renderer switches from the flat
        // `background` clear to evaluating this fill per pixel across the canvas (a gradient / noise / grid / dots /
        // sprite backdrop). No separate "use fill" toggle: a Solid-transparent fill already reproduces the empty
        // backdrop, so editing-turns-it-on is the whole control (ui-layout-rules: don't add a toggle you don't need).
        ZuiFillControl BackgroundFillRow(PyrePlusSpec s)
        {
            s.backgroundFill ??= new ZuiFill(new Color(0f, 0f, 0f, 0f));
            return Z.Fill("Background",
                s.backgroundFill,
                "The backdrop behind every layer. Default = a transparent clear (composites into a game scene). "
                + "Editing it turns on the per-pixel background fill: make it a solid colour, a gradient, noise, a "
                + "grid, dots, or a stamped sprite. Cosmetic backdrops for the PREVIEW only live in the backdrop "
                + "panel below — this one IS baked into the frames.",
                onChanged: () => { s.backgroundUseFill = true; if (spec != null) EditorUtility.SetDirty(spec); MarkDirty(); },
                onBeforeMutate: () => { if (spec != null) Undo.RecordObject(spec, "Edit Pyre Plus"); },
                new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f));
        }

        // ── layer list (R3) ─────────────────────────────────────────────────────────
        // A compact stack at the top of the left pane. One row per layer — reorder grip, enable toggle, select
        // button, rename-in-place name field, per-row remove — plus an add / duplicate row. Each row also carries
        // its OWN Matte box, folded underneath it (BuildMatteBox), so a matte reads as a property of the layer in
        // the stack. Selection drives which layer the Shape / Swarm / Modifiers sections below edit. Mirrors
        // PyreWindow's layer-list chrome (row + folded matte box inside one drag wrap).
        void BuildLayerList(VisualElement root)
        {
            var box = Z.BoxKeyed("Layers",
                "The paint stack — earlier (higher) layers composite BEHIND later (lower) ones. Click a layer to "
                + "edit its Shape / Swarm / Modifiers dials below; expand a row's Matte box to make it a stencil or "
                + "clip it by another layer's mask; drag the grip to reorder.",
                "pyreplus.layers");
            layerListHost = new VisualElement();
            box.Add(layerListHost);
            RebuildLayerList();
            box.Add(WrapRow(
                Z.Button("+ Add layer", "Append a new default layer to the FRONT of the stack (undoable).", AddLayer),
                Z.Button("Duplicate", "Duplicate the selected layer just in front of itself (undoable).", DuplicateSelectedLayer)));
            root.Add(box);
        }

        void RebuildLayerList()
        {
            if (layerListHost == null || spec == null || spec.layers == null) return;
            layerListHost.Clear();
            for (int li = 0; li < spec.layers.Count; li++)
                layerListHost.Add(BuildLayerRow(layerListHost, li));
        }

        VisualElement BuildLayerRow(VisualElement listHost, int li)
        {
            var layer = spec.layers[li];
            bool sel = li == layerSel;

            // The row PLUS its own Matte box, folded underneath — so a matte reads as a property of this layer in
            // the stack, which is where it acts (mirrors Pyre1). The WRAP (not the inner row) is the drag/reorder
            // unit, or reordering would leave the folded matte box behind.
            var wrap = new VisualElement();

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (sel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this layer in the paint stack.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, wrap, listHost, (from, to) =>
            {
                Dirty(() =>
                {
                    var l = spec.layers[from];
                    spec.layers.RemoveAt(from);
                    spec.layers.Insert(to, l);
                });
                layerSel = to;
                RebuildAllForSelection();
            });
            row.Add(grip);

            row.Add(Z.Toggle("", "Show or hide this layer in the render.", layer.enabled,
                v => { Dirty(() => layer.enabled = v); RebuildLayerList(); }));

            row.Add(Z.Button(sel ? "●" : "○", "Select this layer to edit it below.", () => SelectLayer(li)).W(24f));

            var name = Z.TextInput(layer.name ?? "", "This layer's name — rename it right here.", v =>
            {
                Undo.RecordObject(spec, "Rename layer");
                layer.name = v;
                EditorUtility.SetDirty(spec);
            }, 0f);
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.minWidth = 50f;
            name.AddToClassList("zui-audit-allow-stretch");   // the rulebook's name-field stretch exception
            name.RegisterCallback<PointerDownEvent>(_ => { if (layerSel != li) SelectLayer(li); });
            row.Add(name);

            if (!layer.enabled) row.Add(Z.Text("off", ZuiText.Small, "This layer is currently hidden."));
            // #57 — the matte indicators are gated on matteEnabled to mirror the renderer: a disabled matte acts as a
            // plain Draw layer (its role/clip/height are preserved but inert), so the row shows NO matte indicator.
            else if (layer.matteEnabled && layer.matteRole == MatteRole.WriteMatte)
                row.Add(Z.Text($"→{Mathf.Clamp(layer.matteChannel, 0, 3)}", ZuiText.Small,
                    "A matte layer — invisible; writes its coverage into the shown mask channel."));
            else if (layer.matteEnabled && layer.matteRole == MatteRole.LumaMatte)
                row.Add(Z.Text("◧", ZuiText.Small,
                    "A luma-matte layer — invisible; masks the layers above it by its luminance × alpha."));
            else if (layer.matteEnabled && layer.heightFromChannel >= 0)
                row.Add(Z.Text($"▲{Mathf.Clamp(layer.heightFromChannel, 0, 3)}", ZuiText.Small,
                    "A heightmap layer (slice 4b) — renders the fused matte channel as a relief-lit surface instead of drawing its shape."));

            // Matte toggle ICON (mirrors Pyre1's per-row Matte toggle) — sits just before the delete button. A
            // glyph-swap button like the select button above (▦ mask-pattern = on, □ hollow = off), so it shows its
            // own state and matches the other row icons' style/size (.W(24f)). Both glyphs are Geometric Shapes,
            // the same block as the ●/○ select glyphs that already render. Turning it OFF also resets the layer to
            // inert (Draw + no clip) so a previously-configured matte stops acting; the box appears/disappears via
            // RebuildLayerList (same as a role change).
            row.Add(Z.Button(layer.matteEnabled ? "▦" : "□",
                "Toggle matte for this layer — write a mask channel, or clip this layer by one.", () =>
            {
                Dirty(() =>
                {
                    // #57 — just flip the master gate. The renderer honours matteEnabled, so a disabled layer acts as a
                    // plain Draw layer WITHOUT wiping matteRole / clipByChannel / heightFromChannel — the full matte
                    // setup is PRESERVED and comes back on re-enable. The Matte box still hides while disabled (below),
                    // and the row's matte indicator is gated on matteEnabled, so the row reads as a plain Draw layer.
                    layer.matteEnabled = !layer.matteEnabled;
                });
                RebuildLayerList();
            }).W(24f));

            row.Add(Z.Button("✕", "Delete this layer (undoable).", () =>
            {
                if (spec.layers.Count <= 1) { ShowNotification(new GUIContent("A Pyre Plus asset needs at least one layer.")); return; }
                Dirty(() => spec.layers.RemoveAt(li));
                layerSel = Mathf.Clamp(layerSel, 0, spec.layers.Count - 1);
                RebuildAllForSelection();
            }).W(22f));

            wrap.Add(row);
            // The Matte box folds out ONLY when this layer has matte enabled (mirrors Pyre1, where the box shows
            // only for a matte layer). The wrap still owns row + box as one drag/reorder unit.
            if (layer.matteEnabled) wrap.Add(BuildMatteBox(layer, li));
            return wrap;
        }

        void AddLayer()
        {
            if (spec == null) return;
            Dirty(() =>
            {
                spec.layers.Add(new PyrePlusLayer { name = $"Layer {spec.layers.Count + 1}", matteEnabled = false });
                layerSel = spec.layers.Count - 1;
            });
            RebuildAllForSelection();
        }

        void DuplicateSelectedLayer()
        {
            var srcLayer = SelLayer;
            if (srcLayer == null) return;
            Dirty(() =>
            {
                var copy = srcLayer.Clone();
                copy.name = (srcLayer.name ?? "Layer") + " copy";
                int at = Mathf.Clamp(layerSel + 1, 0, spec.layers.Count);
                spec.layers.Insert(at, copy);
                layerSel = at;
            });
            RebuildAllForSelection();
        }

        void SelectLayer(int li)
        {
            layerSel = li;
            RebuildAllForSelection();
        }

        // Re-point every selection-bound section at the (possibly new) SelLayer and refresh the list highlight.
        // RebuildLayerList also rebuilds every row's folded Matte box, so no separate matte rebuild is needed.
        void RebuildAllForSelection()
        {
            RebuildLayerList();
            RebuildShape();
            RebuildSwarm();
            RebuildModifiers();
            MarkDirty();
        }

        // ── Matte (R3) — a property of each layer, folded under its row in the layer list ──────────────
        static readonly string[] MatteRoleLabels = { "Draw", "Write matte", "Luma matte" };
        static readonly string[] MatteCombineLabels = { "Max", "Add", "Subtract" };
        static readonly string[] MatteScopeLabels = { "Next layer", "All above" };
        static readonly List<string> ClipChannelChoices = new List<string> { "None", "0", "1", "2", "3" };

        // This layer's own Matte box, folded under its row (built from BuildLayerRow into the row's drag wrap, only
        // when layer.matteEnabled). Every control binds to the passed-in `layer` — THIS row's layer, never the
        // selected one — so matte is authored per-row, independent of which layer is selected. The stateKey is
        // per-layer-index so two mattes never share fold state; it opens by default when it appears (the user just
        // enabled it, so they want to configure it). Mirrors Pyre1's BuildMatteBox: a matte belongs to the layer in
        // the stack, shown only when that layer is a matte, not to a separate section.
        VisualElement BuildMatteBox(PyrePlusLayer layer, int li)
        {
            var box = Z.BoxKeyed("Matte",
                "Turns this layer into a stencil. A Write-matte layer is INVISIBLE — instead of drawing, it writes "
                + "its coverage into one of four numbered mask channels. A Draw layer can then Clip its own opacity "
                + "by any channel a layer BELOW it wrote, so an earlier shape can mask or cut into a later one.",
                $"pyreplus.matte:{li}");
            box.style.marginLeft = 16f;   // indent under its row, so the list still reads as a list

            box.Add(Z.Field("Role",
                "Draw = composite this layer onto the frame normally. Write matte = don't draw it; write its "
                + "coverage into a mask channel for the Draw layers above to clip by. Luma matte = don't draw it; "
                + "use its luminance × alpha as a mask that alters the layers above (opacity, brightness, hue…).",
                Z.Segmented((int)layer.matteRole, MatteRoleLabels,
                    "Draw composites this layer. Write matte makes it invisible and stencils a channel instead. "
                    + "Luma matte makes it invisible and masks the layers above by its luminance.",
                    // Role swaps the box's controls (Channel/Combine ↔ Clip/Invert) AND the row's matte indicator,
                    // so rebuild the whole list; the per-index fold key keeps this box's open/closed state across it.
                    v => { Dirty(() => layer.matteRole = (MatteRole)v); RebuildLayerList(); })));

            if (layer.matteRole == MatteRole.WriteMatte)
            {
                box.Add(WrapRow(
                    Z.MicroSlider("Channel", layer.matteChannel, 0f, 3f,
                        "Which of the four mask channels (0–3) this layer's coverage writes into. A Draw layer above "
                        + "picks the same number in its Clip-by to be stencilled by this layer.",
                        v => Dirty(() => layer.matteChannel = Mathf.Clamp(Mathf.RoundToInt(v), 0, 3)), 150f,
                        showValue: true, decimals: 0),
                    Z.Field("Combine",
                        "How this layer's coverage merges with anything an earlier matte layer already wrote into "
                        + "the same channel. Max = union; Add = accumulate; Subtract = carve out.",
                        Z.Segmented((int)layer.matteCombine, MatteCombineLabels,
                            "Max = union of masks (default). Add = accumulate & clamp. Subtract = carve one mask out of another.",
                            v => Dirty(() => layer.matteCombine = (MatteCombine)v)))));
                // Slice 4b: write LUMINANCE × alpha instead of coverage-alpha, so several such layers on one channel
                // (Combine = Max) fuse into a single HEIGHTMAP a Draw layer can render as a relief-lit surface.
                box.Add(Z.Toggle("Write luminance (heightmap)",
                    "Deposit this layer's LUMINANCE × alpha into the channel instead of flat coverage-alpha. Several "
                    + "write-matte layers on one channel (Combine = Max) then fuse into a single HEIGHTMAP — set a Draw "
                    + "layer above to Height-from that channel to render it as one relief-lit surface.",
                    layer.matteWriteLuma, v => Dirty(() => layer.matteWriteLuma = v)));
            }
            else if (layer.matteRole == MatteRole.LumaMatte)
            {
                // Defensive: a hand-built layer might predate these fields. Fresh/duplicated layers always have them
                // (field initialisers + Clone deep-copy), so this only guards the rare null path.
                layer.matteStrength ??= new ZUIValue(1f);
                layer.matteBlurAmount ??= new ZUIValue(3f);
                layer.matteDisplaceAmount ??= new ZUIValue(4f);
                layer.matteHueDegrees ??= new ZUIValue(60f);

                // Channel flags — a multi-toggle. Any combination acts at once, in the fixed order
                // Displace → Blur → Saturation → Hue → Brightness → Alpha. Toggling a channel rebuilds the list so
                // the matching amount field (Blur/Displace/Hue) appears/disappears.
                box.Add(Z.Field("Channels",
                    "Which effects this luma matte imposes on the layers above — any combination acts at once, in a "
                    + "fixed order (Displace → Blur → Saturation → Hue → Brightness → Alpha). The mask = the matte "
                    + "layer's LUMINANCE × its own alpha.",
                    WrapRow(
                        ChannelFlag(layer, MatteChannel.Alpha, "Alpha", "Multiply the covered layers' opacity by the mask (classic luma matte)."),
                        ChannelFlag(layer, MatteChannel.Brightness, "Bright", "Darken toward black where the mask is low — a shadow/light pass."),
                        ChannelFlag(layer, MatteChannel.Saturation, "Sat", "Drain toward grey where the mask is low — ash, smoke, heat-death."),
                        ChannelFlag(layer, MatteChannel.Hue, "Hue", "Rotate hue by the mask — heat shimmer, chemical burn, cold spots."),
                        ChannelFlag(layer, MatteChannel.Blur, "Blur", "Soften where the mask is high, sharp where it's low."),
                        ChannelFlag(layer, MatteChannel.Displace, "Displace", "Push pixels along the mask's own slope — refraction, heat haze."))));

                box.Add(WrapRow(
                    Z.Field("Scope",
                        "How far up the stack this matte reaches. Next layer = clip only the next drawn layer (a "
                        + "clipping mask). All above = affect every layer above it, until another luma matte replaces it.",
                        Z.MiniRadio((int)layer.matteScope, MatteScopeLabels,
                            "Next layer = the next drawn layer only. All above = every layer above until another luma matte replaces it.",
                            v => Dirty(() => layer.matteScope = (MatteScope)v), wrap: true)),
                    Z.Toggle("Invert",
                        "Invert the mask (1 − mask) — impose the effect where the matte is DARK/transparent instead of bright.",
                        layer.matteInvert, v => Dirty(() => layer.matteInvert = v))));

                box.Add(Z.Toggle("Strength from edge (1−α)",
                    "Drive the effect strength by the COVERED layer's OWN alpha as (1 − α): fully OPAQUE pixels get NO "
                    + "effect, soft / thin / anti-aliased EDGE pixels get the FULL effect. Concentrates the matte on "
                    + "feathered rims, wisps and dissolve fronts and leaves solid interiors untouched. The Alpha "
                    + "channel is excluded from this (clipping opacity where opacity is already lowest is degenerate) — "
                    + "pair it with Bright / Sat / Hue / Blur / Displace.",
                    layer.matteAlphaSource, v => Dirty(() => layer.matteAlphaSource = v)));

                box.Add(Val("Strength",
                    "Master strength of the whole matte over the matte layer's OWN life — scales the mask 0 (no "
                    + "effect) → 1 (full). Animate it to fade the matte in/out.",
                    layer.matteStrength, 0f, 1f));
                if ((layer.matteFlags & MatteChannel.Blur) != 0)
                    box.Add(Val("Blur amount",
                        "Max blur radius (px) where the mask is full — the per-pixel radius is amount × mask.",
                        layer.matteBlurAmount, 0f, 12f));
                if ((layer.matteFlags & MatteChannel.Displace) != 0)
                    box.Add(Val("Displace amount",
                        "How far (px) mask EDGES push the pixels behind them along the mask's slope.",
                        layer.matteDisplaceAmount, 0f, 16f));
                if ((layer.matteFlags & MatteChannel.Hue) != 0)
                    box.Add(Val("Hue degrees",
                        "Hue rotation (degrees) applied where the mask is full.",
                        layer.matteHueDegrees, -180f, 180f));
            }
            else
            {
                box.Add(WrapRow(
                    Z.Field("Clip by",
                        "Multiply THIS layer's opacity by a mask channel an EARLIER (lower) layer wrote — None = no "
                        + "clipping. The layer then only shows where that channel is bright.",
                        Z.MiniRadio(Mathf.Clamp(layer.clipByChannel + 1, 0, 4), ClipChannelChoices.ToArray(),
                            "None, or channel 0–3 written by a Write-matte layer below this one. This layer is clipped to it.",
                            v => Dirty(() => layer.clipByChannel = v - 1), wrap: true)),
                    Z.Toggle("Invert",
                        "Clip by (1 − channel) instead — show where the mask is DARK, hide where it's bright.",
                        layer.clipInvert, v => Dirty(() => layer.clipInvert = v))));

                // Slice 4b — heightmap consumer: render a fused matte channel as a relief-lit surface INSTEAD of
                // drawing the shape. None = off (draw normally). Picking a channel reveals the Relief + Light-angle
                // knobs; the shape simply isn't drawn while a channel is selected. Rebuild the list on change so the
                // knobs appear/disappear (like the LumaMatte channel flags do).
                box.Add(WrapRow(
                    Z.Field("Height from",
                        "Render a fused matte channel as a relief-lit HEIGHTMAP through this layer's Fill gradient "
                        + "INSTEAD of drawing its shape. None = draw the shape normally. 0–3 = the channel the "
                        + "write-matte LUMINANCE layers below fused into (bright/lit where their luminance piles up).",
                        Z.MiniRadio(Mathf.Clamp(layer.heightFromChannel + 1, 0, 4), ClipChannelChoices.ToArray(),
                            "None, or channel 0–3 fused by the write-matte layers below. This layer becomes that heightmap surface instead of its shape.",
                            v => { Dirty(() => layer.heightFromChannel = v - 1); RebuildLayerList(); }, wrap: true))));
                if (layer.heightFromChannel >= 0)
                    box.Add(WrapRow(
                        Z.MicroSlider("Relief", layer.heightRelief, 0f, 8f,
                            "How steeply the fused field's slope carves the surface into lit highlights and shadow. "
                            + "0 = a flat gradient-mapped field with no relief lighting.",
                            v => Dirty(() => layer.heightRelief = v), 150f, showValue: true),
                        Z.MicroSlider("Light angle", layer.heightLightAngle, 0f, 360f,
                            "Direction (degrees, screen plane) the relief light falls across the fused heightmap surface.",
                            v => Dirty(() => layer.heightLightAngle = v), 150f, showValue: true)));
            }
            return box;
        }

        // One channel-flag toggle for the LumaMatte box: shows + sets a single MatteChannel bit on the layer, then
        // rebuilds the list so the matching amount field (Blur/Displace/Hue) appears/disappears. Binds to the
        // passed-in `layer` (this row's), like every other matte control (never the selected one).
        VisualElement ChannelFlag(PyrePlusLayer layer, MatteChannel bit, string label, string tip)
            => Z.Toggle(label, tip, (layer.matteFlags & bit) != 0, on =>
            {
                Dirty(() => layer.matteFlags = on ? (layer.matteFlags | bit) : (layer.matteFlags & ~bit));
                RebuildLayerList();
            });

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

        static readonly List<string> ShapeFormChoices = new List<string> { "Disc", "Gem", "Crescent", "Sparkle", "Sprite", "Box", "Pyramid", "Can", "Orb", "Ring", "Text", "Streak", "Star", "Fire", "Fireball" };
        // Fireball's wedge mode — Mirror (alternate wedges reflected, a seam) / Repeat (each wedge the same, rotated).
        // Index 0 = Mirror (fireballMirror true), 1 = Repeat (false).
        static readonly string[] FireballMirrorChoices = { "Mirror", "Repeat" };
        // Fire's two arm modes — Mirror (symmetric) / Vary (each arm its own seed). Order matches FireArmMode.
        static readonly string[] FireArmModeChoices = { "Mirror", "Vary" };
        // Coalesce render-mode selector. Off / Fuse (slice 1, MetaBlob) / Ramp (slice 2, HeightBalls). Order matches
        // the LayerCoalesce enum (Off=0, Fuse=1, Ramp=2), so the MiniRadio index casts straight to the enum.
        static readonly string[] CoalesceChoices = { "Off", "Fuse", "Ramp" };
        static readonly List<string> TextFillModeChoices = new List<string> { "Per-char gradient", "Per-char step", "Text gradient" };

        void RebuildShape()
        {
            var s = SelLayer;   // every Shape control now edits the SELECTED layer's fields
            if (s == null || shapeBody == null) { shapeBody?.Clear(); return; }
            shapeBody.Clear();

            s.alpha ??= new ZUIValue(1f);
            s.shapeFill ??= new ZuiFill();   // defensive; the real OverLife-fire default comes from the spec factory

            // FORM selector — a wrapped MiniRadio (was a Dropdown/context-menu): 13 short labels read as radio
            // buttons folding across several lines. ZuiSegmented would overflow (flex-shrink:0, never wraps), but
            // MiniRadio wrap:true reflows to the column width — and the column can now widen (ColumnFlow). Rebuild
            // so the form-specific rows swap in/out.
            shapeBody.Add(Z.Field("Form", "The particle's rendered form.",
                Z.MiniRadio((int)s.shapeForm, ShapeFormChoices.ToArray(),
                    "Disc = a flat soft disc. Gem = a true-3D lit crystal. Crescent = a disc with an offset bite. "
                    + "Sparkle = twinkling lit cells. Sprite = a stamped image. Box / Pyramid / Can = true-3D lit "
                    + "solids sharing the Gem's facet lighting (tilt, light, edge lines, glows). Orb = a lit "
                    + "sphere; Ring = a flat tilted annulus (a Saturn ring) — both reuse that same lighting "
                    + "analytically. Text = a string as extruded SDF letters (one particle per character; the "
                    + "particle count follows the string). Streak = a root-anchored comet-tail capsule that grows "
                    + "forward along its orientation (great with the Swarm's Orient). Star = a filled star polygon "
                    + "(arms, reach, base width, swirl). Fire = a stateful flame SIMULATION with built-in emitters "
                    + "(no swarm; every rate is an envelope over the layer's life). Fireball = a stateful cellular "
                    + "explosion — heat blooms outward from one centre, folded into kaleidoscope arms (single-source, "
                    + "no swarm).",
                    // Rebuild BOTH sections: Text swaps in its own Shape box AND hides the Swarm's Count field.
                    // FIX 1: switching TO a 3D solid gives a fresh/pristine OverLife-fire fill a STEADY Solid
                    // material (inside the SAME Dirty block, so one Undo reverts both the form and the fill together
                    // — never leaving an intermediate solid+OverLife state for an undo to land on and re-convert).
                    v => { Dirty(() => { s.shapeForm = (ShapeForm)v; SteadyDefaultFillForSolid(s); }); RebuildShape(); RebuildSwarm(); }, wrap: true)));

            // Lifetime window (#55) — the frame range this LAYER is alive, ported 1:1 from Pyre1's per-layer
            // "Life (frames)" row. A bounded int min/max pair over [0, frameCount-1] ⇒ ONE Z.MinMax(isInt) range
            // (never two separate fields), per the layout rules. The layer's life sweeps 0→1 across [start, end];
            // outside the window the layer draws nothing. endFrame's -1 sentinel ("the last frame") DISPLAYS as
            // frameCount-1; dragging the high handle back to the far right restores -1 so a full-range window keeps
            // auto-tracking the frame count (and stays byte-identical), while any inset stores a concrete end.
            // fcMax is captured at rebuild time — the render-time clamp keeps it correct if frameCount changes since.
            int fcMax = Mathf.Max(0, spec.frameCount - 1);
            int winLo = Mathf.Clamp(s.startFrame, 0, fcMax);
            int winHi = s.endFrame < 0 ? fcMax : Mathf.Clamp(s.endFrame, 0, fcMax);
            const string framesTip = "The frame window this layer is alive. Its life is lerped 0→1 across [start, end]; before Start / after End the layer contributes nothing (blank). Full range = the whole timeline.";
            shapeBody.Add(Z.Field("Life (frames)", framesTip,
                Z.MinMax(winLo, winHi, 0f, fcMax, framesTip,
                    (lo, hi) => Dirty(() =>
                    {
                        int a = Mathf.Clamp(Mathf.RoundToInt(lo), 0, fcMax);
                        int b = Mathf.Clamp(Mathf.RoundToInt(hi), a, fcMax);
                        s.startFrame = a;
                        s.endFrame = b >= fcMax ? -1 : b;   // far-right restores the "last frame" sentinel (auto-tracks frameCount)
                    }), 130f, isInt: true)));

            // Shared rows. For the Gem, Colour is its material tint and Size is its girdle radius; for the Sprite,
            // Colour is the optional tint. TEXT takes its colour from its own Fill / Border gradients instead, so
            // the Colour row is hidden for it (the Text box's Fill row tooltip says so). Size is the scale driver
            // for every form — Text reads it as the character HEIGHT.
            // Colour → a ZuiFill: Solid, Over life (a gradient, the default), or a spatial fill (linear/radial/
            // noise); every mode is alpha-capable, switched via the ⋯ menu. Hidden for Text (its own per-char fill
            // rules its colour). Z.Fill draws its own "Fill" label + the ⋯, so it isn't wrapped in a Z.Field; its
            // tooltip is composed for the CURRENT form.
            if (s.shapeForm != ShapeForm.Text)
                shapeBody.Add(FillRow("Fill", FillTooltip(s.shapeForm), s.shapeFill,
                    new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));
            shapeBody.Add(Val("Alpha", "Opacity over the particle's own life (multiplies the final output alpha).", s.alpha, 0f, 1f));
            // Size drives every form's scale — except the Streak, which has its OWN Length/Width envelopes, so the
            // shared Size row is hidden for it (a dead control would be clutter per the layout rules). Its max is
            // the CANVAS size (not a fixed 32) so a big canvas can hold a big particle — the Canvas Size slider
            // rebuilds the pane to refresh this.
            // Streak has its own Length/Width; Fire and Fireball are whole-layer sims bounded by their Reach radius,
            // not a particle radius — so all three hide the shared Size row (a dead control is clutter per the rules).
            if (s.shapeForm != ShapeForm.Streak && s.shapeForm != ShapeForm.Fire && s.shapeForm != ShapeForm.Fireball)
                shapeBody.Add(Val("Size (px)", SizeTooltip(s.shapeForm), s.size, 0f, spec.canvasSize));

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
                case ShapeForm.Streak:
                    BuildStreakRows(s);
                    break;
                case ShapeForm.Star:
                    BuildStarRows(s);
                    break;
                case ShapeForm.Fire:
                    BuildFireBox(s);
                    break;
                case ShapeForm.Fireball:
                    BuildFireballBox(s);
                    break;
            }

            // Fire and Fireball are whole-layer simulations with no per-particle Spin/Travel — skip both the Spin
            // control and the Advanced section (they would be dead). Their Fill (the ramp) and Alpha (overall
            // opacity) rows above apply.
            if (s.shapeForm == ShapeForm.Fire || s.shapeForm == ShapeForm.Fireball) return;

            // Spin — the particle's own-life in-place rotation. A NORMAL Shape control now (no longer buried behind
            // the Advanced gate, Bug 3 2026-07-26): fill/spin must be discoverable. For the 3D solid forms Spin IS
            // the Solid box's "Turn °" (the same particleSpin field, shown once), so it stays hidden here for them;
            // 2D forms and Text show it. Its tooltip is composed for the CURRENT form.
            s.particleSpin ??= new ZUIValue(0f);
            if (!IsSolidForm(s.shapeForm))
                shapeBody.Add(Val("Spin °", SpinTooltip(s.shapeForm), s.particleSpin, -720f, 720f));

            // Advanced gate: the particle's OWN travel path after birth (opt-in — rarer than spin). Toggling
            // rebuilds just this section.
            shapeBody.Add(Z.Toggle("Advanced",
                "A per-particle travel path on the particle's own life clock, added to its spawn position.",
                s.shapeAdvanced, v => { Dirty(() => s.shapeAdvanced = v); RebuildShape(); }));
            if (!s.shapeAdvanced) return;

            float half = Mathf.Max(1f, spec.canvasSize * 0.5f);
            s.particlePathX ??= new ZUIValue(0f);
            s.particlePathY ??= new ZUIValue(0f);

            shapeBody.Add(Val2D("Travel",
                "The particle's own path after birth: canvas-pixel offsets ADDED to its spawn position, sampled on "
                + "the particle's OWN life (0 = birth, 1 = death). Author it as a Curve to make the particle "
                + "drift/arc as it lives; Static 0 = no travel.",
                s.particlePathX, s.particlePathY,
                new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)));
        }

        // The 3D-solid controls — one framed box inside the Shape body, shown for Gem / Box / Pyramid / Can (all
        // true-3D convex facet solids sharing one renderer). Colour, Alpha and Size stay above (shared); this box
        // adds the per-form geometry then the SHARED tilt / lighting / lines / glows every 3D solid uses. Titled
        // "Solid" (not "Gem") since it now serves four forms. All plain sliders are label-inside MicroSliders; the
        // two glows are animatable ZUIValues (Val), each packed with its own colour; Specular packs with its colour.
        void BuildSolidBox(PyrePlusLayer s)
        {
            s.particleSpin ??= new ZUIValue(0f);
            s.gemTilt ??= new ZUIValue(18f);
            s.gemRoll ??= new ZUIValue(0f);
            s.gemEdgeGlow ??= new ZUIValue(0.5f);     // defensive; the real steady defaults come from the spec factories
            s.gemInnerGlow ??= new ZUIValue(0.35f);

            // BoxKeyed: view presets persist under this stable key — retitling the box or rewording
            // its tooltip must never orphan saved views.
            var box = Z.BoxKeyed("Solid", SolidBoxTooltip(s.shapeForm), "pyreplus.solid");

            // ── per-form geometry ──
            if (s.shapeForm == ShapeForm.Gem)
            {
                box.Add(Z.HGroup(
                    Z.MicroSlider("Sides", s.gemSides, 3f, 8f,
                        "Girdle vertex count — 4 is the classic octahedral gem; more sides make a rounder crystal.",
                        v => Dirty(() => s.gemSides = Mathf.Clamp(Mathf.RoundToInt(v), 3, 8)), 150f, showValue: true, decimals: 0),
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

            // ── shared: rotation trio / light / lines / glows (every 3D solid) ──
            // Turn (yaw = the shared particleSpin), Tilt (gemTilt) and Roll (gemRoll) — the three rotation axes in
            // plain words, each animatable over the particle's own life. Roll is geometrically inert for the
            // symmetric Ring (Turn + Tilt already shape its ellipse), so it's hidden there. Tooltips are composed
            // for the CURRENT form.
            box.Add(Val("Turn °", TurnTooltip(s.shapeForm), s.particleSpin, -1440f, 1440f));
            box.Add(Val("Tilt °", TiltTooltip(s.shapeForm), s.gemTilt, -1440f, 1440f));
            if (s.shapeForm != ShapeForm.Ring)
                box.Add(Val("Roll °", RollTooltip(s.shapeForm), s.gemRoll, -1440f, 1440f));

            // Light / Lines / Glow rows opt into the box's ⚙ gear (the Turn/Tilt/Roll trio above stays mandatory). Each group
            // toggle flips its whole cluster at once; keys are STABLE "solid.*" strings (never a display
            // label) so a saved view survives a relabel. A saved "view" round-trips these on/off states.
            box.ToggleGroup("Light", "Light");
            box.ToggleGroup("Lines", "Lines");
            box.ToggleGroup("Glow", "Glow");

            box.Add(Z.Divider("Light",
                "The single KEY light and how the surfaces respond to it. The key light is DIRECTIONAL — aim it with "
                + "the pad; Ambient is a separate non-directional base light. The edge Lines and the Glows have their "
                + "OWN strengths and do NOT obey this light."));
            // Light DIRECTION as one 2D pad (yaw × pitch) — a plain-Vector2 Z.Pad, so dragging aims the key light in
            // one gesture instead of two separate 1D sliders. X = yaw (gemLightYaw, −180..180), Y = pitch
            // (gemLightPitch, −85..85 — widened from 0..85 so the light can come from below/behind, not just the
            // front hemisphere). Kept under the same "solid.light.angles" view key as the old angle sliders.
            const string lightDirTip = "The key light is DIRECTIONAL — drag the pad to aim it. X = yaw (which side it "
                + "comes FROM, left/right, −180..180°); Y = pitch (its height, −85..85° — negative brings it from "
                + "below/behind). Only the lit faces and the specular hotspot follow it — the Lines and Glows have "
                + "their own strengths and do NOT obey the light.";
            box.Add(box.Toggleable(
                Z.Field("Light dir", lightDirTip,
                    Z.Pad(new Vector2(s.gemLightYaw, s.gemLightPitch), new Rect(-180f, -85f, 360f, 170f), lightDirTip,
                        v => Dirty(() => { s.gemLightYaw = v.x; s.gemLightPitch = v.y; }), 56f)),
                "solid.light.angles", "Angle", "Light"));
            box.Add(box.Toggleable(WrapRow(
                Z.MicroSlider("Distance", s.gemLightDistance, 1.5f, 8f,
                    "How far the key light sits from the solid, as a multiple of its radius — the light's real 3rd "
                    + "axis (the pad sets its two angles; this sets its distance). Closer = a tighter, brighter "
                    + "hotspot; farther = flatter, more even light (the falloff tracks it, so a distant light still "
                    + "reaches the solid).",
                    v => Dirty(() => s.gemLightDistance = Mathf.Clamp(v, 1.5f, 8f)), 150f, showValue: true),
                Z.MicroSlider("Ambient", s.gemAmbient, 0f, 1f,
                    "Non-directional BASE light on every face (it doesn't come from a direction). Near zero keeps the "
                    + "solid contrasty; raise it to flatten the shading.",
                    v => Dirty(() => s.gemAmbient = v), 150f, showValue: true),
                Z.MicroSlider("Diffuse", s.gemDiffuse, 0f, 3f,
                    "The key light's DIFFUSE strength on the faces it hits (the Lambert term). 0 = only Ambient + "
                    + "Specular light the faces; 2.1 is the default look. This is the dial that was missing — with it "
                    + "at 0 and Ambient/Specular at 0 the faces finally go dark instead of staying diffuse-lit.",
                    v => Dirty(() => s.gemDiffuse = v), 150f, showValue: true),
                Z.MicroSlider("Specular", s.gemSpecular, 0f, 2f,
                    "Strength of the tight highlight (the bright hot spot) where the key light reflects — pair it with "
                    + "Spec power for the hotspot's tightness.",
                    v => Dirty(() => s.gemSpecular = v), 150f, showValue: true),
                Z.MicroSlider("Spec power", s.gemSpecPower, 2f, 128f,
                    "TIGHTNESS of the specular hotspot — higher = a smaller, sharper glint; lower spreads it into a "
                    + "broad sheen. (The old fixed 48 was so tight the highlight rarely showed — lower it to see it.)",
                    v => Dirty(() => s.gemSpecPower = v), 150f, showValue: true),
                SlotFill("Spec fill", "Fill for the specular highlight — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemSpecularFill)),
                "solid.light.response", "Response", "Light"));

            box.Add(Z.Divider("Lines", "The hard facet edge lines that catch the light."));
            box.Add(box.Toggleable(WrapRow(
                Z.MicroSlider("Line width", s.gemLineWidth, 0f, 3f,
                    "Width of the hard facet edge lines in pixels (0 = no lines). The lines catch the key light.",
                    v => Dirty(() => s.gemLineWidth = v), 150f, showValue: true),
                SlotFill("Line fill", "Fill for the facet edge lines — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemLineFill)),
                "solid.lines", "Width & fill", "Lines"));

            box.Add(Z.Divider("Glow", "A rim halo and an interior glow — steady by default (author a Curve to pulse), each with its own fill."));
            box.Add(box.Toggleable(WrapRow(
                Val("Edge glow",
                    "Strength (0-1) of the halo around the edge lines, over the particle's OWN life; it spills "
                    + "OUTSIDE the solid's silhouette. Static = a steady glow (the default); author a Curve to make "
                    + "it pulse over the particle's life.",
                    s.gemEdgeGlow, 0f, 1f),
                SlotFill("Edge fill", "Fill for the edge-line halo glow — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemEdgeGlowFill)),
                "solid.glow.edge", "Edge", "Glow"));
            box.Add(box.Toggleable(WrapRow(
                Val("Inner glow",
                    "Strength (0-1) of the emissive glow rising from the facet interiors, over the particle's OWN "
                    + "life; interior only. Static = a steady glow (the default); author a Curve to make it pulse "
                    + "over the particle's life.",
                    s.gemInnerGlow, 0f, 1f),
                SlotFill("Inner fill", "Fill for the facet inner glow — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemInnerGlowFill)),
                "solid.glow.inner", "Inner", "Glow"));

            shapeBody.Add(box);
        }

        // The Edge-softness slider — its own MicroSlider caption is the "Edge" label, so it isn't wrapped in a
        // Z.Field (that would print "Edge" twice). Shared by Disc (its single rim) and Crescent (both rims), each
        // passing its own tooltip.
        ZuiMicroSlider EdgeRow(PyrePlusLayer s, string tooltip) =>
            Z.MicroSlider("Edge", s.edgeSoftness, 0f, 1f, tooltip,
                v => Dirty(() => s.edgeSoftness = v), 150f, showValue: true);

        // Crescent form rows — the shared Edge row (drives BOTH rims), then the bite: size + facing packed, and
        // the push-out offset.
        void BuildCrescentRows(PyrePlusLayer s)
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
        void BuildSparkleRows(PyrePlusLayer s)
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
        void BuildSpriteRows(PyrePlusLayer s)
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

        // Streak form rows — the streak's own Length (its scale driver, replacing the hidden shared Size), then
        // Width + Anchor + Tip packed, then the shared Edge row (which feathers the streak's two long SIDES). The
        // particle is an anchor point ON the streak (Anchor 0 = tail, 0.5 = centred, 1 = tip); default orientation
        // up, steered by the Swarm's Orient (and its own Advanced Spin).
        void BuildStreakRows(PyrePlusLayer s)
        {
            s.streakLength ??= new ZUIValue(20f);   // defensive; the real shoot-out arc comes from the spec factory
            s.streakWidth ??= new ZUIValue(3f);

            shapeBody.Add(Val("Length (px)",
                "The streak's length forward (from its root) in pixels, over the particle's own life. Author a "
                + "Curve to make it shoot out then ease shorter — the default comet-tail arc.",
                s.streakLength, 0f, spec.canvasSize));   // max = the canvas, so a big canvas gets a long streak

            shapeBody.Add(WrapRow(
                Val("Width (px)", "The streak's thickness across, in pixels, over the particle's own life.",
                    s.streakWidth, 0f, spec.canvasSize / 4f),   // width max = a quarter-canvas (keeps the old 64→16 feel)
                Z.MicroSlider("Anchor", s.streakAnchor, 0f, 1f,
                    "Where the particle sits ALONG the streak, from tail to tip: 0 = at the TAIL (the streak grows "
                    + "forward), 0.5 = CENTRED (Length grows both ways, so it never slides off the particle), 1 = at "
                    + "the TIP (grows backward).",
                    v => Dirty(() => s.streakAnchor = v), 150f, showValue: true),
                Z.MicroSlider("Tip", s.streakSoftTip, 0f, 1f,
                    "End softness — how much of EACH end (forward and back) feathers out to transparent (0 = hard "
                    + "flat ends; 1 = the streak fades from its centre to both tips).",
                    v => Dirty(() => s.streakSoftTip = v), 150f, showValue: true)));

            shapeBody.Add(EdgeRow(s,
                "Soft sides (1) vs hard pixel edges (0) — feathers the streak's two long SIDES (both ends are "
                + "feathered by the Tip control above)."));

            // Bars taper (slice 3): make the Swarm's per-index Scale drive LENGTH only, leaving width uniform — a row
            // of equal-width bars of graduated length, Pyre's barTaper flame silhouette. Only meaningful with a Swarm
            // whose Scale-by-index is a Curve/MinMax (Advanced Swarm); a no-op at the default (Scale-by-index = 1).
            shapeBody.Add(Z.Toggle("Taper length only",
                "With a Swarm, make the per-index Scale change the streak's LENGTH only, not its width — equal-width "
                + "bars of graduated length (a flame/asterisk silhouette). Off = the index Scale changes both length "
                + "and width together. Set the Swarm's Scale-by-index (Advanced) to a Curve or Min/Max to see it.",
                s.streakScaleLengthOnly, v => Dirty(() => s.streakScaleLengthOnly = v)));
        }

        // Star form rows — a filled star polygon. Arms (point count) packed with Skew (the arm swirl); then Length
        // (arm reach) packed with Base width (valley position); then the shared Edge row (the star's rim softness).
        // The shared Size row above stays visible — it's the tip radius the arms reach to.
        void BuildStarRows(PyrePlusLayer s)
        {
            s.starLength ??= new ZUIValue(0.62f);       // defensive; the real defaults come from the spec factories
            s.starBaseWidth ??= new ZUIValue(1f);
            s.starSkew ??= new ZUIValue(0f);

            shapeBody.Add(Z.HGroup(
                Z.MicroSlider("Arms", s.starArms, 2f, 20f,
                    "How many points the star has (2–20). 5 = the classic five-pointed star; 6 = a Star of David.",
                    v => Dirty(() => s.starArms = Mathf.Clamp(Mathf.RoundToInt(v), 2, 20)), 150f,
                    showValue: true, decimals: 0),
                Val("Skew °",
                    "Swirls the arms by rotating the inner (valley) vertices, in degrees, over the particle's own "
                    + "life — 0 = straight symmetric arms, ± twists them into a pinwheel. (Clamped so a valley "
                    + "never crosses a tip.)",
                    s.starSkew, -60f, 60f)));

            shapeBody.Add(Z.HGroup(
                Val("Length",
                    "How far the arm tips reach out, 0..1, over the particle's own life — the inner (valley) radius "
                    + "is R·(1−length), so higher = longer, sharper arms (0.62 ≈ the classical pentagram).",
                    s.starLength, 0f, 1f),
                Val("Base width",
                    "Angular width of each arm's base, 0.1..1, over the particle's own life — 1 = the classical "
                    + "midpoint valleys; smaller pulls the valleys toward the tips for thinner arm bases and wider "
                    + "notches between them.",
                    s.starBaseWidth, 0.1f, 1f)));

            shapeBody.Add(EdgeRow(s,
                "Soft rim (1) vs a hard pixel edge (0) — feathers the star's whole outline inward along each ray."));
        }

        // Fire form box — the stateful flame SIMULATION (slice 6a). Every rate is an envelope over the LAYER's life
        // (Fire has no particles — the whole layer IS the sim), grouped Emitter → Heat & fuel → Motion → Flame shape →
        // Confinement → Output. Its colour ramp is the shared Shape Fill above (smoke→fire) and its overall opacity is
        // the shared Shape Alpha; `size` and the Swarm don't apply (both hidden/noted). Reuses Pyre's own FireSim via
        // the renderer's replay harness — the dials here map 1:1 onto Pyre's Fire fields.
        void BuildFireBox(PyrePlusLayer s)
        {
            // Defensive nulls (the real defaults come from the spec factories).
            s.fireIntensity ??= new ZUIValue(1f);
            s.fireDirection ??= new ZUIValue(90f);
            s.fireEmitterWidth ??= new ZUIValue(9f);
            s.fireEmitterInset ??= new ZUIValue(0f);
            s.fireHeat ??= new ZUIValue(0.95f);
            s.fireFuel ??= new ZUIValue(0.75f);
            s.firePulse ??= new ZUIValue(0.18f);
            s.fireFlow ??= new ZUIValue(1f);
            s.fireBuoyancy ??= new ZUIValue(4f);
            s.fireCurl ??= new ZUIValue(1.5f);
            s.fireCurlScale ??= new ZUIValue(7f);
            s.fireFlicker ??= new ZUIValue(0.6f);
            s.fireStretch ??= new ZUIValue(3f);
            s.firePinch ??= new ZUIValue(0.6f);
            s.fireBreakup ??= new ZUIValue(0.4f);
            s.fireDissipation ??= new ZUIValue(0.35f);
            s.fireBurn ??= new ZUIValue(1.5f);
            s.fireReach ??= new ZUIValue(0.8f);
            s.fireEdgeCooling ??= new ZUIValue(0.9f);

            var box = Z.BoxKeyed("Fire",
                "A stateful flame SIMULATION: heat is carried by a velocity field, so it's reached by REPLAYING the sim "
                + "from frame 0 (scrubbing and baking stay exact). Every rate is an envelope over the layer's life, so "
                + "you author the SHAPE of the burn — ignite, roar, die back — not a speed. Built-in emitters (arms "
                + "around the centre); the Swarm doesn't apply unless 'Swarm emitters' is on. Colour is the Shape "
                + "Fill above (smoke→fire ramp); overall opacity is the Shape Alpha.", "pyreplus.fire");

            // Swarm emitters (slice 8) — source the emitters from the Swarm instead of the built-in arms. Only acts
            // when the Swarm is enabled; rebuild the Swarm section on change so it un-gates (or re-gates) accordingly.
            box.Add(Z.Toggle("Swarm emitters",
                "Source the flame's emitters from the SWARM instead of the built-in arms: each alive swarm particle "
                + "becomes ONE heat/fuel injection at its own position (radius/heat/fuel = this box's Emitter width / "
                + "Heat / Fuel envelopes at that particle's OWN life), all advecting and merging into ONE shared fire "
                + "field. Only takes effect with the Swarm ENABLED (turn it on in the Swarm section below) — with the "
                + "Swarm off, the built-in Arms emitters are used. Arms / Direction still shape the fluid field's "
                + "buoyancy and confinement.",
                s.fireSwarmEmitters,
                v => { Dirty(() => s.fireSwarmEmitters = v); RebuildSwarm(); }));

            // Progress — the master burn envelope (scales the injected heat + fuel).
            box.Add(Val("Progress",
                "The burn's PROGRESS over the layer's life as ONE envelope: 0 = the emitter is off, 1 = full. This is "
                + "how the fire ignites, holds and dies — shape this curve instead of setting a speed. It scales the "
                + "injected heat and fuel, so the flame physically grows and shrinks with it.",
                s.fireIntensity, 0f, 1f));

            // ── Emitter ──
            box.Add(Z.HGroup(
                Z.MicroSlider("Arms", s.fireArms, 1f, 8f,
                    "How many flame arms radiate from the centre. 1 = a single directional flame; Pinch opens the "
                    + "cold gaps between arms, so a 3-arm fire is a 3-point star, not a filled triangle.",
                    v => Dirty(() => s.fireArms = Mathf.Clamp(Mathf.RoundToInt(v), 1, 8)), 150f, showValue: true, decimals: 0),
                Z.Field("Mode",
                    "Mirror = every arm emits identically (kaleidoscope symmetry). Vary = each arm gets its own seed, "
                    + "so the flames genuinely differ while sharing these dials.",
                    Z.Segmented((int)s.fireArmMode, FireArmModeChoices,
                        "Mirror = arms identical; Vary = each arm its own seed.",
                        v => Dirty(() => s.fireArmMode = (Laubrary.Pyre.FireArmMode)v)))));
            box.Add(Val("Direction °",
                "Which way arm 0 points, in degrees — 90 = up. (Arms are spaced evenly around the circle from here.)",
                s.fireDirection, 0f, 360f));
            box.Add(Z.HGroup(
                Val("Emitter width (px)", "Width of each arm's emitter, in pixels — the base of the flame.",
                    s.fireEmitterWidth, 1f, spec.canvasSize),
                Val("Inset (px)", "How far each emitter sits out from the centre, in pixels.",
                    s.fireEmitterInset, 0f, Mathf.Max(1f, spec.canvasSize * 0.5f))));

            // ── Heat & fuel ──
            box.Add(Z.HGroup(
                Val("Heat", "How hot the emitter injects. Animate it to ignite, roar and die back (scaled by Progress).",
                    s.fireHeat, 0f, 1f),
                Val("Fuel", "How much unburnt fuel the emitter injects — fuel turns into heat as it burns, which is "
                    + "what gives a flame a body rather than a glow (scaled by Progress).",
                    s.fireFuel, 0f, 1f)));
            box.Add(Z.HGroup(
                Val("Burn", "How fast fuel converts into heat.", s.fireBurn, 0f, 4f),
                Val("Dissipation", "How fast heat fades. High = a short sharp flame; low = long lingering tongues.",
                    s.fireDissipation, 0f, 2f)));
            box.Add(Val("Pulse", "How much the emitter's output breathes in and out — a seeded wobble on the base.",
                s.firePulse, 0f, 2f));

            // ── Motion ──
            box.Add(Z.HGroup(
                Val("Flow", "Steady outward push away from the centre — a jet.", s.fireFlow, 0f, 6f),
                Val("Buoyancy", "How strongly HEAT carries itself outward — what makes a flame CLIMB rather than just "
                    + "spread.", s.fireBuoyancy, 0f, 10f)));
            box.Add(Z.HGroup(
                Val("Curl", "Swirl strength — curls the tongues instead of merely stretching them.", s.fireCurl, 0f, 6f),
                Val("Curl scale", "Size of the swirls — small = fine turbulence, large = slow broad rolls.",
                    s.fireCurlScale, 2f, 24f)));
            box.Add(Val("Flicker", "Sideways wobble of the tongues — how much they lick and wave.", s.fireFlicker, 0f, 4f));

            // ── Flame shape ── (what makes it read as a FLAME, not an expanding blob)
            box.Add(Z.HGroup(
                Val("Stretch", "Elongate the flame along its direction — high = long licking tongues, 0 = squat.",
                    s.fireStretch, 0f, 8f),
                Val("Pinch", "Taper the sides into a pointed tongue — most of what makes it read as a flame, and (with "
                    + "several arms) what opens the cold gaps between them.", s.firePinch, 0f, 3f)));
            box.Add(Val("Breakup", "Eat the edges into wisps instead of a smooth silhouette.", s.fireBreakup, 0f, 3f));

            // ── Confinement ── (the reason a hot setting stays usable — never touches the frame edge)
            box.Add(Z.HGroup(
                Val("Reach", "How far the flame may reach, as a fraction of the canvas half-size. Past this it's cooled "
                    + "to nothing, so it can NEVER touch the frame edge however hard it's driven — raise it for more "
                    + "room, not to make the fire bigger.", s.fireReach, 0f, 1f),
                Val("Edge cooling", "How hard the flame is cooled once past the Reach radius.",
                    s.fireEdgeCooling, 0f, 1f)));

            // ── Output / sim ──
            box.Add(Z.HGroup(
                Z.MicroSlider("Steps", s.fireSteps, 1f, 8f,
                    "Simulation steps per frame — more = smoother, faster-evolving motion for the same frame count "
                    + "(it does not change the flame's shape, only how far it gets each frame).",
                    v => Dirty(() => s.fireSteps = Mathf.Clamp(Mathf.RoundToInt(v), 1, 8)), 150f, showValue: true, decimals: 0),
                Z.MicroSlider("Threshold", s.fireThreshold, 0f, 0.9f,
                    "Heat below this reads as empty — raise it to carve a crisper silhouette.",
                    v => Dirty(() => s.fireThreshold = v), 150f, showValue: true)));
            box.Add(Z.MicroSlider("Contrast", s.fireContrast, 0.05f, 2f,
                "Contrast on the gradient lookup — below 1 pushes more of the flame toward the hot end of the ramp.",
                v => Dirty(() => s.fireContrast = v), 150f, showValue: true));

            shapeBody.Add(box);
        }

        // Fireball form box — the stateful CELLULAR SIMULATION (slice 6b). Heat blooms OUTWARD from one central point,
        // folded into `Arms` kaleidoscope wedges, so it reads as a radial/star explosion cooling at the rim. SINGLE-
        // SOURCE (no swarm, no particles — the whole layer IS the sim). Every rate is an envelope over the LAYER's life,
        // grouped Progress → Kaleidoscope → Core & reach → Arm shape → Output. Its colour ramp is the shared Shape Fill
        // above (smoke→fire) and its overall opacity the shared Shape Alpha; `size` and the Swarm don't apply. Reuses
        // Pyre's own FireballSim via the renderer's replay harness — the dials here map 1:1 onto Pyre's Fireball fields.
        void BuildFireballBox(PyrePlusLayer s)
        {
            // Defensive nulls (the real defaults come from the spec factories).
            s.fireballSource ??= new ZUIValue(1f);
            s.fireballSourceRadius ??= new ZUIValue(4f);
            s.fireballCooling ??= new ZUIValue(0.03f);
            s.fireballSharpness ??= new ZUIValue(0.2f);
            s.fireballSpread ??= new ZUIValue(0.5f);
            s.fireballReach ??= new ZUIValue(0.95f);

            var box = Z.BoxKeyed("Fireball",
                "A stateful CELLULAR flame SIMULATION (the cheap \"doom-fire\" family): heat blooms OUTWARD from one "
                + "central point and is folded into Arms kaleidoscope wedges, so it reads as a radial / star explosion "
                + "cooling at the rim. Reached by REPLAYING the sim from frame 0 (scrubbing and baking stay exact). "
                + "Every rate is an envelope over the layer's life — you author the SHAPE of the burn. SINGLE-SOURCE: "
                + "the Swarm doesn't apply. Colour is the Shape Fill above (smoke→fire ramp); overall opacity is the "
                + "Shape Alpha.", "pyreplus.fireball");

            // Progress — the master burn envelope (how hot the centre injects over life).
            box.Add(Val("Progress",
                "The burn's PROGRESS over the layer's life as ONE envelope — how hot the centre injects: 0 = off, "
                + "1 = full. Shape this to ignite, hold and die back, instead of setting a speed.",
                s.fireballSource, 0f, 1f));

            // ── Kaleidoscope ──
            box.Add(Z.HGroup(
                Z.MicroSlider("Arms", s.fireballArms, 1f, 12f,
                    "Radial wedges the flame is mirrored into. 1 = a plain outward burst; more give a kaleidoscope "
                    + "explosion — a 5-arm fireball is a 5-point star. Sharpness opens the cold gaps between arms.",
                    v => Dirty(() => s.fireballArms = Mathf.Clamp(Mathf.RoundToInt(v), 1, 12)), 150f, showValue: true, decimals: 0),
                Z.Field("Mode",
                    "Mirror = alternate wedges are reflected, so neighbours meet at a seam (a true kaleidoscope). "
                    + "Repeat = each wedge is the same, just rotated. Only matters with more than one arm.",
                    Z.Segmented(s.fireballMirror ? 0 : 1, FireballMirrorChoices,
                        "Mirror = alternate wedges reflected; Repeat = rotated copies.",
                        v => Dirty(() => s.fireballMirror = (v == 0))))));

            // ── Core & reach ──
            box.Add(Z.HGroup(
                Val("Core radius (px)", "Radius of the hot core at the centre, in pixels — the source the arms grow from.",
                    s.fireballSourceRadius, 1f, Mathf.Max(2f, spec.canvasSize * 0.5f)),
                Val("Reach", "How far the flame may reach, as a fraction of the canvas half-size. Past this it is cooled "
                    + "to nothing, so it can NEVER touch the frame edge — raise it to give long arms room.",
                    s.fireballReach, 0f, 1f)));

            // ── Arm shape ── (LENGTH vs THINNESS, set independently — the whole point of the fireball's look)
            box.Add(Z.HGroup(
                Val("Cooling", "How fast the flame cools travelling outward — this sets arm LENGTH. Low = long reaching "
                    + "tongues; high = a tight core. Pair a LOW value here with high Sharpness for long thin arms.",
                    s.fireballCooling, 0f, 0.3f),
                Val("Sharpness", "How hard the arms taper — their THINNESS, set independently of length. High = narrow "
                    + "pointed spokes; 0 = a round burst. Only matters with more than one arm.",
                    s.fireballSharpness, 0f, 2f)));
            box.Add(Val("Spread", "Sideways waver of the tongues — how much they lick and slip instead of being straight "
                + "radial spokes.", s.fireballSpread, 0f, 2f));

            // ── Output / sim ──
            box.Add(Z.HGroup(
                Z.MicroSlider("Threshold", s.fireballThreshold, 0f, 0.9f,
                    "Heat below this reads as empty — raise it to carve a crisper silhouette.",
                    v => Dirty(() => s.fireballThreshold = v), 150f, showValue: true),
                Z.MicroSlider("Contrast", s.fireballContrast, 0.05f, 2f,
                    "Contrast on the gradient lookup — below 1 pushes more of the flame toward the hot end of the ramp.",
                    v => Dirty(() => s.fireballContrast = v), 150f, showValue: true)));

            shapeBody.Add(box);
        }

        // Text form box — the string, the SDF font, spacing, the fill (mode + angle + gradient), the border (width
        // + gradient), and the 3D extrusion (Solid + Depth). Depth shows only when Solid, so the Solid toggle
        // rebuilds the Shape body. Text takes its colour from the Fill / Border gradients here — the shared Colour
        // row above is hidden for it. Size (above) is the character height; Tilt is the shared gemTilt (default);
        // per-letter Spin lives in the Advanced section.
        void BuildTextBox(PyrePlusLayer s)
        {
            s.textFill ??= new ZuiFill();
            s.textBorder ??= new ZuiFill();

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

            // Sweep is a wrapped MiniRadio (was a Dropdown/context-menu): three modes read as radio buttons that
            // fold onto a second line in a narrow column. On its own row (the labels are long), with Angle below.
            box.Add(Z.Field("Sweep",
                "How the Fill's gradient is swept across the letters. Per-char gradient = each letter contains "
                + "the whole gradient. Per-char step = each letter one flat colour along the gradient, by index. "
                + "Text gradient = one gradient swept across the whole line (degrades to per-char step when the "
                + "Swarm is on — there's no line to sweep). Text takes its colour from the Fill below, NOT the "
                + "Colour gradient above. (A Solid or textured Fill ignores this — see the Fill.)",
                Z.MiniRadio((int)s.textFillMode, TextFillModeChoices.ToArray(),
                    "How the Fill's gradient is swept: per-char gradient / per-char step / one gradient across the whole line.",
                    v => Dirty(() => s.textFillMode = (TextFillMode)v), wrap: true)));
            box.Add(Z.MicroSlider("Angle", s.textGradientAngle, -180f, 180f,
                "Rotates the fill axis. 0 = vertical bottom→top for per-char gradient; 0 = left→right across "
                + "the line for text gradient. (Per-char step is index-based and ignores it.)",
                v => Dirty(() => s.textGradientAngle = v), 150f, showValue: true));

            box.Add(FillRow("Fill",
                "The letter fill — a solid colour, a gradient (swept per the Sweep mode above), or a texture "
                + "(sprite / noise / grid / dots) stamped across each letter. This is where Text's colour comes "
                + "from; the Shape Fill above is hidden for Text.",
                s.textFill, new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));

            box.Add(Z.MicroSlider("Border px", s.textBorderWidth, 0f, 4f,
                "Letter outline width in screen pixels (0 = no border). Drawn as an SDF band just inside each "
                + "glyph edge, coloured from the Border fill below.",
                v => Dirty(() => s.textBorderWidth = v), 150f, showValue: true));
            box.Add(FillRow("Border",
                "The letter outline fill, sampled the same way as the Fill (solid / gradient / texture). A single "
                + "colour reads as a solid outline.",
                s.textBorder, new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));

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
        // change, so the conditional controls appear/disappear without rebuilding the whole window. The section
        // itself is held so RebuildSwarm can rebind its HEADER checkbox (the swarm enable) to the selected layer.
        ZuiSection swarmSection;
        VisualElement swarmBody;
        static readonly string[] SwarmModeLabels = { "Area", "Path" };
        static readonly string[] SwarmOrientLabels = { "None", "Outward", "Tangent" };
        static readonly string[] SwarmTimingLabels = { "Window", "Frames" };

        void BuildSwarm(VisualElement root, PyrePlusSpec s)
        {
            swarmSection = Z.Section("Swarm", "Place many particles in a shape instead of one centred particle.");
            swarmBody = new VisualElement();
            swarmSection.Add(swarmBody);
            root.Add(swarmSection);
            RebuildSwarm();
        }

        void RebuildSwarm()
        {
            var s = SelLayer;   // every Swarm control now edits the SELECTED layer's fields
            if (s == null || swarmBody == null) { swarmBody?.Clear(); return; }
            swarmBody.Clear();

            // The swarm enable is a HEADER checkbox on the section (swarm DATA, not view state — the view system
            // only captures ZuiBoxes, and this sits on a ZuiSection), rebound to the SELECTED layer every rebuild.
            // Off ⇒ exactly one centred particle (Shape alone); the body shows nothing. For the Text form the count
            // is the STRING LENGTH, so its Count field is hidden below.
            swarmSection?.SetHeaderToggle(s.swarmEnabled,
                "Off = one centred particle (the Shape section alone; Text = one centred line). On = Count particles "
                + "placed in a shape. For the Text form the particle count is the number of characters, so Count is "
                + "hidden — each letter rides one swarm position.",
                v => { Dirty(() => s.swarmEnabled = v); RebuildSwarm(); });

            // Fire and Fireball are whole-layer SIMULATIONS with their own source (Fire's built-in arm emitters,
            // Fireball's single central point) — the swarm does not drive either, EXCEPT a Fire layer with 'Swarm
            // emitters' on (slice 8), which DOES source its emitters from the swarm and so wants the real placement
            // controls. Show a note instead of the (inert) swarm controls only when the swarm truly doesn't drive it.
            bool fireSwarm = s.shapeForm == ShapeForm.Fire && s.fireSwarmEmitters;
            if ((s.shapeForm == ShapeForm.Fire || s.shapeForm == ShapeForm.Fireball) && !fireSwarm)
            {
                var note = new Label(s.shapeForm == ShapeForm.Fire
                    ? "Fire uses its own built-in emitters (arms around the centre) — the Swarm doesn't place it. "
                      + "Turn on 'Swarm emitters' in the Fire box to source them from the Swarm instead. Otherwise "
                      + "use the Fire box's Arms / Direction to shape the flame."
                    : "Fireball is single-source — heat blooms from one central point, folded into kaleidoscope arms. "
                      + "The Swarm doesn't place it (Fireball stays single-source by design). Use the Fireball box's "
                      + "Arms / Cooling / Sharpness to shape the explosion instead.");
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.opacity = 0.7f;
                note.style.marginTop = 2; note.style.marginBottom = 2;
                swarmBody.Add(note);
                return;
            }

            // Fire + 'Swarm emitters' on: the placement controls below now drive the flame's emitters. A short note
            // makes that explicit before the normal swarm dials.
            if (fireSwarm)
            {
                var note = new Label("Swarm emitters ON — each alive swarm particle injects heat/fuel into ONE shared "
                    + "fire field. Place them with the controls below; the Fire box's Emitter width / Heat / Fuel set "
                    + "each source's size and strength, and Arms / Direction shape the field's buoyancy.");
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.opacity = 0.7f;
                note.style.marginTop = 2; note.style.marginBottom = 2;
                swarmBody.Add(note);
            }

            if (!s.swarmEnabled) return;

            // Dual spawn-path visualisation (P4): two independent overlay toggles — the authored Shape (outline +
            // drag handle + numbered spawn dots) and the objective spawner Trace (the canonical spine). Both are
            // cosmetic preview aids on the SPEC (never baked; they never re-render the frames → DirtyRepaintOnly),
            // and both may be on at once. Neither on = no overlay at all. (Spec-level, not per-layer: the overlay
            // is a single global aid that follows whichever layer is selected.)
            swarmBody.Add(Z.HGroup(
                Z.Toggle("Show shape",
                    "Draw the authored spawn shape — its outline, the drag handle and a numbered dot at every "
                    + "particle's actual spawn point.",
                    spec.previewShowShape, v => DirtyRepaintOnly(() => spec.previewShowShape = v)),
                Z.Toggle("Show trace",
                    "Draw the objective spawner trace — the canonical amber path the spawn point sweeps through "
                    + "space over the whole timeline, under the spawn dots.",
                    spec.previewShowTrace, v => DirtyRepaintOnly(() => spec.previewShowTrace = v))));

            float half = Mathf.Max(1f, spec.canvasSize * 0.5f);

            // Count + Particle life — both apply whichever Timing mode is chosen — packed to reflow. Text hides
            // Count (the string length rules the particle count) but keeps Particle life.
            var baseRow = new List<VisualElement>();
            if (s.shapeForm != ShapeForm.Text)
                baseRow.Add(Z.MicroSlider("Count", s.swarmCount, 2f, 200f,
                    "How many particles the swarm places (at least 2).",
                    v => Dirty(() => s.swarmCount = Mathf.Clamp(Mathf.RoundToInt(v), 2, 200)), 150f,
                    showValue: true, decimals: 0));
            baseRow.Add(Z.MicroSlider("Particle life", s.swarmParticleLife, 0.05f, 1f,
                "How long each particle lives, as a fraction of the timeline. Its colour/alpha/size envelopes "
                + "always play over ITS OWN life, not the timeline.",
                v => Dirty(() => s.swarmParticleLife = v), 150f, showValue: true));
            swarmBody.Add(Z.HGroup(baseRow.ToArray()));

            // Timing mode (G3): Window spreads the spawns across a fraction of the timeline; Frames spawns the first
            // particle on a chosen frame, then one more every N frames. Rebuild on change so the mode's own rows
            // swap in; the composed tooltip re-reads the current mode (house rule: no if-lists in a tooltip).
            swarmBody.Add(Z.Field("Timing", SwarmTimingTooltip(s),
                Z.Segmented((int)s.swarmTiming, SwarmTimingLabels, SwarmTimingTooltip(s),
                    v => { Dirty(() => s.swarmTiming = (SwarmTiming)v); RebuildSwarm(); })));

            if (s.swarmTiming == SwarmTiming.Window)
            {
                // Spawn timing IS the whole timing mapping now (the old Spawn window scale is gone — it was just a
                // scale of this curve). X = WHICH particle, value = WHEN on the TIMELINE it spawns.
                swarmBody.Add(Val("Spawn timing",
                    "Maps WHICH particle (X: 0 = the first spawned, 1 = the last) to WHEN it spawns on the "
                    + "timeline (value: 0 = frame 1, 1 = the last frame). End the curve low to finish spawning "
                    + "early — the default (0→0.5) spreads the spawns across the first half. Linear = evenly "
                    + "spread; ease it for a burst then a trickle; a flat Static value spawns them all together "
                    + "at that moment; MinMax gives every particle a random moment.",
                    s.swarmSpawnTiming, 0f, 1f));
            }
            else
            {
                // FrameStep: first frame + step, both 0-based frame indexes (the transport readout shows frames
                // 1-based). Packed to reflow; each clamps at 0 (its [Min(0)] spec attribute).
                string firstTip = "The frame the FIRST particle spawns on. 0-based — frame 0 is the very first frame "
                    + "(the transport readout shows it as \"frame 1\").";
                string stepTip = "Frames between each following spawn (0-based step). 0 = every particle spawns "
                    + "together on the First frame; 2 = one new particle every 2 frames until the Count is filled.";
                swarmBody.Add(WrapRow(
                    Z.Field("First frame", firstTip,
                        Z.Int(s.swarmFirstFrame, firstTip, v => Dirty(() => s.swarmFirstFrame = Mathf.Max(0, v)), 60f)),
                    Z.Field("Every N frames", stepTip,
                        Z.Int(s.swarmFrameStep, stepTip, v => Dirty(() => s.swarmFrameStep = Mathf.Max(0, v)), 60f))));
            }

            // Placement geometry: mode (Area vs Path) + the shape kind, packed together.
            string modeTip = "Area = particles fill the shape's interior; Path = particles ride along its outline.";
            // Labels + the matching enum values, kept in lockstep so the MiniRadio index maps by POSITION (not by a
            // direct (SwarmShapeKind)index cast) — necessary because Custom is Path-only, so a raw cast would put Line
            // at the wrong ordinal in Area mode. Line is always offered (a 1-D row, valid in both Area and Path).
            var kindChoices = new List<string> { "Circle", "Triangle", "Square", "Pentagon", "Hexagon" };
            var kindValues = new List<SwarmShapeKind>
                { SwarmShapeKind.Circle, SwarmShapeKind.Triangle, SwarmShapeKind.Square, SwarmShapeKind.Pentagon, SwarmShapeKind.Hexagon };
            if (s.swarmSpawnMode == SwarmSpawnMode.Path) { kindChoices.Add("Custom"); kindValues.Add(SwarmShapeKind.Custom); }   // Custom is Path-only
            kindChoices.Add("Line"); kindValues.Add(SwarmShapeKind.Line);   // Line: a straight row (Bars); both modes
            string kindTip = "The swarm's outline — a regular polygon by side count (Circle = ∞ sides)"
                + (s.swarmSpawnMode == SwarmSpawnMode.Path ? ", a hand-drawn Custom path" : "")
                + ", or a straight Line (a row through the centre — a Streak row = Pyre's Bars).";

            swarmBody.Add(Z.Field("Mode", modeTip,
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
                })));
            // Shape kind is a wrapped MiniRadio (was a Dropdown/context-menu) — 5–6 short labels read as radio
            // buttons folding onto a second line in a narrow column; on its own row since it's a multi-item control.
            int kindSel = Mathf.Max(0, kindValues.IndexOf(s.swarmShapeKind));
            swarmBody.Add(Z.Field("Shape", kindTip,
                Z.MiniRadio(kindSel, kindChoices.ToArray(), kindTip,
                    v => { Dirty(() => s.swarmShapeKind = kindValues[Mathf.Clamp(v, 0, kindValues.Count - 1)]); RebuildSwarm(); }, wrap: true)));

            // Per-particle FACING as each is placed (S1). Rebuild on change so the composed tooltip re-reads the
            // current mode (Tangent means something different in Area vs Path).
            swarmBody.Add(Z.Field("Orient", SwarmOrientTooltip(s),
                Z.Segmented((int)s.swarmOrient, SwarmOrientLabels, SwarmOrientTooltip(s),
                    v => { Dirty(() => s.swarmOrient = (SwarmOrient)v); RebuildSwarm(); })));

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

                // Even-path spacing (B1): spread the string EVENLY along the outline by index; Spawn travel then
                // rides the whole evenly-spaced string along the path together. Spread only matters when even, so
                // it's hidden until then (toggling rebuilds this section).
                var evenRow = new List<VisualElement>
                {
                    Z.Toggle("Even spacing",
                        "Spread the particles EVENLY along the outline (by index) instead of each sampling Spawn "
                        + "travel on its own. Spawn travel then rides the whole evenly-spaced string along the "
                        + "path together — ideal for readable text or a comet chain on a path.",
                        s.swarmEvenPath, v => { Dirty(() => s.swarmEvenPath = v); RebuildSwarm(); }),
                };
                if (s.swarmEvenPath)
                    evenRow.Add(Z.MicroSlider("Spread", s.swarmPathSpread, 0f, 1f,
                        "How much of the outline the evenly-spaced string covers (1 = the whole path start-to-end; "
                        + "smaller packs the particles into a shorter arc).",
                        v => Dirty(() => s.swarmPathSpread = v), 150f, showValue: true));
                swarmBody.Add(WrapRow(evenRow.ToArray()));
            }

            // Per-particle SIZE by index (S1) + shared DEATH point (S1).
            swarmBody.Add(Val("Scale by index",
                "Multiplies each particle's size by a factor read from its index (0 = first, 1 = last). Static 1 = "
                + "every particle full size; a Curve tapers the swarm (ends-vs-middle, centre-vs-edge — author it "
                + "freely, Bars-style); MinMax gives each particle a random size.",
                s.swarmScaleByIndex, 0f, 3f));
            swarmBody.Add(Z.Toggle("Die together",
                "All particles fade out at the SAME timeline moment (spawn-window end + particle life) instead of "
                + "each dying one particle-life after its own spawn — a burst that vanishes as one.",
                s.swarmDieTogether, v => Dirty(() => s.swarmDieTogether = v)));

            // Shared shape transform — every field a per-spawn snapshot (see the box tooltip). This is the SPAWNER
            // transform (where particles are PLACED); the separate Swarm spin box below rotates the placed cloud live.
            var xform = Z.BoxKeyed("Transform",
                "The SPAWNER transform — offset, size, rotation and pseudo-3D tilt of the shape particles spawn "
                + "onto. Every field is a per-spawn SNAPSHOT: each particle reads it at its OWN spawn moment and "
                + "keeps that value for life. Animating a field therefore does NOT move particles already placed — "
                + "it spreads a TRAIL of new spawns along the curve (rotate past 360, or travel past once-around, "
                + "for several laps). To spin the already-placed cloud live instead, use Swarm spin below.",
                "pyreplus.transform");
            xform.Add(Val2D("Offset",
                "Shape-centre offset in canvas pixels — drag to move the whole shape off the origin. Animating it "
                + "does NOT slide placed particles; each takes the offset at its own spawn moment, leaving a trail.",
                s.shapeOffsetX, s.shapeOffsetY,
                new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)));
            xform.Add(WrapRow(
                Val("Scale (px)", "The shape's radius in canvas pixels. Animating this does NOT resize placed "
                    + "particles — each takes the radius at its own spawn moment, so a growing curve leaves a "
                    + "trail of expanding rings.", s.shapeScale, 0f, spec.canvasSize),   // max = canvas (was 64)
                Z.MicroSlider("Snap", s.shapeScaleSnap, 0f, spec.canvasSize * 0.25f,
                    "Round the evaluated scale to the nearest multiple of this, so placements land on "
                    + "fixed radii. 0 = off.",
                    v => Dirty(() => s.shapeScaleSnap = Mathf.Clamp(v, 0f, spec.canvasSize * 0.25f)), 150f, showValue: true)));
            // The spawner's rotation as the SAME three-axis Turn/Tilt/Roll stack the Solid box uses — one animatable
            // Val per axis (rather than a yaw×pitch 2D pad), so it reads consistently AND, prefixed "Spawner", is
            // unmistakable from the live Swarm spin box below. These are what the user pictured as "a stack of 3
            // controls". Turn = yaw (shapeYaw), Tilt = pitch (shapePitch), Roll = the in-plane Z (shapeRotation);
            // each is a per-spawn SNAPSHOT (animating spreads a placement trail, it does not live-spin the cloud).
            xform.Add(Val("Spawner turn °",
                "Yaw the whole spawn shape around the VERTICAL axis (turntable), in degrees. A per-spawn SNAPSHOT: "
                + "each particle takes the value at its OWN spawn moment, so animating it does NOT re-turn placed "
                + "particles — it SPREADS new spawns into a trail (several laps past 360). Rotates WHERE particles "
                + "are placed (the spawner), not the placed cloud — for that, use Swarm spin below.",
                s.shapeYaw, -1440f, 1440f));
            xform.Add(Val("Spawner tilt °",
                "Pitch the whole spawn shape around the HORIZONTAL axis (tip it toward/away), in degrees — pseudo-3D, "
                + "so nearer parts spawn bigger and brighter. A per-spawn SNAPSHOT: each particle takes the tilt at "
                + "its OWN spawn moment, spreading a trail rather than re-tilting placed particles.",
                s.shapePitch, -1440f, 1440f));
            xform.Add(Val("Spawner roll °",
                "Roll the whole spawn shape in the canvas plane (about the axis pointing at you), in degrees. A "
                + "per-spawn SNAPSHOT: each particle takes the value at its OWN spawn moment, so a rising curve "
                + "spreads spawns around the shape (several laps past 360) rather than spinning placed particles.",
                s.shapeRotation, -1440f, 1440f));
            swarmBody.Add(xform);

            // Swarm spin (Issue 2B) — a SEPARATE box, the deliberate counterpart to the Spawner rotation above: a
            // LIVE rigid rotation of the whole placed cloud, evaluated at the CURRENT frame (not a spawn snapshot),
            // so animating an axis spins the entire swarm as one solid group with its arrangement preserved. Same
            // three-axis Turn/Tilt/Roll stack, so the two rotations read as a matched pair. All default 0 (no spin).
            s.swarmTurn ??= new ZUIValue(0f);
            s.swarmTilt ??= new ZUIValue(0f);
            s.swarmRoll ??= new ZUIValue(0f);
            s.swarmScale ??= new ZUIValue(1f);
            var spin = Z.BoxKeyed("Swarm spin",
                "A LIVE rigid rotation of the whole placed swarm around the shape centre, evaluated at the CURRENT "
                + "frame — every particle rotates together keeping the arrangement, so animating an axis spins the "
                + "cloud as one solid group (a turning constellation). DISTINCT from the Spawner rotation in "
                + "Transform above: that snapshots per spawn to SPREAD placements into a trail; this spins the "
                + "already-placed cloud. All three default to 0 (no spin).",
                "pyreplus.swarmspin");
            spin.Add(Val("Swarm turn °",
                "Yaw the whole placed cloud around the VERTICAL axis (turntable), live at the current frame — the "
                + "swarm spins as one, arrangement preserved. Animate it for a rotating cloud.",
                s.swarmTurn, -1440f, 1440f));
            spin.Add(Val("Swarm tilt °",
                "Pitch the whole placed cloud around the HORIZONTAL axis, live at the current frame — tips the cloud "
                + "toward/away (pseudo-3D). Animate it to roll the swarm forward/back.",
                s.swarmTilt, -1440f, 1440f));
            spin.Add(Val("Swarm roll °",
                "Roll the whole placed cloud in the screen plane (about the axis pointing at you), live at the "
                + "current frame. Animate it to spin the swarm flat against the screen.",
                s.swarmRoll, -1440f, 1440f));
            spin.Add(Val("Swarm scale",
                "A LIVE uniform radial scale of the whole placed cloud about the shape centre, at the current frame "
                + "— the sibling of the three spin axes above. 1 = identity (no change); animate it to make the swarm "
                + "expand or contract as one group (a live breathing cloud). DISTINCT from the Spawner scale/radius in "
                + "Transform above, which snapshots per spawn and leaves a trail; this resizes the already-placed "
                + "cloud. Default 1 (no scaling).",
                s.swarmScale, 0f, 4f));
            swarmBody.Add(spin);

            // ── Coalesce render mode — how the placed cloud turns into pixels. Off = draw + Over-composite each
            // particle (every form). Fuse = MetaBlob: read the WHOLE cloud as one metaball field and composite a
            // single gradient-shaded merged blob (overlapping particles melt with necks). Ramp = HeightBalls: fuse
            // the cloud into density/height fields, relief-light the height slope and shade one smoke→fire cloud. A
            // wrapped MiniRadio (Off / Fuse / Ramp), rebuilt on change so the Fuse/Ramp box appears/disappears. Sits
            // at the end since, like the Swarm spin/scale above, it reads the already-placed cloud as a whole.
            string coalesceTip = "How the placed swarm turns into pixels. Off = each particle is drawn and "
                + "Over-composited (every form). Fuse = MetaBlob: the whole cloud is read as ONE metaball field and "
                + "composited as a single smooth, gradient-shaded blob — overlapping particles MELT together (necks "
                + "between them). Ramp = HeightBalls: the cloud fuses into density/height fields, relief-lit from the "
                + "height slope and shaded as one carved smoke→fire mass. Both are best with an overlapping swarm.";
            int coalesceSel = (int)s.coalesce;
            swarmBody.Add(Z.Field("Coalesce", coalesceTip,
                Z.MiniRadio(coalesceSel, CoalesceChoices, coalesceTip,
                    v => { Dirty(() => s.coalesce = (LayerCoalesce)v); RebuildSwarm(); },
                    wrap: true)));

            if (s.coalesce == LayerCoalesce.Fuse)
            {
                var fuse = Z.BoxKeyed("Fuse",
                    "MetaBlob field-pass: the placed particles become metaball circles summed into ONE field, then "
                    + "thresholded and gradient-shaded (by the Shape's Fill) into a single merged blob. Threshold "
                    + "sets how eagerly they fuse; Shade range maps the gradient surface→core; Softness is the edge AA.",
                    "pyreplus.fuse");
                fuse.Add(Z.MicroSlider("Threshold", s.fuseThreshold, 0.02f, 2f,
                    "Iso-threshold. Lower = the particles fuse more eagerly (fatter necks, one shape); higher = "
                    + "distinct lobes pull apart.",
                    v => Dirty(() => s.fuseThreshold = v), 170f, showValue: true));
                fuse.Add(Z.MicroSlider("Shade range", s.fuseShadeRange, 0.05f, 4f,
                    "How much field above the threshold spans the Fill gradient (surface → core). Smaller = a "
                    + "punchier, brighter core.",
                    v => Dirty(() => s.fuseShadeRange = v), 170f, showValue: true));
                fuse.Add(Z.MicroSlider("Softness", s.fuseSoftness, 0.01f, 1f,
                    "Edge softness — the alpha AA band across the iso-surface (internally capped at the Threshold). "
                    + "0.01 ≈ crisp.",
                    v => Dirty(() => s.fuseSoftness = v), 170f, showValue: true));
                swarmBody.Add(fuse);
            }
            else if (s.coalesce == LayerCoalesce.Ramp)
            {
                // Defensive: a hand-built layer might predate these fields. Fresh/duplicated layers always have them
                // (field initialisers + Clone deep-copy), so this only guards the rare null path.
                s.density ??= new ZUIValue(0.4f);
                s.heat ??= new ZUIValue(0.6f);
                var ramp = Z.BoxKeyed("Ramp",
                    "HeightBalls field-pass: the placed particles become DOMES fused (by a soft max) into shared "
                    + "density + height fields, relief-lit from the height slope and shaded (by the Shape's Fill) as "
                    + "ONE carved smoke→fire cloud. Density/Heat are the per-particle weights; the knobs shape how the "
                    + "domes fuse, the opacity, the relief lighting and the boiling rim.",
                    "pyreplus.ramp");
                ramp.Add(Val("Density",
                    "Each particle's MASS/body over its own life — gives the cloud the volume that catches the relief "
                    + "light and nudges it up the ramp even with no heat. The density field's per-particle weight.",
                    s.density, 0f, 1f));
                ramp.Add(Val("Heat",
                    "Each particle's HEIGHT/energy over its own life — how far up the smoke→fire ramp it sits (low = "
                    + "cold smoke, high = fire) and how tall it stands in the relief light.",
                    s.heat, 0f, 1f));
                ramp.Add(Z.MicroSlider("Fusion", s.rampFusion, 0f, 1f,
                    "How eagerly neighbouring domes MELT into one mass. 0 = a hard max (distinct orbs); higher = a "
                    + "smoother, heavier merged cloud.",
                    v => Dirty(() => s.rampFusion = v), 170f, showValue: true));
                ramp.Add(Z.MicroSlider("Coverage", s.rampCoverage, 0.1f, 24f,
                    "Opacity gain — how much combined density+heat becomes alpha. Higher = a more solid, opaque cloud.",
                    v => Dirty(() => s.rampCoverage = v), 170f, showValue: true));
                ramp.Add(Z.Toggle("Relief lighting",
                    "Light the cloud's relief from the height field's local slope — carved highlights and shadow. "
                    + "Off = a flat gradient cloud.",
                    s.rampLighting, v => { Dirty(() => s.rampLighting = v); RebuildSwarm(); }));
                if (s.rampLighting)
                {
                    ramp.Add(Z.MicroSlider("Relief", s.rampRelief, 0.01f, 8f,
                        "How steeply the height slope bends the surface normal. Higher = a more sharply carved, "
                        + "bumpier lit surface.",
                        v => Dirty(() => s.rampRelief = v), 170f, showValue: true));
                    ramp.Add(Z.MicroSlider("Light angle", s.rampLightAngle, 0f, 360f,
                        "The relief light's angle in degrees (screen plane) — which way the highlights fall across "
                        + "the cloud.",
                        v => Dirty(() => s.rampLightAngle = v), 170f, showValue: true));
                }
                ramp.Add(Z.MicroSlider("Rim boil", s.rampRimScale, 0f, 1f,
                    "Surface-noise rim — deforms the shared cloud rim so neighbouring domes bulge/pinch together and "
                    + "read as ONE boiling mass instead of fused flat discs. 0 = a smooth rim.",
                    v => Dirty(() => s.rampRimScale = v), 170f, showValue: true));
                swarmBody.Add(ramp);
            }
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
                // Show where each bake frame lands on the curve (numbers thin out when frames are dense).
                frameCount = spec != null ? spec.frameCount : 0,
            };
            return Z.Value(label, v, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre Plus"));
        }

        // 2D analog of Val — an animatable XY pair, same Undo-record + preview-dirty wiring.
        VisualElement Val2D(string label, string tooltip, ZUIValue x, ZUIValue y, ZuiValue2DControl.Options o)
            => Z.Value2D(label, x, y, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre Plus"));

        // A Z.Fill row (ZuiFill editor) wired to the same Undo/dirty/preview contract as Val: record the asset once
        // per gesture (onBeforeMutate), then dirty + repaint (onChanged). The control mutates the ZuiFill instance
        // directly, so there's no explicit setter.
        ZuiFillControl FillRow(string label, string tooltip, ZuiFill fill, ZuiFillControl.Options opt = null)
            => Z.Fill(label, fill, tooltip,
                () => { if (spec != null) EditorUtility.SetDirty(spec); MarkDirty(); },
                () => { if (spec != null) Undo.RecordObject(spec, "Edit Pyre Plus"); },
                opt);

        // Compact Z.Fill for the Solid box's slot fills (spec / line / edge / inner), packed beside their sliders.
        ZuiFillControl SlotFill(string label, string tooltip, ZuiFill fill)
            => FillRow(label, tooltip, fill, new ZuiFillControl.Options().WithWidth(96f));

        // The Solid box tooltip, naming the CURRENT solid form (the box rebuilds on form change).
        static string SolidBoxTooltip(ShapeForm f)
        {
            const string shared = "A true-3D form lit per-pixel by one key light, with light-catching hard edge lines "
                + "and two glows (a rim halo + an interior glow). Its material colour is the shared Fill above (a blue "
                + "gradient = a sapphire); its base size is the shared Size. ";
            switch (f)
            {
                case ShapeForm.Gem:     return shared + "This form: an octahedral crystal.";
                case ShapeForm.Box:     return shared + "This form: a cuboid.";
                case ShapeForm.Pyramid: return shared + "This form: a square pyramid.";
                case ShapeForm.Can:     return shared + "This form: a cylinder.";
                case ShapeForm.Orb:     return shared + "This form: a sphere (no geometry rows — a ball needs none).";
                case ShapeForm.Ring:    return shared + "This form: a flat two-sided tilted annulus (a Saturn ring).";
                default:                return shared;
            }
        }

        // FIX 1 — when a layer shows a 3D solid form (Gem/Box/Pyramid/Can/Orb/Ring) and its Fill is still the EXACT
        // pristine OverLife fire default, swap that fill to a STEADY Solid material (the gradient's mid colour) so
        // the lit solid reads as light-driven, not "pulsing": the OverLife gold→dark-red life ramp darkens the whole
        // gem over its life, and the light dials can't counter it because it IS the material colour. Only the
        // untouched factory default is converted (IsPristineDefaultShapeFill is strict); a user-customised fill is
        // left exactly as-is, and Disc/Crescent/Sparkle/Sprite/Text/Streak/Star keep the OverLife fire default
        // (right for soft particles). MUST be called inside a Dirty() block (records Undo). One-way — never converts
        // a Solid back to OverLife. Returns true when it changed the fill.
        static bool SteadyDefaultFillForSolid(PyrePlusLayer layer)
        {
            if (layer == null || !IsSolidForm(layer.shapeForm)) return false;
            var f = layer.shapeFill;
            if (!PyrePlusLayer.IsPristineDefaultShapeFill(f)) return false;
            f.color = f.gradient.Evaluate(0.5f);   // a sensible mid material (the fire gradient's midpoint)
            f.mode = ZuiFill.Mode.Solid;
            return true;
        }

        // The 3D-solid forms — they share BuildSolidBox and its Turn/Tilt/Roll rotation trio (and hide the Advanced
        // Spin row, since Turn IS that field). Disc / Crescent / Sparkle / Sprite / Text are NOT solid forms.
        static bool IsSolidForm(ShapeForm f) =>
            f == ShapeForm.Gem || f == ShapeForm.Box || f == ShapeForm.Pyramid ||
            f == ShapeForm.Can || f == ShapeForm.Orb || f == ShapeForm.Ring;

        // The Swarm Timing tooltip, composed for the CURRENT mode (the swarm rebuilds on change), so it names only
        // the mode the user is in (house rule: no if-lists in a tooltip).
        static string SwarmTimingTooltip(PyrePlusLayer s)
        {
            const string common = "How the swarm's spawns are spread across the timeline. ";
            if (s.swarmTiming == SwarmTiming.FrameStep)
                return common + "Frames: the first particle spawns on First frame, then one more every N frames until "
                     + "the Count is filled (N = 0 spawns them all on that one frame). Frame indexes are 0-based; the "
                     + "transport readout shows frames 1-based.";
            return common + "Window: spawns spread across a fraction of the timeline (Spawn window), with Spawn timing "
                 + "remapping which particle lands when inside it.";
        }

        // The Swarm Orient tooltip, composed for the CURRENT orient + mode (the swarm rebuilds on either change),
        // so it never lists branches the user isn't in.
        static string SwarmOrientTooltip(PyrePlusLayer s)
        {
            const string common = "Turns each particle to face a direction as it's placed — the renderer folds it "
                + "into the form's own rotation (Streak forward, Sprite/Disc spin, Text/solid roll). ";
            switch (s.swarmOrient)
            {
                case SwarmOrient.Outward:
                    return common + "Outward: each particle faces away from the shape centre (a Streak points "
                         + "outward; letters/solids roll to match).";
                case SwarmOrient.PathTangent:
                    return common + (s.swarmSpawnMode == SwarmSpawnMode.Path
                        ? "Tangent: each particle faces ALONG the outline it rides — readable text follows the "
                          + "path and Streaks trail along it. Pair with Even spacing for a clean string."
                        : "Tangent: only meaningful in Path mode; in Area it falls back to Outward.");
                default:
                    return common + "None: particles keep their own orientation (no turning). Outward faces them "
                         + "away from the centre; Tangent (Path mode) faces them along the outline.";
            }
        }

        // ── per-form tooltip composers (rebuilt on every form change, so each branches to the CURRENT form) ──
        static string FillTooltip(ShapeForm f)
        {
            const string modes = " Solid = one flat colour; Over life = a gradient across the particle's life; "
                + "Linear / Radial / Noise = a spatial fill across the shape. Every mode is alpha-capable (⋯ to switch).";
            if (IsSolidForm(f))
                return "The solid's material fill — a blue gradient reads as a sapphire." + modes;
            if (f == ShapeForm.Sprite)
                return "Tints the stamped sprite (multiplied over its colours), sampled at the particle centre only — "
                     + "a spatial fill has no effect on a sprite tint. Turn Tint off for the sprite's raw colours." + modes;
            if (f == ShapeForm.Fire)
                return "The flame's colour RAMP: the sim reads this fill's GRADIENT as one smoke→fire ramp (low end = "
                     + "smoke, high end = fire), mapping each pixel's heat onto it — like Height balls. Prefer an Over "
                     + "life gradient; a Solid fill leaves the flame white." + modes;
            if (f == ShapeForm.Fireball)
                return "The fireball's colour RAMP: the cellular sim reads this fill's GRADIENT as one smoke→fire ramp "
                     + "(low end = cool rim, high end = hot core), mapping each pixel's heat onto it. Prefer an Over "
                     + "life gradient; a Solid fill leaves the flame white." + modes;
            return "The particle's colour (0 = birth, 1 = death)." + modes;
        }

        static string SizeTooltip(ShapeForm f)
        {
            if (IsSolidForm(f))
                return "Radius in pixels over the particle's own life — the base size R that Aspect / Depth (or the "
                     + "Gem's Crown / Pavilion) scale from.";
            if (f == ShapeForm.Text)
                return "The character HEIGHT in pixels over the particle's own life.";
            if (f == ShapeForm.Star)
                return "The star's TIP radius in pixels over the particle's own life — the arms reach out to it "
                     + "(the valleys sit at Length inside).";
            return "Radius in pixels over the particle's own life.";
        }

        // Spin only shows for the 2D forms + Text (solids edit it as Turn), so this branches only those cases.
        static string SpinTooltip(ShapeForm f)
        {
            switch (f)
            {
                case ShapeForm.Crescent:
                    return "Degrees the crescent rotates over its own life — turns the whole crescent, on top of its "
                         + "own bite Angle.";
                case ShapeForm.Sparkle:
                    return "Degrees the sparkle field rotates in place over its own life — an even field is radially "
                         + "symmetric, so add a geometry/texture Modifier for the spin to read.";
                case ShapeForm.Sprite:
                    return "Degrees the stamped sprite rotates over its own life.";
                case ShapeForm.Streak:
                    return "Degrees the streak turns about its root over its own life — rotates its forward "
                         + "direction, ADDED on top of any Swarm Orient facing.";
                case ShapeForm.Star:
                    return "Degrees the star spins in place over its own life — unlike a disc, a star isn't "
                         + "radially symmetric, so its arms visibly turn.";
                case ShapeForm.Text:
                    return "Degrees each letter yaws about its OWN centre over its life (its 3D letter-box turns to "
                         + "face the light); paired with the letters' tilt for the extruded look.";
                default:   // Disc
                    return "Degrees the disc's pixels spin in place over its own life — a plain disc is radially "
                         + "symmetric so it shows little (add a geometry/texture Modifier for the spin to read).";
            }
        }

        static string TurnTooltip(ShapeForm f)
        {
            const string common = "Rotate around the VERTICAL axis, like a turntable (yaw), over the particle's own life. ";
            if (f == ShapeForm.Orb)
                return common + "For the Orb it rolls the lit hotspot left/right around the ball (the silhouette never changes).";
            if (f == ShapeForm.Ring)
                return common + "For the Ring it swings the ellipse — turning the ring edge-on along the horizontal axis.";
            return common + "Sweeps the solid's facets past the light.";
        }

        static string TiltTooltip(ShapeForm f)
        {
            const string common = "Lean the top toward or away from you (about the HORIZONTAL axis), over the particle's own life. ";
            if (f == ShapeForm.Orb)
                return common + "For the Orb it rolls the lit hotspot up/down across the ball (the silhouette never changes).";
            if (f == ShapeForm.Ring)
                return common + "For the Ring it opens/closes the ellipse — 0° face-on (a full circle), ±90° edge-on (a sliver).";
            return common + "Tips the solid so different facets catch the light.";
        }

        // Roll is hidden for the Ring (geometrically inert there), so this only ever branches Orb vs the facet solids.
        static string RollTooltip(ShapeForm f)
        {
            const string common = "Rotate the solid flat against the screen (about the axis pointing at you), over the particle's own life. ";
            if (f == ShapeForm.Orb)
                return common + "For the Orb it rolls the lit hotspot around the centre of the ball.";
            return common + "Spins the whole silhouette in the screen plane.";
        }

        void Dirty(System.Action apply)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Pyre Plus");
            apply();
            EditorUtility.SetDirty(spec);
            MarkDirty();
        }

        // A cosmetic edit that only changes how the preview is LAID OUT / drawn (not the rendered frames): record
        // Undo + SetDirty + repaint, but do NOT set previewDirty. Used by the filmstrip's Tile-px slider so dragging
        // it re-lays-out the existing tile textures instead of re-rendering every frame (the strip cache is keyed
        // to previewDirty + frameCount/canvas only — tile size is neither).
        void DirtyRepaintOnly(System.Action apply)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Pyre Plus");
            apply();
            EditorUtility.SetDirty(spec);
            preview?.MarkDirtyRepaint();
        }

        // Deferred, coalesced Rebuild used only by the Canvas Size slider (Bug 1). Refreshing every
        // canvasSize-derived control range needs a full Rebuild, but doing it per drag-delta recreated the slider
        // and broke the drag. Scheduled off rootVisualElement (which SURVIVES Rebuild — Rebuild only replaces its
        // children) with StartingIn(0) so it runs on the next frame, AFTER the pointer-up event has finished
        // dispatching; coalesced (Pause the prior item) so rapid commits fire it once.
        void ScheduleRangeRebuild()
        {
            rangeRebuildPending?.Pause();
            rangeRebuildPending = rootVisualElement.schedule.Execute(Rebuild).StartingIn(0);
        }

        // DrawPreview + the Swarm authoring overlay live in PyrePlusWindow.Preview.cs.
    }
}
