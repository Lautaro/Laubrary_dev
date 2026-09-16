// ChunkPreviewSim — what a recipe has on screen at one instant, worked out from the recipe alone.
//
// The preview's whole promise is that it PREDICTS the burst: if the schematic says three blasts have gone off
// and the fragments are already falling at 0.8s, the real burst must agree. So this is not a decorative
// animation over the dials — it re-walks the same maths the runtime walks. Every arc integrates gravity and
// drag exactly the way Chunk.Update and ChunkModuleRunner.Update do (accelerate, damp, move, in that order),
// every formation point comes out of SpawnFormation.Resolve rather than a second copy of the placement code,
// and every random draw is taken from ChunkRng in the same ORDER the capability's own Fire takes it, so the
// dot at index 4 is the chunk that will actually be at index 4.
//
// Two deliberate departures from the runtime, both forced and both narrow:
//
//   1. A seed of 0 means "reroll every play" at runtime, which is a UnityEngine.Random draw — banned in
//      anything the preview runs (BC-1.3), and meaningless in a picture that must hold still while you dial.
//      An unseeded capability therefore previews off a hash of its own capability id: stable for the life of
//      that capability, different from its neighbours, and never written back to the asset.
//   2. Three things are too expensive to redo inside a repaint: a fracture's real cut (FragmentCutter over a
//      readable texture), a splash's real footprint (a full pixel scan) and a blast's real frames. Each is
//      worked out ONCE per source and cached here, then drawn every frame: the fracture draws its real
//      pieces, the splash emits from its real opaque pixels in their real colours, and a blast shows its
//      own current frame under its tint. A source whose pixels cannot be read falls back exactly the way
//      the runtime does (no pieces; a white spray), and the stage says so in its tooltip.
//
// It produces plain geometry (dots, discs, cones, polylines) and knows nothing about screens, rects or GUI:
// the stage decides where a world point lands, this decides what is at that world point at time t.
using System.Collections.Generic;
using Laubrary.Layering;
using UnityEngine;

namespace Laubrary.Chunks.Editor
{
    /// How one guide reads: a spinning piece, a particle, a blast's footprint, or a hit radius.
    internal enum ChunkGuideShape
    {
        /// A small oriented square — a debris chunk or a fragment, so its spin is visible as rotation.
        Square = 0,
        /// A filled dot — a splash particle or a trail puff, too small to have a readable orientation.
        Dot = 1,
        /// A filled disc — one blast's footprint, sized from the effect it spawns.
        Disc = 2,
        /// An unfilled circle — a Hits radius, or a blast that has not gone off yet.
        Ring = 3,
    }

    /// One drawable thing at one instant, in the recipe's own world units with the origin at (0,0).
    internal struct ChunkGuide
    {
        public Vector2 pos;
        public float radius;      // world units: half-extent of a square, radius of a dot/disc/ring
        public float angleDeg;    // squares always; discs only when showAngle is set (a Fling orientation tick)
        public bool showAngle;    // discs only — a square always draws its angle, so this has no effect there
        public float alpha;
        public Color color;
        public ChunkGuideShape shape;
        public int order;         // painted low-to-high; layer slot first, stack position second
        public string label;      // a blast's firing number across the whole recipe, else null
        /// The capability this guide belongs to (its EnsureId()), or null for a guide with nothing to drag
        /// (a chunk, a splash particle). Only Pyre Blast sets this today — the stage's drag hit-test
        /// (ChunkWindow.Preview.cs) uses it to find which capability's offset a dragged disc/ring belongs to.
        public string capId;
        public Color outline;     // the owning card's identity colour for a disc/ring rim; alpha 0 = use color

        // ── the real picture, when there is one ──────────────────────────────────────────────────────────
        // A square with a texture is a real fracture piece; a disc with a texture is a blast's current frame.
        // Either way the picture replaces the schematic fill, and the texture is owned by ChunkPreviewSim's
        // caches — a guide only borrows it for one repaint.
        public Texture texture;
        public Rect uv;           // texcoords of the picture inside the texture
        public Vector2 picSize;   // world units, unrotated
        public Vector2 picOffset; // world units, from pos to the picture's centre, unrotated
        public Color picColor;    // multiplied onto the picture — the tint, and the alpha it comes out at
        public bool hollow;       // discs only: rim only, no fill (a blast whose frames have run out)
    }

    /// A producer's aim: where it throws and how wide. Drawn under everything as context.
    internal struct ChunkGuideCone
    {
        public Vector2 pos;
        public float centreDeg;
        public float halfSpreadDeg;
        public float radius;       // world units — how far the cone is drawn, not how far things travel
        public float alpha;
        public Color color;
        public int order;
    }

    /// A path something has travelled (a trajectory arc), in world units.
    internal struct ChunkGuidePath
    {
        public Vector2[] points;
        public float alpha;
        public Color color;
        public int order;
    }

    /// Everything the stage draws for one instant. Reused between repaints so a moving preview allocates
    /// nothing per frame.
    internal sealed class ChunkPreviewFrame
    {
        public readonly List<ChunkGuideCone> Cones = new List<ChunkGuideCone>();
        public readonly List<ChunkGuidePath> Paths = new List<ChunkGuidePath>();
        public readonly List<ChunkGuide> Guides = new List<ChunkGuide>();

        /// How far from the origin anything in this frame reached, world units.
        public float Reach;

        /// How far out the stage MUST reach to stay honest, whatever the framing decides — the recipe's
        /// authored placements (a formation's points), which say where the recipe puts things rather than
        /// where physics happened to fling one particle. Framing may crop a far-flung dot; cropping a
        /// formation point would hide part of the shape the author laid out.
        public float Required;

        /// Something drawn in this frame rolls afresh on every real burst (a seed of 0), so the picture is one
        /// example of it rather than THE burst.
        public bool Unseeded;

        /// Things the stage should admit about this frame (an unreadable picture, nothing to cut). Each string
        /// is owned by a cache and reused, so filling this allocates nothing per repaint.
        public readonly List<string> Notes = new List<string>();

        public void Clear()
        {
            Cones.Clear();
            Paths.Clear();
            Guides.Clear();
            Notes.Clear();
            Reach = 0f;
            Required = 0f;
            Unseeded = false;
        }
    }

    internal static class ChunkPreviewSim
    {
        // ── budgets ───────────────────────────────────────────────────────────────────────────────────────
        // A repaint has to finish inside a frame, and a recipe is free to ask for more pieces than a schematic
        // can usefully show. These are the ceilings; a recipe under them is drawn in full.
        const int DotsPerCapability = 200;
        const int DotsPerFrame = 600;
        const int MaxSteps = 900;          // 15 s of flight at the step below
        const float Step = 1f / 60f;       // the runtime integrates once per frame; 60 is the reference rate
        const int TrailPuffs = 24;         // most recent puffs kept per trailed piece

        /// Colours a guide takes from its Layer-Plan slot. Without a plan a guide wears its card's own colour
        /// (ChunkCardColors), the same one its card and its Timing lane wear.
        internal static readonly Color[] SlotColors =
        {
            new Color(0.36f, 0.62f, 0.92f), new Color(0.95f, 0.62f, 0.25f),
            new Color(0.45f, 0.83f, 0.52f), new Color(0.86f, 0.45f, 0.72f),
            new Color(0.85f, 0.82f, 0.36f), new Color(0.55f, 0.55f, 0.95f),
        };

        /// What an unslotted capability draws in once a Layer Plan exists: it belongs to no slot, so it takes
        /// no slot's colour.
        internal static readonly Color UnslottedColor = new Color(0.72f, 0.72f, 0.75f);

        const float PendingAlpha = 0.22f;   // a capability whose delay has not come round yet

        /// How long a settled chunk lingers before it is gone, when Rest on floor is off. Chunk.Update's own
        /// number — a short linger rather than a blink-out.
        const float SettleLinger = 0.05f;

        // ── caches ────────────────────────────────────────────────────────────────────────────────────────
        // A blast's look and a splash's pixels are keyed by the ASSET, because two cards pointing at one Pyre
        // must not render it twice. A fracture's cut and a splash's resolution are keyed by the CAPABILITY,
        // remember which references they were worked out from, and redo themselves the moment one differs —
        // so picking another source is seen on the next frame, without waiting for a window rebuild. The
        // window clears everything on a rebuild, which is what catches a source edited in place.

        static readonly Dictionary<Object, SourceLook> Looks = new Dictionary<Object, SourceLook>();
        static readonly Dictionary<Sprite, PixelSamples> Samples = new Dictionary<Sprite, PixelSamples>();
        static readonly Dictionary<ChunkCapability, CutLook> Cuts = new Dictionary<ChunkCapability, CutLook>();
        static readonly Dictionary<ChunkCapability, SplashLook> Splashes = new Dictionary<ChunkCapability, SplashLook>();
        static readonly List<SpawnPlacement> Placements = new List<SpawnPlacement>(16);
        static readonly List<Vector2> PathScratch = new List<Vector2>(256);
        static int _shapeCount;

        /// True while <see cref="Reach"/> is measuring rather than drawing — the only time a blast works out
        /// the peak of every flight, which is far too much integration to redo per repaint.
        static bool _measuring;

        static ChunkPreviewSim()
        {
            // The cut textures are HideAndDontSave: a domain reload would orphan them for the rest of the
            // editor session if nothing released them first.
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ClearCaches;
        }

        /// Forget everything measured, cut or sampled. Called when the window rebuilds.
        internal static void ClearCaches()
        {
            foreach (var cut in Cuts.Values) cut.ReleaseParts();
            Cuts.Clear();
            Splashes.Clear();
            Samples.Clear();
            Looks.Clear();
        }

        /// Forget which sprite each splash resolved to (the pixels read from it stay). Called on every edit:
        /// a splash's answer depends on fields its card may grow without this file learning their names, and
        /// re-asking the capability once per edit is cheap next to guessing wrong.
        internal static void ForgetResolutions() => Splashes.Clear();

        // ── the frame ─────────────────────────────────────────────────────────────────────────────────────

        /// Fill <paramref name="into"/> with the recipe's state at <paramref name="time"/> seconds.
        /// <paramref name="includeDisabled"/> draws switched-off capabilities too — never for the picture,
        /// only for the measurement that frames it (see <see cref="Reach(ChunkSpec, ChunkPreviewFrame)"/>).
        internal static void Build(ChunkSpec spec, float time, ChunkPreviewFrame into, bool includeDisabled = false)
        {
            if (into == null) return;
            into.Clear();
            if (spec == null || spec.capabilities == null) return;

            var layers = spec.ResolveLayers();
            bool planned = spec.FirstEnabled<LayerPlan>() != null;
            var stack = spec.capabilities;

            int budget = DotsPerFrame;
            CollectFiringRanks(spec);

            for (int i = 0; i < stack.Count && budget > 0; i++)
            {
                var cap = stack[i];
                if (cap == null) continue;
                if (!cap.OccupiesTime) continue;          // coordinators and modifiers draw through their target
                // Switched off draws nothing, but it keeps its firing numbers — the same reason ChunkClock
                // counts it: turning one off must not renumber the others.
                if (!cap.enabled && !includeDisabled) continue;

                var colour = ColorFor(spec, cap, layers, planned);
                int order = OrderFor(cap, layers, i);

                // Where a blast's pattern and flights reach is true whatever the clock says, so framing
                // measures it before the "is it on screen now" test rather than only when it happens to be.
                if (_measuring && cap is PyreBlast measured) BlastRequired(spec, measured, into);

                float delay = Mathf.Max(0f, cap.delay);
                float duration = Mathf.Max(0f, cap.DurationSeconds(spec));
                if (time > delay + duration) continue;    // after its duration it is gone, not frozen

                bool pending = time < delay;
                float local = time - delay;

                switch (cap)
                {
                    case DebrisScatter debris:
                        budget -= Debris(spec, debris, local, pending, colour, order, budget, into);
                        break;
                    case FragmentFracture fracture:
                        budget -= Fracture(spec, fracture, local, pending, colour, order, budget, into);
                        break;
                    case PaletteSplash splash:
                        budget -= Splash(spec, splash, local, pending, colour, order, budget, into);
                        break;
                    case PyreBlast blast:
                        budget -= Blast(spec, blast, local, pending, colour, order, budget, into);
                        break;
                }
            }

            for (int g = 0; g < into.Guides.Count; g++)
            {
                var guide = into.Guides[g];
                into.Reach = Mathf.Max(into.Reach, guide.pos.magnitude + guide.radius);
            }
            for (int c = 0; c < into.Cones.Count; c++)
                into.Reach = Mathf.Max(into.Reach, into.Cones[c].pos.magnitude + into.Cones[c].radius);
        }

        /// How far out the stage frames, world units.
        ///
        /// NOT the recipe's furthest reach: framing to the outermost thing that ever happens lets ONE
        /// far-flung particle at the very end of the clock shrink the whole composition to a dot for the
        /// entire run, which is the opposite of what a preview is for. So the frame is sized to hold about
        /// nine tenths of what is on screen at the clock's MIDPOINT — the instant a burst is most itself,
        /// with the early producers still alive and the late ones already out — and then widened, never
        /// narrowed, so the origin, every authored formation point and the top of every blast's flight are
        /// inside it, with room round a lone blast so it sits in the stage instead of filling it. A handful of
        /// far-flung particles leaving the picture is the intended trade; a blast leaving it is not.
        ///
        /// It is one instant rather than the running one, so the picture does not zoom in and out while the
        /// clock runs; and it counts SWITCHED-OFF capabilities, exactly as the clock's own length does, so
        /// flicking one off to look at the rest cannot rescale everything else under the cursor. Expensive
        /// enough to be worth caching — the window recomputes it only when the recipe changed, never per frame.
        internal static float Reach(ChunkSpec spec, ChunkPreviewFrame scratch)
        {
            if (spec == null || scratch == null) return 1f;

            return Reach(spec, scratch, out _);
        }

        /// <see cref="Reach(ChunkSpec, ChunkPreviewFrame)"/>, plus the part of it that is not negotiable:
        /// how far the recipe's authored placements and every blast's flight peak reach. A window holding its
        /// zoom still (while a value is being dragged) may ignore the rest, but never this.
        internal static float Reach(ChunkSpec spec, ChunkPreviewFrame scratch, out float required)
        {
            required = 0f;
            if (spec == null || scratch == null) return 1f;

            _measuring = true;
            try
            {
                float length = Mathf.Max(0.05f, ChunkClock.Length(spec));
                Build(spec, length * 0.5f, scratch, includeDisabled: true);
                float fit = Fit(scratch);
                required = scratch.Required;

                // A recipe whose output is all early or all late can have nothing at all on screen at its
                // midpoint. Framing that to the floor would open the recipe zoomed into empty space, so fall
                // back to the fullest instant instead of to nothing.
                if (fit <= 0f)
                {
                    const int SampleCount = 24;
                    for (int s = 0; s <= SampleCount; s++)
                    {
                        Build(spec, length * s / SampleCount, scratch, includeDisabled: true);
                        fit = Mathf.Max(fit, Fit(scratch));
                        required = Mathf.Max(required, scratch.Required);
                    }
                }

                required = Mathf.Max(0.5f, required);
                return Mathf.Max(0.5f, Mathf.Max(fit, required));
            }
            finally { _measuring = false; }
        }

        static readonly List<float> Extents = new List<float>(256);

        /// The distance that holds ~90 % of this frame's drawn things, world units. 0 when nothing is drawn.
        static float Fit(ChunkPreviewFrame f)
        {
            Extents.Clear();
            for (int i = 0; i < f.Guides.Count; i++)
                Extents.Add(f.Guides[i].pos.magnitude + f.Guides[i].radius);
            if (Extents.Count == 0) return 0f;

            Extents.Sort();
            // Ceil, so a frame of one thing frames that thing rather than nine tenths of it.
            int index = Mathf.Clamp(Mathf.CeilToInt(0.9f * (Extents.Count - 1)), 0, Extents.Count - 1);
            return Extents[index];
        }

        // ── Debris Scatter ────────────────────────────────────────────────────────────────────────────────
        // The draws below are taken in the SAME order DebrisScatter.Fire takes them (sprite pick, palette
        // pick, cone angle, speed, spin, life, size), because the whole point of a seeded scatter is that the
        // picture and the burst agree chunk for chunk. Skipping a draw here would shift every later value.

        static int Debris(ChunkSpec spec, DebrisScatter d, float local, bool pending,
                          Color colour, int order, int budget, ChunkPreviewFrame into)
        {
            float centreDeg = spec != null ? spec.directionDeg : 0f;
            AddCone(into, Vector2.zero, centreDeg, d.spreadDeg, ConeRadius(d.speedMax, d.lifeMax),
                    pending ? PendingAlpha : 0.5f, colour, order);
            if (d.seed == 0) into.Unseeded = true;
            if (pending) return 0;

            var rng = Seeded(d, d.seed);
            int count = rng.RangeInclusive(Mathf.Min(d.countMin, d.countMax), Mathf.Max(d.countMin, d.countMax));
            if (count <= 0) return 0;

            var trail = spec != null ? spec.FindModifier<Trail>(d) : null;
            var hits = spec != null ? spec.FindModifier<Hits>(d) : null;
            bool trailing = trail != null && trail.enabled;
            bool hitting = hits != null && hits.enabled;

            bool animated = d.visual == DebrisVisual.Animated && d.AnimationSource != null;
            bool haveSprites = d.visual == DebrisVisual.Sprites && d.sprites != null && d.sprites.Count > 0;
            bool cut = d.UsesSampledDebris;   // a successful cut takes no draw; see DebrisScatter.Fire

            int drawn = 0;
            for (int i = 0; i < count; i++)
            {
                // 1. the sprite pick, mirrored so every later draw lands where the runtime's does
                if (!animated && !cut) rng.Next(haveSprites ? d.sprites.Count : ShapeCount);
                // 2. the palette pick — a preview burst carries no palette, so the runtime takes no draw either

                float angle = (centreDeg + rng.Range(-d.spreadDeg, d.spreadDeg)) * Mathf.Deg2Rad;
                var velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * rng.Range(d.speedMin, d.speedMax);
                velocity.y += d.upwardBias;

                bool tumbling = cut && d.tumble;
                float spin = (tumbling ? rng.Range(d.tumbleSpeedMin, d.tumbleSpeedMax)
                                       : rng.Range(d.angularSpeedMin, d.angularSpeedMax)) * rng.NextSign();
                float life = rng.Range(d.lifeMin, d.lifeMax);
                float size = rng.Range(d.sizeMin, d.sizeMax);

                if (drawn >= budget || drawn >= DotsPerCapability) continue;   // keep drawing NOTHING, not a wrong dot
                if (local > life) continue;

                bool wantPath = trailing;
                var flight = Fly(Vector2.zero, velocity, d.gravity, d.drag, local,
                                 d.useFloor, d.floorY, d.bounciness, d.floorFriction, d.restOnFloor,
                                 wantPath ? PathScratch : null);

                // Rest on floor OFF means a settled piece lingers a moment and is gone — Chunk.Update cuts its
                // life to the instant it stopped plus a beat. Without this the picture keeps a floor full of
                // debris the burst will have cleared, which is the whole difference the dial makes.
                if (!d.restOnFloor && flight.settled && local > flight.settledAt + SettleLinger) continue;

                float t01 = Mathf.Clamp01(local / Mathf.Max(0.01f, life));
                float sizeMul = Mathf.Max(0f, ZUIEnvelopeEvaluator.Evaluate(d.sizeEnvelope, t01, 1f));
                float alpha = Mathf.Clamp01(ZUIEnvelopeEvaluator.Evaluate(d.alphaEnvelope, t01, 1f));
                Color tint = d.colorOverLife != null ? d.colorOverLife.Evaluate(t01) : Color.white;
                float radius = Mathf.Max(0.002f, size * 0.5f * sizeMul);

                float facing = d.faceVelocity
                    ? Mathf.Atan2(flight.velocity.y, flight.velocity.x) * Mathf.Rad2Deg
                    : spin * local;

                if (trailing)
                    AddTrail(into, PathScratch, trail.interval, local, colour, order - 1);

                if (hitting)
                    into.Guides.Add(new ChunkGuide
                    {
                        pos = flight.position,
                        radius = Mathf.Max(0.004f, radius * Mathf.Max(0.01f, hits.radiusScale) * 2f),
                        alpha = alpha * 0.25f,
                        color = colour,
                        shape = ChunkGuideShape.Ring,
                        order = order,
                    });

                into.Guides.Add(new ChunkGuide
                {
                    pos = flight.position,
                    radius = radius,
                    angleDeg = facing,
                    alpha = alpha,
                    color = colour * tint,
                    shape = ChunkGuideShape.Square,
                    order = order,
                });
                drawn++;
            }
            return drawn;
        }

        // ── Fragment Fracture ─────────────────────────────────────────────────────────────────────────────
        // The real cut, drawn as the real pieces: the resolved source goes through FragmentCutter exactly as
        // Fire does (same piece count, same minimum area, the preview's seed), each piece starts at its own
        // offset from the picture's pivot, and the launch is FragmentFracture.Fire's LerpAngle blend between
        // the aim and that piece's own radial direction, off the same second stream. Nothing to cut — no
        // source, or pixels that cannot be read — draws nothing at all, because that is what the burst does.

        static int Fracture(ChunkSpec spec, FragmentFracture f, float local, bool pending,
                            Color colour, int order, int budget, ChunkPreviewFrame into)
        {
            int seed = Seed(f, f.seed);
            var cut = CutOf(f, seed);
            if (cut.stageNote != null) into.Notes.Add(cut.stageNote);
            if (cut.parts == null || cut.parts.Length == 0) return 0;
            if (f.seed == 0) into.Unseeded = true;

            float centreDeg = f.useBurstDirection ? (spec != null ? spec.directionDeg : 0f) : f.directionDeg;
            AddCone(into, Vector2.zero, centreDeg, f.spreadDeg, ConeRadius(f.speedMax, f.lifeMax),
                    pending ? PendingAlpha : 0.5f, colour, order);
            if (pending)
            {
                // Not broken yet: the whole picture, faint, where it will break.
                for (int i = 0; i < cut.parts.Length; i++)
                {
                    var part = cut.parts[i];
                    into.Guides.Add(PieceGuide(part, part.offset, 0f, PendingAlpha, order));
                }
                return 0;
            }

            var rng = new ChunkRng(seed, 1);   // stream 1, exactly as Fire does

            var trail = spec != null ? spec.FindModifier<Trail>(f) : null;
            var hits = spec != null ? spec.FindModifier<Hits>(f) : null;
            bool trailing = trail != null && trail.enabled;
            bool hitting = hits != null && hits.enabled;

            float spread = Mathf.Clamp(f.spreadDeg, 0f, 180f);
            float grav = Mathf.Max(0f, f.gravity), air = Mathf.Clamp(f.drag, 0f, 20f);
            float spdMin = Mathf.Max(0f, f.speedMin), spdMax = Mathf.Max(spdMin, f.speedMax);
            float lifeLo = Mathf.Max(0.01f, f.lifeMin), lifeHi = Mathf.Max(lifeLo, f.lifeMax);
            float spinMin = Mathf.Max(0f, f.angularSpeedMin), spinMax = Mathf.Max(spinMin, f.angularSpeedMax);

            int drawn = 0;
            for (int i = 0; i < cut.parts.Length; i++)
            {
                var part = cut.parts[i];
                float radial = part.offset.sqrMagnitude > 1e-8f
                    ? Mathf.Atan2(part.offset.y, part.offset.x) * Mathf.Rad2Deg
                    : centreDeg;
                float angle = Mathf.LerpAngle(centreDeg, radial, spread / 180f) * Mathf.Deg2Rad;
                float speed = rng.Range(spdMin, spdMax);
                var velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                float life = rng.Range(lifeLo, lifeHi);
                float spin = rng.Range(spinMin, spinMax) * rng.NextSign();

                if (drawn >= budget || drawn >= DotsPerCapability) continue;
                if (local > life) continue;

                var flight = Fly(part.offset, velocity, grav, air, local,
                                 false, 0f, 0f, 0f, false, trailing ? PathScratch : null);

                float t01 = Mathf.Clamp01(local / Mathf.Max(0.01f, life));
                float alpha = Mathf.Clamp01(ZUIEnvelopeEvaluator.Evaluate(f.alphaEnvelope, t01, 1f));

                if (trailing) AddTrail(into, PathScratch, trail.interval, local, colour, order - 1);
                if (hitting)
                    into.Guides.Add(new ChunkGuide
                    {
                        // Hits.Attach is handed half the piece's diagonal, the same as here.
                        pos = flight.position,
                        radius = Mathf.Max(0.006f, part.size.magnitude * 0.5f * Mathf.Max(0.01f, hits.radiusScale)),
                        alpha = alpha * 0.25f, color = colour,
                        shape = ChunkGuideShape.Ring, order = order,
                    });

                into.Guides.Add(PieceGuide(part, flight.position, spin * local, alpha, order));
                drawn++;
            }
            return drawn;
        }

        static ChunkGuide PieceGuide(in CutPart part, Vector2 at, float angleDeg, float alpha, int order)
            => new ChunkGuide
            {
                pos = at,
                radius = Mathf.Max(0.01f, Mathf.Max(part.size.x, part.size.y) * 0.5f),
                angleDeg = angleDeg,
                alpha = alpha,
                color = Color.white,
                shape = ChunkGuideShape.Square,
                order = order,
                texture = part.texture,
                uv = new Rect(0f, 0f, 1f, 1f),
                picSize = part.size,
                picColor = new Color(1f, 1f, 1f, alpha),
            };

        // ── Palette Splash ────────────────────────────────────────────────────────────────────────────────
        // Walks PaletteSplash.Fire's own waterfall: the sprite it resolves (its own, else the fracture's),
        // else a Debris Scatter's sample source, else plain white from the origin. When a footprint was read,
        // every particle takes ONE draw into the opaque pixels — its colour always, its start point when From
        // footprint is on — exactly as the runtime does, so a seeded splash is the same splash. The one tier
        // the preview cannot see is a palette handed over by whoever fires the burst.

        static int Splash(ChunkSpec spec, PaletteSplash s, float local, bool pending,
                          Color colour, int order, int budget, ChunkPreviewFrame into)
        {
            var look = SplashOf(spec, s);
            if (look.stageNote != null) into.Notes.Add(look.stageNote);
            if (s.seed == 0) into.Unseeded = true;

            float centreDeg = s.inheritBurstDirection ? (spec != null ? spec.directionDeg : 0f) : s.directionDeg;
            AddCone(into, Vector2.zero, centreDeg, s.spreadDeg * 0.5f, ConeRadius(s.speedMax, s.lifeMax),
                    pending ? PendingAlpha : 0.5f, colour, order);
            if (pending) return 0;

            var rng = Seeded(s, s.seed);
            var pixels = look.samples;
            bool footprint = pixels != null;
            bool fromFootprint = footprint && s.emitFromFootprint;

            int count = Mathf.Max(0, rng.RangeInclusive(Mathf.Min(s.countMin, s.countMax),
                                                        Mathf.Max(s.countMin, s.countMax)));
            float sizeLo = Mathf.Min(s.sizePxMin, s.sizePxMax), sizeHi = Mathf.Max(s.sizePxMin, s.sizePxMax);
            float speedLo = Mathf.Min(s.speedMin, s.speedMax), speedHi = Mathf.Max(s.speedMin, s.speedMax);
            float lifeLo = Mathf.Min(s.lifeMin, s.lifeMax), lifeHi = Mathf.Max(s.lifeMin, s.lifeMax);
            float ppu = Mathf.Max(1f, s.pixelsPerUnit);

            int drawn = 0;
            for (int i = 0; i < count; i++)
            {
                var offset = Vector2.zero;
                var tone = Color.white;
                if (footprint)
                {
                    int pick = rng.Next(pixels.count);
                    tone = pixels.colors[pick];
                    tone.a = 1f;   // the particle's own alpha rides the envelope, not the pixel's
                    if (fromFootprint) offset = pixels.offsets[pick];
                }
                rng.Next(ShapeCount);                      // the shard shape pick
                float px = Mathf.Max(0.1f, rng.Range(sizeLo, sizeHi));
                float rad = (centreDeg + rng.Range(-s.spreadDeg * 0.5f, s.spreadDeg * 0.5f)) * Mathf.Deg2Rad;
                float speed = rng.Range(speedLo, speedHi);
                float life = rng.Range(lifeLo, lifeHi);

                if (drawn >= budget || drawn >= DotsPerCapability) continue;
                if (local > life) continue;

                var velocity = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * speed;
                var flight = Fly(offset, velocity, s.gravity, s.drag, local, false, 0f, 0f, 0f, false, null);

                float t01 = Mathf.Clamp01(local / Mathf.Max(0.02f, life));
                float alpha = Mathf.Clamp01(ZUIEnvelopeEvaluator.Evaluate(s.alphaEnvelope, t01, 1f));

                into.Guides.Add(new ChunkGuide
                {
                    pos = flight.position, radius = Mathf.Max(0.004f, px / ppu * 0.5f),
                    alpha = alpha, color = tone, shape = ChunkGuideShape.Dot, order = order,
                });
                drawn++;
            }
            return drawn;
        }

        // ── Pyre Blast ────────────────────────────────────────────────────────────────────────────────────
        // The placement comes out of SpawnFormation.Resolve itself — the seed-overriding overload that exists
        // for exactly this, so an unseeded formation previews a stable arrangement without the asset being
        // touched. Each point is numbered by when it FIRES, not by its index, so changing the stagger order
        // renumbers the picture; and each point carries its own Trajectory flight if one is aimed at it.
        //
        // What a point SHOWS is the blast itself: the frame it is on at that moment, multiplied by the tint —
        // the same multiply the runtime puts on its renderers, so a cyan tint over a red Pyre comes out as
        // dark here as it does in the burst. A blast with nothing picked draws nothing.

        static int Blast(ChunkSpec spec, PyreBlast b, float local, bool pending,
                         Color colour, int order, int budget, ChunkPreviewFrame into)
        {
            int seed = Seed(b, b.seed);
            var picked = PickSource(b, seed);
            if (picked == null) return 0;
            var look = LookOf(picked);
            if (b.seed == 0 || (b.UsesPattern && b.formation != null && b.formation.seed == 0))
                into.Unseeded = true;

            var centre = (Vector2)b.offset;
            ResolvePlacements(b);

            float scale = BlastScale(b, seed);
            float radius = BlastRadius(look, scale);

            // The runtime multiplies the tint onto whatever the blast draws. The picture carries the blast's
            // own colours, so it takes the tint as it is; the outline and the flight path, which have no
            // picture, take the blast's average colour times the tint — the colour it will actually read as
            // from across the screen. RGB only: the tint's opacity rides with the per-instance alpha below.
            var tint = b.tint;
            var own = look.hasAverage ? look.average : Color.white;
            var tinted = new Color(own.r * tint.r, own.g * tint.g, own.b * tint.b, 1f);
            float tintAlpha = Mathf.Clamp01(tint.a);

            float blast = Mathf.Max(0.05f, b.blastSeconds);
            var flight = spec != null ? spec.FindModifier<Trajectory>(b) : null;
            // The rim says WHICH CARD even when a Layer Plan has taken the fill for its slot colour.
            var identity = ChunkCardColors.For(spec, b);
            RanksByCap.TryGetValue(b, out var ranks);
            if (flight != null && !flight.enabled) flight = null;
            if (flight != null && flight.seed == 0) into.Unseeded = true;

            // Numbered by firing order across the WHOLE recipe (see CollectFiringRanks), so the numbers say
            // when rather than where, and a single blast is numbered as well as a pattern's points.
            int drawn = 0;
            for (int i = 0; i < Placements.Count && drawn < budget; i++)
            {
                var placement = Placements[i];
                var at = (Vector2)placement.Position;
                float fires = placement.Delay;
                string number = ranks != null && i < ranks.Count ? ranks[i].ToString() : null;

                float since = local - fires;
                if (pending || since < 0f)
                {
                    // A point that has not gone off yet keeps its full outline alpha: the faint ring is the
                    // schematic saying "not yet", and dimming it by an authored alpha band would make a
                    // half-faded blast's own placement unreadable before it fires.
                    into.Guides.Add(new ChunkGuide
                    {
                        pos = at, radius = radius, alpha = PendingAlpha, color = tinted,
                        shape = ChunkGuideShape.Ring, order = order,
                        label = number, outline = identity,
                        capId = b.id,
                    });
                    drawn++;
                    continue;
                }
                if (since > blast) continue;   // gone, not held on its last frame

                var pos = at;
                // Orientation tick: the only way a round footprint can show it is flying oriented at all
                // (Face velocity and/or Spin — G5). false leaves the disc's angleDeg unused, same as before
                // this existed.
                bool oriented = false;
                float discAngleDeg = 0f;
                if (flight != null)
                {
                    var velocity = LaunchOf(flight, spec, placement.Index, centre, at, out float spinDegPerSec);
                    var path = Fly(at, velocity, flight.gravity, flight.drag,
                                   flight.untilTargetEnds ? since : Mathf.Min(since, Mathf.Max(0.01f, flight.lifeSeconds)),
                                   false, 0f, 0f, 0f, false, PathScratch);
                    pos = path.position;
                    if (PathScratch.Count >= 2)
                        into.Paths.Add(new ChunkGuidePath
                        {
                            points = Thin(PathScratch),
                            alpha = 0.65f, color = tinted, order = order - 1,
                        });

                    oriented = flight.faceVelocity || spinDegPerSec != 0f;
                    if (oriented)
                    {
                        float spinAngle = spinDegPerSec * since;
                        // Preview never models a Pyre Blast's OWN spawn rotation (rotationMode) for a Disc —
                        // only what Fling itself contributes, matching the runtime baseline captured at the
                        // moment ChunkModuleRunner.Move starts driving the spawn.
                        discAngleDeg = flight.faceVelocity
                            ? Mathf.Atan2(path.velocity.y, path.velocity.x) * Mathf.Rad2Deg + spinAngle
                            : spinAngle;
                    }
                }

                float pointAlpha = tintAlpha * BlastAlpha(b, seed, placement.Index);
                var guide = new ChunkGuide
                {
                    pos = pos, radius = radius,
                    // The outline fades over the authored length so a finished blast lets go of its spot; the
                    // picture below does not, because the frames carry the blast's own fade.
                    alpha = (1f - Mathf.Clamp01(since / blast)) * pointAlpha,
                    color = tinted, shape = ChunkGuideShape.Disc, order = order,
                    label = number, outline = identity,
                    capId = b.id,
                    angleDeg = discAngleDeg, showAngle = oriented,
                };

                if (look.HasFrames)
                {
                    var frame = look.FrameAt(since);
                    if (frame != null && TryPicture(frame, out var tex, out var uv, out var size, out var pivotOffset))
                    {
                        guide.texture = tex;
                        guide.uv = uv;
                        guide.picSize = size * scale;
                        guide.picOffset = pivotOffset * scale;
                        guide.picColor = new Color(tint.r, tint.g, tint.b, pointAlpha);
                    }
                    else guide.hollow = true;   // its frames have run out: the blast is over, only its spot remains
                }

                into.Guides.Add(guide);
                drawn++;
            }
            return drawn;
        }

        /// How many of its own radii a lone blast is framed within: enough that one blast at the origin sits IN
        /// the stage with room round it, rather than filling the stage edge to edge.
        const float LoneBlastRoom = 2.5f;

        /// How far one blast card can put anything, whatever the clock says: every point of its pattern (with
        /// the lone-blast room above), and the whole of every flight, so a flung blast never leaves the picture
        /// at the top of its arc.
        static void BlastRequired(ChunkSpec spec, PyreBlast b, ChunkPreviewFrame into)
        {
            int seed = Seed(b, b.seed);
            var picked = PickSource(b, seed);
            if (picked == null) return;   // draws nothing, so frames nothing

            var look = LookOf(picked);
            float radius = BlastRadius(look, BlastScale(b, seed));
            ResolvePlacements(b);

            var flight = spec != null ? spec.FindModifier<Trajectory>(b) : null;
            if (flight != null && !flight.enabled) flight = null;
            float blast = Mathf.Max(0.05f, b.blastSeconds);
            var centre = (Vector2)b.offset;

            float required = into.Required;
            for (int i = 0; i < Placements.Count; i++)
            {
                var placement = Placements[i];
                var at = (Vector2)placement.Position;
                required = Mathf.Max(required, Mathf.Max(at.magnitude + radius, radius * LoneBlastRoom));

                if (flight == null) continue;
                var velocity = LaunchOf(flight, spec, placement.Index, centre, at, out _);
                float seconds = flight.untilTargetEnds ? blast : Mathf.Min(blast, Mathf.Max(0.01f, flight.lifeSeconds));
                Fly(at, velocity, flight.gravity, flight.drag, seconds, false, 0f, 0f, 0f, false, PathScratch);
                for (int p = 0; p < PathScratch.Count; p++)
                    required = Mathf.Max(required, PathScratch[p].magnitude + radius);
            }
            into.Required = required;
        }

        // ── firing order ──────────────────────────────────────────────────────────────────────────────────
        // Every blast the recipe sets off — each Single, each pattern point — ranked by the moment it goes off
        // (its card's delay plus the point's own stagger). Ties share a number, because they really do go off
        // together. Switched-off cards are ranked too, so turning one off does not renumber the rest.

        static readonly Dictionary<ChunkCapability, List<int>> RanksByCap = new Dictionary<ChunkCapability, List<int>>();
        static readonly List<List<int>> RankListPool = new List<List<int>>();
        static readonly List<float> FireTimes = new List<float>(64);
        static readonly List<float> SortedFireTimes = new List<float>(64);

        static void CollectFiringRanks(ChunkSpec spec)
        {
            foreach (var used in RanksByCap.Values) { used.Clear(); RankListPool.Add(used); }
            RanksByCap.Clear();
            FireTimes.Clear();
            var stack = spec != null ? spec.capabilities : null;
            if (stack == null) return;

            // Pass one: every firing time, in stack order, then point order.
            for (int i = 0; i < stack.Count; i++)
            {
                if (!(stack[i] is PyreBlast b)) continue;
                ResolvePlacements(b);
                float delay = Mathf.Max(0f, b.delay);
                for (int k = 0; k < Placements.Count; k++) FireTimes.Add(delay + Placements[k].Delay);
            }
            SortedFireTimes.Clear();
            SortedFireTimes.AddRange(FireTimes);
            SortedFireTimes.Sort();

            // Pass two, in the same order: a time's rank is one more than the number of times before it.
            int at = 0;
            for (int i = 0; i < stack.Count; i++)
            {
                if (!(stack[i] is PyreBlast b)) continue;
                ResolvePlacements(b);
                List<int> ranks;
                if (RankListPool.Count > 0)
                {
                    ranks = RankListPool[RankListPool.Count - 1];
                    RankListPool.RemoveAt(RankListPool.Count - 1);
                }
                else ranks = new List<int>();
                for (int k = 0; k < Placements.Count && at < FireTimes.Count; k++)
                    ranks.Add(1 + CountBefore(FireTimes[at++] - 0.0001f));
                RanksByCap[b] = ranks;
            }
        }

        /// How many firing times are strictly below <paramref name="t"/>.
        static int CountBefore(float t)
        {
            int lo = 0, hi = SortedFireTimes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (SortedFireTimes[mid] < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// A blast's placements into Placements, resolved exactly as <see cref="Blast"/> resolves them.
        static void ResolvePlacements(PyreBlast b)
        {
            int seed = Seed(b, b.seed);
            var centre = (Vector2)b.offset;
            if (b.UsesPattern && b.formation != null)
                b.formation.Resolve(centre, Placements, b.formation.seed != 0 ? b.formation.seed : seed);
            else
            {
                Placements.Clear();
                Placements.Add(new SpawnPlacement(centre, 0f, 0));
            }
        }

        /// Rank every blast in <paramref name="spec"/> once, for the <see cref="FiringNumbers"/> calls that
        /// follow it. Any later Build re-ranks, so call this right before reading.
        internal static void RankFirings(ChunkSpec spec) => CollectFiringRanks(spec);

        /// The firing numbers one blast card sets off, lowest first, as of the last RankFirings — what its card
        /// header shows, so the card and its discs on the stage carry the same numbers. Empty for anything that
        /// is not a blast.
        internal static void FiringNumbers(ChunkCapability cap, List<int> into)
        {
            into.Clear();
            if (cap is PyreBlast && RanksByCap.TryGetValue(cap, out var ranks)) into.AddRange(ranks);
            into.Sort();
        }

        /// The launch one blast gets from a Trajectory (a.k.a. Fling), mirroring Trajectory.Apply's own draws
        /// — same three rng draws, same order (speed, direction spread, spin), same ResolveCentreDeg — so the
        /// preview can never show a direction or a spin the burst will not actually take.
        static Vector2 LaunchOf(Trajectory t, ChunkSpec spec, int index, Vector2 patternCentre, Vector2 point,
                                out float spinDegPerSec)
        {
            var rng = new ChunkRng(Seed(t, t.seed), index);
            float lo = Mathf.Min(t.speedMin, t.speedMax), hi = Mathf.Max(t.speedMin, t.speedMax);
            float speed = rng.Range(lo, hi);
            float burstDeg = spec != null ? spec.directionDeg : 0f;
            float centreDeg = Trajectory.ResolveCentreDeg(t.directionMode, t.directionDeg, burstDeg,
                                                          patternCentre, point);
            float rad = (centreDeg + rng.Range(-t.spreadDeg, t.spreadDeg)) * Mathf.Deg2Rad;
            var velocity = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * speed;
            velocity.y += t.upwardBias;

            float spinLo = Mathf.Min(t.spinDegMin, t.spinDegMax), spinHi = Mathf.Max(t.spinDegMin, t.spinDegMax);
            spinDegPerSec = rng.Range(spinLo, spinHi);
            return velocity;
        }

        // ── flight ────────────────────────────────────────────────────────────────────────────────────────

        internal struct Flight
        {
            public Vector2 position;
            public Vector2 velocity;
            public bool settled;
            /// Seconds into the flight at which it came to rest, or -1 if it never did. Chunk.Update cuts a
            /// piece's life short from that moment when Rest on floor is off, so the picture needs the time,
            /// not just the fact.
            public float settledAt;
        }

        /// Where something launched at <paramref name="velocity"/> is after <paramref name="seconds"/>.
        ///
        /// Step for step this is Chunk.Update and ChunkModuleRunner.Update: accelerate under gravity, damp
        /// exponentially by drag, THEN move — the order matters, and a closed-form parabola would visibly
        /// disagree with the runtime the moment drag is non-zero. The floor half is Chunk's alone; a blast or
        /// a fragment has no floor to hit.
        static Flight Fly(Vector2 from, Vector2 velocity, float gravity, float drag, float seconds,
                          bool floor, float floorY, float bounce, float friction, bool rest,
                          List<Vector2> path)
        {
            const float SettleSpeed = 0.4f;   // Chunk.SettleSpeed
            var pos = from;
            var vel = velocity;
            bool settled = false;
            float settledAt = -1f;
            path?.Clear();
            path?.Add(pos);

            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0f, seconds) / Step), 0, MaxSteps);
            for (int i = 0; i < steps && !settled; i++)
            {
                float dt = Mathf.Min(Step, seconds - i * Step);
                if (dt <= 0f) break;

                vel.y -= gravity * dt;
                if (drag > 0f) vel *= Mathf.Exp(-drag * dt);
                pos.x += vel.x * dt;
                pos.y += vel.y * dt;

                if (floor && pos.y <= floorY && vel.y <= 0f)
                {
                    pos.y = floorY;
                    vel.y = -vel.y * bounce;
                    vel.x *= 1f - friction;
                    if (vel.magnitude < SettleSpeed)
                    {
                        settled = true;
                        settledAt = (i + 1) * Step;
                        vel = Vector2.zero;
                    }
                }

                path?.Add(pos);
            }

            return new Flight { position = pos, velocity = vel, settled = settled, settledAt = settledAt };
        }

        /// A drawable copy of a flight path, thinned to a readable number of points. The integration runs at
        /// the runtime's own step because the SHAPE of the arc depends on it; the drawing does not, and a
        /// hundred arcs at nine hundred points each is a megabyte of garbage per frame for a curve nobody can
        /// see the joints of.
        const int PathPoints = 48;

        static Vector2[] Thin(List<Vector2> path)
        {
            int n = path.Count;
            if (n <= PathPoints)
            {
                var all = new Vector2[n];
                path.CopyTo(all);
                return all;
            }
            var thinned = new Vector2[PathPoints];
            for (int i = 0; i < PathPoints; i++)
                thinned[i] = path[Mathf.Min(n - 1, i * (n - 1) / (PathPoints - 1))];
            return thinned;
        }

        /// Puffs left behind along a path at a fixed interval, the freshest brightest — a Trail's own shape.
        static void AddTrail(ChunkPreviewFrame into, List<Vector2> path, float interval, float age,
                             Color colour, int order)
        {
            if (path == null || path.Count < 2) return;
            float every = Mathf.Max(0.01f, interval);
            int puffs = Mathf.Min(TrailPuffs, Mathf.FloorToInt(age / every));
            for (int p = 1; p <= puffs; p++)
            {
                float at = p * every;
                int index = Mathf.Clamp(Mathf.RoundToInt(at / Step), 0, path.Count - 1);
                float fade = 1f - (float)(puffs - p) / Mathf.Max(1, TrailPuffs);
                into.Guides.Add(new ChunkGuide
                {
                    pos = path[index], radius = 0.02f, alpha = 0.35f * fade,
                    color = colour, shape = ChunkGuideShape.Dot, order = order,
                });
            }
        }

        static void AddCone(ChunkPreviewFrame into, Vector2 at, float centreDeg, float halfSpreadDeg,
                            float radius, float alpha, Color colour, int order)
        {
            into.Cones.Add(new ChunkGuideCone
            {
                pos = at, centreDeg = centreDeg,
                halfSpreadDeg = Mathf.Clamp(halfSpreadDeg, 0f, 180f),
                radius = radius, alpha = alpha, color = colour, order = order,
            });
        }

        /// How far to draw a cone: about as far as the fastest thing it throws gets before it is halfway
        /// through its life. Long enough to read as an aim, short enough not to swallow the picture.
        static float ConeRadius(float speed, float life)
            => Mathf.Clamp(Mathf.Max(0.1f, speed) * Mathf.Max(0.05f, life) * 0.35f, 0.25f, 6f);

        // ── colour, order, sizes ──────────────────────────────────────────────────────────────────────────

        /// A capability's colour: its Layer-Plan slot's when it has one, grey when a plan exists and it is in
        /// no slot, and its card's own colour when there is no plan at all. Colour says which SLOT, never
        /// which depth — depth is the runtime's own answer (see OrderFor), and grey is exactly the reading
        /// "this one is not in the plan", which is the thing worth seeing at a glance.
        static Color ColorFor(ChunkSpec spec, ChunkCapability cap, LayerSpec layers, bool planned)
        {
            if (!planned) return ChunkCardColors.For(spec, cap);
            int index = layers != null ? layers.IndexOf(cap.LayerName) : -1;
            return index < 0 ? UnslottedColor : SlotColors[index % SlotColors.Length];
        }

        /// Painted low first, from the SAME resolver the burst stamps on its renderers — so what is in front
        /// on the stage is what will be in front on screen, including the case a rule of its own would get
        /// wrong: an unslotted output falls back to the emitter's own sortingOrder plus its place in the
        /// stack, which is a much LARGER number than a named slot's, so it draws in FRONT of slotted output
        /// rather than behind it. The preview has to assume a default emitter, since a recipe does not know
        /// which one will fire it.
        ///
        /// Multiplied by four only to leave room underneath each capability for the things drawn beneath its
        /// own output (a trajectory arc, a trail), which the runtime has no equivalent of: consecutive
        /// unslotted capabilities resolve one apart, so without the gap a trail would sort onto its
        /// neighbour's own number and the two would interleave unpredictably.
        static int OrderFor(ChunkCapability cap, LayerSpec layers, int stackIndex)
            => ChunkModuleContext.ResolveOrder(layers, cap.LayerName,
                                               ChunkEmitter.DefaultSortingOrder + stackIndex) * 4;

        /// The uniform scale a blast comes out at, on its own stream so a scale can never shift the layout.
        static float BlastScale(PyreBlast b, int seed)
        {
            float lo = Mathf.Max(0.01f, Mathf.Min(b.scaleMin, b.scaleMax));
            float hi = Mathf.Max(lo, b.scaleMax);
            return new ChunkRng(seed, 977).Range(lo, hi);
        }

        /// One blast's radius in world units: half the size of the FIRST FRAME of whatever it spawns, times
        /// the scale it comes out at. Chunks cannot ask a spawner how big it is — IChunkEffectSpawner says
        /// nothing about size — but everything Chunks can spawn also hands over frames, and a frame is a
        /// sprite with real world bounds. A source that cannot answer falls back to a radius that at least
        /// reads as a blast rather than as nothing.
        static float BlastRadius(SourceLook look, float scale)
        {
            const float Fallback = 0.5f;
            float half = look != null && look.half > 0f ? look.half : Fallback;
            return Mathf.Clamp(half * scale, 0.02f, 20f);
        }

        /// The opacity one point of a blast comes out at, from its authored alpha band.
        ///
        /// Hashed from the seed and the point's own index rather than replayed in sequence, for the reason
        /// BlastRadius takes its own stream: the preview does not walk a pattern in firing order, so a
        /// sequential draw would hand point 3 the value the runtime gives point 1. Same seed, same value, and
        /// the points differ from each other exactly as the runtime's successive draws do. A band with both
        /// ends together draws nothing at all — the runtime skips its draw there too.
        static float BlastAlpha(PyreBlast b, int seed, int index)
        {
            float lo = Mathf.Clamp01(Mathf.Min(b.alphaMin, b.alphaMax));
            float hi = Mathf.Clamp01(Mathf.Max(b.alphaMin, b.alphaMax));
            return hi <= lo ? lo : new ChunkRng(seed, 613 + index).Range(lo, hi);
        }

        /// Which alternate this blast uses, hashed from its seed rather than drawn in sequence, so the
        /// preview's pick is reproducible from the seed alone (design decision 9).
        static Object PickSource(PyreBlast b, int seed)
        {
            if (b.pool != null && b.pool.Count > 0)
            {
                int usable = 0;
                for (int i = 0; i < b.pool.Count; i++) if (b.pool[i] is IChunkEffectSpawner) usable++;
                if (usable > 0)
                {
                    int pick = new ChunkRng(seed, 31).Next(usable);
                    for (int i = 0; i < b.pool.Count; i++)
                    {
                        if (!(b.pool[i] is IChunkEffectSpawner)) continue;
                        if (pick == 0) return b.pool[i];
                        pick--;
                    }
                }
            }
            return b.source;
        }

        static float SpriteExtent(Sprite s)
        {
            if (s == null) return 0f;
            var size = s.bounds.size;
            return Mathf.Max(size.x, size.y) * 0.5f;
        }

        // ── what a blast looks like ───────────────────────────────────────────────────────────────────────

        /// One blast asset as the stage shows it: its frames, how fast they play, how big the first one is,
        /// and the average colour of its picture when its pixels can be read.
        sealed class SourceLook
        {
            public Sprite[] frames;
            public float fps;
            public bool loop;
            public float half;
            public Color average;
            public bool hasAverage;

            public bool HasFrames => frames != null && frames.Length > 0;

            /// The frame showing <paramref name="seconds"/> after the blast went off, or null once a one-shot
            /// has run out of frames — the moment the real blast is over.
            public Sprite FrameAt(float seconds)
            {
                if (!HasFrames) return null;
                float rate = fps > 0f ? fps : 12f;
                int index = Mathf.FloorToInt(Mathf.Max(0f, seconds) * rate);
                if (loop) index %= frames.Length;
                else if (index >= frames.Length) return null;
                var frame = frames[index];
                return frame != null ? frame : null;   // a destroyed frame is only fake-null
            }
        }

        static SourceLook LookOf(Object source)
        {
            if (Looks.TryGetValue(source, out var look)) return look;

            look = new SourceLook();
            if (source is IChunkAnimation animation)
            {
                // GetFrames can be a real render on first ask, which is exactly why the answer is cached and
                // why nothing here asks again until the window rebuilds.
                try
                {
                    look.frames = animation.GetFrames();
                    look.fps = animation.Fps;
                    look.loop = animation.Loop;
                }
                catch (System.Exception) { look.frames = null; }   // a half-authored source must not break the picture

                Sprite first = null;
                if (look.frames != null)
                    for (int i = 0; i < look.frames.Length && first == null; i++)
                        if (look.frames[i] != null) first = look.frames[i];
                look.half = SpriteExtent(first);

                // The middle of a blast says most about its colour: the first frame is often a spark, the
                // last a wisp.
                var middle = look.HasFrames ? look.frames[look.frames.Length / 2] : null;
                look.hasAverage = TryAverage(middle, out look.average) || TryAverage(first, out look.average);
            }
            Looks[source] = look;
            return look;
        }

        /// The alpha-weighted average colour of a sprite's opaque pixels. False when they cannot be read.
        static bool TryAverage(Sprite sprite, out Color average)
        {
            average = Color.white;
            if (sprite == null) return false;

            Color32[] px;
            try { px = FragmentCutter.ReadSpritePixels(sprite, out _, out _); }
            catch (System.Exception) { return false; }
            if (px == null) return false;

            double r = 0, g = 0, b = 0, weight = 0;
            int step = Mathf.Max(1, px.Length / 65536);   // a colour, not a census: a big frame is sampled
            for (int i = 0; i < px.Length; i += step)
            {
                var p = px[i];
                if (p.a <= 32) continue;
                double w = p.a;
                r += p.r * w;
                g += p.g * w;
                b += p.b * w;
                weight += w;
            }
            if (weight <= 0) return false;

            double k = 1.0 / (weight * 255.0);
            average = new Color((float)(r * k), (float)(g * k), (float)(b * k), 1f);
            return true;
        }

        /// Where a sprite's picture sits in its texture, its world size, and how far its centre is from its
        /// pivot — what the stage needs to draw it where the runtime's renderer would.
        static bool TryPicture(Sprite sprite, out Texture texture, out Rect uv, out Vector2 size, out Vector2 pivotOffset)
        {
            texture = null;
            uv = default;
            size = default;
            pivotOffset = default;
            if (sprite == null) return false;
            var tex = sprite.texture;
            if (tex == null || tex.width <= 0 || tex.height <= 0) return false;

            Rect rect;
            Vector2 inset;
            try
            {
                rect = sprite.textureRect;
                inset = sprite.textureRectOffset;
            }
            catch (System.Exception) { return false; }   // tightly packed: no single rect to draw
            if (rect.width <= 0f || rect.height <= 0f) return false;

            float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
            texture = tex;
            uv = new Rect(rect.x / tex.width, rect.y / tex.height, rect.width / tex.width, rect.height / tex.height);
            size = rect.size / ppu;
            pivotOffset = (inset + rect.size * 0.5f - sprite.pivot) / ppu;
            return true;
        }

        // ── what a fracture cuts ──────────────────────────────────────────────────────────────────────────

        internal struct CutPart
        {
            public Texture2D texture;
            public Vector2 offset;   // world units, from the source's pivot to the piece's centre
            public Vector2 size;     // world units
        }

        sealed class CutLook
        {
            // The references the resolution was worked out from; any of them changing redoes it.
            public Object visual;
            public Sprite fallback;
            public Sprite resolved;

            // The cut's own inputs; any of them changing recuts.
            public bool attempted;
            public int pieces, minArea, seed;
            public CutPart[] parts;

            public string cardNote, stageNote;

            public void ReleaseParts()
            {
                if (parts != null)
                    for (int i = 0; i < parts.Length; i++)
                        if (parts[i].texture != null) Object.DestroyImmediate(parts[i].texture);
                parts = null;
                attempted = false;
            }
        }

        const string NothingToCut = "Nothing to cut: no source. A Zoe that fires this cuts its own frame.";
        const string NothingToCutOnStage = "Fragment Fracture — " + NothingToCut;

        static CutLook CutOf(FragmentFracture f, int seed)
        {
            Cuts.TryGetValue(f, out var look);
            if (look == null || look.visual != f.sourceVisual || look.fallback != f.source)
            {
                look?.ReleaseParts();
                look = new CutLook { visual = f.sourceVisual, fallback = f.source };
                // The capability's own resolver, so the preview can never cut a different picture than a
                // standalone burst would.
                try { look.resolved = f.ResolveSource(); }
                catch (System.Exception) { look.resolved = null; }
                Cuts[f] = look;
            }

            if (look.resolved == null)
            {
                look.cardNote = NothingToCut;
                look.stageNote = NothingToCutOnStage;
                return look;
            }

            int pieces = Mathf.Clamp(f.pieceCount, FragmentCutter.MinPieces, FragmentCutter.MaxPieces);
            int minArea = Mathf.Max(1, f.minPieceAreaPx);
            if (look.attempted && look.pieces == pieces && look.minArea == minArea && look.seed == seed)
                return look;

            look.ReleaseParts();
            look.attempted = true;
            look.pieces = pieces;
            look.minArea = minArea;
            look.seed = seed;
            look.cardNote = null;
            look.stageNote = null;

            var src = look.resolved;
            IReadOnlyList<FragmentPiece> cut;
            // Uncached on purpose: the runtime cache is a small hot-repeat store for real bursts, and a
            // preview dragging Pieces through its range would only churn it.
            try { cut = FragmentCutter.Cut(src, pieces, minArea, seed, cache: false); }
            catch (System.Exception) { cut = null; }

            if (cut == null || cut.Count == 0)
            {
                bool readable = src.texture != null && src.texture.isReadable;
                look.cardNote = readable
                    ? $"'{src.name}' has no solid pixels to cut."
                    : $"Can't cut '{src.name}': its texture isn't Read/Write enabled.";
                look.stageNote = "Fragment Fracture — " + look.cardNote;
                return look;
            }

            // Fire builds each piece at the source's own pixels-per-unit, so the pieces here are that size.
            float ppu = Mathf.Max(1f, src.pixelsPerUnit);
            var parts = new List<CutPart>(cut.Count);
            for (int i = 0; i < cut.Count; i++)
            {
                var piece = cut[i];
                // The runtime skips a piece it cannot build before taking any draw for it, so dropping it
                // here keeps every later piece on the same draws.
                if (piece.pixels == null || piece.width <= 0 || piece.height <= 0) continue;
                var tex = new Texture2D(piece.width, piece.height, TextureFormat.RGBA32, false)
                {
                    name = "ChunkPreviewPiece",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                tex.SetPixels32(piece.pixels);
                tex.Apply(false);
                parts.Add(new CutPart
                {
                    texture = tex,
                    offset = piece.offsetUnits,
                    size = new Vector2(piece.width, piece.height) / ppu,
                });
            }
            look.parts = parts.ToArray();
            return look;
        }

        // ── what a splash samples ─────────────────────────────────────────────────────────────────────────

        sealed class PixelSamples
        {
            public Vector2[] offsets;
            public Color32[] colors;
            public int count;
        }

        sealed class SplashLook
        {
            // The references the resolution was worked out from; any of them changing redoes it.
            public Sprite own;
            public FragmentFracture fracture;
            public Object fractureVisual;
            public Sprite fractureFallback;
            public Sprite debrisSource;

            public Sprite resolved;
            public PixelSamples samples;
            public string cardNote, stageNote;
        }

        const string WhiteSpray = "No sprite: sprays plain white from the origin, unless a Zoe fires it with its own colours.";
        const string WhiteSprayOnStage = "Palette Splash — " + WhiteSpray;

        static SplashLook SplashOf(ChunkSpec spec, PaletteSplash s)
        {
            var fracture = spec != null ? spec.FirstEnabled<FragmentFracture>() : null;
            var debris = spec != null ? spec.FirstEnabled<DebrisScatter>() : null;
            var debrisSource = debris != null ? debris.sampleSource : null;

            if (Splashes.TryGetValue(s, out var look)
                && look.own == s.sprite
                && ReferenceEquals(look.fracture, fracture)
                && (fracture == null
                    || (look.fractureVisual == fracture.sourceVisual && look.fractureFallback == fracture.source))
                && look.debrisSource == debrisSource)
                return look;

            look = new SplashLook
            {
                own = s.sprite,
                fracture = fracture,
                fractureVisual = fracture != null ? fracture.sourceVisual : null,
                fractureFallback = fracture != null ? fracture.source : null,
                debrisSource = debrisSource,
            };
            // The capability's own resolver (its sprite, else the fracture's), never a copy of its rules.
            try { look.resolved = s.ResolveSprite(spec); }
            catch (System.Exception) { look.resolved = null; }

            // Then the rest of PaletteSplash.Fire's waterfall: a Debris Scatter's sample source, else white.
            look.samples = SamplesOf(look.resolved) ?? SamplesOf(debrisSource);
            if (look.samples == null)
            {
                if (look.resolved == null)
                {
                    look.cardNote = WhiteSpray;
                    look.stageNote = WhiteSprayOnStage;
                }
                else
                {
                    look.cardNote = $"Can't read '{look.resolved.name}' (Read/Write is off, or nothing is " +
                                    "opaque): sprays plain white from the origin.";
                    look.stageNote = "Palette Splash — " + look.cardNote;
                }
            }
            Splashes[s] = look;
            return look;
        }

        /// Every opaque pixel of a sprite as an offset from its pivot plus its colour — PaletteSplash's own
        /// scan (same rect, same alpha cut, same pixel-centre offsets, same fallback pixels-per-unit), so an
        /// index into it lands on the pixel the runtime's index lands on. Null when nothing can be read.
        static PixelSamples SamplesOf(Sprite source)
        {
            if (source == null) return null;
            if (Samples.TryGetValue(source, out var cached)) return cached;

            PixelSamples result = null;
            var tex = source.texture;
            if (tex != null && tex.isReadable)
            {
                try
                {
                    var rect = source.textureRect;
                    int x = Mathf.FloorToInt(rect.x), y = Mathf.FloorToInt(rect.y);
                    int w = Mathf.Max(1, Mathf.FloorToInt(rect.width));
                    int h = Mathf.Max(1, Mathf.FloorToInt(rect.height));
                    var pixels = tex.GetPixels(x, y, w, h);
                    float ppu = source.pixelsPerUnit > 0f ? source.pixelsPerUnit : 32f;
                    Vector2 pivot = source.pivot;

                    var offsets = new List<Vector2>(pixels.Length / 2);
                    var colors = new List<Color32>(pixels.Length / 2);
                    for (int py = 0; py < h; py++)
                    for (int px = 0; px < w; px++)
                    {
                        Color32 c = pixels[py * w + px];
                        if (c.a < 25) continue;
                        offsets.Add((new Vector2(px + 0.5f, py + 0.5f) - pivot) / ppu);
                        colors.Add(c);
                    }
                    if (offsets.Count > 0)
                        result = new PixelSamples
                        {
                            offsets = offsets.ToArray(),
                            colors = colors.ToArray(),
                            count = offsets.Count,
                        };
                }
                catch (System.Exception) { result = null; }   // readable by flag but not in fact (mid-import)
            }
            Samples[source] = result;
            return result;
        }

        /// A short line saying why a Fragment Fracture or Palette Splash will not put on screen what its card
        /// suggests (nothing to cut, pixels that cannot be read, a plain white spray), or null when it will.
        /// Read from the same caches the stage draws from, so the card and the stage can never disagree.
        internal static string SourceState(ChunkSpec spec, ChunkCapability cap)
        {
            switch (cap)
            {
                case FragmentFracture f: return CutOf(f, Seed(f, f.seed)).cardNote;
                case PaletteSplash s: return SplashOf(spec, s).cardNote;
                default: return null;
            }
        }

        // ── randomness ────────────────────────────────────────────────────────────────────────────────────

        static ChunkRng Seeded(ChunkCapability cap, int authored) => new ChunkRng(Seed(cap, authored));

        /// The seed the PREVIEW uses: the authored one when there is one, else a hash of the capability's own
        /// id. "Reroll every play" is an honest runtime answer and a useless preview one — a picture that
        /// reshuffled on every repaint could not be read, and re-rolling it would need UnityEngine.Random,
        /// which is banned in anything the preview runs.
        static int Seed(ChunkCapability cap, int authored)
        {
            if (authored != 0) return authored;
            unchecked
            {
                uint h = 2166136261u;
                string id = cap != null ? cap.id : null;
                if (!string.IsNullOrEmpty(id))
                    for (int i = 0; i < id.Length; i++) { h ^= id[i]; h *= 16777619u; }
                else h = 0x9E3779B9u;
                int s = (int)(h & 0x7FFFFFFFu);
                return s == 0 ? 1 : s;
            }
        }

        /// How many procedural shard shapes ChunkSprites offers — asked once, because the runtime takes a
        /// draw against exactly this number and a hard-coded copy here would drift the moment a shape is
        /// added, silently shifting every later value in a seeded scatter.
        static int ShapeCount
        {
            get
            {
                if (_shapeCount <= 0) _shapeCount = Mathf.Max(1, ChunkSprites.Count);
                return _shapeCount;
            }
        }
    }
}
