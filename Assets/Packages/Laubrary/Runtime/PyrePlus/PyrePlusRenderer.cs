// PyrePlusRenderer — the parallel renderer for PyrePlusSpec (see PYREPLUS_DESIGN.md).
//
// Mirrors BlastRenderer's determinism contract exactly: pure, static, every random value derived from a
// seeded System.Random keyed by (seed, particleIndex, fieldId, salt) — never UnityEngine.Random, never Time —
// so preview, bake and runtime all produce byte-identical output from the same inputs, and any frame renders
// standalone. Swarm off ⇒ a single centred particle through the Shape fields. Swarm on ⇒ N particles, each
// spawned at its own point on the blast timeline and placed by a spawn-time snapshot of the shape transform
// (T2: Area = uniform-by-area point inside Circle/regular-polygon; Path = a point on the outline positioned by
// swarmProgress, or the swarmCustomX/Y envelopes for a Custom polyline).
//
// T6 — Modifiers: reuses Pyre's own PyreModifier stack directly (spec.modifiers), applied exactly as
// BlastRenderer applies a layer's stack, adapted to PyrePlus's procedural discs. GeometryModifiers fold the
// sample position per candidate pixel (bending each disc) BEFORE the distance/edge test; PixelModifiers recolour
// or drop each lit pixel before it composites; PostModifiers run over the whole finished frame buffer in list
// order. When the list is empty / all-disabled, ALL modifier machinery is skipped and the output is
// byte-identical to the pre-T6 raster (the orchestrator hash-checks this).
using System.Collections.Generic;
using System.Reflection;
using Laubrary.Pyre;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    public static class PyrePlusRenderer
    {
        static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        // ── field-ID registry ──────────────────────────────────────────────────────
        // Every ZUIValue Eval and every seeded random draw takes a UNIQUE field id so independent fields never
        // share an RNG stream (which would silently CORRELATE their draws — e.g. a MinMax size and a MinMax
        // placement landing in lock-step). Slice 1 used bare ints (size=1, alpha=2); these name them and RESERVE
        // the rest for later tasks. LATER TASKS MUST take their ids from this table — never invent a colliding
        // literal, and never reuse an id already spoken for.
        const int FldSize = 1;       // Shape size envelope, particle's own life          [T1]
        const int FldAlpha = 2;      // Shape alpha envelope, particle's own life         [T1]
        const int FldPlacement = 3;  // Area disc sampling (u1/u2 random draw)            [T1]
        const int FldScale = 4;      // shapeScale — shape radius, spawn-time snapshot    [T1]
        const int FldProgress = 5;   // swarmProgress — Path position, spawn-time snapshot [T2]
        const int FldOffsetX = 6;    // shapeOffsetX, spawn-time snapshot                 [T3]
        const int FldOffsetY = 7;    // shapeOffsetY, spawn-time snapshot                 [T3]
        const int FldRotation = 8;   // shapeRotation 2D, spawn-time snapshot             [T3]
        const int FldPitch = 9;      // shapePitch, spawn-time snapshot                   [T3]
        const int FldYaw = 10;       // shapeYaw, spawn-time snapshot                     [T3]
        const int FldSpin = 11;      // per-particle 2D spin, particle's own life         [T7]
        const int FldPathX = 12;     // per-particle position-path X, own life            [T7]
        const int FldPathY = 13;     // per-particle position-path Y, own life            [T7]
        const int FldCustomX = 14;   // swarmCustomX — Custom Path X(progress), sampled at progress p [T2]
        const int FldCustomY = 15;   // swarmCustomY — Custom Path Y(progress), sampled at progress p [T2]
        // NEGATIVE-ID RULE for NEW single fields: ids 1..15 above are taken and 16+ belongs to the per-modifier
        // 8-wide blocks (below), so any brand-new single-field id must be NEGATIVE (-2, -3, …). The value -1 is
        // already spoken for by ModParticleIndex, but that is a *particleIndex* (the `b` slot of Hash), a
        // DIFFERENT argument slot from a field id (the `c` slot); a negative FIELD id can therefore never collide
        // with the -1 particle sentinel, nor with any positive field/modifier id. Real particles are 0..N-1, so a
        // field keyed (particle i≥0, fid<0) shares no RNG stream with a modifier's (particle -1, fid≥16) draw.
        const int FldSpawnTiming = -2;   // swarmSpawnTiming — remaps a particle's number → its spawn moment [F3]
        const int FldGemTilt = -3;       // gemTilt — Gem world tilt about X, particle's own life          [Gem]
        const int FldGemEdgeGlow = -4;   // gemEdgeGlow — Gem edge-halo pulse strength, particle's own life [Gem]
        const int FldGemInnerGlow = -5;  // gemInnerGlow — Gem facet inner-glow pulse strength, own life     [Gem]
        // (Gem also REUSES existing ids: FldSize for its radius R, FldAlpha for its output alpha, FldSpin for its
        //  3D yaw, and FldPathX/FldPathY for the shared travel offset — no new ids for those.)
        // Modifiers (T6): each PyreModifier in spec.modifiers owns an 8-wide field-id BLOCK starting at
        // FldModifier + listIndex*8, so a modifier's local field id (its Prepare's fid, 0..7) maps to
        // FldModifier + listIndex*8 + fid. Since every modifier has a distinct list index the blocks never
        // overlap, and starting at 16 they never collide with the single-field ids 1..15 above. Mirrors
        // BlastRenderer's own `1000 + uid*8 + fid` keying, just rebased for PyrePlus.
        const int FldModifier = 16;
        // Modifier params are frame-global (not per-particle), so their Eval uses this sentinel particle index —
        // real particles are 0..N-1, so -1 never shares a Min-Max RNG stream with a particle draw. Mirrors
        // BlastRenderer feeding its GlobalLayerId (-1) as the layer id for global modifiers.
        const int ModParticleIndex = -1;

        /// One swarm particle's spawn data: where it lands on the blast timeline and its absolute canvas-pixel
        /// position. RenderFrame consumes this; the preview overlay (T5) calls ComputeSpawns to draw a dot at
        /// each particle's ACTUAL computed spawn location.
        public struct SpawnPoint
        {
            public float spawnLife;   // this particle's spawn point on the blast timeline, [0..1]
            public Vector2 pos;       // absolute canvas-pixel position (origin = buffer's (0,0))
            public float zNorm;       // pseudo-3D depth after the shape tilt, in [-1..1]: +1 nearest the viewer,
                                      // -1 farthest, exactly 0 when untilted. Drives depth shading here (nearer =
                                      // bigger + brighter); T5's overlay depth-codes each dot with it too.
        }

        public static Color32[] RenderFrame(PyrePlusSpec spec, int frameIndex)
        {
            int W = spec != null ? spec.Width : 1;
            int H = spec != null ? spec.Height : 1;
            var buf = new Color32[W * H];
            Color32 bg = spec != null ? (Color32)spec.background : Transparent;
            for (int i = 0; i < buf.Length; i++) buf[i] = bg;
            if (spec == null) return buf;

            int frames = Mathf.Max(1, spec.frameCount);
            float life = frames > 1 ? frameIndex / (float)(frames - 1) : 0f;

            // Build + Prepare the geometry/pixel modifier stack for THIS frame. Empty (list null/empty or every
            // entry disabled/null) ⇒ ModSet.Empty ⇒ DrawParticle takes its byte-identical fast path and ApplyPost
            // below is a no-op, so a no-modifier asset renders exactly as before T6 (orchestrator hash-checked).
            ModSet mods = BuildMods(spec, life);
            // Per-frame wobble phase handed to every GeometryModifier.InverseWarp — matches BlastRenderer.framePhase
            // (note the divide by `frames`, not frames-1, deliberately mirroring BlastRenderer).
            float phase = frames > 1 ? (frameIndex / (float)frames) * Mathf.PI * 2f : 0f;

            if (!spec.swarmEnabled)
                // Single particle, at the centre, on its own life = the blast life. (Slice-1 path; now also warps.)
                DrawParticle(buf, W, H, W * 0.5f, H * 0.5f, life, spec, 0, mods, phase, frameIndex);
            else
                RenderSwarm(buf, W, H, life, spec, mods, phase, frameIndex);

            // Whole-frame post passes (Bloom / Outline / Kaleidoscope) after every particle has composited, in
            // list order — exactly the stage BlastRenderer runs its global PostModifiers at.
            ApplyPost(spec, buf, W, H, life, frameIndex);
            return buf;
        }

        // ── modifiers (T6) ───────────────────────────────────────────────────────────
        // The geometry + pixel modifiers resolved & prepared for the current frame. Sorted/arranged the way
        // BlastRenderer's ModStack is; PostModifiers are NOT here (they run buffer-wide in ApplyPost).
        readonly struct ModSet
        {
            public readonly GeometryModifier[] geo;   // enabled, ascending by WarpPass; InverseWarp applied in REVERSE (highest pass first), mirroring BlastRenderer.ApplyGeo
            public readonly PixelModifier[] pix;      // enabled, list order
            public ModSet(GeometryModifier[] g, PixelModifier[] p) { geo = g; pix = p; }
            public bool AnyGeo => geo != null && geo.Length > 0;
            public bool AnyPix => pix != null && pix.Length > 0;
            public bool Any => AnyGeo || AnyPix;
            public static readonly ModSet Empty = new ModSet(System.Array.Empty<GeometryModifier>(), System.Array.Empty<PixelModifier>());
        }

        // Collect + Prepare this frame's enabled Geometry/Pixel modifiers (Post ones are handled by ApplyPost).
        // Each modifier's animatable params resolve through the SAME closure shape BlastRenderer builds — Eval
        // keyed on the blast life, the seed, the modifier-scope particle sentinel, and this modifier's own 8-wide
        // field-id block (see the registry). Returns ModSet.Empty when nothing applies, which is the byte-identical
        // gate. EdgeModifier / SimulationModifier have no apply stage in PyrePlus's disc raster, so they are
        // ignored here (and the editor's add-menu never offers them).
        static ModSet BuildMods(PyrePlusSpec spec, float life)
        {
            var list = spec.modifiers;
            if (list == null || list.Count == 0) return ModSet.Empty;
            List<GeometryModifier> geo = null;
            List<PixelModifier> pix = null;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || !m.enabled) continue;
                if (m is PostModifier) continue;
                int idx = i;   // Prepare invokes the closure synchronously, but copy the loop var anyway for hygiene
                m.Prepare((v, fid) => Eval(v, life, spec.seed, ModParticleIndex, FldModifier + idx * 8 + fid));
                if (m is GeometryModifier gm) (geo ??= new List<GeometryModifier>()).Add(gm);
                else if (m is PixelModifier pm) (pix ??= new List<PixelModifier>()).Add(pm);
            }
            if (geo == null && pix == null) return ModSet.Empty;

            GeometryModifier[] geoArr;
            if (geo == null) geoArr = System.Array.Empty<GeometryModifier>();
            else
            {
                geoArr = geo.ToArray();
                // Stable insertion sort ascending by WarpPass (mirrors BlastRenderer.SortGeoStable): a higher-pass
                // warp reframes the shape first, so ApplyGeo (walking from the end) applies it OUTERMOST. Same-pass
                // modifiers keep authoring order.
                for (int a = 1; a < geoArr.Length; a++)
                {
                    var e = geoArr[a];
                    int p = e.WarpPass;
                    int b = a - 1;
                    while (b >= 0 && geoArr[b].WarpPass > p) { geoArr[b + 1] = geoArr[b]; b--; }
                    geoArr[b + 1] = e;
                }
            }
            return new ModSet(geoArr, pix == null ? System.Array.Empty<PixelModifier>() : pix.ToArray());
        }

        // Undo the geometry modifiers on a pixel offset from the canvas centre (highest WarpPass first), in
        // shape-aware context — the exact fold BlastRenderer.ApplyGeo performs.
        static Vector2 ApplyGeo(GeometryModifier[] geo, Vector2 off, float phase, in GeoCtx ctx)
        {
            for (int i = geo.Length - 1; i >= 0; i--) off = geo[i].InverseWarp(off, phase, ctx);
            return off;
        }

        // Run the pixel modifiers on one lit pixel; false = drop it. Mirrors BlastRenderer.ApplyPix.
        static bool ApplyPix(PixelModifier[] pix, ref Color col, ref float alpha, int x, int y, float wx, float wy,
                             int frame, float crossFrac, float life, int hash, int W, int H)
        {
            var info = new PixelInfo(x, y, wx, wy, frame, crossFrac, life, hash, W, H);
            for (int i = 0; i < pix.Length; i++)
                if (!pix[i].ApplyPixel(ref col, ref alpha, info)) return false;
            return true;
        }

        // Whole-frame post passes after everything composites, in list order — the stage & keying of
        // BlastRenderer's global PostModifier loop.
        static void ApplyPost(PyrePlusSpec spec, Color32[] buf, int W, int H, float life, int frameIndex)
        {
            var list = spec.modifiers;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || !m.enabled) continue;
                if (m is PostModifier post)
                {
                    SetPostContext(post, life, spec.seed, frameIndex);
                    int idx = i;
                    m.Prepare((v, fid) => Eval(v, life, spec.seed, ModParticleIndex, FldModifier + idx * 8 + fid));
                    post.Apply(buf, W, H);
                }
            }
        }

        // PostModifier exposes its per-frame context (life/seed/frameIndex) through INTERNAL setters — BlastRenderer,
        // in the same assembly, calls them directly (SetLife/SetSeed/SetFrameIndex). PyrePlus is a SEPARATE assembly
        // with no InternalsVisibleTo, and may not modify Pyre, so it reaches them by reflection: resolved once,
        // null-guarded so a rename degrades to "context not set" instead of throwing. This keeps seed/frame-dependent
        // post passes deterministic and correct (e.g. Dissolve's per-frame Scatter churn, Kaleidoscope's Vary
        // seeding) exactly as they render inside Pyre.
        static MethodInfo _postSetLife, _postSetSeed, _postSetFrame;
        static bool _postReflectResolved;
        static void SetPostContext(PostModifier post, float life, int seed, int frameIndex)
        {
            if (!_postReflectResolved)
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                var t = typeof(PostModifier);
                _postSetLife = t.GetMethod("SetLife", F);
                _postSetSeed = t.GetMethod("SetSeed", F);
                _postSetFrame = t.GetMethod("SetFrameIndex", F);
                _postReflectResolved = true;
            }
            _postSetLife?.Invoke(post, new object[] { life });
            _postSetSeed?.Invoke(post, new object[] { seed });
            _postSetFrame?.Invoke(post, new object[] { frameIndex });
        }

        // ── swarm ──────────────────────────────────────────────────────────────────

        static void RenderSwarm(Color32[] buf, int W, int H, float life, PyrePlusSpec spec,
                                in ModSet mods, float phase, int frameIndex)
        {
            var spawns = new List<SpawnPoint>(Mathf.Max(2, spec.swarmCount));
            ComputeSpawns(spec, spawns);

            for (int i = 0; i < spawns.Count; i++)
            {
                var sp = spawns[i];
                // Each particle's own life clock: 0 at its spawn frame, 1 at its death. Skip when not alive yet
                // or already dead this frame. All Shape fields (size/alpha/colour) then evaluate at `own`, exactly
                // as the single particle evaluates them at `life`.
                float own = (life - sp.spawnLife) / Mathf.Max(0.0001f, spec.swarmParticleLife);
                if (own < 0f || own > 1f) continue;
                // Depth shading from the spawn-time tilt: nearer parts (zNorm > 0) draw bigger and brighter, far
                // parts smaller and dimmer. Both multipliers are exactly 1 at zNorm == 0, so an untilted swarm
                // renders byte-identical to the pre-T3 output.
                float sizeMul = Mathf.Clamp(1f + 0.35f * sp.zNorm, 0.5f, 1.6f);
                float brightMul = Mathf.Clamp(1f + 0.30f * sp.zNorm, 0.55f, 1.45f);
                DrawParticle(buf, W, H, sp.pos.x, sp.pos.y, own, spec, i, mods, phase, frameIndex, sizeMul, brightMul);
            }
        }

        /// Compute every swarm particle's spawn life (its point on the blast timeline) and spawn POSITION
        /// (absolute canvas-pixel coords). Pure and deterministic — RenderSwarm consumes it, and the preview
        /// overlay (T5) calls it directly to draw a dot at each particle's real spawn location. Fills `into`
        /// (cleared first); allocation-light beyond the list's own growth. Empty when the swarm is off.
        public static void ComputeSpawns(PyrePlusSpec spec, List<SpawnPoint> into)
        {
            if (into == null) return;
            into.Clear();
            if (spec == null || !spec.swarmEnabled) return;

            int n = Mathf.Max(2, spec.swarmCount);
            float cx = spec.Width * 0.5f;
            float cy = spec.Height * 0.5f;
            float window = Mathf.Clamp01(spec.swarmSpawnWindow);

            for (int i = 0; i < n; i++)
            {
                // Spawn moment inside the window, remapped by swarmSpawnTiming. IsLinear01 recognises the default
                // (Curve, linear 0→1) — and a null field (legacy asset) is treated the same — and takes the EXACT
                // fast path, computing spawnLife with the pre-timing arithmetic `window * i / (n-1)` VERBATIM, so a
                // default/linear-timed swarm stays byte-identical (the orchestrator hash-checks this; re-associating
                // the multiply as window*(i/(n-1)) would risk ULP drift for non-power-of-two windows). A non-linear
                // curve instead remaps the even fraction `frac` (0 = first particle, 1 = last) to a spawn moment t
                // in [0,1]; MinMax makes each particle's moment a per-particle random draw; Static s clusters them
                // all at s. n ≥ 2 so (n-1) ≥ 1. particle 0 lands at spawnLife 0 in the linear case.
                float spawnLife;
                if (spec.swarmSpawnTiming == null || IsLinear01(spec.swarmSpawnTiming))
                    spawnLife = window * i / (n - 1);
                else
                {
                    float frac = i / (float)(n - 1);
                    float t = Mathf.Clamp01(Eval(spec.swarmSpawnTiming, frac, spec.seed, i, FldSpawnTiming));
                    spawnLife = window * t;
                }

                // SPAWN-TIME SNAPSHOT — the ENTIRE shape transform (radius, offset, rotation, pitch, yaw) is
                // evaluated at THIS particle's spawn life, NOT the current frame, so an animated transform leaves a
                // growing trail of placements rather than retroactively resizing/rotating/sliding already-placed
                // particles (the same live-vs-snapshot split Pyre's scatter makes: ringExpand is live, but
                // spawnRadius/ringStartAngle are spawn-snapshot). Radius first — both the local placement and the
                // depth normalize below need it.
                float r = Mathf.Max(0f, Eval(spec.shapeScale, spawnLife, spec.seed, i, FldScale));
                float snap = spec.shapeScaleSnap;
                if (snap > 0f) r = Mathf.Round(r / snap) * snap;

                // 1) Local placement (T2): a point on/inside the shape, radius already baked in. PlaceParticle
                //    returns it in ABSOLUTE canvas pixels (centre baked in as cx/cy); we take the local offset
                //    from that below only when a transform is actually active.
                Vector2 baseAbs = PlaceParticle(spec, i, spawnLife, cx, cy, r);

                // Shared shape transform, each field the same spawn-time snapshot as r above. Every step is an exact
                // no-op at its default (rotation/pitch/yaw 0, offset 0), so a swarm with all transform fields at
                // defaults short-circuits to the untransformed T2 position `baseAbs` — byte-identical to pre-T3.
                float rot   = Eval(spec.shapeRotation, spawnLife, spec.seed, i, FldRotation);
                float yaw   = Eval(spec.shapeYaw,      spawnLife, spec.seed, i, FldYaw);
                float pitch = Eval(spec.shapePitch,    spawnLife, spec.seed, i, FldPitch);
                float offX  = Eval(spec.shapeOffsetX,  spawnLife, spec.seed, i, FldOffsetX);
                float offY  = Eval(spec.shapeOffsetY,  spawnLife, spec.seed, i, FldOffsetY);

                Vector2 pos;
                float zNorm;
                if (rot == 0f && yaw == 0f && pitch == 0f && offX == 0f && offY == 0f)
                {
                    // Nothing to apply → keep the exact T2 pixel position (this is what guarantees the byte-
                    // identical hash; re-deriving cx + (baseAbs - cx) would risk sub-ULP round-trip drift).
                    pos = baseAbs;
                    zNorm = 0f;
                }
                else
                {
                    // Work in shape-local coords relative to the centre; the radius is already inside baseAbs.
                    float x = baseAbs.x - cx, y = baseAbs.y - cy, z = 0f;

                    // 2) 2D rotation of the local point, counter-clockwise (y-up), degrees.
                    if (rot != 0f)
                    {
                        float a = rot * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                        float nx = x * c - y * s;
                        float ny = x * s + y * c;
                        x = nx; y = ny;
                    }

                    // 3) Pseudo-3D tilt of (x, y, 0). YAW first — rotate about the vertical y axis in the (x,z)
                    //    plane; z gains x·sin(yaw), so yaw 90° sends every point to x≈0 (shape → vertical line).
                    if (yaw != 0f)
                    {
                        float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                        float nx = x * c - z * s;
                        float nz = x * s + z * c;
                        x = nx; z = nz;
                    }
                    //    THEN PITCH — rotate about the horizontal x axis in the (y,z) plane; z gains y·sin(pitch),
                    //    so pitch 90° sends every point to y≈0 (shape → horizontal line).
                    if (pitch != 0f)
                    {
                        float a = pitch * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                        float ny = y * c - z * s;
                        float nz = y * s + z * c;
                        y = ny; z = nz;
                    }

                    // 4) Depth normalize against the (snapped) radius: +1 nearest the viewer, -1 farthest, 0 flat.
                    zNorm = Mathf.Clamp(z / Mathf.Max(1e-3f, r), -1f, 1f);
                    // 5) Perspective feel: nearer parts spread outward slightly (persp == 1 exactly at zNorm 0).
                    float persp = 1f + 0.25f * zNorm;
                    // 6) Screen-space offset of the whole shape, applied last.
                    pos = new Vector2(cx + offX + x * persp, cy + offY + y * persp);
                }

                into.Add(new SpawnPoint { spawnLife = spawnLife, pos = pos, zNorm = zNorm });
            }
        }

        /// Place one swarm particle in absolute canvas-pixel coords, given its spawn-time-snapshot shape radius
        /// `r` (circumradius) and index `i`. THE single source of placement truth — RenderSwarm and T5's preview
        /// overlay both get every dot from ComputeSpawns, i.e. from here. Pure/deterministic: any randomness is a
        /// System.Random keyed via Hash by (seed, i, field), stable across frames, never UnityEngine.Random.
        ///   Area — a uniform-by-AREA random point INSIDE the shape (disc for Circle/Custom, fan-triangulated
        ///          regular polygon otherwise).
        ///   Path — a point ON the shape's outline, positioned by progress (see below).
        /// Geometry conventions: regular N-gon has vertex 0 at the TOP (angle +90°), vertices counter-clockwise;
        /// side count Triangle 3 / Square 4 / Pentagon 5 / Hexagon 6.
        static Vector2 PlaceParticle(PyrePlusSpec spec, int i, float spawnLife, float cx, float cy, float r)
        {
            var kind = spec.swarmShapeKind;

            if (spec.swarmSpawnMode == SwarmSpawnMode.Area)
            {
                // Custom is Path-only (the UI enforces it), so Area+Custom falls back to a disc — as does Circle.
                if (kind == SwarmShapeKind.Circle || kind == SwarmShapeKind.Custom)
                {
                    Vector2 off = SampleDisc(r, spec.seed, i);
                    return new Vector2(cx + off.x, cy + off.y);
                }
                return SamplePolygonArea(cx, cy, r, SideCount(kind), spec.seed, i);
            }

            // Path mode. Progress is a SPAWN-TIME SNAPSHOT: sample swarmProgress at THIS particle's own spawn
            // life, NOT the current frame — so a Curve/rewinding progress leaves a trail of placements instead of
            // retroactively sliding already-placed particles. A MinMax swarmProgress falls out of Eval as a
            // per-particle random point along the path (no special-casing). Path mode uses NO disc randomness of
            // its own — the only randomness is whatever mode swarmProgress itself is in.
            float pRaw = Eval(spec.swarmProgress, spawnLife, spec.seed, i, FldProgress);

            if (kind == SwarmShapeKind.Custom)
            {
                // Custom is an OPEN polyline (it has two distinct ends), so progress CLAMPS — p<0 pins to the
                // start, p>1 pins to the end. The two envelopes ARE the path: x(p), y(p) as canvas-pixel offsets
                // from the centre, sampled at progress p. p=0 is the path's start, p=1 its end.
                float pc = Mathf.Clamp01(pRaw);
                float ox = Eval(spec.swarmCustomX, pc, spec.seed, i, FldCustomX);
                float oy = Eval(spec.swarmCustomY, pc, spec.seed, i, FldCustomY);
                return new Vector2(cx + ox, cy + oy);
            }

            // Closed shapes (Circle + regular N-gons) WRAP instead of clamping, so the spawn point can run around
            // the outline more than once: p above 1 means extra laps. The fractional part p - floor(p) keeps p in
            // [0,1); it sends every integer to the path start (0) and folds negatives up too (floor(-0.3) = -1 →
            // 0.7). BYTE-IDENTITY: for p in [0,1) floor(p) is exactly 0, so p is unchanged — identical to the old
            // Clamp01(p); at p == 1 exactly wrap gives 0, the SAME position the old Clamp01(1)=1 produced (Circle:
            // angle π/2, the top; N-gon: vertex 0, the top). So every progress value in [0,1] renders exactly as
            // before (orchestrator hash-verified), and only progress OUTSIDE [0,1] — previously unreachable under
            // Clamp01 — newly maps onto further laps.
            float p = pRaw - Mathf.Floor(pRaw);

            if (kind == SwarmShapeKind.Circle)
            {
                // Counter-clockwise from the top (+90°): p=0 at the top, p=1 once around back to the top.
                float ang = Mathf.PI / 2f + 2f * Mathf.PI * p;
                return new Vector2(cx + r * Mathf.Cos(ang), cy + r * Mathf.Sin(ang));
            }

            // Regular N-gon: walk the closed perimeter by arc length. Every side is the same length, so p·N is
            // directly the (segment index + fraction) arc-length parameter — no need to accumulate side lengths.
            // p=0 is vertex 0 (top), p=1 wraps back to vertex 0.
            int n = SideCount(kind);
            float t = p * n;
            int k = (int)t;
            float frac = t - k;
            if (k >= n) { k = n - 1; frac = 1f; }        // defensive: wrapped p is < 1 so t < n and this can't fire
            Vector2 a = PolyVertex(cx, cy, r, k, n);
            Vector2 b = PolyVertex(cx, cy, r, k + 1, n); // k+1 may equal n; trig is periodic → vertex 0
            return Vector2.Lerp(a, b, frac);
        }

        /// Side count for a regular-polygon shape kind (0 for Circle/Custom, which are not regular polygons).
        static int SideCount(SwarmShapeKind kind)
        {
            switch (kind)
            {
                case SwarmShapeKind.Triangle: return 3;
                case SwarmShapeKind.Square:   return 4;
                case SwarmShapeKind.Pentagon: return 5;
                case SwarmShapeKind.Hexagon:  return 6;
                default:                      return 0;
            }
        }

        /// Absolute canvas-pixel position of vertex `k` of a regular `n`-gon (circumradius `r`, centre cx/cy).
        /// Vertex 0 is at the TOP (angle +90°) and vertices advance counter-clockwise. `k` may exceed n-1; the
        /// trig is periodic so k and k%n coincide (used when lerping the last side back to vertex 0).
        static Vector2 PolyVertex(float cx, float cy, float r, int k, int n)
        {
            float ang = Mathf.PI / 2f + 2f * Mathf.PI * k / n;
            return new Vector2(cx + r * Mathf.Cos(ang), cy + r * Mathf.Sin(ang));
        }

        /// A uniform-by-AREA random point inside a regular `n`-gon (circumradius `r`, centre cx/cy). Fan-
        /// triangulates the polygon into n congruent triangles sharing the centre; draws ONE System.Random keyed
        /// exactly like SampleDisc — (seed, particleIndex, FldPlacement) — and takes three doubles from that one
        /// stream: u0 picks the triangle (congruent ⇒ uniform by area), u1/u2 sample uniformly inside it via the
        /// standard sqrt barycentric trick. One rng, one stream ⇒ per-particle stable across frames.
        static Vector2 SamplePolygonArea(float cx, float cy, float r, int n, int seed, int particleIndex)
        {
            var rng = new System.Random(Hash(seed, particleIndex, FldPlacement, 0));
            double u0 = rng.NextDouble();
            double u1 = rng.NextDouble();
            double u2 = rng.NextDouble();

            int tri = Mathf.Clamp((int)(u0 * n), 0, n - 1);
            Vector2 A = PolyVertex(cx, cy, r, tri, n);       // rim vertices of the chosen fan triangle
            Vector2 B = PolyVertex(cx, cy, r, tri + 1, n);   // C = centre (cx, cy)

            float s = Mathf.Sqrt((float)u1);
            float wC = 1f - s;                 // P = C·(1-s) + A·(s·(1-u2)) + B·(s·u2)
            float wA = s * (1f - (float)u2);
            float wB = s * (float)u2;
            return new Vector2(cx * wC + A.x * wA + B.x * wB,
                               cy * wC + A.y * wA + B.y * wB);
        }

        /// A uniformly-distributed random point inside a disc of the given radius — sqrt-distributed radius so
        /// points spread evenly by AREA (not clumped at the centre), angle uniform. Seeded per
        /// (seed, particleIndex, FldPlacement) so the draw is stable across frames and identical in
        /// preview/bake/runtime.
        static Vector2 SampleDisc(float radius, int seed, int particleIndex)
        {
            var rng = new System.Random(Hash(seed, particleIndex, FldPlacement, 0));
            float u1 = (float)rng.NextDouble();
            float u2 = (float)rng.NextDouble();
            float rr = radius * Mathf.Sqrt(u1);
            float ang = u2 * 2f * Mathf.PI;
            return new Vector2(rr * Mathf.Cos(ang), rr * Mathf.Sin(ang));
        }

        // sizeMul/brightMul default to 1 → the swarm-off single-particle call (and any untilted swarm particle,
        // whose zNorm is 0) draws byte-identical to the pre-T3 path: radius unchanged, colour passed through raw.
        // `mods` carries this frame's geometry+pixel modifiers (ModSet.Empty ⇒ the fast path below, byte-identical
        // to pre-T6). Both the single-particle and swarm callers share this one method so warps apply to either.
        static void DrawParticle(Color32[] buf, int W, int H, float cx, float cy, float life,
                                 PyrePlusSpec spec, int particleIndex, in ModSet mods, float phase, int frameIndex,
                                 float sizeMul = 1f, float brightMul = 1f)
        {
            // Gem FORM branches FIRST, before any Disc arithmetic below, so the Disc path stays textually
            // untouched and its output byte-identical (the orchestrator hash-gates Disc). `life` here IS the
            // particle's own life clock in both callers (blast life for the single particle, `own` for a swarm one).
            if (spec.shapeForm == ShapeForm.Gem)
            {
                DrawGem(buf, W, H, cx, cy, life, spec, particleIndex, mods, frameIndex, sizeMul, brightMul);
                return;
            }

            float radius = Mathf.Max(0f, Eval(spec.size, life, spec.seed, particleIndex, FldSize));
            radius *= sizeMul;                    // depth size shading (exact no-op at sizeMul == 1)
            if (radius <= 0.01f) return;
            float alpha = Mathf.Clamp01(Eval(spec.alpha, life, spec.seed, particleIndex, FldAlpha));
            if (alpha <= 0.002f) return;

            // Per-particle travel (T7): the particle's OWN path after birth — canvas-pixel offsets ADDED to its
            // centre on its own life clock (this method's `life` IS that own clock in both callers: the single
            // particle's own life == the blast life, and a swarm particle's is `own`). Applied before BOTH the fast
            // raster and the modifier raster below, so the whole disc — bounds and warp context included — travels
            // as a unit. Guarded to an exact no-op when BOTH values are Static 0, keeping a default asset (and its
            // fast-path bounds/arith) byte-identical; a Curve/MinMax always evaluates.
            if (!(IsStaticZero(spec.particlePathX) && IsStaticZero(spec.particlePathY)))
            {
                cx += Eval(spec.particlePathX, life, spec.seed, particleIndex, FldPathX);
                cy += Eval(spec.particlePathY, life, spec.seed, particleIndex, FldPathY);
            }

            Color col = spec.colorOverLife != null ? spec.colorOverLife.Evaluate(life) : Color.white;
            // Depth brightness shading: scale RGB (alpha untouched) and clamp each channel to [0,1]. Guarded so
            // brightMul == 1 passes the colour through byte-for-byte (the swarm-off and untilted-swarm paths).
            float cr = col.r, cg = col.g, cb = col.b;
            if (brightMul != 1f)
            {
                cr = Mathf.Clamp01(cr * brightMul);
                cg = Mathf.Clamp01(cg * brightMul);
                cb = Mathf.Clamp01(cb * brightMul);
            }
            // Soft rim: alpha ramps from `soft`·radius out to the edge. 0 softness = a hard pixel disc.
            float soft = Mathf.Clamp01(spec.edgeSoftness);
            float inner = radius * (1f - soft);

            // ── fast path: no modifiers → the exact pre-T6 raster, kept verbatim so the no-modifier hash is
            //    byte-identical (do NOT refactor this loop's arithmetic). ──
            if (!mods.Any)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d > radius) continue;
                        float edge = d <= inner ? 1f : 1f - Mathf.InverseLerp(inner, radius, d);
                        float a = alpha * col.a * edge;
                        if (a <= 0.002f) continue;
                        Over(buf, y * W + x, cr, cg, cb, a);
                    }
                return;
            }

            // ── modifier path: fold each candidate pixel's position through the GeometryModifiers (bending the
            //    disc) BEFORE the distance/edge test, then run the PixelModifiers on each lit pixel before it
            //    composites. Mirrors BlastRenderer.RasterShape's per-pixel loop. ──
            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;             // canvas centre — the frame origin the warps fold around
            Vector2 c = new Vector2(cx - ccx, cy - ccy);      // this particle's centre AS AN OFFSET from the canvas centre (GeoCtx.center convention)
            var ctx = new GeoCtx(ccx, ccy, c, radius);        // hHalf/vHalf = canvas halves; centre = this particle; radius = its radius — exactly how BlastRenderer fills GeoCtx per shape
            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }   // a warp can pull any pixel into the disc, so scan the whole canvas (as RasterShape does)
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
            }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7);   // a stable per-particle seed for PixelInfo.hash (modifiers combine it with x/y)

            // Per-particle 2D spin (T7): rotate this particle's OWN pixels in place. Each sample's LOCAL offset
            // (from the particle centre) is rotated by −spin, composed BEFORE the geometry InverseWarp fold — so a
            // warped/textured disc turns as a unit. The per-pixel order is: local offset → spin-rotate → geometry
            // fold → distance test. A plain disc is radially symmetric so spin shows nothing on it — which is why
            // spin lives ONLY on this modifier path (the fast path above can't display it, so it's left untouched
            // and byte-identical). Guarded on spin != 0f exactly: when 0 the rotation is skipped and the offsets fed
            // to the fold/test are the pre-T7 expressions verbatim, so a default asset stays byte-identical. Raster
            // bounds need NO change — rotation preserves the offset's length (a symmetric disc's extent is
            // unchanged) and an active geometry warp already forces the whole-canvas scan.
            float spin = Eval(spec.particleSpin, life, spec.seed, particleIndex, FldSpin);
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    float wx = sx, wy = sy, dx, dy;
                    if (anyGeo)
                    {
                        // Sample offset lifted into canvas-centre space, spin-rotated about THIS particle's centre
                        // BEFORE the geometry fold. When spin is 0 the input is exactly the pre-T7 (sx-ccx, sy-ccy),
                        // so the folded result is bit-identical.
                        Vector2 geoIn;
                        if (doSpin)
                        {
                            float lox = sx - cx, loy = sy - cy;   // local offset from the particle centre
                            geoIn = new Vector2(c.x + (lox * spinCos - loy * spinSin),
                                                c.y + (lox * spinSin + loy * spinCos));
                        }
                        else geoIn = new Vector2(sx - ccx, sy - ccy);
                        Vector2 off = ApplyGeo(mods.geo, geoIn, phase, ctx);
                        wx = ccx + off.x; wy = ccy + off.y;   // geometry-warped absolute position (== sx,sy when no geo warp moves it)
                        dx = off.x - c.x; dy = off.y - c.y;   // warped position − this particle's centre (== sx-cx, sy-cy when no warp/spin)
                    }
                    else if (doSpin)
                    {
                        float lox = sx - cx, loy = sy - cy;   // spin the local offset in place (no geometry warp to fold)
                        dx = lox * spinCos - loy * spinSin;
                        dy = lox * spinSin + loy * spinCos;
                    }
                    else { dx = sx - cx; dy = sy - cy; }
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    float edge = d <= inner ? 1f : 1f - Mathf.InverseLerp(inner, radius, d);
                    float baseA = alpha * col.a * edge;
                    if (baseA <= 0.002f) continue;
                    if (mods.AnyPix)
                    {
                        // RasterShape convention: the colour carries RGB with alpha 1, the real pixel alpha rides
                        // separately, and both are handed to the modifiers (which may recolour, fade, or drop).
                        Color pc = new Color(cr, cg, cb, 1f);
                        float pa = baseA;
                        float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, radius));   // 0 = centre, 1 = edge
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, cr, cg, cb, baseA);
                }
        }

        // ── Gem form ─────────────────────────────────────────────────────────────────
        // A true-3D faceted crystal, ported faithfully from the approved Diamond3DProbe and generalized to n
        // girdle sides. Octahedral solid (crown fan + pavilion fan + n-gon girdle), convex ⇒ backface culling
        // ONLY (no depth sort). Per-pixel realistic lighting: one point light (position derived from the gem's
        // light angles), near-zero ambient, Blinn-Phong specular; hard 1px facet edge lines that catch the light;
        // an edge-halo glow that spills OUTSIDE the silhouette and a facet inner glow, the two pulsing in
        // anti-phase by default. Every pixel composites into the swarm buffer through Over (unlike the prototype's
        // opaque background). Determinism unchanged: pure static math, every value funnelled through Eval/Hash.
        //
        // Signature extends the design's listed one with `in ModSet mods, int frameIndex` because the design's own
        // body mandates running the PixelModifiers per lit gem pixel (which need them). GEOMETRY modifiers are
        // deliberately NOT applied to the gem in this slice — a 3D-consistent warp fold is its own problem; post
        // modifiers still hit the finished buffer later in ApplyPost, unchanged.
        static void DrawGem(Color32[] buf, int W, int H, float cx, float cy, float own,
                            PyrePlusSpec spec, int particleIndex, in ModSet mods, int frameIndex,
                            float sizeMul, float brightMul)
        {
            // R = the size envelope (exactly as Disc evaluates it) × the depth size multiplier. Skip sub-pixel gems.
            float R = Mathf.Max(0f, Eval(spec.size, own, spec.seed, particleIndex, FldSize)) * sizeMul;
            if (R < 1.5f) return;

            // Output alpha rides the particle's own alpha envelope (multiplies EVERY emitted gem pixel — body,
            // line and glow). Early-out on a fully-faded particle, respecting the same a<=0.002 skip as Disc.
            float alphaEnv = Mathf.Clamp01(Eval(spec.alpha, own, spec.seed, particleIndex, FldAlpha));
            if (alphaEnv <= 0.002f) return;

            // Shared per-particle travel (T7): the gem follows the same own-life travel path a disc does. Guarded
            // to an exact no-op when both are Static 0 (a default gem is unaffected). Not in the design's listed
            // pipeline, but the field is shared spec data and a travelling gem is the correct behaviour.
            if (!(IsStaticZero(spec.particlePathX) && IsStaticZero(spec.particlePathY)))
            {
                cx += Eval(spec.particlePathX, own, spec.seed, particleIndex, FldPathX);
                cy += Eval(spec.particlePathY, own, spec.seed, particleIndex, FldPathY);
            }

            int n = Mathf.Clamp(spec.gemSides, 3, 8);
            float crownH = R * spec.gemCrown;
            float pavD = R * spec.gemPavilion;

            // Model: n girdle verts (index 0..n-1) at PI/2 + 2πk/n scaled R, crown apex (n) above, pavilion apex
            // (n+1) below. y is up, +z toward the viewer after rotation.
            var model = new Vector3[n + 2];
            for (int k = 0; k < n; k++)
            {
                float a = Mathf.PI * 0.5f + 2f * Mathf.PI * k / n;
                model[k] = new Vector3(R * Mathf.Cos(a), 0f, R * Mathf.Sin(a));
            }
            model[n] = new Vector3(0f, crownH, 0f);
            model[n + 1] = new Vector3(0f, -pavD, 0f);

            // Faces (generalize the prototype's `& 3` → `% n`): n crown triangles apexT+girdle(i,i+1), then n
            // pavilion triangles apexB+girdle(i+1,i). Outward winding is fixed per face by the centroid test below.
            var faces = new int[2 * n][];
            for (int i = 0; i < n; i++) faces[i] = new[] { n, i, (i + 1) % n };
            for (int i = 0; i < n; i++) faces[n + i] = new[] { n + 1, (i + 1) % n, i };

            // Rotation: yaw about Y first (the gem's 3D yaw = particleSpin on its own life), then the world tilt
            // about X (gemTilt on its own life) — exactly the prototype's Rot, +z toward the viewer.
            float yaw = Eval(spec.particleSpin, own, spec.seed, particleIndex, FldSpin) * Mathf.Deg2Rad;
            float tilt = Eval(spec.gemTilt, own, spec.seed, particleIndex, FldGemTilt) * Mathf.Deg2Rad;
            float cyw = Mathf.Cos(yaw), syw = Mathf.Sin(yaw);
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            Vector3 Rot(Vector3 p)
            {
                var q = new Vector3(p.x * cyw + p.z * syw, p.y, -p.x * syw + p.z * cyw);
                return new Vector3(q.x, q.y * ct - q.z * st, q.y * st + q.z * ct);
            }
            var verts = new Vector3[n + 2];
            for (int k = 0; k < n + 2; k++) verts[k] = Rot(model[k]);

            // Visible faces: outward normal (flip vs centroid so the origin-inside-the-solid convention holds),
            // then backface cull n.z <= 0. Convex + culled ⇒ front faces tile the silhouette with no overlap.
            var visA = new Vector2[2 * n];   // screen triangle (centered coords: vertex.xy)
            var visB = new Vector2[2 * n];
            var visC = new Vector2[2 * n];
            var visP0 = new Vector3[2 * n];  // the three 3D verts, for the barycentric world point
            var visP1 = new Vector3[2 * n];
            var visP2 = new Vector3[2 * n];
            var visNrm = new Vector3[2 * n];
            int visCount = 0;
            // Unique visible edges (screen segments) for the hard line pass, deduped by vertex pair.
            var edgeA = new Vector2[3 * n];
            var edgeB = new Vector2[3 * n];
            int edgeCount = 0;
            var seenEdge = new HashSet<int>();
            for (int fi = 0; fi < faces.Length; fi++)
            {
                var f = faces[fi];
                Vector3 p0 = verts[f[0]], p1 = verts[f[1]], p2 = verts[f[2]];
                Vector3 nrm = Vector3.Cross(p1 - p0, p2 - p0);
                Vector3 centroid = (p0 + p1 + p2) / 3f;
                if (Vector3.Dot(nrm, centroid) < 0f) nrm = -nrm;   // force outward (origin is inside the solid)
                if (nrm.z <= 0f) continue;                          // backface: not seen
                nrm = nrm.normalized;
                visA[visCount] = new Vector2(p0.x, p0.y);
                visB[visCount] = new Vector2(p1.x, p1.y);
                visC[visCount] = new Vector2(p2.x, p2.y);
                visP0[visCount] = p0; visP1[visCount] = p1; visP2[visCount] = p2;
                visNrm[visCount] = nrm;
                visCount++;
                for (int e = 0; e < 3; e++)
                {
                    int i0 = f[e], i1 = f[(e + 1) % 3];
                    int key = i0 < i1 ? i0 * 64 + i1 : i1 * 64 + i0;
                    if (!seenEdge.Add(key)) continue;
                    edgeA[edgeCount] = new Vector2(verts[i0].x, verts[i0].y);
                    edgeB[edgeCount] = new Vector2(verts[i1].x, verts[i1].y);
                    edgeCount++;
                }
            }
            if (visCount == 0) return;

            // Light POSITION from the gem's light angles at distance 3.5·R (distance not exposed); the falloff
            // range is 4.7·R — both the prototype's proportions. Up-left-front for the default -55°/38° angles.
            float lyaw = spec.gemLightYaw * Mathf.Deg2Rad, lpitch = spec.gemLightPitch * Mathf.Deg2Rad;
            float ldist = 3.5f * R, lhoriz = ldist * Mathf.Cos(lpitch);
            Vector3 lightPos = new Vector3(lhoriz * Mathf.Sin(lyaw), ldist * Mathf.Sin(lpitch), lhoriz * Mathf.Cos(lyaw));
            float lrange = 4.7f * R, lrange2 = lrange * lrange;
            Vector3 viewDir = new Vector3(0f, 0f, 1f);
            Color specColor = new Color(0.9f, 0.95f, 1f);   // cool specular tint (prototype)
            Color innerColor = new Color(0.35f, 0.60f, 1f); // inner-glow tint (prototype)
            Color lineColor = spec.gemLineColor;
            float lineW = spec.gemLineWidth;
            float ambient = spec.gemAmbient, specStr = spec.gemSpecular;

            // The two glow strengths on the particle's own life (0..1). Halo radius / inner radius scale with R
            // (prototype constants 7 and 9 at R=30 → 0.24·R and 0.30·R), floored so tiny gems still glow.
            float edgeGlow = Mathf.Clamp01(Eval(spec.gemEdgeGlow, own, spec.seed, particleIndex, FldGemEdgeGlow));
            float innerGlow = Mathf.Clamp01(Eval(spec.gemInnerGlow, own, spec.seed, particleIndex, FldGemInnerGlow));
            float haloR = Mathf.Max(2.5f, 0.24f * R);
            float innerR = Mathf.Max(3f, 0.30f * R);

            // Raster bounds: the screen bounding box of the rotated verts (centered coords), EXPANDED by haloR so
            // the outside halo isn't clipped, then to absolute pixels around (cx, cy).
            float minX = float.MaxValue, maxX = -float.MaxValue, minY = float.MaxValue, maxY = -float.MaxValue;
            for (int k = 0; k < n + 2; k++)
            {
                if (verts[k].x < minX) minX = verts[k].x;
                if (verts[k].x > maxX) maxX = verts[k].x;
                if (verts[k].y < minY) minY = verts[k].y;
                if (verts[k].y > maxY) maxY = verts[k].y;
            }
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx + minX - haloR));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + maxX + haloR));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy + minY - haloR));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + maxY + haloR));

            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7);   // stable per-particle seed for PixelInfo.hash

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    // Centered pixel coords (relative to the gem centre) — the space the model verts live in.
                    float lx = x + 0.5f - cx, ly = y + 0.5f - cy;
                    Vector2 p = new Vector2(lx, ly);

                    // Nearest visible edge distance — drives BOTH glows and the hard line test, inside and out.
                    float edist = float.MaxValue;
                    for (int e = 0; e < edgeCount; e++)
                        edist = Mathf.Min(edist, DistSeg(p, edgeA[e], edgeB[e]));

                    bool hasFace = false, isLine = false;
                    float fr = 0f, fg = 0f, fb = 0f;   // face RGB before glows
                    for (int vi = 0; vi < visCount; vi++)
                    {
                        if (!InTri(p, visA[vi], visB[vi], visC[vi], out float w0, out float w1, out float w2)) continue;
                        hasFace = true;
                        Vector3 P = visP0[vi] * w0 + visP1[vi] * w1 + visP2[vi] * w2;
                        Vector3 toL = lightPos - P;
                        float dist = toL.magnitude;
                        Vector3 L = toL / Mathf.Max(1e-4f, dist);
                        float atten = 1f / (1f + dist * dist / lrange2);
                        float ndl = Mathf.Max(0f, Vector3.Dot(visNrm[vi], L));
                        float lit = ambient + 2.1f * ndl * atten;
                        if (edist <= lineW)
                        {
                            isLine = true;
                            float k = Mathf.Clamp(0.25f + lit, 0f, 1.15f);   // edges catch the light: bright lit, dim shadowed
                            fr = lineColor.r * k; fg = lineColor.g * k; fb = lineColor.b * k;
                        }
                        else
                        {
                            Vector3 Hh = (L + viewDir).normalized;
                            float sp = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(visNrm[vi], Hh)), 48f);
                            Color baseCol = spec.colorOverLife != null ? spec.colorOverLife.Evaluate(own) : Color.white;
                            float sAdd = specStr * sp * atten;
                            fr = baseCol.r * lit + specColor.r * sAdd;
                            fg = baseCol.g * lit + specColor.g * sAdd;
                            fb = baseCol.b * lit + specColor.b * sAdd;
                        }
                        break;   // convex + culled: first hit wins
                    }

                    // Halo (glow level 1): a soft additive halo around every edge line, inside AND out.
                    float halo = Mathf.Pow(Mathf.Max(0f, 1f - edist / haloR), 2f);
                    float haloAmt = 0.85f * edgeGlow * halo;

                    if (hasFace)
                    {
                        // Add the halo (both) and the inner glow (facet interiors, non-line) to the face RGB.
                        fr += lineColor.r * haloAmt; fg += lineColor.g * haloAmt; fb += lineColor.b * haloAmt;
                        if (!isLine)
                        {
                            float core = Mathf.Pow(Mathf.Clamp01(edist / innerR), 1.4f);
                            float innerAmt = 0.75f * innerGlow * core;
                            fr += innerColor.r * innerAmt; fg += innerColor.g * innerAmt; fb += innerColor.b * innerAmt;
                        }
                        // brightMul scales the lit RGB (depth shading), clamped per channel like Disc.
                        float rr = Mathf.Clamp01(fr * brightMul), gg = Mathf.Clamp01(fg * brightMul), bb = Mathf.Clamp01(fb * brightMul);
                        float pa = alphaEnv;
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(Mathf.Sqrt(lx * lx + ly * ly) / Mathf.Max(0.001f, R));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                    else
                    {
                        // Outside-halo glow fragment: a soft additive glow spilling past the silhouette. Its colour
                        // is the halo tint; its ALPHA is the halo strength × the particle alpha, so it reads as a
                        // soft glow when composited through Over.
                        float pa = Mathf.Clamp01(haloAmt) * alphaEnv;
                        if (pa <= 0.002f) continue;
                        float rr = Mathf.Clamp01(lineColor.r * brightMul), gg = Mathf.Clamp01(lineColor.g * brightMul), bb = Mathf.Clamp01(lineColor.b * brightMul);
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(Mathf.Sqrt(lx * lx + ly * ly) / Mathf.Max(0.001f, R));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                }
        }

        // Barycentric point-in-triangle (orthographic: screen bary == plane bary). Weights out. Ported verbatim
        // from the approved gem prototype.
        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float w0, out float w1, out float w2)
        {
            w0 = w1 = w2 = 0f;
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-6f) return false;
            w0 = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
            w1 = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
            w2 = 1f - w0 - w1;
            return w0 >= 0f && w1 >= 0f && w2 >= 0f;
        }

        // Distance from a point to a segment (screen space). Ported verbatim from the approved gem prototype.
        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        static void Over(Color32[] buf, int i, float r, float g, float b, float a)
        {
            var dst = buf[i];
            float da = dst.a * (1f / 255f);
            float outA = a + da * (1f - a);
            if (outA <= 0.0001f) { buf[i] = Transparent; return; }
            float inv = 1f / outA;
            buf[i] = new Color32(
                (byte)(Mathf.Clamp01((r * a + dst.r * (1f / 255f) * da * (1f - a)) * inv) * 255f),
                (byte)(Mathf.Clamp01((g * a + dst.g * (1f / 255f) * da * (1f - a)) * inv) * 255f),
                (byte)(Mathf.Clamp01((b * a + dst.b * (1f / 255f) * da * (1f - a)) * inv) * 255f),
                (byte)(Mathf.Clamp01(outA) * 255f));
        }

        /// True when a value is exactly Static 0 — the default state of the T7 opt-in travel-path fields. Gates
        /// their Evals so a default asset's particle centre (and thus its whole raster) stays byte-identical; a
        /// Curve or MinMax value always evaluates. A null value counts as zero (Eval(null) == 0 too).
        static bool IsStaticZero(ZUIValue v) => v == null || (v.mode == ZUIValue.Mode.Static && v.staticValue == 0f);

        /// True when a value is EXACTLY the default linear identity — Curve mode, exactly two points, p0 == (0,0)
        /// and p1 == (1,1). This is the fast-path gate for swarmSpawnTiming: when true the caller skips Eval and
        /// uses the verbatim pre-timing spawn arithmetic, so a default (or explicitly linear) timing stays
        /// byte-identical rather than round-tripping through Eval → EnvelopeEvaluator. Exponent is intentionally
        /// NOT checked — the fast path returns the even fraction directly, so a segment bend on a 2-point (0,0)→
        /// (1,1) curve is simply treated as the linear it visually approximates; the default factory makes
        /// exponent 1 anyway. Any non-linear curve / MinMax / non-zero Static returns false and goes through Eval.
        static bool IsLinear01(ZUIValue v)
        {
            if (v == null || v.mode != ZUIValue.Mode.Curve || v.points == null || v.points.Count != 2) return false;
            var p0 = v.points[0];
            var p1 = v.points[1];
            return p0.time == 0f && p0.value == 0f && p1.time == 1f && p1.value == 1f;
        }

        /// Evaluate a ZUIValue for a given particle deterministically — Static reads the value, Curve reads
        /// the envelope at the particle's life, MinMax draws once from a seeded RNG keyed by (seed, index,
        /// field). Mirrors BlastRenderer.Eval; MinMax is what makes each particle differ reproducibly.
        static float Eval(ZUIValue v, float life, int seed, int particleIndex, int fieldId)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static: return v.staticValue;
                case ZUIValue.Mode.MinMax:
                {
                    var rng = new System.Random(Hash(seed, particleIndex, fieldId, 0));
                    return Mathf.Lerp(v.min, v.max, (float)rng.NextDouble());
                }
                case ZUIValue.Mode.Curve:
                    // Points are authored in normalized [0..1]; sample directly (ignore duration/warmup/
                    // cooldown — that's the runtime-seconds API, not our frame-baked timeline). EvaluateRaw
                    // would divide life by duration (default 4s), sweeping only the curve's first quarter.
                    return ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(life), v.yMax);
                default: return v.staticValue;
            }
        }

        static int Hash(int a, int b, int c, int d)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h = (h ^ (uint)d) * 16777619u;
                return (int)h;
            }
        }

        public static Texture2D RenderFrameTexture(PyrePlusSpec spec, int frameIndex)
        {
            int W = spec != null ? spec.Width : 1;
            int H = spec != null ? spec.Height : 1;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(RenderFrame(spec, frameIndex));
            tex.Apply();
            return tex;
        }
    }
}
