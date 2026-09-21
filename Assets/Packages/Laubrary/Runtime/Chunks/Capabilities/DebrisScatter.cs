using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Laubrary.SpriteFx;
using Laubrary.PixelScale;

namespace Laubrary.Chunks
{
    /// What a debris chunk actually LOOKS like. One choice, and only that choice's own dials are authored —
    /// the four used to be four independent field groups that silently outranked each other in a fixed order,
    /// so an author who set a sprite list and a sample source could not tell which one would win.
    public enum DebrisVisual
    {
        /// A procedural pixel-square built at runtime and tinted. Ships no assets, which is the point.
        Squares = 0,
        /// One of an authored pool of sprites, drawn per chunk.
        Sprites = 1,
        /// Small pieces cut out of a source sprite, optionally tumbled with a squash+shade pseudo-3D trick.
        Sampled = 2,
        /// Animated content every chunk plays — a Pyre, a Zoe, or a wrapper that overrides speed/looping.
        Animated = 3,
    }

    /// The shrapnel a burst throws: a swarm of small pieces launched into a cone, arcing under gravity and
    /// drag, spinning, fading and optionally bouncing off a floor. This is what a Chunk Spec has always been,
    /// now stated as one capability among others rather than as the recipe itself — so a recipe can hold two
    /// of them (heavy slabs and fine grit, thrown on different beats) or none at all.
    [System.Serializable]
    public class DebrisScatter : ChunkCapability
    {
        public override string KindName => "Debris Scatter";

        [Tooltip("Which layer-stack slot the debris draws in. Empty leaves it out of the plan, drawing " +
                 "in stack order in FRONT of every slotted output.")]
        public string layerName = "";

        public override string LayerName => layerName;

        // ── what a chunk looks like ───────────────────────────────────────────────
        [Tooltip("What each chunk is made of. Only the chosen kind's own dials are authored.")]
        public DebrisVisual visual = DebrisVisual.Squares;

        [Tooltip("Chunk sprites to pick from at random.")]
        public List<Sprite> sprites = new List<Sprite>();

        [Tooltip("Use the project's Pixel Scale Project Settings (Laubrary/Pixel Scale Project Settings asset) for " +
                 "the pixels-per-unit below, instead of the explicit override — so debris is blocky at the " +
                 "project's own pixel density by default. Off = always use the override, whatever the project says.")]
        public bool useProjectPixelScale = true;
        [FormerlySerializedAs("pixelsPerUnit")]
        [Tooltip("Pixels-per-unit for the procedural pixel-square and the sampled-chunk texel density, used only " +
                 "while 'Use project pixel scale' above is off.")]
        [Min(1f)] public float pixelsPerUnitOverride = 32f;

        /// The pixels-per-unit this scatter actually fires with: the project's Pixel Scale setting by default,
        /// or the explicit override when that's turned off — same override-toggle shape as PixelScaleCamera's
        /// own overrideProjectSettings/overridePixelsPerUnit pair, so authors already know this control.
        public float EffectivePixelsPerUnit
            => useProjectPixelScale ? PixelScaleProjectSettings.Instance.pixelsPerUnit : pixelsPerUnitOverride;

        [Tooltip("The sprite small chunks are cut out of, when no live sample source is supplied by the caller " +
                 "(e.g. a Zoe's current sprite, forwarded automatically by a Zoe-attached Spawn Chunks effect) — " +
                 "so the debris is made of the exploding object's own pixels. Its texture must have Read/Write " +
                 "Enabled. Ignored while a live sample source is supplied; this is what a standalone burst with " +
                 "no Zoe context uses.")]
        public Sprite sampleSource;
        [Tooltip("Smallest sampled chunk, in source-texture pixels. An UPPER BOUND on detail, not an absolute " +
                 "size: a cut is never given more source pixels than the Size dial leaves room to show them " +
                 "in, so widen Size to get bigger, more detailed pieces.")]
        [Min(1)] public int samplePxMin = 5;
        [Tooltip("Largest sampled chunk, in source-texture pixels. An UPPER BOUND on detail, not an absolute " +
                 "size: a cut is never given more source pixels than the Size dial leaves room to show them " +
                 "in, so widen Size to get bigger, more detailed pieces.")]
        [Min(1)] public int samplePxMax = 20;
        [Tooltip("Turn each sampled piece with a squash+shade trick that reads as a lit 3D fragment, instead " +
                 "of a flat 2D spin.")]
        public bool tumble = true;
        [Tooltip("Slowest tumble rate, degrees/sec.")]
        public float tumbleSpeedMin = 180f;
        [Tooltip("Fastest tumble rate, degrees/sec.")]
        public float tumbleSpeedMax = 720f;
        [Range(0f, 1f)]
        [Tooltip("How strong the light/dark swing is as a piece turns. 0 = squash only, 1 = full swing.")]
        public float tumbleShadeStrength = 0.6f;

        [Tooltip("Recolour sampled debris as it is cut: none, every opaque pixel, just the rim, or everywhere " +
                 "except the rim.")]
        public ChunkTintMode tintMode = ChunkTintMode.None;
        [Tooltip("The colour the cut pixels are pulled towards.")]
        public Color tintColor = new Color(1f, 0.35f, 0.08f, 1f);
        [Range(0f, 1f)]
        [Tooltip("How far the tint pulls the source pixel. 0 = no visible effect, 1 = fully replaced.")]
        public float tintStrength = 0.6f;
        [Min(1)]
        [Tooltip("Edge modes only: how many pixels in from the rim count as edge.")]
        public int edgeThicknessPx = 1;

        [Tooltip("Pixel modifiers baked once into each sampled chunk at spawn, in order — a still pass, not " +
                 "animated over the chunk's life.")]
        [SerializeReference] public List<PixelModifier> modifiers = new List<PixelModifier>();

        [Tooltip("Animated content every chunk plays instead of a static sprite — a Pyre, a Zoe, or a wrapper " +
                 "that overrides its speed or looping.")]
        public Object animationSource;

        /// animationSource cast to the contract Chunks actually needs, or null if unset/incompatible.
        public IChunkAnimation AnimationSource => animationSource as IChunkAnimation;

        /// Whether chunks are cut out of a source sprite rather than drawn from art or built procedurally, going
        /// only by this capability's OWN authored sampleSource — the shape the standalone editor preview reads,
        /// unaware of any live caller-supplied override. <see cref="Fire"/> is where a live burst additionally
        /// considers <see cref="ChunkModuleContext.SampleSourceOverride"/>, which can make Sampled-mode cutting
        /// happen even with this false (an authored-empty sampleSource, filled in live by a Zoe at burst time).
        public bool UsesSampledDebris => visual == DebrisVisual.Sampled && sampleSource != null;

        /// The sprite this scatter would actually cut pieces from RIGHT NOW: the caller's live override when the
        /// burst has one, else this capability's own authored sampleSource — the ONE place that priority is
        /// decided, so a live Zoe frame always outranks whatever is separately authored on the recipe.
        public Sprite ResolvedSampleSource(in ChunkModuleContext ctx)
            => ctx.SampleSourceOverride != null ? ctx.SampleSourceOverride : sampleSource;

        /// The sample-crop size range this scatter may actually cut for a chunk that will be DISPLAYED at
        /// <paramref name="worldSize"/> world units: the authored samplePxMin/samplePxMax, each capped so a cut
        /// can never contain more source texels than the chunk has room to show them in.
        ///
        /// ⚠ This is the same rule <see cref="ChunkSprites.GetFitting"/> applies to procedural shards (T-0393),
        /// stated for real crops (T-0397). A crop of S texels built at <paramref name="ppu"/> is S/ppu world
        /// units across; displaying it at worldSize means scaling by worldSize·ppu/S, so S &gt; worldSize·ppu is
        /// a DOWNSCALE below the crop's own texel grid — every one of its texels rendering as a fraction of a
        /// game pixel. That is not a small speck; it is a detailed image compressed far below the resolution it
        /// was captured at, which reads as blur and noise rather than pixel art. Capping S at ⌊worldSize·ppu⌋
        /// makes the scale ≥ 1 by construction, so a cut is only ever scaled UP or left 1:1.
        ///
        /// Knock-on effect, by design and matching GetFitting's: a small Size range narrows the detail a cut can
        /// carry. The Floating Disc hit recipe's authored 5–20 becomes 2–5 at its authored 0.16–0.35 size. The
        /// lever for big detailed pieces is the SIZE range — the crop follows it automatically. Both dials stay
        /// meaningful: Size is how big on screen, Sample px is how much detail, bounded by the first.
        ///
        /// Always at least 1×1, so a target below one pixel still yields a real (single-texel) cut rather than
        /// nothing — the one case a further downscale is unavoidable, and harmless, since one texel has no
        /// internal grid left to subdivide.
        public void FitSampleRange(float worldSize, float ppu, out int minPx, out int maxPx)
        {
            int fit = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(0f, worldSize) * Mathf.Max(1f, ppu)));
            int authoredLo = Mathf.Max(1, Mathf.Min(samplePxMin, samplePxMax));
            int authoredHi = Mathf.Max(1, Mathf.Max(samplePxMin, samplePxMax));
            maxPx = Mathf.Min(authoredHi, fit);
            minPx = Mathf.Min(authoredLo, maxPx);
        }

        // ── emission ──────────────────────────────────────────────────────────────
        [Tooltip("Fewest chunks thrown.")]
        [Min(0)] public int countMin = 8;
        [Tooltip("Most chunks thrown, inclusive.")]
        [Min(0)] public int countMax = 16;

        [Tooltip("Slowest launch speed, world units/sec.")]
        public float speedMin = 3f;
        [Tooltip("Fastest launch speed, world units/sec.")]
        public float speedMax = 7f;

        // The cone's CENTRE is the recipe's own burst direction (or the one a caller passed for this burst),
        // never a dial of its own: aiming a burst is a composition-level act — every producer in the recipe
        // has to turn together, or "throw the debris that way" silently means "that way, plus wherever the
        // splash happens to point". Only the width of the cone is this capability's own business.
        [Range(0f, 180f)]
        [Tooltip("Cone half-angle around the direction. 0 = a tight jet; 180 = a full circle.")]
        public float spreadDeg = 180f;
        [Tooltip("Extra upward velocity on every chunk, so even a radial burst pops.")]
        public float upwardBias = 1.5f;

        // ── flight ────────────────────────────────────────────────────────────────
        [Tooltip("Downward acceleration, world units/sec². Higher = snappier arcs that fall fast.")]
        public float gravity = 20f;
        [Range(0f, 5f)]
        [Tooltip("Air resistance: per-second damping of velocity. 0 = none, ~1 = noticeable, ~3 = soupy.")]
        public float drag = 0.6f;

        [Tooltip("Slowest spin, degrees/sec.")]
        public float angularSpeedMin = 90f;
        [Tooltip("Fastest spin, degrees/sec; the direction is randomised per chunk.")]
        public float angularSpeedMax = 540f;
        [Tooltip("Point each chunk along its travel direction instead of spinning it freely.")]
        public bool faceVelocity = false;

        // ── life / look ───────────────────────────────────────────────────────────
        [Tooltip("Shortest lifetime, seconds.")]
        [Min(0.01f)] public float lifeMin = 0.6f;
        [Tooltip("Longest lifetime, seconds.")]
        [Min(0.01f)] public float lifeMax = 1.1f;

        // Bumped from 0.08/0.18 (2026-09-07, T-0252): at the project's PPU-16 pixel scale those world sizes are
        // 1.3-2.9 SCREEN pixels — sub-pixel, so debris read as fine noise instead of blocky pixel-art shrapnel.
        // The new defaults land around 2.5-5.5 screen pixels at PPU 16, matched by eye against ProtoGuy's own
        // sprite pixel density in Play mode. Still just a starting point — either dial is freely author-tunable.
        [Tooltip("Smallest chunk, world units.")]
        [Min(0.001f)] public float sizeMin = 0.16f;
        [Tooltip("Largest chunk, world units.")]
        [Min(0.001f)] public float sizeMax = 0.35f;

        // Legacy migration source ONLY (T-0261 — no native CurveField anywhere; ZUI Envelope is the only
        // authored curve control). Never authored directly anymore — see sizeEnvelope/alphaEnvelope below,
        // which MigrateLegacyCurves() converts these into on first load.
        [HideInInspector] public AnimationCurve sizeOverLife = DefaultSizeCurve();
        [HideInInspector] public AnimationCurve alphaOverLife = DefaultAlphaCurve();
        [SerializeField, HideInInspector] bool curvesMigrated;

        [Tooltip("Size across a chunk's life, left (spawn) to right (death).")]
        public List<ZUIEnvelopePoint> sizeEnvelope = new List<ZUIEnvelopePoint>();
        [Tooltip("Opacity across a chunk's life, left (spawn) to right (death).")]
        public List<ZUIEnvelopePoint> alphaEnvelope = new List<ZUIEnvelopePoint>();
        [Tooltip("Tint across a chunk's life, multiplied onto its own colour.")]
        public Gradient colorOverLife = DefaultColorGradient();

        public override bool MigrateLegacyCurves()
        {
            if (curvesMigrated) return false;
            sizeEnvelope = SampleCurveToEnvelope(sizeOverLife, 1f);
            alphaEnvelope = SampleCurveToEnvelope(alphaOverLife, 1f);
            curvesMigrated = true;
            return true;
        }

        // ── floor ─────────────────────────────────────────────────────────────────
        [Tooltip("Bounce chunks off a horizontal floor. No Physics2D colliders involved.")]
        public bool useFloor = true;
        [Tooltip("World Y the chunks land on.")]
        public float floorY = 0f;
        [Range(0f, 1f)]
        [Tooltip("Bounce on a floor hit: 0 = dead stop, 1 = full bounce.")]
        public float bounciness = 0.35f;
        [Range(0f, 1f)]
        [Tooltip("Sideways speed lost per floor hit: 0 = frictionless slide, 1 = stops sliding at once.")]
        public float floorFriction = 0.5f;
        [Tooltip("Let a slow chunk settle on the floor until it fades, instead of despawning where it lands.")]
        public bool restOnFloor = true;

        [Tooltip("Fixes every random pick so the scatter is identical every play. 0 = reroll every time.")]
        public int seed = 0;

        /// The last chunk is gone one full lifetime after the scatter fires.
        public override float DurationSeconds(ChunkSpec spec) => Mathf.Max(0.01f, Mathf.Max(lifeMin, lifeMax));

        public override void Fire(in ChunkModuleContext ctx)
        {
            var container = ctx.Container;
            if (container == null) return;

            var rng = Rng(seed);
            int count = rng.RangeInclusive(Mathf.Min(countMin, countMax), Mathf.Max(countMin, countMax));
            if (count <= 0) return;

            // A modifier is resolved BY its producer, never dispatched: asking here means a trail or a hitbox
            // can only ever be handed to a chunk that actually exists.
            var spec = ctx.Spec;
            var trail = spec != null ? spec.FindModifier<Trail>(this) : null;
            var hits = spec != null ? spec.FindModifier<Hits>(this) : null;

            // An override handed in by the caller (a Zoe playing its own death animation through a shared
            // debris recipe) outranks the authored source whatever visual mode is selected — the caller knows
            // something about this one burst that the recipe cannot.
            IChunkAnimation anim = ctx.AnimationOverride
                                   ?? (visual == DebrisVisual.Animated ? AnimationSource : null);

            bool haveSprites = visual == DebrisVisual.Sprites && sprites != null && sprites.Count > 0;
            var palette = ctx.Palette;
            bool havePalette = palette != null && palette.Count > 0;
            float centerDeg = ctx.DirectionDeg;

            // Resolved ONCE per burst, not per chunk: a live caller override (the Zoe's current frame) always
            // outranks the authored sampleSource, and the effective PPU folds in the project's Pixel Scale
            // setting unless this scatter deliberately overrides it — see EffectivePixelsPerUnit/ResolvedSampleSource.
            Sprite resolvedSampleSource = ResolvedSampleSource(in ctx);
            bool sampledMode = visual == DebrisVisual.Sampled && resolvedSampleSource != null;
            float effectivePpu = EffectivePixelsPerUnit;

            for (int i = 0; i < count; i++)
            {
                var chunk = ChunkPool.Get();
                var go = chunk.gameObject;
                go.transform.SetParent(container, false);
                go.transform.localPosition = Vector3.zero;

                var sr = go.GetComponent<SpriteRenderer>();
                // Every chunk in one scatter sits at the SAME depth — a chunk is not a layer, and sub-ordering
                // sixteen of them would spend the whole slot's band on debris nobody can tell apart.
                ctx.ApplyOrder(sr, LayerName);

                // ⚠ ROLLED FIRST, before the sprite is built — this ORDER is the whole fix (T-0397). A chunk's
                // on-screen size and the amount of source art it is made of used to be two independent random
                // numbers with nothing coupling them, so a 20-texel crop could land on a 2.5-pixel target and
                // render ~8 of its own texels inside one game pixel. The size has to exist BEFORE anything picks
                // how many texels to cut or which shard shape to use. ChunkPreviewSim.Debris draws this same
                // roll at this same point in its mirrored stream — move one and you MUST move the other, or a
                // seeded preview silently stops matching the burst it is previewing.
                float size = rng.Range(sizeMin, sizeMax);

                bool sampled = false;
                // True only for a sprite WE built at effectivePpu (a cut or a procedural shard), i.e. one whose
                // texels are the project's own pixels and which the two fits below guarantee is never downscaled.
                // Authored art (Sprites/Animated) is deliberately false: an author's sprite resolution is their
                // choice, and a chunk must never be silently enlarged to "protect" a sprite they sized themselves.
                bool pixelExact = false;
                if (anim == null)
                {
                    // The cut is capped to what this chunk can actually show — see FitSampleRange.
                    FitSampleRange(size, effectivePpu, out int fitMinPx, out int fitMaxPx);
                    Sprite cut = sampledMode
                        ? SampledChunkSprites.Sample(resolvedSampleSource, fitMinPx, fitMaxPx, effectivePpu,
                                                     tintMode, tintColor, tintStrength, edgeThicknessPx, modifiers)
                        : null;
                    if (cut != null) { sr.sprite = cut; sampled = true; pixelExact = true; }
                    else if (haveSprites) sr.sprite = sprites[rng.Next(sprites.Count)];
                    else
                    {
                        // GetFitting, not Get (T-0397). T-0393 introduced GetFitting for exactly this reason and
                        // its own commit message recorded that DebrisScatter's Get call was left alone — this is
                        // that sibling, closed. A 3×3 shard squashed onto a 1px target renders each of its texels
                        // at a third of a game pixel; GetFitting picks only among shards that already fit.
                        // ⚠ The bound swap (ChunkSprites.Count → int.MaxValue) keeps the rng stream intact:
                        // ChunkRng.Next(int) costs exactly one step for ANY bound above 1, and Count is 6. It
                        // would NOT be safe for a bound that can be 0 or 1 — Next short-circuits and consumes
                        // nothing there — which is why ChunkPreviewSim's matching draw is left exactly as it is.
                        sr.sprite = ChunkSprites.GetFitting(size * effectivePpu, effectivePpu,
                                                            rng.Next(int.MaxValue));
                        pixelExact = true;
                    }
                }
                bool tumbling = sampled && tumble;

                Color baseColor = havePalette ? (Color)palette[rng.Next(palette.Count)] : Color.white;

                float angleRad = (centerDeg + rng.Range(-spreadDeg, spreadDeg)) * Mathf.Deg2Rad;
                Vector2 vel = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * rng.Range(speedMin, speedMax);
                vel.y += upwardBias;

                float angular = (tumbling ? rng.Range(tumbleSpeedMin, tumbleSpeedMax)
                                          : rng.Range(angularSpeedMin, angularSpeedMax)) * rng.NextSign();
                float life = rng.Range(lifeMin, lifeMax);
                // (size was rolled at the top of this iteration — see the note there. The stream's LENGTH and
                // composition are unchanged, so ChunkPreviewSim.Debris stays in lockstep by moving its own
                // matching draw to the same new position; only which rolled value lands on which chunk differs.)

                chunk.Init(this, vel, angular, life, size, baseColor, anim, tumbling, ctx.Owner, trail, hits,
                           pixelExact);

                System.Action onFinished = null;
                onFinished = () => { chunk.Finished -= onFinished; ChunkPool.Release(chunk); };
                chunk.Finished += onFinished;
            }
        }

        // Also used as field initialisers, so a capability added from the Add menu already looks right.
        static AnimationCurve DefaultSizeCurve() => AnimationCurve.Constant(0f, 1f, 1f);

        static AnimationCurve DefaultAlphaCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.65f, 1f), new Keyframe(1f, 0f));

        static Gradient DefaultColorGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
