using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Laubrary.PixelScale;

namespace Laubrary.Chunks
{
    /// A spray of tiny pixel particles thrown out of a sprite's own opaque footprint, tinted with that
    /// sprite's own pixel colours — "a splash of the character's own colours" with nothing else involved.
    ///
    /// Its source never silently no-ops: a live caller override (a Zoe's current on-screen frame, forwarded
    /// through ChunkModuleContext.SampleSourceOverride exactly as Debris Scatter and Fragment Fracture read it)
    /// → explicit animated source (Source) → explicit plain sprite → whatever a Fragment Fracture in the same
    /// recipe resolves for ITSELF → the burst's own supplied palette → plain white from the origin, all from
    /// one point. That last floor is deliberate, not an accident: a standalone burst with nothing authored at
    /// all (no Source, no Sprite, no sibling Fracture, no supplied palette) still sprays something rather than
    /// firing nothing, and it fails LOUDLY (visibly white, not a silently empty burst) rather than quietly. The
    /// fracture tier is what lets an author point ONE field at a Zoe and get both the fracture and a splash of
    /// that Zoe's colours, with no second field to keep in sync — and, since the live override is checked
    /// before either authored field, a Zoe-triggered burst gets the Zoe's LIVE frame even with both fields left
    /// empty, not the frame that happened to be authored on the recipe.
    [System.Serializable]
    public class PaletteSplash : ChunkCapability
    {
        public override string KindName => "Palette Splash";

        [Tooltip("Which layer-stack slot the particles draw in. Empty leaves them out of the plan, " +
                 "drawing in stack order in FRONT of every slotted output.")]
        public string layerName = "";

        public override string LayerName => layerName;

        [Tooltip("Animated content to sample colours AND the emission footprint from — a Zoe, a Pyre, anything " +
                 "that can hand over frames. Its FIRST frame is what gets sampled. Outranks the plain sprite " +
                 "below. When a Zoe triggers this burst, its live current sprite outranks both this and the " +
                 "fallback sprite below — this is what a standalone burst uses instead.")]
        public Object sourceVisual;

        [Tooltip("The plain sprite to sample colours AND the emission footprint from, when Source above is " +
                 "empty and no live sample source is supplied by the caller. Empty uses whatever a Fragment " +
                 "Fracture in this recipe is cutting; when a Zoe triggers this burst, its live current sprite " +
                 "outranks this too.")]
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

        [Tooltip("Use the project's Pixel Scale Project Settings (Laubrary/Pixel Scale Project Settings asset) for " +
                 "the particle sizes above, instead of the override below. Off = always use the override, " +
                 "whatever the project says — same Auto-pattern as Debris Scatter (T-0383) and Fragment " +
                 "Fracture (T-0390). This field previously had NO project-setting link at all and defaulted to " +
                 "32 while this project's own Pixel Scale is 16 — every existing splash (including UC4 Zoe " +
                 "Spray) was rendering its particles at half their intended size (T-0391).")]
        public bool useProjectPixelScale = true;
        [FormerlySerializedAs("pixelsPerUnit")]
        [Tooltip("Pixels-per-unit the particle sizes above are measured in, used only while 'Use project pixel " +
                 "scale' above is off.")]
        [Min(1f)] public float pixelsPerUnitOverride = 32f;

        /// The pixels-per-unit a splash's particles actually render at: the project's Pixel Scale setting by
        /// default, or the explicit override when that's turned off. Existing assets carry their old flat
        /// `pixelsPerUnit` value here under the renamed field (FormerlySerializedAs), but it is now DORMANT
        /// unless the author explicitly turns the toggle off — see this field's own tooltip for why that
        /// dormant value was wrong for every real splash in the project.
        public float EffectivePixelsPerUnit
            => useProjectPixelScale ? PixelScaleProjectSettings.Instance.pixelsPerUnit : pixelsPerUnitOverride;

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

        // Legacy migration source ONLY (T-0261 — no native CurveField anywhere; ZUI Envelope is the only
        // authored curve control). Never authored directly anymore — see alphaEnvelope below, which
        // MigrateLegacyCurves() converts this into on first load.
        [HideInInspector] public AnimationCurve alphaOverLife = DefaultAlphaCurve();
        [SerializeField, HideInInspector] bool curvesMigrated;

        // Legacy migration source ONLY (T-0373) — MigrateLegacyCurves() below (reused as this capability's
        // general one-time-upgrade hook, same guarded pattern as Trajectory's directionModeMigrated) halves
        // spreadDeg once for a splash saved before the runtime fix (commit 84973994) changed Spread's meaning
        // from a silently-halved half-angle to a true ± half-angle spray — Spread 180 used to read as an
        // upward half circle and now reads as a full circle, so an old recipe's on-screen cone would silently
        // double without this. Never read directly again. Guarded so it costs nothing once migrated, and
        // independent of curvesMigrated so either upgrade can land without waiting on the other. A brand-new
        // splash is born migrated (see MarkBornMigrated) and authors spreadDeg under the corrected meaning
        // directly, with no halving ever applied.
        [SerializeField, HideInInspector] bool spreadMigrated;

        [Tooltip("Opacity across a particle's life, left (spawn) to right (death).")]
        public List<ZUIEnvelopePoint> alphaEnvelope = new List<ZUIEnvelopePoint>();

        [Tooltip("Fixes every random pick so the spray is identical every play. 0 = reroll every time.")]
        public int seed = 0;

        public override bool MigrateLegacyCurves()
        {
            bool changed = false;
            if (!curvesMigrated)
            {
                alphaEnvelope = SampleCurveToEnvelope(alphaOverLife, 1f);
                curvesMigrated = true;
                changed = true;
            }
            if (!spreadMigrated)
            {
                // In-memory only — this never saves the asset itself (ChunkSpec.UpgradeIfNeeded just marks
                // NeedsSaving; the owner's own edit/save is what actually persists it), exactly like every
                // other capability's legacy-curve upgrade.
                spreadDeg *= 0.5f;
                spreadMigrated = true;
                changed = true;
            }
            return changed;
        }

        /// Called ONLY by the Add-menu factory (ChunkWindow.Recipe.cs) right after `new PaletteSplash()`, never
        /// by deserialization — same born-migrated pattern as <see cref="Trajectory.MarkBornMigrated"/>
        /// (T-0368/2). A capability built this way has no legacy pre-fix spread to convert, so this marks
        /// spreadMigrated up front: MigrateLegacyCurves() then never halves spreadDeg for it, and whatever the
        /// user authors on the card survives every domain reload under the corrected meaning. It deliberately
        /// does NOT touch curvesMigrated — a fresh splash still wants its default alphaOverLife converted into
        /// alphaEnvelope on first load, the same as before this change. An asset saved before spreadMigrated
        /// existed never runs through this method — it deserializes with the flag at the C# default (false)
        /// and still migrates exactly once, as before.
        public void MarkBornMigrated() => spreadMigrated = true;

        public override float DurationSeconds(ChunkSpec spec) => Mathf.Max(0.02f, Mathf.Max(lifeMin, lifeMax));

        /// The AUTHORED source only, animated first (its first usable frame) then the plain fallback sprite —
        /// the same two-tier shape as FragmentFracture.ResolveSource(). Falls THROUGH to the plain sprite when
        /// the animation yields no usable frame, so a half-authored source degrades to whatever art was already
        /// there rather than to nothing.
        Sprite ResolveAuthoredSprite()
        {
            if (sourceVisual != null && sourceVisual is IChunkAnimation animation)
            {
                var frames = animation.GetFrames();
                if (frames != null)
                    for (int i = 0; i < frames.Length; i++)
                        if (frames[i] != null) return frames[i];
            }
            return sprite;
        }

        /// The sprite this splash would sample with NO live burst to ask — its own authored source, else
        /// whatever a Fragment Fracture in the recipe resolves for itself. This is what the standalone editor
        /// preview (no <see cref="ChunkModuleContext"/> to read a live override from) uses; a real Fire uses
        /// <see cref="ResolveSprite(in ChunkModuleContext)"/> below instead. The fracture fallback exists
        /// because the two describe the SAME event — a thing coming apart — so making the author set the same
        /// art twice bought nothing but a second place to keep in sync, and the failure was silent (a splash in
        /// the old character's colours beside fragments of the new one).
        public Sprite ResolveSprite(ChunkSpec spec)
        {
            var authored = ResolveAuthoredSprite();
            if (authored != null) return authored;
            var fracture = spec != null ? spec.FirstEnabled<FragmentFracture>() : null;
            return fracture != null ? fracture.ResolveSource() : null;
        }

        /// The "sampled sprite for this burst" — the ONE method a live Fire (and any live-aware preview, e.g.
        /// T-0359) asks for the sprite this splash actually samples RIGHT NOW: the caller's live override when
        /// the burst has one (a Zoe's current on-screen frame, forwarded through
        /// <see cref="ChunkModuleContext.SampleSourceOverride"/> — same plumbing DebrisScatter's
        /// ResolvedSampleSource and FragmentFracture's ResolveSource(in ChunkModuleContext) use), else this
        /// capability's own authored source, else whatever a Fragment Fracture in the same recipe resolves for
        /// ITSELF (via its own ResolveSource(in ChunkModuleContext), so it too checks the override first) — so
        /// within one burst every sampling producer agrees on one frame: all use the live override when one
        /// exists, and a Splash following a Fracture uses the exact frame the Fracture cuts when neither has a
        /// live override.
        public Sprite ResolveSprite(in ChunkModuleContext ctx)
        {
            if (ctx.SampleSourceOverride != null) return ctx.SampleSourceOverride;
            var authored = ResolveAuthoredSprite();
            if (authored != null) return authored;
            var fracture = ctx.Spec != null ? ctx.Spec.FirstEnabled<FragmentFracture>() : null;
            return fracture != null ? fracture.ResolveSource(ctx) : null;
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
            var resolved = ResolveSprite(in ctx);
            bool haveFootprint = resolved != null && TrySamplePixels(resolved, pixelScratch);
            IList<Color32> palette = null;
            if (!haveFootprint)
            {
                if (ctx.Palette != null && ctx.Palette.Count > 0) palette = ctx.Palette;
                else
                {
                    // Same-frame-as-the-burst tier: a Debris Scatter sibling's OWN resolved sample source (its
                    // live override if any, else its authored one) — never its authored field read directly,
                    // so this floor agrees with every other producer in the burst about which frame is live.
                    var debris = ctx.Spec != null ? ctx.Spec.FirstEnabled<DebrisScatter>() : null;
                    var debrisSource = debris != null ? debris.ResolvedSampleSource(in ctx) : null;
                    if (debrisSource != null)
                        haveFootprint = TrySamplePixels(debrisSource, pixelScratch);
                }
            }

            bool useFootprintPositions = haveFootprint && emitFromFootprint;

            int count = Mathf.Max(0, rng.RangeInclusive(Mathf.Min(countMin, countMax), Mathf.Max(countMin, countMax)));
            float sizeLo = Mathf.Min(sizePxMin, sizePxMax), sizeHi = Mathf.Max(sizePxMin, sizePxMax);
            float speedLo = Mathf.Min(speedMin, speedMax), speedHi = Mathf.Max(speedMin, speedMax);
            float lifeLo = Mathf.Min(lifeMin, lifeMax), lifeHi = Mathf.Max(lifeMin, lifeMax);
            float ppu = Mathf.Max(1f, EffectivePixelsPerUnit);

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
                // texture per particle and there is nothing to clean up on death.
                //
                // The shape-pick draw stays in its ORIGINAL rng-stream position. ChunkPreviewSim.Splash draws
                // its own matching "shard shape pick" at this exact point, so a seeded splash stays
                // reproducible between the editor preview and a real Fire, and the bound swap below (Count →
                // int.MaxValue) is safe because ChunkRng.Next(int) costs exactly one NextUInt() step for ANY
                // bound ABOVE 1 — measured, not assumed. ⚠ That is NOT "any bound": Next(int) short-circuits
                // to 0 and consumes NOTHING when the bound is <= 1, so never swap a bound here for one that
                // could be 0 or 1 without re-checking the preview's matching draw, or the two silently desync.
                // The draw's result now feeds GetFitting instead of Get, so it
                // only ever picks AMONG shards that fit inside the target size rolled just after it — a
                // particle authored as "1-3 game pixels" must never end up built from a 3×3 shard squashed
                // down to 1px, which would render each of that shard's OWN texels at a third of a game pixel:
                // crisp confetti reading as smooth noise instead (T-0393), the actual cause behind "most
                // particles are still sub-pixel" surviving the PPU fix in T-0391.
                int shapePick = rng.Next(int.MaxValue);
                float targetPx = Mathf.Max(0.1f, rng.Range(sizeLo, sizeHi));
                var shape = ChunkSprites.GetFitting(targetPx, ppu, shapePick);
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

                // Same half-angle meaning as DebrisScatter/FragmentFracture: 0 = a tight jet, 180 = every
                // direction (the ± range totals 360 at the slider's max), not half of that — matches this
                // field's own tooltip and lets one card spray a full circle instead of needing a second one
                // aimed the other way (T-0363 J3/D6). The old ×0.5 here was the bug: it silently halved the
                // cone versus every other producer, so 180 read as a half-circle only on this card.
                float centreDeg = inheritBurstDirection ? ctx.DirectionDeg : directionDeg;
                float rad = (centreDeg + rng.Range(-spreadDeg, spreadDeg)) * Mathf.Deg2Rad;
                float speed = rng.Range(speedLo, speedHi);
                Vector3 velocity = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * speed;
                float life = rng.Range(lifeLo, lifeHi);

                // The runner drives POSITION only — the shared contract, so Chunks never bolts a component
                // onto anything it does not own. It deliberately never fades or destroys its targets (wrong
                // for the pooled blasts it also drives), so a particle we own outright gets its own rider.
                runner.Move(t, velocity, gravity, drag, life);
                go.AddComponent<ChunkSplashParticleFade>().Begin(sr, color, alphaEnvelope, life);
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
        List<ZUIEnvelopePoint> alphaEnvelope;
        float life;
        float age;

        public void Begin(SpriteRenderer renderer, Color color, List<ZUIEnvelopePoint> envelope, float lifeSeconds)
        {
            sr = renderer;
            baseColor = color;
            alphaEnvelope = envelope;
            life = Mathf.Max(0.001f, lifeSeconds);
            age = 0f;
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / life);
            if (sr != null)
            {
                float a = ZUIEnvelopeEvaluator.Evaluate(alphaEnvelope, t, 1f - t);
                var c = baseColor; c.a *= Mathf.Clamp01(a);
                sr.color = c;
            }
            if (age >= life) Destroy(gameObject);
        }
    }
}
