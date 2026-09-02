// ShaperEffectApplier — the bridge half of T-0156: what actually RUNS a document's authored effects.
//
// It lives here, in Laubrary.PyreShaper, rather than in Laubrary.Shaper, because this is the only assembly
// that can see both sides. Runtime/Shaper references nothing but ZuiRuntime and Pooling; the concrete
// modifiers are Laubrary.SpriteFx.PixelModifier and the classification table is this assembly's own
// ShaperEffectCatalog, and BOTH sit above Shaper in the dependency graph. So the document names an effect
// with a string and takes an IShaperEffectApplier; this class is the implementation the editor hands it.
//
// T-0163 rewrote what this does, and deleted a kernel rather than adding one. It used to run its own per-pixel
// loop over PixelModifier.ApplyPixel — which meant that of the 41 catalogued effects, only the twelve that
// happen to derive from PixelModifier could even be cast, and a geometry warp or a whole-frame pass (bloom,
// outline, drop shadow — half the reason anyone adds an effect) was silently dropped by an `as` returning
// null. SpriteFxStack.RunStack (Runtime/SpriteFx/SpriteFxBurst.cs:596) already dispatches every family in
// authored order, resolving each modifier's ZUIValue dials against the life it is handed. Calling it is what
// makes an effect list mean what it looks like, and it removes the second formula this file used to carry.
using System.Collections.Generic;
using Laubrary.Shaper;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// Runs an authored <see cref="ShaperEffectRef"/> list over a picture — a layer's own buffer at
    /// <see cref="ShaperEffectStage.PreComposite"/>, the document's folded picture at
    /// <see cref="ShaperEffectStage.PostComposite"/>. Stateless and cheap to construct.
    /// </summary>
    public sealed class ShaperEffectApplier : IShaperEffectApplier
    {
        /// <summary>A ready-to-use instance — the applier holds no per-document state.</summary>
        public static readonly ShaperEffectApplier Instance = new ShaperEffectApplier();

        /// <summary>
        /// Managed, not Burst. <see cref="SpriteFxStack.RunStack"/>'s two runners are documented as producing
        /// the same maths, but the Burst path copies the whole buffer into a <c>NativeArray</c> and back for
        /// every batch — a real cost per frame on a canvas this small, paid to accelerate a loop that is not the
        /// bottleneck. It is also one fewer thing that can differ between the preview and the bake.
        /// </summary>
        const bool UseBurst = false;

        /// <inheritdoc/>
        public void Apply(IReadOnlyList<ShaperEffectRef> effects, ShaperEffectStage stage,
                          Color32[] pixels, int width, int height, float phase01, uint seed)
        {
            if (effects == null || pixels == null || width <= 0 || height <= 0) return;
            int n = width * height;
            if (pixels.Length < n) return;

            // The list is built first, then run ONCE, because RunStack's batching is what makes a run of
            // consecutive colour effects a single pass — feeding it one modifier at a time would defeat that
            // and change nothing about the result.
            List<PyreModifier> mods = null;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null || !e.enabled) continue;

                // An entry whose type has gone, or which cannot run at THIS stage, is SKIPPED rather than
                // thrown on: a document must still render when one effect is missing or misplaced. The window
                // greys the same rows with the same reason, so what is skipped here is never a surprise there.
                var mod = (e.instance as ShaperModifierEffect)?.modifier;
                if (mod == null || !mod.enabled) continue;
                if (!ShaperEffectRuntime.CanRun(mod.GetType().Name, stage, out _)) continue;

                (mods ??= new List<PyreModifier>()).Add(mod);
            }
            if (mods == null) return;

            // `life` is the Shaper phase, so every ZUIValue dial inside these modifiers resolves at the frame
            // being rendered — RunStack Prepares each one through SpriteFxStack.LifeEval(life, seed), which is
            // why an animated Bloom radius pulses here instead of freezing at its static value.
            //
            // `frame` is a PHASE-DERIVED stamp, not the document's frame index, because this interface is
            // handed a phase and not a frame. Only the hash-per-pixel Post effects (Dissolve's erase/scatter
            // masks) read it, and all they need of it is that it is deterministic and distinct per frame —
            // which a fixed quantisation of the phase is, since phase is a fixed function of the frame
            // (ShaperClock, i/(N-1)). Scrubbing back to a frame therefore reproduces its picture exactly.
            int frame = Mathf.RoundToInt(Mathf.Clamp01(phase01) * 1000f);
            SpriteFxStack.RunStack(pixels, width, height, mods, frame, phase01, unchecked((int)seed), UseBurst);
        }
    }
}
