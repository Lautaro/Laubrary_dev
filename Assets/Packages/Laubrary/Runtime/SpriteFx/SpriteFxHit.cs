using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    // ── Burst BATCHED pixel/alpha-mask hit-detection (slice #49) ──────────────────────────────────────────────────
    //
    // An ADDITIVE, OPT-IN utility: test MANY candidate mask-vs-mask overlaps at once, on Burst, verified equivalent
    // to a managed reference. It does NOT replace the live combat flow — the immediate per-hit path
    // (Combat.TryDamage → Hitbox → ReelHitFilter.ConfirmHit → ZonedAnimationPlayer.PixelOverlaps, in
    // Runtime/Combat2D + Runtime/ReelCombat + Runtime/Launimator) is already cheap: coarse MetaLayer cells gated by
    // the Physics2D collider broad-phase. That path stays exactly as-is.
    //
    // Burst pays off only at HIGH candidate-pair counts or FULL-RESOLUTION alpha masks, where the immediate
    // per-pair managed test would stall the main thread. THE ADOPTION PATH (the integration step left to the user,
    // deliberately NOT wired here): each frame, collect the candidate pairs that already survived the cheap
    // broad-phase (distance + collider overlap + faction) into a NativeArray<SfxMaskPair> over a shared packed mask
    // buffer, then in LateUpdate call SpriteFxHit.BatchOverlap(...) once and read the per-pair results — instead of
    // calling PixelOverlaps per pair inline. The masks are the same painted-silhouette coverage the meta-layer
    // already carries (or a sprite's full-resolution alpha); build them with the CoverageFrom* helpers below.
    //
    // Packing model (so many pairs pack into flat NativeArrays):
    //   • masks   : one flat NativeArray<byte> holding EVERY instance's coverage bytes end-to-end (alpha 0..255 per
    //               cell, row-major, bottom-left origin — matching Unity texture/GetPixels32 layout). A cell counts
    //               as "covered" when its byte >= the pair's threshold (1 = any non-zero alpha).
    //   • SfxMask : one instance's placement — its byte offset/length (w*h) into `masks` + its bottom-left cell
    //               position (x,y) and size (w,h) in a SHARED integer grid (world-pixel space; see the adoption note
    //               on mixed PPU below). Two masks overlap where their grid AABBs intersect.
    //   • SfxMaskPair : the two masks (inline, by value) + the PRECOMPUTED overlap AABB (loX..hiX, loY..hiY,
    //               exclusive upper) so the job walks ONLY the intersection region, + the coverage threshold.
    //   • SfxMaskHit  : per-pair result — hit flag + the first coincident cell (shared-grid coords).

    /// One packed coverage mask instance: where its bytes live in the flat `masks` array, and where it sits in the
    /// shared integer grid. Fully blittable (crosses into Burst).
    public struct SfxMask
    {
        public int offset;      // start index into the flat masks byte array
        public int w, h;        // dimensions (length == w*h)
        public int x, y;        // bottom-left cell position in the shared grid
    }

    /// A candidate pair to test: two masks + their precomputed overlap AABB + coverage threshold. Blittable so a
    /// whole batch packs into one NativeArray&lt;SfxMaskPair&gt;.
    public struct SfxMaskPair
    {
        public SfxMask a, b;
        public int loX, loY, hiX, hiY;   // overlap AABB, exclusive upper. hiX<=loX || hiY<=loY ⇒ AABBs disjoint.
        public byte threshold;           // a cell is "covered" when its coverage byte >= threshold (min 1).
    }

    /// Per-pair result. `hit`=1 when any covered cell coincides; (x,y) is then the FIRST such cell in the shared
    /// grid (row-major scan, so it matches the managed reference exactly). Miss ⇒ hit=0, (x,y)=(-1,-1).
    public struct SfxMaskHit
    {
        public byte hit;
        public int x, y;
        public bool Hit => hit != 0;
        public static SfxMaskHit Miss => new SfxMaskHit { hit = 0, x = -1, y = -1 };
    }

    /// The Burst batched path: one work item per PAIR. Broad-phase (disjoint AABB) skips immediately; otherwise it
    /// walks the intersection region only. CompileSynchronously so the first Schedule runs native (no Mono-fallback
    /// first frame) — matching the determinism discipline of the rest of the module. (Pure integer/byte math, so no
    /// float-codegen tolerance question arises: the job is byte-EXACT vs the managed path, not "within 1/255".)
    [BurstCompile(CompileSynchronously = true)]
    public struct MaskOverlapJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> masks;
        [ReadOnly] public NativeArray<SfxMaskPair> pairs;
        [WriteOnly] public NativeArray<SfxMaskHit> results;

        public void Execute(int i) => results[i] = SpriteFxHit.TestPair(pairs[i], masks);
    }

    /// The batched mask-overlap utility: pack masks, build pairs, run the batch inline OR on Burst, plus the managed
    /// reference oracle the equivalence test verifies against.
    public static class SpriteFxHit
    {
        // ── pair construction ───────────────────────────────────────────────────────────────────────────────────

        /// Build a pair from two placed masks, PRECOMPUTING the overlap AABB (so the job tests only the intersection
        /// region). `threshold` is clamped to a min of 1 (0 would treat fully-transparent cells as covered).
        public static SfxMaskPair MakePair(in SfxMask a, in SfxMask b, byte threshold = 1)
        {
            int loX = Mathf.Max(a.x, b.x);
            int loY = Mathf.Max(a.y, b.y);
            int hiX = Mathf.Min(a.x + a.w, b.x + b.w);
            int hiY = Mathf.Min(a.y + a.h, b.y + b.h);
            return new SfxMaskPair
            {
                a = a, b = b, loX = loX, loY = loY, hiX = hiX, hiY = hiY,
                threshold = threshold < 1 ? (byte)1 : threshold,
            };
        }

        // ── the shared Burst-legal core (called by the job AND the inline path — so the two never drift) ──────────

        /// Test one pair using its PRECOMPUTED AABB. Burst-legal (no managed state / no Mathf): the broad-phase
        /// early-out, then a row-major (gy outer, gx inner) scan of the intersection returning the first coincident
        /// covered cell. The scan order is IDENTICAL to <see cref="MaskOverlap"/> so first-hit points match exactly.
        public static SfxMaskHit TestPair(in SfxMaskPair p, in NativeArray<byte> masks)
        {
            byte th = p.threshold < 1 ? (byte)1 : p.threshold;
            if (p.hiX <= p.loX || p.hiY <= p.loY) return SfxMaskHit.Miss;   // disjoint AABBs — skip
            for (int gy = p.loY; gy < p.hiY; gy++)
            {
                int aRow = p.a.offset + (gy - p.a.y) * p.a.w - p.a.x;
                int bRow = p.b.offset + (gy - p.b.y) * p.b.w - p.b.x;
                for (int gx = p.loX; gx < p.hiX; gx++)
                {
                    if (masks[aRow + gx] < th) continue;
                    if (masks[bRow + gx] < th) continue;
                    return new SfxMaskHit { hit = 1, x = gx, y = gy };
                }
            }
            return SfxMaskHit.Miss;
        }

        // ── the managed reference oracle (INDEPENDENT of TestPair — the equivalence check compares them) ──────────

        /// Straightforward managed overlap test: computes its OWN overlap AABB from the two placements (does not read
        /// the pair's precomputed one) and scans row-major for the first coincident covered cell. Written plainly and
        /// independently so "job == this" is a genuine cross-check of both the packing (MakePair's AABB) and the
        /// Burst-compiled TestPair. Returns true on overlap, with (hitX,hitY) the first coincident cell.
        public static bool MaskOverlap(in SfxMask a, in SfxMask b, NativeArray<byte> masks, byte threshold,
                                       out int hitX, out int hitY)
        {
            hitX = -1; hitY = -1;
            int th = threshold < 1 ? 1 : threshold;
            int loX = a.x > b.x ? a.x : b.x;
            int loY = a.y > b.y ? a.y : b.y;
            int hiX = (a.x + a.w) < (b.x + b.w) ? (a.x + a.w) : (b.x + b.w);
            int hiY = (a.y + a.h) < (b.y + b.h) ? (a.y + a.h) : (b.y + b.h);
            if (hiX <= loX || hiY <= loY) return false;
            for (int gy = loY; gy < hiY; gy++)
                for (int gx = loX; gx < hiX; gx++)
                {
                    int av = masks[a.offset + (gy - a.y) * a.w + (gx - a.x)];
                    if (av < th) continue;
                    int bv = masks[b.offset + (gy - b.y) * b.w + (gx - b.x)];
                    if (bv < th) continue;
                    hitX = gx; hitY = gy; return true;
                }
            return false;
        }

        // ── batch entry points (inline OR Burst job) ────────────────────────────────────────────────────────────

        /// Run the whole batch INLINE on the main thread (the same TestPair the job runs). Caller owns all arrays;
        /// `results.Length` must equal `pairs.Length`.
        public static void BatchOverlapInline(NativeArray<SfxMaskPair> pairs, NativeArray<byte> masks,
                                              NativeArray<SfxMaskHit> results)
        {
            for (int i = 0; i < pairs.Length; i++) results[i] = TestPair(pairs[i], masks);
        }

        /// Schedule the Burst job over the batch. Caller owns/disposes every array; `results.Length` must equal
        /// `pairs.Length`. Complete the returned handle before reading `results`.
        public static JobHandle Schedule(NativeArray<SfxMaskPair> pairs, NativeArray<byte> masks,
                                         NativeArray<SfxMaskHit> results, int batch = 32, JobHandle deps = default)
        {
            var job = new MaskOverlapJob { masks = masks, pairs = pairs, results = results };
            return job.Schedule(pairs.Length, Mathf.Max(1, batch), deps);
        }

        /// The clean one-call entry: allocate `results` and run the batch either inline or on Burst (blocking on
        /// Complete). Caller disposes `results` (and the `pairs`/`masks` it passed).
        public static void BatchOverlap(NativeArray<SfxMaskPair> pairs, NativeArray<byte> masks,
                                        out NativeArray<SfxMaskHit> results, Allocator alloc, bool useBurst,
                                        int batch = 32)
        {
            results = new NativeArray<SfxMaskHit>(pairs.Length, alloc);
            if (useBurst) Schedule(pairs, masks, results, batch).Complete();
            else BatchOverlapInline(pairs, masks, results);
        }

        /// Same as <see cref="BatchOverlap(NativeArray{SfxMaskPair},NativeArray{byte},out NativeArray{SfxMaskHit},Allocator,bool,int)"/>
        /// but the inline-vs-Burst choice follows the project toggle (SpriteFxSettings.UseBurstJobs, mirroring #47) —
        /// default INLINE, since for small pixel-art masks the job scheduling cost exceeds the loop; flip Burst on for
        /// large masks or high pair counts.
        public static void BatchOverlap(NativeArray<SfxMaskPair> pairs, NativeArray<byte> masks,
                                        out NativeArray<SfxMaskHit> results, Allocator alloc, int batch = 32)
            => BatchOverlap(pairs, masks, out results, alloc, SpriteFxSettings.UseBurstJobs, batch);

        // ── mask-build helpers (sprite / MetaLayer / raw → packed coverage) ─────────────────────────────────────
        //
        // SpriteFx sits BELOW Launimator/Zoetrope in the dependency graph and must not reference their types, so the
        // MetaLayer helper takes the raw int[] cells (the caller reads them off a MetaFrame) rather than the type.

        /// Coverage from Color32 pixels: coverage[i] = alpha byte. Row-major, bottom-left origin (GetPixels32 layout).
        public static byte[] CoverageFromColors(Color32[] px)
        {
            if (px == null) return null;
            var cov = new byte[px.Length];
            for (int i = 0; i < px.Length; i++) cov[i] = px[i].a;
            return cov;
        }

        /// Coverage from MetaLayer-style int cells (value > 0 ⇒ fully covered 255, else 0). `cells.Length` must be
        /// &gt;= w*h.
        public static byte[] CoverageFromCells(int[] cells, int w, int h)
        {
            if (cells == null || w <= 0 || h <= 0 || cells.Length < w * h) return null;
            var cov = new byte[w * h];
            for (int i = 0; i < cov.Length; i++) cov[i] = cells[i] > 0 ? (byte)255 : (byte)0;
            return cov;
        }

        /// Coverage from a readable Sprite: the alpha of every pixel inside the sprite's rect, row-major bottom-left.
        /// Requires the source texture to be Read/Write enabled (returns null otherwise — the caller decides the
        /// fallback). Outputs the mask's pixel dimensions.
        public static byte[] CoverageFromSprite(Sprite sprite, out int w, out int h)
        {
            w = 0; h = 0;
            if (sprite == null) return null;
            var tex = sprite.texture;
            if (tex == null || !tex.isReadable) return null;
            Rect r = sprite.rect;
            int rx = Mathf.RoundToInt(r.x), ry = Mathf.RoundToInt(r.y);
            w = Mathf.RoundToInt(r.width); h = Mathf.RoundToInt(r.height);
            if (w <= 0 || h <= 0) { w = 0; h = 0; return null; }
            var px = tex.GetPixels32();
            int tw = tex.width;
            var cov = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    cov[y * w + x] = px[(ry + y) * tw + (rx + x)].a;
            return cov;
        }
    }

    /// Managed builder that concatenates per-instance coverage arrays into ONE flat byte buffer and hands back an
    /// <see cref="SfxMask"/> (offset + placement) for each — the packing step before a batch. Fill it each frame,
    /// then <see cref="ToNative"/> once and build the pairs over the returned NativeArray.
    public sealed class SfxMaskBuilder
    {
        readonly List<byte> _bytes = new List<byte>();

        public int ByteCount => _bytes.Count;
        public void Clear() => _bytes.Clear();

        /// Append one mask's coverage (length must be w*h) at grid position (x,y); returns its placement handle.
        public SfxMask Add(byte[] coverage, int w, int h, int x, int y)
        {
            var m = new SfxMask { offset = _bytes.Count, w = w, h = h, x = x, y = y };
            if (coverage != null) _bytes.AddRange(coverage);
            return m;
        }

        /// Copy the accumulated bytes into a fresh NativeArray (caller disposes).
        public NativeArray<byte> ToNative(Allocator alloc)
        {
            var arr = new NativeArray<byte>(_bytes.Count, alloc, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < _bytes.Count; i++) arr[i] = _bytes[i];
            return arr;
        }
    }
}
