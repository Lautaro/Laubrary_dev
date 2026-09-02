using System.Collections.Generic;
using UnityEngine;
using Laubrary.SpriteFx;
using Laubrary.PreviewKit;
using Laubrary.Layering;

namespace Laubrary.Chunks
{
    /// A chunk recipe: an ordered STACK of capabilities that together make one composed effect. Each
    /// capability is one authored unit with its own fields, its own place on the recipe's clock and its own
    /// contribution to what appears — so a recipe shows only the surfaces its own stack brings, and "play one
    /// blast" is not a Chunks job at all (reference the blast directly). Chunks exists to COMPOSE or VARY:
    /// several blasts in a pattern, blasts that fly, a sprite fractured and flung, a palette spray off a
    /// character, all on one clock with cues.
    ///
    /// It ships ZERO assets: debris with no art throws a procedural pixel-square tinted by its own gradient.
    /// Hand it to a <see cref="ChunkEmitter"/> or to the static <c>Chunks.Burst</c> API.
    [CreateAssetMenu(menuName = "Laubrary/Chunks/Chunk Spec", fileName = "Chunks")]
    public class ChunkSpec : ScriptableObject, IVisualPreview
    {
        /// The layout this class writes. Bumped only when an upgrade routine exists to reach it.
        public const int CurrentSchemaVersion = 1;

        // Deliberately initialised to the LEGACY value, not the current one. Unity runs field initialisers
        // before applying serialized data, so a field absent from an old asset's YAML keeps whatever the
        // initialiser gave it — meaning any default other than 0 would tell the upgrade that assets authored
        // before this field existed were already current, and quietly strand every one of them.
        [HideInInspector] public int schemaVersion = 0;

        [Tooltip("The recipe, in authored order. Each entry is one capability with its own dials and its own " +
                 "moment on the clock.")]
        [SerializeReference] public List<ChunkCapability> capabilities = new List<ChunkCapability>();

        [Tooltip("The composition's own aim: the direction producers that inherit the burst direction fire " +
                 "along, unless the caller passes one for a particular burst.")]
        public float directionDeg = 90f;

        // ── legacy layout (schemaVersion 0) ───────────────────────────────────────────────────────────────
        // The fixed module slots a recipe used to be. Hidden, never edited, and read by exactly one thing: the
        // one-time upgrade in UpgradeIfNeeded. They stay for one release so an asset that has not been re-saved
        // yet still carries everything it said, then come out with Legacy/ChunkSpecLegacy.cs.
        [HideInInspector] public int countMin = 8;
        [HideInInspector] public int countMax = 16;
        [HideInInspector] public float speedMin = 3f;
        [HideInInspector] public float speedMax = 7f;
        [HideInInspector] public float spreadDeg = 180f;
        [HideInInspector] public float upwardBias = 1.5f;
        [HideInInspector] public float gravity = 20f;
        [HideInInspector] public float drag = 0.6f;
        [HideInInspector] public float angularSpeedMin = 90f;
        [HideInInspector] public float angularSpeedMax = 540f;
        [HideInInspector] public bool faceVelocity = false;
        [HideInInspector] public float lifeMin = 0.6f;
        [HideInInspector] public float lifeMax = 1.1f;
        [HideInInspector] public float sizeMin = 0.08f;
        [HideInInspector] public float sizeMax = 0.18f;
        [HideInInspector] public AnimationCurve sizeOverLife = AnimationCurve.Constant(0f, 1f, 1f);
        [HideInInspector] public AnimationCurve alphaOverLife = LegacyAlphaCurve();
        [HideInInspector] public Gradient colorOverLife = LegacyColorGradient();
        [HideInInspector] public List<Sprite> sprites = new List<Sprite>();
        [HideInInspector] public float pixelsPerUnit = 32f;
        [HideInInspector] public bool useFloor = true;
        [HideInInspector] public float floorY = 0f;
        [HideInInspector] public float bounciness = 0.35f;
        [HideInInspector] public float floorFriction = 0.5f;
        [HideInInspector] public bool restOnFloor = true;
        [HideInInspector] public Sprite sampleSource;
        [HideInInspector] public int samplePxMin = 5;
        [HideInInspector] public int samplePxMax = 20;
        [HideInInspector] public bool tumble = true;
        [HideInInspector] public float tumbleSpeedMin = 180f;
        [HideInInspector] public float tumbleSpeedMax = 720f;
        [HideInInspector] public float tumbleShadeStrength = 0.6f;
        [HideInInspector] public ChunkTintMode tintMode = ChunkTintMode.None;
        [HideInInspector] public Color tintColor = new Color(1f, 0.35f, 0.08f, 1f);
        [HideInInspector] public float tintStrength = 0.6f;
        [HideInInspector] public int edgeThicknessPx = 1;
        [HideInInspector] [SerializeReference] public List<PixelModifier> modifiers = new List<PixelModifier>();
        [HideInInspector] public Object animationSource;
        [HideInInspector] public ParticleSplashModule particleSplash = new ParticleSplashModule();
        [HideInInspector] public PyreSpawnModule pyreSpawn = new PyreSpawnModule();
        [HideInInspector] public PyreMotionModule pyreMotion = new PyreMotionModule();
        [HideInInspector] public FragmentSlicerModule fragmentSlicer = new FragmentSlicerModule();
        [HideInInspector] public SpawnFormationModule spawnFormation = new SpawnFormationModule();
        [HideInInspector] public List<PyreSpawnModule> blastGroups = new List<PyreSpawnModule>();
        [HideInInspector] public LayerSpec layers = new LayerSpec();
        [HideInInspector] public ChunkTimeline timeline = new ChunkTimeline();
        [HideInInspector] public bool useHitDetection = false;
        [HideInInspector] public float hitDamage = 5f;
        [HideInInspector] public float hitRadiusScale = 0.5f;
        [HideInInspector] public Object trailSource;
        [HideInInspector] public float trailInterval = 0.08f;

        // ── the stack, read ───────────────────────────────────────────────────────────────────────────────

        /// The first enabled capability of a kind, or null. What "the recipe's debris" means when something
        /// needs one without caring which.
        public T FirstEnabled<T>() where T : ChunkCapability
        {
            if (capabilities == null) return null;
            for (int i = 0; i < capabilities.Count; i++)
                if (capabilities[i] is T t && t.enabled) return t;
            return null;
        }

        /// Whether the stack holds an enabled capability of a kind.
        public bool Has<T>() where T : ChunkCapability => FirstEnabled<T>() != null;

        /// The modifier of a kind that acts on <paramref name="producer"/>: one aimed at it by id wins over
        /// one aimed at everything, so a recipe can give one producer its own trail and leave the rest on the
        /// general one. Null when nothing applies.
        public T FindModifier<T>(ChunkCapability producer) where T : ChunkModifier
        {
            if (capabilities == null || producer == null) return null;
            T general = null;
            for (int i = 0; i < capabilities.Count; i++)
            {
                if (!(capabilities[i] is T m) || !m.Targets(producer)) continue;
                if (!string.IsNullOrEmpty(m.targetId)) return m;
                general ??= m;
            }
            return general;
        }

        /// The layer stack this recipe's output is ordered by: the Layer Plan's own, or an empty one whose
        /// resolver degrades every slot to the emitter's flat order. Never null, so no caller needs the
        /// "layering is optional" branch.
        public LayerSpec ResolveLayers()
        {
            var plan = FirstEnabled<LayerPlan>();
            return plan != null && plan.layers != null ? plan.layers : EmptyLayers;
        }

        static readonly LayerSpec EmptyLayers = new LayerSpec();

        /// Seconds from start to the moment the last thing this recipe produces is gone.
        public float ClockLength => ChunkClock.Length(this);

        // ── the one-time upgrade ──────────────────────────────────────────────────────────────────────────

        /// Turns a legacy fixed-slot recipe into a capability stack, once. Idempotent and safe to call from
        /// anywhere: it does nothing at all once <see cref="schemaVersion"/> is current.
        ///
        /// It runs IN MEMORY on load so a build and a play session behave correctly whether or not the asset
        /// on disk has been re-saved; the editor separately walks every ChunkSpec and saves the result, so the
        /// upgrade is paid once rather than on every load forever. Returns true when it actually changed
        /// something, which is what tells the editor pass an asset is worth saving.
        /// True when this object was upgraded in memory and the FILE is therefore still behind. It is the only
        /// honest way to ask that question: loading the asset already ran the upgrade, so reading
        /// <see cref="schemaVersion"/> back afterwards reports every recipe as current whether or not anything
        /// was ever written. Not serialized — it describes this load, not the recipe.
        [System.NonSerialized] bool _upgradedInMemory;
        public bool NeedsSaving => _upgradedInMemory;

        public bool UpgradeIfNeeded()
        {
            capabilities ??= new List<ChunkCapability>();
            if (schemaVersion >= CurrentSchemaVersion)
            {
                if (!EnsureIds()) return false;
                _upgradedInMemory = true;
                return true;
            }

            // A recipe that says "legacy" but already carries a stack has been through this once — by an
            // earlier load, or by hand. Appending would give it every capability twice, so it is stamped
            // current and left exactly as it is: an existing stack is always the truer of the two.
            if (capabilities.Count > 0)
            {
                schemaVersion = CurrentSchemaVersion;
                EnsureIds();
                _upgradedInMemory = true;
                return true;
            }

            // Order matters: it is the stack order the author will read, and with no Layer Plan it is also the
            // draw order. Coordinator first, then producers back-to-front as the old dispatch fired them, then
            // the modifiers that decorate them, then the cues.
            if (layers != null && layers.Count > 0)
                Add(new LayerPlan { layers = layers, displayName = "Layers" });

            var debris = MigrateDebris();
            if (debris != null) Add(debris);

            if (particleSplash != null && particleSplash.enabled) Add(MigrateSplash());
            if (fragmentSlicer != null && fragmentSlicer.enabled) Add(MigrateFracture());

            // A formation SUPERSEDED the plain spawner when both were on — a formation IS a set of spawns, so
            // running both doubled every blast at the origin. Migrating both would resurrect exactly that bug,
            // so the same precedence decides which single blast the first slot becomes.
            PyreBlast firstBlast = null;
            if (spawnFormation != null && spawnFormation.enabled)
                firstBlast = MigrateBlast(pyreSpawn, spawnFormation.formation, spawnFormation.layerName,
                                          LegacyTrack(LegacyFormationTrack));
            else if (pyreSpawn != null && pyreSpawn.enabled)
                firstBlast = MigrateBlast(pyreSpawn, pyreSpawn.useFormation ? pyreSpawn.formation : null,
                                          pyreSpawn.layerName, LegacyTrack(LegacyPyreSpawnTrack));
            if (firstBlast != null) Add(firstBlast);

            if (blastGroups != null)
                for (int i = 0; i < blastGroups.Count; i++)
                {
                    var g = blastGroups[i];
                    if (g == null || !g.enabled) continue;
                    var blast = MigrateBlast(g, g.useFormation ? g.formation : null, g.layerName,
                                             LegacyTrack("Blast " + (i + 2)));
                    if (!string.IsNullOrEmpty(g.label)) blast.displayName = g.label;
                    Add(blast);
                }

            // The old modules had no notion of a target and applied to everything, so every migrated modifier
            // keeps an empty targetId — which means exactly that.
            if (pyreMotion != null && pyreMotion.enabled) Add(MigrateTrajectory());
            if (trailSource != null) Add(new Trail { trailSource = trailSource, interval = trailInterval });
            if (useHitDetection) Add(new Hits { damage = hitDamage, radiusScale = hitRadiusScale });

            if (timeline != null && timeline.enabled && timeline.markers != null && timeline.markers.Count > 0)
                Add(new Cues { cues = new List<ChunkCue>(timeline.markers) });

            schemaVersion = CurrentSchemaVersion;
            EnsureIds();
            _upgradedInMemory = true;
            return true;
        }

        /// Called once the upgraded form has actually been written to disk.
        public void MarkSaved() => _upgradedInMemory = false;

        /// Mints an id for anything in the stack that has none (a capability built in code, an asset saved
        /// before ids existed). True when it changed something.
        public bool EnsureIds()
        {
            bool changed = false;
            if (capabilities == null) return false;
            for (int i = 0; i < capabilities.Count; i++)
            {
                var c = capabilities[i];
                if (c == null || !string.IsNullOrEmpty(c.id)) continue;
                c.EnsureId();
                changed = true;
            }
            return changed;
        }

        void Add(ChunkCapability capability)
        {
            if (capability == null) return;
            capability.EnsureId();
            capabilities.Add(capability);
        }

        // The lane names the old timeline scheduled by. Only the upgrade still needs them; a lane is now a
        // capability's own delay, which no name can go stale against.
        const string LegacySplashTrack = "Splash";
        const string LegacyPyreSpawnTrack = "Pyre Spawn";
        const string LegacyFormationTrack = "Spawn Formation";
        const string LegacyFragmentsTrack = "Fragments";

        float LegacyTrack(string name) => timeline != null ? timeline.DelayFor(name) : 0f;

        /// The legacy debris fields become a capability only when they would actually have thrown something —
        /// a pure-blast recipe authored with count 0 must not gain a debris card it never had.
        DebrisScatter MigrateDebris()
        {
            bool throwsAnything = countMax > 0 || (sprites != null && sprites.Count > 0)
                                  || sampleSource != null || animationSource != null;
            if (!throwsAnything) return null;

            var d = new DebrisScatter
            {
                visual = animationSource != null ? DebrisVisual.Animated
                       : sampleSource != null ? DebrisVisual.Sampled
                       : (sprites != null && sprites.Count > 0) ? DebrisVisual.Sprites
                       : DebrisVisual.Squares,
                sprites = sprites != null ? new List<Sprite>(sprites) : new List<Sprite>(),
                pixelsPerUnit = pixelsPerUnit,
                sampleSource = sampleSource,
                samplePxMin = samplePxMin, samplePxMax = samplePxMax,
                tumble = tumble, tumbleSpeedMin = tumbleSpeedMin, tumbleSpeedMax = tumbleSpeedMax,
                tumbleShadeStrength = tumbleShadeStrength,
                tintMode = tintMode, tintColor = tintColor, tintStrength = tintStrength,
                edgeThicknessPx = edgeThicknessPx,
                modifiers = modifiers != null ? new List<PixelModifier>(modifiers) : new List<PixelModifier>(),
                animationSource = animationSource,
                countMin = countMin, countMax = countMax,
                speedMin = speedMin, speedMax = speedMax,
                spreadDeg = spreadDeg, upwardBias = upwardBias,
                gravity = gravity, drag = drag,
                angularSpeedMin = angularSpeedMin, angularSpeedMax = angularSpeedMax,
                faceVelocity = faceVelocity,
                lifeMin = lifeMin, lifeMax = lifeMax,
                sizeMin = sizeMin, sizeMax = sizeMax,
                sizeOverLife = sizeOverLife, alphaOverLife = alphaOverLife, colorOverLife = colorOverLife,
                useFloor = useFloor, floorY = floorY, bounciness = bounciness,
                floorFriction = floorFriction, restOnFloor = restOnFloor,
                // The old debris had no layer slot of its own — it took the emitter's flat order — and an
                // empty slot is exactly how the stack says that.
                layerName = "",
            };
            return d;
        }

        PaletteSplash MigrateSplash() => new PaletteSplash
        {
            delay = LegacyTrack(LegacySplashTrack),
            layerName = particleSplash.layerName,
            sprite = particleSplash.sprite,
            emitFromFootprint = particleSplash.emitFromFootprint,
            countMin = particleSplash.countMin, countMax = particleSplash.countMax,
            sizePxMin = particleSplash.sizePxMin, sizePxMax = particleSplash.sizePxMax,
            pixelsPerUnit = pixelsPerUnit,
            speedMin = particleSplash.speedMin, speedMax = particleSplash.speedMax,
            inheritBurstDirection = particleSplash.inheritBurstDirection,
            directionDeg = particleSplash.directionDeg, spreadDeg = particleSplash.spreadDeg,
            gravity = particleSplash.gravity, drag = particleSplash.drag,
            lifeMin = particleSplash.lifeMin, lifeMax = particleSplash.lifeMax,
            alphaOverLife = particleSplash.alphaOverLife,
            seed = particleSplash.seed,
        };

        FragmentFracture MigrateFracture() => new FragmentFracture
        {
            delay = LegacyTrack(LegacyFragmentsTrack),
            layerName = fragmentSlicer.layerName,
            // The slicer's own source could be empty and fall through to the spec's sample source at fire
            // time. That fallback has nowhere to live once debris owns its own art, so it is resolved HERE,
            // once, into the field it always meant — otherwise a migrated recipe would cut nothing.
            sourceVisual = fragmentSlicer.sourceVisual,
            source = fragmentSlicer.source != null ? fragmentSlicer.source
                   : (fragmentSlicer.sourceVisual == null ? sampleSource : null),
            pieceCount = fragmentSlicer.pieceCount,
            minPieceAreaPx = fragmentSlicer.minPieceAreaPx,
            seed = fragmentSlicer.seed,
            speedMin = fragmentSlicer.speedMin, speedMax = fragmentSlicer.speedMax,
            useBurstDirection = fragmentSlicer.useBurstDirection,
            directionDeg = fragmentSlicer.directionDeg, spreadDeg = fragmentSlicer.spreadDeg,
            gravity = fragmentSlicer.gravity, drag = fragmentSlicer.drag,
            angularSpeedMin = fragmentSlicer.angularSpeedMin, angularSpeedMax = fragmentSlicer.angularSpeedMax,
            lifeMin = fragmentSlicer.lifeMin, lifeMax = fragmentSlicer.lifeMax,
            alphaOverLife = fragmentSlicer.alphaOverLife,
        };

        PyreBlast MigrateBlast(PyreSpawnModule picking, SpawnFormation formation, string layerName, float delay)
        {
            var blast = new PyreBlast
            {
                delay = delay,
                layerName = layerName ?? "",
                source = picking != null ? picking.source : null,
                pool = picking != null && picking.pool != null ? new List<Object>(picking.pool) : new List<Object>(),
                offset = picking != null ? picking.offset : Vector2.zero,
                rotationMode = picking != null ? picking.rotationMode : PyreSpawnRotation.InheritBurst,
                fixedAngleDeg = picking != null ? picking.fixedAngleDeg : 0f,
                randomAngleMinDeg = picking != null ? picking.randomAngleMinDeg : 0f,
                randomAngleMaxDeg = picking != null ? picking.randomAngleMaxDeg : 360f,
                scaleMin = picking != null ? picking.scaleMin : 1f,
                scaleMax = picking != null ? picking.scaleMax : 1f,
                seed = picking != null ? picking.seed : 0,
            };
            if (formation != null)
            {
                blast.useFormation = true;
                blast.formation = formation;
            }
            return blast;
        }

        Trajectory MigrateTrajectory() => new Trajectory
        {
            speedMin = pyreMotion.speedMin, speedMax = pyreMotion.speedMax,
            inheritBurstDirection = pyreMotion.inheritBurstDirection,
            directionDeg = pyreMotion.directionDeg, spreadDeg = pyreMotion.spreadDeg,
            upwardBias = pyreMotion.upwardBias,
            gravity = pyreMotion.gravity, drag = pyreMotion.drag,
            faceVelocity = pyreMotion.faceVelocity,
            untilTargetEnds = pyreMotion.untilTargetEnds, lifeSeconds = pyreMotion.lifeSeconds,
            seed = pyreMotion.seed,
        };

        void OnEnable() => UpgradeIfNeeded();

        void OnValidate()
        {
            UpgradeIfNeeded();
            if (capabilities == null) return;
            for (int i = 0; i < capabilities.Count; i++)
            {
                var c = capabilities[i];
                if (c == null) continue;
                c.EnsureId();
                c.delay = Mathf.Max(0f, c.delay);
            }
        }

        // ── IVisualPreview: a thumbnail for LauAsset pickers / browsers ────────────────────────────────────
        //
        // The question a thumbnail answers is "what IS this?", and for a composed recipe the answer is almost
        // never the debris. Resolving it through ONE method that the animated trio below shares is not tidiness:
        // when they disagree, a browser that animates its thumbnails shows one asset's still frame with another
        // asset's motion painted over it — and an implementation like Pyre's writes frames straight into the
        // texture with SetPixels32, which throws outright when the still came from somewhere else and is a
        // different size. One resolver, four callers, no second precedence.
        public Texture2D RenderPreviewTexture()
        {
            ResolvePreviewSource(out var animated, out var still);

            // A source that renders nothing (an unbuilt Zoe, a zero-frame blast) is NOT allowed to leave a
            // blank square behind: fall through to the still, and past it to the swatch, exactly as if it had
            // never been picked.
            if (animated != null)
            {
                var t = animated.RenderPreviewTexture();
                if (t != null) return t;
            }
            return SpriteToTexture(still) ?? ProceduralSwatch();
        }

        /// The ONE place a recipe's visual identity is decided. Sets an `animated` delegate (an asset that
        /// renders, and may animate, its own preview) and/or a `still` sprite to crop; both stay null when the
        /// recipe points at no art at all, which is the swatch's cue.
        ///
        /// Precedence walks the stack in AUTHORED order and takes the first capability that can answer, which
        /// is the honest reading of "what is this?": the author put the thing the recipe is about where they
        /// wanted it read. Within one capability the order is most-identifying first — what a fracture cuts
        /// beats the blast going off behind it, and animated content beats a still because it can also animate
        /// the thumbnail.
        ///
        /// Nothing here is random: a blast pool deliberately does NOT roll a die over its entries, because a
        /// thumbnail that flickers between rebuilds is worse than one that only ever shows the first entry.
        void ResolvePreviewSource(out IVisualPreview animated, out Sprite still)
        {
            animated = null;
            still = null;
            if (capabilities == null) return;

            for (int i = 0; i < capabilities.Count; i++)
            {
                var c = capabilities[i];
                if (c == null || !c.enabled) continue;

                if (c is DebrisScatter debris)
                {
                    // `!= null` before every cast: a DESTROYED asset is only fake-null through Unity's ==
                    // overload, never through `is`, so casting first would happily return a dead object.
                    if (debris.animationSource != null && debris.AnimationSource is IVisualPreview anim)
                    { animated = anim; return; }
                    if (debris.sampleSource != null) { still = debris.sampleSource; return; }
                    if (debris.sprites != null)
                        for (int s = 0; s < debris.sprites.Count; s++)
                            if (debris.sprites[s] != null) { still = debris.sprites[s]; return; }
                    continue;   // an art-less debris capability answers nothing; the next one may
                }

                if (c is FragmentFracture fracture)
                {
                    var cut = fracture.ResolveSource();
                    if (fracture.sourceVisual != null && fracture.sourceVisual is IVisualPreview cutVp)
                    { animated = cutVp; still = cut; return; }
                    if (cut != null) { still = cut; return; }
                    continue;
                }

                if (c is PaletteSplash splash && splash.sprite != null) { still = splash.sprite; return; }

                if (c is PyreBlast blast)
                {
                    var asset = BlastAssetOf(blast);
                    if (asset != null && asset is IVisualPreview blastVp) { animated = blastVp; return; }
                }
            }
        }

        static Object BlastAssetOf(PyreBlast blast)
        {
            if (blast == null) return null;
            if (blast.source != null) return blast.source;
            if (blast.pool != null)
                for (int i = 0; i < blast.pool.Count; i++)
                    if (blast.pool[i] != null) return blast.pool[i];
            return null;
        }

        // A recipe with no authored art is not a recipe with no LOOK: it throws procedural pixel-squares tinted
        // along its debris gradient, so that gradient IS its identity. Returning null here leaves a blank square
        // in every browser and picker, which reads as "this has a picture and it failed to load" — a lie, and the
        // one thing the thumbnail rules forbid outright. Draw what the debris actually looks like instead.
        //
        // Both properties a thumbnail needs pull against each other and both come from the same constants: it
        // must be deterministic (never flicker between rebuilds) and it must not be IDENTICAL for every art-less
        // recipe (a browser full of the same scatter, varying only by tint). So the constants are derived from
        // the recipe itself — the seed from its own name, the scatter from the emission dials it will really use.
        // Caller owns / destroys the result.
        Texture2D ProceduralSwatch()
        {
            const int size = 32;
            var debris = FirstEnabled<DebrisScatter>();
            var g = debris?.colorOverLife ?? LegacyColorGradient();
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            var rng = new ChunkRng(SwatchSeed());
            const float half = (size - 1) * 0.5f;

            // A composed recipe that reached this far (blasts or fractures configured, but nothing that could
            // render itself) gets a soft glow behind the scatter, so it never reads as the same plain debris as
            // an unconfigured one. It says "there is more here than debris" without pretending to BE the blast.
            if (Has<PyreBlast>() || Has<FragmentFracture>())
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

            // Clamped to at least 3 even for a recipe that throws no debris at all: "never blank" outranks
            // "numerically faithful", and the glow above is what carries that recipe's read.
            int pieces = Mathf.Clamp(
                Mathf.RoundToInt(debris != null ? (debris.countMin + debris.countMax) * 0.5f : 0f), 3, 24);
            float cone = Mathf.Clamp(debris != null ? debris.spreadDeg : 180f, 0f, 180f);
            float lo = debris != null ? debris.sizeMin : 0.08f;
            float hi = debris != null ? debris.sizeMax : 0.18f;
            float maxR = size * 0.34f;

            for (int n = 0; n < pieces; n++)
            {
                float t = pieces > 1 ? n / (float)(pieces - 1) : 0f;   // walk the gradient across the scatter
                var c = g.Evaluate(t);
                c.a = 1f;   // an all-transparent gradient would otherwise draw an invisible scatter — blank again

                float ang = (directionDeg + (rng.Next01() * 2f - 1f) * cone) * Mathf.Deg2Rad;
                float rad = maxR * (0.25f + 0.75f * rng.Next01());
                // Chunk size is authored in world units, so map the range onto a pixel side: a recipe throwing
                // 0.5-unit slabs reads as chunky, one throwing 0.06-unit sparks reads as fine grit.
                float world = Mathf.Lerp(lo, hi, rng.Next01());
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

        /// Stable per-recipe seed for the swatch. Deliberately NOT string.GetHashCode(): that is randomised per
        /// process on some .NET runtimes, which would reshuffle a thumbnail between editor sessions — the exact
        /// flicker a stable seed exists to prevent. Plain FNV-1a over the asset's own name, so renaming (a
        /// deliberate act, not a rebuild) is the only thing that changes it.
        int SwatchSeed()
        {
            string key = string.IsNullOrEmpty(name) ? "ChunkSpec" : name;
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < key.Length; i++) { h ^= key[i]; h *= 16777619u; }
                return (int)(h & 0x7fffffff);
            }
        }

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

        /// Crop a Sprite to a fresh Texture2D (its source texture must be Read/Write-enabled — otherwise null,
        /// and the caller falls back to the generic icon). Caller owns / destroys the result.
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

        static AnimationCurve LegacyAlphaCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.65f, 1f), new Keyframe(1f, 0f));

        static Gradient LegacyColorGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
