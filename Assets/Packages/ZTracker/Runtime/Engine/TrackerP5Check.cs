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
    /// <summary>Internal conformance fixtures for the proposed command profile. No authoring writes or audio device.</summary>
    public static unsafe class TrackerP5Check
    {
        public static CommandData Fx(string id, int value = 0, bool present = true) => new CommandData
        {
            present = true,
            valuePresent = present,
            identifier = id,
            value = value
        };
        static ColumnValue Col(string id) => new ColumnValue
        {
            kind = ValueKind.Command,
            command = Fx(id)
        };
        static void Need(bool ok, string why)
        {
            if (!ok)
                throw new Exception(why);
        }

        static bool Diagnostic(TrackerOffline e, TrackerRuntimeDiagnostic kind)
        {
            bool found = false;
            while (e.ReadEvent(out var ev))
                if (ev.kind == TrackerEventKind.Diagnostic && ev.payload == (int)kind)
                    found = true;
            return found;
        }

        static void Near(double a, double b, string why) => Need(Math.Abs(a - b) <= 1e-6, why + ": " + a + " expected " + b);
        sealed class Fixture : IDisposable
        {
            public ZTrackerInstrument instrument;
            public SongData song;
            public int block;
            public NativeArray<float> left, right;
            public Fixture(AudioClip pcm, int block)
            {
                this.block = block;
                instrument = TrackerEngineCheck.FixtureInstrument(pcm);
                instrument.model.provenance = "";
                instrument.model.sampler.samples[0].loop = SampleLoop.Forward;
                instrument.model.sampler.samples[0].loopStartFrame = 0;
                instrument.model.sampler.samples[0].loopEndFrame = pcm.samples;
                song = TrackerEngineCheck.FixtureSong(instrument, 2, 32);
                song.ticksPerLine = 4;
                left = new NativeArray<float>(32768, Allocator.Persistent);
                right = new NativeArray<float>(32768, Allocator.Persistent);
            }

            public NoteCell Note(int pitch = 60, int col = 0) => TrackerEngineCheck.Note(pitch, col);
            public void Row(int row, NoteCell note = null, params CommandData[] effects)
            {
                var line = new PatternLine
                {
                    line = row
                };
                if (note != null)
                    line.notes.Add(note);
                for (int i = 0; i < effects.Length; i++)
                    line.effects.Add(new EffectCell { column = i, command = effects[i] });
                song.patterns[0].tracks[0].WriteLine(line);
            }

            public TrackerOffline Open()
            {
                var e = new TrackerOffline(TrackerPreparedSong.Prepare(song));
                e.SendCommand(TrackerCommand.Play());
                return e;
            }

            public void Render(TrackerOffline e, int frames)
            {
                while (frames > 0)
                {
                    int n = Math.Min(frames, left.Length);
                    e.Render(left, right, n, block);
                    frames -= n;
                }
            }

            public void MacrosDevice(int ordinal = 1)
            {
                var d = new SourceDeviceData
                {
                    id = "macros",
                    ordinal = ordinal,
                    kind = SourceDeviceKind.InstrumentMacros,
                    instrumentId = instrument.model.id
                };
                for (int m = 0; m < 8; m++)
                    d.parameters.Add(new SourceParameterData { ordinal = m + 1, defaultValue = instrument.model.macros[m].value });
                song.tracks[0].sourceDevices.Add(d);
            }

            public AutomationLane Lane(int parameter = 0)
            {
                var lane = new AutomationLane
                {
                    id = "lane",
                    interpolation = AutomationInterpolation.Linear,
                    target = new ParameterTarget
                    {
                        kind = ParameterKind.Device,
                        deviceId = "macros",
                        index = parameter,
                        parameter = "slot"
                    }
                };
                lane.points.Add(new AutomationPoint { line = 0, value = 0 });
                lane.points.Add(new AutomationPoint { line = 1, value = 1 });
                song.patterns[0].tracks[0].automation.Add(lane);
                return lane;
            }

            public TrackerVoice Foreground(TrackerOffline e, int column = 0)
            {
                long cohort = e.Snapshot.columns[column].cohort;
                return e.Snapshot.voices.ToArray().First(v => v.active && v.cohort == cohort);
            }

            public void Dispose()
            {
                left.Dispose();
                right.Dispose();
                UnityEngine.Object.DestroyImmediate(instrument);
            }
        }

        static void OwnershipFixtures(AudioClip pcm, int block)
        {
            using (var f = new Fixture(pcm, block))
            {
                f.instrument.model.sampler.samples[0].nna = NewNoteAction.Continue;
                f.Row(0, f.Note());
                var next = f.Note(64);
                next.sampleFx = Fx("0S", 128);
                f.Row(1, next, Fx("0B", 0), Fx("0E", 128));
                using (var e = f.Open())
                {
                    f.Render(e, 6001);
                    var old = e.Snapshot.voices.ToArray().Single(v => v.active && v.note == 60);
                    Need(old.direction == 1 && !old.explicitStart && old.envelopePosition < .2, "same row static command touched tail");
                }

                next.instrument = 99;
                next.volume = new ColumnValue
                {
                    kind = ValueKind.Value,
                    value = 0
                };
                next.sampleFx = Fx("0U", 16);
                f.Row(1, next);
                f.Row(2, new NoteCell { note = NoteKind.Note, pitch = 67 });
                using (var e = f.Open())
                {
                    f.Render(e, 6001);
                    Near(f.Foreground(e).volume, 1, "failed launch applied setter");
                    Need(!f.Foreground(e).explicitStart && f.Foreground(e).commandPitch == 0, "failed launch inherited local commands");
                    Need(e.Snapshot.columns[0].instrument == 0, "failed selection replaced successful bank");
                    f.Render(e, 6000);
                    Need(f.Foreground(e).note == 67, "next empty instrument lost bank");
                }

                next.instrument = 0;
                next.sampleFx = Fx("0U", 16);
                next.volume = new ColumnValue
                {
                    kind = ValueKind.Value,
                    value = 0
                };
                f.instrument.model.sampler.zones[0].velocityMin = 1;
                f.Row(1, next);
                using (var e = f.Open())
                {
                    f.Render(e, 6001);
                    Need(f.Foreground(e).note == 60 && f.Foreground(e).commandPitch == 0 && f.Foreground(e).volume == 1, "unmapped successor mutated predecessor");
                }

                f.instrument.model.sampler.zones[0].velocityMin = 0;
                f.song.instruments.Add(f.instrument);
                next = f.Note(64);
                next.instrument = 1;
                next.sampleFx = Fx("0G", 16);
                f.Row(1, next);
                using (var e = f.Open())
                {
                    f.Render(e, 6001);
                    Need(f.Foreground(e).instrument == 1 && f.Foreground(e).note == 64, "changed instrument glide spliced held patch");
                    Need(Diagnostic(e, TrackerRuntimeDiagnostic.GlideInstrumentChanged), "changed instrument glide flag");
                }
            }
        }

        static void PitchFixtures(AudioClip pcm, int block)
        {
            using (var f = new Fixture(pcm, block))
            {
                f.Row(0, f.Note(), Fx("0D", 255));
                using (var e = f.Open())
                {
                    f.Render(e, 1);
                    var voice = f.Foreground(e);
                    voice.commandPitch = -1536;
                    var voices = e.Snapshot.voices;
                    voices[0] = voice;
                    f.Render(e, 5999);
                    Near(f.Foreground(e).commandPitch, -1536, "D saturation");
                    f.Row(1, null, Fx("0U", 255));
                }

                f.Row(0, f.Note(), Fx("0U", 255));
                using (var e = f.Open())
                {
                    f.Render(e, 1);
                    var v = f.Foreground(e);
                    v.commandPitch = -1536;
                    var voices = e.Snapshot.voices;
                    voices[0] = v;
                    f.Render(e, 5999);
                    Need(f.Foreground(e).commandPitch > -1536, "opposing slide recovery");
                }

                var layer = ZTrackerMigration.Copy(f.instrument.model.sampler.samples[0]);
                layer.id = "safety-layer";
                f.instrument.model.sampler.samples.Add(layer);
                var zone = ZTrackerMigration.Copy(f.instrument.model.sampler.zones[0]);
                zone.id = "safety-zone";
                zone.sample = 1;
                f.instrument.model.sampler.zones.Add(zone);
                f.Row(0, f.Note());
                using (var e = f.Open())
                {
                    f.Render(e, 1);
                    var v = e.Snapshot.voices[0];
                    v.commandPitch = float.NaN;
                    var voices = e.Snapshot.voices;
                    voices[0] = v;
                    f.Render(e, 1);
                    Need(!e.Snapshot.voices[0].active && e.Snapshot.voices[1].active, "NaN did not isolate component");
                    Need(f.left[0] == f.left[0], "NaN fetched PCM");
                }

                f.instrument.model.sampler.samples[0].fineTuneCents = float.MaxValue;
                f.instrument.model.sampler.samples[0].loopEndFrame = 2;
                using (var e = f.Open())
                {
                    f.Render(e, 100);
                    Need(e.Snapshot.voices.ToArray().Where(v => v.active).All(v => !double.IsNaN(v.position) && !double.IsInfinity(v.position) && v.step >= Math.Pow(2, -20) && v.step <= Math.Pow(2, 20)), "finite extreme/tiny loop increment");
                }
            }
        }

        static void RoutingFixtures(AudioClip pcm, int block)
        {
            using (var f = new Fixture(pcm, block))
            {
                f.song.tracks.Add(new TrackData { id = "a", kind = TrackKind.Group, visibleNoteColumns = 0, outputTrackId = "b" });
                f.song.tracks.Add(new TrackData { id = "b", kind = TrackKind.Group, visibleNoteColumns = 0, parentGroupId = "a", outputTrackId = "master" });
                f.song.tracks[0].outputTrackId = "a";
                f.song.patterns[0].tracks.Add(new PatternTrack { trackId = "a" });
                f.song.patterns[0].tracks.Add(new PatternTrack { trackId = "b" });
                f.song.patterns[0].tracks[3].WriteLine(new PatternLine { line = 0, effects = new List<EffectCell> { new EffectCell { command = Fx("0J", 255) } } });
                f.Row(0, f.Note());
                using (var e = f.Open())
                {
                    f.Render(e, 1);
                    Need(e.Snapshot.tracks[3].output == 1 && Diagnostic(e, TrackerRuntimeDiagnostic.RouteCycle), "current output ancestor cycle accepted");
                }

                f.song.patterns[0].tracks[3].lines.Clear();
                f.song.tracks[2].outputTrackId = "master";
                f.song.tracks[2].sends.Add(new SendDestination { trackId = "b" });
                f.song.tracks[3].kind = TrackKind.Send;
                f.song.tracks[3].parentGroupId = "";
                f.Row(0, f.Note());
                var n = f.Note(64);
                n.instrumentPresent = false;
                n.delayPresent = true;
                n.delay = 128;
                f.Row(1, n);
                f.song.patterns[0].tracks[3].WriteLine(new PatternLine { line = 1, effects = new List<EffectCell> { new EffectCell { command = Fx("0X", 0) } } });
                using (var e = f.Open())
                {
                    f.Render(e, 6001);
                    Need(!e.Snapshot.voices.ToArray().Any(v => v.active), "nested group send contributor not stopped");
                    Need(Diagnostic(e, TrackerRuntimeDiagnostic.StopExpandsSendContributors), "expanded send stop flag");
                    f.Render(e, 3000);
                    Need(f.Foreground(e).note == 64 && e.Snapshot.columns[0].instrument == 0, "X captured empty instrument lost");
                }
            }
        }

        static void DecodeFixtures(AudioClip pcm, int block)
        {
            using (var f = new Fixture(pcm, block))
            {
                f.MacrosDevice(18);
                f.song.tracks[0].sourceDevices[0].enabled = false;
                var malformed = Fx("0U", 16);
                malformed.rawValue = "GG";
                f.Row(0, f.Note(), Fx("I0", 1), malformed);
                string raw = ZTrackerMigration.Json(f.song);
                using (var p = TrackerPreparedSong.Prepare(f.song))
                    Need(p.diagnostics.Any(d => d.Contains("MALFORMED_VALUE") && d.Contains("sequence=0") && d.Contains("revision=")), "malformed raw diagnostic coordinates");
                Need(raw == ZTrackerMigration.Json(f.song), "source raw changed");
                using (var e = f.Open())
                {
                    f.Render(e, 1);
                    Need(e.Snapshot.devices[0].enabled, "I001 base36 enable");
                    Near(f.Foreground(e).commandPitch, 0, "GG guessed zero");
                }

                var scoped = Fx("0U", 16);
                scoped.scope = CommandScope.Global;
                f.Row(0, f.Note(), scoped);
                using (var p = TrackerPreparedSong.Prepare(f.song))
                    Need(p.diagnostics.Any(d => d.Contains("COMMAND_SCOPE_MISMATCH")), "source scope mismatch ignored");
                var row = new PatternLine
                {
                    line = 0,
                    notes = new List<NoteCell>
                    {
                        f.Note(60, 0),
                        f.Note(62, 1)
                    },
                    effects = new List<EffectCell>
                    {
                        new EffectCell
                        {
                            command = Fx("ZT", 144)
                        }
                    }
                };
                row.notes[0].pan = Col("M0");
                f.song.patterns[0].tracks[0].WriteLine(row);
                using (var e = f.Open())
                {
                    f.Render(e, 1);
                    Near(e.Snapshot.bpm, 144, "wrong column MIDI ate independent timing");
                }

                row.notes[0].pan = new ColumnValue();
                row.notes[1].pan = Col("M1");
                row.notes[1].instrumentPresent = false;
                row.effects.Add(new EffectCell { column = 1, command = Fx("0L", 0) });
                using (var p = TrackerPreparedSong.Prepare(f.song))
                {
                    Need(p.diagnostics.Any(d => d.Contains("MIDI_PAYLOAD_AMBIGUOUS")) && p.diagnostics.Any(d => d.Contains("MIDI_INSTRUMENT_REQUIRED")), "MIDI invalid context not preserved");
                }

                using (var e = f.Open())
                {
                    f.Render(e, 1);
                    Near(e.Snapshot.bpm, 120, "MIDI payload decoded as timing");
                    Near(e.Snapshot.tracks[0].preGain, 1, "MIDI payload decoded as L");
                }

                row.notes[1].pan = Col("M4");
                using (var p = TrackerPreparedSong.Prepare(f.song))
                    Need(!p.diagnostics.Any(d => d.Contains("MIDI_PRESERVED_PAYLOAD")), "M4 guessed MIDI");
                row.notes[1].pan = Col("M0");
                row.notes[1].instrumentPresent = true;
                row.effects.Clear();
                var omitted = Fx("01", 999, false);
                row.effects.Add(new EffectCell { command = omitted });
                string rawBefore = ZTrackerMigration.Json(omitted);
                using (var p = TrackerPreparedSong.Prepare(f.song))
                {
                    Need(p.diagnostics.Any(d => d.Contains("IMPLICIT_ZERO")), "omitted MIDI amount lost zero provenance");
                    Need(!p.diagnostics.Any(d => d.Contains("MIDI_PAYLOAD_MALFORMED")), "omitted MIDI amount rejected stale backing number");
                }

                Need(rawBefore == ZTrackerMigration.Json(omitted), "MIDI omission raw presence changed");
            }
        }

        static void AutomationFixtures(AudioClip pcm, int block)
        {
            using (var f = new Fixture(pcm, block))
            {
                f.MacrosDevice();
                var lane = f.Lane();
                var unsupported = ZTrackerMigration.Copy(lane);
                unsupported.id = "unsupported-neighbor";
                unsupported.scaling = .2f;
                unsupported.rawSource = "Lines scaling .2 source";
                f.song.patterns[0].tracks[0].automation.Add(unsupported);
                f.Row(0, f.Note(), Fx("11", 255));
                string raw = ZTrackerMigration.Json(unsupported);
                foreach (double quantum in new[]
                {
                    double.PositiveInfinity,
                    double.NaN
                }

                )
                {
                    var bad = ZTrackerMigration.Copy(lane);
                    bad.id = "bad-quantum";
                    bad.timeQuantum = quantum;
                    f.song.patterns[0].tracks[0].automation.Add(bad);
                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Any(d => d.Contains("AUTOMATION_LANE_UNSUPPORTED") && d.Contains("bad-quantum")), "nonfinite time quantum accepted");
                    f.song.patterns[0].tracks[0].automation.Remove(bad);
                }

                var malformedSource = new SourceDeviceData
                {
                    id = "null-parameters",
                    ordinal = 3,
                    kind = SourceDeviceKind.InstrumentMacros,
                    parameters = null
                };
                f.song.tracks[0].sourceDevices.Add(malformedSource);
                using (var p = TrackerPreparedSong.Prepare(f.song))
                    Need(p.diagnostics.Any(d => d.Contains("SOURCE_METADATA_MALFORMED")), "null source metadata threw or guessed");
                f.song.tracks[0].sourceDevices.Remove(malformedSource);
                using (var p = TrackerPreparedSong.Prepare(f.song))
                    Need(p.diagnostics.Any(d => d.Contains("AUTOMATION_LANE_UNSUPPORTED") && d.Contains("unsupported-neighbor")), "malformed neighbor rejected song");
                Need(raw == ZTrackerMigration.Json(unsupported), "unsupported lane mutation");
                var sourceParameter = f.song.tracks[0].sourceDevices[0].parameters[1];
                sourceParameter.units = "Hz";
                f.song.tracks[0].sourceDevices[0].parameters[2].quantum = 2;
                f.song.tracks[0].sourceDevices[0].parameters[3].lower = float.MaxValue;
                f.song.tracks[0].sourceDevices[0].parameters[3].quantum = 1e-38f;
                using (var p = TrackerPreparedSong.Prepare(f.song))
                    Need(p.diagnostics.Any(d => d.Contains("SOURCE_UNITS_MISMATCH")) && p.diagnostics.Any(d => d.Contains("SOURCE_DOMAIN_UNSUPPORTED")), "source units/quantized domain silently clamped");
                using (var e = f.Open())
                {
                    f.Render(e, 1501);
                    Near(e.ObserveMacro(0, 0), .25, "supported automation neighbor lost");
                }

                lane.interpolation = AutomationInterpolation.Step;
                using (var e = f.Open())
                {
                    f.Render(e, 4501);
                    Near(e.ObserveMacro(0, 0), 0, "Points stepped early");
                    f.Render(e, 1500);
                    Near(e.ObserveMacro(0, 0), 1, "Points endpoint");
                }

                f.song.tracks[0].devices.nodes.Add(new AudioEffectNodeData { uid = "delay", type = Laubrary.Zounds.ZoundEffectType.Delay, p = new[] { 10f, .4f, 1f, 500f, 0f } });
                var device = new SourceDeviceData
                {
                    id = "delay-source",
                    ordinal = 2,
                    kind = SourceDeviceKind.AudioChain
                };
                device.parameters.Add(new SourceParameterData { ordinal = 1, explicitEquivalence = true, units = "normalized", min = 0, max = .98f, target = new ParameterTarget { deviceId = "delay", index = 1 } });
                device.parameters.Add(new SourceParameterData { ordinal = 2, explicitEquivalence = true, units = "milliseconds", min = 10, max = 2000, target = new ParameterTarget { deviceId = "delay", index = 3 } });
                f.song.tracks[0].sourceDevices.Add(device);
                f.Row(0, f.Note(), Fx("21", 128), Fx("22", 255));
                using (var p = TrackerPreparedSong.Prepare(f.song))
                {
                    Need(p.diagnostics.Any(d => d.Contains("MEMORY_SIZING_TARGET")), "delay max resized live");
                    Need(p.state.deviceParameters.ToArray().Any(parameter => parameter.kind == 3 && parameter.parameter == 1), "valid feedback neighbor lost");
                }

                f.Row(1, null, Fx("20", 0));
                f.Row(2, null, Fx("20", 1));
                using (var e = f.Open())
                {
                    f.Render(e, 6001);
                    Need(!e.Snapshot.devices[1].enabled, "delay bypass missing");
                    var chain = e.Snapshot.chains[0];
                    Need(chain.processor.arena.ToArray().Skip(4).All(value => value == 0), "disabled delay history retained");
                    Need(e.Snapshot.devices[0].enabled, "unrelated macro source disabled");
                    f.Render(e, 6000);
                    Near(f.left[5999], 0, "re-enabled delay emitted old echo");
                    f.Render(e, 480);
                    Need(f.left[479] > 0, "re-enabled delay did not rebuild its new echo");
                }
            }
        }

        static void PresetFixture(AudioClip pcm, int block)
        {
            using (var f = new Fixture(pcm, block))
            {
                f.instrument.model.sampler.samples[0].nna = NewNoteAction.Continue;
                f.instrument.model.parameters.presets.Add(new ZTrackerInstrument.InstrumentPreset { ovrVolPan = true, volume = .25f, pan = 0 });
                f.Row(0, f.Note());
                var n = f.Note(64);
                n.parameterSetId = f.instrument.model.id + "/preset-0";
                f.Row(1, n);
                string before = ZTrackerMigration.Json(f.instrument.model);
                using (var e = f.Open())
                {
                    f.Render(e, 6001);
                    var active = e.Snapshot.voices.ToArray().Where(v => v.active).ToArray();
                    Need(active.Length == 2, "preset cloned slot/detached held voice");
                    Near(active.Single(v => v.note == 64).amplitude / active.Single(v => v.note == 60).amplitude, .25, "future preset parameter set");
                    Need(e.Snapshot.instrumentCount == 1, "preset canonical slot cloned");
                }

                Need(before == ZTrackerMigration.Json(f.instrument.model), "preset mutated authoring");
            }
        }

        public static string Execute()
        {
            int pass = 0, fail = 0;
            var report = new StringBuilder();
            var pcm = AudioClip.Create("P5 conformance", 48000, 1, 48000, false);
            var raw = new float[48000];
            for (int i = 0; i < raw.Length; i++)
                raw[i] = .1f;
            pcm.SetData(raw, 0);
            Action<string, Action<Fixture>> check = (name, run) =>
            {
                try
                {
                    foreach (int block in new[]
                    {
                        64,
                        333,
                        1024
                    }

                    )
                        using (var f = new Fixture(pcm, block))
                            run(f);
                    pass++;
                    report.AppendLine("PASS " + name + " [64/333/1024]");
                }
                catch (Exception ex)
                {
                    fail++;
                    report.AppendLine("FAIL " + name + ": " + ex.Message);
                }
            };
            try
            {
                check("01 decode omission and base36", f =>
                {
                    DecodeFixtures(pcm, f.block);
                    f.MacrosDevice(10);
                    var n = f.Note();
                    n.sampleFx = Fx("0U", 16);
                    f.Row(0, n);
                    n = new NoteCell
                    {
                        sampleFx = Fx("0U", -999, false)
                    };
                    f.Row(1, n, Fx("A1", 999, false));
                    using (var e = f.Open())
                    {
                        f.Render(e, 12000);
                        Near(f.Foreground(e).commandPitch, 2, "complete zero-repeat");
                        Near(e.ObserveMacro(0, 0), 0, "base36 parameter omitted zero");
                    }

                    using (var prepared = TrackerPreparedSong.Prepare(f.song))
                        Need(prepared.diagnostics.Count(d => d.Contains("IMPLICIT_ZERO")) == 2, "omission diagnostics");
                });
                check("02 numeric80 and malformed81", f =>
                {
                    var n = f.Note();
                    n.pan = new ColumnValue
                    {
                        kind = ValueKind.Value,
                        value = 64
                    };
                    f.Row(0, n);
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(f.Foreground(e).volume, 1, "gain80");
                        Near(f.Foreground(e).pan, 0, "pan40");
                    }

                    n.volume = new ColumnValue
                    {
                        kind = ValueKind.Legacy,
                        legacyValue = 129,
                        hasLegacy = true
                    };
                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(n.volume.legacyValue == 129, "raw81 lost");
                });
                check("03 Kahan clock stamps and million-row limits", f =>
                {
                    f.song.bpm = 137.5;
                    f.song.ticksPerLine = 6;
                    using (var e = f.Open())
                    {
                        f.Render(e, 63000);
                        var stamps = new List<long>();
                        while (e.ReadEvent(out var ev))
                            if (ev.kind == TrackerEventKind.Row)
                                stamps.Add(ev.samplePosition);
                        Need(stamps.Take(6).SequenceEqual(new long[] { 0, 5236, 10472, 15709, 20945, 26181 }) && stamps[11] == 57600 && stamps[12] == 62836, "Kahan frames");
                    }

                    long[] oracle = null;
                    foreach (int partition in new[]
                    {
                        1,
                        64,
                        257,
                        1024
                    }

                    )
                    {
                        var stamps = new long[1000000];
                        double start = 0, carry = 0, d = (48000 * 60d) / (137.5 * 4);
                        long callerEnd = partition;
                        for (int row = 0; row < stamps.Length; row++)
                        {
                            long deadline = TrackerRealtime.Quantize(start);
                            if (deadline >= callerEnd)
                                callerEnd = (deadline / partition + 1) * partition;
                            long callerStart = callerEnd - partition;
                            Need(deadline >= callerStart && deadline < callerEnd, "virtual callback deadline escaped its partition");
                            stamps[row] = callerStart + (deadline - callerStart);
                            Need(row == 0 || stamps[row] > stamps[row - 1], "million monotonic");
                            start = TrackerRealtime.ClockEnd(start, d, ref carry);
                        }

                        if (oracle == null)
                            oracle = stamps;
                        else
                            Need(oracle.SequenceEqual(stamps), "million shared clock partition");
                    }

                    Near(TrackerRealtime.Quantize(57600 - 1e-11), 57600, "ULP boundary snap");
                    Need(!TrackerRealtime.ClockValid(0, double.NaN, double.NaN, 0), "nonfinite clock accepted");
                    Need(!TrackerRealtime.ClockValid(1099511627775d, 1099511627777d, 2, 0), "clock 2^40 limit accepted");
                    using (var prepared = TrackerPreparedSong.Prepare(f.song))
                    {
                        prepared.state.authoredBpm = double.NaN;
                        using (var e = new TrackerOffline(prepared))
                        {
                            e.SendCommand(TrackerCommand.Play());
                            f.Render(e, 1);
                            Need(!e.Snapshot.playing && Diagnostic(e, TrackerRuntimeDiagnostic.InvalidClock), "invalid runtime clock did not stop before Q");
                        }
                    }

                    using (var shortClock = new Fixture(pcm, f.block))
                    {
                        shortClock.song.bpm = 999;
                        shortClock.song.linesPerBeat = 256;
                        shortClock.song.ticksPerLine = 16;
                        var later = shortClock.Note(60, 0);
                        later.delayPresent = true;
                        later.delay = 17;
                        later.sampleFx = Fx("0U", 16);
                        var earlier = shortClock.Note(62, 1);
                        earlier.delayPresent = true;
                        earlier.delay = 15;
                        earlier.sampleFx = Fx("0U", 16);
                        shortClock.Row(0, later);
                        shortClock.song.patterns[0].tracks[0].lines[0].notes.Add(earlier);
                        using (var e = shortClock.Open())
                        {
                            shortClock.Render(e, 11);
                            Near(shortClock.Foreground(e, 0).commandPitch, 14d / 16, "later exact deadline same Q frame");
                            Near(shortClock.Foreground(e, 1).commandPitch, 15d / 16, "earlier exact deadline same Q frame");
                            var launches = new List<int>();
                            while (e.ReadEvent(out var ev))
                                if (ev.kind == TrackerEventKind.NoteOn)
                                {
                                    Need(ev.samplePosition == 0, "same quantized frame");
                                    launches.Add(ev.column);
                                }

                            Need(launches.SequenceEqual(new[] { 1, 0 }), "exact deadline chronology before column ties");
                        }
                    }

                    using (var prepared = TrackerPreparedSong.Prepare(f.song))
                    {
                        prepared.state.authoredBpm = 1e-9;
                        using (var e = new TrackerOffline(prepared))
                        {
                            e.SendCommand(TrackerCommand.Play());
                            f.Render(e, 1);
                            Need(!e.Snapshot.playing && Diagnostic(e, TrackerRuntimeDiagnostic.ClockLimit), "live 2^40 guard did not stop before conversion");
                            e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 1 });
                            Need(!e.Snapshot.playing && Diagnostic(e, TrackerRuntimeDiagnostic.ClockLimit), "seek 2^40 guard did not stop before conversion");
                        }
                    }
                });
                check("04 pitch saturation and recovery", f =>
                {
                    PitchFixtures(pcm, f.block);
                    f.song.patterns[0].lineCount = 512;
                    for (int row = 0; row < 512; row++)
                        f.Row(row, row == 0 ? f.Note() : null, Fx("0U", 255));
                    var next = ZTrackerMigration.Copy(f.song.patterns[0]);
                    next.id = "continued";
                    next.tracks[0].lines[0].notes.Clear();
                    f.song.patterns.Add(next);
                    f.song.sequence.Add(new SequenceSlot { id = "continue", patternId = "continued" });
                    using (var e = f.Open())
                    {
                        f.Render(e, 772 * 6000);
                        var v = f.Foreground(e);
                        Near(v.commandPitch, 1536, "772 UFF bounded");
                        Need(v.active && v.pitchLimited && v.position >= 0 && v.position < 48000, "bounded loop cursor");
                        e.SendCommand(TrackerCommand.SetParameter(0, TrackerParameter.FineTune, 12000));
                        f.Render(e, 1);
                        Need(e.Snapshot.voices[0].active, "finite extreme pitch");
                        var ops = e.Snapshot.descriptors;
                        var op = ops[0];
                        op.kind = 'D';
                        ops[0] = op;
                        f.Render(e, 1500);
                        Need(f.Foreground(e).commandPitch < 1536, "UFF opposing recovery");
                    }

                    foreach (var pattern in f.song.patterns)
                        foreach (var line in pattern.tracks[0].lines)
                            foreach (var effect in line.effects)
                                effect.command = Fx("0D", 255);
                    using (var e = f.Open())
                    {
                        f.Render(e, 772 * 6000);
                        Near(f.Foreground(e).commandPitch, -1536, "772 DFF bounded");
                        Need(f.Foreground(e).step >= Math.Pow(2, -20) && f.Foreground(e).step <= Math.Pow(2, 20), "D increment bounds");
                    }
                });
                check("05 slide then glide tracker coordinate", f =>
                {
                    f.Row(0, f.Note(), Fx("0U", 32));
                    f.Row(1, new NoteCell { note = NoteKind.Note, pitch = 64 }, Fx("0G", 16));
                    f.Row(2, null, Fx("0G", 16));
                    using (var e = f.Open())
                    {
                        f.Render(e, 12000);
                        Near(f.Foreground(e).commandPitch, 3, "glide starts62 ends63");
                        f.Render(e, 6000);
                        Near(f.Foreground(e).commandPitch, 4, "glide reaches64");
                        Need(f.Foreground(e).note == 60, "immutable launch note");
                    }

                    var tuned = ZTrackerMigration.Copy(f.instrument.model.sampler.samples[0]);
                    tuned.id = "differently-tuned";
                    tuned.fineTuneCents = 1700;
                    f.instrument.model.sampler.samples[0].fineTuneCents = -350;
                    f.instrument.model.sampler.samples.Add(tuned);
                    var zone = ZTrackerMigration.Copy(f.instrument.model.sampler.zones[0]);
                    zone.id = "target-zone";
                    zone.sample = 1;
                    zone.noteMin = 64;
                    f.instrument.model.sampler.zones[0].noteMax = 63;
                    f.instrument.model.sampler.zones.Add(zone);
                    f.Row(1, new NoteCell { note = NoteKind.Note, pitch = 64 }, Fx("0G", 16), Fx("0A", 0x37), Fx("0V", 0x48));
                    using (var e = f.Open())
                    {
                        f.Render(e, 12000);
                        Near(f.Foreground(e).commandPitch, 3, "tuning and modulation changed glide coordinate");
                        Need(f.Foreground(e).sample == 0, "glide reselected tuned target zone");
                    }

                    f.Row(1, new NoteCell { note = NoteKind.Note, pitch = 64 }, Fx("0G", 255));
                    using (var e = f.Open())
                    {
                        f.Render(e, 6001);
                        Near(f.Foreground(e).commandPitch, 4, "GFF tracker coordinate");
                        Need(f.Foreground(e).sample == 0, "GFF reselected target zone");
                    }

                    f.Row(0, f.Note(64), Fx("0G", 16));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(f.Foreground(e).note == 64 && Diagnostic(e, TrackerRuntimeDiagnostic.GlideWithoutVoice), "G without a suitable foreground did not launch/flag");
                        Near(f.Foreground(e).commandPitch, 0, "G from silence invented pitch glide");
                    }
                });
                check("06 first tick and delayed slide", f =>
                {
                    var n = f.Note();
                    n.sampleFx = Fx("0U", 16);
                    f.Row(0, n);
                    using (var e = f.Open())
                    {
                        f.Render(e, 6000);
                        Near(f.Foreground(e).commandPitch, 1, "four active ticks");
                    }

                    n.delayPresent = true;
                    n.delay = 128;
                    using (var e = f.Open())
                    {
                        f.Render(e, 6000);
                        Near(f.Foreground(e).commandPitch, .5, "late two ticks");
                        var note = new List<long>();
                        while (e.ReadEvent(out var ev))
                            if (ev.kind == TrackerEventKind.NoteOn)
                                note.Add(ev.samplePosition);
                        Need(note.Single() == 3000, "exact delayed frame");
                    }
                });
                check("07 Q precedence plus fractional delay", f =>
                {
                    var n = f.Note();
                    n.volume = Col("Q1");
                    n.pan = Col("Q0");
                    n.delayPresent = true;
                    n.delay = 64;
                    f.Row(0, n, Fx("0Q", 2));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1501);
                        Need(f.Foreground(e).age == 1, "pan Q0 frame1500");
                    }

                    n.pan = new ColumnValue();
                    using (var e = f.Open())
                    {
                        f.Render(e, 3001);
                        Need(f.Foreground(e).age == 1, "volumeQ1 frame3000");
                    }

                    n.volume = Col("Q4");
                    using (var e = f.Open())
                    {
                        f.Render(e, 6000);
                        Need(!e.Snapshot.voices.ToArray().Any(v => v.active), "outside delay launches");
                    }
                });
                check("08 cut binding boundaries and OFF", f =>
                {
                    f.Row(0, f.Note());
                    var n = f.Note(64);
                    n.delayPresent = true;
                    n.delay = 128;
                    n.pan = Col("C1");
                    f.Row(1, n);
                    using (var e = f.Open())
                    {
                        f.Render(e, 12000);
                        Need(f.Foreground(e).note == 60, "cut waiting successor cut predecessor");
                    }

                    n.pan = new ColumnValue();
                    n.sampleFx = Fx("0C", 0xf1);
                    using (var e = f.Open())
                    {
                        f.Render(e, 10000);
                        Near(f.Foreground(e).commandGain, 1, "early C initializes successor");
                    }

                    f.Row(0, f.Note());
                    var off = new NoteCell
                    {
                        note = NoteKind.Off,
                        delayPresent = true,
                        delay = 64,
                        volume = Col("C2")
                    };
                    f.Row(1, off);
                    using (var e = f.Open())
                    {
                        f.Render(e, 7501);
                        Need(f.Foreground(e).released, "OFF frame7500");
                        f.Render(e, 1500);
                        Need(!e.Snapshot.voices.ToArray().Any(v => v.active), "OFF future cut lost captured cohort");
                    }

                    f.Row(1, new NoteCell { instrumentPresent = true, instrument = 0, delayPresent = true, delay = 64, volume = Col("C2") });
                    using (var e = f.Open())
                    {
                        f.Render(e, 9001);
                        Need(!e.Snapshot.voices.ToArray().Any(v => v.active), "instrument only future cut lost handle");
                    }

                    f.Row(1, new NoteCell { volume = Col("C4") });
                    using (var e = f.Open())
                    {
                        f.Render(e, 12000);
                        Need(f.Foreground(e).active && Diagnostic(e, TrackerRuntimeDiagnostic.CutOutsideRow), "C4 cut instead of outside-row refusal");
                    }
                });
                check("09 conflict families and valid writer precedence", f =>
                {
                    var n = f.Note();
                    n.sampleFx = Fx("0U", 48);
                    n.volume = Col("D2");
                    n.pan = Col("G1");
                    f.Row(0, n, Fx("0D", 32), Fx("ZT", 144), Fx("ZT", 160), Fx("ZT", 24));
                    f.song.patterns[0].tracks[0].lines[0].notes.Add(f.Note(64, 1));
                    f.song.patterns[0].tracks[1].WriteLine(new PatternLine { line = 0, effects = new List<EffectCell> { new EffectCell { command = Fx("0U", 16) } } });
                    using (var e = f.Open())
                    {
                        f.Render(e, 4500);
                        Near(e.Snapshot.bpm, 160, "last timing writer");
                        Near(f.Foreground(e).commandPitch, 0, "G suppresses U/D");
                        Near(f.Foreground(e, 1).commandPitch, -2, "other column follows track D20");
                        Need(e.Snapshot.commandMemory.ToArray().Count(v => v > 0) >= 4, "suppressed source memory not resolved");
                    }
                });
                check("10 complete memory banks and LFO blank freeze", f =>
                {
                    f.Row(0, f.Note(), Fx("0V", 0x47));
                    f.Row(1, null, Fx("0V", 0));
                    f.Row(2, null, Fx("0V", 0x30));
                    using (var e = f.Open())
                    {
                        f.Render(e, 18000);
                        Near(f.Foreground(e).commandVibrato, 0, "V30 depth zero");
                        float phase = f.Foreground(e).phaseV;
                        f.Render(e, 6000);
                        Near(f.Foreground(e).phaseV, phase, "blank phase freeze");
                        Near(f.Foreground(e).commandVibrato, 0, "blank neutral");
                    }

                    f.song.patterns[0].tracks[0].lines.Clear();
                    var memory = f.Note();
                    memory.volume = Col("U2");
                    f.Row(0, memory);
                    f.Row(1, new NoteCell { sampleFx = Fx("0U", 0) });
                    using (var e = f.Open())
                    {
                        f.Render(e, 12000);
                        Near(f.Foreground(e).commandPitch, 2, "local zero borrowed volume bank");
                        Need(Diagnostic(e, TrackerRuntimeDiagnostic.EmptyMemory), "independent local memory missing flag");
                    }
                });
                check("11 fresh note, pause, stop resets", f =>
                {
                    var n = f.Note();
                    n.volume = new ColumnValue
                    {
                        kind = ValueKind.Value,
                        value = 32
                    };
                    n.pan = new ColumnValue
                    {
                        kind = ValueKind.Value,
                        value = 0
                    };
                    f.Row(0, n, Fx("0U", 16));
                    f.Row(1, f.Note(64));
                    using (var e = f.Open())
                    {
                        f.Render(e, 6001);
                        Near(f.Foreground(e).volume, 1, "fresh gain");
                        Near(f.Foreground(e).pan, 0, "fresh pan");
                        Near(f.Foreground(e).commandPitch, 0, "fresh pitch");
                        long age = f.Foreground(e).age, pos = e.Snapshot.samplePosition;
                        e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Pause });
                        f.Render(e, 6000);
                        Need(e.Snapshot.samplePosition == pos && f.Foreground(e).age == age, "pause advances");
                        e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Resume });
                        f.Render(e, 1);
                        Need(f.Foreground(e).age == age + 1, "resume");
                        e.SendCommand(TrackerCommand.Stop());
                        Need(e.Snapshot.commandMemory.ToArray().All(x => x == 0) && e.Snapshot.columns[0].instrument == -1, "stop state retained");
                    }

                    f.song.patterns[0].lineCount = 1;
                    f.song.patterns[0].tracks[0].lines.RemoveAll(line => line.line >= 1);
                    var nextPattern = ZTrackerMigration.Copy(f.song.patterns[0]);
                    nextPattern.id = "memory-boundary";
                    nextPattern.tracks[0].lines.Clear();
                    var nextNote = f.Note(64);
                    nextNote.sampleFx = Fx("0U", 0);
                    nextPattern.tracks[0].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { nextNote } });
                    f.song.patterns.Add(nextPattern);
                    f.song.sequence.Add(new SequenceSlot { id = "memory-boundary-slot", patternId = nextPattern.id });
                    n.sampleFx = Fx("0U", 16);
                    f.Row(0, n);
                    using (var e = f.Open())
                    {
                        f.Render(e, 6001);
                        Near(f.Foreground(e).commandPitch, .25, "new note local memory across pattern boundary");
                        Need(!Diagnostic(e, TrackerRuntimeDiagnostic.EmptyMemory), "pattern cleared local memory");
                    }
                });
                check("12 layered NNA foreground and tail isolation", f =>
                {
                    OwnershipFixtures(pcm, f.block);
                    var s = ZTrackerMigration.Copy(f.instrument.model.sampler.samples[0]);
                    s.id = "layer";
                    s.nna = NewNoteAction.NoteOff;
                    f.instrument.model.sampler.samples[0].nna = NewNoteAction.Continue;
                    f.instrument.model.sampler.samples.Add(s);
                    var z = ZTrackerMigration.Copy(f.instrument.model.sampler.zones[0]);
                    z.id = "layer-zone";
                    z.sample = 1;
                    f.instrument.model.sampler.zones.Add(z);
                    f.Row(0, f.Note());
                    var n = f.Note(64);
                    n.delayPresent = true;
                    n.delay = 128;
                    n.sampleFx = Fx("0U", 16);
                    f.Row(1, n);
                    using (var e = f.Open())
                    {
                        f.Render(e, 9001);
                        Need(e.Snapshot.voices.ToArray().Count(v => v.active) == 4, "layers not retained");
                        f.Render(e, 3000);
                        Need(e.Snapshot.voices.ToArray().Where(v => v.active && v.note == 60).All(v => v.commandPitch == 0), "detached tails changed");
                    }

                    n.pan = Col("Y0");
                    using (var e = f.Open())
                    {
                        f.Render(e, 9001);
                        var held = e.Snapshot.voices.ToArray().Where(v => v.active).ToArray();
                        Need(held.Length == 2 && held.All(v => v.note == 60 && !v.released && v.commandPitch == 0), "rejected delayed layered successor changed predecessor");
                    }

                    f.instrument.model.sampler.samples[0].oneShot = true;
                    f.Row(1, new NoteCell { note = NoteKind.Off });
                    using (var e = f.Open())
                    {
                        f.Render(e, 6001);
                        var held = e.Snapshot.voices.ToArray().Where(v => v.active).ToArray();
                        Need(held.Length == 2 && !held.First(v => v.sample == 0).released && held.First(v => v.sample == 1).released, "OFF ignored individual one-shot policy");
                    }

                    f.instrument.model.sampler.samples[0].muteGroup = 1;
                    f.instrument.model.sampler.samples[1].muteGroup = 1;
                    f.song.tracks.Insert(1, new TrackData { id = "other-mute-domain", visibleNoteColumns = 1 });
                    f.song.patterns[0].tracks.Add(new PatternTrack { trackId = "other-mute-domain" });
                    f.song.patterns[0].tracks[2].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { f.Note() } });
                    f.Row(0, f.Note());
                    f.Row(1, f.Note(64, 1));
                    using (var e = f.Open())
                    {
                        f.Render(e, 6001);
                        var voices = e.Snapshot.voices.ToArray().Where(v => v.active).ToArray();
                        Need(voices.Length == 4 && voices.Count(v => v.track == 1 && v.note == 60) == 2 && !voices.Any(v => v.track == 0 && v.note == 60), "layer mute group failed same-track tail choke/other-track survival");
                    }
                });
                check("13 V T N independent formulas", f =>
                {
                    f.Row(0, f.Note(), Fx("0V", 0x48), Fx("0T", 0x48), Fx("0N", 0x48));
                    using (var e = f.Open())
                    {
                        f.Render(e, 3001);
                        var v = f.Foreground(e);
                        Near(v.commandVibrato, .5 * Math.Sin(Math.PI / 4), "vibrato");
                        Near(v.commandTremolo, 1 - (8d / 15) * (1 - Math.Cos(Math.PI / 4)) / 2, "tremolo");
                        Near(v.commandPan, (8d / 15) * Math.Sin(Math.PI / 4), "autopan");
                        f.Render(e, 3000);
                        var blank = f.Foreground(e);
                        Near(blank.commandVibrato, 0, "blank V neutral");
                        Near(blank.commandTremolo, 1, "blank T neutral");
                        Near(blank.commandPan, 0, "blank N neutral");
                    }
                });
                check("14 arpeggio and additive gain", f =>
                {
                    f.Row(0, f.Note(), Fx("0A", 0x37), Fx("0C", 0x80), Fx("0O", 16));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1501);
                        Near(f.Foreground(e).commandArp, 3, "arp phase1");
                        f.Render(e, 1500);
                        Near(f.Foreground(e).commandArp, 7, "arp phase2");
                        f.Render(e, 1500);
                        Near(f.Foreground(e).commandArp, 0, "arp phase0");
                        Near(f.Foreground(e).commandGain, 8d / 15 - 1d / 16, "additive O");
                    }

                    f.song.patterns[0].lineCount = 257;
                    f.song.patterns[0].tracks[0].lines.Clear();
                    f.Row(0, f.Note(), Fx("0C", 0));
                    for (int row = 1; row <= 256; row++)
                        f.Row(row, null, Fx("0I", 1));
                    using (var e = f.Open())
                    {
                        f.Render(e, 257 * 6000);
                        Near(f.Foreground(e).commandGain, 1, "256 additive I01 rows");
                    }
                });
                check("15 slices offsets and reverse", f =>
                {
                    var sample = f.instrument.model.sampler.samples[0];
                    sample.sliceMarkers = new List<int>
                    {
                        0,
                        200,
                        600
                    };
                    f.Row(0, f.Note(), Fx("0B", 0), Fx("0S", 2));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        var v = e.Snapshot.voices[0];
                        Need(v.regionStart == 200 && v.regionEnd == 600 && v.position < 200, "slice region reverse starts marker");
                    }

                    sample.sliceMarkers.Clear();
                    f.Row(0, f.Note(), Fx("0S", 128));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(f.Foreground(e).selectedStart == 24000, "half PCM");
                    }

                    sample.sliceMarkers = new List<int>
                    {
                        0,
                        200,
                        600
                    };
                    f.Row(0, f.Note(), Fx("0S", 4));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(f.Foreground(e).selectedStart == 0 && Diagnostic(e, TrackerRuntimeDiagnostic.SliceOutside), "S04 guessed seek");
                    }

                    sample.sliceMarkers.Clear();
                    f.Row(0, f.Note());
                    f.Row(1, null, Fx("0B", 0));
                    using (var e = f.Open())
                    {
                        f.Render(e, 6000);
                        double cursor = f.Foreground(e).position;
                        f.Render(e, 1);
                        Need(f.Foreground(e).direction == -1 && f.Foreground(e).position < cursor, "held B reflected instead of reversing next movement");
                    }

                    var shortPcm = AudioClip.Create("p5-exact-offset", 1000, 1, 48000, false);
                    try
                    {
                        using (var exact = new Fixture(shortPcm, f.block))
                        {
                            exact.Row(0, exact.Note(), Fx("0S", 128));
                            using (var e = exact.Open())
                            {
                                exact.Render(e, 1);
                                Need(exact.Foreground(e).selectedStart == 500, "S80 N1000 cursor");
                            }
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(shortPcm);
                    }

                    f.instrument.model.sampler.zones[0].blend = new SampleBlendExtension
                    {
                        pcmB = pcm,
                        amount = .5f,
                        baseNoteB = 60
                    };
                    f.Row(0, f.Note(), Fx("0B", 0), Fx("0S", 128));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(f.Foreground(e).directionB == -1 && f.Foreground(e).positionB < 24000, "paired B direction/selected offset");
                    }

                    f.instrument.model.family = InstrumentFamily.Synth;
                    f.instrument.model.modulation.Clear();
                    f.instrument.model.sampler.samples[0].modulationSet = -1;
                    f.Row(0, f.Note(), Fx("0S", 128));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(f.Foreground(e).selectedStart == 0 && Diagnostic(e, TrackerRuntimeDiagnostic.SampleDomainUnsupported), "Synth S80 guessed oscillator seek");
                    }
                });
                check("16 E independent clocks and Stepper preservation", f =>
                {
                    var set = f.instrument.model.modulation[0];
                    set.devices[0].attack = .2f;
                    set.devices[0].hold = .1f;
                    set.devices[0].decay = .3f;
                    set.devices.Add(new ModulationDevice { kind = ModulationDeviceKind.Multipoint, target = ModulationTarget.Pan, points = new List<ModulationPoint> { new ModulationPoint { time = 0, value = 0 }, new ModulationPoint { time = 2, value = 1 } } });
                    set.devices.Add(new ModulationDevice { kind = ModulationDeviceKind.Fader, target = ModulationTarget.Pitch, duration = 4, min = 0, max = 1 });
                    set.devices.Add(new ModulationDevice { kind = ModulationDeviceKind.Stepper, target = ModulationTarget.Pitch });
                    f.Row(0, f.Note(), Fx("0E", 128));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(f.Foreground(e).envelopePosition, .3 + 1d / 48000, "held AHDSR");
                        Near(e.Snapshot.modulationState[1].position, 1 + 1d / 48000, "multi point");
                        Near(e.Snapshot.modulationState[2].position, 2 + 1d / 48000, "fader");
                        Near(e.Snapshot.modulationState[3].position, 0, "Stepper unchanged");
                    }

                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Any(d => d.Contains("STEPPER_PRESERVED") && d.Contains("Pitch")), "Stepper destination flag");
                    f.Row(1, null, Fx("0E", 128));
                    using (var e = f.Open())
                    {
                        f.Render(e, 6000);
                        var finished = e.Snapshot.modulationState[1];
                        finished.finished = true;
                        finished.position = 2;
                        var clocks = e.Snapshot.modulationState;
                        clocks[1] = finished;
                        f.Render(e, 1);
                        Need(e.Snapshot.modulationState[1].finished, "E reactivated a finished clock");
                        Near(e.Snapshot.modulationState[1].position, 2, "E rewound a finished clock");
                    }
                });
                check("17 retrigger interval boundaries", f =>
                {
                    f.song.ticksPerLine = 12;
                    f.Row(0, f.Note(), Fx("0R", 4));
                    using (var e = f.Open())
                    {
                        f.Render(e, 4001);
                        Need(f.Foreground(e).age == 1, "retriggers at4 and8");
                        f.Render(e, 1999);
                        Need(f.Foreground(e).age == 2000, "no tick12 retrigger");
                    }

                    f.Row(0, f.Note(), Fx("0R", 0));
                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Any(d => d.Contains("ZERO_RETRIGGER_INTERVAL")), "R00 not retained");
                    f.Row(0, f.Note());
                    f.Row(1, null, Fx("0R", 4));
                    using (var e = f.Open())
                    {
                        f.Render(e, 8001);
                        Need(f.Foreground(e).age == 1, "track R04 did not reset at tick4");
                    }

                    f.Row(1, new NoteCell { volume = Col("R4") });
                    using (var e = f.Open())
                    {
                        f.Render(e, 8000);
                        double cursor = f.Foreground(e).position;
                        f.Render(e, 1);
                        Need(f.Foreground(e).age == 1 && f.Foreground(e).position > cursor, "column R4 cursor retained");
                    }

                    var late = f.Note();
                    late.delayPresent = true;
                    late.delay = 149;
                    f.Row(0, late, Fx("0R", 4));
                    using (var e = f.Open())
                    {
                        f.Render(e, 4001);
                        Need(f.Foreground(e).age == 1, "late note retrigger only k8");
                    }
                });
                check("18 retrigger factors", f =>
                {
                    int[] x =
                    {
                        1,
                        6,
                        7,
                        14,
                        15
                    };
                    double[] expected =
                    {
                        .47,
                        .335,
                        .25,
                        .75,
                        1
                    };
                    for (int i = 0; i < x.Length; i++)
                        Near(TrackerRealtime.RetriggerFactor(.5f, x[i]), expected[i], "factor" + x[i]);
                });
                check("19 deterministic random and exclusive weights", f =>
                {
                    Need(TrackerRealtime.CounterHash(0, 0, 0, 0, 0, 0, ulong.MaxValue, 3) == 0x75b233c5098d5a1bUL, "hash oracle");
                    Need(TrackerRealtime.CounterHash(0, 0, 0, 0, 1, 0, ulong.MaxValue, 3) == 0x352f77727ec8a36dUL, "occurrence oracle");
                    Need(TrackerRealtime.CounterHash(0, 0, 0, 0, 0, 0, ulong.MaxValue, 1) == 0x0525cff84fcbb659UL, "track purpose oracle");
                    Need(TrackerRealtime.CounterHash(0, 0, 0, 0, 0, 0, 0, 2) == 0xa889db176a5b02b5UL, "column purpose oracle");
                    var row = new PatternLine
                    {
                        line = 0
                    };
                    for (int c = 0; c < 2; c++)
                    {
                        var n = f.Note(60, c);
                        n.pan = Col(c == 0 ? "Y0" : "Y1");
                        row.notes.Add(n);
                    }

                    row.effects.Add(new EffectCell { command = Fx("0Y", 0) });
                    f.song.patterns[0].tracks[0].WriteLine(row);
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(e.Snapshot.voices.ToArray().Count(v => v.active) == 1 && f.Foreground(e, 1).column == 1, "exclusive weights");
                    }

                    f.song.tracks[0].visibleNoteColumns = 3;
                    row.notes.Clear();
                    for (int c = 0; c < 3; c++)
                    {
                        var n = f.Note(60, c);
                        n.pan = Col(c == 0 ? "Y0" : c == 1 ? "Y1" : "Y2");
                        row.notes.Add(n);
                    }

                    f.song.patterns[0].lineCount = 1;
                    f.song.patterns[0].tracks[0].WriteLine(row);
                    using (var e = f.Open())
                    {
                        e.SendCommand(TrackerCommand.Play(true));
                        f.Render(e, 1);
                        Need(f.Foreground(e, 2).column == 2, "first occurrence bucket23");
                        f.Render(e, 6000);
                        Need(f.Foreground(e, 1).column == 1, "second occurrence bucket10");
                    }

                    foreach (var n in row.notes)
                        n.pan = Col("Y0");
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(!e.Snapshot.voices.ToArray().Any(v => v.active) && Diagnostic(e, TrackerRuntimeDiagnostic.ExclusiveNoWeight), "all zero exclusive");
                    }

                    row.effects.Clear();
                    foreach (var n in row.notes)
                        n.pan = Col("YF");
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(e.Snapshot.voices.ToArray().Count(v => v.active) == 3, "YF always");
                    }

                    foreach (var n in row.notes)
                        n.pan = Col("Y0");
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(!e.Snapshot.voices.ToArray().Any(v => v.active), "ordinary Y0 never");
                    }

                    foreach (var n in row.notes)
                        n.pan = Col("YF");
                    row.effects.Add(new EffectCell { command = Fx("0Y", 255) });
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(e.Snapshot.voices.ToArray().Count(v => v.active) == 3, "YFF track always");
                    }

                    row.effects[0].command = Fx("0Y", 0);
                    row.notes[0].pan = Col("Y0");
                    row.notes[1].pan = Col("Y1");
                    row.notes[2].pan = Col("Y2");
                    row.notes[2].sampleFx = Fx("0C", 1);
                    using (var e = f.Open())
                    {
                        f.Render(e, 1501);
                        Need(!e.Snapshot.voices.ToArray().Any(v => v.active && v.column != 2), "exclusive gate reselected after selected bundle cut");
                        Near(f.Foreground(e, 2).commandGain, 0, "selected bundle volume cut");
                    }
                });
                check("20 M L P curves and synth domain", f =>
                {
                    f.Row(0, f.Note(), Fx("0M", 0), Fx("0L", 0), Fx("0P", 128));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.Snapshot.tracks[0].instrumentGain, .001, "M00");
                        Near(e.Snapshot.tracks[0].preGain, 0, "L00");
                        Near(e.Snapshot.tracks[0].prePan, 0, "P80");
                    }

                    f.Row(0, f.Note(), Fx("0M", 255), Fx("0L", 255));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.Snapshot.tracks[0].instrumentGain, Math.Pow(10, .15), "MFF");
                        Near(e.Snapshot.tracks[0].preGain, Math.Pow(10, .15), "LFF");
                    }

                    foreach (int value in new[]
                    {
                        1,
                        128
                    }

                    )
                    {
                        f.Row(0, f.Note(), Fx("0L", value));
                        using (var e = f.Open())
                        {
                            f.Render(e, 1);
                            Near(e.Snapshot.tracks[0].preGain, Math.Pow(10, (-60 + 63 * (value - 1) / 254d) / 20), "L interior" + value);
                        }
                    }

                    foreach (int value in new[]
                    {
                        0,
                        64,
                        128,
                        192,
                        255
                    }

                    )
                    {
                        f.Row(0, f.Note(), Fx("0P", value));
                        using (var e = f.Open())
                        {
                            f.Render(e, 1);
                            Near(e.Snapshot.tracks[0].prePan, (value - 128) / (value <= 128 ? 128d : 127d), "P interior" + value);
                        }
                    }

                    f.instrument.model.family = InstrumentFamily.Synth;
                    f.instrument.model.modulation.Clear();
                    f.instrument.model.sampler.samples[0].modulationSet = -1;
                    f.Row(0, f.Note(), Fx("0U", 16));
                    using (var e = f.Open())
                    {
                        f.Render(e, 6000);
                        Near(f.Foreground(e).commandPitch, 0, "synth domain unchanged");
                        Need(Diagnostic(e, TrackerRuntimeDiagnostic.SampleDomainUnsupported), "synth domain flag");
                    }
                });
                check("21 X00 preserves entering row notes", f =>
                {
                    f.Row(0, f.Note());
                    var n = f.Note(64);
                    n.delayPresent = true;
                    n.delay = 128;
                    f.Row(1, n, Fx("0X", 0));
                    using (var e = f.Open())
                    {
                        f.Render(e, 7000);
                        Need(!e.Snapshot.voices.ToArray().Any(v => v.active), "old voices not cut");
                        f.Render(e, 2001);
                        Need(f.Foreground(e).note == 64, "entering delayed launch lost");
                    }

                    n.delayPresent = false;
                    using (var e = f.Open())
                    {
                        f.Render(e, 6001);
                        Need(f.Foreground(e).note == 64 && e.Snapshot.voices.ToArray().Count(v => v.active) == 1, "X00 immediate entering row note lost");
                    }

                    f.Row(1, null, Fx("0X", 1));
                    using (var e = f.Open())
                    {
                        f.Render(e, 6001);
                        Need(f.Foreground(e).note == 60, "X01 guessed sustained/device stop");
                    }

                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Any(d => d.Contains("UNSUPPORTED")), "X01 not preserved and flagged");
                    using (var shared = new Fixture(pcm, f.block))
                    {
                        shared.song.tracks.Insert(1, new TrackData { id = "unrelated", visibleNoteColumns = 1 });
                        shared.song.patterns[0].tracks.Add(new PatternTrack { trackId = "unrelated" });
                        shared.song.tracks[2].devices.nodes.Add(new AudioEffectNodeData { uid = "shared-delay", type = Laubrary.Zounds.ZoundEffectType.Delay, p = new[] { 10f, .5f, .5f, 500f, 0f } });
                        shared.song.patterns[0].tracks[2].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { shared.Note(67) } });
                        shared.Row(0, shared.Note());
                        shared.Row(1, null, Fx("0X", 0));
                        using (var e = shared.Open())
                        {
                            shared.Render(e, 6001);
                            Need(e.Snapshot.voices.ToArray().Any(v => v.active && v.track == 1), "X00 stopped unrelated track");
                            Need(Diagnostic(e, TrackerRuntimeDiagnostic.SharedFxStop), "inseparable downstream history not flagged");
                            bool retainedHistory = false;
                            for (int ci = 0; ci < e.Snapshot.chainCount; ci++)
                                retainedHistory |= e.Snapshot.chains[ci].processor.arena.ToArray().Any(v => v != 0);
                            Need(retainedHistory, "X00 cleared shared downstream history");
                        }
                    }
                });
                check("22 unsupported hardware width phrase groove", f =>
                {
                    RoutingFixtures(pcm, f.block);
                    f.Row(0, f.Note(), Fx("0J", 1), Fx("0W", 255), Fx("0Z", 1), Fx("ZG", 1));
                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Count >= 4, "unsupported source not diagnosed");
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Need(e.Snapshot.tracks[0].output == 1 && e.Snapshot.bpm == 120, "unsupported writes graph");
                    }

                    foreach (var command in new[]
                    {
                        Fx("0W", 0),
                        Fx("0W", 255),
                        Fx("0Z", 0),
                        Fx("0Z", 1),
                        Fx("ZG", 1),
                        Fx("0J", 1)
                    }

                    )
                    {
                        command.rawIdentifier = command.identifier;
                        command.rawValue = command.value.ToString("X2");
                        string before = ZTrackerMigration.Json(command);
                        f.Row(0, f.Note(), command);
                        using (var e = f.Open())
                        {
                            f.Render(e, 1);
                            Need(e.Snapshot.tracks[0].output == 1 && e.Snapshot.bpm == 120, "unsupported context changed graph/clock");
                        }

                        Need(before == ZTrackerMigration.Json(command), "unsupported command raw payload changed");
                    }

                    var phraseOffset = Fx("0S", 32);
                    phraseOffset.unsupported = true;
                    phraseOffset.diagnostic = "PHRASE_CONTEXT_UNSUPPORTED";
                    phraseOffset.sourcePayload = "phrase S20";
                    string phraseBefore = ZTrackerMigration.Json(phraseOffset);
                    f.Row(0, f.Note(), phraseOffset);
                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Any(d => d.Contains("PHRASE_CONTEXT_UNSUPPORTED")), "phrase offset diagnostic lost");
                    Need(phraseBefore == ZTrackerMigration.Json(phraseOffset), "phrase offset payload changed");
                });
                check("23 ZD and raw ZB row-end flow", f =>
                {
                    var p = ZTrackerMigration.Copy(f.song.patterns[0]);
                    p.id = "next";
                    f.song.patterns.Add(p);
                    f.song.sequence.Add(new SequenceSlot { id = "next-slot", patternId = "next" });
                    f.Row(0, f.Note(), Fx("ZB", 0x12), Fx("ZD", 2));
                    using (var e = f.Open())
                    {
                        f.Render(e, 18001);
                        Need(e.Snapshot.sequenceIndex == 1 && e.Snapshot.row == 18, "hold/break destination");
                        var notes = new List<TrackerEvent>();
                        while (e.ReadEvent(out var ev))
                            if (ev.kind == TrackerEventKind.NoteOn)
                                notes.Add(ev);
                        Need(notes.Count == 1, "held repeated note");
                    }

                    f.MacrosDevice();
                    f.Lane();
                    using (var e = f.Open())
                    {
                        f.Render(e, 6001);
                        Near(e.ObserveMacro(0, 0), 1, "hold froze last tick instead of original end");
                        f.Render(e, 11000);
                        Near(e.ObserveMacro(0, 0), 1, "hold automation advanced/repeated");
                    }

                    p.lineCount = 8;
                    p.tracks[0].WriteLine(new PatternLine { line = 7, effects = new List<EffectCell> { new EffectCell { command = Fx("ZB", 0) } } });
                    using (var e = f.Open())
                    {
                        f.Render(e, 18001);
                        Need(e.Snapshot.row == 7 && Diagnostic(e, TrackerRuntimeDiagnostic.BreakClamped), "short target clamp");
                        f.Render(e, 6000);
                        Need(e.Snapshot.sequenceIndex == 0 && e.Snapshot.row == 0, "last sequence slot did not wrap first");
                    }
                });
                check("24 external slot73 identity", f =>
                {
                    f.instrument.model.family = InstrumentFamily.Synth;
                    f.instrument.model.modulation.Clear();
                    f.instrument.model.sampler.samples[0].modulationSet = -1;
                    f.song.tracks[0].externalSources.Add(new ExternalSourceDevice { id = "external", sourceOrdinal = 1, instrumentId = f.instrument.model.id, pluginId = "plugin", parameterNumbers = new List<string> { "2", "73" } });
                    f.instrument.model.externalParameters.Add(new ExternalParameterMapping { externalId = "73", mapping = TrackerP4Check.Route("cutoff", 20, 275, "Hz") });
                    f.Row(0, f.Note(), Fx("12", 128));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.Snapshot.parameterLive[(int)TrackerParameter.FilterCutoff], 148, "slot2 external73 cutoff");
                        Near(e.Snapshot.parameterLive[(int)TrackerParameter.Volume], 1, "external73 changed unrelated volume");
                    }

                    f.Row(0, f.Note(), Fx("11", 0));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.Snapshot.parameterLive[(int)TrackerParameter.Volume], 1, "unmapped external2 guessed slot/ID73");
                        float authoredCutoff = e.Snapshot.parameterBase[(int)TrackerParameter.FilterCutoff];
                        Near(e.Snapshot.parameterLive[(int)TrackerParameter.FilterCutoff], authoredCutoff, "unmapped ID2 changed authored physical cutoff");
                        Need(e.SetExternal("notes", "external", 1, .4f), "old explicit identity API lost mapped external73");
                        Near(e.Snapshot.parameterLive[(int)TrackerParameter.FilterCutoff], 122, "explicit external identity setter");
                        e.SendCommand(TrackerCommand.Play());
                        f.Render(e, 1);
                        Near(e.Snapshot.parameterLive[(int)TrackerParameter.Volume], 1, "fresh Play retained absent-default external write");
                        Near(e.Snapshot.parameterLive[(int)TrackerParameter.FilterCutoff], authoredCutoff, "fresh Play normalized an unknown physical default");
                        e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 0 });
                        Near(e.Snapshot.parameterLive[(int)TrackerParameter.Volume], 1, "seek guessed absent source default");
                        Near(e.Snapshot.parameterLive[(int)TrackerParameter.FilterCutoff], authoredCutoff, "seek normalized an unknown physical default");
                    }

                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Any(d => d.Contains("EXTERNAL_ID_UNMAPPED") && d.Contains("external=2")), "missing external2 map not flagged");
                });
                check("25 reversed explicit curve and quantum", f =>
                {
                    f.MacrosDevice();
                    var p = f.song.tracks[0].sourceDevices[0].parameters[0];
                    p.min = .8f;
                    p.max = .2f;
                    p.quantum = .1f;
                    f.Row(0, f.Note(), Fx("11", 64));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.ObserveMacro(0, 0), .6, "reversed quantized byte64");
                    }

                    p.min = 0;
                    p.max = 1;
                    p.quantum = 0;
                    p.curvePoints = new List<ModulationPoint>
                    {
                        new ModulationPoint
                        {
                            time = 0,
                            value = 0
                        },
                        new ModulationPoint
                        {
                            time = .5,
                            value = .25f
                        },
                        new ModulationPoint
                        {
                            time = 1,
                            value = 1
                        }
                    };
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.ObserveMacro(0, 0), (64d / 255) * .5, "explicit curve");
                    }

                    f.song.patterns[0].tracks[0].lines.Clear();
                    p.min = .8f;
                    p.max = .2f;
                    p.quantum = .1f;
                    p.curvePoints.Clear();
                    var lane = f.Lane();
                    lane.points.Clear();
                    lane.points.Add(new AutomationPoint { line = 0, value = .25f });
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.ObserveMacro(0, 0), .7, "normative .25 reversed .65 rounds .7");
                    }

                    p.min = p.max = .4f;
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.ObserveMacro(0, 0), .4, "equal source endpoints constant");
                    }

                    p.scaling = "Exp Fast";
                    string before = ZTrackerMigration.Json(p);
                    using (var prepared = TrackerPreparedSong.Prepare(f.song))
                        Need(prepared.diagnostics.Any(d => d.Contains("SOURCE_PARAMETER_UNSUPPORTED")), "Exp Fast guessed");
                    Need(before == ZTrackerMigration.Json(p), "curve raw lost");
                });
                check("26 tick automation and same-instant setters", f =>
                {
                    AutomationFixtures(pcm, f.block);
                    f.MacrosDevice();
                    f.Lane();
                    f.Row(0, f.Note(), Fx("11", 255));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.ObserveMacro(0, 0), 1, "tick0 effect wins");
                        f.Render(e, 1500);
                        Near(e.ObserveMacro(0, 0), .25, "tick1 lane resumes");
                        f.Render(e, 1500);
                        Near(e.ObserveMacro(0, 0), .5, "tick2 lane");
                        f.Render(e, 1500);
                        Near(e.ObserveMacro(0, 0), .75, "tick3 lane");
                    }

                    var fractionalTempo = new AutomationLane
                    {
                        id = "fractional-tempo",
                        target = new ParameterTarget
                        {
                            kind = ParameterKind.Timing,
                            parameter = "bpm"
                        },
                        points = new List<AutomationPoint>
                        {
                            new AutomationPoint
                            {
                                line = .125,
                                value = .5f
                            }
                        }
                    };
                    f.song.patterns[0].tracks[0].automation.Add(fractionalTempo);
                    string raw = ZTrackerMigration.Json(fractionalTempo);
                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Any(d => d.Contains("AUTOMATION_LANE_UNSUPPORTED") && d.Contains("fractional-tempo")), "fractional tempo lane guessed timing");
                    Need(raw == ZTrackerMigration.Json(fractionalTempo), "fractional tempo source changed");
                });
                check("27 disabled latent then enable", f =>
                {
                    f.MacrosDevice();
                    f.song.tracks[0].sourceDevices[0].parameters.RemoveRange(1, 7);
                    f.Lane();
                    f.song.patterns[0].tracks[0].automation.Add(new AutomationLane { id = "independent-direct-macro", target = new ParameterTarget { kind = ParameterKind.InstrumentMacro, instrumentId = f.instrument.model.id, index = 1 }, points = new List<AutomationPoint> { new AutomationPoint { line = 0, value = .4f }, new AutomationPoint { line = .75, value = .8f } } });
                    f.Row(0, f.Note(), Fx("10", 0));
                    f.Row(1, null, Fx("10", 1));
                    using (var e = f.Open())
                    {
                        f.Render(e, 4501);
                        Near(e.ObserveMacro(0, 0), 0, "disabled emitted changes");
                        Near(e.Snapshot.deviceParameters[0].latent, .75, "disabled latent");
                        Near(e.ObserveMacro(0, 1), .8, "disabled source affected independent direct macro");
                        f.Render(e, 1500);
                        Near(e.ObserveMacro(0, 0), 1, "enable emits current latent");
                    }

                    f.song.patterns[0].tracks[0].lines.RemoveAll(line => line.line == 1);
                    f.song.patterns[0].tracks[0].automation.Add(new AutomationLane { id = "enable-at-threequarters", target = new ParameterTarget { kind = ParameterKind.Device, deviceId = "macros", parameter = "enabled" }, interpolation = AutomationInterpolation.Step, points = new List<AutomationPoint> { new AutomationPoint { line = 0, value = 0 }, new AutomationPoint { line = .75, value = 1 } } });
                    using (var e = f.Open())
                    {
                        f.Render(e, 4501);
                        Near(e.ObserveMacro(0, 0), .75, "enable at .75 emits current latent");
                        Near(e.ObserveMacro(0, 1), .8, "enable overwrote independent direct macro");
                    }
                });
                check("28 seek reconstructs disabled latent/emitted", f =>
                {
                    f.instrument.model.macros[0].value = .2f;
                    f.MacrosDevice();
                    var lane = f.Lane();
                    lane.interpolation = AutomationInterpolation.Step;
                    lane.points = new List<AutomationPoint>
                    {
                        new AutomationPoint
                        {
                            line = 0,
                            value = .2f
                        },
                        new AutomationPoint
                        {
                            line = .75,
                            value = .6f
                        },
                        new AutomationPoint
                        {
                            line = 1.125,
                            value = .9f
                        }
                    };
                    f.Row(0, f.Note(), Fx("11", 153));
                    f.Row(1, null, Fx("10", 0));
                    using (var e = f.Open())
                    {
                        e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 2 });
                        Near(e.ObserveMacro(0, 0), .6, "normative reconstructed emitted");
                        Need(!e.Snapshot.devices[0].enabled, "seek disabled");
                        Near(e.Snapshot.deviceParameters[0].latent, .9, "off tick latent replay");
                        Need(!e.Snapshot.voices.ToArray().Any(v => v.active), "seek retained voices");
                        Need(Diagnostic(e, TrackerRuntimeDiagnostic.SeekVoiceStateDiscarded), "seek voice discard warning");
                        f.Row(2, null, Fx("10", 1));
                    }

                    f.Row(2, null, Fx("10", 1));
                    using (var e = f.Open())
                    {
                        e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 2 });
                        f.Render(e, 1);
                        Near(e.ObserveMacro(0, 0), .9, "enable after seek");
                    }

                    using (var unreachable = new Fixture(pcm, f.block))
                    {
                        unreachable.Row(0, null, Fx("ZB", 0));
                        using (var e = unreachable.Open())
                        {
                            e.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = 0, b = 2 });
                            Need(!e.Snapshot.playing && Diagnostic(e, TrackerRuntimeDiagnostic.SeekUnreachable), "unreachable flow started guessed seek state");
                        }
                    }
                });
                check("29 decimalhex legacy reversible preservation", f =>
                {
                    PresetFixture(pcm, f.block);
                    foreach (int command in new[]
                    {
                        1,
                        2,
                        3,
                        4,
                        7,
                        8,
                        10,
                        11,
                        12,
                        13,
                        16,
                        17,
                        18,
                        0x16,
                        0x17,
                        0x18
                    }

                    )
                    {
                        var source = ZTrackerMigration.ConvertCommand(command, 127, 0, "fixture");
                        string before = ZTrackerMigration.Json(source);
                        f.Row(0, f.Note(), source);
                        string expectedReason = command == 1 || command == 2 ? "RATIO_SLIDE" : command == 3 ? "GLIDE_TIMING" : command == 4 ? "VIBRATO_CONVERSION" : command == 7 ? "TREMOLO_IGNORED" : command == 8 ? "PAN_QUANTIZED" : command == 10 ? "GAIN_SLIDE_SCOPE" : command == 11 ? "ORDER_JUMP" : command == 12 ? "GAIN_SET" : command == 13 ? "BREAK_BOUNDARY" : command == 16 ? "MACRO_OWNERSHIP" : command == 17 ? "MACRO_SLEW" : command == 18 ? "PRESET_UNRESOLVED" : "UNKNOWN_BYTE";
                        using (var p = TrackerPreparedSong.Prepare(f.song))
                            Need(p.diagnostics.Any(d => d.Contains("LEGACY_" + expectedReason)), "missing named legacy reason " + command);
                        Need(before == ZTrackerMigration.Json(source), "legacy source changed");
                    }

                    var timing = ZTrackerMigration.ConvertCommand(15, 0, 0, "fixture");
                    Need(timing.identifier == "ZK" && timing.value == 1, "F00");
                    for (int value = 17; value <= 31; value++)
                    {
                        f.Row(0, f.Note(), ZTrackerMigration.ConvertCommand(15, value, 0, "fixture"));
                        using (var p = TrackerPreparedSong.Prepare(f.song))
                            Need(p.diagnostics.Any(d => d.Contains("LEGACY_TPL_RANGE")), "F17..31 guessed timing");
                    }

                    Near(Math.Floor(128d * 127 / 255 + .5), 64, "documented pan candidate");
                    foreach (var forged in new[]
                    {
                        new CommandData
                        {
                            present = true,
                            valuePresent = true,
                            identifier = "0U",
                            value = 16,
                            hasLegacy = true,
                            legacyCommand = 1,
                            legacyParameter = 16
                        },
                        new CommandData
                        {
                            present = true,
                            valuePresent = true,
                            identifier = "ZT",
                            value = 144,
                            hasLegacy = true,
                            legacyCommand = 15,
                            legacyParameter = 16,
                            migrationDecision = "chosen"
                        },
                        new CommandData
                        {
                            present = true,
                            valuePresent = true,
                            identifier = "ZK",
                            value = 1,
                            hasLegacy = true,
                            legacyCommand = 15,
                            legacyParameter = 0
                        },
                        new CommandData
                        {
                            present = true,
                            valuePresent = true,
                            identifier = "ZT",
                            value = 144,
                            hasLegacy = true,
                            legacyCommand = 15,
                            legacyParameter = 20
                        }
                    }

                    )
                    {
                        string before = ZTrackerMigration.Json(forged);
                        f.Row(0, f.Note(), forged);
                        using (var e = f.Open())
                        {
                            f.Render(e, 1);
                            Near(e.Snapshot.bpm, 120, "forged retained legacy timing executed");
                            Need(e.Snapshot.ticksPerLine == 4, "unselected F00/forged legacy TPL executed");
                            Near(f.Foreground(e).commandPitch, 0, "forged ratio-to-U relabel executed");
                        }

                        using (var p = TrackerPreparedSong.Prepare(f.song))
                            Need(p.diagnostics.Any(d => d.Contains("LEGACY_CONVERSION_NOT_SELECTED")), "decision text accepted as conversion proof");
                        Need(before == ZTrackerMigration.Json(forged), "forged source provenance changed");
                    }

                    foreach (int value in new[]
                    {
                        1,
                        16,
                        32,
                        144,
                        255
                    }

                    )
                    {
                        var approved = ZTrackerMigration.ConvertCommand(15, value, 0, "approved-P2-subset");
                        f.Row(0, f.Note(), approved);
                        using (var e = f.Open())
                        {
                            f.Render(e, 1);
                            Need(value <= 16 ? e.Snapshot.ticksPerLine == value : e.Snapshot.bpm == value, "exact approved P2 F mapping refused");
                        }

                        using (var p = TrackerPreparedSong.Prepare(f.song))
                            Need(p.diagnostics.Any(d => d.Contains("LEGACY_TIMING_EVENT_DIFFERENCE")), "approved timing mapping lost difference provenance");
                    }
                });
                check("30 defect and shared macro ownership flag", f =>
                {
                    var command = ZTrackerMigration.ConvertCommand(16, 0x03, 0, "channel0");
                    f.Row(0, f.Note(), command);
                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Any(d => d.Contains("MACRO_OWNERSHIP")), "shared native channel macro guessed");
                    Need(command.hasLegacy && command.legacyCommand == 16, "reversible bytes");
                    var other = f.Note(64, 1);
                    other.sampleFx = ZTrackerMigration.ConvertCommand(16, 0x0c, 1, "channel1");
                    var first = f.Note();
                    first.sampleFx = command;
                    f.Row(0, first);
                    f.song.patterns[0].tracks[0].lines[0].notes.Add(other);
                    using (var e = f.Open())
                    {
                        f.Render(e, 1);
                        Near(e.ObserveMacro(0, 0), f.instrument.model.macros[0].value, "shared channel .2/.8 silently became instrument macro");
                    }

                    using (var p = TrackerPreparedSong.Prepare(f.song))
                        Need(p.diagnostics.Count(d => d.Contains("MACRO_OWNERSHIP")) == 2, "each source channel ownership not flagged");
                    f.Row(0, f.Note(), Fx("0T", 0x48));
                    float correctedAmplitude;
                    using (var e = f.Open())
                    {
                        f.Render(e, 3001);
                        Need(f.Foreground(e).commandTremolo < 1, "tremolo remains native no-op");
                        correctedAmplitude = f.left[3000];
                    }

                    f.Row(0, f.Note());
                    using (var e = f.Open())
                    {
                        f.Render(e, 3001);
                        Near(correctedAmplitude / f.left[3000], 1 - (8d / 15) * (1 - Math.Cos(Math.PI / 4)) / 2, "separate tremolo correction amplitude gate");
                    }
                });
                using (var f = new Fixture(pcm, 333))
                {
                    f.MacrosDevice();
                    f.Lane();
                    for (int row = 0; row < 32; row++)
                        f.Row(row, row == 0 ? f.Note() : null, Fx("0U", 16), Fx("0V", 0x48), Fx("0T", 0x48), Fx("0R", 2));
                    using (var e = f.Open())
                    {
                        f.Render(e, 1024);
                        long native = AudioThreadGuard.Blocks, managed = AudioThreadGuard.ManagedBlocks, before = GC.GetAllocatedBytesForCurrentThread();
                        for (int block = 0; block < 64; block++)
                            e.Render(f.left, f.right, 333, 333);
                        long bytes = GC.GetAllocatedBytesForCurrentThread() - before, nativeDelta = AudioThreadGuard.Blocks - native, managedDelta = AudioThreadGuard.ManagedBlocks - managed;
                        Need(bytes == 0 && nativeDelta == 64 && managedDelta == 0 && e.Compiled, "P5 warmed allocation/native guard");
                        report.AppendLine("MEASURE p5RenderAllocatedBytes=" + bytes + " nativeBlocks=" + nativeDelta + " managedBlocksDelta=" + managedDelta);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pcm);
            }

            return "TrackerP5Check " + pass + " passed / " + fail + " failed\n" + report;
        }
    }
}
