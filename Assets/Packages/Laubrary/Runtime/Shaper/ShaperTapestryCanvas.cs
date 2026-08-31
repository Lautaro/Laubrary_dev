using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0111 — the toroidal (wrap-tileable) noise primitives ported from Kiln's
    /// <c>tapsurface/canvas.py</c>, restricted to the subset that is a PURE FUNCTION of a single
    /// sample point: <c>hash_cell</c>, <c>wrapped_value_noise</c> and <c>fbm</c>.
    ///
    /// <b>Scope cut, and why it is principled rather than a shortcut.</b> The Python module also has
    /// a fourth primitive, <c>gradient(field)</c>, that finite-differences a WHOLE materialised field
    /// via <c>np.roll</c> — i.e. it reads neighbouring samples. Shaper's fill contract forbids exactly
    /// that: <c>ShaperFillOps.FillTile</c>'s own doc comment states a fill "reads only its OWN sample —
    /// never <c>[i−1]</c> or <c>[i+width]</c>", and BC-2.1/BC-1.6 name this as structural (tile
    /// independence), not a style rule. <c>gradient()</c> is therefore NOT ported: there is no
    /// per-sample expression of "the neighbour's value" available to a fill's <c>Sample()</c>. Nothing
    /// in the ported <c>steel</c> slice (<see cref="ShaperFillOps"/>'s <c>TapestrySteel</c> case) needs
    /// it — the reference used <c>gradient</c> to build a screen-space SURFACE NORMAL for its own GGX
    /// shading pass, and that whole shading pass is out of a FILL's jurisdiction under Shaper's own
    /// architecture (B4: "a fill emits albedo … shine belongs to the lights, not to the paint").
    ///
    /// Likewise <c>uv(size)</c> is not ported: Shaper already has a continuous per-sample anchor
    /// coordinate (<see cref="ShaperFillOps.Anchor"/>), and every method here takes that coordinate
    /// directly rather than an array index.
    ///
    /// <b>Verified against the Python originals, not merely transcribed.</b> <c>HashCell</c>'s integer
    /// arithmetic is checked to be bit-exact with <c>tapsurface.canvas.hash_cell</c>'s numpy uint64
    /// masked ops: Python sums four terms in uint64 (no overflow — the terms are far under 2^64) and
    /// masks the SUM to 32 bits once; C#'s plain <c>uint</c> arithmetic wraps mod 2^32 at every
    /// intermediate add/multiply. Those two are the SAME value by the modular-arithmetic identity
    /// <c>(a+b+c+d) mod 2^32 == ((a mod 2^32)+(b mod 2^32)+(c mod 2^32)+(d mod 2^32)) mod 2^32</c> — so
    /// no explicit masking is written here; C#'s default unchecked <c>uint</c> overflow IS the mask.
    /// Cross-checked numerically against a live Python run — see
    /// <c>D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0111\VERIFICATION.md</c> Part 2 for the measured
    /// sample points and the exact agreement.
    /// </summary>
    public static class ShaperTapestryCanvas
    {
        /// <summary>
        /// Bit-exact port of <c>tapsurface.canvas.hash_cell</c> (and <c>tapshape.canvas.SdfCanvas.hash_cell</c>,
        /// documented there as the same function). A wrapped cell index gets the SAME value as its
        /// "home" cell — the whole reason this is deterministic-by-index rather than positional — which
        /// is what makes <see cref="WrappedValueNoiseAt"/> tile with no seam at the wrap boundary.
        /// </summary>
        public static float HashCell(int cx, int cy, uint seed, uint salt)
        {
            uint h = unchecked((uint)cx * 374761393u + (uint)cy * 668265263u +
                               seed * 1103515245u + salt * 2032854233u);
            h = unchecked((h ^ (h >> 13)) * 1274126177u);
            h ^= h >> 16;
            return (h & 0x7FFFFFFFu) / (float)0x7FFFFFFFu;
        }

        /// <summary>Python's <c>%</c> on a non-negative modulus always returns a non-negative result; C#'s <c>%</c> does not for a negative dividend. This is that correction, needed because <paramref name="cells"/> callers may hand a UV that wrapped below 0.</summary>
        static int PyMod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        /// <summary>
        /// Point-sampled port of <c>tapsurface.canvas.wrapped_value_noise</c>: the same tileable value
        /// noise (hashed grid corners, smoothstep-interpolated), evaluated at ONE continuous point
        /// rather than materialised over a whole array — the shape every Shaper fill sample needs,
        /// since <c>Sample()</c> is called once per sample with no neighbour access (BC-1.6).
        /// </summary>
        /// <param name="u">Toroidal U, expected already wrapped into [0,1) by the caller.</param>
        /// <param name="v">Toroidal V, expected already wrapped into [0,1) by the caller.</param>
        /// <param name="cells">Grid resolution this octave hashes at. Must be >= 1.</param>
        public static float WrappedValueNoiseAt(float u, float v, int cells, uint seed, uint salt)
        {
            if (cells < 1) cells = 1;
            float gx = u * cells;
            float gy = v * cells;
            int x0 = PyMod(Mathf.FloorToInt(gx), cells);
            int y0 = PyMod(Mathf.FloorToInt(gy), cells);
            int x1 = (x0 + 1) % cells;
            int y1 = (y0 + 1) % cells;
            float tx = gx - Mathf.Floor(gx);
            float ty = gy - Mathf.Floor(gy);

            float v00 = HashCell(x0, y0, seed, salt);
            float v10 = HashCell(x1, y0, seed, salt);
            float v01 = HashCell(x0, y1, seed, salt);
            float v11 = HashCell(x1, y1, seed, salt);

            float sx = tx * tx * (3f - 2f * tx);
            float sy = ty * ty * (3f - 2f * ty);
            float top = v00 * (1f - sx) + v10 * sx;
            float bot = v01 * (1f - sx) + v11 * sx;
            return top * (1f - sy) + bot * sy;
        }

        /// <summary>
        /// Point-sampled port of <c>tapsurface.canvas.fbm</c>: sum of <see cref="WrappedValueNoiseAt"/>
        /// at doubling frequencies (one octave's <c>salt</c> is its octave index, matching the Python
        /// <c>salt=o</c>), normalised by the accumulated amplitude. Roughly 0..1, not hard-clamped —
        /// same as the Python original.
        /// </summary>
        public static float Fbm(float u, float v, int baseCells, int octaves, uint seed, float persistence)
        {
            if (octaves < 1) octaves = 1;
            int cells = baseCells < 1 ? 1 : baseCells;
            float total = 0f, amp = 1f, ampSum = 0f;
            for (int o = 0; o < octaves; o++)
            {
                total += WrappedValueNoiseAt(u, v, cells, seed, (uint)o) * amp;
                ampSum += amp;
                amp *= persistence;
                cells *= 2;
            }
            return total / Mathf.Max(ampSum, 1e-9f);
        }
    }
}
