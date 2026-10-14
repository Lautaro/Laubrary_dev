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
        /// <summary>The instrument page's envelope work of T-0635/T-0636/T-0638/T-0639/T-0640, probed in a disposable
        /// window: the level meter is there; the ADSR is drawn to time (attack 0 is a vertical wall; the time grid and
        /// the later stages follow every edit); the envelope's Maximum (offered only where the engine allows more,
        /// stored, Undo); ADSR as a value mode on every envelope-capable slider (stored on the envelope, Undo, reload);
        /// and the FM Level tooltips and faded Level-0 operators. Reflection entry point; nothing is saved.</summary>
        public static string CheckEnvelopeModes()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>();
            var songAsset = CreateInstance<ZTrackerSong>();
            var inst = CreateInstance<ZTrackerInstrument>();
            inst.schemaVersion = songAsset.schemaVersion = 1;
            inst.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSongNew(inst);
            window.song = songAsset; window.instrument = inst; window.pane = 3; window.instrumentTab = 0;
            window.Show(); window.position = new Rect(160, 120, 1120, 760); window.CreateGUI();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Layout() { var panel = window.rootVisualElement.panel; Need(panel != null, "Window not attached"); for (int i = 0; i < 3; i++) panel.GetType().GetMethod("ValidateLayout", flags)?.Invoke(panel, null); }
            void Rebuild() { window.BuildPane(); ZuiAudit.ExpandAll(window); Layout(); }
            void Fresh(bool fm = false)
            {
                window.StopPreview(); Undo.ClearUndo(inst); Undo.ClearUndo(songAsset);
                inst.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSongNew(inst);
                if (fm) { inst.model.synthMode = SynthMode.FM; inst.model.parameters.type = InstrumentType.FM; inst.model.parameters.fmAlgorithm = TrackerFmSetups.Base + 4; }
                window.pane = 3; window.instrumentTab = 0; window.modSetId = window.modDeviceId = "";
                Rebuild();
            }
            void Check(string name, Action action)
            {
                try { Fresh(); action(); passed++; report.AppendLine("PASS " + name); }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + name + ": " + (ex.InnerException ?? ex).Message); }
            }
            ZuiValueControl Value(string name) { var c = window.rootVisualElement.Q<ZuiValueControl>(name); Need(c != null, "Missing value control " + name); return c; }
            ZuiValueControl.Options Opt(ZuiValueControl c) => (ZuiValueControl.Options)typeof(ZuiValueControl).GetField("_opt", flags).GetValue(c);
            void Mode(ZuiValueControl c, ZUIValue.Mode mode) => typeof(ZuiValueControl).GetMethod("SetMode", flags).Invoke(c, new object[] { mode });
            ZUIEnvelopeData Env(string key) => inst.model.parameters.GetParameterEnvelope(key);
            // A slider edit as the user makes it (the change notification fires, so the host stores it).
            void Commit(ZuiMicroSlider slider, float v) => typeof(ZuiMicroSlider).GetMethod("SetValue", flags).Invoke(slider, new object[] { v, true });
            try
            {
                Check("T-0635: the window shows the shared level meter", () =>
                {
                    var meter = window.rootVisualElement.Q<ZuiLevelMeter>("level-meter");
                    Need(meter != null && meter.worldBound.width > 200 && meter.worldBound.height >= 10, "Meter missing or not laid out: " + meter?.worldBound);
                    Need(meter.Readout.text.StartsWith("Peak"), "Readout " + meter.Readout.text);
                });
                Check("T-0638: attack 0 is a vertical wall; changing decay moves release and the time grid follows", () =>
                {
                    var q = inst.model.parameters; q.attack = 0; q.decay = .2f; q.release = .5f; Rebuild();
                    var adsr = window.rootVisualElement.Q<ZuiAdsr>("amplitude-adsr"); Need(adsr != null, "No amplifier ADSR");
                    var start = adsr.StageSpan(0); var a = adsr.HandlePosition(0);
                    Need(Math.Abs(a.x - start.x) < .01f && Math.Abs(start.y - start.x) < .01f, "Attack 0 is not vertical: start " + start + " peak " + a);
                    float releaseStart = adsr.StageSpan(4).x; string scale = adsr.ScaleText;
                    Commit(adsr.Q<ZuiMicroSlider>("adsr-input-decay"), 2000f); Layout();
                    float releaseAfter = adsr.StageSpan(4).x; string scaleAfter = adsr.ScaleText;
                    report.AppendLine("MEASURE decay 200 ms -> 2 s: release starts at x " + releaseStart.ToString("0.0") + " -> " + releaseAfter.ToString("0.0") + ", grid " + scale + " -> " + scaleAfter + "; stored decay " + q.decay);
                    Need(Math.Abs(q.decay - 2f) < .001f, "Decay slider did not store 2 s: " + q.decay);
                    Need(Math.Abs(releaseAfter - releaseStart) > 5 && scaleAfter != scale, "Release/time grid did not follow the decay");
                    q.attack = .3f; adsr.Refresh();
                    Need(adsr.HandlePosition(0).x - adsr.StageSpan(0).x > 3, "A 300 ms attack is drawn as a wall");
                });
                Check("T-0640: Maximum is offered where the engine allows more (Volume) and not on fixed ranges (Pan); it is stored, clamps higher points, Undo restores", () =>
                {
                    var volume = Value("synth-volume"); var pan = Value("synth-pan");
                    Need(Opt(volume).editableMaximum && Opt(volume).maximumLimit == 1600f, "Volume does not offer Maximum");
                    Need(!Opt(pan).editableMaximum, "Pan offers Maximum");
                    Mode(volume, ZUIValue.Mode.Curve); Need(Env("volume")?.enabled == true, "Envelope mode not stored");
                    volume = Value("synth-volume"); volume.SetMaximum(400f);
                    var env = Env("volume"); Need(env.customMaximum && Math.Abs(env.yMax - 4f) < 1e-5f, "Maximum 400% not stored: " + env.yMax + " custom " + env.customMaximum);
                    volume.Value.points[0].value = 350f; volume.SetMaximum(50f);
                    env = Env("volume"); Need(Math.Abs(env.yMax - .5f) < 1e-5f && env.points.All(p => p.value <= .5f + 1e-6f), "Lowering the maximum left points above it: " + string.Join(",", env.points.Select(p => p.value)));
                    Need(volume.ClampMaximum(1e6f) == 1600f && volume.ClampMaximum(-5f) > 0 && volume.ClampMaximum(float.NaN) == null, "Validation");
                    Undo.PerformUndo(); Need(Math.Abs(Env("volume").yMax - 4f) < 1e-5f, "Undo did not bring 400% back: " + Env("volume").yMax);
                    Rebuild(); volume = Value("synth-volume"); Need(Math.Abs(volume.Value.yMax - 400f) < .01f, "The maximum did not survive a rebuild: " + volume.Value.yMax);
                });
                Check("T-0639: every envelope-capable slider offers ADSR (synth and FM)", () =>
                {
                    var names = window.rootVisualElement.Query<ZuiValueControl>().ToList().Where(c => !string.IsNullOrEmpty(c.name)).ToList();
                    Need(names.Count >= 8 && names.All(c => Opt(c).adsr != null), "Without ADSR: " + string.Join(", ", names.Where(c => Opt(c).adsr == null).Select(c => c.name)));
                    Fresh(true);
                    var fm = window.rootVisualElement.Query<ZuiValueControl>().ToList().Where(c => c.name != null && c.name.StartsWith("fmOperators.")).ToList();
                    Need(fm.Count >= 4 && fm.All(c => Opt(c).adsr != null), "FM operator values without ADSR: " + fm.Count);
                    report.AppendLine("MEASURE ADSR offered on " + names.Count + " synth values and " + fm.Count + " FM operator values");
                });
                Check("T-0639: Volume as an ADSR: stored on its envelope, stage edits reshape it with Undo, it reloads as ADSR, Static keeps its stages", () =>
                {
                    var volume = Value("synth-volume"); Mode(volume, ZUIValue.Mode.Adsr);
                    var env = Env("volume"); Need(env != null && env.enabled && env.adsr && env.points.Count == 4 && env.sustainEnabled, "ADSR not stored as an envelope");
                    volume = Value("synth-volume"); var view = volume.AdsrControl; Need(view != null, "No ADSR control in ADSR mode");
                    Need(volume.Q<ZuiMicroSlider>("zui-value-adsr-start") != null && volume.Q<ZuiMicroSlider>("zui-value-adsr-peak") != null, "Peak/Start sliders missing");
                    // In the Amplifier's narrow column the ADSR mode fits its column: nothing runs into Pan, readouts are whole.
                    Layout(); var host = volume.parent; var pan = Value("synth-pan");
                    Need(volume.worldBound.xMax <= host.worldBound.xMax + .5f && !volume.worldBound.Overlaps(pan.worldBound) && Math.Abs(view.worldBound.width - 248) < 1, "ADSR mode runs into Pan or is squeezed: " + volume.worldBound + " / " + pan.worldBound);
                    foreach (var s in volume.Query<ZuiMicroSlider>().ToList())
                    {
                        var caption = s.Q<Label>(className: "zui-microslider__caption"); var readout = s.Q<Label>(className: "zui-microslider__value"); if (caption == null || readout == null) continue;
                        float cw = caption.MeasureTextSize(caption.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x, vw = readout.MeasureTextSize(readout.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                        Need(readout.worldBound.xMax - vw - (caption.worldBound.xMin + cw) >= 2, s.name + " readout '" + readout.text + "' runs into '" + caption.text + "' at " + s.worldBound.width + " px");
                    }
                    report.AppendLine("MEASURE ADSR mode in the Amplifier column: control " + volume.worldBound.width + " px, envelope " + view.worldBound.width + " px");
                    Commit(view.Q<ZuiMicroSlider>("adsr-input-attack"), 300f); env = Env("volume");
                    Need(Math.Abs(env.adsrAttack - .3f) < 1e-4f && Math.Abs(env.points[1].time - .3f) < 1e-4f && Math.Abs(env.points[1].value - inst.model.parameters.volume) < 1e-5f, "Attack edit did not reshape: " + env.adsrAttack + " / " + env.points[1].time);
                    Undo.PerformUndo(); Need(Math.Abs(Env("volume").adsrAttack - .01f) < 1e-4f, "Undo of the attack: " + Env("volume").adsrAttack);
                    Undo.PerformRedo(); Need(Math.Abs(Env("volume").adsrAttack - .3f) < 1e-4f, "Redo of the attack: " + Env("volume").adsrAttack);
                    Rebuild(); volume = Value("synth-volume"); Need(volume.Value.mode == ZUIValue.Mode.Adsr && volume.AdsrControl != null, "Did not reload as ADSR");
                    Mode(volume, ZUIValue.Mode.Static); env = Env("volume"); Need(!env.enabled && Math.Abs(env.adsrAttack - .3f) < 1e-4f, "Static lost the stages");
                    Rebuild(); volume = Value("synth-volume"); Mode(volume, ZUIValue.Mode.Adsr); Need(Math.Abs(Env("volume").adsrAttack - .3f) < 1e-4f, "Switching back lost the stages");
                    Mode(Value("synth-volume"), ZUIValue.Mode.Curve); env = Env("volume"); Need(env.enabled && !env.adsr && env.points.Count == 4, "Envelope mode did not start from the ADSR shape: " + env.points.Count);
                });
                Check("T-0636: FM Level tooltips say carrier = loudness, modulator = depth; a modulator at Level 0 is faded in the diagram", () =>
                {
                    Fresh(true);
                    var carrier = Value("fmOperators.3.level"); var modulator = Value("fmOperators.1.level");
                    Need(carrier.tooltip.Contains("silences the instrument"), "Carrier tip: " + carrier.tooltip);
                    Need(modulator.tooltip.Contains("not a volume") && modulator.tooltip.Contains("plainer tone"), "Modulator tip: " + modulator.tooltip);
                    var q = inst.model.parameters; var b = q.fmOperators[1]; b.level = 0; q.fmOperators[1] = b; Rebuild();
                    var node = window.rootVisualElement.Q<Label>("fm-routing-node-1"); var c = window.rootVisualElement.Q<Label>("fm-routing-node-3");
                    Need(node != null && Math.Abs(node.resolvedStyle.opacity - .35f) < .01f && Math.Abs(c.resolvedStyle.opacity - 1f) < .01f, "B at Level 0 not faded: " + node?.resolvedStyle.opacity);
                    Need(node.tooltip.Contains("bends nothing"), "Node tip: " + node.tooltip);
                });
            }
            finally
            {
                window.StopPreview(); window.Close();
                Undo.ClearUndo(inst); Undo.ClearUndo(songAsset); DestroyImmediate(inst); DestroyImmediate(songAsset);
            }
            return "CheckEnvelopeModes passed=" + passed + " failed=" + failed + "\n" + report;
        }
    }
}
