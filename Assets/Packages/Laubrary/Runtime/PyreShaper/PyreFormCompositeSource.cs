using System;
using Laubrary.Pyre;
using Laubrary.Shaper;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0112 — hosts an existing <see cref="PyreForm"/>, UNMODIFIED, behind Shaper's
    /// <see cref="IShaperCompositeSource"/>. This is the whole bridge: everything either side needs to know about
    /// the other lives in this one class, in the <c>Runtime/PyreShaper/</c> asmdef named for the two systems it
    /// connects (the ZoetropePyre/ZoetropeLaunimator convention). Neither <c>Runtime/Shaper</c> nor
    /// <c>Runtime/Pyre</c> references the other directly, and this task adds no such reference — only this
    /// bridge asmdef depends on both.
    ///
    /// <b>What "unmodified" means here, precisely.</b> The form's own class — <c>OrbForm</c>, <c>TorchForm</c>,
    /// any of the nine — is not touched, not subclassed, not wrapped in a way that changes its behaviour. This
    /// adapter only supplies the <see cref="PyreFormCtx"/> a whole-layer form already expects and calls its own
    /// public <c>Prepare</c>/<c>Render</c>, exactly the sequence <c>PyreRenderer.RenderFormLayer</c> already uses
    /// (<c>PyreRenderer.cs:291,342-345</c>).
    ///
    /// <b>T-0167 — dials now animate over the Shaper phase.</b> A hosted form's <c>ZUIValue</c> dials (Curve-over-
    /// life, MinMax-by-hash, Steps) resolve through <see cref="PyreShaperEval.Eval"/> at this frame's phase, the
    /// same four-mode switch <c>PyreRenderer.Eval</c> applies inside a real Pyre layer, keyed off the same
    /// (seed, particleIndex, fieldId, layerSalt) tuple so a MinMax dial draws from the identical RNG stream a real
    /// hosted layer would produce. <c>particleIndex</c> is <see cref="PyreRenderer.ModParticleIndex"/> (-1) — the
    /// same whole-layer sentinel <c>RenderFormLayer</c> passes a form's own Prepare — and <c>layerSalt</c> is a
    /// fixed 0: a composite node hosts exactly one form, so there is no sibling layer to decorrelate from.
    /// </summary>
    [Serializable]
    public sealed class PyreFormCompositeSource : IShaperCompositeSource, IShaperCacheableSource
    {
        [SerializeReference] public PyreForm form;

        // A composite node hosts exactly one form — no sibling layer whose RNG stream this one could collide
        // with — so the salt is a fixed constant rather than a per-instance field. Kept as a named constant (not
        // inlined) because every Eval call below and ContentHash must agree on it.
        const int LayerSalt = 0;

        public string SourceLabel => form != null ? form.DisplayName : "(no form assigned)";

        public void Render(int width, int height, float phase01, uint seed, Color32[] target)
        {
            if (form == null || target == null) return;

            float life = Mathf.Clamp01(phase01);
            int sd = unchecked((int)seed);

            Func<ZUIValue, int, float> evalRaw = (v, fid) =>
                PyreShaperEval.Eval(v, life, sd, PyreRenderer.ModParticleIndex, fid, LayerSalt);
            form.Prepare(new PyreFormPrepareCtx(life, sd, LayerSalt, evalRaw));

            Func<ZUIValue, int, float, float> evalAtLife = (v, fid, atLife) =>
                PyreShaperEval.Eval(v, atLife, sd, PyreRenderer.ModParticleIndex, fid, LayerSalt);
            var ctx = new PyreFormCtx(width, height, life, sd, LayerSalt, null, 1f,
                                      null, null, null, 0f, 0, 1, evalAtLife);
            form.Render(ctx, target);
        }

        /// <summary>
        /// T-0167 — <see cref="IShaperCacheableSource"/>: folds the form's own <c>ContentHash()</c>
        /// (<c>PyreForm.cs:290-306</c>, reflects every serialized dial) into the key so editing a dial IN PLACE —
        /// dragging a Curve point, retyping a MinMax bound — invalidates the composite's cache at the SAME phase,
        /// not just when phase changes. Without this the node falls back to
        /// <c>ShaperNodeIdentity.SourceContentHash</c>'s reference-identity path, which only notices a
        /// reassigned form reference — exactly the staleness gap that method's own doc names (SPEC.md Part 5).
        /// <c>phase01</c>/<c>seed</c> are NOT mixed here: <c>ShaperNodeIdentity.MixCommon</c> already folds both
        /// into every node's key unconditionally before calling this (<c>ShaperNodeIdentity.cs:77-78</c>), so
        /// mixing them a second time here would be redundant, not incorrect.
        /// </summary>
        public ShaperCacheKey ContentHash()
        {
            var m = ShaperCacheMixer.Begin("shaper.pyreshaper.pyreformcompositesource.v1");
            m.MixBool(form != null);
            if (form != null) m.MixInt(form.ContentHash());
            return m.Key;
        }
    }
}
