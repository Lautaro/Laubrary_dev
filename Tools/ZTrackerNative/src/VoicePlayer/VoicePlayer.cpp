#include "VoicePlayer.h"
#include <algorithm>
#include <cstdlib>

void VoicePlayer::InitVoiceFilter(Voice& v, const InstrumentEffects& fx) {
    v.voiceFilterEnabled = fx.filterEnabled;
    v.voiceDelaySend = fx.delaySend;
    v.voiceReverbSend = fx.reverbSend;
    v.vfX1L = v.vfX2L = v.vfY1L = v.vfY2L = 0;
    v.vfX1R = v.vfX2R = v.vfY1R = v.vfY2R = 0;

    if (fx.filterEnabled) {
        float freq = fx.filterCutoff * (outputRate_ * 0.5f);
        freq = std::max(20.0f, std::min(freq, outputRate_ * 0.5f - 100.0f));
        float w0 = 2.0f * (float)M_PI * freq / outputRate_;
        float sinW0 = sinf(w0);
        float cosW0 = cosf(w0);
        float alpha = sinW0 / (2.0f * fx.filterResonance);
        float a0;

        switch (fx.filterMode) {
            case 0: // LP
                v.vfB0 = (1.0f - cosW0) * 0.5f;
                v.vfB1 = 1.0f - cosW0;
                v.vfB2 = (1.0f - cosW0) * 0.5f;
                a0 = 1.0f + alpha;
                v.vfA1 = -2.0f * cosW0;
                v.vfA2 = 1.0f - alpha;
                break;
            case 1: // HP
                v.vfB0 = (1.0f + cosW0) * 0.5f;
                v.vfB1 = -(1.0f + cosW0);
                v.vfB2 = (1.0f + cosW0) * 0.5f;
                a0 = 1.0f + alpha;
                v.vfA1 = -2.0f * cosW0;
                v.vfA2 = 1.0f - alpha;
                break;
            case 2: // BP
                v.vfB0 = alpha;
                v.vfB1 = 0.0f;
                v.vfB2 = -alpha;
                a0 = 1.0f + alpha;
                v.vfA1 = -2.0f * cosW0;
                v.vfA2 = 1.0f - alpha;
                break;
            default: a0 = 1.0f; break;
        }
        float invA0 = 1.0f / a0;
        v.vfB0 *= invA0; v.vfB1 *= invA0; v.vfB2 *= invA0;
        v.vfA1 *= invA0; v.vfA2 *= invA0;
    }
}

void VoicePlayer::ProcessVoiceFilter(Voice& v, float& sL, float& sR) {
    if (!v.voiceFilterEnabled) return;
    float outL = v.vfB0 * sL + v.vfB1 * v.vfX1L + v.vfB2 * v.vfX2L - v.vfA1 * v.vfY1L - v.vfA2 * v.vfY2L;
    v.vfX2L = v.vfX1L; v.vfX1L = sL; v.vfY2L = v.vfY1L; v.vfY1L = outL;
    sL = outL;

    float outR = v.vfB0 * sR + v.vfB1 * v.vfX1R + v.vfB2 * v.vfX2R - v.vfA1 * v.vfY1R - v.vfA2 * v.vfY2R;
    v.vfX2R = v.vfX1R; v.vfX1R = sR; v.vfY2R = v.vfY1R; v.vfY1R = outR;
    sR = outR;
}

void VoicePlayer::AccumulateSends(Voice& v, float sL, float sR, int i) {
    if (i >= MAX_SEND_BUFFER) return;
    if (v.voiceDelaySend > 0.0f) {
        sendDelayL_[i] += sL * v.voiceDelaySend;
        sendDelayR_[i] += sR * v.voiceDelaySend;
        hasSends_ = true;
    }
    if (v.voiceReverbSend > 0.0f) {
        sendReverbL_[i] += sL * v.voiceReverbSend;
        sendReverbR_[i] += sR * v.voiceReverbSend;
        hasSends_ = true;
    }
}

static const char* MOD_INST_PORTAMENTO = "inst:portamento";
static const char* MOD_INST_ARPEGGIO   = "inst:arpeggio";
static const char* MOD_INST_VIBRATO    = "inst:vibrato";

void VoicePlayer::Init(uint32_t sampleRate) {
    outputRate_ = sampleRate;
    sampleCount_ = 0;

    for (int i = 0; i < MAX_VOICES; i++) {
        voices_[i] = Voice{};
        voices_[i].voiceID = i;
    }
    for (int i = 0; i < MAX_SAMPLES; i++)
        samples_[i] = Sample{};
    for (int i = 0; i < MAX_INSTRUMENTS; i++) {
        sampleInstruments_[i] = SampleInstrumentData{};
        synthInstruments_[i] = SynthInstrumentData{};
        instrumentTypes_[i] = VoiceType::SAMPLE;
    }
    for (int i = 0; i < MAX_CHANNELS; i++) {
        lastChannelIncrement_[i] = 0.0;
        lastChannelNote_[i] = -1;
    }
}

// --- Sample Bank ---

int VoicePlayer::LoadSample(float* dataL, float* dataR,
                            int frameCount, int sampleRate, int channels) {
    if (sampleCount_ >= MAX_SAMPLES) return -1;

    int id = sampleCount_++;
    Sample& smp = samples_[id];

    smp.data = new float[frameCount];
    memcpy(smp.data, dataL, frameCount * sizeof(float));

    if (channels >= 2 && dataR != nullptr) {
        smp.dataR = new float[frameCount];
        memcpy(smp.dataR, dataR, frameCount * sizeof(float));
        smp.channels = 2;
    } else {
        smp.dataR = nullptr;
        smp.channels = 1;
    }

    smp.frameCount = frameCount;
    smp.sampleRate = sampleRate;
    smp.loopEnabled = false;
    smp.loopMode = LoopMode::NONE;

    return id;
}

void VoicePlayer::SetSampleLoop(int sampleID, int loopMode,
                                int loopStart, int loopEnd) {
    if (sampleID < 0 || sampleID >= sampleCount_) return;
    Sample& smp = samples_[sampleID];
    smp.loopMode = (LoopMode)loopMode;
    smp.loopEnabled = (loopMode != (int)LoopMode::NONE);
    smp.loopStart = loopStart;
    smp.loopEnd = loopEnd;
}

void VoicePlayer::SetSampleSustainLoop(int sampleID, int start, int end) {
    if (sampleID < 0 || sampleID >= sampleCount_) return;
    samples_[sampleID].sustainLoopStart = start;
    samples_[sampleID].sustainLoopEnd = end;
}

// --- Instruments ---

void VoicePlayer::SetSampleInstrument(int id, const SampleInstrumentData& data) {
    if (id < 0 || id >= MAX_INSTRUMENTS) return;
    sampleInstruments_[id] = data;
    instrumentTypes_[id] = VoiceType::SAMPLE;
}

void VoicePlayer::SetSynthInstrument(int id, const SynthInstrumentData& data) {
    if (id < 0 || id >= MAX_INSTRUMENTS) return;
    synthInstruments_[id] = data;
    instrumentTypes_[id] = VoiceType::SYNTH;
}

void VoicePlayer::SetFMInstrument(int id, const FMInstrumentData& data) {
    if (id < 0 || id >= MAX_INSTRUMENTS) return;
    fmInstruments_[id] = data;
    instrumentTypes_[id] = VoiceType::FM;
}

void VoicePlayer::SetKitInstrument(int id) {
    if (id < 0 || id >= MAX_INSTRUMENTS) return;
    kitInstruments_[id] = KitInstrumentData{};
    instrumentTypes_[id] = VoiceType::KIT;
}

void VoicePlayer::SetKitEntry(int id, int midiNote, const KitEntry& entry) {
    if (id < 0 || id >= MAX_INSTRUMENTS) return;
    if (midiNote < 0 || midiNote >= MAX_KIT_ENTRIES) return;
    kitInstruments_[id].entries[midiNote] = entry;
}

// --- Voice Allocation ---

int VoicePlayer::AllocateVoice() {
    // Pass 1: find IDLE voice
    for (int i = 0; i < MAX_VOICES; i++) {
        if (!voices_[i].active && voices_[i].adsr.stage == ADSRStage::IDLE)
            return i;
    }

    // Pass 2: steal voice in RELEASE with lowest envelope level
    int bestIdx = -1;
    float bestLevel = 2.0f;
    for (int i = 0; i < MAX_VOICES; i++) {
        if (voices_[i].adsr.stage == ADSRStage::RELEASE &&
            voices_[i].adsr.level < bestLevel) {
            bestLevel = voices_[i].adsr.level;
            bestIdx = i;
        }
    }
    if (bestIdx >= 0) return bestIdx;

    // Pass 3: steal quietest active voice
    bestIdx = 0;
    bestLevel = 2.0f;
    for (int i = 0; i < MAX_VOICES; i++) {
        float eff = voices_[i].adsr.level * voices_[i].volume * voices_[i].velocity;
        if (eff < bestLevel) {
            bestLevel = eff;
            bestIdx = i;
        }
    }
    return bestIdx;
}

// --- ADSR ---

void VoicePlayer::InitADSR(Voice& v, const ADSRParams& p) {
    v.adsr.stage = ADSRStage::ATTACK;
    v.adsr.level = 0.0f;
    v.adsr.sustainLevel = p.sustain;
    v.adsr.releaseRequested = false;

    float rate = (float)outputRate_;
    v.adsr.attackRate  = (p.attack  > 0.0f) ? 1.0f / (p.attack  * rate) : 1.0f;
    v.adsr.decayRate   = (p.decay   > 0.0f) ? (1.0f - p.sustain) / (p.decay * rate) : 1.0f;
    v.adsr.releaseRate = (p.release > 0.0f) ? 1.0f / (p.release * rate) : 1.0f;
}

void VoicePlayer::ProcessADSR(Voice& v) {
    switch (v.adsr.stage) {
        case ADSRStage::ATTACK:
            v.adsr.level += v.adsr.attackRate;
            if (v.adsr.level >= 1.0f) {
                v.adsr.level = 1.0f;
                v.adsr.stage = ADSRStage::DECAY;
            }
            break;
        case ADSRStage::DECAY:
            v.adsr.level -= v.adsr.decayRate;
            if (v.adsr.level <= v.adsr.sustainLevel) {
                v.adsr.level = v.adsr.sustainLevel;
                v.adsr.stage = ADSRStage::SUSTAIN;
            }
            break;
        case ADSRStage::SUSTAIN:
            break;
        case ADSRStage::RELEASE:
            v.adsr.level -= v.adsr.releaseRate;
            if (v.adsr.level <= 0.0f) {
                v.adsr.level = 0.0f;
                v.adsr.stage = ADSRStage::IDLE;
                v.active = false;
            }
            break;
        case ADSRStage::IDLE:
            break;
    }
}

void VoicePlayer::ProcessBlendADSR(Voice& v) {
    // Same state machine as amplitude ADSR but drives blendLevel
    switch (v.blendAdsr.stage) {
        case ADSRStage::ATTACK:
            v.blendAdsr.level += v.blendAdsr.attackRate;
            if (v.blendAdsr.level >= 1.0f) {
                v.blendAdsr.level = 1.0f;
                v.blendAdsr.stage = ADSRStage::DECAY;
            }
            break;
        case ADSRStage::DECAY:
            v.blendAdsr.level -= v.blendAdsr.decayRate;
            if (v.blendAdsr.level <= v.blendAdsr.sustainLevel) {
                v.blendAdsr.level = v.blendAdsr.sustainLevel;
                v.blendAdsr.stage = ADSRStage::SUSTAIN;
            }
            break;
        case ADSRStage::SUSTAIN:
            break;
        case ADSRStage::RELEASE:
            v.blendAdsr.level -= v.blendAdsr.releaseRate;
            if (v.blendAdsr.level <= 0.0f) {
                v.blendAdsr.level = 0.0f;
                v.blendAdsr.stage = ADSRStage::IDLE;
            }
            break;
        case ADSRStage::IDLE:
            break;
    }
    v.blendLevel = v.blendAdsr.level;
}

// --- Modulation ---

void VoicePlayer::InitModulation(Voice& v, const PortamentoParams& pp, const VibratoParams& vp,
                                  const ArpeggioParams& ap, int midiNote, double prevIncrement) {
    // Portamento
    v.portamento = PortamentoState{};
    if (pp.enabled && prevIncrement > 0.0) {
        v.portamento.active = true;
        v.portamento.currentIncrement = prevIncrement;
        v.portamento.targetIncrement = v.baseIncrement;
        v.portamento.glideRate = 1.0 - exp(-1.0 / (pp.glideTime * (double)outputRate_));
        // Start playing at previous pitch
        v.increment = prevIncrement;
    }

    // Vibrato
    v.vibrato = VibratoState{};
    if (vp.depth > 0.0f && vp.rate > 0.0f) {
        v.vibrato.depth = vp.depth;
        v.vibrato.phase = 0.0f;
        v.vibrato.fadeProgress = (vp.fadeIn > 0.0f) ? 0.0f : 1.0f;
        v.vibrato.fadeRate = (vp.fadeIn > 0.0f) ? 1.0f / (vp.fadeIn * (float)outputRate_) : 0.0f;
        // Apply randomness to rate
        float rndFactor = 0.0f;
        if (vp.randomness > 0.0f) {
            rndFactor = ((float)rand() / RAND_MAX * 2.0f - 1.0f) * vp.randomness;
        }
        v.vibrato.currentRate = vp.rate + rndFactor * vp.rate;
    }

    // Arpeggio
    v.arpeggio = ArpeggioState{};
    if (ap.enabled && ap.noteCount >= 2) {
        v.arpeggio.active = true;
        v.arpeggio.noteCount = std::min(ap.noteCount, MAX_ARPEGGIO_NOTES);
        v.arpeggio.currentIndex = 0;
        v.arpeggio.sampleCounter = 0.0;
        v.arpeggio.noteStartTime = 0.0f;
        v.arpeggio.timePerSample = 1.0f / (float)outputRate_;

        // Copy curve
        v.arpeggio.speedIsPerNote = ap.speedIsPerNote;
        v.arpeggio.curvePointCount = std::min(ap.curvePointCount, MAX_ARPEGGIO_CURVE);
        for (int i = 0; i < v.arpeggio.curvePointCount; i++) {
            v.arpeggio.curveTimes[i] = ap.curveTimes[i];
            v.arpeggio.curveValues[i] = ap.curveValues[i];
        }

        // Pre-calculate increments for each arpeggio note
        for (int i = 0; i < v.arpeggio.noteCount; i++) {
            int arpNote = midiNote + ap.notes[i];
            if (v.type == VoiceType::SYNTH) {
                v.arpeggio.increments[i] = CalcSynthIncrement(arpNote, 0.0f, outputRate_);
            } else {
                const SampleInstrumentData& si = sampleInstruments_[v.instrumentID];
                const Sample& smp = samples_[si.sampleID];
                v.arpeggio.increments[i] = CalcIncrement(arpNote, si.baseNote, si.fineTune,
                                                          smp.sampleRate, outputRate_);
            }
        }

        // Evaluate initial speed
        double speed = EvalArpeggioCurve(v.arpeggio);
        double perNoteTime = ap.speedIsPerNote ? speed : speed / v.arpeggio.noteCount;
        v.arpeggio.samplesPerStep = perNoteTime * outputRate_;

        // Set initial increment to first arpeggio note
        v.increment = v.arpeggio.increments[0];
        v.baseIncrement = v.arpeggio.increments[0]; // base for modulation
    }
}

double VoicePlayer::EvalArpeggioCurve(const ArpeggioState& arp) {
    if (arp.curvePointCount <= 0) return 0.05; // fallback 50ms
    if (arp.curvePointCount == 1) return arp.curveValues[0];

    float t = arp.noteStartTime;

    // Before first point
    if (t <= arp.curveTimes[0]) return arp.curveValues[0];
    // After last point
    if (t >= arp.curveTimes[arp.curvePointCount - 1])
        return arp.curveValues[arp.curvePointCount - 1];

    // Piecewise linear interpolation
    for (int i = 0; i < arp.curvePointCount - 1; i++) {
        if (t >= arp.curveTimes[i] && t < arp.curveTimes[i + 1]) {
            float range = arp.curveTimes[i + 1] - arp.curveTimes[i];
            float frac = (range > 0.0f) ? (t - arp.curveTimes[i]) / range : 0.0f;
            return arp.curveValues[i] + frac * (arp.curveValues[i + 1] - arp.curveValues[i]);
        }
    }
    return arp.curveValues[arp.curvePointCount - 1];
}

void VoicePlayer::ProcessModulation(Voice& v) {
    // Instrument portamento (per-voice, from NoteOn)
    if (v.portamento.active) {
        double diff = v.portamento.targetIncrement - v.portamento.currentIncrement;
        double step = diff * v.portamento.glideRate;
        v.portamento.currentIncrement += step;
        if (fabs(diff) < 0.0001) {
            v.portamento.active = false;
            v.portamento.currentIncrement = v.portamento.targetIncrement;
        }
        v.pitchMods.Set(MOD_INST_PORTAMENTO, v.portamento.currentIncrement / v.baseIncrement);
    }

    // Arpeggio (overrides pitch — sets ratio from base)
    if (v.arpeggio.active) {
        v.arpeggio.noteStartTime += v.arpeggio.timePerSample;
        v.arpeggio.sampleCounter += 1.0;

        if (v.arpeggio.sampleCounter >= v.arpeggio.samplesPerStep) {
            v.arpeggio.sampleCounter -= v.arpeggio.samplesPerStep;
            v.arpeggio.currentIndex = (v.arpeggio.currentIndex + 1) % v.arpeggio.noteCount;

            double speed = EvalArpeggioCurve(v.arpeggio);
            double perNoteTime = v.arpeggio.speedIsPerNote ? speed : speed / v.arpeggio.noteCount;
            v.arpeggio.samplesPerStep = perNoteTime * outputRate_;
        }
        v.pitchMods.Set(MOD_INST_ARPEGGIO, v.arpeggio.increments[v.arpeggio.currentIndex] / v.baseIncrement);
    }

    // Vibrato
    if (v.vibrato.depth > 0.0f && v.vibrato.currentRate > 0.0f) {
        if (v.vibrato.fadeProgress < 1.0f) {
            v.vibrato.fadeProgress += v.vibrato.fadeRate;
            if (v.vibrato.fadeProgress > 1.0f) v.vibrato.fadeProgress = 1.0f;
        }
        v.vibrato.phase += (v.vibrato.currentRate / (float)outputRate_) * 2.0f * (float)M_PI;
        if (v.vibrato.phase > 2.0f * (float)M_PI)
            v.vibrato.phase -= 2.0f * (float)M_PI;

        float vibCents = sinf(v.vibrato.phase) * v.vibrato.depth * v.vibrato.fadeProgress;
        float vibMul = powf(2.0f, vibCents / 1200.0f);
        v.pitchMods.Set(MOD_INST_VIBRATO, (double)vibMul);
    }

    // Apply all pitch modifiers — linearly interpolated across tick interval
    v.increment = v.baseIncrement * v.pitchMods.EvaluateSmoothed(v.samplesPerTick);
}

// --- Voice Control ---

int VoicePlayer::NoteOn(int instrumentID, int midiNote, float velocity, float channelVolume, float channelPan) {
    noteChannelVolume_ = channelVolume;
    noteChannelPan_ = channelPan;
    if (instrumentID < 0 || instrumentID >= MAX_INSTRUMENTS) return -1;

    if (instrumentTypes_[instrumentID] == VoiceType::SYNTH) {
        return SynthNoteOn(instrumentID, midiNote, velocity);
    }

    if (instrumentTypes_[instrumentID] == VoiceType::FM) {
        const FMInstrumentData& inst = fmInstruments_[instrumentID];
        int idx = AllocateVoice();
        Voice& v = voices_[idx];
        v.active = true;
        v.type = VoiceType::FM;
        v.instrumentID = instrumentID;
        v.channelID = -1;
        v.midiNote = midiNote;
        v.goingForward = true;
        v.unisonGroup = -1;
        v.unisonCount = 0;
        v.pitchMods.ClearAll();
        v.volumeMods.ClearAll();
        v.channelVolume = noteChannelVolume_;
        v.channelPan = noteChannelPan_;
        v.envState.time = 0.0f;
        v.envState.timePerSample = 1.0f / (float)outputRate_;

        v.baseIncrement = CalcSynthIncrement(midiNote, 0.0f, outputRate_);
        v.increment = v.baseIncrement;
        v.volume = inst.volume;
        v.pan = inst.pan;
        v.velocity = velocity;

        // Init main ADSR from operator 0 (carrier)
        ADSRParams p;
        p.attack  = inst.operators[0].attack;
        p.decay   = inst.operators[0].decay;
        p.sustain = inst.operators[0].sustain;
        p.release = inst.operators[0].release;
        InitADSR(v, p);

        // Init FM state
        for (int op = 0; op < FM_NUM_OPERATORS; op++) {
            v.fmState.phase[op] = 0.0f;
            // Init per-operator ADSR
            v.fmState.adsr[op].stage = ADSRStage::ATTACK;
            v.fmState.adsr[op].level = 0.0f;
            v.fmState.adsr[op].sustainLevel = inst.operators[op].sustain;
            float rate = (float)outputRate_;
            v.fmState.adsr[op].attackRate  = (inst.operators[op].attack  > 0.0f) ? 1.0f / (inst.operators[op].attack  * rate) : 1.0f;
            v.fmState.adsr[op].decayRate   = (inst.operators[op].decay   > 0.0f) ? (1.0f - inst.operators[op].sustain) / (inst.operators[op].decay * rate) : 1.0f;
            v.fmState.adsr[op].releaseRate = (inst.operators[op].release > 0.0f) ? 1.0f / (inst.operators[op].release * rate) : 1.0f;
        }
        v.fmState.prevOutput = 0.0f;

        // Modulation
        v.portamento = PortamentoState{};
        v.vibrato = VibratoState{};
        v.arpeggio = ArpeggioState{};
        InitVoiceFilter(v, inst.effects);

        return idx;
    }

    if (instrumentTypes_[instrumentID] == VoiceType::KIT) {
        // Look up the kit entry for this note
        if (midiNote < 0 || midiNote >= MAX_KIT_ENTRIES) return -1;
        const KitEntry& ke = kitInstruments_[instrumentID].entries[midiNote];
        if (ke.sampleID < 0 || ke.sampleID >= sampleCount_) return -1;
        const Sample& smp = samples_[ke.sampleID];

        int idx = AllocateVoice();
        Voice& v = voices_[idx];
        v.active = true;
        v.type = VoiceType::KIT;
        v.instrumentID = instrumentID;
        v.channelID = -1;
        v.midiNote = midiNote;
        v.position = 0.0;
        v.goingForward = true;
        v.unisonGroup = -1;
        v.unisonCount = 0;
        v.pitchMods.ClearAll();
        v.volumeMods.ClearAll();
        v.channelVolume = noteChannelVolume_;
        v.channelPan = noteChannelPan_;
        v.envState.time = 0.0f;
        v.envState.timePerSample = 1.0f / (float)outputRate_;

        // Kit entries always play at base pitch (no pitch shifting by note)
        v.baseIncrement = CalcIncrement(ke.baseNote, ke.baseNote, 0.0f, smp.sampleRate, outputRate_);
        v.increment = v.baseIncrement;

        v.volume = ke.volume * kitInstruments_[instrumentID].masterVolume;
        v.pan = ke.pan;
        v.velocity = velocity;

        ADSRParams p;
        p.attack  = ke.attack;
        p.decay   = ke.decay;
        p.sustain = ke.sustain;
        p.release = ke.release;
        InitADSR(v, p);

        // No modulation for kit entries
        v.portamento = PortamentoState{};
        v.vibrato = VibratoState{};
        v.arpeggio = ArpeggioState{};

        return idx;
    }

    const SampleInstrumentData& inst = sampleInstruments_[instrumentID];
    if (inst.sampleID < 0 || inst.sampleID >= sampleCount_) return -1;
    const Sample& smp = samples_[inst.sampleID];

    int idx = AllocateVoice();
    Voice& v = voices_[idx];

    v.active = true;
    v.type = VoiceType::SAMPLE;
    v.instrumentID = instrumentID;
    v.channelID = -1;
    v.midiNote = midiNote;
    v.position = 0.0;
    v.positionB = 0.0;
    v.goingForward = true;
    v.unisonGroup = -1;
    v.unisonCount = 0;
    v.pitchMods.ClearAll();
    v.volumeMods.ClearAll();
    v.channelVolume = noteChannelVolume_;
    v.channelPan = noteChannelPan_;
    v.envState.time = 0.0f;
    v.envState.timePerSample = 1.0f / (float)outputRate_;

    v.baseIncrement = CalcIncrement(midiNote, inst.baseNote, inst.fineTune,
                                    smp.sampleRate, outputRate_);
    v.increment = v.baseIncrement;

    // Sample-B increment: derive from B's own baseNote/fineTune/sampleRate so
    // pitch tracking is independent of A. Disabled (incrementB = 0) when B's
    // sample slot is invalid.
    v.incrementB = 0.0;
    if (inst.sampleIDB >= 0 && inst.sampleIDB < sampleCount_) {
        const Sample& smpB = samples_[inst.sampleIDB];
        if (smpB.data != nullptr && smpB.frameCount > 0) {
            v.incrementB = CalcIncrement(midiNote, inst.baseNoteB, inst.fineTuneB,
                                         smpB.sampleRate, outputRate_);
        }
    }

    // Blend state (mirrors SynthNoteOn). PM depth lives on the voice so the
    // PM_DEPTH envelope can drive it per-sample even for sample voices.
    v.blendLevel = inst.blendDefault;
    v.blendTarget = inst.blendDefault;
    v.pmDepthCur = inst.pmDepth;
    v.pmDepthTarget = inst.pmDepth;
    if (inst.blendEnvelopeEnabled) {
        v.blendAdsr.stage = ADSRStage::ATTACK;
        v.blendAdsr.level = 0.0f;
        v.blendAdsr.sustainLevel = inst.blendEnvelope.sustain;
        float rate = (float)outputRate_;
        v.blendAdsr.attackRate  = (inst.blendEnvelope.attack  > 0.0f) ? 1.0f / (inst.blendEnvelope.attack  * rate) : 1.0f;
        v.blendAdsr.decayRate   = (inst.blendEnvelope.decay   > 0.0f) ? (1.0f - inst.blendEnvelope.sustain) / (inst.blendEnvelope.decay * rate) : 1.0f;
        v.blendAdsr.releaseRate = (inst.blendEnvelope.release > 0.0f) ? 1.0f / (inst.blendEnvelope.release * rate) : 1.0f;
    } else {
        v.blendAdsr.stage = ADSRStage::IDLE;
    }

    v.volume = inst.volume;
    v.pan = inst.pan;
    v.velocity = velocity;

    ADSRParams p;
    p.attack  = inst.attack;
    p.decay   = inst.decay;
    p.sustain = inst.sustain;
    p.release = inst.release;
    InitADSR(v, p);

    // Modulation — use channel's last increment for portamento
    double prevInc = 0.0;
    if (inst.portamento.enabled && v.channelID >= 0 && v.channelID < MAX_CHANNELS) {
        prevInc = lastChannelIncrement_[v.channelID];
    }
    InitModulation(v, inst.portamento, inst.vibrato, inst.arpeggio, midiNote, prevInc);
    InitVoiceFilter(v, inst.effects);

    // Track for future portamento
    if (v.channelID >= 0 && v.channelID < MAX_CHANNELS) {
        lastChannelIncrement_[v.channelID] = v.baseIncrement;
        lastChannelNote_[v.channelID] = midiNote;
    }

    return idx;
}

int VoicePlayer::SynthNoteOn(int instrumentID, int midiNote, float velocity) {
    const SynthInstrumentData& inst = synthInstruments_[instrumentID];
    int voiceCount = std::max(1, std::min(inst.unisonVoices, MAX_UNISON));

    int firstIdx = -1;

    for (int u = 0; u < voiceCount; u++) {
        int idx = AllocateVoice();
        Voice& v = voices_[idx];

        v.active = true;
        v.type = VoiceType::SYNTH;
        v.instrumentID = instrumentID;
        v.channelID = -1;
        v.midiNote = midiNote;
        v.synthPhase = 0.0f;
        v.synthPhaseB = 0.0f;
        v.goingForward = true;
        v.pitchMods.ClearAll();
        v.volumeMods.ClearAll();
        v.channelVolume = noteChannelVolume_;
        v.channelPan = noteChannelPan_;
        v.envState.time = 0.0f;
        v.envState.timePerSample = 1.0f / (float)outputRate_;

        // Detune and pan spread for unison
        float detuneCents = 0.0f;
        float panOffset = 0.0f;
        if (voiceCount > 1) {
            // Spread evenly: -1..+1 mapped to detune and pan
            float t = (float)u / (float)(voiceCount - 1);  // 0..1
            float spread = t * 2.0f - 1.0f;                // -1..+1
            detuneCents = spread * inst.unisonDetune;
            panOffset = spread * inst.unisonSpread;
        }

        float totalFineTune = detuneCents;
        v.baseIncrement = CalcSynthIncrement(midiNote, totalFineTune, outputRate_);
        v.increment = v.baseIncrement;

        // Gain-compensate unison: each voice at 1/sqrt(N)
        float unisonGain = (voiceCount > 1) ? 1.0f / sqrtf((float)voiceCount) : 1.0f;
        v.volume = inst.volume * unisonGain;
        v.pan = std::max(-1.0f, std::min(1.0f, inst.pan + panOffset));
        v.velocity = velocity;

        // Amplitude ADSR
        ADSRParams p;
        p.attack  = inst.attack;
        p.decay   = inst.decay;
        p.sustain = inst.sustain;
        p.release = inst.release;
        InitADSR(v, p);

        // Modulation (only for first voice in unison or single)
        double prevInc = 0.0;
        if (inst.portamento.enabled && v.channelID >= 0 && v.channelID < MAX_CHANNELS) {
            prevInc = lastChannelIncrement_[v.channelID];
        }
        InitModulation(v, inst.portamento, inst.vibrato, inst.arpeggio, midiNote, prevInc);
        InitVoiceFilter(v, inst.effects);

        // Blend envelope
        v.blendLevel = inst.blendDefault;
        v.blendTarget = inst.blendDefault;
        v.pmDepthCur = inst.pmDepth;
        v.pmDepthTarget = inst.pmDepth;
        v.waveBRatioCur = inst.waveBRatio;
        v.waveBRatioTarget = inst.waveBRatio;
        v.pulseWidthCur = inst.pulseWidth;
        v.pulseWidthTarget = inst.pulseWidth;
        if (inst.blendEnvelopeEnabled) {
            v.blendAdsr.stage = ADSRStage::ATTACK;
            v.blendAdsr.level = 0.0f;
            v.blendAdsr.sustainLevel = inst.blendEnvelope.sustain;
            float rate = (float)outputRate_;
            v.blendAdsr.attackRate  = (inst.blendEnvelope.attack  > 0.0f) ? 1.0f / (inst.blendEnvelope.attack  * rate) : 1.0f;
            v.blendAdsr.decayRate   = (inst.blendEnvelope.decay   > 0.0f) ? (1.0f - inst.blendEnvelope.sustain) / (inst.blendEnvelope.decay * rate) : 1.0f;
            v.blendAdsr.releaseRate = (inst.blendEnvelope.release > 0.0f) ? 1.0f / (inst.blendEnvelope.release * rate) : 1.0f;
        } else {
            v.blendAdsr.stage = ADSRStage::IDLE;
        }

        // Unison group tracking
        if (u == 0) {
            firstIdx = idx;
            v.unisonGroup = idx;
            v.unisonCount = voiceCount;
        } else {
            v.unisonGroup = firstIdx;
            v.unisonCount = 0;
        }
    }

    // Track for future portamento
    if (firstIdx >= 0) {
        Voice& leader = voices_[firstIdx];
        if (leader.channelID >= 0 && leader.channelID < MAX_CHANNELS) {
            lastChannelIncrement_[leader.channelID] = leader.baseIncrement;
            lastChannelNote_[leader.channelID] = midiNote;
        }
    }

    return firstIdx;
}

void VoicePlayer::ReleaseVoice(Voice& v) {
    if (v.adsr.stage == ADSRStage::RELEASE || v.adsr.stage == ADSRStage::IDLE) return;

    float releaseTime = 0.3f;
    if (v.type == VoiceType::SYNTH)
        releaseTime = synthInstruments_[v.instrumentID].release;
    else if (v.type == VoiceType::FM)
        releaseTime = fmInstruments_[v.instrumentID].operators[0].release;
    else if (v.type == VoiceType::KIT && v.midiNote >= 0 && v.midiNote < MAX_KIT_ENTRIES)
        releaseTime = kitInstruments_[v.instrumentID].entries[v.midiNote].release;
    else
        releaseTime = sampleInstruments_[v.instrumentID].release;

    // Simple: fade from current level to 0 over release time
    v.adsr.releaseRate = (v.adsr.level > 0.0f && releaseTime > 0.0f)
        ? v.adsr.level / (releaseTime * (float)outputRate_)
        : 1.0f;
    v.adsr.stage = ADSRStage::RELEASE;
    v.adsr.releaseRequested = false;

    if (v.blendAdsr.stage != ADSRStage::IDLE &&
        v.blendAdsr.stage != ADSRStage::RELEASE) {
        v.blendAdsr.stage = ADSRStage::RELEASE;
    }

    // Release FM operator envelopes
    if (v.type == VoiceType::FM) {
        for (int op = 0; op < FM_NUM_OPERATORS; op++) {
            auto& a = v.fmState.adsr[op];
            if (a.stage != ADSRStage::RELEASE && a.stage != ADSRStage::IDLE) {
                a.releaseRate = (a.level > 0.0f && releaseTime > 0.0f)
                    ? a.level / (releaseTime * (float)outputRate_)
                    : 1.0f;
                a.stage = ADSRStage::RELEASE;
            }
        }
    }
}

void VoicePlayer::NoteOff(int voiceID) {
    if (voiceID < 0 || voiceID >= MAX_VOICES) return;
    Voice& v = voices_[voiceID];
    if (!v.active) return;

    // If this is a unison group leader, release all voices in the group
    if (v.unisonGroup == voiceID && v.unisonCount > 1) {
        for (int i = 0; i < MAX_VOICES; i++) {
            if (voices_[i].active && voices_[i].unisonGroup == voiceID) {
                ReleaseVoice(voices_[i]);
            }
        }
        return;
    }

    ReleaseVoice(v);
}

void VoicePlayer::AllNotesOff() {
    for (int i = 0; i < MAX_VOICES; i++) {
        voices_[i].active = false;
        voices_[i].adsr.stage = ADSRStage::IDLE;
        voices_[i].adsr.level = 0.0f;
        voices_[i].unisonGroup = -1;
        voices_[i].unisonCount = 0;
    }
}

// --- Loop Handling ---

void VoicePlayer::HandleLoop(Voice& v, const Sample& smp) {
    if (!smp.loopEnabled) {
        if (v.position >= smp.frameCount) {
            v.active = false;
            v.adsr.stage = ADSRStage::IDLE;
        }
        return;
    }

    uint32_t loopEnd   = smp.loopEnd;
    uint32_t loopStart = smp.loopStart;

    if (smp.loopMode == LoopMode::SUSTAIN_LOOP &&
        v.adsr.stage != ADSRStage::RELEASE) {
        if (smp.sustainLoopEnd > smp.sustainLoopStart) {
            loopEnd   = smp.sustainLoopEnd;
            loopStart = smp.sustainLoopStart;
        }
    }

    if (loopEnd <= loopStart || loopEnd > smp.frameCount) {
        if (v.position >= smp.frameCount) {
            v.active = false;
            v.adsr.stage = ADSRStage::IDLE;
        }
        return;
    }

    switch (smp.loopMode) {
        case LoopMode::FORWARD:
        case LoopMode::SUSTAIN_LOOP:
            if (v.position >= loopEnd) {
                v.position = loopStart + fmod(v.position - loopStart,
                                              (double)(loopEnd - loopStart));
            }
            break;

        case LoopMode::PING_PONG:
            if (v.goingForward && v.position >= loopEnd) {
                v.position = loopEnd - (v.position - loopEnd);
                v.goingForward = false;
            } else if (!v.goingForward && v.position <= loopStart) {
                v.position = loopStart + (loopStart - v.position);
                v.goingForward = true;
            }
            break;

        case LoopMode::NONE:
            break;
    }

    if (smp.loopMode == LoopMode::SUSTAIN_LOOP &&
        v.adsr.stage == ADSRStage::RELEASE) {
        if (v.position >= smp.frameCount) {
            v.active = false;
            v.adsr.stage = ADSRStage::IDLE;
        }
    }
}

// --- Render ---

void VoicePlayer::RenderSampleVoice(Voice& v, float* outL, float* outR, int numFrames) {
    const SampleInstrumentData& inst = sampleInstruments_[v.instrumentID];
    if (inst.sampleID < 0 || inst.sampleID >= sampleCount_) return;
    const Sample& smp = samples_[inst.sampleID];
    if (smp.data == nullptr || smp.frameCount == 0) return;

    // Resolve sample B once per buffer. nullptr = blend disabled; render
    // collapses to the sample-A-only path with no per-frame branching cost
    // for instruments that don't use blend.
    const Sample* smpB = nullptr;
    if (inst.sampleIDB >= 0 && inst.sampleIDB < sampleCount_) {
        const Sample& cand = samples_[inst.sampleIDB];
        if (cand.data != nullptr && cand.frameCount > 0)
            smpB = &cand;
    }
    const bool blendActive = (smpB != nullptr);

    float panR = (v.pan + 1.0f) * 0.5f;
    panR = v.channelPan < 0 ? panR * (1 + v.channelPan) : panR + (1 - panR) * v.channelPan;
    float panL = 1.0f - panR;
    float gainL = sqrtf(panL);
    float gainR = sqrtf(panR);

    const float smoothRate = 1.0f / (0.005f * (float)outputRate_); // ~5ms toward target

    for (int i = 0; i < numFrames; i++) {
        if (!v.active) break;

        ProcessADSR(v);
        if (!v.active) break;
        ProcessModulation(v);

        // Blend ADSR (writes blendLevel directly when enabled — same semantics
        // as the synth path).
        if (blendActive && inst.blendEnvelopeEnabled && v.blendAdsr.stage != ADSRStage::IDLE) {
            ProcessBlendADSR(v);
        }

        // Per-sample envelope evaluation. BLEND drives blendTarget; PM_DEPTH
        // drives pmDepthTarget. Smoothing happens below so envelope values
        // don't introduce zipper noise.
        if (blendActive) {
            float et = v.envState.time;
            const auto& envs = inst.envelopes;
            if (envs.envs[(int)EnvParamID::BLEND].enabled)
                v.blendTarget = envs.envs[(int)EnvParamID::BLEND].Evaluate(et);
            if (envs.envs[(int)EnvParamID::PM_DEPTH].enabled)
                v.pmDepthTarget = envs.envs[(int)EnvParamID::PM_DEPTH].Evaluate(et);
            v.blendLevel  += (v.blendTarget  - v.blendLevel)  * smoothRate;
            v.pmDepthCur  += (v.pmDepthTarget - v.pmDepthCur) * smoothRate;
        }
        v.envState.time += v.envState.timePerSample;

        float amplitude = v.adsr.level * v.volume * v.channelVolume * v.velocity;

        // Sample A read.
        float aL, aR;
        if (v.position >= 0 && v.position < smp.frameCount) {
            aL = Lerp(smp.data, smp.frameCount, v.position);
            aR = (smp.dataR != nullptr)
               ? Lerp(smp.dataR, smp.frameCount, v.position)
               : aL;
        } else {
            aL = aR = 0.0f;
        }

        float outSampleL, outSampleR;
        if (!blendActive) {
            outSampleL = aL;
            outSampleR = aR;
        } else {
            // Sample B read.
            float bL = 0.0f, bR = 0.0f;
            if (v.positionB >= 0 && v.positionB < smpB->frameCount) {
                bL = Lerp(smpB->data, smpB->frameCount, v.positionB);
                bR = (smpB->dataR != nullptr)
                   ? Lerp(smpB->dataR, smpB->frameCount, v.positionB)
                   : bL;
            }

            const float blend = v.blendLevel;
            switch (inst.blendMode) {
                case BlendMode::RING: {
                    // Ring mod by default; blend mixes clean A back in.
                    float rL = aL * bL;
                    float rR = aR * bR;
                    outSampleL = rL * (1.0f - blend) + aL * blend;
                    outSampleR = rR * (1.0f - blend) + aR * blend;
                    break;
                }
                case BlendMode::SYNC: {
                    // SYNC reset is applied at advance time below; here we
                    // just blend B (synced) against clean A.
                    outSampleL = bL * (1.0f - blend) + aL * blend;
                    outSampleR = bR * (1.0f - blend) + aR * blend;
                    break;
                }
                case BlendMode::PM: {
                    // A modulates B's read position. Re-read B at offset.
                    float pmShift = aL * v.pmDepthCur;
                    double posPM = v.positionB + (double)pmShift;
                    // Wrap into B's frame range so we never read out of bounds.
                    double fc = (double)smpB->frameCount;
                    if (fc > 0.0) {
                        posPM = fmod(posPM, fc);
                        if (posPM < 0.0) posPM += fc;
                    }
                    float pmBL = Lerp(smpB->data, smpB->frameCount, posPM);
                    float pmBR = (smpB->dataR != nullptr)
                               ? Lerp(smpB->dataR, smpB->frameCount, posPM)
                               : pmBL;
                    outSampleL = pmBL * (1.0f - blend) + aL * blend;
                    outSampleR = pmBR * (1.0f - blend) + aR * blend;
                    break;
                }
                default: { // MIX
                    outSampleL = aL * (1.0f - blend) + bL * blend;
                    outSampleR = aR * (1.0f - blend) + bR * blend;
                    break;
                }
            }
        }

        float oL = outSampleL * amplitude * gainL;
        float oR = outSampleR * amplitude * gainR;
        ProcessVoiceFilter(v, oL, oR);
        outL[i] += oL;
        outR[i] += oR;
        AccumulateSends(v, oL, oR, i);

        // Advance A (with ping-pong direction) and B (forward only).
        double prevPosA = v.position;
        if (v.goingForward)
            v.position += v.increment;
        else
            v.position -= v.increment;

        HandleLoop(v, smp);

        if (blendActive) {
            v.positionB += v.incrementB;
            // Sample B has its own forward loop. If no loop, positionB simply
            // goes past frameCount and the bounds check above silences it.
            if (smpB->loopEnabled && smpB->loopEnd > smpB->loopStart) {
                if (v.positionB >= smpB->loopEnd) {
                    v.positionB = smpB->loopStart + fmod(v.positionB - smpB->loopStart,
                                                         (double)(smpB->loopEnd - smpB->loopStart));
                }
            }
            // SYNC: when sample A loops (post-HandleLoop position jumped backwards),
            // reset B's cursor. Matches synth-SYNC semantics (A resets B's phase).
            if (inst.blendMode == BlendMode::SYNC && v.position < prevPosA && v.goingForward)
                v.positionB = 0.0;
        }
    }
}

void VoicePlayer::RenderKitVoice(Voice& v, float* outL, float* outR, int numFrames) {
    if (v.midiNote < 0 || v.midiNote >= MAX_KIT_ENTRIES) return;
    const KitEntry& ke = kitInstruments_[v.instrumentID].entries[v.midiNote];
    if (ke.sampleID < 0 || ke.sampleID >= sampleCount_) return;
    const Sample& smp = samples_[ke.sampleID];
    if (smp.data == nullptr || smp.frameCount == 0) return;

    float panR = (v.pan + 1.0f) * 0.5f;
    panR = v.channelPan < 0 ? panR * (1 + v.channelPan) : panR + (1 - panR) * v.channelPan;
    float panL = 1.0f - panR;
    float gainL = sqrtf(panL);
    float gainR = sqrtf(panR);

    for (int i = 0; i < numFrames; i++) {
        if (!v.active) break;

        ProcessADSR(v);
        if (!v.active) break;

        float amplitude = v.adsr.level * v.volume * v.channelVolume * v.velocity;

        float smpL, smpR;
        if (v.position >= 0 && v.position < smp.frameCount) {
            smpL = Lerp(smp.data, smp.frameCount, v.position);
            smpR = (smp.dataR != nullptr)
                 ? Lerp(smp.dataR, smp.frameCount, v.position)
                 : smpL;
        } else {
            smpL = smpR = 0.0f;
        }

        float oL = smpL * amplitude * gainL;
        float oR = smpR * amplitude * gainR;
        ProcessVoiceFilter(v, oL, oR);
        outL[i] += oL;
        outR[i] += oR;
        AccumulateSends(v, oL, oR, i);

        v.position += v.increment;

        // No loop — kit samples play to end
        if (v.position >= smp.frameCount) {
            v.active = false;
            v.adsr.stage = ADSRStage::IDLE;
        }
    }
}

void VoicePlayer::RenderFMVoice(Voice& v, float* outL, float* outR, int numFrames) {
    const FMInstrumentData& inst = fmInstruments_[v.instrumentID];

    float panR = (v.pan + 1.0f) * 0.5f;
    panR = v.channelPan < 0 ? panR * (1 + v.channelPan) : panR + (1 - panR) * v.channelPan;
    float panL = 1.0f - panR;
    float gainL = sqrtf(panL);
    float gainR = sqrtf(panR);

    for (int i = 0; i < numFrames; i++) {
        if (!v.active) break;

        ProcessADSR(v);
        if (!v.active) break;
        ProcessModulation(v);

        float amplitude = v.adsr.level * v.volume * v.channelVolume * v.velocity;

        // Process per-operator ADSR
        float opLevel[FM_NUM_OPERATORS];
        for (int op = 0; op < FM_NUM_OPERATORS; op++) {
            auto& a = v.fmState.adsr[op];
            switch (a.stage) {
                case ADSRStage::ATTACK:
                    a.level += a.attackRate;
                    if (a.level >= 1.0f) { a.level = 1.0f; a.stage = ADSRStage::DECAY; }
                    break;
                case ADSRStage::DECAY:
                    a.level -= a.decayRate;
                    if (a.level <= a.sustainLevel) { a.level = a.sustainLevel; a.stage = ADSRStage::SUSTAIN; }
                    break;
                case ADSRStage::SUSTAIN: break;
                case ADSRStage::RELEASE:
                    a.level -= a.releaseRate;
                    if (a.level <= 0.0f) { a.level = 0.0f; a.stage = ADSRStage::IDLE; }
                    break;
                case ADSRStage::IDLE: break;
            }
            opLevel[op] = a.level * inst.operators[op].level;
        }

        // Generate operator outputs
        float opOut[FM_NUM_OPERATORS];
        float baseFreqInc = (float)v.increment;

        // Calculate each operator's phase increment
        float opInc[FM_NUM_OPERATORS];
        for (int op = 0; op < FM_NUM_OPERATORS; op++) {
            if (inst.operators[op].freqFixed > 0.0f)
                opInc[op] = inst.operators[op].freqFixed / (float)outputRate_;
            else
                opInc[op] = baseFreqInc * inst.operators[op].freqRatio;
        }

        // Apply algorithm
        float output = 0.0f;
        switch (inst.algorithm) {
            case 0: // [1→2→3→4]→out (serial)
            {
                float mod = sinf(v.fmState.phase[0] * 2.0f * (float)M_PI + inst.feedback * v.fmState.prevOutput) * opLevel[0];
                v.fmState.phase[0] += opInc[0]; if (v.fmState.phase[0] >= 1.0f) v.fmState.phase[0] -= 1.0f;

                mod = sinf((v.fmState.phase[1] + mod) * 2.0f * (float)M_PI) * opLevel[1];
                v.fmState.phase[1] += opInc[1]; if (v.fmState.phase[1] >= 1.0f) v.fmState.phase[1] -= 1.0f;

                mod = sinf((v.fmState.phase[2] + mod) * 2.0f * (float)M_PI) * opLevel[2];
                v.fmState.phase[2] += opInc[2]; if (v.fmState.phase[2] >= 1.0f) v.fmState.phase[2] -= 1.0f;

                output = sinf((v.fmState.phase[3] + mod) * 2.0f * (float)M_PI) * opLevel[3];
                v.fmState.phase[3] += opInc[3]; if (v.fmState.phase[3] >= 1.0f) v.fmState.phase[3] -= 1.0f;
                break;
            }
            case 1: // [1→2]→out + [3→4]→out (two pairs)
            {
                float mod1 = sinf(v.fmState.phase[0] * 2.0f * (float)M_PI + inst.feedback * v.fmState.prevOutput) * opLevel[0];
                v.fmState.phase[0] += opInc[0]; if (v.fmState.phase[0] >= 1.0f) v.fmState.phase[0] -= 1.0f;
                float out1 = sinf((v.fmState.phase[1] + mod1) * 2.0f * (float)M_PI) * opLevel[1];
                v.fmState.phase[1] += opInc[1]; if (v.fmState.phase[1] >= 1.0f) v.fmState.phase[1] -= 1.0f;

                float mod2 = sinf(v.fmState.phase[2] * 2.0f * (float)M_PI) * opLevel[2];
                v.fmState.phase[2] += opInc[2]; if (v.fmState.phase[2] >= 1.0f) v.fmState.phase[2] -= 1.0f;
                float out2 = sinf((v.fmState.phase[3] + mod2) * 2.0f * (float)M_PI) * opLevel[3];
                v.fmState.phase[3] += opInc[3]; if (v.fmState.phase[3] >= 1.0f) v.fmState.phase[3] -= 1.0f;

                output = (out1 + out2) * 0.5f;
                break;
            }
            case 2: // [1+2→3→4]→out (two mods into chain)
            {
                float mod1 = sinf(v.fmState.phase[0] * 2.0f * (float)M_PI + inst.feedback * v.fmState.prevOutput) * opLevel[0];
                v.fmState.phase[0] += opInc[0]; if (v.fmState.phase[0] >= 1.0f) v.fmState.phase[0] -= 1.0f;
                float mod2 = sinf(v.fmState.phase[1] * 2.0f * (float)M_PI) * opLevel[1];
                v.fmState.phase[1] += opInc[1]; if (v.fmState.phase[1] >= 1.0f) v.fmState.phase[1] -= 1.0f;

                float mod = sinf((v.fmState.phase[2] + mod1 + mod2) * 2.0f * (float)M_PI) * opLevel[2];
                v.fmState.phase[2] += opInc[2]; if (v.fmState.phase[2] >= 1.0f) v.fmState.phase[2] -= 1.0f;

                output = sinf((v.fmState.phase[3] + mod) * 2.0f * (float)M_PI) * opLevel[3];
                v.fmState.phase[3] += opInc[3]; if (v.fmState.phase[3] >= 1.0f) v.fmState.phase[3] -= 1.0f;
                break;
            }
            case 3: // [1→2]→out + 3→out + 4→out
            {
                float mod1 = sinf(v.fmState.phase[0] * 2.0f * (float)M_PI + inst.feedback * v.fmState.prevOutput) * opLevel[0];
                v.fmState.phase[0] += opInc[0]; if (v.fmState.phase[0] >= 1.0f) v.fmState.phase[0] -= 1.0f;
                float out1 = sinf((v.fmState.phase[1] + mod1) * 2.0f * (float)M_PI) * opLevel[1];
                v.fmState.phase[1] += opInc[1]; if (v.fmState.phase[1] >= 1.0f) v.fmState.phase[1] -= 1.0f;

                float out2 = sinf(v.fmState.phase[2] * 2.0f * (float)M_PI) * opLevel[2];
                v.fmState.phase[2] += opInc[2]; if (v.fmState.phase[2] >= 1.0f) v.fmState.phase[2] -= 1.0f;
                float out3 = sinf(v.fmState.phase[3] * 2.0f * (float)M_PI) * opLevel[3];
                v.fmState.phase[3] += opInc[3]; if (v.fmState.phase[3] >= 1.0f) v.fmState.phase[3] -= 1.0f;

                output = (out1 + out2 + out3) / 3.0f;
                break;
            }
            case 4: // [1→2→3]→out + 4→out
            {
                float mod = sinf(v.fmState.phase[0] * 2.0f * (float)M_PI + inst.feedback * v.fmState.prevOutput) * opLevel[0];
                v.fmState.phase[0] += opInc[0]; if (v.fmState.phase[0] >= 1.0f) v.fmState.phase[0] -= 1.0f;
                mod = sinf((v.fmState.phase[1] + mod) * 2.0f * (float)M_PI) * opLevel[1];
                v.fmState.phase[1] += opInc[1]; if (v.fmState.phase[1] >= 1.0f) v.fmState.phase[1] -= 1.0f;
                float out1 = sinf((v.fmState.phase[2] + mod) * 2.0f * (float)M_PI) * opLevel[2];
                v.fmState.phase[2] += opInc[2]; if (v.fmState.phase[2] >= 1.0f) v.fmState.phase[2] -= 1.0f;

                float out2 = sinf(v.fmState.phase[3] * 2.0f * (float)M_PI) * opLevel[3];
                v.fmState.phase[3] += opInc[3]; if (v.fmState.phase[3] >= 1.0f) v.fmState.phase[3] -= 1.0f;

                output = (out1 + out2) * 0.5f;
                break;
            }
            case 5: // [1+2+3+4]→out (all parallel, additive)
            default:
            {
                for (int op = 0; op < FM_NUM_OPERATORS; op++) {
                    float fb = (op == 0) ? inst.feedback * v.fmState.prevOutput : 0.0f;
                    opOut[op] = sinf((v.fmState.phase[op] + fb) * 2.0f * (float)M_PI) * opLevel[op];
                    v.fmState.phase[op] += opInc[op];
                    if (v.fmState.phase[op] >= 1.0f) v.fmState.phase[op] -= 1.0f;
                }
                output = (opOut[0] + opOut[1] + opOut[2] + opOut[3]) * 0.25f;
                break;
            }
        }

        v.fmState.prevOutput = output;

        float oL = output * amplitude * gainL;
        float oR = output * amplitude * gainR;
        ProcessVoiceFilter(v, oL, oR);
        outL[i] += oL;
        outR[i] += oR;
        AccumulateSends(v, oL, oR, i);
    }
}

void VoicePlayer::RenderSynthVoice(Voice& v, float* outL, float* outR, int numFrames) {
    const SynthInstrumentData& inst = synthInstruments_[v.instrumentID];

    float panR = (v.pan + 1.0f) * 0.5f;
    panR = v.channelPan < 0 ? panR * (1 + v.channelPan) : panR + (1 - panR) * v.channelPan;
    float panL = 1.0f - panR;
    float gainL = sqrtf(panL);
    float gainR = sqrtf(panR);

    for (int i = 0; i < numFrames; i++) {
        if (!v.active) break;

        ProcessADSR(v);
        if (!v.active) break;
        ProcessModulation(v);

        // Update blend envelope
        if (inst.blendEnvelopeEnabled && v.blendAdsr.stage != ADSRStage::IDLE) {
            ProcessBlendADSR(v);
        }

        float amplitude = v.adsr.level * v.volume * v.channelVolume * v.velocity;

        // Evaluate native envelopes — sets targets per-sample
        {
            float et = v.envState.time;
            const auto& envs = inst.envelopes;
            if (envs.envs[(int)EnvParamID::BLEND].enabled)
                v.blendTarget = envs.envs[(int)EnvParamID::BLEND].Evaluate(et);
            if (envs.envs[(int)EnvParamID::PULSE_WIDTH].enabled)
                v.pulseWidthTarget = envs.envs[(int)EnvParamID::PULSE_WIDTH].Evaluate(et);
            if (envs.envs[(int)EnvParamID::WAVE_B_RATIO].enabled)
                v.waveBRatioTarget = envs.envs[(int)EnvParamID::WAVE_B_RATIO].Evaluate(et);
            if (envs.envs[(int)EnvParamID::PM_DEPTH].enabled)
                v.pmDepthTarget = envs.envs[(int)EnvParamID::PM_DEPTH].Evaluate(et);
            v.envState.time += v.envState.timePerSample;
        }

        // Smooth toward targets (~5ms)
        float smoothRate = 1.0f / (0.005f * (float)outputRate_);
        v.blendLevel += (v.blendTarget - v.blendLevel) * smoothRate;
        v.pmDepthCur += (v.pmDepthTarget - v.pmDepthCur) * smoothRate;
        v.waveBRatioCur += (v.waveBRatioTarget - v.waveBRatioCur) * smoothRate;
        v.pulseWidthCur += (v.pulseWidthTarget - v.pulseWidthCur) * smoothRate;

        // Generate output based on blend mode
        float blend = v.blendLevel;
        float output;
        float pw = v.pulseWidthCur;
        float sampleA = GenerateSample(inst.waveA, v.synthPhase, pw);

        switch (inst.blendMode) {
            case BlendMode::RING: {
                float sampleB = GenerateSample(inst.waveB, v.synthPhaseB, pw);
                // Ring mod output by default. Blend mixes clean A back in.
                float ring = sampleA * sampleB;
                output = ring * (1.0f - blend) + sampleA * blend;
                break;
            }
            case BlendMode::SYNC: {
                float sampleB = GenerateSample(inst.waveB, v.synthPhaseB, pw);
                // One-pole lowpass on sync output to tame harsh overtones
                // Higher B ratio = more harmonics = more filtering needed
                float syncCutoff = 0.15f + 0.85f / (1.0f + v.waveBRatioCur * 0.5f);
                v.syncFilterState += syncCutoff * (sampleB - v.syncFilterState);
                float filteredB = v.syncFilterState;
                // Hard sync: output is filtered synced B by default. Blend mixes clean A back in.
                output = filteredB * (1.0f - blend) + sampleA * blend;
                break;
            }
            case BlendMode::PM: {
                // Phase modulation: A modulates B's phase. PM output by default.
                float modAmount = sampleA * v.pmDepthCur;
                float pmPhase = v.synthPhaseB + modAmount;
                pmPhase -= floorf(pmPhase); // wrap to 0..1
                float sampleB = GenerateSample(inst.waveB, pmPhase, pw);
                // Blend mixes clean A back in
                output = sampleB * (1.0f - blend) + sampleA * blend;
                break;
            }
            default: { // MIX
                float sampleB = GenerateSample(inst.waveB, v.synthPhaseB, pw);
                output = sampleA * (1.0f - blend) + sampleB * blend;
                break;
            }
        }

        {
            float oL = output * amplitude * gainL;
            float oR = output * amplitude * gainR;
            ProcessVoiceFilter(v, oL, oR);
            outL[i] += oL;
            outR[i] += oR;
            AccumulateSends(v, oL, oR, i);
        }

        // Advance phases
        float phaseInc = (float)v.increment;
        float prevPhaseA = v.synthPhase;
        v.synthPhase += phaseInc;
        if (v.synthPhase >= 1.0f)
            v.synthPhase -= 1.0f;

        // Wave B runs at frequency ratio relative to A (all modes)
        v.synthPhaseB += phaseInc * v.waveBRatioCur;
        while (v.synthPhaseB >= 1.0f)
            v.synthPhaseB -= 1.0f;

        // Hard sync: reset B's phase when A completes a cycle
        if (inst.blendMode == BlendMode::SYNC && v.synthPhase < prevPhaseA)
            v.synthPhaseB = 0.0f;
    }
}

void VoicePlayer::Process(float* outL, float* outR, int numFrames) {
    memset(outL, 0, numFrames * sizeof(float));
    memset(outR, 0, numFrames * sizeof(float));

    // Clear send buffers
    int frames = std::min(numFrames, MAX_SEND_BUFFER);
    memset(sendDelayL_, 0, frames * sizeof(float));
    memset(sendDelayR_, 0, frames * sizeof(float));
    memset(sendReverbL_, 0, frames * sizeof(float));
    memset(sendReverbR_, 0, frames * sizeof(float));
    hasSends_ = false;

    // Count independent notes (unison followers don't count separately)
    int activeCount = 0;
    for (int i = 0; i < MAX_VOICES; i++) {
        if (!voices_[i].active) continue;
        // Only count group leaders or non-unison voices
        if (voices_[i].unisonGroup < 0 || voices_[i].unisonGroup == i)
            activeCount++;
    }

    for (int i = 0; i < MAX_VOICES; i++) {
        if (!voices_[i].active) continue;

        switch (voices_[i].type) {
            case VoiceType::SAMPLE:
                RenderSampleVoice(voices_[i], outL, outR, numFrames);
                break;
            case VoiceType::SYNTH:
                RenderSynthVoice(voices_[i], outL, outR, numFrames);
                break;
            case VoiceType::KIT:
                RenderKitVoice(voices_[i], outL, outR, numFrames);
                break;
            case VoiceType::FM:
                RenderFMVoice(voices_[i], outL, outR, numFrames);
                break;
        }
    }

    // Polyphony scaling: 1/sqrt(N), smoothed across buffer
    float targetScale = (activeCount > 1) ? 1.0f / sqrtf((float)activeCount) : 1.0f;
    float scale = prevPolyScale_;
    float scaleStep = (targetScale - scale) / (float)numFrames;

    for (int i = 0; i < numFrames; i++) {
        scale += scaleStep;
        outL[i] *= scale;
        outR[i] *= scale;

        // Soft knee limiter — transparent below 0.9, gentle compression above
        const float knee = 0.9f;
        if (outL[i] > knee)
            outL[i] = knee + (1.0f - knee) * tanhf((outL[i] - knee) / (1.0f - knee));
        else if (outL[i] < -knee)
            outL[i] = -knee - (1.0f - knee) * tanhf((-outL[i] - knee) / (1.0f - knee));

        if (outR[i] > knee)
            outR[i] = knee + (1.0f - knee) * tanhf((outR[i] - knee) / (1.0f - knee));
        else if (outR[i] < -knee)
            outR[i] = -knee - (1.0f - knee) * tanhf((-outR[i] - knee) / (1.0f - knee));
    }
    prevPolyScale_ = targetScale;
}
