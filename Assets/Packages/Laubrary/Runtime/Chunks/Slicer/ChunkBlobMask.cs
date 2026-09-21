using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// Carves a randomly-grown, fully CONNECTED, asymmetric sub-region out of a rectangular pixel crop, so a
    /// multi-texel sampled debris piece reads as a torn scrap of the art instead of a little square of it.
    ///
    /// WHY THIS EXISTS. Every sampled cut in this module is a RECTANGLE — <see cref="SampledChunkSprites.Sample"/>
    /// rolls a square sub-rect, <see cref="SampledChunkSprites.SampleAt"/> is handed one. At one texel that is
    /// invisible (a pixel is a pixel), but from two texels up a swarm of little squares of somebody's sprite
    /// reads as confetti, not as debris. This turns the rectangle into a blob while changing nothing about how
    /// BIG the crop is: the mask only clears ALPHA, so the built sprite still has exactly the rect's own texel
    /// count and the size-vs-resolution contract T-0397 established (crop texels ≤ ⌊worldSize·ppu⌋, hence render
    /// scale ≥ 1) is untouched by construction. Shape changes; size does not.
    ///
    /// ALGORITHM — seeded Eden growth over the crop's own opaque texels. A start texel is picked centre-biased
    /// (nearest-of-K among random opaque candidates, the mirror image of <see cref="FragmentCutter"/>'s
    /// farthest-of-K seed spread, for the opposite reason: a fragment's seeds must spread apart, a blob's single
    /// seed must not wander off into a corner where the piece would render badly off its own pivot). The region
    /// then grows one texel at a time, each time taking a texel from the FRONTIER — the set of unselected opaque
    /// texels 4-adjacent to something already selected. Every texel ever added is therefore a neighbour of the
    /// region, so the result is connected by construction and can contain no isolated texel; there is no
    /// post-pass to check, the invariant is the growth rule.
    ///
    /// Taking a UNIFORMLY random frontier texel alone grows a roundish clump (that is what plain Eden growth
    /// does). The shape is made asymmetric by sometimes taking the most RECENTLY added candidate instead
    /// (<see cref="TendrilChance"/>) — a depth-first bias that runs out a limb before filling in around it, which
    /// is what produces lobes, notches and the occasional hole rather than a circle.
    ///
    /// The target texel count is a random fraction of the rect's area, and is hard-capped at one below the
    /// crop's opaque texel count, so the selection is ALWAYS a strict subset: whatever else happens, a full
    /// rectangle can never be handed back as "a blob". That cap is also what makes the property a caller can
    /// assert on — the selection's bounding box always covers strictly more area than the selection itself.
    ///
    /// DETERMINISM. Everything is drawn from <see cref="ChunkRng"/>, the module's one generator, on its SECOND
    /// stream (the two-argument constructor) so a seed that also drives something else — PaletteSplash hands the
    /// same per-crop seed to the torn-edge jitter — moves the two independently. Same seed in, same blob out, on
    /// every platform and every run. Nothing here reads <c>UnityEngine.Random</c>.
    public static class ChunkBlobMask
    {
        /// The blob's texel count as a fraction of the crop's area, rolled per crop. The floor is high enough
        /// that a piece still reads as a piece (a 4×4 crop keeps at least 6 texels) and the ceiling low enough
        /// that the silhouette always has bites taken out of it.
        public const float MinCoverage = 0.40f;
        public const float MaxCoverage = 0.85f;

        /// How often growth takes the newest frontier texel (a limb) instead of a uniformly random one (a
        /// clump). Tuned by eye over generated crops: below ~0.25 the shapes go round, above ~0.65 they go
        /// stringy and stop reading as a solid piece of the thing that broke.
        const float TendrilChance = 0.45f;

        /// Random opaque texels weighed for the start pick, keeping the one nearest the crop's centre. Four is
        /// enough to keep a blob off the rim at the sizes debris sampling actually uses (2–20 texels).
        const int SeedCandidates = 4;

        /// Alpha at or below which a texel is "not part of the picture" and can never be grown into. Matches
        /// the intent of <see cref="FragmentCutter"/>'s own threshold (32/255), stated in float alpha because
        /// this side of the module works in <see cref="Color"/>.
        const float OpaqueAlpha = 32f / 255f;

        /// The ChunkRng stream this draws on. Any value works as long as it is not the 0 the single-argument
        /// constructor implies; a distinct one keeps a blob from moving in step with whatever else the same
        /// seed drives.
        const int BlobStream = 0xB10B;

        /// The smallest rect worth carving. A 1×1 crop has no interior at all and MUST stay a byte-for-byte
        /// no-op (the owner's own 1-game-pixel test recipe lives there); 2×1 and 1×2 have no room for a shape
        /// either. From 2×2 up there is something to take a bite out of.
        const int MinArea = 4;

        /// Chooses the blob and reports it as a per-texel mask over the crop, row-major, index = y*width + x —
        /// the same ordering GetPixels/SetPixels use. <paramref name="kept"/> is how many texels it selected.
        ///
        /// Null — and <paramref name="kept"/> 0 — whenever there is nothing meaningful to carve: a null or
        /// short buffer, a rect below <see cref="MinArea"/>, or fewer than three opaque texels in it. A null
        /// return is the caller's signal to leave the pixels exactly as they are, never to draw nothing.
        public static bool[] Select(Color[] px, int width, int height, int seed, out int kept)
        {
            kept = 0;
            if (px == null || width < 1 || height < 1) return null;

            int area = width * height;
            if (area < MinArea || px.Length < area) return null;

            var opaque = new List<int>(area);
            for (int i = 0; i < area; i++)
                if (px[i].a > OpaqueAlpha) opaque.Add(i);
            if (opaque.Count < 3) return null;   // two texels or fewer: any subset is a dot, not a shape

            var rng = new ChunkRng(seed, BlobStream);

            // Strictly fewer than every opaque texel, so the mask can never reproduce its own input.
            int target = Mathf.RoundToInt(area * rng.Range(MinCoverage, MaxCoverage));
            target = Mathf.Clamp(target, 2, opaque.Count - 1);

            int start = PickStart(opaque, width, height, ref rng);

            var selected = new bool[area];
            var queued = new bool[area];
            var frontier = new List<int>(area);

            selected[start] = true;
            queued[start] = true;
            kept = 1;
            EnqueueNeighbours(px, width, height, start, queued, frontier);

            while (kept < target && frontier.Count > 0)
            {
                // The newest candidate (a limb) or a uniformly random one (a clump) — see TendrilChance.
                int slot = rng.Next01() < TendrilChance ? frontier.Count - 1 : rng.Next(frontier.Count);
                int idx = frontier[slot];
                frontier[slot] = frontier[frontier.Count - 1];
                frontier.RemoveAt(frontier.Count - 1);

                selected[idx] = true;
                kept++;
                EnqueueNeighbours(px, width, height, idx, queued, frontier);
            }

            // The frontier can run dry before the target is met when the crop's opaque texels form several
            // islands and growth started on a small one. That is a graceful degrade, not a failure: the piece
            // is simply smaller than asked for, still connected, still made of real pixels.
            return selected;
        }

        /// <see cref="Select"/>, then clears the alpha of every texel outside the blob. RGB is never touched,
        /// so what survives is still made of the source art's own colours — the same alpha-only discipline
        /// <see cref="SampledChunkSprites"/>' torn-edge pass keeps.
        ///
        /// Returns how many texels survived, or 0 when it was a no-op and the buffer was left untouched.
        public static int Apply(Color[] px, int width, int height, int seed)
        {
            var mask = Select(px, width, height, seed, out int kept);
            if (mask == null) return 0;

            int area = width * height;
            for (int i = 0; i < area; i++)
                if (!mask[i]) px[i].a = 0f;
            return kept;
        }

        /// Nearest-of-K to the crop's centre among random opaque candidates — see the class comment for why the
        /// start is pulled inward rather than taken uniformly. Consumes exactly SeedCandidates draws whatever
        /// it picks, so the stream length does not depend on the art.
        static int PickStart(List<int> opaque, int width, int height, ref ChunkRng rng)
        {
            float cx = width * 0.5f, cy = height * 0.5f;
            int best = -1;
            float bestD = float.MaxValue;
            for (int c = 0; c < SeedCandidates; c++)
            {
                int i = opaque[rng.Next(opaque.Count)];
                float dx = (i % width) + 0.5f - cx, dy = (i / width) + 0.5f - cy;
                float d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// Adds this texel's not-yet-queued opaque 4-neighbours to the frontier. 4-adjacency, not 8: a blob
        /// joined only corner-to-corner reads as two pieces that happen to touch, and would break apart the
        /// moment the torn-edge pass took a bite anywhere near the join.
        static void EnqueueNeighbours(Color[] px, int width, int height, int idx,
                                      bool[] queued, List<int> frontier)
        {
            int x = idx % width, y = idx / width;
            TryEnqueue(px, width, height, x - 1, y, queued, frontier);
            TryEnqueue(px, width, height, x + 1, y, queued, frontier);
            TryEnqueue(px, width, height, x, y - 1, queued, frontier);
            TryEnqueue(px, width, height, x, y + 1, queued, frontier);
        }

        static void TryEnqueue(Color[] px, int width, int height, int x, int y,
                               bool[] queued, List<int> frontier)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int i = y * width + x;
            if (queued[i] || px[i].a <= OpaqueAlpha) return;
            queued[i] = true;
            frontier.Add(i);
        }
    }
}
