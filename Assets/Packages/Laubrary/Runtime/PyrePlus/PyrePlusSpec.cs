// PyrePlusSpec — the asset behind the PyrePlus prototype (see PYREPLUS_DESIGN.md).
//
// PyrePlus is a PARALLEL rework of Pyre's shape system, kept entirely separate from the shipping
// Pyre/BlastSpec/Layer/BlastRenderer so the real tool is never at risk. One implicit layer organized into
// three conceptual sections — Shape, Swarm, Modifiers — instead of Pyre's one flat ~40-field Layer. The Shape
// section (one particle) and the full Swarm data model are in place; the Swarm renderer is being built out
// task by task (T1 = Area+Circle placement) and Modifiers reuse Pyre's PyreModifier directly.
using System.Collections.Generic;
using Laubrary.Pyre;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    // How the swarm places its particles: uniformly inside the shape's area, or travelling along its outline.
    public enum SwarmSpawnMode { Area, Path }

    // The swarm shape: a regular polygon by side count (Circle = ∞ sides), or a hand-authored polyline.
    // Custom is Path-only (enforced in the UI later); the renderer treats Area+Custom as a Circle fallback.
    public enum SwarmShapeKind { Circle, Triangle, Square, Pentagon, Hexagon, Custom }

    // The particle's rendered FORM. One implicit layer, so the whole swarm is one form. The STATELESS forms:
    //   Disc     — a flat soft disc (slice 1).
    //   Gem      — a true-3D lit faceted octahedral crystal (per-pixel point lighting, hard edge lines, two
    //              staggered glows; see PyrePlusRenderer.DrawGem).
    //   Crescent — a disc with a second offset disc masked out (its own bite size / facing / push-out).
    //   Sparkle  — random lit pixel-cells scattered inside the disc, twinkling deterministically per frame.
    //   Sprite   — a Sprite's pixels stamped, scaled/rotated to the particle, optionally tinted by the gradient.
    // APPEND ONLY — the values are serialized as ints, so never reorder or insert. (Fire/Fireball/HeightBalls
    // are simulation-backed and deferred; they are NOT here.)
    public enum ShapeForm { Disc, Gem, Crescent, Sparkle, Sprite }

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
        // (the Gem block below drives it); its material colour is colorOverLife, its radius is `size`.
        public ShapeForm shapeForm = ShapeForm.Disc;
        public Gradient colorOverLife = DefaultColor();
        public ZUIValue alpha = DefaultAlpha();     // over the particle's OWN life (multiplies the final output alpha for BOTH forms)
        public ZUIValue size = DefaultSize();       // radius in pixels, over the particle's own life (Gem: the girdle radius)
        [Range(0f, 1f)] public float edgeSoftness = 0.4f;   // soft rim vs hard pixel edge (Disc form only)

        // ── Gem form (shapeForm == Gem) — a true-3D faceted crystal ────────────────
        // Octahedral by default (gemSides == 4): a square girdle with a crown point above and a longer pavilion
        // point below, lit per-pixel. Base colour = colorOverLife at the particle's own life (a blue gradient =
        // a sapphire). The `size` envelope is the single scale driver — the girdle radius R = evaluated size.
        [Range(3, 8)] public int gemSides = 4;              // girdle vertex count (4 = the approved octahedron)
        public float gemCrown = 0.75f;                      // crown height as a fraction of R (the girdle radius)
        public float gemPavilion = 1.55f;                   // pavilion depth as a fraction of R
        public ZUIValue gemTilt = new ZUIValue(18f);        // world tilt about X, degrees, over the particle's own life
        // Lighting. The light POSITION derives in the renderer from these two angles at distance 3.5·R with a
        // 4.7·R falloff range (the prototype's proportions) — distance is not exposed.
        public float gemLightYaw = -55f;                    // key-light azimuth (left/right), degrees
        public float gemLightPitch = 38f;                   // key-light elevation (above the horizon), degrees
        [Range(0f, 1f)] public float gemAmbient = 0.05f;    // near-zero fill on unlit faces
        [Range(0f, 2f)] public float gemSpecular = 0.9f;    // Blinn-Phong highlight strength
        // Light-catching hard edge lines along every visible facet boundary.
        [Range(0f, 3f)] public float gemLineWidth = 1f;     // edge-line width in screen pixels (0 = no lines)
        public Color gemLineColor = new Color(1f, 0.98f, 0.90f);
        // The two staggered glows — strength 0..1 over the particle's OWN life. EdgeGlow is a white halo around
        // the edge lines that spills OUTSIDE the silhouette; InnerGlow is emissive light rising from the facet
        // interiors. Their defaults pulse in ANTI-PHASE (see the factories).
        public ZUIValue gemEdgeGlow = DefaultEdgeGlow();
        public ZUIValue gemInnerGlow = DefaultInnerGlow();

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
        // fallback instead. `spriteTint` multiplies by colorOverLife (at own life); off = raw sprite colours.
        public Sprite spriteImage;
        public bool spriteTint = true;

        // ── opt-in Shape fields (T7) — the particle's OWN motion after birth, on its own life clock ────────
        // A per-particle travel path: canvas-pixel offsets ADDED to the particle's spawn position, evaluated on
        // its OWN life (0 = birth, 1 = death). Default Static 0 (a no-op — the renderer skips the Eval entirely
        // when both are Static 0, so a default asset renders byte-identical).
        public ZUIValue particlePathX = new ZUIValue(0f);
        public ZUIValue particlePathY = new ZUIValue(0f);
        // The particle's own rotation over its own life, degrees. DISC form: its pixels rotating IN PLACE (2D —
        // the pseudo-3D tilt belongs to the swarm shape transform shapePitch/shapeYaw, not here). GEM form: the
        // gem's 3D YAW about its vertical axis (see PyrePlusRenderer.DrawGem). Default Static 0 (a no-op).
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
            // Two anti-phase pulses over the particle's life; reshape freely. The exact mirror of DefaultInnerGlow:
            // this glow PEAKS where the inner glow RESTS and vice versa (the approved staggered double-pulse look).
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0.5f));
            v.points.Add(new ZUIEnvelopePoint(0.125f, 1f));
            v.points.Add(new ZUIEnvelopePoint(0.375f, 0f));
            v.points.Add(new ZUIEnvelopePoint(0.625f, 1f));
            v.points.Add(new ZUIEnvelopePoint(0.875f, 0f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0.5f));
            return v;
        }

        static ZUIValue DefaultInnerGlow()
        {
            // Two anti-phase pulses over the particle's life; reshape freely. The exact mirror of DefaultEdgeGlow
            // (peaks while the edge glow rests) — together they read as one crystal breathing between rim and core.
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0.5f));
            v.points.Add(new ZUIEnvelopePoint(0.125f, 0f));
            v.points.Add(new ZUIEnvelopePoint(0.375f, 1f));
            v.points.Add(new ZUIEnvelopePoint(0.625f, 0f));
            v.points.Add(new ZUIEnvelopePoint(0.875f, 1f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0.5f));
            return v;
        }
    }
}
