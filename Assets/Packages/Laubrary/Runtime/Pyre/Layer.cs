using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre
{
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

        [Tooltip("Scatter radius as a fraction of the explosion (0 = centre, 1 = canvas edge).")]
        public ZUIValue spawnRadius = new ZUIValue(0.3f);

        [Tooltip("Extra X offset per shape, in pixels (fixed, random spread, or animated drift).")]
        public ZUIValue positionX = new ZUIValue(0f);
        [Tooltip("Extra Y offset per shape, in pixels (fixed, random spread, or animated drift).")]
        public ZUIValue positionY = new ZUIValue(0f);

        [Tooltip("Shape radius in pixels over life — a multicontrol (defaults to a grow-then-shrink envelope).")]
        public ZUIValue size = DefaultSize();

        [Tooltip("Colour vs normalised life 0..1.")]
        public Gradient colorOverLife = DefaultColor(LayerShape.Disc);
        [Tooltip("Alpha over life — a multicontrol (defaults to an envelope). The ONLY thing that fades a shape.")]
        public ZUIValue alpha = DefaultAlpha();

        [Tooltip("Optional radial alpha: an envelope over distance from the shape centre (0 = centre, 1 = edge) that " +
                 "multiplies alpha per pixel. null = off. Makes a shape soft-edged / hollow / haloed instead of a hard disc.")]
        public List<ZUIEnvelopePoint> radialAlpha = null;

        // ── opt-in modifiers (Pyre v2): geometry warps (skew/rotate/squash/wobble) + pixel effects
        // (tint/dissolve/…) composed on top of this layer. Empty = no clutter. Serialized polymorphically.
        [SerializeReference]
        public List<PyreModifier> modifiers = new();

        [Tooltip("Disc: border thickness. 1 = a full disc; lower values grow a ring inward from the edge; 0 = a " +
                 "1px border. Animatable — a Disc IS a ring/circle/sphere in one type.")]
        public ZUIValue thickness = new ZUIValue(1f);

        [Tooltip("Sprite: the sprite stamped as particles (its texture must be read/write enabled). Use the editor's " +
                 "'New sprite (Aseprite)' button to make + edit one.")]
        public Sprite particleSprite;
        [Tooltip("Sprite: degrees each particle spins over its life (each starts at a random angle). Animatable.")]
        public ZUIValue spriteSpin = new ZUIValue(0f);

        [Tooltip("SparkleField: fraction of pixels inside the circle that light up. Animatable — a rising envelope " +
                 "makes the sparkles ignite over the shape's life.")]
        public ZUIValue sparkleDensity = new ZUIValue(0.25f);

        [Tooltip("Crescent: X offset (px) of the mask disc that bites into the main disc.")]
        public ZUIValue crescentOffsetX = new ZUIValue(6f);
        [Tooltip("Crescent: Y offset (px) of the mask disc that bites into the main disc.")]
        public ZUIValue crescentOffsetY = new ZUIValue(0f);

        [Range(0f, 1f)]
        [Tooltip("Randomises each shape's start/end within the layer window so they don't all pop together.")]
        public float perShapeLifeJitter = 0.3f;

        // ── emission: Radial (default) or Directional (stream off a bendable origin line) ────────────
        [Tooltip("Radial = scatter around the centre and grow outward. Directional = start on an origin line and " +
                 "stream one way across the frame.")]
        public EmissionMode emission = EmissionMode.Radial;
        [Tooltip("Directional: centre of the origin line, in pixels from the canvas centre (e.g. y=-28 = bottom).")]
        public float originOffsetX = 0f;
        public float originOffsetY = 0f;
        [Tooltip("Directional: length of the origin line/surface in pixels.")]
        public float originLength = 40f;
        [Tooltip("Directional: bend of the origin line, 0 = straight, 1 = full circle (like a Choreographer spread). Animatable.")]
        public ZUIValue originBend = new ZUIValue(0f);
        [Tooltip("Directional: rotation of the origin line in degrees. Animatable — sweep the emitting surface.")]
        public ZUIValue originAngleDeg = new ZUIValue(0f);
        [Tooltip("Directional: travel direction offset from the line's outward normal, in degrees (animatable).")]
        public ZUIValue emitAngleDeg = new ZUIValue(0f);
        [Tooltip("Directional: how far a shape travels from its origin over life, in pixels (animatable).")]
        public ZUIValue travel = new ZUIValue(34f);
        [Tooltip("Directional: random per-shape spread of the travel direction, in degrees. Animatable — widen the fan over time.")]
        public ZUIValue emitSpreadDeg = new ZUIValue(8f);

        // ── Bars mode (LayerShape.Bars): a symmetric row of forward-growing bars streaming off an edge ──
        // Most are multicontrols evaluated over the layer's timeline, so the whole row can animate (sweep the
        // angle, widen the spacing, pulse the width…).
        [Tooltip("Bars: number of bars on EACH side of the centre bar (total = 2*barCount + 1). Animatable.")]
        public ZUIValue barCount = new ZUIValue(7f);
        [Tooltip("Bars: perpendicular spacing between neighbouring bars, in pixels. Animatable.")]
        public ZUIValue barSpacing = new ZUIValue(4f);
        [Tooltip("Bars: thickness of each bar (across the direction), in pixels. Animatable.")]
        public ZUIValue barWidth = new ZUIValue(3f);
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

        // ── wind: a directional drift added to EVERY shape, growing with the shape's age (any emission mode) ──
        [Tooltip("Wind drift X in pixels, applied × the shape's life so older particles drift further (animatable). " +
                 "Models e.g. a fast-moving object exploding — everything gets pushed one way over time.")]
        public ZUIValue windX = new ZUIValue(0f);
        [Tooltip("Wind drift Y in pixels, applied × the shape's life (animatable).")]
        public ZUIValue windY = new ZUIValue(0f);

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
                thickness = new ZUIValue(1f),
                sparkleDensity = new ZUIValue(0.25f),
                crescentOffsetX = new ZUIValue(6f),
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
            l.positionX = CloneVal(positionX);
            l.positionY = CloneVal(positionY);
            l.size = CloneVal(size);
            l.crescentOffsetX = CloneVal(crescentOffsetX);
            l.crescentOffsetY = CloneVal(crescentOffsetY);
            l.emitAngleDeg = CloneVal(emitAngleDeg);
            l.travel = CloneVal(travel);
            l.originBend = CloneVal(originBend);
            l.originAngleDeg = CloneVal(originAngleDeg);
            l.emitSpreadDeg = CloneVal(emitSpreadDeg);
            l.sparkleDensity = CloneVal(sparkleDensity);
            l.thickness = CloneVal(thickness);
            l.windX = CloneVal(windX);
            l.windY = CloneVal(windY);
            l.barCount = CloneVal(barCount);
            l.barSpacing = CloneVal(barSpacing);
            l.barWidth = CloneVal(barWidth);
            l.barForward = CloneVal(barForward);
            l.barBackwardFrac = CloneVal(barBackwardFrac);
            l.barAngleDeg = CloneVal(barAngleDeg);
            l.originInset = CloneVal(originInset);
            l.barTaper = CloneVal(barTaper);
            l.barStagger = CloneVal(barStagger);
            l.baseAngleDeg = CloneVal(baseAngleDeg);
            l.spreadDegrees = CloneVal(spreadDegrees);
            l.spriteSpin = CloneVal(spriteSpin);
            l.colorOverLife = CloneGradient(colorOverLife);
            l.alpha = CloneVal(alpha);
            l.radialAlpha = radialAlpha == null ? null
                : radialAlpha.ConvertAll(p => new ZUIEnvelopePoint(p.time, p.value, p.exponent, p.editState));
            l.modifiers = modifiers == null ? new List<PyreModifier>()
                : modifiers.ConvertAll(m => m?.Clone());
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

        /// A bar's forward reach over its life: shoot out fast, then pull back.
        public static ZUIValue DefaultBarForward() => CurveVal(48f, 0f, 2f, 0.4f, 40f, 1f, 8f);
    }
}
