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

        /// Canvas width/height in pixels. Normally the authored size (height falls back to width when 0 = square);
        /// when any Bars layer has Star on, the canvas auto-fits its arms so nothing clips.
        public int Width  { get { if (AnyStar) { ComputeStarBox(out int w, out _); return w; } return Mathf.Max(1, canvasSize); } }
        public int Height { get { if (AnyStar) { ComputeStarBox(out _, out int h); return h; } return canvasHeight > 0 ? canvasHeight : Mathf.Max(1, canvasSize); } }

        bool AnyStar
        {
            get
            {
                if (layers == null) return false;
                foreach (var l in layers)
                    if (l != null && l.enabled && l.shape == LayerShape.Bars && l.star) return true;
                return false;
            }
        }

        // Fit the canvas to every star Bars layer's arms (each arm radiates outward from the shared centre). Sized
        // symmetric about the centre so the shared origin stays centred. No star layers → authored canvas.
        void ComputeStarBox(out int w, out int h)
        {
            float maxX = 0f, maxY = 0f;
            bool any = false;
            if (layers != null)
                foreach (var l in layers)
                {
                    if (l == null || !l.enabled || l.shape != LayerShape.Bars || !l.star) continue;
                    any = true;
                    float reach = MaxOf(l.barForward) + Mathf.Max(0f, MaxOf(l.originInset));
                    float combHalf = MaxOf(l.barCount) * MaxOf(l.barSpacing) + MaxOf(l.barWidth);
                    maxX = Mathf.Max(maxX, combHalf); maxY = Mathf.Max(maxY, combHalf);
                    int n = Mathf.Max(1, l.spreadCount);
                    bool tight = l.baseAngleDeg != null && l.spreadDegrees != null
                                 && l.baseAngleDeg.mode == ZUIValue.Mode.Static && l.spreadDegrees.mode == ZUIValue.Mode.Static;
                    if (!tight)
                    {
                        maxX = Mathf.Max(maxX, reach); maxY = Mathf.Max(maxY, reach);   // arms may sweep → fit a square
                    }
                    else
                    {
                        float baseDeg = l.baseAngleDeg.staticValue;
                        float step = n > 1 ? l.spreadDegrees.staticValue / n : 0f;
                        for (int i = 0; i < n; i++)
                        {
                            float a = (baseDeg + step * i) * Mathf.Deg2Rad;
                            maxX = Mathf.Max(maxX, Mathf.Abs(Mathf.Cos(a) * reach));
                            maxY = Mathf.Max(maxY, Mathf.Abs(Mathf.Sin(a) * reach));
                        }
                    }
                }
            if (!any) { w = Mathf.Max(1, canvasSize); h = canvasHeight > 0 ? canvasHeight : w; return; }
            const float margin = 3f;
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

            layers = new List<Layer>();

            // ── back: dark smoke that starts a touch late and billows out big ──
            var smoke = Layer.Default(LayerShape.Disc);
            smoke.name = "Smoke";
            smoke.colorOverLife = Layer.SmokeGradient();
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
            fire.perShapeLifeJitter = 0.3f;
            fire.alpha = Layer.DefaultAlpha();
            fire.modifiers.Add(new DissolveModifier { mode = DissolveMode.Erase });   // crumble away near the end
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

            // ── front: a single expanding shockwave ring (a thin-Thickness Disc) ──
            var ring = Layer.Default(LayerShape.Disc);
            ring.name = "Shockwave";
            ring.startFrame = 0; ring.endFrame = 9;
            ring.count = new ZUIValue(1f);
            ring.spawnRadius = new ZUIValue(0f);
            ring.size = Layer.CurveVal(30f, 0f, 3f, 1f, 28f);
            ring.thickness = new ZUIValue(0.08f);   // thin ring
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
            sparks.sparkleDensity = new ZUIValue(0.10f);
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
