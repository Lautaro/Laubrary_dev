// Pyre — the asset behind the Pyre prototype (see PYREPLUS_DESIGN.md).
//
// Pyre is a PARALLEL rework of Pyre's shape system, kept entirely separate from the shipping
// Pyre/BlastSpec/Layer/BlastRenderer so the real tool is never at risk. R3 makes it MULTI-LAYER: the spec now
// holds a LIST of PyreLayer (index 0 = back, painted first), and every per-particle / form / swarm /
// modifier field lives on the layer — only the canvas, timing, seed, background and preview state stay on the
// spec. A single default layer renders byte-identical to the pre-R3 single-layer spec. Layers also carry Pyre's
// matte-channel idea, SIMPLIFIED: a layer is either Drawn or writes its coverage into one of four numbered
// mask channels, and a Draw layer can clip its own alpha by any channel an earlier layer wrote.
using System.Collections.Generic;
using Laubrary.SpriteFx;
using TMPro;
using UnityEngine;

namespace Laubrary.Pyre
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
    // orientDeg + PyreRenderer's per-form fold.
    // Radial = face directly away from the Pyre's MIDDLE (the canvas centre), re-computed EVERY FRAME from where the
    // particle is drawn — so a ring keeps pointing outward while the live Swarm spin turns it or Swarm scale grows
    // it (Outward is frozen at spawn and measured from the spawn shape's own centre). Particle Spin then turns each
    // one RELATIVE to its outward direction, so the whole ring stays symmetric (Spin 180 = all face inward).
    // APPEND ONLY — serialized as an int.
    public enum SwarmOrient { None, Outward, PathTangent, Radial }

    // How the swarm distributes its spawns IN TIME. Window (the default) = the swarmSpawnTiming envelope maps each
    // particle's number to its spawn moment on the blast timeline (it IS the whole mapping now). FrameStep =
    // spawn the first particle at frame `swarmFirstFrame`, then one more every `swarmFrameStep` frames until the
    // count is fulfilled (step 0 = all on that one frame). Default Window keeps a default swarm byte-identical.
    public enum SwarmTiming { Window, FrameStep }

    // The particle's rendered FORM. One implicit layer, so the whole swarm is one form. The STATELESS forms:
    //   Disc     — a flat soft disc (slice 1).
    //   Gem      — a true-3D lit faceted octahedral crystal (per-pixel point lighting, hard edge lines, two
    //              staggered glows; see PyreRenderer.DrawFacetSolid).
    //   Crescent — a disc with a second offset disc masked out (its own bite size / facing / push-out).
    //   Sparkle  — random lit pixel-cells scattered inside the disc, twinkling deterministically per frame.
    //   Sprite   — a Sprite's pixels stamped, scaled/rotated to the particle, optionally tinted by the gradient.
    //   Box      — a true-3D lit cuboid (8 verts / 6 quads), the Gem's facet pipeline. Aspect = height, Depth = z.
    //   Pyramid  — a true-3D lit square pyramid (apex + base), the Gem's facet pipeline. Aspect = apex height.
    //   Can      — a true-3D lit 16-sided cylinder (barrel + two caps), the Gem's facet pipeline. Aspect = height.
    //   Orb      — a true-3D lit SPHERE, analytic (no facets). Its silhouette is a plain circle that NEVER changes
    //              under spin/tilt — the LIGHTING FRAME rotates instead, so the shading + specular hotspot roll
    //              around the ball as it spins (see PyreRenderer.DrawOrb).
    //   Ring     — a flat two-sided tilted annulus (a Saturn ring), analytic. Outer radius = size, inner hole =
    //              size·ringInner; gemTilt opens/closes the ellipse (0° face-on, 90° edge-on) and particleSpin
    //              rolls it in-plane (see PyreRenderer.DrawRing).
    //   Text     — every character of a free string is one particle, drawn from a TMP SDF font atlas: a spatial
    //              gradient fill, an optional border, and optional 3D extrusion (front face + darker sides). Swarm
    //              OFF ⇒ the whole string lays out as one centred line; ON ⇒ each character rides a swarm position.
    //              gemTilt tips the letters and particleSpin yaws each one about its own centre (see
    //              PyreRenderer.DrawTextChar / RenderTextLine). Text overrides the swarm particle COUNT to the
    //              string length.
    //   Streak   — an ANCHOR-BIASED comet-tail capsule/rect: the particle sits at streakAnchor along the streak
    //              (0 = tail/grows forward, 0.5 = centred, 1 = tip/grows backward), oriented default up/+y (the
    //              swarm Orient or its own spin steer it). Its own streakLength/streakWidth envelopes drive it
    //              (NOT `size`); streakSoftTip feathers BOTH ends; edgeSoftness feathers the two long sides
    //              (see PyreRenderer.DrawStreakBody).
    //   Star     — a filled star POLYGON: N arms (starArms) with tips at radius R (= `size`·sizeMul) and inner
    //              (valley) vertices at radius R·(1−starLength); starBaseWidth sets each valley's angular position
    //              inside its sector (1 = the classical pentagram midpoint) and starSkew swirls the valleys into a
    //              pinwheel. A flat 2D form (shapeFill colours it, edgeSoftness feathers the rim). Star-shaped about
    //              its centre, so the inside test is a per-ray boundary (see PyreRenderer.DrawStarBody).
    // Box/Pyramid/Can share the Gem's shared 3D block (tilt, light, lines, glows) — see PyreRenderer.DrawFacetSolid.
    // Orb/Ring reuse that SAME per-pixel lighting/lines/glows math analytically (point light + Blinn-Phong, halo +
    // inner glow, edge lines) but with sphere/annulus geometry instead of facets.
    //   Fire     — a STATEFUL grid SIMULATION (slice 6a): retained frame-to-frame heat/fuel grids reached by a
    //              REPLAY harness, NOT a per-frame closed form. It REUSES Pyre's public FireSim/FireParams directly
    //              (Laubrary.Pyre) with BUILT-IN fixed emitters (arms around the canvas centre); its ramp is the
    //              shapeFill gradient and its overall opacity the shared `alpha` envelope. It ignores `size` and the
    //              swarm. See PyreRenderer.RenderFireLayer / _fireSims (the CWT + content-hash replay cache).
    //   Fireball — the SECOND stateful sim (slice 6b): the cheap "doom-fire" cellular flame. Heat blooms OUTWARD from
    //              one central point, folded into `fireballArms` kaleidoscope wedges → a radial/star explosion. Like
    //              Fire it retains a frame-to-frame heat grid reached by the SAME replay harness, NOT a closed form; it
    //              REUSES Pyre's public FireballSim/FireballParams directly. SINGLE-SOURCE (one central emitter) — it
    //              ignores `size` and the swarm. See PyreRenderer.RenderFireballLayer / _fireballSims.
    //   Polygon  — a flat filled regular convex N-gon (polygonSides edges): triangle / square / pentagon / hexagon /…,
    //              the 2D counterpart to the 3D Box/Pyramid. Every vertex sits at radius R (= `size`·sizeMul), so an R
    //              box bounds it exactly like the Star; the inside test is the SAME per-ray boundary as the Star but
    //              with ONE edge per angular sector (no valleys). A flat 2D form — shapeFill colours it, edgeSoftness
    //              feathers the rim, particleSpin turns it, and it swarms/travels/modifies like every 2D form. An even
    //              side count rests on a flat edge (a square sits flat, not as a diamond); an odd count points a vertex
    //              up (an upright triangle/pentagon). See PyreRenderer.DrawPolygonBody.
    // APPEND ONLY — the values are serialized as ints, so never reorder or insert. Fire (slice 6a) and Fireball (slice
    // 6b) are the only two simulation-backed forms here — both retain frame-to-frame grid state and share the replay
    // harness. HeightBalls is NOT a sim: it is CLOSED-FORM / stateless, proven at Runtime/Pyre/BlastRenderer.cs:1493-
    // 1496 — every ball's whole state at a frame is a pure function of (layer hash, group, ball index, layer life),
    // nothing accumulates between frames — so it arrives as a stateless Coalesce (Ramp) field-pass mode alongside
    // MetaBlob, never as a deferred sim form here.
    //   Playback3D — a PROOF-OF-CONCEPT form whose source is a real 3D animation (typically a ParticleSystem-based
    //              fire/explosion prefab) instead of a procedural closed-form/sim shape. UNLIKE every other form,
    //              it has NO runtime bake path yet (see PyreRenderer's Playback3D case) — its whole point is
    //              the EDITOR experience: PyreWindow.Playback3DPreview drives the assigned prefab's
    //              ParticleSystem(s) live in a PreviewRenderUtility scene (speed/scale/tint applied to the main
    //              module, scrubbed via Simulate) and can downsample that render to a small pixel grid so the
    //              user can gauge how a baked pixel-art version might read. It ignores `size` and the swarm.
    // APPEND ONLY — the values are serialized as ints, so never reorder or insert. Playback3D was appended last for
    // exactly that reason, even though it reads oddly out of the earlier alphabetical-ish grouping above.
    // Inferno and ForkBlast are RETIRED slots: both live on as PyreForm plug-ins (Runtime/Pyre/Forms/Kiln) held
    // in PyreLayer.form; the values stay so the ints of every other case keep meaning what they mean. A layer
    // still carrying one of them (and no form) draws as a Disc.
    public enum ShapeForm
    {
        Disc, Gem, Crescent, Sparkle, Sprite, Box, Pyramid, Can, Orb, Ring, Text, Streak, Star, Fire, Fireball, Polygon,
        [System.Obsolete("Migrated to PyreForm (Forms/Kiln/InfernoForm) — a retired enum slot.")] Inferno,
        [System.Obsolete("Migrated to PyreForm (Forms/Kiln/ForkBlastForm) — a retired enum slot.")] ForkBlast,
        Playback3D,
    }

    // How the Text form's spatial fill gradient is applied. PerCharGradient = every letter contains the WHOLE
    // gradient (across its own box, along the rotated fill axis). PerCharStep = every letter is ONE flat colour,
    // grad(i/(n-1)) by index. TextGradient = ONE gradient swept across the whole line's extent (swarm ON has no
    // line, so it degrades to PerCharStep). See PyreRenderer's SampleTextColor.
    public enum TextFillMode { PerCharGradient, PerCharStep, TextGradient }

    // A layer's ROLE in the stack. Draw = composite it onto the frame as normal. WriteMatte = do NOT composite
    // it; instead write its per-pixel COVERAGE (the alpha it would draw, before colour) into one of four numbered
    // mask channels, which the Draw layers above it can clip by. Simplified from vanilla Pyre's Draw/Matte — see
    // PyreRenderer's matte section for the full drift note.
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
    // (same names + bit values); a NEW Pyre-local enum (distinct from the existing `matteChannel` int, which is
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
    // relief (slice 2: RenderPlusRampField). Off stays byte-identical to pre-Coalesce. See PyreRenderer.Render-
    // Swarm's Coalesce seam. APPEND ONLY — serialized as an int, so never reorder or insert.
    public enum LayerCoalesce { Off, Fuse, Ramp }

    // ── one Pyre layer — everything per-particle / per-form / per-swarm / per-modifier, plus its matte role ──
    // The spec holds a LIST of these (index 0 at the BACK). A single default layer's fields carry the pre-R3
    // defaults verbatim, so a one-layer spec renders byte-identical to the old flat spec. The Default*() factories
    // that seed the animatable defaults live here with the fields they initialise.
    [System.Serializable]
    public class PyreLayer
    {
        // ── list identity (R3) ─────────────────────────────────────────────────────
        public bool enabled = true;      // hidden layers are skipped entirely by the renderer
        public string name = "Layer";    // shown in the layer list; rename-in-place

        // ── lifetime window (#55) — ported 1:1 from Pyre1's Layer.startFrame/endFrame ──────────────
        // The frame range this layer is ALIVE. Its life is lerped 0..1 across [startFrame, endFrame]
        // (clamped), EXACTLY like Pyre1 (BlastRenderer.cs:565); OUTSIDE that range the layer contributes
        // nothing that frame (an inactive layer). DEFAULT = the FULL range, so every existing spec is
        // byte-identical: startFrame 0 and endFrame -1, where -1 is the SENTINEL "the last frame",
        // resolved to frameCount-1 at render time. The sentinel (rather than a hardcoded 15) is what keeps
        // a spec byte-identical at ANY frameCount — a full-range window on a 24-frame spec must still end
        // at frame 23, giving life = frame/(frameCount-1) verbatim. An OLDER asset written before these
        // fields existed deserialises with them ABSENT ⇒ the initializers (0 / -1) apply ⇒ full range ⇒
        // byte-identical. Both are value-type ints, so Clone()'s MemberwiseClone copies them for free.
        [Tooltip("First frame this layer's shapes are alive. Its life is lerped 0..1 across [start, end]; before Start the layer contributes nothing.")]
        public int startFrame = 0;
        [Tooltip("Last frame this layer's shapes are alive (life reaches 1 here; after End the layer contributes nothing). -1 = the last frame — full range, the whole timeline, by default.")]
        public int endFrame = -1;

        // ── matte (R3) — Pyre's matte idea, simplified to numbered channels ─────────
        // The master gate for this layer's WHOLE matte block (role / clip / height). The RENDERER honours it (#57):
        // matteRole/clipByChannel/heightFromChannel only act when this is true, so a layer with matteEnabled == false
        // renders as a plain Draw layer and its matte sub-fields are PRESERVED (toggling the Matte box OFF no longer
        // wipes them — re-enabling restores the full setup). Also drives whether the Matte box is shown in the layer
        // list. DEFAULT TRUE and serialized: an OLDER asset that stored a matte role BEFORE this field existed
        // deserialises with the field ABSENT ⇒ the initializer's `true` keeps its matte enabled (byte-identical), and
        // a matte the new UI disables stores an explicit `false`. Fresh layers created in code set it false so a
        // brand-new layer still hides its (empty) matte box. Value type, so MemberwiseClone in Clone() copies it.
        [HideInInspector] public bool matteEnabled = true;
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
        //                        this layer's shapeFill gradient, relief-lit by PyreField.ReliefLight, Over-comp.
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
        // Shape's Fill as one smoke→fire cloud (see PyreRenderer.RenderPlusRampField). Two NEW per-particle
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

        // ── Mass shading (explosion study #2) — how a HEIGHT becomes colour, for both height tools (the Ramp
        // cloud and "Height from channel"). Off (default) = the plain lookup of height into the Fill, untouched.
        // On: height reads like MASS — below the soot line it is coloured from the soot ramp, above it the height is
        // rescaled across the Fill (the fire ramp), and Ignition decides how abruptly one turns into the other.
        // Bleed nudges each pixel's height by pixel-sized noise before the lookup, so bands tongue into each other
        // instead of meeting on a clean contour; Dither adds a 4×4 ordered pattern for a hand-pixelled edge.
        [Tooltip("Mass shading: read height like mass — soot below the soot line, fire above it, with ragged bleeding bands.")]
        public bool massShading = false;
        [Tooltip("Mass shading: the height below which colour comes from the Soot ramp (0..1).")]
        [Range(0f, 1f)] public float massSootLine = 0.3f;
        [Tooltip("Mass shading: how abruptly soot turns into fire at the soot line. 0 = a long smoky blend, 1 = an instant flip.")]
        [Range(0f, 1f)] public float massIgnition = 0.7f;
        [Tooltip("Mass shading: the soot ramp used below the soot line (bottom of the ramp = the lowest height).")]
        public ZuiFill massSootFill = DefaultSootFill();
        [Tooltip("Mass shading: how far pixel noise may push each pixel's height up or down before colouring (0 = clean bands).")]
        [Range(0f, 0.5f)] public float massBleed = 0.12f;
        [Tooltip("Mass shading: size of the bleed noise's specks, in canvas pixels.")]
        [Range(1f, 16f)] public float massBleedCellPx = 2f;
        [Tooltip("Mass shading: add a 4×4 ordered dither at band edges, like hand-placed pixels.")]
        public bool massDither = false;

        // ── Edge response (explosion study #3 + #5) — the layer's finished pixels react to how far they sit from its
        // own outline (a true distance to the nearest transparent pixel, whatever drew the silhouette). Off (default)
        // = untouched. Every effect is strongest at the rim and fades to nothing `edgeWidth` pixels inward.
        [Tooltip("Edge response: the layer's pixels change colour, glow and texture by how close they are to its own outline.")]
        public bool edgeResponse = false;
        [Tooltip("Edge response: how many pixels inward from the outline the effects reach.")]
        [Range(1f, 32f)] public float edgeRespWidth = 5f;
        [Tooltip("Edge response: shape of the fade. 1 = even; higher hugs the rim; lower spreads deep into the shape.")]
        [Range(0.2f, 4f)] public float edgeRespFalloff = 1f;
        [Tooltip("Edge response: brightness multiplier at the rim (1 = unchanged). Animatable over the layer's life.")]
        public ZUIValue edgeRespBrightness = new ZUIValue(1f);
        [Tooltip("Edge response: contrast at the rim (1 = unchanged).")]
        public ZUIValue edgeRespContrast = new ZUIValue(1f);
        [Tooltip("Edge response: hue shift at the rim, in degrees.")]
        public ZUIValue edgeRespHue = new ZUIValue(0f);
        [Tooltip("Edge response: saturation multiplier at the rim (1 = unchanged, 0 = grey).")]
        public ZUIValue edgeRespSaturation = new ZUIValue(1f);
        [Tooltip("Edge response: inner glow strength at the rim — adds light, it doesn't replace colour.")]
        public ZUIValue edgeRespGlow = new ZUIValue(0f);
        [Tooltip("Edge response: the inner glow's colour.")]
        public Color edgeRespGlowColor = new Color(1f, 0.85f, 0.45f, 1f);
        [Tooltip("Edge response: brightness grain from pixel noise whose specks get finer toward the rim (0 = none).")]
        [Range(0f, 1f)] public float edgeRespGrain = 0f;
        [Tooltip("Edge response: grain speck size at the rim, in canvas pixels.")]
        [Range(1f, 16f)] public float edgeRespGrainRimPx = 1.5f;
        [Tooltip("Edge response: grain speck size deep inside, in canvas pixels.")]
        [Range(1f, 32f)] public float edgeRespGrainCorePx = 6f;
        [Tooltip("Edge response: smear the layer's own pattern ALONG the outline near the rim, so its grain runs parallel to the edge. Pixels of reach (0 = off).")]
        [Range(0f, 12f)] public float edgeRespFlow = 0f;

        static ZuiFill DefaultSootFill()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.05f, 0.04f, 0.04f), 0f),
                              new GradientColorKey(new Color(0.22f, 0.2f, 0.19f), 0.6f),
                              new GradientColorKey(new Color(0.42f, 0.38f, 0.35f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return new ZuiFill { mode = ZuiFill.Mode.OverLife, gradient = g };
        }

        // ── Shape — the particle's own look (mandatory section) ────────────────────
        // Which FORM the particle renders as. Disc = the flat soft disc (slice 1). Gem = a true-3D lit crystal
        // (the Gem block below drives it); its material colour is shapeFill, its radius is `size`.
        public ShapeForm shapeForm = ShapeForm.Disc;
        // ── plug-in form (PyreForm.cs) ─────────────────────────────────────────────────────────────────────
        // When set, THIS object is the layer's shape and `shapeForm` is ignored: the renderer dispatches to it once
        // at the top of RenderLayer and the editor draws its fields by reflection. Null (the default, and every
        // pre-existing asset) ⇒ the built-in ShapeForm path, byte-identical. Polymorphic ([SerializeReference]) so
        // a form is just a subclass — in this assembly or in a family asmdef the core never references.
        [SerializeReference] public PyreForm form;
        // The particle's colour, as a ZuiFill: Solid (one flat colour), OverLife (a gradient over the particle's
        // life — the default), or a SPATIAL fill (Linear / Radial / Noise, sampled across the particle by its
        // normalized local point). Every mode is alpha-capable. Default = OverLife with the fire gradient, which
        // renders byte-identical to the old colorOverLife path. Text ignores this (it has its own richer per-char
        // fill system below).
        public ZuiFill shapeFill = DefaultShapeFill();
        public ZUIValue alpha = DefaultAlpha();     // over the particle's OWN life (multiplies the final output alpha for BOTH forms)
        public ZUIValue size = DefaultSize();       // radius in pixels, over the particle's own life (Gem: the girdle radius)
        [Range(0f, 1f)] public float edgeSoftness = 0.4f;   // soft rim vs hard pixel edge — FROZEN legacy source (task #12)
        // Edge softness, animatable over the particle's own life (Static / Min-Max / Curve). Migrated from the
        // `edgeSoftness` float (#12): NULL on an un-migrated asset ⇒ the renderer reads the legacy float (byte-
        // identical); the editor seeds it Static(edgeSoftness) the first time the Edge row is built, and Static
        // still evaluates to the legacy value, so the 0x2D95… render gate holds either way.
        [SerializeReference] public ZUIValue edgeSoftnessAnim;

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
        // texture is set). See PyreRenderer.SampleTextColor.
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
        public ZUIValue crescentAngle = new ZUIValue(0f);     // degrees — which way the bite faces, over own life (LEGACY — see the centre pad)
        [Range(0f, 1f)] public float crescentOffset = 0.5f;   // how far the bite disc is pushed out, fraction of radius (LEGACY — see the centre pad)
        // Mask-disc CENTRE as an (x,y) pair in radius units (-1..1), animatable over own life — the 2D pad that
        // replaces the polar Offset + Angle (#12 part 2). NULL on an un-migrated asset ⇒ the renderer uses the
        // legacy polar (crescentOffset, crescentAngle), so existing assets are byte-identical. The pad seeds its
        // DISPLAY from the polar values but only WRITES these on an explicit drag (a silent polar→cartesian seed is
        // NOT byte-identical — (off·radius)·cos vs (off·cos)·radius differ by float-multiply order).
        [SerializeReference] public ZUIValue crescentCenterXAnim;
        [SerializeReference] public ZUIValue crescentCenterYAnim;

        // ── Sparkle form (shapeForm == Sparkle) — random lit cells scattered inside the disc ───────────────
        // A virtual grid of `sparkleSize`-px cells overlays the disc; a cell lights this frame iff its stable
        // per-cell presence draw < density AND its per-frame twinkle draw passes (both from the Hash funnel, no
        // state — see PyreRenderer.DrawSparkleBody). Lit cells are hard full-colour pixels, no edge falloff.
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
        // shared `size` row stays visible (= the tip radius). See PyreRenderer.DrawStarBody.
        [Range(2, 20)] public int starArms = 5;                 // point count 2..20 (5 = the classical five-pointed star)
        public ZUIValue starLength = new ZUIValue(0.62f);       // arm reach 0..1 over own life; inner radius = R·(1−length). 0.62 ≈ the golden-ratio pentagram inner radius
        public ZUIValue starBaseWidth = new ZUIValue(1f);       // valley angular position as a fraction of the half-sector, 0.1..1 over own life (1 = classical midpoint; smaller = thinner arm bases, wider valleys)
        public ZUIValue starSkew = new ZUIValue(0f);            // valley swirl in degrees −60..60 over own life — rotates the valleys, pinwheel-twisting the arms (clamped so valleys never cross tips)

        // ── Polygon form (shapeForm == Polygon) — a flat filled regular convex N-gon ─────────────────────────────
        // The 2D counterpart to the 3D Box/Pyramid: a regular convex polygon with `polygonSides` equal-length edges,
        // circumradius R = evaluated `size` × sizeMul (every vertex sits at R, like the Star's tips). The renderer's
        // inside test is the SAME per-ray boundary as the Star (one tip→tip edge spanning each angular sector, no
        // valleys) — see PyreRenderer.DrawPolygonBody — and it reuses the flat-2D fill + edgeSoftness (rim) +
        // particleSpin machinery Disc/Star use. An even side count is rotated half a sector so it rests on a flat
        // edge (a square sits flat, not a diamond); an odd count points a vertex up (an upright triangle/pentagon).
        // Value-type int ⇒ Clone()'s MemberwiseClone copies it for free (no explicit deep-copy — see Clone).
        [Range(3, 12)] public int polygonSides = 4;             // side count 3..12 (3 = triangle, 4 = square, 6 = hexagon)

        // ── Border (task #60 / #65) — an optional coloured RIM on the flat 2D forms ─────────────────────────────
        // The flat 2D forms (Disc / Crescent / Ring / Streak / Star / Polygon) get a first-class border, the 2D
        // counterpart to the 3D solids' lit edge lines (and what a Disc used as a 2D ball wants for a rim). The
        // renderer draws it as a per-layer SILHOUETTE-outline pass over the layer's FINISHED alpha (one general
        // implementation for all six — no per-form boundary math): the outermost `borderWidth` px of the drawn
        // silhouette are recoloured to `borderFill`, its own alpha × the shape's coverage so the rim inherits the
        // shape's anti-aliased edge. NOT applied to the 3D solids (Gem/Box/Pyramid/Can/Orb — their own edge lines),
        // Text (its own textBorder), or Sprite/Fire/Fireball/Sparkle — the renderer gates on the flat-2D form set
        // (PyreRenderer.IsFlat2DBorderForm). ALL DEFAULT OFF / no-op ⇒ byte-identical: borderEnabled false ⇒
        // the renderer skips the whole pass, so an existing spec — and an OLDER asset predating these fields, which
        // deserialises with the initializers below — renders unchanged. borderWidth (ZUIValue) + borderFill (ZuiFill)
        // are deep-copied in Clone(); borderEnabled / borderOverMatte are value types (MemberwiseClone copies them).
        [Tooltip("Draw a coloured rim around this shape's silhouette (the flat 2D forms only). Off = no border (byte-identical to no border).")]
        public bool borderEnabled = false;
        [Tooltip("Rim thickness in pixels, over the layer's life — the outermost N px of the shape's silhouette are recoloured to the Border fill.")]
        public ZUIValue borderWidth = new ZUIValue(2f);
        [Tooltip("The border's colour/fill — Solid, a gradient, or a spatial fill (alpha-capable), like the shape's own Fill.")]
        public ZuiFill borderFill = DefaultBorderFill();
        // Draw-over-matte (task #65): when ON, the shape's FILL feeds this layer's role (Write-matte coverage / Luma
        // mask / a normal or clipped Draw) WITHOUT the border, and the BORDER is deferred and composited on TOP of the
        // finished frame instead — so a shape's fill can BE a matte while its border still draws on top, with no
        // separate outline-only layer needed (the clean fix for #65). OFF (default) = the border is part of the layer
        // like normal (folded into its coverage/mask, composited/clipped with the fill).
        [Tooltip("When this layer feeds a matte (Write/Luma) or is clipped: send only the FILL into the mask and draw the BORDER on top of the finished frame instead — so a shape's fill can BE the matte while its border still shows. Off = the border is part of the layer.")]
        public bool borderOverMatte = false;

        // ── Fire form (shapeForm == Fire) — a STATEFUL grid SIMULATION (slice 6a) ──────────────────────────────
        // Fire is the FIRST sim-backed Pyre form: it retains frame-to-frame heat/fuel grids and is reached by a
        // REPLAY harness (PyreRenderer._fireSims + RenderFireLayer), NOT by a per-frame closed form. It REUSES
        // Pyre's own PUBLIC FireSim/FireParams directly (Laubrary.Pyre) — zero re-port of the grid physics; Pyre
        // writes only the thin replay harness around them. These fields are the exact inputs Pyre's FireParamsAt
        // reads (Runtime/Pyre/BlastRenderer.cs:258); the ZUIValue rates are all envelopes over the LAYER's life (Fire
        // has NO particles — the whole layer IS the sim), the defaults copied verbatim from Pyre's Layer.cs so a
        // fresh Fire layer reads identically. Fire has BUILT-IN fixed emitters (arms around the canvas centre) — the
        // swarm does NOT apply (swarm-driven emitters are a later slice); it ignores `size` (the reach radius bounds
        // it, not a particle radius); its colour ramp is the shared shapeFill gradient and its overall opacity is the
        // shared `alpha` envelope over the layer's life. The ZUIValue fields below are deep-copied in Clone().
        public ZUIValue fireIntensity = DefaultFireIntensity();   // the burn's PROGRESS over life (0 = emitter off, 1 = full); scales the injected heat + fuel
        [Min(1)] public int fireArms = 1;                         // flame arms radiating from centre (1 = a single directional flame)
        public FireArmMode fireArmMode = FireArmMode.Mirror;      // Mirror = every arm emits identically (symmetric); Vary = each arm its own seed
        public ZUIValue fireDirection = new ZUIValue(90f);        // which way arm 0 points, degrees (90 = up)
        public ZUIValue fireEmitterWidth = new ZUIValue(9f);      // each arm emitter's width in px (the base of the flame)
        public ZUIValue fireEmitterInset = new ZUIValue(0f);      // how far each emitter sits from centre, px
        public ZUIValue fireHeat = new ZUIValue(0.95f);           // how hot the emitter injects
        public ZUIValue fireFuel = new ZUIValue(0.75f);           // unburnt fuel injected (fuel→heat gives the flame a body, not just a glow)
        public ZUIValue firePulse = new ZUIValue(0.18f);          // how much the emitter output breathes in and out
        public ZUIValue fireFlow = new ZUIValue(1f);              // steady outward push away from centre (a jet)
        public ZUIValue fireBuoyancy = new ZUIValue(4f);          // how strongly heat carries itself outward (a flame CLIMBS rather than just spreads)
        public ZUIValue fireCurl = new ZUIValue(1.5f);            // swirl strength — curls the tongues instead of merely stretching them
        public ZUIValue fireCurlScale = new ZUIValue(7f);         // swirl size (small = fine turbulence, large = slow broad rolls)
        public ZUIValue fireFlicker = new ZUIValue(0.6f);         // sideways wobble of the tongues (how they lick and wave)
        public ZUIValue fireStretch = new ZUIValue(3f);           // elongate the flame along its direction
        public ZUIValue firePinch = new ZUIValue(0.6f);           // taper the sides into a pointed tongue (most of what makes it read as a flame)
        public ZUIValue fireBreakup = new ZUIValue(0.4f);         // eat the edges into wisps instead of a smooth silhouette
        public ZUIValue fireDissipation = new ZUIValue(0.35f);    // how fast heat fades (high = a short sharp flame)
        public ZUIValue fireBurn = new ZUIValue(1.5f);            // how fast fuel converts into heat
        public ZUIValue fireReach = new ZUIValue(0.8f);           // reach as a fraction of the canvas half-size — confinement (the flame can NEVER touch the frame edge)
        public ZUIValue fireEdgeCooling = new ZUIValue(0.9f);     // how hard the flame is cooled once past the reach radius
        [Min(1)] public int fireSteps = 2;                        // simulation steps per frame (smoother/faster motion, same frame count)
        [Range(0f, 0.9f)] public float fireThreshold = 0.06f;     // heat below this reads as empty (raise for a crisper silhouette)
        public float fireContrast = 0.85f;                        // contrast on the gradient lookup (below 1 pushes more of the flame toward the hot end)
        // Swarm-driven emitters (slice 8) — OPT-IN, default OFF. When ON *and* the layer's Swarm is enabled, Fire
        // sources its emitters from the SWARM instead of the built-in arms around the centre: each alive swarm
        // particle becomes ONE heat/fuel injection into a shared fire field (position = the particle position;
        // radius/heat/fuel = this layer's Fire envelopes at that particle's OWN life; pulse phase = particle index).
        // The heat then advects/merges through the SAME centre-based fluid physics (buoyancy/curl/confinement) into
        // one connected flame — driven by a Pyre-LOCAL PyreFireSim (a faithful re-port of FireSim that takes
        // caller-supplied emitters), on its own replay harness. OFF (the default) OR swarm off ⇒ the slice-6a
        // built-in fixed-emitter FireSim path runs UNCHANGED (byte-identical). Value type ⇒ MemberwiseClone in
        // Clone() copies it for free. See PyreRenderer.RenderFireLayer / _plusFireSims.
        public bool fireSwarmEmitters = false;
        // Off-centre emitter (task #61) — a POSITION OFFSET in canvas pixels for the BUILT-IN (non-swarm) flame. Pyre's
        // FireSim always emits at the canvas centre + a radial inset (cx + dir·inset), so a single non-mirrored flame can
        // only sit on a ray FROM the centre; this shifts the whole flame off-centre. The renderer simulates the flame
        // EXACTLY as if centred (the byte-faithful FireSim path is untouched — buoyancy / arms / confinement stay
        // relative to the flame's own frame), then TRANSLATES the finished grid by (x, y) integer px when compositing —
        // so it reuses the proven sim path verbatim and only moves where the result LANDS. X = right, Y = up (matching
        // Direction 90 = up). DEFAULT (0,0) is a HARD no-op: the renderer takes the exact centred sim.Render path, so a
        // fixed-emitter Fire spec is byte-identical to before, and an older asset predating this field deserialises to
        // (0,0). Value type ⇒ Clone()'s MemberwiseClone copies it for free. Applies ONLY to the built-in fixed-emitter
        // path — the swarm-emitter path (fireSwarmEmitters) already places emitters at particle positions, and Fireball
        // is single-source by design; both are unaffected.
        [Tooltip("Shift the whole built-in flame off the canvas centre, in pixels (X right, Y up). The flame simulates as if centred, then moves — buoyancy, arms and confinement move with it. 0,0 = centred (the built-in behaviour).")]
        public Vector2 fireEmitterOffset = Vector2.zero;

        // ── Fireball form (shapeForm == Fireball) — a STATEFUL cellular SIMULATION (slice 6b) ──────────────────
        // Pyre's SECOND sim-backed form (after Fire): the cheap "doom-fire" cellular flame — heat propagates
        // OUTWARD from one central point, folded into `fireballArms` kaleidoscope wedges, so it reads as a radial/
        // star explosion cooling at the rim. Like Fire it retains a frame-to-frame heat grid reached by the SAME
        // replay harness (PyreRenderer._fireballSims + RenderFireballLayer), NOT a per-frame closed form. It
        // REUSES Pyre's own PUBLIC FireballSim/FireballParams directly (Laubrary.Pyre) — zero re-port of the cellular
        // physics. These fields are the exact inputs Pyre's StepFireball reads (Runtime/Pyre/BlastRenderer.cs:233);
        // the ZUIValue rates are envelopes over the LAYER's life. Fireball is SINGLE-SOURCE (one central emitter) — it
        // ignores `size` and the swarm. Defaults mirror Pyre's Layer.cs Fireball block exactly.
        public ZUIValue fireballSource = DefaultFireballSource();  // the burn's PROGRESS over life — how hot the centre injects
        public ZUIValue fireballSourceRadius = new ZUIValue(4f);  // radius of the hot core at the centre, px
        public ZUIValue fireballCooling = new ZUIValue(0.03f);    // how fast the flame cools travelling outward — arm LENGTH (low = long)
        public ZUIValue fireballSharpness = new ZUIValue(0.2f);   // how hard the arms taper — arm THINNESS, independent of length
        public ZUIValue fireballSpread = new ZUIValue(0.5f);      // sideways waver of the tongues — how much they lick
        public ZUIValue fireballReach = new ZUIValue(0.95f);      // reach as a fraction of the canvas half-size — confinement (never touches the edge)
        [Min(1)] public int fireballArms = 1;                     // radial wedges the flame is mirrored into (1 = a plain outward burst; more = kaleidoscope)
        public bool fireballMirror = true;                        // Mirror = alternate wedges reflected (a seam); off = each wedge the same, rotated
        [Range(0f, 0.9f)] public float fireballThreshold = 0.06f; // heat below this reads as empty (raise for a crisper silhouette)
        public float fireballContrast = 0.85f;                    // contrast on the gradient lookup (below 1 pushes more toward the hot end)

        // ── Playback3D form (shapeForm == Playback3D) — a PROOF-OF-CONCEPT 3D-animation source ──────────────────
        // Whole-layer, like Fire/Fireball: there is no swarm/particle placement, just one
        // assigned prefab whose ParticleSystem(s) the editor preview drives directly. NOT baked at runtime yet —
        // PyreRenderer's Playback3D case is a documented stub (a flat placeholder colour), so a spec using
        // this form does NOT yet render correctly outside the editor preview. See PyrePlayback3DPreview.cs
        // for the PreviewRenderUtility scene that actually plays/scrubs it and the pixelated-preview downsample.
        [Tooltip("The 3D content to preview/play — a prefab carrying one or more ParticleSystem components (e.g. a fire/explosion burst). Only its ParticleSystem(s) are driven; other components are inert here.")]
        public GameObject playbackPrefab;
        [Tooltip("Multiplies the prefab's ParticleSystem main-module simulation speed. 1 = authored speed.")]
        [Range(0.05f, 4f)] public float playbackSpeed = 1f;
        [Tooltip("Uniform scale applied to the instantiated prefab in the preview (also scales the particle systems' start size).")]
        [Range(0.05f, 5f)] public float playbackScale = 1f;
        [Tooltip("Tint multiplied into the FINAL graded image. White = the prefab's authored colours untouched. (Deliberately not written into the particle systems' start colour, which would flatten the pack's authored per-particle gradients.)")]
        public Color playbackTint = Color.white;
        [Tooltip("Preview framing. The camera auto-frames the dense body of the effect; this zooms in (>1) or pulls back (<1) from that automatic fit — e.g. pull back to bring a tall smoke column into frame.")]
        [Range(0.25f, 4f)] public float playbackZoom = 1f;
        [Tooltip("Strength of the preview's bloom/glow pass. VFX packs author their fire far brighter than white and ship a Bloom volume with their demo scene; this reproduces it, because without a glow the same particles read as flat clipped colour. 1 = the strength the Vefects pack's own demo scene uses. 0 = no glow.")]
        [Range(0f, 3f)] public float playbackGlow = 1f;
        [Tooltip("Where in the loop the preview scrubs to (0 = the moment it starts emitting, 1 = one full Loop duration later). Static preview position — dragging the transport elsewhere re-simulates from 0 up to this point.")]
        [Range(0f, 1f)] public float playbackScrub01 = 0f;
        [Tooltip("How long (seconds) one preview loop is — the scrub range Playback Scrub 0..1 maps across, and the length Play loops over.")]
        [Range(0.1f, 10f)] public float playbackLoopDuration = 2f;
        [Tooltip("Preview only: downsample the 3D render to a small pixel grid (point-filtered) so you can gauge how a baked pixel-art version would read. Does not affect the runtime (unbaked) form.")]
        public bool playbackPixelated = false;
        [Tooltip("Pixel grid resolution (long edge) the downsampled preview renders at when Pixelated is on. The short edge follows the preview's aspect, so the effect is never squashed.")]
        [Range(8, 128)] public int playbackPixelGrid = 32;
        [Tooltip("Colour levels per channel in the pixelated preview (posterisation). 0 = off (a straight shrink, which reads as a small photo rather than pixel art). Low values give authored-looking colour bands.")]
        [Range(0, 32)] public int playbackPixelLevels = 0;

        // ── opt-in Shape fields (T7) — the particle's OWN motion after birth, on its own life clock ────────
        // A per-particle travel path: canvas-pixel offsets ADDED to the particle's spawn position, evaluated on
        // its OWN life (0 = birth, 1 = death). Default Static 0 (a no-op — the renderer skips the Eval entirely
        // when both are Static 0, so a default asset renders byte-identical).
        public ZUIValue particlePathX = new ZUIValue(0f);
        public ZUIValue particlePathY = new ZUIValue(0f);
        // The particle's own rotation over its own life, degrees. DISC form: its pixels rotating IN PLACE (2D —
        // the pseudo-3D tilt belongs to the swarm shape transform shapePitch/shapeYaw, not here). GEM form: the
        // 3D solids' YAW about their vertical axis (see PyreRenderer.DrawFacetSolid). Default Static 0 (a no-op).
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
        // trail of placements instead of retroactively sliding already-placed particles (see PyreRenderer).
        public bool swarmEnabled = false;
        [Min(2)] public int swarmCount = 8;
        public SwarmSpawnMode swarmSpawnMode = SwarmSpawnMode.Area;
        public SwarmShapeKind swarmShapeKind = SwarmShapeKind.Circle;
        // Area-mode DISTRIBUTION (independent of spawn order below): 0 = a neat, evenly-spaced, almost-grid
        // placement (a Vogel/sunflower spiral — exact particle count, no clustering, reads as a rosette/grid
        // rather than scattered noise); 1 = today's uniform-random scatter (SampleDisc/SamplePolygonArea).
        // In between blends the two. Default 0 (grid) — a deliberate default CHANGE (2026-08-23): existing
        // Area-mode swarms will render differently the moment this field is added, on purpose (approved).
        // Path mode ignores this entirely (its placement is already ordered, by outline progress).
        [Range(0f, 1f)] public float swarmChaos = 0f;
        // Which end the grid END of swarmChaos above builds from. Circle/Custom (rings): false =
        // centre-out (innermost ring first), true = edge-in (outermost ring first). Square/Triangle/
        // Pentagon/Hexagon (raster grid): false/true pick opposite starting corners. Only visible in
        // its effect when swarmChaos < 1 (there's no "grid" to have a direction once fully random).
        public bool swarmGridReverse = false;
        // SPAWN order: which of the N positions established by swarmChaos above gets revealed at each spawn
        // moment. 0 = spawn slot i reveals position slot i — under a grid distribution, consecutive indices
        // are spatial neighbours, so this reads as "spawn from one corner, then its neighbour, and so on".
        // 1 = a full seeded shuffle — the position revealed at each spawn moment is decoupled from spatial
        // order. Values between smoothly scramble more of the mapping. Applies to Area AND Path modes alike
        // (Path: which point along the outline is revealed when). Default 0 (neighbour order).
        [Range(0f, 1f)] public float swarmSpawnChaos = 0f;
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

        // ── Simulation modifier (slice 7) — the layer's own STATEFUL "always last" modifier slot ──────────────────
        // A SimulationModifier (Pyre's own type, e.g. PixelFluidModifier) IS a PyreModifier, but unlike the stateless
        // Geometry/Pixel/Post modifiers in `modifiers` above it RETAINS frame-to-frame state and REPLAYS internally on
        // a scrub, so it lives in its OWN dedicated slot (mirroring vanilla Pyre's separate Layer.simulationModifier
        // field — NOT part of the modifier list) and always runs LAST within the layer: the sim slot, after the layer's
        // stateless post modifiers and before the matte apply. PyreRenderer drives its INTERNAL EnsureFrame/SetSeed
        // by reflection (a separate assembly, no InternalsVisibleTo). Default null ⇒ inert ⇒ every existing spec renders
        // byte-identical. See PyreRenderer.ApplyLayerSim.
        [SerializeReference] public SimulationModifier simulationModifier;

        // ── deep copy (R3) — for the layer list's "Duplicate" ──────────────────────
        // JsonUtility is NOT used: it silently drops the [SerializeReference] modifier stack (Unity JsonUtility has
        // no SerializeReference support), so a JsonUtility round-trip would duplicate a layer with an EMPTY modifier
        // list. Instead we mirror vanilla Pyre's proven Layer.Clone: MemberwiseClone the value fields, then deep-copy
        // every reference field the copy must own independently (each ZUIValue via its CopyFrom, each ZuiFill /
        // Gradient by hand) and clone the polymorphic modifiers via each modifier's own Clone(). Sprite/font stay
        // shared refs (they are assets, not per-layer data).
        public PyreLayer Clone()
        {
            var l = (PyreLayer)MemberwiseClone();
            l.shapeFill = CloneFill(shapeFill);
            l.massSootFill = CloneFill(massSootFill);
            l.edgeRespBrightness = CloneVal(edgeRespBrightness);
            l.edgeRespContrast = CloneVal(edgeRespContrast);
            l.edgeRespHue = CloneVal(edgeRespHue);
            l.edgeRespSaturation = CloneVal(edgeRespSaturation);
            l.edgeRespGlow = CloneVal(edgeRespGlow);
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
            l.edgeSoftnessAnim = CloneVal(edgeSoftnessAnim);
            l.crescentBite = CloneVal(crescentBite);
            l.crescentAngle = CloneVal(crescentAngle);
            l.crescentCenterXAnim = CloneVal(crescentCenterXAnim);
            l.crescentCenterYAnim = CloneVal(crescentCenterYAnim);
            l.sparkleDensity = CloneVal(sparkleDensity);
            l.streakLength = CloneVal(streakLength);
            l.streakWidth = CloneVal(streakWidth);
            l.starLength = CloneVal(starLength);
            l.starBaseWidth = CloneVal(starBaseWidth);
            l.starSkew = CloneVal(starSkew);
            // Border (task #60/#65): the width envelope + the fill are deep-copied so the copy owns its own data;
            // borderEnabled / borderOverMatte are value-type bools already copied by MemberwiseClone above.
            l.borderWidth = CloneVal(borderWidth);
            l.borderFill = CloneFill(borderFill);
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
            // Fire (slice 6a): fireIntensity + the 18 rate ZUIValues are deep-copied so the copy owns its own curve
            // data; fireArms/fireArmMode/fireSteps/fireThreshold/fireContrast are value types (MemberwiseClone copied).
            l.fireIntensity = CloneVal(fireIntensity);
            l.fireDirection = CloneVal(fireDirection);
            l.fireEmitterWidth = CloneVal(fireEmitterWidth);
            l.fireEmitterInset = CloneVal(fireEmitterInset);
            l.fireHeat = CloneVal(fireHeat);
            l.fireFuel = CloneVal(fireFuel);
            l.firePulse = CloneVal(firePulse);
            l.fireFlow = CloneVal(fireFlow);
            l.fireBuoyancy = CloneVal(fireBuoyancy);
            l.fireCurl = CloneVal(fireCurl);
            l.fireCurlScale = CloneVal(fireCurlScale);
            l.fireFlicker = CloneVal(fireFlicker);
            l.fireStretch = CloneVal(fireStretch);
            l.firePinch = CloneVal(firePinch);
            l.fireBreakup = CloneVal(fireBreakup);
            l.fireDissipation = CloneVal(fireDissipation);
            l.fireBurn = CloneVal(fireBurn);
            l.fireReach = CloneVal(fireReach);
            l.fireEdgeCooling = CloneVal(fireEdgeCooling);
            // Fireball (slice 6b): the six rate ZUIValues are deep-copied so the copy owns its own curve data;
            // fireballArms/fireballMirror/fireballThreshold/fireballContrast are value types (MemberwiseClone copied).
            l.fireballSource = CloneVal(fireballSource);
            l.fireballSourceRadius = CloneVal(fireballSourceRadius);
            l.fireballCooling = CloneVal(fireballCooling);
            l.fireballSharpness = CloneVal(fireballSharpness);
            l.fireballSpread = CloneVal(fireballSpread);
            l.fireballReach = CloneVal(fireballReach);
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
            // Plug-in form: a [SerializeReference] object MemberwiseClone would ALIAS between the two layers (and
            // Unity would then serialize it once, shared) — deep-copy through the form's own reflection Clone.
            l.form = form?.Clone();
            // Simulation modifier (slice 7): a stateful [SerializeReference] slot — MemberwiseClone shared the ref, so
            // deep-copy via its own Clone() (which resets the copy's live sim grids/PRNG; see PixelFluidModifier.Clone)
            // so a duplicated layer owns its own sim instance and never corrupts the original's running state. Null stays
            // null (the default), keeping a plain layer's clone byte-identical.
            l.simulationModifier = simulationModifier != null ? (SimulationModifier)simulationModifier.Clone() : null;
            return l;
        }

        static ZUIValue CloneVal(ZUIValue v)
        {
            if (v == null) return null;
            var c = new ZUIValue();
            c.CopyFrom(v);   // deep-copies mode + every mode's data, curve points included
            return c;
        }

        public static ZuiFill CloneFill(ZuiFill f)
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
                // Animatable spatial companions (task #64) — deep-copy so a duplicated layer keeps its own authored
                // zoom/centre curve. Null stays null; the clone's EnsureSpatialAnim re-seeds from the copied legacy
                // scalars, so a fill that never touched the companions clones byte-identically either way.
                zoomAnim = CloneVal(f.zoomAnim),
                centerXAnim = CloneVal(f.centerXAnim),
                centerYAnim = CloneVal(f.centerYAnim),
                space = f.space,
                // Texture group — Sprite stays a shared asset ref (like font/spriteImage), not per-layer data.
                texture = f.texture,
                textureSprite = f.textureSprite,
                noiseKind = f.noiseKind,
                noisePixelScale = f.noisePixelScale,
                noiseCellPx = f.noiseCellPx,
                noiseDetail = f.noiseDetail,
                noiseSeed = f.noiseSeed,
                noiseSteps = f.noiseSteps,
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

        static ZUIValue DefaultFireIntensity()
        {
            // Fire's burn PROGRESS envelope over the layer's life: a quick ignite, a hold, then a fade to nothing —
            // the exact shape of Pyre's fireIntensity default (Layer.cs: CurveVal(1f, 0f,0f, 0.18f,1f, 0.7f,1f, 1f,0f)).
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(0.18f, 1f));
            v.points.Add(new ZUIEnvelopePoint(0.7f, 1f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0f));
            return v;
        }

        static ZUIValue DefaultFireballSource()
        {
            // Fireball's burn PROGRESS envelope over the layer's life — the exact shape of Pyre's fireballSource
            // default (Layer.cs: CurveVal(1f, 0f,0f, 0.12f,1f, 0.6f,1f, 1f,0f)): a quick ignite, a hold, a fade to nothing.
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(0.12f, 1f));
            v.points.Add(new ZUIEnvelopePoint(0.6f, 1f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0f));
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

        static ZuiFill DefaultBorderFill()
        {
            // The default border (task #60): a solid near-white rim — a clean, visible outline the moment Border is
            // enabled. The user switches it to any colour / gradient / spatial fill via the fill's ⋯ menu. Never read
            // unless borderEnabled is on, so it has no effect on a default (border-off) render.
            return new ZuiFill(new Color(1f, 1f, 1f, 1f));
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

    [CreateAssetMenu(menuName = "Laubrary/Pyre", fileName = "Pyre")]
    public partial class Pyre : ScriptableObject
    {
        // ── canvas / timing (mirrors Pyre's own top-level fields) ─────────────────
        [Min(1)] public int canvasSize = 64;
        [Min(1)] public int frameCount = 16;
        public int seed = 1234;
        // Per-asset UI persistence, ported from Pyre (Pyre.cs's previewLayerSel/previewScroll): WHICH layer's
        // inspector is open travels with the asset instead of resetting to layer 0 every time the window is
        // reopened or another asset is visited. [HideInInspector] + cosmetic — never read by the renderer.
        [HideInInspector] public int previewLayerSel = 0;
        // The flat clear colour. KEPT as the serialized render-clear field for compatibility: when backgroundUseFill
        // is false (the default) the renderer fills every pixel with this exactly as before (byte-identical).
        public Color background = new Color(0f, 0f, 0f, 0f);
        // T-0383: this number is not a preview setting — it is stamped straight onto every real Sprite this
        // Pyre ever produces (PyreRenderer.GetFrames' Sprite.Create, PyreBaker's spritePixelsPerUnit), so two
        // Pyres carrying different values render their pixels at genuinely different apparent sizes side by
        // side. Nothing ever synced it to the project's own Pixel Scale setting, so a Pyre authored before a
        // project-wide PPU change (or copied in from another project) kept its old value forever. Auto ON is
        // the default for assets old and new alike — the same "nobody chose the drift on purpose" reasoning
        // T-0356 used for PyreBlast.blastSecondsAuto — and makes this track
        // PixelScaleProjectSettings.pixelsPerUnit. Read EffectivePixelsPerUnit, never this field, anywhere the
        // answer actually renders something; the stored field is what Auto OFF hands back to the author.
        [Tooltip("Pixels per unit stamped onto every sprite this Pyre bakes. Used only while Auto is off.")]
        public float pixelsPerUnit = 16f;

        [Tooltip("On: pixels per unit follows the project's Pixel Scale Project Settings asset, so this Pyre's " +
                 "pixels come out the same apparent size as every other piece of art in the game. Off: the " +
                 "value beside it is used instead — a deliberate per-asset override, e.g. an intentionally " +
                 "hi-res glow layer under low-res sprites.")]
        public bool pixelsPerUnitAuto = true;

        /// The pixels-per-unit this Pyre actually bakes with: the project's Pixel Scale setting while Auto is
        /// on, the per-asset override once it is off — same override-toggle shape as DebrisScatter's
        /// useProjectPixelScale/pixelsPerUnitOverride pair and PixelScaleCamera's own. Everything that stamps
        /// or measures a real sprite PPU goes through this.
        public float EffectivePixelsPerUnit
            => Mathf.Max(0.01f, pixelsPerUnitAuto
                ? Laubrary.PixelScale.PixelScaleProjectSettings.Instance.pixelsPerUnit
                : pixelsPerUnit);

        // ── anchor (optional) — see PyreAnchor. Off by default so every existing asset keeps its implicit origin
        // (the canvas centre / sprite pivot). When set, the anchor IS the origin: a placing system puts it on the
        // spawn point, and a Vector anchor also says which way the Pyre faces. Never read by the renderer.
        [Tooltip("Mark where this Pyre's base / start / centre is, so a system that places it (a Zoe event, a " +
                 "muzzle flash) can put THAT point on the spawn point instead of the canvas centre. The anchor " +
                 "does nothing by itself — it only tells placing systems how to treat this Pyre.")]
        public bool anchorEnabled = false;
        [Tooltip("Position = one point (the base / start / centre). Vector = that point PLUS a facing, so an " +
                 "event that rotates the Pyre toward a direction knows which way its art already points.")]
        public PyreAnchorKind anchorKind = PyreAnchorKind.Position;
        [Tooltip("Anchor point, normalized 0..1 across the canvas, bottom-left origin.")]
        public Vector2 anchorOrigin = new Vector2(0.5f, 0.5f);
        [Tooltip("Vector anchor only: the direction the Pyre's art faces, in its own space ((1,0) = +X).")]
        public Vector2 anchorDirection = Vector2.right;
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
        public List<PyreLayer> layers = new List<PyreLayer> { new PyreLayer { matteEnabled = false } };

        // ── spec-wide GLOBAL modifiers (task #56) — ported 1:1 from Pyre1's Pyre.globalModifiers ─────────────────
        // Geometry warps + pixel effects + post passes applied to EVERY layer, exactly as Pyre1 does (Runtime/Pyre/
        // Pyre.cs:118-120 + BlastRenderer.BuildStack / the trailing global post loop). Each layer's EFFECTIVE stack =
        // its own modifiers with these WRAPPED around them in Pyre1's order: a global GEOMETRY warp is the OUTERMOST
        // transform (a global Rotate spins the whole animation as one), a global PIXEL effect runs AFTER each layer's
        // own pixel modifiers, and a global POST pass runs over the whole finished frame after everything composites.
        // DEFAULT EMPTY ⇒ every layer's effective stack == layer.modifiers verbatim ⇒ byte-identical to pre-#56, and
        // an OLDER asset predating this field deserialises with it ABSENT ⇒ the empty-list initializer applies (same
        // byte-identical outcome). SerializeReference, matching the per-layer modifier list.
        [SerializeReference] public List<PyreModifier> globalModifiers = new List<PyreModifier>();

        // ── editor preview state (cosmetic; never affects the render) ──────────────
        [HideInInspector] public float previewZoom = 4f;
        [HideInInspector] public float previewFps = 12f;
        // GIF export upscale (G3) — nearest-neighbour integer scale for PyreGif.Export. Cosmetic authoring
        // state, never read by the renderer (like previewZoom); persisted so the last-used scale sticks per asset.
        [HideInInspector] public int previewGifScale = 4;
        // GIF alpha dithering — GIF transparency is ONE BIT, so a soft edge has to be either kept or dropped.
        // ON (the default) stipples the partly-transparent band instead of slicing it at 50%, which is what makes
        // a feathered explosion still read as fading in an exported GIF. Cosmetic authoring state like the scale.
        [HideInInspector] public bool previewGifDither = true;
        [HideInInspector] public int previewFrame = 0;
        [HideInInspector] public bool previewShowFrame = true;   // draw a thin canvas border in the preview (Frame toggle)
        // Filmstrip / contact-sheet preview (Part A): show EVERY frame as a grid of tiles instead of one zoomed
        // frame. Purely cosmetic editor state — never read by the renderer, never baked.
        [HideInInspector] public bool previewStrip = false;
        [HideInInspector] public float previewStripSize = 96f;   // filmstrip tile size in px (32..256)
        // Swarm overlay dual visualisation (P4) — which of the two spawn-path overlays draw. Cosmetic authoring
        // state, never read by the renderer (like previewZoom). Show shape = the authored spawn shape + dots
        // (today's overlay); Show trace = the objective spawner-trace spine (PyreRenderer.ComputeSpawnTrace).
        // Both may be on at once; neither on = no overlay at all.
        [HideInInspector] public bool previewShowShape = true;
        [HideInInspector] public bool previewShowTrace = false;

        // ── CherryFraming — cherry-pick frames from this spec's own baked frames into a sub-sequence ──────────
        // See Editor/Pyre/PyreWindow.CherryFraming.cs. Everything here defaults to a no-op / empty so an
        // asset saved before CherryFraming existed deserialises byte-identical (rendering is untouched — this is
        // PREVIEW-ONLY authoring state plus the CherryFrame list itself, never read by PyreRenderer).
        [HideInInspector] public bool cherryEnabled = false;
        public List<CherryFrame> cherryFrames = new List<CherryFrame>();
        [HideInInspector] public float previewCherryStripSize = 96f;   // cherry-grid tile size in px (32..256), independent of the filmstrip's previewStripSize
        [HideInInspector] public float previewDelay = 0f;              // seconds of blank preview between cherry-loop iterations (0 = no gap)
        [HideInInspector] public int previewZoundFrame = -1;           // cherry slot index a preview-only Zound fires on each loop; -1 = never
        [HideInInspector] public string previewZoundName;              // name of the Zound to fire at previewZoundFrame (preview-only, never baked)
        // The preview backdrop is an editor-only BackSplash (camera colour + one image), persisted PER ASSET so it
        // survives closing/reopening the window (mirrors Pyre1's own Pyre.previewBackSplash). Lazily allocated —
        // an asset saved before this field existed has no `previewBackSplash:` block in its YAML (the field is
        // [HideInInspector], so that's invisible in the Inspector too) until the user first touches the backdrop.
        [HideInInspector] public Laubrary.BackSplash.BackSplashSettings previewBackSplash;

        public int Width => Mathf.Max(1, canvasSize);
        public int Height => Mathf.Max(1, canvasSize);
    }
}
