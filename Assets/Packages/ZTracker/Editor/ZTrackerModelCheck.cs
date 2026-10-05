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
        public const string ImplementationWitness = "P2-song-schema-v1-r5-independent-review-final";
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
                ReviewChecks();
                LegacyEditorChecks();
                PlaybackChecks();
                DemoChecks();
            }
            finally { foreach (var t in temporary) if (t != null) { Undo.ClearUndo(t); UnityEngine.Object.DestroyImmediate(t); } temporary.Clear(); }
            return "ZTracker P2 MODEL CHECK passed=" + passed + " failed=" + failed + "\n" + string.Join("\n",results);
        }
        static void ReviewChecks()
        {
            Check("null capture / copy / malformed preset refusal without publication", () => {
                var instrument = Temp<ZTrackerInstrument>(); instrument.presets.Add(null);
                var captured = ZTrackerMigration.Capture(instrument);
                Assert(captured.presets.Count == 1 && captured.presets[0] == null && ZTrackerMigration.Copy(captured).presets[0] == null,"JSON fabricated a preset");
                Assert(!ZTrackerMigration.Upgrade(instrument,out var error) && error.Contains("Null legacy preset") && instrument.schemaVersion == 0 && instrument.model == null && instrument.presets[0] == null,"Null preset replaced/published");
                Assert(!ZTrackerLegacyCompatibility.CanEdit(instrument,out _),"Malformed preset entered Undo/edit transaction");
                var song = Song(); song.patterns[0].cells[0] = null;
                Assert(ZTrackerMigration.Capture(song).patterns[0].cells[0] == null,"JSON fabricated a dense cell");
                Assert(!ZTrackerLegacyCompatibility.CanEdit(song,out _) && song.schemaVersion == 0 && song.patterns[0].cells[0] == null,"Malformed song edit changed source");
                song.channels[0] = null; Assert(!ZTrackerMigration.Upgrade(song,out _) && song.channels[0] == null && song.schemaVersion == 0,"Null channel manufactured/published");
                var valid = Temp<ZTrackerInstrument>(); var playable = Song(); playable.instruments.Add(valid);
                using (var prepared = ZTrackerLegacyCompatibility.Prepare(playable)) Assert(valid.fmOperators == null && valid.kitEntries == null,"Read-only projection normalized source nulls");
                using (var edit = new ZTrackerLegacyEdit(valid,"null-safe edit")) { valid.blend = .3f; edit.Commit(); }
                Assert(valid.legacyArchive.fmOperators == null && valid.legacyArchive.kitEntries == null,"Editor snapshot normalized legacy before archiving");
                using (var edit = new ZTrackerLegacyEdit(valid,"null-safe abort")) { valid.blend = .9f; }
                Assert(valid.legacyArchive.fmOperators == null && valid.model.parameters.fmOperators == null && valid.blend == .3f,"Abort lost nulls or canonical values");
                var undoNull = Temp<ZTrackerInstrument>(); Undo.IncrementCurrentGroup();
                using (var edit = new ZTrackerLegacyEdit(undoNull,"null-safe migration Undo")) { undoNull.blend = .6f; edit.Commit(); }
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert(undoNull.schemaVersion == 0 && undoNull.fmOperators == null && undoNull.kitEntries == null,"Migration Undo normalized null legacy arrays");
                Undo.PerformRedo(); Assert(undoNull.schemaVersion == 1 && undoNull.legacyArchive.fmOperators == null,"Migration Redo lost null archive");
            });
            Check("null archive survives actual scoped save/reload", () => {
                string scratch = "Assets/ZTrackerP2NullScratch-" + Guid.NewGuid().ToString("N"); string full = Path.GetFullPath(scratch), assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
                Assert(full.StartsWith(assets,StringComparison.OrdinalIgnoreCase),"Scratch outside Assets");
                ZTrackerSong song = null; ZTrackerInstrument instrument = null;
                try {
                    AssetDatabase.CreateFolder("Assets",Path.GetFileName(full)); song = ScriptableObject.CreateInstance<ZTrackerSong>(); song.channelCount = 1; song.channels.Add(new ZTrackerChannelConfig()); song.patterns.Add(new ZTrackerPattern("null",1,1)); song.patterns[0].cells[0] = null; song.orderList.Add(0);
                    instrument = ScriptableObject.CreateInstance<ZTrackerInstrument>(); instrument.macros = new[] {new ZTrackerInstrument.MacroDef {name = null,links = null}};
                    Upgrade(song); Upgrade(instrument);
                    AssetDatabase.CreateAsset(song,scratch + "/Song.asset"); AssetDatabase.CreateAsset(instrument,scratch + "/Instrument.asset");
                    EditorUtility.SetDirty(song); AssetDatabase.SaveAssetIfDirty(song); EditorUtility.SetDirty(instrument); AssetDatabase.SaveAssetIfDirty(instrument);
                    Resources.UnloadAsset(song); Resources.UnloadAsset(instrument);
                    AssetDatabase.ImportAsset(scratch + "/Song.asset",ImportAssetOptions.ForceSynchronousImport); AssetDatabase.ImportAsset(scratch + "/Instrument.asset",ImportAssetOptions.ForceSynchronousImport);
                    song = AssetDatabase.LoadAssetAtPath<ZTrackerSong>(scratch + "/Song.asset"); instrument = AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(scratch + "/Instrument.asset");
                    Assert(song.legacyArchive.patterns[0].cells[0] == null && song.legacyArchiveNulls.Contains("patterns/0/cells/0"),"Null archive cell lost on disk");
                    Assert(instrument.legacyArchive.macros[0].name == null && instrument.legacyArchive.macros[0].links == null && instrument.legacyArchive.kitEntries == null && instrument.legacyArchive.fmOperators == null,"Null string/struct-array member/array lost on disk");
                    Assert(!ZTrackerLegacyCompatibility.CanEdit(song,out _),"Malformed archive became editable after reload");
                } finally {
                    if (!Path.GetFullPath(scratch).Equals(full,StringComparison.OrdinalIgnoreCase) || !full.StartsWith(assets,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe scratch deletion");
                    AssetDatabase.DeleteAsset(scratch);
                    if (song != null && !AssetDatabase.Contains(song)) UnityEngine.Object.DestroyImmediate(song);
                    if (instrument != null && !AssetDatabase.Contains(instrument)) UnityEngine.Object.DestroyImmediate(instrument);
                }
            });
            Check("independent zero pan+delay / column-track-global scope retention", () => {
                var song = Song(); Upgrade(song);
                var line = new PatternLine {line = 0};
                line.notes.Add(new NoteCell {note = NoteKind.Note,pitch = 0,pan = new ColumnValue {kind = ValueKind.Value,value = 0},delayPresent = true,delay = 0,
                    sampleFx = new CommandData {present = true,valuePresent = true,identifier = "0R",value = 0,scope = CommandScope.Column,targetColumn = 0}});
                line.effects.Add(new EffectCell {column = 0,command = new CommandData {present = true,valuePresent = true,identifier = "0L",value = 0,scope = CommandScope.Track}});
                line.effects.Add(new EffectCell {column = 1,command = new CommandData {present = true,valuePresent = true,identifier = "ZT",value = 120,scope = CommandScope.Global}});
                song.model.patterns[0].tracks[0].WriteLine(line);
                Assert(ZTrackerModelValidation.Validate(song.model) == null,"Valid independent column/command data rejected");
                string before = JsonUtility.ToJson(song.model); var copy = ZTrackerMigration.Copy(song.model).patterns[0].tracks[0].lines[0];
                Assert(copy.notes[0].pan.HasPayload && copy.notes[0].delayPresent && copy.notes[0].delay == 0 && copy.notes[0].sampleFx.scope == CommandScope.Column && copy.effects[0].command.scope == CommandScope.Track && copy.effects[1].command.scope == CommandScope.Global,"Zero or command scope lost");
                Rejected(() => ZTrackerLegacyCompatibility.Prepare(song)); Assert(JsonUtility.ToJson(song.model) == before,"Unsupported modern commands/zero subcolumns silently altered");
            });
            Check("all modulation devices/targets / FX and external-map serialization", () => {
                var data = ZTrackerMigration.Convert(new InstrumentParameters {type = InstrumentType.Synth},"modern synth");
                var set = new ModulationSet {id = "modern",filterType = 3}; int index = 0;
                foreach (ModulationDeviceKind kind in Enum.GetValues(typeof(ModulationDeviceKind))) set.devices.Add(new ModulationDevice {
                    id = "device-" + index,kind = kind,target = (ModulationTarget)index++,operation = ModulationOperation.Add,hold = .1f,rate = 4,phase = .7f,min = -1,max = 2,curve = 2,
                    points = new List<ModulationPoint> {new ModulationPoint {time = 0,value = 0},new ModulationPoint {time = 1,value = 1}},sustainEnabled = true,sustainPosition = .5,loopEnabled = true,loop = SampleLoop.PingPong,loopStart = .25,loopEnd = 1 });
                data.modulation.Add(set); var chain = new AudioEffectChainData(); chain.nodes.Add(new AudioEffectNodeData {uid = "fx",p = new[] {.25f}}); data.fxChains.Add(chain);
                data.externalParameters.Add(new ExternalParameterMapping {externalId = "stand-in-vst/parameter-12",mapping = new Mapping {target = new ParameterTarget {kind = ParameterKind.Synth,parameter = "blend"},min = -1,max = 2,curve = 3}});
                Assert(ZTrackerModelValidation.Validate(data) == null,"Modern modulation/chains/map rejected");
                string before = JsonUtility.ToJson(data); Assert(JsonUtility.ToJson(ZTrackerMigration.Copy(data)) == before,"Modern fields not serialized");
                Rejected(() => ZTrackerLegacyCompatibility.Project(data)); Assert(JsonUtility.ToJson(data) == before,"Modern devices/map discarded");
            });
            Check("retained subtractive+FM fields and canonical preset count stable", () => {
                var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.Synth; i.waveA = 1; i.waveB = 3; i.blendMode = 2; i.blend = .43f; i.pmDepth = 3; i.waveBRatio = 1.7f; i.pulseWidth = .3f;
                i.unisonVoices = 4; i.unisonDetune = 13; i.unisonSpread = .5f; i.vibratoDepth = 31; i.vibratoRate = 7; i.vibratoFadeIn = .8f; i.vibratoRandomness = .2f;
                i.arpeggioEnabled = true; i.arpeggioNotes = new[] {0,3,9}; i.arpeggioSpeed = .13f; i.instFilterEnabled = true; i.instFilterMode = 2; i.instFilterCutoff = .4f; i.instFilterResonance = 1.2f; i.instDelaySend = .2f; i.instReverbSend = .3f;
                i.fmAlgorithm = 5; i.fmFeedback = .6f; i.fmOperators = Enumerable.Range(0,4).Select(n => new ZTrackerInstrument.FMOperatorData {freqRatio = n+1,freqFixed = n*100,level = .5f,waveform = n,attack = .1f,decay = .2f,sustain = .3f,release = .4f}).ToArray();
                i.unisonDetuneEnvelopeData = new ZUIEnvelopeData(0,20) {loopEnabled = true,loopMode = 2,loopStart = .2f,loopEnd = .8f};
                i.presets.Add(new ZTrackerInstrument.InstrumentPreset {ovrPulseWidth = true,pulseWidth = .65f,ovrVibrato = false,vibratoDepth = 99}); Upgrade(i);
                var p = i.model.parameters;
                Assert(p.waveA == 1 && p.waveB == 3 && p.blendMode == 2 && p.blend == .43f && p.pmDepth == 3 && p.waveBRatio == 1.7f && p.pulseWidth == .3f,"Oscillator/blend fields lost");
                Assert(p.unisonVoices == 4 && p.unisonDetune == 13 && p.unisonSpread == .5f && p.vibratoDepth == 31 && p.vibratoRate == 7 && p.vibratoFadeIn == .8f && p.vibratoRandomness == .2f && p.arpeggioNotes[2] == 9 && p.arpeggioSpeed == .13f,"Unison/vibrato/arpeggio fields lost");
                Assert(p.instFilterEnabled && p.instFilterMode == 2 && p.instFilterCutoff == .4f && p.instFilterResonance == 1.2f && p.instDelaySend == .2f && p.instReverbSend == .3f && p.fmAlgorithm == 5 && p.fmFeedback == .6f && p.fmOperators.Length == 4 && p.fmOperators[3].freqFixed == 300 && p.fmOperators[3].release == .4f && p.unisonDetuneEnvelopeData.loopMode == 2,"Effects/FM/envelope fields lost");
                var song = Song(); song.instruments.Add(i); int count = song.instruments.Count; var sets = ZTrackerMigration.ResolveParameterSets(i.model);
                Assert(song.instruments.Count == count && sets.Count == 2 && sets[1].data.parameters.pulseWidth == .65f && sets[1].data.parameters.vibratoDepth == 31,"Preset slots/inheritance corrupted");
            });
            Check("canonical sampler projection ignores stale legacy wrappers", () => {
                var i = Temp<ZTrackerInstrument>(); var clip = AudioClip.Create("canonical PCM",32,1,48000,false); temporary.Add(clip); Upgrade(i);
                i.model.sampler.volume = .31f; i.model.sampler.pan = -.25f; i.model.sampler.samples[0].pcm = clip;
                i.model.sampler.samples[0].baseNote = i.model.sampler.zones[0].baseNote = 63; i.model.sampler.samples[0].fineTuneCents = 17;
                i.model.modulation[0].devices[0].attack = .25f; i.volume = .99f; i.baseNote = 12;
                string original = JsonUtility.ToJson(i);
                var projected = ZTrackerLegacyCompatibility.Prepare(i);
                try { Assert(projected.volume == .31f && projected.pan == -.25f && projected.sampleClip == clip && projected.baseNote == 63 && projected.fineTune == 17 && projected.attack == .25f,"Canonical sample edits not uploaded"); }
                finally { UnityEngine.Object.DestroyImmediate(projected); }
                Assert(JsonUtility.ToJson(i) == original,"Projection rewrote authority or archive");
            });
            Check("canonical synth parameters+mode and sample B projection", () => {
                var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.Synth; Upgrade(i); i.model.parameters.volume = .37f; i.model.parameters.waveA = 1;
                Assert(ZTrackerLegacyCompatibility.Project(i.model).volume == .37f,"Synth scalar rejected/stale");
                i.model.synthMode = SynthMode.FM; Assert(ZTrackerLegacyCompatibility.Project(i.model).type == InstrumentType.FM,"Synth mode ignored");
                i.OnBeforeSerialize(); i.model.parameters.fmOperators = new ZTrackerInstrument.FMOperatorData[4]; i.model.parameters.fmOperators[0].freqRatio = 3;
                ZTrackerLegacyCompatibility.RefreshView(i);
                Assert(i.fmOperators != null && i.fmOperators.Length == 4 && i.fmOperators[0].freqRatio == 3,"Stale null metadata erased a projected FM array");
                var b = AudioClip.Create("B",32,1,48000,false); temporary.Add(b);
                var data = ZTrackerMigration.Convert(new InstrumentParameters { sampleClipB = b },"sample");
                data.sampler.zones[0].blend.amount = .73f; data.sampler.zones[0].blend.baseNoteB = 69; data.sampler.zones[0].blend.pmDepth = 2.5f;
                var p = ZTrackerLegacyCompatibility.Project(data); Assert(p.blend == .73f && p.baseNoteB == 69 && p.pmDepth == 2.5f,"Canonical B shadowed");
            });
            Check("kit last uploaded duplicate / effective globals / zero refusal", () => {
                var clip = AudioClip.Create("drum",32,1,48000,false); temporary.Add(clip);
                var data = ZTrackerMigration.Convert(new InstrumentParameters { type = InstrumentType.Kit,volume = .2f,pan = .5f,kitEntries = new[] {
                    new ZTrackerInstrument.KitEntry {midiNote = 36,clip = clip,volume = 0,baseNote = 0}, new ZTrackerInstrument.KitEntry {midiNote = 36,clip = null,volume = 1} } },"kit");
                Assert(!data.sampler.zones[0].inactive && data.sampler.zones[1].inactive,"Null duplicate superseded uploaded mapping");
                Assert(data.sampler.volume == 1 && data.sampler.pan == 0 && data.parameters.volume == .2f && data.parameters.pan == .5f,"Ineffective legacy global became active");
                var p = ZTrackerLegacyCompatibility.Project(data); Assert(p.kitEntries[0].volume == 0 && p.kitEntries[0].baseNote == 0,"Authored zeros lost");
                data.sampler.samples[0].volume = .29f; data.modulation[0].devices[0].release = .6f; p = ZTrackerLegacyCompatibility.Project(data);
                Assert(p.kitEntries[0].volume == .29f && p.kitEntries[0].release == .6f,"Canonical kit shadowed");
                data.sampler.samples[0].volume = 0; Rejected(() => ZTrackerLegacyCompatibility.Project(data));
                data.sampler.samples[0].volume = .29f; data.sampler.volume = .5f; Rejected(() => ZTrackerLegacyCompatibility.Project(data));
            });
            Check("preset inherited modern blend/modulation and deep copies", () => {
                var b = AudioClip.Create("inherited B",32,1,48000,false); temporary.Add(b);
                var data = ZTrackerMigration.Convert(new InstrumentParameters { sampleClipB = b, presets = new List<ZTrackerInstrument.InstrumentPreset> {
                    new ZTrackerInstrument.InstrumentPreset {ovrSampleParams = true,sampleClipB = b,baseNote = 65,ovrAdsr = true,attack = .5f} } },"sampler");
                data.sampler.volume = .34f; data.sampler.zones[0].blend.amount = .76f; data.sampler.zones[0].blend.pmDepth = 3;
                data.modulation[0].filterType = 7; data.modulation[0].devices[0].hold = .2f;
                data.modulation[0].devices.Add(new ModulationDevice {id = "inherited-lfo",kind = ModulationDeviceKind.LFO,target = ModulationTarget.Pitch});
                string before = JsonUtility.ToJson(data); var sets = ZTrackerMigration.ResolveParameterSets(data); var variant = sets[1].data;
                Assert(sets[0].data.parameters.volume == .34f && sets[0].data.parameters.blend == .76f,"Base set inherited obsolete legacy values");
                Assert(variant.sampler.volume == .34f && variant.sampler.zones[0].blend.amount == .76f && variant.sampler.zones[0].blend.pmDepth == 3,"Sample override replaced inherited blend");
                Assert(variant.modulation[0].devices.Count == 2 && variant.modulation[0].filterType == 7 && variant.modulation[0].devices[0].hold == .2f && variant.modulation[0].devices[0].attack == .5f,"ADSR override replaced inherited modulation section");
                variant.modulation[0].devices[1].depth = .1f; variant.sampler.zones[0].blend.amount = .2f;
                Assert(JsonUtility.ToJson(data) == before && sets[0].data.sampler.zones[0].blend.amount == .76f,"Resolved sets alias authoring/each other");
            });
            Check("repeated gesture rejected commit restores complete original", () => {
                var i = Temp<ZTrackerInstrument>(); string before = JsonUtility.ToJson(i);
                using (var edit = new ZTrackerLegacyEdit(i,"repeated gesture")) { i.attack = .2f; edit.Commit(); i.attack = -1; Rejected(edit.Commit); }
                Assert(JsonUtility.ToJson(i) == before,"Failed second move left invalid wrapper/authority");
            });
            Check("canonical macro edits / fifth macro and nonlinear mapping refusal", () => {
                var data = ZTrackerMigration.Convert(new InstrumentParameters {type = InstrumentType.Synth},"macros");
                data.macros[1].value = .4f; var p = ZTrackerLegacyCompatibility.Project(data);
                Assert(p.macros.Length == 2 && p.macros[1].defaultValue == .4f && p.macros[0].name == "Macro 1","Macro edit not projected/padding corrupted");
                data.macros[4].value = .2f; Rejected(() => ZTrackerLegacyCompatibility.Project(data)); data.macros[4].value = 0;
                data.macros[1].mappings.Add(new Mapping {target = new ParameterTarget {kind = ParameterKind.Synth,parameter = "blend",units = "legacy parameter units"},legacyLink = "blend",curve = 2});
                Rejected(() => ZTrackerLegacyCompatibility.Project(data));
            });
            Check("invalid modulation enum/shape and preserved legacy native capacities", () => {
                var data = ZTrackerMigration.Convert(new InstrumentParameters(),"bad"); data.modulation[0].devices[0].kind = (ModulationDeviceKind)999;
                Assert(ZTrackerModelValidation.Validate(data) != null,"Unknown modulation kind accepted"); data.modulation[0].devices[0].kind = ModulationDeviceKind.AHDSR; data.modulation[0].devices[0].phase = float.NaN;
                Assert(ZTrackerModelValidation.Validate(data) != null,"Nonfinite modulation shape accepted");
                var i = Temp<ZTrackerInstrument>(); i.type = InstrumentType.FM; i.fmOperators = new ZTrackerInstrument.FMOperatorData[5]; string raw = JsonUtility.ToJson(i);
                Rejected(() => ZTrackerLegacyCompatibility.Prepare(i)); Assert(JsonUtility.ToJson(i) == raw,"Extra operators lost");
                i.type = InstrumentType.Synth; i.fmOperators = null; i.arpeggioEnabled = true; i.arpeggioNotes = new[] {0,1,2,3,4,5}; Rejected(() => ZTrackerLegacyCompatibility.Prepare(i));
                i.arpeggioEnabled = false; i.blendEnvelopeData = new ZUIEnvelopeData(0,1); i.blendEnvelopeData.enabled = true; i.blendEnvelopeData.points.Clear();
                for (int n = 0; n < 33; n++) i.blendEnvelopeData.points.Add(new ZUIEnvelopePoint(n / 32f,n / 32f,1));
                Rejected(() => ZTrackerLegacyCompatibility.Prepare(i));
                i.blendEnvelopeData = null; i.attack = -1; Rejected(() => ZTrackerLegacyCompatibility.Prepare(i)); i.attack = .01f;
                i.presets.Add(new ZTrackerInstrument.InstrumentPreset {ovrAdsr = true,attack = -1}); Rejected(() => ZTrackerLegacyCompatibility.Prepare(i)); i.presets.Clear();
                i.type = (InstrumentType)999; Upgrade(i); string preserved = JsonUtility.ToJson(i);
                Rejected(() => ZTrackerLegacyCompatibility.Prepare(i)); Assert(JsonUtility.ToJson(i) == preserved && i.model.diagnostics.Exists(d => d.StartsWith("Unknown legacy engine")),"Unknown engine silently converted to Sample");
            });
            Check("live refresh captured slot identity / repeated references / structural refusal", () => {
                Assert(ZTrackerPlayback.Current == null,"Another playback active"); var song = Song(); var first = Temp<ZTrackerInstrument>(); first.type = InstrumentType.Synth;
                var other = Temp<ZTrackerInstrument>(); other.type = InstrumentType.Synth; other.blend = .1f;
                song.instruments.AddRange(new[] {first,other,first}); Upgrade(first); Upgrade(other); Upgrade(song);
                ZTrackerPlayback playback = null;
                try {
                    Assert(ZTrackerPlayback.TryPlay(song,out playback,out var error),error);
                    var field = typeof(ZTrackerPlayback).GetField("playingSong",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); var uploaded = (ZTrackerSong)field.GetValue(playback);
                    song.model.instruments[0] = other; song.model.instruments[1] = first; first.model.parameters.blend = .8f;
                    playback.RefreshInstrument(first,-1);
                    Assert(uploaded.instruments[0].blend == .8f && uploaded.instruments[2].blend == .8f && uploaded.instruments[1].blend == .1f,"Mutable authoring list retargeted uploaded IDs");
                    string before = JsonUtility.ToJson(uploaded); first.model.parameters.presets.Add(new ZTrackerInstrument.InstrumentPreset());
                    Rejected(() => playback.RefreshInstrument(first,-1)); Assert(JsonUtility.ToJson(uploaded) == before,"Structural refresh changed snapshot before refusal");
                } finally { if (playback != null) playback.Stop(); }
            });
            Check("exact legacy sample-bank limit / null user-slot capacity", () => {
                var clip = AudioClip.Create("bank limit",8,1,48000,false); temporary.Add(clip);
                var instrument = Temp<ZTrackerInstrument>(); instrument.type = InstrumentType.Kit;
                instrument.kitEntries = new[] {new ZTrackerInstrument.KitEntry {midiNote = 36,clip = clip,volume = 1}};
                for (int n = 0; n < 511; n++) instrument.presets.Add(new ZTrackerInstrument.InstrumentPreset());
                var song = Song(); song.instruments.Add(instrument);
                using (var prepared = ZTrackerLegacyCompatibility.Prepare(song)) Assert(prepared.song.instruments.Count == 1,"Exactly 512 uploads incorrectly refused");
                instrument.presets.Add(new ZTrackerInstrument.InstrumentPreset()); Rejected(() => ZTrackerLegacyCompatibility.Prepare(song));
                song.instruments.Clear(); for (int n = 0; n < 513; n++) song.instruments.Add(null);
                Rejected(() => ZTrackerLegacyCompatibility.Prepare(song));
            });
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
                        foreach (string file in new[] {"Song.asset","Synth.asset"})
                        {
                            string sourceGuid = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(roots[n] + "/" + file + ".meta"),@"(?m)^guid: (\w+)").Groups[1].Value;
                            string scratchGuid = AssetDatabase.AssetPathToGUID(folder + "/" + file);
                            Assert(sourceGuid.Length == 32 && scratchGuid.Length == 32 && sourceGuid != scratchGuid,"Scratch reused/missed an asset GUID");
                        }
                        song.instruments[0] = inst; Upgrade(inst); Upgrade(song); Assert(song.model.tracks.Count == 2 && song.model.patterns[0].tracks[0].lines.Count == 4,"Demo shape"); Assert(song.model.patterns[0].tracks[0].lines.Select(l => l.notes[0].pitch).SequenceEqual(new[] {60,64,67,72}),"Demo notes"); Assert(inst.model.macros.Length == 8 && song.model.bpm == 120 && song.model.linesPerBeat == 4 && song.model.ticksPerLine == 6,"Demo defaults");
                        string id = song.model.id, iid = inst.model.id; string before = JsonUtility.ToJson(song.model), beforeInstrument = JsonUtility.ToJson(inst.model);
                        EditorUtility.SetDirty(inst); AssetDatabase.SaveAssetIfDirty(inst); EditorUtility.SetDirty(song); AssetDatabase.SaveAssetIfDirty(song);
                        Resources.UnloadAsset(song); Resources.UnloadAsset(inst); AssetDatabase.ImportAsset(folder + "/Synth.asset",ImportAssetOptions.ForceSynchronousImport); AssetDatabase.ImportAsset(folder + "/Song.asset",ImportAssetOptions.ForceSynchronousImport);
                        song = AssetDatabase.LoadAssetAtPath<ZTrackerSong>(folder + "/Song.asset"); inst = AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(folder + "/Synth.asset");
                        Assert(song.schemaVersion == 1 && inst.schemaVersion == 1 && song.model.id == id && inst.model.id == iid,"Version/identity not persisted"); Assert(song.instruments[0] == inst && song.model.instruments[0] == inst,"Scratch reference points at live instrument"); Upgrade(song); Upgrade(inst); Assert(JsonUtility.ToJson(song.model) == before && JsonUtility.ToJson(inst.model) == beforeInstrument,"Reload upgrade changed song/instrument payload"); using (var p = ZTrackerLegacyCompatibility.Prepare(song)) Assert(p.song.patterns[0].GetCell(0,0,1).note == 60,"Demo projection");
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
