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
    /// <summary>Deterministic game-control checks. No devices, windows, asset writes, forced collection or Test Runner.</summary>
    public static class TrackerGameControlCheck
    {
        static void Need(bool condition, string why) { if (!condition) throw new Exception(why); }
        static DynamicParameterData Definition(string uid, string id, DynamicScope scope, ParameterTarget target, float min, float max)
            => new DynamicParameterData { uid = uid, id = id, name = uid, scope = scope, target = target, min = min, max = max };
        static SongData Song(ZTrackerInstrument instrument)
        {
            var song = TrackerEngineCheck.FixtureSong(instrument, lines: 16);
            song.commandModelVersion = ZTrackerCommandRegistry.CurrentVersion;
            song.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = { TrackerEngineCheck.Note(60) } });
            return song;
        }
        static void TempoDefinition(SongData song)
            => song.dynamicParameters.Add(Definition("tempo", "BT", DynamicScope.Song, new ParameterTarget { kind = ParameterKind.Timing, parameter = "bpm" }, 60, 315));
        static void AddSecondTrack(SongData song)
        {
            song.tracks.Insert(1, new TrackData { id = "drums", visibleNoteColumns = 1 });
            song.patterns[0].tracks.Add(new PatternTrack { trackId = "drums" });
        }
        static TrackerVoice Voice(TrackerOffline engine, int track)
            => engine.Snapshot.voices.ToArray().Where(v => v.active && v.track == track).OrderByDescending(v => v.cohort).First();

        public static string Execute()
        {
            int pass = 0, fail = 0;
            var report = new StringBuilder();
            void Check(string name, Action action)
            {
                try { action(); pass++; report.AppendLine("PASS " + name); }
                catch (Exception ex) { fail++; report.AppendLine("FAIL " + name + ": " + ex.Message); }
            }
            var clip = AudioClip.Create("game-control-check", 48000, 1, 48000, false);
            var pcm = new float[48000];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = .1f * (float)Math.Sin(i * .025);
            clip.SetData(pcm, 0);
            var sample = TrackerEngineCheck.FixtureInstrument(clip);
            var synth = TrackerP4Check.Synth();
            using (var left = new NativeArray<float>(24000, Allocator.Persistent))
            using (var right = new NativeArray<float>(24000, Allocator.Persistent))
            try
            {
                Check("tempo waits for the next row, last queued wins, authored timing restores on clear at 64/333/1024 frames", () =>
                {
                    foreach (int block in new[] { 64, 333, 1024 })
                    {
                        var song = Song(sample); TempoDefinition(song);
                        song.patterns[0].tracks[0].WriteLine(new PatternLine { line = 1, effects = { new EffectCell { column = 0, command = new CommandData { present = true, valuePresent = true, identifier = "BT", value = 120, scope = CommandScope.Track } } } });
                        using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                        {
                            engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, 1000, block);
                            engine.SendCommand(TrackerCommand.SetTempo(200)); engine.SendCommand(TrackerCommand.SetTempo(240));
                            Need(engine.Snapshot.bpm == 120 && engine.Snapshot.rowDuration == 6000, "Current row changed");
                            engine.Render(left, right, 5100, block);
                            Need(engine.Snapshot.row == 1 && engine.Snapshot.bpm == 240 && engine.Snapshot.rowDuration == 3000, "Next row did not take the override");
                            engine.SendCommand(TrackerCommand.ClearTempoOverride()); engine.Render(left, right, 3000, block);
                            Need(engine.Snapshot.row == 2 && Math.Abs(engine.Snapshot.bpm - 180) < .0001 && Math.Abs(engine.Snapshot.rowDuration - 4000) < .001, "Clear did not restore authored 180 BPM");
                            var rows = new List<long>(); while (engine.ReadEvent(out var ev)) if (ev.kind == TrackerEventKind.Row) rows.Add(ev.samplePosition);
                            Need(rows.SequenceEqual(new long[] { 0, 6000, 9000 }), "Row timestamps drifted: " + string.Join(",", rows));
                            Need(engine.Compiled, "Renderer did not run in Burst");
                        }
                    }
                });
                Check("stable UID lookup validates inputs and queues dynamic tempo beneath the persistent override", () =>
                {
                    var song = Song(sample); TempoDefinition(song);
                    using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                    {
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, 100);
                        Need(!engine.SetCommandParameter("BT", .5f) && !engine.SetCommandParameter("missing", .5f) && !engine.SetCommandParameter("tempo", float.NaN) && !engine.SetCommandParameter("tempo", 1.1f), "Invalid dynamic address/value accepted");
                        Need(engine.SetCommandParameter("tempo", 180f / 255f), "Stable UID refused");
                        Need(engine.Snapshot.bpm == 120, "Dynamic tempo changed current row");
                        engine.SendCommand(TrackerCommand.SetTempo(300)); engine.Render(left, right, 6000);
                        Need(engine.Snapshot.bpm == 300, "Persistent override lost priority");
                        engine.SendCommand(TrackerCommand.ClearTempoOverride()); engine.Render(left, right, 2400);
                        Need(Math.Abs(engine.Snapshot.bpm - 240) < .0001, "Pending dynamic tempo was lost under override");
                    }
                });
                Check("stable effect UID reuses the ramp evaluator and reaches the rendered gain", () =>
                {
                    var song = Song(sample);
                    song.tracks[0].devices.nodes.Add(new AudioEffectNodeData { uid = "gain", type = Laubrary.Zounds.ZoundEffectType.Gain, p = new[] { 1f } });
                    song.dynamicParameters.Add(Definition("effect", "EG", DynamicScope.Effect, new ParameterTarget { kind = ParameterKind.Device, trackId = "notes", deviceId = "gain", index = 0 }, 0, 1));
                    using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                    using (var control = new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                    using (var controlLeft = new NativeArray<float>(2048, Allocator.Persistent))
                    using (var controlRight = new NativeArray<float>(2048, Allocator.Persistent))
                    {
                        engine.SendCommand(TrackerCommand.Play()); control.SendCommand(TrackerCommand.Play());
                        engine.Render(left, right, 100); control.Render(controlLeft, controlRight, 100);
                        Need(engine.SetCommandParameter("effect", .25f), "Effect UID refused");
                        engine.Render(left, right, 2048, 333); control.Render(controlLeft, controlRight, 2048, 333);
                        double error = 0, power = 0; for (int i = 600; i < 2048; i++) { error += Math.Abs(left[i] - .25f * controlLeft[i]); power += Math.Abs(controlLeft[i]); }
                        Need(power > 1 && error < .0001, "Rendered effect gain incorrect: " + error);
                        Need(Math.Abs(left[0]) > Math.Abs(controlLeft[0]) * .8, "Effect stepped directly to final gain instead of ramping");
                    }
                });
                Check("track semitones and cents compose with authored tuning and leave an excluded track unchanged", () =>
                {
                    var song = Song(sample); AddSecondTrack(song);
                    string before = ZTrackerMigration.Json(sample.model);
                    using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                    using (var control = new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                    {
                        foreach (var current in new[] { engine, control }) { current.SendCommand(TrackerCommand.Audition(0, 60, 127, 0)); current.SendCommand(TrackerCommand.Audition(0, 60, 127, 1)); current.Render(left, right, 64); }
                        double first = Voice(engine, 0).position, other = Voice(engine, 1).position, baseline = Voice(control, 0).position;
                        engine.SendCommand(TrackerCommand.SetTrackTranspose(0, 1)); engine.SendCommand(TrackerCommand.SetTrackDetune(0, 50));
                        engine.Render(left, right, 1000); control.Render(left, right, 1000);
                        double expected = Math.Pow(2, 1.5 / 12), ratio = (Voice(engine, 0).position - first) / (Voice(control, 0).position - baseline);
                        Need(Math.Abs(ratio - expected) < .000001, "Active pitch ratio " + ratio);
                        Need(Math.Abs(Voice(engine, 1).position - Voice(control, 1).position) < .000001, "Excluded track changed");
                        engine.SendCommand(TrackerCommand.Audition(0, 60)); engine.Render(left, right, 1000);
                        Need(Math.Abs(Voice(engine, 0).position / (Voice(control, 0).position - baseline) - expected) < .000001, "Future note did not inherit both offsets");
                        engine.SendCommand(TrackerCommand.SetTrackDetune(0, 0));
                        Need(engine.Snapshot.tracks[0].runtimeTranspose == 1, "Clearing detune erased semitone offset");
                    }
                    Need(before == ZTrackerMigration.Json(sample.model), "Authoring mutated");
                });
                Check("sampler pair receives the same additive pitch on both members", () =>
                {
                    sample.model.sampler.zones[0].blend = new SampleBlendExtension { pcmB = clip, baseNoteB = 60, amount = .5f };
                    try
                    {
                        using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(Song(sample))))
                        using (var control = new TrackerOffline(TrackerPreparedSong.Prepare(Song(sample))))
                        {
                            engine.SendCommand(TrackerCommand.SetTrackTranspose(0, 2)); engine.SendCommand(TrackerCommand.SetTrackDetune(0, 50));
                            engine.SendCommand(TrackerCommand.Audition(0, 60)); control.SendCommand(TrackerCommand.Audition(0, 60));
                            engine.Render(left, right, 1000); control.Render(left, right, 1000);
                            var a = Voice(engine, 0); var b = Voice(control, 0); double ratio = Math.Pow(2, 2.5 / 12);
                            Need(Math.Abs(a.position / b.position - ratio) < .000001 && Math.Abs(a.positionB / b.positionB - ratio) < .000001, "Paired pitch diverged");
                        }
                    }
                    finally { sample.model.sampler.zones[0].blend = null; }
                });
                Check("synth macro fine tuning remains additive with game transpose and cents", () =>
                {
                    synth.model.macros[0].mappings.Add(TrackerP4Check.Route("fineTune", 1200, 1200, "cents"));
                    try
                    {
                        using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(Song(synth))))
                        {
                            engine.SendCommand(TrackerCommand.SetTrackTranspose(0, 1)); engine.SendCommand(TrackerCommand.SetTrackDetune(0, 50));
                            engine.SendCommand(TrackerCommand.Audition(0, 69)); engine.Render(left, right, 16);
                            double phase = (16 * 880d * Math.Pow(2, 1.5 / 12) / 48000) % 1;
                            Need(Math.Abs(Voice(engine, 0).phaseA - phase) < .00001, "Macro pitch was replaced or counted twice");
                        }
                    }
                    finally { synth.model.macros[0].mappings.Clear(); }
                });
                Check("per-note pitch envelope keeps its precedence over macros and still composes with game pitch", () =>
                {
                    var instrument = TrackerP4Check.Synth();
                    try
                    {
                        instrument.model.macros[0].mappings.Add(TrackerP4Check.Route("fineTune", 1200, 1200, "cents"));
                        var curve = new ZUIEnvelopeData(0, .01f, 0, 1200, false);
                        curve.points.Add(new ZUIEnvelopePoint(0, 300)); curve.points.Add(new ZUIEnvelopePoint(.01f, 300));
                        instrument.model.parameters.SetParameterEnvelope("fineTune", curve);
                        using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(Song(instrument))))
                        {
                            engine.SendCommand(TrackerCommand.SetTrackTranspose(0, 1)); engine.SendCommand(TrackerCommand.SetTrackDetune(0, 50));
                            engine.SendCommand(TrackerCommand.Audition(0, 69)); engine.Render(left, right, 16);
                            double phase = (16 * 440d * Math.Pow(2, 4.5 / 12) / 48000) % 1;
                            Need(Math.Abs(Voice(engine, 0).phaseA - phase) < .00001, "Envelope precedence or additive offset lost");
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(instrument); }
                });
                Check("compatible refresh keeps offsets and pending tempo; Play, Stop and Seek reset game controls", () =>
                {
                    var song = Song(sample); TempoDefinition(song);
                    using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                    {
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, 100);
                        engine.SendCommand(TrackerCommand.SetTempo(240)); engine.SendCommand(TrackerCommand.SetTrackTranspose(0, 4)); engine.SendCommand(TrackerCommand.SetTrackDetune(0, 50));
                        Need(engine.SetCommandParameter("tempo", 120f / 255f), "Dynamic request failed");
                        var next = TrackerPreparedSong.Prepare(song);
                        if (!engine.RefreshPrepared(next, out string reason)) { next.Dispose(); throw new Exception(reason); }
                        Need(engine.Snapshot.runtimeTempo == 240 && engine.Snapshot.tracks[0].runtimeTranspose == 4 && engine.Snapshot.tracks[0].runtimeDetune == 50, "Refresh reset controls");
                        engine.Render(left, right, 6000); engine.SendCommand(TrackerCommand.ClearTempoOverride()); engine.Render(left, right, 3000);
                        Need(Math.Abs(engine.Snapshot.bpm - 180) < .0001, "Refresh lost pending dynamic tempo");
                        foreach (var command in new[] { TrackerCommand.Play(), TrackerCommand.Stop(), new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 0 } })
                        {
                            engine.SendCommand(TrackerCommand.SetTempo(300)); engine.SendCommand(TrackerCommand.SetTrackTranspose(0, 4)); engine.SendCommand(TrackerCommand.SetTrackDetune(0, 50)); engine.SendCommand(command);
                            Need(engine.Snapshot.runtimeTempo == 0 && engine.Snapshot.tracks[0].runtimeTranspose == 0 && engine.Snapshot.tracks[0].runtimeDetune == 0, "Transport did not reset " + command.kind);
                        }
                    }
                });
                Check("nonfinite and out-of-range native controls are ignored", () =>
                {
                    using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(Song(sample))))
                    {
                        engine.SendCommand(TrackerCommand.SetTempo(float.NaN)); engine.SendCommand(TrackerCommand.SetTempo(float.PositiveInfinity)); engine.SendCommand(TrackerCommand.SetTempo(1000));
                        engine.SendCommand(TrackerCommand.SetTrackTranspose(0, 121)); engine.SendCommand(TrackerCommand.SetTrackTranspose(-1, 12));
                        engine.SendCommand(TrackerCommand.SetTrackDetune(0, float.NaN)); engine.SendCommand(TrackerCommand.SetTrackDetune(0, 1201));
                        Need(engine.Snapshot.runtimeTempo == 0 && engine.Snapshot.tracks[0].runtimeTranspose == 0 && engine.Snapshot.tracks[0].runtimeDetune == 0, "Invalid native control changed state");
                    }
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(synth); UnityEngine.Object.DestroyImmediate(sample); UnityEngine.Object.DestroyImmediate(clip); }
            return "TrackerGameControlCheck " + pass + " passed / " + fail + " failed\n" + report;
        }
    }
}
