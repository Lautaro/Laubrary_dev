// PlusFireSim — a PyrePlus-LOCAL re-port of Pyre's FireSim (Runtime/Pyre/FireSim.cs), generalized so the caller
// supplies the EMITTER SET instead of the fixed "N arms around the canvas centre" the original bakes into Step.
//
// WHY A RE-PORT (and not a reuse). Slice 6a's Fire form reuses Pyre's public FireSim verbatim, because its
// built-in arm emitters are exactly what that path wants. Slice 8's swarm-driven Fire needs ONE heat/fuel
// injection per alive swarm particle at its own canvas position — but FireSim bakes its emitter geometry into
// Step's inner arm loop, and its heat/fuel GRIDS are private, so external emitters cannot be injected into it.
// The standing rule is that PyrePlus must NOT modify Pyre, so the faithful answer is a PyrePlus-local copy that
// keeps the fluid physics BYTE-FAITHFUL to FireSim and replaces ONLY the injection.
//
// WHAT MIRRORS FireSim EXACTLY (copied verbatim so a single centred emitter reads ≈ Pyre's Fire look):
//   • state — four float[W*H] grids (heat/fuel/heatB/fuelB), LastFrame, and the active-box AABB (boxX0..boxY1)
//   • Allocate / Reset (Reset widens the box to the whole canvas so the first step is unconditionally correct)
//   • Hash01 (the integer Wang hash) and Sample (clamped bilinear)
//   • the advect+cool step: per-pixel arm-axis selection, rise = flow + (buoyancy+stretch)·h, sideways curl
//     swirl, flicker, semi-Lagrangian back-sample, fuel→heat burn, cooling (pinch/behind/breakup), the reach
//     CONFINEMENT ramp, the double-buffer swap, and the live-box carry-forward — all IDENTICAL to FireSim.
//   • Render (composite heat+fuel·0.35 through a Gradient at threshold/contrast)
//
// WHAT CHANGES (the whole point of the re-port):
//   • Step's signature — Step(in FireParams p, IReadOnlyList<HeatEmitter> emitters, int seed, float t, float dt).
//     `p` still carries the PHYSICS + shape dials (flow/buoyancy/curl/dissipation/reach/arms/directionDeg/…) so
//     the advect field is byte-faithful to FireSim; `emitters` replaces the fixed arm INJECT loop. p.heat/p.fuel/
//     p.emitterWidth/p.emitterInset are unused here (each emitter carries its own heat/fuel/radius) — only the
//     physics dials of p and p.pulse (the pulse AMOUNT) are read.
//   • the inject loop iterates the caller's HeatEmitter list — each injects a soft heat/fuel disc at its own
//     (x,y) with its own radius, breathing on its own phaseSeed — instead of N arms around centre.
//
// NOISE SUBSTITUTION (documented, deliberate). FireSim's advect step samples Pyre's `PyreNoise.Sample(x, y, seed,
// warp=0)` twice (the curl swirl and the breakup). PyreNoise is `internal` to the Pyre assembly and unreachable
// from PyrePlus (no InternalsVisibleTo). Its warp==0 reduction is exactly two octaves of value noise blended
// 0.65/0.35 with the second at 2.13× scale — which is precisely the SHAPE of PyrePlusField.Noise01 (the PyrePlus-
// local value noise the Coalesce field-passes already use). So every FireSim `PyreNoise.Sample(a, b, s, 0f)`
// becomes `PyrePlusField.Noise01(a, b, s)` (the always-zero warp arg dropped). The two differ ONLY in the
// underlying integer hash (PyreNoise → BlastRenderer.Hash01; Noise01 → its own FNV-1a), so the turbulence has the
// same coherent character but is not bit-identical to Pyre — hence "≈ Pyre's look", never a byte match to Pyre.
using System.Collections.Generic;
using Laubrary.Pyre;   // FireParams / FireArmMode (public) — the physics/shape dials this Step still consumes
using UnityEngine;

namespace Laubrary.PyrePlus
{
    /// One externally-supplied heat/fuel injection into the shared fire field. The swarm-driven Fire path builds
    /// one of these per alive swarm particle each frame: position = the particle's canvas position; radius/heat/
    /// fuel = the layer's Fire envelopes evaluated at that particle's OWN life; phaseSeed = the particle index
    /// folded with the sim seed (so each source's pulse breathes on its own phase, like FireSim's Vary arms).
    public struct HeatEmitter
    {
        public float x, y;      // absolute canvas-pixel position of the emitter's centre
        public float radius;    // emitter influence radius in px (= Pyre's emitterWidth / 2 — the base of the flame)
        public float heat;      // peak heat injected at the centre, 0..1 (falls off to 0 at radius)
        public float fuel;      // peak unburnt fuel injected at the centre, 0..1
        public int phaseSeed;   // decorrelation seed for this emitter's pulse phase (per-particle)
    }

    /// A PyrePlus-local mirror of Pyre's FireSim with a caller-supplied emitter set (see the file header).
    public class PlusFireSim
    {
        /// Pixels of guaranteed clearance between the flame's outermost reach and the canvas edge (matches FireSim).
        public const float EdgeClearance = 2f;

        public int W, H;
        float[] heat, fuel, heatB, fuelB;
        public int LastFrame = -1;

        // The box that currently holds any heat/fuel (mirrors FireSim exactly — see FireSim.cs for the rationale).
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

        // Verbatim from FireSim.Hash01 (the integer Wang hash).
        static float Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        // Verbatim from FireSim.Sample (clamped bilinear).
        float Sample(float[] f, float x, float y)
        {
            x = Mathf.Clamp(x, 0f, W - 1.001f);
            y = Mathf.Clamp(y, 0f, H - 1.001f);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Mathf.Min(x0 + 1, W - 1), y1 = Mathf.Min(y0 + 1, H - 1);
            float fx = x - x0, fy = y - y0;
            float a = Mathf.Lerp(f[y0 * W + x0], f[y0 * W + x1], fx);
            float b = Mathf.Lerp(f[y1 * W + x0], f[y1 * W + x1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// One simulation step. `p` carries the physics + shape dials (byte-faithful to FireSim's FireParams);
        /// `emitters` REPLACES FireSim's fixed arm inject loop with one soft heat/fuel disc per caller emitter.
        /// `t` is the layer's life progress (the noise phase); `dt` is one (sub)step of time. Everything after the
        /// inject block is copied verbatim from FireSim.Step so the fluid field stays byte-faithful.
        public void Step(in FireParams p, IReadOnlyList<HeatEmitter> emitters, int seed, float t, float dt)
        {
            float cx = W * 0.5f, cy = H * 0.5f;
            float half = Mathf.Min(W, H) * 0.5f;
            float reachPx = Mathf.Clamp(p.reach * (half - EdgeClearance), 1f, half - EdgeClearance);
            int arms = Mathf.Max(1, p.arms);
            float sector = Mathf.PI * 2f / arms;
            float dirRad = p.directionDeg * Mathf.Deg2Rad;

            // ── inject (GENERALIZED — one soft disc per caller emitter, replacing FireSim's N-arm loop) ─────────
            int injX0 = W, injY0 = H, injX1 = -1, injY1 = -1;   // where injection touched this step
            int emitterCount = emitters != null ? emitters.Count : 0;
            for (int e = 0; e < emitterCount; e++)
            {
                var em = emitters[e];
                float rad = Mathf.Max(0.001f, em.radius);
                // Pulsing: a seeded wobble on the emitter's output, so the base of the flame breathes. Identical to
                // FireSim, but keyed on this emitter's own phaseSeed instead of an arm seed, and the pulse AMOUNT
                // p.pulse still comes from the shared FireParams.
                float ph = Hash01(em.phaseSeed, 0, 11) * 10f;
                float pulse = 1f + (Mathf.Sin((t * 12f + ph) * Mathf.PI * 2f) * 0.5f
                                    + (Hash01(em.phaseSeed, Mathf.FloorToInt(t * 60f), 13) - 0.5f)) * p.pulse;

                int r = Mathf.Max(1, Mathf.CeilToInt(rad));
                int exI = Mathf.RoundToInt(em.x), eyI = Mathf.RoundToInt(em.y);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = exI + dx, y = eyI + dy;
                        if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / rad;
                        if (d > 1f) continue;
                        float falloff = 1f - d * d;
                        int i = y * W + x;
                        heat[i] = Mathf.Max(heat[i], Mathf.Clamp01(em.heat * pulse * falloff));
                        fuel[i] = Mathf.Max(fuel[i], Mathf.Clamp01(em.fuel * pulse * falloff));
                        if (x < injX0) injX0 = x; if (x > injX1) injX1 = x;
                        if (y < injY0) injY0 = y; if (y > injY1) injY1 = y;
                    }
            }

            // ── everything below is copied VERBATIM from FireSim.Step (byte-faithful fluid field) ──────────────
            int margin = Mathf.CeilToInt(p.flow + p.buoyancy + p.stretch + p.curl * 3f + p.flicker * 2f) + 2;
            int ax0 = Mathf.Max(0, Mathf.Min(boxX0, injX1 < 0 ? boxX0 : injX0) - margin);
            int ay0 = Mathf.Max(0, Mathf.Min(boxY0, injY1 < 0 ? boxY0 : injY0) - margin);
            int ax1 = Mathf.Min(W - 1, Mathf.Max(boxX1, injX1) + margin);
            int ay1 = Mathf.Min(H - 1, Mathf.Max(boxY1, injY1) + margin);

            System.Array.Clear(heatB, 0, heatB.Length);
            System.Array.Clear(fuelB, 0, fuelB.Length);
            int nbx0 = W, nby0 = H, nbx1 = -1, nby1 = -1;

            float curlScale = Mathf.Max(2f, p.curlScale);
            int tKey = Mathf.FloorToInt(t * 200f);
            bool multi = arms > 1;
            float dirNx = Mathf.Cos(dirRad), dirNy = Mathf.Sin(dirRad);
            for (int y = ay0; y <= ay1; y++)
                for (int x = ax0; x <= ax1; x++)
                {
                    int i = y * W + x;
                    float ox = x - cx, oy = y - cy;
                    float dist = Mathf.Sqrt(ox * ox + oy * oy);

                    float nx = dirNx, ny = dirNy;
                    float pixAng = 0f;
                    if (multi)
                    {
                        pixAng = Mathf.Atan2(oy, ox);
                        float armAng = dirRad + Mathf.Round((pixAng - dirRad) / sector) * sector;
                        nx = Mathf.Cos(armAng); ny = Mathf.Sin(armAng);
                    }

                    float along = ox * nx + oy * ny;
                    float lat = -ox * ny + oy * nx;
                    float latAbs = Mathf.Abs(lat);
                    float angOff = dist > 0.001f ? latAbs / dist : 0f;

                    float h = heat[i];
                    float rise = p.flow + (p.buoyancy + p.stretch) * h;
                    float vx = nx * rise, vy = ny * rise;

                    float qx = ox, qy = oy;
                    if (multi)
                    {
                        float folded = Mathf.Repeat(pixAng - dirRad, sector);
                        if (p.armMode == FireArmMode.Mirror && folded > sector * 0.5f) folded = sector - folded;
                        qx = Mathf.Cos(folded + dirRad) * dist; qy = Mathf.Sin(folded + dirRad) * dist;
                    }
                    // PyreNoise.Sample(..., warp=0) → PyrePlusField.Noise01 (see file header for the substitution).
                    float swirl = (PyrePlusField.Noise01((qx + cx) / curlScale, (qy + cy - t * 60f) / curlScale, seed + 31) - 0.5f)
                                  * p.curl * 3f * (0.3f + h);
                    vx += -ny * swirl;
                    vy += nx * swirl;

                    if (p.flicker > 0.001f)
                    {
                        int fh = (Mathf.RoundToInt(qx * 3f) * 73856093) ^ (Mathf.RoundToInt(qy * 3f) * 19349663);
                        vx += -ny * (Hash01(seed, fh, tKey) - 0.5f) * p.flicker * 2f;
                        vy += nx * (Hash01(seed, fh, tKey) - 0.5f) * p.flicker * 2f;
                    }

                    float sh = Sample(heat, x - vx * dt, y - vy * dt);
                    float sf = Sample(fuel, x - vx * dt, y - vy * dt);

                    float burned = sf * p.burn * dt;
                    sh += burned;
                    sf -= burned;

                    float cool = p.dissipation;
                    cool += p.pinch * angOff * 2.2f;
                    if (along < 0f) cool += (-along) * 0.06f + p.pinch;
                    if (p.breakup > 0.001f)
                    {
                        float nb = PyrePlusField.Noise01((qx + cx) / 5f + tKey * 0.13f, (qy + cy) / 5f - tKey * 0.19f, seed + 91);
                        cool += p.breakup * Mathf.Max(0f, nb - 0.35f) * 2.5f * (1.2f - sh);
                    }
                    sh *= Mathf.Max(0f, 1f - cool * dt);

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
            if (nbx1 < nbx0) { boxX0 = boxY0 = 0; boxX1 = boxY1 = 0; }
            else { boxX0 = nbx0; boxY0 = nby0; boxX1 = nbx1; boxY1 = nby1; }
        }

        /// Composite the current state into a layer buffer through the layer's own gradient. Verbatim from
        /// FireSim.Render (one ramp: low end smoke, high end fire).
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
