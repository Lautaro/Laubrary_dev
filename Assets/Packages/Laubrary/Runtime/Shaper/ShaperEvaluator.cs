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

                    case ShaperOpKind.CompositeSample:
                    {
                        // T-0112 — a composite generator has no analytic distance, only a raster it rendered once
                        // at compile time (ShaperCompiler.EmitComposite). Map the canvas point into the node's
                        // own local frame exactly like Leaf does, sample that raster's coverage there, and invert
                        // it back into a pseudo-distance so the rest of the fold (Combine, Sweep, Shell, Dilate)
                        // needs no branch of its own for this op kind.
                        float lx = ops[i].m00 * x + ops[i].m01 * y + ops[i].m02;
                        float ly = ops[i].m10 * x + ops[i].m11 * y + ops[i].m12;
                        ShaperCompiledComposite raster = program.composites[ops[i].count];
                        float cov = SampleCompositeCoverage(raster, lx, ly, ops[i].p0, ops[i].p1);
                        float dLocal = InverseCoverage(cov, ops[i].p2);
                        stack[sp++] = dLocal * ops[i].distanceScale;
                        break;
                    }

                    case ShaperOpKind.SpriteSample:
                    {
                        // T-0175 — a Sprite primitive's raster already holds a real signed distance (unlike
                        // CompositeSample's coverage, which needs inverting), so this is a straight bilinear
                        // fetch: map into the node's own local frame like Leaf/CompositeSample, sample, scale.
                        float lx = ops[i].m00 * x + ops[i].m01 * y + ops[i].m02;
                        float ly = ops[i].m10 * x + ops[i].m11 * y + ops[i].m12;
                        ShaperCompiledSpriteField raster = program.spriteFields[ops[i].count];
                        float dLocal = SampleSpriteDistance(raster, lx, ly, ops[i].p0, ops[i].p1);
                        stack[sp++] = dLocal * ops[i].distanceScale;
                        break;
                    }

                    case ShaperOpKind.TextSample:
                    {
                        // T-0174 — a Text primitive's raster holds a real signed distance for the same reason a
                        // Sprite's does (an exact distance transform, not an inverted coverage), so this is the
                        // same straight bilinear fetch against its own array.
                        float lx = ops[i].m00 * x + ops[i].m01 * y + ops[i].m02;
                        float ly = ops[i].m10 * x + ops[i].m11 * y + ops[i].m12;
                        ShaperCompiledTextField raster = program.textFields[ops[i].count];
                        float dLocal = SampleTextDistance(raster, lx, ly, ops[i].p0, ops[i].p1);
                        stack[sp++] = dLocal * ops[i].distanceScale;
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

        /// <summary>
        /// T-0112 — bilinear-sample a composite generator's baked coverage raster at a LOCAL-frame point
        /// <paramref name="lx"/>/<paramref name="ly"/>, where the raster covers
        /// <c>[-halfExtentX, halfExtentX] × [-halfExtentY, halfExtentY]</c>. CLAMPS at the box edge rather than
        /// treating outside as automatically zero — <see cref="ShaperCompositeDef.halfExtentX"/>'s doc names
        /// this as the reason an author must size the box to where the source has already faded out.
        /// </summary>
        static float SampleCompositeCoverage(ShaperCompiledComposite raster, float lx, float ly,
                                             float halfExtentX, float halfExtentY)
        {
            if (raster == null || raster.coverage == null || raster.width <= 0 || raster.height <= 0) return 0f;

            float u = halfExtentX > 1e-9f ? (lx + halfExtentX) / (2f * halfExtentX) : 0.5f;
            float v = halfExtentY > 1e-9f ? (ly + halfExtentY) / (2f * halfExtentY) : 0.5f;
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            float fx = u * raster.width - 0.5f;
            float fy = v * raster.height - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, raster.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, raster.height - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, raster.width - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, raster.height - 1);
            float tx = Mathf.Clamp01(fx - x0);
            float ty = Mathf.Clamp01(fy - y0);

            float c00 = raster.coverage[y0 * raster.width + x0];
            float c10 = raster.coverage[y0 * raster.width + x1];
            float c01 = raster.coverage[y1 * raster.width + x0];
            float c11 = raster.coverage[y1 * raster.width + x1];
            float top = Mathf.Lerp(c00, c10, tx);
            float bot = Mathf.Lerp(c01, c11, tx);
            return Mathf.Lerp(top, bot, ty);
        }

        /// <summary>
        /// T-0191 — write a composite root's OWN PICTURE into the albedo sheet for one tile: the consumer
        /// <see cref="ShaperCompiledComposite.pixels"/> was always written for. Called by
        /// <c>ShaperFillResolver.PaintTile</c> immediately after the owner's fill has emitted, replacing what
        /// that fill wrote — see <see cref="ShaperCompositeAlbedo"/> for the four clauses that decide WHEN.
        ///
        /// Straight LINEAR albedo, no alpha: the composite's alpha is already the node's published coverage
        /// (<see cref="ShaperCompiledComposite.coverage"/>), it has already become <c>ownCoverage</c> and then
        /// <c>coverageEff</c>, and the paint pass multiplies albedo by that. Returning a premultiplied colour
        /// here would apply alpha twice and darken every antialiased edge against its own background.
        /// </summary>
        /// <param name="albedo">Three floats per sample, addressed exactly as <c>ShaperFillEmit.albedo</c> is.</param>
        public static void FillCompositeAlbedoTile(ShaperCompositeAlbedo src, in ShaperSampleGrid grid,
                                                   int x0, int y0, int width, int height,
                                                   float[] albedo, int dstOffset, int dstStride)
        {
            if (src == null || src.raster == null || albedo == null) return;

            for (int j = 0; j < height; j++)
            {
                float y = grid.originY + (y0 + j) * grid.pixelSize;
                int row = dstOffset + j * dstStride;
                for (int i = 0; i < width; i++)
                {
                    float x = grid.originX + (x0 + i) * grid.pixelSize;
                    // The SAME mapping the CompositeSample case uses for coverage, so colour and coverage are
                    // read at one point rather than at two that differ by a fraction of a texel.
                    float lx = src.m00 * x + src.m01 * y + src.m02;
                    float ly = src.m10 * x + src.m11 * y + src.m12;
                    SampleCompositeColour(src.raster, lx, ly, src.halfExtentX, src.halfExtentY,
                                          out float r, out float g, out float b);
                    int a3 = (row + i) * 3;
                    albedo[a3 + 0] = r;
                    albedo[a3 + 1] = g;
                    albedo[a3 + 2] = b;
                }
            }
        }

        /// <summary>
        /// T-0191 — bilinear-sample a composite's baked picture as STRAIGHT LINEAR RGB, with the same clamping
        /// box as <see cref="SampleCompositeCoverage"/> so colour and coverage agree everywhere including
        /// outside the box.
        ///
        /// <b>Filtered in PREMULTIPLIED space, then un-premultiplied.</b> The raster is straight alpha, and
        /// lerping straight colour across an edge texel mixes in the colour a fully transparent texel merely
        /// happens to carry — a dark or arbitrary halo one texel wide around every silhouette. Weighting each
        /// texel by its own alpha is the alpha-weighted mean of the COVERED texels, which is the same
        /// correction <c>PyreSupersample.Downsample</c> makes when it collapses a supersampled block
        /// (<c>Runtime/Pyre/PyreSupersample.cs:58</c>).
        ///
        /// Where the whole neighbourhood is transparent the weighted mean is undefined, so the nearest texel's
        /// own colour is returned rather than a division by zero. Coverage there is 0, so nothing is painted;
        /// the value exists only so the sheet never carries a NaN into the accumulator.
        /// </summary>
        static void SampleCompositeColour(ShaperCompiledComposite raster, float lx, float ly,
                                          float halfExtentX, float halfExtentY,
                                          out float r, out float g, out float b)
        {
            r = g = b = 0f;
            if (raster == null || raster.pixels == null || raster.width <= 0 || raster.height <= 0) return;

            float u = halfExtentX > 1e-9f ? (lx + halfExtentX) / (2f * halfExtentX) : 0.5f;
            float v = halfExtentY > 1e-9f ? (ly + halfExtentY) / (2f * halfExtentY) : 0.5f;
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            float fx = u * raster.width - 0.5f;
            float fy = v * raster.height - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, raster.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, raster.height - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, raster.width - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, raster.height - 1);
            float tx = Mathf.Clamp01(fx - x0);
            float ty = Mathf.Clamp01(fy - y0);

            Color32 p00 = raster.pixels[y0 * raster.width + x0];
            Color32 p10 = raster.pixels[y0 * raster.width + x1];
            Color32 p01 = raster.pixels[y1 * raster.width + x0];
            Color32 p11 = raster.pixels[y1 * raster.width + x1];

            Premul(p00, out float r00, out float g00, out float b00, out float a00);
            Premul(p10, out float r10, out float g10, out float b10, out float a10);
            Premul(p01, out float r01, out float g01, out float b01, out float a01);
            Premul(p11, out float r11, out float g11, out float b11, out float a11);

            float pr = Mathf.Lerp(Mathf.Lerp(r00, r10, tx), Mathf.Lerp(r01, r11, tx), ty);
            float pg = Mathf.Lerp(Mathf.Lerp(g00, g10, tx), Mathf.Lerp(g01, g11, tx), ty);
            float pb = Mathf.Lerp(Mathf.Lerp(b00, b10, tx), Mathf.Lerp(b01, b11, tx), ty);
            float pa = Mathf.Lerp(Mathf.Lerp(a00, a10, tx), Mathf.Lerp(a01, a11, tx), ty);

            if (pa > 1e-6f)
            {
                float inv = 1f / pa;
                r = pr * inv; g = pg * inv; b = pb * inv;
            }
            else
            {
                // `tx/ty >= 0.5` picks the texel the sample actually sits nearest, which is what "nearest" has
                // to mean for this to be a defined value rather than an arbitrary corner.
                Color32 near = tx < 0.5f ? (ty < 0.5f ? p00 : p01) : (ty < 0.5f ? p10 : p11);
                r = ShaperSrgb.DecodeChannel(near.r * (1f / 255f));
                g = ShaperSrgb.DecodeChannel(near.g * (1f / 255f));
                b = ShaperSrgb.DecodeChannel(near.b * (1f / 255f));
            }
        }

        /// <summary>
        /// One straight-alpha sRGB texel as premultiplied LINEAR RGB plus its straight alpha. RGB is decoded
        /// through <see cref="ShaperSrgb.DecodeChannel"/> and alpha is NOT — alpha is a coverage, not a colour,
        /// the same split <c>ShaperFillCompiler</c> makes when it decodes a Texture fill's pixels
        /// (<c>ShaperFillCompiler.cs:630-635</c>).
        /// </summary>
        static void Premul(Color32 c, out float r, out float g, out float b, out float a)
        {
            a = c.a * (1f / 255f);
            r = ShaperSrgb.DecodeChannel(c.r * (1f / 255f)) * a;
            g = ShaperSrgb.DecodeChannel(c.g * (1f / 255f)) * a;
            b = ShaperSrgb.DecodeChannel(c.b * (1f / 255f)) * a;
        }

        /// <summary>
        /// T-0175 — bilinear-sample a Sprite primitive's baked distance raster at a LOCAL-frame point
        /// <paramref name="lx"/>/<paramref name="ly"/>, where the raster covers
        /// <c>[-halfExtentX, halfExtentX] x [-halfExtentY, halfExtentY]</c>. CLAMPS at the box edge exactly like
        /// <see cref="SampleCompositeCoverage"/> — a point outside the raster's own fitted box (possible under
        /// <see cref="ShaperSpriteFitMode.Uniform"/>, where the raster can be smaller than the primitive's
        /// authored half-extent) reads the nearest edge value rather than extrapolating.
        /// </summary>
        static float SampleSpriteDistance(ShaperCompiledSpriteField raster, float lx, float ly,
                                          float halfExtentX, float halfExtentY)
        {
            if (raster == null || raster.distance == null || raster.width <= 0 || raster.height <= 0) return ShaperField.Empty;

            float u = halfExtentX > 1e-9f ? (lx + halfExtentX) / (2f * halfExtentX) : 0.5f;
            float v = halfExtentY > 1e-9f ? (ly + halfExtentY) / (2f * halfExtentY) : 0.5f;
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            float fx = u * raster.width - 0.5f;
            float fy = v * raster.height - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, raster.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, raster.height - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, raster.width - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, raster.height - 1);
            float tx = Mathf.Clamp01(fx - x0);
            float ty = Mathf.Clamp01(fy - y0);

            float d00 = raster.distance[y0 * raster.width + x0];
            float d10 = raster.distance[y0 * raster.width + x1];
            float d01 = raster.distance[y1 * raster.width + x0];
            float d11 = raster.distance[y1 * raster.width + x1];
            float top = Mathf.Lerp(d00, d10, tx);
            float bot = Mathf.Lerp(d01, d11, tx);
            return Mathf.Lerp(top, bot, ty);
        }

        /// <summary>
        /// T-0174 — bilinear-sample a Text primitive's baked distance raster at a LOCAL-frame point, where the
        /// raster covers <c>[-halfExtentX, halfExtentX] x [-halfExtentY, halfExtentY]</c>. CLAMPS at the box edge
        /// like the two samplers above. Clamping is why <see cref="ShaperTextPrepassCache"/> bakes a blank margin
        /// around the string: past the raster the field stops growing, so a border or a shell can only reach as
        /// far out as that margin was baked.
        /// </summary>
        static float SampleTextDistance(ShaperCompiledTextField raster, float lx, float ly,
                                        float halfExtentX, float halfExtentY)
        {
            if (raster == null || raster.distance == null || raster.width <= 0 || raster.height <= 0) return ShaperField.Empty;

            float u = halfExtentX > 1e-9f ? (lx + halfExtentX) / (2f * halfExtentX) : 0.5f;
            float v = halfExtentY > 1e-9f ? (ly + halfExtentY) / (2f * halfExtentY) : 0.5f;
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            float fx = u * raster.width - 0.5f;
            float fy = v * raster.height - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, raster.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, raster.height - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, raster.width - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, raster.height - 1);
            float tx = Mathf.Clamp01(fx - x0);
            float ty = Mathf.Clamp01(fy - y0);

            float d00 = raster.distance[y0 * raster.width + x0];
            float d10 = raster.distance[y0 * raster.width + x1];
            float d01 = raster.distance[y1 * raster.width + x0];
            float d11 = raster.distance[y1 * raster.width + x1];
            float top = Mathf.Lerp(d00, d10, tx);
            float bot = Mathf.Lerp(d01, d11, tx);
            return Mathf.Lerp(top, bot, ty);
        }

        /// <summary>
        /// T-0112 — the exact closed-form inverse of <see cref="ShaperField.Coverage"/>'s smoothstep: given
        /// coverage <paramref name="coverage"/> ∈ [0,1] and the same <paramref name="halfBand"/> the forward
        /// direction used, returns a <c>d</c> such that
        /// <c>ShaperField.Coverage(d, halfBand) == coverage</c> (to float precision).
        ///
        /// <b>Where this is honest and where it is not.</b> The formula is exact everywhere coverage is strictly
        /// between 0 and 1 — the antialiased rim, roughly one bake texel wide. Outside that rim, coverage is
        /// flatly 0 or 1 and the formula SATURATES at <c>±halfBand</c> rather than reporting how much further out
        /// the sample really is — the raster carries no information past its own antialiasing, so there is none
        /// to invert. That saturation is exactly why a composite generator gives up the border stage and any
        /// soft-combine wider than about a texel (<see cref="ShaperCompiler"/>'s <c>EmitBorderJoin</c> refuses the
        /// former outright); a HARD combine (Add/Subtract/Intersect, blend width 0) only needs the SIGN correct,
        /// which this gives everywhere, saturated or not.
        /// </summary>
        static float InverseCoverage(float coverage, float halfBand)
        {
            if (halfBand <= 0f) return coverage >= 0.5f ? -1e-6f : 1e-6f;
            float s = Mathf.Clamp01(1f - coverage);          // smoothstep(t) target
            float t = 0.5f - Mathf.Sin(Mathf.Asin(Mathf.Clamp(1f - 2f * s, -1f, 1f)) / 3f);
            return halfBand * (2f * t - 1f);
        }
    }
}
