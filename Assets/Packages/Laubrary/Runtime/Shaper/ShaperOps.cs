using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The three combine modes (SHAPE-TREE-RULES R1). Softness is a <i>property every mode has</i> — a blend
    /// width for Add and Intersect, a carve strength for Subtract — rather than a fourth mode, which is why
    /// the reference app's <c>subtract</c>/<c>subtractSoft</c> pair is one mode here.
    /// APPEND-ONLY: serialized as an int.
    /// </summary>
    public enum ShaperCombineMode { Add = 0, Subtract = 1, Intersect = 2 }

    /// <summary>Which side of the surface a shell's band sits on.</summary>
    public enum ShaperShellAlignment { Centred = 0, Inward = 1, Outward = 2 }

    /// <summary>
    /// The operators: the two smoothing kernels, the three combine modes, sweep and shell.
    /// Every one of these is a static function over floats — nothing here dispatches, allocates or boxes.
    /// </summary>
    public static class ShaperOps
    {
        /// <summary>
        /// The blend profile exponent, <c>n = pow(8, sharpness)</c>, so n runs 1…8 as sharpness runs 0…1.
        /// Independent of the blend width: the deepest a soft-add joint ever pulls in is <c>k/(2n)</c>, so
        /// raising sharpness at fixed width shrinks the fillet from <c>k/2</c> to <c>k/16</c>.
        /// </summary>
        public static float BlendExponent(float sharpness) => Mathf.Pow(8f, Mathf.Clamp01(sharpness));

        /// <summary>
        /// <c>smoothMinShaped(a,b,k,n) = min(a,b) − pow(max(k−|a−b|,0)/k, n) · k/(2n)</c>
        /// (reference app, <c>index.html:1007</c>).
        ///
        /// At <c>k ≤ 0</c> it returns <c>min(a,b)</c> by an exact early-out, which is what makes soft Add at
        /// zero blend width bit-identical to hard Add.
        /// </summary>
        public static float SmoothMinShaped(float a, float b, float k, float n)
        {
            if (k <= 0f) return Mathf.Min(a, b);
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Min(a, b) - Mathf.Pow(h, n) * k / (2f * n);
        }

        /// <summary>The exact negation dual of <see cref="SmoothMinShaped"/>, so the same identity holds at <c>k ≤ 0</c>.</summary>
        public static float SmoothMaxShaped(float a, float b, float k, float n)
            => -SmoothMinShaped(-a, -b, k, n);

        /// <summary>
        /// Fold one member into the accumulator. <paramref name="a"/> is the accumulated silhouette,
        /// <paramref name="b"/> the incoming member.
        ///
        /// Subtract's soft form is the reference app's, preserved verbatim because both of its properties are
        /// genuine correctness arguments rather than preferences (<c>index.html:1123-1134</c>):
        /// scaling the <i>carved amount</i> by strength — rather than shrinking the cutter's own field by an
        /// offset — is what makes <c>strength = 0</c> an exact no-op <i>everywhere in the field</i>, where the
        /// offset alternative silently clips a deep point's magnitude with no visible hole; and the band
        /// <c>sin(strength·π)·reach·0.6</c> is zero at both ends of strength, so the cut's edge rounds as it
        /// grows and then creases back to an exact hard cut.
        /// </summary>
        public static float Combine(ShaperCombineMode mode, float a, float b,
                                    float blendWidth, float blendExponent, float carveStrength, float reach)
        {
            switch (mode)
            {
                case ShaperCombineMode.Add:
                    return blendWidth > 0f ? SmoothMinShaped(a, b, blendWidth, blendExponent) : Mathf.Min(a, b);

                case ShaperCombineMode.Intersect:
                    return blendWidth > 0f ? SmoothMaxShaped(a, b, blendWidth, blendExponent) : Mathf.Max(a, b);

                case ShaperCombineMode.Subtract:
                {
                    // Both identities are structural early-outs, not numerically-neutral computations. The
                    // strength-1 case in particular CANNOT be left to the algebra: float sin(π) is −8.7e-8,
                    // not 0, so the band would be a tiny non-zero (or tiny negative) number and the result
                    // would differ from max(a, −b) in the last bits.
                    if (carveStrength <= 0f) return a;
                    if (carveStrength >= 1f) return Mathf.Max(a, -b);
                    return SubtractSoftRaw(a, b, carveStrength, blendExponent, reach);
                }
            }
            return a;
        }

        /// <summary>
        /// The soft-subtract expression with <b>no early-out</b>, exactly as the reference app writes it
        /// (<c>index.html:1123-1134</c>). <see cref="Combine"/> calls this only at strictly partial strength;
        /// it is public so the audit can evaluate the algebra at the endpoints, where <see cref="Combine"/>
        /// short-circuits and a test of the endpoints would otherwise be testing the <c>if</c> rather than the
        /// maths.
        ///
        /// The reference app's own correctness argument is that <c>band = sin(strength·π)·reach·0.6</c> is
        /// zero at both ends of strength, so at strength 1 the expression "degenerates exactly to the hard
        /// <c>max(a, −b)</c>". The argument is right in real arithmetic and <b>false in floats</b>: float
        /// <c>sin(π)</c> is −8.74e-8, so the band is a tiny NEGATIVE number, <see cref="SmoothMinShaped"/>
        /// takes its <c>k ≤ 0</c> path, and the result differs from <c>max(a, −b)</c> only by rounding in the
        /// last bits — which is why the identity is asserted structurally rather than left to the algebra.
        /// </summary>
        public static float SubtractSoftRaw(float a, float b, float carveStrength, float blendExponent, float reach)
        {
            float carve = -b - a;                                            // = the (b − a) term of max(a,b) = a + max(0, b−a)
            float band = Mathf.Sin(carveStrength * Mathf.PI) * reach * 0.6f;
            float bite = -SmoothMinShaped(0f, -carve, band, blendExponent);
            return a + carveStrength * bite;
        }

        /// <summary>
        /// The exact wedge field for a radial sweep, in the sweep node's own local frame: the intersection of
        /// two half-planes through the origin for <c>extent ≤ 180°</c>, their union above it. Both half-planes
        /// are exactly Lipschitz-1, and so is a max or min of them.
        /// </summary>
        public static float RadialWedge(float n1x, float n1y, float n2x, float n2y, bool union, float x, float y)
        {
            float f1 = n1x * x + n1y * y;
            float f2 = n2x * x + n2y * y;
            return union ? Mathf.Min(f1, f2) : Mathf.Max(f1, f2);
        }

        /// <summary>The exact slab field for a longitudinal sweep: <c>max(x0 − x, x − x1)</c>, Lipschitz-1.</summary>
        public static float LongitudinalSlab(float x0, float x1, float x)
            => Mathf.Max(x0 - x, x - x1);

        /// <summary>
        /// Shell. All three alignments are an <c>abs</c> and a hard <c>max</c> of Lipschitz-<c>B_child</c>
        /// quantities, so the bound composes unchanged.
        ///
        /// This is not Ring's inner radius under a new name: subtracting a scaled-down copy of a shape from
        /// itself gives a wall whose thickness varies with the shape, thin where the shape is thin, whereas
        /// Shell keeps a constant wall thickness everywhere.
        /// </summary>
        public static float Shell(ShaperShellAlignment alignment, float thickness, float d)
        {
            switch (alignment)
            {
                case ShaperShellAlignment.Inward: return Mathf.Max(d, -d - thickness);
                case ShaperShellAlignment.Outward: return Mathf.Max(-d, d - thickness);
                default: return Mathf.Abs(d) - thickness * 0.5f;
            }
        }
    }
}
