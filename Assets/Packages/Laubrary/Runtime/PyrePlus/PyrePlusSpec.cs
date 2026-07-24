// PyrePlusSpec — the asset behind the PyrePlus prototype (see PYREPLUS_DESIGN.md).
//
// PyrePlus is a PARALLEL rework of Pyre's shape system, kept entirely separate from the shipping
// Pyre/BlastSpec/Layer/BlastRenderer so the real tool is never at risk. One implicit layer organized into
// three conceptual sections — Shape, Swarm, Modifiers — instead of Pyre's one flat ~40-field Layer. The Shape
// section (one particle) and the full Swarm data model are in place; the Swarm renderer is being built out
// task by task (T1 = Area+Circle placement) and Modifiers reuse Pyre's PyreModifier directly.
using System.Collections.Generic;
using Laubrary.Pyre;
using TMPro;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    // How the swarm places its particles: uniformly inside the shape's area, or travelling along its outline.
    public enum SwarmSpawnMode { Area, Path }

    // The swarm shape: a regular polygon by side count (Circle = ∞ sides), or a hand-authored polyline.
    // Custom is Path-only (enforced in the UI later); the renderer treats Area+Custom as a Circle fallback.
    public enum SwarmShapeKind { Circle, Triangle, Square, Pentagon, Hexagon, Custom }

    // How the swarm ORIENTS each particle as it's placed — the per-particle facing the renderer folds into the
    // form's own rotation (Streak forward, Disc/Sprite spin, Text/solid roll). None = no turning (every particle
    // keeps its own orientation). Outward = face away from the shape centre. PathTangent = face along the outline
    // it rides (readable text along a path); in Area mode PathTangent falls back to Outward. See ComputeSpawns'
    // orientDeg + PyrePlusRenderer's per-form fold.
    public enum SwarmOrient { None, Outward, PathTangent }

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
    //   Streak   — a ROOT-ANCHORED comet-tail capsule/rect: the particle position is the streak's root, and it
    //              grows FORWARD along its orientation (default up/+y; the swarm Orient or its own spin steer it).
    //              Its own streakLength/streakWidth envelopes drive it (NOT `size`); streakBackFrac spills it
    //              behind the root; streakSoftTip feathers the forward tip; edgeSoftness feathers the two long
    //              sides (see PyrePlusRenderer.DrawStreakBody).
    // Box/Pyramid/Can share the Gem's shared 3D block (tilt, light, lines, glows) — see PyrePlusRenderer.DrawFacetSolid.
    // Orb/Ring reuse that SAME per-pixel lighting/lines/glows math analytically (point light + Blinn-Phong, halo +
    // inner glow, edge lines) but with sphere/annulus geometry instead of facets.
    // APPEND ONLY — the values are serialized as ints, so never reorder or insert. (Fire/Fireball/HeightBalls are
    // simulation-backed and deferred; they are NOT here.)
    public enum ShapeForm { Disc, Gem, Crescent, Sparkle, Sprite, Box, Pyramid, Can, Orb, Ring, Text, Streak }

    // How the Text form's spatial fill gradient is applied. PerCharGradient = every letter contains the WHOLE
    // gradient (across its own box, along the rotated fill axis). PerCharStep = every letter is ONE flat colour,
    // grad(i/(n-1)) by index. TextGradient = ONE gradient swept across the whole line's extent (swarm ON has no
    // line, so it degrades to PerCharStep). See PyrePlusRenderer's SampleTextColor.
    public enum TextFillMode { PerCharGradient, PerCharStep, TextGradient }

    [CreateAssetMenu(menuName = "Laubrary/Pyre Plus", fileName = "PyrePlus")]
    public class PyrePlusSpec : ScriptableObject
    {
        // ── canvas / timing (mirrors Pyre's own top-level fields) ─────────────────
        [Min(1)] public int canvasSize = 64;
        [Min(1)] public int frameCount = 16;
        public int seed = 1234;
        public Color background = new Color(0f, 0f, 0f, 0f);
        public float pixelsPerUnit = 16f;

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
        // Lighting. The light POSITION derives in the renderer from these two angles at distance 3.5·R with a
        // 4.7·R falloff range (the prototype's proportions) — distance is not exposed.
        public float gemLightYaw = -55f;                    // key-light azimuth (left/right), degrees
        public float gemLightPitch = 38f;                   // key-light elevation (above the horizon), degrees
        [Range(0f, 1f)] public float gemAmbient = 0.05f;    // near-zero fill on unlit faces
        [Range(0f, 2f)] public float gemSpecular = 0.9f;    // Blinn-Phong highlight strength
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
        // Which fill mode paints the letters. (Text ignores shapeFill entirely — see the enum.)
        public TextFillMode textFillMode = TextFillMode.PerCharGradient;
        // The SPATIAL fill ramp. NOT over the particle's life — sampled in SPACE across the char/line (unlike every
        // other form, Text does not read shapeFill). Deep-crimson→orange→gold by default (the Pyre ramp).
        public Gradient textFillGradient = DefaultFireRamp();
        // Rotates the fill axis. Convention: 0 = vertical bottom→top for PerCharGradient (PerCharStep is index-based
        // and ignores it); for TextGradient 0 = left→right across the whole line.
        [Range(-180f, 180f)] public float textGradientAngle = 0f;
        // Letter outline, in SCREEN pixels (0 = no border). Drawn as an SDF band just inside each glyph edge,
        // coloured from textBorderGradient sampled with the SAME fill mode + angle as the fill.
        [Range(0f, 4f)] public float textBorderWidth = 1f;
        // The border colour ramp — a single-colour gradient reads as a solid outline.
        public Gradient textBorderGradient = DefaultWarmWhite();
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

        // ── Streak form (shapeForm == Streak) — a root-anchored comet-tail capsule ─────────────────────────
        // The particle's position is the streak's ROOT; it grows FORWARD along its orientation (default up/+y,
        // steered by the swarm Orient and its own spin). Its length/width are their OWN envelopes over the
        // particle's own life (NOT the shared `size`, which is hidden in the UI for this form). Its colour is the
        // shared shapeFill; a SPATIAL fill sweeps across the streak — see the (u,v) mapping in DrawStreakBody.
        public ZUIValue streakLength = DefaultStreakLength();   // length forward, in px over own life (a shoot-out arc)
        public ZUIValue streakWidth = new ZUIValue(3f);         // thickness across, in px over own life
        [Range(0f, 1f)] public float streakBackFrac = 0.15f;    // how far the streak spills BEHIND the root, as a fraction of length
        [Range(0f, 1f)] public float streakSoftTip = 0.5f;      // alpha feather over the last softTip·length of the FORWARD tip (sides reuse edgeSoftness)

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
        [Range(0f, 1f)] public float swarmSpawnWindow = 0.5f;           // fraction of the blast timeline the N spawns spread across (0 = all at frame 0)
        // WHEN each particle spawns INSIDE the window: maps a particle's number (0 = first, 1 = last) to its
        // spawn moment (0 = window start, 1 = window end). Linear (the default) = evenly spread, exactly the
        // pre-timing behaviour; an eased curve = burst-then-trickle (or the reverse); MinMax = every particle at
        // a random moment; a flat Static value s = all particles spawn together at moment s. Default Curve,
        // linear (0,0)→(1,1) — the renderer's IsLinear01 fast path keeps a default swarm byte-identical.
        public ZUIValue swarmSpawnTiming = DefaultSpawnTiming();
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

        // ── Modifiers — reuses Pyre's own PyreModifier directly, zero reimplementation ──
        [SerializeReference] public List<PyreModifier> modifiers = new List<PyreModifier>();

        // ── editor preview state (cosmetic; never affects the render) ──────────────
        [HideInInspector] public float previewZoom = 4f;
        [HideInInspector] public float previewFps = 12f;
        [HideInInspector] public int previewFrame = 0;
        [HideInInspector] public bool previewShowFrame = true;   // draw a thin canvas border in the preview (Frame toggle)

        public int Width => Mathf.Max(1, canvasSize);
        public int Height => Mathf.Max(1, canvasSize);

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
            // Linear identity (0,0)→(1,1): particle number maps straight to spawn moment, so the N spawns spread
            // evenly across the window — identical to the pre-timing distribution. Same shape as DefaultProgress;
            // the renderer detects this exact curve (IsLinear01) and short-circuits to the byte-identical fast path.
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(1f, 1f));
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
}
