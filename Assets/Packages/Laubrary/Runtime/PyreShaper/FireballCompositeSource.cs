using System;
using Laubrary.Pyre;
using Laubrary.Shaper;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0173 — the radial star-burst flame as a Shaper composite generator: Pyre's cellular "doom fire"
    /// (<see cref="FireballSim"/>, <c>Runtime/SpriteFx/FireballSim.cs:32</c>) driven from Shaper's own clock.
    ///
    /// <b>What it is, next to <see cref="FireCompositeSource"/>.</b> Fire is a fluid simulation — heat carried by
    /// a velocity field, with buoyancy and curl noise. This is the opposite and much cheaper rule: each cell
    /// takes the value of a cell one step CLOSER to the centre, minus a small random cooling, with a small
    /// sideways slip (<c>FireballSim.cs:5-15</c>). Iterated, that single rule is the classic flickering pixel
    /// fire, and folding it into wedges turns it into a pointed kaleidoscope burst. Both are stateful and both
    /// are reached by replay; they are not two settings of one generator, they are two different looks.
    ///
    /// <b>Why it is a composite source.</b> Same reason as Fire: in Pyre, Fireball is a <c>ShapeForm</c> enum case
    /// with a renderer-side harness (<c>Pyre.cs:589-607</c>, <c>PyreRenderer.cs:757</c>), not a <c>PyreForm</c>
    /// plug-in, so <see cref="PyreFormCompositeSource"/> has nothing to host. The sim itself is untouched and
    /// un-forked — it was already public and already reachable from this bridge asmdef.
    ///
    /// <b>Single-source by construction.</b> The heat comes from one hot core at the centre and propagates
    /// outward; there is no emitter set to place, so the swarm question Fire has to answer does not arise here at
    /// all. Shaper's swarm still instances the finished burst like any other node.
    /// </summary>
    [Serializable]
    [ShaperCompositeSourceInfo("Fireball", "Simulations", ShaperCompositeReason.AuthoredData)]
    public sealed class FireballCompositeSource : IShaperCompositeSource
    {
        // Distinct, stable ids for the seeded Min-Max draw — see FireCompositeSource for the full reasoning.
        const int FldSource = 1, FldRadius = 2, FldCooling = 3, FldSharpness = 4, FldSpread = 5, FldReach = 6;

        [Tooltip("Shapes the whole burst over the animation: how hot the centre injects, from nothing through "
               + "full blast and back. One curve takes the fireball from ignition to burnt out.")]
        [ZUILabel("Source")] public ZUIValue source = PyreShaperSimSupport.IgniteHoldFade();

        [Tooltip("Radius of the hot core at the centre, in pixels. Wider gives a fatter fireball with a bigger "
               + "solid heart.")]
        [ZUILabel("Core Radius")] public ZUIValue sourceRadius = new ZUIValue(4f);

        [Tooltip("How fast heat is lost travelling outward. This alone sets arm LENGTH — low cooling lets heat "
               + "survive further out, so the arms reach.")]
        [ZUILabel("Cooling")] public ZUIValue cooling = new ZUIValue(0.03f);

        [Tooltip("How hard cells off a wedge axis are cooled. This sets arm THINNESS independently of length, so "
               + "long and thin is reachable rather than only long and fat.")]
        [ZUILabel("Sharpness")] public ZUIValue sharpness = new ZUIValue(0.2f);

        [Tooltip("Sideways waver as heat travels out, so the tongues lick instead of running as straight spokes.")]
        [ZUILabel("Spread")] public ZUIValue spread = new ZUIValue(0.5f);

        [Tooltip("How far the burst may reach, as a fraction of the canvas half-size. It can never touch the "
               + "frame edge however hard the other dials are driven.")]
        [ZUILabel("Reach")] public ZUIValue reach = new ZUIValue(0.95f);

        [Min(1)]
        [Tooltip("How many wedges the burst is folded into. 1 is a plain outward ball; more gives the pointed "
               + "kaleidoscope star.")]
        [ZUILabel("Arms")] public int arms = 1;

        [Tooltip("On, alternate wedges are reflected so neighbours meet at a seam. Off, every wedge is the same "
               + "one rotated.")]
        [ZUILabel("Mirror")] public bool mirror = true;

        [Tooltip("The smoke-to-fire ramp the burst is painted through. Its alpha is what this generator publishes "
               + "as coverage, so the transparent end of the ramp is also the burst's silhouette.")]
        [ZUILabel("Ramp")] public Gradient ramp = PyreShaperSimSupport.DefaultRamp();

        [Range(0f, 0.9f)]
        [Tooltip("Heat below this reads as empty. Raise it for a crisper silhouette with less haze around it.")]
        [ZUILabel("Threshold")] public float threshold = 0.06f;

        [Tooltip("Contrast on the ramp lookup. Below 1 pushes more of the burst toward the hot end of the ramp.")]
        [ZUILabel("Contrast")] public float contrast = 0.85f;

        [Min(1)]
        [Tooltip("How many simulation steps the whole animation spans. Set this to the document's frame count so "
               + "each frame advances the burst exactly once; a smaller number makes it play out sooner.")]
        [ZUILabel("Sim Frames")] public int simFrames = 8;

        /// <summary>
        /// T-0202 — overall opacity over the burst's life, the row Pyre's Shape section shows for a Fireball
        /// layer. Pyre evaluates the layer's Alpha envelope and hands it to the sim's own renderer
        /// (<c>PyreRenderer.cs:800</c>, <c>sim.Render(target, ramp, alpha, …)</c>); this source hardcoded 1, so a
        /// hosted burst was still at full strength in the frames where Pyre's had already gone out. Defaulted
        /// from a throwaway <see cref="PyreLayer"/> so the two tools' defaults cannot drift apart.
        /// </summary>
        [Tooltip("Overall opacity across the burst's life, multiplied into what the simulation paints. "
               + "Fading it to nothing at the end is what makes the burst go out rather than stop.")]
        [ZUILabel("Alpha")] public ZUIValue alpha = new PyreLayer().alpha;

        // Replay state — private so neither Unity's serializer nor the window's reflection drawer can see it.
        FireballSim _sim;
        ShaperCacheKey _lastKey;
        int _lastFrame = -1;

        public string SourceLabel => "Fireball";

        public void Render(int width, int height, float phase01, uint seed, Color32[] target)
        {
            if (target == null || width <= 0 || height <= 0) return;
            if (target.Length < width * height) return;

            int sd = unchecked((int)seed);
            int frames = Mathf.Max(1, simFrames);
            int frame = PyreShaperSimSupport.SimFrameOfPhase(phase01, frames);
            var key = KeyFor(width, height, sd);

            if (_sim == null) _sim = new FireballSim();
            _sim.Allocate(width, height);

            PyreShaperSimSupport.Replay(ref _lastKey, ref _lastFrame, key, frame,
                                        () => _sim.Reset(),
                                        f => Step(f, frames, sd));

            // Unlike FireSim, FireballSim.Render writes every pixel including the empty ones
            // (FireballSim.cs:146 clears below threshold), so the buffer needs no pre-clear.
            // The layer Alpha envelope at this life, exactly where PyreRenderer.cs:800 applies it. Null only on a
            // document authored before the field existed, which keeps rendering as it did until its card repairs it.
            float a = alpha != null
                ? Mathf.Clamp01(PyreShaperEval.Eval(alpha, Mathf.Clamp01(phase01), sd,
                                                    PyreRenderer.ModParticleIndex, PyreShaperEval.FieldIdAlpha, 0))
                : 1f;
            _sim.Render(target, ramp, a, threshold, contrast);
        }

        /// One cellular step. There are NO substeps here, and that is not an omission: FireballSim.Step takes the
        /// INTEGER frame index as its random key (<c>FireballSim.cs:64,71</c>), so a frame's flicker is a property
        /// of that frame's number. Splitting a frame would change which randomness a frame draws and break the
        /// promise that scrubbing back to a frame shows exactly what playing through it showed.
        void Step(int frame, int frames, int sd)
        {
            float lp = ShaperClock.PhaseOfFrame(frame, frames);
            _sim.Step(ParamsAt(lp, sd), sd, lp, frame);
        }

        /// The dials resolved at one phase. Mirrors Pyre's FireballParamsAt (PyreRenderer.cs:817-832) field for
        /// field, clamps included.
        FireballParams ParamsAt(float life, int sd)
        {
            float E(ZUIValue v, int fid) =>
                PyreShaperEval.Eval(v, life, sd, PyreRenderer.ModParticleIndex, fid, PyreShaperSimSupport.LayerSalt);

            return new FireballParams
            {
                sourceHeat = Mathf.Clamp01(E(source, FldSource)),
                sourceRadius = Mathf.Max(1f, E(sourceRadius, FldRadius)),
                cooling = Mathf.Max(0.001f, E(cooling, FldCooling)),
                spread = Mathf.Max(0f, E(spread, FldSpread)),
                reach = Mathf.Clamp01(E(reach, FldReach)),
                sharpness = Mathf.Max(0f, E(sharpness, FldSharpness)),
                arms = Mathf.Max(1, arms),
                mirror = mirror,
            };
        }

        ShaperCacheKey KeyFor(int width, int height, int sd)
        {
            var m = ShaperCacheMixer.Begin("shaper.pyreshaper.fireballcompositesource.v1");
            m.MixString(JsonUtility.ToJson(this));
            m = PyreShaperSimSupport.MixGradient(m, ramp);
            m.MixInt(width); m.MixInt(height); m.MixInt(sd);
            return m.Key;
        }

    }
}
