using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// How a tile of the sample grid maps to canvas points. The mapping is a pure function of the
    /// <b>absolute</b> sample index, which is what makes tile independence structural rather than a
    /// convention: the same tile produces identical values whether it is requested alone or as part of the
    /// whole grid (BC-1.6).
    /// </summary>
    public struct ShaperSampleGrid
    {
        /// <summary>Canvas position of sample (0,0).</summary>
        public float originX, originY;
        /// <summary>Canvas units per sample.</summary>
        public float pixelSize;
        /// <summary>Extra coverage softness in canvas units, beyond the half-pixel default.</summary>
        public float edgeSoftness;

        public static ShaperSampleGrid Centred(int width, int height, float pixelSize, float edgeSoftness = 0f)
        {
            return new ShaperSampleGrid
            {
                originX = -0.5f * (width - 1) * pixelSize,
                originY = -0.5f * (height - 1) * pixelSize,
                pixelSize = pixelSize,
                edgeSoftness = edgeSoftness,
            };
        }

        public float X(int ix) => originX + ix * pixelSize;
        public float Y(int iy) => originY + iy * pixelSize;
    }

    /// <summary>
    /// Walks a compiled program. The block entry fills a rectangular tile in one call; the point entry is the
    /// same walk for a single canvas point.
    ///
    /// The inner loop is a flat walk over a struct array with a switch: no virtual method, no interface, no
    /// delegate, no allocation, no boxing, no LINQ, no <c>System.Random</c>. A generator is never invoked once
    /// per sample through a managed callback (BC-1.1), and no value funnel is called from inside the loop
    /// (BC-1.2) — every dial was resolved into the program at compile time.
    /// </summary>
    public static class ShaperEvaluator
    {
        /// <summary>
        /// The signed distance at one canvas point, in canvas pixels. Negative inside.
        /// The signature is deliberately the general one — a point, not a pixel index.
        /// </summary>
        public static float Distance(ShaperProgram program, float x, float y, float[] stack)
        {
            ShaperOp[] ops = program.ops;
            int sp = 0;

            for (int i = 0; i < ops.Length; i++)
            {
                switch (ops[i].kind)
                {
                    case ShaperOpKind.Empty:
                        stack[sp++] = ShaperField.Empty;
                        break;

                    case ShaperOpKind.Leaf:
                    {
                        float lx = ops[i].m00 * x + ops[i].m01 * y + ops[i].m02;
                        float ly = ops[i].m10 * x + ops[i].m11 * y + ops[i].m12;
                        float d = ShaperSdf.Evaluate(ops[i].primitive, ops[i].count,
                                                     ops[i].p0, ops[i].p1, ops[i].p2, ops[i].p3,
                                                     ops[i].p4, ops[i].p5, ops[i].p6, ops[i].p7,
                                                     ops[i].p8, ops[i].p9, ops[i].p10, ops[i].p11,
                                                     lx, ly);
                        stack[sp++] = d * ops[i].distanceScale;
                        break;
                    }

                    case ShaperOpKind.Combine:
                    {
                        float b = stack[--sp];
                        float a = stack[sp - 1];
                        stack[sp - 1] = ShaperOps.Combine(ops[i].mode, a, b,
                                                          ops[i].p0, ops[i].p1, ops[i].p2, ops[i].p3);
                        break;
                    }

                    case ShaperOpKind.Sweep:
                    {
                        // Identity is guaranteed STRUCTURALLY, not numerically: at full extent the child's
                        // float is returned by an exact early-out before any arithmetic touches it. Relying on
                        // "the wedge test happens to pass" would leave a max against a computed value free to
                        // perturb the last bit.
                        if (ops[i].p5 != 0f) break;

                        float lx = ops[i].m00 * x + ops[i].m01 * y + ops[i].m02;
                        float ly = ops[i].m10 * x + ops[i].m11 * y + ops[i].m12;
                        float w = ops[i].sweepAxis == ShaperSweepAxis.Radial
                            ? ShaperOps.RadialWedge(ops[i].p0, ops[i].p1, ops[i].p2, ops[i].p3, ops[i].p4 != 0f, lx, ly)
                            : ShaperOps.LongitudinalSlab(ops[i].p0, ops[i].p1, lx);
                        stack[sp - 1] = Mathf.Max(stack[sp - 1], w * ops[i].distanceScale);
                        break;
                    }

                    case ShaperOpKind.Shell:
                    {
                        // Shell's ONE identity is `enabled == false`, and it is structural in the compiler:
                        // no op is emitted at all (ShaperCompiler.EmitShell). There is deliberately no second
                        // early-out on thickness here — a zero thickness is an empty interior, not the solid.
                        stack[sp - 1] = ShaperOps.Shell(ops[i].shellAlignment, ops[i].p0, stack[sp - 1]);
                        break;
                    }

                    case ShaperOpKind.Dilate:
                    {
                        // BORDER-CONTRACT BD-2.2: a joining border publishes `d − reach`, the EXACT signed
                        // distance of the dilated set {d ≤ reach}. One subtraction — no combine, no min against
                        // the strip, and therefore no spurious zero crossing on the original silhouette. The
                        // identity is structural in the compiler: when there is no live joining border no op is
                        // emitted at all (ShaperCompiler.EmitBorderJoin), so BD-1.5's zero-width no-op is bitwise
                        // rather than arithmetic.
                        stack[sp - 1] = ShaperBorder.Dilate(ops[i].p0, stack[sp - 1]);
                        break;
                    }
                }
            }

            return sp > 0 ? stack[0] : ShaperField.Empty;
        }

        /// <summary>Coverage at one canvas point, derived from the finished distance once, at the top.</summary>
        public static float Coverage(ShaperProgram program, in ShaperSampleGrid grid, float x, float y, float[] stack)
            => ShaperField.Coverage(Distance(program, x, y, stack),
                                    ShaperField.HalfBand(grid.edgeSoftness, grid.pixelSize));

        /// <summary>
        /// Fill a rectangular tile of the sample grid in one call, writing <paramref name="width"/> contiguous
        /// values per row for <paramref name="height"/> rows into host-supplied flat arrays (BC-1.1/BC-1.2).
        /// The host owns, allocates and sizes the arrays; the generator never allocates, replaces, resizes or
        /// frees a sheet (BC-3.7f).
        ///
        /// Either sheet may be null — a consumer allocates only the declared, requested quantities.
        /// </summary>
        /// <param name="x0">Absolute sample index of the tile's left column.</param>
        /// <param name="y0">Absolute sample index of the tile's bottom row.</param>
        /// <param name="dstOffset">Index in the destination arrays of the tile's first sample.</param>
        /// <param name="dstStride">Row stride of the destination arrays; pass <paramref name="width"/> for a packed tile.</param>
        public static void FillTile(ShaperProgram program, in ShaperSampleGrid grid,
                                    int x0, int y0, int width, int height,
                                    float[] distance, float[] coverage,
                                    int dstOffset, int dstStride, float[] stack)
        {
            float halfBand = ShaperField.HalfBand(grid.edgeSoftness, grid.pixelSize);

            for (int j = 0; j < height; j++)
            {
                float y = grid.originY + (y0 + j) * grid.pixelSize;
                int row = dstOffset + j * dstStride;
                for (int i = 0; i < width; i++)
                {
                    float x = grid.originX + (x0 + i) * grid.pixelSize;
                    float d = Distance(program, x, y, stack);
                    if (distance != null) distance[row + i] = d;
                    if (coverage != null) coverage[row + i] = ShaperField.Coverage(d, halfBand);
                }
            }
        }

        /// <summary>Convenience for the whole grid: one tile at the origin, packed.</summary>
        public static void Fill(ShaperProgram program, in ShaperSampleGrid grid, int width, int height,
                                float[] distance, float[] coverage, float[] stack)
            => FillTile(program, grid, 0, 0, width, height, distance, coverage, 0, width, stack);
    }
}
