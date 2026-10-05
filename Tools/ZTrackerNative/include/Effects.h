#pragma once

#include "Common.h"

// ── Biquad Filter (per-channel insert) ─────────────────────────────────

class BiquadFilter {
public:
    void Init(uint32_t sampleRate);
    void SetParams(float cutoff, float resonance, int mode);
    void Process(float* left, float* right, int numSamples);
    void Reset();

private:
    uint32_t sampleRate_ = 44100;
    float cutoff_    = 1.0f;    // normalised 0-1 (1 = Nyquist)
    float resonance_ = 0.707f;  // Q factor
    int   mode_      = 0;       // 0=LP, 1=HP, 2=BP
    bool  dirty_     = true;

    // Coefficients
    float b0_ = 1, b1_ = 0, b2_ = 0, a1_ = 0, a2_ = 0;

    // State (stereo)
    float x1L_ = 0, x2L_ = 0, y1L_ = 0, y2L_ = 0;
    float x1R_ = 0, x2R_ = 0, y1R_ = 0, y2R_ = 0;

    // Smoothing
    float targetCutoff_ = 1.0f;
    float smoothCutoff_ = 1.0f;

    void RecalcCoefficients();
};

// ── Delay (shared send) ────────────────────────────────────────────────

class Delay {
public:
    void Init(uint32_t sampleRate);
    void SetParams(float time, float feedback, float wet, bool pingPong);
    void Process(float* left, float* right, int numSamples);
    void Reset();

    static constexpr int MAX_DELAY_SAMPLES = 88200; // 2 seconds at 44100

private:
    uint32_t sampleRate_ = 44100;
    float bufferL_[MAX_DELAY_SAMPLES] = {};
    float bufferR_[MAX_DELAY_SAMPLES] = {};
    int   writePos_   = 0;
    float delayTime_  = 0.25f;
    float feedback_   = 0.4f;
    float wet_        = 0.3f;
    bool  pingPong_   = false;
};

// ── Reverb (Freeverb-style, shared send) ───────────────────────────────

class Reverb {
public:
    void Init(uint32_t sampleRate);
    void SetParams(float roomSize, float damp, float wet, float width);
    void Process(float* left, float* right, int numSamples);
    void Reset();

private:
    static constexpr int NUM_COMBS    = 8;
    static constexpr int NUM_ALLPASS  = 4;
    static constexpr int MAX_COMB_LEN = 4096;
    static constexpr int MAX_AP_LEN   = 2048;

    struct CombFilter {
        float buffer[MAX_COMB_LEN] = {};
        int   bufSize = 0;
        int   idx     = 0;
        float filterStore = 0;
    };

    struct AllpassFilter {
        float buffer[MAX_AP_LEN] = {};
        int   bufSize = 0;
        int   idx     = 0;
    };

    uint32_t sampleRate_ = 44100;
    float roomSize_ = 0.5f;
    float damp_     = 0.5f;
    float wet_      = 0.3f;
    float width_    = 1.0f;

    // Stereo Freeverb: separate comb/allpass for L and R
    CombFilter    combsL_[NUM_COMBS];
    CombFilter    combsR_[NUM_COMBS];
    AllpassFilter allpassL_[NUM_ALLPASS];
    AllpassFilter allpassR_[NUM_ALLPASS];

    void InitCombSizes();
};

// ── Mix Bus ────────────────────────────────────────────────────────────

class MixBus {
public:
    void Init(uint32_t sampleRate);

    // Per-channel filter
    void SetChannelFilter(int channel, int mode, float cutoff, float resonance);

    // Send levels
    void SetChannelDelaySend(int channel, float send);
    void SetChannelReverbSend(int channel, float send);

    // Shared effect params
    void SetDelayParams(float time, float feedback, float wet);
    void SetReverbParams(float roomSize, float damp, float wet);

    // Process all channels into final stereo output
    // channelBuffersL/R: per-channel audio [channel][sample]
    // outL/outR: final mix output
    void Process(float** channelBuffersL, float** channelBuffersR,
                 int channelCount, float* outL, float* outR, int numFrames);

private:
    uint32_t sampleRate_ = 44100;

    BiquadFilter filters_[MAX_CHANNELS];
    float        delaySend_[MAX_CHANNELS]  = {};
    float        reverbSend_[MAX_CHANNELS] = {};

    Delay  sharedDelay_;
    Reverb sharedReverb_;

    // Temp buffers for send mixing
    float delayInL_[4096]  = {};
    float delayInR_[4096]  = {};
    float reverbInL_[4096] = {};
    float reverbInR_[4096] = {};
};
