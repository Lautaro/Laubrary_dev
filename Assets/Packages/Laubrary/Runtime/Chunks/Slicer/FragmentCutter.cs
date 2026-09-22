using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// One large piece of a sliced sprite: its own cropped pixel block, where it sat in the original picture,
    /// and how far its centre is from the sprite's pivot in WORLD units. That last field is the whole reason
    /// this type exists rather than just returning sprites — a fragment that spawns at the burst origin
    /// destroys the "it split apart" read, so where a piece came from has to travel with the piece.
    public struct FragmentPiece
    {
        /// The cropped block, RGBA32, bottom-left origin, row-major (index = y * width + x) — the same
        /// ordering GetPixels32/SetPixels32 use, so it uploads without a flip. Pixels of other cells are
        /// transparent, so the pieces tile the original exactly.
        public Color32[] pixels;
        public int width, height;
        /// The piece's bounding box in the SOURCE sprite's own pixel space (relative to its textureRect,
        /// bottom-left origin) — same convention as SampledChunkSprites' out-cutRect, so an editor preview can
        /// draw where a cut came from without re-deriving it.
        public RectInt rect;
        /// Offset from the source sprite's PIVOT to this piece's centre, in world units. Add it to the burst
        /// origin and the piece starts exactly where that part of the picture was.
        public Vector2 offsetUnits;
        /// How many opaque pixels the piece actually owns (its cell's area, not its bounding box's).
        public int areaPx;
    }

    /// A tiny deterministic xorshift RNG. Deliberately NOT UnityEngine.Random: a burst that reads the shared
    /// generator perturbs everything else drawing randomness that frame, and a seeded cut must reproduce
    /// regardless of what else ran first. Same reason SampledChunkSprites' editor preview has to save/restore
    /// Random.state — this side-steps the problem instead of working around it.
    internal struct FragmentRng
    {
        uint _s;
        public FragmentRng(int seed) { _s = (uint)seed; if (_s == 0u) _s = 2463534242u; }
        public uint NextUInt() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        public float Next01() => (NextUInt() & 0xFFFFFFu) / 16777216f;
        public int Range(int minInclusive, int maxExclusive)
            => maxExclusive <= minInclusive ? minInclusive
             : minInclusive + Mathf.Min((int)(Next01() * (maxExclusive - minInclusive)), maxExclusive - minInclusive - 1);
        public float Range(float a, float b) => a + (b - a) * Next01();
        public bool NextBool() => (NextUInt() & 1u) == 0u;
    }

    /// Cuts a sprite into a SMALL number of LARGE pieces that still read as parts of the original thing —
    /// the big-piece sibling of SampledChunkSprites (which is tuned for many small, deliberately
    /// unrecognisable debris bits).
    ///
    /// ALGORITHM — a seeded cell partition (Voronoi over the sprite's own opaque pixels), chosen over the
    /// cheaper recursive straight-line split for one reason: every opaque pixel is assigned to exactly one
    /// cell, so the pieces TILE THE ORIGINAL EXACTLY. Nothing is lost between the cuts and nothing overlaps,
    /// which is what makes three fragments still read as one spaceship that broke rather than three unrelated
    /// shapes. Seeds are spread by farthest-of-K sampling (pick K opaque candidates, keep the one furthest
    /// from the seeds chosen so far) so the cells come out comparable in size without the cost of a full
    /// Lloyd relaxation; a plain uniform pick clumps and produces one huge piece plus slivers. Cells smaller
    /// than minPieceAreaPx are MERGED into their nearest surviving neighbour rather than dropped, so the
    /// exact-tiling guarantee survives the degenerate-piece guard.
    ///
    /// Requires the source texture's Read/Write Enabled import setting, like any GetPixels call. Cut()
    /// returns null rather than throwing when it is off — a missing import flag on someone's art must not
    /// crash a burst (same contract SampledChunkSprites.Sample honours).
    public static class FragmentCutter
    {
        /// Alpha (0–255) at or below which a pixel counts as "not part of the picture" and is never assigned
        /// to a cell. Low enough to keep soft anti-aliased edges with their piece, high enough to ignore the
        /// near-invisible fringe an atlas leaves around packed art.
        const byte OpaqueAlphaThreshold = 32;
        /// Candidates weighed per seed by the farthest-of-K spread. 16 is plenty at these piece counts and
        /// costs nothing next to the per-pixel assignment pass.
        const int SeedCandidates = 16;
        /// The hard piece-count range. The design calls for 2–6; the ceiling is looser than the UI's so a
        /// value dialled past the recommended range still cuts instead of silently clamping to something else.
        public const int MinPieces = 2;
        public const int MaxPieces = 12;
        /// How many distinct seeded cuts are remembered. Small on purpose: this is a hot-repeat cache for the
        /// "same burst fired over and over" case, not a general asset cache.
        const int MaxCachedCuts = 8;

        // ── the cut cache ────────────────────────────────────────────────────────────────────────────────
        // WHAT IS CACHED, AND WHY THAT: the PIXEL DATA of each piece, never the built Texture2D/Sprite. The
        // partition is the expensive half (a full GetPixels plus an O(W*H*N) assignment pass); building a
        // small texture from a ready Color32[] is cheap by comparison. Caching the sprites instead would
        // collide head-on with the ownership rule that a fragment destroys its own sprite+texture when it
        // dies — the second burst would find a destroyed sprite in the cache. So: cache the cut, rebuild the
        // sprites. Only SEEDED cuts are cached (seed == 0 means "reroll every play", so its key would never
        // repeat and the cache would just grow), and the store is a bounded MRU that evicts the oldest entry.
        readonly struct CutKey : System.IEquatable<CutKey>
        {
            readonly int _sprite, _pieces, _minArea, _seed, _ppuBits;
            public CutKey(int sprite, int pieces, int minArea, int seed, float ppu)
            { _sprite = sprite; _pieces = pieces; _minArea = minArea; _seed = seed; _ppuBits = System.BitConverter.SingleToInt32Bits(ppu); }
            public bool Equals(CutKey o) => _sprite == o._sprite && _pieces == o._pieces
                                         && _minArea == o._minArea && _seed == o._seed && _ppuBits == o._ppuBits;
            public override bool Equals(object o) => o is CutKey k && Equals(k);
            public override int GetHashCode()
            {
                unchecked { return ((((_sprite * 397) ^ _pieces) * 397 ^ _minArea) * 397 ^ _seed) * 397 ^ _ppuBits; }
            }
        }

        static readonly Dictionary<CutKey, FragmentPiece[]> _cache = new Dictionary<CutKey, FragmentPiece[]>();
        static readonly List<CutKey> _cacheOrder = new List<CutKey>();

        /// Drops every remembered cut. Nothing else needs to call this at runtime (the cache is bounded); it
        /// exists so an editor that re-imports the source art can be sure it is not looking at a stale cut.
        public static void ClearCache()
        {
            _cache.Clear();
            _cacheOrder.Clear();
        }

        /// Splits source into `pieceCount` large pieces. Returns null (never throws) when source is null, has
        /// no texture, the texture is not readable, its rect is empty, or nothing in it is opaque.
        /// <paramref name="cache"/> should be false for an unseeded (reroll-every-time) cut and for editor
        /// previews, both of which would otherwise fill the cache with keys that never repeat.
        public static IReadOnlyList<FragmentPiece> Cut(Sprite source, int pieceCount, int minPieceAreaPx,
                                                       int seed, bool cache, float pixelsPerUnit)
        {
            if (source == null || source.texture == null) return null;

            pieceCount = Mathf.Clamp(pieceCount, MinPieces, MaxPieces);
            minPieceAreaPx = Mathf.Max(1, minPieceAreaPx);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);

            var key = new CutKey(source.GetInstanceID(), pieceCount, minPieceAreaPx, seed, pixelsPerUnit);
            if (cache && _cache.TryGetValue(key, out var hit)) return hit;

            var cut = CutUncached(source, pieceCount, minPieceAreaPx, seed, pixelsPerUnit);
            if (cut == null) return null;

            if (cache)
            {
                _cache[key] = cut;
                _cacheOrder.Add(key);
                while (_cacheOrder.Count > MaxCachedCuts)
                {
                    _cache.Remove(_cacheOrder[0]);
                    _cacheOrder.RemoveAt(0);
                }
            }
            return cut;
        }

        static FragmentPiece[] CutUncached(Sprite source, int pieceCount, int minPieceAreaPx, int seed, float pixelsPerUnit)
        {
            var texRect = source.textureRect;   // the sprite's own region inside a possibly-shared atlas texture
            int texX = Mathf.RoundToInt(texRect.x), texY = Mathf.RoundToInt(texRect.y);
            int W = Mathf.RoundToInt(texRect.width), H = Mathf.RoundToInt(texRect.height);
            if (W <= 0 || H <= 0) return null;

            Color32[] px = ReadRect(source.texture, texX, texY, W, H);
            if (px == null) return null;   // not Read/Write enabled — degrade, do not throw

            // Every pixel that belongs to the picture at all. If there is nothing here there is nothing to cut.
            var opaque = new List<int>(px.Length / 4);
            for (int i = 0; i < px.Length; i++)
                if (px[i].a > OpaqueAlphaThreshold) opaque.Add(i);
            if (opaque.Count == 0) return null;

            int n = Mathf.Min(pieceCount, opaque.Count);
            if (n < 1) return null;

            var rng = new FragmentRng(seed);
            PickSeeds(opaque, W, n, ref rng, out int[] sx, out int[] sy);

            // Nearest-seed assignment: the partition itself. -1 = not part of the picture.
            var label = new int[px.Length];
            for (int i = 0; i < label.Length; i++) label[i] = -1;
            var counts = new int[n];
            for (int k = 0; k < opaque.Count; k++)
            {
                int i = opaque[k];
                int x = i % W, y = i / W;
                int best = 0; long bestD = long.MaxValue;
                for (int s = 0; s < n; s++)
                {
                    long dx = x - sx[s], dy = y - sy[s];
                    long d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = s; }
                }
                label[i] = best;
                counts[best]++;
            }

            MergeUndersizedCells(label, counts, opaque, W, sx, sy, minPieceAreaPx);

            return BuildPieces(px, label, counts, W, H, source, pixelsPerUnit);
        }

        /// A sprite's own pixels (atlas sub-rect aware), bottom-left origin, row-major. Null when the sprite,
        /// its texture or its rect is unusable, or the texture is not Read/Write enabled. Public because an
        /// editor preview needs to draw the SAME pixels the cut partitions — sharing this one read is what
        /// keeps "where the preview says a piece comes from" identical to where the piece actually comes from.
        public static Color32[] ReadSpritePixels(Sprite source, out int width, out int height)
        {
            width = 0; height = 0;
            if (source == null || source.texture == null) return null;
            var texRect = source.textureRect;
            int x = Mathf.RoundToInt(texRect.x), y = Mathf.RoundToInt(texRect.y);
            int w = Mathf.RoundToInt(texRect.width), h = Mathf.RoundToInt(texRect.height);
            if (w <= 0 || h <= 0) return null;
            var px = ReadRect(source.texture, x, y, w, h);
            if (px == null) return null;
            width = w; height = h;
            return px;
        }

        /// Reads a sub-rect out of a texture. Returns null instead of propagating the UnityException a
        /// non-readable texture raises — the single most common real-world failure here.
        static Color32[] ReadRect(Texture2D tex, int x, int y, int W, int H)
        {
            if (!tex.isReadable) return null;
            try
            {
                if (x == 0 && y == 0 && W == tex.width && H == tex.height) return tex.GetPixels32();
                var block = tex.GetPixels(x, y, W, H);
                var px = new Color32[block.Length];
                for (int i = 0; i < block.Length; i++) px[i] = block[i];
                return px;
            }
            catch (UnityException) { return null; }
        }

        /// Farthest-of-K seed spread — see the class comment for why a uniform pick is not good enough.
        static void PickSeeds(List<int> opaque, int W, int n, ref FragmentRng rng, out int[] sx, out int[] sy)
        {
            sx = new int[n];
            sy = new int[n];
            int first = opaque[rng.Range(0, opaque.Count)];
            sx[0] = first % W; sy[0] = first / W;

            for (int s = 1; s < n; s++)
            {
                int bestIdx = -1; long bestD = -1;
                for (int c = 0; c < SeedCandidates; c++)
                {
                    int i = opaque[rng.Range(0, opaque.Count)];
                    int x = i % W, y = i / W;
                    long nearest = long.MaxValue;
                    for (int t = 0; t < s; t++)
                    {
                        long dx = x - sx[t], dy = y - sy[t];
                        long d = dx * dx + dy * dy;
                        if (d < nearest) nearest = d;
                    }
                    if (nearest > bestD) { bestD = nearest; bestIdx = i; }
                }
                sx[s] = bestIdx % W; sy[s] = bestIdx / W;
            }
        }

        /// Repeatedly folds the smallest surviving cell into its nearest surviving neighbour until every
        /// remaining cell clears minPieceAreaPx (or only two are left — a "fragment slicer" that hands back
        /// one piece has not sliced anything). Merging rather than dropping is what keeps the pieces tiling
        /// the original exactly, which is the whole point of the partition.
        static void MergeUndersizedCells(int[] label, int[] counts, List<int> opaque, int W,
                                         int[] sx, int[] sy, int minPieceAreaPx)
        {
            int alive = 0;
            for (int s = 0; s < counts.Length; s++) if (counts[s] > 0) alive++;

            while (alive > MinPieces)
            {
                int worst = -1;
                for (int s = 0; s < counts.Length; s++)
                    if (counts[s] > 0 && (worst < 0 || counts[s] < counts[worst])) worst = s;
                if (worst < 0 || counts[worst] >= minPieceAreaPx) break;

                // The nearest surviving OTHER seed swallows it.
                int into = -1; long bestD = long.MaxValue;
                for (int s = 0; s < counts.Length; s++)
                {
                    if (s == worst || counts[s] <= 0) continue;
                    long dx = sx[s] - sx[worst], dy = sy[s] - sy[worst];
                    long d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; into = s; }
                }
                if (into < 0) break;

                for (int k = 0; k < opaque.Count; k++)
                {
                    int i = opaque[k];
                    if (label[i] == worst) label[i] = into;
                }
                counts[into] += counts[worst];
                counts[worst] = 0;
                alive--;
            }
        }

        static FragmentPiece[] BuildPieces(Color32[] px, int[] label, int[] counts, int W, int H, Sprite source, float pixelsPerUnit)
        {
            int n = counts.Length;
            var minX = new int[n]; var minY = new int[n]; var maxX = new int[n]; var maxY = new int[n];
            for (int s = 0; s < n; s++) { minX[s] = int.MaxValue; minY[s] = int.MaxValue; maxX[s] = -1; maxY[s] = -1; }

            for (int i = 0; i < label.Length; i++)
            {
                int s = label[i];
                if (s < 0) continue;
                int x = i % W, y = i / W;
                if (x < minX[s]) minX[s] = x;
                if (y < minY[s]) minY[s] = y;
                if (x > maxX[s]) maxX[s] = x;
                if (y > maxY[s]) maxY[s] = y;
            }

            Vector2 pivotPx = source.pivot;                      // pixels, relative to the sprite rect's bottom-left
            float ppu = Mathf.Max(1f, pixelsPerUnit);

            var result = new List<FragmentPiece>(n);
            for (int s = 0; s < n; s++)
            {
                if (counts[s] <= 0 || maxX[s] < 0) continue;
                int x0 = minX[s], y0 = minY[s];
                int w = maxX[s] - x0 + 1, h = maxY[s] - y0 + 1;

                var block = new Color32[w * h];                   // default = fully transparent
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int src = (y0 + y) * W + (x0 + x);
                        if (label[src] != s) continue;
                        block[y * w + x] = px[src];
                    }

                // The piece's own centre, in the sprite's pixel space, measured from the sprite's pivot: the
                // fragment sprite is built pivoted at its own centre, so this offset is exactly where that
                // centre has to sit for the piece to start in the picture it was cut from.
                float cx = x0 + w * 0.5f, cy = y0 + h * 0.5f;
                result.Add(new FragmentPiece
                {
                    pixels = block,
                    width = w,
                    height = h,
                    rect = new RectInt(x0, y0, w, h),
                    offsetUnits = new Vector2(cx - pivotPx.x, cy - pivotPx.y) / ppu,
                    areaPx = counts[s],
                });
            }

            if (result.Count == 0) return null;
            SortClockwiseFromTop(result);
            return result.ToArray();
        }

        /// Numbers the pieces CLOCKWISE FROM 12 O'CLOCK by where each one sat in the picture (owner's decision,
        /// T-0365 Q3). Before this the order was whatever the nearest-seed partition happened to produce —
        /// stable for one seed, but meaning nothing: "piece 2" named a different part of the picture the
        /// moment a seed, a piece count or a live frame changed, so a Depth row pointing at it pointed
        /// somewhere else. Clockwise-from-the-top gives "piece 1" a meaning a human can find on screen — the
        /// piece that was at the top — and it survives a different live frame being cut.
        ///
        /// A piece sitting exactly on the pivot has no angle; it sorts first, then by area (largest first),
        /// then by its rect, so the whole order stays deterministic for one cut.
        static void SortClockwiseFromTop(List<FragmentPiece> pieces)
        {
            pieces.Sort((a, b) =>
            {
                int c = ClockAngle(a.offsetUnits).CompareTo(ClockAngle(b.offsetUnits));
                if (c != 0) return c;
                c = b.areaPx.CompareTo(a.areaPx);
                if (c != 0) return c;
                c = a.rect.y.CompareTo(b.rect.y);
                return c != 0 ? c : a.rect.x.CompareTo(b.rect.x);
            });
        }

        /// The piece's angle measured CLOCKWISE from straight up: 0 at 12 o'clock, π/2 at 3 o'clock. Atan2(x, y)
        /// — the arguments deliberately swapped from the usual Atan2(y, x) — is exactly that rotation, and the
        /// wrap keeps it in [0, 2π). A piece on the centre returns -1, which sorts it ahead of everything.
        static float ClockAngle(Vector2 offset)
        {
            if (offset.sqrMagnitude <= 1e-10f) return -1f;
            float a = Mathf.Atan2(offset.x, offset.y);
            return a < 0f ? a + Mathf.PI * 2f : a;
        }

        /// Turns one cut piece into a live sprite pivoted at its own centre. The texture and the sprite are
        /// both HideAndDontSave — they are runtime scratch, must never be written into a scene or asset, and
        /// are the caller's to destroy when the fragment dies (FragmentSlicerModule does exactly that).
        public static Sprite BuildSprite(in FragmentPiece piece, float pixelsPerUnit, string name)
        {
            if (piece.pixels == null || piece.width <= 0 || piece.height <= 0) return null;

            var tex = new Texture2D(piece.width, piece.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels32(piece.pixels);
            tex.Apply(false);

            var sprite = Sprite.Create(tex, new Rect(0, 0, piece.width, piece.height),
                                       new Vector2(0.5f, 0.5f), Mathf.Max(1f, pixelsPerUnit));
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
