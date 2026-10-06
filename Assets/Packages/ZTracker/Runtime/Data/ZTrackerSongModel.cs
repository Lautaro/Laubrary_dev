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
    public enum ParameterKind { Mixer, Device, InstrumentMacro, Sample, Modulation, Synth, Timing }
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
    [Serializable] public sealed class AutomationPoint { public double line; public float value; }
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
        public Color color = Color.gray;
        public int visibleNoteColumns = 1, visibleEffectColumns = 1;
        public List<ColumnVisibility> columns = new List<ColumnVisibility>();
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
