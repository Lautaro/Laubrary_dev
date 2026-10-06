using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Named retained actions, complete Undo, actual compiled audio and saved-disk fixtures. No user assets or Test Runner.</summary>
        public static string CheckInstrumentWorkflow()
        {
            var report = new StringBuilder(); int pass = 0, fail = 0;
            var window = CreateInstance<ZTrackerModelWindow>(); var songAsset = CreateInstance<ZTrackerSong>(); var inst = CreateInstance<ZTrackerInstrument>();
            inst.schemaVersion = 1; inst.model = NewInstrumentData(); songAsset.schemaVersion = 1; songAsset.model = TrackerEngineCheck.FixtureSong(inst);
            var clip = AudioClip.Create("P6 instrument PCM", 48000, 1, 48000, false); var pcm = Enumerable.Repeat(.4f, 48000).ToArray(); clip.SetData(pcm, 0);
            window.song = songAsset; window.instrument = inst; window.entryInstrument = 0; window.pane = 3; window.Show(); window.CreateGUI();
            void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
            void Fresh()
            {
                window.StopPreview(); Undo.ClearUndo(inst); Undo.ClearUndo(songAsset); inst.serializedNulls = null; songAsset.serializedNulls = null;
                inst.model = NewInstrumentData(); songAsset.model = TrackerEngineCheck.FixtureSong(inst);
                window.sampleId = window.zoneId = window.modSetId = window.modDeviceId = ""; window.macroIndex = window.instrumentChain = window.sampleSort = 0; window.presetIndex = -1; window.instrumentTab = 0; window.BuildPane();
            }
            void SamplerFixture(int count)
            {
                inst.model.family = InstrumentFamily.Sampler; inst.model.parameters.type = InstrumentType.Sample;
                for (int n = 0; n < count; n++) inst.model.sampler.samples.Add(new SampleData { id = Id(), name = "Fixture sample " + n, pcm = clip, regionEndFrame = clip.samples, loopEndFrame = clip.samples });
                window.sampleId = inst.model.sampler.samples.FirstOrDefault()?.id ?? ""; window.BuildPane();
            }
            void Check(string label, Action action) { try { Fresh(); action(); pass++; report.AppendLine("PASS " + label); } catch (Exception e) { fail++; report.AppendLine("FAIL " + label + ": " + (e.InnerException ?? e).Message); } }
            void Click(string name) { var button = window.rootVisualElement.Q<Button>(name); Need(button != null, "Missing named action " + name); typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(button.clickable, new object[] { null, 0 }); }
            void DialValue(string name, float value) { var dial = window.rootVisualElement.Q<ZuiMicroSlider>(name); Need(dial != null, "Missing named dial " + name); typeof(ZuiMicroSlider).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dial, new object[] { value, true }); }
            void RangeValue(string name, float low, float high) { var range = window.rootVisualElement.Q<ZuiMicroMinMax>(name); Need(range != null, "Missing named range " + name); typeof(ZuiMicroMinMax).GetMethod("SetValues", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(range, new object[] { low, high, true }); }
            void Tab(int tab) { window.instrumentTab = tab; window.BuildPane(); }
            try
            {
                Check("modern factory centered sampler and eight initialized macros", () => { Need(inst.model.provenance == "" && !inst.model.provenance.Contains("native-v0"), "New asset falsely claims native provenance"); Need(inst.model.sampler.pan == 0 && inst.model.sampler.volume == 1, "Modern sampler defaults"); Need(inst.model.macros.Length == 8 && inst.model.macros.All(m => m != null), "Uninitialized macros"); Need(inst.model.parameters.fmOperators.Length == 4 && inst.model.parameters.fmOperators.All(o => o.waveform == 0), "FM defaults"); });
                Check("sample assignment and per-sample NNA use actual authoring actions", () => { window.InstrumentEdit("family check", () => { inst.model.family = InstrumentFamily.Sampler; inst.model.parameters.type = InstrumentType.Sample; }, true); Tab(0); Click("add-sample"); var sample = window.SelectedSample; window.AssignSample(sample, clip); Need(sample.pcm == clip && sample.regionEndFrame == clip.samples && sample.loopEndFrame == clip.samples, "PCM assignment did not initialize bounds"); inst.model.sampler.nna = NewNoteAction.Continue; Click("add-sample"); Need(window.SelectedSample.nna == NewNoteAction.Continue && inst.model.sampler.samples[0].nna == NewNoteAction.NoteOff, "Default changed existing NNA"); string addedId = window.SelectedSample.id; Undo.PerformUndo(); Need(inst.model.sampler.samples.Count == 1, "Sample Undo failed"); Undo.PerformRedo(); Need(inst.model.sampler.samples.Count == 2, "Sample Redo failed"); window.sampleId = addedId; window.BuildPane(); window.AssignSample(window.SelectedSample, clip); Need(inst.model.sampler.samples.All(s => s.pcm == clip), "Assigned PCM lost after reselecting restored identity"); });
                Check("keyzones retain inclusive ranges and sample identities after reorder and Undo", () => { SamplerFixture(2); Tab(1); Click("add-zone"); var zone = window.SelectedZone; zone.noteMin = 60; zone.noteMax = 72; zone.velocityMin = 20; zone.velocityMax = 100; string sample = inst.model.sampler.samples[zone.sample].id; var before = Clone(zone); SetZoneRange(zone, before, 200, -200, 0); Need(zone.noteMax == 119 && zone.noteMin == 107 && zone.velocityMin == 0 && zone.velocityMax == 80, "Zone move lost span or clipping"); SetZoneRange(zone, before, -20, 20, 1 | 8); Need(zone.noteMin == 40 && zone.velocityMax == 120, "Range edges incorrect"); window.InstrumentEdit("reorder sample check", () => window.ReorderSamples(1, 0), true); Need(inst.model.sampler.samples[inst.model.sampler.zones[0].sample].id == sample, "Zone retargeted to another PCM"); Undo.PerformUndo(); Need(inst.model.sampler.samples[inst.model.sampler.zones[0].sample].id == sample, "Undo lost sample identity"); Undo.PerformRedo(); Click("clone-zone"); Need(inst.model.sampler.zones.Count == 2 && inst.model.sampler.zones.Select(z => z.id).Distinct().Count() == 2, "Clone identity"); });
                Check("loop frames, slices, regions and paired source prepare actual PCM", () => { SamplerFixture(1); Tab(1); Click("add-zone"); Tab(0); window.SelectedSample.loop = SampleLoop.Forward; RangeValue("sample-region", 0, 2000); RangeValue("sample-loop-frames", 100, 1000); Click("add-slice"); Need(inst.model.sampler.samples[0].sliceMarkers.Count == 1, "Slice button unavailable"); var z = inst.model.sampler.zones[0]; z.blend = new SampleBlendExtension { pcmB = clip, loopB = SampleLoop.PingPong, loopEndFrameB = 2000, amount = .3f }; using (var prepared = TrackerPreparedSong.Prepare(songAsset.model)) Need(prepared.state.samples.Length > 0, "Sampler did not prepare"); RangeValue("sample-loop-frames", 2000, 2000); Need(inst.model.sampler.samples[0].loopStartFrame == 1998 && inst.model.sampler.samples[0].loopEndFrame == 2000, "Loop collapsed to invalid zero-width/end-outside region"); Undo.PerformUndo(); Need(inst.model.sampler.samples[0].loopStartFrame == 100 && inst.model.sampler.samples[0].loopEndFrame == 1000, "Boundary edit Undo lost loop"); RangeValue("sample-loop-frames", 100, 1500); Undo.PerformUndo(); Need(inst.model.sampler.samples[0].loopEndFrame == 1000 && inst.model.sampler.samples[0].sliceMarkers.Count == 1, "Loop complete Undo"); Undo.PerformRedo(); Need(inst.model.sampler.samples[0].loopEndFrame == 1500 && inst.model.sampler.zones[0].blend.pcmB == clip, "Loop Redo"); });
                Check("modulation set devices assignment and shared chain reachable with Undo", () => { SamplerFixture(1); Tab(1); Click("add-zone"); Tab(2); Click("add-mod-set"); var set = inst.model.modulation[0]; window.AddModDevice(set, ModulationDeviceKind.AHDSR); window.AddModDevice(set, ModulationDeviceKind.Multipoint); Need(set.devices.Count == 2 && set.devices[1].points.Count == 2, "Modulation device setup"); window.SelectedSample.modulationSet = 0; Tab(3); Click("add-instrument-chain"); Need(window.rootVisualElement.Q("instrument-chain-editor") != null, "Shared chain widget missing"); window.SelectedSample.fxChain = 0; inst.model.fxChains[0].nodes.Add(new AudioEffectNodeData { uid = "instrument-gain", type = Laubrary.Zounds.ZoundEffectType.Gain, p = new[] { .5f } }); window.InstrumentEdit("chain scalar check", () => inst.model.fxChains[0].nodes[0].p[0] = .25f); Undo.PerformUndo(); Need(inst.model.fxChains[0].nodes[0].p[0] == .5f, "Chain scalar Undo"); using (var prepared = TrackerPreparedSong.Prepare(songAsset.model)) Need(prepared.state.chainCount > 0, "Instrument FX not compiled"); });
                Check("wave domains FM fallback and supported mappings are explicit", () => { inst.model.family = InstrumentFamily.Synth; inst.model.parameters.type = InstrumentType.Synth; inst.model.synthMode = SynthMode.Subtractive; Tab(0); Need(WaveLabels(SoundEnumDomain.SavedAuthoring).Length == 5 && WaveLabels(SoundEnumDomain.NativeDirect).Length == 7, "Wave domains merged"); Need(window.InstrumentDestinations(false).All(t => !t.target.parameter.StartsWith("fmOperators.", StringComparison.Ordinal)), "Inactive FM targets offered"); inst.model.synthMode = SynthMode.FM; inst.model.parameters.type = InstrumentType.FM; inst.model.parameters.fmAlgorithm = 7; window.BuildPane(); Need(window.rootVisualElement.Q("fm-algorithm") != null, "FM algorithm picker missing"); Need(window.InstrumentDestinations(false).Any(t => t.target.parameter == "fmOperators.3.level") && !window.InstrumentDestinations(false).Any(t => t.target.parameter == "pulseWidth"), "FM applicability"); using (var prepared = TrackerPreparedSong.Prepare(songAsset.model)) Need(prepared.diagnostics.Any(s => s.Contains("FM_ALGORITHM_FALLBACK")), "Fallback diagnostic missing"); inst.model.synthMode = SynthMode.Subtractive; inst.model.parameters.type = InstrumentType.Synth; inst.model.parameters.fmAlgorithm = 0; });
                Check("macro eight, typed endpoints, external declarations and curve widgets", () => { Tab(4); window.macroIndex = 7; window.BuildPane(); Click("add-macro-mapping"); Need(inst.model.macros[7].mappings.Count == 1, "Eighth macro inaccessible"); var mapping = inst.model.macros[7].mappings[0]; mapping.min = 1; mapping.max = .25f; mapping.quantum = .01f; mapping.lower = .1f; mapping.curvePoints.AddRange(new[] { new ModulationPoint { time = 0, value = 0 }, new ModulationPoint { time = 1, value = 1 } }); window.BuildPane(); Need(window.rootVisualElement.Q("macro-mapping-0-curve-points-canvas") != null, "Curve authoring absent"); Click("add-external-parameter"); Need(inst.model.externalParameters.Count == 1 && window.InstrumentDestinations(true).Count(t => t.target.kind == ParameterKind.InstrumentMacro) == 8, "External map targets incomplete"); Undo.PerformUndo(); Need(inst.model.externalParameters.Count == 0, "External declaration Undo"); Undo.PerformRedo(); Need(inst.model.externalParameters.Count == 1, "External declaration Redo"); });
                Check("section preset create edit apply and complete Base Undo", () => { Tab(5); Click("new-instrument-preset"); var preset = inst.model.parameters.presets[0]; preset.volume = .6f; float before = inst.model.parameters.volume; Click("apply-instrument-preset"); Need(inst.model.parameters.volume == .6f && inst.model.parameters.presets.Count == 1, "Preset application dropped retained list"); Undo.PerformUndo(); Need(inst.model.parameters.volume == before && inst.model.parameters.presets[0].volume == .6f, "Apply Undo did not restore Base"); Undo.PerformRedo(); Need(inst.model.parameters.volume == .6f, "Apply Redo"); });
                Check("all instrument tabs build without authored mutation", () => { window.StopPreview(); string before = JsonUtility.ToJson(inst.model); foreach (var family in new[] { InstrumentFamily.Synth, InstrumentFamily.Sampler }) { inst.model.family = family; string f = JsonUtility.ToJson(inst.model); for (int tab = 0; tab < 6; tab++) Tab(tab); Need(f == JsonUtility.ToJson(inst.model), "Building controls mutated " + family); } inst.model.family = InstrumentFamily.Synth; });
                Check("rendered held-note scalar and macro Undo Redo preserve unrelated game overrides", () => CheckHeldInstrumentUndo(window, songAsset, inst, Need, DialValue, Tab));
                Check("Save writes both linked instrument assets to disk and reload", () => CheckInstrumentDiskSave(window, Need));
            }
            finally { window.StopPreview(); window.Close(); DestroyImmediate(window); Undo.ClearUndo(songAsset); Undo.ClearUndo(inst); DestroyImmediate(songAsset); DestroyImmediate(inst); DestroyImmediate(clip); }
            report.AppendLine($"TOTAL passed={pass} failed={fail}"); return report.ToString();
        }
        static void CheckHeldInstrumentUndo(ZTrackerModelWindow window, ZTrackerSong songAsset, ZTrackerInstrument inst, Action<bool, string> Need, Action<string, float> DialValue, Action<int> Tab)
        {
            // This fixture starts from fresh modern authored data. Named UI callbacks perform
            // the edits and Unity performs Undo/Redo; the real preserving renderer consumes each restored state.
            Undo.ClearUndo(inst); inst.model = NewInstrumentData(); window.macroIndex = 0; var q = inst.model.parameters; q.waveA = 3; q.waveB = 0; q.blend = 0; q.attack = q.decay = 0; q.sustain = 1; q.release = 1; q.volume = 1;
            songAsset.model = TrackerEngineCheck.FixtureSong(inst); var macro = inst.model.macros[0]; macro.mappings.Add(new Mapping { target = new ParameterTarget { kind = ParameterKind.Synth, parameter = "volume", units = "linear gain", instrumentId = inst.model.id }, min = 1, max = .25f });
            var l = new NativeArray<float>(256, Allocator.TempJob); var r = new NativeArray<float>(256, Allocator.TempJob);
            try
            {
                using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(songAsset.model)))
                {
                    engine.SendCommand(TrackerCommand.Audition(0, 60)); engine.SendCommand(TrackerCommand.SetParameter(0, TrackerParameter.Pan, -.3f)); engine.Render(l, r, 256); Need(engine.Compiled, "Held-note render was managed fallback");
                    float Level() { engine.Render(l, r, 256); double sum = 0; for (int i = 64; i < 256; i++) sum += l[i] * l[i] + r[i] * r[i]; return (float)Math.Sqrt(sum / 192); }
                    void Refresh() { long frame = engine.Snapshot.samplePosition, age = engine.Snapshot.voices[0].age; var next = TrackerPreparedSong.Prepare(songAsset.model); if (!engine.RefreshPrepared(next, out var reason)) { next.Dispose(); throw new Exception(reason); } Need(engine.Snapshot.samplePosition == frame && engine.Snapshot.voices[0].age == age, "Live change restarted held note or transport"); Need(Math.Abs(engine.Snapshot.parameterDirect[(int)TrackerParameter.Pan] + .3f) < .00001f, "Unrelated game pan override wiped"); }
                    float baseline = Level(); Tab(4); DialValue("macro-value", 1); Refresh(); float changed = Level(); Need(Math.Abs(changed / baseline - .25f) < .0001f, "Macro edit not audible"); Undo.PerformUndo(); Refresh(); Need(Math.Abs(Level() / baseline - 1) < .0001f, "Macro Undo UI restored but held sound did not"); Undo.PerformRedo(); Refresh(); Need(Math.Abs(Level() / baseline - .25f) < .0001f, "Macro Redo not audible");
                }
                Undo.ClearUndo(inst); inst.model = NewInstrumentData(); q = inst.model.parameters; q.waveA = 3; q.attack = q.decay = 0; q.sustain = 1; q.volume = 1; songAsset.model = TrackerEngineCheck.FixtureSong(inst);
                using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(songAsset.model)))
                {
                    engine.SendCommand(TrackerCommand.Audition(0, 60)); engine.SendCommand(TrackerCommand.SetParameter(0, TrackerParameter.Pan, -.3f)); engine.Render(l, r, 256);
                    float Level() { engine.Render(l, r, 256); double sum = 0; for (int i = 64; i < 256; i++) sum += l[i] * l[i] + r[i] * r[i]; return (float)Math.Sqrt(sum / 192); }
                    void Refresh() { long age = engine.Snapshot.voices[0].age; var next = TrackerPreparedSong.Prepare(songAsset.model); if (!engine.RefreshPrepared(next, out var reason)) { next.Dispose(); throw new Exception(reason); } Need(engine.Snapshot.voices[0].age == age && Math.Abs(engine.Snapshot.parameterDirect[(int)TrackerParameter.Pan] + .3f) < .00001f, "Scalar refresh destroyed held state/override"); }
                    float baseline = Level(); Tab(0); DialValue("synth-volume", .4f); Refresh(); Need(Math.Abs(Level() / baseline - .4f) < .0001f, "Scalar edit not audible"); Undo.PerformUndo(); Refresh(); Need(Math.Abs(Level() / baseline - 1) < .0001f, "Scalar Undo not audible"); Undo.PerformRedo(); Refresh(); Need(Math.Abs(Level() / baseline - .4f) < .0001f, "Scalar Redo not audible");
                    double step = engine.Snapshot.voices[0].step; DialValue("synth-fineTune", 1200); Refresh(); engine.Render(l, r, 256); Need(Math.Abs(engine.Snapshot.voices[0].step / step - 2) < .000001, "Held authored tuning base not advanced"); Undo.PerformUndo(); Refresh(); engine.Render(l, r, 256); Need(Math.Abs(engine.Snapshot.voices[0].step / step - 1) < .000001, "Held tuning Undo failed"); Undo.PerformRedo(); Refresh(); Need(Math.Abs(engine.Snapshot.voices[0].step / step - 2) < .000001, "Held tuning Redo failed");
                }
            }
            finally { l.Dispose(); r.Dispose(); }
        }
        static void CheckInstrumentDiskSave(ZTrackerModelWindow window, Action<bool, string> Need)
        {
            var originalSong = window.song; var originalInstrument = window.instrument;
            string folder = "Assets/ZTrackerP6InstrumentScratch-" + Guid.NewGuid().ToString("N");
            var a = CreateInstance<ZTrackerInstrument>(); var b = CreateInstance<ZTrackerInstrument>(); var song = CreateInstance<ZTrackerSong>();
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring(7)); a.schemaVersion = b.schemaVersion = song.schemaVersion = 1; a.model = NewInstrumentData(); b.model = NewInstrumentData(); a.model.family = InstrumentFamily.Sampler; a.model.parameters.type = InstrumentType.Sample; song.model = TrackerEngineCheck.FixtureSong(a); song.model.instruments.Add(b);
                AssetDatabase.CreateAsset(a, folder + "/Sampler.asset"); AssetDatabase.CreateAsset(b, folder + "/Synth.asset"); AssetDatabase.CreateAsset(song, folder + "/Song.asset"); AssetDatabase.SaveAssetIfDirty(a); AssetDatabase.SaveAssetIfDirty(b); AssetDatabase.SaveAssetIfDirty(song);
                window.song = song; window.instrument = b; a.model.name = "Saved sampler marker"; b.model.name = "Saved synth marker"; EditorUtility.SetDirty(a); EditorUtility.SetDirty(b); window.Save();
                Need(System.IO.File.ReadAllText(folder + "/Sampler.asset").Contains("Saved sampler marker"), "Save omitted previously edited sampler"); Need(System.IO.File.ReadAllText(folder + "/Synth.asset").Contains("Saved synth marker"), "Save omitted selected synth");
                Resources.UnloadAsset(song); Resources.UnloadAsset(a); Resources.UnloadAsset(b); song = AssetDatabase.LoadAssetAtPath<ZTrackerSong>(folder + "/Song.asset"); a = AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(folder + "/Sampler.asset"); b = AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(folder + "/Synth.asset"); Need(a.model.name == "Saved sampler marker" && b.model.name == "Saved synth marker" && song.model.instruments[0] == a && song.model.instruments[1] == b, "Disk reload lost values or references");
            }
            finally { window.song = originalSong; window.instrument = originalInstrument; var full = System.IO.Path.GetFullPath(folder); var assets = System.IO.Path.GetFullPath("Assets") + System.IO.Path.DirectorySeparatorChar; if (full.StartsWith(assets, StringComparison.OrdinalIgnoreCase)) AssetDatabase.DeleteAsset(folder); window.BuildPane(); }
        }
    }
}
