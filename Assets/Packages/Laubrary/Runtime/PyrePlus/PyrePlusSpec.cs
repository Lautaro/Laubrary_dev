// PyrePlusSpec — the asset behind the PyrePlus prototype (see PYREPLUS_DESIGN.md).
//
// PyrePlus is a PARALLEL rework of Pyre's shape system, kept entirely separate from the shipping
// Pyre/BlastSpec/Layer/BlastRenderer so the real tool is never at risk. R3 makes it MULTI-LAYER: the spec now
// holds a LIST of PyrePlusLayer (index 0 = back, painted first), and every per-particle / form / swarm /
// modifier field lives on the layer — only the canvas, timing, seed, background and preview state stay on the
// spec. A single default layer renders byte-identical to the pre-R3 single-layer spec. Layers also carry Pyre's
// matte-channel idea, SIMPLIFIED: a layer is either Drawn or writes its coverage into one of four numbered
// mask channels, and a Draw layer can clip its own alpha by any channel an earlier layer wrote.
using System.Collections.Generic;
using Laubrary.Pyre;
using TMPro;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    // How the swarm places its particles: uniformly inside the shape's area, or travelling along its outline.
    public enum SwarmSpawnMode { Area, Path }

    // The swarm shape: a regular polygon by side count (Circle = ∞ sides), a hand-authored polyline, or a straight
    // Line. Custom is Path-only (enforced in the UI later); the renderer treats Area+Custom as a Circle fallback.
    // Line (slice 3 — Bars) places the particles evenly along a straight horizontal segment through the shape centre
    // (endpoints at ±radius), independent of Area/Path (a Line is 1-D); the shared shape transform then rotates the
    // whole row, so shapeRotation IS the row angle. A row of Streak particles on a Line = Pyre's Bars row.
    // APPEND ONLY — serialized as an int, so never reorder or insert (Line was appended after Custom).
    public enum SwarmShapeKind { Circle, Triangle, Square, Pentagon, Hexagon, Custom, Line }

    // How the swarm ORIENTS each particle as it's placed — the per-particle facing the renderer folds into the
    // form's own rotation (Streak forward, Disc/Sprite spin, Text/solid roll). None = no turning (every particle
    // keeps its own orientation). Outward = face away from the shape centre. PathTangent = face along the outline
    // it rides (readable text along a path); in Area mode PathTangent falls back to Outward. See ComputeSpawns'
    // orientDeg + PyrePlusRenderer's per-form fold.
    public enum SwarmOrient { None, Outward, PathTangent }

    // How the swarm distributes its spawns IN TIME. Window (the default) = the swarmSpawnTiming envelope maps each
    // particle's number to its spawn moment on the blast timeline (it IS the whole mapping now). FrameStep =
    // spawn the first particle at frame `swarmFirstFrame`, then one more every `swarmFrameStep` frames until the
    // count is fulfilled (step 0 = all on that one frame). Default Window keeps a default swarm byte-identical.
    public enum SwarmTiming { Window, FrameStep }

    // The particle's rendered FORM. One implicit layer, so the whole swarm is one form. The STATELESS forms:
    //   Disc     — a flat soft disc (slice 1).
    //   Gem      — a true-3D lit faceted octahedral crystal (per-pixel point lighting, hard edge lines, two
    //              staggered glows; see PyrePlusRenderer.DrawFacetSolid).
    //   Crescent — a disc with a second offset disc masked out (its own bite size / facing / push-out).
    //   Sparkle  — random lit pixel-cells scattered inside the disc, twinkling deterministically per frame.
    //   Sprite   — a Sprite's pixels stamped, scaled/rotated to the particle, optionally tinted by the gradient.
    //   Box      — a true-3D lit cuboid (8 verts / 6 quads), the Gem's facet pipeline. Aspect = height, Depth = z.
    //   Pyramid  — a true-3D lit square pyramid (apex + base), the Gem's facet pipeline. Aspect = apex height.
    //   Can      — a true-3D lit 16-sided cylinder (barrel + two caps), the Gem's facet pipeline. Aspect = height.
    //   Orb      — a true-3D lit SPHERE, analytic (no facets). Its silhouette is a plain circle that NEVER changes
    //              under spin/tilt — the LIGHTING FRAME rotates instead, so the shading + specular hotspot roll
    //              around the ball as it spins (see PyrePlusRenderer.DrawOrb).
    //   Ring     — a flat two-sided tilted annulus (a Saturn ring), analytic. Outer radius = size, inner hole =
    //              size·ringInner; gemTilt opens/closes the ellipse (0° face-on, 90° edge-on) and particleSpin
    //              rolls it in-plane (see PyrePlusRenderer.DrawRing).
    //   Text     — every character of a free string is one particle, drawn from a TMP SDF font atlas: a spatial
    //              gradient fill, an optional border, and optional 3D extrusion (front face + darker sides). Swarm
    //              OFF ⇒ the whole string lays out as one centred line; ON ⇒ each character rides a swarm position.
    //              gemTilt tips the letters and particleSpin yaws each one about its own centre (see
    //              PyrePlusRenderer.DrawTextChar / RenderTextLine). Text overrides the swarm particle COUNT to the
    //              string length.
    //   Streak   — an ANCHOR-BIASED comet-tail capsule/rect: the particle sits at streakAnchor along the streak
    //              (0 = tail/grows forward, 0.5 = centred, 1 = tip/grows backward), oriented default up/+y (the
    //              swarm Orient or its own spin steer it). Its own streakLength/streakWidth envelopes drive it
    //              (NOT `size`); streakSoftTip feathers BOTH ends; edgeSoftness feathers the two long sides
    //              (see PyrePlusRenderer.DrawStreakBody).
    //   Star     — a filled star POLYGON: N arms (starArms) with tips at radius R (= `size`·sizeMul) and inner
    //              (valley) vertices at radius R·(1−starLength); starBaseWidth sets each valley's angular position
    //              inside its sector (1 = the classical pentagram midpoint) and starSkew swirls the valleys into a
    //              pinwheel. A flat 2D form (shapeFill colours it, edgeSoftness feathers the rim). Star-shaped about
    //              its centre, so the inside test is a per-ray boundary (see PyrePlusRenderer.DrawStarBody).
    // Box/Pyramid/Can share the Gem's shared 3D block (tilt, light, lines, glows) — see PyrePlusRenderer.DrawFacetSolid.
    // Orb/Ring reuse that SAME per-pixel lighting/lines/glows math analytically (point light + Blinn-Phong, halo +
    // inner glow, edge lines) but with sphere/annulus geometry instead of facets.
    // APPEND ONLY — the values are serialized as ints, so never reorder or insert. (Fire and Fireball are the only
    // simulation-backed forms — they retain frame-to-frame grid state and need a replay harness — and are
    // deferred; they are NOT here. HeightBalls is NOT a sim: it is CLOSED-FORM / stateless, proven at
    // Runtime/Pyre/BlastRenderer.cs:1493-1496 — every ball's whole state at a frame is a pure function of
    // (layer hash, group, ball index, layer life), nothing accumulates between frames — so it arrives as a
    // stateless Coalesce (Ramp) field-pass mode alongside MetaBlob, never as a deferred sim form here.)
    public enum ShapeForm { Disc, Gem, Crescent, Sparkle, Sprite, Box, Pyramid, Can, Orb, Ring, Text, Streak, Star }

    // How the Text form's spatial fill gradient is applied. PerCharGradient = every letter contains the WHOLE
    // gradient (across its own box, along the rotated fill axis). PerCharStep = every letter is ONE flat colour,
    // grad(i/(n-1)) by index. TextGradient = ONE gradient swept across the whole line's extent (swarm ON has no
    // line, so it degrades to PerCharStep). See PyrePlusRenderer's SampleTextColor.
    public enum TextFillMode { PerCharGradient, PerCharStep, TextGradient }

    // A layer's ROLE in the stack. Draw = composite it onto the frame as normal. WriteMatte = do NOT composite
    // it; instead write its per-pixel COVERAGE (the alpha it would draw, before colour) into one of four numbered
    // mask channels, which the Draw layers above it can clip by. Simplified from vanilla Pyre's Draw/Matte — see
    // PyrePlusRenderer's matte section for the full drift note.
    // LumaMatte (slice 4) = a NEW, PARALLEL, opt-in role that ports Pyre1's full six-channel LUMINANCE matte
    // (BlastRenderer's Draw/Matte): don't composite it; build a luminance×alpha MASK from its finished pixels and
    // impose the matteFlags channels (Alpha/Brightness/Saturation/Hue/Blur/Displace) on the layers above, scoped by
    // matteScope. It is ADDITIVE — the WriteMatte numbered-channel coverage-clip path above is untouched, so existing
    // specs render byte-identical. APPEND ONLY — serialized as an int, so never reorder or insert (LumaMatte appended
    // after WriteMatte: Draw=0, WriteMatte=1, LumaMatte=2).
    public enum MatteRole { Draw, WriteMatte, LumaMatte }

    // (slice 4 — LumaMatte) Which effects a LumaMatte layer's mask imposes on the layers above it. A [Flags] set —
    // any combination acts at once, applied in a FIXED order (spatial first, then colour, then alpha) so a
    // combination is deterministic regardless of which bits are set. Mirrors Pyre1's PyreEnums.MatteChannel exactly
    // (same names + bit values); a NEW PyrePlus-local enum (distinct from the existing `matteChannel` int, which is
    // the WriteMatte numbered channel and is left untouched). None ⇒ ApplyMatte falls back to Alpha (legacy guard).
    [System.Flags]
    public enum MatteChannel
    {
        None       = 0,
        Alpha      = 1 << 0,   // classic luma matte: mask multiplies the covered layers' opacity
        Brightness = 1 << 1,   // darken toward black where the mask is low — a shadow/light pass
        Saturation = 1 << 2,   // drain toward greyscale where the mask is low (ash, smoke, heat-death)
        Hue        = 1 << 3,   // rotate hue by the mask — heat shimmer, chemical burn, cold spots
        Blur       = 1 << 4,   // soften where the mask is high, sharp where it's low
        Displace   = 1 << 5,   // push pixels along the mask's own SLOPE — refraction, heat haze, lensing
    }

    // (slice 4 — LumaMatte) How far up the stack a LumaMatte layer reaches. Mirrors Pyre1's PyreEnums.MatteScope.
    public enum MatteScope
    {
        NextLayer,   // clip only the next drawn layer above it (Photoshop's clipping-mask behaviour)
        AllAbove,    // affect every layer above it, until another LumaMatte replaces it
    }

    // How a WriteMatte layer's coverage combines with whatever is already in its channel (earlier WriteMatte
    // layers can target the same channel). Max (the default) = the classic union of masks; Add = accumulate and
    // clamp; Subtract = carve one mask out of another. The cheap mirror of vanilla Pyre's combinable mattes.
    public enum MatteCombine { Max, Add, Subtract }

    // A layer's RENDER MODE — how it turns its swarm into pixels (slice 0 seam). Off (the default) = PER-PARTICLE
    // Over-compositing: the swarm loop draws each particle and composites it (every form today). Fuse / Ramp are the
    // two STATELESS FIELD-PASS modes: after the swarm loop the WHOLE placed particle set is read as one field,
    // accumulated → thresholded/ramped → shaded, and composited as a single merged silhouette instead of drawn per
    // particle. Fuse = MetaBlob's metaball fuse (slice 1: RenderPlusFusedField); Ramp = HeightBalls' density/height
    // relief (slice 2: RenderPlusRampField). Off stays byte-identical to pre-Coalesce. See PyrePlusRenderer.Render-
    // Swarm's Coalesce seam. APPEND ONLY — serialized as an int, so never reorder or insert.
    public enum LayerCoalesce { Off, Fuse, Ramp }

    // ── one PyrePlus layer — everything per-particle / per-form / per-swarm / per-modifier, plus its matte role ──
    // The spec holds a LIST of these (index 0 at the BACK). A single default layer's fields carry the pre-R3
    // defaults verbatim, so a one-layer spec renders byte-identical to the old flat spec. The Default*() factories
    // that seed the animatable defaults live here with the fields they initialise.
    [System.Serializable]
    public class PyrePlusLayer
    {
        // ── list identity (R3) ─────────────────────────────────────────────────────
        public bool enabled = true;      // hidden layers are skipped entirely by the renderer
        public string name = "Layer";    // shown in the layer list; rename-in-place

        // ── matte (R3) — Pyre's matte idea, simplified to numbered channels ─────────
        // UI-gating only: whether this layer's Matte box is enabled/shown in the layer list (mirrors Pyre1's
        // per-row Matte toggle). The RENDERER never reads it — turning it OFF also resets the layer to inert
        // (matteRole=Draw, clipByChannel=-1) so a previously-configured matte stops acting. Value type, so
        // MemberwiseClone in Clone() copies it for free.
        [HideInInspector] public bool matteEnabled = false;
        // Draw = composite normally (the default; a Draw layer with clipByChannel < 0 is exactly a pre-R3 layer).
        // WriteMatte = invisible; write this layer's coverage into channel `matteChannel` for the Draw layers above.
        public MatteRole matteRole = MatteRole.Draw;
        public int matteChannel = 0;               // 0..3 — which channel a WriteMatte layer writes into
        public MatteCombine matteCombine = MatteCombine.Max;   // how it combines with what's already in that channel
        public int clipByChannel = -1;             // Draw only: -1 = no clip; 0..3 = multiply this layer's alpha by that channel
        public bool clipInvert = false;            // Draw + clip: use (1 - channel) instead of channel

        // ── Matte heightmap (slice 4b) — the numbered-channel path EXTENDED into a fused scalar HEIGHTMAP ────────
        // Two opt-in, default-OFF additions on top of the WriteMatte coverage-clip plumbing above, so several matte
        // layers can contribute LUMINANCE into ONE channel (a fused scalar field) and a target Draw layer renders
        // that field as a relief-lit surface (like HeightBalls, but fed by authored matte layers instead of a swarm):
        //   • matteWriteLuma  — a WriteMatte layer deposits its LUMINANCE × alpha (not flat coverage-alpha) into
        //                        matteChannel, combined by the same MatteCombine (Max = "wherever they intersect,
        //                        fused"). Several such layers on one channel = one fused heightmap.
        //   • heightFromChannel — a Draw layer, when ≥ 0, does NOT draw its shape; it renders channels[that] through
        //                        this layer's shapeFill gradient, relief-lit by PyrePlusField.ReliefLight, Over-comp.
        // Both default to their no-op (false / -1), so a layer that opts into neither renders byte-identical. All four
        // are value types ⇒ Clone()'s MemberwiseClone copies them for free (no explicit deep-copy — see Clone below).
        [Tooltip("Write matte: deposit this layer's LUMINANCE × alpha into its channel (a heightmap contribution) instead of flat coverage-alpha. Several such layers on one channel (Combine = Max) fuse into one heightmap for a Height-from Draw layer to render.")]
        public bool matteWriteLuma = false;
        [Tooltip("Draw: -1 = off (draw this layer's shape normally). 0..3 = don't draw the shape; render that fused matte channel as a relief-lit heightmap through this layer's Fill gradient (bright/lit where the source luminance piles up).")]
        public int heightFromChannel = -1;
        [Tooltip("Heightmap consumer: relief strength — how steeply the fused field's local slope bends the surface normal for carved highlights/shadow. 0 = a flat gradient-mapped field, no relief lighting.")]
        public float heightRelief = 3f;
        [Tooltip("Heightmap consumer: the relief light's angle in degrees (screen plane) — which way the highlights fall across the fused surface.")]
        public float heightLightAngle = 135f;

        // ── Luma-matte (matteRole == LumaMatte, slice 4) — Pyre1's six-channel luminance matte, added PARALLEL ──
        // All-NEW fields (no renames of the coverage-clip fields above, so no data migration): a LumaMatte layer is
        // invisible; its finished pixels become a MASK = luminance × its own alpha, and the matteFlags channels are
        // imposed on the layers above (scoped by matteScope). Ported verbatim from BlastRenderer's Draw/Matte model.
        // The mask STRENGTH + the Blur/Displace/Hue amounts are ZUIValues over the matte layer's OWN life (deep-copied
        // in Clone below); matteFlags/matteScope/matteInvert are value types (MemberwiseClone copies them). Read ONLY
        // when matteRole == LumaMatte — inert for Draw/WriteMatte layers, so existing specs are byte-identical.
        public MatteChannel matteFlags = MatteChannel.Alpha;   // which channels this matte imposes (default: classic alpha luma matte)
        public MatteScope matteScope = MatteScope.NextLayer;   // NextLayer = the next drawn layer only; AllAbove = every layer above until replaced
        public bool matteInvert = false;                       // mask = 1 - mask (impose where the matte is DARK)
        // α-strength source (slice 5). When ON, the mask is modulated per COVERED pixel by that pixel's OWN alpha as
        // (1 − α): fully opaque interior ⇒ no effect, soft / thin / anti-aliased EDGE pixels ⇒ full effect — an
        // edge/soft-region mask derived from coverage (fresnel-like, but from alpha). Does NOT change how the mask is
        // BUILT (still luminance × the matte layer's alpha); only how ApplyMatte applies it. The snapshot of each
        // covered layer's alpha is taken ONCE at ApplyMatte entry (before any channel rewrites it). Default OFF ⇒ the
        // effMask == mask no-op ⇒ byte-identical to the plain luma matte. The Alpha channel is deliberately excluded
        // from this modulation (near-degenerate). Value type ⇒ MemberwiseClone in Clone() copies it for free.
        public bool matteAlphaSource = false;
        public ZUIValue matteStrength = new ZUIValue(1f);      // master mask strength 0..1 over the matte layer's own life
        public ZUIValue matteBlurAmount = new ZUIValue(3f);    // Blur channel: max radius (px) where the mask is full
        public ZUIValue matteDisplaceAmount = new ZUIValue(4f);// Displace channel: how far (px) mask edges push pixels
        public ZUIValue matteHueDegrees = new ZUIValue(60f);   // Hue channel: hue rotation (degrees) where the mask is full

        // ── Coalesce render-mode — how this layer turns its swarm into pixels ────────────────────────────
        // Off (default) = per-particle Over-compositing, byte-identical to pre-Coalesce (every form today). Fuse/Ramp
        // are the two STATELESS field-pass modes: read the whole swarm as a field and composite one merged silhouette
        // instead of drawing each particle. Fuse = MetaBlob (slice 1, the fuse* dials below); Ramp = HeightBalls
        // (slice 2, the density/heat envelopes + ramp* knobs below). Value type ⇒ Clone()'s MemberwiseClone copies
        // it for free (like matteRole). APPEND ONLY — serialized as an int.
        public LayerCoalesce coalesce = LayerCoalesce.Off;

        // ── Fuse (Coalesce == Fuse) field-pass dials (slice 1) — MetaBlob's iso-surface controls ─────────
        // Only read when coalesce == Fuse. The swarm's placed particles become metaball circles summed into ONE
        // scalar field (Σ weight·(1−d²/r²)²); these three shape how that field turns back into pixels — the exact
        // twins of Pyre1's metaThreshold/metaShadeRange/metaSoftness (defaults copied from Layer.cs so a fused blob
        // reads the same). Plain floats (like their Pyre1 counterparts), so MemberwiseClone in Clone() copies them.
        [Tooltip("Fuse: iso-threshold. Lower = the particles fuse more eagerly (fatter necks, one shape); higher = distinct lobes.")]
        public float fuseThreshold = 0.6f;
        [Tooltip("Fuse: how much field above the threshold spans the gradient (surface → core). Smaller = a punchier core.")]
        public float fuseShadeRange = 1.5f;
        [Tooltip("Fuse: edge softness — the alpha AA band across the iso-surface (capped at the threshold). 0.01 ≈ crisp.")]
        public float fuseSoftness = 0.18f;

        // ── Ramp (Coalesce == Ramp) field-pass dials (slice 2) — HeightBalls' density-relief controls ─────
        // Only read when coalesce == Ramp. The swarm's placed particles become DOMES fused into three shared scalar
        // fields (density / heat / height) by a SmoothMax, relief-lit from the height slope, and shaded through the
        // Shape's Fill as one smoke→fire cloud (see PyrePlusRenderer.RenderPlusRampField). Two NEW per-particle
        // envelopes carry the weights a plain swarm doesn't: `density` = a ball's MASS/body, `heat` = its
        // height/ENERGY (how far up the fire ramp it sits, how tall it stands in the relief light) — both over the
        // particle's OWN life, evaluated in the collect-loop beside size/alpha (field ids -25/-26). The rest are
        // plain-float shaping knobs (like the fuse dials, so MemberwiseClone copies them). Defaults mirror Pyre1's
        // HeightBalls layer + group (BlastRenderer/Layer.cs) so a Ramp swarm reads familiar. `density`/`heat` are
        // ZUIValues, so Clone() deep-copies them.
        [Tooltip("Ramp: a particle's MASS/body over its own life — gives the cloud volume that catches the relief light and nudges it up the ramp even with no heat. The density field's per-particle weight.")]
        public ZUIValue density = DefaultRampDensity();
        [Tooltip("Ramp: a particle's HEIGHT/energy over its own life — how far up the smoke→fire ramp it sits (low = cold smoke, high = fire) and how tall it stands in the relief light.")]
        public ZUIValue heat = DefaultRampHeat();
        [Tooltip("Ramp: fusion knee — how eagerly neighbouring domes MELT into one mass. 0 = a hard max (distinct orbs); higher = a smoother, heavier merged cloud.")]
        public float rampFusion = 0.35f;
        [Tooltip("Ramp: opacity gain — how much combined density+heat becomes alpha. Higher = a more solid, opaque cloud.")]
        public float rampCoverage = 8f;
        [Tooltip("Ramp: light the cloud's relief from the height field's local slope — carved highlights and shadow. Off = a flat gradient cloud.")]
        public bool rampLighting = true;
        [Tooltip("Ramp: relief strength — how steeply the height slope bends the surface normal. Higher = a more sharply carved, bumpier lit surface.")]
        public float rampRelief = 3f;
        [Tooltip("Ramp: the relief light's angle in degrees (screen plane) — which way the highlights fall across the cloud.")]
        public float rampLightAngle = 135f;
        [Tooltip("Ramp: surface-noise rim — deforms the shared cloud rim so neighbouring domes bulge/pinch together and read as ONE boiling mass instead of fused flat discs. 0 = a smooth rim.")]
        public float rampRimScale = 0.35f;

        // ── Shape — the particle's own look (mandatory section) ────────────────────
        // Which FORM the particle renders as. Disc = the flat soft disc (slice 1). Gem = a true-3D lit crystal
        // (the Gem block below drives it); its material colour is shapeFill, its radius is `size`.
        public ShapeForm shapeForm = ShapeForm.Disc;
        // The particle's colour, as a ZuiFill: Solid (one flat colour), OverLife (a gradient over the particle's
        // life — the default), or a SPATIAL fill (Linear / Radial / Noise, sampled across the particle by its
        // normalized local point). Every mode is alpha-capable. Default = OverLife with the fire gradient, which
        // renders byte-identical to the old colorOverLife path. Text ignores this (it has its own richer per-char
        // fill system below).
        public ZuiFill shapeFill = DefaultShapeFill();
        public ZUIValue alpha = DefaultAlpha();     // over the particle's OWN life (multiplies the final output alpha for BOTH forms)
        public ZUIValue size = DefaultSize();       // radius in pixels, over the particle's own life (Gem: the girdle radius)
        [Range(0f, 1f)] public float edgeSoftness = 0.4f;   // soft rim vs hard pixel edge (Disc form only)

        // ── Gem form (shapeForm == Gem) — a true-3D faceted crystal ────────────────
        // Octahedral by default (gemSides == 4): a square girdle with a crown point above and a longer pavilion
        // point below, lit per-pixel. Base colour = shapeFill at the particle's own life (a blue gradient =
        // a sapphire). The `size` envelope is the single scale driver — the girdle radius R = evaluated size.
        [Range(3, 8)] public int gemSides = 4;              // girdle vertex count (4 = the approved octahedron)
        public float gemCrown = 0.75f;                      // crown height as a fraction of R (the girdle radius)
        public float gemPavilion = 1.55f;                   // pavilion depth as a fraction of R
        public ZUIValue gemTilt = new ZUIValue(18f);        // TILT about X (lean top toward/away), degrees, over the particle's own life
        // ROLL about Z in model space (rotate flat against the screen), degrees, over the particle's own life —
        // applied FIRST, before the shared spin (Turn/yaw) and the tilt. Default Static 0 (a no-op — the renderer's
        // exact roll==0 guard keeps a default solid byte-identical). For the Orb it rolls the lit hotspot around
        // the ball; geometrically a no-op for the symmetric Ring (a flat ring rolled in its own plane is unchanged).
        public ZUIValue gemRoll = new ZUIValue(0f);
        // Lighting. The light POSITION derives in the renderer from these two angles at distance gemLightDistance·R
        // (default 3.5) with a (gemLightDistance + 1.2)·R falloff range (so the falloff tracks the distance — a far
        // light still reaches the solid). The 3.5 default reproduces the prototype's 3.5·R / 4.7·R proportions
        // exactly (3.5 + 1.2 == 4.7 in float, so a default solid renders byte-identical).
        public float gemLightYaw = -55f;                    // key-light azimuth (left/right), degrees
        public float gemLightPitch = 38f;                   // key-light elevation (above/below the horizon), degrees
        // The key light's DISTANCE from the solid, as a multiple of the radius R — the light's real 3rd degree of
        // freedom (position = the two angles above + this radius; a 3rd ROTATION would be meaningless for a point
        // light). Closer = a tighter, brighter hotspot; farther = flatter, more even light. Default 3.5 reproduces
        // the pre-existing hardcoded 3.5·R position + 4.7·R falloff byte-for-byte.
        [Range(1.5f, 8f)] public float gemLightDistance = 3.5f;
        [Range(0f, 1f)] public float gemAmbient = 0.05f;    // non-directional base light on ALL faces (near-zero keeps it contrasty)
        // DIFFUSE strength of the key light on facing surfaces (Lambert term): lit = gemAmbient + gemDiffuse·ndl·atten.
        // Default 2.1 reproduces the pre-P5 hardcoded 2.1 byte-for-byte; drop it to 0 and only ambient + spec light the
        // faces (the missing dial that let ambient=spec=0 still show bright diffuse-lit faces — the P5 fix).
        [Range(0f, 3f)] public float gemDiffuse = 2.1f;
        [Range(0f, 2f)] public float gemSpecular = 0.9f;    // Blinn-Phong highlight strength (the tight hotspot)
        // SPECULAR exponent (Blinn-Phong power) = the highlight's TIGHTNESS: higher = a smaller, sharper hotspot.
        // Default 48 reproduces the pre-P5 hardcoded pow(·,48) byte-for-byte; lower it to spread the highlight so it's
        // actually visible (48 was so tight it rarely showed).
        [Range(2f, 128f)] public float gemSpecPower = 48f;
        public ZuiFill gemSpecularFill = new ZuiFill(new Color(0.9f, 0.95f, 1f));   // Blinn-Phong highlight fill (Solid by default)
        // Light-catching hard edge lines along every visible facet boundary.
        [Range(0f, 3f)] public float gemLineWidth = 1f;     // edge-line width in screen pixels (0 = no lines)
        public ZuiFill gemLineFill = new ZuiFill(new Color(1f, 0.98f, 0.90f));      // hard facet edge-line fill (Solid by default)
        // The two staggered glows — strength 0..1 over the particle's OWN life. EdgeGlow is a halo around the edge
        // lines that spills OUTSIDE the silhouette; InnerGlow is emissive light rising from the facet interiors.
        // Their defaults are STEADY (Static) so the light does NOT pulse out of the box — author a Curve on either
        // to make it breathe over the particle's life. Each has its OWN authorable fill: gemEdgeGlowFill (default
        // equals gemLineFill's default colour so a default solid is unchanged) and gemInnerGlowFill.
        public ZUIValue gemEdgeGlow = DefaultEdgeGlow();
        public ZuiFill gemEdgeGlowFill = new ZuiFill(new Color(1f, 0.98f, 0.90f));  // edge-halo glow fill (Solid by default)
        public ZUIValue gemInnerGlow = DefaultInnerGlow();
        public ZuiFill gemInnerGlowFill = new ZuiFill(new Color(0.35f, 0.60f, 1f)); // facet inner-glow fill (Solid by default)

        // ── shared 3D-solid form fields (Box / Pyramid / Can) — the true-3D convex facet solids that reuse the
        //    Gem block above (tilt, light, lines, glows) but are NOT octahedral gems. Both are fractions of the
        //    base size R (= evaluated `size` × sizeMul). Gem itself ignores these (it uses gemSides/Crown/Pavilion).
        [Range(0.3f, 3f)] public float solidAspect = 1f;    // height / width — Box height, Pyramid apex height, Can height
        [Range(0.2f, 2f)] public float solidDepth = 1f;     // depth / width — Box z-extent, Pyramid base z-extent (unused for Can)

        // ── Ring form (shapeForm == Ring) — a flat two-sided tilted annulus (a Saturn ring) ───────────────────
        // Outer radius R = evaluated `size` × sizeMul; inner hole radius = R·ringInner. gemTilt tips the ring
        // (0° face-on → 90° edge-on) and particleSpin rolls it in-plane; both reuse the shared lighting/lines/glows
        // above. Not animatable (a plain float) — the ring's animation lives in size/tilt/spin.
        [Range(0.1f, 0.92f)] public float ringInner = 0.55f;   // inner radius as a fraction of the outer radius

        // ── Text form (shapeForm == Text) — a string rendered as extruded SDF letters ────────────────────────
        // Every character is one particle. `size` is the character HEIGHT in pixels; each glyph is scaled so its
        // height matches it. Text does NOT use shapeFill — its colour is the SPATIAL fill below.
        public string textString = "PYRE";
        // The SDF font atlas. Null = the renderer auto-finds the first TMP_FontAsset with a READABLE atlas (an
        // editor-only AssetDatabase lookup, cached); at runtime with nothing found the form falls back to a plain
        // Disc per character. The atlas must be Read/Write-enabled (a Dynamic SDF font works).
        public TMP_FontAsset textFont;
        // Which fill mode paints the letters. (Text ignores shapeFill entirely — see the enum.) This is the SCOPE
        // over which the fill's gradient is swept; the fill's OWN mode (Solid/OverLife/Linear/Radial) is not read
        // for text — the renderer reads the fill's gradient (or its colour when Solid, or its texture when a
        // texture is set). See PyrePlusRenderer.SampleTextColor.
        public TextFillMode textFillMode = TextFillMode.PerCharGradient;
        // The letter fill, as a ZuiFill. NOT over the particle's life — the renderer sweeps the fill's GRADIENT in
        // SPACE across the char/line per textFillMode (unlike every other form, Text does not read shapeFill).
        // A Solid fill paints one flat colour; a TEXTURE (sprite/noise/grid/dots) stamps the letters directly.
        // Default = an OverLife ZuiFill carrying the deep-crimson→orange→gold Pyre ramp, so the renderer reads the
        // same fire gradient it read from the old textFillGradient — byte-identical.
        public ZuiFill textFill = DefaultTextFill();
        // Rotates the fill axis. Convention: 0 = vertical bottom→top for PerCharGradient (PerCharStep is index-based
        // and ignores it); for TextGradient 0 = left→right across the whole line.
        [Range(-180f, 180f)] public float textGradientAngle = 0f;
        // Letter outline, in SCREEN pixels (0 = no border). Drawn as an SDF band just inside each glyph edge,
        // coloured from textBorder sampled with the SAME fill mode + angle as the fill.
        [Range(0f, 4f)] public float textBorderWidth = 1f;
        // The border fill — a single-colour gradient reads as a solid outline; a texture stamps the outline band.
        // Default = an OverLife ZuiFill carrying the warm-white ramp (byte-identical to the old textBorderGradient).
        public ZuiFill textBorder = DefaultTextBorder();
        // Advance multiplier for the centred line layout (swarm OFF): <1 tightens the letters, >1 spreads them.
        [Range(0.6f, 1.6f)] public float textSpacing = 1f;
        // 3D extrusion. Solid = extruded letter boxes (lit front face + darker sides); false = a flat 2D plane.
        public bool textSolid = true;
        [Range(0.05f, 1f)] public float textDepth = 0.35f;   // extrusion depth as a fraction of the char size

        // ── Crescent form (shapeForm == Crescent) — a disc with a second offset disc masked out ────────────
        // A pixel is lit when it's inside the main disc but NOT inside the bite (mask) disc. The bite disc sits
        // `crescentOffset·radius` out in the `crescentAngle` direction and is `crescentBite·radius` across.
        // Crescent REUSES `edgeSoftness` above for BOTH rims (the outer disc edge and the bite edge).
        public ZUIValue crescentBite = new ZUIValue(0.55f);   // mask disc size vs the main disc, 0..1, over own life
        public ZUIValue crescentAngle = new ZUIValue(0f);     // degrees — which way the bite faces, over own life
        [Range(0f, 1f)] public float crescentOffset = 0.5f;   // how far the bite disc is pushed out, as a fraction of radius

        // ── Sparkle form (shapeForm == Sparkle) — random lit cells scattered inside the disc ───────────────
        // A virtual grid of `sparkleSize`-px cells overlays the disc; a cell lights this frame iff its stable
        // per-cell presence draw < density AND its per-frame twinkle draw passes (both from the Hash funnel, no
        // state — see PyrePlusRenderer.DrawSparkleBody). Lit cells are hard full-colour pixels, no edge falloff.
        public ZUIValue sparkleDensity = new ZUIValue(0.35f);   // fraction of cells lit, 0..1, over own life
        [Range(1, 4)] public int sparkleSize = 1;               // lit pixel block size

        // ── Sprite form (shapeForm == Sprite) — stamp a Sprite's pixels ────────────────────────────────────
        // The sprite is scaled so its larger dimension maps to 2·radius, centred on the particle, rotated by
        // `particleSpin` (Sprite reuses the shared spin — no separate rotation field), point-sampled. Its
        // texture must be Read/Write-enabled to sample; a null image or a non-readable texture renders the Disc
        // fallback instead. `spriteTint` multiplies by shapeFill (sampled at the particle centre, own life); off =
        // raw sprite colours.
        public Sprite spriteImage;
        public bool spriteTint = true;

        // ── Streak form (shapeForm == Streak) — an ANCHOR-BIASED comet-tail capsule ─────────────────────────
        // The particle sits at fraction `streakAnchor` along the streak (0 = at the TAIL, grows forward; 0.5 =
        // centred; 1 = at the TIP, grows backward), oriented default up/+y (steered by the swarm Orient and its own
        // spin). Its length/width are their OWN envelopes over the particle's own life (NOT the shared `size`, which
        // is hidden in the UI for this form). Its colour is the shared shapeFill; a SPATIAL fill sweeps across the
        // streak — see the (u,v) mapping in DrawStreakBody.
        public ZUIValue streakLength = DefaultStreakLength();   // length forward, in px over own life (a shoot-out arc)
        public ZUIValue streakWidth = new ZUIValue(3f);         // thickness across, in px over own life
        // Where the particle sits ALONG the streak, as a fraction from the TAIL (0) to the TIP (1). 0.5 (the
        // default) centres it, so growing Length extends the streak SYMMETRICALLY and no longer slides it off the
        // particle — the particle is a stable anchor point. 0 = tail (grows forward), 1 = tip (grows backward).
        [Range(0f, 1f)] public float streakAnchor = 0.5f;
        [Range(0f, 1f)] public float streakSoftTip = 0.5f;      // alpha feather over softTip·(each end's length) at BOTH ends (sides reuse edgeSoftness)
        // Bars taper (slice 3): when ON (and the form is Streak), the per-index size multiplier swarmScaleByIndex
        // scales the streak's LENGTH only, NOT its width — equal-width bars of graduated length, the barTaper flame
        // silhouette from Pyre's Bars. OFF (the default) = swarmScaleByIndex scales BOTH length and width (the plain
        // sizeMul path), so an existing Streak swarm renders byte-identical. Value type ⇒ MemberwiseClone in Clone()
        // copies it for free (like streakAnchor). Only read for the Streak form; inert for every other form.
        // This is the barTaper half of Pyre's Bars mapping. NOTE — the OTHER Bars decay mode, BarDecay.Dissolve (a
        // per-index alpha front advancing across the row over the LAYER's life), is deliberately NOT ported: a
        // particle's own-life alpha can't express cross-index coordination. It's deferred to a future
        // swarmAlphaByIndex-over-life ("dissolve front"); see PYREPLUS_ADVANCED_DESIGN.md "Capability 4".
        public bool streakScaleLengthOnly = false;

        // ── Star form (shapeForm == Star) — a filled star polygon ────────────────────────────────────────────
        // N points. Tips at radius R (= evaluated `size` × sizeMul); inner (valley) vertices at radius
        // R·(1−starLength). A star polygon is star-shaped about its centre, so the renderer's inside test is a
        // per-ray boundary (the ray-segment intersection with the one tip→valley / valley→tip edge spanning that
        // ray's angular slot). Its colour is the shared shapeFill and edgeSoftness feathers the rim radially; the
        // shared `size` row stays visible (= the tip radius). See PyrePlusRenderer.DrawStarBody.
        [Range(2, 20)] public int starArms = 5;                 // point count 2..20 (5 = the classical five-pointed star)
        public ZUIValue starLength = new ZUIValue(0.62f);       // arm reach 0..1 over own life; inner radius = R·(1−length). 0.62 ≈ the golden-ratio pentagram inner radius
        public ZUIValue starBaseWidth = new ZUIValue(1f);       // valley angular position as a fraction of the half-sector, 0.1..1 over own life (1 = classical midpoint; smaller = thinner arm bases, wider valleys)
        public ZUIValue starSkew = new ZUIValue(0f);            // valley swirl in degrees −60..60 over own life — rotates the valleys, pinwheel-twisting the arms (clamped so valleys never cross tips)

        // ── opt-in Shape fields (T7) — the particle's OWN motion after birth, on its own life clock ────────
        // A per-particle travel path: canvas-pixel offsets ADDED to the particle's spawn position, evaluated on
        // its OWN life (0 = birth, 1 = death). Default Static 0 (a no-op — the renderer skips the Eval entirely
        // when both are Static 0, so a default asset renders byte-identical).
        public ZUIValue particlePathX = new ZUIValue(0f);
        public ZUIValue particlePathY = new ZUIValue(0f);
        // The particle's own rotation over its own life, degrees. DISC form: its pixels rotating IN PLACE (2D —
        // the pseudo-3D tilt belongs to the swarm shape transform shapePitch/shapeYaw, not here). GEM form: the
        // 3D solids' YAW about their vertical axis (see PyrePlusRenderer.DrawFacetSolid). Default Static 0 (a no-op).
        public ZUIValue particleSpin = new ZUIValue(0f);
        // Pure UI gate for the advanced controls above — cosmetic, NEVER read by the renderer (like previewZoom).
        [HideInInspector] public bool shapeAdvanced;

        // ── Swarm ──────────────────────────────────────────────────────────────────
        // Off ⇒ exactly one centred particle (the Shape section alone). On ⇒ swarmCount particles placed in a
        // shape. The full model lives here; the renderer uses count/window/particle-life + shapeScale, and does
        // full placement (T2): Area = uniform-by-area inside Circle or a regular polygon; Path = a point on the
        // outline positioned by swarmProgress, or the swarmCustomX/Y envelopes for Custom. The shared transform's
        // offset/rotation/pitch/yaw remain wired-not-applied (T3). Every shape-transform field below is a
        // per-spawn SNAPSHOT animatable: each particle
        // samples it at ITS OWN spawn frame and keeps that value for life, so an animated transform leaves a
        // trail of placements instead of retroactively sliding already-placed particles (see PyrePlusRenderer).
        public bool swarmEnabled = false;
        [Min(2)] public int swarmCount = 8;
        public SwarmSpawnMode swarmSpawnMode = SwarmSpawnMode.Area;
        public SwarmShapeKind swarmShapeKind = SwarmShapeKind.Circle;
        // Custom shape (Path only): the hand-drawn polyline authored as a PAIR of envelopes over progress —
        // x(progress) and y(progress) in Curve mode, values in canvas-pixel offsets from the shape centre.
        // The polyline IS these two curves: point order is progress/time, so the T5 preview click-to-adds a
        // paired point onto both, and the renderer just evaluates both at progress p to get the path position.
        public ZUIValue swarmCustomX = DefaultCustomX();
        public ZUIValue swarmCustomY = DefaultCustomY();
        public ZUIValue swarmProgress = DefaultProgress();              // Path only: 0 = shape start, 1 = once around (wraps past 1 on closed shapes); sampled per-particle at its spawn frame
        // The swarm's spawn TIMING: maps a particle's number (0 = the first spawned, 1 = the last) to its spawn
        // moment on the blast TIMELINE (0 = frame 0, 1 = the last frame). This envelope is the WHOLE mapping now —
        // the old swarmSpawnWindow scale is gone (it was just a scale of this curve). End the curve low to finish
        // spawning early: the default (0,0)→(1,0.5) spreads the N spawns across the first half of the timeline.
        // Linear = evenly spread; an eased curve = burst-then-trickle (or the reverse); MinMax = every particle at
        // a random moment; a flat Static value s = all particles spawn together at moment s. The renderer's two-
        // point (0,0)→(1,K) fast path uses K·i/(n-1) verbatim (the old window·i/(n-1) with the endpoint K in
        // window's place), so the default swarm stays byte-identical.
        public ZUIValue swarmSpawnTiming = DefaultSpawnTiming();
        // Frame-step spawn timing (G3): an alternative to the Window/timing pair above, chosen by swarmTiming.
        // FrameStep places the first particle on frame swarmFirstFrame, then one more every swarmFrameStep frames
        // (0-based frame indexes; the transport readout shows them 1-based). step 0 = all particles on swarmFirstFrame.
        // Default Window ⇒ the Window arithmetic runs verbatim, so a default swarm is byte-identical.
        public SwarmTiming swarmTiming = SwarmTiming.Window;
        [Min(0)] public int swarmFirstFrame = 0;
        [Min(0)] public int swarmFrameStep = 2;
        [Range(0.05f, 1f)] public float swarmParticleLife = 0.5f;       // each particle's own life duration as a fraction of the blast timeline

        // ── Swarm placement/lifetime concepts (S1) — all default to an exact no-op so a default swarm is byte-identical ──
        // Per-particle FACING as each is placed (see the enum). None (default) = no turning; the renderer folds
        // the resulting orientDeg into each form's own rotation. Off/None ⇒ orient is irrelevant.
        public SwarmOrient swarmOrient = SwarmOrient.None;
        // Per-particle SIZE multiplier chosen by index: Eval'd with i/(n-1) as the curve input (like spawn timing).
        // Static 1 (default) = every particle full size (an exact no-op, IsStaticOne-gated). A Curve tapers the
        // swarm centre-vs-edge (authored freely); MinMax gives per-particle random size jitter.
        public ZUIValue swarmScaleByIndex = new ZUIValue(1f);
        // Path mode: spread the particles EVENLY along the outline by index instead of each sampling swarmProgress
        // independently. When on, particle i's progress = travelValue + (i/(n-1))·swarmPathSpread, where travelValue
        // = swarmProgress at its spawn life — so Spawn travel rides the whole evenly-spaced string along the path.
        // Off (default) = today's per-particle behaviour, byte-identical.
        public bool swarmEvenPath = false;
        [Range(0f, 1f)] public float swarmPathSpread = 1f;   // fraction of the outline the evenly-spaced string covers
        // When on, every particle DIES at the same shared timeline point Min(1, spawnWindow + particleLife) instead
        // of one particle-life after its own spawn — a burst that vanishes as one. Off (default) = byte-identical.
        public bool swarmDieTogether = false;

        // Shared shape transform — ALL per-spawn-snapshot animatables. T1 renderer uses only shapeScale; the rest land in T3.
        public ZUIValue shapeOffsetX = new ZUIValue(0f);   // shape-centre offset X, canvas pixels
        public ZUIValue shapeOffsetY = new ZUIValue(0f);   // shape-centre offset Y, canvas pixels
        public ZUIValue shapeScale = new ZUIValue(20f);    // shape radius in canvas pixels
        [Min(0f)] public float shapeScaleSnap = 0f;        // 0 = off; else the evaluated scale rounds to the nearest multiple (placements land on fixed radii)
        public ZUIValue shapeRotation = new ZUIValue(0f);  // 2D rotation, degrees
        public ZUIValue shapePitch = new ZUIValue(0f);     // pseudo-3D tilt, degrees (T3)
        public ZUIValue shapeYaw = new ZUIValue(0f);       // pseudo-3D tilt, degrees (T3)

        // ── Swarm live rotation (Issue 2) — a rigid whole-cloud spin, DISTINCT from the spawner rotation above.
        // shapeRotation/shapePitch/shapeYaw are per-spawn SNAPSHOTs (each particle reads them at ITS spawn moment,
        // so animating them SPREADS placements into a trail). These three instead are evaluated at the CURRENT
        // frame's life and applied UNIFORMLY to every already-placed particle, rotating the whole cloud around the
        // shape centre — so animating one spins the entire swarm as one solid group, arrangement preserved. All
        // default Static 0, an EXACT no-op (the renderer skips the rotation math entirely), so an existing swarm
        // renders byte-identical. Turn = yaw (vertical axis), Tilt = pitch (horizontal axis), Roll = Z (screen plane).
        public ZUIValue swarmTurn = new ZUIValue(0f);      // whole-cloud yaw, degrees, at the current frame's life
        public ZUIValue swarmTilt = new ZUIValue(0f);      // whole-cloud pitch, degrees, at the current frame's life
        public ZUIValue swarmRoll = new ZUIValue(0f);      // whole-cloud roll (screen plane), degrees, at the current frame's life

        // ── Swarm live SCALE (slice 0) — the exact sibling of swarmTurn/Tilt/Roll above, but a uniform RADIAL scale
        // of the whole placed cloud about its centre, evaluated at the CURRENT frame's life and applied uniformly to
        // every already-placed particle: final live pos = centre + scale·spin(offset). Static 1 (the default) is an
        // EXACT no-op — the renderer's scale==1 guard skips the multiply entirely, so an existing swarm renders
        // byte-identical. This is the LIVE expand/contract the per-spawn-snapshot shapeScale can't express (animating
        // shapeScale leaves a trail of placements; this resizes the placed cloud as one group). It also homes
        // MetaBlob's `metaExpand` when the Fuse field-pass lands (see PYREPLUS_ADVANCED_DESIGN.md).
        public ZUIValue swarmScale = new ZUIValue(1f);     // whole-cloud uniform radial scale about centre, at the current frame's life

        // ── Modifiers — reuses Pyre's own PyreModifier directly, zero reimplementation ──
        [SerializeReference] public List<PyreModifier> modifiers = new List<PyreModifier>();

        // ── deep copy (R3) — for the layer list's "Duplicate" ──────────────────────
        // JsonUtility is NOT used: it silently drops the [SerializeReference] modifier stack (Unity JsonUtility has
        // no SerializeReference support), so a JsonUtility round-trip would duplicate a layer with an EMPTY modifier
        // list. Instead we mirror vanilla Pyre's proven Layer.Clone: MemberwiseClone the value fields, then deep-copy
        // every reference field the copy must own independently (each ZUIValue via its CopyFrom, each ZuiFill /
        // Gradient by hand) and clone the polymorphic modifiers via each modifier's own Clone(). Sprite/font stay
        // shared refs (they are assets, not per-layer data).
        public PyrePlusLayer Clone()
        {
            var l = (PyrePlusLayer)MemberwiseClone();
            l.shapeFill = CloneFill(shapeFill);
            l.alpha = CloneVal(alpha);
            l.size = CloneVal(size);
            l.gemTilt = CloneVal(gemTilt);
            l.gemRoll = CloneVal(gemRoll);
            l.gemSpecularFill = CloneFill(gemSpecularFill);
            l.gemLineFill = CloneFill(gemLineFill);
            l.gemEdgeGlow = CloneVal(gemEdgeGlow);
            l.gemEdgeGlowFill = CloneFill(gemEdgeGlowFill);
            l.gemInnerGlow = CloneVal(gemInnerGlow);
            l.gemInnerGlowFill = CloneFill(gemInnerGlowFill);
            l.textFill = CloneFill(textFill);
            l.textBorder = CloneFill(textBorder);
            l.crescentBite = CloneVal(crescentBite);
            l.crescentAngle = CloneVal(crescentAngle);
            l.sparkleDensity = CloneVal(sparkleDensity);
            l.streakLength = CloneVal(streakLength);
            l.streakWidth = CloneVal(streakWidth);
            l.starLength = CloneVal(starLength);
            l.starBaseWidth = CloneVal(starBaseWidth);
            l.starSkew = CloneVal(starSkew);
            l.particlePathX = CloneVal(particlePathX);
            l.particlePathY = CloneVal(particlePathY);
            l.particleSpin = CloneVal(particleSpin);
            l.swarmCustomX = CloneVal(swarmCustomX);
            l.swarmCustomY = CloneVal(swarmCustomY);
            l.swarmProgress = CloneVal(swarmProgress);
            l.swarmSpawnTiming = CloneVal(swarmSpawnTiming);
            l.swarmScaleByIndex = CloneVal(swarmScaleByIndex);
            l.shapeOffsetX = CloneVal(shapeOffsetX);
            l.shapeOffsetY = CloneVal(shapeOffsetY);
            l.shapeScale = CloneVal(shapeScale);
            l.shapeRotation = CloneVal(shapeRotation);
            l.shapePitch = CloneVal(shapePitch);
            l.shapeYaw = CloneVal(shapeYaw);
            l.swarmTurn = CloneVal(swarmTurn);
            l.swarmTilt = CloneVal(swarmTilt);
            l.swarmRoll = CloneVal(swarmRoll);
            l.swarmScale = CloneVal(swarmScale);
            l.density = CloneVal(density);
            l.heat = CloneVal(heat);
            // Luma-matte (slice 4): matteFlags/matteScope/matteInvert are value types (MemberwiseClone copied them);
            // the four amount envelopes are ZUIValues, so deep-copy each so the copy owns its own curve data.
            l.matteStrength = CloneVal(matteStrength);
            l.matteBlurAmount = CloneVal(matteBlurAmount);
            l.matteDisplaceAmount = CloneVal(matteDisplaceAmount);
            l.matteHueDegrees = CloneVal(matteHueDegrees);
            // coalesce is a plain enum (value type) — MemberwiseClone above already copied it, like matteRole; the
            // three fuseThreshold/fuseShadeRange/fuseSoftness floats + the Ramp knobs (rampFusion/rampCoverage/
            // rampLighting/rampRelief/rampLightAngle/rampRimScale) are value types too, so MemberwiseClone deep-copies
            // them for free — only the density/heat ZUIValue envelopes above need an explicit deep copy. Likewise the
            // slice-4b matte-heightmap fields (matteWriteLuma bool, heightFromChannel int, heightRelief/heightLightAngle
            // floats) are all value types, so MemberwiseClone already copied them — no explicit deep-copy needed.
            l.modifiers = modifiers == null ? new List<PyreModifier>() : modifiers.ConvertAll(m => m?.Clone());
            return l;
        }

        static ZUIValue CloneVal(ZUIValue v)
        {
            if (v == null) return null;
            var c = new ZUIValue();
            c.CopyFrom(v);   // deep-copies mode + every mode's data, curve points included
            return c;
        }

        static ZuiFill CloneFill(ZuiFill f)
        {
            if (f == null) return null;
            return new ZuiFill
            {
                mode = f.mode,
                color = f.color,
                gradient = CloneGradient(f.gradient),
                angleDeg = f.angleDeg,
                zoom = f.zoom,
                center = f.center,
                space = f.space,
                // Texture group — Sprite stays a shared asset ref (like font/spriteImage), not per-layer data.
                texture = f.texture,
                textureSprite = f.textureSprite,
                noiseKind = f.noiseKind,
                gridAngle = f.gridAngle,
                gridSpacing = f.gridSpacing,
                gridLineWidth = f.gridLineWidth,
                gridVertical = f.gridVertical,
                gridHorizontal = f.gridHorizontal,
                dotSize = f.dotSize,
                dotSpacing = f.dotSpacing,
                dotStagger = f.dotStagger,
            };
        }

        static Gradient CloneGradient(Gradient g)
        {
            if (g == null) return null;
            var n = new Gradient();
            n.SetKeys(g.colorKeys, g.alphaKeys);
            n.mode = g.mode;
            return n;
        }

        // ── the animatable-default factories (moved here with the fields they seed) ─
        static Gradient DefaultColor()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),
                    new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.5f),
                    new GradientColorKey(new Color(0.5f, 0.1f, 0.05f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static ZuiFill DefaultShapeFill()
        {
            // The default shape fill: OverLife with the fire gradient above. OverLife evaluates as
            // gradient.Evaluate(life), so this is byte-identical to the old `colorOverLife.Evaluate(life)` path.
            return new ZuiFill { mode = ZuiFill.Mode.OverLife, gradient = DefaultColor() };
        }

        // FIX 1 support — is `f` the EXACT pristine default shape fill (the OverLife fire gradient a fresh layer
        // ships with, untouched)? The window uses this to give a fresh 3D solid (Gem/Box/Pyramid/Can/Orb/Ring) a
        // STEADY Solid material instead of this OverLife default, whose gold→dark-red life ramp darkens the whole
        // lit gem over its life and READS as the light pulsing (which the light dials can't stop — it's the material
        // colour). Detection is strict: mode must be OverLife, no texture may be set, and every gradient colour/alpha
        // key must match DefaultColor()'s. ANY user edit (a mode change, a moved/added/removed key, a texture) makes
        // it non-pristine, so a customised fill is NEVER converted — only the exact factory default is. Kept here,
        // beside the factory it compares against, so the two can never drift.
        public static bool IsPristineDefaultShapeFill(ZuiFill f)
        {
            if (f == null) return false;
            if (f.mode != ZuiFill.Mode.OverLife) return false;
            if (f.texture != ZuiFill.TextureKind.None) return false;   // a texture set ⇒ user-touched, leave it
            return GradientsMatch(f.gradient, DefaultColor());
        }

        static bool GradientsMatch(Gradient a, Gradient b)
        {
            if (a == null || b == null) return false;
            GradientColorKey[] ac = a.colorKeys, bc = b.colorKeys;
            GradientAlphaKey[] aa = a.alphaKeys, ba = b.alphaKeys;
            if (ac.Length != bc.Length || aa.Length != ba.Length) return false;
            for (int i = 0; i < ac.Length; i++)
                if (ac[i].color != bc[i].color || !Mathf.Approximately(ac[i].time, bc[i].time)) return false;
            for (int i = 0; i < aa.Length; i++)
                if (!Mathf.Approximately(aa[i].alpha, ba[i].alpha) || !Mathf.Approximately(aa[i].time, ba[i].time)) return false;
            return true;
        }

        static ZUIValue DefaultAlpha()
        {
            // fade in fast, hold, fade out — a particle's own life envelope.
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(0.15f, 1f));
            v.points.Add(new ZUIEnvelopePoint(0.7f, 1f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0f));
            return v;
        }

        static ZUIValue DefaultSize()
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 24f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 4f));
            v.points.Add(new ZUIEnvelopePoint(0.4f, 22f));
            v.points.Add(new ZUIEnvelopePoint(1f, 16f));
            return v;
        }

        static ZUIValue DefaultRampDensity()
        {
            // Body grows in fast then settles — mass over the particle's own life. Gives the cloud the volume that
            // catches the relief light (mirrors the intent of HeightBallGroup.mass, animated instead of static).
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0.15f));
            v.points.Add(new ZUIEnvelopePoint(0.35f, 0.9f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0.5f));
            return v;
        }

        static ZUIValue DefaultRampHeat()
        {
            // Born hot, cools to smoke — energy over the particle's own life. Young particles read as fire (top of
            // the ramp), old ones as cool smoke (bottom): the smoke→fire spread across the cloud that, fused, makes
            // it read as one boiling mass rather than uniform discs (mirrors HeightBallGroup.height's role).
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0.95f));
            v.points.Add(new ZUIEnvelopePoint(0.5f, 0.55f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0.12f));
            return v;
        }

        static ZUIValue DefaultStreakLength()
        {
            // Mirrors DefaultSize's shoot-out-then-shorten arc, in px over the particle's own life, yMax 28: grow
            // fast to a peak, then settle a little shorter — a comet tail that lengthens as it's born and eases back.
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 28f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 5f));
            v.points.Add(new ZUIEnvelopePoint(0.4f, 26f));
            v.points.Add(new ZUIEnvelopePoint(1f, 18f));
            return v;
        }

        static ZUIValue DefaultProgress()
        {
            // Path mode: a linear 0→1 ramp — 0 = shape start, 1 = once around. An authored envelope can
            // ease/hold/rewind so spawns cluster or spread, each sampling it at its own spawn frame.
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(1f, 1f));
            return v;
        }

        static ZUIValue DefaultSpawnTiming()
        {
            // (0,0)→(1,0.5): particle number maps to its spawn moment on the TIMELINE, finishing the swarm's
            // spawns halfway through — the old default's window 0.5 × linear timing folded into one envelope now
            // that swarmSpawnWindow is gone. The renderer's two-point (0,0)→(1,K) fast path reads K = 0.5 and
            // computes 0.5·i/(n-1) verbatim, byte-identical to the old window·i/(n-1).
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0.5f));
            return v;
        }

        static ZUIValue DefaultCustomX()
        {
            // A small visible starter path so switching to Custom shows something immediately: a shallow
            // zigzag sweeping left → centre → right in canvas-pixel offsets. yMin/yMax bound the editor's
            // draw range; the point VALUES are the actual pixel offsets the renderer reads.
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = -32f, yMax = 32f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, -16f));
            v.points.Add(new ZUIEnvelopePoint(0.5f, 0f));
            v.points.Add(new ZUIEnvelopePoint(1f, 16f));
            return v;
        }

        static ZUIValue DefaultCustomY()
        {
            // The Y half of the starter zigzag: dip down then back up (paired with DefaultCustomX by progress).
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = -32f, yMax = 32f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, -10f));
            v.points.Add(new ZUIEnvelopePoint(0.5f, 14f));
            v.points.Add(new ZUIEnvelopePoint(1f, -10f));
            return v;
        }

        static ZUIValue DefaultEdgeGlow()
        {
            // Steady halo out of the box (Static 0.5) — the light does NOT pulse by default. Pulsing is deliberate
            // authoring: switch this to a Curve to make the edge halo breathe over the particle's life.
            return new ZUIValue(0.5f);
        }

        static ZUIValue DefaultInnerGlow()
        {
            // Steady inner glow out of the box (Static 0.35) — no pulse by default. Author a Curve to make it pulse
            // over the particle's life.
            return new ZUIValue(0.35f);
        }

        static ZuiFill DefaultTextFill()
        {
            // The default letter fill: an OverLife ZuiFill carrying the fire ramp. The Text renderer reads a
            // non-Solid fill's GRADIENT (the mode itself is irrelevant to text — see SampleTextColor), so this is
            // byte-identical to the old `textFillGradient = DefaultFireRamp()` path.
            return new ZuiFill { mode = ZuiFill.Mode.OverLife, gradient = DefaultFireRamp() };
        }

        static ZuiFill DefaultTextBorder()
        {
            // The default border fill: an OverLife ZuiFill carrying the warm-white ramp — byte-identical to the
            // old `textBorderGradient = DefaultWarmWhite()` path (the renderer reads the gradient the same way).
            return new ZuiFill { mode = ZuiFill.Mode.OverLife, gradient = DefaultWarmWhite() };
        }

        static Gradient DefaultFireRamp()
        {
            // Deep crimson → orange → gold — the Pyre spatial fill ramp (matches the Text prototype's Grad()).
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.55f, 0.08f, 0.10f), 0f),
                    new GradientColorKey(new Color(0.95f, 0.45f, 0.10f), 0.5f),
                    new GradientColorKey(new Color(1f, 0.86f, 0.35f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static Gradient DefaultWarmWhite()
        {
            // A single warm-white colour = a solid border (sampled the same way as the fill, but flat since both
            // stops are the same colour).
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.97f, 0.88f), 0f),
                    new GradientColorKey(new Color(1f, 0.97f, 0.88f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }

    [CreateAssetMenu(menuName = "Laubrary/Pyre Plus", fileName = "PyrePlus")]
    public class PyrePlusSpec : ScriptableObject
    {
        // ── canvas / timing (mirrors Pyre's own top-level fields) ─────────────────
        [Min(1)] public int canvasSize = 64;
        [Min(1)] public int frameCount = 16;
        public int seed = 1234;
        // The flat clear colour. KEPT as the serialized render-clear field for compatibility: when backgroundUseFill
        // is false (the default) the renderer fills every pixel with this exactly as before (byte-identical).
        public Color background = new Color(0f, 0f, 0f, 0f);
        public float pixelsPerUnit = 16f;
        // Background fill (F2): when backgroundUseFill is on, the renderer evaluates backgroundFill per pixel across
        // the whole canvas (u,v in -1..1) as the backdrop instead of the flat `background` clear — a gradient,
        // noise, grid, dots or a stamped sprite behind the layers. Default OFF + a Solid-transparent fill, so a
        // default asset takes the old flat-clear path and stays byte-identical. The window flips backgroundUseFill
        // true the moment the user edits backgroundFill.
        public bool backgroundUseFill = false;
        public ZuiFill backgroundFill = new ZuiFill(new Color(0f, 0f, 0f, 0f));

        // ── layers (R3) — paint order, index 0 at the BACK ─────────────────────────
        // One default layer = exactly the pre-R3 single-layer spec, so a fresh asset renders byte-identical. Every
        // per-particle / form / swarm / modifier field lives on the layer now; only canvas/timing/preview are here.
        public List<PyrePlusLayer> layers = new List<PyrePlusLayer> { new PyrePlusLayer() };

        // ── editor preview state (cosmetic; never affects the render) ──────────────
        [HideInInspector] public float previewZoom = 4f;
        [HideInInspector] public float previewFps = 12f;
        // GIF export upscale (G3) — nearest-neighbour integer scale for PyrePlusGif.Export. Cosmetic authoring
        // state, never read by the renderer (like previewZoom); persisted so the last-used scale sticks per asset.
        [HideInInspector] public int previewGifScale = 4;
        [HideInInspector] public int previewFrame = 0;
        [HideInInspector] public bool previewShowFrame = true;   // draw a thin canvas border in the preview (Frame toggle)
        // Filmstrip / contact-sheet preview (Part A): show EVERY frame as a grid of tiles instead of one zoomed
        // frame. Purely cosmetic editor state — never read by the renderer, never baked.
        [HideInInspector] public bool previewStrip = false;
        [HideInInspector] public float previewStripSize = 96f;   // filmstrip tile size in px (32..256)
        // Swarm overlay dual visualisation (P4) — which of the two spawn-path overlays draw. Cosmetic authoring
        // state, never read by the renderer (like previewZoom). Show shape = the authored spawn shape + dots
        // (today's overlay); Show trace = the objective spawner-trace spine (PyrePlusRenderer.ComputeSpawnTrace).
        // Both may be on at once; neither on = no overlay at all.
        [HideInInspector] public bool previewShowShape = true;
        [HideInInspector] public bool previewShowTrace = false;

        public int Width => Mathf.Max(1, canvasSize);
        public int Height => Mathf.Max(1, canvasSize);
    }
}
