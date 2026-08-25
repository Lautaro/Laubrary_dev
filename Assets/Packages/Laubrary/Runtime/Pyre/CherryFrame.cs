// CherryFrame — one cherry-picked slot in a Pyre spec's cherry sequence.
//
// A Pyre spec bakes N frames via the existing Pyre pipeline. CherryFraming (see PyreWindow.CherryFraming.cs)
// lets the user build a sub-sequence of those frames. Each CherryFrame is ONE slot in that sub-sequence.
//
//   • sourceIndex         — which baked frame this slot plays (the "default" frame for the slot).
//   • multiFrame          — when on, multiFrameSources holds several source frames; on each playback entry the
//                           sequencer picks one at random (or seeded) and treats it as the frame for that slot.
//   • lengthMultiplier    — Static or MinMax. How many beat ticks the slot occupies in the preview loop.
//                           1 = one beat (= one tick of previewFps). 2 = two beats. Lets the user hold a frame.
//                           The MinMax mode randomises per-evaluation, so a "1..3× length" frame plays for a
//                           different duration each loop through.
//
// Defaults are exact no-ops: sourceIndex 0, multiFrame off, lengthMultiplier 1. A list of zero CherryFrames
// renders as a blank preview when CherryFraming is enabled (per spec). The struct is value-only (int / float /
// List<int>) — MemberwiseClone copies it for free; the multiFrameSources list is the only reference field and is
// rebuilt by callers as needed.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre
{
    [System.Serializable]
    public class CherryFrame
    {
        [Tooltip("Index into the spec's baked frame list (0..frameCount-1). The frame that plays when this slot is reached, when MultiFrame is off.")]
        public int sourceIndex = 0;

        // ── Variable Length ───────────────────────────────────────────────────────────
        // How many BEAT TICKS this slot occupies in the preview loop (1 tick = one tick of previewFps). 1 = normal.
        // Static is a plain multiplier; MinMax randomises between min and max on each evaluation so a frame can hold
        // for varying lengths across replays.
        [Tooltip("How many beat ticks this slot occupies in the preview loop. 1 = normal. 2 = hold the frame twice as long.")]
        public float lengthMultiplier = 1f;
        [Tooltip("MinMax mode: lower bound of the per-evaluation length. Ignored unless useMinMaxLength is true.")]
        public float minLengthMultiplier = 1f;
        [Tooltip("MinMax mode: upper bound of the per-evaluation length. Ignored unless useMinMaxLength is true.")]
        public float maxLengthMultiplier = 1f;
        [Tooltip("When true, lengthMultiplier is randomised between minLengthMultiplier and maxLengthMultiplier on each playback entry. When false, lengthMultiplier is used verbatim.")]
        public bool useMinMaxLength = false;

        // ── MultiFrame ────────────────────────────────────────────────────────────────
        // When ON, multiFrameSources holds 1..N source indices; on each playback entry into this slot, one of them is
        // picked at random (or seeded by multiFrameRandomSeed if non-zero) and used AS IF it were sourceIndex. When
        // OFF, sourceIndex is used verbatim.
        [Tooltip("When true, the sequencer randomly picks one source index from multiFrameSources each time this slot is played. When false, sourceIndex is used.")]
        public bool multiFrame = false;
        [Tooltip("The source frames this slot can pick between when MultiFrame is on. Falls back to sourceIndex if empty.")]
        public List<int> multiFrameSources = new List<int>();
        [Tooltip("Seed for the per-slot picker when MultiFrame is on. 0 = fully random.")]
        public int multiFrameRandomSeed = 0;

        // Pick the source frame that plays in this slot at THIS playback entry. For a non-MultiFrame slot that's
        // just sourceIndex; for a MultiFrame slot it's a random pick (or seeded) from multiFrameSources, falling back
        // to sourceIndex when the list is empty.
        public int PickSourceFrame()
        {
            if (!multiFrame || multiFrameSources == null || multiFrameSources.Count == 0)
                return Mathf.Max(0, sourceIndex);
            if (multiFrameRandomSeed != 0)
            {
                var rng = new System.Random(multiFrameRandomSeed);
                return Mathf.Max(0, multiFrameSources[rng.Next(multiFrameSources.Count)]);
            }
            return Mathf.Max(0, multiFrameSources[Random.Range(0, multiFrameSources.Count)]);
        }

        // Resolve this slot's current length multiplier. For a non-MinMax slot that's just lengthMultiplier; for a
        // MinMax slot it's a random sample in [min..max].
        public float ResolveLength()
        {
            if (!useMinMaxLength) return Mathf.Max(0.01f, lengthMultiplier);
            float lo = Mathf.Min(minLengthMultiplier, maxLengthMultiplier);
            float hi = Mathf.Max(minLengthMultiplier, maxLengthMultiplier);
            return Mathf.Max(0.01f, Random.Range(lo, hi));
        }
    }
}
