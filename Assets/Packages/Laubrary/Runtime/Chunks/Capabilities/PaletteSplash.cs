using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Laubrary.PixelScale;

namespace Laubrary.Chunks
{
    /// What a splash particle is made of. Named for the LOOK, not the mechanism, and deliberately shaped like
    /// <see cref="DebrisVisual"/> — a mode ON the capability rather than a second capability, so the whole
    /// source waterfall, flight, life, seed, layer slot and editor card stay written once.
    public enum SplashParticleLook
    {
        /// The original: a tiny procedural pixel shard from <see cref="ChunkSprites"/>, tinted with ONE colour
        /// sampled off the source. Ships no art and costs no texture. The default, forever — everything
        /// authored before sampled crops existed is this.
        Shards = 0,
        /// A real little rectangle of the source art itself, cut out at spawn and flung with every one of its
        /// own pixels intact — the "a patch of pixels came loose" look.
        SampledCrops = 1,
    }

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
    ///
    /// ── The bullet-hole mode (T-0394) ────────────────────────────────────────────────────────────────────
    /// Three independent opt-ins sit on top of that, every one of them OFF by default so an existing splash is
    /// byte-identical: <see cref="particleLook"/> = SampledCrops flings real cut-outs of the art instead of
    /// tinted shards; <see cref="maskCropEdges"/> eats each cut's rectangle away toward transparency;
    /// <see cref="useHitRadius"/> takes pixels only from a disc on the art rather than from anywhere in its
    /// footprint; and <see cref="tintCropEdges"/> scorches each cut's rim with a named ramp.
    ///
    /// ⚠ WHERE THE HIT IS, stated plainly because it is an approximation and the card says so too. This
    /// capability has always laid the source sprite out with its PIVOT at the burst's own origin — that is what
    /// <see cref="emitFromFootprint"/> has always meant, and nothing in <see cref="ChunkModuleContext"/> says
    /// where the source sprite actually sits in the world. So the hit point in the ART's own space is the pivot
    /// plus <see cref="hitOffsetPx"/>, which the author sets; it is NOT derived from the burst position. What
    /// IS derived from the burst position is where the spray comes out: with a hit radius on, a particle's spawn
    /// offset is measured from the DISC'S CENTRE, not the pivot, so the patch of pixels always flies out of the
    /// burst origin — which at runtime is the real hit point a Zoe event resolved. In one sentence:
    /// <see cref="hitOffsetPx"/> chooses WHICH patch of art comes loose, the burst chooses WHERE it flies from.
    /// (The upgrade that would remove the approximation is a hit point in sprite space carried on the context
    /// itself; that is a plumbing change across the emitter and every caller, deliberately not done here.)
    ///
    /// ⚠ A disc that catches no opaque pixels — fully off the art, or over a hole — does not fire an empty
    /// burst: it falls back to the whole opaque footprint, which is exactly what the splash did before the disc
    /// existed. Same "never silently no-op" floor as the source waterfall above, and
    /// <see cref="CountPixelsInRadius"/> is what lets the editor card say so out loud before anyone plays it.
    [System.Serializable]
    public class PaletteSplash : ChunkCapability
    {
        public override string KindName => "Palette Splash";

        // LEGACY (T-0403): the named depth slot this used to pick. Read once, by LayerPlan.MigrateRows, and
        // never written again — depth is authored in the recipe's Depth list now.
        [HideInInspector] public string layerName = "";

        public override string LayerName => layerName;

        public override bool DrawsOutput => true;

        // DepthInstanceCount stays 1: a splash's particles NEVER split across the depth stack (owner's answer
        // to T-0365 Q4). Their count varies per burst and "particle 7" addresses nothing anyone can see.

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

        // ── what a particle is made of ────────────────────────────────────────────────────────────────────
        [Tooltip("What each particle is made of. Shards are the tiny procedural pixel shapes tinted with one " +
                 "sampled colour. Sampled crops are real little cut-outs of the source art itself, flung with " +
                 "all their own detail — the 'a patch of pixels came loose' look. Only the chosen kind's own " +
                 "dials are authored.")]
        public SplashParticleLook particleLook = SplashParticleLook.Shards;

        [Tooltip("Sampled crops only: smallest cut, in SOURCE-texture pixels. A cut is drawn at exactly this " +
                 "many game pixels — its texel count and its on-screen size are the same number by " +
                 "construction, so a crop can never be squashed below its own pixel grid. Anything above 1 " +
                 "comes out as an asymmetric connected blob of pixels within that square, never the square " +
                 "itself.")]
        [Min(1)] public int cropPxMin = 1;
        [Tooltip("Sampled crops only: largest cut, in SOURCE-texture pixels, inclusive. The cut is the square " +
                 "a blob is grown inside, so this is the widest a fragment can get, not how many pixels it " +
                 "will have.")]
        [Min(1)] public int cropPxMax = 4;

        [Tooltip("Sampled crops only: eat each cut further away toward transparency, on top of the asymmetric " +
                 "blob shape every multi-pixel cut already has — rounds it off and thins it out. Off = the " +
                 "blob's own hard pixel edges, byte-identical to no masking at all.")]
        public bool maskCropEdges = false;
        [Range(0f, 1f)]
        [Tooltip("How far in the fade eats. 0 = nothing; low values just round the corners off; 1 leaves " +
                 "barely a dot.")]
        public float edgeMaskStrength = 0.55f;
        [Range(0f, 1f)]
        [Tooltip("How ragged the faded boundary is. 0 = a clean circle; higher values break it up, hashed from " +
                 "each pixel's own coordinates so the same crop masks the same way every run.")]
        public float edgeMaskJitter = 0.4f;

        // ── where on the art the pixels come loose from ───────────────────────────────────────────────────
        [Tooltip("Take pixels only from a disc on the source art, instead of from anywhere in its opaque " +
                 "footprint — the 'the bullet hit HERE and this patch came loose' look. Off = the whole " +
                 "footprint, exactly as before this existed.")]
        public bool useHitRadius = false;
        [Tooltip("Radius of that disc, in SOURCE-texture pixels.")]
        [Min(0.5f)] public float hitRadiusPx = 6f;
        [Tooltip("Where the disc sits on the art, in SOURCE-texture pixels from the sprite's pivot. X right, " +
                 "Y up. Zero centres it on the pivot. The disc may hang off the edge of the art — whatever " +
                 "opaque pixels DO fall inside it are still used.")]
        public Vector2 hitOffsetPx = Vector2.zero;

        // ── scorched / wounded edges ──────────────────────────────────────────────────────────────────────
        [Tooltip("Sampled crops only: blend a colour ramp onto each cut's EDGE pixels, for scorched or wounded " +
                 "edges. Where in the ramp a cut reads from is how far it came from the middle of the hit.")]
        public bool tintCropEdges = false;
        [Tooltip("Which ramp. Rust, Blood and Charred are built in; Custom uses the gradient below.")]
        public ChunkEdgeTintPreset edgeTintPreset = ChunkEdgeTintPreset.Rust;
        [Tooltip("The ramp used when the preset above is Custom. Left = a cut from the dead centre of the hit, " +
                 "right = one from the outer rim. Its ALPHA is a per-position strength on top of the dial below.")]
        public Gradient customEdgeTint = ChunkEdgeTints.Clone(ChunkEdgeTints.Of(ChunkEdgeTintPreset.Rust));
        [Range(0f, 1f)]
        [Tooltip("How far the ramp pulls an edge pixel. 0 = no visible effect, 1 = fully replaced.")]
        public float edgeTintStrength = 0.7f;
        [Min(1)]
        [Tooltip("How many pixels in from a cut's rim count as its edge.")]
        public int edgeTintThicknessPx = 1;

        /// The ramp this splash actually tints with — the authored custom gradient on Custom, else the named
        /// preset. One place so the card's swatch and a real Fire can never disagree about what Rust is.
        public Gradient ResolveEdgeTint() => ChunkEdgeTints.Resolve(edgeTintPreset, customEdgeTint);

        [Tooltip("Fewest particles sprayed.")]
        [Min(0)] public int countMin = 6;
        [Tooltip("Most particles sprayed, inclusive.")]
        [Min(0)] public int countMax = 14;

        [Tooltip("Smallest particle, in pixels at the Pixels/Unit below. One is the floor and always will be — " +
                 "a particle smaller than one game pixel is invisible fine noise, not pixel art.")]
        [Min(1f)] public float sizePxMin = 1f;
        [Tooltip("Largest particle, in pixels at the Pixels/Unit below.")]
        [Min(1f)] public float sizePxMax = 3f;

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
        struct PixelSample { public Vector2 offset; public Color32 color; public int px, py; }

        /// Every opaque pixel of source as a world-space offset from its pivot plus that pixel's own colour —
        /// and, since T-0394, the pixel's own coordinates inside the sprite's textureRect, which is what a
        /// sampled CROP centres itself on. The offsets and the coordinates are two views of the same pixel, so
        /// nothing can pick one and cut the other.
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
                    color = c,
                    px = px,
                    py = py,
                });
            }
            return outPixels.Count > 0;
        }

        /// The sprite's own pixels-per-unit, with the same fallback <see cref="TrySamplePixels"/> uses. This is
        /// deliberately NOT <see cref="EffectivePixelsPerUnit"/>: that one is the density a PARTICLE is drawn
        /// at, whereas this converts a dial written in SOURCE-texture pixels (the hit radius, the hit offset)
        /// into the same offset space the pixel scan produced. Mixing the two would make the disc change size
        /// whenever the project's Pixel Scale changed, which has nothing to do with the art it is sitting on.
        static float SourcePpu(Sprite source)
            => source != null && source.pixelsPerUnit > 0f ? source.pixelsPerUnit : 32f;

        /// How many of <paramref name="source"/>'s opaque pixels the hit disc currently covers, and (out) how
        /// many opaque pixels it has in total. -1 for a source that cannot be read at all. Same arithmetic
        /// <see cref="Fire"/> filters with, exposed so the editor card can say "the disc covers 37 of 512
        /// pixels" — or that it covers none and the spray will fall back to the whole footprint — before
        /// anybody has to press play. Answers for the whole footprint when <see cref="useHitRadius"/> is off.
        public int CountPixelsInRadius(Sprite source, out int totalOpaque)
        {
            totalOpaque = 0;
            var scratch = new List<PixelSample>(64);
            if (!TrySamplePixels(source, scratch)) return -1;
            totalOpaque = scratch.Count;
            if (!useHitRadius) return scratch.Count;

            float ppu = SourcePpu(source);
            Vector2 centre = hitOffsetPx / ppu;
            float r = Mathf.Max(0.01f, hitRadiusPx) / ppu;
            float rSq = r * r;
            int n = 0;
            for (int i = 0; i < scratch.Count; i++)
                if ((scratch[i].offset - centre).sqrMagnitude <= rSq) n++;
            return n;
        }

        /// Half the sprite's diagonal, in the same world-offset units the pixel scan produces — what a crop's
        /// distance is measured against when no hit disc is set, so the edge ramp still spans the whole art
        /// (0 at the pivot, ~1 at a far corner) instead of saturating at its first key.
        static float FootprintHalfSpan(Sprite source, float ppu)
        {
            if (source == null) return 1f;
            var r = source.textureRect;
            return 0.5f * Mathf.Sqrt(r.width * r.width + r.height * r.height) / Mathf.Max(0.0001f, ppu);
        }

        /// One sampled crop: a <paramref name="cropPx"/>-wide square of the source's own pixels, centred on the
        /// pixel that was picked, optionally rim-tinted and edge-masked, built at <paramref name="particlePpu"/>
        /// so it lands on screen at exactly cropPx game pixels.
        ///
        /// SQUARE is how much art is READ, not what is flung: SampleAt carves an asymmetric connected blob out
        /// of it (T-0398) whenever cropPx &gt; 1, off <paramref name="cropSeed"/> — the same per-particle seed
        /// that drives the torn-edge jitter, which ChunkRng's two-stream constructor keeps independent of it.
        /// So the crop is still exactly cropPx game pixels wide and a seeded splash still reproduces itself in
        /// the editor preview draw for draw, while no particle is a little square of somebody's sprite.
        ///
        /// The cut is delegated whole to <see cref="SampledChunkSprites.SampleAt"/> rather than reimplemented:
        /// that is where "cut a piece out of the thing that broke" already lives (Debris Scatter's Sampled
        /// visual cuts through the same file), including the rim-vs-interior edge test and the texture build.
        /// The one thing this capability could NOT reuse is the older <see cref="SampledChunkSprites.Sample"/>
        /// entry point, which rolls its own size and position off UnityEngine.Random — a splash is seeded and
        /// its editor preview has to reproduce it draw for draw, so the WHERE and the HOW BIG are decided here,
        /// off this capability's own ChunkRng, and handed over as an explicit rect.
        ///
        /// Null (and the caller falls back to a tinted shard) when the source turns out to be uncuttable.
        Sprite BuildCrop(Sprite source, PixelSample sample, int cropPx, float particlePpu, int cropSeed,
                         Gradient edgeRamp, Vector2 centreOffset, float rampSpan)
        {
            var cut = SampledChunkSprites.CutRectAround(source, sample.px, sample.py, cropPx);
            if (cut.width < 1) return null;

            var mode = ChunkTintMode.None;
            Color rimColour = default;
            float rimStrength = 0f;
            if (edgeRamp != null)
            {
                // WHERE IN THE RAMP: how far this cut came from the middle of the hit (0) versus its rim (1).
                // WHICH PIXELS IT LANDS ON: the cut's own edge — EdgesOnly, the same rim test Debris Scatter's
                // tint uses. Both halves of "crop edge or radius edge?" answered, one each: the radius decides
                // the COLOUR, the crop decides the PIXELS. Tinting by radius position alone would leave whole
                // fragments untouched depending on where they happened to land; tinting a crop's rim with one
                // flat colour would make every fragment equally scorched however far out it came from.
                float t = Mathf.Clamp01((sample.offset - centreOffset).magnitude / Mathf.Max(0.0001f, rampSpan));
                var c = edgeRamp.Evaluate(t);
                rimColour = c;
                // The ramp's own alpha rides as a per-position strength on top of the authored dial, which is
                // what lets a preset fade itself out toward the rim.
                rimStrength = Mathf.Clamp01(edgeTintStrength) * Mathf.Clamp01(c.a);
                if (rimStrength > 0f) mode = ChunkTintMode.EdgesOnly;
            }

            return SampledChunkSprites.SampleAt(
                source, cut, particlePpu,
                mode, rimColour, rimStrength, Mathf.Max(1, edgeTintThicknessPx),
                maskCropEdges ? edgeMaskStrength : 0f, edgeMaskJitter, cropSeed);
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
            // WHICH sprite the footprint actually came from — the tier that answered, not the tier that was
            // asked. A sampled crop is cut out of this exact sprite, so reading `resolved` here would cut from
            // the wrong art (or from null) whenever the Debris Scatter tier below is the one that answered.
            Sprite footprintSprite = haveFootprint ? resolved : null;
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
                    if (debrisSource != null && TrySamplePixels(debrisSource, pixelScratch))
                    {
                        haveFootprint = true;
                        footprintSprite = debrisSource;
                    }
                }
            }

            bool useFootprintPositions = haveFootprint && emitFromFootprint;

            // ── the hit disc ──────────────────────────────────────────────────────────────────────────────
            // Filtered ONCE per fire into a list of indices, not re-tested per particle: every particle just
            // needs a random index, exactly as the unfiltered path always has. The test is done in the scan's
            // own offset space (world units from the pivot) rather than in pixel coordinates, because the
            // editor preview holds only the offsets — one arithmetic, two places, no chance of the preview
            // drawing a differently-shaped disc than the burst.
            float sourcePpu = SourcePpu(footprintSprite);
            Vector2 centreOffset = hitOffsetPx / sourcePpu;
            float radiusWorld = Mathf.Max(0.01f, hitRadiusPx) / sourcePpu;
            List<int> radiusIndices = null;
            if (haveFootprint && useHitRadius)
            {
                float rSq = radiusWorld * radiusWorld;
                radiusIndices = new List<int>(64);
                for (int i = 0; i < pixelScratch.Count; i++)
                    if ((pixelScratch[i].offset - centreOffset).sqrMagnitude <= rSq) radiusIndices.Add(i);
                // Nothing inside it (a disc entirely off the art, or over a hole): degrade to the whole
                // footprint — the pre-disc behaviour, still a visible spray — instead of firing nothing.
                if (radiusIndices.Count == 0) radiusIndices = null;
            }
            bool radiusActive = radiusIndices != null;

            // Cutting real pixels needs a sprite to cut them OUT of, so an unreadable / absent source drops
            // this particle back to a tinted shard rather than to nothing.
            bool cropMode = particleLook == SplashParticleLook.SampledCrops && haveFootprint && footprintSprite != null;
            var edgeRamp = tintCropEdges ? ResolveEdgeTint() : null;
            // What a crop's position in the ramp is measured against: the disc's own radius when there is one,
            // else the sprite's half-diagonal, so the ramp still spans the art with no disc set.
            float rampSpan = radiusActive
                ? radiusWorld
                : Mathf.Max(0.0001f, FootprintHalfSpan(footprintSprite, sourcePpu));

            int count = Mathf.Max(0, rng.RangeInclusive(Mathf.Min(countMin, countMax), Mathf.Max(countMin, countMax)));
            float sizeLo = Mathf.Min(sizePxMin, sizePxMax), sizeHi = Mathf.Max(sizePxMin, sizePxMax);
            float speedLo = Mathf.Min(speedMin, speedMax), speedHi = Mathf.Max(speedMin, speedMax);
            float lifeLo = Mathf.Min(lifeMin, lifeMax), lifeHi = Mathf.Max(lifeMin, lifeMax);
            float ppu = Mathf.Max(1f, EffectivePixelsPerUnit);

            int pickBound = radiusActive ? radiusIndices.Count : pixelScratch.Count;

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = Vector3.zero;
                Color32 color = new Color32(255, 255, 255, 255);
                var sample = default(PixelSample);
                bool havePixel = false;
                if (haveFootprint)
                {
                    int raw = rng.Next(pickBound);
                    sample = pixelScratch[radiusActive ? radiusIndices[raw] : raw];
                    havePixel = true;
                    color = sample.color;
                    // Measured from the DISC'S centre when there is one, so the patch flies out of the burst
                    // origin (the real hit point) wherever on the art it was taken from — see the class note.
                    if (useFootprintPositions)
                        offset = radiusActive ? (Vector3)(sample.offset - centreOffset) : (Vector3)sample.offset;
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
                // ONE draw here whichever mode is on — a crop's size is rolled from its own dials, but at the
                // same position in the stream — so switching Particles between Shards and Sampled crops keeps
                // every position, angle, speed and lifetime identical and changes only the look. (Both are
                // ChunkRng.Range, which costs exactly one NextUInt whatever the bounds are.)
                float sizeRoll = cropMode
                    ? rng.Range(Mathf.Min(cropPxMin, cropPxMax), Mathf.Max(cropPxMin, cropPxMax) + 0.999f)
                    : rng.Range(sizeLo, sizeHi);

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
                // Drawn ONLY in crop mode, and last: appending it unconditionally would shift every later
                // particle's picks and quietly re-roll every seeded Shards spray in the project.
                int cropSeed = cropMode ? rng.Next(int.MaxValue) : 0;

                // ── build the particle's own sprite ───────────────────────────────────────────────────────
                // Uniform INCLUSIVE integer texel count: the roll above spans [lo, hi+0.999] so a floor lands on
                // every value in the range with equal weight, hi included.
                int cropPx = cropMode
                    ? Mathf.Clamp(Mathf.FloorToInt(sizeRoll), Mathf.Max(1, Mathf.Min(cropPxMin, cropPxMax)),
                                  Mathf.Max(1, Mathf.Max(cropPxMin, cropPxMax)))
                    : 1;

                Sprite ownedCrop = null;
                if (cropMode && havePixel)
                    ownedCrop = BuildCrop(footprintSprite, sample, cropPx, ppu, cropSeed, edgeRamp,
                                          centreOffset, rampSpan);

                Sprite shape;
                float scale;
                if (ownedCrop != null)
                {
                    // A crop is cut at N source texels and built at the particle PPU, so it is exactly N game
                    // pixels across with NO scaling at all. That is the structural reason this path cannot
                    // reproduce T-0393: there is no shape library picked independently of the target size — the
                    // texel count and the on-screen size are the same number, so nothing can be squashed below
                    // its own grid. Its colour comes from the art it was cut from, so the renderer tint stays
                    // white instead of multiplying the sampled pixel's colour over it a second time.
                    shape = ownedCrop;
                    scale = 1f;
                    color = new Color32(255, 255, 255, 255);
                }
                else
                {
                    // The shape-pick draw stays in its ORIGINAL rng-stream position (above). ChunkPreviewSim
                    // .Splash draws its own matching "shard shape pick" at that exact point, so a seeded splash
                    // stays reproducible between the editor preview and a real Fire, and the bound swap there
                    // (Count → int.MaxValue) is safe because ChunkRng.Next(int) costs exactly one NextUInt()
                    // step for ANY bound ABOVE 1 — measured, not assumed. ⚠ That is NOT "any bound": Next(int)
                    // short-circuits to 0 and consumes NOTHING when the bound is <= 1, so never swap a bound
                    // there for one that could be 0 or 1 without re-checking the preview's matching draw, or
                    // the two silently desync.
                    // The draw's result feeds GetFitting instead of Get, so it only ever picks AMONG shards
                    // that fit inside the target size rolled just after it — a particle authored as "1-3 game
                    // pixels" must never end up built from a 3×3 shard squashed down to 1px, which would render
                    // each of that shard's OWN texels at a third of a game pixel: crisp confetti reading as
                    // smooth noise instead (T-0393), the actual cause behind "most particles are still
                    // sub-pixel" surviving the PPU fix in T-0391.
                    // Every ChunkSprites shape is a shared, statically cached pixel sprite, so this allocates
                    // no texture per particle and there is nothing to clean up on death.
                    // ⚠ HARD SUB-PIXEL FLOOR (T-0394, owner escalation). Floored at ONE, not at 0.1: sizePxMin/
                    // sizePxMax are authored in GAME PIXELS at EffectivePixelsPerUnit, so a target below 1 is
                    // literally a particle smaller than one pixel of the project's own pixel grid — invisible
                    // fine noise, never crisp pixel art. The old 0.1f floor let an authored 0.1-1.0 range
                    // through untouched, and T-0393's GetFitting does NOT close this: that only stops a shard
                    // being squashed below its OWN texel grid, it never questions a sub-1 target in the first
                    // place. Clamped at the point of USE rather than only on the fields, so a splash authored
                    // before this (UC4 Zoe Spray and friends) is fixed without anyone re-saving the asset.
                    float targetPx = Mathf.Max(1f, cropMode ? cropPx : sizeRoll);
                    shape = ChunkSprites.GetFitting(targetPx, ppu, shapePick);
                    scale = targetPx / Mathf.Max(0.01f, shape.rect.width);
                }

                var go = new GameObject("SplashParticle");
                var t = go.transform;
                t.position = ctx.Origin + offset;
                t.localScale = Vector3.one * scale;
                t.SetParent(ctx.Container, true);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = shape;
                sr.color = color;
                // -1: one row for the whole card. The particle index only sub-orders the specks inside that
                // one row's own gap, exactly as it did inside a named slot's band.
                ctx.ApplyOrder(sr, this, -1, i);

                // The runner drives POSITION only — the shared contract, so Chunks never bolts a component
                // onto anything it does not own. It deliberately never fades or destroys its targets (wrong
                // for the pooled blasts it also drives), so a particle we own outright gets its own rider.
                runner.Move(t, velocity, gravity, drag, life);
                go.AddComponent<ChunkSplashParticleFade>().Begin(sr, color, alphaEnvelope, life, ownedCrop);
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
        Sprite ownedSprite;

        /// <paramref name="owned"/> is a sprite built FOR THIS PARTICLE ALONE (a sampled crop, with its own
        /// little Texture2D behind it) and nothing else will ever reference it, so this rider disposes of both
        /// when the particle dies. Null for a shard, which is a shared statically-cached sprite that must
        /// never be destroyed — hence the explicit hand-over rather than "destroy whatever the renderer holds".
        public void Begin(SpriteRenderer renderer, Color color, List<ZUIEnvelopePoint> envelope, float lifeSeconds,
                          Sprite owned = null)
        {
            sr = renderer;
            baseColor = color;
            alphaEnvelope = envelope;
            life = Mathf.Max(0.001f, lifeSeconds);
            age = 0f;
            ownedSprite = owned;
        }

        void OnDestroy()
        {
            if (ownedSprite == null) return;
            var tex = ownedSprite.texture;
            Destroy(ownedSprite);
            if (tex != null) Destroy(tex);
            ownedSprite = null;
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
