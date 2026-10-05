#include "Sequencer.h"
#include "EventQueue.h"
#include <cstring>
#include <cmath>
#include <algorithm>

// Static source identifiers for parameter modifiers
static const char* MOD_SEQ_GLIDE = "seq:3xx";
static const char* MOD_SEQ_SLIDE = "seq:slide";
static const char* MOD_SEQ_VIBRATO = "seq:vibrato";
static const char* MOD_SEQ_TREMOLO = "seq:tremolo";

void Sequencer::Init(uint32_t sampleRate) {
    outputRate_ = sampleRate;
    playing_ = false;
    currentRow_ = 0;
    currentTick_ = 0;
    sampleCountdown_ = 0.0;
    orderPosition_ = 0;
    orderLength_ = 0;
    patternCount_ = 0;
    channelCount_ = 8;
    patternBreak_ = false;
    patternJump_ = false;

    for (int i = 0; i < MAX_CHANNELS; i++) {
        channels_[i] = ChannelState{};
    }
    for (int i = 0; i < MAX_ORDER; i++)
        orderList_[i] = 0;
    for (int i = 0; i < MAX_PATTERNS; i++)
        patterns_[i] = Pattern{};

    // Preset map defaults to "not registered" — unregistered user indices pass
    // through verbatim so pre-preset callers keep working.
    for (int i = 0; i < MAX_INSTRUMENTS; i++) {
        presetBaseSlot_[i] = -1;
        presetVariantCount_[i] = 0;
    }

    RecalcTempo();
}

void Sequencer::SetPresetMap(int userIdx, int baseSlot, int variantCount) {
    if (userIdx < 0 || userIdx >= MAX_INSTRUMENTS) return;
    presetBaseSlot_[userIdx] = baseSlot;
    presetVariantCount_[userIdx] = variantCount < 0 ? 0 : variantCount;
}

void Sequencer::SetTempo(int bpm, int ticksPerRow) {
    bpm_ = bpm > 0 ? bpm : 120;
    ticksPerRow_ = ticksPerRow > 0 ? ticksPerRow : 6;
    RecalcTempo();
}

void Sequencer::SetLinesPerBeat(int lpb) {
    linesPerBeat_ = lpb > 0 ? lpb : 4;
    RecalcTempo();
}

void Sequencer::RecalcTempo() {
    // BPM = musical beats per minute
    // LPB = rows (lines) per beat
    // rows_per_second = BPM * LPB / 60
    // ticks_per_second = rows_per_second * ticksPerRow
    double rowsPerSecond = (bpm_ * linesPerBeat_) / 60.0;
    double ticksPerSecond = rowsPerSecond * ticksPerRow_;
    samplesPerTick_ = outputRate_ / ticksPerSecond;
}

void Sequencer::SetChannelType(int channel, int type) {
    if (channel >= 0 && channel < MAX_CHANNELS)
        channelTypes_[channel] = (ChannelType)type;
}

void Sequencer::SetEventString(int patternID, int row, int channel, const char* str) {
    if (patternID < 0 || patternID >= MAX_PATTERNS) return;
    if (row < 0 || row >= MAX_ROWS) return;
    if (channel < 0 || channel >= MAX_CHANNELS) return;
    strncpy(patterns_[patternID].eventStrings[row][channel], str, 63);
    patterns_[patternID].eventStrings[row][channel][63] = '\0';
}

void Sequencer::SetChannelCount(int count) {
    channelCount_ = std::max(1, std::min(count, MAX_CHANNELS));
}

void Sequencer::SetPatternData(int patternID, const TrackerCell* cells, int rowCount, int channelCount) {
    if (patternID < 0 || patternID >= MAX_PATTERNS) return;

    Pattern& pat = patterns_[patternID];
    pat.rowCount = std::min(rowCount, MAX_ROWS);
    pat.channelCount = std::min(channelCount, MAX_CHANNELS);

    // cells is a flat array: [row * channelCount + channel]
    for (int r = 0; r < pat.rowCount; r++) {
        for (int c = 0; c < pat.channelCount; c++) {
            pat.cells[r][c] = cells[r * channelCount + c];
        }
    }

    if (patternID >= patternCount_)
        patternCount_ = patternID + 1;
}

void Sequencer::SetOrderList(const int* order, int length) {
    orderLength_ = std::min(length, MAX_ORDER);
    for (int i = 0; i < orderLength_; i++)
        orderList_[i] = order[i];
}

void Sequencer::MuteChannel(int channel, bool muted) {
    if (channel >= 0 && channel < MAX_CHANNELS) {
        channels_[channel].muted = muted;
        if (muted && channels_[channel].activeVoiceID >= 0) {
            channels_[channel].pendingNoteOff = true;
        }
    }
}

void Sequencer::SetChannelVolume(int channel, float vol) {
    if (channel >= 0 && channel < MAX_CHANNELS)
        channels_[channel].volume = std::clamp(vol, 0.0f, 1.0f);
}

void Sequencer::SetChannelPan(int channel, float pan) {
    if (channel >= 0 && channel < MAX_CHANNELS)
        channels_[channel].pan = std::clamp(pan, -1.0f, 1.0f);
}

void Sequencer::Play(int fromOrder, int fromRow) {
    if (orderLength_ <= 0) return;
    orderPosition_ = std::max(0, std::min(fromOrder, orderLength_ - 1));
    currentRow_ = fromRow;
    currentTick_ = 0;
    sampleCountdown_ = samplesPerTick_;
    patternBreak_ = false;
    patternJump_ = false;

    // Reset channel playback state but preserve config (mute, volume, pan)
    for (int i = 0; i < MAX_CHANNELS; i++) {
        bool wasMuted = channels_[i].muted;
        float vol = channels_[i].volume;
        float pan = channels_[i].pan;
        channels_[i] = ChannelState{};
        channels_[i].muted = wasMuted;
        channels_[i].volume = vol;
        channels_[i].pan = pan;
    }

    playing_ = true;
    currentTick_ = 0;
    firstRowPending_ = true;
    PushEvent(ZTrackerEventType::SONG_STARTED);
}

void Sequencer::Stop() {
    playing_ = false;
    PushEvent(ZTrackerEventType::SONG_STOPPED);
}

const Pattern& Sequencer::CurrentPattern() const {
    int patIdx = orderList_[orderPosition_];
    if (patIdx < 0 || patIdx >= MAX_PATTERNS)
        return patterns_[0];
    return patterns_[patIdx];
}

void Sequencer::ProcessRow(VoicePlayer& vp) {
    const Pattern& pat = CurrentPattern();
    if (currentRow_ >= pat.rowCount) return;

    int patIdx = orderList_[orderPosition_];
    PushEvent(ZTrackerEventType::ROW_CHANGED, patIdx, currentRow_);

    // Beat tick
    if (beatTickInterval_ > 0 && (currentRow_ % beatTickInterval_) == 0)
        PushEvent(ZTrackerEventType::BEAT_TICK, patIdx, currentRow_);

    for (int ch = 0; ch < channelCount_; ch++) {
        const TrackerCell& cell = pat.cells[currentRow_][ch];

        // Reset per-row flags
        channels_[ch].portaActive = false;

        // Event track channels — fire string event, skip audio
        if (channelTypes_[ch] == ChannelType::EVENT_TRACK) {
            const char* str = pat.eventStrings[currentRow_][ch];
            if (str[0] != '\0') {
                ZTrackerEvent evt = {};
                evt.type = ZTrackerEventType::EVENT_TRACK_FIRED;
                evt.samplePosition = totalSamples_;
                evt.patternIndex = patIdx;
                evt.rowIndex = currentRow_;
                evt.channelIndex = ch;
                strncpy(evt.stringPayload, str, 63);
                evt.stringPayload[63] = '\0';
                if (eventQueue_) eventQueue_->Push(evt);
            }
            continue; // skip audio processing for this channel
        }

        // Process row-based effects first
        ProcessRowEffects(vp, ch, cell);

        if (channels_[ch].muted) continue;

        // Note off
        if (cell.note == NOTE_OFF_VALUE) {
            if (channels_[ch].activeVoiceID >= 0) {
                vp.NoteOff(channels_[ch].activeVoiceID);
                channels_[ch].activeVoiceID = -1;
                PushEvent(ZTrackerEventType::CHANNEL_NOTE_OFF, patIdx, currentRow_, ch);
            }
            continue;
        }

        // Note on / glide to note
        if (cell.note >= 0 && cell.note < NOTE_OFF_VALUE) {

            // Glide to note (command 03): don't trigger new note, set glide target
            if (cell.effectCmd == 0x03 && channels_[ch].activeVoiceID >= 0) {
                // If not already gliding, init portaPitch from current note
                if (channels_[ch].portaTarget < 0)
                    channels_[ch].portaPitch = (double)channels_[ch].lastNote;
                channels_[ch].portaTarget = cell.note;
                // Speed is set in ProcessRowEffects (handles G00 = continue)
                continue; // skip NoteOn — voice keeps playing, pitch will glide
            }

            // Translate the cell's *user* instrument index into the native
            // slot band. `activePreset` (set by the I command) selects the
            // variant: 0 = Base, N = preset N-1. Out-of-range falls back to
            // Base. Unregistered user indices pass through (back-compat).
            int userInst = cell.instrument >= 0 ? cell.instrument : channels_[ch].lastInstrument;
            if (userInst < 0) continue;

            int inst;
            if (userInst >= 0 && userInst < MAX_INSTRUMENTS && presetBaseSlot_[userInst] >= 0) {
                int preset = channels_[ch].activePreset;
                int base   = presetBaseSlot_[userInst];
                int count  = presetVariantCount_[userInst];
                if (preset > 0 && preset <= count)
                    inst = base + preset; // variant slot (preset N lives at base + N)
                else
                    inst = base;          // Base slot
            } else {
                inst = userInst;          // no map registered — pass through
            }

            // Kit instruments: optionally let drums overlap on same track
            bool skipCut = false;
            if (inst >= 0 && inst < MAX_INSTRUMENTS &&
                vp.instrumentTypes_[inst] == VoiceType::KIT) {
                skipCut = vp.kitInstruments_[inst].overlapOnSameTrack;
            }
            if (channels_[ch].activeVoiceID >= 0 && !skipCut) {
                vp.NoteOff(channels_[ch].activeVoiceID);
            }

            float vel = (cell.volume != 0xFF) ? (cell.volume / 64.0f) : 1.0f;
            vel = std::min(vel, 1.0f);

            int voiceID = vp.NoteOn(inst, cell.note, vel, channels_[ch].volume, channels_[ch].pan);
            channels_[ch].activeVoiceID = voiceID;
            channels_[ch].lastNote = cell.note;
            // Remember the *user* index, not the resolved slot, so subsequent
            // I-command changes re-resolve against Base on the next note-on
            // even when the cell has no instrument column set.
            channels_[ch].lastInstrument = userInst;
            channels_[ch].portaPitch = (double)cell.note;
            PushEvent(ZTrackerEventType::CHANNEL_NOTE_ON, patIdx, currentRow_, ch, cell.note, userInst);
        }
    }
}

void Sequencer::ProcessRowEffects(VoicePlayer& vp, int channel, const TrackerCell& cell) {
    uint8_t cmd = cell.effectCmd;
    uint8_t param = cell.effectParam;
    auto& ch = channels_[channel];

    switch (cmd) {
        case 0x01: // Pitch slide up
            ch.slideSpeed = param * 0.0001;
            break;
        case 0x02: // Pitch slide down
            ch.slideSpeed = -(param * 0.0001);
            break;
        case 0x03: // Glide to note
            // Note target is set in ProcessRow (before effects) — see the glide check there
            // param 00 = continue with last speed, otherwise set new speed
            if (param > 0)
                ch.portaSpeed = param / 16.0; // xx/16 semitones per row (10 = 1 semitone/row)
            ch.portaActive = true;
            break;
        case 0x04: // Vibrato
            ch.vibratoSpeed = ((param >> 4) & 0x0F) * 0.5f;
            ch.vibratoDepth = (param & 0x0F) * 4.0f; // cents
            break;
        case 0x07: // Tremolo
            ch.tremoloSpeed = ((param >> 4) & 0x0F) * 0.5f;
            ch.tremoloDepth = (param & 0x0F) / 15.0f;
            break;
        case 0x08: // Pan
            ch.pan = (param / 255.0f) * 2.0f - 1.0f;
            break;
        case 0x0A: { // Volume slide
            uint8_t up = (param >> 4) & 0x0F;
            uint8_t down = param & 0x0F;
            ch.volSlideUp = up / 64.0f;
            ch.volSlideDown = down / 64.0f;
            break;
        }
        case 0x0B: // Pattern Jump
            patternJump_ = true;
            patternJumpOrder_ = param;
            break;
        case 0x0C: // Set Volume
            ch.volume = param / 64.0f;
            if (ch.activeVoiceID >= 0)
                vp.voices_[ch.activeVoiceID].channelVolume = ch.volume;
            break;
        case 0x0D: // Pattern Break
            patternBreak_ = true;
            patternBreakRow_ = param;
            break;
        case 0x0F: // Set Tempo/BPM
            if (param < 32)
                ticksPerRow_ = param > 0 ? param : 1;
            else
                bpm_ = param;
            RecalcTempo();
            PushEvent(ZTrackerEventType::TEMPO_CHANGED, -1, -1, -1, -1, -1, bpm_);
            break;
        case 16: { // G: Set macro (param = xx, 00-FF scaled to 0-1 for macro 0)
            // Use high nibble for macro index, low nibble for value
            int macroIdx = (param >> 4) & 0x0F;
            float val = (param & 0x0F) / 15.0f;
            if (macroIdx < ChannelState::MAX_MACROS) {
                ch.macroValue[macroIdx] = val;
                ch.macroTarget[macroIdx] = val;
            }
            break;
        }
        case 17: { // H: Slide macro to value (high nibble = macro index, low nibble = target value)
            int macroIdx = (param >> 4) & 0x0F;
            float target = (param & 0x0F) / 15.0f;
            if (macroIdx < ChannelState::MAX_MACROS) {
                ch.macroTarget[macroIdx] = target;
                ch.macroSlideSpeed[macroIdx] = 1.0f; // reach target in 1 row
            }
            break;
        }
        case 18: // I: Set preset
            ch.activePreset = param;
            break;
    }

    // Reset per-tick state for commands that don't set them
    if (cmd != 0x01 && cmd != 0x02) ch.slideSpeed = 0.0f;
    if (cmd != 0x04) { /* keep vibrato running until explicitly changed */ }
    if (cmd != 0x07) { /* keep tremolo running */ }
    if (cmd != 0x0A) { ch.volSlideUp = 0; ch.volSlideDown = 0; }
}

void Sequencer::ProcessTickEffects(VoicePlayer& vp, int channel, const TrackerCell& cell) {
    auto& ch = channels_[channel];
    int vid = ch.activeVoiceID;
    if (vid < 0 || vid >= MAX_VOICES || !vp.voices_[vid].active) return;
    auto& voice = vp.voices_[vid];
    voice.samplesPerTick = samplesPerTick_;

    // Pitch slide (01/02) — not active during portamento
    if (ch.slideSpeed != 0.0f && !ch.portaActive) {
        // Accumulate slide as a ratio modifier
        double currentMod = 1.0;
        for (int m = 0; m < MAX_PARAM_MODS; m++) {
            if (voice.pitchMods.mods[m].active && voice.pitchMods.mods[m].source == MOD_SEQ_SLIDE) {
                currentMod = voice.pitchMods.mods[m].value;
                break;
            }
        }
        currentMod += ch.slideSpeed;
        if (currentMod < 0.01) currentMod = 0.01;
        voice.pitchMods.Set(MOD_SEQ_SLIDE, currentMod);
    }

    // Glide to note (portamento) — only on rows with active 03xx command
    if (ch.portaActive && ch.portaTarget >= 0 && ch.portaSpeed > 0.0) {
        double target = (double)ch.portaTarget;
        double dist = target - ch.portaPitch;
        double speedPerTick = ch.portaSpeed / (double)ticksPerRow_;

        // Snap if close enough or one step would pass it
        if (fabs(dist) <= speedPerTick + 0.001) {
            ch.portaPitch = target;
            ch.portaTarget = -1;
        } else {
            ch.portaPitch += (dist > 0 ? speedPerTick : -speedPerTick);
        }

        // Set pitch modifier as ratio from original note to current portaPitch
        double semitoneDiff = ch.portaPitch - (double)voice.midiNote;
        double ratio = pow(2.0, semitoneDiff / 12.0);
        voice.pitchMods.Set(MOD_SEQ_GLIDE, ratio);
    }

    // Vibrato (sequencer-level, from 04xy command)
    if (ch.vibratoDepth > 0.0f && ch.vibratoSpeed > 0.0f) {
        ch.vibratoPhase += ch.vibratoSpeed * 0.1f;
        float vibCents = sinf(ch.vibratoPhase) * ch.vibratoDepth;
        float vibMul = powf(2.0f, vibCents / 1200.0f);
        voice.pitchMods.Set(MOD_SEQ_VIBRATO, (double)vibMul);
    }

    // Tremolo
    if (ch.tremoloDepth > 0.0f && ch.tremoloSpeed > 0.0f) {
        ch.tremoloPhase += ch.tremoloSpeed * 0.1f;
        float trem = sinf(ch.tremoloPhase) * ch.tremoloDepth;
        voice.volumeMods.Set(MOD_SEQ_TREMOLO, (double)std::max(0.0f, 1.0f + trem));
    }

    // Volume slide
    if (ch.volSlideUp > 0)
        ch.volume = std::min(1.0f, ch.volume + ch.volSlideUp);
    else if (ch.volSlideDown > 0)
        ch.volume = std::max(0.0f, ch.volume - ch.volSlideDown);
    if (ch.volSlideUp > 0 || ch.volSlideDown > 0)
        voice.channelVolume = ch.volume;

    // Macro slides
    for (int m = 0; m < ChannelState::MAX_MACROS; m++) {
        if (ch.macroSlideSpeed[m] > 0.0f) {
            float diff = ch.macroTarget[m] - ch.macroValue[m];
            float step = ch.macroSlideSpeed[m] / (float)ticksPerRow_;
            if (fabs(diff) <= step)
                ch.macroValue[m] = ch.macroTarget[m];
            else
                ch.macroValue[m] += (diff > 0 ? step : -step);
        }
    }
}

void Sequencer::AdvanceToNextRow(VoicePlayer& vp) {
    // Handle pattern break / jump
    if (patternJump_) {
        orderPosition_ = std::min(patternJumpOrder_, orderLength_ - 1);
        currentRow_ = patternBreak_ ? patternBreakRow_ : 0;
        patternJump_ = false;
        patternBreak_ = false;
        return;
    }
    if (patternBreak_) {
        orderPosition_++;
        if (orderPosition_ >= orderLength_)
            orderPosition_ = 0;
        currentRow_ = patternBreakRow_;
        patternBreak_ = false;
        return;
    }

    int prevOrder = orderPosition_;
    currentRow_++;
    const Pattern& pat = CurrentPattern();
    if (currentRow_ >= pat.rowCount) {
        currentRow_ = 0;
        orderPosition_++;
        if (orderPosition_ >= orderLength_) {
            orderPosition_ = 0;
            PushEvent(ZTrackerEventType::SONG_LOOPED);
        }
        PushEvent(ZTrackerEventType::PATTERN_CHANGED, orderList_[orderPosition_], 0);
    }
}

void Sequencer::AdvanceTick(VoicePlayer& vp) {
    currentTick_++;
    if (currentTick_ >= ticksPerRow_) {
        currentTick_ = 0;
        AdvanceToNextRow(vp);
        ProcessRow(vp);
    }

    // Process per-tick effects on ALL ticks (including tick 0)
    const Pattern& pat = CurrentPattern();
    if (currentRow_ < pat.rowCount) {
        for (int ch = 0; ch < channelCount_; ch++) {
            ProcessTickEffects(vp, ch, pat.cells[currentRow_][ch]);
        }
    }
}

void Sequencer::Process(VoicePlayer& vp, float* outL, float* outR, int numFrames) {
    // Drain pending mute note-offs
    for (int ch = 0; ch < channelCount_; ch++) {
        if (channels_[ch].pendingNoteOff && channels_[ch].activeVoiceID >= 0) {
            vp.NoteOff(channels_[ch].activeVoiceID);
            channels_[ch].activeVoiceID = -1;
            channels_[ch].pendingNoteOff = false;
        }
    }

    if (!playing_) {
        vp.Process(outL, outR, numFrames);
        return;
    }

    // Process the first row immediately on play start
    if (firstRowPending_) {
        firstRowPending_ = false;
        ProcessRow(vp);
    }

    int framesProcessed = 0;

    while (framesProcessed < numFrames) {
        int framesUntilTick = (int)sampleCountdown_;
        if (framesUntilTick < 1) framesUntilTick = 1;

        int framesToProcess = std::min(framesUntilTick, numFrames - framesProcessed);

        vp.Process(outL + framesProcessed, outR + framesProcessed, framesToProcess);

        framesProcessed += framesToProcess;
        sampleCountdown_ -= framesToProcess;
        totalSamples_ += framesToProcess;

        if (sampleCountdown_ <= 0.0) {
            AdvanceTick(vp);
            sampleCountdown_ += samplesPerTick_;
        }
    }
}

void Sequencer::PushEvent(ZTrackerEventType type, int patIdx, int row,
                           int ch, int note, int inst, int iparam, float fparam) {
    if (!eventQueue_) return;
    ZTrackerEvent evt = {};
    evt.type = type;
    evt.samplePosition = totalSamples_;
    evt.patternIndex = patIdx;
    evt.rowIndex = row;
    evt.channelIndex = ch;
    evt.noteValue = note;
    evt.instrumentID = inst;
    evt.intParam = iparam;
    evt.floatParam = fparam;
    eventQueue_->Push(evt);
}
