using System;
using System.Threading;
using Laubrary.ZTracker.Engine;

namespace Laubrary.ZTracker.Midi
{
    public enum ZTrackerVelocityCurve { Linear, Soft, Hard }

    /// <summary>
    /// A MIDI input: lists its devices, opens one, and hands every short message to a router on its own thread.
    /// Implementations must never allocate or lock per message and must close their device before scripts reload.
    /// </summary>
    public interface IZTrackerMidiSource : IDisposable
    {
        string SourceName { get; }
        /// <summary>Re-reads the device list. Returns true when it changed. Main thread.</summary>
        bool RefreshDevices();
        int DeviceCount { get; }
        string DeviceName(int index);
        bool Open(string device, ZTrackerMidiRouter router, out string error);
        void Close();
        bool IsOpen { get; }
        string OpenDevice { get; }
    }

    /// <summary>
    /// Turns raw MIDI messages into live engine events and sends them to the current target (a song track, or a clean
    /// preview channel). Runs on the MIDI input thread: no allocation, no locks, settings are read from single fields.
    /// Key-ups always go to the renderer that received the key-down, even after the target has moved.
    /// </summary>
    public sealed class ZTrackerMidiRouter
    {
        /// <summary>Where notes go: a renderer door plus the track, cursor column and instrument to play.</summary>
        public sealed class Route
        {
            public readonly TrackerLiveTarget target;
            public readonly int track, column, instrument;
            public Route(TrackerLiveTarget target, int track, int column, int instrument) { this.target = target; this.track = track; this.column = column; this.instrument = instrument; }
        }

        volatile Route route;
        volatile TrackerLiveTarget previous;
        volatile int channel = -1, octaveShift, modWheelMacro, curve;
        volatile float bendRange = 2;
        readonly TrackerLiveTarget[] noteOwner = new TrackerLiveTarget[128];
        readonly byte[] noteSent = new byte[128];
        readonly float[][] curves = new float[3][];
        long messages, notes, ignored;
        /// <summary>A key as the main thread sees it for recording: note after octave shift, velocity, gain, press time.</summary>
        public struct Captured { public long stamp; public int note, velocity; public float gain; public bool on; }
        readonly Captured[] captured = new Captured[512];
        long capturedWrite, capturedRead;
        volatile bool capturing;
        /// <summary>When on, every routed key-down and key-up is also queued for <see cref="TryReadCaptured"/> (recording).</summary>
        public bool Capturing { get => capturing; set { capturing = value; if (!value) Volatile.Write(ref capturedRead, Volatile.Read(ref capturedWrite)); } }
        /// <summary>Main thread: the next captured key, oldest first. A full queue drops the newest.</summary>
        public bool TryReadCaptured(out Captured value)
        {
            long r = capturedRead;
            if (r >= Volatile.Read(ref capturedWrite)) { value = default; return false; }
            value = captured[r % captured.Length]; Volatile.Write(ref capturedRead, r + 1); return true;
        }
        void Capture(int note, int velocity, float gain, bool on, long stamp)
        {
            long w = capturedWrite;
            if (w - Volatile.Read(ref capturedRead) >= captured.Length) return;
            captured[w % captured.Length] = new Captured { stamp = stamp, note = note, velocity = velocity, gain = gain, on = on };
            Volatile.Write(ref capturedWrite, w + 1);
        }
        volatile int lastNote = -1, lastVelocity, lastChannel;

        /// <summary>First knob controller number mapped to macro 1 (eight knobs cover macros 1..8).</summary>
        public const int KnobFirstController = 21;

        public ZTrackerMidiRouter()
        {
            for (int c = 0; c < 3; c++)
            {
                curves[c] = new float[128];
                for (int v = 0; v < 128; v++) { float x = v / 127f; curves[c][v] = c == 1 ? (float)Math.Sqrt(x) : c == 2 ? x * x : x; }
            }
        }

        // ---- Settings (main thread) ----
        /// <summary>-1 listens to every channel (omni); 0..15 listens to one.</summary>
        public int Channel { get => channel; set => channel = Math.Max(-1, Math.Min(15, value)); }
        public int OctaveShift { get => octaveShift; set => octaveShift = Math.Max(-4, Math.Min(4, value)); }
        public ZTrackerVelocityCurve VelocityCurve { get => (ZTrackerVelocityCurve)curve; set => curve = (int)value; }
        /// <summary>Pitch-wheel range in semitones each way (default 2).</summary>
        public float BendRange { get => bendRange; set => bendRange = Math.Max(0, Math.Min(24, value)); }
        /// <summary>Macro (0..7) the mod wheel drives on the target instrument.</summary>
        public int ModWheelMacro { get => modWheelMacro; set => modWheelMacro = Math.Max(0, Math.Min(7, value)); }
        public Route Current => route;

        /// <summary>Points new notes at a renderer door, track, cursor column and instrument. Held notes keep their own target.</summary>
        public void SetRoute(TrackerLiveTarget target, int track, int column, int instrument)
        {
            var now = route;
            if (now != null && now.target == target && now.track == track && now.column == column && now.instrument == instrument) return;
            if (now != null && now.target != target && now.target != null) previous = now.target;
            route = target == null ? null : new Route(target, track, column, instrument);
        }

        // ---- Diagnostics (any thread) ----
        public long Messages => Interlocked.Read(ref messages);
        public long Notes => Interlocked.Read(ref notes);
        public long Ignored => Interlocked.Read(ref ignored);
        public int LastNote => lastNote;
        public int LastVelocity => lastVelocity;
        public int LastChannel => lastChannel;

        /// <summary>Handles one packed short message (status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16). Called on the input thread.</summary>
        public void Receive(uint packed, long stamp)
        {
            Interlocked.Increment(ref messages);
            int status = (int)(packed & 0xFF), d1 = (int)((packed >> 8) & 0x7F), d2 = (int)((packed >> 16) & 0x7F);
            if (status < 0x80 || status >= 0xF0) { Interlocked.Increment(ref ignored); return; }
            int type = status & 0xF0, ch = status & 0x0F;
            int filter = channel;
            if (filter >= 0 && ch != filter) { Interlocked.Increment(ref ignored); return; }
            var r = route;
            switch (type)
            {
                case 0x90 when d2 > 0:
                {
                    int note = d1 + 12 * octaveShift;
                    if (r == null || note < 0 || note > 119) { Interlocked.Increment(ref ignored); return; }
                    // The same key pressed again before its key-up: end the earlier one first.
                    var earlier = noteOwner[d1];
                    if (earlier != null) { earlier.Write(Stamp(TrackerLiveEvent.NoteOff(noteSent[d1]), stamp)); noteOwner[d1] = null; }
                    var e = TrackerLiveEvent.NoteOn(r.track, r.column, r.instrument, note, d2, curves[curve][d2]);
                    if (r.target.Write(Stamp(e, stamp))) { noteOwner[d1] = r.target; noteSent[d1] = (byte)note; if (capturing) Capture(note, d2, e.value, true, stamp); }
                    Interlocked.Increment(ref notes); lastNote = note; lastVelocity = d2; lastChannel = ch;
                    return;
                }
                case 0x80:
                case 0x90:
                {
                    var owner = noteOwner[d1];
                    if (owner != null) { owner.Write(Stamp(TrackerLiveEvent.NoteOff(noteSent[d1]), stamp)); noteOwner[d1] = null; if (capturing) Capture(noteSent[d1], 0, 0, false, stamp); }
                    return;
                }
                case 0xB0:
                    switch (d1)
                    {
                        case 64: Broadcast(r, TrackerLiveEvent.Sustain(d2 >= 64), stamp); return;
                        case 120: Broadcast(r, TrackerLiveEvent.AllSoundOff(), stamp); ForgetNotes(); return;
                        case 123: Broadcast(r, TrackerLiveEvent.AllNotesOff(), stamp); ForgetNotes(); return;
                        case 1: if (r != null) r.target.Write(Stamp(TrackerLiveEvent.Macro(r.instrument, modWheelMacro, d2 / 127f), stamp)); return;
                    }
                    if (d1 >= KnobFirstController && d1 < KnobFirstController + 8 && r != null) { r.target.Write(Stamp(TrackerLiveEvent.Macro(r.instrument, d1 - KnobFirstController, d2 / 127f), stamp)); return; }
                    Interlocked.Increment(ref ignored); return;
                case 0xE0:
                {
                    int bend = (d2 << 7 | d1) - 8192;
                    float semitones = (bend >= 0 ? bend / 8191f : bend / 8192f) * bendRange;
                    Broadcast(r, TrackerLiveEvent.PitchBend(semitones), stamp); return;
                }
                default: Interlocked.Increment(ref ignored); return; // program change, aftertouch: not used
            }
        }

        static TrackerLiveEvent Stamp(TrackerLiveEvent e, long stamp) { e.stamp = stamp; return e; }

        void Broadcast(Route r, TrackerLiveEvent e, long stamp)
        {
            e.stamp = stamp;
            var a = r?.target; var b = previous;
            a?.Write(e);
            if (b != null && b != a) b.Write(e);
        }

        void ForgetNotes() { for (int i = 0; i < noteOwner.Length; i++) noteOwner[i] = null; }

        /// <summary>Silences every live note on every renderer that has one, immediately. Main thread or input thread.</summary>
        public void Panic()
        {
            long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            var r = route; var e = TrackerLiveEvent.AllSoundOff(); e.stamp = stamp;
            r?.target.Write(e); var b = previous; if (b != null && b != r?.target) b.Write(e);
            for (int i = 0; i < noteOwner.Length; i++) { var owner = noteOwner[i]; if (owner != null && owner != r?.target && owner != b) owner.Write(e); noteOwner[i] = null; }
        }
    }
}
