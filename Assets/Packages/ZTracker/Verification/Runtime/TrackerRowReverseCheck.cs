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
    /// <summary>Direct offline checks; no device access, Test Runner, asset writes or forced collection.</summary>
    public static class TrackerRowReverseCheck
    {
        const int Rate = 48000, Total = 144000;
        static void Need(bool value, string why) { if (!value) throw new Exception(why); }
        static SongData Song(ZTrackerInstrument instrument)
        {
            var song = TrackerEngineCheck.FixtureSong(instrument, lines: 8);
            song.commandModelVersion = ZTrackerCommandRegistry.CurrentVersion;
            for (int row = 0; row < 5; row++)
                song.patterns[0].tracks[0].WriteLine(new PatternLine
                {
                    line = row,
                    notes = { row == 3 ? new NoteCell { note = NoteKind.Off, column = 0 } : TrackerEngineCheck.Note(60 + row) },
                    events = { new EventCell { present = true, payload = "row-" + row } }
                });
            return song;
        }
        static TrackerPreparedSong Prepare(SongData song, bool history = false)
        {
            var p = TrackerPreparedSong.Prepare(song);
            try { if (history) p.EnableRewindHistory(30); return p; }
            catch { p.Dispose(); throw; }
        }
        static void Start(TrackerOffline engine, int row = 4, int order = 0)
        {
            engine.SendCommand(TrackerCommand.Play());
            engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = order, b = row });
            while (engine.ReadEvent(out _)) { }
        }
        static List<TrackerEvent> Events(TrackerOffline engine)
        {
            var list = new List<TrackerEvent>(); while (engine.ReadEvent(out var e)) list.Add(e); return list;
        }
        static EffectCell Fx(string id, int value, int column = 0) => new EffectCell
        {
            column = column,
            command = new CommandData { present = true, valuePresent = true, identifier = id, value = value, scope = CommandScope.Track }
        };

        public static string Execute()
        {
            int pass = 0, fail = 0; var report = new StringBuilder();
            void Check(string name, Action action)
            {
                try { action(); pass++; report.AppendLine("PASS " + name); }
                catch (Exception ex) { fail++; report.AppendLine("FAIL " + name + ": " + ex.Message); }
            }
            var clip = AudioClip.Create("reverse-row-check", Rate, 2, Rate, false);
            var pcm = new float[Rate * 2];
            for (int i = 0; i < Rate; i++) { pcm[i * 2] = .12f * (float)Math.Sin(i * .037); pcm[i * 2 + 1] = .09f * (float)Math.Cos(i * .021); }
            clip.SetData(pcm, 0);
            var instrument = TrackerEngineCheck.FixtureInstrument(clip);
            // The imported native-v0 compatibility mix intentionally normalizes overlapping voices over a
            // caller-sized slice. Use modern authored mixing for an exact partition-invariance assertion.
            instrument.model.provenance = "row-reverse modern authored fixture";
            instrument.model.sampler.samples[0].loop = SampleLoop.Forward;
            instrument.model.sampler.samples[0].loopEndFrame = Rate;
            using (var left = new NativeArray<float>(Total + 1024, Allocator.Persistent))
            using (var right = new NativeArray<float>(Total + 1024, Allocator.Persistent))
            try
            {
                Check("descending rows/events/ticks, exact endpoint, stereo partition invariance and same-order landing", () =>
                {
                    float[] baselineLeft = null, baselineRight = null;
                    foreach (int block in new[] { 64, 333, 1024 })
                    {
                        var song = Song(instrument);
                        song.sequence.Add(new SequenceSlot { id = "second-pass", patternId = song.patterns[0].id });
                        using (var e = new TrackerOffline(Prepare(song)))
                        {
                            Need(!e.Snapshot.legacyMix, "Partition fixture accidentally selected legacy slice normalization");
                            Start(e, order: 1); e.Render(left, right, 217, block); Events(e);
                            long start = e.Snapshot.samplePosition;
                            e.SendCommand(TrackerCommand.RequestReverseRows()); e.Render(left, right, Total, block);
                            var status = e.RowReverseStatus; var events = Events(e);
                            Need(status.result == TrackerRowReverseResult.Completed && status.completed == 1 && !status.active && status.elapsedFrames == Total, "Incomplete endpoint");
                            Need(e.Snapshot.row == 0 && e.Snapshot.sequenceIndex == 1 && e.Snapshot.samplePosition == start + Total, "Landing changed order or absolute time");
                            var rows = events.Where(v => v.kind == TrackerEventKind.Row).ToArray();
                            Need(rows.Select(v => v.row).SequenceEqual(new[] { 4, 3, 2, 1, 0 }), "Rows: " + string.Join(",", rows.Select(v => v.row)));
                            Need(events.Where(v => v.kind == TrackerEventKind.Authored).Select(v => v.row).SequenceEqual(new[] { 4, 3, 2, 1, 0 }), "Authored events not once per descending row");
                            Need(rows.Take(4).All(v => v.direction == TrackerPlaybackDirection.Reverse) && rows[4].direction == TrackerPlaybackDirection.Forward && rows[4].samplePosition == start + Total, "Direction or row-zero timestamp wrong");
                            for (int i = 1; i < events.Count; i++) Need(events[i].samplePosition >= events[i - 1].samplePosition, "Event timestamps moved backwards");
                            foreach (int row in new[] { 4, 3, 2, 1 }) Need(events.Where(v => v.kind == TrackerEventKind.Tick && v.row == row).Select(v => v.tick).SequenceEqual(Enumerable.Range(0, 6)), "Ticks reversed or duplicated on row " + row);
                            Need(events.Count(v => v.kind == TrackerEventKind.RowReverseCompleted) == 1 && e.Compiled && e.EventOverflow == 0, "Completion, native renderer or event queue failure");
                            if (baselineLeft == null) { baselineLeft = left.ToArray().Take(Total).ToArray(); baselineRight = right.ToArray().Take(Total).ToArray(); }
                            else Need(baselineLeft.SequenceEqual(left.ToArray().Take(Total)) && baselineRight.SequenceEqual(right.ToArray().Take(Total)), "Stereo depends on buffer size " + block);
                            report.AppendLine("MEASURE block=" + block + " frames=" + status.elapsedFrames + " rowFrames=" + string.Join(",", rows.Select(v => v.samplePosition - start)));
                            e.Render(left, right, 1, block); Need(!Events(e).Any(v => v.kind == TrackerEventKind.Row && v.row == 0), "Row zero retriggered on next block");
                        }
                    }
                });
                Check("ordinary forward sample/envelope playback within a reversed row and real OFF release", () =>
                {
                    var env = instrument.model.modulation[0].devices[0]; float oldAttack = env.attack;
                    env.attack = .03f;
                    try
                    {
                        using (var control = new TrackerOffline(Prepare(Song(instrument))))
                        using (var reverse = new TrackerOffline(Prepare(Song(instrument))))
                        {
                            Start(control); control.Render(left, right, 512);
                            var a = left.ToArray(); var b = right.ToArray();
                            Start(reverse); reverse.SendCommand(TrackerCommand.RequestReverseRows()); reverse.Render(left, right, 512);
                            double error = 0;
                            for (int i = 0; i < 512; i++)
                            {
                                float gain = 1 - .5f * i / 23999f;
                                error = Math.Max(error, Math.Abs(left[i] - a[i] * gain)); error = Math.Max(error, Math.Abs(right[i] - b[i] * gain));
                            }
                            Need(error < .000001, "Audio reversed/resampled within row: " + error);
                            var voice = reverse.Snapshot.voices.ToArray().Single(v => v.active);
                            Need(voice.age == 512 && voice.position > 500 && !voice.released && voice.envelope > 0 && voice.envelope < 1, "Voice age/sample/envelope did not advance normally");
                            int end = (int)reverse.Snapshot.rowEnd;
                            reverse.Render(left, right, end - 512 + 100);
                            Need(reverse.Snapshot.row == 3 && reverse.Snapshot.voices.ToArray().Any(v => v.active && v.released), "Descending OFF row did not release the preceding voice");
                            Need(Events(reverse).Count(v => v.kind == TrackerEventKind.NoteOff && v.row == 3) == 1, "OFF callback missing/duplicated");
                        }
                    }
                    finally { env.attack = oldAttack; }
                });
                Check("held voices are cut on direction entry; game tempo/pitch/gates survive landing", () =>
                {
                    using (var e = new TrackerOffline(Prepare(Song(instrument))))
                    {
                        Start(e); e.Render(left, right, 100); Need(e.Snapshot.voices.ToArray().Any(v => v.active), "Fixture has no held voice");
                        Events(e);
                        e.SendCommand(TrackerCommand.SetTempo(180)); e.SendCommand(TrackerCommand.SetTrackTranspose(0, 2)); e.SendCommand(TrackerCommand.SetTrackDetune(0, 50));
                        e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.TrackMute, a = 0, c = 1 });
                        e.SendCommand(TrackerCommand.RequestReverseRows());
                        Need(!e.Snapshot.voices.ToArray().Any(v => v.active), "Held voice survived direction entry");
                        e.Render(left, right, Total);
                        Need(e.Snapshot.bpm == 180 && e.Snapshot.runtimeTempo == 180 && e.Snapshot.tracks[0].runtimeTranspose == 2 && e.Snapshot.tracks[0].runtimeDetune == 50 && e.Snapshot.tracks[0].triggerMute, "Landing lost game controls");
                        Need(!Events(e).Any(v => v.kind == TrackerEventKind.NoteOn), "Gate leaked an authored trigger during reverse/landing");
                        e.SendCommand(TrackerCommand.Audition(0, 60)); e.Render(left, right, 64);
                        Need(e.Snapshot.voices.ToArray().Any(v => v.active && Math.Abs(v.position / 64 - Math.Pow(2, 2.5 / 12)) < .00001), "Preserved pitch did not affect new explicit voice");
                    }
                });
                Check("timing/flow cannot override deadline, while ordinary effect commands apply in traversal order", () =>
                {
                    var song = Song(instrument); song.tracks[0].visibleEffectColumns = 8;
                    song.tracks[0].devices.nodes.Add(new AudioEffectNodeData { uid = "gain", type = Laubrary.Zounds.ZoundEffectType.Gain, p = new[] { 1f } });
                    song.dynamicParameters.Add(new DynamicParameterData { uid = "gain-command", id = "EG", name = "gain", scope = DynamicScope.Effect, min = 0, max = 1, target = new ParameterTarget { kind = ParameterKind.Device, trackId = "notes", deviceId = "gain", index = 0 } });
                    var row = song.patterns[0].tracks[0].lines.Find(v => v.line == 4);
                    row.effects.Add(Fx("ZT", 0)); row.effects.Add(Fx("ZL", 0, 1)); row.effects.Add(Fx("ZK", 1, 2)); row.effects.Add(Fx("ZB", 7, 3)); row.effects.Add(Fx("ZD", 8, 4)); row.effects.Add(Fx("EG", 64, 5));
                    song.patterns[0].tracks[0].lines.Find(v => v.line == 2).effects.Add(Fx("EG", 192));
                    using (var e = new TrackerOffline(Prepare(song)))
                    {
                        Start(e); e.SendCommand(TrackerCommand.RequestReverseRows()); e.Render(left, right, 1000);
                        var gain = e.Snapshot.deviceParameters.ToArray().Single(v => v.dynamic && v.kind == 3);
                        Need(e.RowReverseStatus.active && e.Snapshot.playing && e.Snapshot.ticksPerLine == 6 && e.Snapshot.holdRemaining == 0 && Math.Abs(gain.destination - 64f / 255f) < .0001, "Timing/flow took over or effect did not apply");
                        e.Render(left, right, 85000);
                        gain = e.Snapshot.deviceParameters.ToArray().Single(v => v.dynamic && v.kind == 3);
                        Need(e.Snapshot.row == 2 && Math.Abs(gain.destination - 192f / 255f) < .0001, "Later traversed effect command missing");
                        e.SendCommand(TrackerCommand.SetTempo(220)); e.Render(left, right, Total - 86000);
                        Need(e.RowReverseStatus.completed == 1 && e.Snapshot.row == 0 && e.Snapshot.bpm == 220, "Deadline or deferred game tempo lost");
                    }
                });
                Check("clock anchors retain reverse chronology and the forward landing on monotonic sample time", () =>
                {
                    using (var e = new TrackerOffline(Prepare(Song(instrument), true)))
                    {
                        e.SendCommand(TrackerCommand.Play()); e.Render(left, right, 25000);
                        long start = e.Snapshot.samplePosition, first = e.ClockRowsWritten;
                        e.SendCommand(TrackerCommand.RequestReverseRows());
                        e.Render(left, right, 1000);
                        Need(!e.RewindStatus.completeHistory && !e.RewindStatus.CanRewind, "Reverse row audio entered tape history");
                        Need(e.TryReadClockHead(out var head) && head.playing != 0 && head.rowReverse.active && head.position == start + 1000, "Reverse head is unavailable/incoherent");
                        e.Render(left, right, Total - 1000);
                        var anchors = new List<TrackerClockRow>();
                        for (long serial = first; serial < e.ClockRowsWritten; serial++) { Need(e.TryReadClockRow(serial, out var anchor), "Lost needed latency anchor"); anchors.Add(anchor); }
                        Need(anchors.Select(v => v.row).SequenceEqual(new[] { 4, 3, 2, 1, 0 }), "Clock row ordering mismatch");
                        for (int i = 0; i < 4; i++) Need(anchors[i].direction == TrackerPlaybackDirection.Reverse && anchors[i].start + anchors[i].duration == anchors[i + 1].start, "Anchor gap/overlap or wrong reverse direction");
                        Need(anchors[4].direction == TrackerPlaybackDirection.Forward && anchors[4].start == start + Total, "Forward landing anchor missing");
                        Need(e.TryReadClockHead(out head) && head.rowReverse.completed == 1 && head.position == start + Total, "Head completion publication incoherent");
                        e.Render(left, right, 1000); Need(e.RewindStatus.CanRewind && e.RewindStatus.historyFrames == 1000, "Fresh forward tape pass did not start at landing");
                    }
                });
                Check("pause freezes progress/audio; explicit seek, stop, play and compatible refresh cancel once", () =>
                {
                    foreach (int operation in new[] { 0, 1, 2, 3 })
                    {
                        var song = Song(instrument);
                        using (var e = new TrackerOffline(Prepare(song)))
                        {
                            Start(e); e.SendCommand(TrackerCommand.RequestReverseRows()); e.Render(left, right, 2000);
                            if (operation == 0)
                            {
                                long before = e.Snapshot.samplePosition;
                                e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Pause }); e.Render(left, right, 1000);
                                Need(e.RowReverseStatus.elapsedFrames == 2000 && e.Snapshot.samplePosition == before && left.ToArray().Take(1000).All(v => v == 0), "Pause advanced or sounded reverse transport");
                                e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Resume }); e.Render(left, right, 1000);
                                Need(e.RowReverseStatus.elapsedFrames == 3000, "Resume failed");
                                e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 0 });
                            }
                            else if (operation == 1) e.SendCommand(TrackerCommand.Stop());
                            else if (operation == 2) e.SendCommand(TrackerCommand.Play());
                            else
                            {
                                var next = Prepare(song);
                                if (!e.RefreshPrepared(next, out string reason)) { next.Dispose(); throw new Exception("Refresh refused: " + reason); }
                            }
                            Need(!e.RowReverseStatus.active && e.RowReverseStatus.result == TrackerRowReverseResult.Cancelled && e.RowReverseStatus.cancelled == 1 && e.RowReverseStatus.completed == 0, "Cancel counter/result incorrect for operation " + operation);
                            e.SendCommand(TrackerCommand.Stop()); Need(e.RowReverseStatus.cancelled == 1, "Cancellation counted twice");
                        }
                    }
                });
                Check("invalid, row-zero, stopped and repeated requests refuse; tape and row reverse exclude each other", () =>
                {
                    using (var e = new TrackerOffline(Prepare(Song(instrument), true)))
                    {
                        e.SendCommand(TrackerCommand.RequestReverseRows()); Need(e.RowReverseStatus.result == TrackerRowReverseResult.NotPlaying, "Stopped accepted");
                        e.SendCommand(TrackerCommand.Play()); e.SendCommand(TrackerCommand.RequestReverseRows()); Need(e.RowReverseStatus.result == TrackerRowReverseResult.AtPatternStart, "Row zero accepted");
                        Start(e); e.SendCommand(TrackerCommand.RequestReverseRows(float.NaN)); Need(e.RowReverseStatus.result == TrackerRowReverseResult.InvalidDuration, "Invalid duration accepted");
                        e.SendCommand(TrackerCommand.RequestReverseRows()); e.Render(left, right, 1000);
                        e.SendCommand(TrackerCommand.RequestReverseRows()); Need(e.RowReverseStatus.result == TrackerRowReverseResult.Busy && e.RowReverseStatus.elapsedFrames == 1000, "Repeated request restarted traversal");
                        e.SendCommand(TrackerCommand.RequestRewind()); Need(e.RewindStatus.result == TrackerRewindResult.Busy, "Tape accepted during row reverse");
                        e.Render(left, right, Total - 1000); e.Render(left, right, 13000); e.SendCommand(TrackerCommand.RequestRewind());
                        Need(e.RewindStatus.IsRewinding, "Tape history unavailable after fresh forward pass");
                        e.SendCommand(TrackerCommand.RequestReverseRows()); Need(e.RowReverseStatus.result == TrackerRowReverseResult.Busy, "Rows accepted during tape");
                        e.Render(left, right, Total); Need(e.RewindStatus.completed == 1 && e.Snapshot.row == 0, "Tape did not retain its endpoint semantics");
                    }
                });
                Check("one-row reversal, short duration and direction-aware next-event lookup", () =>
                {
                    var song = Song(instrument);
                    using (var e = new TrackerOffline(Prepare(song)))
                    {
                        Start(e, row: 1); e.SendCommand(TrackerCommand.RequestReverseRows(.1f)); e.Render(left, right, 4800, 333);
                        Need(e.RowReverseStatus.completed == 1 && e.RowReverseStatus.elapsedFrames == 4800 && Events(e).Where(v => v.kind == TrackerEventKind.Row).Select(v => v.row).SequenceEqual(new[] { 1, 0 }), "Short/one-row endpoint incorrect");
                    }
                    var clock = new ZTrackerSongClock(() => null, () => song);
                    Need(ZTrackerAccents.NextEventRow(clock, 0, 4, 0, -1) == 3 && ZTrackerAccents.NextEventRow(clock, 0, 3, 0, -1) == 2 && ZTrackerAccents.NextEventRow(clock, 0, 1, 0, -1) == 0, "Reverse next ON/OFF row incorrect");
                    Need(ZTrackerAccents.NextEventRow(clock, 0, 2, 0) == 3 && ZTrackerAccents.NextEventRow(clock, 0, 4, 0) == 8, "Forward helper behavior changed");
                    var around = new List<double>(); var scratch = new List<int>();
                    ZTrackerAccents.Around(clock, new ZTrackerSongPosition { Order = 0, Row = 2, Direction = -1, LinesPerBeat = 4 }, ZTrackerAccentSource.Beats, around, scratch);
                    Need(around.SequenceEqual(new[] { 0d, 4d }), "Reverse accent candidates crossed orders");
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(instrument); UnityEngine.Object.DestroyImmediate(clip); }
            report.AppendLine("TOTAL passed=" + pass + " failed=" + fail);
            return report.ToString();
        }
    }
}
