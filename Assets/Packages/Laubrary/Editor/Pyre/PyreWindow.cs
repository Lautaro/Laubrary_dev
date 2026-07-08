using UnityEditor;
using UnityEngine;
using Laubrary.Pyre;
using Laubrary.PreviewStage;
using Laubrary.AssetKit.Editor;

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

        BlastSpec spec => Current;   // the base owns the current asset; alias for the dial/preview code

        protected override string TypeLabel => "Blast";
        protected override string NewAssetName => "New Pyre";
        protected override string DefaultFolder => "Assets/Pyre";
        protected override void OnAssetChanged() { frame = 0; layerSel = 0; scrub = -1; }
        protected override void InitializeNewAsset(BlastSpec item) => item.AddExampleContent();
        protected override Texture2D RenderThumbnail(BlastSpec item)
        {
            int mid = Mathf.Clamp(item.frameCount / 2, 0, Mathf.Max(0, item.frameCount - 1));
            var tex = BlastRenderer.RenderFrameTexture(item, mid);
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        // layout (serialized so it sticks)
        [SerializeField] float leftWidth = 340f;
        [SerializeField] float previewHeight = 320f;
        [SerializeField] float zoom = 4f;

        // preview backdrop (editor-only, never baked)
        enum PreviewBgMode { Solid, Gradient, Image }
        [SerializeField] PreviewBgMode bgMode = PreviewBgMode.Solid;
        [SerializeField] Color bgSolid = new Color(0.08f, 0.08f, 0.10f);
        [SerializeField] Gradient bgGradient;
        [SerializeField] Texture2D bgImage;
        [SerializeField] Color bgImageTint = Color.white; // multiplies the backdrop image; white = untouched
        [SerializeField] float bgImageZoom = 1f;          // scales the backdrop image within the viewport
        [SerializeField] bool showFrame = false;          // draw the sprite-frame (canvas) border in the preview

        // add-layer shape picker
        [SerializeField] LayerShape addShape = LayerShape.Disc;

        int layerSel;
        Vector2 leftScroll;

        // splitters
        bool dragLeft, dragPreview;
        bool draggingOrigin;   // dragging the origin/pivot ✛ handle in the preview
        bool draggingPan;      // middle-dragging to pan the animation frame
        [SerializeField] float originMarkerAlpha = 0.95f;   // preview-only: origin ✛ opacity
        [SerializeField] Vector2 previewPan;                // preview-only: offset the animation frame (middle-drag)
        [SerializeField] PreviewBackground stageBg;         // reusable sprite test-backdrop (separate from the frame)
        int stageSel = -1;
        bool draggingStage;
        bool placeMetaMode;    // MetaBlob: clicking the preview drops orbs
        [SerializeField] bool showMetaMarkers = true;   // MetaBlob: draw the orb rings + numbers over the preview
        int metaSel = -1;
        bool draggingMetaOrb;
        SmudgeModifier paintSmudge;   // the Smudge modifier currently recording a stroke in the preview (null = none)
        bool draggingSmudge;          // true while a smudge stroke is being dragged out
        int draggingLayer = -1;   // index of the layer being drag-reordered, or -1
        int draggingMod = -1;     // index of the modifier being drag-reordered, or -1
        string draggingModList;   // idp of the modifier list that drag belongs to (layer mods vs global mods)

        // playback
        double lastTime;
        float acc;
        int frame;
        bool playing = true;
        float fps = 12f;
        float speed = 1f;
        int scrub = -1;                 // >=0 means the user is holding a scrubbed frame (paused)
        Texture2D previewTex;
        Rect lastView;                  // remembered for the Fit button

        static readonly string[] ShapeLabels = { "Disc", "Crescent", "Sparkle", "Bars", "Sprite", "Meta blob" };
        static readonly string[] BarDecayLabels = { "Contract", "Dissolve" };

        // Captured on the Layout event only so the control set can't change between Layout and Repaint of the same
        // frame (IMGUI reflow hazard). starLayout = any Bars layer has Star (gates the auto-canvas readout);
        // barStarLayout = the SELECTED layer is a star Bars layer (gates its Arms/Spread rows). See OnZUI.
        bool starLayout, barStarLayout, discHollowLayout, metaFlowLayout;

        protected override void OnZUIEnable()
        {
            lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            bgGradient ??= DefaultBgGradient();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= Tick;
            if (previewTex != null) { DestroyImmediate(previewTex); previewTex = null; }
        }

        int FrameCount => spec != null ? Mathf.Max(1, spec.frameCount) : 1;
        int CurrentFrame() => Mathf.Clamp(scrub >= 0 ? scrub : frame, 0, FrameCount - 1);

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTime), 0f, 0.1f);
            lastTime = now;
            if (playing && spec != null && scrub < 0)
            {
                acc += dt * fps * speed;
                while (acc >= 1f) { acc -= 1f; frame = (frame + 1) % FrameCount; }
                Repaint();
            }
            // Keep the preview repainting while paused too (not the browser) so the origin ✛ marker flashes.
            else if (spec != null && !IsBrowsing && originMarkerAlpha > 0.001f) Repaint();
        }

        protected override void DrawAsset(BlastSpec asset)
        {
            // Capture the star gate on Layout only, so the width/height block below has a stable control count
            // across this frame's Layout and Repaint passes even if the Orbit/Star radio is clicked.
            if (Event.current.type == EventType.Layout)
            {
                starLayout = AnyStarLayer();
                var sel = layerSel >= 0 && layerSel < asset.layers.Count ? asset.layers[layerSel] : null;
                barStarLayout = sel != null && sel.shape == LayerShape.Bars && sel.star;
                discHollowLayout = sel != null && (sel.shape == LayerShape.Disc || sel.shape == LayerShape.SparkleField) && sel.hollow;
                metaFlowLayout = sel != null && sel.shape == LayerShape.MetaBlob && sel.metaFlow;
            }

            // Wider label column so the longer ValRow labels ("Taper (centre↔edge)", "Sparkle density", …) don't
            // clip — the text-overflow rule from the UIAudit heuristics applied to this editor window.
            EditorGUIUtility.labelWidth = 112f;

            EditorGUILayout.BeginHorizontal();
            DrawLeft();
            DrawVerticalSplitter();
            DrawPreview();
            EditorGUILayout.EndHorizontal();

            // Any edit (layer toggle, a dial, a deform value…) must rebuild the preview even while paused —
            // the preview texture is only regenerated on Repaint, so schedule one whenever something changed.
            if (GUI.changed) Repaint();
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
            EditorGUI.BeginChangeCheck();

            Label("Blast", ZUI.ZTextStyle.SectionHeader);
            spec.seed = EditorGUILayout.IntField("Seed", spec.seed);
            // ZUI.IntField measures its own label width (in a Flow row), so long labels can't be clipped.
            // In Star spread the canvas auto-fits the arms, so show the resolved size instead of editable fields.
            if (starLayout)
                Label($"Canvas {spec.Width}×{spec.Height}  (auto-fit for star)", ZUI.ZTextStyle.Subtle);
            else
                using (ZUI.Flow())
                {
                    spec.canvasSize = ZUI.IntField("Width", spec.canvasSize, 52f, 4, 512);
                    spec.canvasHeight = ZUI.IntField("Height (0 = square)", spec.canvasHeight, 52f, 0, 512);
                }
            spec.pixelsPerUnit = Mathf.Max(1f, EditorGUILayout.FloatField("Pixels per unit", spec.pixelsPerUnit));
            spec.background = EditorGUILayout.ColorField("Bake background", spec.background);
            spec.frameCount = Mathf.Max(1, Mathf.RoundToInt(Slider(spec.frameCount, 1, 64, "Frame count")));

            // Origin / pivot (normalized): the point that lands on the spawn position. Editable here and by dragging
            // the ✛ handle in the preview. A game aligns this to the hit pixel.
            spec.origin.x = EditorGUILayout.Slider("Origin X", spec.origin.x, 0f, 1f);
            spec.origin.y = EditorGUILayout.Slider("Origin Y", spec.origin.y, 0f, 1f);
            originMarkerAlpha = EditorGUILayout.Slider("Origin marker α", originMarkerAlpha, 0f, 1f);
            if (Button("Origin → centre")) spec.origin = new Vector2(0.5f, 0.5f);

            VerticalSpace();
            Label("Global modifiers", ZUI.ZTextStyle.SectionHeader);
            DrawModifiers(spec.globalModifiers, "gm.");

            VerticalSpace();
            DrawLayerList();

            VerticalSpace();
            DrawSelectedLayer();

            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(spec); frame = Mathf.Min(frame, FrameCount - 1); }

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

        void DrawSelectedLayer()
        {
            if (layerSel < 0 || layerSel >= spec.layers.Count) return;
            var l = spec.layers[layerSel];
            float cs = spec.canvasSize;
            float half = cs * 0.5f;

            Label($"Layer — {l.name}", ZUI.ZTextStyle.SectionHeader);

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
                // Gradient position/zoom + a movable core apply to both spatial fills (Fill + Flow fill). Offset the
                // core + a bright→dark gradient = a 3D orb / energy ball.
                if (colorModeForLayout != ColorMode.OverLife)
                {
                    ValRow("Gradient position", l.colorFlow, -2f, 2f, 0f);
                    ValRow("Gradient zoom", l.colorFlowZoom, 0.1f, 4f, 1f);
                    ValRow("Core offset X", l.gradientOffsetX, -1f, 1f, 0f);
                    ValRow("Core offset Y", l.gradientOffsetY, -1f, 1f, 0f);
                }
            }
            ValRow("Alpha", l.alpha, 0f, 1f);

            // Bars is a self-contained directional mode — none of the scatter / emission / deform controls apply,
            // so show ONLY the Bars box.
            if (shapeForLayout == LayerShape.Bars)
            {
                using (Box("Bars — forward-growing row"))
                {
                    ValRow("Bars per side", l.barCount, 0f, 40f, 7f);
                    ValRow("Width (px)", l.barWidth, 1f, 12f, 3f);
                    ValRow("Spacing (×width)", l.barSpacing, 1f, 6f, 1.5f);   // 1 = bars touch, 2 = one-bar gap, …
                    ValRow("Edge softness", l.barSoftness, 0f, 1f, 0f);       // soft sides + tip (dissolve too)
                    ValRow("Forward reach", l.barForward, 0f, cs, 40f);
                    // Taper is the arm-shape control: +1 = centre longest → triangle/flame, 0 = flat, -1 = concave.
                    ValRow("Taper (centre↔edge)", l.barTaper, -1f, 1f, 0.85f);
                    ValRow("Backward frac", l.barBackwardFrac, 0f, 1f, 0.18f);
                    // Stagger is TIMING, not shape: it delays outer bars so the row unfurls centre-out.
                    ValRow("Stagger (timing)", l.barStagger, 0f, 0.5f, 0.05f);
                    ValRow("Layer angle", l.barAngleDeg, -180f, 180f, 0f);
                    l.barMirror = Toggle(l.barMirror, "Mirror angle");
                    ValRow("Origin inset", l.originInset, 0f, 40f, 4f);
                    var decayForLayout = l.barDecay;
                    l.barDecay = (BarDecay)MiniRadio((int)l.barDecay, BarDecayLabels);
                    if (decayForLayout == BarDecay.Dissolve)
                        l.dissolveStart = Slider(l.dissolveStart, 0f, 1f, "Dissolve start");

                    ValRow("Base angle", l.baseAngleDeg, -180f, 180f, 0f);
                    l.star = Toggle(l.star, "Star (arms radiate from centre)");
                    if (barStarLayout)   // Layout-captured gate so the control count is reflow-safe
                    {
                        l.spreadCount = Mathf.Max(1, Mathf.RoundToInt(Slider(l.spreadCount, 1, 24, "Arms")));
                        ValRow("Spread degrees", l.spreadDegrees, 0f, 360f, 360f);
                        Label("Arms share the centre and radiate outward; canvas auto-fits.", ZUI.ZTextStyle.Small);
                    }
                }
                VerticalSpace();
                Label("Modifiers", ZUI.ZTextStyle.SectionHeader);
                DrawModifiers(l.modifiers, "lm." + layerSel + ".");
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
                    l.metaThreshold = Slider(l.metaThreshold, 0.1f, 2f, "Threshold");
                    l.metaShadeRange = Slider(l.metaShadeRange, 0.1f, 3f, "Shade range");
                    l.metaSoftness = Slider(l.metaSoftness, 0.01f, 1f, "Edge softness");
                    l.metaSpawnInterval = Slider(l.metaSpawnInterval, 0f, 0.5f, "Spawn interval");

                    // Layer-wide motion — one animatable value shared by every orb, so the whole blob comes alive.
                    ValRow("Radius pulse", l.metaRadiusScale, 0f, 3f, 1f);   // ×radius of every orb over life
                    ValRow("Expand", l.metaExpand, 0f, 3f, 1f);              // contract/expand centres about the origin

                    // Flow shading: scroll/zoom the gradient through the field depth (mirrored) instead of static.
                    l.metaFlow = Toggle(l.metaFlow, "Flow gradient (scroll depth)");
                    if (metaFlowLayout)   // Layout-captured gate so the control count is reflow-safe
                    {
                        ValRow("Gradient position", l.colorFlow, -2f, 2f, 0f);
                        ValRow("Gradient zoom", l.colorFlowZoom, 0.1f, 4f, 1f);
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
                VerticalSpace();
                Label("Modifiers", ZUI.ZTextStyle.SectionHeader);
                DrawModifiers(l.modifiers, "lm." + layerSel + ".");
                return;
            }

            // ── shape (scatter) controls ─────────────────────────────────────────
            ValRow("Count", l.count, 1f, 40f);
            ValRow("Spawn radius", l.spawnRadius, 0f, 1f);
            ValRow("Position X", l.positionX, -half, half);
            ValRow("Position Y", l.positionY, -half, half);
            ValRow("Size", l.size, 0f, half);
            l.perShapeLifeJitter = Slider(l.perShapeLifeJitter, 0f, 1f, "Life jitter");
            l.spawnStagger = Slider(l.spawnStagger, 0f, 1f, "Spawn stagger");

            switch (shapeForLayout)
            {
                case LayerShape.Disc:
                    DrawDiscEdges(l);
                    break;
                case LayerShape.SparkleField:
                    ValRow("Sparkle density", l.sparkleDensity, 0f, 1f, 0.25f);
                    ValRow("Sparkle seed", l.sparkleSeed, 0f, 1f);   // Min-Max default → twinkles per frame
                    DrawDiscEdges(l);                                 // a sparkle field is a disc: soft edges + hole
                    break;
                case LayerShape.Crescent:
                    ValRow("Crescent X", l.crescentOffsetX, -half, half);
                    ValRow("Crescent Y", l.crescentOffsetY, -half, half);
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

            using (Box("Wind drift"))
            {
                ValRow("Wind X", l.windX, -half, half, 0f);
                ValRow("Wind Y", l.windY, -half, half, 0f);
            }

            VerticalSpace();
            Label("Modifiers", ZUI.ZTextStyle.SectionHeader);
            DrawModifiers(l.modifiers, "lm." + layerSel + ".");
        }

        // One animatable-value row (ZUIValueControl). def (when given) is the double-click reset target.
        // Curve mode here spans the LAYER's frame window and takes its Y range from [lo, hi], so the curve's
        // Duration/Warmup/Loop and Value-Range fields are hidden (not meaningful for a frame-baked blast).
        void ValRow(string label, ZUIValue v, float lo, float hi, float? def = null)
        {
            var o = ZUIValueControl.Options.Default.WithRange(lo, hi).WithoutCurveExtras();
            if (def.HasValue) o = o.WithDefault(def.Value);
            ZUIValueControl.Draw(label, v, o);
        }

        // The disc-like edge controls (Disc + SparkleField): always an outer-edge alpha gradient, and an optional
        // Hollow hole with its own size + inner-edge alpha gradient.
        void DrawDiscEdges(Layer l)
        {
            ValRow("Outer softness", l.outerSoftness, 0f, 1f, 0f);
            l.hollow = Toggle(l.hollow, "Hollow");
            if (discHollowLayout)   // Layout-captured gate so the control count is reflow-safe
            {
                ValRow("Hole size", l.holeSize, 0f, 1f, 0.5f);
                ValRow("Inner softness", l.innerSoftness, 0f, 1f, 0f);
                ValRow("Hole offset X", l.holeOffsetX, -1f, 1f, 0f);   // offset the hole = a crescent
                ValRow("Hole offset Y", l.holeOffsetY, -1f, 1f, 0f);
            }
        }

        static readonly string[] DissolveModeLabels = { "Erase", "Fade", "Bleed", "Scatter" };
        static readonly string[] MaskShapeLabels = { "Disc out", "Disc in", "Swipe H", "Swipe V", "Wedge" };
        static readonly string[] ColorModeLabels = { "Over life", "Fill", "Flow fill" };

        // ── modifier stack UI (Pyre v2): the opt-in geometry/pixel effects on a layer or globally ──────────
        // Shared by the Blast panel (global modifiers) and the Layer panel (per-layer). Removal is deferred to
        // after the loop so the control set is stable within a frame; adds come from a GenericMenu (also deferred).
        void DrawModifiers(System.Collections.Generic.List<PyreModifier> list, string idp)
        {
            if (list == null) return;
            float half = spec.canvasSize * 0.5f;
            int remove = -1;
            var rowRects = new System.Collections.Generic.List<Rect>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null) { remove = i; continue; }
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
                    GUILayout.FlexibleSpace();
                    if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) remove = i;
                    EditorGUILayout.EndHorizontal();
                    if (m.enabled) DrawModBody(m, half, idp + i);
                }
                rowRects.Add(GUILayoutUtility.GetLastRect());   // the whole modifier block (Box rect) = drop target
            }

            HandleModDrag(list, idp, rowRects);

            if (Button("+ Add modifier")) ShowAddModifierMenu(list, idp != null && idp.StartsWith("gm"));
            if (remove >= 0)
            {
                if (list[remove] == paintSmudge) { paintSmudge = null; draggingSmudge = false; }
                Undo.RecordObject(spec, "Remove modifier"); list.RemoveAt(remove); EditorUtility.SetDirty(spec);
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
                case SkewModifier s: ValRow("Amount", s.amount, -2f, 2f, 0f); break;
                case SquashModifier s: ValRow("Amount", s.amount, 0.3f, 3f, 1f); break;
                case RotateModifier r:
                    ValRow("Degrees", r.degrees, -180f, 180f, 0f);
                    r.pivotX = EditorGUILayout.Slider("Pivot X", r.pivotX, -1f, 1f);
                    r.pivotY = EditorGUILayout.Slider("Pivot Y", r.pivotY, -1f, 1f);
                    break;
                case WobbleModifier w:
                    ValRow("Amplitude", w.amplitude, 0f, Mathf.Max(4f, half), 0f);
                    ValRow("Frequency", w.frequency, 0f, 8f, 1f);
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
                case ContrastModifier cm: ValRow("Contrast", cm.amount, 0f, 2f, 1f); break;
                case BrightnessModifier bm: ValRow("Brightness", bm.amount, 0f, 2f, 1f); break;
                case SaturationModifier sm: ValRow("Saturation", sm.amount, 0f, 2f, 1f); break;
                case DissolveModifier d:
                    ValRow("Amount", d.amount, 0f, 1f, 0f);
                    d.mode = (DissolveMode)MiniRadio((int)d.mode, DissolveModeLabels);
                    break;
                case AlphaMaskModifier am:
                    am.shape = (MaskShape)MiniRadio((int)am.shape, MaskShapeLabels);
                    ValRow("Progress", am.progress, 0f, 1f, 1f);
                    am.sharpness = Slider(am.sharpness, 0f, 1f, "Sharpness");
                    ValRow("Size", am.size, 0.1f, 4f, 1f);
                    ValRow("Rotation", am.rotation, -180f, 180f, 0f);
                    am.offsetX = Slider(am.offsetX, -1f, 1f, "Offset X");
                    am.offsetY = Slider(am.offsetY, -1f, 1f, "Offset Y");
                    break;
                case BloomModifier bm:
                    bm.threshold = Slider(bm.threshold, 0f, 1f, "Threshold");
                    bm.radius = Mathf.RoundToInt(Slider(bm.radius, 0, 16, "Radius (px)"));
                    ValRow("Intensity", bm.intensity, 0f, 3f, 1.2f);
                    break;
                case OutlineModifier om:
                    om.color ??= new Gradient();
                    om.color = EditorGUILayout.GradientField("Colour (in→out)", om.color);
                    ValRow("Size (px)", om.size, 0f, 12f, 1f);
                    om.alphaThreshold = Slider(om.alphaThreshold, 0.01f, 1f, "Edge alpha");
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
                    ds.offsetX = Slider(ds.offsetX, -16f, 16f, "Offset X");
                    ds.offsetY = Slider(ds.offsetY, -16f, 16f, "Offset Y");
                    ds.color = EditorGUILayout.ColorField("Shadow colour", ds.color);
                    ds.alphaThreshold = Slider(ds.alphaThreshold, 0.01f, 1f, "Edge alpha");
                    break;
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
            Add("Geometry/Skew", () => new SkewModifier());
            Add("Geometry/Rotate", () => new RotateModifier());
            Add("Geometry/Squash", () => new SquashModifier());
            Add("Geometry/Wobble", () => new WobbleModifier());
            Add("Geometry/Profile (mold shape)", () => new ProfileModifier());
            Add("Geometry/Ground (grow from surface)", () => new GroundModifier());
            Add("Geometry/Jagg (star)", () => new JaggModifier());
            Add("Geometry/Smudge", () => new SmudgeModifier());
            Add("Colour/Tint", () => new TintModifier());
            Add("Colour/Contrast", () => new ContrastModifier());
            Add("Colour/Brightness", () => new BrightnessModifier());
            Add("Colour/Saturation", () => new SaturationModifier());
            Add("Dissolve", () => new DissolveModifier());
            Add("Alpha mask", () => new AlphaMaskModifier());
            // Whole-frame post effects only make sense on the blast's GLOBAL list (they run once after compositing).
            if (isGlobal)
            {
                Add("Post/Bloom (glow)", () => new BloomModifier());
                Add("Post/Outline", () => new OutlineModifier());
                Add("Post/Drop shadow", () => new DropShadowModifier());
            }
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
                    var local = new Rect((view.width - w) * 0.5f + previewPan.x, (view.height - h) * 0.5f + previewPan.y, w, h);
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
            // owns the drag entirely; (1) MetaBlob orbs when editing a MetaBlob layer (place / drag) — it must win
            // over the centred origin ✛, or placing near the centre would grab the pivot instead; (2) origin ✛
            // handle, (3) a stage sprite, (4) fall through to panning the frame. Each Use()s its event.
            HandleSmudgePaint(view);
            HandleMetaBlob(view);
            DrawMetaOrbMarkers(view);
            DrawSmudgeStroke(view);
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
            if (Button("⟲ Restart")) { frame = 0; acc = 0f; scrub = -1; }
            if (Button("Fit")) { FitZoom(); previewPan = Vector2.zero; }
            if (Button("Centre")) previewPan = Vector2.zero;
            showFrame = Toggle(showFrame, "Frame");
            if (Button("Bake")) BlastBaker.Bake(spec);
            EditorGUILayout.EndHorizontal();

            // frame scrubber — dragging it pauses playback and holds the frame
            int shown = scrub >= 0 ? scrub : frame;
            int scrubbed = Mathf.RoundToInt(Slider(shown, 0, Mathf.Max(0, FrameCount - 1), "Frame"));
            if (scrubbed != shown) { scrub = scrubbed; playing = false; Repaint(); }

            // retime: same layers, different number of frames
            int fc = Mathf.RoundToInt(Slider(spec.frameCount, 1, 64, "Frame count"));
            if (fc != spec.frameCount) { spec.frameCount = Mathf.Max(1, fc); EditorUtility.SetDirty(spec); frame = Mathf.Min(frame, FrameCount - 1); }

            zoom = Mathf.Max(1f, Mathf.Round(Slider(zoom, 1f, 16f, "Zoom")));
            speed = Slider(speed, 0.1f, 3f, "Speed");
            Label($"frame {cur + 1} / {FrameCount}", ZUI.ZTextStyle.Subtle);

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
            Rect spr = new Rect(view.x + (view.width - w) * 0.5f + previewPan.x, view.y + (view.height - h) * 0.5f + previewPan.y, w, h);
            float ox = spr.x + Mathf.Clamp01(spec.origin.x) * w;
            float oy = spr.yMax - Mathf.Clamp01(spec.origin.y) * h;   // origin.y = 0 is the bottom

            var e = Event.current;
            Rect zone = new Rect(ox - 8f, oy - 8f, 16f, 16f);
            if (view.Contains(new Vector2(ox, oy))) EditorGUIUtility.AddCursorRect(zone, MouseCursor.MoveArrow);

            if (e.type == EventType.MouseDown && e.button == 0 && zone.Contains(e.mousePosition) && view.Contains(e.mousePosition))
            { draggingOrigin = true; e.Use(); }
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
                    if ((mp - e.mousePosition).sqrMagnitude <= 100f) { metaSel = i; draggingMetaOrb = true; e.Use(); break; }
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
            using (Box("Preview backdrop (not baked)"))
            {
                bgMode = (PreviewBgMode)EditorGUILayout.EnumPopup("Mode", bgMode);
                switch (bgMode)
                {
                    case PreviewBgMode.Solid:
                        bgSolid = EditorGUILayout.ColorField("Colour", bgSolid);
                        break;
                    case PreviewBgMode.Gradient:
                        bgGradient ??= DefaultBgGradient();
                        bgGradient = EditorGUILayout.GradientField("Gradient", bgGradient);
                        break;
                    case PreviewBgMode.Image:
                        bgImage = (Texture2D)EditorGUILayout.ObjectField("Image", bgImage, typeof(Texture2D), false);
                        bgImageTint = EditorGUILayout.ColorField("Tint", bgImageTint);
                        bgImageZoom = EditorGUILayout.Slider("Zoom", bgImageZoom, 0.1f, 8f);
                        break;
                }
            }

            DrawTestBackground();
        }

        // Reusable sprite test-backdrop (PreviewStage): compose the effect against props (floor/wall/…). Saved
        // separately from the frame position, and recallable across tools.
        [SerializeField] string stageSaveName = "PreviewBg";
        void DrawTestBackground()
        {
            using (Box("Test background (sprites)"))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(stageBg != null ? (AssetDatabase.Contains(stageBg) ? stageBg.name : "· unsaved ·") : "· none ·",
                                EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (Button("Recall…"))
                    PreviewStageGUI.ShowRecall(GUILayoutUtility.GetLastRect(), b => { stageBg = b; stageSel = -1; Repaint(); });
                if (Button("New")) { stageBg = ScriptableObject.CreateInstance<PreviewBackground>(); stageSel = -1; }
                EditorGUILayout.EndHorizontal();

                if (stageBg == null) { Label("Recall a backdrop or hit New to build one.", ZUI.ZTextStyle.Subtle); return; }

                stageBg.fill = EditorGUILayout.ColorField("Fill (α0 = overlay)", stageBg.fill);

                int removeAt = -1;
                for (int i = 0; i < stageBg.sprites.Count; i++)
                {
                    var s = stageBg.sprites[i];
                    using (Box())
                    {
                        EditorGUILayout.BeginHorizontal();
                        bool sel = stageSel == i;
                        if (Button(sel ? "●" : "○", ZUI.Style.Default, GUILayout.Width(24))) stageSel = i;
                        s.sprite = (Sprite)EditorGUILayout.ObjectField(s.sprite, typeof(Sprite), false);
                        s.front = GUILayout.Toggle(s.front, "Front", "Button", GUILayout.Width(48));
                        if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) removeAt = i;
                        EditorGUILayout.EndHorizontal();
                        s.scale = EditorGUILayout.Slider("Scale", s.scale, 0.1f, 8f);
                        s.tint = EditorGUILayout.ColorField("Tint", s.tint);
                        s.position = EditorGUILayout.Vector2Field("Position", s.position);
                    }
                }
                if (removeAt >= 0) { stageBg.sprites.RemoveAt(removeAt); stageSel = -1; }

                EditorGUILayout.BeginHorizontal();
                if (Button("+ Add sprite")) { stageBg.sprites.Add(new StageSprite()); stageSel = stageBg.sprites.Count - 1; }
                if (Button("Clear")) { stageBg.sprites.Clear(); stageSel = -1; }
                GUILayout.FlexibleSpace();
                stageSaveName = EditorGUILayout.TextField(stageSaveName, GUILayout.Width(110));
                if (Button("Save")) { PreviewStageGUI.Save(ref stageBg, stageSaveName); }
                EditorGUILayout.EndHorizontal();

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
                        var imgRect = new Rect((view.width - w) * 0.5f, (view.height - h) * 0.5f, w, h);
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
        // ── Sprite particles: create a starter PNG + open/edit it in Aseprite (like Zoetrope) ──────────────
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
                Debug.LogWarning($"[Pyre] could not open Aseprite ({e.Message}). Set the path via Tools ▸ Zoetrope ▸ Set Aseprite Path…");
            }
        }

        // Shares Zoetrope's saved Aseprite path (EditorPref) so it's set once for the whole library.
        static string ResolveAsepriteExe()
        {
            string p = EditorPrefs.GetString("Zoetrope.AsepritePath", "");
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
