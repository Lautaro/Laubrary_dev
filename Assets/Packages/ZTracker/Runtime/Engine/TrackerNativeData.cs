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
    public enum TrackerEventKind { Row, Tick, NoteOn, NoteOff, Beat, Authored, VoiceStolen, PreparedSwap, Stopped, Diagnostic, Started }
    public enum TrackerRuntimeDiagnostic { SeekUnreachable=1, PitchLimit=2, InvalidPitch=3, BreakClamped=4 }
    public struct TrackerEvent
    {
        public TrackerEventKind kind;
        public long samplePosition;
        public int sequence, pattern, row, tick, track, column, note, instrument, payload;
    }
    /// <summary>One audio producer and one main-thread consumer. Storage outlives graph instances and prepared swaps.</summary>
    public unsafe struct TrackerEventRing : IDisposable
    {
        [NativeDisableUnsafePtrRestriction] public TrackerEvent* events;
        [NativeDisableUnsafePtrRestriction] public long* counters; // published write, consumed read, overflow
        public int capacity;
        public static TrackerEventRing Create(int capacity)
        {
            if (capacity < 2 || capacity > 1048576) throw new ArgumentOutOfRangeException(nameof(capacity));
            var r = new TrackerEventRing { capacity = capacity };
            r.events = (TrackerEvent*)UnsafeUtility.Malloc(capacity * (long)sizeof(TrackerEvent), 16, Allocator.Persistent);
            r.counters = (long*)UnsafeUtility.Malloc(3 * sizeof(long), 16, Allocator.Persistent);
            UnsafeUtility.MemClear(r.counters, 3 * sizeof(long));
            return r;
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
            this = default;
        }
    }
    public struct TrackerPcm { public int offset, frames, channels, frequency; public float peak; }
    public struct TrackerSample
    {
        public int pcm, instrument, loopStart, loopEnd, modStart, modCount, filterType, fxChain, muteGroup;
        public SampleLoop loop;
        public SampleInterpolation interpolation;
        public NewNoteAction nna;
        public bool releaseExitsLoop, oneShot, legacyPan, legacySamplePitch, orderedVolume;
        public float legacyFineTuneCents;
        public int legacyTranspose;
        public float volume, pan, tune, cutoff, resonance, attack, hold, decay, sustain, release;
        public int legacyFilter;
        public int delayDestination, reverbDestination;
        public float delaySend, reverbSend;
    }
    public struct TrackerZone { public int sample, minNote, maxNote, minVelocity, maxVelocity, baseNote; public bool tracking; }
    public struct TrackerInstrument { public int zoneStart, zoneCount, chainStart, chainCount; public NewNoteAction nna; }
    public struct TrackerModPoint { public double time; public float value, exponent; }
    public struct TrackerMod
    {
        public ModulationTarget target;
        public ModulationDeviceKind kind;
        public ModulationOperation operation;
        public float attack, hold, decay, sustain, release, rate, depth, phase, min, max, curve;
        public int points, pointCount, shape;
        public double sustainPosition, loopStart, loopEnd;
        public bool sustainEnabled, loopEnabled;
        public SampleLoop loop;
        public float fadeSeconds, randomness;
        public bool advanceFirst, primaryEnvelope;
    }
    public struct TrackerModState { public double position; public float releaseStart, output; public bool released; public int direction; }
    public struct TrackerVoice
    {
        public bool active, released, pitchLimited;
        public int sample, track, column, note, instrument, bus, velocity, stage, direction;
        public long cohort, age, releaseAge;
        public double position, step;
        public float volume, pan, envelope, releaseStart, releaseStep, amplitude;
        public float fL1, fL2, fR1, fR2, xL1, xL2, xR1, xR2;
        public float b0, b1, b2, a1, a2, filterCutoff, filterQ;
        public float phaseA, phaseB, syncFilter, blendCurrent, pmCurrent, ratioCurrent, pulseCurrent, detuneCurrent, envTime;
        public float memberSpread, memberGain, blendLevel, blendReleaseStart, arpTime;
        public int blendStage, arpIndex, directionB;
        public double positionB, stepB, glideCurrent, arpCounter, arpStep;
        public uint noiseA, noiseB;
        public float3 pinkA, pinkB;
        public float4 fmPhase, fmLevel, fmReleaseStart;
        public int4 fmStage;
        public float fmPrevious;
        public float vibratoPhase, vibratoFade;
        public double vibratoSmooth, arpSmooth;
        public float amplitudeDepth, amplitudePhase, amplitudeRate;
    }
    public struct TrackerColumn
    {
        public int instrument;
        public float volume, pan;
        public long cohort, due;
        public double dueExact;
        public int pendingNote, pendingInstrument, pendingVelocity, pendingCell;
        public bool pendingOff;
        public int previousNote;
        public bool hasPreviousNote;
    }
    public struct TrackerCell
    {
        public int track, column, note, instrument, volume, pan, delay;
        public bool instrumentPresent;
    }
    public struct TrackerRow { public int cells, cellCount, commands, commandCount, events, eventCount; }
    public struct TrackerClockCommand { public int kind, value; } // 1 BPM, 2 LPB, 3 TPL, 4 break
    public struct TrackerAuthoredEvent { public int track, column, payload; }
    public struct TrackerPattern { public int rows, lineCount; }
    public struct TrackerSequence { public int pattern, muteOffset; }
    public struct TrackerSend { public int destination, position; public float gain; public bool postfader; }
    public struct TrackerTrack
    {
        public TrackKind kind;
        public int columns, columnCount, output, chainStart, chainCount, sendStart, sendCount;
        public float preGain, prePan, width, postGain, postPan;
        public float authoredPostGain, authoredPostPan, livePostGain, livePostPan, gainStep, panStep;
        public int gainRemaining, panRemaining;
        public bool triggerMute, outputMute, soloEnabled, beatTicks;
        public bool authoredTriggerMute, authoredOutputMute;
        public int beatInterval;
    }
    public struct TrackerChain
    {
        public SapChainLayout layout;
        public AudioChainProcessor processor;
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
        public NativeArray<float> pcm, left, right, busLeft, busRight, outputLeft, outputRight, faderGain, faderPan;
        public NativeArray<TrackerPcm> clips;
        public NativeArray<TrackerSample> samples;
        public NativeArray<TrackerInstrument> instruments;
        public NativeArray<TrackerZone> zones;
        public NativeArray<TrackerMod> mods;
        public NativeArray<TrackerModPoint> points;
        public NativeArray<TrackerModState> modulationState;
        public NativeArray<TrackerVoice> voices;
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
        [NativeDisableContainerSafetyRestriction] public NativeArray<long> ticket;
        [NativeDisableUnsafePtrRestriction] public TrackerChain* chains;
        public int chainCount, pcmCount, sampleCount, instrumentCount, trackCount, busCount, columnCount, modStride;
        public int macroRouteCount, externalRouteCount;
        public int sampleRate, maxFrames, master, sequenceIndex, row, tick, breakRow, linesPerBeat, ticksPerLine, beatInterval;
        public double bpm, tickRemaining, rowStart, rowEnd, clockCompensation, tickDeadline;
        public double authoredBpm;
        public int authoredLinesPerBeat, authoredTicksPerLine;
        public long transportOrigin;
        public long samplePosition, cohort;
        public float normalization;
        public bool playing, rowPending, loopSong, beatTicks, legacyMix;
    }
    public enum TrackerCommandKind { Play, Stop, ReleaseAll, Seek, AuditionOn, AuditionOff, TrackGain, TrackPan, TrackMute, Swap, AuditionNormalized, ReleaseVoice, MacroSet, MacroTarget, MacroAdvance, ParameterSet, ExternalSet, AmplitudeModifier }
    public struct TrackerCommand
    {
        public TrackerCommandKind kind;
        public int a, b, c, d;
        public float value;
        public long generation;
        public TrackerState replacement;
        public static TrackerCommand Play(bool loop = true) => new TrackerCommand { kind = TrackerCommandKind.Play, a = loop ? 1 : 0 };
        public static TrackerCommand Stop() => new TrackerCommand { kind = TrackerCommandKind.Stop };
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
