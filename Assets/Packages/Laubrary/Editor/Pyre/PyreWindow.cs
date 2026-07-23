using UnityEditor;
using UnityEngine;
using Laubrary.Pyre;
using Laubrary.PreviewStage;
using Laubrary.AssetKit.Editor;
using Laubrary.PreviewKit.Editor;

namespace Laubrary.Pyre.Editor
{
    /// Pyre: author a pixel-art explosion as a flat back-to-front stack of Layers and watch it loop live. Left =
    /// a drag-resizable pane with the blast dials, a Layer list (select / enable / dup / delete / add) and the
    /// selected layer's inspector (animatable values via ZUIValueControl); right = a resizable preview viewport
    /// with a chooseable backdrop, a zoom, and play / scrub / retime / speed transport. The preview draws the exact
    /// same BlastRenderer frames the baker and the runtime player use, so preview == bake == runtime.
    public class PyreWindow : LaubraryAssetWindow<BlastSpec>
    {
        [MenuItem("Laubrary/Pyre")]
        public static void Open() => GetWindow<PyreWindow>("Pyre");

        /// Open the window focused directly on a specific blast — the entry point other tools (e.g. the
        /// Zoetrope.Pyre editor bridge) use to jump straight into previewing/editing a referenced BlastSpec.
        public static void OpenFor(BlastSpec spec)
        {
            var w = GetWindow<PyreWindow>("Pyre");
            if (spec != null) w.SetAsset(spec);
        }

        BlastSpec spec => Current;   // the base owns the current asset; alias for the dial/preview code

        protected override string TypeLabel => "Blast";
        protected override string NewAssetName => "New Pyre";
        protected override string DefaultFolder => "Assets/Pyre";
        // layerSel/leftScroll/frame are per-asset (BlastSpec.previewX below) and load in with the new asset, so
        // there's nothing to reset here beyond scrub — a transient "actively being dragged" interaction state,
        // not a "where was I" position, so it always starts fresh regardless of which asset is now current.
        protected override void OnAssetChanged() { scrub = -1; DisposePreviewSubject(); }
        protected override void InitializeNewAsset(BlastSpec item) => item.AddExampleContent();
        protected override Texture2D RenderThumbnail(BlastSpec item)
        {
            int mid = Mathf.Clamp(item.frameCount / 2, 0, Mathf.Max(0, item.frameCount - 1));
            var tex = BlastRenderer.RenderFrameTexture(item, mid);
            tex.filterMode = FilterMode.Point;
            return tex;
        }
        // Browser thumbnails loop the actual baked animation instead of sitting on one static frame — Pyre's own
        // opt-in via AssetKit's AnimateThumbnails hook (see LaubraryAssetWindow<T>); every other AssetKit tool is
        // untouched by this. Paced by the asset's own Preview fps, same clock every thumbnail shares
        // (EditorApplication.timeSinceStartup) so they're not each individually timed, just cheaply in sync.
        protected override bool AnimateThumbnails => true;
        protected override void UpdateAnimatedThumbnail(BlastSpec item, Texture2D tex, double time)
        {
            if (item == null || item.frameCount <= 1) return;
            float fps = Mathf.Max(1f, item.previewFps);
            int f = Mathf.FloorToInt((float)(time * fps)) % item.frameCount;
            tex.SetPixels32(BlastRenderer.RenderFrame(item, f));
            tex.Apply();
        }

        // layout (serialized so it sticks — window chrome, not per-asset content)
        [SerializeField] float leftWidth = 340f;
        [SerializeField] float previewHeight = 320f;

        // Zoom, preview backdrop, and the test-backdrop sprite (below) are all PER-ASSET, not per-window — they
        // live on the BlastSpec itself (BlastSpec.previewX fields) so switching assets, or closing and reopening
        // the window, always shows that blast's own preview setup instead of whatever the window last had. Each
        // property below is a thin proxy so every existing call site keeps reading/writing a plain-looking field.
        float zoom { get => spec != null ? spec.previewZoom : 4f; set { if (spec != null) spec.previewZoom = value; } }

        // Preview backdrop (Solid/Gradient/Image): once a Test-background asset (stageBg, below) exists, these
        // proxy ITS fields instead of the legacy per-BlastSpec ones — so Save/Recall on that one shared asset
        // carries the WHOLE background (style + sprites) as a single recallable preset, not just the sprites.
        // Falls back to the legacy spec.previewBgX fields whenever stageBg is null (no preset saved/recalled yet)
        // so every already-authored blast keeps rendering exactly as before with zero migration.
        static PreviewBgMode ToPyreMode(PreviewBackground.Mode m) => m switch
        {
            PreviewBackground.Mode.Gradient => PreviewBgMode.Gradient,
            PreviewBackground.Mode.Image => PreviewBgMode.Image,
            _ => PreviewBgMode.Solid,   // None (another tool's unset preset) or Solid both read as Solid here
        };
        static PreviewBackground.Mode ToStageMode(PreviewBgMode m) => m switch
        {
            PreviewBgMode.Gradient => PreviewBackground.Mode.Gradient,
            PreviewBgMode.Image => PreviewBackground.Mode.Image,
            _ => PreviewBackground.Mode.Solid,
        };
        PreviewBgMode bgMode
        {
            get => stageBg != null ? ToPyreMode(stageBg.mode) : (spec != null ? spec.previewBgMode : PreviewBgMode.Solid);
            set { if (stageBg != null) stageBg.mode = ToStageMode(value); else if (spec != null) spec.previewBgMode = value; }
        }
        Color bgSolid
        {
            get => stageBg != null ? stageBg.solid : (spec != null ? spec.previewBgSolid : Color.black);
            set { if (stageBg != null) stageBg.solid = value; else if (spec != null) spec.previewBgSolid = value; }
        }
        Gradient bgGradient
        {
            get
            {
                if (stageBg != null) return stageBg.gradient ??= DefaultBgGradient();
                if (spec == null) return DefaultBgGradient();
                return spec.previewBgGradient ??= DefaultBgGradient();
            }
            set { if (stageBg != null) stageBg.gradient = value; else if (spec != null) spec.previewBgGradient = value; }
        }
        Texture2D bgImage
        {
            get => stageBg != null ? stageBg.image : (spec != null ? spec.previewBgImage : null);
            set { if (stageBg != null) stageBg.image = value; else if (spec != null) spec.previewBgImage = value; }
        }
        Color bgImageTint
        {
            get => stageBg != null ? stageBg.imageTint : (spec != null ? spec.previewBgImageTint : Color.white);
            set { if (stageBg != null) stageBg.imageTint = value; else if (spec != null) spec.previewBgImageTint = value; }
        }
        float bgImageZoom
        {
            get => stageBg != null ? stageBg.imageZoom : (spec != null ? spec.previewBgImageZoom : 1f);
            set { if (stageBg != null) stageBg.imageZoom = value; else if (spec != null) spec.previewBgImageZoom = value; }
        }
        Vector2 bgImagePos
        {
            get => stageBg != null ? stageBg.imagePos : (spec != null ? spec.previewBgImagePos : Vector2.zero);
            set { if (stageBg != null) stageBg.imagePos = value; else if (spec != null) spec.previewBgImagePos = value; }
        }
        bool showFrame { get => spec != null && spec.previewShowFrame; set { if (spec != null) spec.previewShowFrame = value; } }

        // Compact-control width caps (see EDITOR_TOOL_CONVENTIONS.md: no infinite-width sliders/colour
        // fields) — a slider or swatch stretched across an 1000+px wide panel is harder to read at a glance
        // than one sized to what its own content actually needs, so combo rows below give each control an
        // explicit width instead of GUILayout's default "fill whatever's left" behaviour.
        const float CompactPadSize = 68f;   // 56 * 1.2 — the settings area can afford ~20% more vertical room
        const float CompactSliderWidth = 150f;
        const float CompactColorWidth = 130f;
        // A ValRow (ZUIValueControl-based, ZUIValue/MultiCont fields) needs more room than a MicroSlider-based
        // compact field (CompactSliderWidth above, used for the plain-float preview transport row) — it ALSO
        // reserves a ~40px numeric value field (ZUISliderDef.valueWidth, a fixed per-STYLE width, not adaptive
        // to the field's actual min/max range — a real gap worth fixing in ZUI itself later) plus a 24px "⋯"
        // config button on top of label + slider. CompactValRowWidth is the FLOOR for a single field — never
        // go narrower than this even in a crowded 5+-field row.
        const float CompactValRowWidth = 200f;   // unused fallback default (every call site passes width: explicitly)
        // Sizing target for a packed row's fields, LIVE from the pane's actual current width (`leftWidth`,
        // user-resizable via the splitter below) — NOT a static guess. A flat 760px guess was the bug: totally
        // untethered from leftWidth's own real value (default 340f), it demanded far more room than the pane
        // ever had, so packed rows overflowed the visible pane and forced a horizontal scrollbar — reported as
        // controls clipped off past the edge (Gradient's Offset field, the +Add modifier/Paste row) even after
        // widening the pane well past half the screen. `-48` is a rough allowance for the scroll view's own
        // vertical scrollbar (~14px) plus a box's inner padding/indent (~2×~17px) that eats into what's left.
        float PaneRowBudget => Mathf.Max(120f, leftWidth - 48f);
        // Width to give EACH field when `fieldCount` of them share one packed row — a straight division of the
        // REAL budget above. Deliberately NOT floored above what the budget can actually support (the previous
        // bug): clamped only to a sane usable range, 90 (a slider needs some room to be draggable) to 420 (so a
        // lightly-packed 1-2 field row doesn't balloon just because the budget technically allows it). This
        // guarantees fieldCount × GroupFieldWidth(fieldCount) never exceeds PaneRowBudget except in the most
        // extreme case (leftWidth at its 240px minimum clamp with a 5-field row, e.g. RoseRings) — keep the
        // pane reasonably sized (as the screenshots already show) and this stays overflow-free.
        float GroupFieldWidth(int fieldCount) => Mathf.Clamp(PaneRowBudget / Mathf.Max(1, fieldCount), 90f, 420f);
        static readonly string[] BgModeLabels = { "Solid", "Gradient", "Image" };   // matches PreviewBgMode order

        // add-layer shape picker
        [SerializeField] LayerShape addShape = LayerShape.Disc;

        // Which layer's inspector is open, and the left pane's scroll position — per-asset (like zoom/bgMode
        // above), so reopening an asset returns to whatever you were last working on instead of always layer 0.
        // layerSel explicitly dirties the asset on change (a discrete, meaningful pick worth reliably saving to
        // disk); leftScroll/frame below don't (they change continuously while dragging/playing — dirtying on
        // every tick would leave the asset permanently "modified" just from watching a preview play).
        int layerSel
        {
            get => spec != null ? spec.previewLayerSel : 0;
            set { if (spec != null && spec.previewLayerSel != value) { spec.previewLayerSel = value; EditorUtility.SetDirty(spec); } }
        }
        Vector2 leftScroll { get => spec != null ? spec.previewScroll : Vector2.zero; set { if (spec != null) spec.previewScroll = value; } }

        // splitters
        bool dragLeft, dragPreview;
        bool draggingOrigin;   // dragging the origin/pivot ✛ handle in the preview
        bool draggingPan;      // middle-dragging to pan the animation frame
        [SerializeField] float originMarkerAlpha = 0.95f;   // preview-only: origin ✛ opacity
        [SerializeField] Vector2 previewPan;                // preview-only: offset the animation frame (middle-drag)
        // Auto-computed each Repaint (see DrawPreview) when a live preview subject is configured — an EXTRA
        // offset, on top of previewPan, that lands the blast's origin marker on the subject's attach point.
        // DrawOriginHandle folds it in too so the ✛ handle stays visually consistent with the shifted frame.
        Vector2 subjectAlignOffset;
        // Reusable sprite test-backdrop — per-asset (BlastSpec.previewStageBg), like zoom/bgMode above. Stored on
        // the asset as a plain Object since PreviewBackground is an editor-only type Runtime code can't reference.
        PreviewBackground stageBg
        {
            get => spec != null ? spec.previewStageBg as PreviewBackground : null;
            set { if (spec != null) spec.previewStageBg = value; }
        }
        int stageSel = -1;
        bool draggingStage;

        // Optional LIVE animated preview subject (see IPyrePreviewSubject) — resolved lazily and cached
        // against the (asset, clip, attachId) triple it was built from, so it only rebuilds when one of
        // those actually changes, not every repaint. Entirely inert (previewSubject stays null, zero cost)
        // unless a bridge module has registered a resolver AND the asset has a previewSubjectAsset assigned.
        IPyrePreviewSubject previewSubject;
        UnityEngine.Object previewSubjectFor;
        string previewSubjectClipFor, previewSubjectAttachFor;

        IPyrePreviewSubject ResolvePreviewSubject()
        {
            if (spec == null || spec.previewSubjectAsset == null || PyrePreviewSubjectProvider.Resolve == null)
            { DisposePreviewSubject(); return null; }

            bool stale = previewSubject == null || previewSubjectFor != spec.previewSubjectAsset
                         || previewSubjectClipFor != spec.previewSubjectClip || previewSubjectAttachFor != spec.previewSubjectAttachId;
            if (stale)
            {
                DisposePreviewSubject();
                previewSubject = PyrePreviewSubjectProvider.Resolve(spec);
                previewSubjectFor = spec.previewSubjectAsset;
                previewSubjectClipFor = spec.previewSubjectClip;
                previewSubjectAttachFor = spec.previewSubjectAttachId;
            }
            return previewSubject;
        }

        // Lazily created, torn down only when the WINDOW closes (not per-asset like previewSubject) — a real
        // Editor resource (an isolated scene + render target) wrapping UnityEditor.PreviewRenderUtility, so
        // whatever subject is currently resolved renders through an actual camera + actual gameplay components
        // instead of hand-rolled IMGUI pivot/scale math. See LiveScenePreview's own doc for why.
        LiveScenePreview livePreview;
        LiveScenePreview LivePreview => livePreview ??= new LiveScenePreview();

        void DisposePreviewSubject()
        {
            previewSubject?.Dispose();
            previewSubject = null;
            previewSubjectFor = null;
        }
        bool placeMetaMode;    // MetaBlob: clicking the preview drops orbs
        [SerializeField] bool showMetaMarkers = true;   // MetaBlob: draw the orb rings + numbers over the preview
        int metaSel = -1;
        bool draggingMetaOrb;
        SmudgeModifier paintSmudge;   // the Smudge modifier currently recording a stroke in the preview (null = none)
        bool draggingSmudge;          // true while a smudge stroke is being dragged out
        PinWarpModifier editPin;      // the Pin warp modifier currently being authored in the preview (null = none)
        int pinSel = -1;
        bool draggingPin;
        // The Curl / Vortex field (progress) modifier currently being authored in the preview (null = none) — typed
        // as the shared IVortexHost interface so either modifier's vortex list can be armed for the same click/
        // drag/gizmo code below.
        IVortexHost editCurl;
        int vortexSel = -1;
        bool draggingVortex;
        int draggingLayer = -1;   // index of the layer being drag-reordered, or -1
        int draggingMod = -1;     // index of the modifier being drag-reordered, or -1
        string draggingModList;   // idp of the modifier list that drag belongs to (layer mods vs global mods)

        // playback
        double lastTime;
        float acc;
        // Current preview frame is also per-asset — coming back to an asset resumes wherever its playhead was.
        int frame { get => spec != null ? spec.previewFrame : 0; set { if (spec != null) spec.previewFrame = value; } }
        bool playing = true;
        // fps/speed are also per-asset (BlastSpec.previewFps/previewSpeed) — see the zoom/bgMode proxies above.
        float fps { get => spec != null ? spec.previewFps : 12f; set { if (spec != null) spec.previewFps = value; } }
        float speed { get => spec != null ? spec.previewSpeed : 1f; set { if (spec != null) spec.previewSpeed = value; } }
        int scrub = -1;                 // >=0 means the user is holding a scrubbed frame (paused)
        Texture2D previewTex;
        Texture2D shapePreviewTex;      // isolated single-shape preview (see DrawShapePreview)
        Rect lastView;                  // remembered for the Fit button

        static readonly string[] ShapeLabels = { "Disc", "Crescent", "Sparkle", "Bars", "Sprite", "Meta blob" };
        static readonly string[] BarDecayLabels = { "Contract", "Dissolve" };
        static readonly string[] ScatterModeLabels = { "Area", "Ring", "Rosing" };
        static readonly string[] RingOrderLabels = { "Sequential", "Random" };

        // Captured on the Layout event only so the control set can't change between Layout and Repaint of the same
        // frame (IMGUI reflow hazard). starLayout = any Bars layer has Star (gates the auto-canvas readout);
        // barStarLayout = the SELECTED layer is a star Bars layer (gates its Arms/Spread rows). See OnZUI.
        bool starLayout, barStarLayout, discHollowLayout, ringLayout, rosingLayout, fuseLayout, sparkleBlobsLayout;

        protected override void OnZUIEnable()
        {
            lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= Tick;
            if (previewTex != null) { DestroyImmediate(previewTex); previewTex = null; }
            if (shapePreviewTex != null) { DestroyImmediate(shapePreviewTex); shapePreviewTex = null; }
            DisposePreviewSubject();
            livePreview?.Dispose();
            livePreview = null;
        }

        int FrameCount => spec != null ? Mathf.Max(1, spec.frameCount) : 1;
        int CurrentFrame() => Mathf.Clamp(scrub >= 0 ? scrub : frame, 0, FrameCount - 1);

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            var subject = !IsBrowsing ? ResolvePreviewSubject() : null;
            if (playing && spec != null && scrub < 0)
            {
                acc += dt * fps * speed;
                while (acc >= 1f)
                {
                    acc -= 1f;
                    int next = (frame + 1) % FrameCount;
                    if (next == 0) subject?.Restart();   // blast looped — restart the subject alongside it,
                    frame = next;                         // matching them starting together in-game
                }
                subject?.Tick(dt * speed);
                Repaint();
            }
            // Keep the preview repainting while paused too (not the browser) so the origin ✛ marker flashes.
            else if (spec != null && !IsBrowsing && originMarkerAlpha > 0.001f) Repaint();
        }

        protected override void DrawAsset(BlastSpec asset)
        {
            // editPin/editCurl/paintSmudge are "armed for editing in the preview" references to a specific
            // modifier instance — but the ONLY place that ever cleared them was the modifier list's own "X"
            // remove button. Deleting the whole LAYER that owned the armed modifier, an Undo that removed it,
            // or switching to a different asset entirely all left the reference dangling: the C# object is
            // still alive (still referenced), so its stroke/vortex data kept right on rendering in the preview
            // with no in-UI way to clear it (reported: leftover Smudge strokes + a Curl vortex marker/gizmo
            // still visible after removing that Curl modifier). Self-heals every frame instead of trying to
            // patch every possible removal path individually.
            ValidateArmedModifiers();

            // Capture the star gate on Layout only, so the width/height block below has a stable control count
            // across this frame's Layout and Repaint passes even if the Orbit/Star radio is clicked.
            if (Event.current.type == EventType.Layout)
            {
                starLayout = AnyStarLayer();
                var sel = layerSel >= 0 && layerSel < asset.layers.Count ? asset.layers[layerSel] : null;
                barStarLayout = sel != null && sel.shape == LayerShape.Bars && sel.star;
                discHollowLayout = sel != null && (sel.shape == LayerShape.Disc || sel.shape == LayerShape.SparkleField) && sel.hollow;
                ringLayout = sel != null && sel.scatterMode == ScatterMode.Ring;
                rosingLayout = sel != null && sel.scatterMode == ScatterMode.Rosing;
                fuseLayout = sel != null && sel.shape == LayerShape.Disc && sel.fuse && (ringLayout || rosingLayout);
                sparkleBlobsLayout = sel != null && sel.shape == LayerShape.SparkleField && sel.sparkleBlobs;
            }

            // Wider label column so the longer ValRow labels ("Taper (centre↔edge)", "Sparkle density", …) don't
            // clip — the text-overflow rule from the UIAudit heuristics applied to this editor window.
            EditorGUIUtility.labelWidth = 112f;

            EditorGUILayout.BeginHorizontal();
            DrawLeft();
            DrawVerticalSplitter();
            using (ZUI.PaddedArea()) DrawPreview();
            EditorGUILayout.EndHorizontal();

            // Any edit (layer toggle, a dial, a deform value…) must rebuild the preview even while paused —
            // the preview texture is only regenerated on Repaint, so schedule one whenever something changed.
            if (GUI.changed) Repaint();
        }

        void ValidateArmedModifiers()
        {
            if (spec == null) { editPin = null; editCurl = null; paintSmudge = null; return; }
            if (editPin != null && !ModifierStillExists(editPin)) { editPin = null; pinSel = -1; draggingPin = false; }
            if (editCurl != null && !ModifierStillExists(editCurl as PyreModifier)) { editCurl = null; vortexSel = -1; draggingVortex = false; }
            if (paintSmudge != null && !ModifierStillExists(paintSmudge)) { paintSmudge = null; draggingSmudge = false; }
        }

        bool ModifierStillExists(PyreModifier m)
        {
            if (spec.layers != null)
                foreach (var l in spec.layers)
                    if (l != null && l.modifiers != null && l.modifiers.Contains(m)) return true;
            if (spec.globalModifiers != null && spec.globalModifiers.Contains(m)) return true;
            return false;
        }

        bool AnyStarLayer()
        {
            if (spec == null || spec.layers == null) return false;
            foreach (var l in spec.layers)
                if (l != null && l.enabled && l.shape == LayerShape.Bars && l.star) return true;
            return false;
        }

        // ── left: dials + layer list + selected-layer inspector ──────────────────
        void DrawLeft()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            // Hard-cap every row's width to what's actually usable inside this scroll view. Without this, ANY
            // child using ExpandWidth(true) (a plain full-width Slider/ValRow, a stretchy Button, a TextField)
            // sizes itself against the scroll view's own VIRTUAL content width — which can silently balloon
            // past leftWidth the moment some row demands more room, dragging every OTHER uncapped row along
            // with it and forcing a horizontal scrollbar (reported repeatedly: modifier sliders hundreds of
            // pixels wide with the numeric box pushed past the edge, "+ Add modifier"/Paste, the layer name
            // field, Gradient's Offset). MaxWidth makes leftWidth an actual ceiling for this whole subtree,
            // not just a hint any one leaf control could unwittingly ignore. `-18` leaves room for the scroll
            // view's own vertical scrollbar.
            EditorGUILayout.BeginVertical(GUILayout.MaxWidth(leftWidth - 18f));
            EditorGUI.BeginChangeCheck();
            // Snapshots spec BEFORE any control below can mutate it — this is the ONE thing EndChangeCheck's
            // SetDirty below was missing, so every dial edit in this whole tree (Blast settings, Global
            // modifiers, layer list, and everything DrawSelectedLayer draws — shape params, RoseRings, Colour/
            // Alpha, per-layer Modifiers) had NO undo support at all: only the structural list ops (Add/Remove/
            // Reorder layer or modifier) called Undo.RecordObject of their own accord. Recording unconditionally
            // every repaint is the standard pattern for a hand-rolled (non-SerializedProperty) editor — cheap,
            // and Unity coalesces repeated no-op records so a slider drag becomes ONE undo step, not one per
            // frame dragged.
            Undo.RecordObject(spec, "Edit Pyre Blast");

            Label("Blast", ZUI.ZTextStyle.SectionHeader);
            // ZUI.IntField/FloatField measure their own label width (in a Flow row), so long labels can't be
            // clipped. Width/Height/PPU share one row — all three are "how big/dense is this canvas," and none
            // needs more than a compact typed field (not a slider — you type an exact size, you don't drag to
            // one). In Star spread the canvas auto-fits the arms, so show the resolved size instead.
            if (starLayout)
                Label($"Canvas {spec.Width}×{spec.Height}  (auto-fit for star)", ZUI.ZTextStyle.Subtle);
            else
                using (ZUI.Flow())
                {
                    spec.canvasSize = ZUI.IntField("Width", spec.canvasSize, 70f, 4, 512);
                    spec.canvasHeight = ZUI.IntField("Height (0 = square)", spec.canvasHeight, 70f, 0, 512);
                    spec.pixelsPerUnit = Mathf.Max(1f, ZUI.FloatField("PPU", spec.pixelsPerUnit, 70f, 1f));
                }
            VerticalSpace(2f);
            // Seed/Frame count/Bake background — the blast's other top-level settings, packed the same way.
            // "Bake background" is the actual pixel colour baked into every EXPORTED frame (usually fully
            // transparent, so it composites correctly into a game scene) — different from "Preview backdrop"
            // further down, which is purely a cosmetic viewport aid and never gets baked into the output.
            using (ZUI.HRow())
            {
                spec.seed = ZUI.IntField("Seed", spec.seed, 90f);
                ZUI.HorizontalSpace();
                int fcNew = Mathf.RoundToInt(CompactSlider("Frame count", spec.frameCount, 1, 64, GroupFieldWidth(3)));
                if (fcNew != spec.frameCount) { spec.frameCount = Mathf.Max(1, fcNew); frame = Mathf.Min(frame, FrameCount - 1); }
                ZUI.HorizontalSpace();
                spec.background = EditorGUILayout.ColorField(new GUIContent("Bake background",
                    "The actual pixel colour baked into every EXPORTED frame — usually fully transparent, so it " +
                    "composites correctly into a game scene. Different from 'Preview backdrop' below, which is " +
                    "cosmetic-only (a viewport aid for authoring) and never gets baked into the output."),
                    spec.background, true, true, false, GUILayout.Width(CompactColorWidth));
                GUILayout.FlexibleSpace();
            }
            VerticalSpace(2f);

            // Origin / pivot (normalized): the point that lands on the spawn position. Editable here and by dragging
            // the ✛ handle in the preview. A game aligns this to the hit pixel.
            using (ZUI.HRow())
            {
                GUILayout.Label("Origin", EditorStyles.miniBoldLabel, GUILayout.Width(44f));
                spec.origin = ZUI.PositionPad(spec.origin, new Rect(0f, 0f, 1f, 1f), 64f);
                ZUI.HorizontalSpace();
                originMarkerAlpha = CompactSlider("Marker α", originMarkerAlpha, 0f, 1f, GroupFieldWidth(2));
                ZUI.HorizontalSpace();
                if (Button("Origin → centre")) spec.origin = new Vector2(0.5f, 0.5f);
                GUILayout.FlexibleSpace();
            }

            VerticalSpace();
            Label("Global modifiers", ZUI.ZTextStyle.SectionHeader);
            DrawModifiers(spec.globalModifiers, "gm.", isGlobal: true);

            VerticalSpace();
            DrawSimulationModifier("Simulation (genuinely iterative, always last)", "sim.0",
                () => spec.simulationModifier, m => spec.simulationModifier = m);

            VerticalSpace();
            DrawLayerList();

            VerticalSpace();
            DrawSelectedLayer();

            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(spec); frame = Mathf.Min(frame, FrameCount - 1); }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawLayerList()
        {
            Label("Layers (back → front)", ZUI.ZTextStyle.SectionHeader);

            EditorGUILayout.BeginHorizontal();
            addShape = (LayerShape)EditorGUILayout.EnumPopup(addShape, GUILayout.Width(110));
            if (Button("+ Add layer"))
            {
                Undo.RecordObject(spec, "Add layer");
                spec.layers.Add(Layer.Default(addShape));
                layerSel = spec.layers.Count - 1;
                EditorUtility.SetDirty(spec);
            }
            if (Button("Recall…"))
            {
                Rect act = GUILayoutUtility.GetLastRect();
                PopupWindow.Show(act, new PyreLayerLibraryPopup(PyreLayerLibrary.Load(), leftWidth - 24f,
                                                               InsertLibraryLayer));
            }
            EditorGUILayout.EndHorizontal();

            int dup = -1, remove = -1, saveToLib = -1;
            var rowRects = new System.Collections.Generic.List<Rect>(spec.layers.Count);
            for (int li = 0; li < spec.layers.Count; li++)
            {
                var layer = spec.layers[li];
                bool sel = li == layerSel;

                Rect row = EditorGUILayout.BeginHorizontal();
                rowRects.Add(row);
                if (Event.current.type == EventType.Repaint)
                {
                    if (li == draggingLayer) EditorGUI.DrawRect(row, new Color(0.35f, 0.55f, 0.95f, 0.30f));
                    else if (sel) EditorGUI.DrawRect(row, new Color(0.35f, 0.55f, 0.95f, 0.18f));
                }

                // drag handle — grab to reorder
                GUILayout.Label("≡", EditorStyles.boldLabel, GUILayout.Width(16));
                Rect grip = GUILayoutUtility.GetLastRect();
                EditorGUIUtility.AddCursorRect(grip, MouseCursor.Pan);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && grip.Contains(Event.current.mousePosition))
                {
                    draggingLayer = li; layerSel = li; Event.current.Use(); Repaint();
                }

                bool wasEnabled = layer.enabled;
                layer.enabled = Toggle(layer.enabled, layer.enabled ? "✓" : "", ZUI.Style.Default, GUILayout.Width(26));
                if (layer.enabled != wasEnabled) { EditorUtility.SetDirty(spec); Repaint(); }
                // select dot + inline-editable name (rename right here in the list)
                if (Button(sel ? "●" : "○", ZUI.Style.Default, GUILayout.Width(24))) layerSel = li;
                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField(layer.name, GUILayout.MinWidth(50));
                if (EditorGUI.EndChangeCheck()) { layer.name = newName; EditorUtility.SetDirty(spec); }
                Rect nameRect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.MouseDown && nameRect.Contains(Event.current.mousePosition)) layerSel = li;
                if (!layer.enabled) GUILayout.Label("off", EditorStyles.miniLabel, GUILayout.Width(20));
                if (Button("★", ZUI.Style.Default, GUILayout.Width(24))) saveToLib = li;
                if (Button("Dup", ZUI.Style.Default, GUILayout.Width(40))) dup = li;
                if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) remove = li;
                EditorGUILayout.EndHorizontal();
            }

            HandleLayerDrag(rowRects);

            if (saveToLib >= 0)
            {
                var layer = spec.layers[saveToLib];
                PyreLayerLibrary.Load().Add(layer, layer.name);
                ShowNotification(new GUIContent($"Saved “{layer.name}” to layer library"));
            }
            if (dup >= 0)
            {
                Undo.RecordObject(spec, "Duplicate layer");
                var copy = spec.layers[dup].Clone();
                copy.name += " copy";
                spec.layers.Insert(dup + 1, copy);
                layerSel = dup + 1;
                EditorUtility.SetDirty(spec);
            }
            if (remove >= 0)
            {
                Undo.RecordObject(spec, "Remove layer");
                spec.layers.RemoveAt(remove);
                layerSel = Mathf.Clamp(layerSel, 0, Mathf.Max(0, spec.layers.Count - 1));
                EditorUtility.SetDirty(spec);
            }
        }

        // Drop a recalled library layer (already a clone) in just after the current selection.
        void InsertLibraryLayer(Layer layer)
        {
            if (spec == null || layer == null) return;
            Undo.RecordObject(spec, "Recall layer");
            int at = Mathf.Clamp(layerSel + 1, 0, spec.layers.Count);
            spec.layers.Insert(at, layer);
            layerSel = at;
            EditorUtility.SetDirty(spec);
            Repaint();
        }

        // Drag a layer by its ≡ grip to reorder it; draws an insertion line and moves on release.
        void HandleLayerDrag(System.Collections.Generic.List<Rect> rowRects)
        {
            if (draggingLayer < 0 || rowRects.Count == 0) return;
            var e = Event.current;

            int target = rowRects.Count;
            for (int i = 0; i < rowRects.Count; i++)
                if (e.mousePosition.y < rowRects[i].center.y) { target = i; break; }

            if (e.type == EventType.Repaint)
            {
                float y = target < rowRects.Count ? rowRects[target].yMin : rowRects[rowRects.Count - 1].yMax;
                var r0 = rowRects[0];
                EditorGUI.DrawRect(new Rect(r0.x, y - 1f, r0.width, 2f), new Color(0.4f, 0.8f, 1f));
            }
            else if (e.type == EventType.MouseDrag) { Repaint(); e.Use(); }
            else if (e.type == EventType.MouseUp)
            {
                int from = draggingLayer;
                draggingLayer = -1;
                int to = target;
                if (from >= 0 && from < spec.layers.Count && to != from && to != from + 1)
                {
                    Undo.RecordObject(spec, "Reorder layer");
                    var lay = spec.layers[from];
                    spec.layers.RemoveAt(from);
                    if (to > from) to--;
                    to = Mathf.Clamp(to, 0, spec.layers.Count);
                    spec.layers.Insert(to, lay);
                    layerSel = to;
                    EditorUtility.SetDirty(spec);
                }
                e.Use();
                Repaint();
            }
        }

        // Isolated single-shape preview: exactly ONE of this layer's shapes, centred and at max size for the
        // box — ignores Count/Position/Spawn radius/Ring placement entirely, since those are about scatter,
        // not the shape's own look. `t` tracks the main transport's current frame, mapped into THIS layer's
        // own life window, so scrubbing/playing the normal timeline animates the isolated shape too — no
        // separate scrub control needed. Off by default (opt-in, per-asset via BlastSpec.previewShapeOn).
        void DrawShapePreview(Layer l)
        {
            spec.previewShapeOn = Toggle(spec.previewShapeOn, new GUIContent("Shape preview",
                "An isolated preview of exactly ONE of this layer's shapes, centred and shown at max size for " +
                "the box below — ignoring Count/Position/Spawn radius/Ring placement entirely. Use the toggles " +
                "to choose which of the shape's own aspects show up here, so a busy layer with lots of movement " +
                "and a high instance count can still be dialed in cleanly."));
            if (!spec.previewShapeOn) return;

            using (Box())
            {
                EditorGUILayout.BeginHorizontal();

                const float boxSize = 128f;
                Rect r = GUILayoutUtility.GetRect(boxSize, boxSize, GUILayout.Width(boxSize), GUILayout.Height(boxSize));
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
                    int cur = CurrentFrame();
                    float span = Mathf.Max(1, l.endFrame - l.startFrame);
                    float t = Mathf.Clamp01((cur - l.startFrame) / span);
                    if (shapePreviewTex != null) DestroyImmediate(shapePreviewTex);
                    shapePreviewTex = BlastRenderer.RenderShapePreviewTexture(l, spec, layerSel, t, cur,
                        (int)boxSize, (int)boxSize, spec.previewShapeGradientFill, spec.previewShapeCrescent,
                        spec.previewShapeHollow, spec.previewShapeSize, spec.previewShapeSpin, spec.previewShapeAlpha,
                        spec.previewShapeModifiers);
                    GUI.DrawTexture(r, shapePreviewTex, ScaleMode.StretchToFill, true);
                }

                GUILayout.Space(8f);

                EditorGUILayout.BeginVertical();
                using (ZUI.HRow())
                {
                    spec.previewShapeGradientFill = Toggle(spec.previewShapeGradientFill, "Gradient Fill");
                    spec.previewShapeCrescent = Toggle(spec.previewShapeCrescent, "Crescent");
                }
                using (ZUI.HRow())
                {
                    spec.previewShapeHollow = Toggle(spec.previewShapeHollow, "Hollow");
                    spec.previewShapeSize = Toggle(spec.previewShapeSize, "Size");
                }
                using (ZUI.HRow())
                {
                    spec.previewShapeSpin = Toggle(spec.previewShapeSpin, "Spin");
                    spec.previewShapeAlpha = Toggle(spec.previewShapeAlpha, "Alpha");
                }
                using (ZUI.HRow())
                {
                    spec.previewShapeModifiers = Toggle(spec.previewShapeModifiers, "Layer Modifiers");
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawSelectedLayer()
        {
            if (layerSel < 0 || layerSel >= spec.layers.Count) return;
            var l = spec.layers[layerSel];
            float cs = spec.canvasSize;
            float half = cs * 0.5f;

            Label($"Layer — {l.name}", ZUI.ZTextStyle.SectionHeader);
            if (!l.enabled)
                EditorGUILayout.HelpBox("This layer is disabled (its checkbox in the list above is unticked) — " +
                    "nothing you change down here will show up in the render until you re-enable it.", MessageType.Warning);

            DrawShapePreview(l);

            // (renamed inline in the layer list above)
            // Capture the shape used for THIS frame's layout; a change from the radio applies next frame so the
            // per-shape control block below doesn't change the drawn control set mid-frame.
            var shapeForLayout = l.shape;
            l.shape = (LayerShape)MiniRadio((int)l.shape, ShapeLabels);

            // Life window as a min-max range that can't exceed the current frame count.
            int fcMax = Mathf.Max(1, FrameCount - 1);
            float sf = Mathf.Clamp(l.startFrame, 0, fcMax);
            float ef = Mathf.Clamp(l.endFrame, 0, fcMax);
            MicroMinMax(ref sf, ref ef, 0f, fcMax, "Life (frames)", ZUI.SliderStyle.Default, true);
            l.startFrame = Mathf.RoundToInt(sf);
            l.endFrame = Mathf.Clamp(Mathf.RoundToInt(ef), l.startFrame, fcMax);

            // Colour + alpha apply to every mode.
            l.colorOverLife ??= Layer.DefaultColor(l.shape);
            l.alpha ??= Layer.DefaultAlpha();
            l.colorOverLife = EditorGUILayout.GradientField("Colour", l.colorOverLife);
            if (shapeForLayout == LayerShape.Disc || shapeForLayout == LayerShape.Crescent)
            {
                var colorModeForLayout = l.colorMode;
                l.colorMode = (ColorMode)MiniRadio((int)l.colorMode, ColorModeLabels);
                if (colorModeForLayout == ColorMode.NoiseFill)
                {
                    // Per-shape (si) normally — EXCEPT a Fused Disc, which melts every shape into one metaball
                    // field and samples the noise ONCE for that whole field instead (see fuseActive in Eval).
                    DrawNoiseFillParams(l, half, perShape: !fuseLayout);
                }
                // Gradient position/zoom + a movable core apply to both spatial fills (Fill + Flow fill). Offset the
                // core + a bright→dark gradient = a 3D orb / energy ball. All three are about the SAME thing
                // (how the gradient sits/moves), so they share one labelled group and one row.
                else if (colorModeForLayout != ColorMode.OverLife)
                {
                    Label("Gradient", ZUI.ZTextStyle.GroupTitle);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Position", l.colorFlow, -2f, 2f, 0f, width: GroupFieldWidth(3));
                        ZUI.HorizontalSpace();
                        CompactValRow("Zoom", l.colorFlowZoom, 0.1f, 4f, 1f, width: GroupFieldWidth(3));
                        ZUI.HorizontalSpace();
                        CompactValue2DRow("Offset", l.gradientOffsetX, l.gradientOffsetY,
                            ZUIValue2DControl.Options.Default.WithRange(-1f, 1f, -1f, 1f).WithDefault(Vector2.zero), GroupFieldWidth(3));
                        GUILayout.FlexibleSpace();
                    }
                }
                VerticalSpace(2f);
            }
            // Bars/MetaBlob both return before reaching the scatter section below (where Alpha pairs with Size
            // for every other shape) — draw it here instead, alone, so they don't lose it. Per-shape everywhere
            // EXCEPT MetaBlob, which has no scatter/count and samples Alpha once for the whole layer instead.
            if (shapeForLayout == LayerShape.Bars || shapeForLayout == LayerShape.MetaBlob)
                ValRow("Alpha", l.alpha, 0f, 1f, allowMinMax: shapeForLayout != LayerShape.MetaBlob);

            // Bars is a self-contained directional mode — none of the scatter / emission / deform controls apply,
            // so show ONLY the Bars box.
            if (shapeForLayout == LayerShape.Bars)
            {
                using (Box("Bars — forward-growing row"))
                {
                    // All of these (except Forward reach, per-bar) are sampled ONCE for the whole row/arm-set —
                    // Min-Max would just be a frozen dice roll, not variety across the bars.
                    using (ZUI.HRow())
                    {
                        CompactValRow("Bars per side", l.barCount, 0f, 40f, 7f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Width (px)", l.barWidth, 1f, 12f, 3f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())   // 1 = bars touch, 2 = one-bar gap, … · soft sides + tip (dissolve too)
                    {
                        CompactValRow("Spacing (×width)", l.barSpacing, 1f, 6f, 1.5f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Edge softness", l.barSoftness, 0f, 1f, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())   // How far a bar reaches, forward (Min-Max IS variety here) and back.
                    {
                        CompactValRow("Forward reach", l.barForward, 0f, cs, 40f, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Backward frac", l.barBackwardFrac, 0f, 1f, 0.18f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    // Taper is the arm-shape control: +1 = centre longest → triangle/flame, 0 = flat, -1 = concave.
                    // Stagger is TIMING, not shape: it delays outer bars so the row unfurls centre-out.
                    using (ZUI.HRow())
                    {
                        CompactValRow("Taper (centre↔edge)", l.barTaper, -1f, 1f, 0.85f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Stagger (timing)", l.barStagger, 0f, 0.5f, 0.05f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Layer angle", l.barAngleDeg, -180f, 180f, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        l.barMirror = Toggle(l.barMirror, "Mirror angle");
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Origin inset", l.originInset, 0f, 40f, 4f, allowMinMax: false);
                    var decayForLayout = l.barDecay;
                    l.barDecay = (BarDecay)MiniRadio((int)l.barDecay, BarDecayLabels);
                    if (decayForLayout == BarDecay.Dissolve)
                        l.dissolveStart = Slider(l.dissolveStart, 0f, 1f, "Dissolve start");
                    VerticalSpace(2f);

                    using (ZUI.HRow())
                    {
                        CompactValRow("Base angle", l.baseAngleDeg, -180f, 180f, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        l.star = Toggle(l.star, "Star (arms radiate from centre)");
                        GUILayout.FlexibleSpace();
                    }
                    if (barStarLayout)   // Layout-captured gate so the control count is reflow-safe
                    {
                        VerticalSpace(2f);
                        using (ZUI.HRow())
                        {
                            l.spreadCount = Mathf.Max(1, Mathf.RoundToInt(CompactSlider("Arms", l.spreadCount, 1, 24, GroupFieldWidth(2))));
                            ZUI.HorizontalSpace();
                            CompactValRow("Spread degrees", l.spreadDegrees, 0f, 360f, 360f, allowMinMax: false, width: GroupFieldWidth(2));
                            GUILayout.FlexibleSpace();
                        }
                        Label("Arms share the centre and radiate outward; canvas auto-fits.", ZUI.ZTextStyle.Small);
                    }
                }
                VerticalSpace(2f);
                Label("Modifiers", ZUI.ZTextStyle.SectionHeader);
                DrawModifiers(l.modifiers, "lm." + layerSel + ".");
                VerticalSpace();
                DrawSimulationModifier("Simulation (genuinely iterative, always last IN THIS LAYER)", "lsim." + layerSel,
                    () => l.simulationModifier, m => l.simulationModifier = m);
                return;
            }

            // MetaBlob is authored by clicking the preview to drop fusing orbs — no scatter/count controls.
            if (shapeForLayout == LayerShape.MetaBlob)
            {
                using (Box("MetaBlob — orbs fuse into one gradient-shaded shape"))
                {
                    EditorGUILayout.BeginHorizontal();
                    placeMetaMode = Toggle(placeMetaMode, placeMetaMode ? "● Placing — click the preview" : "○ Place orbs (click preview)");
                    showMetaMarkers = Toggle(showMetaMarkers, "Markers", ZUI.Style.Default, GUILayout.Width(72));
                    EditorGUILayout.EndHorizontal();
                    using (ZUI.HRow())
                    {
                        l.metaThreshold = CompactSlider("Threshold", l.metaThreshold, 0.1f, 2f, GroupFieldWidth(3));
                        ZUI.HorizontalSpace();
                        l.metaShadeRange = CompactSlider("Shade range", l.metaShadeRange, 0.1f, 3f, GroupFieldWidth(3));
                        ZUI.HorizontalSpace();
                        l.metaSoftness = CompactSlider("Edge softness", l.metaSoftness, 0.01f, 1f, GroupFieldWidth(3));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    EditorGUILayout.BeginHorizontal();
                    l.metaSpawnInterval = EditorGUILayout.Slider("Spawn interval", l.metaSpawnInterval, 0f, 0.5f);
                    // Only takes effect on NEWLY placed orbs on its own — this button is the "apply it to what's
                    // already here too" action, so the slider doesn't read as inert once orbs already exist.
                    using (new EditorGUI.DisabledScope(l.metaOrbs.Count == 0))
                        if (Button("Renumber births", ZUI.Style.Default, GUILayout.Width(110)))
                        {
                            for (int oi = 0; oi < l.metaOrbs.Count; oi++)
                            {
                                var oo = l.metaOrbs[oi];
                                if (oo == null) continue;
                                oo.birth = Mathf.Clamp01(oi * l.metaSpawnInterval);
                                oo.life = Mathf.Clamp(1f - oo.birth, 0.25f, 1f);
                            }
                            EditorUtility.SetDirty(spec);
                        }
                    EditorGUILayout.EndHorizontal();

                    // Layer-wide motion — one animatable value shared by every orb, so the whole blob comes alive
                    // (sampled once for the whole layer, so Min-Max would just be a frozen dice roll here).
                    // ×radius of every orb over life, and contract/expand centres about the origin.
                    using (ZUI.HRow())
                    {
                        CompactValRow("Radius pulse", l.metaRadiusScale, 0f, 3f, 1f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Expand", l.metaExpand, 0f, 3f, 1f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);

                    // Shading: the SAME Over life / Fill / Flow fill options Disc/Crescent get (see ColorMode).
                    var metaColorModeForLayout = l.colorMode;
                    l.colorMode = (ColorMode)MiniRadio((int)l.colorMode, ColorModeLabels);
                    if (metaColorModeForLayout == ColorMode.NoiseFill)
                    {
                        DrawNoiseFillParams(l, half, perShape: false);   // one shared field for the whole blob
                    }
                    else if (metaColorModeForLayout != ColorMode.OverLife)
                    {
                        Label("Gradient", ZUI.ZTextStyle.GroupTitle);
                        using (ZUI.HRow())
                        {
                            CompactValRow("Position", l.colorFlow, -2f, 2f, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                            ZUI.HorizontalSpace();
                            CompactValRow("Zoom", l.colorFlowZoom, 0.1f, 4f, 1f, allowMinMax: false, width: GroupFieldWidth(2));
                            GUILayout.FlexibleSpace();
                        }
                        VerticalSpace(2f);
                    }

                    int rm = -1;
                    float lw = EditorGUIUtility.labelWidth;
                    for (int i = 0; i < l.metaOrbs.Count; i++)
                    {
                        var o = l.metaOrbs[i];
                        using (Box())
                        {
                            // Row 1: select · number · position (X/Y) · radius · delete — packed onto one line so the
                            // orb list stays vertically compact and spreads across the pane's width.
                            EditorGUILayout.BeginHorizontal();
                            if (Button(metaSel == i ? "●" : "○", ZUI.Style.Default, GUILayout.Width(24))) metaSel = i;
                            GUILayout.Label($"#{i + 1}", EditorStyles.miniBoldLabel, GUILayout.Width(24));
                            EditorGUIUtility.labelWidth = 1f;   // hide the (empty) Vector2Field label column
                            o.pos = EditorGUILayout.Vector2Field(GUIContent.none, o.pos, GUILayout.MinWidth(96));
                            EditorGUIUtility.labelWidth = 30f;
                            o.radius = EditorGUILayout.Slider("Rad", o.radius, 2f, half);
                            EditorGUIUtility.labelWidth = lw;
                            if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) rm = i;
                            EditorGUILayout.EndHorizontal();

                            // Row 2: birth + life split the width.
                            EditorGUILayout.BeginHorizontal();
                            EditorGUIUtility.labelWidth = 34f;
                            o.birth = EditorGUILayout.Slider("Birth", o.birth, 0f, 1f);
                            o.life = EditorGUILayout.Slider("Life", o.life, 0.02f, 1f);
                            EditorGUIUtility.labelWidth = lw;
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    if (rm >= 0) { l.metaOrbs.RemoveAt(rm); metaSel = -1; }

                    EditorGUILayout.BeginHorizontal();
                    if (Button("Clear orbs")) { l.metaOrbs.Clear(); metaSel = -1; }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{l.metaOrbs.Count} orb(s)", EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }
                VerticalSpace(2f);
                Label("Modifiers", ZUI.ZTextStyle.SectionHeader);
                DrawModifiers(l.modifiers, "lm." + layerSel + ".");
                VerticalSpace();
                DrawSimulationModifier("Simulation (genuinely iterative, always last IN THIS LAYER)", "lsim." + layerSel,
                    () => l.simulationModifier, m => l.simulationModifier = m);
                return;
            }

            // ── shape (scatter) controls ─────────────────────────────────────────
            if (!rosingLayout)   // Rosing's rings each carry their own count/radius — the flat ones don't apply
            {
                // Count is sampled once for the whole layer (it decides HOW MANY shapes exist, not a per-shape
                // value), so Min-Max there is just a frozen dice roll; Spawn radius is genuinely per-shape.
                using (ZUI.HRow())
                {
                    CompactValRow("Count", l.count, 1f, 40f, allowMinMax: false, width: GroupFieldWidth(2));
                    ZUI.HorizontalSpace();
                    CompactValRow("Spawn radius", l.spawnRadius, 0f, 1f, width: GroupFieldWidth(2));
                    GUILayout.FlexibleSpace();
                }
            }
            l.scatterMode = (ScatterMode)MiniRadio((int)l.scatterMode, ScatterModeLabels);
            if (ringLayout || rosingLayout)   // Layout-captured gate so the control count is reflow-safe
            {
                using (Box(rosingLayout ? "Rosing — rings bloom outward over life" : "Ring — placed along the rim"))
                {
                    l.ringOrder = (RingOrder)MiniRadio((int)l.ringOrder, RingOrderLabels);
                    // Spawn-locked (evaluated once per LAYER, not per shape — see BlastRenderer's spawnLp
                    // comment), so Min-Max here is one frozen roll for the whole ring, not per-shape variety.
                    using (ZUI.HRow())
                    {
                        CompactValRow("Start angle", l.ringStartAngle, -180f, 180f, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Arc degrees", l.ringArcDegrees, 0f, 360f, 360f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    // Live EVERY frame (not spawn-locked like the two above) — animate it and the whole ring
                    // grows/shrinks with every shape already on it riding along, independent of Size entirely.
                    // Align rotation is just a checkbox, so it shares Ring expand's row (same pattern as Outer
                    // softness/Hollow below) instead of eating a whole row of its own.
                    using (ZUI.HRow())
                    {
                        CompactValRow("Ring expand", l.ringExpand, 0f, 3f, 1f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        l.ringAlignRotation = Toggle(l.ringAlignRotation, new GUIContent("Align rotation",
                            "Rotates each shape to face its own angle around the ring — matters for asymmetric " +
                            "shapes (Crescent, an offset hole); a plain Disc looks the same either way."));
                        GUILayout.FlexibleSpace();
                    }
                    if (rosingLayout)
                    {
                        l.roseReverseDraw = Toggle(l.roseReverseDraw, new GUIContent("Reverse ring order",
                            "Off = later rings in the list draw ON TOP of earlier ones (with the default stack " +
                            "that's the outer, later-blooming ring in front). On = reversed, so earlier rings " +
                            "draw in front instead. Only changes which RING composites over which — not the " +
                            "individual discs' own draw order within a ring."));
                        Label("Each ring below places its own Count shapes evenly around the arc above, at its " +
                              "own Radius, appearing together at Birth and living for Life.", ZUI.ZTextStyle.Small);
                        DrawRoseRings(l);
                    }
                    else
                    {
                        Label("360° = the full rim; less confines shapes to a wedge starting at Start angle. Start " +
                              "angle/Arc degrees lock in per-shape at spawn — for a whole ring that visibly spins " +
                              "live, add a Rotate geometry modifier instead.", ZUI.ZTextStyle.Small);
                    }
                }
            }
            // PROTOTYPE: 2D MultiCont control, tried here first (Position X/Y specifically, since dragging two
            // separate sliders to aim a position is exactly the ergonomics problem it's meant to fix) before any
            // wider rollout to the rest of Pyre's position-like field pairs. Spin shares its row — both are
            // "where/how this shape sits", and the pad's own compact footprint leaves room to spare.
            using (ZUI.HRow())
            {
                CompactValue2DRow("Position", l.positionX, l.positionY,
                    ZUIValue2DControl.Options.Default.WithRange(-half, half, -half, half).WithDefault(Vector2.zero), GroupFieldWidth(2));
                ZUI.HorizontalSpace();
                CompactValRow("Spin", l.spinDegrees, -360f, 360f, 0f, width: GroupFieldWidth(2));
                GUILayout.FlexibleSpace();
            }
            VerticalSpace(2f);
            // Size shares Alpha's row — Bars/MetaBlob (which have no Size here) already drew Alpha on its own
            // above, before this scatter section, so they lose nothing.
            using (ZUI.HRow())
            {
                CompactValRow("Size", l.size, 0f, half, width: GroupFieldWidth(2));
                ZUI.HorizontalSpace();
                CompactValRow("Alpha", l.alpha, 0f, 1f, width: GroupFieldWidth(2));
                GUILayout.FlexibleSpace();
            }
            VerticalSpace(2f);
            // Life jitter applies regardless of scatter mode; Spawn stagger only for non-Rosing (Rosing's
            // timing comes from each ring's own Birth/Life instead) — sharing a row when both are present,
            // Life jitter keeping the row to itself otherwise.
            using (ZUI.HRow())
            {
                int lifeRowFields = rosingLayout ? 1 : 2;
                l.perShapeLifeJitter = CompactSlider("Life jitter", l.perShapeLifeJitter, 0f, 1f, GroupFieldWidth(lifeRowFields));
                if (!rosingLayout)
                {
                    ZUI.HorizontalSpace();
                    l.spawnStagger = CompactSlider("Spawn stagger", l.spawnStagger, 0f, 1f, GroupFieldWidth(lifeRowFields));
                }
                GUILayout.FlexibleSpace();
            }
            if (!rosingLayout)
            {
                l.syncDeath = Toggle(l.syncDeath, new GUIContent("Sync death",
                    "Every shape reaches the end of its life at the SAME frame (this layer's own End frame) " +
                    "regardless of when it spawned — shapes born earlier mature more slowly so the whole burst " +
                    "finishes together, instead of each shape getting the same fixed duration."));
            }

            switch (shapeForLayout)
            {
                case LayerShape.Disc:
                    DrawDiscEdges(l);
                    if (ringLayout || rosingLayout)
                    {
                        l.fuse = Toggle(l.fuse, new GUIContent("Fuse",
                            "Melts every shape in this layer into ONE gradient-shaded metaball field (like " +
                            "MetaBlob, but fed by this layer's own Ring/Rosing-placed discs) instead of " +
                            "compositing them independently — nearby/overlapping discs melt together."));
                        if (fuseLayout)   // Layout-captured gate so the control count is reflow-safe
                        {
                            using (Box("Fuse — metaball field"))
                            using (ZUI.HRow())
                            {
                                l.metaThreshold = CompactSlider("Threshold", l.metaThreshold, 0.1f, 2f, GroupFieldWidth(3));
                                ZUI.HorizontalSpace();
                                l.metaShadeRange = CompactSlider("Shade range", l.metaShadeRange, 0.1f, 3f, GroupFieldWidth(3));
                                ZUI.HorizontalSpace();
                                l.metaSoftness = CompactSlider("Edge softness", l.metaSoftness, 0.01f, 1f, GroupFieldWidth(3));
                                GUILayout.FlexibleSpace();
                            }
                        }
                    }
                    break;
                case LayerShape.SparkleField:
                    ValRow("Sparkle density", l.sparkleDensity, 0f, 1f, 0.25f);
                    l.sparkleBlobs = Toggle(l.sparkleBlobs, new GUIContent("Blobs",
                        "Off = the original single-pixel-per-frame twinkle (Sparkle seed below). On = each " +
                        "sparkle becomes a small blob with its own grow/hold/fade lifetime and a soft, shrinking/" +
                        "growing radius, instead of a single flickering pixel."));
                    if (sparkleBlobsLayout)   // Layout-captured gate so the control count is reflow-safe
                    {
                        using (Box("Sparkle blobs"))
                        using (ZUI.HRow())
                        {
                            CompactValRow("Blob radius (px)", l.sparkleBlobRadius, 0.5f, Mathf.Max(4f, half * 0.3f), 2f, width: GroupFieldWidth(3));
                            ZUI.HorizontalSpace();
                            CompactValRow("Blob life (frames)", l.sparkleBlobLife, 1f, 30f, 8f, width: GroupFieldWidth(3));
                            ZUI.HorizontalSpace();
                            CompactValRow("Blob softness", l.sparkleBlobSoftness, 0f, 1f, 0.6f, width: GroupFieldWidth(3));
                            GUILayout.FlexibleSpace();
                        }
                    }
                    else
                    {
                        ValRow("Sparkle seed", l.sparkleSeed, 0f, 1f);   // Min-Max default → twinkles per frame
                    }
                    DrawDiscEdges(l);                                 // a sparkle field is a disc: soft edges + hole
                    break;
                case LayerShape.Crescent:
                    // Range goes to ±2 (not ±1): the mask disc shares the main disc's own radius, so overlap —
                    // and so the bite — only fully vanishes (a plain disc, no crescent) once the offset magnitude
                    // reaches 2 (both discs the same size, separated by their combined radii). ±1 alone only
                    // reaches the half-moon bisection, never a clean disc.
                    ZUIValue2DControl.Draw("Crescent offset", l.crescentOffsetX, l.crescentOffsetY,
                        ZUIValue2DControl.Options.Default.WithRange(-2f, 2f, -2f, 2f).WithDefault(new Vector2(0.45f, 0f)));
                    VerticalSpace(2f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Outer softness", l.outerSoftness, 0f, 1f, 0f, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Bite softness", l.innerSoftness, 0f, 1f, 0f, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    break;
                case LayerShape.Sprite:
                    EditorGUI.BeginChangeCheck();
                    l.particleSprite = (Sprite)EditorGUILayout.ObjectField("Sprite", l.particleSprite, typeof(Sprite), false);
                    if (EditorGUI.EndChangeCheck()) BlastRenderer.ClearSpriteCache();
                    ValRow("Spin", l.spriteSpin, -360f, 360f, 0f);
                    EditorGUILayout.BeginHorizontal();
                    if (Button("New sprite (Aseprite)")) CreateParticleSprite(l);
                    if (Button("Edit in Aseprite") && l.particleSprite != null) OpenInAseprite(l.particleSprite);
                    EditorGUILayout.EndHorizontal();
                    break;
            }

            VerticalSpace(2f);
            Label("Modifiers", ZUI.ZTextStyle.SectionHeader);
            DrawModifiers(l.modifiers, "lm." + layerSel + ".");
            VerticalSpace();
            DrawSimulationModifier("Simulation (genuinely iterative, always last IN THIS LAYER)", "lsim." + layerSel,
                () => l.simulationModifier, m => l.simulationModifier = m);
        }

        // One animatable-value row (ZUIValueControl). def (when given) is the double-click reset target.
        // Curve mode here spans the LAYER's frame window and takes its Y range from [lo, hi], so the curve's
        // Duration/Warmup/Loop and Value-Range fields are hidden (not meaningful for a frame-baked blast).
        // allowMinMax: Min-Max is a per-shape-stable RANDOMIZER (BlastRenderer.Eval hashes it per shape index,
        // si) — genuinely useful when the field is sampled once per SHAPE, so several instances in one baked
        // animation each get their own fixed-but-different value. Pass false at call sites where the field is
        // only ever evaluated ONCE for the whole layer (h2 = 0, not si) — there Min-Max would just pick one
        // random-but-still-frozen number, offering nothing Static doesn't already give more directly.
        void ValRow(string label, ZUIValue v, float lo, float hi, float? def = null, bool allowMinMax = true)
        {
            var o = ZUIValueControl.Options.Default.WithRange(lo, hi).WithoutCurveExtras().WithoutLiveReadout();
            if (def.HasValue) o = o.WithDefault(def.Value);
            o.allowMinMax = allowMinMax;
            ZUIValueControl.Draw(label, v, o);
        }

        // The disc-like edge controls (Disc + SparkleField): always an outer-edge alpha gradient, and an optional
        // Hollow hole with its own size + inner-edge alpha gradient.
        void DrawDiscEdges(Layer l)
        {
            using (ZUI.HRow())
            {
                CompactValRow("Outer softness", l.outerSoftness, 0f, 1f, 0f, width: GroupFieldWidth(2));
                ZUI.HorizontalSpace();
                l.hollow = Toggle(l.hollow, "Hollow");
                GUILayout.FlexibleSpace();
            }
            if (discHollowLayout)   // Layout-captured gate so the control count is reflow-safe
            {
                VerticalSpace(2f);
                using (ZUI.HRow())
                {
                    CompactValRow("Hole size", l.holeSize, 0f, 1f, 0.5f, width: GroupFieldWidth(2));
                    ZUI.HorizontalSpace();
                    CompactValRow("Inner softness", l.innerSoftness, 0f, 1f, 0f, width: GroupFieldWidth(2));
                    GUILayout.FlexibleSpace();
                }
                VerticalSpace(2f);
                // offset the hole = a crescent
                ZUIValue2DControl.Draw("Hole offset", l.holeOffsetX, l.holeOffsetY,
                    ZUIValue2DControl.Options.Default.WithRange(-1f, 1f, -1f, 1f).WithDefault(Vector2.zero));
            }
        }

        // Rosing's authored ring list — mirrors MetaBlob's orb list (same fields: a count/radius plus a
        // birth/life pair), just editing RoseRing instead of MetaOrb. Uses ZUIForm (per the ZUI reference's
        // "pack related short fields into one row" guidance) instead of raw EditorGUILayout + manual
        // labelWidth juggling — each related pair/trio shares a row, and the form measures its own label
        // column instead of hard-coding one that only happens to fit today's labels.
        void DrawRoseRings(Layer l)
        {
            l.roseRings ??= new System.Collections.Generic.List<RoseRing>();
            int rm = -1;
            for (int i = 0; i < l.roseRings.Count; i++)
            {
                var r = l.roseRings[i];
                if (r == null) { rm = i; continue; }
                // One single row, no title bar (a titled Box adds its own header line above the content —
                // exactly the "2 rows" this collapses away): ring number + all 5 fields + delete, packed onto
                // one line, no shared row label needed since each stacked field carries its own. Count = how
                // many shapes; Radius = PLACEMENT distance from the origin (0-1); Size = a ×scale on the
                // layer's own Size for just this ring's discs — independent of Radius, so rings can graduate
                // in disc size without also moving; Birth/Life = timing.
                using (Box())
                using (ZUI.HRow())
                {
                    // Switched from ZUI.StackedInt/StackedFloat (label-drag-scrub, no visible track — you had
                    // to already know the label itself was draggable) to ZUI.SliderStacked (same label-on-top
                    // look, but an actual draggable slider track underneath) — a real slider is more approachable
                    // than a hidden drag gesture, and each field now gets a proper share of the row's own width
                    // instead of a cramped ~40-44px regardless of how much space the row actually has.
                    float fw = GroupFieldWidth(5);
                    GUILayout.Label($"R{i + 1}", EditorStyles.miniBoldLabel, GUILayout.Width(18f));
                    r.count = Mathf.Max(1, Mathf.RoundToInt(ZUI.SliderStacked(r.count, 1, 60, "Count", widthOverride: fw, isInt: true)));
                    ZUI.HorizontalSpace("H Control Gap");
                    r.radius = ZUI.SliderStacked(r.radius, 0f, 1f, "Radius", widthOverride: fw);
                    ZUI.HorizontalSpace("H Control Gap");
                    r.sizeScale = ZUI.SliderStacked(r.sizeScale, 0.1f, 3f, "Size", widthOverride: fw);
                    ZUI.HorizontalSpace("H Control Gap");
                    r.birth = ZUI.SliderStacked(r.birth, 0f, 1f, "Birth", widthOverride: fw);
                    ZUI.HorizontalSpace("H Control Gap");
                    r.life = ZUI.SliderStacked(r.life, 0.02f, 1f, "Life", widthOverride: fw);
                    GUILayout.FlexibleSpace();
                    if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) rm = i;
                }
                if (i < l.roseRings.Count - 1) VerticalSpace(2f);   // a gap between rings, not after the last one
            }
            if (rm >= 0) { Undo.RecordObject(spec, "Remove rose ring"); l.roseRings.RemoveAt(rm); EditorUtility.SetDirty(spec); }

            EditorGUILayout.BeginHorizontal();
            if (Button("+ Add ring"))
            {
                Undo.RecordObject(spec, "Add rose ring");
                int n = l.roseRings.Count;
                l.roseRings.Add(new RoseRing { count = 4 + n * 6, radius = Mathf.Min(0.9f, 0.15f + n * 0.2f), birth = Mathf.Min(0.6f, n * 0.15f), life = 1f });
                EditorUtility.SetDirty(spec);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{l.roseRings.Count} ring(s)", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        // Zoom/Rotation/Drift/Warp/Bands — the Noise fill colour mode's own params (Disc/Crescent/MetaBlob),
        // all sampling the same PyreNoise field.
        // perShape: true only when the caller evaluates these per-shape (si) — MetaBlob/a fused Disc sample
        // them ONCE for the whole layer instead, where Min-Max is just a frozen dice roll, not variety.
        void DrawNoiseFillParams(Layer l, float half, bool perShape)
        {
            Label("Gradient", ZUI.ZTextStyle.GroupTitle);
            using (ZUI.HRow())
            {
                CompactValRow("Position", l.noiseGradientPosition, 0f, 1f, 0f, allowMinMax: perShape, width: GroupFieldWidth(2));
                ZUI.HorizontalSpace();
                CompactValRow("Zoom", l.noiseGradientZoom, 0.1f, 8f, 1f, allowMinMax: perShape, width: GroupFieldWidth(2));
                GUILayout.FlexibleSpace();
            }
            VerticalSpace(2f);

            Label("Noise", ZUI.ZTextStyle.GroupTitle);
            using (ZUI.HRow())
            {
                CompactValRow("Zoom", l.noiseZoom, 1f, Mathf.Max(8f, half), 20f, allowMinMax: perShape, width: GroupFieldWidth(2));
                ZUI.HorizontalSpace();
                CompactValRow("Rotation", l.noiseRotation, -720f, 720f, 0f, allowMinMax: perShape, width: GroupFieldWidth(2));
                GUILayout.FlexibleSpace();
            }
            VerticalSpace(2f);
            using (ZUI.HRow())
            {
                CompactValue2DRow("Drift", l.noiseDriftX, l.noiseDriftY,
                    ZUIValue2DControl.Options.Default.WithRange(-half, half, -half, half).WithDefault(Vector2.zero), GroupFieldWidth(2));
                ZUI.HorizontalSpace();
                CompactValRow("Warp", l.noiseWarp, 0f, 2f, 0.6f, allowMinMax: perShape, width: GroupFieldWidth(2));
                GUILayout.FlexibleSpace();
            }
            VerticalSpace(2f);
            l.noiseBands = Mathf.Max(1, Mathf.RoundToInt(Slider(l.noiseBands, 1, 8, "Shading bands")));
            l.noiseBandSoftness = Slider(l.noiseBandSoftness, 0f, 1f, "Band softness");
        }

        static readonly string[] DissolveModeLabels = { "Erase", "Scatter" };
        static readonly string[] MaskShapeLabels = { "Disc out", "Disc in", "Swipe H", "Swipe V", "Wedge", "Noise" };
        static readonly string[] ColorModeLabels = { "Over life", "Fill", "Flow fill", "Noise fill" };
        // Outline + VoronoiCrack both reuse ColorMode (not a bespoke enum each) for the same Over-life-vs-spatial
        // split every shape fill already has, but only Over life/Fill make sense for their non-radial gradient
        // reads (thickness / seam proximity) — Flow/Noise fill need fields (scroll position, a noise domain)
        // neither has, so they're left off this shared 2-option picker.
        static readonly string[] OverLifeFillLabels = { "Over life", "Fill" };
        static readonly string[] ScaleAxisLabels = { "Vertical", "Horizontal", "Both" };
        static readonly string[] CrackSpreadModeLabels = { "Uniform", "Centre out", "Edge in", "Both" };

        // Single in-memory clipboard (last-copied wins) — no asset/browser, just a quick way to carry one
        // modifier's settings to another slot in the same list, a different layer, or the global list. Static so
        // it survives closing/reopening the window within the session, like a real clipboard.
        static PyreModifier modifierClipboard;

        // Modifiers whose ENTIRE body is one animatable value — Skew/Contrast/Brightness/Saturation/Sphere/
        // Ordered dither. These don't need a separate labelled row below the header: the header already shows
        // the modifier's name, so a second row repeating it (Contrast / Contrast [=====slider=====]) was pure
        // redundancy, and giving that lone slider the WHOLE row's width for one number served nothing. Drawn
        // inline in the header row instead (see DrawModifiers), unlabeled and compactly sized.
        static bool TrySingleValueBody(PyreModifier m, out ZUIValue v, out float lo, out float hi, out float def)
        {
            v = null; lo = 0f; hi = 1f; def = 0f;
            switch (m)
            {
                case SkewModifier s: v = s.amount; lo = -2f; hi = 2f; def = 0f; return true;
                case ContrastModifier cm: v = cm.amount; lo = 0f; hi = 2f; def = 1f; return true;
                case BrightnessModifier bm: v = bm.amount; lo = 0f; hi = 2f; def = 1f; return true;
                case SaturationModifier sm: v = sm.amount; lo = 0f; hi = 2f; def = 1f; return true;
                case OrderedDitherModifier od: v = od.strength; lo = 0f; hi = 1f; def = 1f; return true;
                default: return false;
            }
        }

        // ── modifier stack UI (Pyre v2): the opt-in geometry/pixel effects on a layer or globally ──────────
        // Shared by the Blast panel (global modifiers) and the Layer panel (per-layer). Removal is deferred to
        // after the loop so the control set is stable within a frame; adds come from a GenericMenu (also deferred).
        void DrawModifiers(System.Collections.Generic.List<PyreModifier> list, string idp, bool isGlobal = false)
        {
            if (list == null) return;
            float half = spec.canvasSize * 0.5f;
            int remove = -1;
            var rowRects = new System.Collections.Generic.List<Rect>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null) { remove = i; continue; }
                bool single = TrySingleValueBody(m, out var singleVal, out float singleLo, out float singleHi, out float singleDef);
                using (Box(m.DisplayName))
                {
                    EditorGUILayout.BeginHorizontal();
                    // drag grip — grab to reorder within this list
                    GUILayout.Label("≡", EditorStyles.boldLabel, GUILayout.Width(16));
                    Rect grip = GUILayoutUtility.GetLastRect();
                    EditorGUIUtility.AddCursorRect(grip, MouseCursor.Pan);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && grip.Contains(Event.current.mousePosition))
                    {
                        draggingMod = i; draggingModList = idp; Event.current.Use(); Repaint();
                    }
                    m.enabled = Toggle(m.enabled, m.enabled ? "✓" : "", ZUI.Style.Default, GUILayout.Width(28));
                    GUILayout.Label(m.DisplayName, EditorStyles.miniBoldLabel);
                    if (single && m.enabled)
                        CompactValRow("", singleVal, singleLo, singleHi, singleDef, width: GroupFieldWidth(2));
                    GUILayout.FlexibleSpace();
                    if (Button("Copy", ZUI.Style.Default, GUILayout.Width(40))) modifierClipboard = m.Clone();
                    if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) remove = i;
                    EditorGUILayout.EndHorizontal();
                    if (m.enabled && !single) DrawModBody(m, half, idp + i);
                }
                rowRects.Add(GUILayoutUtility.GetLastRect());   // the whole modifier block (Box rect) = drop target
            }

            HandleModDrag(list, idp, rowRects);

            EditorGUILayout.BeginHorizontal();
            // ExpandWidth(false): the button style otherwise stretches to fill the row (no cap, no sibling
            // FlexibleSpace to share with) — with nothing bounding it, it grew to match whatever width other
            // rows in the same scroll view had already established, pushing Paste off past the visible pane
            // (reported: "Paste" cut off even though "+ Add modifier" had absurd amounts of empty space).
            if (Button("+ Add modifier", ZUI.Style.Default, GUILayout.ExpandWidth(false))) ShowAddModifierMenu(list, isGlobal);
            using (new EditorGUI.DisabledScope(modifierClipboard == null))
                if (Button($"Paste{(modifierClipboard != null ? " " + modifierClipboard.DisplayName : "")}",
                    ZUI.Style.Default, GUILayout.ExpandWidth(false)))
                {
                    Undo.RecordObject(spec, "Paste modifier");
                    list.Add(modifierClipboard.Clone());   // clone so pasting twice never shares one instance
                    EditorUtility.SetDirty(spec);
                }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            if (remove >= 0)
            {
                if (list[remove] == paintSmudge) { paintSmudge = null; draggingSmudge = false; }
                if (list[remove] == editPin) { editPin = null; pinSel = -1; draggingPin = false; }
                if (ReferenceEquals(list[remove], editCurl)) { editCurl = null; vortexSel = -1; draggingVortex = false; }
                Undo.RecordObject(spec, "Remove modifier"); list.RemoveAt(remove); EditorUtility.SetDirty(spec);
            }
        }

        // The one, always-last SIMULATION modifier slot (BlastSpec.simulationModifier, or a Layer's own) — a
        // single nullable field, not a list, so deliberately simpler than DrawModifiers above: no drag grip
        // (nothing to reorder), no Copy/Paste (only one instance can ever exist by construction). Just
        // enabled/body/remove when occupied, or a single "+ Add simulation" button when empty. Shared by the
        // Blast panel (BlastSpec.simulationModifier) and every per-layer panel (Layer.simulationModifier) —
        // `get`/`set` read/write whichever slot the caller means; `idp` scopes DrawModBody's own per-field UI
        // state the same way DrawModifiers' `idp` does, so the blast-wide and per-layer bodies never cross-talk.
        void DrawSimulationModifier(string label, string idp, System.Func<PyreModifier> get, System.Action<PyreModifier> set)
        {
            Label(label, ZUI.ZTextStyle.SectionHeader);
            var current = get();
            if (current == null)
            {
                if (Button("+ Add simulation", ZUI.Style.Default, GUILayout.ExpandWidth(false)))
                {
                    Undo.RecordObject(spec, "Add simulation modifier");
                    set(new PixelFluidModifier());
                    EditorUtility.SetDirty(spec);
                }
                return;
            }
            bool remove = false;
            using (Box(current.DisplayName))
            {
                EditorGUILayout.BeginHorizontal();
                current.enabled = Toggle(current.enabled, current.enabled ? "✓" : "", ZUI.Style.Default, GUILayout.Width(28));
                GUILayout.Label(current.DisplayName, EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) remove = true;
                EditorGUILayout.EndHorizontal();
                if (current.enabled) DrawModBody(current, spec.canvasSize * 0.5f, idp);
            }
            if (remove)
            {
                Undo.RecordObject(spec, "Remove simulation modifier");
                set(null);
                EditorUtility.SetDirty(spec);
            }
        }

        // Drag a modifier by its ≡ grip to reorder it within its own list; draws an insertion line and moves on
        // release. `idp` scopes the drag so the per-layer and global modifier lists don't cross-talk. Mirrors
        // HandleLayerDrag; rowRects[i] is the block rect of list[i] (assumes no null entries, pruned each frame).
        void HandleModDrag(System.Collections.Generic.List<PyreModifier> list, string idp,
                           System.Collections.Generic.List<Rect> rowRects)
        {
            if (draggingMod < 0 || draggingModList != idp || rowRects.Count == 0) return;
            var e = Event.current;

            int target = rowRects.Count;
            for (int i = 0; i < rowRects.Count; i++)
                if (e.mousePosition.y < rowRects[i].center.y) { target = i; break; }

            if (e.type == EventType.Repaint)
            {
                if (draggingMod < rowRects.Count)
                    EditorGUI.DrawRect(rowRects[draggingMod], new Color(0.35f, 0.55f, 0.95f, 0.18f));
                float y = target < rowRects.Count ? rowRects[target].yMin : rowRects[rowRects.Count - 1].yMax;
                var r0 = rowRects[0];
                EditorGUI.DrawRect(new Rect(r0.x, y - 1f, r0.width, 2f), new Color(0.4f, 0.8f, 1f));
            }
            else if (e.type == EventType.MouseDrag) { Repaint(); e.Use(); }
            else if (e.type == EventType.MouseUp)
            {
                int from = draggingMod;
                draggingMod = -1; draggingModList = null;
                int to = target;
                if (from >= 0 && from < list.Count && to != from && to != from + 1)
                {
                    Undo.RecordObject(spec, "Reorder modifier");
                    var mm = list[from];
                    list.RemoveAt(from);
                    if (to > from) to--;
                    to = Mathf.Clamp(to, 0, list.Count);
                    list.Insert(to, mm);
                    EditorUtility.SetDirty(spec);
                }
                e.Use();
                Repaint();
            }
        }

        void DrawModBody(PyreModifier m, float half, string id)
        {
            switch (m)
            {
                // Skew/Contrast/Brightness/Saturation/Sphere/Ordered dither: single-value modifiers, drawn
                // inline in DrawModifiers' header row instead (see TrySingleValueBody) — never reach here.
                case ScaleModifier sc:
                    sc.axis = (ScaleAxis)MiniRadio((int)sc.axis, ScaleAxisLabels);
                    switch (sc.axis)
                    {
                        case ScaleAxis.Vertical: ValRow("Vertical", sc.vertical, 0f, 3f, 1f); break;
                        case ScaleAxis.Horizontal: ValRow("Horizontal", sc.horizontal, 0f, 3f, 1f); break;
                        default: ValRow("Both", sc.both, 0f, 3f, 1f); break;
                    }
                    break;
                case RotateModifier r:
                    ValRow("Degrees", r.degrees, -180f, 180f, 0f);
                    // pivotX/Y are plain floats, not ZUIValue (not animatable) — ZUIValue2DControl can't take
                    // them directly, and promoting the field TYPE to make them animatable would be a real data
                    // model change affecting existing saved BlastSpecs, well beyond a UI swap. ZUI.PositionPad
                    // (works on plain Vector2) is the fit for this tier instead.
                    GUILayout.Label("Pivot", EditorStyles.miniLabel);
                    var rPivot = ZUI.PositionPad(new Vector2(r.pivotX, r.pivotY), new Rect(-1f, -1f, 2f, 2f));
                    r.pivotX = rPivot.x; r.pivotY = rPivot.y;
                    break;
                case WobbleModifier w:
                    ValRow("Amplitude", w.amplitude, 0f, Mathf.Max(4f, half), 0f);
                    ValRow("Frequency", w.frequency, 0f, 8f, 1f);
                    break;
                case SunburstWobbleModifier sw:
                    ValRow("Amplitude", sw.amplitude, 0f, Mathf.Max(4f, half), 3f);
                    ValRow("Frequency (beams)", sw.frequency, 1f, 24f, 6f);
                    ValRow("Rotation", sw.rotation, -180f, 180f, 0f);
                    break;
                case ProfileModifier pm:
                    pm.widthByHeight ??= ProfileModifier.DefaultProfile();
                    CurveField("pyre.profile." + id, "Width by height", pm.widthByHeight, 0f, 2f);
                    ValRow("Strength", pm.strength, 0f, 1f, 1f);
                    break;
                case GroundModifier g:
                    ValRow("Grow angle", g.angle, -180f, 180f, 0f);
                    g.surface = EditorGUILayout.Slider("Surface", g.surface, -1f, 1f);
                    ValRow("Stretch (height)", g.stretch, 0f, 4f, 1f);
                    g.bury = EditorGUILayout.Slider("Bury base", g.bury, 0f, 1f);
                    break;
                case TintModifier t:
                    t.tint = EditorGUILayout.ColorField("Tint", t.tint);
                    t.crossGradient ??= Layer.WhiteGradient();
                    t.crossGradient = EditorGUILayout.GradientField("Cross grad", t.crossGradient);
                    ValRow("Cross amount", t.crossAmount, 0f, 1f, 1f);
                    break;
                case DissolveModifier d:
                    ValRow("Amount", d.amount, 0f, 1f, 0f);
                    d.mode = (DissolveMode)MiniRadio((int)d.mode, DissolveModeLabels);
                    ValRow("Smoothness", d.smoothness, 0f, 1f, 0f);
                    break;
                case LayerDissolveModifier ld:
                    ValRow("Amount", ld.amount, 0f, 1f, 0f);
                    ld.mode = (DissolveMode)MiniRadio((int)ld.mode, DissolveModeLabels);
                    ValRow("Smoothness", ld.smoothness, 0f, 1f, 0f);
                    break;
                case AlphaMaskModifier am:
                    am.shape = (MaskShape)MiniRadio((int)am.shape, MaskShapeLabels);
                    ValRow("Progress", am.progress, 0f, 1f, 1f);
                    am.sharpness = Slider(am.sharpness, 0f, 1f, "Sharpness");
                    ValRow("Size", am.size, 0.1f, 4f, 1f);
                    ValRow("Rotation", am.rotation, -180f, 180f, 0f);
                    // Plain floats (not ZUIValue) — same tier as RotateModifier's pivot above, ZUI.PositionPad
                    // not ZUIValue2DControl. Sits right above the already-converted Noise-drift pad below.
                    GUILayout.Label("Offset", EditorStyles.miniLabel);
                    var amOffset = ZUI.PositionPad(new Vector2(am.offsetX, am.offsetY), new Rect(-1f, -1f, 2f, 2f));
                    am.offsetX = amOffset.x; am.offsetY = amOffset.y;
                    if (am.shape == MaskShape.Noise)
                    {
                        am.noiseWarp = Slider(am.noiseWarp, 0f, 2f, "Noise warp");
                        ZUIValue2DControl.Draw("Noise drift", am.noiseDriftX, am.noiseDriftY,
                            ZUIValue2DControl.Options.Default.WithRange(-64f, 64f, -64f, 64f).WithDefault(Vector2.zero));
                    }
                    break;
                case BloomModifier bm:
                    bm.threshold = Slider(bm.threshold, 0f, 1f, "Threshold");
                    bm.radius = Mathf.RoundToInt(Slider(bm.radius, 0, 16, "Radius (px)"));
                    ValRow("Intensity", bm.intensity, 0f, 3f, 1.2f);
                    break;
                case OutlineModifier om:
                    om.mode = (ColorMode)MiniRadio((int)om.mode, OverLifeFillLabels);
                    om.color ??= new Gradient();
                    om.color = EditorGUILayout.GradientField(
                        om.mode == ColorMode.OverLife ? "Colour (over life)" : "Colour (in→out)", om.color);
                    ValRow("Size (px)", om.size, 0f, 12f, 1f);
                    om.alphaThreshold = Slider(om.alphaThreshold, 0.01f, 1f, "Edge sensitivity");
                    om.innerSoftness = Slider(om.innerSoftness, 0f, 16f, "Inner softness (px)");
                    om.innerSoftnessCurve = Slider(om.innerSoftnessCurve, 0.2f, 5f, "Inner curve");
                    om.outerSoftness = Slider(om.outerSoftness, 0f, 16f, "Outer softness (px)");
                    om.outerSoftnessCurve = Slider(om.outerSoftnessCurve, 0.2f, 5f, "Outer curve");
                    break;
                case JaggModifier jm:
                    jm.arms = Mathf.RoundToInt(Slider(jm.arms, 2, 24, "Arms"));
                    ValRow("Strength", jm.strength, 0f, 0.95f, 0.4f);
                    ValRow("Twist", jm.twist, -180f, 180f, 0f);
                    break;
                case SmudgeModifier sm:
                    ValRow("Brush size (px)", sm.size, 1f, half, 12f);
                    ValRow("Strength (px)", sm.strength, 0f, half, 12f);
                    ValRow("Grow", sm.grow, 0f, 1f, 1f);   // 0→1 front advancing along each stroke (all in parallel)
                    EditorGUILayout.BeginHorizontal();
                    bool painting = paintSmudge == sm;
                    if (Button(painting ? "● Painting — drag to add strokes" : "○ Paint stroke"))
                    { paintSmudge = painting ? null : sm; draggingSmudge = false; Repaint(); }
                    GUILayout.FlexibleSpace();
                    if (Button("⌫ Last", ZUI.Style.Default, GUILayout.Width(56)) && sm.strokes.Count > 0)
                    { sm.strokes.RemoveAt(sm.strokes.Count - 1); EditorUtility.SetDirty(spec); Repaint(); }
                    if (Button("Clear", ZUI.Style.Default, GUILayout.Width(48)))
                    { sm.strokes.Clear(); EditorUtility.SetDirty(spec); Repaint(); }
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Label($"{sm.strokes.Count} stroke(s)", EditorStyles.miniLabel);
                    break;
                case DropShadowModifier ds:
                    // Plain floats, same tier as RotateModifier's pivot / AlphaMaskModifier's offset above.
                    GUILayout.Label("Offset", EditorStyles.miniLabel);
                    var dsOffset = ZUI.PositionPad(new Vector2(ds.offsetX, ds.offsetY), new Rect(-16f, -16f, 32f, 32f));
                    ds.offsetX = dsOffset.x; ds.offsetY = dsOffset.y;
                    ds.color = EditorGUILayout.ColorField("Shadow colour", ds.color);
                    ds.alphaThreshold = Slider(ds.alphaThreshold, 0.01f, 1f, "Edge alpha");
                    break;
                case PosterizeModifier pz:
                    pz.levels = Mathf.RoundToInt(Slider(pz.levels, 2, 16, "Levels"));
                    pz.affectAlpha = Toggle(pz.affectAlpha, "Affect alpha");
                    break;
                case TurbulenceModifier tb:
                    ValRow("Amplitude", tb.amplitude, 0f, Mathf.Max(4f, half), 4f);
                    ValRow("Zoom", tb.zoom, 1f, Mathf.Max(8f, half * 2f), 24f);
                    ValRow("Rotation", tb.rotation, -720f, 720f, 0f);
                    ZUIValue2DControl.Draw("Offset", tb.offsetX, tb.offsetY,
                        ZUIValue2DControl.Options.Default.WithRange(-half, half, -half, half).WithDefault(Vector2.zero));
                    ValRow("Warp", tb.warp, 0f, 2f, 0.6f);
                    break;
                case PerlinTurbulenceModifier pt:
                    ValRow("Amplitude", pt.amplitude, 0f, Mathf.Max(4f, half), 4f);
                    ValRow("Zoom", pt.zoom, 1f, Mathf.Max(8f, half * 2f), 24f);
                    ValRow("Rotation", pt.rotation, -720f, 720f, 0f);
                    ZUIValue2DControl.Draw("Offset", pt.offsetX, pt.offsetY,
                        ZUIValue2DControl.Options.Default.WithRange(-half, half, -half, half).WithDefault(Vector2.zero));
                    ValRow("Warp", pt.warp, 0f, 2f, 0.6f);
                    break;
                case EdgeSmoothModifier es:
                    ValRow("Radius (px)", es.radius, 0f, 16f, 2f, allowMinMax: false);
                    ValRow("Strength", es.strength, 0f, 1f, 1f);
                    break;
                case RingWaveModifier rw:
                    ValRow("Amplitude", rw.amplitude, 0f, Mathf.Max(4f, half), 3f);
                    ValRow("Wavelength", rw.wavelength, 1f, Mathf.Max(8f, half), 10f);
                    ValRow("Phase (travel)", rw.phase, -6f, 6f, 0f);
                    break;
                case PointBlastModifier bl:
                    using (ZUI.HRow())
                    {
                        CompactValue2DRow("Origin", bl.originX, bl.originY,
                            ZUIValue2DControl.Options.Default.WithRange(-half, half, -half, half).WithDefault(Vector2.zero), GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Angle", bl.angleDeg, -180f, 180f, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    bl.arcDegrees = Slider(bl.arcDegrees, 0f, 360f, "Arc (360=disc, 0=line)");
                    using (ZUI.HRow())
                    {
                        CompactValRow("Arc softness", bl.arcSoftness, 0f, 1f, 0.2f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Band width (px)", bl.bandWidth, 1f, Mathf.Max(8f, half * 0.5f), 12f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Radius (px)", bl.radius, 0f, Mathf.Max(4f, half), 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Strength", bl.strength, -20f, 20f, 6f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    break;
                case CloudProjectileModifier cpj:
                    using (ZUI.HRow())
                    {
                        CompactValRow("Angle", cpj.angleDeg, -180f, 180f, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Offset (px)", cpj.offset, -half, half, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Depth", cpj.depth, 0f, 1f, allowMinMax: false);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Radius (px)", cpj.radius, 1f, Mathf.Max(4f, half), 10f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Strength", cpj.strength, -20f, 20f, 6f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Density", cpj.density, 0f, 5f, 1f, allowMinMax: false);
                    break;
                case BallisticShockwaveModifier bs:
                    Label("Projectile", ZUI.ZTextStyle.GroupTitle);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Angle", bs.angleDeg, -180f, 180f, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Offset (px)", bs.offset, -half, half, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Depth", bs.depth, 0f, 1f, allowMinMax: false);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Radius (px)", bs.projectileRadius, 0.5f, Mathf.Max(4f, half * 0.3f), 3f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Force", bs.projectileForce, -20f, 20f, 6f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Erosion", bs.erosion, 0f, 1f, 0.75f, allowMinMax: false);
                    VerticalSpace(4f);

                    Label("Shockwaves", ZUI.ZTextStyle.GroupTitle);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Spacing", bs.waveSpacing, 0.01f, 0.5f, 0.06f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Strength", bs.waveStrength, -20f, 20f, 5f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Expansion (px)", bs.waveExpansion, 0f, Mathf.Max(8f, half), 30f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Decay", bs.waveDecay, 0f, 20f, 6f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Thickness (px)", bs.waveThickness, 0.5f, Mathf.Max(4f, half * 0.2f), 2.5f, allowMinMax: false);
                    VerticalSpace(4f);

                    Label("Vortices", ZUI.ZTextStyle.GroupTitle);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Spacing", bs.vortexSpacing, 0.01f, 0.5f, 0.05f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Strength", bs.vortexStrength, -20f, 20f, 8f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Radius (px)", bs.vortexRadius, 0.5f, Mathf.Max(8f, half * 0.4f), 8f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Decay", bs.vortexDecay, 0f, 20f, 5f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Pulse", bs.vortexPulse, 0f, 5f, 1f, allowMinMax: false);
                    break;
                case PixelFluidModifier pf:
                    Label("Projectile", ZUI.ZTextStyle.GroupTitle);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Angle", pf.angleDeg, -180f, 180f, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Offset (px)", pf.offset, -half, half, 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Depth", pf.depth, 0f, 1f, allowMinMax: false);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Radius (px)", pf.projectileRadius, 0.5f, Mathf.Max(4f, half * 0.3f), 3f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Force", pf.projectileForce, -20f, 20f, 10f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Erosion rate", pf.erosionRate, 0f, 1f, 0.35f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Erosion healing", pf.erosionHealing, 0f, 1f, 0.85f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(4f);

                    Label("Shockwaves", ZUI.ZTextStyle.GroupTitle);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Spacing", pf.waveSpacing, 0.01f, 0.5f, 0.06f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Strength", pf.waveStrength, -20f, 20f, 6f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Expansion (px/f)", pf.waveExpansion, 0f, Mathf.Max(2f, half * 0.1f), 1.5f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Persistence", pf.wavePersistence, 0f, 1f, 0.9f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Thickness (px)", pf.waveThickness, 0.5f, Mathf.Max(4f, half * 0.2f), 2.5f, allowMinMax: false);
                    VerticalSpace(4f);

                    Label("Vortices", ZUI.ZTextStyle.GroupTitle);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Spacing", pf.vortexSpacing, 0.01f, 0.5f, 0.05f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Strength", pf.vortexStrength, -20f, 20f, 10f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Radius (px)", pf.vortexRadius, 0.5f, Mathf.Max(8f, half * 0.4f), 8f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Persistence", pf.vortexPersistence, 0f, 1f, 0.94f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Drift (px/f)", pf.vortexDrift, -5f, 5f, 0.6f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        pf.childShedChance = CompactSlider("Child shed", pf.childShedChance, 0f, 0.5f, GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(4f);

                    Label("Fluid", ZUI.ZTextStyle.GroupTitle);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Drag", pf.velocityDrag, 0f, 1f, 0.85f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Viscosity", pf.viscosity, 0f, 1f, 0.25f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    VerticalSpace(2f);
                    ValRow("Display scale", pf.displayScale, 0f, 5f, 1f, allowMinMax: false);
                    break;
                case SunburstModifier sb:
                    sb.rays = Mathf.RoundToInt(Slider(sb.rays, 2, 32, "Rays"));
                    ValRow("Strength", sb.strength, 0f, 0.95f, 0.6f);
                    sb.sharpness = Slider(sb.sharpness, 0.5f, 8f, "Sharpness");
                    ValRow("Rotation", sb.rotation, -180f, 180f, 0f);
                    break;
                case PulseRingsModifier pr:
                    pr.rings = Mathf.RoundToInt(Slider(pr.rings, 1, 12, "Rings"));
                    ValRow("Speed", pr.speed, -4f, 4f, 1f);
                    ValRow("Strength (px)", pr.strength, 0f, Mathf.Max(4f, half), 3f);
                    break;
                case VoronoiCrackModifier vc:
                    ValRow("Zoom", vc.zoom, 1f, Mathf.Max(8f, half), 10f);
                    ValRow("Rotation", vc.rotation, -720f, 720f, 0f);
                    ZUIValue2DControl.Draw("Drift", vc.driftX, vc.driftY,
                        ZUIValue2DControl.Options.Default.WithRange(-half, half, -half, half).WithDefault(Vector2.zero));
                    ValRow("Seed offset", vc.seedOffset, -8f, 8f, 0f);
                    using (ZUI.HRow())
                    {
                        CompactValRow("Crack width", vc.crackWidth, 0.01f, 3f, 0.15f, allowMinMax: false, width: GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Seam sharpness", vc.seamSharpness, 0.1f, 8f, 1f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    vc.mode = (ColorMode)MiniRadio((int)vc.mode, OverLifeFillLabels);
                    vc.crackTint ??= new Gradient();
                    vc.crackTint = EditorGUILayout.GradientField(
                        vc.mode == ColorMode.Fill ? "Tint (seam→away)" : "Tint (over life)", vc.crackTint);
                    ValRow("Strength", vc.strength, 0f, 1f, 1f);
                    vc.tintCells = Toggle(vc.tintCells, "Tint cells");
                    if (vc.tintCells) ValRow("Cell shade", vc.cellShadeStrength, 0f, 1f, 0.25f);
                    // Captured BEFORE the radio can change it, same reflow-safety reason as colorModeForLayout/
                    // shapeForLayout elsewhere in this file — the control SET drawn this frame must match what
                    // was true at the start of THIS OnGUI pass, not a value the radio just changed mid-draw.
                    var spreadModeForLayout = vc.spreadMode;
                    vc.spreadMode = (CrackSpreadMode)MiniRadio((int)vc.spreadMode, CrackSpreadModeLabels);
                    if (spreadModeForLayout != CrackSpreadMode.Uniform)
                    {
                        ValRow("Spread", vc.spreadProgress, 0f, 1f, 1f);
                        vc.spreadSoftness = Slider(vc.spreadSoftness, 0f, 1f, "Spread softness");
                    }
                    break;
                case ChromaticAberrationModifier ca:
                    ValRow("Amount (px)", ca.amount, 0f, 8f, 1.5f);
                    ValRow("Alpha", ca.alpha, 0f, 1f, 1f);
                    ca.radial = Toggle(ca.radial, "Radial (from centre)");
                    if (!ca.radial) ValRow("Angle", ca.angleDeg, -180f, 180f, 0f);
                    break;
                case PinWarpModifier pw:
                    DrawPinWarpBody(pw);
                    break;
                case CurlModifier cu:
                    DrawCurlBody(cu, half);
                    break;
                case CurlProgressModifier cp:
                    DrawCurlProgressBody(cp, half);
                    break;
                case SphereModifier sp:
                    ValRow("Strength", sp.strength, -5f, 5f, 1f, allowMinMax: false);
                    using (ZUI.HRow())
                    {
                        CompactValue2DRow("Origin", sp.originX, sp.originY,
                            ZUIValue2DControl.Options.Default.WithRange(-half, half, -half, half).WithDefault(Vector2.zero), GroupFieldWidth(2));
                        ZUI.HorizontalSpace();
                        CompactValRow("Radius (0=auto)", sp.radius, 0f, Mathf.Max(4f, half), 0f, allowMinMax: false, width: GroupFieldWidth(2));
                        GUILayout.FlexibleSpace();
                    }
                    break;
                case FuseModifier fu:
                    ValRow("Radius (px)", fu.radius, 0f, Mathf.Max(4f, half * 0.5f), 4f);
                    ValRow("Threshold", fu.threshold, 0f, 1f, 0.5f);
                    ValRow("Softness", fu.softness, 0.02f, 1f, 0.3f);
                    ValRow("Colour bleed", fu.colorBleed, 0f, 1f, 0.4f);
                    break;
                case EdgeWarpModifier re:
                    ValRow("Amplitude", re.amplitude, 0f, Mathf.Max(4f, half * 0.3f), 2f);
                    ValRow("Frequency", re.frequency, 1f, 24f, 6f);
                    ValRow("Jaggedness", re.jaggedness, 0f, 1f, 0.5f);
                    ValRow("Warp", re.warp, 0f, 2f, 0.4f);
                    ValRow("Softness (px)", re.softness, 0f, Mathf.Max(4f, half * 0.2f), 0f);
                    break;
            }
        }

        // Pin warp's inspector body: the authoring toggle + a compact per-pin list (radius, keyframe count,
        // delete pin / delete the keyframe at the current frame). Kept separate from DrawModBody's switch since
        // it's the only modifier with its own preview-click authoring tool (mirrors Smudge's own "paint" section).
        void DrawPinWarpBody(PinWarpModifier pw)
        {
            using (Box("Pin warp — click the preview to add / drag pins"))
            {
                bool editing = editPin == pw;
                if (Button(editing ? "● Editing pins — click preview to add, drag to move" : "○ Edit pins (click preview)"))
                {
                    editPin = editing ? null : pw;
                    pinSel = -1; draggingPin = false; Repaint();
                }

                int curFrame = CurrentFrame();
                int rm = -1;
                for (int i = 0; i < pw.dots.Count; i++)
                {
                    var d = pw.dots[i];
                    if (d == null) continue;
                    using (Box())
                    {
                        EditorGUILayout.BeginHorizontal();
                        if (Button(pinSel == i ? "●" : "○", ZUI.Style.Default, GUILayout.Width(24))) pinSel = i;
                        GUILayout.Label($"Pin #{d.id}", EditorStyles.miniBoldLabel, GUILayout.Width(56));
                        d.radius = EditorGUILayout.Slider("Radius", d.radius, 2f, 128f);
                        if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) rm = i;
                        EditorGUILayout.EndHorizontal();

                        bool hasKeyAtFrame = d.keyframes.Exists(k => k.frame == curFrame);
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label($"{d.keyframes.Count} keyframe(s)", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(!hasKeyAtFrame || d.keyframes.Count <= 1))
                        {
                            if (Button("Remove keyframe @ this frame", ZUI.Style.Default, GUILayout.Width(170)))
                                d.keyframes.RemoveAll(k => k.frame == curFrame);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                if (rm >= 0)
                {
                    pw.dots.RemoveAt(rm);
                    if (pinSel == rm) pinSel = -1;
                    EditorUtility.SetDirty(spec);
                }

                EditorGUILayout.BeginHorizontal();
                if (Button("Clear pins")) { pw.dots.Clear(); pinSel = -1; EditorUtility.SetDirty(spec); }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{pw.dots.Count} pin(s) — frame {curFrame}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        // A ValRow squeezed into a fixed-width column — same trick the preview transport bar uses to pack
        // Frame count/Zoom/Speed onto one shared row (ZUI.HRow + a per-field width cap) instead of each
        // claiming a full-width row of its own. NarrowLabel keeps the field's OWN label from grabbing the
        // ambient EditorGUIUtility.labelWidth (which would eat most of a ~110px column for a one-word label).
        void CompactValRow(string label, ZUIValue v, float lo, float hi, float? def = null, bool allowMinMax = true, float width = CompactValRowWidth)
        {
            using (ZUI.NarrowLabel(label))
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
                ValRow(label, v, lo, hi, def, allowMinMax);
        }

        // A ZUIValue2DControl squeezed into the same fixed-width column CompactValRow uses. Needed because the
        // 2D control's own collapsed-thumbnail body uses GUILayout.ExpandWidth(true) internally — left
        // unconstrained, it greedily fills whatever's left in its row rather than a compact thumbnail, which is
        // exactly what overflowed the Gradient row (Position + Offset both uncapped, sharing a row with Zoom)
        // and forced a horizontal scrollbar. Wrapping it in a fixed VerticalScope caps what "available space"
        // even means to that ExpandWidth call, same fix CompactValRow already applies to ValRow.
        void CompactValue2DRow(string label, ZUIValue x, ZUIValue y, ZUIValue2DControl.Options opts, float width = CompactValRowWidth)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
                ZUIValue2DControl.Draw(label, x, y, opts);
        }

        // Plain-float sibling of CompactValRow, for the handful of scatter fields that are bare floats rather
        // than ZUIValue (perShapeLifeJitter, spawnStagger — sampled once per layer, never animated/randomized
        // per shape, so they were never promoted to a MultiCont). Same packing trick, no "⋯" config button or
        // value-width reservation to fight since ZUI.Slider's plain (non-ZUIValueControl) body is already just
        // one compact row.
        float CompactSlider(string label, float value, float lo, float hi, float width = CompactValRowWidth)
        {
            using (ZUI.NarrowLabel(label))
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
                return Slider(value, lo, hi, label);
        }

        // Curl's inspector body: the ambient swirl params packed onto one row (mirrors the preview transport
        // bar's Frame count/Zoom/Speed row), then a compact per-vortex list (radius/strength/speed/direction/
        // delete packed onto two rows instead of the field-per-row layout this started with), plus the same
        // preview-click authoring toggle Pin warp uses for its own pins.
        void DrawCurlBody(CurlModifier cu, float half)
        {
            using (Box("Curl — ambient swirl"))
            {
                using (ZUI.HRow())
                {
                    CompactValRow("Strength", cu.strength, 0f, 24f, 4f, allowMinMax: false, width: GroupFieldWidth(4));
                    ZUI.HorizontalSpace();
                    CompactValRow("Zoom", cu.zoom, 1f, Mathf.Max(8f, half), 24f, allowMinMax: false, width: GroupFieldWidth(4));
                    ZUI.HorizontalSpace();
                    CompactValRow("Speed", cu.speed, -4f, 4f, 1f, allowMinMax: false, width: GroupFieldWidth(4));
                    ZUI.HorizontalSpace();
                    using (ZUI.NarrowLabel("Warp"))
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(GroupFieldWidth(4))))
                        cu.warp = Slider(cu.warp, 0f, 2f, "Warp");
                    GUILayout.FlexibleSpace();
                }
            }
            using (Box("Curl — vortices (click the preview to add / drag to move)"))
            {
                bool editing = editCurl == cu;
                if (Button(editing ? "● Editing vortices — click preview to add, drag to move" : "○ Edit vortices (click preview)"))
                {
                    editCurl = editing ? null : cu;
                    vortexSel = -1; draggingVortex = false; Repaint();
                }

                int rm = -1;
                for (int i = 0; i < cu.vortices.Count; i++)
                {
                    var v = cu.vortices[i];
                    if (v == null) continue;
                    using (Box())
                    {
                        // Row 1: identity + direction + delete — every control that ISN'T a ZUIValue, so the
                        // three animatable fields below get the whole row to themselves.
                        EditorGUILayout.BeginHorizontal();
                        if (Button(vortexSel == i ? "●" : "○", ZUI.Style.Default, GUILayout.Width(24))) vortexSel = i;
                        GUILayout.Label($"#{i + 1}", EditorStyles.miniBoldLabel, GUILayout.Width(24));
                        v.clockwise = GUILayout.Toggle(v.clockwise, v.clockwise ? "CW" : "CCW", "Button", GUILayout.Width(40));
                        GUILayout.FlexibleSpace();
                        if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) rm = i;
                        EditorGUILayout.EndHorizontal();
                        VerticalSpace(2f);

                        // Row 2: Radius/Strength/Speed, all animatable — packed side by side like the ambient
                        // box above, instead of Radius alone eating row 1 and Strength+Speed eating row 2.
                        using (ZUI.HRow())
                        {
                            CompactValRow("Radius", v.radius, 2f, Mathf.Max(8f, half), 24f, allowMinMax: false, width: GroupFieldWidth(3));
                            ZUI.HorizontalSpace();
                            CompactValRow("Strength°", v.strength, 0f, 50f, 25f, allowMinMax: false, width: GroupFieldWidth(3));
                            ZUI.HorizontalSpace();
                            CompactValRow("Speed", v.speed, -4f, 4f, 1f, allowMinMax: false, width: GroupFieldWidth(3));
                            GUILayout.FlexibleSpace();
                        }
                    }
                }
                if (rm >= 0)
                {
                    cu.vortices.RemoveAt(rm);
                    if (vortexSel == rm) vortexSel = -1;
                    EditorUtility.SetDirty(spec);
                }

                EditorGUILayout.BeginHorizontal();
                if (Button("Clear vortices")) { cu.vortices.Clear(); vortexSel = -1; EditorUtility.SetDirty(spec); }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{cu.vortices.Count} vortex(es)", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        // Vortex field (progress)'s inspector body — same per-vortex layout as DrawCurlBody's vortex box (they
        // share the click-to-place/drag authoring below via IVortexHost), just Progress in place of Speed and no
        // ambient-swirl box, since this modifier is vortices-only.
        void DrawCurlProgressBody(CurlProgressModifier cp, float half)
        {
            using (Box("Vortex field (progress) — click the preview to add / drag to move"))
            {
                bool editing = editCurl == cp;
                if (Button(editing ? "● Editing vortices — click preview to add, drag to move" : "○ Edit vortices (click preview)"))
                {
                    editCurl = editing ? null : cp;
                    vortexSel = -1; draggingVortex = false; Repaint();
                }

                int rm = -1;
                for (int i = 0; i < cp.vortices.Count; i++)
                {
                    var v = cp.vortices[i];
                    if (v == null) continue;
                    using (Box())
                    {
                        EditorGUILayout.BeginHorizontal();
                        if (Button(vortexSel == i ? "●" : "○", ZUI.Style.Default, GUILayout.Width(24))) vortexSel = i;
                        GUILayout.Label($"#{i + 1}", EditorStyles.miniBoldLabel, GUILayout.Width(24));
                        v.clockwise = GUILayout.Toggle(v.clockwise, v.clockwise ? "CW" : "CCW", "Button", GUILayout.Width(40));
                        GUILayout.FlexibleSpace();
                        if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) rm = i;
                        EditorGUILayout.EndHorizontal();
                        VerticalSpace(2f);

                        using (ZUI.HRow())
                        {
                            CompactValRow("Radius", v.radius, 2f, Mathf.Max(8f, half), 24f, allowMinMax: false, width: GroupFieldWidth(3));
                            ZUI.HorizontalSpace();
                            CompactValRow("Strength°", v.strength, 0f, 50f, 25f, allowMinMax: false, width: GroupFieldWidth(3));
                            ZUI.HorizontalSpace();
                            CompactValRow("Progress", v.progress, 0f, 1f, null, allowMinMax: false, width: GroupFieldWidth(3));
                            GUILayout.FlexibleSpace();
                        }
                    }
                }
                if (rm >= 0)
                {
                    cp.vortices.RemoveAt(rm);
                    if (vortexSel == rm) vortexSel = -1;
                    EditorUtility.SetDirty(spec);
                }

                EditorGUILayout.BeginHorizontal();
                if (Button("Clear vortices")) { cp.vortices.Clear(); vortexSel = -1; EditorUtility.SetDirty(spec); }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{cp.vortices.Count} vortex(es)", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        void ShowAddModifierMenu(System.Collections.Generic.List<PyreModifier> list, bool isGlobal)
        {
            var menu = new GenericMenu();
            void Add(string label, System.Func<PyreModifier> make) =>
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    Undo.RecordObject(spec, "Add modifier");
                    list.Add(make());
                    EditorUtility.SetDirty(spec);
                    Repaint();
                });
            // Geometry: warps the coordinate frame (silhouette + fill both move) — includes EdgeWarp even
            // though it's technically its own base class (EdgeModifier, silhouette-only), since "this changes
            // the shape's boundary" is where a user looks for it regardless of the C# family underneath.
            Add("Geometry/Skew", () => new SkewModifier());
            Add("Geometry/Rotate", () => new RotateModifier());
            Add("Geometry/Scale", () => new ScaleModifier());
            Add("Geometry/Wobble", () => new WobbleModifier());
            Add("Geometry/Sunburst wobble", () => new SunburstWobbleModifier());
            Add("Geometry/Profile (mold shape)", () => new ProfileModifier());
            Add("Geometry/Ground (grow from surface)", () => new GroundModifier());
            Add("Geometry/Jagg (star)", () => new JaggModifier());
            Add("Geometry/Edge warp (jagged, wavy silhouette only)", () => new EdgeWarpModifier());
            Add("Geometry/Smudge", () => new SmudgeModifier());
            Add("Geometry/Turbulence (churn)", () => new TurbulenceModifier());
            Add("Geometry/Perlin turbulence (sharper churn)", () => new PerlinTurbulenceModifier());
            Add("Geometry/Curl (swirl)", () => new CurlModifier());
            Add("Geometry/Vortex field (progress)", () => new CurlProgressModifier());
            Add("Geometry/Sphere (fake depth)", () => new SphereModifier());
            Add("Geometry/Ring wave (shockwave ripple)", () => new RingWaveModifier());
            Add("Geometry/Pulse rings (radius-relative shockwave)", () => new PulseRingsModifier());
            Add("Geometry/Blast (disc, arc, or line)", () => new PointBlastModifier());
            Add("Geometry/Sunburst (star silhouette)", () => new SunburstModifier());
            Add("Geometry/Pin warp (hand-animated drag)", () => new PinWarpModifier());
            // Colour: recolours pixels without changing what's visible (alpha untouched, or only a side effect).
            Add("Colour/Tint", () => new TintModifier());
            Add("Colour/Contrast", () => new ContrastModifier());
            Add("Colour/Brightness", () => new BrightnessModifier());
            Add("Colour/Saturation", () => new SaturationModifier());
            Add("Colour/Posterize", () => new PosterizeModifier());
            Add("Colour/Voronoi crack", () => new VoronoiCrackModifier());
            // Alpha: changes WHAT'S VISIBLE and where — erosion/dither/masking, as opposed to Colour's shading.
            // Dissolve/Layer dissolve are mutually exclusive by CONTEXT, not user choice: Dissolve hashes in
            // fixed screen space (fine for the global list, which has no single shape/geometry stack to be
            // "local" to); Layer dissolve hashes in this layer's own geometry-WARPED local space (so a Sphere/
            // Ground/Jagg earlier in the SAME layer's stack drags the erase pattern along with it) — meaningless
            // at the global level, where there's no one shape's stack to read.
            if (isGlobal) Add("Alpha/Dissolve", () => new DissolveModifier());
            else Add("Alpha/Layer dissolve (follows this layer's own geometry warps)", () => new LayerDissolveModifier());
            Add("Alpha/Ordered dither", () => new OrderedDitherModifier());
            Add("Alpha/Alpha mask", () => new AlphaMaskModifier());
            // Whole-frame post effects: on a layer they isolate to that layer's own pixels (rendered into a
            // private buffer, post-processed, then composited); on the global list they run once after every
            // layer composites — see BlastRenderer.FinishLayerPost / the global post pass at RenderFrame's tail.
            Add("Post/Bloom (glow)", () => new BloomModifier());
            Add("Post/Outline", () => new OutlineModifier());
            Add("Post/Edge smooth", () => new EdgeSmoothModifier());
            Add("Post/Drop shadow", () => new DropShadowModifier());
            Add("Post/Chromatic aberration", () => new ChromaticAberrationModifier());
            Add("Post/Fuse (blob melt)", () => new FuseModifier());
            Add("Post/Cloud projectile", () => new CloudProjectileModifier());
            Add("Post/Ballistic shockwave (rings + vortex street)", () => new BallisticShockwaveModifier());
            menu.ShowAsContext();
        }

        // ── vertical splitter between the left pane and the preview ──────────────
        void DrawVerticalSplitter()
        {
            Rect r = GUILayoutUtility.GetRect(6f, 6f, GUILayout.Width(6f), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { dragLeft = true; e.Use(); }
            if (dragLeft)
            {
                if (e.type == EventType.MouseDrag)
                {
                    leftWidth = Mathf.Clamp(e.mousePosition.x, 240f, Mathf.Max(260f, position.width - 260f));
                    Repaint();
                }
                if (e.type == EventType.MouseUp) { dragLeft = false; }
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseUp) e.Use();
            }
        }

        // ── right: live preview ──────────────────────────────────────────────────
        void DrawPreview()
        {
            EditorGUILayout.BeginVertical();

            int cur = CurrentFrame();

            // viewport (resizable via the horizontal splitter below it)
            Rect view = GUILayoutUtility.GetRect(200f, previewHeight, GUILayout.ExpandWidth(true),
                                                 GUILayout.Height(previewHeight));
            lastView = view;
            if (Event.current.type == EventType.Repaint)
            {
                DrawBackdrop(view);
                if (stageBg != null) PreviewStageGUI.Draw(view, stageBg, zoom, false);   // backdrop sprites BEHIND the frame

                // Live animated preview subject (if configured) — rendered through a REAL isolated camera and
                // the subject's own real gameplay components (see LiveScenePreview / IPyrePreviewSubject),
                // not hand-drawn IMGUI pivot/scale math — so it's pixel-for-pixel what gameplay would render.
                // Anchored near the viewport's BOTTOM edge (its own pivot, typically a character's feet, lands
                // there) via the render rect's own placement, not dead-centre: a pivot at the exact centre
                // pushes the whole body into the upper half of the view. The blast's own canvas keeps
                // rendering as raw canvas-pixels × zoom (pixel-perfect WYSIWYG authoring — origin handle,
                // MetaBlob, Smudge, PinWarp all rely on that 1:1 canvas-pixel mapping and must NOT change);
                // it then gets an extra auto-computed offset so its own origin marker lands exactly on the
                // subject's live attach point, itself read back from the SAME real camera's own projection
                // (Camera.WorldToScreenPoint) — not a second, hand-matched formula.
                var subject = ResolvePreviewSubject();
                subjectAlignOffset = Vector2.zero;
                if (subject != null && spec != null)
                {
                    var live = LivePreview;

                    // This blast's own canvas-px-per-world-unit (its pixelsPerUnit) times the shared zoom —
                    // the same conversion the blast's canvas uses — so the subject renders at its TRUE size
                    // relative to the (fixed-canvas-pixel) blast, matching how their relative on-screen size
                    // actually changes in gameplay when either asset's pixelsPerUnit is tuned.
                    float screenPxPerWorldUnit = zoom * spec.pixelsPerUnit;
                    Vector2 subjectAnchorScreen = new Vector2(view.center.x, view.yMax - view.height * 0.15f) + previewPan;
                    float boxSize = view.height;
                    Rect subjectRect = new Rect(subjectAnchorScreen.x - boxSize * 0.5f, subjectAnchorScreen.y - boxSize * 0.5f, boxSize, boxSize);

                    subject.SpawnInto(live, Vector3.zero);
                    live.Frame(Vector3.zero, boxSize / screenPxPerWorldUnit);
                    GUI.BeginClip(view);
                    live.Draw(new Rect(subjectRect.x - view.x, subjectRect.y - view.y, subjectRect.width, subjectRect.height));
                    GUI.EndClip();

                    if (subject.TryGetAttachWorldPos(out var attachWorld))
                    {
                        // The camera's own projection — the same one that just rendered the subject — turns
                        // the attach point into a screen position, so alignment can never drift from size.
                        // WorldToScreenPoint returns coordinates in the render TEXTURE's own pixel space, which
                        // PreviewRenderUtility renders at retina/HiDPI resolution (camera.pixelWidth/Height),
                        // NOT in the GUI-point space subjectRect is expressed in — scale back down by the
                        // actual ratio between the two, or the offset overshoots by the display's DPI factor.
                        Vector3 sp = live.Camera.WorldToScreenPoint(attachWorld);
                        float px2pt = subjectRect.width / Mathf.Max(1, live.Camera.pixelWidth);
                        float py2pt = subjectRect.height / Mathf.Max(1, live.Camera.pixelHeight);
                        Vector2 attachScreen = new Vector2(subjectRect.x + sp.x * px2pt, subjectRect.y + (subjectRect.height - sp.y * py2pt));
                        float ow = spec.Width * zoom, oh = spec.Height * zoom;
                        Vector2 originNoOffset = new Vector2(
                            view.x + (view.width - ow) * 0.5f + previewPan.x + Mathf.Clamp01(spec.origin.x) * ow,
                            view.y + (view.height - oh) * 0.5f + previewPan.y + oh - Mathf.Clamp01(spec.origin.y) * oh);
                        subjectAlignOffset = attachScreen - originNoOffset;
                    }
                }

                if (spec == null)
                {
                    var c = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                    GUI.Label(view, "No blast selected", c);
                }
                else { UpdatePreviewTexture(cur);
                if (previewTex != null)
                {
                    float w = spec.Width * zoom, h = spec.Height * zoom;
                    GUI.BeginClip(view);
                    var local = new Rect((view.width - w) * 0.5f + previewPan.x + subjectAlignOffset.x,
                                          (view.height - h) * 0.5f + previewPan.y + subjectAlignOffset.y, w, h);
                    GUI.DrawTexture(local, previewTex, ScaleMode.StretchToFill, true);
                    if (showFrame)
                    {
                        var frameCol = new Color(1f, 1f, 1f, 0.55f);
                        EditorGUI.DrawRect(new Rect(local.x, local.y, local.width, 1f), frameCol);
                        EditorGUI.DrawRect(new Rect(local.x, local.yMax - 1f, local.width, 1f), frameCol);
                        EditorGUI.DrawRect(new Rect(local.x, local.y, 1f, local.height), frameCol);
                        EditorGUI.DrawRect(new Rect(local.xMax - 1f, local.y, 1f, local.height), frameCol);
                    }
                    GUI.EndClip();
                }
                }
                if (stageBg != null) PreviewStageGUI.Draw(view, stageBg, zoom, true);   // decoration sprites IN FRONT of the frame
            }

            // Priority for a click in the viewport: (0) Smudge stroke painting when a Smudge modifier is armed — it
            // owns the drag entirely; (1) Pin warp pins when a Pin warp modifier is armed (add / select / drag);
            // (1.5) Curl vortices when a Curl modifier is armed, same reasoning as Pin warp; (2) MetaBlob orbs when
            // editing a MetaBlob layer (place / drag) — it must win over the centred origin ✛, or placing near the
            // centre would grab the pivot instead; (3) origin ✛ handle, (4) a stage sprite, (5) fall through to
            // panning the frame. Each Use()s its event.
            HandleSmudgePaint(view);
            HandlePinWarp(view);
            HandleCurlVortices(view);
            HandleMetaBlob(view);
            DrawMetaOrbMarkers(view);
            DrawSmudgeStroke(view);
            DrawPinMarkers(view);
            DrawCurlVortexMarkers(view);
            if (spec != null) DrawOriginHandle(view);
            if (stageBg != null && PreviewStageGUI.Edit(view, stageBg, zoom, ref stageSel, ref draggingStage))
                EditorUtility.SetDirty(stageBg);

            // Position the animation frame against the backdrop: left-drag empty space (or middle-drag anywhere).
            var pe = Event.current;
            if (pe.type == EventType.MouseDown && (pe.button == 0 || pe.button == 2) && view.Contains(pe.mousePosition))
            { draggingPan = true; pe.Use(); }
            if (draggingPan)
            {
                if (pe.type == EventType.MouseDrag) { previewPan += pe.delta; Repaint(); pe.Use(); }
                if (pe.type == EventType.MouseUp) { draggingPan = false; pe.Use(); }
            }

            DrawPreviewSplitter();

            // transport
            EditorGUILayout.BeginHorizontal();
            if (Button(playing ? "❚❚ Pause" : "▶ Play")) { playing = !playing; scrub = -1; }
            if (Button("⟲ Restart")) { frame = 0; acc = 0f; scrub = -1; previewSubject?.Restart(); }
            if (Button("Fit")) { FitZoom(); previewPan = Vector2.zero; }
            if (Button("Centre")) previewPan = Vector2.zero;
            showFrame = Toggle(showFrame, "Frame");
            if (Button("Bake")) BlastBaker.Bake(spec);
            EditorGUILayout.EndHorizontal();

            // frame scrubber — dragging it pauses playback and holds the frame
            int shown = scrub >= 0 ? scrub : frame;
            int scrubbed = Mathf.RoundToInt(Slider(shown, 0, Mathf.Max(0, FrameCount - 1), "Frame"));
            if (scrubbed != shown) { scrub = scrubbed; playing = false; Repaint(); }

            // retime, zoom, speed — three settings tweaked far less often than the scrubber above, so they
            // share one row instead of each claiming a full-width slider row of their own. Each still gets a
            // bit more than the bare minimum (the row has room to spare with only 3 of them in it — taking
            // horizontal space isn't bad by itself, cramming or truncating is) and ZUI.HorizontalSpace()
            // between each pair, not one FlexibleSpace dumped at the end, so the gap reads as deliberate
            // separation rather than leftover space.
            using (ZUI.HRow())
            {
                var sliderOpts = new[] { GUILayout.Width(CompactSliderWidth + 40f) };
                int fc = Mathf.RoundToInt(ZUI.MicroSlider(spec.frameCount, 1, 64, "Frame count", showInputField: true, options: sliderOpts));
                if (fc != spec.frameCount)
                {
                    Undo.RecordObject(spec, "Change frame count");
                    spec.frameCount = Mathf.Max(1, fc); EditorUtility.SetDirty(spec); frame = Mathf.Min(frame, FrameCount - 1);
                }
                ZUI.HorizontalSpace(2f);

                zoom = Mathf.Max(1f, Mathf.Round(ZUI.MicroSlider(zoom, 1f, 16f, "Zoom", showInputField: true, options: sliderOpts)));
                ZUI.HorizontalSpace(2f);

                // 1 decimal — a preview playback speed multiplier has no practical use finer than 0.1
                // increments, and constraining it (not just cleaning up float noise) keeps the input field
                // from ever having more to show than the value actually means.
                speed = ZUI.MicroSlider(speed, 0.1f, 3f, "Speed", decimals: 1, showInputField: true, options: sliderOpts);
                ZUI.HorizontalSpace(2f);

                GUILayout.FlexibleSpace();
                // No fixed width: a 70px cap was right on the edge of what "frame 64/64" (2-digit FrameCount's
                // own max) needs, so it wrapped/reflowed depending on exact digit count and font metrics —
                // changing the row's own height frame-to-frame during playback as the counter ticked over a
                // digit boundary, which read as the whole preview flickering. It's the last thing on this row
                // with nothing after it, so auto-sizing to its own content is exactly as safe as FitWidth.
                Label($"frame {cur + 1}/{FrameCount}", ZUI.ZTextStyle.Subtle, GUILayout.ExpandWidth(false));
            }

            VerticalSpace();
            DrawBackdropOptions();

            EditorGUILayout.EndVertical();
        }

        // A thin horizontal handle under the viewport that drags its height.
        void DrawPreviewSplitter()
        {
            Rect r = GUILayoutUtility.GetRect(20f, 6f, GUILayout.ExpandWidth(true), GUILayout.Height(6f));
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical);

            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { dragPreview = true; e.Use(); }
            if (dragPreview)
            {
                if (e.type == EventType.MouseDrag) { previewHeight = Mathf.Clamp(previewHeight + e.delta.y, 120f, 1200f); Repaint(); }
                if (e.type == EventType.MouseUp) { dragPreview = false; }
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseUp) e.Use();
            }
        }

        // A draggable ✛ marking the blast's origin/pivot over the preview. Dragging it writes spec.origin (0..1,
        // y bottom-up). This is the point a game (Colosseum) aligns to the hit pixel.
        void DrawOriginHandle(Rect view)
        {
            float w = spec.Width * zoom, h = spec.Height * zoom;
            Rect spr = new Rect(view.x + (view.width - w) * 0.5f + previewPan.x + subjectAlignOffset.x,
                                 view.y + (view.height - h) * 0.5f + previewPan.y + subjectAlignOffset.y, w, h);
            float ox = spr.x + Mathf.Clamp01(spec.origin.x) * w;
            float oy = spr.yMax - Mathf.Clamp01(spec.origin.y) * h;   // origin.y = 0 is the bottom

            var e = Event.current;
            Rect zone = new Rect(ox - 8f, oy - 8f, 16f, 16f);
            if (view.Contains(new Vector2(ox, oy))) EditorGUIUtility.AddCursorRect(zone, MouseCursor.MoveArrow);

            if (e.type == EventType.MouseDown && e.button == 0 && zone.Contains(e.mousePosition) && view.Contains(e.mousePosition))
            { Undo.RecordObject(spec, "Move origin"); draggingOrigin = true; e.Use(); }
            if (draggingOrigin)
            {
                if (e.type == EventType.MouseDrag)
                {
                    spec.origin = new Vector2(Mathf.Clamp01((e.mousePosition.x - spr.x) / Mathf.Max(1f, w)),
                                              Mathf.Clamp01((spr.yMax - e.mousePosition.y) / Mathf.Max(1f, h)));
                    EditorUtility.SetDirty(spec); Repaint(); e.Use();
                }
                if (e.type == EventType.MouseUp) { draggingOrigin = false; e.Use(); }
            }

            if (e.type == EventType.Repaint && view.Contains(new Vector2(ox, oy)) && originMarkerAlpha > 0.001f)
            {
                // Oscillate the marker white↔black fast so it reads against ANY backdrop colour. Alpha from the slider.
                bool white = Mathf.Repeat((float)(EditorApplication.timeSinceStartup * 6.0), 1f) < 0.5f;
                Color c = white ? Color.white : Color.black;   c.a = originMarkerAlpha;
                EditorGUI.DrawRect(new Rect(ox - 7f, oy - 1f, 14f, 2f), c);
                EditorGUI.DrawRect(new Rect(ox - 1f, oy - 7f, 2f, 14f), c);
            }
        }

        // The frame's on-screen rect in window space (incl. zoom + pan) — shared by the origin handle + MetaBlob.
        Rect FrameRect(Rect view)
        {
            float w = spec.Width * zoom, h = spec.Height * zoom;
            return new Rect(view.x + (view.width - w) * 0.5f + previewPan.x, view.y + (view.height - h) * 0.5f + previewPan.y, w, h);
        }

        // MetaBlob authoring in the preview: place mode drops orbs on click (birth by order); otherwise drag an orb.
        void HandleMetaBlob(Rect view)
        {
            if (spec == null || layerSel < 0 || layerSel >= spec.layers.Count) return;
            var l = spec.layers[layerSel];
            if (l == null || l.shape != LayerShape.MetaBlob) return;

            var e = Event.current;
            Vector2 ctr = FrameRect(view).center;

            if (placeMetaMode)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
                {
                    float ox = (e.mousePosition.x - ctr.x) / Mathf.Max(0.01f, zoom);
                    float oy = (ctr.y - e.mousePosition.y) / Mathf.Max(0.01f, zoom);   // canvas y is up
                    float birth = Mathf.Clamp01(l.metaOrbs.Count * l.metaSpawnInterval);
                    Undo.RecordObject(spec, "Place MetaBlob orb");
                    l.metaOrbs.Add(new MetaOrb { pos = new Vector2(ox, oy), radius = 12f, birth = birth, life = Mathf.Clamp(1f - birth, 0.25f, 1f) });
                    metaSel = l.metaOrbs.Count - 1;
                    EditorUtility.SetDirty(spec); e.Use(); Repaint();
                }
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
                for (int i = l.metaOrbs.Count - 1; i >= 0; i--)
                {
                    var o = l.metaOrbs[i];
                    Vector2 mp = new Vector2(ctr.x + o.pos.x * zoom, ctr.y - o.pos.y * zoom);
                    if ((mp - e.mousePosition).sqrMagnitude <= 100f)
                    { Undo.RecordObject(spec, "Move MetaBlob orb"); metaSel = i; draggingMetaOrb = true; e.Use(); break; }
                }
            if (draggingMetaOrb && metaSel >= 0 && metaSel < l.metaOrbs.Count)
            {
                if (e.type == EventType.MouseDrag)
                {
                    l.metaOrbs[metaSel].pos += new Vector2(e.delta.x, -e.delta.y) / Mathf.Max(0.01f, zoom);
                    EditorUtility.SetDirty(spec); Repaint(); e.Use();
                }
                if (e.type == EventType.MouseUp) { draggingMetaOrb = false; e.Use(); }
            }
        }

        // Pin warp authoring: while `editPin` is armed, clicking an existing pin selects+drags it (each drag event
        // records/updates a keyframe at the CURRENT scrubbed frame); clicking empty space adds a new pin there,
        // whose first keyframe (at the current frame) doubles as its rest position and radius-of-influence anchor.
        void HandlePinWarp(Rect view)
        {
            if (editPin == null || spec == null) return;
            var e = Event.current;
            Vector2 ctr = FrameRect(view).center;
            int curFrame = CurrentFrame();

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                for (int i = editPin.dots.Count - 1; i >= 0; i--)
                {
                    var d = editPin.dots[i];
                    if (d == null) continue;
                    Vector2 p = d.Evaluate(curFrame);
                    Vector2 mp = new Vector2(ctr.x + p.x * zoom, ctr.y - p.y * zoom);
                    if ((mp - e.mousePosition).sqrMagnitude <= 100f)
                    { Undo.RecordObject(spec, "Move pin"); pinSel = i; draggingPin = true; e.Use(); return; }
                }
                // No existing pin under the click — add a new one, born here, at the current frame.
                float ox = (e.mousePosition.x - ctr.x) / Mathf.Max(0.01f, zoom);
                float oy = (ctr.y - e.mousePosition.y) / Mathf.Max(0.01f, zoom);
                int nextId = 0;
                foreach (var d in editPin.dots) if (d != null) nextId = Mathf.Max(nextId, d.id + 1);
                var dot = new PinDot { id = nextId, radius = 16f };
                dot.SetKeyframe(curFrame, new Vector2(ox, oy));
                Undo.RecordObject(spec, "Add pin");
                editPin.dots.Add(dot);
                pinSel = editPin.dots.Count - 1;
                EditorUtility.SetDirty(spec); e.Use(); Repaint();
                return;
            }

            if (draggingPin && pinSel >= 0 && pinSel < editPin.dots.Count)
            {
                if (e.type == EventType.MouseDrag)
                {
                    var d = editPin.dots[pinSel];
                    Vector2 p = d.Evaluate(curFrame) + new Vector2(e.delta.x, -e.delta.y) / Mathf.Max(0.01f, zoom);
                    d.SetKeyframe(curFrame, p);
                    EditorUtility.SetDirty(spec); Repaint(); e.Use();
                }
                if (e.type == EventType.MouseUp) { draggingPin = false; e.Use(); }
            }
        }

        // Draw every pin of the armed Pin warp modifier: a wire circle for its radius of influence, a marker at
        // its position AT THE CURRENT SCRUBBED FRAME (filled = an explicit keyframe sits on this exact frame,
        // hollow = this frame is interpolated/held between keyframes), and its id.
        void DrawPinMarkers(Rect view)
        {
            if (Event.current.type != EventType.Repaint || editPin == null) return;
            Vector2 ctr = FrameRect(view).center;
            int curFrame = CurrentFrame();
            Handles.BeginGUI();
            var prevC = Handles.color;
            for (int i = 0; i < editPin.dots.Count; i++)
            {
                var d = editPin.dots[i];
                if (d == null) continue;
                Vector2 p = d.Evaluate(curFrame);
                Vector2 mp = new Vector2(ctr.x + p.x * zoom, ctr.y - p.y * zoom);
                if (!view.Contains(mp)) continue;
                bool sel = pinSel == i;
                bool exactKeyframe = d.keyframes != null && d.keyframes.Exists(k => k.frame == curFrame);
                Color c = sel ? new Color(1f, 0.7f, 0.2f) : new Color(0.4f, 1f, 0.6f, 0.9f);
                Handles.color = new Color(c.r, c.g, c.b, 0.35f);
                Handles.DrawWireDisc(new Vector3(mp.x, mp.y, 0f), Vector3.forward, d.radius * zoom);
                Handles.color = c;
                if (exactKeyframe) EditorGUI.DrawRect(new Rect(mp.x - 3f, mp.y - 3f, 6f, 6f), c);
                else Handles.DrawWireDisc(new Vector3(mp.x, mp.y, 0f), Vector3.forward, 3f);
                GUI.Label(new Rect(mp.x + 5f, mp.y - 9f, 26f, 14f), d.id.ToString(), EditorStyles.miniLabel);
            }
            Handles.color = prevC;
            Handles.EndGUI();
        }

        // Vortex authoring, shared by CurlModifier and CurlProgressModifier (whichever is armed as `editCurl`, via
        // the IVortexHost interface): clicking an existing vortex selects+drags it; clicking empty space adds a
        // new one there with default radius/strength — mirrors HandlePinWarp, minus the keyframe machinery (a
        // vortex has just one, non-animated placement).
        void HandleCurlVortices(Rect view)
        {
            if (editCurl == null || spec == null) return;
            var e = Event.current;
            Vector2 ctr = FrameRect(view).center;

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                for (int i = editCurl.Vortices.Count - 1; i >= 0; i--)
                {
                    var v = editCurl.Vortices[i];
                    if (v == null) continue;
                    Vector2 mp = new Vector2(ctr.x + v.pos.x * zoom, ctr.y - v.pos.y * zoom);
                    if ((mp - e.mousePosition).sqrMagnitude <= 100f)
                    { Undo.RecordObject(spec, "Move vortex"); vortexSel = i; draggingVortex = true; e.Use(); return; }
                }
                // No existing vortex under the click — add a new one here.
                float ox = (e.mousePosition.x - ctr.x) / Mathf.Max(0.01f, zoom);
                float oy = (ctr.y - e.mousePosition.y) / Mathf.Max(0.01f, zoom);
                Undo.RecordObject(spec, "Add vortex");
                editCurl.Vortices.Add(new VortexPoint { pos = new Vector2(ox, oy) });
                vortexSel = editCurl.Vortices.Count - 1;
                EditorUtility.SetDirty(spec); e.Use(); Repaint();
                return;
            }

            if (draggingVortex && vortexSel >= 0 && vortexSel < editCurl.Vortices.Count)
            {
                if (e.type == EventType.MouseDrag)
                {
                    editCurl.Vortices[vortexSel].pos += new Vector2(e.delta.x, -e.delta.y) / Mathf.Max(0.01f, zoom);
                    EditorUtility.SetDirty(spec); Repaint(); e.Use();
                }
                if (e.type == EventType.MouseUp) { draggingVortex = false; e.Use(); }
            }
        }

        // Draw every vortex of the armed Curl / Vortex field (progress) modifier: a wire circle for its radius of
        // influence, a short tick showing spin direction (CW/CCW), and its index — mirrors DrawPinMarkers.
        void DrawCurlVortexMarkers(Rect view)
        {
            if (Event.current.type != EventType.Repaint || editCurl == null) return;
            Vector2 ctr = FrameRect(view).center;
            Handles.BeginGUI();
            var prevC = Handles.color;
            for (int i = 0; i < editCurl.Vortices.Count; i++)
            {
                var v = editCurl.Vortices[i];
                if (v == null) continue;
                Vector2 mp = new Vector2(ctr.x + v.pos.x * zoom, ctr.y - v.pos.y * zoom);
                if (!view.Contains(mp)) continue;
                bool sel = vortexSel == i;
                Color c = sel ? new Color(1f, 0.7f, 0.2f) : new Color(0.5f, 0.8f, 1f, 0.9f);
                Handles.color = new Color(c.r, c.g, c.b, 0.35f);
                // An approximation (the Static-mode value) for the gizmo circle — precise enough to place the
                // vortex by eye even when Radius is animated; the actual bake evaluates the real curve/range.
                Handles.DrawWireDisc(new Vector3(mp.x, mp.y, 0f), Vector3.forward, v.radius.staticValue * zoom);
                Handles.color = c;
                EditorGUI.DrawRect(new Rect(mp.x - 3f, mp.y - 3f, 6f, 6f), c);
                // A short tangential tick showing spin direction — CW swings down-right, CCW swings up-right.
                float tickAng = (v.clockwise ? -1f : 1f) * 40f * Mathf.Deg2Rad;
                Vector2 tick = new Vector2(Mathf.Cos(tickAng), Mathf.Sin(tickAng)) * 14f;
                Handles.DrawLine(new Vector3(mp.x, mp.y, 0f), new Vector3(mp.x + tick.x, mp.y - tick.y, 0f));
                GUI.Label(new Rect(mp.x + 5f, mp.y - 9f, 60f, 14f), $"{i + 1} {(v.clockwise ? "CW" : "CCW")}", EditorStyles.miniLabel);
            }
            Handles.color = prevC;
            Handles.EndGUI();
        }

        // Smudge authoring: while a Smudge modifier is in paint mode, EACH left-drag in the preview records a NEW
        // stroke of canvas-centre points (min-spaced so the polyline stays light). Multiple strokes accumulate and
        // grow in parallel at bake time. A click with no drag (<2 points) is dropped.
        void HandleSmudgePaint(Rect view)
        {
            if (paintSmudge == null || spec == null) return;
            var e = Event.current;
            Vector2 ctr = FrameRect(view).center;
            Vector2 ToCanvas(Vector2 mp) => new Vector2((mp.x - ctr.x) / Mathf.Max(0.01f, zoom),
                                                        (ctr.y - mp.y) / Mathf.Max(0.01f, zoom));

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                Undo.RecordObject(spec, "Paint smudge stroke");
                var stroke = new SmudgeStroke();
                stroke.points.Add(ToCanvas(e.mousePosition));
                paintSmudge.strokes.Add(stroke);
                draggingSmudge = true; EditorUtility.SetDirty(spec); e.Use(); Repaint();
            }
            else if (draggingSmudge && e.type == EventType.MouseDrag && paintSmudge.strokes.Count > 0)
            {
                var pts = paintSmudge.strokes[paintSmudge.strokes.Count - 1].points;
                Vector2 p = ToCanvas(e.mousePosition);
                if (pts.Count == 0 || (p - pts[pts.Count - 1]).sqrMagnitude >= 4f)   // ~2px canvas min spacing
                    pts.Add(p);
                EditorUtility.SetDirty(spec); e.Use(); Repaint();
            }
            else if (draggingSmudge && e.type == EventType.MouseUp)
            {
                var s = paintSmudge.strokes;
                if (s.Count > 0 && s[s.Count - 1].points.Count < 2) s.RemoveAt(s.Count - 1);   // drop click-only strokes
                draggingSmudge = false; EditorUtility.SetDirty(spec); e.Use(); Repaint();
            }
        }

        // Draw every painted smudge stroke (polylines) + a brush-radius disc at the head of the most recent one so
        // the smear width is visible while authoring.
        void DrawSmudgeStroke(Rect view)
        {
            if (Event.current.type != EventType.Repaint || paintSmudge == null) return;
            var strokes = paintSmudge.strokes;
            if (strokes == null || strokes.Count == 0) return;
            Vector2 ctr = FrameRect(view).center;
            Vector2 ToScreen(Vector2 p) => new Vector2(ctr.x + p.x * zoom, ctr.y - p.y * zoom);

            Handles.BeginGUI();
            var prev = Handles.color;
            Handles.color = new Color(0.4f, 0.85f, 1f, 0.9f);
            foreach (var stroke in strokes)
            {
                var pts = stroke != null ? stroke.points : null;
                if (pts == null || pts.Count < 2) continue;
                for (int i = 1; i < pts.Count; i++)
                {
                    Vector2 a = ToScreen(pts[i - 1]), b = ToScreen(pts[i]);
                    if (view.Contains(a) || view.Contains(b)) Handles.DrawAAPolyLine(3f, a, b);
                }
            }
            var lastPts = strokes[strokes.Count - 1].points;
            if (lastPts != null && lastPts.Count > 0)
            {
                Vector2 head = ToScreen(lastPts[lastPts.Count - 1]);
                if (view.Contains(head))
                {
                    float r = Mathf.Max(1f, paintSmudge.size != null ? paintSmudge.size.staticValue : 12f) * zoom;
                    Handles.color = new Color(0.4f, 0.85f, 1f, 0.35f);
                    Handles.DrawWireDisc(new Vector3(head.x, head.y, 0f), Vector3.forward, r);
                }
            }
            Handles.color = prev;
            Handles.EndGUI();
        }

        void DrawMetaOrbMarkers(Rect view)
        {
            if (Event.current.type != EventType.Repaint || !showMetaMarkers) return;
            if (spec == null || layerSel < 0 || layerSel >= spec.layers.Count) return;
            var l = spec.layers[layerSel];
            if (l == null || l.shape != LayerShape.MetaBlob) return;

            Vector2 ctr = FrameRect(view).center;
            Handles.BeginGUI();
            var prevC = Handles.color;
            for (int i = 0; i < l.metaOrbs.Count; i++)
            {
                var o = l.metaOrbs[i];
                Vector2 mp = new Vector2(ctr.x + o.pos.x * zoom, ctr.y - o.pos.y * zoom);
                if (!view.Contains(mp)) continue;
                bool sel = metaSel == i;
                Color c = sel ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 1f, 1f, 0.75f);
                Handles.color = new Color(c.r, c.g, c.b, 0.4f);
                Handles.DrawWireDisc(new Vector3(mp.x, mp.y, 0f), Vector3.forward, o.radius * zoom);
                EditorGUI.DrawRect(new Rect(mp.x - 2f, mp.y - 2f, 4f, 4f), c);
                GUI.Label(new Rect(mp.x + 4f, mp.y - 9f, 26f, 14f), (i + 1).ToString(), EditorStyles.miniLabel);
            }
            Handles.color = prevC;
            Handles.EndGUI();
        }

        void DrawBackdropOptions()
        {
            using (Box("Preview backdrop", tooltip:
                "Renders live every repaint, purely as a visual aid for authoring — it's never baked into " +
                "any asset and has no effect on the baked sprite sheet or the runtime blast."))
            {
                // Save/Recall for the WHOLE background (this box's mode/colour/gradient/image AND the Test
                // background sprites below — one recallable unit) live here now, not down in Test background,
                // so there's one save action for everything on this side of the panel. ★ mirrors the layer
                // list's own quick-save: saves under the typed name, creating the asset the first time (seeding
                // it from whatever's on screen right now) and just re-saving in place every time after.
                using (ZUI.HRow())
                {
                    GUILayout.Label(stageBg != null ? (AssetDatabase.Contains(stageBg) ? stageBg.name : "· unsaved ·") : "· none ·",
                                    EditorStyles.miniBoldLabel);
                    ZUI.HorizontalSpace();
                    stageSaveName = EditorGUILayout.TextField(stageSaveName, GUILayout.Width(110));
                    if (Button("Recall…"))
                        PreviewStageGUI.ShowRecall(GUILayoutUtility.GetLastRect(), b =>
                        { stageBg = b; stageSel = -1; if (b != null) stageSaveName = b.name; Repaint(); });
                    if (Button("★", ZUI.Style.Default, GUILayout.Width(24))) SaveBackdrop();
                    GUILayout.FlexibleSpace();
                }

                // Mode as a vertical radio stack beside its own content, not a dropdown above it — built by
                // hand (not MiniRadioVertical, whose height is auto-measured from label text and ignores any
                // passed-in Height option) so it's EXACTLY CompactPadSize tall, matching the image combo next
                // to it pixel-for-pixel. ZUI.HorizontalSpace() between each column is the sheet-configurable
                // gap (Style Editor → "H Control Gap") — the ZUI-native way to get adjustable spacing, versus
                // packing controls with zero gap between them.
                using (ZUI.HRow())
                {
                    int modeIdx = DrawModeRadio((int)bgMode, BgModeLabels, CompactPadSize, 70f);
                    if ((PreviewBgMode)modeIdx != bgMode) bgMode = (PreviewBgMode)modeIdx;
                    ZUI.HorizontalSpace();

                    switch (bgMode)
                    {
                        case PreviewBgMode.Solid:
                            // NarrowLabel: without it, the ambient EditorGUIUtility.labelWidth (112f, set
                            // above for the LEFT panel's long dial labels) would reserve 112 of this field's
                            // 130px CompactColorWidth just for a 5-character label, leaving almost nothing for
                            // the actual swatch.
                            using (ZUI.NarrowLabel("Colour"))
                                bgSolid = EditorGUILayout.ColorField(new GUIContent("Colour"), bgSolid, true, true, false,
                                                                      GUILayout.Width(CompactColorWidth), GUILayout.Height(18f));
                            break;
                        case PreviewBgMode.Gradient:
                            bgGradient ??= DefaultBgGradient();
                            bgGradient = EditorGUILayout.GradientField(GUIContent.none, bgGradient, GUILayout.Width(CompactSliderWidth + CompactColorWidth));
                            break;
                        case PreviewBgMode.Image:
                            // Compact combo: a small thumbnail/picker beside a position drag-pad (both
                            // naturally square) with Zoom + Tint stacked next to them — replaces what used to
                            // be a big ~140px preview swatch plus three separate infinite-width rows.
                            var thumbRect = GUILayoutUtility.GetRect(CompactPadSize, CompactPadSize, GUILayout.Width(CompactPadSize), GUILayout.Height(CompactPadSize));
                            bgImage = (Texture2D)EditorGUI.ObjectField(thumbRect, bgImage, typeof(Texture2D), false);
                            ZUI.HorizontalSpace();

                            bgImagePos = ZUI.PositionPad(bgImagePos, new Rect(-200f, -200f, 400f, 400f), CompactPadSize);
                            ZUI.HorizontalSpace();

                            // Zoom shows its value inline in the bar's own label ("Zoom: 1.88") — no separate
                            // input field, so it stays compact; Tint's label sits on its own line above the
                            // swatch (rather than Unity's default label-left/swatch-right) so the swatch keeps
                            // a usable width within this narrow stacked column.
                            GUILayout.BeginVertical(GUILayout.Width(CompactSliderWidth), GUILayout.Height(CompactPadSize));
                            GUILayout.FlexibleSpace();
                            bgImageZoom = ZUI.MicroSlider(bgImageZoom, 0.1f, 8f, "Zoom", defaultValue: 1f,
                                                           options: new[] { GUILayout.Width(CompactSliderWidth), GUILayout.Height(18f) });
                            EditorGUILayout.Space(3f);
                            GUILayout.Label("Tint", EditorStyles.miniLabel);
                            bgImageTint = EditorGUILayout.ColorField(GUIContent.none, bgImageTint, true, true, false,
                                                                      GUILayout.Width(CompactSliderWidth), GUILayout.Height(18f));
                            GUILayout.FlexibleSpace();
                            GUILayout.EndVertical();
                            break;
                    }
                    GUILayout.FlexibleSpace();
                }
            }

            DrawTestBackground();
            DrawPreviewSubjectOptions();
        }

        // Saves the WHOLE background (this box's mode/colour/gradient/image + Test background's sprites) as
        // one PreviewBackground asset under `stageSaveName` — creating it (seeded from whatever's currently
        // showing via the legacy per-blast fields, so the first save doesn't visually jump) if nothing's loaded
        // yet, or just re-saving in place if stageBg is already an asset. Replaces the old separate "New" (seed)
        // + bottom "Save" (persist) two-step with the one ★ action.
        void SaveBackdrop()
        {
            if (stageBg == null)
            {
                var seeded = ScriptableObject.CreateInstance<PreviewBackground>();
                seeded.mode = ToStageMode(bgMode);
                seeded.solid = bgSolid;
                var srcGrad = bgGradient;
                var clonedGrad = new Gradient();
                if (srcGrad != null) { clonedGrad.SetKeys(srcGrad.colorKeys, srcGrad.alphaKeys); clonedGrad.mode = srcGrad.mode; }
                seeded.gradient = clonedGrad;
                seeded.image = bgImage; seeded.imageTint = bgImageTint;
                seeded.imageZoom = bgImageZoom; seeded.imagePos = bgImagePos;
                stageBg = seeded; stageSel = -1;
            }
            var bg = stageBg;
            PreviewStageGUI.Save(ref bg, stageSaveName);
            stageBg = bg;
        }

        // A vertical radio stack built from Rect-based ZUI.Toggle calls, not ZUI.MiniRadioVertical — that
        // helper auto-measures each item's height from its own label text and ignores any Height option
        // passed to it, so it can't be forced to match another control's height exactly. This can, because it
        // owns the whole Rect and splits it itself.
        static int DrawModeRadio(int selected, string[] labels, float totalHeight, float width)
        {
            Rect area = GUILayoutUtility.GetRect(width, totalHeight, GUILayout.Width(width), GUILayout.Height(totalHeight));
            float itemH = totalHeight / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rect(area.x, area.y + i * itemH, area.width, itemH);
                bool isFirst = i == 0, isLast = i == labels.Length - 1;
                var mask = isFirst && isLast ? ZUICornerMask.All
                         : isFirst          ? ZUICornerMask.Top
                         : isLast           ? ZUICornerMask.Bottom
                         :                    ZUICornerMask.Square;
                if (ZUI.Toggle(r, selected == i, labels[i], ZUI.Style.Default, null, mask) && selected != i)
                    selected = i;
            }
            return selected;
        }

        // Optional LIVE animated preview subject (see IPyrePreviewSubject/PyrePreviewSubjectProvider) — the
        // field itself is a plain Object (any asset can be dragged in) so this panel has zero dependency on
        // whatever asset TYPE actually resolves; a bridge module (e.g. Pyre.Launimator.Editor) is what makes a
        // dragged-in asset actually do anything. With no bridge loaded this panel still works for
        // authoring/persisting the fields, it just won't render/align anything. Named "Reel Preview" (not the
        // more generic "Live preview subject") because Launimator is currently the ONLY bridge that resolves
        // anything here — if a second asset type ever gets its own bridge, rename back to something generic
        // at that point; the underlying fields/interface stay fully generic either way, only this label
        // reflects what's actually usable today.
        void DrawPreviewSubjectOptions()
        {
            if (spec == null) return;
            using (Box("Reel Preview", tooltip:
                "Plays through the same real gameplay components the subject uses in-game (a real " +
                "SpriteRenderer-driven player, rendered via LiveScenePreview) — nothing here is baked. " +
                "These fields are preview-time wiring only; they aren't part of the runtime blast. " +
                "Attach id targets a MetaLayer painted on the Reel's clip."))
            {
                EditorGUI.BeginChangeCheck();
                UnityEngine.Object asset; string clip, attachId;

                // Asset/Clip/Attach share ONE row, as originally asked. The earlier split into two rows was
                // a wrong call on my part — the truncation that prompted it was actually the
                // EditorGUIUtility.labelWidth leak (see NarrowLabel below), not a real space shortage. Asset's
                // max is generous (500) rather than tightly rationed against its row-mates: Clip/Attach are
                // small, fixed-ish widths and the row ends in FlexibleSpace(), so there's no real contention
                // to protect them from — a cap only earns its keep when it's actually shielding a sibling
                // control from being squeezed, not as a reflexive "don't let anything get big" default. No
                // truncation should happen here short of a genuinely absurd asset name.
                string assetLabel = spec.previewSubjectAsset != null ? spec.previewSubjectAsset.name : "";
                using (ZUI.HRow())
                {
                    using (ZUI.NarrowLabel("Asset"))
                        asset = EditorGUILayout.ObjectField("Asset", spec.previewSubjectAsset, typeof(UnityEngine.Object), false,
                                                             GUILayout.Width(ZUI.FitWidth("Asset", assetLabel, 160f, 500f)));
                    ZUI.HorizontalSpace();

                    // Every FitWidth-sized field here is also wrapped in ZUI.NarrowLabel — otherwise Unity's
                    // AMBIENT EditorGUIUtility.labelWidth (set to 112f above, for the LEFT panel's long dial
                    // labels like "Taper (centre↔edge)") leaks into these fields too, reserving 112px for a
                    // label that only needs ~35px and eating most of FitWidth's carefully-computed content
                    // budget — exactly how "Asset" ended up rendering almost nothing but its icon and button.
                    using (ZUI.NarrowLabel("Clip"))
                        clip = EditorGUILayout.TextField("Clip", spec.previewSubjectClip,
                                                           GUILayout.Width(ZUI.FitWidth("Clip", spec.previewSubjectClip, 90f, 220f)));
                    ZUI.HorizontalSpace();

                    // Attach id is a MetaLayer name on the selected Reel/clip. ALWAYS a plain text field —
                    // never conditionally swapped for a Popup — because that field's own current value (the
                    // "options" list depends on `clip`, which changes on every keystroke while typing) would
                    // then decide which CONTROL TYPE gets drawn here. Structural GUILayout differences that
                    // hinge on a live-edited value are exactly what corrupts Unity's Layout/Repaint rect
                    // matching for the rest of the draw call — this shipped once already (the Asset field
                    // above and this row both rendered garbled/overlapping mid-edit). A small "▾" button next
                    // to the field instead opens a GenericMenu (a modal overlay, not part of the persistent
                    // layout) when options are available — same convenience, zero structural risk.
                    using (ZUI.NarrowLabel("Attach"))
                        attachId = EditorGUILayout.TextField("Attach", spec.previewSubjectAttachId,
                                                              GUILayout.Width(ZUI.FitWidth("Attach", spec.previewSubjectAttachId, 90f, 220f)));

                    var options = PyrePreviewSubjectProvider.GetAttachPointOptions?.Invoke(spec.previewSubjectAsset, spec.previewSubjectClip);
                    if (options != null && options.Length > 0 && Button("▾", ZUI.Style.Default, GUILayout.Width(20f)))
                    {
                        var menu = new GenericMenu();
                        foreach (var opt in options)
                        {
                            string captured = opt;
                            menu.AddItem(new GUIContent(captured), captured == spec.previewSubjectAttachId,
                                         () => { Undo.RecordObject(spec, "Change attach id"); spec.previewSubjectAttachId = captured; EditorUtility.SetDirty(spec); });
                        }
                        menu.ShowAsContext();
                    }
                    GUILayout.FlexibleSpace();
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(spec, "Change preview subject");
                    spec.previewSubjectAsset = asset; spec.previewSubjectClip = clip; spec.previewSubjectAttachId = attachId;
                    EditorUtility.SetDirty(spec);
                }
                if (spec.previewSubjectAsset != null && PyrePreviewSubjectProvider.Resolve == null)
                    EditorGUILayout.HelpBox("No bridge module registered to resolve this asset type (e.g. Pyre.Launimator.Editor).", MessageType.Info);
            }
        }

        // Reusable sprite test-backdrop (PreviewStage): compose the effect against props (floor/wall/…). Saved
        // separately from the frame position, and recallable across tools.
        [SerializeField] string stageSaveName = "PreviewBg";
        void DrawTestBackground()
        {
            using (Box("Test background (sprites)"))
            {
                Label("Part of the same preset as \"Preview backdrop\" above — Recall/★ up there loads or saves " +
                      "these sprites too, not just the mode/colour/gradient/image.", ZUI.ZTextStyle.Small);

                if (stageBg == null) { Label("Recall a backdrop above, or hit ★ to start one.", ZUI.ZTextStyle.Subtle); return; }

                stageBg.fill = EditorGUILayout.ColorField(new GUIContent("Fill (α0 = overlay)"), stageBg.fill, true, true, false,
                                                            GUILayout.Width(CompactSliderWidth + CompactColorWidth));

                int removeAt = -1;
                for (int i = 0; i < stageBg.sprites.Count; i++)
                {
                    var s = stageBg.sprites[i];
                    using (Box())
                    {
                        // Slim identity/select row, then one compact combo row (picker + position pad + scale
                        // & tint stacked) instead of three separate infinite-width rows — same pattern as the
                        // "Preview backdrop" Image mode above, so both read as the same visual language.
                        EditorGUILayout.BeginHorizontal();
                        bool sel = stageSel == i;
                        if (Button(sel ? "●" : "○", ZUI.Style.Default, GUILayout.Width(24))) stageSel = i;
                        s.front = GUILayout.Toggle(s.front, "Front", "Button", GUILayout.Width(48));
                        if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) removeAt = i;
                        EditorGUILayout.EndHorizontal();

                        using (ZUI.HRow())
                        {
                            var spriteRect = GUILayoutUtility.GetRect(CompactPadSize, CompactPadSize, GUILayout.Width(CompactPadSize), GUILayout.Height(CompactPadSize));
                            s.sprite = (Sprite)EditorGUI.ObjectField(spriteRect, s.sprite, typeof(Sprite), false);

                            // flipY:false — this position is ALREADY consumed with a screen-Y-down convention
                            // by PreviewStageGUI's own direct viewport dragging (position.y increases = moves
                            // DOWN); matching that existing convention matters more than the pad's own default
                            // "up feels like up" feel, since the same field edited two different-feeling ways
                            // would be far more confusing than either alone.
                            s.position = ZUI.PositionPad(s.position, new Rect(-200f, -200f, 400f, 400f), CompactPadSize, flipY: false);

                            GUILayout.BeginVertical(GUILayout.Height(CompactPadSize));
                            GUILayout.FlexibleSpace();
                            s.scale = ZUI.MicroSlider(s.scale, 0.1f, 8f, "Scale", showInputField: true, defaultValue: 1f,
                                                       options: new[] { GUILayout.Width(CompactSliderWidth), GUILayout.Height(18f) });
                            EditorGUILayout.Space(2f);
                            // GUIContent.none + a separate stacked Label (matching the backdrop image's Tint
                            // above) instead of ColorField's own inline label — sidesteps the ambient
                            // EditorGUIUtility.labelWidth entirely (a real inline label would eat most of
                            // CompactSliderWidth for a 4-character word) rather than needing a NarrowLabel
                            // scope around it.
                            GUILayout.Label("Tint", EditorStyles.miniLabel);
                            s.tint = EditorGUILayout.ColorField(GUIContent.none, s.tint, true, true, false,
                                                                 GUILayout.Width(CompactSliderWidth), GUILayout.Height(18f));
                            GUILayout.FlexibleSpace();
                            GUILayout.EndVertical();
                            GUILayout.FlexibleSpace();
                        }
                    }
                }
                if (removeAt >= 0) { stageBg.sprites.RemoveAt(removeAt); stageSel = -1; }

                EditorGUILayout.BeginHorizontal();
                if (Button("+ Add sprite")) { stageBg.sprites.Add(new StageSprite()); stageSel = stageBg.sprites.Count - 1; }
                if (Button("Clear")) { stageBg.sprites.Clear(); stageSel = -1; }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                // Marked dirty on any edit here; ★ up in "Preview backdrop" is the one save action that flushes
                // it (and everything else in the preset) to disk — no separate Save button needed down here.
                if (GUI.changed) EditorUtility.SetDirty(stageBg);
            }
        }

        void DrawBackdrop(Rect view)
        {
            switch (bgMode)
            {
                case PreviewBgMode.Solid:
                    EditorGUI.DrawRect(view, bgSolid);
                    break;
                case PreviewBgMode.Gradient:
                {
                    var g = bgGradient ?? DefaultBgGradient();
                    const int bands = 48;
                    for (int i = 0; i < bands; i++)
                    {
                        float tt = i / (float)(bands - 1);
                        var band = new Rect(view.x, view.y + view.height * i / bands, view.width, view.height / bands + 1f);
                        EditorGUI.DrawRect(band, g.Evaluate(tt));
                    }
                    break;
                }
                case PreviewBgMode.Image:
                    if (bgImage != null)
                    {
                        EditorGUI.DrawRect(view, bgSolid); // shows behind the image when zoomed out (<1)
                        var prevCol = GUI.color;
                        GUI.color = bgImageTint;
                        GUI.BeginClip(view);
                        // ScaleToFit shows the WHOLE image (aspect-preserved, letterboxed) instead of ScaleAndCrop,
                        // which filled the viewport by cropping the edges off. Zoom scales it within the viewport.
                        float w = view.width * bgImageZoom, h = view.height * bgImageZoom;
                        var imgRect = new Rect((view.width - w) * 0.5f + bgImagePos.x, (view.height - h) * 0.5f - bgImagePos.y, w, h);
                        GUI.DrawTexture(imgRect, bgImage, ScaleMode.ScaleToFit, false);
                        GUI.EndClip();
                        GUI.color = prevCol;
                    }
                    else EditorGUI.DrawRect(view, bgSolid);
                    break;
            }
        }

        void FitZoom()
        {
            if (lastView.width < 2f || spec == null) return;
            float z = Mathf.Floor(Mathf.Min(lastView.width / Mathf.Max(1, spec.Width), lastView.height / Mathf.Max(1, spec.Height)));
            zoom = Mathf.Clamp(z, 1f, 16f);
            Repaint();
        }

        void UpdatePreviewTexture(int f)
        {
            if (previewTex != null) DestroyImmediate(previewTex);
            previewTex = BlastRenderer.RenderFrameTexture(spec, f);
        }

        static Gradient DefaultBgGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.06f, 0.07f, 0.12f), 0f),
                    new GradientColorKey(new Color(0.02f, 0.02f, 0.03f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        // ── asset helpers ────────────────────────────────────────────────────────
        // ── Sprite particles: create a starter PNG + open/edit it in Aseprite (like Launimator) ──────────────
        void CreateParticleSprite(Layer l)
        {
            string specPath = spec != null ? AssetDatabase.GetAssetPath(spec) : null;
            string dir = string.IsNullOrEmpty(specPath) ? "Assets" : System.IO.Path.GetDirectoryName(specPath);
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/PyreSprite.png");

            // A tiny 8×8 starter with a 2×2 white dot so it renders before you draw anything.
            const int size = 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 0);
            for (int yy = 3; yy <= 4; yy++) for (int xx = 3; xx <= 4; xx++) px[yy * size + xx] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.isReadable = true;                 // BlastRenderer reads the pixels
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }

            l.particleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (spec != null) EditorUtility.SetDirty(spec);
            BlastRenderer.ClearSpriteCache();
            OpenInAseprite(l.particleSprite);
            Repaint();
        }

        void OpenInAseprite(Sprite s)
        {
            if (s == null) return;
            BlastRenderer.ClearSpriteCache();
            string p = System.IO.Path.GetFullPath(AssetDatabase.GetAssetPath(s));
            if (!System.IO.File.Exists(p)) return;
            string exe = ResolveAsepriteExe();
            try
            {
                var psi = exe != null
                    ? new System.Diagnostics.ProcessStartInfo(exe, $"\"{p}\"") { UseShellExecute = false }
                    : new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Pyre] could not open Aseprite ({e.Message}). Set the path via Tools ▸ Launimator ▸ Set Aseprite Path…");
            }
        }

        // Shares Launimator's saved Aseprite path (EditorPref) so it's set once for the whole library.
        static string ResolveAsepriteExe()
        {
            string p = EditorPrefs.GetString("Launimator.AsepritePath", "");
            if (System.IO.File.Exists(p)) return p;
            string[] common =
            {
                @"C:\Program Files\Aseprite\Aseprite.exe",
                @"C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe",
                @"C:\Program Files\Steam\steamapps\common\Aseprite\Aseprite.exe",
                "/Applications/Aseprite.app/Contents/MacOS/aseprite",
            };
            foreach (var c in common) if (System.IO.File.Exists(c)) return c;
            return null;
        }

    }
}
