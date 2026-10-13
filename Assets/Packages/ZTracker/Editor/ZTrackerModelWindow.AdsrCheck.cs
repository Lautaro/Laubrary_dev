using System;
using System.Collections.Generic;
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
        /// The synth amplitude envelope as it was laid out before T-0609 (title, 112 px graph with stage
        /// captions, a row of 82 px sliders), measured at 434 x 150 in a 1120 px window like this check's.
        const float AdsrAreaBeforeT0609 = 434f * 150f;

        /// <summary>T-0609: the compact ADSR. Size against the old one, title, steps and units, stored values
        /// left alone until edited, slider and lane resets with Undo, a Shift-fine drag that never freezes on a
        /// step, and every place the control is used. Disposable assets; callable by reflection.</summary>
        public static string CheckCompactAdsr()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>();
            var songAsset = CreateInstance<ZTrackerSong>();
            var inst = CreateInstance<ZTrackerInstrument>();
            var clip = AudioClip.Create("ADSR probe", 4800, 1, 48000, false);
            inst.schemaVersion = songAsset.schemaVersion = 1;
            inst.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSong(inst);
            window.song = songAsset; window.instrument = inst; window.pane = 3; window.instrumentTab = 0;
            window.Show(); window.position = new Rect(160, 120, 1120, 760); window.CreateGUI();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Near(float actual, float expected, string message) => Need(Math.Abs(actual - expected) < .00001f, message + " (" + actual + " != " + expected + ")");
            void Layout() { var panel = window.rootVisualElement.panel; Need(panel != null, "Window not attached"); for (int i = 0; i < 3; i++) panel.GetType().GetMethod("ValidateLayout", flags)?.Invoke(panel, null); }
            void Fresh(float width)
            {
                window.StopPreview(); Undo.ClearUndo(inst); Undo.ClearUndo(songAsset);
                inst.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSong(inst);
                window.position = new Rect(160, 120, width, 760); window.pane = 3; window.instrumentTab = 0; window.modSetId = window.modDeviceId = "";
                window.BuildPane(); ZuiAudit.ExpandAll(window); Layout();
            }
            void Check(string name, Action action)
            {
                try { Fresh(1120); action(); passed++; report.AppendLine("PASS " + name); }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + name + ": " + (ex.InnerException ?? ex).Message); }
            }
            ZuiAdsr Find(string name) { var control = window.rootVisualElement.Q<ZuiAdsr>(name); Need(control != null, "Missing envelope " + name); return control; }
            ZuiMicroSlider Slider(ZuiAdsr adsr, string stage) { var s = adsr.Q<ZuiMicroSlider>("adsr-input-" + stage); Need(s != null, "Missing " + stage + " slider"); return s; }
            string Readout(ZuiMicroSlider s) => s.Q<Label>(className: "zui-microslider__value").text;
            void Send(VisualElement target, EventBase e) { using (e) { e.target = target; target.SendEvent(e); } }
            void Press(VisualElement target, Vector2 world, int clicks = 1, bool shift = false)
                => Send(target, PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = clicks, mousePosition = world, modifiers = shift ? EventModifiers.Shift : EventModifiers.None }));
            void Drag(VisualElement target, Vector2 world, bool shift = false)
                => Send(target, PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = world, modifiers = shift ? EventModifiers.Shift : EventModifiers.None }));
            void Release(VisualElement target, Vector2 world)
                => Send(target, PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = world }));
            float[] Stages() { var q = inst.model.parameters; return new[] { q.attack, q.decay, q.sustain, q.release }; }
            // Every stage letter and readout lies whole inside its own slider, and the letter and readout keep a
            // clear gap: nothing overlaps or is clipped. Returns the narrowest gap found, for the report.
            float Legible(ZuiAdsr adsr, string where)
            {
                float narrowest = float.MaxValue;
                var title = adsr.Q<Label>();
                if (title != null && title.parent == adsr) Need(title.worldBound.xMax <= adsr.worldBound.xMax + .5f && title.worldBound.width > 1, where + ": title clipped");
                foreach (var s in adsr.Query<ZuiMicroSlider>().ToList())
                {
                    var caption = s.Q<Label>(className: "zui-microslider__caption"); var value = s.Q<Label>(className: "zui-microslider__value");
                    Need(caption != null && value != null, where + ": " + s.name + " lacks its texts");
                    Need(Math.Abs(caption.resolvedStyle.fontSize - ZuiAdsr.SliderFontSize) < .01f && Math.Abs(value.resolvedStyle.fontSize - ZuiAdsr.SliderFontSize) < .01f, where + ": " + s.name + " font " + caption.resolvedStyle.fontSize);
                    Rect box = s.worldBound, c = caption.worldBound, v = value.worldBound;
                    // The text's own measured width, so a label box wider than its text cannot hide a collision.
                    float cw = caption.MeasureTextSize(caption.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                    float vw = value.MeasureTextSize(value.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                    float captionRight = c.xMin + cw, valueLeft = v.xMax - vw;
                    Need(c.xMin >= box.xMin - .5f && v.xMax <= box.xMax + .5f && valueLeft >= box.xMin && captionRight <= box.xMax, where + ": " + s.name + " text clipped (" + caption.text + " / " + value.text + " in " + box.width + ")");
                    Need(c.yMin >= box.yMin - .5f && c.yMax <= box.yMax + .5f && v.yMin >= box.yMin - .5f && v.yMax <= box.yMax + .5f, where + ": " + s.name + " text clipped vertically");
                    float gap = valueLeft - captionRight;
                    Need(gap >= 2f, where + ": " + s.name + " readout '" + value.text + "' runs into '" + caption.text + "' (gap " + gap.ToString("0.0") + ")");
                    narrowest = Math.Min(narrowest, gap);
                }
                return narrowest;
            }
            // The widest readouts each stage can show: 990 ms (the last millisecond step), a long time in
            // seconds near the stage's top, and 100%.
            void Widest(float attack, float decay, float sustain, float release) { var q = inst.model.parameters; q.attack = attack; q.decay = decay; q.sustain = sustain; q.release = release; window.BuildPane(); ZuiAudit.ExpandAll(window); Layout(); }
            void LegibleAtWidth(float width)
            {
                Fresh(width); float gap = Legible(Find("amplitude-adsr"), width + " px defaults");
                Widest(.99f, .99f, 1f, .99f); gap = Math.Min(gap, Legible(Find("amplitude-adsr"), width + " px 990 ms"));
                Widest(4.95f, 9.95f, 1f, 9.95f); gap = Math.Min(gap, Legible(Find("amplitude-adsr"), width + " px long times"));
                Widest(.444f, .888f, .88f, .888f); gap = Math.Min(gap, Legible(Find("amplitude-adsr"), width + " px 440/890 ms"));
                var adsr = Find("amplitude-adsr");
                report.AppendLine("  " + width + " px: envelope " + adsr.worldBound.width + "x" + adsr.worldBound.height + ", narrowest letter-to-readout gap " + gap.ToString("0.0") + " px");
            }
            try
            {
                Check("steps and units: whole ms below 100 ms, 10 ms to 1 s, 0.05 s above; whole percent sustain", () =>
                {
                    Near(ZuiAdsr.SnapSeconds(.0057f), .006f, "5.7 ms"); Near(ZuiAdsr.SnapSeconds(.0994f), .099f, "99.4 ms");
                    Near(ZuiAdsr.SnapSeconds(.437f), .44f, "437 ms"); Near(ZuiAdsr.SnapSeconds(1.234f), 1.25f, "1.234 s");
                    Near(ZuiAdsr.SnapSeconds(0f), 0f, "zero"); Near(ZuiAdsr.SnapLevel(.554f), .55f, "sustain percent");
                    Need(ZuiAdsr.FormatTime(.0057f) == "6 ms" && ZuiAdsr.FormatTime(.25f) == "250 ms" && ZuiAdsr.FormatTime(1.5f) == "1.5 s" && ZuiAdsr.FormatTime(2f) == "2 s" && ZuiAdsr.FormatTime(.9999f) == "1 s",
                        "Readouts: " + ZuiAdsr.FormatTime(.0057f) + ", " + ZuiAdsr.FormatTime(.25f) + ", " + ZuiAdsr.FormatTime(1.5f) + ", " + ZuiAdsr.FormatTime(.9999f));
                });
                Check("compact: at most 30% of the old area, titled ADSR, sliders in one row in stage order", () =>
                {
                    var adsr = Find("amplitude-adsr"); var canvas = adsr.Q("adsr-canvas");
                    float area = adsr.worldBound.width * adsr.worldBound.height;
                    report.AppendLine("  size " + adsr.worldBound.width + "x" + adsr.worldBound.height + " = " + (area / AdsrAreaBeforeT0609 * 100f).ToString("0") + "% of before");
                    Need(area <= AdsrAreaBeforeT0609 * .30f && area >= AdsrAreaBeforeT0609 * .2f, "Area is " + (area / AdsrAreaBeforeT0609 * 100f).ToString("0") + "% of the old control");
                    Need(adsr.Q<Label>()?.text == "ADSR", "Title is " + adsr.Q<Label>()?.text);
                    Need(!window.rootVisualElement.Query<Label>().ToList().Any(l => l.text == "Amplitude"), "An Amplitude title remains");
                    var row = adsr.Q("adsr-sliders"); Need(row.worldBound.yMax <= adsr.worldBound.yMax + .5f && row.worldBound.height < 26, "Sliders wrapped or overflow: " + row.worldBound);
                    var sliders = new[] { "attack", "decay", "sustain", "release" }.Select(n => Slider(adsr, n)).ToArray();
                    // Since T-0638 the graph is drawn to time (each stage as wide as it lasts), so the sliders no longer sit
                    // under a lane each: they stay one row in stage order, an equal share of the width each.
                    for (int i = 1; i < 4; i++) Need(sliders[i].worldBound.xMin >= sliders[i - 1].worldBound.xMax - .5f, sliders[i].name + " is not after " + sliders[i - 1].name);
                    Need(Math.Abs(sliders[0].worldBound.xMin - canvas.worldBound.xMin) < 2 && Math.Abs(sliders[3].worldBound.xMax - canvas.worldBound.xMax) < 3, "Slider row does not span the graph");
                    Need(Readout(sliders[0]) == "10 ms" && Readout(sliders[1]) == "200 ms" && Readout(sliders[2]) == "70%" && Readout(sliders[3]) == "500 ms",
                        "Default readouts " + string.Join(", ", sliders.Select(Readout)));
                    Legible(adsr, "defaults");
                });
                Check("no letter or readout overlaps or is clipped, at 1120 px and at the 760 px minimum window, widest values", () =>
                {
                    LegibleAtWidth(1120); LegibleAtWidth(760);
                });
                Check("a value stored before the steps is shown rounded and left untouched until edited", () =>
                {
                    var q = inst.model.parameters; q.attack = .0057f; q.decay = 1.2345f; q.sustain = .123f; window.BuildPane(); Layout();
                    var adsr = Find("amplitude-adsr");
                    Need(Readout(Slider(adsr, "attack")) == "6 ms" && Readout(Slider(adsr, "decay")) == "1.25 s" && Readout(Slider(adsr, "sustain")) == "12%",
                        "Readouts " + Readout(Slider(adsr, "attack")) + ", " + Readout(Slider(adsr, "decay")) + ", " + Readout(Slider(adsr, "sustain")));
                    Near(q.attack, .0057f, "Building rewrote attack"); Near(q.decay, 1.2345f, "Building rewrote decay"); Near(q.sustain, .123f, "Building rewrote sustain");
                    var set = typeof(ZuiMicroSlider).GetMethod("SetValue", flags);
                    set.Invoke(Slider(adsr, "attack"), new object[] { 437f, true }); Near(q.attack, .44f, "Typed 437 ms");
                    set.Invoke(Slider(adsr, "release"), new object[] { 1234f, true }); Near(q.release, 1.25f, "Typed 1234 ms");
                    set.Invoke(Slider(adsr, "sustain"), new object[] { 55.4f, true }); Near(q.sustain, .55f, "Typed 55.4%");
                    Near(q.decay, 1.2345f, "Editing other stages rewrote decay");
                });
                Check("double-click resets each slider and each lane to the declared default, with Undo and Redo", () =>
                {
                    var q = inst.model.parameters; var defaults = Stages(); q.attack = .3f; q.decay = .9f; q.sustain = .2f; q.release = 2f; window.BuildPane(); Layout();
                    var adsr = Find("amplitude-adsr"); var names = new[] { "attack", "decay", "sustain", "release" };
                    for (int i = 0; i < 4; i++)
                    {
                        // Undo and Redo rebuild the page, so find the control again each time.
                        adsr = Find("amplitude-adsr"); var before = Stages(); var s = Slider(adsr, names[i]); Press(s, s.worldBound.center, 2);
                        Near(Stages()[i], defaults[i], names[i] + " slider reset");
                        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Near(Stages()[i], before[i], names[i] + " reset Undo"); Undo.PerformRedo(); Near(Stages()[i], defaults[i], names[i] + " reset Redo");
                    }
                    q.attack = .3f; q.release = 2f; window.BuildPane(); Layout(); adsr = Find("amplitude-adsr"); var canvas = adsr.Q("adsr-canvas");
                    Press(canvas, canvas.LocalToWorld(adsr.LaneCenter(0)), 2); Near(q.attack, defaults[0], "Attack lane double-click");
                    Press(canvas, canvas.LocalToWorld(adsr.LaneCenter(4)), 2); Near(q.release, defaults[3], "Release lane double-click");
                });
                Check("pointer drags: a lane drag edits only its stage onto a step; Shift-fine slider drag keeps moving on 50 ms steps", () =>
                {
                    var adsr = Find("amplitude-adsr"); var canvas = adsr.Q("adsr-canvas"); var before = Stages();
                    Vector2 start = canvas.LocalToWorld(adsr.HandlePosition(4));
                    Press(canvas, start); for (int n = 1; n <= 4; n++) Drag(canvas, start + new Vector2(2 * n, 0)); Release(canvas, start + new Vector2(8, 0));
                    var after = Stages(); Need(after[3] > before[3], "Release did not grow");
                    Near(after[3], ZuiAdsr.SnapSeconds(after[3]), "Release not on a step");
                    for (int i = 0; i < 3; i++) Near(after[i], before[i], "Release drag changed stage " + i);
                    Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Near(Stages()[3], before[3], "Lane drag is not one Undo");
                    inst.model.parameters.release = 2f; window.BuildPane(); Layout(); adsr = Find("amplitude-adsr"); var s = Slider(adsr, "release"); Vector2 p = s.worldBound.center;
                    Press(s, p, 1, true); float last = inst.model.parameters.release; int moved = 0;
                    for (int n = 1; n <= 10; n++) { Drag(s, p + new Vector2(6 * n, 0), true); if (inst.model.parameters.release > last) { moved++; last = inst.model.parameters.release; } }
                    Release(s, p + new Vector2(60, 0));
                    Need(moved >= 3 && last > 2f, "Fine drag froze on the 50 ms steps (moved " + moved + " times, to " + last + ")");
                    Near(last, ZuiAdsr.SnapSeconds(last), "Fine drag left a value off the steps");
                });
                Check("every use is the compact control: FM operators, blend, second sample, AHDSR modulator, preset", () =>
                {
                    var q = inst.model.parameters;
                    q.blendEnvelope = true; window.BuildPane(); Layout(); Need(Find("blend-adsr").Q<Label>().text == "ADSR", "Blend envelope title");
                    inst.model.synthMode = SynthMode.FM; EnsureFmOperators(q); window.BuildPane(); Layout();
                    for (int op = 0; op < 4; op++) { var a = Find("fm-adsr-" + op); Legible(a, a.name); Need(a.Q<Label>().text == "ADSR" && Readout(Slider(a, "attack")).EndsWith("ms"), "FM operator " + op); }
                    inst.model.synthMode = SynthMode.Subtractive;
                    inst.model.family = InstrumentFamily.Sampler;
                    inst.model.sampler.samples.Add(new SampleData { id = "sample", pcm = clip, regionEndFrame = clip.samples, loopEndFrame = clip.samples });
                    inst.model.sampler.zones.Add(new Keyzone { id = "zone", blend = new SampleBlendExtension { pcmB = clip, envelopeEnabled = true } });
                    var set = new ModulationSet { id = "mod", name = "Mod" }; set.devices.Add(new ModulationDevice { id = "adsr", kind = ModulationDeviceKind.AHDSR }); inst.model.modulation.Add(set);
                    window.modSetId = "mod"; window.modDeviceId = "adsr";
                    var found = new HashSet<string>(); ZuiAdsr mod = null;
                    for (int tab = 0; tab < 6; tab++)
                    {
                        window.instrumentTab = tab; window.BuildPane(); ZuiAudit.ExpandAll(window); Layout();
                        foreach (var a in window.rootVisualElement.Query<ZuiAdsr>().ToList())
                        {
                            found.Add(a.name); Legible(a, a.name); if (a.name == "mod-adsr") { mod = a; break; }
                            Need(a.Q<Label>().text == "ADSR", a.name + " is titled " + a.Q<Label>().text);
                        }
                        if (mod != null) break;
                    }
                    Need(found.Contains("paired-adsr"), "Second-sample envelope not found: " + string.Join(",", found));
                    inst.model.family = InstrumentFamily.Synth; inst.model.modulation.Clear(); window.modSetId = window.modDeviceId = "";
                    inst.model.parameters.presets.Add(new ZTrackerInstrument.InstrumentPreset { name = "Check preset", ovrAdsr = true, attack = .02f, decay = .3f, sustain = .5f, release = .4f });
                    window.presetIndex = 0; ZuiAdsr preset = null;
                    for (int tab = 0; tab < 6 && preset == null; tab++) { window.instrumentTab = tab; window.BuildPane(); ZuiAudit.ExpandAll(window); Layout(); preset = window.rootVisualElement.Q<ZuiAdsr>("preset-adsr"); }
                    if (preset != null) Legible(preset, "preset-adsr");
                    Need(preset != null && Readout(Slider(preset, "attack")) == "20 ms" && !preset.Query<Label>().ToList().Any(l => l.text == "ADSR" || l.text == "Amplitude"), "Preset envelope missing or titled twice");
                    var labels = window.rootVisualElement.Query<Label>().ToList(); Need(labels.Any(l => l.text == "ADSR") && !labels.Any(l => l.text == "Amplitude"), "Preset section is not titled ADSR");
                    window.presetIndex = -1;
                    Need(mod != null && mod.Q<ZuiMicroSlider>("adsr-input-hold") != null, "AHDSR modulator has no Hold slider");
                    Need(mod.worldBound.width <= 5 * new ZuiAdsr.Options().laneWidth + 1, "AHDSR wider than five lanes: " + mod.worldBound.width);
                    Need(!mod.Query<Label>().ToList().Any(l => l.text == "Amplitude" || l.text == "ADSR"), "AHDSR modulator repeats a title under its box");
                });
                Check("narrow window: the envelope fits its column, sliders stay one row inside it", () =>
                {
                    Fresh(560);
                    var adsr = Find("amplitude-adsr"); var row = adsr.Q("adsr-sliders"); Legible(adsr, "560 px");
                    Need(adsr.worldBound.width > 150, "Collapsed to " + adsr.worldBound.width);
                    Need(row.worldBound.height < 26, "Slider row wrapped at narrow width: " + row.worldBound.height);
                    foreach (var s in adsr.Query<ZuiMicroSlider>().ToList()) Need(s.worldBound.xMin >= adsr.worldBound.xMin - .5f && s.worldBound.xMax <= adsr.worldBound.xMax + .5f, s.name + " overflows");
                    report.AppendLine("  narrow size " + adsr.worldBound.width + "x" + adsr.worldBound.height);
                });
            }
            finally
            {
                window.StopPreview(); window.Close(); Undo.ClearUndo(inst); Undo.ClearUndo(songAsset);
                DestroyImmediate(inst); DestroyImmediate(songAsset); DestroyImmediate(clip);
            }
            report.AppendLine("TOTAL passed=" + passed + " failed=" + failed);
            return report.ToString();
        }
    }
}
