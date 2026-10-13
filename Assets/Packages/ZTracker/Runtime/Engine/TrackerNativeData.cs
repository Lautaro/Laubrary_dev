using System;
using System.Threading;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public enum TrackerEventKind { Row, Tick, NoteOn, NoteOff, Beat, Authored, VoiceStolen, PreparedSwap, Stopped, Diagnostic, Started, SongLooped, TempoChanged, RewindStarted, RewindCompleted, RewindRefused, RowReverseStarted, RowReverseCompleted, RowReverseRefused }
    public enum TrackerRuntimeDiagnostic { SeekUnreachable=1, PitchLimit=2, InvalidPitch=3, BreakClamped=4, EmptyMemory=5, DelayOutsideRow=6, CutOutsideRow=7, GlideNoTarget=8, ExclusiveNoWeight=9, SharedFxStop=10, InvalidClock=11, ClockLimit=12, SliceOutside=13, GlideWithoutVoice=14, GlideInstrumentChanged=15, InstrumentUnresolved=16, SeekVoiceStateDiscarded=17, SampleDomainUnsupported=18, RouteCycle=19, StopExpandsSendContributors=20 }
    public struct TrackerEvent
    {
        public TrackerEventKind kind;
        public long samplePosition;
        public double bpm;
        public TrackerPlaybackDirection direction;
        public int sequence, pattern, row, tick, track, column, note, instrument, payload;
    }
    /// <summary>
    /// One row as the renderer entered it, published for the song clock. Positions are on the transport sample
    /// timeline (the renderer's own frame counter, which stops while paused and never jumps on seek or swap).
    /// A held row (ZDxx) publishes one entry per repeat.
    /// </summary>
    public struct TrackerClockRow
    {
        public long serial;
        public double start, duration, bpm;
        public int sequence, pattern, row, linesPerBeat, ticksPerLine, holdRemaining;
        public long occurrence;
        public byte held;
        public TrackerPlaybackDirection direction;
    }
    /// <summary>Where the renderer has got to: the transport position at the end of the last rendered block.</summary>
    public struct TrackerClockHead
    {
        public long position, blocks;
        public int sampleRate;
        public byte playing, paused;
        public TrackerRewindStatus rewind;
        public TrackerRowReverseStatus rowReverse;
        public double bpm;
        public float requestedTempo;
        public int sequence, row, pendingParameters, trackCount;
        public bool tempoPending;
    }
    /// <summary>Audio-published readout, never a reference into mutable renderer state.</summary>
    public struct TrackerTrackReadout
    {
        public long block;
        public int activeVoices, transpose;
        public float detune;
        public bool gated, muted;
    }
    /// <summary>One audio producer and one main-thread consumer. Storage outlives graph instances and prepared swaps.</summary>
    public unsafe struct TrackerEventRing : IDisposable
    {
        public const int ClockRowCapacity = 64;
        [NativeDisableUnsafePtrRestriction] public TrackerEvent* events;
        [NativeDisableUnsafePtrRestriction] public long* counters; // published write, consumed read, overflow, clock rows written, clock head version
        // Song clock: never consumed, only overwritten, so any number of main-thread readers can look without
        // competing with the event consumer. Each record is guarded by a version so a torn read is detected.
        [NativeDisableUnsafePtrRestriction] public TrackerClockRow* clockRows;
        [NativeDisableUnsafePtrRestriction] public TrackerClockHead* clockHead;
        [NativeDisableUnsafePtrRestriction] public TrackerTrackReadout* trackReadouts;
        public int readoutCapacity;
        public int capacity;
        public static TrackerEventRing Create(int capacity, int tracks = 0)
        {
            if (capacity < 2 || capacity > 1048576) throw new ArgumentOutOfRangeException(nameof(capacity));
            var r = new TrackerEventRing { capacity = capacity };
            r.events = (TrackerEvent*)UnsafeUtility.Malloc(capacity * (long)sizeof(TrackerEvent), 16, Allocator.Persistent);
            r.counters = (long*)UnsafeUtility.Malloc(5 * sizeof(long), 16, Allocator.Persistent);
            UnsafeUtility.MemClear(r.counters, 5 * sizeof(long));
            r.clockRows = (TrackerClockRow*)UnsafeUtility.Malloc(ClockRowCapacity * (long)sizeof(TrackerClockRow), 16, Allocator.Persistent);
            UnsafeUtility.MemClear(r.clockRows, ClockRowCapacity * (long)sizeof(TrackerClockRow));
            for (int i = 0; i < ClockRowCapacity; i++) r.clockRows[i].serial = -1;
            r.clockHead = (TrackerClockHead*)UnsafeUtility.Malloc(sizeof(TrackerClockHead), 16, Allocator.Persistent);
            UnsafeUtility.MemClear(r.clockHead, sizeof(TrackerClockHead));
            r.readoutCapacity = tracks;
            if (tracks > 0) {
                r.trackReadouts = (TrackerTrackReadout*)UnsafeUtility.Malloc(tracks * (long)sizeof(TrackerTrackReadout), 16, Allocator.Persistent);
                UnsafeUtility.MemClear(r.trackReadouts, tracks * (long)sizeof(TrackerTrackReadout));
            }
            return r;
        }
        // ---- Song clock, written on the audio thread ----
        public void PublishRow(TrackerClockRow value)
        {
            if (clockRows == null || counters == null) return;
            long serial = counters[3];
            ref var slot = ref clockRows[serial % ClockRowCapacity];
            Volatile.Write(ref slot.serial, -1);
            value.serial = -1; slot = value;
            Volatile.Write(ref slot.serial, serial);
            Volatile.Write(ref counters[3], serial + 1);
        }
        public void PublishHead(long position, int sampleRate, bool playing, bool paused, TrackerRewindStatus rewind = default, TrackerRowReverseStatus rowReverse = default)
        {
            if (clockHead == null) return;
            long version = counters[4];
            Volatile.Write(ref counters[4], version + 1); // odd: write in progress
            clockHead->position = position; clockHead->sampleRate = sampleRate; clockHead->blocks++;
            clockHead->playing = playing ? (byte)1 : (byte)0; clockHead->paused = paused ? (byte)1 : (byte)0;
            clockHead->rewind = rewind;
            clockHead->rowReverse = rowReverse;
            Volatile.Write(ref counters[4], version + 2);
        }
        public void PublishReadout(in TrackerState state, TrackerRewindStatus rewind, TrackerRowReverseStatus reverse)
        {
            if (clockHead == null) return;
            long version = counters[4];
            Volatile.Write(ref counters[4], version + 1);
            clockHead->position = state.samplePosition; clockHead->sampleRate = state.sampleRate; clockHead->blocks++;
            clockHead->playing = state.playing && (state.paused || state.rewind.phase == TrackerRewindPhase.Idle) ? (byte)1 : (byte)0; clockHead->paused = state.paused ? (byte)1 : (byte)0;
            clockHead->rewind = rewind; clockHead->rowReverse = reverse;
            clockHead->bpm = state.bpm; clockHead->requestedTempo = state.runtimeTempo;
            clockHead->sequence = state.sequenceIndex; clockHead->row = state.row;
            clockHead->tempoPending = state.runtimeTempo > 0 ? !state.runtimeTempoApplied || state.bpm != state.runtimeTempo : state.runtimeTempoApplied;
            int pending = 0;
            for (int i = 0; i < state.deviceParameters.Length; i++) if (state.deviceParameters[i].runtimePending) pending++;
            clockHead->pendingParameters = pending;
            int count = math.min(state.trackCount, readoutCapacity);
            for (int t = 0; t < count; t++) {
                var track = state.tracks[t];
                trackReadouts[t] = new TrackerTrackReadout { block = clockHead->blocks, transpose = track.runtimeTranspose, detune = track.runtimeDetune, gated = track.triggerMute, muted = track.outputMute || !track.soloEnabled || state.sequenceMutes[state.sequence[state.sequenceIndex].muteOffset + t] != 0 };
            }
            for (int i = 0; i < state.voices.Length; i++) { var voice = state.voices[i]; if (voice.active && voice.track >= 0 && voice.track < count) trackReadouts[voice.track].activeVoices++; }
            // Count is stored in the shared head: copies of the ring share its publication.
            clockHead->trackCount = count;
            Volatile.Write(ref counters[4], version + 2);
        }
        public bool TryReadTrack(int track, out TrackerTrackReadout value)
        {
            value = default;
            if (clockHead == null || trackReadouts == null || track < 0 || track >= readoutCapacity) return false;
            for (int attempt = 0; attempt < 8; attempt++) {
                long version = Volatile.Read(ref counters[4]);
                if (version == 0 || (version & 1) != 0) continue;
                if (track >= clockHead->trackCount) return false;
                value = trackReadouts[track];
                if (Volatile.Read(ref counters[4]) == version) return true;
            }
            return false;
        }
        // ---- Song clock, read on the main thread (any number of readers, nothing consumed) ----
        public long ClockRowsWritten => counters == null ? 0 : Volatile.Read(ref counters[3]);
        public bool TryReadClockRow(long serial, out TrackerClockRow value)
        {
            value = default;
            if (clockRows == null || serial < 0) return false;
            ref var slot = ref clockRows[serial % ClockRowCapacity];
            if (Volatile.Read(ref slot.serial) != serial) return false;
            value = slot;
            return Volatile.Read(ref slot.serial) == serial; // overwritten while copying: reject
        }
        public bool TryReadClockHead(out TrackerClockHead value)
        {
            value = default;
            if (clockHead == null) return false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                long before = Volatile.Read(ref counters[4]);
                if ((before & 1) != 0) continue;
                value = *clockHead;
                if (Volatile.Read(ref counters[4]) == before) return before != 0;
            }
            return false;
        }
        public void Write(TrackerEvent value)
        {
            long w = counters[0], read = Volatile.Read(ref counters[1]);
            if (w - read >= capacity) { counters[2]++; return; }
            events[w % capacity] = value;
            Volatile.Write(ref counters[0], w + 1);
        }
        public bool TryRead(out TrackerEvent value)
        {
            long r = counters[1];
            if (r >= Volatile.Read(ref counters[0])) { value = default; return false; }
            value = events[r % capacity];
            Volatile.Write(ref counters[1], r + 1);
            return true;
        }
        public long OverflowCount => counters == null ? 0 : Volatile.Read(ref counters[2]);
        public void Dispose()
        {
            if (events != null) UnsafeUtility.Free(events, Allocator.Persistent);
            if (counters != null) UnsafeUtility.Free(counters, Allocator.Persistent);
            if (clockRows != null) UnsafeUtility.Free(clockRows, Allocator.Persistent);
            if (clockHead != null) UnsafeUtility.Free(clockHead, Allocator.Persistent);
            if (trackReadouts != null) UnsafeUtility.Free(trackReadouts, Allocator.Persistent);
            this = default;
        }
    }
    public struct TrackerPcm { public int offset, frames, channels, frequency; public float peak; }
    public struct TrackerSample
    {
        public int pcm, instrument, loopStart, loopEnd, modStart, modCount, filterType, fxChain, muteGroup;
        public int slices, sliceCount, regionStart, regionEnd;
        public SampleLoop loop;
        public SampleInterpolation interpolation;
        public NewNoteAction nna;
        public bool releaseExitsLoop, oneShot, legacyPan, legacySamplePitch, orderedVolume;
        public float legacyFineTuneCents;
        public int legacyTranspose;
        public float volume, pan, tune, cutoff, resonance, attack, hold, decay, sustain, release, localVolume, localPan;
        public int legacyFilter;
        public int delayDestination, reverbDestination;
        public float delaySend, reverbSend;
        public float drumGateSeconds;
    }
    public struct TrackerZone { public int sample, minNote, maxNote, minVelocity, maxVelocity, baseNote; public bool tracking; }
    public struct TrackerInstrument { public int zoneStart, zoneCount, chainStart, chainCount; public NewNoteAction nna; }
    public struct TrackerParameterSet { public int instrument, zones, zoneCount; }
    public struct TrackerModPoint { public double time; public float value, exponent; }
    public struct TrackerMod
    {
        public bool parameterEnvelope;
        public TrackerParameter parameter;
        public ModulationTarget target;
        public ModulationDeviceKind kind;
        public ModulationOperation operation;
        public float attack, hold, decay, sustain, release, rate, depth, phase, min, max, curve;
        public int points, pointCount, shape;
        public double sustainPosition, loopStart, loopEnd;
        public bool sustainEnabled, loopEnabled;
        // Times (points, loop, sustain) are song rows, so the envelope follows the song's tempo (T-0629).
        public bool rows;
        public SampleLoop loop;
        public float fadeSeconds, randomness;
        public bool advanceFirst, primaryEnvelope;
    }
    public struct TrackerModState { public double position; public float releaseStart, output; public bool released, finished, seeked; public int direction; }
    public struct TrackerVoice
    {
        public bool active, released, pitchLimited;
        public int sample, track, column, note, instrument, bus, velocity, stage, direction, modulationOffset;
        public long cohort, age, releaseAge;
        public double position, step;
        public double envelopePosition;
        public float volume, pan, envelope, releaseStart, releaseStep, amplitude;
        public float fL1, fL2, fR1, fR2, xL1, xL2, xR1, xR2;
        public float b0, b1, b2, a1, a2, filterCutoff, filterQ;
        public float phaseA, phaseB, syncFilter, blendCurrent, pmCurrent, ratioCurrent, pulseCurrent, detuneCurrent;
        // Seconds since the note started, for the synth's own per-note envelopes. A double: a float clock drifted by up to
        // 0.7% once a note had been held a few seconds, which put envelope loops out of time (T-0629).
        public double envTime;
        // The same clock in song rows at the tempo of the moment, and where it stood at note-off (envelopes in rows).
        public double envRows, releaseRows;
        public float memberSpread, memberGain, blendLevel, blendReleaseStart, arpTime;
        public int blendStage, arpIndex, directionB;
        public double positionB, stepB, glideCurrent, arpCounter, arpStep;
        public uint noiseA, noiseB;
        public float3 pinkA, pinkB;
        public float4 fmPhase, fmLevel, fmReleaseStart;
        public int4 fmStage;
        public float fmPrevious;
        // Modulator self-feedback (A, B, D): the gliding amount and the last two raw outputs.
        public float3 fmFeedbackAmount, fmFeedbackLast, fmFeedbackBefore;
        public float vibratoPhase, vibratoFade;
        public double vibratoSmooth, arpSmooth;
        public float amplitudeDepth, amplitudePhase, amplitudeRate;
        public float commandPitch, commandGain, commandArp, commandVibrato, commandTremolo, commandPan, phaseV, phaseT, phaseN, retriggerGain;
        public int regionStart, regionEnd, selectedStart;
        public bool explicitStart;
        // Instrument glide (T-0627): semitones still to travel (signed) and the size of the whole slide, which is
        // covered at a constant rate in exactly the glide time. glideCurrent is 2^(glideSemis/12).
        public double glideSemis, glideSpan;
        // Legato velocity slide (T-0628): level and velocity move from From to Target over Span samples.
        public float volumeFrom, volumeTarget;
        public int volumeRemaining, volumeSpan, velocityFrom, velocityTarget;
    }
    /// <summary>
    /// Where one voice is on each of its per-note parameter envelopes, published by the renderer once per rendered slice so
    /// an editor can draw one playhead per sounding note (T-0629). Fixed size, written in place: no allocation and no
    /// managed work on the audio side. A position is in the envelope's own unit (seconds or song rows); NaN where the
    /// parameter has no envelope on this voice.
    /// </summary>
    public unsafe struct TrackerEnvelopeHead
    {
        public long cohort;
        public int instrument, parameterSet;
        public byte active, released;
        public fixed float position[TrackerParameters.Stride];
    }
    public struct TrackerColumn
    {
        public int instrument;
        public float volume, pan;
        public long cohort, due;
        public double dueExact;
        public int pendingNote, pendingInstrument, pendingVelocity, pendingCell;
        public bool pendingOff, localsPending;
        public int previousNote;
        public bool hasPreviousNote;
        public int glideTarget;
        public long cutCohort, volumeCutCohort;
        public int cutTick, volumeCutTick, volumeCutValue;
        public bool launchSuppressed;
        public double activeFrom;
    }
    public struct TrackerCell
    {
        public int track, column, note, instrument, volume, pan, delay;
        public bool instrumentPresent;
        public int ops, opCount;
        public int parameterSet;
    }
    public struct TrackerRow { public int cells, cellCount, commands, commandCount, events, eventCount, ops, opCount; }
    public struct TrackerOp { public int track, column, source, priority, kind, value, bank, target; public bool shorthand; }
    public struct TrackerDevice { public int track, ordinal, parameters, count, kind; public bool enabled, authoredEnabled; }
    public struct TrackerDeviceParameter
    {
        public int device, ordinal, kind, track, instrument, parameter, chain, node, route, points, pointCount;
        public float latent, emitted, authored, min, max, quantum, lower;
        public bool authoredPresent, written;
        // A song command definition (dynamic parameter). Values below are in the target's own units.
        // destinationAuthored is the target's authored value, restored exactly on Play/Stop/Seek.
        public bool dynamic, smooth, ramped, stepping, laneRamp;
        public float destinationAuthored, destination, target, step, laneNext;
        public int remaining;
        public double laneFrames;
        // curve: how a normalized value maps to the target (0 linear min…max; 1 the 0L gain curve; 2 the 0P pan curve).
        // An envelope lane (laneEnvelope) glides onto its line instead of jumping when something else wrote last
        // (laneOwned false); typedRow is the row serial on which a typed command took the value for its row.
        public byte curve;
        public bool laneOwned, laneEnvelope;
        // Main-thread dynamic tempo writes wait for row entry; other targets ramp immediately.
        public bool runtimePending;
        public float runtimeValue;
        public long typedRow;
    }
    // envelope: an envelope column's lane, which drives its target only between its first and last point.
    public struct TrackerAutomation { public int pattern, target, points, count; public bool linear, timing, envelope; }
    // linear: the segment leaving this point is a straight line (otherwise the value holds until the next point).
    public struct TrackerAutomationPoint { public double line; public float value; public bool linear; }
    public struct TrackerClockCommand { public int kind, value; } // 1 BPM, 2 LPB, 3 TPL, 4 break
    public struct TrackerAuthoredEvent { public int track, column, payload; }
    public struct TrackerPattern { public int rows, lineCount; }
    public struct TrackerSequence { public int pattern, muteOffset; }
    public struct TrackerSend { public int destination, position; public float gain; public bool postfader; }
    public struct TrackerTrack
    {
        public TrackKind kind;
        public int columns, columnCount, output, chainStart, chainCount, sendStart, sendCount;
        public int authoredOutput, parent;
        public float instrumentGain;
        // Game-controlled offsets compose after every authored pitch source, including envelopes and macros.
        public int runtimeTranspose;
        public float runtimeDetune;
        public float preGain, prePan, width, postGain, postPan;
        public float authoredPreGain, authoredPrePan, authoredWidth;
        public float authoredPostGain, authoredPostPan, livePostGain, livePostPan, gainStep, panStep;
        // Per-frame change of the pre-effects stage within one render slice; zero outside a dynamic ramp.
        public float preGainStep, prePanStep, widthStep;
        public int gainRemaining, panRemaining;
        public bool triggerMute, outputMute, soloEnabled, beatTicks;
        public bool authoredTriggerMute, authoredOutputMute;
        public int beatInterval;
    }
    public struct TrackerChain
    {
        public SapChainLayout layout;
        public AudioChainProcessor processor;
        public NativeArray<float> authoredParameters;
        public int position;
    }
    internal static class TrackerChainSeed
    {
        public static void Apply(ref AudioChainProcessor processor,in SapChainLayout layout)
        {
            for(int i=0;i<layout.modCount;i++){
                int p=layout.modParamOffset[i],so=layout.modStateOffset[i];
                if(layout.modType[i]==Laubrary.Zounds.ZoundModifierType.Random){uint rng=processor.rng;rng^=rng<<13;rng^=rng>>17;rng^=rng<<5;processor.rng=rng;processor.arena[so]=ChainModulation.DrawRandom(layout.modParamFlat[p],layout.modParamFlat[p+1],layout.modParamFlat[p+2],(rng&0xffffff)/16777216f);}
                if(layout.modType[i]==Laubrary.Zounds.ZoundModifierType.Step){int count=layout.modStepCountOf[i];if(layout.modParamFlat[p+3]>.5f)processor.arena[so]=(int)((processor.rng&0xffffff)/16777216f*count);if(layout.modParamFlat[p]>.5f&&layout.modParamFlat[p+4]<.5f){processor.arena[so+5]=1;processor.arena[so+6]=12345;}}
            }
        }
    }
    public struct TrackerBus { public int track, instrument, fx, chain; }
    public unsafe struct TrackerState
    {
        public TrackerRewindState rewind;
        public TrackerRowReverseState rowReverse;
        public NativeArray<float> pcm, left, right, busLeft, busRight, outputLeft, outputRight, faderGain, faderPan;
        public NativeArray<TrackerPcm> clips;
        public NativeArray<TrackerSample> samples;
        public NativeArray<TrackerInstrument> instruments;
        public NativeArray<TrackerZone> zones;
        public NativeArray<TrackerMod> mods;
        public NativeArray<TrackerModPoint> points;
        public NativeArray<TrackerModState> modulationState;
        public NativeArray<TrackerVoice> voices;
        public NativeArray<TrackerEnvelopeHead> envelopeHeads;
        public NativeArray<TrackerTone> tones;
        public NativeArray<float> parameterBase, parameterDirect, parameterLive;
        public NativeArray<byte> parameterWritten;
        public NativeArray<TrackerMacroValue> macros;
        public NativeArray<TrackerParameterRoute> macroRoutes, externalRoutes;
        public NativeArray<TrackerMod> toneEnvelopes;
        public NativeArray<TrackerColumn> columns;
        public NativeArray<TrackerTrack> tracks;
        public NativeArray<TrackerSend> sends;
        public NativeArray<int> order;
        public NativeArray<TrackerPattern> patterns;
        public NativeArray<TrackerRow> rows;
        public NativeArray<TrackerCell> cells;
        public NativeArray<TrackerClockCommand> commands;
        public NativeArray<TrackerAuthoredEvent> authoredEvents;
        public NativeArray<TrackerSequence> sequence;
        public NativeArray<byte> sequenceMutes;
        public NativeArray<byte> outputMutes;
        public NativeArray<TrackerBus> buses;
        public NativeArray<TrackerOp> ops, descriptors;
        public NativeArray<int> commandMemory, sliceMarkers;
        public NativeArray<long> occurrences;
        public NativeArray<TrackerDevice> devices;
        public NativeArray<TrackerDeviceParameter> deviceParameters;
        public NativeArray<TrackerAutomation> automation;
        public NativeArray<TrackerAutomationPoint> automationPoints;
        public NativeArray<TrackerParameterSet> parameterSets;
        public NativeArray<float> presetParameters;
        public NativeArray<byte> deviceDirty, parameterDirty;
        // Indices into deviceParameters of the song's command definitions, in definition order.
        public NativeArray<int> dynamicParameters;
        public int dynamicCount;
        public NativeArray<int> routeQueue,routeMarks;
        [NativeDisableContainerSafetyRestriction] public NativeArray<long> ticket;
        [NativeDisableUnsafePtrRestriction] public TrackerChain* chains;
        public int chainCount, pcmCount, sampleCount, instrumentCount, trackCount, busCount, columnCount, modStride;
        public int macroRouteCount, externalRouteCount;
        public int sampleRate, maxFrames, master, sequenceIndex, row, tick, breakRow, linesPerBeat, ticksPerLine, beatInterval;
        public double bpm, tickRemaining, rowStart, rowEnd, clockCompensation, tickDeadline;
        public double authoredBpm;
        public float runtimeTempo;
        public double underlyingBpm;
        public bool runtimeTempoApplied;
        public int authoredLinesPerBeat, authoredTicksPerLine;
        public long transportOrigin;
        public long samplePosition, cohort;
        public float normalization;
        // Output safety limiter (T-0635): how far below unity its gain is now (0 = not limiting).
        public float limiterReduction;
        public bool playing, rowPending, loopSong, beatTicks, legacyMix;
        public bool paused, held, silentReplay;
        public int holdRemaining, opCount, deviceCount, deviceParameterCount, automationCount;
        public int carryCells, carryOps;
        public bool liveTimingPending;
        public double rowDuration, automationTime, automationDeadline;
        public ulong seed;
        public long rowOccurrence;
        // rowSerial counts every row entered (including a seek's silent replay); dynamicLanding is true from a
        // Play/Stop/Seek reset until the first row has been entered, where lanes land exactly on their line.
        public long rowSerial;
        public bool dynamicLanding;
    }
    public enum TrackerCommandKind { Play, Stop, ReleaseAll, Seek, AuditionOn, AuditionOff, TrackGain, TrackPan, TrackMute, Swap, AuditionNormalized, ReleaseVoice, MacroSet, MacroTarget, MacroAdvance, ParameterSet, ExternalSet, AmplitudeModifier, Pause, Resume, PreserveSwap, TempoOverride, TrackTranspose, TrackDetune, DynamicParameterSet, Rewind, ReverseRows, RetriggerNote }
    public struct TrackerCommand
    {
        public TrackerCommandKind kind;
        public int a, b, c, d;
        public float value;
        public long generation;
        public TrackerState replacement;
        public static TrackerCommand Play(bool loop = true) => new TrackerCommand { kind = TrackerCommandKind.Play, a = loop ? 1 : 0 };
        public static TrackerCommand Stop() => new TrackerCommand { kind = TrackerCommandKind.Stop };
        /// <summary>Persistent tempo override (32..999 BPM), effective at the next row; zero clears it.</summary>
        public static TrackerCommand SetTempo(float bpm) => new TrackerCommand { kind = TrackerCommandKind.TempoOverride, value = bpm };
        public static TrackerCommand ClearTempoOverride() => SetTempo(0);
        /// <summary>Requests tape playback of this pattern pass's actual rendered mix, then resumes at row zero.</summary>
        public static TrackerCommand RequestRewind(float seconds = 3f) => new TrackerCommand { kind = TrackerCommandKind.Rewind, value = seconds };
        /// <summary>Retrigger rows N..1 in descending order with normal forward voice playback, then land on row zero.</summary>
        public static TrackerCommand RequestReverseRows(float seconds = 3f) => new TrackerCommand { kind = TrackerCommandKind.ReverseRows, value = seconds };
        /// <summary>Independent additive semitone offset (-120..120) for active and future track voices.</summary>
        public static TrackerCommand SetTrackTranspose(int track, int semitones) => new TrackerCommand { kind = TrackerCommandKind.TrackTranspose, a = track, b = semitones };
        /// <summary>Independent additive cents offset (-1200..1200); does not replace authored fine tune.</summary>
        public static TrackerCommand SetTrackDetune(int track, float cents) => new TrackerCommand { kind = TrackerCommandKind.TrackDetune, a = track, value = cents };
        public static TrackerCommand Audition(int instrument, int note, int velocity = 127, int track = 0) => new TrackerCommand { kind = TrackerCommandKind.AuditionOn, a = instrument, b = note, c = velocity, d = track };
        /// <summary>Exact normalized audition gain. Integer-rounded velocity is used only for zone selection.</summary>
        public static TrackerCommand Audition(int instrument,int note,float velocity,int track=0)=>new TrackerCommand{kind=TrackerCommandKind.AuditionNormalized,a=instrument,b=note,c=(int)math.round(velocity*127),d=track,value=velocity};
        /// <summary>Release a specific slot only while its cohort generation still matches. A stale handle is a no-op.</summary>
        public static TrackerCommand ReleaseVoice(int slot,long generation)=>new TrackerCommand{kind=TrackerCommandKind.ReleaseVoice,a=slot,generation=generation};
        public static TrackerCommand SetMacro(int instrument,int macro,float value)=>new TrackerCommand{kind=TrackerCommandKind.MacroSet,a=instrument,b=macro,value=value};
        /// <summary>Approach by at most step per explicit active tick. Does not decode legacy G/H commands.</summary>
        public static TrackerCommand TargetMacro(int instrument,int macro,float value,float step)=>new TrackerCommand{kind=TrackerCommandKind.MacroTarget,a=instrument,b=macro,value=value,generation=math.asint(step)};
        public static TrackerCommand AdvanceMacros(int instrument)=>new TrackerCommand{kind=TrackerCommandKind.MacroAdvance,a=instrument};
        public static TrackerCommand SetParameter(int instrument,TrackerParameter parameter,float value)=>new TrackerCommand{kind=TrackerCommandKind.ParameterSet,a=instrument,b=(int)parameter,value=value};
        public static TrackerCommand SetExternal(int compiledRoute,float normalized)=>new TrackerCommand{kind=TrackerCommandKind.ExternalSet,a=compiledRoute,value=normalized};
        /// <summary>Gain=max(0,1+sin(phase)*depth), phase in cycles; scoped to foreground column cohort.</summary>
        public static TrackerCommand AmplitudeModifier(int track,int column,float depth,float hz)=>new TrackerCommand{kind=TrackerCommandKind.AmplitudeModifier,a=track,b=column,value=depth,generation=math.asint(hz)};
    }
}
