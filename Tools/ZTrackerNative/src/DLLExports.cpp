#include "Common.h"
#include "VoicePlayer.h"
#include "Sequencer.h"
#include "EventQueue.h"
#include "Effects.h"
#include <algorithm>
#include <atomic>
#include <thread>
#include <chrono>

struct ZTrackerContext {
    VoicePlayer  voicePlayer;
    Sequencer    sequencer;
    EventQueue   eventQueue;
    MixBus       mixBus;

    // Master inserts (applied to final stereo output)
    BiquadFilter masterFilter;
    Delay        masterDelay;
    Reverb       masterReverb;
    bool         masterFilterEnabled = false;
    bool         masterDelayEnabled  = false;
    bool         masterReverbEnabled = false;

    uint32_t     sampleRate;

    // ── Shutdown barrier ─────────────────────────────────────────────────
    // stopped:    set by ZT_Destroy; ZT_Process observes and short-circuits.
    // inProcess:  incremented on entry to ZT_Process, decremented on exit;
    //             lets ZT_Destroy wait for any in-flight audio call to drain
    //             before freeing memory. Without this, a concurrent
    //             OnAudioFilterRead call would touch freed state on Unity
    //             domain reload / play mode exit.
    std::atomic<bool> stopped { false };
    std::atomic<int>  inProcess { 0 };
};

extern "C" {

EXPORT int ZT_GetABIVersion() { return 1; }

// --- Pipeline test (keep for verification) ---
EXPORT int ZT_Add(int a, int b) {
    return a + b;
}

// --- Engine lifecycle ---

EXPORT void* ZT_Create(int sampleRate) {
    ZTrackerContext* ctx = new ZTrackerContext();
    ctx->sampleRate = sampleRate;
    ctx->voicePlayer.Init(sampleRate);
    ctx->sequencer.Init(sampleRate);
    ctx->eventQueue.Init();
    ctx->sequencer.SetEventQueue(&ctx->eventQueue);
    ctx->mixBus.Init(sampleRate);
    ctx->masterFilter.Init(sampleRate);
    ctx->masterDelay.Init(sampleRate);
    ctx->masterReverb.Init(sampleRate);
    return ctx;
}

EXPORT void ZT_Destroy(void* ptr) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;

    // Raise the barrier. Any future ZT_Process call will see this with
    // memory_order_acquire and short-circuit before touching ctx members.
    ctx->stopped.store(true, std::memory_order_release);

    // Wait for any in-flight ZT_Process call to finish before we delete.
    // 100ms upper bound is well beyond the longest plausible audio buffer
    // (~21ms at 1024 frames / 48kHz). If we time out, we delete anyway —
    // the alternative is a definite leak on a permanent hang.
    for (int i = 0; i < 100; i++) {
        if (ctx->inProcess.load(std::memory_order_acquire) == 0) break;
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }

    ctx->voicePlayer.AllNotesOff();
    delete ctx;
}

// --- Audio thread ---

EXPORT void ZT_Process(void* ptr, float* outL, float* outR, int numFrames) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;

    // Shutdown barrier: if ZT_Destroy has raised `stopped`, produce silence
    // and return before touching any ctx member. The inProcess counter is
    // what ZT_Destroy waits on; we increment with acquire ordering so
    // ZT_Destroy's release-store of `stopped` is visible if it happened
    // before our load, and decrement with release so ZT_Destroy's
    // acquire-load sees our exit.
    ctx->inProcess.fetch_add(1, std::memory_order_acquire);
    if (ctx->stopped.load(std::memory_order_acquire)) {
        std::memset(outL, 0, numFrames * sizeof(float));
        std::memset(outR, 0, numFrames * sizeof(float));
        ctx->inProcess.fetch_sub(1, std::memory_order_release);
        return;
    }

    ctx->sequencer.Process(ctx->voicePlayer, outL, outR, numFrames);

    // Per-instrument send effects (delay/reverb from voice send buffers)
    if (ctx->voicePlayer.hasSends_) {
        int frames = std::min(numFrames, VoicePlayer::MAX_SEND_BUFFER);
        // Process delay sends
        ctx->masterDelay.Process(ctx->voicePlayer.sendDelayL_, ctx->voicePlayer.sendDelayR_, frames);
        for (int i = 0; i < frames; i++) {
            outL[i] += ctx->voicePlayer.sendDelayL_[i];
            outR[i] += ctx->voicePlayer.sendDelayR_[i];
        }
        // Process reverb sends
        ctx->masterReverb.Process(ctx->voicePlayer.sendReverbL_, ctx->voicePlayer.sendReverbR_, frames);
        for (int i = 0; i < frames; i++) {
            outL[i] += ctx->voicePlayer.sendReverbL_[i];
            outR[i] += ctx->voicePlayer.sendReverbR_[i];
        }
    }

    // Master inserts
    if (ctx->masterFilterEnabled)
        ctx->masterFilter.Process(outL, outR, numFrames);
    if (ctx->masterDelayEnabled)
        ctx->masterDelay.Process(outL, outR, numFrames);
    if (ctx->masterReverbEnabled)
        ctx->masterReverb.Process(outL, outR, numFrames);

    // Let ZT_Destroy proceed if it's waiting for us.
    ctx->inProcess.fetch_sub(1, std::memory_order_release);
}

// --- Sample bank ---

EXPORT int ZT_LoadSample(void* ptr, float* dataL, float* dataR,
                         int frameCount, int sampleRate, int channels) {
    if (!ptr) return -1;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    return ctx->voicePlayer.LoadSample(dataL, dataR, frameCount, sampleRate, channels);
}

EXPORT void ZT_SetSampleLoop(void* ptr, int sampleID, int loopMode,
                             int loopStart, int loopEnd) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->voicePlayer.SetSampleLoop(sampleID, loopMode, loopStart, loopEnd);
}

EXPORT void ZT_SetSampleSustainLoop(void* ptr, int sampleID, int start, int end) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->voicePlayer.SetSampleSustainLoop(sampleID, start, end);
}

// --- Instruments ---

EXPORT void ZT_SetSampleInstrument(void* ptr, int id,
                                   int sampleID, int baseNote, float fineTune,
                                   float volume, float pan,
                                   float attack, float decay,
                                   float sustain, float release,
                                   int sampleIDB, int baseNoteB, float fineTuneB,
                                   int blendMode, float blendDefault, float pmDepth,
                                   int blendEnvelopeEnabled,
                                   float blendAttack, float blendDecay,
                                   float blendSustain, float blendRelease) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    SampleInstrumentData data;
    data.sampleID = sampleID;
    data.baseNote = baseNote;
    data.fineTune = fineTune;
    data.volume   = volume;
    data.pan      = pan;
    data.attack   = attack;
    data.decay    = decay;
    data.sustain  = sustain;
    data.release  = release;
    data.sampleIDB = sampleIDB;
    data.baseNoteB = baseNoteB;
    data.fineTuneB = fineTuneB;
    data.blendMode = (BlendMode)blendMode;
    data.blendDefault = blendDefault;
    data.pmDepth = pmDepth;
    data.blendEnvelopeEnabled = (blendEnvelopeEnabled != 0);
    data.blendEnvelope.attack  = blendAttack;
    data.blendEnvelope.decay   = blendDecay;
    data.blendEnvelope.sustain = blendSustain;
    data.blendEnvelope.release = blendRelease;
    ctx->voicePlayer.SetSampleInstrument(id, data);
}

EXPORT void ZT_SetSynthInstrument(void* ptr, int id,
                                  int waveA, int waveB,
                                  int blendMode, float blendDefault, float pmDepth, float waveBRatio,
                                  int blendEnvelopeEnabled,
                                  float blendAttack, float blendDecay,
                                  float blendSustain, float blendRelease,
                                  int unisonVoices, float unisonDetune, float unisonSpread,
                                  float volume, float pan,
                                  float attack, float decay,
                                  float sustain, float release,
                                  float pulseWidth) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    SynthInstrumentData data;
    data.waveA = (WaveType)waveA;
    data.waveB = (WaveType)waveB;
    data.blendMode = (BlendMode)blendMode;
    data.blendDefault = blendDefault;
    data.pmDepth = pmDepth;
    data.waveBRatio = waveBRatio;
    data.blendEnvelopeEnabled = (blendEnvelopeEnabled != 0);
    data.blendEnvelope.attack  = blendAttack;
    data.blendEnvelope.decay   = blendDecay;
    data.blendEnvelope.sustain = blendSustain;
    data.blendEnvelope.release = blendRelease;
    data.unisonVoices = unisonVoices;
    data.unisonDetune = unisonDetune;
    data.unisonSpread = unisonSpread;
    data.volume  = volume;
    data.pan     = pan;
    data.attack  = attack;
    data.decay   = decay;
    data.sustain = sustain;
    data.release = release;
    data.pulseWidth = pulseWidth;
    ctx->voicePlayer.SetSynthInstrument(id, data);
}

// --- Voice Player ---

EXPORT int ZT_NoteOn(void* ptr, int instrumentID, int midiNote, float velocity) {
    if (!ptr) return -1;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    return ctx->voicePlayer.NoteOn(instrumentID, midiNote, velocity);
}

EXPORT void ZT_NoteOff(void* ptr, int voiceID) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->voicePlayer.NoteOff(voiceID);
}

EXPORT void ZT_AllNotesOff(void* ptr) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->voicePlayer.AllNotesOff();
}

// --- FM Instruments ---

EXPORT void ZT_SetFMInstrument(void* ptr, int id, int algorithm, float feedback,
                                float volume, float pan) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    FMInstrumentData data;
    data.algorithm = algorithm;
    data.feedback = feedback;
    data.volume = volume;
    data.pan = pan;
    ctx->voicePlayer.SetFMInstrument(id, data);
}

EXPORT void ZT_SetFMOperator(void* ptr, int instrumentID, int opIndex,
                              float freqRatio, float freqFixed, float level,
                              int waveform,
                              float attack, float decay, float sustain, float release) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (instrumentID < 0 || instrumentID >= MAX_INSTRUMENTS) return;
    if (opIndex < 0 || opIndex >= FM_NUM_OPERATORS) return;
    auto& op = ctx->voicePlayer.fmInstruments_[instrumentID].operators[opIndex];
    op.freqRatio = freqRatio;
    op.freqFixed = freqFixed;
    op.level = level;
    op.waveform = (WaveType)waveform;
    op.attack = attack;
    op.decay = decay;
    op.sustain = sustain;
    op.release = release;
}

// --- Kit Instruments ---

EXPORT void ZT_SetKitInstrument(void* ptr, int id) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->voicePlayer.SetKitInstrument(id);
}

EXPORT void ZT_SetKitEntry(void* ptr, int instrumentID, int midiNote,
                           int sampleID, int baseNote,
                           float volume, float pan,
                           float attack, float decay, float sustain, float release) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    KitEntry entry;
    entry.sampleID = sampleID;
    entry.baseNote = baseNote;
    entry.volume = volume;
    entry.pan = pan;
    entry.attack = attack;
    entry.decay = decay;
    entry.sustain = sustain;
    entry.release = release;
    ctx->voicePlayer.SetKitEntry(instrumentID, midiNote, entry);
}

EXPORT void ZT_SetVoicePitch(void* ptr, int voiceID, double increment) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return;
    ctx->voicePlayer.voices_[voiceID].increment = increment;
    ctx->voicePlayer.voices_[voiceID].baseIncrement = increment;
}

EXPORT void ZT_SetVoiceVolume(void* ptr, int voiceID, float volume) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return;
    ctx->voicePlayer.voices_[voiceID].volume = volume;
}

EXPORT void ZT_SetVoicePulseWidth(void* ptr, int voiceID, float pw) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return;
    ctx->voicePlayer.voices_[voiceID].pulseWidthTarget = pw;
}

EXPORT void ZT_SetVoiceWaveBRatio(void* ptr, int voiceID, float ratio) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return;
    ctx->voicePlayer.voices_[voiceID].waveBRatioTarget = ratio;
}

EXPORT void ZT_SetKitOverlap(void* ptr, int instrumentID, int overlap) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (instrumentID < 0 || instrumentID >= MAX_INSTRUMENTS) return;
    ctx->voicePlayer.kitInstruments_[instrumentID].overlapOnSameTrack = (overlap != 0);
}

EXPORT void ZT_SetVoiceBlend(void* ptr, int voiceID, float blend) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return;
    // Set blend target on this voice and all voices in the same unison group
    int groupID = ctx->voicePlayer.voices_[voiceID].unisonGroup;
    if (groupID >= 0) {
        for (int i = 0; i < MAX_VOICES; i++) {
            if (ctx->voicePlayer.voices_[i].active && ctx->voicePlayer.voices_[i].unisonGroup == groupID)
                ctx->voicePlayer.voices_[i].blendTarget = blend;
        }
    } else {
        ctx->voicePlayer.voices_[voiceID].blendTarget = blend;
    }
}

// --- Modulation ---

EXPORT void ZT_SetInstrumentPortamento(void* ptr, int instrumentID,
                                       int enabled, float glideTime, int legato) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (instrumentID < 0 || instrumentID >= MAX_INSTRUMENTS) return;

    PortamentoParams pp;
    pp.enabled = (enabled != 0);
    pp.glideTime = glideTime;
    pp.legato = (legato != 0);

    if (ctx->voicePlayer.instrumentTypes_[instrumentID] == VoiceType::SAMPLE)
        ctx->voicePlayer.sampleInstruments_[instrumentID].portamento = pp;
    else
        ctx->voicePlayer.synthInstruments_[instrumentID].portamento = pp;
}

EXPORT void ZT_SetInstrumentVibrato(void* ptr, int instrumentID,
                                    float depth, float rate, float fadeIn, float randomness) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (instrumentID < 0 || instrumentID >= MAX_INSTRUMENTS) return;

    VibratoParams vp;
    vp.depth = depth;
    vp.rate = rate;
    vp.fadeIn = fadeIn;
    vp.randomness = randomness;

    if (ctx->voicePlayer.instrumentTypes_[instrumentID] == VoiceType::SAMPLE)
        ctx->voicePlayer.sampleInstruments_[instrumentID].vibrato = vp;
    else
        ctx->voicePlayer.synthInstruments_[instrumentID].vibrato = vp;
}

EXPORT void ZT_SetInstrumentArpeggio(void* ptr, int instrumentID,
                                     int enabled, int* notes, int noteCount,
                                     int speedIsPerNote,
                                     float* curveTimes, float* curveValues, int curvePointCount) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (instrumentID < 0 || instrumentID >= MAX_INSTRUMENTS) return;

    ArpeggioParams ap;
    ap.enabled = (enabled != 0);
    ap.noteCount = std::min(noteCount, MAX_ARPEGGIO_NOTES);
    for (int i = 0; i < ap.noteCount; i++)
        ap.notes[i] = notes[i];
    ap.speedIsPerNote = (speedIsPerNote != 0);
    ap.curvePointCount = std::min(curvePointCount, MAX_ARPEGGIO_CURVE);
    for (int i = 0; i < ap.curvePointCount; i++) {
        ap.curveTimes[i] = curveTimes[i];
        ap.curveValues[i] = curveValues[i];
    }

    if (ctx->voicePlayer.instrumentTypes_[instrumentID] == VoiceType::SAMPLE)
        ctx->voicePlayer.sampleInstruments_[instrumentID].arpeggio = ap;
    else
        ctx->voicePlayer.synthInstruments_[instrumentID].arpeggio = ap;
}

// --- Sequencer ---

EXPORT void ZT_SetSongTempo(void* ptr, int bpm, int ticksPerRow) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetTempo(bpm, ticksPerRow);
}

EXPORT void ZT_SetLinesPerBeat(void* ptr, int lpb) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetLinesPerBeat(lpb);
}

EXPORT void ZT_SetChannelCount(void* ptr, int count) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetChannelCount(count);
}

EXPORT void ZT_SetPatternData(void* ptr, int patternID, TrackerCell* cells,
                              int rowCount, int channelCount) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetPatternData(patternID, cells, rowCount, channelCount);
}

EXPORT void ZT_SetOrderList(void* ptr, int* order, int length) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetOrderList(order, length);
}

EXPORT void ZT_MuteChannel(void* ptr, int channel, int muted) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.MuteChannel(channel, muted != 0);
}

EXPORT void ZT_SetChannelVolume(void* ptr, int channel, float volume) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetChannelVolume(channel, volume);
}

EXPORT void ZT_SetChannelPan(void* ptr, int channel, float pan) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetChannelPan(channel, pan);
}

EXPORT void ZT_Play(void* ptr, int fromOrder, int fromRow) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.Play(fromOrder, fromRow);
}

EXPORT void ZT_Stop(void* ptr) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.Stop();
    ctx->voicePlayer.AllNotesOff();
}

EXPORT int ZT_GetCurrentRow(void* ptr) {
    if (!ptr) return 0;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    return ctx->sequencer.GetCurrentRow();
}

EXPORT int ZT_GetCurrentOrder(void* ptr) {
    if (!ptr) return 0;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    return ctx->sequencer.GetCurrentOrder();
}

// --- Event Tracks & Queries ---

EXPORT void ZT_SetChannelType(void* ptr, int channel, int type) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetChannelType(channel, type);
}

EXPORT void ZT_SetEventString(void* ptr, int patternID, int row, int channel, const char* str) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetEventString(patternID, row, channel, str);
}

EXPORT void ZT_SetBeatTickInterval(void* ptr, int rows) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetBeatTickInterval(rows);
}

EXPORT double ZT_GetVoiceCurrentPitch(void* ptr, int voiceID) {
    if (!ptr) return 0;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return 0;
    auto& v = ctx->voicePlayer.voices_[voiceID];
    if (!v.active) return 0;
    // Convert increment back to frequency
    if (v.type == VoiceType::SYNTH || v.type == VoiceType::FM)
        return v.increment * ctx->sampleRate; // synth: increment = freq / sampleRate
    else
        return v.increment; // sample: increment is ratio, not directly frequency
}

EXPORT int ZT_GetVoiceCurrentNote(void* ptr, int voiceID) {
    if (!ptr) return -1;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return -1;
    return ctx->voicePlayer.voices_[voiceID].midiNote;
}

EXPORT int ZT_IsVoiceActive(void* ptr, int voiceID) {
    if (!ptr) return 0;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return 0;
    return ctx->voicePlayer.voices_[voiceID].active ? 1 : 0;
}

// --- Instrument Envelopes ---

#pragma pack(push, 1)
struct NativeEnvPointData {
    float time;
    float value;
    float exponent;
};
#pragma pack(pop)

EXPORT void ZT_SetInstrumentEnvelope(void* ptr, int instrumentID, int paramID,
                                      int enabled, NativeEnvPointData* points, int pointCount,
                                      float duration, int loopEnabled, int loopMode,
                                      float loopStart, float loopEnd) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (instrumentID < 0 || instrumentID >= MAX_INSTRUMENTS) return;
    if (paramID < 0 || paramID >= NUM_ENV_PARAMS) return;

    // Get the envelope array for this instrument type
    InstrumentEnvelopes* envs = nullptr;
    switch (ctx->voicePlayer.instrumentTypes_[instrumentID]) {
        case VoiceType::SYNTH: envs = &ctx->voicePlayer.synthInstruments_[instrumentID].envelopes; break;
        case VoiceType::SAMPLE: envs = &ctx->voicePlayer.sampleInstruments_[instrumentID].envelopes; break;
        case VoiceType::FM: envs = &ctx->voicePlayer.fmInstruments_[instrumentID].envelopes; break;
        default: return;
    }

    auto& env = envs->envs[paramID];
    env.enabled = (enabled != 0);
    env.duration = duration;
    env.loopEnabled = (loopEnabled != 0);
    env.loopMode = loopMode;
    env.loopStart = loopStart;
    env.loopEnd = loopEnd;
    env.pointCount = std::min(pointCount, MAX_ENV_POINTS);
    for (int i = 0; i < env.pointCount; i++) {
        env.points[i].time = points[i].time;
        env.points[i].value = points[i].value;
        env.points[i].exponent = points[i].exponent;
    }
}

// --- Instrument Effects ---

EXPORT void ZT_SetInstrumentEffects(void* ptr, int instrumentID,
                                     int filterEnabled, int filterMode, float cutoff, float resonance,
                                     float delaySend, float reverbSend) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (instrumentID < 0 || instrumentID >= MAX_INSTRUMENTS) return;

    InstrumentEffects fx;
    fx.filterEnabled = (filterEnabled != 0);
    fx.filterMode = filterMode;
    fx.filterCutoff = cutoff;
    fx.filterResonance = resonance;
    fx.delaySend = delaySend;
    fx.reverbSend = reverbSend;

    switch (ctx->voicePlayer.instrumentTypes_[instrumentID]) {
        case VoiceType::SAMPLE: ctx->voicePlayer.sampleInstruments_[instrumentID].effects = fx; break;
        case VoiceType::SYNTH:  ctx->voicePlayer.synthInstruments_[instrumentID].effects = fx; break;
        case VoiceType::FM:     ctx->voicePlayer.fmInstruments_[instrumentID].effects = fx; break;
        default: break;
    }
}

// --- Effects ---

EXPORT void ZT_SetChannelFilter(void* ptr, int channel, int mode, float cutoff, float resonance) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->mixBus.SetChannelFilter(channel, mode, cutoff, resonance);
}

EXPORT void ZT_SetChannelDelaySend(void* ptr, int channel, float send) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->mixBus.SetChannelDelaySend(channel, send);
}

EXPORT void ZT_SetChannelReverbSend(void* ptr, int channel, float send) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->mixBus.SetChannelReverbSend(channel, send);
}

EXPORT void ZT_SetDelayParams(void* ptr, float time, float feedback, float wet) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->mixBus.SetDelayParams(time, feedback, wet);
    ctx->masterDelay.SetParams(time, feedback, wet, true);
    ctx->masterDelayEnabled = (wet > 0.001f);
}

EXPORT void ZT_SetReverbParams(void* ptr, float roomSize, float damp, float wet) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->mixBus.SetReverbParams(roomSize, damp, wet);
    ctx->masterReverb.SetParams(roomSize, damp, wet, 1.0f);
    ctx->masterReverbEnabled = (wet > 0.001f);
}

EXPORT void ZT_SetMasterFilter(void* ptr, int mode, float cutoff, float resonance) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->masterFilter.SetParams(cutoff, resonance, mode);
    ctx->masterFilterEnabled = (cutoff < 0.999f || mode != 0);
}

// --- Voice Parameter Modifiers ---

static const char* MOD_MACRO_0 = "macro:0";
static const char* MOD_MACRO_1 = "macro:1";
static const char* MOD_MACRO_2 = "macro:2";
static const char* MOD_MACRO_3 = "macro:3";
static const char* MACRO_IDS[] = { MOD_MACRO_0, MOD_MACRO_1, MOD_MACRO_2, MOD_MACRO_3 };

EXPORT void ZT_SetVoicePitchMod(void* ptr, int voiceID, int macroIndex, double ratio) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return;
    if (macroIndex < 0 || macroIndex >= 4) return;
    ctx->voicePlayer.voices_[voiceID].pitchMods.Set(MACRO_IDS[macroIndex], ratio);
}

EXPORT void ZT_SetVoiceVolumeMod(void* ptr, int voiceID, int macroIndex, double ratio) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (voiceID < 0 || voiceID >= MAX_VOICES) return;
    if (macroIndex < 0 || macroIndex >= 4) return;
    ctx->voicePlayer.voices_[voiceID].volumeMods.Set(MACRO_IDS[macroIndex], ratio);
}

// --- Macros ---

EXPORT int ZT_GetChannelVoiceID(void* ptr, int channel) {
    if (!ptr) return -1;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (channel < 0 || channel >= MAX_CHANNELS) return -1;
    return ctx->sequencer.GetChannelState(channel).activeVoiceID;
}

EXPORT float ZT_GetChannelMacro(void* ptr, int channel, int macroIndex) {
    if (!ptr) return 0;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (channel < 0 || channel >= MAX_CHANNELS) return 0;
    if (macroIndex < 0 || macroIndex >= ChannelState::MAX_MACROS) return 0;
    return ctx->sequencer.GetChannelState(channel).macroValue[macroIndex];
}

EXPORT int ZT_GetChannelPreset(void* ptr, int channel) {
    if (!ptr) return -1;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (channel < 0 || channel >= MAX_CHANNELS) return -1;
    return ctx->sequencer.GetChannelState(channel).activePreset;
}

EXPORT int ZT_GetChannelInstrument(void* ptr, int channel) {
    if (!ptr) return -1;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    if (channel < 0 || channel >= MAX_CHANNELS) return -1;
    return ctx->sequencer.GetChannelState(channel).lastInstrument;
}

EXPORT void ZT_SetPresetMap(void* ptr, int userInstrumentIndex, int baseSlot, int variantCount) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->sequencer.SetPresetMap(userInstrumentIndex, baseSlot, variantCount);
}

// --- Events ---

EXPORT int ZT_PollEvent(void* ptr, ZTrackerEvent* outEvent) {
    if (!ptr || !outEvent) return 0;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    return ctx->eventQueue.Pop(*outEvent) ? 1 : 0;
}

EXPORT void ZT_FlushEvents(void* ptr) {
    if (!ptr) return;
    ZTrackerContext* ctx = (ZTrackerContext*)ptr;
    ctx->eventQueue.Flush();
}

} // extern "C"
