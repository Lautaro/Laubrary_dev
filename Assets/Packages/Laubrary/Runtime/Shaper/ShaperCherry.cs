using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0143 -- one cherry-picked slot in a document's cherry sequence, ported field-for-field from Pyre's
    /// <c>CherryFrame</c> (<c>Runtime/Pyre/CherryFrame.cs</c>).
    ///
    /// A document resolves to a sequence of frames (design C1). Cherry framing builds a SUB-SEQUENCE of those
    /// frames: an ordered list of slots, each naming a source frame and holding it for a number of beats. It
    /// is a playback-order feature only -- it never changes how a frame is rendered, only which frame is shown
    /// and for how long.
    ///
    /// Defaults are exact no-ops: <c>sourceIndex</c> 0, <c>multiFrame</c> off, <c>lengthMultiplier</c> 1. A
    /// document that never enables cherry framing serialises and plays exactly as it did before this existed.
    /// </summary>
    [Serializable]
    public class ShaperCherryFrame
    {
        /// <summary>Index into the document's own frame range (<c>0 .. frameCount-1</c>). The frame this slot
        /// plays when <see cref="multiFrame"/> is off.</summary>
        public int sourceIndex = 0;

        // ── Variable length ──────────────────────────────────────────────────────────────────────────────
        /// <summary>How many BEATS this slot occupies, where one beat is one tick of the document's frame
        /// rate. 1 = a normal-length frame; 2 = hold it twice as long.</summary>
        public float lengthMultiplier = 1f;
        /// <summary>Lower bound of the per-entry length. Ignored unless <see cref="useMinMaxLength"/>.</summary>
        public float minLengthMultiplier = 1f;
        /// <summary>Upper bound of the per-entry length. Ignored unless <see cref="useMinMaxLength"/>.</summary>
        public float maxLengthMultiplier = 1f;
        /// <summary>When true the hold length is drawn between min and max on each entry into this slot, so a
        /// "1..3x" frame holds for a different length on each pass through the loop.</summary>
        public bool useMinMaxLength = false;

        // ── Multi-frame ──────────────────────────────────────────────────────────────────────────────────
        /// <summary>When true the slot draws one index out of <see cref="multiFrameSources"/> on each entry
        /// and plays it as if it were <see cref="sourceIndex"/>.</summary>
        public bool multiFrame = false;
        /// <summary>The candidate source frames for a multi-frame slot. Falls back to
        /// <see cref="sourceIndex"/> when empty.</summary>
        public List<int> multiFrameSources = new List<int>();
        /// <summary>Per-slot salt for the multi-frame draw. Non-zero pins this slot's picks independently of
        /// the document seed, so one slot can be re-rolled without disturbing the others.</summary>
        public int multiFrameRandomSeed = 0;

        /// <summary>
        /// The source frame this slot plays on this entry. Deterministic: the same document, seed, slot and
        /// loop index always produce the same pick.
        ///
        /// Pyre's equivalent calls <c>UnityEngine.Random.Range</c> / <c>System.Random</c>. Neither is
        /// available here -- <see cref="ShaperValue"/>'s own doc records that <c>System.Random</c> is banned
        /// from a generator (BC-1.3) and that <c>UnityEngine.Random</c> "re-rolls on every call and would make
        /// a dial flicker every frame". The same objection applies exactly to a cherry draw: a re-rolling pick
        /// makes the SAME frame of the SAME document play differently on two consecutive repaints, which is
        /// indistinguishable from a bug. Drawing from a hash of (seed, slot, loop) keeps Pyre's authored
        /// intent -- the pick still varies per slot and per pass through the loop -- while making a given
        /// playback reproducible, which is what LT-4's seeded-MinMax rule asks of every other draw in this
        /// engine.
        /// </summary>
        public int PickSourceFrame(uint seed, int slotIndex, int loopIndex)
        {
            if (!multiFrame || multiFrameSources == null || multiFrameSources.Count == 0)
                return Mathf.Max(0, sourceIndex);

            uint s = multiFrameRandomSeed != 0 ? unchecked((uint)multiFrameRandomSeed) : seed;
            float u = ShaperCherry.Draw(s, slotIndex, loopIndex, ShaperCherry.SaltPick);
            int i = Mathf.Clamp(Mathf.FloorToInt(u * multiFrameSources.Count), 0, multiFrameSources.Count - 1);
            return Mathf.Max(0, multiFrameSources[i]);
        }

        /// <summary>This slot's hold length in beats on this entry, drawn deterministically when
        /// <see cref="useMinMaxLength"/> is on. Floored at a twentieth of a beat so a slot can never take zero
        /// or negative beats and stall the sequencer in an infinite advance.</summary>
        public float ResolveLength(uint seed, int slotIndex, int loopIndex)
        {
            if (!useMinMaxLength) return Mathf.Max(0.05f, lengthMultiplier);
            float lo = Mathf.Min(minLengthMultiplier, maxLengthMultiplier);
            float hi = Mathf.Max(minLengthMultiplier, maxLengthMultiplier);
            float u = ShaperCherry.Draw(seed, slotIndex, loopIndex, ShaperCherry.SaltLength);
            return Mathf.Max(0.05f, Mathf.Lerp(lo, hi, u));
        }
    }

    /// <summary>
    /// The cherry sequencer's whole mutable state, held by whoever is playing back (an editor window, a
    /// runtime driver, a test) and handed to <see cref="ShaperCherry.AdvanceOneBeat"/> to get the next one.
    ///
    /// The engine owns the RULE and the caller owns the STATE, deliberately: Pyre's version keeps these five
    /// values as window fields and mutates them in place, which is why its rule cannot be tested or reused
    /// outside that window.
    /// </summary>
    [Serializable]
    public struct ShaperCherryState
    {
        /// <summary>Index into the document's cherry slot list that is currently showing.</summary>
        public int slot;
        /// <summary>Beats remaining before the current slot gives way to the next.</summary>
        public float beatsLeft;
        /// <summary>True while the preview is deliberately blank between loop iterations.</summary>
        public bool delayActive;
        /// <summary>Beats remaining in that blank gap.</summary>
        public float delayBeatsLeft;
        /// <summary>How many complete passes through the sequence have finished. Feeds the deterministic
        /// draws, so a min/max length or a multi-frame pick varies from one pass to the next instead of
        /// freezing on the value it drew the first time.</summary>
        public int loopIndex;
        /// <summary>The resolved source frame to display, or <see cref="ShaperCherry.BlankFrame"/> during the
        /// between-loop gap.</summary>
        public int frame;
    }

    /// <summary>
    /// T-0143 -- the cherry-framing playback rule, ported from <c>PyreWindow.CherryAdvanceOneBeat</c> and made
    /// a pure function of (state, document) so it can be driven from anywhere and asserted in a test.
    ///
    /// One beat is one tick of the document's frame rate (<see cref="ShaperDocument.frameRate"/>). Call
    /// <see cref="AdvanceOneBeat"/> once per beat; the returned state carries the frame to draw.
    /// </summary>
    public static class ShaperCherry
    {
        /// <summary>The sentinel <see cref="ShaperCherryState.frame"/> carries when nothing should be drawn --
        /// the blank gap between loop iterations, or a cherry-enabled document with no slots at all. A caller
        /// must check for it before using the value as an index.</summary>
        public const int BlankFrame = -1;

        internal const uint SaltLength = 0x9E3779B9u;
        internal const uint SaltPick = 0x85EBCA6Bu;

        /// <summary>A reproducible unit draw in [0,1) from (seed, slot, loop, purpose), built on the same
        /// lowbias32 avalanche <see cref="ShaperValue"/> uses for its seeded MinMax dials so the two agree on
        /// what "deterministic draw" means in this engine.</summary>
        internal static float Draw(uint seed, int slotIndex, int loopIndex, uint salt)
        {
            unchecked
            {
                uint x = seed;
                x = ShaperValue.HashMix(x ^ salt);
                x = ShaperValue.HashMix(x ^ (uint)slotIndex);
                x = ShaperValue.HashMix(x ^ (uint)loopIndex);
                return ShaperValue.UnitOf(x);
            }
        }

        /// <summary>
        /// The state a sequence starts (or restarts) from. Call after ANY edit to the slot list, the enabled
        /// flag, the loop delay or the frame count, so playback can never hold a stale resolved frame or a
        /// beat count measured against a slot that no longer exists -- the job Pyre's
        /// <c>ResetCherryPlayback</c> does.
        /// </summary>
        public static ShaperCherryState Begin(ShaperDocument doc)
        {
            var st = new ShaperCherryState { slot = 0, loopIndex = 0, frame = BlankFrame };
            if (doc == null) return st;

            var slots = doc.cherryFrames;
            if (!doc.cherryEnabled || slots == null || slots.Count == 0) return st;

            return EnterSlot(st, doc, 0);
        }

        /// <summary>
        /// One beat of cherry playback: hold the current slot's frame for its resolved length, then move to
        /// the next. After the last slot the sequence either loops straight back to slot 0, or -- when
        /// <see cref="ShaperDocument.cherryLoopDelaySeconds"/> is above zero -- goes blank for that long first.
        ///
        /// Returns the next state; the input is not modified.
        /// </summary>
        public static ShaperCherryState AdvanceOneBeat(ShaperCherryState state, ShaperDocument doc)
        {
            if (doc == null) return state;
            var slots = doc.cherryFrames;
            if (slots == null || slots.Count == 0)
            {
                state.frame = BlankFrame;
                return state;
            }

            if (state.delayActive)
            {
                state.delayBeatsLeft -= 1f;
                if (state.delayBeatsLeft > 0f) { state.frame = BlankFrame; return state; }
                state.delayActive = false;
                return EnterSlot(state, doc, 0);
            }

            state.beatsLeft -= 1f;
            if (state.beatsLeft > 0f) return state;   // still holding this slot's frame

            int next = state.slot + 1;
            if (next >= slots.Count)
            {
                // A completed pass. Bump the loop index FIRST so the next pass's deterministic draws differ
                // from this one's -- that is what keeps a min/max length varying across repeats instead of
                // freezing on its first draw.
                state.loopIndex++;

                if (doc.cherryLoopDelaySeconds > 0f)
                {
                    state.delayActive = true;
                    state.delayBeatsLeft = Mathf.Max(1f, doc.cherryLoopDelaySeconds * Mathf.Max(1f, doc.frameRate));
                    state.frame = BlankFrame;
                    return state;
                }
                next = 0;
            }
            return EnterSlot(state, doc, next);
        }

        static ShaperCherryState EnterSlot(ShaperCherryState state, ShaperDocument doc, int slotIndex)
        {
            var slots = doc.cherryFrames;
            slotIndex = Mathf.Clamp(slotIndex, 0, slots.Count - 1);
            var slot = slots[slotIndex];

            state.slot = slotIndex;
            if (slot == null)
            {
                // A null entry in a serialized list is possible after a bad edit; treat it as a blank beat
                // rather than throwing inside a playback tick.
                state.frame = BlankFrame;
                state.beatsLeft = 1f;
                return state;
            }

            int picked = slot.PickSourceFrame(doc.seed, slotIndex, state.loopIndex);
            state.frame = Mathf.Clamp(picked, 0, Mathf.Max(0, doc.frameCount - 1));
            state.beatsLeft = slot.ResolveLength(doc.seed, slotIndex, state.loopIndex);
            return state;
        }
    }
}
