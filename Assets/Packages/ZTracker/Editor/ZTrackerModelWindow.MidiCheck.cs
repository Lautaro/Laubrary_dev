using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Midi;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Result of the last <see cref="StartMidiUiCheck"/> run; "RUNNING" until it finishes.</summary>
        public static string MidiUiCheckReport { get; private set; } = "";

        /// <summary>
        /// MIDI in the real window with real pointer events and real audio graphs: turn MIDI on, pick the simulated
        /// keyboard from the right-click menu, play a chord through the cursor's track, play the song, measure
        /// arrival-to-applied latency at the project buffer and at 256 samples, record while playing and at the cursor, preview
        /// on the clean channel over the playing song with MIDI and computer keys in one chord, Panic, unplug/replug and turn MIDI off. In-memory fixtures only; nothing is saved. Runs over
        /// editor updates; read <see cref="MidiUiCheckReport"/> afterwards.
        /// </summary>
        public static void StartMidiUiCheck()
        {
            MidiUiCheckReport = "RUNNING";
            var report = new StringBuilder(); int pass = 0, fail = 0;
            var clip = AudioClip.Create("midi-ui-check", 96000, 1, 48000, false); var raw = new float[96000]; for (int i = 0; i < raw.Length; i++) raw[i] = .05f * Mathf.Sin(i * .05f); clip.SetData(raw, 0);
            var inst = TrackerEngineCheck.FixtureInstrument(clip); inst.name = "Check sine";
            // A synth needs no sample data, so the audio restart of the buffer step cannot invalidate the fixture.
            inst.model.family = InstrumentFamily.Synth;
            var asset = CreateInstance<ZTrackerSong>(); asset.schemaVersion = 1; asset.model = TrackerEngineCheck.FixtureSong(inst, 3, 16); asset.model.voiceCapacity = 64; asset.model.tracks[0].name = "Lead";
            asset.model.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { TrackerEngineCheck.Note(48, 2) } });
            var window = CreateInstance<ZTrackerModelWindow>(); window.song = asset; window.titleContent = new GUIContent("ZTracker MIDI check"); window.midiSimulatedOnly = true; window.Show(); window.position = new Rect(120, 120, 1100, 760); window.CreateGUI();
            var sim = ZTrackerMidiSimulated.Shared; sim.Unplugged = false; sim.SimulateInUse = false;
            var steps = new Queue<(string name, double wait, Action run)>();
            Action<bool, string> need = (c, m) => { if (!c) throw new Exception(m); };
            Func<string, VisualElement> find = name => window.rootVisualElement.Q(name) ?? throw new Exception("No element " + name);
            Action<VisualElement, int> click = (element, button) =>
            {
                var at = element.worldBound.center;
                window.SendEvent(new Event { type = EventType.MouseDown, button = button, mousePosition = at, clickCount = 1 });
                window.SendEvent(new Event { type = EventType.MouseUp, button = button, mousePosition = at, clickCount = 1 });
            };
            Action<KeyCode, bool> key = (code, down) => window.SendEvent(new Event { type = down ? EventType.KeyDown : EventType.KeyUp, keyCode = code });
            Func<string, Button> anyButton = text => window.rootVisualElement.panel.visualTree.Query<Button>().ToList().FirstOrDefault(b => b.text == text);
            double energySong = 0, energyPreview = 0; string savedDevice = EditorPrefs.GetString(MidiPrefs + "Device", null); int savedBuffer = EditorPrefs.GetInt(MidiPrefs + "Buffer", 0);
            steps.Enqueue(("MIDI toggle turns MIDI on (real click)", .3, () => { need(find("midi-status") is Label s && s.text == "Off", "Status not Off at start"); click(find("midi-toggle"), 0); need(window.midiOn, "Click did not turn MIDI on"); }));
            steps.Enqueue(("right-click menu lists the simulated keyboard; picking it opens it", .5, () =>
            {
                click(find("midi-toggle"), 1);
                var item = anyButton(ZTrackerMidiSimulated.Device); need(item != null, "Menu has no Simulated keyboard entry");
                click(item, 0); need(sim.IsOpen, "Simulated keyboard not opened");
                window.midiMenu?.Close(); window.midiMenu = null;
            }));
            steps.Enqueue(("status names device and target track/column; song renderer created", .5, () =>
            {
                var text = window.MidiStatusText; need(text.Contains(ZTrackerMidiSimulated.Device) && text.Contains("Lead col 1"), "Status: " + text);
                need(window.SongHost != null && window.SongHost.LiveInput.IsOpen, "No song renderer for MIDI");
                report.AppendLine("STATUS " + text);
            }));
            steps.Enqueue(("chord spreads over Lead's three columns and sounds", .4, () =>
            {
                energySong = window.SongHost.RenderedEnergy; sim.NoteOn(60, 110); sim.NoteOn(64, 90); sim.NoteOn(67, 70);
            }));
            steps.Enqueue(("chord applied by the live graph", .4, () =>
            {
                need(window.SongHost.LiveInput.HeldNotes == 3, "Held " + window.SongHost.LiveInput.HeldNotes);
                need(window.SongHost.RenderedEnergy > energySong, "No audio rendered by the song graph");
                need(window.SongHost.RenderCompiled, "Song graph rendered managed code");
                sim.NoteOff(60); sim.NoteOff(64); sim.NoteOff(67);
            }));
            steps.Enqueue(("Play (real click) starts the song on the same renderer", .6, () => { var h = window.SongHost; click(find("play"), 0); need(window.playing && window.SongHost == h, "Play replaced the MIDI renderer or did not start"); }));
            steps.Enqueue(("latency: key arrival to applied in the playing graph", .1, () =>
            {
                var ms = TrackerMidiCheck.MeasureAppliedLatency(window.SongHost.LiveInput, 40, 72);
                need(ms.Length >= 30, "Only " + ms.Length + " keys were applied");
                AudioSettings.GetDSPBufferSize(out int buffer, out int count); int rate = AudioSettings.outputSampleRate;
                Array.Sort(ms);
                report.AppendLine($"MEASURE appliedLatencyMs min={ms[0]:F2} median={ms[ms.Length / 2]:F2} p95={ms[(int)(ms.Length * .95)]:F2} max={ms[ms.Length - 1]:F2} n={ms.Length}; dspBuffer={buffer}x{count} rate={rate} period={buffer * 1000.0 / rate:F2}ms queue={buffer * count * 1000.0 / rate:F2}ms");
            }));
            steps.Enqueue(("latency at a 256-sample buffer, chosen in the right-click menu (real clicks)", 1.5, () =>
            {
                click(find("midi-toggle"), 1); var item = anyButton("256"); need(item != null, "No 256 buffer choice"); click(item, 0); window.midiMenu?.Close(); window.midiMenu = null;
                AudioSettings.GetDSPBufferSize(out int buffer, out _); need(buffer == 256, "Buffer is " + buffer);
            }));
            steps.Enqueue(("a fresh renderer after the audio restart; Play again", .8, () => { need(window.SongHost != null && window.SongHost.LiveInput.IsOpen, "No renderer after the buffer change"); click(find("play"), 0); need(window.playing, "Play failed"); }));
            steps.Enqueue(("latency measured at 256", .1, () =>
            {
                var ms = TrackerMidiCheck.MeasureAppliedLatency(window.SongHost.LiveInput, 40, 72); need(ms.Length >= 30, "Only " + ms.Length + " keys were applied"); Array.Sort(ms);
                AudioSettings.GetDSPBufferSize(out int buffer, out int count); int rate = AudioSettings.outputSampleRate;
                report.AppendLine($"MEASURE appliedLatencyMs@{buffer} min={ms[0]:F2} median={ms[ms.Length / 2]:F2} p95={ms[(int)(ms.Length * .95)]:F2} max={ms[ms.Length - 1]:F2} n={ms.Length}; queue={buffer * count * 1000.0 / rate:F2}ms");
                click(find("midi-toggle"), 1); var item = anyButton("Project"); need(item != null, "No Project buffer choice"); click(item, 0); window.midiMenu?.Close(); window.midiMenu = null;
            }));
            steps.Enqueue(("Project buffer restored; Play again", 1.5, () => { }));
            steps.Enqueue(("restored", .6, () => { AudioSettings.GetDSPBufferSize(out int buffer, out _); need(buffer == ProjectDspBuffer(), "Buffer not restored: " + buffer); click(find("play"), 0); need(window.playing, "Play failed after restore"); }));
            steps.Enqueue(("Rec (real click) while playing writes the key at the heard row", .5, () => { click(find("midi-record"), 0); need(window.midiRecord, "Rec did not latch"); sim.NoteOn(70, 100); }));
            steps.Enqueue(("recorded while playing", .3, () =>
            {
                sim.NoteOff(70); var lines = asset.model.patterns[0].tracks[0].lines;
                need(lines.Any(l => l.notes.Any(n => n.note == NoteKind.Note && n.pitch == 70 && n.instrumentPresent && n.instrument == 0)), "Played key not written into the pattern");
                report.AppendLine("RECORDED rows " + string.Join(",", lines.Where(l => l.notes.Any(n => n.pitch == 70)).Select(l => l.line)));
            }));
            steps.Enqueue(("Stop (real click), then Rec at the cursor: a chord fills columns and the cursor steps on when keys are up", .4, () =>
            {
                click(find("stop"), 0); need(!window.playing, "Stop failed");
            }));
            steps.Enqueue(("stopped chord", .4, () => { window.pane = 0; window.BuildPane(); window.Select(9, 0, false); sim.NoteOn(62, 100); sim.NoteOn(65, 100); }));
            steps.Enqueue(("chord written", .3, () =>
            {
                var line = asset.model.patterns[0].tracks[0].ReadLine(9);
                need(line.notes.Any(n => n.column == 0 && n.pitch == 62) && line.notes.Any(n => n.column == 1 && n.pitch == 65), "Chord not written across columns: " + string.Join(",", line.notes.Select(n => n.column + ":" + n.pitch)));
                need(window.row == 9, "Cursor moved while keys were down");
                sim.NoteOff(62); sim.NoteOff(65);
            }));
            steps.Enqueue(("cursor stepped and one Undo removes the last recorded note", .3, () =>
            {
                need(window.row == 10, "Cursor did not step: " + window.row);
                Undo.PerformUndo(); var line = asset.model.patterns[0].tracks[0].ReadLine(9);
                need(line.notes.Any(n => n.pitch == 62) && !line.notes.Any(n => n.pitch == 65), "Undo did not remove exactly the last note");
                click(find("midi-record"), 0); need(!window.midiRecord, "Rec did not turn off");
                click(find("play"), 0);
            }));
            steps.Enqueue(("Instrument workspace (real click) moves the keyboard to the clean preview over the playing song", .6, () =>
            {
                var tabs = window.rootVisualElement.Query<Button>().ToList().FirstOrDefault(b => b.text == "Instrument"); need(tabs != null, "No Instrument tab");
                window.entryInstrument = 0; window.instrument = inst; click(tabs, 0); need(window.pane == 3, "Tab click did not switch workspace");
            }));
            steps.Enqueue(("clean preview plays while the song keeps playing", .5, () =>
            {
                need(window.MidiPreviewHost != null && window.MidiStatusText.Contains("clean preview"), "No clean preview: " + window.MidiStatusText);
                energyPreview = window.MidiPreviewHost.RenderedEnergy; energySong = window.SongHost.RenderedEnergy;
                sim.NoteOn(60, 100); sim.NoteOn(67, 100);
            }));
            steps.Enqueue(("preview and song both render", .5, () =>
            {
                need(window.MidiPreviewHost.LiveInput.HeldNotes == 2, "Preview held " + window.MidiPreviewHost.LiveInput.HeldNotes);
                need(window.MidiPreviewHost.RenderedEnergy > energyPreview, "Preview silent");
                need(window.playing && window.SongHost.RenderedEnergy > energySong, "Song stopped or silent under preview");
                need(window.SongHost.LiveInput.HeldNotes == 0, "Preview notes leaked into the song track");
                report.AppendLine("STATUS " + window.MidiStatusText);
            }));
            int typedLow = window.octave * 12 + PianoOffset(KeyCode.Z);
            steps.Enqueue(("computer keys Z and C (real key events) join the MIDI chord on the same preview", .4, () =>
            {
                need(typedLow == 60, "Octave assumption: Z plays " + typedLow);
                energyPreview = window.MidiPreviewHost.RenderedEnergy;
                key(KeyCode.Z, true); key(KeyCode.C, true); key(KeyCode.Z, true);
            }));
            steps.Enqueue(("both keyboards sound together, the same pitch twice", .3, () =>
            {
                need(window.MidiPreviewHost.LiveInput.HeldNotes == 4, "Preview held " + window.MidiPreviewHost.LiveInput.HeldNotes + " (expected MIDI 60, 67 and typed 60, 64)");
                need(window.MidiPreviewHost.RenderedEnergy > energyPreview && window.SongHost.LiveInput.HeldNotes == 0, "Typed keys silent or leaked into the song");
                key(KeyCode.Z, false); sim.NoteOff(67);
            }));
            steps.Enqueue(("typed Z and MIDI 67 released, MIDI 60 and typed C still held", .3, () =>
            {
                need(window.MidiPreviewHost.LiveInput.HeldNotes == 2, "Preview held " + window.MidiPreviewHost.LiveInput.HeldNotes);
                sim.NoteOn(67, 100);
            }));
            steps.Enqueue(("Panic (real click) silences both keyboards on the preview, the song plays on", .3, () => { need(window.MidiPreviewHost.LiveInput.HeldNotes == 3, "Re-pressed key not held"); click(find("midi-panic"), 0); }));
            steps.Enqueue(("panic result", .1, () =>
            {
                need(window.MidiPreviewHost.LiveInput.HeldNotes == 0, "Panic left held notes"); need(window.playing, "Panic stopped the song");
                key(KeyCode.C, false); sim.NoteOff(60); sim.NoteOff(67); need(window.instrumentKeys.Count == 0, "Typed key still listed after its key-up");
            }));
            steps.Enqueue(("unplugged keyboard is reported and comes back on its own", 2.5, () => { sim.Unplugged = true; window.midiNextScan = 0; }));
            steps.Enqueue(("unplug status", 2.5, () => { need(!sim.IsOpen && !window.MidiStatusText.Contains(ZTrackerMidiSimulated.Device), "Unplug not noticed: " + window.MidiStatusText); report.AppendLine("STATUS " + window.MidiStatusText); sim.Unplugged = false; window.midiNextScan = 0; }));
            steps.Enqueue(("replug", .5, () => { need(sim.IsOpen && window.MidiStatusText.Contains(ZTrackerMidiSimulated.Device), "Not reopened: " + window.MidiStatusText); }));
            steps.Enqueue(("in use by another program is reported", .3, () => { sim.Close(); sim.SimulateInUse = true; window.midiNextScan = 0; }));
            steps.Enqueue(("in use status", 2.2, () => { need(window.MidiStatusText.Contains("In use"), "Status: " + window.MidiStatusText); report.AppendLine("STATUS " + window.MidiStatusText); sim.SimulateInUse = false; window.midiNextScan = 0; }));
            steps.Enqueue(("MIDI off (real click) closes the device and the preview channel", .5, () => { click(find("midi-toggle"), 0); }));
            steps.Enqueue(("off result", .1, () => { need(!window.midiOn && !sim.IsOpen && window.MidiPreviewHost == null && window.MidiStatusText == "Off", "MIDI off left something open: " + window.MidiStatusText); }));

            double due = EditorApplication.timeSinceStartup + .5;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (EditorApplication.timeSinceStartup < due) return;
                if (steps.Count > 0)
                {
                    var step = steps.Dequeue();
                    try { step.run(); pass++; report.AppendLine("PASS " + step.name); }
                    catch (Exception ex) { fail++; report.AppendLine("FAIL " + step.name + ": " + ex.Message); }
                    due = EditorApplication.timeSinceStartup + step.wait;
                    return;
                }
                EditorApplication.update -= tick;
                try { window.MidiShutdown(); window.StopPreview(); window.Close(); } catch (Exception ex) { report.AppendLine("CLEANUP " + ex.Message); }
                DestroyImmediate(window); Undo.ClearUndo(asset); DestroyImmediate(asset); DestroyImmediate(inst); DestroyImmediate(clip);
                sim.Unplugged = false; sim.SimulateInUse = false; sim.Close();
                if (savedDevice == null) EditorPrefs.DeleteKey(MidiPrefs + "Device"); else EditorPrefs.SetString(MidiPrefs + "Device", savedDevice);
                EditorPrefs.SetInt(MidiPrefs + "Buffer", savedBuffer);
                MidiUiCheckReport = $"MidiUiCheck passed={pass} failed={fail}\n" + report;
            };
            EditorApplication.update += tick;
        }

    }
}
