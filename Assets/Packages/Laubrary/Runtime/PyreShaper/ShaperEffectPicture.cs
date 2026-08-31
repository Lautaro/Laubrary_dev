using System;
using System.Reflection;
using Laubrary.Shaper;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0114 — precondition 2 (a padded buffer) and precondition 3 (the picture-rect call actually made) of
    /// SHAPER_THE_DESIGN.md C8, built together because #3 only becomes load-bearing once #2 exists (P1-digest.md
    /// D4: "harmless today because nothing pads. It becomes live the instant effects are allowed to draw outside
    /// the shape... so it has to be fixed in the same change, not afterwards").
    /// </summary>
    public static class ShaperEffectPicture
    {
        // ── the dormant picture-rect call, wired for real ───────────────────────────────────────────────────
        // PostModifier.SetPicture(int,int,int,int) is internal to Laubrary.SpriteFx. That assembly grants
        // InternalsVisibleTo ONLY to "com.Lautaro-Arino.Laubrary.Pyre" (SpriteFx/AssemblyInfo.cs) — Runtime/
        // PyreShaper has no such grant, and PyreRenderer.cs itself calls the sibling hooks (SetLife/SetSeed/
        // SetFrameIndex) by reflection anyway rather than relying on that grant (PyreRenderer.cs:1372-1391,
        // "Pyre drives the same hooks by reflection... so it needs no grant here" per SpriteFx's own comment).
        // This mirrors that exact, already-established pattern rather than widening SpriteFx's public API or
        // adding a second assembly grant for one call.
        static MethodInfo _setPicture;
        static bool TrySetPicture(PostModifier post, int pictureW, int pictureH, int padX, int padY)
        {
            if (_setPicture == null)
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                _setPicture = typeof(PostModifier).GetMethod("SetPicture", F);
            }
            if (_setPicture == null) return false;   // reflection failed → caller falls back to "picture == buffer"
            _setPicture.Invoke(post, new object[] { pictureW, pictureH, padX, padY });
            return true;
        }

        /// <summary>
        /// The padding a chain of effects needs, in pixels — the Shaper-side use of the SAME
        /// <c>PyreModifier.OutwardReachPx()</c> dial the standalone SpriteFx Stack already sums per-modifier
        /// (I-effect-universality.md bucket (ii): "run on a padded buffer sized by OutwardReach"). An effect
        /// that never overrides it contributes 0, matching the base class's own identity default.
        /// </summary>
        public static int RequiredPadding(PyreModifier[] chain)
        {
            if (chain == null) return 0;
            int total = 0;
            foreach (var m in chain)
                if (m != null && m.enabled) total += Mathf.Max(0, m.OutwardReachPx());
            return total;
        }

        /// <summary>
        /// Renders <paramref name="source"/> at its native <paramref name="width"/>×<paramref name="height"/>,
        /// blits it into a buffer padded by <paramref name="padX"/>/<paramref name="padY"/> pixels per side
        /// (fully transparent margin), then runs <paramref name="postChain"/> over that padded buffer — calling
        /// <see cref="TrySetPicture"/> on EVERY post modifier immediately before its own <c>Apply</c>, exactly the
        /// way <c>SpriteFxBurst.cs:656</c> already does for the standalone stack (the one pre-existing call site
        /// in the whole project). With <c>padX == padY == 0</c> this is byte-identical to running the chain on
        /// the unpadded buffer directly — the same "no padding, no behaviour change" contract
        /// <c>SfxKernels.MakePixel</c>'s own padded overload (<c>SpriteFxBurst.cs</c>) already promises.
        /// </summary>
        public static Color32[] RenderPadded(IShaperCompositeSource source, int width, int height,
                                              int padX, int padY, float phase01, uint seed,
                                              PostModifier[] postChain, out int paddedW, out int paddedH)
        {
            paddedW = width + 2 * padX;
            paddedH = height + 2 * padY;
            var padded = new Color32[paddedW * paddedH];

            var inner = new Color32[width * height];
            source.Render(width, height, phase01, seed, inner);
            for (int y = 0; y < height; y++)
                Array.Copy(inner, y * width, padded, (y + padY) * paddedW + padX, width);

            if (postChain != null)
            {
                // Static-value-only resolution, the same "named, honest simplification" PyreFormCompositeSource
                // already takes for a hosted PyreForm's own dials — a real caller driving an authored timeline
                // would pass a life-aware eval instead; this bridge does not yet wire one (out of scope, named).
                Func<ZUIValue, int, float> evalRaw = (v, fieldId) => v != null ? v.staticValue : 0f;
                foreach (var post in postChain)
                {
                    if (post == null || !post.enabled) continue;
                    post.Prepare(evalRaw);
                    TrySetPicture(post, width, height, padX, padY);
                    post.Apply(padded, paddedW, paddedH);
                }
            }

            return padded;
        }
    }
}
