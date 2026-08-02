using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Choreographer.Editor
{
    /// Choreographer: author a Choreography as one reusable shape and watch N Dancers perform it in a looping
    /// 2D preview. Left = the dials (path, spread, facing, timing); right = the live stage with toggleable
    /// visualisation — full path lines, onion-skin ghosts, trails, index-coloured dots, and drop-in sample
    /// sprites. The preview samples the exact same ChoreographySampler the runtime uses, so what you tune is
    /// what plays. Drag the path control points right in the stage.
    ///
    /// PILOT of the ZUI → UI Toolkit migration (2026-07-23): first real tool on Laubrary.Zui/ZuiAssetWindow
    /// instead of IMGUI ZUI/LaubraryAssetWindow. The stage is a Painter2D custom element (the Trial-2 pattern).
    public class ChoreographerWindow : ZuiAssetWindow<Choreography>
    {
        [MenuItem("Laubrary/Choreographer")]
        public static void Open() => GetWindow<ChoreographerWindow>("Choreographer");

        /// Same entry-point shape as PyreWindow.OpenFor/MirageWindow.OpenFor — lets a LauAssetField's Edit
        /// button (e.g. Mirage's entry Choreography field) jump straight into this Choreography's own editor.
        public static void OpenFor(Choreography choreography)
        {
            var w = GetWindow<ChoreographerWindow>("Choreographer");
            if (choreography != null) w.SetAsset(choreography);
        }

        Choreography choreo => Current;   // the base owns the current asset; alias for the dial/stage code
        [SerializeField] List<Sprite> previewSprites = new();

        protected override string TypeLabel => "Choreography";
        protected override string NewAssetName => "Choreography";
        protected override string DefaultFolder => "Assets/Choreographer";

        // playback
        double lastTime;
        float previewTime;
        bool playing = true;
        float previewSpeed = 1f;
        int previewCount = 0;            // 0 = use choreo.defaultCount

        // visualisation toggles — every element is individually switchable; all off = empty stage
        bool showGrid = true, showTemplate = true, showSpread = true, showLines = true;
        bool showOnion = false, showTrails = false, showDots = true, showFacing = true;
        bool showSprites = false, showAnchors = true, showBlend = true, indexColor = true;
        int onionCount = 7;
        float trailFraction = 0.18f;
        float dotSize = 7f, spriteSize = 28f;

        // preview stand-ins for the runtime Launcher/Target transforms (shown whenever the choreo uses them)
        Vector2 launcherN = new(-0.9f, 0f), targetN = new(0.9f, 0f);

        // Per-dancer captured anchors — mirrors the runtime freeze: launcher locked at launch, target locked when
        // the dancer crosses the retarget marker. So moving the target only affects dancers not yet past the marker.
        Vector2[] capLauncherN = System.Array.Empty<Vector2>();
        Vector2[] capTargetN = System.Array.Empty<Vector2>();
        bool[] capturedL = System.Array.Empty<bool>();
        bool[] capturedT = System.Array.Empty<bool>();

        // multi-select of path points
        readonly HashSet<int> selection = new();

        const int MaxRouteDancers = 48;   // cap line/onion detail on huge N
        const int MaxDots = 500;

        // live element refs (all rebuilt with the tree — released in OnBeforeRebuild via base Rebuild)
        ChoreoStage stage;
        Label infoLabel, pointsLabel, angleFieldLabel;
        Button playButton, removePointButton;
        Slider phaseSlider;
        VisualElement selectionBoxHost, spriteListHost;

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
        }

        protected override void OnAssetChanged() { previewTime = 0f; selection.Clear(); }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            if (!playing || choreo == null || stage == null) return;
            previewTime += dt * previewSpeed;
            phaseSlider?.SetValueWithoutNotify(Phase());
            RefreshInfoLabel();
            stage.MarkDirtyRepaint();
            stage.UpdateOverlay();
        }

        int Count => Mathf.Max(1, previewCount > 0 ? previewCount : (choreo != null ? choreo.defaultCount : 1));

        float Phase()
        {
            if (choreo == null) return 0f;
            float cyc = Mathf.Max(0.0001f, choreo.CycleSeconds);
            return choreo.loop ? Mathf.Repeat(previewTime, cyc) / cyc : Mathf.Clamp01(previewTime / cyc);
        }

        ChoreoAnchors PreviewAnchors()
        {
            var an = new ChoreoAnchors();
            if (choreo.useLauncher) { an.hasLauncher = true; an.launcher = launcherN; }
            if (choreo.useTarget) { an.hasTarget = true; an.target = targetN; }
            return an;
        }

        // Per-dancer anchors using the captured (frozen) launcher/target once this dancer has passed those points.
        ChoreoAnchors PreviewAnchorsFor(int i)
        {
            var an = new ChoreoAnchors();
            if (choreo.useLauncher) { an.hasLauncher = true; an.launcher = i < capturedL.Length && capturedL[i] ? capLauncherN[i] : launcherN; }
            if (choreo.useTarget) { an.hasTarget = true; an.target = i < capturedT.Length && capturedT[i] ? capTargetN[i] : targetN; }
            return an;
        }

        // Advance the freeze state: launcher locks the instant a dancer leaves the launcher, target locks the
        // instant it crosses the retarget marker — exactly like ChoreographyPlayer, so preview == runtime.
        void UpdateCapture()
        {
            int n = Count;
            if (capLauncherN.Length != n) capLauncherN = new Vector2[n];
            if (capTargetN.Length != n) capTargetN = new Vector2[n];
            if (capturedL.Length != n) capturedL = new bool[n];
            if (capturedT.Length != n) capturedT = new bool[n];
            float phase = Phase();
            for (int i = 0; i < n; i++)
            {
                float progress = ChoreographySampler.ProgressAt(choreo, i, n, phase);
                if (progress <= 0f) capturedL[i] = false;
                else if (!capturedL[i]) { capLauncherN[i] = launcherN; capturedL[i] = true; }
                if (progress < choreo.releaseAt) capturedT[i] = false;
                else if (!capturedT[i]) { capTargetN[i] = targetN; capturedT[i] = true; }
            }
        }

        // ── mutation helpers ──────────────────────────────────────────────────
        /// Every dial edit goes through here: undoable, dirties the asset, refreshes the stage.
        void Dial(string undoLabel, System.Action apply)
        {
            Undo.RecordObject(choreo, undoLabel);
            apply();
            EditorUtility.SetDirty(choreo);
            choreo.MarkPathDirty();
            RefreshInfoLabel();
            RepaintStage();
        }

        void RepaintStage() { stage?.MarkDirtyRepaint(); stage?.UpdateOverlay(); }

        void RefreshInfoLabel()
        {
            if (infoLabel != null && choreo != null)
                infoLabel.text = $"len {choreo.Cache.Length:0.00}  ·  {Count} dancers";
        }

        // Called by the stage whenever a path point is added/removed/dragged or the selection changes.
        void OnPathOrSelectionEdited()
        {
            if (pointsLabel != null) pointsLabel.text = $"Points: {choreo.pathPoints.Count}";
            removePointButton?.SetEnabled(choreo.pathPoints.Count > 2);
            RebuildSelectionBox();
            RefreshInfoLabel();
        }

        // ── window build ──────────────────────────────────────────────────────
        protected override void OnBeforeRebuild()
        {
            stage = null; infoLabel = null; pointsLabel = null; angleFieldLabel = null;
            playButton = null; removePointButton = null; phaseSlider = null;
            selectionBoxHost = null; spriteListHost = null;
        }

        protected override void BuildAsset(VisualElement root, Choreography asset)
        {
            root.style.flexGrow = 1f;

            infoLabel = Z.Text("", ZuiText.Small, "Cached path length and the dancer count currently previewed.");
            root.Add(infoLabel);
            RefreshInfoLabel();

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.alignItems = Align.Stretch;
            split.style.minHeight = 0f;   // flexbox: content height must not become a floor, or the column overflows the window
            root.Add(split);

            var left = new VisualElement();
            left.style.width = 320f;
            left.style.flexShrink = 0f;
            left.style.marginRight = 4f;
            left.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            BuildControls(scroll.contentContainer);
            left.Add(scroll);
            left.Add(BuildTransport());   // pinned under the list
            split.Add(left);

            stage = new ChoreoStage(this);
            split.Add(stage);
            stage.UpdateOverlay();
        }

        // ── left: the dials ───────────────────────────────────────────────────
        void BuildControls(VisualElement root)
        {
            root.Add(Z.Text("Path", ZuiText.Section, "The shape all dancers travel, authored as control points."));
            root.Add(Z.Row(
                Z.Button("Line", "Replace the path with a straight line preset.", () => SetPath(Presets.Line())),
                Z.Button("Sine", "Replace the path with a sine-wave preset.", () => SetPath(Presets.Sine())),
                Z.Button("Arc", "Replace the path with an arc preset.", () => SetPath(Presets.Arc())),
                Z.Button("S", "Replace the path with an S-curve preset.", () => SetPath(Presets.SCurve()))));
            root.Add(Z.Toggle("Smooth (spline)", "Interpolate the control points as a smooth spline instead of straight segments.",
                choreo.smooth, v => Dial("Smooth", () => choreo.smooth = v)));
            root.Add(Z.Toggle("Constant speed", "Reparameterise so dancers move at uniform speed along the whole path.",
                choreo.constantSpeed, v => Dial("Constant speed", () => choreo.constantSpeed = v)));
            pointsLabel = Z.Text($"Points: {choreo.pathPoints.Count}", ZuiText.Small, "How many control points the path has.");
            root.Add(pointsLabel);
            removePointButton = Z.Button("- point", "Remove the last control point (a path keeps at least two).", () =>
            {
                Dial("Remove path point", () => choreo.pathPoints.RemoveAt(choreo.pathPoints.Count - 1));
                OnPathOrSelectionEdited();
            });
            removePointButton.SetEnabled(choreo.pathPoints.Count > 2);
            root.Add(Z.Row(
                Z.Button("+ point", "Extend the path by one control point, continuing its last direction.", () =>
                {
                    Dial("Add path point", AddPoint);
                    OnPathOrSelectionEdited();
                }),
                removePointButton));
            root.Add(Z.Text("Drag a handle to move it · click the curve to add a point · right-click a handle to remove. " +
                "Marquee-drag empty space to select many · shift-click to add · drag any selected handle to move them together.",
                ZuiText.Subtle));

            selectionBoxHost = new VisualElement();
            root.Add(selectionBoxHost);
            RebuildSelectionBox();

            root.Add(Z.Text("Spread (start line → circle)", ZuiText.Section,
                "How the N dancers fan out around the shared path."));
            root.Add(Z.Field("Length", "Total width of the dancer fan, in path-space units.",
                Z.Slider(choreo.spreadLength, 0f, 4f, "Total width of the dancer fan, in path-space units.",
                    v => Dial("Spread length", () => choreo.spreadLength = v))));
            root.Add(Z.Field("Bend", "0 = straight start line, 1 = fully bent into a circle around the path.",
                Z.Slider(choreo.spreadBend, 0f, 1f, "0 = straight start line, 1 = fully bent into a circle around the path.",
                    v => Dial("Spread bend", () => choreo.spreadBend = v))));

            root.Add(Z.Text("Facing", ZuiText.Section, "Which way each dancer points while travelling."));
            root.Add(Z.MiniRadio((int)choreo.facing, new[] { "Fixed", "Radial" },
                "Fixed = every dancer faces the same angle. Radial = each faces along its own travel direction, plus the offset below.",
                v =>
                {
                    Dial("Facing mode", () => choreo.facing = (FacingMode)v);
                    if (angleFieldLabel != null) angleFieldLabel.text = choreo.facing == FacingMode.Radial ? "Angle offset" : "Angle";
                }));
            var angleField = Z.Field(choreo.facing == FacingMode.Radial ? "Angle offset" : "Angle",
                "Facing angle in degrees — absolute in Fixed mode, added to the travel direction in Radial mode.",
                Z.Slider(choreo.facingAngle, -180f, 180f,
                    "Facing angle in degrees — absolute in Fixed mode, added to the travel direction in Radial mode.",
                    v => Dial("Facing angle", () => choreo.facingAngle = v)));
            angleFieldLabel = angleField.Q<Label>();
            root.Add(angleField);

            root.Add(Z.Text("Population & timing", ZuiText.Section, "How many dancers, and how the cycle plays out in time."));
            root.Add(Z.Field("Default count", "Dancer count a ChoreographyPlayer uses unless told otherwise.",
                Z.SliderInt(choreo.defaultCount, 1, 200, "Dancer count a ChoreographyPlayer uses unless told otherwise.",
                    v => Dial("Default count", () => choreo.defaultCount = v))));
            root.Add(Z.Field("Duration (s)", "Seconds one full travel of the path takes.",
                Z.Slider(choreo.duration, 0.1f, 10f, "Seconds one full travel of the path takes.",
                    v => Dial("Duration", () => choreo.duration = v))));
            root.Add(Z.Field("Stagger", "0 = all dancers move together, 1 = starts spread evenly across the whole cycle.",
                Z.Slider(choreo.stagger, 0f, 1f, "0 = all dancers move together, 1 = starts spread evenly across the whole cycle.",
                    v => Dial("Stagger", () => choreo.stagger = v))));
            root.Add(Z.MiniRadio((int)choreo.direction, new[] { "Scatter", "Gather" },
                "Scatter = dancers travel outward from the path start. Gather = they run it in reverse, converging.",
                v => Dial("Direction", () => choreo.direction = (Direction)v)));
            root.Add(Z.Toggle("Loop", "Restart the cycle when it completes instead of stopping at the end.",
                choreo.loop, v => Dial("Loop", () => choreo.loop = v)));

            root.Add(Z.Text("Anchors (Launcher / Target)", ZuiText.Section,
                "Optional live transforms the path is stretched between at runtime."));
            var launchBlendField = Z.Field("Launch blend", "Fraction of the journey spent blending out from the launcher point.",
                Z.Slider(choreo.launchBlend, 0.01f, 1f, "Fraction of the journey spent blending out from the launcher point.",
                    v => Dial("Launch blend", () => choreo.launchBlend = v)));
            var releaseField = Z.Field("Release", "Progress at which each dancer commits to (freezes) its target position.",
                Z.Slider(choreo.releaseAt, 0f, 0.99f, "Progress at which each dancer commits to (freezes) its target position.",
                    v => Dial("Release", () => choreo.releaseAt = v)));
            var targetBlendField = Z.Field("Target blend", "Fraction of the journey spent blending in toward the target point.",
                Z.Slider(choreo.targetBlend, 0.01f, 1f, "Fraction of the journey spent blending in toward the target point.",
                    v => Dial("Target blend", () => choreo.targetBlend = v)));
            root.Add(Z.Toggle("Use launcher", "Anchor the path start to a live Launcher transform at runtime.",
                choreo.useLauncher, v => { Dial("Use launcher", () => choreo.useLauncher = v); launchBlendField.Shown(v); }));
            root.Add(launchBlendField.Shown(choreo.useLauncher));
            root.Add(Z.Toggle("Use target", "Anchor the path end to a live Target transform at runtime.",
                choreo.useTarget, v => { Dial("Use target", () => choreo.useTarget = v); releaseField.Shown(v); targetBlendField.Shown(v); }));
            root.Add(releaseField.Shown(choreo.useTarget));
            root.Add(targetBlendField.Shown(choreo.useTarget));

            BuildPreviewSection(root);
        }

        void BuildPreviewSection(VisualElement root)
        {
            root.Add(Z.Text("Preview — shows only what's ticked", ZuiText.Section,
                "Preview-only visualisation switches; nothing here is saved into the asset."));
            root.Add(Z.Field("Preview count", "Dancer count for THIS preview only — 0 falls back to the asset's default count.",
                Z.SliderInt(previewCount, 0, 200, "Dancer count for THIS preview only — 0 falls back to the asset's default count.",
                    v => { previewCount = v; RefreshInfoLabel(); RepaintStage(); })));

            var onionField = Z.Field("Onion frames", "How many ghosted time-steps the onion skin shows.",
                Z.SliderInt(onionCount, 2, 20, "How many ghosted time-steps the onion skin shows.",
                    v => { onionCount = v; RepaintStage(); }));
            var trailField = Z.Field("Trail length", "How far back each dancer's trail reaches, as a fraction of the journey.",
                Z.Slider(trailFraction, 0.02f, 1f, "How far back each dancer's trail reaches, as a fraction of the journey.",
                    v => { trailFraction = v; RepaintStage(); }));
            var spriteSizeField = Z.Field("Sprite size", "On-stage size of the sample sprites, in pixels.",
                Z.Slider(spriteSize, 8f, 96f, "On-stage size of the sample sprites, in pixels.",
                    v => { spriteSize = v; RepaintStage(); }));
            spriteListHost = new VisualElement();

            ZuiToggleButton Tg(string label, string tooltip, bool value, System.Action<bool> set) =>
                Z.Toggle(label, tooltip, value, v => { set(v); RepaintStage(); }).W(142f);

            root.Add(Z.Row(
                Tg("Grid", "Show the reference unit box and axes.", showGrid, v => showGrid = v),
                Tg("Path + handles", "Show the authored path template and its draggable control points.", showTemplate,
                    v => { showTemplate = v; RepaintStage(); })));
            root.Add(Z.Row(
                Tg("Spread line", "Show the dancer fan at mid-flight, where the spread is fullest.", showSpread, v => showSpread = v),
                Tg("Path lines", "Show the actual route each sampled dancer travels.", showLines, v => showLines = v)));
            root.Add(Z.Row(
                Tg("Onion-skin", "Show ghosted dancer positions at several time-steps at once.", showOnion,
                    v => { showOnion = v; onionField.Shown(v); }),
                Tg("Trails", "Show a fading trail behind each dancer.", showTrails,
                    v => { showTrails = v; trailField.Shown(v); })));
            root.Add(Z.Row(
                Tg("Dots", "Show each dancer as a coloured dot.", showDots, v => showDots = v),
                Tg("Facing ticks", "Show a small tick indicating each dancer's facing.", showFacing, v => showFacing = v)));
            root.Add(Z.Row(
                Tg("Sprites", "Show dancers as the sample sprites listed below instead of dots.", showSprites,
                    v => { showSprites = v; spriteSizeField.Shown(v); spriteListHost.Shown(v); }),
                Tg("Anchors", "Show the draggable Launcher/Target preview markers.", showAnchors, v => showAnchors = v)));
            root.Add(Z.Row(
                Tg("Blend marks", "Mark where launch fan-out completes and where target convergence begins.", showBlend, v => showBlend = v),
                Tg("Colour by index", "Tint every per-dancer element by its index instead of a flat colour.", indexColor, v => indexColor = v)));

            root.Add(onionField.Shown(showOnion));
            root.Add(trailField.Shown(showTrails));
            root.Add(spriteSizeField.Shown(showSprites));
            root.Add(spriteListHost.Shown(showSprites));
            RebuildSpriteList();

            if (choreo.useLauncher || choreo.useTarget)
                root.Add(Z.Text("The path runs launcher → target. Drag the green (launcher) / red (target) markers in the " +
                    "stage to move the whole choreo.", ZuiText.Subtle));
        }

        void RebuildSelectionBox()
        {
            if (selectionBoxHost == null) return;
            selectionBoxHost.Clear();
            if (selection.Count == 0) return;

            selectionBoxHost.Add(Z.Box($"Selection — {selection.Count} points",
                "Transforms applied to the selected path points around their shared centre.",
                Z.Row(
                    Z.Button("Rotate ⟲", "Rotate the selected points 15° counter-clockwise around their centre.", () => RotateSelection(-15f)),
                    Z.Button("Rotate ⟳", "Rotate the selected points 15° clockwise around their centre.", () => RotateSelection(15f))),
                Z.Row(
                    Z.Button("Wider", "Scale the selection 15% wider horizontally.", () => ScaleSelection(1.15f, 1f)),
                    Z.Button("Narrower", "Scale the selection 15% narrower horizontally.", () => ScaleSelection(1f / 1.15f, 1f))),
                Z.Row(
                    Z.Button("Taller", "Scale the selection 15% taller vertically.", () => ScaleSelection(1f, 1.15f)),
                    Z.Button("Shorter", "Scale the selection 15% shorter vertically.", () => ScaleSelection(1f, 1f / 1.15f))),
                Z.Row(
                    Z.Button("Flip H", "Mirror the selection horizontally around its centre.", () => ScaleSelection(-1f, 1f)),
                    Z.Button("Flip V", "Mirror the selection vertically around its centre.", () => ScaleSelection(1f, -1f))),
                Z.Row(
                    Z.Button("Select all", "Select every control point on the path.", () =>
                    {
                        selection.Clear();
                        for (int i = 0; i < choreo.pathPoints.Count; i++) selection.Add(i);
                        OnPathOrSelectionEdited(); RepaintStage();
                    }),
                    Z.Button("Clear", "Deselect all points.", () => { selection.Clear(); OnPathOrSelectionEdited(); RepaintStage(); }))));
        }

        VisualElement BuildTransport()
        {
            playButton = Z.Button(playing ? "❚❚" : "▶", "Play or pause the looping preview.", () =>
            {
                playing = !playing;
                playButton.text = playing ? "❚❚" : "▶";
            }).W(38f);
            // "⟲" has no glyph in the editor's UI Toolkit font (renders a fallback box) — use ASCII.
            phaseSlider = Z.Slider(Phase(), 0f, 1f, "Scrub the preview to an exact phase of the cycle (pauses playback).",
                v =>
                {
                    previewTime = v * Mathf.Max(0.0001f, choreo.CycleSeconds);
                    playing = false;
                    playButton.text = "▶";
                    RepaintStage();
                }, 150f);

            return Z.Box(null, null,
                Z.Row(
                    playButton,
                    Z.Button("|<", "Rewind the preview to the start of the cycle.", () =>
                    {
                        previewTime = 0f;
                        phaseSlider.SetValueWithoutNotify(0f);
                        RepaintStage();
                    }).W(32f),
                    phaseSlider),
                Z.Field("Speed", "Preview playback speed multiplier.",
                    Z.Slider(previewSpeed, 0.1f, 3f, "Preview playback speed multiplier.", v => previewSpeed = v)));
        }

        void RebuildSpriteList()
        {
            if (spriteListHost == null) return;
            spriteListHost.Clear();
            for (int i = 0; i < previewSprites.Count; i++)
            {
                int idx = i;
                spriteListHost.Add(Z.Row(
                    Z.Object<Sprite>(previewSprites[idx], "A sample sprite dancers cycle through on the stage (preview only).",
                        v => { previewSprites[idx] = v; RepaintStage(); }, 200f),
                    Z.Button("×", "Remove this sprite from the preview list.", () =>
                    {
                        previewSprites.RemoveAt(idx);
                        RebuildSpriteList(); RepaintStage();
                    }).W(24f)));
            }
            spriteListHost.Add(Z.Button("+ sprite", "Add a sample-sprite slot to the preview list.", () =>
            {
                previewSprites.Add(null);
                RebuildSpriteList();
            }));
        }

        // ── asset & path helpers ──────────────────────────────────────────────
        void SetPath(List<Vector2> pts)
        {
            Dial("Path preset", () => choreo.pathPoints = pts);
            selection.Clear();
            OnPathOrSelectionEdited();
        }

        void AddPoint()
        {
            var last = choreo.pathPoints[choreo.pathPoints.Count - 1];
            var prev = choreo.pathPoints[choreo.pathPoints.Count - 2];
            choreo.pathPoints.Add(last + (last - prev));
        }

        Vector2 SelectionCentroid()
        {
            Vector2 c = Vector2.zero; int n = 0;
            foreach (int i in selection) if (i < choreo.pathPoints.Count) { c += choreo.pathPoints[i]; n++; }
            return n > 0 ? c / n : Vector2.zero;
        }

        void TransformSelection(System.Func<Vector2, Vector2, Vector2> op)   // op(point, centroid) -> new point
        {
            if (selection.Count == 0) return;
            Vector2 c = SelectionCentroid();
            Dial("Transform selection", () =>
            {
                foreach (int i in selection) if (i < choreo.pathPoints.Count) choreo.pathPoints[i] = ClampN(op(choreo.pathPoints[i], c));
            });
        }

        void RotateSelection(float deg) => TransformSelection((p, c) => c + ChoreographySampler.Rotate(p - c, deg));
        void ScaleSelection(float sx, float sy) => TransformSelection((p, c) => c + Vector2.Scale(p - c, new Vector2(sx, sy)));

        // Preview markers live in normalised space; a drag divides by ppu, so a tiny ppu could once fling them to
        // huge values and collapse the view. Keep them (and drag results) inside a sane range so it can't recur.
        void SanitizeAnchors()
        {
            if (!IsSane(launcherN)) launcherN = new Vector2(-0.9f, 0f);
            if (!IsSane(targetN)) targetN = new Vector2(0.9f, 0f);
        }

        static bool IsSane(Vector2 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && Mathf.Abs(v.x) < 20f && Mathf.Abs(v.y) < 20f;
        static Vector2 ClampN(Vector2 v) => new(Mathf.Clamp(v.x, -8f, 8f), Mathf.Clamp(v.y, -8f, 8f));

        static class Presets
        {
            public static List<Vector2> Line() => new() { new(-0.5f, 0), new(0.5f, 0) };
            public static List<Vector2> Arc() => new() { new(-0.5f, 0), new(0, 0.4f), new(0.5f, 0) };
            public static List<Vector2> SCurve() => new() { new(-0.5f, -0.3f), new(-0.15f, 0.3f), new(0.15f, -0.3f), new(0.5f, 0.3f) };
            public static List<Vector2> Sine()
            {
                var pts = new List<Vector2>();
                int n = 17;
                for (int i = 0; i < n; i++)
                {
                    float x = Mathf.Lerp(-0.5f, 0.5f, i / (float)(n - 1));
                    pts.Add(new Vector2(x, 0.3f * Mathf.Sin(i / (float)(n - 1) * Mathf.PI * 2f)));
                }
                return pts;
            }
        }

        // ── right: the stage (Painter2D custom element — the Trial-2 pattern) ────────────────
        class ChoreoStage : VisualElement
        {
            readonly ChoreographerWindow w;

            // coordinate mapping (rebuilt each paint)
            Vector2 originPixel;
            float ppu = 100f;

            // dragging: >=0 a path point, -2 launcher, -3 target
            int dragIndex = -1;
            const int DragLauncher = -2, DragTarget = -3;

            bool marqueeing;
            Vector2 marqueeStart, marqueeCur;

            static readonly Color ReleaseColor = new(0.95f, 0.3f, 0.3f, 0.95f);

            // overlay children (labels + sprite pool — Painter2D draws everything else)
            readonly Label launcherLabel, targetLabel, phaseLabel, legendLabel;
            readonly List<Image> spritePool = new();

            Choreography choreo => w.choreo;

            public ChoreoStage(ChoreographerWindow window)
            {
                w = window;
                AddToClassList("zui-stage");
                style.overflow = Overflow.Hidden;
                tooltip = "The live stage. Drag orange handles to shape the path; drag the green/red markers to move " +
                    "the launcher/target; click the curve to insert a point; right-click a handle to remove it.";

                generateVisualContent += Paint;
                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<GeometryChangedEvent>(_ => { MarkDirtyRepaint(); UpdateOverlay(); });

                launcherLabel = OverlayLabel("Launcher");
                targetLabel = OverlayLabel("Target");
                phaseLabel = OverlayLabel("");
                legendLabel = OverlayLabel("◦ green ring = fanned out (launch blend)   ◦ red ring = Release (target committed)");
            }

            Label OverlayLabel(string text)
            {
                var l = new Label(text);
                l.AddToClassList("zui-text--small");
                l.style.position = Position.Absolute;
                l.pickingMode = PickingMode.Ignore;
                Add(l);
                return l;
            }

            // ── painting ──────────────────────────────────────────────────────
            void Paint(MeshGenerationContext mgc)
            {
                if (choreo == null) return;
                var view = contentRect;
                if (!(view.width > 10f) || !(view.height > 10f)) return;   // also rejects NaN pre-layout

                w.SanitizeAnchors();
                w.UpdateCapture();
                FitView(view);

                var p = mgc.painter2D;
                int n = w.Count;
                float phase = w.Phase();

                if (w.showGrid)
                {
                    var axis = new Color(1, 1, 1, 0.08f);
                    Poly(p, new[] { N2P(new(-0.5f, -0.5f)), N2P(new(0.5f, -0.5f)), N2P(new(0.5f, 0.5f)), N2P(new(-0.5f, 0.5f)), N2P(new(-0.5f, -0.5f)) }, axis, 1f);
                    Poly(p, new[] { N2P(new(-0.5f, 0)), N2P(new(0.5f, 0)) }, axis, 1f);
                    Poly(p, new[] { N2P(new(0, -0.5f)), N2P(new(0, 0.5f)) }, axis, 1f);
                }

                var an = w.PreviewAnchors();
                if (w.showTemplate) DrawTemplate(p);
                if (w.showSpread) DrawSpread(p, n);
                if (w.showLines) DrawRoutes(p, n);
                if (w.showBlend) DrawBlendMarkers(p, n, an);
                if (w.showOnion) DrawOnion(p, n);
                if (w.showTrails) DrawTrails(p, n, phase);
                if (w.showDots || w.showFacing) DrawDancers(p, n, phase);
                if (w.showAnchors) DrawAnchorDots(p, an);
                if (w.showTemplate) DrawHandles(p, an);

                if (marqueeing)
                {
                    var r = MarqueeRect();
                    Fill(p, r, new Color(0.3f, 0.7f, 1f, 0.12f));
                    Poly(p, new[] { new Vector2(r.x, r.y), new(r.xMax, r.y), new(r.xMax, r.yMax), new(r.x, r.yMax), new(r.x, r.y) }, Color.white, 1f);
                }
            }

            /// Reposition the overlay labels + sprite pool. Called on tick, geometry change, and after edits.
            public void UpdateOverlay()
            {
                if (choreo == null || !(contentRect.width > 10f)) return;   // also rejects NaN pre-layout
                w.SanitizeAnchors();
                FitView(contentRect);
                var an = w.PreviewAnchors();

                bool l = w.showAnchors && an.hasLauncher, t = w.showAnchors && an.hasTarget;
                launcherLabel.Shown(l);
                targetLabel.Shown(t);
                if (l) { var pos = N2P(an.launcher); launcherLabel.style.left = pos.x + 8f; launcherLabel.style.top = pos.y - 8f; }
                if (t) { var pos = N2P(an.target); targetLabel.style.left = pos.x + 8f; targetLabel.style.top = pos.y - 8f; }

                phaseLabel.Shown(w.showGrid);
                if (w.showGrid)
                {
                    phaseLabel.text = $"phase {w.Phase():0.00}";
                    phaseLabel.style.left = 8f;
                    phaseLabel.style.top = contentRect.height - 18f;
                }
                legendLabel.Shown(w.showGrid && w.showBlend && ((an.hasLauncher && choreo.useLauncher) || (an.hasTarget && choreo.useTarget)));
                legendLabel.style.left = 8f;
                legendLabel.style.top = 4f;

                UpdateSprites();
            }

            void UpdateSprites()
            {
                int wanted = w.showSprites && w.previewSprites.Count > 0 ? Mathf.Min(w.Count, MaxDots) : 0;
                while (spritePool.Count < wanted)
                {
                    var img = new Image { scaleMode = ScaleMode.ScaleToFit };
                    img.style.position = Position.Absolute;
                    img.pickingMode = PickingMode.Ignore;
                    Add(img);
                    spritePool.Add(img);
                }
                for (int i = 0; i < spritePool.Count; i++) spritePool[i].Shown(i < wanted);
                if (wanted == 0) return;

                float phase = w.Phase();
                int n = w.Count;
                float size = w.spriteSize;
                for (int i = 0; i < wanted; i++)
                {
                    var img = spritePool[i];
                    var sprite = w.previewSprites[i % w.previewSprites.Count];
                    if (sprite == null) { img.Shown(false); continue; }   // null slot → the dot fallback in DrawDancers
                    var pose = ChoreographySampler.Evaluate(choreo, i, n, phase, w.PreviewAnchorsFor(i));
                    Vector2 pos = N2P(pose.position);
                    img.sprite = sprite;
                    img.style.width = size;
                    img.style.height = size;
                    img.style.left = pos.x - size * 0.5f;
                    img.style.top = pos.y - size * 0.5f;
                    img.style.rotate = new Rotate(w.showFacing ? -pose.facingDegrees : 0f);
                }
            }

            void DrawSpread(Painter2D p, int n)
            {
                var an = w.PreviewAnchors();
                int m = Mathf.Clamp(n, 2, MaxRouteDancers);
                var pts = new List<Vector2>();
                for (int k = 0; k < m; k++)
                {
                    int i = Mathf.RoundToInt(k / (float)(m - 1) * (n - 1));
                    pts.Add(N2P(ChoreographySampler.SamplePosition(choreo, i, n, 0.5f, an)));
                }
                Poly(p, pts.ToArray(), new Color(0.4f, 0.8f, 1f, 0.5f), 1.5f);
            }

            void DrawTemplate(Painter2D p)
            {
                var pts = new List<Vector2>();
                int steps = 64;
                for (int k = 0; k <= steps; k++)
                    pts.Add(N2P(TemplatePoint(k / (float)steps)));
                Poly(p, pts.ToArray(), new Color(1f, 0.8f, 0.2f, 0.35f), 1.5f);
            }

            void DrawBlendMarkers(Painter2D p, int n, ChoreoAnchors an)
            {
                bool launch = an.hasLauncher && choreo.useLauncher;
                bool target = an.hasTarget && choreo.useTarget;
                if (!launch && !target) return;

                int drawn = Mathf.Min(n, MaxRouteDancers);
                for (int d = 0; d < drawn; d++)
                {
                    int i = drawn == 1 ? 0 : Mathf.RoundToInt(d / (float)(drawn - 1) * (n - 1));
                    if (launch)
                        Dot(p, N2P(ChoreographySampler.SamplePosition(choreo, i, n, choreo.launchBlend, an)), 5f, new Color(0.3f, 0.95f, 0.4f, 0.9f));
                    if (target)
                        Dot(p, N2P(ChoreographySampler.SamplePosition(choreo, i, n, choreo.releaseAt, an)), 5f, ReleaseColor);
                }
            }

            Vector2 TemplatePoint(float u) => ChoreographySampler.FramePoint(choreo, choreo.Cache.Sample(u, false, out _), w.PreviewAnchors());

            void DrawRoutes(Painter2D p, int n)
            {
                int drawn = Mathf.Min(n, MaxRouteDancers);
                for (int d = 0; d < drawn; d++)
                {
                    int i = drawn == 1 ? 0 : Mathf.RoundToInt(d / (float)(drawn - 1) * (n - 1));
                    var an = w.PreviewAnchorsFor(i);
                    var pts = new List<Vector2>();
                    int steps = 40;
                    for (int k = 0; k <= steps; k++)
                        pts.Add(N2P(ChoreographySampler.SamplePosition(choreo, i, n, k / (float)steps, an)));
                    var c = w.indexColor ? IndexColor(ChoreographySampler.Ni(i, n)) : new Color(0.6f, 0.6f, 0.6f);
                    c.a = 0.25f;
                    Poly(p, pts.ToArray(), c, 1.2f);
                }
            }

            void DrawOnion(Painter2D p, int n)
            {
                int drawn = Mathf.Min(n, MaxRouteDancers);
                for (int f = 0; f < w.onionCount; f++)
                {
                    float prog = w.onionCount == 1 ? 0.5f : f / (float)(w.onionCount - 1);
                    float a = Mathf.Lerp(0.12f, 0.5f, prog);
                    for (int d = 0; d < drawn; d++)
                    {
                        int i = drawn == 1 ? 0 : Mathf.RoundToInt(d / (float)(drawn - 1) * (n - 1));
                        var c = w.indexColor ? IndexColor(ChoreographySampler.Ni(i, n)) : Color.white;
                        c.a = a;
                        Dot(p, N2P(ChoreographySampler.SamplePosition(choreo, i, n, prog, w.PreviewAnchorsFor(i))), w.dotSize * 0.7f, c);
                    }
                }
            }

            void DrawTrails(Painter2D p, int n, float phase)
            {
                for (int i = 0; i < n && i < MaxDots; i++)
                {
                    var an = w.PreviewAnchorsFor(i);
                    var here = ChoreographySampler.Evaluate(choreo, i, n, phase, an);
                    float p1 = here.progress;
                    float p0 = Mathf.Max(0f, p1 - w.trailFraction);
                    var pts = new List<Vector2>();
                    int steps = 16;
                    for (int k = 0; k <= steps; k++)
                    {
                        float u = Mathf.Lerp(p0, p1, k / (float)steps);
                        pts.Add(N2P(ChoreographySampler.SamplePosition(choreo, i, n, u, an)));
                    }
                    var c = w.indexColor ? IndexColor(ChoreographySampler.Ni(i, n)) : Color.white;
                    c.a = 0.5f;
                    Poly(p, pts.ToArray(), c, 1.5f);
                }
            }

            void DrawDancers(Painter2D p, int n, float phase)
            {
                bool spritesActive = w.showSprites && w.previewSprites.Count > 0;
                for (int i = 0; i < n && i < MaxDots; i++)
                {
                    var pose = ChoreographySampler.Evaluate(choreo, i, n, phase, w.PreviewAnchorsFor(i));
                    Vector2 pos = N2P(pose.position);
                    var c = w.indexColor ? IndexColor(ChoreographySampler.Ni(i, n)) : new Color(0.9f, 0.9f, 0.95f);

                    bool spriteHere = spritesActive && w.previewSprites[i % w.previewSprites.Count] != null;
                    if (w.showDots && !spriteHere)
                        Dot(p, pos, w.dotSize, c);

                    if (w.showFacing)
                    {
                        Vector2 tip = N2P(pose.position + ChoreographySampler.Rotate(new Vector2(0.06f, 0f), pose.facingDegrees));
                        Poly(p, new[] { pos, tip }, new Color(1, 1, 1, 0.7f), 1.5f);
                    }
                }
            }

            void DrawAnchorDots(Painter2D p, ChoreoAnchors an)
            {
                if (an.hasLauncher) Dot(p, N2P(an.launcher), 12f, new Color(0.3f, 0.95f, 0.4f));
                if (an.hasTarget) Dot(p, N2P(an.target), 12f, new Color(0.95f, 0.3f, 0.3f));
            }

            void DrawHandles(Painter2D p, ChoreoAnchors an)
            {
                int releaseIdx = NearestPointToRelease();
                for (int i = 0; i < choreo.pathPoints.Count; i++)
                {
                    Vector2 pos = N2P(ChoreographySampler.FramePoint(choreo, choreo.pathPoints[i], an));
                    if (i == releaseIdx) Dot(p, pos, 16f, ReleaseColor);   // release border
                    bool sel = w.selection.Contains(i);
                    Dot(p, pos, 10f, i == dragIndex ? Color.yellow : sel ? new Color(0.3f, 0.9f, 1f) : new Color(1f, 0.8f, 0.2f, 0.9f));
                }
            }

            // The control point closest to where the Release marker sits along the path.
            int NearestPointToRelease()
            {
                if (!choreo.useTarget || choreo.pathPoints.Count == 0) return -1;
                Vector2 relPt = choreo.Cache.Sample(Mathf.Clamp01(choreo.releaseAt), choreo.constantSpeed, out _);
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < choreo.pathPoints.Count; i++)
                {
                    float d = (choreo.pathPoints[i] - relPt).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                return best;
            }

            // ── interaction ───────────────────────────────────────────────────
            void OnPointerDown(PointerDownEvent e)
            {
                if (choreo == null) return;
                Vector2 mouse = e.localPosition;
                var an = w.PreviewAnchors();
                Vector2 HandlePix(int i) => N2P(ChoreographySampler.FramePoint(choreo, choreo.pathPoints[i], an));

                if (e.button == 1)   // right-click a handle to remove it (only when the path is shown)
                {
                    if (!w.showTemplate) return;
                    for (int i = 0; i < choreo.pathPoints.Count; i++)
                        if ((HandlePix(i) - mouse).sqrMagnitude < 100f && choreo.pathPoints.Count > 2)
                        {
                            int idx = i;
                            w.Dial("Remove path point", () => choreo.pathPoints.RemoveAt(idx));
                            w.selection.Clear();
                            w.OnPathOrSelectionEdited();
                            e.StopPropagation();
                            return;
                        }
                    return;
                }
                if (e.button != 0) return;

                if (w.showAnchors && choreo.useLauncher && (N2P(w.launcherN) - mouse).sqrMagnitude < 120f)
                { dragIndex = DragLauncher; this.CapturePointer(e.pointerId); e.StopPropagation(); return; }
                if (w.showAnchors && choreo.useTarget && (N2P(w.targetN) - mouse).sqrMagnitude < 120f)
                { dragIndex = DragTarget; this.CapturePointer(e.pointerId); e.StopPropagation(); return; }
                if (!w.showTemplate) return;

                for (int i = 0; i < choreo.pathPoints.Count; i++)
                    if ((HandlePix(i) - mouse).sqrMagnitude < 100f)
                    {
                        if (e.shiftKey) { if (!w.selection.Remove(i)) w.selection.Add(i); }   // shift-click toggles selection
                        else if (!w.selection.Contains(i)) { w.selection.Clear(); w.selection.Add(i); } // fresh single-select
                        dragIndex = i;
                        Undo.RecordObject(choreo, "Move path points");   // one record up front; the drag then mutates freely
                        this.CapturePointer(e.pointerId);
                        w.OnPathOrSelectionEdited();
                        MarkDirtyRepaint();
                        e.StopPropagation();
                        return;
                    }

                // empty space → start a marquee (which, if it turns out to be a click on the curve, inserts a point)
                marqueeing = true;
                marqueeStart = marqueeCur = mouse;
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
            }

            void OnPointerMove(PointerMoveEvent e)
            {
                if (!this.HasPointerCapture(e.pointerId)) return;
                Vector2 mouse = e.localPosition;

                if (dragIndex != -1)
                {
                    Vector2 nrm = ClampN(P2N(mouse));
                    if (dragIndex == DragLauncher) w.launcherN = nrm;
                    else if (dragIndex == DragTarget) w.targetN = nrm;
                    else
                    {
                        var an = w.PreviewAnchors();
                        Vector2 target = ClampN(ChoreographySampler.InverseFramePoint(choreo, nrm, an));
                        Vector2 delta = target - choreo.pathPoints[dragIndex];       // move the whole selection by the same delta
                        if (w.selection.Count == 0) w.selection.Add(dragIndex);
                        foreach (int i in w.selection) choreo.pathPoints[i] = ClampN(choreo.pathPoints[i] + delta);
                        choreo.MarkPathDirty(); EditorUtility.SetDirty(choreo);
                        w.RefreshInfoLabel();
                    }
                    MarkDirtyRepaint(); UpdateOverlay();
                    e.StopPropagation();
                }
                else if (marqueeing)
                {
                    marqueeCur = mouse;
                    MarkDirtyRepaint();
                    e.StopPropagation();
                }
            }

            void OnPointerUp(PointerUpEvent e)
            {
                if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);

                if (dragIndex != -1) { dragIndex = -1; MarkDirtyRepaint(); e.StopPropagation(); return; }
                if (!marqueeing) return;
                marqueeing = false;

                var an = w.PreviewAnchors();
                Vector2 HandlePix(int i) => N2P(ChoreographySampler.FramePoint(choreo, choreo.pathPoints[i], an));

                if ((marqueeCur - marqueeStart).sqrMagnitude < 16f)          // a click, not a drag
                {
                    if (!TryInsertPointAt(marqueeStart, an) && !e.shiftKey) w.selection.Clear();
                }
                else
                {
                    if (!e.shiftKey) w.selection.Clear();
                    var rect = MarqueeRect();
                    for (int i = 0; i < choreo.pathPoints.Count; i++)
                        if (rect.Contains(HandlePix(i))) w.selection.Add(i);
                }
                w.OnPathOrSelectionEdited();
                MarkDirtyRepaint();
                e.StopPropagation();
            }

            Rect MarqueeRect()
            {
                return Rect.MinMaxRect(Mathf.Min(marqueeStart.x, marqueeCur.x), Mathf.Min(marqueeStart.y, marqueeCur.y),
                                       Mathf.Max(marqueeStart.x, marqueeCur.x), Mathf.Max(marqueeStart.y, marqueeCur.y));
            }

            // Insert a control point where the user clicked the path curve. Only fires if the click is near the
            // drawn template; the new point goes into whichever control-polygon edge it least distorts.
            bool TryInsertPointAt(Vector2 mouse, ChoreoAnchors an)
            {
                float best = float.MaxValue;
                Vector2 prev = N2P(TemplatePoint(0f));
                for (int k = 1; k <= 64; k++)
                {
                    Vector2 cur = N2P(TemplatePoint(k / 64f));
                    best = Mathf.Min(best, DistToSegment(mouse, prev, cur));
                    prev = cur;
                }
                if (best > 18f) return false;

                Vector2 authored = ChoreographySampler.InverseFramePoint(choreo, P2N(mouse), an);
                int insert = choreo.pathPoints.Count;
                float bestDetour = float.MaxValue;
                for (int i = 0; i < choreo.pathPoints.Count - 1; i++)
                {
                    Vector2 a = choreo.pathPoints[i], b = choreo.pathPoints[i + 1];
                    float detour = Vector2.Distance(a, authored) + Vector2.Distance(authored, b) - Vector2.Distance(a, b);
                    if (detour < bestDetour) { bestDetour = detour; insert = i + 1; }
                }
                int at = insert;
                w.Dial("Add path point", () => choreo.pathPoints.Insert(at, authored));
                dragIndex = at;   // grab it so it can be dragged immediately
                w.OnPathOrSelectionEdited();
                return true;
            }

            static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
            {
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                return Vector2.Distance(p, a + t * ab);
            }

            // ── coordinate mapping ────────────────────────────────────────────
            void FitView(Rect view)
            {
                Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
                void Grow(Vector2 v) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }

                var an = w.PreviewAnchors();
                int n = w.Count;
                int sample = Mathf.Min(n, 12);
                for (int d = 0; d < sample; d++)
                {
                    int i = sample == 1 ? 0 : Mathf.RoundToInt(d / (float)(sample - 1) * (n - 1));
                    for (int k = 0; k <= 12; k++)
                        Grow(ChoreographySampler.SamplePosition(choreo, i, n, k / 12f, an));
                }
                foreach (var pp in choreo.pathPoints) Grow(ChoreographySampler.FramePoint(choreo, pp, an));
                if (an.hasLauncher) Grow(an.launcher);
                if (an.hasTarget) Grow(an.target);
                Grow(new(-0.5f, -0.5f)); Grow(new(0.5f, 0.5f));

                Vector2 center = (min + max) * 0.5f;
                Vector2 ext = Vector2.Max((max - min) * 0.5f, new Vector2(0.1f, 0.1f)) * 1.18f;
                ppu = Mathf.Min(view.width / (ext.x * 2f), view.height / (ext.y * 2f));
                originPixel = new Vector2(view.center.x - center.x * ppu, view.center.y + center.y * ppu);
            }

            Vector2 N2P(Vector2 nrm) => originPixel + new Vector2(nrm.x * ppu, -nrm.y * ppu);
            Vector2 P2N(Vector2 pix) { var d = pix - originPixel; return new Vector2(d.x / ppu, -d.y / ppu); }

            // ── Painter2D primitives ──────────────────────────────────────────
            static void Poly(Painter2D p, Vector2[] pts, Color c, float width)
            {
                if (pts.Length < 2) return;
                p.strokeColor = c;
                p.lineWidth = width;
                p.lineJoin = LineJoin.Round;
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                p.Stroke();
            }

            static void Dot(Painter2D p, Vector2 center, float size, Color c) =>
                Fill(p, new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), c);

            static void Fill(Painter2D p, Rect r, Color c)
            {
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(new Vector2(r.x, r.y));
                p.LineTo(new Vector2(r.xMax, r.y));
                p.LineTo(new Vector2(r.xMax, r.yMax));
                p.LineTo(new Vector2(r.x, r.yMax));
                p.ClosePath();
                p.Fill();
            }

            static Color IndexColor(float ni) => ChoreographySampler.IndexColor(ni);
        }
    }
}
