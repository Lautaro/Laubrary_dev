// InfernoForm — the Inferno volumetric fireball explosion as a PyreForm (the algorithm itself is PyreInferno).
//
// Migrated from the ShapeForm.Inferno enum case: the same dials with the same defaults, ranges and clamps, and the
// same seed / field-id streams (EvalRaw with the ids the enum case used), so a migrated layer renders byte-for-byte
// what it rendered before — including any Min-Max dial an asset had authored.
using System;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre.Forms.Kiln
{
    [Serializable]
    [PyreFormInfo("Inferno", group: "Explosions", icon: "bomb")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.PyrePlus.Forms.Kiln", "com.Lautaro-Arino.Laubrary.PyrePlus.Forms.Kiln", null)]
    public sealed class InfernoForm : PyreForm
    {
        /// Inferno inverse-warps every sample point itself (PyreInferno's field loop), so containment follows the warp;
        /// the renderer's generic post-render pass must therefore NOT warp it again.
        public override bool HandlesGeometry => true;

        public override string DisplayName => "Inferno";
        public override string Description =>
            "A volumetric fireball explosion: contained blasts built from big billowing lobes, a torn silhouette, an "
            + "ignition flash, embers, a smouldering afterglow and pseudo-3D lighting. Closed-form (not a sim), so any "
            + "frame is exact — which is what lets the Progress envelope remap the whole timeline. The SWARM places the "
            + "blasts: off = one centred blast, on = one blast per swarm particle. Colour comes only from the Shape "
            + "Fill (left end = white-hot); most dials are envelopes over the layer's life like every other form.";

        // ── the explosion's clock ──
        [Tooltip("The explosion's progress over the layer's life — a TIME REMAP as one envelope. The default straight line plays in real time; bend it to snap in and hold at full bloom, slow the smoky tail, or play sections at different speeds. A Static value freezes the explosion at that moment as a pose.")]
        [Range(0f, 1f)] public ZUIValue progress = IdentityCurve();

        // ── multi-blast character (only meaningful when the swarm ignites several blasts) ──
        [PyreSwarmOnly, Tooltip("Per-blast variation of size, torque, heat and jaggedness — 0 makes every blast a twin.")]
        [Range(0f, 1f)] public float mutation = 0.4f;
        [PyreSwarmOnly, Tooltip("How strongly overlapping blasts ADD heat instead of replacing one another — what makes overlapping blasts glow hotter than either alone.")]
        [Range(0f, 1f)] public float heatStacking = 0.48f;
        [PyreSwarmOnly, Tooltip("Slides each blast's fire-vs-smoke character across the sequence: negative = the first blasts burn fierier and later ones turn sootier, positive = the reverse. 0 = every blast takes the Fire and Smoke dials as-is.")]
        [Range(-1f, 1f)] public float characterDrift = 0f;
        [PyreSwarmOnly, Tooltip("Randomises each blast's fire-vs-smoke character — some fierier, some sootier, no order.")]
        [Range(0f, 1f)] public float characterJitter = 0f;

        // ── blast ──
        [Tooltip("Final occupied radius inside the safe zone, over the layer's life.")]
        [Range(0.05f, 1f)] public ZUIValue blastSize = new ZUIValue(0.82f);
        [Tooltip("White-hot ignition flash and central punch at each blast's birth, over the layer's life. Tinted from the Fill's hot end.")]
        [Range(0f, 1f)] public ZUIValue flash = new ZUIValue(0.72f);
        [Tooltip("How abruptly the first expansion happens — 1 is a violent snap (frames, not a bloom).")]
        [Range(0.2f, 1f)] public float bangSpeed = 0.82f;
        [Tooltip("DRAMA, opt-in: the bang overshoots its radius and settles back, ignition spikes the heat white-hot, and the flash blows out bigger. 0 = the calm prototype look.")]
        [Range(0f, 1f)] public float punch = 0f;
        [Tooltip("How FAR the flash reaches out from the blast centre — its size, set independently of how bright it is.")]
        [Range(0f, 1f)] public float flashReach = 0.4f;
        [Tooltip("How gradually the flash's alpha fades out into the cloud: 0 = a tight core with a crisp edge, 1 = a broad soft glow. (WHEN it fades is the Flash envelope's job.)")]
        [Range(0f, 1f)] public float flashSoftness = 0.45f;
        [Tooltip("Pulls the outer shape back in after the blast.")]
        [Range(0f, 1f)] public float recoil = 0.42f;

        // ── containment ──
        [Tooltip("How far the cloud's own rim fades out, in absolute canvas terms — so the fade looks the same whether the blast is tiny or huge. Low reads as a hard-edged solid; raise it for gas.")]
        [Range(0f, 1f)] public ZUIValue edgeSoftness = new ZUIValue(0.3f);
        [Tooltip("Minimum empty border around the effect, as a fraction of the canvas — nothing is drawn past it, so the effect can never touch the frame edge.")]
        [Range(0f, 0.25f)] public float safeMargin = 0.08f;
        [Tooltip("How wide the fade-to-nothing is as the cloud nears the Safe margin — anything close to the border dissolves instead of being cut. The band grows inward, so the frame edge itself is always fully transparent.")]
        [Range(0f, 1f)] public float frameFade = 0.35f;

        // ── cloud shape ──
        [Tooltip("Large coherent lobes, merged into one cloud.")]
        [Range(1, 9)] public int clumps = 5;
        [Tooltip("Separates the hot lobes without breaking cohesion, over the layer's life.")]
        [Range(0f, 1f)] public ZUIValue clumpSpread = new ZUIValue(0.52f);
        [Tooltip("Strength of the rolling, 3D-looking cloud pockets, over the layer's life — what keeps the cloud from reading as a flat slab.")]
        [Range(0f, 1f)] public ZUIValue billow = new ZUIValue(0.72f);
        [Tooltip("Breaks the perfect circle into torn explosive lobes, over the layer's life.")]
        [Range(0f, 1f)] public ZUIValue jagged = new ZUIValue(0.53f);
        [Tooltip("Keeps the MIDDLE of the cloud thick over the layer's life. The cavity and noise detail bite hardest where the cloud is thickest, which thins (or holes) the centre without this.")]
        [Range(0f, 1f)] public ZUIValue coreDensity = new ZUIValue(0.5f);
        [Tooltip("Higher keeps all clumps visibly connected as one mass, over the layer's life — fall to a low value late and the cloud visibly blows apart into fragments.")]
        [Range(0f, 1f)] public ZUIValue cohesion = new ZUIValue(0.78f);
        [Tooltip("Carves the cloud's core out into a cavity over the layer's life, leaving a burning shell — 0 = solid, high = a ring/torus of fire. Animate it to make the cloud bloom open into a ring.")]
        [Range(0f, 1f)] public ZUIValue hollow = new ZUIValue(0f);
        [Tooltip("Heat concentrated on the cavity's INNER boundary over the layer's life, so the shell visibly burns. Only acts once Hollow is raised.")]
        [Range(0f, 1f)] public ZUIValue hollowRim = new ZUIValue(0.5f);
        [Tooltip("Heat concentrated on the cloud's OUTER rim over the layer's life — a burning surface instead of an evenly lit disc. Animate it to have the shell ignite and cool.")]
        [Range(0f, 1f)] public ZUIValue outerRim = new ZUIValue(0f);

        // ── churn & motion ──
        [Tooltip("Rolling internal displacement, over the layer's life.")]
        [Range(0f, 1f)] public ZUIValue churn = new ZUIValue(0.68f);
        [Tooltip("Rotational torque, either way (−1..1), over the layer's life; 0 = none.")]
        [Range(-1f, 1f)] public ZUIValue rotation = new ZUIValue(0.22f);
        [Tooltip("A secondary compression wave that breathes the SAME blast in and out after the bang — it does not add a second explosion (use more swarm particles for that).")]
        [Range(0f, 1f)] public float pulse = 0.46f;

        // ── fire ──
        [Tooltip("How much of the cloud is FLAME, over the layer's life: 0 = no fire at all (pure smoke); 1 = fire fills most of the dense regions.")]
        [Range(0f, 1f)] public ZUIValue fire = new ZUIValue(0.79f);
        [Tooltip("Varies the heat WITHIN the fire — internal boiling regions. (Clumps shape the cloud's mass; this only changes how hot each part of it burns.)")]
        [Range(0f, 1f)] public float heatPockets = 0.67f;
        [Tooltip("How quickly flame turns into dark smoke over each blast's own life — the fire→smoke rate.")]
        [Range(0f, 1f)] public float cooling = 0.54f;
        [Tooltip("An inner glow at the cloud's core over the layer's life, added after Cooling so it survives it — the smoulder left inside the smoke. Its timing is entirely this envelope's: flat glows throughout, a curve swells and dies exactly when you draw it.")]
        [Range(0f, 1f)] public ZUIValue coreGlow = new ZUIValue(0.4f);

        // ── smoke ──
        [Tooltip("How much SOOT there is, over the layer's life — the one dial for the amount of smoke.")]
        [Range(0f, 1f)] public ZUIValue smoke = new ZUIValue(0.66f);
        [Tooltip("How far the soot reaches BEYOND the fire, over the layer's life — the shell of smoke that frames the flame and feathers into the background instead of stopping at its silhouette.")]
        [Range(0f, 1f)] public ZUIValue smokeSpread = new ZUIValue(0.45f);
        [Tooltip("Heavier, darker soot over the layer's life. SMOKE ONLY — burning pixels take the Fill ramp's colour outright, so this never tints the flame.")]
        [Range(0f, 1f)] public ZUIValue darkness = new ZUIValue(0.64f);
        [Tooltip("How long the smoke STAYS: 0 = fades out over the last frames; 1 = persists to the very end of the timeline. The shared Alpha envelope above also fades the tail by default — flatten it for smoke that holds to the last frame.")]
        [Range(0f, 1f)] public float linger = 0.5f;
        [Tooltip("How OPAQUE the thick of the cloud reads, over the layer's life: 0 = ghostly gas, 1 = dense fire and smoke read as solid matter. Affects flame and soot alike.")]
        [Range(0f, 1f)] public ZUIValue body = new ZUIValue(0.75f);

        // ── finish ──
        [Tooltip("Dissolves the whole effect to nothing over the final fraction of the timeline, so it ends on its own instead of running until the last frame cuts it off. 0 = no forced ending.")]
        [Range(0f, 1f)] public float dieOut = 0f;
        [Tooltip("Short contained sparks that arc out and fade before the border.")]
        [Range(0f, 1f)] public float embers = 0.38f;
        [Tooltip("Pseudo-3D shading from the cloud's own density — carves lit billows and shadowed pockets.")]
        [Range(0f, 1f)] public float lighting = 0.76f;
        [Tooltip("Separates hot cavities from dark billows on the heat ramp.")]
        [Range(0f, 1f)] public float contrast = 0.61f;

        // The identity curve (0,0)→(1,1): internal time == the layer's life, so an untouched Progress is no remap.
        static ZUIValue IdentityCurve()
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(1f, 1f));
            return v;
        }

        // The enum case's field ids, kept verbatim so a migrated layer's Min-Max dials draw the same random stream.
        const int FldProgress = -32, FldBlastSize = -33, FldFlash = -34, FldChurn = -35, FldRotation = -36;
        const int FldFire = -38, FldSmoke = -40, FldHollow = -41, FldPixHash = -42, FldCoreGlow = -43;
        const int FldBillow = -44, FldJagged = -45, FldCohesion = -46, FldClumpSpread = -47, FldDarkness = -48;
        const int FldBody = -49, FldHollowRim = -50, FldOuterRim = -51, FldCoreDensity = -52, FldSmokeSpread = -53;
        const int FldEdgeSoft = -54;

        [NonSerialized] PyreInferno.Anim _anim;

        public override void Prepare(in PyreFormPrepareCtx ctx)
        {
            _anim = new PyreInferno.Anim
            {
                progress = Mathf.Clamp01(ctx.EvalRaw(progress, FldProgress)),
                blastSize = ctx.EvalRaw(blastSize, FldBlastSize),
                flash = ctx.EvalRaw(flash, FldFlash),
                churn = ctx.EvalRaw(churn, FldChurn),
                rotation = ctx.EvalRaw(rotation, FldRotation),
                fire = ctx.EvalRaw(fire, FldFire),
                smoke = ctx.EvalRaw(smoke, FldSmoke),
                hollow = ctx.EvalRaw(hollow, FldHollow),
                coreGlow = ctx.EvalRaw(coreGlow, FldCoreGlow),
                billow = ctx.EvalRaw(billow, FldBillow),
                jagged = ctx.EvalRaw(jagged, FldJagged),
                cohesion = ctx.EvalRaw(cohesion, FldCohesion),
                clumpSpread = ctx.EvalRaw(clumpSpread, FldClumpSpread),
                darkness = ctx.EvalRaw(darkness, FldDarkness),
                body = ctx.EvalRaw(body, FldBody),
                hollowRim = ctx.EvalRaw(hollowRim, FldHollowRim),
                outerRim = ctx.EvalRaw(outerRim, FldOuterRim),
                coreDensity = ctx.EvalRaw(coreDensity, FldCoreDensity),
                smokeSpread = ctx.EvalRaw(smokeSpread, FldSmokeSpread),
                edgeSoft = ctx.EvalRaw(edgeSoftness, FldEdgeSoft),
            };
        }

        public override void Render(in PyreFormCtx ctx, Color32[] target)
        {
            var structural = new PyreInferno.Structural
            {
                mutation = mutation, accumulation = heatStacking, balanceDrift = characterDrift, balanceJitter = characterJitter,
                bangSpeed = bangSpeed, punch = punch, recoil = recoil, flashReach = flashReach, flashSoft = flashSoftness,
                clumps = clumps, pulse = pulse, heatPockets = heatPockets, cooling = cooling, linger = linger, dieOut = dieOut,
                embers = embers, lighting = lighting, contrast = contrast, margin = safeMargin, frameFade = frameFade,
            };
            // One blast per swarm instance, placed in y-up NDC at its spawn moment (swarm off ⇒ null ⇒ one centred blast).
            var origins = SwarmOrigins(ctx);
            var mods = new PyreInferno.Mods
            {
                geo = ctx.geo, pix = ctx.pix, phase = ctx.phase, frameIndex = ctx.frameIndex, life = ctx.life,
                pixHash = PyreRenderer.Hash(ctx.seed, PyreRenderer.ModParticleIndex, FldPixHash, ctx.layerSalt),
            };
            int seed = PyreRenderer.Hash(ctx.seed, ctx.layerSalt, 0, 0);
            PyreInferno.Render(target, ctx.W, ctx.H, structural, seed, ctx.fill, ctx.alpha, _anim, origins, mods);
        }

        static PyreInferno.SwarmOrigin[] SwarmOrigins(in PyreFormCtx ctx)
        {
            if (ctx.swarm == null) return null;
            var origins = new PyreInferno.SwarmOrigin[ctx.swarm.Length];
            for (int i = 0; i < origins.Length; i++)
            {
                var sp = ctx.swarm[i];
                origins[i] = new PyreInferno.SwarmOrigin
                {
                    x = sp.x / Mathf.Max(1, ctx.W) * 2f - 1f,
                    y = sp.y / Mathf.Max(1, ctx.H) * 2f - 1f,
                    start = Mathf.Clamp(sp.spawnLife, 0f, 0.85f),
                };
            }
            return origins;
        }
    }
}
