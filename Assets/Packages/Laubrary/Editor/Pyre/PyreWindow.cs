using System.Linq;
using Laubrary.AssetKit.Editor;
using Laubrary.BackSplash.Editor;
using Laubrary.SpriteFx;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Pyre.Editor
{
    /// Pyre: author a pixel-art explosion as a flat back-to-front stack of Layers and watch it loop live.
    /// Left = a drag-resizable pane with the blast dials, a Layer list and the selected layer's inspector
    /// (animatable values via ZuiValueControl); right = a resizable preview viewport with a chooseable
    /// backdrop, zoom, and play / scrub / retime / speed transport. The preview draws the exact same
    /// BlastRenderer frames the baker and the runtime player use, so preview == bake == runtime.
    ///
    /// UI TOOLKIT PORT (ZUI → UI Toolkit migration): every control surface is Laubrary.Zui (Z.*) retained-mode
    /// controls. The ONE deliberate exception is the preview VIEWPORT, which stays IMGUI inside an
    /// IMGUIContainer — it is genuinely bespoke canvas painting (frame-texture blits, the BackSplash
    /// backdrop, LiveScenePreview, and the gizmo interaction layers) and porting it buys no control-surface
    /// benefit; behavior stays pixel-identical to the pre-migration window. Split across partials:
    ///   PyreWindow.cs           — shell, state, layout, blast settings, shared helpers
    ///   PyreWindow.Layers.cs    — layer list + selected-layer inspector
    ///   PyreWindow.Modifiers.cs — modifier stacks + per-modifier bodies
    ///   PyreWindow.Preview.cs   — the IMGUI viewport + transport + backdrop/subject panels
    public partial class PyreWindow : ZuiAssetWindow<Pyre>
    {
        [MenuItem("Laubrary/Pyre")]
        public static void Open() => GetWindow<PyreWindow>("Pyre");

        /// Open the window focused directly on a specific blast — the entry point other tools (e.g. the
        /// Zoetrope.Pyre editor bridge) use to jump straight into previewing/editing a referenced Pyre.
        public static void OpenFor(Pyre spec)
        {
            var w = GetWindow<PyreWindow>("Pyre");
            if (spec != null) w.SetAsset(spec);
        }

        Pyre spec => Current;   // the base owns the current asset; alias for the dial/preview code

        protected override string TypeLabel => "Pyre";
        protected override string NewAssetName => "New Pyre";
        protected override string DefaultFolder => "Assets/Pyre";
        protected override void OnAssetChanged() { scrub = -1; DisposePreviewSubject(); previewDirty = true; lastRenderedFrame = -1; }
        protected override void InitializeNewAsset(Pyre item) => item.AddExampleContent();
        protected override Texture2D RenderThumbnail(Pyre item)
        {
            int mid = Mathf.Clamp(item.frameCount / 2, 0, Mathf.Max(0, item.frameCount - 1));
            var tex = BlastRenderer.RenderFrameTexture(item, mid);
            tex.filterMode = FilterMode.Point;
            return tex;
        }
        protected override bool AnimateThumbnails => true;
        protected override void UpdateAnimatedThumbnail(Pyre item, Texture2D tex, double time)
        {
            if (item == null || item.frameCount <= 1) return;
            float fps = Mathf.Max(1f, item.previewFps);
            int f = Mathf.FloorToInt((float)(time * fps)) % item.frameCount;
            tex.SetPixels32(BlastRenderer.RenderFrame(item, f));
            tex.Apply();
        }

        // ── layout (serialized so it sticks — window chrome, not per-asset content) ─────────
        [SerializeField] float leftWidth = 340f;
        [SerializeField] float previewHeight = 320f;

        // ── per-asset proxies (live on the Pyre so switching assets restores its own setup) ─────
        float zoom { get => spec != null ? spec.previewZoom : 4f; set { if (spec != null) spec.previewZoom = value; } }

        bool showFrame { get => spec != null && spec.previewShowFrame; set { if (spec != null) spec.previewShowFrame = value; } }

        // The preview backdrop is a BackSplash — the shared recallable test backdrop (camera colour + one
        // zoomable image), which replaced Pyre's own PreviewStage/PreviewBackground. What lives on the spec is
        // a private, OWNED COPY of a preset's fields, never a reference to the preset asset: editing a shared
        // asset in one tool used to silently change every other tool previewing against it. Recall copies
        // values in, Save writes them out; nothing stays linked. Lazily created so specs saved before this
        // field existed still work.
        Laubrary.BackSplash.BackSplashSettings backSplash
        {
            get
            {
                if (spec == null) return null;
                spec.previewBackSplash ??= new Laubrary.BackSplash.BackSplashSettings();
                return spec.previewBackSplash;
            }
        }

        int layerSel
        {
            get => spec != null ? spec.previewLayerSel : 0;
            set { if (spec != null && spec.previewLayerSel != value) { spec.previewLayerSel = value; EditorUtility.SetDirty(spec); } }
        }

        int frame { get => spec != null ? spec.previewFrame : 0; set { if (spec != null) spec.previewFrame = value; } }
        float fps { get => spec != null ? spec.previewFps : 12f; set { if (spec != null) spec.previewFps = value; } }
        float speed { get => spec != null ? spec.previewSpeed : 1f; set { if (spec != null) spec.previewSpeed = value; } }

        // ── transient state (mirrors the pre-port window's fields) ──────────────────────────
        [SerializeField] LayerShape addShape = LayerShape.Disc;
        [SerializeField] float originMarkerAlpha = 0.95f;
        [SerializeField] Vector2 previewPan;
        [SerializeField] bool showMetaMarkers = true;

        Vector2 subjectAlignOffset;
        bool draggingOrigin, draggingPan;
        bool placeMetaMode;
        int metaSel = -1;
        // Height-balls groups that are currently folded shut (by index). Transient chrome, not asset data.
        readonly System.Collections.Generic.HashSet<int> hbGroupClosed = new System.Collections.Generic.HashSet<int>();
        bool draggingMetaOrb;
        bool draggingMetaRadius;   // dragging an orb's ring to resize it (Static radius only)
        SmudgeModifier paintSmudge;
        bool draggingSmudge;
        PinWarpModifier editPin;
        int pinSel = -1;
        bool draggingPin;
        IVortexHost editCurl;
        int vortexSel = -1;
        bool draggingVortex;

        // playback
        double lastTime;
        float acc;
        bool playing = true;
        int scrub = -1;                 // >=0 means the user is holding a scrubbed frame (paused)
        Texture2D previewTex;
        Texture2D shapePreviewTex;
        Rect lastView;

        // live preview subject machinery (unchanged from the pre-port window)
        IPyrePreviewSubject previewSubject;
        Object previewSubjectFor;
        string previewSubjectClipFor, previewSubjectAttachFor;
        Laubrary.PreviewKit.Editor.LiveScenePreview livePreview;
        Laubrary.PreviewKit.Editor.LiveScenePreview LivePreview => livePreview ??= new Laubrary.PreviewKit.Editor.LiveScenePreview();

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

        void DisposePreviewSubject()
        {
            previewSubject?.Dispose();
            previewSubject = null;
            previewSubjectFor = null;
        }

        static readonly string[] ShapeLabels = { "Disc", "Crescent", "Sparkle", "Bars", "Sprite", "Meta blob", "Height balls", "Fire", "Fireball" };
        static readonly string[] BarDecayLabels = { "Contract", "Dissolve" };
        static readonly string[] ScatterModeLabels = { "Area", "Ring", "Rosing" };
        static readonly string[] RingOrderLabels = { "Sequential", "Random" };
        static readonly string[] DissolveModeLabels = { "Erase", "Scatter" };
        static readonly string[] MaskShapeLabels = { "Disc out", "Disc in", "Swipe H", "Swipe V", "Wedge", "Noise" };
        static readonly string[] ColorModeLabels = { "Over life", "Fill", "Flow fill", "Noise fill" };
        static readonly string[] OverLifeFillLabels = { "Over life", "Fill" };
        static readonly string[] ScaleAxisLabels = { "Vertical", "Horizontal", "Both" };
        static readonly string[] CrackSpreadModeLabels = { "Uniform", "Centre out", "Edge in", "Both" };

        // ── lifecycle ───────────────────────────────────────────────────────────────────────
        protected override void OnEnable()
        {
            base.OnEnable();
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
                    if (next == 0) subject?.Restart();
                    frame = next;
                }
                subject?.Tick(dt * speed);
                RefreshTransport();
                previewContainer?.MarkDirtyRepaint();
            }
            else if (spec != null && !IsBrowsing && originMarkerAlpha > 0.001f)
                previewContainer?.MarkDirtyRepaint();   // keep the origin ✛ flashing while paused
        }

        // ── mutation helpers (the Undo contract every control routes through) ───────────────
        /// Record + apply + dirty in one step — for immediate edits (buttons, structural ops).
        void Dial(string undoLabel, System.Action apply)
        {
            Undo.RecordObject(spec, undoLabel);
            apply();
            DirtySpec();
        }

        /// onBeforeMutate hook for Z controls: record once per gesture.
        void RecordSpec() { if (spec != null) Undo.RecordObject(spec, "Edit Pyre"); }

        /// onChanged hook for Z controls: dirty + refresh the preview.
        // The preview repaints far more often than the frame advances (Tick MarkDirtyRepaints every editor
        // update; playback only advances at `fps`), and gizmos/backdrop want those repaints. But re-rendering
        // the FRAME TEXTURE on a repaint where nothing changed is pure waste — and for a Fire layer it's
        // ruinous: re-rendering the same frame isn't the sim's cheap forward step, so it replays the whole
        // history from 0. previewDirty gates the texture rebuild so it only happens on a frame change or a real
        // edit; the texture is just re-blitted on the other repaints.
        int lastRenderedFrame = -1;
        bool previewDirty = true;

        void DirtySpec()
        {
            if (spec == null) return;
            EditorUtility.SetDirty(spec);
            frame = Mathf.Min(frame, FrameCount - 1);
            previewDirty = true;
            previewContainer?.MarkDirtyRepaint();
        }

        // ── window build ────────────────────────────────────────────────────────────────────
        VisualElement leftPane, leftContent;
        IMGUIContainer previewContainer;

        protected override void OnBeforeRebuild()
        {
            leftPane = null; leftContent = null; previewContainer = null;
            scrubSlider = null; frameLabel = null; playButton = null; stageNameLabel = null;
        }

        protected override void BuildAsset(VisualElement root, Pyre asset)
        {
            ValidateArmedModifiers();
            root.style.flexGrow = 1f;

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;
            split.style.alignItems = Align.Stretch;
            root.Add(split);

            leftPane = new VisualElement();
            leftPane.style.width = leftWidth;
            leftPane.style.flexShrink = 0f;
            leftPane.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            leftContent = scroll.contentContainer;
            BuildLeftContent(leftContent);
            leftPane.Add(scroll);
            split.Add(leftPane);

            split.Add(BuildVerticalSplitter());
            split.Add(BuildRightPane());
        }

        /// Structural change (layer/modifier add/remove/reorder, shape/mode switches…) → rebuild the
        /// left pane from the data. The retained-mode answer to IMGUI's Layout-capture gymnastics.
        void RebuildLeft()
        {
            if (leftContent == null || spec == null) return;
            ValidateArmedModifiers();
            leftContent.Clear();
            BuildLeftContent(leftContent);
            previewContainer?.MarkDirtyRepaint();
        }

        void BuildLeftContent(VisualElement root)
        {
            BuildBlastSettings(root);
            root.Add(Z.VSpace());
            root.Add(Z.Text("Global modifiers", ZuiText.Section,
                "Modifiers applied to the whole composited frame, after every layer."));
            BuildModifiers(root, spec.globalModifiers, isGlobal: true);
            root.Add(Z.VSpace());
            BuildSimulationModifier(root, "Simulation (genuinely iterative, always last)",
                () => spec.simulationModifier, m => spec.simulationModifier = m);
            root.Add(Z.VSpace());
            BuildLayerList(root);
            root.Add(Z.VSpace());
            BuildSelectedLayer(root);
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
            if (spec.simulationModifier == m) return true;
            if (spec.layers != null)
                foreach (var l in spec.layers)
                    if (l != null && l.simulationModifier == m) return true;
            return false;
        }

        bool AnyStarLayer()
        {
            if (spec == null || spec.layers == null) return false;
            foreach (var l in spec.layers)
                if (l != null && l.enabled && l.shape == LayerShape.Bars && l.star) return true;
            return false;
        }

        // ── Blast settings ─────────────────────────────────────────────────────────────────
        void BuildBlastSettings(VisualElement root)
        {
            root.Add(Z.Text("Pyre", ZuiText.Section, "This Pyre's top-level canvas and timing settings."));

            if (AnyStarLayer())
                root.Add(Z.Text($"Canvas {spec.Width}×{spec.Height}  (auto-fit for star)", ZuiText.Subtle,
                    "A star Bars layer auto-fits the canvas to its arms — Width/Height are computed, not set here."));
            else
                root.Add(WrapRow(
                    Z.Field("Width", "Canvas width in pixels.",
                        Z.Int(spec.canvasSize, "Canvas width in pixels.",
                            v => Dial("Canvas width", () => spec.canvasSize = Mathf.Clamp(v, 4, 512)), 52f)),
                    Z.Field("Height", "Canvas height in pixels — 0 keeps it square (same as Width).",
                        Z.Int(spec.canvasHeight, "Canvas height in pixels — 0 keeps it square (same as Width).",
                            v => Dial("Canvas height", () => spec.canvasHeight = Mathf.Clamp(v, 0, 512)), 52f)),
                    Z.Field("PPU", "Pixels-per-unit — how large this canvas is in world units at runtime.",
                        Z.Float(spec.pixelsPerUnit, "Pixels-per-unit — how large this canvas is in world units at runtime.",
                            v => Dial("PPU", () => spec.pixelsPerUnit = Mathf.Max(1f, v)), 52f))));

            root.Add(WrapRow(
                Z.Field("Seed", "Random seed — every random choice in the bake derives from it deterministically.",
                    Z.Int(spec.seed, "Random seed — every random choice in the bake derives from it deterministically.",
                        v => Dial("Seed", () => spec.seed = v), 60f)),
                Z.Field("Frames", "How many frames the baked animation has.",
                    Z.SliderInt(spec.frameCount, 1, 64, "How many frames the baked animation has.",
                        v => Dial("Frame count", () => { spec.frameCount = Mathf.Max(1, v); frame = Mathf.Min(frame, FrameCount - 1); }), 120f)),
                Z.Field("Bake bg", "The actual pixel colour baked into every EXPORTED frame — usually fully transparent so it composites into a game scene. Different from 'Preview backdrop' below, which is cosmetic-only and never baked.",
                    Z.Color(spec.background, "The actual pixel colour baked into every EXPORTED frame — usually fully transparent so it composites into a game scene.",
                        v => Dial("Bake background", () => spec.background = v)))));

            // Origin is a full 2D control (not a bare pad) so it carries the same label/Reset/⋯/value-
            // display chrome as every other XY field — but over a PLAIN Vector2, so it never offers
            // animation (a pivot must not move over the blast's life). Its side panel hosts the
            // preview-only marker-α slider, and its Reset (default = centre) replaces the old
            // "Origin → centre" button.
            root.Add(Z.Vector2Field("Origin", () => spec.origin, v => spec.origin = v, spec,
                new ZuiValue2DControl.Options()
                    .WithRange(0f, 1f, 0f, 1f)
                    .WithDefault(new Vector2(0.5f, 0.5f))
                    .WithPlotSize(96f)
                    .Expanded()
                    .WithSidePanelExtra(() =>
                        Z.MicroSlider("α", originMarkerAlpha, 0f, 1f,
                            "Opacity of the flashing origin ✛ marker in the preview (preview-only — never baked).",
                            v => { originMarkerAlpha = v; previewContainer?.MarkDirtyRepaint(); }, 90f, showValue: false)),
                "The blast's pivot (0..1, y bottom-up) — the point a game aligns to the spawn/hit position. Also draggable as the ✛ handle in the preview.",
                DirtySpec, RecordSpec));
        }

        // ── shared row/value helpers ────────────────────────────────────────────────────────
        /// Packs controls side by side. Two layouts, chosen by content:
        ///  • If the row carries an animatable value control (ZuiValueControl / ZuiValue2DControl — the
        ///    ones that can expand into a TALL envelope), it lays out as independent masonry COLUMNS
        ///    (Z.Columns). A plain Row couples heights: expanding one control's envelope stretches its
        ///    neighbour and shoves the next row down. Columns flow separately, so an expansion only
        ///    grows its own column and never moves the control beside it.
        ///  • Otherwise (toolbars of buttons, compact int/color field rows) it stays a flowing wrap row
        ///    — the retained-mode answer to the IMGUI horizontal-scrollbar smell.
        static VisualElement WrapRow(params VisualElement[] children)
        {
            var real = children.Where(c => c != null).ToArray();
            bool hasExpandable = real.Any(c => c is ZuiValueControl || c is ZuiValue2DControl);
            if (hasExpandable && real.Length >= 2)
                return Z.Columns(2, real);
            var row = Z.Row(real);
            row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        /// One animatable-value row. Curve mode spans the layer's frame window with Y pinned to
        /// [lo, hi] (no timing/range rows) — not meaningful for a frame-baked blast.
        VisualElement ValRow(string label, string tooltip, ZUIValue v, float lo, float hi,
            float? def = null, bool allowMinMax = true, float width = 170f, bool isInt = false)
        {
            var o = new ZuiValueControl.Options
            {
                absMin = lo,
                absMax = hi,
                allowMinMax = allowMinMax,
                hideCurveTiming = true,
                hideCurveRange = true,
                hideLiveReadout = true,
                staticDefault = def,
                controlWidth = width,
                grow = true,   // fill the row's spare horizontal space instead of leaving it empty
                decimals = isInt ? 0 : -1,   // discrete values (counts) show no decimals
            };
            return Z.Value(label, v, o, tooltip, DirtySpec, RecordSpec);
        }

        /// Compact ValRow for packed (wrapping) rows.
        VisualElement PackedVal(string label, string tooltip, ZUIValue v, float lo, float hi,
            float? def = null, bool allowMinMax = true, bool isInt = false)
            => ValRow(label, tooltip, v, lo, hi, def, allowMinMax, 110f, isInt);

        /// A synchronized XY pair for packed rows.
        VisualElement PackedVal2D(string label, string tooltip, ZUIValue x, ZUIValue y,
            ZuiValue2DControl.Options opts)
            => Z.Value2D(label, x, y, opts, tooltip, DirtySpec, RecordSpec);

        /// Plain-float compact slider (for the handful of bare-float fields never promoted to ZUIValue).
        /// A MicroSlider with the LABEL and value both inside the track — matching the animatable rows'
        /// Static look — and growing to fill the row's spare width.
        VisualElement PackedSlider(string label, string tooltip, float value, float lo, float hi,
            System.Action<float> set, float width = 110f, bool isInt = false)
        {
            var ms = Z.MicroSlider(label, value, lo, hi, tooltip,
                v => Dial("Edit Pyre", () => set(v)), width, showValue: true, decimals: isInt ? 0 : -1);
            ms.style.flexGrow = 1f; ms.style.flexShrink = 1f; ms.style.maxWidth = width * 3.2f;
            return ms;
        }


        // ── splitters ───────────────────────────────────────────────────────────────────────
        VisualElement BuildVerticalSplitter()
        {
            var s = new VisualElement { tooltip = "Drag to resize the dial pane." };
            s.style.width = 6f;
            s.style.flexShrink = 0f;
            s.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            s.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { s.CapturePointer(e.pointerId); e.StopPropagation(); } });
            s.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!s.HasPointerCapture(e.pointerId)) return;
                leftWidth = Mathf.Clamp(leftWidth + e.deltaPosition.x, 240f, Mathf.Max(260f, position.width - 260f));
                if (leftPane != null) leftPane.style.width = leftWidth;
                e.StopPropagation();
            });
            s.RegisterCallback<PointerUpEvent>(e => { if (s.HasPointerCapture(e.pointerId)) s.ReleasePointer(e.pointerId); });
            return s;
        }

        VisualElement BuildPreviewSplitter()
        {
            var s = new VisualElement { tooltip = "Drag to resize the preview viewport." };
            s.style.height = 6f;
            s.style.flexShrink = 0f;
            s.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            s.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { s.CapturePointer(e.pointerId); e.StopPropagation(); } });
            s.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!s.HasPointerCapture(e.pointerId)) return;
                previewHeight = Mathf.Clamp(previewHeight + e.deltaPosition.y, 120f, 1200f);
                if (previewContainer != null) previewContainer.style.height = previewHeight;
                e.StopPropagation();
            });
            s.RegisterCallback<PointerUpEvent>(e => { if (s.HasPointerCapture(e.pointerId)) s.ReleasePointer(e.pointerId); });
            return s;
        }

        // ── asset helpers (Aseprite sprite particles — unchanged from the pre-port window) ──
        void CreateParticleSprite(Layer l)
        {
            string specPath = spec != null ? AssetDatabase.GetAssetPath(spec) : null;
            string dir = string.IsNullOrEmpty(specPath) ? "Assets" : System.IO.Path.GetDirectoryName(specPath);
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/PyreSprite.png");

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
                imp.isReadable = true;
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }

            l.particleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (spec != null) EditorUtility.SetDirty(spec);
            BlastRenderer.ClearSpriteCache();
            OpenInAseprite(l.particleSprite);
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
