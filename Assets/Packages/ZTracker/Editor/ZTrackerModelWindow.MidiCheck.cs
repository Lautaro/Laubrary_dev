using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
        /// arrival-to-applied latency, force garbage collections while keys stream in, preview on the clean channel over
        /// the playing song, Panic, unplug/replug and turn MIDI off. In-memory fixtures only; nothing is saved. Runs over
        /// editor updates; read <see cref="MidiUiCheckReport"/> afterwards.
        /// </summary>
        public static void StartMidiUiCheck()
        {
            MidiUiCheckReport = "RUNNING";
            var report = new StringBuilder(); int pass = 0, fail = 0;
            var clip = AudioClip.Create("midi-ui-check", 96000, 1, 48000, false); var raw = new float[96000]; for (int i = 0; i < raw.Length; i++) raw[i] = .05f * Mathf.Sin(i * .05f); clip.SetData(raw, 0);
            var inst = TrackerEngineCheck.FixtureInstrument(clip); inst.name = "Check sine";
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
            Func<string, Button> anyButton = text => window.rootVisualElement.panel.visualTree.Query<Button>().ToList().FirstOrDefault(b => b.text == text);
            double energySong = 0, energyPreview = 0; long framesBefore = 0, managedBefore = 0; double gcStart = 0; int gcCount = 0; Thread producer = null; var stop = new StopFlag(); string savedDevice = EditorPrefs.GetString(MidiPrefs + "Device", null);
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
            steps.Enqueue(("forced garbage collections while keys stream in: audio thread keeps time and stays compiled", .1, () =>
            {
                framesBefore = window.SongHost.RenderedFrames; managedBefore = AudioThreadGuard.ManagedBlocks; gcStart = EditorApplication.timeSinceStartup; gcCount = 0;
                producer = new Thread(() => { int n = 0; while (!stop.value) { sim.NoteOn(48 + n % 24, 100); Thread.Sleep(2); sim.NoteOff(48 + n % 24); sim.Controller(1, n & 127); sim.Bend((n * 97) & 16383); n++; Thread.Sleep(3); } }) { IsBackground = true };
                producer.Start();
            }));
            for (int k = 0; k < 30; k++) steps.Enqueue(("gc", .1, () => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); gcCount++; }));
            steps.Enqueue(("gc result", 0, () =>
            {
                stop.value = true; producer.Join(1000);
                double seconds = EditorApplication.timeSinceStartup - gcStart; long frames = window.SongHost.RenderedFrames - framesBefore; double expected = seconds * AudioSettings.outputSampleRate;
                long managed = AudioThreadGuard.ManagedBlocks - managedBefore;
                report.AppendLine($"MEASURE forcedGc collections={gcCount * 2} seconds={seconds:F2} renderedFrames={frames} expected={expected:F0} ratio={frames / expected:F3} managedAudioBlocks={managed} liveDropped={window.SongHost.LiveInput.Dropped}");
                need(managed == 0, "Managed audio blocks during forced GC");
                AudioSettings.GetDSPBufferSize(out int buffer, out int count);
                need(Math.Abs(frames - expected) <= Math.Max(4 * buffer * count, expected * .05), "Audio fell behind wall time under forced GC");
                sim.Controller(123, 0);
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
            steps.Enqueue(("Panic (real click) silences the preview, the song plays on", .3, () => { click(find("midi-panic"), 0); }));
            steps.Enqueue(("panic result", .1, () => { need(window.MidiPreviewHost.LiveInput.HeldNotes == 0, "Panic left held notes"); need(window.playing, "Panic stopped the song"); }));
            steps.Enqueue(("unplugged keyboard is reported and comes back on its own", 2.5, () => { sim.Unplugged = true; window.midiNextScan = 0; }));
            steps.Enqueue(("unplug status", 0, () => { need(!sim.IsOpen && !window.MidiStatusText.Contains(ZTrackerMidiSimulated.Device), "Unplug not noticed: " + window.MidiStatusText); report.AppendLine("STATUS " + window.MidiStatusText); sim.Unplugged = false; window.midiNextScan = 0; }));
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
                    try { step.run(); if (step.name != "gc") { pass++; report.AppendLine("PASS " + step.name); } }
                    catch (Exception ex) { fail++; report.AppendLine("FAIL " + step.name + ": " + ex.Message); if (step.name == "gc result" || producer != null) stop.value = true; }
                    due = EditorApplication.timeSinceStartup + step.wait;
                    return;
                }
                EditorApplication.update -= tick;
                stop.value = true;
                try { window.MidiShutdown(); window.StopPreview(); window.Close(); } catch (Exception ex) { report.AppendLine("CLEANUP " + ex.Message); }
                DestroyImmediate(window); Undo.ClearUndo(asset); DestroyImmediate(asset); DestroyImmediate(inst); DestroyImmediate(clip);
                sim.Unplugged = false; sim.SimulateInUse = false; sim.Close();
                if (savedDevice == null) EditorPrefs.DeleteKey(MidiPrefs + "Device"); else EditorPrefs.SetString(MidiPrefs + "Device", savedDevice);
                MidiUiCheckReport = $"MidiUiCheck passed={pass} failed={fail}\n" + report;
            };
            EditorApplication.update += tick;
        }

        sealed class StopFlag { public volatile bool value; }
    }
}
