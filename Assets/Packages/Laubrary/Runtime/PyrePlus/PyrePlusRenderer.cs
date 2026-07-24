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
using TMPro;
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
        const int FldCrescentBite = -6;  // crescentBite — Crescent mask-disc size, particle's own life      [G2]
        const int FldCrescentAngle = -7; // crescentAngle — Crescent bite facing, particle's own life         [G2]
        const int FldSparkle = -8;       // sparkleDensity Eval + the per-cell presence/twinkle Hash draws     [G2]
        // (Gem also REUSES existing ids: FldSize for its radius R, FldAlpha for its output alpha, FldSpin for its
        //  3D yaw, and FldPathX/FldPathY for the shared travel offset — no new ids for those. Crescent/Sparkle/
        //  Sprite likewise REUSE FldSize/FldAlpha (radius/alpha), FldSpin (2D spin), FldPathX/Y (travel); Sprite
        //  needs no new random-draw id at all. Orb/Ring reuse the SAME Gem ids — FldSize (radius/outer radius),
        //  FldAlpha, FldSpin (Orb: lighting-frame yaw / Ring: in-plane yaw), FldGemTilt, FldGemEdgeGlow,
        //  FldGemInnerGlow, FldPathX/Y — and add NO new ids: ringInner is a plain non-animatable float.)
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

            // Text form: bake + snapshot the SDF atlas for this frame's string/font ONCE per frame (see
            // EnsureTextGlyphs). _textReady gates every Text draw below; when false (no readable font) each Text
            // particle falls back to the Disc raster instead. Non-Text forms never read _textReady.
            _textReady = spec.shapeForm == ShapeForm.Text && EnsureTextGlyphs(spec);

            if (!spec.swarmEnabled)
            {
                // Text + a usable font: the whole string is ONE centred line (each char its own particle), painted
                // far→near for correct occlusion. Every other form — and Text with no font — draws the single
                // centred particle exactly as before (Slice-1 path; now also warps).
                if (spec.shapeForm == ShapeForm.Text && _textReady)
                    RenderTextLine(buf, W, H, life, spec, mods, frameIndex);
                else
                    DrawParticle(buf, W, H, W * 0.5f, H * 0.5f, life, spec, 0, mods, phase, frameIndex);
            }
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

            // Text overrides the swarm count: one particle per CHARACTER (min 1), so the swarm places exactly the
            // string's letters. An exact guarded branch — every other form keeps `Mathf.Max(2, swarmCount)` VERBATIM
            // so its spawn arithmetic is byte-identical (orchestrator hash-gated). Text may make n == 1 (one char),
            // which the (n-1) divisions below now special-case.
            int n = spec.shapeForm == ShapeForm.Text
                ? Mathf.Max(1, (spec.textString ?? "").Length)
                : Mathf.Max(2, spec.swarmCount);
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
                if (n <= 1)
                    spawnLife = 0f;   // single-char Text: one particle at timeline 0 (avoids the (n-1)==0 divisions)
                else if (spec.swarmSpawnTiming == null || IsLinear01(spec.swarmSpawnTiming))
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
            // Text form: this particle renders ITS OWN character (index = particleIndex) as an extruded SDF glyph
            // at (cx, cy). This is the SWARM path (each swarm particle draws one letter at its swarm position); the
            // swarm-OFF line is laid out separately by RenderTextLine. When no readable font exists (_textReady is
            // false) we fall THROUGH to the Disc raster below as the per-particle fallback — so travel/size/alpha
            // are left for the Disc code to evaluate and cx/cy stay untouched here in that case.
            if (spec.shapeForm == ShapeForm.Text)
            {
                if (_textReady)
                {
                    string ts = spec.textString ?? "";
                    if (ts.Length == 0) return;
                    char ch = ts[Mathf.Clamp(particleIndex, 0, ts.Length - 1)];
                    float szc = Mathf.Max(0f, Eval(spec.size, life, spec.seed, particleIndex, FldSize)) * sizeMul;
                    // Shared per-particle travel (T7) — same guard/keys as every other form (no-op at Static 0).
                    if (!(IsStaticZero(spec.particlePathX) && IsStaticZero(spec.particlePathY)))
                    {
                        cx += Eval(spec.particlePathX, life, spec.seed, particleIndex, FldPathX);
                        cy += Eval(spec.particlePathY, life, spec.seed, particleIndex, FldPathY);
                    }
                    // Swarm mode has no line layout, so TextGradient degrades to PerCharStep (index fraction):
                    // pass lineMode=false, x0/span 0 (unused).
                    DrawTextChar(buf, W, H, cx, cy, szc, life, spec, particleIndex, ch, particleIndex,
                                 Mathf.Max(1, ts.Length), 0f, 0f, false, mods, frameIndex, brightMul);
                    return;
                }
                // else: no readable font → fall through to the Disc raster (the documented per-particle fallback).
            }

            // The true-3D facet SOLIDS (Gem + Box/Pyramid/Can) branch FIRST, before any Disc arithmetic below, so
            // the Disc path stays textually untouched and its output byte-identical (the orchestrator hash-gates
            // Disc). All four share DrawFacetSolid — same rotation, lighting, edge lines and glows — differing only
            // in their model verts / faces / which edges are lines. `life` here IS the particle's own life clock in
            // both callers (blast life for the single particle, `own` for a swarm one).
            if (spec.shapeForm == ShapeForm.Gem || spec.shapeForm == ShapeForm.Box ||
                spec.shapeForm == ShapeForm.Pyramid || spec.shapeForm == ShapeForm.Can)
            {
                DrawFacetSolid(buf, W, H, cx, cy, life, spec, particleIndex, mods, frameIndex, sizeMul, brightMul);
                return;
            }
            // Orb + Ring — the ANALYTIC true-3D forms (a sphere and a flat annulus). They reuse the SAME per-pixel
            // point-light + Blinn-Phong lighting, hard edge lines and halo/inner glows as DrawFacetSolid, but drive
            // them from analytic geometry instead of facets (see DrawOrb / DrawRing). Branch before the Disc
            // arithmetic so the Disc path stays byte-identical, exactly like the facet-solid branch above.
            if (spec.shapeForm == ShapeForm.Orb)
            {
                DrawOrb(buf, W, H, cx, cy, life, spec, particleIndex, mods, frameIndex, sizeMul, brightMul);
                return;
            }
            if (spec.shapeForm == ShapeForm.Ring)
            {
                DrawRing(buf, W, H, cx, cy, life, spec, particleIndex, mods, frameIndex, sizeMul, brightMul);
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

            // ── other STATELESS forms (G2) — branch BEFORE the Disc raster so the Disc/Gem paths below stay
            //    textually untouched (both are hash-gated). Each shares the contract Disc uses: radius from
            //    `size`·sizeMul, alpha envelope × gradient at own life, brightMul on RGB, the travel offset
            //    already folded into cx/cy above, and spin + geometry warps folded per pixel exactly as Disc's
            //    modifier path does. Sprite alone can decline (null / non-readable texture) and fall THROUGH to
            //    the Disc raster as its fallback. ──
            if (spec.shapeForm == ShapeForm.Crescent)
            {
                DrawCrescentBody(buf, W, H, cx, cy, radius, alpha, col, cr, cg, cb, soft, inner,
                                 spec, particleIndex, mods, phase, frameIndex, life);
                return;
            }
            if (spec.shapeForm == ShapeForm.Sparkle)
            {
                DrawSparkleBody(buf, W, H, cx, cy, radius, alpha, col, cr, cg, cb,
                                spec, particleIndex, mods, phase, frameIndex, life);
                return;
            }
            if (spec.shapeForm == ShapeForm.Sprite)
            {
                // A null spriteImage, or a texture without Read/Write, can't be sampled — DrawSpriteBody returns
                // false and we FALL THROUGH to the Disc raster below as the fallback (the UI's picker tooltip
                // warns the texture must have Read/Write enabled).
                if (DrawSpriteBody(buf, W, H, cx, cy, radius, alpha, col, brightMul,
                                   spec, particleIndex, mods, phase, frameIndex, life))
                    return;
            }

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

        // ── other stateless forms (G2): Crescent / Sparkle / Sprite ────────────────────
        // Resolve one candidate pixel's sample position, folding the particle's 2D spin (rotate the local
        // offset by −spin about the particle centre) and then the geometry modifiers, in the SAME order and
        // with the SAME arithmetic as DrawParticle's Disc modifier path — so a warped/spun/textured shape turns
        // as a unit. `dx/dy` = offset from the particle centre (the shape-local test coord); `wx/wy` = the
        // geometry-warped ABSOLUTE canvas position (for PixelInfo). When neither spin nor geo is active this is
        // exactly (sx−cx, sy−cy) and (sx, sy), so a plain shape is undisturbed.
        static void ResolveSample(float sx, float sy, float cx, float cy, float ccx, float ccy, Vector2 c,
                                  bool doSpin, float spinCos, float spinSin, bool anyGeo,
                                  GeometryModifier[] geo, float phase, in GeoCtx ctx,
                                  out float dx, out float dy, out float wx, out float wy)
        {
            wx = sx; wy = sy;
            if (anyGeo)
            {
                Vector2 geoIn;
                if (doSpin)
                {
                    float lox = sx - cx, loy = sy - cy;   // local offset from the particle centre
                    geoIn = new Vector2(c.x + (lox * spinCos - loy * spinSin),
                                        c.y + (lox * spinSin + loy * spinCos));
                }
                else geoIn = new Vector2(sx - ccx, sy - ccy);
                Vector2 off = ApplyGeo(geo, geoIn, phase, ctx);
                wx = ccx + off.x; wy = ccy + off.y;
                dx = off.x - c.x; dy = off.y - c.y;
            }
            else if (doSpin)
            {
                float lox = sx - cx, loy = sy - cy;
                dx = lox * spinCos - loy * spinSin;
                dy = lox * spinSin + loy * spinCos;
            }
            else { dx = sx - cx; dy = sy - cy; }
        }

        // Crescent: a disc with a second offset disc masked out. A pixel is lit when it's inside the main disc
        // (d ≤ radius) AND outside the bite disc (mdist > biteRadius). The bite disc's centre sits crescentOffset·
        // radius out in the crescentAngle direction; its radius is crescentBite·radius (both on the particle's own
        // life). edgeSoftness feathers BOTH rims: the outer edge exactly as Disc does (via `inner`), the bite edge
        // over a soft·radius band just OUTSIDE it — mirroring real Pyre's `(mnd−1)/innerSoft` bite feather
        // (normalised to the main radius), generalised to a bite disc of its own size.
        static void DrawCrescentBody(Color32[] buf, int W, int H, float cx, float cy, float radius, float alpha,
                                     Color col, float cr, float cg, float cb, float soft, float inner,
                                     PyrePlusSpec spec, int particleIndex, in ModSet mods, float phase,
                                     int frameIndex, float life)
        {
            float bite = Mathf.Clamp01(Eval(spec.crescentBite, life, spec.seed, particleIndex, FldCrescentBite));
            float biteRadius = radius * bite;
            float ang = Eval(spec.crescentAngle, life, spec.seed, particleIndex, FldCrescentAngle) * Mathf.Deg2Rad;
            float off = Mathf.Clamp01(spec.crescentOffset) * radius;
            float bx = off * Mathf.Cos(ang), by = off * Mathf.Sin(ang);   // bite-disc centre, in the shape-local (dx,dy) frame

            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            Vector2 c = new Vector2(cx - ccx, cy - ccy);
            var ctx = new GeoCtx(ccx, ccy, c, radius);
            float spin = Eval(spec.particleSpin, life, spec.seed, particleIndex, FldSpin);
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7);

            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
            }

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    ResolveSample(sx, sy, cx, cy, ccx, ccy, c, doSpin, spinCos, spinSin, anyGeo, mods.geo, phase, ctx,
                                  out float dx, out float dy, out float wx, out float wy);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    float mdx = dx - bx, mdy = dy - by;
                    float mdist = Mathf.Sqrt(mdx * mdx + mdy * mdy);
                    float biteEdge;
                    if (biteRadius < 0.5f) biteEdge = 1f;                              // no meaningful bite → plain disc
                    else if (soft <= 0.001f) biteEdge = mdist >= biteRadius ? 1f : 0f; // hard bite edge
                    else biteEdge = Mathf.Clamp01((mdist - biteRadius) / (soft * radius));
                    if (biteEdge <= 0.001f) continue;
                    float outerEdge = d <= inner ? 1f : 1f - Mathf.InverseLerp(inner, radius, d);
                    float baseA = alpha * col.a * outerEdge * biteEdge;
                    if (baseA <= 0.002f) continue;
                    if (mods.AnyPix)
                    {
                        Color pc = new Color(cr, cg, cb, 1f);
                        float pa = baseA;
                        float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, radius));
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, cr, cg, cb, baseA);
                }
        }

        // Sparkle: overlay a virtual grid of `sparkleSize`-px cells on the disc. A cell is LIT this frame iff its
        // stable presence draw < density AND its per-frame twinkle draw passes — both from the Hash funnel, no
        // state, so any frame renders standalone. Presence keys (seed, particleIndex, FldSparkle, cellIndex) so
        // the same cells are the candidates every frame (stable identity); twinkle keys the frame bucket in too
        // (cellIndex ^ frameSalt) so each candidate flickers on/off — the deterministic mirror of Pyre's
        // stable-but-twinkling sparkles. frameSalt is the particle's OWN-life frame bucket, so a swarm particle's
        // twinkle tracks its own clock. Lit cells are hard full-colour/alpha pixels — no edge falloff; pixels
        // past the disc radius are never lit.
        static void DrawSparkleBody(Color32[] buf, int W, int H, float cx, float cy, float radius, float alpha,
                                    Color col, float cr, float cg, float cb,
                                    PyrePlusSpec spec, int particleIndex, in ModSet mods, float phase,
                                    int frameIndex, float life)
        {
            float density = Mathf.Clamp01(Eval(spec.sparkleDensity, life, spec.seed, particleIndex, FldSparkle));
            if (density <= 0.001f) return;
            float baseAlpha = alpha * col.a;
            if (baseAlpha <= 0.002f) return;
            int cell = Mathf.Clamp(spec.sparkleSize, 1, 4);
            int frames = Mathf.Max(1, spec.frameCount);
            // Own-life frame bucket → the twinkle clock. The +1 keeps it nonzero so a cell's twinkle key
            // (cellIndex ^ frameSalt) can never coincide with its presence key (cellIndex, frameSalt 0 would).
            int frameSalt = 1 + Mathf.FloorToInt(Mathf.Clamp01(life) * frames);

            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            Vector2 c = new Vector2(cx - ccx, cy - ccy);
            var ctx = new GeoCtx(ccx, ccy, c, radius);
            float spin = Eval(spec.particleSpin, life, spec.seed, particleIndex, FldSpin);
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7);

            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
            }

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    ResolveSample(sx, sy, cx, cy, ccx, ccy, c, doSpin, spinCos, spinSin, anyGeo, mods.geo, phase, ctx,
                                  out float dx, out float dy, out float wx, out float wy);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    int gx = Mathf.FloorToInt(dx / cell), gy = Mathf.FloorToInt(dy / cell);
                    // Spatial-hash the cell to one int; XOR the golden-ratio constant so the origin cell (0,0)
                    // maps to a nonzero id — otherwise its presence draw would key (…, FldSparkle, 0), colliding
                    // with a MinMax sparkleDensity's own d=0 draw in this same field stream.
                    int cellIndex = ((gx * 73856093) ^ (gy * 19349663)) ^ unchecked((int)0x9E3779B9);
                    float presence = Hash01(spec.seed, particleIndex, FldSparkle, cellIndex);
                    if (presence >= density) continue;                                  // not a candidate cell
                    float twinkle = Hash01(spec.seed, particleIndex, FldSparkle, cellIndex ^ frameSalt);
                    if (twinkle >= 0.5f) continue;                                       // candidate, but off this frame
                    if (mods.AnyPix)
                    {
                        Color pc = new Color(cr, cg, cb, 1f);
                        float pa = baseAlpha;
                        float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, radius));
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, cr, cg, cb, baseAlpha);
                }
        }

        // Sprite: stamp spec.spriteImage's pixels, scaled so its larger dimension maps to 2·radius, centred on
        // the particle, rotated by the shared spin and folded through the geometry warps (via ResolveSample —
        // spin-then-warp, the Disc order), point-sampled (nearest) from the sprite's rect within its texture.
        // Tinted by the gradient when spriteTint, then alpha envelope × brightMul, PixelModifiers, Over. Returns
        // false (→ Disc fallback) when the sprite/texture is null or not Read/Write-enabled (GetPixels32 throws,
        // caught once in TryGetTexturePixels). True once it has taken responsibility for the stamp.
        static bool DrawSpriteBody(Color32[] buf, int W, int H, float cx, float cy, float radius, float alpha,
                                   Color col, float brightMul, PyrePlusSpec spec, int particleIndex,
                                   in ModSet mods, float phase, int frameIndex, float life)
        {
            var sprite = spec.spriteImage;
            if (sprite == null || sprite.texture == null) return false;
            if (!TryGetTexturePixels(sprite.texture, out var px)) return false;   // non-readable texture → fallback

            var tex = sprite.texture;
            int texW = tex.width;
            Rect tr = sprite.textureRect;
            int rx = Mathf.FloorToInt(tr.x), ry = Mathf.FloorToInt(tr.y);
            int rw = Mathf.Max(1, Mathf.FloorToInt(tr.width)), rh = Mathf.Max(1, Mathf.FloorToInt(tr.height));
            float scale = (2f * radius) / Mathf.Max(rw, rh);
            if (scale <= 1e-4f) return true;   // radius collapsed — nothing to stamp, but the sprite HAS handled it

            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            Vector2 c = new Vector2(cx - ccx, cy - ccy);
            var ctx = new GeoCtx(ccx, ccy, c, radius);
            float spin = Eval(spec.particleSpin, life, spec.seed, particleIndex, FldSpin);
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7);

            float box = radius * 1.5f;   // generous bbox to cover the rotated stamp (matches Pyre's RasterSprite)
            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - box));
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + box));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - box));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + box));
            }

            bool tint = spec.spriteTint;
            float tr2 = tint ? col.r : 1f, tg2 = tint ? col.g : 1f, tb2 = tint ? col.b : 1f, ta2 = tint ? col.a : 1f;
            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    ResolveSample(sx, sy, cx, cy, ccx, ccy, c, doSpin, spinCos, spinSin, anyGeo, mods.geo, phase, ctx,
                                  out float dx, out float dy, out float wx, out float wy);
                    int su = Mathf.FloorToInt(dx / scale + rw * 0.5f);
                    int sv = Mathf.FloorToInt(dy / scale + rh * 0.5f);
                    if (su < 0 || su >= rw || sv < 0 || sv >= rh) continue;
                    Color32 sc = px[(ry + sv) * texW + (rx + su)];   // GetPixels32 is row-major, bottom-left origin
                    if (sc.a == 0) continue;
                    float outR = (sc.r / 255f) * tr2, outG = (sc.g / 255f) * tg2, outB = (sc.b / 255f) * tb2;
                    if (brightMul != 1f)
                    {
                        outR = Mathf.Clamp01(outR * brightMul);
                        outG = Mathf.Clamp01(outG * brightMul);
                        outB = Mathf.Clamp01(outB * brightMul);
                    }
                    float outA = (sc.a / 255f) * alpha * ta2;
                    if (outA <= 0.002f) continue;
                    if (mods.AnyPix)
                    {
                        Color pc = new Color(outR, outG, outB, 1f);
                        float pa = outA;
                        float crossFrac = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(0.001f, radius));
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, outR, outG, outB, outA);
                }
            return true;
        }

        // Full-texture pixel cache for the Sprite form, keyed by Texture2D. GetPixels32 needs the texture
        // Read/Write-enabled; a non-readable texture throws → caught, the entry is cached as null so we don't
        // retry every frame, and callers treat null as "not sampleable" (→ Disc fallback). Purity holds: the
        // texture's CONTENT is itself an input, so memoising the read preserves same-inputs-same-output; a
        // texture edited/reimported at author time would read stale until a domain reload clears the cache
        // (mirrors real Pyre's own sprite cache, which exposes an explicit clear for exactly that).
        static readonly System.Collections.Generic.Dictionary<Texture2D, Color32[]> _spritePixelCache =
            new System.Collections.Generic.Dictionary<Texture2D, Color32[]>();

        static bool TryGetTexturePixels(Texture2D tex, out Color32[] px)
        {
            px = null;
            if (tex == null) return false;
            if (_spritePixelCache.TryGetValue(tex, out px)) return px != null;
            Color32[] got;
            try { got = tex.GetPixels32(); }
            catch { got = null; }
            _spritePixelCache[tex] = got;
            px = got;
            return got != null;
        }

        // A deterministic [0,1) draw straight from the FNV Hash — same funnel as everything else, no per-pixel
        // System.Random allocation (used for the Sparkle presence/twinkle draws, evaluated per candidate pixel).
        static float Hash01(int a, int b, int c, int d)
        {
            uint h = (uint)Hash(a, b, c, d);
            return (h & 0x00FFFFFFu) / 16777216f;   // top of the 24-bit mantissa → [0,1)
        }

        // ── Text form (Gem-family extruded SDF letters) ──────────────────────────────
        // Per-font glyph-atlas snapshot: the raw SDF bytes + dims + padding, captured AFTER TryAddCharacters bakes
        // every needed glyph. A Dynamic SDF font REGENERATES its atlas when characters are added, so a snapshot
        // taken BEFORE the add would be stale — the real bug the prototype documents; hence snapshot-after. The
        // glyph bitmap content is a render INPUT (the same purity argument as the Sprite pixel cache), so memoising
        // it preserves same-inputs-same-output; it's re-baked/re-snapshotted when the font changes, the requested
        // string changes, or a lookup misses (e.g. after a domain reload nulls these statics). _textReady, set once
        // per frame in RenderFrame, is the gate every Text draw checks.
        static TMP_FontAsset _textFont;
        static byte[] _textSdf;
        static int _textAw, _textAh, _textPad;
        static string _textCachedChars = "";
        static bool _textReady;
#if UNITY_EDITOR
        static TMP_FontAsset _textAutoFont;   // cached auto-pick, so FindAssets isn't run every frame
#endif

        // Resolve the font (spec.textFont, else an auto-found readable one in the editor), bake every char of
        // spec.textString into its atlas, and snapshot the atlas into the static fields above. Returns false — so
        // the caller falls back to Disc per particle — when no font with a readable atlas exists.
        static bool EnsureTextGlyphs(PyrePlusSpec spec)
        {
            TMP_FontAsset font = spec.textFont;
#if UNITY_EDITOR
            if (font == null)
            {
                // Cache the auto-pick; re-find only if the cached one went invalid (a domain reload on an import
                // clears the static anyway, so a newly-added font is picked up then).
                if (_textAutoFont == null || _textAutoFont.atlasTexture == null || !_textAutoFont.atlasTexture.isReadable)
                    _textAutoFont = FindReadableTextFont();
                font = _textAutoFont;
            }
#endif
            if (font == null || font.atlasTexture == null || !font.atlasTexture.isReadable) return false;

            string need = spec.textString ?? "";
            bool needBake = font != _textFont || need != _textCachedChars || _textSdf == null;
            if (!needBake)
            {
                // A char present in the cached string set but somehow missing from the live lookup (font rebuilt,
                // atlas cleared) forces a fresh bake + snapshot.
                for (int i = 0; i < need.Length; i++)
                    if (!font.characterLookupTable.ContainsKey(need[i])) { needBake = true; break; }
            }
            if (needBake)
            {
                if (!string.IsNullOrEmpty(need)) font.TryAddCharacters(need);   // bake FIRST (regenerates the atlas)
                var atlas = font.atlasTexture;                                   // ...THEN snapshot the fresh atlas
                if (atlas == null || !atlas.isReadable) return false;
                _textAw = atlas.width; _textAh = atlas.height; _textPad = font.atlasPadding;
                _textSdf = atlas.GetRawTextureData<byte>().ToArray();
                _textFont = font;
                _textCachedChars = need;
            }
            return true;
        }

#if UNITY_EDITOR
        // The auto-pick: the first TMP_FontAsset in the project whose atlas is readable. Editor-only (AssetDatabase);
        // at runtime a null spec.textFont means the Text form falls back to Disc.
        static TMP_FontAsset FindReadableTextFont()
        {
            foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var f = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                if (f != null && f.atlasTexture != null && f.atlasTexture.isReadable) return f;
            }
            return null;
        }
#endif

        // Swarm-OFF line: lay the whole string out as one centred line and draw the letters far→near so nearer ones
        // correctly overdraw farther ones under a yaw (fixes the prototype's crude painter-skip occlusion). Each
        // char is particle index i on its OWN size/alpha/spin/tilt draws, but all share the blast life (there's no
        // swarm timing here). Called only when _textReady.
        static void RenderTextLine(Color32[] buf, int W, int H, float life, PyrePlusSpec spec,
                                   in ModSet mods, int frameIndex)
        {
            string ts = spec.textString ?? "";
            int n = ts.Length;
            if (n == 0) return;
            float spacing = Mathf.Clamp(spec.textSpacing, 0.6f, 1.6f);

            // Per-char layout: scale each glyph so its HEIGHT == its evaluated Size; advance by that glyph's own
            // scaled width plus a fixed tracking gap, all × Spacing (so a MinMax Size gives ragged letters that
            // still lay out cleanly). x0[i] = the char centre in TEXT space (0-centred); span = half the line
            // extent, used by the TextGradient fill. A missing glyph (space, unbaked) gets a blank monospace slot.
            var szA = new float[n];
            var glyphW = new float[n];
            var adv = new float[n];
            for (int i = 0; i < n; i++)
            {
                szA[i] = Mathf.Max(0f, Eval(spec.size, life, spec.seed, i, FldSize));
                float track = szA[i] * 0.28f;
                if (_textFont.characterLookupTable.TryGetValue(ts[i], out var tc) && tc.glyph.glyphRect.height > 0)
                {
                    var gr = tc.glyph.glyphRect;
                    glyphW[i] = gr.width * (szA[i] / gr.height);
                }
                else glyphW[i] = szA[i] * 0.4f;   // blank slot
                adv[i] = (glyphW[i] + track) * spacing;
            }
            float total = 0f;
            for (int i = 0; i < n; i++) total += adv[i];
            total -= szA[n - 1] * 0.28f * spacing;   // drop the trailing gap so the visual line centres
            var x0 = new float[n];
            float pen = -total * 0.5f;
            for (int i = 0; i < n; i++) { x0[i] = pen + glyphW[i] * 0.5f; pen += adv[i]; }
            float span = Mathf.Max(1f, total * 0.5f);

            float cx = W * 0.5f, cy = H * 0.5f;

            // Draw order: the letters share the same yaw sign (particleSpin at the blast life), so under a yaw one
            // side of every letter comes toward the viewer and the other recedes. AX.z at the representative yaw
            // tells us which: AX.z < 0 (yaw > 0) ⇒ each letter's +x edge recedes, so the near edge of the pair sits
            // on the RIGHT letter's left — paint left→right; AX.z > 0 ⇒ paint right→left. A per-char MinMax yaw
            // isn't perfectly ordered by one direction, but occlusion between wildly different rotations is
            // ambiguous anyway — this is a display nicety. No yaw ⇒ letters don't overlap in depth, order is moot.
            float yaw0 = Eval(spec.particleSpin, life, spec.seed, 0, FldSpin) * Mathf.Deg2Rad;
            float axz = -Mathf.Sin(yaw0) * Mathf.Cos(Eval(spec.gemTilt, life, spec.seed, 0, FldGemTilt) * Mathf.Deg2Rad);
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            float dir = axz > 0f ? -1f : 1f;
            System.Array.Sort(order, (a, b) => (x0[a] * dir).CompareTo(x0[b] * dir));

            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                DrawTextChar(buf, W, H, cx + x0[i], cy, szA[i], life, spec, i, ts[i], i, n,
                             x0[i], span, true, mods, frameIndex, 1f);
            }
        }

        // One extruded SDF character, centred at (cx, cy), rotated by its OWN yaw (particleSpin) + tilt (gemTilt)
        // about that centre. Per-pixel: walk depth layers front→back (layer 0 = the lit front face, deeper = darker
        // extrusion side), first SDF hit wins; bilinear SDF sample, threshold 0.5; a border band just inside the
        // edge; the fill / border colour comes from SampleTextColor. Geometry warps are intentionally skipped (the
        // 3D-family convention — same as the facet solids); PixelModifiers still run per lit pixel and Post passes
        // still hit the finished buffer. `sz` is the char HEIGHT in px, supplied by the caller (line layout or the
        // swarm particle) so it and the layout agree. lineMode/x0/span feed TextGradient; charIndex/n feed the
        // per-char step. brightMul is the swarm depth shade (1 in line mode).
        static void DrawTextChar(Color32[] buf, int W, int H, float cx, float cy, float sz, float own,
                                 PyrePlusSpec spec, int particleIndex, char ch, int charIndex, int n,
                                 float x0, float span, bool lineMode, in ModSet mods, int frameIndex, float brightMul)
        {
            if (sz < 1.5f) return;
            if (!_textFont.characterLookupTable.TryGetValue(ch, out var tchar)) return;   // unbaked glyph (e.g. space)
            var gr = tchar.glyph.glyphRect;
            float gw = gr.width, gh = gr.height;
            if (gw < 1f || gh < 1f) return;

            float alphaEnv = Mathf.Clamp01(Eval(spec.alpha, own, spec.seed, particleIndex, FldAlpha));
            if (alphaEnv <= 0.002f) return;

            // Scale so the glyph HEIGHT == sz; halfW/halfH are the glyph box half-extents in screen px.
            float scGlyph = sz / gh;
            float halfW = gw * scGlyph * 0.5f, halfH = gh * scGlyph * 0.5f;

            // 3D extrusion: front face at +depth/2, back at -depth/2, walked in DepthSteps layers. !textSolid ⇒ a
            // single flat plane (depthSteps 0). Depth = a fraction of the char size, exactly like the prototype.
            bool solid = spec.textSolid;
            const int DepthSteps = 8;
            float depth = solid ? sz * Mathf.Clamp(spec.textDepth, 0.05f, 1f) : 0f;
            int steps = solid ? DepthSteps : 0;

            // Per-char rotation about its own centre: yaw about Y (particleSpin) then tilt about X (gemTilt), +z
            // toward the viewer — the facet solids' / prototype's Rot exactly. The char centre is the pivot, so
            // model space is centred at the origin and translated to (cx, cy) in screen space.
            float yaw = Eval(spec.particleSpin, own, spec.seed, particleIndex, FldSpin) * Mathf.Deg2Rad;
            float tilt = Eval(spec.gemTilt, own, spec.seed, particleIndex, FldGemTilt) * Mathf.Deg2Rad;
            float cyw = Mathf.Cos(yaw), syw = Mathf.Sin(yaw);
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            Vector3 Rot(Vector3 p)
            {
                var q = new Vector3(p.x * cyw + p.z * syw, p.y, -p.x * syw + p.z * cyw);
                return new Vector3(q.x, q.y * ct - q.z * st, q.y * st + q.z * ct);
            }
            Vector3 AX = Rot(new Vector3(1, 0, 0)), AY = Rot(new Vector3(0, 1, 0)), AZ = Rot(new Vector3(0, 0, 1));
            float axx = AX.x, axy = AX.y, ayx = AY.x, ayy = AY.y;
            float det = axx * ayy - axy * ayx;
            if (Mathf.Abs(det) < 1e-4f) return;   // face turned edge-on to the screen — nothing to raster
            float inv = 1f / det;

            // Raster bounds: project all 8 corners of the front+back box (centred coords), padded 2px.
            float minX = float.MaxValue, maxX = -float.MaxValue, minY = float.MaxValue, maxY = -float.MaxValue;
            for (int s = 0; s < 8; s++)
            {
                float uu = (s & 1) == 0 ? -halfW : halfW;
                float vv = (s & 2) == 0 ? -halfH : halfH;
                float zz = (s & 4) == 0 ? -depth * 0.5f : depth * 0.5f;
                float pxc = AX.x * uu + AY.x * vv + AZ.x * zz;
                float pyc = AX.y * uu + AY.y * vv + AZ.y * zz;
                if (pxc < minX) minX = pxc; if (pxc > maxX) maxX = pxc;
                if (pyc < minY) minY = pyc; if (pyc > maxY) maxY = pyc;
            }
            int px0 = Mathf.Max(0, Mathf.FloorToInt(cx + minX) - 2);
            int px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + maxX) + 2);
            int py0 = Mathf.Max(0, Mathf.FloorToInt(cy + minY) - 2);
            int py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + maxY) + 2);

            // Border band, converting the screen-px width to SDF VALUE units. TMP's normalized SDF rises ~0.5 (from
            // the 0.5 contour to fully-inside 1.0) over ~atlasPadding atlas px, i.e. slope ≈ 1/(2·pad) per atlas px.
            // A w-screen-px band is w/scGlyph atlas px deep (scGlyph = screen px per atlas px), so
            //   borderBand = (w / scGlyph) · (1/(2·pad)) = w / (2·pad·scGlyph).
            // This reproduces the prototype's hard-coded 0.10 at w≈1.5, pad≈9, scGlyph≈0.83 (0.10 ✓) — the factor 2
            // (absent from the design's shorthand "w/(pad·scale)") is that half-range-over-padding slope.
            float borderBand = spec.textBorderWidth > 0f
                ? spec.textBorderWidth / Mathf.Max(1e-3f, 2f * _textPad * scGlyph)
                : 0f;

            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7);
            float boxExtent = Mathf.Max(halfW, halfH);

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sxp = x + 0.5f - cx, syp = y + 0.5f - cy;
                    for (int k = 0; k <= steps; k++)
                    {
                        float zoff = solid ? depth * 0.5f - (depth * k) / DepthSteps : 0f;
                        // Solve C + AX·u + AY·v + AZ·zoff = (sxp, syp, *) in screen x/y (C == 0, centre at origin).
                        float rx = sxp - AZ.x * zoff;
                        float ry = syp - AZ.y * zoff;
                        float u = (rx * ayy - ry * ayx) * inv;
                        float v = (-rx * axy + ry * axx) * inv;
                        if (u < -halfW || u > halfW || v < -halfH || v > halfH) continue;

                        float sd = SampleTextSdf(gr, (u + halfW) / scGlyph, (v + halfH) / scGlyph);
                        if (sd < 0.5f) continue;

                        bool face = k == 0;
                        bool border = borderBand > 0f && sd < 0.5f + borderBand;
                        Gradient grad = border ? spec.textBorderGradient : spec.textFillGradient;
                        Color col = SampleTextColor(spec, grad, charIndex, n, u, v, halfW, halfH, x0, span, lineMode);
                        if (!face)
                        {
                            // Extrusion side: fill (or border) × 0.45, then k-shaded darker with depth (prototype
                            // constants) so the sides read as receding.
                            col *= 0.45f * Mathf.Lerp(1f, 0.55f, k / (float)DepthSteps);
                        }

                        float rr = Mathf.Clamp01(col.r), gg = Mathf.Clamp01(col.g), bb = Mathf.Clamp01(col.b);
                        if (brightMul != 1f)
                        {
                            rr = Mathf.Clamp01(col.r * brightMul);
                            gg = Mathf.Clamp01(col.g * brightMul);
                            bb = Mathf.Clamp01(col.b * brightMul);
                        }
                        float pa = alphaEnv;
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v) / Mathf.Max(0.001f, boxExtent));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H))
                                break;   // dropped by a modifier — the front face occludes any deeper layer anyway
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                        break;   // first (front-most) depth hit wins: face vs side
                    }
                }
        }

        // The Text fill/border colour at a surface point. PerCharGradient: sampled across the char's own box along
        // the angle-rotated axis (0 = vertical bottom→top). PerCharStep: one flat colour grad(i/(n-1)) by index.
        // TextGradient: across the whole line's extent along the angle-rotated axis (0 = left→right); in SWARM mode
        // there's no line, so it degrades to the per-char step (index fraction). Same call drives the border, just
        // with the border gradient. (u,v) is the surface point in the char's local px frame; x0 = the char centre
        // in text space; span = half the line extent.
        static Color SampleTextColor(PyrePlusSpec spec, Gradient grad, int charIndex, int n,
                                     float u, float v, float halfW, float halfH, float x0, float span, bool lineMode)
        {
            if (grad == null) return Color.white;
            float t;
            switch (spec.textFillMode)
            {
                case TextFillMode.PerCharStep:
                    t = n > 1 ? charIndex / (float)(n - 1) : 0f;
                    break;
                case TextFillMode.TextGradient:
                    if (lineMode)
                    {
                        float ang = spec.textGradientAngle * Mathf.Deg2Rad;
                        float tx = x0 + u, ty = v;
                        float along = tx * Mathf.Cos(ang) + ty * Mathf.Sin(ang);   // 0° = left→right across the line
                        t = span > 1e-3f ? Mathf.InverseLerp(-span, span, along) : 0.5f;
                    }
                    else t = n > 1 ? charIndex / (float)(n - 1) : 0f;   // swarm: no line → per-char step
                    break;
                default:   // PerCharGradient
                {
                    float ang = spec.textGradientAngle * Mathf.Deg2Rad;
                    float sa = Mathf.Sin(ang), ca = Mathf.Cos(ang);
                    float along = u * sa + v * ca;                       // 0° = v (vertical bottom→top)
                    float ext = Mathf.Abs(halfW * sa) + Mathf.Abs(halfH * ca);   // box half-extent along the axis
                    t = ext > 1e-3f ? Mathf.InverseLerp(-ext, ext, along) : 0.5f;
                    break;
                }
            }
            return grad.Evaluate(Mathf.Clamp01(t));
        }

        // Bilinear SDF sample from the snapshot atlas; (gx,gy) in glyph-local pixels (0..gr.width, 0..gr.height),
        // padding honoured so the border falloff just outside the tight rect is reachable. Ported verbatim from the
        // approved Text prototype (SampleSdf/At), reading the static snapshot instead of instance fields.
        static float SampleTextSdf(UnityEngine.TextCore.GlyphRect gr, float gx, float gy)
        {
            float ax = gr.x + gx, ay = gr.y + gy;
            ax = Mathf.Clamp(ax, gr.x - _textPad + 1, gr.x + gr.width + _textPad - 2);
            ay = Mathf.Clamp(ay, gr.y - _textPad + 1, gr.y + gr.height + _textPad - 2);
            int ix = Mathf.FloorToInt(ax), iy = Mathf.FloorToInt(ay);
            float fx = ax - ix, fy = ay - iy;
            float s00 = AtText(ix, iy), s10 = AtText(ix + 1, iy), s01 = AtText(ix, iy + 1), s11 = AtText(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(s00, s10, fx), Mathf.Lerp(s01, s11, fx), fy);
        }

        static float AtText(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _textAw || y >= _textAh) return 0f;
            return _textSdf[y * _textAw + x] / 255f;
        }

        // ── 3D facet solids (Gem / Box / Pyramid / Can) ──────────────────────────────
        // ONE renderer for every true-3D convex facet solid, generalized out of the approved gem prototype
        // (Diamond3DProbe). It differs per FORM only in (model verts, faces, which edges draw as lines) — supplied
        // by the Build*Geometry helpers below; EVERYTHING else (rotation, backface cull, per-pixel point light +
        // Blinn-Phong, hard edge lines, halo + inner glow, ApplyPix, Over) is shared and unchanged. Convex ⇒
        // backface culling ONLY (no depth sort). Determinism unchanged: pure static math through Eval/Hash. Every
        // pixel composites into the swarm buffer through Over.
        //
        // GEM BYTE-IDENTITY: BuildGemGeometry reproduces the prototype's model/faces verbatim and passes
        // lineEdges == null (every edge is a line — the gem's original behaviour), so a Gem renders exactly as the
        // old DrawGem did (the orchestrator hash-gates Gem). Box/Pyramid/Can supply their own verts/faces and an
        // explicit line-edge set that excludes quad diagonals / barrel seams / cap spokes.
        //
        // Signature extends the design's listed one with `in ModSet mods, int frameIndex` because the design's own
        // body mandates running the PixelModifiers per lit pixel (which need them). GEOMETRY modifiers are
        // deliberately NOT applied to the solids in this slice — a 3D-consistent warp fold is its own problem; post
        // modifiers still hit the finished buffer later in ApplyPost, unchanged.
        static void DrawFacetSolid(Color32[] buf, int W, int H, float cx, float cy, float own,
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

            // Model (y up, +z toward the viewer after rotation), triangle faces, and the LINE-edge set — all three
            // supplied per form. lineEdges == null means "every edge is a line" (Gem). See the Build*Geometry
            // helpers at the bottom of the class.
            Vector3[] model;
            int[][] faces;
            HashSet<int> lineEdges;
            switch (spec.shapeForm)
            {
                case ShapeForm.Box:     BuildBoxGeometry(spec, R, out model, out faces, out lineEdges); break;
                case ShapeForm.Pyramid: BuildPyramidGeometry(spec, R, out model, out faces, out lineEdges); break;
                case ShapeForm.Can:     BuildCanGeometry(spec, R, out model, out faces, out lineEdges); break;
                default:                BuildGemGeometry(spec, R, out model, out faces, out lineEdges); break;
            }

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
            var verts = new Vector3[model.Length];
            for (int k = 0; k < model.Length; k++) verts[k] = Rot(model[k]);

            // Visible faces: outward normal (flip vs centroid so the origin-inside-the-solid convention holds),
            // then backface cull n.z <= 0. Convex + culled ⇒ front faces tile the silhouette with no overlap.
            var visA = new Vector2[faces.Length];   // screen triangle (centered coords: vertex.xy)
            var visB = new Vector2[faces.Length];
            var visC = new Vector2[faces.Length];
            var visP0 = new Vector3[faces.Length];  // the three 3D verts, for the barycentric world point
            var visP1 = new Vector3[faces.Length];
            var visP2 = new Vector3[faces.Length];
            var visNrm = new Vector3[faces.Length];
            int visCount = 0;
            // Unique visible LINE edges (screen segments) for the hard line pass, deduped by vertex pair. Sized to
            // the loose upper bound of 3 edges per face; only the line edges are actually stored.
            var edgeA = new Vector2[3 * faces.Length];
            var edgeB = new Vector2[3 * faces.Length];
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
                    // lineEdges == null ⇒ every edge is a line (Gem's original behaviour, byte-identical). A non-
                    // null set names the LINE edges only, so quad diagonals / barrel seams / cap spokes are skipped
                    // — they neither draw a hard line nor seed the inner glow, which measures distance to this same
                    // edge set (so faces glow from their real borders, not phantom internal diagonals).
                    if (lineEdges != null && !lineEdges.Contains(key)) continue;
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
            // Colours are now authorable spec fields (Part A). Defaults equal the old renderer constants, so a
            // default solid is byte-identical. NOTE: the halo (edge glow) tint used to reuse lineColor; it is split
            // off into gemEdgeGlowColor here — byte-identical only while gemLineColor sits at its default (which the
            // hash-gated probes do), since gemEdgeGlowColor's default equals gemLineColor's default.
            Color specColor = spec.gemSpecularColor;     // Blinn-Phong highlight tint (was new Color(0.9f, 0.95f, 1f))
            Color innerColor = spec.gemInnerGlowColor;   // inner-glow tint (was new Color(0.35f, 0.60f, 1f))
            Color lineColor = spec.gemLineColor;         // hard edge-line colour
            Color edgeGlowColor = spec.gemEdgeGlowColor; // halo/edge-glow tint (was lineColor)
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
            for (int k = 0; k < verts.Length; k++)
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
                        fr += edgeGlowColor.r * haloAmt; fg += edgeGlowColor.g * haloAmt; fb += edgeGlowColor.b * haloAmt;
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
                        float rr = Mathf.Clamp01(edgeGlowColor.r * brightMul), gg = Mathf.Clamp01(edgeGlowColor.g * brightMul), bb = Mathf.Clamp01(edgeGlowColor.b * brightMul);
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

        // ── Orb: a true lit sphere, analytic (no facets) ─────────────────────────────────────────────────────
        // Silhouette = the plain circle d ≤ R, NEVER squashed by spin/tilt (a sphere looks identical from every
        // angle). The per-pixel visible surface point is P = (dx, dy, +sqrt(R²−dx²−dy²)) in screen space (+z toward
        // the viewer), normal N = P/R — so the smooth silhouette falloff comes out for free. The lighting formula,
        // edge line, halo and inner glow are IDENTICAL to DrawFacetSolid's face path (mirrored here rather than
        // shared, so the facet forms stay byte-identical): the only differences are (1) N is analytic instead of a
        // flat face normal, and (2) how spin/tilt are applied — see the lighting-frame rotation below.
        static void DrawOrb(Color32[] buf, int W, int H, float cx, float cy, float own,
                            PyrePlusSpec spec, int particleIndex, in ModSet mods, int frameIndex,
                            float sizeMul, float brightMul)
        {
            float R = Mathf.Max(0f, Eval(spec.size, own, spec.seed, particleIndex, FldSize)) * sizeMul;
            if (R < 1.5f) return;
            float alphaEnv = Mathf.Clamp01(Eval(spec.alpha, own, spec.seed, particleIndex, FldAlpha));
            if (alphaEnv <= 0.002f) return;

            // Shared per-particle travel (T7) — same guard as the facet/disc path (exact no-op at Static 0).
            if (!(IsStaticZero(spec.particlePathX) && IsStaticZero(spec.particlePathY)))
            {
                cx += Eval(spec.particlePathX, own, spec.seed, particleIndex, FldPathX);
                cy += Eval(spec.particlePathY, own, spec.seed, particleIndex, FldPathY);
            }

            // Light POSITION exactly as DrawFacetSolid builds it (screen space, distance 3.5·R, falloff 4.7·R).
            float lyaw = spec.gemLightYaw * Mathf.Deg2Rad, lpitch = spec.gemLightPitch * Mathf.Deg2Rad;
            float ldist = 3.5f * R, lhoriz = ldist * Mathf.Cos(lpitch);
            Vector3 lightPos = new Vector3(lhoriz * Mathf.Sin(lyaw), ldist * Mathf.Sin(lpitch), lhoriz * Mathf.Cos(lyaw));
            float lrange = 4.7f * R, lrange2 = lrange * lrange;
            Vector3 viewDir = new Vector3(0f, 0f, 1f);

            // ── Orb spin/tilt = a LIGHTING-FRAME rotation, NOT a geometry rotation. A uniform sphere's silhouette
            //    AND its true screen-space normals (N = P/R) are both rotation-invariant, so a plain lit sphere
            //    would show NOTHING as it spins. To roll the shading + specular hotspot around the ball (matching
            //    the Bakery Orb, which spins in the sphere-local shading plane), we rotate the LIGHT by the INVERSE
            //    of the sphere's rotation and keep shading with the true N = P/R (which preserves the correct rim
            //    falloff). This is EXACTLY equivalent to rotating N (and P) FORWARD by the rotation with a fixed
            //    light: a rotation preserves dot products and lengths, so N·(Rot⁻¹·L) == (Rot·N)·L and
            //    |Rot⁻¹·L − P| == |L − Rot·P| — same lit value, same specular, same attenuation. Rotating the light
            //    ONCE here is far cheaper than rotating N per pixel. Rot (the facet solids' own) is yaw-about-Y then
            //    tilt-about-X; its inverse is yaw(−) applied after tilt(−).
            float yaw = Eval(spec.particleSpin, own, spec.seed, particleIndex, FldSpin) * Mathf.Deg2Rad;
            float tilt = Eval(spec.gemTilt, own, spec.seed, particleIndex, FldGemTilt) * Mathf.Deg2Rad;
            float cyw = Mathf.Cos(yaw), syw = Mathf.Sin(yaw);
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            {
                // TiltX(−tilt): undo the tilt about X, then YawY(−yaw): undo the yaw about Y.
                float ax = lightPos.x;
                float ay = lightPos.y * ct + lightPos.z * st;
                float az = -lightPos.y * st + lightPos.z * ct;
                lightPos = new Vector3(ax * cyw - az * syw, ay, ax * syw + az * cyw);
            }

            Color specColor = spec.gemSpecularColor;
            Color innerColor = spec.gemInnerGlowColor;
            Color lineColor = spec.gemLineColor;
            Color edgeGlowColor = spec.gemEdgeGlowColor;
            float lineW = spec.gemLineWidth;
            float ambient = spec.gemAmbient, specStr = spec.gemSpecular;

            float edgeGlow = Mathf.Clamp01(Eval(spec.gemEdgeGlow, own, spec.seed, particleIndex, FldGemEdgeGlow));
            float innerGlow = Mathf.Clamp01(Eval(spec.gemInnerGlow, own, spec.seed, particleIndex, FldGemInnerGlow));
            float haloR = Mathf.Max(2.5f, 0.24f * R);
            float innerR = Mathf.Max(3f, 0.30f * R);
            Color baseCol = spec.colorOverLife != null ? spec.colorOverLife.Evaluate(own) : Color.white;

            // Raster bounds: the circle d ≤ R, expanded by haloR so the outside halo isn't clipped.
            float extent = R + haloR;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - extent));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + extent));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - extent));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + extent));
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7);

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float lx = x + 0.5f - cx, ly = y + 0.5f - cy;
                    float d = Mathf.Sqrt(lx * lx + ly * ly);
                    // The orb's ONLY edge is the silhouette rim; distance to it (both sides) drives lines + glows,
                    // exactly as DrawFacetSolid's `edist` (nearest face-edge distance) does.
                    float rimDist = Mathf.Abs(d - R);

                    bool hasFace = d <= R;
                    bool isLine = false;
                    float fr = 0f, fg = 0f, fb = 0f;
                    if (hasFace)
                    {
                        float z = Mathf.Sqrt(Mathf.Max(0f, R * R - d * d));   // toward-viewer bulge
                        Vector3 P = new Vector3(lx, ly, z);
                        Vector3 N = P / R;
                        Vector3 toL = lightPos - P;
                        float dist = toL.magnitude;
                        Vector3 L = toL / Mathf.Max(1e-4f, dist);
                        float atten = 1f / (1f + dist * dist / lrange2);
                        float ndl = Mathf.Max(0f, Vector3.Dot(N, L));
                        float lit = ambient + 2.1f * ndl * atten;
                        if (rimDist <= lineW)   // inside points at the rim draw the silhouette line, lit-scaled
                        {
                            isLine = true;
                            float k = Mathf.Clamp(0.25f + lit, 0f, 1.15f);
                            fr = lineColor.r * k; fg = lineColor.g * k; fb = lineColor.b * k;
                        }
                        else
                        {
                            Vector3 Hh = (L + viewDir).normalized;
                            float sp = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(N, Hh)), 48f);
                            float sAdd = specStr * sp * atten;
                            fr = baseCol.r * lit + specColor.r * sAdd;
                            fg = baseCol.g * lit + specColor.g * sAdd;
                            fb = baseCol.b * lit + specColor.b * sAdd;
                        }
                    }

                    float halo = Mathf.Pow(Mathf.Max(0f, 1f - rimDist / haloR), 2f);
                    float haloAmt = 0.85f * edgeGlow * halo;

                    if (hasFace)
                    {
                        fr += edgeGlowColor.r * haloAmt; fg += edgeGlowColor.g * haloAmt; fb += edgeGlowColor.b * haloAmt;
                        if (!isLine)
                        {
                            // Inner glow rising from the interior: measured inward from the rim (R − d == rimDist
                            // inside), so it's 0 at the silhouette and grows toward the centre — same shape as the
                            // facet's `edist/innerR` core.
                            float core = Mathf.Pow(Mathf.Clamp01(rimDist / innerR), 1.4f);
                            float innerAmt = 0.75f * innerGlow * core;
                            fr += innerColor.r * innerAmt; fg += innerColor.g * innerAmt; fb += innerColor.b * innerAmt;
                        }
                        float rr = Mathf.Clamp01(fr * brightMul), gg = Mathf.Clamp01(fg * brightMul), bb = Mathf.Clamp01(fb * brightMul);
                        float pa = alphaEnv;
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, R));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                    else
                    {
                        // Outside-halo fragment (past the silhouette) — same as DrawFacetSolid's else branch.
                        float pa = Mathf.Clamp01(haloAmt) * alphaEnv;
                        if (pa <= 0.002f) continue;
                        float rr = Mathf.Clamp01(edgeGlowColor.r * brightMul), gg = Mathf.Clamp01(edgeGlowColor.g * brightMul), bb = Mathf.Clamp01(edgeGlowColor.b * brightMul);
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, R));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                }
        }

        // ── Ring: a flat two-sided tilted annulus (a Saturn ring), analytic ──────────────────────────────────
        // The ring lies in its own LOCAL XY plane (normal +Z) at radius ρ ∈ [innerR, R], transformed to screen by
        // the facet solids' own Rot (yaw = particleSpin, tilt = gemTilt). Under that map, with spin 0, a screen
        // pixel (lx, ly) comes from local (u, v) = (lx, ly/cos·tilt) — i.e. the ellipse is the circle compressed
        // vertically by cos(tilt) — EXACTLY Bakery's RingWithTilt, which is why the edge-on degeneracy is |cos
        // tilt| ≈ 0 (tilt → 90°). Spin adds an in-plane roll (a shear/rotation of the ellipse). It's TWO-SIDED
        // (no backface cull): the plane normal is flipped to whichever side faces the viewer (N.z > 0). Lighting /
        // lines / glows mirror DrawFacetSolid, with the flat plane's CONSTANT normal and a per-pixel 3D plane point.
        static void DrawRing(Color32[] buf, int W, int H, float cx, float cy, float own,
                             PyrePlusSpec spec, int particleIndex, in ModSet mods, int frameIndex,
                             float sizeMul, float brightMul)
        {
            float R = Mathf.Max(0f, Eval(spec.size, own, spec.seed, particleIndex, FldSize)) * sizeMul;
            if (R < 1.5f) return;
            float alphaEnv = Mathf.Clamp01(Eval(spec.alpha, own, spec.seed, particleIndex, FldAlpha));
            if (alphaEnv <= 0.002f) return;
            float innerR = R * Mathf.Clamp(spec.ringInner, 0.1f, 0.92f);   // the hole radius

            if (!(IsStaticZero(spec.particlePathX) && IsStaticZero(spec.particlePathY)))
            {
                cx += Eval(spec.particlePathX, own, spec.seed, particleIndex, FldPathX);
                cy += Eval(spec.particlePathY, own, spec.seed, particleIndex, FldPathY);
            }

            // Rotation: spin → yaw about Y (rolls the ellipse in-plane), gemTilt → tilt about X (opens/closes it).
            float yaw = Eval(spec.particleSpin, own, spec.seed, particleIndex, FldSpin) * Mathf.Deg2Rad;
            float tilt = Eval(spec.gemTilt, own, spec.seed, particleIndex, FldGemTilt) * Mathf.Deg2Rad;
            float cyw = Mathf.Cos(yaw), syw = Mathf.Sin(yaw);
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            // Edge-on degeneracy: |cos tilt| ≈ 0 (the design's check) collapses the ellipse to an invisible sliver;
            // |cos yaw| ≈ 0 does the same on the other axis AND would divide by zero in the inverse map below.
            if (Mathf.Abs(ct) < 0.02f || Mathf.Abs(cyw) < 0.02f) return;

            // Forward map, local (u,v,0) → screen:  lx = u·cyw ;  ly = u·(syw·st) + v·ct.  The screen-space plane
            // normal is Rot(0,0,1) = (syw, −cyw·st, cyw·ct); pick the viewer-facing side (N.z > 0) — the ring is
            // two-sided so we never cull it, we just shade whichever face points at the camera. N is CONSTANT (the
            // plane is flat), so it's computed once here, not per pixel.
            float sywst = syw * st;
            Vector3 N = new Vector3(syw, -cyw * st, cyw * ct);
            if (N.z < 0f) N = -N;

            float lyaw = spec.gemLightYaw * Mathf.Deg2Rad, lpitch = spec.gemLightPitch * Mathf.Deg2Rad;
            float ldist = 3.5f * R, lhoriz = ldist * Mathf.Cos(lpitch);
            Vector3 lightPos = new Vector3(lhoriz * Mathf.Sin(lyaw), ldist * Mathf.Sin(lpitch), lhoriz * Mathf.Cos(lyaw));
            float lrange = 4.7f * R, lrange2 = lrange * lrange;
            Vector3 viewDir = new Vector3(0f, 0f, 1f);

            Color specColor = spec.gemSpecularColor;
            Color innerColor = spec.gemInnerGlowColor;
            Color lineColor = spec.gemLineColor;
            Color edgeGlowColor = spec.gemEdgeGlowColor;
            float lineW = spec.gemLineWidth;
            float ambient = spec.gemAmbient, specStr = spec.gemSpecular;

            float edgeGlow = Mathf.Clamp01(Eval(spec.gemEdgeGlow, own, spec.seed, particleIndex, FldGemEdgeGlow));
            float innerGlow = Mathf.Clamp01(Eval(spec.gemInnerGlow, own, spec.seed, particleIndex, FldGemInnerGlow));
            float haloR = Mathf.Max(2.5f, 0.24f * R);
            float innerGlowR = Mathf.Max(3f, 0.30f * R);   // inner-GLOW band radius (distinct from innerR, the hole)
            Color baseCol = spec.colorOverLife != null ? spec.colorOverLife.Evaluate(own) : Color.white;

            // Screen bounds of the (sheared) outer ellipse + halo:  |lx| ≤ R·|cyw|,  |ly| ≤ R·(|syw·st| + |ct|).
            float exHalf = R * Mathf.Abs(cyw) + haloR;
            float eyHalf = R * (Mathf.Abs(sywst) + Mathf.Abs(ct)) + haloR;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - exHalf));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + exHalf));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - eyHalf));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + eyHalf));
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7);
            float invHole = 1f / Mathf.Max(0.001f, R - innerR);   // for crossFrac (0 at inner rim, 1 at outer)

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float lx = x + 0.5f - cx, ly = y + 0.5f - cy;
                    // Invert the forward map:  u = lx/cyw ;  v = (ly − u·syw·st)/ct.
                    float u = lx / cyw;
                    float v = (ly - u * sywst) / ct;
                    float rho = Mathf.Sqrt(u * u + v * v);

                    // Band SDF in ring-plane (local) units: < 0 inside the annulus, > 0 past either rim.
                    float dBand = Mathf.Max(innerR - rho, rho - R);
                    // Gradient-magnitude correction (the Bakery lesson: trueDist ≈ Δf/|∇f|). The tilt (and spin)
                    // compress the ellipse in screen space, so a fixed ring-plane distance spans FEWER screen
                    // pixels near the ellipse's flat sides than at its ends — an uncorrected local rim distance
                    // would draw a rim line that visibly thins there. Dividing the local distance by |∇ρ| (the
                    // local-to-screen contraction along the rim normal) restores a uniform SCREEN thickness all the
                    // way around. |∇ρ| is derived from the inverse map's partials (∂u/∂lx=1/cyw, ∂v/∂lx=−syw·st/
                    // (cyw·ct), ∂v/∂ly=1/ct); ∂ρ/∂lx = (u/cyw − v·syw·st/(cyw·ct))/ρ, ∂ρ/∂ly = v/(ct·ρ). Floored at
                    // 0.2 like Bakery so a near-zero gradient can't blow the distance up.
                    float drdx = rho > 1e-4f ? (u / cyw - v * sywst / (cyw * ct)) / rho : 0f;
                    float drdy = rho > 1e-4f ? (v / ct) / rho : 0f;
                    float gradMag = Mathf.Max(0.2f, Mathf.Sqrt(drdx * drdx + drdy * drdy));
                    float rimDist = Mathf.Abs(dBand) / gradMag;   // screen-space distance to the NEAREST rim (both)

                    bool hasFace = dBand <= 0f;
                    bool isLine = false;
                    float fr = 0f, fg = 0f, fb = 0f;
                    if (hasFace)
                    {
                        // 3D point on the tilted plane at this pixel (orthographic: its screen x/y ARE lx/ly; z from
                        // the forward map's P.z = v·st − u·syw·ct).
                        float pz = v * st - u * syw * ct;
                        Vector3 P = new Vector3(lx, ly, pz);
                        Vector3 toL = lightPos - P;
                        float dist = toL.magnitude;
                        Vector3 L = toL / Mathf.Max(1e-4f, dist);
                        float atten = 1f / (1f + dist * dist / lrange2);
                        float ndl = Mathf.Max(0f, Vector3.Dot(N, L));
                        float lit = ambient + 2.1f * ndl * atten;
                        if (rimDist <= lineW)   // BOTH rims draw the hard line (screen-distance, gradient-corrected)
                        {
                            isLine = true;
                            float k = Mathf.Clamp(0.25f + lit, 0f, 1.15f);
                            fr = lineColor.r * k; fg = lineColor.g * k; fb = lineColor.b * k;
                        }
                        else
                        {
                            Vector3 Hh = (L + viewDir).normalized;
                            float sp = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(N, Hh)), 48f);
                            float sAdd = specStr * sp * atten;
                            fr = baseCol.r * lit + specColor.r * sAdd;
                            fg = baseCol.g * lit + specColor.g * sAdd;
                            fb = baseCol.b * lit + specColor.b * sAdd;
                        }
                    }

                    float halo = Mathf.Pow(Mathf.Max(0f, 1f - rimDist / haloR), 2f);
                    float haloAmt = 0.85f * edgeGlow * halo;

                    if (hasFace)
                    {
                        fr += edgeGlowColor.r * haloAmt; fg += edgeGlowColor.g * haloAmt; fb += edgeGlowColor.b * haloAmt;
                        if (!isLine)
                        {
                            // Inner glow spreads across the band inward from BOTH rims (rimDist is the nearest-rim
                            // screen distance, so it's 0 at either rim and peaks at the band's middle).
                            float core = Mathf.Pow(Mathf.Clamp01(rimDist / innerGlowR), 1.4f);
                            float innerAmt = 0.75f * innerGlow * core;
                            fr += innerColor.r * innerAmt; fg += innerColor.g * innerAmt; fb += innerColor.b * innerAmt;
                        }
                        float rr = Mathf.Clamp01(fr * brightMul), gg = Mathf.Clamp01(fg * brightMul), bb = Mathf.Clamp01(fb * brightMul);
                        float pa = alphaEnv;
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01((rho - innerR) * invHole);
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                    else
                    {
                        // Outside-halo fragment — past the outer rim OR inside the hole (both are ring "edges").
                        float pa = Mathf.Clamp01(haloAmt) * alphaEnv;
                        if (pa <= 0.002f) continue;
                        float rr = Mathf.Clamp01(edgeGlowColor.r * brightMul), gg = Mathf.Clamp01(edgeGlowColor.g * brightMul), bb = Mathf.Clamp01(edgeGlowColor.b * brightMul);
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01((rho - innerR) * invHole);
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                }
        }

        // ── facet-solid geometry (model verts + triangle faces + LINE-edge set), one Build per form ──────────
        // Convention shared by all four: y up, +z toward the viewer; origin strictly INSIDE the convex solid so
        // DrawFacetSolid's centroid test resolves each face's outward normal. `lineEdges` names the edges that draw
        // as hard lines AND seed the inner glow; null = every edge is a line (Gem only). Edge keys use the same
        // `min*64 + max` packing DrawFacetSolid dedupes with (valid while every vertex index < 64 — true here: Gem
        // ≤ 10, Box 8, Pyramid 5, Can 34).
        static int EdgeKey(int i, int j) => i < j ? i * 64 + j : j * 64 + i;

        // Gem: n girdle verts + a crown apex + a pavilion apex, VERBATIM from the original DrawGem so a Gem stays
        // byte-identical. lineEdges == null ⇒ every visible facet boundary is a line (the gem's original look).
        static void BuildGemGeometry(PyrePlusSpec spec, float R, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            int n = Mathf.Clamp(spec.gemSides, 3, 8);
            float crownH = R * spec.gemCrown;
            float pavD = R * spec.gemPavilion;

            // Model: n girdle verts (index 0..n-1) at PI/2 + 2πk/n scaled R, crown apex (n) above, pavilion apex
            // (n+1) below. y is up, +z toward the viewer after rotation.
            model = new Vector3[n + 2];
            for (int k = 0; k < n; k++)
            {
                float a = Mathf.PI * 0.5f + 2f * Mathf.PI * k / n;
                model[k] = new Vector3(R * Mathf.Cos(a), 0f, R * Mathf.Sin(a));
            }
            model[n] = new Vector3(0f, crownH, 0f);
            model[n + 1] = new Vector3(0f, -pavD, 0f);

            // Faces (generalize the prototype's `& 3` → `% n`): n crown triangles apexT+girdle(i,i+1), then n
            // pavilion triangles apexB+girdle(i+1,i). Outward winding is fixed per face by the centroid test.
            faces = new int[2 * n][];
            for (int i = 0; i < n; i++) faces[i] = new[] { n, i, (i + 1) % n };
            for (int i = 0; i < n; i++) faces[n + i] = new[] { n + 1, (i + 1) % n, i };
            lineEdges = null;   // every edge is a line edge — the gem's original behaviour
        }

        // Box: a real cuboid — half-extents (R, R·solidAspect, R·solidDepth). 8 shared verts, 6 quads → 12 tris.
        // The genuinely-shared vertices are what make the silhouette CLOSE (the Bakery oblique-box bug was two
        // faces extruded in different directions that never met — impossible here since all faces index the same
        // 8 corners). Each quad's splitting diagonal is NON-line, so the line set is exactly the 12 cube edges.
        static void BuildBoxGeometry(PyrePlusSpec spec, float R, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            float hx = R, hy = R * spec.solidAspect, hz = R * spec.solidDepth;
            model = new[]
            {
                new Vector3(-hx, -hy, -hz), // 0
                new Vector3( hx, -hy, -hz), // 1
                new Vector3( hx,  hy, -hz), // 2
                new Vector3(-hx,  hy, -hz), // 3
                new Vector3(-hx, -hy,  hz), // 4
                new Vector3( hx, -hy,  hz), // 5
                new Vector3( hx,  hy,  hz), // 6
                new Vector3(-hx,  hy,  hz), // 7
            };
            // Each quad is a coplanar face loop (v0,v1,v2,v3); winding is fixed by DrawFacetSolid's centroid test.
            int[][] quads =
            {
                new[] { 0, 1, 2, 3 }, // -Z back
                new[] { 4, 5, 6, 7 }, // +Z front
                new[] { 0, 1, 5, 4 }, // -Y bottom
                new[] { 3, 2, 6, 7 }, // +Y top
                new[] { 0, 3, 7, 4 }, // -X left
                new[] { 1, 2, 6, 5 }, // +X right
            };
            faces = new int[12][];
            lineEdges = new HashSet<int>();
            for (int q = 0; q < quads.Length; q++)
            {
                int a = quads[q][0], b = quads[q][1], c = quads[q][2], d = quads[q][3];
                faces[q * 2]     = new[] { a, b, c };
                faces[q * 2 + 1] = new[] { a, c, d };
                // the four perimeter edges are cube edges (lines); the (a,c) diagonal is the seam (non-line).
                lineEdges.Add(EdgeKey(a, b));
                lineEdges.Add(EdgeKey(b, c));
                lineEdges.Add(EdgeKey(c, d));
                lineEdges.Add(EdgeKey(d, a));
            }
        }

        // Pyramid: apex at (0, +R·solidAspect, 0), a rectangular base of half-width R (x) × R·solidDepth (z) at
        // y = -R·solidAspect·0.35. 4 side tris + a base quad split into 2 tris (its diagonal non-line). At the
        // default solidDepth == 1 the base is the square half-width-R base the design specifies; a non-default
        // Depth stretches it front-to-back so the shown Depth control is live. Line set = 4 apex spokes + 4 base
        // perimeter edges.
        static void BuildPyramidGeometry(PyrePlusSpec spec, float R, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            float apexY = R * spec.solidAspect;
            float baseY = -R * spec.solidAspect * 0.35f;
            float hz = R * spec.solidDepth;
            model = new[]
            {
                new Vector3(0f, apexY, 0f), // 0 apex
                new Vector3(-R, baseY, -hz), // 1
                new Vector3( R, baseY, -hz), // 2
                new Vector3( R, baseY,  hz), // 3
                new Vector3(-R, baseY,  hz), // 4
            };
            faces = new[]
            {
                new[] { 0, 1, 2 }, // side
                new[] { 0, 2, 3 }, // side
                new[] { 0, 3, 4 }, // side
                new[] { 0, 4, 1 }, // side
                new[] { 1, 2, 3 }, // base tri 1
                new[] { 1, 3, 4 }, // base tri 2 — diagonal (1,3) is the seam (non-line)
            };
            lineEdges = new HashSet<int>
            {
                EdgeKey(0, 1), EdgeKey(0, 2), EdgeKey(0, 3), EdgeKey(0, 4), // apex spokes
                EdgeKey(1, 2), EdgeKey(2, 3), EdgeKey(3, 4), EdgeKey(4, 1), // base perimeter
            };
        }

        // Can: a vertical cylinder approximated as a 16-sided prism — radius R, half-height R·solidAspect
        // (solidDepth is unused; the cross-section is circular). 16 top-rim + 16 bottom-rim verts + 2 cap centres.
        // Barrel = 16 quads → 32 tris; each cap = a 16-tri fan. LINE edges = the two cap rims ONLY; the barrel's
        // vertical seams + quad diagonals + cap fan spokes are all non-line. Flat per-face barrel normals band into
        // 16 strips — accepted retro banding for this slice.
        static void BuildCanGeometry(PyrePlusSpec spec, float R, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            const int seg = 16;
            float hy = R * spec.solidAspect;
            model = new Vector3[seg * 2 + 2];
            for (int k = 0; k < seg; k++)
            {
                float a = 2f * Mathf.PI * k / seg;
                float vx = R * Mathf.Cos(a), vz = R * Mathf.Sin(a);
                model[k] = new Vector3(vx, hy, vz);          // top rim 0..15
                model[seg + k] = new Vector3(vx, -hy, vz);   // bottom rim 16..31
            }
            int topC = seg * 2;       // 32
            int botC = seg * 2 + 1;   // 33
            model[topC] = new Vector3(0f, hy, 0f);
            model[botC] = new Vector3(0f, -hy, 0f);

            var faceList = new List<int[]>(seg * 4);
            lineEdges = new HashSet<int>();
            for (int k = 0; k < seg; k++)
            {
                int k1 = (k + 1) % seg;
                int t0 = k, t1 = k1, b0 = seg + k, b1 = seg + k1;
                faceList.Add(new[] { t0, t1, b1 });   // barrel quad, tri 1
                faceList.Add(new[] { t0, b1, b0 });   // barrel quad, tri 2 (diagonal + verticals non-line)
                faceList.Add(new[] { topC, t0, t1 }); // top cap fan
                faceList.Add(new[] { botC, b1, b0 }); // bottom cap fan
                lineEdges.Add(EdgeKey(t0, t1));       // top rim (line)
                lineEdges.Add(EdgeKey(b0, b1));       // bottom rim (line)
            }
            faces = faceList.ToArray();
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
