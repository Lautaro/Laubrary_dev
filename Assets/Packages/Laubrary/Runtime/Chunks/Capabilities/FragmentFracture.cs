using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// The BIG-PIECE cut: one picture partitioned into a few large pieces that tile it exactly, each thrown
    /// outward from where it sat. The difference from sampled debris — many small, deliberately unrecognisable
    /// cuts that read as confetti — is the whole reason both exist: a hull comes apart into three pieces of
    /// hull. Two things carry that read and both are load-bearing: the pieces tile without gaps, and each keeps
    /// its OFFSET from the picture's pivot so it starts exactly where that part of the picture was.
    [System.Serializable]
    public class FragmentFracture : ChunkCapability
    {
        public override string KindName => "Fragment Fracture";

        [Tooltip("Which layer-stack slot the pieces draw in. Empty leaves them out of the plan, drawing " +
                 "in stack order in FRONT of every slotted output.")]
        public string layerName = "";

        public override string LayerName => layerName;

        // ── what gets cut ─────────────────────────────────────────────────────────
        [Tooltip("Animated content to fracture — a Zoe, a Pyre, anything that can hand over frames. Its FIRST " +
                 "frame is the picture that gets cut, so a character comes apart in the pose it was in.")]
        public Object sourceVisual;

        [Tooltip("The plain sprite that gets cut, used when no animated source is set. Its texture needs " +
                 "Read/Write Enabled or nothing can be cut.")]
        public Sprite source;

        [Tooltip("How many pieces the picture is cut into. 2–6 keeps each piece recognisable as part of it.")]
        public int pieceCount = 3;

        [Tooltip("Smallest piece, in source pixels. Anything below this is merged into its neighbour instead " +
                 "of becoming a fragment nobody can see.")]
        public int minPieceAreaPx = 24;

        [Tooltip("Fixes which pieces the cut produces and how they fly. 0 = reroll every time.")]
        public int seed = 0;

        // ── how it flies ──────────────────────────────────────────────────────────
        [Tooltip("Slowest launch speed, world units/sec.")]
        public float speedMin = 1.5f;
        [Tooltip("Fastest launch speed, world units/sec.")]
        public float speedMax = 3.5f;

        [Tooltip("Aim the pieces along the recipe's own direction instead of the angle set here.")]
        public bool useBurstDirection = true;

        [Tooltip("Centre of the cone in degrees. 0 = right, 90 = up.")]
        public float directionDeg = 90f;

        [Range(0f, 180f)]
        [Tooltip("Cone half-angle. 180 = each piece flies straight out from where it sat; 0 = all the same way.")]
        public float spreadDeg = 180f;

        [Tooltip("Downward acceleration, world units/sec².")]
        public float gravity = 8f;

        [Range(0f, 5f)]
        [Tooltip("Air resistance: per-second damping of velocity.")]
        public float drag = 0.4f;

        [Tooltip("Slowest spin, degrees/sec.")]
        public float angularSpeedMin = 30f;
        [Tooltip("Fastest spin, degrees/sec; each piece's direction is randomised.")]
        public float angularSpeedMax = 180f;

        [Tooltip("Shortest lifetime, seconds.")]
        public float lifeMin = 1.2f;
        [Tooltip("Longest lifetime, seconds.")]
        public float lifeMax = 2f;

        // Legacy migration source ONLY (T-0261 — no native CurveField anywhere; ZUI Envelope is the only
        // authored curve control). Never authored directly anymore — see alphaEnvelope below, which
        // MigrateLegacyCurves() converts this into on first load.
        [HideInInspector] public AnimationCurve alphaOverLife = DefaultAlphaCurve();
        [SerializeField, HideInInspector] bool curvesMigrated;

        [Tooltip("Opacity across a piece's life, left (spawn) to right (death).")]
        public List<ZUIEnvelopePoint> alphaEnvelope = new List<ZUIEnvelopePoint>();

        public override bool MigrateLegacyCurves()
        {
            if (curvesMigrated) return false;
            alphaEnvelope = SampleCurveToEnvelope(alphaOverLife, 1f);
            curvesMigrated = true;
            return true;
        }

        public override float DurationSeconds(ChunkSpec spec) => Mathf.Max(0.01f, Mathf.Max(lifeMin, lifeMax));

        /// The ONE place the source precedence is decided — animated content first, plain sprite second — so
        /// no caller (the fire, the preview, a Palette Splash borrowing it) re-derives it and drifts.
        ///
        /// Animated content wins because it is what the thing WAS at the instant it broke, and only its first
        /// usable frame is taken. It deliberately falls THROUGH to the plain sprite when the animation yields
        /// no usable frame (not built yet, an empty reel), so a half-authored source degrades to whatever art
        /// was already there rather than to nothing.
        public Sprite ResolveSource()
        {
            // != null before the cast: a destroyed asset is only fake-null through Unity's == overload, so
            // casting first would happily hand back a dead object.
            if (sourceVisual != null && sourceVisual is IChunkAnimation animation)
            {
                var frames = animation.GetFrames();
                if (frames != null)
                    for (int i = 0; i < frames.Length; i++)
                        if (frames[i] != null) return frames[i];
            }
            return source;
        }

        /// Whether this is actually pointed at anything — what a UI asks before warning that a burst will
        /// produce no fragments.
        public bool HasSource => ResolveSource() != null;

        public override void Fire(in ChunkModuleContext ctx)
        {
            if (ctx.Container == null || ctx.Runner == null) return;

            var src = ResolveSource();
            if (src == null) return;   // no art, no honest output, and a log per burst would be noise

            // Seed 0 means "reroll every play", which also means the cut can never be cached (its key would
            // never repeat) — so the cache is asked for only when the author actually pinned a seed.
            bool seeded = seed != 0;
            int effectiveSeed = seeded ? seed : Random.Range(1, int.MaxValue);

            var pieces = FragmentCutter.Cut(src, pieceCount, minPieceAreaPx, effectiveSeed, cache: seeded);
            if (pieces == null || pieces.Count == 0) { WarnUncuttableOnce(src); return; }

            float spdMin = Mathf.Max(0f, speedMin), spdMax = Mathf.Max(spdMin, speedMax);
            float spinMin = Mathf.Max(0f, angularSpeedMin), spinMax = Mathf.Max(spinMin, angularSpeedMax);
            float lifeLo = Mathf.Max(0.01f, lifeMin), lifeHi = Mathf.Max(lifeLo, lifeMax);
            float grav = Mathf.Max(0f, gravity), air = Mathf.Clamp(drag, 0f, 20f);
            float spread = Mathf.Clamp(spreadDeg, 0f, 180f);
            float coneCentre = useBurstDirection ? ctx.DirectionDeg : directionDeg;
            float ppu = Mathf.Max(1f, src.pixelsPerUnit);
            var curve = alphaEnvelope;

            // A second stream off the same seed, so a dial that only changes motion never re-rolls the cut.
            var rng = new ChunkRng(effectiveSeed, 1);

            // Modifiers are resolved BY the producer, never dispatched, so they can only ever be handed a
            // piece that actually exists.
            var spec = ctx.Spec;
            var trail = spec != null ? spec.FindModifier<Trail>(this) : null;
            var hits = spec != null ? spec.FindModifier<Hits>(this) : null;

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
                // The per-piece offset is what lets several fragments sub-order inside one named slot.
                ctx.ApplyOrder(sr, layerName, i);

                hits?.Attach(go, sprite.bounds.size.magnitude * 0.5f, ctx.Owner);

                // Outward = away from the picture's centre, i.e. along the piece's own offset. The cone dial
                // pulls that toward the aim: at spread 180 the launch is purely radial (it comes apart), at 0
                // every piece travels the aim direction. The piece's own position IS the per-piece variation.
                float radial = piece.offsetUnits.sqrMagnitude > 1e-8f
                    ? Mathf.Atan2(piece.offsetUnits.y, piece.offsetUnits.x) * Mathf.Rad2Deg
                    : coneCentre;
                float angle = Mathf.LerpAngle(coneCentre, radial, spread / 180f) * Mathf.Deg2Rad;
                float speed = rng.Range(spdMin, spdMax);
                var velocity = new Vector3(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed, 0f);

                float life = rng.Range(lifeLo, lifeHi);

                // Position is driven by the shared runner rather than by a component bolted onto the piece —
                // the standing rule, and it keeps all capability motion on one clock. faceVelocity is off
                // because this spins its own pieces and the two would fight for the rotation.
                ctx.Runner.Move(go.transform, velocity, grav, air, life, faceVelocity: false);

                gos[i] = go;
                srs[i] = sr;
                sprites[i] = sprite;
                lives[i] = life;
                spins[i] = rng.Range(spinMin, spinMax) * rng.NextSign();
            }

            ctx.Runner.StartCoroutine(LiveAndDie(gos, srs, sprites, lives, spins, curve,
                                                 trail != null ? trail.Source : null,
                                                 trail != null ? trail.interval : 0f));
        }

        /// Spin + fade + teardown for one fire's worth of pieces on ONE coroutine rather than one per piece.
        /// Each piece is destroyed when its own life runs out, along with the sprite and texture built for it —
        /// those are scratch objects nothing else will collect deterministically.
        ///
        /// The one path this cannot cover: if the burst CONTAINER dies early the runner living on it dies too
        /// and this simply stops, leaving those textures to Unity's own sweep. That is the accepted cost of
        /// not bolting a cleanup component onto every piece.
        static IEnumerator LiveAndDie(GameObject[] gos, SpriteRenderer[] srs, Sprite[] sprites,
                                      float[] lives, float[] spins, List<ZUIEnvelopePoint> alpha,
                                      IChunkTrailSource trail, float trailInterval)
        {
            float t = 0f;
            bool any = true;
            // One shared clock for the whole set rather than one per piece: the puffs are meant to read as a
            // wake behind the fracture, and staggering them per piece is invisible at these intervals.
            float trailClock = 0f;
            float step = Mathf.Max(0.01f, trailInterval);
            while (any)
            {
                float dt = Time.deltaTime;
                t += dt;
                any = false;

                bool puff = false;
                if (trail != null)
                {
                    trailClock += dt;
                    if (trailClock >= step) { trailClock -= step; puff = true; }
                }

                for (int i = 0; i < gos.Length; i++)
                {
                    if (sprites[i] == null) continue;                        // already torn down
                    if (gos[i] == null) { Release(sprites, i); continue; }    // container took it with it

                    if (t >= lives[i])
                    {
                        Object.Destroy(gos[i]);
                        gos[i] = null;
                        Release(sprites, i);
                        continue;
                    }

                    any = true;
                    gos[i].transform.Rotate(0f, 0f, spins[i] * dt);
                    if (puff)
                        trail.SpawnPuff(gos[i].transform.position, srs[i] != null ? srs[i].sortingOrder : 0);
                    if (srs[i] != null)
                    {
                        var c = srs[i].color;
                        c.a = Mathf.Clamp01(ZUIEnvelopeEvaluator.Evaluate(alpha, t / lives[i], 1f));
                        srs[i].color = c;
                    }
                }

                if (any) yield return null;
            }

            for (int i = 0; i < sprites.Length; i++) Release(sprites, i);
        }

        /// Destroys a piece's sprite AND its texture — Sprite.Create does not own the texture, so releasing
        /// only the sprite leaks one texture per piece per burst.
        static void Release(Sprite[] sprites, int i)
        {
            var sprite = sprites[i];
            if (sprite == null) return;
            var tex = sprite.texture;
            sprites[i] = null;
            Object.Destroy(sprite);
            if (tex != null) Object.Destroy(tex);
        }

        /// One warning per sprite instance, ever. A burst can fire dozens of times a second and the condition
        /// is a static property of the art, so repeating it would drown the console instead of informing it.
        static readonly HashSet<int> _warnedUncuttable = new HashSet<int>();

        static void WarnUncuttableOnce(Sprite src)
        {
            if (src == null || !_warnedUncuttable.Add(src.GetInstanceID())) return;
            bool readable = src.texture != null && src.texture.isReadable;
            Debug.LogWarning(readable
                ? $"[Chunks] Fragment Fracture cut nothing from '{src.name}': no opaque pixels large enough to " +
                  $"make a piece. Lower Min Piece Area or use art with a solid silhouette."
                : $"[Chunks] Fragment Fracture cannot cut '{src.name}': its texture is not Read/Write Enabled, " +
                  $"so its pixels cannot be read. Tick Read/Write in that texture's import settings. (Frames " +
                  $"coming from a Zoe or a Pyre are ordinary imported sprites and are NOT readable by default.)", src);
        }

        static AnimationCurve DefaultAlphaCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f));
    }
}
