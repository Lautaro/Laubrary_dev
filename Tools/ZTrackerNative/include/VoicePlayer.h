#pragma once

#include "Common.h"

class VoicePlayer {
public:
    void Init(uint32_t sampleRate);

    // Sample bank
    int  LoadSample(float* dataL, float* dataR, int frameCount, int sampleRate, int channels);
    void SetSampleLoop(int sampleID, int loopMode, int loopStart, int loopEnd);
    void SetSampleSustainLoop(int sampleID, int start, int end);

    // Instruments
    void SetSampleInstrument(int id, const SampleInstrumentData& data);
    void SetSynthInstrument(int id, const SynthInstrumentData& data);
    void SetFMInstrument(int id, const FMInstrumentData& data);
    void SetKitInstrument(int id);
    void SetKitEntry(int id, int midiNote, const KitEntry& entry);

    // Voice control
    int  NoteOn(int instrumentID, int midiNote, float velocity, float channelVolume = 1.0f, float channelPan = 0.0f);
    int  SynthNoteOn(int instrumentID, int midiNote, float velocity);
    void NoteOff(int voiceID);
    void AllNotesOff();

    // Render
    void Process(float* outL, float* outR, int numFrames);

    // Instrument data (public for DLL export access)
    SampleInstrumentData  sampleInstruments_[MAX_INSTRUMENTS];
    SynthInstrumentData   synthInstruments_[MAX_INSTRUMENTS];
    FMInstrumentData      fmInstruments_[MAX_INSTRUMENTS];
    KitInstrumentData     kitInstruments_[MAX_INSTRUMENTS];
    VoiceType             instrumentTypes_[MAX_INSTRUMENTS];
    Voice                 voices_[MAX_VOICES];

    float                 prevPolyScale_ = 1.0f;

    // Send buffers for delay/reverb (accumulated during voice render)
    static constexpr int MAX_SEND_BUFFER = 4096;
    float                 sendDelayL_[MAX_SEND_BUFFER] = {};
    float                 sendDelayR_[MAX_SEND_BUFFER] = {};
    float                 sendReverbL_[MAX_SEND_BUFFER] = {};
    float                 sendReverbR_[MAX_SEND_BUFFER] = {};
    bool                  hasSends_ = false;

private:
    float noteChannelVolume_ = 1.0f;
    float noteChannelPan_ = 0.0f;
    uint32_t              outputRate_ = 44100;

    Sample                samples_[MAX_SAMPLES];
    int                   sampleCount_ = 0;

    int  AllocateVoice();
    void InitADSR(Voice& v, const ADSRParams& p);
    void ProcessADSR(Voice& v);
    void ProcessBlendADSR(Voice& v);
    void RenderSampleVoice(Voice& v, float* outL, float* outR, int numFrames);
    void RenderSynthVoice(Voice& v, float* outL, float* outR, int numFrames);
    void RenderKitVoice(Voice& v, float* outL, float* outR, int numFrames);
    void RenderFMVoice(Voice& v, float* outL, float* outR, int numFrames);
    void HandleLoop(Voice& v, const Sample& smp);
    void ReleaseVoice(Voice& v);
    void InitVoiceFilter(Voice& v, const InstrumentEffects& fx);
    void ProcessVoiceFilter(Voice& v, float& sampleL, float& sampleR);
    void AccumulateSends(Voice& v, float sampleL, float sampleR, int frameIndex);

    // Modulation
    void InitModulation(Voice& v, const PortamentoParams& pp, const VibratoParams& vp, const ArpeggioParams& ap, int midiNote, double prevIncrement);
    void ProcessModulation(Voice& v);
    double EvalArpeggioCurve(const ArpeggioState& arp);

    // Per-channel last note tracking for portamento
    double lastChannelIncrement_[MAX_CHANNELS];
    int    lastChannelNote_[MAX_CHANNELS];
};
