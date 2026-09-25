using System.Threading;

namespace Laubrary.Zounds.Dsp {

    public enum ZoundDspEventType {
        AudioEnd = 0,
        VoiceStolen = 1,
        SourceSlotStolen = 2,
        PlayDropped = 3,
        ChainOverrun = 4,
    }

    public struct ZoundDspEvent {
        public ZoundDspEventType type;
        public int nodeId;      // voice or group slot index
        public long tokenId;    // monotonic, assigned on the main thread at allocation
        public long dspSample;
    }

    /// <summary>
    /// Lock-free single-producer single-consumer ring over a preallocated struct array. The audio
    /// thread produces, the main thread consumes. Never blocks, never allocates; drops on full and
    /// counts the drops.
    /// </summary>
    public sealed class ZoundDspEventRing {
        private readonly ZoundDspEvent[] events = new ZoundDspEvent[ZoundDspConstants.EVENT_RING_SIZE];
        private int head; // next write (producer)
        private int tail; // next read (consumer)
        internal long dropped;

        public bool TryPush(in ZoundDspEvent e) {
            int h = head;
            int next = (h + 1) & (ZoundDspConstants.EVENT_RING_SIZE - 1);
            if (next == Volatile.Read(ref tail)) { dropped++; return false; }
            events[h] = e;
            Volatile.Write(ref head, next);
            return true;
        }

        public bool TryPop(out ZoundDspEvent e) {
            int t = tail;
            if (t == Volatile.Read(ref head)) { e = default; return false; }
            e = events[t];
            Volatile.Write(ref tail, (t + 1) & (ZoundDspConstants.EVENT_RING_SIZE - 1));
            return true;
        }

        public long Dropped => dropped;
    }

}
