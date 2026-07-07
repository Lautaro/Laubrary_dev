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
        [SerializeField] bool showFrame = false;          // draw the sprite-frame (canvas) border in the preview

        // add-layer shape picker
        [SerializeField] LayerShape addShape = LayerShape.Disc;

        int layerSel;
        Vector2 leftScroll;

        // splitters
        bool dragLeft, dragPreview;
        bool draggingOrigin;   // dragging the origin/pivot ✛ handle in the preview
        [SerializeField] float originMarkerAlpha = 0.95f;   // preview-only: origin ✛ opacity
        int draggingLayer = -1;   // index of the layer being drag-reordered, or -1
        int draggingMod = -1;     // index of the modifier being drag-reordered, or -1
        string draggingModList;   // idp of the modifier list that drag belongs to (layer mods vs global mods)

        // browser ("blast library") mode: the left pane becomes a grid of every BlastSpec in the project
        bool browsing;
        Vector2 browseScroll;
        string[] browseGuids;
        readonly System.Collections.Generic.Dictionary<string, Texture2D> browseThumbs = new();

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

        static readonly string[] ShapeLabels = { "Disc", "Crescent", "Sparkle", "Bars", "Sprite" };
        static readonly string[] BarDecayLabels = { "Contract", "Dissolve" };

        // Captured on the Layout event only so the control set can't change between Layout and Repaint of the same
        // frame (IMGUI reflow hazard). starLayout = any Bars layer has Star (gates the auto-canvas readout);
        // barStarLayout = the SELECTED layer is a star Bars layer (gates its Arms/Spread rows). See OnZUI.
        bool starLayout, barStarLayout, discHollowLayout;

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
            ClearBrowseThumbs();
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
            else if (spec != null && !browsing && originMarkerAlpha > 0.001f) Repaint();
        }

        protected override void OnZUI()
        {
            // Capture the star gate on Layout only, so the width/height block below has a stable control count
            // across this frame's Layout and Repaint passes even if the Orbit/Star radio is clicked.
            if (Event.current.type == EventType.Layout)
            {
                starLayout = AnyStarLayer();
                var sel = spec != null && layerSel >= 0 && layerSel < spec.layers.Count ? spec.layers[layerSel] : null;
                barStarLayout = sel != null && sel.shape == LayerShape.Bars && sel.star;
                discHollowLayout = sel != null && (sel.shape == LayerShape.Disc || sel.shape == LayerShape.SparkleField) && sel.hollow;
            }

            DrawTopBar();

            // Browser mode: the left dials/layers pane is swapped for a grid of every blast; the preview stays.
            if (browsing)
            {
                EditorGUILayout.BeginHorizontal();
                DrawBrowser();
                DrawVerticalSplitter();
                DrawPreview();
                EditorGUILayout.EndHorizontal();
                if (GUI.changed) Repaint();
                return;
            }

            if (spec == null)
            {
                VerticalSpace();
                Label("Pick a BlastSpec above, or hit \"New asset\" to create one — or \"Browse\" the library.",
                    ZUI.ZTextStyle.Subtle);
                return;
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

        // ── top bar ────────────────────────────────────────────────────────────
        bool renaming;
        string renameText = "";
        bool creating;
        string createText = "";
        bool focusNewField;

        void DrawTopBar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            spec = (BlastSpec)EditorGUILayout.ObjectField(spec, typeof(BlastSpec), false, GUILayout.Width(200));
            if (EditorGUI.EndChangeCheck()) { frame = 0; layerSel = 0; scrub = -1; renaming = false; creating = false; Repaint(); }
            if (Button("New asset")) { creating = true; renaming = false; createText = "New Pyre"; focusNewField = true; }
            if (Button(browsing ? "Close browser" : "Browse")) { browsing = !browsing; renaming = false; creating = false; if (browsing) RefreshBrowse(); }

            string path = spec != null ? AssetDatabase.GetAssetPath(spec) : null;
            bool isAsset = !string.IsNullOrEmpty(path);
            if (isAsset)
            {
                if (Button("Duplicate")) DuplicateAsset(path);
                if (Button("Rename")) { renaming = !renaming; creating = false; renameText = System.IO.Path.GetFileNameWithoutExtension(path); }
                if (Button("Delete")) DeleteAsset(path);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Inline name prompt for a brand-new asset — no file dialog; it's saved beside the current spec (or in
            // Assets/Pyre) under the typed name.
            if (creating)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Asset name", GUILayout.Width(72));
                bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                GUI.SetNextControlName("PyreNewAssetField");
                createText = EditorGUILayout.TextField(createText, GUILayout.Width(200));
                if (focusNewField && Event.current.type == EventType.Repaint)
                { EditorGUI.FocusTextInControl("PyreNewAssetField"); focusNewField = false; }
                if (Button("Create") || enter) CreateAssetNamed(createText);
                if (Button("Cancel")) creating = false;
                EditorGUILayout.EndHorizontal();
            }

            // Inline rename row (a modal text prompt isn't worth it in IMGUI).
            if (isAsset && renaming)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("New name", GUILayout.Width(72));
                bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                renameText = EditorGUILayout.TextField(renameText, GUILayout.Width(200));
                if (Button("OK") || enter) { RenameAsset(path, renameText); renaming = false; }
                if (Button("Cancel")) renaming = false;
                EditorGUILayout.EndHorizontal();
            }
        }

        void DuplicateAsset(string path)
        {
            string copy = AssetDatabase.GenerateUniqueAssetPath(path);
            if (AssetDatabase.CopyAsset(path, copy))
            {
                AssetDatabase.SaveAssets();
                var s = AssetDatabase.LoadAssetAtPath<BlastSpec>(copy);
                if (s != null) { spec = s; frame = 0; layerSel = 0; scrub = -1; }
                Repaint();
            }
        }

        void RenameAsset(string path, string newName)
        {
            newName = newName?.Trim();
            if (string.IsNullOrEmpty(newName)) return;
            string err = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(err)) Debug.LogWarning($"[Pyre] rename failed: {err}");
            else AssetDatabase.SaveAssets();
            Repaint();
        }

        void DeleteAsset(string path)
        {
            if (!EditorUtility.DisplayDialog("Delete BlastSpec",
                    $"Delete '{System.IO.Path.GetFileName(path)}'? This cannot be undone.", "Delete", "Cancel"))
                return;
            if (AssetDatabase.DeleteAsset(path)) { spec = null; renaming = false; layerSel = 0; scrub = -1; Repaint(); }
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
            }
        }

        static readonly string[] DissolveModeLabels = { "Erase", "Fade", "Bleed", "Scatter" };
        static readonly string[] MaskShapeLabels = { "Disc out", "Disc in", "Swipe H", "Swipe V" };
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
            if (remove >= 0) { Undo.RecordObject(spec, "Remove modifier"); list.RemoveAt(remove); EditorUtility.SetDirty(spec); }
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
        // ── browser: a grid of every BlastSpec in the project, in place of the dials/layers pane ──────────────
        void DrawBrowser()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
            if (browseGuids == null) RefreshBrowse();

            EditorGUILayout.BeginHorizontal();
            Label($"Blast library ({browseGuids.Length})", ZUI.ZTextStyle.SectionHeader);
            GUILayout.FlexibleSpace();
            if (Button("Refresh")) RefreshBrowse();
            EditorGUILayout.EndHorizontal();

            // actions on the currently-previewed blast
            string selPath = spec != null ? AssetDatabase.GetAssetPath(spec) : null;
            if (!string.IsNullOrEmpty(selPath))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(System.IO.Path.GetFileNameWithoutExtension(selPath), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (Button("Edit")) { browsing = false; frame = 0; layerSel = 0; scrub = -1; }
                if (Button("Dup")) { DuplicateAsset(selPath); RefreshBrowse(); }
                if (Button("Rename")) { renaming = !renaming; renameText = System.IO.Path.GetFileNameWithoutExtension(selPath); }
                if (Button("Del")) { DeleteAsset(selPath); RefreshBrowse(); }
                EditorGUILayout.EndHorizontal();

                if (renaming)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("New name", GUILayout.Width(72));
                    bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                    renameText = EditorGUILayout.TextField(renameText, GUILayout.Width(160));
                    if (Button("OK") || enter) { RenameAsset(selPath, renameText); renaming = false; RefreshBrowse(); }
                    if (Button("Cancel")) renaming = false;
                    EditorGUILayout.EndHorizontal();
                }
            }
            else Label("Click a blast to preview it; double-click (or Edit) to open it.", ZUI.ZTextStyle.Subtle);

            browseScroll = EditorGUILayout.BeginScrollView(browseScroll);
            const float cell = 104f, thumb = 92f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((leftWidth - 16f) / cell));
            int i = 0;
            while (i < browseGuids.Length)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < cols && i < browseGuids.Length; c++, i++)
                {
                    var guid = browseGuids[i];
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var s = AssetDatabase.LoadAssetAtPath<BlastSpec>(path);
                    if (s != null) DrawBrowseCell(guid, s, cell, thumb);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawBrowseCell(string guid, BlastSpec s, float cell, float thumb)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(cell));
            bool selected = spec == s;

            Rect tr = GUILayoutUtility.GetRect(thumb, thumb, GUILayout.Width(thumb), GUILayout.Height(thumb));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(tr, selected ? new Color(0.35f, 0.55f, 0.95f, 0.35f) : new Color(0.11f, 0.12f, 0.15f));
                var tex = BrowseThumb(guid, s);
                if (tex != null)
                {
                    float sc = Mathf.Min((thumb - 6f) / Mathf.Max(1, tex.width), (thumb - 6f) / Mathf.Max(1, tex.height));
                    float w = tex.width * sc, h = tex.height * sc;
                    GUI.DrawTexture(new Rect(tr.x + (thumb - w) * 0.5f, tr.y + (thumb - h) * 0.5f, w, h), tex, ScaleMode.StretchToFill, true);
                }
                if (selected)
                {
                    var b = new Color(0.4f, 0.8f, 1f, 0.9f);
                    EditorGUI.DrawRect(new Rect(tr.x, tr.y, tr.width, 1f), b);
                    EditorGUI.DrawRect(new Rect(tr.x, tr.yMax - 1f, tr.width, 1f), b);
                    EditorGUI.DrawRect(new Rect(tr.x, tr.y, 1f, tr.height), b);
                    EditorGUI.DrawRect(new Rect(tr.xMax - 1f, tr.y, 1f, tr.height), b);
                }
            }
            EditorGUIUtility.AddCursorRect(tr, MouseCursor.Link);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && tr.Contains(e.mousePosition))
            {
                bool open = e.clickCount == 2;
                spec = s; frame = 0; scrub = -1; renaming = false;
                if (open) { browsing = false; layerSel = 0; }
                e.Use(); Repaint();
            }
            GUILayout.Label(new GUIContent(s.name, s.name), EditorStyles.miniLabel, GUILayout.Width(thumb));
            EditorGUILayout.EndVertical();
        }

        // Cached representative-frame thumbnail per blast (rendered once through the real BlastRenderer).
        Texture2D BrowseThumb(string guid, BlastSpec s)
        {
            if (browseThumbs.TryGetValue(guid, out var t) && t != null) return t;
            int mid = Mathf.Clamp(s.frameCount / 2, 0, Mathf.Max(0, s.frameCount - 1));
            var tex = BlastRenderer.RenderFrameTexture(s, mid);
            tex.filterMode = FilterMode.Point;
            browseThumbs[guid] = tex;
            return tex;
        }

        void RefreshBrowse()
        {
            ClearBrowseThumbs();
            browseGuids = AssetDatabase.FindAssets("t:BlastSpec");
        }

        void ClearBrowseThumbs()
        {
            foreach (var t in browseThumbs.Values) if (t != null) DestroyImmediate(t);
            browseThumbs.Clear();
        }

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
                    var local = new Rect((view.width - w) * 0.5f, (view.height - h) * 0.5f, w, h);
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
            }

            // Origin/pivot ✛ handle — drag it to set where the blast anchors to a spawn point (the hit pixel).
            if (spec != null) DrawOriginHandle(view);

            DrawPreviewSplitter();

            // transport
            EditorGUILayout.BeginHorizontal();
            if (Button(playing ? "❚❚ Pause" : "▶ Play")) { playing = !playing; scrub = -1; }
            if (Button("⟲ Restart")) { frame = 0; acc = 0f; scrub = -1; }
            if (Button("Fit")) FitZoom();
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
            Rect spr = new Rect(view.x + (view.width - w) * 0.5f, view.y + (view.height - h) * 0.5f, w, h);
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

        // Create + save a new BlastSpec under the typed name, no file dialog. Saved beside the current spec if there
        // is one, otherwise in Assets/Pyre (created on demand).
        void CreateAssetNamed(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) name = "New Pyre";

            string dir = "Assets/Pyre";
            if (spec != null)
            {
                string cur = AssetDatabase.GetAssetPath(spec);
                if (!string.IsNullOrEmpty(cur)) dir = System.IO.Path.GetDirectoryName(cur).Replace('\\', '/');
            }
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Pyre");

            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{name}.asset");
            var s = CreateInstance<BlastSpec>();
            s.AddExampleContent();
            AssetDatabase.CreateAsset(s, path);
            AssetDatabase.SaveAssets();
            spec = s; frame = 0; layerSel = 0; scrub = -1; creating = false;
            Repaint();
        }
    }
}
