using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    // Rendered-audio measurements of note behaviour: instrument glide timing (T-0627), legato velocity (T-0628) and
    // per-note parameter envelope loops (T-0629). Every claim is measured from Burst-rendered PCM; nothing is saved.
    public sealed partial class ZTrackerModelWindow
    {
        public const int SlideRate = 48000;

        public enum SlideSource { Synth, Fm, Sampler }

        public static ZTrackerInstrument SlideInstrument(SlideSource kind)
        {
            ZTrackerInstrument a;
            if (kind == SlideSource.Sampler)
            {
                // A looped 480 Hz sine whose base note is B4 (59 + 12 = 71 at 480 Hz is not exact; only pitch changes are measured).
                var clip = AudioClip.Create("slide-sine", SlideRate, 1, SlideRate, false); var raw = new float[SlideRate];
                for (int i = 0; i < raw.Length; i++) raw[i] = Mathf.Sin(2 * Mathf.PI * 480f * i / SlideRate);
                clip.SetData(raw, 0);
                a = TrackerEngineCheck.FixtureInstrument(clip); a.model.provenance = "";
                var s = a.model.sampler.samples[0]; s.loop = SampleLoop.Forward; s.loopStartFrame = 0; s.loopEndFrame = SlideRate;
                // One note at a time, as on the synths: the previous note stops when the next starts.
                s.nna = NewNoteAction.Cut; a.model.sampler.nna = NewNoteAction.Cut;
                foreach (var set in a.model.modulation) foreach (var device in set.devices) if (device.kind == ModulationDeviceKind.AHDSR) device.release = .005f;
            }
            else
            {
                a = CreateInstance<ZTrackerInstrument>(); a.schemaVersion = 1; a.model = NewInstrumentData();
                var q = a.model.parameters; a.model.synthMode = kind == SlideSource.Fm ? SynthMode.FM : SynthMode.Subtractive;
                q.type = kind == SlideSource.Fm ? InstrumentType.FM : InstrumentType.Synth; q.enumDomain = SoundEnumDomain.NativeDirect;
                q.waveA = q.waveB = 0; q.blend = 0; q.unisonVoices = 1; q.vibratoDepth = 0; q.volume = .5f;
                // FM: a single sine carrier (operator D of the Stack routing), the others silent, so the output is a pure sine.
                for (int n = 0; n < q.fmOperators.Length; n++) { var op = q.fmOperators[n]; op.attack = op.decay = 0; op.sustain = 1; op.level = n == 3 ? 1 : 0; op.freqRatio = 1; q.fmOperators[n] = op; }
                q.fmAlgorithm = 0;
            }
            var p = a.model.parameters; p.attack = 0; p.decay = 0; p.sustain = 1; p.release = .005f;
            return a;
        }

        /// A one-track song (120 BPM, 4 lines per beat: a row is 6000 samples) with the given notes (row, note, volume 0..128, off?).
        public static SongData SlideSong(ZTrackerInstrument instrument, int lines, params (int row, int note, int volume)[] notes)
        {
            var song = TrackerEngineCheck.FixtureSong(instrument, 1, lines);
            foreach (var n in notes)
            {
                var cell = n.note < 0 ? new NoteCell { column = 0, note = NoteKind.Off } : TrackerEngineCheck.Note(n.note, 0, n.volume);
                song.patterns[0].tracks[0].WriteLine(new PatternLine { line = n.row, notes = { cell } });
            }
            return song;
        }

        public static float[] SlideRender(SongData song, int frames, int block = 1024)
        {
            var output = new float[frames];
            var l = new NativeArray<float>(block, Allocator.Persistent); var r = new NativeArray<float>(block, Allocator.Persistent);
            try
            {
                using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song, SlideRate)))
                {
                    e.SendCommand(TrackerCommand.Play());
                    for (int at = 0; at < frames; at += block) { int n = Math.Min(block, frames - at); e.Render(l, r, n, n); for (int i = 0; i < n; i++) output[at + i] = l[i]; }
                }
            }
            finally { l.Dispose(); r.Dispose(); }
            return output;
        }

        /// Instantaneous pitch (MIDI note number, fractional) from successive upward zero crossings of a sine, with the time
        /// (seconds) at the middle of each period.
        public static List<(double t, double note)> SlidePitch(float[] x)
        {
            var cross = new List<double>();
            for (int i = 1; i < x.Length; i++) if (x[i - 1] < 0 && x[i] >= 0) cross.Add(i - 1 + x[i - 1] / (x[i - 1] - x[i]));
            var result = new List<(double, double)>();
            for (int i = 1; i < cross.Count; i++) { double f = SlideRate / (cross[i] - cross[i - 1]); result.Add(((cross[i] + cross[i - 1]) * .5 / SlideRate, 12 * Math.Log(f / 440, 2) + 69)); }
            return result;
        }

        /// Times (ms after <paramref name="start"/>) at which the pitch has covered 50, 63.2, 95 and 99% of its way from the
        /// level held before the start to the level held at the end of the render.
        public static double[] SlideTimes(List<(double t, double note)> p, double start, double settledFrom, double settledTo)
        {
            double before = p.Where(v => v.t > start - .4 && v.t < start - .02).Average(v => v.note);
            double after = p.Where(v => v.t > settledFrom && v.t < settledTo).Average(v => v.note);
            double distance = after - before;
            // The last period still short of the share, so one odd period where the old and new note meet cannot count.
            double At(double share) { int last = p.FindLastIndex(v => v.t > start && v.t < settledFrom && (v.note - before) / distance < share); return last < 0 || last + 1 >= p.Count ? 0 : (p[last + 1].t - start) * 1000; }
            return new[] { At(.5), At(.632), At(.95), At(.99), before, after };
        }

        /// A per-note parameter envelope: a ramp from <paramref name="from"/> to <paramref name="to"/> over
        /// <paramref name="seconds"/>, looped over its whole length (forward or ping-pong).
        public static ZUIEnvelopeData LoopRamp(float from, float to, float seconds, bool pingPong, float yMin, float yMax)
        {
            var e = new ZUIEnvelopeData(0, seconds, yMin, yMax, false) { requiresEndPoint = false, enabled = true };
            e.points.Add(new ZUIEnvelopePoint(0, from, 1)); e.points.Add(new ZUIEnvelopePoint(seconds, to, 1));
            e.loopEnabled = true; e.loopMode = pingPong ? 2 : 1; e.loopStart = 0; e.loopEnd = seconds;
            return e;
        }

        /// Per cycle of the rendered wave (between upward zero crossings): (time, peak, share of the cycle above zero).
        public static List<(double t, double peak, double duty)> SlideCycles(float[] x)
        {
            var cross = new List<double>();
            for (int i = 1; i < x.Length; i++) if (x[i - 1] < 0 && x[i] >= 0) cross.Add(i - 1 + x[i - 1] / (x[i - 1] - x[i]));
            var cycles = new List<(double, double, double)>();
            for (int c = 1; c < cross.Count; c++)
            {
                int a = (int)Math.Ceiling(cross[c - 1]), b = (int)Math.Floor(cross[c]); double peak = 0; int above = 0;
                for (int i = a; i <= b; i++) { peak = Math.Max(peak, Math.Abs(x[i])); if (x[i] > 0) above++; }
                cycles.Add(((cross[c] + cross[c - 1]) * .5 / SlideRate, peak, above / Math.Max(1.0, b - a + 1)));
            }
            return cycles;
        }

        /// Average per-cycle peak (the note's level) between two times.
        static double SlideLevel(List<(double t, double peak, double duty)> cycles, double from, double to) => cycles.Where(c => c.t > from && c.t < to).Average(c => c.peak);

        /// Times at which a measure crosses its middle on the way down (a forward loop starting again).
        static List<double> DownCrossings(List<(double t, double value)> series)
        {
            double low = series.Min(v => v.value), high = series.Max(v => v.value), middle = (low + high) * .5; var list = new List<double>();
            for (int i = 1; i < series.Count; i++) if (series[i - 1].value >= middle && series[i].value < middle) list.Add(series[i].t);
            return list;
        }

        /// T-0627 / T-0628: glide time, legato slide and legato velocity, measured from rendered audio.
        public static string CheckNoteSlides()
        {
            var report = new StringBuilder(); int pass = 0, fail = 0;
            void Check(string name, Action run) { try { run(); pass++; report.AppendLine("PASS " + name); } catch (Exception ex) { fail++; report.AppendLine("FAIL " + name + ": " + ex.Message); } }
            void Need(bool ok, string why) { if (!ok) throw new Exception(why); }
            ZTrackerInstrument Synth() => SlideInstrument(SlideSource.Synth);

            foreach (SlideSource kind in Enum.GetValues(typeof(SlideSource)))
                Check("Glide arrives in exactly the stated time, evenly in pitch (" + kind + ")", () =>
                {
                    var ins = SlideInstrument(kind);
                    try
                    {
                        var line = new StringBuilder();
                        foreach (float seconds in new[] { .05f, .1f, .5f })
                            foreach (int target in new[] { 84, 60 })
                            {
                                var q = ins.model.parameters; q.glideEnabled = true; q.glideSeconds = seconds;
                                var t = SlideTimes(SlidePitch(SlideRender(SlideSong(ins, 32, (0, 72, 128), (8, target, 128)), SlideRate * 2)), 1.0, 1.0 + seconds + .2, 1.0 + seconds + .45);
                                double ms = seconds * 1000; string at = kind + " " + ms + " ms to " + target + ": ";
                                Need(Math.Abs(t[5] - t[4] - (target - 72)) < .02, at + "ended " + (t[5] - t[4]).ToString("0.000") + " semitones away instead of " + (target - 72));
                                Need(Math.Abs(t[0] - ms * .5) <= 3, at + "half way at " + t[0].ToString("0.0") + " ms");
                                Need(Math.Abs(t[2] - ms * .95) <= 3, at + "95% at " + t[2].ToString("0.0") + " ms");
                                Need(t[3] <= ms + 3 && t[3] >= ms * .99 - 3, at + "99% at " + t[3].ToString("0.0") + " ms");
                                if (seconds == .1f) line.Append(" " + (target > 72 ? "up" : "down") + ": 50% " + t[0].ToString("0") + ", 63% " + t[1].ToString("0") + ", 95% " + t[2].ToString("0") + ", 99% " + t[3].ToString("0") + " ms");
                            }
                        report.AppendLine("MEASURE glide 100 ms " + kind + ":" + line);
                    }
                    finally { DestroyImmediate(ins); }
                });

            Check("Glide 0 ms plays exactly as no glide", () =>
            {
                var ins = Synth(); try { var q = ins.model.parameters; q.glideSeconds = 0;
                    q.glideEnabled = true; var glide = SlideRender(SlideSong(ins, 32, (0, 72, 128), (8, 84, 128)), SlideRate * 2);
                    q.glideEnabled = false; var plain = SlideRender(SlideSong(ins, 32, (0, 72, 128), (8, 84, 128)), SlideRate * 2);
                    int differ = 0; for (int i = 0; i < plain.Length; i++) if (plain[i] != glide[i]) differ++;
                    Need(differ == 0, differ + " samples differ from no glide"); } finally { DestroyImmediate(ins); }
            });

            Check("Legato slides an overlapping note's pitch over Glide ms and keeps its envelope running", () =>
            {
                var ins = Synth(); try { var q = ins.model.parameters; q.legatoEnabled = true; q.legatoSlides = true; q.glideSeconds = .1f; q.attack = 2;
                    var x = SlideRender(SlideSong(ins, 32, (0, 72, 128), (8, 84, 128)), SlideRate * 2);
                    var t = SlideTimes(SlidePitch(x), 1.0, 1.3, 1.5);
                    Need(Math.Abs(t[2] - 95) <= 3 && t[3] <= 103, "legato slide 95% at " + t[2].ToString("0.0") + " ms, 99% at " + t[3].ToString("0.0") + " ms");
                    var c = SlideCycles(x); double before = SlideLevel(c, .97, .995), after = SlideLevel(c, 1.005, 1.03);
                    Need(after > before * .98 && after < before * 1.1, "level restarted at the legato note: " + before.ToString("0.000") + " → " + after.ToString("0.000"));
                    report.AppendLine("MEASURE legato slide 100 ms: 50% " + t[0].ToString("0") + ", 95% " + t[2].ToString("0") + ", 99% " + t[3].ToString("0") + " ms; level across the note " + before.ToString("0.000") + " → " + after.ToString("0.000") + " (attack still rising)");
                } finally { DestroyImmediate(ins); }
            });

            Check("A Legato saved before legato slid still jumps (its sound is unchanged)", () =>
            {
                var ins = Synth(); try { var q = ins.model.parameters; q.legatoEnabled = true; q.legatoSlides = false; q.glideSeconds = .1f;
                    var t = SlideTimes(SlidePitch(SlideRender(SlideSong(ins, 32, (0, 72, 128), (8, 84, 128)), SlideRate * 2)), 1.0, 1.2, 1.4);
                    Need(t[3] <= 4, "older legato slid: 99% at " + t[3].ToString("0.0") + " ms"); } finally { DestroyImmediate(ins); }
            });

            Check("Both: a note during a slide slides on from the pitch reached, with no jump", () =>
            {
                var ins = Synth(); try { var q = ins.model.parameters; q.legatoEnabled = true; q.glideEnabled = true; q.legatoSlides = true; q.glideSeconds = .2f;
                    var p = SlidePitch(SlideRender(SlideSong(ins, 32, (0, 72, 128), (8, 84, 128), (9, 60, 128)), SlideRate * 2));
                    double worst = 0; for (int i = 1; i < p.Count; i++) if (p[i].t > .95 && p[i].t < 1.6) worst = Math.Max(worst, Math.Abs(p[i].note - p[i - 1].note));
                    double reached = p.Where(v => v.t > 1.115 && v.t < 1.125).Average(v => v.note), end = p.Where(v => v.t > 1.4 && v.t < 1.6).Average(v => v.note);
                    Need(worst < .5, "pitch jumped " + worst.ToString("0.00") + " semitones between periods");
                    Need(Math.Abs(end - 60) < .02, "ended at " + end.ToString("0.00"));
                    int arrive = p.FindLastIndex(v => v.t < 1.4 && Math.Abs(v.note - 60) > .12);
                    Need(arrive >= 0 && p[arrive].t - 1.125 <= .2 + .004, "second slide took " + (arrive < 0 ? "?" : ((p[arrive].t - 1.125) * 1000).ToString("0")) + " ms");
                    report.AppendLine("MEASURE both: pitch " + reached.ToString("0.00") + " when the third note came (slide to 84 under way), largest step between periods " + worst.ToString("0.000") + " semitones, at 60 after " + ((p[arrive].t - 1.125) * 1000).ToString("0") + " ms");
                } finally { DestroyImmediate(ins); }
            });

            // Legato velocity: one held note at full volume, then a legato note on the same pitch at volume 32 (a quarter).
            (double before, List<(double t, double peak, double duty)> cycles, TrackerVoice voice) Velocity(LegatoVelocity mode, float scale, float slide, bool command = false)
            {
                var ins = Synth(); try { var q = ins.model.parameters; q.legatoEnabled = true; q.legatoSlides = true; q.glideSeconds = 0; q.legatoVelocity = mode; q.legatoVelocityScale = scale; q.legatoVelocitySeconds = slide;
                    var song = SlideSong(ins, 32, (0, 72, 128), (8, 72, 32));
                    if (command) song.patterns[0].tracks[0].WriteLine(new PatternLine { line = 9, notes = { new NoteCell { column = 0, volume = new ColumnValue { kind = ValueKind.Value, value = 64 } } } });
                    var c = SlideCycles(SlideRender(song, SlideRate * 2));
                    TrackerVoice held;
                    var a = new NativeArray<float>(1024, Allocator.Persistent); var b = new NativeArray<float>(1024, Allocator.Persistent);
                    try { using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song, SlideRate))) { e.SendCommand(TrackerCommand.Play()); for (int i = 0; i < 70; i++) e.Render(a, b, 1024, 1024); held = e.Snapshot.voices.ToArray().First(v => v.active && !v.released); } }
                    finally { a.Dispose(); b.Dispose(); }
                    return (SlideLevel(c, .8, .99), c, held);
                } finally { DestroyImmediate(ins); }
            }
            Check("Legato velocity Follow at 100% jumps to the new velocity (as before)", () =>
            {
                var r = Velocity(LegatoVelocity.Follow, 1, .05f); double after = SlideLevel(r.cycles, 1.2, 1.4) / r.before, first = r.cycles.First(c => c.t > 1.003).peak / r.before;
                Need(Math.Abs(after - .25) < .005 && first < .27, "level ×" + after.ToString("0.000") + ", first cycle ×" + first.ToString("0.000"));
                Need(r.voice.velocity == 32, "velocity " + r.voice.velocity + " instead of 32");
                report.AppendLine("MEASURE legato velocity follow: ×" + after.ToString("0.000") + " at once (first cycle ×" + first.ToString("0.000") + "), velocity 32");
            });
            Check("Legato velocity Ignore keeps the held note's level and velocity", () =>
            {
                var r = Velocity(LegatoVelocity.Ignore, 1, .05f); double after = SlideLevel(r.cycles, 1.2, 1.4) / r.before;
                Need(Math.Abs(after - 1) < .005, "level ×" + after.ToString("0.000")); Need(r.voice.velocity == 127, "velocity " + r.voice.velocity);
                report.AppendLine("MEASURE legato velocity ignore: ×" + after.ToString("0.000") + ", velocity 127");
            });
            Check("Legato velocity Slide moves evenly to the new level over the slide time", () =>
            {
                var r = Velocity(LegatoVelocity.Slide, 1, .1f); double half = r.cycles.Where(c => c.t > 1.045 && c.t < 1.055).Average(c => c.peak) / r.before, end = SlideLevel(r.cycles, 1.11, 1.4) / r.before;
                Need(Math.Abs(half - .625) < .02, "half way ×" + half.ToString("0.000")); Need(Math.Abs(end - .25) < .005, "end ×" + end.ToString("0.000"));
                double prev = 2; foreach (var c in r.cycles.Where(c => c.t > 1.0 && c.t < 1.11)) { Need(c.peak / r.before <= prev + .01, "not falling evenly at " + c.t.ToString("0.000")); prev = c.peak / r.before; }
                Need(r.voice.velocity == 32, "velocity " + r.voice.velocity);
                report.AppendLine("MEASURE legato velocity slide 100 ms: ×" + half.ToString("0.000") + " after 50 ms, ×" + end.ToString("0.000") + " from 110 ms");
            });
            Check("Legato velocity scale applies its share of the change (Follow and Slide)", () =>
            {
                var f = Velocity(LegatoVelocity.Follow, .5f, .05f); double a = SlideLevel(f.cycles, 1.2, 1.4) / f.before;
                var s = Velocity(LegatoVelocity.Slide, .5f, .1f); double b = SlideLevel(s.cycles, 1.2, 1.4) / s.before;
                Need(Math.Abs(a - .625) < .005 && Math.Abs(b - .625) < .005, "follow ×" + a.ToString("0.000") + ", slide ×" + b.ToString("0.000"));
                Need(f.voice.velocity == 80 && s.voice.velocity == 80, "velocity " + f.voice.velocity + "/" + s.voice.velocity + " instead of 80");
                report.AppendLine("MEASURE legato velocity scale 50%: follow ×" + a.ToString("0.000") + ", slide ×" + b.ToString("0.000") + ", velocity 80");
            });
            Check("A volume command during a velocity slide takes over at once", () =>
            {
                var r = Velocity(LegatoVelocity.Slide, 1, .5f, command: true); double after = SlideLevel(r.cycles, 1.13, 1.4) / r.before;
                Need(Math.Abs(after - .5) < .01, "level ×" + after.ToString("0.000") + " instead of the command's 0.5");
            });
            Check("Instruments saved before these settings read Follow, 100%, 50 ms and a jumping legato", () =>
            {
                var json = JsonUtility.ToJson(new InstrumentParameters { legatoVelocity = LegatoVelocity.Slide, legatoVelocityScale = .25f, legatoVelocitySeconds = 1, legatoSlides = true });
                foreach (var key in new[] { "\"legatoVelocity\":2,", "\"legatoVelocitySeconds\":1.0,", "\"legatoVelocityScale\":0.25,", "\"legatoSlides\":true," }) { Need(json.Contains(key), "fixture JSON lacks " + key); json = json.Replace(key, ""); }
                var old = JsonUtility.FromJson<InstrumentParameters>(json);
                Need(old.legatoVelocity == LegatoVelocity.Follow && old.legatoVelocityScale == 1 && Math.Abs(old.legatoVelocitySeconds - .05f) < 1e-6 && !old.legatoSlides, "older JSON read " + old.legatoVelocity + " " + old.legatoVelocityScale + " " + old.legatoVelocitySeconds + " " + old.legatoSlides);
                int assets = 0;
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:ZTrackerInstrument"))
                {
                    var a = UnityEditor.AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)); if (a == null || a.schemaVersion != 1 || a.model?.parameters == null) continue;
                    var q = a.model.parameters; assets++;
                    Need(q.legatoVelocity == LegatoVelocity.Follow && q.legatoVelocityScale == 1 && Math.Abs(q.legatoVelocitySeconds - .05f) < 1e-6, "saved " + a.name + " read " + q.legatoVelocity + " " + q.legatoVelocityScale + " " + q.legatoVelocitySeconds);
                }
                report.AppendLine("MEASURE saved instruments read with the new defaults: " + assets);
            });
            report.AppendLine("TOTAL passed=" + pass + " failed=" + fail);
            return report.ToString();
        }

        /// T-0629: per-note parameter envelope loops in time, ping-pong, envelopes in rows, and one playhead per note.
        public static string CheckEnvelopeLoops()
        {
            var report = new StringBuilder(); int pass = 0, fail = 0;
            void Check(string name, Action run) { try { run(); pass++; report.AppendLine("PASS " + name); } catch (Exception ex) { fail++; report.AppendLine("FAIL " + name + ": " + ex.Message); } }
            void Need(bool ok, string why) { if (!ok) throw new Exception(why); }
            const float period = .2f; const int seconds = 20;
            List<double> Restarts(string parameter, ZUIEnvelopeData env, double bpm = 120)
            {
                var ins = SlideInstrument(SlideSource.Synth); var q = ins.model.parameters;
                try
                {
                    q.waveA = parameter == "pulseWidth" ? 1 : 0; q.SetParameterEnvelope(parameter, env);
                    var song = SlideSong(ins, 256, (0, 72, 128)); song.bpm = bpm;
                    var cycles = SlideCycles(SlideRender(song, SlideRate * seconds));
                    return DownCrossings(cycles.Select(c => (c.t, parameter == "volume" ? c.peak : c.duty)).ToList());
                }
                finally { DestroyImmediate(ins); }
            }
            string Timing(List<double> restarts, double every, double offset, out double worst)
            {
                worst = 0; for (int k = 0; k < restarts.Count; k++) worst = Math.Max(worst, Math.Abs(restarts[k] - (every * (k + 1) + offset)));
                return restarts.Count + " restarts, largest timing error " + (worst * 1000).ToString("0.00") + " ms";
            }
            Check("A looped volume envelope restarts on time for 20 seconds", () =>
            {
                var r = Restarts("volume", LoopRamp(.05f, 1, period, false, 0, 2)); string t = Timing(r, period, 0, out double worst);
                Need(r.Count == (int)(seconds / period) - 1 && worst < .003, t); report.AppendLine("MEASURE volume loop 200 ms: " + t);
            });
            Check("A looped synth envelope (pulse width) restarts on time for 20 seconds (it drifted by up to 0.7%)", () =>
            {
                var r = Restarts("pulseWidth", LoopRamp(.1f, .9f, period, false, .01f, .99f)); string t = Timing(r, period, .002, out double worst);
                Need(r.Count == (int)(seconds / period) - 1 && worst < .004, t); report.AppendLine("MEASURE pulse width loop 200 ms (5 ms smoothing): " + t);
            });
            Check("A ping-pong loop runs there and back", () =>
            {
                var ins = SlideInstrument(SlideSource.Synth); try
                {
                    ins.model.parameters.SetParameterEnvelope("volume", LoopRamp(.05f, 1, period, true, 0, 2));
                    var cycles = SlideCycles(SlideRender(SlideSong(ins, 256, (0, 72, 128)), SlideRate * 10));
                    double scale = cycles.Max(c => c.peak), worst = 0;
                    foreach (var c in cycles) { double phase = c.t % (2 * period); double expected = .05 + .95 * (phase < period ? phase / period : 2 - phase / period); worst = Math.Max(worst, Math.Abs(c.peak / scale - expected)); }
                    Need(worst < .03, "departs from the triangle by " + worst.ToString("0.000")); report.AppendLine("MEASURE ping-pong volume loop: largest departure from the triangle " + worst.ToString("0.000") + " over 10 s");
                } finally { DestroyImmediate(ins); }
            });
            Check("An envelope in rows follows the song's tempo", () =>
            {
                foreach (double bpm in new[] { 120.0, 150.0 })
                {
                    var env = LoopRamp(.05f, 1, 2, false, 0, 2); env.durationInRows = true;
                    double row = 60 / (bpm * 4); var r = Restarts("volume", env, bpm); string t = Timing(r, 2 * row, 0, out double worst);
                    Need(worst < .003 && r.Count >= (int)(seconds / (2 * row)) - 1, bpm + " BPM: " + t);
                    report.AppendLine("MEASURE 2-row loop at " + bpm + " BPM (" + (2 * row * 1000).ToString("0") + " ms): " + t);
                }
            });
            Check("An envelope in rows changes speed with a tempo command on a held note", () =>
            {
                var ins = SlideInstrument(SlideSource.Synth); try
                {
                    var env = LoopRamp(.05f, 1, 2, false, 0, 2); env.durationInRows = true; ins.model.parameters.SetParameterEnvelope("volume", env);
                    var song = SlideSong(ins, 64, (0, 72, 128));
                    // ZT 96: 150 BPM from row 16 (2 seconds at 120 BPM).
                    song.patterns[0].tracks[0].WriteLine(new PatternLine { line = 16, effects = { new EffectCell { column = 0, command = new CommandData { present = true, valuePresent = true, identifier = "ZT", value = 0x96, scope = CommandScope.Global } } } });
                    var r = DownCrossings(SlideCycles(SlideRender(song, SlideRate * 4)).Select(c => (c.t, c.peak)).ToList());
                    var early = r.Where(x => x < 2).ToList(); var late = r.Where(x => x > 2.05).ToList();
                    Need(early.Count > 2 && late.Count > 2, early.Count + " restarts before and " + late.Count + " after the tempo change");
                    double a = early.Zip(early.Skip(1), (x, y) => y - x).Average(), b = late.Zip(late.Skip(1), (x, y) => y - x).Average();
                    Need(Math.Abs(a - .25) < .002 && Math.Abs(b - .2) < .002, "loop every " + (a * 1000).ToString("0.0") + " ms, then " + (b * 1000).ToString("0.0") + " ms");
                    report.AppendLine("MEASURE 2-row loop: every " + (a * 1000).ToString("0.0") + " ms at 120 BPM, every " + (b * 1000).ToString("0.0") + " ms after ZT 96 (150 BPM)");
                } finally { DestroyImmediate(ins); }
            });
            Check("The renderer publishes one playhead per sounding note, released notes included, with no allocation", () =>
            {
                var ins = SlideInstrument(SlideSource.Synth); try
                {
                    var q = ins.model.parameters; q.unisonVoices = 3; q.release = .5f; ins.model.sampler.nna = NewNoteAction.Continue;
                    q.SetParameterEnvelope("volume", LoopRamp(.05f, 1, .3f, false, 0, 2)); q.SetParameterEnvelope("pulseWidth", LoopRamp(.1f, .9f, .4f, false, .01f, .99f));
                    var song = TrackerEngineCheck.FixtureSong(ins, 3, 32); song.voiceCapacity = 32;
                    for (int n = 0; n < 3; n++) song.patterns[0].tracks[0].WriteLine(new PatternLine { line = n * 2, notes = { TrackerEngineCheck.Note(60 + n, n) } });
                    var off = song.patterns[0].tracks[0].ReadLine(6); song.patterns[0].tracks[0].WriteLine(new PatternLine { line = 6, notes = { new NoteCell { column = 0, note = NoteKind.Off } } });
                    var pos = new float[16]; var rel = new bool[16];
                    var a = new NativeArray<float>(64, Allocator.Persistent); var b = new NativeArray<float>(64, Allocator.Persistent);
                    try
                    {
                        using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song, SlideRate)))
                        {
                            e.SendCommand(TrackerCommand.Play());
                            int frames = 0; long allocated = 0;
                            while (frames < SlideRate * .81) { long before = GC.GetAllocatedBytesForCurrentThread(); e.Render(a, b, 64, 64); allocated += GC.GetAllocatedBytesForCurrentThread() - before; frames += 64; }
                            Need(e.Compiled, "render ran as managed code"); Need(allocated == 0, allocated + " bytes allocated while rendering");
                            double now = frames / (double)SlideRate;
                            int count = e.ReadEnvelopeHeads(0, (int)TrackerParameter.Volume, -1, pos, rel);
                            Need(count == 3, count + " volume playheads for three notes of three unison voices each");
                            var got = pos.Take(count).OrderBy(x => x).ToArray(); var want = new[] { .75 % .3 + (now - .75), (now - .25) % .3, (now - .5) % .3 }.OrderBy(x => x).ToArray(); // the released note has left its loop
                            for (int i = 0; i < 3; i++) Need(Math.Abs(got[i] - want[i]) < .002, "playhead " + got[i].ToString("0.0000") + " instead of " + want[i].ToString("0.0000"));
                            Need(rel.Take(count).Count(x => x) == 1, "the released note is not marked released (" + string.Join(",", rel.Take(count)) + ")");
                            int pw = e.ReadEnvelopeHeads(0, (int)TrackerParameter.PulseWidth, -1, pos, rel);
                            Need(pw == 3, pw + " pulse width playheads"); var pwGot = pos.Take(pw).OrderBy(x => x).ToArray(); var pwWant = new[] { .75 % .4 + (now - .75), (now - .25) % .4, (now - .5) % .4 }.OrderBy(x => x).ToArray(); // the released note has left its loop
                            for (int i = 0; i < 3; i++) Need(Math.Abs(pwGot[i] - pwWant[i]) < .002, "pulse width playhead " + pwGot[i].ToString("0.0000") + " instead of " + pwWant[i].ToString("0.0000"));
                            Need(e.ReadEnvelopeHeads(0, (int)TrackerParameter.Pan, -1, pos, rel) == 0, "a parameter without an envelope has playheads");
                            report.AppendLine("MEASURE playheads at " + (now * 1000).ToString("0") + " ms: volume " + string.Join(", ", got.Select(x => (x * 1000).ToString("0.0"))) + " ms, pulse width " + string.Join(", ", pwGot.Select(x => (x * 1000).ToString("0.0"))) + " ms; rendering allocated " + allocated + " bytes");
                            while (frames < SlideRate * 1.5) { e.Render(a, b, 64, 64); frames += 64; }
                            Need(e.ReadEnvelopeHeads(0, (int)TrackerParameter.Volume, -1, pos, rel) == 2, "the released note's playhead did not go when its voice ended");
                        }
                    }
                    finally { a.Dispose(); b.Dispose(); }
                } finally { DestroyImmediate(ins); }
            });
            report.AppendLine("TOTAL passed=" + pass + " failed=" + fail);
            return report.ToString();
        }
    }
}
