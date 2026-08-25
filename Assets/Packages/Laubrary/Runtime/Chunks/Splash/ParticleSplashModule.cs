using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// Standalone module #1 (CHUNKS_OVERHAUL_DESIGN.md "Standalone modules"): sprays a configurable number of
    /// tiny (1–3px, a little wider allowed) random-shape particles outward from a source sprite's own opaque
    /// footprint, tinted with that sprite's own pixel colours — "add a splash of the character's own colours"
    /// with no fragment, no pyre, no timeline involved. Must work with nothing else in the spec enabled, so its
    /// source resolution never silently no-ops: explicit sprite → whatever the Fragment Slicer is cutting →
    /// the burst's own supplied palette → the spec's Sampled Debris source sprite → plain white particles from
    /// the origin, in that order. The slicer tier is what lets an author point ONE field at a Zoe and get both
    /// the fracture and a splash of that Zoe's own colours, with no second field to keep in sync.
    [System.Serializable]
    public class ParticleSplashModule : IChunkModule
    {
        [Tooltip("Spray palette-sampled pixel particles out of the source sprite's own footprint.")]
        public bool enabled = false;

        [Tooltip("Which layer-stack slot the particles draw in. Ignored when no layer stack is configured.")]
        public string layerName = "Splash";

        [Tooltip("Sprite to sample colours AND the emission footprint from. Leave it EMPTY and the splash uses " +
                 "whatever the Fragment Slicer is cutting (its Source Visual's first frame, else its Sprite) — " +
                 "so a Zoe splashes its own colours with nothing to keep in sync. Set it only to splash " +
                 "something OTHER than what is being cut. Failing that: the burst's own supplied palette, then " +
                 "the Sampled Debris source sprite, then plain white particles from the origin.")]
        public Sprite sprite;

        [Tooltip("Spawn each particle from a random OPAQUE pixel of the source sprite (its own footprint) instead " +
                 "of a single point at the burst origin. Uses the same sprite resolved above, slicer fallback " +
                 "included. Forced off automatically when nothing readable resolves — then the splash still " +
                 "sprays, just all from the origin.")]
        public bool emitFromFootprint = true;

        [Tooltip("Fewest particles a splash sprays.")]
        [Min(0)] public int countMin = 6;
        [Tooltip("Most particles a splash sprays (inclusive). Each burst picks a random count in between.")]
        [Min(0)] public int countMax = 14;

        [Tooltip("Smallest particle size, in pixels at the spec's Pixels/Unit. 1–3px is the design intent; the " +
                 "range goes a little wider so a bigger splash stays reachable.")]
        [Min(0.1f)] public float sizePxMin = 1f;
        [Tooltip("Largest particle size, in pixels at the spec's Pixels/Unit.")]
        [Min(0.1f)] public float sizePxMax = 3f;

        [Tooltip("Slowest launch speed, world units/sec.")]
        public float speedMin = 1.5f;
        [Tooltip("Fastest launch speed, world units/sec.")]
        public float speedMax = 5f;

        [Tooltip("Aim the spray along the burst's own direction instead of the fixed angle below. This is what " +
                 "lets a Follow Emitter throw the spray backwards out of a moving character, since the burst " +
                 "direction it fires with is the reverse of that character's travel.")]
        public bool inheritBurstDirection = false;

        [Tooltip("Centre direction of the spray cone, in degrees. 0 = +X (right), 90 = +Y (up). Unused while " +
                 "the spray follows the burst's direction.")]
        public float directionDeg = 90f;
        [Range(0f, 180f)]
        [Tooltip("Cone half-angle around the direction. 0 = a tight jet; 180 = a full circle (radial spray).")]
        public float spreadDeg = 180f;

        [Tooltip("Downward acceleration, world units/sec².")]
        public float gravity = 6f;
        [Range(0f, 5f)]
        [Tooltip("Air resistance: per-second exponential damping of velocity. 0 = none, ~1 = noticeable, ~3 = soupy.")]
        public float drag = 0.4f;

        [Tooltip("Shortest particle lifetime, seconds.")]
        [Min(0.02f)] public float lifeMin = 0.15f;
        [Tooltip("Longest particle lifetime, seconds.")]
        [Min(0.02f)] public float lifeMax = 0.4f;

        [Tooltip("Opacity across a particle's life, left (spawn) to right (death).")]
        public AnimationCurve alphaOverLife = DefaultAlphaCurve();

        [Tooltip("Fixes every random pick (spawn pixel, size, speed, direction, life) so a splash resolves " +
                 "identically every time. 0 = reroll on every play — the right default for a one-shot splash.")]
        public int seed = 0;

        public bool Enabled => enabled;
        public string LayerName => layerName;

        /// The sprite this splash actually samples: its own, else whatever the Fragment Slicer is cutting.
        ///
        /// Why the fallback exists at all: the two modules describe the SAME event — a thing coming apart — so
        /// making the author set the same art twice bought nothing but a second place to keep in sync, and the
        /// failure mode was silent (a splash in the old character's colours next to fragments of the new one).
        /// The slicer owns that precedence in FragmentSlicerModule.ResolveSource(), so this asks it rather than
        /// re-deriving "animated source first, plain sprite second" and drifting from it.
        ///
        /// Standalone-safe by contract: no spec, no slicer, no sprite → null, and Fire's waterfall carries on to
        /// the palette / sample source / white tiers exactly as before. Written with explicit == checks instead
        /// of ?. because spec is a UnityEngine.Object and a destroyed one is fake-null, which ?. does not see.
        public Sprite ResolveSprite(ChunkSpec spec)
        {
            if (sprite != null) return sprite;
            if (spec == null || spec.fragmentSlicer == null) return null;
            return spec.fragmentSlicer.ResolveSource();
        }

        static AnimationCurve DefaultAlphaCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f));

        // ── shape source: a single opaque-pixel scan of the resolved sprite's texture, computed once per Fire
        // (not once per particle) since every particle just needs a random INDEX into it. ──────────────────────
        struct PixelSample { public Vector2 offset; public Color32 color; }

        /// Tries to read source's texture and returns every opaque pixel as a world-space offset (relative to
        /// the sprite's own pivot, ignoring the exploded object's own rotation/scale — ChunkModuleContext hands
        /// modules a world Origin only, not the source transform) + that pixel's own colour. False (with an
        /// empty list) on a null/unreadable/fully-transparent source — the caller falls further down the
        /// priority chain rather than treating that as a hard failure, per the "never silently no-op, but never
        /// throw on unreadable art" contract.
        static bool TrySamplePixels(Sprite source, List<PixelSample> outPixels)
        {
            outPixels.Clear();
            if (source == null || source.texture == null || !source.texture.isReadable) return false;

            var rect = source.textureRect;
            int x = Mathf.FloorToInt(rect.x), y = Mathf.FloorToInt(rect.y);
            int w = Mathf.Max(1, Mathf.FloorToInt(rect.width)), h = Mathf.Max(1, Mathf.FloorToInt(rect.height));

            // GetPixels32 has no sub-rect overload (only the whole-texture one), so the rect read goes through
            // GetPixels — the same call ChunkSpec.SpriteToTexture uses to crop a sprite out of an atlas page.
            Color[] pixels;
            try { pixels = source.texture.GetPixels(x, y, w, h); }
            catch (UnityException) { return false; } // not actually readable despite the flag (e.g. mid-import)

            float ppu = source.pixelsPerUnit > 0f ? source.pixelsPerUnit : 32f;
            Vector2 pivot = source.pivot; // pixels, relative to the sprite rect's own bottom-left — same convention SampledChunkSprites uses for its cutRect
            for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                Color32 c = pixels[py * w + px];
                if (c.a < 25) continue; // ~10% alpha threshold — skip near-transparent fringe pixels
                Vector2 offset = (new Vector2(px + 0.5f, py + 0.5f) - pivot) / ppu;
                outPixels.Add(new PixelSample { offset = offset, color = c });
            }
            return outPixels.Count > 0;
        }

        public void Fire(in ChunkModuleContext ctx)
        {
            var runner = ctx.Runner;
            if (runner == null) return; // defensive — ChunkModules.Run always supplies one

            // ── seeded RNG: 0 = reroll every play (UnityEngine.Random, matches every other Chunks module's own
            // convention — see SpawnFormation.seed); non-zero = a deterministic System.Random so an authored
            // splash hits the same marks twice. ──────────────────────────────────────────────────────────────
            System.Random rng = seed != 0 ? new System.Random(seed) : null;
            float RandRange(float min, float max) => rng != null ? (float)(rng.NextDouble() * (max - min) + min) : Random.Range(min, max);
            int RandRangeInclusive(int min, int max) => rng != null ? rng.Next(min, max + 1) : Random.Range(min, max + 1);

            // ── source resolution — explicit sprite → the Fragment Slicer's source → caller's palette → the
            // spec's own Sampled Debris source
            // → white-from-origin. A standalone module must never silently do nothing, so the chain always
            // bottoms out in a usable (if plain) result. ────────────────────────────────────────────────────
            var pixelScratch = new List<PixelSample>(64);
            // A proper waterfall: each tier is tried whenever the PREVIOUS one failed for ANY reason (missing,
            // unreadable texture, or fully transparent) — not just "wasn't set" — so an authored-but-broken
            // explicit sprite still degrades to the palette/sampleSource/white tiers below it rather than
            // jumping straight to white, per the "unreadable source degrades, never throws" contract.
            // Tier 1 is now "the sprite this splash resolves to" rather than "the sprite field" — an unset field
            // means "same art the Fragment Slicer is cutting", which is the answer the author wanted in the
            // overwhelmingly common case of one thing coming apart. See ResolveSprite.
            var resolved = ResolveSprite(ctx.Spec);
            bool haveFootprint = resolved != null && TrySamplePixels(resolved, pixelScratch);
            IList<Color32> palette = null;
            if (!haveFootprint)
            {
                if (ctx.Palette != null && ctx.Palette.Count > 0)
                    palette = ctx.Palette;
                else if (ctx.Spec != null && ctx.Spec.sampleSource != null)
                    haveFootprint = TrySamplePixels(ctx.Spec.sampleSource, pixelScratch);
            }
            // emitFromFootprint=false is an explicit opt-out: keep the resolved sprite's colours (if any) but
            // spawn every particle at the origin instead of scattering across the footprint.
            bool useFootprintPositions = haveFootprint && emitFromFootprint;

            int count = Mathf.Max(0, RandRangeInclusive(Mathf.Min(countMin, countMax), Mathf.Max(countMin, countMax)));
            float sizeLo = Mathf.Min(sizePxMin, sizePxMax), sizeHi = Mathf.Max(sizePxMin, sizePxMax);
            float speedLo = Mathf.Min(speedMin, speedMax), speedHi = Mathf.Max(speedMin, speedMax);
            float lifeLo = Mathf.Min(lifeMin, lifeMax), lifeHi = Mathf.Max(lifeMin, lifeMax);
            float ppu = ctx.Spec != null && ctx.Spec.pixelsPerUnit > 0f ? ctx.Spec.pixelsPerUnit : 32f;

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = Vector3.zero;
                Color32 color = new Color32(255, 255, 255, 255);
                if (haveFootprint)
                {
                    var sample = pixelScratch[rng != null ? rng.Next(pixelScratch.Count) : Random.Range(0, pixelScratch.Count)];
                    color = sample.color;
                    if (useFootprintPositions) offset = sample.offset;
                }
                else if (palette != null)
                {
                    color = palette[rng != null ? rng.Next(palette.Count) : Random.Range(0, palette.Count)];
                }
                // else: white, from the origin — the "never silently no-op" floor.
                color.a = 255; // the particle's OWN alpha is driven by alphaOverLife below, not the sampled pixel's

                // Every ChunkSprites shape is a shared, statically-cached pixel sprite (built once, reused
                // forever, never destroyed) — reusing it here means this module allocates ZERO new
                // Texture2D/Sprite per particle per burst, so there is nothing to mark HideAndDontSave or clean
                // up on death. It also gives the "random shape" requirement for free (six pre-built shard
                // shapes) instead of hand-rolling per-particle procedural geometry.
                var shape = ChunkSprites.Random(ppu);
                float targetPx = Mathf.Max(0.1f, RandRange(sizeLo, sizeHi));
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

                // ctx.DirectionDeg is already resolved by the emitter (a per-burst override, else the spec's
                // own), and is never NaN by the time a module sees it — so inheriting needs no fallback here.
                float centreDeg = inheritBurstDirection ? ctx.DirectionDeg : directionDeg;
                float dirDeg = centreDeg + RandRange(-spreadDeg * 0.5f, spreadDeg * 0.5f);
                float rad = dirDeg * Mathf.Deg2Rad;
                float speed = RandRange(speedLo, speedHi);
                Vector3 velocity = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * speed;
                float life = RandRange(lifeLo, lifeHi);

                // Runner drives POSITION (velocity/gravity/drag) — the shared contract every module uses so
                // Chunks never bolts a component onto anything it doesn't own. It does not fade or destroy the
                // target on its own (that would be wrong for the Pyre-blast callers it also drives, since a
                // pool reclaims those itself), so a plain particle we spawned and own outright — never pooled,
                // never shared — gets its own tiny fade+despawn rider below.
                runner.Move(t, velocity, gravity, drag, life);
                go.AddComponent<ChunkSplashParticleFade>().Begin(sr, color, alphaOverLife, life);
            }
        }
    }

    /// The fade+despawn half of a splash particle's life, split from ChunkModuleRunner.Move on purpose: Move's
    /// job is externally-driven POSITION for transforms Chunks may not own (a pooled Pyre blast among them), so
    /// it deliberately never fades colour or destroys anything. A particle THIS module spawned is fully our
    /// own, never pooled, never shared — so it is safe (and necessary, since nothing else does it) to ride its
    /// own alpha curve and self-destruct here instead of teaching the shared runner about a lifecycle only one
    /// caller needs.
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
