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
                // Even spawn distribution across the window; particle 0 at 0. n ≥ 2 so (n-1) ≥ 1.
                float spawnLife = window * i / (n - 1);

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
            float p = Mathf.Clamp01(Eval(spec.swarmProgress, spawnLife, spec.seed, i, FldProgress));

            if (kind == SwarmShapeKind.Custom)
            {
                // The two envelopes ARE the path: x(p), y(p) as canvas-pixel offsets from the centre, sampled at
                // progress p. Open (not wrapped): p=0 is the path's start, p=1 its end.
                float ox = Eval(spec.swarmCustomX, p, spec.seed, i, FldCustomX);
                float oy = Eval(spec.swarmCustomY, p, spec.seed, i, FldCustomY);
                return new Vector2(cx + ox, cy + oy);
            }

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
            if (k >= n) { k = n - 1; frac = 1f; }        // p == 1 exactly
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
