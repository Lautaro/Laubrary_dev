using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using TMPro;
using Laubrary.Zui;
using Laubrary.AssetKit.Editor;
using Laubrary.BackSplash;
using Laubrary.BackSplash.Editor;
using Laubrary.PreviewKit.Editor;
using Laubrary.SpriteFx;

namespace Laubrary.TextSplash.Editor
{
    /// The TextSplash authoring window: the look, the two transitions and the per-letter behaviour on the left,
    /// the whole entrance → hold → exit playing live on a BackSplash backdrop on the right. The preview drives the
    /// SAME SplashPlayer statics the runtime does (ApplyLook / ApplyMesh / EvaluateLine over a SplashSchedule), so
    /// what is authored here is what plays — including the dilated border twin, which is a second real TMP, the
    /// per-play seed that decides the Random letter order and every Min-Max roll, and pixelation, which rasterizes
    /// the preview into a genuinely low-res buffer and runs SplashPixelRig's own CPU stage over the read-back.
    public class TextSplashWindow : ZuiAssetWindow<TextSplash>
    {
        [MenuItem("Laubrary/Text Splash")]
        public static void Open() => GetWindow<TextSplashWindow>("Text Splash");

        /// Same entry-point shape as the other tools — lets a LauAsset Edit button jump straight in.
        public static void OpenFor(TextSplash s) { var w = GetWindow<TextSplashWindow>("Text Splash"); w.SetAsset(s); }

        protected override string DefaultFolder => "Assets";
        protected override string NewAssetName => "New Splash";

        // Option labels — index order MUST match the enums in TextSplash.cs.
        static readonly string[] DirLabels = { "None", "Left", "Right", "Top", "Bottom" };
        static readonly string[] EaseLabels = { "Linear", "Smooth", "In", "Out", "In-out", "Back", "Elastic", "Bounce", "Custom" };
        static readonly string[] AxisLabels = { "X", "Y", "Z" };
        static readonly string[] OrderLabels = { "L to R", "R to L", "Centre out", "Edges in", "Random" };

        // The border atlas is a TEXTURE PAGE, so its useful sizes are the powers of two — an atlas of 1723 is
        // neither a size anyone wants nor one a slider could offer without inviting it.
        static readonly string[] AtlasLabels = { "256", "512", "1024", "2048", "4096" };
        static readonly string[] RasterLabels = { "SDF", "Coverage", "Pixel font" };
        static readonly string[] CoverageLabels = { "x2", "x4", "x8" };
        static readonly int[] CoverageFactors = { 2, 4, 8 };
        static readonly int[] AtlasSizes = { 256, 512, 1024, 2048, 4096 };

        // The control column. Wider than the old 344 because the widest rows now genuinely need it: a scalar row is
        // label + a grown ZuiValueControl (150–270) + its ⋯ + the 84px scope toggle, and a transition packs three
        // 112px MicroSliders side by side — both land at ~355px of content, plus box padding and the scrollbar.
        const float LeftWidth = 400f;

        // ── preview ─────────────────────────────────────────────────────────────────
        [SerializeField] BackSplashSettings backSplash = new BackSplashSettings();
        LiveScenePreview _preview;
        GameObject _canvasGO;        // the FACE text
        GameObject _borderGO;        // the dilated BORDER twin (created by SplashPlayer.EnsureBorderTwin)
        TextMeshPro _tmp;            // 3D TMP (mesh) — a UI-canvas TMP won't render in a PreviewRenderUtility scene
        TMP_Text _border;
        bool _borderAdopted;         // the twin has been moved into the preview scene (adopting twice errors)
        float _scrub;
        bool _playing;
        double _lastTick;
        // The current play's seed: it picks the Random letter order and every Min-Max roll, so it is re-rolled
        // when a play STARTS from the beginning and held perfectly still while paused or scrubbing.
        int _playSeed = 1;

        // Read-back of the low-res pixelation buffer — allocated only while a CPU stage (colour steps / SpriteFx)
        // actually needs one, and freed with the preview.
        Texture2D _cpuBuffer;
        RenderTexture _covBuffer;   // the Coverage mode's reduced buffer; null unless that mode is selected
        // The runtime's OWN premultiplied presentation material (Resources/SplashPremultipliedUI): the preview
        // composites its buffer through the same shader the pixel rig presents through — see Composite.
        Material _premulMat;
        bool _premulResolved;
        // A coalesced request to rebuild the border twin. The atlas dials change how it is BAKED, and a slider
        // drag fires every frame, so the request is taken a beat after the last edit instead. 0 = nothing pending.
        double _rebakeAt;
        // How much LARGER than the rect it was asked for the preview hands its texture back (it supersamples, and
        // scales again by the editor's DPI). Measured rather than assumed — see DrawScene.
        float _previewOversample = 1f;

        // Rebuildable pieces (nulled in OnBeforeRebuild — the retained-mode contract).
        VisualElement _controlsHost, _transportHost;
        IMGUIContainer _previewView;
        ZuiMicroSlider _scrubSlider;
        ZuiToggleButton _playButton;
        // The "auto = N px" read-out beside the border padding switch. Held because the number it shows is
        // DERIVED from the border width dial, so it has to be restated on a drag that never rebuilds the column.
        Label _autoPadding;

        // Preview world frame + text scale (fontSize is ~world-unit-ish; scale it into a ~10.8-tall frame).
        const float PrevW = 19.2f, PrevH = 10.8f, PrevScale = 0.05f;

        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.update += Tick;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= Tick;
            DisposePreview();
        }

        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();
            _controlsHost = null; _transportHost = null; _previewView = null;
            _scrubSlider = null; _playButton = null; _autoPadding = null;
        }

        void DisposePreview()
        {
            DestroyTextObjects();
            if (_cpuBuffer != null) { DestroyImmediate(_cpuBuffer); _cpuBuffer = null; }
            if (_covBuffer != null) { _covBuffer.Release(); DestroyImmediate(_covBuffer); _covBuffer = null; }
            if (_premulMat != null) { DestroyImmediate(_premulMat); _premulMat = null; }
            _premulResolved = false;
            _preview?.Dispose();
            _preview = null;
        }

        void DestroyTextObjects()
        {
            // The twin is a child of the face — destroy it first so nothing is left pointing at a dead object.
            DropBorderTwin();
            if (_canvasGO != null) DestroyImmediate(_canvasGO);
            _canvasGO = null; _tmp = null;
        }

        /// Throw the border twin away. EnsureBorder builds a fresh one on the next repaint, which is how a change
        /// to the atlas dials (or to the width they are derived from) reaches a twin that was already baked.
        void DropBorderTwin()
        {
            if (_borderGO != null) DestroyImmediate(_borderGO);
            _borderGO = null; _border = null; _borderAdopted = false;
        }

        /// Ask for that rebuild, coalesced: an atlas bake is not free and a slider drag would otherwise demand one
        /// every frame, so the last edit of a gesture is the only one that pays.
        void RequestBorderRebake() => _rebakeAt = EditorApplication.timeSinceStartup + 0.25;

        /// The Pixel-font mode's bake is keyed on the font, the size and the pixel size, so any of the three moving
        /// invalidates it. Shares the border's debounce: both are "the preview's fonts are stale, settle first".
        void RequestPixelFontBake() => _rebakeAt = EditorApplication.timeSinceStartup + 0.25;

        protected override void OnAssetChanged() => RebuildPreviewScene();

        void RebuildPreviewScene()
        {
            _preview ??= new LiveScenePreview();
            DestroyTextObjects();
            if (Current == null) return;
            _scrub = 0f;
            RollSeed();   // a different asset is a different play

            // A 3D TMP (mesh renderer) — NOT a UI-canvas TMP, which won't render under PreviewRenderUtility's camera.
            _canvasGO = _preview.Spawn("SplashText");
            _tmp = _canvasGO.AddComponent<TextMeshPro>();
            var rt = _tmp.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(4000f, 800f);   // generous; overflow-mode text never wraps/clips
            _canvasGO.transform.localScale = Vector3.one * PrevScale;

            SplashPlayer.ApplyLook(Current, _tmp, null, 1f, _playSeed, false);
            // Font fallback: a bare TextMeshPro with no default font asset renders nothing.
            if (Current.font == null && _tmp.font == null && TMP_Settings.defaultFontAsset != null)
                _tmp.font = TMP_Settings.defaultFontAsset;
            _tmp.ForceMeshUpdate();

            EnsureBorder();
        }

        /// A fresh seed for the next play. Re-rolled only when a play starts from the beginning, so a Min-Max
        /// scalar visibly draws a new value each time round the loop — while a paused or scrubbed frame keeps
        /// the seed it was rendered with and never changes under the pointer.
        void RollSeed() => _playSeed = UnityEngine.Random.Range(1, int.MaxValue);

        /// <summary>The border is a whole second TMP the runtime dilates behind the face, so the preview builds the
        /// SAME twin rather than faking the outline. Re-asked every frame: the twin may only come into existence
        /// once the border actually has a width, and a fresh one has to be moved into the isolated preview scene
        /// (only if it isn't already there by being parented to the face — adopting twice is an error).
        ///
        /// The face and this twin are the only two texts the preview builds. Depth's stacked copies are N MORE
        /// texts to spawn, drive and pose, so they arrive here — through the N-copy helper SplashPlayer is growing
        /// out of EnsureBorderTwin — and are driven in DrawPreview beside the border pass.</summary>
        void EnsureBorder()
        {
            if (Current == null || _tmp == null) return;
            var was = _borderGO;
            _border = SplashPlayer.EnsureBorderTwin(Current, _tmp, ref _borderGO);
            if (_borderGO != was) _borderAdopted = false;
            if (_borderGO != null && !_borderAdopted && _borderGO.transform.parent == null)
            {
                _preview.Adopt(_borderGO);
                _borderAdopted = true;
            }
            if (_border == null) return;
            if (Current.font == null && _border.font == null && TMP_Settings.defaultFontAsset != null)
                _border.font = TMP_Settings.defaultFontAsset;
        }

        void Tick()
        {
            // Ahead of the playback gate on purpose: the atlas dials are authored while the preview sits paused.
            if (_rebakeAt > 0d && EditorApplication.timeSinceStartup >= _rebakeAt)
            {
                _rebakeAt = 0d;
                DropBorderTwin();
                _previewView?.MarkDirtyRepaint();
                Repaint();
            }

            if (!_playing || Current == null) return;
            double now = EditorApplication.timeSinceStartup;
            _scrub += (float)(now - _lastTick);
            _lastTick = now;

            // Loop — and take a fresh seed with each lap, because a new lap IS a new play.
            float total = TotalLength(Current);
            if (total <= 0f) _scrub = 0f;
            else if (_scrub >= total) { _scrub = 0f; RollSeed(); }

            if (_scrubSlider != null) _scrubSlider.value = _scrub;   // the retained transport follows playback
            _previewView?.MarkDirtyRepaint();
            Repaint();
        }

        int LetterCount() => _tmp != null && _tmp.textInfo != null && _tmp.textInfo.characterCount > 0
            ? _tmp.textInfo.characterCount
            : Mathf.Max(1, Current != null && Current.text != null ? Current.text.Length : 1);

        /// The whole play's length, straight off the schedule (which already folds in the spawn stagger).
        float TotalLength(TextSplash s) => s.Schedule(LetterCount(), null, _playSeed).Total;

        // ── build ───────────────────────────────────────────────────────────────────
        protected override void BuildAsset(VisualElement root, TextSplash s)
        {
            _preview ??= new LiveScenePreview();
            if (_canvasGO == null) RebuildPreviewScene();

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.width = LeftWidth;
            left.style.flexShrink = 0f;
            _controlsHost = left.contentContainer;
            BuildControls(_controlsHost, s);
            split.Add(left);

            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.minWidth = 0f;
            _transportHost = new VisualElement();
            _transportHost.style.flexShrink = 0f;
            _transportHost.Add(BuildTransport(s));
            right.Add(_transportHost);
            _previewView = new IMGUIContainer(DrawPreview);
            _previewView.style.flexGrow = 1f;
            right.Add(_previewView);
            split.Add(right);

            root.Add(split);
        }

        /// Rebuilds ONLY the control column — for an edit that changes what OTHER tooltips in it say, so no
        /// control is left explaining a mode the splash is no longer in.
        void RebuildControls()
        {
            if (_controlsHost == null || Current == null) return;
            _controlsHost.Clear();
            BuildControls(_controlsHost, Current);
        }

        void BuildControls(VisualElement host, TextSplash s)
        {
            EnsureAuthoringData(s);

            host.Add(Z.BoxKeyed("Content", "What the splash says, in which font, at what size.", "textsplash.content",
                Z.Field("Text", "The line to splash — this asset's default; a Show call can override it per play.",
                    Z.TextInput(s.text, "The line to splash.",
                        v => Edit("Edit splash text", () => s.text = v), 260f)),
                Z.Field("Font", "TMP font asset. Blank = TMP's default font.",
                    // A cleared font can't be un-applied to a live TMP (ApplyLook only assigns a non-null one),
                    // so a font change respawns the preview text rather than leaving it on the old face.
                    Z.Object<TMP_FontAsset>(s.font, "TMP font asset. Blank = TMP's default font.",
                        v => { Edit("Edit splash font", () => s.font = v); RebuildPreviewScene(); }, 230f)),
                ScalarRow("Size", s.size, 8f, 300f,
                    "Font size in points. Animatable — curve it to punch the text bigger as it lands. Scoped per "
                    + "letter it becomes a scale about each glyph's own centre, so a letter can swell without "
                    + "re-flowing the line.", 0)));

            host.Add(Z.BoxKeyed("Face", "The text face. The border behind it has its own fill and width below.",
                "textsplash.face",
                Z.Fill("Fill", s.fill,
                    "The face fill — a solid colour or a ZUI gradient (the same fill system Pyre uses).",
                    () => { EditorUtility.SetDirty(s); AfterEdit(); },
                    () => Undo.RecordObject(s, "Edit splash fill"),
                    new ZuiFillControl.Options { controlWidth = 200f, grow = true, maxWidthFactor = 1.6f, showFit = true, subjectHalf = SubjectHalf() }),
                ScalarRow("Alpha", s.alpha, 0f, 1f,
                    "Overall opacity. Animatable — curve it for a flicker or a slow bleed-out.")));

            host.Add(BuildBorder(s));

            host.Add(BuildTransition(s, s.inTransition, entering: true));

            // Between the two ends, so the column reads in → hold → out.
            host.Add(Z.MicroSlider("Hold (s)", s.holdDuration, 0f, 6f,
                "Seconds the text sits at rest between the entrance and the exit — this asset's default; a Show "
                + "call can override it per play.",
                v => Edit("Edit hold", () => s.holdDuration = Mathf.Max(0f, v)), 200f, decimals: 2,
                prefsKey: "splash.hold"));

            host.Add(BuildTransition(s, s.outTransition, entering: false));

            host.Add(Z.Vector2Field("Anchor", () => s.anchor, v => s.anchor = v, s,
                new ZuiValue2DControl.Options()
                    .WithRange(0f, 1f, 0f, 1f)
                    .WithDefault(new Vector2(0.5f, 0.5f))
                    .WithPlotSize(96f)
                    .Expanded(),
                "Where the line rests, in viewport space — (0.5, 0.5) is dead centre. Drag the dot.",
                () => { EditorUtility.SetDirty(s); AfterEdit(); },
                () => Undo.RecordObject(s, "Edit splash anchor")));

            string orderTip = OrderTip(s);
            string exitOrderTip = ExitOrderTip(s);

            // The exit's own cascade, revealed by a display toggle rather than a rebuild — the same mechanism the
            // Custom-ease curve row uses, so flipping the switch never tears the pane out from under the click.
            var exitStagger = Z.MicroSlider("Exit stagger (s)", s.exitStagger, 0f, 0.3f, ExitStaggerTip(s),
                v => EditMode("Edit exit stagger", () => s.exitStagger = Mathf.Max(0f, v)),
                150f, decimals: 3, prefsKey: "splash.exit.stagger");
            var exitOrder = Z.Field("Exit order", exitOrderTip,
                Z.MiniRadio((int)s.exitOrder, OrderLabels, exitOrderTip,
                    v => Edit("Edit exit letter order", () => s.exitOrder = (SplashOrder)v), wrap: true).W(300f));
            exitStagger.Shown(!s.exitSameAsEntrance);
            exitOrder.Shown(!s.exitSameAsEntrance);

            host.Add(Z.BoxKeyed("Per letter", "How the line breaks into individual letters, coming in and going "
                + "out. There is no master switch: per-letter animation turns itself on as soon as a stagger "
                + "exists or any scalar above is scoped Per letter.", "textsplash.perletter",
                Z.Row(
                    Z.MicroSlider("Stagger (s)", s.spawnStagger, 0f, 0.3f, StaggerTip(s),
                        // Continuous, so this cannot rebuild on every frame of a drag — EditMode only rebuilds
                        // when the derived mode actually moves (crossing 0 is what changes the other tooltips).
                        v => EditMode("Edit spawn stagger", () => s.spawnStagger = Mathf.Max(0f, v)),
                        180f, decimals: 3, prefsKey: "splash.stagger"),
                    Z.ToggleButton("Move each letter", MotionTip(s), s.perLetterMotion,
                        v => { Edit("Toggle per-letter motion", () => s.perLetterMotion = v); RebuildControls(); })
                        .W(130f)),
                Z.Field("Order", orderTip,
                    Z.MiniRadio((int)s.order, OrderLabels, orderTip,
                        v => Edit("Edit letter order", () => s.order = (SplashOrder)v), wrap: true).W(300f)),
                Z.Row(
                    Z.Toggle("Exit same as entrance", ExitSameTip(s), s.exitSameAsEntrance, v =>
                    {
                        // Whether the exit reuses the entrance's cascade can decide whether the line animates per
                        // letter at all, so this goes through EditMode exactly like the staggers themselves.
                        EditMode("Toggle exit cascade", () => s.exitSameAsEntrance = v);
                        exitStagger.Shown(!v);
                        exitOrder.Shown(!v);
                    }),
                    exitStagger),
                exitOrder));

            host.Add(Z.BoxKeyed("Colour cycle", "Scroll the FACE fill's colour over time. The border keeps its own "
                + "fill and is never cycled.", "textsplash.cycle",
                Z.Toggle("Cycle the fill colour", "Scroll the face fill's colour over time.",
                    s.cycleFill, v => { Edit("Toggle colour cycle", () => s.cycleFill = v); RebuildControls(); }),
                Z.Row(
                    Z.MicroSlider("Speed", s.cycleSpeed, 0f, 4f, CycleSpeedTip(s),
                        v => Edit("Edit cycle speed", () => s.cycleSpeed = v), 168f, decimals: 2,
                        prefsKey: "splash.cyc.speed"),
                    Z.MicroSlider("Letter shift", s.cyclePerLetter, 0f, 1f, CycleShiftTip(s),
                        v => Edit("Edit cycle letter shift", () => s.cyclePerLetter = v), 168f, decimals: 2,
                        prefsKey: "splash.cyc.shift"))));

            host.Add(BuildBevel(s));
            host.Add(BuildDepth(s));
            host.Add(BuildPixelation(s));

            host.Add(BackSplashZui.Build(backSplash, "Preview backdrop",
                "The backdrop to audit the splash against (shared with Pyre / Mirage). Preview-only.", Repaint));
        }

        /// ONE builder for both ends, so an entrance and an exit can never drift into different layouts.
        VisualElement BuildTransition(TextSplash s, SplashTransition t, bool entering)
        {
            string title = entering ? "Entrance" : "Exit";
            string key = entering ? "textsplash.entrance" : "textsplash.exit";
            string prefs = entering ? "splash.in." : "splash.out.";
            string undo = entering ? "Edit entrance" : "Edit exit";

            string dirTip = entering
                ? "Edge the text flies IN from. None = no slide — it fades and/or scales in place."
                : "Edge the text flies OUT to. None = no slide — it fades and/or scales in place.";
            string easeTip = "Easing applied to this end's 0..1 progress. Custom swaps in the envelope below, "
                + "which you author by hand.";
            string axisTip = "Axis the spin turns about. X and Y foreshorten into a flip under the flat overlay "
                + "camera; Z spins in-plane.";

            // Only shown while Ease = Custom — a retained display toggle, so switching ease never rebuilds the pane.
            var curveRow = Z.Row(Z.Value("Curve", t.easeCurve,
                new ZuiValueControl.Options
                {
                    allowStatic = false, allowMinMax = false, allowCurve = true,
                    absMin = 0f, absMax = 1f,
                    hideCurveTiming = true,     // the shape is authored against a normalized 0..1 progress
                    hideCurveRange = true,      // ...with Y pinned to 0..1 (eased progress)
                    hideLiveReadout = true,
                    controlWidth = 200f, grow = true, maxWidthFactor = 1.6f,
                },
                "The custom easing shape: X = raw progress 0..1, Y = eased progress. Click the label to open the "
                + "envelope editor.",
                () => { EditorUtility.SetDirty(s); AfterEdit(); },
                () => Undo.RecordObject(s, undo + " ease curve")));
            curveRow.style.alignItems = Align.FlexStart;
            curveRow.Shown(t.ease == SplashEase.Custom);

            return Z.BoxKeyed(title,
                entering ? "How the text COMES IN. Every dial here is independent of the exit."
                         : "How the text GOES OUT. Every dial here is independent of the entrance.", key,

                Z.Field(entering ? "From" : "To", dirTip,
                    Z.MiniRadio((int)t.direction, DirLabels, dirTip,
                        v => Edit(undo + " direction", () => t.direction = (SplashDir)v), wrap: true).W(300f)),

                // The three short scalars of this end share one row — losing Fade and Stagger left the old
                // two-row split lopsided, and the trio reads as one "how long / how far / how big" cluster.
                Z.Row(
                    Z.MicroSlider("Seconds", t.duration, 0f, 3f, "How long this end takes.",
                        v => Edit(undo + " duration", () => t.duration = Mathf.Max(0f, v)), 112f, decimals: 2,
                        prefsKey: prefs + "dur"),
                    Z.MicroSlider("Distance", t.slideDistance, 0f, 1f, DistanceTip(t, entering),
                        v => Edit(undo + " distance", () => t.slideDistance = Mathf.Clamp01(v)), 112f, decimals: 2,
                        prefsKey: prefs + "dist"),
                    Z.MicroSlider("Scale", t.scale, 0f, 4f,
                        "Scale at the off-stage extreme: 1 = no punch, 0 = grows from nothing, 2 = shrinks in "
                        + "from double size.",
                        v => Edit(undo + " scale", () => t.scale = v), 112f, decimals: 2, prefsKey: prefs + "scale")),

                Z.Field("Ease", easeTip,
                    Z.MiniRadio((int)t.ease, EaseLabels, easeTip, v =>
                    {
                        Edit(undo + " ease", () => t.ease = (SplashEase)v);
                        curveRow.Shown(t.ease == SplashEase.Custom);
                    }, wrap: true).W(310f)),

                curveRow,

                Z.Row(
                    Z.MicroSlider("Spin °", t.spinDegrees, -1440f, 1440f, SpinTip(s),
                        v => Edit(undo + " spin", () => t.spinDegrees = v), 168f, decimals: 0,
                        prefsKey: prefs + "spin"),
                    Z.Field("Axis", axisTip,
                        Z.Segmented((int)t.spinAxis, AxisLabels, axisTip,
                            v => Edit(undo + " spin axis", () => t.spinAxis = (SplashAxis)v)))));
        }

        /// <summary>The border: its fill, its animatable width, and the two atlas dials that decide how thick a
        /// border the baked glyphs can physically carry. The dials are quality/cost, not look — which is why they
        /// sit under the width they cap rather than beside the fill.</summary>
        VisualElement BuildBorder(TextSplash s)
        {
            bool auto = s.borderAtlasPadding <= 0;
            string autoTip = AutoPaddingTip(s);
            string padTip = "Texels of padding baked into the border atlas — the hard cap on how thick a border "
                + "can get, because TMP's dilation only reaches padding / (2 x sampling size) of an em. Raise it "
                + "for a fatter border; it costs atlas area, so fewer glyphs fit on a page.";
            string atlasTip = "Resolution of the atlas the border pass is baked into. It has to hold every glyph "
                + "the splash uses at the padding beside it, and more padding means bigger cells — 1024 fits "
                + "roughly 45 glyphs at padding 45, 2048 roughly 180. Bigger costs memory; too small spills the "
                + "glyphs onto extra pages.";

            // Assigning a font swaps which atlas the border pass draws from, so the twin is dropped and rebuilt
            // exactly as an atlas dial does — and the tooltip is restated in place (empty and assigned are two
            // different stories, and one of them is a build hazard) rather than rebuilding the whole column.
            Label bakedLabel = null;
            VisualElement bakedFont = null;
            bakedFont = Z.Object<TMP_FontAsset>(s.bakedBorderFont, BakedFontTip(s), v =>
            {
                Edit("Edit baked border font", () => s.bakedBorderFont = v);
                RequestBorderRebake();
                string tip = BakedFontTip(s);
                bakedFont.tooltip = tip;
                if (bakedLabel != null) bakedLabel.tooltip = tip;
            }, 230f);
            var bakedRow = Z.Field("Baked font", BakedFontTip(s), bakedFont);
            bakedLabel = bakedRow.Q<Label>(className: "zui-field__label");

            var pad = Z.MicroSlider("Padding", Mathf.Clamp(s.borderAtlasPadding, 8, 128), 8f, 128f, padTip,
                v =>
                {
                    Edit("Edit border padding",
                        () => s.borderAtlasPadding = Mathf.Clamp(Mathf.RoundToInt(v), 8, 128));
                    RequestBorderRebake();
                },
                140f, decimals: 0, prefsKey: "splash.border.pad");
            pad.Shown(!auto);

            // The number auto arrives at, in the slider's place — "0" on a track would say nothing at all.
            _autoPadding = Z.Text(AutoPaddingText(s), ZuiText.Subtle, autoTip);
            _autoPadding.Shown(auto);

            return Z.BoxKeyed("Border", "A dilated duplicate of the text drawn behind the face — a real fill, "
                + "not TMP's thin outline, so it can go far past a hairline.", "textsplash.border",
                Z.Fill("Fill", s.borderFill,
                    "The border fill — solid or a ZUI gradient, exactly like the face fill.",
                    () => { EditorUtility.SetDirty(s); AfterEdit(); },
                    () => Undo.RecordObject(s, "Edit splash border fill"),
                    new ZuiFillControl.Options { controlWidth = 200f, grow = true, maxWidthFactor = 1.6f, showFit = true, subjectHalf = SubjectHalf() }),
                ScalarRow("Width", s.borderWidth, 0f, 0.5f,
                    "Border thickness as a FRACTION OF THE FONT SIZE — 0.02 is a hairline, 0.25 a fat cartoon "
                    + "outline. (Not TMP's old 0..1 outline units.) Animatable.", 2,
                    // Auto padding is derived from the widest border this dial can reach, so a change to it
                    // re-bakes the twin — but only while auto is the one deriving it.
                    () => { if (s.borderAtlasPadding <= 0) RequestBorderRebake(); }),
                Z.Row(
                    Z.Toggle("Auto padding", autoTip, auto, v =>
                    {
                        // Switching auto OFF hands the slider the number auto had just derived, so the dial
                        // starts where the border already was instead of jumping.
                        Edit("Toggle auto border padding",
                            () => s.borderAtlasPadding = v ? 0 : ResolvedPadding(s));
                        RequestBorderRebake();
                        RebuildControls();
                    }),
                    pad, _autoPadding),
                Z.Field("Atlas", atlasTip,
                    Z.MiniRadio(AtlasIndex(s.borderAtlasSize), AtlasLabels, atlasTip,
                        v =>
                        {
                            Edit("Edit border atlas size", () => s.borderAtlasSize = AtlasSizes[v]);
                            RequestBorderRebake();
                        }).W(240f)),
                bakedRow);
        }

        /// <summary>TMP's per-pixel bevel: one switch, and eleven dials that only take up room while it is on.
        /// Packed three to a row — stacked full width they would be a screenful on their own.</summary>
        VisualElement BuildBevel(TextSplash s)
        {
            var b = s.bevel;
            const float W = 112f;      // three of these plus their gaps fit the 400px column exactly
            string specTip = "Colour of the specular highlight.";

            var body = Z.Column(
                Z.Row(
                    Z.MicroSlider("Amount", b.amount, 0f, 1f,
                        "How pronounced the relief is — at 0 the letters read flat however the rest is dialled.",
                        v => Edit("Edit bevel amount", () => b.amount = v), W, decimals: 2,
                        prefsKey: "splash.bev.amount"),
                    Z.MicroSlider("Offset", b.offset, -0.5f, 0.5f,
                        "Push the bevel in from the glyph's edge (negative) or out past it (positive).",
                        v => Edit("Edit bevel offset", () => b.offset = v), W, decimals: 2,
                        prefsKey: "splash.bev.offset"),
                    Z.MicroSlider("Width", b.width, -0.5f, 0.5f, BevelWidthTip(s),
                        v => Edit("Edit bevel width", () => b.width = v), W, decimals: 2,
                        prefsKey: "splash.bev.width")),
                Z.Row(
                    Z.MicroSlider("Roundness", b.roundness, 0f, 1f,
                        "Round the bevel's shoulder instead of leaving it a hard chisel.",
                        v => Edit("Edit bevel roundness", () => b.roundness = v), W, decimals: 2,
                        prefsKey: "splash.bev.round"),
                    Z.MicroSlider("Clamp", b.clamp, 0f, 1f,
                        "Flatten the bevel's peak, for a plateau rather than a ridge.",
                        v => Edit("Edit bevel clamp", () => b.clamp = v), W, decimals: 2,
                        prefsKey: "splash.bev.clamp")),

                Z.Divider("Light", "How the relief is lit. The light lives in GLYPH space, so it belongs to the "
                    + "letter rather than to the scene — nothing in the splash's own motion moves it."),

                Z.Row(
                    Z.MicroSlider("Angle", b.lightAngle, 0f, 360f,
                        "Direction the light comes from, in degrees. Fixed in GLYPH space: a spun letter carries "
                        + "its shading round with it instead of catching the light from a new side.",
                        v => Edit("Edit bevel light angle", () => b.lightAngle = v), W, decimals: 0,
                        prefsKey: "splash.bev.angle"),
                    Z.MicroSlider("Diffuse", b.diffuse, 0f, 1f,
                        "Strength of the directional shading — how much darker the faces turned away from the "
                        + "light go.",
                        v => Edit("Edit bevel diffuse", () => b.diffuse = v), W, decimals: 2,
                        prefsKey: "splash.bev.diffuse"),
                    Z.MicroSlider("Ambient", b.ambient, 0f, 1f,
                        "Light reaching the faces that point away from it. Raise it to keep the dark side of the "
                        + "relief readable.",
                        v => Edit("Edit bevel ambient", () => b.ambient = v), W, decimals: 2,
                        prefsKey: "splash.bev.ambient")),
                Z.Row(
                    Z.Field("Specular", specTip,
                        Z.Color(b.specularColor, specTip,
                            v => Edit("Edit bevel specular colour", () => b.specularColor = v), 70f,
                            showAlpha: false)),
                    Z.MicroSlider("Gloss", b.specularPower, 0f, 4f,
                        "Tightness of the specular highlight (TMP's Specular Power) — higher is a smaller, "
                        + "glossier hotspot.",
                        v => Edit("Edit bevel gloss", () => b.specularPower = v), W, decimals: 2,
                        prefsKey: "splash.bev.gloss")));
            body.Shown(b.enabled);

            // The switch restates itself in place rather than rebuilding the column: nothing OUTSIDE this box
            // reads differently once the bevel is on, and the dials it reveals are its own children.
            Toggle sw = null;
            sw = Z.Toggle("Bevel the letters", BevelTip(s), b.enabled, v =>
            {
                Edit("Toggle bevel", () => b.enabled = v);
                body.Shown(v);
                sw.tooltip = BevelTip(s);
                // A bevel spends the same atlas padding the border does, so auto has to re-derive it.
                if (s.borderAtlasPadding <= 0) RequestBorderRebake();
            });

            return Z.BoxKeyed("Bevel", "Per-pixel relief lit off the glyph's own distance field — TMP's built-in "
                + "bevel. Solid, chiselled letters for no extra geometry and no extra draw call, and because the "
                + "effect lives inside the SDF it survives a per-letter spin. Its one honest limit: the light is "
                + "fixed in glyph space, so a spin foreshortens the letter while its shading stays put.",
                "textsplash.bevel", sw, body);
        }

        /// <summary>Extruded depth: the switch, how much geometry it costs, the SIDES' own fill and the two dials
        /// that shape it, and the resting tilt without which the whole thing is edge-on and invisible. The fill is
        /// a whole card rather than a one-line control, so it sits BETWEEN the two packed rows — the geometry above
        /// it, and directly beneath it the brightness/saturation that operate on nothing else.</summary>
        VisualElement BuildDepth(TextSplash s)
        {
            var d = s.depth;
            const float W = 112f;
            string sidesTip = "The SIDES' own fill — a solid colour or a gradient, exactly like the face and the "
                + "border fills. The sides are a different surface from the face and almost never want to be a "
                + "darker copy of it, so they carry a colour of their own rather than a tint of the face's.";
            string tiltTip = "Resting tilt of the line, in degrees — what makes the sides visible while the text "
                + "is still facing you. With no tilt and no spin the layers are edge-on and the depth reads as "
                + "nothing at all. Tip turns the line about X, Swing about Y.";

            var body = Z.Column(
                Z.Row(
                    Z.MicroSlider("Layers", d.layers, 2f, 32f,
                        "How many copies the extrusion is built from. More is smoother, but every copy is another "
                        + "draw — raise it only until the banding down the sides disappears.",
                        v => Edit("Edit depth layers", () => d.layers = Mathf.RoundToInt(v)), W, decimals: 0,
                        prefsKey: "splash.depth.layers"),
                    Z.MicroSlider("Distance", d.distance, 0f, 1f,
                        "How deep the extrusion goes, as a fraction of the font size.",
                        v => Edit("Edit depth distance", () => d.distance = v), W, decimals: 2,
                        prefsKey: "splash.depth.dist"),
                    // Named for what it does rather than for the field behind it: the sides can now be BRIGHTER
                    // than the face, so "darken" would describe only one of the two directions this can go.
                    Z.MicroSlider("Falloff", d.darken, 0f, 1f,
                        "How far toward the side fill the BACK of the extrusion travels — the depth falloff. The "
                        + "layers ramp evenly from the face's own colour at the front to this much of the side "
                        + "fill at the rear: 0 leaves every layer the colour of the face, so the sides read flat, "
                        + "and 1 takes the rearmost layer fully to the side fill.",
                        v => Edit("Edit depth falloff", () => d.darken = v), W, decimals: 2,
                        prefsKey: "splash.depth.darken")),

                Z.Fill("Sides", d.sideFill, sidesTip,
                    () => { EditorUtility.SetDirty(s); AfterEdit(); },
                    () => Undo.RecordObject(s, "Edit depth side fill"),
                    new ZuiFillControl.Options { controlWidth = 200f, grow = true, maxWidthFactor = 1.6f, showFit = true, subjectHalf = SubjectHalf() }),

                // Both grade the fill above rather than replacing it, so they live under its card.
                Z.Row(
                    Z.MicroSlider("Brightness", d.sideBrightness, 0f, 2f,
                        "Brightness of the sides, applied on top of their fill. Below 1 reads as the sides "
                        + "falling into shadow, above 1 as light catching them — a relight without re-picking "
                        + "the fill itself.",
                        v => Edit("Edit side brightness", () => d.sideBrightness = v), 168f, decimals: 2,
                        prefsKey: "splash.depth.bright"),
                    Z.MicroSlider("Saturation", d.sideSaturation, 0f, 2f,
                        "Saturation of the sides, applied on top of their fill. 0 is grey, 1 leaves the fill "
                        + "alone, above 1 pushes the colour harder — the cheapest way to make the extrusion read "
                        + "as a lit solid rather than a flat shadow.",
                        v => Edit("Edit side saturation", () => d.sideSaturation = v), 168f, decimals: 2,
                        prefsKey: "splash.depth.sat")),
                // Two ROTATIONS under one label, not a 2D pad: a pad's axes would sit transposed against them —
                // a rotation about X moves the letters VERTICALLY and one about Y horizontally, so dragging the
                // dot right would tip the line up, and the control's own X/Y readouts can't be relabelled.
                Z.Field("Tilt", tiltTip, Z.Row(
                    Z.MicroSlider("Tip X", d.tilt.x, -90f, 90f,
                        "Resting tilt about X, in degrees — positive brings the TOP of the line toward you. "
                        + "Without some tilt (or a spin) the extrusion is edge-on and invisible.",
                        v => Edit("Edit depth tilt", () => d.tilt = new Vector2(v, d.tilt.y)), W, decimals: 0,
                        prefsKey: "splash.depth.tiltx"),
                    Z.MicroSlider("Swing Y", d.tilt.y, -90f, 90f,
                        "Resting tilt about Y, in degrees — swings the SIDE of the letters into view. Without "
                        + "some tilt (or a spin) the extrusion is edge-on and invisible.",
                        v => Edit("Edit depth tilt", () => d.tilt = new Vector2(d.tilt.x, v)), W, decimals: 0,
                        prefsKey: "splash.depth.tilty"))));
            body.Shown(d.enabled);

            Toggle sw = null;
            sw = Z.Toggle("Extrude the letters", DepthTip(s), d.enabled, v =>
            {
                Edit("Toggle depth", () => d.enabled = v);
                body.Shown(v);
                sw.tooltip = DepthTip(s);
            });

            return Z.BoxKeyed("Depth", "Real extruded depth from stacked copies of the text stepped away from "
                + "you — the only thing here that gives the letters a true SILHOUETTE, since TMP's glyph quads "
                + "are flat. It needs the splash canvas in camera space, which turning it on arranges: an "
                + "overlay canvas ignores z outright, so the copies would collapse into each other.",
                "textsplash.depth", sw, body);
        }

        /// <summary>Pixelation: the low-res buffer, what the colour treatments do to the fills inside it, and how its
        /// EDGES resolve. Two pairings are deliberate. The cutoff and the edge fix share a row because a cutoff above
        /// 0 leaves no half-transparent pixel for the fix to correct, so the pair has to be read together rather than
        /// found in two different rows. And the palette lock joins the two switches at the top rather than sitting
        /// beside the colour steps it makes inert — it lands directly above them, which reads the same, and those
        /// three switches together are what decide the pixel-art CHARACTER: render as pixels, step motion by pixels,
        /// paint only in the splash's own colours.</summary>
        VisualElement BuildPixelation(TextSplash s)
        {
            return BuildPixelationBox(s, s.pixelation);
        }

        /// <summary>Give a splash that has never been pixelated the settings that actually make it look pixelated.
        ///
        /// The grid alone leaves a gradient a smooth ramp and leaves TMP's antialiased edge as a whole pixel of
        /// mush, so switching Pixelate on with `colorSteps = 0` and `alphaCutoff = 0` produces a low-resolution
        /// photo rather than pixel art — which is exactly what it looked like. Worse, an asset authored before
        /// those two dials existed keeps 0 for them no matter what the field defaults say, because a C# default
        /// never reaches data that is already serialized. So seed them the first time pixelation is turned on,
        /// and ONLY when both are still untouched — a deliberate 0 is a real choice and must not be overwritten.</summary>
        static void SeedPixelArtDefaults(SplashPixelation px)
        {
            if (px == null) return;
            if (px.colorSteps == 0 && px.alphaCutoff <= 0f)
            {
                px.colorSteps = 4;
                px.alphaCutoff = 0.5f;
            }
        }

        VisualElement BuildPixelationBox(TextSplash s, SplashPixelation px)
        {
            // Held so the cutoff can restate it in place: a raised cutoff removes the very soft rim the fix
            // exists to correct, and a continuous drag can't rebuild the column it is being dragged in.
            Toggle fixEdges = null;
            fixEdges = Z.Toggle("Fix edges", FixEdgesTip(s), px.fixEdgeAlpha,
                v => Edit("Toggle pixel edge fix", () => px.fixEdgeAlpha = v));

            string rasterTip = RasterTip(s);
            string samplesTip = CoverageSamplesTip(s);

            var samples = Z.Field("Samples", samplesTip,
                Z.Segmented(CoverageIndex(px.coverageSamples), CoverageLabels, samplesTip,
                    v => Edit("Edit coverage samples", () => px.coverageSamples = CoverageFactors[v])).W(150f));
            samples.Shown(px.rasterMode == SplashRasterMode.AreaAverage);

            // What the chosen mode is actually going to do with THIS asset — the bake it needs, or the feature it
            // is about to switch off. A mode that silently ignored a border or a size curve would look broken.
            string note = RasterNote(s);
            var rasterNote = Z.Text(note, ZuiText.Subtle, rasterTip);
            rasterNote.Shown(!string.IsNullOrEmpty(note));

            return Z.BoxKeyed("Pixelation", "Renders the splash through a low-res buffer so it comes out in real "
                + "chunky pixels — and this preview rasterizes it the same way, so the grid, the palette lock, the "
                + "colour crunch and the SpriteFx stack all show up here.", "textsplash.pixelation",
                Z.Row(
                    Z.Toggle("Pixelate", "Render the splash into a low-res buffer and blit it back up with point "
                        + "filtering — real pixels, not a shader faking them. The preview rasterizes the same way, "
                        + "so what you see here is what plays.",
                        px.enabled,
                        v => { Edit("Toggle pixelation", () => { px.enabled = v; if (v) SeedPixelArtDefaults(px); });
                               RebuildControls(); }),
                    Z.Toggle("Snap motion", PixelTip(s, "Snap the splash's motion to the pixel grid too, so it "
                        + "steps between pixels instead of sliding smoothly through them. Applied when the splash "
                        + "plays — the preview shows the pixels, not the stepping."),
                        px.snapMotion,
                        v => Edit("Toggle pixel snap", () => px.snapMotion = v)),
                    // Discrete, and it decides whether the Colour steps dial below is live at all — so unlike the
                    // continuous cutoff, it can afford to rebuild the column and reword that dial properly.
                    Z.Toggle("Palette lock", PaletteLockTip(s), px.paletteLock,
                        v => { Edit("Toggle palette lock", () => px.paletteLock = v); RebuildControls(); })),
                // The mode decides what every dial below it is even working on, so it sits directly under the
                // switches rather than at the bottom with the settings it governs.
                Z.Row(
                    Z.Field("Raster", rasterTip,
                        Z.MiniRadio((int)px.rasterMode, RasterLabels, rasterTip,
                            v =>
                            {
                                Edit("Edit raster mode", () => px.rasterMode = (SplashRasterMode)v);
                                RequestPixelFontBake();
                                RebuildControls();
                            }).W(240f)),
                    samples),
                rasterNote,
                Z.Row(
                    Z.MicroSlider("Pixel size", px.pixelSize, 1f, 32f, PixelTip(s,
                            "Screen pixels per splash pixel — 1 is native, 8 is very chunky. The preview "
                            + "rasterizes at the same ratio, so the chunk you see is the chunk you author."),
                        v =>
                        {
                            Edit("Edit pixel size", () => px.pixelSize = Mathf.RoundToInt(v));
                            // The pixel font is baked at fontSize/pixelSize, so this dial moves the bake.
                            RequestPixelFontBake();
                        },
                        168f, decimals: 0, prefsKey: "splash.px.size"),
                    Z.MicroSlider("Colour steps", px.colorSteps, 0f, 32f, ColourStepsTip(s),
                        v => Edit("Edit colour steps", () => px.colorSteps = Mathf.RoundToInt(v)),
                        168f, decimals: 0, prefsKey: "splash.px.steps")),
                Z.Row(
                    Z.MicroSlider("Alpha cutoff", px.alphaCutoff, 0f, 1f, PixelTip(s,
                            "Alpha threshold that makes the edges CRISP. TMP's SDF antialiases every edge, and at "
                            + "this resolution those half-transparent pixels are a whole visible pixel of mush — "
                            + "the softness around a pixelated letter. Any pixel at least this opaque snaps to "
                            + "fully opaque and everything below it disappears, so every pixel is either on or "
                            + "off, exactly like real pixel art. 0 = off, keeping the antialiasing."),
                        v =>
                        {
                            Edit("Edit alpha cutoff", () => px.alphaCutoff = v);
                            // Crossing 0 changes whether the edge fix beside it has anything left to fix.
                            fixEdges.tooltip = FixEdgesTip(s);
                        },
                        168f, decimals: 2, prefsKey: "splash.px.cutoff"),
                    fixEdges),
                Z.Field("SpriteFx", PixelTip(s, "An optional SpriteFx stack run over the low-res buffer every "
                        + "frame — the same modifiers a sprite uses. It costs a read-back of the buffer, which is "
                        + "affordable only because the buffer is small."),
                    Z.Object<SpriteFxSpec>(px.spriteFx, "SpriteFx stack asset.",
                        v => Edit("Edit splash SpriteFx", () => px.spriteFx = v), 230f)));
        }

        /// One animatable scalar (a ZUI MultCont) plus the ONE thing a splash adds to it: whose life its curve reads.
        /// `onEdited` runs after an edit for a caller that needs more than a repaint (the border width re-derives
        /// the atlas padding).
        VisualElement ScalarRow(string label, SplashScalar sc, float lo, float hi, string tooltip,
            int decimals = -1, Action onEdited = null)
        {
            var s = Current;
            var value = Z.Value(label, sc.value,
                new ZuiValueControl.Options
                {
                    absMin = lo, absMax = hi,
                    hideCurveTiming = true,   // curves here are authored against a normalized 0..1 life, never seconds
                    hideCurveRange = true,    // ...and their Y range is pinned to this field's own range
                    hideLiveReadout = true,   // a wall-clock readout means nothing for a life-mapped curve
                    decimals = decimals,
                    controlWidth = 150f, grow = true, maxWidthFactor = 1.8f,
                },
                tooltip,
                () => { EditorUtility.SetDirty(s); AfterEdit(); onEdited?.Invoke(); },
                () => Undo.RecordObject(s, "Edit splash " + label.ToLowerInvariant()));

            // Flipping a scope can switch per-letter animation on all by itself, which rewords other tooltips.
            var scope = Z.ToggleButton("Per letter", ScopeTip(sc), sc.perLetter,
                v =>
                {
                    Edit("Toggle " + label.ToLowerInvariant() + " scope", () => sc.perLetter = v);
                    RebuildControls();
                }).W(84f);

            var row = Z.Row(value, scope);
            row.style.alignItems = Align.FlexStart;   // an expanded curve must not drag the toggle down its middle
            return row;
        }

        // ── conditional tooltips (recomposed whenever the state they describe changes) ───

        /// The scope button works entirely on its own — turning it on IS what makes the line animate per letter.
        static string ScopeTip(SplashScalar sc) => sc != null && sc.perLetter
            ? "Scope: ON — each letter reads this against its OWN lifetime. A Curve plays out once per letter "
            + "(so a staggered line ripples), and a Min-Max rolls a fresh value for every letter, once, when it "
            + "spawns. This switch alone is enough: it also turns per-letter animation on."
            : "Scope: OFF — the whole line shares one lifetime, so every letter reads the same value at the same "
            + "moment (a Min-Max rolls one value for the whole play). Turn it on to give each letter its own "
            + "lifetime — nothing else has to be switched on first.";

        static string DistanceTip(SplashTransition t, bool entering) => (entering
            ? "Where the slide STARTS, between just-off-screen and the resting place. "
            : "Where the slide ENDS, between the resting place and just-off-screen. ")
            + "0 = starts fully outside the viewport, so none of the text shows; 1 = starts already at its "
            + "resting place, so there is no slide at all; 0.5 is half way in."
            + (t != null && t.direction == SplashDir.None
                ? " Inert right now, because the direction is None." : "");

        static string StaggerTip(TextSplash s) => s != null && s.spawnStagger > 0f
            ? "Seconds between one letter ENTERING and the next. This cascade is what gives every letter its OWN "
            + "lifetime, which is the window a per-letter curve or Min-Max runs over."
            : "Seconds between one letter ENTERING and the next. 0 = no cascade, so the letters all take their "
            + "turn at once. Raise it to ripple the line — that alone switches per-letter animation on.";

        static string MotionTip(TextSplash s)
        {
            if (s != null && (s.spawnStagger > 0f || s.EffectiveExitStagger > 0f))
                return "Forces each letter to slide / spin / scale about its own centre. A stagger already does "
                     + "that, so this changes nothing at the moment.";
            return s != null && s.perLetterMotion
                ? "ON — the letters slide, spin and scale individually even with no stagger, so they all move at "
                + "the same moment but each about its own centre."
                : "Forces per-letter MOTION when there is no stagger: every letter moves at once, but each about "
                + "its own centre instead of the line turning as one block. Only needed in that no-stagger case — "
                + "a stagger, or a scalar scoped Per letter, already animates the letters individually.";
        }

        static string OrderTip(TextSplash s) => s != null && s.spawnStagger > 0f
            ? "Which letter ENTERS first as the stagger cascades across the line. Centre out and Edges in are "
            + "symmetric, so the matching pair either side moves together; Random shuffles the turns using this "
            + "play's seed."
            : "Which letter ENTERS first as the stagger cascades across the line — inert right now, because the "
            + "stagger is 0 and there is no cascade to order.";

        /// The exit can either mirror the entrance's cascade or run one of its own — and which it is decides
        /// whether the two controls beside/below this switch mean anything at all.
        static string ExitSameTip(TextSplash s) => s != null && s.exitSameAsEntrance
            ? "ON — the exit reuses the ENTRANCE's stagger and order above, so the line leaves exactly the way it "
            + "arrived. Switch it off to give the exit a cascade of its own: arrive left-to-right, leave "
            + "centre-out, say."
            : "OFF — the exit runs the stagger beside this switch and the order below it, both independent of the "
            + "entrance. Switch it on to make the line leave exactly the way it arrived.";

        static string ExitStaggerTip(TextSplash s) => s != null && s.exitStagger > 0f
            ? "Seconds between one letter LEAVING and the next — the exit's own cascade, unrelated to the "
            + "entrance's. It lengthens the play, since the line is not gone until the last letter has left."
            : "Seconds between one letter LEAVING and the next. 0 = the whole line goes at once. Raise it to "
            + "ripple the exit — that alone is enough to switch per-letter animation on.";

        static string ExitOrderTip(TextSplash s) => s != null && s.EffectiveExitStagger > 0f
            ? "Which letter LEAVES first as the exit stagger cascades across the line — it need not match the "
            + "entrance. Centre out and Edges in are symmetric, so the matching pair either side moves together; "
            + "Random shuffles the turns using this play's seed."
            : "Which letter LEAVES first as the exit stagger cascades across the line — inert right now, because "
            + "the exit stagger is 0 and there is no cascade to order.";

        static string SpinTip(TextSplash s) => s != null && s.PerLetter
            ? "Degrees spun THROUGH over this end (0 = none). Each letter spins about its own centre."
            : "Degrees spun THROUGH over this end (0 = none). The whole line spins about its centre.";

        static string CycleSpeedTip(TextSplash s) => s != null && s.cycleFill
            ? "Fill colour cycles per second."
            : "Fill colour cycles per second — inert right now, because \"Cycle the fill colour\" is off.";

        static string CycleShiftTip(TextSplash s)
        {
            if (s == null || !s.cycleFill)
                return "Phase offset from one letter to the next — inert right now, because \"Cycle the fill "
                     + "colour\" is off.";
            return s.PerLetter
                ? "Phase offset from one letter to the next — a rainbow travelling across the line."
                : "Phase offset from one letter to the next — inert right now, because the letters are not "
                + "animating individually, so the line has one shared colour. Give it a spawn stagger (or scope "
                + "a scalar Per letter) to bring it to life.";
        }

        /// Auto is the DEFAULT and reads as a switch, so its tooltip has to carry what the number even means —
        /// and which of the two ways it is being arrived at right now.
        string AutoPaddingTip(TextSplash s)
        {
            const string what = "Padding, in texels, of the font atlas the border pass is baked at. THIS is what "
                + "caps border thickness: TMP's dilation only reaches padding / (2 x sampling size) of an em, "
                + "which on a stock font asset is about 4% — ten times short of what the Width dial offers. ";
            return s != null && s.borderAtlasPadding <= 0
                ? what + "ON — derived from the widest border the Width dial can reach, plus headroom when the "
                + "bevel is on, since a bevel spends the same budget. Switch it off to set the number yourself."
                : what + "OFF — baked at the padding beside this switch, whatever the width asks for. Switch it "
                + "on to let the width dial size it again.";
        }

        /// The padding auto arrives at — restated on every edit, because the width dial it is derived from is a
        /// continuous drag that never rebuilds the column.
        string AutoPaddingText(TextSplash s) => "auto = " + ResolvedPadding(s) + " px";

        int ResolvedPadding(TextSplash s) => s != null ? s.ResolveBorderPadding(SamplingPointSize(s)) : 45;

        /// <summary>The box a spatial fill is actually sampled over — the laid-out line's half-extents — handed to
        /// the fill control so its swatch can outline the text inside the gradient's own -1..1 domain. Falls back
        /// to a square when there is no live preview text yet, which reads as "no box named" and draws no outline
        /// rather than a wrong one.</summary>
        Vector2 SubjectHalf()
        {
            if (_tmp == null) return Vector2.zero;
            Vector2 half = SplashPlayer.TextHalfExtents(_tmp);
            return half.x > 0f && half.y > 0f ? half : Vector2.zero;
        }


        // ── raster mode ────────────────────────────────────────────────────────────────────────────────────────
        /// The offered oversample nearest the authored one, so an asset carrying an off-list value still lights up
        /// a segment instead of showing nothing selected.
        static int CoverageIndex(int samples)
        {
            int best = 0;
            for (int i = 1; i < CoverageFactors.Length; i++)
                if (Mathf.Abs(CoverageFactors[i] - samples) < Mathf.Abs(CoverageFactors[best] - samples)) best = i;
            return best;
        }

        /// The font the splash actually draws with — the asset's own, or TMP's default when it has none.
        static TMP_FontAsset SourceFont(TextSplash s)
            => s != null && s.font != null ? s.font : TMP_Settings.defaultFontAsset;

        /// Whether the size scalar is a single fixed number. A Pixel-font bake is per-size by construction, so this
        /// is the difference between a mode that is honest and one that is approximating every frame.
        static bool SizeIsStatic(TextSplash s)
            => s != null && s.size != null && s.size.value != null && s.size.value.mode == ZUIValue.Mode.Static;

        static float StaticSize(TextSplash s) => s != null && s.size != null ? s.size.Evaluate(0f, 0) : 96f;

        string RasterTip(TextSplash s)
        {
            var px = s != null ? s.pixelation : null;
            if (px == null) return "How a glyph becomes cells.";

            string body = px.rasterMode switch
            {
                SplashRasterMode.AreaAverage =>
                    "COVERAGE — the splash is rendered oversampled and each block of samples is averaged down to "
                    + "one cell, so a cell's alpha is its TRUE area coverage instead of one point sample. What "
                    + "that buys, measured, is a CLEANER buffer: at a 12-cell cap it cuts part-covered cells from "
                    + "485 to 392 and distinct colours from 183 to 149, so the cutoff and the palette lock have "
                    + "less to repair. It does NOT rescue small text — see Pixel font for that.",
                SplashRasterMode.PixelFont =>
                    "PIXEL FONT — the font itself is rasterized at the cell size and grid-fitted, so a glyph "
                    + "arrives already made of whole cells. The only mode that produces true 1-bit output: zero "
                    + "part-covered cells and a single colour. It is exact for text that is held or slid at its "
                    + "baked size, and it is the wrong choice for anything that rotates or scales down, which "
                    + "shreds the letterforms.",
                _ =>
                    "SDF — TMP's distance field sampled once per cell, then the alpha cutoff decides on or off. "
                    + "This is what every splash rendered before the choice existed. Its weakness is colour rather "
                    + "than structure: about 98% of covered cells come out partly transparent and a two-colour "
                    + "splash rasterizes into 183 distinct ones. Still the safest mode under rotation and heavy "
                    + "scaling.",
            };
            return PixelTip(s, body);
        }

        /// No "…this does nothing" branch, unlike the other conditional tips here: this control is HIDDEN unless
        /// Coverage is selected, so a tooltip describing it as inert could never be read.
        string CoverageSamplesTip(TextSplash s)
            => PixelTip(s, "How many samples across each cell get averaged — x4 means 4x4 = 16 samples per cell. "
                + "More samples measure a cell's coverage more finely and cost that many more fragments to render. "
                + "Only powers of two are offered because the reduction is a chain of exact halvings, and a factor "
                + "of 3 or 6 would break that into a resample with the very blur this mode exists to avoid.");

        /// <summary>What the chosen mode is about to do to THIS asset that the dials alone do not say — the bake it
        /// still needs, or the feature it is going to switch off. Empty when there is nothing to report, which is
        /// every case for the two modes that need no bake and disable nothing.</summary>
        string RasterNote(TextSplash s)
        {
            var px = s != null ? s.pixelation : null;
            if (px == null || !px.enabled) return string.Empty;

            if (px.rasterMode == SplashRasterMode.AreaAverage)
                return $"Rendering {px.CoverageFactor()}x oversampled, averaged down to the pixel grid — "
                     + $"{px.CoverageFactor() * px.CoverageFactor()} samples per cell.";

            if (px.rasterMode != SplashRasterMode.PixelFont) return string.Empty;

            var src = SourceFont(s);
            if (src == null) return "No font to bake from — assign one, or set TMP's default font asset.";

            int sampling = px.PixelSampling(StaticSize(s));
            var parts = new List<string> { $"Bitmap font baked at {sampling} px/em — about {sampling} cells tall." };

            if (!SizeIsStatic(s))
                parts.Add("The size scalar animates; a grid-fitted bake is per-size, so the size is held at its "
                        + "first value here. Use Coverage if the size must move.");
            if (s.WidestBorder() > 0f)
                parts.Add("The border is an SDF dilation and cannot draw from a bitmap font, so it is off in this "
                        + "mode.");
            if (s.bevel != null && s.bevel.enabled)
                parts.Add("Bevel needs a distance field and is off in this mode.");
            if (px.NeedsPixelFontBake(src, StaticSize(s)))
                parts.Add("Baking...");

            return string.Join("  ", parts);
        }

        /// The font's own sampling point size — the unit TMP measures its padding budget in. The preview's live
        /// face is asked first, since that is the font actually being drawn; 90 is TMP's own stock value.
        float SamplingPointSize(TextSplash s)
        {
            var f = s != null && s.font != null ? s.font
                  : (_tmp != null && _tmp.font != null ? _tmp.font : TMP_Settings.defaultFontAsset);
            return f != null && f.faceInfo.pointSize > 0f ? f.faceInfo.pointSize : 90f;
        }

        /// The offered atlas size nearest the authored one, so an asset carrying an off-list size still lights up
        /// the closest option instead of showing nothing selected.
        static int AtlasIndex(int size)
        {
            int best = 0;
            for (int i = 1; i < AtlasSizes.Length; i++)
                if (Mathf.Abs(AtlasSizes[i] - size) < Mathf.Abs(AtlasSizes[best] - size)) best = i;
            return best;
        }

        static string BevelTip(TextSplash s) => s != null && s.bevel != null && s.bevel.enabled
            ? "ON — the letters are lit as raised, solid shapes off their own distance field. The text draws "
            + "through TMP's full Distance Field shader while this is on, because the stock font material is a "
            + "Mobile variant and every Mobile variant strips shading."
            : "Light the letters as raised, solid shapes off their own distance field — no extra geometry, no "
            + "extra draw call. Turning it on swaps the text to TMP's full Distance Field shader, since the "
            + "stock font material is a Mobile variant that has no shading to switch on.";

        /// The bevel ramp and the border both eat the font atlas's padding, so how much room a deep bevel really
        /// has depends on who is deciding that padding.
        static string BevelWidthTip(TextSplash s)
        {
            const string what = "How far in from the edge the bevel ramp runs — a wide ramp reads as a gentle "
                + "swell, a narrow one as a sharp lip. It shares the font atlas's padding budget with the "
                + "border, so a deep bevel and a fat border compete for the same room. ";
            return s != null && s.borderAtlasPadding <= 0
                ? what + "Auto padding is on, so the border box is already leaving headroom for it."
                : what + "Padding is set by hand, so a deep bevel may need that number raised.";
        }

        static string DepthTip(TextSplash s) => s != null && s.depth != null && s.depth.enabled
            ? "ON — the line is drawn as stacked copies stepping away from you, so the letters have real sides. "
            + "Every copy is another draw, and the splash canvas is moved into camera space while this is on."
            : "Stack copies of the text behind itself so the letters get real extruded depth and a true "
            + "silhouette. Every copy is a draw; turning it on also moves the splash canvas into camera space, "
            + "since an overlay canvas ignores z entirely.";

        static string PixelTip(TextSplash s, string body)
            => s != null && s.pixelation != null && s.pixelation.enabled
                ? body
                : body + " Pixelation is off, so this does nothing.";

        /// <summary>The alpha cutoff and the palette lock are the two halves of "make it read as pixel art", and the
        /// user's own words for the split are the right ones: the cutoff owns the letters' EDGE, this owns their
        /// INSIDE. So the tooltip is written against that pair, and it has to own up to the two things the lock's
        /// last word on colour costs — the colour-steps dial, and any per-pixel shading.</summary>
        static string PaletteLockTip(TextSplash s)
        {
            const string what = "Snap every pixel to the nearest colour the splash actually USES — its face fill, "
                + "its border fill and, with depth on, the sides' — instead of letting it be a blend. Alpha cutoff "
                + "fixes the EDGE of the letters; this fixes their INSIDE: a low-res pixel that straddles the white "
                + "face and the dark border rasterizes to a grey half way between the two, so a splash whose whole "
                + "palette is two colours comes out showing dozens of shades of grey. Colour steps cannot do this — "
                + "it snaps to an even numeric grid your own colours do not sit on. ";
            return PixelTip(s, s != null && s.pixelation != null && s.pixelation.paletteLock
                ? what + "ON — the letters can only be painted in the colours you picked. That makes it the LAST "
                + "word on colour, so two things give way to it: Colour steps stops running, and shading that is "
                + "not one of those fills (a bevel's lighting, the depth falloff) is snapped onto them too and "
                + "reads flatter. A gradient fill still spans its whole ramp — it is sampled, not collapsed."
                : what + "OFF — a blended pixel keeps whatever colour it happened to rasterize to.");
        }

        /// The crunch and the lock answer the same question, and the lock answers it better — so while the lock is on
        /// the crunch does not run at all, and this has to say so rather than reading as a live dial.
        static string ColourStepsTip(TextSplash s)
        {
            const string what = "Quantize each colour channel to this many steps (0 = off, and the fill keeps its "
                + "full smooth ramp). The pixel GRID alone does not make a gradient read as pixel art — the fill is "
                + "still a continuous ramp, merely sampled at low resolution. This is what bands it into flat "
                + "steps, which is what actually reads as a palette. ";
            return PixelTip(s, s != null && s.pixelation != null && s.pixelation.paletteLock
                ? what + "Inert right now, because Palette lock is snapping every pixel to one of the splash's OWN "
                + "colours instead — strictly better than an even numeric grid your colours do not sit on. Turn "
                + "the lock off to hand the colour back to this dial."
                : what);
        }

        /// The edge fix and the alpha cutoff are two answers to the same soft rim, and a cutoff answers it first —
        /// once no partly transparent pixel survives the buffer there is nothing left for the blend to get wrong.
        static string FixEdgesTip(TextSplash s)
        {
            const string what = "Correct the antialiased EDGE of the pixelated splash: TMP writes premultiplied "
                + "alpha and the default UI blend multiplies by alpha a second time, so the rim composites darker "
                + "than it should. On presents it through a premultiplied blend that fixes it; off is the plain "
                + "UI blend. ";
            return PixelTip(s, s != null && s.pixelation != null && s.pixelation.alphaCutoff > 0f
                ? what + "The cutoff beside it is already throwing every partly transparent pixel away, so there "
                + "is no soft rim left for this to get right or wrong."
                : what + "This preview composites the buffer the SAME two ways, so flip it while watching the rim "
                + "of a chunky letter and the difference shows here.");
        }

        /// <summary>Empty and assigned are two genuinely different stories — one of them is a build hazard — so
        /// this says which one is on screen rather than describing both at once.</summary>
        static string BakedFontTip(TextSplash s) => s != null && s.bakedBorderFont != null
            ? "ASSIGNED — the border pass draws its glyphs from this pre-baked asset instead of baking an atlas at "
            + "runtime, which is what makes a BUILD show the same thick border this window does. TMP nulls the "
            + "source font on a Static font asset, so a runtime bake can only find the font in the EDITOR; a built "
            + "player would silently fall back to a thin border. Clear it to go back to baking at runtime."
            : "A pre-baked large-padding font asset for the border pass, committed as a project asset. Empty = the "
            + "border atlas is baked at RUNTIME, which is fine in the editor and for a Dynamic font asset — but "
            + "TMP nulls the source font on a Static one, so that bake can only find the font in the EDITOR and a "
            + "BUILD would silently fall back to a thin border. Assign one and the border survives the build.";

        /// Old assets can deserialize without the fields this window added; the controls all dereference them, so
        /// repair a null rather than blanking the whole window on a NullReferenceException.
        static void EnsureAuthoringData(TextSplash s)
        {
            s.fill ??= new ZuiFill { color = Color.white };
            s.borderFill ??= new ZuiFill { color = Color.black };
            s.size ??= new SplashScalar(96f, 8f, 300f);
            s.alpha ??= new SplashScalar(1f, 0f, 1f);
            s.borderWidth ??= new SplashScalar(0.06f, 0f, 0.5f);
            s.inTransition ??= new SplashTransition();
            s.outTransition ??= new SplashTransition();
            s.bevel ??= new SplashBevel();
            s.depth ??= new SplashDepth();
            // Z.Fill throws on a null fill, and a depth block deserialized before the sides had one of their own
            // arrives with exactly that — so the default is restated here as well as on the field.
            s.depth.sideFill ??= new ZuiFill { color = new Color(0.35f, 0.12f, 0.05f, 1f) };
            s.pixelation ??= new SplashPixelation();
            s.size.value ??= new ZUIValue(96f);
            s.alpha.value ??= new ZUIValue(1f);
            s.borderWidth.value ??= new ZUIValue(0.06f);
            s.inTransition.easeCurve ??= SplashTransition.MakeEaseCurve();
            s.outTransition.easeCurve ??= SplashTransition.MakeEaseCurve();
        }

        // ── transport + preview ─────────────────────────────────────────────────────
        VisualElement BuildTransport(TextSplash s)
        {
            float total = Mathf.Max(0.01f, TotalLength(s));
            _scrubSlider = Z.MicroSlider("t", Mathf.Clamp(_scrub, 0f, total), 0f, total,
                "Scrub through the whole play. Scrubbing pauses it and holds the current seed, so a still frame "
                + "never re-rolls under you.",
                v => { _scrub = v; SetPlaying(false); _previewView?.MarkDirtyRepaint(); }, 220f, decimals: 2);

            _playButton = Z.ToggleButton(PlayLabel(), PlayTip(), _playing, v =>
            {
                if (v)
                {
                    // Resuming carries on from wherever the scrub sits — pausing must never rewind. A play that
                    // begins at the START (or from the very END of the sequence, which rewinds) is a NEW play, so
                    // that and only that re-rolls the seed; resuming mid-play keeps the roll it was showing.
                    if (_scrub <= 0f || _scrub >= TotalLength(s) - 1e-4f) { _scrub = 0f; RollSeed(); }
                    // Tick integrates the wall-clock gap since the last tick; without this stamp the whole
                    // paused interval would land on the first frame and the animation would jump.
                    _lastTick = EditorApplication.timeSinceStartup;
                }
                SetPlaying(v);
                _previewView?.MarkDirtyRepaint();
                Repaint();
            });

            var row = Z.Row(_playButton, _scrubSlider,
                Z.Text($"total {total:0.00}s", ZuiText.Subtle,
                    "Total play length — entrance + hold + exit, including the spawn stagger."));
            row.style.flexShrink = 0f;
            return row;
        }

        /// Flip play/pause and restate the button, WITHOUT rebuilding the transport — a rebuild would tear the
        /// scrub slider out from under a drag that is itself what paused playback.
        void SetPlaying(bool playing)
        {
            _playing = playing;
            if (_playButton == null) return;
            _playButton.value = playing;
            _playButton.text = PlayLabel();
            _playButton.tooltip = PlayTip();
        }

        string PlayLabel() => _playing ? "⏸ Pause" : "▶ Play";

        string PlayTip() => _playing
            ? "Pause. The scrub stays exactly where it is — press Play again to carry on from there."
            : "Play the whole entrance → hold → exit from where the scrub sits (it loops, taking a fresh random "
            + "seed each time round so a Min-Max scalar draws a new value per lap).";

        /// The play length moves with almost every edit (durations, hold, stagger, letter count), so the transport
        /// is rebuilt after one rather than left showing a stale total and a wrongly-scaled scrub track.
        void RefreshTransport()
        {
            if (_transportHost == null || Current == null) return;
            _transportHost.Clear();
            _transportHost.Add(BuildTransport(Current));
        }

        void DrawPreview()
        {
            var rect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type != EventType.Repaint) return;

            // Backdrop (BackSplash) — colour fill + optional image, exactly like Pyre's viewport.
            EditorGUI.DrawRect(rect, backSplash != null ? backSplash.cameraColor : new Color(0.08f, 0.08f, 0.10f));
            if (backSplash != null && backSplash.image != null && backSplash.image.texture != null)
            {
                var sp = backSplash.image;
                var tr = sp.textureRect;
                var tc = new Rect(tr.x / sp.texture.width, tr.y / sp.texture.height,
                                  tr.width / sp.texture.width, tr.height / sp.texture.height);
                var prev = GUI.color; GUI.color = backSplash.imageTint;
                GUI.DrawTextureWithTexCoords(rect, sp.texture, tc, true);
                GUI.color = prev;
            }

            if (_preview == null || _tmp == null || Current == null) return;

            EnsureBorder();

            // One schedule + one seed per frame drive BOTH passes and the pose, so face, border and transform can
            // never disagree about where in the play we are or which random roll it took.
            var sch = Current.Schedule(LetterCount(), Current.holdDuration, _playSeed);
            // Half-extents come back in the TEXT's own local units, and EvaluateLine is being asked for a pose in
            // the preview's WORLD units — so they cross the same scale the text object carries.
            var textHalf = SplashPlayer.TextHalfExtents(_tmp) * PrevScale;
            var pose = SplashPlayer.EvaluateLine(Current, sch, _scrub, PrevW, PrevH, textHalf, _playSeed);
            float life = sch.LineLife(_scrub);

            DriveText(_tmp, sch, life, pose, isBorderPass: false);
            if (_border != null) DriveText(_border, sch, life, pose, isBorderPass: true);

            // The whole-line pose: rest at the anchor, plus the slide, the scale punch and the line spin. (In
            // per-letter mode the letters carry their own motion in the mesh and this stays at rest.)
            var tr2 = _canvasGO.transform;
            tr2.position = new Vector3((Current.anchor.x - 0.5f) * PrevW + pose.offset.x,
                                       (Current.anchor.y - 0.5f) * PrevH + pose.offset.y, 0f);
            tr2.localScale = Vector3.one * (PrevScale * Mathf.Max(0.0001f, pose.scale));
            tr2.localRotation = Quaternion.AngleAxis(pose.spinDeg, AxisVec(pose.spinAxis));
            // If the twin is a sibling rather than a child, it has to be posed too.
            if (_borderGO != null && _borderGO.transform.parent != tr2)
            {
                _borderGO.transform.SetPositionAndRotation(tr2.position, tr2.rotation);
                _borderGO.transform.localScale = tr2.localScale;
            }

            _preview.Frame(Vector3.zero, PrevH);
            DrawScene(rect);
        }

        /// <summary>The scene render itself. With pixelation OFF this is a plain full-resolution render. With it ON
        /// the scene is RASTERIZED into a buffer of (viewport ÷ pixel size) and blown back up with point filtering —
        /// the same principle as the runtime rig, whose camera targets a low-res render texture — so the pixels are
        /// genuinely missing detail rather than a blur pretending to be chunky. The whole CPU half — SpriteFx, the
        /// crisping threshold, the palette lock, the colour crunch — then runs through
        /// <see cref="SplashPixelRig.ApplyCpuStage"/>: the runtime's own code, not a lookalike.
        ///
        /// EVERY size and position in the pixelated path is decided in DEVICE PIXELS, never in GUI points, because
        /// a block that is a whole number of POINTS is a fractional number of screen pixels on any scaled display
        /// and comes out ragged. That mapping is ZUI's: <c>Z.PixelFit</c> says how big a buffer to render and at
        /// what whole-pixel zoom, <c>Z.DrawPixels</c> puts it on screen snapped to a device pixel. ZuiPixel.cs
        /// carries the full account of the trap.</summary>
        void DrawScene(Rect rect)
        {
            var px = Current.pixelation;
            // What comes back is ALWAYS premultiplied (see Composite), so the plain blend is used only where the
            // runtime would use it too: a pixelated splash presented through uGUI's default UI material with the
            // edge fix switched off. That is the one case the fix exists for, and now the one case it shows in.
            bool premul = px == null || !px.enabled || px.fixEdgeAlpha;

            if (px == null || !px.enabled)
            {
                var full = _preview.Render(rect);
                if (full == null) return;
                full.filterMode = FilterMode.Bilinear;   // the preview renders above 1:1; let it resolve smoothly
                Composite(rect, full, premul);
                return;
            }

            int ps = Mathf.Clamp(px.pixelSize, 1, 32);

            // The pixel-size dial is in POINTS and the screen is in device pixels. ZuiPixel turns the one into a
            // whole number of the other and hands back the buffer that fits — including the part that is easy to
            // forget, that the rect is LOCAL to the IMGUI container and the container's own place in the window
            // is half of whether a local coordinate lands on a whole device pixel.
            var fit = Z.PixelFit(rect, _previewView, ps);

            // The preview hands back a texture LARGER than the rect it was asked for (it supersamples, and scales
            // again by the editor's DPI), which would silently halve every authored pixel — so the ask is divided
            // by the factor the LAST render actually produced. That factor is a pure function of the ask, so it
            // settles on the first repaint; a preview that does not supersample measures 1 and nothing changes.
            // The 512 cap keeps the ask inside the range where that factor is constant, so the measurement below
            // can never chase its own tail.
            float over = Mathf.Clamp(_previewOversample, 1f, 8f);
            // In Coverage mode the SCENE is rendered this many times larger and averaged back down, so the ask is
            // multiplied by it here and divided out again by the reduction below. Everything after that point sees
            // a buffer of exactly the same size the other modes produce.
            int cov = SplashPixelRig.NeedsCoverage(px) ? px.CoverageFactor() : 1;
            var lowRect = new Rect(rect.x, rect.y,
                Mathf.Clamp(fit.width / over, 1f, 512f),
                Mathf.Clamp(fit.height / over, 1f, 512f));

            var tex = _preview.Render(cov > 1
                ? new Rect(lowRect.x, lowRect.y, lowRect.width * cov, lowRect.height * cov)
                : lowRect);
            if (tex == null) return;

            if (cov > 1)
            {
                // Reduced through the RIG's own static, so the preview cannot average differently from what plays.
                tex.filterMode = FilterMode.Bilinear;   // the reduction reads it; see SplashPixelRig.Reduce
                tex = ReduceCoverage(tex, cov) ?? tex;
            }
            tex.filterMode = FilterMode.Point;      // the whole point — nearest-neighbour on the way back up
            tex.wrapMode = TextureWrapMode.Clamp;

            // A loose threshold on purpose: the utility TRUNCATES the scaled size, so demanding an exact match
            // would chase a ±1-pixel buffer for ever and the grid would crawl.
            float measured = Mathf.Clamp(tex.width / Mathf.Max(1f, lowRect.width), 1f, 8f);
            if (Mathf.Abs(measured - _previewOversample) > 0.05f)
            {
                _previewOversample = measured;
                _previewView?.MarkDirtyRepaint();
            }

            Texture draw = tex;
            // The rig's OWN predicate, CALLED rather than restated. While this was a copy of it, `alphaCutoff` was
            // missing from the copy — so the crispness dial did nothing at all in the preview while working
            // perfectly at runtime, because the read-back it needs never happened.
            if (SplashPixelRig.NeedsCpuStage(px))
                draw = ReadBackCpuStage(tex) ?? tex;

            // Placed against the buffer that ACTUALLY came back rather than the ask — which is what makes the
            // oversample above harmless: whatever size it lands at, it is centred on a whole device pixel and
            // blown up by a whole number of them. Point filtering is restated by the draw itself.
            Z.DrawPixels(fit.Place(draw), draw, premul ? PremulMaterial() : null);
        }

        /// <summary>Put a rendered buffer on screen. TMP's SDF shaders blend `One OneMinusSrcAlpha`, so everything
        /// the preview camera drew landed in the buffer with its colour ALREADY multiplied by its coverage — and
        /// `GUI.DrawTexture(..., alphaBlend: true)` is a plain `SrcAlpha OneMinusSrcAlpha`, which multiplies by
        /// alpha a second time and composites every antialiased rim dark. That is the exact double-multiply
        /// <see cref="SplashPixelation.fixEdgeAlpha"/> exists to remove, so with it on the buffer goes through the
        /// runtime's OWN premultiplied presentation shader instead and the toggle finally shows here.
        ///
        /// Interior pixels (alpha 1) come out identical either way — only the rim ever differs.</summary>
        void Composite(Rect rect, Texture tex, bool premultiplied)
        {
            var mat = premultiplied ? PremulMaterial() : null;
            // A missing shader silently becomes "don't fix it" rather than "draw nothing" — the same fallback the
            // runtime rig takes.
            if (mat == null) { GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true); return; }
            Graphics.DrawTexture(rect, tex, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0, Color.white, mat);
        }

        /// The premultiplied presentation material, resolved once per window. It loads the shader out of the
        /// package's own Resources folder — the same lookup <see cref="SplashPixelRig"/> makes, so the preview and
        /// the player cannot end up fixing the rim differently.
        Material PremulMaterial()
        {
            if (_premulResolved) return _premulMat;
            _premulResolved = true;
            var shader = Resources.Load<Shader>("SplashPremultipliedUI")
                         ?? Shader.Find("Hidden/Laubrary/TextSplash/PremultipliedUI");
            if (shader != null)
                _premulMat = new Material(shader)
                {
                    name = "[TextSplash] Preview Premultiplied",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            return _premulMat;
        }

        /// <summary>Box-average an oversampled preview render down to the buffer the other modes render directly,
        /// through <see cref="SplashPixelRig.Reduce"/> — the runtime's own reduction, not a lookalike.
        ///
        /// The runtime's chain is exact because the rig sizes both ends itself. Here the preview utility decides the
        /// size it hands back (it supersamples by the editor's DPI and truncates), so the last halving can land a
        /// texel or two off an exact factor of two and resample very slightly rather than averaging exactly. That is
        /// sub-texel on a preview and invisible; the shipped path is unaffected.</summary>
        Texture ReduceCoverage(Texture src, int factor)
        {
            if (src == null || factor <= 1) return src;
            int w = Mathf.Max(1, src.width / factor), h = Mathf.Max(1, src.height / factor);

            if (_covBuffer == null || _covBuffer.width != w || _covBuffer.height != h)
            {
                if (_covBuffer != null) { _covBuffer.Release(); DestroyImmediate(_covBuffer); }
                _covBuffer = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    name = "[TextSplash] Preview Coverage",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                _covBuffer.Create();
            }

            SplashPixelRig.Reduce(src, _covBuffer);
            return _covBuffer;
        }

        /// <summary>Pull the low-res render back to the CPU and run the SAME stage the runtime rig runs — the SpriteFx
        /// stack, the crisping threshold, then the palette lock or the palette crunch — through SplashPixelRig's
        /// public static, so the preview can never drift from what plays. Taken only when one of them is actually
        /// asked for: it costs a GPU→CPU sync, affordable purely because the buffer is tiny. The read-back texture is
        /// cached and rebuilt only when the buffer's size changes.</summary>
        Texture2D ReadBackCpuStage(Texture tex)
        {
            if (!(tex is RenderTexture rt)) return null;
            int w = rt.width, h = rt.height;

            if (_cpuBuffer == null || _cpuBuffer.width != w || _cpuBuffer.height != h)
            {
                if (_cpuBuffer != null) DestroyImmediate(_cpuBuffer);
                _cpuBuffer = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
                {
                    name = "[TextSplash] Preview Pixel Buffer",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            _cpuBuffer.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
            RenderTexture.active = prev;

            // Time and frame both come off the SCRUB, never a wall clock: a paused frame has to look identical on
            // every repaint, and the stack's dither/noise modifiers step on the frame number.
            SplashPixelRig.ApplyCpuStage(Current, _cpuBuffer, w, h, _scrub, Mathf.FloorToInt(_scrub * 60f));
            return _cpuBuffer;
        }

        /// One text pass: the static look at this life, the line alpha, then the per-letter mesh work.
        void DriveText(TMP_Text tmp, in SplashSchedule sch, float life, in SplashPose pose, bool isBorderPass)
        {
            SplashPlayer.ApplyLook(Current, tmp, null, life, _playSeed, isBorderPass);
            tmp.alpha = pose.alpha;
            tmp.ForceMeshUpdate();   // the isolated preview scene never ticks Update(); rebuild the mesh by hand
            // Vertex offsets are in the text's LOCAL units, so the frame is divided by the text's own scale.
            SplashPlayer.ApplyMesh(Current, tmp, sch, _scrub, PrevW / PrevScale, PrevH / PrevScale,
                _playSeed, isBorderPass);
        }

        static Vector3 AxisVec(SplashAxis a) => a switch
        {
            SplashAxis.X => Vector3.right,
            SplashAxis.Z => Vector3.forward,
            _ => Vector3.up,
        };

        // ── edits ───────────────────────────────────────────────────────────────────
        void Edit(string label, Action apply)
        {
            if (Current == null) return;
            Undo.RecordObject(Current, label);
            apply();
            EditorUtility.SetDirty(Current);
            AfterEdit();
        }

        /// An edit that can change the splash's DERIVED per-letter mode (TextSplash.PerLetter is computed, never
        /// authored), which is what half the tooltips in the column are written against. Rebuilds the column only
        /// when that mode actually moves: for a continuous control, rebuilding on every frame of a drag would
        /// destroy the very slider being dragged.
        void EditMode(string label, Action apply)
        {
            if (Current == null) return;
            int before = ModeKey(Current);
            Edit(label, apply);
            if (ModeKey(Current) != before) RebuildControls();
        }

        /// The derived facts the conditional tooltips branch on — including the EXIT's effective stagger, which
        /// moves both when its own slider crosses 0 and when the reuse-the-entrance switch is flipped.
        static int ModeKey(TextSplash s) => (s.PerLetter ? 1 : 0) | (s.spawnStagger > 0f ? 2 : 0)
                                          | (s.EffectiveExitStagger > 0f ? 4 : 0);

        /// What every edit path (Edit, and the Z.Value/Z.Fill onChanged hooks) does once the data has changed.
        void AfterEdit()
        {
            RefreshTransport();
            if (_autoPadding != null && Current != null) _autoPadding.text = AutoPaddingText(Current);
            _previewView?.MarkDirtyRepaint();
            Repaint();
        }
    }
}
