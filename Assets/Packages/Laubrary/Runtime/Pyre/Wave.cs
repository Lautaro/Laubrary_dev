using UnityEngine;

namespace Laubrary.Pyre
{
    /// One timed burst of N identical shapes that share a life span (startFrame..endFrame) and animate
    /// their size, position, colour and alpha across that life. A blast is built from several Waves stacked
    /// into Layers. Every value here is authored in pixels/frames so the same numbers mean the same thing in
    /// the editor preview, the baker, and the runtime player (BlastRenderer reads this class in all three).
    [System.Serializable]
    public class Wave
    {
        [Tooltip("Label shown in the editor's wave list. Cosmetic only.")]
        public string name = "Wave";

        [Tooltip("First frame this wave's shapes are alive.")]
        public int startFrame = 0;
        [Tooltip("Last frame this wave's shapes are alive. Life is lerped 0..1 across [start, end].")]
        public int endFrame = 12;

        [Tooltip("Which primitive every shape in this wave draws.")]
        public WaveShape shape = WaveShape.Disc;

        [Min(1)]
        [Tooltip("How many shapes this wave scatters.")]
        public int count = 6;

        [Tooltip("Shapes are scattered within this radius of the blast centre, in pixels.")]
        public float spawnRadius = 6f;
        [Tooltip("Extra uniform random position offset applied per shape, in pixels.")]
        public float posJitter = 1f;

        [Tooltip("Shape radius in pixels at the start of its life.")]
        public float startSize = 6f;
        [Tooltip("Shape radius in pixels at the end of its life (lerped from startSize).")]
        public float endSize = 10f;

        [Tooltip("Colour vs normalised life 0..1.")]
        public Gradient colorOverLife = DefaultColor(WaveShape.Disc);
        [Tooltip("Alpha vs normalised life 0..1 (multiplied by the spawn/end envelope).")]
        public AnimationCurve alphaOverLife = DefaultAlpha();

        [Tooltip("How each shape appears at its start frame.")]
        public SpawnMode spawnMode = SpawnMode.FadeIn;
        [Tooltip("How each shape leaves at its end frame.")]
        public EndMode endMode = EndMode.FadeOut;

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

        [Tooltip("Crescent: offset (in pixels) of the mask disc that bites into the main disc.")]
        public Vector2 crescentOffset = new Vector2(6f, 0f);

        [Range(0f, 1f)]
        [Tooltip("Randomises each shape's start/end within the wave window so they don't all pop together.")]
        public float perShapeLifeJitter = 0.3f;

        /// A pleasing starting point per shape type; the editor adds waves through this.
        public static Wave Default(WaveShape shape)
        {
            var w = new Wave
            {
                name = shape.ToString(),
                shape = shape,
                startFrame = 0,
                endFrame = 12,
                count = 6,
                spawnRadius = 6f,
                posJitter = 1f,
                startSize = 6f,
                endSize = 10f,
                spawnMode = SpawnMode.FadeIn,
                endMode = EndMode.FadeOut,
                ringThickness = 2f,
                dissolveCenter = 0f,
                dissolveKeepBorder = true,
                sparkleDensity = 0.25f,
                crescentOffset = new Vector2(6f, 0f),
                perShapeLifeJitter = 0.3f,
                colorOverLife = DefaultColor(shape),
                alphaOverLife = DefaultAlpha(),
            };

            switch (shape)
            {
                case WaveShape.Ring:
                    w.count = 1; w.spawnRadius = 0f; w.posJitter = 0f;
                    w.startSize = 3f; w.endSize = 26f; w.ringThickness = 2f;
                    w.spawnMode = SpawnMode.Instant; w.endMode = EndMode.FadeOut;
                    break;
                case WaveShape.DissolvingDisc:
                    w.count = 5; w.spawnRadius = 5f; w.startSize = 8f; w.endSize = 16f;
                    w.spawnMode = SpawnMode.GrowIn; w.endMode = EndMode.FadeOut;
                    break;
                case WaveShape.SparkleField:
                    w.count = 1; w.spawnRadius = 0f; w.posJitter = 0f;
                    w.startSize = 10f; w.endSize = 22f; w.sparkleDensity = 0.12f;
                    w.spawnMode = SpawnMode.Instant; w.endMode = EndMode.FadeOut;
                    break;
                case WaveShape.Crescent:
                    w.count = 4; w.spawnRadius = 6f; w.startSize = 7f; w.endSize = 11f;
                    break;
            }
            return w;
        }

        /// A shape-appropriate default gradient (fire for most, smoke for dissolving discs).
        public static Gradient DefaultColor(WaveShape shape)
        {
            var g = new Gradient();
            switch (shape)
            {
                case WaveShape.DissolvingDisc:   // dark smoke
                    g.SetKeys(
                        new[]
                        {
                            new GradientColorKey(new Color(0.55f, 0.52f, 0.5f), 0f),
                            new GradientColorKey(new Color(0.28f, 0.26f, 0.26f), 0.5f),
                            new GradientColorKey(new Color(0.10f, 0.10f, 0.12f), 1f),
                        },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                    break;
                case WaveShape.SparkleField:     // hot sparks
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

        /// Rise quickly, hold, then fade — a punchy explosion envelope.
        public static AnimationCurve DefaultAlpha() => new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.15f, 1f),
            new Keyframe(0.7f, 1f),
            new Keyframe(1f, 0f));
    }
}
