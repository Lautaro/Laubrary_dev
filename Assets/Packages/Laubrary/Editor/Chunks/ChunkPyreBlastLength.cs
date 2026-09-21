// ChunkPyreBlastLength — how long ONE spawn of a Pyre Blast's chosen source really plays, read from the
// SAME numbers PyreDirectSpawn/PyreBlastPlayer actually play for (T-0356).
//
// WHY THIS LIVES HERE, NOT ON PyreBlast ITSELF: PyreBlast (Runtime/Chunks) cannot reference Pyre — Pyre
// already references Chunks (PyreChunksDirect.cs implements IChunkEffectSpawner), so the reverse would be an
// assembly cycle Unity refuses to compile. This file lives in Chunks.EDITOR instead, which has no such
// problem: nothing in Runtime/Pyre ever references an EDITOR-only asmdef, so Chunks.Editor referencing Pyre's
// runtime asmdef (added to this asmdef's references alongside this file) is one-directional and safe. It is
// UI-only support code, exactly like the rest of this folder already is Pyre-aware in spirit (see
// ChunkWindow.PyreBlastCard.cs's "Blast" picker, filtered to IChunkEffectSpawner but authored with Pyre in
// mind throughout).
//
// WHY NOT IChunkAnimation (Fps/Loop/GetFrames): that is Pyre's OTHER Chunks contract — handing over frames
// for someone else to play as ongoing chunk content — and it reports Loop = true UNCONDITIONALLY (see
// PyreChunksDirect.cs's own comment on why: chunk content loops until its owner ends it). Reading THAT length
// would tell you a directly-spawned blast never ends, which is false: PyreChunksDirect.cs's SpawnEffect calls
// PyreDirectSpawn.Spawn(this, DirectFps, loop: false, 0f, ...) — a spawned blast is ALWAYS a one-shot unless a
// PyreSpawnSource wrapper says otherwise. IChunkEffectSpawner itself says nothing about length by design (see
// its own header comment), so this file reads the concrete types instead of widening that interface.
//
// CherryFraming (Pyre.cherryFrames / CherryFrame.ResolveLength) was checked and is NOT used here on purpose:
// its own header comment says cherry-picking only reorders the EDITOR PREVIEW loop and "rendering is
// untouched" — PyreRenderer.GetFrames (what PyreBlastPlayer actually plays) always returns exactly
// `frameCount` frames in source order, cherry-picking or not. Reading CherryFrame lengths here would describe
// a preview loop nobody spawns, not the real play length this file exists to report.
using UnityEngine;
using Laubrary.Pyre;
using PyreAsset = Laubrary.Pyre.Pyre;   // the class is shadowed by the namespace inside a Laubrary.* namespace
                                        // (same alias MirageRig.cs carries, for the same reason) — writing the
                                        // bare name here is CS0118, which is how this file landed (T-0383).

namespace Laubrary.Chunks.Editor
{
    internal static class ChunkPyreBlastLength
    {
        /// The real seconds ONE spawn of `source` plays for, or null when `source` is not (yet) a type this
        /// can read a length from — an empty slot, or some future IChunkEffectSpawner implementor besides
        /// Pyre/PyreSpawnSource. Null means "On screen" falls back to whatever is typed, same as before this
        /// existed.
        public static float? RealSeconds(Object source)
        {
            switch (source)
            {
                // A directly-picked Pyre always plays ONCE, at its own authored rate — PyreChunksDirect.cs
                // SpawnEffect: PyreDirectSpawn.Spawn(this, DirectFps, false, 0f, ...). PyreBlastPlayer.Update
                // advances `clock` by `fps` per second and stops once `Mathf.FloorToInt(clock) >= frames.Length`
                // (frames.Length == frameCount, PyreRenderer.GetFrames) — so it takes exactly
                // frameCount / fps seconds to finish, which is exactly this division.
                case PyreAsset p:
                    return p.frameCount / p.DirectFps;

                // The wrapper carries its OWN per-use fps/loop/loopSeconds (PyreSpawnSource.SpawnEffect calls
                // PyreDirectSpawn.Spawn(spec, fps, loop, loopSeconds, ...)). A looping wrapper is cut short at
                // loopSeconds by PyreDirectSpawn.EndLoopAfter — that IS its real length, not frameCount/fps.
                // loopSeconds == 0 means "never stop on its own" (the caller tears it down another way): that
                // has no finite length to report, so it falls through to "unknown" rather than claiming 0s.
                case PyreSpawnSource s when s.spec != null:
                    if (s.loop) return s.loopSeconds > 0f ? (float?)s.loopSeconds : null;
                    return s.spec.frameCount / Mathf.Max(1f, s.fps);

                default:
                    return null;
            }
        }

        /// The real length a whole Pyre Blast card's "On screen" should default to: the single source, or —
        /// once the pool holds more than one usable alternate — the LONGEST of them.
        ///
        /// Longest, not shortest or an average: "On screen" only sizes the clock/bands/preview flight and
        /// never cuts a blast short (see PyreBlast.blastSeconds' own doc comment). Sizing to the shortest
        /// alternate would make every OTHER alternate's tail run past the end of its own band on whichever
        /// spawn happens to draw it — exactly the "gaps and overlaps that do not happen" H6 already caught for
        /// a fixed 0.6s guess, just moved from "one wrong guess" to "wrong for every alternate but the
        /// shortest." Sizing to the longest means only the FASTER alternates ever finish early inside their
        /// band (a blast visibly ending sooner than the band it was drawn in reads as normal — plenty of
        /// existing recipes already do that on purpose with the Life dial elsewhere); a blast still visibly
        /// playing after its band/clock says it is over reads as broken, so that is the direction never to
        /// allow.
        public static float? RealSeconds(PyreBlast blast)
        {
            if (blast == null) return null;

            float? longest = null;
            if (blast.pool != null)
                for (int i = 0; i < blast.pool.Count; i++)
                {
                    float? one = RealSeconds(blast.pool[i]);
                    if (one.HasValue && (!longest.HasValue || one.Value > longest.Value)) longest = one.Value;
                }
            if (longest.HasValue) return longest;

            return RealSeconds(blast.source);
        }
    }
}
