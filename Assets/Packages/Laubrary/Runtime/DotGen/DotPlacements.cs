// DotPlacements.cs
// The three placement methods. Each one turns a generator area into local dots, and Grid and Box Row also
// give every dot a CELL — the rectangle around it that a child can be sized to and a fill drawer can paint.
// Radial Grid deliberately does not, which is why "Placement cell" is not offered under a radial parent.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.DotGen
{
    /// An X-by-Y lattice clipped to the generator's shape.
    [Serializable]
    [DotModule("grid", "Grid", "Grid", Order = 0)]
    public class DotGridPlacement : DotPlacement
    {
        [Range(1, 28)]
        [Tooltip("How many dots across the lattice.")]
        public int columns = 4;

        [Range(1, 28)]
        [Tooltip("How many dots down the lattice.")]
        public int rows = 4;

        [Range(0f, 80f)]
        [Tooltip("Widens the lattice horizontally past the area, spreading the columns apart.")]
        public float gapX = 0f;

        [Range(0f, 80f)]
        [Tooltip("Widens the lattice vertically past the area, spreading the rows apart.")]
        public float gapY = 0f;

        [Range(-80f, 80f)]
        [Tooltip("Slants the lattice sideways, more with each row down.")]
        public float skewX = 0f;

        [Range(-80f, 80f)]
        [Tooltip("Slants the lattice vertically, more with each column across.")]
        public float skewY = 0f;

        [Range(-180f, 180f)]
        [Tooltip("Turns the whole lattice inside the area. Cells turn with it.")]
        public float rotation = 0f;

        [Range(-50f, 50f)]
        [Tooltip("Slides the lattice sideways inside the area; dots pushed outside the shape are dropped.")]
        public float offsetX = 0f;

        [Range(-50f, 50f)]
        [Tooltip("Slides the lattice up or down inside the area; dots pushed outside the shape are dropped.")]
        public float offsetY = 0f;

        public override void Evaluate(DotGenerator gen, in DotArea area, int instIdx, int globalSeed, List<DotLocal> outDots)
        {
            int co = Mathf.Max(1, columns);
            int ro = Mathf.Max(1, rows);
            float spanX = 0.9f + gapX / 180f;
            float spanY = 0.9f + gapY / 180f;
            float cellW = Mathf.Min(0.9f, spanX / co) * 0.8f;
            float cellH = Mathf.Min(0.9f, spanY / ro) * 0.8f;
            float an = rotation * Mathf.Deg2Rad;
            float ca = Mathf.Cos(an), sa = Mathf.Sin(an);

            for (int y = 0; y < ro; y++)
            {
                for (int x = 0; x < co; x++)
                {
                    float lx = co == 1 ? 0f : (x / (float)(co - 1) - 0.5f) * spanX;
                    float ly = ro == 1 ? 0f : (y / (float)(ro - 1) - 0.5f) * spanY;
                    lx += (ro == 1 ? 0f : y / (float)(ro - 1) - 0.5f) * skewX / 100f;
                    ly += (co == 1 ? 0f : x / (float)(co - 1) - 0.5f) * skewY / 100f;

                    float rx = lx * ca - ly * sa;
                    float ry = lx * sa + ly * ca;
                    lx = rx + offsetX / 100f;
                    ly = ry + offsetY / 100f;

                    if (!DotGenMath.Inside(lx, ly, gen.shape)) continue;
                    outDots.Add(new DotLocal
                    {
                        x = lx, y = ly, key = x + y * co,
                        hasCell = true, cellW = cellW, cellH = cellH, cellRot = an
                    });
                }
            }
        }
    }

    /// How a Box Row picks a box's height.
    public enum DotBoxHeightBasis { GeneratorArea, MultipleOfBoxWidth }

    /// Which edge of the area a Box Row's boxes sit on.
    public enum DotBoxAlign { Bottom, Centre, Top }

    /// Variable-width boxes packed left to right, each with an exact cell. The generic basis for building
    /// masses: a row of boxes is a skyline only because of what is put in the cells.
    [Serializable]
    [DotModule("boxRow", "Box Row", "Box Row", Order = 1)]
    public class DotBoxRowPlacement : DotPlacement
    {
        [Range(1f, 50f)]
        [Tooltip("Narrowest a box may be, as a percentage of the area's width.")]
        public float widthMin = 8f;

        [Range(1f, 60f)]
        [Tooltip("Widest a box may be, as a percentage of the area's width.")]
        public float widthMax = 18f;

        [Tooltip("Measure box height against the generator area, or as a multiple of each box's own width.")]
        public DotBoxHeightBasis heightBasis = DotBoxHeightBasis.GeneratorArea;

        [Range(1f, 1000f)]
        [ZUIShowIf("heightBasis", "GeneratorArea")]
        [Tooltip("Shortest a box may be, as a percentage of the area's height. Past 100% needs Grow outside area.")]
        public float heightMin = 35f;

        [Range(1f, 1000f)]
        [ZUIShowIf("heightBasis", "GeneratorArea")]
        [Tooltip("Tallest a box may be, as a percentage of the area's height. Past 100% needs Grow outside area.")]
        public float heightMax = 92f;

        [Range(0.1f, 10f)]
        [ZUIShowIf("heightBasis", "MultipleOfBoxWidth")]
        [Tooltip("Shortest a box may be, as a multiple of its own width.")]
        public float aspectMin = 1f;

        [Range(0.1f, 10f)]
        [ZUIShowIf("heightBasis", "MultipleOfBoxWidth")]
        [Tooltip("Tallest a box may be, as a multiple of its own width.")]
        public float aspectMax = 3f;

        [Tooltip("On, the vertical anchor is a baseline and boxes may grow right out of the area, across whatever is behind them.")]
        public bool growOutsideArea = true;

        [Range(0f, 20f)]
        [Tooltip("Gap left between neighbouring boxes, as a percentage of the area's width.")]
        public float gap = 2f;

        [Range(0f, 25f)]
        [Tooltip("Empty margin kept at each end of the row, as a percentage of the area's width.")]
        public float edgePadding = 2f;

        [Tooltip("Which edge the boxes stand on. Bottom grows them upward.")]
        public DotBoxAlign verticalAnchor = DotBoxAlign.Bottom;

        [Range(0, 999)]
        [Tooltip("Changes which widths and heights this row draws, without changing anything else.")]
        public int variationSeed = 101;

        /// Boxes packed per evaluated area. A safety limit, not an authored one.
        public const int MaxBoxes = 300;

        public override void Evaluate(DotGenerator gen, in DotArea area, int instIdx, int globalSeed, List<DotLocal> outDots)
        {
            float pad = DotGenMath.Clamp(edgePadding / 100f, 0f, 0.45f);
            float gp = DotGenMath.Clamp(gap / 100f, 0f, 0.4f);
            float minW = Mathf.Max(0.005f, Mathf.Min(widthMin, widthMax) / 100f);
            float maxW = Mathf.Max(minW, Mathf.Max(widthMin, widthMax) / 100f);
            float minH = Mathf.Max(0.005f, Mathf.Min(heightMin, heightMax) / 100f);
            float maxH = Mathf.Max(minH, Mathf.Max(heightMin, heightMax) / 100f);
            float minA = Mathf.Max(0.05f, Mathf.Min(aspectMin, aspectMax));
            float maxA = Mathf.Max(minA, Mathf.Max(aspectMin, aspectMax));

            float right = 0.5f - pad;
            float x = -0.5f + pad;
            int key = 0;

            while (x + minW <= right && key < MaxBoxes)
            {
                float w = DotGenMath.Lerp(minW, maxW, (float)DotGenMath.Hash01(globalSeed + variationSeed, instIdx, key));
                float remain = right - x;
                if (w > remain) w = remain;
                if (w < minW * 0.7f) break;

                float hv = (float)DotGenMath.Hash01(globalSeed + variationSeed + 17, instIdx, key);
                float h = heightBasis == DotBoxHeightBasis.MultipleOfBoxWidth
                    ? w * (area.w / Mathf.Max(0.0001f, area.h)) * DotGenMath.Lerp(minA, maxA, hv)
                    : DotGenMath.Lerp(minH, maxH, hv);

                if (!growOutsideArea) h = Mathf.Min(h, 1f - pad * 2f);

                float y = verticalAnchor == DotBoxAlign.Top ? -0.5f + pad + h / 2f
                        : verticalAnchor == DotBoxAlign.Centre ? 0f
                        : 0.5f - pad - h / 2f;

                outDots.Add(new DotLocal
                {
                    x = x + w / 2f, y = y, key = key,
                    hasCell = true, cellW = w, cellH = h, cellRot = 0f
                });

                x += w + gp;
                key++;
            }
        }
    }

    /// Which end of the radius the ring ordering starts from.
    public enum DotRadialFlow { CenterToEdge, EdgeToCenter }

    /// Rings of points. No cells.
    [Serializable]
    [DotModule("radial", "Radial Grid", "Radial Grid", Order = 2)]
    public class DotRadialPlacement : DotPlacement
    {
        public override bool ProvidesCells => false;

        [Tooltip("Which end of the radius counts as the first ring, for the spacing curve and the density scale.")]
        public DotRadialFlow flow = DotRadialFlow.CenterToEdge;

        [Range(1, 14)]
        [Tooltip("How many rings of dots.")]
        public int rings = 3;

        [Range(1, 36)]
        [Tooltip("Dots on a ring before the density scale changes it.")]
        public int baseDotsPerRing = 7;

        [Range(0f, 95f)]
        [Tooltip("Radius of the innermost ring, as a percentage of the area.")]
        public float innerRadius = 18f;

        [Range(5f, 100f)]
        [Tooltip("Radius of the outermost ring, as a percentage of the area.")]
        public float outerRadius = 88f;

        [Range(-100f, 100f)]
        [Tooltip("Bunches the rings toward one end of the radius instead of spacing them evenly.")]
        public float ringSpacingCurve = 0f;

        [Range(-90f, 180f)]
        [Tooltip("Adds dots to the outer rings and takes them off the inner ones (or the reverse, below zero).")]
        public float outerDensityScale = 45f;

        [Range(0f, 100f)]
        [Tooltip("Turns every ring by a fraction of its own dot spacing, so the rings stop lining up.")]
        public float phase = 8f;

        [Range(-180f, 180f)]
        [Tooltip("Turns the whole pattern.")]
        public float rotation = 0f;

        [Tooltip("Puts one dot at the exact centre, whatever the rings do.")]
        public bool centerDot = true;

        public override void Evaluate(DotGenerator gen, in DotArea area, int instIdx, int globalSeed, List<DotLocal> outDots)
        {
            int n = Mathf.Max(1, rings);
            if (centerDot) outDots.Add(new DotLocal { x = 0f, y = 0f, key = 0 });

            float exp = Mathf.Pow(2f, ringSpacingCurve / 55f);

            for (int r = 0; r < n; r++)
            {
                float t = n == 1 ? 0.5f : r / (float)(n - 1);
                float ord = flow == DotRadialFlow.EdgeToCenter ? 1f - t : t;
                float u = Mathf.Pow(ord, exp);
                float rad = DotGenMath.Lerp(innerRadius / 200f, outerRadius / 200f, u);
                // RoundJs, not Mathf.RoundToInt: .NET rounds a half to EVEN, so a ring landing on exactly 10.5
                // dots would come out with 10 here and 11 in the reference.
                int count = Mathf.Max(1, (int)DotGenMath.RoundJs(baseDotsPerRing * (1f + (outerDensityScale / 100f) * (t - 0.5f) * 2f)));

                for (int i = 0; i < count; i++)
                {
                    float an = (i / (float)count) * DotGenMath.Tau + (phase / 100f) * DotGenMath.Tau + rotation * Mathf.Deg2Rad;
                    float x = Mathf.Cos(an) * rad;
                    float y = Mathf.Sin(an) * rad;
                    if (!DotGenMath.Inside(x, y, gen.shape)) continue;
                    outDots.Add(new DotLocal { x = x, y = y, key = 1 + r * 100 + i });
                }
            }
        }
    }
}
