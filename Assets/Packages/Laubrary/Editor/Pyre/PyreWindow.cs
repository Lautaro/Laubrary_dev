using UnityEditor;
using UnityEngine;
using Laubrary.Pyre;

namespace Laubrary.Pyre.Editor
{
    /// Pyre: author a pixel-art explosion as stacked Layers of timed Waves and watch it loop live. Left = the
    /// blast dials and a layer/wave list editor; right = a live preview that plays through the frames, a frame
    /// scrubber, a frame-count slider to retime, and play/speed controls. The preview draws the exact same
    /// BlastRenderer frames the baker and the runtime player use, so preview == bake == runtime.
    public class PyreWindow : ZUIWindow
    {
        [MenuItem("Laubrary/Pyre/Pyre")]
        public static void Open() => GetWindow<PyreWindow>("Pyre");

        [SerializeField] BlastSpec spec;

        int layerSel, waveSel;
        Vector2 leftScroll;

        // playback
        double lastTime;
        float acc;
        int frame;
        bool playing = true;
        float fps = 12f;
        float speed = 1f;
        int scrub = -1;                 // >=0 means the user is holding a scrubbed frame (paused)
        Texture2D previewTex;

        static readonly string[] ShapeLabels = { "Disc", "Ring", "Dissolve", "Sparkle", "Crescent" };
        static readonly string[] SpawnLabels = { "Instant", "FadeIn", "GrowIn" };
        static readonly string[] EndLabels = { "Instant", "FadeOut", "Disint.", "Shrink" };

        protected override void OnZUIEnable()
        {
            lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
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

            EditorGUILayout.BeginHorizontal();
            DrawLeft(GUILayout.Width(340));
            DrawPreview();
            EditorGUILayout.EndHorizontal();
        }

        // ── top bar ────────────────────────────────────────────────────────────
        void DrawTopBar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            spec = (BlastSpec)EditorGUILayout.ObjectField(spec, typeof(BlastSpec), false, GUILayout.Width(220));
            if (EditorGUI.EndChangeCheck()) { frame = 0; layerSel = waveSel = 0; Repaint(); }
            if (Button("New asset")) CreateAsset();
            if (spec != null && Button("New example blast")) NewExample();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ── left: dials + layer/wave list ────────────────────────────────────────
        void DrawLeft(params GUILayoutOption[] opt)
        {
            EditorGUILayout.BeginVertical(opt);
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

            Label("Blast", ZUI.ZTextStyle.SectionHeader);
            EditorGUI.BeginChangeCheck();
            spec.seed = EditorGUILayout.IntField("Seed", spec.seed);
            spec.canvasSize = Mathf.Clamp(EditorGUILayout.IntField("Canvas size", spec.canvasSize), 4, 256);
            spec.pixelsPerUnit = Mathf.Max(1f, EditorGUILayout.FloatField("Pixels per unit", spec.pixelsPerUnit));
            spec.background = EditorGUILayout.ColorField("Background", spec.background);
            spec.frameCount = Mathf.Max(1, Mathf.RoundToInt(Slider(spec.frameCount, 1, 64, "Frame count")));
            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(spec); frame = Mathf.Min(frame, FrameCount - 1); }

            VerticalSpace();
            Label("Deform", ZUI.ZTextStyle.SectionHeader);
            EditorGUI.BeginChangeCheck();
            spec.squash = Slider(spec.squash, 0.3f, 3f, "Squash");
            spec.skew = Slider(spec.skew, -2f, 2f, "Skew");
            spec.wobbleAmplitude = Slider(spec.wobbleAmplitude, 0f, 10f, "Wobble amp");
            spec.wobbleFrequency = Slider(spec.wobbleFrequency, 0f, 8f, "Wobble freq");
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(spec);

            VerticalSpace();
            DrawLayers();
            VerticalSpace();
            DrawSelectedWave();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawLayers()
        {
            Label("Layers (back → front)", ZUI.ZTextStyle.SectionHeader);
            if (Button("+ Layer")) { Undo.RecordObject(spec, "Add layer"); spec.layers.Add(new Layer { name = "Layer " + spec.layers.Count }); EditorUtility.SetDirty(spec); }

            int removeLayer = -1;
            for (int li = 0; li < spec.layers.Count; li++)
            {
                var layer = spec.layers[li];
                using (Box(layer.name))
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.BeginHorizontal();
                    layer.name = EditorGUILayout.TextField(layer.name);
                    layer.visible = Toggle(layer.visible, "Vis");
                    if (Button("X")) removeLayer = li;
                    EditorGUILayout.EndHorizontal();

                    int removeWave = -1;
                    for (int wi = 0; wi < layer.waves.Count; wi++)
                    {
                        var wave = layer.waves[wi];
                        EditorGUILayout.BeginHorizontal();
                        bool sel = li == layerSel && wi == waveSel;
                        if (Button((sel ? "● " : "○ ") + wave.name)) { layerSel = li; waveSel = wi; }
                        if (Button("x")) removeWave = wi;
                        EditorGUILayout.EndHorizontal();
                    }

                    if (Button("+ Wave"))
                    {
                        layer.waves.Add(Wave.Default(WaveShape.Disc));
                        layerSel = li; waveSel = layer.waves.Count - 1;
                    }
                    if (removeWave >= 0) layer.waves.RemoveAt(removeWave);
                    if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(spec);
                }
            }
            if (removeLayer >= 0) { Undo.RecordObject(spec, "Remove layer"); spec.layers.RemoveAt(removeLayer); layerSel = waveSel = 0; EditorUtility.SetDirty(spec); }
        }

        void DrawSelectedWave()
        {
            if (layerSel < 0 || layerSel >= spec.layers.Count) return;
            var layer = spec.layers[layerSel];
            if (waveSel < 0 || waveSel >= layer.waves.Count) return;
            var w = layer.waves[waveSel];

            Label($"Wave — {w.name}", ZUI.ZTextStyle.SectionHeader);
            EditorGUI.BeginChangeCheck();

            w.name = EditorGUILayout.TextField("Name", w.name);
            w.shape = (WaveShape)MiniRadio((int)w.shape, ShapeLabels);

            EditorGUILayout.BeginHorizontal();
            w.startFrame = EditorGUILayout.IntField("Start", w.startFrame);
            w.endFrame = EditorGUILayout.IntField("End", w.endFrame);
            EditorGUILayout.EndHorizontal();

            w.count = Mathf.Max(1, Mathf.RoundToInt(Slider(w.count, 1, 64, "Count")));
            w.spawnRadius = Slider(w.spawnRadius, 0f, 32f, "Spawn radius");
            w.posJitter = Slider(w.posJitter, 0f, 16f, "Pos jitter");
            w.startSize = Slider(w.startSize, 0f, 40f, "Start size");
            w.endSize = Slider(w.endSize, 0f, 40f, "End size");
            w.spawnMode = (SpawnMode)MiniRadio((int)w.spawnMode, SpawnLabels);
            w.endMode = (EndMode)MiniRadio((int)w.endMode, EndLabels);
            w.perShapeLifeJitter = Slider(w.perShapeLifeJitter, 0f, 1f, "Life jitter");

            switch (w.shape)
            {
                case WaveShape.Ring:
                    w.ringThickness = Slider(w.ringThickness, 1f, 12f, "Ring thickness");
                    break;
                case WaveShape.DissolvingDisc:
                    w.dissolveCenter = Slider(w.dissolveCenter, 0f, 1f, "Dissolve centre");
                    w.dissolveKeepBorder = Toggle(w.dissolveKeepBorder, "Keep border");
                    break;
                case WaveShape.SparkleField:
                    w.sparkleDensity = Slider(w.sparkleDensity, 0f, 1f, "Sparkle density");
                    break;
                case WaveShape.Crescent:
                    w.crescentOffset = EditorGUILayout.Vector2Field("Crescent offset", w.crescentOffset);
                    break;
            }

            w.colorOverLife ??= Wave.DefaultColor(w.shape);
            w.alphaOverLife ??= Wave.DefaultAlpha();
            w.colorOverLife = EditorGUILayout.GradientField("Colour over life", w.colorOverLife);
            w.alphaOverLife = EditorGUILayout.CurveField("Alpha over life", w.alphaOverLife);

            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(spec);
        }

        // ── right: live preview ──────────────────────────────────────────────────
        void DrawPreview()
        {
            EditorGUILayout.BeginVertical();

            int cur = CurrentFrame();
            if (Event.current.type == EventType.Repaint) UpdatePreviewTexture(cur);

            Rect view = GUILayoutUtility.GetRect(220, 220, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(view, new Color(0.08f, 0.08f, 0.10f));
            if (previewTex != null) GUI.DrawTexture(view, previewTex, ScaleMode.ScaleToFit, true);

            EditorGUILayout.BeginHorizontal();
            if (Button(playing ? "❚❚ Pause" : "▶ Play")) { playing = !playing; scrub = -1; }
            if (Button("⟲ Restart")) { frame = 0; acc = 0f; scrub = -1; }
            if (Button("Bake")) BlastBaker.Bake(spec);
            EditorGUILayout.EndHorizontal();

            // frame scrubber — dragging it pauses playback and holds the frame
            int shown = scrub >= 0 ? scrub : frame;
            int scrubbed = Mathf.RoundToInt(Slider(shown, 0, Mathf.Max(0, FrameCount - 1), "Frame"));
            if (scrubbed != shown) { scrub = scrubbed; playing = false; Repaint(); }

            // retime: same waves, different number of frames
            int fc = Mathf.RoundToInt(Slider(spec.frameCount, 1, 64, "Frame count"));
            if (fc != spec.frameCount) { spec.frameCount = Mathf.Max(1, fc); EditorUtility.SetDirty(spec); frame = Mathf.Min(frame, FrameCount - 1); }

            speed = Slider(speed, 0.1f, 3f, "Speed");
            Label($"frame {cur + 1} / {FrameCount}", ZUI.ZTextStyle.Subtle);

            EditorGUILayout.EndVertical();
        }

        void UpdatePreviewTexture(int f)
        {
            if (previewTex != null) DestroyImmediate(previewTex);
            previewTex = BlastRenderer.RenderFrameTexture(spec, f);
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
            frame = 0; acc = 0f; scrub = -1; layerSel = waveSel = 0;
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
            spec = s; frame = 0; layerSel = waveSel = 0;
        }
    }
}
