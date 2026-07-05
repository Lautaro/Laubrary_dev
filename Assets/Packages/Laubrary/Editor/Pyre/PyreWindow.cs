using UnityEditor;
using UnityEngine;
using Laubrary.Pyre;

namespace Laubrary.Pyre.Editor
{
    /// Pyre: author a pixel-art explosion as a flat back-to-front stack of Layers and watch it loop live. Left =
    /// a drag-resizable pane with the blast dials, a Layer list (select / enable / dup / delete / add) and the
    /// selected layer's inspector (animatable values via ZUIValueControl); right = a resizable preview viewport
    /// with a chooseable backdrop, a zoom, and play / scrub / retime / speed transport. The preview draws the exact
    /// same BlastRenderer frames the baker and the runtime player use, so preview == bake == runtime.
    public class PyreWindow : ZUIWindow
    {
        [MenuItem("Laubrary/Pyre/Pyre")]
        public static void Open() => GetWindow<PyreWindow>("Pyre");

        [SerializeField] BlastSpec spec;

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

        // add-layer shape picker
        [SerializeField] LayerShape addShape = LayerShape.Disc;

        int layerSel;
        Vector2 leftScroll;

        // splitters
        bool dragLeft, dragPreview;
        int draggingLayer = -1;   // index of the layer being drag-reordered, or -1

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

        static readonly string[] ShapeLabels = { "Disc", "Ring", "Dissolve", "Sparkle", "Crescent" };

        protected override void OnZUIEnable()
        {
            lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            bgGradient ??= DefaultBgGradient();
        }

        void OnDisable()
        {
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
        }

        protected override void OnZUI()
        {
            DrawTopBar();
            if (spec == null)
            {
                VerticalSpace();
                Label("Pick or create a BlastSpec, or hit \"New example blast\" for a ready-made explosion.",
                    ZUI.ZTextStyle.Subtle);
                if (Button("New example blast")) NewExample();
                return;
            }

            EditorGUIUtility.labelWidth = 92f;

            EditorGUILayout.BeginHorizontal();
            DrawLeft();
            DrawVerticalSplitter();
            DrawPreview();
            EditorGUILayout.EndHorizontal();

            // Any edit (layer toggle, a dial, a deform value…) must rebuild the preview even while paused —
            // the preview texture is only regenerated on Repaint, so schedule one whenever something changed.
            if (GUI.changed) Repaint();
        }

        // ── top bar ────────────────────────────────────────────────────────────
        void DrawTopBar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            spec = (BlastSpec)EditorGUILayout.ObjectField(spec, typeof(BlastSpec), false, GUILayout.Width(220));
            if (EditorGUI.EndChangeCheck()) { frame = 0; layerSel = 0; scrub = -1; Repaint(); }
            if (Button("New asset")) CreateAsset();
            if (spec != null && Button("New example blast")) NewExample();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ── left: dials + layer list + selected-layer inspector ──────────────────
        void DrawLeft()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            EditorGUI.BeginChangeCheck();

            Label("Blast", ZUI.ZTextStyle.SectionHeader);
            spec.seed = EditorGUILayout.IntField("Seed", spec.seed);
            spec.canvasSize = Mathf.Clamp(EditorGUILayout.IntField("Canvas size", spec.canvasSize), 4, 256);
            spec.pixelsPerUnit = Mathf.Max(1f, EditorGUILayout.FloatField("Pixels per unit", spec.pixelsPerUnit));
            spec.background = EditorGUILayout.ColorField("Bake background", spec.background);
            spec.frameCount = Mathf.Max(1, Mathf.RoundToInt(Slider(spec.frameCount, 1, 64, "Frame count")));

            VerticalSpace();
            DrawGlobalDeform();

            VerticalSpace();
            DrawLayerList();

            VerticalSpace();
            DrawSelectedLayer();

            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(spec); frame = Mathf.Min(frame, FrameCount - 1); }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawGlobalDeform()
        {
            using (Box("Global deform"))
            {
                spec.deformEnabled = Toggle(spec.deformEnabled, "Enable global deform");
                if (spec.deformEnabled) DrawDeformFields(spec.squash, spec.skew, spec.wobbleAmplitude,
                                                          spec.wobbleFrequency, spec.rotation);
            }
        }

        // The 5 shared deform controls (used for both the global block and the per-layer block).
        void DrawDeformFields(ZUIValue squash, ZUIValue skew, ZUIValue wobAmp, ZUIValue wobFreq, ZUIValue rot)
        {
            float half = spec.canvasSize * 0.5f;
            ValRow("Squash", squash, 0.3f, 3f, 1f);
            ValRow("Skew", skew, -2f, 2f, 0f);
            ValRow("Rotation", rot, -180f, 180f, 0f);
            ValRow("Wobble amp", wobAmp, 0f, half, 0f);
            ValRow("Wobble freq", wobFreq, 0f, 8f, 1f);
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
            EditorGUILayout.EndHorizontal();

            int dup = -1, remove = -1;
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
                string nm = (sel ? "● " : "○ ") + layer.name + (layer.enabled ? "" : "  (off)");
                if (Button(nm)) layerSel = li;
                if (Button("Dup", ZUI.Style.Default, GUILayout.Width(40))) dup = li;
                if (Button("X", ZUI.Style.Default, GUILayout.Width(22))) remove = li;
                EditorGUILayout.EndHorizontal();
            }

            HandleLayerDrag(rowRects);

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

            l.name = EditorGUILayout.TextField("Name", l.name);
            l.shape = (LayerShape)MiniRadio((int)l.shape, ShapeLabels);

            // Life window as a min-max range that can't exceed the current frame count.
            int fcMax = Mathf.Max(1, FrameCount - 1);
            float sf = Mathf.Clamp(l.startFrame, 0, fcMax);
            float ef = Mathf.Clamp(l.endFrame, 0, fcMax);
            MicroMinMax(ref sf, ref ef, 0f, fcMax, "Life (frames)", ZUI.SliderStyle.Default, true);
            l.startFrame = Mathf.RoundToInt(sf);
            l.endFrame = Mathf.Clamp(Mathf.RoundToInt(ef), l.startFrame, fcMax);

            ValRow("Count", l.count, 1f, 40f);
            ValRow("Spawn radius", l.spawnRadius, 0f, 1f);
            ValRow("Position X", l.positionX, -half, half);
            ValRow("Position Y", l.positionY, -half, half);
            ValRow("Start size", l.startSize, 0f, half);
            ValRow("End size", l.endSize, 0f, half);

            l.perShapeLifeJitter = Slider(l.perShapeLifeJitter, 0f, 1f, "Life jitter");
            l.disintegrate = Slider(l.disintegrate, 0f, 1f, "Disintegrate");

            switch (l.shape)
            {
                case LayerShape.Ring:
                    l.ringThickness = Slider(l.ringThickness, 1f, 12f, "Ring thickness");
                    break;
                case LayerShape.DissolvingDisc:
                    l.dissolveCenter = Slider(l.dissolveCenter, 0f, 1f, "Dissolve centre");
                    l.dissolveKeepBorder = Toggle(l.dissolveKeepBorder, "Keep border");
                    break;
                case LayerShape.SparkleField:
                    l.sparkleDensity = Slider(l.sparkleDensity, 0f, 1f, "Sparkle density");
                    break;
                case LayerShape.Crescent:
                    ValRow("Crescent X", l.crescentOffsetX, -half, half);
                    ValRow("Crescent Y", l.crescentOffsetY, -half, half);
                    break;
            }

            l.colorOverLife ??= Layer.DefaultColor(l.shape);
            l.alphaOverLife ??= Layer.DefaultAlpha();
            l.colorOverLife = EditorGUILayout.GradientField("Colour over life", l.colorOverLife);
            CurveField("pyre.alpha." + layerSel, "Alpha over life", l.alphaOverLife, 0f, 1f);

            using (Box("Layer deform"))
            {
                l.deformEnabled = Toggle(l.deformEnabled, "Enable layer deform");
                if (l.deformEnabled) DrawDeformFields(l.deformSquash, l.deformSkew, l.deformWobbleAmplitude,
                                                      l.deformWobbleFrequency, l.deformRotation);
            }
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
                UpdatePreviewTexture(cur);
                if (previewTex != null)
                {
                    float w = spec.canvasSize * zoom, h = spec.canvasSize * zoom;
                    GUI.BeginClip(view);
                    var local = new Rect((view.width - w) * 0.5f, (view.height - h) * 0.5f, w, h);
                    GUI.DrawTexture(local, previewTex, ScaleMode.StretchToFill, true);
                    GUI.EndClip();
                }
            }

            DrawPreviewSplitter();

            // transport
            EditorGUILayout.BeginHorizontal();
            if (Button(playing ? "❚❚ Pause" : "▶ Play")) { playing = !playing; scrub = -1; }
            if (Button("⟲ Restart")) { frame = 0; acc = 0f; scrub = -1; }
            if (Button("Fit")) FitZoom();
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
                        float w = view.width * bgImageZoom, h = view.height * bgImageZoom;
                        var imgRect = new Rect((view.width - w) * 0.5f, (view.height - h) * 0.5f, w, h);
                        GUI.DrawTexture(imgRect, bgImage, ScaleMode.ScaleAndCrop, false);
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
            float z = Mathf.Floor(Mathf.Min(lastView.width, lastView.height) / Mathf.Max(1, spec.canvasSize));
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
        void NewExample()
        {
            if (spec == null)
            {
                spec = CreateInstance<BlastSpec>();
                spec.AddExampleContent();
            }
            else
            {
                Undo.RecordObject(spec, "Pyre example content");
                spec.AddExampleContent();
                EditorUtility.SetDirty(spec);
            }
            frame = 0; acc = 0f; scrub = -1; layerSel = 0;
            Repaint();
        }

        void CreateAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("New Blast", "Blast", "asset", "");
            if (string.IsNullOrEmpty(path)) return;
            var s = CreateInstance<BlastSpec>();
            s.AddExampleContent();
            AssetDatabase.CreateAsset(s, path);
            AssetDatabase.SaveAssets();
            spec = s; frame = 0; layerSel = 0; scrub = -1;
        }
    }
}
