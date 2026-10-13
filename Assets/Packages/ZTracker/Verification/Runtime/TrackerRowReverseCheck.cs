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
        const int Rate = 48000, Total = 144000, Slow = 36000;
        static void Need(bool value, string why) { if (!value) throw new Exception(why); }
        static SongData Song(ZTrackerInstrument instrument)
        {
            var song = TrackerEngineCheck.FixtureSong(instrument, lines: 8);
            song.commandModelVersion = ZTrackerCommandRegistry.CurrentVersion;
            for (int row = 0; row < 8; row++)
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
                Check("forward slowdown then descending rows/events/ticks, exact endpoint, stereo partitions and same-order landing", () =>
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
                            int[] expectedRows = { 4, 5, 6, 6, 5, 4, 3, 2, 1, 0 };
                            long[] expectedFrames = { 0, 6606, 15215, 36000, 57000, 74400, 91800, 109200, 126600, Total };
                            Need(rows.Select(v => v.row).SequenceEqual(expectedRows), "Rows: " + string.Join(",", rows.Select(v => v.row)));
                            Need(rows.Select(v => v.samplePosition - start).SequenceEqual(expectedFrames), "120 BPM/4 LPB integrated schedule incorrect");
                            Need(expectedFrames[1] > 6000 && expectedFrames[2] - expectedFrames[1] > expectedFrames[1], "Forward rows did not slow below authored rate");
                            Need(events.Where(v => v.kind == TrackerEventKind.Authored).Select(v => v.row).SequenceEqual(expectedRows), "Authored events not once per visited row");
                            Need(rows.Take(3).All(v => v.direction == TrackerPlaybackDirection.Forward) && rows.Skip(3).Take(6).All(v => v.direction == TrackerPlaybackDirection.Reverse) && rows.Last().direction == TrackerPlaybackDirection.Forward, "Direction wrong before/after turn or landing");
                            for (int i = 1; i < events.Count; i++) Need(events[i].samplePosition >= events[i - 1].samplePosition, "Event timestamps moved backwards");
                            for (int visit = 0; visit < rows.Length - 1; visit++) Need(events.Where(v => v.kind == TrackerEventKind.Tick && v.samplePosition >= rows[visit].samplePosition && v.samplePosition < rows[visit + 1].samplePosition).Select(v => v.tick).SequenceEqual(Enumerable.Range(0, 6)), "Ticks reversed or duplicated on visit " + visit);
                            Need(events.Count(v => v.kind == TrackerEventKind.RowReverseCompleted) == 1 && e.Compiled && e.EventOverflow == 0, "Completion, native renderer or event queue failure");
                            if (baselineLeft == null) { baselineLeft = left.ToArray().Take(Total).ToArray(); baselineRight = right.ToArray().Take(Total).ToArray(); }
                            else Need(baselineLeft.SequenceEqual(left.ToArray().Take(Total)) && baselineRight.SequenceEqual(right.ToArray().Take(Total)), "Stereo depends on buffer size " + block);
                            report.AppendLine("MEASURE block=" + block + " frames=" + status.elapsedFrames + " rowFrames=" + string.Join(",", rows.Select(v => v.samplePosition - start)));
                            e.Render(left, right, 1, block); Need(!Events(e).Any(v => v.kind == TrackerEventKind.Row && v.row == 0), "Row zero retriggered on next block");
                        }
                    }
                });
                Check("pitch falls near zero then recovers while sample direction and envelope time stay forward; real OFF release", () =>
                {
                    var env = instrument.model.modulation[0].devices[0]; float oldAttack = env.attack;
                    env.attack = .03f;
                    try
                    {
                        using (var reverse = new TrackerOffline(Prepare(Song(instrument))))
                        {
                            // Last row cannot advance out of the pattern, so one voice measures the entire slowdown.
                            Start(reverse, row: 7); reverse.SendCommand(TrackerCommand.RequestReverseRows()); reverse.Render(left, right, 512);
                            var voice = reverse.Snapshot.voices.ToArray().Single(v => v.active);
                            double expectedEarly = 512 - 511d * 512 / (2 * Slow);
                            Need(Math.Abs(voice.position / voice.step - expectedEarly) < .00001, "Early pitch/sample cursor did not integrate authored-rate slowdown");
                            Need(voice.age == 512 && !voice.released && voice.envelope > 0 && voice.envelope < 1, "Envelope did not advance in realtime");
                            Need(left.ToArray().Take(512).Any(v => Math.Abs(v) > .00001), "Slowing phase silent");
                            reverse.Render(left, right, Slow - 512 - 129);
                            var near = reverse.Snapshot.voices.ToArray().Single(v => v.active);
                            reverse.Render(left, right, 128);
                            var end = reverse.Snapshot.voices.ToArray().Single(v => v.active);
                            Need(reverse.RowReverseStatus.phase == TrackerRowReversePhase.Slowing && reverse.RowReverseStatus.elapsedFrames == Slow - 1 && reverse.Snapshot.row == 7, "Late-pattern clamp or pre-turn phase wrong");
                            double slowRate = (end.position - near.position) / (128 * end.step);
                            Need(Math.Abs(slowRate - .02) < .000001 && end.age == Slow - 1 && end.envelope > .99f, "Near-zero pitch or realtime envelope wrong: " + slowRate);
                            reverse.Render(left, right, 1);
                            Need(reverse.RowReverseStatus.phase == TrackerRowReversePhase.Reversing && reverse.RowReverseStatus.elapsedFrames == Slow && reverse.RowReverseStatus.turnRow == 7, "Turn did not occur at exact frame 36000");
                            Need(reverse.Snapshot.voices.ToArray().Single(v => v.active).age == 0, "Turn did not cut and retrigger its row");
                            reverse.Render(left, right, 128);
                            voice = reverse.Snapshot.voices.ToArray().Single(v => v.active);
                            Need(voice.age == 128 && Math.Abs(voice.position / voice.step - 128 * .02) < .000001 && voice.envelope > 0 && voice.envelope < 1, "Reverse onset did not advance sample/envelope forward at minimum pitch");
                            reverse.Render(left, right, 7200 - 128);
                            var full = reverse.Snapshot.voices.ToArray().Single(v => v.active);
                            reverse.Render(left, right, 128);
                            voice = reverse.Snapshot.voices.ToArray().Single(v => v.active);
                            Need(Math.Abs((voice.position - full.position) / (128 * voice.step) - 1) < .000001, "Reverse pitch did not regain original step after 150ms");
                            var observed = Events(reverse);
                            Need(observed.Where(v => v.kind == TrackerEventKind.Row).Select(v => v.row).SequenceEqual(new[] { 7, 7 }) && observed.All(v => v.sequence == 0), "Late-pattern slowdown escaped pattern");
                            // For turn row7, reverse row3 begins after four of seven normalized intervals.
                            int offFrame = Slow + (int)Math.Round(4 * (Total - Slow - 3600d) / 7 + 3600);
                            reverse.Render(left, right, offFrame + 100 - reverse.RowReverseStatus.elapsedFrames);
                            Need(reverse.Snapshot.row == 3 && reverse.Snapshot.voices.ToArray().Any(v => v.active && v.released), "Descending OFF row did not release the preceding voice");
                            Need(Events(reverse).Count(v => v.kind == TrackerEventKind.NoteOff && v.row == 3) == 1, "OFF callback missing/duplicated");
                            report.AppendLine("MEASURE forwardEarlyRate=" + (expectedEarly / 512) + " nearTurnRate=" + slowRate + " turnFrame=" + Slow + " reverseFullRate=1");
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
                        e.Render(left, right, 109000);
                        gain = e.Snapshot.deviceParameters.ToArray().Single(v => v.dynamic && v.kind == 3);
                        Need(e.Snapshot.row == 2 && Math.Abs(gain.destination - 192f / 255f) < .0001, "Later traversed effect command missing");
                        e.SendCommand(TrackerCommand.SetTempo(220)); e.Render(left, right, Total - 110000);
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
                        Need(anchors.Select(v => v.row).SequenceEqual(new[] { 4, 5, 6, 6, 5, 4, 3, 2, 1, 0 }), "Clock row ordering mismatch");
                        var clock = new ZTrackerSongClock(() => null, () => Song(instrument));
                        var fill = typeof(ZTrackerSongClock).GetMethod("Fill", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        Need(fill != null, "Clock fraction mapper unavailable");
                        for (int i = 0; i < anchors.Count - 1; i++) {
                            var anchor = anchors[i];
                            Need(anchor.direction == (i < 3 ? TrackerPlaybackDirection.Forward : TrackerPlaybackDirection.Reverse) && anchor.start + anchor.duration == anchors[i + 1].start, "Anchor gap/overlap or wrong direction on visit " + i);
                            object[] args = { anchor, anchor.start + anchor.duration * .5, false, default(ZTrackerSongPosition) };
                            fill.Invoke(clock, args); var position = (ZTrackerSongPosition)args[3];
                            Need(position.Direction == (i < 3 ? 1 : -1) && position.Row == anchor.row && position.RowFraction == .5 && position.Tick == 3 && position.ElapsedSamples == position.RemainingSamples && !position.Extrapolated, "Half-row clock fraction/direction incoherent on visit " + i);
                        }
                        Need(anchors[3].start == start + Slow && anchors.Last().direction == TrackerPlaybackDirection.Forward && anchors.Last().start == start + Total, "Turn/forward landing anchor missing");
                        Need(e.TryReadClockHead(out head) && head.rowReverse.completed == 1 && head.position == start + Total, "Head completion publication incoherent");
                        e.Render(left, right, 1000); Need(e.RewindStatus.CanRewind && e.RewindStatus.historyFrames == 1000, "Fresh forward tape pass did not start at landing");
                    }
                });
                Check("pause freezes progress/audio; explicit seek, stop, play and compatible refresh cancel once", () =>
                {
                    foreach (int elapsed in new[] { 2000, Slow + 2000 })
                    foreach (int operation in new[] { 0, 1, 2, 3 })
                    {
                        var song = Song(instrument);
                        using (var e = new TrackerOffline(Prepare(song)))
                        {
                            Start(e); e.SendCommand(TrackerCommand.RequestReverseRows()); e.Render(left, right, elapsed);
                            Need(e.RowReverseStatus.phase == (elapsed < Slow ? TrackerRowReversePhase.Slowing : TrackerRowReversePhase.Reversing), "Cancellation fixture wrong phase");
                            if (operation == 0)
                            {
                                long before = e.Snapshot.samplePosition;
                                e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Pause }); e.Render(left, right, 1000);
                                Need(e.RowReverseStatus.elapsedFrames == elapsed && e.Snapshot.samplePosition == before && left.ToArray().Take(1000).All(v => v == 0), "Pause advanced or sounded transport in phase " + e.RowReverseStatus.phase);
                                e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Resume }); e.Render(left, right, 1000);
                                Need(e.RowReverseStatus.elapsedFrames == elapsed + 1000, "Resume failed");
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
                        var events = Events(e).Where(v => v.kind == TrackerEventKind.Row).ToArray();
                        Need(e.RowReverseStatus.completed == 1 && e.RowReverseStatus.elapsedFrames == 4800 && events.Select(v => v.row).SequenceEqual(new[] { 1, 1, 0 }) && events.Select(v => v.samplePosition).SequenceEqual(new long[] { 0, 1200, 4800 }) && events[0].direction == TrackerPlaybackDirection.Forward && events[1].direction == TrackerPlaybackDirection.Reverse, "Short/one-row endpoint incorrect");
                    }
                    var clock = new ZTrackerSongClock(() => null, () => song);
                    Need(ZTrackerAccents.NextEventRow(clock, 0, 4, 0, -1) == 3 && ZTrackerAccents.NextEventRow(clock, 0, 3, 0, -1) == 2 && ZTrackerAccents.NextEventRow(clock, 0, 1, 0, -1) == 0, "Reverse next ON/OFF row incorrect");
                    Need(ZTrackerAccents.NextEventRow(clock, 0, 2, 0) == 3 && ZTrackerAccents.NextEventRow(clock, 0, 4, 0) == 5 && ZTrackerAccents.NextEventRow(clock, 0, 7, 0) == 8, "Forward helper behavior changed");
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
