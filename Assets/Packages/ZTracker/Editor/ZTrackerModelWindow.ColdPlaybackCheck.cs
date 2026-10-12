using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Midi;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        public static string ColdPlaybackCheckReport { get; private set; } = "";
        static ZTrackerInstrument ColdSynth(bool fm, bool legacy)
        {
            var asset = CreateInstance<ZTrackerInstrument>(); asset.name = "Cold " + (fm ? "FM" : "Synth");
            if (legacy)
            {
                asset.type = fm ? InstrumentType.FM : InstrumentType.Synth; asset.waveA = 0; asset.waveB = 0;
                asset.volume = .25f; asset.attack = asset.decay = 0; asset.sustain = 1; asset.release = .1f;
                asset.fmOperators = NewInstrumentData().parameters.fmOperators;
            }
            else
            {
                asset.schemaVersion = 1; asset.model = NewInstrumentData(); asset.model.synthMode = fm ? SynthMode.FM : SynthMode.Subtractive;
                var q = asset.model.parameters; q.type = fm ? InstrumentType.FM : InstrumentType.Synth; q.attack = q.decay = 0; q.sustain = 1; q.release = .1f;
            }
            return asset;
        }
        static ZTrackerSong ColdSong(ZTrackerInstrument instrument)
        {
            var song = CreateInstance<ZTrackerSong>(); song.schemaVersion = 1; song.model = TrackerEngineCheck.FixtureSong(instrument);
            song.model.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { TrackerEngineCheck.Note(69) } });
            return song;
        }
        public static string CheckColdPlaybackPcm()
        {
            int pass = 0, fail = 0; var report = new StringBuilder();
            using (var left = new NativeArray<float>(4096, Allocator.TempJob)) using (var right = new NativeArray<float>(4096, Allocator.TempJob))
            foreach (bool legacy in new[] { false, true }) foreach (bool fm in new[] { false, true }) foreach (string path in new[] { "typed audition", "MIDI audition", "transport" })
            {
                var ins = ColdSynth(fm, legacy); var song = ColdSong(ins); string before = JsonUtility.ToJson(ins);
                string label = (legacy ? "legacy " : "model ") + (fm ? "FM " : "Synth ") + path;
                try
                {
                    using (var snapshot = PlaybackSnapshot.Create(song, null, -1)) using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(snapshot.Data)))
                    {
                        if (path == "transport") engine.SendCommand(TrackerCommand.Play());
                        else if (path == "typed audition") engine.SendCommand(TrackerCommand.Audition(0,69,127,0));
                        else { var router = new ZTrackerMidiRouter(); router.SetRoute(engine.LiveInput,0,0,0); router.Receive((uint)(0x90 | 69 << 8 | 110 << 16), Stopwatch.GetTimestamp()); }
                        engine.Render(left,right,4096); double energy = left.ToArray().Sum(x => (double)x*x) + right.ToArray().Sum(x => (double)x*x);
                        if (energy <= .0001 || !engine.Snapshot.voices.ToArray().Any(v => v.active)) throw new Exception("Silent PCM: energy=" + energy);
                        if (before != JsonUtility.ToJson(ins)) throw new Exception("Playback mutated the source instrument");
                        report.AppendLine("PASS " + label + " energy=" + energy.ToString("F6")); pass++;
                    }
                }
                catch (Exception ex) { report.AppendLine("FAIL " + label + ": " + ex.Message); fail++; }
                finally { DestroyImmediate(song); DestroyImmediate(ins); }
            }
            return "Cold PCM: " + pass + " passed / " + fail + " failed\n" + report;
        }
        public static string CheckRetainedSendPlayback()
        {
            int pass = 0, fail = 0; var report = new StringBuilder();
            foreach (bool legacy in new[] { false, true }) foreach (bool fm in new[] { false, true })
            {
                var ins = ColdSynth(fm, legacy); var song = ColdSong(ins);
                string label = (legacy ? "legacy " : "model ") + (fm ? "FM" : "Synth");
                try
                {
                    float[] dry;
                    using (var left = new NativeArray<float>(4096, Allocator.TempJob)) using (var right = new NativeArray<float>(4096, Allocator.TempJob))
                    {
                        using (var snapshot = PlaybackSnapshot.Create(song, null, -1)) using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(snapshot.Data)))
                        { engine.SendCommand(TrackerCommand.Play()); engine.Render(left,right,4096); dry = left.ToArray().Concat(right.ToArray()).ToArray(); }
                        if (legacy) { ins.instDelaySend = .3f; ins.instReverbSend = .6f; }
                        else { ins.model.parameters.instDelaySend = .3f; ins.model.parameters.instReverbSend = .6f; }
                        string before = JsonUtility.ToJson(ins);
                        using (var snapshot = PlaybackSnapshot.Create(song, null, -1))
                        {
                            var prepared = TrackerPreparedSong.Prepare(snapshot.Data);
                            using (var engine = new TrackerOffline(prepared))
                            {
                                if (!prepared.diagnostics.Any(x => x.Contains("effect=Delay")) || !prepared.diagnostics.Any(x => x.Contains("effect=Reverb"))) throw new Exception("Missing destination diagnostics");
                                engine.SendCommand(TrackerCommand.Play()); engine.Render(left,right,4096);
                                var actual = left.ToArray().Concat(right.ToArray()).ToArray();
                                if (actual.Sum(x => (double)x*x) <= .0001) throw new Exception("Retained sends silenced dry playback");
                                if (actual.Where((x,i) => Math.Abs(x-dry[i]) > 1e-7f).Any()) throw new Exception("Absent sends changed dry PCM");
                                if (before != JsonUtility.ToJson(ins)) throw new Exception("Authored send amounts changed");
                            }
                        }
                    }
                    report.AppendLine("PASS " + label + " missing destinations preserve dry PCM and authored amounts"); pass++;
                }
                catch (Exception ex) { report.AppendLine("FAIL " + label + ": " + ex.Message); fail++; }
                finally { DestroyImmediate(song); DestroyImmediate(ins); }
            }
            return "Retained sends: " + pass + " passed / " + fail + " failed\n" + report;
        }
        // Actual SAP graph startup without any Instrument preview or hardware MIDI device. Poll the report after completion.
        public static void StartColdPlaybackUiCheck(bool stoppedByConfiguration = false)
        {
            ColdPlaybackCheckReport = "RUNNING"; var report = new StringBuilder(); int pass = 0, fail = 0;
            var previous = focusedWindow; ZTrackerModelWindow window = null; ZTrackerSong song = null; ZTrackerInstrument ins = null;
            var steps = new Queue<(string label, double delay, Action action)>();
            void Cleanup()
            {
                if (window != null) { window.StopPreview(); window.DisposeMidiPreview(); window.Close(); window = null; }
                if (song != null) { DestroyImmediate(song); song = null; } if (ins != null) { DestroyImmediate(ins); ins = null; }
            }
            foreach (bool legacyValue in new[] { false, true }) foreach (bool fmValue in new[] { false, true }) foreach (string pathValue in new[] { "typed audition", "MIDI audition", "transport" })
            {
                bool legacy = legacyValue, fm = fmValue; string path = pathValue;
                string label = (legacy ? "legacy " : "model ") + (fm ? "FM " : "Synth ") + path; string before = null; double startEnergy = 0;
                steps.Enqueue(("SETUP " + label, .15, () =>
                {
                    Cleanup(); ins = ColdSynth(fm,legacy); song = ColdSong(ins); before = JsonUtility.ToJson(ins);
                    window = CreateInstance<ZTrackerModelWindow>(); window.song = song; window.instrument = ins; window.entryInstrument = 0; window.pane = 0; window.editMode = false; window.midiSimulatedOnly = true;
                    window.titleContent = new GUIContent("ZTracker cold playback check"); window.position = new Rect(120,120,1100,760); window.Show(); window.CreateGUI();
                    window.midiRouter.Channel = -1; window.midiRouter.OctaveShift = 0;
                    if (window.host != null || window.midiPreviewHost != null) throw new Exception("Fixture was warmed during setup");
                    if (stoppedByConfiguration) { window.EnsurePreview(); window.host.ReconfigureOutput(false); }
                }));
                steps.Enqueue(("TRIGGER " + label, .55, () =>
                {
                    if (path == "transport") window.Play(false);
                    else if (path == "typed audition")
                    {
                        window.grid.Focus(); using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z })) window.grid.SendEvent(key);
                    }
                    else { window.RouteLiveKeys(); window.midiRouter.Receive((uint)(0x90 | 69 << 8 | 110 << 16), Stopwatch.GetTimestamp()); }
                    if (window.host == null) throw new Exception("No song renderer: " + window.lastError);
                    startEnergy = window.host.RenderedEnergy;
                }));
                steps.Enqueue((label, .1, () =>
                {
                    if (window.host == null) throw new Exception("Renderer disappeared");
                    var source = window.preview.GetComponent<AudioSource>(); double energy = window.host.RenderedEnergy - startEnergy;
                    report.AppendLine("MEASURE " + label + " frames=" + window.host.RenderedFrames + " blocks=" + window.host.RenderedBlocks + " energy=" + energy.ToString("F6") + " sourcePlaying=" + source.isPlaying + " error=" + window.lastError);
                    if (window.midiPreviewHost != null) throw new Exception("Instrument preview unexpectedly started");
                    if (window.host.RenderedFrames <= 0 || energy <= .0001) throw new Exception("Cold graph produced no audible PCM");
                    if (before != JsonUtility.ToJson(ins)) throw new Exception("Cold preview mutated source data");
                }));
            }
            EditorApplication.CallbackFunction update = null; double next = EditorApplication.timeSinceStartup + .1;
            update = () =>
            {
                if (EditorApplication.timeSinceStartup < next) return;
                if (steps.Count == 0)
                {
                    EditorApplication.update -= update; Cleanup(); if (previous != null) previous.Focus();
                    ColdPlaybackCheckReport = (stoppedByConfiguration ? "Stopped graph SAP: " : "Cold SAP: ") + pass + " passed / " + fail + " failed\n" + report; return;
                }
                var step = steps.Dequeue();
                try { step.action(); if (!step.label.StartsWith("SETUP ") && !step.label.StartsWith("TRIGGER ")) { pass++; report.AppendLine("PASS " + step.label); } }
                catch (Exception ex) { fail++; report.AppendLine("FAIL " + step.label + ": " + ex.Message); }
                next = EditorApplication.timeSinceStartup + step.delay;
            };
            EditorApplication.update += update;
        }
    }
}
