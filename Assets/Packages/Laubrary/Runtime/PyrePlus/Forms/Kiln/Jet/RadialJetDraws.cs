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
            w = 176, h = 176, nozzleX = 0.50f, nozzleY = 0.54f, aim = new ZUIValue(0f), reach = new ZUIValue(0.300f), spread = new ZUIValue(180f), bias = new ZUIValue(1.05f),
            drag = 1.45f, buoy = 0.17f, grav = 0f,
            r0 = new ZUIValue(3.4f), growth = 0.150f, elong = new ZUIValue(1.9f), roundAt = 0.30f,
            slots = 700, life = 0.60f, jitter = new ZUIValue(0.44f), strength = new ZUIValue(0.60f), cool = new ZUIValue(1.15f), soot = new ZUIValue(0.80f),
            rootR = new ZUIValue(4.4f), rootAmp = new ZUIValue(2.2f),
            shed = new ZUIValue(0.13f), shedKick = new ZUIValue(1.10f), shedLife = 1.35f, sparks = 16, sparkR = new ZUIValue(1.30f),
            warp0 = new ZUIValue(0.5f), warp1 = new ZUIValue(4.4f), warpCell = 9.0f, warpOct = 2,
            steps = 0, lo = new ZUIValue(0.20f), hi = new ZUIValue(3.185f), curve = new ZUIValue(0.692f), soft = new ZUIValue(0.15f),
            ramp = PlusRampPresets.Ember(), sootRamp = PlusRampPresets.EmberSoot(), sootLo = new ZUIValue(PlusRampPresets.EmberSootLo), sootHi = new ZUIValue(PlusRampPresets.EmberSootHi),
        };

        /// #002 `fan` (seed 113, 144 × 216, 30 frames @ 15 fps): a 124° sheet thrown sideways with a 2° tilt — the arc
        /// end of the note; GOLD (no white) crossfading into a hot orange where the gas is old.
        public static RadialJetSettings Fan() => new RadialJetSettings
        {
            w = 144, h = 216, nozzleX = 0.09f, nozzleY = 0.50f, aim = new ZUIValue(2f), reach = new ZUIValue(0.600f), spread = new ZUIValue(62f), bias = new ZUIValue(1.10f),
            drag = 1.45f, buoy = 0.14f, grav = 0f,
            r0 = new ZUIValue(3.0f), growth = 0.098f, elong = new ZUIValue(3.4f), roundAt = 0.38f,
            slots = 560, life = 0.60f, jitter = new ZUIValue(0.42f), strength = new ZUIValue(0.58f), cool = new ZUIValue(1.20f), soot = new ZUIValue(0.95f),
            rootR = new ZUIValue(3.8f), rootAmp = new ZUIValue(2.2f),
            shed = new ZUIValue(0.13f), shedKick = new ZUIValue(1.15f), shedLife = 1.40f, sparks = 12, sparkR = new ZUIValue(1.30f),
            warp0 = new ZUIValue(0.5f), warp1 = new ZUIValue(5.0f), warpCell = 9.5f, warpOct = 2,
            steps = 0, lo = new ZUIValue(0.20f), hi = new ZUIValue(3.442f), curve = new ZUIValue(0.654f), soft = new ZUIValue(0.14f),
            ramp = PlusRampPresets.JetGold(), sootRamp = PlusRampPresets.JetGoldHeat(), sootLo = new ZUIValue(0.20f), sootHi = new ZUIValue(0.85f),
        };

        /// #003 `crown` (seed 127, 184 × 180, 32 frames @ 14 fps): a burner RING — src_r 0.095 with 26 root lumps, a
        /// dark centre, strong buoyancy and a short reach; BURNER blue at the ports crossfading to orange tips by AGE.
        public static RadialJetSettings Crown() => new RadialJetSettings
        {
            w = 184, h = 180, nozzleX = 0.50f, nozzleY = 0.72f, aim = new ZUIValue(0f), reach = new ZUIValue(0.200f), spread = new ZUIValue(180f), bias = new ZUIValue(1.00f),
            srcR = new ZUIValue(0.095f),
            drag = 1.30f, buoy = 0.90f, grav = 0f,
            r0 = new ZUIValue(3.0f), growth = 0.160f, elong = new ZUIValue(1.7f), roundAt = 0.30f,
            slots = 650, life = 0.64f, jitter = new ZUIValue(0.44f), strength = new ZUIValue(0.55f), cool = new ZUIValue(1.15f), soot = new ZUIValue(1.25f),
            rootR = new ZUIValue(2.2f), rootAmp = new ZUIValue(1.30f), rootK = 26,
            shed = new ZUIValue(0.10f), shedKick = new ZUIValue(1.10f), shedLife = 1.40f, sparks = 10, sparkR = new ZUIValue(1.20f),
            warp0 = new ZUIValue(0.4f), warp1 = new ZUIValue(5.2f), warpCell = 8.0f, warpOct = 2,
            steps = 0, lo = new ZUIValue(0.20f), hi = new ZUIValue(2.076f), curve = new ZUIValue(0.677f), soft = new ZUIValue(0.16f),
            ramp = PlusRampPresets.JetBurner(), sootRamp = PlusRampPresets.JetBurnerTips(), sootLo = new ZUIValue(0.18f), sootHi = new ZUIValue(0.88f),
        };

        /// #004 `whirl` (seed 139, 176 × 176, 32 frames @ 15 fps): a fire whirl — three lobes, swirl 142°, spin 1 and
        /// warp_spin 1, so three curved arms rotate exactly once a loop; WHIRL magenta-black → white.
        public static RadialJetSettings Whirl() => new RadialJetSettings
        {
            w = 176, h = 176, nozzleX = 0.50f, nozzleY = 0.52f, aim = new ZUIValue(0f), reach = new ZUIValue(0.290f), spread = new ZUIValue(180f), bias = new ZUIValue(1.00f),
            lobes = 3, lobeDepth = new ZUIValue(0.78f), lobeKick = new ZUIValue(0.46f),
            swirl = new ZUIValue(142f), spin = 1,
            drag = 1.40f, buoy = 0.13f, grav = 0f,
            r0 = new ZUIValue(3.0f), growth = 0.112f, elong = new ZUIValue(3.4f), roundAt = 0.46f,
            slots = 560, life = 0.58f, jitter = new ZUIValue(0.42f), strength = new ZUIValue(0.58f), cool = new ZUIValue(1.25f),
            rootR = new ZUIValue(4.2f), rootAmp = new ZUIValue(2.1f),
            shed = new ZUIValue(0.08f), shedKick = new ZUIValue(1.10f), shedLife = 1.30f, sparks = 13, sparkR = new ZUIValue(1.25f),
            warp0 = new ZUIValue(0.4f), warp1 = new ZUIValue(3.2f), warpCell = 9.0f, warpOct = 2, warpSpin = 1,
            steps = 0, lo = new ZUIValue(0.20f), hi = new ZUIValue(4.171f), curve = new ZUIValue(0.549f), soft = new ZUIValue(0.15f),
            ramp = PlusRampPresets.JetWhirl(), sootRamp = new PlusRamp(), sootLo = new ZUIValue(0.35f), sootHi = new ZUIValue(0.95f),
        };

        /// #005 `shockring` (seed 151, 176 × 176, 30 frames @ 13 fps): three concentric rings a loop seen FACE ON over a
        /// low slow disc; VIOLET (the wyrm ramp) — the transparent one.
        public static RadialJetSettings Shockring() => new RadialJetSettings
        {
            w = 176, h = 176, nozzleX = 0.50f, nozzleY = 0.52f, aim = new ZUIValue(0f), reach = new ZUIValue(0.240f), spread = new ZUIValue(180f), bias = new ZUIValue(1.10f),
            drag = 1.25f, buoy = 0.15f, grav = 0f,
            r0 = new ZUIValue(3.0f), growth = 0.130f, elong = new ZUIValue(1.8f), roundAt = 0.34f,
            slots = 400, life = 0.60f, jitter = new ZUIValue(0.42f), strength = new ZUIValue(0.50f), cool = new ZUIValue(1.25f),
            rootR = new ZUIValue(4.0f), rootAmp = new ZUIValue(2.0f),
            shed = new ZUIValue(0.06f), shedKick = new ZUIValue(1.15f), shedLife = 1.35f, sparks = 9, sparkR = new ZUIValue(1.20f),
            ringFlat = true, ringN = 3, ringK = 16, ringR0 = new ZUIValue(3.0f), ringGrow = 0.185f, ringLife = 1.70f, ringReach = new ZUIValue(1.32f), ringAmp = new ZUIValue(3.40f),
            warp0 = new ZUIValue(0.4f), warp1 = new ZUIValue(3.4f), warpCell = 10.0f, warpOct = 2,
            steps = 0, lo = new ZUIValue(0.20f), hi = new ZUIValue(3.001f), curve = new ZUIValue(0.652f), soft = new ZUIValue(0.20f),
            ramp = PlusRampPresets.JetWyrm(), sootRamp = new PlusRamp(), sootLo = new ZUIValue(0.35f), sootHi = new ZUIValue(0.95f),
        };

        /// #006 `maw` (seed 163, 200 × 152, 30 frames @ 13 fps): a vent belching a 164° wall straight up, guttering twice
        /// a loop (pulse 2 × 0.72), the only draw with sag; DIRTY (no white) crossfading into soot.
        public static RadialJetSettings Maw() => new RadialJetSettings
        {
            w = 200, h = 152, nozzleX = 0.50f, nozzleY = 0.76f, aim = new ZUIValue(-90f), reach = new ZUIValue(0.300f), spread = new ZUIValue(82f), bias = new ZUIValue(1.05f),
            drag = 1.85f, buoy = 0.17f, grav = 0.10f,
            r0 = new ZUIValue(3.4f), growth = 0.150f, elong = new ZUIValue(1.8f), roundAt = 0.26f,
            slots = 650, life = 0.66f, jitter = new ZUIValue(0.52f), strength = new ZUIValue(0.58f), cool = new ZUIValue(1.15f), soot = new ZUIValue(1.10f),
            pulseN = 2, pulseDepth = new ZUIValue(0.72f),
            rootR = new ZUIValue(3.8f), rootAmp = new ZUIValue(2.0f),
            shed = new ZUIValue(0.18f), shedKick = new ZUIValue(1.10f), shedLife = 1.45f, sparks = 13, sparkR = new ZUIValue(1.45f),
            warp0 = new ZUIValue(0.8f), warp1 = new ZUIValue(5.0f), warpCell = 11.0f, warpOct = 2,
            steps = 0, lo = new ZUIValue(0.20f), hi = new ZUIValue(6.724f), curve = new ZUIValue(0.487f), soft = new ZUIValue(0.18f),
            ramp = PlusRampPresets.JetDirty(), sootRamp = PlusRampPresets.JetDirtySoot(), sootLo = new ZUIValue(0.30f), sootHi = new ZUIValue(0.95f),
        };

        /// #007 `starburst` (seed 173, 176 × 176, 28 frames @ 17 fps): corona's disc gathered into seven tongues with
        /// real gaps (lobe depth 0.70, kick 0.42), short life, high key; SOLAR — the hottest ramp, it flickers.
        public static RadialJetSettings Starburst() => new RadialJetSettings
        {
            w = 176, h = 176, nozzleX = 0.50f, nozzleY = 0.50f, aim = new ZUIValue(0f), reach = new ZUIValue(0.310f), spread = new ZUIValue(180f), bias = new ZUIValue(1.00f),
            lobes = 7, lobeDepth = new ZUIValue(0.70f), lobeKick = new ZUIValue(0.42f),
            drag = 1.10f, buoy = 0.11f, grav = 0f,
            r0 = new ZUIValue(2.9f), growth = 0.105f, elong = new ZUIValue(3.0f), roundAt = 0.46f,
            slots = 640, life = 0.48f, jitter = new ZUIValue(0.42f), strength = new ZUIValue(0.58f), cool = new ZUIValue(1.30f),
            rootR = new ZUIValue(4.6f), rootAmp = new ZUIValue(2.3f),
            shed = new ZUIValue(0.10f), shedKick = new ZUIValue(1.25f), shedLife = 1.30f, sparks = 18, sparkR = new ZUIValue(1.20f),
            warp0 = new ZUIValue(0.4f), warp1 = new ZUIValue(3.8f), warpCell = 8.5f, warpOct = 2,
            steps = 0, lo = new ZUIValue(0.20f), hi = new ZUIValue(3.089f), curve = new ZUIValue(0.638f), soft = new ZUIValue(0.13f),
            ramp = PlusRampPresets.JetSolar(), sootRamp = new PlusRamp(), sootLo = new ZUIValue(0.35f), sootHi = new ZUIValue(0.95f),
        };

        /// #008 `halo` (seed 181, 192 × 192, 32 frames @ 12 fps): the cold one — a thin pale haze drifting through standing
        /// concentric shells (shock 2.6 × 0.45), lowest strength and ceilings; GHOST teal → white.
        public static RadialJetSettings Halo() => new RadialJetSettings
        {
            w = 192, h = 192, nozzleX = 0.50f, nozzleY = 0.52f, aim = new ZUIValue(0f), reach = new ZUIValue(0.285f), spread = new ZUIValue(180f), bias = new ZUIValue(1.00f),
            drag = 1.05f, buoy = 0.19f, grav = 0f,
            r0 = new ZUIValue(3.0f), growth = 0.125f, elong = new ZUIValue(2.0f), roundAt = 0.36f,
            slots = 480, life = 0.68f, jitter = new ZUIValue(0.48f), strength = new ZUIValue(0.50f), cool = new ZUIValue(1.10f),
            shockN = 2.6f, shockDepth = new ZUIValue(0.45f),
            rootR = new ZUIValue(3.4f), rootAmp = new ZUIValue(1.7f),
            shed = new ZUIValue(0.15f), shedKick = new ZUIValue(1.20f), shedLife = 1.45f, sparks = 8, sparkR = new ZUIValue(1.15f),
            warp0 = new ZUIValue(0.4f), warp1 = new ZUIValue(5.6f), warpCell = 11.0f, warpOct = 2,
            steps = 0, lo = new ZUIValue(0.20f), hi = new ZUIValue(2.433f), curve = new ZUIValue(0.45f), soft = new ZUIValue(0.24f),
            ramp = PlusRampPresets.JetGhost(), sootRamp = new PlusRamp(), sootLo = new ZUIValue(0.35f), sootHi = new ZUIValue(0.95f),
        };

        /// The contract seed of each draw (the spec seed of a parity run; the form takes its seed from the spec).
        public static int Seed(RadialJetForm.Variant v) => v switch
        {
            RadialJetForm.Variant.Corona => 101, RadialJetForm.Variant.Fan => 113, RadialJetForm.Variant.Crown => 127, RadialJetForm.Variant.Whirl => 139,
            RadialJetForm.Variant.Shockring => 151, RadialJetForm.Variant.Maw => 163, RadialJetForm.Variant.Starburst => 173, _ => 181,
        };
    }
}
