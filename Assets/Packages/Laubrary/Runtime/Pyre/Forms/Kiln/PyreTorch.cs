// PyreTorch — the Kiln "Flame / agent2" GROUNDED FLAME, generation 2 (torch / brazier / campfire), as pure functions.
//
// Source: D:/CODEZ/Kiln/projects/Flame/agents/agent2 (gen.py `heat_field` + `render`, flamelib.py; MANIFEST.md
// "generation 2 — the GROUNDED flame, ranged by HOW IT MOVES"). One program for all five published draws (emberbed,
// surge, barbs, curl, lash — calm → violent): an anchored envelope × periodic 3-D Perlin fBm evaluated on a 3×
// supersampled grid, a contour of (source + noise) rather than source × noise, plus discrete tongue and ember
// populations summed into the heat; colour = hard 7-band palette on a cooled copy of the heat, alpha = smoothstep on
// the raw heat; box-filtered in PREMULTIPLIED space. Every term is periodic in t = frame / frames by construction
// (sinusoids in 2πt, noise scrolled by exactly one lattice period, population ages (t + phase) mod 1).
//
// Component list (contract components.json), status in this port:
//   surge_envelope  PORTED   pulse_env: u = (t·n + ph) mod 1, sin(π·u^skew)^sharp — reach (pulse) and brightness (pulse_gain).
//   reach           PORTED   h_flame × breathe (two harmonics) × (1 + rvar·nreach) × (1 + pulse·surge); Hn = Y / hf.
//   sway            PORTED   sway·nw·climb^1.25 + lean·sin(2πt + lphase)·climb^1.6 (climb = clip(Hn, 0, 1.6)).
//   lash            PORTED   lash·climb^1.3·sin(2π·lash_k·t − lash_wave·climb + lash_ph) — the wave travels UP.
//   envelope        PORTED   root pinch, width w0·(1−Hn)^wp·root + wmin, exp(−(u²)^uexp·ku)·exp(−Hn^vexp·kv)·ceiling, foot bite.
//   curl_warp       PORTED   P = fbm potential; (ux, uy) = (dP/drow, −dP/dcol) by np.gradient, normalised by the RMS over
//                            the draw's FRAME (per frame, as the source — a standalone statistic, not a cross-frame fit);
//                            applied to the noise SAMPLING coordinates only.
//   noise_contour   PORTED   F = env + (abig·nbig + alick·nlick − bias)·shape^pexp·still; kinds fbm / ridged / billow.
//   bulge           PORTED   bulge·surge·famp·exp(−((Hn − pos)/bulge_w)²), pos riding up with the surge phase.
//   fuel_bed        PORTED   glow·shim·exp(−(X/glow_w)² − (max(Y − glow_y, 0)/glow_h)²).
//   tongues         PORTED   population from numpy default_rng(seed + 4231) — bit-exact via PyreNumpyRng; in flame space (Xw).
//   embers          PORTED   population from default_rng(seed + 977) — bit-exact; in world space (X).
//   ground_cut      PORTED   F ×= smoothstep(−0.5, 0.6, Y).
//   shade           PORTED   colour = band k where C ≥ threshold_k (hard, no interpolation); alpha = smoothstep(a_lo, a_hi, H).
//   resolve         PORTED   3× box mean in premultiplied space, un-premultiplied, rounded once (PyreSupersample.Downsample).
//   Nothing approximated, nothing dropped. The noise lattice (`pnoise3`: 12 edge gradients, quintic fade, periodic on
//   the lattice INDEX, perm = default_rng(seed).permutation(256) doubled) is `PyreFieldOps.GradientNoise3Periodic`.
//
// Frame of reference: everything runs in the draw's SOURCE px (X = px from the flame axis, Y = px above the fuel bed,
// y-UP), sampled on the supersampled canvas grid through u = canvas px per source px. At u = 1 with the frame
// letterboxed on the square canvas the sample set is exactly the contract's, so parity is exact; at any other u the
// same picture is magnified. The field is evaluated only inside the draw's own frame rect (what the source rendered).
using System;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre.Forms.Kiln
{
    /// What `fbm` does with each octave: signed (body sway, domain warp), ridged 1 − 2|n| (vertical creases become
    /// the licks), billow 2|n| − 1 (rounded lumps, the chunky curled cartoon fire).
    public enum TorchNoiseKind { Fbm, Ridged, Billow }

    /// A draw's own frame in source px: canvas W × H, the flame axis x and the fuel-bed row (y-down). The field is
    /// evaluated inside this rect (mapped through u) and the curl RMS is taken over it.
    public readonly struct TorchSource
    {
        public readonly int W, H;
        public readonly double cx, baseY;
        public TorchSource(int w, int h, double cx, double baseY) { W = w; H = h; this.cx = cx; this.baseY = baseY; }

        public static readonly TorchSource Emberbed = new TorchSource(124, 80, 62.0, 74.0);
        public static readonly TorchSource Surge = new TorchSource(64, 126, 32.0, 116.0);
        public static readonly TorchSource Barbs = new TorchSource(64, 118, 32.0, 108.0);
        public static readonly TorchSource Curl = new TorchSource(96, 112, 48.0, 104.0);
        public static readonly TorchSource Lash = new TorchSource(104, 124, 52.0, 114.0);
    }

    /// One flame to accumulate: the supersampled canvas size, the fuel-bed anchor in canvas px (y-DOWN), the scale u
    /// (canvas px per source px), a heat multiplier and the seed of its populations / lattice.
    public struct TorchFrame
    {
        public int W2, H2;          // supersampled canvas
        public double ox, oy;       // anchor: the flame axis × the fuel bed, canvas px, y-down
        public double u;            // canvas px per source px
        public double amp;          // heat multiplier (swarm brightness)
        public uint seed;           // the Kiln seed of this flame
        public TorchSource src;
    }

    /// The per-draw parameter set the program reads. Field names ARE the contract's keys (camelCased); the form's
    /// settings box carries the tooltips, this is the plain view the kernel takes.
    [Serializable]
    public sealed class TorchSettings
    {
        // ── envelope ──
        [Tooltip("Reach of the flame in SOURCE px — the reference every other px value here is relative to (the canvas height comes from the shared Height dial: u = Height × canvas / this).")]
        [ZUILabel("Flame reach (source px)")] [ZUIGroup("Shape", Tooltip = "The flame's silhouette: width, height and how each falls off.")]
        [Range(10f, 200f)] public float hFlame = 74f;
        [Tooltip("Half-width of the source at the base, px.")]
        [ZUILabel("Base half-width")] [ZUIGroup("Shape")]
        [Range(1f, 60f)] public ZUIValue w0 = new ZUIValue(7f);
        [Tooltip("How fast the width collapses with height: w = w0·(1 − Hn)^wp — low = a near-parallel column, high = a cone.")]
        [ZUILabel("Taper rate")] [ZUIGroup("Shape")]
        [Range(0.02f, 2f)] public ZUIValue wp = new ZUIValue(0.12f);
        [Tooltip("Width floor at the tip, px — a REAL fraction of w0, so the tip is as wide as a tongue (the needle fix).")]
        [ZUILabel("Tip width floor")] [ZUIGroup("Shape")]
        [Range(0.5f, 20f)] public ZUIValue wmin = new ZUIValue(2.6f);
        [Tooltip("Lateral falloff exponent: exp(−(u²)^uexp·ku), u = x / width.")]
        [ZUILabel("Side falloff shape")] [ZUIGroup("Shape", Advanced = true)]
        [Range(0.5f, 3f)] public ZUIValue uexp = new ZUIValue(1.55f);
        [Tooltip("Lateral falloff rate.")]
        [ZUILabel("Side falloff rate")] [ZUIGroup("Shape", Advanced = true)]
        [Range(0.2f, 4f)] public ZUIValue ku = new ZUIValue(1.75f);
        [Tooltip("Vertical falloff exponent: exp(−Hn^vexp·kv). Must stay ABOVE wp's effect so intensity dies faster than width (the needle rule).")]
        [ZUILabel("Height falloff shape")] [ZUIGroup("Shape", Advanced = true)]
        [Range(1f, 6f)] public ZUIValue vexp = new ZUIValue(3.6f);
        [Tooltip("Vertical falloff rate.")]
        [ZUILabel("Height falloff rate")] [ZUIGroup("Shape", Advanced = true)]
        [Range(0.2f, 5f)] public ZUIValue kv = new ZUIValue(2.2f);
        [Tooltip("Heat at the heart; the band thresholds are compared against a field that peaks near this.")]
        [ZUILabel("Core heat")] [ZUIGroup("Shape")]
        [Range(0.3f, 3f)] public ZUIValue gain = new ZUIValue(1.34f);
        [Tooltip("Height (fraction of the reach) where the ceiling starts eating the source — the flame ends in the ragged zone between Cap0 and Cap1 where the noise owns the contour.")]
        [ZUILabel("Ceiling start")] [ZUIGroup("Shape")]
        [Range(0.3f, 1.2f)] public ZUIValue cap0 = new ZUIValue(0.80f);
        [Tooltip("Height (fraction of the reach) where the ceiling has fully cut the source.")]
        [ZUILabel("Ceiling end")] [ZUIGroup("Shape")]
        [Range(0.8f, 2f)] public ZUIValue cap1 = new ZUIValue(1.22f);
        [Tooltip("Width at the very bottom relative to w0 (the root pinch: a torch grows out of something; a campfire sits flat at 1).")]
        [ZUILabel("Root width")] [ZUIGroup("Shape")]
        [Range(0.1f, 1f)] public ZUIValue rmin = new ZUIValue(0.55f);
        [Tooltip("Height (fraction of the reach) over which the root pinch opens to full width.")]
        [ZUILabel("Root opening height")] [ZUIGroup("Shape")]
        [Range(0.02f, 0.6f)] public ZUIValue rh = new ZUIValue(0.14f);
        [Tooltip("Noise cell size along x, px per lattice cell.")]
        [ZUILabel("Noise cell size X")] [ZUIGroup("Texture", Advanced = true)]
        [Range(2f, 40f)] public float xsc = 9f;
        [Tooltip("Noise cell size along y, px per lattice cell.")]
        [ZUILabel("Noise cell size Y")] [ZUIGroup("Texture", Advanced = true)]
        [Range(2f, 40f)] public float ysc = 15f;
        [Tooltip("Bite taken out of the heat in the bottom rows (the light-bar fix): the fuel is not the hottest part of a fire, the gas above it is. Kept shallow so it never becomes a dark shelf.")]
        [ZUILabel("Foot bite")] [ZUIGroup("Shape")]
        [Range(0f, 0.8f)] public ZUIValue foot = new ZUIValue(0.22f);
        [Tooltip("Height of the foot bite, px.")]
        [ZUILabel("Foot bite height")] [ZUIGroup("Shape", Advanced = true)]
        [Range(0.5f, 8f)] public ZUIValue footH = new ZUIValue(2.0f);

        // ── reach (how high, varying along the base and over time) ──
        [Tooltip("Reach-variation noise frequency along x (cells per Xsc): low Fry makes it almost a function of x alone, so neighbouring columns disagree and tongues of different heights stand side by side.")]
        [ZUILabel("Reach variation frequency X")] [ZUIGroup("Breathing", Tooltip = "Slow variation of the flame's own height over the loop.", Advanced = true)]
        [Range(0.1f, 4f)] public float frx = 1.20f;
        [Tooltip("Reach-variation noise frequency along y (cells per Ysc). The scroll period is round(12·Fry) lattice cells per loop.")]
        [ZUILabel("Reach variation frequency Y")] [ZUIGroup("Breathing", Advanced = true)]
        [Range(0.05f, 2f)] public float fry = 0.18f;
        [Tooltip("Amplitude of the reach variation (± fraction of the reach).")]
        [ZUILabel("Height variation")] [ZUIGroup("Breathing")]
        [Range(0f, 1f)] public ZUIValue rvar = new ZUIValue(0.32f);
        [Tooltip("Breathing of the whole body, first harmonic (± fraction of the reach, once per loop).")]
        [ZUILabel("Breathing (slow)")] [ZUIGroup("Breathing")]
        [Range(0f, 0.3f)] public ZUIValue breathe = new ZUIValue(0.07f);
        [Tooltip("Breathing second harmonic (twice per loop) so the pulse is not a metronome.")]
        [ZUILabel("Breathing (fast)")] [ZUIGroup("Breathing")]
        [Range(0f, 0.2f)] public ZUIValue breathe2 = new ZUIValue(0.04f);
        [Tooltip("Phase of the first breathing harmonic, radians.")]
        [ZUILabel("Breathing phase")] [ZUIGroup("Breathing", Advanced = true)]
        [Range(0f, 6.2832f)] public float bphase = 2.2f;

        // ── sway ──
        [Tooltip("Sway noise frequency along x (cells per Xsc).")]
        [ZUILabel("Sway noise frequency")] [ZUIGroup("Sway & lean", Tooltip = "Side-to-side motion of the flame.", Advanced = true)]
        [Range(0.1f, 3f)] public float fw = 0.70f;
        [Tooltip("Vertical anisotropy of the sway noise (its y frequency = Fw × this); the scroll period is round(12·Fw·Yaniso) cells per loop.")]
        [ZUILabel("Sway vertical stretch")] [ZUIGroup("Sway & lean", Advanced = true)]
        [Range(0.1f, 2f)] public float yaniso = 0.50f;
        [Tooltip("Noise-driven lateral displacement at the top, px — on the order of the flame's own WIDTH, not a fraction of it (a 2 px wiggle on an 11 px column reads as a jittering cone).")]
        [ZUILabel("Sway amount")] [ZUIGroup("Sway & lean")]
        [Range(0f, 12f)] public ZUIValue sway = new ZUIValue(2.6f);
        [Tooltip("Rigid lean at the top, px, once per loop.")]
        [ZUILabel("Lean amount")] [ZUIGroup("Sway & lean")]
        [Range(0f, 6f)] public ZUIValue lean = new ZUIValue(1.2f);
        [Tooltip("Phase of the lean, radians.")]
        [ZUILabel("Lean phase")] [ZUIGroup("Sway & lean", Advanced = true)]
        [Range(0f, 6.2832f)] public float lphase = 0.6f;

        // ── lash (the travelling whip) ──
        [Tooltip("Whip amplitude at the top, px. 0 = off. The wave travels UP the flame so the column takes an S and the tip cracks.")]
        [ZUILabel("Whip amount")] [ZUIGroup("Whip", Tooltip = "A travelling wave that whips the flame's tip — off by default.")]
        [Range(0f, 24f)] public ZUIValue lash = new ZUIValue(0f);
        [Tooltip("Whip cycles per loop — an integer so the wave closes with the loop.")]
        [ZUILabel("Whip cycles per loop")] [ZUIGroup("Whip", Advanced = true)]
        [Range(1, 4)] public int lashK = 1;
        [Tooltip("How far the whip's phase lags per unit height (radians per reach): the base is already returning while the top is still going out.")]
        [ZUILabel("Whip lag with height")] [ZUIGroup("Whip", Advanced = true)]
        [Range(0f, 8f)] public float lashWave = 2.6f;
        [Tooltip("Whip phase, radians.")]
        [ZUILabel("Whip phase")] [ZUIGroup("Whip", Advanced = true)]
        [Range(0f, 6.2832f)] public float lashPh = 0f;

        // ── surge (the fed torch) ──
        [Tooltip("Reach jump per surge (fraction of the reach). 0 = no surge (calm draws cost nothing here).")]
        [ZUILabel("Surge height jump")] [ZUIGroup("Surge", Tooltip = "A fed torch: periodic surges that jump the flame taller and hotter.")]
        [Range(0f, 1f)] public ZUIValue pulse = new ZUIValue(0f);
        [Tooltip("Surges per loop — an integer so the loop closes.")]
        [ZUILabel("Surges per loop")] [ZUIGroup("Surge", Advanced = true)]
        [Range(1, 6)] public int pulseN = 2;
        [Tooltip("Where in the surge the loop seam falls (fraction of a surge). Put it in the slow decay (≈0.7), not the fast attack, or the wrap lands on the steepest frame.")]
        [ZUILabel("Loop seam point")] [ZUIGroup("Surge", Advanced = true)]
        [Range(0f, 1f)] public float pulsePh = 0f;
        [Tooltip("Attack asymmetry: below 1 the peak comes early (≈28 % of the cycle at 0.55) — a jump then a settle, not a breath.")]
        [ZUILabel("Surge attack shape")] [ZUIGroup("Surge", Advanced = true)]
        [Range(0.2f, 1f)] public float pulseSkew = 0.55f;
        [Tooltip("Tail flattening: higher = the flame spends more of the cycle low and is only briefly tall.")]
        [ZUILabel("Surge tail shape")] [ZUIGroup("Surge", Advanced = true)]
        [Range(0.5f, 4f)] public float pulseSharp = 1.6f;
        [Tooltip("Brightness jump per surge (fraction of Gain) — fire that surges gets hotter as well as taller.")]
        [ZUILabel("Surge brightness jump")] [ZUIGroup("Surge")]
        [Range(0f, 1f)] public ZUIValue pulseGain = new ZUIValue(0f);
        [Tooltip("Heat of the lump that rides UP the column on each surge — what actually makes a surge legible (taller alone reads as a zoom).")]
        [ZUILabel("Rising heat lump")] [ZUIGroup("Surge")]
        [Range(0f, 1f)] public ZUIValue bulge = new ZUIValue(0f);
        [Tooltip("Height of the travelling lump, fraction of the reach.")]
        [ZUILabel("Lump height")] [ZUIGroup("Surge")]
        [Range(0.05f, 0.6f)] public ZUIValue bulgeW = new ZUIValue(0.22f);

        // ── curl warp ──
        [Tooltip("RMS displacement of the divergence-free warp on the noise sampling coordinates, px. 0 = off. Lobes ROLL over instead of wobbling (the warp cannot compress anything).")]
        [ZUILabel("Curl warp amount")] [ZUIGroup("Curl warp", Tooltip = "Rolls the flame's lobes over instead of just wobbling them.")]
        [Range(0f, 16f)] public float curl = 0f;
        [Tooltip("Curl potential frequency along x (cells per Xsc).")]
        [ZUILabel("Curl frequency X")] [ZUIGroup("Curl warp", Advanced = true)]
        [Range(0.1f, 3f)] public float curlX = 1.0f;
        [Tooltip("Curl potential frequency along y (cells per Ysc); scroll period round(12·CurlY) cells per loop.")]
        [ZUILabel("Curl frequency Y")] [ZUIGroup("Curl warp", Advanced = true)]
        [Range(0.05f, 2f)] public float curlY = 0.55f;

        // ── noise contour ──
        [Tooltip("Big-lobe noise frequency along x (cells per Xsc).")]
        [ZUILabel("Big-lobe frequency X")] [ZUIGroup("Texture", Advanced = true)]
        [Range(0.1f, 3f)] public float fbx = 0.75f;
        [Tooltip("Big-lobe noise frequency along y (cells per Ysc); scroll period round(12·Fby).")]
        [ZUILabel("Big-lobe frequency Y")] [ZUIGroup("Texture", Advanced = true)]
        [Range(0.05f, 2f)] public float fby = 0.50f;
        [Tooltip("Kind of the big-lobe noise (2 octaves).")]
        [ZUILabel("Big-lobe noise kind")] [ZUIGroup("Texture", Tooltip = "The flame's noisy contour and licking tongues of texture.")]
        public TorchNoiseKind bigKind = TorchNoiseKind.Fbm;
        [Tooltip("Amplitude of the big-lobe noise (added to the envelope: the silhouette is a CONTOUR of source + noise).")]
        [ZUILabel("Big-lobe strength")] [ZUIGroup("Texture")]
        [Range(0f, 1.5f)] public ZUIValue abig = new ZUIValue(0.30f);
        [Tooltip("Lick noise frequency along x (cells per Xsc).")]
        [ZUILabel("Lick frequency X")] [ZUIGroup("Texture", Advanced = true)]
        [Range(0.1f, 5f)] public float ftx = 1.60f;
        [Tooltip("Lick noise frequency along y (cells per Ysc); scroll period round(12·Fty).")]
        [ZUILabel("Lick frequency Y")] [ZUIGroup("Texture", Advanced = true)]
        [Range(0.05f, 2f)] public float fty = 0.55f;
        [Tooltip("Octaves of the lick noise (at most 4 with the 64-cell lattice).")]
        [ZUILabel("Lick noise octaves")] [ZUIGroup("Texture", Advanced = true)]
        [Range(1, 4)] public int toct = 2;
        [Tooltip("Kind of the lick noise: ridged carves the upper flame into separate tongues; billow makes rounded lumps.")]
        [ZUILabel("Lick noise kind")] [ZUIGroup("Texture")]
        public TorchNoiseKind turbKind = TorchNoiseKind.Ridged;
        [Tooltip("Amplitude of the lick noise.")]
        [ZUILabel("Lick strength")] [ZUIGroup("Texture")]
        [Range(0f, 1.5f)] public ZUIValue alick = new ZUIValue(0.32f);
        [Tooltip("Constant subtracted with the noise (field units) — erodes the contour so licks pinch off.")]
        [ZUILabel("Noise erosion bias")] [ZUIGroup("Texture", Advanced = true)]
        [Range(0f, 0.6f)] public ZUIValue bias = new ZUIValue(0.10f);
        [Tooltip("Exponent on the envelope that gates the noise (famp = shape^pexp): the noise lives where the source is, normalised so Gain moves colour, not shape.")]
        [ZUILabel("Noise gating shape")] [ZUIGroup("Texture", Advanced = true)]
        [Range(0.1f, 1.5f)] public ZUIValue pexp = new ZUIValue(0.44f);
        [Tooltip("Height (fraction of the reach) over which the noise fades in from the base — keeps the bottom rows quiet so the flame stays anchored.")]
        [ZUILabel("Quiet base height")] [ZUIGroup("Texture", Advanced = true)]
        [Range(0.01f, 0.6f)] public ZUIValue still = new ZUIValue(0.09f);

        // ── fuel bed ──
        [Tooltip("Heat of the wide low pool under the flame, shimmering slowly. 0 = off. Keep it a THIN strip: too much and the whole draw is one glowing loaf.")]
        [ZUILabel("Bed glow")] [ZUIGroup("Fuel bed glow", Tooltip = "A wide shimmering pool of heat under the flame.")]
        [Range(0f, 1f)] public ZUIValue glow = new ZUIValue(0.24f);
        [Tooltip("Half-width of the fuel-bed pool, px.")]
        [ZUILabel("Bed glow width")] [ZUIGroup("Fuel bed glow")]
        [Range(1f, 60f)] public ZUIValue glowW = new ZUIValue(6f);
        [Tooltip("Height of the fuel-bed pool above Glow Y, px.")]
        [ZUILabel("Bed glow height")] [ZUIGroup("Fuel bed glow")]
        [Range(0.5f, 20f)] public ZUIValue glowH = new ZUIValue(4.5f);
        [Tooltip("Height above the fuel bed where the pool starts, px.")]
        [ZUILabel("Bed glow position")] [ZUIGroup("Fuel bed glow")]
        [Range(-10f, 20f)] public ZUIValue glowY = new ZUIValue(0f);

        // ── tongues ──
        [Tooltip("Discrete licks alive in the population — flame-shaped tongues that peel off the flanks, lean outward as they climb and burn out. 0 = off.")]
        [ZUILabel("Tongue count")] [ZUIGroup("Tongues", Tooltip = "Discrete licks peeling off the flanks.")]
        [Range(0, 40)] public int tongues = 26;
        [Tooltip("Heat of a tongue at its peak.")]
        [ZUILabel("Tongue brightness")] [ZUIGroup("Tongues")]
        [Range(0f, 1.5f)] public ZUIValue tongueGain = new ZUIValue(0.46f);
        [Tooltip("Root distance from the axis, px (range, either side). MUST sit at the body's rim or the licks live inside the core and add nothing.")]
        [ZUILabel("Tongue root spread")] [ZUIGroup("Tongues")]
        [Range(0f, 40f)] public Vector2 tongueX = new Vector2(8f, 12f);
        [Tooltip("Root height above the fuel bed, px (range).")]
        [ZUILabel("Tongue root height")] [ZUIGroup("Tongues")]
        [Range(0f, 100f)] public Vector2 tongueY = new Vector2(8f, 62f);
        [Tooltip("How far a tongue climbs over its life, px (range).")]
        [ZUILabel("Tongue climb")] [ZUIGroup("Tongues")]
        [Range(0f, 60f)] public Vector2 tongueRise = new Vector2(9f, 22f);
        [Tooltip("How far a tongue peels away sideways over its life, px (range).")]
        [ZUILabel("Tongue peel-out")] [ZUIGroup("Tongues")]
        [Range(0f, 24f)] public Vector2 tongueOut = new Vector2(3f, 8f);
        [Tooltip("Tongue half-width, px (range).")]
        [ZUILabel("Tongue width")] [ZUIGroup("Tongues")]
        [Range(0.3f, 6f)] public Vector2 tongueW = new Vector2(1.2f, 2.1f);
        [Tooltip("Tongue length, px (range).")]
        [ZUILabel("Tongue length")] [ZUIGroup("Tongues")]
        [Range(1f, 16f)] public Vector2 tongueL = new Vector2(3.5f, 7f);
        [Tooltip("Tongue lifetime as a fraction of the loop (range).")]
        [ZUILabel("Tongue lifetime")] [ZUIGroup("Tongues")]
        [Range(0.05f, 1f)] public Vector2 tongueLife = new Vector2(0.30f, 0.60f);
        [Tooltip("How much a tongue's upper part leans outward as it ages (shear of its shape).")]
        [ZUILabel("Tongue lean")] [ZUIGroup("Tongues")]
        [Range(0f, 1.5f)] public ZUIValue tongueLean = new ZUIValue(0.60f);

        // ── embers ──
        [Tooltip("Sparks in the population — cooling fuel in world space, taking the same palette. 0 = off.")]
        [ZUILabel("Ember count")] [ZUIGroup("Embers", Tooltip = "Sparks that launch from the fuel bed and cool as they rise.")]
        [Range(0, 40)] public int embers = 9;
        [Tooltip("Half-width of the ember launch zone across the axis, px.")]
        [ZUILabel("Ember launch spread")] [ZUIGroup("Embers")]
        [Range(0f, 40f)] public float emberSpread = 6f;
        [Tooltip("How far an ember rises over its life, px (range).")]
        [ZUILabel("Ember rise")] [ZUIGroup("Embers")]
        [Range(0f, 140f)] public Vector2 emberRise = new Vector2(40f, 84f);
        [Tooltip("Sideways drift over an ember's life, px (× −1..1 per ember).")]
        [ZUILabel("Ember drift")] [ZUIGroup("Embers")]
        [Range(0f, 16f)] public float emberDrift = 3.2f;
        [Tooltip("Sideways wobble amplitude, px.")]
        [ZUILabel("Ember wobble")] [ZUIGroup("Embers")]
        [Range(0f, 5f)] public ZUIValue emberWobble = new ZUIValue(1.2f);
        [Tooltip("Ember radius, px (range).")]
        [ZUILabel("Ember size")] [ZUIGroup("Embers")]
        [Range(0.2f, 4f)] public Vector2 emberSize = new Vector2(0.7f, 1.4f);
        [Tooltip("Launch height above the fuel bed, px.")]
        [ZUILabel("Ember launch height")] [ZUIGroup("Embers")]
        [Range(0f, 80f)] public ZUIValue emberY0 = new ZUIValue(28f);
        [Tooltip("Heat of an ember at its peak.")]
        [ZUILabel("Ember brightness")] [ZUIGroup("Embers")]
        [Range(0f, 1.5f)] public ZUIValue emberGain = new ZUIValue(0.38f);

        // ── shade ──
        [Tooltip("How much the colour field is cooled with height: C = H·(1 − cool·clip(Hn, 0, 1.4)) — tips go red while the heart stays white, without the silhouette changing.")]
        [ZUILabel("Cooling with height")] [ZUIGroup("Colour", Tooltip = "The colour ramp and how the heat field maps onto it.")]
        [Range(0f, 0.6f)] public ZUIValue cool = new ZUIValue(0.30f);
        [Tooltip("Heat where opacity lifts off (field units). Below it the pixel is transparent.")]
        [ZUILabel("Fade-in point")] [ZUIGroup("Colour")]
        [Range(0f, 0.1f)] public ZUIValue aLo = new ZUIValue(0.013f);
        [Tooltip("Heat where opacity reaches 1 (field units). Alpha is a smoothstep between A Lo and A Hi on the raw heat — continuous, no threshold, no dither.")]
        [ZUILabel("Solid point")] [ZUIGroup("Colour")]
        [Range(0.005f, 0.3f)] public ZUIValue aHi = new ZUIValue(0.056f);
        [Tooltip("The cel palette: a free gradient sampled into hard bands (thresholds evenly divide 0..Ramp Top). Edit the ramp freely; the Bands count only changes the sampling resolution, so it never loses a colour you've picked. Presets: PyreRampPresets.TorchGradient(hot / ember / white / rim / gold).")]
        [ZUILabel("Colour ramp")] [ZUIGroup("Colour")]
        public ZuiGradient ramp = PyreRampPresets.TorchGradient("rim");
        [Tooltip("Field value of the ramp's top (position 1). Thresholds are compared against a heat that peaks near Gain, so this must sit ABOVE the peak or the whole core lands in the top band (the white-blob fix).")]
        [ZUILabel("Ramp ceiling")] [ZUIGroup("Colour")]
        [Range(0.3f, 2.5f)] public ZUIValue rampTop = new ZUIValue(1.15f);

        // ── the five published draws (gen.py VARIANTS over DEFAULTS; every value the contract's params.json) ──

        /// #001 emberbed — THE CALM END: a brazier burned down to coals, one slow breath per loop. Seed 811, 124 × 80, 26 f @ 12.
        public static TorchSettings Emberbed() => new TorchSettings
        {
            hFlame = 40f, w0 = new ZUIValue(30f), wp = new ZUIValue(0.52f), wmin = new ZUIValue(7f), uexp = new ZUIValue(1.60f), ku = new ZUIValue(1.30f), vexp = new ZUIValue(2.80f), kv = new ZUIValue(2.30f), gain = new ZUIValue(1.02f),
            cap0 = new ZUIValue(0.76f), cap1 = new ZUIValue(1.24f), rmin = new ZUIValue(1.00f), rh = new ZUIValue(0.20f), xsc = 13f, ysc = 17f, foot = new ZUIValue(0.26f), footH = new ZUIValue(2.2f),
            frx = 1.10f, fry = 0.18f, rvar = new ZUIValue(0.30f), fw = 0.45f, yaniso = 0.60f, sway = new ZUIValue(1.6f), lean = new ZUIValue(0.5f),
            fbx = 0.60f, fby = 0.42f, bigKind = TorchNoiseKind.Fbm, abig = new ZUIValue(0.50f),
            ftx = 1.90f, fty = 0.55f, toct = 3, turbKind = TorchNoiseKind.Ridged, alick = new ZUIValue(0.38f),
            bias = new ZUIValue(0.12f), pexp = new ZUIValue(0.44f), still = new ZUIValue(0.10f), cool = new ZUIValue(0.26f), aLo = new ZUIValue(0.012f), aHi = new ZUIValue(0.056f),
            ramp = PyreRampPresets.TorchGradient("ember"), rampTop = new ZUIValue(0.82f),
            breathe = new ZUIValue(0.075f), breathe2 = new ZUIValue(0.028f), bphase = 1.1f,
            glow = new ZUIValue(0.24f), glowW = new ZUIValue(24f), glowH = new ZUIValue(2.8f), glowY = new ZUIValue(0f),
            tongues = 13, tongueGain = new ZUIValue(0.40f), tongueX = new Vector2(2f, 32f), tongueY = new Vector2(16f, 28f),
            tongueRise = new Vector2(10f, 22f), tongueOut = new Vector2(0.5f, 3f), tongueW = new Vector2(1.6f, 3f),
            tongueL = new Vector2(4f, 8f), tongueLife = new Vector2(0.55f, 0.95f), tongueLean = new ZUIValue(0.30f),
            embers = 6, emberSpread = 18f, emberRise = new Vector2(20f, 44f), emberDrift = 2.4f,
            emberWobble = new ZUIValue(1.0f), emberSize = new Vector2(0.8f, 1.6f), emberY0 = new ZUIValue(10f), emberGain = new ZUIValue(0.34f),
            lash = new ZUIValue(0f), pulse = new ZUIValue(0f), pulseGain = new ZUIValue(0f), bulge = new ZUIValue(0f), curl = 0f,
        };

        /// #002 surge — THE PULSATING ONE: a torch being fed, two surges per loop. Seed 233, 64 × 126, 24 f @ 15.
        public static TorchSettings Surge() => new TorchSettings
        {
            hFlame = 62f, w0 = new ZUIValue(10f), wp = new ZUIValue(0.22f), wmin = new ZUIValue(3.4f), uexp = new ZUIValue(1.45f), ku = new ZUIValue(1.45f), vexp = new ZUIValue(3.20f), kv = new ZUIValue(2.20f), gain = new ZUIValue(1.30f),
            cap0 = new ZUIValue(0.80f), cap1 = new ZUIValue(1.28f), rmin = new ZUIValue(0.52f), rh = new ZUIValue(0.16f), xsc = 9.5f, ysc = 15f, foot = new ZUIValue(0.22f), footH = new ZUIValue(2.0f),
            frx = 1.25f, fry = 0.18f, rvar = new ZUIValue(0.26f), fw = 0.75f, yaniso = 0.50f, sway = new ZUIValue(3.6f), lean = new ZUIValue(1.6f),
            fbx = 0.80f, fby = 0.52f, bigKind = TorchNoiseKind.Fbm, abig = new ZUIValue(0.44f),
            ftx = 2.20f, fty = 0.70f, toct = 3, turbKind = TorchNoiseKind.Ridged, alick = new ZUIValue(0.46f),
            bias = new ZUIValue(0.13f), pexp = new ZUIValue(0.42f), still = new ZUIValue(0.10f), cool = new ZUIValue(0.30f), aLo = new ZUIValue(0.012f), aHi = new ZUIValue(0.058f),
            ramp = PyreRampPresets.TorchGradient("white"), rampTop = new ZUIValue(1.12f),
            breathe = new ZUIValue(0.05f), breathe2 = new ZUIValue(0.03f), bphase = 0.4f,
            pulse = new ZUIValue(0.34f), pulseN = 2, pulsePh = 0.70f, pulseGain = new ZUIValue(0.28f), bulge = new ZUIValue(0.34f), bulgeW = new ZUIValue(0.20f),
            glow = new ZUIValue(0.30f), glowW = new ZUIValue(7.5f), glowH = new ZUIValue(5f), glowY = new ZUIValue(0f),
            tongues = 9, tongueGain = new ZUIValue(0.42f), tongueX = new Vector2(11f, 16f), tongueY = new Vector2(30f, 56f),
            tongueRise = new Vector2(14f, 30f), tongueOut = new Vector2(2f, 6f), tongueW = new Vector2(1.3f, 2.2f),
            tongueL = new Vector2(4f, 8f), tongueLife = new Vector2(0.30f, 0.55f), tongueLean = new ZUIValue(0.45f),
            embers = 12, emberSpread = 5f, emberRise = new Vector2(48f, 96f), emberDrift = 3.0f,
            emberWobble = new ZUIValue(1.1f), emberSize = new Vector2(0.75f, 1.5f), emberY0 = new ZUIValue(30f), emberGain = new ZUIValue(0.42f),
            lash = new ZUIValue(0f), curl = 0f,
        };

        /// #003 barbs — THE LICKING ONE (0115), the torch: a near-parallel column that sheds barbs. Seed 115, 64 × 118, 24 f @ 16.
        /// These are also the field initialisers above (the canonical draw).
        public static TorchSettings Barbs() => new TorchSettings();

        /// #004 curl — THE ROLLING ONE (0139 / 0140): chunky lobes turning over. Seed 139, 96 × 112, 24 f @ 11.
        public static TorchSettings Curl() => new TorchSettings
        {
            hFlame = 68f, w0 = new ZUIValue(23f), wp = new ZUIValue(0.36f), wmin = new ZUIValue(7.5f), uexp = new ZUIValue(1.45f), ku = new ZUIValue(1.15f), vexp = new ZUIValue(3.00f), kv = new ZUIValue(2.05f), gain = new ZUIValue(1.20f),
            cap0 = new ZUIValue(0.80f), cap1 = new ZUIValue(1.30f), rmin = new ZUIValue(0.60f), rh = new ZUIValue(0.16f), xsc = 15f, ysc = 17f, foot = new ZUIValue(0.34f), footH = new ZUIValue(2.4f),
            frx = 0.95f, fry = 0.18f, rvar = new ZUIValue(0.26f), fw = 0.50f, yaniso = 0.80f, sway = new ZUIValue(4.4f), lean = new ZUIValue(2.0f),
            fbx = 0.44f, fby = 0.44f, bigKind = TorchNoiseKind.Billow, abig = new ZUIValue(0.56f),
            ftx = 1.10f, fty = 0.60f, toct = 3, turbKind = TorchNoiseKind.Billow, alick = new ZUIValue(0.24f),
            bias = new ZUIValue(0.12f), pexp = new ZUIValue(0.45f), still = new ZUIValue(0.07f), cool = new ZUIValue(0.16f), aLo = new ZUIValue(0.014f), aHi = new ZUIValue(0.054f),
            ramp = PyreRampPresets.TorchGradient("gold"), rampTop = new ZUIValue(1.18f),
            breathe = new ZUIValue(0.10f), breathe2 = new ZUIValue(0.05f), bphase = 2.6f,
            curl = 8.0f, curlX = 0.55f, curlY = 0.42f,
            glow = new ZUIValue(0.08f), glowW = new ZUIValue(9f), glowH = new ZUIValue(3f), glowY = new ZUIValue(0f),
            tongues = 11, tongueGain = new ZUIValue(0.40f), tongueX = new Vector2(15f, 23f), tongueY = new Vector2(14f, 46f),
            tongueRise = new Vector2(10f, 24f), tongueOut = new Vector2(2f, 6f), tongueW = new Vector2(2f, 3.4f),
            tongueL = new Vector2(4f, 7.5f), tongueLife = new Vector2(0.40f, 0.75f), tongueLean = new ZUIValue(0.70f),
            embers = 8, emberSpread = 13f, emberRise = new Vector2(30f, 64f), emberDrift = 4.6f,
            emberWobble = new ZUIValue(1.8f), emberSize = new Vector2(1f, 2f), emberY0 = new ZUIValue(24f), emberGain = new ZUIValue(0.38f),
            lash = new ZUIValue(0f), pulse = new ZUIValue(0f), pulseGain = new ZUIValue(0f), bulge = new ZUIValue(0f),
        };

        /// #005 lash — THE VIOLENT END: a fire blown about, the whole column whips. Seed 707, 104 × 124, 22 f @ 20.
        public static TorchSettings Lash() => new TorchSettings
        {
            hFlame = 72f, w0 = new ZUIValue(13f), wp = new ZUIValue(0.20f), wmin = new ZUIValue(4.2f), uexp = new ZUIValue(1.35f), ku = new ZUIValue(1.40f), vexp = new ZUIValue(3.30f), kv = new ZUIValue(2.10f), gain = new ZUIValue(1.38f),
            cap0 = new ZUIValue(0.78f), cap1 = new ZUIValue(1.32f), rmin = new ZUIValue(0.60f), rh = new ZUIValue(0.14f), xsc = 9f, ysc = 13f, foot = new ZUIValue(0.22f), footH = new ZUIValue(2.0f),
            frx = 1.45f, fry = 0.18f, rvar = new ZUIValue(0.38f), fw = 0.85f, yaniso = 0.45f, sway = new ZUIValue(5.0f), lean = new ZUIValue(1.0f),
            fbx = 0.85f, fby = 0.52f, bigKind = TorchNoiseKind.Fbm, abig = new ZUIValue(0.50f),
            ftx = 2.30f, fty = 0.72f, toct = 4, turbKind = TorchNoiseKind.Ridged, alick = new ZUIValue(0.50f),
            bias = new ZUIValue(0.14f), pexp = new ZUIValue(0.40f), still = new ZUIValue(0.10f), cool = new ZUIValue(0.24f), aLo = new ZUIValue(0.012f), aHi = new ZUIValue(0.058f),
            ramp = PyreRampPresets.TorchGradient("hot"), rampTop = new ZUIValue(0.88f),
            breathe = new ZUIValue(0.09f), breathe2 = new ZUIValue(0.05f), bphase = 0.9f,
            lash = new ZUIValue(13.0f), lashK = 1, lashWave = 3.4f, lashPh = 0.7f,
            glow = new ZUIValue(0.30f), glowW = new ZUIValue(9f), glowH = new ZUIValue(5f), glowY = new ZUIValue(0f),
            tongues = 16, tongueGain = new ZUIValue(0.42f), tongueX = new Vector2(14f, 21f), tongueY = new Vector2(16f, 60f),
            tongueRise = new Vector2(12f, 30f), tongueOut = new Vector2(5f, 13f), tongueW = new Vector2(1.2f, 2.3f),
            tongueL = new Vector2(3.5f, 7f), tongueLife = new Vector2(0.25f, 0.50f), tongueLean = new ZUIValue(0.85f),
            embers = 20, emberSpread = 10f, emberRise = new Vector2(44f, 92f), emberDrift = 7.0f,
            emberWobble = new ZUIValue(2.2f), emberSize = new Vector2(0.7f, 1.5f), emberY0 = new ZUIValue(26f), emberGain = new ZUIValue(0.42f),
            pulse = new ZUIValue(0f), pulseGain = new ZUIValue(0f), bulge = new ZUIValue(0f), curl = 0f,
        };

        /// The envelopes above resolved at one layer life (slots 10 onward; the five boxes share them — only the active one
        /// is resolved) — what `Accumulate` and the form's shade read.
        public struct Live { public float w0, wp, wmin, uexp, ku, vexp, kv, gain, cap0, cap1, rmin, rh, foot, footH, rvar, breathe, breathe2, sway, lean, lash, pulse, pulseGain, bulge, bulgeW, abig, alick, bias, pexp, still, glow, glowW, glowH, glowY, tongueGain, tongueLean, emberWobble, emberY0, emberGain, cool, aLo, aHi, rampTop; }
        [NonSerialized] public Live live;
        [NonSerialized] public bool liveResolved;
        /// A caller driving `Accumulate` outside a frame (a test, a probe) gets the Static values.
        public void EnsureLive() { if (!liveResolved) ResolveStatic(); }
        public void Resolve(in PyreFormPrepareCtx ctx) { liveResolved = true; live = new Live { w0 = ctx.Eval(w0, 10), wp = ctx.Eval(wp, 11), wmin = ctx.Eval(wmin, 12), uexp = ctx.Eval(uexp, 13), ku = ctx.Eval(ku, 14), vexp = ctx.Eval(vexp, 15), kv = ctx.Eval(kv, 16), gain = ctx.Eval(gain, 17), cap0 = ctx.Eval(cap0, 18), cap1 = ctx.Eval(cap1, 19), rmin = ctx.Eval(rmin, 20), rh = ctx.Eval(rh, 21), foot = ctx.Eval(foot, 22), footH = ctx.Eval(footH, 23), rvar = ctx.Eval(rvar, 24), breathe = ctx.Eval(breathe, 25), breathe2 = ctx.Eval(breathe2, 26), sway = ctx.Eval(sway, 27), lean = ctx.Eval(lean, 28), lash = ctx.Eval(lash, 29), pulse = ctx.Eval(pulse, 30), pulseGain = ctx.Eval(pulseGain, 31), bulge = ctx.Eval(bulge, 32), bulgeW = ctx.Eval(bulgeW, 33), abig = ctx.Eval(abig, 34), alick = ctx.Eval(alick, 35), bias = ctx.Eval(bias, 36), pexp = ctx.Eval(pexp, 37), still = ctx.Eval(still, 38), glow = ctx.Eval(glow, 39), glowW = ctx.Eval(glowW, 40), glowH = ctx.Eval(glowH, 41), glowY = ctx.Eval(glowY, 42), tongueGain = ctx.Eval(tongueGain, 43), tongueLean = ctx.Eval(tongueLean, 44), emberWobble = ctx.Eval(emberWobble, 45), emberY0 = ctx.Eval(emberY0, 46), emberGain = ctx.Eval(emberGain, 47), cool = ctx.Eval(cool, 48), aLo = ctx.Eval(aLo, 49), aHi = ctx.Eval(aHi, 50), rampTop = ctx.Eval(rampTop, 51) }; }
        /// The Static values (no renderer funnel) — for a caller that drives `Accumulate` outside a frame (a test, a probe).
        public void ResolveStatic() { liveResolved = true; live = new Live { w0 = w0.staticValue, wp = wp.staticValue, wmin = wmin.staticValue, uexp = uexp.staticValue, ku = ku.staticValue, vexp = vexp.staticValue, kv = kv.staticValue, gain = gain.staticValue, cap0 = cap0.staticValue, cap1 = cap1.staticValue, rmin = rmin.staticValue, rh = rh.staticValue, foot = foot.staticValue, footH = footH.staticValue, rvar = rvar.staticValue, breathe = breathe.staticValue, breathe2 = breathe2.staticValue, sway = sway.staticValue, lean = lean.staticValue, lash = lash.staticValue, pulse = pulse.staticValue, pulseGain = pulseGain.staticValue, bulge = bulge.staticValue, bulgeW = bulgeW.staticValue, abig = abig.staticValue, alick = alick.staticValue, bias = bias.staticValue, pexp = pexp.staticValue, still = still.staticValue, glow = glow.staticValue, glowW = glowW.staticValue, glowH = glowH.staticValue, glowY = glowY.staticValue, tongueGain = tongueGain.staticValue, tongueLean = tongueLean.staticValue, emberWobble = emberWobble.staticValue, emberY0 = emberY0.staticValue, emberGain = emberGain.staticValue, cool = cool.staticValue, aLo = aLo.staticValue, aHi = aHi.staticValue, rampTop = rampTop.staticValue }; }
    }

    /// Per-instance scratch the kernel reuses frame to frame (a form renders one frame per thread on its own clone).
    public sealed class TorchScratch
    {
        public float[] F, Hf, Xw, P, Ux, Uy;      // rect planes
        public double[] X, Y;                     // source coords per rect column / row
        public int rw, rh;

        public void Ensure(int w, int h, bool curl)
        {
            int n = w * h;
            if (F == null || F.Length < n) { F = new float[n]; Hf = new float[n]; Xw = new float[n]; }
            if (curl && (P == null || P.Length < n)) { P = new float[n]; Ux = new float[n]; Uy = new float[n]; }
            if (X == null || X.Length < w) X = new double[w];
            if (Y == null || Y.Length < h) Y = new double[h];
            rw = w; rh = h;
        }
    }

    public static class PyreTorch
    {
        public const int SS = 3;                 // supersample factor
        public const int PX = 64, PY = 12, PZ = 6; // lattice periods: x fixed, y derived per call (round(PY·ky)), z scrolled PZ per loop

        /// numpy `default_rng(seed).permutation(256)`, doubled to 512 entries — the lattice hash of one flame.
        public static int[] PermTable(uint seed)
        {
            var rng = new PyreNumpyRng(seed);
            var a = new float[256];
            for (int i = 0; i < 256; i++) a[i] = i;
            rng.Shuffle(a);
            var perm = new int[512];
            for (int i = 0; i < 256; i++) perm[i] = perm[i + 256] = (int)a[i];
            return perm;
        }

        /// The scroll distance AND the lattice period of a y-frequency: the same integer, derived from the frequency
        /// (round half-even, as Python's round). Scaling the coordinate by ky while keeping a fixed period would land
        /// the field somewhere else after one loop — a visible hitch once per loop.
        public static int Sy(double ky) => Math.Max(1, (int)Math.Round(PY * ky, MidpointRounding.ToEven));

        /// `nz`: rising noise that is exactly periodic in t.
        public static double Nz(double x, double y, double t, double xsc, double ysc, double kx, double ky, double zoff,
                                int octaves, TorchNoiseKind kind, int[] perm)
        {
            int sy = Sy(ky);
            return Fbm(x / xsc * kx, y / ysc * ky - sy * t, zoff + PZ * t, PX, sy, PZ, perm, octaves, kind);
        }

        /// `fbm`: sum of octaves, each with doubled frequency and doubled periods, gain 0.5, divided by the amplitude sum.
        public static double Fbm(double x, double y, double z, int px, int py, int pz, int[] perm, int octaves, TorchNoiseKind kind)
        {
            double total = 0, amp = 1, norm = 0; int f = 1;
            for (int o = 0; o < octaves; o++)
            {
                double n = PyreFieldOps.GradientNoise3Periodic(x * f, y * f, z * f, px * f, py * f, pz * f, perm);
                if (kind == TorchNoiseKind.Ridged) n = 1.0 - Math.Abs(n) * 2.0;
                else if (kind == TorchNoiseKind.Billow) n = Math.Abs(n) * 2.0 - 1.0;
                total += n * amp; norm += amp; amp *= 0.5; f *= 2;
            }
            return total / norm;
        }

        public static double SmoothStep(double e0, double e1, double x)
        {
            double t = (x - e0) / Math.Max(e1 - e0, 1e-9);
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return t * t * (3.0 - 2.0 * t);
        }

        /// `pulse_env`: a SURGE — fast attack, slow decay, exactly 0 at both ends of its own period, n per loop.
        public static double PulseEnv(double t, int n, double phase, double skew, double sharp)
        {
            double u = Mod1(t * n + phase);
            return Math.Pow(Math.Sin(Math.PI * Math.Pow(u, skew)), sharp);
        }

        static double Mod1(double v) { double m = v % 1.0; return m < 0 ? m + 1.0 : m; }
        static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        /// The tongue population, drawn once from default_rng(seed + 4231) in the source's order.
        public struct Tongue { public double x0, y0, phase, rise, outX, w, l, life, hot; }
        public static Tongue[] TongueTable(TorchSettings s, uint seed)
        {
            int n = s.tongues; if (n <= 0) return null;
            var rng = new PyreNumpyRng(seed + 4231u);
            var T = new Tongue[n];
            var side = new double[n];
            for (int k = 0; k < n; k++) side[k] = rng.NextDouble() < 0.5 ? -1.0 : 1.0;
            for (int k = 0; k < n; k++) T[k].x0 = side[k] * rng.Uniform(s.tongueX.x, s.tongueX.y);
            for (int k = 0; k < n; k++) T[k].y0 = rng.Uniform(s.tongueY.x, s.tongueY.y);
            for (int k = 0; k < n; k++) T[k].phase = rng.Uniform(0, 1);
            for (int k = 0; k < n; k++) T[k].rise = rng.Uniform(s.tongueRise.x, s.tongueRise.y);
            for (int k = 0; k < n; k++) T[k].outX = side[k] * rng.Uniform(s.tongueOut.x, s.tongueOut.y);
            for (int k = 0; k < n; k++) T[k].w = rng.Uniform(s.tongueW.x, s.tongueW.y);
            for (int k = 0; k < n; k++) T[k].l = rng.Uniform(s.tongueL.x, s.tongueL.y);
            for (int k = 0; k < n; k++) T[k].life = rng.Uniform(s.tongueLife.x, s.tongueLife.y);
            for (int k = 0; k < n; k++) T[k].hot = rng.Uniform(0.7, 1.0);
            return T;
        }

        /// The ember population, drawn once from default_rng(seed + 977) in the source's order.
        public struct Ember { public double x0, phase, rise, drift, wob, size, life, hot; }
        public static Ember[] EmberTable(TorchSettings s, uint seed)
        {
            int n = s.embers; if (n <= 0) return null;
            var rng = new PyreNumpyRng(seed + 977u);
            var E = new Ember[n];
            for (int k = 0; k < n; k++) E[k].x0 = rng.Uniform(-s.emberSpread, s.emberSpread);
            for (int k = 0; k < n; k++) E[k].phase = rng.Uniform(0, 1);
            for (int k = 0; k < n; k++) E[k].rise = rng.Uniform(s.emberRise.x, s.emberRise.y);
            for (int k = 0; k < n; k++) E[k].drift = rng.Uniform(-1, 1) * s.emberDrift;
            for (int k = 0; k < n; k++) E[k].wob = rng.Uniform(0.6, 2.2);
            for (int k = 0; k < n; k++) E[k].size = rng.Uniform(s.emberSize.x, s.emberSize.y);
            for (int k = 0; k < n; k++) E[k].life = rng.Uniform(0.45, 0.95);
            for (int k = 0; k < n; k++) E[k].hot = rng.Uniform(0.55, 1.0);
            return E;
        }

        /// The supersample index range [i0, i1) whose centres (i + 0.5)/SS fall in [lo, hi) canvas px, clipped to [0, n).
        static void Span(double lo, double hi, int n, out int i0, out int i1)
        {
            i0 = (int)Math.Ceiling(lo * SS - 0.5); i1 = (int)Math.Ceiling(hi * SS - 0.5);
            if (i0 < 0) i0 = 0; if (i1 > n) i1 = n;
        }

        /// One frame of one flame: `heat_field` over the flame's frame rect, ADDED into the supersampled heat plane
        /// `Fp` (= max(F, 0) × amp) and colour plane `C` (= Fp × (1 − cool·clip(Hn, 0, 1.4))), both W2 × H2, y-down.
        public static void Accumulate(TorchSettings s, in TorchFrame fr, double t, TorchScratch sc, float[] Fp, float[] C)
        {
            s.EnsureLive();
            var src = fr.src; double u = fr.u;
            Span(fr.ox - src.cx * u, fr.ox + (src.W - src.cx) * u, fr.W2, out int i0, out int i1);
            Span(fr.oy - src.baseY * u, fr.oy + (src.H - src.baseY) * u, fr.H2, out int j0, out int j1);
            int rw = i1 - i0, rh = j1 - j0;
            if (rw <= 0 || rh <= 0) return;
            bool curlOn = s.curl > 0f;
            sc.Ensure(rw, rh, curlOn);
            var X = sc.X; var Y = sc.Y;
            for (int i = 0; i < rw; i++) X[i] = ((i0 + i + 0.5) / SS - fr.ox) / u;
            for (int j = 0; j < rh; j++) Y[j] = (fr.oy - (j0 + j + 0.5) / SS) / u;
            var perm = PermTable(fr.seed);

            // ── per-frame scalars ──
            double surge = PulseEnv(t, s.pulseN, s.pulsePh, s.pulseSkew, s.pulseSharp);
            double breathe = 1.0 + s.live.breathe * Math.Sin(2 * Math.PI * t + s.bphase) + s.live.breathe2 * Math.Sin(4 * Math.PI * t + 1.7);
            double leanNow = s.live.lean * Math.Sin(2 * Math.PI * t + s.lphase);
            double hfBase = s.hFlame * breathe * (1.0 + s.live.pulse * surge);
            double envGain = s.live.gain * (1.0 + s.live.pulseGain * surge);
            double bulgePos = -0.15 + 1.35 * Mod1(t * s.pulseN + s.pulsePh);
            double shim = 1.0 + 0.16 * Math.Sin(2 * Math.PI * t * 2.0 + 0.9) + 0.10 * Math.Sin(2 * Math.PI * t + 2.2);
            double xsc = s.xsc, ysc = s.ysc, fwy = (double)s.fw * s.yaniso;

            // ── the curl potential and its divergence-free gradient, normalised over the frame (np.gradient semantics) ──
            float[] F = sc.F, Hf = sc.Hf, Xw = sc.Xw, P = sc.P, Ux = sc.Ux, Uy = sc.Uy;
            if (curlOn)
            {
                for (int j = 0; j < rh; j++)
                    for (int i = 0; i < rw; i++)
                        P[j * rw + i] = (float)Nz(X[i], Y[j], t, xsc, ysc, s.curlX, s.curlY, 63.0, 2, TorchNoiseKind.Fbm, perm);
                double sum = 0;
                for (int j = 0; j < rh; j++)
                    for (int i = 0; i < rw; i++)
                    {
                        int k = j * rw + i;
                        // dP/drow (axis 0) → ux ; −dP/dcol (axis 1) → uy ; central inside, one-sided at the frame edge
                        float dr = rh < 2 ? 0f : j == 0 ? P[k + rw] - P[k] : j == rh - 1 ? P[k] - P[k - rw] : (P[k + rw] - P[k - rw]) * 0.5f;
                        float dc = rw < 2 ? 0f : i == 0 ? P[k + 1] - P[k] : i == rw - 1 ? P[k] - P[k - 1] : (P[k + 1] - P[k - 1]) * 0.5f;
                        Ux[k] = dr; Uy[k] = -dc;
                        sum += (double)dr * dr + (double)dc * dc;
                    }
                double m = Math.Sqrt(sum / (rw * rh));
                if (m == 0) m = 1.0;
                float sc1 = (float)(s.curl / m);
                for (int k = 0; k < rw * rh; k++) { Ux[k] *= sc1; Uy[k] *= sc1; }
            }

            // ── the envelope, the motion and the noise contour ──
            for (int j = 0; j < rh; j++)
            {
                double y = Y[j];
                double footMul = s.live.foot > 0f ? 1.0 - s.live.foot * Math.Exp(-Sq(Math.Max(y, 0.0) / s.live.footH)) : 1.0;
                double glowRow = s.live.glow > 0f ? Sq(Math.Max(y - s.live.glowY, 0.0) / s.live.glowH) : 0.0;
                for (int i = 0; i < rw; i++)
                {
                    int k = j * rw + i;
                    double x = X[i];
                    double nreach = Nz(x, y, t, xsc, ysc, s.frx, s.fry, 21.0, 2, TorchNoiseKind.Fbm, perm);
                    double hf = hfBase * (1.0 + s.live.rvar * nreach);
                    double Hn = y / hf;
                    double nw = Nz(x, y, t, xsc, ysc, s.fw, fwy, 0.0, 2, TorchNoiseKind.Fbm, perm);
                    double climb = Clamp(Hn, 0.0, 1.6);
                    double sway = s.live.sway * nw * Math.Pow(climb, 1.25) + leanNow * Math.Pow(climb, 1.6);
                    if (s.live.lash > 0f)
                        sway += s.live.lash * Math.Pow(climb, 1.3) * Math.Sin(2 * Math.PI * s.lashK * t - s.lashWave * climb + s.lashPh);
                    double xw = x - sway;

                    double root = s.live.rmin + (1.0 - s.live.rmin) * SmoothStep(0.0, s.live.rh, Hn);
                    double w = s.live.w0 * Math.Pow(Clamp(1.0 - Hn, 0.0, 1.0), s.live.wp) * root + s.live.wmin;
                    double uu = xw / w;
                    double ceiling = 1.0 - SmoothStep(s.live.cap0, s.live.cap1, Hn);
                    double shape = Math.Exp(-Math.Pow(uu * uu, s.live.uexp) * s.live.ku) * Math.Exp(-Math.Pow(Math.Max(Hn, 0.0), s.live.vexp) * s.live.kv) * ceiling;
                    shape *= footMul;
                    double env = envGain * shape;

                    double xn = xw, yn = y;
                    if (curlOn) { xn += Ux[k]; yn += Uy[k]; }
                    double nbig = Nz(xn, yn, t, xsc, ysc, s.fbx, s.fby, 5.0, 2, s.bigKind, perm);
                    double nlick = Nz(xn, yn, t, xsc, ysc, s.ftx, s.fty, 11.0, s.toct, s.turbKind, perm);
                    double famp = Math.Pow(shape, s.live.pexp);
                    double still = Clamp(Hn / s.live.still, 0.0, 1.0);
                    double f = env + (s.live.abig * nbig + s.live.alick * nlick - s.live.bias) * famp * still;
                    if (s.live.bulge > 0f) f += s.live.bulge * surge * famp * Math.Exp(-Sq((Hn - bulgePos) / s.live.bulgeW));
                    if (s.live.glow > 0f) f += s.live.glow * shim * Math.Exp(-Sq(x / s.live.glowW) - glowRow);

                    F[k] = (float)f; Hf[k] = (float)hf; Xw[k] = (float)xw;
                }
            }

            // ── tongues (flame space) and embers (world space) — each a bounded bump; exp(−x) is exactly 0 past x ≈ 104 in
            //    the source's float32, so samples that far out are skipped without changing a byte ──
            var TT = TongueTable(s, fr.seed);
            if (TT != null)
            {
                const double R2Max = 32.0;   // r2^1.35 > 104
                for (int kq = 0; kq < TT.Length; kq++)
                {
                    var tg = TT[kq];
                    double age = Mod1(t + tg.phase);
                    if (age >= tg.life) continue;
                    double a = age / tg.life;
                    double ex = tg.x0 + tg.outX * Math.Pow(a, 1.5);
                    double ey = tg.y0 + tg.rise * Math.Pow(a, 0.85);
                    double envT = Math.Pow(Math.Sin(Math.PI * a), 0.55);
                    double grow = 0.55 + 0.45 * Math.Sin(Math.PI * Math.Min(a * 1.4, 1.0));
                    double ampT = envT * tg.hot * s.live.tongueGain;
                    double shear = s.live.tongueLean * tg.outX / Math.Max(Math.Abs(tg.outX), 1e-3) * a;
                    double lg = tg.l * grow, wg = tg.w * grow;
                    for (int j = 0; j < rh; j++)
                    {
                        double dy = Y[j] - ey;
                        double ry = dy / lg, ry2 = ry * ry;
                        if (ry2 > R2Max) continue;
                        double taper = 1.0 - 0.45 * Clamp(dy / lg, 0.0, 1.0);
                        double sx = wg * Math.Max(taper, 0.35);
                        double shift = shear * Math.Max(dy, 0.0);
                        for (int i = 0; i < rw; i++)
                        {
                            int k = j * rw + i;
                            double dxs = (Xw[k] - ex) - shift;
                            double r2 = Sq(dxs / sx) + ry2;
                            if (r2 > R2Max) continue;
                            F[k] += (float)(Math.Exp(-Math.Pow(r2, 1.35)) * ampT);
                        }
                    }
                }
            }
            var EE = EmberTable(s, fr.seed);
            if (EE != null)
            {
                const double R2Max = 104.0;
                for (int kq = 0; kq < EE.Length; kq++)
                {
                    var em = EE[kq];
                    double age = Mod1(t + em.phase);
                    if (age >= em.life) continue;
                    double a = age / em.life;
                    double ex = em.x0 + em.drift * a + Math.Sin(2 * Math.PI * em.wob * a + em.phase * 9.0) * s.live.emberWobble;
                    double ey = s.live.emberY0 + em.rise * Math.Pow(a, 0.82);
                    double ampE = Math.Pow(Math.Sin(Math.PI * a), 0.7) * em.hot * s.live.emberGain;
                    double inv = 1.0 / (em.size * em.size);
                    for (int j = 0; j < rh; j++)
                    {
                        double ry2 = Sq((Y[j] - ey) * 1.35) * inv;
                        if (ry2 > R2Max) continue;
                        for (int i = 0; i < rw; i++)
                        {
                            double r2 = Sq(X[i] - ex) * inv + ry2;
                            if (r2 > R2Max) continue;
                            F[j * rw + i] += (float)(Math.Exp(-r2) * ampE);
                        }
                    }
                }
            }

            // ── ground cut, clip, cool, and into the canvas planes ──
            for (int j = 0; j < rh; j++)
            {
                double cut = SmoothStep(-0.5, 0.6, Y[j]);
                int row = (j0 + j) * fr.W2 + i0;
                for (int i = 0; i < rw; i++)
                {
                    int k = j * rw + i;
                    double f = F[k] * cut;
                    if (f <= 0.0) continue;
                    double fp = f * fr.amp;
                    double Hn = Y[j] / Hf[k];
                    Fp[row + i] += (float)fp;
                    C[row + i] += (float)(fp * (1.0 - s.live.cool * Clamp(Hn, 0.0, 1.4)));
                }
            }
        }

        static double Sq(double v) => v * v;
    }
}
