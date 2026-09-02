using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// A spray of tiny pixel particles thrown out of a sprite's own opaque footprint, tinted with that
    /// sprite's own pixel colours — "a splash of the character's own colours" with nothing else involved.
    ///
    /// Its source never silently no-ops: explicit sprite → whatever a Fragment Fracture in the same recipe is
    /// cutting → the burst's own supplied palette → plain white from the origin. The fracture tier is what
    /// lets an author point ONE field at a Zoe and get both the fracture and a splash of that Zoe's colours,
    /// with no second field to keep in sync.
    [System.Serializable]
    public class PaletteSplash : ChunkCapability
    {
        public override string KindName => "Palette Splash";

        [Tooltip("Which layer-stack slot the particles draw in. Empty leaves them out of the plan, " +
                 "drawing in stack order in FRONT of every slotted output.")]
        public string layerName = "";

        public override string LayerName => layerName;

        [Tooltip("Sprite to sample colours AND the emission footprint from. Empty uses whatever a Fragment " +
                 "Fracture in this recipe is cutting.")]
        public Sprite sprite;

        [Tooltip("Spawn each particle from a random opaque pixel of the source, instead of all from one point.")]
        public bool emitFromFootprint = true;

        [Tooltip("Fewest particles sprayed.")]
        [Min(0)] public int countMin = 6;
        [Tooltip("Most particles sprayed, inclusive.")]
        [Min(0)] public int countMax = 14;

        [Tooltip("Smallest particle, in pixels at the Pixels/Unit below.")]
        [Min(0.1f)] public float sizePxMin = 1f;
        [Tooltip("Largest particle, in pixels at the Pixels/Unit below.")]
        [Min(0.1f)] public float sizePxMax = 3f;

        [Tooltip("Pixels-per-unit the particle sizes above are measured in.")]
        [Min(1f)] public float pixelsPerUnit = 32f;

        [Tooltip("Slowest launch speed, world units/sec.")]
        public float speedMin = 1.5f;
        [Tooltip("Fastest launch speed, world units/sec.")]
        public float speedMax = 5f;

        [Tooltip("Aim the spray along the recipe's own direction instead of the angle set here — what lets a " +
                 "Follow Emitter throw the spray out behind a moving character.")]
        public bool inheritBurstDirection = false;

        [Tooltip("Centre of the spray cone in degrees. 0 = right, 90 = up.")]
        public float directionDeg = 90f;
        [Range(0f, 180f)]
        [Tooltip("Cone half-angle. 0 = a tight jet; 180 = a full circle.")]
        public float spreadDeg = 180f;

        [Tooltip("Downward acceleration, world units/sec².")]
        public float gravity = 6f;
        [Range(0f, 5f)]
        [Tooltip("Air resistance: per-second damping of velocity.")]
        public float drag = 0.4f;

        [Tooltip("Shortest particle lifetime, seconds.")]
        [Min(0.02f)] public float lifeMin = 0.15f;
        [Tooltip("Longest particle lifetime, seconds.")]
        [Min(0.02f)] public float lifeMax = 0.4f;

        [Tooltip("Opacity across a particle's life, left (spawn) to right (death).")]
        public AnimationCurve alphaOverLife = DefaultAlphaCurve();

        [Tooltip("Fixes every random pick so the spray is identical every play. 0 = reroll every time.")]
        public int seed = 0;

        public override float DurationSeconds(ChunkSpec spec) => Mathf.Max(0.02f, Mathf.Max(lifeMin, lifeMax));

        /// The sprite this splash actually samples: its own, else whatever a Fragment Fracture in the recipe
        /// is cutting. The fallback exists because the two describe the SAME event — a thing coming apart — so
        /// making the author set the same art twice bought nothing but a second place to keep in sync, and the
        /// failure was silent (a splash in the old character's colours beside fragments of the new one).
        public Sprite ResolveSprite(ChunkSpec spec)
        {
            if (sprite != null) return sprite;
            var fracture = spec != null ? spec.FirstEnabled<FragmentFracture>() : null;
            return fracture != null ? fracture.ResolveSource() : null;
        }

        static AnimationCurve DefaultAlphaCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f));

        // ── shape source: ONE opaque-pixel scan of the resolved sprite per fire (not per particle), since
        // every particle just needs a random index into it. ─────────────────────────────────────────────────
        struct PixelSample { public Vector2 offset; public Color32 color; }

        /// Every opaque pixel of source as a world-space offset from its pivot plus that pixel's own colour.
        /// False (with an empty list) for a null, unreadable or fully transparent source — the caller then
        /// falls further down its own chain rather than treating that as a hard failure.
        static bool TrySamplePixels(Sprite source, List<PixelSample> outPixels)
        {
            outPixels.Clear();
            if (source == null || source.texture == null || !source.texture.isReadable) return false;

            var rect = source.textureRect;
            int x = Mathf.FloorToInt(rect.x), y = Mathf.FloorToInt(rect.y);
            int w = Mathf.Max(1, Mathf.FloorToInt(rect.width)), h = Mathf.Max(1, Mathf.FloorToInt(rect.height));

            // GetPixels32 has no sub-rect overload, so the rect read goes through GetPixels.
            Color[] pixels;
            try { pixels = source.texture.GetPixels(x, y, w, h); }
            catch (UnityException) { return false; }   // not actually readable despite the flag (mid-import)

            float ppu = source.pixelsPerUnit > 0f ? source.pixelsPerUnit : 32f;
            Vector2 pivot = source.pivot;
            for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                Color32 c = pixels[py * w + px];
                if (c.a < 25) continue;   // ~10% alpha — skip the near-transparent fringe
                outPixels.Add(new PixelSample
                {
                    offset = (new Vector2(px + 0.5f, py + 0.5f) - pivot) / ppu,
                    color = c
                });
            }
            return outPixels.Count > 0;
        }

        public override void Fire(in ChunkModuleContext ctx)
        {
            var runner = ctx.Runner;
            if (runner == null) return;

            var rng = Rng(seed);

            // A proper waterfall: each tier is tried whenever the previous one failed for ANY reason (missing,
            // unreadable, fully transparent), so an authored-but-broken sprite still degrades through the
            // palette tier rather than jumping straight to white.
            var pixelScratch = new List<PixelSample>(64);
            var resolved = ResolveSprite(ctx.Spec);
            bool haveFootprint = resolved != null && TrySamplePixels(resolved, pixelScratch);
            IList<Color32> palette = null;
            if (!haveFootprint)
            {
                if (ctx.Palette != null && ctx.Palette.Count > 0) palette = ctx.Palette;
                else
                {
                    var debris = ctx.Spec != null ? ctx.Spec.FirstEnabled<DebrisScatter>() : null;
                    if (debris != null && debris.sampleSource != null)
                        haveFootprint = TrySamplePixels(debris.sampleSource, pixelScratch);
                }
            }

            bool useFootprintPositions = haveFootprint && emitFromFootprint;

            int count = Mathf.Max(0, rng.RangeInclusive(Mathf.Min(countMin, countMax), Mathf.Max(countMin, countMax)));
            float sizeLo = Mathf.Min(sizePxMin, sizePxMax), sizeHi = Mathf.Max(sizePxMin, sizePxMax);
            float speedLo = Mathf.Min(speedMin, speedMax), speedHi = Mathf.Max(speedMin, speedMax);
            float lifeLo = Mathf.Min(lifeMin, lifeMax), lifeHi = Mathf.Max(lifeMin, lifeMax);
            float ppu = Mathf.Max(1f, pixelsPerUnit);

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = Vector3.zero;
                Color32 color = new Color32(255, 255, 255, 255);
                if (haveFootprint)
                {
                    var sample = pixelScratch[rng.Next(pixelScratch.Count)];
                    color = sample.color;
                    if (useFootprintPositions) offset = sample.offset;
                }
                else if (palette != null)
                {
                    color = palette[rng.Next(palette.Count)];
                }
                // else: white, from the origin — the "never silently no-op" floor.
                color.a = 255;   // the particle's own alpha rides alphaOverLife, not the sampled pixel's

                // Every ChunkSprites shape is a shared, statically cached pixel sprite, so this allocates no
                // texture per particle and there is nothing to clean up on death. It also gives the random
                // shard shape for free instead of hand-rolling per-particle geometry.
                var shape = ChunkSprites.Get(rng.Next(ChunkSprites.Count), ppu);
                float targetPx = Mathf.Max(0.1f, rng.Range(sizeLo, sizeHi));
                float nativeShapePx = Mathf.Max(0.01f, shape.rect.width);

                var go = new GameObject("SplashParticle");
                var t = go.transform;
                t.position = ctx.Origin + offset;
                t.localScale = Vector3.one * (targetPx / nativeShapePx);
                t.SetParent(ctx.Container, true);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = shape;
                sr.color = color;
                ctx.ApplyOrder(sr, LayerName, i);

                float centreDeg = inheritBurstDirection ? ctx.DirectionDeg : directionDeg;
                float rad = (centreDeg + rng.Range(-spreadDeg * 0.5f, spreadDeg * 0.5f)) * Mathf.Deg2Rad;
                float speed = rng.Range(speedLo, speedHi);
                Vector3 velocity = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * speed;
                float life = rng.Range(lifeLo, lifeHi);

                // The runner drives POSITION only — the shared contract, so Chunks never bolts a component
                // onto anything it does not own. It deliberately never fades or destroys its targets (wrong
                // for the pooled blasts it also drives), so a particle we own outright gets its own rider.
                runner.Move(t, velocity, gravity, drag, life);
                go.AddComponent<ChunkSplashParticleFade>().Begin(sr, color, alphaOverLife, life);
            }
        }
    }

    /// The fade+despawn half of a splash particle's life, split from ChunkModuleRunner.Move on purpose: Move's
    /// job is externally-driven POSITION for transforms Chunks may not own (a pooled blast among them), so it
    /// never fades colour or destroys anything. A particle this capability spawned is fully our own, never
    /// pooled, never shared — so it is safe (and necessary, since nothing else does it) to ride its own alpha
    /// curve and self-destruct here rather than teaching the shared runner a lifecycle only one caller needs.
    [DisallowMultipleComponent]
    class ChunkSplashParticleFade : MonoBehaviour
    {
        SpriteRenderer sr;
        Color baseColor;
        AnimationCurve alphaOverLife;
        float life;
        float age;

        public void Begin(SpriteRenderer renderer, Color color, AnimationCurve curve, float lifeSeconds)
        {
            sr = renderer;
            baseColor = color;
            alphaOverLife = curve;
            life = Mathf.Max(0.001f, lifeSeconds);
            age = 0f;
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / life);
            if (sr != null)
            {
                float a = alphaOverLife != null ? alphaOverLife.Evaluate(t) : (1f - t);
                var c = baseColor; c.a *= Mathf.Clamp01(a);
                sr.color = c;
            }
            if (age >= life) Destroy(gameObject);
        }
    }
}
