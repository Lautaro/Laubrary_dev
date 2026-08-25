// PyreLayerCache — rendered layer buffers by content key, with a memory cap and least-recently-used eviction.
//
// One entry per layer KEY (PyreLayerKey.LayerKey — the layer's content + index + the spec part), holding one slot
// per frame; a slot carries the frame-variant it was rendered under (PyreLayerKey.FrameVariant) so a fused or
// heightmap-dependent buffer is never mistaken for the plain one. Because the key is content-addressed, a buffer is
// valid for as long as it exists: toggling a layer off and on, undoing a dial, or moving a layer back all find
// their buffers again instead of rendering. Eviction is per entry, from the least-recently-used end, and never
// touches an entry used in the current generation (the layers of the spec being filled must all fit — the cap is a
// ceiling for what is KEPT, not a reason to thrash the live spec). Main-thread only; the buffers it hands out are
// shared read-only with worker threads, so nothing may write into one (the composer clones before a matte apply).
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre
{
    public sealed class PyreLayerCache
    {
        public sealed class Slot
        {
            public ulong variant;
            public Color32[] pixels;
            public Color32[] border;     // the over-matte border rim, or null
        }

        sealed class Entry
        {
            public ulong key;
            public Slot[] frames;
            public long bytes;
            public int generation;
            public LinkedListNode<Entry> node;
        }

        readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();
        readonly LinkedList<Entry> _lru = new LinkedList<Entry>();   // most recently used at the FRONT
        int _generation;

        /// Bytes of pixel data the cache may keep. Exceeded only while the live spec alone needs more.
        public long CapacityBytes { get; set; }
        public long UsedBytes { get; private set; }
        public int EntryCount => _entries.Count;
        public int Evictions { get; private set; }

        public PyreLayerCache(long capacityBytes = 64L << 20) { CapacityBytes = capacityBytes; }

        /// Start a new use generation: entries touched (TryGet / Put) from now on are protected from eviction
        /// until the next BeginGeneration. Call once per fill.
        public void BeginGeneration() => _generation++;

        /// The buffer of `key` for `frame` if it was rendered under `variant`. Touches the entry.
        public bool TryGet(ulong key, int frame, ulong variant, out Slot slot)
        {
            slot = null;
            if (!_entries.TryGetValue(key, out var e)) return false;
            Touch(e);
            if (frame < 0 || frame >= e.frames.Length) return false;
            var s = e.frames[frame];
            if (s == null || s.variant != variant) return false;
            slot = s;
            return true;
        }

        /// Store one rendered layer-frame. `frameCount` sizes a new entry; an existing entry keyed the same already
        /// has that size (the frame count is part of the key).
        public void Put(ulong key, int frameCount, int frame, ulong variant, Color32[] pixels, Color32[] border)
        {
            if (pixels == null || frame < 0 || frame >= frameCount) return;
            if (!_entries.TryGetValue(key, out var e))
            {
                e = new Entry { key = key, frames = new Slot[frameCount] };
                e.node = _lru.AddFirst(e);
                _entries[key] = e;
            }
            Touch(e);
            var old = e.frames[frame];
            if (old != null) { long ob = Bytes(old); e.bytes -= ob; UsedBytes -= ob; }
            var s = new Slot { variant = variant, pixels = pixels, border = border };
            e.frames[frame] = s;
            long nb = Bytes(s);
            e.bytes += nb; UsedBytes += nb;
            Evict();
        }

        public void Clear()
        {
            _entries.Clear(); _lru.Clear(); UsedBytes = 0;
        }

        void Touch(Entry e)
        {
            e.generation = _generation;
            if (_lru.First != e.node) { _lru.Remove(e.node); _lru.AddFirst(e.node); }
        }

        // Drop least-recently-used entries from the tail until under the cap, skipping this generation's.
        void Evict()
        {
            var n = _lru.Last;
            while (UsedBytes > CapacityBytes && n != null)
            {
                var prev = n.Previous;
                var e = n.Value;
                if (e.generation != _generation)
                {
                    _lru.Remove(n);
                    _entries.Remove(e.key);
                    UsedBytes -= e.bytes;
                    Evictions++;
                }
                n = prev;
            }
        }

        static long Bytes(Slot s) => 4L * ((s.pixels?.Length ?? 0) + (s.border?.Length ?? 0));
    }
}
