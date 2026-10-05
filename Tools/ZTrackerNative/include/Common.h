#pragma once

#include <cstdint>
#include <cmath>
#include <cstring>

#ifdef _WIN32
    #define EXPORT __declspec(dllexport)
#else
    #define EXPORT __attribute__((visibility("default")))
#endif

#ifndef M_PI
    #define M_PI 3.14159265358979323846
#endif

constexpr int MAX_VOICES      = 128;
constexpr int MAX_SAMPLES     = 512;
constexpr int MAX_INSTRUMENTS = 512;
constexpr int MAX_CHANNELS    = 32;

enum class VoiceType : uint8_t { SAMPLE, SYNTH, KIT, FM };

enum class ADSRStage : uint8_t { IDLE, ATTACK, DECAY, SUSTAIN, RELEASE };

enum class LoopMode : uint8_t { NONE, FORWARD, PING_PONG, SUSTAIN_LOOP };

enum class WaveType : uint8_t {
    SINE,
    SQUARE,
    SAWTOOTH,
    SAWTOOTH_REVERSE,
    TRIANGLE,
    NOISE_WHITE,
    NOISE_PINK
};

enum class BlendMode : uint8_t {
    MIX,        // Linear crossfade (default)
    RING,       // Multiply A * B — metallic/bell tones
    SYNC,       // A resets B's phase — aggressive/resonant
    PM          // A modulates B's phase — FM-like harmonics
};

struct ADSRParams {
    float attack  = 0.001f;
    float decay   = 0.1f;
    float sustain = 0.8f;
    float release = 0.3f;
};

struct ADSRState {
    ADSRStage stage            = ADSRStage::IDLE;
    float     level            = 0.0f;
    float     attackRate       = 0.0f;
    float     decayRate        = 0.0f;
    float     sustainLevel     = 0.0f;
    float     releaseRate      = 0.0f;
    bool      releaseRequested = false;
};

struct Sample {
    float*   data       = nullptr;  // left channel (or mono)
    float*   dataR      = nullptr;  // right channel (nullptr if mono)
    uint32_t frameCount = 0;
    uint32_t sampleRate = 0;
    uint8_t  channels   = 1;

    bool     loopEnabled = false;
    LoopMode loopMode    = LoopMode::NONE;
    uint32_t loopStart   = 0;
    uint32_t loopEnd     = 0;
    uint32_t sustainLoopStart = 0;
    uint32_t sustainLoopEnd   = 0;
};

// --- Modulation Parameters (shared by sample + synth instruments) ---

struct PortamentoParams {
    bool  enabled   = false;
    float glideTime = 0.1f;   // seconds
    bool  legato    = false;  // only glide when previous note still held
};

struct VibratoParams {
    float depth      = 0.0f;  // cents (0-200)
    float rate       = 0.0f;  // Hz (0.1-20)
    float fadeIn     = 0.0f;  // seconds before full depth
    float randomness = 0.0f;  // 0-1, jitter on rate per note
};

constexpr int MAX_ARPEGGIO_NOTES  = 5;
constexpr int MAX_ARPEGGIO_CURVE  = 16;

struct ArpeggioParams {
    bool  enabled        = false;
    int   notes[MAX_ARPEGGIO_NOTES] = {};  // semitone offsets (notes[0] usually 0)
    int   noteCount      = 2;
    bool  speedIsPerNote = true;
    int   curvePointCount = 1;
    float curveTimes[MAX_ARPEGGIO_CURVE]  = {0.0f};
    float curveValues[MAX_ARPEGGIO_CURVE] = {0.05f}; // default 50ms per note
};

// --- Modulation State (per voice) ---

struct PortamentoState {
    bool   active          = false;
    double currentIncrement = 0.0;
    double targetIncrement  = 0.0;
    double glideRate        = 0.0;   // 1 - exp(-1 / (glideTime * sampleRate))
};

struct VibratoState {
    float phase        = 0.0f;
    float fadeProgress = 0.0f;
    float fadeRate     = 0.0f;   // per sample
    float currentRate  = 0.0f;   // Hz with randomness applied
    float depth        = 0.0f;   // cents
};

struct ArpeggioState {
    bool   active        = false;
    double increments[MAX_ARPEGGIO_NOTES] = {};  // pre-calculated per note
    int    noteCount     = 0;
    int    currentIndex  = 0;
    double sampleCounter = 0.0;
    double samplesPerStep = 0.0;
    float  noteStartTime = 0.0f;  // seconds since note start (for curve eval)
    float  timePerSample = 0.0f;

    // Curve data (copied from params)
    bool  speedIsPerNote = true;
    int   curvePointCount = 0;
    float curveTimes[MAX_ARPEGGIO_CURVE]  = {};
    float curveValues[MAX_ARPEGGIO_CURVE] = {};
};

// ── Native Envelope ─────────────────────────────────────────────────
// Multi-point envelope curve evaluated per-sample on the audio thread.
// This is the single source of truth — C# pushes points, native evaluates.

constexpr int MAX_ENV_POINTS = 32;

struct NativeEnvPoint {
    float time     = 0.0f;
    float value    = 0.0f;
    float exponent = 1.0f; // 1=linear, <1=log, >1=exp
};

struct NativeEnvelope {
    bool  enabled    = false;
    int   pointCount = 0;
    NativeEnvPoint points[MAX_ENV_POINTS];
    float duration   = 1.0f;

    // Loop
    bool  loopEnabled = false;
    int   loopMode    = 0;    // 0=forward, 1=ping-pong
    float loopStart   = 0.0f;
    float loopEnd     = 1.0f;

    float Evaluate(float time) const {
        if (pointCount == 0) return 0.0f;
        if (pointCount == 1) return points[0].value;

        // Handle loop
        float t = time;
        if (loopEnabled && t > loopEnd && loopEnd > loopStart) {
            float loopLen = loopEnd - loopStart;
            float inLoop = fmodf(t - loopStart, loopLen);
            if (loopMode == 1) { // ping-pong
                int cycle = (int)((t - loopStart) / loopLen);
                if (cycle % 2 == 1) inLoop = loopLen - inLoop;
            }
            t = loopStart + inLoop;
        }

        // Clamp to range
        if (t <= points[0].time) return points[0].value;
        if (t >= points[pointCount - 1].time) return points[pointCount - 1].value;

        // Find segment
        for (int i = 1; i < pointCount; i++) {
            if (t <= points[i].time) {
                float x1 = points[i - 1].time, x2 = points[i].time;
                float range = x2 - x1;
                if (range <= 0) return points[i].value;
                float frac = (t - x1) / range;
                return points[i - 1].value + (points[i].value - points[i - 1].value) * powf(frac, points[i].exponent);
            }
        }
        return points[pointCount - 1].value;
    }
};

// Envelope parameter IDs
enum class EnvParamID : uint8_t {
    BLEND = 0,
    PULSE_WIDTH,
    WAVE_B_RATIO,
    PM_DEPTH,
    DETUNE,
    ENV_PARAM_COUNT
};

constexpr int NUM_ENV_PARAMS = (int)EnvParamID::ENV_PARAM_COUNT;

// Per-voice envelope playback state
struct VoiceEnvelopeState {
    float time = 0.0f;           // seconds since NoteOn
    float timePerSample = 0.0f;  // 1.0 / sampleRate
};

// Per-instrument effect settings
struct InstrumentEffects {
    bool  filterEnabled = false;
    int   filterMode    = 0;        // 0=LP, 1=HP, 2=BP
    float filterCutoff  = 1.0f;     // 0-1 (1 = Nyquist)
    float filterResonance = 0.707f;
    float delaySend     = 0.0f;     // 0-1
    float reverbSend    = 0.0f;     // 0-1
};

struct InstrumentEnvelopes {
    NativeEnvelope envs[NUM_ENV_PARAMS];
};

struct SampleInstrumentData {
    int   sampleID  = -1;
    int   baseNote  = 60;
    float fineTune  = 0.0f;

    // Sample B blend (mirrors SynthInstrumentData's wave-A/B blend, but with
    // two PCM samples instead of two waveforms). sampleIDB = -1 disables blend
    // and the voice renders sample A only.
    int       sampleIDB    = -1;
    int       baseNoteB    = 60;
    float     fineTuneB    = 0.0f;
    BlendMode blendMode    = BlendMode::MIX;
    float     blendDefault = 0.0f;          // 0 = only A, 1 = only B
    float     pmDepth      = 16.0f;         // PM mode: read offset in samples for B
    bool      blendEnvelopeEnabled = false;
    ADSRParams blendEnvelope;               // dedicated A/D/S/R that drives blend over time

    float volume    = 1.0f;
    float pan       = 0.0f;
    float attack    = 0.001f;
    float decay     = 0.1f;
    float sustain   = 0.8f;
    float release   = 0.3f;
    PortamentoParams portamento;
    VibratoParams    vibrato;
    ArpeggioParams   arpeggio;
    InstrumentEffects effects;
    InstrumentEnvelopes envelopes;
};

constexpr int MAX_UNISON = 8;

struct SynthInstrumentData {
    WaveType  waveA        = WaveType::SAWTOOTH;
    WaveType  waveB        = WaveType::SINE;
    BlendMode blendMode    = BlendMode::MIX;
    float     blendDefault = 0.0f;   // 0=only A, 1=only B
    float     pmDepth      = 1.0f;   // phase mod depth (for PM mode)
    float     waveBRatio   = 2.0f;   // frequency ratio of wave B relative to A (for Sync/Ring/PM)
    bool      blendEnvelopeEnabled = false;
    ADSRParams blendEnvelope;       // controls blend amount over time

    int      unisonVoices = 1;      // 1 = no unison
    float    unisonDetune = 0.0f;   // cents spread across unison voices
    float    unisonSpread = 0.0f;   // 0=mono, 1=full stereo spread

    float    pulseWidth = 0.5f;  // square wave duty cycle (0.01-0.99)

    float    volume    = 1.0f;
    float    pan       = 0.0f;
    float    attack    = 0.001f;
    float    decay     = 0.1f;
    float    sustain   = 0.8f;
    float    release   = 0.3f;
    PortamentoParams portamento;
    VibratoParams    vibrato;
    ArpeggioParams   arpeggio;
    InstrumentEffects effects;
    InstrumentEnvelopes envelopes;
};

constexpr int MAX_KIT_ENTRIES = 128;

struct KitEntry {
    int   sampleID  = -1;
    int   baseNote  = 60;
    float volume    = 1.0f;
    float pan       = 0.0f;
    float attack    = 0.001f;
    float decay     = 0.1f;
    float sustain   = 0.0f;
    float release   = 0.1f;
};

// ── FM Synthesis ─────────────────────────────────────────────────────
// 4 operators, 6 algorithms. Old school Sega style.

constexpr int FM_NUM_OPERATORS = 4;

struct FMOperator {
    float   freqRatio  = 1.0f;   // frequency multiplier relative to base note
    float   freqFixed  = 0.0f;   // fixed frequency (Hz), 0 = use ratio
    float   level      = 1.0f;   // output level / modulation depth
    float   attack     = 0.001f;
    float   decay      = 0.2f;
    float   sustain    = 0.7f;
    float   release    = 0.3f;
    WaveType waveform  = WaveType::SINE;
};

// Algorithm defines how operators connect:
// 0: [1→2→3→4]→out         (serial, classic)
// 1: [1→2]→out + [3→4]→out (two serial pairs mixed)
// 2: [1+2→3→4]→out         (two modulators into carrier chain)
// 3: [1→2]→out + 3→out + 4→out (one pair + two independent)
// 4: [1→2→3]→out + 4→out   (triple serial + independent)
// 5: [1+2+3+4]→out         (all parallel, additive)

struct FMInstrumentData {
    FMOperator operators[FM_NUM_OPERATORS];
    int        algorithm   = 0;
    float      feedback    = 0.0f;  // op1 self-feedback amount
    float      volume      = 1.0f;
    float      pan         = 0.0f;
    InstrumentEffects effects;
    InstrumentEnvelopes envelopes;
};

// Per-voice FM state
struct FMVoiceState {
    float phase[FM_NUM_OPERATORS]     = {};
    ADSRState adsr[FM_NUM_OPERATORS]  = {};
    float prevOutput                  = 0.0f; // for feedback
};

struct KitInstrumentData {
    KitEntry entries[MAX_KIT_ENTRIES]; // indexed by MIDI note
    float    masterVolume = 1.0f;
    bool     overlapOnSameTrack = true; // true = drums ring out, false = new note cuts previous
};

// ── Parameter Modifier System ────────────────────────────────────────
// Each modifier is a named ratio (1.0 = no effect). Multiple sources
// can modify the same parameter without interfering with each other.

constexpr int MAX_PARAM_MODS = 8;

struct ParamModifier {
    const char* source  = nullptr;  // e.g. "vibrato", "seq:3xx", "seq:1xx"
    double      value   = 1.0;      // target ratio (multiplicative)
    double      smooth  = 1.0;      // current smoothed value
    bool        active  = false;
};

struct ParamModStack {
    ParamModifier mods[MAX_PARAM_MODS];

    void Set(const char* source, double value) {
        // Find existing or empty slot
        int emptySlot = -1;
        for (int i = 0; i < MAX_PARAM_MODS; i++) {
            if (mods[i].active && mods[i].source == source) {
                mods[i].value = value;
                return;
            }
            if (!mods[i].active && emptySlot < 0) emptySlot = i;
        }
        if (emptySlot >= 0) {
            mods[emptySlot].source = source;
            mods[emptySlot].value = value;
            mods[emptySlot].smooth = value; // no jump on first set
            mods[emptySlot].active = true;
        }
    }

    void Clear(const char* source) {
        for (int i = 0; i < MAX_PARAM_MODS; i++) {
            if (mods[i].active && mods[i].source == source) {
                mods[i].active = false;
                mods[i].source = nullptr;
                mods[i].value = 1.0;
            }
        }
    }

    void ClearAll() {
        for (int i = 0; i < MAX_PARAM_MODS; i++) {
            mods[i] = ParamModifier{};
        }
    }

    // Evaluate with per-sample linear interpolation toward target values
    // samplesPerStep: how many samples between updates (e.g. samplesPerTick)
    double EvaluateSmoothed(double samplesPerStep) {
        double result = 1.0;
        for (int i = 0; i < MAX_PARAM_MODS; i++) {
            if (mods[i].active) {
                if (samplesPerStep > 1.0) {
                    double step = (mods[i].value - mods[i].smooth) / samplesPerStep;
                    mods[i].smooth += step;
                    // Clamp to avoid overshooting
                    if ((step > 0 && mods[i].smooth > mods[i].value) ||
                        (step < 0 && mods[i].smooth < mods[i].value))
                        mods[i].smooth = mods[i].value;
                } else {
                    mods[i].smooth = mods[i].value;
                }
                result *= mods[i].smooth;
            }
        }
        return result;
    }

    double Evaluate() const {
        double result = 1.0;
        for (int i = 0; i < MAX_PARAM_MODS; i++) {
            if (mods[i].active)
                result *= mods[i].value;
        }
        return result;
    }
};

struct Voice {
    bool         active      = false;
    VoiceType    type        = VoiceType::SAMPLE;
    int          instrumentID = -1;
    int          channelID   = -1;
    int          voiceID     = -1;
    int          midiNote    = -1;

    // Playback
    double       position    = 0.0;
    double       baseIncrement = 0.0;
    double       increment   = 0.0;
    bool         goingForward = true;

    // Sample-B cursor (only meaningful for SAMPLE voices when sampleIDB >= 0).
    // Advances independently of position so sample B can have its own pitch
    // (baseNoteB / fineTuneB) and own loop. Forward-only — no ping-pong for B.
    double       positionB   = 0.0;
    double       incrementB  = 0.0;

    // Amplitude
    float        volume      = 1.0f;
    float        pan         = 0.0f;
    float        velocity    = 1.0f;
    float        channelVolume = 1.0f;
    float        channelPan = 0.0f;
    ADSRState    adsr;

    // Parameter modifier stacks
    ParamModStack pitchMods;   // applied to baseIncrement → increment
    ParamModStack volumeMods;  // applied to volume
    double        samplesPerTick = 3528.0; // for smoothing between tick updates

    // Synth-specific
    float        synthPhase  = 0.0f;  // 0..1 phase accumulator for wave A
    float        synthPhaseB = 0.0f;  // 0..1 phase accumulator for wave B (used in sync/PM modes)
    ADSRState    blendAdsr;           // blend envelope state
    float        blendLevel  = 0.0f;  // current blend 0..1
    float        blendTarget = 0.0f;  // target blend (smoothed toward)
    float        pmDepthCur  = 1.0f;  // current PM depth (smoothed)
    float        pmDepthTarget = 1.0f;
    float        waveBRatioCur = 2.0f;  // current B ratio (smoothed)
    float        waveBRatioTarget = 2.0f;
    float        syncFilterState = 0.0f;  // one-pole LP state for sync smoothing
    float        pulseWidthCur  = 0.5f;  // current pulse width (smoothed)
    float        pulseWidthTarget = 0.5f;

    // Per-voice filter state (from instrument effects)
    bool         voiceFilterEnabled = false;
    float        vfB0 = 1, vfB1 = 0, vfB2 = 0, vfA1 = 0, vfA2 = 0;
    float        vfX1L = 0, vfX2L = 0, vfY1L = 0, vfY2L = 0;
    float        vfX1R = 0, vfX2R = 0, vfY1R = 0, vfY2R = 0;
    float        voiceDelaySend = 0.0f;
    float        voiceReverbSend = 0.0f;

    // Envelope playback
    VoiceEnvelopeState envState;

    // FM state
    FMVoiceState    fmState;

    // Modulation state
    PortamentoState portamento;
    VibratoState    vibrato;
    ArpeggioState   arpeggio;

    // Unison group: first voice in group holds the group info
    int          unisonGroup = -1;    // group ID (voiceID of first voice), -1 = not unison
    int          unisonCount = 0;     // only set on the group leader
};

inline float MidiToFreq(int midiNote) {
    return 440.0f * powf(2.0f, (midiNote - 69) / 12.0f);
}

inline double CalcIncrement(int midiNote, int baseNote, float fineTune,
                            uint32_t sampleRate, uint32_t outputRate) {
    float noteFreq = MidiToFreq(midiNote);
    float baseFreq = MidiToFreq(baseNote);
    float fineMul  = powf(2.0f, fineTune / 1200.0f);
    return (noteFreq / baseFreq) * fineMul * ((double)sampleRate / outputRate);
}

// Synth: increment = frequency / outputRate (phase advances per sample, wraps at 1.0)
inline double CalcSynthIncrement(int midiNote, float fineTune, uint32_t outputRate) {
    float freq = MidiToFreq(midiNote);
    float fineMul = powf(2.0f, fineTune / 1200.0f);
    return (freq * fineMul) / (double)outputRate;
}

inline float GenerateSample(WaveType type, float phase, float pulseWidth = 0.5f) {
    switch (type) {
    case WaveType::SINE:
        return sinf(phase * 2.0f * (float)M_PI);
    case WaveType::SQUARE:
        return phase < pulseWidth ? 1.0f : -1.0f;
    case WaveType::SAWTOOTH:
        return 2.0f * phase - 1.0f;
    case WaveType::SAWTOOTH_REVERSE:
        return 1.0f - 2.0f * phase;
    case WaveType::TRIANGLE:
        return phase < 0.5f ? 4.0f * phase - 1.0f : 3.0f - 4.0f * phase;
    case WaveType::NOISE_WHITE:
        return ((float)rand() / RAND_MAX) * 2.0f - 1.0f;
    case WaveType::NOISE_PINK:
        return ((float)rand() / RAND_MAX) * 2.0f - 1.0f;
    default:
        return 0.0f;
    }
}

inline float Lerp(const float* data, uint32_t frameCount, double pos) {
    uint32_t i0 = (uint32_t)pos;
    uint32_t i1 = (i0 + 1 < frameCount) ? i0 + 1 : i0;
    float t = (float)(pos - i0);
    return data[i0] + t * (data[i1] - data[i0]);
}
