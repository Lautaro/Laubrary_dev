// PyrePrepassCache<T> — a deterministic one-shot pre-pass a form runs ONCE per authoring state, not per frame.
//
// Auto-fit scans, layout solves, raster→geometry snapshots, baked LUTs, clip-level statistics: none of them is
// state (they depend only on the spec), but all of them are too slow to redo every frame. This is the invalidation
// half of the fire harness (a ConditionalWeakTable keyed by identity + a content hash) WITHOUT the replay half.
//
//   static readonly PyrePrepassCache<MyLayout> _layout = new PyrePrepassCache<MyLayout>();
//   var layout = _layout.Get(ctx, this, () => SolveLayout(...));
//
// Key = (this form instance's identity) × (form.ContentHash() — every serialized dial) × (W, H, frameCount, seed,
// layerSalt) × extraHash. Any edit anywhere in that tuple builds a fresh value; an unchanged tuple returns the SAME
// object on every frame. The identity key means a duplicated layer (a fresh form object) gets its own entry and a
// deleted layer's entry is collected with it. Deterministic by construction: `build` may only read the ctx and the
// form's fields (the same rule as Render), so a cold editor and a warm one produce identical values.
//
// Thread-safe: frames of one clip render concurrently, each worker on its own clone of the form (PyreFrameFill).
// Identity is `form.PrepassIdentity`, so a render clone shares its origin's entry; an entry holds an immutable
// (key, value) snapshot swapped atomically, and a miss builds under the entry's lock — N workers missing the same
// key at once build it ONCE and the rest wait for that result instead of each solving the pre-pass again.
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Laubrary.Pyre
{
    public sealed class PyrePrepassCache<T> where T : class
    {
        sealed class Snapshot { public readonly int key; public readonly T value; public Snapshot(int k, T v) { key = k; value = v; } }
        sealed class Entry { public volatile Snapshot snap; }
        readonly ConditionalWeakTable<PyreForm, Entry> _entries = new ConditionalWeakTable<PyreForm, Entry>();
        int _hits, _misses;

        public int Hits => _hits;
        public int Misses => _misses;

        /// The cached value for (form, ctx, extraHash), building it with `build` on a miss.
        public T Get(in PyreFormCtx ctx, PyreForm form, Func<T> build, int extraHash = 0)
        {
            int key = KeyOf(ctx, form, extraHash);
            var e = _entries.GetOrCreateValue(form.PrepassIdentity);
            var s = e.snap;
            if (s != null && s.key == key) { Interlocked.Increment(ref _hits); return s.value; }
            lock (e)
            {
                s = e.snap;   // another thread may have built it while we waited
                if (s != null && s.key == key) { Interlocked.Increment(ref _hits); return s.value; }
                Interlocked.Increment(ref _misses);
                var v = build();
                e.snap = new Snapshot(key, v);
                return v;
            }
        }

        /// Drop the entry for one form (the next Get rebuilds).
        public void Invalidate(PyreForm form) => _entries.Remove(form.PrepassIdentity);

        /// The hash Get keys by — exposed so a form can key its own structures the same way.
        public static int KeyOf(in PyreFormCtx ctx, PyreForm form, int extraHash = 0)
        {
            unchecked
            {
                int h = form != null ? form.ContentHash() : 0;
                h = (h ^ ctx.W) * 16777619; h = (h ^ ctx.H) * 16777619;
                h = (h ^ ctx.frameCount) * 16777619; h = (h ^ ctx.seed) * 16777619;
                h = (h ^ ctx.layerSalt) * 16777619; h = (h ^ extraHash) * 16777619;
                return h;
            }
        }
    }
}
