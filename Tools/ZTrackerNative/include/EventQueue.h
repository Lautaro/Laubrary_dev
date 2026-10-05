#pragma once

#include <cstdint>
#include <atomic>
#include <cstring>

enum class ZTrackerEventType : uint8_t {
    ROW_CHANGED,
    PATTERN_CHANGED,
    SONG_STARTED,
    SONG_STOPPED,
    SONG_LOOPED,
    CHANNEL_NOTE_ON,
    CHANNEL_NOTE_OFF,
    TEMPO_CHANGED,
    EVENT_TRACK_FIRED,
    BEAT_TICK
};

struct ZTrackerEvent {
    ZTrackerEventType type;
    uint64_t samplePosition;
    int32_t  patternIndex;
    int32_t  rowIndex;
    int32_t  channelIndex;
    int32_t  noteValue;
    int32_t  instrumentID;
    int32_t  intParam;
    float    floatParam;
    char     stringPayload[64];
};

// Lock-free single-producer single-consumer ring buffer.
// Producer: audio thread (Push). Consumer: main thread (Pop).
class EventQueue {
public:
    void Init() {
        head_.store(0, std::memory_order_relaxed);
        tail_.store(0, std::memory_order_relaxed);
    }

    bool Push(const ZTrackerEvent& evt) {
        uint32_t h = head_.load(std::memory_order_relaxed);
        uint32_t next = (h + 1) % CAPACITY;
        // If full, drop the event (audio thread must never block)
        if (next == tail_.load(std::memory_order_acquire))
            return false;
        buffer_[h] = evt;
        head_.store(next, std::memory_order_release);
        return true;
    }

    bool Pop(ZTrackerEvent& out) {
        uint32_t t = tail_.load(std::memory_order_relaxed);
        if (t == head_.load(std::memory_order_acquire))
            return false;
        out = buffer_[t];
        tail_.store((t + 1) % CAPACITY, std::memory_order_release);
        return true;
    }

    void Flush() {
        tail_.store(head_.load(std::memory_order_acquire), std::memory_order_release);
    }

private:
    static constexpr uint32_t CAPACITY = 4096;
    ZTrackerEvent buffer_[CAPACITY];
    std::atomic<uint32_t> head_;
    std::atomic<uint32_t> tail_;
};
