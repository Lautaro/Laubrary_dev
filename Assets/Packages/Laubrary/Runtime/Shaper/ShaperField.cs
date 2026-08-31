using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The vocabulary the whole shape engine agrees on: the sign convention, the empty-field constant,
    /// and the single place a finished signed distance becomes coverage.
    ///
    /// Sign convention (BUFFER_CONTRACT BC-3.3 #3): <b>negative inside, positive outside, zero on the
    /// boundary</b>. Units are canvas pixels at every level of the tree — the transform block's
    /// <c>σ_min</c> rescale (see <see cref="ShaperMatrix.SigmaMin"/>) is what guarantees that, so a blend
    /// width authored in pixels means the same thing at any nesting depth or scale.
    /// Frame is the layer's own local space, +Y up, origin at the layer origin.
    /// </summary>
    public static class ShaperField
    {
        /// <summary>
        /// The value an empty field publishes: an empty bag, a member whose transform is singular, and the
        /// accumulator a bag starts from before its first member folds in.
        ///
        /// It is deliberately a <i>named</i> constant rather than the reference app's bare <c>1e6</c>
        /// literal, and it is deliberately outside the metric contract: it is not a distance to anything and
        /// no consumer may read meaning into its magnitude. What it <i>is</i> guaranteed to do is compose:
        /// <c>min(Empty, d) == d</c>, <c>max(Empty, -d) == Empty</c> and <c>max(Empty, d) == Empty</c> all
        /// hold exactly, which is precisely R1's "the accumulator starts empty, so a bottom-most Add gives
        /// itself and a bottom-most Subtract or Intersect gives nothing" — with no special case anywhere.
        ///
        /// Because it is a <i>constant</i> field its gradient is zero, so it trivially satisfies any declared
        /// Lipschitz bound.
        /// </summary>
        public const float Empty = 1e9f;

        /// <summary>Anything at or above this is the empty field rather than a real distance.</summary>
        public const float EmptyThreshold = 1e8f;

        /// <summary>True when <paramref name="distance"/> is the empty field rather than a real distance.</summary>
        public static bool IsEmpty(float distance) => distance >= EmptyThreshold;

        /// <summary>
        /// The half-width of the distance→coverage transition band, in canvas units.
        /// <c>halfBand = max(edgeSoftness, 0.5 · pixelSize)</c>, so the default (<c>edgeSoftness == 0</c>) is a
        /// half-pixel antialiased edge and nothing else.
        /// </summary>
        public static float HalfBand(float edgeSoftness, float pixelSize)
            => Mathf.Max(edgeSoftness, 0.5f * pixelSize);

        /// <summary>
        /// The distance→coverage kernel: <c>coverage = 1 − smoothstep(−halfBand, +halfBand, distance)</c>.
        ///
        /// Derived from the finished, combined distance <i>once, at the top</i> — combining happens in the
        /// distance domain (SHAPE-TREE-RULES R6) and nothing folds coverage. Clamped to [0,1] because a
        /// distance field carries no information above 1.
        /// </summary>
        public static float Coverage(float distance, float halfBand)
        {
            if (halfBand <= 0f) return distance <= 0f ? 1f : 0f;
            float t = (distance + halfBand) / (halfBand + halfBand);
            if (t <= 0f) return 1f;
            if (t >= 1f) return 0f;
            return 1f - t * t * (3f - 2f * t);
        }
    }
}
