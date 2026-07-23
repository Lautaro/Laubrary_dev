// FireballSim — the cheap cellular flame, the "doom fire" family.
//
// The current Fire is a real fluid simulation: heat + fuel carried by a velocity field, buoyancy, curl
// noise. Beautiful, but heavy, and it needs a full replay from frame 0 to reach any frame. This is the
// opposite: each cell simply takes the value of a cell FURTHER FROM the source, minus a small random
// cooling, with a small random sideways slip. That one rule, iterated, is the classic flickering pixel
// fire — and it "just builds on last frame" exactly as asked. Still stateful (it reads the previous
// frame), so it's reached by replay like Fire, but each step is a single cheap pass with no trig, no
// bilinear advection, no noise field.
//
// The source here is a POINT (the centre), and heat propagates OUTWARD from it, so the natural shape is a
// radial burst — and mirroring it into N wedges gives the kaleidoscope-explosion the request describes,
// for free and in-model (not as a post pass on finished pixels).
using UnityEngine;

namespace Laubrary.Pyre
{
    public struct FireballParams
    {
        public float sourceHeat;     // how hot the centre injects (0..1), already scaled by Intensity
        public float sourceRadius;   // radius of the hot core, in pixels
        public float cooling;        // how much each cell loses per step as it moves outward (the flame's reach)
        public float spread;         // sideways slip strength — how much the tongues waver
        public float reach;          // hard confinement radius as a fraction of the canvas half
        public float sharpness;      // how hard off-axis cells cool — arm THINNESS, independent of length
        public int arms;             // radial wedges the flame is mirrored into (1 = a plain outward burst)
        public bool mirror;          // mirror alternate wedges (kaleidoscope) vs repeat them
    }

    public class FireballSim
    {
        public const float EdgeClearance = 2f;

        public int W, H;
        float[] heat, heatB;
        public int LastFrame = -1;

        public void Allocate(int w, int h)
        {
            if (W == w && H == h && heat != null) return;
            W = w; H = h;
            heat = new float[w * h]; heatB = new float[w * h];
        }

        public void Reset()
        {
            System.Array.Clear(heat, 0, heat.Length);
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

        /// One cellular step. `t` only varies the per-cell random draws frame to frame so the fire flickers.
        public void Step(in FireballParams p, int seed, float t, int frameIndex)
        {
            float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;
            float half = Mathf.Min(W, H) * 0.5f;
            float reachPx = Mathf.Clamp(p.reach * (half - EdgeClearance), 1f, half - EdgeClearance);
            int arms = Mathf.Max(1, p.arms);
            float wedge = Mathf.PI * 2f / arms;
            int tk = frameIndex;   // integer so a cold scrub to the same frame draws the same randomness

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float ox = x - cx, oy = y - cy;
                    float dist = Mathf.Sqrt(ox * ox + oy * oy);

                    // The hot core: injected fresh every step so the fire keeps burning from the centre.
                    if (dist <= p.sourceRadius)
                    {
                        heatB[i] = Mathf.Clamp01(p.sourceHeat * (1f - dist / Mathf.Max(0.001f, p.sourceRadius) * 0.4f));
                        continue;
                    }
                    if (dist > reachPx) { heatB[i] = 0f; continue; }   // hard confinement — never touch the edge

                    // Fold this cell into wedge 0, so all arms read the SAME source cells → an N-fold
                    // symmetric burst. Mirror flips alternate wedges so neighbours meet at a seam.
                    // A radial burst is rotationally symmetric, so folding it alone yields no visible arms;
                    // `axisDist` (0 on a wedge axis, 1 at the gap between two) drives extra cooling below, so
                    // the flame reaches far ALONG each axis and is cut between them — that is what turns the
                    // fireball into a pointed star.
                    float ang = Mathf.Atan2(oy, ox);
                    float sampAng = ang;
                    float axisDist = 0f;
                    if (arms > 1)
                    {
                        float foldedRaw = Mathf.Repeat(ang, wedge);            // [0, wedge)
                        axisDist = Mathf.Min(foldedRaw, wedge - foldedRaw) / (wedge * 0.5f);   // 0 axis → 1 gap
                        float samp = foldedRaw;
                        if (p.mirror && samp > wedge * 0.5f) samp = wedge - samp;
                        sampAng = samp;
                    }

                    // Source direction for this (folded) cell — a unit vector pointing back toward the centre.
                    float ux = Mathf.Cos(sampAng), uy = Mathf.Sin(sampAng);
                    // Sample the previous frame ONE step closer to the source, with a small sideways slip so
                    // the tongues waver instead of being straight radial spokes.
                    float slip = (Hash01(seed, i, tk) - 0.5f) * p.spread * 3f;
                    float sx = cx + ux * (dist - 1f) - uy * slip;
                    float sy = cy + uy * (dist - 1f) + ux * slip;
                    float src = SampleNearest(sx, sy);

                    // Cool as it travels outward — a per-cell random draw makes the flame's edge wispy and
                    // flickering rather than a smooth ramp. This is the whole look. `cooling` alone sets arm
                    // LENGTH (low cooling = heat survives further out = long arms).
                    float cool = p.cooling * (0.6f + 0.8f * Hash01(seed + 7, i, tk));
                    // Spokes: off-axis cells cool EXTRA, so the flame is a pointed arm along each wedge axis
                    // with cold gaps between. Crucially this term is ABSOLUTE (added, not multiplied into
                    // `cool`) — otherwise thinness would scale with `cooling`, and you could never get long
                    // AND thin: low cooling for length would also weaken the thinning into fat blobs. Kept
                    // separate, low cooling gives the length and `sharpness` gives the thinness independently.
                    if (arms > 1) cool += p.sharpness * axisDist * axisDist;
                    heatB[i] = Mathf.Max(0f, src - cool);
                }

            var tmp = heat; heat = heatB; heatB = tmp;
        }

        float SampleNearest(float x, float y)
        {
            int ix = Mathf.Clamp(Mathf.RoundToInt(x), 0, W - 1);
            int iy = Mathf.Clamp(Mathf.RoundToInt(y), 0, H - 1);
            return heat[iy * W + ix];
        }

        /// Composite through the layer's gradient (smoke → fire), like Fire and Height balls, so the family
        /// reads consistently. Writes into a transparent buffer (never the shared frame) — the caller
        /// Over-composites it.
        public void Render(Color32[] buf, Gradient ramp, float layerAlpha, float threshold, float contrast)
        {
            for (int i = 0; i < heat.Length; i++)
            {
                float v = heat[i];
                if (v <= threshold) { buf[i] = default; continue; }
                float tt = Mathf.Clamp01((v - threshold) / Mathf.Max(0.001f, 1f - threshold));
                tt = Mathf.Clamp01(Mathf.Pow(tt, Mathf.Max(0.05f, contrast)));
                var c = ramp != null ? ramp.Evaluate(tt) : Color.white;
                float a = Mathf.Clamp01(c.a * layerAlpha * Mathf.Clamp01(tt * 2.2f));
                buf[i] = a <= 0.002f ? default
                    : new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
            }
        }
    }
}
