using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Laubrary.ZTracker.Model
{
    [Serializable] public sealed class LegacySongPayload
    {
        public string songName;
        public int bpm, linesPerBeat, ticksPerRow, channelCount;
        public List<ZTrackerInstrument> instruments;
        public List<ZTrackerPattern> patterns;
        public List<int> orderList;
        public List<ZTrackerChannelConfig> channels;
    }

    public static class ZTrackerMigration
    {
        public const int CurrentVersion = 1;
        public const string CommandContract = "T-0017/COMMANDS.md revision 1 section 11";
        public static string Json(object value)
        {
            var paths = NullPaths(value);
            try { return JsonUtility.ToJson(value); }
            finally { RestoreNulls(value,paths); }
        }
        public static void Overwrite(object value, object target)
        {
            var paths = NullPaths(value);
            // Partial legacy payloads do not contain the target's sidecar. Its
            // previous paths must not erase newly projected non-null fields in
            // Unity's deserialization callback.
            if (target is ZTrackerSong song) song.serializedNulls = null;
            if (target is ZTrackerInstrument instrument) instrument.serializedNulls = null;
            JsonUtility.FromJsonOverwrite(Json(value),target);
            RestoreNulls(target,paths);
            if (target is ZTrackerSong s) { s.serializedNulls = NullPaths(s); s.serializedNulls.Remove(nameof(s.serializedNulls)); }
            if (target is ZTrackerInstrument i) { i.serializedNulls = NullPaths(i); i.serializedNulls.Remove(nameof(i.serializedNulls)); }
        }
        public static T Copy<T>(T value)
        {
            if (value == null) return default;
            var paths = NullPaths(value);
            try { var copy = JsonUtility.FromJson<T>(JsonUtility.ToJson(value)); RestoreNulls(copy,paths); return copy; }
            finally { RestoreNulls(value,paths); }
        }
        public static string Identity() => Guid.NewGuid().ToString("N");
        public static LegacySongPayload Capture(ZTrackerSong source)
        {
            var sourcePaths = NullPaths(source); var paths = NullPaths(source,typeof(LegacySongPayload));
            try { var copy = JsonUtility.FromJson<LegacySongPayload>(JsonUtility.ToJson(source)); RestoreNulls(copy,paths); return copy; }
            finally { RestoreNulls(source,sourcePaths); }
        }
        public static InstrumentParameters Capture(ZTrackerInstrument source)
        {
            var sourcePaths = NullPaths(source); var paths = NullPaths(source,typeof(InstrumentParameters));
            try { var copy = JsonUtility.FromJson<InstrumentParameters>(JsonUtility.ToJson(source)); RestoreNulls(copy,paths); return copy; }
            finally { RestoreNulls(source,sourcePaths); }
        }

        // Unity serializes a null managed record as a default record (and null
        // strings/collections as empty values). Explicit paths preserve that
        // distinction for clones and the immutable legacy archives on disk.
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static bool Serialized(FieldInfo field) => !field.IsStatic && !field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField),true));
        public static List<string> NullPaths(object value, Type shape = null)
        {
            var paths = new List<string>();
            void Visit(object current,Type type,string path)
            {
                if (current == null) { paths.Add(path); return; }
                if (current is string || type.IsPrimitive || type.IsEnum) return;
                if (current is UnityEngine.Object && path != "") return;
                if (current is IList list)
                {
                    for (int n = 0; n < list.Count; n++) Visit(list[n],list[n]?.GetType() ?? typeof(object),path + "/" + n);
                    return;
                }
                foreach (var field in type.GetFields(Fields))
                {
                    if (!Serialized(field)) continue;
                    var source = current.GetType().GetField(field.Name,Fields); if (source == null) continue;
                    // Unity object identity/null is already represented by Unity.
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                    var child = source.GetValue(current);
                    if (child is UnityEngine.Object) continue;
                    Visit(child,field.FieldType,path == "" ? field.Name : path + "/" + field.Name);
                }
            }
            if (value != null) Visit(value,shape ?? value.GetType(),"");
            return paths;
        }
        public static void RestoreNulls(object value,List<string> paths)
        {
            if (value == null || paths == null) return;
            object Restore(object current,string[] parts,int depth)
            {
                if (current == null || depth == parts.Length) return null;
                if (current is IList list)
                {
                    if (int.TryParse(parts[depth],out int index) && index >= 0 && index < list.Count) list[index] = Restore(list[index],parts,depth+1);
                }
                else
                {
                    var field = current.GetType().GetField(parts[depth],Fields);
                    if (field != null && Serialized(field)) field.SetValue(current,Restore(field.GetValue(current),parts,depth+1));
                }
                return current;
            }
            foreach (string path in paths) if (!string.IsNullOrEmpty(path)) Restore(value,path.Split('/'),0);
        }

        public static bool Upgrade(ZTrackerSong asset, out string error)
        {
            error = VersionError(asset.schemaVersion);
            if (error != null) return false;
            if (asset.schemaVersion == CurrentVersion) return true;
            try
            {
                var legacy = Capture(asset);
                var candidate = Convert(legacy);
                error = ZTrackerModelValidation.Validate(candidate);
                if (error != null) return false;
                asset.legacyArchive = Copy(legacy);
                asset.legacyArchiveNulls = NullPaths(legacy);
                asset.model = candidate;
                asset.schemaVersion = CurrentVersion;
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        public static bool Upgrade(ZTrackerInstrument asset, out string error)
        {
            error = VersionError(asset.schemaVersion);
            if (error != null) return false;
            if (asset.schemaVersion == CurrentVersion) return true;
            try
            {
                var legacy = Capture(asset);
                var candidate = Convert(legacy, asset.name);
                error = ZTrackerModelValidation.Validate(candidate);
                if (error != null) return false;
                asset.legacyArchive = Copy(legacy);
                asset.legacyArchiveNulls = NullPaths(legacy);
                asset.model = candidate;
                asset.schemaVersion = CurrentVersion;
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        public static string VersionError(int version) => version < 0 || version > CurrentVersion ? "Unsupported schema version " + version + "; payload is read-only." : null;

        public static SongData Convert(LegacySongPayload old, SongData identities = null)
        {
            if (old == null || old.channelCount < 1 || old.patterns == null || old.channels == null || old.orderList == null || old.instruments == null)
                throw new InvalidOperationException("Incomplete legacy song; retained without upgrade.");
            var result = new SongData { id = identities?.id ?? Identity(), name = old.songName, bpm = old.bpm, linesPerBeat = old.linesPerBeat, ticksPerLine = old.ticksPerRow, instruments = new List<ZTrackerInstrument>(old.instruments), provenance = "native-v0; saved dense stride=" + old.channelCount };
            string master = identities?.tracks.Find(t => t.kind == TrackKind.Master)?.id ?? Identity();
            for (int t = 0; t < old.channelCount; t++)
            {
                var c = t < old.channels.Count ? old.channels[t] : new ZTrackerChannelConfig();
                if (c == null) throw new InvalidOperationException("Null legacy channel " + t);
                var track = new TrackData { id = identities != null && t < identities.tracks.Count - 1 ? identities.tracks[t].id : Identity(), name = "Track " + (t+1), outputTrackId = master, preVolume = c.volume, prePan = c.pan, triggerMute = c.muted, visibleNoteColumns = c.noteColumnCount, visibleEffectColumns = c.fxColumnCount };
                for (int n = 0; n < 12; n++) track.columns.Add(new ColumnVisibility());
                result.tracks.Add(track);
            }
            result.tracks.Add(new TrackData { id = master, name = "Master", kind = TrackKind.Master, visibleNoteColumns = 0, visibleEffectColumns = 0 });
            for (int p = 0; p < old.patterns.Count; p++)
            {
                var dense = old.patterns[p];
                if (dense == null || dense.cells == null) throw new InvalidOperationException("Null legacy pattern " + p);
                var pattern = new PatternData { id = identities != null && p < identities.patterns.Count ? identities.patterns[p].id : Identity(), name = dense.name, lineCount = dense.rowCount };
                for (int t = 0; t < old.channelCount; t++)
                {
                    var pt = new PatternTrack { trackId = result.tracks[t].id };
                    for (int r = 0; r < dense.rowCount; r++)
                    {
                        int i = r * old.channelCount + t;
                        if (i >= dense.cells.Count || dense.cells[i] == null) { result.diagnostics.Add("Missing dense cell " + p + "/" + r + "/" + t + "; empty projection, archive retained."); continue; }
                        var cell = dense.cells[i];
                        var line = Convert(cell, result.tracks[t], r, p + "/" + t + "/" + r);
                        if (line.HasPayload) pt.lines.Add(line);
                    }
                    pattern.tracks.Add(pt);
                }
                pattern.tracks.Add(new PatternTrack { trackId = master });
                if (dense.cells.Count != dense.rowCount * old.channelCount) result.diagnostics.Add("Pattern " + p + " dense length mismatch; original tail retained in archive.");
                result.patterns.Add(pattern);
            }
            for (int s = 0; s < old.orderList.Count; s++)
            {
                int p = old.orderList[s];
                if (p < 0 || p >= result.patterns.Count) throw new InvalidOperationException("Legacy sequence references missing pattern " + p);
                result.sequence.Add(new SequenceSlot { id = identities != null && s < identities.sequence.Count ? identities.sequence[s].id : Identity(), patternId = result.patterns[p].id });
            }
            return result;
        }

        public static PatternLine Convert(ZTrackerCellSerialized cell, TrackData track, int row, string address)
        {
            var line = new PatternLine { line = row };
            int notes = Math.Max(track.visibleNoteColumns, 1 + (cell.extraNotes?.Count ?? 0));
            for (int n = 0; n < notes; n++)
            {
                int raw = n == 0 ? cell.note : n-1 < (cell.extraNotes?.Count ?? 0) ? cell.extraNotes[n-1] : -1;
                var note = new NoteCell { column = n, inactive = n >= track.visibleNoteColumns, note = raw == -1 ? NoteKind.Empty : raw == 127 ? NoteKind.Off : raw >= 0 && raw <= 119 ? NoteKind.Note : NoteKind.Legacy, pitch = raw >= 0 && raw <= 119 ? raw : 0, legacyNote = raw, instrumentPresent = cell.instrument != -1, instrument = cell.instrument };
                if (cell.volume != 255) note.volume = new ColumnValue { kind = cell.volume >= 0 && cell.volume <= 64 ? ValueKind.Value : ValueKind.Legacy, value = cell.volume <= 64 ? cell.volume * 2 : 128, hasLegacy = true, legacyValue = cell.volume };
                if (note.HasPayload) line.notes.Add(note);
            }
            int fx = Math.Max(track.visibleEffectColumns, Math.Max(1 + (cell.extraEffectCmds?.Count ?? 0), 1 + (cell.extraEffectParams?.Count ?? 0)));
            for (int e = 0; e < fx; e++)
            {
                int cmd = e == 0 ? cell.effectCmd : e-1 < (cell.extraEffectCmds?.Count ?? 0) ? cell.extraEffectCmds[e-1] : 0;
                int value = e == 0 ? cell.effectParam : e-1 < (cell.extraEffectParams?.Count ?? 0) ? cell.extraEffectParams[e-1] : 0;
                if (cmd != 0 || value != 0) line.effects.Add(new EffectCell { column = e, inactive = e >= track.visibleEffectColumns, command = ConvertCommand(cmd, value, e, address) });
            }
            return line;
        }
        public static CommandData ConvertCommand(int cmd, int value, int column, string address)
        {
            var c = new CommandData { present = cmd != 0, valuePresent = true, value = value, hasLegacy = true, legacyCommand = cmd, legacyParameter = value, sourceColumn = column, sourceAddress = address, profile = "native-v0", unsupported = true, diagnostic = "LEGACY_UNRESOLVED" };
            // Only the table's unconditional scalar conversions are published. Native
            // source remains executable solely through the explicit compatibility bridge.
            if (cmd == 15 && value >= 0 && value <= 255)
            {
                if (value >= 32 || value <= 16)
                {
                    c.identifier = value >= 32 ? "ZT" : "ZK";
                    c.value = value == 0 ? 1 : value;
                    c.scope = CommandScope.Global;
                    c.profile = "renoise-p5-v1";
                    c.unsupported = false;
                    c.diagnostic = "LEGACY_TIMING_EVENT_DIFFERENCE";
                }
                else c.diagnostic = "LEGACY_TPL_RANGE";
            }
            else
            {
                string[] reasons = { "LEGACY_ORPHAN_ARGUMENT", "LEGACY_RATIO_SLIDE", "LEGACY_RATIO_SLIDE", "LEGACY_GLIDE_TIMING", "LEGACY_VIBRATO_CONVERSION" };
                c.diagnostic = cmd >= 0 && cmd < reasons.Length ? reasons[cmd] : cmd == 7 ? "LEGACY_TREMOLO_IGNORED" : cmd == 8 ? "LEGACY_PAN_QUANTIZED" : cmd == 10 ? "LEGACY_GAIN_SLIDE_SCOPE" : cmd == 11 ? "LEGACY_ORDER_JUMP" : cmd == 12 ? "LEGACY_GAIN_SET" : cmd == 13 ? "LEGACY_BREAK_BOUNDARY" : cmd == 16 ? "LEGACY_MACRO_OWNERSHIP" : cmd == 17 ? "LEGACY_MACRO_SLEW" : cmd == 18 ? "LEGACY_PRESET_UNRESOLVED" : "LEGACY_UNKNOWN_BYTE";
            }
            return c;
        }

        public static InstrumentData Convert(InstrumentParameters old, string name, string id = null)
        {
            var data = new InstrumentData { id = id ?? Identity(), name = name, family = old.type == InstrumentType.Synth || old.type == InstrumentType.FM ? InstrumentFamily.Synth : InstrumentFamily.Sampler, synthMode = old.type == InstrumentType.FM ? SynthMode.FM : SynthMode.Subtractive, parameters = Copy(old), provenance = "native-v0; " + CommandContract };
            if (!Enum.IsDefined(typeof(InstrumentType),old.type)) data.diagnostics.Add("Unknown legacy engine " + (int)old.type + " retained; projection requires explicit repair.");
            // Native kits ignore global level/pan. Keep those authored values in
            // parameters, with the diagnostic below; canonical effective globals
            // stay neutral instead of acquiring a new audible meaning in P3.
            if (old.type != InstrumentType.Kit) { data.sampler.volume = old.volume; data.sampler.pan = old.pan; }
            data.sampler.nna = old.type == InstrumentType.Kit && old.kitOverlap ? NewNoteAction.Continue : NewNoteAction.NoteOff;
            if (data.family == InstrumentFamily.Sampler && old.type == InstrumentType.Kit)
            {
                var entries = old.kitEntries ?? Array.Empty<ZTrackerInstrument.KitEntry>();
                for (int i = 0; i < entries.Length; i++)
                {
                    var k = entries[i]; bool displaced = false;
                    // Upload skips null PCM entries; they cannot supersede an earlier
                    // audible mapping. Only the last actually uploaded duplicate wins.
                    for (int j = i+1; j < entries.Length; j++) if (entries[j].midiNote == k.midiNote && entries[j].clip != null) displaced = true;
                    bool invalid = k.midiNote < 0 || k.midiNote > 119;
                    data.sampler.samples.Add(new SampleData { id = "sample-" + i, name = k.displayName, pcm = k.clip, baseNote = k.baseNote <= 0 ? 60 : k.baseNote, volume = k.volume <= 0 ? 1 : k.volume, legacyBaseNote = k.baseNote, legacyVolume = k.volume, legacyKitDefaults = true, modulationSet = i, inactive = displaced || invalid || k.clip == null, nna = data.sampler.nna });
                    data.sampler.zones.Add(new Keyzone { id = "zone-" + i, sample = i, noteMin = k.midiNote, noteMax = k.midiNote, baseNote = k.baseNote, keyTracking = false, inactive = displaced || invalid || k.clip == null, provenance = "kit-entry=" + i + "; fixed rate, no note transpose" });
                    data.modulation.Add(Adsr("kit-" + i, k.attack, k.decay, k.sustain, k.release));
                    if (displaced || invalid || k.clip == null) data.diagnostics.Add("Kit entry " + i + " retained inactive: " + (displaced ? "superseded note" : invalid ? "outside note profile" : "missing PCM; not uploaded"));
                }
                data.diagnostics.Add("Kit global level/modifiers/effects retained separately from effective native kit behavior.");
            }
            else if (data.family == InstrumentFamily.Sampler)
            {
                data.sampler.samples.Add(new SampleData { id = "sample-0", name = name, pcm = old.sampleClip, baseNote = old.baseNote, fineTuneCents = old.fineTune, modulationSet = 0 });
                data.sampler.zones.Add(new Keyzone { id = "zone-0", baseNote = old.baseNote, blend = old.sampleClipB == null ? null : new SampleBlendExtension { pcmB = old.sampleClipB, baseNoteB = old.baseNoteB, fineTuneBCents = old.fineTuneB, mode = old.blendMode, amount = old.blend, pmDepth = old.pmDepth, envelopeEnabled = old.blendEnvelope, attack = old.blendAttack, decay = old.blendDecay, sustain = old.blendSustain, release = old.blendRelease, blendEnvelope = Copy(old.blendEnvelopeData), pmEnvelope = Copy(old.pmDepthEnvelopeData) } });
                data.modulation.Add(Adsr("amplitude", old.attack, old.decay, old.sustain, old.release));
            }
            var macros = old.macros ?? Array.Empty<ZTrackerInstrument.MacroDef>();
            for (int m = 0; m < Math.Max(8, macros.Length); m++)
            {
                var macro = new InstrumentMacro { name = m < macros.Length ? macros[m].name : "Macro " + (m+1), value = m < macros.Length ? macros[m].defaultValue : 0 };
                if (m < macros.Length && macros[m].links != null) foreach (var link in macros[m].links)
                {
                    bool known = typeof(InstrumentParameters).GetField(link.parameterName ?? "")?.FieldType == typeof(float);
                    macro.mappings.Add(new Mapping { target = new ParameterTarget { kind = ParameterKind.Synth, parameter = link.parameterName, unresolved = !known, units = "legacy parameter units" }, min = link.minValue, max = link.maxValue, legacyLink = link.parameterName });
                    if (!known) data.diagnostics.Add("Unresolved macro link: " + link.parameterName);
                    bool direct = link.parameterName == "blend" || link.parameterName == "volume" || (data.family == InstrumentFamily.Synth && (link.parameterName == "pulseWidth" || link.parameterName == "waveBRatio"));
                    bool nextNote = old.type == InstrumentType.Synth && (link.parameterName == "pmDepth" || link.parameterName == "unisonDetune" || link.parameterName == "unisonSpread" || link.parameterName == "pan" || link.parameterName == "attack" || link.parameterName == "decay" || link.parameterName == "sustain" || link.parameterName == "release" || link.parameterName == "vibratoDepth" || link.parameterName == "vibratoRate");
                    if (known && !direct && !nextNote) data.diagnostics.Add("Legacy-only macro link not evaluated by this backend: " + link.parameterName);
                    if (link.parameterName == "volume" && (link.minValue <= 0 || link.maxValue <= 0)) data.diagnostics.Add("Legacy macro volume floors zero at 0.001; exact-zero semantics require P3/P4.");
                }
                if (m >= 4 && (macro.value != 0 || macro.mappings.Count != 0)) data.diagnostics.Add("Macro " + (m+1) + " retained legacy-only; this backend evaluates macros 1..4.");
                if (m < 8) data.macros[m] = macro; else { data.archivedMacros.Add(macro); data.diagnostics.Add("Macro " + m + " retained outside eight-macro bank."); }
            }
            return data;
        }
        static ModulationSet Adsr(string id, float a, float d, float s, float r)
        {
            return new ModulationSet { id = id, name = id, devices = new List<ModulationDevice> { new ModulationDevice { id = id + "-adsr", attack = a, decay = d, sustain = s, release = r } } };
        }

        public static List<CompiledParameterSet> ResolveParameterSets(InstrumentData authored)
        {
            var output = new List<CompiledParameterSet>();
            var effective = Copy(authored);
            if (authored.family == InstrumentFamily.Synth) effective.parameters.type = authored.synthMode == SynthMode.FM ? InstrumentType.FM : InstrumentType.Synth;
            else if (authored.parameters.type == InstrumentType.Sample)
            {
                var q = effective.parameters;
                q.volume = authored.sampler.volume; q.pan = authored.sampler.pan;
                if (authored.sampler.samples.Count > 0)
                {
                    var sample = authored.sampler.samples[0]; q.sampleClip = sample.pcm; q.baseNote = sample.baseNote; q.fineTune = sample.fineTuneCents;
                    if (sample.modulationSet >= 0 && sample.modulationSet < authored.modulation.Count)
                    {
                        var env = authored.modulation[sample.modulationSet].devices.Find(d => d.kind == ModulationDeviceKind.AHDSR && d.target == ModulationTarget.Volume);
                        if (env != null) { q.attack = env.attack; q.decay = env.decay; q.sustain = env.sustain; q.release = env.release; }
                    }
                }
                if (authored.sampler.zones.Count > 0)
                {
                    var b = authored.sampler.zones[0].blend; q.sampleClipB = b?.pcmB;
                    if (b != null)
                    {
                        q.baseNoteB = b.baseNoteB; q.fineTuneB = b.fineTuneBCents; q.blendMode = b.mode; q.blend = b.amount; q.pmDepth = b.pmDepth;
                        q.blendEnvelope = b.envelopeEnabled; q.blendAttack = b.attack; q.blendDecay = b.decay; q.blendSustain = b.sustain; q.blendRelease = b.release;
                        q.blendEnvelopeData = Copy(b.blendEnvelope); q.pmDepthEnvelopeData = Copy(b.pmEnvelope);
                    }
                }
            }
            output.Add(new CompiledParameterSet { id = authored.id + "/base", data = Copy(effective) });
            var presets = authored.parameters.presets;
            if (presets == null) return output;
            for (int i = 0; i < presets.Count; i++)
            {
                var p = presets[i]; if (p == null) throw new InvalidOperationException("Null preset " + i);
                var resolved = Copy(effective.parameters);
                Apply(p, resolved, p.ovrVolPan, "volume", "pan");
                Apply(p, resolved, p.ovrSampleParams, "sampleClip", "baseNote", "fineTune", "sampleClipB", "baseNoteB", "fineTuneB");
                Apply(p, resolved, p.ovrSynthParams, "waveA", "waveB", "blendMode", "unisonVoices", "unisonSpread");
                Apply(p, resolved, p.ovrBlend, "blend", "blendEnvelopeData"); Apply(p, resolved, p.ovrPulseWidth, "pulseWidth", "pulseWidthEnvelopeData");
                Apply(p, resolved, p.ovrBRatio, "waveBRatio", "waveBRatioEnvelopeData"); Apply(p, resolved, p.ovrPMDepth, "pmDepth", "pmDepthEnvelopeData"); Apply(p, resolved, p.ovrDetune, "unisonDetune", "unisonDetuneEnvelopeData");
                Apply(p, resolved, p.ovrAdsr, "attack", "decay", "sustain", "release");
                Apply(p, resolved, p.ovrVibrato, "vibratoDepth", "vibratoRate", "vibratoFadeIn", "vibratoRandomness");
                Apply(p, resolved, p.ovrEffects, "instFilterEnabled", "instFilterMode", "instFilterCutoff", "instFilterResonance", "instDelaySend", "instReverbSend");
                var set = Copy(authored); set.parameters = Copy(resolved);
                // Sampler A/B and amplitude overrides are resolved independently too.
                var legacyResolved = Convert(resolved, authored.name, authored.id);
                if (p.ovrSampleParams || p.ovrAdsr || p.ovrVolPan)
                {
                    if (authored.family == InstrumentFamily.Sampler && authored.parameters.type == InstrumentType.Sample && set.sampler.samples.Count > 0 && set.sampler.zones.Count > 0)
                    {
                        if (p.ovrSampleParams)
                        {
                            set.sampler.samples[0].pcm = resolved.sampleClip; set.sampler.samples[0].baseNote = resolved.baseNote; set.sampler.samples[0].fineTuneCents = resolved.fineTune; set.sampler.zones[0].baseNote = resolved.baseNote;
                            var b = set.sampler.zones[0].blend;
                            if (resolved.sampleClipB == null) set.sampler.zones[0].blend = null;
                            else
                            {
                                // A PCM/pitch override does not replace inherited blend
                                // settings, envelopes or any other sampler section.
                                if (b == null) b = Copy(legacyResolved.sampler.zones[0].blend);
                                b.pcmB = resolved.sampleClipB; b.baseNoteB = resolved.baseNoteB; b.fineTuneBCents = resolved.fineTuneB;
                                set.sampler.zones[0].blend = b;
                            }
                        }
                        int modulationIndex = set.sampler.samples[0].modulationSet;
                        if (p.ovrAdsr && modulationIndex >= 0 && modulationIndex < set.modulation.Count)
                        {
                            // Replace only the four overridden ADSR values, retaining
                            // hold, routing, extra devices, sustain/loop and filter data.
                            var env = set.modulation[modulationIndex].devices.Find(d => d.kind == ModulationDeviceKind.AHDSR && d.target == ModulationTarget.Volume);
                            if (env != null) { env.attack = resolved.attack; env.decay = resolved.decay; env.sustain = resolved.sustain; env.release = resolved.release; }
                        }
                        if (p.ovrVolPan) { set.sampler.volume = resolved.volume; set.sampler.pan = resolved.pan; }
                    }
                }
                if (set.sampler.zones.Count == 1 && set.sampler.zones[0].blend != null)
                { var b = set.sampler.zones[0].blend; if (p.ovrBlend) { b.amount = resolved.blend; b.blendEnvelope = Copy(resolved.blendEnvelopeData); } if (p.ovrPMDepth) { b.pmDepth = resolved.pmDepth; b.pmEnvelope = Copy(resolved.pmDepthEnvelopeData); } if (p.ovrSynthParams) b.mode = resolved.blendMode; }
                output.Add(new CompiledParameterSet { id = authored.id + "/preset-" + i, data = set });
            }
            return output;
        }
        static void Apply(ZTrackerInstrument.InstrumentPreset from, InstrumentParameters to, bool enabled, params string[] fields)
        {
            if (!enabled) return;
            foreach (string field in fields) typeof(InstrumentParameters).GetField(field).SetValue(to, from.GetType().GetField(field).GetValue(from));
        }
    }
}
