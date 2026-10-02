using System;
using System.Collections.Generic;
using Laubrary.MetaMapper;
using Laubrary.MetaMapper.Editor;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Cartographer.Editor
{
    /// <summary>
    /// CARTOGRAPHER → METAMAPPER: the one file allowed to know both, and the reason a clump can be the
    /// SUBJECT of a meta map at all. MetaMapper references nothing (that is what lets every tool reference
    /// IT), so it cannot name <see cref="Tileset"/> or <see cref="Clump"/>; this registers a resolver with
    /// <see cref="MetaSubjectProviders"/> at load and the generic window draws clump art without ever
    /// learning what a clump is (design §7's worked example, built).
    ///
    /// THE COORDINATE CONTRACT, and the one thing to get right — <see cref="Describe"/> and
    /// <see cref="Compose"/> must agree with <c>LevelAsset.ClumpCellsOf</c>, which is the canonical stamp
    /// geometry every consumer resolves through:
    /// <code>  level-local = (x − bounds.xMin, −(y − bounds.yMin))  </code>
    /// A TILESET's rows run DOWN the screen; a LEVEL's run UP. So the clump's anchor — the cell a placement
    /// records — is the footprint's TOP-LEFT cell, its map space runs x ∈ [0, width] and
    /// y ∈ [−(height−1), 1], and the picture handed over must be composed FLIPPED to match, or a dot placed
    /// on the top board reads back as the bottom one.
    ///
    /// Two consequences worth stating, because both contradict a first reading of METAMAPPER_DESIGN.md §7:
    ///   • <c>originPx.x</c> is ZERO, not <c>−bounds.xMin × cellPx</c>. §7's worked example assumed map space
    ///     was the clump's RAW authored offsets (which really can be negative); the stamp geometry normalises
    ///     x by <c>bounds.xMin</c> before it ever reaches a level, so map x always starts at 0.
    ///   • <c>originPx.y</c> is <c>(height − 1) × cellPx</c> — the flip, which §7 does not mention at all.
    /// The probe in the acceptance test asserts both numerically rather than trusting this comment.
    /// </summary>
    [InitializeOnLoad]
    public static class ClumpMetaSubject
    {
        static ClumpMetaSubject()
        {
            MetaSubjectProviders.Register(MetaSubjectKind.Clump, ResolveSubject);
            MetaSubjectProviders.RegisterSession(MetaSubjectKind.Clump, ResolveSession);
        }

        /// <summary>A clump's metadata is EMBEDDED (design §3): the data is the clump's own field and the
        /// undo target is the TILESET, because that is the real Object it lives on. Resolving it from a
        /// serializable ref rather than handing over a live session is what lets an edit survive a domain
        /// reload — and what lets an undo hand back the freshly-deserialized instance.</summary>
        static MetaEditSession ResolveSession(MetaSubjectRef r)
        {
            var set = r?.subject as Tileset;
            var clump = ClumpOf(set, r?.key);
            if (clump == null) return null;
            if (clump.meta == null) clump.meta = new MetaMapData();
            // The contract, restated only where restating it is safe (see Describe). Dirtied but NOT recorded
            // for undo: this writes bookkeeping the author never authored, and an undo entry for it would be
            // noise at best and would drop a freshly-minted drift baseline at worst.
            if (Describe(clump.meta, clump)) EditorUtility.SetDirty(set);
            return new MetaEditSession { data = clump.meta, undoTarget = set };
        }

        /// <summary>THE KEY CONVENTION, in one place: a clump is named to MetaMapper by its STABLE ID, never by
        /// its display name. Two clumps may legitimately both be called "Shelf" — resolving metadata by name
        /// meant opening one of them edited the other's, which is what the old "your clump has been renamed for
        /// you" dialog existed to paper over. Naming by id removes the ambiguity instead of the duplicate.
        ///
        /// Minting is silent (no undo entry, no dialog, no rename): an id is bookkeeping the author never chose
        /// and can never meaningfully revert. The tileset is dirtied so it persists.</summary>
        public static MetaSubjectRef RefFor(Tileset set, Clump clump)
        {
            if (clump != null && !clump.HasId)
            {
                clump.EnsureId();
                if (set != null) EditorUtility.SetDirty(set);
            }
            return new MetaSubjectRef
            {
                kind = MetaSubjectKind.Clump,
                subject = set,
                key = clump != null ? clump.id : "",
            };
        }

        /// <summary>Resolve a subject key back to a clump: by id, and — only when that finds nothing — by name.
        /// The name path exists for ONE thing, a window whose serialized subject ref was written before ids
        /// existed, so that a domain reload after this change does not dump the author out of their edit. It is
        /// ambiguous by nature (first match wins), which is exactly why nothing else uses it.</summary>
        static Clump ClumpOf(Tileset set, string key)
        {
            if (set == null || string.IsNullOrEmpty(key)) return null;
            return set.GetClumpById(key) ?? set.GetClump(key);
        }

        /// <summary>Nothing to call — touching the type runs the static ctor. Exists so a host that wants the
        /// provider guaranteed present before it pushes a visual can say so out loud.</summary>
        public static void EnsureRegistered() { }

        static MetaSubjectVisual ResolveSubject(MetaSubjectRef r)
        {
            var clump = ClumpOf(r?.subject as Tileset, r?.key);
            // Null, never an exception: a deleted clump means "I cannot resolve THIS one", and the window
            // falls back to a blank canvas so authoring identity is never blocked by missing art.
            return clump == null ? null : VisualFor(clump);
        }

        // ── the visual-provider contract (§7) ───────────────────────────────────────

        /// <summary>The clump as ONE picture plus an honest description of where map (0,0) sits inside it.
        /// Null when the clump has no drawable cell at all.</summary>
        public static MetaSubjectVisual VisualFor(Clump clump)
        {
            if (clump == null) return null;
            var tex = Compose(clump, out int cellPx);
            if (tex == null) return null;
            var b = clump.Bounds;
            return new MetaSubjectVisual
            {
                frames = new[] { tex },
                ownsFrames = true,
                fps = 0f,
                // map (0,0) is the anchor cell's LOWER-LEFT corner. The anchor is the footprint's TOP-left
                // cell (see the class note), so in a picture counted from its bottom-left that corner sits
                // one row's worth below the top: (height − 1) cells up.
                originPx = new Vector2(0f, (b.height - 1) * cellPx),
                pixelsPerUnit = cellPx,
                space = MapSpace.GridCells,
                refSize = new Vector2Int(b.width, b.height),
                label = clump.displayName,
                drift = DriftOf(clump),
            };
        }

        // ── footprint drift: detected, described, never silently applied ─────────────

        /// <summary>Map-space footprint anchor of a clump of this height. A tileset's rows run DOWN and a
        /// level's run UP, so the anchor cell is the footprint's TOP-left one and the map runs into negative y.
        /// One expression, used by the contract and the drift maths alike so they cannot disagree.</summary>
        static Vector2 FootprintMinOf(RectInt bounds) => new Vector2(0f, -(bounds.height - 1));

        /// <summary>Has this clump changed shape since its metadata was authored, and if so, HOW — the half of
        /// the answer only Cartographer can give.
        ///
        /// THE MATHS, which is the whole reason this is not a size comparison. A member at tileset offset
        /// (x, y) lands at map (x − bounds.xMin, −(y − bounds.yMin)) — <c>LevelAsset.ClumpCellsOf</c>'s
        /// canonical geometry. So when the bounds change, the EXISTING art moves by
        /// <c>(oldMin.x − newMin.x, newMin.y − oldMin.y)</c>: growing on the left or the top moves it, growing
        /// on the right or the bottom does not. A clump that gained one row is 3×5 either way, which is exactly
        /// why <see cref="MetaMapData.HasDrift(Vector2Int,Vector2)"/> can only say THAT it drifted.</summary>
        public static MetaSubjectDrift DriftOf(Clump clump)
        {
            if (clump == null) return null;
            var was = clump.metaFootprint;
            if (was.width <= 0 || was.height <= 0) return null;   // never recorded: nothing to have drifted from
            var now = clump.Bounds;
            if (was.xMin == now.xMin && was.yMin == now.yMin && was.size == now.size) return null;

            return new MetaSubjectDrift
            {
                artShift = new Vector2(was.xMin - now.xMin, now.yMin - was.yMin),
                newRefSize = new Vector2Int(now.width, now.height),
                newFootprintMin = FootprintMinOf(now),
                what = DescribeChange(was, now),
                onResolved = () => clump.metaFootprint = clump.Bounds,
            };
        }

        /// <summary>The change in the author's own vocabulary — "grew 1 row on top, 2 columns right" — because
        /// the generic window cannot phrase this without knowing what a clump is.</summary>
        static string DescribeChange(RectInt was, RectInt now)
        {
            var grew = new List<string>();
            var lost = new List<string>();
            void Side(int delta, string unit, string where)
            {
                if (delta > 0) grew.Add($"{delta} {unit}{(delta > 1 ? "s" : "")} {where}");
                else if (delta < 0) lost.Add($"{-delta} {unit}{(delta < -1 ? "s" : "")} {where}");
            }
            Side(was.xMin - now.xMin, "column", "left");
            Side(now.xMax - was.xMax, "column", "right");
            Side(was.yMin - now.yMin, "row", "on top");      // tileset rows run DOWN: a smaller yMin is higher
            Side(now.yMax - was.yMax, "row", "at the bottom");

            var parts = new List<string>();
            if (grew.Count > 0) parts.Add("grew " + string.Join(", ", grew));
            if (lost.Count > 0) parts.Add("lost " + string.Join(", ", lost));
            if (parts.Count == 0)
                return $"The clump moved within the grid ({was.width}×{was.height} → {now.width}×{now.height}).";
            return "The clump " + string.Join(" and ", parts) + $" ({was.width}×{was.height} → {now.width}×{now.height}).";
        }

        /// <summary>Write the coordinate contract onto a map's data — and ONLY where writing it is safe.
        ///
        /// The contract (space, footprint size, footprint anchor) is derived from the clump's geometry, so
        /// restating it over an UNAUTHORED map costs nothing. Over an AUTHORED one it is destructive: adopting
        /// a new anchor while the marks stay put is precisely "the author added a row and their anchors quietly
        /// moved to different boards". So an authored map is left exactly as it is and the disagreement is
        /// reported as drift instead (<see cref="DriftOf"/>), for the author to resolve.
        ///
        /// Also the silent migration: a clump authored before <c>metaFootprint</c> existed adopts its CURRENT
        /// bounds as the authored one. That is the only honest guess available (the old bounds were never
        /// recorded), it produces no drift, and it costs nothing — the very next change is detected properly.</summary>
        /// <returns>True when it actually wrote something, so the caller knows to dirty the tileset.</returns>
        public static bool Describe(MetaMapData d, Clump clump)
        {
            if (d == null || clump == null) return false;
            var b = clump.Bounds;

            if (d.LayerCount == 0)
            {
                var min = FootprintMinOf(b);
                var size = new Vector2Int(b.width, b.height);
                bool changed = d.space != MapSpace.GridCells || d.refSize != size
                            || d.footprintMin != min || !clump.metaFootprint.Equals(b);
                d.space = MapSpace.GridCells;
                d.refSize = size;
                d.footprintMin = min;
                clump.metaFootprint = b;
                return changed;
            }

            // Authored. Touch nothing except the never-recorded drift baseline.
            if (clump.metaFootprint.width > 0 && clump.metaFootprint.height > 0) return false;
            clump.metaFootprint = b;
            return true;
        }

        /// <summary>The clump's tiles composed into one texture, FLIPPED to level orientation (row 0 of the
        /// picture is the clump's BOTTOM row as a level renders it). Cells route by layerShift so an overhang
        /// draws over what it overhangs, exactly as the level will render it.
        ///
        /// Goes through <see cref="CartographerPreview"/> — the package's single tile-to-pixels path — rather
        /// than growing a third private copy of the same blitter.</summary>
        public static Texture2D Compose(Clump clump, out int cellPx)
        {
            cellPx = 0;
            if (clump?.cells == null || clump.cells.Count == 0) return null;

            // A clump may legitimately mix cell sizes; the canvas must fit the LARGEST or bigger tiles overrun.
            var drawable = new List<ClumpCell>();
            foreach (var c in clump.cells)
            {
                if (c?.tile == null) continue;
                var s = SpriteOf(c);
                if (s == null) continue;
                cellPx = Mathf.Max(cellPx, CartographerPreview.CellPixels(s));
                drawable.Add(c);
            }
            if (drawable.Count == 0) return null;
            if (cellPx <= 0) cellPx = 16;

            var b = clump.Bounds;
            int w = b.width * cellPx, h = b.height * cellPx;
            if (w <= 0 || h <= 0 || w > CartographerPreview.MaxSide || h > CartographerPreview.MaxSide) return null;

            // Back to front: a cell marked "one layer in front" must draw over the cell it overhangs, or the
            // author aims at art the level will cover up.
            drawable.Sort((a, c) => a.layerShift.CompareTo(c.layerShift));

            var tex = CartographerPreview.NewCanvas(w, h);
            tex.hideFlags = HideFlags.HideAndDontSave;
            foreach (var c in drawable)
            {
                var s = SpriteOf(c);
                if (s == null) continue;
                // THE FLIP, in the one place it can be checked against ClumpCellsOf's −(y − yMin).
                int x = (c.offset.x - b.xMin) * cellPx;
                int y = (b.yMin + b.height - 1 - c.offset.y) * cellPx;
                CartographerPreview.Blit(tex, s, x, y, cellPx);
            }
            tex.Apply();
            return tex;
        }

        static Sprite SpriteOf(ClumpCell c)
            => c?.tile == null ? null : (c.tile.IsAnimated ? c.tile.animation[0] : c.tile.SpriteOfVariant(0));

        // ── the one-click authoring path ────────────────────────────────────────────

        /// <summary>Open MetaMapper on this clump's OWN metadata, drawn on the clump's OWN art. There is no
        /// asset to create, name, choose or find: the data is a field on the clump, which is a field on the
        /// tileset. One click from "I have a clump" to "I am authoring its metadata".
        ///
        /// <paramref name="onChanged"/> lets the host repaint whatever of its own shows this clump (the
        /// grid's identity badge) while the author works in the other window.</summary>
        public static void Open(Tileset set, Clump clump, Action onChanged = null)
        {
            if (set == null || clump == null) return;
            MetaMapperWindow.OpenSubject(RefFor(set, clump), onChanged);
        }
    }
}
