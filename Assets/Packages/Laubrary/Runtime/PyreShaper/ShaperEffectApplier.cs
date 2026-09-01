// ShaperEffectApplier — the bridge half of T-0156: what actually RUNS a document's authored effects.
//
// It lives here, in Laubrary.PyreShaper, rather than in Laubrary.Shaper, because this is the only assembly
// that can see both sides. Runtime/Shaper references nothing but ZuiRuntime and Pooling; the concrete
// modifiers are Laubrary.SpriteFx.PixelModifier and the classification table is this assembly's own
// ShaperEffectCatalog, and BOTH sit above Shaper in the dependency graph. So the document names an effect
// with a string and takes an IShaperEffectApplier; this class is the implementation the editor hands it.
//
// The per-pixel kernel below is deliberately NOT a second formula: it is the same construction
// ShaperEffectStageRunner.ApplyInPlace already uses (SfxKernels.MakePixel for the PixelInfo, and a dropped
// pixel erased to fully transparent, matching BlastRenderer.ApplyPix's own convention). Two ways of running
// one modifier would be two things to keep in agreement, and the whole point of T-0114's stage work was that
// the difference between stages is REAL while the kernel itself must not vary.
using System;
using System.Collections.Generic;
using Laubrary.Shaper;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// Runs a <see cref="ShaperDocument"/>'s authored <see cref="ShaperEffectRef"/> list over a finished
    /// picture. Stateless and cheap to construct; the type cache below is shared and built once.
    /// </summary>
    public sealed class ShaperEffectApplier : IShaperEffectApplier
    {
        /// <summary>A ready-to-use instance — the applier holds no per-document state.</summary>
        public static readonly ShaperEffectApplier Instance = new ShaperEffectApplier();

        static Dictionary<string, Type> _byName;

        /// <summary>
        /// Resolve a catalog <c>typeName</c> to a concrete <see cref="PixelModifier"/> type.
        ///
        /// Built by scanning the assembly <see cref="PixelModifier"/> itself lives in, rather than by
        /// <c>Type.GetType</c> on an assembly-qualified string: the authored document stores the SHORT name
        /// (exactly <c>ShaperEffectCatalogEntry.typeName</c>), which keeps the asset free of assembly
        /// identity — a document must not stop resolving because an assembly was renamed or a type moved
        /// namespace.
        /// </summary>
        static Type Resolve(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            if (_byName == null)
            {
                _byName = new Dictionary<string, Type>(StringComparer.Ordinal);
                foreach (var t in typeof(PixelModifier).Assembly.GetTypes())
                {
                    if (t.IsAbstract || !typeof(PixelModifier).IsAssignableFrom(t)) continue;
                    _byName[t.Name] = t;
                }
            }
            return _byName.TryGetValue(typeName, out var found) ? found : null;
        }

        /// <inheritdoc/>
        public void Apply(IReadOnlyList<ShaperEffectRef> effects, ShaperEffectStage stage,
                          Color32[] pixels, int width, int height, float phase01, uint seed)
        {
            if (effects == null || pixels == null || width <= 0 || height <= 0) return;
            int n = width * height;
            if (pixels.Length < n) return;

            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null || !e.enabled || e.stage != stage) continue;

                // An unresolvable or unavailable entry is SKIPPED, never thrown on. A document that names an
                // effect whose type has gone must still render — the alternative is one missing modifier
                // taking the whole picture down, which is a far worse failure than one absent effect.
                var type = Resolve(e.typeName);
                if (type == null) continue;

                var modifier = Activator.CreateInstance(type) as PixelModifier;
                if (modifier == null || !modifier.enabled) continue;

                ApplyInPlace(pixels, width, height, phase01, seed, modifier);
            }
        }

        /// <summary>
        /// The same per-pixel pass as <see cref="ShaperEffectStageRunner"/>'s, kept identical on purpose — see
        /// this file's header. A pixel the effect drops is erased to fully transparent.
        /// </summary>
        static void ApplyInPlace(Color32[] buf, int W, int H, float life, uint seed, PixelModifier effect)
        {
            effect.Prepare((v, fieldId) => v != null ? v.staticValue : 0f);
            int sd = unchecked((int)seed);
            for (int y = 0, idx = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++, idx++)
                {
                    var s = buf[idx];
                    if (s.a == 0) continue;   // nothing to recolour or drop on an already-empty pixel
                    var col = new Color(s.r / 255f, s.g / 255f, s.b / 255f, s.a / 255f);
                    float a = col.a;
                    var info = SfxKernels.MakePixel(x, y, W, H, 0, life, sd);
                    bool keep = effect.ApplyPixel(ref col, ref a, info);
                    buf[idx] = keep ? (Color32)new Color(col.r, col.g, col.b, a) : new Color32(0, 0, 0, 0);
                }
            }
        }
    }
}
