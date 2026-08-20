// ForkBlastForm — the Fork Blast puff detonation as a PlusForm (the algorithm itself is PlusForkBlast; its header
// documents what was ported from the Kiln "agent3_fork_explosive" generator and what was dropped).
//
// Migrated from the ShapeForm.ForkBlast enum case: same dials, defaults, ranges, seed and field-id streams (EvalRaw
// with the ids the enum case used), so a migrated layer renders byte-for-byte what it rendered before.
using System;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    [Serializable]
    [PlusFormInfo("Fork Blast", group: "Explosions", icon: "meteor")]
    public sealed class ForkBlastForm : PlusForm
    {
        public override string DisplayName => "Fork Blast";
        public override string Description =>
            "A radial detonation built from hundreds of small burning puffs thrown outward and shaped by "
            + "drag/entrainment — a gritty, particulate blast rather than Inferno's smooth volumetric cloud. "
            + "Closed-form (not a sim), so any frame is exact. The SWARM places the blasts: off = one centred "
            + "detonation, on = one blast per swarm particle. Colour comes only from the Shape Fill (left end = "
            + "white-hot); its own alpha ramp doubles as the body's opacity ceiling.";

        // ── the blast's clock and envelopes ──
        [Tooltip("The blast's progress over the layer's life — a TIME REMAP as one envelope. The default straight line plays in real time; bend it to snap in and hold, slow the tail, or freeze a pose (a Static value).")]
        [Range(0f, 1f)] public ZUIValue progress = IdentityCurve();
        [Tooltip("How far the fastest puffs travel, as a fraction of the canvas half-extent, over life.")]
        [Range(0.1f, 1.5f)] public ZUIValue reach = new ZUIValue(0.85f);
        [Tooltip("White-hot ignition flash at each blast's birth, over life. Tinted from the Fill's hot end.")]
        [Range(0f, 1f)] public ZUIValue flash = new ZUIValue(0.7f);

        // ── emission shape ──
        [Tooltip("Half-angle of the emission arc, degrees. 180 = a full circle (a true radial blast).")]
        [Range(0f, 180f)] public float spread = 180f;
        [Tooltip("The arc's centre direction, degrees. Only visible when Spread is below 180 (a full circle has no facing).")]
        [Range(-180f, 180f)] public float aim = 0f;
        [Tooltip("Angle-distribution power across the arc. 1 = uniform coverage — keep this near 1 on a full circle, or the puffs pile back into a beam.")]
        [Range(0.3f, 3f)] public float bias = 1.05f;
        [Tooltip("Puffs per blast.")]
        [Range(20, 500)] public int puffs = 220;

        // ── puff physics — travel, growth, shape ──
        [Tooltip("Higher decelerates a puff sooner, so it stalls closer to the source.")]
        [Range(0.3f, 6f)] public float drag = 2.2f;
        [Tooltip("Upward rise late in a puff's life.")]
        [Range(0f, 0.4f)] public float buoyancy = 0.05f;
        [Tooltip("Radius gained per pixel travelled (entrainment) — a puff fattens as it slows.")]
        [Range(0f, 0.3f)] public float growth = 0.09f;
        [Tooltip("Radius gained per unit AGE rather than distance — fills a stalled centre so the fireball doesn't hollow into a smoke ring.")]
        [Range(0f, 1.2f)] public float swell = 0.4f;
        [Tooltip("Extra length/width at birth, along the puff's own travel direction; decays as it slows.")]
        [Range(0f, 4f)] public float elongation = 2f;
        [Tooltip("The age by which a puff has stopped stretching and is round again.")]
        [Range(0.02f, 0.9f)] public float roundAt = 0.3f;
        [Tooltip("Per-puff variation in speed / size / amplitude / life.")]
        [Range(0.3f, 4f)] public float jitter = 0.55f;
        [Tooltip("0 = every puff leaves at one speed, which reads as a hollow expanding SHELL. Above 0 spreads the speeds so the middle fills in with slow-travelling gas instead of hollowing into a smoke ring.")]
        [Range(0f, 1f)] public float fillVolume = 0.7f;

        // ── detonation clock ──
        [Tooltip("Above 1 piles puff births at the FRONT of the birth span — a hard attack with a ragged tail, which is what makes this read as a detonation rather than a steady jet.")]
        [Range(1f, 6f)] public float birthSkew = 2.4f;
        [Tooltip("How much of the blast's own clock the puff births are spread over.")]
        [Range(0.01f, 0.6f)] public float birthSpan = 0.14f;
        [Tooltip("How long a puff burns, as a fraction of the blast's own clock.")]
        [Range(0.1f, 1.2f)] public float puffLife = 0.55f;
        [Tooltip("Amplitude falloff exponent over a puff's life.")]
        [Range(0f, 3f)] public float cool = 1.6f;

        // ── how it dies — hold the amplitude up, then contract rather than fade, closing inward from the fastest gas ──
        [Tooltip("Above 0 holds a puff's amplitude up and drops it LATE instead of dimming from birth — the delay that keeps the body solid long enough for Shrink to be the thing you see.")]
        [Range(0f, 3f)] public float hold = 1.7f;
        [Tooltip("Exponent on the Fill's own alpha ceiling. Below 1 pushes the body toward solid while doing least at the coolest, already-thin rim — so the body opens up without trading away the edge falloff.")]
        [Range(0.2f, 1.5f)] public float opacity = 0.55f;
        [Tooltip("How much of its radius a puff loses by the end of its life — dies by getting SMALLER, not more transparent. The kernel's peak is its amplitude, so a shrinking puff stays exactly as bright at its centre.")]
        [Range(0f, 1f)] public float shrink = 0.8f;
        [Tooltip("The age the contraction starts at.")]
        [Range(0f, 0.95f)] public float shrinkAt = 0.5f;
        [Tooltip("The fastest (outermost) gas dies first, so the silhouette closes INWARD as it collapses — shrinking every puff by the same amount alone leaves the outer shell in place and merely makes it smaller.")]
        [Range(0f, 1f)] public float leadDie = 0.42f;

        // ── shedding burning mass ──
        [Tooltip("Lumps of burning mass shed off the blast in its OPENING phase, that then dissipate — optional, 0 = none. Opposite death to the body: it balloons and thins as it goes, which is what makes it read as having come off something. The Gob dials below act only when this is above 0.")]
        [Range(0, 40)] public int gobs = 0;
        [Tooltip("A gob's radius — mass, not a spark; several times a puff's own size.")]
        [Range(1f, 10f)] public float gobSizePx = 4.5f;
        [Tooltip("A gob's travel, as a fraction of Reach.")]
        [Range(0.3f, 1.5f)] public float gobReach = 0.85f;
        [Tooltip("A gob DISSIPATES — it balloons as it goes out, the opposite of the body, which shrinks and stays solid.")]
        [Range(0f, 3f)] public float gobSwell = 1.6f;
        [Tooltip("A gob's life, as a multiple of Puff life.")]
        [Range(0.5f, 3f)] public float gobLife = 1.3f;
        [Tooltip("Gob brightness.")]
        [Range(0f, 3f)] public float gobAmount = 1.2f;
        [Tooltip("Gobs are born inside this fraction of the birth span — the opening phase. A gob that leaves late just reads as a second, smaller explosion.")]
        [Range(0.05f, 1f)] public float gobTiming = 0.45f;

        // ── look — puff size at the source, turbulence, and the heat-field-to-pixel mapping ──
        [Tooltip("A puff's radius at the source.")]
        [Range(1f, 10f)] public float puffSizePx = 2.6f;
        [Tooltip("A per-puff wobble that breaks up the disc into licks — an approximation of true domain-warp turbulence.")]
        [Range(0f, 20f)] public float turbulencePx = 3f;
        [Tooltip("Tint gained by the end of a puff's life, darkening/desaturating it — a third route to transparency-reading if pushed too high; keep it modest.")]
        [Range(0f, 0.6f)] public float soot = 0.3f;
        [Tooltip("Width of the falloff at the silhouette's edge, in heat-field units.")]
        [Range(0.1f, 3f)] public float edgeSoftness = 0.6f;
        [Tooltip("Fit the heat ceiling to this frame's own measured peak instead of a fixed Field high. On by default — leaving it off means Field high has to be re-fitted by hand any time puff count, amplitude, blast count, or almost any other dial changes, or the blast reads as a flat, washed-out silhouette (ceiling too high) or a blown-out core (ceiling too low).")]
        public bool autoExposure = true;
        [ZUIShowIf("autoExposure", "True")]
        [Tooltip("Multiplier on the measured peak of this frame's heat field. Lower = brighter/hotter overall (clips more of the field to the ramp's hot end); higher = dimmer, more rim.")]
        [Range(0.2f, 2f)] public float exposure = 0.85f;
        [Tooltip("Heat-field value at the silhouette's outer edge — everything below this is fully transparent.")]
        [Range(0f, 0.6f)] public float fieldLow = 0.22f;
        [ZUIShowIf("autoExposure", "False")]
        [Tooltip("Heat-field value at which the Fill ramp tops out (hottest). Puff count/amplitude/overlap shift the field's real range, so this and Field low are the two dials that keep the blast from reading as all-rim or all-core.")]
        [Range(0.3f, 3f)] public float fieldHigh = 1.2f;
        [Tooltip("Bends where the ramp is spent along the heat field. Below 1 = hotter/brighter overall, above 1 = mostly cool envelope with a tight hot spine.")]
        [Range(0.3f, 2f)] public float curve = 1f;

        static ZUIValue IdentityCurve()
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(1f, 1f));
            return v;
        }

        // The enum case's field ids, kept verbatim so a migrated layer's Min-Max dials draw the same random stream.
        const int FldProgress = -55, FldReach = -56, FldFlash = -57, FldPixHash = -58;

        [NonSerialized] PlusForkBlast.Anim _anim;

        public override void Prepare(in PlusFormPrepareCtx ctx)
        {
            _anim = new PlusForkBlast.Anim
            {
                progress = Mathf.Clamp01(ctx.EvalRaw(progress, FldProgress)),
                reach = ctx.EvalRaw(reach, FldReach),
                flash = ctx.EvalRaw(flash, FldFlash),
            };
        }

        public override void Render(in PlusFormCtx ctx, Color32[] target)
        {
            var p = new PlusForkBlast.Params
            {
                spread = spread, aim = aim, bias = bias, puffCount = puffs,
                drag = drag, growth = growth, swell = swell, elong = elongation, roundAt = roundAt, buoy = buoyancy,
                blastSkew = birthSkew, blastSpan = birthSpan, velSpread = fillVolume, puffLife = puffLife, jitter = jitter, cool = cool,
                hold = hold, shrink = shrink, shrinkAt = shrinkAt, leadDie = leadDie, opaq = opacity, soot = soot,
                gobCount = gobs, gobReach = gobReach, gobSwell = gobSwell, gobLifeMul = gobLife, gobAmp = gobAmount, gobEarly = gobTiming,
                puffSizePx = puffSizePx, gobSizePx = gobSizePx, warpAmount = turbulencePx,
                lo = fieldLow, hi = fieldHigh, curve = curve, soft = edgeSoftness, exposureMult = exposure, autoExposure = autoExposure,
            };
            PlusForkBlast.SwarmOrigin[] origins = null;
            if (ctx.swarm != null)
            {
                origins = new PlusForkBlast.SwarmOrigin[ctx.swarm.Length];
                for (int i = 0; i < origins.Length; i++)
                {
                    var sp = ctx.swarm[i];
                    origins[i] = new PlusForkBlast.SwarmOrigin
                    {
                        x = sp.x / Mathf.Max(1, ctx.W) * 2f - 1f,
                        y = sp.y / Mathf.Max(1, ctx.H) * 2f - 1f,
                        start = Mathf.Clamp(sp.spawnLife, 0f, 0.85f),
                    };
                }
            }
            var mods = new PlusForkBlast.Mods
            {
                pix = ctx.pix, phase = ctx.phase, frameIndex = ctx.frameIndex, life = ctx.life,
                pixHash = PyrePlusRenderer.Hash(ctx.seed, PyrePlusRenderer.ModParticleIndex, FldPixHash, ctx.layerSalt),
            };
            int seed = PyrePlusRenderer.Hash(ctx.seed, ctx.layerSalt, 1, 7);
            PlusForkBlast.Render(target, ctx.W, ctx.H, p, seed, ctx.fill, ctx.alpha, _anim, origins, mods);
        }
    }
}
