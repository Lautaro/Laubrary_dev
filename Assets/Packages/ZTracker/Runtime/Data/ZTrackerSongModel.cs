using System;
using System.Collections.Generic;
using Laubrary.Audio;
using UnityEngine;

namespace Laubrary.ZTracker.Model
{
    public enum TrackKind { Sequencer, Group, Send, Master, Event }
    public enum NoteKind { Empty, Note, Off, Legacy }
    public enum ValueKind { Empty, Value, Command, Legacy }
    public enum CommandScope { Column, Track, Global, Unresolved }
    public enum AutomationInterpolation { Step, Linear }
    // Dynamic: a song's own command definition, addressed by its stable uid in ParameterTarget.parameter.
    public enum ParameterKind { Mixer, Device, InstrumentMacro, Sample, Modulation, Synth, Timing, Dynamic }
    // InstrumentEffect: a setting of an effect inside an instrument's own chain. Every track keeps its own copy of that
    // chain, so the command changes the copy of the track it is typed on. Appended last so stored scopes keep their values.
    public enum DynamicScope { Instrument, Track, Effect, Song, InstrumentEffect }
    public enum SourceDeviceKind { InstrumentAutomation, InstrumentMacros, AudioChain, Unsupported }

    [Serializable] public sealed class ParameterTarget
    {
        public ParameterKind kind;
        public string trackId = "", deviceId = "", parameter = "", instrumentId = "";
        public int index = -1;
        public string units = "normalized";
        public bool unresolved;
    }

    [Serializable] public sealed class CommandData
    {
        public bool present, valuePresent;
        public string identifier = "";
        public int value;
        public CommandScope scope = CommandScope.Unresolved;
        public int targetColumn = -1;
        public string profile = "renoise-p5-v1";
        public bool unsupported;
        public string diagnostic = "";
        public string rawIdentifier = "", rawValue = "", sourcePayload = "", migrationDecision = "preserve-and-flag";
        public bool hasLegacy;
        public int legacyCommand, legacyParameter, sourceColumn;
        public string sourceAddress = "";
        public bool HasPayload => present || valuePresent || hasLegacy;
    }

    [Serializable] public sealed class ColumnValue
    {
        public ValueKind kind;
        public int value, legacyValue;
        public bool hasLegacy;
        public CommandData command = new CommandData();
        public bool HasPayload => kind != ValueKind.Empty || hasLegacy || command.HasPayload;
    }

    [Serializable] public sealed class NoteCell
    {
        public int column;
        public bool inactive;
        public NoteKind note;
        public int pitch, legacyNote;
        public bool instrumentPresent;
        public int instrument;
        public ColumnValue volume = new ColumnValue(), pan = new ColumnValue();
        public bool delayPresent;
        public int delay;
        public CommandData sampleFx = new CommandData();
        // Extensions keep selectors/overrides without manufacturing instrument slots.
        public string parameterSetId = "", legacyExtras = "";
        public bool HasPayload => note != NoteKind.Empty || instrumentPresent || volume.HasPayload || pan.HasPayload || delayPresent || sampleFx.HasPayload || parameterSetId != "" || legacyExtras != "";
    }

    [Serializable] public sealed class EffectCell
    {
        public int column;
        public bool inactive;
        public CommandData command = new CommandData();
    }
    [Serializable] public sealed class EventCell
    {
        public int column;
        public string payload = "", profile = "ztracker-event-v1", provenance = "";
        public bool present;
    }
    [Serializable] public sealed class PatternLine
    {
        public int line;
        public List<NoteCell> notes = new List<NoteCell>();
        public List<EffectCell> effects = new List<EffectCell>();
        public List<EventCell> events = new List<EventCell>();
        public bool HasPayload => notes.Exists(n => n.HasPayload) || effects.Exists(e => e.command.HasPayload) || events.Exists(e => e.present);
    }
    /// <summary>How the line leaves a point: as the lane says, straight to the next point, or held until the next point.</summary>
    public enum AutomationShape { Lane, Linear, Step }
    [Serializable] public sealed class AutomationPoint { public double line; public float value; public AutomationShape segment; }
    [Serializable] public sealed class AutomationLane
    {
        public string id = "";
        public ParameterTarget target = new ParameterTarget();
        public AutomationInterpolation interpolation;
        public List<AutomationPoint> points = new List<AutomationPoint>();
        public bool enabled = true;
        public bool unsupported;
        public string diagnostic = "", sourceMode = "", rawSource = "", sourceConvention = "zero-based";
        public float scaling;
        public double timeQuantum = 1d/256;
    }
    [Serializable] public sealed class PatternTrack
    {
        public string trackId = "";
        public List<PatternLine> lines = new List<PatternLine>();
        public List<AutomationLane> automation = new List<AutomationLane>();
        // A read never inserts a row or exposes a writable empty singleton.
        public PatternLine ReadLine(int line) => lines.Find(l => l.line == line) ?? new PatternLine { line = line };
        public void WriteLine(PatternLine value)
        {
            if (value == null || value.line < 0 || value.line >= 512) throw new ArgumentOutOfRangeException(nameof(value));
            lines.RemoveAll(l => l.line == value.line);
            if (value.HasPayload) lines.Add(value);
            lines.Sort((a,b) => a.line.CompareTo(b.line));
        }
    }
    [Serializable] public sealed class PatternData
    {
        public string id = "", name = "";
        public int lineCount = 64;
        public List<PatternTrack> tracks = new List<PatternTrack>();
    }
    [Serializable] public sealed class ColumnVisibility { public bool volume = true, pan, delay, sampleFx; }
    /// <summary>
    /// How one track FX column is shown while editing. View state only: the engine never reads it, and the
    /// commands stay in the column they were typed in. <c>command</c> binds the column to one command: a
    /// built-in or imported-device id (two characters) or a dynamic parameter's stable uid, so a rename of the
    /// id keeps the binding. <c>bar</c> draws a bound column's values as bars instead of characters.
    /// </summary>
    [Serializable] public sealed class EffectColumnView
    {
        public int column;
        public string command = "";
        public bool bar;
    }
    /// <summary>
    /// An envelope column of a track: a line drawn down the pattern that drives one command. <c>command</c> is a song
    /// command's stable uid or a built-in id (0L, 0P); empty while unbound. Its points live in each pattern as the
    /// automation lane <c>ZTrackerEnvelopes.LaneId(uid)</c> of this track, so the engine plays them like any lane.
    /// </summary>
    [Serializable] public sealed class EnvelopeColumnData
    {
        public string uid = "";
        public string command = "";
    }
    [Serializable] public sealed class SendDestination
    {
        public string trackId = "";
        public float gain = 1;
        // 0 is before first node; nodes.Count is after last. Postfader is explicit.
        public int devicePosition;
        public bool postfader;
    }
    [Serializable] public sealed class TrackData
    {
        public string id = "", name = "", parentGroupId = "", outputTrackId = "";
        public TrackKind kind;
        public bool drumTrack;
        public Color color = Color.gray;
        public int visibleNoteColumns = 1, visibleEffectColumns = 1;
        public List<ColumnVisibility> columns = new List<ColumnVisibility>();
        public List<EffectColumnView> effectColumnViews = new List<EffectColumnView>();
        public List<EnvelopeColumnData> envelopeColumns = new List<EnvelopeColumnData>();
        public float preVolume = 1, prePan, preWidth = 1, postVolume = 1, postPan;
        public bool triggerMute, outputMute, solo;
        public AudioEffectChainData devices = new AudioEffectChainData();
        public List<SendDestination> sends = new List<SendDestination>();
        public bool beatTicks;
        public int beatIntervalLines = 4;
        public List<ExternalSourceDevice> externalSources = new List<ExternalSourceDevice>();
        public List<SourceDeviceData> sourceDevices = new List<SourceDeviceData>();
    }
    // Ordinal selects a slot; its explicit external ID then resolves on the linked instrument.
    [Serializable] public sealed class ExternalSourceDevice
    {
        public string id = "", instrumentId = "", pluginId = "";
        public int sourceOrdinal;
        public List<string> parameterNumbers = new List<string>();
    }
    [Serializable] public sealed class SourceDeviceData
    {
        public string id = "", instrumentId = "", pluginId = "", rawSource = "";
        public int ordinal;
        public SourceDeviceKind kind;
        public bool enabled = true;
        public List<SourceParameterData> parameters = new List<SourceParameterData>();
    }
    [Serializable] public sealed class SourceParameterData
    {
        public int ordinal;
        public string externalId = "", parameter = "", units = "normalized", scaling = "Linear";
        public ParameterTarget target = new ParameterTarget();
        public float defaultValue, min, max = 1, quantum, lower;
        public bool explicitEquivalence;
        public List<ModulationPoint> curvePoints = new List<ModulationPoint>();
    }
    /// <summary>
    /// A setting given a two-character command id and a friendly name, so a track FX row can set it. The uid never
    /// changes and is what columns and automation lanes refer to; the id is what a pattern cell types and may be
    /// renamed. Target addressing per scope (see Documentation~/COMMAND_MODEL_DESIGN.md): Instrument = instrumentId plus
    /// a typed parameter name (kind Sample/Synth) or a macro (kind InstrumentMacro, index); Track = trackId plus a mixer
    /// value name; Effect = trackId, deviceId (the effect node uid in that track's chain) and the setting index;
    /// Song = a song value name ("bpm"). min/max are in the target's units and say what 00 and FF mean.
    /// </summary>
    [Serializable] public sealed class DynamicParameterData
    {
        public string uid = "", id = "", name = "";
        public DynamicScope scope;
        public ParameterTarget target = new ParameterTarget();
        public float min, max = 1;
        // Alpha zero means "use the automatic colour" (ZTrackerCommandRegistry.AutoColor). View-only.
        public Color color = Color.clear;
    }
    [Serializable] public sealed class SequenceSlot
    {
        public string id = "", patternId = "";
        public List<string> mutedTrackIds = new List<string>();
    }
    [Serializable] public sealed class SongData
    {
        public string id = "", name = "";
        public double bpm = 120;
        public int linesPerBeat = 4, ticksPerLine = 6, voiceCapacity = 128;
        public bool beatTicks = true;
        public int beatIntervalLines = 4;
        public List<ZTrackerInstrument> instruments = new List<ZTrackerInstrument>();
        public List<TrackData> tracks = new List<TrackData>();
        public List<PatternData> patterns = new List<PatternData>();
        public List<SequenceSlot> sequence = new List<SequenceSlot>();
        public string provenance = "";
        public ulong seed;
        public List<string> diagnostics = new List<string>();
        // No initializer on purpose: a song saved before the command model reads as version 0.
        public int commandModelVersion;
        // 0: every note column has its own instrument cell; 1: one instrument column per track, stored on note column 0 (T-0648).
        public int instrumentColumnVersion;
        public List<DynamicParameterData> dynamicParameters = new List<DynamicParameterData>();
    }

    public static class ZTrackerPatternOperations
    {
        // Return the complete pre-edit pattern as an explicit archive/Undo payload.
        // Removed events/automation are never silently mistaken for empty notes.
        public static PatternData Resize(PatternData pattern, int lines)
        {
            if (lines < 1 || lines > 512) throw new ArgumentOutOfRangeException(nameof(lines));
            var archive = ZTrackerMigration.Copy(pattern);
            pattern.lineCount = lines;
            foreach (var track in pattern.tracks)
            {
                track.lines.RemoveAll(l => l.line >= lines);
                if(track.automation!=null)foreach (var lane in track.automation) lane?.points?.RemoveAll(p => p!=null&&p.line > lines);
            }
            return archive;
        }
        public static PatternData InsertLine(PatternData pattern, int line)
        {
            if (line < 0 || line >= pattern.lineCount) throw new ArgumentOutOfRangeException(nameof(line));
            var archive = ZTrackerMigration.Copy(pattern);
            foreach (var track in pattern.tracks)
            {
                foreach (var row in track.lines) if (row.line >= line) row.line++;
                track.lines.RemoveAll(l => l.line >= pattern.lineCount);
                if(track.automation!=null)foreach (var lane in track.automation) { if(lane?.points==null)continue;foreach (var point in lane.points) if (point!=null&&point.line >= line) point.line++; lane.points.RemoveAll(p => p!=null&&p.line > pattern.lineCount); }
            }
            return archive;
        }
        public static PatternData DeleteLine(PatternData pattern, int line)
        {
            if (line < 0 || line >= pattern.lineCount) throw new ArgumentOutOfRangeException(nameof(line));
            var archive = ZTrackerMigration.Copy(pattern);
            foreach (var track in pattern.tracks)
            {
                track.lines.RemoveAll(l => l.line == line);
                foreach (var row in track.lines) if (row.line > line) row.line--;
                if(track.automation!=null)foreach (var lane in track.automation) { if(lane?.points==null)continue;lane.points.RemoveAll(p => p!=null&&p.line >= line && p.line < line+1); foreach (var point in lane.points) if (point!=null&&point.line >= line+1) point.line--; }
            }
            return archive;
        }
    }
}
