// PyreWindow — the authoring window for the Pyre prototype (see PYREPLUS_DESIGN.md).
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

namespace Laubrary.Pyre.Editor
{
    // Split across partials: PyreWindow.cs = shell + left-pane dials; PyreWindow.Preview.cs = the
    // IMGUI preview island + the Swarm authoring overlay (shape outline, spawn dots, drag handles).
    public partial class PyreWindow : ZuiAssetWindow<Pyre>, IPyreShapeCardHost
    {
        [MenuItem("Laubrary/Pyre")]
        public static void Open()
        {
            // The same floor Shaper declares: below it the split's two minimum panes (360 + 320) cannot both
            // fit, so a smaller window would clamp the divider into an unusable layout (T-0302).
            var w = GetWindow<PyreWindow>("Pyre");
            w.minSize = new Vector2(820f, 520f);
        }

        /// Open the window ON a particular Pyre. Not a menu item — the entry point a reference chip's "Edit"
        /// and a sibling tool's "open the thing this points at" both need. Several files across the package
        /// already describe their own OpenFor as being "the same shape as PyreWindow.OpenFor"; this is that
        /// method, which until now only existed in those comments.
        public static void OpenFor(Pyre spec)
        {
            var w = GetWindow<PyreWindow>("Pyre");
            if (spec != null) w.SetAsset(spec);
        }

        Pyre spec => Current;
        // The tool is called Pyre. "Pyre Plus" was the pre-2026-08-23 name and this window was the last place
        // it still reached the user: TypeLabel is threaded through fifteen visible strings (the Save tooltip,
        // the browser header, the New/Duplicate/Delete wording and their Undo entries), so every one of them
        // said a name the project no longer uses. Existing assets keep the file names they were saved under.
        protected override string TypeLabel => "Pyre";
        protected override string NewAssetName => "New Pyre";
        protected override string DefaultFolder => "Assets/Pyre";

        protected override Texture2D RenderThumbnail(Pyre item)
        {
            int mid = Mathf.Clamp(item.frameCount / 2, 0, Mathf.Max(0, item.frameCount - 1));
            return PyreRenderer.RenderFrameTexture(item, mid);
        }
        protected override bool AnimateThumbnails => true;
        // Was calling PyreRenderer.RenderFrame (a full from-scratch layer sim/composite) fresh every tick for
        // every animating thumbnail — with N Pyres animating at once that's N full renders every ~83ms, which
        // is both the "lag" and the "not every frame plays" symptom (the frame index is time-based, so a late
        // tick jumps ahead rather than replaying the skipped frame). GetFrames renders each distinct frame
        // ONCE (lazily, first time this spec is asked to animate at all) and caches it as a real Sprite[] — a
        // true in-memory sprite sheet. Every tick after that first pass is just a pixel copy, not a re-sim.
        protected override void UpdateAnimatedThumbnail(Pyre item, Texture2D tex, double time)
        {
            if (item == null || item.frameCount <= 1) return;
            var frames = PyreRenderer.GetFrames(item);
            if (frames == null || frames.Length == 0) return;
            float fps = Mathf.Max(1f, item.previewFps);
            int f = Mathf.FloorToInt((float)(time * fps)) % frames.Length;
            var frameTex = frames[f]?.texture;
            if (frameTex == null) return;
            tex.SetPixels32(frameTex.GetPixels32());
            tex.Apply();
        }

        // Dial-pane width, draggable via the vertical splitter (mirrors Pyre1). Persists across domain
        // reloads. The ColumnFlow reads this width, so dragging the divider wider adds columns.
        [SerializeField] float leftPaneWidth = 360f;
        ScrollView leftPane;
        VisualElement verticalSplitter;   // set once, in BuildAsset — read back by ReservedRightWidth (T-0319)

        // preview state
        IMGUIContainer preview;
        double lastTime;
        float acc;
        bool playing = true;
        int frame;
        Button playButton;
        SliderInt scrubSlider;   // frame scrubber (transport parity with Pyre1); drives `frame`, follows playback
        Label frameReadout;      // "frame N/M" readout beside Zoom/Speed, kept in sync with `frame`
        Label fillReadout;       // "rendering n/N" while the frame cache fills (PyreWindow.FrameCache.cs); hidden when complete
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
        //
        // The dirty flag belongs to a USER's choice of layer, never to housekeeping. Merely showing an asset —
        // loading it, rebuilding the window, repairing an out-of-range index — must leave the file alone, or the
        // asset is unsaved-dirty the instant it is opened and the next flush (Ctrl+S, the quit prompt, the
        // toolbar's own Save) rewrites a file the author never edited. Hence the two doors below: `layerSel`
        // for a real selection, `ClampLayerSel` for a repair.
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

        /// Bring a stored selection back into range WITHOUT dirtying the asset. A stale index (its layer was
        /// deleted, or an older build wrote a sentinel) is repaired in memory so every section reads a valid
        /// layer; the corrected value reaches disk on the author's next real edit, not on a mere open.
        void ClampLayerSel()
        {
            if (spec == null || spec.layers == null) return;
            int clamped = Mathf.Clamp(spec.previewLayerSel, 0, Mathf.Max(0, spec.layers.Count - 1));
            if (clamped != spec.previewLayerSel) spec.previewLayerSel = clamped;
        }

        PyreLayer SelLayer
        {
            get
            {
                if (spec == null || spec.layers == null || spec.layers.Count == 0) return null;
                // Read-only: a getter that writes to the asset is a write nobody asked for. The stored value is
                // repaired by ClampLayerSel on load/rebuild; here we only clamp what we return.
                int clamped = Mathf.Clamp(spec.previewLayerSel, 0, spec.layers.Count - 1);
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
            DestroyFrameCache();
            DisposeAllFills();   // joins any worker still mid-job so the spec clones can be destroyed before the domain goes
            DestroyLayerStore();
            DestroyCherryStripCache();
            playback3DPreview?.Dispose(); playback3DPreview = null;
        }

        // Playback 3D (PROOF OF CONCEPT) — the PreviewRenderUtility-backed live preview, lazily created (see
        // PyrePlayback3DPreview.cs). Disposed above on OnDisable; RefreshPlayback3DPreview just re-dirties the
        // preview repaint (the class itself re-simulates on every Render call, so there's no cache to invalidate).
        PyrePlayback3DPreview playback3DPreview;
        void RefreshPlayback3DPreview() { preview?.MarkDirtyRepaint(); }
        protected override void OnAssetChanged()
        {
            frame = 0; previewDirty = true; DestroyFrameCache(); DestroyCherryStripCache();
            // The asset's OWN stored selection is what "come back to where you were" means, so loading one must
            // not overwrite it. This used to write int.MaxValue here and let BuildAsset clamp it back, which
            // dirtied every asset the moment it was shown (net data change: nothing) and, if anything flushed
            // between the two, persisted the raw sentinel — which is how a shipped asset came to carry
            // previewLayerSel: 2147483647. A brand-new asset has one layer, so its stored 0 is already its last.
            ClampLayerSel();
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
                if (l != null && IsSolidForm(l.shapeForm) && PyreLayer.IsPristineDefaultShapeFill(l.shapeFill)) { need = true; break; }
            if (!need) return;
            Dirty(() => { foreach (var l in spec.layers) SteadyDefaultFillForSolid(l); });
        }

        void Tick()
        {
            // `this == null`: EditorApplication.update runs a snapshot of its invocation list, so when the window is
            // closed from INSIDE another update callback (a tool command, an asset event) this tick still fires once
            // after OnDisable unsubscribed it — on a destroyed window whose cache is gone, which would otherwise
            // allocate a fresh cache and a fill nobody will ever dispose.
            if (this == null || spec == null) return;
            FillFrameCacheTick();   // background render of frames the cache is missing — runs paused or playing
            if (!playing) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            acc += dt * Mathf.Max(1f, spec.previewFps);
            bool advanced = false;
            while (acc >= 1f)
            {
                // Playback only steps onto frames the cache already holds. While the fill is still working, the
                // transport waits at the render front (acc held at one beat, so it moves the moment the frame
                // lands) instead of stalling the whole editor on a synchronous render — the preview itself never
                // renders during playback. CherryFraming's beat state is rolled back when its pick isn't ready,
                // so that code path stays exactly as written.
                if (spec.cherryEnabled)
                {
                    var saved = (frame, cherrySlot, cherryBeatsLeft, cherryDelayActive, cherryDelayBeatsLeft);
                    CherryAdvanceOneBeat();
                    if (frame >= 0 && !IsFrameReady(frame))
                    {
                        (frame, cherrySlot, cherryBeatsLeft, cherryDelayActive, cherryDelayBeatsLeft) = saved;
                        acc = 1f;
                        break;
                    }
                }
                else
                {
                    int next = (frame + 1) % Mathf.Max(1, spec.frameCount);
                    if (!IsFrameReady(next)) { acc = 1f; break; }
                    frame = next;
                }
                acc -= 1f;
                advanced = true;
            }
            if (advanced) { preview?.MarkDirtyRepaint(); RefreshTransportReadout(); }
        }

        // ── CherryFraming playback (editor preview only — see PyreWindow.CherryFraming.cs) ────────────
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
            preview?.MarkDirtyRepaint();   // a playback reset picks a different cached frame; it renders nothing new
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

        // Set by every authored edit (MarkDirty) and consumed by the frame cache (EnsureFrameCache), which then
        // forgets every frame and refills from the one on screen.
        bool previewDirty = true;

        // Undo/redo and asset switches rebuild the whole window through ZuiWindow.Rebuild, which bypasses Dirty —
        // so the cache is invalidated here, otherwise the preview would keep showing the pre-undo frames.
        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();
            previewDirty = true;
        }

        // Coalesced canvas-size range refresh (Bug 1). The Canvas Size slider's derived-range Rebuild is deferred
        // to drag-commit and scheduled onto the next frame; this holds the pending item so rapid commits coalesce
        // into one Rebuild instead of stacking several.
        UnityEngine.UIElements.IVisualElementScheduledItem rangeRebuildPending;

        // Any authored edit routes through here (Dirty → MarkDirty). Refreshing the transport readout alongside
        // keeps the scrubber's range + value and the "frame N/M" label in sync when e.g. the frame count changes.
        void MarkDirty() { previewDirty = true; preview?.MarkDirtyRepaint(); RefreshTransportReadout(); }

        protected override void BuildAsset(VisualElement root, Pyre s)
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
            left.style.width = ClampedLeftPaneWidth();
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
            // T-0065 — Views is a normal ZuiSection too (green header, foldable, included in the toggle
            // bar below), same as every other unit in this flow, rather than a bare bar with no fold.
            var viewBar = BuildViewBar(root);
            viewsSection = Z.Section("Views", "Save and recall named presets of which boxes are folded open.", "pyreplus.views");
            viewsSection.Add(viewBar);
            flow.Add(viewsSection);

            // Selection is STICKY across rebuilds — only clamped to a valid index. (Defaulting to the
            // last layer on every rebuild silently jumped the overlay/sections to another layer after
            // any undo or structural edit; a fresh ASSET picks its last layer via OnAssetChanged.)
            ClampLayerSel();   // repair only — a rebuild is not an edit, so it must not dirty the asset

            BuildCanvas(flow, s);      // Canvas first — the output settings sit at the top of the dials
            BuildLayerList(flow);      // the compact layer stack — under the views bar, below Canvas
            BuildGlobalModifiers(flow, s);   // task #56 — spec-wide modifiers applied to EVERY layer (right after Layers)
            BuildShape(flow, s);
            BuildSwarm(flow, s);
            BuildModifiers(flow, s);   // PyreWindow.Modifiers.cs
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
            // T-0321 — the island CLIPS its own painting. The stage rule deliberately honours the zoom the
            // author dialled rather than shrinking to fit ("a pane too small … gets a clipped picture, which
            // is honest" — ZuiPixelStage.FitZoom), so `placement.rect` is routinely bigger than this element:
            // measured at Zoom 6 on a 64×64 canvas, a 682×682pt picture inside a 445×320pt island. Neither
            // GUI.DrawTexture (ZuiPixel.Draw) nor EditorGUI.DrawRect (the canvas-edge outline below) clips to
            // the container on its own, so without this the frame painted straight over the transport, the
            // Preview backdrop box and the Bake box, and the outline drew two window-wide rules across the
            // Frame row and the Tags band. Seen in a capture, invisible to every element-geometry probe —
            // every element was exactly where it belonged. Shaper's stage already clips (its picture is a
            // UITK child whose worldClip is the stage rect); this gives the IMGUI stage the same guarantee.
            preview.style.overflow = Overflow.Hidden;
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
            verticalSplitter = BuildVerticalSplitter();
            split.Add(verticalSplitter);          // drag to resize the dial pane (and change its column count)
            split.Add(rightPane);
            // Re-clamp on every resize, not only on rebuild: shrinking the window does not rebuild this tree,
            // so without this the pane would keep the width it was built at and push the right-hand pane off
            // the edge until something else happened to trigger a rebuild. Re-reading the persisted intent
            // each time is also what lets the pane grow BACK when the window is widened again.
            split.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (leftPane == null) return;
                float want = ClampedLeftPaneWidth();
                if (!Mathf.Approximately(leftPane.resolvedStyle.width, want)) leftPane.style.width = want;
            });
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            // T-0065 — the section toggle bar sits ABOVE everything else, spanning the full window width, so
            // it reads as the absolute top of the per-asset UI rather than a unit squeezed into the 360px
            // dial column. TagsSection was already parented under the WINDOW's root by the base class
            // (ZuiAssetWindow.BuildUI runs before BuildAsset) — VisualElement.Add always detaches an element
            // from its current parent before reattaching, so re-adding it here pulls it out from above the
            // bar and drops it back in right below it, where it reads as just another toggleable section.
            root.Add(BuildSectionToggleBar());
            if (TagsSection != null) root.Add(TagsSection);
            root.Add(split);

            // Whole tree is now under `root`; re-apply the view the user left this window in.
            viewBar.RestoreLast();
        }

        // A 6px draggable divider between the dial pane and the preview (mirrors Pyre1's splitter). Dragging sets
        // leftPaneWidth + the pane's fixed width live; the ColumnFlow reads that width, so a wider pane = more
        // columns. Clamped between one column (360) and the four-column cap (or the window width, whichever is less).
        /// The dial pane's width as it may actually be APPLIED, as opposed to the width the user asked for.
        /// The persisted intent (leftPaneWidth) is kept untouched so the pane regrows to it when the window is
        /// widened again; only what reaches `style.width` is clamped. `CapFor(position.width)` is the same
        /// expression the drag handler below uses, so the two can no longer disagree: before this existed, the
        /// drag clamped against the window but the BUILD clamped only against the four-column cap, so a
        /// divider legitimately dragged wide at a large window parked the entire preview/transport/bake pane
        /// off the right edge the next time the window was rebuilt at a smaller size, with no scroller and no
        /// reachable drag anchor. Measured at 900x700 with leftPaneWidth 1458: 11 of 27 buttons — the whole
        /// transport, GIF and Bake — sat entirely outside the window. Same class as Z.Split's T-0296 defect.
        ///
        /// T-0319 — the cap above used to subtract only the right pane's own 260px minWidth from the
        /// window width, but the dial pane does not get the whole rest of the window: the root also
        /// spends its own horizontal padding, and the divider between the two panes spends its own width.
        /// Both were missing (14.2px total — measured: 8px root padding + ~6.2px resolved divider width),
        /// so a divider dragged to (or past) the cap pushed the preview pane, its transport and 23 drawn
        /// elements off the window at any width below ~1718px. ReservedRightWidth() reads both back from
        /// the live elements so this cap can't drift from them again, and CapFor() is the one place both
        /// this method and the drag handler below compute the cap, so they can no longer disagree either.
        float ClampedLeftPaneWidth()
        {
            return Mathf.Clamp(leftPaneWidth, 360f, CapFor(position.width));
        }

        float CapFor(float windowWidth)
        {
            return Mathf.Min(4f * 360f + 3f * 6f, Mathf.Max(360f, windowWidth - 260f - ReservedRightWidth()));
        }

        /// What the cap must ALSO give back beyond the right pane's own 260px minWidth: the root's own
        /// horizontal padding, and the vertical splitter's own width. Read back from the live elements
        /// when they exist (so a stylesheet or splitter-width change is picked up for free); otherwise
        /// fall back to the values those elements are declared with, named at their source so nobody has
        /// to re-derive them: `.zui-root { padding: 4px }` in
        /// `Assets/Packages/Laubrary/Zui/Toolkit/ZuiToolkit.uss` (left + right = 8px), and
        /// `BuildVerticalSplitter()`'s own `s.style.width = 6f` a few lines below.
        float ReservedRightWidth()
        {
            var root = rootVisualElement;
            float rootPadding = root != null
                ? root.resolvedStyle.paddingLeft + root.resolvedStyle.paddingRight
                : 8f; // .zui-root padding: 4px, both sides (ZuiToolkit.uss) — before the root has laid out
            float dividerWidth = verticalSplitter != null
                ? verticalSplitter.resolvedStyle.width
                : 6f; // BuildVerticalSplitter()'s own declared width — before the splitter has laid out
            if (float.IsNaN(rootPadding) || rootPadding <= 0f) rootPadding = 8f;
            if (float.IsNaN(dividerWidth) || dividerWidth <= 0f) dividerWidth = 6f;
            return rootPadding + dividerWidth;
        }

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
                // A real drag IS the user's intent, so it updates the persisted width; ClampedLeftPaneWidth
                // then decides what that means at the current window size. CapFor() is the same expression
                // ClampedLeftPaneWidth() uses (T-0319) — there is only one copy of this arithmetic now.
                leftPaneWidth = Mathf.Clamp(leftPaneWidth + e.deltaPosition.x, 360f, CapFor(position.width));
                if (leftPane != null) leftPane.style.width = ClampedLeftPaneWidth();
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
        const string ViewStorePath = "Assets/Pyre/PyreViews.asset";
        const string ViewPrefsKey = "Pyre.lastView";
        ZuiSection viewsSection;   // T-0065 — the ZuiSection wrapping BuildViewBar's bar, so Views can join the toggle bar

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

        // T-0065 — the shared ZuiSectionToggleBar (Zui/Toolkit/ZuiSectionToggleBar.cs) over Pyre's eight
        // top-level sections: the base-class Tags section, the Views bar (also now a section), and the six
        // built above. That shared widget owns the Sections-vs-Toggle-Bar mode switch and the mutual-exclusion
        // with each section's own header click — nothing Pyre-specific left to do here beyond listing
        // which sections it has. A null entry (a rare rebuild-ordering issue, or no asset saved yet so
        // TagsSection is null) is skipped harmlessly by the bar itself.
        VisualElement BuildSectionToggleBar()
        {
            return new ZuiSectionToggleBar("Pyre",
                ("Tags", TagsSection),
                ("Views", viewsSection),
                ("Canvas", canvasSection),
                ("Layers", layersSection),
                ("Global Mod", globalModifiersSection),
                ("Shape", shapeSection),
                ("Swarm", swarmSection),
                ("Modifiers", modifiersSection));
        }

        // Mint the Pyre views asset — only ever called from the bar's Save-as when none exists yet.
        // Undo-registered per the Laubrary Undo rule; creates the folder if missing.
        ZuiViewStore CreateViewStore()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Pyre"))
                AssetDatabase.CreateFolder("Assets", "Pyre");
            var store = ScriptableObject.CreateInstance<ZuiViewStore>();
            AssetDatabase.CreateAsset(store, ViewStorePath);
            Undo.RegisterCreatedObjectUndo(store, "Create Pyre Views");
            return store;
        }

        // Preview transport: a Play/Pause button (flips its own label) + the Frame-border toggle + the Strip
        // (filmstrip / contact-sheet) toggle and, when Strip is on, a Tile-px size slider. Lives in a host that
        // rebuilds on the Strip toggle so the Tile-px slider appears/disappears (the same show/hide idiom the
        // Shape/Swarm sections use). The minimal mirror of PyreWindow's transport row.
        VisualElement transportHost;

        void BuildTransport(VisualElement root, Pyre s)
        {
            transportHost = new VisualElement();
            root.Add(transportHost);
            RebuildTransport(s);
        }

        void RebuildTransport(Pyre s)
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
                    s.previewShowFrame, v => DirtyRepaintOnly(() => s.previewShowFrame = v)),
                Z.Toggle("Strip",
                    "Show the whole animation as a contact sheet of every frame instead of one zoomed frame. Tiles "
                    + "lay out left-to-right and wrap to more rows when they overflow the width; click a tile to "
                    + "jump the transport to that frame. (No scrolling — a block taller than the view is clipped.)",
                    s.previewStrip, v => { DirtyRepaintOnly(() => s.previewStrip = v); RebuildTransport(s); }),
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
                    Undo.RecordObject(spec, "Edit Pyre");
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
                    Undo.RecordObject(spec, "Edit Pyre");
                    s.previewGifDither = v;
                    EditorUtility.SetDirty(spec);
                }));
            // Bake (G — see PyreBaker): writes a real, engine-usable PNG sprite sheet + AnimationClip beside
            // the spec asset, same technique and same guard rails as Pyre1's own Bake button.
            kids.Add(Z.Button("Bake",
                "Bake a real sprite sheet PNG + AnimationClip for this blast, beside the spec asset — the same "
                + "renderer as the live preview, so the bake is byte-identical to what you see here. Never "
                + "overwrites an existing bake; a repeat bake gets a versioned name.",
                () => PyreBaker.Bake(s)));
            transportHost.Add(Z.HGroup(kids.ToArray()));   // Play + Frame/Strip/Tile/GIF/GIF-scale/Bake as one wrapping unit-row

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
            // (previewFps) — an absolute fps rather than Pyre1's 0.1–3× multiplier, since previewFps IS Pyre's
            // playback rate; it also sets the exported GIF's rate. The readout mirrors Pyre1's "frame N/M" label.
            // Zoom is the package-wide rule (ZuiPixelStage): an integer, where 1 draws a canvas pixel at the
            // size it has in game rather than at one GUI point, and Fit is an explicit press that writes the
            // zoom it chose back into the slider. The stored float is migrated to the nearest whole zoom on
            // read; previewZoom stays preview-only state on the spec, exactly as it was.
            var zoomMs = ZuiPixelStage.ZoomControl(
                ZuiPixelStage.Migrate(s.previewZoom),
                z => DirtyRepaintOnly(() => s.previewZoom = z),
                () => ZuiPixelStage.FitZoom(lastPreviewView, Mathf.Max(1, s.Width), Mathf.Max(1, s.Height)),
                "the single-frame preview", out _, 150f);
            var speedMs = Z.MicroSlider("Speed", s.previewFps, 1f, 30f,
                "Preview playback rate in frames per second — how fast the loop plays (also the exported GIF's rate).",
                v => DirtyRepaintOnly(() => s.previewFps = Mathf.Clamp(Mathf.Round(v), 1f, 30f)), 150f,
                showValue: true, decimals: 0);
            frameReadout = Z.Text("", ZuiText.Subtle, "The frame currently shown / the total frame count.");
            // Reserved-width and hidden (not removed) when idle, so its appearance never reflows the row.
            fillReadout = Z.Text("", ZuiText.Subtle, "");
            fillReadout.style.width = 110f;
            fillReadout.style.visibility = Visibility.Hidden;
            // Delay lives HERE (a transport concern — how long the preview holds blank between loop iterations),
            // not inside the CherryFraming list below, and it applies regardless of whether CherryFraming is on:
            // a plain preview loop pauses blank for this long before restarting from frame 0 too.
            var delayMs = Z.MicroSlider("Delay", s.previewDelay, 0f, 5f,
                "Seconds the preview holds BLANK between loop iterations before restarting. 0 = no gap.",
                v => DirtyRepaintOnly(() => { s.previewDelay = Mathf.Clamp(v, 0f, 5f); ResetCherryPlayback(); }), 150f,
                showValue: true, decimals: 2);
            transportHost.Add(WrapRow(zoomMs, speedMs, delayMs, frameReadout, fillReadout));

            RefreshTransportReadout();
            RefreshFillReadout();
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
            string path = EditorUtility.SaveFilePanel("Export GIF", "", (spec.name ?? "Pyre") + ".gif", "gif");
            if (string.IsNullOrEmpty(path)) return;
            PyreGif.Export(spec, path, Mathf.Clamp(spec.previewGifScale, 1, 8), spec.previewGifDither);
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

        // Held (not just a local var) so the section-toggle bar (BuildSectionToggleBar) can read/drive its
        // IsOpen alongside the other five top-level sections.
        ZuiSection canvasSection;

        void BuildCanvas(VisualElement root, Pyre s)
        {
            // Green-header Section (matching Shape / Swarm / Modifiers) rather than a framed BoxKeyed, so the
            // window's top-level sections read consistently. The stable key keeps the fold state from orphaning
            // on a title/tooltip reword (ZuiSection persists fold per key, same idiom as the box did).
            var box = canvasSection = Z.Section("Canvas", "The output resolution, frame count, seed and background.", "pyreplus.canvas",
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
        ZuiFillControl BackgroundFillRow(Pyre s)
        {
            s.backgroundFill ??= new ZuiFill(new Color(0f, 0f, 0f, 0f));
            return Z.Fill("Background",
                s.backgroundFill,
                "The backdrop behind every layer. Default = a transparent clear (composites into a game scene). "
                + "Editing it turns on the per-pixel background fill: make it a solid colour, a gradient, noise, a "
                + "grid, dots, or a stamped sprite. Cosmetic backdrops for the PREVIEW only live in the backdrop "
                + "panel below — this one IS baked into the frames.",
                onChanged: () => { s.backgroundUseFill = true; if (spec != null) EditorUtility.SetDirty(spec); MarkDirty(); },
                onBeforeMutate: () => { if (spec != null) Undo.RecordObject(spec, "Edit Pyre"); },
                new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f));
        }

        // ── layer list (R3) ─────────────────────────────────────────────────────────
        // A compact stack at the top of the left pane. One row per layer — reorder grip, enable toggle, select
        // button, rename-in-place name field, per-row remove — plus an add / duplicate row. Each row also carries
        // its OWN Matte box, folded underneath it (BuildMatteBox), so a matte reads as a property of the layer in
        // the stack. Selection drives which layer the Shape / Swarm / Modifiers sections below edit. Mirrors
        // PyreWindow's layer-list chrome (row + folded matte box inside one drag wrap).
        // Held (not just a local var) so the section-toggle bar (BuildSectionToggleBar) can read/drive its
        // IsOpen alongside the other five top-level sections.
        ZuiSection layersSection;

        void BuildLayerList(VisualElement root)
        {
            // Green-header Section (matching Canvas / Shape / Swarm / Modifiers) rather than a framed BoxKeyed,
            // so the top-level sections read consistently. The stable key keeps fold state from orphaning on a
            // title/tooltip reword (ZuiSection persists fold per key). Per-row Matte boxes inside stay ZuiBoxes,
            // so the saved-views bar still captures those.
            var box = layersSection = Z.Section("Layers",
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
            // T-0318 — the glyph is clipped by the button's own padding, not by the row: a 24px button's
            // default 6+6 padding and 1+1 border leave 9.8px of content for a "□"/"▦" that measures 12.0.
            // Horizontal padding zeroed rather than the slot widened, the same call the Library star got in
            // T-0311 and the transport's pause glyph in T-0313.
            var matteBtn = Z.Button(layer.matteEnabled ? "▦" : "□",
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
            }).W(24f);
            matteBtn.style.paddingLeft = 0f;
            matteBtn.style.paddingRight = 0f;
            row.Add(matteBtn);

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

            // T-0318 — same clipped-glyph case as the matte toggle above, worse here: a 22px button leaves
            // 8.0px of content for a "✕" that measures 12.0, so the delete glyph was cut at every width.
            var delBtn = Z.Button("✕", "Delete this layer (undoable).", () =>
            {
                if (spec.layers.Count <= 1) { ShowNotification(new GUIContent("A Pyre asset needs at least one layer.")); return; }
                Dirty(() => spec.layers.RemoveAt(li));
                layerSel = Mathf.Clamp(layerSel, 0, spec.layers.Count - 1);
                RebuildAllForSelection();
            }).W(22f);
            delBtn.style.paddingLeft = 0f;
            delBtn.style.paddingRight = 0f;
            row.Add(delBtn);

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
                spec.layers.Add(new PyreLayer { name = $"Layer {spec.layers.Count + 1}", matteEnabled = false });
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
        VisualElement BuildMatteBox(PyreLayer layer, int li)
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
        VisualElement ChannelFlag(PyreLayer layer, MatteChannel bit, string label, string tip)
            => Z.Toggle(label, tip, (layer.matteFlags & bit) != 0, on =>
            {
                Dirty(() => layer.matteFlags = on ? (layer.matteFlags | bit) : (layer.matteFlags & ~bit));
                RebuildLayerList();
            });

        // The Shape section is a stable header + a body container we clear/refill whenever the Advanced gate
        // flips — the same mechanism BuildSwarm uses for swarmBody/RebuildSwarm, so the opt-in Travel/Spin
        // controls appear/disappear without rebuilding the whole window.
        VisualElement shapeBody;
        // Held (not just a local var) so the section-toggle bar (BuildSectionToggleBar) can read/drive its
        // IsOpen alongside the other five top-level sections.
        ZuiSection shapeSection;

        void BuildShape(VisualElement root, Pyre s)
        {
            var sec = shapeSection = Z.Section("Shape", "The particle's own look — colour, opacity and size over its life.",
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
            ("Sparkle", ShapeForm.Sparkle, "sparkle"), ("Sprite", ShapeForm.Sprite, "image"),
            ("Playback 3D", ShapeForm.Playback3D, "play"),
        };
        const string Forms3DTip = "True-3D lit solids sharing the Gem's facet lighting (tilt, light, edge lines, glows). "
            + "Gem = a faceted crystal, Box = a cuboid, Pyramid = a square pyramid, Can = a cylinder, Orb = a sphere.";
        const string Forms2DTip = "Flat 2D forms. Disc = a soft disc, Crescent = a disc with an offset bite, Ring = a "
            + "tilted annulus (a Saturn ring), Streak = a comet-tail capsule, Star = a filled star polygon, Polygon = a "
            + "filled regular convex N-gon (triangle / square / hexagon /… by a sides count).";
        const string FormsSpecialTip = "Standalone forms. Text = a string as extruded SDF letters (one particle per "
            + "character). Fire / Fireball = stateful flame simulations (built-in emitters, no swarm). Sparkle = "
            + "twinkling lit cells. Sprite = a stamped image. Playback 3D (PROOF OF CONCEPT) = a real 3D animation "
            + "prefab (a ParticleSystem burst) played/scrubbed live in the preview, with an optional pixelated "
            + "downsample — NOT baked at runtime yet. Plug-in forms (the columns to the right) are self-contained "
            + "modules with their own dials; whole-layer ones are placed by the Swarm, one instance per particle.";
        // Coalesce render-mode selector. Off / Fuse (slice 1, MetaBlob) / Ramp (slice 2, HeightBalls). Order matches
        // the LayerCoalesce enum (Off=0, Fuse=1, Ramp=2), so the MiniRadio index casts straight to the enum.
        static readonly string[] CoalesceChoices = { "Off", "Fuse", "Ramp" };

        // Build one labelled form-picker subgroup row (#59 Part B). `sel` is -1 when the current form isn't in this
        // group, so its MiniRadio shows NOTHING selected there; because every pick calls RebuildShape() (below), all
        // three rows are rebuilt on each change and exactly ONE ever shows a highlight. Picking maps the in-group index
        // straight to its ShapeForm; SteadyDefaultFillForSolid runs inside the SAME Dirty block so one Undo reverts both
        // the form and any auto-steadied fill together (the pre-existing FIX 1 behaviour, preserved).
        // The shape-FORM picker as a header context menu (opened by the "Shape" title's caret button or a right-click
        // on the title). The 3D / 2D / Special groups are stacked as three side-by-side COLUMNS, each form an
        // icon + label row with the current one checked. Picking sets s.shapeForm exactly as the old radio rows did.
        void ShowShapeMenu(PyreLayer s, VisualElement anchor)
        {
            if (s == null) return;
            var menu = Z.Menu(anchor).Width(490f);
            menu.Custom((body, close) =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.Add(FormColumn(s, "3D", Forms3DTip, Forms3D, close));
                row.Add(FormColumn(s, "2D", Forms2DTip, Forms2D, close));
                row.Add(FormColumn(s, "Special", FormsSpecialTip, FormsSpecial, close));
                // Plug-in forms (PyreForm) — discovered by assembly scan, one column per group (see .Forms.cs).
                AddFormColumns(row, s, close);
                body.Add(row);
            });
            menu.Show();
        }

        VisualElement FormColumn(PyreLayer s, string title, string tip,
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
                // A plug-in form, when set, overrides the enum — so the enum columns show no check while one is active.
                var check = new Label(s.form == null && s.shapeForm == f ? "✓" : "") { pickingMode = PickingMode.Ignore };
                check.AddToClassList("zui-menu__check");
                item.Add(check);
                var img = Z.Icon(icon, 14f);
                if (img != null) { img.pickingMode = PickingMode.Ignore; img.style.marginRight = 5f; item.Add(img); }
                var lbl = new Label(label) { pickingMode = PickingMode.Ignore };
                lbl.AddToClassList("zui-menu__label");
                item.Add(lbl);
                item.AddManipulator(new Clickable(() =>
                {
                    Dirty(() => { s.form = null; s.shapeForm = f; SteadyDefaultFillForSolid(s); });
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

            // FORM selector — a header context menu (the "Shape" title's caret button / right-click), not in-body
            // radio rows. See ShowShapeMenu; the ShapeForm enum + its 3D / 2D / Special grouping are unchanged.
            // Every card below lives in PyreShapeCards (T-0183) so Shaper's Shape section can draw the same ones;
            // this window supplies them a host (the IPyreShapeCardHost implementation at the foot of this file).
            PyreShapeCards.BuildLifeWindow(this, s);

            // Plug-in form (PyreForm): the layer's look is the form object. It shares the Fill (its colour source, unless
            // the form carries its own ramps) and Alpha (overall opacity) rows with every other form, then its own
            // reflection-drawn card; a whole-layer
            // form has no particle to size or position, so Size / Position / Border do not apply.
            if (s.form != null)
            {
                // A form with its own ramps (UsesFill == false) gets no Fill row: a dead control is worse than none.
                if (s.form.UsesFill)
                    shapeBody.Add(FillRow("Fill",
                        $"The {s.form.DisplayName} form's colour source. {s.form.Description}", s.shapeFill,
                        new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));
                shapeBody.Add(Val("Alpha", "Overall opacity over the layer's life (multiplies the form's output alpha).", s.alpha, 0f, 1f));
                BuildFormCard(s);
                return;
            }

            PyreShapeCards.BuildLegacyForm(this, s);
        }

        // Playback 3D form box (PROOF OF CONCEPT) — configures a real 3D animation prefab (typically a
        // ParticleSystem fire/explosion burst) that the preview island plays/scrubs live via
        // PyrePlayback3DPreview (see that file), instead of the normal composited 2D canvas. There is no
        // runtime bake for this form yet (PyreRenderer's Playback3D case is a documented stub) — everything
        // here only drives the EDITOR preview. The prefab is picked via an object-reference field (never typed by
        // name, per the "never type a reference string" rule); its ParticleSystem(s) are driven directly by
        // Speed/Scale/Tint through PreviewRenderUtility, no reflection needed (ParticleSystem.MainModule exposes
        // all of them).
        void BuildPlaybackBox(PyreLayer s)
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

        // ── Swarm ──────────────────────────────────────────────────────────────────
        // The section is a stable header + a body container we clear/refill on every toggle/mode/kind
        // change, so the conditional controls appear/disappear without rebuilding the whole window. The section
        // itself is held so RebuildSwarm can rebind its HEADER checkbox (the swarm enable) to the selected layer.
        ZuiSection swarmSection;
        VisualElement swarmBody;
        static readonly string[] SwarmModeLabels = { "Area", "Path" };
        static readonly string[] SwarmOrientLabels = { "None", "Outward", "Tangent" };
        static readonly string[] SwarmTimingLabels = { "Window", "Frames" };

        void BuildSwarm(VisualElement root, Pyre s)
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
                + "hidden — each letter rides one swarm position. For a whole-layer plug-in form each particle IS one instance (an Inferno blast, say).",
                v =>
                {
                    Dirty(() => s.swarmEnabled = v);
                    RebuildSwarm();
                    // A plug-in form's card hides/shows its [PyreSwarmOnly] dials on this toggle.
                    if (s.form != null) RebuildShape();
                });

            // Whole-layer plug-in forms: the swarm IS the placement — one instance per particle (off = one centred). The
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

            // Distribution + spawn order: two independent axes over the same established spots. Distribution
            // — 0 = a neat, shape-appropriate ordered fill (concentric rings for a disc; a dedicated lattice
            // per polygon kind — a triangular lattice, a grid, a hex lattice, or concentric rings again for
            // Pentagon, which has no regular tiling of its own), 1 = today's uniform-random scatter (Path
            // mode ignores this; its placement is already ordered, by outline progress).
            var distRow = new List<VisualElement>();
            distRow.Add(Z.MicroSlider("Distribution", s.swarmChaos, 0f, 1f,
                "How the swarm fills its shape: 0 = a neat, shape-appropriate ordered arrangement — concentric "
                + "rings for a disc, a dedicated lattice for each polygon shape. 1 = today's uniform-random "
                + "scatter. In between blends the two. Path mode ignores this — its placement is already "
                + "ordered, by outline progress.",
                v => Dirty(() => s.swarmChaos = v), 150f, showValue: true));
            distRow.Add(Z.MicroSlider("Spawn order", s.swarmSpawnChaos, 0f, 1f,
                "The ORDER particles are revealed in, independent of where they sit: 0 = neighbour order — "
                + "spawns hop from one spot to the next-closest one. 1 = a full shuffle — which spot appears "
                + "next is decoupled from spatial position.",
                v => Dirty(() => s.swarmSpawnChaos = v), 150f, showValue: true));
            bool showReverse = s.swarmSpawnMode == SwarmSpawnMode.Area && s.swarmShapeKind != SwarmShapeKind.Line;
            if (showReverse)
                distRow.Add(Z.Toggle("Reverse", s.swarmShapeKind == SwarmShapeKind.Circle
                        || s.swarmShapeKind == SwarmShapeKind.Custom
                        ? "Which end the ring fill starts from: off = centre-out (innermost ring first), on = "
                          + "edge-in (outermost ring first)."
                        : "Which corner the grid fill starts from — flips both row and column order.",
                    s.swarmGridReverse, v => Dirty(() => s.swarmGridReverse = v)));
            if (s.swarmSpawnMode == SwarmSpawnMode.Path) distRow.RemoveAt(0);   // Distribution is Area-only
            swarmBody.Add(Z.HGroup(distRow.ToArray()));

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

        // ── PyreShapeCards host (T-0183) ─────────────────────────────────────────
        // The cards were extracted verbatim into PyreShapeCards so Shaper can draw the same ones; these are
        // the members they used to call inline, so what this window draws and how it records Undo is
        // unchanged. Implemented EXPLICITLY: every one of them forwards to a member this window already had
        // under the same name, and an implicit implementation would collide with it.
        VisualElement IPyreShapeCardHost.Body => shapeBody;
        int IPyreShapeCardHost.CanvasSize => spec != null ? spec.canvasSize : 64;
        int IPyreShapeCardHost.FrameCount => spec != null ? spec.frameCount : 0;
        void IPyreShapeCardHost.Dirty(System.Action apply) => Dirty(apply);
        void IPyreShapeCardHost.MarkDirty() => MarkDirty();
        void IPyreShapeCardHost.RecordUndo() { if (spec != null) Undo.RecordObject(spec, "Edit Pyre"); }
        // Pyre's own window has no shape tree above the layer, so this box IS the layer's placement (T-0265).
        bool IPyreShapeCardHost.HostOwnsPlacement => false;
        void IPyreShapeCardHost.RebuildShape() => RebuildShape();
        void IPyreShapeCardHost.RebuildSwarm() => RebuildSwarm();
        VisualElement IPyreShapeCardHost.Val(string label, string tooltip, ZUIValue v, float lo, float hi, bool cyclic)
            => Val(label, tooltip, v, lo, hi, cyclic);
        VisualElement IPyreShapeCardHost.Val2D(string label, string tooltip, ZUIValue x, ZUIValue y, ZuiValue2DControl.Options o)
            => Val2D(label, tooltip, x, y, o);
        ZuiFillControl IPyreShapeCardHost.FillRow(string label, string tooltip, ZuiFill fill, ZuiFillControl.Options opt)
            => FillRow(label, tooltip, fill, opt);
        ZuiFillControl IPyreShapeCardHost.SlotFill(string label, string tooltip, ZuiFill fill)
            => SlotFill(label, tooltip, fill);
        void IPyreShapeCardHost.BuildPlaybackBox(PyreLayer s) => BuildPlaybackBox(s);

        // Still called from this window outside the Shape section, so they forward to the one copy.
        static VisualElement WrapRow(params VisualElement[] kids) => PyreShapeCards.WrapRow(kids);
        static bool IsSolidForm(ShapeForm f) => PyreShapeCards.IsSolidForm(f);
        static bool SteadyDefaultFillForSolid(PyreLayer layer) => PyreShapeCards.SteadyDefaultFillForSolid(layer);


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
                currentFrame = () => frame,   // a folded envelope reads out the value at the playhead
            };
            return Z.Value(label, v, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre"));
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
            return Z.Value(label, v, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre"));
        }

        // 2D analog of Val — an animatable XY pair, same Undo-record + preview-dirty wiring.
        VisualElement Val2D(string label, string tooltip, ZUIValue x, ZUIValue y, ZuiValue2DControl.Options o)
            => Z.Value2D(label, x, y, o, tooltip, () => MarkDirty(), () => Undo.RecordObject(spec, "Edit Pyre"));

        // A Z.Fill row (ZuiFill editor) wired to the same Undo/dirty/preview contract as Val: record the asset once
        // per gesture (onBeforeMutate), then dirty + repaint (onChanged). The control mutates the ZuiFill instance
        // directly, so there's no explicit setter.
        ZuiFillControl FillRow(string label, string tooltip, ZuiFill fill, ZuiFillControl.Options opt = null)
            => Z.Fill(label, fill, tooltip,
                () => { if (spec != null) EditorUtility.SetDirty(spec); MarkDirty(); },
                () => { if (spec != null) Undo.RecordObject(spec, "Edit Pyre"); },
                opt);

        // Compact Z.Fill for the Solid box's slot fills (spec / line / edge / inner), packed beside their sliders.
        ZuiFillControl SlotFill(string label, string tooltip, ZuiFill fill)
            => FillRow(label, tooltip, fill, new ZuiFillControl.Options().WithWidth(96f));

        // The Swarm Timing tooltip, composed for the CURRENT mode (the swarm rebuilds on change), so it names only
        // the mode the user is in (house rule: no if-lists in a tooltip).
        static string SwarmTimingTooltip(PyreLayer s)
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
        static bool SwarmOrientIsInert(PyreLayer s, Pyre spec)
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
        static string SwarmOrientInertTooltip(PyreLayer s)
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
        static string SwarmOrientTooltip(PyreLayer s)
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

        void Dirty(System.Action apply)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Pyre");
            apply();
            EditorUtility.SetDirty(spec);
            MarkDirty();
        }

        // An edit that changes how the preview is LAID OUT / PLAYED (not the rendered frames): record Undo +
        // SetDirty + repaint, but do NOT set previewDirty, so the frame cache survives. Used by the filmstrip's
        // Tile-px slider, zoom, speed, the loop delay and every CherryFraming sequence edit — none of them is a
        // render input, and invalidating the cache for them would re-render the whole clip for nothing.
        void DirtyRepaintOnly(System.Action apply)
        {
            if (spec == null) return;
            Undo.RecordObject(spec, "Edit Pyre");
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

        // DrawPreview + the Swarm authoring overlay live in PyreWindow.Preview.cs.
    }
}
