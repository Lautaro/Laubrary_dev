using System;
using System.Collections.Generic;
using Laubrary.Audio;
using UnityEngine;

namespace Laubrary.ZTracker.Model
{
    public enum InstrumentFamily { Sampler, Synth }
    public enum SynthMode { Subtractive, FM }
    public enum SampleLoop { Off, Forward, Backward, PingPong }
    public enum SampleInterpolation { Linear, Cubic }
    public enum NewNoteAction { Cut, NoteOff, Continue }
    public enum ModulationTarget { Volume, Pan, Pitch, Cutoff, Resonance, Drive }
    public enum ModulationDeviceKind { AHDSR, Multipoint, LFO, Velocity, KeyTracking, Fader }
    public enum ModulationOperation { Add, Multiply, Replace }

    [Serializable] public sealed class Mapping
    {
        public ParameterTarget target = new ParameterTarget();
        public float min, max = 1, curve = 1;
        public string legacyLink = "";
    }
    [Serializable] public sealed class InstrumentMacro
    {
        public string name = "";
        public float value;
        public List<Mapping> mappings = new List<Mapping>();
    }
    [Serializable] public sealed class ModulationPoint { public double time; public float value, exponent = 1; }
    [Serializable] public sealed class ModulationDevice
    {
        public string id = "", units = "seconds";
        public ModulationTarget target;
        public ModulationDeviceKind kind;
        public ModulationOperation operation = ModulationOperation.Multiply;
        public bool enabled = true;
        public float attack = .01f, hold, decay = .2f, sustain = .7f, release = .5f;
        public List<ModulationPoint> points = new List<ModulationPoint>();
        public bool sustainEnabled, loopEnabled;
        public double sustainPosition, loopStart, loopEnd = 1;
        public SampleLoop loop;
        public int lfoShape;
        public float rate = 1, depth = 1, phase, min, max = 1, curve = 1;
    }
    [Serializable] public sealed class ModulationSet
    {
        public string id = "", name = "";
        public int filterType;
        public List<ModulationDevice> devices = new List<ModulationDevice>();
    }
    [Serializable] public sealed class SampleData
    {
        public string id = "", name = "";
        // PCM is borrowed by identity. Voice state never owns or copies this asset.
        public AudioClip pcm;
        public float volume = 1, pan, fineTuneCents;
        public int transpose, baseNote = 60;
        public SampleLoop loop;
        public int loopStartFrame, loopEndFrame;
        public bool releaseExitsLoop, oneShot;
        public SampleInterpolation interpolation;
        public NewNoteAction nna = NewNoteAction.NoteOff;
        public int modulationSet = -1, fxChain = -1, muteGroup = -1;
        public bool inactive;
        public int legacyBaseNote;
        public float legacyVolume;
        public bool legacyKitDefaults;
    }
    [Serializable] public sealed class SampleBlendExtension
    {
        public AudioClip pcmB;
        public int baseNoteB = 60, mode;
        public float fineTuneBCents, amount, pmDepth = 1;
        public bool envelopeEnabled;
        public float attack, decay, sustain, release;
        public ZUIEnvelopeData blendEnvelope, pmEnvelope;
        public bool requiresRenoiseInterchangeWarning = true;
    }
    [Serializable] public sealed class Keyzone
    {
        public string id = "";
        public int sample, noteMin, noteMax = 119, velocityMin, velocityMax = 127, baseNote = 60;
        public bool keyTracking = true, inactive;
        public SampleBlendExtension blend;
        public string provenance = "";
    }
    [Serializable] public sealed class SamplerData
    {
        public List<SampleData> samples = new List<SampleData>();
        public List<Keyzone> zones = new List<Keyzone>();
        public float volume = 1, pan, fineTuneCents;
        public int transpose;
        public NewNoteAction nna = NewNoteAction.NoteOff;
    }
    [Serializable] public sealed class ExternalParameterMapping
    {
        public string externalId = "";
        public Mapping mapping = new Mapping();
    }

    // Same public field spellings permit exact JSON conversion of the retained payload,
    // including inactive engines and disabled preset sections. This is authoring data only.
    [Serializable] public sealed class InstrumentParameters
    {
        public InstrumentType type;
        public AudioClip sampleClip, sampleClipB;
        public int baseNote = 60, baseNoteB = 60;
        public float fineTune, fineTuneB;
        public int waveA = 2, waveB, blendMode;
        public float blend, pmDepth = 1, waveBRatio = 2;
        public bool blendEnvelope;
        public float blendAttack = .01f, blendDecay = .1f, blendSustain = .5f, blendRelease = .3f;
        public int unisonVoices = 1;
        public float unisonDetune = 10, unisonSpread = .8f, pulseWidth = .5f;
        public float volume = 1, pan, attack = .01f, decay = .2f, sustain = .7f, release = .5f;
        public float vibratoDepth, vibratoRate = 5, vibratoFadeIn, vibratoRandomness;
        public bool arpeggioEnabled;
        public int[] arpeggioNotes = {0,4,7};
        public float arpeggioSpeed = .05f;
        public ZUIEnvelopeData blendEnvelopeData, pulseWidthEnvelopeData, waveBRatioEnvelopeData, pmDepthEnvelopeData, unisonDetuneEnvelopeData;
        public bool instFilterEnabled;
        public int instFilterMode;
        public float instFilterCutoff = 1, instFilterResonance = .707f, instDelaySend, instReverbSend;
        public int fmAlgorithm;
        public float fmFeedback;
        public ZTrackerInstrument.FMOperatorData[] fmOperators;
        public bool kitOverlap = true;
        public ZTrackerInstrument.KitEntry[] kitEntries;
        public ZTrackerInstrument.MacroDef[] macros;
        public List<ZTrackerInstrument.InstrumentPreset> presets = new List<ZTrackerInstrument.InstrumentPreset>();
        public int activePresetIndex = -1;
    }
    [Serializable] public sealed class InstrumentData
    {
        public string id = "", name = "";
        public InstrumentFamily family;
        public SynthMode synthMode;
        public InstrumentParameters parameters = new InstrumentParameters();
        public SamplerData sampler = new SamplerData();
        public List<ModulationSet> modulation = new List<ModulationSet>();
        public List<AudioEffectChainData> fxChains = new List<AudioEffectChainData>();
        public InstrumentMacro[] macros = new InstrumentMacro[8];
        public List<InstrumentMacro> archivedMacros = new List<InstrumentMacro>();
        public List<ExternalParameterMapping> externalParameters = new List<ExternalParameterMapping>();
        public List<string> diagnostics = new List<string>();
        public string provenance = "";
    }
    // A compiled parameter set is independent managed preparation data. No canonical
    // instrument slot is added, and curves/preset arrays do not alias the authoring asset.
    public sealed class CompiledParameterSet
    {
        public string id;
        public InstrumentData data;
    }
}
