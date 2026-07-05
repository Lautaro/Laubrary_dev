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

        [Tooltip("Ring: thickness of the annulus in pixels (drawn inward from the radius).")]
        public float ringThickness = 2f;

        [Range(0f, 1f)]
        [Tooltip("DissolvingDisc: where the growing hole sits, 0 = centred, 1 = pushed to the rim.")]
        public float dissolveCenter = 0f;
        [Tooltip("DissolvingDisc: keep a 1px outer border even after the middle has dissolved away.")]
        public bool dissolveKeepBorder = true;

        [Range(0f, 1f)]
        [Tooltip("SparkleField: fraction of pixels inside the circle that light up.")]
        public float sparkleDensity = 0.25f;

        [Tooltip("Crescent: X offset (px) of the mask disc that bites into the main disc.")]
        public ZUIValue crescentOffsetX = new ZUIValue(6f);
        [Tooltip("Crescent: Y offset (px) of the mask disc that bites into the main disc.")]
        public ZUIValue crescentOffsetY = new ZUIValue(0f);

        [Range(0f, 1f)]
        [Tooltip("Randomises each shape's start/end within the layer window so they don't all pop together.")]
        public float perShapeLifeJitter = 0.3f;

        [Range(0f, 1f)]
        [Tooltip("As life ends, deterministically drop up to this fraction of the shape's pixels (crumble away). " +
                 "The one dissolve effect a plain alpha curve can't express.")]
        public float disintegrate = 0f;

        // ── per-layer deform (composited under the global blast deform) ──────────
        [Tooltip("Enable this layer's own directional deform. When off, the block is identity (skipped).")]
        public bool deformEnabled = false;

        [Tooltip("Horizontal squash/stretch about the centre. 1 = none, <1 tall & thin, >1 wide & flat.")]
        public ZUIValue deformSquash = new ZUIValue(1f);
        [Tooltip("Horizontal shear based on height — leans this layer for a directional look.")]
        public ZUIValue deformSkew = new ZUIValue(0f);
        [Tooltip("Amplitude (px) of a vertical wobble that ripples this layer horizontally.")]
        public ZUIValue deformWobbleAmplitude = new ZUIValue(0f);
        [Tooltip("How many wobble ripples run up the canvas.")]
        public ZUIValue deformWobbleFrequency = new ZUIValue(1f);
        [Tooltip("Rotation (degrees) applied about the centre.")]
        public ZUIValue deformRotation = new ZUIValue(0f);

        // ── emission: Radial (default) or Directional (stream off a bendable origin line) ────────────
        [Tooltip("Radial = scatter around the centre and grow outward. Directional = start on an origin line and " +
                 "stream one way across the frame.")]
        public EmissionMode emission = EmissionMode.Radial;
        [Tooltip("Directional: centre of the origin line, in pixels from the canvas centre (e.g. y=-28 = bottom).")]
        public float originOffsetX = 0f;
        public float originOffsetY = 0f;
        [Tooltip("Directional: length of the origin line/surface in pixels.")]
        public float originLength = 40f;
        [Tooltip("Directional: bend of the origin line, 0 = straight, 1 = full circle (like a Choreographer spread).")]
        [Range(0f, 1f)] public float originBend = 0f;
        [Tooltip("Directional: rotation of the origin line in degrees.")]
        public float originAngleDeg = 0f;
        [Tooltip("Directional: travel direction offset from the line's outward normal, in degrees (animatable).")]
        public ZUIValue emitAngleDeg = new ZUIValue(0f);
        [Tooltip("Directional: how far a shape travels from its origin over life, in pixels (animatable).")]
        public ZUIValue travel = new ZUIValue(34f);
        [Tooltip("Directional: random per-shape spread of the travel direction, in degrees.")]
        public float emitSpreadDeg = 8f;

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
        [Tooltip("Bars: forward-length multiplier by distance from centre (0 = centre bar, 1 = outermost). The curve " +
                 "IS the blast silhouette — a falling curve makes a triangle/flame. Editable envelope.")]
        public List<ZUIEnvelopePoint> barLengthDist = DefaultBarLengthDist();
        [Range(0f, 0.5f)]
        [Tooltip("Bars: per-bar appearance delay as a fraction of the layer window (centre bar first, then outward).")]
        public float barStagger = 0.05f;
        [Tooltip("Bars: this layer's angle offset from the blast's base angle, in degrees. Animatable (sweep the row).")]
        public ZUIValue barAngleDeg = new ZUIValue(0f);
        [Tooltip("Bars: also draw a mirror of this layer's angle on the other side of the base angle.")]
        public bool barMirror = false;
        [Tooltip("Bars: push the origin this many pixels in from the surface edge, for a little breathing room. Animatable.")]
        public ZUIValue originInset = new ZUIValue(4f);

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
                ringThickness = 2f,
                dissolveCenter = 0f,
                dissolveKeepBorder = true,
                sparkleDensity = 0.25f,
                crescentOffsetX = new ZUIValue(6f),
                crescentOffsetY = new ZUIValue(0f),
                perShapeLifeJitter = 0.3f,
                disintegrate = 0f,
                colorOverLife = DefaultColor(shape),
                alpha = DefaultAlpha(),
            };

            switch (shape)
            {
                case LayerShape.Ring:
                    l.count = new ZUIValue(1f); l.spawnRadius = new ZUIValue(0f);
                    l.size = CurveVal(30f, 0f, 3f, 1f, 26f); l.ringThickness = 2f;
                    break;
                case LayerShape.DissolvingDisc:
                    l.count = new ZUIValue(5f); l.spawnRadius = new ZUIValue(0.24f);
                    l.size = CurveVal(20f, 0f, 8f, 1f, 16f);
                    break;
                case LayerShape.SparkleField:
                    l.count = new ZUIValue(1f); l.spawnRadius = new ZUIValue(0f);
                    l.size = CurveVal(26f, 0f, 10f, 1f, 22f); l.sparkleDensity = 0.12f;
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
            l.deformSquash = CloneVal(deformSquash);
            l.deformSkew = CloneVal(deformSkew);
            l.deformWobbleAmplitude = CloneVal(deformWobbleAmplitude);
            l.deformWobbleFrequency = CloneVal(deformWobbleFrequency);
            l.deformRotation = CloneVal(deformRotation);
            l.emitAngleDeg = CloneVal(emitAngleDeg);
            l.travel = CloneVal(travel);
            l.windX = CloneVal(windX);
            l.windY = CloneVal(windY);
            l.barCount = CloneVal(barCount);
            l.barSpacing = CloneVal(barSpacing);
            l.barWidth = CloneVal(barWidth);
            l.barForward = CloneVal(barForward);
            l.barBackwardFrac = CloneVal(barBackwardFrac);
            l.barAngleDeg = CloneVal(barAngleDeg);
            l.originInset = CloneVal(originInset);
            l.barLengthDist = barLengthDist == null ? DefaultBarLengthDist()
                : barLengthDist.ConvertAll(p => new ZUIEnvelopePoint(p.time, p.value, p.exponent, p.editState));
            l.colorOverLife = CloneGradient(colorOverLife);
            l.alpha = CloneVal(alpha);
            l.radialAlpha = radialAlpha == null ? null
                : radialAlpha.ConvertAll(p => new ZUIEnvelopePoint(p.time, p.value, p.exponent, p.editState));
            return l;
        }

        static ZUIValue CloneVal(ZUIValue s)
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

        static Gradient CloneGradient(Gradient g)
        {
            if (g == null) return null;
            var n = new Gradient();
            n.SetKeys(g.colorKeys, g.alphaKeys);
            n.mode = g.mode;
            return n;
        }

        /// A shape-appropriate default gradient (fire for most, smoke for dissolving discs).
        public static Gradient DefaultColor(LayerShape shape)
        {
            var g = new Gradient();
            switch (shape)
            {
                case LayerShape.DissolvingDisc:   // dark smoke
                    g.SetKeys(
                        new[]
                        {
                            new GradientColorKey(new Color(0.55f, 0.52f, 0.5f), 0f),
                            new GradientColorKey(new Color(0.28f, 0.26f, 0.26f), 0.5f),
                            new GradientColorKey(new Color(0.10f, 0.10f, 0.12f), 1f),
                        },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                    break;
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

        /// Grow-then-shrink radius envelope — a sensible default a shape's size animates through over its life.
        public static ZUIValue DefaultSize() => CurveVal(16f, 0f, 4f, 0.3f, 11f, 1f, 5f);

        /// Rise quickly, hold, then fade — a punchy explosion alpha envelope.
        public static ZUIValue DefaultAlpha() => CurveVal(1f, 0f, 0f, 0.15f, 1f, 0.7f, 1f, 1f, 0f);

        /// A bar's forward reach over its life: shoot out fast, then pull back.
        public static ZUIValue DefaultBarForward() => CurveVal(48f, 0f, 2f, 0.4f, 40f, 1f, 8f);

        /// Forward-length multiplier by distance from centre — a falling curve makes a triangle/flame silhouette.
        public static List<ZUIEnvelopePoint> DefaultBarLengthDist() => new List<ZUIEnvelopePoint>
        { new ZUIEnvelopePoint(0f, 1f), new ZUIEnvelopePoint(0.5f, 0.68f), new ZUIEnvelopePoint(1f, 0.12f) };
    }
}
