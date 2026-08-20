// PlusPrepassCache<T> — a deterministic one-shot pre-pass a form runs ONCE per authoring state, not per frame.
//
// Auto-fit scans, layout solves, raster→geometry snapshots, baked LUTs, clip-level statistics: none of them is
// state (they depend only on the spec), but all of them are too slow to redo every frame. This is the invalidation
// half of the fire harness (a ConditionalWeakTable keyed by identity + a content hash) WITHOUT the replay half.
//
//   static readonly PlusPrepassCache<MyLayout> _layout = new PlusPrepassCache<MyLayout>();
//   var layout = _layout.Get(ctx, this, () => SolveLayout(...));
//
// Key = (this form instance's identity) × (form.ContentHash() — every serialized dial) × (W, H, frameCount, seed,
// layerSalt) × extraHash. Any edit anywhere in that tuple builds a fresh value; an unchanged tuple returns the SAME
// object on every frame. The identity key means a duplicated layer (a fresh form object) gets its own entry and a
// deleted layer's entry is collected with it. Deterministic by construction: `build` may only read the ctx and the
// form's fields (the same rule as Render), so a cold editor and a warm one produce identical values.
using System;
using System.Runtime.CompilerServices;

namespace Laubrary.PyrePlus
{
    public sealed class PlusPrepassCache<T> where T : class
    {
        sealed class Entry { public int key; public T value; }
        readonly ConditionalWeakTable<PlusForm, Entry> _entries = new ConditionalWeakTable<PlusForm, Entry>();

        public int Hits { get; private set; }
        public int Misses { get; private set; }

        /// The cached value for (form, ctx, extraHash), building it with `build` on a miss.
        public T Get(in PlusFormCtx ctx, PlusForm form, Func<T> build, int extraHash = 0)
        {
            int key = KeyOf(ctx, form, extraHash);
            var e = _entries.GetOrCreateValue(form);
            if (e.value != null && e.key == key) { Hits++; return e.value; }
            Misses++;
            e.value = build();
            e.key = key;
            return e.value;
        }

        /// Drop the entry for one form (the next Get rebuilds).
        public void Invalidate(PlusForm form) => _entries.Remove(form);

        /// The hash Get keys by — exposed so a form can key its own structures the same way.
        public static int KeyOf(in PlusFormCtx ctx, PlusForm form, int extraHash = 0)
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
