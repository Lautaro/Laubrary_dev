#pragma once

#include "Common.h"
#include "VoicePlayer.h"
#include "EventQueue.h"

constexpr int MAX_PATTERNS   = 256;
constexpr int MAX_ROWS       = 256;
constexpr int MAX_ORDER      = 256;
constexpr int NOTE_OFF_VALUE = 127;

#pragma pack(push, 1)
struct TrackerCell {
    int8_t  note        = -1;    // MIDI note, -1 = empty, 127 = note off
    int16_t instrument  = -1;    // -1 = use previous
    uint8_t volume      = 0xFF;  // 0xFF = use instrument default
    uint8_t effectCmd   = 0;     // effect command byte
    uint8_t effectParam = 0;     // effect parameter byte
};
#pragma pack(pop)

enum class ChannelType : uint8_t { AUDIO, EVENT_TRACK };

struct Pattern {
    int         rowCount     = 64;
    int         channelCount = 0;
    TrackerCell cells[MAX_ROWS][MAX_CHANNELS]; // [row][channel]
    char        eventStrings[MAX_ROWS][MAX_CHANNELS][64]; // for event track channels
};

struct ChannelState {
    int   lastNote       = -1;
    int   lastInstrument = -1;
    int   activeVoiceID  = -1;
    float volume         = 1.0f;
    float pan            = 0.0f;
    bool  muted          = false;
    bool  pendingNoteOff = false;

    // Per-tick effect state
    float slideSpeed     = 0.0f;  // pitch slide per tick (increment delta)
    int    portaTarget     = -1;    // target note for tone portamento
    double portaSpeed      = 0.0;
    bool   portaActive     = false; // true only on rows with 03xx command
    double portaPitch      = 0.0;   // current pitch in semitones (fractional, for smooth glide)
    float vibratoPhase   = 0.0f;
    float vibratoSpeed   = 0.0f;
    float vibratoDepth   = 0.0f;
    float tremoloPhase   = 0.0f;
    float tremoloSpeed   = 0.0f;
    float tremoloDepth   = 0.0f;
    float volSlideUp     = 0.0f;
    float volSlideDown   = 0.0f;

    // Macros (0-1 values, up to 4 per channel)
    static constexpr int MAX_MACROS = 4;
    float macroValue[MAX_MACROS]  = {};
    float macroTarget[MAX_MACROS] = {};
    float macroSlideSpeed[MAX_MACROS] = {};
    int   activePreset = -1;
};

class Sequencer {
public:
    void Init(uint32_t sampleRate);
    void SetEventQueue(EventQueue* eq) { eventQueue_ = eq; }

    // Song configuration (call from main thread before playing)
    void SetTempo(int bpm, int ticksPerRow);
    void SetLinesPerBeat(int lpb);
    void SetChannelCount(int count);
    void SetChannelType(int channel, int type);
    void SetEventString(int patternID, int row, int channel, const char* str);
    void SetBeatTickInterval(int rows) { beatTickInterval_ = rows; }
    void SetPatternData(int patternID, const TrackerCell* cells, int rowCount, int channelCount);
    void SetOrderList(const int* order, int length);
    void MuteChannel(int channel, bool muted);
    void SetChannelVolume(int channel, float vol);
    void SetChannelPan(int channel, float pan);

    // Preset slot map. Each *user* instrument (the index carried in cell.instrument
    // and shown to the user) expands into a band of native instrument slots —
    // one for Base plus one for each preset. The I<n> command picks which
    // variant the channel uses for subsequent note-ons: 0 = Base, N = preset N-1.
    // SetPresetMap registers the base-slot and variant-count for user index i.
    void SetPresetMap(int userInstrumentIndex, int baseSlot, int variantCount);

    // Transport
    void Play(int fromOrder = 0, int fromRow = 0);
    void Stop();
    bool IsPlaying() const { return playing_; }

    // Getters for UI
    int GetCurrentRow() const { return currentRow_; }
    int GetCurrentOrder() const { return orderPosition_; }
    int GetBPM() const { return bpm_; }
    const ChannelState& GetChannelState(int ch) const { return channels_[ch]; }

    // Audio thread
    void Process(VoicePlayer& vp, float* outL, float* outR, int numFrames);

private:
    uint32_t outputRate_     = 44100;

    // Tempo
    int      bpm_            = 120;
    int      linesPerBeat_   = 4;
    int      ticksPerRow_    = 6;
    double   samplesPerTick_ = 0.0;

    // Song data
    int      channelCount_   = 8;
    Pattern  patterns_[MAX_PATTERNS];
    int      patternCount_   = 0;
    int      orderList_[MAX_ORDER];
    int      orderLength_    = 0;

    // Playback state
    bool     playing_        = false;
    int      orderPosition_  = 0;
    int      currentRow_     = 0;
    int      currentTick_    = 0;
    double   sampleCountdown_ = 0.0;
    bool     firstRowPending_ = false;

    ChannelState channels_[MAX_CHANNELS];

    // Preset slot map — parallel arrays. `presetBaseSlot_[u]` = native slot of
    // Base for user-instrument `u`; `presetVariantCount_[u]` = number of
    // presets (0 = no presets, only Base). A value of -1 means "user index not
    // registered", in which case the user index is used verbatim (backward-
    // compat for callers that never registered).
    int presetBaseSlot_[MAX_INSTRUMENTS];
    int presetVariantCount_[MAX_INSTRUMENTS];
    ChannelType  channelTypes_[MAX_CHANNELS] = {};
    int          beatTickInterval_ = 4;  // fire BEAT_TICK every N rows
    EventQueue*  eventQueue_    = nullptr;
    uint64_t     totalSamples_  = 0;

    // Pattern break/jump requests (set by effects, applied at end of row)
    bool     patternBreak_     = false;
    int      patternBreakRow_  = 0;
    bool     patternJump_      = false;
    int      patternJumpOrder_ = 0;

    void RecalcTempo();
    void AdvanceTick(VoicePlayer& vp);
    void ProcessRow(VoicePlayer& vp);
    void ProcessRowEffects(VoicePlayer& vp, int channel, const TrackerCell& cell);
    void ProcessTickEffects(VoicePlayer& vp, int channel, const TrackerCell& cell);
    void AdvanceToNextRow(VoicePlayer& vp);
    const Pattern& CurrentPattern() const;
    void PushEvent(ZTrackerEventType type, int patIdx = -1, int row = -1,
                   int ch = -1, int note = -1, int inst = -1, int iparam = 0, float fparam = 0.0f);
};
