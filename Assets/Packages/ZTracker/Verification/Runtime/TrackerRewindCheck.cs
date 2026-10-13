using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>Offline checks of actual post-master stereo tape samples, clocks and ownership. Opens no device/window.</summary>
    public static class TrackerRewindCheck
    {
        const int Rate = 48000, Prefix = 13000, Slow = 24000, Total = 144000;
        static void Need(bool value, string message) { if (!value) throw new Exception(message); }
        static SongData Song(ZTrackerInstrument instrument)
        {
            var song = TrackerEngineCheck.FixtureSong(instrument, lines: 16);
            song.tracks[0].postPan = .3f;
            song.tracks[0].devices.nodes.Add(new AudioEffectNodeData { uid = "mix-gain", type = Laubrary.Zounds.ZoundEffectType.Gain, p = new[] { 1.7f } });
            song.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = { TrackerEngineCheck.Note(60) } });
            return song;
        }
        static TrackerPreparedSong Prepare(SongData song, float seconds = 30)
        {
            var prepared = TrackerPreparedSong.Prepare(song);
            try { prepared.EnableRewindHistory(seconds); return prepared; }
            catch { prepared.Dispose(); throw; }
        }
        static float Interpolate(float[] samples, double cursor)
        {
            int a = Math.Max(0, Math.Min(samples.Length - 1, (int)Math.Floor(cursor)));
            int b = Math.Min(samples.Length - 1, a + 1);
            return math.lerp(samples[a], samples[b], (float)Math.Max(0, Math.Min(1, cursor - a)));
        }
        public static string Execute()
        {
            int passed = 0, failed = 0; var report = new StringBuilder();
            void Check(string title, Action action)
            {
                try { action(); passed++; report.AppendLine("PASS " + title); }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + title + ": " + ex.Message); }
            }
            var clip = AudioClip.Create("rewind-stereo-check", Rate, 2, Rate, false);
            var pcm = new float[Rate * 2];
            for (int i = 0; i < Rate; i++) { pcm[2 * i] = .12f * (float)Math.Sin(i * .037); pcm[2 * i + 1] = .07f * (float)Math.Cos(i * .051); }
            clip.SetData(pcm, 0);
            var instrument = TrackerEngineCheck.FixtureInstrument(clip);
            instrument.model.sampler.samples[0].loop = SampleLoop.Forward;
            instrument.model.sampler.samples[0].loopStartFrame = 0;
            instrument.model.sampler.samples[0].loopEndFrame = Rate;
            using (var left = new NativeArray<float>(Total, Allocator.Persistent))
            using (var right = new NativeArray<float>(Total, Allocator.Persistent))
            try
            {
                Check("three-second tape uses the actually rendered stereo mix and resumes at the exact frame across 64/333/1024 blocks", () =>
                {
                    float[] baselineLeft = null, baselineRight = null;
                    foreach (int block in new[] { 64, 333, 1024 })
                    {
                        var song = Song(instrument);
                        float[] referenceLeft, referenceRight;
                        using (var control = new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                        {
                            control.SendCommand(TrackerCommand.Play()); control.Render(left, right, Prefix + Slow, block);
                            referenceLeft = left.ToArray().Take(Prefix + Slow).ToArray(); referenceRight = right.ToArray().Take(Prefix + Slow).ToArray();
                        }
                        using (var engine = new TrackerOffline(Prepare(song)))
                        {
                            engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, Prefix, block);
                            Need(engine.RewindStatus.CanRewind && engine.RewindStatus.historyFrames == Prefix, "Pattern history missing");
                            while (engine.ReadEvent(out _)) { }
                            engine.SendCommand(TrackerCommand.RequestRewind()); engine.Render(left, right, Total, block);
                            var status = engine.RewindStatus;
                            Need(!status.IsRewinding && status.completed == 1 && status.result == TrackerRewindResult.Completed && status.elapsedFrames == Total, "Endpoint/completion status incorrect");
                            Need(engine.Snapshot.row == 0 && engine.Snapshot.sequenceIndex == 0 && engine.Snapshot.samplePosition == Prefix + Total, "Did not return to exact pattern start frame");
                            Need(engine.Compiled, "Tape renderer did not run in Burst");
                            double cursor = Prefix, error = 0;
                            for (int i = 0; i < Slow; i++)
                            {
                                float t = i / (float)(Slow - 1), gain = 1 - .5f * t;
                                error = Math.Max(error, Math.Abs(left[i] - Interpolate(referenceLeft, cursor) * gain));
                                error = Math.Max(error, Math.Abs(right[i] - Interpolate(referenceRight, cursor) * gain));
                                cursor += 1 - .5 * t;
                            }
                            double reverseStart = cursor;
                            for (int i = Slow; i < Total; i++)
                            {
                                double position = reverseStart * (1 - (i - Slow) / (double)(Total - Slow - 1));
                                error = Math.Max(error, Math.Abs(left[i] - Interpolate(referenceLeft, position) * .5f));
                                error = Math.Max(error, Math.Abs(right[i] - Interpolate(referenceRight, position) * .5f));
                            }
                            Need(error < .000001, "Recorded mix sample error " + error + " at block " + block);
                            if (baselineLeft == null) { baselineLeft = left.ToArray(); baselineRight = right.ToArray(); }
                            else Need(baselineLeft.SequenceEqual(left.ToArray()) && baselineRight.SequenceEqual(right.ToArray()), "Tape differs by block partition");
                            int starts = 0, ends = 0, musical = 0; long endAt = 0;
                            while (engine.ReadEvent(out var ev)) { if (ev.kind == TrackerEventKind.RewindStarted) starts++; else if (ev.kind == TrackerEventKind.RewindCompleted) { ends++; endAt = ev.samplePosition; } else if (ev.kind == TrackerEventKind.Row || ev.kind == TrackerEventKind.NoteOn || ev.kind == TrackerEventKind.Authored) musical++; }
                            Need(starts == 1 && ends == 1 && endAt == Prefix + Total && musical == 0, "Tape event boundary leaked musical callbacks");
                            engine.Render(left, right, 64, block);
                            for (int i = 0; i < 64; i++) Need(Math.Abs(left[i] - referenceLeft[i]) < .000001 && Math.Abs(right[i] - referenceRight[i]) < .000001, "Fresh pattern did not resume normally");
                            report.AppendLine("MEASURE block=" + block + " tapeFrames=" + Total + " maxStereoError=" + error + " completedAt=" + endAt);
                        }
                    }
                });
                Check("disabled, empty, exhausted and insufficient-headroom history explicitly refuse; next pattern restores eligibility", () =>
                {
                    using (var engine = new TrackerOffline(Prepare(Song(instrument), 0)))
                    {
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, 64); engine.SendCommand(TrackerCommand.RequestRewind());
                        Need(engine.RewindStatus.result == TrackerRewindResult.Disabled, "Disabled history accepted");
                    }
                    using (var engine = new TrackerOffline(Prepare(Song(instrument), 1)))
                    {
                        engine.SendCommand(TrackerCommand.Play()); engine.SendCommand(TrackerCommand.RequestRewind());
                        Need(engine.RewindStatus.result == TrackerRewindResult.EmptyHistory, "Empty history accepted");
                        engine.Render(left, right, 30000); engine.SendCommand(TrackerCommand.RequestRewind());
                        Need(engine.RewindStatus.result == TrackerRewindResult.HistoryExhausted, "Missing slowdown headroom accepted");
                        engine.Render(left, right, 30000); engine.SendCommand(TrackerCommand.RequestRewind());
                        Need(engine.RewindStatus.result == TrackerRewindResult.HistoryExhausted && !engine.RewindStatus.CanRewind, "Overwritten pattern start silently accepted");
                        engine.Render(left, right, 36010);
                        Need(engine.RewindStatus.CanRewind && engine.RewindStatus.historyFrames == 10, "New pattern did not reset bounded history");
                    }
                });
                Check("two-frame history works and repeated requests cannot restart the active tape", () =>
                {
                    using (var engine = new TrackerOffline(Prepare(Song(instrument))))
                    {
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, 2); engine.SendCommand(TrackerCommand.RequestRewind());
                        Need(engine.RewindStatus.IsRewinding, "Short history refused");
                        engine.Render(left, right, 1000); int elapsed = engine.RewindStatus.elapsedFrames;
                        engine.SendCommand(TrackerCommand.RequestRewind());
                        Need(engine.RewindStatus.result == TrackerRewindResult.Busy && engine.RewindStatus.elapsedFrames == elapsed && engine.RewindStatus.refused == 1, "Repeated request restarted tape");
                        engine.Render(left, right, Total - 1000);
                        Need(engine.RewindStatus.completed == 1 && engine.Snapshot.samplePosition == Total + 2, "Short rewind endpoint incorrect");
                    }
                });
                Check("pause freezes reverse progress and outputs silence; resume finishes exactly once", () =>
                {
                    using (var engine = new TrackerOffline(Prepare(Song(instrument))))
                    {
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, Prefix); engine.SendCommand(TrackerCommand.RequestRewind()); engine.Render(left, right, Slow + 1000);
                        long position = engine.Snapshot.samplePosition; int elapsed = engine.RewindStatus.elapsedFrames;
                        engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Pause }); engine.Render(left, right, 4000);
                        Need(engine.Snapshot.samplePosition == position && engine.RewindStatus.elapsedFrames == elapsed && left.ToArray().Take(4000).All(x => x == 0) && right.ToArray().Take(4000).All(x => x == 0), "Pause advanced tape or sounded");
                        engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Resume }); engine.Render(left, right, Total - elapsed);
                        Need(engine.RewindStatus.completed == 1 && engine.Snapshot.samplePosition == Prefix + Total, "Resume lost exact tape duration");
                    }
                });
                Check("rewind lands at the triggering order even if forward slowdown crosses another pattern", () =>
                {
                    var song = Song(instrument); song.patterns[0].lineCount = 4;
                    var pattern = new PatternData { id = "second", lineCount = 2 };
                    pattern.tracks.Add(new PatternTrack { trackId = "notes" }); pattern.tracks.Add(new PatternTrack { trackId = "master" });
                    pattern.tracks[0].WriteLine(new PatternLine { line = 0, notes = { TrackerEngineCheck.Note(72) } });
                    song.patterns.Add(pattern); song.sequence.Add(new SequenceSlot { id = "second-order", patternId = pattern.id });
                    using (var engine = new TrackerOffline(Prepare(song)))
                    {
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, 25000, 333);
                        Need(engine.Snapshot.sequenceIndex == 1 && engine.RewindStatus.historyFrames == 1000, "Second pattern history not isolated");
                        engine.SendCommand(TrackerCommand.RequestRewind()); engine.Render(left, right, Total, 333);
                        Need(engine.Snapshot.sequenceIndex == 1 && engine.Snapshot.row == 0 && engine.RewindStatus.completed == 1, "Rewind used the forward renderer's later order");
                        engine.Render(left, right, 1);
                        Need(engine.Snapshot.voices.ToArray().Any(v => v.active && v.note == 72), "Second pattern did not restart");
                    }
                });
                Check("Seek, Stop and compatible refresh cancel without copying or sharing native history", () =>
                {
                    var song = Song(instrument);
                    using (var engine = new TrackerOffline(Prepare(song)))
                    {
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, Prefix); engine.SendCommand(TrackerCommand.RequestRewind()); engine.Render(left, right, 100);
                        engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 2 });
                        Need(!engine.RewindStatus.IsRewinding && engine.RewindStatus.result == TrackerRewindResult.Cancelled && !engine.RewindStatus.CanRewind, "Seek did not cancel and discard partial history");
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, Prefix); engine.SendCommand(TrackerCommand.RequestRewind()); engine.Render(left, right, 100);
                        var next = Prepare(song);
                        if (!engine.RefreshPrepared(next, out string reason)) { next.Dispose(); throw new Exception(reason); }
                        Need(!engine.RewindStatus.IsRewinding && engine.RewindStatus.result == TrackerRewindResult.Cancelled && engine.RewindStatus.historyFrames == 0, "Refresh retained stale/aliased history");
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, Prefix); engine.SendCommand(TrackerCommand.RequestRewind()); engine.SendCommand(TrackerCommand.Stop());
                        Need(!engine.RewindStatus.IsRewinding && !engine.RewindStatus.CanRewind && engine.RewindStatus.completed == 0, "Stop completed a cancelled rewind");
                    }
                });
                Check("completion restores authored controls and invalid durations never change transport", () =>
                {
                    using (var engine = new TrackerOffline(Prepare(Song(instrument))))
                    {
                        engine.SendCommand(TrackerCommand.Play()); engine.SendCommand(TrackerCommand.SetTempo(200)); engine.SendCommand(TrackerCommand.SetTrackTranspose(0, 4)); engine.SendCommand(TrackerCommand.SetTrackDetune(0, 50)); engine.Render(left, right, Prefix);
                        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, 0f, 11f }) { engine.SendCommand(TrackerCommand.RequestRewind(bad)); Need(engine.RewindStatus.result == TrackerRewindResult.InvalidDuration && !engine.RewindStatus.IsRewinding, "Invalid duration accepted"); }
                        engine.SendCommand(TrackerCommand.RequestRewind()); engine.Render(left, right, Total);
                        Need(engine.Snapshot.runtimeTempo == 0 && engine.Snapshot.tracks[0].runtimeTranspose == 0 && engine.Snapshot.tracks[0].runtimeDetune == 0, "Completion retained game controls");
                    }
                });
                Check("history storage belongs to the preparation and cannot be resized after publication", () =>
                {
                    var prepared = Prepare(Song(instrument), 1);
                    Need(prepared.state.rewind.history.Length == Rate, "Wrong bounded allocation");
                    using (var engine = new TrackerOffline(prepared))
                    {
                        bool refused = false; try { prepared.EnableRewindHistory(2); } catch (InvalidOperationException) { refused = true; }
                        Need(refused, "Published native history was resized");
                        engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, 100); engine.SendCommand(TrackerCommand.RequestRewind(.1f)); engine.Render(left, right, 200);
                    }
                    Need(prepared.Disposed && !prepared.state.rewind.history.IsCreated, "Prepared owner retained history after retirement");
                    prepared.Dispose();
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(instrument); UnityEngine.Object.DestroyImmediate(clip); }
            return "TrackerRewindCheck " + passed + " passed / " + failed + " failed\n" + report;
        }
    }
}
