using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// The authored recipe for one baked explosion: a canvas size, a frame count, a deterministic seed, a global
    /// directional deform, and a flat back-to-front stack of Layers. BlastRenderer turns this into identical
    /// pixels in the editor preview, the asset baker and the runtime player, so what you tune is exactly what
    /// ships. Layers are drawn in list order (index 0 is behind the rest).
    [CreateAssetMenu(menuName = "Laubrary/Pyre/Blast", fileName = "Blast")]
    public class BlastSpec : ScriptableObject
    {
        [Tooltip("Seeds all per-shape randomness. Same seed = byte-identical frames on every render.")]
        public int seed = 1234;

        [Min(1)]
        [Tooltip("Number of frames baked into the sheet / clip.")]
        public int frameCount = 16;

        [Min(4)]
        [Tooltip("Square canvas edge in pixels. Every frame is canvasSize x canvasSize.")]
        public int canvasSize = 64;

        [Min(1f)]
        [Tooltip("Pixels-per-unit used for the baked sprites and the runtime player.")]
        public float pixelsPerUnit = 64f;

        [Tooltip("Colour the canvas is cleared to before compositing. Usually fully transparent.")]
        public Color background = new Color(0f, 0f, 0f, 0f);

        [Tooltip("Back-to-front layer stack. First layer is drawn behind the rest.")]
        public List<Layer> layers = new();

        // ── global directional deform, applied while mapping pixels in BlastRenderer ──
        [Tooltip("Enable the whole-blast deform. When off, the block is identity (skipped).")]
        public bool deformEnabled = false;

        [Tooltip("Horizontal squash/stretch. 1 = none, <1 tall & thin, >1 wide & flat.")]
        public ZUIValue squash = new ZUIValue(1f);
        [Tooltip("Horizontal shear based on height — leans the blast for a directional look.")]
        public ZUIValue skew = new ZUIValue(0f);
        [Tooltip("Amplitude (px) of a vertical wobble that ripples the blast horizontally.")]
        public ZUIValue wobbleAmplitude = new ZUIValue(0f);
        [Tooltip("How many wobble ripples run up the canvas.")]
        public ZUIValue wobbleFrequency = new ZUIValue(1f);
        [Tooltip("Rotation (degrees) of the whole blast about the centre.")]
        public ZUIValue rotation = new ZUIValue(0f);

        /// Wipe the layers and build a punchy white-hot core -> orange/red fire -> dark smoke -> shockwave ring ->
        /// sparks explosion from scratch. Used by the editor's "New example blast" button and by the demo so there
        /// is always something good to look at. Fade in/out is owned entirely by each layer's alpha curve; grow /
        /// shrink by its animatable start/end size — no spawn/end modes any more.
        public void AddExampleContent()
        {
            seed = 1234;
            frameCount = 16;
            canvasSize = 64;
            pixelsPerUnit = 64f;
            background = new Color(0f, 0f, 0f, 0f);

            deformEnabled = false;
            squash = new ZUIValue(1f);
            skew = new ZUIValue(0f);
            wobbleAmplitude = new ZUIValue(0f);
            wobbleFrequency = new ZUIValue(1f);
            rotation = new ZUIValue(0f);

            layers = new List<Layer>();

            // ── back: dark smoke that starts a touch late and billows out big ──
            var smoke = Layer.Default(LayerShape.DissolvingDisc);
            smoke.name = "Smoke";
            smoke.startFrame = 3; smoke.endFrame = 15;
            smoke.count = new ZUIValue(6f);
            smoke.spawnRadius = new ZUIValue(0.34f);
            smoke.positionX = MinMax(-2f, 2f);
            smoke.positionY = MinMax(-1f, 3f);
            smoke.startSize = new ZUIValue(6f);
            smoke.endSize = new ZUIValue(20f);
            smoke.perShapeLifeJitter = 0.4f;
            smoke.alphaOverLife = SmokeAlpha();     // ease in, hold, slow fade
            layers.Add(smoke);

            // ── mid: the fire body — hot core falling through orange to red, crumbling as it dies ──
            var fire = Layer.Default(LayerShape.Disc);
            fire.name = "Fire body";
            fire.startFrame = 0; fire.endFrame = 12;
            fire.count = new ZUIValue(10f);
            fire.spawnRadius = new ZUIValue(0.30f);
            fire.positionX = MinMax(-2f, 2f);
            fire.positionY = MinMax(-2f, 2f);
            fire.startSize = MinMax(7f, 10f);       // varied blobs
            fire.endSize = new ZUIValue(5f);
            fire.disintegrate = 0.85f;              // break up near the end
            fire.perShapeLifeJitter = 0.3f;
            fire.alphaOverLife = Layer.DefaultAlpha();
            layers.Add(fire);

            // ── mid-front: a tight white-hot core that pops then shrinks away ──
            var core = Layer.Default(LayerShape.Disc);
            core.name = "Hot core";
            core.startFrame = 0; core.endFrame = 7;
            core.count = new ZUIValue(3f);
            core.spawnRadius = new ZUIValue(0.10f);
            core.positionX = MinMax(-1f, 1f);
            core.positionY = MinMax(-1f, 1f);
            core.startSize = new ZUIValue(8f);
            core.endSize = new ZUIValue(3f);
            core.colorOverLife = Layer.WhiteHotGradient();
            core.alphaOverLife = CoreAlpha();       // instant on, quick fade
            layers.Add(core);

            // ── front: a single expanding shockwave ring ──
            var ring = Layer.Default(LayerShape.Ring);
            ring.name = "Shockwave";
            ring.startFrame = 0; ring.endFrame = 9;
            ring.count = new ZUIValue(1f);
            ring.spawnRadius = new ZUIValue(0f);
            ring.startSize = new ZUIValue(3f);
            ring.endSize = new ZUIValue(28f);
            ring.ringThickness = 2f;
            ring.colorOverLife = Layer.WhiteHotGradient();
            ring.alphaOverLife = RingAlpha();       // bright then fades as it expands
            layers.Add(ring);

            // ── front: a scatter of hot sparks that spread outward ──
            var sparks = Layer.Default(LayerShape.SparkleField);
            sparks.name = "Sparks";
            sparks.startFrame = 1; sparks.endFrame = 14;
            sparks.count = new ZUIValue(1f);
            sparks.spawnRadius = new ZUIValue(0f);
            sparks.startSize = new ZUIValue(8f);
            sparks.endSize = new ZUIValue(24f);
            sparks.sparkleDensity = 0.10f;
            sparks.alphaOverLife = Layer.DefaultAlpha();
            layers.Add(sparks);
        }

        // A ZUIValue in Min-Max random mode with the given spread.
        static ZUIValue MinMax(float lo, float hi)
        {
            var v = new ZUIValue(0.5f * (lo + hi)) { mode = ZUIValue.Mode.MinMax, min = lo, max = hi };
            return v;
        }

        static AnimationCurve SmokeAlpha() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.25f, 0.9f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f));

        static AnimationCurve CoreAlpha() => new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.5f, 0.9f), new Keyframe(1f, 0f));

        static AnimationCurve RingAlpha() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.1f, 1f), new Keyframe(0.55f, 0.7f), new Keyframe(1f, 0f));
    }
}
