// FireSim — the grid simulation behind LayerShape.Fire.
//
// WHY THIS IS NOT CLOSED-FORM, AND WHY THAT IS ALLOWED HERE
// Every other Pyre shape computes a frame from (seed, index, frame) alone, which is what makes preview,
// bake and runtime identical and lets any frame be scrubbed to directly. A flame is genuinely stateful:
// heat is carried by a velocity field, so frame N depends on frame N-1. Pyre already has the answer for
// that — SimulationModifier reaches a requested frame by REPLAYING from a reset state — and Fire uses the
// same discipline. So Fire is still perfectly scrubbable and deterministic; it is just reached by replay
// instead of by evaluation.
//
// Replay is done from frame 0 EVERY time, deliberately. SimulationModifier used to keep a checkpoint and
// re-step the last frame when the same frame was re-requested, and that was removed as wrong: dragging a
// dial changes the parameters of every earlier frame too, so a checkpoint restores state that was built
// under the OLD values. Replaying is O(frames²) across a scrub, which for a 64×64 pixel-art canvas is
// cheap, and it is the only version that cannot silently show a stale frame.
//
// ARMS
// The arms are emitters inside ONE shared grid rather than N separate simulations. That is both cheaper
// and better-looking: neighbouring arms genuinely bleed into each other. Mirror gives every arm identical
// emission (kaleidoscope symmetry); Vary offsets each arm's flicker/pulse/curl phase by its own seed, so
// the arms are truly independent — which the Kaleidoscope post modifier cannot do, since it only ever sees
// one finished image.
using UnityEngine;

namespace Laubrary.Pyre
{
    /// How a Fire layer's arms relate.
    public enum FireArmMode
    {
        Mirror,   // every arm emits identically — symmetric
        Vary,     // each arm gets its own seed, so the flames differ while sharing the dials
    }

    /// One frame's worth of evaluated Fire dials. Every rate is authored as an envelope over the layer's
    /// life, so what the user shapes is the PROGRESS of the burn, not a speed — a candle that swells into a
    /// blaze and dies back is one curve, not a timeline of tweaks.
    public struct FireParams
    {
        public float emitterWidth, emitterInset, heat, fuel, pulse;
        public float flow, buoyancy, curl, curlScale, flicker;
        public float dissipation, burn, reach, edgeCooling, directionDeg;
        public int arms, steps;
        public FireArmMode armMode;
    }

    public class FireSim
    {
        /// Pixels of guaranteed clearance between the flame's outermost reach and the canvas edge.
        public const float EdgeClearance = 2f;

        public int W, H;
        float[] heat, fuel, heatB, fuelB;
        public int LastFrame = -1;

        public void Allocate(int w, int h)
        {
            if (W == w && H == h && heat != null) return;
            W = w; H = h;
            heat = new float[w * h]; fuel = new float[w * h];
            heatB = new float[w * h]; fuelB = new float[w * h];
        }

        public void Reset()
        {
            System.Array.Clear(heat, 0, heat.Length);
            System.Array.Clear(fuel, 0, fuel.Length);
            LastFrame = -1;
        }

        static float Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        float Sample(float[] f, float x, float y)
        {
            // Bilinear, clamped. Clamping (rather than wrapping) matters: a flame must not smear back in
            // from the opposite edge of the canvas.
            x = Mathf.Clamp(x, 0f, W - 1.001f);
            y = Mathf.Clamp(y, 0f, H - 1.001f);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Mathf.Min(x0 + 1, W - 1), y1 = Mathf.Min(y0 + 1, H - 1);
            float fx = x - x0, fy = y - y0;
            float a = Mathf.Lerp(f[y0 * W + x0], f[y0 * W + x1], fx);
            float b = Mathf.Lerp(f[y1 * W + x0], f[y1 * W + x1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// One simulation step. `t` is the layer's life progress, used only for the noise phase — the dial
        /// values themselves are already evaluated into `p` by the caller.
        public void Step(in FireParams p, int seed, float t, float dt)
        {
            float cx = W * 0.5f, cy = H * 0.5f;
            float half = Mathf.Min(W, H) * 0.5f;
            // Reach 1.0 must still leave clearance, or the promise "it can never touch the frame edge" is
            // false exactly where it matters most. Measured: without this, reach 1.0 lit 48 edge pixels,
            // because the edge midpoints sit at distance == half and so were never counted as "past reach".
            float reachPx = Mathf.Clamp(p.reach * (half - EdgeClearance), 1f, half - EdgeClearance);
            int arms = Mathf.Max(1, p.arms);
            float sector = Mathf.PI * 2f / arms;
            float dirRad = p.directionDeg * Mathf.Deg2Rad;

            // ── inject ────────────────────────────────────────────────────────────────────────
            for (int a = 0; a < arms; a++)
            {
                // Vary gives each arm its own seed, so its pulse and flicker run on a different phase.
                int armSeed = p.armMode == FireArmMode.Vary ? seed + a * 7919 : seed;
                float ang = (p.directionDeg + 360f / arms * a) * Mathf.Deg2Rad;
                float dirX = Mathf.Cos(ang), dirY = Mathf.Sin(ang);

                // Pulsing: a seeded wobble on the emitter's output, so the base of the flame breathes.
                float ph = Hash01(armSeed, 0, 11) * 10f;
                float pulse = 1f + (Mathf.Sin((t * 12f + ph) * Mathf.PI * 2f) * 0.5f
                                    + (Hash01(armSeed, Mathf.FloorToInt(t * 60f), 13) - 0.5f)) * p.pulse;

                float ex = cx + dirX * p.emitterInset, ey = cy + dirY * p.emitterInset;
                int r = Mathf.Max(1, Mathf.CeilToInt(p.emitterWidth * 0.5f));
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = Mathf.RoundToInt(ex) + dx, y = Mathf.RoundToInt(ey) + dy;
                        if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(0.001f, p.emitterWidth * 0.5f);
                        if (d > 1f) continue;
                        float falloff = 1f - d * d;
                        int i = y * W + x;
                        heat[i] = Mathf.Max(heat[i], Mathf.Clamp01(p.heat * pulse * falloff));
                        fuel[i] = Mathf.Max(fuel[i], Mathf.Clamp01(p.fuel * pulse * falloff));
                    }
            }

            // ── advect + cool ─────────────────────────────────────────────────────────────────
            float curlScale = Mathf.Max(2f, p.curlScale);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float ox = x - cx, oy = y - cy;
                    float dist = Mathf.Sqrt(ox * ox + oy * oy);

                    // Which ARM this pixel belongs to decides which way its fire travels. Not the radial
                    // direction from the centre: with one arm that would ignore Direction entirely and
                    // spread heat evenly in all directions, which is a growing blob rather than a flame
                    // (exactly what the first version produced). One arm = one direction for the whole
                    // canvas; N arms = each sector carries its fire along its own axis.
                    float armAng = dirRad;
                    if (arms > 1)
                    {
                        float rel = Mathf.Atan2(oy, ox) - dirRad;
                        armAng = dirRad + Mathf.Round(rel / sector) * sector;
                    }
                    float nx = Mathf.Cos(armAng), ny = Mathf.Sin(armAng);

                    float h = heat[i];
                    float vx = nx * (p.flow + p.buoyancy * h);
                    float vy = ny * (p.flow + p.buoyancy * h);

                    // Turbulence is sampled in the pixel's own ARM SECTOR, folded back to sector 0. Without
                    // this the noise field is shared across the whole canvas, so "Mirror" arms move through
                    // DIFFERENT turbulence and the symmetry it promises never actually appears (measured: a
                    // 4-arm Mirror was as asymmetric as Vary). Folding makes the field N-fold symmetric, and
                    // Mirror folds once more within the sector so neighbouring arms meet as reflections.
                    float qx = ox, qy = oy;
                    if (arms > 1 && p.armMode == FireArmMode.Mirror)
                    {
                        float a0 = Mathf.Atan2(oy, ox) - dirRad;
                        float folded = Mathf.Repeat(a0, sector);
                        if (folded > sector * 0.5f) folded = sector - folded;   // mirror within the sector
                        float fa = folded + dirRad;
                        qx = Mathf.Cos(fa) * dist; qy = Mathf.Sin(fa) * dist;
                    }

                    // Curl noise: two offset noise samples give a divergence-free-ish swirl, which is what
                    // makes the tongues curl instead of just stretching.
                    float n1 = PyreNoise.Sample((qx + cx + t * 40f) / curlScale, (qy + cy) / curlScale, seed + 31, 0.5f) - 0.5f;
                    float n2 = PyreNoise.Sample((qx + cx) / curlScale, (qy + cy - t * 40f) / curlScale, seed + 57, 0.5f) - 0.5f;
                    vx += n2 * p.curl * 2f;
                    vy -= n1 * p.curl * 2f;

                    // Flicker: a small jitter so edges break up rather than staying glassy. Keyed on the
                    // FOLDED position too, for the same symmetry reason as the curl above.
                    if (p.flicker > 0.001f)
                    {
                        int fh = Mathf.RoundToInt(qx * 4f) * 73856093 ^ Mathf.RoundToInt(qy * 4f) * 19349663;
                        vx += (Hash01(seed, fh, Mathf.FloorToInt(t * 97f)) - 0.5f) * p.flicker * 2f;
                        vy += (Hash01(seed, fh, Mathf.FloorToInt(t * 97f) + 5) - 0.5f) * p.flicker * 2f;
                    }

                    // Semi-Lagrangian: read from where this pixel's content came FROM.
                    float sh = Sample(heat, x - vx * dt, y - vy * dt);
                    float sf = Sample(fuel, x - vx * dt, y - vy * dt);

                    // Fuel burns into heat, then both decay.
                    float burned = sf * p.burn * dt;
                    sh += burned;
                    sf -= burned;
                    sh *= 1f - p.dissipation * dt;

                    // ── confinement ───────────────────────────────────────────────────────────
                    // The reason "crank it up" is safe. Past the reach radius, heat is cooled hard and
                    // ramps to zero at the limit, so the flame CANNOT reach the canvas edge no matter how
                    // much flow or buoyancy is dialled in — the thing that made a hot setting unusable as a
                    // game asset. Inside the radius nothing is touched at all.
                    if (dist > reachPx)
                    {
                        float over = Mathf.Clamp01((dist - reachPx) / Mathf.Max(1f, half - reachPx));
                        float kill = 1f - over;
                        sh *= kill * (1f - p.edgeCooling * dt * (1f + over * 4f));
                        sf *= kill;
                    }

                    heatB[i] = Mathf.Clamp01(sh);
                    fuelB[i] = Mathf.Clamp01(sf);
                }

            var th = heat; heat = heatB; heatB = th;
            var tf = fuel; fuel = fuelB; fuelB = tf;
        }

        /// Composite the current state into a layer buffer through the layer's own gradient — one ramp whose
        /// low end is smoke and whose high end is fire, exactly like Height balls, so the two read as part of
        /// the same tool rather than two unrelated generators.
        public void Render(Color32[] buf, Gradient ramp, float layerAlpha, float threshold, float contrast)
        {
            for (int i = 0; i < heat.Length; i++)
            {
                float v = Mathf.Clamp01(heat[i] + fuel[i] * 0.35f);
                if (v <= threshold) continue;
                float t = Mathf.Clamp01((v - threshold) / Mathf.Max(0.001f, 1f - threshold));
                t = Mathf.Clamp01(Mathf.Pow(t, Mathf.Max(0.05f, contrast)));
                var c = ramp != null ? ramp.Evaluate(t) : Color.white;
                float a = Mathf.Clamp01(c.a * layerAlpha * Mathf.Clamp01(t * 2.2f));
                if (a <= 0.002f) continue;
                buf[i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
            }
        }
    }
}
