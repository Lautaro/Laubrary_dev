#include "Effects.h"
#include <cmath>
#include <cstring>
#include <algorithm>

// ── Biquad Filter ──────────────────────────────────────────────────────

void BiquadFilter::Init(uint32_t sampleRate) {
    sampleRate_ = sampleRate;
    Reset();
}

void BiquadFilter::SetParams(float cutoff, float resonance, int mode) {
    targetCutoff_ = std::max(0.001f, std::min(cutoff, 1.0f));
    resonance_ = std::max(0.1f, resonance);
    mode_ = mode;
    dirty_ = true;
}

void BiquadFilter::Reset() {
    x1L_ = x2L_ = y1L_ = y2L_ = 0;
    x1R_ = x2R_ = y1R_ = y2R_ = 0;
    smoothCutoff_ = cutoff_;
    dirty_ = true;
}

void BiquadFilter::RecalcCoefficients() {
    // Robert Bristow-Johnson Audio EQ Cookbook
    float freq = smoothCutoff_ * (sampleRate_ * 0.5f);
    freq = std::max(20.0f, std::min(freq, sampleRate_ * 0.5f - 100.0f));
    float w0 = 2.0f * (float)M_PI * freq / sampleRate_;
    float sinW0 = sinf(w0);
    float cosW0 = cosf(w0);
    float alpha = sinW0 / (2.0f * resonance_);

    float a0;
    switch (mode_) {
        case 0: // Lowpass
            b0_ = (1.0f - cosW0) * 0.5f;
            b1_ = 1.0f - cosW0;
            b2_ = (1.0f - cosW0) * 0.5f;
            a0  = 1.0f + alpha;
            a1_ = -2.0f * cosW0;
            a2_ = 1.0f - alpha;
            break;
        case 1: // Highpass
            b0_ = (1.0f + cosW0) * 0.5f;
            b1_ = -(1.0f + cosW0);
            b2_ = (1.0f + cosW0) * 0.5f;
            a0  = 1.0f + alpha;
            a1_ = -2.0f * cosW0;
            a2_ = 1.0f - alpha;
            break;
        case 2: // Bandpass
            b0_ = alpha;
            b1_ = 0.0f;
            b2_ = -alpha;
            a0  = 1.0f + alpha;
            a1_ = -2.0f * cosW0;
            a2_ = 1.0f - alpha;
            break;
        default:
            a0 = 1.0f;
            break;
    }

    // Normalize
    float invA0 = 1.0f / a0;
    b0_ *= invA0;
    b1_ *= invA0;
    b2_ *= invA0;
    a1_ *= invA0;
    a2_ *= invA0;
}

void BiquadFilter::Process(float* left, float* right, int numSamples) {
    // Smooth cutoff to avoid zipper noise (~10ms)
    float smoothRate = 1.0f / (0.01f * sampleRate_);

    for (int i = 0; i < numSamples; i++) {
        // Smooth cutoff
        if (fabsf(smoothCutoff_ - targetCutoff_) > 0.0001f) {
            smoothCutoff_ += (targetCutoff_ - smoothCutoff_) * smoothRate;
            dirty_ = true;
        }

        if (dirty_) {
            RecalcCoefficients();
            dirty_ = false;
        }

        // Left channel
        float inL = left[i];
        float outL = b0_ * inL + b1_ * x1L_ + b2_ * x2L_ - a1_ * y1L_ - a2_ * y2L_;
        x2L_ = x1L_; x1L_ = inL;
        y2L_ = y1L_; y1L_ = outL;
        left[i] = outL;

        // Right channel
        float inR = right[i];
        float outR = b0_ * inR + b1_ * x1R_ + b2_ * x2R_ - a1_ * y1R_ - a2_ * y2R_;
        x2R_ = x1R_; x1R_ = inR;
        y2R_ = y1R_; y1R_ = outR;
        right[i] = outR;
    }
}

// ── Delay ──────────────────────────────────────────────────────────────

void Delay::Init(uint32_t sampleRate) {
    sampleRate_ = sampleRate;
    Reset();
}

void Delay::SetParams(float time, float feedback, float wet, bool pingPong) {
    delayTime_ = std::max(0.001f, std::min(time, 2.0f));
    feedback_  = std::max(0.0f, std::min(feedback, 0.95f));
    wet_       = std::max(0.0f, std::min(wet, 1.0f));
    pingPong_  = pingPong;
}

void Delay::Reset() {
    memset(bufferL_, 0, sizeof(bufferL_));
    memset(bufferR_, 0, sizeof(bufferR_));
    writePos_ = 0;
}

void Delay::Process(float* left, float* right, int numSamples) {
    int delaySamples = (int)(delayTime_ * sampleRate_);
    delaySamples = std::max(1, std::min(delaySamples, MAX_DELAY_SAMPLES - 1));

    for (int i = 0; i < numSamples; i++) {
        int readPos = writePos_ - delaySamples;
        if (readPos < 0) readPos += MAX_DELAY_SAMPLES;

        float delL = bufferL_[readPos];
        float delR = bufferR_[readPos];

        float inL = left[i];
        float inR = right[i];

        if (pingPong_) {
            bufferL_[writePos_] = inL + delR * feedback_;
            bufferR_[writePos_] = inR + delL * feedback_;
        } else {
            bufferL_[writePos_] = inL + delL * feedback_;
            bufferR_[writePos_] = inR + delR * feedback_;
        }

        left[i]  = inL + delL * wet_;
        right[i] = inR + delR * wet_;

        writePos_ = (writePos_ + 1) % MAX_DELAY_SAMPLES;
    }
}

// ── Reverb (Freeverb) ──────────────────────────────────────────────────

// Freeverb tuning constants (scaled from 44100 reference)
static const int combTuningL[8]  = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
static const int combTuningR[8]  = { 1139, 1211, 1300, 1379, 1445, 1514, 1580, 1640 };
static const int allpassTuningL[4] = { 556, 441, 341, 225 };
static const int allpassTuningR[4] = { 579, 464, 364, 248 };

void Reverb::Init(uint32_t sampleRate) {
    sampleRate_ = sampleRate;
    InitCombSizes();
    Reset();
}

void Reverb::InitCombSizes() {
    float scale = sampleRate_ / 44100.0f;
    for (int i = 0; i < NUM_COMBS; i++) {
        combsL_[i].bufSize = std::min((int)(combTuningL[i] * scale), MAX_COMB_LEN - 1);
        combsR_[i].bufSize = std::min((int)(combTuningR[i] * scale), MAX_COMB_LEN - 1);
    }
    for (int i = 0; i < NUM_ALLPASS; i++) {
        allpassL_[i].bufSize = std::min((int)(allpassTuningL[i] * scale), MAX_AP_LEN - 1);
        allpassR_[i].bufSize = std::min((int)(allpassTuningR[i] * scale), MAX_AP_LEN - 1);
    }
}

void Reverb::SetParams(float roomSize, float damp, float wet, float width) {
    roomSize_ = std::max(0.0f, std::min(roomSize, 1.0f));
    damp_     = std::max(0.0f, std::min(damp, 1.0f));
    wet_      = std::max(0.0f, std::min(wet, 1.0f));
    width_    = std::max(0.0f, std::min(width, 1.0f));
}

void Reverb::Reset() {
    for (int i = 0; i < NUM_COMBS; i++) {
        memset(combsL_[i].buffer, 0, sizeof(combsL_[i].buffer));
        memset(combsR_[i].buffer, 0, sizeof(combsR_[i].buffer));
        combsL_[i].idx = 0; combsL_[i].filterStore = 0;
        combsR_[i].idx = 0; combsR_[i].filterStore = 0;
    }
    for (int i = 0; i < NUM_ALLPASS; i++) {
        memset(allpassL_[i].buffer, 0, sizeof(allpassL_[i].buffer));
        memset(allpassR_[i].buffer, 0, sizeof(allpassR_[i].buffer));
        allpassL_[i].idx = 0;
        allpassR_[i].idx = 0;
    }
}

void Reverb::Process(float* left, float* right, int numSamples) {
    float feedback = roomSize_ * 0.28f + 0.7f; // scale to useful range
    float damp1 = damp_;
    float damp2 = 1.0f - damp_;

    for (int i = 0; i < numSamples; i++) {
        float input = (left[i] + right[i]) * 0.5f; // mono input
        float outL = 0, outR = 0;

        // Parallel comb filters
        for (int c = 0; c < NUM_COMBS; c++) {
            // Left
            {
                auto& comb = combsL_[c];
                float output = comb.buffer[comb.idx];
                comb.filterStore = output * damp2 + comb.filterStore * damp1;
                comb.buffer[comb.idx] = input + comb.filterStore * feedback;
                comb.idx = (comb.idx + 1) % comb.bufSize;
                outL += output;
            }
            // Right
            {
                auto& comb = combsR_[c];
                float output = comb.buffer[comb.idx];
                comb.filterStore = output * damp2 + comb.filterStore * damp1;
                comb.buffer[comb.idx] = input + comb.filterStore * feedback;
                comb.idx = (comb.idx + 1) % comb.bufSize;
                outR += output;
            }
        }

        // Series allpass filters
        for (int a = 0; a < NUM_ALLPASS; a++) {
            // Left
            {
                auto& ap = allpassL_[a];
                float bufOut = ap.buffer[ap.idx];
                ap.buffer[ap.idx] = outL + bufOut * 0.5f;
                outL = bufOut - outL;
                ap.idx = (ap.idx + 1) % ap.bufSize;
            }
            // Right
            {
                auto& ap = allpassR_[a];
                float bufOut = ap.buffer[ap.idx];
                ap.buffer[ap.idx] = outR + bufOut * 0.5f;
                outR = bufOut - outR;
                ap.idx = (ap.idx + 1) % ap.bufSize;
            }
        }

        // Stereo width
        float wet1 = wet_ * (1.0f + width_) * 0.5f;
        float wet2 = wet_ * (1.0f - width_) * 0.5f;

        left[i]  += outL * wet1 + outR * wet2;
        right[i] += outR * wet1 + outL * wet2;
    }
}

// ── Mix Bus ────────────────────────────────────────────────────────────

void MixBus::Init(uint32_t sampleRate) {
    sampleRate_ = sampleRate;
    for (int i = 0; i < MAX_CHANNELS; i++) {
        filters_[i].Init(sampleRate);
        delaySend_[i] = 0.0f;
        reverbSend_[i] = 0.0f;
    }
    sharedDelay_.Init(sampleRate);
    sharedReverb_.Init(sampleRate);
}

void MixBus::SetChannelFilter(int channel, int mode, float cutoff, float resonance) {
    if (channel < 0 || channel >= MAX_CHANNELS) return;
    filters_[channel].SetParams(cutoff, resonance, mode);
}

void MixBus::SetChannelDelaySend(int channel, float send) {
    if (channel >= 0 && channel < MAX_CHANNELS)
        delaySend_[channel] = std::max(0.0f, std::min(send, 1.0f));
}

void MixBus::SetChannelReverbSend(int channel, float send) {
    if (channel >= 0 && channel < MAX_CHANNELS)
        reverbSend_[channel] = std::max(0.0f, std::min(send, 1.0f));
}

void MixBus::SetDelayParams(float time, float feedback, float wet) {
    sharedDelay_.SetParams(time, feedback, wet, true);
}

void MixBus::SetReverbParams(float roomSize, float damp, float wet) {
    sharedReverb_.SetParams(roomSize, damp, wet, 1.0f);
}

void MixBus::Process(float** channelBuffersL, float** channelBuffersR,
                      int channelCount, float* outL, float* outR, int numFrames) {
    // Clear output
    memset(outL, 0, numFrames * sizeof(float));
    memset(outR, 0, numFrames * sizeof(float));

    // Clear send buffers
    int frames = std::min(numFrames, 4096);
    memset(delayInL_, 0, frames * sizeof(float));
    memset(delayInR_, 0, frames * sizeof(float));
    memset(reverbInL_, 0, frames * sizeof(float));
    memset(reverbInR_, 0, frames * sizeof(float));

    // Process each channel: filter → sum to output + send buses
    for (int ch = 0; ch < channelCount && ch < MAX_CHANNELS; ch++) {
        float* chL = channelBuffersL[ch];
        float* chR = channelBuffersR[ch];

        // Apply per-channel filter (in-place)
        filters_[ch].Process(chL, chR, frames);

        // Sum to master output
        for (int i = 0; i < frames; i++) {
            outL[i] += chL[i];
            outR[i] += chR[i];
        }

        // Sum to send buses
        float dSend = delaySend_[ch];
        float rSend = reverbSend_[ch];
        if (dSend > 0.0f) {
            for (int i = 0; i < frames; i++) {
                delayInL_[i] += chL[i] * dSend;
                delayInR_[i] += chR[i] * dSend;
            }
        }
        if (rSend > 0.0f) {
            for (int i = 0; i < frames; i++) {
                reverbInL_[i] += chL[i] * rSend;
                reverbInR_[i] += chR[i] * rSend;
            }
        }
    }

    // Process shared effects and add to output
    sharedDelay_.Process(delayInL_, delayInR_, frames);
    for (int i = 0; i < frames; i++) {
        outL[i] += delayInL_[i];
        outR[i] += delayInR_[i];
    }

    sharedReverb_.Process(reverbInL_, reverbInR_, frames);
    for (int i = 0; i < frames; i++) {
        outL[i] += reverbInL_[i];
        outR[i] += reverbInR_[i];
    }
}
