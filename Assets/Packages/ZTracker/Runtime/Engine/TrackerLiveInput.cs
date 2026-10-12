using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Laubrary.ZTracker.Engine
{
    public enum TrackerLiveKind : byte { None, NoteOn, NoteOff, Sustain, AllNotesOff, AllSoundOff, PitchBend, Macro }

    /// <summary>
    /// One live performance event (a MIDI key, pedal, wheel or panic) on its way to the renderer. Fixed size, no references,
    /// so any thread can copy it into the live ring without allocating.
    /// </summary>
    public struct TrackerLiveEvent
    {
        /// <summary>Stopwatch ticks when the event arrived (diagnostics and latency measurement only).</summary>
        public long stamp;
        /// <summary>Assigned by the ring when accepted (1, 2, 3...); the renderer publishes the last one it applied.</summary>
        public long serial;
        public short track, column, instrument;
        public TrackerLiveKind kind;
        /// <summary>Note number 0..119 after octave shift (note on/off); macro index 0..7 (macro).</summary>
        public byte note;
        /// <summary>Raw MIDI velocity 1..127, used for velocity zones.</summary>
        public byte velocity;
        /// <summary>Gain 0..1 (note on), semitones (pitch bend), 0/1 (sustain), normalized 0..1 (macro).</summary>
        public float value;

        public static TrackerLiveEvent NoteOn(int track, int cursorColumn, int instrument, int note, int velocity, float gain)
            => new TrackerLiveEvent { kind = TrackerLiveKind.NoteOn, track = (short)track, column = (short)cursorColumn, instrument = (short)instrument, note = (byte)note, velocity = (byte)velocity, value = gain };
        public static TrackerLiveEvent NoteOff(int note) => new TrackerLiveEvent { kind = TrackerLiveKind.NoteOff, note = (byte)note };
        public static TrackerLiveEvent Sustain(bool down) => new TrackerLiveEvent { kind = TrackerLiveKind.Sustain, value = down ? 1 : 0 };
        public static TrackerLiveEvent AllNotesOff() => new TrackerLiveEvent { kind = TrackerLiveKind.AllNotesOff };
        public static TrackerLiveEvent AllSoundOff() => new TrackerLiveEvent { kind = TrackerLiveKind.AllSoundOff };
        public static TrackerLiveEvent PitchBend(float semitones) => new TrackerLiveEvent { kind = TrackerLiveKind.PitchBend, value = semitones };
        public static TrackerLiveEvent Macro(int instrument, int macro, float normalized) => new TrackerLiveEvent { kind = TrackerLiveKind.Macro, instrument = (short)instrument, note = (byte)macro, value = normalized };
    }

    /// <summary>One held (or pedal-sustained) live note, indexed by note number.</summary>
    public struct TrackerLiveSlot
    {
        public long cohort, order;
        public int track, column;
        public byte active, held;
    }

    /// <summary>
    /// Bounded multi-producer / single-consumer ring from input threads to the renderer, plus the live-note table the
    /// renderer keeps. Producers never block: a full ring drops the NEWEST event and counts it; the renderer then releases
    /// every live note so a lost key-up can never leave a note stuck. The consumer only reads and writes plain memory, so
    /// it runs inside the compiled audio callback without touching the managed runtime. Storage is owned by the host
    /// (generator or offline engine) and freed with its other render-side memory, after the renderer is quiet.
    /// </summary>
    public unsafe struct TrackerLiveRing : IDisposable
    {
        public const int NoteSlots = 128;
        // Control words: 0 enqueue position, 1 dequeue position, 2 dropped (full), 3 serial counter,
        // 4 last applied serial, 5 drains with at least one event, 6 dropped count already handled by the renderer,
        // 7 blocks drained (every render call), 8 live order counter, 9 sustain pedal (0/1), 10 pitch bend (float bits).
        const int Words = 16;
        [NativeDisableUnsafePtrRestriction] public long* control;
        [NativeDisableUnsafePtrRestriction] public long* sequence;
        [NativeDisableUnsafePtrRestriction] public TrackerLiveEvent* cells;
        [NativeDisableUnsafePtrRestriction] public TrackerLiveSlot* slots;
        public int capacity, mask;
        public bool IsCreated => control != null;

        public static TrackerLiveRing Create(int capacity = 1024)
        {
            if (capacity < 2 || capacity > 65536 || (capacity & (capacity - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two, 2..65536");
            var r = new TrackerLiveRing { capacity = capacity, mask = capacity - 1 };
            r.control = (long*)UnsafeUtility.Malloc(Words * sizeof(long), 64, Allocator.Persistent);
            UnsafeUtility.MemClear(r.control, Words * sizeof(long));
            r.sequence = (long*)UnsafeUtility.Malloc(capacity * (long)sizeof(long), 64, Allocator.Persistent);
            for (int i = 0; i < capacity; i++) r.sequence[i] = i;
            r.cells = (TrackerLiveEvent*)UnsafeUtility.Malloc(capacity * (long)sizeof(TrackerLiveEvent), 16, Allocator.Persistent);
            UnsafeUtility.MemClear(r.cells, capacity * (long)sizeof(TrackerLiveEvent));
            r.slots = (TrackerLiveSlot*)UnsafeUtility.Malloc(NoteSlots * (long)sizeof(TrackerLiveSlot), 16, Allocator.Persistent);
            UnsafeUtility.MemClear(r.slots, NoteSlots * (long)sizeof(TrackerLiveSlot));
            return r;
        }

        // ---- Producer side: any thread, wait-free apart from a bounded compare-and-swap retry between producers ----
        public bool TryWrite(TrackerLiveEvent value)
        {
            if (control == null) return false;
            long pos = Volatile.Read(ref control[0]);
            while (true)
            {
                long seq = Volatile.Read(ref sequence[pos & mask]);
                long diff = seq - pos;
                if (diff == 0)
                {
                    long seen = Interlocked.CompareExchange(ref control[0], pos + 1, pos);
                    if (seen == pos) break;
                    pos = seen;
                }
                else if (diff < 0) { Interlocked.Increment(ref control[2]); return false; }
                else pos = Volatile.Read(ref control[0]);
            }
            value.serial = Interlocked.Increment(ref control[3]);
            cells[pos & mask] = value;
            Volatile.Write(ref sequence[pos & mask], pos + 1);
            return true;
        }

        // ---- Consumer side: the renderer only ----
        public bool TryRead(out TrackerLiveEvent value)
        {
            long pos = control[1];
            long seq = Volatile.Read(ref sequence[pos & mask]);
            if (seq != pos + 1) { value = default; return false; }
            value = cells[pos & mask];
            Volatile.Write(ref sequence[pos & mask], pos + capacity);
            control[1] = pos + 1;
            return true;
        }

        // ---- Observation (any thread) ----
        public long Dropped => control == null ? 0 : Volatile.Read(ref control[2]);
        public long Written => control == null ? 0 : Volatile.Read(ref control[3]);
        public long LastAppliedSerial => control == null ? 0 : Volatile.Read(ref control[4]);
        public long BlocksDrained => control == null ? 0 : Volatile.Read(ref control[7]);
        public int HeldNotes { get { if (slots == null) return 0; int n = 0; for (int i = 0; i < NoteSlots; i++) if (Volatile.Read(ref slots[i].active) != 0) n++; return n; } }
        public bool SustainDown => control != null && Volatile.Read(ref control[9]) != 0;

        public void Dispose()
        {
            if (control != null) UnsafeUtility.Free(control, Allocator.Persistent);
            if (sequence != null) UnsafeUtility.Free(sequence, Allocator.Persistent);
            if (cells != null) UnsafeUtility.Free(cells, Allocator.Persistent);
            if (slots != null) UnsafeUtility.Free(slots, Allocator.Persistent);
            this = default;
        }
    }

    /// <summary>
    /// The producer-facing door to one renderer's live ring. Input threads write through it; the owning host closes it
    /// before the ring's memory can be freed. Closing waits only for writes already inside the door (a few hundred
    /// nanoseconds); writes that start after closing are refused, so a freed ring is never touched.
    /// </summary>
    public sealed class TrackerLiveTarget
    {
        TrackerLiveRing ring;
        int inFlight, open;
        /// <summary>Changes every time a host opens a door, so a note-off can tell its note's renderer was replaced.</summary>
        public readonly int Generation;
        static int generations;
        internal TrackerLiveTarget(TrackerLiveRing ring) { this.ring = ring; open = 1; Generation = Interlocked.Increment(ref generations); }
        public bool IsOpen => Volatile.Read(ref open) != 0;
        public bool Write(TrackerLiveEvent value)
        {
            Interlocked.Increment(ref inFlight);
            try
            {
                if (Volatile.Read(ref open) == 0) return false;
                value.stamp = value.stamp != 0 ? value.stamp : System.Diagnostics.Stopwatch.GetTimestamp();
                return ring.TryWrite(value);
            }
            finally { Interlocked.Decrement(ref inFlight); }
        }
        /// <summary>Refuses new writes, then waits for writes already in progress. Main thread, before freeing the ring.</summary>
        internal void Close()
        {
            Volatile.Write(ref open, 0);
            var spin = new SpinWait();
            while (Volatile.Read(ref inFlight) != 0) spin.SpinOnce();
        }
        public long Dropped => IsOpen ? ring.Dropped : 0;
        public long Written => IsOpen ? ring.Written : 0;
        public long LastAppliedSerial => IsOpen ? ring.LastAppliedSerial : 0;
        public long BlocksDrained => IsOpen ? ring.BlocksDrained : 0;
        public int HeldNotes => IsOpen ? ring.HeldNotes : 0;
        public bool SustainDown => IsOpen && ring.SustainDown;
    }
}
