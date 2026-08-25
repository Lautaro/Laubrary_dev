using System.Collections.Generic;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre
{
    // ── Inferno form — a STATELESS volumetric fireball explosion (closed-form f(t)) ──────────────────────────────
    // A faithful port of the "Contained Fireball Explosion Lab" HTML prototype (a known-good look): a per-pixel
    // volumetric density/heat/smoke field built from a torn angular silhouette + metaball clumps, warped by a
    // seeded directional-sine noise pack, shaded by a density-gradient pseudo-normal light, with an ignition
    // flash and contained embers. The prototype's torn shock-front RING pass is deliberately NOT ported (looked
    // bad — dropped on request). Unlike Fire/Fireball this is NOT a sim: every frame is a pure function of
    // (params, life, seed) — events and embers are rebuilt deterministically per frame — so scrubbing is exact
    // with no replay harness, no CWT cache, no content hash.
    //
    // Beyond the prototype:
    //   • SWARM-NATIVE PLACEMENT — the prototype's waves/cluster modes are GONE; the layer's swarm IS the
    //     arrangement engine. Swarm off ⇒ one centred blast; swarm on ⇒ every spawn (position + spawn moment,
    //     via ComputeSpawns) ignites one blast, so Count / Circle / Polygon / Path / Custom shapes / the
    //     spawn-timing envelope author waves, rings, carpets and anything else natively (see MakeEvents).
    //   • SMOKE THAT STAYS — Linger holds the soot to the end of the timeline instead of fading it out, and
    //     AFTERGLOW adds late residual core heat (added AFTER the hot-life chain so cooling can't kill it):
    //     the low heat values sample the ramp's dark end — the dark red-orange glow smouldering in the smoke
    //     after a punchy blast.
    //   • ENVELOPES — the animatable dials arrive pre-evaluated in an Anim snapshot (InfernoForm.Prepare runs
    //     them through the renderer's Eval funnel at the layer's life), so Blast size / Fire /
    //     Balance / Smoke / Churn / Rotation / Lift / Hollow / Flash are ZUIValue envelopes like every other
    //     Pyre form — and PROGRESS is the crown piece: it REMAPS life onto the explosion's internal time
    //     (identity curve = real time; reshape to hold at full bloom, slow the tail, or freeze a pose), which
    //     only works because every frame is closed-form.
    //   • CHARACTER — each sub-blast gets its OWN fire↔smoke balance: Balance drift slides it across the
    //     sequence (first blasts fiery → later ones sooty, or the reverse) and Balance jitter randomises it
    //     per blast. Folded into per-event fireMul/smokeMul at event build, so the field pass needs no extra work.
    //   • HOLLOW — a controllable radial cavity: density is carved inside the hollow edge, clump lobes are pushed
    //     onto the shell, and Hollow rim concentrates heat on the inner boundary so it reads as a burning shell.
    //   • PUNCH — opt-in violence: the bang overshoots its radius and settles back, ignition spikes the heat
    //     white-hot, and the flash blows out bigger; Bang speed's ceiling is also faster than the prototype's.
    //   • BODY — how OPAQUE the thick of the cloud reads: the prototype's alpha never saturates (everything
    //     stays gauzy); Body pushes dense, material-rich pixels to solid.
    //
    // Colour comes ONLY from the layer's shapeFill — evaluated through the FILL (mode + Adjust apply, so a Solid
    // fill gives a single-colour flame and the Adjust knobs retune the ramp live) at position (1 − heat): the
    // fill's LEFT end is the white-hot core, its RIGHT end the coolest flame — the direction of the default
    // OverLife fire fill. The ignition flash is tinted from the ramp's own hot end (a blue fill gives a blue-white
    // flash). Smoke grey is derived from the Darkness dial alone and never tints burning pixels (the fire colour
    // takes over hard as heat rises). Overall opacity = the shared `alpha` envelope (one caller-applied multiplier).
    //
    // The noise/hash functions are LOCAL ports of the prototype's (imul-based lattice hash, directional sine
    // pack, looping value noise) rather than PyreField.Noise01, deliberately: the look being ported depends
    // on this exact noise character, and substituting the house noise would change it. They are seeded, pure and
    // Time/UnityEngine.Random-free like everything else in the renderer (the determinism contract).
    //
    // Buffer convention: row 0 = BOTTOM (y-up) like the rest of PyreRenderer; all internal math runs in
    // y-up NDC so Lift raises the cloud and the pseudo-normal light falls from visually above. The base field
    // pass SETS pixels (it owns the whole silhouette), so the caller MUST hand this an isolated transparent
    // scratch — which the renderer always does for a PyreForm layer.
    internal static class PyreInferno
    {
        const float TAU = Mathf.PI * 2f;

        // The animatable dials, pre-evaluated by InfernoForm.Prepare through the renderer's Eval funnel at the
        // layer's life — one snapshot per frame, so an envelope/MinMax/Steps value behaves exactly like
        // it does on every other Pyre form. `progress` is the time remap: the explosion's internal t.
        public struct Anim
        {
            public float progress, blastSize, flash, churn, rotation, fire, smoke, hollow;
            public float coreGlow, billow, jagged, cohesion, clumpSpread, darkness, body;
            public float hollowRim, outerRim, coreDensity, smokeSpread, edgeSoft;
        }

        // One swarm-authored blast origin: position in y-up NDC + its spawn moment on the timeline. Built by
        // InfernoForm from the renderer's swarm instances, so placement is exactly the swarm's own.
        public struct SwarmOrigin { public float x, y, start; }

        // The layer's ALREADY-PREPARED geometry/pixel modifiers (BuildMods ran Prepare this frame): geometry
        // inverse-warps each FIELD sample point (canvas-pixel space, the Fuse-pass precedent), pixel modifiers
        // recolour/drop each lit pixel in the shading pass. Post modifiers never come through here — the caller's
        // ApplyLayerPost already shapes the finished scratch. The flash and ember passes stay deliberately
        // UNWARPED: InverseWarp is an inverse map (it cannot forward-transport the stamped points), and a
        // few-frame glow mismatch under a warp is invisible next to warping the cloud itself.
        public struct Mods
        {
            public GeometryModifier[] geo;
            public PixelModifier[] pix;
            public float phase;      // the renderer's per-frame wobble phase (modifier convention)
            public int frameIndex;
            public float life;       // the LAYER's life (modifier convention — not the remapped internal t)
            public int pixHash;      // whole-layer pixel-modifier hash stream
        }

        // The plain (non-animatable) structural dials, handed in by the caller already authored — the per-blast
        // character, the event-time shapes (bang / recoil / pulse) and the finish/containment knobs. Clamped into
        // the algorithm's own ranges in ReadParams.
        public struct Structural
        {
            public float mutation, accumulation, balanceDrift, balanceJitter;
            public float bangSpeed, punch, recoil, flashReach, flashSoft;
            public int clumps;
            public float pulse, heatPockets, cooling, linger, dieOut;
            public float embers, lighting, contrast, margin, frameFade;
        }

        // ── the layer's Inferno dials, snapshotted once per frame ────────────────────────────────────────────
        struct P
        {
            public float mutation, accumulation, charDrift, charJitter;
            public float blastSize, bangSpeed, punch, flash, recoil, flashReach, flashSoft;
            public int clumps;
            public float clumpSpread, billow, jagged, cohesion, hollow, hollowRim, outerRim, coreDensity;
            public float churn, rotation, pulse;
            public float fire, heatPockets, cooling, smoke, smokeSpread, linger, dieOut, coreGlow;
            public float darkness, body, embers;
            public float lighting, contrast, margin, edgeSoft, frameFade;
            public float hollowEdge;   // derived: the cavity edge in r/occupiedRadius units
        }

        static P ReadParams(in Structural l, in Anim a)
        {
            var p = new P
            {
                mutation = Mathf.Clamp01(l.mutation),
                accumulation = Mathf.Clamp01(l.accumulation),
                charDrift = Mathf.Clamp(l.balanceDrift, -1f, 1f),
                charJitter = Mathf.Clamp01(l.balanceJitter),
                blastSize = Mathf.Clamp(a.blastSize, 0.05f, 1f),
                bangSpeed = Mathf.Clamp(l.bangSpeed, 0.2f, 1f),
                punch = Mathf.Clamp01(l.punch),
                flash = Mathf.Clamp01(a.flash),
                recoil = Mathf.Clamp01(l.recoil),
                flashReach = Mathf.Clamp01(l.flashReach),
                flashSoft = Mathf.Clamp01(l.flashSoft),
                clumps = Mathf.Clamp(l.clumps, 1, 9),
                clumpSpread = Mathf.Clamp01(a.clumpSpread),
                // Billow's authored 0..1 maps to 0..1.5 internally: the old ceiling (a flat, rock-like cloud
                // below it) now sits around 0.67 on the slider, leaving real headroom above it.
                billow = Mathf.Clamp(a.billow, 0f, 1f) * 1.5f,
                jagged = Mathf.Clamp01(a.jagged),
                cohesion = Mathf.Clamp01(a.cohesion),
                hollow = Mathf.Clamp01(a.hollow),
                hollowRim = Mathf.Clamp01(a.hollowRim),
                outerRim = Mathf.Clamp01(a.outerRim),
                coreDensity = Mathf.Clamp01(a.coreDensity),
                churn = Mathf.Clamp01(a.churn),
                rotation = Mathf.Clamp(a.rotation, -1f, 1f),
                pulse = Mathf.Clamp01(l.pulse),
                fire = Mathf.Clamp01(a.fire),
                heatPockets = Mathf.Clamp01(l.heatPockets),
                cooling = Mathf.Clamp01(l.cooling),
                smoke = Mathf.Clamp01(a.smoke),
                smokeSpread = Mathf.Clamp01(a.smokeSpread),
                linger = Mathf.Clamp01(l.linger),
                dieOut = Mathf.Clamp01(l.dieOut),
                coreGlow = Mathf.Clamp01(a.coreGlow),
                darkness = Mathf.Clamp01(a.darkness),
                body = Mathf.Clamp01(a.body),
                embers = Mathf.Clamp01(l.embers),
                lighting = Mathf.Clamp01(l.lighting),
                contrast = Mathf.Clamp01(l.contrast),
                margin = Mathf.Clamp(l.margin, 0f, 0.25f),
                edgeSoft = Mathf.Clamp01(a.edgeSoft),
                frameFade = Mathf.Clamp01(l.frameFade),
            };
            p.hollowEdge = p.hollow * 0.78f;
            return p;
        }

        // ── tiny math (the prototype's easings) ──────────────────────────────────────────────────────────────
        static float Smoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Smootherstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        static float Sq(float v) => v * v;

        // ── hashing / noise — a verbatim port of the prototype's imul lattice hash + noise pack ─────────────
        static float Hash2(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h ^= (int)((uint)h >> 13);
                h *= 1274126177;
                h ^= (int)((uint)h >> 16);
                return (uint)h / 4294967295f;
            }
        }

        static float SignedHash(int x, int y, int seed) => Hash2(x, y, seed) * 2f - 1f;

        static float ValueNoise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
            float a = Hash2(xi, yi, seed), b = Hash2(xi + 1, yi, seed);
            float c = Hash2(xi, yi + 1, seed), d = Hash2(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        // Loops seamlessly in t (the sample point orbits the lattice), so churn never pops at a wrap.
        static float LoopValueNoise(float x, float y, float t, int seed)
        {
            float a = TAU * t;
            return ValueNoise(x + Mathf.Cos(a) * 1.13f, y + Mathf.Sin(a) * 1.13f, seed);
        }

        // Ten seeded directional sine waves per event; WaveNoise reads three at an offset (0 / 3 / 6 are the
        // three independent triplets the prototype uses for billow / cavities / angular tearing).
        struct Wave { public float dx, dy, freq, phase, speed; }

        static Wave[] MakeNoisePack(int seed)
        {
            var pack = new Wave[10];
            for (int i = 0; i < 10; i++)
            {
                float angle = TAU * Hash2(i, 313, seed + 17);
                pack[i] = new Wave
                {
                    dx = Mathf.Cos(angle),
                    dy = Mathf.Sin(angle),
                    freq = 1.55f + Hash2(i, 331, seed + 29) * 5.2f,
                    phase = TAU * Hash2(i, 349, seed + 43),
                    speed = (Hash2(i, 367, seed + 59) > 0.5f ? 1f : -1f) * (0.28f + Hash2(i, 383, seed + 71) * 1.5f),
                };
            }
            return pack;
        }

        static float WaveNoise(float x, float y, float t, Wave[] pack, int offset)
        {
            Wave a = pack[offset], b = pack[offset + 1], c = pack[offset + 2];
            float p = TAU * t;
            return Mathf.Clamp01(0.5f
                + Mathf.Sin((x * a.dx + y * a.dy) * a.freq + a.phase + p * a.speed) * 0.24f
                + Mathf.Sin((x * b.dx + y * b.dy) * b.freq + b.phase + p * b.speed) * 0.18f
                + Mathf.Sin((x * c.dx + y * c.dy) * c.freq + c.phase + p * c.speed) * 0.11f);
        }

        // ── events — the sub-blasts a pattern ignites ────────────────────────────────────────────────────────
        struct Clump { public float angle, radial, size, phase, heat, skew; }

        sealed class Ev
        {
            public int i;
            public float start, duration, cx, cy, scale, strength, mutation;
            public int seed;
            public float fireMul, smokeMul;         // per-event fire↔smoke character (drift + jitter folded in)
            public float flashPhase1, flashPhase2;  // pre-hashed small phases (never sin(hugeSeed) — precision)
            public float angPhase;                  // phase of the clump-spread angular sine
            public Wave[] pack;
            public Clump[] clumps;
        }

        // The big coherent lobes each event's cloud is built from. Multi-blast events drop a couple of clumps so
        // a crowd of small blasts stays readable (the prototype's cluster reduction, generalised). With Hollow
        // up, lobe radii are remapped onto [hollowEdge, 1] so the lobes ride the burning shell.
        static Clump[] MakeClumps(in P p, int seed, bool multi)
        {
            int reduction = multi ? 2 : 0;
            int count = Mathf.Max(1, p.clumps - reduction);
            var arr = new Clump[count];
            for (int i = 0; i < count; i++)
            {
                float v = Hash2(i, 29, seed + 7);
                // Spread pushes the lobes further out (×1.3) and the size range is wider than the prototype's,
                // so Clumps/Clump spread visibly restructure the cloud instead of nudging its texture.
                float radial = (0.05f + 0.95f * Mathf.Sqrt(v)) * p.clumpSpread * 1.3f;
                if (p.hollow > 0f) radial = p.hollowEdge + (1f - p.hollowEdge) * radial;
                arr[i] = new Clump
                {
                    angle = TAU * Hash2(i, 11, seed),
                    radial = radial,
                    size = 0.24f + 0.58f * Hash2(i, 47, seed + 19),
                    phase = TAU * Hash2(i, 71, seed + 31),
                    heat = 0.55f + 0.55f * Hash2(i, 97, seed + 41),
                    skew = SignedHash(i, 111, seed + 57),
                };
            }
            return arr;
        }

        static List<Ev> MakeEvents(in P p, int seed, SwarmOrigin[] origins)
        {
            var events = new List<Ev>();
            var pLocal = p;   // for the local function below (in-params can't be captured)
            bool multi = origins != null && origins.Length > 1;

            void Push(int i, float q, float start, float duration, float cx, float cy, float scale,
                      float strength, float mutation)
            {
                int evSeed = seed + i * 977 + Mathf.RoundToInt((mutation + 1f) * 137f);
                // Per-event fire↔smoke character: the layer Balance, slid across the sequence by Balance drift
                // (q = 0 first blast … 1 last) and randomised by Balance jitter. A lone blast keeps plain Balance.
                // Per-blast CHARACTER: 0 = this blast takes the layer's Fire/Smoke as-is (neutral multipliers of
                // exactly 1), negative = sootier than its siblings, positive = fierier. Drift slides it across
                // the sequence, jitter randomises it. A lone blast is always neutral — its character IS the dials.
                float bal = !multi
                    ? 0f
                    : Mathf.Clamp(pLocal.charDrift * (q * 2f - 1f)
                                  + SignedHash(i, 617, seed + 29) * pLocal.charJitter, -1f, 1f);
                float fireBias = (bal + 1f) * 0.5f;
                var ev = new Ev
                {
                    i = i, start = start, duration = duration, cx = cx, cy = cy,
                    scale = scale, strength = strength, mutation = mutation, seed = evSeed,
                    fireMul = Mathf.Lerp(0.30f, 1.70f, fireBias),        // 1.0 at neutral — see the note above
                    smokeMul = Mathf.Lerp(0.30f, 1.70f, 1f - fireBias),  // 1.0 at neutral
                    flashPhase1 = TAU * Hash2(5, 701, evSeed),
                    flashPhase2 = TAU * Hash2(9, 733, evSeed),
                    angPhase = TAU * Hash2(11, 761, evSeed),
                    pack = MakeNoisePack(evSeed),
                };
                ev.clumps = MakeClumps(pLocal, evSeed, multi);
                events.Add(ev);
            }

            // Swarm OFF ⇒ one centred blast with the full strength of the dials.
            if (origins == null || origins.Length == 0)
            {
                Push(0, 0.5f, 0f, 1f, 0f, 0f, 1f, 1f, 0f);
                return events;
            }

            // Swarm ON ⇒ the swarm supplies WHERE (spawn position) and WHEN (spawn moment) per blast — its
            // Count/shape/path/spawn-timing author the whole arrangement (a ring = a circle path; waves = one
            // tight cluster staggered by spawn timing; a carpet = area placement). Blast size scales down with
            // distance from centre (room shrinks toward the safe edge) and Mutation varies the rest.
            int m = origins.Length;
            float swarmNorm = Mathf.Lerp(0.88f, 0.50f, Mathf.Clamp01((m - 2) / 8f));
            for (int i = 0; i < m; i++)
            {
                float q = m == 1 ? 0.5f : i / (m - 1f);
                float mutation = SignedHash(i, 541, seed + 47) * p.mutation;
                float ocx = origins[i].x, ocy = origins[i].y;
                float radius = Mathf.Sqrt(ocx * ocx + ocy * ocy);
                float available = Mathf.Clamp(1f - radius, 0.30f, 1f);
                float scale = m == 1
                    ? Mathf.Clamp(0.9f * available + mutation * 0.10f, 0.30f, 1f)
                    : Mathf.Clamp((0.52f + 0.22f * Hash2(i, 557, seed)) * available + mutation * 0.10f, 0.22f, 0.85f);
                float duration = Mathf.Clamp(1f - origins[i].start + 0.10f, 0.38f, 0.94f);
                float strength = swarmNorm * (0.84f + 0.20f * Hash2(i, 593, seed));
                Push(i, q, origins[i].start, duration, ocx, ocy, scale, strength, mutation);
            }
            return events;
        }

        // ── embers — short contained sparks that fade before the border ─────────────────────────────────────
        struct Ember
        {
            public int eventIndex;
            public float angle, turn, speed, delay, life, hot;
            public int size;
        }

        static Ember[] MakeEmbers(in P p, List<Ev> events)
        {
            int total = Mathf.RoundToInt(p.embers * (events.Count <= 1 ? 72 : 92));
            var arr = new Ember[total];
            for (int i = 0; i < total; i++)
            {
                int eventIndex = i % events.Count;
                int s = events[eventIndex].seed;
                arr[i] = new Ember
                {
                    eventIndex = eventIndex,
                    angle = TAU * Hash2(i, 121, s + 101),
                    turn = SignedHash(i, 137, s) * 1.65f,
                    speed = 0.28f + 0.72f * Hash2(i, 151, s + 3),
                    delay = 0.03f + 0.34f * Hash2(i, 167, s + 5),
                    life = 0.20f + 0.38f * Hash2(i, 181, s + 9),
                    size = Hash2(i, 199, s + 13) > 0.86f ? 3 : (Hash2(i, 197, s + 13) > 0.54f ? 2 : 1),
                    hot = Hash2(i, 211, s + 17),
                };
            }
            return arr;
        }

        // ── per-frame event state — everything an event contributes at one t, precomputed once per frame ────
        sealed class St
        {
            public Ev ev;
            public float local;
            public float centerX, centerY, targetRadius, occupiedRadius;
            public float ignition, smokeLife, hotLife, punchHeat;
            public float rotation, cosR, sinR, churnTime;
            public float jagged, billow, fireScale, smokeScale, strength;
            public float[] harmFreq, harmAmp, harmPhase;   // 4 silhouette harmonics
            // Clump placement, precomputed ONCE per state: none of it depends on the pixel, yet it used to be
            // recomputed inside the per-pixel loop — 5 trig + 2 smoothsteps per clump per pixel per blast, over
            // half of this form's entire transcendental cost.
            public float[] clumpX, clumpY, clumpRSq, clumpW, clumpHeat;
        }

        static List<St> BuildStates(in P p, float t, List<Ev> events, float safeRadius)
        {
            var states = new List<St>();
            foreach (var ev in events)
            {
                float lt = (t - ev.start) / ev.duration;
                if (lt < 0f || lt > 1.05f) continue;
                float local = Mathf.Clamp01(lt);
                float mutation = ev.mutation;
                float bang = Mathf.Clamp01(p.bangSpeed * (1f + mutation * 0.45f));
                // Faster ceiling than the prototype (.075 → .03): Bang speed 1 completes the expansion almost
                // instantly — with Punch it overshoots past the target and settles back, which is what makes a
                // detonation read violent instead of inflating.
                float expansionEnd = Mathf.Lerp(0.52f, 0.03f, bang);
                float expansion = Smootherstep(0f, expansionEnd, local);
                float overshoot = 1f + p.punch * 0.28f * expansion * Mathf.Exp(-Mathf.Max(0f, local - expansionEnd) * 10f);
                float recoil = 1f - p.recoil * 0.40f * Smoothstep(0.32f, 0.72f, local);
                float pulseEnvelope = Smoothstep(0.08f, 0.22f, local) * (1f - Smoothstep(0.64f, 0.92f, local));
                float pulseWave = Mathf.Sin((local - 0.10f) * TAU * 1.45f);
                float pulseScale = 1f + p.pulse * 0.30f * pulseWave * pulseEnvelope;
                // No directional bias: an explosion has no up. (The prototype's Lift drifted the cloud upward as
                // it aged — removed on request; a directional drift belongs to a modifier, not the form.)
                float centerX = ev.cx;
                float centerY = ev.cy;
                float centerLen = Mathf.Sqrt(centerX * centerX + centerY * centerY);
                float available = Mathf.Max(0.12f, safeRadius - centerLen - 0.025f);
                float targetRadius = Mathf.Min(available, safeRadius * p.blastSize * ev.scale);
                float occupiedRadius = targetRadius * (0.055f + 0.945f * expansion) * recoil * pulseScale * overshoot;
                float ignition = Smoothstep(0f, 0.035f, local);
                // Linger holds the soot: at 0 the smoke fades out over the last frames (the prototype's tail);
                // at 1 there is NO fade at all — the fade factor compounds through the density→soot→alpha chain,
                // so anything short of zero still visibly thins the final frame. Full persistence must mean full.
                float smokeFade = 0.80f * (1f - p.linger) * Smoothstep(Mathf.Lerp(0.91f, 0.80f, p.linger), 1f, local);
                float smokeLife = ignition * (1f - smokeFade);
                float coolStart = Mathf.Lerp(0.68f, 0.20f, p.cooling);
                float hotLife = ignition * (1f - Smoothstep(coolStart, 0.96f, local));
                // Die out — dissolve EVERYTHING to nothing by the last frame, over the final `dieOut` fraction of
                // the timeline, so a blast ends on its own instead of being cut off by the frame count. It rides
                // the explosion's own clock (t, not local), so a staggered swarm still ends together.
                if (p.dieOut > 0f)
                {
                    float death = Smoothstep(1f - p.dieOut, 1f, t);
                    float alive = 1f - death;
                    smokeLife *= alive;
                    hotLife *= alive;
                }
                float rotation = (p.rotation + mutation * 0.35f) * TAU * 0.58f * Smoothstep(0.025f, 1f, local);
                // NOT wrapped with % 1: WaveNoise advances its phase by TAU·t·speed with a NON-INTEGER speed, so
                // wrapping t from ~1 back to 0 jumped every wave's phase by a non-multiple of TAU — a hard
                // discontinuity that read as the whole cloud SNAPPING to a different shape in one frame
                // (whenever churn·local passed 1). Unwrapped time is continuous, and LoopValueNoise's cos/sin
                // orbit is periodic in t anyway, so nothing else changes.
                float churnTime = local * (0.08f + 1.62f * p.churn) + ev.i * 0.137f;
                float jagged = Mathf.Clamp01(p.jagged * (1f + mutation * 0.55f));
                var st = new St
                {
                    ev = ev, local = local,
                    centerX = centerX, centerY = centerY,
                    targetRadius = targetRadius, occupiedRadius = occupiedRadius,
                    ignition = ignition, smokeLife = smokeLife, hotLife = hotLife,
                    // Punch's ignition heat spike: the first instants blaze white-hot, then settle (τ ≈ 0.11 local).
                    punchHeat = 1f + p.punch * 1.6f * Mathf.Exp(-local * 9f),
                    rotation = rotation, cosR = Mathf.Cos(rotation), sinR = Mathf.Sin(rotation),
                    churnTime = churnTime, jagged = jagged,
                    billow = Mathf.Clamp01(p.billow * (1f + mutation * 0.35f)),
                    fireScale = Mathf.Clamp(1f + mutation * 0.52f, 0.40f, 1.55f),
                    smokeScale = Mathf.Clamp(1f - mutation * 0.30f, 0.55f, 1.45f),
                    strength = ev.strength,
                    harmFreq = new float[4], harmAmp = new float[4], harmPhase = new float[4],
                };
                for (int k = 0; k < 4; k++)
                {
                    st.harmFreq[k] = 2 + k;
                    st.harmAmp[k] = (0.35f / (k + 1)) * jagged * (0.72f + Hash2(k, 17, ev.seed));
                    st.harmPhase[k] = TAU * Hash2(k, 23, ev.seed + 61);
                }
                // Hoisted clump placement (see the St fields) — identical math, evaluated once per blast.
                var cl = ev.clumps;
                int nc = cl.Length;
                st.clumpX = new float[nc]; st.clumpY = new float[nc]; st.clumpRSq = new float[nc];
                st.clumpW = new float[nc]; st.clumpHeat = new float[nc];
                float radialGrow = 0.16f + 0.84f * Smoothstep(0f, 0.28f, local);
                float sizeGrow = 0.34f + 0.66f * Smoothstep(0f, 0.20f, local);
                float roll = 0.105f * p.churn * occupiedRadius;
                for (int i = 0; i < nc; i++)
                {
                    Clump c = cl[i];
                    float wob = TAU * churnTime + c.phase;
                    float wobCos = Mathf.Cos(wob), wobSin = Mathf.Sin(wob);
                    float driftAngle = c.angle + rotation + wobSin * 0.38f * p.churn;
                    float radial = occupiedRadius * c.radial * radialGrow;
                    st.clumpX[i] = Mathf.Cos(driftAngle) * radial + wobCos * roll;
                    st.clumpY[i] = Mathf.Sin(driftAngle) * radial + wobSin * roll + c.skew * roll * 0.45f;
                    float cr = Mathf.Max(0.024f, occupiedRadius * c.size * sizeGrow);
                    st.clumpRSq[i] = cr * cr;
                    st.clumpW[i] = 0.75f + 0.60f * c.size;
                    st.clumpHeat[i] = c.heat;
                }
                states.Add(st);
            }
            return states;
        }

        // Straight-alpha source-over of a glow colour onto the buffer (the prototype's compositeGlow).
        static void CompositeGlow(Color32[] buf, int i, float cr, float cg, float cb, float a)
        {
            a = Mathf.Clamp01(a);
            if (a <= 0f) return;
            float oldA = buf[i].a / 255f;
            float outA = a + oldA * (1f - a);
            if (outA <= 0f) return;
            float inv = 1f / outA;
            float keep = oldA * (1f - a);
            buf[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt((cr * a + buf[i].r * keep) * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((cg * a + buf[i].g * keep) * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((cb * a + buf[i].b * keep) * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(outA * 255f), 0, 255));
        }

        // ── the frame render — pure f(params, progress, seed); `target` MUST be an isolated transparent scratch ──
        public static void Render(Color32[] target, int W, int H, in Structural structural, int seed,
                                  ZuiFill fill, float layerAlpha, in Anim anim, SwarmOrigin[] origins, in Mods mods)
        {
            if (W < 2 || H < 2 || layerAlpha <= 0f) return;
            P p = ReadParams(structural, anim);

            var events = MakeEvents(p, seed, origins);
            if (events.Count == 0) return;
            float safeRadius = Mathf.Max(0.30f, 1f - p.margin * 2f);
            // The Progress envelope IS the explosion's clock: identity curve = real time; any other shape holds,
            // slows or freezes the whole detonation — exact at every frame because everything is closed-form.
            var states = BuildStates(p, Mathf.Clamp01(anim.progress), events, safeRadius);
            if (states.Count == 0) return;

            // Heat-ramp LUT sampled through the FILL (mode + Adjust apply — a Solid fill gives a single-colour
            // flame, the Adjust knobs retune the ramp live) at position (1 − heat), so the fill's LEFT end is the
            // white-hot core — the default OverLife fire fill's direction. Spatial fill modes sample at the shape
            // centre (a heat ramp has no meaningful u,v). 0..255 floats for the prototype's colour math.
            const int LutN = 128;
            var lutR = new float[LutN]; var lutG = new float[LutN]; var lutB = new float[LutN]; var lutA = new float[LutN];
            for (int k = 0; k < LutN; k++)
            {
                Color c = fill != null ? fill.Evaluate(1f - k / (LutN - 1f), 0.5f, 0.5f) : Color.white;
                lutR[k] = c.r * 255f; lutG[k] = c.g * 255f; lutB[k] = c.b * 255f; lutA[k] = c.a;
            }

            // ── field pass: union density / heat / smoke / detail across every active event ──────────────────
            int count = W * H;
            var density = new float[count];
            var heat = new float[count];
            var smokeF = new float[count];
            var detailF = new float[count];
            float accumulation = events.Count > 1 ? p.accumulation : 0f;

            // Geometry modifiers: inverse-warp each sample point in CANVAS-PIXEL space around the canvas centre
            // (never in NDC — per-axis NDC divisors would shear a Rotate/Skew differently than every other form),
            // then convert to y-up NDC. Containment is computed from the WARPED point, so a warp that drags the
            // cloud outward isn't hard-clipped by an unwarped circle. Mirrors the Fuse field-pass precedent.
            bool anyGeo = mods.geo != null && mods.geo.Length > 0;
            bool anyPix = mods.pix != null && mods.pix.Length > 0;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            var gctx = anyGeo ? new GeoCtx(ccx, ccy, Vector2.zero, safeRadius * Mathf.Min(W, H) * 0.5f) : default;
            // FRAME fade — purely the canvas-border guard, now separate from the blast's own rim softness (Edge
            // softness, applied per-blast in the loop): the band over which anything approaching the Safe margin
            // is taken to full transparency. It grows INWARD from the safe radius, so "nothing past Safe margin,
            // and the very frame edge is always 100% transparent" holds at every setting.
            float featherW = Mathf.Lerp(0.03f, 0.32f, p.frameFade);

            for (int py = 0; py < H; py++)
            {
                float rowYPix = (py + 0.5f) - ccy;           // row 0 = bottom ⇒ y-up already
                for (int px = 0; px < W; px++)
                {
                    float sxPix = (px + 0.5f) - ccx, syPix = rowYPix;
                    if (anyGeo)
                    {
                        var off = new Vector2(sxPix, syPix);
                        for (int gi = mods.geo.Length - 1; gi >= 0; gi--)
                            off = mods.geo[gi].InverseWarp(off, mods.phase, gctx);
                        sxPix = off.x; syPix = off.y;
                    }
                    float screenX = sxPix / ccx;
                    float screenY = syPix / ccy;
                    float screenR = Mathf.Sqrt(screenX * screenX + screenY * screenY);
                    float contain = 1f - Smoothstep(safeRadius - featherW, safeRadius, screenR);
                    if (contain <= 0f) continue;

                    float unionD = 0f, unionSmoke = 0f, heatMax = 0f, heatSum = 0f, detail = 0f;
                    for (int si = 0; si < states.Count; si++)
                    {
                        var st = states[si];
                        float occupied = st.occupiedRadius;
                        if (occupied < 0.006f) continue;
                        float x = screenX - st.centerX;
                        float y = screenY - st.centerY;
                        // CONSERVATIVE PRE-REJECT before the four noise calls below. The churn warp displaces a
                        // sample by at most 0.5·warp per axis, so anything further out than the disc bound plus
                        // that diagonal is provably rejected by the post-warp test — skipping it here is exactly
                        // equivalent, and saves the whole 10-trig + 8-hash noise prologue on every pixel that
                        // belongs to a DIFFERENT blast. With a multi-blast swarm this is the single biggest win.
                        float warp = occupied * p.churn * 0.20f;
                        float r0 = Mathf.Sqrt(x * x + y * y);          // rotation preserves length
                        if (r0 > occupied * 1.45f + 0.08f + 0.7072f * warp) continue;
                        float rx = x * st.cosR - y * st.sinR;
                        float ry = x * st.sinR + y * st.cosR;

                        var pack = st.ev.pack;
                        // Offsets are plain adds — never `% 1` (see the churnTime note: wrapping is what snapped).
                        float billowNoise = WaveNoise(rx * 2.2f, ry * 2.2f, st.churnTime, pack, 0);
                        float cavityNoise = WaveNoise(rx * 4.6f + 1.3f, ry * 4.6f - 1.8f, st.churnTime + 0.27f, pack, 3);
                        float fineA = LoopValueNoise(rx * 10.2f - 2.4f, ry * 10.2f + 3.1f, st.churnTime + 0.11f, st.ev.seed + 809);
                        float fineB = LoopValueNoise((rx + ry) * 6.4f + 1.7f, (ry - rx) * 6.4f - 2.2f, st.churnTime + 0.43f, st.ev.seed + 977);
                        float fineNoise = Mathf.Clamp01(fineA * 0.61f + fineB * 0.39f);
                        rx += (billowNoise - 0.5f) * warp;
                        ry += (cavityNoise - 0.5f) * warp;

                        float r = Mathf.Sqrt(rx * rx + ry * ry);
                        if (r > occupied * 1.45f + 0.08f) continue;
                        float angle = Mathf.Atan2(ry, rx);
                        float angular = 1f;
                        for (int k = 0; k < 4; k++)
                            angular += st.harmAmp[k] * Mathf.Sin(st.harmFreq[k] * angle + st.harmPhase[k] + st.rotation * 0.45f);
                        angular += (WaveNoise(Mathf.Cos(angle) * 1.7f, Mathf.Sin(angle) * 1.7f, st.churnTime, pack, 6) - 0.5f) * 0.55f * st.jagged;
                        angular += Mathf.Sin(angle * Mathf.Max(2, p.clumps) + st.ev.angPhase + st.rotation) * p.clumpSpread * 0.30f;
                        angular = Mathf.Clamp(angular, 0.50f, 1.52f);
                        // Edge softness is ABSOLUTE (a fraction of the canvas, not of the blast), so the rim fades
                        // over the same distance whatever size the blast is — a small blast is no longer a hard
                        // pellet and a big one no longer a stone disc. The billow term stays as a small extra.
                        float edgeSoft = 0.012f + 0.30f * p.edgeSoft + 0.03f * st.billow;
                        float baseBoundary = Mathf.Max(0.015f, occupied * angular);
                        float baseD = 1f - Smoothstep(baseBoundary - edgeSoft, baseBoundary + edgeSoft, r);

                        // Clump positions/radii are PRECOMPUTED per blast (see BuildStates) — this loop is now
                        // only the membership test, which is the only part that depends on the pixel.
                        float metaball = 0f, hotBalls = 0f;
                        var cX = st.clumpX; var cY = st.clumpY; var cRSq = st.clumpRSq;
                        for (int i = 0; i < cX.Length; i++)
                        {
                            float ddx = rx - cX[i], ddy = ry - cY[i];
                            float q = (ddx * ddx + ddy * ddy) / cRSq[i];
                            if (q < 1f)
                            {
                                float m = 1f - q;
                                metaball += m * m * st.clumpW[i];
                                hotBalls = Mathf.Max(hotBalls, m * st.clumpHeat[i]);
                            }
                        }
                        metaball = 1f - Mathf.Exp(-metaball * 1.8f);

                        float rr = r / (occupied + 0.001f);
                        // billow: bClamp for lerp POSITIONS (Unity's Lerp clamps t, so the 0..1.5 range would be
                        // silently truncated), bGain as an unclamped MULTIPLIER where the headroom must show.
                        float bClamp = Mathf.Clamp01(st.billow);
                        float bGain = st.billow;
                        float baseWeight = Mathf.Lerp(0.025f, 0.82f, p.cohesion);
                        float d = 1f - (1f - baseD * baseWeight) * (1f - metaball);
                        d *= Mathf.LerpUnclamped(0.58f, 1.62f, Mathf.LerpUnclamped(0.5f, billowNoise, bGain));
                        d *= Mathf.Lerp(0.72f, 1.32f, fineNoise);
                        // Core density: the cavity and fine-noise BITES below are strongest exactly where the
                        // cloud is thickest, which is what thinned (or outright holed) the CENTRE even with
                        // Hollow at 0. A core bias refills the middle and damps both bites there.
                        float coreBias = p.coreDensity * Mathf.Pow(Mathf.Clamp01(1f - rr), 1.35f);
                        float biteDamp = Mathf.Lerp(1f, 0.18f, coreBias);
                        float cavities = Mathf.Max(0f, cavityNoise - Mathf.Lerp(0.72f, 0.48f, bClamp))
                                       * Mathf.LerpUnclamped(0.3f, 1.25f, bGain);
                        d -= cavities * (1f - 0.62f * p.cohesion) * biteDamp;
                        d -= Smoothstep(0f, 0.20f, 0.48f - fineNoise) * 0.22f * bClamp * Smoothstep(0.15f, 0.85f, d) * biteDamp;
                        d += coreBias * 0.60f;
                        // Hollow: carve the core inside the hollow edge (soft, billow-widened boundary), leaving
                        // the burning shell. Outside the shell rr > edge saturates the step ⇒ no effect.
                        if (p.hollow > 0f)
                        {
                            float soft = 0.06f + 0.10f * bClamp;
                            d *= Smoothstep(p.hollowEdge - soft, p.hollowEdge + soft * 0.5f, rr);
                        }
                        d = Mathf.Clamp01(d * st.smokeLife * contain * st.strength);

                        // Smoke shell: the soot reaches BEYOND the fire body on its own wider, softer boundary,
                        // so heavy smoke FRAMES the fire and feathers into the background instead of stopping
                        // dead at the flame's silhouette. It carries density too, so the halo lights and
                        // composites as real cloud rather than a flat tint.
                        float dAll = d;
                        if (p.smokeSpread > 0f && p.smoke > 0f)
                        {
                            float sB = baseBoundary * (1f + 0.34f * p.smokeSpread);
                            float sS = edgeSoft * (1f + 2.6f * p.smokeSpread);
                            float shellD = (1f - Smoothstep(sB - sS, sB + sS, r))
                                         * Mathf.Lerp(0.75f, 1.15f, billowNoise) * st.smokeLife * contain * st.strength;
                            dAll = Mathf.Max(d, Mathf.Clamp01(shellD) * p.smoke * (0.30f + 0.70f * p.smokeSpread));
                        }
                        if (dAll < 0.004f) continue;

                        float inner = Mathf.Clamp01(1f - rr);
                        float hotFront = Mathf.Exp(-Sq((rr - 0.48f) / 0.27f));
                        float turbulentHeat = Mathf.Clamp01((billowNoise - 0.23f) * 1.55f) * Mathf.Clamp01((cavityNoise - 0.11f) * 1.28f);
                        float boiling = Smoothstep(0.36f, 0.72f, fineNoise) * (0.42f + 0.58f * billowNoise);
                        float h = inner * 0.46f + hotFront * 0.19f + turbulentHeat * 0.36f * p.heatPockets
                                + boiling * 0.29f * p.heatPockets + hotBalls * 0.39f * p.heatPockets;
                        // Rim heat — a hot band on the cavity's INNER boundary (needs Hollow) and/or on the
                        // cloud's OUTER rim. Both are envelopes, so a shell can ignite, burn and cool across the
                        // blast; the outer one is what makes a fireball read as a burning surface, not a lit disc.
                        if (p.hollow > 0f && p.hollowRim > 0f)
                        {
                            float rimW = 0.09f + 0.07f * p.hollowRim;
                            h += Mathf.Exp(-Sq((rr - p.hollowEdge) / rimW)) * 0.62f * p.hollowRim;
                        }
                        if (p.outerRim > 0f)
                        {
                            float rimW = 0.10f + 0.09f * p.outerRim;
                            h += Mathf.Exp(-Sq((rr - 0.90f) / rimW)) * 0.62f * p.outerRim;
                        }
                        h *= Mathf.Lerp(0.60f, 1.28f, fineNoise) * p.fire * st.hotLife * st.fireScale * st.ev.fireMul
                           * st.strength * st.punchHeat
                           // Cool toward the containment rim: without this, a pixel keeps FULL heat while its
                           // density fades, so the boundary reads as a hot-to-nothing slice along the circle.
                           * Mathf.Pow(contain, 0.6f);
                        // Core glow — an INNER GLOW added after the hot-life chain, so Cooling can't kill it. Its
                        // TIMING is entirely the envelope's: there is no baked-in ramp, so a flat value glows the
                        // whole time and a curve makes it swell, hold and die exactly when authored.
                        if (p.coreGlow > 0f)
                            h += p.coreGlow * Mathf.Pow(inner, 1.5f) * (0.55f + 0.45f * billowNoise)
                               * st.ignition * st.strength * 0.85f;

                        float shell = Smoothstep(0.18f, 0.82f, rr);
                        float ageSmoke = Smoothstep(0.10f, 0.86f, st.local);
                        float soot = dAll * p.smoke * st.smokeScale * st.ev.smokeMul
                                   * (0.18f + shell * 0.90f + ageSmoke * 0.75f + billowNoise * 0.28f);
                        soot = Mathf.Clamp01(soot);

                        unionD = 1f - (1f - unionD) * (1f - dAll);
                        unionSmoke = 1f - (1f - unionSmoke) * (1f - soot);
                        heatMax = Mathf.Max(heatMax, h);
                        heatSum += h;
                        detail += fineNoise * dAll;
                    }

                    if (unionD <= 0.003f) continue;
                    int idx = py * W + px;
                    density[idx] = Mathf.Clamp01(unionD);
                    // Heat stacking: overlapping events ADD heat (scaled by Accumulation) instead of merely
                    // taking the max — what makes overlapping waves glow hotter than either alone.
                    heat[idx] = Mathf.Clamp01(heatMax + Mathf.Max(0f, heatSum - heatMax) * accumulation * 0.62f);
                    smokeF[idx] = Mathf.Clamp01(unionSmoke);
                    detailF[idx] = Mathf.Clamp01(detail / Mathf.Max(0.001f, heatSum + unionD));
                }
            }

            // ── shading pass: pseudo-normal lighting + smoke grey ↔ fire-ramp mix → straight-alpha pixels ────
            float contrastMul = Mathf.Lerp(0.78f, 1.62f, p.contrast);
            float contrastPow = Mathf.Lerp(1.48f, 0.76f, p.contrast);
            float darkBase = Mathf.Lerp(108f, 7f, p.darkness);
            for (int py = 0; py < H; py++)
            {
                for (int px = 0; px < W; px++)
                {
                    int i = py * W + px;
                    float d = density[i];
                    if (d < 0.006f) continue;
                    float l = density[py * W + Mathf.Max(0, px - 1)], r = density[py * W + Mathf.Min(W - 1, px + 1)];
                    // "u" = the visually-UP neighbour — row py+1 in this y-up buffer — so the light falls from
                    // above exactly as in the prototype's y-down canvas (where up was row y-1).
                    float u = density[Mathf.Min(H - 1, py + 1) * W + px], dn = density[Mathf.Max(0, py - 1) * W + px];
                    float nx = (l - r) * 4.1f, ny = (u - dn) * 4.1f, nz = 0.48f;
                    float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    float directional = Mathf.Clamp01((nx * -0.46f + ny * -0.70f + nz * 0.56f) * inv * 0.5f + 0.5f);
                    float shade = Mathf.Lerp(1f, Mathf.Lerp(0.38f, 1.68f, directional), p.lighting);
                    float cavity = Mathf.Clamp01(1f - (Mathf.Abs(l - r) + Mathf.Abs(u - dn)) * 1.45f);

                    float soot = smokeF[i];
                    float detail = detailF[i];
                    float smokeR = darkBase * (0.68f + 0.42f * shade) + 24f * (1f - p.darkness);
                    float smokeG = smokeR * 0.92f;
                    float smokeB = smokeR * 0.88f + 7f;
                    float smokeTexture = Mathf.Lerp(0.57f, 1.48f, detail) * Mathf.Lerp(0.72f, 1.22f, soot);
                    smokeR *= smokeTexture; smokeG *= smokeTexture; smokeB *= smokeTexture;

                    float rawHeat = heat[i];   // per-event fire/smoke balance already folded in at the field pass
                    float hv = Mathf.Clamp01(Mathf.Pow(rawHeat, contrastPow) * contrastMul);
                    int li = Mathf.Clamp(Mathf.RoundToInt(hv * (LutN - 1)), 0, LutN - 1);
                    float fireMix = Smoothstep(0.085f, 0.72f, hv);
                    fireMix *= Mathf.Clamp01(0.30f + d * 1.10f) * Mathf.Lerp(0.72f, 1.08f, cavity);
                    fireMix = Mathf.Clamp01(fireMix * Mathf.Lerp(0.45f, 1.08f, p.fire)) * lutA[li];

                    // COLOUR decoupling: anything visibly burning takes the ramp's colour outright (a hard, early
                    // takeover), so Darkness genuinely recolours only smoke — fireMix keeps driving the fire's
                    // ALPHA/brightness contribution, but never leaves a hot pixel tinted by the smoke grey.
                    float colourMix = Mathf.Max(Smoothstep(0.04f, 0.50f, hv) * lutA[li], fireMix);

                    // Wider ceiling than the prototype (1.34 → 2.2): a high Smoke dial reaches genuinely HEAVY
                    // soot instead of topping out translucent. Smoke is now the ONE dial that sets how much
                    // soot there is (the old Balance re-weighted it a second time from a different section).
                    float visibleSmoke = Mathf.Clamp01(soot * Mathf.Lerp(0.85f, 2.2f, p.smoke));
                    float rr = Mathf.Lerp(smokeR, lutR[li], colourMix);
                    float gg = Mathf.Lerp(smokeG, lutG[li], colourMix);
                    float bb = Mathf.Lerp(smokeB, lutB[li], colourMix);
                    rr *= shade; gg *= shade; bb *= Mathf.Lerp(0.79f, 1.12f, shade);

                    float materialAlpha = Mathf.Clamp01(Mathf.Max(fireMix * 0.95f, visibleSmoke));
                    float alpha0 = Mathf.Clamp01(Mathf.Pow(d, 0.65f) * (0.025f + 0.995f * materialAlpha));
                    // Body: thick, material-rich pixels saturate toward OPAQUE — the prototype's alpha never
                    // reaches 1, which reads as ghostly gas. The old form was a Max() against a term that
                    // alpha0 usually already exceeded, so the dial barely bit; it now BLENDS toward solid and
                    // then lifts the mid-range, which is what makes a dense cloud actually read as matter.
                    float alpha = alpha0;
                    if (p.body > 0f)
                    {
                        float dense = Smoothstep(0.15f, 0.62f, d) * Mathf.Clamp01(materialAlpha * 1.9f);
                        alpha = Mathf.Lerp(alpha, Mathf.Max(alpha, dense), p.body);
                        alpha = Mathf.Min(1f, alpha * (1f + 0.55f * p.body * dense));
                    }
                    alpha *= layerAlpha;

                    // Pixel modifiers — recolour/drop each lit pixel (the renderer's ApplyPix convention: RGB in
                    // a Color with alpha 1, the real alpha rides separately; crossFrac = the heat value). A drop
                    // writes nothing — this pass SETS pixels, there is no blend to skip.
                    if (anyPix)
                    {
                        var col = new Color(rr / 255f, gg / 255f, bb / 255f, 1f);
                        var info = new PixelInfo(px, py, px + 0.5f, py + 0.5f, mods.frameIndex, hv, mods.life,
                                                 mods.pixHash, W, H);
                        bool keep = true;
                        for (int mi = 0; mi < mods.pix.Length && keep; mi++)
                            keep = mods.pix[mi].ApplyPixel(ref col, ref alpha, info);
                        if (!keep) continue;
                        rr = col.r * 255f; gg = col.g * 255f; bb = col.b * 255f;
                    }
                    target[i] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(rr), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(gg), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(bb), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
                }
            }

            // ── ignition flash — the white-hot asymmetric punch at each event's birth (bounded Gaussian).
            // The prototype's torn shock-front RING pass is deliberately omitted here (dropped on request).
            // Tinted from the RAMP'S OWN HOT END pushed toward white — a blue fill gives a blue-white flash —
            // and Punch blows it out bigger and brighter.
            float flashR = Mathf.Lerp(lutR[LutN - 1], 255f, 0.6f);
            float flashG = Mathf.Lerp(lutG[LutN - 1], 255f, 0.6f);
            float flashB = Mathf.Lerp(lutB[LutN - 1], 255f, 0.6f);
            foreach (var st in states)
            {
                float flashLife = Mathf.Exp(-st.local * Mathf.Lerp(15f, 8f, p.flash)) * p.flash * st.strength
                                * (1f + p.punch * 1.2f);
                if (flashLife <= 0.004f) continue;
                // Reach sizes the flash INDEPENDENTLY of its brightness (they used to be the same dial, so a
                // dim wide flash or a tight brilliant one were both impossible).
                float flashRadius = (0.05f + st.targetRadius * Mathf.Lerp(0.15f, 1.30f, p.flashReach))
                                  * (1f + p.punch * 0.35f);
                // Falloff exponent: high = a tight core with a hard alpha edge, low = a broad soft glow that
                // fades gradually into the cloud. (WHEN the flash fades is the Flash envelope's job.)
                float flashFall = Mathf.Lerp(5.5f, 0.65f, p.flashSoft);
                float cull = flashRadius * 1.37f * Mathf.Lerp(1.6f, 3.4f, p.flashSoft);   // asymmetry × tail cutoff
                int x0 = Mathf.Max(0, Mathf.FloorToInt((st.centerX - cull) * 0.5f * W + W * 0.5f));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt((st.centerX + cull) * 0.5f * W + W * 0.5f));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((st.centerY - cull) * 0.5f * H + H * 0.5f));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt((st.centerY + cull) * 0.5f * H + H * 0.5f));
                for (int py = y0; py <= y1; py++)
                {
                    float sy = (py + 0.5f) / H * 2f - 1f;
                    for (int px = x0; px <= x1; px++)
                    {
                        float sx = (px + 0.5f) / W * 2f - 1f;
                        // The same containment FEATHER as the cloud — the old hard `> safeRadius` reject sliced
                        // the glow along a perfect circle with zero falloff (the most visible boundary cut).
                        float fc = 1f - Smoothstep(safeRadius - featherW, safeRadius, Mathf.Sqrt(sx * sx + sy * sy));
                        if (fc <= 0f) continue;
                        float dx = sx - st.centerX, dy = sy - st.centerY;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float angle = Mathf.Atan2(dy, dx);
                        float asymmetric = 1f + 0.24f * Mathf.Sin(angle * 3f + st.ev.flashPhase1)
                                              + 0.13f * Mathf.Sin(angle * 5f - st.ev.flashPhase2);
                        float a = Mathf.Exp(-Sq(r / (flashRadius * asymmetric)) * flashFall) * flashLife * fc;
                        if (a <= 0.004f) continue;
                        CompositeGlow(target, py * W + px, flashR, flashG, flashB, a * 0.92f * layerAlpha);
                    }
                }
            }

            // ── embers — contained sparks riding each event, coloured off the heat ramp ──────────────────────
            if (p.embers > 0.01f)
            {
                var stByEvent = new St[events.Count];
                foreach (var st in states) stByEvent[st.ev.i] = st;
                var embers = MakeEmbers(p, events);
                foreach (var e in embers)
                {
                    var st = stByEvent[e.eventIndex];
                    if (st == null) continue;
                    float age = (st.local - e.delay) / e.life;
                    if (age < 0f || age > 1f) continue;
                    float ease = 1f - Mathf.Pow(1f - age, 2.25f);
                    float dist = Mathf.Min(st.targetRadius * 0.93f, st.occupiedRadius * (0.13f + 0.77f * e.speed * ease));
                    float ang = e.angle + e.turn * ease + (p.rotation + st.ev.mutation * 0.3f) * st.local * 0.8f;
                    int ex = Mathf.RoundToInt(((st.centerX + Mathf.Cos(ang) * dist) * 0.5f + 0.5f) * W - 0.5f);
                    int ey = Mathf.RoundToInt(((st.centerY + Mathf.Sin(ang) * dist) * 0.5f + 0.5f) * H - 0.5f);
                    if (ex < 1 || ey < 1 || ex >= W - 1 || ey >= H - 1) continue;
                    float emberAlpha = Mathf.Sin(Mathf.PI * age) * p.embers * st.strength * 1.28f * layerAlpha;
                    int radius = e.size;
                    int hi = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(0.58f + 0.42f * e.hot * st.hotLife) * (LutN - 1)), 0, LutN - 1);
                    for (int oy = -radius + 1; oy < radius; oy++)
                        for (int ox = -radius + 1; ox < radius; ox++)
                        {
                            float fall = 1f - Mathf.Sqrt(ox * ox + oy * oy) / Mathf.Max(1, radius);
                            if (fall <= 0f) continue;
                            CompositeGlow(target, (ey + oy) * W + (ex + ox), lutR[hi], lutG[hi], lutB[hi], emberAlpha * fall);
                        }
                }
            }
        }
    }
}
