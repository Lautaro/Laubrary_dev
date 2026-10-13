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
    // T-0645: the FM audit follow-ups, measured on rendered audio: operator settings read once per slice (the per-note
    // envelopes still move every sample), the Parallel routing's output feedback in radians, Free phase, and 2x
    // oversampling. Disposable in-memory instruments; reflection entry points. The UI group opens a disposable window.
    public sealed partial class ZTrackerModelWindow
    {
        /// A held FM instrument with flat envelopes (amplifier and every operator: attack 0, sustain 1), so a window of
        /// its sustained sound is exactly periodic. Operators at fixed Hz when <paramref name="hz"/> is above 0.
        static ZTrackerInstrument FmQualityInstrument(int algorithm, float hz, float modulatorLevel, bool singleRate, bool freePhase)
        {
            var a = CreateInstance<ZTrackerInstrument>(); a.schemaVersion = 1; a.model = NewInstrumentData();
            a.model.synthMode = SynthMode.FM; var q = a.model.parameters; q.type = InstrumentType.FM; q.fmAlgorithm = algorithm;
            q.attack = 0; q.decay = 0; q.sustain = 1; q.release = .05f; q.vibratoDepth = 0; q.fmSingleRate = singleRate; q.fmFreePhase = freePhase;
            for (int k = 0; k < 4; k++) { var o = q.fmOperators[k]; o.attack = 0; o.decay = 0; o.sustain = 1; o.freqRatio = 1; o.freqFixed = hz; if (k != 3 || algorithm == 5) o.level = modulatorLevel; q.fmOperators[k] = o; }
            return a;
        }
        static float[] FmQualityRender(ZTrackerInstrument a, int frames, params int[] notes)
            => LevelLive(a, frames, true, (live, at) => { if (at == 0) foreach (int n in notes) live.Write(TrackerLiveEvent.NoteOn(0, 0, 0, n, 100, 100 / 127f, TrackerLiveSource.Computer)); }).x;
        /// Energy of one exact DFT bin (Goertzel) of x[start..start+length).
        static double FmBin(float[] x, int start, int length, int bin)
        {
            double w = 2 * Math.PI * bin / length, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
            for (int i = 0; i < length; i++) { double s0 = x[start + i] + c * s1 - s2; s2 = s1; s1 = s0; }
            return (s1 * s1 + s2 * s2 - c * s1 * s2) * 2 / ((double)length * length);
        }
        /// Aliasing of a sustained tone with exactly <paramref name="cycles"/> periods in the window: harmonics land on
        /// multiples of that bin; anything folded back from above the output's highest frequency lands between them.
        /// Returns the energy between the harmonics relative to the harmonics' energy, in dB.
        static double FmAliasDb(float[] x, int start, int length, int cycles)
        {
            double total = 0, mean = 0; for (int i = 0; i < length; i++) mean += x[start + i]; mean /= length;
            for (int i = 0; i < length; i++) { double v = x[start + i] - mean; total += v * v; } total /= length;
            double harmonics = 0; for (int k = cycles; k < length / 2; k += cycles) harmonics += FmBin(x, start, length, k);
            return 10 * Math.Log10(Math.Max(1e-30, total - harmonics) / harmonics);
        }

        public static string CheckFmQuality()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            var made = new List<UnityEngine.Object>();
            ZTrackerInstrument Keep(ZTrackerInstrument o) { made.Add(o); return o; }
            void Check(string title, Action action) { try { action(); passed++; report.AppendLine("PASS " + title); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); } }
            const int Window = 4800;
            try
            {
                Check("new instruments oversample by default and keep phase 0; the settings round-trip", () =>
                {
                    var p = NewInstrumentData().parameters; Need(!p.fmSingleRate && !p.fmFreePhase, "Defaults " + p.fmSingleRate + " " + p.fmFreePhase);
                    p.fmSingleRate = true; p.fmFreePhase = true; var back = JsonUtility.FromJson<InstrumentParameters>(JsonUtility.ToJson(p));
                    Need(back.fmSingleRate && back.fmFreePhase, "Round trip lost the FM settings");
                });
                Check("settings read once per slice: a per-note Level envelope on the carrier still moves every sample", () =>
                {
                    var a = Keep(FmQualityInstrument(104, 0, .5f, true, false));
                    var e1 = new ZUIEnvelopeData(0, .5f, 0, 2, false); e1.points.Add(new ZUIEnvelopePoint(0, 0f)); e1.points.Add(new ZUIEnvelopePoint(.5f, 1f)); e1.requiresEndPoint = false;
                    a.model.parameters.parameterEnvelopes = new List<ParameterEnvelope> { new ParameterEnvelope { parameter = "fmOperators.3.level", envelope = e1 } };
                    var x = FmQualityRender(a, 24064, 69);
                    double Rms(int from, int n) { double s = 0; for (int i = from; i < from + n; i++) s += x[i] * x[i]; return Math.Sqrt(s / n); }
                    double early = Rms(2400, 480), late = Rms(21600, 480), ratio = late / early;
                    report.AppendLine("MEASURE carrier Level envelope 0 -> 100% over 0.5 s: level at 0.05 s " + early.ToString("0.0000") + ", at 0.45 s " + late.ToString("0.0000") + " (ratio " + ratio.ToString("0.00") + ", theory 9)");
                    Need(ratio > 7 && ratio < 11, "The envelope did not ramp the level: " + ratio);
                });
                Check("Parallel adds output feedback in radians, like the other older routings", () =>
                {
                    // Operator A alone (B, C, D at Level 0), output feedback 2: the sound is s[n] = sin(2 pi phase + 2 * s[n-1] / 4).
                    var a = Keep(FmQualityInstrument(5, 500, 0f, true, false)); var q = a.model.parameters; q.fmFeedback = 2;
                    var o = q.fmOperators[0]; o.level = 1; q.fmOperators[0] = o;
                    var x = FmQualityRender(a, 24064, 69);
                    double Ratio(float[] y, int start) => 10 * Math.Log10(FmBin(y, start, Window, 100) / FmBin(y, start, Window, 50));
                    float[] Simulate(bool cycles)
                    {
                        var y = new float[Window * 3]; double phase = 0; float previous = 0;
                        for (int i = 0; i < y.Length; i++) { float fb = 2 * previous; float s = cycles ? (float)Math.Sin((phase + fb) * 2 * Math.PI) : (float)Math.Sin(phase * 2 * Math.PI + fb); previous = s / 4; y[i] = s; phase += 500.0 / 48000; phase -= Math.Floor(phase); }
                        return y;
                    }
                    double rendered = Ratio(x, 9600), radians = Ratio(Simulate(false), Window), cycles2 = Ratio(Simulate(true), Window);
                    report.AppendLine("MEASURE Parallel, A alone, Output feedback 2: second harmonic " + rendered.ToString("0.00") + " dB re fundamental; radians model " + radians.ToString("0.00") + " dB; the old cycles model " + cycles2.ToString("0.00") + " dB");
                    Need(Math.Abs(rendered - radians) < .3, "Not the radians formula");
                    Need(Math.Abs(rendered - cycles2) > 3, "Indistinguishable from the old formula");
                });
                Check("Free phase: repeatable, keeps each note's level, starts notes at their own points", () =>
                {
                    var off = Keep(FmQualityInstrument(5, 0, .8f, true, false)); var on = Keep(FmQualityInstrument(5, 0, .8f, true, true));
                    var x0 = FmQualityRender(off, 12032, 60); var x1 = FmQualityRender(on, 12032, 60); var x1b = FmQualityRender(on, 12032, 60);
                    Need(x1.SequenceEqual(x1b), "Two renders of the same note differ");
                    Need(!x1.SequenceEqual(x0), "Free phase changed nothing");
                    double Rms(float[] y) { double s = 0; for (int i = 2400; i < y.Length; i++) s += y[i] * y[i]; return Math.Sqrt(s / (y.Length - 2400)); }
                    double levelDb = 20 * Math.Log10(Rms(x1) / Rms(x0));
                    // Parallel's four carriers at the same ratio stay in phase with each other, so the note's level is unchanged.
                    report.AppendLine("MEASURE Parallel (4 carriers at ratio 1): Free phase changes the level by " + levelDb.ToString("+0.000;-0.000") + " dB");
                    Need(Math.Abs(levelDb) < .05, "Free phase changed the level of a note: " + levelDb);
                    int First(float[] y) { for (int i = 0; i < y.Length; i++) if (y[i] != 0) return i; return -1; }
                    var y62 = FmQualityRender(on, 2048, 62); var z62 = FmQualityRender(off, 2048, 62);
                    report.AppendLine("MEASURE first samples: phase 0 note 60 " + x0[First(x0)].ToString("0.0000") + ", note 62 " + z62[First(z62)].ToString("0.0000") + "; Free phase note 60 " + x1[First(x1)].ToString("0.0000") + ", note 62 " + y62[First(y62)].ToString("0.0000"));
                });
                Check("Oversample: less aliasing on a bright high note, the same level on a plain tone, no allocation", () =>
                {
                    // 2090 Hz = exactly 209 cycles in 4800 samples.
                    string line = "MEASURE aliasing between the harmonics, 2090 Hz held:";
                    // The default operator levels held at their default sustain (A 70%, B and D 35%), and every modulator at 150%.
                    ZTrackerInstrument Bright(bool single, bool strong)
                    {
                        var a = Keep(FmQualityInstrument(104, 2090, 1.5f, single, false));
                        if (!strong) { var q = a.model.parameters; for (int k = 0; k < 3; k++) { var o = q.fmOperators[k]; o.level = k == 0 ? .7f : .35f; q.fmOperators[k] = o; } }
                        return a;
                    }
                    var gains = new List<double>();
                    foreach (bool strong in new[] { false, true })
                    {
                        double single = FmAliasDb(FmQualityRender(Bright(true, strong), 14400, 69), 9600, Window, 209);
                        double doubled = FmAliasDb(FmQualityRender(Bright(false, strong), 14400, 69), 9600, Window, 209);
                        line += (strong ? " modulators at 150%: " : " default levels: ") + single.ToString("0.0") + " dB -> " + doubled.ToString("0.0") + " dB;";
                        gains.Add(single - doubled);
                    }
                    report.AppendLine(line);
                    Need(gains[0] > 10, "Oversampling removed only " + gains[0].ToString("0.0") + " dB of aliasing at the default levels");
                    Need(gains[1] > 2, "Oversampling removed nothing at strong modulation");
                    float[] plain1 = FmQualityRender(Keep(FmQualityInstrument(104, 1000, 0f, true, false)), 14400, 69), plain2 = FmQualityRender(Keep(FmQualityInstrument(104, 1000, 0f, false, false)), 14400, 69);
                    double r1 = Math.Sqrt(FmBin(plain1, 9600, Window, 100)), r2 = Math.Sqrt(FmBin(plain2, 9600, Window, 100)), db = 20 * Math.Log10(r2 / r1);
                    report.AppendLine("MEASURE a plain 1000 Hz sine: level change with oversampling " + db.ToString("+0.0000;-0.0000") + " dB");
                    Need(Math.Abs(db) < .01, "Oversampling changed a plain tone's level: " + db);
                    var snap = PlaybackSnapshot.CreateClean(Keep(FmQualityInstrument(104, 0, .8f, false, false)), 12);
                    var l = new NativeArray<float>(256, Allocator.Persistent); var r = new NativeArray<float>(256, Allocator.Persistent); long allocated = 0;
                    try
                    {
                        using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(snap.Data, SlideRate)))
                        {
                            for (int k = 0; k < 8; k++) e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 60 + k * 2, 100, 100 / 127f, TrackerLiveSource.Computer));
                            e.Render(l, r, 256, 256);
                            for (int i = 0; i < 100; i++) { long before = GC.GetAllocatedBytesForCurrentThread(); e.Render(l, r, 256, 256); allocated += GC.GetAllocatedBytesForCurrentThread() - before; }
                        }
                    }
                    finally { l.Dispose(); r.Dispose(); snap.Dispose(); }
                    report.AppendLine("MEASURE 8 oversampled FM notes, 100 blocks: " + allocated + " bytes allocated");
                    Need(allocated == 0, "Allocated " + allocated + " bytes");
                });
            }
            finally { foreach (var o in made) DestroyImmediate(o); }
            return "CheckFmQuality passed=" + passed + " failed=" + failed + "\n" + report;
        }

        public static string CheckFmQualityUi()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>(); var songAsset = CreateInstance<ZTrackerSong>(); var inst = CreateInstance<ZTrackerInstrument>();
            inst.schemaVersion = songAsset.schemaVersion = 1;
            void Fresh() { inst.model = NewInstrumentData(); inst.model.synthMode = SynthMode.FM; inst.model.parameters.type = InstrumentType.FM; songAsset.model = TrackerEngineCheck.FixtureSong(inst); }
            Fresh(); window.song = songAsset; window.instrument = inst; window.pane = 3; window.instrumentTab = 0;
            window.Show(); window.position = new Rect(160, 120, 1120, 760); window.CreateGUI();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Layout() { var panel = window.rootVisualElement.panel; Need(panel != null, "Window not attached"); for (int i = 0; i < 3; i++) panel.GetType().GetMethod("ValidateLayout", flags)?.Invoke(panel, null); }
            void Rebuild() { window.BuildPane(); Laubrary.Zui.ZuiAudit.ExpandAll(window); Layout(); }
            void Check(string name, Action action)
            {
                try { window.StopPreview(); Undo.ClearUndo(inst); Fresh(); window.pane = 3; window.instrumentTab = 0; Rebuild(); action(); passed++; report.AppendLine("PASS " + name); }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + name + ": " + (ex.InnerException ?? ex).Message); }
            }
            VisualElement Q(string name) => window.rootVisualElement.Q(name);
            var q = (Func<InstrumentParameters>)(() => inst.model.parameters);
            try
            {
                foreach (var size in new[] { new Vector2(1120, 760), new Vector2(760, 760) })
                    Check("Oversample and Free phase sit beside each other above the routing at " + size.x + " px, without overlaps", () =>
                    {
                        window.position = new Rect(160, 120, size.x, size.y); Rebuild();
                        var os = Q("fm-oversample"); var fp = Q("fm-free-phase"); Need(os != null && fp != null && Q("fm-setups") != null, "Missing FM settings");
                        Need(!os.worldBound.Overlaps(fp.worldBound), "The two toggles overlap");
                        Need(os.worldBound.width < 200 && fp.worldBound.width < 200, "A toggle stretches: " + os.worldBound.width + " / " + fp.worldBound.width);
                        report.AppendLine("MEASURE at " + size.x + " px: Oversample " + os.worldBound + ", Free phase " + fp.worldBound);
                    });
                Check("clicking each toggle stores it, the tooltip follows the state, Undo and Redo", () =>
                {
                    foreach (var (name, read, startsOn) in new (string, Func<bool>, bool)[] { ("fm-oversample", () => !q().fmSingleRate, true), ("fm-free-phase", () => q().fmFreePhase, false) })
                    {
                        Rebuild(); Need(read() == startsOn, name + " does not start " + (startsOn ? "on" : "off"));
                        string before = Q(name).tooltip; Need(before.StartsWith(startsOn ? "On:" : "Off:"), name + " tooltip does not describe the current state: " + before);
                        PointerClick(Q(name).Q<Button>() ?? Q(name)); Layout();
                        Need(read() == !startsOn, name + " click did not store");
                        string after = Q(name).tooltip; Need(after.StartsWith(!startsOn ? "On:" : "Off:"), name + " tooltip did not follow: " + after);
                        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); bool undone = read(); Undo.PerformRedo(); bool redone = read();
                        report.AppendLine("MEASURE " + name + ": " + startsOn + " -> click " + !startsOn + ", Undo " + undone + ", Redo " + redone);
                        Need(undone == startsOn && redone == !startsOn, name + " Undo/Redo " + undone + " " + redone);
                    }
                });
            }
            finally
            {
                window.StopPreview(); window.Close();
                Undo.ClearUndo(inst); Undo.ClearUndo(songAsset); DestroyImmediate(inst); DestroyImmediate(songAsset);
            }
            return "CheckFmQualityUi passed=" + passed + " failed=" + failed + "\n" + report;
        }
    }
}
