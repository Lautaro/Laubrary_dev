using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// The authored recipe for one baked explosion: a canvas size, a frame count, a deterministic seed, and a
    /// back-to-front stack of Layers (each a stack of Waves). BlastRenderer turns this into identical pixels in
    /// the editor preview, the asset baker and the runtime player, so what you tune is exactly what ships.
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
        [Range(0.3f, 3f)]
        [Tooltip("Horizontal squash/stretch. 1 = none, <1 tall & thin, >1 wide & flat.")]
        public float squash = 1f;

        [Range(-2f, 2f)]
        [Tooltip("Horizontal shear based on height — leans the blast for a directional look.")]
        public float skew = 0f;

        [Tooltip("Amplitude (px) of a vertical wobble that ripples the blast horizontally.")]
        public float wobbleAmplitude = 0f;
        [Tooltip("How many wobble ripples run up the canvas.")]
        public float wobbleFrequency = 1f;

        /// Wipe the layers and build a punchy fire + smoke + shockwave + sparks explosion from scratch. Used by
        /// the editor's "New example blast" button and by the demo so there is always something good to look at.
        public void AddExampleContent()
        {
            seed = 1234;
            frameCount = 16;
            canvasSize = 64;
            pixelsPerUnit = 64f;
            background = new Color(0f, 0f, 0f, 0f);
            squash = 1f; skew = 0f; wobbleAmplitude = 0f; wobbleFrequency = 1f;

            layers = new List<Layer>();

            // ── back: dark smoke that starts a touch late and billows out big ──
            var smoke = new Layer { name = "Smoke" };
            var smokeWave = Wave.Default(WaveShape.DissolvingDisc);
            smokeWave.name = "Smoke plumes";
            smokeWave.startFrame = 3; smokeWave.endFrame = 15;
            smokeWave.count = 6; smokeWave.spawnRadius = 8f; smokeWave.posJitter = 2f;
            smokeWave.startSize = 6f; smokeWave.endSize = 20f;
            smokeWave.spawnMode = SpawnMode.GrowIn; smokeWave.endMode = EndMode.FadeOut;
            smokeWave.perShapeLifeJitter = 0.4f;
            smoke.waves.Add(smokeWave);

            // ── mid: the fire body — bright core falling through orange to red, breaking up as it dies ──
            var fire = new Layer { name = "Fire" };
            var fireBody = Wave.Default(WaveShape.Disc);
            fireBody.name = "Fire body";
            fireBody.startFrame = 0; fireBody.endFrame = 12;
            fireBody.count = 10; fireBody.spawnRadius = 7f; fireBody.posJitter = 2f;
            fireBody.startSize = 9f; fireBody.endSize = 5f;
            fireBody.spawnMode = SpawnMode.Instant; fireBody.endMode = EndMode.Disintegrate;
            fireBody.perShapeLifeJitter = 0.3f;
            fire.waves.Add(fireBody);

            var core = Wave.Default(WaveShape.Disc);
            core.name = "Hot core";
            core.startFrame = 0; core.endFrame = 7;
            core.count = 3; core.spawnRadius = 3f; core.posJitter = 1f;
            core.startSize = 8f; core.endSize = 3f;
            core.spawnMode = SpawnMode.Instant; core.endMode = EndMode.FadeOut;
            core.colorOverLife = WhiteHotGradient();
            fire.waves.Add(core);

            // ── front: a single expanding shockwave ring + a scatter of hot sparks ──
            var front = new Layer { name = "Shock + Sparks" };

            var ring = Wave.Default(WaveShape.Ring);
            ring.name = "Shockwave";
            ring.startFrame = 0; ring.endFrame = 9;
            ring.count = 1; ring.spawnRadius = 0f; ring.posJitter = 0f;
            ring.startSize = 3f; ring.endSize = 28f; ring.ringThickness = 2f;
            ring.spawnMode = SpawnMode.Instant; ring.endMode = EndMode.FadeOut;
            ring.colorOverLife = WhiteHotGradient();
            front.waves.Add(ring);

            var sparks = Wave.Default(WaveShape.SparkleField);
            sparks.name = "Sparks";
            sparks.startFrame = 1; sparks.endFrame = 14;
            sparks.count = 1; sparks.spawnRadius = 0f; sparks.posJitter = 0f;
            sparks.startSize = 8f; sparks.endSize = 24f; sparks.sparkleDensity = 0.10f;
            sparks.spawnMode = SpawnMode.Instant; sparks.endMode = EndMode.FadeOut;
            front.waves.Add(sparks);

            layers.Add(smoke);
            layers.Add(fire);
            layers.Add(front);
        }

        static Gradient WhiteHotGradient()
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
    }
}
