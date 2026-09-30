using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.Zui;
using Laubrary.Zounds.Uitk;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using B = Laubrary.Zui.PilotBaseline;

namespace Laubrary.UISeparationPilot
{
    /// <summary>Developer-only frozen-vs-candidate visual harness. It owns no real project data.</summary>
    public sealed class PilotWindow : EditorWindow
    {
        const string Root = "Assets/Editor/UISeparationPilot/";
        const string FixtureKey = "Laubrary.UISeparationPilot.fixture.v1";
        static PilotWindow s_baseline, s_candidate;
        static Fixture s_fixture;
        [SerializeField] bool _reference, _colorful, _override;
        VisualElement _previewShape;

        sealed class Fixture : ScriptableObject
        {
            public float scalar = .42f, low = .25f, high = .72f;
            public bool enabled = true;
            public float[] bands = { .18f, .55f, .81f, .35f, 1.14f };
            public List<ZUIEnvelopePoint> points = new List<ZUIEnvelopePoint> { new ZUIEnvelopePoint(0, .2f), new ZUIEnvelopePoint(.4f, .8f), new ZUIEnvelopePoint(1, .35f) };
        }

        static Fixture F { get { if (s_fixture == null) { s_fixture = CreateInstance<Fixture>(); s_fixture.hideFlags = HideFlags.HideAndDontSave; JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(FixtureKey, "{}"), s_fixture); } return s_fixture; } }

        [MenuItem("Laubrary/UI Separation Pilot")]
        static void OpenMenu() => OpenPair();

        public static void OpenPair(bool colorful = false)
        {
            foreach (var old in Resources.FindObjectsOfTypeAll<PilotWindow>()) old.Close();
            s_baseline = CreateWindow<PilotWindow>("UI Separation — Baseline");
            s_candidate = CreateWindow<PilotWindow>("UI Separation — Candidate");
            s_baseline.position = new Rect(80, 90, 620, 600);
            s_candidate.position = new Rect(712, 90, 620, 600);
            s_baseline.Rebuild(true, colorful);
            s_candidate.Rebuild(false, colorful);
            s_baseline.Show(); s_candidate.Show();
        }

        public static void SetOverride(bool enabled)
        {
            if (s_candidate == null) OpenPair();
            s_candidate._override = enabled;
            s_candidate.rootVisualElement.EnableInClassList("lau-tool-pilot", enabled);
            s_baseline?.rootVisualElement.Q<Toggle>("pilot-override")?.SetValueWithoutNotify(enabled);
            s_candidate.rootVisualElement.Q<Toggle>("pilot-override")?.SetValueWithoutNotify(enabled);
            s_candidate.Repaint();
        }

        public static void SetSkin(bool colorful)
        {
            foreach (var w in new[] { s_baseline, s_candidate })
            {
                if (w == null) continue;
                w._colorful = colorful;
                RemoveSheets(w.rootVisualElement);
                AttachSheets(w.rootVisualElement, w._reference, colorful);
                w.rootVisualElement.Q<Toggle>("pilot-colorful")?.SetValueWithoutNotify(colorful);
                w.rootVisualElement.Q<VisualElement>("controls-slot").Query<Button>().ForEach(b => {
                    b.EnableInClassList(b.ClassListContains("zui-togglebutton") ? "zs-RichToggle" : "zs-RichButton", colorful);
                });
                w.Repaint();
            }
        }

        public static void SetSize(float width)
        {
            foreach (var w in new[] { s_baseline, s_candidate })
                if (w != null) w.position = new Rect(w.position.x, w.position.y, width, 600);
        }

        static void RemoveSheets(VisualElement root)
        {
            for (int i = root.styleSheets.count - 1; i >= 0; i--)
            {
                string path = AssetDatabase.GetAssetPath(root.styleSheets[i]);
                if (path.StartsWith(Root, StringComparison.Ordinal) || path.StartsWith("Assets/Packages/Laubrary/", StringComparison.Ordinal)) root.styleSheets.Remove(root.styleSheets[i]);
            }
        }

        public static string CapturePair(string label = "pilot")
        {
            if (s_baseline == null || s_candidate == null) return "FAILED: open the pilot pair first.";
            var a = ZoundsUitkCompare.Capture(s_baseline);
            var b = ZoundsUitkCompare.Capture(s_candidate);
            if (a == null || b == null) return "FAILED: capture unavailable.";
            try
            {
                if (a.width != b.width || a.height != b.height) return $"FAILED: dimensions differ ({a.width}x{a.height} / {b.width}x{b.height}).";
                if (Blank(a) || Blank(b)) return "FAILED: blank capture rejected.";
                string folder = ZoundsUitkCompare.OutputFolder;
                Directory.CreateDirectory(folder);
                string Save(Texture2D t, string suffix) { string p = Path.Combine(folder, label + "_" + suffix + ".png"); File.WriteAllBytes(p, t.EncodeToPNG()); return p; }
                Save(a, "baseline"); Save(b, "candidate");
                var pair = new Texture2D(a.width * 2 + 8, a.height, TextureFormat.RGBA32, false);
                pair.SetPixels32(Fill(pair.width * pair.height, new Color32(255, 0, 255, 255)));
                pair.SetPixels32(0, 0, a.width, a.height, a.GetPixels32()); pair.SetPixels32(a.width + 8, 0, b.width, b.height, b.GetPixels32()); pair.Apply(); Save(pair, "pair");
                var diff = new Texture2D(a.width, a.height, TextureFormat.RGBA32, false); var pa = a.GetPixels32(); var pb = b.GetPixels32(); var px = new Color32[pa.Length];
                for (int i = 0; i < px.Length; i++) { int d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Max(Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b))); px[i] = d > ZoundsUitkCompare.Tolerance ? new Color32(255, 40, 40, 255) : new Color32((byte)(pa[i].r / 4), (byte)(pa[i].g / 4), (byte)(pa[i].b / 4), 255); }
                diff.SetPixels32(px); diff.Apply(); Save(diff, "diff");
                DestroyImmediate(pair); DestroyImmediate(diff);
                return $"Captured {label}_baseline/candidate/pair/diff.png in {folder}.";
            }
            finally { DestroyImmediate(a); DestroyImmediate(b); }
        }

        public static string State() => $"scalar={F.scalar:0.000}; range={F.low:0.00}-{F.high:0.00}; pair={(s_baseline != null && s_candidate != null ? "open" : "closed")}";
        public static string Geometry() => s_baseline == null || s_candidate == null ? "pair closed" : $"baseline={s_baseline.position.width:0}x{s_baseline.position.height:0}; candidate={s_candidate.position.width:0}x{s_candidate.position.height:0}";

        void OnEnable() { minSize = new Vector2(420,600); _ = F; if (_reference) s_baseline = this; else s_candidate = this; Undo.undoRedoPerformed += OnUndo; }
        public void CreateGUI() => Rebuild(_reference, _colorful);
        void OnDisable() { Undo.undoRedoPerformed -= OnUndo; SaveFixture(); }
        void OnUndo() { SaveFixture(); SyncVisible(); }

        /// <summary>Public for editor eval: reference=true builds frozen controls; false builds current controls.</summary>
        public void Rebuild(bool reference, bool colorful)
        {
            _reference = reference; _colorful = colorful;
            var root = rootVisualElement; root.Clear();
            // Keep Unity's editor theme (including its fonts and native field styles).
            RemoveSheets(root);
            var toolbar = new VisualElement(); toolbar.AddToClassList("pilot-toolbar");
            var skin = new Toggle("Colorful") { name = "pilot-colorful", tooltip = "Switch both windows to the existing Zounds Colorful presentation." };
            skin.SetValueWithoutNotify(colorful); skin.RegisterValueChangedCallback(e => SetSkin(e.newValue)); toolbar.Add(skin);
            var scoped = new Toggle("Tool override") { name = "pilot-override", tooltip = "Apply the tool parent class to the candidate only; the baseline stays unchanged." };
            scoped.SetValueWithoutNotify(_override); scoped.RegisterValueChangedCallback(e => SetOverride(e.newValue)); toolbar.Add(scoped);
            var sizes = new UnityEditor.UIElements.ToolbarMenu { text = "Width", tooltip = "Resize the pair to check wrapping." };
            foreach (float width in new[] { 420f, 620f, 900f }) { float size = width; sizes.menu.AppendAction(width + " px", _ => SetSize(size)); }
            toolbar.Add(sizes); root.Add(toolbar);
            root.RemoveFromClassList("pilot-baseline"); root.RemoveFromClassList("pilot-candidate"); root.RemoveFromClassList("zs-root"); root.RemoveFromClassList("lau-tool-pilot");
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "PilotLayout.uxml");
            if (tree != null) tree.CloneTree(root);
            root.AddToClassList("pilot-root"); root.AddToClassList("zui-root"); root.AddToClassList(reference ? "pilot-baseline" : "pilot-candidate");
            if (!reference && _override) root.AddToClassList("lau-tool-pilot");
            AttachSheets(root, reference, colorful);
            var controls = root.Q<VisualElement>("controls-slot") ?? root;
            var preview = root.Q<VisualElement>("preview-slot") ?? root;
            Build(controls, preview, reference);
        }

        static void AttachSheets(VisualElement root, bool reference, bool colorful)
        {
            void Add(string path) { var s = AssetDatabase.LoadAssetAtPath<StyleSheet>(path); if (s != null) root.styleSheets.Add(s); }
            if (reference)
            {
                Add(Root + "Baseline/BaselineToolkit.uss");
                Add(Root + "Baseline/Skin/ZoundsSkin.uss");
                if (colorful) Add(Root + "Baseline/BaselineZoundsLayout.uss");
            }
            else { Z.Attach(root); Add("Assets/Packages/Laubrary/Editor/Zounds/Uitk/Skin/ZoundsSkin.uss"); if (colorful) ZS.Attach(root); }
            // Skinned range/band controls have always required the existing Zounds skin.
            root.AddToClassList("zs-root");
            Add(Root + "Pilot.uss"); Add(Root + "PilotOverride.uss");
        }

        void Build(VisualElement controls, VisualElement preview, bool reference)
        {
            controls.Add(new Label("UI Separation Fixture") { tooltip = "The same fixture is rendered in both windows." });
            var actions = new VisualElement(); actions.AddToClassList("pilot-actions");
            var reset = reference ? new Button(ResetFixture) { text = "Reset", tooltip = "Reset all preview values." } : Z.Button("Reset", "Reset all preview values.", ResetFixture);
            reset.AddToClassList("zui-button"); actions.Add(reset);
            if (reference) actions.Add(new B.ZuiToggleButton("Enabled", "Toggle the shared fixture.", F.enabled, v => { Change(() => F.enabled = v); SyncVisible(); }));
            else actions.Add(new ZuiToggleButton("Enabled", "Toggle the shared fixture.", F.enabled, v => { Change(() => F.enabled = v); SyncVisible(); }));
            controls.Add(actions);
            Action<float> scalar = v => { Change(() => F.scalar = v); SyncVisible(); };
            if (reference) controls.Add(new B.ZuiMicroSlider("Amount", F.scalar, 0, 1, "Changes the preview shape.", scalar, true, .42f, decimals: 2, prefsKey:"ui-separation-pilot.amount"));
            else controls.Add(new ZuiMicroSlider("Amount", F.scalar, 0, 1, "Changes the preview shape.", scalar, true, .42f, decimals: 2, prefsKey:"ui-separation-pilot.amount"));
            if (reference) controls.Add(new B.ZuiMicroMinMax("Window", F.low, F.high, 0, 1, "Adjust the active interval.", (a, b) => { Change(() => { F.low = a; F.high = b; }); SyncVisible(); }, true, .25f, .72f, decimals: 2));
            else controls.Add(new ZuiMicroMinMax("Window", F.low, F.high, 0, 1, "Adjust the active interval.", (a, b) => { Change(() => { F.low = a; F.high = b; }); SyncVisible(); }, true, .25f, .72f, decimals: 2));
            controls.Add(reference ? SkinSliderB() : SkinSliderC());
            controls.Add(reference ? SkinRangeB() : SkinRangeC());
            Action<float,float> interval = (a,b) => { Change(() => { F.low=a; F.high=b; }); SyncVisible(); };
            controls.Add(reference ? (VisualElement)new B.ZuiSkinMinMax("Interval",F.low,F.high,0,1,"Drag the interval edges or centre.",interval) : new ZuiSkinMinMax("Interval",F.low,F.high,0,1,"Drag the interval edges or centre.",interval));
            controls.Add(reference ? BandsB() : BandsC());
            controls.Add(reference ? EnvelopeB() : EnvelopeC());
            var wrap = new VisualElement(); wrap.AddToClassList("pilot-wrap");
            for (int i = 0; i < 5; i++)
            {
                VisualElement p = reference
                    ? new B.ZuiMicroSlider("Parameter " + (i + 1), F.scalar, 0, 1, "Another view of Amount; adjusts the preview.", scalar, true, decimals: 2, prefsKey:"ui-separation-pilot.parameter"+i)
                    : new ZuiMicroSlider("Parameter " + (i + 1), F.scalar, 0, 1, "Another view of Amount; adjusts the preview.", scalar, true, decimals: 2, prefsKey:"ui-separation-pilot.parameter"+i);
                p.AddToClassList("pilot-parameter"); wrap.Add(p);
            }
            controls.Add(wrap);
            controls.Query<VisualElement>().ForEach(e => {
                if (e.ClassListContains("zui-skinslider") || e.ClassListContains("zui-skinrange") || e.ClassListContains("zui-skinminmax") || e.ClassListContains("zui-skinband")) e.AddToClassList("zs-slider-default");
                if (_colorful && e is Button) e.AddToClassList(e.ClassListContains("zui-togglebutton") ? "zs-RichToggle" : "zs-RichButton");
            });
            _previewShape = new VisualElement { tooltip = "Preview changes with Amount." }; _previewShape.AddToClassList("pilot-preview-shape"); UpdatePreview(); preview.Add(_previewShape);
            // Frozen direct constructors predate the factory's 150 px default; pin only that reference side to the historical factory width.
            if (reference) controls.Query<VisualElement>(className: "zui-microslider").ForEach(e => e.style.width = 150f);
        }

        VisualElement SkinSliderC() => new ZuiSkinSlider("Skin amount", F.scalar, 0, 1, "Skinned scalar.", v => { Change(() => F.scalar = v); SyncVisible(); }, ZuiSkinSlider.LabelMode.LabelAndValue, .42f);
        VisualElement SkinSliderB() => new B.ZuiSkinSlider("Skin amount", F.scalar, 0, 1, "Skinned scalar.", v => { Change(() => F.scalar = v); SyncVisible(); }, B.ZuiSkinSlider.LabelMode.LabelAndValue, .42f);
        VisualElement SkinRangeC() => new ZuiSkinRangeSlider("Range", F.low, F.high, 0, 1, new ZuiSkinRangeSlider.Geometry { thumbWidth = 12, thumbHeight = 18, trackHeight = 8, valueWidth = 140, labelWidth = 44, showValueField = true }, (a, b) => { Change(() => { F.low = a; F.high = b; }); SyncVisible(); });
        VisualElement SkinRangeB() => new B.ZuiSkinRangeSlider("Range", F.low, F.high, 0, 1, new B.ZuiSkinRangeSlider.Geometry { thumbWidth = 12, thumbHeight = 18, trackHeight = 8, valueWidth = 140, labelWidth = 44, showValueField = true }, (a, b) => { Change(() => { F.low = a; F.high = b; }); SyncVisible(); });
        VisualElement BandsC() => new ZuiSkinBandSliders(F.bands, 0, 1, .5f, v => { Change(() => Array.Copy(v, F.bands, v.Length)); SyncVisible(); }, .5f, i => "Band " + (i + 1));
        VisualElement BandsB() => new B.ZuiSkinBandSliders(F.bands, 0, 1, .5f, v => { Change(() => Array.Copy(v, F.bands, v.Length)); SyncVisible(); }, .5f, i => "Band " + (i + 1));
        ZUIEnvelopeRuntime EnvelopeRuntime() => new ZUIEnvelopeRuntime { showGrid = true, onDragStarted = BeginEnvelopeUndo, onDragUpdated = SyncVisible, onMutated = () => { EditorUtility.SetDirty(F); SaveFixture(); SyncVisible(); } };
        void BeginEnvelopeUndo() => Undo.RecordObject(F, "UI Separation Pilot envelope");
        VisualElement EnvelopeC() { var e = new ZuiSkinEnvelope(F.points, new Color(.3f, .7f, 1f), new ZUIEnvelopeDef(), EnvelopeRuntime()); e.AddToClassList("zui-envelope--legacy-profile"); return e; }
        VisualElement EnvelopeB() => new B.ZuiSkinEnvelope(F.points, new Color(.3f, .7f, 1f), new ZUIEnvelopeDef(), EnvelopeRuntime());
        static void Change(Action apply) { Undo.RecordObject(F, "UI Separation Pilot"); apply(); EditorUtility.SetDirty(F); SaveFixture(); }
        void ResetFixture()
        {
            Change(() => {
                F.scalar=.42f; F.low=.25f; F.high=.72f; F.enabled=true;
                F.bands=new[] { .18f,.55f,.81f,.35f,1.14f };
                F.points=new List<ZUIEnvelopePoint> { new ZUIEnvelopePoint(0,.2f),new ZUIEnvelopePoint(.4f,.8f),new ZUIEnvelopePoint(1,.35f) };
            });
            SyncVisible();
        }
        static void SaveFixture() => EditorPrefs.SetString(FixtureKey, JsonUtility.ToJson(F));
        void UpdatePreview() { if (_previewShape != null) { _previewShape.style.width = 30 + (F.enabled ? F.scalar : 0f) * 180; _previewShape.style.opacity = F.enabled ? 1f : .35f; } }
        void RefreshControls()
        {
            rootVisualElement.Query<VisualElement>().ForEach(e =>
            {
                if (e is ZuiMicroSlider slider) slider.value = F.scalar;
                else if (e is B.ZuiMicroSlider baselineSlider) baselineSlider.value = F.scalar;
                else if (e is ZuiSkinSlider skinSlider) skinSlider.SetValueWithoutNotify(F.scalar);
                else if (e is B.ZuiSkinSlider baselineSkinSlider) baselineSkinSlider.SetValueWithoutNotify(F.scalar);
                else if (e is ZuiSkinRangeSlider range) range.SetValuesWithoutNotify(F.low, F.high);
                else if (e is B.ZuiSkinRangeSlider baselineRange) baselineRange.SetValuesWithoutNotify(F.low, F.high);
                else if (e is ZuiSkinMinMax minmax) minmax.SetValuesWithoutNotify(F.low,F.high);
                else if (e is B.ZuiSkinMinMax baselineMinmax) baselineMinmax.SetValuesWithoutNotify(F.low,F.high);
                else if (e is ZuiSkinBandSliders bands) bands.SetValues(F.bands);
                else if (e is B.ZuiSkinBandSliders baselineBands) baselineBands.SetValues(F.bands);
                else if (e is ZuiToggleButton toggle) toggle.SetValueWithoutNotify(F.enabled);
                else if (e is B.ZuiToggleButton baselineToggle) baselineToggle.SetValueWithoutNotify(F.enabled);
                else if (e is ZuiSkinEnvelope envelope) { envelope.points = F.points; envelope.Repaint(); }
                else if (e is B.ZuiSkinEnvelope baselineEnvelope) { baselineEnvelope.points = F.points; baselineEnvelope.Repaint(); }
            });
            SetMicroMinMax(e: rootVisualElement, reference: _reference);
        }
        static void SetMicroMinMax(VisualElement e, bool reference)
        {
            var type = reference ? typeof(B.ZuiMicroMinMax) : typeof(ZuiMicroMinMax);
            var set = type.GetMethod("SetValues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            e.Query<VisualElement>(className: "zui-microminmax").ForEach(control => { if (type.IsInstanceOfType(control)) set?.Invoke(control, new object[] { F.low, F.high, false }); });
        }
        void SyncVisible() { s_baseline?.RefreshControls(); s_candidate?.RefreshControls(); s_baseline?.UpdatePreview(); s_candidate?.UpdatePreview(); }
        static bool Blank(Texture2D t) { var p = t.GetPixels32(); for (int i = 1; i < p.Length; i++) if (p[i].r != p[0].r || p[i].g != p[0].g || p[i].b != p[0].b) return false; return true; }
        static Color32[] Fill(int n, Color32 c) { var a = new Color32[n]; for (int i = 0; i < n; i++) a[i] = c; return a; }
    }
}
