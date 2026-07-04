using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Choreographer.Editor
{
    /// Choreographer: author a Choreography as one reusable shape and watch N Dancers perform it in a looping
    /// 2D preview. Left = the dials (path, spread, facing, timing); right = the live stage with toggleable
    /// visualisation — full path lines, onion-skin ghosts, trails, index-coloured dots, and drop-in sample
    /// sprites. The preview samples the exact same ChoreographySampler the runtime uses, so what you tune is
    /// what plays. Drag the path control points right in the stage.
    public class ChoreographerWindow : ZUIWindow
    {
        [MenuItem("Laubrary/Choreographer/Choreographer")]
        public static void Open() => GetWindow<ChoreographerWindow>("Choreographer");

        [SerializeField] Choreography choreo;
        [SerializeField] List<Sprite> previewSprites = new();

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

        Vector2 leftScroll;

        // preview coordinate mapping (rebuilt each paint)
        Vector2 originPixel;
        float ppu = 100f;

        // dragging: >=0 a path point, -2 launcher, -3 target
        int dragIndex = -1;
        const int DragLauncher = -2, DragTarget = -3;

        // multi-select of path points + marquee
        readonly System.Collections.Generic.HashSet<int> selection = new();
        bool marqueeing;
        Vector2 marqueeStart, marqueeCur;

        const int MaxRouteDancers = 48;   // cap line/onion detail on huge N
        const int MaxDots = 500;

        protected override void OnZUIEnable() { lastTime = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
        void OnDisable() { EditorApplication.update -= Tick; }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            if (playing && choreo != null) { previewTime += dt * previewSpeed; Repaint(); }
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

        protected override void OnZUI()
        {
            DrawToolbar();
            if (choreo == null)
            {
                Label("Pick or create a Choreography to start. A choreo is one reusable shape; " +
                    "the same asset drives any number of dancers.", ZUI.ZTextStyle.Subtle);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawControls(GUILayout.Width(320));
            DrawStage();
            EditorGUILayout.EndHorizontal();
        }

        void DrawToolbar()
        {
            using (var row = ZUI.HRow())
            {
                EditorGUI.BeginChangeCheck();
                choreo = (Choreography)EditorGUILayout.ObjectField(choreo, typeof(Choreography), false, GUILayout.Width(220));
                if (EditorGUI.EndChangeCheck()) { previewTime = 0f; Repaint(); }
                if (row.Button("New", ZUI.Style.Default, GUILayout.Width(50))) CreateChoreo();
                row.Flexible();
                if (choreo != null) row.Label($"len {choreo.Cache.Length:0.00}  ·  {Count} dancers");
            }
        }

        // ── left: the dials (ZUI) ─────────────────────────────────────────────
        void DrawControls(params GUILayoutOption[] opt)
        {
            EditorGUILayout.BeginVertical(opt);
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            EditorGUI.BeginChangeCheck();

            Section("Path");
            using (var row = ZUI.HRow())
            {
                if (row.Button("Line")) SetPath(Presets.Line());
                if (row.Button("Sine")) SetPath(Presets.Sine());
                if (row.Button("Arc")) SetPath(Presets.Arc());
                if (row.Button("S")) SetPath(Presets.SCurve());
            }
            choreo.smooth = Toggle(choreo.smooth, "Smooth (spline)");
            choreo.constantSpeed = Toggle(choreo.constantSpeed, "Constant speed");
            Label($"Points: {choreo.pathPoints.Count}", ZUI.ZTextStyle.Small);
            using (var row = ZUI.HRow())
            {
                if (row.Button("+ point")) AddPoint();
                using (new EditorGUI.DisabledScope(choreo.pathPoints.Count <= 2))
                    if (row.Button("- point")) { choreo.pathPoints.RemoveAt(choreo.pathPoints.Count - 1); choreo.MarkPathDirty(); }
            }
            Label("Drag a handle to move it · click the curve to add a point · right-click a handle to remove. " +
                "Marquee-drag empty space to select many · shift-click to add · drag any selected handle to move them together.",
                ZUI.ZTextStyle.Subtle);

            if (selection.Count > 0)
            {
                Section($"Selection — {selection.Count} points");
                using (ZUI.HRow()) { if (Button("Rotate ⟲")) RotateSelection(-15f); if (Button("Rotate ⟳")) RotateSelection(15f); }
                using (ZUI.HRow()) { if (Button("Wider")) ScaleSelection(1.15f, 1f); if (Button("Narrower")) ScaleSelection(1f / 1.15f, 1f); }
                using (ZUI.HRow()) { if (Button("Taller")) ScaleSelection(1f, 1.15f); if (Button("Shorter")) ScaleSelection(1f, 1f / 1.15f); }
                using (ZUI.HRow()) { if (Button("Flip H")) ScaleSelection(-1f, 1f); if (Button("Flip V")) ScaleSelection(1f, -1f); }
                using (ZUI.HRow())
                {
                    if (Button("Select all")) { selection.Clear(); for (int i = 0; i < choreo.pathPoints.Count; i++) selection.Add(i); }
                    if (Button("Clear")) selection.Clear();
                }
            }

            Section("Spread (start line → circle)");
            choreo.spreadLength = Slider(choreo.spreadLength, 0f, 4f, "Length");
            choreo.spreadBend = Slider(choreo.spreadBend, 0f, 1f, "Bend");

            Section("Facing");
            choreo.facing = (FacingMode)MiniRadio((int)choreo.facing, new[] { "Fixed", "Radial" });
            choreo.facingAngle = Slider(choreo.facingAngle, -180f, 180f, choreo.facing == FacingMode.Radial ? "Angle offset" : "Angle");

            Section("Population & timing");
            choreo.defaultCount = Mathf.RoundToInt(Slider(choreo.defaultCount, 1, 200, "Default count"));
            choreo.duration = Slider(choreo.duration, 0.1f, 10f, "Duration (s)");
            choreo.stagger = Slider(choreo.stagger, 0f, 1f, "Stagger");
            choreo.direction = (Direction)MiniRadio((int)choreo.direction, new[] { "Scatter", "Gather" });
            choreo.loop = Toggle(choreo.loop, "Loop");

            Section("Anchors (Launcher / Target)");
            choreo.useLauncher = Toggle(choreo.useLauncher, "Use launcher");
            if (choreo.useLauncher) choreo.launchBlend = Slider(choreo.launchBlend, 0.01f, 1f, "Launch blend");
            choreo.useTarget = Toggle(choreo.useTarget, "Use target");
            if (choreo.useTarget)
            {
                choreo.releaseAt = Slider(choreo.releaseAt, 0f, 0.99f, "Release");
                choreo.targetBlend = Slider(choreo.targetBlend, 0.01f, 1f, "Target blend");
            }

            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(choreo); choreo.MarkPathDirty(); Repaint(); }

            Section("Preview — shows only what's ticked");
            previewCount = Mathf.RoundToInt(Slider(previewCount, 0, 200, "Preview count (0 = default)"));
            using (ZUI.HRow()) { showGrid = Tg(showGrid, "Grid"); showTemplate = Tg(showTemplate, "Path + handles"); }
            using (ZUI.HRow()) { showSpread = Tg(showSpread, "Spread line"); showLines = Tg(showLines, "Path lines"); }
            using (ZUI.HRow()) { showOnion = Tg(showOnion, "Onion-skin"); showTrails = Tg(showTrails, "Trails"); }
            using (ZUI.HRow()) { showDots = Tg(showDots, "Dots"); showFacing = Tg(showFacing, "Facing ticks"); }
            using (ZUI.HRow()) { showSprites = Tg(showSprites, "Sprites"); showAnchors = Tg(showAnchors, "Anchors"); }
            using (ZUI.HRow()) { showBlend = Tg(showBlend, "Blend marks"); indexColor = Tg(indexColor, "Colour by index"); }
            if (showOnion) onionCount = Mathf.RoundToInt(Slider(onionCount, 2, 20, "Onion frames"));
            if (showTrails) trailFraction = Slider(trailFraction, 0.02f, 1f, "Trail length");
            if (showSprites)
            {
                spriteSize = Slider(spriteSize, 8f, 96f, "Sprite size");
                DrawSpriteList();
            }
            if (choreo.useLauncher || choreo.useTarget)
                Label("The path runs launcher → target. Drag the green (launcher) / red (target) markers in the " +
                    "stage to move the whole choreo.", ZUI.ZTextStyle.Subtle);

            EditorGUILayout.EndScrollView();

            DrawTransport();   // pinned under the list
            EditorGUILayout.EndVertical();
        }

        void DrawTransport()
        {
            using (this.Box())
            {
                using (var row = ZUI.HRow())
                {
                    if (row.Button(playing ? "❚❚" : "▶", ZUI.Style.Default, GUILayout.Width(38))) playing = !playing;
                    if (row.Button("⟲", ZUI.Style.Default, GUILayout.Width(32))) { previewTime = 0f; Repaint(); }
                    EditorGUI.BeginChangeCheck();
                    float ph = Slider(Phase(), 0f, 1f, "");
                    if (EditorGUI.EndChangeCheck()) { previewTime = ph * Mathf.Max(0.0001f, choreo.CycleSeconds); playing = false; Repaint(); }
                }
                previewSpeed = Slider(previewSpeed, 0.1f, 3f, "Speed");
            }
        }

        void DrawSpriteList()
        {
            int remove = -1;
            for (int i = 0; i < previewSprites.Count; i++)
                using (var row = ZUI.HRow())
                {
                    previewSprites[i] = (Sprite)EditorGUILayout.ObjectField(previewSprites[i], typeof(Sprite), false);
                    if (row.Button("×", ZUI.Style.Default, GUILayout.Width(24))) remove = i;
                }
            if (Button("+ sprite")) previewSprites.Add(null);
            if (remove >= 0) previewSprites.RemoveAt(remove);
        }

        void Section(string t) { VerticalSpace(); Label(t, ZUI.ZTextStyle.SectionHeader); }

        // One cell of the 2-column preview toggle grid.
        bool Tg(bool v, string label) => Toggle(v, label, ZUI.Style.Default, GUILayout.Width(142));

        // ── right: the stage ──────────────────────────────────────────────────
        void DrawStage()
        {
            Rect view = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(view, new Color(0.10f, 0.10f, 0.12f));

            SanitizeAnchors();   // heal any runaway preview markers before they collapse the view
            UpdateCapture();      // advance per-dancer launcher/target freeze (mirrors the runtime)
            FitView(view);
            HandlePointDrag(view);

            if (Event.current.type != EventType.Repaint) { if (showTemplate) DrawHandlesOverlay(view); return; }

            int n = Count;
            float phase = Phase();

            if (showGrid)   // reference unit box + origin
            {
                var axis = new Color(1, 1, 1, 0.08f);
                DrawPoly(new[] { N2P(new(-0.5f, -0.5f)), N2P(new(0.5f, -0.5f)), N2P(new(0.5f, 0.5f)), N2P(new(-0.5f, 0.5f)), N2P(new(-0.5f, -0.5f)) }, axis, 1f);
                DrawPoly(new[] { N2P(new(-0.5f, 0)), N2P(new(0.5f, 0)) }, axis, 1f);
                DrawPoly(new[] { N2P(new(0, -0.5f)), N2P(new(0, 0.5f)) }, axis, 1f);
            }

            var an = PreviewAnchors();
            if (showTemplate) DrawTemplate();
            if (showSpread) DrawSpread(n);
            if (showLines) DrawRoutes(n);
            if (showBlend) DrawBlendMarkers(n, an);
            if (showOnion) DrawOnion(n);
            if (showTrails) DrawTrails(n, phase);
            if (showDots || showFacing || showSprites) DrawDancers(n, phase);
            if (showAnchors) DrawAnchors(an);

            if (showGrid)
            {
                GUI.Label(new Rect(view.x + 8, view.yMax - 20, view.width, 20), $"phase {phase:0.00}", EditorStyles.miniLabel);
                if (showBlend && ((an.hasLauncher && choreo.useLauncher) || (an.hasTarget && choreo.useTarget)))
                    GUI.Label(new Rect(view.x + 8, view.y + 4, view.width - 16, 16),
                        "◦ green ring = fanned out (launch blend)   ◦ red ring = Release (target committed)", EditorStyles.miniLabel);
            }

            if (showTemplate) DrawHandlesOverlay(view);
        }

        // The fan at mid-flight: dancer positions at progress 0.5 in index order — shows the spread's width/bend
        // where it is fullest, sitting around the spine.
        void DrawSpread(int n)
        {
            var an = PreviewAnchors();
            int m = Mathf.Clamp(n, 2, MaxRouteDancers);
            var pts = new List<Vector3>();
            for (int k = 0; k < m; k++)
            {
                int i = Mathf.RoundToInt(k / (float)(m - 1) * (n - 1));
                pts.Add(N2P(ChoreographySampler.SamplePosition(choreo, i, n, 0.5f, an)));
            }
            DrawPoly(pts.ToArray(), new Color(0.4f, 0.8f, 1f, 0.5f), 1.5f);
        }

        // The authored path in its own (path-local) space — the shape the orange handles edit. Drawn faint so
        // it reads as a template, distinct from the vivid coloured routes each dancer actually travels.
        void DrawTemplate()
        {
            var pts = new List<Vector3>();
            int steps = 64;
            for (int k = 0; k <= steps; k++)
                pts.Add(N2P(TemplatePoint(k / (float)steps)));
            DrawPoly(pts.ToArray(), new Color(1f, 0.8f, 0.2f, 0.35f), 1.5f);
        }

        // A marker on each drawn route at the moment the launch fan-out completes (progress = launchBlend) and
        // the moment the target converge begins (progress = 1 - targetBlend), so the blend windows are visible.
        void DrawBlendMarkers(int n, ChoreoAnchors an)
        {
            bool launch = an.hasLauncher && choreo.useLauncher;
            bool target = an.hasTarget && choreo.useTarget;
            if (!launch && !target) return;

            int drawn = Mathf.Min(n, MaxRouteDancers);
            for (int d = 0; d < drawn; d++)
            {
                int i = drawn == 1 ? 0 : Mathf.RoundToInt(d / (float)(drawn - 1) * (n - 1));
                if (launch)
                    DrawDot(N2P(ChoreographySampler.SamplePosition(choreo, i, n, choreo.launchBlend, an)), 5f, new Color(0.3f, 0.95f, 0.4f, 0.9f));
                if (target)
                    DrawDot(N2P(ChoreographySampler.SamplePosition(choreo, i, n, choreo.releaseAt, an)), 5f, ReleaseColor);
            }
        }

        // The spine in on-stage (framed) space: the authored path carried onto the launcher→target frame.
        Vector2 TemplatePoint(float u) => ChoreographySampler.FramePoint(choreo, choreo.Cache.Sample(u, false, out _), PreviewAnchors());

        void DrawRoutes(int n)
        {
            int drawn = Mathf.Min(n, MaxRouteDancers);
            for (int d = 0; d < drawn; d++)
            {
                int i = drawn == 1 ? 0 : Mathf.RoundToInt(d / (float)(drawn - 1) * (n - 1));
                var an = PreviewAnchorsFor(i);
                var pts = new List<Vector3>();
                int steps = 40;
                for (int k = 0; k <= steps; k++)
                    pts.Add(N2P(ChoreographySampler.SamplePosition(choreo, i, n, k / (float)steps, an)));
                var c = indexColor ? IndexColor(ChoreographySampler.Ni(i, n)) : new Color(0.6f, 0.6f, 0.6f);
                c.a = 0.25f;
                DrawPoly(pts.ToArray(), c, 1.2f);
            }
        }

        void DrawOnion(int n)
        {
            int drawn = Mathf.Min(n, MaxRouteDancers);
            for (int f = 0; f < onionCount; f++)
            {
                float prog = onionCount == 1 ? 0.5f : f / (float)(onionCount - 1);
                float a = Mathf.Lerp(0.12f, 0.5f, prog);
                for (int d = 0; d < drawn; d++)
                {
                    int i = drawn == 1 ? 0 : Mathf.RoundToInt(d / (float)(drawn - 1) * (n - 1));
                    var c = indexColor ? IndexColor(ChoreographySampler.Ni(i, n)) : Color.white;
                    c.a = a;
                    DrawDot(N2P(ChoreographySampler.SamplePosition(choreo, i, n, prog, PreviewAnchorsFor(i))), dotSize * 0.7f, c);
                }
            }
        }

        void DrawTrails(int n, float phase)
        {
            for (int i = 0; i < n && i < MaxDots; i++)
            {
                var an = PreviewAnchorsFor(i);
                var here = ChoreographySampler.Evaluate(choreo, i, n, phase, an);
                float p1 = here.progress;
                float p0 = Mathf.Max(0f, p1 - trailFraction);
                var pts = new List<Vector3>();
                int steps = 16;
                for (int k = 0; k <= steps; k++)
                {
                    float p = Mathf.Lerp(p0, p1, k / (float)steps);
                    pts.Add(N2P(ChoreographySampler.SamplePosition(choreo, i, n, p, an)));
                }
                var c = indexColor ? IndexColor(ChoreographySampler.Ni(i, n)) : Color.white;
                c.a = 0.5f;
                DrawPoly(pts.ToArray(), c, 1.5f);
            }
        }

        void DrawDancers(int n, float phase)
        {
            for (int i = 0; i < n && i < MaxDots; i++)
            {
                var pose = ChoreographySampler.Evaluate(choreo, i, n, phase, PreviewAnchorsFor(i));
                Vector2 p = N2P(pose.position);
                var c = indexColor ? IndexColor(ChoreographySampler.Ni(i, n)) : new Color(0.9f, 0.9f, 0.95f);

                if (showSprites && previewSprites.Count > 0)
                    DrawSprite(previewSprites[i % previewSprites.Count], p, pose.facingDegrees);
                else if (showDots)
                    DrawDot(p, dotSize, c);

                if (showFacing)
                {
                    Vector2 tip = N2P(pose.position + ChoreographySampler.Rotate(new Vector2(0.06f, 0f), pose.facingDegrees));
                    DrawPoly(new Vector3[] { p, tip }, new Color(1, 1, 1, 0.7f), 1.5f);
                }
            }
        }

        void DrawAnchors(ChoreoAnchors an)
        {
            if (an.hasLauncher)
            {
                Vector2 p = N2P(an.launcher);
                DrawDot(p, 12f, new Color(0.3f, 0.95f, 0.4f));
                GUI.Label(new Rect(p.x + 8, p.y - 8, 80, 16), "Launcher", EditorStyles.miniLabel);
            }
            if (an.hasTarget)
            {
                Vector2 p = N2P(an.target);
                DrawDot(p, 12f, new Color(0.95f, 0.3f, 0.3f));
                GUI.Label(new Rect(p.x + 8, p.y - 8, 80, 16), "Target", EditorStyles.miniLabel);
            }
        }

        // ── path-point handles (drawn as GUI, so they work on non-repaint events) ──
        static readonly Color ReleaseColor = new(0.95f, 0.3f, 0.3f, 0.95f);

        void DrawHandlesOverlay(Rect view)
        {
            var an = PreviewAnchors();
            int releaseIdx = NearestPointToRelease();
            for (int i = 0; i < choreo.pathPoints.Count; i++)
            {
                Vector2 p = N2P(ChoreographySampler.FramePoint(choreo, choreo.pathPoints[i], an));
                if (i == releaseIdx) EditorGUI.DrawRect(new Rect(p.x - 8, p.y - 8, 16, 16), ReleaseColor);   // release border
                bool sel = selection.Contains(i);
                var r = new Rect(p.x - 5, p.y - 5, 10, 10);
                EditorGUI.DrawRect(r, i == dragIndex ? Color.yellow : sel ? new Color(0.3f, 0.9f, 1f) : new Color(1f, 0.8f, 0.2f, 0.9f));
            }
            if (marqueeing)
            {
                var r = MarqueeRect();
                EditorGUI.DrawRect(r, new Color(0.3f, 0.7f, 1f, 0.12f));
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), Color.white);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), Color.white);
                EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), Color.white);
                EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), Color.white);
            }
        }

        // The control point closest to where the Release marker sits along the path (so you can see where release begins).
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

        void HandlePointDrag(Rect view)
        {
            var e = Event.current;
            var an = PreviewAnchors();
            Vector2 HandlePix(int i) => N2P(ChoreographySampler.FramePoint(choreo, choreo.pathPoints[i], an));

            // right-click a handle to remove it (only when the path is shown)
            if (showTemplate && e.type == EventType.ContextClick && view.Contains(e.mousePosition))
            {
                for (int i = 0; i < choreo.pathPoints.Count; i++)
                    if ((HandlePix(i) - e.mousePosition).sqrMagnitude < 100f && choreo.pathPoints.Count > 2)
                    {
                        Undo.RecordObject(choreo, "Remove path point");
                        choreo.pathPoints.RemoveAt(i);
                        choreo.MarkPathDirty(); EditorUtility.SetDirty(choreo);
                        e.Use(); Repaint(); return;
                    }
            }

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                if (showAnchors && choreo.useLauncher && (N2P(launcherN) - e.mousePosition).sqrMagnitude < 120f) { dragIndex = DragLauncher; e.Use(); return; }
                if (showAnchors && choreo.useTarget && (N2P(targetN) - e.mousePosition).sqrMagnitude < 120f) { dragIndex = DragTarget; e.Use(); return; }
                if (showTemplate)
                {
                    for (int i = 0; i < choreo.pathPoints.Count; i++)
                        if ((HandlePix(i) - e.mousePosition).sqrMagnitude < 100f)
                        {
                            if (e.shift) { if (!selection.Remove(i)) selection.Add(i); }   // shift-click toggles selection
                            else if (!selection.Contains(i)) { selection.Clear(); selection.Add(i); } // fresh single-select
                            dragIndex = i; e.Use(); Repaint(); return;
                        }
                    // empty space → start a marquee (which, if it turns out to be a click on the curve, inserts a point)
                    marqueeing = true; marqueeStart = marqueeCur = e.mousePosition; e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && dragIndex != -1)
            {
                Vector2 nrm = ClampN(P2N(e.mousePosition));
                if (dragIndex == DragLauncher) launcherN = nrm;
                else if (dragIndex == DragTarget) targetN = nrm;
                else
                {
                    Vector2 target = ClampN(ChoreographySampler.InverseFramePoint(choreo, nrm, an));
                    Vector2 delta = target - choreo.pathPoints[dragIndex];       // move the whole selection by the same delta
                    if (selection.Count == 0) selection.Add(dragIndex);
                    foreach (int i in selection) choreo.pathPoints[i] = ClampN(choreo.pathPoints[i] + delta);
                    choreo.MarkPathDirty(); EditorUtility.SetDirty(choreo);
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && marqueeing) { marqueeCur = e.mousePosition; e.Use(); Repaint(); }
            else if (e.type == EventType.MouseUp)
            {
                if (dragIndex != -1) { dragIndex = -1; e.Use(); }
                else if (marqueeing)
                {
                    marqueeing = false;
                    if ((marqueeCur - marqueeStart).sqrMagnitude < 16f)          // a click, not a drag
                    {
                        if (!TryInsertPointAt(marqueeStart, an) && !e.shift) selection.Clear();
                    }
                    else
                    {
                        if (!e.shift) selection.Clear();
                        var rect = MarqueeRect();
                        for (int i = 0; i < choreo.pathPoints.Count; i++)
                            if (rect.Contains(HandlePix(i))) selection.Add(i);
                    }
                    e.Use(); Repaint();
                }
            }
        }

        Rect MarqueeRect()
        {
            return Rect.MinMaxRect(Mathf.Min(marqueeStart.x, marqueeCur.x), Mathf.Min(marqueeStart.y, marqueeCur.y),
                                   Mathf.Max(marqueeStart.x, marqueeCur.x), Mathf.Max(marqueeStart.y, marqueeCur.y));
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
            Undo.RecordObject(choreo, "Transform selection");
            Vector2 c = SelectionCentroid();
            foreach (int i in selection) if (i < choreo.pathPoints.Count) choreo.pathPoints[i] = ClampN(op(choreo.pathPoints[i], c));
            choreo.MarkPathDirty(); EditorUtility.SetDirty(choreo); Repaint();
        }

        void RotateSelection(float deg) => TransformSelection((p, c) => c + ChoreographySampler.Rotate(p - c, deg));
        void ScaleSelection(float sx, float sy) => TransformSelection((p, c) => c + Vector2.Scale(p - c, new Vector2(sx, sy)));

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
            Undo.RecordObject(choreo, "Add path point");
            choreo.pathPoints.Insert(insert, authored);
            choreo.MarkPathDirty(); EditorUtility.SetDirty(choreo);
            dragIndex = insert;   // grab it so it can be dragged immediately
            return true;
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + t * ab);
        }

        // ── coordinate mapping ────────────────────────────────────────────────
        void FitView(Rect view)
        {
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            void Grow(Vector2 v) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }

            var an = PreviewAnchors();
            int n = Count;
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

        // Preview markers live in normalised space; a drag divides by ppu, so a tiny ppu could once fling them to
        // huge values and collapse the view. Keep them (and drag results) inside a sane range so it can't recur.
        void SanitizeAnchors()
        {
            if (!IsSane(launcherN)) launcherN = new Vector2(-0.9f, 0f);
            if (!IsSane(targetN)) targetN = new Vector2(0.9f, 0f);
        }

        static bool IsSane(Vector2 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && Mathf.Abs(v.x) < 20f && Mathf.Abs(v.y) < 20f;
        static Vector2 ClampN(Vector2 v) => new(Mathf.Clamp(v.x, -8f, 8f), Mathf.Clamp(v.y, -8f, 8f));

        Vector2 N2P(Vector2 n) => originPixel + new Vector2(n.x * ppu, -n.y * ppu);
        Vector2 P2N(Vector2 p) { var d = p - originPixel; return new Vector2(d.x / ppu, -d.y / ppu); }

        // ── primitives ────────────────────────────────────────────────────────
        static void DrawDot(Vector2 p, float size, Color c) =>
            EditorGUI.DrawRect(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), c);

        static void DrawPoly(Vector3[] pts, Color c, float width)
        {
            if (Event.current.type != EventType.Repaint || pts.Length < 2) return;
            Handles.color = c;
            Handles.DrawAAPolyLine(width, pts);
        }

        static void DrawPoly(Vector2[] pts, Color c, float width)
        {
            var v = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++) v[i] = pts[i];
            DrawPoly(v, c, width);
        }

        void DrawSprite(Sprite s, Vector2 p, float facingDeg)
        {
            if (s == null || s.texture == null) { DrawDot(p, dotSize, Color.white); return; }
            var tex = s.texture;
            var r = s.textureRect;
            var uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
            var dst = new Rect(p.x - spriteSize * 0.5f, p.y - spriteSize * 0.5f, spriteSize, spriteSize);

            Matrix4x4 old = GUI.matrix;
            if (showFacing) GUIUtility.RotateAroundPivot(-facingDeg, p);
            GUI.DrawTextureWithTexCoords(dst, tex, uv, true);
            GUI.matrix = old;
        }

        static Color IndexColor(float ni) => ChoreographySampler.IndexColor(ni);

        // ── asset & path helpers ──────────────────────────────────────────────
        void SetPath(List<Vector2> pts) { Undo.RecordObject(choreo, "Path preset"); choreo.pathPoints = pts; choreo.MarkPathDirty(); EditorUtility.SetDirty(choreo); }

        void AddPoint()
        {
            var last = choreo.pathPoints[choreo.pathPoints.Count - 1];
            var prev = choreo.pathPoints[choreo.pathPoints.Count - 2];
            choreo.pathPoints.Add(last + (last - prev));
            choreo.MarkPathDirty();
        }

        void CreateChoreo()
        {
            string path = EditorUtility.SaveFilePanelInProject("New Choreography", "Choreography", "asset", "");
            if (string.IsNullOrEmpty(path)) return;
            var c = CreateInstance<Choreography>();
            AssetDatabase.CreateAsset(c, path);
            AssetDatabase.SaveAssets();
            choreo = c; previewTime = 0f;
        }

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
    }
}
