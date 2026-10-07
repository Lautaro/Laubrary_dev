using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Exercises retained control gesture paths on disposable assets; callable by reflection, with no Test Runner or menu.</summary>
        public static string CheckInstrumentReworkWorkflow()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>();
            var songAsset = CreateInstance<ZTrackerSong>();
            var inst = CreateInstance<ZTrackerInstrument>();
            inst.schemaVersion = songAsset.schemaVersion = 1;
            inst.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSong(inst);
            window.song = songAsset; window.instrument = inst; window.pane = 3; window.instrumentTab = 0;
            window.position = new Rect(160, 120, 1120, 760); window.Show(); window.CreateGUI();
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Near(float actual, float expected, string message) => Need(Math.Abs(actual - expected) < .00001f, message + " (" + actual + " != " + expected + ")");
            void Layout()
            {
                var panel = window.rootVisualElement.panel;
                Need(panel != null, "Verification window is not attached");
                // Flush this disposable panel's layout so drag distances use the actual control geometry.
                var method = panel.GetType().GetMethod("ValidateLayout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                method?.Invoke(panel, null);
            }
            void Fresh()
            {
                window.StopPreview(); Undo.ClearUndo(inst); Undo.ClearUndo(songAsset);
                inst.schemaVersion = songAsset.schemaVersion = 1; window.legacyInstrumentViews.Clear();
                inst.serializedNulls = songAsset.serializedNulls = null;
                inst.model = NewInstrumentData(); inst.model.parameters.instFilterEnabled = true; songAsset.model = TrackerEngineCheck.FixtureSong(inst);
                window.instrumentTab = 0; window.modSetId = window.modDeviceId = ""; window.BuildPane(); ZuiAudit.ExpandAll(window); Layout();
            }
            void Check(string name, Action action)
            {
                try { Fresh(); action(); passed++; report.AppendLine("PASS " + name); }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + name + ": " + (ex.InnerException ?? ex).Message); }
            }
            T Find<T>(string name) where T : VisualElement
            {
                var control = window.rootVisualElement.Q<T>(name);
                Need(control != null, "Missing control " + name); return control;
            }
            bool Visible(VisualElement control)
            {
                for (var element = control; element != null; element = element.hierarchy.parent)
                    if (element.resolvedStyle.display == DisplayStyle.None || element.resolvedStyle.visibility == UnityEngine.UIElements.Visibility.Hidden) return false;
                return control.panel != null;
            }
            void ExpandCurve(string name)
            {
                var control = Find<ZuiValueControl>(name);
                Need(control.Value.mode == ZUIValue.Mode.Curve, "Cannot expand a static value " + name);
                control.IsExpanded = true; Layout();
                Need(control.IsExpanded && Visible(Find<VisualElement>(name + "-timing")), "Expanded curve has no visible timing controls " + name);
            }
            void Dial(string name, float value)
            {
                var control = window.rootVisualElement.Q<ZuiMicroSlider>(name) ?? window.rootVisualElement.Q<ZuiValueControl>(name)?.Q<ZuiMicroSlider>();
                Need(control != null, "Missing scalar control " + name);
                Need(Visible(control), "Scalar control is hidden " + name);
                typeof(ZuiMicroSlider).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { value, true });
            }
            void Click(string name)
            {
                var button = Find<Button>(name);
                Need(Visible(button), "Button is hidden " + name);
                typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null, 0 });
            }
            void Mode(string name, ZUIValue.Mode mode)
                => typeof(ZuiValueControl).GetMethod("SetMode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Find<ZuiValueControl>(name), new object[] { mode });
            float[] Stages()
            {
                var q = inst.model.parameters; return new[] { q.attack, 0f, q.decay, q.sustain, q.release };
            }
            bool Same(float[] a, float[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) < .00001f).All(x => x);
            string CurveShape(ZUIEnvelopeData env) => string.Join(";", env.points.Select(p => p.time.ToString("R") + ":" + p.value.ToString("R") + ":" + p.exponent.ToString("R")));
            ZUIEnvelopeData Curve() => inst.model.parameters.GetParameterEnvelope("instFilterCutoff");
            void AuthoredCurve()
            {
                inst.model.parameters.SetParameterEnvelope("instFilterCutoff", new ZUIEnvelopeData(0, 1, 0, 1, false)
                {
                    enabled = true, requiresEndPoint = false, loopEnabled = true, loopMode = 1,
                    loopStart = .25f, loopEnd = .75f, sustainEnabled = true, sustainPosition = .5f
                });
                Curve().points.AddRange(new[] { new ZUIEnvelopePoint(0, .15f), new ZUIEnvelopePoint(.5f, .6f), new ZUIEnvelopePoint(1, .1f) });
                window.BuildPane(); Layout();
            }
            try
            {
                foreach (int stage in new[] { 0, 2, 3, 4 })
                {
                    int selectedStage = stage;
                    Check("ADSR stage " + selectedStage + " multi-move drag isolates its value and has one Undo/Redo", () =>
                    {
                        var adsr = Find<ZuiAdsr>("amplitude-adsr");
                        var canvas = adsr.Q("adsr-canvas");
                        Need(canvas.contentRect.width > 80 && canvas.contentRect.height > 40, "ADSR is not laid out at real size");
                        var before = Stages(); Vector2 start = adsr.HandlePosition(selectedStage);
                        adsr.BeginDrag(selectedStage);
                        for (int move = 1; move <= 4; move++) adsr.DragToLocal(start + (selectedStage == 3 ? new Vector2(0, -3 * move) : new Vector2(3 * move, 0)));
                        adsr.EndDrag(); var after = Stages();
                        Need(Math.Abs(after[selectedStage] - before[selectedStage]) > .001f, "Dragging did not change the chosen stage");
                        for (int other = 0; other < 5; other++) if (other != selectedStage) Near(after[other], before[other], "Dragging changed another stage " + other);
                        Undo.PerformUndo(); Need(Same(Stages(), before), "One Undo did not restore every move in the gesture");
                        Undo.PerformRedo(); Need(Same(Stages(), after), "Redo did not restore the completed gesture");
                    });
                }
                Check("AHDSR Hold multi-move drag changes only Hold and has one Undo/Redo", () =>
                {
                    var set = new ModulationSet { id = Id(), name = "Hold check" };
                    set.devices.Add(new ModulationDevice { id = Id(), kind = ModulationDeviceKind.AHDSR, hold = .2f });
                    inst.model.modulation.Add(set); window.instrumentTab = 1; window.modSetId = set.id; window.modDeviceId = set.devices[0].id; window.BuildPane(); Layout();
                    float[] Read() { var d = inst.model.modulation[0].devices[0]; return new[] { d.attack, d.hold, d.decay, d.sustain, d.release }; }
                    var before = Read(); var adsr = Find<ZuiAdsr>("mod-adsr"); var start = adsr.HandlePosition(1);
                    adsr.BeginDrag(1); for (int n = 1; n <= 4; n++) adsr.DragToLocal(start + new Vector2(3 * n, 0)); adsr.EndDrag();
                    var after = Read(); Need(after[1] != before[1], "Hold did not move");
                    for (int i = 0; i < 5; i++) if (i != 1) Near(after[i], before[i], "Hold drag changed another stage");
                    Undo.PerformUndo(); Need(Same(Read(), before), "Hold Undo did not restore the full drag");
                    Undo.PerformRedo(); Need(Same(Read(), after), "Hold Redo failed");
                });
                Check("musical ms Hz and percent controls store compatible physical units", () =>
                {
                    Dial("synth-vibratoFadeIn", 250); Near(inst.model.parameters.vibratoFadeIn, .25f, "Milliseconds were stored as seconds incorrectly");
                    Dial("synth-instFilterCutoff", 1200); Near(inst.model.parameters.instFilterCutoff, 1200f / (AudioSettings.outputSampleRate * .5f), "Hz did not convert to saved Nyquist fraction");
                    Dial("synth-volume", 137); Near(inst.model.parameters.volume, 1.37f, "Percent volume conversion");
                    Dial("synth-pan", -40); Near(inst.model.parameters.pan, -.4f, "Signed pan conversion");
                    var attack = Find<ZuiAdsr>("amplitude-adsr").Q<ZuiMicroSlider>("adsr-input-attack");
                    typeof(ZuiMicroSlider).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(attack, new object[] { 125f, true });
                    Near(inst.model.parameters.attack, .125f, "ADSR milliseconds conversion");
                });
                Check("right-click mode action preserves authored points across Static Curve and Undo", () =>
                {
                    Mode("synth-instFilterCutoff", ZUIValue.Mode.Curve); Need(Curve()?.enabled == true, "Curve mode did not create an enabled envelope");
                    ExpandCurve("synth-instFilterCutoff");
                    var value = Find<ZuiValueControl>("synth-instFilterCutoff"); value.OnBeforeMutate?.Invoke();
                    value.Value.points.Clear(); value.Value.points.AddRange(new[] { new ZUIEnvelopePoint(0, 1500), new ZUIEnvelopePoint(.35f, 5000, 2), new ZUIEnvelopePoint(1, 400) }); value.OnChanged?.Invoke();
                    string authored = CurveShape(Curve());
                    Mode("synth-instFilterCutoff", ZUIValue.Mode.Static); Need(!Curve().enabled && CurveShape(Curve()) == authored, "Static mode discarded the envelope");
                    Mode("synth-instFilterCutoff", ZUIValue.Mode.Curve); Need(Curve().enabled && CurveShape(Curve()) == authored, "Returning to Curve replaced authored points");
                    Undo.PerformUndo(); Need(!Curve().enabled && CurveShape(Curve()) == authored, "Mode Undo did not preserve the disabled curve");
                    Undo.PerformRedo(); Need(Curve().enabled && CurveShape(Curve()) == authored, "Mode Redo did not preserve the enabled curve");
                });
                Check("envelope duration scales points loop and sustain with one Undo/Redo", () =>
                {
                    AuthoredCurve(); string before = JsonUtility.ToJson(Curve());
                    ExpandCurve("synth-instFilterCutoff");
                    Dial("synth-instFilterCutoff-duration", 2);
                    Near(Curve().xMax, 2, "Duration"); Near(Curve().points[1].time, 1, "Point time did not scale");
                    Near(Curve().loopStart, .5f, "Loop start did not scale"); Near(Curve().loopEnd, 1.5f, "Loop end did not scale"); Near(Curve().sustainPosition, 1, "Sustain did not scale");
                    string after = JsonUtility.ToJson(Curve()); Undo.PerformUndo(); Need(JsonUtility.ToJson(Curve()) == before, "Duration Undo lost curve metadata");
                    Undo.PerformRedo(); Need(JsonUtility.ToJson(Curve()) == after, "Duration Redo lost curve metadata");
                });
                Check("loop and sustain controls constrain degenerate or shortened curves", () =>
                {
                    AuthoredCurve(); Curve().loopEnabled = false; Curve().sustainEnabled = false; Curve().sustainPosition = 50;
                    Curve().points.RemoveAt(2); window.BuildPane(); Layout(); ExpandCurve("synth-instFilterCutoff"); Click("synth-instFilterCutoff-sustain");
                    Need(Curve().sustainEnabled && Curve().sustainPosition <= .5f, "Sustain enabled outside the last point");
                    ExpandCurve("synth-instFilterCutoff"); Click("synth-instFilterCutoff-loop"); Need(Curve().loopEnabled && Curve().loopEnd <= .5f && Curve().loopEnd > Curve().loopStart, "Loop bounds invalid after shortened curve");
                    Curve().points.RemoveAt(1); Curve().loopEnabled = false; window.BuildPane(); Layout(); ExpandCurve("synth-instFilterCutoff"); Click("synth-instFilterCutoff-loop");
                    Need(!Curve().loopEnabled, "A zero-duration curve enabled an invalid loop");
                });
                Check("shared effect frequency time and percent controls preserve physical values", () =>
                {
                    var cutoff = Laubrary.Zounds.Dsp.ZoundEffectDescriptors.Get(Laubrary.Zounds.ZoundEffectType.LowPass).parameters[0];
                    float edited = -1;
                    var hz = (ZuiMicroSlider)Laubrary.Audio.Editor.AudioChainEditor.Parameter(cutoff, 8000, v => edited = v);
                    Near(hz.value, 8000, "Log frequency exposed normalized position instead of Hz");
                    typeof(ZuiMicroSlider).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hz, new object[] { 1200f, true });
                    Near(edited, 1200, "Frequency input did not preserve physical Hz");
                    var fade = Laubrary.Zounds.Dsp.ZoundEffectDescriptors.Get(Laubrary.Zounds.ZoundEffectType.Fade).parameters[0];
                    var ms = (ZuiMicroSlider)Laubrary.Audio.Editor.AudioChainEditor.Parameter(fade, .125f, v => edited = v);
                    Near(ms.value, 125, "Fade display is not milliseconds");
                    typeof(ZuiMicroSlider).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ms, new object[] { 250f, true });
                    Near(edited, .25f, "Fade milliseconds did not preserve stored seconds");
                    var mix = Laubrary.Zounds.Dsp.ZoundEffectDescriptors.Get(Laubrary.Zounds.ZoundEffectType.Reverb).parameters[3];
                    var percent = (ZuiMicroSlider)Laubrary.Audio.Editor.AudioChainEditor.Parameter(mix, .37f, v => edited = v);
                    Near(percent.value, 37, "Mix is not displayed as a percentage");
                    bool mutated = false;
                    Laubrary.Audio.Editor.AudioChainEditor.Parameter(cutoff, 96000, _ => mutated = true);
                    Need(!mutated, "Building an out-of-range effect control changed authored data");
                });
                Check("shared Random range edits both endpoints atomically in retained units", () =>
                {
                    var chain = new Laubrary.Audio.AudioEffectChainData();
                    chain.modifiers.Add(new Laubrary.Audio.AudioModifierData { type = Laubrary.Zounds.ZoundModifierType.Random, uid = "random-check", p = new[] { -.25f, .25f, 1f } });
                    int edits = 0; string before = JsonUtility.ToJson(chain);
                    var editor = new Laubrary.Audio.Editor.AudioChainEditor(() => chain, (label, action, structural) => { edits++; action(); });
                    Need(edits == 0 && JsonUtility.ToJson(chain) == before, "Building Random controls mutated the chain");
                    var ranges = editor.Query<ZuiMicroMinMax>().ToList(); Need(ranges.Count == 1, "Random endpoints are not one paired control");
                    typeof(ZuiMicroMinMax).GetMethod("SetValues", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ranges[0], new object[] { -50f, 75f, true });
                    Need(edits == 1, "The two endpoint changes were not one authoring transaction");
                    Near(chain.modifiers[0].p[0], -.5f, "Random lower physical value"); Near(chain.modifiers[0].p[1], .75f, "Random upper physical value");
                });
                Check("attaching and rebuilding controls preserves out-of-range authored values", () =>
                {
                    var q = inst.model.parameters; q.volume = 6; q.attack = 30; q.vibratoRate = 999; q.vibratoDepth = 1200; q.unisonDetune = 1000;
                    q.SetParameterEnvelope("volume", new ZUIEnvelopeData(0, 30, 0, 6));
                    string before = JsonUtility.ToJson(inst.model); window.BuildPane(); Layout(); window.BuildPane(); Layout();
                    Need(JsonUtility.ToJson(inst.model) == before, "Control construction clamped or rewrote existing values");
                    inst.model.synthMode = SynthMode.FM; inst.model.parameters.type = InstrumentType.FM; inst.model.parameters.fmOperators[0].freqRatio = 64;
                    before = JsonUtility.ToJson(inst.model); window.BuildPane(); Layout(); Need(JsonUtility.ToJson(inst.model) == before, "FM control construction rewrote imported operator settings");
                });
                Check("hidden-range curves display preserved out-of-range values inside the graph", () =>
                {
                    Mode("synth-instFilterCutoff", ZUIValue.Mode.Curve);
                    string before = JsonUtility.ToJson(inst.model); int undo = Undo.GetCurrentGroup();
                    ExpandCurve("synth-instFilterCutoff");
                    var cutoff = Find<ZuiValueControl>("synth-instFilterCutoff");
                    var graph = cutoff.Q<ZuiEnvelope>();
                    Need(graph != null && graph.HasLayout, "Expanded cutoff graph has no layout");
                    foreach (var point in cutoff.Value.points)
                        Need(graph.contentRect.Contains(graph.PointToLocal(point.time, point.value)), "Preserved cutoff point is painted outside its graph");
                    Need(JsonUtility.ToJson(inst.model) == before && Undo.GetCurrentGroup() == undo, "Fitting the display changed authored values or Undo");
                    foreach (bool hidden in new[] { false, true })
                    {
                        var value = new ZUIValue(7) { mode = ZUIValue.Mode.Curve, yMin = 7, yMax = 7 };
                        value.points.Clear(); value.points.Add(new ZUIEnvelopePoint(0, 7)); value.points.Add(new ZUIEnvelopePoint(1, 7));
                        var control = new ZuiValueControl("Display range fixture", value, new ZuiValueControl.Options { absMin = 7, absMax = 7, hideCurveRange = hidden }, "Envelope display range regression fixture.");
                        window.rootVisualElement.Add(control);
                        try
                        {
                            control.IsExpanded = true; Layout();
                            var envelope = control.Q<ZuiEnvelope>();
                            var options = (ZuiEnvelopeOptions)typeof(ZuiEnvelope).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(envelope);
                            Near(value.yMin, 7, "Display fitting changed the authored minimum"); Near(value.yMax, 7, "Display fitting changed the authored maximum");
                            if (hidden)
                            {
                                Need(options.yMax > options.yMin, "Hidden zero-span range remained degenerate");
                                Need(envelope.contentRect.Contains(envelope.PointToLocal(0, 7)), "Zero-span point is outside the graph");
                            }
                            else { Near(options.yMin, 7, "Visible range minimum changed"); Near(options.yMax, 7, "Visible range maximum changed"); }
                        }
                        finally { control.RemoveFromHierarchy(); }
                    }
                });
                Check("legacy enabled-empty envelopes display Static without migrating or seeding source data", () =>
                {
                    inst.schemaVersion = 0; inst.model = null; inst.type = InstrumentType.Synth;
                    inst.blend = .31f; inst.pulseWidth = .43f;
                    inst.blendEnvelopeData = new ZUIEnvelopeData(0, 1, 0, 1, false) { enabled = true };
                    inst.pulseWidthEnvelopeData = new ZUIEnvelopeData(0, 1, 0, 1, false) { enabled = true };
                    window.legacyInstrumentViews.Clear();
                    string before = JsonUtility.ToJson(ZTrackerMigration.Capture(inst)); bool dirty = EditorUtility.IsDirty(inst);
                    for (int build = 0; build < 2; build++)
                    {
                        window.BuildPane(); Layout();
                        foreach (var name in new[] { "synth-blend", "synth-pulseWidth" })
                        {
                            var control = Find<ZuiValueControl>(name);
                            Need(control.Value.mode == ZUIValue.Mode.Static, "Enabled-empty legacy envelope displayed as Curve " + name);
                            Need(!Visible(Find<VisualElement>(name + "-timing")), "Empty envelope exposed timing controls " + name);
                        }
                        Near(Find<ZuiValueControl>("synth-blend").Value.staticValue, 31, "Legacy blend scalar changed");
                        Near(Find<ZuiValueControl>("synth-pulseWidth").Value.staticValue, 43, "Legacy pulse scalar changed");
                    }
                    Need(inst.schemaVersion == 0 && inst.model == null, "Read-only display migrated the legacy asset");
                    Need(inst.blendEnvelopeData.enabled && inst.blendEnvelopeData.Count == 0 && inst.pulseWidthEnvelopeData.Count == 0, "Display seeded or disabled authored empty envelopes");
                    Need(JsonUtility.ToJson(ZTrackerMigration.Capture(inst)) == before && EditorUtility.IsDirty(inst) == dirty, "Legacy display changed authored payload or dirty state");
                });
                Check("malformed null envelope points remain read-only until explicit curve activation", () =>
                {
                    AuthoredCurve(); var malformed = Curve();
                    typeof(ZUIEnvelopeData).GetField("m_points", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(malformed, null);
                    window.BuildPane(); Layout();
                    Need(malformed.points == null && Find<ZuiValueControl>("synth-instFilterCutoff").Value.mode == ZUIValue.Mode.Static, "Display changed the malformed list or exposed a curve");
                    Mode("synth-instFilterCutoff", ZUIValue.Mode.Curve);
                    Need(Curve().enabled && Curve().points != null && Curve().points.Count == 2, "Explicit activation did not create a valid detached curve");
                    Need(malformed.points == null, "Activation modified the old envelope object in place");
                });
                Check("collapsed envelopes occupy one row and only the expanded value shows timing without data or Undo changes", () =>
                {
                    AuthoredCurve(); inst.model.parameters.SetParameterEnvelope("volume", Curve().DeepCopy());
                    window.BuildPane(); Layout();
                    var cutoff = Find<ZuiValueControl>("synth-instFilterCutoff"); var volume = Find<ZuiValueControl>("synth-volume");
                    cutoff.IsExpanded = volume.IsExpanded = false; Layout();
                    string before = JsonUtility.ToJson(inst.model); int undo = Undo.GetCurrentGroup(); int changes = 0;
                    cutoff.ExpansionChanged += () => changes++;
                    var host = cutoff.parent;
                    Need(host.worldBound.height > 0 && host.worldBound.height <= 42, "Collapsed envelope is not one compact row: " + host.worldBound.height);
                    Need(host.worldBound.width <= 270, "Collapsed envelope kept expanded width: " + host.worldBound.width);
                    Need(cutoff.Query<VisualElement>().ToList().Where(Visible).All(e => e.worldBound.width <= 0 || e.worldBound.xMax <= host.worldBound.xMax + 1.5f), "Collapsed envelope label or thumbnail extends beyond its row");
                    Need(!Visible(Find<VisualElement>("synth-instFilterCutoff-timing")) && !Visible(Find<VisualElement>("synth-volume-timing")), "Collapsed curve exposed timing controls");
                    float collapsedHeight = host.worldBound.height;
                    ExpandCurve("synth-instFilterCutoff");
                    Need(host.worldBound.height > collapsedHeight + 80, "Expanded curve did not expose its graph and timing");
                    Need(!volume.IsExpanded && !Visible(Find<VisualElement>("synth-volume-timing")), "Expanding one parameter expanded its neighbour");
                    cutoff.IsExpanded = false; Layout();
                    Need(host.worldBound.height <= 42 && !Visible(Find<VisualElement>("synth-instFilterCutoff-timing")), "Collapsing did not return to one row");
                    Need(changes == 2, "Expansion change notifications were missing or duplicated");
                    Need(JsonUtility.ToJson(inst.model) == before && Undo.GetCurrentGroup() == undo, "View expansion modified authored data or Undo history");
                });
                Check("physical curve summaries and scales are readable and endpoint handles are inset", () =>
                {
                    AuthoredCurve();
                    Curve().points.Clear();
                    float nyquist = AudioSettings.outputSampleRate * .5f;
                    Curve().points.Add(new ZUIEnvelopePoint(0, 6000 / nyquist)); Curve().points.Add(new ZUIEnvelopePoint(1, 800 / nyquist));
                    window.BuildPane(); Layout();
                    var value = Find<ZuiValueControl>("synth-instFilterCutoff"); value.IsExpanded = false; Layout();
                    var summary = value.Q<Label>(className: "zui-value__curve-summary");
                    Need(summary != null && summary.text == "6000 → 800 Hz", "Collapsed curve lost its physical start/end values");
                    Need(summary.resolvedStyle.fontSize >= 12 && value.Q(className: "zui-value__curve-field").resolvedStyle.borderTopWidth > 0, "Collapsed value is unframed or too small");
                    ExpandCurve("synth-instFilterCutoff");
                    var graph = value.Q<ZuiEnvelope>();
                    foreach (var point in value.Value.points)
                    {
                        var p = graph.PointToLocal(point.time, point.value);
                        Need(p.x >= 8 && p.x <= graph.contentRect.width - 8 && p.y >= 8 && p.y <= graph.contentRect.height - 8, "Point handle touches the plot outer edge");
                    }
                    var ticks = value.Query<Label>(className: "zui-value__curve-tick").ToList();
                    Need(ticks.Count == 2 && ticks.All(t => t.text.EndsWith(" Hz") && t.resolvedStyle.fontSize >= 12), "Physical vertical scale is missing or unreadable");
                    Need(value.Q<Label>(className: "zui-value__curve-axis").resolvedStyle.fontSize >= 12, "Progress caption is too small");
                    // The legacy flat Nyquist value is above the editing range and must remain visible unchanged.
                    Curve().points[0].value = Curve().points[1].value = 1;
                    window.BuildPane(); Layout(); value = Find<ZuiValueControl>("synth-instFilterCutoff"); value.IsExpanded = false; Layout();
                    Need(value.Q<Label>(className: "zui-value__curve-summary").text == nyquist.ToString("0") + " Hz", "Flat curve does not show one physical value");
                    string before = JsonUtility.ToJson(inst.model); ExpandCurve("synth-instFilterCutoff"); graph = value.Q<ZuiEnvelope>();
                    Need(graph.PointToLocal(0, nyquist).y >= 8, "Preserved out-of-range flat curve lies on the top edge");
                    Need(JsonUtility.ToJson(inst.model) == before, "Reading the physical graph changed saved values");
                });
                Check("framed thumbnail pointer clicks and multi-move curve drags preserve complete Undo and Redo", () =>
                {
                    AuthoredCurve();
                    for (int cycle = 0; cycle < 2; cycle++)
                    {
                        var control = Find<ZuiValueControl>("synth-instFilterCutoff"); control.IsExpanded = false; Layout();
                        var frame = control.Q(className: "zui-value__curve-field");
                        using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = frame.worldBound.center })) { down.target = frame; frame.SendEvent(down); }
                        using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = frame.worldBound.center })) { up.target = frame; frame.SendEvent(up); }
                        Layout(); Need(control.IsExpanded, "Clicking thumbnail did not expand its curve");
                        var graph = control.Q<ZuiEnvelope>(); var world = graph.LocalToWorld(graph.PointToLocal(0, control.Value.points[0].value));
                        string before = JsonUtility.ToJson(inst.model);
                        using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = world, clickCount = 1 })) { down.target = graph; graph.SendEvent(down); }
                        for (int move = 1; move <= 4; move++)
                            using (var drag = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = world + new Vector2(0, -3 * move), delta = new Vector2(0, -3) })) { drag.target = graph; graph.SendEvent(drag); }
                        using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = world + new Vector2(0, -12) })) { up.target = graph; graph.SendEvent(up); }
                        string after = JsonUtility.ToJson(inst.model); Need(after != before, "Pointer drag did not change the envelope");
                        Undo.PerformUndo(); Layout(); Need(JsonUtility.ToJson(inst.model) == before, "One Undo did not restore the complete curve drag");
                        Undo.PerformRedo(); Layout(); Need(JsonUtility.ToJson(inst.model) == after, "Redo restored an intermediate drag value");
                        Undo.PerformUndo(); Layout();
                    }
                });
                Check("every section enable switch hides only its body and preserves settings through two cycles and Undo", () =>
                {
                    void Verify(string prefix, Func<bool> read, Action<bool> write)
                    {
                        write(false); window.BuildPane(); ZuiAudit.ExpandAll(window); Layout(); Undo.ClearUndo(inst);
                        for (int cycle = 0; cycle < 2; cycle++)
                        {
                            var body = Find<VisualElement>(prefix + "-body");
                            Need(!Visible(body) && Visible(Find<Button>(prefix + "-enabled")), prefix + " off body/header visibility");
                            string before = JsonUtility.ToJson(inst.model);
                            write(true); string expected = JsonUtility.ToJson(inst.model); write(false);
                            Click(prefix + "-enabled"); Layout();
                            Need(read() && Visible(Find<VisualElement>(prefix + "-body")), prefix + " did not expose enabled body");
                            Need(JsonUtility.ToJson(inst.model) == expected, prefix + " enabling changed other authored settings");
                            Undo.PerformUndo(); Layout();
                            Need(!read() && !Visible(Find<VisualElement>(prefix + "-body")) && JsonUtility.ToJson(inst.model) == before, prefix + " Undo failed");
                            Undo.PerformRedo(); Layout(); Need(read() && Visible(Find<VisualElement>(prefix + "-body")) && JsonUtility.ToJson(inst.model) == expected, prefix + " Redo failed");
                            Click(prefix + "-enabled"); Layout(); Need(!read() && JsonUtility.ToJson(inst.model) == before, prefix + " disabling changed authored settings");
                            ZuiAudit.ExpandAll(window); Layout(); Need(!Visible(Find<VisualElement>(prefix + "-body")), prefix + " ExpandAll revealed disabled settings");
                        }
                    }
                    Verify("synth-filter", () => inst.model.parameters.instFilterEnabled, v => inst.model.parameters.instFilterEnabled = v);
                    Verify("synth-blend-envelope", () => inst.model.parameters.blendEnvelope, v => inst.model.parameters.blendEnvelope = v);
                    inst.model.family = InstrumentFamily.Sampler; inst.model.parameters.type = InstrumentType.Sample;
                    Verify("sampler-filter", () => inst.model.parameters.instFilterEnabled, v => inst.model.parameters.instFilterEnabled = v);
                    inst.model.sampler.samples.Add(new SampleData { id = "section-sample", name = "Section sample" });
                    inst.model.sampler.zones.Add(new Keyzone { id = "section-zone", sample = 0, blend = new SampleBlendExtension { attack = .123f, amount = .37f } });
                    window.sampleId = "section-sample"; window.zoneId = "section-zone"; window.instrumentTab = 1;
                    Verify("paired-envelope", () => inst.model.sampler.zones[0].blend.envelopeEnabled, v => inst.model.sampler.zones[0].blend.envelopeEnabled = v);
                    inst.model.modulation.Add(new ModulationSet { id = "section-mod", name = "Section modulation" });
                    inst.model.modulation[0].devices.Add(new ModulationDevice { id = "section-device", kind = ModulationDeviceKind.AHDSR, attack = .137f });
                    window.instrumentTab = 2; window.modSetId = "section-mod"; window.modDeviceId = "section-device";
                    foreach (var kind in new[] { ModulationDeviceKind.AHDSR, ModulationDeviceKind.LFO, ModulationDeviceKind.Velocity, ModulationDeviceKind.KeyTracking, ModulationDeviceKind.Fader, ModulationDeviceKind.Multipoint })
                    {
                        inst.model.modulation[0].devices[0].kind = kind;
                        Verify("mod", () => inst.model.modulation[0].devices[0].enabled, v => inst.model.modulation[0].devices[0].enabled = v);
                    }
                    inst.model.parameters.presets.Add(new ZTrackerInstrument.InstrumentPreset { name = "Retained settings", volume = .37f });
                    window.instrumentTab = 5; window.presetIndex = 0;
                    foreach (var fieldName in new[] { "ovrVolPan", "ovrSampleParams", "ovrSynthParams", "ovrBlend", "ovrPulseWidth", "ovrBRatio", "ovrPMDepth", "ovrDetune", "ovrAdsr", "ovrVibrato", "ovrEffects" })
                    {
                        var field = typeof(ZTrackerInstrument.InstrumentPreset).GetField(fieldName);
                        Verify("preset-" + fieldName, () => (bool)field.GetValue(inst.model.parameters.presets[0]), v => field.SetValue(inst.model.parameters.presets[0], v));
                    }
                });
                Check("UI audit catches horizontal clipping in vertical scroll panes without rejecting horizontal workspaces", () =>
                {
                    var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "instrument-rework-audit-fixture" };
                    scroll.style.position = Position.Absolute; scroll.style.left = 0; scroll.style.top = 0;
                    scroll.style.width = 300; scroll.style.height = 90;
                    scroll.horizontalScrollerVisibility = scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                    var wide = new VisualElement(); wide.style.width = 500; wide.style.height = 20; wide.style.flexShrink = 0;
                    scroll.Add(wide); window.rootVisualElement.Add(scroll);
                    bool Clipped() => ZuiAudit.Audit(window).Any(f => f.check == "horizontal-clipping" && f.element.Contains("instrument-rework-audit-fixture"));
                    try
                    {
                        Layout(); Need(scroll.contentViewport.worldBound.width > 200, "Audit fixture was not laid out");
                        Need(Clipped(), "Vertical pane silently accepted content wider than its viewport");
                        scroll.contentContainer.style.width = 280; Layout();
                        Need(Clipped(), "Constrained content root hid its overflowing child from the audit");
                        wide.style.width = 250; Layout(); Need(!Clipped(), "Fitting vertical content produced a false clipping finding");
                        wide.style.width = 500; scroll.mode = ScrollViewMode.Horizontal; Layout();
                        Need(!Clipped(), "Intentional horizontal workspace was treated as clipped");
                    }
                    finally { scroll.RemoveFromHierarchy(); Layout(); }
                });
            }
            finally
            {
                window.StopPreview(); window.Close(); DestroyImmediate(window);
                Undo.ClearUndo(inst); Undo.ClearUndo(songAsset); DestroyImmediate(inst); DestroyImmediate(songAsset);
            }
            report.AppendLine("TOTAL passed=" + passed + " failed=" + failed); return report.ToString();
        }
    }
}
