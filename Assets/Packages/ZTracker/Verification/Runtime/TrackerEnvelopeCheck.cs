using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>
    /// Rendered-audio checks for envelope columns (T-0601). A constant (DC) looped sample makes the output level equal to
    /// the driven gain times a fixed factor, so the rendered level can be compared sample by sample with the drawn line.
    /// Covers straight and held segments, the span rule (driven only between the first and last point), a typed command
    /// taking its own row, the built-in 0L/0P curves, seek/stop/play/loop, partition sizes, allocation, and the song
    /// file round trip. No assets are written.
    /// </summary>
    public static class TrackerEnvelopeCheck
    {
        const int Rate = 48000, Row = 6000, Tick = 1500; // 120 BPM, 4 lines per beat, 4 ticks per line
        static void Need(bool ok, string why) { if (!ok) throw new Exception(why); }

        sealed class Fixture : IDisposable
        {
            public ZTrackerInstrument instrument;
            public SongData song;
            public TrackData Track => song.tracks[0];
            public PatternTrack Pattern => song.patterns[0].tracks[0];
            public Fixture(AudioClip clip, int lines = 16)
            {
                instrument = TrackerEngineCheck.FixtureInstrument(clip);
                instrument.model.provenance = "";
                var s = instrument.model.sampler.samples[0];
                s.loop = SampleLoop.Forward; s.loopStartFrame = 0; s.loopEndFrame = clip.samples;
                song = TrackerEngineCheck.FixtureSong(instrument, 1, lines);
                song.ticksPerLine = 4;
                Pattern.WriteLine(new PatternLine { line = 0, notes = { TrackerEngineCheck.Note(60) } });
            }
            public DynamicParameterData Define(string id, string parameter, float min, float max)
            {
                song.commandModelVersion = ZTrackerCommandRegistry.CurrentVersion;
                var p = new DynamicParameterData { uid = "uid-" + id, id = id, name = "Check " + id, scope = DynamicScope.Track, target = new ParameterTarget { kind = ParameterKind.Mixer, trackId = "notes", parameter = parameter }, min = min, max = max };
                song.dynamicParameters.Add(p);
                return p;
            }
            /// An envelope column bound to <paramref name="command"/>, with points (row, value, held?).
            public AutomationLane Envelope(string command, params (int row, float value, bool held)[] points)
            {
                var column = new EnvelopeColumnData { uid = "env" + Track.envelopeColumns.Count, command = command };
                Track.envelopeColumns.Add(column);
                var lane = ZTrackerEnvelopes.EnsureLane(Pattern, Track, column);
                foreach (var p in points) ZTrackerEnvelopes.Set(lane, p.row, p.value, p.held ? AutomationShape.Step : AutomationShape.Lane);
                return lane;
            }
            public void Typed(int row, string id, int value)
            {
                var line = Pattern.ReadLine(row);
                var l = new PatternLine { line = row, notes = new List<NoteCell>(line.notes), effects = new List<EffectCell> { new EffectCell { column = 0, command = new CommandData { present = true, valuePresent = true, identifier = id, value = value, scope = CommandScope.Track } } } };
                Pattern.WriteLine(l);
            }
            public void Dispose() { if (instrument != null) UnityEngine.Object.DestroyImmediate(instrument); }
        }

        struct Output { public float[] l, r; public bool compiled; public List<string> diagnostics; }

        static Output Render(SongData song, int frames, int block = 1024, Action<TrackerOffline> afterPlay = null)
        {
            var o = new Output { l = new float[frames], r = new float[frames] };
            var prepared = TrackerPreparedSong.Prepare(song, Rate);
            o.diagnostics = new List<string>(prepared.diagnostics);
            var left = new NativeArray<float>(block, Allocator.Persistent); var right = new NativeArray<float>(block, Allocator.Persistent);
            try
            {
                using (var e = new TrackerOffline(prepared))
                {
                    e.SendCommand(TrackerCommand.Play()); afterPlay?.Invoke(e);
                    for (int at = 0; at < frames; at += block)
                    {
                        int n = Math.Min(block, frames - at); e.Render(left, right, n, n);
                        for (int i = 0; i < n; i++) { o.l[at + i] = left[i]; o.r[at + i] = right[i]; }
                        while (e.ReadEvent(out _)) { }
                    }
                    o.compiled = e.Compiled;
                }
            }
            finally { left.Dispose(); right.Dispose(); }
            return o;
        }

        static bool Same(float[] a, float[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false; return true; }
        static double Mean(float[] x, int from, int count) { double s = 0; for (int i = from; i < from + count; i++) s += x[i]; return s / count; }

        public static string Execute()
        {
            var report = new StringBuilder(); int pass = 0, fail = 0;
            void Check(string name, Action run) { try { run(); pass++; report.AppendLine("PASS " + name); } catch (Exception ex) { fail++; report.AppendLine("FAIL " + name + ": " + ex.Message); } }
            var clip = AudioClip.Create("envelope-check", 4800, 1, Rate, false);
            var raw = new float[4800]; for (int i = 0; i < raw.Length; i++) raw[i] = .5f; clip.SetData(raw, 0);
            int frames = Row * 12;
            float[] baseline;
            using (var f = new Fixture(clip)) baseline = Render(f.song, frames).l;
            // Output per unit of post gain (the authored gain is 1).
            double k = Mean(baseline, Row * 2, Row);
            // Largest difference, in units of the command's range, between the rendered level and the drawn line.
            double Departure(float[] l, AutomationLane lane, int fromSample, int toSample, Func<double, double> map = null)
            {
                double worst = 0;
                for (int i = fromSample; i < toSample; i++)
                {
                    var u = ZTrackerEnvelopes.ValueAt(lane, i / (double)Row); if (!u.HasValue) continue;
                    double expected = map == null ? u.Value : map(u.Value);
                    worst = Math.Max(worst, Math.Abs(l[i] / k - expected));
                }
                return worst;
            }
            try
            {
                Check("Fixture: a constant sample gives a constant level", () =>
                {
                    Need(k > .01, "fixture is silent");
                    for (int i = Row; i < frames; i++) Need(Math.Abs(baseline[i] / k - 1) < 1e-5, "baseline is not constant at sample " + i);
                });

                Check("An envelope column with no points, bound to a song command or to 0L, renders bit-identically to none", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define("TV", "postVolume", 0, 1);
                        f.Track.envelopeColumns.Add(new EnvelopeColumnData { uid = "a", command = p.uid });
                        f.Track.envelopeColumns.Add(new EnvelopeColumnData { uid = "b", command = "0L" });
                        f.Track.envelopeColumns.Add(new EnvelopeColumnData { uid = "c", command = "" });
                        var o = Render(f.song, frames);
                        Need(Same(o.l, baseline), "an empty envelope column changed the audio");
                        Need(o.compiled, "render was not Burst-compiled");
                    }
                });

                Check("Straight segments: the rendered level follows the drawn line (linear between rows, continuous within a row)", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define("TV", "postVolume", 0, 1);
                        var lane = f.Envelope(p.uid, (1, .2f, false), (5, 1f, false), (7, .4f, false), (10, .9f, false));
                        var o = Render(f.song, frames);
                        Need(o.diagnostics.All(d => !d.StartsWith("AUTOMATION")), string.Join("; ", o.diagnostics));
                        // Before the first point the command keeps its value (authored gain 1).
                        Need(Math.Abs(Mean(o.l, Row / 2, Row / 4) / k - 1) < 1e-5, "before the first point the value was not held");
                        // Where the line begins mid-song the level glides onto it within one tick, then follows it.
                        double glideEnd = o.l[Row + Tick - 1] / k, onLine = ZTrackerEnvelopes.ValueAt(lane, (Row + Tick - 1) / (double)Row).Value;
                        Need(Math.Abs(glideEnd - onLine) < .002, "the glide did not reach the line within a tick: " + glideEnd + " vs " + onLine);
                        for (int i = Row + 1; i < Row + Tick; i++) Need(o.l[i] <= o.l[i - 1] + 1e-6, "the glide onto the line is not monotonic");
                        double worst = Departure(o.l, lane, Row + Tick, Row * 10 + 1);
                        Need(worst < 1e-4, "rendered level departs from the drawn line by " + worst.ToString("0.000000"));
                        // After the last point the value holds.
                        Need(Math.Abs(Mean(o.l, Row * 11, Row) / k - .9) < 1e-4, "after the last point the value did not hold");
                        report.AppendLine("MEASURE linear envelope: largest departure from the line " + worst.ToString("0.0000000") + " (of the 0…1 range), glide onto the line within one tick (" + Tick + " samples)");
                    }
                });

                Check("Held segments: the value holds, jumps at a held point within 5 ms, and glides onto a line within a tick", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define("TV", "postVolume", 0, 1);
                        var lane = f.Envelope(p.uid, (0, .2f, true), (4, .8f, true), (6, .3f, false), (9, .6f, false));
                        var o = Render(f.song, frames);
                        double held = Math.Max(Departure(o.l, lane, Tick, Row * 4), Departure(o.l, lane, Row * 4 + 300, Row * 6));
                        Need(held < 1e-5, "a held value moved: " + held);
                        int ramp = (int)(Rate * .005);
                        Need(Math.Abs(o.l[Row * 4 + ramp + 2] / k - .8) < 1e-4, "the jump between held values did not settle within 5 ms: " + o.l[Row * 4 + ramp + 2] / k);
                        double maxStep = 0; for (int i = Row * 4; i < Row * 4 + ramp; i++) maxStep = Math.Max(maxStep, Math.Abs(o.l[i + 1] - o.l[i]) / k);
                        Need(maxStep < .6 / ramp * 1.5, "the jump is not ramped: largest step " + maxStep);
                        // Held .8 ends at row 6, where a straight line from .3 begins: glide onto it within one tick, no click.
                        Need(Math.Abs(o.l[Row * 6 + Tick] / k - ZTrackerEnvelopes.ValueAt(lane, 6 + Tick / (double)Row).Value) < .002, "did not reach the line within a tick after the held segment");
                        double glideStep = 0; for (int i = Row * 6; i < Row * 6 + Tick; i++) glideStep = Math.Max(glideStep, Math.Abs(o.l[i + 1] - o.l[i]) / k);
                        Need(glideStep < .5 / Tick * 1.5, "the glide onto the line has a click: step " + glideStep);
                        double after = Departure(o.l, lane, Row * 6 + Tick, Row * 9 + 1);
                        Need(after < 1e-4, "line after the held segment departs by " + after);
                        report.AppendLine("MEASURE held envelope: held values moved " + held.ToString("0.0000000") + "; held-to-held jump over " + ramp + " samples (largest step " + maxStep.ToString("0.00000") + "); held-to-line glide over one tick (largest step " + glideStep.ToString("0.000000") + "); line after it " + after.ToString("0.0000000"));
                    }
                });

                Check("A typed command for the same command takes its own row; the line resumes on the next row without a click", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define("TV", "postVolume", 0, 1);
                        var lane = f.Envelope(p.uid, (0, 1f, false), (8, 0f, false));
                        f.Typed(4, "TV", 0);
                        var o = Render(f.song, frames);
                        Need(Math.Abs(o.l[Row * 4 + Row / 2] / k) < 1e-4, "the typed value did not take its row: " + o.l[Row * 4 + Row / 2] / k);
                        Need(Math.Abs(o.l[Row * 5 + Tick] / k - ZTrackerEnvelopes.ValueAt(lane, 5 + Tick / (double)Row).Value) < .002, "the line did not resume by the first tick of the next row");
                        double maxStep = 0; for (int i = Row * 4 - 10; i < Row * 5 + Tick; i++) maxStep = Math.Max(maxStep, Math.Abs(o.l[i + 1] - o.l[i]) / k);
                        Need(maxStep < .01, "a click where the typed row starts or ends: step " + maxStep);
                        // A typed command after the last point holds (the envelope no longer drives the command there).
                        f.Typed(10, "TV", 0x80);
                        var o2 = Render(f.song, frames);
                        Need(Math.Abs(Mean(o2.l, Row * 11, Row) / k - 0x80 / 255.0) < 1e-4, "a typed command after the line did not hold");
                    }
                });

                Check("Built-in 0L: the column follows the 0L gain curve; typed 0L on that track shares it row by row", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var lane = f.Envelope("0L", (0, 1f, false), (8, .6f, false));
                        double Curve(double u) { double v = u * 255; return v <= 0 ? 0 : v < 1 ? v * .001 : Math.Pow(10, (-60 + 63 * (v - 1) / 254) / 20); }
                        var o = Render(f.song, frames);
                        double worst = Departure(o.l, lane, Tick, Row * 8, Curve);
                        // Between ticks the gain is a straight ramp, so it can sit slightly off the dB curve.
                        Need(worst < .002, "0L envelope departs from the 0L curve by " + worst);
                        f.Typed(9, "0L", 0x80);
                        var typed = Render(f.song, frames);
                        Need(Math.Abs(Mean(typed.l, Row * 10, Row) / k - Curve(0x80 / 255.0)) < 1e-4, "typed 0L after the line did not set the 0L curve value");
                        report.AppendLine("MEASURE 0L envelope: largest departure from the 0L gain curve " + worst.ToString("0.000000") + " (gain units; ramps are straight between ticks)");
                    }
                    using (var f = new Fixture(clip))
                    {
                        f.Envelope("0P", (0, 0x40 / 255f, false), (4, 0x40 / 255f, false));
                        var env = Render(f.song, Row * 4);
                        using (var g = new Fixture(clip)) { g.Typed(0, "0P", 0x40); var typed = Render(g.song, Row * 4); Need(Math.Abs(Mean(env.l, Row * 2, Row) - Mean(typed.l, Row * 2, Row)) < 1e-5 && Math.Abs(Mean(env.r, Row * 2, Row) - Mean(typed.r, Row * 2, Row)) < 1e-5, "0P envelope at 40 does not pan like typed 0P40"); }
                    }
                });

                Check("Seek lands on the line's value without a glide; Stop and Play restore the authored value", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define("TV", "postVolume", 0, 1);
                        var lane = f.Envelope(p.uid, (2, 0f, false), (10, 1f, false));
                        f.Pattern.WriteLine(new PatternLine { line = 6, notes = { TrackerEngineCheck.Note(60) } });
                        var o = Render(f.song, Row * 2, 1024, e => e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 6 }));
                        double worst = 0; for (int i = 0; i < Row * 2; i++) worst = Math.Max(worst, Math.Abs(o.l[i] / k - ZTrackerEnvelopes.ValueAt(lane, 6 + i / (double)Row).Value));
                        Need(worst < 1e-3, "after seeking the level departs from the line by " + worst);
                        var prepared = TrackerPreparedSong.Prepare(f.song, Rate);
                        var l = new NativeArray<float>(Row, Allocator.Persistent); var r = new NativeArray<float>(Row, Allocator.Persistent);
                        try
                        {
                            using (var e = new TrackerOffline(prepared))
                            {
                                e.SendCommand(TrackerCommand.Play()); for (int i = 0; i < 5; i++) e.Render(l, r, Row, 1024);
                                e.SendCommand(TrackerCommand.Stop()); e.SendCommand(TrackerCommand.Play()); e.Render(l, r, Row, 1024);
                                var first = new float[Row]; for (int i = 0; i < Row; i++) first[i] = l[i];
                                Need(Same(first, baseline.Take(Row).ToArray()), "row 0 after Stop/Play differs from a fresh play");
                            }
                        }
                        finally { l.Dispose(); r.Dispose(); }
                        report.AppendLine("MEASURE seek into the line: largest departure " + worst.ToString("0.000000"));
                    }
                });

                Check("Looping keeps the last value until the line begins again, then follows it", () =>
                {
                    // Read the command's own value from the engine: the fixture's note retriggers on every pass, so the
                    // level also carries the previous note's release.
                    using (var f = new Fixture(clip, 8))
                    {
                        var p = f.Define("TV", "postVolume", 0, 1);
                        var lane = f.Envelope(p.uid, (2, .5f, false), (6, .1f, false));
                        var l = new NativeArray<float>(Row / 2, Allocator.Persistent); var r = new NativeArray<float>(Row / 2, Allocator.Persistent);
                        try
                        {
                            using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(f.song, Rate)))
                            {
                                e.SendCommand(TrackerCommand.Play());
                                float Value() { var s = e.Snapshot; return s.deviceParameters[s.dynamicParameters[0]].destination; }
                                for (int half = 0; half < 32; half++)
                                {
                                    e.Render(l, r, Row / 2, 1024);
                                    double t = (half + 1) / 2.0; double inPattern = t % 8; int pass = (int)(t / 8);
                                    if (pass == 1 && inPattern > 0 && inPattern <= 2) Need(Math.Abs(Value() - .1f) < 1e-5, "on the next pass before the line began the value was " + Value() + " at row " + inPattern);
                                    if (pass == 1 && inPattern >= 2.5 && inPattern <= 6) Need(Math.Abs(Value() - ZTrackerEnvelopes.ValueAt(lane, inPattern).Value) < 1e-3, "on the next pass the value " + Value() + " is off the line at row " + inPattern);
                                }
                            }
                        }
                        finally { l.Dispose(); r.Dispose(); }
                    }
                });

                Check("Partition sizes 64/333/1024 give the same envelope result", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define("TV", "postVolume", 0, 1);
                        f.Envelope(p.uid, (1, .2f, false), (5, 1f, true), (9, .4f, false), (11, .7f, false));
                        var a = Render(f.song, frames, 1024).l; var b = Render(f.song, frames, 333).l; var c = Render(f.song, frames, 64).l;
                        double m = 0; for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Max(Math.Abs(a[i] - b[i]), Math.Abs(a[i] - c[i])));
                        Need(m < 1e-5, "partition difference " + m);
                    }
                });

                Check("No managed allocation while envelopes ramp (Burst render)", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define("TV", "postVolume", 0, 1);
                        f.Envelope(p.uid, (0, 0f, false), (3, 1f, false), (6, .2f, true), (9, .8f, false), (15, 0f, false));
                        f.Envelope("0P", (0, 0f, false), (15, 1f, false));
                        var l = new NativeArray<float>(1024, Allocator.Persistent); var r = new NativeArray<float>(1024, Allocator.Persistent);
                        try
                        {
                            using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(f.song, Rate)))
                            {
                                e.SendCommand(TrackerCommand.Play()); e.Render(l, r, 1024, 1024);
                                long before = GC.GetAllocatedBytesForCurrentThread();
                                for (int i = 0; i < 80; i++) e.Render(l, r, 1024, 1024);
                                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                                Need(e.Compiled, "render was not Burst-compiled");
                                Need(allocated == 0, allocated + " bytes allocated");
                                while (e.ReadEvent(out _)) { }
                            }
                        }
                        finally { l.Dispose(); r.Dispose(); }
                    }
                });

                Check("Song file: columns, bindings, points and segment shapes survive a round trip; older songs read empty", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define("TV", "postVolume", 0, 1);
                        f.Envelope(p.uid, (1, .25f, true), (5, .75f, false));
                        f.Envelope("0P", (2, .5f, false), (3, .6f, false));
                        string json = JsonUtility.ToJson(f.song);
                        var back = JsonUtility.FromJson<SongData>(json);
                        Need(JsonUtility.ToJson(back) == json, "round trip changed the song");
                        Need(back.tracks[0].envelopeColumns.Count == 2 && back.tracks[0].envelopeColumns[0].command == p.uid && back.tracks[0].envelopeColumns[1].command == "0P", "columns or bindings lost");
                        var lane = ZTrackerEnvelopes.Lane(back.patterns[0].tracks[0], back.tracks[0].envelopeColumns[0]);
                        Need(lane != null && lane.points.Count == 2 && lane.points[0].segment == AutomationShape.Step && lane.points[1].value == .75f, "points or shapes lost");
                        var old = JsonUtility.FromJson<SongData>("{\"tracks\":[{\"id\":\"t\",\"name\":\"Old\"}],\"patterns\":[{\"id\":\"p\",\"tracks\":[{\"trackId\":\"t\",\"automation\":[{\"id\":\"lane\",\"points\":[{\"line\":1,\"value\":0.5}]}]}]}]}");
                        Need(old.tracks[0].envelopeColumns != null && old.tracks[0].envelopeColumns.Count == 0, "an older track did not read with no envelope columns");
                        Need(old.patterns[0].tracks[0].automation[0].points[0].segment == AutomationShape.Lane, "an older point did not read as following its lane");
                    }
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
            return "TrackerEnvelopeCheck " + pass + " passed / " + fail + " failed\n" + report;
        }
    }
}
