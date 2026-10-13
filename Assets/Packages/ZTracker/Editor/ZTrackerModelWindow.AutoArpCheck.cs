using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    // T-0642: AutoArp measured on rendered audio against the owner's examples. A sine synth plays high notes (C6, E6, G6,
    // B6: a period of about a millisecond) so the sounding note can be read from every single period. Live keys go in
    // through the engine's live-input door (synthetic events, no device); pattern notes through a song. Disposable
    // in-memory instruments only; reflection entry point.
    public sealed partial class ZTrackerModelWindow
    {
        static readonly int[] ArpNotes = { 84, 88, 91, 95 };

        static ZTrackerInstrument ArpInstrument(float stepSeconds, bool perCycle, float windowSeconds, AutoArpRetrigger retrigger = AutoArpRetrigger.FirstNote)
        {
            var a = SlideInstrument(SlideSource.Synth); var q = a.model.parameters;
            q.legatoEnabled = true; q.legatoPoly = true; q.legatoSlides = true; q.glideSeconds = 0;
            q.autoArp = true; q.autoArpSeconds = stepSeconds; q.autoArpPerCycle = perCycle; q.autoArpWindowSeconds = windowSeconds; q.autoArpRetrigger = retrigger;
            q.release = .5f;
            return a;
        }

        /// Renders live key events (time in seconds, note, true = down) on the clean preview song (12 note columns).
        static float[] ArpLiveRender(ZTrackerInstrument asset, double seconds, params (double t, int note, bool down)[] keys)
        {
            int frames = (int)(seconds * SlideRate); var output = new float[frames];
            var snap = PlaybackSnapshot.CreateClean(asset, 12);
            var l = new NativeArray<float>(64, Allocator.Persistent); var r = new NativeArray<float>(64, Allocator.Persistent);
            try
            {
                using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(snap.Data, SlideRate)))
                {
                    var pending = keys.OrderBy(k => k.t).ToList(); int next = 0;
                    for (int at = 0; at < frames; at += 64)
                    {
                        while (next < pending.Count && pending[next].t * SlideRate <= at)
                        {
                            var k = pending[next++];
                            e.LiveInput.Write(k.down ? TrackerLiveEvent.NoteOn(0, 0, 0, k.note, 110, 110 / 127f, TrackerLiveSource.Computer) : TrackerLiveEvent.NoteOff(k.note, TrackerLiveSource.Computer));
                        }
                        int n = Math.Min(64, frames - at); e.Render(l, r, n, n);
                        for (int i = 0; i < n; i++) output[at + i] = l[i];
                    }
                }
            }
            finally { l.Dispose(); r.Dispose(); snap.Dispose(); }
            return output;
        }

        /// The sounding note per period (nearest of <paramref name="notes"/>; -1 for a period between notes), joined into
        /// runs: (start s, end s, note). Runs shorter than two periods (a crossfade) are dropped.
        static List<(double start, double end, int note)> ArpRuns(float[] x, int[] notes)
        {
            var periods = SlidePitch(x); var runs = new List<(double, double, int)>();
            int Nearest(double n) { int best = -1; double d = .3; foreach (int c in notes) if (Math.Abs(n - c) < d) { d = Math.Abs(n - c); best = c; } return best; }
            int current = -2, count = 0; double start = 0, last = 0;
            foreach (var (t, n) in periods)
            {
                int k = Nearest(n);
                if (k != current) { if (current >= 0 && count >= 2) runs.Add((start, last, current)); current = k; count = 0; start = t; }
                count++; last = t;
            }
            if (current >= 0 && count >= 2) runs.Add((start, last, current));
            // Merge runs of the same note split by a dropped short run.
            var merged = new List<(double start, double end, int note)>();
            foreach (var run in runs) { if (merged.Count > 0 && merged[merged.Count - 1].note == run.Item3 && run.Item1 - merged[merged.Count - 1].end < .004) merged[merged.Count - 1] = (merged[merged.Count - 1].start, run.Item2, run.Item3); else merged.Add(run); }
            return merged;
        }

        static string ArpSequence(List<(double start, double end, int note)> runs, double from, double to)
            => string.Join(" ", runs.Where(r => r.start >= from && r.start < to).Select(r => r.note + "@" + (r.start * 1000).ToString("0")));

        public static string CheckAutoArp()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Check(string title, Action action) { try { action(); passed++; report.AppendLine("PASS " + title); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); } }
            var made = new List<UnityEngine.Object>();
            ZTrackerInstrument Make(float step, bool perCycle, float window, AutoArpRetrigger retrigger = AutoArpRetrigger.FirstNote) { var a = ArpInstrument(step, perCycle, window, retrigger); made.Add(a); return a; }
            // Step starts within [from, to): the interval between consecutive run starts (the step length).
            List<double> Steps(List<(double start, double end, int note)> runs, double from, double to) { var s = runs.Where(r => r.start >= from && r.start < to).Select(r => r.start).ToList(); return s.Zip(s.Skip(1), (a, b) => b - a).ToList(); }
            bool Cycles(List<(double start, double end, int note)> runs, double from, double to, int[] order, int least = -1)
            {
                var seq = runs.Where(r => r.start >= from && r.start < to).Select(r => r.note).ToList();
                if (seq.Count < (least < 0 ? order.Length * 2 : least)) return false;
                int at = Array.IndexOf(order, seq[0]); if (at < 0) return false;
                for (int i = 0; i < seq.Count; i++) if (seq[i] != order[(at + i) % order.Length]) return false;
                return true;
            }
            try
            {
                Check("owner's example, per note: 10 ms with 3 notes steps every 10 ms, upwards, a cycle every 30 ms", () =>
                {
                    var x = ArpLiveRender(Make(.01f, false, .05f), .6, (.1, 84, true), (.1, 88, true), (.1, 91, true));
                    var runs = ArpRuns(x, ArpNotes); var steps = Steps(runs, .12, .55);
                    report.AppendLine("MEASURE per note 10 ms: " + ArpSequence(runs, .1, .2) + " ...; " + steps.Count + " steps, mean " + (steps.Average() * 1000).ToString("0.00") + " ms, range " + (steps.Min() * 1000).ToString("0.0") + ".." + (steps.Max() * 1000).ToString("0.0") + " ms");
                    Need(Cycles(runs, .1, .55, new[] { 84, 88, 91 }), "Not cycling 84 88 91 upwards: " + ArpSequence(runs, .1, .3));
                    Need(Math.Abs(steps.Average() - .01) < .0002 && steps.All(s => Math.Abs(s - .01) < .0015), "Steps " + steps.Min() + ".." + steps.Max());
                });
                Check("owner's example, per cycle: 100 ms with 4 notes plays each note 25 ms", () =>
                {
                    var x = ArpLiveRender(Make(.1f, true, .05f), .8, (.1, 84, true), (.1, 88, true), (.1, 91, true), (.1, 95, true));
                    var runs = ArpRuns(x, ArpNotes); var steps = Steps(runs, .12, .75);
                    report.AppendLine("MEASURE per cycle 100 ms, 4 notes: " + ArpSequence(runs, .1, .3) + " ...; mean step " + (steps.Average() * 1000).ToString("0.00") + " ms");
                    Need(Cycles(runs, .1, .75, ArpNotes), "Not cycling the four notes: " + ArpSequence(runs, .1, .3));
                    Need(Math.Abs(steps.Average() - .025) < .0003 && steps.All(s => Math.Abs(s - .025) < .0015), "Steps " + steps.Min() + ".." + steps.Max());
                });
                Check("a new key joins the arpeggio in pitch order; per cycle shortens each step as the arpeggio grows", () =>
                {
                    var x = ArpLiveRender(Make(.06f, true, .05f), .8, (.1, 84, true), (.1, 91, true), (.4, 88, true));
                    var runs = ArpRuns(x, ArpNotes); var before = Steps(runs, .15, .38); var after = Steps(runs, .45, .75);
                    report.AppendLine("MEASURE two notes then three: " + ArpSequence(runs, .3, .55) + "; steps " + (before.Average() * 1000).ToString("0.0") + " ms then " + (after.Average() * 1000).ToString("0.0") + " ms");
                    Need(Cycles(runs, .1, .38, new[] { 84, 91 }) && Cycles(runs, .45, .75, new[] { 84, 88, 91 }), "Order " + ArpSequence(runs, .1, .75));
                    Need(Math.Abs(before.Average() - .03) < .001 && Math.Abs(after.Average() - .02) < .001, "Steps " + before.Average() + " / " + after.Average());
                });
                Check("window: two of four keys released 20 ms apart keep playing until the 100 ms window ends, then the arpeggio carries on with the two held", () =>
                {
                    var x = ArpLiveRender(Make(.02f, false, .1f), 1.0, (.1, 84, true), (.1, 88, true), (.1, 91, true), (.1, 95, true), (.5, 84, false), (.52, 88, false));
                    var runs = ArpRuns(x, ArpNotes);
                    report.AppendLine("MEASURE inside the window " + ArpSequence(runs, .5, .6) + " | after " + ArpSequence(runs, .6, .75));
                    Need(Cycles(runs, .48, .59, ArpNotes, 5), "Inside the window not all four: " + ArpSequence(runs, .48, .6));
                    Need(Cycles(runs, .62, .95, new[] { 91, 95 }), "After the window not the held two: " + ArpSequence(runs, .6, .95));
                });
                Check("all keys released inside the window (staggered 0/10/20 ms): the release starts with the last released key and cycles through all three as it fades", () =>
                {
                    var x = ArpLiveRender(Make(.02f, false, .1f), 1.3, (.1, 84, true), (.1, 88, true), (.1, 91, true), (.5, 84, false), (.51, 88, false), (.52, 91, false));
                    var runs = ArpRuns(x, ArpNotes); var cycles = SlideCycles(x);
                    double held = cycles.Where(c => c.t > .45 && c.t < .5).Average(c => c.peak), early = cycles.Where(c => c.t > .5 && c.t < .52).Average(c => c.peak), later = cycles.Where(c => c.t > .72 && c.t < .77).Average(c => c.peak);
                    report.AppendLine("MEASURE release: " + ArpSequence(runs, .52, .75) + "; level held " + held.ToString("0.000") + ", between the releases " + early.ToString("0.000") + ", 0.2 s into the release " + later.ToString("0.000"));
                    Need(Cycles(runs, .53, .9, new[] { 84, 88, 91 }), "Release does not cycle the last held combination: " + ArpSequence(runs, .52, .9));
                    Need(Math.Abs(early - held) < .01 && later < held * .75 && later > held * .4, "Level " + held + " / " + early + " / " + later);
                });
                Check("a key released after the window has ended leaves for good: the later release cycles only the notes still held", () =>
                {
                    var x = ArpLiveRender(Make(.02f, false, .05f), 1.3, (.1, 84, true), (.1, 88, true), (.1, 91, true), (.5, 84, false), (.7, 88, false), (.7, 91, false));
                    var runs = ArpRuns(x, ArpNotes);
                    report.AppendLine("MEASURE C released at 0.5 s (window 50 ms): " + ArpSequence(runs, .5, .6) + " | after " + ArpSequence(runs, .6, .7) + " | release " + ArpSequence(runs, .7, .9));
                    Need(Cycles(runs, .57, .69, new[] { 88, 91 }) && Cycles(runs, .71, 1.0, new[] { 88, 91 }), "C still in " + ArpSequence(runs, .55, 1.0));
                });
                // A plucked shape: 20 ms attack, 60 ms decay to 10%, so a restart shows as a peak at the start of a step.
                double StepPeak(ZTrackerInstrument a, double from, double to)
                {
                    var q = a.model.parameters; q.attack = .02f; q.decay = .06f; q.sustain = .1f;
                    var x = ArpLiveRender(a, 1.2, (.1, 84, true), (.1, 91, true), (.6, 88, true));
                    return SlideCycles(x).Where(c => c.t > from && c.t < to).Max(c => c.peak);
                }
                Check("retrigger: First note keeps the envelopes running; Single restarts them on every step; All restarts them on every key press", () =>
                {
                    double firstLate = StepPeak(Make(.08f, false, .05f), .9, 1.1), firstJoin = StepPeak(Make(.08f, false, .05f), .6, .75);
                    double singleLate = StepPeak(Make(.08f, false, .05f, AutoArpRetrigger.Single), .9, 1.1);
                    double allJoin = StepPeak(Make(.08f, false, .05f, AutoArpRetrigger.All), .6, .75), allLate = StepPeak(Make(.08f, false, .05f, AutoArpRetrigger.All), .9, 1.1);
                    report.AppendLine("MEASURE peak level: First note just after the 2nd key " + firstJoin.ToString("0.000") + ", later " + firstLate.ToString("0.000") + "; Single later " + singleLate.ToString("0.000") + "; All just after the 2nd key " + allJoin.ToString("0.000") + ", later " + allLate.ToString("0.000"));
                    Need(firstJoin < .1 && firstLate < .1, "First note restarted: " + firstJoin + " / " + firstLate);
                    Need(singleLate > .3, "Single did not restart each step: " + singleLate);
                    Need(allJoin > .3 && allLate < .1, "All: " + allJoin + " / " + allLate);
                });
                Check("pattern notes: a three-note chord in three columns arpeggiates, and its OFF releases the arpeggio", () =>
                {
                    var a = Make(.03f, false, .05f);
                    var song = TrackerEngineCheck.FixtureSong(a, 3, 16);
                    song.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = { TrackerEngineCheck.Note(84, 0), TrackerEngineCheck.Note(88, 1), TrackerEngineCheck.Note(91, 2) } });
                    song.patterns[0].tracks[0].WriteLine(new PatternLine { line = 8, notes = { new NoteCell { column = 0, note = NoteKind.Off }, new NoteCell { column = 1, note = NoteKind.Off }, new NoteCell { column = 2, note = NoteKind.Off } } });
                    var x = SlideRender(song, SlideRate * 2); var runs = ArpRuns(x, ArpNotes);
                    report.AppendLine("MEASURE pattern chord: " + ArpSequence(runs, 0, .15) + " ... release from 1 s: " + ArpSequence(runs, 1, 1.15));
                    Need(Cycles(runs, .02, .95, new[] { 84, 88, 91 }) && Cycles(runs, 1.01, 1.3, new[] { 84, 88, 91 }), "Pattern arpeggio " + ArpSequence(runs, 0, 1.3));
                });
                Check("a step change does not click (fade between notes); rendering allocates nothing; AutoArp off plays a chord as before", () =>
                {
                    var a = Make(.01f, false, .05f);
                    var x = ArpLiveRender(a, .5, (.1, 84, true), (.1, 88, true), (.1, 91, true));
                    double jump = 0; for (int i = (int)(.12 * SlideRate); i < (int)(.45 * SlideRate); i++) jump = Math.Max(jump, Math.Abs(x[i] - x[i - 1]));
                    // A sine at B6 (1976 Hz) moves at most 2 pi f / rate of its peak per sample.
                    double peak = x.Skip((int)(.12 * SlideRate)).Take(SlideRate / 4).Max(v => Math.Abs(v)), limit = peak * 2 * Math.PI * 1976 / SlideRate;
                    report.AppendLine("MEASURE largest sample step " + jump.ToString("0.0000") + " (a pure B6 at this level may move " + limit.ToString("0.0000") + ")");
                    Need(jump <= limit * 1.05, "Click at a step change: " + jump + " > " + limit);
                    long allocated = 0; var snap = PlaybackSnapshot.CreateClean(a, 12);
                    var l = new NativeArray<float>(64, Allocator.Persistent); var r = new NativeArray<float>(64, Allocator.Persistent);
                    try
                    {
                        using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(snap.Data, SlideRate)))
                        {
                            foreach (int k in new[] { 84, 88, 91 }) e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, k, 110, 110 / 127f, TrackerLiveSource.Computer));
                            e.Render(l, r, 64, 64);
                            for (int i = 0; i < 400; i++) { if (i == 200) e.LiveInput.Write(TrackerLiveEvent.NoteOff(84, TrackerLiveSource.Computer)); long before = GC.GetAllocatedBytesForCurrentThread(); e.Render(l, r, 64, 64); allocated += GC.GetAllocatedBytesForCurrentThread() - before; }
                        }
                    }
                    finally { l.Dispose(); r.Dispose(); snap.Dispose(); }
                    report.AppendLine("MEASURE allocated while arpeggiating 400 blocks: " + allocated + " bytes");
                    Need(allocated == 0, "Allocated " + allocated);
                    // Off: the same chord sample for sample as an instrument that never had AutoArp settings.
                    a.model.parameters.autoArp = false;
                    var plain = SlideInstrument(SlideSource.Synth); made.Add(plain); var pq = plain.model.parameters; pq.legatoEnabled = pq.legatoPoly = pq.legatoSlides = true; pq.glideSeconds = 0; pq.release = .5f;
                    var off = ArpLiveRender(a, .4, (.1, 84, true), (.1, 88, true), (.1, 91, true)); var never = ArpLiveRender(plain, .4, (.1, 84, true), (.1, 88, true), (.1, 91, true));
                    Need(off.SequenceEqual(never), "AutoArp off does not play the chord as before");
                });
            }
            finally { foreach (var o in made) DestroyImmediate(o); }
            return "CheckAutoArp passed=" + passed + " failed=" + failed + "\n" + report;
        }

        /// <summary>T-0642 on the instrument page, in a disposable window: AutoArp is offered only with Legato Poly; switching
        /// it on shows Arp ms, Per, Retrigger and Window ms; they store, double-click resets the sliders, Undo/Redo; and
        /// the row fits without overlaps. Nothing is saved.</summary>
        public static string CheckAutoArpUi()
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
            void Check(string name, Action action)
            {
                try
                {
                    window.StopPreview(); Undo.ClearUndo(inst); inst.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSong(inst);
                    window.pane = 3; window.instrumentTab = 0; Rebuild(); action(); passed++; report.AppendLine("PASS " + name);
                }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + name + ": " + (ex.InnerException ?? ex).Message); }
            }
            VisualElement Q(string name) => window.rootVisualElement.Q(name);
            void Commit(Laubrary.Zui.ZuiMicroSlider slider, float v) => typeof(Laubrary.Zui.ZuiMicroSlider).GetMethod("SetValue", flags).Invoke(slider, new object[] { v, true });
            void Send(VisualElement target, EventBase e) { using (e) { e.target = target; target.SendEvent(e); } }
            void DoubleClick(VisualElement target) { var at = target.worldBound.center; Send(target, PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = 2, mousePosition = at })); Send(target, PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, clickCount = 2, mousePosition = at })); }
            var q = (Func<InstrumentParameters>)(() => inst.model.parameters);
            try
            {
                Check("AutoArp is offered only with Legato Poly; switched on it shows Arp ms, Per, Retrigger and Window ms in one row without overlaps", () =>
                {
                    Need(Q("synth-autoarp") == null, "Offered without Legato");
                    q().legatoEnabled = true; q().legatoPoly = false; Rebuild(); Need(Q("synth-autoarp") == null, "Offered with Legato Mono");
                    q().legatoPoly = true; Rebuild(); Need(Q("synth-autoarp") != null && Q("synth-autoarp-ms") == null, "Not offered with Poly, or its settings show while it is off");
                    q().autoArp = true; Rebuild();
                    var parts = new[] { "synth-autoarp", "synth-autoarp-ms", "synth-autoarp-per", "synth-autoarp-retrigger", "synth-autoarp-window" }.Select(Q).ToList();
                    Need(parts.All(p => p != null), "Missing: " + string.Join(",", parts.Select((p, i) => p == null ? i.ToString() : "")));
                    for (int i = 0; i < parts.Count; i++) for (int j = i + 1; j < parts.Count; j++) Need(!parts[i].worldBound.Overlaps(parts[j].worldBound), parts[i].name + " overlaps " + parts[j].name);
                    report.AppendLine("MEASURE AutoArp row: " + string.Join(", ", parts.Select(p => p.name.Replace("synth-autoarp", "autoarp") + " " + p.worldBound.width.ToString("0") + " px")) + "; lines " + parts.Select(p => Mathf.Round(p.worldBound.y)).Distinct().Count());
                });
                Check("settings store; double-click resets Arp ms and Window ms; Undo and Redo", () =>
                {
                    q().legatoEnabled = q().legatoPoly = q().autoArp = true; Rebuild();
                    Commit(Q("synth-autoarp-ms") as Laubrary.Zui.ZuiMicroSlider, 30f); Commit(Q("synth-autoarp-window") as Laubrary.Zui.ZuiMicroSlider, 120f);
                    Need(Math.Abs(q().autoArpSeconds - .03f) < 1e-5f && Math.Abs(q().autoArpWindowSeconds - .12f) < 1e-5f, "Stored " + q().autoArpSeconds + " / " + q().autoArpWindowSeconds);
                    foreach (var (name, read, expected) in new (string, Func<float>, float)[] { ("synth-autoarp-ms", () => q().autoArpSeconds, new InstrumentParameters().autoArpSeconds), ("synth-autoarp-window", () => q().autoArpWindowSeconds, new InstrumentParameters().autoArpWindowSeconds) })
                    {
                        Rebuild(); float before = read(); DoubleClick(Q(name)); float reset = read();
                        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); float undone = read(); Undo.PerformRedo(); float redone = read();
                        report.AppendLine("MEASURE " + name + ": " + before + " -> double-click " + reset + ", Undo " + undone + ", Redo " + redone);
                        Need(Math.Abs(reset - expected) < 1e-5f && Math.Abs(undone - before) < 1e-5f && Math.Abs(redone - expected) < 1e-5f, name + " reset/Undo/Redo " + reset + " " + undone + " " + redone);
                    }
                    var per = Q("synth-autoarp-per").Q<Laubrary.Zui.ZuiSegmented>(); var retrigger = Q("synth-autoarp-retrigger").Q<Laubrary.Zui.ZuiSegmented>();
                    Need(per != null && retrigger != null, "Per / Retrigger are not segmented choices");
                    report.AppendLine("MEASURE tooltips: Arp ms '" + Q("synth-autoarp-ms").tooltip.Substring(0, 40) + "...', Window '" + Q("synth-autoarp-window").tooltip.Substring(0, 40) + "...'");
                });
            }
            finally
            {
                window.StopPreview(); window.Close();
                Undo.ClearUndo(inst); Undo.ClearUndo(songAsset); DestroyImmediate(inst); DestroyImmediate(songAsset);
            }
            return "CheckAutoArpUi passed=" + passed + " failed=" + failed + "\n" + report;
        }
    }
}
