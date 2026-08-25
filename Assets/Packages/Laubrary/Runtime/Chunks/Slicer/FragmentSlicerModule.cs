using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// Chunks 2.0 module #4 — the BIG-PIECE cut. Splits one source sprite into a small number of large pieces
    /// (2–6, not dozens) and throws each one outward from where it sat in the original picture, with its own
    /// speed, spin, arc and fade.
    ///
    /// The difference from SampledChunkSprites, which is the whole reason this exists: that one cuts many
    /// small random sub-rects, deliberately unrecognisable, and reads as confetti. This one partitions the
    /// sprite's own opaque pixels into cells that tile the original exactly (see FragmentCutter), so a
    /// spaceship hull comes apart into three pieces of hull. Two things carry that read, and both are load-
    /// bearing: the pieces tile without gaps or overlap, and every piece keeps its OFFSET from the sprite's
    /// pivot so it starts exactly where that part of the picture was and flies outward from there. A fragment
    /// that spawns at the origin destroys the effect entirely.
    ///
    /// Standalone by contract: nothing else in the ChunkSpec has to be switched on. No layer stack, no
    /// timeline, no Pyre spawn, not even a debris count — with a source sprite and this module enabled a
    /// burst is just the picture coming apart.
    [System.Serializable]
    public class FragmentSlicerModule : IChunkModule
    {
        [Tooltip("Cut the source sprite into a few large, still-recognisable pieces that fly apart.")]
        public bool enabled = false;

        [Tooltip("Which layer-stack slot the fragments draw in. Ignored when no layer stack is configured.")]
        public string layerName = "Fragments";

        // ── what gets cut ────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Animated content to fracture — a Zoe, a Pyre blast, any asset implementing IChunkAnimation. " +
                 "Its FIRST frame is the picture that gets cut, so a character comes apart in the pose it " +
                 "starts in. Takes priority over the plain Sprite below. Empty = use that Sprite instead.")]
        public Object sourceVisual;

        [Tooltip("The plain sprite that gets cut into fragments, used when no animated Source Visual is set. " +
                 "Empty falls back to the Chunk's own Sample Source. Its texture needs Read/Write Enabled — " +
                 "without it nothing is cut and the burst is simply fragment-free.")]
        public Sprite source;

        [Tooltip("How many pieces the sprite is cut into. 2–6 is the range this is built for: few enough that " +
                 "each piece still reads as a part of the original thing.")]
        public int pieceCount = 3;

        [Tooltip("Fixes which pieces the cut produces so every play is identical. 0 = reroll every time.")]
        public int seed = 0;

        [Tooltip("Smallest piece, in source pixels. A cell below this is merged into its nearest neighbour " +
                 "instead of becoming a 1px 'fragment' nobody can see.")]
        public int minPieceAreaPx = 24;

        // ── how it flies ─────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Slowest initial launch speed, world units/sec.")]
        public float speedMin = 1.5f;
        [Tooltip("Fastest initial launch speed, world units/sec.")]
        public float speedMax = 3.5f;

        [Tooltip("Aim the fragments along the burst's own direction instead of a fixed angle set here.")]
        public bool useBurstDirection = true;

        [Tooltip("Centre direction of the cone in degrees. 0 = +X (right), 90 = +Y (up).")]
        public float directionDeg = 90f;

        [Range(0f, 180f)]
        [Tooltip("Cone half-angle around the direction. 180 = each piece flies straight out from where it sat " +
                 "in the picture (the natural 'it came apart' look); 0 = every piece flies the same way.")]
        public float spreadDeg = 180f;

        [Tooltip("Downward acceleration, world units/sec². Higher = snappier arcs that fall fast.")]
        public float gravity = 8f;

        [Range(0f, 5f)]
        [Tooltip("Air resistance: per-second exponential damping of velocity. 0 = none, ~1 = noticeable, ~3 = soupy.")]
        public float drag = 0.4f;

        [Tooltip("Slowest spin, degrees/sec.")]
        public float angularSpeedMin = 30f;
        [Tooltip("Fastest spin, degrees/sec (each piece's direction is randomised).")]
        public float angularSpeedMax = 180f;

        [Tooltip("Shortest lifetime, seconds.")]
        public float lifeMin = 1.2f;
        [Tooltip("Longest lifetime, seconds.")]
        public float lifeMax = 2f;

        [Tooltip("Opacity across a fragment's life, left (spawn) to right (death).")]
        public AnimationCurve alphaOverLife = DefaultAlphaCurve();

        public bool Enabled => enabled;
        public string LayerName => layerName;

        /// The ONE place the source precedence is decided — animated content first, plain sprite second — so no
        /// caller (Fire, the editor preview, ParticleSplashModule's fallback) re-derives it and drifts.
        ///
        /// Why animated content wins: Chunks cannot reference Zoetrope or Pyre (both already reference Chunks
        /// for IChunkAnimation, so the arrow back would cycle), which is why sourceVisual is a bare Object cast
        /// to the interface — the same trick ChunkSpec.AnimationSource uses. Only the FIRST usable frame is
        /// taken: a fracture is an instant, and the frame the thing was showing when it broke is the picture
        /// that has to come apart.
        ///
        /// Null-safe at every step on purpose: an unconfigured slicer is a legitimate authoring state (the
        /// module ships disabled and empty), so this returns null rather than throwing, and every caller is
        /// expected to treat null as "no fragments this burst".
        public Sprite ResolveSource()
        {
            // != null before the cast so a *destroyed* asset doesn't sneak past as a live managed reference —
            // Unity's fake-null only shows up through the == overload, never through `is`.
            if (sourceVisual != null && sourceVisual is IChunkAnimation animation)
            {
                var frames = animation.GetFrames();
                if (frames != null)
                {
                    for (int i = 0; i < frames.Length; i++)
                        if (frames[i] != null) return frames[i];
                }
                // Deliberately falls THROUGH to the static sprite instead of returning null when the animation
                // yields no usable frame (not built yet, empty reel, all-null array). Same waterfall rule the
                // splash module states outright: a tier that failed for ANY reason hands over to the one below
                // it, so a half-authored Zoe degrades to whatever art was already there rather than to nothing.
            }
            return source;
        }

        /// Convenience mirror of PyreSpawnModule.HasSpawner — the local idiom for "is this module actually
        /// pointed at anything?", used by UI that wants to warn before a burst silently produces no fragments.
        public bool HasSource => ResolveSource() != null;

        public void Fire(in ChunkModuleContext ctx)
        {
            if (!enabled || ctx.Container == null || ctx.Runner == null) return;

            // Precedence lives in ResolveSource() (animated content → plain sprite) and nowhere else; the one
            // tier it cannot know about is the spec's own sample source, since a module is handed the spec only
            // at Fire time. Still nothing → do nothing, and say nothing: a standalone module with no art has no
            // honest output, and a log per burst would be noise.
            // Explicit == checks rather than ?? because these are UnityEngine.Objects: a destroyed sprite is
            // fake-null and would sail straight through the null-coalescing operator.
            var src = ResolveSource();
            if (src == null && ctx.Spec != null) src = ctx.Spec.sampleSource;
            if (src == null) return;

            // seed 0 means "reroll every play", which also means the cut can never be cached (its key would
            // never repeat) — so the cache is asked for only when the author actually pinned a seed.
            bool seeded = seed != 0;
            int effectiveSeed = seeded ? seed : Random.Range(1, int.MaxValue);

            var pieces = FragmentCutter.Cut(src, pieceCount, minPieceAreaPx, effectiveSeed, cache: seeded);
            if (pieces == null || pieces.Count == 0)
            {
                // Still a soft failure — the burst goes on without fragments, nothing throws. But it used to be
                // a *silent* one, which became a real authoring trap the moment sourceVisual let an author
                // point this at a Zoe or a Pyre: those frames are ordinary imported sprites whose textures are
                // NOT Read/Write Enabled by default, so the cut returns nothing and the picture simply never
                // comes apart with no clue as to why. Warn ONCE per offending sprite (the file's own "a log per
                // burst would be noise" rule still holds) and keep going.
                WarnUncuttableOnce(src);
                return;
            }

            // Local, clamped copies: OnValidate lives on ChunkSpec and cannot reach into this module's fields,
            // so a min dialled above its max is corrected here rather than producing a negative range.
            float spdMin = Mathf.Max(0f, speedMin), spdMax = Mathf.Max(spdMin, speedMax);
            float spinMin = Mathf.Max(0f, angularSpeedMin), spinMax = Mathf.Max(spinMin, angularSpeedMax);
            float lifeLo = Mathf.Max(0.01f, lifeMin), lifeHi = Mathf.Max(lifeLo, lifeMax);
            float grav = Mathf.Max(0f, gravity), air = Mathf.Clamp(drag, 0f, 20f);
            float spread = Mathf.Clamp(spreadDeg, 0f, 180f);
            float coneCentre = useBurstDirection ? ctx.DirectionDeg : directionDeg;
            float ppu = Mathf.Max(1f, src.pixelsPerUnit);
            var curve = alphaOverLife;

            // A second generator, offset off the cut's seed, so the flight is reproducible with the cut but a
            // dial that only changes motion never re-rolls which pieces you get.
            var rng = new FragmentRng(effectiveSeed ^ 0x5BF03635);

            int n = pieces.Count;
            var gos = new GameObject[n];
            var srs = new SpriteRenderer[n];
            var sprites = new Sprite[n];
            var lives = new float[n];
            var spins = new float[n];

            for (int i = 0; i < n; i++)
            {
                var piece = pieces[i];
                var sprite = FragmentCutter.BuildSprite(piece, ppu, src.name + "_Fragment" + (i + 1));
                if (sprite == null) continue;

                var go = new GameObject("Fragment" + (i + 1));
                go.transform.SetParent(ctx.Container, false);
                go.transform.position = ctx.Origin + new Vector3(piece.offsetUnits.x, piece.offsetUnits.y, 0f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                // The ONE place draw order is decided — never sortingOrder by hand. The per-piece offset is
                // what lets several fragments sub-order inside one named slot (the design's Fragment1 /
                // Fragment2 / Fragment3 idea), and it degrades to the emitter's flat order + i when no layer
                // stack is configured at all.
                ctx.ApplyOrder(sr, layerName, i);

                // Outward = away from the sprite's centre, i.e. along the piece's own offset. The cone dial
                // pulls that toward the aim direction: at spread 180 the launch is purely radial (pieces come
                // apart), at 0 they all travel the aim direction together. No hidden jitter — the piece's own
                // position in the picture IS the per-piece variation, and it is the variation that reads.
                float radial = piece.offsetUnits.sqrMagnitude > 1e-8f
                    ? Mathf.Atan2(piece.offsetUnits.y, piece.offsetUnits.x) * Mathf.Rad2Deg
                    : coneCentre;
                float angle = Mathf.LerpAngle(coneCentre, radial, spread / 180f) * Mathf.Deg2Rad;
                float speed = rng.Range(spdMin, spdMax);
                var velocity = new Vector3(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed, 0f);

                float life = rng.Range(lifeLo, lifeHi);

                // Position is driven by the shared runner, not by a component bolted onto the fragment —
                // Chunks' standing rule, and it keeps all module motion on one clock. faceVelocity is off
                // because this module spins its pieces itself; the two would fight for the rotation.
                ctx.Runner.Move(go.transform, velocity, grav, air, life, faceVelocity: false);

                gos[i] = go;
                srs[i] = sr;
                sprites[i] = sprite;
                lives[i] = life;
                spins[i] = rng.Range(spinMin, spinMax) * (rng.NextBool() ? 1f : -1f);
            }

            ctx.Runner.StartCoroutine(LiveAndDie(gos, srs, sprites, lives, spins, curve));
        }

        /// Spin + fade + teardown for one burst's worth of fragments, on ONE coroutine rather than one per
        /// piece. Each fragment is destroyed the moment its own life runs out, along with the sprite and
        /// texture built for it — those are HideAndDontSave scratch objects and nothing else will collect them
        /// deterministically.
        ///
        /// The one path this cannot cover: if the burst CONTAINER is destroyed early, the runner living on it
        /// dies too and this coroutine simply stops, leaving those textures to Unity's own finalizer sweep
        /// rather than to us. That is the accepted cost of not bolting a cleanup component onto each fragment.
        static IEnumerator LiveAndDie(GameObject[] gos, SpriteRenderer[] srs, Sprite[] sprites,
                                      float[] lives, float[] spins, AnimationCurve alpha)
        {
            float t = 0f;
            bool any = true;
            while (any)
            {
                float dt = Time.deltaTime;
                t += dt;
                any = false;

                for (int i = 0; i < gos.Length; i++)
                {
                    if (sprites[i] == null) continue;             // already torn down
                    if (gos[i] == null) { Release(sprites, i); continue; }   // container took it with it

                    if (t >= lives[i])
                    {
                        Object.Destroy(gos[i]);
                        gos[i] = null;
                        Release(sprites, i);
                        continue;
                    }

                    any = true;
                    gos[i].transform.Rotate(0f, 0f, spins[i] * dt);
                    if (srs[i] != null)
                    {
                        var c = srs[i].color;
                        c.a = alpha != null ? Mathf.Clamp01(alpha.Evaluate(t / lives[i])) : 1f;
                        srs[i].color = c;
                    }
                }

                if (any) yield return null;
            }

            for (int i = 0; i < sprites.Length; i++) Release(sprites, i);
        }

        /// Destroys a fragment's sprite AND its texture — Sprite.Create does not own the texture, so releasing
        /// only the sprite leaks one texture per fragment per burst.
        static void Release(Sprite[] sprites, int i)
        {
            var sprite = sprites[i];
            if (sprite == null) return;
            var tex = sprite.texture;
            sprites[i] = null;
            Object.Destroy(sprite);
            if (tex != null) Object.Destroy(tex);
        }

        /// One warning per sprite instance, ever — a burst can fire dozens of times a second and the condition
        /// is a static property of the art, so repeating it would drown the console instead of informing it.
        /// Static (shared across every spec) because the sprite, not the module, is what is broken.
        static readonly HashSet<int> _warnedUncuttable = new HashSet<int>();

        static void WarnUncuttableOnce(Sprite src)
        {
            if (src == null || !_warnedUncuttable.Add(src.GetInstanceID())) return;
            bool readable = src.texture != null && src.texture.isReadable;
            Debug.LogWarning(readable
                ? $"[Chunks] Fragment Slicer cut nothing from '{src.name}': no opaque pixels large enough to " +
                  $"make a piece. Lower Min Piece Area or use art with a solid silhouette."
                : $"[Chunks] Fragment Slicer cannot cut '{src.name}': its texture is not Read/Write Enabled, so " +
                  $"its pixels cannot be read. Tick Read/Write in that texture's import settings. (Frames coming " +
                  $"from a Zoe or a Pyre are ordinary imported sprites and are NOT readable by default.)", src);
        }

        static AnimationCurve DefaultAlphaCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f));
    }
}
