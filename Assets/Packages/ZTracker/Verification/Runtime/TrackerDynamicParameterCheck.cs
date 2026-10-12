using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>
    /// Rendered-audio checks for song command definitions (dynamic parameters, T-0596): every target kind changes the
    /// output as commanded, a command at the authored value is bit-identical to none, changes ramp without a click,
    /// values survive a missing voice, seek/stop/play/loop restore or keep the right value, a linear lane follows its
    /// line, the render runs in Burst without allocating, and malformed definitions are reported. No assets are written.
    /// </summary>
    public static class TrackerDynamicParameterCheck
    {
        const int Rate = 48000, Row = 6000; // 120 BPM, 4 lines per beat
        static void Need(bool ok, string why) { if (!ok) throw new Exception(why); }

        sealed class Fixture : IDisposable
        {
            public ZTrackerInstrument instrument;
            public SongData song;
            public Fixture(AudioClip clip, int lines = 16)
            {
                instrument = TrackerEngineCheck.FixtureInstrument(clip);
                instrument.model.provenance = "";
                var s = instrument.model.sampler.samples[0];
                s.loop = SampleLoop.Forward; s.loopStartFrame = 0; s.loopEndFrame = clip.samples;
                song = TrackerEngineCheck.FixtureSong(instrument, 1, lines);
                song.ticksPerLine = 4;
                song.tracks[0].devices.nodes.Add(new AudioEffectNodeData { type = Laubrary.Zounds.ZoundEffectType.Gain, uid = "fx-gain", p = new[] { 1f } });
                Row(0, TrackerEngineCheck.Note(60));
            }
            public void Row(int line, NoteCell note = null, params CommandData[] fx)
            {
                var existing = song.patterns[0].tracks[0].ReadLine(line);
                var l = new PatternLine { line = line, notes = new List<NoteCell>(existing.notes), effects = new List<EffectCell>(existing.effects) };
                if (note != null) { l.notes.RemoveAll(n => n.column == note.column); l.notes.Add(note); }
                for (int i = 0; i < fx.Length; i++) { l.effects.RemoveAll(e => e.column == i); l.effects.Add(new EffectCell { column = i, command = fx[i] }); }
                song.patterns[0].tracks[0].WriteLine(l);
            }
            public DynamicParameterData Define(string id, DynamicScope scope, ParameterTarget target, float min, float max)
            {
                song.commandModelVersion = ZTrackerCommandRegistry.CurrentVersion;
                var p = new DynamicParameterData { uid = "uid-" + id, id = id, name = "Check " + id, scope = scope, target = target, min = min, max = max };
                song.dynamicParameters.Add(p);
                return p;
            }
            public void Dispose() { if (instrument != null) UnityEngine.Object.DestroyImmediate(instrument); }
        }

        static CommandData Fx(string id, int value) => new CommandData { present = true, valuePresent = true, identifier = id, value = value, scope = CommandScope.Track };
        static ParameterTarget Track(string parameter) => new ParameterTarget { kind = ParameterKind.Mixer, trackId = "notes", parameter = parameter };

        struct Output { public float[] l, r; public List<TrackerEvent> events; public bool compiled; public List<string> diagnostics; }

        static Output Render(SongData song, int frames, int block = 1024, Action<TrackerOffline> afterPlay = null, int[] stopAt = null)
        {
            var o = new Output { l = new float[frames], r = new float[frames], events = new List<TrackerEvent>() };
            var prepared = TrackerPreparedSong.Prepare(song, Rate);
            o.diagnostics = new List<string>(prepared.diagnostics);
            var left = new NativeArray<float>(block, Allocator.Persistent);
            var right = new NativeArray<float>(block, Allocator.Persistent);
            try
            {
                using (var e = new TrackerOffline(prepared))
                {
                    e.SendCommand(TrackerCommand.Play());
                    afterPlay?.Invoke(e);
                    for (int at = 0; at < frames; at += block)
                    {
                        int n = Math.Min(block, frames - at);
                        e.Render(left, right, n, n);
                        for (int i = 0; i < n; i++) { o.l[at + i] = left[i]; o.r[at + i] = right[i]; }
                        while (e.ReadEvent(out var ev)) o.events.Add(ev);
                    }
                    o.compiled = e.Compiled;
                }
            }
            finally { left.Dispose(); right.Dispose(); }
            return o;
        }

        static double Rms(float[] x, int from, int count) { double s = 0; for (int i = from; i < from + count; i++) s += (double)x[i] * x[i]; return Math.Sqrt(s / count); }
        static double MaxJump(float[] x, int from, int count) { double m = 0; for (int i = Math.Max(1, from); i < from + count; i++) m = Math.Max(m, Math.Abs(x[i] - x[i - 1])); return m; }
        static bool Same(float[] a, float[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false; return true; }

        public static string Execute()
        {
            var report = new StringBuilder(); int pass = 0, fail = 0;
            void Check(string name, Action run) { try { run(); pass++; report.AppendLine("PASS " + name); } catch (Exception ex) { fail++; report.AppendLine("FAIL " + name + ": " + ex.Message); } }
            // A looped sine whose period divides the loop, so the loop seam itself never jumps.
            var clip = AudioClip.Create("dynamic-check", 48000, 1, Rate, false);
            var raw = new float[48000]; for (int i = 0; i < raw.Length; i++) raw[i] = .5f * Mathf.Sin(2 * Mathf.PI * 480 * i / Rate); clip.SetData(raw, 0);
            int frames = Row * 8;
            float[] baseline, baselineRight;
            using (var f = new Fixture(clip)) { var b = Render(f.song, frames); baseline = b.l; baselineRight = b.r; }
            double baseRms = Rms(baseline, Row * 6, Row);
            try
            {
                Check("Empty registry, version 1 and unused definitions are bit-identical to an old song", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.song.commandModelVersion = 1;
                        Need(Same(Render(f.song, frames).l, baseline), "version 1 with no definitions changed the audio");
                        f.Define("TV", DynamicScope.Track, Track("postVolume"), 0, 2);
                        f.Define("GA", DynamicScope.Effect, new ParameterTarget { kind = ParameterKind.Device, trackId = "notes", deviceId = "fx-gain", index = 0 }, 0, 4);
                        f.Define("IV", DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.Sample, instrumentId = f.instrument.model.id, parameter = "volume" }, 0, 2);
                        var o = Render(f.song, frames);
                        Need(Same(o.l, baseline) && Same(o.r, baselineRight), "unused definitions changed the audio");
                        Need(o.compiled, "render was not Burst-compiled");
                    }
                });

                // Each target kind, commanded at row 4, measured at row 6 against the same rows without the command.
                void Kind(string name, Func<Fixture, CommandData> setup, double expectedRatio, bool rightChannel = false)
                {
                    Check(name, () =>
                    {
                        using (var f = new Fixture(clip))
                        {
                            var cmd = setup(f);
                            f.Row(4, null, cmd);
                            var o = Render(f.song, frames);
                            Need(o.compiled, "render was not Burst-compiled");
                            Need(o.diagnostics.All(d => !d.StartsWith("DYNAMIC")), "diagnostics: " + string.Join("; ", o.diagnostics.Where(d => d.StartsWith("DYNAMIC"))));
                            var x = rightChannel ? o.r : o.l;
                            var before = Rms(x, Row * 2, Row) / Rms(rightChannel ? baselineRight : baseline, Row * 2, Row);
                            var ratio = Rms(x, Row * 6, Row) / (rightChannel ? Rms(baselineRight, Row * 6, Row) : baseRms);
                            Need(Math.Abs(before - 1) < 1e-6, "audio before the command changed (" + before + ")");
                            Need(Math.Abs(ratio - expectedRatio) <= .01 + .01 * expectedRatio, "level after the command " + ratio.ToString("0.0000") + " expected " + expectedRatio);
                            // No click: the transition's largest sample step stays within the larger steady step around it.
                            double steady = Math.Max(MaxJump(x, Row * 3, Row), MaxJump(x, Row * 5, Row));
                            double transition = MaxJump(x, Row * 4 - 64, 2048);
                            Need(transition <= steady * 1.02 + 1e-6, "click at the change: step " + transition.ToString("0.00000") + " vs steady " + steady.ToString("0.00000"));
                            // The ramp is short: 12 ms after the row starts the new level is reached.
                            double settled = Rms(x, Row * 4 + 600, 400) / (rightChannel ? 1 : baseRms);
                            if (!rightChannel) Need(Math.Abs(settled - expectedRatio) <= .02 + .02 * expectedRatio, "not settled 12 ms after the command: " + settled.ToString("0.000"));
                            report.AppendLine("     " + name + ": level ×" + ratio.ToString("0.000") + ", transition step " + transition.ToString("0.0000") + " vs steady " + steady.ToString("0.0000"));
                        }
                    });
                }
                Kind("Track post gain command (TV 00 over 0..2) silences the track", f => { f.Define("TV", DynamicScope.Track, Track("postVolume"), 0, 2); return Fx("TV", 0); }, 0);
                Kind("Track pre gain command (TP 7F over 0..1)", f => { f.Define("TP", DynamicScope.Track, Track("preVolume"), 0, 1); return Fx("TP", 0x7F); }, 127 / 255.0);
                Kind("Track width command keeps a mono source", f => { f.Define("TW", DynamicScope.Track, Track("width"), 0, 2); return Fx("TW", 0); }, 1);
                Kind("Track pre pan command (PN 00 = left) empties the right channel", f => { f.Define("PN", DynamicScope.Track, Track("prePan"), -1, 1); return Fx("PN", 0); }, 0, true);
                Kind("Track post pan command (QN FF = right) keeps the right channel", f => { f.Define("QN", DynamicScope.Track, Track("postPan"), -1, 1); return Fx("QN", 0xFF); }, 1, true);
                Kind("Effect setting command (Gain effect GA FF over 0..4) raises the level fourfold", f => { f.Define("GA", DynamicScope.Effect, new ParameterTarget { kind = ParameterKind.Device, trackId = "notes", deviceId = "fx-gain", index = 0 }, 0, 4); return Fx("GA", 0xFF); }, 4);
                Kind("Instrument parameter command (volume IV 00) silences the instrument", f => { f.Define("IV", DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.Sample, instrumentId = f.instrument.model.id, parameter = "volume" }, 0, 2); return Fx("IV", 0); }, 0);
                Kind("Instrument parameter command (volume IV FF over 0..2) doubles the level", f => { f.Define("IV", DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.Sample, instrumentId = f.instrument.model.id, parameter = "volume" }, 0, 2); return Fx("IV", 0xFF); }, 2);
                Kind("Instrument macro command (MC 00, macro mapped to volume) silences the instrument", f =>
                {
                    var m = f.instrument.model.macros[0]; m.value = 1;
                    m.mappings.Add(new Mapping { target = new ParameterTarget { kind = ParameterKind.Sample, instrumentId = f.instrument.model.id, parameter = "volume", units = "linear gain" }, min = 0, max = 1 });
                    f.Define("MC", DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.InstrumentMacro, instrumentId = f.instrument.model.id, index = 0 }, 0, 1);
                    return Fx("MC", 0);
                }, 0);

                Check("Song tempo command applies to the entering row (BT B4 over 60..315 = 240 BPM)", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define("BT", DynamicScope.Song, new ParameterTarget { kind = ParameterKind.Timing, parameter = "bpm" }, 60, 315);
                        f.Row(2, null, Fx("BT", 0xB4));
                        var o = Render(f.song, frames);
                        var rows = o.events.Where(e => e.kind == TrackerEventKind.Row).Select(e => e.samplePosition).ToList();
                        Need(rows.Count > 4, "too few rows");
                        Need(rows[1] - rows[0] == Row && rows[3] - rows[2] == Row / 2 && rows[4] - rows[3] == Row / 2, "row starts " + string.Join(",", rows.Take(5)));
                    }
                });

                Check("A command at the authored value is bit-identical to no command (track, effect, instrument)", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        float authoredVolume;
                        using (var probe = TrackerPreparedSong.Prepare(f.song, Rate)) authoredVolume = probe.state.parameterBase[(int)TrackerParameter.Volume];
                        f.Define("TV", DynamicScope.Track, Track("postVolume"), 0, 1);
                        f.Define("GA", DynamicScope.Effect, new ParameterTarget { kind = ParameterKind.Device, trackId = "notes", deviceId = "fx-gain", index = 0 }, 0, 1);
                        f.Define("IV", DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.Sample, instrumentId = f.instrument.model.id, parameter = "volume" }, 0, authoredVolume);
                        f.Row(1, null, Fx("TV", 0xFF), Fx("GA", 0xFF));
                        f.Row(3, null, Fx("IV", 0xFF));
                        f.song.tracks[0].visibleEffectColumns = 2;
                        var o = Render(f.song, frames);
                        Need(Same(o.l, baseline), "default commands changed the audio");
                    }
                });

                Check("A value set before any voice exists is used by the next note", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.song.patterns[0].tracks[0].lines.Clear();
                        f.Define("IV", DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.Sample, instrumentId = f.instrument.model.id, parameter = "volume" }, 0, 2);
                        f.Row(0, null, Fx("IV", 0x80));
                        f.Row(2, TrackerEngineCheck.Note(60));
                        var o = Render(f.song, frames);
                        double ratio = Rms(o.l, Row * 4, Row) / baseRms;
                        Need(Math.Abs(ratio - 2 * 0x80 / 255.0) < .02, "note after the command plays at " + ratio.ToString("0.000"));
                    }
                });

                Check("Seek lands on the value the row would have, with no ramp; before the command it is untouched", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define("TV", DynamicScope.Track, Track("postVolume"), 0, 2);
                        f.Row(2, null, Fx("TV", 0));
                        f.Row(5, TrackerEngineCheck.Note(60));
                        var after = Render(f.song, Row * 2, 1024, e => e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 5 }));
                        Need(after.l.All(v => v == 0), "seek past the command was not silent from its first sample");
                        f.Row(1, TrackerEngineCheck.Note(60));
                        var before = Render(f.song, Row, 1024, e => e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 1 }));
                        Need(Rms(before.l, 0, Row) > baseRms * .9, "seek before the command lost the authored value");
                    }
                });

                Check("Stop and Play restore every target's authored value", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define("TP", DynamicScope.Track, Track("preVolume"), 0, 1);
                        f.Define("GA", DynamicScope.Effect, new ParameterTarget { kind = ParameterKind.Device, trackId = "notes", deviceId = "fx-gain", index = 0 }, 0, 4);
                        f.Define("IV", DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.Sample, instrumentId = f.instrument.model.id, parameter = "volume" }, 0, 2);
                        f.song.tracks[0].visibleEffectColumns = 3;
                        f.Row(1, null, Fx("TP", 0x40), Fx("GA", 0x10), Fx("IV", 0x30));
                        var prepared = TrackerPreparedSong.Prepare(f.song, Rate);
                        var l = new NativeArray<float>(Row, Allocator.Persistent); var r = new NativeArray<float>(Row, Allocator.Persistent);
                        try
                        {
                            using (var e = new TrackerOffline(prepared))
                            {
                                e.SendCommand(TrackerCommand.Play()); for (int i = 0; i < 3; i++) e.Render(l, r, Row, 1024);
                                e.SendCommand(TrackerCommand.Stop()); e.SendCommand(TrackerCommand.Play());
                                e.Render(l, r, Row, 1024);
                                var first = new float[Row]; for (int i = 0; i < Row; i++) first[i] = l[i];
                                Need(Same(first, baseline.Take(Row).ToArray()), "row 0 after Stop/Play differs from a fresh play");
                            }
                        }
                        finally { l.Dispose(); r.Dispose(); }
                    }
                });

                Check("Looping keeps the commanded value", () =>
                {
                    using (var f = new Fixture(clip, 8))
                    {
                        f.Define("TV", DynamicScope.Track, Track("postVolume"), 0, 2);
                        f.Row(4, null, Fx("TV", 0));
                        var o = Render(f.song, Row * 11);
                        Need(Rms(o.l, Row * 9, Row) == 0, "after the loop the value was restored");
                    }
                });

                Check("A linear lane on a dynamic parameter follows its line continuously (envelope hook)", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define("TV", DynamicScope.Track, Track("postVolume"), 0, 1);
                        var lane = new AutomationLane { id = "env", interpolation = AutomationInterpolation.Linear, target = new ParameterTarget { kind = ParameterKind.Dynamic, parameter = p.uid } };
                        lane.points.Add(new AutomationPoint { line = 0, value = 1 }); lane.points.Add(new AutomationPoint { line = 8, value = 0 });
                        f.song.patterns[0].tracks[0].automation.Add(lane);
                        var o = Render(f.song, frames);
                        Need(o.diagnostics.All(d => !d.StartsWith("AUTOMATION")), string.Join("; ", o.diagnostics));
                        double worst = 0;
                        for (int at = 0; at < Row * 8 - 600; at += 300)
                        {
                            double expected = 1 - (at + 150) / (double)(Row * 8);
                            double got = Rms(o.l, at, 300) / baseRms;
                            worst = Math.Max(worst, Math.Abs(got - expected));
                        }
                        Need(worst < .01, "lane level departs from its line by " + worst.ToString("0.0000"));
                        double steady = MaxJump(baseline, Row * 2, Row);
                        Need(MaxJump(o.l, 0, Row * 8) <= steady * 1.001, "lane produced a step larger than the steady signal");
                        report.AppendLine("     lane: largest departure from the line " + worst.ToString("0.00000"));
                    }
                });

                Check("Partition sizes 64/333/1024 give the same command result", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define("TV", DynamicScope.Track, Track("postVolume"), 0, 2);
                        f.Row(4, null, Fx("TV", 0x40));
                        var a = Render(f.song, frames, 1024).l; var b = Render(f.song, frames, 333).l; var c = Render(f.song, frames, 64).l;
                        double m = 0; for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Max(Math.Abs(a[i] - b[i]), Math.Abs(a[i] - c[i])));
                        Need(m < 1e-5, "partition difference " + m);
                    }
                });

                Check("No managed allocation while commands ramp (Burst render)", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define("TV", DynamicScope.Track, Track("postVolume"), 0, 2);
                        f.Define("GA", DynamicScope.Effect, new ParameterTarget { kind = ParameterKind.Device, trackId = "notes", deviceId = "fx-gain", index = 0 }, 0, 4);
                        f.Define("IV", DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.Sample, instrumentId = f.instrument.model.id, parameter = "volume" }, 0, 2);
                        f.song.tracks[0].visibleEffectColumns = 3;
                        for (int row = 1; row < 16; row++) f.Row(row, null, Fx("TV", row * 16), Fx("GA", 255 - row * 16), Fx("IV", row * 8));
                        var l = new NativeArray<float>(1024, Allocator.Persistent); var r = new NativeArray<float>(1024, Allocator.Persistent);
                        try
                        {
                            using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(f.song, Rate)))
                            {
                                e.SendCommand(TrackerCommand.Play());
                                e.Render(l, r, 1024, 1024);
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

                Check("Malformed definitions and misplaced ids are reported, neighbours still compile", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define("TV", DynamicScope.Track, Track("postVolume"), 0, 2);
                        f.Define("TV", DynamicScope.Track, Track("preVolume"), 0, 2).uid = "dup";
                        f.Define("0Q", DynamicScope.Track, Track("preVolume"), 0, 2).uid = "reserved";
                        f.Define("XX", DynamicScope.Track, new ParameterTarget { kind = ParameterKind.Mixer, trackId = "missing", parameter = "preVolume" }, 0, 2);
                        f.song.tracks[0].visibleEffectColumns = 2;
                        f.Row(1, null, Fx("QQ", 1), Fx("TV", 0));
                        var local = TrackerEngineCheck.Note(60); local.sampleFx = new CommandData { present = true, valuePresent = true, identifier = "TV", value = 0, scope = CommandScope.Column };
                        f.Row(2, local);
                        var o = Render(f.song, frames);
                        string all = string.Join("\n", o.diagnostics);
                        Need(all.Contains("DYNAMIC_ID_DUPLICATE") && all.Contains("DYNAMIC_ID_INVALID") && all.Contains("DYNAMIC_TARGET_UNRESOLVED") && all.Contains("DYNAMIC_PARAMETER_UNDEFINED") && all.Contains("COMMAND_OUT_OF_PLACE"), "missing diagnostics:\n" + all);
                        Need(Rms(o.l, Row * 6, Row) == 0, "the valid TV command next to the broken ones did not play");
                    }
                });

                Check("An imported device at the same address keeps its meaning", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var d = new SourceDeviceData { id = "macros", ordinal = 1, kind = SourceDeviceKind.InstrumentMacros, instrumentId = f.instrument.model.id };
                        for (int m = 0; m < 8; m++) d.parameters.Add(new SourceParameterData { ordinal = m + 1, defaultValue = 0 });
                        f.song.tracks[0].sourceDevices.Add(d);
                        f.Define("11", DynamicScope.Track, Track("postVolume"), 0, 2);
                        f.Row(1, null, Fx("11", 0));
                        var o = Render(f.song, frames);
                        Need(o.diagnostics.Any(x => x.StartsWith("DYNAMIC_ID_SHADOWED")), "no shadow diagnostic");
                        Need(Rms(o.l, Row * 6, Row) > baseRms * .9, "the dynamic definition took over an imported device address");
                    }
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
            report.Insert(0, "TrackerDynamicParameterCheck passed=" + pass + " failed=" + fail + "\n");
            return report.ToString();
        }
    }
}
