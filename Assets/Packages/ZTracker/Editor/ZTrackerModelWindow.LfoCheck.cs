using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    // T-0641: an LFO merged into a parameter's envelope or ADSR, measured on rendered audio. Disposable in-memory
    // instruments only; reflection entry point.
    public sealed partial class ZTrackerModelWindow
    {
        /// A flat envelope at <paramref name="value"/> for <paramref name="seconds"/>, held there (sustain at its end).
        public static ZUIEnvelopeData FlatEnvelope(float value, float seconds = 1f, float yMax = 1f)
        {
            var e = new ZUIEnvelopeData(0, seconds, 0, yMax, false) { requiresEndPoint = false, enabled = true };
            e.points.Add(new ZUIEnvelopePoint(0, value, 1)); e.points.Add(new ZUIEnvelopePoint(seconds, value, 1));
            e.sustainEnabled = true; e.sustainPosition = seconds;
            return e;
        }

        static ZUIEnvelopeData WithLfo(ZUIEnvelopeData e, float depth, float speed, float delay = 0, int shape = 0)
        {
            e.lfo = true; e.lfoDepth = depth; e.lfoSpeed = speed; e.lfoDelay = delay; e.lfoShape = shape; return e;
        }

        public static string CheckEnvelopeLfo()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Check(string title, Action action) { try { action(); passed++; report.AppendLine("PASS " + title); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); } }
            var made = new List<UnityEngine.Object>();
            ZTrackerInstrument Synth(Action<InstrumentParameters> edit)
            {
                var a = SlideInstrument(SlideSource.Synth); made.Add(a); var p = a.model.parameters; p.release = 10f; p.volume = .5f; edit(p); return a;
            }
            // The note's level per cycle (peak / the left pan gain), and the times and values of its turning points.
            double pan = Math.Cos(Math.PI / 4);
            List<(double t, double level)> Levels(float[] x) => SlideCycles(x).Select(c => (c.t, c.peak / pan)).ToList();
            double LevelAt(List<(double t, double level)> l, double t) => l.OrderBy(c => Math.Abs(c.t - t)).First().level;
            (double lo, double hi) Span(List<(double t, double level)> l, double from, double to) { var s = l.Where(c => c.t > from && c.t < to).Select(c => c.level).ToList(); return (s.Min(), s.Max()); }
            // Times where the level passes its centre going up (the wave's start of cycle).
            List<double> Ups(List<(double t, double level)> l, double centre, double from, double to)
            {
                var ups = new List<double>(); for (int i = 1; i < l.Count; i++) if (l[i].t > from && l[i].t < to && l[i - 1].level < centre && l[i].level >= centre) ups.Add(l[i].t);
                return ups;
            }
            try
            {
                Check("the data: off on an envelope saved before it; the fields survive a save/load round trip; every shape starts at 0 and rises", () =>
                {
                    var old = JsonUtility.FromJson<ZUIEnvelopeData>("{\"m_enabled\":true,\"m_points\":[]}");
                    Need(!old.lfo && old.lfoDepth == 0 && old.LfoAt(.3f) == 0, "An older envelope has an LFO");
                    var e = WithLfo(FlatEnvelope(.5f), .2f, 3f, .4f, 2); var copy = JsonUtility.FromJson<ZUIEnvelopeData>(JsonUtility.ToJson(e));
                    Need(copy.lfo && copy.lfoDepth == .2f && copy.lfoSpeed == 3f && copy.lfoDelay == .4f && copy.lfoShape == 2, "LFO lost on a round trip");
                    for (int s = 0; s < 4; s++)
                    {
                        float w0 = ZUIEnvelopeData.LfoWave(s, 0), w1 = ZUIEnvelopeData.LfoWave(s, .1f), q = ZUIEnvelopeData.LfoWave(s, .25f), h = ZUIEnvelopeData.LfoWave(s, .75f);
                        // Saw: rises from 0 to the top at half a cycle, drops to the bottom and rises back to 0.
                        bool shaped = s == 2 ? q == .5f && ZUIEnvelopeData.LfoWave(s, .49f) > .97f && ZUIEnvelopeData.LfoWave(s, .5f) == -1f && h == -.5f : q > .9f && h < -.9f;
                        Need((s == 3 ? w0 == 1 : w0 == 0) && w1 > 0 && shaped, "Shape " + s + ": " + w0 + " " + w1 + " " + q + " " + h);
                    }
                });
                Check("Volume: a flat 50% envelope with a 25% sine LFO at 2 Hz swings 25%..75%, starting from 50% and rising, one cycle every 0.5 s", () =>
                {
                    var x = SlideRender(SlideSong(Synth(p => p.SetParameterEnvelope("volume", WithLfo(FlatEnvelope(.5f), .25f, 2f))), 32, (0, 69, 128)), SlideRate * 3);
                    var l = Levels(x); var (lo, hi) = Span(l, .2, 2.8); var ups = Ups(l, .5, .2, 2.8);
                    double period = ups.Count > 1 ? (ups.Last() - ups.First()) / (ups.Count - 1) : 0;
                    report.AppendLine("MEASURE level at 0 s " + LevelAt(l, .003).ToString("0.000") + ", at 0.125 s " + LevelAt(l, .125).ToString("0.000") + ", range " + lo.ToString("0.000") + ".." + hi.ToString("0.000") + ", period " + period.ToString("0.0000") + " s over " + ups.Count + " cycles");
                    Need(Math.Abs(LevelAt(l, .003) - .5) < .03 && LevelAt(l, .125) > .72, "Start/rise " + LevelAt(l, .003) + " / " + LevelAt(l, .125));
                    Need(Math.Abs(lo - .25) < .01 && Math.Abs(hi - .75) < .01, "Range " + lo + ".." + hi);
                    Need(Math.Abs(period - .5) < .003, "Period " + period);
                });
                Check("Delay: the LFO fades in over its delay (1 s): small at first, about half way at 0.5 s, full after 1 s", () =>
                {
                    var x = SlideRender(SlideSong(Synth(p => p.SetParameterEnvelope("volume", WithLfo(FlatEnvelope(.5f), .25f, 4f, 1f))), 32, (0, 69, 128)), SlideRate * 2);
                    var l = Levels(x); var a = Span(l, 0, .2); var b = Span(l, .4, .6); var c = Span(l, 1.1, 1.9);
                    report.AppendLine("MEASURE swing 0-0.2 s " + (a.hi - a.lo).ToString("0.000") + ", 0.4-0.6 s " + (b.hi - b.lo).ToString("0.000") + ", after 1 s " + (c.hi - c.lo).ToString("0.000"));
                    Need(a.hi - a.lo < .12 && Math.Abs((b.hi - b.lo) - .25) < .05 && Math.Abs((c.hi - c.lo) - .5) < .02, "Fade " + (a.hi - a.lo) + " / " + (b.hi - b.lo) + " / " + (c.hi - c.lo));
                });
                Check("Clamp: the merged value stays in the parameter's range (Volume 10% with a 50% LFO goes 0%..60%, never below 0)", () =>
                {
                    var x = SlideRender(SlideSong(Synth(p => p.SetParameterEnvelope("volume", WithLfo(FlatEnvelope(.1f), .5f, 2f))), 32, (0, 69, 128)), SlideRate * 2);
                    var l = Levels(x); var (lo, hi) = Span(l, .2, 1.8);
                    // A negative volume would flip the wave's sign and keep it sounding; clamped at 0 it is silent while
                    // 0.1 + 0.5 sin is below 0, which is 43.6% of each cycle.
                    int from = (int)(.25 * SlideRate), to = (int)(1.75 * SlideRate); double silent = 0; for (int i = from; i < to; i++) if (x[i] == 0) silent++; silent /= to - from;
                    report.AppendLine("MEASURE range " + lo.ToString("0.000") + ".." + hi.ToString("0.000") + ", silent share " + silent.ToString("0.000") + " (expected 0.436)");
                    Need(Math.Abs(hi - .6) < .01 && lo < .005 && Math.Abs(silent - .436) < .02, "Clamp " + lo + ".." + hi + " silent " + silent);
                });
                Check("Rows: an envelope in song rows takes its LFO speed per row (4 rows a cycle: 0.5 s at 120 BPM, 0.4 s at 150 BPM)", () =>
                {
                    foreach (int bpm in new[] { 120, 150 })
                    {
                        var env = WithLfo(FlatEnvelope(.5f, 8f), .25f, .25f); env.durationInRows = true;
                        var song = SlideSong(Synth(p => p.SetParameterEnvelope("volume", env)), 64, (0, 69, 128)); song.bpm = bpm;
                        var l = Levels(SlideRender(song, SlideRate * 3)); var ups = Ups(l, .5, .1, 2.9);
                        double period = (ups.Last() - ups.First()) / (ups.Count - 1), expected = 4 * 60.0 / (bpm * 4);
                        report.AppendLine("MEASURE " + bpm + " BPM: period " + period.ToString("0.0000") + " s (expected " + expected.ToString("0.000") + ")");
                        Need(Math.Abs(period - expected) < .003, bpm + " BPM period " + period);
                    }
                });
                Check("ADSR: the LFO rides on an ADSR (sustain 20%), keeps swinging while held and through the release", () =>
                {
                    var env = WithLfo(AdsrEnvelope(0, .5f, .05f, .1f, .4f, .5f), .1f, 4f);
                    var x = SlideRender(SlideSong(Synth(p => p.SetParameterEnvelope("volume", env)), 32, (0, 69, 128), (8, -1, 0)), SlideRate * 2);
                    // Divide out the amplifier's own 10 s release after note-off at 1 s.
                    var l = Levels(x).Select(c => (c.t, c.t > 1 ? c.level / (1 - (c.t - 1) / 10) : c.level)).ToList();
                    var held = Span(l, .3, .95); var rel = Span(l, 1.05, 1.2);
                    report.AppendLine("MEASURE held " + held.lo.ToString("0.000") + ".." + held.hi.ToString("0.000") + ", early release " + rel.lo.ToString("0.000") + ".." + rel.hi.ToString("0.000"));
                    Need(Math.Abs(held.lo - .1) < .01 && Math.Abs(held.hi - .3) < .01, "Held " + held.lo + ".." + held.hi);
                    Need(rel.hi - rel.lo > .1, "No swing in the release " + rel.lo + ".." + rel.hi);
                });
                Check("Pitch: Tune with a 50-cent sine LFO swings the pitch +/-50 cents; a square LFO jumps between +100 and -100 cents", () =>
                {
                    var x = SlideRender(SlideSong(Synth(p => p.SetParameterEnvelope("fineTune", WithLfo(FlatEnvelope(0, 1, 1200), 50f, 2f))), 32, (0, 69, 128)), SlideRate * 2);
                    var pitch = SlidePitch(x).Where(v => v.t > .3 && v.t < 1.8).ToList(); double lo = pitch.Min(v => v.note) - 69, hi = pitch.Max(v => v.note) - 69;
                    var sq = SlidePitch(SlideRender(SlideSong(Synth(p => p.SetParameterEnvelope("fineTune", WithLfo(FlatEnvelope(0, 1, 1200), 100f, 2f, 0, 3))), 32, (0, 69, 128)), SlideRate * 2)).Where(v => v.t > .3 && v.t < 1.8).ToList();
                    int up = sq.Count(v => Math.Abs(v.note - 70) < .02), down = sq.Count(v => Math.Abs(v.note - 68) < .02);
                    report.AppendLine("MEASURE sine " + (lo * 100).ToString("0.0") + ".." + (hi * 100).ToString("+0.0") + " cents; square: " + up + " periods at +100 cents, " + down + " at -100 of " + sq.Count);
                    Need(Math.Abs(lo + .5) < .03 && Math.Abs(hi - .5) < .03, "Sine pitch " + lo + ".." + hi);
                    Need(up > sq.Count * .4 && down > sq.Count * .4, "Square " + up + "/" + down + " of " + sq.Count);
                });
                Check("the synth's own envelopes: Pulse width 50% with a 30% triangle LFO swings the duty 20%..80%, within the 1%..99% range", () =>
                {
                    var a = Synth(p => { p.waveA = 1; p.pulseWidth = .5f; p.pulseWidthEnvelopeData = WithLfo(FlatEnvelope(.5f), .3f, 2f, 0, 1); });
                    var cycles = SlideCycles(SlideRender(SlideSong(a, 32, (0, 69, 128)), SlideRate * 2)).Where(c => c.t > .3 && c.t < 1.8).ToList();
                    double lo = cycles.Min(c => c.duty), hi = cycles.Max(c => c.duty);
                    report.AppendLine("MEASURE duty " + lo.ToString("0.000") + ".." + hi.ToString("0.000"));
                    Need(Math.Abs(lo - .2) < .03 && Math.Abs(hi - .8) < .03, "Duty " + lo + ".." + hi);
                });
                Check("Cutoff (stored as a share of Nyquist) compiles its depth to Hz with its points; rendering with LFOs allocates nothing", () =>
                {
                    var a = Synth(p => { p.waveA = 2; p.instFilterEnabled = true; p.SetParameterEnvelope("instFilterCutoff", WithLfo(FlatEnvelope(.1f), .05f, 3f)); p.SetParameterEnvelope("volume", WithLfo(FlatEnvelope(.5f), .2f, 5f, .2f)); });
                    using (var prepared = TrackerPreparedSong.Prepare(SlideSong(a, 32, (0, 69, 128)), SlideRate))
                    {
                        var mods = prepared.state.mods.ToArray().Where(m => m.parameterEnvelope).ToArray();
                        var cutoff = mods.First(m => m.parameter == TrackerParameter.FilterCutoff);
                        Need(Math.Abs(cutoff.lfoDepth - .05f * SlideRate * .5f) < .01f, "Cutoff LFO depth " + cutoff.lfoDepth);
                    }
                    long allocated = 0;
                    var l = new NativeArray<float>(256, Allocator.Persistent); var r = new NativeArray<float>(256, Allocator.Persistent);
                    try
                    {
                        using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(SlideSong(a, 32, (0, 69, 128)), SlideRate)))
                        {
                            e.SendCommand(TrackerCommand.Play()); e.Render(l, r, 256, 256);
                            for (int i = 0; i < 100; i++) { long before = GC.GetAllocatedBytesForCurrentThread(); e.Render(l, r, 256, 256); allocated += GC.GetAllocatedBytesForCurrentThread() - before; }
                        }
                    }
                    finally { l.Dispose(); r.Dispose(); }
                    report.AppendLine("MEASURE allocated while rendering 100 blocks with two LFOs: " + allocated + " bytes");
                    Need(allocated == 0, "Allocated " + allocated);
                });
                Check("a change to the LFO (switch, depth, speed, delay, shape), an ADSR stage or an envelope's Maximum applies to a playing song without stopping it", () =>
                {
                    var env = FlatEnvelope(.5f); var a = Synth(p => p.SetParameterEnvelope("volume", env));
                    var first = TrackerPreparedSong.Prepare(SlideSong(a, 32, (0, 69, 128)), SlideRate);
                    WithLfo(env, .3f, 7f, .5f, 2);
                    var second = TrackerPreparedSong.Prepare(SlideSong(a, 32, (0, 69, 128)), SlideRate);
                    try { Need(second.PreparePreserving(first, out var reason), "Refused: " + reason); }
                    finally { first.Dispose(); second.Dispose(); }
                    // An ADSR stage edit and an envelope's Maximum also apply live (the shape keeps its point count).
                    var adsr = AdsrEnvelope(0, .5f, .1f, .2f, .5f, .3f); var b = Synth(p => p.SetParameterEnvelope("volume", adsr));
                    first = TrackerPreparedSong.Prepare(SlideSong(b, 32, (0, 69, 128)), SlideRate);
                    adsr.adsrAttack = .3f; adsr.ShapeAdsr(.5f); adsr.customMaximum = true; adsr.yMax = 4;
                    second = TrackerPreparedSong.Prepare(SlideSong(b, 32, (0, 69, 128)), SlideRate);
                    try { Need(second.PreparePreserving(first, out var reason), "ADSR edit refused: " + reason); }
                    finally { first.Dispose(); second.Dispose(); }
                });
            }
            finally { foreach (var o in made) DestroyImmediate(o); }
            return "CheckEnvelopeLfo passed=" + passed + " failed=" + failed + "\n" + report;
        }

        /// <summary>T-0641 on the instrument page, in a disposable window: the LFO switch in a value's right-click menu
        /// (Envelope and ADSR only, the menu stays open), the Depth / Speed / Delay sliders and the shape (stored, double-click
        /// reset, Undo/Redo), the merged wave drawn over the curve, rows, and the layout of the row. Nothing is saved.</summary>
        public static string CheckEnvelopeLfoUi()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>();
            var songAsset = CreateInstance<ZTrackerSong>();
            var inst = CreateInstance<ZTrackerInstrument>();
            inst.schemaVersion = songAsset.schemaVersion = 1;
            inst.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSong(inst);
            window.song = songAsset; window.instrument = inst; window.pane = 3; window.instrumentTab = 0;
            window.Show(); window.position = new Rect(160, 120, 1120, 760); window.CreateGUI();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Layout() { var panel = window.rootVisualElement.panel; Need(panel != null, "Window not attached"); for (int i = 0; i < 3; i++) panel.GetType().GetMethod("ValidateLayout", flags)?.Invoke(panel, null); }
            void Rebuild() { window.BuildPane(); Laubrary.Zui.ZuiAudit.ExpandAll(window); Layout(); }
            void CloseMenus() { foreach (var s in window.rootVisualElement.panel.visualTree.Query(className: "zui-popover__scrim").ToList()) s.RemoveFromHierarchy(); }
            void Fresh()
            {
                window.StopPreview(); CloseMenus(); Undo.ClearUndo(inst); Undo.ClearUndo(songAsset);
                inst.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSong(inst);
                window.pane = 3; window.instrumentTab = 0; window.modSetId = window.modDeviceId = ""; Rebuild();
            }
            void Check(string name, Action action)
            {
                try { Fresh(); action(); passed++; report.AppendLine("PASS " + name); }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + name + ": " + (ex.InnerException ?? ex).Message); }
            }
            Laubrary.Zui.ZuiValueControl Value(string name) { var c = window.rootVisualElement.Q<Laubrary.Zui.ZuiValueControl>(name); Need(c != null, "Missing value control " + name); return c; }
            void Mode(Laubrary.Zui.ZuiValueControl c, ZUIValue.Mode mode) => typeof(Laubrary.Zui.ZuiValueControl).GetMethod("SetMode", flags).Invoke(c, new object[] { mode });
            ZUIEnvelopeData Env() => inst.model.parameters.GetParameterEnvelope("volume");
            void Send(VisualElement target, EventBase e) { using (e) { e.target = target; target.SendEvent(e); } }
            void RightClick(VisualElement target) { var at = target.worldBound.center; Send(target, PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 1, mousePosition = at })); Send(target, PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 1, mousePosition = at })); }
            void Click(VisualElement target, int clicks = 1) { var at = target.worldBound.center; Send(target, PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = clicks, mousePosition = at })); Send(target, PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, clickCount = clicks, mousePosition = at })); }
            // A button click as Unity's own click simulation makes it (the Clickable fires its action).
            void Press(Button target) => typeof(Clickable).GetMethod("SimulateSingleClick", flags).Invoke(target.clickable, new object[] { null, 0 });
            Laubrary.Zui.ZuiToggleButton MenuLfo() => window.rootVisualElement.panel.visualTree.Query<Laubrary.Zui.ZuiToggleButton>().ToList().FirstOrDefault(t => t.text == "LFO" && t.GetFirstAncestorOfType<Laubrary.Zui.ZuiValueControl>() == null);
            Laubrary.Zui.ZuiMicroSlider Slider(Laubrary.Zui.ZuiValueControl c, string part) { var s = c.Q<Laubrary.Zui.ZuiMicroSlider>("zui-value-lfo-" + part); Need(s != null, "No LFO " + part + " slider"); return s; }
            void Commit(Laubrary.Zui.ZuiMicroSlider slider, float v) => typeof(Laubrary.Zui.ZuiMicroSlider).GetMethod("SetValue", flags).Invoke(slider, new object[] { v, true });
            string Readout(Laubrary.Zui.ZuiMicroSlider s) => s.Q<Label>(className: "zui-microslider__value").text;
            void Legible(Laubrary.Zui.ZuiValueControl c)
            {
                var row = c.LfoRow; Need(row != null, "No LFO row");
                foreach (var s in row.Query<Laubrary.Zui.ZuiMicroSlider>().ToList())
                {
                    var caption = s.Q<Label>(className: "zui-microslider__caption"); var readout = s.Q<Label>(className: "zui-microslider__value");
                    float cw = caption.MeasureTextSize(caption.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x, vw = readout.MeasureTextSize(readout.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                    Need(readout.worldBound.xMax - vw - (caption.worldBound.xMin + cw) >= 2, s.name + " readout '" + readout.text + "' runs into '" + caption.text + "' at " + s.worldBound.width + " px");
                    Need(s.worldBound.xMax <= c.worldBound.xMax + .5f, s.name + " runs past its control");
                }
                var kids = row.Children().ToList();
                for (int i = 0; i < kids.Count; i++) for (int j = i + 1; j < kids.Count; j++) Need(!kids[i].worldBound.Overlaps(kids[j].worldBound), kids[i].name + " overlaps " + kids[j].name);
            }
            try
            {
                Check("the menu offers LFO on an Envelope, not on a Static value; switching it on keeps the menu open, stores it and shows Depth 20% / Speed 4 Hz / Delay 0 ms", () =>
                {
                    var volume = Value("synth-volume"); RightClick(volume); Need(MenuLfo() == null, "LFO offered on a Static value"); CloseMenus();
                    Mode(volume, ZUIValue.Mode.Curve); volume = Value("synth-volume"); RightClick(volume);
                    var toggle = MenuLfo(); Need(toggle != null, "No LFO switch in the Envelope menu");
                    Click(toggle); Layout();
                    Need(Env().lfo && Math.Abs(Env().lfoDepth - .2f) < 1e-5f && Env().lfoSpeed == 4f, "Not stored: lfo " + Env().lfo + " depth " + Env().lfoDepth);
                    Need(MenuLfo() != null && MenuLfo().panel != null, "The menu closed on a setting");
                    CloseMenus(); volume = Value("synth-volume");
                    Need(Readout(Slider(volume, "depth")) == "20" && Readout(Slider(volume, "speed")) == "4.00" && Readout(Slider(volume, "delay")) == "0", "Readouts " + Readout(Slider(volume, "depth")) + " / " + Readout(Slider(volume, "speed")) + " / " + Readout(Slider(volume, "delay")));
                    Need(volume.CurveEnvelope?.configuration.overlay != null, "The merged wave is not drawn on the curve");
                    Legible(volume); report.AppendLine("MEASURE envelope mode: LFO row " + volume.LfoRow.worldBound.width + "x" + volume.LfoRow.worldBound.height + " px in a " + volume.worldBound.width + " px control");
                });
                Check("Depth, Speed, Delay and the shape store in the parameter's units; double-click resets each slider; Undo and Redo", () =>
                {
                    Mode(Value("synth-volume"), ZUIValue.Mode.Curve); var env = Env(); env.lfo = true; env.lfoDepth = .2f; env.lfoSpeed = 4; Rebuild(); var volume = Value("synth-volume"); volume.IsExpanded = true;
                    Commit(Slider(volume, "depth"), 50f); Commit(Slider(volume, "speed"), 7f); Commit(Slider(volume, "delay"), 300f);
                    Need(Math.Abs(Env().lfoDepth - .5f) < 1e-5f && Math.Abs(Env().lfoSpeed - 7f) < .01f && Math.Abs(Env().lfoDelay - .3f) < 1e-4f, "Stored " + Env().lfoDepth + " " + Env().lfoSpeed + " " + Env().lfoDelay);
                    var shape = volume.Q<Button>("zui-value-lfo-shape"); for (int i = 0; i < 3; i++) Press(shape); Need(Env().lfoShape == 3 && shape.text.Contains("Square"), "Shape " + Env().lfoShape + " shown " + shape.text);
                    float overlay = volume.CurveEnvelope.configuration.overlay(.01f), curve = volume.Value.points[0].value;
                    report.AppendLine("MEASURE merged wave at 1% of the curve: " + overlay.ToString("0.00") + " (curve " + curve.ToString("0.00") + ", square LFO faded in over 300 ms)");
                    Need(overlay > curve + 1f, "Overlay does not add the LFO: " + overlay + " vs " + curve);
                    foreach (var (part, stored) in new (string, Func<float>)[] { ("depth", () => Env().lfoDepth), ("speed", () => Env().lfoSpeed), ("delay", () => Env().lfoDelay) })
                    {
                        Rebuild(); volume = Value("synth-volume"); volume.IsExpanded = true; float before = stored(); Click(Slider(volume, part), 2); float reset = stored();
                        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); float undone = stored(); Undo.PerformRedo(); float redone = stored();
                        report.AppendLine("MEASURE " + part + ": " + before + " -> double-click " + reset + ", Undo " + undone + ", Redo " + redone);
                        float expected = part == "depth" ? .2f : part == "speed" ? 4f : 0f;
                        Need(Math.Abs(reset - expected) < 1e-4f && Math.Abs(undone - before) < 1e-4f && Math.Abs(redone - expected) < 1e-4f, part + " reset/Undo/Redo " + reset + " " + undone + " " + redone);
                    }
                });
                Check("rows: in a rows envelope Speed reads as rows per cycle and Delay in rows; switching Time in keeps the LFO's timing", () =>
                {
                    Mode(Value("synth-volume"), ZUIValue.Mode.Curve); var env = Env(); env.lfo = true; env.lfoDepth = .2f; env.lfoSpeed = 4; env.lfoDelay = .5f; Rebuild(); Value("synth-volume").IsExpanded = true; Layout();
                    var unit = window.rootVisualElement.Q("synth-volume-unit"); Need(unit != null, "No Time in control");
                    Click(unit.Query<Button>().ToList()[1]); Rebuild(); var volume = Value("synth-volume"); volume.IsExpanded = true;
                    var speed = Slider(volume, "speed"); var delay = Slider(volume, "delay");
                    string label = speed.Q<Label>(className: "zui-microslider__caption").text, delayLabel = delay.Q<Label>(className: "zui-microslider__caption").text;
                    report.AppendLine("MEASURE rows: speed " + Env().lfoSpeed + " cycles/row shown '" + label + " " + Readout(speed) + "', delay " + Env().lfoDelay + " rows shown '" + delayLabel + " " + Readout(delay) + "'");
                    Need(Env().durationInRows && label == "Cycle rows" && delayLabel == "Delay rows", "Labels " + label + " / " + delayLabel);
                    // 120 BPM, 4 lines a beat: 8 rows a second; 4 Hz is half a cycle a row (2 rows a cycle); 0.5 s is 4 rows.
                    Need(Math.Abs(Env().lfoSpeed - .5f) < .01f && Math.Abs(Env().lfoDelay - 4f) < .01f && Readout(speed).StartsWith("2"), "Converted " + Env().lfoSpeed + " / " + Env().lfoDelay);
                    Commit(speed, 8f); Need(Math.Abs(Env().lfoSpeed - .125f) < 1e-4f, "8 rows a cycle stored " + Env().lfoSpeed);
                });
                Check("ADSR: the LFO row sits under the compact ADSR and fits the Amplifier column; Static hides it and keeps it", () =>
                {
                    var volume = Value("synth-volume"); Mode(volume, ZUIValue.Mode.Adsr); volume = Value("synth-volume"); RightClick(volume);
                    var toggle = MenuLfo(); Need(toggle != null, "No LFO switch in the ADSR menu"); Click(toggle); CloseMenus(); Rebuild(); volume = Value("synth-volume");
                    Need(volume.AdsrControl != null && volume.LfoRow != null && volume.LfoRow.worldBound.yMin >= volume.AdsrControl.worldBound.yMax - .5f, "LFO row not under the ADSR");
                    Legible(volume); var pan = Value("synth-pan"); Need(!volume.worldBound.Overlaps(pan.worldBound), "Runs into Pan");
                    report.AppendLine("MEASURE ADSR mode: LFO row " + volume.LfoRow.worldBound.width + "x" + volume.LfoRow.worldBound.height + " px");
                    Mode(volume, ZUIValue.Mode.Static); Rebuild(); volume = Value("synth-volume"); Need(volume.LfoRow == null && Env().lfo, "Static shows the LFO or lost it");
                });
            }
            finally
            {
                CloseMenus(); window.StopPreview(); window.Close();
                Undo.ClearUndo(inst); Undo.ClearUndo(songAsset); DestroyImmediate(inst); DestroyImmediate(songAsset);
            }
            return "CheckEnvelopeLfoUi passed=" + passed + " failed=" + failed + "\n" + report;
        }
    }
}
