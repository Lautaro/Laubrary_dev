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
    // Rendered-audio measurements for the output level and limiter (T-0635), the FM routing with an operator at Level 0
    // (T-0636) and the ADSR value mode (T-0639). Disposable in-memory instruments only; reflection entry points.
    public sealed partial class ZTrackerModelWindow
    {
        static readonly int[] LevelChord = { 48, 52, 55, 60, 64, 67, 72, 76, 79, 84, 88, 91 };

        static ZTrackerInstrument LevelFm(int algorithm, float volume, float level)
        {
            var a = CreateInstance<ZTrackerInstrument>(); a.schemaVersion = 1; a.model = NewInstrumentData();
            a.model.synthMode = SynthMode.FM; var q = a.model.parameters; q.type = InstrumentType.FM; q.fmAlgorithm = algorithm; q.volume = volume;
            for (int op = 0; op < 4; op++) { var o = q.fmOperators[op]; o.level = level; q.fmOperators[op] = o; }
            return a;
        }

        /// Plays <paramref name="keys"/> live keys of a chord on the clean preview song (12 note columns), releases them,
        /// and returns the louder channel per sample, with the renderer's peak record and limiter state after each block.
        static float[] LevelChordRender(ZTrackerInstrument asset, int keys, int frames, Action<TrackerOffline, int> block = null)
        {
            var output = new float[frames];
            var snap = PlaybackSnapshot.CreateClean(asset, 12);
            var l = new NativeArray<float>(256, Allocator.Persistent); var r = new NativeArray<float>(256, Allocator.Persistent);
            try
            {
                using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(snap.Data, SlideRate)))
                {
                    for (int at = 0; at < frames; at += 256)
                    {
                        if (at == 4096) for (int k = 0; k < keys; k++) e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, LevelChord[k], 110, 110 / 127f, TrackerLiveSource.Computer));
                        if (at == 47872) for (int k = 0; k < keys; k++) e.LiveInput.Write(TrackerLiveEvent.NoteOff(LevelChord[k], TrackerLiveSource.Computer));
                        int n = Math.Min(256, frames - at); e.Render(l, r, n, n);
                        for (int i = 0; i < n; i++) output[at + i] = Math.Abs(l[i]) > Math.Abs(r[i]) ? l[i] : r[i];
                        block?.Invoke(e, at);
                    }
                }
            }
            finally { l.Dispose(); r.Dispose(); snap.Dispose(); }
            return output;
        }

        static float TakeOfflinePeak(TrackerOffline e)
        {
            var ticket = e.Snapshot.ticket; long bits = ticket[TrackerRealtime.PeakSlot]; ticket[TrackerRealtime.PeakSlot] = 0;
            return BitConverter.Int32BitsToSingle((int)bits);
        }

        public static string CheckOutputLevel()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Check(string title, Action action) { try { action(); passed++; report.AppendLine("PASS " + title); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); } }
            var made = new List<UnityEngine.Object>();
            ZTrackerInstrument Make(int algorithm, float volume, float level) { var a = LevelFm(algorithm, volume, level); made.Add(a); return a; }
            try
            {
                Check("many loud FM notes: the mix goes past full scale, the output never does, and the meter reads the mix", () =>
                {
                    foreach (int keys in new[] { 12 })
                    {
                        float before = 0; float limitedMost = 0;
                        var x = LevelChordRender(Make(0, 1f, 1f), keys, 96000, (e, at) => { before = Math.Max(before, TakeOfflinePeak(e)); limitedMost = Math.Max(limitedMost, e.Snapshot.limiterReduction); });
                        float after = x.Max(v => Math.Abs(v)); int over = x.Count(v => Math.Abs(v) > 1f);
                        report.AppendLine("MEASURE " + keys + " keys (Stack, volume 100%, levels 100%): mix peak " + before.ToString("0.000") + " (" + (20 * Math.Log10(before)).ToString("+0.0") + " dB), output peak " + after.ToString("0.0000") + ", samples above full scale " + over + ", deepest limiting " + (20 * Math.Log10(1 - limitedMost)).ToString("0.0") + " dB");
                        Need(before > 1f, keys + " keys did not exceed full scale before the limiter (" + before + ")");
                        Need(over == 0 && after <= 1f, keys + " keys: output above full scale (" + after + ")");
                    }
                });
                Check("below full scale the limiter never acts: the output peak equals the mix peak and the gain stays exactly 1", () =>
                {
                    float before = 0; bool acted = false;
                    var x = LevelChordRender(Make(0, .25f, .5f), 8, 96000, (e, at) => { before = Math.Max(before, TakeOfflinePeak(e)); acted |= e.Snapshot.limiterReduction != 0; });
                    float after = x.Max(v => Math.Abs(v));
                    report.AppendLine("MEASURE 8 keys at the default levels: mix peak " + before.ToString("0.0000") + ", output peak " + after.ToString("0.0000"));
                    Need(before < 1f && !acted && after == before, "Limiter acted below full scale: mix " + before + " output " + after);
                });
                Check("after a loud chord the gain returns to exactly 1 (recovery ~" + (TrackerRealtime.LimiterReleaseSeconds * 1000) + " ms)", () =>
                {
                    float last = 1; LevelChordRender(Make(104, 1f, 1f), 12, 192000, (e, at) => last = e.Snapshot.limiterReduction);
                    Need(last == 0, "Still limiting " + last + " three seconds after the release");
                });
                Check("the peak record is cleared by the reader and refills from the next block", () =>
                {
                    float first = -1, second = -1; int blocks = 0;
                    LevelChordRender(Make(0, .25f, .5f), 4, 12288, (e, at) => { if (at == 8192) { first = TakeOfflinePeak(e); second = TakeOfflinePeak(e); } blocks++; });
                    Need(first > .01f && second == 0, "Peak record " + first + " then " + second);
                });
            }
            finally { foreach (var o in made) DestroyImmediate(o); }
            return "CheckOutputLevel passed=" + passed + " failed=" + failed + "\n" + report;
        }

        // T-0636: each routing with each operator at Level 0, against FM theory: a carrier at 0 silences its branch; a
        // modulator at 0 leaves the operators it fed unbent (a plain sine when nothing else bends them).
        public static string CheckFmRouting()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Check(string title, Action action) { try { action(); passed++; report.AppendLine("PASS " + title); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); } }
            var made = new List<UnityEngine.Object>();
            (double rms, double sine) Measure(int algorithm, int zero)
            {
                var a = CreateInstance<ZTrackerInstrument>(); made.Add(a); a.schemaVersion = 1; a.model = NewInstrumentData(); a.model.synthMode = SynthMode.FM;
                var q = a.model.parameters; q.type = InstrumentType.FM; q.fmAlgorithm = algorithm; q.fmFeedback = 0; q.volume = .5f; q.attack = q.decay = 0; q.sustain = 1; q.pan = 0;
                for (int op = 0; op < 4; op++) q.fmOperators[op] = new ZTrackerInstrument.FMOperatorData { freqRatio = 1, level = op == zero ? 0 : .8f, attack = 0, decay = 0, sustain = 1, release = 1 };
                var x = new float[8192];
                using (var l = new NativeArray<float>(256, Allocator.TempJob)) using (var r = new NativeArray<float>(256, Allocator.TempJob))
                using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(TrackerEngineCheck.FixtureSong(a), 48000)))
                {
                    e.SendCommand(TrackerCommand.Audition(0, 69));
                    for (int at = 0; at < x.Length; at += 256) { e.Render(l, r, 256, 256); for (int i = 0; i < 256; i++) x[at + i] = l[i]; }
                }
                int from = 2048, n = x.Length - from; double sum = 0; for (int i = from; i < x.Length; i++) sum += x[i] * x[i];
                double h1 = 0, all = 0;
                for (int k = 1; k <= 24; k++)
                {
                    double w = 2 * Math.PI * 440d * k / 48000, re = 0, im = 0;
                    for (int i = 0; i < n; i++) { double win = .5 - .5 * Math.Cos(2 * Math.PI * i / (n - 1)); re += x[from + i] * win * Math.Cos(w * i); im -= x[from + i] * win * Math.Sin(w * i); }
                    double m = re * re + im * im; all += m; if (k == 1) h1 = m;
                }
                return (Math.Sqrt(sum / n), all > 0 ? h1 / all : 0);
            }
            try
            {
                var algorithms = new List<(int algorithm, int[] edges, int[] carriers, string name)>();
                for (int a = 0; a < 6; a++) algorithms.Add((a, FmEdges[a], FmCarriers[a], ClassicFmNames[a]));
                for (int s = 1; s <= TrackerFmSetups.Count; s++) algorithms.Add((TrackerFmSetups.Base + s, TrackerFmSetups.Edges(s), new[] { TrackerFmSetups.Carrier }, "setup " + s + " " + TrackerFmSetups.Formula(s)));
                foreach (var (algorithm, edges, carriers, label) in algorithms)
                {
                    Check(label + ": every operator at Level 0 behaves as FM theory says", () =>
                    {
                        var line = new StringBuilder("MEASURE " + label + ":");
                        bool setup = algorithm > TrackerFmSetups.Base;
                        for (int zero = 0; zero < 4; zero++)
                        {
                            if (setup && !TrackerFmSetups.Uses(algorithm - TrackerFmSetups.Base, zero)) continue;
                            var (rms, sine) = Measure(algorithm, zero);
                            // Theory: the operators still sounding are the carriers other than the zeroed one; one of them is
                            // bent if a live (non-zeroed) modulator reaches it along the routing, directly or through others.
                            var live = carriers.Where(c => c != zero).ToArray();
                            bool Bent(int target, int depth) { if (depth > 4) return false; for (int e = 0; e < edges.Length; e += 2) if (edges[e + 1] == target && edges[e] != zero) return true; return false; }
                            bool silent = live.Length == 0, pure = !silent && !live.Any(c => Bent(c, 0));
                            string name = setup ? TrackerFmSetups.Letter(zero).ToString() : "Op " + (zero + 1);
                            line.Append(" " + name + "=0: " + (rms < 1e-6 ? "silent" : "rms " + rms.ToString("0.000") + " sine " + sine.ToString("P1")));
                            if (silent) Need(rms < 1e-6, name + " at 0 should silence " + label + " (only carrier) but rms is " + rms);
                            else if (pure) Need(rms > .01 && sine > .999, name + " at 0 should leave plain sines but sine share is " + sine.ToString("P2"));
                            else Need(rms > .01 && sine < .99, name + " at 0 should leave a modulated (bent) tone but sine share is " + sine.ToString("P2"));
                        }
                        report.AppendLine(line.ToString());
                    });
                }
            }
            finally { foreach (var o in made) DestroyImmediate(o); }
            return "CheckFmRouting passed=" + passed + " failed=" + failed + "\n" + report;
        }

        // T-0639: an envelope drawn as an ADSR: the generated shape, its sound, the classic release from the level reached
        // (also on the synth's own Blend/Pulse/Ratio/PM/Detune envelopes), and that a free envelope keeps its behaviour.
        public static ZUIEnvelopeData AdsrEnvelope(float start, float peak, float attack, float decay, float sustain, float release)
        {
            var env = new ZUIEnvelopeData(0, 1, 0, 1, false) { adsrStart = start, adsrAttack = attack, adsrDecay = decay, adsrSustain = sustain, adsrRelease = release, adsrSet = true };
            env.ShapeAdsr(peak); return env;
        }

        public static string CheckAdsrModes()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Check(string title, Action action) { try { action(); passed++; report.AppendLine("PASS " + title); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); } }
            var made = new List<UnityEngine.Object>();
            // A synth sine whose own amplifier is flat during the note and releases over 10 s (its slope is divided out),
            // so the measured level is the Volume parameter itself.
            ZTrackerInstrument Synth(ZUIEnvelopeData volume)
            {
                var a = SlideInstrument(SlideSource.Synth); made.Add(a); var p = a.model.parameters; p.release = 10f; p.volume = .5f;
                if (volume != null) p.SetParameterEnvelope("volume", volume);
                return a;
            }
            // Volume per 10 ms window: peak |x| / pan gain / the amplifier's release slope after note-off.
            double Level(float[] x, double seconds, double releasedAt)
            {
                int a = (int)(seconds * SlideRate), n = SlideRate / 100; float peak = 0; for (int i = a; i < a + n && i < x.Length; i++) peak = Math.Max(peak, Math.Abs(x[i]));
                double amp = seconds > releasedAt ? 1 - (seconds - releasedAt) / 10.0 : 1; return peak / Math.Cos(Math.PI / 4) / amp;
            }
            try
            {
                Check("the ADSR shape: start, attack to the peak, decay to the sustain share, sustain point, release back to start; an attack of 0 starts at the peak", () =>
                {
                    var e = AdsrEnvelope(.1f, .9f, .3f, .2f, .5f, .4f);
                    string pts = string.Join(" ", e.points.Select(p => "(" + p.time.ToString("0.###") + "," + p.value.ToString("0.###") + ")"));
                    report.AppendLine("MEASURE points " + pts + " sustain at " + e.sustainPosition + " length " + e.xMax);
                    Need(e.points.Count == 4 && Mathf.Approximately(e.points[0].value, .1f) && Mathf.Approximately(e.points[1].time, .3f) && Mathf.Approximately(e.points[1].value, .9f) && Mathf.Approximately(e.points[2].time, .5f) && Mathf.Approximately(e.points[2].value, .5f) && Mathf.Approximately(e.points[3].time, .9f) && Mathf.Approximately(e.points[3].value, .1f), "Points " + pts);
                    Need(e.sustainEnabled && Mathf.Approximately(e.sustainPosition, .5f) && !e.loopEnabled && e.adsr && e.enabled, "Sustain/flags");
                    var wall = AdsrEnvelope(0, 1, 0, 0, 1, 0);
                    Need(wall.points[0].time == 0 && wall.points[0].value == 1 && wall.points.Zip(wall.points.Skip(1), (p, q) => q.time > p.time).All(b => b), "Zero stages: " + string.Join(" ", wall.points.Select(p => p.time + ":" + p.value)));
                    var copy = JsonUtility.FromJson<ZUIEnvelopeData>(JsonUtility.ToJson(e));
                    Need(copy.adsr && copy.adsrSet && copy.adsrAttack == .3f && copy.adsrDecay == .2f && copy.adsrSustain == .5f && copy.adsrRelease == .4f && copy.adsrStart == .1f, "ADSR fields lost on a save/load round trip");
                });
                Check("Volume as an ADSR sounds its shape: rises over Attack, falls to Sustain, releases over Release", () =>
                {
                    var x = SlideRender(SlideSong(Synth(AdsrEnvelope(0, .5f, .2f, .2f, .4f, .3f)), 16, (0, 69, 128), (8, -1, 0)), SlideRate * 2);
                    double At(double t) => Level(x, t, 1.0);
                    report.AppendLine("MEASURE volume 0.1 s " + At(.095).ToString("0.000") + " 0.2 s " + At(.195).ToString("0.000") + " 0.3 s " + At(.295).ToString("0.000") + " held " + At(.8).ToString("0.000") + " +0.15 s " + At(1.145).ToString("0.000") + " +0.3 s " + At(1.31).ToString("0.000"));
                    Need(Math.Abs(At(.095) - .25) < .02 && Math.Abs(At(.195) - .5) < .02, "Attack " + At(.095) + " / " + At(.195));
                    Need(Math.Abs(At(.295) - .35) < .02 && Math.Abs(At(.8) - .2) < .01, "Decay/sustain " + At(.295) + " / " + At(.8));
                    Need(Math.Abs(At(1.145) - .1) < .02 && At(1.31) < .01, "Release " + At(1.145) + " / " + At(1.31));
                });
                Check("a note released during the attack releases from the level it reached, as the amplifier ADSR does; a free envelope plays on", () =>
                {
                    var adsr = SlideRender(SlideSong(Synth(AdsrEnvelope(0, .5f, .4f, .2f, .4f, .2f)), 16, (0, 69, 128), (1, -1, 0)), SlideRate);
                    var free = AdsrEnvelope(0, .5f, .4f, .2f, .4f, .2f); free.adsr = false;
                    var played = SlideRender(SlideSong(Synth(free), 16, (0, 69, 128), (1, -1, 0)), SlideRate);
                    double atOff = Level(adsr, .12, .125), adsrLater = Level(adsr, .225, .125), freeLater = Level(played, .225, .125);
                    report.AppendLine("MEASURE released at 0.125 s: level " + atOff.ToString("0.000") + "; 0.1 s later ADSR " + adsrLater.ToString("0.000") + " (falling), free envelope " + freeLater.ToString("0.000") + " (still rising)");
                    Need(Math.Abs(atOff - .156) < .02 && Math.Abs(adsrLater - atOff * .5) < .02 && Level(adsr, .33, .125) < .01, "ADSR release " + atOff + " -> " + adsrLater);
                    Need(freeLater > atOff + .05, "A free envelope (not an ADSR) no longer plays on through its shape: " + freeLater);
                });
                Check("the synth's own envelopes (Pulse width as an ADSR) also release from the level reached", () =>
                {
                    var a = SlideInstrument(SlideSource.Synth); made.Add(a); var p = a.model.parameters; p.waveA = 1; p.release = 10f; p.pulseWidth = .5f;
                    p.pulseWidthEnvelopeData = AdsrEnvelope(.1f, .9f, .4f, .2f, .5f, .2f);
                    var x = SlideRender(SlideSong(a, 16, (0, 69, 128), (1, -1, 0)), SlideRate);
                    var cycles = SlideCycles(x);
                    double Duty(double t) { var c = cycles.OrderBy(k => Math.Abs(k.t - t)).First(); return c.duty; }
                    double off = Duty(.125), later = Duty(.225), end = Duty(.4);
                    report.AppendLine("MEASURE pulse width ADSR 10% to 90%, released at 0.125 s: duty " + off.ToString("0.000") + ", 0.1 s later " + later.ToString("0.000") + ", at 0.4 s " + end.ToString("0.000"));
                    Need(Math.Abs(off - .35) < .04 && later < off - .05 && Math.Abs(end - .1) < .03, "Pulse ADSR release " + off + " -> " + later + " -> " + end);
                });
            }
            finally { foreach (var o in made) DestroyImmediate(o); }
            return "CheckAdsrModes passed=" + passed + " failed=" + failed + "\n" + report;
        }
    }
}
