using System.Collections.Generic;
using UnityEngine;
using Laubrary.SpriteFx;

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

        [Tooltip("Which layer-stack slot the debris draws in. Empty draws it in stack order, behind every " +
                 "slotted output.")]
        public string layerName = "";

        public override string LayerName => layerName;

        // ── what a chunk looks like ───────────────────────────────────────────────
        [Tooltip("What each chunk is made of. Only the chosen kind's own dials are authored.")]
        public DebrisVisual visual = DebrisVisual.Squares;

        [Tooltip("Chunk sprites to pick from at random.")]
        public List<Sprite> sprites = new List<Sprite>();

        [Tooltip("Pixels-per-unit for the procedural pixel-square.")]
        [Min(1f)] public float pixelsPerUnit = 32f;

        [Tooltip("The sprite small chunks are cut out of, so the debris is made of the exploding object's own " +
                 "pixels. Its texture must have Read/Write Enabled.")]
        public Sprite sampleSource;
        [Tooltip("Smallest sampled chunk, in source-texture pixels.")]
        [Min(1)] public int samplePxMin = 5;
        [Tooltip("Largest sampled chunk, in source-texture pixels.")]
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

        /// Whether chunks are cut out of a source sprite rather than drawn from art or built procedurally.
        public bool UsesSampledDebris => visual == DebrisVisual.Sampled && sampleSource != null;

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

        [Tooltip("Smallest chunk, world units.")]
        [Min(0.001f)] public float sizeMin = 0.08f;
        [Tooltip("Largest chunk, world units.")]
        [Min(0.001f)] public float sizeMax = 0.18f;

        [Tooltip("Size across a chunk's life, left (spawn) to right (death).")]
        public AnimationCurve sizeOverLife = DefaultSizeCurve();
        [Tooltip("Opacity across a chunk's life, left (spawn) to right (death).")]
        public AnimationCurve alphaOverLife = DefaultAlphaCurve();
        [Tooltip("Tint across a chunk's life, multiplied onto its own colour.")]
        public Gradient colorOverLife = DefaultColorGradient();

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

                bool sampled = false;
                if (anim == null)
                {
                    Sprite cut = UsesSampledDebris
                        ? SampledChunkSprites.Sample(sampleSource, samplePxMin, samplePxMax, pixelsPerUnit,
                                                     tintMode, tintColor, tintStrength, edgeThicknessPx, modifiers)
                        : null;
                    if (cut != null) { sr.sprite = cut; sampled = true; }
                    else if (haveSprites) sr.sprite = sprites[rng.Next(sprites.Count)];
                    else sr.sprite = ChunkSprites.Get(rng.Next(ChunkSprites.Count), pixelsPerUnit);
                }
                bool tumbling = sampled && tumble;

                Color baseColor = havePalette ? (Color)palette[rng.Next(palette.Count)] : Color.white;

                float angleRad = (centerDeg + rng.Range(-spreadDeg, spreadDeg)) * Mathf.Deg2Rad;
                Vector2 vel = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * rng.Range(speedMin, speedMax);
                vel.y += upwardBias;

                float angular = (tumbling ? rng.Range(tumbleSpeedMin, tumbleSpeedMax)
                                          : rng.Range(angularSpeedMin, angularSpeedMax)) * rng.NextSign();
                float life = rng.Range(lifeMin, lifeMax);
                float size = rng.Range(sizeMin, sizeMax);

                chunk.Init(this, vel, angular, life, size, baseColor, anim, tumbling, ctx.Owner, trail, hits);

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
