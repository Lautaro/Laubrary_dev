using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Laubrary.ZTracker.Renoise;
using Laubrary.Zounds;

namespace Laubrary.ZTracker.Editor
{
    public sealed class RenoiseSongConversion
    {
        public SongData Song;
        public readonly List<RenoiseIssue> Issues = new List<RenoiseIssue>();
        public readonly List<RenoiseSongDevice> Devices = new List<RenoiseSongDevice>();
    }

    public sealed class RenoiseSongDevice
    {
        public int TrackIndex, DeviceIndex;
        public string SourceKey = "", TargetId = "", Type = "", Name = "", Raw = "";
        public bool Mapped;
    }

    /// <summary>Deterministic conversion into managed authoring data. No asset IO, UI, or playback side effects.</summary>
    public static class RenoiseSongConverter
    {
        const string Profile = "renoise-p5-v1";
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public static RenoiseSongConversion Convert(RenoiseArchive archive, string identitySeed = "renoise")
        {
            if (archive == null || !archive.IsSong) throw new FormatException("Choose a Renoise song archive.");
            var result = Convert(archive.Xml, identitySeed);
            var entries = RenoiseCatalog.Create(archive).Entries;
            foreach (var device in result.Devices)
            {
                var entry = entries.FirstOrDefault(e => e.Kind == "Device" && e.NestingDepth == 0 && e.InstrumentIndex == -1 && e.TrackIndex == device.TrackIndex && e.DeviceIndex == device.DeviceIndex);
                if (entry != null) device.SourceKey = entry.Key;
            }
            return result;
        }

        public static RenoiseSongConversion Convert(XDocument document, string identitySeed = "renoise")
        {
            var root = document?.Root;
            if (root == null || root.Name.LocalName != "RenoiseSong") throw new FormatException("RenoiseSong root is required.");
            int revision = IntText((string)root.Attribute("doc_version"), 0);
            // Renoise 3.0+ uses the modern XML structure and subline automation coordinates.
            if (revision < 54 || revision > 67) throw new FormatException("Song conversion supports document revisions 54–67 (Renoise 3.0–3.5). Earlier documents require a separate structure/timing adapter.");
            var global = root.Element("GlobalSongData");
            int engine = Int(global, "PlaybackEngineVersion", 6);
            Require(engine >= 4 && engine <= 6, "Only LPB playback engines 4–6 are supported; older speed-based or future playback needs its own timing adapter.");
            var result = new RenoiseSongConversion();
            var song = result.Song = new SongData { id = Id(identitySeed + ":song"), name = Text(global, "SongName", "Imported song"), bpm = Number(global, "BeatsPerMin", revision < 63 ? 140 : revision == 63 ? 105 : 80), linesPerBeat = Int(global, "LinesPerBeat", 4), ticksPerLine = Int(global, "TicksPerLine", 12), beatTicks = false, provenance = "Renoise song revision " + revision, commandModelVersion = 1 };
            if (engine < 6) Issue(result, "OLDER_PLAYBACK_APPROXIMATE", "Notes and the initial LPB clock are imported. Older-engine command timing and playback behavior are not reproduced; all pattern commands are retained inactive, including tempo changes.", "Playback engine " + engine);
            Require(song.bpm >= 32 && song.bpm <= 999 && song.linesPerBeat >= 1 && song.linesPerBeat <= 256 && song.ticksPerLine >= 1 && song.ticksPerLine <= 16, "Initial tempo/lines/ticks exceed the supported range.");
            if (Bool(global, "ShuffleIsActive")) Issue(result, "GROOVE_UNMAPPED", "Groove timing is not reproduced; straight timing is used.", "Song");
            bool offsetLegacy = Bool(global, "SampleOffsetCompatibilityMode"), pitchLegacy = Bool(global, "PitchEffectsCompatibilityMode");
            int slots = Children(root.Element("Instruments")).Count;
            Require(slots <= 4096, "Too many instrument slots.");
            for (int i = 0; i < slots; i++) song.instruments.Add(null);
            var tracks = Children(root.Element("Tracks"));
            Require(tracks.Count > 0 && tracks.Count <= 128, "A song needs 1–128 tracks.");
            for (int i = 0; i < tracks.Count; i++)
            {
                var source = tracks[i];
                TrackKind kind;
                switch (source.Name.LocalName)
                {
                    case "SequencerTrack": kind = TrackKind.Sequencer; break;
                    case "SequencerGroupTrack": kind = TrackKind.Group; break;
                    case "SequencerSendTrack": kind = TrackKind.Send; break;
                    case "SequencerMasterTrack": kind = TrackKind.Master; break;
                    default: throw new FormatException("Unknown structural track type " + source.Name.LocalName);
                }
                var track = new TrackData { id = Id(identitySeed + ":track:" + i), name = Text(source, "Name", "Track " + (i + 1)), kind = kind, visibleNoteColumns = Int(source, "NumberOfVisibleNoteColumns", kind == TrackKind.Sequencer ? 1 : 0), visibleEffectColumns = Int(source, "NumberOfVisibleEffectColumns", 1), solo = Bool(source, "Soloed") };
                Require(track.visibleNoteColumns >= (kind == TrackKind.Sequencer ? 1 : 0) && track.visibleNoteColumns <= (kind == TrackKind.Sequencer ? 12 : 0) && track.visibleEffectColumns >= 0 && track.visibleEffectColumns <= 8, "Visible column limits exceeded on " + track.name);
                string state = Text(source, "State", "Active");
                track.triggerMute = state == "Muted"; track.outputMute = state == "Off";
                Require(state == "Active" || state == "Muted" || state == "Off", "Unknown track state " + state);
                for (int c = 0; c < 12; c++) track.columns.Add(new ColumnVisibility { volume = Bool(source, "VolumeColumnIsVisible", kind == TrackKind.Sequencer), pan = Bool(source, "PanningColumnIsVisible"), delay = Bool(source, "DelayColumnIsVisible"), sampleFx = Bool(source, "SampleEffectColumnIsVisible") });
                var rgb = Text(source, "Color", "128,128,128").Split(',');
                if (rgb.Length == 3 && byte.TryParse(rgb[0], out byte red) && byte.TryParse(rgb[1], out byte green) && byte.TryParse(rgb[2], out byte blue)) track.color = new UnityEngine.Color32(red, green, blue, 255);
                if (Number(source, "TrackDelay", 0) != 0) Issue(result, "TRACK_DELAY_UNMAPPED", "Track delay is retained in the source but is not applied.", track.name);
                song.tracks.Add(track);
            }
            Require(song.tracks.Count(t => t.kind == TrackKind.Master) == 1 && song.tracks.Count(t => t.kind == TrackKind.Sequencer) <= 64, "Exactly one master and at most 64 note tracks are required.");
            var master = song.tracks.First(t => t.kind == TrackKind.Master);
            // Renoise places each group AFTER its members. Find the first subsequent shallower track.
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = song.tracks[i]; int depth = Int(tracks[i], "GroupNestingLevel", 0);
                Require(depth >= 0 && depth < 128, "Invalid group nesting depth.");
                if (depth > 0)
                {
                    int parent = -1;
                    for (int j = i + 1; j < tracks.Count; j++) if (Int(tracks[j], "GroupNestingLevel", 0) < depth) { parent = j; break; }
                    Require(parent >= 0 && song.tracks[parent].kind == TrackKind.Group && Int(tracks[parent], "GroupNestingLevel", 0) == depth - 1, "Unresolved group ancestry on " + track.name);
                    track.parentGroupId = song.tracks[parent].id;
                }
                int route = Int(tracks[i], "TrackRouting", 0);
                if (track.kind != TrackKind.Master)
                {
                    if (route == -1) Require(track.parentGroupId != "", "Group routing without a group on " + track.name);
                    else if (route == 0) track.outputTrackId = master.id;
                    else Issue(result, "OUTPUT_ROUTE_UNMAPPED", "Non-default output route is replaced by the parent/master route.", track.name);
                }
                ReadDevices(result, tracks[i], i, identitySeed);
            }
            double headroom = Number(global, "GlobalTrackHeadroom", 0.501187205);
            if (Math.Abs(headroom - 1) > 1e-7) Issue(result, "HEADROOM_APPROXIMATE", "Renoise global headroom is not applied; set the final ZTracker mix level by ear.", "Song");
            var patterns = Children(root.Element("PatternPool")?.Element("Patterns"));
            Require(patterns.Count > 0 && patterns.Count <= 4096, "A song needs 1–4096 patterns.");
            long expandedCells = 0;
            for (int p = 0; p < patterns.Count; p++)
            {
                var source = patterns[p]; int length = Int(source, "NumberOfLines", 64);
                Require(length >= 1 && length <= 512, "Pattern length exceeds 1–512 lines.");
                var pattern = new PatternData { id = Id(identitySeed + ":pattern:" + p), name = Text(source, "Name", "Pattern " + p), lineCount = length };
                var sourceTracks = Children(source.Element("Tracks"));
                Require(sourceTracks.Count == tracks.Count, "Pattern " + p + " has a different track count from the song.");
                for (int t = 0; t < tracks.Count; t++)
                {
                    string location = "Pattern " + p + ", " + song.tracks[t].name;
                    var destination = new PatternTrack { trackId = song.tracks[t].id };
                    var resolved = ResolveAlias(patterns, p, t);
                    var seen = new HashSet<int>();
                    foreach (var row in Children(resolved.Element("Lines")))
                    {
                        int line = IntText((string)row.Attribute("index"), -1);
                        Require(line >= 0 && line < 512 && seen.Add(line), "Duplicate/out-of-range row at " + location);
                        // Renoise keeps old rows beyond a shortened pattern's playable length (seen in factory songs).
                        // They must not extend the music or make a valid song unimportable; the original archive keeps them.
                        if (line >= length) { Issue(result, "PATTERN_TAIL_ARCHIVED", "Rows beyond the current pattern length are retained only in the original source archive.", location); continue; }
                        expandedCells += 1 + (row.Element("NoteColumns")?.Elements().Count() ?? 0) + (row.Element("EffectColumns")?.Elements().Count() ?? 0);
                        Require(expandedCells <= 2000000, "Expanded aliases exceed the two-million-cell import safety limit.");
                        var converted = ReadLine(result, row, line, song.tracks[t], tracks[t], location, offsetLegacy, pitchLegacy);
                        if (engine < 6)
                        {
                            foreach (var command in converted.effects.Select(e => e.command).Concat(converted.notes.SelectMany(n => new[] { n.sampleFx, n.volume.command, n.pan.command })).Where(c => c.HasPayload))
                                Unsupported(result, command, "OLDER_ENGINE_COMMAND", "Older-engine pattern commands remain inactive until their playback semantics are adapted.");
                        }
                        if (converted.HasPayload) destination.lines.Add(converted);
                    }
                    destination.lines.Sort((a,b) => a.line.CompareTo(b.line));
                    // Alias shares rows only; the destination retains its own automation.
                    ReadAutomation(result, sourceTracks[t], destination, p, t, length, identitySeed);
                    pattern.tracks.Add(destination);
                }
                song.patterns.Add(pattern);
            }
            var sequence = Children(root.Element("PatternSequence")?.Element("SequenceEntries"));
            Require(sequence.Count > 0 && sequence.Count <= 65536, "A song needs 1–65536 sequence slots.");
            for (int i = 0; i < sequence.Count; i++)
            {
                int patternIndex = Int(sequence[i], "Pattern", 0);
                Require(patternIndex >= 0 && patternIndex < patterns.Count, "Sequence pattern reference is out of range.");
                var slot = new SequenceSlot { id = Id(identitySeed + ":slot:" + i), patternId = song.patterns[patternIndex].id };
                foreach (var mute in Children(sequence[i].Element("MutedTracks")))
                {
                    int index = IntText(mute.Value, -1); Require(index >= 0 && index < tracks.Count, "Sequence mute reference is out of range.");
                    if (!slot.mutedTrackIds.Contains(song.tracks[index].id)) slot.mutedTrackIds.Add(song.tracks[index].id);
                }
                song.sequence.Add(slot);
            }
            // A dangling source selection still owns its numerical slot. Keep an explicit silent slot
            // so later target assignment and native preparation cannot collapse or retarget it.
            int highestSlot = song.patterns.SelectMany(p => p.tracks).SelectMany(t => t.lines).SelectMany(l => l.notes)
                .Where(n => n.instrumentPresent).Select(n => n.instrument).DefaultIfEmpty(-1).Max();
            while (song.instruments.Count <= highestSlot) song.instruments.Add(null);
            string validation = ZTrackerModelValidation.Validate(song);
            Require(validation == null, "Converted song cannot be represented safely: " + validation);
            foreach (var issue in result.Issues) song.diagnostics.Add(issue.Code + " (" + issue.Count + "): " + issue.Message);
            return result;
        }

        static XElement ResolveAlias(List<XElement> patterns, int p, int t)
        {
            var seen = new HashSet<int>();
            while (true)
            {
                Require(p >= 0 && p < patterns.Count && seen.Add(p), "Pattern alias is cyclic or out of range.");
                var tracks = Children(patterns[p].Element("Tracks")); Require(t < tracks.Count, "Alias track is missing.");
                int alias = Int(tracks[t], "AliasPatternIndex", -1);
                if (alias < 0) return tracks[t];
                p = alias;
            }
        }

        static PatternLine ReadLine(RenoiseSongConversion result, XElement source, int index, TrackData track, XElement sourceTrack, string location, bool oldOffset, bool oldPitch)
        {
            var row = new PatternLine { line = index }; location += ", row " + index;
            var notes = Children(source.Element("NoteColumns")); var states = Children(sourceTrack.Element("NoteColumnStates"));
            Require(notes.Count <= 12, "More than twelve note columns at " + location);
            for (int c = 0; c < notes.Count; c++)
            {
                var xml = notes[c]; string address = location + ", note " + c;
                var note = new NoteCell { column = c, inactive = c >= track.visibleNoteColumns || (c < states.Count && states[c].Value != "Active") };
                string token = Text(xml, "Note", "");
                if (token == "OFF") note.note = NoteKind.Off;
                else if (token != "" && token != "---") { note.note = NoteKind.Note; note.pitch = Pitch(token); }
                token = Text(xml, "Instrument", "");
                if (!Empty(token) && token != "FF") { note.instrumentPresent = true; note.instrument = Hex(token); if (note.instrument >= result.Song.instruments.Count) Issue(result, "INSTRUMENT_SLOT_UNRESOLVED", "An explicit instrument slot has no source instrument; the selection is retained and remains silent.", address); }
                note.volume = Column(result, Text(xml, "Volume", ""), c, address + " volume", oldPitch);
                note.pan = Column(result, Text(xml, "Panning", ""), c, address + " pan", oldPitch);
                token = Text(xml, "Delay", "");
                if (!Empty(token)) { note.delayPresent = true; note.delay = Hex(token); }
                note.sampleFx = Command(result, Text(xml, "EffectNumber", ""), Text(xml, "EffectValue", ""), c, CommandScope.Column, address + " local FX", oldOffset, oldPitch);
                if (note.HasPayload) row.notes.Add(note);
                if (note.HasPayload && note.inactive) Issue(result, "INACTIVE_NOTE_COLUMN", "Hidden or muted note-column data is retained inactive.", address);
            }
            var effects = Children(source.Element("EffectColumns")); Require(effects.Count <= 8, "More than eight effect columns at " + location);
            // MIDI panning tokens consume effect-column payload. Never reinterpret their message bytes as tracker commands.
            bool midi = notes.Any(n => { string p = Text(n, "Panning", ""); return p.Length == 2 && p[0] == 'M' && p[1] >= '0' && p[1] <= '3'; });
            for (int c = 0; c < effects.Count; c++)
            {
                var command = Command(result, Text(effects[c], "Number", ""), Text(effects[c], "Value", ""), c, CommandScope.Track, location + ", FX " + c, oldOffset, oldPitch);
                if (midi && command.HasPayload) Unsupported(result, command, "MIDI_MESSAGE", "MIDI effect payload is retained and cannot be played by ZTracker.");
                if (command.HasPayload) row.effects.Add(new EffectCell { column = c, command = command });
            }
            return row;
        }

        static ColumnValue Column(RenoiseSongConversion result, string token, int column, string address, bool oldPitch)
        {
            var value = new ColumnValue(); if (Empty(token)) return value;
            if (token.Length == 2 && int.TryParse(token, NumberStyles.HexNumber, Invariant, out int number) && number <= 128) { value.kind = ValueKind.Value; value.value = number; return value; }
            value.kind = ValueKind.Command;
            value.command = new CommandData { present = true, valuePresent = token.Length == 2, identifier = token, rawIdentifier = token, sourceAddress = address, sourceColumn = column, targetColumn = column, scope = CommandScope.Column, profile = Profile };
            if (token.Length == 2 && int.TryParse(token.Substring(1), NumberStyles.HexNumber, Invariant, out number) && "IOJKUDGCBQYR".IndexOf(token[0]) >= 0) { value.command.value = number; if (oldPitch && "UDG".IndexOf(token[0]) >= 0) Unsupported(result, value.command, "LEGACY_PITCH_MODE", "Old pitch compatibility commands require a separate adapter."); }
            else Unsupported(result, value.command, "COLUMN_COMMAND_UNMAPPED", "This column token is preserved without playback.");
            return value;
        }

        static CommandData Command(RenoiseSongConversion result, string id, string value, int column, CommandScope scope, string address, bool oldOffset, bool oldPitch)
        {
            var command = new CommandData { present = !Empty(id) && id != "00", valuePresent = !Empty(value), identifier = Empty(id) || id == "00" ? "" : id, rawIdentifier = id, rawValue = value, scope = scope, targetColumn = scope == CommandScope.Column ? column : -1, sourceColumn = column, sourceAddress = address, profile = Profile };
            if (id.Length == 2 && id[0] == '-') { id = "0" + id[1]; command.identifier = id; }
            // Renoise serializes the empty effect pair as 00/00 in some producers.
            if (!command.present && (Empty(value) || value == "00")) { command.valuePresent = false; return command; }
            if (command.valuePresent) { if (value.Length != 2 || !int.TryParse(value, NumberStyles.HexNumber, Invariant, out command.value)) { Unsupported(result, command, "COMMAND_VALUE", "Malformed command argument is preserved without playback."); return command; } }
            const string local = "0A 0U 0D 0G 0V 0I 0O 0T 0C 0S 0B 0E 0N 0Y";
            const string full = local + " 0M 0Q 0R 0L 0P 0X ZT ZL ZK ZG ZB ZD";
            bool allowed = (scope == CommandScope.Column ? local : full).Split(' ').Contains(command.identifier);
            if (command.identifier.StartsWith("Z", StringComparison.Ordinal)) command.scope = CommandScope.Global;
            if (!allowed || (id == "ZG" && command.value != 0) || (id == "ZK" && (command.value < 1 || command.value > 16)) || (id == "ZT" && command.value > 0 && command.value < 32)) Unsupported(result, command, "COMMAND_UNMAPPED_" + id, "Command is retained without a verified playback mapping.");
            else if ((oldOffset && id == "0S") || (oldPitch && (id == "0U" || id == "0D" || id == "0G" || id == "0V"))) Unsupported(result, command, "LEGACY_COMMAND_MODE", "Compatibility-mode command requires a separate adapter.");
            return command;
        }

        static void ReadDevices(RenoiseSongConversion result, XElement sourceTrack, int trackIndex, string seed)
        {
            var track = result.Song.tracks[trackIndex]; var devices = Children(sourceTrack.Element("FilterDevices")?.Element("Devices"));
            for (int d = 0; d < devices.Count; d++)
            {
                var xml = devices[d]; string type = xml.Name.LocalName, uid = Id(seed + ":track:" + trackIndex + ":device:" + d);
                var record = new RenoiseSongDevice { TrackIndex = trackIndex, DeviceIndex = d, TargetId = uid, Type = type, Name = Text(xml, "CustomDeviceName", type), Raw = xml.ToString(SaveOptions.DisableFormatting) };
                bool enabled = DeviceValue(xml, "IsActive", 1) != 0;
                if (type == "TrackMixerDevice" || type == "GroupTrackMixerDevice" || type == "MasterTrackMixerDevice" || type == "SendTrackMixerDevice")
                {
                    track.preVolume = (float)DeviceValue(xml, "Volume", 1); track.prePan = (float)(DeviceValue(xml, "Panning", .5) * 2 - 1);
                    track.postVolume = (float)DeviceValue(xml, "PostVolume", 1); track.postPan = (float)(DeviceValue(xml, "PostPanning", .5) * 2 - 1);
                    Require(track.preVolume >= 0 && track.preVolume <= 16 && track.postVolume >= 0 && track.postVolume <= 16 && Math.Abs(track.prePan) <= 1 && Math.Abs(track.postPan) <= 1, "Mixer values exceed supported limits.");
                    record.Mapped = true;
                    if (DeviceValue(xml, "Surround", 0) != 0) Issue(result, "MIXER_SURROUND_UNMAPPED", "Surround/width is not equivalent to the target mixer; source setting retained.", track.name);
                    if (Bool(xml, "AutoDcCorrection") || Bool(xml, "SoftClipping") || Bool(xml, "AutoGain")) Issue(result, "MASTER_PROCESSING_UNMAPPED", "Master DC correction, clipping or automatic gain requires a replacement.", track.name);
                }
                else if (type == "GainerDevice" && DeviceValue(xml, "Panning", .5) == .5 && !Bool(xml, "LPhaseInvert") && !Bool(xml, "RPhaseInvert") && DeviceValue(xml, "Volume", 1) >= 0 && DeviceValue(xml, "Volume", 1) <= 4)
                {
                    // Only the saved physical amplitude is transferred. The normalized automation curve is NOT inferred.
                    track.devices.nodes.Add(new AudioEffectNodeData { uid = uid, type = ZoundEffectType.Gain, enabled = enabled, p = new[] { (float)DeviceValue(xml, "Volume", 1) } }); record.Mapped = true;
                }
                else if (type == "SendDevice" && !Bool(xml, "MuteSource", true) && DeviceValue(xml, "SendPan", .5) == .5)
                {
                    int destination = (int)DeviceValue(xml, "DestSendTrack", -1); var sends = result.Song.tracks.Where(t => t.kind == TrackKind.Send).ToList(); double gain = DeviceValue(xml, "SendAmount", 1);
                    if (destination >= 0 && destination < sends.Count && gain >= 0 && gain <= 16 && enabled) { track.sends.Add(new SendDestination { trackId = sends[destination].id, gain = (float)gain, devicePosition = track.devices.nodes.Count, postfader = Bool(xml, "ApplyPostVolume", true) }); record.Mapped = true; }
                    else if (!enabled) record.Mapped = true;
                }
                if (!record.Mapped) Issue(result, "DEVICE_UNMAPPED_" + type, "Device needs a ZTracker replacement; its source settings and chain position are retained.", track.name + ", device " + d);
                // Metadata preserves FULL XML indices, including the mixer. Literal device commands are deliberately disabled until a calibrated parameter map exists.
                if (d > 0) track.sourceDevices.Add(new SourceDeviceData { id = uid, ordinal = d, kind = SourceDeviceKind.Unsupported, enabled = enabled, pluginId = type, rawSource = record.Raw });
                result.Devices.Add(record);
            }
        }

        static void ReadAutomation(RenoiseSongConversion result, XElement source, PatternTrack target, int pattern, int track, int length, string seed)
        {
            int laneIndex = 0;
            foreach (var xml in Children(source.Element("Automations")?.Element("Envelopes")))
            {
                int device = Int(xml, "DeviceIndex", -1), parameter = Int(xml, "ParameterIndex", -1); var envelope = xml.Element("Envelope");
                var lane = new AutomationLane { id = Id(seed + ":automation:" + pattern + ":" + track + ":" + laneIndex++), rawSource = xml.ToString(SaveOptions.DisableFormatting), sourceMode = Text(envelope, "PlayMode", "Curves"), sourceConvention = "Renoise XML sublines / 256", target = new ParameterTarget { kind = ParameterKind.Device, trackId = target.trackId, deviceId = Id(seed + ":track:" + track + ":device:" + device), index = parameter, unresolved = true }, enabled = false, unsupported = true, diagnostic = "AUTOMATION_UNCALIBRATED" };
                lane.interpolation = lane.sourceMode == "Points" ? AutomationInterpolation.Step : AutomationInterpolation.Linear;
                bool safe = lane.sourceMode == "Points" || lane.sourceMode == "Lines" || lane.sourceMode == "Linear"; double previous = -1;
                if (Int(envelope, "Length", length * 256) != length * 256) safe = false;
                foreach (var point in Children(envelope?.Element("Points")))
                {
                    var parts = point.Value.Split(',');
                    if (parts.Length < 2 || parts.Length > 3 || !TryNumber(parts[0], out double time) || !TryNumber(parts[1], out double value) || time < 0 || time / 256 >= length || time <= previous || value < 0 || value > 1) { safe = false; continue; }
                    if (parts.Length == 3 && (!TryNumber(parts[2], out double curvature) || curvature != 0)) safe = false;
                    previous = time; lane.points.Add(new AutomationPoint { line = time / 256, value = (float)value });
                }
                if (Number(envelope, "ValueQuantum", 0) != 0 || Text(envelope, "Polarity", "Unipolar") != "Unipolar") safe = false;
                // Renoise mixer order is Panning, Volume, Width, Post Panning, Post Volume (NOT volume first).
                // Installed schema order agrees with the live API dump at forum.renoise.com/t/renoise-device-parameter-name-list/29485.
                // XML parameter indices are zero based. Pan is linear normalized 0..1 in both engines.
                var sourceDevice = result.Devices.FirstOrDefault(d => d.TrackIndex == track && d.DeviceIndex == device);
                if (safe && device == 0 && (parameter == 0 || parameter == 3) && sourceDevice != null && sourceDevice.Mapped && sourceDevice.Type.EndsWith("MixerDevice", StringComparison.Ordinal))
                {
                    lane.target = new ParameterTarget { kind = ParameterKind.Mixer, trackId = target.trackId, parameter = parameter == 0 ? "prePan" : "postPan" }; lane.enabled = true; lane.unsupported = false; lane.diagnostic = "";
                }
                else Issue(result, safe ? "AUTOMATION_UNCALIBRATED" : "AUTOMATION_CURVE_UNSUPPORTED", "Automation retained inactive because its source address, value curve or interpolation lacks a safe mapping.", "Pattern " + pattern + ", track " + track + ", device " + device + ", parameter " + parameter);
                target.automation.Add(lane);
            }
        }

        static int Pitch(string token)
        {
            if (token.Length != 3 || token[2] < '0' || token[2] > '9') throw new FormatException("Invalid note " + token);
            int semitone = Array.IndexOf(new[] { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" }, token.Substring(0,2));
            Require(semitone >= 0, "Invalid note " + token); return (token[2] - '0') * 12 + semitone;
        }
        static int Hex(string value) { Require(value.Length == 2 && int.TryParse(value, NumberStyles.HexNumber, Invariant, out _), "Invalid hexadecimal token " + value); return int.Parse(value, NumberStyles.HexNumber, Invariant); }
        static bool Empty(string value) => value == "" || value == ".." || value == "--";
        static List<XElement> Children(XElement element) => element?.Elements().ToList() ?? new List<XElement>();
        static string Text(XElement element, string name, string fallback) => element?.Element(name)?.Value ?? fallback;
        static int Int(XElement element, string name, int fallback) => IntText(element?.Element(name)?.Value, fallback);
        static int IntText(string text, int fallback) { if (text == null) return fallback; if (!int.TryParse(text, NumberStyles.Integer, Invariant, out int value)) throw new FormatException("Invalid integer " + text); return value; }
        static bool Bool(XElement element, string name, bool fallback = false) { string value = element?.Element(name)?.Value; if (value == null) return fallback; if (value == "true" || value == "1") return true; if (value == "false" || value == "0") return false; throw new FormatException("Invalid boolean " + value); }
        static bool TryNumber(string text, out double number) => double.TryParse(text, NumberStyles.Float, Invariant, out number) && !double.IsNaN(number) && !double.IsInfinity(number);
        static double Number(XElement element, string name, double fallback) { string text = element?.Element(name)?.Value; if (text == null) return fallback; if (!TryNumber(text, out double number)) throw new FormatException("Invalid number " + text); return number; }
        static double DeviceValue(XElement element, string name, double fallback) => Number(element?.Element(name), "Value", fallback);
        static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
        static string Id(string input) { using (var sha = SHA256.Create()) return "xr-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLowerInvariant().Substring(0,24); }
        static void Unsupported(RenoiseSongConversion result, CommandData command, string code, string message) { command.unsupported = true; command.diagnostic = code; Issue(result, code, message, command.sourceAddress); }
        static void Issue(RenoiseSongConversion result, string code, string message, string location)
        {
            var issue = result.Issues.Find(i => i.Code == code); if (issue != null) { issue.Count++; return; }
            result.Issues.Add(new RenoiseIssue { Code = code, Message = message, Location = location, Count = 1 });
        }
    }
}
