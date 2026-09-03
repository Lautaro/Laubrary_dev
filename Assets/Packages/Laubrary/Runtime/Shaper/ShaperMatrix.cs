using System;
using UnityEngine;
using UnityEngine.Serialization;

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

        /// <summary>Maps a point through this affine map. Exists so a caller that only has a matrix and a
        /// point (the preview stage placing the pivot cross, T-0220) does not have to hand-inline the two
        /// dot products every time — <c>m02</c>/<c>m12</c> alone only ever gives the image of (0,0).</summary>
        public Vector2 TransformPoint(Vector2 p) => new Vector2(m00 * p.x + m01 * p.y + m02, m10 * p.x + m11 * p.y + m12);

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
    public class ShaperTransformBlock : ISerializationCallbackReceiver
    {
        // Field ids, so each dial's Min-Max mode draws its own uncorrelated sample rather than every dial on a
        // node drawing the same one. Same discipline the star's dials already use (ShaperPrimitives.cs).
        const uint FldTranslateX = 0x7A00_0001u, FldTranslateY = 0x7A00_0002u;
        const uint FldRotation   = 0x7A00_0003u;
        const uint FldScaleX     = 0x7A00_0004u, FldScaleY     = 0x7A00_0005u;
        const uint FldSkewX      = 0x7A00_0006u, FldSkewY      = 0x7A00_0007u;
        const uint FldOriginX    = 0x7A00_0008u, FldOriginY    = 0x7A00_0009u;

        public ZUIValue translateX = new ZUIValue(0f);
        public ZUIValue translateY = new ZUIValue(0f);
        /// <summary>Degrees, counter-clockwise (+Y up).</summary>
        public ZUIValue rotationDegrees = new ZUIValue(0f);
        public ZUIValue scaleX = new ZUIValue(1f);
        public ZUIValue scaleY = new ZUIValue(1f);
        /// <summary>Degrees, clamped to ±89 before being taken through <c>tan</c>.</summary>
        public ZUIValue skewX = new ZUIValue(0f);
        public ZUIValue skewY = new ZUIValue(0f);
        /// <summary>Pivot, in the node's own local units.</summary>
        public ZUIValue originX = new ZUIValue(0f);
        public ZUIValue originY = new ZUIValue(0f);

        /// <summary>
        /// A per-instance offset the swarm compiler folds in around the authored dials, expressed as a DELTA so
        /// that zero is the identity — which is what makes it safe to leave un-serialized: whatever state a
        /// runtime deserializer leaves these in, "nothing set" and "no jitter" are the same thing. It sits here
        /// rather than being written onto the authored dials because writing a jittered number onto a dial
        /// would have to overwrite its static value, which silently discards an authored Curve.
        /// </summary>
        [NonSerialized] public Vector2 instanceTranslate;
        [NonSerialized] public float instanceRotationDegrees;
        /// <summary>Scale multiplier expressed as <c>1 + bias</c>, so 0 is the identity.</summary>
        [NonSerialized] public float instanceScaleBias;

        // ── the pre-promotion storage, read once to seed the dials above ─────────────────────────────────
        // A document authored before the transform was animatable stores five plain numbers under these five
        // names; keeping the fields (renamed, with the old names declared) is what lets that document open with
        // its authored position, rotation, scale, skew and pivot intact.
        [SerializeField, FormerlySerializedAs("translate")]   Vector2 legacyTranslate = Vector2.zero;
        [SerializeField, FormerlySerializedAs("rotation")]    float legacyRotation = 0f;
        [SerializeField, FormerlySerializedAs("scale")]       Vector2 legacyScale = Vector2.one;
        [SerializeField, FormerlySerializedAs("skewDegrees")] Vector2 legacySkew = Vector2.zero;
        [SerializeField, FormerlySerializedAs("origin")]      Vector2 legacyOrigin = Vector2.zero;

        /// <summary>
        /// False in exactly one situation — data written before the dials existed. It is stamped true on the way
        /// OUT of serialization rather than defaulted true, because that is the only stamp an older file cannot
        /// have: anything this build saves carries it, anything older cannot, and no heuristic over the values
        /// themselves is needed to tell the two apart.
        /// </summary>
        [SerializeField] bool dialsPromoted;

        /// <summary>Static-value view of <see cref="translateX"/>/<see cref="translateY"/>, for code that
        /// positions a node outright rather than authoring an envelope.</summary>
        public Vector2 translate
        {
            get => new Vector2(Stat(translateX), Stat(translateY));
            set { SetStat(ref translateX, value.x); SetStat(ref translateY, value.y); }
        }

        public float rotation
        {
            get => Stat(rotationDegrees);
            set => SetStat(ref rotationDegrees, value);
        }

        public Vector2 scale
        {
            get => new Vector2(Stat(scaleX, 1f), Stat(scaleY, 1f));
            set { SetStat(ref scaleX, value.x); SetStat(ref scaleY, value.y); }
        }

        public Vector2 skewDegrees
        {
            get => new Vector2(Stat(skewX), Stat(skewY));
            set { SetStat(ref skewX, value.x); SetStat(ref skewY, value.y); }
        }

        public Vector2 origin
        {
            get => new Vector2(Stat(originX), Stat(originY));
            set { SetStat(ref originX, value.x); SetStat(ref originY, value.y); }
        }

        static float Stat(ZUIValue v, float fallback = 0f) => v != null ? v.staticValue : fallback;

        static void SetStat(ref ZUIValue v, float value)
        {
            if (v == null) v = new ZUIValue(value);
            else v.staticValue = value;
        }

        public void OnBeforeSerialize() => dialsPromoted = true;

        public void OnAfterDeserialize()
        {
            if (dialsPromoted) { EnsureDials(); return; }
            translateX = new ZUIValue(legacyTranslate.x);
            translateY = new ZUIValue(legacyTranslate.y);
            rotationDegrees = new ZUIValue(legacyRotation);
            scaleX = new ZUIValue(legacyScale.x);
            scaleY = new ZUIValue(legacyScale.y);
            skewX = new ZUIValue(legacySkew.x);
            skewY = new ZUIValue(legacySkew.y);
            originX = new ZUIValue(legacyOrigin.x);
            originY = new ZUIValue(legacyOrigin.y);
            dialsPromoted = true;
        }

        /// <summary>A deserializer is free to hand back a null managed reference; every dial has to exist before
        /// anything samples it, and a missing one means its identity, not a crash.</summary>
        public void EnsureDials()
        {
            if (translateX == null) translateX = new ZUIValue(0f);
            if (translateY == null) translateY = new ZUIValue(0f);
            if (rotationDegrees == null) rotationDegrees = new ZUIValue(0f);
            if (scaleX == null) scaleX = new ZUIValue(1f);
            if (scaleY == null) scaleY = new ZUIValue(1f);
            if (skewX == null) skewX = new ZUIValue(0f);
            if (skewY == null) skewY = new ZUIValue(0f);
            if (originX == null) originX = new ZUIValue(0f);
            if (originY == null) originY = new ZUIValue(0f);
        }

        /// <summary>The pivot's own sampled position, in the node's local units — same phase/seed a
        /// <see cref="ToMatrix"/> call for the same frame would use. Exists so a caller that wants to know
        /// WHERE the pivot is (the preview stage's pivot cross, T-0220) can ask for exactly that instead of
        /// re-deriving it from the composed matrix, which is the general "T·origin·R·S·K·origin⁻¹ fixes
        /// origin" fact rather than something specific to drawing.</summary>
        public Vector2 SampleOrigin(float phase01, uint seed)
        {
            EnsureDials();
            return new Vector2(
                ShaperValue.Sample(originX, phase01, seed ^ FldOriginX, 0f),
                ShaperValue.Sample(originY, phase01, seed ^ FldOriginY, 0f));
        }

        /// <summary>The forward map, <c>T · P · R · S · K · P⁻¹</c>, with every dial sampled once at
        /// <paramref name="phase01"/> — which is what lets a node grow, spin or drift over the document's
        /// frames instead of holding one authored pose.</summary>
        public ShaperMatrix ToMatrix(float phase01, uint seed)
        {
            EnsureDials();

            float tx = ShaperValue.Sample(translateX, phase01, seed ^ FldTranslateX, 0f) + instanceTranslate.x;
            float ty = ShaperValue.Sample(translateY, phase01, seed ^ FldTranslateY, 0f) + instanceTranslate.y;
            float rot = ShaperValue.Sample(rotationDegrees, phase01, seed ^ FldRotation, 0f) + instanceRotationDegrees;
            float jitterScale = 1f + instanceScaleBias;
            float sx = ShaperValue.Sample(scaleX, phase01, seed ^ FldScaleX, 1f) * jitterScale;
            float sy = ShaperValue.Sample(scaleY, phase01, seed ^ FldScaleY, 1f) * jitterScale;
            float ox = ShaperValue.Sample(originX, phase01, seed ^ FldOriginX, 0f);
            float oy = ShaperValue.Sample(originY, phase01, seed ^ FldOriginY, 0f);

            float kx = Mathf.Tan(Mathf.Clamp(ShaperValue.Sample(skewX, phase01, seed ^ FldSkewX, 0f), -89f, 89f) * Mathf.Deg2Rad);
            float ky = Mathf.Tan(Mathf.Clamp(ShaperValue.Sample(skewY, phase01, seed ^ FldSkewY, 0f), -89f, 89f) * Mathf.Deg2Rad);

            ShaperMatrix m = ShaperMatrix.Translate(tx, ty);
            m = ShaperMatrix.Mul(m, ShaperMatrix.Translate(ox, oy));
            m = ShaperMatrix.Mul(m, ShaperMatrix.Rotate(rot * Mathf.Deg2Rad));
            m = ShaperMatrix.Mul(m, ShaperMatrix.Scale(sx, sy));
            m = ShaperMatrix.Mul(m, ShaperMatrix.Skew(kx, ky));
            m = ShaperMatrix.Mul(m, ShaperMatrix.Translate(-ox, -oy));
            return m;
        }
    }
}
