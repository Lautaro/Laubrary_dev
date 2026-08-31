// TapestryCircuitLinesGenerator — a faithful-as-practical C# port of the Kiln "lines" agent (Tapestry Shape
// project, agents/lines/gen.py, generation 8): 45-degree self-avoiding walkers on a wrapped cell lattice,
// rasterised as capsule SDFs and stamped into an elevation field (raised ridge or recessed channel), with a
// true-circular-arc fillet at every corner drawn from one stock radius per tile, a double-bevel "shoulder"
// plinth under thick/trunk traces, and terminal pads (with an optional via) at every open trail end. Ported
// from a pure HEIGHT-field generator (0 = base, + = raised, - = recessed) into Tapestry's neutral-tint +
// alpha-from-relief convention, so this generator can sit under a Multiply blend on any Surface-style layer
// (an engraved-line look on any material) or stand alone under Normal blend (pale lines on transparency) — see
// TapestryGenerator's Shape/Surface split. Sibling to TapestryLinesGenerator (the older, simpler walker); this
// is the more advanced one, not a replacement.
//
// Simplifications made porting a 2150-line, 8-generation-evolved algorithm into one file with no compiler
// available this session — disclosed rather than silently dropped:
//   - CLOSED-LOOP trails (algebraic closure solving a Diophantine system over the 8-compass, `_solve_close`/
//     `try_close` in the source) are not ported. Every trail here is OPEN and terminates in a pad at both
//     ends, which is what the source's `terminalMode="pads"` already looked like.
//   - The FIGURE system (ring / plate / comb / rivets closed objects, their footprint reservation with
//     retry-then-shrink placement, and the figure<->trace docking/linking connector geometry) is not ported —
//     it is roughly 500 lines of the source on its own, most of it about placing and clearance-testing a
//     SECOND kind of object against the walk. In its place, a much smaller "tick" detail (a short perpendicular
//     stub stamped at one interior point of a trace, same elevation tier as its parent) stands in for the
//     "occasional small perpendicular tick-mark" reading the reference renders have, without the closed-shape
//     vocabulary (box/octagon/circle rings and plates, rivet rims, comb spines) or the docking/linking system.
//   - The REST-FLOOR negative-space cascade (`_n_rest`, dropping whichever trail buys back the most empty
//     coarse blocks) is not ported; the sparse traceCount range (2-4) plus the length/span/run floors already
//     keep a tile from reading as busy, which is what the reference praise ("very clean," "only one strong
//     feature") was actually about.
//   - The AREA-CEILING cascade IS ported, in the source's own order (drop the fattest trail one at a time,
//     then drop shoulders, then binary-search a uniform width scale down to a pixel floor) — this is the part
//     of the source that explicitly warns an analytic ink estimate is wrong, so it stays MEASURED off the
//     actual rasterised height field, exactly like the source.
//   - The deterministic RNG is a counter-driven hash stream built from the SAME hash-by-coordinate formula
//     TapestryPanelsGenerator's HashCell uses (not System.Random) — the source's own `random.Random(seed)` is
//     a plain sequential stream consumed once to author the whole tile, and reproducing that role with a hash
//     of an incrementing counter keeps this generator off System.Random while still giving every draw in the
//     stream a fully deterministic value from the layer's seed. Tileability itself comes from the same place
//     it does in the source: toroidal wrapping of lattice coordinates and 9-copy stamping of every capsule
//     segment (mirrors tapshape/canvas.py's SdfCanvas.segment_sdf and TapestryLinesGenerator.StampSegment), not
//     from the RNG.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Laubrary.Tapestry
{
    public enum CircuitCurveMode { None, Tight, Sweep }
    public enum CircuitSignMode { Raised, Recessed, Mixed }
    public enum CircuitBevelProfile { Smooth, Linear, Chamfer, Step }

    [TapestryGeneratorInfo("Circuit Lines", "Shape")]
    [System.Serializable]
    public class TapestryCircuitLinesGenerator : TapestryGenerator
    {
        // -- lattice / walk --------------------------------------------------------------------
        [Range(14, 28)] public int gridSize = 20;
        [Range(2, 4)] public int traceCount = 3;
        // one compass heading for the whole tile: every walker's STARTING run leaves on this heading's
        // 90-degree family (axis, axis+90, axis+180, axis+270), so a tile is either an orthogonal part or a
        // diagonal one rather than an arbitrary mixture — turns are still free to diverge from there.
        [Range(0, 7)] public int tileAxis = 0;
        [Range(1.0f, 1.9f)] public float minLengthFrac = 1.4f;
        [Range(0.060f, 0.150f)] public float separation = 0.10f;
        [Range(0.20f, 0.45f)] public float minRunFrac = 0.30f;
        [Range(2, 6)] public int maxTurns = 4;
        [Range(0f, 0.55f)] public float turn90Chance = 0.30f;

        // -- stroke geometry ---------------------------------------------------------------------
        [Range(0.013f, 0.030f)] public float traceWidth = 0.017f;
        [Range(1.6f, 3.4f)] public float trunkWidthMul = 2.2f;
        [Range(0.15f, 0.70f)] public float trunkShare = 0.30f;
        [Range(0.030f, 0.150f)] public float coverageCap = 0.070f;

        // -- curves: ONE true circular-arc fillet radius for every corner in the tile ------------
        public CircuitCurveMode curveMode = CircuitCurveMode.Sweep;
        [Range(0, 3)] public int cornerRung = 1;

        // -- elevation -----------------------------------------------------------------------------
        [Range(0.80f, 1.60f)] public float elevation = 1.1f;
        [Range(0.45f, 1.0f)] public float thinTierMul = 0.7f;
        public CircuitSignMode signMode = CircuitSignMode.Mixed;
        [Range(0.50f, 1.30f)] public float recessDepth = 0.7f;
        // the double-bevel: a wide, shallow plinth under a trace, ramped LINEAR and narrow so it terminates
        // in a real edge rather than haloing into the background.
        [Range(0.55f, 1.0f)] public float shoulderShare = 0.7f;
        [Range(0.22f, 0.50f)] public float shoulderTier = 0.30f;

        // -- bevel -------------------------------------------------------------------------------
        [Range(0.15f, 0.55f)] public float bevelRatio = 0.35f;
        public CircuitBevelProfile profile = CircuitBevelProfile.Smooth;

        // -- terminals -----------------------------------------------------------------------------
        [Range(1.4f, 2.6f)] public float padRadiusMul = 2.0f;
        [Range(0.10f, 0.55f)] public float padTier = 0.30f;
        [Range(0f, 0.9f)] public float viaDepth = 0.3f;

        // -- detail ticks: simplified stand-in for the source's ring/plate/comb/rivets figures ----
        [Range(0f, 1f)] public float tickChance = 0.30f;
        [Range(1, 3)] public int tickMaxPerTrace = 1;

        // -- material: Shape/Surface split. Neutral tint, modulated by the trace's OWN bevel highlight/
        // shadow; alpha is how "line" each pixel is (derived from the height field's own relief), so this
        // generator reads correctly under Multiply on any Surface layer, or stands alone under Normal.
        public Color tint = new Color(0.86f, 0.87f, 0.90f, 1f);
        public Color highlightColor = new Color(1f, 1f, 1f, 1f);
        public Color shadowColor = new Color(0.05f, 0.05f, 0.06f, 1f);
        [Range(0f, 2f)] public float bevelStrength = 1f;
        [Range(0f, 360f)] public float lightAngle = 135f;

        public int seed = 0;

        public override string DisplayName => "Circuit Lines";
        public override string Description =>
            "45-degree self-avoiding circuit traces on a wrapped lattice — a true circular-arc fillet at every "
            + "corner from one stock radius per tile, a double-bevel plinth under trunk traces, terminal pads "
            + "with an optional via, and occasional perpendicular tick details. Neutral tint + relief-driven "
            + "alpha, so it engraves into any Surface layer under Multiply or stands alone under Normal. "
            + "Always seamless, no matter the settings.";

        // -- constants carried over from the source, unchanged in meaning -----------------------
        const float ShoulderMul = 2.0f;
        const float PlateauMin = 0.50f;
        const float BevelAbsMax = 0.010f;
        const float ShoulderBevelMax = 0.008f;
        const float MinStrokePx = 3.0f;
        const float MinRelief = 0.35f;
        const float MinSpanFrac = 0.45f;
        const float RunMaxMul = 1.15f;
        const int TurnAttempts = 160;

        static readonly Vector2Int[] Dirs8 =
        {
            new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1),
            new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
        };

        // the ONLY radii a tile may draw its one fillet from, in canvas_frac (x gridSize = lattice cells).
        static readonly float[] ArcLadder = { 0.012f, 0.016f, 0.022f, 0.030f, 0.045f, 0.065f, 0.090f, 0.120f };
        const int ArcTightMax = 4;
        static readonly float[] ArcStepDown = { 1.0f, 0.60f, 0.35f };
        const float ArcTanShare = 0.42f;
        const float ArcMaxTanShare = 0.40f;
        const float ArcSampleDeg = 4.0f;
        const int ArcSampleMin = 4, ArcSampleMax = 40;

        // ------------------------------------------------------------------------------- entry point
        public override void Generate(in TapestryGenCtx ctx, Color32[] target)
        {
            int W = ctx.width, H = ctx.height;
            var rng = new HashRng(seed + ctx.seed);

            int grid = Mathf.Max(4, gridSize);
            int spacing = Mathf.Clamp(Mathf.RoundToInt(separation * grid), 1, 5);
            int minRun = Mathf.Max(3, Mathf.RoundToInt(minRunFrac * grid));
            int runMax = Mathf.Max(minRun + 1, Mathf.RoundToInt(grid * RunMaxMul));
            int count = Mathf.Max(1, traceCount);
            int minLen = Mathf.RoundToInt(minLengthFrac * grid);
            minLen = Mathf.Max(12, Mathf.Max(2 * minRun, Mathf.Min(minLen, Mathf.RoundToInt(grid * 2f))));
            int maxLen = Mathf.Max(minLen + 2, Mathf.RoundToInt(grid * 1.9f));
            int[] dirs0 = AxisFamily(tileAxis);
            float corridor = 0.8f * spacing / (float)grid;
            float minPx = 1f / Mathf.Max(W, H);

            float recessP = signMode == CircuitSignMode.Raised ? 0f
                : signMode == CircuitSignMode.Recessed ? 1f : 0.5f;

            float cornerUv = RungRadius(cornerRung, curveMode);
            float cornerCells = cornerUv * grid;
            int maxRunCells = Mathf.Max(2, grid / 2);

            // ---------------------------------------------------------------------------- the walk
            var trails = WalkAll(ref rng, grid, count, minLen, spacing, minRun, runMax, turn90Chance,
                maxTurns, maxLen, dirs0, TurnAttempts);
            if (trails.Count == 0 && spacing > 1)
                trails = WalkAll(ref rng, grid, count, minLen, spacing - 1, minRun, runMax, turn90Chance,
                    maxTurns, maxLen, dirs0, TurnAttempts);

            // ------------------------------------------------------------------- per-trail styling
            var traces = new List<Trace>();
            foreach (var wr in trails)
            {
                bool isTrunk = rng.Chance(trunkShare);
                bool isRecessed = rng.Chance(recessP);
                bool hasShoulder = rng.Chance(shoulderShare);
                bool hasTick = tickMaxPerTrace > 0 && wr.cells.Count >= 8 && rng.Chance(tickChance);

                var verts = PolylineVerts(wr.cells, wr.dirs);
                var curved = CurvePath(verts, cornerCells, grid);
                var path = SplitLong(curved, maxRunCells);

                float width = traceWidth * (isTrunk ? trunkWidthMul : 1f);
                float tier = isTrunk ? 1f : thinTierMul;
                float mag = Mathf.Max((isRecessed ? recessDepth : elevation) * tier, MinRelief);
                float elev = isRecessed ? -mag : mag;
                float shoulderWidth = hasShoulder ? Mathf.Min(width * ShoulderMul, width + 1.5f * corridor) : 0f;

                var tr = new Trace
                {
                    cells = wr.cells,
                    dirs = wr.dirs,
                    path = path,
                    trunk = isTrunk,
                    recessed = isRecessed,
                    width = width,
                    shoulderWidth = shoulderWidth,
                    elevation = elev,
                    ink = wr.cells.Count * (width + shoulderWidth),
                    ticks = new List<(Vector2Int cell, Vector2 perp)>(),
                };

                if (hasTick)
                {
                    int tickCount = Mathf.Max(1, rng.Range(1, tickMaxPerTrace));
                    for (int k = 0; k < tickCount; k++)
                    {
                        int idx = rng.Range(2, Mathf.Max(2, wr.cells.Count - 3));
                        idx = Mathf.Clamp(idx, 0, wr.cells.Count - 1);
                        var cell = wr.cells[idx];
                        int dIdx = Mathf.Clamp(idx, 0, wr.dirs.Count - 1);
                        Vector2Int dv8 = wr.dirs.Count > 0 ? Dirs8[wr.dirs[dIdx]] : new Vector2Int(1, 0);
                        var dv = new Vector2(dv8.x, dv8.y);
                        var perp = new Vector2(-dv.y, dv.x);
                        perp = perp.sqrMagnitude > 1e-8f ? perp.normalized : Vector2.up;
                        tr.ticks.Add((cell, perp));
                    }
                }
                traces.Add(tr);
            }

            // ------------------------------------------------------------- area-ceiling cascade
            var (height, maxRelief) = RenderHeight(W, H, grid, traces, 1f, corridor, minPx);
            float cov = Coverage(height);

            while (traces.Count > 1 && cov > coverageCap)
            {
                var gone = traces.OrderByDescending(s => s.ink).First();
                var next = new List<Trace>(traces);
                next.Remove(gone);
                var (h2, mr2) = RenderHeight(W, H, grid, next, 1f, corridor, minPx);
                float c2 = Coverage(h2);
                if (c2 >= cov) break;   // dropping the fattest trail did not actually help; accept as-is
                traces = next; height = h2; maxRelief = mr2; cov = c2;
            }

            if (cov > coverageCap && traces.Any(s => s.shoulderWidth > 0f))
            {
                foreach (var s in traces) s.shoulderWidth = 0f;
                (height, maxRelief) = RenderHeight(W, H, grid, traces, 1f, corridor, minPx);
                cov = Coverage(height);
            }

            if (cov > coverageCap && traces.Count > 0)
            {
                float thinnest = traces.Min(s => s.width);
                float lo = Mathf.Min(1f, (MinStrokePx / Mathf.Max(W, H)) / Mathf.Max(thinnest, 1e-6f));
                var (floorHeight, floorRelief) = RenderHeight(W, H, grid, traces, lo, corridor, minPx);
                float floorCov = Coverage(floorHeight);
                if (floorCov >= coverageCap)
                {
                    height = floorHeight; maxRelief = floorRelief;
                }
                else
                {
                    float hi = 1f;
                    var bestHeight = floorHeight; var bestRelief = floorRelief;
                    for (int iter = 0; iter < 5; iter++)
                    {
                        float mid = 0.5f * (lo + hi);
                        var (probe, probeRelief) = RenderHeight(W, H, grid, traces, mid, corridor, minPx);
                        float pc = Coverage(probe);
                        if (pc > coverageCap) hi = mid;
                        else { lo = mid; bestHeight = probe; bestRelief = probeRelief; }
                    }
                    height = bestHeight; maxRelief = bestRelief;
                }
            }

            // --------------------------------------------------------------------- final colour pass
            Vector2 lightDir = new Vector2(Mathf.Cos(lightAngle * Mathf.Deg2Rad), Mathf.Sin(lightAngle * Mathf.Deg2Rad));
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float h = height[i];
                    if (Mathf.Abs(h) < 1e-5f) { target[i] = new Color32(0, 0, 0, 0); continue; }
                    Vector2 grad = HeightGradient(height, W, H, x, y);
                    float lit = Vector2.Dot(grad, lightDir) * bevelStrength;
                    Color shaded = lit >= 0f
                        ? Color.Lerp(tint, highlightColor, Mathf.Clamp01(lit))
                        : Color.Lerp(tint, shadowColor, Mathf.Clamp01(-lit));
                    float alpha = Mathf.Clamp01(Mathf.Abs(h) / Mathf.Max(maxRelief, 1e-4f));
                    shaded.a = tint.a * alpha;
                    target[i] = shaded;
                }
            }
        }

        // ------------------------------------------------------------------------------- rasterising
        class Trace
        {
            public List<Vector2Int> cells;
            public List<int> dirs;
            public List<Vector2> path;         // arc-filleted + split, UNWRAPPED lattice-cell units
            public bool trunk;
            public bool recessed;
            public float width;                // UV, unscaled by the area-ceiling's width scale
            public float shoulderWidth;         // UV, unscaled; 0 = no plinth
            public float elevation;             // signed target elevation
            public float ink;                   // ordering key only, never a measurement
            public List<(Vector2Int cell, Vector2 perp)> ticks;
        }

        (float[] height, float maxRelief) RenderHeight(int W, int H, int grid, List<Trace> sel, float widthScale,
            float corridor, float minPx)
        {
            var height = new float[W * H];
            float maxRelief = MinRelief;

            // 1. trail shoulders (plinths) — linear ramp, narrow, so they terminate in a real edge
            foreach (var s in sel)
            {
                float sw = s.shoulderWidth * widthScale;
                if (sw <= 0f) continue;
                float elev = s.elevation * shoulderTier;
                maxRelief = Mathf.Max(maxRelief, Mathf.Abs(elev));
                float bw = ShoulderBevel(sw, minPx);
                for (int i = 0; i < s.path.Count - 1; i++)
                {
                    var uv = SegUV(s.path[i], s.path[i + 1], grid);
                    StampCapsule(height, W, H, uv.ax, uv.ay, uv.bx, uv.by, sw, elev, bw, CircuitBevelProfile.Linear);
                }
            }

            // 2. channels (recessed), then traces (raised) — matches the source's pass order exactly
            for (int pass = 0; pass < 2; pass++)
            {
                bool wantRecessed = pass == 0;
                foreach (var s in sel)
                {
                    if (s.recessed != wantRecessed) continue;
                    float w = s.width * widthScale;
                    maxRelief = Mathf.Max(maxRelief, Mathf.Abs(s.elevation));
                    float bw = BevelWidth(w, bevelRatio, s.trunk, minPx);
                    for (int i = 0; i < s.path.Count - 1; i++)
                    {
                        var uv = SegUV(s.path[i], s.path[i + 1], grid);
                        StampCapsule(height, W, H, uv.ax, uv.ay, uv.bx, uv.by, w, s.elevation, bw, profile);
                    }
                }
            }

            // 3. terminal pads + vias, at every open end
            foreach (var s in sel)
            {
                if (s.cells.Count == 0) continue;
                for (int e = 0; e < 2; e++)
                {
                    var cell = e == 0 ? s.cells[0] : s.cells[s.cells.Count - 1];
                    float r0 = Mathf.Min(s.width * padRadiusMul * 0.5f, corridor + traceWidth * 0.5f);
                    float r = r0 * widthScale;
                    if (r <= 0.5f * minPx) continue;
                    var (ux, uy) = UV(cell, grid);
                    float padElev = s.elevation * (1f + padTier);
                    maxRelief = Mathf.Max(maxRelief, Mathf.Abs(padElev));
                    float bw = PadBevel(r, bevelRatio, minPx);
                    StampCapsule(height, W, H, ux, uy, ux, uy, r * 2f, padElev, bw, CircuitBevelProfile.Smooth);

                    if (viaDepth > 0.02f && r > s.width * widthScale * 0.9f)
                    {
                        float vr = r * 0.42f;
                        maxRelief = Mathf.Max(maxRelief, viaDepth);
                        StampCapsule(height, W, H, ux, uy, ux, uy, vr * 2f, -viaDepth,
                            Mathf.Max(minPx, r * 0.25f), CircuitBevelProfile.Smooth);
                    }
                }
            }

            // 4. detail ticks — simplified stand-in for the source's figures, same elevation as their trace
            foreach (var s in sel)
            {
                if (s.ticks == null) continue;
                float tw = s.width * widthScale * 0.7f;
                if (tw <= 0f) continue;
                float bw = BevelWidth(tw, bevelRatio, false, minPx);
                foreach (var (cell, perp) in s.ticks)
                {
                    float half = Mathf.Max(1.2f, s.width * grid * widthScale * 1.3f);
                    Vector2 center = new Vector2(cell.x, cell.y);
                    Vector2 a = center + perp * half, b = center - perp * half;
                    maxRelief = Mathf.Max(maxRelief, Mathf.Abs(s.elevation));
                    var uv = SegUV(a, b, grid);
                    StampCapsule(height, W, H, uv.ax, uv.ay, uv.bx, uv.by, tw, s.elevation, bw, profile);
                }
            }

            return (height, maxRelief);
        }

        static float Coverage(float[] height)
        {
            int n = 0;
            for (int i = 0; i < height.Length; i++) if (Mathf.Abs(height[i]) > 0.02f) n++;
            return n / (float)height.Length;
        }

        // -- bevel width helpers, ported 1:1 from the source's _bevel_width / _shoulder_bevel / _pad_bevel --
        static float BevelWidth(float w, float ratio, bool wide, float minPx)
        {
            float half = w * 0.5f;
            float bw = half * ratio * (wide ? 1.35f : 1.0f);
            bw = Mathf.Min(bw, half * (1.0f - PlateauMin));
            return Mathf.Max(minPx, Mathf.Min(bw, BevelAbsMax));
        }

        static float ShoulderBevel(float sw, float minPx) =>
            Mathf.Max(minPx, Mathf.Min(sw * 0.16f, ShoulderBevelMax));

        static float PadBevel(float r, float ratio, float minPx) =>
            Mathf.Max(minPx, Mathf.Min(Mathf.Min(r * ratio * 0.9f, r * (1.0f - PlateauMin)), BevelAbsMax));

        // -- capsule stamping: elevation-aware composition (raised = max, recessed = min against what's
        // already there), evaluated at all 9 toroidal copies so a segment crossing the seam reads correctly
        // on both sides of it — mirrors tapshape/canvas.py's SdfCanvas.segment_sdf + HeightCanvas.stamp, and
        // TapestryLinesGenerator's own StampSegment/StampSegmentLocal pattern. A disc/pad is the degenerate
        // capsule a==b.
        static void StampCapsule(float[] height, int W, int H, float ax, float ay, float bx, float by,
            float fullWidth, float elev, float profWidth, CircuitBevelProfile profile)
        {
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                    StampCapsuleLocal(height, W, H, ax + ox, ay + oy, bx + ox, by + oy, fullWidth, elev, profWidth, profile);
        }

        static void StampCapsuleLocal(float[] height, int W, int H, float pax, float pay, float pbx, float pby,
            float fullWidth, float elev, float profWidth, CircuitBevelProfile profile)
        {
            float rHalf = fullWidth * 0.5f;
            float pad = rHalf + profWidth + 1f / Mathf.Max(W, H);
            int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(pax, pbx) - pad) * W));
            int maxX = Mathf.Min(W - 1, Mathf.CeilToInt((Mathf.Max(pax, pbx) + pad) * W));
            int minY = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(pay, pby) - pad) * H));
            int maxY = Mathf.Min(H - 1, Mathf.CeilToInt((Mathf.Max(pay, pby) + pad) * H));
            if (minX > maxX || minY > maxY) return;

            Vector2 A = new Vector2(pax, pay), B = new Vector2(pbx, pby);
            Vector2 AB = B - A;
            float abLenSq = Mathf.Max(1e-9f, AB.sqrMagnitude);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / W, (y + 0.5f) / H);
                    float t = Mathf.Clamp01(Vector2.Dot(p - A, AB) / abLenSq);
                    Vector2 closest = A + AB * t;
                    float d = Vector2.Distance(p, closest) - rHalf;
                    float tt = ProfileT(d, profWidth, profile);
                    if (tt <= 0f) continue;
                    int idx = y * W + x;
                    float target = height[idx] * (1f - tt) + elev * tt;
                    height[idx] = elev >= 0f ? Mathf.Max(height[idx], target) : Mathf.Min(height[idx], target);
                }
            }
        }

        static float ProfileT(float sdf, float width, CircuitBevelProfile profile)
        {
            float raw = Mathf.Clamp01(-sdf / Mathf.Max(width, 1e-6f));
            switch (profile)
            {
                case CircuitBevelProfile.Linear:
                case CircuitBevelProfile.Chamfer:
                    return raw;
                case CircuitBevelProfile.Step:
                    return raw > 0.5f ? 1f : 0f;
                default:
                    return raw * raw * (3f - 2f * raw); // smooth (smoothstep)
            }
        }

        static Vector2 HeightGradient(float[] h, int W, int H, int x, int y)
        {
            int xL = Mod(x - 1, W), xR = Mod(x + 1, W), yU = Mod(y - 1, H), yD = Mod(y + 1, H);
            float gx = (h[y * W + xR] - h[y * W + xL]) * 0.5f;
            float gy = (h[yD * W + x] - h[yU * W + x]) * 0.5f;
            return new Vector2(gx, gy);
        }

        // -------------------------------------------------------------------------- path -> capsules
        static List<Vector2> PolylineVerts(List<Vector2Int> cells, List<int> dirs)
        {
            float x = cells[0].x, y = cells[0].y;
            var pts = new List<Vector2> { new Vector2(x, y) };
            foreach (int cd in dirs)
            {
                var d = Dirs8[cd];
                x += d.x; y += d.y;
                pts.Add(new Vector2(x, y));
            }

            var verts = new List<Vector2> { pts[0] };
            int i = 0;
            while (i < dirs.Count)
            {
                int j = i;
                while (j < dirs.Count && dirs[j] == dirs[i]) j++;
                verts.Add(pts[j]);
                i = j;
            }
            return verts;
        }

        // -- true circular-arc corner fillets, one stock radius for the whole tile ------------------
        static (float theta, float sign) TurnAngle(Vector2 u, Vector2 w)
        {
            float cross = u.x * w.y - u.y * w.x;
            float dot = Mathf.Clamp(u.x * w.x + u.y * w.y, -1f, 1f);
            float theta = Mathf.Acos(dot);
            return (theta, cross > 0f ? 1f : -1f);
        }

        static float TangentLen(float theta, float r) => r * Mathf.Tan(theta * 0.5f);

        static List<Vector2> Arc(Vector2 a, Vector2 v, Vector2 b, float r, float turnSign, float theta)
        {
            Vector2 u = v - a;
            float n = u.magnitude;
            if (n < 1e-6f) return new List<Vector2> { a, b };
            u /= n;
            Vector2 normal = new Vector2(-u.y, u.x) * turnSign;
            Vector2 c = a + normal * r;
            float a0 = Mathf.Atan2(a.y - c.y, a.x - c.x);
            int steps = Mathf.Clamp(Mathf.RoundToInt(theta * Mathf.Rad2Deg / ArcSampleDeg), ArcSampleMin, ArcSampleMax);
            var outPts = new List<Vector2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float ang = a0 + turnSign * theta * (i / (float)steps);
                outPts.Add(new Vector2(c.x + r * Mathf.Cos(ang), c.y + r * Mathf.Sin(ang)));
            }
            outPts[outPts.Count - 1] = b;
            return outPts;
        }

        static List<Vector2> CurvePath(List<Vector2> verts, float radiusCells, int grid)
        {
            if (verts.Count < 2 || radiusCells <= 0f) return new List<Vector2>(verts);
            int m = verts.Count - 1;
            var lens = new float[m];
            for (int i = 0; i < m; i++) lens[i] = Vector2.Distance(verts[i], verts[i + 1]);

            Vector2 Heading(int i)
            {
                float L = Mathf.Max(lens[i], 1e-6f);
                return (verts[i + 1] - verts[i]) / L;
            }

            float cap = ArcMaxTanShare * grid;
            var cornerT = new float?[m + 1];
            var cornerR = new float[m + 1];
            var cornerTheta = new float[m + 1];
            var cornerSign = new float[m + 1];

            for (int j = 1; j < m; j++)
            {
                var (theta, sign) = TurnAngle(Heading(j - 1), Heading(j));
                if (theta < 1e-6f) continue;
                float room = Mathf.Min(ArcTanShare * Mathf.Min(lens[j - 1], lens[j]), cap);
                foreach (float mul in ArcStepDown)
                {
                    float r = radiusCells * mul;
                    float t = TangentLen(theta, r);
                    if (t <= room)
                    {
                        cornerT[j] = t; cornerR[j] = r; cornerTheta[j] = theta; cornerSign[j] = sign;
                        break;
                    }
                }
            }

            var pts = new List<Vector2>();
            void Push(IEnumerable<Vector2> seq)
            {
                foreach (var p in seq)
                    if (pts.Count == 0 || Vector2.Distance(pts[pts.Count - 1], p) > 1e-6f) pts.Add(p);
            }

            for (int i = 0; i < m; i++)
            {
                Vector2 a = verts[i], b = verts[i + 1];
                float L = lens[i];
                float tIn = cornerT[i] ?? 0f;
                float tOut = cornerT[i + 1] ?? 0f;
                float t0 = tIn / L, t1 = 1f - tOut / L;
                if (t1 <= t0) { t0 = 0.5f; t1 = 0.5f; }
                Push(new[] { Vector2.Lerp(a, b, t0), Vector2.Lerp(a, b, t1) });

                int j = i + 1;
                if (!cornerT[j].HasValue || j >= m) continue;
                float t = cornerT[j].Value, r = cornerR[j], theta = cornerTheta[j], sign = cornerSign[j];
                Vector2 na = verts[j], nb = verts[j + 1];
                float nl = lens[j];
                Vector2 outPt = Vector2.Lerp(na, nb, t / nl);
                Push(Arc(pts[pts.Count - 1], verts[j], outPt, r, sign, theta));
            }
            return pts;
        }

        static List<Vector2> SplitLong(List<Vector2> pts, float maxLen)
        {
            var outPts = new List<Vector2> { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float L = Vector2.Distance(a, b);
                int n = Mathf.Max(1, Mathf.CeilToInt(L / maxLen));
                for (int k = 1; k <= n; k++) outPts.Add(Vector2.Lerp(a, b, k / (float)n));
            }
            return outPts;
        }

        float RungRadius(int rung, CircuitCurveMode mode)
        {
            if (mode == CircuitCurveMode.None) return 0f;
            int start = mode == CircuitCurveMode.Tight ? 0 : ArcTightMax;
            int count = mode == CircuitCurveMode.Tight ? ArcTightMax : (ArcLadder.Length - ArcTightMax);
            int idx = Mathf.Clamp(rung, 0, count - 1);
            return ArcLadder[start + idx];
        }

        // one polyline segment as a UV endpoint pair, START normalised into [0,1) — without this, a long
        // trail's UNWRAPPED lattice coordinates accumulate past the 9-copy stamp's reach and silently vanish.
        static (float ax, float ay, float bx, float by) SegUV(Vector2 a, Vector2 b, int grid)
        {
            float ax = (a.x + 0.5f) / grid, ay = (a.y + 0.5f) / grid;
            float bx = (b.x + 0.5f) / grid, by = (b.y + 0.5f) / grid;
            float ox = Mod1(ax) - ax, oy = Mod1(ay) - ay;
            return (ax + ox, ay + oy, bx + ox, by + oy);
        }

        static (float, float) UV(Vector2Int p, int grid) => ((p.x + 0.5f) / grid, (p.y + 0.5f) / grid);

        static float Mod1(float v) => v - Mathf.Floor(v);
        static int Mod(int v, int n) => ((v % n) + n) % n;

        // -------------------------------------------------------------------------------- the walk
        class WalkResult
        {
            public List<Vector2Int> cells;
            public List<int> dirs;
        }

        static int[] AxisFamily(int axis)
        {
            int a = Mod(axis, 8);
            var res = new int[4];
            for (int k = 0; k < 4; k++) res[k] = (a + 2 * k) % 8;
            return res;
        }

        static int SpanCells(List<int> dirs)
        {
            int x = 0, y = 0, loX = 0, hiX = 0, loY = 0, hiY = 0;
            foreach (int cd in dirs)
            {
                var d = Dirs8[cd];
                x += d.x; y += d.y;
                loX = Mathf.Min(loX, x); hiX = Mathf.Max(hiX, x);
                loY = Mathf.Min(loY, y); hiY = Mathf.Max(hiY, y);
            }
            return Mathf.Max(hiX - loX, hiY - loY);
        }

        // True if `cell` may not be entered: hard self-avoidance first (any occupied cell), then the
        // CLEARANCE test that gives traces real space from each other. `exempt` is the walker's own recent
        // tail — without it the cell the walker is standing on blocks every candidate and it can never take
        // a first step (the real bug the source documents fixing). Clearance against OTHER trails is never
        // relaxed.
        static bool Blocked(Vector2Int cell, int grid, HashSet<(int, int)> committed, HashSet<(int, int)> own,
            HashSet<(int, int)> exempt, int spacing)
        {
            var c = (cell.x, cell.y);
            if (committed.Contains(c) || own.Contains(c)) return true;
            if (spacing <= 0) return false;
            for (int dy = -spacing; dy <= spacing; dy++)
            {
                for (int dx = -spacing; dx <= spacing; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var n = (Mod(cell.x + dx, grid), Mod(cell.y + dy, grid));
                    if (committed.Contains(n)) return true;
                    if (own.Contains(n) && !exempt.Contains(n)) return true;
                }
            }
            return false;
        }

        static WalkResult WalkOne(ref HashRng rng, int grid, HashSet<(int, int)> committed, int minLen,
            int spacing, int minRun, int runMax, float turn90, int maxTurnsV, int targetLen, int[] dirs0)
        {
            var start = new Vector2Int(rng.NextInt(grid), rng.NextInt(grid));
            if (Blocked(start, grid, committed, new HashSet<(int, int)>(), new HashSet<(int, int)>(), spacing))
                return null;

            int d = dirs0[rng.NextInt(dirs0.Length)];
            var cells = new List<Vector2Int> { start };
            var own = new HashSet<(int, int)> { (start.x, start.y) };
            var dirs = new List<int>();
            int recentSpan = 2 * spacing + 2;

            bool Free(Vector2Int cell)
            {
                var exempt = new HashSet<(int, int)>();
                int from = Mathf.Max(0, cells.Count - recentSpan);
                for (int k = from; k < cells.Count; k++) exempt.Add((cells[k].x, cells[k].y));
                return !Blocked(cell, grid, committed, own, exempt, spacing);
            }

            int turnsUsed = 0;
            while (true)
            {
                int run = rng.Range(minRun, runMax);
                int took = 0;
                for (int s = 0; s < run; s++)
                {
                    var delta = Dirs8[d];
                    var nxt = new Vector2Int(Mod(cells[cells.Count - 1].x + delta.x, grid),
                        Mod(cells[cells.Count - 1].y + delta.y, grid));
                    if (!Free(nxt)) break;
                    cells.Add(nxt);
                    own.Add((nxt.x, nxt.y));
                    dirs.Add(d);
                    took++;
                }
                if (took < minRun) return null;   // a short run is never committed — attempt discarded

                turnsUsed++;
                if (turnsUsed > maxTurnsV || cells.Count >= targetLen) break;
                int step = rng.Chance(turn90) ? 2 : 1;
                int sign = rng.Chance(0.5f) ? 1 : -1;
                d = Mod(d + step * sign, 8);
            }

            if (cells.Count < minLen || SpanCells(dirs) < MinSpanFrac * grid) return null;
            return new WalkResult { cells = cells, dirs = dirs };
        }

        static List<WalkResult> WalkAll(ref HashRng rng, int grid, int count, int minLen, int spacing, int minRun,
            int runMax, float turn90, int maxTurnsV, int maxLen, int[] dirs0, int attempts)
        {
            var committed = new HashSet<(int, int)>();
            var trails = new List<WalkResult>();
            for (int w = 0; w < count; w++)
            {
                for (int t = 0; t < attempts; t++)
                {
                    int target = rng.Range(minLen, maxLen);
                    var got = WalkOne(ref rng, grid, committed, minLen, spacing, minRun, runMax, turn90,
                        maxTurnsV, target, dirs0);
                    if (got != null)
                    {
                        foreach (var c in got.cells) committed.Add((c.x, c.y));
                        trails.Add(got);
                        break;
                    }
                }
            }
            return trails;
        }

        // ------------------------------------------------------------------------- deterministic RNG
        // A counter-driven hash stream, built from the exact hash-by-coordinate formula
        // TapestryPanelsGenerator's HashCell uses (cx=the call counter, cy=0, seed, a fixed salt) — never
        // System.Random. Serves the same role the source's `random.Random(seed)` stream does (a single
        // deterministic sequence of draws that authors the whole tile once), while every individual draw is
        // itself a hash rather than mutated multiplicative state.
        struct HashRng
        {
            readonly int seed;
            uint counter;

            public HashRng(int seed)
            {
                this.seed = seed;
                counter = 0u;
            }

            public float NextFloat()
            {
                counter++;
                return HashCell((int)counter, 0, seed, 911);
            }

            public int NextInt(int n) => n <= 0 ? 0 : Mathf.Min(n - 1, Mathf.FloorToInt(NextFloat() * n));

            public int Range(int minIncl, int maxIncl) =>
                maxIncl <= minIncl ? minIncl : minIncl + NextInt(maxIncl - minIncl + 1);

            public bool Chance(float p) => NextFloat() < p;

            static float HashCell(int cx, int cy, int seed, int salt)
            {
                unchecked
                {
                    int h = cx * 374761393 + cy * 668265263 + seed * 1103515245 + salt * 2032854233;
                    h = (h ^ (h >> 13)) * 1274126177;
                    h ^= h >> 16;
                    return (h & 0x7fffffff) / (float)int.MaxValue;
                }
            }
        }
    }
}
