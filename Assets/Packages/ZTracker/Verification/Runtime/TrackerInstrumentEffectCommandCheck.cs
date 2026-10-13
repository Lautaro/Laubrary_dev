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
    /// Rendered-audio checks for commands on an effect inside an instrument's own chain (T-0606). Two tracks play the
    /// same instrument, panned hard left and hard right so each channel is one track; the instrument's chain holds a
    /// Gain effect with a command id. Every track plays its own copy of the chain, so the command typed on one track
    /// must change that track's channel only, while the other stays bit-identical. No assets are written.
    /// </summary>
    public static class TrackerInstrumentEffectCommandCheck
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
                s.loop = SampleLoop.Forward; s.loopStartFrame = 0; s.loopEndFrame = clip.samples; s.fxChain = 0;
                var chain = new AudioEffectChainData();
                chain.nodes.Add(new AudioEffectNodeData { type = Laubrary.Zounds.ZoundEffectType.Gain, uid = "inst-gain", p = new[] { 1f } });
                instrument.model.fxChains.Add(chain);
                song = TrackerEngineCheck.FixtureSong(instrument, 1, lines);
                song.ticksPerLine = 4;
                // A second track playing the same instrument, before the master.
                song.tracks.Insert(1, new TrackData { id = "notes2", name = "Right", visibleNoteColumns = 1, visibleEffectColumns = 1 });
                song.patterns[0].tracks.Insert(1, new PatternTrack { trackId = "notes2" });
                song.tracks[0].name = "Left";
                song.tracks[0].postPan = -1; song.tracks[1].postPan = 1;
                Put(0, 0, TrackerEngineCheck.Note(60));
                Put(1, 0, TrackerEngineCheck.Note(60));
            }
            public void Put(int track, int line, NoteCell note = null, params CommandData[] fx)
            {
                var pt = song.patterns[0].tracks[track];
                var existing = pt.ReadLine(line);
                var l = new PatternLine { line = line, notes = new List<NoteCell>(existing.notes), effects = new List<EffectCell>(existing.effects) };
                if (note != null) { l.notes.RemoveAll(n => n.column == note.column); l.notes.Add(note); }
                for (int i = 0; i < fx.Length; i++) { l.effects.RemoveAll(e => e.column == i); l.effects.Add(new EffectCell { column = i, command = fx[i] }); }
                pt.WriteLine(l);
            }
            public DynamicParameterData Define(string id = "IG", float min = 0, float max = 4)
            {
                song.commandModelVersion = ZTrackerCommandRegistry.CurrentVersion;
                var p = new DynamicParameterData { uid = "uid-" + id, id = id, name = "Instrument gain", scope = DynamicScope.InstrumentEffect, target = new ParameterTarget { kind = ParameterKind.Device, instrumentId = instrument.model.id, deviceId = "inst-gain", index = 0, parameter = "1" }, min = min, max = max };
                song.dynamicParameters.Add(p);
                return p;
            }
            public void Dispose() { if (instrument != null) UnityEngine.Object.DestroyImmediate(instrument); }
        }

        static CommandData Fx(string id, int value) => new CommandData { present = true, valuePresent = true, identifier = id, value = value, scope = CommandScope.Track };

        struct Output { public float[] l, r; public bool compiled; public List<string> diagnostics; }

        static Output Render(SongData song, int frames, int block = 1024)
        {
            var o = new Output { l = new float[frames], r = new float[frames] };
            var prepared = TrackerPreparedSong.Prepare(song, Rate);
            o.diagnostics = new List<string>(prepared.diagnostics);
            var left = new NativeArray<float>(block, Allocator.Persistent);
            var right = new NativeArray<float>(block, Allocator.Persistent);
            try
            {
                using (var e = new TrackerOffline(prepared))
                {
                    e.SendCommand(TrackerCommand.Play());
                    for (int at = 0; at < frames; at += block)
                    {
                        int n = Math.Min(block, frames - at);
                        e.Render(left, right, n, n);
                        for (int i = 0; i < n; i++) { o.l[at + i] = left[i]; o.r[at + i] = right[i]; }
                        while (e.ReadEvent(out _)) { }
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
        static string Diff(float[] a, float[] b) { double m = 0; int at = -1, first = -1; for (int i = 0; i < a.Length; i++) { double d = Math.Abs(a[i] - b[i]); if (d > 0 && first < 0) first = i; if (d > m) { m = d; at = i; } } int last = -1; for (int i = a.Length - 1; i >= 0; i--) if (a[i] != b[i]) { last = i; break; } return "largest difference " + m.ToString("0.########") + " at sample " + at + ", first at " + first + ", last at " + last; }
        // The master sums the two hard-panned tracks and passes them through its mid/side width stage
        // (l = (l+r)/2 + (l-r)/2), which in float moves a channel by a unit or two in the last place of the
        // combined level whenever the other channel changes. Allowed per sample: 2^-22 of |l|+|r| (two units in the
        // last place); a real change of the other track's copy would be thousands of times larger.
        static string Rounding(float[] channel, float[] reference, float[] otherChannel)
        {
            for (int i = 0; i < channel.Length; i++)
            {
                double d = Math.Abs(channel[i] - reference[i]);
                double allowed = (Math.Abs(channel[i]) + Math.Abs(otherChannel[i])) / 4194304.0;
                if (d > allowed) return "sample " + i + " moved " + d.ToString("0.##########") + ", rounding allows " + allowed.ToString("0.##########");
            }
            return null;
        }
        static double MaxDiff(float[] a, float[] b) { double m = 0; for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i])); return m; }
        static double Peak(float[] a) { double m = 0; foreach (var v in a) m = Math.Max(m, Math.Abs(v)); return m; }
        static string Dyn(Output o) => string.Join("; ", o.diagnostics.Where(d => d.StartsWith("DYNAMIC")));

        public static string Execute()
        {
            var report = new StringBuilder(); int pass = 0, fail = 0;
            void Check(string name, Action run) { try { run(); pass++; report.AppendLine("PASS " + name); } catch (Exception ex) { fail++; report.AppendLine("FAIL " + name + ": " + ex.Message); } }
            var clip = AudioClip.Create("instrument-effect-check", 48000, 1, Rate, false);
            var raw = new float[48000]; for (int i = 0; i < raw.Length; i++) raw[i] = .25f * Mathf.Sin(2 * Mathf.PI * 480 * i / Rate); clip.SetData(raw, 0);
            int frames = Row * 8;
            Output baseline;
            using (var f = new Fixture(clip)) baseline = Render(f.song, frames);
            double leftRms = Rms(baseline.l, Row * 6, Row), rightRms = Rms(baseline.r, Row * 6, Row);
            try
            {
                Check("Fixture: each channel is exactly one track (the other track alone is exactly silent there)", () =>
                {
                    Need(leftRms > .01 && rightRms > .01, "baseline is silent: " + leftRms + " / " + rightRms);
                    using (var f = new Fixture(clip))
                    {
                        f.song.patterns[0].tracks[1].lines.Clear();
                        var o = Render(f.song, frames);
                        Need(Same(o.l, baseline.l), "removing the right track changed the left channel");
                        Need(Peak(o.r) == 0, "the left track leaks into the right channel: " + Peak(o.r));
                    }
                });

                Check("A defined but unused instrument effect command is bit-identical to none", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define();
                        var o = Render(f.song, frames);
                        Need(o.compiled, "render was not Burst-compiled");
                        Need(Dyn(o) == "", "diagnostics: " + Dyn(o));
                        Need(Same(o.l, baseline.l) && Same(o.r, baseline.r), "an unused definition changed the audio");
                    }
                });

                void OneTrack(int track)
                {
                    string side = track == 0 ? "left" : "right";
                    Check("IG FF typed on the " + side + " track raises only that track's copy of the instrument's Gain fourfold; the other copy is unchanged (float rounding of the master only)", () =>
                    {
                        var reference = baseline;
                        using (var f = new Fixture(clip))
                        {
                            f.Define();
                            f.Put(track, 4, null, Fx("IG", 0xFF));
                            var o = Render(f.song, frames);
                            Need(o.compiled, "render was not Burst-compiled");
                            Need(Dyn(o) == "", "diagnostics: " + Dyn(o));
                            var changed = track == 0 ? o.l : o.r; var other = track == 0 ? o.r : o.l;
                            var changedBase = track == 0 ? reference.l : reference.r; var otherBase = track == 0 ? reference.r : reference.l;
                            string why = Rounding(other, otherBase, changed); double moved = MaxDiff(other, otherBase);
                            Need(why == null, "the other track's copy changed: " + why);
                            double before = Rms(changed, Row * 2, Row) / Rms(changedBase, Row * 2, Row);
                            double after = Rms(changed, Row * 6, Row) / Rms(changedBase, Row * 6, Row);
                            Need(Math.Abs(before - 1) < 1e-6, "audio before the command changed (" + before + ")");
                            Need(Math.Abs(after - 4) < .05, "level after the command ×" + after.ToString("0.000") + ", expected ×4");
                            double steady = MaxJump(changed, Row * 5, Row), transition = MaxJump(changed, Row * 4 - 64, 2048);
                            Need(transition <= steady * 1.02 + 1e-6, "click at the change: " + transition.ToString("0.00000") + " vs steady " + steady.ToString("0.00000"));
                            report.AppendLine("MEASURE " + side + ": commanded track ×" + after.ToString("0.000") + ", other channel moved at most " + moved.ToString("0.##########") + " (float rounding of the master's mid/side stage), transition step " + transition.ToString("0.0000") + " vs steady " + steady.ToString("0.0000"));
                        }
                    });
                }
                OneTrack(0);
                OneTrack(1);

                Check("Control: the left track's own post gain ×4 (a per-track mixer value) moves the right channel by the same master rounding", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.song.tracks[0].postVolume = 4;
                        var o = Render(f.song, frames);
                        double moved = MaxDiff(o.r, baseline.r);
                        Need(Rounding(o.r, baseline.r, o.l) == null, "control moved more than rounding");
                        report.AppendLine("MEASURE control: right channel moved at most " + moved.ToString("0.##########") + " when only the left track's post gain changed");
                    }
                });

                Check("Different values on the two tracks: each copy follows its own track's command", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define();
                        f.Put(0, 4, null, Fx("IG", 0x00));
                        f.Put(1, 4, null, Fx("IG", 0x80));
                        var o = Render(f.song, frames);
                        double l = Rms(o.l, Row * 6, Row) / leftRms, r = Rms(o.r, Row * 6, Row) / rightRms;
                        Need(l < 1e-4, "left copy not silenced: ×" + l.ToString("0.0000"));
                        Need(Math.Abs(r - 4 * 128 / 255.0) < .03, "right copy ×" + r.ToString("0.000") + ", expected ×" + (4 * 128 / 255.0).ToString("0.000"));
                        report.AppendLine("MEASURE both: left ×" + l.ToString("0.0000") + ", right ×" + r.ToString("0.000"));
                    }
                });

                Check("An envelope lane on one track drives only that track's copy (the other channel moves by float rounding of the master only)", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define();
                        var lane = new AutomationLane { id = ZTrackerEnvelopes.LanePrefix + "check", interpolation = AutomationInterpolation.Linear, target = new ParameterTarget { kind = ParameterKind.Dynamic, parameter = p.uid }, points = new List<AutomationPoint> { new AutomationPoint { line = 4, value = 0 }, new AutomationPoint { line = 6, value = 0 } } };
                        f.song.patterns[0].tracks[1].automation.Add(lane);
                        var o = Render(f.song, frames);
                        Need(Dyn(o) == "", "diagnostics: " + Dyn(o));
                        var why = Rounding(o.l, baseline.l, o.r); Need(why == null, "the left copy changed: " + why);
                        Need(Rms(o.r, Row * 5, Row) / rightRms < 1e-3, "right copy not driven to zero by the lane");
                    }
                });

                Check("Typed on a track without its own copy (the master) it is reported and does nothing", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define();
                        f.Put(2, 4, null, Fx("IG", 0xFF));
                        var o = Render(f.song, frames);
                        Need(o.diagnostics.Any(d => d.StartsWith("DYNAMIC_TARGET_NOT_ON_TRACK")), "not reported: " + string.Join("; ", o.diagnostics));
                        Need(Same(o.l, baseline.l) && Same(o.r, baseline.r), "the audio changed");
                    }
                });

                Check("A removed effect or a delay's memory size is reported; neighbours still play", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        var p = f.Define();
                        p.target.deviceId = "gone";
                        f.Define("TV").scope = DynamicScope.Track;
                        f.song.dynamicParameters[1].target = new ParameterTarget { kind = ParameterKind.Mixer, trackId = "notes2", parameter = "postVolume" };
                        f.song.dynamicParameters[1].min = 0; f.song.dynamicParameters[1].max = 2;
                        f.Put(1, 4, null, Fx("TV", 0));
                        var o = Render(f.song, frames);
                        Need(o.diagnostics.Any(d => d.StartsWith("DYNAMIC_TARGET_UNRESOLVED") && d.Contains("id=IG")), "removed effect not reported: " + Dyn(o));
                        Need(Rms(o.r, Row * 6, Row) / rightRms < 1e-4, "neighbouring command did not play");
                    }
                });

                Check("Stop and Play restore the authored value on every copy", () =>
                {
                    using (var f = new Fixture(clip))
                    {
                        f.Define();
                        f.Put(0, 4, null, Fx("IG", 0));
                        var prepared = TrackerPreparedSong.Prepare(f.song, Rate);
                        var l = new NativeArray<float>(Row, Allocator.Persistent); var r = new NativeArray<float>(Row, Allocator.Persistent);
                        try
                        {
                            using (var e = new TrackerOffline(prepared))
                            {
                                e.SendCommand(TrackerCommand.Play());
                                for (int i = 0; i < 6; i++) e.Render(l, r, Row, 1024);
                                e.SendCommand(TrackerCommand.Stop());
                                e.SendCommand(TrackerCommand.Play());
                                e.Render(l, r, Row, 1024);
                                var first = l.ToArray();
                                var expected = new float[Row]; Array.Copy(baseline.l, expected, Row);
                                Need(Same(first, expected), "row 0 after Stop/Play differs from a fresh start");
                            }
                        }
                        finally { l.Dispose(); r.Dispose(); }
                    }
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
            report.AppendLine("TOTAL passed=" + pass + " failed=" + fail);
            return report.ToString();
        }
    }
}
