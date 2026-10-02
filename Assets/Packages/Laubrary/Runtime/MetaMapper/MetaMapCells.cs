using UnityEngine;

namespace Laubrary.MetaMapper
{
    /// <summary>
    /// The GridCells binding: map space (fractional cells from the subject's own origin) → world, for a subject
    /// STAMPED into a grid at some cell, rotation and mirror. The model is pure data and placement-blind; this
    /// helper owns all of the world math for grid subjects.
    /// </summary>
    public static class MetaMapCells
    {
        /// <summary>
        /// Transform a subject-local fractional cell offset by <paramref name="rotation"/> quarter-turns
        /// ANTICLOCKWISE and an optional X mirror. MIRROR IS APPLIED FIRST, so a mirrored-then-rotated stamp
        /// matches what the preview drew.
        ///
        /// TWIN: <c>Prop.TransformOffset</c> in <c>Runtime/Cartographer/Prop.cs</c> — the canonical copy for
        /// placement, resolution and the stamper. These four lines are DUPLICATED, not shared, because
        /// MetaMapper references nothing (Cartographer references MetaMapper, never the reverse). If one
        /// changes, change the other; a placement disagreeing with a map is a bug that looks like bad authoring.
        /// </summary>
        public static Vector2 TransformOffset(Vector2 offset, int rotation, bool mirrorX)
        {
            if (mirrorX) offset.x = -offset.x;
            int turns = ((rotation % 4) + 4) % 4;
            for (int i = 0; i < turns; i++) offset = new Vector2(-offset.y, offset.x);
            return offset;
        }

        /// <summary>
        /// Transform a CONTINUOUS map position the way the placement transformed the CELLS under it.
        ///
        /// ⚠️ Not the same thing as <see cref="TransformOffset"/> on the raw position, and the difference is a
        /// whole cell. A cell INDEX c covers the interval [c, c+1); mirroring the index gives −c, which covers
        /// [−c, −c+1), but the true mirror of [c, c+1) is [−c−1, −c). So mirroring/rotating a position directly
        /// moves it one cell away from the tile it was authored on. Shifting into CELL-CENTRE space first and
        /// back makes the two agree exactly, for every rotation × mirror:
        /// <code>  TransformCellPoint(c + 0.5) == TransformOffset(c) + 0.5  </code>
        /// which is the very rule <c>LevelInstance.PublishSpots</c> already applies to PropSpots ("+0.5 puts the
        /// spot at the centre of its cell"). At rotation 0 with no mirror this is the identity, so an unrotated
        /// stamp — every stamp the Cartographer places today — is unaffected.
        /// </summary>
        public static Vector2 TransformCellPoint(Vector2 mapPos, int rotation, bool mirrorX)
        {
            var half = new Vector2(0.5f, 0.5f);
            return TransformOffset(mapPos - half, rotation, mirrorX) + half;
        }

        /// <summary>
        /// Map position → world for a subject stamped at <paramref name="placementCell"/>.
        ///
        /// NOTE the difference from PropSpot: a PropSpot offset is a whole-cell offset that placement code
        /// re-centres with +0.5, whereas a map position already CARRIES its intra-cell fraction — a mark
        /// authored on the anchor cell's centre reads back as (0.5, 0.5). So nothing is added here; the fraction
        /// the author placed is the fraction that lands. The quarter-turn/mirror goes through
        /// <see cref="TransformCellPoint"/> precisely so that fraction keeps landing on the same TILE.
        ///
        /// Grid conversion goes through CellToLocalInterpolated + LocalToWorld (the same path
        /// <c>CartographerLevel</c>/<c>LevelInstance</c> use), so fractional positions, cell gaps, cell swizzle
        /// and isometric layouts are all honoured rather than approximated by cellSize multiplication. A null
        /// grid degrades to "one cell == one world unit", which is what an un-gridded caller means.
        /// </summary>
        public static Vector3 ToWorld(Vector2Int placementCell, int rotation, bool mirrorX, Grid grid, Vector2 mapPos)
        {
            Vector2 t = TransformCellPoint(mapPos, rotation, mirrorX);
            Vector3 cell = new Vector3(placementCell.x + t.x, placementCell.y + t.y, 0f);
            return grid != null ? grid.LocalToWorld(grid.CellToLocalInterpolated(cell)) : cell;
        }

        /// <summary>
        /// A GridCells shape → world for a subject stamped at <paramref name="placementCell"/>. The centre goes
        /// through <see cref="ToWorld"/> — the very transform marks use, so a footprint drawn under a mark stays
        /// under it on every rotation and mirror. The extent is the shape's box with its width and height
        /// SWAPPED on an odd number of quarter-turns (a mirror changes no size), then measured in world units
        /// along the grid's own axes, so cell size, gap and the grid's scale are honoured.
        ///
        /// Sizes assume a rectangular grid: an isometric layout has no axis-aligned box to report, and a
        /// circle's diameter is measured along the grid's x axis.
        /// </summary>
        public static MetaWorldShape ShapeToWorld(MetaShape shape, Vector2Int placementCell, int rotation,
            bool mirrorX, Grid grid)
        {
            if (shape == null) return default;
            Vector2 size = shape.size;
            int turns = ((rotation % 4) + 4) % 4;
            if ((turns & 1) == 1) size = new Vector2(size.y, size.x);
            if (shape.kind == ShapeKind.Circle) size = new Vector2(shape.size.x, shape.size.x);

            Vector2 cellWorld = Vector2.one;
            if (grid != null)
            {
                Vector3 o = grid.LocalToWorld(grid.CellToLocalInterpolated(Vector3.zero));
                cellWorld = new Vector2(
                    (grid.LocalToWorld(grid.CellToLocalInterpolated(Vector3.right)) - o).magnitude,
                    (grid.LocalToWorld(grid.CellToLocalInterpolated(Vector3.up)) - o).magnitude);
            }
            var worldSize = new Vector2(size.x * cellWorld.x, size.y * cellWorld.y);
            if (shape.kind == ShapeKind.Circle) worldSize.y = worldSize.x;

            return new MetaWorldShape
            {
                name = shape.name ?? "",
                kind = shape.kind,
                center = ToWorld(placementCell, rotation, mirrorX, grid, shape.center),
                size = worldSize,
            };
        }

        /// <summary>A facing → world, quarter-turned and mirrored with the placement but not translated. Taken
        /// as the difference of two grid conversions so a non-square or isometric grid skews the heading the
        /// same way it skews the geometry.</summary>
        public static Vector3 ToWorldDirection(int rotation, bool mirrorX, Grid grid, Vector2 dir)
        {
            Vector2 t = TransformOffset(dir, rotation, mirrorX);
            Vector3 local = new Vector3(t.x, t.y, 0f);
            if (grid != null)
            {
                Vector3 origin = grid.CellToLocalInterpolated(Vector3.zero);
                local = grid.CellToLocalInterpolated(local) - origin;
                local = grid.transform.TransformDirection(local);
            }
            return local.sqrMagnitude > 0f ? local.normalized : Vector3.zero;
        }
    }
}
