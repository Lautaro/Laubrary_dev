using System.Collections.Generic;
using UnityEngine;
using Laubrary.SpriteFx;
using Laubrary.PreviewKit;
using Laubrary.Layering;

namespace Laubrary.Chunks
{
    /// A ChunkSpec is the reusable recipe for one debris burst — the shrapnel a game throws when something explodes.
    /// It says how many bits fly out, how fast and in which cone, how they arc under gravity, drag and spin, how big
    /// and how long they live, how they tint and fade, and how they behave when they hit a floor. It ships ZERO
    /// assets: if <see cref="sprites"/> is empty, chunks use a procedural pixel-square built at runtime, tinted by
    /// the SpriteRenderer's colour. Hand it to a <see cref="ChunkEmitter"/> or the static <c>Chunks.Burst</c> API.
    [CreateAssetMenu(menuName = "Laubrary/Chunks/Chunk Spec", fileName = "Chunks")]
    public class ChunkSpec : ScriptableObject, IVisualPreview
    {
        // ── Emission ──────────────────────────────────────────────────────────────
        [Header("Emission")]
        [Tooltip("Fewest chunks a burst spawns.")]
        [Min(0)] public int countMin = 8;
        [Tooltip("Most chunks a burst spawns (inclusive). Each burst picks a random count in [min, max].")]
        [Min(0)] public int countMax = 16;

        [Tooltip("Slowest initial launch speed, world units/sec.")]
        public float speedMin = 3f;
        [Tooltip("Fastest initial launch speed, world units/sec.")]
        public float speedMax = 7f;

        [Tooltip("Centre direction of the cone in degrees. 0 = +X (right), 90 = +Y (up).")]
        public float directionDeg = 90f;
        [Range(0f, 180f)]
        [Tooltip("Cone half-angle around the direction. 0 = a tight jet; 180 = a full circle (radial burst).")]
        public float spreadDeg = 180f;
        [Tooltip("Extra initial +Y velocity added to every chunk, so even a radial burst still pops upward.")]
        public float upwardBias = 1.5f;

        // ── Physics ───────────────────────────────────────────────────────────────
        [Header("Physics")]
        [Tooltip("Downward acceleration, world units/sec². Higher = snappier arcs that fall fast.")]
        public float gravity = 20f;
        [Range(0f, 5f)]
        [Tooltip("Air resistance: per-second exponential damping of velocity. 0 = none, ~1 = noticeable, ~3 = soupy.")]
        public float drag = 0.6f;

        [Tooltip("Slowest spin, degrees/sec.")]
        public float angularSpeedMin = 90f;
        [Tooltip("Fastest spin, degrees/sec (sign is randomised per chunk).")]
        public float angularSpeedMax = 540f;
        [Tooltip("Rotate each chunk to point along its travel direction instead of spinning freely.")]
        public bool faceVelocity = false;

        // ── Life / look ───────────────────────────────────────────────────────────
        [Header("Life / look")]
        [Tooltip("Shortest lifetime, seconds.")]
        [Min(0.01f)] public float lifeMin = 0.6f;
        [Tooltip("Longest lifetime, seconds.")]
        [Min(0.01f)] public float lifeMax = 1.1f;

        [Tooltip("Smallest chunk size, world units.")]
        [Min(0.001f)] public float sizeMin = 0.08f;
        [Tooltip("Largest chunk size, world units.")]
        [Min(0.001f)] public float sizeMax = 0.18f;

        [Tooltip("Size multiplier over normalised life (0→1). Default holds at 1. Set a falloff to shrink as they die.")]
        public AnimationCurve sizeOverLife = DefaultSizeCurve();
        [Tooltip("Alpha over normalised life (0→1). Default holds opaque, then fades to 0 near the end.")]
        public AnimationCurve alphaOverLife = DefaultAlphaCurve();
        [Tooltip("Tint over normalised life (0→1). Multiplied onto the chunk's base colour. Default white→white.")]
        public Gradient colorOverLife = DefaultColorGradient();

        [Tooltip("Chunk sprites to pick from at random. Leave EMPTY to use a procedural pixel-square (tinted).")]
        public List<Sprite> sprites = new();
        [Tooltip("Pixels-per-unit for the procedural fallback sprite (only used when the sprite list is empty).")]
        [Min(1f)] public float pixelsPerUnit = 32f;

        // ── Floor / collision (cheap, no Physics2D) ─────────────────────────────────
        [Header("Floor / collision")]
        [Tooltip("Bounce chunks off a horizontal floor at floorY. No Physics2D colliders involved.")]
        public bool useFloor = true;
        [Tooltip("World Y of the floor the chunks land on.")]
        public float floorY = 0f;
        [Range(0f, 1f)]
        [Tooltip("Restitution on a floor hit: 0 = dead stop, 1 = full bounce.")]
        public float bounciness = 0.35f;
        [Range(0f, 1f)]
        [Tooltip("Horizontal speed lost on each floor hit: 0 = frictionless slide, 1 = instantly stops sliding.")]
        public float floorFriction = 0.5f;
        [Tooltip("When a chunk goes slow on the floor, settle it there (stop + rest until it fades) instead of despawning.")]
        public bool restOnFloor = true;

        // ── Sampled pseudo-3D debris (optional) ──────────────────────────────────
        [Header("Sampled pseudo-3D debris (optional)")]
        [Tooltip("Instead of a flat procedural shape or a hand-authored sprite, cut small chunks directly " +
                 "out of the exploding object's own sprite and tumble them with a squash+shade trick that " +
                 "reads as a lit 3D fragment with no real 3D geometry. Leave null to use plain debris. " +
                 "Loses to animationSource if that's also set. Source texture must have Read/Write Enabled.")]
        public Sprite sampleSource;
        [Tooltip("Smallest sampled chunk size, in source-texture pixels.")]
        [Min(1)] public int samplePxMin = 5;
        [Tooltip("Largest sampled chunk size, in source-texture pixels.")]
        [Min(1)] public int samplePxMax = 20;
        [Tooltip("Tumble sampled chunks (squash + shade) instead of a flat 2D spin — the pseudo-3D trick. " +
                 "Only applies to chunks sourced from sampleSource.")]
        public bool tumble = true;
        [Tooltip("Slowest simulated tumble rate, degrees/sec.")]
        public float tumbleSpeedMin = 180f;
        [Tooltip("Fastest simulated tumble rate, degrees/sec.")]
        public float tumbleSpeedMax = 720f;
        [Range(0f, 1f)]
        [Tooltip("How strong the light/dark swing is as a chunk turns. 0 = squash only, 1 = full swing.")]
        public float tumbleShadeStrength = 0.6f;

        [Tooltip("Recolour sampled debris (see Sample source above) as it's cut. None = the source pixels, " +
                 "unchanged. Whole = every opaque pixel. Edges only = just the rim (the cut boundary AND any " +
                 "genuine alpha-silhouette edge) — a burned/glowing-edge look. Excluding edges = everywhere " +
                 "EXCEPT the rim — a scorched interior with a clean, untinted edge.")]
        public ChunkTintMode tintMode = ChunkTintMode.None;
        [Tooltip("The tint colour (or sample a gradient's own colour externally and set this per-burst).")]
        public Color tintColor = new Color(1f, 0.35f, 0.08f, 1f);
        [Range(0f, 1f)]
        [Tooltip("How strongly the tint blends onto the source pixel — 0 = no visible effect, 1 = fully replaced.")]
        public float tintStrength = 0.6f;
        [Min(1)]
        [Tooltip("Edge modes only: how many pixels from the rim (cut boundary or alpha silhouette) count as 'edge'.")]
        public int edgeThicknessPx = 1;

        [Tooltip("Optional SpriteFx pixel-modifier stack baked ONCE into each sampled chunk's texture at spawn, " +
                 "in list order — a cheap way to style the cut debris (tint, posterise, dither, dissolve, and the " +
                 "other shaped SpriteFx pixel modifiers). Empty = the raw sampled pixels, unchanged (byte-identical " +
                 "to no stack). Only applies to SAMPLED debris (Sample source set); it does not touch procedural " +
                 "pixel-squares, authored sprites or animated content. Resolved at life 0 (the spawn instant); it " +
                 "is a one-time still pass, not animated over the chunk's life.")]
        [SerializeReference] public List<PixelModifier> modifiers = new();

        /// True when a chunk should be sourced by sampling sampleSource rather than sprites/procedural.
        public bool UsesSampledDebris => sampleSource != null;

        // ── Animated content (optional) ──────────────────────────────────────────
        [Header("Animated content (optional)")]
        [Tooltip("Optional animated content every chunk plays instead of a static/procedural sprite — pick a " +
                 "Pyre or a Zoe directly, or a wrapper (Pyre Blast Chunk Animation, Lauminary Chunk " +
                 "Animation) when you need to override its speed or looping. Leave empty for plain debris.")]
        public Object animationSource;

        /// animationSource cast to the interface Chunks actually needs, or null if unset/incompatible.
        public IChunkAnimation AnimationSource => animationSource as IChunkAnimation;

        // ── Chunks 2.0 modules (all optional, all standalone) ────────────────────
        // Every one of these is off by default, so a spec authored before they existed fires exactly the burst
        // it always did. Each is its own serializable object rather than a flat run of fields on ChunkSpec, so
        // one module's settings can never be mistaken for another's and each can be built, tested and shown in
        // the UI on its own — the design doc's standalone-first principle, expressed in the data model.
        [Header("Chunks 2.0 modules")]
        [Tooltip("Sprays palette-sampled pixel particles out of the source sprite's own footprint.")]
        public ParticleSplashModule particleSplash = new ParticleSplashModule();

        [Tooltip("Spawns a Pyre blast, or one picked at random from a pool, when the burst fires.")]
        public PyreSpawnModule pyreSpawn = new PyreSpawnModule();

        [Tooltip("Gives each spawned blast real physical motion instead of playing it where it was spawned.")]
        public PyreMotionModule pyreMotion = new PyreMotionModule();

        [Tooltip("Cuts the source sprite into a few large, still-recognisable pieces that fly apart.")]
        public FragmentSlicerModule fragmentSlicer = new FragmentSlicerModule();

        [Tooltip("Places several spawns in a shape and staggers when each one fires, instead of one at the origin.")]
        public SpawnFormationModule spawnFormation = new SpawnFormationModule();

        [Tooltip("Further blast groups, each with its OWN blast and its own layer slot — the way to author " +
                 "'this explosion behind the fragments, those ones in front of them'. The Pyre Spawn module " +
                 "above is simply the first group; every entry here is another one, fired at the same time " +
                 "unless the timeline says otherwise.")]
        public List<PyreSpawnModule> blastGroups = new List<PyreSpawnModule>();

        [Tooltip("The ordered named draw-order slots this composed effect's pieces sit in. Empty = every piece " +
                 "just takes the emitter's own sorting order, which is the pre-layering behaviour.")]
        public LayerSpec layers = new LayerSpec();

        [Tooltip("Schedules when each module fires relative to burst-start, and fires code/sound events along the way.")]
        public ChunkTimeline timeline = new ChunkTimeline();

        // ── Hit detection (optional, cheap) ───────────────────────────────────────
        [Header("Hit detection (optional, cheap)")]
        [Tooltip("Give each chunk a trigger CircleCollider2D + a Combat2D Hitbox while it's alive, so it can " +
                 "damage hurtboxes it touches — a wall peppered by bullet debris, embers that also burn. Cheap " +
                 "circle-APPROXIMATION only, not pixel-perfect (that would need Burst/Jobs, a bigger, separate " +
                 "decision) — off by default since most debris is purely visual.")]
        public bool useHitDetection = false;
        [Tooltip("Damage dealt by a single chunk's hit (once per target, per chunk).")]
        [Min(0f)] public float hitDamage = 5f;
        [Range(0.1f, 3f)]
        [Tooltip("Collider radius as a multiple of the chunk's own current world size (so it shrinks with it as size-over-life ramps down).")]
        public float hitRadiusScale = 0.5f;

        // ── Trail (optional) ──────────────────────────────────────────────────────
        [Header("Trail (optional)")]
        [Tooltip("Optional puff spawned at this chunk's own position on a timer while it's flying — an asset " +
                 "implementing IChunkTrailSource (e.g. a Pyre Blast Trail Source, a fire→smoke blast). Leave " +
                 "empty for no trail.")]
        public Object trailSource;
        [Min(0.01f)]
        [Tooltip("Seconds between trail puffs.")]
        public float trailInterval = 0.08f;

        /// trailSource cast to the interface Chunks actually needs, or null if unset/incompatible.
        public IChunkTrailSource TrailSource => trailSource as IChunkTrailSource;

        // ── IVisualPreview: a thumbnail for LauAsset pickers / browsers. ─────────────────────────────────────
        //
        // The question a thumbnail answers is "what IS this chunk?", and for a Chunks 2.0 spec the answer is
        // almost never in the legacy sprite fields. This used to consult only animationSource / sampleSource /
        // sprites and then fall through to a fixed-seed swatch, which meant every composed recipe in the
        // library rendered the SAME scatter of white squares — a flagship spec whose identity is a character
        // shattering in front of three layered blasts looked exactly like an empty spec. So the whole module
        // set is resolved here, most-identifying first, by ONE method (ResolvePreviewSource) that the animated
        // trio below calls too — see the note on it for why that has to be shared.
        public Texture2D RenderPreviewTexture()
        {
            ResolvePreviewSource(out var animated, out var still);

            // A delegate that renders nothing (an unbuilt Zoe, a zero-frame blast) is NOT allowed to leave a
            // blank square behind: fall through to the still, and past it to the swatch, exactly as if it had
            // never been picked. See ProceduralSwatch for why blank is forbidden outright.
            if (animated != null)
            {
                var t = animated.RenderPreviewTexture();
                if (t != null) return t;
            }
            return SpriteToTexture(still) ?? ProceduralSwatch();
        }

        /// The ONE place a ChunkSpec's visual identity is decided, in the same spirit as
        /// FragmentSlicerModule.ResolveSource(): every preview member asks this rather than re-deriving the
        /// precedence and drifting from it. Sets an `animated` delegate (an asset that renders its own preview,
        /// and may animate it) and/or a `still` sprite to crop; both stay null when the spec has no authored art
        /// at all, which is the swatch's cue. The one tier that sets BOTH is the fragment slicer, so that a
        /// source which cannot render itself still degrades to its own first frame rather than to the swatch.
        ///
        /// Precedence, most-identifying first:
        ///   1. Animated chunk content — what every single chunk literally plays, so it beats everything.
        ///   2. What the Fragment Slicer cuts apart — for a "X blows up" recipe, X is the answer to "what is
        ///      this?", ahead of any blast going off behind it. Its animated source is preferred over the
        ///      plain sprite because that source can also ANIMATE the thumbnail; when it can't render itself
        ///      the slicer's own ResolveSource() still hands over the first frame as a still.
        ///   3. Authored debris art — sample source, then the first sprite in the pool. Unchanged from before,
        ///      and still exactly right for a plain debris spec.
        ///   4. The Particle Splash sprite, when that module is the only thing pointed at any art.
        ///   5. The blast — for a pure-blast spec (no debris, no fragments) the blast IS the effect.
        ///
        /// Modules are consulted only when ENABLED, so a thumbnail only ever shows something the burst will
        /// actually produce. Nothing here is random: tier 5 deliberately does NOT call PyreSpawnModule
        /// .PickSpawner(), which rolls a die over the pool — see ProceduralSwatch on why determinism matters.
        void ResolvePreviewSource(out IVisualPreview animated, out Sprite still)
        {
            animated = null;
            still = null;

            // `!= null` before every cast: a DESTROYED asset is only fake-null through Unity's == overload,
            // never through `is`, so casting first would happily return a dead object.
            if (animationSource != null && AnimationSource is IVisualPreview anim) { animated = anim; return; }

            if (fragmentSlicer != null && fragmentSlicer.enabled)
            {
                var cut = fragmentSlicer.ResolveSource();
                if (fragmentSlicer.sourceVisual != null && fragmentSlicer.sourceVisual is IVisualPreview cutVp)
                { animated = cutVp; still = cut; return; }
                if (cut != null) { still = cut; return; }
            }

            if (sampleSource != null) { still = sampleSource; return; }
            if (sprites != null)
                for (int i = 0; i < sprites.Count; i++)
                    if (sprites[i] != null) { still = sprites[i]; return; }

            if (particleSplash != null && particleSplash.enabled && particleSplash.sprite != null)
            { still = particleSplash.sprite; return; }

            var blast = FirstBlastAsset();
            if (blast != null && blast is IVisualPreview blastVp) { animated = blastVp; return; }
        }

        /// The blast a pure-blast spec is "about": the first enabled group's single source, else that group's
        /// first usable pool entry. In authored order — pyreSpawn is group one, blastGroups are the rest —
        /// and with no randomness anywhere, so the thumbnail is the same one every time it is rebuilt.
        Object FirstBlastAsset()
        {
            var first = BlastAssetOf(pyreSpawn);
            if (first != null) return first;
            if (blastGroups != null)
                for (int i = 0; i < blastGroups.Count; i++)
                {
                    var g = BlastAssetOf(blastGroups[i]);
                    if (g != null) return g;
                }
            return null;
        }

        static Object BlastAssetOf(PyreSpawnModule g)
        {
            if (g == null || !g.enabled) return null;
            if (g.source != null) return g.source;
            if (g.pool != null)
                for (int i = 0; i < g.pool.Count; i++)
                    if (g.pool[i] != null) return g.pool[i];
            return null;
        }

        // A spec with no authored art is not a spec with no LOOK: it throws procedural pixel-squares tinted along
        // colorOverLife, so that gradient IS its visual identity. Returning null here used to leave a blank square in
        // every browser and picker, which reads as "this has a picture and it failed to load" — a lie, and the one
        // thing the thumbnail rules forbid outright. Draw what the debris actually looks like instead.
        //
        // It used to draw that from a FIXED seed and a fixed 14 squares of fixed size, which made it deterministic
        // (good — a thumbnail must never flicker between rebuilds) and simultaneously IDENTICAL for every art-less
        // spec (bad — the browser showed a wall of the same square scatter, varying only by tint). Both properties
        // came from the same constant, so the fix is to derive the constants from the SPEC instead of dropping them:
        // the seed from the asset's own name, and the scatter itself from the emission dials the burst will really
        // use — how many chunks, how big, and in which direction and cone they fly. Two different specs therefore
        // scatter differently while one spec stays byte-stable across rebuilds. Caller owns / destroys the result.
        Texture2D ProceduralSwatch()
        {
            const int size = 32;
            var g = colorOverLife ?? DefaultColorGradient();
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            var rng = new System.Random(SwatchSeed());
            const float half = (size - 1) * 0.5f;

            // A composed effect that reached this far (blasts/fragments configured, but nothing that could render
            // itself) gets a soft glow behind the scatter, so it never reads as the same plain debris burst as an
            // unconfigured spec. It says "there is more here than debris" without pretending to BE the blast.
            if (IsComposedEffect)
            {
                var glow = g.Evaluate(0.35f);
                float rGlow = size * 0.44f;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half));
                    if (d > rGlow) continue;
                    px[y * size + x] = new Color(glow.r, glow.g, glow.b, Mathf.Lerp(0.32f, 0f, d / rGlow));
                }
            }

            // Clamped to at least 3 even for a zero-count spec (a pure-blast recipe emits no debris at all):
            // "never blank" outranks "numerically faithful", and the glow above is what carries that spec's read.
            int pieces = Mathf.Clamp(Mathf.RoundToInt((countMin + countMax) * 0.5f), 3, 24);
            float cone = Mathf.Clamp(spreadDeg, 0f, 180f);
            float maxR = size * 0.34f;

            for (int n = 0; n < pieces; n++)
            {
                float t = pieces > 1 ? n / (float)(pieces - 1) : 0f;  // walk the gradient across the scatter
                var c = g.Evaluate(t);
                c.a = 1f;   // an all-transparent gradient would otherwise draw an invisible scatter — blank again.

                float ang = (directionDeg + ((float)rng.NextDouble() * 2f - 1f) * cone) * Mathf.Deg2Rad;
                float rad = maxR * (0.25f + 0.75f * (float)rng.NextDouble());
                // Chunk size is authored in world units, so map the spec's own range onto a pixel side: a spec
                // throwing 0.5-unit slabs reads as chunky, one throwing 0.06-unit sparks reads as fine grit.
                float world = Mathf.Lerp(sizeMin, sizeMax, (float)rng.NextDouble());
                int side = Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(1.5f, 7f, Mathf.InverseLerp(0.05f, 0.5f, world))), 1, 7);

                int x0 = Mathf.Clamp(Mathf.RoundToInt(half + Mathf.Cos(ang) * rad) - side / 2, 0, size - side);
                int y0 = Mathf.Clamp(Mathf.RoundToInt(half + Mathf.Sin(ang) * rad) - side / 2, 0, size - side);
                for (int y = y0; y < y0 + side; y++)
                for (int x = x0; x < x0 + side; x++)
                    px[y * size + x] = c;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// Stable per-spec seed for the swatch. Deliberately NOT string.GetHashCode(): that is randomised per
        /// process on some .NET runtimes, which would reshuffle a thumbnail between editor sessions — the exact
        /// flicker the old fixed seed existed to prevent. This is plain FNV-1a over the asset's own name, so the
        /// same name always yields the same scatter, and renaming the asset (a deliberate act, not a rebuild) is
        /// the only thing that changes it.
        int SwatchSeed()
        {
            string key = string.IsNullOrEmpty(name) ? "ChunkSpec" : name;
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < key.Length; i++) { h ^= key[i]; h *= 16777619u; }
                return (int)(h & 0x7fffffff);   // masked positive: System.Random(int.MinValue) is a trap.
            }
        }

        /// True when this spec is more than a debris burst — any Chunks 2.0 module that produces visible content
        /// is switched on. Used only to decide how the fallback swatch reads; it changes no behaviour.
        bool IsComposedEffect =>
            (fragmentSlicer != null && fragmentSlicer.enabled) ||
            (particleSplash != null && particleSplash.enabled) ||
            (spawnFormation != null && spawnFormation.enabled) ||
            (pyreSpawn != null && pyreSpawn.enabled) ||
            (blastGroups != null && blastGroups.Count > 0);

        // The animated trio MUST resolve through the same ResolvePreviewSource as RenderPreviewTexture. When they
        // disagree, a browser that animates its thumbnails shows one asset's still frame with another asset's
        // motion painted over it — and worse, an implementation like Pyre's writes frames straight into the
        // texture with SetPixels32, which throws outright if the still it is animating came from somewhere else
        // and is a different size. One resolver, four callers, no second precedence.
        public bool CanAnimatePreview
        {
            get { ResolvePreviewSource(out var animated, out _); return animated != null && animated.CanAnimatePreview; }
        }

        public float PreviewFps
        {
            get { ResolvePreviewSource(out var animated, out _); return animated != null ? animated.PreviewFps : 12f; }
        }

        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            if (tex == null) return;
            ResolvePreviewSource(out var animated, out _);
            animated?.UpdateAnimatedPreview(tex, time);
        }

        // Crop a Sprite to a fresh Texture2D (its source texture must be Read/Write-enabled — otherwise null, and the
        // caller falls back to the generic icon). Caller owns / destroys the result, per IVisualPreview.
        static Texture2D SpriteToTexture(Sprite s)
        {
            if (s == null || s.texture == null || !s.texture.isReadable) return null;
            var r = s.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            if (w <= 0 || h <= 0) return null;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels(s.texture.GetPixels(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), w, h));
            tex.Apply();
            return tex;
        }

        void OnValidate()
        {
            countMin = Mathf.Max(0, countMin);
            countMax = Mathf.Max(countMin, countMax);
            speedMin = Mathf.Max(0f, speedMin);
            speedMax = Mathf.Max(speedMin, speedMax);
            spreadDeg = Mathf.Clamp(spreadDeg, 0f, 180f);
            gravity = Mathf.Max(0f, gravity);
            drag = Mathf.Clamp(drag, 0f, 20f);
            angularSpeedMin = Mathf.Max(0f, angularSpeedMin);
            angularSpeedMax = Mathf.Max(angularSpeedMin, angularSpeedMax);
            lifeMin = Mathf.Max(0.01f, lifeMin);
            lifeMax = Mathf.Max(lifeMin, lifeMax);
            sizeMin = Mathf.Max(0.001f, sizeMin);
            sizeMax = Mathf.Max(sizeMin, sizeMax);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            bounciness = Mathf.Clamp01(bounciness);
            floorFriction = Mathf.Clamp01(floorFriction);
            samplePxMin = Mathf.Max(1, samplePxMin);
            samplePxMax = Mathf.Max(samplePxMin, samplePxMax);
            tumbleSpeedMin = Mathf.Max(0f, tumbleSpeedMin);
            tumbleSpeedMax = Mathf.Max(tumbleSpeedMin, tumbleSpeedMax);
            tumbleShadeStrength = Mathf.Clamp01(tumbleShadeStrength);
            tintStrength = Mathf.Clamp01(tintStrength);
            edgeThicknessPx = Mathf.Max(1, edgeThicknessPx);
            hitDamage = Mathf.Max(0f, hitDamage);
            hitRadiusScale = Mathf.Clamp(hitRadiusScale, 0.1f, 3f);
            trailInterval = Mathf.Max(0.01f, trailInterval);
            // A spec serialized before a module existed deserializes that field as null; every consumer
            // treats "null module" as "off", but re-creating it here is what lets the editor bind to it.
            particleSplash ??= new ParticleSplashModule();
            pyreSpawn ??= new PyreSpawnModule();
            pyreMotion ??= new PyreMotionModule();
            fragmentSlicer ??= new FragmentSlicerModule();
            spawnFormation ??= new SpawnFormationModule();
            layers ??= new LayerSpec();
            timeline ??= new ChunkTimeline();
            sizeOverLife ??= DefaultSizeCurve();
            alphaOverLife ??= DefaultAlphaCurve();
            colorOverLife ??= DefaultColorGradient();
        }

        // ── default curves/gradient (also used as field initialisers so CreateInstance assets look good too) ──
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
