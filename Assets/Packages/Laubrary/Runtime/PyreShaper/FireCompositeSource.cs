using System;
using Laubrary.Pyre;
using Laubrary.Shaper;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0173 — a real flame as a Shaper composite generator: Pyre's fluid fire simulation
    /// (<see cref="FireSim"/>, <c>Runtime/SpriteFx/FireSim.cs:54</c>) driven straight from Shaper's own clock.
    ///
    /// <b>Why this is a composite source and not a <c>PyreForm</c>.</b> Every other generator Shaper hosts is a
    /// <c>PyreForm</c> plug-in and arrives through <see cref="PyreFormCompositeSource"/>. Fire is not one: in Pyre
    /// it is a <c>ShapeForm</c> ENUM case with a renderer-side harness (<c>Pyre.cs:530-587</c>,
    /// <c>PyreRenderer.cs:392</c>), so there is no form object to hand to that bridge and Shaper had no fire at
    /// all. This class is the missing half — it implements <see cref="IShaperCompositeSource"/> directly and
    /// drives the same public sim Pyre drives.
    ///
    /// <b>Nothing about the physics is re-ported.</b> <see cref="FireSim"/> and <see cref="FireParams"/> are
    /// already public and already reachable from this bridge asmdef (<c>Runtime/PyreShaper</c> references
    /// <c>com.Lautaro-Arino.Laubrary.SpriteFx</c>), so hosting the flame needed no change to Pyre or to the sim
    /// whatsoever. What lives here is the replay harness and the dial set, mirroring Pyre's own
    /// <c>FireParamsAt</c> (<c>PyreRenderer.cs:500-528</c>) field for field.
    ///
    /// <b>Why a flame can be scrubbed at all.</b> Every other Shaper generator computes a frame from
    /// (seed, phase) alone. A flame genuinely cannot: heat is carried by a velocity field, so frame N depends on
    /// frame N-1. It stays fully deterministic by being REACHED rather than evaluated — replayed from a fixed
    /// reset, the same discipline the sim's own header sets out (<c>FireSim.cs:7-19</c>). The replay is cached by
    /// content (see <see cref="ContentHash"/>), so playing or baking forward costs one step per frame instead of
    /// re-running the whole history for each one.
    ///
    /// <b>Emitters are the built-in fixed ones, and that is a limit worth naming.</b> Pyre can also source a
    /// flame's emitters from a layer's swarm, one injection per particle (<c>PyreRenderer.cs:557-569</c>). That is
    /// unreachable from here by construction: <see cref="IShaperCompositeSource.Render"/> is handed only
    /// (width, height, phase, seed) — a composite source cannot see the node it hangs on, so it cannot see that
    /// node's <c>ShaperSwarmDef</c>. Shaper's swarm still applies to this node the way it applies to any other:
    /// it instances the finished flame, rather than feeding the flame's interior. Sourcing emitters from the
    /// swarm would need the composite contract itself widened, which is a bigger, separate decision than this.
    /// </summary>
    [Serializable]
    [ShaperCompositeSourceInfo("Fire", "Simulations")]
    public sealed class FireCompositeSource : IShaperCompositeSource, IShaperCacheableSource
    {
        // Field ids only have to be DISTINCT and STABLE: they key the seeded draw a Min-Max dial makes
        // (PyreShaperEval.Eval → PyreRenderer.Hash), so two dials must never share one or they would draw the
        // same number, and a dial must never change id or its authored value would move. They are deliberately
        // NOT Pyre's own F_Fire* ids: Pyre's fire dials live on a layer, not on a form, so there is no hosted
        // object here whose RNG stream this one would have to match.
        const int FldIntensity = 1, FldDirection = 2, FldEmitterWidth = 3, FldEmitterInset = 4;
        const int FldHeat = 5, FldFuel = 6, FldPulse = 7, FldFlow = 8, FldBuoyancy = 9;
        const int FldCurl = 10, FldCurlScale = 11, FldFlicker = 12, FldStretch = 13, FldPinch = 14;
        const int FldBreakup = 15, FldDissipation = 16, FldBurn = 17, FldReach = 18, FldEdgeCooling = 19;

        // dt is ONE FRAME split across the substeps, so every velocity dial below reads in pixels per frame
        // rather than per substep — raising Sub-steps then smooths the motion without also speeding it up.
        // Same constant and the same reason as Pyre's FireDt (PyreRenderer.cs:390).
        const float FrameDt = 1f;

        // ── the burn ─────────────────────────────────────────────────────────────────────────────────────

        [Tooltip("Shapes the whole burn over the animation: 0 puts the emitter out, 1 drives it flat out. Scales "
               + "the heat and fuel injected, so one curve takes the flame from ignite through roar to nothing.")]
        public ZUIValue intensity = PyreShaperSimSupport.IgniteHoldFade();

        [Min(1)]
        [Tooltip("How many flames radiate from the centre. 1 is a single directional flame; more open cold gaps "
               + "between pointed tongues.")]
        public int arms = 1;

        [Tooltip("Mirror makes every arm emit identically, so the flame is symmetric. Vary gives each arm its own "
               + "flicker and pulse phase, so the arms move independently.")]
        public FireArmMode armMode = FireArmMode.Mirror;

        [Tooltip("Which way the first arm points, in degrees. 90 sends the flame up the canvas.")]
        public ZUIValue direction = new ZUIValue(90f);

        [Tooltip("How wide the base of each flame is, in pixels. Wider gives a broader, slower-looking fire.")]
        public ZUIValue emitterWidth = new ZUIValue(9f);

        [Tooltip("Pushes each emitter out from the centre, in pixels, so the flames start off a ring rather than "
               + "a point.")]
        public ZUIValue emitterInset = new ZUIValue(0f);

        [Tooltip("How hot the emitter injects. High burns bright immediately; low needs fuel to build a body.")]
        public ZUIValue heat = new ZUIValue(0.95f);

        [Tooltip("Unburnt fuel injected alongside the heat. Fuel converting into heat is what gives the flame a "
               + "body instead of only a glow.")]
        public ZUIValue fuel = new ZUIValue(0.75f);

        [Tooltip("How much the emitter's output breathes in and out, so the base of the flame is never static.")]
        public ZUIValue pulse = new ZUIValue(0.18f);

        // ── how it moves ─────────────────────────────────────────────────────────────────────────────────

        [Tooltip("A steady outward push away from the centre — turns a lick into a jet.")]
        public ZUIValue flow = new ZUIValue(1f);

        [Tooltip("How strongly heat carries itself outward. This is what makes the flame CLIMB rather than merely "
               + "spread.")]
        public ZUIValue buoyancy = new ZUIValue(4f);

        [Tooltip("Swirl strength across the arm axis. Curls the tongues so they lick sideways instead of only "
               + "travelling straight.")]
        public ZUIValue curl = new ZUIValue(1.5f);

        [Tooltip("Size of the swirl. Small values give fine turbulence; large ones give slow broad rolls.")]
        public ZUIValue curlScale = new ZUIValue(7f);

        [Tooltip("Sideways wobble of the tongues — how much they wave frame to frame.")]
        public ZUIValue flicker = new ZUIValue(0.6f);

        [Tooltip("Elongates the flame along its own direction, so it reads as a tongue rather than a bloom.")]
        public ZUIValue stretch = new ZUIValue(3f);

        [Tooltip("Tapers the sides to a point. Most of what makes this read as fire, and what keeps several arms "
               + "distinct from each other.")]
        public ZUIValue pinch = new ZUIValue(0.6f);

        [Tooltip("Eats the outer edge into wisps instead of a smooth silhouette. Bites the cool edge, not the "
               + "hot core.")]
        public ZUIValue breakup = new ZUIValue(0.4f);

        // ── how it dies ──────────────────────────────────────────────────────────────────────────────────

        [Tooltip("How fast heat fades once it leaves the emitter. High gives a short sharp flame.")]
        public ZUIValue dissipation = new ZUIValue(0.35f);

        [Tooltip("How fast fuel converts into heat. Slow burning carries the flame further before it lights.")]
        public ZUIValue burn = new ZUIValue(1.5f);

        [Tooltip("How far the flame may reach, as a fraction of the canvas half-size. It can never touch the "
               + "frame edge however hard the other dials are driven.")]
        public ZUIValue reach = new ZUIValue(0.8f);

        [Tooltip("How hard the flame is killed once it passes its reach, so the outer edge stops rather than "
               + "clipping.")]
        public ZUIValue edgeCooling = new ZUIValue(0.9f);

        // ── how it is drawn and how it is simulated ──────────────────────────────────────────────────────

        [Tooltip("The smoke-to-fire ramp the flame is painted through. Its alpha is what this generator "
               + "publishes as coverage, so the transparent end of the ramp is also the flame's silhouette.")]
        public Gradient ramp = PyreShaperSimSupport.DefaultRamp();

        [Range(0f, 0.9f)]
        [Tooltip("Heat below this reads as empty. Raise it for a crisper silhouette with less haze around it.")]
        public float threshold = 0.06f;

        [Tooltip("Contrast on the ramp lookup. Below 1 pushes more of the flame toward the hot end of the ramp.")]
        public float contrast = 0.85f;

        [Min(1)]
        [Tooltip("Simulation steps per animation frame. More gives smoother, faster-looking motion over the same "
               + "number of frames, at a proportional cost.")]
        public int subSteps = 2;

        [Min(1)]
        [Tooltip("How many simulation steps the whole animation spans. Set this to the document's frame count so "
               + "each frame advances the flame exactly once; a smaller number makes the burn play out sooner.")]
        public int simFrames = 16;

        // ── replay state (never serialized, never drawn) ─────────────────────────────────────────────────
        // Private rather than [NonSerialized] public: Unity skips private fields anyway, and the window's
        // reflection drawer only ever reads PUBLIC instance fields (ZuiReflect.cs:130), so keeping these private
        // is what guarantees they can neither be saved into a document nor appear on the card as a dial. A
        // domain reload therefore lands with lastFrame at -1 and the next render cold-replays, which is correct.
        FireSim _sim;
        ShaperCacheKey _lastKey;
        int _lastFrame = -1;

        public string SourceLabel => "Fire";

        public void Render(int width, int height, float phase01, uint seed, Color32[] target)
        {
            if (target == null || width <= 0 || height <= 0) return;
            if (target.Length < width * height) return;

            int sd = unchecked((int)seed);
            int frames = Mathf.Max(1, simFrames);
            int frame = PyreShaperSimSupport.SimFrameOfPhase(phase01, frames);

            // The key gates the replay, so it must fold everything that changes the GRID — the dials, the seed
            // and the canvas size — but the render-only settings too, because they are cheap to include and a
            // key that misses one is a silently stale picture rather than a slow one.
            var key = KeyFor(width, height, sd);

            if (_sim == null) _sim = new FireSim();
            _sim.Allocate(width, height);

            PyreShaperSimSupport.Replay(ref _lastKey, ref _lastFrame, key, frame,
                                        () => _sim.Reset(),
                                        f => Step(f, frames, sd));

            // FireSim.Render writes only the pixels above threshold and leaves the rest untouched
            // (FireSim.cs:297-307) — it is built to composite into a Pyre layer that already holds something.
            // A composite bake buffer carries no such promise, so it is cleared first; without this a previous
            // frame's flame would show through wherever this one is cold.
            Array.Clear(target, 0, width * height);
            _sim.Render(target, ramp, 1f, threshold, contrast);
        }

        /// One frame of the sim, replayed identically every time that frame is reached. The dials are read at the
        /// frame's OWN phase, not at the phase being rendered — that is what makes the burn a shape over the
        /// animation rather than a value smeared across it, and it is why the conversion goes through
        /// <see cref="ShaperClock.PhaseOfFrame"/> rather than reusing the caller's phase.
        void Step(int frame, int frames, int sd)
        {
            float lp = ShaperClock.PhaseOfFrame(frame, frames);
            var p = ParamsAt(lp, sd);
            int steps = Mathf.Max(1, subSteps);
            for (int s = 0; s < steps; s++)
            {
                // The tiny per-substep phase nudge advances the noise smoothly WITHIN a frame instead of
                // sampling the same turbulence `steps` times; same nudge as Pyre's StepFire (PyreRenderer.cs:494).
                _sim.Step(p, sd, lp + s / (float)steps * 0.01f, FrameDt / steps);
            }
        }

        /// The dials resolved at one phase. Mirrors Pyre's FireParamsAt (PyreRenderer.cs:500-528) field for field,
        /// including its clamps — the clamps are not defensive noise, they are what stops an authored curve
        /// overshoot from driving the sim somewhere it has no defined behaviour.
        FireParams ParamsAt(float life, int sd)
        {
            float E(ZUIValue v, int fid) =>
                PyreShaperEval.Eval(v, life, sd, PyreRenderer.ModParticleIndex, fid, PyreShaperSimSupport.LayerSalt);

            float amount = Mathf.Clamp01(E(intensity, FldIntensity));
            return new FireParams
            {
                arms = Mathf.Max(1, arms),
                armMode = armMode,
                steps = Mathf.Max(1, subSteps),
                directionDeg = E(direction, FldDirection),
                emitterWidth = Mathf.Max(1f, E(emitterWidth, FldEmitterWidth)),
                emitterInset = E(emitterInset, FldEmitterInset),
                heat = Mathf.Clamp01(E(heat, FldHeat) * amount),
                fuel = Mathf.Clamp01(E(fuel, FldFuel) * amount),
                pulse = Mathf.Max(0f, E(pulse, FldPulse)),
                flow = E(flow, FldFlow),
                buoyancy = E(buoyancy, FldBuoyancy),
                curl = Mathf.Max(0f, E(curl, FldCurl)),
                curlScale = Mathf.Max(2f, E(curlScale, FldCurlScale)),
                flicker = Mathf.Max(0f, E(flicker, FldFlicker)),
                stretch = Mathf.Max(0f, E(stretch, FldStretch)),
                pinch = Mathf.Max(0f, E(pinch, FldPinch)),
                breakup = Mathf.Max(0f, E(breakup, FldBreakup)),
                dissipation = Mathf.Max(0f, E(dissipation, FldDissipation)),
                burn = Mathf.Max(0f, E(burn, FldBurn)),
                reach = Mathf.Clamp01(E(reach, FldReach)),
                edgeCooling = Mathf.Clamp01(E(edgeCooling, FldEdgeCooling)),
            };
        }

        ShaperCacheKey KeyFor(int width, int height, int sd)
        {
            var m = ShaperCacheMixer.Begin("shaper.pyreshaper.firecompositesource.v1");
            // JSON covers every serialized dial in one pass, including each ZUIValue's whole authored envelope,
            // so adding a dial cannot leave the key silently behind the way a hand-written field list would.
            m.MixString(JsonUtility.ToJson(this));
            // The ramp is the one thing JSON does not carry (a Gradient has no stable textual form), so it is
            // folded in explicitly — otherwise recolouring the flame would not invalidate anything.
            m = PyreShaperSimSupport.MixGradient(m, ramp);
            m.MixInt(width); m.MixInt(height); m.MixInt(sd);
            return m.Key;
        }

        /// <summary>
        /// T-0115's optional cache capability. Seed and phase are deliberately absent: the node's own identity
        /// already folds both in before this is consulted (<c>ShaperNodeIdentity.cs:77-78</c>), so mixing them
        /// again would be redundant. Canvas size is absent for the same reason — it is the node's, not the
        /// source's. What is here is exactly what the AUTHOR can change, which is what the fallback
        /// reference-identity path would otherwise miss entirely.
        /// </summary>
        public ShaperCacheKey ContentHash()
        {
            var m = ShaperCacheMixer.Begin("shaper.pyreshaper.firecompositesource.content.v1");
            m.MixString(JsonUtility.ToJson(this));
            m = PyreShaperSimSupport.MixGradient(m, ramp);
            return m.Key;
        }
    }
}
