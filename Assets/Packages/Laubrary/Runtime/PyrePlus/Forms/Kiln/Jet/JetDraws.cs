// JetDraws — the five published agent3 gen-2 draws as `JetSettings` factories: every value is that draw's contract
// (D:/Claude@GDrive/Flame/GEN2/contract/agent3/<draw>/params.json = gen.py's JetSpec(...) call), applied over the
// JetSpec class defaults exactly as gen.py constructs them, so a key a draw does not set holds the class default.
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    public static class JetDraws
    {
        /// #001 `lance` (seed 11, 168 × 52, 28 frames @ 16 fps): a 3.4° needle, high speed, almost no drag or growth,
        /// elong 4.2 streaks, shock diamonds (3.4 nodes per reach at depth 0.60), TORCH ramp, continuous.
        public static JetSettings Lance() => new JetSettings
        {
            w = 168, h = 52, nozzleX = 0.05f, nozzleY = 0.52f, aim = 0f, reach = 0.86f, spread = 3.4f,
            drag = 0.50f, buoy = 0.05f, grav = 0f,
            r0 = 2.3f, growth = 0.042f, elong = 4.2f, roundAt = 0.55f,
            slots = 170, life = 0.42f, jitter = 0.42f, strength = 0.42f, cool = 1.35f,
            shockN = 3.4f, shockDepth = 0.60f,
            rootR = 3.1f, rootAmp = 2.1f,
            shed = 0f, sparks = 7, sparkR = 1.15f,
            warp0 = 0.25f, warp1 = 2.8f, warpCell = 7f, warpOct = 2,
            steps = 0, lo = 0.20f, hi = 2.473f, curve = 0.641f, soft = 0.11f,
            ramp = PlusRampPresets.JetTorch(), sootRamp = new PlusRamp(), sootLo = 0.35f, sootHi = 0.95f,
        };

        /// #002 `gout` (seed 23, 160 × 92, 30 frames @ 15 fps): THE CLASSIC weapon gout — wide cone, heavy entrainment,
        /// late buoyancy, a fifth of the slots shed as tumbling fireballs, EMBER ramp crossfading into soot at the tip.
        public static JetSettings Gout() => new JetSettings
        {
            w = 160, h = 92, nozzleX = 0.06f, nozzleY = 0.68f, aim = -1f, reach = 0.78f, spread = 15f,
            drag = 1.45f, buoy = 0.30f, grav = 0f,
            r0 = 2.8f, growth = 0.078f, elong = 2.6f, roundAt = 0.26f,
            slots = 210, life = 0.60f, jitter = 0.58f, strength = 0.42f, cool = 1.55f, soot = 0.85f,
            rootR = 3.6f, rootAmp = 2.2f,
            shed = 0.18f, shedKick = 1.25f, shedLife = 1.40f, sparks = 9, sparkR = 1.35f,
            warp0 = 0.5f, warp1 = 3.8f, warpCell = 9.5f, warpOct = 2,
            steps = 0, lo = 0.20f, hi = 1.975f, curve = 0.524f, soft = 0.15f,
            ramp = PlusRampPresets.Ember(), sootRamp = PlusRampPresets.EmberSoot(), sootLo = PlusRampPresets.EmberSootLo, sootHi = PlusRampPresets.EmberSootHi,
        };

        /// #003 `sputter` (seed 37, 152 × 116, 30 frames @ 13 fps): fuel-rich and drooping — aimed 9° down with sag
        /// beating lift, surging twice a loop (depth 0.78), DIRTY ramp with no white, 16 shades.
        public static JetSettings Sputter() => new JetSettings
        {
            w = 152, h = 116, nozzleX = 0.07f, nozzleY = 0.31f, aim = 9f, reach = 0.72f, spread = 17f,
            drag = 1.95f, buoy = 0.10f, grav = 0.35f,
            r0 = 3.4f, growth = 0.105f, elong = 1.7f, roundAt = 0.20f,
            slots = 155, life = 0.66f, jitter = 0.72f, strength = 0.50f, cool = 1.30f, soot = 1.15f,
            pulseN = 2, pulseDepth = 0.78f,
            rootR = 3.4f, rootAmp = 2.0f,
            shed = 0.24f, shedKick = 1.20f, shedLife = 1.5f, sparks = 6, sparkR = 1.5f,
            warp0 = 0.9f, warp1 = 4.4f, warpCell = 11f, warpOct = 2,
            steps = 16, lo = 0.20f, hi = 2.615f, curve = 0.487f, soft = 0.18f,
            ramp = PlusRampPresets.JetDirty(), sootRamp = PlusRampPresets.JetDirtySoot(), sootLo = 0.30f, sootHi = 0.95f,
        };

        /// #004 `whip` (seed 53, 168 × 104, 32 frames @ 15 fps): the weapon SWUNG — the aim sweeps 20° either side once
        /// a loop and every puff keeps the aim it was born under, so the stream is an S-curve; GOLD ramp crossfading
        /// into a hot orange at the head, 24 shades.
        public static JetSettings Whip() => new JetSettings
        {
            w = 168, h = 104, nozzleX = 0.07f, nozzleY = 0.50f, aim = 0f, reach = 0.62f, spread = 12f,
            drag = 1.30f, buoy = 0.09f, grav = 0f,
            r0 = 2.9f, growth = 0.080f, elong = 3.4f, roundAt = 0.46f,
            slots = 205, life = 0.52f, jitter = 0.62f, strength = 0.42f, cool = 1.45f, soot = 0.95f,
            sweep = 20f, sweepN = 1,
            rootR = 3.2f, rootAmp = 2.0f,
            shed = 0.10f, shedKick = 1.3f, shedLife = 1.3f, sparks = 8, sparkR = 1.25f,
            warp0 = 0.5f, warp1 = 7.0f, warpCell = 8f, warpOct = 2,
            steps = 24, lo = 0.20f, hi = 2.895f, curve = 0.535f, soft = 0.13f,
            ramp = PlusRampPresets.JetGold(), sootRamp = PlusRampPresets.JetGoldHeat(), sootLo = 0.20f, sootHi = 0.85f,
        };

        /// #005 `wyrm` (seed 71, 180 × 100, 30 frames @ 13 fps): a coherent lance shedding three vortex rings a loop,
        /// drawn brighter than the lance they collar; WYRM ramp, the most transparent, 32 shades.
        public static JetSettings Wyrm() => new JetSettings
        {
            w = 180, h = 100, nozzleX = 0.06f, nozzleY = 0.50f, aim = 0f, reach = 0.62f, spread = 7f,
            drag = 1.10f, buoy = 0.12f, grav = 0f,
            r0 = 2.7f, growth = 0.075f, elong = 2.0f, roundAt = 0.40f,
            slots = 150, life = 0.58f, jitter = 0.44f, strength = 0.44f, cool = 1.5f,
            rootR = 3.3f, rootAmp = 2.0f,
            shed = 0.06f, shedKick = 1.2f, shedLife = 1.4f, sparks = 5, sparkR = 1.2f,
            ringN = 3, ringK = 12, ringR0 = 3.5f, ringGrow = 0.135f, ringLife = 2.3f, ringReach = 1.55f, ringAmp = 1.45f,
            warp0 = 0.4f, warp1 = 5.2f, warpCell = 10f, warpOct = 2,
            steps = 32, lo = 0.20f, hi = 1.918f, curve = 0.54f, soft = 0.22f,
            ramp = PlusRampPresets.JetWyrm(), sootRamp = new PlusRamp(), sootLo = 0.35f, sootHi = 0.95f,
        };

        /// The contract seed of each draw (the spec seed of a parity run; the form takes its seed from the spec).
        public static int Seed(JetForm.Variant v) => v switch
        {
            JetForm.Variant.Lance => 11, JetForm.Variant.Gout => 23, JetForm.Variant.Sputter => 37, JetForm.Variant.Whip => 53, _ => 71,
        };
    }
}
