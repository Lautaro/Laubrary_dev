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
        [Tooltip("Canvas WIDTH in pixels.")]
        public int canvasSize = 64;

        [Min(0)]
        [Tooltip("Canvas HEIGHT in pixels. 0 = square (use the width). Bar/directional blasts want a rectangle.")]
        public int canvasHeight = 0;

        [Min(1f)]
        [Tooltip("Pixels-per-unit used for the baked sprites and the runtime player.")]
        public float pixelsPerUnit = 64f;

        // ── bar/directional composition (used by Bars-mode layers and the star spread) ──
        [Tooltip("Fundamental direction (deg) the blast grows toward. Bar layer angles are relative to this. Animatable.")]
        public ZUIValue baseAngleDeg = new ZUIValue(0f);
        [Tooltip("Star: duplicate the blast into `arms` copies that share the centre and radiate OUTWARD (an " +
                 "asterisk of bar-combs); the canvas auto-fits. Off = a single arm (bars stream off the back edge).")]
        public bool star = false;
        [Min(1)]
        [Tooltip("Star: how many arms radiate from the centre.")]
        public int spreadCount = 5;
        [Tooltip("Star: total arc (deg) the arms span. 360 = evenly around the full circle. Animatable.")]
        public ZUIValue spreadDegrees = new ZUIValue(360f);

        /// Canvas width/height in pixels. Normally the authored size (height falls back to width when 0 = square);
        /// when Star is on the canvas auto-fits the bar arms so nothing clips.
        public int Width  { get { if (star) { ComputeStarBox(out int w, out _); return w; } return Mathf.Max(1, canvasSize); } }
        public int Height { get { if (star) { ComputeStarBox(out _, out int h); return h; } return canvasHeight > 0 ? canvasHeight : Mathf.Max(1, canvasSize); } }

        // When Star is on the bar arms share the centre and reach outward, so the canvas must fit the arms in every
        // direction they point. Sized from the bar layers' max forward reach + the arm angles, symmetric about the
        // centre so the shared origin stays centred. No bar layers (or star off) → fall back to the authored canvas.
        void ComputeStarBox(out int w, out int h)
        {
            float reach = 0f, combHalf = 0f;
            if (layers != null)
                foreach (var l in layers)
                {
                    if (l == null || !l.enabled || l.shape != LayerShape.Bars) continue;
                    reach = Mathf.Max(reach, MaxOf(l.barForward) + Mathf.Max(0f, MaxOf(l.originInset)));
                    combHalf = Mathf.Max(combHalf, MaxOf(l.barCount) * MaxOf(l.barSpacing) + MaxOf(l.barWidth));
                }
            if (reach < 1f)   // nothing to fit — keep the authored canvas
            {
                w = Mathf.Max(1, canvasSize);
                h = canvasHeight > 0 ? canvasHeight : w;
                return;
            }
            int n = Mathf.Max(1, spreadCount);
            const float margin = 3f;
            // If the base angle or arc animate, the arms sweep, so fit a square that covers any rotation. Otherwise
            // fit a tight symmetric box to the static arm directions.
            bool tight = baseAngleDeg != null && spreadDegrees != null
                         && baseAngleDeg.mode == ZUIValue.Mode.Static && spreadDegrees.mode == ZUIValue.Mode.Static;
            if (!tight)
            {
                int s = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(reach, combHalf) + margin) * 2f), 4, 1024);
                w = s; h = s;
                return;
            }
            float baseDeg = baseAngleDeg.staticValue;
            float step = n > 1 ? spreadDegrees.staticValue / n : 0f;
            float maxX = combHalf, maxY = combHalf;   // the comb base spans ±combHalf around the shared origin
            for (int i = 0; i < n; i++)
            {
                float a = (baseDeg + step * i) * Mathf.Deg2Rad;
                maxX = Mathf.Max(maxX, Mathf.Abs(Mathf.Cos(a) * reach));
                maxY = Mathf.Max(maxY, Mathf.Abs(Mathf.Sin(a) * reach));
            }
            w = Mathf.Clamp(Mathf.CeilToInt((maxX + margin) * 2f), 4, 1024);
            h = Mathf.Clamp(Mathf.CeilToInt((maxY + margin) * 2f), 4, 1024);
        }

        // Upper bound of a ZUIValue (used only to size the star canvas): the constant, the max of a random range,
        // or the highest authored curve point.
        static float MaxOf(ZUIValue v)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static: return v.staticValue;
                case ZUIValue.Mode.MinMax: return Mathf.Max(v.min, v.max);
                case ZUIValue.Mode.Curve:
                    float m = 0f;
                    if (v.points != null) foreach (var p in v.points) m = Mathf.Max(m, p.value);
                    return m;
                default: return v.staticValue;
            }
        }

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

        // ── global colour grade (composes on top of each layer's own grade) ──
        [Tooltip("Global cross gradient — colours every bar/shape across itself and multiplies. White = no effect.")]
        public Gradient crossGradient = Layer.WhiteGradient();
        [Tooltip("Global cross-gradient strength (0 = off). Animatable.")]
        public ZUIValue crossAmount = new ZUIValue(0f);
        [Tooltip("Global contrast (1 = unchanged). Animatable.")]
        public ZUIValue contrast = new ZUIValue(1f);
        [Tooltip("Global brightness (1 = unchanged). Animatable.")]
        public ZUIValue brightness = new ZUIValue(1f);
        [Tooltip("Global saturation (1 = unchanged, 0 = greyscale). Animatable.")]
        public ZUIValue saturation = new ZUIValue(1f);

        // ── opt-in GLOBAL modifiers (Pyre v2): geometry warps + pixel effects applied to every layer. ──
        [SerializeReference]
        public List<PyreModifier> globalModifiers = new();

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
            smoke.size = Layer.CurveVal(22f, 0f, 6f, 1f, 20f);
            smoke.perShapeLifeJitter = 0.4f;
            smoke.alpha = SmokeAlpha();     // ease in, hold, slow fade
            layers.Add(smoke);

            // ── mid: the fire body — hot core falling through orange to red, crumbling as it dies ──
            var fire = Layer.Default(LayerShape.Disc);
            fire.name = "Fire body";
            fire.startFrame = 0; fire.endFrame = 12;
            fire.count = new ZUIValue(10f);
            fire.spawnRadius = new ZUIValue(0.30f);
            fire.positionX = MinMax(-2f, 2f);
            fire.positionY = MinMax(-2f, 2f);
            fire.size = Layer.CurveVal(12f, 0f, 9f, 0.3f, 10f, 1f, 5f);  // swell then crumble
            fire.disintegrate = 0.85f;              // break up near the end
            fire.perShapeLifeJitter = 0.3f;
            fire.alpha = Layer.DefaultAlpha();
            layers.Add(fire);

            // ── mid-front: a tight white-hot core that pops then shrinks away ──
            var core = Layer.Default(LayerShape.Disc);
            core.name = "Hot core";
            core.startFrame = 0; core.endFrame = 7;
            core.count = new ZUIValue(3f);
            core.spawnRadius = new ZUIValue(0.10f);
            core.positionX = MinMax(-1f, 1f);
            core.positionY = MinMax(-1f, 1f);
            core.size = Layer.CurveVal(10f, 0f, 8f, 1f, 3f);
            core.colorOverLife = Layer.WhiteHotGradient();
            core.alpha = CoreAlpha();       // instant on, quick fade
            layers.Add(core);

            // ── front: a single expanding shockwave ring ──
            var ring = Layer.Default(LayerShape.Ring);
            ring.name = "Shockwave";
            ring.startFrame = 0; ring.endFrame = 9;
            ring.count = new ZUIValue(1f);
            ring.spawnRadius = new ZUIValue(0f);
            ring.size = Layer.CurveVal(30f, 0f, 3f, 1f, 28f);
            ring.ringThickness = 2f;
            ring.colorOverLife = Layer.WhiteHotGradient();
            ring.alpha = RingAlpha();       // bright then fades as it expands
            layers.Add(ring);

            // ── front: a scatter of hot sparks that spread outward ──
            var sparks = Layer.Default(LayerShape.SparkleField);
            sparks.name = "Sparks";
            sparks.startFrame = 1; sparks.endFrame = 14;
            sparks.count = new ZUIValue(1f);
            sparks.spawnRadius = new ZUIValue(0f);
            sparks.size = Layer.CurveVal(26f, 0f, 8f, 1f, 24f);
            sparks.sparkleDensity = 0.10f;
            sparks.alpha = Layer.DefaultAlpha();
            layers.Add(sparks);
        }

        // A ZUIValue in Min-Max random mode with the given spread.
        static ZUIValue MinMax(float lo, float hi)
        {
            var v = new ZUIValue(0.5f * (lo + hi)) { mode = ZUIValue.Mode.MinMax, min = lo, max = hi };
            return v;
        }

        static ZUIValue SmokeAlpha() => Layer.CurveVal(1f, 0f, 0f, 0.25f, 0.9f, 0.7f, 0.8f, 1f, 0f);
        static ZUIValue CoreAlpha() => Layer.CurveVal(1f, 0f, 1f, 0.5f, 0.9f, 1f, 0f);
        static ZUIValue RingAlpha() => Layer.CurveVal(1f, 0f, 0f, 0.1f, 1f, 0.55f, 0.7f, 1f, 0f);
    }
}
