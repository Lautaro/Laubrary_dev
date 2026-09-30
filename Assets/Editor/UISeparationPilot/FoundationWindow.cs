using System;
using System.IO;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using B = Laubrary.Zui.FoundationBaseline;
using FZ = Laubrary.Zui.FoundationFactoryBaseline.Z;

namespace Laubrary.UISeparationPilot
{
    public sealed class FoundationWindow : EditorWindow
    {
        const string Folder = "Assets/Editor/UISeparationPilot/";
        [SerializeField] bool _reference;
        [SerializeField] Fixture _fixture;
        SerializedObject _serialized;
        Label _preview;
        public sealed class Fixture : ScriptableObject
        {
            public float amount = .42f;
            public string caption = "Foundation preview";
            public Vector3 position = new Vector3(1, 2, 3);
            [SerializeReference] public Settings settings = new Settings();
        }
        [Serializable] public class Settings { public float gain = .6f; public bool enabled = true; }
        [Serializable] public sealed class Reflected { public float speed = .35f; public int count = 3; public bool active = true; public string name = "Sample"; }
        Reflected _reflected = new Reflected();

        [MenuItem("Laubrary/UI Separation Foundations")]
        public static void OpenPair()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<FoundationWindow>()) w.Close();
            for (int i = 0; i < 2; i++)
            {
                var w = CreateInstance<FoundationWindow>();
                w._reference = i == 0;
                w.titleContent = new GUIContent(i == 0 ? "Foundations — Reference" : "Foundations — Candidate");
                w.Show();
                w.position = new Rect(60 + i * 730, 90, 720, 760);
                w.Build();
            }
        }
        void OnEnable() { minSize = new Vector2(420, 620); Undo.undoRedoPerformed += RefreshPreview; }
        void OnDisable() { Undo.undoRedoPerformed -= RefreshPreview; _serialized?.Dispose(); }
        public void CreateGUI() => Build();
        void Build()
        {
            var root = rootVisualElement;
            root.Clear();
            for (int i = root.styleSheets.count - 1; i >= 0; i--)
                if (AssetDatabase.GetAssetPath(root.styleSheets[i]).StartsWith("Assets/")) root.styleSheets.Remove(root.styleSheets[i]);
            if (_reference)
            {
                root.AddToClassList("zui-root");
                root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(Folder + "Baseline/BaselineToolkit.uss"));
                foreach (string name in new[] { "ZuiPilotStandard", "ZuiPilotBands", "ZuiPilotEnvelope", "ZuiPresentation" })
                    root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Packages/Laubrary/Zui/Toolkit/" + name + ".uss"));
            }
            else Z.Attach(root);
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(Folder + "Foundation.uss"));
            root.AddToClassList("foundation-surface");
            if (_fixture == null) { _fixture = CreateInstance<Fixture>(); _fixture.hideFlags = HideFlags.HideAndDontSave; }
            _serialized?.Dispose(); _serialized = new SerializedObject(_fixture);
            var toolbar = Z.Row(Z.Button("Reset", "Reset the disposable comparison fixture.", () => { _fixture.amount = .42f; _fixture.caption = "Foundation preview"; _reflected = new Reflected(); Build(); }),
                Z.Button("Tool override", "Toggle parent-scoped styling on the candidate only.", ToggleOverride));
            root.Add(toolbar);
            var scroll = new ScrollView(); scroll.AddToClassList("foundation-scroll"); root.Add(scroll);
            VisualElement box = _reference ? new B.ZuiBox("Card", "Foldable control card.") : new ZuiBox("Card", "Foldable control card.");
            box.name = "foundation-box";
            if (_reference)
            {
                var b = (B.ZuiBox)box;
                b.AddHeaderContent(Z.Text("Header", tooltip: "Header content."));
                b.AddHeaderLead(Z.Text("•", tooltip: "Header marker."));
                b.ToggleGroup("fixture", "Fixture options");
                b.Toggleable(Z.Text("Optional row", tooltip: "A toggleable item."), "option", "Optional row", "fixture");
            }
            else
            {
                var b = (ZuiBox)box;
                b.AddHeaderContent(Z.Text("Header", tooltip: "Header content."));
                b.AddHeaderLead(Z.Text("•", tooltip: "Header marker."));
                b.ToggleGroup("fixture", "Fixture options");
                b.Toggleable(Z.Text("Optional row", tooltip: "A toggleable item."), "option", "Optional row", "fixture");
            }
            var floatField = _reference ? FZ.Float(.42f, "Change the preview amount.", EditAmount) : Z.Float(.42f, "Change the preview amount.", EditAmount);
            floatField.name = "factory-float";
            var text = _reference ? FZ.TextInput("Foundation preview", "Change preview text.", EditCaption) : Z.TextInput("Foundation preview", "Change preview text.", EditCaption);
            text.name = "factory-text";
            var slider = _reference ? FZ.Slider(.42f, 0, 1, "Change the amount.", EditAmount) : Z.Slider(.42f, 0, 1, "Change the amount.", EditAmount);
            slider.name = "factory-slider";
            var objectField = _reference ? FZ.Object<UnityEngine.Object>(null, "Optional preview reference.", _ => {}) : Z.Object<UnityEngine.Object>(null, "Optional preview reference.", _ => {});
            objectField.name = "factory-object";
            box.Add(Z.Row(Z.Field("Amount", "Preview amount.", floatField), Z.Field("Title", "Preview title.", text)));
            box.Add(Z.Row(slider, objectField)); scroll.Add(box);
            var integer = _reference ? FZ.Int(3, "Whole number.", _ => {}) : Z.Int(3, "Whole number.", _ => {}); integer.name = "factory-int";
            var color = _reference ? FZ.Color(UnityEngine.Color.cyan, "Preview colour.", _ => {}) : Z.Color(UnityEngine.Color.cyan, "Preview colour.", _ => {}); color.name = "factory-color";
            var choices = new System.Collections.Generic.List<string> { "First", "Second" };
            var choice = _reference ? FZ.Dropdown(0, choices, "Choose a fixture option.", _ => {}) : Z.Dropdown(0, choices, "Choose a fixture option.", _ => {}); choice.name = "factory-choice";
            box.Add(Z.Row(Z.Field("Count", "Whole number.", integer), Z.Field("Colour", "Preview colour.", color), choice));
            var curve = _reference ? FZ.Curve(AnimationCurve.Linear(0, 0, 1, 1), "Editable curve.", _ => {}) : Z.Curve(AnimationCurve.Linear(0, 0, 1, 1), "Editable curve.", _ => {}); curve.name = "factory-curve";
            var gradient = _reference ? FZ.Gradient(new Gradient(), "Editable gradient.", _ => {}) : Z.Gradient(new Gradient(), "Editable gradient.", _ => {}); gradient.name = "factory-gradient";
            box.Add(Z.Row(curve, gradient));
            var minmax = _reference ? FZ.MinMax(.2f, .8f, 0, 1, "Fixture range.", (_, __) => {}) : Z.MinMax(.2f, .8f, 0, 1, "Fixture range.", (_, __) => {}); minmax.name = "factory-minmax"; box.Add(minmax);
            VisualElement section = _reference ? new B.ZuiSection("Section", "A section with independent fields.") : new ZuiSection("Section", "A section with independent fields.");
            section.name = "foundation-section";
            if (_reference) { ((B.ZuiSection)section).SetHeaderMenu("caret-down", "Open example settings.", OpenSettings); ((B.ZuiSection)section).SetHeaderToggle(true, "Enable example section.", _ => {}); }
            else { ((ZuiSection)section).SetHeaderMenu("caret-down", "Open example settings.", OpenSettings); ((ZuiSection)section).SetHeaderToggle(true, "Enable example section.", _ => {}); }
            var left = Z.Column(Z.Text("First column", tooltip: "Column label."), Z.Text("Second item", tooltip: "Column item."));
            var right = Z.Column(Z.Text("Other column", tooltip: "Column label."));
            section.Add(_reference ? FZ.Columns(2, left, right) : Z.Columns(2, left, right)); scroll.Add(section);
            VisualElement flow = _reference ? (VisualElement)new B.ZuiColumnFlow(220) : new ZuiColumnFlow(220);
            flow.name = "foundation-column-flow";
            for (int i = 0; i < 6; i++) flow.Add(Z.Text("Flow item " + i, tooltip: "Ordered responsive item."));
            scroll.Add(flow);
            VisualElement frame = _reference ? new B.ZuiFrame("Reflected fields", "Fields generated from a plain object.") : new ZuiFrame("Reflected fields", "Fields generated from a plain object.");
            if (_reference) B.ZuiReflect.FlowFields(frame, _reflected, new B.ZuiReflect.Options { OnChanged = RefreshPreview, TooltipFor = f => "Edit " + f.Name });
            else ZuiReflect.FlowFields(frame, _reflected, new ZuiReflect.Options { OnChanged = RefreshPreview, TooltipFor = f => "Edit " + f.Name });
            scroll.Add(frame);
            VisualElement serializedFrame = _reference ? new B.ZuiFrame("Serialized fields", "Undo-aware object fields.") : new ZuiFrame("Serialized fields", "Undo-aware object fields.");
            var amount = _serialized.FindProperty("amount");
            serializedFrame.Add(_reference ? B.ZuiSerialized.Field(amount, onChanged: RefreshPreview) : ZuiSerialized.Field(amount, onChanged: RefreshPreview));
            var position = _serialized.FindProperty("position");
            var property = _reference ? B.ZuiSerialized.Property(position) : ZuiSerialized.Property(position); property.name = "serialized-property"; serializedFrame.Add(property);
            var settings = _serialized.FindProperty("settings");
            var managed = _reference ? (VisualElement)new B.ZuiManagedRef(settings, "Settings", "Expand the managed settings.") : new ZuiManagedRef(settings, "Settings", "Expand the managed settings.");
            managed.name = "managed-settings"; serializedFrame.Add(managed); scroll.Add(serializedFrame);
            _preview = new Label { name = "foundation-preview" }; _preview.AddToClassList("foundation-preview"); root.Add(_preview); RefreshPreview();
        }
        void EditAmount(float value) { Undo.RecordObject(_fixture, "Edit foundation amount"); _fixture.amount = value; RefreshPreview(); }
        void OpenSettings(VisualElement anchor) => ZuiPopover.Show(anchor, panel => panel.Add(Z.Float(_fixture.amount, "Amount inside the settings popup.", EditAmount)));
        void EditCaption(string value) { Undo.RecordObject(_fixture, "Edit foundation title"); _fixture.caption = value; RefreshPreview(); }
        void RefreshPreview() { if (_fixture != null && _preview != null) _preview.text = $"{_fixture.caption} · amount {_fixture.amount:0.00} · speed {_reflected.speed:0.00}"; }
        public static void ToggleOverride()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<FoundationWindow>()) if (!w._reference)
                w.rootVisualElement.EnableInClassList("lau-tool-foundation-demo", !w.rootVisualElement.ClassListContains("lau-tool-foundation-demo"));
        }
        public static void SetWidth(float width) { foreach (var w in Resources.FindObjectsOfTypeAll<FoundationWindow>()) w.position = new Rect(w.position.x, w.position.y, width, 760); }
        public static string Capture(string label)
        {
            string folder = Laubrary.Zounds.Uitk.ZoundsUitkCompare.OutputFolder;
            Directory.CreateDirectory(folder);
            var windows = Resources.FindObjectsOfTypeAll<FoundationWindow>();
            if (windows.Length != 2 || Vector2.Distance(windows[0].position.position, windows[1].position.position) < 40)
                throw new InvalidOperationException("Two independently positioned reference/candidate windows are required.");
            int count = 0;
            foreach (var w in windows)
            {
                var t = Laubrary.Zounds.Uitk.ZoundsUitkCompare.Capture(w);
                if (t == null) throw new InvalidOperationException("Capture failed");
                File.WriteAllBytes(Path.Combine(folder, label + (w._reference ? "_baseline.png" : "_candidate.png")), t.EncodeToPNG()); DestroyImmediate(t); count++;
            }
            return $"Captured {count} windows to {folder}";
        }
    }
}
