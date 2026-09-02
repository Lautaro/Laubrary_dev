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

        /// The square canvas a form is hosted on when the bake box is not square. Reused across frames and
        /// grown on demand, the same shape <c>PyreSupersample</c>'s own scratch takes: a form is rendered once
        /// per compile, so a fresh allocation per frame would be pure garbage at canvas resolution.
        [NonSerialized] Color32[] _square;

        public void Render(int width, int height, float phase01, uint seed, Color32[] target)
        {
            if (form == null || target == null || width <= 0 || height <= 0) return;
            if (target.Length < width * height) return;

            float life = Mathf.Clamp01(phase01);
            int sd = unchecked((int)seed);

            Func<ZUIValue, int, float> evalRaw = (v, fid) =>
                PyreShaperEval.Eval(v, life, sd, PyreRenderer.ModParticleIndex, fid, LayerSalt);
            form.Prepare(new PyreFormPrepareCtx(life, sd, LayerSalt, evalRaw));

            Func<ZUIValue, int, float, float> evalAtLife = (v, fid, atLife) =>
                PyreShaperEval.Eval(v, atLife, sd, PyreRenderer.ModParticleIndex, fid, LayerSalt);

            // A PyreForm is written for PYRE'S canvas, and Pyre's canvas is SQUARE (Pyre.cs:1259-1260 — Width
            // and Height are both canvasSize). Several forms take that literally and build a square internal
            // field from one edge: ArcBurst sizes its plane S = min(W,H)·k and then box-filters S×S down into a
            // W×H target (ArcBurstForm.cs:454-456,520), which reads off the end of that plane the moment H > W,
            // and PlasmaBloom does the same (PlasmaBloomForm.cs:469). Shaper's canvas is free-form and its bake
            // box is now the canvas itself (ShaperCompositeDef.FitTo), so a 96×152 document handed the form a
            // rectangle it has no representation for. The bridge therefore hands every form the square canvas
            // its contract assumes and reads the node's box out of the middle — the same treatment a hosted
            // whole LAYER already gets (PyreLayerCompositeSource.cs:88-92), and for the same reason. The larger
            // edge is chosen so the picture is never shrunk to fit; what falls outside the box is discarded,
            // which is a framing choice the author can see rather than a silent rescale.
            int canvas = Mathf.Max(width, height);
            if (canvas == width && canvas == height)
            {
                // Square: the form draws straight into the node's own buffer, byte-for-byte what it did before.
                var square = new PyreFormCtx(width, height, life, sd, LayerSalt, null, 1f,
                                             null, null, null, 0f, 0, 1, evalAtLife);
                form.Render(square, target);
                return;
            }

            int n = canvas * canvas;
            if (_square == null || _square.Length < n) _square = new Color32[n];
            else Array.Clear(_square, 0, n);

            var ctx = new PyreFormCtx(canvas, canvas, life, sd, LayerSalt, null, 1f,
                                      null, null, null, 0f, 0, 1, evalAtLife);
            form.Render(ctx, _square);

            // Row 0 is the bottom in both conventions (ShaperCompositeDef.cs:49-52 states Shaper's, and it is
            // Pyre's own), so centring is a straight row-for-row window with no flip.
            int dx = (canvas - width) / 2, dy = (canvas - height) / 2;
            for (int y = 0; y < height; y++)
            {
                int sy = y + dy;
                if (sy < 0 || sy >= canvas) { Array.Clear(target, y * width, width); continue; }
                int srcRow = sy * canvas + dx;
                for (int x = 0; x < width; x++)
                {
                    int sx = dx + x;
                    target[y * width + x] = (sx < 0 || sx >= canvas) ? default : _square[srcRow + x];
                }
            }
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
