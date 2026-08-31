using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// A 2×3 affine map, <c>x' = m00·x + m01·y + m02</c>, <c>y' = m10·x + m11·y + m12</c>.
    /// The shape tree's transform is strictly 2D affine (R2); pitch/yaw bake a perspective scalar and belong
    /// to the extrusion/resolve stage, not here.
    /// </summary>
    [Serializable]
    public struct ShaperMatrix
    {
        public float m00, m01, m02;
        public float m10, m11, m12;

        public static ShaperMatrix Identity => new ShaperMatrix { m00 = 1f, m11 = 1f };

        public static ShaperMatrix Translate(float tx, float ty)
            => new ShaperMatrix { m00 = 1f, m11 = 1f, m02 = tx, m12 = ty };

        /// <summary>Counter-clockwise by <paramref name="radians"/> — the frame is +Y up.</summary>
        public static ShaperMatrix Rotate(float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new ShaperMatrix { m00 = c, m01 = -s, m10 = s, m11 = c };
        }

        public static ShaperMatrix Scale(float sx, float sy)
            => new ShaperMatrix { m00 = sx, m11 = sy };

        /// <summary>Shear by the two <i>tangents</i> (not angles): <c>[[1, kx], [ky, 1]]</c>.</summary>
        public static ShaperMatrix Skew(float kx, float ky)
            => new ShaperMatrix { m00 = 1f, m01 = kx, m10 = ky, m11 = 1f };

        /// <summary>Composition: the result applies <paramref name="b"/> first, then <paramref name="a"/>.</summary>
        public static ShaperMatrix Mul(in ShaperMatrix a, in ShaperMatrix b)
        {
            return new ShaperMatrix
            {
                m00 = a.m00 * b.m00 + a.m01 * b.m10,
                m01 = a.m00 * b.m01 + a.m01 * b.m11,
                m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02,
                m10 = a.m10 * b.m00 + a.m11 * b.m10,
                m11 = a.m10 * b.m01 + a.m11 * b.m11,
                m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12,
            };
        }

        public float Determinant => m00 * m11 - m01 * m10;

        /// <summary>Inverse. Returns false (and identity) when the linear part is singular; never divides by zero.</summary>
        public bool TryInvert(out ShaperMatrix inverse)
        {
            float det = Determinant;
            if (Mathf.Abs(det) < 1e-12f) { inverse = Identity; return false; }
            float inv = 1f / det;
            float a = m11 * inv, b = -m01 * inv, c = -m10 * inv, d = m00 * inv;
            inverse = new ShaperMatrix
            {
                m00 = a, m01 = b, m02 = -(a * m02 + b * m12),
                m10 = c, m11 = d, m12 = -(c * m02 + d * m12),
            };
            return true;
        }

        /// <summary>
        /// The singular values of the 2×2 linear part, in closed form — no iterative SVD.
        /// For <c>[[a,b],[c,d]]</c> with <c>E=(a+d)/2, F=(a−d)/2, G=(c+b)/2, H=(c−b)/2</c>:
        /// <c>σ_max = hypot(E,H) + hypot(F,G)</c>, <c>σ_min = |hypot(E,H) − hypot(F,G)|</c>.
        /// </summary>
        public void SingularValues(out float sigmaMin, out float sigmaMax)
        {
            float e = (m00 + m11) * 0.5f;
            float f = (m00 - m11) * 0.5f;
            float g = (m10 + m01) * 0.5f;
            float h = (m10 - m01) * 0.5f;
            float p = Mathf.Sqrt(e * e + h * h);
            float q = Mathf.Sqrt(f * f + g * g);
            sigmaMax = p + q;
            sigmaMin = Mathf.Abs(p - q);
        }

        /// <summary>
        /// The smallest singular value of the linear part — the factor a child's local distance is multiplied
        /// by on the way back up, so every node publishes in canvas pixels (R2).
        ///
        /// This one multiplication is why a transform composes its child's bound <b>unchanged</b>: with
        /// <c>f_parent(p) = σ_min · f_child(M⁻¹(p − t))</c>,
        /// <c>|∇f_parent| = σ_min · |∇f_child · M⁻¹| ≤ σ_min · L · σ_max(M⁻¹) = σ_min · L / σ_min = L</c>.
        /// The reference app omits the rescale entirely and consequently over-reports 5.00× at
        /// <c>scale.x = 0.2</c>.
        /// </summary>
        public float SigmaMin { get { SingularValues(out float lo, out _); return lo; } }

        /// <summary>The largest singular value. Used only for support-extent boxes, never for the distance rescale.</summary>
        public float SigmaMax { get { SingularValues(out _, out float hi); return hi; } }
    }

    /// <summary>
    /// The transform block, exactly R2: translate, rotate, scale, skew, <b>origin</b>.
    /// Composed forward as <c>M = T · P · R · S · K · P⁻¹</c>, where <c>P</c> translates to
    /// <see cref="origin"/>. Points travel <i>down</i> the tree by the inverse only; nothing is ever
    /// resampled into a buffer.
    ///
    /// <see cref="origin"/> being an authored field is the structural fix for the class of bug where a warp
    /// pivots at the top-right corner. It is expressed as a point in the node's own local units (0,0 = the
    /// node's own centre), not as a 0..1 fraction of an extent, so it needs no knowledge of the node's size.
    /// </summary>
    [Serializable]
    public class ShaperTransformBlock
    {
        public Vector2 translate = Vector2.zero;
        /// <summary>Degrees, counter-clockwise (+Y up).</summary>
        public float rotation = 0f;
        public Vector2 scale = Vector2.one;
        /// <summary>Degrees, clamped to ±89 before being taken through <c>tan</c>.</summary>
        public Vector2 skewDegrees = Vector2.zero;
        /// <summary>Pivot, in the node's own local units.</summary>
        public Vector2 origin = Vector2.zero;

        /// <summary>The forward map, <c>T · P · R · S · K · P⁻¹</c>.</summary>
        public ShaperMatrix ToMatrix()
        {
            float kx = Mathf.Tan(Mathf.Clamp(skewDegrees.x, -89f, 89f) * Mathf.Deg2Rad);
            float ky = Mathf.Tan(Mathf.Clamp(skewDegrees.y, -89f, 89f) * Mathf.Deg2Rad);

            ShaperMatrix m = ShaperMatrix.Translate(translate.x, translate.y);
            m = ShaperMatrix.Mul(m, ShaperMatrix.Translate(origin.x, origin.y));
            m = ShaperMatrix.Mul(m, ShaperMatrix.Rotate(rotation * Mathf.Deg2Rad));
            m = ShaperMatrix.Mul(m, ShaperMatrix.Scale(scale.x, scale.y));
            m = ShaperMatrix.Mul(m, ShaperMatrix.Skew(kx, ky));
            m = ShaperMatrix.Mul(m, ShaperMatrix.Translate(-origin.x, -origin.y));
            return m;
        }
    }
}
