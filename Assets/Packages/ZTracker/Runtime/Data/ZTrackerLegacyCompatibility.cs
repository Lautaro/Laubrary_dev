using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.ZTracker.Model
{
    // All allocation/projection lives on the main thread. A prepared snapshot owns
    // only detached managed Unity objects; AudioClip references remain shared.
    public sealed class PreparedLegacySong : IDisposable
    {
        public ZTrackerSong song;
        public SongData sourceModel;
        public readonly List<string> nativeTrackIds = new List<string>();
        public readonly List<int> nativeNoteColumns = new List<int>();
        public readonly List<ZTrackerInstrument> owned = new List<ZTrackerInstrument>();
        public void PushToNative(IntPtr context)
        {
            song.PushLegacyToNative(context);
            if (sourceModel == null) return;
            ZTrackerNative.ZT_SetBeatTickInterval(context,sourceModel.beatTicks ? sourceModel.beatIntervalLines : 0);
            for (int t = 0; t < sourceModel.tracks.Count; t++) if (sourceModel.tracks[t].kind == TrackKind.Event)
            {
                int ch = nativeTrackIds.IndexOf(sourceModel.tracks[t].id);
                ZTrackerNative.ZT_SetChannelType(context,ch,1);
                for (int p = 0; p < sourceModel.patterns.Count; p++)
                {
                    var pt = sourceModel.patterns[p].tracks.Find(x => x.trackId == sourceModel.tracks[t].id);
                    foreach (var line in pt.lines) foreach (var ev in line.events) if (ev.present) ZTrackerNative.ZT_SetEventString(context,p,line.line,ch,ev.payload);
                }
            }
        }
        public void Dispose() { foreach (var i in owned) if (i != null) UnityEngine.Object.DestroyImmediate(i); owned.Clear(); if (song != null) UnityEngine.Object.DestroyImmediate(song); song = null; }
    }

    public static class ZTrackerLegacyCompatibility
    {
        static void Require(string error) { if (error != null) throw new InvalidOperationException(error); }
        static string Json(object value) => JsonUtility.ToJson(value);
        static bool Same(object a, object b) => Json(a) == Json(b);
        static bool MatchesLegacy(SongData model, LegacySongPayload legacy)
        {
            foreach (var p in legacy.patterns) if (p.cells == null || p.cells.Count != p.rowCount*legacy.channelCount) return false;
            var candidate = ZTrackerMigration.Convert(legacy,model);
            candidate.provenance = model.provenance; candidate.diagnostics = ZTrackerMigration.Copy(model.diagnostics);
            candidate.beatTicks = model.beatTicks; candidate.beatIntervalLines = model.beatIntervalLines;
            return Same(candidate,model);
        }

        public static LegacySongPayload Project(SongData model)
        {
            Require(ZTrackerModelValidation.Validate(model));
            if (model.diagnostics.Exists(d => d.StartsWith("Missing dense cell") || d.Contains("dense length mismatch"))) throw new InvalidOperationException("Malformed legacy dense payload is archived and read-only; explicit repair is required.");
            if (model.voiceCapacity != 128) throw new InvalidOperationException("Legacy backend requires 128 voices.");
            var legacy = new LegacySongPayload { songName = model.name, bpm = (int)model.bpm, linesPerBeat = model.linesPerBeat, ticksPerRow = model.ticksPerLine, channelCount = model.tracks.Count-1, instruments = new List<ZTrackerInstrument>(model.instruments), patterns = new List<ZTrackerPattern>(), channels = new List<ZTrackerChannelConfig>(), orderList = new List<int>() };
            if (model.bpm != legacy.bpm) throw new InvalidOperationException("Legacy tempo is integer; fractional BPM requires the new engine.");
            var master = model.tracks.Find(t => t.kind == TrackKind.Master);
            if (model.tracks[model.tracks.Count-1] != master) throw new InvalidOperationException("Legacy editor requires terminal master.");
            foreach (var track in model.tracks)
            {
                if (track.kind == TrackKind.Master) continue;
                if (track.kind != TrackKind.Sequencer || track.parentGroupId != "" || track.outputTrackId != master.id || track.outputMute || track.solo || track.postVolume != 1 || track.postPan != 0 || track.preWidth != 1 || track.sends.Count != 0 || track.devices.nodes.Count != 0 || track.devices.modifiers.Count != 0 || track.devices.bindings.Count != 0 || track.beatTicks)
                    throw new InvalidOperationException("Track " + track.name + " uses buses, sends, devices or mixer/event features unavailable in the legacy editor/backend.");
                legacy.channels.Add(new ZTrackerChannelConfig { volume = track.preVolume, pan = track.prePan, muted = track.triggerMute, noteColumnCount = track.visibleNoteColumns, fxColumnCount = track.visibleEffectColumns });
            }
            foreach (var p in model.patterns)
            {
                var dense = new ZTrackerPattern(p.name,p.lineCount,legacy.channelCount);
                for (int t = 0; t < legacy.channelCount; t++)
                {
                    var pt = p.tracks.Find(x => x.trackId == model.tracks[t].id);
                    if (pt.automation.Count != 0) throw new InvalidOperationException("Automation requires the new engine/editor.");
                    foreach (var line in pt.lines)
                    {
                        var cell = dense.GetCell(line.line,t,legacy.channelCount);
                        if (line.events.Count != 0) throw new InvalidOperationException("Persistent events require the new backend.");
                        bool shared = false; int instrument = -1, volume = 255;
                        foreach (var n in line.notes)
                        {
                            if (n.pan.HasPayload || n.delayPresent || n.sampleFx.HasPayload || n.parameterSetId != "" || n.legacyExtras != "") throw new InvalidOperationException("Independent pan/delay/sample-fx/preset overrides require the new editor/backend.");
                            int inst = n.instrumentPresent ? n.instrument : -1, vol = LegacyVolume(n.volume);
                            if (!shared) { instrument = inst; volume = vol; shared = true; }
                            else if (instrument != inst || volume != vol) throw new InvalidOperationException("Independent note-column instrument/volume is read-only in the legacy editor/backend.");
                            cell.SetNote(n.column, n.note == NoteKind.Empty ? -1 : n.note == NoteKind.Off ? 127 : n.note == NoteKind.Legacy ? n.legacyNote : n.pitch);
                        }
                        // A missing visible column is genuinely empty; shared row fields cannot represent it.
                        if (shared && (instrument != -1 || volume != 255)) for (int n = 0; n < model.tracks[t].visibleNoteColumns; n++) if (!line.notes.Exists(x => x.column == n)) throw new InvalidOperationException("Sparse column has independent empty shared fields; legacy projection refused.");
                        cell.instrument = instrument; cell.volume = volume;
                        foreach (var fx in line.effects)
                        {
                            var cmd = fx.command;
                            if (!cmd.hasLegacy) throw new InvalidOperationException("Renoise commands require P5; no native semantics are inferred.");
                            cell.SetEffect(fx.column,cmd.legacyCommand,cmd.legacyParameter);
                        }
                    }
                }
                legacy.patterns.Add(dense);
            }
            foreach (var slot in model.sequence)
            {
                if (slot.mutedTrackIds.Count != 0) throw new InvalidOperationException("Per-slot track mutes require the new backend/editor.");
                legacy.orderList.Add(model.patterns.FindIndex(p => p.id == slot.patternId));
            }
            // Exact representability gate includes hidden data, provenance, device/visibility
            // settings, inactive extras and neutral defaults. A second writable model is forbidden.
            var roundtrip = ZTrackerMigration.Convert(legacy,model);
            roundtrip.diagnostics = ZTrackerMigration.Copy(model.diagnostics);
            roundtrip.beatTicks = model.beatTicks; roundtrip.beatIntervalLines = model.beatIntervalLines;
            roundtrip.provenance = model.provenance;
            if (!Same(roundtrip,model)) throw new InvalidOperationException("Song contains data the legacy view cannot roundtrip exactly; retained read-only.");
            return legacy;
        }
        static int LegacyVolume(ColumnValue v)
        {
            if (!v.HasPayload) return 255;
            if (v.hasLegacy && ((v.kind == ValueKind.Value && v.value == v.legacyValue*2) || (v.kind == ValueKind.Legacy && v.value == 128))) return v.legacyValue;
            if (v.kind != ValueKind.Value || (v.value & 1) != 0) throw new InvalidOperationException("Volume cannot be represented exactly in legacy 0..64 units.");
            return v.value/2;
        }
        public static InstrumentParameters Project(InstrumentData model)
        {
            Require(ZTrackerModelValidation.Validate(model));
            var restored = ZTrackerMigration.Convert(model.parameters,model.name,model.id);
            if (!Same(restored,model)) throw new InvalidOperationException("Instrument zones, modulation, FX, macros, maps or parameters cannot roundtrip through the legacy editor/backend; retained read-only.");
            return ZTrackerMigration.Copy(model.parameters);
        }
        public static ZTrackerInstrument Prepare(ZTrackerInstrument source)
        {
            Require(ZTrackerMigration.VersionError(source.schemaVersion));
            var parameters = source.schemaVersion == 0 ? ZTrackerMigration.Capture(source) : Project(source.model);
            Require(ZTrackerModelValidation.Validate(ZTrackerMigration.Convert(parameters,source.name)));
            ValidateInstrumentUpload(parameters);
            var snapshot = ScriptableObject.CreateInstance<ZTrackerInstrument>(); snapshot.hideFlags = HideFlags.HideAndDontSave; snapshot.name = source.name;
            JsonUtility.FromJsonOverwrite(Json(parameters),snapshot);
            snapshot.legacyPrepared = true;
            return snapshot;
        }
        public static PreparedLegacySong Prepare(ZTrackerSong source)
        {
            Require(ZTrackerMigration.VersionError(source.schemaVersion));
            var prepared = new PreparedLegacySong();
            try
            {
                var model = source.schemaVersion == 0 ? ZTrackerMigration.Convert(ZTrackerMigration.Capture(source)) : ZTrackerMigration.Copy(source.model);
                prepared.sourceModel = model;
                var legacy = ProjectForNative(model);
                // Even legacy inputs pass a pure conversion first; no authoring asset is dirtied.
                if (source.schemaVersion == 0) Require(ZTrackerModelValidation.Validate(ZTrackerMigration.Convert(legacy)));
                CheckNativeLimits(legacy);
                prepared.song = ScriptableObject.CreateInstance<ZTrackerSong>(); prepared.song.hideFlags = HideFlags.HideAndDontSave;
                JsonUtility.FromJsonOverwrite(Json(legacy),prepared.song);
                prepared.song.legacyPrepared = true;
                for (int i = 0; i < legacy.instruments.Count; i++) if (legacy.instruments[i] != null)
                {
                    var inst = Prepare(legacy.instruments[i]); prepared.owned.Add(inst); prepared.song.instruments[i] = inst;
                }
                foreach (var track in model.tracks) if (track.kind != TrackKind.Master)
                {
                    int count = track.kind == TrackKind.Event ? 1 : track.visibleNoteColumns;
                    for (int n = 0; n < count; n++) { prepared.nativeTrackIds.Add(track.id); prepared.nativeNoteColumns.Add(track.kind == TrackKind.Event ? -1 : n); }
                }
                return prepared;
            }
            catch { prepared.Dispose(); throw; }
        }
        static LegacySongPayload ProjectForNative(SongData model)
        {
            Require(ZTrackerModelValidation.Validate(model));
            var sanitized = ZTrackerMigration.Copy(model);
            for (int t = 0; t < model.tracks.Count; t++) if (model.tracks[t].kind == TrackKind.Event)
            {
                var track = sanitized.tracks[t];
                if (track.triggerMute || track.outputMute || track.solo || track.beatTicks) throw new InvalidOperationException("Legacy event tracks cannot express mute/solo/per-track beat behavior.");
                track.kind = TrackKind.Sequencer; track.visibleNoteColumns = 1; track.name = "Track " + (t+1);
                track.columns.Clear(); for (int c = 0; c < 12; c++) track.columns.Add(new ColumnVisibility());
                foreach (var p in sanitized.patterns)
                {
                    var pt = p.tracks.Find(x => x.trackId == track.id);
                    foreach (var line in pt.lines)
                    {
                        if (line.effects.Count != 0 || line.events.FindAll(e => e.present).Count > 1) throw new InvalidOperationException("Legacy event lane cannot express effects or multiple events per line.");
                        foreach (var ev in line.events) if (ev.present && (string.IsNullOrEmpty(ev.payload) || ev.payload.Length > 63 || ev.payload.AnyNonAscii())) throw new InvalidOperationException("Legacy event text must be nonempty ASCII, at most 63 bytes; full text retained.");
                        line.events.Clear();
                    }
                    pt.lines.RemoveAll(l => !l.HasPayload);
                }
            }
            return Project(sanitized);
        }
        static bool AnyNonAscii(this string value) { foreach (char c in value) if (c < 32 || c > 126) return true; return false; }
        static void ValidateInstrumentUpload(InstrumentParameters p)
        {
            if (!Enum.IsDefined(typeof(InstrumentType),p.type)) throw new InvalidOperationException("Unknown legacy instrument engine.");
            var clips = new HashSet<AudioClip>();
            void Clip(AudioClip c) { if (c == null || !clips.Add(c)) return; if (c.loadType != AudioClipLoadType.DecompressOnLoad || c.loadState != AudioDataLoadState.Loaded || !c.GetData(new float[checked(c.samples*c.channels)],0)) throw new InvalidOperationException("PCM clip is not readable: " + c.name); }
            if (p.type == InstrumentType.Sample) { Clip(p.sampleClip); Clip(p.sampleClipB); }
            if (p.type == InstrumentType.Kit && p.kitEntries != null) foreach (var k in p.kitEntries) Clip(k.clip);
            if (p.presets != null) foreach (var preset in p.presets) { if (preset == null) throw new InvalidOperationException("Null preset retained; cannot upload."); if (p.type == InstrumentType.Sample && preset.ovrSampleParams) { Clip(preset.sampleClip); Clip(preset.sampleClipB); } }
            if (clips.Count > 512 || (p.type == InstrumentType.Kit && (p.kitEntries?.Length ?? 0) > 512)) throw new InvalidOperationException("Legacy sample capacity exceeded.");
        }
        static void CheckNativeLimits(LegacySongPayload song)
        {
            if (song.patterns.Count > 256 || song.orderList.Count > 256 || song.channelCount < 1) throw new InvalidOperationException("Legacy pattern/order capacity exceeded.");
            int channels = 0, slots = 0, samples = 0;
            var clips = new HashSet<AudioClip>();
            foreach (var c in song.channels) channels += c.noteColumnCount;
            if (channels > 32) throw new InvalidOperationException("Legacy 32-channel capacity exceeded.");
            foreach (var p in song.patterns)
            {
                if (p.rowCount > 256 || p.cells.Count != p.rowCount*song.channelCount) throw new InvalidOperationException("Legacy row/dense capacity mismatch; payload retained.");
                for (int t = 0; t < song.channelCount; t++) for (int row = 0; row < p.rowCount; row++)
                {
                    var c = p.GetCell(row,t,song.channelCount); var hit = new HashSet<int>();
                    for (int e = 0; e < song.channels[t].fxColumnCount; e++) if (c.GetEffectCmd(e) != 0)
                    {
                        if (c.GetEffectCmd(e) < 0 || c.GetEffectCmd(e) > 255 || c.GetEffectParam(e) < 0 || c.GetEffectParam(e) > 255) throw new InvalidOperationException("Legacy effect byte overflow.");
                        if (!hit.Add(e % song.channels[t].noteColumnCount)) throw new InvalidOperationException("Legacy effect collision at " + p.name + "/" + row + "/" + t + "; later-wins loss refused.");
                    }
                }
            }
            foreach (var i in song.instruments) if (i != null)
            {
                var p = i.schemaVersion == 0 ? ZTrackerMigration.Capture(i) : Project(i.model);
                slots += 1 + (p.presets?.Count ?? 0);
                void Clip(AudioClip c)
                {
                    if (c == null || !clips.Add(c)) return;
                    if (c.loadType != AudioClipLoadType.DecompressOnLoad || c.loadState != AudioDataLoadState.Loaded || !c.GetData(new float[checked(c.samples*c.channels)],0)) throw new InvalidOperationException("PCM clip must expose loaded Decompress On Load data: " + c.name);
                    samples++;
                }
                if (p.type == InstrumentType.Sample) { Clip(p.sampleClip); Clip(p.sampleClipB); if (p.presets != null) foreach (var preset in p.presets) if (preset.ovrSampleParams) { Clip(preset.sampleClip); Clip(preset.sampleClipB); } }
                if (p.type == InstrumentType.Kit && p.kitEntries != null) foreach (var k in p.kitEntries) { Clip(k.clip); if (k.clip != null) samples += 1 + (p.presets?.Count ?? 0); }
            }
            if (slots > 512 || samples > 512) throw new InvalidOperationException("Legacy instrument/sample bank capacity exceeded.");
        }

        // Synchronization is allowed only after exact projection has proved the model
        // contains no data omitted by the old UI. Caller owns one complete-object Undo.
        public static bool CanEdit(ZTrackerSong asset, out string error)
        {
            try { Require(ZTrackerMigration.VersionError(asset.schemaVersion)); if (asset.schemaVersion != 0) { Project(asset.model); if (!MatchesLegacy(asset.model,ZTrackerMigration.Capture(asset))) throw new InvalidOperationException("Legacy view is stale; rebuild the view before editing."); } error = null; return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        public static bool CanEdit(ZTrackerInstrument asset, out string error)
        {
            try { Require(ZTrackerMigration.VersionError(asset.schemaVersion)); if (asset.schemaVersion != 0) { var p = Project(asset.model); if (!Same(p,ZTrackerMigration.Capture(asset))) throw new InvalidOperationException("Legacy instrument view is stale; rebuild before editing."); } error = null; return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        public static void RefreshView(ZTrackerSong asset)
        { if (asset != null && asset.schemaVersion == ZTrackerMigration.CurrentVersion) { var p = Project(asset.model); if (!MatchesLegacy(asset.model,ZTrackerMigration.Capture(asset))) JsonUtility.FromJsonOverwrite(Json(p),asset); } }
        public static void RefreshView(ZTrackerInstrument asset)
        { if (asset != null && asset.schemaVersion == ZTrackerMigration.CurrentVersion) { var p = Project(asset.model); if (!Same(p,ZTrackerMigration.Capture(asset))) JsonUtility.FromJsonOverwrite(Json(p),asset); } }
        public static void Reconcile(ZTrackerSong asset, SongData before)
        {
            var candidate = ZTrackerMigration.Convert(ZTrackerMigration.Capture(asset),before);
            if (before != null) { candidate.beatTicks = before.beatTicks; candidate.beatIntervalLines = before.beatIntervalLines; }
            Require(ZTrackerModelValidation.Validate(candidate));
            asset.model = candidate; asset.schemaVersion = ZTrackerMigration.CurrentVersion;
        }
        public static void Reconcile(ZTrackerInstrument asset, InstrumentData before)
        {
            var candidate = ZTrackerMigration.Convert(ZTrackerMigration.Capture(asset),asset.name,before?.id);
            Require(ZTrackerModelValidation.Validate(candidate));
            asset.model = candidate; asset.schemaVersion = ZTrackerMigration.CurrentVersion;
        }
        public static void AssignDuplicateIdentity(ZTrackerInstrument copy)
        {
            if (copy.model == null) return;
            string old = copy.model.id; copy.model.id = ZTrackerMigration.Identity(); copy.model.name = copy.name;
            foreach (var macro in copy.model.macros) foreach (var map in macro.mappings) if (map.target.instrumentId == old) map.target.instrumentId = copy.model.id;
            foreach (var map in copy.model.externalParameters) if (map.mapping.target.instrumentId == old) map.mapping.target.instrumentId = copy.model.id;
        }
    }
}
