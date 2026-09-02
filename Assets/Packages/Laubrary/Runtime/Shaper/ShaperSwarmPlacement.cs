using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// Where a swarm instance LANDS. T-0169 — the shape a swarm arranges itself on, ported from Pyre's own
    /// spawn maths (<c>PyreRenderer.PlaceParticle</c> / <c>ApplyShapeTransform</c> / <c>PathTangentLocal</c>,
    /// <c>Runtime/Pyre/PyreRenderer.cs:2260-2460</c>) rather than re-derived, so a Pyre effect brought across
    /// as a Shaper composite arranges the same way it did in Pyre.
    ///
    /// APPEND-ONLY on every enum: each is serialized as an int inside <see cref="ShaperSwarmDef"/>, so a
    /// member may be added at the end but never renumbered or removed.
    /// </summary>
    public enum ShaperSwarmShape
    {
        /// <summary>The identity, and the default: instances stay on the node's own origin and are moved only
        /// by the jitter dials, which is exactly what a swarm authored before this task did. Every other
        /// member is opt-in, so an existing document opens rendering what it always rendered.</summary>
        None = 0,
        Circle = 1,
        Triangle = 2,
        Square = 3,
        Pentagon = 4,
        Hexagon = 5,
        /// <summary>A straight horizontal segment through the centre, endpoints at ±radius. One-dimensional,
        /// so it ignores Area/Path entirely; the spawner rotation is what gives the row its angle.</summary>
        Line = 6,
    }

    /// <summary>Whether instances fill the shape's INTERIOR or ride its OUTLINE.</summary>
    public enum ShaperSwarmSpawnMode { Area = 0, Path = 1 }

    /// <summary>Which way an instance FACES once placed — folded into the instance's own rotation on top of
    /// the node's authored rotation and the swarm's rotation jitter.</summary>
    public enum ShaperSwarmOrient
    {
        /// <summary>No turning. The identity, and the default.</summary>
        None = 0,
        /// <summary>Away from the spawner centre.</summary>
        Outward = 1,
        /// <summary>Along the outline at the point the instance sits on. Path mode only; in Area mode, or on a
        /// degenerate tangent, it falls back to Outward rather than silently doing nothing.</summary>
        PathTangent = 2,
    }

    /// <summary>
    /// How the swarm distributes its instances IN TIME.
    /// </summary>
    public enum ShaperSwarmTiming
    {
        /// <summary>The default and the identity: every instance is alive for the whole document, its own
        /// clock drawn by hash and blended toward the node's own by <see cref="ShaperSwarmDef.lifetimeStagger"/>.
        /// This is the model shipped by T-0113, kept as the default so no existing document changes.</summary>
        Stagger = 0,
        /// <summary>Instance <c>i</c> is BORN at the phase <see cref="ShaperSwarmDef.spawnTiming"/> maps
        /// <c>i/(n-1)</c> to, lives for <see cref="ShaperSwarmDef.instanceLife"/> of the document, and is not
        /// emitted at all before its birth or after its death.</summary>
        Window = 1,
        /// <summary>Same birth/death model as Window, but the birth phases are an arithmetic series — the
        /// first instance at <see cref="ShaperSwarmDef.firstSpawnPhase"/>, each next one
        /// <see cref="ShaperSwarmDef.spawnPhaseStep"/> later. Authored in FRAMES by the window, stored in
        /// phase, so the engine needs no knowledge of the document's frame count.</summary>
        FrameStep = 2,
    }

    /// <summary>
    /// The pure, deterministic placement maths. No <c>System.Random</c>/<c>UnityEngine.Random</c> anywhere
    /// (BC-1.3) — every draw is <see cref="ShaperValue.HashMix"/> over (seed, instance index, field id), the
    /// same definition of "a deterministic draw" the rest of the engine uses.
    ///
    /// Everything here works in the node's own PARENT-local canvas units with the spawner centre at the
    /// ORIGIN; <see cref="ShaperSwarmDef.spawnerOffsetX"/>/<c>Y</c> is added last by the caller, exactly as
    /// Pyre adds <c>cx + offX</c> last.
    /// </summary>
    public static class ShaperSwarmPlacement
    {
        const uint FldPlaceA = 0x7E10_0001u, FldPlaceB = 0x7E10_0002u, FldPlaceC = 0x7E10_0003u;
        const uint FldOrder  = 0x7E10_0004u;

        /// <summary>A unit draw in [0,1) for instance <paramref name="i"/> under one field id. Two avalanche
        /// rounds, because one round of a small (seed ^ index) mix leaves neighbouring indices correlated —
        /// which shows up directly as a diagonal streak in an Area scatter.</summary>
        public static float Unit(uint seed, int i, uint field)
        {
            uint h = ShaperValue.HashMix(seed ^ field ^ ((uint)i * 2654435761u + 0x9E3779B9u));
            return ShaperValue.UnitOf(ShaperValue.HashMix(h));
        }

        /// <summary>Side count for a regular-polygon shape; 0 for the kinds that are not one.</summary>
        public static int SideCount(ShaperSwarmShape kind)
        {
            switch (kind)
            {
                case ShaperSwarmShape.Triangle: return 3;
                case ShaperSwarmShape.Square:   return 4;
                case ShaperSwarmShape.Pentagon: return 5;
                case ShaperSwarmShape.Hexagon:  return 6;
                default:                        return 0;
            }
        }

        /// <summary>Vertex <paramref name="k"/> of a regular <paramref name="n"/>-gon of circumradius
        /// <paramref name="r"/>, centred on the origin. Vertex 0 is at the TOP (+90°) and vertices advance
        /// counter-clockwise, matching Pyre's convention so a ported effect keeps its orientation.
        /// <paramref name="k"/> may exceed <c>n-1</c>; the trig is periodic.</summary>
        public static Vector2 PolyVertex(float r, int k, int n)
        {
            float ang = Mathf.PI / 2f + 2f * Mathf.PI * k / n;
            return new Vector2(r * Mathf.Cos(ang), r * Mathf.Sin(ang));
        }

        /// <summary>The boundary distance of a regular <paramref name="n"/>-gon at screen angle
        /// <paramref name="ang"/> — the apothem divided by the cosine of the angle off the nearest side's
        /// normal. Used to scale the ordered ring layout onto a polygon, so a ring lands ON the polygon's edge
        /// rather than on the circle through its vertices.</summary>
        public static float PolygonRadiusAt(float r, int n, float ang)
        {
            if (n < 3) return r;
            float seg = 2f * Mathf.PI / n;
            float rel = ang - Mathf.PI / 2f;
            rel -= Mathf.Floor(rel / seg) * seg;   // into [0, seg)
            return r * Mathf.Cos(Mathf.PI / n) / Mathf.Max(1e-4f, Mathf.Cos(rel - Mathf.PI / n));
        }

        /// <summary>
        /// The ORDERED (distribution 0) interior layout: concentric rings, ring count ≈ √(n/π) so density is
        /// roughly even, each ring's share of the instances proportional to its radius, and alternating rings
        /// half-step offset so nothing lines up on radial spokes. Ported from Pyre's <c>RingLayoutDisc</c>
        /// (<c>PyreRenderer.cs:2440</c>). <paramref name="sides"/> 0 gives a disc; 3+ scales each ring onto
        /// that polygon's own boundary.
        ///
        /// Honest scope limit vs Pyre: Pyre gives Triangle/Square/Hexagon a dedicated lattice each because one
        /// generic ring construction read visibly uneven for a triangle. This port keeps the one boundary-
        /// scaled ring construction for every polygon — good enough to read as ordered, not identical to
        /// Pyre's per-kind lattices. Filed rather than hidden.
        /// </summary>
        public static Vector2 RingLayout(float radius, int sides, int n, int i, bool reverse)
        {
            if (n <= 1) return Vector2.zero;
            int rings = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(n / Mathf.PI)), 1, n);
            int[] counts = RingCounts(n, rings);

            int idx = i;
            for (int step = 0; step < rings; step++)
            {
                int k = reverse ? rings - 1 - step : step;
                int count = counts[k];
                if (idx < count)
                {
                    float frac = (k + 1) / (float)rings;
                    float ang = count > 0 ? idx / (float)count * 2f * Mathf.PI : 0f;
                    if (k % 2 == 1) ang += Mathf.PI / Mathf.Max(1, count);
                    float rr = (sides >= 3 ? PolygonRadiusAt(radius, sides, ang) : radius) * frac;
                    return new Vector2(rr * Mathf.Cos(ang), rr * Mathf.Sin(ang));
                }
                idx -= count;
            }
            return Vector2.zero;   // unreachable — the counts sum to exactly n
        }

        /// <summary>How many of <paramref name="n"/> instances each ring gets, weighted by radius so an outer
        /// ring — with more circumference — earns proportionally more. Rounding drift is absorbed by the
        /// outermost ring, where a ±1 reads least.</summary>
        static int[] RingCounts(int n, int rings)
        {
            var counts = new int[rings];
            int totalWeight = rings * (rings + 1) / 2;
            int assigned = 0;
            for (int k = 0; k < rings; k++)
            {
                counts[k] = Mathf.Max(1, n * (k + 1) / totalWeight);
                assigned += counts[k];
            }
            counts[rings - 1] = Mathf.Max(1, counts[rings - 1] + (n - assigned));
            return counts;
        }

        /// <summary>A uniform-by-AREA point in a disc: √-distributed radius, uniform angle, so points do not
        /// clump at the centre.</summary>
        public static Vector2 SampleDisc(float radius, uint seed, int i)
        {
            float rr = radius * Mathf.Sqrt(Unit(seed, i, FldPlaceA));
            float ang = Unit(seed, i, FldPlaceB) * 2f * Mathf.PI;
            return new Vector2(rr * Mathf.Cos(ang), rr * Mathf.Sin(ang));
        }

        /// <summary>A uniform-by-AREA point in a regular polygon: fan-triangulate into congruent triangles,
        /// pick one uniformly, then draw a uniform barycentric point in it (the √ makes the triangle draw
        /// uniform rather than centre-biased).</summary>
        public static Vector2 SamplePolygonArea(float r, int sides, uint seed, int i)
        {
            float u0 = Unit(seed, i, FldPlaceA);
            float u1 = Unit(seed, i, FldPlaceB);
            float u2 = Unit(seed, i, FldPlaceC);

            int tri = Mathf.Clamp((int)(u0 * sides), 0, sides - 1);
            Vector2 a = PolyVertex(r, tri, sides);
            Vector2 b = PolyVertex(r, tri + 1, sides);

            float s = Mathf.Sqrt(u1);
            return a * (s * (1f - u2)) + b * (s * u2);   // the third weight is the centre, which is the origin
        }

        /// <summary>
        /// The spawner's own transform, applied to a placed point: a 2D rotation, then a pseudo-3D yaw (about
        /// the vertical axis) and pitch (about the horizontal), depth-normalised against the radius and given
        /// a mild perspective spread so a tilted ring reads as tilted rather than merely squashed. Ported
        /// verbatim in behaviour from <c>PyreRenderer.ApplyShapeTransform</c>. Every step is guarded, so a
        /// spawner at its defaults returns the point unchanged.
        /// </summary>
        public static Vector2 ApplySpawnerTransform(Vector2 local, float r, float rot, float yaw, float pitch,
                                                    out float zNorm)
        {
            float x = local.x, y = local.y, z = 0f;

            if (rot != 0f)
            {
                float a = rot * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float nx = x * c - y * s;
                y = x * s + y * c;
                x = nx;
            }
            if (yaw != 0f)
            {
                float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float nx = x * c - z * s;
                z = x * s + z * c;
                x = nx;
            }
            if (pitch != 0f)
            {
                float a = pitch * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float ny = y * c - z * s;
                z = y * s + z * c;
                y = ny;
            }

            zNorm = Mathf.Clamp(z / Mathf.Max(1e-3f, r), -1f, 1f);
            float persp = 1f + 0.25f * zNorm;
            return new Vector2(x * persp, y * persp);
        }

        /// <summary>
        /// The UNTRANSFORMED local placement of instance <paramref name="posIdx"/> — a point in or on the
        /// shape, centred on the origin, radius already baked in. The caller applies the spawner transform and
        /// offset afterwards.
        /// </summary>
        /// <param name="progress">Path mode only: where along the outline the instance sits. Closed shapes
        /// WRAP (so a progress above 1 means further laps), matching Pyre.</param>
        public static Vector2 Place(ShaperSwarmShape kind, ShaperSwarmSpawnMode mode, float radius, int n,
                                    int posIdx, float distribution, bool gridReverse, float progress, uint seed)
        {
            if (kind == ShaperSwarmShape.None) return Vector2.zero;

            if (kind == ShaperSwarmShape.Line)
            {
                float f = n > 1 ? posIdx / (float)(n - 1) : 0f;
                return new Vector2((f * 2f - 1f) * radius, 0f);
            }

            int sides = SideCount(kind);

            if (mode == ShaperSwarmSpawnMode.Area)
            {
                float chaos = Mathf.Clamp01(distribution);
                Vector2 ordered = RingLayout(radius, sides, n, posIdx, gridReverse);
                if (chaos <= 0f) return ordered;
                Vector2 scattered = sides >= 3
                    ? SamplePolygonArea(radius, sides, seed, posIdx)
                    : SampleDisc(radius, seed, posIdx);
                if (chaos >= 1f) return scattered;
                return Vector2.Lerp(ordered, scattered, chaos);
            }

            float p = progress - Mathf.Floor(progress);   // wrap: whole laps fold back to the path start

            if (sides < 3)
            {
                // Circle — counter-clockwise from the top, so progress 0 and 1 are the same point.
                float ang = Mathf.PI / 2f + 2f * Mathf.PI * p;
                return new Vector2(radius * Mathf.Cos(ang), radius * Mathf.Sin(ang));
            }

            // Regular n-gon: every side is the same length, so p·sides IS the (side index + fraction)
            // arc-length parameter — no need to accumulate side lengths.
            float t = p * sides;
            int k = (int)t;
            float frac = t - k;
            if (k >= sides) { k = sides - 1; frac = 1f; }
            return Vector2.Lerp(PolyVertex(radius, k, sides), PolyVertex(radius, k + 1, sides), frac);
        }

        /// <summary>The outline TANGENT at the same point <see cref="Place"/> would return in Path mode —
        /// direction only, the caller takes its angle. Zero when the shape has no outline tangent to speak of,
        /// which the caller reads as "fall back to Outward".</summary>
        public static Vector2 PathTangent(ShaperSwarmShape kind, float radius, float progress)
        {
            int sides = SideCount(kind);
            float p = progress - Mathf.Floor(progress);

            if (kind == ShaperSwarmShape.None || kind == ShaperSwarmShape.Line) return Vector2.zero;

            if (sides < 3)
            {
                float ang = Mathf.PI / 2f + 2f * Mathf.PI * p;
                return new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang));
            }

            float t = p * sides;
            int k = (int)t;
            if (k >= sides) k = sides - 1;
            return PolyVertex(radius, k + 1, sides) - PolyVertex(radius, k, sides);
        }

        /// <summary>
        /// Which POSITION slot each SPAWN slot reveals. At order-chaos 0 the two coincide, except in Area mode
        /// where the base rank is a greedy nearest-neighbour walk over the placements — so consecutive spawn
        /// moments reveal spatial NEIGHBOURS ("fill from one side") rather than whatever order the ring
        /// construction happened to produce. At 1 the mapping is a full seeded shuffle. Ported from Pyre's
        /// <c>BuildSpawnPermutation</c> (<c>PyreRenderer.cs:2691</c>).
        ///
        /// Only meaningful when the timing mode gives instances distinct birth moments — under Stagger every
        /// instance exists for the whole document, so which one appears "first" is not observable.
        /// </summary>
        public static int[] BuildSpawnPermutation(ShaperSwarmShape kind, ShaperSwarmSpawnMode mode, float radius,
                                                  int n, float orderChaos, float distribution, bool gridReverse,
                                                  uint seed)
        {
            var perm = new int[n];
            for (int i = 0; i < n; i++) perm[i] = i;
            if (n <= 1) return perm;

            int[] baseRank;
            if (mode == ShaperSwarmSpawnMode.Area && kind != ShaperSwarmShape.None && kind != ShaperSwarmShape.Line)
            {
                var pts = new Vector2[n];
                for (int j = 0; j < n; j++)
                    pts[j] = Place(kind, mode, radius, n, j, distribution, gridReverse, 0f, seed);
                baseRank = InvertOrder(GreedyNeighbourWalk(pts));
            }
            else
            {
                baseRank = perm;   // index order already IS neighbour order along a line or an outline
            }

            if (orderChaos <= 0f)
            {
                var rankCopy = baseRank;
                System.Array.Sort(perm, (a, b) => rankCopy[a].CompareTo(rankCopy[b]));
                return perm;
            }

            var keys = new float[n];
            for (int i = 0; i < n; i++)
                keys[i] = baseRank[i] + orderChaos * n * Unit(seed, i, FldOrder);
            System.Array.Sort(perm, (a, b) => keys[a].CompareTo(keys[b]));
            return perm;
        }

        /// <summary>Greedy nearest-neighbour walk from index 0. Not an optimal tour and does not need to be —
        /// it only has to read as "hop to the next one along", which it does for an evenly spread cloud.
        /// O(n²), negligible at a swarm's 64-instance ceiling.</summary>
        static int[] GreedyNeighbourWalk(Vector2[] points)
        {
            int n = points.Length;
            var order = new int[n];
            var visited = new bool[n];
            int current = 0;
            visited[0] = true;
            for (int step = 1; step < n; step++)
            {
                int best = -1;
                float bestDist = float.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (visited[j]) continue;
                    float d = (points[j] - points[current]).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = j; }
                }
                if (best < 0) break;
                visited[best] = true;
                order[step] = best;
                current = best;
            }
            return order;
        }

        static int[] InvertOrder(int[] order)
        {
            var rank = new int[order.Length];
            for (int k = 0; k < order.Length; k++) rank[order[k]] = k;
            return rank;
        }
    }
}
