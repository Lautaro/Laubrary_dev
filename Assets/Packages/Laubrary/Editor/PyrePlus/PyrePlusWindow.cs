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
        // The preview backdrop is an editor-only BackSplash (camera colour + one image), persisted PER ASSET on
        // spec.previewBackSplash (mirrors PyreWindow's own `backSplash` property) so it survives closing/reopening
        // the window instead of resetting every time.
        Laubrary.BackSplash.BackSplashSettings backSplash
        {
            get
            {
                if (spec == null) return null;
                spec.previewBackSplash ??= new Laubrary.BackSplash.BackSplashSettings();
                return spec.previewBackSplash;
            }
        }

        // Draggable resize bar on the preview's bottom edge — how tall the preview island is, in px, persisted
        // across domain reloads. Clamped so the preview can't be dragged away entirely or past a sane ceiling.
        [SerializeField] float previewHeight = 320f;
        const float PreviewHeightMin = 140f;
        const float PreviewHeightMax = 900f;

        // ── layer selection — PERSISTED ON THE ASSET (Pyre parity: Pyre.previewLayerSel) ─────────────
        // Which layer the Shape / Swarm / Matte / Modifiers sections below edit. It lives on the SPEC, not the
        // window, so reopening the window or coming back to an asset lands on the layer you were working on
        // instead of resetting. SelLayer clamps, so a stale index (a deleted layer) is always safe. Cosmetic:
        // the setter marks the asset dirty but never repaints the render.
        int layerSel
        {
            get => spec != null ? spec.previewLayerSel : 0;
            set
            {
                if (spec == null || spec.previewLayerSel == value) return;
                spec.previewLayerSel = value;
                EditorUtility.SetDirty(spec);
            }
        }
        PyrePlusLayer SelLayer
        {
            get
            {
                if (spec == null || spec.layers == null || spec.layers.Count == 0) return null;
                int clamped = Mathf.Clamp(spec.previewLayerSel, 0, spec.layers.Count - 1);
                if (clamped != spec.previewLayerSel) spec.previewLayerSel = clamped;
                return spec.layers[clamped];
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
            DestroyCherryStripCache();
            playback3DPreview?.Dispose(); playback3DPreview = null;
        }

        // Playback 3D (PROOF OF CONCEPT) — the PreviewRenderUtility-backed live preview, lazily created (see
        // PyrePlusPlayback3DPreview.cs). Disposed above on OnDisable; RefreshPlayback3DPreview just re-dirties the
        // preview repaint (the class itself re-simulates on every Render call, so there's no cache to invalidate).
        PyrePlusPlayback3DPreview playback3DPreview;
        void RefreshPlayback3DPreview() { preview?.MarkDirtyRepaint(); }
        protected override void OnAssetChanged()
        {
            frame = 0; previewDirty = true; DestroyStripCache(); DestroyCherryStripCache();
            layerSel = int.MaxValue;   // a NEW asset defaults to its last layer (BuildAsset clamps)
            NormalizeSolidDefaultFills();   // FIX 1 (initial build): once-per-load steady-fill migration for solids
            ResetCherryPlayback();
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
            while (acc >= 1f)
            {
                acc -= 1f;
                advanced = true;
                if (spec.cherryEnabled) CherryAdvanceOneBeat();
                else frame = (frame + 1) % Mathf.Max(1, spec.frameCount);
            }
            // In Strip mode the frames themselves don't change as playback advances — only the highlighted tile
            // moves — so DON'T set previewDirty (that would needlessly re-render every tile); just repaint. In
            // single-frame mode previewDirty forces the new frame to render.
            if (advanced) { if (!spec.previewStrip) previewDirty = true; preview?.MarkDirtyRepaint(); RefreshTransportReadout(); }
        }

        // ── CherryFraming playback (editor preview only — see PyrePlusWindow.CherryFraming.cs) ────────────
        // `frame` doubles as the resolved source frame CherryFraming plays; -1 is the BLANK sentinel (Preview.cs
        // reads it before clamping) used during the between-loop Delay. One "beat" = one tick of previewFps —
        // CherryAdvanceOneBeat is called once per beat from Tick above.
        int cherrySlot;             // index into spec.cherryFrames currently showing
        float cherryBeatsLeft;      // beats remaining before the current slot advances
        bool cherryDelayActive;     // true while blank between loop iterations (previewDelay > 0)
        float cherryDelayBeatsLeft;

        // Re-enter CherryFraming playback from slot 0 (or go blank if there are no slots). Call after ANY edit to
        // cherryFrames/cherryEnabled/previewDelay/frameCount so playback never holds a stale resolved frame or a
        // beat count computed against a slot that no longer exists.
        void ResetCherryPlayback()
        {
            cherrySlot = 0;
            cherryDelayActive = false;
            cherryDelayBeatsLeft = 0f;
            acc = 0f;
            if (spec != null && spec.cherryEnabled && spec.cherryFrames != null && spec.cherryFrames.Count > 0)
                EnterCherrySlot(spec.cherryFrames[0]);
            else if (spec != null)
                frame = Mathf.Clamp(frame, 0, Mathf.Max(0, spec.frameCount - 1));
            previewDirty = true;
            preview?.MarkDirtyRepaint();
            RefreshTransportReadout();
        }

        // One beat tick of CherryFraming playback: hold the current slot's resolved source frame for its resolved
        // length (in beats), then advance to the next slot. After the LAST slot, if previewDelay > 0 the preview
        // goes BLANK (frame = -1) for that long before looping back to slot 0; otherwise it loops immediately.
        void CherryAdvanceOneBeat()
        {
            var frames = spec.cherryFrames;
            if (frames == null || frames.Count == 0) { frame = -1; return; }

            if (cherryDelayActive)
            {
                cherryDelayBeatsLeft -= 1f;
                if (cherryDelayBeatsLeft > 0f) { frame = -1; return; }
                cherryDelayActive = false;
                cherrySlot = 0;
                EnterCherrySlot(frames[0]);
                return;
            }

            cherryBeatsLeft -= 1f;
            if (cherryBeatsLeft > 0f) return;   // still holding the current slot's frame

            cherrySlot++;
            if (cherrySlot >= frames.Count)
            {
                if (spec.previewDelay > 0f)
                {
                    cherryDelayActive = true;
                    cherryDelayBeatsLeft = Mathf.Max(1f, spec.previewDelay * Mathf.Max(1f, spec.previewFps));
                    frame = -1;
                    return;
                }
                cherrySlot = 0;
            }
            EnterCherrySlot(frames[cherrySlot]);
        }

        void EnterCherrySlot(CherryFrame slot)
        {
            frame = Mathf.Clamp(slot.PickSourceFrame(), 0, Mathf.Max(0, spec.frameCount - 1));
            cherryBeatsLeft = slot.ResolveLength();
            // A preview-only Zound cue fires once per loop when this slot's index matches previewZoundFrame.
            // Playback isn't wired up yet — see CHANGELOG / handover for the open item — the field just records
            // WHICH slot the user wants it on so the UI has somewhere to store the choice.
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

            BuildCanvas(flow, s);      // Canvas first — the output settings sit at the top of the dials
            BuildLayerList(flow);      // the compact layer stack — under the views bar, below Canvas
            BuildGlobalModifiers(flow, s);   // task #56 — spec-wide modifiers applied to EVERY layer (right after Layers)
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
            preview.style.height = Mathf.Clamp(previewHeight, PreviewHeightMin, PreviewHeightMax);
            preview.style.flexGrow = 0f;
            preview.style.flexShrink = 0f;
            preview.style.minWidth = 200f;
            preview.AddToClassList("zui-stage");
            rightPane.Add(preview);
            rightPane.Add(BuildPreviewResizeBar());

            // Transport (Play/Pause + Frame border) and the shared BackSplash backdrop panel sit below the preview.
            var chrome = new VisualElement();
            chrome.style.flexShrink = 0f;
            BuildTransport(chrome, s);
            BuildBackdropPanel(chrome);
            rightPane.Add(chrome);

            // CherryFraming (lower-right) — cherry-pick a sub-sequence of this spec's own baked frames. Grows to
            // fill whatever's left below the transport/backdrop chrome; scrolls internally when it overflows.
            BuildCherryPanel(rightPane, s);

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

        // A 6px draggable divider on the preview's BOTTOM edge — how tall the preview island is. UITK's `cursor`
        // style doesn't accept MouseCursor in this Unity version, so there's no cursor hint; the tooltip is the
        // only affordance.
        VisualElement BuildPreviewResizeBar()
        {
            var bar = new VisualElement { tooltip = "Drag to resize the preview vertically." };
            bar.style.height = 6f;
            bar.style.flexShrink = 0f;
            bar.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            bar.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { bar.CapturePointer(e.pointerId); e.StopPropagation(); } });
            bar.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!bar.HasPointerCapture(e.pointerId)) return;
                previewHeight = Mathf.Clamp(previewHeight + e.deltaPosition.y, PreviewHeightMin, PreviewHeightMax);
                if (preview != null) preview.style.height = previewHeight;
                e.StopPropagation();
            });
            bar.RegisterCallback<PointerUpEvent>(e => { if (bar.HasPointerCapture(e.pointerId)) bar.ReleasePointer(e.pointerId); });
            return bar;
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
            kids.Add(Z.MicroSlider("GIF scale", s.previewGifScale, 1f, 8f,
                "Nearest-neighbour upscale applied ONLY to the exported GIF (1–8×) — it does NOT change the live "
                + "preview, only the pixel size of the saved .gif file. Bigger = a larger file with the same crisp "
                + "pixels.",
                v =>
                {
                    if (spec == null) return;
                    Undo.RecordObject(spec, "Edit Pyre Plus");
                    s.previewGifScale = Mathf.Clamp(Mathf.RoundToInt(v), 1, 8);
                    EditorUtility.SetDirty(spec);
                }, 150f, showValue: true, decimals: 0));
            kids.Add(Z.Toggle("GIF dither",
                "GIF transparency is one bit — every pixel is either fully opaque or fully invisible, so a soft "
                + "edge has to be kept or dropped. On (recommended) stipples the partly-transparent band so soft "
                + "rims, smoke and fades still read as fading; off cuts them at 50% opacity, which turns a "
                + "feathered edge into a hard silhouette. Export only — the live preview is unaffected.",
                s.previewGifDither,
                v =>
                {
                    if (spec == null) return;
                    Undo.RecordObject(spec, "Edit Pyre Plus");
                    s.previewGifDither = v;
                    EditorUtility.SetDirty(spec);
                }));
            transportHost.Add(Z.HGroup(kids.ToArray()));   // Play + Frame/Strip/Tile/GIF/GIF-scale as one wrapping unit-row

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
            // Delay lives HERE (a transport concern — how long the preview holds blank between loop iterations),
            // not inside the CherryFraming list below, and it applies regardless of whether CherryFraming is on:
            // a plain preview loop pauses blank for this long before restarting from frame 0 too.
            var delayMs = Z.MicroSlider("Delay", s.previewDelay, 0f, 5f,
                "Seconds the preview holds BLANK between loop iterations before restarting. 0 = no gap.",
                v => Dirty(() => { s.previewDelay = Mathf.Clamp(v, 0f, 5f); ResetCherryPlayback(); }), 150f,
                showValue: true, decimals: 2);
            transportHost.Add(WrapRow(zoomMs, speedMs, delayMs, frameReadout));

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
            PyrePlusGif.Export(spec, path, Mathf.Clamp(spec.previewGifScale, 1, 8), spec.previewGifDither);
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
                + "has no effect on the render. Persisted per asset (spec.previewBackSplash) — closing and reopening "
                + "this window keeps it.",
                onChanged: () => preview?.MarkDirtyRepaint(),
                onStructureChanged: () => { preview?.MarkDirtyRepaint(); FillBackdropPanel(); },
                icon: "eye", owner: spec));
        }

        void BuildCanvas(VisualElement root, PyrePlusSpec s)
        {
            // Green-header Section (matching Shape / Swarm / Modifiers) rather than a framed BoxKeyed, so the
            // window's top-level sections read consistently. The stable key keeps the fold state from orphaning
            // on a title/tooltip reword (ZuiSection persists fold per key, same idiom as the box did).
            var box = Z.Section("Canvas", "The output resolution, frame count, seed and background.", "pyreplus.canvas",
                icon: "frame-corners");
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
            // Frames is a label-inside int MicroSlider (NOT a thumbed SliderInt): a thumbed slider here read as a
            // frame scrubber and the user kept grabbing it by mistake. The transport's frame SCRUBBER stays a
            // thumbed Z.SliderInt on purpose (Pyre1 parity) — this "how many frames to bake" count does not.
            // Per-shape "Life (frames)" ranges capture fcMax off frameCount at rebuild time (same reason Canvas
            // Size's controls do, see sizeSlider above), so committing a Frames change needs the same deferred,
            // coalesced Rebuild-on-pointer-up as Bug 1 — without it those ranges silently keep their stale max.
            var framesSlider = Z.MicroSlider("Frames", s.frameCount, 1f, 64f,
                "How many frames the animation bakes to.",
                v => Dirty(() => s.frameCount = Mathf.Clamp(Mathf.RoundToInt(v), 1, 64)), 150f,
                showValue: true, decimals: 0);
            framesSlider.RegisterCallback<PointerUpEvent>(_ => ScheduleRangeRebuild());
            box.Add(WrapRow(
                framesSlider,
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
            // Green-header Section (matching Canvas / Shape / Swarm / Modifiers) rather than a framed BoxKeyed,
            // so the top-level sections read consistently. The stable key keeps fold state from orphaning on a
            // title/tooltip reword (ZuiSection persists fold per key). Per-row Matte boxes inside stay ZuiBoxes,
            // so the saved-views bar still captures those.
            var box = Z.Section("Layers",
                "The paint stack — earlier (higher) layers composite BEHIND later (lower) ones. Click a layer to "
                + "edit its Shape / Swarm / Modifiers dials below; expand a row's Matte box to make it a stencil or "
                + "clip it by another layer's mask; drag the grip to reorder.",
                "pyreplus.layers", icon: "stack");
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

            // Per-row Duplicate — sits right after the Matte toggle (mirrors Pyre1's per-row "Dup"). Deep-clones
            // THIS row's layer via Clone(), inserts the copy just after it, and selects the copy. Undo-safe (one
            // Dirty); the "+ Add / Duplicate" toolbar row below duplicates the SELECTED layer — this is the
            // per-layer one the user asked for, by the matte toggle.
            row.Add(Z.Button("Dup", "Duplicate this layer just after itself (undoable).", () =>
            {
                Dirty(() =>
                {
                    var copy = layer.Clone();
                    copy.name = (layer.name ?? "Layer") + " copy";
                    int at = Mathf.Clamp(li + 1, 0, spec.layers.Count);
                    spec.layers.Insert(at, copy);
                    layerSel = at;
                });
                RebuildAllForSelection();
            }).W(40f));

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
                $"pyreplus.matte:{li}", "mask-happy");
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
            var sec = Z.Section("Shape", "The particle's own look — colour, opacity and size over its life.",
                icon: "shapes");
            // The shape-FORM picker lives in a header context menu (the caret button, or a right-click on the title)
            // instead of three in-body radio rows — reclaiming that vertical space.
            sec.SetHeaderMenu("caret-down", "Choose the shape form (or right-click the title).",
                anchor => ShowShapeMenu(SelLayer, anchor));
            shapeBody = new VisualElement();
            sec.Add(shapeBody);
            root.Add(sec);
            RebuildShape();
        }

        // Form picker — DISPLAY-ONLY subgroups (#59 Part B). The ShapeForm enum, its order and serialization are
        // UNCHANGED; this only clusters the buttons into labelled rows so the long flat list reads clearly. Each group
        // is (label, ShapeForm) pairs; picking any button sets the same s.shapeForm it always did. 3D = the true-3D lit
        // solids; 2D = the flat forms; Special = the standalone forms (Text / the two sims / Sparkle / Sprite). The five
        // task-listed singletons share ONE "Special" row rather than five caption+single-button rows — a singleton
        // would print its form name twice under its own caption, and one row is more compact (both per the layout rules).
        static readonly (string label, ShapeForm form, string icon)[] Forms3D =
        {
            ("Gem", ShapeForm.Gem, "diamond"), ("Box", ShapeForm.Box, "cube"), ("Pyramid", ShapeForm.Pyramid, "triangle"),
            ("Can", ShapeForm.Can, "cylinder"), ("Orb", ShapeForm.Orb, "sphere"),
        };
        static readonly (string label, ShapeForm form, string icon)[] Forms2D =
        {
            ("Disc", ShapeForm.Disc, "circle"), ("Crescent", ShapeForm.Crescent, "moon"), ("Ring", ShapeForm.Ring, "circle-dashed"),
            ("Streak", ShapeForm.Streak, "lightning"), ("Star", ShapeForm.Star, "star"), ("Polygon", ShapeForm.Polygon, "polygon"),
        };
        static readonly (string label, ShapeForm form, string icon)[] FormsSpecial =
        {
            ("Text", ShapeForm.Text, "text-aa"), ("Fire", ShapeForm.Fire, "flame"), ("Fireball", ShapeForm.Fireball, "fire"),
            ("Inferno", ShapeForm.Inferno, "bomb"),
            ("Fork Blast", ShapeForm.ForkBlast, "meteor"),
            ("Sparkle", ShapeForm.Sparkle, "sparkle"), ("Sprite", ShapeForm.Sprite, "image"),
            ("Playback 3D", ShapeForm.Playback3D, "play"),
        };
        const string Forms3DTip = "True-3D lit solids sharing the Gem's facet lighting (tilt, light, edge lines, glows). "
            + "Gem = a faceted crystal, Box = a cuboid, Pyramid = a square pyramid, Can = a cylinder, Orb = a sphere.";
        const string Forms2DTip = "Flat 2D forms. Disc = a soft disc, Crescent = a disc with an offset bite, Ring = a "
            + "tilted annulus (a Saturn ring), Streak = a comet-tail capsule, Star = a filled star polygon, Polygon = a "
            + "filled regular convex N-gon (triangle / square / hexagon /… by a sides count).";
        const string FormsSpecialTip = "Standalone forms. Text = a string as extruded SDF letters (one particle per "
            + "character). Fire / Fireball = stateful flame simulations (built-in emitters, no swarm). Inferno = a "
            + "volumetric fireball explosion — the Swarm detonates one blast per particle (off = one centred blast); "
            + "stateless, so scrubbing is exact. Fork Blast = a radial detonation built from hundreds of small "
            + "burning puffs (a gritty, particulate blast rather than Inferno's smooth cloud) — same Swarm-places-"
            + "the-blasts convention. Sparkle = twinkling lit cells. Sprite = a stamped image. Playback 3D "
            + "(PROOF OF CONCEPT) = a real 3D animation prefab (a ParticleSystem burst) played/scrubbed live in "
            + "the preview, with an optional pixelated downsample — NOT baked at runtime yet.";
        // Fireball's wedge mode — Mirror (alternate wedges reflected, a seam) / Repeat (each wedge the same, rotated).
        // Index 0 = Mirror (fireballMirror true), 1 = Repeat (false).
        static readonly string[] FireballMirrorChoices = { "Mirror", "Repeat" };
        // Fire's two arm modes — Mirror (symmetric) / Vary (each arm its own seed). Order matches FireArmMode.
        static readonly string[] FireArmModeChoices = { "Mirror", "Vary" };
        // Coalesce render-mode selector. Off / Fuse (slice 1, MetaBlob) / Ramp (slice 2, HeightBalls). Order matches
        // the LayerCoalesce enum (Off=0, Fuse=1, Ramp=2), so the MiniRadio index casts straight to the enum.
        static readonly string[] CoalesceChoices = { "Off", "Fuse", "Ramp" };
        static readonly List<string> TextFillModeChoices = new List<string> { "Per-char gradient", "Per-char step", "Text gradient" };

        // Build one labelled form-picker subgroup row (#59 Part B). `sel` is -1 when the current form isn't in this
        // group, so its MiniRadio shows NOTHING selected there; because every pick calls RebuildShape() (below), all
        // three rows are rebuilt on each change and exactly ONE ever shows a highlight. Picking maps the in-group index
        // straight to its ShapeForm; SteadyDefaultFillForSolid runs inside the SAME Dirty block so one Undo reverts both
        // the form and any auto-steadied fill together (the pre-existing FIX 1 behaviour, preserved).
        // The shape-FORM picker as a header context menu (opened by the "Shape" title's caret button or a right-click
        // on the title). The 3D / 2D / Special groups are stacked as three side-by-side COLUMNS, each form an
        // icon + label row with the current one checked. Picking sets s.shapeForm exactly as the old radio rows did.
        void ShowShapeMenu(PyrePlusLayer s, VisualElement anchor)
        {
            if (s == null) return;
            var menu = Z.Menu(anchor).Width(370f);
            menu.Custom((body, close) =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.Add(FormColumn(s, "3D", Forms3DTip, Forms3D, close));
                row.Add(FormColumn(s, "2D", Forms2DTip, Forms2D, close));
                row.Add(FormColumn(s, "Special", FormsSpecialTip, FormsSpecial, close));
                body.Add(row);
            });
            menu.Show();
        }

        VisualElement FormColumn(PyrePlusLayer s, string title, string tip,
            (string label, ShapeForm form, string icon)[] group, System.Action close)
        {
            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;
            col.style.marginRight = 12;
            col.style.minWidth = 104;
            var head = new Label(title) { tooltip = tip, pickingMode = PickingMode.Ignore };
            head.AddToClassList("zui-menu__section");
            col.Add(head);
            foreach (var (label, form, icon) in group)
            {
                var f = form;
                var item = new VisualElement { tooltip = tip };
                item.AddToClassList("zui-menu__item");
                item.style.flexDirection = FlexDirection.Row;
                item.style.alignItems = Align.Center;
                var check = new Label(s.shapeForm == f ? "✓" : "") { pickingMode = PickingMode.Ignore };
                check.AddToClassList("zui-menu__check");
                item.Add(check);
                var img = Z.Icon(icon, 14f);
                if (img != null) { img.pickingMode = PickingMode.Ignore; img.style.marginRight = 5f; item.Add(img); }
                var lbl = new Label(label) { pickingMode = PickingMode.Ignore };
                lbl.AddToClassList("zui-menu__label");
                item.Add(lbl);
                item.AddManipulator(new Clickable(() =>
                {
                    Dirty(() => { s.shapeForm = f; SteadyDefaultFillForSolid(s); });
                    RebuildShape(); RebuildSwarm();
                    close?.Invoke();
                }));
                col.Add(item);
            }
            return col;
        }

        void RebuildShape()
        {
            var s = SelLayer;   // every Shape control now edits the SELECTED layer's fields
            if (s == null || shapeBody == null) { shapeBody?.Clear(); return; }
            shapeBody.Clear();

            s.alpha ??= new ZUIValue(1f);
            s.shapeFill ??= new ZuiFill();   // defensive; the real OverLife-fire default comes from the spec factory

            // FORM selector — now a header context menu (the "Shape" title's caret button / right-click), not in-body
            // radio rows. See ShowShapeMenu; the ShapeForm enum + its 3D / 2D / Special grouping are unchanged.

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
            // Inferno sizes by its own Blast size (a fraction of the safe zone), so it hides Size too.
            if (s.shapeForm != ShapeForm.Streak && s.shapeForm != ShapeForm.Fire && s.shapeForm != ShapeForm.Fireball
                && s.shapeForm != ShapeForm.Inferno && s.shapeForm != ShapeForm.ForkBlast && s.shapeForm != ShapeForm.Playback3D)
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
                case ShapeForm.Polygon:
                    BuildPolygonRows(s);
                    break;
                case ShapeForm.Fire:
                    BuildFireBox(s);
                    break;
                case ShapeForm.Fireball:
                    BuildFireballBox(s);
                    break;
                case ShapeForm.Inferno:
                    BuildInfernoBox(s);
                    break;
                case ShapeForm.ForkBlast:
                    BuildForkBlastBox(s);
                    break;
                case ShapeForm.Playback3D:
                    BuildPlaybackBox(s);
                    break;
            }

            // First-class Border (task #60/#65) — the six flat 2D forms get an optional coloured rim. Shown ONLY for
            // them (the 3D solids have their own edge lines, Text its own border, and Sprite/Fire/Fireball/Sparkle get
            // none). Added AFTER the form-specific rows so it reads as part of the shape's look. Off = a single compact
            // toggle; on = a Border box (Width / Fill / Draw-over-matte).
            if (IsFlat2DBorderForm(s.shapeForm)) BuildBorderBox(s);

            // Fire, Fireball, Inferno and Fork Blast are whole-layer forms with no particles at all, so a Position
            // section there would be dead — skip it. Their Fill (the ramp) and Alpha (overall opacity) rows still apply.
            if (s.shapeForm == ShapeForm.Fire || s.shapeForm == ShapeForm.Fireball || s.shapeForm == ShapeForm.Inferno
                || s.shapeForm == ShapeForm.ForkBlast || s.shapeForm == ShapeForm.Playback3D) return;

            // Position (task #11) — one collapsible box grouping everything positional: the particle's rotation and
            // its Offset from spawn. Present for every particle form. A collapsible box already provides show/hide,
            // so there is no longer an "Advanced" toggle gating it (the old toggle only gated the UI — the renderer
            // always keyed travel off particlePathX/Y being non-static-zero, never off the shapeAdvanced flag).
            BuildPositionBox(s);
        }

        // The Position box (task #11) — the particle's orientation AND its Offset from the spawn position, grouped
        // into ONE collapsible box shown for every particle form (only the whole-layer Fire/Fireball sims are
        // excluded, above). Orientation is the 3D solids' Turn / Tilt / Roll trio (moved here out of the old Solid
        // box), or a single in-plane Spin for the flat forms and Text — all the SAME particleSpin field, just
        // labelled per form. Offset (formerly "Travel") is the per-particle path added to the spawn position; a
        // static value is a constant offset that causes no motion, which is why "Travel" was the wrong name. There
        // is deliberately no enable-toggle: a collapsible box already gives show/hide (ui-layout-rules — no extra
        // toggle when a section can collapse), and the renderer applies the offset whenever particlePathX/Y are not
        // static-zero, so always showing this is byte-identical to the old "Advanced" gate.
        void BuildPositionBox(PyrePlusLayer s)
        {
            s.particleSpin ??= new ZUIValue(0f);
            s.particlePathX ??= new ZUIValue(0f);
            s.particlePathY ??= new ZUIValue(0f);

            var box = Z.BoxKeyed("Position",
                "Where the particle sits and how it is turned, over its own life: its rotation (Turn / Tilt / Roll "
                + "for a 3D solid, Spin for a flat form) and its Offset from the position the swarm spawned it at. "
                + "Every field defaults to no rotation and no offset, so a fresh shape sits exactly where it was "
                + "placed.",
                "pyreplus.position");

            if (IsSolidForm(s.shapeForm))
            {
                s.gemTilt ??= new ZUIValue(18f);
                s.gemRoll ??= new ZUIValue(0f);
                // Turn (yaw = the shared particleSpin), Tilt (gemTilt) and Roll (gemRoll) — the three rotation axes
                // in plain words, each animatable over the particle's own life. Roll is geometrically inert for the
                // symmetric Ring (Turn + Tilt already shape its ellipse), so it's hidden there. Tooltips per form.
                box.Add(Val("Turn °", TurnTooltip(s.shapeForm), s.particleSpin, -1440f, 1440f, cyclic: true));
                box.Add(Val("Tilt °", TiltTooltip(s.shapeForm), s.gemTilt, -1440f, 1440f, cyclic: true));
                if (s.shapeForm != ShapeForm.Ring)
                    box.Add(Val("Roll °", RollTooltip(s.shapeForm), s.gemRoll, -1440f, 1440f, cyclic: true));
            }
            else
            {
                // The flat 2D forms + Text edit particleSpin as a single in-plane Spin. (For a 3D solid the same
                // field is shown once above as "Turn °", so it isn't repeated here.) Tooltip composed per form.
                box.Add(Val("Spin °", SpinTooltip(s.shapeForm), s.particleSpin, -720f, 720f, cyclic: true));
            }

            float half = Mathf.Max(1f, spec.canvasSize * 0.5f);
            box.Add(Val2D("Offset",
                "A per-particle positional OFFSET from the spawn position, in canvas pixels, sampled on the "
                + "particle's OWN life (0 = birth, 1 = death). Static = a constant offset (Static 0 = it stays where "
                + "it spawned); author a Curve to make the particle drift or arc as it lives.",
                s.particlePathX, s.particlePathY,
                new ZuiValue2DControl.Options().WithRange(-half, half, -half, half).WithDefault(Vector2.zero)));

            shapeBody.Add(box);
        }

        // The 3D-solid controls (task #13 restructure). What used to be ONE "Solid" box holding geometry + the
        // rotation trio + Light/Lines/Glow sub-sections is now split: the "Solid" box keeps only the per-form
        // GEOMETRY, while Light / Lines / Glow are their OWN sibling collapsible boxes (added straight into the Shape
        // body beside Solid, not nested under it). The rotation trio (Turn/Tilt/Roll) moved out to the Position box
        // (task #11). Shown for Gem / Box / Pyramid / Can / Orb / Ring. Colour, Alpha and Size stay above (shared).
        // All plain sliders are label-inside MicroSliders; the two glows are animatable ZUIValues (Val), each packed
        // with its own colour; Specular packs with its colour.
        void BuildSolidBox(PyrePlusLayer s)
        {
            s.gemEdgeGlow ??= new ZUIValue(0.5f);     // defensive; the real steady defaults come from the spec factories
            s.gemInnerGlow ??= new ZUIValue(0.35f);

            // ── Solid box: the per-form GEOMETRY only. BoxKeyed: view presets persist under this stable key —
            // retitling the box or rewording its tooltip must never orphan saved views. The Orb (a bare sphere) has
            // no geometry, so its Solid box would be empty — skip it there rather than show an empty box.
            var solid = Z.BoxKeyed("Solid", SolidBoxTooltip(s.shapeForm), "pyreplus.solid");

            // ── per-form geometry (Orb has none, so its Solid box is never added) ──
            if (s.shapeForm == ShapeForm.Gem)
            {
                solid.Add(Z.HGroup(
                    Z.MicroSlider("Sides", s.gemSides, 3f, 8f,
                        "Girdle vertex count — 4 is the classic octahedral gem; more sides make a rounder crystal.",
                        v => Dirty(() => s.gemSides = Mathf.Clamp(Mathf.RoundToInt(v), 3, 8)), 150f, showValue: true, decimals: 0),
                    Z.MicroSlider("Crown", s.gemCrown, 0.2f, 2.5f,
                        "Crown height (the top point) as a fraction of the gem's radius.",
                        v => Dirty(() => s.gemCrown = v), 150f, showValue: true),
                    Z.MicroSlider("Pavilion", s.gemPavilion, 0.2f, 2.5f,
                        "Pavilion depth (the bottom point) as a fraction of the gem's radius.",
                        v => Dirty(() => s.gemPavilion = v), 150f, showValue: true)));
                shapeBody.Add(solid);
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
                solid.Add(WrapRow(geo.ToArray()));
                shapeBody.Add(solid);
            }
            else if (s.shapeForm == ShapeForm.Ring)
            {
                // Ring — one geometry row: the hole radius. (Orb has NO geometry rows — a sphere needs none — so its
                // Solid box is skipped; it falls straight through to the Light / Lines / Glow boxes below.)
                solid.Add(Z.MicroSlider("Inner", s.ringInner, 0.1f, 0.92f,
                    "Inner radius as a fraction of the outer radius — the size of the ring's hole (0.1 = a nearly "
                    + "solid disc, 0.92 = a thin hoop).",
                    v => Dirty(() => s.ringInner = v), 150f, showValue: true));
                shapeBody.Add(solid);
            }

            // ── Light — its own sibling collapsible box (task #13), no longer a divider-subsection of Solid. The
            // rotation trio (Turn/Tilt/Roll) that used to sit here has moved to the Position box (task #11), and the
            // old ⚙ gear + per-control ToggleGroups are gone: Light / Lines / Glow are now three separate collapsible
            // boxes, so each box's own fold is its show/hide (a collapsible section already does that job).
            var light = Z.BoxKeyed("Light",
                "The single KEY light and how the surfaces respond to it. The key light is DIRECTIONAL — aim it on "
                + "the sphere; Ambient is a separate non-directional base light. The edge Lines and the Glows have "
                + "their OWN strengths and do NOT obey this light.",
                "pyreplus.solid.light");
            // Light DIRECTION + distance as ONE reusable Z.Direction3D control (ZUI #67): a draggable LIT SPHERE
            // gizmo (yaw × pitch) whose lit hotspot IS the readout, with numeric fallback fields, the distance as
            // its 3rd axis, and a larger 3D preview on hover / pin. It edits the SAME gemLightYaw/Pitch/Distance
            // fields (yaw −180..180, pitch −85..85, distance 1.5..8 — the control's option defaults), so every spec
            // renders byte-identical. Wrapped in a Z.Frame so it reads as one titled unit.
            const string lightDirTip = "The key light is DIRECTIONAL — aim it on the sphere. Yaw = which side it "
                + "comes FROM (left/right, −180..180°); Pitch = its height (−85..85°, negative brings it from "
                + "below/behind); Distance = how far off, in radii (closer = a tighter, brighter hotspot). Only the "
                + "lit faces and the specular hotspot follow it — the Lines and Glows have their own strengths and "
                + "do NOT obey the light. Hover (or pin) for a larger 3D preview.";
            light.Add(Z.Frame("Direction", lightDirTip,
                Z.Direction3D(s.gemLightYaw, s.gemLightPitch, s.gemLightDistance, lightDirTip,
                    (yaw, pitch, dist) => Dirty(() =>
                    {
                        s.gemLightYaw = yaw;
                        s.gemLightPitch = pitch;
                        s.gemLightDistance = Mathf.Clamp(dist, 1.5f, 8f);
                    }),
                    new ZuiDirection3D.Options { showDistance = true })));
            light.Add(WrapRow(
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
                    s.gemSpecularFill)));
            shapeBody.Add(light);

            // ── Lines — its own sibling collapsible box (task #13). The hard facet edge lines.
            var lines = Z.BoxKeyed("Lines", "The hard facet edge lines that catch the light.", "pyreplus.solid.lines");
            lines.Add(WrapRow(
                Z.MicroSlider("Line width", s.gemLineWidth, 0f, 3f,
                    "Width of the hard facet edge lines in pixels (0 = no lines). The lines catch the key light.",
                    v => Dirty(() => s.gemLineWidth = v), 150f, showValue: true),
                SlotFill("Line fill", "Fill for the facet edge lines — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemLineFill)));
            shapeBody.Add(lines);

            // ── Glow — its own sibling collapsible box (task #13). A rim halo + an interior glow.
            var glow = Z.BoxKeyed("Glow",
                "A rim halo and an interior glow — steady by default (author a Curve to pulse), each with its own fill.",
                "pyreplus.solid.glow");
            glow.Add(WrapRow(
                Val("Edge glow",
                    "Strength (0-1) of the halo around the edge lines, over the particle's OWN life; it spills "
                    + "OUTSIDE the solid's silhouette. Static = a steady glow (the default); author a Curve to make "
                    + "it pulse over the particle's life.",
                    s.gemEdgeGlow, 0f, 1f),
                SlotFill("Edge fill", "Fill for the edge-line halo glow — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemEdgeGlowFill)));
            glow.Add(WrapRow(
                Val("Inner glow",
                    "Strength (0-1) of the emissive glow rising from the facet interiors, over the particle's OWN "
                    + "life; interior only. Static = a steady glow (the default); author a Curve to make it pulse "
                    + "over the particle's life.",
                    s.gemInnerGlow, 0f, 1f),
                SlotFill("Inner fill", "Fill for the facet inner glow — Solid, or a gradient/spatial fill (alpha-capable).",
                    s.gemInnerGlowFill)));
            shapeBody.Add(glow);
        }

        // The Edge-softness control — now a MultiCont (Static / Min-Max / Curve over the particle's own life, #12),
        // seeded Static(edgeSoftness) the first time it's built so an un-migrated asset stays byte-identical. Shared
        // by Disc (its single rim), Crescent (both rims) and the other flat 2D forms, each passing its own tooltip.
        VisualElement EdgeRow(PyrePlusLayer s, string tooltip)
        {
            s.edgeSoftnessAnim ??= new ZUIValue(s.edgeSoftness);
            return Val("Edge", tooltip, s.edgeSoftnessAnim, 0f, 1f);
        }

        // Crescent form rows — the shared Edge row (drives BOTH rims), the bite size, then the mask-disc CENTRE as a
        // 2D pad (#12 part 2, replacing the old polar Offset + Angle).
        void BuildCrescentRows(PyrePlusLayer s)
        {
            s.crescentBite ??= new ZUIValue(0.55f);
            s.crescentAngle ??= new ZUIValue(0f);

            shapeBody.Add(EdgeRow(s,
                "Soft rim (1) vs a hard pixel edge (0). For the Crescent it feathers BOTH rims — the outer disc "
                + "edge and the bite edge."));
            shapeBody.Add(Val("Bite",
                "Size of the disc bitten out of the main disc, as a fraction of its radius (0 = no bite, a "
                + "full disc; 1 = a bite as wide as the disc), over the particle's own life.",
                s.crescentBite, 0f, 1f));
            shapeBody.Add(CrescentCenterPad(s));
        }

        // The mask-disc CENTRE as a 2D pad (#12 part 2), replacing the polar Offset + Angle. The pad is SEEDED for
        // display from the legacy polar values, but only CONVERTS — writing crescentCenterX/YAnim — on an explicit
        // drag, so an untouched crescent keeps rendering through the byte-identical polar path (a silent
        // polar→cartesian seed is not byte-identical: (off·radius)·cos vs (off·cos)·radius differ by multiply order).
        VisualElement CrescentCenterPad(PyrePlusLayer s)
        {
            bool live = s.crescentCenterXAnim != null && s.crescentCenterYAnim != null;
            float angRad = (s.crescentAngle != null ? s.crescentAngle.staticValue : 0f) * Mathf.Deg2Rad;
            float off = Mathf.Clamp01(s.crescentOffset);
            var cx = live ? s.crescentCenterXAnim : new ZUIValue(off * Mathf.Cos(angRad));
            var cy = live ? s.crescentCenterYAnim : new ZUIValue(off * Mathf.Sin(angRad));
            var o = new ZuiValue2DControl.Options().WithRange(-1f, 1f, -1f, 1f).WithDefault(Vector2.zero);
            return Z.Value2D("Mask", cx, cy, o,
                "Where the bitten-out mask disc sits, as (x,y) in radius units from the drawn disc's centre (-1..1), "
                + "over the particle's own life. (0,0) = the bite dead-centre (a hole/ring); push it out for a "
                + "thinner sliver of a crescent. Replaces the old Offset + Angle.",
                () => { if (s.crescentCenterXAnim == null) { s.crescentCenterXAnim = cx; s.crescentCenterYAnim = cy; } MarkDirty(); },
                () => Undo.RecordObject(spec, "Edit Pyre Plus"));
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

        // Polygon form rows — a flat filled regular convex N-gon (#59 Part A). Just the Sides count (3..12) + the
        // shared Edge (rim softness) row; the shared Size row above stays visible (it's the circumradius the vertices
        // reach to). Sides is a bounded int scalar ⇒ a MicroSlider (matching the Star's Arms row), never a bare field.
        void BuildPolygonRows(PyrePlusLayer s)
        {
            shapeBody.Add(Z.MicroSlider("Sides", s.polygonSides, 3f, 12f,
                "How many sides the polygon has (3–12): 3 = a triangle, 4 = a square, 5 = a pentagon, 6 = a hexagon,… "
                + "An even count rests on a flat edge (a square sits flat, not a diamond); an odd count points a vertex "
                + "up (an upright triangle/pentagon). Radius is set by Size (px), above.",
                v => Dirty(() => s.polygonSides = Mathf.Clamp(Mathf.RoundToInt(v), 3, 12)), 150f,
                showValue: true, decimals: 0));

            shapeBody.Add(EdgeRow(s,
                "Soft rim (1) vs a hard pixel edge (0) — feathers the polygon's whole outline inward along each ray."));
        }

        // Fire form box — the stateful flame SIMULATION (slice 6a). Every rate is an envelope over the LAYER's life
        // (Fire has no particles — the whole layer IS the sim), grouped Emitter → Heat & fuel → Motion → Flame shape →
        // Confinement → Output. Its colour ramp is the shared Shape Fill above (smoke→fire) and its overall opacity is
        // the shared Shape Alpha; `size` and the Swarm don't apply (both hidden/noted). Reuses Pyre's own FireSim via
        // the renderer's replay harness — the dials here map 1:1 onto Pyre's Fire fields.
        // Inferno form box — the STATELESS volumetric fireball explosion (closed-form; see PlusInferno.cs). Every
        // frame is a pure function of (dials, life, seed) — no replay harness — so the transport scrub IS the
        // explosion's progress control. Grouped Pattern → Blast → Cloud → Motion → Fire & smoke → Light; its colour
        // ramp is the shared Shape Fill above (LEFT end = white-hot core) and its overall opacity the shared Shape
        // Alpha; `size` and the Swarm don't apply (the Pattern places the blasts). All dials are plain bounded
        // scalars (the prototype's model — time behaviour is baked into the form itself), so every row is a
        // MicroSlider, packed 2–3 per row per the layout rules.
        void BuildInfernoBox(PyrePlusLayer s)
        {
            // Defensive nulls for the animatable envelopes (real defaults come from the field initializers).
            s.infernoProgress ??= new ZUIValue(1f);
            s.infernoBlastSize ??= new ZUIValue(0.82f);
            s.infernoFlash ??= new ZUIValue(0.72f);
            s.infernoClumpSpread ??= new ZUIValue(0.52f);
            s.infernoBillow ??= new ZUIValue(0.72f);
            s.infernoJagged ??= new ZUIValue(0.53f);
            s.infernoCohesion ??= new ZUIValue(0.78f);
            s.infernoHollow ??= new ZUIValue(0f);
            s.infernoHollowRim ??= new ZUIValue(0.5f);
            s.infernoOuterRim ??= new ZUIValue(0f);
            s.infernoCoreDensity ??= new ZUIValue(0.5f);
            s.infernoChurn ??= new ZUIValue(0.68f);
            s.infernoRotation ??= new ZUIValue(0.22f);
            s.infernoFire ??= new ZUIValue(0.79f);
            s.infernoCoreGlow ??= new ZUIValue(0.4f);
            s.infernoSmoke ??= new ZUIValue(0.66f);
            s.infernoSmokeSpread ??= new ZUIValue(0.45f);
            s.infernoDarkness ??= new ZUIValue(0.64f);
            s.infernoBody ??= new ZUIValue(0.75f);
            s.infernoEdgeSoftness ??= new ZUIValue(0.3f);

            var box = Z.BoxKeyed("Inferno",
                "A volumetric fireball explosion: contained blasts built from big billowing lobes, a torn "
                + "silhouette, an ignition flash, embers, a smouldering afterglow and pseudo-3D lighting. "
                + "Closed-form (not a sim), so any frame is exact — which is what lets the Progress envelope "
                + "remap the whole timeline. The SWARM places the blasts: off = one centred blast, on = one "
                + "blast per swarm particle. Colour comes only from the Shape Fill (left end = white-hot); "
                + "most dials are envelopes over the layer's life like every other form.", "pyreplus.inferno");

            // A bounded scalar dial → one MicroSlider (label + value inside the track), Dirty-wrapped.
            VisualElement MS(string label, float value, float min, float max, string tip,
                             System.Action<float> set, int decimals = -1)
                => Z.MicroSlider(label, value, min, max, tip, v => Dirty(() => set(v)), 150f,
                                 showValue: true, decimals: decimals);

            // Progress — the explosion's CLOCK, remapping the layer's life onto internal time. Identity (the
            // default straight line) = real time; bend it to snap in and hold at full bloom, slow the smoky
            // tail, or freeze a pose (a Static value).
            box.Add(Val("Progress",
                "The explosion's progress over the layer's life — a TIME REMAP as one envelope. The default "
                + "straight line plays in real time; bend it to snap in and hold at full bloom, slow the smoky "
                + "tail, or play sections at different speeds. A Static value freezes the explosion at that moment "
                + "as a pose.",
                s.infernoProgress, 0f, 1f));

            // ── Multi-blast dials — the SWARM ignites the blasts (one per particle), so these show only with
            // the Swarm on; off = one centred blast and they'd be dead controls. How to get many blasts lives
            // in the box/section TOOLTIPS (never an on-screen instruction label — UI-Guide "tooltip, not title").
            if (s.swarmEnabled)
            {
                box.Add(Z.HGroup(
                    MS("Mutation", s.infernoMutation, 0f, 1f,
                        "Per-blast variation of size, torque, heat and jaggedness — 0 makes every blast a twin.",
                        v => s.infernoMutation = v),
                    MS("Heat stacking", s.infernoAccumulation, 0f, 1f,
                        "How strongly overlapping blasts ADD heat instead of replacing one another — what "
                        + "makes overlapping blasts glow hotter than either alone.",
                        v => s.infernoAccumulation = v)));
                box.Add(Z.HGroup(
                    MS("Character drift", s.infernoBalanceDrift, -1f, 1f,
                        "Slides each blast's fire-vs-smoke character across the sequence: negative = the first "
                        + "blasts burn fierier and later ones turn sootier, positive = the reverse. 0 = every "
                        + "blast takes the Fire and Smoke dials as-is.",
                        v => s.infernoBalanceDrift = v),
                    MS("Character jitter", s.infernoBalanceJitter, 0f, 1f,
                        "Randomises each blast's fire-vs-smoke character — some fierier, some sootier, no order.",
                        v => s.infernoBalanceJitter = v)));
            }

            // ── Blast — the detonation's envelope ──
            box.Add(Z.HGroup(
                Val("Blast size",
                    "Final occupied radius inside the safe zone, over the layer's life.",
                    s.infernoBlastSize, 0.05f, 1f),
                Val("Flash",
                    "White-hot ignition flash and central punch at each blast's birth, over the layer's life. "
                    + "Tinted from the Fill's hot end.",
                    s.infernoFlash, 0f, 1f)));
            box.Add(Z.HGroup(
                MS("Bang speed", s.infernoBangSpeed, 0.2f, 1f,
                    "How abruptly the first expansion happens — 1 is a violent snap (frames, not a bloom).",
                    v => s.infernoBangSpeed = v),
                MS("Punch", s.infernoPunch, 0f, 1f,
                    "DRAMA, opt-in: the bang overshoots its radius and settles back, ignition spikes the heat "
                    + "white-hot, and the flash blows out bigger. 0 = the calm prototype look.",
                    v => s.infernoPunch = v)));
            box.Add(Z.HGroup(
                MS("Flash reach", s.infernoFlashReach, 0f, 1f,
                    "How FAR the flash reaches out from the blast centre — its size, set independently of how "
                    + "bright it is.",
                    v => s.infernoFlashReach = v),
                MS("Flash softness", s.infernoFlashSoft, 0f, 1f,
                    "How gradually the flash's alpha fades out into the cloud: 0 = a tight core with a crisp "
                    + "edge, 1 = a broad soft glow. (WHEN it fades is the Flash envelope's job.)",
                    v => s.infernoFlashSoft = v)));
            box.Add(MS("Recoil", s.infernoRecoil, 0f, 1f,
                "Pulls the outer shape back in after the blast.",
                v => s.infernoRecoil = v));

            // ── Containment — the blast's own rim softness vs the canvas-border guard (two different jobs) ──
            box.Add(Val("Edge softness",
                "How far the cloud's own rim fades out, in absolute canvas terms — so the fade looks the same "
                + "whether the blast is tiny or huge. Low reads as a hard-edged solid; raise it for gas.",
                s.infernoEdgeSoftness, 0f, 1f));
            box.Add(Z.HGroup(
                MS("Safe margin", s.infernoMargin, 0f, 0.25f,
                    "Minimum empty border around the effect, as a fraction of the canvas — nothing is drawn "
                    + "past it, so the effect can never touch the frame edge.",
                    v => s.infernoMargin = v),
                MS("Frame fade", s.infernoFrameFade, 0f, 1f,
                    "How wide the fade-to-nothing is as the cloud nears the Safe margin — anything close to the "
                    + "border dissolves instead of being cut. The band grows inward, so the frame edge itself is "
                    + "always fully transparent.",
                    v => s.infernoFrameFade = v)));

            // ── Cloud shape ──
            box.Add(Z.HGroup(
                MS("Clumps", s.infernoClumps, 1f, 9f,
                    "Large coherent lobes, merged into one cloud.",
                    v => s.infernoClumps = Mathf.Clamp(Mathf.RoundToInt(v), 1, 9), decimals: 0),
                Val("Clump spread",
                    "Separates the hot lobes without breaking cohesion, over the layer's life.",
                    s.infernoClumpSpread, 0f, 1f)));
            box.Add(Z.HGroup(
                Val("Billow",
                    "Strength of the rolling, 3D-looking cloud pockets, over the layer's life — what keeps the "
                    + "cloud from reading as a flat slab. The usable range now runs well past the old ceiling.",
                    s.infernoBillow, 0f, 1f),
                Val("Jagged",
                    "Breaks the perfect circle into torn explosive lobes, over the layer's life.",
                    s.infernoJagged, 0f, 1f)));
            box.Add(Z.HGroup(
                Val("Core density",
                    "Keeps the MIDDLE of the cloud thick over the layer's life. The cavity and noise detail bite "
                    + "hardest where the cloud is thickest, which thins (or holes) the centre without this.",
                    s.infernoCoreDensity, 0f, 1f),
                Val("Cohesion",
                    "Higher keeps all clumps visibly connected as one mass, over the layer's life — fall to a "
                    + "low value late and the cloud visibly blows apart into fragments.",
                    s.infernoCohesion, 0f, 1f)));
            box.Add(Z.HGroup(
                Val("Hollow",
                    "Carves the cloud's core out into a cavity over the layer's life, leaving a burning shell — "
                    + "0 = solid, high = a ring/torus of fire. Animate it to make the cloud bloom open into a ring.",
                    s.infernoHollow, 0f, 1f),
                Val("Hollow rim",
                    "Heat concentrated on the cavity's INNER boundary over the layer's life, so the shell "
                    + "visibly burns. Only acts once Hollow is raised.",
                    s.infernoHollowRim, 0f, 1f)));
            box.Add(Val("Outer rim",
                "Heat concentrated on the cloud's OUTER rim over the layer's life — a burning surface instead "
                + "of an evenly lit disc. Animate it to have the shell ignite and cool.",
                s.infernoOuterRim, 0f, 1f));

            // ── Churn & motion ──
            box.Add(Z.HGroup(
                Val("Churn",
                    "Rolling internal displacement, over the layer's life.",
                    s.infernoChurn, 0f, 1f),
                Val("Rotation",
                    "Rotational torque, either way (−1..1), over the layer's life; 0 = none.",
                    s.infernoRotation, -1f, 1f, cyclic: true)));
            box.Add(MS("Pulse", s.infernoPulse, 0f, 1f,
                "A secondary compression wave that breathes the SAME blast in and out after the bang — it does "
                + "not add a second explosion (use more swarm particles for that).",
                v => s.infernoPulse = v));

            // ── Fire ── (its own box: how much of the cloud burns, and how that heat behaves)
            var fireBox = Z.BoxKeyed("Fire",
                "How much of the cloud BURNS and how that fire behaves. Fire sets the amount, Heat pockets vary "
                + "it inside the cloud, Cooling turns it to smoke over each blast's life, and Core glow is a "
                + "separate inner glow that survives the cooling.", "pyreplus.inferno.fire");
            fireBox.Add(Val("Fire",
                "How much of the cloud is FLAME, over the layer's life: 0 = no fire at all (pure smoke); "
                + "1 = fire fills most of the dense regions.",
                s.infernoFire, 0f, 1f));
            fireBox.Add(Z.HGroup(
                MS("Heat pockets", s.infernoHeatPockets, 0f, 1f,
                    "Varies the heat WITHIN the fire — internal boiling regions. (Clumps shape the cloud's mass; "
                    + "this only changes how hot each part of it burns.)",
                    v => s.infernoHeatPockets = v),
                MS("Cooling", s.infernoCooling, 0f, 1f,
                    "How quickly flame turns into dark smoke over each blast's own life — the fire→smoke rate.",
                    v => s.infernoCooling = v)));
            fireBox.Add(Val("Core glow",
                "An inner glow at the cloud's core over the layer's life, added after Cooling so it survives it "
                + "— the smoulder left inside the smoke. Its timing is entirely this envelope's: flat glows "
                + "throughout, a curve swells and dies exactly when you draw it.",
                s.infernoCoreGlow, 0f, 1f));
            box.Add(fireBox);

            // ── Smoke ── (its own box: how much soot, how dark, how far it reaches, how long it stays)
            var smokeBox = Z.BoxKeyed("Smoke",
                "How much SOOT there is and how it reads. Smoke sets the amount, Spread pushes it out past the "
                + "flame to frame it, Darkness colours it, Linger holds it to the end, and Body decides how "
                + "solid the whole cloud looks.", "pyreplus.inferno.smoke");
            smokeBox.Add(Z.HGroup(
                Val("Smoke",
                    "How much SOOT there is, over the layer's life — the one dial for the amount of smoke.",
                    s.infernoSmoke, 0f, 1f),
                Val("Spread",
                    "How far the soot reaches BEYOND the fire, over the layer's life — the shell of smoke that "
                    + "frames the flame and feathers into the background instead of stopping at its silhouette.",
                    s.infernoSmokeSpread, 0f, 1f)));
            smokeBox.Add(Z.HGroup(
                Val("Darkness",
                    "Heavier, darker soot over the layer's life. SMOKE ONLY — burning pixels take the Fill "
                    + "ramp's colour outright, so this never tints the flame.",
                    s.infernoDarkness, 0f, 1f),
                MS("Linger", s.infernoLinger, 0f, 1f,
                    "How long the smoke STAYS: 0 = fades out over the last frames; 1 = persists to the very end "
                    + "of the timeline. The shared Alpha envelope above also fades the tail by default — flatten "
                    + "it for smoke that holds to the last frame.",
                    v => s.infernoLinger = v)));
            smokeBox.Add(Val("Body",
                "How OPAQUE the thick of the cloud reads, over the layer's life: 0 = ghostly gas, 1 = dense "
                + "fire and smoke read as solid matter. Affects flame and soot alike.",
                s.infernoBody, 0f, 1f));
            box.Add(smokeBox);

            // ── Finish ──
            box.Add(Z.HGroup(
                MS("Die out", s.infernoDieOut, 0f, 1f,
                    "Dissolves the whole effect to nothing over the final fraction of the timeline, so it ends "
                    + "on its own instead of running until the last frame cuts it off. 0 = no forced ending.",
                    v => s.infernoDieOut = v),
                MS("Embers", s.infernoEmbers, 0f, 1f,
                    "Short contained sparks that arc out and fade before the border.",
                    v => s.infernoEmbers = v)));
            box.Add(Z.HGroup(
                MS("Lighting", s.infernoLighting, 0f, 1f,
                    "Pseudo-3D shading from the cloud's own density — carves lit billows and shadowed pockets.",
                    v => s.infernoLighting = v),
                MS("Contrast", s.infernoContrast, 0f, 1f,
                    "Separates hot cavities from dark billows on the heat ramp.",
                    v => s.infernoContrast = v)));

            shapeBody.Add(box);
        }

        // Playback 3D form box (PROOF OF CONCEPT) — configures a real 3D animation prefab (typically a
        // ParticleSystem fire/explosion burst) that the preview island plays/scrubs live via
        // PyrePlusPlayback3DPreview (see that file), instead of the normal composited 2D canvas. There is no
        // runtime bake for this form yet (PyrePlusRenderer's Playback3D case is a documented stub) — everything
        // here only drives the EDITOR preview. The prefab is picked via an object-reference field (never typed by
        // name, per the "never type a reference string" rule); its ParticleSystem(s) are driven directly by
        // Speed/Scale/Tint through PreviewRenderUtility, no reflection needed (ParticleSystem.MainModule exposes
        // all of them).
        void BuildPlaybackBox(PyrePlusLayer s)
        {
            var box = Z.BoxKeyed("Playback 3D (POC)",
                "PROOF OF CONCEPT: plays a real 3D animation prefab (a ParticleSystem-based burst, e.g. fire or "
                + "an explosion) live in the preview instead of a procedural shape. Speed/Scale/Tint drive the "
                + "prefab's ParticleSystem main module directly. There is no runtime bake for this form yet — it "
                + "is editor-preview only.", "pyreplus.playback3d");

            box.Add(Z.Field("Prefab",
                "The 3D content to preview — a prefab carrying one or more ParticleSystem components. Only its "
                + "ParticleSystem(s) are driven; other components are inert in this preview.",
                Z.Object<GameObject>(s.playbackPrefab, "Pick a prefab with a ParticleSystem (a fire/explosion "
                    + "burst works best).", v => { Dirty(() => s.playbackPrefab = v); RefreshPlayback3DPreview(); }, 190f)));

            box.Add(Z.HGroup(
                Z.MicroSlider("Speed", s.playbackSpeed, 0.05f, 4f,
                    "Multiplies the prefab's ParticleSystem simulation speed. 1 = authored speed.",
                    v => Dirty(() => s.playbackSpeed = v), 150f, showValue: true),
                Z.MicroSlider("Scale", s.playbackScale, 0.05f, 5f,
                    "Uniform scale applied to the instantiated prefab (also scales each ParticleSystem's start size).",
                    v => Dirty(() => s.playbackScale = v), 150f, showValue: true)));

            box.Add(Z.MicroSlider("Zoom", s.playbackZoom, 0.25f, 4f,
                "Preview framing. The camera automatically frames the dense body of the effect (a full-extent fit "
                + "would shrink the fire to a dot to fit the smoke column and stray sparks); this zooms in above 1 "
                + "or pulls back below 1 from that fit — pull back to bring a tall smoke plume into frame.",
                v => { Dirty(() => s.playbackZoom = v); RefreshPlayback3DPreview(); }, 150f, showValue: true));

            box.Add(Z.HGroup(
                Z.Field("Tint",
                    "Multiplied into the finished, graded image. White leaves the prefab's own authored colours "
                    + "completely untouched — it is deliberately NOT written into the particle systems themselves, "
                    + "which would flatten the pack's per-particle colour gradients.",
                    Z.Color(s.playbackTint, "Tint colour multiplied into the finished preview image.",
                        v => { Dirty(() => s.playbackTint = v); RefreshPlayback3DPreview(); }, 130f)),
                Z.MicroSlider("Glow", s.playbackGlow, 0f, 3f,
                    "Strength of the bloom/glow pass. VFX packs author fire far brighter than white and ship a "
                    + "Bloom volume with their demo scene — without a glow pass the same particles read as flat, "
                    + "clipped colour. 1 matches the pack's own demo-scene setting; 0 turns the glow off.",
                    v => { Dirty(() => s.playbackGlow = v); RefreshPlayback3DPreview(); }, 150f, showValue: true)));

            box.Add(Z.HGroup(
                Z.MicroSlider("Scrub", s.playbackScrub01, 0f, 1f,
                    "Where in the loop the preview scrubs to (0 = the moment it starts emitting, 1 = one full "
                    + "Loop duration later). Re-simulates from 0 up to this point every time it changes.",
                    v => Dirty(() => s.playbackScrub01 = v), 150f, showValue: true),
                Z.MicroSlider("Loop (s)", s.playbackLoopDuration, 0.1f, 10f,
                    "How long (seconds) one preview loop is — the range Scrub maps across, and the length Play loops over.",
                    v => Dirty(() => s.playbackLoopDuration = v), 150f, showValue: true, decimals: 2)));

            box.Add(Z.HGroup(
                Z.Toggle("Pixelated preview", "Switch the preview from the live 3D render to a downsampled, "
                    + "point-filtered pixel grid — a rough gauge of how a baked pixel-art version might read. "
                    + "Preview only; does not affect the (unbaked) runtime form.",
                    s.playbackPixelated, v => Dirty(() => s.playbackPixelated = v)),
                Z.Field("Pixel grid", "Pixel grid resolution along the preview's LONG edge. The short edge follows "
                    + "the preview's aspect, so the effect keeps its proportions instead of being squashed.",
                    Z.Int(s.playbackPixelGrid, "Pixel grid resolution along the preview's long edge when "
                        + "Pixelated preview is on.",
                        v => { Dirty(() => s.playbackPixelGrid = Mathf.Clamp(v, 8, 128)); RefreshPlayback3DPreview(); }, 60f)),
                Z.Field("Colours", "Colour levels per channel in the pixelated preview. 0 = off — a straight "
                    + "shrink, which reads as a small photo rather than pixel art. Lower values band the fire into "
                    + "flat, authored-looking colour steps.",
                    Z.Int(s.playbackPixelLevels, "Colour levels per channel (0 = off).",
                        v => { Dirty(() => s.playbackPixelLevels = Mathf.Clamp(v, 0, 32)); RefreshPlayback3DPreview(); }, 60f))));

            shapeBody.Add(box);
        }

        void BuildForkBlastBox(PyrePlusLayer s)
        {
            // Defensive nulls for the animatable envelopes (real defaults come from the field initializers).
            s.forkProgress ??= new ZUIValue(1f);
            s.forkReach ??= new ZUIValue(0.85f);
            s.forkFlash ??= new ZUIValue(0.7f);

            var box = Z.BoxKeyed("Fork Blast",
                "A radial detonation built from hundreds of small burning puffs thrown outward and shaped by "
                + "drag/entrainment — a gritty, particulate blast rather than Inferno's smooth volumetric cloud. "
                + "Closed-form (not a sim), so any frame is exact. The SWARM places the blasts: off = one centred "
                + "detonation, on = one blast per swarm particle. Colour comes only from the Shape Fill (left end "
                + "= white-hot); its own alpha ramp doubles as the body's opacity ceiling.", "pyreplus.forkblast");

            VisualElement MS(string label, float value, float min, float max, string tip,
                             System.Action<float> set, int decimals = -1)
                => Z.MicroSlider(label, value, min, max, tip, v => Dirty(() => set(v)), 150f,
                                 showValue: true, decimals: decimals);

            box.Add(Val("Progress",
                "The blast's progress over the layer's life — a TIME REMAP as one envelope. The default straight "
                + "line plays in real time; bend it to snap in and hold, slow the tail, or freeze a pose (a Static "
                + "value).",
                s.forkProgress, 0f, 1f));
            box.Add(Z.HGroup(
                Val("Reach", "How far the fastest puffs travel, as a fraction of the canvas half-extent, over life.",
                    s.forkReach, 0.1f, 1.5f),
                Val("Flash", "White-hot ignition flash at each blast's birth, over life. Tinted from the Fill's "
                    + "hot end.", s.forkFlash, 0f, 1f)));

            if (s.swarmEnabled)
                box.Add(Z.Text(
                    "Multiple blasts: the Swarm places them (Count / shape / spawn-timing) — one blast per particle.",
                    ZuiText.Small));

            // ── Emission shape ──
            box.Add(Z.HGroup(
                MS("Spread", s.forkSpread, 0f, 180f,
                    "Half-angle of the emission arc, degrees. 180 = a full circle (a true radial blast).",
                    v => s.forkSpread = v, decimals: 0),
                MS("Aim", s.forkAim, -180f, 180f,
                    "The arc's centre direction, degrees. Only visible when Spread is below 180 (a full circle "
                    + "has no facing).",
                    v => s.forkAim = v, decimals: 0)));
            box.Add(Z.HGroup(
                MS("Bias", s.forkBias, 0.3f, 3f,
                    "Angle-distribution power across the arc. 1 = uniform coverage — keep this near 1 on a full "
                    + "circle, or the puffs pile back into a beam.",
                    v => s.forkBias = v),
                MS("Puffs", s.forkPuffCount, 20f, 500f,
                    "Puffs per blast.",
                    v => s.forkPuffCount = Mathf.RoundToInt(v), decimals: 0)));

            // ── Puff physics — travel, growth, shape ──
            box.Add(Z.HGroup(
                MS("Drag", s.forkDrag, 0.3f, 6f,
                    "Higher decelerates a puff sooner, so it stalls closer to the source.",
                    v => s.forkDrag = v),
                MS("Buoyancy", s.forkBuoy, 0f, 0.4f,
                    "Upward rise late in a puff's life.",
                    v => s.forkBuoy = v)));
            box.Add(Z.HGroup(
                MS("Growth", s.forkGrowth, 0f, 0.3f,
                    "Radius gained per pixel travelled (entrainment) — a puff fattens as it slows.",
                    v => s.forkGrowth = v),
                MS("Swell", s.forkSwell, 0f, 1.2f,
                    "Radius gained per unit AGE rather than distance — fills a stalled centre so the fireball "
                    + "doesn't hollow into a smoke ring.",
                    v => s.forkSwell = v)));
            box.Add(Z.HGroup(
                MS("Elongation", s.forkElong, 0f, 4f,
                    "Extra length/width at birth, along the puff's own travel direction; decays as it slows.",
                    v => s.forkElong = v),
                MS("Round at", s.forkRoundAt, 0.02f, 0.9f,
                    "The age by which a puff has stopped stretching and is round again.",
                    v => s.forkRoundAt = v)));
            box.Add(Z.HGroup(
                MS("Jitter", s.forkJitter, 0.3f, 4f,
                    "Per-puff variation in speed / size / amplitude / life.",
                    v => s.forkJitter = v),
                MS("Fill (volume)", s.forkVelSpread, 0f, 1f,
                    "0 = every puff leaves at one speed, which reads as a hollow expanding SHELL. Above 0 spreads "
                    + "the speeds so the middle fills in with slow-travelling gas instead of hollowing into a "
                    + "smoke ring.",
                    v => s.forkVelSpread = v)));

            // ── Detonation clock ──
            box.Add(Z.HGroup(
                MS("Birth skew", s.forkBlastSkew, 1f, 6f,
                    "Above 1 piles puff births at the FRONT of the birth span — a hard attack with a ragged "
                    + "tail, which is what makes this read as a detonation rather than a steady jet.",
                    v => s.forkBlastSkew = v),
                MS("Birth span", s.forkBlastSpan, 0.01f, 0.6f,
                    "How much of the blast's own clock the puff births are spread over.",
                    v => s.forkBlastSpan = v)));
            box.Add(Z.HGroup(
                MS("Puff life", s.forkPuffLife, 0.1f, 1.2f,
                    "How long a puff burns, as a fraction of the blast's own clock.",
                    v => s.forkPuffLife = v),
                MS("Cool", s.forkCool, 0f, 3f,
                    "Amplitude falloff exponent over a puff's life.",
                    v => s.forkCool = v)));

            // ── How it dies (generation 5's whole point) ──
            box.Add(Z.Text(
                "How it dies — hold the amplitude up, then contract rather than fade, closing inward from the "
                + "fastest (outer) gas first.", ZuiText.Small));
            box.Add(Z.HGroup(
                MS("Hold", s.forkHold, 0f, 3f,
                    "Above 0 holds a puff's amplitude up and drops it LATE instead of dimming from birth — the "
                    + "delay that keeps the body solid long enough for Shrink to be the thing you see.",
                    v => s.forkHold = v),
                MS("Opacity", s.forkOpaq, 0.2f, 1.5f,
                    "Exponent on the Fill's own alpha ceiling. Below 1 pushes the body toward solid while doing "
                    + "least at the coolest, already-thin rim — so the body opens up without trading away the "
                    + "edge falloff.",
                    v => s.forkOpaq = v)));
            box.Add(Z.HGroup(
                MS("Shrink", s.forkShrink, 0f, 1f,
                    "How much of its radius a puff loses by the end of its life — dies by getting SMALLER, not "
                    + "more transparent. The kernel's peak is its amplitude, so a shrinking puff stays exactly as "
                    + "bright at its centre.",
                    v => s.forkShrink = v),
                MS("Shrink at", s.forkShrinkAt, 0f, 0.95f,
                    "The age the contraction starts at.",
                    v => s.forkShrinkAt = v)));
            box.Add(MS("Lead die", s.forkLeadDie, 0f, 1f,
                "The fastest (outermost) gas dies first, so the silhouette closes INWARD as it collapses — "
                + "shrinking every puff by the same amount alone leaves the outer shell in place and merely "
                + "makes it smaller (a widening necklace of lumps, not a collapse).",
                v => s.forkLeadDie = v));

            // ── Shedding burning mass ──
            box.Add(Z.HGroup(
                MS("Gobs", s.forkGobCount, 0f, 40f,
                    "Lumps of burning mass shed off the blast in its OPENING phase, that then dissipate — "
                    + "optional, 0 = none. Opposite death to the body: it balloons and thins as it goes, which "
                    + "is what makes it read as having come off something.",
                    v => s.forkGobCount = Mathf.RoundToInt(v), decimals: 0),
                MS("Gob size (px)", s.forkGobSizePx, 1f, 10f,
                    "A gob's radius — mass, not a spark; several times a puff's own size.",
                    v => s.forkGobSizePx = v)));
            if (s.forkGobCount > 0)
            {
                box.Add(Z.HGroup(
                    MS("Gob reach", s.forkGobReach, 0.3f, 1.5f,
                        "A gob's travel, as a fraction of Reach.",
                        v => s.forkGobReach = v),
                    MS("Gob swell", s.forkGobSwell, 0f, 3f,
                        "A gob DISSIPATES — it balloons as it goes out, the opposite of the body, which shrinks "
                        + "and stays solid.",
                        v => s.forkGobSwell = v)));
                box.Add(Z.HGroup(
                    MS("Gob life", s.forkGobLifeMul, 0.5f, 3f,
                        "A gob's life, as a multiple of Puff life.",
                        v => s.forkGobLifeMul = v),
                    MS("Gob amount", s.forkGobAmp, 0f, 3f,
                        "Gob brightness.",
                        v => s.forkGobAmp = v)));
                box.Add(MS("Gob timing", s.forkGobEarly, 0.05f, 1f,
                    "Gobs are born inside this fraction of the birth span — the opening phase. A gob that leaves "
                    + "late just reads as a second, smaller explosion.",
                    v => s.forkGobEarly = v));
            }

            // ── Look — puff size at the source, turbulence, and the heat-field-to-pixel mapping ──
            box.Add(Z.HGroup(
                MS("Puff size (px)", s.forkPuffSizePx, 1f, 10f,
                    "A puff's radius at the source.",
                    v => s.forkPuffSizePx = v),
                MS("Turbulence (px)", s.forkWarpAmount, 0f, 20f,
                    "A per-puff wobble that breaks up the disc into licks — an approximation of true domain-warp "
                    + "turbulence.",
                    v => s.forkWarpAmount = v)));
            box.Add(Z.HGroup(
                MS("Soot", s.forkSoot, 0f, 0.6f,
                    "Tint gained by the end of a puff's life, darkening/desaturating it — a third route to "
                    + "transparency-reading if pushed too high; keep it modest.",
                    v => s.forkSoot = v),
                MS("Edge softness", s.forkSoft, 0.1f, 3f,
                    "Width of the falloff at the silhouette's edge, in heat-field units.",
                    v => s.forkSoft = v)));
            box.Add(Z.Toggle("Auto exposure",
                "Fit the heat ceiling to this frame's own measured peak instead of a fixed Field high. On by "
                + "default — leaving it off means Field high has to be re-fitted by hand any time puff count, "
                + "amplitude, blast count, or almost any other dial changes, or the blast reads as a flat, "
                + "washed-out silhouette (ceiling too high) or a blown-out core (ceiling too low).",
                s.forkAutoExposure, v => Dirty(() => s.forkAutoExposure = v)));
            if (s.forkAutoExposure)
                box.Add(MS("Exposure", s.forkExposureMult, 0.2f, 2f,
                    "Multiplier on the measured peak of this frame's heat field. Lower = brighter/hotter overall "
                    + "(clips more of the field to the ramp's hot end); higher = dimmer, more rim.",
                    v => s.forkExposureMult = v));
            box.Add(Z.HGroup(
                MS("Field low", s.forkLo, 0f, 0.6f,
                    "Heat-field value at the silhouette's outer edge — everything below this is fully transparent.",
                    v => s.forkLo = v),
                MS("Field high", s.forkHi, 0.3f, 3f,
                    s.forkAutoExposure
                        ? "MANUAL heat ceiling — only used when Auto exposure (above) is off."
                        : "Heat-field value at which the Fill ramp tops out (hottest). Puff count/amplitude/overlap "
                          + "shift the field's real range, so this and Field low are the two dials that keep the "
                          + "blast from reading as all-rim or all-core.",
                    v => s.forkHi = v)));
            box.Add(MS("Curve", s.forkCurve, 0.3f, 2f,
                "Bends where the ramp is spent along the heat field. Below 1 = hotter/brighter overall, above 1 "
                + "= mostly cool envelope with a tight hot spine.",
                v => s.forkCurve = v));

            shapeBody.Add(box);
        }

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
            // Off-centre emitter (task #61): shift the whole built-in flame off the canvas centre. A plain Vector2 in px
            // (not animatable) → a Z.Pad, mirroring the Gem light-dir pad; the renderer translates the FINISHED flame,
            // so the sim stays centred and byte-faithful (0,0 = the pre-change centred behaviour). Undo-safe via Dirty.
            {
                float offHalf = Mathf.Max(1f, spec.canvasSize * 0.5f);
                const string offTip = "Shift the whole flame off the canvas centre, in pixels — X right, Y up. The flame "
                    + "is simulated exactly as if centred (buoyancy, arms and confinement all move with it), then "
                    + "translated to here. 0,0 = centred (the built-in behaviour). Places a flame that doesn't sit in the "
                    + "middle. (Built-in arms path only — with 'Swarm emitters' on, place the sources with the Swarm.)";
                box.Add(Z.Field("Emitter offset (px)", offTip,
                    Z.Pad(s.fireEmitterOffset, new Rect(-offHalf, -offHalf, offHalf * 2f, offHalf * 2f), offTip,
                        v => Dirty(() => s.fireEmitterOffset = v), 56f)));
            }

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
            swarmSection = Z.Section("Swarm", "Place many particles in a shape instead of one centred particle.",
                icon: "circles-three-plus");
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
                + "hidden — each letter rides one swarm position. For Inferno each particle IS one blast.",
                v =>
                {
                    Dirty(() => s.swarmEnabled = v);
                    RebuildSwarm();
                    // Inferno's Shape box swaps its multi-blast rows on this toggle (the swarm ignites the blasts).
                    if (s.shapeForm == ShapeForm.Inferno) RebuildShape();
                });

            // Inferno: the swarm IS the blast placement — one blast per particle (off = one centred blast). The
            // section shows its normal controls; what it drives is said by the header toggle's TOOLTIP (an
            // on-screen instruction label here was a UI-Guide violation — "tooltip, not title").

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
            // current mode (Tangent means something different in Area vs Path). GREY it out when the current
            // form makes orient a SILENT no-op (Ring; a plain symmetric Disc) so an author isn't left wondering
            // why nothing changes — the disabled tooltip explains why. Every form where it works stays live.
            bool orientInert = SwarmOrientIsInert(s, spec);
            string orientTip = orientInert ? SwarmOrientInertTooltip(s) : SwarmOrientTooltip(s);
            var orientField = Z.Field("Orient", orientTip,
                Z.Segmented((int)s.swarmOrient, SwarmOrientLabels, orientTip,
                    v => { Dirty(() => s.swarmOrient = (SwarmOrient)v); RebuildSwarm(); }));
            orientField.SetEnabled(!orientInert);
            swarmBody.Add(orientField);

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

            // Per-particle SIZE by index (S1) + shared DEATH point (S1). The curve's X axis is the particle
            // INDEX (not time), so it gets INDEX markers (one per particle, count = swarmCount) and Index/Scale
            // axis captions instead of the frame lines every other Val shows.
            swarmBody.Add(ValIndexed("Scale by index",
                "Multiplies each particle's size by a factor read from its index (0 = first, 1 = last). Static 1 = "
                + "every particle full size; a Curve tapers the swarm (ends-vs-middle, centre-vs-edge — author it "
                + "freely, Bars-style); MinMax gives each particle a random size.",
                s.swarmScaleByIndex, 0f, 3f, s.swarmCount, "Index", "Scale"));
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
                s.swarmTurn, -1440f, 1440f, cyclic: true));
            spin.Add(Val("Swarm tilt °",
                "Pitch the whole placed cloud around the HORIZONTAL axis, live at the current frame — tips the cloud "
                + "toward/away (pseudo-3D). Animate it to roll the swarm forward/back.",
                s.swarmTilt, -1440f, 1440f, cyclic: true));
            spin.Add(Val("Swarm roll °",
                "Roll the whole placed cloud in the screen plane (about the axis pointing at you), live at the "
                + "current frame. Animate it to spin the swarm flat against the screen.",
                s.swarmRoll, -1440f, 1440f, cyclic: true));
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

        VisualElement Val(string label, string tooltip, ZUIValue v, float lo, float hi, bool cyclic = false)
        {
            var o = new ZuiValueControl.Options
            {
                absMin = lo, absMax = hi,
                hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                controlWidth = 170f, grow = true,
                cyclic = cyclic,   // a wrapping angle (rotation / spin) → offer the Cycles envelope generator
                // Show where each bake frame lands on the curve (numbers thin out when frames are dense).
                frameCount = spec != null ? spec.frameCount : 0,
            };
            return Z.Value(label, v, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre Plus"));
        }

        // Val variant for an INDEX-mapped curve — X axis is a particle INDEX (0…last), not time. Swaps the
        // frame-boundary markers for INDEX markers (vertical lines labelled 0,1,2,… by particle, reusing the
        // frame-marker line/label/thinning code) and captions the envelope's axes, so a scale-by-index curve
        // reads as "which particle → what scale". Frame lines are NOT drawn (frameCount left 0). Only the
        // scale-by-index control uses this; every other Val stays exactly as-is.
        VisualElement ValIndexed(string label, string tooltip, ZUIValue v, float lo, float hi,
                                 int indexCount, string xAxisLabel, string yAxisLabel)
        {
            var o = new ZuiValueControl.Options
            {
                absMin = lo, absMax = hi,
                hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                controlWidth = 170f, grow = true,
                indexMarkerCount = Mathf.Max(0, indexCount),
                xAxisLabel = xAxisLabel, yAxisLabel = yAxisLabel,
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

        // The 3D-solid forms — they share BuildSolidBox (Solid geometry + the Light / Lines / Glow sibling boxes) and
        // edit particleSpin in the Position box as "Turn °" (Tilt/Roll join it there), so the flat forms' "Spin °"
        // row is not shown for them. Disc / Crescent / Sparkle / Sprite / Text are NOT solid forms.
        static bool IsSolidForm(ShapeForm f) =>
            f == ShapeForm.Gem || f == ShapeForm.Box || f == ShapeForm.Pyramid ||
            f == ShapeForm.Can || f == ShapeForm.Orb || f == ShapeForm.Ring;

        // The flat 2D forms that get a first-class Border (task #60/#65) — mirrors PyrePlusRenderer.IsFlat2DBorderForm
        // exactly (Ring counts as a flat 2D form here, unlike the 3D solids). The 3D solids, Text, and Sprite/Fire/
        // Fireball/Sparkle are excluded, so the Border box never shows for them.
        static bool IsFlat2DBorderForm(ShapeForm f) =>
            f == ShapeForm.Disc || f == ShapeForm.Crescent || f == ShapeForm.Ring ||
            f == ShapeForm.Streak || f == ShapeForm.Star || f == ShapeForm.Polygon;

        // The Border sub-box (task #60/#65), shown only for the flat 2D forms. Off = a single compact toggle row (the
        // common default, minimal footprint); on = a titled box with Width / Fill / Draw-over-matte. Toggling Enable
        // rebuilds the Shape body so the box expands/collapses. Every edit is Undo-safe (Dirty / the Fill/Val contract).
        void BuildBorderBox(PyrePlusLayer s)
        {
            s.borderWidth ??= new ZUIValue(2f);            // defensive; the real defaults come from the spec factories
            s.borderFill ??= new ZuiFill(new Color(1f, 1f, 1f, 1f));

            if (!s.borderEnabled)
            {
                shapeBody.Add(Z.Toggle("Border",
                    "Add a coloured rim around this shape's silhouette (the outermost few px of the drawn alpha) — the "
                    + "2D counterpart to the 3D solids' edge lines, and what a disc used as a ball wants for a rim. "
                    + "Turn on to set its width, fill and draw-over-matte.",
                    s.borderEnabled, v => { Dirty(() => s.borderEnabled = v); RebuildShape(); }));
                return;
            }

            var box = Z.BoxKeyed("Border",
                "A coloured rim around this shape's silhouette — the outermost Width px of the drawn alpha, in the "
                + "Border fill (the 2D counterpart to the 3D solids' edge lines).",
                "pyreplus.border", "bounding-box");
            // Collapsed, the box hides an active rim — mark it so folding away the border doesn't hide that it's on.
            box.SetHeaderSuffix(() => s.borderEnabled ? " (on)" : "");
            box.Add(Z.Toggle("Enable",
                "Draw the rim. Off = no border (the shape is unchanged, byte-identical to no border).",
                s.borderEnabled, v => { Dirty(() => s.borderEnabled = v); RebuildShape(); }));
            box.Add(Val("Width (px)",
                "Rim thickness in pixels, over the layer's life — the outermost N px of the shape's silhouette are "
                + "recoloured to the Border fill (the rim keeps the shape's anti-aliased edge).",
                s.borderWidth, 0f, Mathf.Max(8f, spec.canvasSize / 4f)));
            box.Add(FillRow("Fill",
                "The rim's colour/fill — Solid, a gradient, or a spatial fill (alpha-capable), like the shape's own "
                + "Fill. A spatial fill is mapped across the shape's bounding box.",
                s.borderFill, new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));
            box.Add(Z.Toggle("Draw over matte",
                "When this layer feeds a matte (Write / Luma) or is clipped: send only the FILL into the mask and draw "
                + "the BORDER on top of the finished frame instead — so a shape's fill can BE the matte while its "
                + "border still shows, with no separate outline-only layer. Off = the border is part of the layer.",
                s.borderOverMatte, v => Dirty(() => s.borderOverMatte = v)));
            shapeBody.Add(box);
        }

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

        // Orient is a SILENT no-op for radially-symmetric placements, so the control is greyed for those forms:
        //   • Ring — the renderer EXPLICITLY ignores orientDeg (DrawRing takes no roll; its Turn/Tilt already
        //     shape the annulus), so per-particle facing does nothing.
        //   • a plain Disc whose fill has NO angular component (Solid / Over life, no texture) AND no modifiers —
        //     the no-modifier Disc raster never folds orientDeg, and a round uniformly-filled disc has no feature
        //     to turn. A spatial fill (Linear/Radial/a texture) or any modifier makes orient live again, so those
        //     stay ENABLED.
        // NOT greyed: the Orb (its renderer rolls the lit hotspot by orientDeg — a real effect), the facet solids,
        // Crescent/Sprite/Streak/Star/Text — all show orient, so they keep the control live.
        static bool SwarmOrientIsInert(PyrePlusLayer s, PyrePlusSpec spec)
        {
            if (s == null) return false;
            if (s.shapeForm == ShapeForm.Ring) return true;
            if (s.shapeForm == ShapeForm.Disc)
            {
                bool hasMods = (s.modifiers != null && s.modifiers.Count > 0)
                               || s.simulationModifier != null
                               || (spec != null && spec.globalModifiers != null && spec.globalModifiers.Count > 0);
                return !hasMods && FillIsAngularlySymmetric(s.shapeFill);
            }
            return false;
        }

        // A fill with no spatial/directional component: a flat colour or an over-life gradient, and no texture.
        // Linear/Radial gradients and every texture (Sprite/Noise/Grid/Dots) vary across the shape, so rotating a
        // particle turns them — orient is live for those; Solid/Over life look identical at any facing.
        static bool FillIsAngularlySymmetric(ZuiFill fill)
        {
            if (fill == null) return true;
            if (fill.texture != ZuiFill.TextureKind.None) return false;
            return fill.mode == ZuiFill.Mode.Solid || fill.mode == ZuiFill.Mode.OverLife;
        }

        // The tooltip shown while Orient is greyed (inert), explaining WHY for the CURRENT form so a disabled
        // control isn't a mystery — and pointing at what to change to make it live.
        static string SwarmOrientInertTooltip(PyrePlusLayer s)
        {
            if (s != null && s.shapeForm == ShapeForm.Ring)
                return "Orient has no visible effect on a Ring — it's radially symmetric, so the renderer ignores "
                     + "per-particle facing (Turn/Tilt already shape the annulus). Switch to Crescent/Sprite/"
                     + "Streak/Star/Text (or another non-symmetric form) to use it.";
            return "Orient has no visible effect on a plain Disc with a symmetric fill (Solid or Over life, no "
                 + "texture) and no modifiers — a round, uniformly-filled disc has no feature to turn. Give it a "
                 + "spatial fill (Linear/Radial/a texture) or a modifier, or switch forms (Crescent/Sprite/Streak/"
                 + "Star/Text), to use it.";
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
            if (f == ShapeForm.Inferno)
                return "The explosion's HEAT ramp: Inferno reads this fill's GRADIENT with the LEFT end as the "
                     + "white-hot core and the RIGHT end as the coolest flame (the default fire fill is already in "
                     + "that order). Smoke grey is NOT from this ramp — the Darkness dial makes it. Prefer an Over "
                     + "life gradient; a Solid fill gives a single-colour flame." + modes;
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
                return "The star's TIP radius in absolute pixels over the particle's own life — the arms reach out to "
                     + "it (the valleys sit at Length inside). It only reads as 'big' because the canvas is small.";
            if (f == ShapeForm.Polygon)
                return "The polygon's circumradius in absolute pixels over the particle's own life — every vertex "
                     + "reaches out to it (the same pixel convention as every form).";
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
                case ShapeForm.Polygon:
                    return "Degrees the polygon spins in place over its own life — unlike a disc, a regular polygon "
                         + "isn't radially symmetric, so its corners visibly turn.";
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
