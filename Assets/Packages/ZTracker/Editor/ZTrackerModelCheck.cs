using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    // Invoked directly or by the one requested menu. No Test Runner or live-asset save.
    public static class ZTrackerModelCheck
    {
        public const string ImplementationWitness = "P2-song-schema-v1-r2";
        static int passed, failed;
        static readonly List<string> results = new List<string>();
        static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
        static void Check(string name, Action action) { try { action(); passed++; results.Add("PASS " + name); } catch (Exception ex) { failed++; results.Add("FAIL " + name + ": " + ex.Message); } }
        static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static T Temp<T>() where T : ScriptableObject { var value = ScriptableObject.CreateInstance<T>(); value.hideFlags = HideFlags.HideAndDontSave; temporary.Add(value); return value; }
        static ZTrackerSong Song(int columns = 1)
        {
            var s = Temp<ZTrackerSong>(); s.channelCount = 1; s.patterns.Add(new ZTrackerPattern("P",4,1)); s.orderList.Add(0); s.channels.Add(new ZTrackerChannelConfig { noteColumnCount = columns }); return s;
        }
        static void Upgrade(ZTrackerSong s) => Assert(ZTrackerMigration.Upgrade(s,out var e),e);
        static void Upgrade(ZTrackerInstrument i) => Assert(ZTrackerMigration.Upgrade(i,out var e),e);
        static void Rejected(Action action) { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Assert(rejected,"Expected explicit rejection"); }
        [MenuItem("Laubrary/ZTracker Model Check")]
        public static void Menu() => Debug.Log(Execute());
        public static string Execute()
        {
            passed = failed = 0; results.Clear();
            try
            {
                Check("missing version / future untouched / idempotence", () => {
                    var s = Song(); Assert(s.schemaVersion == 0,"Initializer masked legacy"); Upgrade(s); string json = JsonUtility.ToJson(s); Upgrade(s); Assert(JsonUtility.ToJson(s) == json,"Second upgrade changed payload"); s.schemaVersion = 42; json = JsonUtility.ToJson(s); Assert(!ZTrackerMigration.Upgrade(s,out _),"Future accepted"); Assert(json == JsonUtility.ToJson(s),"Future payload changed"); Rejected(() => ZTrackerLegacyCompatibility.Prepare(s));
                    var i = Temp<ZTrackerInstrument>(); i.schemaVersion = 42; json = JsonUtility.ToJson(i); Assert(!ZTrackerMigration.Upgrade(i,out _),"Future instrument accepted"); Assert(json == JsonUtility.ToJson(i),"Future instrument changed");
                });
                Check("three columns / explicit zero / OFF / fan-out", () => {
                    var s = Song(3); var c = s.patterns[0].cells[0]; c.note = 0; c.SetNote(1,-1); c.SetNote(2,127); c.instrument = 0; c.volume = 0; Upgrade(s);
                    var n = s.model.patterns[0].tracks[0].lines[0].notes; Assert(n.Count == 3 && n.All(x => x.instrumentPresent && x.instrument == 0 && x.volume.kind == ValueKind.Value && x.volume.value == 0),"Shared zero lost"); Assert(n[0].note == NoteKind.Note && n[0].pitch == 0 && n[1].note == NoteKind.Empty && n[2].note == NoteKind.Off,"Note empties confused"); var p = ZTrackerLegacyCompatibility.Project(s.model); Assert(p.patterns[0].cells[0].volume == 0,"Zero projection lost"); n[1].instrument = 1; Rejected(() => ZTrackerLegacyCompatibility.Project(s.model)); Assert(n[1].instrument == 1,"Rejection modified model");
                });
                Check("sparse 512 / last clear / read no mutation / zero effects", () => {
                    var s = Song(); s.patterns[0].Resize(512,1,1); s.channels[0].fxColumnCount = 0; s.patterns[0].cells[511].note = 60; Upgrade(s); var pt = s.model.patterns[0].tracks[0]; Assert(pt.lines.Count == 1 && pt.lines[0].line == 511,"Dense authoritative rows"); pt.ReadLine(17).notes.Add(new NoteCell { note = NoteKind.Note }); Assert(pt.lines.Count == 1,"Read inserted line"); pt.WriteLine(new PatternLine { line = 511 }); Assert(pt.lines.Count == 0,"Clear left empty line"); pt.WriteLine(new PatternLine { line = 511, events = new List<EventCell> { new EventCell { present = true, payload = "event" } } }); Assert(pt.lines.Count == 1,"Event-only line erased"); Assert(ZTrackerModelValidation.Validate(s.model) == null,"Sparse data invalid"); Rejected(() => ZTrackerLegacyCompatibility.Prepare(s));
                });
                Check("12 notes / 8 effects / invalid capacities / sparse row operations", () => {
                    var s = Song(12); s.channels[0].fxColumnCount = 8; Upgrade(s); Assert(ZTrackerModelValidation.Validate(s.model) == null,"Maximum column capacities invalid"); s.model.tracks[0].visibleNoteColumns = 13; Assert(ZTrackerModelValidation.Validate(s.model) != null,"13 notes accepted"); s.model.tracks[0].visibleNoteColumns = 12; s.model.tracks[0].visibleEffectColumns = 9; Assert(ZTrackerModelValidation.Validate(s.model) != null,"9 effects accepted"); s.model.tracks[0].visibleEffectColumns = 8;
                    var p = s.model.patterns[0]; var pt = p.tracks[0]; pt.WriteLine(new PatternLine {line = 1,notes = new List<NoteCell> {new NoteCell {column = 11,note = NoteKind.Note,pitch = 0}},effects = new List<EffectCell> {new EffectCell {column = 7,command = new CommandData {present = true,valuePresent = true,identifier = "ZT",value = 0,scope = CommandScope.Global}}},events = new List<EventCell> {new EventCell {present = true,payload = "payload"}} }); pt.automation.Add(new AutomationLane {id = "lane",target = new ParameterTarget {kind = ParameterKind.Mixer,parameter = "volume"},points = new List<AutomationPoint> {new AutomationPoint {line = 1.5,value = 0}}});
                    string original = JsonUtility.ToJson(p); var archive = ZTrackerPatternOperations.InsertLine(p,1); Assert(JsonUtility.ToJson(archive) == original && pt.lines[0].line == 2 && pt.automation[0].points[0].line == 2.5,"Insert lost payload/automation/archive"); ZTrackerPatternOperations.DeleteLine(p,1); Assert(JsonUtility.ToJson(p) == original,"Delete did not shift all payload categories"); var crop = ZTrackerPatternOperations.Resize(p,1); Assert(pt.lines.Count == 0 && pt.automation[0].points.Count == 0 && crop.tracks[0].lines[0].events[0].payload == "payload","Resize archive lost event"); p.lineCount = 513; Assert(ZTrackerModelValidation.Validate(s.model) != null,"513 lines accepted");
                });
                Check("hidden notes/effects / malformed dense tail retained", () => {
                    var s = Song(); var c = s.patterns[0].cells[0]; c.SetNote(3,126); c.SetEffect(5,18,0); c.volume = 254; s.patterns[0].cells.Add(new ZTrackerCellSerialized { note = 45 }); Upgrade(s); var l = s.model.patterns[0].tracks[0].lines[0]; Assert(l.notes.Exists(n => n.column == 3 && n.inactive && n.note == NoteKind.Legacy && n.legacyNote == 126),"Hidden note lost"); Assert(l.effects.Exists(e => e.column == 5 && e.inactive && e.command.legacyCommand == 18),"Hidden effect lost"); Assert(s.legacyArchive.patterns[0].cells.Count == 5,"Tail lost");
                });
                Check("committed command mapping / byte bases / zero argument", () => {
                    Assert(ZTrackerMigration.ConvertCommand(15,0,0,"a").identifier == "ZK" && ZTrackerMigration.ConvertCommand(15,0,0,"a").value == 1,"F00 mapping"); Assert(ZTrackerMigration.ConvertCommand(15,120,0,"a").identifier == "ZT","BPM mapping"); Assert(ZTrackerMigration.ConvertCommand(15,31,0,"a").unsupported,"TPL31 invented"); Assert(ZTrackerMigration.ConvertCommand(16,0,0,"a").diagnostic == "LEGACY_MACRO_OWNERSHIP","Decimal16 confused"); Assert(ZTrackerMigration.ConvertCommand(0x16,0,0,"a").diagnostic == "LEGACY_UNKNOWN_BYTE","Hex16 confused"); var s = Song(); s.patterns[0].cells[0].SetEffect(0,16,0); s.patterns[0].cells[1].effectParam = 17; Upgrade(s); Assert(s.model.patterns[0].tracks[0].lines.Count == 2,"Orphan/zero commands dropped"); Assert(ZTrackerLegacyCompatibility.Project(s.model).patterns[0].cells[1].effectParam == 17,"Raw orphan argument lost");
                });
                Check("effect collision / capacities / odd volume rejection", () => {
                    var s = Song(); s.channels[0].fxColumnCount = 2; s.patterns[0].cells[0].SetEffect(0,1,0); s.patterns[0].cells[0].SetEffect(1,2,0); Upgrade(s); Rejected(() => { using (var p = ZTrackerLegacyCompatibility.Prepare(s)) {} }); var n = s.model.patterns[0].tracks[0].lines[0]; n.notes.Add(new NoteCell { volume = new ColumnValue { kind = ValueKind.Value,value = 1 } }); Rejected(() => ZTrackerLegacyCompatibility.Project(s.model));
                });
                Check("AudioCore chain exact JSON / no Zounds assembly reference", () => {
                    var chain = new AudioEffectChainData(); chain.nodes.Add(new AudioEffectNodeData { type = (Laubrary.Zounds.ZoundEffectType)5, uid = "node", p = new[] {0f,.5f} }); chain.modifiers.Add(new AudioModifierData { uid = "mod", p = new[] {1f}, steps = new[] {0f,1f} }); chain.bindings.Add(new AudioModifierBindingData { modifierIndex = 0,nodeIndex = 0,paramIndex = 1,depth = .75f }); string json = JsonUtility.ToJson(chain); Assert(json == JsonUtility.ToJson(ZTrackerMigration.Copy(chain)),"Chain changed"); Assert(!typeof(SongData).Assembly.GetReferencedAssemblies().Any(a => a.Name == "com.Lautaro-Arino.Laubrary.Zounds"),"Zounds dependency"); var s = Song(); Upgrade(s); s.model.tracks[0].devices = chain; Assert(ZTrackerModelValidation.Validate(s.model) == null,"Portable chain invalid"); Rejected(() => ZTrackerLegacyCompatibility.Project(s.model));
                });
                Check("nested groups / route+send cycles / slot mutes", () => {
                    var s = Song(); Upgrade(s); var m = s.model; var master = m.tracks[1]; var group = new TrackData { id = "g",kind = TrackKind.Group,visibleNoteColumns = 0, outputTrackId = master.id }; var outer = new TrackData { id = "o",kind = TrackKind.Group,visibleNoteColumns = 0,outputTrackId = master.id }; var send = new TrackData { id = "s",kind = TrackKind.Send,visibleNoteColumns = 0,outputTrackId = master.id }; group.parentGroupId = outer.id; group.outputTrackId = ""; m.tracks.Add(group); m.tracks.Add(outer); m.tracks.Add(send); m.tracks[0].parentGroupId = group.id; m.tracks[0].outputTrackId = ""; foreach (var p in m.patterns) { p.tracks.Add(new PatternTrack { trackId = "g" }); p.tracks.Add(new PatternTrack { trackId = "o" }); p.tracks.Add(new PatternTrack { trackId = "s" }); } Assert(ZTrackerModelValidation.Validate(m) == null,"Nested groups invalid"); outer.parentGroupId = group.id; Assert(ZTrackerModelValidation.Validate(m)?.Contains("cycle") == true,"Ancestry cycle accepted"); outer.parentGroupId = ""; send.outputTrackId = group.id; group.sends.Add(new SendDestination { trackId = send.id }); Assert(ZTrackerModelValidation.Validate(m)?.Contains("cycle") == true,"Send cycle accepted"); send.outputTrackId = master.id; m.sequence.Add(new SequenceSlot { id = "slot2",patternId = m.patterns[0].id,mutedTrackIds = new List<string> { m.tracks[0].id } }); Assert(ZTrackerModelValidation.Validate(m) == null,"Slot mute invalid"); Assert(m.sequence[0].mutedTrackIds.Count == 0,"Slot mute leaked"); Rejected(() => ZTrackerLegacyCompatibility.Project(m));
                });
                Check("automation targets / interpolation / duplicate time", () => {
                    var s = Song(); Upgrade(s); var lane = new AutomationLane { id = "lane",target = new ParameterTarget {kind = ParameterKind.Mixer,parameter = "volume"},interpolation = AutomationInterpolation.Linear,points = new List<AutomationPoint> { new AutomationPoint {line = 0,value = 0}, new AutomationPoint {line = 1.5,value = 1} } }; s.model.patterns[0].tracks[0].automation.Add(lane); Assert(ZTrackerModelValidation.Validate(s.model) == null,"Automation invalid"); lane.interpolation = AutomationInterpolation.Step; Assert(ZTrackerModelValidation.Validate(s.model) == null,"Step invalid"); lane.points[1].line = 0; Assert(ZTrackerModelValidation.Validate(s.model) != null,"Duplicate timestamp accepted"); lane.points[1].line = 1; lane.target.kind = ParameterKind.Device; lane.target.deviceId = "missing"; Assert(ZTrackerModelValidation.Validate(s.model) != null,"Missing target accepted");
                });
                Check("single sample / shared PCM / paired B / preset clips", () => {
                    var a = AudioClip.Create("A",16,1,22050,false); var b = AudioClip.Create("B",16,2,48000,false); temporary.Add(a); temporary.Add(b); var i = Temp<ZTrackerInstrument>(); i.sampleClip = a; i.sampleClipB = b; i.baseNote = 57; i.baseNoteB = 69; i.fineTuneB = -12; i.blendMode = 3; i.blend = .8f; i.blendEnvelope = true; i.pmDepthEnvelopeData = new ZUIEnvelopeData(0,4); i.pmDepthEnvelopeData.loopEnabled = true; i.pmDepthEnvelopeData.loopMode = 2; Upgrade(i); Assert(i.model.sampler.samples.Count == 1 && i.model.sampler.zones.Count == 1,"B became a layer"); Assert(i.model.sampler.samples[0].pcm == a && i.model.sampler.zones[0].blend.pcmB == b,"PCM duplicated/replaced"); Assert(i.model.sampler.zones[0].blend.baseNoteB == 69 && i.model.sampler.zones[0].blend.requiresRenoiseInterchangeWarning,"B pitch/interchange lost"); Assert(ZTrackerLegacyCompatibility.Project(i.model).sampleClipB == b,"B projection lost");
                });
                Check("kit fixed pitch / duplicates / authored zero / envelopes", () => {
                    var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.Kit; i.kitOverlap = false; i.kitEntries = new[] { new ZTrackerInstrument.KitEntry { midiNote = 36,baseNote = 0,volume = 0,attack = .3f,sustain = .4f }, new ZTrackerInstrument.KitEntry {midiNote = 36,baseNote = 65,volume = .5f}, new ZTrackerInstrument.KitEntry {midiNote = 126,volume = 1} }; Upgrade(i); var d = i.model; Assert(d.sampler.zones.Count == 3 && d.sampler.zones[0].inactive && d.sampler.zones[2].inactive && !d.sampler.zones[1].keyTracking,"Kit layering/pitch changed"); Assert(d.sampler.samples[0].baseNote == 60 && d.sampler.samples[0].volume == 1 && d.parameters.kitEntries[0].volume == 0,"Effective zero defaults/raw data lost"); Assert(d.modulation[0].devices[0].attack == .3f && d.sampler.nna == NewNoteAction.NoteOff,"Kit envelope/NNA lost"); Assert(ZTrackerLegacyCompatibility.Project(d).kitEntries[0].baseNote == 0,"Raw kit base lost");
                });
                Check("FM enum / presets resolved independently / inactive sections", () => {
                    Assert((int)InstrumentType.Sample == 0 && (int)InstrumentType.Synth == 1 && (int)InstrumentType.Kit == 2 && (int)InstrumentType.FM == 3,"Legacy enum renumbered"); var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.FM; i.fmAlgorithm = 7; i.fmFeedback = .8f; i.waveA = 99; i.blendEnvelopeData = new ZUIEnvelopeData(0,1); i.presets.Add(new ZTrackerInstrument.InstrumentPreset { ovrVolPan = true,volume = .3f, ovrBlend = false,blend = .9f }); Upgrade(i); var sets = ZTrackerMigration.ResolveParameterSets(i.model); Assert(sets.Count == 2 && sets[1].data.parameters.volume == .3f && sets[1].data.parameters.blend == i.blend,"Preset inheritance wrong"); Assert(i.model.parameters.presets[0].blend == .9f && i.model.parameters.waveA == 99 && i.model.synthMode == SynthMode.FM,"Inactive/raw engine lost"); sets[1].data.parameters.blendEnvelopeData.points[0].value = .2f; Assert(i.blendEnvelopeData.points[0].value == 1,"Compiled parameters alias source"); Assert(i.model.parameters.presets.Count == 1,"Preset added instrument slots");
                });
                Check("eight macros / overflow retained / unknown maps", () => {
                    var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.Synth; i.macros = Enumerable.Range(0,9).Select(n => new ZTrackerInstrument.MacroDef { name = "M" + n,defaultValue = 0,links = new[] { new ZTrackerInstrument.MacroLink {parameterName = "unknown",minValue = -1,maxValue = 2} } }).ToArray(); Upgrade(i); Assert(i.model.macros.Length == 8 && i.model.archivedMacros.Count == 1,"Macro overflow lost"); Assert(i.model.macros[0].mappings[0].target.unresolved && i.model.macros[0].mappings[0].min == -1,"Unknown link erased"); i.model.externalParameters.Add(new ExternalParameterMapping { externalId = "vst-id",mapping = new Mapping { target = new ParameterTarget {kind = ParameterKind.Synth,parameter = "blend"},min = -1,max = 2,curve = 2} }); Assert(ZTrackerModelValidation.Validate(i.model) == null,"External map invalid"); Rejected(() => ZTrackerLegacyCompatibility.Project(i.model));
                });
                Check("sampler loop modes / layers / modulation / native refusal", () => {
                    var i = Temp<ZTrackerInstrument>(); var clip = AudioClip.Create("loop",32,1,48000,false); temporary.Add(clip); i.sampleClip = clip; Upgrade(i); foreach (SampleLoop loop in Enum.GetValues(typeof(SampleLoop))) { i.model.sampler.samples[0].loop = loop; i.model.sampler.samples[0].loopStartFrame = 2; i.model.sampler.samples[0].loopEndFrame = 20; i.model.sampler.samples[0].releaseExitsLoop = true; Assert(ZTrackerModelValidation.Validate(i.model) == null,"Loop invalid " + loop); } i.model.sampler.samples[0].interpolation = SampleInterpolation.Cubic; i.model.sampler.zones.Add(new Keyzone { id = "layer",sample = 0,velocityMin = 64 }); i.model.modulation[0].devices.Add(new ModulationDevice { id = "points",kind = ModulationDeviceKind.Multipoint,sustainEnabled = true,sustainPosition = .5,loopEnabled = true,loop = SampleLoop.PingPong,loopEnd = 1,points = new List<ModulationPoint> { new ModulationPoint {time = 0,value = 0},new ModulationPoint {time = 1,value = 1} } }); Assert(ZTrackerModelValidation.Validate(i.model) == null,"Layers/modulation invalid"); Rejected(() => ZTrackerLegacyCompatibility.Prepare(i));
                });
                Check("legacy edit migration / authoritative roundtrip / rollback", () => {
                    var s = Song(); using (var edit = new ZTrackerLegacyEdit(s,"zero note")) { s.patterns[0].cells[0].note = 0; s.patterns[0].cells[0].volume = 0; edit.Commit(); } Assert(s.schemaVersion == 1 && s.model.patterns[0].tracks[0].lines[0].notes[0].pitch == 0,"Edit not reconciled"); string json = JsonUtility.ToJson(s); using (var edit = new ZTrackerLegacyEdit(s,"abort")) { s.bpm = 0; } Assert(JsonUtility.ToJson(s) == json,"Rollback changed asset"); var i = Temp<ZTrackerInstrument>(); using (var edit = new ZTrackerLegacyEdit(i,"scalar")) { i.blend = .9f; edit.Commit(); } Assert(i.model.parameters.blend == .9f,"Instrument stale");
                });
                Check("read-only prepare does not mutate sources", () => {
                    var s = Song(); var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.Synth; s.instruments.Add(i); string a = JsonUtility.ToJson(s), b = JsonUtility.ToJson(i); using (var p = ZTrackerLegacyCompatibility.Prepare(s)) { Assert(p.song != s && p.song.instruments[0] != i,"Snapshot aliases authoring"); } Assert(JsonUtility.ToJson(s) == a && JsonUtility.ToJson(i) == b,"Read-only load migrated live assets");
                });
                LegacyEditorChecks();
                PlaybackChecks();
                DemoChecks();
            }
            finally { foreach (var t in temporary) if (t != null) { Undo.ClearUndo(t); UnityEngine.Object.DestroyImmediate(t); } temporary.Clear(); }
            return "ZTracker P2 MODEL CHECK passed=" + passed + " failed=" + failed + "\n" + string.Join("\n",results);
        }
        static void LegacyEditorChecks()
        {
            Check("complete migration+note Undo / Redo / dense resize Undo", () => {
                var s = Song(); s.patterns[0].cells[3].note = 67;
                Undo.IncrementCurrentGroup(); string original = JsonUtility.ToJson(s);
                using (var edit = new ZTrackerLegacyEdit(s,"note")) { s.patterns[0].cells[0].note = 60; edit.Commit(); }
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); string changed = JsonUtility.ToJson(s);
                Undo.PerformUndo(); Assert(JsonUtility.ToJson(s) == original,"Undo did not restore schema0 + dense payload");
                Undo.PerformRedo(); Assert(JsonUtility.ToJson(s) == changed,"Redo did not restore authoritative and legacy models");
                string beforeResize = JsonUtility.ToJson(s);
                Undo.IncrementCurrentGroup(); using (var edit = new ZTrackerLegacyEdit(s,"rows")) { s.patterns[0].Resize(2,1,1); edit.Commit(); }
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo(); Assert(JsonUtility.ToJson(s) == beforeResize && s.patterns[0].cells[3].note == 67,"Cropped dense notes not restored");
            });
            Check("actual editor note / paste / clear / interpolate / row shifts", () => {
                var s = Song(3); var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.Synth; s.instruments.Add(i);
                var w = Temp<ZTrackerWindow>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                void Set(string n, object v) => typeof(ZTrackerWindow).GetField(n,flags).SetValue(w,v);
                void Call(string n, params object[] args) => typeof(ZTrackerWindow).GetMethod(n,flags).Invoke(w,args);
                void Consistent() { Assert(ZTrackerLegacyCompatibility.CanEdit(s,out var error),"Editor model/view diverged: " + error); using (var prepared = ZTrackerLegacyCompatibility.Prepare(s)) Assert(prepared.song.schemaVersion == 0,"Prepared snapshot schema wrong"); }
                Set("song",s); Set("row",0); Set("sub",0); Set("step",0); Call("EnterNote",60); Consistent();
                Set("sub",2); Call("EnterNote",67); Consistent(); Call("Copy"); Set("row",1); Call("Paste"); Consistent(); Assert(s.patterns[0].cells[1].GetNote(2) == 67,"Paste hidden columns lost");
                Call("Clear"); Consistent(); Assert(s.patterns[0].cells[1].GetNote(2) == -1,"Clear failed");
                using (var edit = new ZTrackerLegacyEdit(s,"volume endpoints")) { s.patterns[0].cells[0].volume = 0; s.patterns[0].cells[3].volume = 64; edit.Commit(); }
                Set("sub",4); Set("anchorRow",0); Set("anchorTrack",0); Set("row",3); Call("Interpolate"); Consistent(); Assert(s.patterns[0].cells[1].volume == 21 && s.model.patterns[0].tracks[0].lines[1].notes.All(n => n.volume.value == 42),"Interpolation fanout wrong");
                Set("anchorRow",-1); Set("row",0); Call("ShiftRow",true); Consistent(); Assert(s.patterns[0].cells[1].note == 60,"Insert lost row"); Call("ShiftRow",false); Consistent(); Assert(s.patterns[0].cells[0].note == 60,"Delete lost row");
                Set("sub",0); Call("Transpose",1); Consistent(); Assert(s.patterns[0].cells[0].note == 61,"Transpose failed");
            });
            Check("actual editor track duplicate/remove / identity / sequence reorder", () => {
                var s = Song(); Upgrade(s); string originalTrack = s.model.tracks[0].id, pattern = s.model.patterns[0].id;
                var w = Temp<ZTrackerWindow>(); var f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic; typeof(ZTrackerWindow).GetField("song",f).SetValue(w,s);
                typeof(ZTrackerWindow).GetMethod("DuplicateTrack",f).Invoke(w,null); Assert(s.channelCount == 2 && s.model.tracks[0].id == originalTrack && s.model.tracks[1].id != originalTrack && s.model.patterns[0].id == pattern,"Duplicate identity collision");
                typeof(ZTrackerWindow).GetField("track",f).SetValue(w,0); string survivor = s.model.tracks[1].id; typeof(ZTrackerWindow).GetMethod("RemoveTrack",f).Invoke(w,null); Assert(s.model.tracks[0].id == survivor,"Removal changed surviving identity");
                using (var edit = new ZTrackerLegacyEdit(s,"new pattern")) { s.patterns.Add(new ZTrackerPattern("P2",4,1)); s.orderList.Add(1); edit.Commit(); }
                string first = s.model.sequence[0].id, second = s.model.sequence[1].id;
                using (var edit = new ZTrackerLegacyEdit(s,"reorder sequence")) { s.orderList.Reverse(); edit.Commit(); }
                Assert(s.model.sequence[0].id == second && s.model.sequence[1].id == first,"Sequence identity follows ordinal");
            });
            Check("actual editor shared fields / preset / kit / macro / envelope gesture", () => {
                var s = Song(3); var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.Synth; s.instruments.Add(i);
                var w = Temp<ZTrackerWindow>(); var f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(ZTrackerWindow).GetField("song",f).SetValue(w,s); typeof(ZTrackerWindow).GetField("instrument",f).SetValue(w,i);
                var change = typeof(ZTrackerWindow).GetMethod("Change",f);
                void Change(UnityEngine.Object owner,Action action) => change.Invoke(w,new object[] {owner,"probe edit",action,false,false});
                Change(s,() => { s.patterns[0].cells[0].instrument = 0; s.patterns[0].cells[0].volume = 0; }); Assert(s.model.patterns[0].tracks[0].lines[0].notes.Count == 3,"Shared field fanout lost");
                Change(i,() => i.presets.Add(new ZTrackerInstrument.InstrumentPreset { ovrBlend = true,blend = .7f })); Assert(ZTrackerMigration.ResolveParameterSets(i.model)[1].data.parameters.blend == .7f,"Editor preset stale");
                Change(i,() => { i.kitEntries = new[] { new ZTrackerInstrument.KitEntry {midiNote = 36,volume = .5f} }; i.macros = new[] { new ZTrackerInstrument.MacroDef {name = "M",links = Array.Empty<ZTrackerInstrument.MacroLink>()} }; i.blendEnvelopeData = new ZUIEnvelopeData(0,1); });
                typeof(ZTrackerWindow).GetMethod("BeginInstrumentGesture",f).Invoke(w,new object[] {"envelope"}); i.blendEnvelopeData.points[0].value = .2f; typeof(ZTrackerWindow).GetMethod("EndInstrumentGesture",f).Invoke(w,null);
                i.blendEnvelopeData.points[0].value = .4f; typeof(ZTrackerWindow).GetMethod("EndInstrumentGesture",f).Invoke(w,null); Assert(i.model.parameters.blendEnvelopeData.points[0].value == .4f,"Second drag update not reconciled");
                i.blendEnvelopeData.points[0].value = .2f; typeof(ZTrackerWindow).GetMethod("EndInstrumentGesture",f).Invoke(w,null);
                Assert(i.model.parameters.blendEnvelopeData.points[0].value == .2f && i.model.macros.Length == 8 && i.model.parameters.kitEntries.Length == 1,"Envelope/macro/kit stale");
                string before = JsonUtility.ToJson(i); i.model.externalParameters.Add(new ExternalParameterMapping {externalId = "unresolved",mapping = new Mapping {target = new ParameterTarget {unresolved = true}}}); string advanced = JsonUtility.ToJson(i);
                Change(i,() => i.blend = 1); Assert(JsonUtility.ToJson(i) == advanced,"Advanced edit guard mutated asset");
            });
        }
        static string Hash(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant(); }
        static void PlaybackChecks()
        {
            Check("persistent Event track / native once / mapping / text rejection", () => {
                var s = Song(); Upgrade(s); var master = s.model.tracks[1]; var ev = new TrackData {id = "event",name = "Events",kind = TrackKind.Event,visibleNoteColumns = 0,visibleEffectColumns = 0,outputTrackId = master.id}; s.model.tracks.Insert(1,ev);
                var pt = new PatternTrack {trackId = "event"}; pt.WriteLine(new PatternLine {line = 0,events = new List<EventCell> {new EventCell {present = true,payload = "hello"}}}); s.model.patterns[0].tracks.Insert(1,pt);
                Assert(ZTrackerModelValidation.Validate(s.model) == null,"Event data invalid"); Assert(!ZTrackerLegacyCompatibility.CanEdit(s,out _),"Old editor accepted event data");
                using (var prepared = ZTrackerLegacyCompatibility.Prepare(s))
                {
                    Assert(prepared.nativeTrackIds.Count == 2 && prepared.nativeTrackIds[1] == "event" && prepared.nativeNoteColumns[1] == -1,"Event channel mapping");
                    IntPtr context = ZTrackerNative.ZT_Create(48000);
                    try { prepared.PushToNative(context); ZTrackerNative.ZT_Play(context,0,0); ZTrackerNative.ZT_Process(context,new float[64],new float[64],64); int fired = 0; while (ZTrackerNative.ZT_PollEvent(context,out var value) == 1) if (value.type == (byte)ZTrackerEventType.EVENT_TRACK_FIRED) { fired++; Assert(value.stringPayload == "hello" && value.channelIndex == 1 && value.samplePosition == 0,"Event payload/address/sample position lost"); } Assert(fired == 1,"Event duplicated or missing"); }
                    finally { ZTrackerNative.ZT_Destroy(context); }
                }
                pt.lines[0].events[0].payload = new string('x',64); string json = JsonUtility.ToJson(s); Rejected(() => ZTrackerLegacyCompatibility.Prepare(s)); Assert(JsonUtility.ToJson(s) == json,"Long event text truncated");
            });
            Check("preview+runtime start / audition / snapshot refresh / advanced rejection", () => {
                Assert(ZTrackerPlayback.Current == null,"Another playback active; check did not take ownership"); var s = Song(); var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.Synth; i.waveA = 0; i.volume = .25f; s.instruments.Add(i); s.patterns[0].cells[0].note = 60; s.patterns[0].cells[0].instrument = 0; Upgrade(i); Upgrade(s); string original = JsonUtility.ToJson(s);
                ZTrackerPlayback playback = null;
                try
                {
                    Assert(ZTrackerPlayback.TryPlay(s,out playback,out var error),error); Assert(playback.TryMapNativeChannel(0,out var track,out var col) && track == s.model.tracks[0].id && col == 0,"Runtime mapping");
                    using (var edit = new ZTrackerLegacyEdit(i,"held blend")) { i.blend = .75f; edit.Commit(); } playback.RefreshInstrument(i,-1);
                    var field = typeof(ZTrackerPlayback).GetField("playingSong",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); var snapshot = (ZTrackerSong)field.GetValue(playback); Assert(snapshot != s && snapshot.instruments[0] != i && snapshot.instruments[0].blend == .75f,"Live polling snapshot stale");
                }
                finally { if (playback != null) playback.Stop(); }
                try { Assert(ZTrackerPlayback.TryAudition(i,60,out playback,out var error),error); Assert(playback.IsPlaying,"Audition not prepared"); }
                finally { if (playback != null) playback.Stop(); }
                Assert(JsonUtility.ToJson(s) == original,"Playback mutated song"); i.model.fxChains.Add(new AudioEffectChainData()); Assert(!ZTrackerPlayback.TryAudition(i,60,out playback,out _),"Advanced instrument silently downgraded"); Assert(ZTrackerPlayback.Current == null,"Rejected playback leaked host");
            });
        }
        static void DemoChecks()
        {
            string scratch = "Assets/ZTrackerP2Scratch-" + Guid.NewGuid().ToString("N");
            string full = Path.GetFullPath(scratch), assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            Assert(full.StartsWith(assets,StringComparison.OrdinalIgnoreCase),"Scratch outside Assets");
            var roots = new[] { "Assets/Packages/ZTracker/Samples~/ZTrackerDemo", "Assets/Demos/ZTrackerDemo" };
            var paths = roots.SelectMany(r => new[] { r + "/Song.asset",r + "/Synth.asset",r + "/Song.asset.meta",r + "/Synth.asset.meta" }).ToArray();
            var hashes = paths.Select(Hash).ToArray();
            try
            {
                AssetDatabase.CreateFolder("Assets",Path.GetFileName(full));
                for (int index = 0; index < roots.Length; index++)
                {
                    int n = index;
                    Check("saved demo " + roots[n] + " scratch save/reload/GUID", () => {
                        string folder = scratch + "/Set" + n; AssetDatabase.CreateFolder(scratch,"Set" + n);
                        // Copy YAML only. Fresh asset metas avoid duplicate live GUIDs. The
                        // serialized script GUID is copied verbatim, then references are rebound.
                        File.Copy(roots[n] + "/Song.asset",folder + "/Song.asset"); File.Copy(roots[n] + "/Synth.asset",folder + "/Synth.asset");
                        AssetDatabase.ImportAsset(folder + "/Synth.asset",ImportAssetOptions.ForceSynchronousImport); AssetDatabase.ImportAsset(folder + "/Song.asset",ImportAssetOptions.ForceSynchronousImport);
                        var song = AssetDatabase.LoadAssetAtPath<ZTrackerSong>(folder + "/Song.asset"); var inst = AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(folder + "/Synth.asset"); Assert(song != null && inst != null,"Retained script GUID/type failed");
                        song.instruments[0] = inst; Upgrade(inst); Upgrade(song); Assert(song.model.tracks.Count == 2 && song.model.patterns[0].tracks[0].lines.Count == 4,"Demo shape"); Assert(song.model.patterns[0].tracks[0].lines.Select(l => l.notes[0].pitch).SequenceEqual(new[] {60,64,67,72}),"Demo notes"); Assert(inst.model.macros.Length == 8 && song.model.bpm == 120 && song.model.linesPerBeat == 4 && song.model.ticksPerLine == 6,"Demo defaults");
                        string id = song.model.id, iid = inst.model.id; string before = JsonUtility.ToJson(song.model);
                        EditorUtility.SetDirty(inst); AssetDatabase.SaveAssetIfDirty(inst); EditorUtility.SetDirty(song); AssetDatabase.SaveAssetIfDirty(song);
                        Resources.UnloadAsset(song); Resources.UnloadAsset(inst); AssetDatabase.ImportAsset(folder + "/Synth.asset",ImportAssetOptions.ForceSynchronousImport); AssetDatabase.ImportAsset(folder + "/Song.asset",ImportAssetOptions.ForceSynchronousImport);
                        song = AssetDatabase.LoadAssetAtPath<ZTrackerSong>(folder + "/Song.asset"); inst = AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(folder + "/Synth.asset");
                        Assert(song.schemaVersion == 1 && inst.schemaVersion == 1 && song.model.id == id && inst.model.id == iid,"Version/identity not persisted"); Assert(song.instruments[0] == inst && song.model.instruments[0] == inst,"Scratch reference points at live instrument"); Upgrade(song); Upgrade(inst); Assert(JsonUtility.ToJson(song.model) == before,"Reload upgrade changed payload"); using (var p = ZTrackerLegacyCompatibility.Prepare(song)) Assert(p.song.patterns[0].GetCell(0,0,1).note == 60,"Demo projection");
                        Assert(File.ReadAllText(folder + "/Song.asset").Contains("c9234bbc80b22ff41a0ffb03c40c7bad") && File.ReadAllText(folder + "/Synth.asset").Contains("2b96e89e657daa44898819eea2fce0f6"),"Script GUID changed");
                    });
                }
            }
            finally
            {
                Check("all eight live demo files byte-identical", () => { for (int i = 0; i < paths.Length; i++) Assert(Hash(paths[i]) == hashes[i],"Live file changed " + paths[i]); });
                if (!Path.GetFullPath(scratch).Equals(full,StringComparison.OrdinalIgnoreCase) || !full.StartsWith(assets,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refuse unsafe scratch deletion");
                AssetDatabase.DeleteAsset(scratch);
            }
        }
    }
}
