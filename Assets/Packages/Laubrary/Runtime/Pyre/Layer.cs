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

        [Tooltip("Shape radius in pixels at the start of its life.")]
        public ZUIValue startSize = new ZUIValue(6f);
        [Tooltip("Shape radius in pixels at the end of its life (lerped from startSize over life).")]
        public ZUIValue endSize = new ZUIValue(10f);

        [Tooltip("Colour vs normalised life 0..1.")]
        public Gradient colorOverLife = DefaultColor(LayerShape.Disc);
        [Tooltip("Alpha vs normalised life 0..1. This is the ONLY thing that fades a shape in/out.")]
        public AnimationCurve alphaOverLife = DefaultAlpha();

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
                startSize = new ZUIValue(6f),
                endSize = new ZUIValue(10f),
                ringThickness = 2f,
                dissolveCenter = 0f,
                dissolveKeepBorder = true,
                sparkleDensity = 0.25f,
                crescentOffsetX = new ZUIValue(6f),
                crescentOffsetY = new ZUIValue(0f),
                perShapeLifeJitter = 0.3f,
                disintegrate = 0f,
                colorOverLife = DefaultColor(shape),
                alphaOverLife = DefaultAlpha(),
            };

            switch (shape)
            {
                case LayerShape.Ring:
                    l.count = new ZUIValue(1f); l.spawnRadius = new ZUIValue(0f);
                    l.startSize = new ZUIValue(3f); l.endSize = new ZUIValue(26f); l.ringThickness = 2f;
                    break;
                case LayerShape.DissolvingDisc:
                    l.count = new ZUIValue(5f); l.spawnRadius = new ZUIValue(0.24f);
                    l.startSize = new ZUIValue(8f); l.endSize = new ZUIValue(16f);
                    break;
                case LayerShape.SparkleField:
                    l.count = new ZUIValue(1f); l.spawnRadius = new ZUIValue(0f);
                    l.startSize = new ZUIValue(10f); l.endSize = new ZUIValue(22f); l.sparkleDensity = 0.12f;
                    break;
                case LayerShape.Crescent:
                    l.count = new ZUIValue(4f); l.spawnRadius = new ZUIValue(0.28f);
                    l.startSize = new ZUIValue(7f); l.endSize = new ZUIValue(11f);
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
            l.startSize = CloneVal(startSize);
            l.endSize = CloneVal(endSize);
            l.crescentOffsetX = CloneVal(crescentOffsetX);
            l.crescentOffsetY = CloneVal(crescentOffsetY);
            l.deformSquash = CloneVal(deformSquash);
            l.deformSkew = CloneVal(deformSkew);
            l.deformWobbleAmplitude = CloneVal(deformWobbleAmplitude);
            l.deformWobbleFrequency = CloneVal(deformWobbleFrequency);
            l.deformRotation = CloneVal(deformRotation);
            l.colorOverLife = CloneGradient(colorOverLife);
            l.alphaOverLife = alphaOverLife != null ? new AnimationCurve(alphaOverLife.keys) : DefaultAlpha();
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

        /// Rise quickly, hold, then fade — a punchy explosion envelope.
        public static AnimationCurve DefaultAlpha() => new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.15f, 1f),
            new Keyframe(0.7f, 1f),
            new Keyframe(1f, 0f));
    }
}
