using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Laubrary.Audio;
using Laubrary.ZTracker.Midi;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>
    /// Live-note layer and MIDI routing checks on the actual compiled renderer: chord spread over note columns,
    /// oldest-first steal, per-note release, sustain pedal, panic, pitch bend, controllers, ring order/overflow with
    /// several producers, zero allocation on the input and render paths, block-start latency and source lifecycle.
    /// No Test Runner, menu, user asset or audio device. Run through eval: TrackerMidiCheck.Execute().
    /// </summary>
    public static class TrackerMidiCheck
    {
        static TrackerVoice[] Active(TrackerOffline e) => e.Snapshot.voices.ToArray().Where(v => v.active).ToArray();

        public static string Execute()
        {
            var report = new StringBuilder(); int pass = 0, fail = 0;
            Action<string, Action> check = (name, run) => { try { run(); pass++; report.AppendLine("PASS " + name); } catch (Exception ex) { fail++; report.AppendLine("FAIL " + name + ": " + ex.Message); } };
            Action<bool, string> need = (condition, message) => { if (!condition) throw new Exception(message); };
            var clip = AudioClip.Create("midi-check", 96000, 1, 48000, false); var raw = new float[96000]; for (int i = 0; i < raw.Length; i++) raw[i] = .1f; clip.SetData(raw, 0);
            var instrument = TrackerEngineCheck.FixtureInstrument(clip);
            var l = new NativeArray<float>(48000, Allocator.Persistent); var r = new NativeArray<float>(48000, Allocator.Persistent);
            Func<int, SongData> song = columns => { var s = TrackerEngineCheck.FixtureSong(instrument, columns, 16); s.voiceCapacity = 32; return s; };
            try
            {
                check("chord spreads over the track's note columns from the cursor column", () =>
                {
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(3))))
                    {
                        var door = e.LiveInput;
                        foreach (int n in new[] { 60, 64, 67 }) need(door.Write(TrackerLiveEvent.NoteOn(0, 0, 0, n, 100, 1)), "Write refused");
                        e.Render(l, r, 256, 256);
                        var v = Active(e); need(v.Length == 3, "Expected 3 voices, got " + v.Length);
                        need(v.First(x => x.note == 60).column == 0 && v.First(x => x.note == 64).column == 1 && v.First(x => x.note == 67).column == 2, "Columns " + string.Join(",", v.Select(x => x.note + "@" + x.column)));
                        need(door.HeldNotes == 3 && door.LastAppliedSerial == door.Written, "Live table or applied serial wrong");
                    }
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(3))))
                    {
                        e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 1, 0, 60, 100, 1)); e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 1, 0, 64, 100, 1)); e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 1, 0, 67, 100, 1));
                        e.Render(l, r, 256, 256); var v = Active(e);
                        need(v.First(x => x.note == 60).column == 1 && v.First(x => x.note == 64).column == 2 && v.First(x => x.note == 67).column == 0, "Cursor column 2 did not start the spread: " + string.Join(",", v.Select(x => x.note + "@" + x.column)));
                    }
                });
                check("more keys than columns: the oldest live note in the track gives way", () =>
                {
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(3))))
                    {
                        foreach (int n in new[] { 60, 64, 67, 72 }) { e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, n, 100, 1)); e.Render(l, r, 64, 64); }
                        var v = Active(e).Where(x => !x.released).ToArray();
                        need(v.Length == 3, "Polyphony not bounded by columns: " + v.Length);
                        need(v.First(x => x.note == 72).column == 0 && !v.Any(x => x.note == 60), "Oldest note (60, column 1) was not the one replaced: " + string.Join(",", v.Select(x => x.note + "@" + x.column)));
                        e.LiveInput.Write(TrackerLiveEvent.NoteOff(60)); e.Render(l, r, 64, 64);
                        need(Active(e).Count(x => !x.released) == 3, "Key-up of a replaced note released another note");
                    }
                });
                check("per-note release leaves the other held notes sounding", () =>
                {
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(3))))
                    {
                        foreach (int n in new[] { 60, 64, 67 }) e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, n, 100, 1));
                        e.Render(l, r, 128, 128); e.LiveInput.Write(TrackerLiveEvent.NoteOff(64)); e.Render(l, r, 128, 128);
                        var v = Active(e); need(v.First(x => x.note == 64).released && !v.First(x => x.note == 60).released && !v.First(x => x.note == 67).released, "Wrong notes released");
                        e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 65, 100, 1)); e.Render(l, r, 64, 64);
                        need(Active(e).First(x => x.note == 65 && !x.released).column == 1, "Freed column 2 was not reused by the next key");
                    }
                });
                check("sustain pedal holds releases until it lifts", () =>
                {
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(3))))
                    {
                        e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 60, 100, 1)); e.LiveInput.Write(TrackerLiveEvent.Sustain(true)); e.LiveInput.Write(TrackerLiveEvent.NoteOff(60));
                        e.Render(l, r, 256, 256); need(!Active(e)[0].released && e.LiveInput.SustainDown, "Pedal did not hold the note");
                        e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 64, 100, 1)); e.Render(l, r, 64, 64);
                        need(Active(e).First(x => x.note == 64).column == 1, "A sustained note did not keep occupying its column");
                        e.LiveInput.Write(TrackerLiveEvent.Sustain(false)); e.Render(l, r, 64, 64);
                        var v = Active(e); need(v.First(x => x.note == 60).released && !v.First(x => x.note == 64).released, "Pedal up released the wrong notes");
                    }
                });
                check("all-notes-off releases, panic cuts at once, the song keeps playing", () =>
                {
                    var s = song(3); s.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { TrackerEngineCheck.Note(48, 2) } });
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(s)))
                    {
                        e.SendCommand(TrackerCommand.Play());
                        e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 60, 100, 1)); e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 64, 100, 1));
                        e.Render(l, r, 256, 256);
                        e.LiveInput.Write(TrackerLiveEvent.AllNotesOff()); e.Render(l, r, 64, 64);
                        need(Active(e).Where(x => x.note != 48).All(x => x.released), "All-notes-off left a live note held");
                        e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 62, 100, 1)); e.Render(l, r, 64, 64);
                        e.LiveInput.Write(TrackerLiveEvent.AllSoundOff()); e.Render(l, r, 64, 64);
                        need(!Active(e).Any(x => x.note == 62 || x.note == 60 || x.note == 64), "Panic did not cut live voices");
                        need(Active(e).Any(x => x.note == 48 && !x.released) && e.Snapshot.playing, "Panic touched the song's own note or stopped the song");
                    }
                });
                check("live note through a playing song's track: column cut is allowed, the song continues", () =>
                {
                    var s = song(2); s.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { TrackerEngineCheck.Note(48, 0) } });
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(s)))
                    {
                        e.SendCommand(TrackerCommand.Play()); e.Render(l, r, 1024, 256);
                        e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 1, 0, 72, 100, 1)); e.Render(l, r, 6000, 256);
                        var v = Active(e); need(v.Any(x => x.note == 48 && x.column == 0) && v.Any(x => x.note == 72 && x.column == 1), "Pattern and live notes not both sounding");
                        int rows = 0; while (e.ReadEvent(out var ev)) if (ev.kind == TrackerEventKind.Row) rows++;
                        need(rows >= 2 && e.Snapshot.playing, "Song transport did not continue");
                        e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 74, 100, 1)); e.Render(l, r, 64, 64);
                        need(Active(e).Any(x => x.note == 74 && x.column == 0), "Live note could not take the pattern column");
                        // Track mute applies to live notes because they play through the track.
                        e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.TrackMute, a = 0, b = 1 }); e.Render(l, r, 256, 256);
                        need(l.Take(256).All(x => x == 0), "Muted track still sounded");
                    }
                });
                check("pitch wheel bends only live notes, including their release", () =>
                {
                    var s = song(2); s.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { TrackerEngineCheck.Note(60, 0) } });
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(s)))
                    {
                        e.SendCommand(TrackerCommand.Play()); e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 1, 0, 60, 100, 1)); e.Render(l, r, 256, 256);
                        e.LiveInput.Write(TrackerLiveEvent.PitchBend(2)); e.Render(l, r, 64, 64);
                        var before = e.Snapshot.voices.ToArray(); e.Render(l, r, 1000, 1000); var after = e.Snapshot.voices.ToArray();
                        double Rate(int col) { int i = Array.FindIndex(before, v => v.active && v.column == col); return after[i].position - before[i].position; }
                        double ratio = Rate(1) / Rate(0); need(Math.Abs(ratio - Math.Pow(2, 2 / 12d)) < 1e-4, "Bend ratio " + ratio);
                        e.LiveInput.Write(TrackerLiveEvent.NoteOff(60)); e.Render(l, r, 64, 64);
                        before = e.Snapshot.voices.ToArray(); e.Render(l, r, 1000, 1000); after = e.Snapshot.voices.ToArray();
                        need(Math.Abs(Rate(1) / Rate(0) - Math.Pow(2, 2 / 12d)) < 1e-4, "Release tail lost the bend");
                    }
                });
                check("router: velocity curves, velocity-0 key-up, channel filter, octave shift, mod wheel and knobs to macros", () =>
                {
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(4))))
                    {
                        var router = new ZTrackerMidiRouter(); var sim = new ZTrackerMidiSimulated(); need(sim.Open(ZTrackerMidiSimulated.Device, router, out _), "Simulated open failed");
                        router.SetRoute(e.LiveInput, 0, 0, 0); router.VelocityCurve = ZTrackerVelocityCurve.Soft;
                        sim.NoteOn(60, 32); e.Render(l, r, 64, 64);
                        need(Math.Abs(Active(e)[0].volume - Math.Sqrt(32 / 127.0)) < 1e-5, "Soft curve gain " + Active(e)[0].volume);
                        sim.Send(0x90, 60, 0); e.Render(l, r, 64, 64); need(Active(e)[0].released, "Velocity 0 was not a key-up");
                        router.Channel = 3; sim.NoteOn(62, 100, 0); e.Render(l, r, 64, 64); need(!Active(e).Any(v => v.note == 62), "Channel filter let channel 1 through");
                        sim.NoteOn(62, 100, 3); e.Render(l, r, 64, 64); need(Active(e).Any(v => v.note == 62), "Channel 4 blocked");
                        router.Channel = -1; router.OctaveShift = -1; sim.NoteOn(70, 100); e.Render(l, r, 64, 64); need(Active(e).Any(v => v.note == 58), "Octave shift not applied");
                        router.OctaveShift = 0; sim.NoteOff(70); e.Render(l, r, 64, 64); need(Active(e).First(v => v.note == 58).released, "Key-up after an octave change missed the shifted note");
                        router.ModWheelMacro = 2; sim.Controller(1, 127); e.Render(l, r, 64, 64); need(Math.Abs(e.ObserveMacro(0, 2) - 1) < 1e-6, "Mod wheel did not reach macro 3");
                        sim.Controller(ZTrackerMidiRouter.KnobFirstController + 5, 64); e.Render(l, r, 64, 64); need(Math.Abs(e.ObserveMacro(0, 5) - 64 / 127f) < 1e-6, "Knob 6 did not reach macro 6");
                        long ignored = router.Ignored; sim.Send(0xC0, 5, 0); need(router.Ignored == ignored + 1, "Program change not ignored");
                        sim.Bend(16383); e.Render(l, r, 64, 64); // full up: +2 semitones by default
                    }
                });
                check("key-up follows its note to the renderer that played it after the target moves", () =>
                {
                    using (var a = new TrackerOffline(TrackerPreparedSong.Prepare(song(2))))
                    using (var b = new TrackerOffline(TrackerPreparedSong.Prepare(song(2))))
                    {
                        var router = new ZTrackerMidiRouter(); var sim = new ZTrackerMidiSimulated(); sim.Open(ZTrackerMidiSimulated.Device, router, out _);
                        router.SetRoute(a.LiveInput, 0, 0, 0); sim.NoteOn(60); router.SetRoute(b.LiveInput, 0, 0, 0); sim.NoteOn(64); sim.Controller(64, 127);
                        sim.NoteOff(60); a.Render(l, r, 64, 64); b.Render(l, r, 64, 64);
                        need(Active(a).Single().note == 60 && !Active(a).Single().released, "Pedal did not reach the previous target holding a note");
                        sim.Controller(64, 0); a.Render(l, r, 64, 64); need(Active(a).Single().released, "Key-up/pedal did not reach the first renderer");
                        need(Active(b).Single().note == 64 && !Active(b).Single().released, "Second renderer disturbed");
                        router.Panic(); a.Render(l, r, 64, 64); b.Render(l, r, 64, 64); need(Active(a).Length == 0 && Active(b).Length == 0, "Panic missed a renderer");
                    }
                });
                check("ring: order, overflow drops newest without blocking, lost key-up releases everything", () =>
                {
                    var ring = TrackerLiveRing.Create(8);
                    try
                    {
                        for (int i = 0; i < 8; i++) need(ring.TryWrite(TrackerLiveEvent.NoteOff(i)), "Write " + i + " refused");
                        need(!ring.TryWrite(TrackerLiveEvent.NoteOff(99)) && ring.Dropped == 1, "Overflow not refused/counted");
                        for (int i = 0; i < 8; i++) need(ring.TryRead(out var ev) && ev.note == i, "Order broken at " + i);
                        need(!ring.TryRead(out _), "Ring not empty");
                    }
                    finally { ring.Dispose(); }
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(3))))
                    {
                        e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 60, 100, 1)); e.Render(l, r, 64, 64);
                        var sw = Stopwatch.StartNew(); int refused = 0;
                        for (int i = 0; i < 1100; i++) if (!e.LiveInput.Write(TrackerLiveEvent.Sustain(true))) refused++;
                        need(sw.Elapsed.TotalMilliseconds < 50 && refused == 1100 - 1024 && e.LiveInput.Dropped == refused, "Full ring blocked or miscounted: " + refused);
                        e.Render(l, r, 64, 64); need(Active(e).All(v => v.released), "Dropped events did not release live notes");
                    }
                });
                check("ring: three producer threads and the renderer, every event exactly once", () =>
                {
                    var ring = TrackerLiveRing.Create(1024); var seen = new bool[3 * 20000 + 1]; int got = 0; long dropped;
                    try
                    {
                        var threads = Enumerable.Range(0, 3).Select(t => new Thread(() => { for (int i = 0; i < 20000; i++) { while (!ring.TryWrite(TrackerLiveEvent.NoteOff(t))) Thread.SpinWait(20); } })).ToArray();
                        foreach (var t in threads) t.Start();
                        var limit = Stopwatch.StartNew();
                        while (got < 60000 && limit.Elapsed.TotalSeconds < 20) { while (ring.TryRead(out var ev)) { need(!seen[ev.serial], "Duplicate serial " + ev.serial); seen[ev.serial] = true; got++; } }
                        foreach (var t in threads) t.Join(); dropped = ring.Dropped;
                    }
                    finally { ring.Dispose(); }
                    // Producers here retry on a full ring, so drops are counted but every serial must still arrive once.
                    need(got == 60000, "Consumed " + got);
                    report.AppendLine("MEASURE multiProducerEvents=60000 fullRingRetries=" + dropped);
                });
                check("zero allocation: router/input path, live writes and compiled rendering with live events", () =>
                {
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(4))))
                    {
                        var router = new ZTrackerMidiRouter(); router.SetRoute(e.LiveInput, 0, 0, 0);
                        long stamp = Stopwatch.GetTimestamp();
                        for (int i = 0; i < 200; i++) { router.Receive(0x90u | (uint)(60 + i % 12) << 8 | 100u << 16, stamp); router.Receive(0x80u | (uint)(60 + i % 12) << 8, stamp); router.Receive(0xE0u | 0x40u << 16, stamp); e.Render(l, r, 256, 256); }
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        for (int i = 0; i < 2000; i++) { router.Receive(0x90u | (uint)(60 + i % 12) << 8 | 100u << 16, stamp); router.Receive(0xB0u | 1u << 8 | (uint)(i & 127) << 16, stamp); router.Receive(0x80u | (uint)(60 + i % 12) << 8, stamp); }
                        long inputBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                        long managed = AudioThreadGuard.ManagedBlocks;
                        before = GC.GetAllocatedBytesForCurrentThread();
                        for (int i = 0; i < 100; i++) { router.Receive(0x90u | (uint)(48 + i % 24) << 8 | 90u << 16, stamp); router.Receive(0xE0u | (uint)(i & 127) << 16, stamp); e.Render(l, r, 512, 256); router.Receive(0x80u | (uint)(48 + i % 24) << 8, stamp); }
                        long renderBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                        need(e.Compiled, "Renderer ran managed");
                        need(AudioThreadGuard.ManagedBlocks == managed, "Managed render blocks");
                        report.AppendLine("MEASURE inputPathBytes=" + inputBytes + " (6000 messages) renderWithLiveBytes=" + renderBytes + " (200 blocks + 300 messages) dropped=" + e.LiveInput.Dropped);
                        need(inputBytes == 0, "Input path allocated " + inputBytes);
                        need(renderBytes == 0, "Render with live events allocated " + renderBytes);
                    }
                });
                check("latency: a key written before a block sounds from that block's first sample", () =>
                {
                    var onsets = new List<string>();
                    foreach (int block in new[] { 64, 256, 1024 })
                    {
                        using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(1))))
                        {
                            e.Render(l, r, block * 3, block);
                            need(l.Take(block * 3).All(x => x == 0), "Not silent before the key");
                            e.LiveInput.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 60, 100, 1)); e.Render(l, r, block, block);
                            int first = -1; for (int i = 0; i < block; i++) if (l[i] != 0) { first = i; break; }
                            need(first == 0, "Block " + block + ": first sound at sample " + first);
                            onsets.Add(block + ":" + first);
                        }
                    }
                    report.AppendLine("MEASURE onsetSampleInBlock " + string.Join(" ", onsets) + " (engine adds no delay inside the block; the wait is until the next block starts, at most one block)");
                });
                check("simulated source lifecycle: open, in use, unplug, closed door refuses writes", () =>
                {
                    var router = new ZTrackerMidiRouter(); var sim = new ZTrackerMidiSimulated();
                    need(sim.Open(ZTrackerMidiSimulated.Device, router, out _) && sim.IsOpen, "Open failed");
                    sim.Close(); sim.SimulateInUse = true; need(!sim.Open(ZTrackerMidiSimulated.Device, router, out var error) && error.Contains("In use") && !sim.IsOpen, "In-use not reported: " + error);
                    sim.SimulateInUse = false; sim.Open(ZTrackerMidiSimulated.Device, router, out _); sim.Unplugged = true;
                    need(sim.RefreshDevices() && !sim.IsOpen && sim.DeviceCount == 0, "Unplug did not close");
                    need(!sim.Open(ZTrackerMidiSimulated.Device, router, out error) && error == "Not found", "Unplugged device opened");
                    TrackerLiveTarget door;
                    using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(song(1)))) door = e.LiveInput;
                    need(!door.IsOpen && !door.Write(TrackerLiveEvent.NoteOn(0, 0, 0, 60, 100, 1)), "Closed door accepted a write");
                    router.SetRoute(door, 0, 0, 0); router.Receive(0x90u | 60u << 8 | 100u << 16, 0); // must be harmless
                    var windows = new ZTrackerMidiWindows(); report.AppendLine("MEASURE windowsMidiInputs=" + windows.DeviceCount + (windows.DeviceCount > 0 ? " first=\"" + windows.DeviceName(0) + "\"" : "")); windows.Dispose();
                });
            }
            finally { l.Dispose(); r.Dispose(); UnityEngine.Object.DestroyImmediate(instrument); UnityEngine.Object.DestroyImmediate(clip); }
            return "TrackerMidiCheck " + pass + " passed / " + fail + " failed\n" + report;
        }

        /// <summary>
        /// Arrival-to-applied latency on a live renderer (SAP host): a producer thread writes a key and stamps the time; a
        /// watcher thread spins on the renderer's applied-serial counter and stamps when the key was applied (start of the
        /// next audio block). Returns milliseconds per sample. The output device buffer comes on top of this.
        /// </summary>
        public static double[] MeasureAppliedLatency(TrackerLiveTarget door, int samples = 40, int note = 60)
        {
            var result = new List<double>();
            for (int i = 0; i < samples; i++)
            {
                var e = i % 2 == 0 ? TrackerLiveEvent.NoteOn(0, 0, 0, note, 90, .5f) : TrackerLiveEvent.NoteOff(note);
                long t0 = Stopwatch.GetTimestamp(); e.stamp = t0;
                if (!door.Write(e)) break;
                long serial = door.Written; var limit = Stopwatch.StartNew();
                while (door.LastAppliedSerial < serial && limit.ElapsedMilliseconds < 500) Thread.SpinWait(50);
                long t1 = Stopwatch.GetTimestamp();
                if (door.LastAppliedSerial >= serial) result.Add((t1 - t0) * 1000.0 / Stopwatch.Frequency);
                Thread.Sleep(3 + i % 7); // spread arrivals across the audio period
            }
            return result.ToArray();
        }
    }
}
