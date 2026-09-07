// ArcBurstForm — Kiln "Energy Explosion / agent4" ARC BURST, generation 4, as a PyreForm.
//
// Source: D:/CODEZ/Kiln/projects/Energy Explosion/agents/agent4 (generate.py + arclib4.py + arclib.py, MANIFEST
// "generation 4 — the ARC burst, transparent to the core"). The algorithm and the ported / approximated / dropped
// list are in PyreArcBurst.cs's header. The ten source draws are ten PROGRAMS over one stroke library, so they are
// ten `layout`s of one form, each ported with its own literals; `Layout.Bolt` (contract draw `bolt`, seed 4303,
// palette violet — D:/Claude@GDrive/Energy Explosion/GEN4/contract/agent4/bolt) is the canonical one and every
// shared default is bolt's. Each layout's own knobs live in its settings class below with the draw_* literal as the
// default (the field's tooltip says what it does; the class header says which function it came from).
//
// Envelopes: a dial the program reads as a per-frame AMOUNT (the alpha mapping, the bloom, the stroke scales, a layout's
// roughness / probabilities / opacities / jitter / depth shading / branch geometry / flash) is a ZUIValue — Static draws
// the same bytes as a plain float, a Curve drives it over the layer's life. `Prepare` resolves the form's own dials and
// the ACTIVE layout's into `live` structs the program reads. A range drawn ONCE per element from the anchor stream
// (reach, span, ghost opacity, strike / snap / blow-out / break-off moments, speeds, lengths, the tilt, the jet scatter)
// is structural, a `*Start` / `*End` / `*K` moment or exponent is timing, and counts are counts — they all stay plain.
//
// Bolt trees are RE-ROLLED EVERY FRAME by design (lightning re-strikes: `random.Random(seed·m + frame)`), with the
// per-draw constants (arm angles, reach, ghost opacities, blow-out moments) fixed once from `random.Random(seed + k)`.
// Both streams are CPython's Mersenne Twister, replicated bit-for-bit by PyrePyRandom, so the form draws the
// contract's own bolts; `rerollPerFrame` off holds the per-frame stream at frame 0 so a bolt grows instead of
// flickering (not in the source — the one authored option the brief asked for).
//
// Units: the draws are in the source's 128 px frame; the form scales that frame to the canvas (and to a swarm
// particle's size) and renders k× supersampled when the canvas is smaller than 128 so sub-pixel filaments survive
// the downsample. Colour is the form's own five cel bands, never the layer Fill.
using System;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre.Forms.Kiln
{
    [Serializable]
    [PyreFormInfo("Arc Burst", group: "Kiln/Energy Explosion", icon: "zap")]
    // [SerializeReference] embeds this type's namespace+assembly in every real .asset's YAML — required even
    // though the class name itself is unchanged, since the containing namespace/assembly renamed 2026-08-23.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.PyrePlus.Forms.Kiln", "com.Lautaro-Arino.Laubrary.PyrePlus.Forms.Kiln", null)]
    public sealed class ArcBurstForm : PyreForm, IPlusFieldPublisher, IPlusRampProbe
    {
        public override string DisplayName => "Arc Burst";
        public override string Description =>
            "An ELECTRIC arc burst (Kiln Energy Explosion, agent4 gen 4): branching lightning trunks, rings of "
            + "jittered arcs, a caged sphere, flying shards — ten layouts of one stroke engine. Strokes deposit energy "
            + "and an already-normalised alpha with MAXIMUM compositing (a solid arc crossing a ghost wins the "
            + "crossing), a 3-pass box bloom spreads colour further than opacity, and the energy snaps to five cel "
            + "bands (white core, saturated sheath). Half the arms are translucent to the core. The bolts are "
            + "re-struck every frame, as the source does. Colour comes from the form's own Palette bands, NOT the layer Fill. "
            + "SWARM: off = one centred burst; on = one burst per swarm particle at its position and life.";

        public override bool UsesFill => false;

        public enum Layout { Core, Weave, Bolt, Crown, Lattice, Terminal, Cage, Stipple, Pinch, Lichten }

        // ── which program ──
        [Tooltip("Which of the ten source draws this is. Core: a translucent plasma lump with whips. Weave: a shockwave ring breaking into dashes under a rotating transparent shutter. Bolt: five huge branching trunks with a see-through gap travelling root to tip. Crown: twelve fat petals round a gear-toothed heart, ghost sets crossfading. Lattice: a hubless node net woven by depth. Terminal: a hub striking nine nodes, dying one side first. Cage: arcs over a sphere, back faces transparent. Stipple: a flash decomposing into shards then dots. Pinch: two axial jets through a translucent equatorial ring. Lichten: capillary creep hollowing from the inside.")]
        [ZUILabel("Arc pattern")]
        public Layout layout = Layout.Bolt;

        // ── field (arclib4.Field) ──
        [Tooltip("Energy at which a stroke's alpha reaches 1 (alpha = clip(energy / Aref)^Agamma × opacity, set at deposition). Lower = every stroke more opaque. bolt: 0.28.")]
        [ZUILabel("Fade-in energy")] [ZUIGroup("Alpha window", Tooltip = "How stroke energy maps to opacity.")]
        [Range(0.1f, 0.6f)] public ZUIValue aref = new ZUIValue(0.28f);
        [Tooltip("Gamma on the alpha ramp (below 1 lifts the faint sheath). bolt: 0.70.")]
        [ZUILabel("Alpha gamma")] [ZUIGroup("Alpha window")]
        [Range(0.3f, 1.5f)] public ZUIValue agamma = new ZUIValue(0.70f);

        // ── bloom ──
        [Tooltip("Radius of the energy bloom's box blur (3 passes ≈ Gaussian), in source pixels at the 128 px frame — scales with the canvas. 0 = no bloom. bolt: 3.0.")]
        [ZUILabel("Bloom radius")] [ZUIGroup("Bloom", Tooltip = "The glow spread around every lit stroke.")]
        [Range(0f, 6f)] public ZUIValue bloomRadius = new ZUIValue(3.0f);
        [Tooltip("How much blurred energy is ADDED back (overlapping arcs glow here, not at deposition). bolt: 0.48.")]
        [ZUILabel("Bloom strength")] [ZUIGroup("Bloom")]
        [Range(0f, 1f)] public ZUIValue bloomStrength = new ZUIValue(0.48f);
        [Tooltip("The ALPHA plane's bloom as a share of the energy bloom, blurred over 0.8× the radius — colour must spread further than opacity or the glow reads as grey smoke. Source: 0.34.")]
        [ZUILabel("Bloom colour reach")] [ZUIGroup("Bloom")]
        [Range(0f, 1f)] public ZUIValue bloomAlpha = new ZUIValue(0.34f);

        // ── stroke scales ──
        [Tooltip("Multiplier on every stroke's core and sheath width (1 = the source's pixels).")]
        [ZUILabel("Stroke width")] [ZUIGroup("Stroke look", Tooltip = "Global multipliers applied to every stroke, whichever pattern is active.")]
        [Range(0.3f, 2.5f)] public ZUIValue widthScale = new ZUIValue(1f);
        [Tooltip("Multiplier on every stroke's and body's energy (1 = the source). Energy decides the colour band, so above 1 more of the figure goes white.")]
        [ZUILabel("Stroke energy")] [ZUIGroup("Stroke look")]
        [Range(0.3f, 2f)] public ZUIValue ampScale = new ZUIValue(1f);
        [Tooltip("keep_hue floor: a ghost arm's energy is multiplied by floor + (1 − floor) × its opacity, so a translucent core lands in the saturated band instead of rendering as grey string. Source: 0.36.")]
        [ZUILabel("Ghost colour floor")] [ZUIGroup("Stroke look")]
        [Range(0f, 1f)] public ZUIValue keepHueFloor = new ZUIValue(0.36f);
        [Tooltip("Opacity range of the DEEP ghosts (the fifth of translucent arms that are barely there). Source: 0.05..0.13. The low end.")]
        [ZUILabel("Deep ghost opacity (low)")] [ZUIGroup("Stroke look")]
        [Range(0f, 0.3f)] public float ghostDeepLo = 0.05f;
        [Tooltip("The high end of the deep-ghost opacity range. Source: 0.13.")]
        [ZUILabel("Deep ghost opacity (high)")] [ZUIGroup("Stroke look")]
        [Range(0f, 0.4f)] public float ghostDeepHi = 0.13f;
        [Tooltip("On (the source): the bolt geometry is rolled afresh every frame from random.Random(seed·m + frame), so lightning re-strikes. Off: the per-frame stream is held at frame 0 and a bolt grows along the clock instead of flickering.")]
        [ZUILabel("Re-strike every frame")] [ZUIGroup("Stroke look")]
        public bool rerollPerFrame = true;

        // ── colour ──
        [Tooltip("The cel palette: a free gradient sampled into N hard bands (below the first band's start, the pixel is absent — the form's own energy floor). Edit the ramp freely; the Bands count only changes the sampling resolution, so it never loses a colour you've picked. Presets: ArcBands.Ion / Violet / Acid / Plasma / Cyan / Magenta / Chroma / Steel / Crimson / Teal.")]
        [ZUILabel("Colour bands")] [ZUIGroup("Colour", Tooltip = "The five-band cel palette every layout is coloured with.")]
        public ZuiGradient palette = ArcBands.Violet();

        // ── per-layout settings (one box shows at a time) ──
        [ZUIShowIf("layout", "Core")] [Tooltip("draw_core's literals.")] public CoreSettings core = new CoreSettings();
        [ZUIShowIf("layout", "Weave")] [Tooltip("draw_weave's literals.")] public WeaveSettings weave = new WeaveSettings();
        [ZUIShowIf("layout", "Bolt")] [Tooltip("draw_bolt's literals.")] public BoltSettings bolt = new BoltSettings();
        [ZUIShowIf("layout", "Crown")] [Tooltip("draw_crown's literals.")] public CrownSettings crown = new CrownSettings();
        [ZUIShowIf("layout", "Lattice")] [Tooltip("draw_lattice's literals.")] public LatticeSettings lattice = new LatticeSettings();
        [ZUIShowIf("layout", "Terminal")] [Tooltip("draw_terminal's literals.")] public TerminalSettings terminal = new TerminalSettings();
        [ZUIShowIf("layout", "Cage")] [Tooltip("draw_cage's literals.")] public CageSettings cage = new CageSettings();
        [ZUIShowIf("layout", "Stipple")] [Tooltip("draw_stipple's literals.")] public StippleSettings stipple = new StippleSettings();
        [ZUIShowIf("layout", "Pinch")] [Tooltip("draw_pinch's literals.")] public PinchSettings pinch = new PinchSettings();
        [ZUIShowIf("layout", "Lichten")] [Tooltip("draw_lichten's literals.")] public LichtenSettings lichten = new LichtenSettings();

        // ── swarm ──
        [PyreSwarmOnly]
        [Tooltip("Size of each swarm particle's burst as a fraction of the solo burst (the swarm's own size/depth shading multiplies it).")]
        [ZUILabel("Swarm burst size")] [ZUIGroup("Swarm")]
        [Range(0.1f, 1f)] public ZUIValue swarmSize = new ZUIValue(0.5f);

        // ═════════════════════════════ per-layout settings classes ═════════════════════════════

        /// draw_core (generate.py): 13 whips off a translucent body; fixed per whip: angle, curl direction, reach, ghost opacity.
        [Serializable] public sealed class CoreSettings
        {
            [ZUILabel("Whip count")] [ZUIGroup("Shape", Tooltip = "How many pieces this layout draws, and their size.")]
            [Tooltip("Whips lashing off the body (source: 13).")] [Range(1, 32)] public int whips = 13;
            [ZUILabel("Whip sideways sweep")] [ZUIGroup("Shape")]
            [Tooltip("How far a whip's tip sweeps sideways over the clip, radians (each whip picks ±this once; source 0.75).")] [Range(0f, 2f)] public float curl = 0.75f;
            [ZUILabel("Shortest reach")] [ZUIGroup("Shape")]
            [Tooltip("Shortest whip as a fraction of the outer radius (source 0.72).")] [Range(0.2f, 1f)] public float reachLo = 0.72f;
            [ZUILabel("Longest reach")] [ZUIGroup("Shape")]
            [Tooltip("Longest whip as a fraction of the outer radius (source 1.0).")] [Range(0.2f, 1.2f)] public float reachHi = 1.0f;
            [ZUILabel("Ghost share")] [ZUIGroup("Ghosts", Tooltip = "The translucent strokes mixed in among the solid ones.")]
            [Tooltip("Share of whips that are translucent (source 0.55).")] [Range(0f, 1f)] public float ghostP = 0.55f;
            [ZUILabel("Faintest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Faintest ordinary ghost opacity (source 0.12).")] [Range(0f, 1f)] public float ghostLo = 0.12f;
            [ZUILabel("Brightest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Most opaque ghost opacity (source 0.42).")] [Range(0f, 1f)] public float ghostHi = 0.42f;
            [ZUILabel("Deep ghost share")] [ZUIGroup("Ghosts")]
            [Tooltip("Share of ghosts that are DEEP (5–13 %, source 0.18).")] [Range(0f, 1f)] public float ghostDeepP = 0.18f;
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the global alpha fade begins on the clock (source 0.52).")] [Range(0f, 1f)] public float dissolveStart = 0.52f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the energy starts cooling (source 0.90).")] [Range(0f, 1f)] public float coolStart = 0.90f;
            [ZUILabel("Roughness")] [ZUIGroup("Detail & texture", Tooltip = "Roughness, jitter and the small extra touches on top of the shape.", Advanced = true)]
            [Tooltip("Midpoint-displacement roughness of a whip (source 0.19).")] [Range(0f, 0.6f)] public ZUIValue rough = new ZUIValue(0.19f);
            [ZUILabel("Path detail")] [ZUIGroup("Detail & texture", Advanced = true)]
            [Tooltip("Midpoint-displacement levels: 2^detail segments per whip (source 5).")] [Range(2, 7)] public int detail = 5;
            [ZUILabel("Fork chance")] [ZUIGroup("Detail & texture")]
            [Tooltip("Base chance a whip throws a fork (rises by 0.3 over the clip; source 0.5).")] [Range(0f, 1f)] public ZUIValue forkP = new ZUIValue(0.5f);
            [ZUILabel("Crawler count")] [ZUIGroup("Detail & texture")]
            [Tooltip("Crawlers — short arcs skating over the body's skin at the start (source 7, thinning with t).")] [Range(0, 20)] public int crawlers = 7;
            [ZUILabel("Body brightness")] [ZUIGroup("Body & energy", Tooltip = "Brightness of the layout's own special parts.")]
            [Tooltip("Peak energy of the body (source 1.9).")] [Range(0f, 3f)] public ZUIValue bodyAmp = new ZUIValue(1.9f);
            [ZUILabel("Body opacity")] [ZUIGroup("Body & energy")]
            [Tooltip("Opacity of the body at the start — it is a body of light you see the far whips THROUGH (source 0.88, falling to 0.46).")] [Range(0f, 1f)] public ZUIValue bodyOpa = new ZUIValue(0.88f);

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float bodyAmp, bodyOpa, rough, forkP; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { bodyAmp = ctx.Eval(bodyAmp, 10), bodyOpa = ctx.Eval(bodyOpa, 11), rough = ctx.Eval(rough, 12), forkP = ctx.Eval(forkP, 13) };
        }

        /// draw_weave: 26 jittered arcs on a thickening ring, chords, spurs, a rotating transparent shutter.
        [Serializable] public sealed class WeaveSettings
        {
            [ZUILabel("Arc count")] [ZUIGroup("Shape")]
            [Tooltip("Arcs round the ring (source 26).")] [Range(2, 64)] public int arcs = 26;
            [ZUILabel("Shortest span")] [ZUIGroup("Shape")]
            [Tooltip("Shortest arc span, radians (source 0.5).")] [Range(0.1f, 3f)] public float spanLo = 0.5f;
            [ZUILabel("Longest span")] [ZUIGroup("Shape")]
            [Tooltip("Longest arc span, radians (source 1.05).")] [Range(0.1f, 3f)] public float spanHi = 1.05f;
            [ZUILabel("Ghost share")] [ZUIGroup("Ghosts", Tooltip = "The translucent strokes mixed in among the solid ones.")]
            [Tooltip("Share of arcs that are translucent (source 0.50).")] [Range(0f, 1f)] public float ghostP = 0.50f;
            [ZUILabel("Faintest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Faintest ghost opacity (source 0.22).")] [Range(0f, 1f)] public float ghostLo = 0.22f;
            [ZUILabel("Brightest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Most opaque ghost opacity (source 0.52).")] [Range(0f, 1f)] public float ghostHi = 0.52f;
            [ZUILabel("Deep ghost share")] [ZUIGroup("Ghosts")]
            [Tooltip("Share of ghosts that are deep (source 0.10).")] [Range(0f, 1f)] public float ghostDeepP = 0.10f;
            [ZUILabel("Earliest die-off")] [ZUIGroup("Timing")]
            [Tooltip("Earliest moment an arc starts fading out on its own (source 0.54).")] [Range(0f, 1.2f)] public float dieLo = 0.54f;
            [ZUILabel("Latest die-off")] [ZUIGroup("Timing")]
            [Tooltip("Latest moment an arc starts fading (above 1 = it outlives the clip; source 1.12).")] [Range(0f, 1.5f)] public float dieHi = 1.12f;
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the chords' alpha fade begins (source 0.46).")] [Range(0f, 1f)] public float dissolveStart = 0.46f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the energy starts cooling (source 0.90).")] [Range(0f, 1f)] public float coolStart = 0.90f;
            [ZUILabel("Chord count")] [ZUIGroup("Shape")]
            [Tooltip("Chords across the interior at the start, thinning to 1 (source 3).")] [Range(0, 12)] public int chords = 3;
            [ZUILabel("Spur count")] [ZUIGroup("Shape")]
            [Tooltip("Spurs shot outward off the shell by the end (5 at the start + this; source 9).")] [Range(0, 30)] public int spurs = 9;
            [ZUILabel("Jitter amount")] [ZUIGroup("Detail & texture")]
            [Tooltip("Jitter amplitude of the arcs at the start, px (grows by 2.6; source 1.8).")] [Range(0f, 6f)] public ZUIValue jitter = new ZUIValue(1.8f);
            [ZUILabel("Shutter opens at")] [ZUIGroup("Body & energy")]
            [Tooltip("When the transparent shutter sector starts opening (source 0.34 — a shockwave must close before it comes apart).")] [Range(0f, 1f)] public float shutterStart = 0.34f;
            [ZUILabel("Shutter transparency")] [ZUIGroup("Body & energy")]
            [Tooltip("How transparent the shutter sector gets (source 0.76).")] [Range(0f, 1f)] public ZUIValue shutterDepth = new ZUIValue(0.76f);

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float jitter, shutterDepth; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { jitter = ctx.Eval(jitter, 10), shutterDepth = ctx.Eval(shutterDepth, 11) };
        }

        /// draw_bolt: five trunks, depth-3 trees, one or two ghost trunks, a travelling blow-out window per trunk.
        [Serializable] public sealed class BoltSettings
        {
            [ZUILabel("Trunk count")] [ZUIGroup("Shape")]
            [Tooltip("Trunks (source 5 — at 128 px a branching trunk needs room to branch).")] [Range(1, 16)] public int trunks = 5;
            [ZUILabel("Shortest reach")] [ZUIGroup("Shape")]
            [Tooltip("Shortest trunk as a fraction of the full reach (source 0.82).")] [Range(0.2f, 1f)] public float reachLo = 0.82f;
            [ZUILabel("Longest reach")] [ZUIGroup("Shape")]
            [Tooltip("Longest trunk as a fraction of the full reach (source 1.0).")] [Range(0.2f, 1.2f)] public float reachHi = 1.0f;
            [ZUILabel("Fewest branches")] [ZUIGroup("Branching", Tooltip = "How the tree paths fork.")]
            [Tooltip("Fewest branches a path throws (re-rolled per frame; source 2).")] [Range(0, 6)] public int branchMin = 2;
            [ZUILabel("Most branches")] [ZUIGroup("Branching")]
            [Tooltip("Most branches a path throws (source 3).")] [Range(0, 6)] public int branchMax = 3;
            [ZUILabel("Branch generations")] [ZUIGroup("Branching")]
            [Tooltip("Branch generations under the trunk (source 3).")] [Range(0, 5)] public int depth = 3;
            [ZUILabel("Roughness")] [ZUIGroup("Detail & texture", Tooltip = "Roughness, jitter and the small extra touches on top of the shape.", Advanced = true)]
            [Tooltip("Midpoint-displacement roughness (source 0.20).")] [Range(0f, 0.6f)] public ZUIValue rough = new ZUIValue(0.20f);
            [ZUILabel("Path detail")] [ZUIGroup("Detail & texture", Advanced = true)]
            [Tooltip("Midpoint-displacement levels: 2^detail segments per path (source 5).")] [Range(2, 7)] public int detail = 5;
            [ZUILabel("Branch length share")] [ZUIGroup("Branching")]
            [Tooltip("A branch's length as a share of the distance left to the parent's tip (source 0.48).")] [Range(0.1f, 1f)] public ZUIValue branchScale = new ZUIValue(0.48f);
            [ZUILabel("Branch angle spread")] [ZUIGroup("Branching")]
            [Tooltip("Widest branch angle off the parent, radians (source 1.1).")] [Range(0.3f, 1.6f)] public ZUIValue branchSpread = new ZUIValue(1.1f);
            [ZUILabel("Branch energy keep")] [ZUIGroup("Branching")]
            [Tooltip("Energy and width a generation keeps relative to its parent (source 0.62).")] [Range(0.2f, 1f)] public ZUIValue branchAmp = new ZUIValue(0.62f);
            [ZUILabel("Ghost share")] [ZUIGroup("Ghosts", Tooltip = "The translucent strokes mixed in among the solid ones.")]
            [Tooltip("Share of trunks that are ghosts (source 0.45 — with five arms the arms ARE the silhouette, so no deep ghosts here).")] [Range(0f, 1f)] public float ghostP = 0.45f;
            [ZUILabel("Faintest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Faintest ghost trunk opacity (source 0.24).")] [Range(0f, 1f)] public float ghostLo = 0.24f;
            [ZUILabel("Brightest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Most opaque ghost trunk opacity (source 0.50).")] [Range(0f, 1f)] public float ghostHi = 0.50f;
            [ZUILabel("Deep ghost share")] [ZUIGroup("Ghosts")]
            [Tooltip("Share of ghosts that are deep (source 0).")] [Range(0f, 1f)] public float ghostDeepP = 0f;
            [ZUILabel("Earliest blow-out")] [ZUIGroup("Timing")]
            [Tooltip("Earliest moment a trunk's blow-out gap starts travelling from the root (source 0.42).")] [Range(0f, 1f)] public float blowLo = 0.42f;
            [ZUILabel("Latest blow-out")] [ZUIGroup("Timing")]
            [Tooltip("Latest blow-out start (source 0.68 — each trunk fails at its own moment).")] [Range(0f, 1f)] public float blowHi = 0.68f;
            [ZUILabel("Translucent veil chance")] [ZUIGroup("Detail & texture")]
            [Tooltip("Chance a non-trunk path gets a translucent stretch (source 0.35).")] [Range(0f, 1f)] public ZUIValue veilP = new ZUIValue(0.35f);
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the global alpha fade begins (source 0.52).")] [Range(0f, 1f)] public float dissolveStart = 0.52f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the energy starts cooling (source 0.90).")] [Range(0f, 1f)] public float coolStart = 0.90f;
            [ZUILabel("Root flash energy")] [ZUIGroup("Body & energy")]
            [Tooltip("Energy of the flash the trunks are rooted in, gone by frame 7 (source 1.95).")] [Range(0f, 3f)] public ZUIValue flashAmp = new ZUIValue(1.95f);

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float rough, branchScale, branchSpread, branchAmp, veilP, flashAmp; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { rough = ctx.Eval(rough, 10), branchScale = ctx.Eval(branchScale, 11), branchSpread = ctx.Eval(branchSpread, 12), branchAmp = ctx.Eval(branchAmp, 13), veilP = ctx.Eval(veilP, 14), flashAmp = ctx.Eval(flashAmp, 15) };
        }

        /// draw_crown: twelve tapered lobes with a filament up each spine, alternating ghost sets that crossfade mid-flight.
        [Serializable] public sealed class CrownSettings
        {
            [ZUILabel("Lobe count")] [ZUIGroup("Shape")]
            [Tooltip("Lobes (source 12; the core's gear teeth follow the count).")] [Range(2, 24)] public int lobes = 12;
            [ZUILabel("Shortest reach")] [ZUIGroup("Shape")]
            [Tooltip("Shortest lobe reach multiplier (source 0.60).")] [Range(0.2f, 1.2f)] public float reachLo = 0.60f;
            [ZUILabel("Longest reach")] [ZUIGroup("Shape")]
            [Tooltip("Longest lobe reach multiplier (source 1.05).")] [Range(0.2f, 1.5f)] public float reachHi = 1.05f;
            [ZUILabel("Thinnest width")] [ZUIGroup("Shape")]
            [Tooltip("Thinnest lobe width multiplier (source 0.72).")] [Range(0.2f, 2f)] public float widthLo = 0.72f;
            [ZUILabel("Fattest width")] [ZUIGroup("Shape")]
            [Tooltip("Fattest lobe width multiplier (source 1.22).")] [Range(0.2f, 2f)] public float widthHi = 1.22f;
            [ZUILabel("Faintest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Opacity floor of a ghost lobe (source 0.18).")] [Range(0f, 1f)] public float ghostLo = 0.18f;
            [ZUILabel("Ghost opacity range")] [ZUIGroup("Ghosts")]
            [Tooltip("Random span added to the ghost floor (source 0.16 → ghosts at 18–34 %).")] [Range(0f, 1f)] public float ghostSpan = 0.16f;
            [ZUILabel("Ghost swap begins at")] [ZUIGroup("Timing")]
            [Tooltip("When the ghost set starts crossfading to the other half (source 0.30).")] [Range(0f, 1f)] public float swapStart = 0.30f;
            [ZUILabel("Ghost swap ends at")] [ZUIGroup("Timing")]
            [Tooltip("When the crossfade completes (source 0.72 — slow enough that no frame is the moment).")] [Range(0f, 1f)] public float swapEnd = 0.72f;
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the lobes' alpha fade begins (source 0.46).")] [Range(0f, 1f)] public float dissolveStart = 0.46f;
            [ZUILabel("Fade shape")] [ZUIGroup("Timing", Advanced = true)]
            [Tooltip("Exponent of that fade (source 1.15).")] [Range(0.5f, 2f)] public float dissolveK = 1.15f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the lobes' energy starts cooling (source 0.92).")] [Range(0f, 1f)] public float coolStart = 0.92f;
            [ZUILabel("Lobe brightness")] [ZUIGroup("Body & energy")]
            [Tooltip("Lobe energy — kept under the pale band's 0.58 threshold so petals are COLOURED and only the filament is white (source 0.54).")] [Range(0f, 1.5f)] public ZUIValue lobeAmp = new ZUIValue(0.54f);
            [ZUILabel("Crackle chance")] [ZUIGroup("Body & energy")]
            [Tooltip("Chance of a tooth-to-tooth crackle per lobe while the core has charge (source 0.55).")] [Range(0f, 1f)] public ZUIValue crackleP = new ZUIValue(0.55f);
            [ZUILabel("Roughness")] [ZUIGroup("Detail & texture", Tooltip = "Roughness, jitter and the small extra touches on top of the shape.", Advanced = true)]
            [Tooltip("Roughness of the spine filaments (source 0.11).")] [Range(0f, 0.6f)] public ZUIValue rough = new ZUIValue(0.11f);

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float lobeAmp, crackleP, rough; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { lobeAmp = ctx.Eval(lobeAmp, 10), crackleP = ctx.Eval(crackleP, 11), rough = ctx.Eval(rough, 12) };
        }

        /// draw_lattice: 12 rim nodes + 3 interior, edges by ROLE (rim / spokes / chords / triangle), opacity from depth.
        [Serializable] public sealed class LatticeSettings
        {
            [ZUILabel("Rim node count")] [ZUIGroup("Shape")]
            [Tooltip("Rim nodes (source 12).")] [Range(3, 24)] public int outer = 12;
            [ZUILabel("Interior node count")] [ZUIGroup("Shape")]
            [Tooltip("Interior nodes (source 3).")] [Range(1, 6)] public int inner = 3;
            [ZUILabel("Chord count")] [ZUIGroup("Shape")]
            [Tooltip("Chords across the rim (source 7).")] [Range(0, 20)] public int chords = 7;
            [ZUILabel("Roughness")] [ZUIGroup("Detail & texture", Tooltip = "Roughness, jitter and the small extra touches on top of the shape.", Advanced = true)]
            [Tooltip("Edge roughness (source 0.15).")] [Range(0f, 0.6f)] public ZUIValue rough = new ZUIValue(0.15f);
            [ZUILabel("Path detail")] [ZUIGroup("Detail & texture", Advanced = true)]
            [Tooltip("Midpoint-displacement levels per edge (source 4).")] [Range(2, 7)] public int detail = 4;
            [ZUILabel("Deepest interior opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Opacity of the deepest interior edge (source 0.12; nearer edges climb toward 1 as depth^1.4).")] [Range(0f, 1f)] public ZUIValue ghostFloor = new ZUIValue(0.12f);
            [ZUILabel("Rim opacity floor")] [ZUIGroup("Ghosts")]
            [Tooltip("Opacity floor of the RIM (source 0.30 — at 0.12 the back of the ring stopped being a ring).")] [Range(0f, 1f)] public ZUIValue rimFloor = new ZUIValue(0.30f);
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the global alpha fade begins (source 0.48).")] [Range(0f, 1f)] public float dissolveStart = 0.48f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the energy starts cooling (source 0.90).")] [Range(0f, 1f)] public float coolStart = 0.90f;
            [ZUILabel("Net breaks at")] [ZUIGroup("Timing")]
            [Tooltip("When the net lets go and the nodes fly (source 0.60).")] [Range(0f, 1f)] public float burstStart = 0.60f;
            [ZUILabel("Translucent veil chance")] [ZUIGroup("Detail & texture")]
            [Tooltip("Chance an edge gets a translucent stretch (source 0.45).")] [Range(0f, 1f)] public ZUIValue veilP = new ZUIValue(0.45f);
            [ZUILabel("Slowest escape speed")] [ZUIGroup("Timing")]
            [Tooltip("Slowest escaping node (source 0.7).")] [Range(0f, 2f)] public float flyLo = 0.7f;
            [ZUILabel("Fastest escape speed")] [ZUIGroup("Timing")]
            [Tooltip("Fastest escaping node (source 1.25).")] [Range(0f, 2f)] public float flyHi = 1.25f;

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float ghostFloor, rimFloor, rough, veilP; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { ghostFloor = ctx.Eval(ghostFloor, 10), rimFloor = ctx.Eval(rimFloor, 11), rough = ctx.Eval(rough, 12), veilP = ctx.Eval(veilP, 13) };
        }

        /// draw_terminal: a hub strikes nine nodes in quick sequence, neighbours hop along the rim, one side dies first.
        [Serializable] public sealed class TerminalSettings
        {
            [ZUILabel("Node count")] [ZUIGroup("Shape")]
            [Tooltip("Nodes round the hub (source 9).")] [Range(2, 24)] public int nodes = 9;
            [ZUILabel("Shortest reach")] [ZUIGroup("Shape")]
            [Tooltip("Nearest node as a fraction of the radius (source 0.86).")] [Range(0.2f, 1f)] public float reachLo = 0.86f;
            [ZUILabel("Longest reach")] [ZUIGroup("Shape")]
            [Tooltip("Farthest node (source 1.0).")] [Range(0.2f, 1.2f)] public float reachHi = 1.0f;
            [ZUILabel("Earliest strike")] [ZUIGroup("Timing")]
            [Tooltip("Earliest strike moment (source 0.01).")] [Range(0f, 1f)] public float fireLo = 0.01f;
            [ZUILabel("Latest strike")] [ZUIGroup("Timing")]
            [Tooltip("Latest strike moment (source 0.20 — compressed so the silhouette is there by frame 4).")] [Range(0f, 1f)] public float fireHi = 0.20f;
            [ZUILabel("Ghost share")] [ZUIGroup("Ghosts", Tooltip = "The translucent strokes mixed in among the solid ones.")]
            [Tooltip("Share of spokes that are translucent (source 0.50).")] [Range(0f, 1f)] public float ghostP = 0.50f;
            [ZUILabel("Faintest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Faintest ghost opacity (source 0.14).")] [Range(0f, 1f)] public float ghostLo = 0.14f;
            [ZUILabel("Brightest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Most opaque ghost opacity (source 0.45).")] [Range(0f, 1f)] public float ghostHi = 0.45f;
            [ZUILabel("Deep ghost share")] [ZUIGroup("Ghosts")]
            [Tooltip("Share of ghosts that are deep (source 0.18).")] [Range(0f, 1f)] public float ghostDeepP = 0.18f;
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the global alpha fade begins (source 0.70 — late, the angular sweep does the dissipation).")] [Range(0f, 1f)] public float dissolveStart = 0.70f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the node flares start cooling (source 0.78).")] [Range(0f, 1f)] public float coolStart = 0.78f;
            [ZUILabel("Roughness")] [ZUIGroup("Detail & texture", Tooltip = "Roughness, jitter and the small extra touches on top of the shape.", Advanced = true)]
            [Tooltip("Spoke roughness (source 0.13).")] [Range(0f, 0.6f)] public ZUIValue rough = new ZUIValue(0.13f);
            [ZUILabel("Path detail")] [ZUIGroup("Detail & texture", Advanced = true)]
            [Tooltip("Midpoint-displacement levels per spoke (source 6).")] [Range(2, 7)] public int detail = 6;
            [ZUILabel("Sweep eats from")] [ZUIGroup("Timing")]
            [Tooltip("When the transparent sector starts eating the wheel (source 0.40).")] [Range(0f, 1f)] public float sweepStart = 0.40f;
            [ZUILabel("Sweep transparency")] [ZUIGroup("Body & energy")]
            [Tooltip("How transparent the eaten side gets (source 0.96).")] [Range(0f, 1f)] public ZUIValue sweepDepth = new ZUIValue(0.96f);

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float rough, sweepDepth; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { rough = ctx.Eval(rough, 10), sweepDepth = ctx.Eval(sweepDepth, 11) };
        }

        /// draw_cage: nine partial great circles over a dim ball, opacity from z (8 % behind to 100 % in front), ribs snapping.
        [Serializable] public sealed class CageSettings
        {
            [ZUILabel("Rib count")] [ZUIGroup("Shape")]
            [Tooltip("Ribs — partial great circles (source 9).")] [Range(1, 20)] public int ribs = 9;
            [ZUILabel("Shortest span")] [ZUIGroup("Shape")]
            [Tooltip("Shortest rib, radians of its great circle (source 1.8).")] [Range(0.5f, 6.3f)] public float spanLo = 1.8f;
            [ZUILabel("Longest span")] [ZUIGroup("Shape")]
            [Tooltip("Longest rib (source 4.0).")] [Range(0.5f, 6.3f)] public float spanHi = 4.0f;
            [ZUILabel("Earliest snap")] [ZUIGroup("Shape")]
            [Tooltip("Earliest rib snap (source 0.40).")] [Range(0f, 1f)] public float snapLo = 0.40f;
            [ZUILabel("Latest snap")] [ZUIGroup("Shape")]
            [Tooltip("Latest rib snap (source 0.74).")] [Range(0f, 1f)] public float snapHi = 0.74f;
            [ZUILabel("Ghost share")] [ZUIGroup("Ghosts", Tooltip = "The translucent strokes mixed in among the solid ones.")]
            [Tooltip("Share of ribs that are ghosts (source 0.30 — light, the DEPTH does the work here).")] [Range(0f, 1f)] public float ghostP = 0.30f;
            [ZUILabel("Faintest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Faintest ghost rib opacity (source 0.30).")] [Range(0f, 1f)] public float ghostLo = 0.30f;
            [ZUILabel("Brightest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Most opaque ghost rib opacity (source 0.60).")] [Range(0f, 1f)] public float ghostHi = 0.60f;
            [ZUILabel("Deep ghost share")] [ZUIGroup("Ghosts")]
            [Tooltip("Share of ghosts that are deep (source 0).")] [Range(0f, 1f)] public float ghostDeepP = 0f;
            [ZUILabel("Far-side opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Opacity of the far side of the sphere (source 0.08).")] [Range(0f, 1f)] public ZUIValue backOpa = new ZUIValue(0.08f);
            [ZUILabel("Depth falloff shape")] [ZUIGroup("Ghosts", Advanced = true)]
            [Tooltip("Gamma on the front-to-back opacity (source 1.5: most of the back hemisphere is a faint trace).")] [Range(0.3f, 4f)] public ZUIValue depthGamma = new ZUIValue(1.5f);
            [ZUILabel("Jitter amount")] [ZUIGroup("Detail & texture")]
            [Tooltip("Jitter amplitude of a rib at the start, px (grows with t and with snapping; source 3.8).")] [Range(0f, 10f)] public ZUIValue jitter = new ZUIValue(3.8f);
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the global alpha fade begins (source 0.52).")] [Range(0f, 1f)] public float dissolveStart = 0.52f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the ribs' energy starts cooling (source 0.88).")] [Range(0f, 1f)] public float coolStart = 0.88f;
            [ZUILabel("Cooling shape")] [ZUIGroup("Timing", Advanced = true)]
            [Tooltip("Exponent of that cooling (source 1.4).")] [Range(0.5f, 3f)] public float coolK = 1.4f;
            [ZUILabel("Breakout arc count")] [ZUIGroup("Shape")]
            [Tooltip("Breakout arcs discharging THROUGH the surface by the end (source 15).")] [Range(0, 40)] public int breakouts = 15;
            [ZUILabel("Spark count")] [ZUIGroup("Shape")]
            [Tooltip("Sparks round the rim by the end (source 18).")] [Range(0, 60)] public int sparks = 18;
            [ZUILabel("Ball opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Opacity of the ball behind the ribs (source 0.42).")] [Range(0f, 1f)] public ZUIValue ballOpa = new ZUIValue(0.42f);

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float backOpa, depthGamma, jitter, ballOpa; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { backOpa = ctx.Eval(backOpa, 10), depthGamma = ctx.Eval(depthGamma, 11), jitter = ctx.Eval(jitter, 12), ballOpa = ctx.Eval(ballOpa, 13) };
        }

        /// draw_stipple: a flash with six early trees, then 24 shards that shorten, separate into their depths, and cross-talk.
        [Serializable] public sealed class StippleSettings
        {
            [ZUILabel("Shard count")] [ZUIGroup("Shape")]
            [Tooltip("Shards (source 24).")] [Range(2, 64)] public int shards = 24;
            [ZUILabel("Arc count")] [ZUIGroup("Shape")]
            [Tooltip("Early branching discharges while the body lives (source 6).")] [Range(0, 16)] public int arcs = 6;
            [ZUILabel("Slowest speed")] [ZUIGroup("Shape")]
            [Tooltip("Slowest shard (source 0.60).")] [Range(0.1f, 2f)] public float speedLo = 0.60f;
            [ZUILabel("Fastest speed")] [ZUIGroup("Shape")]
            [Tooltip("Fastest shard (source 1.18).")] [Range(0.1f, 2f)] public float speedHi = 1.18f;
            [ZUILabel("Shortest length")] [ZUIGroup("Shape")]
            [Tooltip("Shortest shard length multiplier (source 0.55).")] [Range(0.1f, 3f)] public float lenLo = 0.55f;
            [ZUILabel("Longest length")] [ZUIGroup("Shape")]
            [Tooltip("Longest shard length multiplier (source 1.55).")] [Range(0.1f, 3f)] public float lenHi = 1.55f;
            [ZUILabel("Ghost share")] [ZUIGroup("Ghosts", Tooltip = "The translucent strokes mixed in among the solid ones.")]
            [Tooltip("Share of shards that recede into a ghost (source 0.60).")] [Range(0f, 1f)] public float ghostP = 0.60f;
            [ZUILabel("Faintest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Faintest shard opacity (source 0.18 — isolated pieces need a floor or they read as smoke).")] [Range(0f, 1f)] public float ghostLo = 0.18f;
            [ZUILabel("Brightest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Most opaque ghost shard (source 0.52).")] [Range(0f, 1f)] public float ghostHi = 0.52f;
            [ZUILabel("Deep ghost share")] [ZUIGroup("Ghosts")]
            [Tooltip("Share of ghosts that are deep (source 0).")] [Range(0f, 1f)] public float ghostDeepP = 0f;
            [ZUILabel("Earliest break-off")] [ZUIGroup("Timing")]
            [Tooltip("Earliest moment a shard breaks off (source 0.02).")] [Range(0f, 1f)] public float breakLo = 0.02f;
            [ZUILabel("Latest break-off")] [ZUIGroup("Timing")]
            [Tooltip("Latest break-off (source 0.34).")] [Range(0f, 1f)] public float breakHi = 0.34f;
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the global alpha fade begins (source 0.54).")] [Range(0f, 1f)] public float dissolveStart = 0.54f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the energy starts cooling (source 0.92).")] [Range(0f, 1f)] public float coolStart = 0.92f;
            [ZUILabel("Roughness")] [ZUIGroup("Detail & texture", Tooltip = "Roughness, jitter and the small extra touches on top of the shape.", Advanced = true)]
            [Tooltip("Roughness of the early trees (source 0.22).")] [Range(0f, 0.6f)] public ZUIValue rough = new ZUIValue(0.22f);
            [ZUILabel("Branch generations")] [ZUIGroup("Branching")]
            [Tooltip("Branch generations of the early trees (source 2).")] [Range(0, 4)] public int depth = 2;
            [ZUILabel("Fewest branches")] [ZUIGroup("Branching", Tooltip = "How the tree paths fork.")]
            [Tooltip("Fewest branches per path (source 1).")] [Range(0, 6)] public int branchMin = 1;
            [ZUILabel("Most branches")] [ZUIGroup("Branching")]
            [Tooltip("Most branches per path (source 3).")] [Range(0, 6)] public int branchMax = 3;
            [ZUILabel("Hair chance")] [ZUIGroup("Detail & texture")]
            [Tooltip("Chance a live shard throws a hair (source 0.22, falling with age).")] [Range(0f, 1f)] public ZUIValue hairP = new ZUIValue(0.22f);
            [ZUILabel("Cross-talk chance")] [ZUIGroup("Detail & texture")]
            [Tooltip("Chance of an arc jumping to the next shard round the ring, mid-clip (source 0.32).")] [Range(0f, 1f)] public ZUIValue crossP = new ZUIValue(0.32f);

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float rough, hairP, crossP; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { rough = ctx.Eval(rough, 10), hairP = ctx.Eval(hairP, 11), crossP = ctx.Eval(crossP, 12) };
        }

        /// draw_pinch: a tilted lens, two axial jets (depth-3 trees) and a squashed equatorial ring of 14 translucent arcs.
        [Serializable] public sealed class PinchSettings
        {
            [ZUILabel("Tilt amount")] [ZUIGroup("Shape")]
            [Tooltip("Random tilt of the whole pinch, radians (±this; source 0.22).")] [Range(0f, 1.6f)] public float tilt = 0.22f;
            [ZUILabel("Ring arc count")] [ZUIGroup("Shape")]
            [Tooltip("Arcs in the equatorial ring (source 14).")] [Range(2, 40)] public int ring = 14;
            [ZUILabel("Ring arc span")] [ZUIGroup("Shape")]
            [Tooltip("Span of each ring arc, radians (source 0.52).")] [Range(0.1f, 2f)] public ZUIValue ringSpan = new ZUIValue(0.52f);
            [ZUILabel("Ghost share")] [ZUIGroup("Ghosts", Tooltip = "The translucent strokes mixed in among the solid ones.")]
            [Tooltip("Share of ring arcs that are translucent (source 0.55).")] [Range(0f, 1f)] public float ghostP = 0.55f;
            [ZUILabel("Faintest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Faintest ring arc (source 0.26 — the ring must still read as a ring).")] [Range(0f, 1f)] public float ghostLo = 0.26f;
            [ZUILabel("Brightest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Most opaque ghost arc (source 0.55).")] [Range(0f, 1f)] public float ghostHi = 0.55f;
            [ZUILabel("Deep ghost share")] [ZUIGroup("Ghosts")]
            [Tooltip("Share of ghosts that are deep (source 0).")] [Range(0f, 1f)] public float ghostDeepP = 0f;
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the global alpha fade begins (source 0.50).")] [Range(0f, 1f)] public float dissolveStart = 0.50f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the JETS' energy starts cooling (source 0.80).")] [Range(0f, 1f)] public float coolStart = 0.80f;
            [ZUILabel("Ring cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the RING's energy starts cooling (source 0.92 — it outlives the jets).")] [Range(0f, 1f)] public float ringCoolStart = 0.92f;
            [ZUILabel("Roughness")] [ZUIGroup("Detail & texture", Tooltip = "Roughness, jitter and the small extra touches on top of the shape.", Advanced = true)]
            [Tooltip("Jet tree roughness (source 0.16).")] [Range(0f, 0.6f)] public ZUIValue rough = new ZUIValue(0.16f);
            [ZUILabel("Branch generations")] [ZUIGroup("Branching")]
            [Tooltip("Jet branch generations (source 3).")] [Range(0, 5)] public int depth = 3;
            [ZUILabel("Fewest branches")] [ZUIGroup("Branching", Tooltip = "How the tree paths fork.")]
            [Tooltip("Fewest branches per jet path (source 2).")] [Range(0, 6)] public int branchMin = 2;
            [ZUILabel("Most branches")] [ZUIGroup("Branching")]
            [Tooltip("Most branches per jet path (source 3).")] [Range(0, 6)] public int branchMax = 3;
            [ZUILabel("Branch length share")] [ZUIGroup("Branching")]
            [Tooltip("Jet branch length share (source 0.42).")] [Range(0.1f, 1f)] public ZUIValue branchScale = new ZUIValue(0.42f);
            [ZUILabel("Branch angle spread")] [ZUIGroup("Branching")]
            [Tooltip("Widest jet branch angle, radians (source 0.75).")] [Range(0.3f, 1.6f)] public ZUIValue branchSpread = new ZUIValue(0.75f);
            [ZUILabel("Ring squash")] [ZUIGroup("Shape")]
            [Tooltip("Ring squash: its radius along the axis as a share of its radius across (source 0.40).")] [Range(0.1f, 1f)] public ZUIValue axisRatio = new ZUIValue(0.40f);
            [ZUILabel("Jet tip scatter")] [ZUIGroup("Detail & texture")]
            [Tooltip("Sideways scatter of a jet's tip, px σ (source 3.0).")] [Range(0f, 12f)] public float jetSpread = 3.0f;
            [ZUILabel("Lens opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Opacity of the lens at the start (source 0.66, falling to 0.04).")] [Range(0f, 1f)] public ZUIValue lensOpa = new ZUIValue(0.66f);

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float lensOpa, rough, branchScale, branchSpread, axisRatio, ringSpan; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { lensOpa = ctx.Eval(lensOpa, 10), rough = ctx.Eval(rough, 11), branchScale = ctx.Eval(branchScale, 12), branchSpread = ctx.Eval(branchSpread, 13), axisRatio = ctx.Eval(axisRatio, 14), ringSpan = ctx.Eval(ringSpan, 15) };
        }

        /// draw_lichten: nine depth-4 capillary trees, a transparency front eating the figure from the centre outward.
        [Serializable] public sealed class LichtenSettings
        {
            [ZUILabel("Root tree count")] [ZUIGroup("Shape")]
            [Tooltip("Root trees (source 9).")] [Range(1, 24)] public int roots = 9;
            [ZUILabel("Shortest reach")] [ZUIGroup("Shape")]
            [Tooltip("Shortest root reach (source 0.78).")] [Range(0.2f, 1f)] public float reachLo = 0.78f;
            [ZUILabel("Longest reach")] [ZUIGroup("Shape")]
            [Tooltip("Longest root reach (source 1.0).")] [Range(0.2f, 1.2f)] public float reachHi = 1.0f;
            [ZUILabel("Ghost share")] [ZUIGroup("Ghosts", Tooltip = "The translucent strokes mixed in among the solid ones.")]
            [Tooltip("Share of roots that are translucent (source 0.52).")] [Range(0f, 1f)] public float ghostP = 0.52f;
            [ZUILabel("Faintest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Faintest ghost root (source 0.16).")] [Range(0f, 1f)] public float ghostLo = 0.16f;
            [ZUILabel("Brightest ghost opacity")] [ZUIGroup("Ghosts")]
            [Tooltip("Most opaque ghost root (source 0.48).")] [Range(0f, 1f)] public float ghostHi = 0.48f;
            [ZUILabel("Deep ghost share")] [ZUIGroup("Ghosts")]
            [Tooltip("Share of ghosts that are deep (source 0.18).")] [Range(0f, 1f)] public float ghostDeepP = 0.18f;
            [ZUILabel("Fade begins at")] [ZUIGroup("Timing", Tooltip = "When things start, peak or fade over the layer's life.")]
            [Tooltip("Where the global alpha fade begins (source 0.66).")] [Range(0f, 1f)] public float dissolveStart = 0.66f;
            [ZUILabel("Cooling begins at")] [ZUIGroup("Timing")]
            [Tooltip("Where the energy starts cooling (source 0.86).")] [Range(0f, 1f)] public float coolStart = 0.86f;
            [ZUILabel("Roughness")] [ZUIGroup("Detail & texture", Tooltip = "Roughness, jitter and the small extra touches on top of the shape.", Advanced = true)]
            [Tooltip("Capillary roughness (source 0.24).")] [Range(0f, 0.6f)] public ZUIValue rough = new ZUIValue(0.24f);
            [ZUILabel("Path detail")] [ZUIGroup("Detail & texture", Advanced = true)]
            [Tooltip("Midpoint-displacement levels per path (source 5).")] [Range(2, 7)] public int detail = 5;
            [ZUILabel("Branch generations")] [ZUIGroup("Branching")]
            [Tooltip("Branch generations (source 4 — capillary density is the point).")] [Range(0, 5)] public int depth = 4;
            [ZUILabel("Fewest branches")] [ZUIGroup("Branching", Tooltip = "How the tree paths fork.")]
            [Tooltip("Fewest branches per path (source 2).")] [Range(0, 6)] public int branchMin = 2;
            [ZUILabel("Most branches")] [ZUIGroup("Branching")]
            [Tooltip("Most branches per path (source 3).")] [Range(0, 6)] public int branchMax = 3;
            [ZUILabel("Branch length share")] [ZUIGroup("Branching")]
            [Tooltip("Branch length share (source 0.44).")] [Range(0.1f, 1f)] public ZUIValue branchScale = new ZUIValue(0.44f);
            [ZUILabel("Branch angle spread")] [ZUIGroup("Branching")]
            [Tooltip("Widest branch angle, radians (source 1.25).")] [Range(0.3f, 1.6f)] public ZUIValue branchSpread = new ZUIValue(1.25f);
            [ZUILabel("Branch energy keep")] [ZUIGroup("Branching")]
            [Tooltip("Energy and width a generation keeps (source 0.72 — the trunk is barely thicker than its twigs).")] [Range(0.2f, 1f)] public ZUIValue branchAmp = new ZUIValue(0.72f);
            [ZUILabel("Hollow front begins at")] [ZUIGroup("Timing")]
            [Tooltip("When the transparency front starts growing from the centre (source 0.34).")] [Range(0f, 1f)] public float hollowStart = 0.34f;
            [ZUILabel("Spark count")] [ZUIGroup("Shape")]
            [Tooltip("Sparks at the advancing tips at the start (source 20, halving by the end).")] [Range(0, 60)] public int sparks = 20;
            [ZUILabel("Overall fade amount")] [ZUIGroup("Body & energy")]
            [Tooltip("How much the whole web fades evenly over the back half, outside the hole (source 0.72).")] [Range(0f, 1f)] public ZUIValue fadeAmt = new ZUIValue(0.72f);
            [ZUILabel("Translucent veil chance")] [ZUIGroup("Detail & texture")]
            [Tooltip("Chance a path gets a translucent stretch (source 0.45).")] [Range(0f, 1f)] public ZUIValue veilP = new ZUIValue(0.45f);

            /// The envelopes above resolved at one layer life — what the program reads.
            public struct Live { public float rough, branchScale, branchSpread, branchAmp, veilP, fadeAmt; }
            [NonSerialized] public Live live;
            public void Resolve(in PyreFormPrepareCtx ctx) => live = new Live { rough = ctx.Eval(rough, 10), branchScale = ctx.Eval(branchScale, 11), branchSpread = ctx.Eval(branchSpread, 12), branchAmp = ctx.Eval(branchAmp, 13), veilP = ctx.Eval(veilP, 14), fadeAmt = ctx.Eval(fadeAmt, 15) };
        }

        // ═════════════════════════════ runtime ═════════════════════════════
        /// The form's own envelopes resolved at one layer life (slots 0–8; a layout's settings take 10 onward —
        /// only the active layout is resolved, so the layouts share those slots).
        public struct Live { public float aref, agamma, bloomRadius, bloomStrength, bloomAlpha, widthScale, ampScale, keepHueFloor, swarmSize; }
        [NonSerialized] public Live live;

        public override void Prepare(in PyreFormPrepareCtx ctx)
        {
            live = new Live
            {
                aref = ctx.Eval(aref, 0), agamma = ctx.Eval(agamma, 1),
                bloomRadius = ctx.Eval(bloomRadius, 2), bloomStrength = ctx.Eval(bloomStrength, 3), bloomAlpha = ctx.Eval(bloomAlpha, 4),
                widthScale = ctx.Eval(widthScale, 5), ampScale = ctx.Eval(ampScale, 6), keepHueFloor = ctx.Eval(keepHueFloor, 7),
                swarmSize = ctx.Eval(swarmSize, 8),
            };
            switch (layout)
            {
                case Layout.Core: core.Resolve(ctx); break;
                case Layout.Weave: weave.Resolve(ctx); break;
                case Layout.Bolt: bolt.Resolve(ctx); break;
                case Layout.Crown: crown.Resolve(ctx); break;
                case Layout.Lattice: lattice.Resolve(ctx); break;
                case Layout.Terminal: terminal.Resolve(ctx); break;
                case Layout.Cage: cage.Resolve(ctx); break;
                case Layout.Stipple: stipple.Resolve(ctx); break;
                case Layout.Pinch: pinch.Resolve(ctx); break;
                default: lichten.Resolve(ctx); break;
            }
        }

        const int SourcePx = 128;
        [NonSerialized] float[] _E, _A, _sA, _sB, _pr, _pg, _pb, _pa;
        [NonSerialized] float[] _dumpH, _dumpT, _dumpA;
        [NonSerialized] PyreLut _lut;

        /// The energy threshold below which a pixel is absent (the source's floor) — a property of the FORM, not
        /// the palette, now that the palette is a free gradient with no threshold of its own to borrow.
        const float Floor = 0.045f;

        void IPlusFieldPublisher.PublishFields(Action<string, float[]> sink)
        {
            if (_dumpH != null) sink("H", _dumpH);
            if (_dumpT != null) sink("ramp_t", _dumpT);
            if (_dumpA != null) sink("alpha", _dumpA);
            _dumpH = _dumpT = _dumpA = null;
        }

        /// The contract's LUT is 256 entries of the step table, so the probe quantises t to that grid first — at
        /// t = 0.3 the LUT's entry 76 (0.298) is still the band below, and so is this answer.
        Color IPlusRampProbe.ProbeRamp(float t)
        {
            ReadBands();
            float tq = Mathf.Round(Mathf.Clamp01(t) * 255f) / 255f;
            var c = _lut.Sample32(tq);
            float a = tq < Floor ? 0f : 1f;
            return new Color(c.r / 255f, c.g / 255f, c.b / 255f, a);
        }

        /// Bake the palette (a locked ZuiGradient) into a plain-array LUT — texture-free, so this is safe on a
        /// PyreFrameFill worker thread. Cheap enough (256 entries) to rebake once per Render call, no caching needed.
        void ReadBands()
        {
            palette ??= ArcBands.Violet();
            _lut = PyreShade.BakeLut(palette, 256);
        }

        public override void Render(in PyreFormCtx ctx, Color32[] target)
        {
            // The renderer Prepares before Render; a direct caller (a test, a probe) may not — same funnel, same life, idempotent.
            Prepare(ctx.PrepareCtxAt(ctx.life));
            int seed = unchecked(ctx.seed + ctx.layerSalt * 1000003);
            int canvas = Mathf.Min(ctx.W, ctx.H);
            int k = Mathf.Clamp(Mathf.CeilToInt(SourcePx / (float)Mathf.Max(1, canvas)), 1, 4);
            int S = canvas * k, n = S * S;
            if (_E == null || _E.Length != n)
            {
                _E = new float[n]; _A = new float[n]; _sA = new float[n]; _sB = new float[n];
                _pr = new float[n]; _pg = new float[n]; _pb = new float[n]; _pa = new float[n];
            }
            var field = new ArcField(S, _E, _A) { aref = live.aref, agamma = live.agamma };
            field.Clear();
            double sBase = S / (double)SourcePx;
            int frames = Mathf.Max(1, ctx.frameCount);

            if (ctx.swarm == null)
            {
                field.ox = S * 0.5; field.oy = S * 0.5; field.s = sBase;
                DrawOne(field, ctx.life, ctx.frameIndex, seed, 0);
            }
            else
            {
                for (int i = 0; i < ctx.swarm.Length; i++)
                {
                    var sp = ctx.swarm[i];
                    if (sp.own < 0f || sp.own > 1f) continue;
                    // swarm positions are y-up canvas px; the planes are y-down
                    field.ox = sp.x * k; field.oy = S - sp.y * k;
                    field.s = sBase * live.swarmSize * Mathf.Max(sp.sizeMul, 0.01f);
                    int fi = Mathf.RoundToInt(sp.own * (frames - 1));
                    DrawOne(field, sp.own, fi, seed, 7919 * (sp.index + 1));
                }
                field.ox = S * 0.5; field.oy = S * 0.5; field.s = sBase;
            }

            // every collected stroke lands now; then bloom once over the whole frame (the source blooms per frame
            // after every stroke of that frame)
            field.Flush();
            field.Bloom(live.bloomRadius * sBase, live.bloomStrength, live.bloomStrength * live.bloomAlpha, _sA, _sB);

            // colorize: band by energy threshold, alpha = A (truncated to a byte like astype(uint8)), absent under the floor
            ReadBands();
            float alphaMul = Mathf.Clamp01(ctx.alpha);
            bool dump = PyreFormDebug.FieldSink != null;
            for (int i = 0; i < n; i++)
            {
                float e = _E[i], a = _A[i];
                if (a > 1f) a = 1f; else if (a < 0f) a = 0f;
                if (e < Floor) a = 0f;
                a *= alphaMul;
                var c = _lut.Sample32(e);
                // premultiplied planes for the box-down; at k = 1 the quantisation below is the source's own
                _pr[i] = c.r / 255f * a; _pg[i] = c.g / 255f * a; _pb[i] = c.b / 255f * a; _pa[i] = a;
            }
            if (k == 1)
            {
                int W = ctx.W, H = ctx.H;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        int i = y * S + x;
                        float a = _pa[i];
                        byte ab = (byte)Mathf.Clamp((int)(a * 255f), 0, 255);   // truncation, as the source
                        var c = ab == 0 ? default : _lut.Sample32(_E[i]);
                        if (ab != 0) c.a = ab;
                        target[(H - 1 - y) * W + x] = c;                           // y-down plane → y-up buffer
                    }
            }
            else PyreSupersample.Downsample(_pr, _pg, _pb, _pa, S, S, k, target, ctx.W, ctx.H, flipY: true);

            if (dump)
            {
                var T = new float[n];
                var Ac = new float[n];
                for (int i = 0; i < n; i++) { T[i] = Mathf.Clamp01(_E[i]); Ac[i] = Mathf.Clamp01(_A[i]); }
                _dumpH = PyreArcBurst.BoxDown(_E, S, k); _dumpT = PyreArcBurst.BoxDown(T, S, k); _dumpA = PyreArcBurst.BoxDown(Ac, S, k);
                FlipY(_dumpH, S / k); FlipY(_dumpT, S / k); FlipY(_dumpA, S / k);
            }
            ApplyPixelModifiers(ctx, target);
        }

        void DrawOne(ArcField field, float life, int frameIdx, int seed, int streamSalt)
        {
            var F = new PyreArcBurst.Frame
            {
                f = field, t = Mathf.Clamp01(life), seed = seed,
                i = (rerollPerFrame ? frameIdx : 0) + streamSalt,
                frameNo = rerollPerFrame ? frameIdx : 0,
                widthK = live.widthScale, ampK = live.ampScale, hueFloor = live.keepHueFloor, deepLo = ghostDeepLo, deepHi = ghostDeepHi,
            };
            switch (layout)
            {
                case Layout.Core: PyreArcBurst.DrawCore(F, core); break;
                case Layout.Weave: PyreArcBurst.DrawWeave(F, weave); break;
                case Layout.Bolt: PyreArcBurst.DrawBolt(F, bolt); break;
                case Layout.Crown: PyreArcBurst.DrawCrown(F, crown); break;
                case Layout.Lattice: PyreArcBurst.DrawLattice(F, lattice); break;
                case Layout.Terminal: PyreArcBurst.DrawTerminal(F, terminal); break;
                case Layout.Cage: PyreArcBurst.DrawCage(F, cage); break;
                case Layout.Stipple: PyreArcBurst.DrawStipple(F, stipple); break;
                case Layout.Pinch: PyreArcBurst.DrawPinch(F, pinch); break;
                default: PyreArcBurst.DrawLichten(F, lichten); break;
            }
        }

        static void FlipY(float[] p, int s)
        {
            for (int y = 0; y < s / 2; y++)
                for (int x = 0; x < s; x++)
                {
                    int a = y * s + x, b = (s - 1 - y) * s + x;
                    (p[a], p[b]) = (p[b], p[a]);
                }
        }

        /// The layer's pixel modifiers, per lit canvas pixel (the heat-ramp forms' convention).
        void ApplyPixelModifiers(in PyreFormCtx ctx, Color32[] target)
        {
            if (ctx.pix == null || ctx.pix.Length == 0) return;
            int W = ctx.W, H = ctx.H;
            int pixHash = PyreRenderer.Hash(ctx.seed, PyreRenderer.ModParticleIndex, ctx.layerSalt, 0x2f);
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
        /// Apply a contract param by key: `layout` / `draw` (a draw name), `palette` (a PAL name), `bloom_alpha_strength`
        /// (absolute → the ratio dial), and the plain same-named float / int / bool fields. The per-layout literals
        /// (ghost, dissolve, cool) are already each layout's defaults, so those keys answer true without a change.
        /// Returns false for an unknown key.
        public bool SetContractParam(string key, object value)
        {
            switch (key)
            {
                case "layout": case "draw": case "tag":
                    if (Enum.TryParse(value?.ToString(), true, out Layout l)) { layout = l; return true; }
                    return false;
                case "palette": { var r = ArcBands.Get(value?.ToString()); if (r == null) return false; palette = r; return true; }
                case "bloom_alpha_strength": bloomAlpha = new ZUIValue(bloomStrength.staticValue > 0f ? Convert.ToSingle(value) / bloomStrength.staticValue : 0.34f); return true;
                case "ghost": case "dissolve": case "cool": case "bands": case "keep_hue": case "stroke_profile": case "deposit_alpha": case "seg_cull":
                    return true;
            }
            string name = key.Replace("_", "");
            foreach (var fi in GetType().GetFields())
            {
                if (!string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (fi.FieldType == typeof(int)) fi.SetValue(this, Convert.ToInt32(value));
                else if (fi.FieldType == typeof(float)) fi.SetValue(this, Convert.ToSingle(value));
                else if (fi.FieldType == typeof(bool)) fi.SetValue(this, Convert.ToSingle(value) != 0f);
                else if (fi.FieldType == typeof(ZUIValue)) fi.SetValue(this, new ZUIValue(Convert.ToSingle(value)));   // a contract scalar = the Static value
                else return false;
                return true;
            }
            return false;
        }
    }
}
