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
    /// <b>Named, honest simplification: dials resolve to their STATIC value only.</b> A hosted form's
    /// <c>ZUIValue</c> dials (Curve-over-life, MinMax-by-hash) are not driven through
    /// <c>PyreRenderer</c>'s deterministic <c>Eval</c> funnel — that funnel is PyreRenderer-internal and wiring
    /// it fully into Shaper's own compile pass is out of this task's scope. A dial authored as a plain Static
    /// value renders identically to hosting it inside Pyre; a dial authored with a Curve or MinMax variance
    /// renders as if it were Static at that field's own default. This is the same posture
    /// <see cref="PyreFormPrepareCtx"/>'s own null-<c>evalRaw</c> fallback already takes for a hand-built ctx.
    /// </summary>
    [Serializable]
    public sealed class PyreFormCompositeSource : IShaperCompositeSource
    {
        [SerializeReference] public PyreForm form;

        public string SourceLabel => form != null ? form.DisplayName : "(no form assigned)";

        public void Render(int width, int height, float phase01, uint seed, Color32[] target)
        {
            if (form == null || target == null) return;

            float life = Mathf.Clamp01(phase01);
            int sd = unchecked((int)seed);

            // Static-value-only resolution — see the class doc's "named, honest simplification".
            Func<ZUIValue, int, float> evalRaw = (v, fid) => v != null ? v.staticValue : 0f;
            form.Prepare(new PyreFormPrepareCtx(life, sd, 0, evalRaw));

            var ctx = new PyreFormCtx(width, height, life, sd, 0, null, 1f,
                                      null, null, null, 0f, 0, 1, null);
            form.Render(ctx, target);
        }
    }
}
