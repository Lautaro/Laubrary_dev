// FireSim — the grid simulation behind LayerShape.Fire. Migrated from Laubrary.Pyre 2026-08-23 as part of
// Pyre's retirement (Pyre/PyreFireSim already depended on this directly). No [MovedFrom] needed: FireArmMode
// (enum) and FireParams (struct) are plain value-typed serialized fields, not [SerializeReference] — Unity
// stores their underlying int/float values by field name, not by type identity, so a namespace move doesn't
// affect existing serialized data at all.
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

namespace Laubrary.SpriteFx
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
        // Shape — these are what make it read as a FLAME rather than an expanding blob.
        public float stretch;   // elongate along the arm axis
        public float pinch;     // taper the sides into a tongue (also what keeps arms distinct)
        public float breakup;   // wispy, broken edges instead of a smooth silhouette
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

        // The box that currently holds any heat/fuel. The step only processes this (expanded by how far one
        // step can advect), because everything outside is cold and — being confined — stays cold. A small
        // candle then touches ~900 pixels instead of all 4096, which is most of the paused-drag cost. Reset()
        // widens it to the whole canvas so the first step after a reset is unconditionally correct.
        int boxX0, boxY0, boxX1, boxY1;

        public void Allocate(int w, int h)
        {
            if (W == w && H == h && heat != null) return;
            W = w; H = h;
            heat = new float[w * h]; fuel = new float[w * h];
            heatB = new float[w * h]; fuelB = new float[w * h];
            boxX0 = 0; boxY0 = 0; boxX1 = w - 1; boxY1 = h - 1;
        }

        public void Reset()
        {
            System.Array.Clear(heat, 0, heat.Length);
            System.Array.Clear(fuel, 0, fuel.Length);
            LastFrame = -1;
            boxX0 = 0; boxY0 = 0; boxX1 = W - 1; boxY1 = H - 1;   // first step after a reset scans everything
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
            int injX0 = W, injY0 = H, injX1 = -1, injY1 = -1;   // where injection touched this step
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
                        if (x < injX0) injX0 = x; if (x > injX1) injX1 = x;
                        if (y < injY0) injY0 = y; if (y > injY1) injY1 = y;
                    }
            }

            // The box worth scanning: wherever heat already is (boxX0..) OR was just injected, grown by how
            // far a single step could carry it. Everything outside is cold and confined, so it stays cold.
            int margin = Mathf.CeilToInt(p.flow + p.buoyancy + p.stretch + p.curl * 3f + p.flicker * 2f) + 2;
            int ax0 = Mathf.Max(0, Mathf.Min(boxX0, injX1 < 0 ? boxX0 : injX0) - margin);
            int ay0 = Mathf.Max(0, Mathf.Min(boxY0, injY1 < 0 ? boxY0 : injY0) - margin);
            int ax1 = Mathf.Min(W - 1, Mathf.Max(boxX1, injX1) + margin);
            int ay1 = Mathf.Min(H - 1, Mathf.Max(boxY1, injY1) + margin);

            // The target buffers must be zero everywhere the loop won't write, or the swap would surface stale
            // state from two steps ago outside the box. A memset is trivial next to the per-pixel noise/trig.
            System.Array.Clear(heatB, 0, heatB.Length);
            System.Array.Clear(fuelB, 0, fuelB.Length);
            int nbx0 = W, nby0 = H, nbx1 = -1, nby1 = -1;

            // ── advect + cool ─────────────────────────────────────────────────────────────────
            float curlScale = Mathf.Max(2f, p.curlScale);
            int tKey = Mathf.FloorToInt(t * 200f);   // slow enough that flicker reads as motion, not static noise
            // One arm's axis never changes across the canvas, so hoist it out of the pixel loop — the common
            // (candle) case then does no per-pixel trig for the axis at all.
            bool multi = arms > 1;
            float dirNx = Mathf.Cos(dirRad), dirNy = Mathf.Sin(dirRad);
            for (int y = ay0; y <= ay1; y++)
                for (int x = ax0; x <= ax1; x++)
                {
                    int i = y * W + x;
                    float ox = x - cx, oy = y - cy;
                    float dist = Mathf.Sqrt(ox * ox + oy * oy);

                    // Which ARM this pixel belongs to. Its fire travels along THAT arm's axis, not radially
                    // from the centre — one arm would otherwise ignore Direction and spread evenly, which is
                    // a growing blob, not a flame.
                    float nx = dirNx, ny = dirNy;
                    float pixAng = 0f;
                    if (multi)
                    {
                        pixAng = Mathf.Atan2(oy, ox);
                        float armAng = dirRad + Mathf.Round((pixAng - dirRad) / sector) * sector;
                        nx = Mathf.Cos(armAng); ny = Mathf.Sin(armAng);
                    }

                    // Arm-local coordinates: `along` = distance from the emitter toward the tip; `lat` = signed
                    // sideways distance from the arm's own axis. Everything that shapes the flame is expressed
                    // in these, which is what makes it anisotropic (a tongue) instead of isotropic (a blob).
                    float along = ox * nx + oy * ny;
                    float lat = -ox * ny + oy * nx;
                    float latAbs = Mathf.Abs(lat);
                    // How far off the arm axis, as sin(angle) = lat/dist — a cheap stand-in for the angle that
                    // costs no atan2. Both the tongue taper and (multi-arm) the gap between arms come from
                    // cooling by this; directly-behind pixels are handled by the along<0 term below.
                    float angOff = dist > 0.001f ? latAbs / dist : 0f;

                    float h = heat[i];
                    // Rise: outward along the axis, accelerating with heat (buoyancy) and elongated by stretch.
                    float rise = p.flow + (p.buoyancy + p.stretch) * h;
                    float vx = nx * rise, vy = ny * rise;

                    // Turbulence sampled in the pixel's own ARM SECTOR, folded to sector 0 (and mirrored once
                    // more for Mirror mode) so the arms genuinely share/reflect one field instead of each
                    // moving through different noise. The curl acts SIDEWAYS (perpendicular to the axis), which
                    // is what makes a tongue wave and lick rather than just travel straight.
                    float qx = ox, qy = oy;
                    if (multi)
                    {
                        float folded = Mathf.Repeat(pixAng - dirRad, sector);
                        if (p.armMode == FireArmMode.Mirror && folded > sector * 0.5f) folded = sector - folded;
                        qx = Mathf.Cos(folded + dirRad) * dist; qy = Mathf.Sin(folded + dirRad) * dist;
                    }
                    // warp 0 on the noise: fire doesn't need the domain-distortion octave, and dropping it
                    // halves the lattice sampling — the dominant per-pixel cost.
                    float swirl = (PyreNoise.Sample((qx + cx) / curlScale, (qy + cy - t * 60f) / curlScale, seed + 31, 0f) - 0.5f)
                                  * p.curl * 3f * (0.3f + h);   // stronger where there's fire, so cold air is calm
                    vx += -ny * swirl;   // perpendicular to the arm axis
                    vy += nx * swirl;

                    if (p.flicker > 0.001f)
                    {
                        int fh = (Mathf.RoundToInt(qx * 3f) * 73856093) ^ (Mathf.RoundToInt(qy * 3f) * 19349663);
                        vx += -ny * (Hash01(seed, fh, tKey) - 0.5f) * p.flicker * 2f;
                        vy += nx * (Hash01(seed, fh, tKey) - 0.5f) * p.flicker * 2f;
                    }

                    // Semi-Lagrangian: read from where this pixel's content came FROM.
                    float sh = Sample(heat, x - vx * dt, y - vy * dt);
                    float sf = Sample(fuel, x - vx * dt, y - vy * dt);

                    // Fuel burns into heat, then both decay.
                    float burned = sf * p.burn * dt;
                    sh += burned;
                    sf -= burned;

                    // ── cooling: this is the shape ──────────────────────────────────────────────
                    float cool = p.dissipation;
                    // Pinch — cool by ANGLE off the arm axis, so each arm narrows to a pointed tongue and,
                    // with several arms, the cold gaps between them open up. This one term does most of the
                    // work of making it read as fire AND of keeping arms distinct.
                    cool += p.pinch * angOff * 2.2f;
                    // Anything behind the emitter (along < 0) is cooled hard — fire only goes forward.
                    if (along < 0f) cool += (-along) * 0.06f + p.pinch;
                    // Breakup — a finer, faster noise that eats the edges into wisps rather than a smooth
                    // silhouette. Scaled by (1-h) so it bites the cool outer flame, not the hot core.
                    if (p.breakup > 0.001f)
                    {
                        float nb = PyreNoise.Sample((qx + cx) / 5f + tKey * 0.13f, (qy + cy) / 5f - tKey * 0.19f, seed + 91, 0f);
                        cool += p.breakup * Mathf.Max(0f, nb - 0.35f) * 2.5f * (1.2f - sh);
                    }
                    sh *= Mathf.Max(0f, 1f - cool * dt);

                    // ── confinement ───────────────────────────────────────────────────────────
                    // Past the reach radius heat is killed and ramps to zero at the limit, so the flame can
                    // never reach the canvas edge however hard flow/buoyancy are driven. Inside, untouched.
                    if (dist > reachPx)
                    {
                        float over = Mathf.Clamp01((dist - reachPx) / Mathf.Max(1f, half - reachPx));
                        float kill = 1f - over;
                        sh *= kill * (1f - p.edgeCooling * dt * (1f + over * 4f));
                        sf *= kill;
                    }

                    float nh = Mathf.Clamp01(sh), nf = Mathf.Clamp01(sf);
                    heatB[i] = nh;
                    fuelB[i] = nf;
                    if (nh > 0.002f || nf > 0.002f)
                    {
                        if (x < nbx0) nbx0 = x; if (x > nbx1) nbx1 = x;
                        if (y < nby0) nby0 = y; if (y > nby1) nby1 = y;
                    }
                }

            var th = heat; heat = heatB; heatB = th;
            var tf = fuel; fuel = fuelB; fuelB = tf;
            // Carry the live box forward. If nothing survived, collapse to the emitter so re-ignition still
            // gets scanned next step (the active box unions the emitter region regardless).
            if (nbx1 < nbx0) { boxX0 = boxY0 = 0; boxX1 = boxY1 = 0; }
            else { boxX0 = nbx0; boxY0 = nby0; boxX1 = nbx1; boxY1 = nby1; }
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
