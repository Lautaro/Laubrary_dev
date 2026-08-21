// RadialJetDraws — the eight published agent3_fork_radial gen-3 draws as `RadialJetSettings` factories: every value is
// that draw's contract (D:/Claude@GDrive/Flame/GEN3/contract/agent3_fork_radial/<draw>/params.json = gen.py's
// JetSpec(...) call), applied over the JetSpec class defaults exactly as gen.py constructs them, so a key a draw does
// not set holds the class default.
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    public static class RadialJetDraws
    {
        /// #001 `corona` (seed 101, 176 × 176, 30 frames @ 15 fps): the plain answer — gout opened to 360°, bias 1.05,
        /// 700 slots, EMBER crossfading into soot at the outer edge; breathes and boils, licks up, sits down.
        public static RadialJetSettings Corona() => new RadialJetSettings
        {
            w = 176, h = 176, nozzleX = 0.50f, nozzleY = 0.54f, aim = 0f, reach = 0.300f, spread = 180f, bias = 1.05f,
            drag = 1.45f, buoy = 0.17f, grav = 0f,
            r0 = 3.4f, growth = 0.150f, elong = 1.9f, roundAt = 0.30f,
            slots = 700, life = 0.60f, jitter = 0.44f, strength = 0.60f, cool = 1.15f, soot = 0.80f,
            rootR = 4.4f, rootAmp = 2.2f,
            shed = 0.13f, shedKick = 1.10f, shedLife = 1.35f, sparks = 16, sparkR = 1.30f,
            warp0 = 0.5f, warp1 = 4.4f, warpCell = 9.0f, warpOct = 2,
            steps = 0, lo = 0.20f, hi = 3.185f, curve = 0.692f, soft = 0.15f,
            ramp = PlusRampPresets.Ember(), sootRamp = PlusRampPresets.EmberSoot(), sootLo = PlusRampPresets.EmberSootLo, sootHi = PlusRampPresets.EmberSootHi,
        };

        /// #002 `fan` (seed 113, 144 × 216, 30 frames @ 15 fps): a 124° sheet thrown sideways with a 2° tilt — the arc
        /// end of the note; GOLD (no white) crossfading into a hot orange where the gas is old.
        public static RadialJetSettings Fan() => new RadialJetSettings
        {
            w = 144, h = 216, nozzleX = 0.09f, nozzleY = 0.50f, aim = 2f, reach = 0.600f, spread = 62f, bias = 1.10f,
            drag = 1.45f, buoy = 0.14f, grav = 0f,
            r0 = 3.0f, growth = 0.098f, elong = 3.4f, roundAt = 0.38f,
            slots = 560, life = 0.60f, jitter = 0.42f, strength = 0.58f, cool = 1.20f, soot = 0.95f,
            rootR = 3.8f, rootAmp = 2.2f,
            shed = 0.13f, shedKick = 1.15f, shedLife = 1.40f, sparks = 12, sparkR = 1.30f,
            warp0 = 0.5f, warp1 = 5.0f, warpCell = 9.5f, warpOct = 2,
            steps = 0, lo = 0.20f, hi = 3.442f, curve = 0.654f, soft = 0.14f,
            ramp = PlusRampPresets.JetGold(), sootRamp = PlusRampPresets.JetGoldHeat(), sootLo = 0.20f, sootHi = 0.85f,
        };

        /// #003 `crown` (seed 127, 184 × 180, 32 frames @ 14 fps): a burner RING — src_r 0.095 with 26 root lumps, a
        /// dark centre, strong buoyancy and a short reach; BURNER blue at the ports crossfading to orange tips by AGE.
        public static RadialJetSettings Crown() => new RadialJetSettings
        {
            w = 184, h = 180, nozzleX = 0.50f, nozzleY = 0.72f, aim = 0f, reach = 0.200f, spread = 180f, bias = 1.00f,
            srcR = 0.095f,
            drag = 1.30f, buoy = 0.90f, grav = 0f,
            r0 = 3.0f, growth = 0.160f, elong = 1.7f, roundAt = 0.30f,
            slots = 650, life = 0.64f, jitter = 0.44f, strength = 0.55f, cool = 1.15f, soot = 1.25f,
            rootR = 2.2f, rootAmp = 1.30f, rootK = 26,
            shed = 0.10f, shedKick = 1.10f, shedLife = 1.40f, sparks = 10, sparkR = 1.20f,
            warp0 = 0.4f, warp1 = 5.2f, warpCell = 8.0f, warpOct = 2,
            steps = 0, lo = 0.20f, hi = 2.076f, curve = 0.677f, soft = 0.16f,
            ramp = PlusRampPresets.JetBurner(), sootRamp = PlusRampPresets.JetBurnerTips(), sootLo = 0.18f, sootHi = 0.88f,
        };

        /// #004 `whirl` (seed 139, 176 × 176, 32 frames @ 15 fps): a fire whirl — three lobes, swirl 142°, spin 1 and
        /// warp_spin 1, so three curved arms rotate exactly once a loop; WHIRL magenta-black → white.
        public static RadialJetSettings Whirl() => new RadialJetSettings
        {
            w = 176, h = 176, nozzleX = 0.50f, nozzleY = 0.52f, aim = 0f, reach = 0.290f, spread = 180f, bias = 1.00f,
            lobes = 3, lobeDepth = 0.78f, lobeKick = 0.46f,
            swirl = 142f, spin = 1,
            drag = 1.40f, buoy = 0.13f, grav = 0f,
            r0 = 3.0f, growth = 0.112f, elong = 3.4f, roundAt = 0.46f,
            slots = 560, life = 0.58f, jitter = 0.42f, strength = 0.58f, cool = 1.25f,
            rootR = 4.2f, rootAmp = 2.1f,
            shed = 0.08f, shedKick = 1.10f, shedLife = 1.30f, sparks = 13, sparkR = 1.25f,
            warp0 = 0.4f, warp1 = 3.2f, warpCell = 9.0f, warpOct = 2, warpSpin = 1,
            steps = 0, lo = 0.20f, hi = 4.171f, curve = 0.549f, soft = 0.15f,
            ramp = PlusRampPresets.JetWhirl(), sootRamp = new PlusRamp(), sootLo = 0.35f, sootHi = 0.95f,
        };

        /// #005 `shockring` (seed 151, 176 × 176, 30 frames @ 13 fps): three concentric rings a loop seen FACE ON over a
        /// low slow disc; VIOLET (the wyrm ramp) — the transparent one.
        public static RadialJetSettings Shockring() => new RadialJetSettings
        {
            w = 176, h = 176, nozzleX = 0.50f, nozzleY = 0.52f, aim = 0f, reach = 0.240f, spread = 180f, bias = 1.10f,
            drag = 1.25f, buoy = 0.15f, grav = 0f,
            r0 = 3.0f, growth = 0.130f, elong = 1.8f, roundAt = 0.34f,
            slots = 400, life = 0.60f, jitter = 0.42f, strength = 0.50f, cool = 1.25f,
            rootR = 4.0f, rootAmp = 2.0f,
            shed = 0.06f, shedKick = 1.15f, shedLife = 1.35f, sparks = 9, sparkR = 1.20f,
            ringFlat = true, ringN = 3, ringK = 16, ringR0 = 3.0f, ringGrow = 0.185f, ringLife = 1.70f, ringReach = 1.32f, ringAmp = 3.40f,
            warp0 = 0.4f, warp1 = 3.4f, warpCell = 10.0f, warpOct = 2,
            steps = 0, lo = 0.20f, hi = 3.001f, curve = 0.652f, soft = 0.20f,
            ramp = PlusRampPresets.JetWyrm(), sootRamp = new PlusRamp(), sootLo = 0.35f, sootHi = 0.95f,
        };

        /// #006 `maw` (seed 163, 200 × 152, 30 frames @ 13 fps): a vent belching a 164° wall straight up, guttering twice
        /// a loop (pulse 2 × 0.72), the only draw with sag; DIRTY (no white) crossfading into soot.
        public static RadialJetSettings Maw() => new RadialJetSettings
        {
            w = 200, h = 152, nozzleX = 0.50f, nozzleY = 0.76f, aim = -90f, reach = 0.300f, spread = 82f, bias = 1.05f,
            drag = 1.85f, buoy = 0.17f, grav = 0.10f,
            r0 = 3.4f, growth = 0.150f, elong = 1.8f, roundAt = 0.26f,
            slots = 650, life = 0.66f, jitter = 0.52f, strength = 0.58f, cool = 1.15f, soot = 1.10f,
            pulseN = 2, pulseDepth = 0.72f,
            rootR = 3.8f, rootAmp = 2.0f,
            shed = 0.18f, shedKick = 1.10f, shedLife = 1.45f, sparks = 13, sparkR = 1.45f,
            warp0 = 0.8f, warp1 = 5.0f, warpCell = 11.0f, warpOct = 2,
            steps = 0, lo = 0.20f, hi = 6.724f, curve = 0.487f, soft = 0.18f,
            ramp = PlusRampPresets.JetDirty(), sootRamp = PlusRampPresets.JetDirtySoot(), sootLo = 0.30f, sootHi = 0.95f,
        };

        /// #007 `starburst` (seed 173, 176 × 176, 28 frames @ 17 fps): corona's disc gathered into seven tongues with
        /// real gaps (lobe depth 0.70, kick 0.42), short life, high key; SOLAR — the hottest ramp, it flickers.
        public static RadialJetSettings Starburst() => new RadialJetSettings
        {
            w = 176, h = 176, nozzleX = 0.50f, nozzleY = 0.50f, aim = 0f, reach = 0.310f, spread = 180f, bias = 1.00f,
            lobes = 7, lobeDepth = 0.70f, lobeKick = 0.42f,
            drag = 1.10f, buoy = 0.11f, grav = 0f,
            r0 = 2.9f, growth = 0.105f, elong = 3.0f, roundAt = 0.46f,
            slots = 640, life = 0.48f, jitter = 0.42f, strength = 0.58f, cool = 1.30f,
            rootR = 4.6f, rootAmp = 2.3f,
            shed = 0.10f, shedKick = 1.25f, shedLife = 1.30f, sparks = 18, sparkR = 1.20f,
            warp0 = 0.4f, warp1 = 3.8f, warpCell = 8.5f, warpOct = 2,
            steps = 0, lo = 0.20f, hi = 3.089f, curve = 0.638f, soft = 0.13f,
            ramp = PlusRampPresets.JetSolar(), sootRamp = new PlusRamp(), sootLo = 0.35f, sootHi = 0.95f,
        };

        /// #008 `halo` (seed 181, 192 × 192, 32 frames @ 12 fps): the cold one — a thin pale haze drifting through standing
        /// concentric shells (shock 2.6 × 0.45), lowest strength and ceilings; GHOST teal → white.
        public static RadialJetSettings Halo() => new RadialJetSettings
        {
            w = 192, h = 192, nozzleX = 0.50f, nozzleY = 0.52f, aim = 0f, reach = 0.285f, spread = 180f, bias = 1.00f,
            drag = 1.05f, buoy = 0.19f, grav = 0f,
            r0 = 3.0f, growth = 0.125f, elong = 2.0f, roundAt = 0.36f,
            slots = 480, life = 0.68f, jitter = 0.48f, strength = 0.50f, cool = 1.10f,
            shockN = 2.6f, shockDepth = 0.45f,
            rootR = 3.4f, rootAmp = 1.7f,
            shed = 0.15f, shedKick = 1.20f, shedLife = 1.45f, sparks = 8, sparkR = 1.15f,
            warp0 = 0.4f, warp1 = 5.6f, warpCell = 11.0f, warpOct = 2,
            steps = 0, lo = 0.20f, hi = 2.433f, curve = 0.45f, soft = 0.24f,
            ramp = PlusRampPresets.JetGhost(), sootRamp = new PlusRamp(), sootLo = 0.35f, sootHi = 0.95f,
        };

        /// The contract seed of each draw (the spec seed of a parity run; the form takes its seed from the spec).
        public static int Seed(RadialJetForm.Variant v) => v switch
        {
            RadialJetForm.Variant.Corona => 101, RadialJetForm.Variant.Fan => 113, RadialJetForm.Variant.Crown => 127, RadialJetForm.Variant.Whirl => 139,
            RadialJetForm.Variant.Shockring => 151, RadialJetForm.Variant.Maw => 163, RadialJetForm.Variant.Starburst => 173, _ => 181,
        };
    }
}
