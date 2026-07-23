using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// One click-placed orb of a MetaBlob layer: a position + radius that contributes to the fused metaball field,
    /// appearing at `birth` (0..1 of the layer's life, set by placement order) and living for `life` after — it
    /// grows in, holds, then melts out, so the merged shape grows and reshapes over time.
    [System.Serializable]
    public class MetaOrb
    {
        public Vector2 pos;
        [Range(0f, 1f)] public float birth = 0f;
        [Range(0.02f, 1f)] public float life = 0.8f;

        // Radius became an animatable ZUIValue (2026-07-23) so an orb can pulse/grow over its OWN
        // life. Migration: `radius` is the legacy scalar every already-authored BlastSpec still
        // serializes; `radiusValue` only becomes authoritative once `radiusUpgraded` is set, which
        // Radius does lazily on first access (seeded from the legacy scalar, so nothing shifts
        // visually). Unity never leaves a [Serializable]-class field null after deserialization, so
        // an explicit flag — not a null check — is what tells the two apart.
        [SerializeField] float radius = 12f;
        [SerializeField] ZUIValue radiusValue = new ZUIValue(12f);
        [SerializeField] bool radiusUpgraded;

        /// This orb's radius in canvas pixels, evaluated over its own life (Static by default).
        public ZUIValue Radius
        {
            get
            {
                if (!radiusUpgraded)
                {
                    radiusValue = new ZUIValue(radius);
                    radiusUpgraded = true;
                }
                return radiusValue;
            }
        }

        /// The radius as a plain number — the Static value, or the curve sampled at `t` (0..1 of
        /// this orb's own life). Editor gizmos that need one number use t = 0.
        public float RadiusAt(float t)
        {
            var v = Radius;
            return v.mode == ZUIValue.Mode.Curve
                ? ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(t), v.yMax)
                : v.staticValue;
        }

        /// True while the radius is a single fixed number (so an editor may offer a drag handle for it).
        public bool RadiusIsStatic => Radius.mode == ZUIValue.Mode.Static;

        /// Set a plain radius (the common editor path: a drag handle, a Static-mode edit).
        public void SetStaticRadius(float r)
        {
            var v = Radius;
            v.mode = ZUIValue.Mode.Static;
            v.staticValue = r;
            radius = r;   // keep the legacy scalar in step so older readers stay sane
        }

        public MetaOrb() { }

        public MetaOrb(Vector2 pos, float radius, float birth, float life)
        {
            this.pos = pos;
            this.birth = birth;
            this.life = life;
            this.radius = radius;
            radiusValue = new ZUIValue(radius);
            radiusUpgraded = true;
        }

        public MetaOrb Clone()
        {
            var c = new MetaOrb { pos = pos, birth = birth, life = life, radius = radius, radiusUpgraded = true };
            c.radiusValue = new ZUIValue();
            c.radiusValue.CopyFrom(Radius);
            return c;
        }
    }

    /// One ring of a Rosing-scatter layer: Count shapes evenly placed around this ring's own Radius (0..1, same
    /// convention as the plain Spawn radius), appearing together at Birth (0..1 of the layer's life) and living
    /// for Life after — mirrors MetaOrb's birth/life exactly, just for a whole ring of shapes instead of one orb.
    /// Radius is PLACEMENT (how far this ring sits from the origin); Size is a separate ×multiplier on the
    /// layer's own Size for just this ring's discs, so rings can graduate in disc size independently of how far
    /// out they sit — a fixed radius with tiny discs reads completely differently from the same radius with big
    /// ones. Stack a few (few shapes/small radius/early birth, then more/bigger/later) for a blooming rose.
    [System.Serializable]
    public class RoseRing
    {
        [Min(1)] public int count = 6;
        [Range(0f, 1f)] public float radius = 0.3f;
        [Range(0.1f, 3f)] public float sizeScale = 1f;
        [Range(0f, 1f)] public float birth = 0f;
        [Range(0.02f, 1f)] public float life = 1f;

        public RoseRing Clone() => new RoseRing { count = count, radius = radius, sizeScale = sizeScale, birth = birth, life = life };
    }

    /// One timed burst of N identical shapes that share a life span (startFrame..endFrame) and animate their
    /// size, position, colour and alpha across that life. A blast is a flat back-to-front stack of Layers.
    ///
    /// Most numeric knobs are <see cref="ZUIValue"/>s, so each can be a fixed constant, a Min-Max random spread
    /// (a stable per-shape value, or a per-frame shake for deform), or an animation Curve sampled over the
    /// blast timeline. Everything is authored in pixels/frames (positions/sizes) or normalised units
    /// (spawnRadius 0..1) so the same numbers mean the same thing in the editor preview, the baker and the
    /// runtime player — BlastRenderer reads this class in all three.
    [System.Serializable]
    public class Layer
    {
        [Tooltip("Label shown in the editor's layer list. Cosmetic only.")]
        public string name = "Layer";

        [Tooltip("Hide this layer in the preview and the bake without deleting it.")]
        public bool enabled = true;

        [Tooltip("First frame this layer's shapes are alive.")]
        public int startFrame = 0;
        [Tooltip("Last frame this layer's shapes are alive. Life is lerped 0..1 across [start, end].")]
        public int endFrame = 12;

        [Tooltip("Which primitive every shape in this layer draws.")]
        public LayerShape shape = LayerShape.Disc;

        // ── animatable per-shape values ──────────────────────────────────────────
        [Tooltip("How many shapes this layer scatters (rounded). Curve is sampled over blast progress.")]
        public ZUIValue count = new ZUIValue(6f);

        [Tooltip("Scatter radius as a fraction of the explosion (0 = centre, 1 = canvas edge). In Ring mode this is " +
                 "the rim's radius rather than the max of a random scatter.")]
        public ZUIValue spawnRadius = new ZUIValue(0.3f);

        [Tooltip("Area = the shapes scatter at random points inside Spawn radius (the existing behaviour). " +
                 "Ring = they're placed along the RIM at Spawn radius instead, per Ring order + the arc range. " +
                 "Rosing = an authored list of RINGS (below), each with its own count/radius/timing, blooming " +
                 "outward over life like a rose.")]
        public ScatterMode scatterMode = ScatterMode.Area;
        [Tooltip("Ring/Rosing: Sequential = shape #i sits at its own evenly-spaced slot around the arc, in index " +
                 "order. Random = each shape gets an independent random angle within the arc.")]
        public RingOrder ringOrder = RingOrder.Sequential;
        [Tooltip("Ring/Rosing: where the arc begins, in degrees (0 = +X axis, increasing counter-clockwise). A " +
                 "PLACEMENT value — read once, at each shape's own spawn moment, then fixed for that shape's life " +
                 "(same as Spawn radius). Animate it to change where NEW shapes land over time; it will never move " +
                 "an already-placed shape. For the whole ring to visibly spin as a live, ongoing effect, add a " +
                 "Rotate geometry modifier instead — that's a transform, not a placement decision.")]
        public ZUIValue ringStartAngle = new ZUIValue(0f);
        [Tooltip("Ring/Rosing: how much of the circle the arc spans, in degrees. 360 = the full rim; less confines " +
                 "the shapes to a wedge/fan starting at Ring start angle. Same spawn-locked placement semantics as " +
                 "Ring start angle above. Animatable.")]
        public ZUIValue ringArcDegrees = new ZUIValue(360f);
        [Tooltip("Ring/Rosing: rotate each shape to face its own angle around the ring, so an asymmetric shape " +
                 "(Crescent, an offset hole) orients outward/consistently instead of every instance sharing " +
                 "one fixed orientation.")]
        public bool ringAlignRotation = false;
        [Tooltip("Ring/Rosing: scales the ring's own placement radius EVERY FRAME (unlike Spawn radius/a ring's " +
                 "own Radius, which lock in place the moment a shape spawns) — so every shape already on the ring " +
                 "moves together as this animates, staying attached to the ring at its own angular slot while the " +
                 "ring itself grows or shrinks. Only moves shapes; never touches Size, so each disc/crescent's " +
                 "own size stays exactly what Size/a ring's own Size scale already gave it. 1 = unchanged. " +
                 "Animate it (a rising curve) for a ring that visibly blooms outward over life.")]
        public ZUIValue ringExpand = new ZUIValue(1f);
        [Tooltip("Rosing: draw order of the RINGS (not the discs within a ring). Off (default) = rings later in " +
                 "the list below composite ON TOP of earlier ones — with the default stack (small/early first, " +
                 "big/late last) that puts the outer, later-blooming ring in front. On = reversed, so earlier " +
                 "(usually inner) rings draw in front of later ones instead. Doesn't affect placement, timing, " +
                 "or count — only which ring's shapes composite over which.")]
        public bool roseReverseDraw = false;
        [Tooltip("Degrees this shape spins around its OWN centre over its OWN life (0..1 of ITS life span, not the " +
                 "layer's) — independent of Ring/Rosing placement entirely, and works for Area scatter too. Adds " +
                 "onto whatever Align rotation set as the initial facing, so a shape can start aligned outward " +
                 "then keep spinning from there. Animatable — e.g. a rising Curve spins it up continuously.")]
        public ZUIValue spinDegrees = new ZUIValue(0f);
        [Tooltip("Rosing: the rings. Each spawns its own Count shapes evenly around the shared arc (Start angle/ " +
                 "Arc degrees/Ring order above), at its own Radius, appearing together at Birth and living for " +
                 "Life after. Stack a few — fewer shapes, smaller radius, earlier birth for the first; more, " +
                 "bigger, later for the next — for a blooming rose.")]
        public List<RoseRing> roseRings = new List<RoseRing>
        {
            new RoseRing { count = 4, radius = 0.15f, birth = 0f, life = 1f },
            new RoseRing { count = 10, radius = 0.35f, birth = 0.15f, life = 0.85f },
            new RoseRing { count = 18, radius = 0.55f, birth = 0.3f, life = 0.7f },
        };
        [Tooltip("Ring/Rosing + Disc only: fuse every shape into ONE gradient-shaded metaball field (like " +
                 "MetaBlob, but fed by this layer's own procedurally-placed discs) instead of compositing them " +
                 "independently — nearby/overlapping discs melt together into one blob. Uses the same Threshold/ " +
                 "Shade range/Edge softness knobs as MetaBlob (revealed below when this is on).")]
        public bool fuse = false;

        [Tooltip("Extra X offset per shape, in pixels (fixed, random spread, or animated drift).")]
        public ZUIValue positionX = new ZUIValue(0f);
        [Tooltip("Extra Y offset per shape, in pixels (fixed, random spread, or animated drift).")]
        public ZUIValue positionY = new ZUIValue(0f);

        [Tooltip("Shape radius in pixels over life — a multicontrol (defaults to a grow-then-shrink envelope).")]
        public ZUIValue size = DefaultSize();

        [Tooltip("The shape's colour gradient. How it's applied is set by Colour mode.")]
        public Gradient colorOverLife = DefaultColor(LayerShape.Disc);
        [Tooltip("Disc/Crescent: Over life = one colour sampled at life; Fill = the gradient fills the shape " +
                 "centre→edge; Flow fill = that spatial fill scrolls through the (mirrored) gradient by Flow " +
                 "position; Noise fill = the gradient is painted through the layer's noise field (see the Noise " +
                 "params revealed below) for a cloudy/marbled interior.")]
        public ColorMode colorMode = ColorMode.OverLife;
        [Tooltip("Flow fill: the gradient scroll POSITION (multicontrol). Animate it — the curve's slope is the " +
                 "speed, its sign the direction. Default = a 0→1 sweep over life; make it static for no flow.")]
        public ZUIValue colorFlow = DefaultFlow();
        [Tooltip("Fill / Flow fill: how much of the gradient spans the shape. 1 = the whole gradient once; <1 = only " +
                 "part of it; >1 = it repeats. Animatable.")]
        public ZUIValue colorFlowZoom = new ZUIValue(1f);
        [Tooltip("Fill / Flow fill: moves the gradient CORE off-centre horizontally (−1..1 of the radius). Offset the " +
                 "core + a bright→dark gradient = a 3D orb / bowling-ball highlight. Animatable — a moving core.")]
        public ZUIValue gradientOffsetX = new ZUIValue(0f);
        [Tooltip("Fill / Flow fill: moves the gradient CORE off-centre vertically (−1..1 of the radius). Animatable.")]
        public ZUIValue gradientOffsetY = new ZUIValue(0f);
        [Tooltip("Alpha over life — a multicontrol (defaults to an envelope). The ONLY thing that fades a shape.")]
        public ZUIValue alpha = DefaultAlpha();

        // ── opt-in modifiers (Pyre v2): geometry warps (skew/rotate/squash/wobble) + pixel effects
        // (tint/dissolve/…) composed on top of this layer. Empty = no clutter. Serialized polymorphically.
        [SerializeReference]
        public List<PyreModifier> modifiers = new();

        /// The one, always-last SIMULATION modifier for just THIS layer — mirrors BlastSpec.simulationModifier
        /// exactly (its own dedicated single slot, not part of `modifiers` above), except scoped to this one
        /// layer's own isolated buffer instead of the whole composited frame. Lets a Pixel fluid (etc.) react to
        /// only this layer's own pixels/shape, independent of a blast-wide one in BlastSpec.simulationModifier —
        /// both can be used together (this one runs first, on this layer's own isolated buffer, before it
        /// composites onto the frame; the blast-wide one runs last of all, after every layer has composited).
        [SerializeReference]
        public PyreModifier simulationModifier;

        [Tooltip("Disc/Crescent: alpha gradient on the OUTER edge (0 = sharp, 1 = the whole shape fades out to its " +
                 "edge). Always available. Animatable.")]
        public ZUIValue outerSoftness = new ZUIValue(0f);
        [Tooltip("Disc: carve a hole out of the centre (a ring). Reveals Hole size + Inner softness.")]
        public bool hollow = false;
        [Tooltip("Disc (Hollow): hole radius as a fraction of the disc (0 = no hole → full disc, 1 = no disc left). " +
                 "Animatable — the hole can grow/shrink over time regardless of the edge softness.")]
        public ZUIValue holeSize = new ZUIValue(0.5f);
        [Tooltip("Disc (Hollow): alpha gradient on the hole's INNER edge (0 = sharp, 1 = soft). Scaled by Hole size, " +
                 "so it does nothing when the hole is 0 and grows with it. Crescent: the same softness, applied to " +
                 "the BITE edge instead of a hole (no Hole size scaling — the bite disc is always full-radius). " +
                 "Animatable.")]
        public ZUIValue innerSoftness = new ZUIValue(0f);
        [Tooltip("Disc (Hollow): move the hole CENTRE off the shape centre, X, in −1..1 of the radius. Offset hole = " +
                 "a crescent. Animatable — slide the hole across.")]
        public ZUIValue holeOffsetX = new ZUIValue(0f);
        [Tooltip("Disc (Hollow): move the hole CENTRE off the shape centre, Y, in −1..1 of the radius. Animatable.")]
        public ZUIValue holeOffsetY = new ZUIValue(0f);

        [Tooltip("Sprite: the sprite stamped as particles (its texture must be read/write enabled). Use the editor's " +
                 "'New sprite (Aseprite)' button to make + edit one.")]
        public Sprite particleSprite;
        [Tooltip("Sprite: degrees each particle spins over its life (each starts at a random angle). Animatable.")]
        public ZUIValue spriteSpin = new ZUIValue(0f);

        [Tooltip("SparkleField: fraction of pixels inside the circle that light up. Animatable — a rising envelope " +
                 "makes the sparkles ignite over the shape's life.")]
        public ZUIValue sparkleDensity = new ZUIValue(0.25f);
        [Tooltip("SparkleField: a sub-seed choosing WHICH pixels light up. Default = Min-Max random (re-rolls every " +
                 "frame, so the sparkles TWINKLE). Set it Static to freeze the pattern in place. Only used in the " +
                 "original single-pixel mode below — Blobs has its own, independent persistence mechanism.")]
        public ZUIValue sparkleSeed = DefaultSparkleSeed();
        [Tooltip("SparkleField: off (default) = the original single-pixel-per-frame twinkle, unchanged. On = each " +
                 "sparkle becomes a small blob with its own lifetime instead of a single flickering pixel — grows " +
                 "in, holds, fades out, and its own radius grows/shrinks along with that same envelope (so both " +
                 "its brightness AND its area of effect fade together), repeating on a cycle so it keeps twinkling " +
                 "without needing a fresh reroll every frame. Density still picks which cells get a sparkle at " +
                 "all; the reroll-every-frame Sparkle seed above doesn't apply here.")]
        public bool sparkleBlobs = false;
        [Tooltip("SparkleField (Blobs): each sparkle's radius at the peak of its cycle, in pixels. Animatable.")]
        public ZUIValue sparkleBlobRadius = new ZUIValue(2f);
        [Tooltip("SparkleField (Blobs): how many frames one full grow-hold-fade cycle takes. Animatable — longer " +
                 "life reads as a lingering glow, shorter as a rapid twinkle.")]
        public ZUIValue sparkleBlobLife = new ZUIValue(8f);
        [Tooltip("SparkleField (Blobs): softness of each sparkle's own edge (0 = a hard dot, 1 = a soft glow). Animatable.")]
        public ZUIValue sparkleBlobSoftness = new ZUIValue(0.6f);

        [Tooltip("Crescent: X offset of the mask disc that bites into the main disc, in units of the RADIUS (same " +
                 "convention as Hole offset X/Y) — so the crescent's proportions (sliver thickness/curvature) stay " +
                 "put as Size changes, instead of a fixed pixel offset going from barely-a-bite to no-overlap-at-" +
                 "all as the disc grows or shrinks. The mask disc shares the main disc's own radius, so the bite " +
                 "only fully vanishes (a plain disc, no crescent) once the offset magnitude reaches 2 (both discs " +
                 "the same size, pushed apart by their combined radii) — 1 alone only reaches the half-moon " +
                 "bisection. Animatable.")]
        public ZUIValue crescentOffsetX = new ZUIValue(0.45f);
        [Tooltip("Crescent: Y offset of the mask disc, in units of the RADIUS — see X offset for the full range " +
                 "explanation. Animatable.")]
        public ZUIValue crescentOffsetY = new ZUIValue(0f);

        [Range(0f, 1f)]
        [Tooltip("Randomises each shape's start/end within the layer window so they don't all pop together.")]
        public float perShapeLifeJitter = 0.3f;

        [Range(0f, 1f)]
        [Tooltip("Distributes the Count shapes across the layer's timeline (each spawns later, with a correspondingly " +
                 "shorter life). 0 = all live the full window; 1 = evenly spread — first spawn starts at the first " +
                 "frame, last spawn ends at the last.")]
        public float spawnStagger = 0f;
        [Tooltip("Area/Ring: every shape reaches the END of its life at the SAME frame (this layer's own End " +
                 "frame) regardless of when it spawned, instead of each shape getting the same fixed duration " +
                 "(which — with Spawn stagger above — makes later spawns end later too). Shapes born earlier " +
                 "mature more slowly (a longer life) so the whole burst finishes together. Composes oddly with a " +
                 "large Spawn stagger: a very-late spawn gets squeezed into a very short life (down to a 1-frame " +
                 "floor) to still die on time, so it can read as a pop rather than a fade.")]
        public bool syncDeath = false;

        // ── Bars mode (LayerShape.Bars): a symmetric row of forward-growing bars streaming off an edge ──
        // Most are multicontrols evaluated over the layer's timeline, so the whole row can animate (sweep the
        // angle, widen the spacing, pulse the width…).
        [Tooltip("Bars: number of bars on EACH side of the centre bar (total = 2*barCount + 1). Animatable.")]
        public ZUIValue barCount = new ZUIValue(7f);
        [Tooltip("Bars: neighbour spacing in bar-WIDTHS. 1 = bars touch (no gap), 2 = a one-bar gap, … (min 1). Animatable.")]
        public ZUIValue barSpacing = new ZUIValue(1.5f);
        [Tooltip("Bars: thickness of each bar (across the direction), in pixels (min 1). Animatable.")]
        public ZUIValue barWidth = new ZUIValue(3f);
        [Tooltip("Bars: edge softness — alpha gradient on the bar SIDES and TIP (0 = sharp, 1 = very soft). Applies " +
                 "in Dissolve mode too. Animatable.")]
        public ZUIValue barSoftness = new ZUIValue(0f);
        [Tooltip("Bars: how far a bar reaches FORWARD (along the direction) at full growth — animatable over its life.")]
        public ZUIValue barForward = DefaultBarForward();
        [Tooltip("Bars: backward reach as a fraction of the forward reach (a little spill behind the surface). Animatable.")]
        public ZUIValue barBackwardFrac = new ZUIValue(0.18f);
        [Tooltip("Bars: arm silhouette by distance from the centre bar. +1 = centre longest, tapering to the edges " +
                 "(a triangle/flame); 0 = all bars equal (a rectangle); -1 = concave (edges longest). This is the " +
                 "shape control — independent of the timing Stagger. Animatable.")]
        public ZUIValue barTaper = new ZUIValue(0.85f);
        [Tooltip("Bars: per-bar appearance delay as a fraction of the layer window (centre bar first, then outward). " +
                 "Timing only — the arm silhouette is Taper. Animatable.")]
        public ZUIValue barStagger = new ZUIValue(0.05f);
        [Tooltip("Bars: this layer's angle offset from the blast's base angle, in degrees. Animatable (sweep the row).")]
        public ZUIValue barAngleDeg = new ZUIValue(0f);
        [Tooltip("Bars: also draw a mirror of this layer's angle on the other side of the base angle.")]
        public bool barMirror = false;
        [Tooltip("Bars: Contract = size envelope shrinks back; Dissolve = size holds at its peak, then bars fade " +
                 "out from the centre outward (a transparency front spreads across the row).")]
        public BarDecay barDecay = BarDecay.Contract;
        [Range(0f, 1f)]
        [Tooltip("Bars (Dissolve): fraction of the layer's life at which the dissolve front starts spreading.")]
        public float dissolveStart = 0.45f;
        [Tooltip("Bars: push the origin this many pixels in from the surface edge, for a little breathing room. Animatable.")]
        public ZUIValue originInset = new ZUIValue(4f);

        // ── Bars star (PER-LAYER): duplicate this bar row into arms radiating from the centre ──
        [Tooltip("Bars: fundamental direction (deg) this row grows toward. Animatable.")]
        public ZUIValue baseAngleDeg = new ZUIValue(0f);
        [Tooltip("Bars: Star = duplicate this layer into `arms` copies sharing the centre and radiating outward (an " +
                 "asterisk); the canvas auto-fits. Off = a single arm off the back edge. Per-layer.")]
        public bool star = false;
        [Min(1)]
        [Tooltip("Bars star: how many arms radiate from the centre.")]
        public int spreadCount = 5;
        [Tooltip("Bars star: total arc (deg) the arms span. 360 = a full circle. Animatable.")]
        public ZUIValue spreadDegrees = new ZUIValue(360f);

        // ── MetaBlob: click-placed orbs that fuse into ONE gradient-shaded shape (SDF metaballs) ──
        [Tooltip("MetaBlob: the placed orbs. Click in the preview to drop them (in order); each grows in, holds, then " +
                 "melts out over its life, so the fused field grows and reshapes.")]
        public List<MetaOrb> metaOrbs = new List<MetaOrb>();
        [Tooltip("MetaBlob: iso-threshold. Lower = orbs fuse more eagerly (fatter necks, one shape); higher = distinct lobes.")]
        public float metaThreshold = 0.6f;
        [Tooltip("MetaBlob: how much field above the threshold spans the gradient (surface→core). Smaller = punchier core.")]
        public float metaShadeRange = 1.5f;
        [Tooltip("MetaBlob: edge softness (alpha AA band across the iso-surface). 0 = crisp.")]
        public float metaSoftness = 0.18f;
        [Range(0f, 1f)]
        [Tooltip("MetaBlob: gap (fraction of the layer life) applied between successive orbs' birth times. Only takes " +
                 "effect when you PLACE A NEW ORB (sets its birth = orb count x this) or hit 'Renumber births' below " +
                 "— it does not retroactively change already-placed orbs just by moving this slider.")]
        public float metaSpawnInterval = 0.12f;
        [Tooltip("MetaBlob: multiply EVERY orb's radius over the layer's life — a shared breathe/pulse. Animate it " +
                 "(a curve) to swell then settle the whole blob at once. 1 = the placed radii.")]
        public ZUIValue metaRadiusScale = new ZUIValue(1f);
        [Tooltip("MetaBlob: contract/expand ALL orb centres about the blast's origin. <1 implodes toward the origin, " +
                 ">1 flings them out. Animate 0→N for a burst, or N→1 to gather in. 1 = the placed positions.")]
        public ZUIValue metaExpand = new ZUIValue(1f);

        // ── Height balls: a cloud of soft balls fused into density/heat/height fields, relief-lit, shaded by ONE
        // gradient (low = smoke, high = fire). Placement, energy and confinement are all closed-form functions of
        // (seed, ball index, frame) — no frame-to-frame state — so it scrubs and bakes like every other shape.
        // Count / Spawn radius / Size / Position / Alpha / Colour are the layer's own shared fields, reused here.
        [Tooltip("Height balls: how much MASS each ball adds to the cloud. Raises the whole cloud's height (so it " +
                 "catches more relief light) and pushes it up the gradient even with no heat at all. Animatable.")]
        public ZUIValue hbDensity = new ZUIValue(0.16f);
        [Tooltip("Height balls: the RESTING heat every ball idles at — where an ordinary, un-energised bubble sits " +
                 "on the gradient (near the smoke end). A newly born wave ball starts here too, then climbs. " +
                 "Animatable — raise it over life to warm the whole cloud at once.")]
        public ZUIValue hbBaseHeat = new ZUIValue(0.06f);
        [Range(0f, 1f)]
        [Tooltip("Height balls: how eagerly neighbouring balls MELT into each other (a smooth-max blend instead of " +
                 "a hard one). 0 = each ball keeps its own hard edge; higher fuses them into one metaball-like mass " +
                 "with soft necks between lobes.")]
        public float hbFusion = 0.35f;
        [Range(0.5f, 12f)]
        [Tooltip("Height balls: how fast the cloud reaches full opacity as it thickens. Low = a wispy, translucent " +
                 "cloud whose thin rim stays see-through; high = a solid silhouette that only fades at the very edge.")]
        public float hbCoverage = 8f;
        [Tooltip("Height balls: shade the cloud by the local SLOPE of its height field, as if lit from one side. " +
                 "This is what gives the chunky pixel-art 3D read; off leaves flat gradient shading.")]
        public bool hbLighting = true;
        [Range(0.2f, 6f)]
        [Tooltip("Height balls: how steeply the height field is treated when lighting it — how pronounced the " +
                 "bumps and creases between fused balls read. High values exaggerate every lobe into hard facets.")]
        public float hbRelief = 3f;
        [Tooltip("Height balls: where the light comes from, in degrees (0 = from the right, 90 = from above). " +
                 "Animatable — sweep it and the whole cloud's shading rolls across.")]
        public ZUIValue hbLightAngle = new ZUIValue(135f);
        [Tooltip("Height balls: how far each ball wanders from its resting spot, in pixels — the cloud's idle " +
                 "churn. Animatable.")]
        public ZUIValue hbChurn = new ZUIValue(2.5f);
        [Range(0.1f, 8f)]
        [Tooltip("Height balls: how many full churn cycles a ball completes across this layer's life. Low = a slow " +
                 "roll; high = a busy boil.")]
        public float hbChurnSpeed = 1.6f;
        [Tooltip("Height balls: pixels the cloud drifts upward across its life — smoke rising. Animatable.")]
        public ZUIValue hbRise = new ZUIValue(0f);

        [Range(0, 12)]
        [Tooltip("Height balls: how many bursts of energy fire off over this layer's life, evenly spaced so the " +
                 "last one still finishes before the layer ends. 0 = a purely idling cloud.")]
        public int hbWaves = 3;
        [Range(1, 24)]
        [Tooltip("Height balls: how many balls each wave adds, spread around a circle from the cloud's centre.")]
        public int hbWaveBalls = 7;
        [Range(0.05f, 1f)]
        [Tooltip("Height balls: how long one wave lasts, as a fraction of this layer's life — its whole climb, " +
                 "hold and wither.")]
        public float hbWaveLife = 0.55f;
        [Range(0f, 1f)]
        [Tooltip("Height balls: 1 = a wave's balls sit at perfectly even angles; lower scatters them, so bursts " +
                 "read as lopsided and organic rather than as a clean rosette.")]
        public float hbWaveSymmetry = 0.8f;
        [Tooltip("Height balls: the heat a wave ball reaches at its peak — how far UP the gradient it climbs " +
                 "(toward the fire end). Animatable: make later waves hotter or cooler than earlier ones.")]
        public ZUIValue hbWaveHeat = new ZUIValue(0.8f);
        [Tooltip("Height balls: how far outward a wave ball travels, in pixels, before the push runs out. It " +
                 "eases to a stop rather than flying, and Confine below caps it regardless. Animatable.")]
        public ZUIValue hbWavePush = new ZUIValue(14f);
        [Range(0.02f, 0.9f)]
        [Tooltip("Height balls: the fraction of a wave ball's life spent CLIMBING from resting heat to its peak. " +
                 "Large values mean a new ball is indistinguishable from an ordinary cloud ball when it appears " +
                 "and only heats up gradually; tiny values make it flash hot the moment it's born.")]
        public float hbIgnition = 0.35f;
        [Range(0.02f, 0.95f)]
        [Tooltip("Height balls: the fraction of a wave ball's life spent WITHERING — cooling back down the gradient " +
                 "toward smoke while shrinking and thinning out, so it dies away instead of blinking out.")]
        public float hbWither = 0.5f;
        [Range(0.05f, 1f)]
        [Tooltip("Height balls: the cloud's self-limiting radius, as a fraction of the canvas half-size. Nothing " +
                 "the cloud draws can ever reach past it — travel is squeezed smoothly toward this limit rather " +
                 "than clipped — so the animation never collides with the frame edge.")]
        public float hbConfine = 0.8f;
        [Range(0f, 1f)]
        [Tooltip("Height balls: how much of the outer cloud gets FOLDED UNDER by pressure. A ball pushed into that " +
                 "band loses mass and heat, shrinks, sinks back toward smoke and is tucked inward — so pushing " +
                 "harder thickens and churns the cloud instead of flinging balls outward. 0 = no folding.")]
        public float hbFold = 0.4f;

        // ── Noise fill: the domain-warped noise field ColorMode.NoiseFill paints through a shape's own silhouette
        // (Disc/Crescent/MetaBlob) — the shape stays the alpha mask; this is texture only, no silhouette of its own.
        [Tooltip("Noise fill: shifts which part of the gradient the noise field maps to, wrapping around — the " +
                 "SAME noise pattern reads as a different band of colour. Animate it and the gradient sweeps " +
                 "through the noise like a glow/ember effect, independent of the noise pattern's own drift.")]
        public ZUIValue noiseGradientPosition = new ZUIValue(0f);
        [Tooltip("Noise fill: scales the noise value onto the gradient before Position shifts it — 1 (default) = " +
                 "one full gradient cycle across the noise's own range, unchanged from before this field existed. " +
                 "Higher repeats the gradient several times through the SAME pattern (more, tighter colour bands); " +
                 "lower compresses it into a narrower slice of the gradient. Animatable.")]
        public ZUIValue noiseGradientZoom = new ZUIValue(1f);
        [Tooltip("Noise fill: noise frequency — bigger = larger, slower-looking billows; smaller = fine, busy detail. Animatable.")]
        public ZUIValue noiseZoom = new ZUIValue(20f);
        [Tooltip("Noise fill: rotates the noise sampling domain, in degrees — spins the churn in place. Animatable.")]
        public ZUIValue noiseRotation = new ZUIValue(0f);
        [Tooltip("Noise fill: drifts the noise sampling domain horizontally over life, in pixels. Animatable.")]
        public ZUIValue noiseDriftX = new ZUIValue(0f);
        [Tooltip("Noise fill: drifts the noise sampling domain vertically over life, in pixels. Animatable.")]
        public ZUIValue noiseDriftY = new ZUIValue(0f);
        [Tooltip("Noise fill: domain-warp strength — how much the noise bends on itself (0 = smooth blobby cloud, " +
                 "higher = churned/organic eddies). Animatable.")]
        public ZUIValue noiseWarp = new ZUIValue(0.6f);
        [Range(1, 8)]
        [Tooltip("Noise fill: number of discrete shading bands across the field's depth. 1 = smooth (no banding).")]
        public int noiseBands = 4;
        [Range(0f, 1f)]
        [Tooltip("Noise fill: softens the cut between adjacent shading bands. 0 = the hard step Bands has always " +
                 "had; 1 = each band blends fully into its neighbour (no visible step at all, same look as " +
                 "Bands=1). Use a small amount to keep recognizable bands without the harsh pop between them.")]
        public float noiseBandSoftness = 0f;

        /// A pleasing starting point per shape type; the editor adds layers through this.
        public static Layer Default(LayerShape shape)
        {
            var l = new Layer
            {
                name = shape.ToString(),
                enabled = true,
                shape = shape,
                startFrame = 0,
                endFrame = 12,
                count = new ZUIValue(6f),
                spawnRadius = new ZUIValue(0.28f),
                positionX = new ZUIValue(0f),
                positionY = new ZUIValue(0f),
                size = DefaultSize(),
                sparkleDensity = new ZUIValue(0.25f),
                crescentOffsetX = new ZUIValue(0.45f),
                crescentOffsetY = new ZUIValue(0f),
                perShapeLifeJitter = 0.3f,
                colorOverLife = DefaultColor(shape),
                alpha = DefaultAlpha(),
            };

            switch (shape)
            {
                case LayerShape.Sprite:
                    l.count = new ZUIValue(10f); l.spawnRadius = new ZUIValue(0.4f);
                    l.size = CurveVal(8f, 0f, 6f, 0.5f, 8f, 1f, 4f);
                    break;
                case LayerShape.SparkleField:
                    l.count = new ZUIValue(1f); l.spawnRadius = new ZUIValue(0f);
                    l.size = CurveVal(26f, 0f, 10f, 1f, 22f); l.sparkleDensity = new ZUIValue(0.12f);
                    break;
                case LayerShape.Crescent:
                    l.count = new ZUIValue(4f); l.spawnRadius = new ZUIValue(0.28f);
                    l.size = CurveVal(14f, 0f, 7f, 1f, 11f);
                    break;
                case LayerShape.MetaBlob:
                    l.alpha = new ZUIValue(1f);
                    l.colorOverLife = WhiteHotGradient();   // fire: white-hot core → dark edge across the field
                    l.metaOrbs = new List<MetaOrb>
                    {
                        new MetaOrb(new Vector2(-8f, 0f), 16f, 0f,    1f),
                        new MetaOrb(new Vector2( 9f, 3f), 14f, 0.15f, 0.85f),
                        new MetaOrb(new Vector2( 0f,-9f), 12f, 0.3f,  0.7f),
                    };
                    break;
                case LayerShape.HeightBalls:
                    l.count = new ZUIValue(48f);
                    l.spawnRadius = new ZUIValue(0.34f);
                    l.size = new ZUIValue(6f);          // one steady ball radius; the CLOUD does the animating
                    l.colorOverLife = SmokeToFireGradient();
                    break;
            }
            return l;
        }

        /// Deep copy — used by the editor's "Dup" so tweaking a duplicate never bleeds into the original.
        /// MemberwiseClone copies the value fields; every reference field (ZUIValues, Gradient, curve) is cloned.
        public Layer Clone()
        {
            var l = (Layer)MemberwiseClone();
            l.count = CloneVal(count);
            l.spawnRadius = CloneVal(spawnRadius);
            l.ringStartAngle = CloneVal(ringStartAngle);
            l.ringArcDegrees = CloneVal(ringArcDegrees);
            l.ringExpand = CloneVal(ringExpand);
            l.positionX = CloneVal(positionX);
            l.positionY = CloneVal(positionY);
            l.size = CloneVal(size);
            l.crescentOffsetX = CloneVal(crescentOffsetX);
            l.crescentOffsetY = CloneVal(crescentOffsetY);
            l.sparkleDensity = CloneVal(sparkleDensity);
            l.sparkleSeed = CloneVal(sparkleSeed);
            l.sparkleBlobRadius = CloneVal(sparkleBlobRadius);
            l.sparkleBlobLife = CloneVal(sparkleBlobLife);
            l.sparkleBlobSoftness = CloneVal(sparkleBlobSoftness);
            l.outerSoftness = CloneVal(outerSoftness);
            l.holeSize = CloneVal(holeSize);
            l.innerSoftness = CloneVal(innerSoftness);
            l.holeOffsetX = CloneVal(holeOffsetX);
            l.holeOffsetY = CloneVal(holeOffsetY);
            l.colorFlow = CloneVal(colorFlow);
            l.colorFlowZoom = CloneVal(colorFlowZoom);
            l.gradientOffsetX = CloneVal(gradientOffsetX);
            l.gradientOffsetY = CloneVal(gradientOffsetY);
            l.barCount = CloneVal(barCount);
            l.barSpacing = CloneVal(barSpacing);
            l.barWidth = CloneVal(barWidth);
            l.barSoftness = CloneVal(barSoftness);
            l.barForward = CloneVal(barForward);
            l.barBackwardFrac = CloneVal(barBackwardFrac);
            l.barAngleDeg = CloneVal(barAngleDeg);
            l.originInset = CloneVal(originInset);
            l.barTaper = CloneVal(barTaper);
            l.barStagger = CloneVal(barStagger);
            l.baseAngleDeg = CloneVal(baseAngleDeg);
            l.spreadDegrees = CloneVal(spreadDegrees);
            l.spriteSpin = CloneVal(spriteSpin);
            l.spinDegrees = CloneVal(spinDegrees);
            l.colorOverLife = CloneGradient(colorOverLife);
            l.alpha = CloneVal(alpha);
            l.modifiers = modifiers == null ? new List<PyreModifier>()
                : modifiers.ConvertAll(m => m?.Clone());
            l.metaRadiusScale = CloneVal(metaRadiusScale);
            l.metaExpand = CloneVal(metaExpand);
            l.metaOrbs = metaOrbs == null ? new List<MetaOrb>()
                : metaOrbs.ConvertAll(o => o == null ? new MetaOrb() : o.Clone());
            l.roseRings = roseRings == null ? new List<RoseRing>()
                : roseRings.ConvertAll(r => r == null ? new RoseRing() : r.Clone());
            l.hbDensity = CloneVal(hbDensity);
            l.hbBaseHeat = CloneVal(hbBaseHeat);
            l.hbLightAngle = CloneVal(hbLightAngle);
            l.hbChurn = CloneVal(hbChurn);
            l.hbRise = CloneVal(hbRise);
            l.hbWaveHeat = CloneVal(hbWaveHeat);
            l.hbWavePush = CloneVal(hbWavePush);
            l.noiseGradientPosition = CloneVal(noiseGradientPosition);
            l.noiseGradientZoom = CloneVal(noiseGradientZoom);
            l.noiseZoom = CloneVal(noiseZoom);
            l.noiseRotation = CloneVal(noiseRotation);
            l.noiseDriftX = CloneVal(noiseDriftX);
            l.noiseDriftY = CloneVal(noiseDriftY);
            return l;
        }

        internal static ZUIValue CloneVal(ZUIValue s)
        {
            if (s == null) return new ZUIValue();
            var v = new ZUIValue(s.staticValue)
            {
                mode = s.mode, min = s.min, max = s.max, yMin = s.yMin, yMax = s.yMax,
                duration = s.duration, warmup = s.warmup, cooldown = s.cooldown, multiplierId = s.multiplierId,
            };
            v.points.Clear();
            foreach (var p in s.points) v.points.Add(new ZUIEnvelopePoint(p.time, p.value, p.exponent, p.editState));
            return v;
        }

        /// A flat white gradient — the identity for the multiplying cross gradient / colour grade.
        public static Gradient WhiteGradient()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        internal static Gradient CloneGradient(Gradient g)
        {
            if (g == null) return null;
            var n = new Gradient();
            n.SetKeys(g.colorKeys, g.alphaKeys);
            n.mode = g.mode;
            return n;
        }

        /// A dark grey→charcoal smoke gradient.
        public static Gradient SmokeGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.55f, 0.52f, 0.5f), 0f),
                    new GradientColorKey(new Color(0.28f, 0.26f, 0.26f), 0.5f),
                    new GradientColorKey(new Color(0.10f, 0.10f, 0.12f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// A shape-appropriate default gradient (fire for most, hot sparks for sparkle fields).
        public static Gradient DefaultColor(LayerShape shape)
        {
            var g = new Gradient();
            switch (shape)
            {
                case LayerShape.HeightBalls:      // one continuous smoke → fire ramp
                    return SmokeToFireGradient();
                case LayerShape.SparkleField:     // hot sparks
                    g.SetKeys(
                        new[]
                        {
                            new GradientColorKey(Color.white, 0f),
                            new GradientColorKey(new Color(1f, 0.9f, 0.35f), 0.4f),
                            new GradientColorKey(new Color(1f, 0.55f, 0.15f), 1f),
                        },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                    break;
                default:                          // fire: white -> yellow -> orange -> red
                    g.SetKeys(
                        new[]
                        {
                            new GradientColorKey(Color.white, 0f),
                            new GradientColorKey(new Color(1f, 0.92f, 0.5f), 0.25f),
                            new GradientColorKey(new Color(1f, 0.55f, 0.12f), 0.6f),
                            new GradientColorKey(new Color(0.7f, 0.12f, 0.05f), 1f),
                        },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                    break;
            }
            return g;
        }

        /// ONE continuous smoke→fire ramp: near-black smoke at the low end climbing through ember red and orange
        /// into a pale hot core. Height balls index this by a pixel's combined density+heat, so "adding energy"
        /// literally walks a ball up this gradient (and withering walks it back down).
        public static Gradient SmokeToFireGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.051f, 0.059f, 0.078f), 0f),
                    new GradientColorKey(new Color(0.102f, 0.114f, 0.141f), 0.14f),
                    new GradientColorKey(new Color(0.204f, 0.204f, 0.227f), 0.26f),
                    new GradientColorKey(new Color(0.333f, 0.125f, 0.086f), 0.40f),
                    new GradientColorKey(new Color(0.651f, 0.165f, 0.051f), 0.55f),
                    new GradientColorKey(new Color(0.941f, 0.392f, 0.047f), 0.70f),
                    new GradientColorKey(new Color(1f, 0.757f, 0.173f), 0.85f),
                    new GradientColorKey(new Color(1f, 0.961f, 0.784f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// A punchy white-hot core gradient (bright centre falling to warm gold).
        public static Gradient WhiteHotGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0.35f),
                    new GradientColorKey(new Color(1f, 0.7f, 0.25f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// A ZUIValue in Curve mode built from flat (time, value) pairs. yMax bounds the curve editor's Y axis.
        public static ZUIValue CurveVal(float yMax, params float[] tv)
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = yMax };
            v.points.Clear();
            for (int i = 0; i + 1 < tv.Length; i += 2) v.points.Add(new ZUIEnvelopePoint(tv[i], tv[i + 1]));
            return v;
        }

        /// Grow-from-nothing then shrink-to-nothing radius envelope — no motion is built in, so Size defaults to an
        /// envelope the user tweaks (Pyre v2: nothing grows/shrinks on its own).
        public static ZUIValue DefaultSize() => CurveVal(16f, 0f, 1f, 0.35f, 14f, 1f, 1f);

        /// Rise quickly, hold, then fade — a punchy explosion alpha envelope.
        public static ZUIValue DefaultAlpha() => CurveVal(1f, 0f, 0f, 0.15f, 1f, 0.7f, 1f, 1f, 0f);

        /// Flow-fill scroll position — a 0→1 sweep over life by default (so it flows; the user re-shapes it).
        public static ZUIValue DefaultFlow() => CurveVal(1f, 0f, 0f, 1f, 1f);

        /// Sparkle sub-seed — a Min-Max random by default so it re-rolls every frame (the sparkles twinkle).
        public static ZUIValue DefaultSparkleSeed() => new ZUIValue(0f) { mode = ZUIValue.Mode.MinMax, min = 0f, max = 1f };

        /// A bar's forward reach over its life: shoot out fast, then pull back.
        public static ZUIValue DefaultBarForward() => CurveVal(48f, 0f, 2f, 0.4f, 40f, 1f, 8f);
    }
}
