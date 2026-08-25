// ExplosiveJetDraws — the ten published agent3_fork_explosive gen-7 draws as `ExplosiveJetSettings` factories: every value
// is that draw's contract (D:/Claude@GDrive/Flame/GEN7/contract/agent3_fork_explosive/<draw>/params.json = gen.py's
// JetSpec(...) call, `fitted` hi / curve included), applied over the JetSpec class defaults exactly as gen.py constructs
// them, so a key a draw does not set holds the class default. GENERATED from the contract JSON (not typed), then read.
using System.Collections.Generic;

namespace Laubrary.Pyre.Forms.Kiln
{
    public static class ExplosiveJetDraws
    {
        static ExplosiveBlast B(float at, float pow = 1f, float share = 1f, float offX = 0f, float offY = 0f) =>
            new ExplosiveBlast { at = at, pow = pow, share = share, offX = offX, offY = offY };

        /// #001 `detonate`: THE PLAIN ANSWER and the draw the generation was tuned on — one blast at 0, a 180° fireball that holds, goes solid and dies from the outside in; fracture certain (3 pieces, cut 0.16), EMBER crossfading into soot. Seed 101, 184 × 184, 30 frames @ 15 fps.
        public static ExplosiveJetSettings Detonate() => new ExplosiveJetSettings
        {
            w = 184, h = 184, nozzleX = 0.5f, nozzleY = 0.52f,
            aim = new ZUIValue(0f), reach = new ZUIValue(0.205f), spread = new ZUIValue(180f), bias = 1.02f, drag = 1.55f, buoy = 0.2f, r0 = new ZUIValue(3.4f),
            growth = 0.15f, elong = new ZUIValue(2.1f), slots = 560, life = 0.58f, jitter = new ZUIValue(0.46f), strength = new ZUIValue(0.6f), cool = new ZUIValue(1.3f),
            soot = new ZUIValue(0.55f), rootR = new ZUIValue(4.2f), rootAmp = new ZUIValue(2f), shed = new ZUIValue(0.1f), shedKick = new ZUIValue(1.1f), shedLife = 1.05f, sparks = 22,
            sparkR = new ZUIValue(1.3f), warp0 = new ZUIValue(0.5f), warp1 = new ZUIValue(4.4f), blastSpan = 0.11f, blastSkew = 2.5f, blastFront = 0.6f, velSpread = 0.78f,
            swell = 0.115f, hold = 1.7f, shrink = 0.8f, shrinkAt = 0.44f, leadDie = 0.42f, opaq = 0.55f, shedSwell = 0.9f,
            lo = new ZUIValue(0.2f), hi = new ZUIValue(7.788f), curve = new ZUIValue(0.55f), soft = new ZUIValue(0.2f),
            blasts = new List<ExplosiveBlast> { B(0f) },
            fracture = new ExplosiveFracture { chance = 1f, pieces = 3, at = 0.3f, open = 1.05f, kick = 0.42f, spin = 9f, grip = 0.95f, rot = 0.07f, drift = 0.2f, stagger = 0.2f, cut = 0.16f, body = 0.76f },
            fracture2 = new ExplosiveFracture2 { chance = 0.6f, at = 0.58f, open = 0.62f, cut = 0.045f, kick = 0.16f, drift = 0.03f, spin = 8f },
            flash = new ExplosiveFlash { radius = 9f, amp = 2.8f, life = 0.095f, grow = 1.7f },
            chunks = new ExplosiveChunks { count = 14, radius = 2.6f, reach = 1.15f, life = 1.2f, sag = 0.26f, amp = 1.6f },
            gobs = new ExplosiveGobs { count = 22, radius = 5f, reach = 0.78f, life = 0.9f, amp = 1.3f, swell = 1.55f },
            dust = new ExplosiveDust { count = 76, radius = 2f, from = 0.38f, to = 0.88f, where = 0.95f, reach = 0.38f, drag = 1.7f, life = 0.58f, amp = 1.2f },
            ramp = PyreRampPresets.Ember(), sootRamp = PyreRampPresets.EmberSoot(), sootLo = new ZUIValue(PyreRampPresets.EmberSootLo), sootHi = new ZUIValue(PyreRampPresets.EmberSootHi),
        };

        /// #002 `backdraft`: aimed UP (−90°) with hard buoyancy and a little sag, the fattest puffs and the tallest exposure (hi 15.1); 4 pieces, EMBER. Seed 277, 196 × 224, 32 frames @ 13 fps.
        public static ExplosiveJetSettings Backdraft() => new ExplosiveJetSettings
        {
            w = 196, h = 224, nozzleX = 0.5f, nozzleY = 0.8f,
            aim = new ZUIValue(-90f), reach = new ZUIValue(0.23f), spread = new ZUIValue(80f), bias = 1.05f, drag = 2.4f, buoy = 0.52f, grav = 0.04f,
            r0 = new ZUIValue(3.6f), growth = 0.165f, elong = new ZUIValue(1.8f), roundAt = 0.26f, slots = 640, life = 0.56f, jitter = new ZUIValue(0.5f),
            strength = new ZUIValue(0.6f), cool = new ZUIValue(1.1f), soot = new ZUIValue(0.78f), rootR = new ZUIValue(4f), rootAmp = new ZUIValue(2f), shed = new ZUIValue(0.11f), shedKick = new ZUIValue(1.1f),
            shedLife = 1.15f, sparks = 12, sparkR = new ZUIValue(1.4f), warp0 = new ZUIValue(0.8f), warp1 = new ZUIValue(5.2f), warpCell = 11f, blastSpan = 0.14f,
            blastSkew = 2f, blastFront = 0.4f, velSpread = 0.8f, swell = 0.085f, hold = 2f, shrink = 0.8f, leadDie = 0.55f,
            opaq = 0.5f, shedSwell = 0.8f, lo = new ZUIValue(0.2f), hi = new ZUIValue(15.116f), curve = new ZUIValue(0.439f), soft = new ZUIValue(0.2f),
            blasts = new List<ExplosiveBlast> { B(0f) },
            fracture = new ExplosiveFracture { chance = 1f, pieces = 4, at = 0.42f, open = 0.86f, kick = 0.34f, spin = 5f, rot = 0.18f, drift = 0.135f, stagger = 0.3f, cut = 0.16f, body = 0.85f },
            fracture2 = new ExplosiveFracture2 { chance = 0.6f, at = 0.64f, open = 0.6f, cut = 0.05f, kick = 0.2f, drift = 0.05f, spin = 5f, stagger = 0.18f },
            flash = new ExplosiveFlash { radius = 10f, amp = 2.4f, life = 0.11f, grow = 2f },
            chunks = new ExplosiveChunks { count = 9, radius = 2.6f, reach = 0.95f, life = 1.35f, sag = 0.45f, amp = 1.3f, wide = 1.15f },
            gobs = new ExplosiveGobs { count = 30, radius = 4.8f, reach = 0.6f, life = 1f, amp = 1.15f, swell = 1.5f, sag = 0.16f, wide = 1.2f },
            dust = new ExplosiveDust { count = 82, radius = 2.3f, from = 0.42f, to = 0.92f, where = 0.95f, reach = 0.3f, drag = 2.1f, life = 0.7f, sag = 0.26f, wide = 1.3f },
            ramp = PyreRampPresets.Ember(), sootRamp = PyreRampPresets.EmberSoot(), sootLo = new ZUIValue(PyreRampPresets.EmberSootLo), sootHi = new ZUIValue(PyreRampPresets.EmberSootHi),
        };

        /// #003 `chain`: THREE staggered blasts across a wide frame (seats at ±0.245), each with its own violence and share; DIRTY, the low-contrast one. Seed 241, 296 × 168, 32 frames @ 15 fps.
        public static ExplosiveJetSettings Chain() => new ExplosiveJetSettings
        {
            w = 296, h = 168, nozzleX = 0.5f, nozzleY = 0.64f,
            aim = new ZUIValue(-90f), reach = new ZUIValue(0.132f), spread = new ZUIValue(74f), bias = 1.05f, drag = 1.7f, buoy = 0.3f, grav = 0.05f,
            r0 = new ZUIValue(3.2f), growth = 0.14f, elong = new ZUIValue(2.2f), slots = 620, life = 0.28f, jitter = new ZUIValue(0.48f), strength = new ZUIValue(0.58f),
            cool = new ZUIValue(1.15f), soot = new ZUIValue(0.72f), rootR = new ZUIValue(3.4f), rootAmp = new ZUIValue(1.9f), shed = new ZUIValue(0.16f), shedKick = new ZUIValue(1.1f), shedLife = 1.1f,
            sparks = 27, sparkR = new ZUIValue(1.35f), warp1 = new ZUIValue(4.6f), warpCell = 9.5f, blastSpan = 0.085f, blastFront = 0.55f, velSpread = 0.7f,
            swell = 0.075f, hold = 1.9f, shrink = 0.88f, shrinkAt = 0.36f, leadDie = 0.54f, opaq = 0.52f, shedSwell = 0.95f,
            lo = new ZUIValue(0.2f), hi = new ZUIValue(7.434f), curve = new ZUIValue(0.461f), soft = new ZUIValue(0.2f),
            blasts = new List<ExplosiveBlast> { B(0f, 1.15f, 0.4f, -0.245f, 0.03f), B(0.3f, 0.72f, 0.26f, 0.02f, -0.03f), B(0.55f, 0.95f, 0.34f, 0.245f, 0.01f) },
            fracture = new ExplosiveFracture { chance = 0.6f, pieces = 4, at = 0.32f, open = 0.82f, kick = 0.46f, spin = 11f, rot = 0.33f, drift = 0.075f, stagger = 0.22f, cut = 0.045f, body = 0.62f },
            fracture2 = new ExplosiveFracture2 { chance = 0.5f, at = 0.58f, open = 0.55f, cut = 0.022f, kick = 0.2f, drift = 0.04f, spin = 8f },
            flash = new ExplosiveFlash { radius = 7f, amp = 2.8f, life = 0.075f, grow = 1.9f },
            chunks = new ExplosiveChunks { count = 21, radius = 2.2f, reach = 0.9f, life = 1.45f, amp = 1.4f, wide = 1.2f },
            gobs = new ExplosiveGobs { count = 18, radius = 4f, life = 1.55f, amp = 1.2f, swell = 1.5f, sag = 0.22f, wide = 1.25f, early = 0.5f },
            dust = new ExplosiveDust { count = 62, radius = 1.8f, to = 0.88f, where = 0.94f, reach = 0.34f, drag = 1.9f, life = 0.95f, amp = 1.1f, sag = 0.22f, wide = 1.2f },
            ramp = PyreRampPresets.JetDirty(), sootRamp = PyreRampPresets.JetDirtySoot(), sootLo = new ZUIValue(0.30f), sootHi = new ZUIValue(0.95f),
        };

        /// #004 `frag`: FIVE blasts at five seats, the short life and the most chunks (36, trail 4); fracture 0.9 with the hardest kick; GOLD crossfading into heat. Seed 269, 184 × 152, 30 frames @ 16 fps.
        public static ExplosiveJetSettings Frag() => new ExplosiveJetSettings
        {
            w = 184, h = 152, nozzleX = 0.495f, nozzleY = 0.535f,
            aim = new ZUIValue(0f), reach = new ZUIValue(0.158f), spread = new ZUIValue(180f), bias = 1f, drag = 2f, buoy = 0.16f, grav = 0.02f,
            r0 = new ZUIValue(2.8f), growth = 0.12f, elong = new ZUIValue(2.6f), roundAt = 0.34f, slots = 420, life = 0.22f, jitter = new ZUIValue(0.5f),
            strength = new ZUIValue(0.58f), cool = new ZUIValue(1.3f), soot = new ZUIValue(0.68f), rootR = new ZUIValue(2.6f), rootAmp = new ZUIValue(1.7f), shed = new ZUIValue(0.18f), shedKick = new ZUIValue(1.15f),
            shedLife = 1.1f, sparks = 34, sparkR = new ZUIValue(1.25f), warp0 = new ZUIValue(0.5f), warp1 = new ZUIValue(3.4f), warpCell = 8f, blastSpan = 0.06f,
            blastSkew = 2.8f, blastFront = 0.8f, velSpread = 0.7f, hold = 1.7f, shrink = 0.86f, shrinkAt = 0.36f, leadDie = 0.4f,
            opaq = 0.56f, shedSwell = 1.1f, lo = new ZUIValue(0.2f), hi = new ZUIValue(3.316f), curve = new ZUIValue(0.449f), soft = new ZUIValue(0.16f),
            blasts = new List<ExplosiveBlast> { B(0f, 1.05f, 0.28f, -0.05f, 0.04f), B(0.17f, 0.55f, 0.14f, 0.18f, -0.1f), B(0.41f, 0.88f, 0.24f, 0.06f, 0.08f), B(0.52f, 0.42f, 0.12f, -0.2f, -0.06f), B(0.78f, 0.74f, 0.22f, 0.13f, 0.02f) },
            fracture = new ExplosiveFracture { chance = 0.9f, pieces = 3, at = 0.26f, open = 1.02f, kick = 0.68f, spin = 16f, grip = 0.92f, rot = 0.46f, drift = 0.215f, stagger = 0.2f, cut = 0.13f, body = 0.9f },
            fracture2 = new ExplosiveFracture2 { chance = 0.85f, at = 0.48f, open = 0.8f, cut = 0.075f, kick = 0.34f, drift = 0.09f, spin = 13f },
            flash = new ExplosiveFlash { radius = 6f, amp = 3.2f, life = 0.055f },
            chunks = new ExplosiveChunks { count = 36, radius = 2.6f, reach = 1.35f, drag = 0.55f, life = 2.1f, sag = 0.4f, trail = 4, amp = 2.2f, wide = 1f },
            gobs = new ExplosiveGobs { count = 26, radius = 3.5f, reach = 0.9f, life = 1.65f, amp = 1.35f, swell = 1.65f, sag = 0.18f, early = 0.5f },
            dust = new ExplosiveDust { count = 120, radius = 1.7f, from = 0.22f, to = 0.9f, where = 0.92f, reach = 0.55f, drag = 1.4f, life = 1.2f, amp = 1.3f, sag = 0.2f, scatter = 0.6f },
            ramp = PyreRampPresets.JetGold(), sootRamp = PyreRampPresets.JetGoldHeat(), sootLo = new ZUIValue(0.20f), sootHi = new ZUIValue(0.85f),
        };

        /// #005 `fuelair`: the slow one — the longest span and the softest skew, no chunks, no gobs, no second crack; GHOST, the lowest ceilings. Seed 257, 176 × 200, 32 frames @ 12 fps.
        public static ExplosiveJetSettings Fuelair() => new ExplosiveJetSettings
        {
            w = 176, h = 200, nozzleX = 0.5f, nozzleY = 0.62f,
            aim = new ZUIValue(0f), reach = new ZUIValue(0.2f), spread = new ZUIValue(180f), bias = 1f, drag = 2.1f, buoy = 0.34f, r0 = new ZUIValue(3.6f),
            growth = 0.175f, elong = new ZUIValue(1.6f), roundAt = 0.28f, slots = 440, life = 0.6f, jitter = new ZUIValue(0.44f), strength = new ZUIValue(0.46f),
            cool = new ZUIValue(1.15f), soot = new ZUIValue(0f), rootR = new ZUIValue(3f), rootAmp = new ZUIValue(1.3f), shed = new ZUIValue(0.16f), shedKick = new ZUIValue(1.15f), shedLife = 1.2f,
            sparks = 6, sparkR = new ZUIValue(1.15f), warp0 = new ZUIValue(0.4f), warp1 = new ZUIValue(5.4f), warpCell = 11.5f, blastSpan = 0.2f, blastSkew = 1.6f,
            blastFront = 0.3f, velSpread = 0.85f, swell = 0.09f, hold = 1.2f, shrink = 0.72f, shrinkAt = 0.55f, leadDie = 0.45f,
            opaq = 0.6f, lo = new ZUIValue(0.2f), hi = new ZUIValue(6.702f), curve = new ZUIValue(0.44f), soft = new ZUIValue(0.26f),
            blasts = new List<ExplosiveBlast> { B(0f) },
            fracture = new ExplosiveFracture { chance = 1f, pieces = 3, at = 0.58f, open = 0.62f, spin = 4f, grip = 0.72f, rot = 0.17f, drift = 0.075f, stagger = 0.24f, cut = 0.075f, body = 0.45f },
            flash = new ExplosiveFlash { radius = 9f, amp = 1.9f, life = 0.14f, grow = 2.6f },
            dust = new ExplosiveDust { count = 52, radius = 2.2f, from = 0.38f, to = 0.92f, where = 0.98f, reach = 0.26f, drag = 2.3f, amp = 1f, scatter = 0.6f },
            ramp = PyreRampPresets.JetGhost(),
        };

        /// #006 `lash`: the VORTEX — swirl 96° and warp_spin 1 over four lobes, two blasts; WHIRL. Seed 283, 184 × 184, 32 frames @ 15 fps.
        public static ExplosiveJetSettings Lash() => new ExplosiveJetSettings
        {
            w = 184, h = 184, nozzleX = 0.5f, nozzleY = 0.512f,
            aim = new ZUIValue(0f), reach = new ZUIValue(0.234f), spread = new ZUIValue(180f), bias = 1f, drag = 1.5f, buoy = 0.15f, r0 = new ZUIValue(3f),
            growth = 0.118f, elong = new ZUIValue(3.2f), roundAt = 0.44f, slots = 520, life = 0.32f, jitter = new ZUIValue(0.46f), strength = new ZUIValue(0.58f),
            cool = new ZUIValue(1.25f), soot = new ZUIValue(0f), swirl = 96f, lobes = 4, lobeDepth = 0.52f, lobeKick = 0.3f, rootR = new ZUIValue(3.8f),
            rootAmp = new ZUIValue(1.9f), shed = new ZUIValue(0.1f), shedKick = new ZUIValue(1.1f), shedLife = 1f, sparks = 18, sparkR = new ZUIValue(1.25f), warp0 = new ZUIValue(0.4f),
            warp1 = new ZUIValue(3.4f), warpSpin = 1, blastSkew = 2.3f, blastFront = 0.65f, velSpread = 0.72f, hold = 1.6f, shrink = 0.82f,
            shrinkAt = 0.4f, leadDie = 0.4f, opaq = 0.56f, shedSwell = 0.9f, lo = new ZUIValue(0.2f), hi = new ZUIValue(4.924f), curve = new ZUIValue(0.492f),
            soft = new ZUIValue(0.17f),
            blasts = new List<ExplosiveBlast> { B(0f, 1.1f, 0.58f, -0.05f, 0.03f), B(0.52f, 0.78f, 0.42f, 0.05f, -0.03f) },
            fracture = new ExplosiveFracture { chance = 0.55f, pieces = 5, at = 0.36f, open = 0.74f, kick = 0.4f, spin = 12f, rot = 0.13f, drift = 0.135f, stagger = 0.22f, cut = 0.08f, body = 0.6f },
            fracture2 = new ExplosiveFracture2 { chance = 0.55f, at = 0.6f, open = 0.55f, cut = 0.035f, kick = 0.2f, drift = 0.05f, spin = 10f },
            flash = new ExplosiveFlash { radius = 8.5f, life = 0.08f, grow = 1.8f },
            chunks = new ExplosiveChunks { count = 13, radius = 2.2f, reach = 1f, sag = 0.2f, amp = 1.4f },
            gobs = new ExplosiveGobs { count = 12, radius = 4.4f, reach = 0.8f, swell = 1.55f },
            dust = new ExplosiveDust { count = 46, from = 0.34f, to = 0.86f, where = 0.96f, reach = 0.36f, drag = 1.7f, life = 0.62f, amp = 1.15f },
            ramp = PyreRampPresets.JetWhirl(),
        };

        /// #007 `muzzle`: a DIRECTIONAL flash — a 26° cone from the left, four blasts in quick succession, the flash stretched 2.6× along the aim; CORDITE crossfading into burnt orange. Seed 223, 232 × 156, 28 frames @ 18 fps.
        public static ExplosiveJetSettings Muzzle() => new ExplosiveJetSettings
        {
            w = 232, h = 156, nozzleX = 0.15f, nozzleY = 0.52f,
            aim = new ZUIValue(0f), reach = new ZUIValue(0.235f), spread = new ZUIValue(26f), bias = 1.4f, drag = 2.3f, buoy = 0.1f, r0 = new ZUIValue(2.6f),
            growth = 0.09f, elong = new ZUIValue(4f), roundAt = 0.36f, slots = 480, life = 0.18f, jitter = new ZUIValue(0.44f), strength = new ZUIValue(0.62f),
            cool = new ZUIValue(1.35f), soot = new ZUIValue(0.55f), rootR = new ZUIValue(3f), rootAmp = new ZUIValue(2.2f), shed = new ZUIValue(0.12f), shedKick = new ZUIValue(1.2f), shedLife = 1.1f,
            sparks = 27, sparkR = new ZUIValue(1.25f), warp0 = new ZUIValue(0.4f), warp1 = new ZUIValue(4.2f), warpCell = 8f, blastSpan = 0.035f, blastSkew = 2.2f,
            blastFront = 0.75f, velSpread = 0.62f, hold = 1.3f, shrink = 0.86f, shrinkAt = 0.34f, leadDie = 0.3f, opaq = 0.6f,
            lo = new ZUIValue(0.2f), hi = new ZUIValue(4.727f), curve = new ZUIValue(0.525f), soft = new ZUIValue(0.16f),
            blasts = new List<ExplosiveBlast> { B(0f, 1.1f, 0.3f), B(0.29f, 0.92f, 0.25f), B(0.53f, 0.7f, 0.2f), B(0.78f, 0.86f, 0.25f) },
            fracture = new ExplosiveFracture { chance = 0.5f, pieces = 3, at = 0.32f, open = 0.8f, kick = 0.42f, spin = 7f, drift = 0.075f, stagger = 0.18f, cut = 0.035f, body = 0.5f },
            fracture2 = new ExplosiveFracture2 { chance = 0.45f, at = 0.6f, open = 0.5f, cut = 0.018f, kick = 0.16f, drift = 0.04f, spin = 6f },
            flash = new ExplosiveFlash { radius = 7.5f, amp = 3.2f, life = 0.045f, grow = 1.5f, elong = 2.6f },
            chunks = new ExplosiveChunks { count = 24, radius = 1.9f, reach = 0.85f, life = 1.9f, sag = 0.16f, trail = 4 },
            dust = new ExplosiveDust { count = 38, radius = 1.5f, from = 0.25f, to = 0.8f, where = 0.92f, reach = 0.3f, drag = 1.9f, life = 0.85f, scatter = 0.55f, wide = 1.35f },
            ramp = PyreRampPresets.JetCordite(), sootRamp = PyreRampPresets.JetCorditeSoot(), sootLo = new ZUIValue(0.34f), sootHi = new ZUIValue(0.96f),
        };

        /// #008 `shatter`: the BREAK — the biggest frame, fracture certain into 4 pieces with the widest cut and the likeliest second crack; SOLAR, the hottest ramp. Seed 211, 216 × 216, 30 frames @ 15 fps.
        public static ExplosiveJetSettings Shatter() => new ExplosiveJetSettings
        {
            w = 216, h = 216, nozzleX = 0.485f, nozzleY = 0.5f,
            aim = new ZUIValue(0f), reach = new ZUIValue(0.222f), spread = new ZUIValue(180f), bias = 1.02f, drag = 1.6f, buoy = 0.17f, r0 = new ZUIValue(3.1f),
            growth = 0.135f, roundAt = 0.32f, slots = 600, life = 0.54f, jitter = new ZUIValue(0.46f), strength = new ZUIValue(0.58f), cool = new ZUIValue(1.3f),
            soot = new ZUIValue(0f), rootR = new ZUIValue(3.6f), rootAmp = new ZUIValue(1.8f), shed = new ZUIValue(0.07f), shedKick = new ZUIValue(1.1f), shedLife = 1f, sparks = 20,
            sparkR = new ZUIValue(1.25f), warp0 = new ZUIValue(0.5f), warp1 = new ZUIValue(4f), warpCell = 8.5f, blastSkew = 2.5f, blastFront = 0.65f, velSpread = 0.76f,
            swell = 0.1f, hold = 1.65f, shrink = 0.76f, shrinkAt = 0.46f, leadDie = 0.34f, opaq = 0.57f, shedSwell = 0.85f,
            lo = new ZUIValue(0.2f), hi = new ZUIValue(7f), curve = new ZUIValue(0.573f), soft = new ZUIValue(0.18f),
            blasts = new List<ExplosiveBlast> { B(0f) },
            fracture = new ExplosiveFracture { chance = 1f, pieces = 4, at = 0.22f, open = 0.95f, kick = 0.5f, spin = 13f, grip = 0.95f, rot = 0.21f, drift = 0.22f, stagger = 0.2f, cut = 0.16f, body = 0.88f },
            fracture2 = new ExplosiveFracture2 { chance = 0.65f, at = 0.52f, open = 0.75f, cut = 0.07f, kick = 0.26f, drift = 0.07f, spin = 9f, stagger = 0.16f },
            flash = new ExplosiveFlash { radius = 8.5f, life = 0.085f, grow = 1.8f },
            chunks = new ExplosiveChunks { count = 14, radius = 2.3f, reach = 1.05f, life = 1.45f, sag = 0.24f },
            gobs = new ExplosiveGobs { count = 10, radius = 4.2f, reach = 0.82f, life = 1.1f, swell = 1.45f, early = 0.4f },
            dust = new ExplosiveDust { count = 96, radius = 2.1f, to = 0.86f, where = 0.96f, reach = 0.42f, drag = 1.6f, life = 0.6f, amp = 1.25f },
            ramp = PyreRampPresets.JetSolar(),
        };

        /// #009 `shockfront`: two blasts from the left edge with the only RING: two flat 44° fronts breaking on the same seams as the gas; no lead_die; VIOLET. Seed 233, 232 × 208, 30 frames @ 14 fps.
        public static ExplosiveJetSettings Shockfront() => new ExplosiveJetSettings
        {
            w = 232, h = 208, nozzleX = 0.12f, nozzleY = 0.55f,
            aim = new ZUIValue(-3f), reach = new ZUIValue(0.415f), spread = new ZUIValue(50f), bias = 1.05f, drag = 1.35f, buoy = 0.13f, r0 = new ZUIValue(3f),
            growth = 0.105f, elong = new ZUIValue(2.6f), roundAt = 0.4f, slots = 520, life = 0.38f, jitter = new ZUIValue(0.34f), strength = new ZUIValue(0.55f),
            cool = new ZUIValue(1.25f), soot = new ZUIValue(0f), rootR = new ZUIValue(3.2f), rootAmp = new ZUIValue(1.6f), shed = new ZUIValue(0.08f), shedKick = new ZUIValue(1.1f), shedLife = 1.05f,
            sparks = 14, sparkR = new ZUIValue(1.2f), ringN = 2, ringK = 20, ringR0 = new ZUIValue(3.2f), ringGrow = 0.34f, ringLife = 1.1f,
            ringReach = new ZUIValue(1f), ringAmp = new ZUIValue(1.25f), ringFlat = true, ringArc = 44f, warp0 = new ZUIValue(0.4f), warp1 = new ZUIValue(3.6f), warpCell = 10f,
            blastSpan = 0.035f, blastSkew = 1f, blastFront = 0.2f, velSpread = 0.45f, hold = 1.6f, shrink = 0.76f, shrinkAt = 0.46f,
            opaq = 0.58f, shedSwell = 1f, lo = new ZUIValue(0.2f), hi = new ZUIValue(8.54f), curve = new ZUIValue(0.436f), soft = new ZUIValue(0.2f),
            blasts = new List<ExplosiveBlast> { B(0f, 1.1f, 0.6f), B(0.55f, 0.68f, 0.4f) },
            fracture = new ExplosiveFracture { chance = 1f, pieces = 4, at = 0.46f, open = 0.7f, kick = 0.36f, drift = 0.1f, stagger = 0.12f, cut = 0.075f, body = 0.6f },
            fracture2 = new ExplosiveFracture2 { chance = 0.5f, at = 0.7f, open = 0.5f, cut = 0.03f, drift = 0.05f },
            flash = new ExplosiveFlash { radius = 8f, amp = 2.6f, life = 0.075f, grow = 2.2f, elong = 1.4f },
            chunks = new ExplosiveChunks { count = 12, radius = 2.1f, reach = 1.05f, life = 1.3f, sag = 0.24f, amp = 1.3f, wide = 1.1f },
            gobs = new ExplosiveGobs { count = 8, radius = 4.6f, reach = 0.7f, life = 1.2f, amp = 1.2f, swell = 1.7f, wide = 1.3f, early = 0.55f },
            dust = new ExplosiveDust { count = 56, from = 0.44f, to = 0.9f, where = 0.98f, reach = 0.28f, drag = 1.8f, life = 0.62f, amp = 1.15f, wide = 1.25f },
            ramp = PyreRampPresets.JetWyrm(),
        };

        /// #010 `starshell`: seven LOBES in three blasts, the chemical one; TOXIC. Seed 293, 208 × 208, 30 frames @ 17 fps.
        public static ExplosiveJetSettings Starshell() => new ExplosiveJetSettings
        {
            w = 208, h = 208, nozzleX = 0.507f, nozzleY = 0.5f,
            aim = new ZUIValue(0f), reach = new ZUIValue(0.222f), spread = new ZUIValue(180f), bias = 1f, drag = 1.25f, buoy = 0.12f, r0 = new ZUIValue(2.9f),
            growth = 0.108f, elong = new ZUIValue(3f), roundAt = 0.44f, slots = 560, life = 0.28f, jitter = new ZUIValue(0.44f), strength = new ZUIValue(0.58f),
            cool = new ZUIValue(1.3f), soot = new ZUIValue(0f), lobes = 7, lobeDepth = 0.72f, lobeKick = 0.44f, rootR = new ZUIValue(4f), rootAmp = new ZUIValue(2.1f),
            shed = new ZUIValue(0.1f), shedKick = new ZUIValue(1.25f), shedLife = 1f, sparks = 22, sparkR = new ZUIValue(1.2f), warp0 = new ZUIValue(0.4f), warp1 = new ZUIValue(3.8f),
            warpCell = 8.5f, blastSpan = 0.055f, blastSkew = 2.7f, blastFront = 0.85f, velSpread = 0.66f, hold = 1.3f, shrink = 0.82f,
            shrinkAt = 0.42f, leadDie = 0.48f, opaq = 0.58f, lo = new ZUIValue(0.2f), hi = new ZUIValue(3.157f), curve = new ZUIValue(0.553f), soft = new ZUIValue(0.15f),
            blasts = new List<ExplosiveBlast> { B(0f, 1.12f, 0.46f), B(0.4f, 0.62f, 0.24f), B(0.68f, 0.85f, 0.3f) },
            fracture = new ExplosiveFracture { chance = 0.75f, pieces = 5, at = 0.4f, open = 0.52f, kick = 0.34f, spin = 10f, rot = 0.29f, drift = 0.12f, stagger = 0.18f, cut = 0.048f, body = 0.5f },
            fracture2 = new ExplosiveFracture2 { chance = 0.45f, at = 0.66f, open = 0.5f, cut = 0.026f, drift = 0.04f, spin = 9f },
            flash = new ExplosiveFlash { radius = 8f, amp = 3.4f, life = 0.065f, grow = 1.9f },
            chunks = new ExplosiveChunks { count = 16, radius = 2f, reach = 1.25f, life = 1.9f, sag = 0.18f, amp = 1.4f },
            dust = new ExplosiveDust { count = 48, radius = 1.6f, from = 0.34f, to = 0.88f, where = 0.95f, reach = 0.4f, drag = 1.6f, life = 0.75f, amp = 1.2f },
            ramp = PyreRampPresets.JetToxic(),
        };

    }
}
