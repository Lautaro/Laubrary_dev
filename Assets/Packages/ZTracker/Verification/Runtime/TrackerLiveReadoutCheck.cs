using System;
using System.Text;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>Native publication checks only. No devices, editor windows, forced GC or Test Runner.</summary>
    public static class TrackerLiveReadoutCheck
    {
        static void Need(bool value, string reason) { if (!value) throw new Exception(reason); }
        public static string Execute()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var instrument = TrackerP4Check.Synth();
            try {
                foreach (int block in new[] { 64, 333, 1024 }) {
                    try {
                        var song = TrackerEngineCheck.FixtureSong(instrument, lines: 64);
                        song.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = { TrackerEngineCheck.Note(60) } });
                        string authored = ZTrackerMigration.Json(song);
                        var prepared = TrackerPreparedSong.Prepare(song); prepared.EnableRewindHistory(10);
                        using (var engine = new TrackerOffline(prepared))
                        using (var left = new NativeArray<float>(160000, Allocator.Persistent))
                        using (var right = new NativeArray<float>(160000, Allocator.Persistent)) {
                            Need(!engine.TryReadTrack(0, out _), "Unpublished readout succeeded");
                            engine.SendCommand(TrackerCommand.Play()); engine.Render(left, right, 1000, block);
                            Need(engine.TryReadClockHead(out var initial) && initial.row == 0 && initial.bpm == 120, "Initial transport missing");
                            Need(engine.TryReadTrack(0, out var track) && track.activeVoices > 0 && track.block == initial.blocks, "Activity or block incoherent");
                            engine.SendCommand(TrackerCommand.SetTempo(240));
                            engine.SendCommand(TrackerCommand.SetTrackTranspose(0, 1)); engine.SendCommand(TrackerCommand.SetTrackDetune(0, 50));
                            engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.TrackMute, a = 0, b = 1, c = 1 });
                            engine.Render(left, right, 64, block);
                            Need(engine.TryReadClockHead(out var pending) && pending.bpm == 120 && pending.requestedTempo == 240 && pending.tempoPending, "Next-row pending tempo missing");
                            Need(engine.TryReadTrack(0, out track) && track.transpose == 1 && track.detune == 50 && track.gated && track.muted, "Track controls not published");
                            Need(!engine.TryReadTrack(-1, out _) && !engine.TryReadTrack(song.tracks.Count, out _), "Track bounds accepted");
                            engine.Render(left, right, 6000, block);
                            Need(engine.TryReadClockHead(out var applied) && applied.bpm == 240 && !applied.tempoPending && applied.row > 0, "Applied tempo or row stale");
                            engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Pause }); engine.Render(left, right, 333, block);
                            Need(engine.TryReadClockHead(out var paused) && paused.paused != 0 && paused.position == applied.position && paused.blocks > applied.blocks, "Pause status stale");
                            engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Resume });
                            engine.SendCommand(TrackerCommand.RequestReverseRows(3)); engine.Render(left, right, 64, block);
                            Need(engine.TryReadClockHead(out var reverse) && reverse.rowReverse.active, "Reverse state absent");
                            engine.Render(left, right, 144000, block);
                            Need(engine.TryReadClockHead(out var landed) && !landed.rowReverse.active && landed.rowReverse.completed == 1, "Reverse completion absent");
                            Need(engine.TryReadTrack(0, out track) && track.transpose == 1 && track.detune == 50 && track.gated, "Landing controls stale");
                            engine.Render(left, right, 1000, block); engine.SendCommand(TrackerCommand.RequestRewind(3)); engine.Render(left, right, 64, block);
                            Need(engine.TryReadClockHead(out var tape) && tape.rewind.IsRewinding, "Tape state absent");
                            engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Stop }); engine.Render(left, right, 64, block);
                            Need(engine.TryReadClockHead(out var stopped) && stopped.playing == 0 && !stopped.rewind.IsRewinding, "Stop state stale");
                            bool rowEvent = false; while (engine.ReadEvent(out var ev)) rowEvent |= ev.kind == TrackerEventKind.Row;
                            Need(rowEvent, "Readout consumed game events");
                            Need(engine.Compiled, "Managed renderer");
                        }
                        Need(authored == ZTrackerMigration.Json(song), "Readout mutated authored data");
                        passed++; report.AppendLine("PASS live publication, controls, pause, reverse/tape, lifecycle and independent events block=" + block);
                    } catch (Exception ex) { failed++; report.AppendLine("FAIL block=" + block + ": " + ex.Message); }
                }
            } finally { UnityEngine.Object.DestroyImmediate(instrument); }
            return report.AppendLine("TrackerLiveReadoutCheck: " + passed + " passed, " + failed + " failed").ToString();
        }
    }
}
