using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// The two placements a formation can lay out. Deliberately small — a shape nobody can describe in one
    /// word is a bespoke spawner, not a formation.
    public enum FormationShape
    {
        /// Points spread evenly along a straight segment centred on the origin.
        Line = 0,
        /// Points spread evenly around a circle (or a slice of one) centred on the origin.
        Ring = 1,
    }

    /// Which point counts as "first" when the stagger hands out delays.
    public enum FormationStaggerOrder
    {
        /// Point 0 first, then 1, 2, … — a wipe from one end of the shape to the other.
        Sequential = 0,
        /// The last point first — the same wipe running backwards.
        Reverse = 1,
        /// The middle point(s) first, spreading outwards; symmetric pairs fire together.
        FromCentre = 2,
        /// A shuffled order — the shape fills in unpredictably instead of sweeping.
        Random = 3,
    }

    /// <summary>
    /// WHERE a set of spawn points sits and WHEN each one fires — nothing about what gets spawned there.
    /// That separation is the point: this is the placement half, and whatever consumes a
    /// <see cref="SpawnPlacement"/> (a Pyre Blast capability, today) is the picking half. A plain serializable field bag, not a ScriptableObject, so it can be embedded in any
    /// asset that wants a formation without dragging a second file around.
    /// </summary>
    [System.Serializable]
    public class SpawnFormation
    {
        [Tooltip("Line = points along a straight segment; Ring = points around a circle or an arc of one.")]
        public FormationShape shape = FormationShape.Ring;

        /// <summary>How many spawn points the formation lays out. 1 puts a single point on the origin.</summary>
        [Tooltip("How many spawn points the formation lays out. 1 = a single point on the origin.")]
        [Range(1, 64)] public int count = 5;

        // ── Line ──────────────────────────────────────────────────────────────────
        /// <summary>Line only: the segment's total length in world units. Points sit evenly along it, centred on the origin.</summary>
        [Tooltip("Line only: total length of the segment, world units. The points are centred on the origin.")]
        public float length = 4f;

        /// <summary>Line only: which way the segment points, in degrees. 0 = right (+X), 90 = up (+Y).</summary>
        [Tooltip("Line only: the segment's direction in degrees. 0 = right, 90 = up.")]
        public float angleDeg = 0f;

        // ── Ring ──────────────────────────────────────────────────────────────────
        /// <summary>Ring only: distance from the origin to every point, world units.</summary>
        [Tooltip("Ring only: distance from the origin out to each point, world units.")]
        public float radius = 2f;

        /// <summary>
        /// Ring only: how much of the circle is used. 360 is a full ring (evenly spaced, no doubled-up point
        /// where the circle closes); anything less is an arc that includes BOTH of its end points.
        /// </summary>
        [Tooltip("Ring only: how much of the circle to use. 360 = a full ring; less = an arc, with a point on each end.")]
        [Range(0f, 360f)] public float arcDeg = 360f;

        /// <summary>Ring only: the angle of the first point, in degrees. 0 = right (+X), 90 = up (+Y).</summary>
        [Tooltip("Ring only: where the first point sits, in degrees. 0 = right, 90 = up.")]
        public float startAngleDeg = 0f;

        // ── Scatter ───────────────────────────────────────────────────────────────
        /// <summary>
        /// Radius of a random offset added to every point, world units. 0 leaves the shape perfectly regular;
        /// a little breaks the "stamped by a machine" look without losing the shape.
        /// </summary>
        [Tooltip("Random offset added to each point, world units. 0 = a perfectly regular shape.")]
        [Min(0f)] public float positionJitter = 0f;

        // ── Stagger ───────────────────────────────────────────────────────────────
        /// <summary>
        /// Seconds between one point firing and the next, following <see cref="staggerOrder"/>. 0 fires the
        /// whole formation at once (and costs nothing at all — no runner is created).
        /// </summary>
        [Tooltip("Seconds between one point firing and the next. 0 = the whole formation fires at once.")]
        [Min(0f)] public float staggerSeconds = 0.05f;

        /// <summary>
        /// Random ± wobble added to each point's delay, seconds. Keeps a long stagger from sounding
        /// metronomic. Clamped at USE to <see cref="staggerSeconds"/> (see Resolve) so the wobble can never
        /// out-shout the beat it decorates — the authored value is left exactly as typed.
        /// </summary>
        [Tooltip("Random plus/minus wobble on each point's delay, seconds. 0 = a perfectly even beat. " +
                 "Capped at the Stagger Seconds value when used, so the ripple's order always survives.")]
        [Min(0f)] public float staggerJitter = 0f;

        /// <summary>Which point the stagger treats as first — a wipe, a reverse wipe, an outward bloom, or a shuffle.</summary>
        [Tooltip("Which point fires first: a wipe along the shape, the same backwards, outwards from the middle, or shuffled.")]
        public FormationStaggerOrder staggerOrder = FormationStaggerOrder.Sequential;

        /// <summary>
        /// Fixes the randomness (jitter + shuffled order) so the formation resolves identically every time.
        /// 0 means "reroll on every play" — the right default for debris, wrong for anything authored to hit
        /// the same marks twice.
        /// </summary>
        [Tooltip("Fixes the random jitter and shuffle so every play is identical. 0 = reroll every time.")]
        public int seed = 0;

        // ── resolver ──────────────────────────────────────────────────────────────
        // Turns the authored fields above into concrete world-space SpawnPlacements. This is the ONLY place a
        // formation is laid out — the Pyre Blast capability never computes a position itself, and the editor
        // preview calls the very same method, so "what you see" and "what fires" cannot drift apart.

        /// <summary>
        /// Resolves this formation into <paramref name="results"/> (cleared first), each placement's position
        /// already offset by <paramref name="origin"/>. Reads <see cref="seed"/> for its randomness — 0 means
        /// reroll every play, non-zero resolves identically every time.
        /// </summary>
        public void Resolve(Vector3 origin, List<SpawnPlacement> results) => Resolve(origin, results, seed);

        /// <summary>
        /// Same as <see cref="Resolve(Vector3, List{SpawnPlacement})"/> but forces the random stream's seed
        /// instead of reading <see cref="seed"/> off this instance. The editor preview uses this so a formation
        /// authored with seed 0 ("reroll every play") still previews a STABLE arrangement instead of reshuffling
        /// on every repaint, without touching the authored asset.
        /// </summary>
        public void Resolve(Vector3 origin, List<SpawnPlacement> results, int seedOverride)
        {
            if (results == null) return;
            results.Clear();

            int n = Mathf.Max(1, count);
            // One draw off Unity's shared generator when the layout is unseeded ("reroll every play"), then
            // everything downstream comes off ChunkRng — so the whole arrangement is reproducible from one
            // integer, which is what lets a preview draw exactly the formation a burst will make.
            var rng = new ChunkRng(seedOverride != 0 ? seedOverride : Random.Range(1, int.MaxValue));

            // ── 1. base positions, in the formation's own local space (origin folded in at the end) ──────────
            var positions = new Vector3[n];
            if (n == 1)
            {
                // Both shapes agree here: a "formation" of one point is just a single spawn on the origin —
                // there is no honest length/radius to place a lone point at.
                positions[0] = Vector3.zero;
            }
            else if (shape == FormationShape.Line)
            {
                float rad = angleDeg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / (n - 1) - 0.5f;   // -0.5 … 0.5, centred on the origin
                    positions[i] = dir * (t * length);
                }
            }
            else // Ring
            {
                bool fullRing = arcDeg >= 360f;
                // Full ring: step = arc/n so point 0 and point n never land on the same angle (no doubled-up
                // closing point). A partial arc includes BOTH end points, so step = arc/(n-1) instead.
                float step = fullRing ? (arcDeg / n) : (arcDeg / (n - 1));
                for (int i = 0; i < n; i++)
                {
                    float ang = (startAngleDeg + step * i) * Mathf.Deg2Rad;
                    positions[i] = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * radius;
                }
            }

            // ── 2. scatter — a uniform offset inside a disc of positionJitter, off the SAME random stream ────
            if (positionJitter > 0f)
            {
                for (int i = 0; i < n; i++)
                {
                    float r = positionJitter * Mathf.Sqrt(rng.Next01());   // sqrt(u) = uniform over the disc's AREA
                    float a = rng.Next01() * Mathf.PI * 2f;
                    positions[i] += new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r;
                }
            }

            // ── 3. stagger rank — WHICH point counts as Nth for the delay formula below ─────────────────────
            int[] rank = ComputeStaggerRanks(n, staggerOrder, ref rng);

            // ── 4. delays ─────────────────────────────────────────────────────────────────────────────────
            // staggerSeconds == 0 must yield an all-zero-delay formation (the field's own tooltip promises the
            // whole formation "fires at once and costs nothing at all") — so jitter is skipped outright rather
            // than added around a zero base and clamped, which would silently bias every delay positive.
            //
            // The jitter is capped at staggerSeconds AT USE, not in the field, because an unbounded wobble
            // destroys the very thing it decorates: with staggerSeconds 0.05 and staggerJitter 0.3 (a real
            // authored value) the noise beats the beat 6:1, roughly half the points clamp to delay 0 and fire
            // together, and the rest scatter out past +0.45s — the authored ripple becomes a mess. Capped, a
            // point can at most swap with its immediate neighbour, so the sweep is always still legible.
            // Clamping here rather than rewriting the serialized field keeps the author's typed value intact.
            float jitter = Mathf.Min(staggerJitter, staggerSeconds);
            for (int i = 0; i < n; i++)
            {
                float delay = 0f;
                if (staggerSeconds > 0f)
                {
                    delay = staggerSeconds * rank[i];
                    if (jitter > 0f) delay += (rng.Next01() * 2f - 1f) * jitter;
                    delay = Mathf.Max(0f, delay);
                }
                results.Add(new SpawnPlacement(origin + positions[i], delay, i));
            }
        }

        /// Convenience form of <see cref="Resolve(Vector3, List{SpawnPlacement})"/> for a caller that doesn't
        /// want to own a reusable list.
        public SpawnPlacement[] Resolve(Vector3 origin)
        {
            var list = new List<SpawnPlacement>(Mathf.Max(1, count));
            Resolve(origin, list);
            return list.ToArray();
        }

        /// The firing RANK (0 = first) of every point index, per <paramref name="order"/>. Symmetric pairs in
        /// FromCentre intentionally share a rank — that is what makes them fire together.
        static int[] ComputeStaggerRanks(int n, FormationStaggerOrder order, ref ChunkRng rng)
        {
            var rank = new int[n];
            switch (order)
            {
                case FormationStaggerOrder.Reverse:
                    for (int i = 0; i < n; i++) rank[i] = n - 1 - i;
                    break;

                case FormationStaggerOrder.FromCentre:
                {
                    float centre = (n - 1) * 0.5f;
                    var dist = new float[n];
                    for (int i = 0; i < n; i++) dist[i] = Mathf.Abs(i - centre);
                    var distinct = new List<float>(n);
                    for (int i = 0; i < n; i++)
                    {
                        bool found = false;
                        for (int k = 0; k < distinct.Count; k++)
                            if (Mathf.Abs(distinct[k] - dist[i]) < 0.0001f) { found = true; break; }
                        if (!found) distinct.Add(dist[i]);
                    }
                    distinct.Sort();
                    for (int i = 0; i < n; i++)
                        for (int k = 0; k < distinct.Count; k++)
                            if (Mathf.Abs(distinct[k] - dist[i]) < 0.0001f) { rank[i] = k; break; }
                    break;
                }

                case FormationStaggerOrder.Random:
                {
                    for (int i = 0; i < n; i++) rank[i] = i;
                    // Fisher-Yates over the SAME stream jitter/scatter already used, so "seed 0 = reroll every
                    // play, non-zero = identical every time" holds for the shuffle too, not just the jitter.
                    for (int i = n - 1; i > 0; i--)
                    {
                        int j = rng.Next(i + 1);
                        (rank[i], rank[j]) = (rank[j], rank[i]);
                    }
                    break;
                }

                default: // Sequential
                    for (int i = 0; i < n; i++) rank[i] = i;
                    break;
            }
            return rank;
        }
    }
}
