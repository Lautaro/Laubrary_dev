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
//   2. Three things cannot be computed inside a repaint: a fracture's real cut (FragmentCutter over a
//      readable texture), a splash's real footprint (a full GetPixels scan) and a blast's real content. The
//      first two are approximated over the source's own bounds — the MOTION is exact, the emission POINTS
//      are schematic — and the third is asked once per asset for its first frame's world size and cached.
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
        public float angleDeg;    // squares only
        public float alpha;
        public Color color;
        public ChunkGuideShape shape;
        public int order;         // painted low-to-high; layer slot first, stack position second
        public string label;      // a formation point's firing number, else null
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

        public void Clear()
        {
            Cones.Clear();
            Paths.Clear();
            Guides.Clear();
            Reach = 0f;
            Required = 0f;
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

        /// Colours a guide takes from its Layer-Plan slot. Same six as the Timing lanes, so a lane and the
        /// thing it stands for read as the same object.
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

        // ── caches ────────────────────────────────────────────────────────────────────────────────────────
        // Keyed by the asset, not by the capability, because two blasts pointing at one Pyre must not render
        // it twice. Cleared whenever the window says the recipe changed, so an edited source is re-measured.

        static readonly Dictionary<Object, float> SizeCache = new Dictionary<Object, float>();
        static readonly List<SpawnPlacement> Placements = new List<SpawnPlacement>(16);
        static readonly List<Vector2> PathScratch = new List<Vector2>(256);
        static int _shapeCount;

        /// Forget every measured source size. Called when the recipe (or the asset a card points at) changed.
        internal static void ClearCaches() => SizeCache.Clear();

        // ── the frame ─────────────────────────────────────────────────────────────────────────────────────

        /// Fill <paramref name="into"/> with the recipe's state at <paramref name="time"/> seconds.
        /// <paramref name="includeDisabled"/> draws switched-off capabilities too — never for the picture,
        /// only for the measurement that frames it (see <see cref="Reach"/>).
        internal static void Build(ChunkSpec spec, float time, ChunkPreviewFrame into, bool includeDisabled = false)
        {
            if (into == null) return;
            into.Clear();
            if (spec == null || spec.capabilities == null) return;

            var layers = spec.ResolveLayers();
            bool planned = spec.FirstEnabled<LayerPlan>() != null;
            var stack = spec.capabilities;

            int budget = DotsPerFrame;
            int timedIndex = 0;

            for (int i = 0; i < stack.Count && budget > 0; i++)
            {
                var cap = stack[i];
                if (cap == null) continue;
                if (!cap.OccupiesTime) continue;          // coordinators and modifiers draw through their target
                int slot = timedIndex++;
                // Switched off draws nothing, but it keeps its place in the colour and lane order — the same
                // reason ChunkClock counts it: turning one off must not renumber or restyle the others.
                if (!cap.enabled && !includeDisabled) continue;

                var colour = ColorFor(cap, layers, planned, slot);
                int order = OrderFor(cap, layers, i);

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
        /// narrowed, so the origin and every authored formation point are inside it. A handful of outliers
        /// leaving the picture is the intended trade.
        ///
        /// It is one instant rather than the running one, so the picture does not zoom in and out while the
        /// clock runs; and it counts SWITCHED-OFF capabilities, exactly as the clock's own length does, so
        /// flicking one off to look at the rest cannot rescale everything else under the cursor. Expensive
        /// enough to be worth caching — the window recomputes it only when the recipe changed, never per frame.
        internal static float Reach(ChunkSpec spec, ChunkPreviewFrame scratch)
        {
            if (spec == null || scratch == null) return 1f;

            float length = Mathf.Max(0.05f, ChunkClock.Length(spec));
            Build(spec, length * 0.5f, scratch, includeDisabled: true);
            float fit = Fit(scratch);
            float required = scratch.Required;

            // A recipe whose output is all early or all late can have nothing at all on screen at its
            // midpoint. Framing that to the floor would open the recipe zoomed into empty space, so fall
            // back to the fullest instant instead of to nothing.
            if (fit <= 0f)
            {
                const int Samples = 24;
                for (int s = 0; s <= Samples; s++)
                {
                    Build(spec, length * s / Samples, scratch, includeDisabled: true);
                    fit = Mathf.Max(fit, Fit(scratch));
                    required = Mathf.Max(required, scratch.Required);
                }
            }

            return Mathf.Max(0.5f, Mathf.Max(fit, required));
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

                float t01 = Mathf.Clamp01(local / Mathf.Max(0.01f, life));
                float sizeMul = d.sizeOverLife != null ? Mathf.Max(0f, d.sizeOverLife.Evaluate(t01)) : 1f;
                float alpha = d.alphaOverLife != null ? Mathf.Clamp01(d.alphaOverLife.Evaluate(t01)) : 1f;
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
        // The launch is exact (FragmentFracture.Fire's LerpAngle blend between the aim and each piece's own
        // radial direction, off the same second stream). Where the pieces START is schematic: the real answer
        // is FragmentCutter's partition of a readable texture, which is not something a repaint can afford.
        // Pieces are laid out evenly round the source's own half-extent instead, so the count, the spread and
        // the fall are honest and only the exact seams are not.

        static int Fracture(ChunkSpec spec, FragmentFracture f, float local, bool pending,
                            Color colour, int order, int budget, ChunkPreviewFrame into)
        {
            float centreDeg = f.useBurstDirection ? (spec != null ? spec.directionDeg : 0f) : f.directionDeg;
            float extent = SourceExtent(f);
            AddCone(into, Vector2.zero, centreDeg, f.spreadDeg, ConeRadius(f.speedMax, f.lifeMax),
                    pending ? PendingAlpha : 0.5f, colour, order);
            if (pending)
            {
                into.Guides.Add(new ChunkGuide
                {
                    pos = Vector2.zero, radius = extent, alpha = PendingAlpha,
                    color = colour, shape = ChunkGuideShape.Ring, order = order,
                });
                return 0;
            }

            int n = Mathf.Clamp(f.pieceCount, 1, DotsPerCapability);
            var rng = new ChunkRng(Seed(f, f.seed), 1);   // stream 1, exactly as Fire does

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
            for (int i = 0; i < n; i++)
            {
                // The schematic stand-in for a piece's own offset from the picture's pivot.
                float around = n <= 1 ? centreDeg : (360f * i / n);
                var offset = n <= 1 ? Vector2.zero
                    : new Vector2(Mathf.Cos(around * Mathf.Deg2Rad), Mathf.Sin(around * Mathf.Deg2Rad)) * extent * 0.55f;

                float radial = offset.sqrMagnitude > 1e-8f
                    ? Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg
                    : centreDeg;
                float angle = Mathf.LerpAngle(centreDeg, radial, spread / 180f) * Mathf.Deg2Rad;
                float speed = rng.Range(spdMin, spdMax);
                var velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                float life = rng.Range(lifeLo, lifeHi);
                float spin = rng.Range(spinMin, spinMax) * rng.NextSign();

                if (drawn >= budget) continue;
                if (local > life) continue;

                var flight = Fly(offset, velocity, grav, air, local,
                                 false, 0f, 0f, 0f, false, trailing ? PathScratch : null);

                float t01 = Mathf.Clamp01(local / Mathf.Max(0.01f, life));
                float alpha = f.alphaOverLife != null ? Mathf.Clamp01(f.alphaOverLife.Evaluate(t01)) : 1f;
                float radius = Mathf.Max(0.01f, extent / Mathf.Max(1.4f, Mathf.Sqrt(n)));

                if (trailing) AddTrail(into, PathScratch, trail.interval, local, colour, order - 1);
                if (hitting)
                    into.Guides.Add(new ChunkGuide
                    {
                        pos = flight.position,
                        radius = Mathf.Max(0.006f, radius * Mathf.Max(0.01f, hits.radiusScale) * 2f),
                        alpha = alpha * 0.25f, color = colour,
                        shape = ChunkGuideShape.Ring, order = order,
                    });

                into.Guides.Add(new ChunkGuide
                {
                    pos = flight.position, radius = radius, angleDeg = spin * local,
                    alpha = alpha, color = colour, shape = ChunkGuideShape.Square, order = order,
                });
                drawn++;
            }
            return drawn;
        }

        // ── Palette Splash ────────────────────────────────────────────────────────────────────────────────
        // Same shape of compromise as the fracture: the counts, sizes, speeds, cone (half-spread, as
        // PaletteSplash.Fire uses it) and lives are exact; the footprint the particles are emitted FROM is a
        // scatter inside the source's bounds rather than a per-pixel scan, which needs GetPixels.

        static int Splash(ChunkSpec spec, PaletteSplash s, float local, bool pending,
                          Color colour, int order, int budget, ChunkPreviewFrame into)
        {
            float centreDeg = s.inheritBurstDirection ? (spec != null ? spec.directionDeg : 0f) : s.directionDeg;
            AddCone(into, Vector2.zero, centreDeg, s.spreadDeg * 0.5f, ConeRadius(s.speedMax, s.lifeMax),
                    pending ? PendingAlpha : 0.5f, colour, order);
            if (pending) return 0;

            var rng = Seeded(s, s.seed);
            var sprite = s.ResolveSprite(spec);
            bool footprint = sprite != null && s.emitFromFootprint;
            float extent = sprite != null ? SpriteExtent(sprite) : 0f;

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
                if (sprite != null)
                {
                    // One draw where the runtime takes one (its random index into the sampled pixels), so a
                    // seeded splash keeps its speeds and lives even though the positions are schematic.
                    float u = rng.Next01(), v = rng.Next01();
                    if (footprint) offset = new Vector2(u - 0.5f, v - 0.5f) * (extent * 2f);
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
                float alpha = s.alphaOverLife != null ? Mathf.Clamp01(s.alphaOverLife.Evaluate(t01)) : 1f;

                into.Guides.Add(new ChunkGuide
                {
                    pos = flight.position, radius = Mathf.Max(0.004f, px / ppu * 0.5f),
                    alpha = alpha, color = colour, shape = ChunkGuideShape.Dot, order = order,
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

        static int Blast(ChunkSpec spec, PyreBlast b, float local, bool pending,
                         Color colour, int order, int budget, ChunkPreviewFrame into)
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

            float radius = BlastRadius(b, seed);
            for (int i = 0; i < Placements.Count; i++)
                into.Required = Mathf.Max(into.Required,
                                          ((Vector2)Placements[i].Position).magnitude + radius);

            float blast = Mathf.Max(0.05f, b.blastSeconds);
            var flight = spec != null ? spec.FindModifier<Trajectory>(b) : null;
            if (flight != null && !flight.enabled) flight = null;

            // Firing order, so the numbers say when rather than where. Ties (a FromCentre pair) share a number
            // because they really do go off together.
            int drawn = 0;
            for (int i = 0; i < Placements.Count && drawn < budget; i++)
            {
                var placement = Placements[i];
                var at = (Vector2)placement.Position;
                float fires = placement.Delay;
                int rank = 1;
                for (int k = 0; k < Placements.Count; k++)
                    if (Placements[k].Delay < fires - 0.0001f) rank++;

                float since = local - fires;
                if (pending || since < 0f)
                {
                    into.Guides.Add(new ChunkGuide
                    {
                        pos = at, radius = radius, alpha = PendingAlpha, color = colour,
                        shape = ChunkGuideShape.Ring, order = order,
                        label = Placements.Count > 1 ? rank.ToString() : null,
                    });
                    drawn++;
                    continue;
                }
                if (since > blast) continue;   // gone, not held on its last frame

                var pos = at;
                if (flight != null)
                {
                    var velocity = LaunchOf(flight, spec, placement.Index);
                    var path = Fly(at, velocity, flight.gravity, flight.drag,
                                   flight.untilTargetEnds ? since : Mathf.Min(since, Mathf.Max(0.01f, flight.lifeSeconds)),
                                   false, 0f, 0f, 0f, false, PathScratch);
                    pos = path.position;
                    if (PathScratch.Count >= 2)
                        into.Paths.Add(new ChunkGuidePath
                        {
                            points = Thin(PathScratch),
                            alpha = 0.65f, color = colour, order = order - 1,
                        });
                }

                into.Guides.Add(new ChunkGuide
                {
                    pos = pos, radius = radius,
                    alpha = 1f - Mathf.Clamp01(since / blast),      // fades out over its authored length
                    color = colour, shape = ChunkGuideShape.Disc, order = order,
                    label = Placements.Count > 1 ? rank.ToString() : null,
                });
                drawn++;
            }
            return drawn;
        }

        /// The launch one blast gets from a Trajectory, mirroring Trajectory.Apply's own draws.
        static Vector2 LaunchOf(Trajectory t, ChunkSpec spec, int index)
        {
            var rng = new ChunkRng(Seed(t, t.seed), index);
            float lo = Mathf.Min(t.speedMin, t.speedMax), hi = Mathf.Max(t.speedMin, t.speedMax);
            float speed = rng.Range(lo, hi);
            float centreDeg = t.inheritBurstDirection ? (spec != null ? spec.directionDeg : 0f) : t.directionDeg;
            float rad = (centreDeg + rng.Range(-t.spreadDeg, t.spreadDeg)) * Mathf.Deg2Rad;
            var velocity = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * speed;
            velocity.y += t.upwardBias;
            return velocity;
        }

        // ── flight ────────────────────────────────────────────────────────────────────────────────────────

        internal struct Flight
        {
            public Vector2 position;
            public Vector2 velocity;
            public bool settled;
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
                    if (vel.magnitude < SettleSpeed) { settled = true; vel = Vector2.zero; }
                }

                path?.Add(pos);
            }

            return new Flight { position = pos, velocity = vel, settled = settled };
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
        /// no slot, and its place in the stack when there is no plan at all. Colour says which SLOT, never
        /// which depth — depth is the runtime's own answer (see OrderFor), and grey is exactly the reading
        /// "this one is not in the plan", which is the thing worth seeing at a glance.
        static Color ColorFor(ChunkCapability cap, LayerSpec layers, bool planned, int stackSlot)
        {
            if (!planned) return SlotColors[stackSlot % SlotColors.Length];
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

        /// One blast's radius in world units: half the size of the FIRST FRAME of whatever it spawns, times
        /// the scale it comes out at. Chunks cannot ask a spawner how big it is — IChunkEffectSpawner says
        /// nothing about size — but everything Chunks can spawn also hands over frames, and a frame is a
        /// sprite with real world bounds. Measured once per asset and cached; a source that cannot answer
        /// falls back to a radius that at least reads as a blast rather than as nothing.
        static float BlastRadius(PyreBlast b, int seed)
        {
            const float Fallback = 0.5f;
            var picked = PickSource(b, seed);
            float half = picked != null ? MeasureSource(picked) : 0f;
            if (half <= 0f) half = Fallback;

            float lo = Mathf.Max(0.01f, Mathf.Min(b.scaleMin, b.scaleMax));
            float hi = Mathf.Max(lo, b.scaleMax);
            float scale = new ChunkRng(seed, 977).Range(lo, hi);   // its own stream: a scale must not shift the layout
            return Mathf.Clamp(half * scale, 0.02f, 20f);
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

        static float MeasureSource(Object source)
        {
            if (source == null) return 0f;
            if (SizeCache.TryGetValue(source, out float cached)) return cached;

            float half = 0f;
            if (source is IChunkAnimation animation)
            {
                // GetFrames can be a real render on first ask, which is exactly why the answer is cached and
                // why nothing here asks again until the recipe changes.
                try
                {
                    var frames = animation.GetFrames();
                    if (frames != null)
                        for (int i = 0; i < frames.Length; i++)
                            if (frames[i] != null) { half = SpriteExtent(frames[i]); break; }
                }
                catch (System.Exception) { half = 0f; }   // a half-authored source must not break the picture
            }

            SizeCache[source] = half;
            return half;
        }

        static float SpriteExtent(Sprite s)
        {
            if (s == null) return 0f;
            var size = s.bounds.size;
            return Mathf.Max(size.x, size.y) * 0.5f;
        }

        /// The half-extent of whatever a fracture is cutting, measured once and cached — ResolveSource can
        /// pull frames out of an animated source, which is not a per-repaint operation.
        static float SourceExtent(FragmentFracture f)
        {
            Object key = f.sourceVisual != null ? f.sourceVisual : f.source;
            if (key == null) return 0.4f;
            if (SizeCache.TryGetValue(key, out float cached)) return cached > 0f ? cached : 0.4f;

            float half = 0f;
            try { half = SpriteExtent(f.ResolveSource()); }
            catch (System.Exception) { half = 0f; }
            SizeCache[key] = half;
            return half > 0f ? half : 0.4f;
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
