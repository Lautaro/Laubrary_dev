using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.MetaMapper
{
    /// <summary>
    /// THE MODEL: typed spatial metadata on a visual subject — Points, Directions and painted Masks, in named
    /// layers, optionally varying per animation frame, all anchored to ONE declared coordinate space. A plain
    /// [Serializable] class so it embeds in ANY asset (an AnimationDef, a Prop, a game config); the standalone
    /// asset wrapper is <see cref="MetaMap"/>, for subjects (a bare Sprite) that cannot own one.
    ///
    /// It owns "what is where at frame i" — data, spaces, queries. It owns NO CLOCK, no playback and no events:
    /// frame-INDEXING is data (cheap, here), frame-ADVANCING is behaviour (expensive, stays in the players).
    /// That one cut is what lets a static shelf clump and a per-frame sword hitbox be the same model without
    /// either paying for the other.
    ///
    /// All queries answer in MAP SPACE. World conversion is the bindings' job — <see cref="MetaMapSprite"/> for
    /// SpritePixels, <see cref="MetaMapCells"/> for GridCells.
    /// </summary>
    [Serializable]
    public class MetaMapData
    {
        [Tooltip("The frame of reference for EVERY coordinate here. Set from the subject kind at creation; " +
                 "changing it later reinterprets all authored numbers and is not something an author should do " +
                 "by hand.")]
        public MapSpace space = MapSpace.SpritePixels;

        [Tooltip("SpritePixels: the subject's rect AT AUTHORING TIME — a DRIFT GUARD, so a resized sprite is " +
                 "detected (HasDrift) and healed (RescaleTo) instead of silently misplacing every mark. " +
                 "GridCells: the footprint bounds, informational only. Also the denominator that gives a " +
                 "coarser-than-subject mask its cell size (refSize / maskW).")]
        public Vector2Int refSize;

        [Tooltip("Map-space position of the subject footprint's LOWER-LEFT corner — and therefore of mask cell " +
                 "(0,0). Zero for a sprite (map space IS the rect, bottom-left origin). NON-ZERO whenever the " +
                 "subject's own origin is not the corner of its picture: a Cartographer clump anchors at its " +
                 "TOP-left cell (a tileset's rows run down, a level's run up), so its footprint runs from " +
                 "y = -(height-1) up to y = 1 and this reads (0, -(height-1)). Marks always handled negatives " +
                 "fine; masks could not until this existed, and painted silently in the wrong place.")]
        public Vector2 footprintMin;

        [Tooltip("1 = a static map (the ordinary case — a static map is simply the one-frame case, not a " +
                 "degenerate animation). N = aligned to the OWNER's frame sequence; the map does not own the " +
                 "sequence, it only indexes into it.")]
        public int frameCount = 1;

        [Tooltip("The layers. Order is presentation only — lookup is by id, case-insensitively.")]
        public List<MetaMapLayer> layers = new List<MetaMapLayer>();

        // ── identity / introspection ─────────────────────────────────────────────────
        // The cheapest and FIRST real consumer: "is this clump a loot shelf?" is HasLayer("LootShelf") — a layer
        // that EXISTS with nothing painted in it is already a useful answer. Keep this surface total and quiet.

        /// <summary>Every layer id, in authored order. Skips null/blank layers rather than reporting them.</summary>
        public IEnumerable<string> LayerIds
        {
            get
            {
                if (layers == null) yield break;
                for (int i = 0; i < layers.Count; i++)
                {
                    var L = layers[i];
                    if (L != null && !string.IsNullOrEmpty(L.id)) yield return L.id;
                }
            }
        }

        public int LayerCount => layers != null ? layers.Count : 0;

        /// <summary>The layer with this id (case-insensitive), or null. Never throws on a null/blank id.</summary>
        public MetaMapLayer GetLayer(string layerId)
        {
            if (layers == null || string.IsNullOrEmpty(layerId)) return null;
            for (int i = 0; i < layers.Count; i++)
            {
                var L = layers[i];
                if (L != null && string.Equals(L.id, layerId, StringComparison.OrdinalIgnoreCase)) return L;
            }
            return null;
        }

        public MetaMapLayer LayerAt(int index)
            => (layers != null && index >= 0 && index < layers.Count) ? layers[index] : null;

        /// <summary>"Does this subject claim this meaning at all?" — the identity question, independent of
        /// whether anything is painted in the layer yet.</summary>
        public bool HasLayer(string layerId) => GetLayer(layerId) != null;

        /// <summary>Identity plus a shape guarantee: the layer exists AND stores what the caller expects, so a
        /// consumer written against Points never trips over a Mask layer that reused the name.</summary>
        public bool HasLayer(string layerId, LayerKind kind)
        {
            var L = GetLayer(layerId);
            return L != null && L.kind == kind;
        }

        /// <summary>"Is anything spatial authored here, on THIS frame" — the gate an event-firing player uses
        /// (Launimator's FireMetaLayerReached). A param string alone is not content: it annotates geometry, it
        /// isn't geometry. False for a missing layer, an out-of-range frame, or an explicitly empty entry.</summary>
        public bool HasContent(string layerId, int frame = 0)
        {
            var e = EntryAt(GetLayer(layerId), frame);
            return e != null && !e.IsEmpty;
        }

        // ── layer management ─────────────────────────────────────────────────────────

        /// <summary>Create a layer with defaults that are never nonsense: a distinct palette colour, an empty
        /// entry ready to receive content, and the binding the Amendment requires — PerFrame on an ANIMATED map
        /// (frame-scoped is the safe default; Uniform is the deliberate "true on every frame" opt-in), Uniform
        /// on a static one, where the distinction is moot. The id is uniquified, so the caller can hand this
        /// straight to an "+ Add layer" button. Authoring anything visual is optional: the layer's EXISTENCE is
        /// already a queryable fact.</summary>
        public MetaMapLayer AddLayer(string layerId, LayerKind kind = LayerKind.Points)
        {
            if (layers == null) layers = new List<MetaMapLayer>();
            var L = new MetaMapLayer
            {
                id = UniqueLayerId(layerId),
                kind = kind,
                color = MetaPalette.Colors[Mathf.Abs(layers.Count) % MetaPalette.Colors.Length],
                binding = frameCount > 1 ? FrameBinding.PerFrame : FrameBinding.Uniform,
            };
            if (L.binding == FrameBinding.PerFrame) L.EnsureTrack(frameCount);
            layers.Add(L);
            return L;
        }

        /// <summary>The idempotent form: the existing layer if the id is taken (whatever its kind — the caller
        /// asked for this id, and silently making a second one would break the "id is the key" contract), else
        /// a fresh one from <see cref="AddLayer"/>.</summary>
        public MetaMapLayer GetOrAddLayer(string layerId, LayerKind kind = LayerKind.Points)
            => GetLayer(layerId) ?? AddLayer(layerId, kind);

        public bool RemoveLayer(string layerId)
        {
            var L = GetLayer(layerId);
            return L != null && layers.Remove(L);
        }

        /// <summary>A layer id not already in use (case-insensitively): "loot", "loot 2", "loot 3"… A blank
        /// basis becomes "layer" rather than an unnamed layer nothing can look up.</summary>
        public string UniqueLayerId(string basis)
        {
            string root = string.IsNullOrWhiteSpace(basis) ? "layer" : basis.Trim();
            if (GetLayer(root) == null) return root;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = root + " " + n;
                if (GetLayer(candidate) == null) return candidate;
            }
            return root + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        // ── frames ───────────────────────────────────────────────────────────────────

        /// <summary>Re-declare the frame count and pad every PerFrame track with EMPTY entries to match. Never
        /// trims (that would destroy authored frames on a momentary miscount) and never changes any layer's
        /// binding — promotion and collapse are authored choices, not side effects of resizing.</summary>
        public void SetFrameCount(int n)
        {
            frameCount = Mathf.Max(1, n);
            if (layers == null) return;
            for (int i = 0; i < layers.Count; i++)
            {
                var L = layers[i];
                if (L != null && L.binding == FrameBinding.PerFrame) L.EnsureTrack(frameCount);
            }
        }

        /// <summary>THE ONE ACCESSOR every mark/mask lookup goes through (§3.1's seam). Resolve the layer, then
        /// let the LAYER interpret its own binding. A third binding lands in exactly one place.</summary>
        public MetaEntry EntryAt(MetaMapLayer layer, int frame) => layer != null ? layer.EntryAt(frame) : null;

        /// <summary>Id-taking form of <see cref="EntryAt(MetaMapLayer,int)"/>.</summary>
        public MetaEntry EntryAt(string layerId, int frame = 0) => EntryAt(GetLayer(layerId), frame);

        // ── Points (and Direction positions) ─────────────────────────────────────────

        /// <summary>First mark of the layer — the one-mark ergonomics, for the common "the muzzle" case.
        /// Reads Points AND Directions layers: a Direction's position answers point queries too.</summary>
        public bool TryGetPoint(string layerId, out Vector2 pos, int frame = 0)
        {
            pos = default;
            var e = PointEntry(layerId, frame);
            if (e == null || e.marks == null) return false;
            for (int i = 0; i < e.marks.Count; i++)
                if (e.marks[i] != null) { pos = e.marks[i].pos; return true; }
            return false;
        }

        /// <summary>Every mark position, in AUTHORED ORDER (stable — seeded consumers depend on it). Fills the
        /// caller's list (cleared first) so a per-frame caller allocates nothing.</summary>
        public int GetPoints(string layerId, List<Vector2> results, int frame = 0)
        {
            if (results == null) return 0;
            results.Clear();
            var e = PointEntry(layerId, frame);
            if (e == null || e.marks == null) return 0;
            for (int i = 0; i < e.marks.Count; i++)
                if (e.marks[i] != null) results.Add(e.marks[i].pos);
            return results.Count;
        }

        /// <summary>The named individual (case-insensitive) — PropSpot's "landing-pad" shape, without a second
        /// mechanism.</summary>
        public bool TryGetPoint(string layerId, string markName, out Vector2 pos, int frame = 0)
        {
            pos = default;
            if (string.IsNullOrEmpty(markName)) return false;
            var e = PointEntry(layerId, frame);
            if (e == null || e.marks == null) return false;
            for (int i = 0; i < e.marks.Count; i++)
            {
                var m = e.marks[i];
                if (m != null && string.Equals(m.name, markName, StringComparison.OrdinalIgnoreCase))
                { pos = m.pos; return true; }
            }
            return false;
        }

        // ── Directions ───────────────────────────────────────────────────────────────

        /// <summary>First mark of a DIRECTIONS layer, position plus normalised facing. Strict about kind on
        /// purpose: a Points layer has no facing, and handing back (0,0) would be a silent lie.</summary>
        public bool TryGetDirection(string layerId, out Vector2 pos, out Vector2 dir, int frame = 0)
        {
            pos = default; dir = default;
            var e = KindEntry(layerId, LayerKind.Directions, frame);
            if (e == null || e.marks == null) return false;
            for (int i = 0; i < e.marks.Count; i++)
            {
                var m = e.marks[i];
                if (m == null) continue;
                pos = m.pos;
                dir = m.dir.sqrMagnitude > 0f ? m.dir.normalized : Vector2.zero;   // "normalised on use"
                return true;
            }
            return false;
        }

        /// <summary>Every (position, normalised facing) pair, authored order, into the caller's list.</summary>
        public int GetDirections(string layerId, List<(Vector2 pos, Vector2 dir)> results, int frame = 0)
        {
            if (results == null) return 0;
            results.Clear();
            var e = KindEntry(layerId, LayerKind.Directions, frame);
            if (e == null || e.marks == null) return 0;
            for (int i = 0; i < e.marks.Count; i++)
            {
                var m = e.marks[i];
                if (m == null) continue;
                results.Add((m.pos, m.dir.sqrMagnitude > 0f ? m.dir.normalized : Vector2.zero));
            }
            return results.Count;
        }

        // ── Masks ────────────────────────────────────────────────────────────────────

        /// <summary>Is this MAP-SPACE position inside a painted cell? Outputs the painted value (0..10). The
        /// core of Launimator's IsMetaPainted, minus the world transform (that is the binding's inverse).</summary>
        public bool MaskContains(string layerId, Vector2 pos, out int value, int frame = 0)
        {
            value = 0;
            var e = KindEntry(layerId, LayerKind.Mask, frame);
            if (e == null || !e.MaskUsable) return false;
            Vector2 cs = MaskCellToMapScale(e);
            if (cs.x <= 0f || cs.y <= 0f) return false;
            int cx = Mathf.FloorToInt((pos.x - footprintMin.x) / cs.x);
            int cy = Mathf.FloorToInt((pos.y - footprintMin.y) / cs.y);
            if (cx < 0 || cx >= e.maskW || cy < 0 || cy >= e.maskH) return false;
            value = e.mask[cy * e.maskW + cx];
            return value > 0;
        }

        /// <summary>Value-weighted centroid of the painted cells, in MAP SPACE, plus peak intensity (0..1).
        /// This is TryComputeCentroid's exact math — the engine under Launimator's TryGetMetaPoint — with the
        /// cell→map scaling applied at the end instead of inside the world conversion.</summary>
        public bool TryGetMaskCentroid(string layerId, out Vector2 centroid, out float strength01, int frame = 0)
        {
            centroid = default; strength01 = 0f;
            var e = KindEntry(layerId, LayerKind.Mask, frame);
            if (e == null || !e.MaskUsable) return false;

            double sx = 0, sy = 0, sw = 0; int peak = 0;
            for (int y = 0; y < e.maskH; y++)
                for (int x = 0; x < e.maskW; x++)
                {
                    int v = e.mask[y * e.maskW + x];
                    if (v <= 0) continue;
                    sx += (x + 0.5) * v; sy += (y + 0.5) * v; sw += v;
                    if (v > peak) peak = v;
                }
            if (sw <= 0) return false;

            Vector2 cs = MaskCellToMapScale(e);
            strength01 = Mathf.Clamp01(peak / 10f);
            centroid = footprintMin + new Vector2((float)(sx / sw) * cs.x, (float)(sy / sw) * cs.y);
            return true;
        }

        /// <summary>Bounding box of the painted cells in MAP SPACE. Each painted cell contributes its full
        /// extent (not its centre), so a single painted cell yields a one-cell rect, not a zero-size one.</summary>
        public bool TryGetMaskBounds(string layerId, out Rect bounds, int frame = 0)
        {
            bounds = default;
            var e = KindEntry(layerId, LayerKind.Mask, frame);
            if (e == null || !e.MaskUsable) return false;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int y = 0; y < e.maskH; y++)
                for (int x = 0; x < e.maskW; x++)
                {
                    if (e.mask[y * e.maskW + x] <= 0) continue;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            if (maxX < minX) return false;

            Vector2 cs = MaskCellToMapScale(e);
            bounds = new Rect(footprintMin.x + minX * cs.x, footprintMin.y + minY * cs.y,
                              (maxX - minX + 1) * cs.x, (maxY - minY + 1) * cs.y);
            return true;
        }

        /// <summary>A uniformly-random painted cell's CENTRE, in map space. System.Random in, never
        /// UnityEngine.Random: a caller that seeded its own rng gets a reproducible answer, which is the whole
        /// point (ShelfLoot's seed honesty). Intensity is ignored — every painted cell is equally likely.</summary>
        public bool TryGetRandomMaskPoint(string layerId, System.Random rng, out Vector2 pos, int frame = 0)
        {
            pos = default;
            if (rng == null) return false;
            var e = KindEntry(layerId, LayerKind.Mask, frame);
            if (e == null || !e.MaskUsable) return false;

            int painted = 0;
            int n = e.maskW * e.maskH;
            for (int i = 0; i < n; i++) if (e.mask[i] > 0) painted++;
            if (painted <= 0) return false;

            int pick = rng.Next(painted);
            for (int i = 0; i < n; i++)
            {
                if (e.mask[i] <= 0) continue;
                if (pick-- > 0) continue;
                pos = MaskCellCenterToMap(e, new Vector2Int(i % e.maskW, i / e.maskW));
                return true;
            }
            return false;
        }

        /// <summary>Connected components (4-connectivity) of the painted cells, so a consumer that needs two
        /// disjoint blobs kept apart never has to invent a second representation. Blobs come out in scan order
        /// (bottom-left first) and REUSE the buffer's existing MaskBlob instances.
        ///
        /// Computed on demand, NOT cached: every field on this model is public and mutable, so there is no
        /// honest moment to invalidate a cache. A profiled consumer earns a version stamp; none exists yet.</summary>
        public int GetMaskBlobs(string layerId, List<MaskBlob> results, int frame = 0)
        {
            if (results == null) return 0;
            int found = 0;
            var e = KindEntry(layerId, LayerKind.Mask, frame);
            if (e == null || !e.MaskUsable) { results.Clear(); return 0; }

            int w = e.maskW, h = e.maskH;
            var seen = new bool[w * h];
            var stack = new List<int>();
            Vector2 cs = MaskCellToMapScale(e);

            for (int start = 0; start < w * h; start++)
            {
                if (seen[start] || e.mask[start] <= 0) continue;

                var blob = Reusable(results, found);
                blob.Reset();
                double sx = 0, sy = 0, sw = 0;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

                stack.Clear(); stack.Add(start); seen[start] = true;
                while (stack.Count > 0)
                {
                    int idx = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    int x = idx % w, y = idx / w;
                    int v = e.mask[idx];

                    blob.cells.Add(new Vector2Int(x, y));
                    sx += (x + 0.5) * v; sy += (y + 0.5) * v; sw += v;
                    if (v > blob.peakValue) blob.peakValue = v;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;

                    if (x > 0)     Push(stack, seen, e.mask, idx - 1);
                    if (x < w - 1) Push(stack, seen, e.mask, idx + 1);
                    if (y > 0)     Push(stack, seen, e.mask, idx - w);
                    if (y < h - 1) Push(stack, seen, e.mask, idx + w);
                }

                blob.cells.Sort(CellScanOrder);   // flood-fill visit order is arbitrary; publish a stable one
                blob.centroid = footprintMin + new Vector2((float)(sx / sw) * cs.x, (float)(sy / sw) * cs.y);
                blob.bounds = new Rect(footprintMin.x + minX * cs.x, footprintMin.y + minY * cs.y,
                                       (maxX - minX + 1) * cs.x, (maxY - minY + 1) * cs.y);
                found++;
            }

            while (results.Count > found) results.RemoveAt(results.Count - 1);
            return found;
        }

        static void Push(List<int> stack, bool[] seen, byte[] mask, int idx)
        {
            if (seen[idx] || mask[idx] <= 0) return;
            seen[idx] = true;
            stack.Add(idx);
        }

        static MaskBlob Reusable(List<MaskBlob> results, int index)
        {
            while (results.Count <= index) results.Add(new MaskBlob());
            if (results[index] == null) results[index] = new MaskBlob();
            return results[index];
        }

        static readonly Comparison<Vector2Int> CellScanOrder =
            (a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);

        /// <summary>Every painted cell as (MASK-GRID coord, value) — the feeder for debug quads and for
        /// pixel-accurate overlap. Cells are NOT map space (the grid may be coarser); convert them with
        /// <see cref="MaskCellCenterToMap"/> or scale corners by <see cref="MaskCellToMapScale"/>.</summary>
        public int GetMaskCells(string layerId, List<(Vector2Int cell, byte value)> results, int frame = 0)
        {
            if (results == null) return 0;
            results.Clear();
            var e = KindEntry(layerId, LayerKind.Mask, frame);
            if (e == null || !e.MaskUsable) return 0;
            for (int y = 0; y < e.maskH; y++)
                for (int x = 0; x < e.maskW; x++)
                {
                    byte v = e.mask[y * e.maskW + x];
                    if (v > 0) results.Add((new Vector2Int(x, y), v));
                }
            return results.Count;
        }

        /// <summary>Size of ONE mask cell in map units. refSize / maskSize, because a mask may be COARSER than
        /// the subject (Launimator's gridW/gridH generalisation). Falls back to 1:1 when refSize was never
        /// recorded — which is also exactly right for a GridCells mask, where one byte IS one cell.</summary>
        public Vector2 MaskCellToMapScale(MetaEntry entry)
        {
            if (entry == null) return Vector2.one;
            float sx = (refSize.x > 0 && entry.maskW > 0) ? (float)refSize.x / entry.maskW : 1f;
            float sy = (refSize.y > 0 && entry.maskH > 0) ? (float)refSize.y / entry.maskH : 1f;
            return new Vector2(sx, sy);
        }

        /// <summary>Centre of a mask cell in MAP SPACE. Mask cell (0,0) sits at <see cref="footprintMin"/> — the
        /// subject footprint's lower-left corner — so a subject whose footprint runs into NEGATIVE map
        /// coordinates (a clump, which anchors at its top-left cell) paints and queries in the right place.</summary>
        public Vector2 MaskCellCenterToMap(MetaEntry entry, Vector2Int cell)
        {
            Vector2 cs = MaskCellToMapScale(entry);
            return footprintMin + new Vector2((cell.x + 0.5f) * cs.x, (cell.y + 0.5f) * cs.y);
        }

        /// <summary>The map-space box the subject's own picture occupies: <see cref="footprintMin"/> plus
        /// <see cref="refSize"/>. Zero-origin for a sprite; shifted down for a clump. Falls back to a unit box
        /// so a caller dividing by its size never hits zero.</summary>
        public Rect Footprint => new Rect(footprintMin.x, footprintMin.y,
            Mathf.Max(1, refSize.x), Mathf.Max(1, refSize.y));

        // ── param ────────────────────────────────────────────────────────────────────

        /// <summary>This frame's free-text param for the layer, or "" when there is none. Never null, never
        /// throws — a caller comparing strings should not have to guard first.</summary>
        public string GetParam(string layerId, int frame = 0)
        {
            var e = EntryAt(GetLayer(layerId), frame);
            return (e != null && e.param != null) ? e.param : "";
        }

        // ── drift, healing, cloning ──────────────────────────────────────────────────

        /// <summary>Has the subject changed SIZE under an authored map? SpritePixels only — see the two-argument
        /// overload for the general question, which a GridCells subject needs because its footprint can move
        /// without resizing and resize without moving. False when no refSize was ever recorded: there is nothing
        /// to have drifted FROM.</summary>
        public bool HasDrift(Vector2Int subjectSize)
        {
            if (space != MapSpace.SpritePixels) return false;
            if (refSize.x <= 0 || refSize.y <= 0) return false;
            return refSize != subjectSize;
        }

        /// <summary>THE GENERAL DRIFT QUESTION, valid in BOTH spaces: does the subject still occupy the map-space
        /// box this map was authored against? A GridCells subject drifts two ways and a size check catches only
        /// one of them — a Cartographer clump that grows a row on TOP keeps every authored number and moves the
        /// ART, and its <see cref="footprintMin"/> moves with it. Comparing the whole footprint (anchor AND size)
        /// is what makes that detectable at all.
        ///
        /// Detection only. It cannot say which WAY the art moved — that needs the subject's own before/after
        /// geometry, which only the host has — so the host supplies the delta and the fix is
        /// <see cref="Reanchor"/>. False when nothing was ever recorded to drift from.</summary>
        public bool HasDrift(Vector2Int subjectSize, Vector2 subjectFootprintMin)
        {
            if (refSize.x <= 0 || refSize.y <= 0) return false;
            return refSize != subjectSize || footprintMin != subjectFootprintMin;
        }

        /// <summary>HEAL DRIFT BY MOVING THE MAP WITH THE ART — the answer to "the shelf grew a row on top, keep
        /// my anchors on the same boards". <paramref name="artShift"/> is how far the subject's EXISTING art
        /// moved in map space (the host computes it from its own before/after geometry); every mark travels by
        /// it, and every mask cell is re-gridded so the cell that described a piece of art still describes it.
        ///
        /// This is the OPPOSITE of <see cref="RescaleTo"/>: nothing is stretched or resampled, because the art
        /// did not change size — it changed POSITION inside a bigger (or smaller) footprint. Content that falls
        /// outside the new footprint is dropped from the mask and left where it is for marks (a mark outside the
        /// footprint is still an authored number, and silently deleting it would be worse than showing it).</summary>
        public void Reanchor(Vector2Int newRefSize, Vector2 newFootprintMin, Vector2 artShift)
            => Regrid(newRefSize, newFootprintMin, artShift, moveMarks: true);

        /// <summary>ACCEPT THE NEW FOOTPRINT AS AUTHORED — the "I re-authored the geometry, my numbers are
        /// already right" answer. EVERY authored MAP POSITION is preserved: marks keep their coordinates
        /// exactly, and painted cells keep the map positions they occupy (their INDICES shift instead, because
        /// the footprint anchor they are counted from moved). Marks and masks therefore stay in step with each
        /// other, which is the only reading of "the numbers stay as they are" that does not silently mean two
        /// different things for the two kinds. The ART may well end up somewhere else relative to them; that is
        /// precisely the author's stated intent, and <see cref="Reanchor"/> is the other answer.</summary>
        public void AdoptFootprint(Vector2Int newRefSize, Vector2 newFootprintMin)
            => Regrid(newRefSize, newFootprintMin, Vector2.zero, moveMarks: false);

        /// <summary>The shared body of <see cref="Reanchor"/> / <see cref="AdoptFootprint"/>. The mask index
        /// shift falls straight out of the two anchors: old mask cell (i,j)'s lower-left corner sits at
        /// <c>footprintMin + (i,j)·cellSize</c>; after the move that art is at <c>+ artShift</c>; its new index
        /// is that, minus the new anchor, over the cell size. Everything else is bookkeeping.</summary>
        void Regrid(Vector2Int newRefSize, Vector2 newFootprintMin, Vector2 artShift, bool moveMarks)
        {
            newRefSize.x = Mathf.Max(0, newRefSize.x);
            newRefSize.y = Mathf.Max(0, newRefSize.y);

            if (layers != null)
                for (int i = 0; i < layers.Count; i++)
                {
                    var L = layers[i];
                    if (L == null) continue;
                    RegridEntry(L.uniform, newRefSize, newFootprintMin, artShift, moveMarks);
                    if (L.track != null)
                        for (int f = 0; f < L.track.Count; f++)
                            RegridEntry(L.track[f], newRefSize, newFootprintMin, artShift, moveMarks);
                }

            refSize = newRefSize;
            footprintMin = newFootprintMin;
        }

        void RegridEntry(MetaEntry e, Vector2Int newRefSize, Vector2 newFootprintMin, Vector2 artShift,
            bool moveMarks)
        {
            if (e == null) return;

            if (moveMarks && e.marks != null && artShift != Vector2.zero)
                for (int i = 0; i < e.marks.Count; i++)
                {
                    var m = e.marks[i];
                    if (m != null) m.pos += artShift;
                }

            if (!e.MaskUsable) return;

            // Keep the mask's CELL SIZE (it may legitimately be coarser than the subject) and let the grid grow
            // or shrink with the footprint around it.
            Vector2 cs = MaskCellToMapScale(e);
            if (cs.x <= 0f || cs.y <= 0f) return;
            int nw = Mathf.Max(1, Mathf.RoundToInt(newRefSize.x / cs.x));
            int nh = Mathf.Max(1, Mathf.RoundToInt(newRefSize.y / cs.y));
            int sx = Mathf.RoundToInt((footprintMin.x + artShift.x - newFootprintMin.x) / cs.x);
            int sy = Mathf.RoundToInt((footprintMin.y + artShift.y - newFootprintMin.y) / cs.y);
            if (nw == e.maskW && nh == e.maskH && sx == 0 && sy == 0) return;

            var moved = new byte[nw * nh];
            for (int y = 0; y < e.maskH; y++)
                for (int x = 0; x < e.maskW; x++)
                {
                    byte v = e.mask[y * e.maskW + x];
                    if (v == 0) continue;
                    int nx = x + sx, ny = y + sy;
                    if (nx < 0 || ny < 0 || nx >= nw || ny >= nh) continue;   // fell outside the new footprint
                    moved[ny * nw + nx] = v;
                }
            e.maskW = nw; e.maskH = nh; e.mask = moved;
        }

        /// <summary>Heal drift: rescale every mark PROPORTIONALLY and resample every mask by nearest neighbour
        /// (MetaFrame.EnsureSize's behaviour, promoted to the model), then adopt the new refSize. Facings are
        /// left alone — a direction is a semantic heading, not a length. Applies to Uniform and PerFrame
        /// entries alike, so nothing survives a resize in the old scale.</summary>
        public void RescaleTo(Vector2Int newRefSize)
        {
            newRefSize.x = Mathf.Max(0, newRefSize.x);
            newRefSize.y = Mathf.Max(0, newRefSize.y);
            if (newRefSize == refSize) return;

            float sx = refSize.x > 0 ? (float)newRefSize.x / refSize.x : 1f;
            float sy = refSize.y > 0 ? (float)newRefSize.y / refSize.y : 1f;

            if (layers != null)
                for (int i = 0; i < layers.Count; i++)
                {
                    var L = layers[i];
                    if (L == null) continue;
                    RescaleEntry(L.uniform, sx, sy);
                    if (L.track != null)
                        for (int f = 0; f < L.track.Count; f++) RescaleEntry(L.track[f], sx, sy);
                }

            refSize = newRefSize;
        }

        static void RescaleEntry(MetaEntry e, float sx, float sy)
        {
            if (e == null) return;
            if (e.marks != null)
                for (int i = 0; i < e.marks.Count; i++)
                {
                    var m = e.marks[i];
                    if (m != null) m.pos = new Vector2(m.pos.x * sx, m.pos.y * sy);
                }
            if (e.maskW > 0 && e.maskH > 0)
                e.EnsureMaskSize(Mathf.Max(1, Mathf.RoundToInt(e.maskW * sx)),
                                 Mathf.Max(1, Mathf.RoundToInt(e.maskH * sy)));
        }

        /// <summary>Heal a deserialized map into a usable one — NEVER throws, never refuses to load. The
        /// RegionSlicer sidecar's discipline ("a Normalize pass that heals rather than crashes"), promoted to a
        /// model behaviour: null lists, null layers, blank ids, invisible colours, truncated mask arrays and
        /// out-of-range values are all repaired in place. Bindings and authored geometry are NOT touched.</summary>
        public void Normalize()
        {
            if (frameCount < 1) frameCount = 1;
            if (refSize.x < 0) refSize.x = 0;
            if (refSize.y < 0) refSize.y = 0;
            if (layers == null) { layers = new List<MetaMapLayer>(); return; }
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                if (layers[i] == null) { layers.RemoveAt(i); continue; }
                layers[i].Normalize();
            }
        }

        /// <summary>A DEEP copy — every layer, entry, mark and mask array is its own. Hosts snapshot with this
        /// for undo stacks; mutating a clone can never reach back into the original.</summary>
        public MetaMapData Clone()
        {
            var c = new MetaMapData
            {
                space = space,
                refSize = refSize,
                footprintMin = footprintMin,
                frameCount = frameCount,
                layers = new List<MetaMapLayer>(layers != null ? layers.Count : 0),
            };
            if (layers != null)
                for (int i = 0; i < layers.Count; i++)
                    if (layers[i] != null) c.layers.Add(layers[i].Clone());
            return c;
        }

        // ── internals ────────────────────────────────────────────────────────────────

        /// <summary>Entry for a point-ish query: Points OR Directions (§3.3 — "the position answers point
        /// queries too"). A Mask layer has no marks and correctly answers nothing.</summary>
        MetaEntry PointEntry(string layerId, int frame)
        {
            var L = GetLayer(layerId);
            if (L == null || (L.kind != LayerKind.Points && L.kind != LayerKind.Directions)) return null;
            return EntryAt(L, frame);
        }

        MetaEntry KindEntry(string layerId, LayerKind kind, int frame)
        {
            var L = GetLayer(layerId);
            if (L == null || L.kind != kind) return null;
            return EntryAt(L, frame);
        }
    }
}
