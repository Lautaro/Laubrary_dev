// PlasmaBloomForm — Kiln "Energy Explosion / agent3_fork" PLASMA BLOOM, generation 5, as a PlusForm.
//
// Source: D:/CODEZ/Kiln/projects/Energy Explosion/agents/agent3_fork (gen.py + plasma.py, MANIFEST "generation 5:
// the blast has a direction"). The algorithm and the per-component ported / approximated / dropped list are in
// PlusPlasmaBloom.cs's header. EVERY default below is the resolved value of contract draw `detonate`
// (D:/Claude@GDrive/Energy Explosion/GEN5/contract/agent3_fork/detonate/params.json, seed 1147, 128 px, 27 of 30
// frames shipped) — the canonical draw, #001 of the set, the "full-volume blowout" with every component on. The
// other nine draws load straight from their contract params.json (`PlusParityDump.FromContract` + `SetContractParam`
// for the nested / string-typed keys) — the contract, not a frozen table, is the source of truth for the parity runs.
//
// Dial names ARE the contract's parameter keys (expRate, w0, coreGain …) so `PlusParityDump.FromContract` loads any
// draw by reflection and a reader of MANIFEST.md / Appendix A finds the same words here; the meaning of each is in
// its [Tooltip]. Units: lengths are fractions of rMax (the blast's solved radius) or of the canvas half-size, times
// are fractions of the blast clock (0 = detonation, 1 = gone), angles are radians in the source's y-DOWN frame, so
// the form looks the same at PyrePlus's 64 px as at the source's 128 px.
//
// Fitted values (Appendix D §B 9–12): rMax / orgX / orgY are SOLVED at clip level by the cached pre-pass
// (`autoFit`, gen._place + gen._fit ported) — never per frame; `fill` stays a dial (the source's retry loop never
// fired on a published draw). eNorm / cGamma / aLo / aHi / aGamma are hand-set in the source and stay dials.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    /// One of the three piece populations (chunks / embers / motes): every field is the contract's `<kind><Field>`
    /// parameter (chunkN → n, chunkRhoLo → rhoLo …). Drawn as one folding box per population.
    [Serializable]
    public sealed class PlasmaPopulation
    {
        [Tooltip("How many pieces of this kind. A piece is fixed by a handful of numbers drawn once from the seed; its position at any time is a pure function of them.")]
        [Range(0, 200)] public int n = 26;
        [Tooltip("Innermost starting radius, as a fraction of the blast radius, at the moment the piece detaches from the front.")]
        [Range(0f, 1.2f)] public float rhoLo = 0.05f;
        [Tooltip("Outermost starting radius, as a fraction of the blast radius. Pieces spread between Rho lo and Rho hi by a shuffled square-root quantile.")]
        [Range(0f, 1.5f)] public float rhoHi = 1.0f;
        [Tooltip("Gaussian radius of a piece, as a fraction of the blast radius (per piece × 0.62..1.47).")]
        [Range(0.005f, 0.3f)] public float size = 0.079f;
        [Tooltip("Radius gained over a piece's own life (1 = doubles by the end; negative shrinks).")]
        [Range(-0.5f, 0.5f)] public float grow = 0.12f;
        [Tooltip("Elongation along the piece's own velocity at birth: 1 = round, 2 = twice as long as wide.")]
        [Range(0.5f, 3f)] public float stretch = 1.14f;
        [Tooltip("Extra stretch gained over the piece's own life — the smear lengthens as it flies.")]
        [Range(0f, 1.5f)] public float streak = 0.18f;
        [Tooltip("Random tilt of the smear away from the true velocity, radians (per piece × −1..1).")]
        [Range(0f, 1f)] public float tilt = 0.40f;
        [Tooltip("Half-width of the stratified per-piece speed range around Spd base. Wide = some pieces crawl and loiter in the middle while others race to the rim; narrow = everything arrives on one shell and the burst is a ring.")]
        [Range(0f, 1.2f)] public float spread = 0.88f;
        [Tooltip("Velocity ACROSS the ray (per piece × −1..1), as a fraction of the radial speed — takes each piece off its own ray so the smears do not all point at the centre.")]
        [Range(0f, 0.6f)] public float lat = 0.20f;
        [Tooltip("Brightness of this population (0 = off).")]
        [Range(0f, 2f)] public float amp = 1.30f;
        [Tooltip("Multiplier on Exp rate for this population's expansion easing (lower = the pieces decelerate later than the front).")]
        [Range(0.2f, 2f)] public float ease = 0.85f;
        [Tooltip("Linear share of the pieces' travel: above 0 they never fully stop, so the back half of the clip keeps moving.")]
        [Range(0f, 1f)] public float lin = 0.58f;
        [Tooltip("How strongly the FASTEST pieces are faded (by their speed above 1): keeps the far field wide without a lone bright stray in a corner.")]
        [Range(0f, 3f)] public float farFade = 0.45f;
        [Tooltip("Base speed relative to the front. Near 1 the slowest pieces (Spd base − Spread) crawl and fill the middle; far above 1 nothing is slow and the burst hollows into an annulus.")]
        [Range(0.3f, 2.5f)] public float spdBase = 1.12f;
        [Tooltip("Start of the birth window on the blast clock — the first piece detaches here.")]
        [Range(0f, 0.8f)] public float t0 = 0.02f;
        [Tooltip("End of the birth window on the blast clock. Births are stratified across it, so pieces trickle off steadily (the shedding).")]
        [Range(0.02f, 1f)] public float t1 = 0.18f;
        [Tooltip("A piece's lifetime as a fraction of the blast clock (per piece × 0.62..1.47), from its own birth.")]
        [Range(0.1f, 1.2f)] public float life = 0.80f;
        [Tooltip("Fade-in over this fraction of the piece's own life.")]
        [Range(0.01f, 0.5f)] public float ramp = 0.10f;
        [Tooltip("Decay exponent over the piece's own life: (1 − age)^fade.")]
        [Range(0.3f, 3f)] public float fade = 1.2f;
        [Tooltip("How much of the global Swirl this population follows (its angle turns by Swirl × this × time).")]
        [Range(0f, 2f)] public float swirl = 1.0f;
        [Tooltip("Per-piece random rotation rate, radians per unit clock (× −1..1 per piece).")]
        [Range(-1f, 1f)] public float spin = 0f;
        [Tooltip("0 or 1 = pieces spread evenly round the circle. 2 or more = they come off in this many angular GROUPS (the tongues of a plume shed from the tongues).")]
        [Range(0, 16)] public int clusters = 0;
        [ZUIShowIf("clusters", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16")]
        [Tooltip("Angular width of each cluster, radians.")]
        [Range(0f, 2f)] public float clusterW = 0.6f;

        public PlasmaPopulation() { }
        public PlasmaPopulation(int n, float rhoLo, float rhoHi, float size, float grow, float stretch, float streak, float tilt,
                                float spread, float lat, float amp, float ease, float lin, float farFade, float spdBase,
                                float t0, float t1, float life, float ramp, float fade, float swirl, float spin, int clusters, float clusterW)
        {
            this.n = n; this.rhoLo = rhoLo; this.rhoHi = rhoHi; this.size = size; this.grow = grow; this.stretch = stretch;
            this.streak = streak; this.tilt = tilt; this.spread = spread; this.lat = lat; this.amp = amp; this.ease = ease;
            this.lin = lin; this.farFade = farFade; this.spdBase = spdBase; this.t0 = t0; this.t1 = t1; this.life = life;
            this.ramp = ramp; this.fade = fade; this.swirl = swirl; this.spin = spin; this.clusters = clusters; this.clusterW = clusterW;
        }
    }

    [Serializable]
    [PlusFormInfo("Plasma Bloom", group: "Kiln/Energy Explosion", icon: "sparkle")]
    public sealed class PlasmaBloomForm : PlusForm, IPlusFieldPublisher, IPlusRampProbe
    {
        public override string DisplayName => "Plasma Bloom";
        public override string Description =>
            "A soft detonation of LIGHT (Kiln Energy Explosion, agent3_fork gen 5): a Gaussian shell that fractures "
            + "into free-flying pieces, with embers, motes, a core flash, a warm remnant and a beaded ghost ring, all "
            + "read off one energy field through two colour ramps. Direction comes from a cosine bias, a soft "
            + "half-plane gate and drift with a wake. Colour comes from the form's own Hue A / Hue B ramps, NOT the "
            + "layer Fill. Auto fit places and sizes the blast so nothing visible leaves the canvas. SWARM: off = "
            + "one auto-placed bloom; on = one bloom per swarm particle at that particle's position and life.";

        public enum Mode { Bloom, Plume }
        public enum LobeMode { Cos, Noise }

        // ── clock ──
        [Tooltip("Where on the blast's own clock the clip's FIRST frame sits (the source renders t = 1/30 first; 0 is the instant before detonation, which is empty).")]
        [Range(0f, 0.3f)] public float clockStart = 1f / 30f;
        [Tooltip("Where on the blast clock the clip's LAST frame sits. The source trims trailing frames whose peak alpha is under 22/255; detonate kept 27 of 30 = 0.9. Lower cuts the dim tail, 1.0 ends on an empty frame.")]
        [Range(0.2f, 1f)] public float clockEnd = 0.9f;

        // ── fit: where the source sits and how big the blast is ──
        [Tooltip("Solve placement and size from the field itself (the source's _place + _fit): the visible extent in each of the four directions is measured over the whole clip and the blast is scaled to the tightest edge, then shrunk until nothing above Border cap touches the frame. Off = hand-set Scale / Org X / Org Y.")]
        public bool autoFit = true;
        [ZUIShowIf("autoFit", "True")]
        [Tooltip("Share of the room (source-to-edge distance, per direction) the VISIBLE mass is fitted to. Above 1 lets the faint skirt cross the edge; Border cap still guards it.")]
        [Range(0.5f, 1.3f)] public float fill = 1.0f;
        [ZUIShowIf("autoFit", "True")]
        [Tooltip("What counts as visible for the fit: a fraction of the way up the alpha ramp (0 = any opacity at all, which would spend the frame on an invisible halo).")]
        [Range(0.1f, 0.9f)] public float fitVis = 0.45f;
        [ZUIShowIf("autoFit", "True")]
        [Tooltip("Highest alpha (0..255) allowed anywhere on the frame's border ring, over every frame — the containment promise.")]
        [Range(0, 40)] public int borderCap = 14;
        [ZUIShowIf("autoFit", "True")]
        [Tooltip("How much of the solved source offset to apply (room behind / room ahead = extent behind / extent ahead). 1 = a thrown blast starts near the back edge; 0 = always centred.")]
        [Range(0f, 1f)] public float autoOrg = 1.0f;
        [ZUIShowIf("autoFit", "False")]
        [Tooltip("Blast radius as a fraction of the canvas half-size (detonate solved 1.56 at 128 px).")]
        [Range(0.2f, 2.5f)] public float scale = 1.56f;
        [ZUIShowIf("autoFit", "False")]
        [Tooltip("Source position across the canvas, −1 = left edge, +1 = right edge (Auto fit off).")]
        [Range(-0.55f, 0.55f)] public float orgX = 0.114f;
        [ZUIShowIf("autoFit", "False")]
        [Tooltip("Source position down the canvas, −1 = top edge, +1 = bottom edge (Auto fit off).")]
        [Range(-0.55f, 0.55f)] public float orgY = 0.054f;

        // ── the shell (the coherent body) ──
        [Tooltip("How sharply the expansion decelerates: high = the slam, most of the distance in the first few frames.")]
        [Range(3f, 14f)] public float expRate = 7.6f;
        [Tooltip("Linear share of the shell's travel, so the front never fully stops.")]
        [Range(0f, 0.6f)] public float shellLin = 0.20f;
        [Tooltip("Shell thickness as a fraction of its CURRENT radius (so the body is a dot on frame 1 and thick later, not a constant-width ring).")]
        [Range(0.05f, 0.8f)] public float w0 = 0.34f;
        [Tooltip("Thickness gained per unit clock, as a fraction of the blast radius (negative = the body thins as it dissolves into pieces).")]
        [Range(-0.2f, 0.2f)] public float wGrow = -0.05f;

        // ── direction ──
        [Tooltip("Cosine bias of the reach: the shell (and every piece's speed and amount) is stretched this much toward Bias dir and shrunk away from it. Reach only — the GATE is what makes a blast read as thrown.")]
        [Range(0f, 1f)] public float biasAmt = 0f;
        [Tooltip("Direction of the bias, radians (0 = right, +π/2 = down).")]
        [Range(-3.1416f, 3.1416f)] public float biasDir = 0f;
        [Tooltip("Harmonic of the bias: 1 = one lobe (a thrown blast), 2 = two opposite lobes (a bipolar jet with a thin waist).")]
        [Range(1f, 3f)] public float biasK = 1f;
        [Tooltip("Soft half-plane gate: how much of the BACK of the blast is removed (the empty side is what reads as direction).")]
        [Range(0f, 1f)] public float halfAmt = 0f;
        [Tooltip("Direction the gate keeps, radians.")]
        [Range(-3.1416f, 3.1416f)] public float halfDir = 0f;
        [Tooltip("Width of the gate's transition: small = a WALL (flat face, hard corner — an impact), large = a CONE thinning smoothly to nothing behind.")]
        [Range(0.02f, 1.5f)] public float halfSoft = 0.35f;
        [Tooltip("Harmonic of the gate: 2 keeps two opposite jets and empties the waist between them.")]
        [Range(1f, 3f)] public float halfK = 1f;
        [Tooltip("Drift direction X (−1..1): the whole burst is CARRIED this way as it expands (0,0 = no drift).")]
        [Range(-1f, 1f)] public float driftX = 0f;
        [Tooltip("Drift direction Y (−1..1, +1 = down).")]
        [Range(-1f, 1f)] public float driftY = 0f;
        [Tooltip("How far the source travels over the clock, as a fraction of the blast radius.")]
        [Range(0f, 1.5f)] public float driftAmt = 0f;
        [Tooltip("Multiplier on Exp rate for the drift's easing.")]
        [Range(0.2f, 2f)] public float driftEase = 0.9f;
        [Tooltip("Linear share of the drift.")]
        [Range(0f, 1f)] public float driftLin = 0.5f;
        [Tooltip("THE WAKE: a piece is let go where the source was at its birth and lags behind by this share of the distance the source has travelled since — a trail, falling out of the staggered births.")]
        [Range(0f, 1f)] public float driftLag = 0f;

        // ── turbulence and warp ──
        [Tooltip("How much the polar noise modulates the body's brightness (ramps in over the first 7% of the clock so frame 1 is a smooth ball).")]
        [Range(0f, 1.2f)] public float turb = 0.82f;
        [Tooltip("Contrast of the noise (noise^power): higher eats darker holes between the billows.")]
        [Range(0.5f, 3f)] public float turbPow = 1.5f;
        [Tooltip("Noise cells around the angle (the texture wraps seamlessly on this period).")]
        [Range(2, 16)] public int ku = 7;
        [Tooltip("Noise cells per blast radius along the ray.")]
        [Range(0.5f, 6f)] public float kv = 2.6f;
        [Tooltip("Noise octaves.")]
        [Range(1, 6)] public int oct = 4;
        [Tooltip("How fast the billows are carried OUTWARD, in noise cells per unit clock.")]
        [Range(0f, 4f)] public float flow = 1.6f;
        [Tooltip("Rotation of the whole field with time, radians per unit clock at the blast radius (the noise, the pieces and the gate all turn) — the 'skew' look.")]
        [Range(-6f, 6f)] public float swirl = 0f;
        [Tooltip("Displacement of the front's RADIUS by a low-frequency angular noise — a ragged front whose parts travelled different distances, not a circle with brightness painted on.")]
        [Range(0f, 0.6f)] public float warp = 0.26f;
        [Tooltip("How much the warp grows over the clock.")]
        [Range(0f, 3f)] public float warpGrow = 1.7f;
        [Tooltip("Angular period of the warp noise (its own integer period, so it closes on itself).")]
        [Range(2, 12)] public int warpK = 5;

        // ── lobes / plume ──
        [Tooltip("Bloom = one shell. Plume = tongues are ADDED as a second, longer shell gated by angle (needs Lobes ≥ 2).")]
        public Mode mode = Mode.Bloom;
        [Tooltip("Angular lobes: 0 or 1 = none; 2 or more gates the shell (Bloom) or shapes the tongues (Plume).")]
        [Range(0, 16)] public int lobes = 0;
        [ZUIShowIf("lobes", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16")]
        [Tooltip("Cos = evenly spaced lobes. Noise = lobes cut by an angular noise (unequal, irregular).")]
        public LobeMode lobeMode = LobeMode.Cos;
        [ZUIShowIf("lobes", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16")]
        [Tooltip("How much the lobe gate darkens the shell between lobes (Bloom mode only).")]
        [Range(0f, 1f)] public float lobeAmp = 0f;
        [ZUIShowIf("lobes", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16")]
        [Tooltip("Sharpness of the lobes (gate^power): never flat-tops, so the silhouette curves everywhere.")]
        [Range(0.3f, 3f)] public float lobePow = 1f;
        [ZUIShowIf("lobes", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16")]
        [Tooltip("Angular phase of the lobes, radians.")]
        [Range(-3.1416f, 3.1416f)] public float lobePh = 0f;
        [ZUIShowIf("lobeMode", "Noise")]
        [Tooltip("Gain on the noise lobe gate before clipping to 0..1.")]
        [Range(0.5f, 2f)] public float gateGain = 1.25f;
        [ZUIShowIf("mode", "Plume")]
        [Tooltip("Brightness of the tongues.")]
        [Range(0f, 2f)] public float plumeAmp = 0f;
        [ZUIShowIf("mode", "Plume")]
        [Tooltip("How far the tongues reach, as a multiple of the shell radius.")]
        [Range(0.8f, 2f)] public float plumeReach = 1.0f;
        [ZUIShowIf("mode", "Plume")]
        [Tooltip("Radial width of a tongue's tip, in shell thicknesses.")]
        [Range(0.2f, 2f)] public float plumeW = 0.8f;
        [ZUIShowIf("mode", "Plume")]
        [Tooltip("Unequal tongue reach from the warp noise (equal tongues are a snowflake).")]
        [Range(0f, 1f)] public float plumeVary = 0f;

        // ── the fracture ──
        [Tooltip("When the body starts coming apart into chunks, on the blast clock.")]
        [Range(0f, 0.5f)] public float fracT0 = 0.02f;
        [Tooltip("When the body has finished coming apart (a smoothstep between the two, so it stops being able to hold itself over several frames rather than popping).")]
        [Range(0.02f, 0.8f)] public float fracT1 = 0.18f;
        [Tooltip("How much of the mass ends up as pieces: 1 = the body stops existing and the gaps between chunks go to zero.")]
        [Range(0f, 1f)] public float fracMax = 0.88f;

        // ── populations ──
        [Tooltip("The CHUNKS the body fractures into (crossfaded with the shell, chewed by the same noise).")]
        public PlasmaPopulation chunks = new PlasmaPopulation(26, 0.05f, 1.00f, 0.079f, 0.12f, 1.14f, 0.18f, 0.40f, 0.88f, 0.20f, 1.30f, 0.85f, 0.58f, 0.45f, 1.12f, 0.02f, 0.18f, 0.80f, 0.10f, 1.2f, 1.0f, 0f, 0, 0.6f);
        [Tooltip("The EMBERS: fast streaking spray thrown off the fracture (additive).")]
        public PlasmaPopulation embers = new PlasmaPopulation(32, 0.35f, 1.05f, 0.046f, 0.05f, 1.60f, 0.60f, 0.35f, 0.78f, 0.26f, 0.90f, 0.75f, 0.62f, 1.05f, 1.24f, 0.04f, 0.32f, 0.74f, 0.12f, 1.5f, 1.0f, 0f, 0, 0.6f);
        [Tooltip("The MOTES: many tiny round specks shed over most of the clip, the only thing on screen in the last third (additive).")]
        public PlasmaPopulation motes = new PlasmaPopulation(96, 0.20f, 1.00f, 0.024f, -0.16f, 1.05f, 0.25f, 0.00f, 0.94f, 0.34f, 0.95f, 0.70f, 0.66f, 1.45f, 1.30f, 0.03f, 0.64f, 0.58f, 0.16f, 1.5f, 1.0f, 0f, 0, 0.6f);

        // ── core, remnant, ghost ring ──
        [Tooltip("Brightness of the white-hot core flash at the source (additive, so it drives the middle past the top of the ramp).")]
        [Range(0f, 4f)] public float coreGain = 2.0f;
        [Tooltip("Core radius as a fraction of the blast radius.")]
        [Range(0.01f, 0.3f)] public float coreR = 0.055f;
        [Tooltip("How much the core grows with the front's travel.")]
        [Range(0f, 1f)] public float coreFollow = 0.30f;
        [Tooltip("Time constant of the core's decay on the blast clock (gone in three or four frames).")]
        [Range(0.01f, 0.3f)] public float coreTau = 0.040f;
        [Tooltip("Extra spike on the core at the instant of detonation (spatially bounded — never on the whole field).")]
        [Range(0f, 2f)] public float flash = 0.90f;
        [Tooltip("Time constant of the flash spike.")]
        [Range(0.01f, 0.3f)] public float flashTau = 0.05f;
        [Tooltip("A dim, slow core that arrives as the body leaves (it rides the fracture) and keeps the place the explosion came from warm. 0 = a hollow middle.")]
        [Range(0f, 1f)] public float remnant = 0.40f;
        [Tooltip("Remnant radius as a fraction of the blast radius.")]
        [Range(0.05f, 0.6f)] public float remnantR = 0.26f;
        [Tooltip("Time constant of the remnant's decay.")]
        [Range(0.05f, 2f)] public float remnantTau = 0.95f;
        [Tooltip("Brightness of the ghost ring — a second, faster, much thinner front, the only thing moving at a different speed (0 = none).")]
        [Range(0f, 1f)] public float ring2 = 0.45f;
        [Tooltip("How far the ring travels, as a multiple of the blast radius.")]
        [Range(0.5f, 2f)] public float ring2R = 1.25f;
        [Tooltip("The ring's life as a fraction of the blast clock.")]
        [Range(0.1f, 1f)] public float ring2Life = 0.50f;
        [Tooltip("Ring thickness as a fraction of the blast radius.")]
        [Range(0.01f, 0.2f)] public float ring2W = 0.05f;
        [Tooltip("Linear share of the ring's travel.")]
        [Range(0f, 1f)] public float ring2Lin = 0.26f;
        [Tooltip("How strongly the ring BEADS — an angular noise eats holes in it until only lumps travel on the old circle.")]
        [Range(0f, 1f)] public float ring2Bead = 1.0f;
        [Tooltip("Angular period of the bead noise.")]
        [Range(3, 20)] public int ring2K = 11;
        [Tooltip("When the beading starts, on the blast clock.")]
        [Range(0f, 0.5f)] public float beadT0 = 0.02f;
        [Tooltip("When the beading is at full strength.")]
        [Range(0.02f, 0.8f)] public float beadT1 = 0.16f;

        // ── gain: the dissipation curve ──
        [Tooltip("Rise time of the whole field, on the blast clock.")]
        [Range(0.005f, 0.2f)] public float rise = 0.022f;
        [Tooltip("How long the field holds at peak before the decay begins.")]
        [Range(0f, 0.3f)] public float hold = 0.05f;
        [Tooltip("Exponent of the FAST decay term (the blowout — over in three or four frames).")]
        [Range(1f, 6f)] public float tailFast = 3.6f;
        [Tooltip("Exponent of the SLOW decay term (the glow left behind — under 1 holds a legible tail on screen for most of the clip).")]
        [Range(0.1f, 1.5f)] public float tailSlow = 0.44f;
        [Tooltip("Share of the decay carried by the fast term (the rest by the slow one). Both reach exactly zero at the end of the clock.")]
        [Range(0f, 1f)] public float tailMix = 0.58f;

        // ── colour ──
        [Tooltip("The core ramp (position 0 = the faintest energy, 1 = white-hot). Presets: PlusRampPresets.PlasmaIon / Cryo / Volt / Toxin / Flare.")]
        public PlusRamp hueA = PlusRampPresets.PlasmaIon();
        [Tooltip("The rim ramp the colour crossfades into by RADIUS over the travelling front — the cool skirt round a hot core.")]
        public PlusRamp hueB = PlusRampPresets.PlasmaCryo();
        [Tooltip("How much of the rim ramp shows at the outer edge (0 = Hue A only).")]
        [Range(0f, 1f)] public float rimMix = 0.62f;
        [Tooltip("Where the crossfade to Hue B starts, as a multiple of the front's radius.")]
        [Range(0f, 1.5f)] public float rimLo = 0.40f;
        [Tooltip("Where the crossfade to Hue B is complete, as a multiple of the front's radius.")]
        [Range(0.2f, 2f)] public float rimHi = 1.08f;
        [Tooltip("Energy that reaches the TOP of the colour ramp (lower = hotter / whiter overall). Hand-set in the source, not fitted — the field's amplitude is fixed by the dials above.")]
        [Range(0.3f, 3f)] public float eNorm = 1.22f;
        [Tooltip("Gamma on the ramp coordinate: below 1 spends more of the ramp on the faint skirt.")]
        [Range(0.3f, 2f)] public float cGamma = 0.78f;
        [Tooltip("Energy at which alpha lifts off (everything below is fully transparent).")]
        [Range(0f, 0.3f)] public float aLo = 0.06f;
        [Tooltip("Energy at which alpha reaches 1 (a smoothstep between the two — a falloff that spans pixels, not a cutoff).")]
        [Range(0.1f, 1f)] public float aHi = 0.40f;
        [Tooltip("Gamma on the alpha ramp.")]
        [Range(0.3f, 2f)] public float aGamma = 0.85f;

        // ── swarm ──
        [PlusSwarmOnly]
        [Tooltip("Size of each swarm particle's bloom as a fraction of the solo blast's solved radius (the swarm's own size/depth shading multiplies it).")]
        [Range(0.1f, 1f)] public float swarmSize = 0.5f;

        // ── runtime ──
        static readonly PlusPrepassCache<PlasmaFit> Cache = new PlusPrepassCache<PlasmaFit>();
        [NonSerialized] PlasmaFit _fit;
        [NonSerialized] float _canvasPx;
        [NonSerialized] float[] _E, _scratch, _rn, _pr, _pg, _pb, _pa, _q, _a, _mix;
        [NonSerialized] float[] _dumpH, _dumpT, _dumpA, _dumpMix;

        /// The solved placement (for the parity log / a probe): rMax in canvas px, org as canvas-half fractions.
        public bool TryGetFit(out float rMax, out float orgX, out float orgY)
        {
            rMax = _fit?.rMax ?? 0f; orgX = _fit?.orgX ?? 0f; orgY = _fit?.orgY ?? 0f;
            return _fit != null;
        }

        /// life 0..1 → the blast clock t.
        float Clock(float life) => Mathf.Lerp(clockStart, Mathf.Max(clockEnd, clockStart + 1e-3f), Mathf.Clamp01(life));

        Color IPlusRampProbe.ProbeRamp(float t) => PlusShade.EvalStops(hueA?.stops, hueA?.space ?? PlusRampSpace.Srgb, t);

        void IPlusFieldPublisher.PublishFields(Action<string, float[]> sink)
        {
            if (_dumpH != null) sink("H", _dumpH);
            if (_dumpT != null) sink("ramp_t", _dumpT);
            if (_dumpA != null) sink("alpha", _dumpA);
            if (_dumpMix != null) sink("rim_mix", _dumpMix);
            _dumpH = _dumpT = _dumpA = _dumpMix = null;
        }

        public override void Render(in PlusFormCtx ctx, Color32[] target)
        {
            // The spec seed IS the Kiln seed (layer 0 of seed 1147 draws the contract's own populations and lattices);
            // further layers decorrelate by a large stride.
            int seed = unchecked(ctx.seed + ctx.layerSalt * 1000003);
            // The fit measures the clock at (i+1)/N like the source's frames: N = the frames the clip would have if it ran to t = 1.
            int fitFrames = Mathf.Clamp(Mathf.RoundToInt(ctx.frameCount / Mathf.Max(clockEnd, 0.05f)), 4, 128);
            _canvasPx = Mathf.Min(ctx.W, ctx.H);
            var self = this;
            _fit = Cache.Get(ctx, this, () => PlusPlasmaBloom.Solve(self, self._canvasPx, fitFrames, seed), fitFrames ^ seed);

            // 2× supersampled field, shaded per sample, box-averaged in float and quantised ONCE (the source's path).
            const int k = PlusPlasmaBloom.SS;
            int S = (int)_canvasPx * k, n = S * S;
            if (_E == null || _E.Length != n)
            {
                _E = new float[n]; _scratch = new float[n]; _rn = new float[n];
                _pr = new float[n]; _pg = new float[n]; _pb = new float[n]; _pa = new float[n];
                _q = _a = _mix = null;
            }
            bool dump = PlusFormDebug.FieldSink != null;
            if (dump && (_q == null || _q.Length != n)) { _q = new float[n]; _a = new float[n]; _mix = new float[n]; }
            float floorPx = 1f / k;
            var fit = _fit;

            if (ctx.swarm == null)
            {
                var G = PlusPlasmaBloom.Grid.Make(S, _canvasPx, fit.orgX, fit.orgY);
                float t = Clock(ctx.life);
                PlusPlasmaBloom.Energy(this, fit, fit.rMax, fit.orgX, fit.orgY, t, G, floorPx, _E, _scratch);
                PlusPlasmaBloom.RimPlane(this, fit.rMax, t, G, floorPx, _rn, min: false);
            }
            else
            {
                // One bloom per swarm particle: its position is the source (y-up canvas px → the source's y-down
                // half-size fractions), its own life is the blast clock, its size/brightness shading scales the bloom.
                Array.Clear(_E, 0, n);
                for (int i = 0; i < n; i++) _rn[i] = float.MaxValue;
                for (int s = 0; s < ctx.swarm.Length; s++)
                {
                    var sp = ctx.swarm[s];
                    if (sp.own < 0f || sp.own > 1f) continue;
                    float ox = Mathf.Clamp(sp.x / Mathf.Max(1, ctx.W) * 2f - 1f, -1.5f, 1.5f);
                    float oy = Mathf.Clamp(1f - sp.y / Mathf.Max(1, ctx.H) * 2f, -1.5f, 1.5f);
                    float rMax = fit.rMax * swarmSize * Mathf.Max(sp.sizeMul, 0.01f);
                    var G = PlusPlasmaBloom.Grid.Make(S, _canvasPx, ox, oy);
                    float t = Clock(sp.own);
                    PlusPlasmaBloom.Energy(this, fit, rMax, ox, oy, t, G, floorPx, _E, _scratch, sp.brightMul, accumulate: true);
                    PlusPlasmaBloom.RimPlane(this, rMax, t, G, floorPx, _rn, min: true);
                }
            }
            PlusPlasmaBloom.Shade(this, fit, S, _E, _rn, ctx.alpha, _pr, _pg, _pb, _pa, dump ? _q : null, _a, _mix);
            // planes are y-down → flipY reads the top row first so target row 0 is the bottom (renderer convention)
            PlusSupersample.Downsample(_pr, _pg, _pb, _pa, S, S, k, target, ctx.W, ctx.H, flipY: true);
            if (dump)
            {
                _dumpH = PlusPlasmaBloom.BoxDown(_E, S, k); _dumpT = PlusPlasmaBloom.BoxDown(_q, S, k);
                _dumpA = PlusPlasmaBloom.BoxDown(_a, S, k); _dumpMix = PlusPlasmaBloom.BoxDown(_mix, S, k);
                FlipY(_dumpH, S / k); FlipY(_dumpT, S / k); FlipY(_dumpA, S / k); FlipY(_dumpMix, S / k);
            }
            ApplyPixelModifiers(ctx, target);
        }

        /// Planes are computed y-down (the source's frame); the harness expects renderer buffers (y-up) and flips them itself.
        static void FlipY(float[] p, int s)
        {
            for (int y = 0; y < s / 2; y++)
                for (int x = 0; x < s; x++)
                {
                    int a = y * s + x, b = (s - 1 - y) * s + x;
                    (p[a], p[b]) = (p[b], p[a]);
                }
        }

        /// The layer's pixel modifiers, per lit canvas pixel, after the supersample (the heat-ramp forms' convention).
        void ApplyPixelModifiers(in PlusFormCtx ctx, Color32[] target)
        {
            if (ctx.pix == null || ctx.pix.Length == 0) return;
            int W = ctx.W, H = ctx.H;
            int pixHash = PyrePlusRenderer.Hash(ctx.seed, PyrePlusRenderer.ModParticleIndex, ctx.layerSalt, 0x1f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    var c = target[i];
                    if (c.a == 0) continue;
                    var col = new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
                    float alpha = c.a / 255f;
                    var info = new Laubrary.SpriteFx.PixelInfo(x, y, x + 0.5f, y + 0.5f, ctx.frameIndex, ctx.phase, ctx.life, pixHash, W, H);
                    bool keep = true;
                    for (int m = 0; m < ctx.pix.Length && keep; m++) keep = ctx.pix[m].ApplyPixel(ref col, ref alpha, info);
                    target[i] = keep
                        ? new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(col.r * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(col.g * 255f), 0, 255),
                                      (byte)Mathf.Clamp(Mathf.RoundToInt(col.b * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255))
                        : default;
                }
        }

        // ── contract loading, for the parity runs ──
        /// Apply a contract param by its key (`chunkN`, `hueA`, `mode` …) — the nested / string-typed ones the
        /// reflection mapper cannot reach. Returns false for an unknown key.
        public bool SetContractParam(string key, object value)
        {
            PlasmaPopulation pop = key.StartsWith("chunk") ? chunks : key.StartsWith("ember") ? embers : key.StartsWith("mote") ? motes : null;
            if (pop != null)
            {
                string sub = key.Substring(key.StartsWith("chunk") ? 5 : key.StartsWith("ember") ? 5 : 4);
                sub = char.ToLowerInvariant(sub[0]) + sub.Substring(1);
                var f = typeof(PlasmaPopulation).GetField(sub);
                if (f == null) return false;
                f.SetValue(pop, f.FieldType == typeof(int) ? (object)Convert.ToInt32(value) : (object)Convert.ToSingle(value));
                return true;
            }
            switch (key)
            {
                case "mode": mode = string.Equals(value?.ToString(), "plume", StringComparison.OrdinalIgnoreCase) ? Mode.Plume : Mode.Bloom; return true;
                case "lobeMode": lobeMode = string.Equals(value?.ToString(), "noise", StringComparison.OrdinalIgnoreCase) ? LobeMode.Noise : LobeMode.Cos; return true;
                case "hueA": hueA = PlusRampPresets.Plasma(value?.ToString()); return hueA != null;
                case "hueB": hueB = PlusRampPresets.Plasma(value?.ToString()); return hueB != null;
                case "rMax": scale = Convert.ToSingle(value) / PlusPlasmaBloom.SS / 64f; return true;   // contract rMax is in supersampled px of a 128 canvas
            }
            var fi = GetType().GetField(key);
            if (fi == null) return false;
            if (fi.FieldType == typeof(int)) fi.SetValue(this, Convert.ToInt32(value));
            else if (fi.FieldType == typeof(float)) fi.SetValue(this, Convert.ToSingle(value));
            else if (fi.FieldType == typeof(bool)) fi.SetValue(this, Convert.ToSingle(value) != 0f);
            else return false;
            return true;
        }
    }
}
