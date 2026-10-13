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
    // T-0647: does the many-notes clipping of T-0635 (FM) also happen with the subtractive synth, samplers and drum kits,
    // and does the output limiter (and the level meter's pre-limiter peak) cover every instrument type and path? Each
    // case is rendered twice from the same input: once as summed (the limiter bypassed for measurement only) and once as
    // heard. Disposable in-memory instruments; reflection entry point.
    public sealed partial class ZTrackerModelWindow
    {
        struct LevelRun { public float[] x; public float meter, deepest; public int limitEvents; }

        /// Renders <paramref name="frames"/> samples (louder channel) of live events on the clean preview channel (12 note
        /// columns); with <paramref name="bypass"/> the limiter only records the peak. The meter's pre-limiter peak and
        /// the deepest limiting are recorded per block.
        static LevelRun LevelLive(ZTrackerInstrument asset, int frames, bool bypass, Action<TrackerLiveTarget, int> events)
        {
            var run = new LevelRun { x = new float[frames] };
            var snap = PlaybackSnapshot.CreateClean(asset, 12);
            var l = new NativeArray<float>(256, Allocator.Persistent); var r = new NativeArray<float>(256, Allocator.Persistent);
            try
            {
                var prepared = TrackerPreparedSong.Prepare(snap.Data, SlideRate); prepared.state.limiterBypass = bypass;
                using (var e = new TrackerOffline(prepared))
                {
                    bool limiting = false;
                    for (int at = 0; at < frames; at += 256)
                    {
                        events(e.LiveInput, at);
                        int n = Math.Min(256, frames - at); e.Render(l, r, n, n);
                        for (int i = 0; i < n; i++) run.x[at + i] = Math.Abs(l[i]) > Math.Abs(r[i]) ? l[i] : r[i];
                        run.meter = Math.Max(run.meter, TakeOfflinePeak(e)); float reduction = e.Snapshot.limiterReduction;
                        run.deepest = Math.Max(run.deepest, reduction); if (reduction > 0 && !limiting) run.limitEvents++; limiting = reduction > 0;
                    }
                }
            }
            finally { l.Dispose(); r.Dispose(); snap.Dispose(); }
            return run;
        }

        static LevelRun LevelSong(SongData song, int frames, bool bypass)
        {
            var run = new LevelRun { x = new float[frames] };
            var l = new NativeArray<float>(256, Allocator.Persistent); var r = new NativeArray<float>(256, Allocator.Persistent);
            try
            {
                var prepared = TrackerPreparedSong.Prepare(song, SlideRate); prepared.state.limiterBypass = bypass;
                using (var e = new TrackerOffline(prepared))
                {
                    e.SendCommand(TrackerCommand.Play());
                    for (int at = 0; at < frames; at += 256)
                    {
                        int n = Math.Min(256, frames - at); e.Render(l, r, n, n);
                        for (int i = 0; i < n; i++) run.x[at + i] = Math.Abs(l[i]) > Math.Abs(r[i]) ? l[i] : r[i];
                        run.meter = Math.Max(run.meter, TakeOfflinePeak(e)); run.deepest = Math.Max(run.deepest, e.Snapshot.limiterReduction);
                    }
                }
            }
            finally { l.Dispose(); r.Dispose(); }
            return run;
        }

        static AudioClip LevelClip(string name, float peak, Func<int, float> wave, int frames = 48000)
        {
            var clip = AudioClip.Create(name, frames, 1, SlideRate, false); var raw = new float[frames];
            for (int i = 0; i < frames; i++) raw[i] = peak * wave(i);
            clip.SetData(raw, 0); return clip;
        }

        public static string CheckOutputLevelSynth() => OutputLevelTypes(0);
        public static string CheckOutputLevelFmSampler() => OutputLevelTypes(1);
        public static string CheckOutputLevelDrumsSong() => OutputLevelTypes(2);

        static string OutputLevelTypes(int group)
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            int index = 0; void Check(string title, Action action) { int g = index++ switch { 0 => 0, 1 => 1, 2 => 1, _ => 2 }; if (g != group) return; try { action(); passed++; report.AppendLine("PASS " + title); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); } }
            var made = new List<UnityEngine.Object>();
            T Keep<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }
            // Twelve keys pressed together at 0.085 s and released at 1 s, louder than the T-0635 velocity.
            Action<TrackerLiveTarget, int> Chord(int keys) => (live, at) =>
            {
                if (at == 4096) for (int k = 0; k < keys; k++) live.Write(TrackerLiveEvent.NoteOn(0, 0, 0, LevelChord[k], 127, 1f, TrackerLiveSource.Computer));
                if (at == 47872) for (int k = 0; k < keys; k++) live.Write(TrackerLiveEvent.NoteOff(LevelChord[k], TrackerLiveSource.Computer));
            };
            // Measures one case as summed and as heard; returns whether the sum went past full scale.
            bool Measure(string label, Func<bool, LevelRun> render)
            {
                var raw = render(true); var heard = render(false);
                float mix = raw.x.Max(v => Math.Abs(v)), output = heard.x.Max(v => Math.Abs(v));
                int over = raw.x.Count(v => Math.Abs(v) > 1f), overHeard = heard.x.Count(v => Math.Abs(v) > 1f);
                // The limiter's gain per sample where the mix is audible; its largest step between samples while it acts.
                double largestGainStep = 0, prev = 1;
                for (int i = 0; i < raw.x.Length; i++) { if (Math.Abs(raw.x[i]) < 1e-3f) continue; double g = heard.x[i] / raw.x[i]; if (g < 1 - 1e-6 || prev < 1 - 1e-6) largestGainStep = Math.Max(largestGainStep, Math.Abs(g - prev)); prev = g; }
                bool identical = mix <= 1f && raw.x.SequenceEqual(heard.x);
                report.AppendLine("MEASURE " + label + ": sum peak " + mix.ToString("0.000") + " (" + (20 * Math.Log10(Math.Max(1e-9, mix))).ToString("+0.0;-0.0") + " dB, headroom " + (-20 * Math.Log10(Math.Max(1e-9, mix))).ToString("+0.0;-0.0") + " dB), samples over full scale " + over
                    + "; heard peak " + output.ToString("0.0000") + ", over " + overHeard + ", deepest limiting " + (20 * Math.Log10(1 - heard.deepest)).ToString("0.0") + " dB, limiter engaged " + heard.limitEvents + "x, largest gain step " + largestGainStep.ToString("0.000")
                    + "; meter " + heard.meter.ToString("0.000") + (mix <= 1f ? ", heard = summed: " + identical : ""));
                Need(overHeard == 0 && output <= 1f, label + ": heard output above full scale (" + output + ")");
                Need(Math.Abs(heard.meter - mix) < 1e-4f, label + ": meter " + heard.meter + " does not read the summed peak " + mix);
                if (mix <= 1f) Need(identical, label + ": the limiter changed a mix that never reached full scale");
                return mix > 1f;
            }
            ZTrackerInstrument Synth(int blendMode, int unison, int waveA = 2, int waveB = 2)
            {
                var a = Keep(SlideInstrument(SlideSource.Synth)); var q = a.model.parameters;
                q.waveA = waveA; q.waveB = waveB; q.blend = .5f; q.blendMode = blendMode; q.unisonVoices = unison; q.unisonDetune = 15; q.unisonSpread = .8f; q.volume = 1; q.sustain = 1; q.release = .2f;
                return a;
            }
            ZTrackerInstrument Sampler(float peak, int layers)
            {
                var clip = Keep(LevelClip("level-saw", peak, i => 2f * ((i * 220f / SlideRate) % 1f) - 1f));
                var a = Keep(TrackerEngineCheck.FixtureInstrument(clip)); a.model.provenance = "";
                var s = a.model.sampler.samples[0]; s.loop = SampleLoop.Forward; s.loopStartFrame = 0; s.loopEndFrame = clip.samples;
                a.model.sampler.nna = NewNoteAction.NoteOff;
                // Extra zones over the same keys play as layers (each a copy of the first).
                var first = a.model.sampler.zones[0];
                for (int k = 1; k < layers; k++) a.model.sampler.zones.Add(JsonUtility.FromJson<Keyzone>(JsonUtility.ToJson(first)));
                for (int k = 1; k < layers; k++) a.model.sampler.zones[k].id = "layer" + k;
                return a;
            }
            ZTrackerInstrument Kit(int pads, float peak)
            {
                var kit = Keep(SlideInstrument(SlideSource.Synth)); kit.model.family = InstrumentFamily.DrumKit;
                for (int p = 0; p < pads; p++)
                {
                    float hz = 50 + 37 * p; int seed = p;
                    // A drum-like hit: a pitched thump with a noisy click, decaying over 0.3 s, peaking at the given level.
                    var clip = Keep(LevelClip("pad" + p, peak, i => { float t = i / (float)SlideRate; uint h = (uint)(i * 2654435761u + seed * 97u); float noise = (h & 0xffff) / 32768f - 1; return Mathf.Exp(-t * 12) * (.7f * Mathf.Sin(2 * Mathf.PI * hz * t) + .3f * noise * Mathf.Exp(-t * 60)); }, 24000));
                    kit.model.drumKit.mappings.Add(new DrumMapping { id = "pad" + p, noteMin = 36 + p, noteMax = 36 + p, baseNote = 36 + p, pcm = clip, tag = "Pad " + p });
                }
                return kit;
            }
            try
            {
                Check("subtractive synth, 12 keys at full velocity: Mix, Ring, Sync and PM, and unison 8 (saw waves, Volume 100%)", () =>
                {
                    foreach (var (mode, name) in new[] { (0, "Mix"), (1, "Ring"), (2, "Sync"), (3, "PM") })
                        Measure("synth " + name, bypass => LevelLive(Synth(mode, 1), 72000, bypass, Chord(12)));
                    Measure("synth Mix unison 8", bypass => LevelLive(Synth(0, 8), 72000, bypass, Chord(12)));
                    Measure("synth square Mix 4 keys", bypass => LevelLive(Synth(0, 1, 1, 1), 72000, bypass, Chord(4)));
                });
                Check("FM, 12 keys: default levels and the loud setup of T-0635", () =>
                {
                    Measure("FM default", bypass => LevelLive(Keep(LevelFm(TrackerFmSetups.Base + 4, .5f, .5f)), 72000, bypass, Chord(12)));
                    Measure("FM loud", bypass => LevelLive(Keep(LevelFm(0, .5f, 1f)), 72000, bypass, Chord(12)));
                });
                Check("sampler: a loud looped saw (peak 0.95) with 1 and 3 layers, and a quiet one (peak 0.1), 12 keys", () =>
                {
                    Measure("sampler loud 1 layer", bypass => LevelLive(Sampler(.95f, 1), 72000, bypass, Chord(12)));
                    Measure("sampler loud 3 layers", bypass => LevelLive(Sampler(.95f, 3), 72000, bypass, Chord(12)));
                    Measure("sampler quiet", bypass => LevelLive(Sampler(.1f, 1), 72000, bypass, Chord(4)));
                });
                Check("drum kit: 12 pads hit at once (peak 0.9 each), and a 30 ms roll on one pad", () =>
                {
                    var kit = Kit(12, .9f);
                    Measure("drum kit 12 pads together", bypass => LevelLive(kit, 48000, bypass, (live, at) => { if (at == 4096) for (int p = 0; p < 12; p++) live.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 36 + p, 127, 1f, TrackerLiveSource.Computer)); if (at == 8192) for (int p = 0; p < 12; p++) live.Write(TrackerLiveEvent.NoteOff(36 + p, TrackerLiveSource.Computer)); }));
                    Measure("drum kit roll", bypass => LevelLive(kit, 48000, bypass, (live, at) => { if (at >= 4096 && at < 36864 && at % 1536 == 0) { live.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 36, 127, 1f, TrackerLiveSource.Computer)); live.Write(TrackerLiveEvent.NoteOff(36, TrackerLiveSource.Computer)); } }));
                });
                Check("pattern notes in a song (12 columns, through the track and the master): the same limiter and meter", () =>
                {
                    SongData Song() { var s = TrackerEngineCheck.FixtureSong(Synth(0, 1), 12, 16); s.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = LevelChord.Select((n, c) => TrackerEngineCheck.Note(n, c)).ToList() }); return s; }
                    Measure("song 12-column chord, synth", bypass => LevelSong(Song(), 72000, bypass));
                });
            }
            finally { foreach (var o in made) if (o != null) DestroyImmediate(o); }
            return "CheckOutputLevel types group " + group + " passed=" + passed + " failed=" + failed + "\n" + report;
        }
    }
}
