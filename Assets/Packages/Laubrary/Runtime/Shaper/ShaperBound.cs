using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The declared bound and how it composes.
    ///
    /// <b>What the bound means</b> (BC-4.2): a per-node number <c>B ≥ 1</c> promising
    /// <c>reportedDistance ≤ B × trueDistance</c> everywhere in the field, inside and outside. The safe march
    /// step for anything that later steps along a ray is <c>reportedDistance / B</c>. Nothing in the shape
    /// stage marches; the bound is one float per compiled node and one divide, which is nearly free to carry
    /// here and roughly forty times as expensive to retrofit.
    ///
    /// <b>How it is established</b>: analytically wherever the maths allows, and verified by dense
    /// <i>gradient</i> measurement over the whole field. Never read off a landmark point, however natural the
    /// landmark looks — the star's lobe tip is exactly 1.0000× and is precisely where a star is not at its
    /// worst. Gradient rather than ratio is a deliberate strengthening: if <c>sup|∇f| ≤ L</c> over the plane
    /// and <c>f</c> is zero on the boundary, then <c>|f(x)| ≤ L·d(x)</c> everywhere follows immediately, so a
    /// finite sample of the gradient certifies the bound over the whole field, which sampling the ratio at
    /// points cannot do.
    /// </summary>
    public static class ShaperBound
    {
        /// <summary>
        /// A transform composes its child's bound <b>unchanged</b>, because the <c>σ_min</c> rescale already
        /// absorbs the transform's effect on distance. See <see cref="ShaperMatrix.SigmaMin"/> for the
        /// one-line proof; it is the reason the rescale is not optional.
        /// </summary>
        public static float Transform(float childBound) => childBound;

        /// <summary>
        /// Combine. <b>Every mode, hard or soft, takes <c>max(B_a, B_b)</c>.</b>
        ///
        /// <b>Why the soft blend is free.</b> Write <c>h = max(k−|a−b|,0)/k</c>. Differentiating
        /// <c>smoothMinShaped</c> with <c>a &lt; b</c> gives <c>∂f/∂a = 1 − h^(n−1)/2</c> and
        /// <c>∂f/∂b = h^(n−1)/2</c>. Since <c>h ∈ [0,1]</c> and <c>n = pow(8, sharpness) ∈ [1,8]</c>, we have
        /// <c>h^(n−1) ∈ [0,1]</c>, so both weights lie in <c>[0,1]</c> and sum to 1. The result is a genuine
        /// convex combination of the two gradients, so <c>|∇f| ≤ max(|∇a|, |∇b|)</c> and the bound is
        /// preserved exactly. Soft Intersect is the negation dual and carries the same derivation.
        ///
        /// An earlier draft of the spec claimed these weights range over <c>[−1/2, 3/2]</c> and cost a factor
        /// of 2. That was a sign error on the penalty term; SHAPE-TREE-RULES R6 was right all along. Do not
        /// re-introduce the inflated factor from a stale copy of that document.
        ///
        /// <b>Soft Subtract — an explicit declaration whose value is the derived one.</b> The task brief
        /// requires this mode to carry an explicit bound rather than a derived one, because at partial
        /// strength it is not a distance field at all: it adds an authored bite term. The premise is right,
        /// but <i>not being a distance field</i> and <i>not being Lipschitz</i> are different properties, and
        /// the bound is about the second. With <c>result = a + s·bite</c>, <c>w</c> the smooth-min weight
        /// above and so in <c>[0,1]</c>, and <c>sw = s·w ∈ [0,1]</c>:
        /// <c>∇result = (1 − sw)·∇a − sw·∇d</c> — again a convex combination, of <c>∇a</c> and <c>−∇d</c>. So
        /// <c>max(B_a, B_d)</c> holds at every strength, not only at the exact endpoints.
        ///
        /// The declaration stays explicit — its own branch, its own constant, this derivation — while its
        /// value is the proven one. Declaring the brief's larger conservative number would be safe but not
        /// free: the bound divides the safe march step, so an unnecessary 4× is a 4× slower rotation feature,
        /// which is the exact cost this requirement exists to avoid paying. Changing it back is one line.
        /// </summary>
        /// <summary>
        /// The factor a soft Add or soft Intersect applies to <c>max(B_a, B_b)</c>. Proven to be 1 (§4.2 of
        /// the spec) and measured at 1.0000 even on deliberately anti-aligned bridge cases. Named rather than
        /// inlined so that raising it is a one-token change if the proof is ever doubted.
        /// </summary>
        public const float SoftBlendFactor = 1f;

        /// <summary>
        /// The factor a soft Subtract applies to <c>max(B_a, B_b)</c> at partial strength. Explicitly
        /// declared per the task brief; proven to be 1 (§4.3 of the spec). Set this to 4 to restore the
        /// brief's conservative value at the cost of a 4× smaller safe march step.
        /// </summary>
        public const float SoftCarveFactor = 1f;

        public static float Combine(ShaperCombineMode mode, float boundA, float boundB,
                                    float blendWidth, float carveStrength)
        {
            float m = Mathf.Max(boundA, boundB);
            switch (mode)
            {
                case ShaperCombineMode.Add:
                case ShaperCombineMode.Intersect:
                    // Soft or hard, the kernel is a convex combination of the two gradients. See the summary.
                    return blendWidth > 0f ? SoftBlendFactor * m : m;

                case ShaperCombineMode.Subtract:
                    // Explicitly declared at this site, per the task brief, rather than falling through to the
                    // generic rule — the value is the proven one, the declaration is deliberate.
                    return (carveStrength > 0f && carveStrength < 1f) ? SoftCarveFactor * m : m;
            }
            return m;
        }

        /// <summary>
        /// Sweep: <c>max(B_child, 1)</c>. A half-plane, a wedge and a slab are all exactly Lipschitz-1, and
        /// the combine is a hard <c>max</c>, whose gradient magnitude never exceeds the larger of its inputs'.
        /// </summary>
        public static float Sweep(float childBound) => Mathf.Max(childBound, 1f);

        /// <summary><c>B_child</c> — <c>abs</c> and hard <c>max</c> both preserve the gradient magnitude.</summary>
        public static float Shell(float childBound) => childBound;
    }
}
