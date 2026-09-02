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
    ///
    /// <b>T-0202 — the three ctx arguments that used to be stubbed.</b> <c>RenderFormLayer</c> builds its ctx from
    /// the LAYER (<c>PyreRenderer.cs:292,341-344</c>): the layer's Shape Fill, the layer's Alpha envelope resolved
    /// at this life, and the spec's real frame index/count. This adapter passed <c>null, 1f, frame 0 of 1</c> for
    /// all three, and each stub was visible on screen:
    /// <list type="bullet">
    /// <item><b>Fill.</b> Only two of the nine forms read <c>ctx.fill</c> — Inferno (<c>InfernoForm.cs:190</c>) and
    /// Fork Blast (<c>ForkBlastForm.cs:189</c>), the two whose <c>UsesFill</c> is left at the base <c>true</c>
    /// (<c>PyreForm.cs:195</c>); the other seven override it to false and carry their own ramps. A null fill is
    /// exactly why those two, and only those two, came out white and grey.</item>
    /// <item><b>Alpha.</b> Seven of the nine multiply <c>ctx.alpha</c> into their output, so a constant 1 dropped
    /// the whole opacity envelope Pyre's Shape section shows beside the Fill row.</item>
    /// <item><b>Frames.</b> Several forms derive their ANIMATION from the frame index rather than from life —
    /// <c>OrbForm.cs:506</c> reads <c>ctx.frameIndex</c>/<c>ctx.frameCount</c> directly, and
    /// <c>PlasmaBloomForm.cs:417</c> sizes its clock fit from <c>ctx.frameCount</c>. Frozen at frame 0 of 1, Orb
    /// drew the same picture at every phase and Plasma's fit collapsed to its lower clamp, which is why neither
    /// appeared to respond to anything.</item>
    /// </list>
    /// The three are authored here, on the source, because a hosted form has no layer to read them off — the same
    /// place and the same reason <see cref="PyreLayerCompositeSource.frames"/> carries the frame count for a
    /// hosted layer. Their defaults are Pyre's own factory defaults, taken from a throwaway
    /// <see cref="PyreLayer"/> rather than retyped, so "Shaper at defaults" and "Pyre at defaults" are the same
    /// picture by construction and cannot drift apart when Pyre changes its defaults.
    /// </summary>
    [Serializable]
    public sealed class PyreFormCompositeSource : IShaperCompositeSource, IShaperCacheableSource
    {
        [SerializeReference] public PyreForm form;

        /// <summary>
        /// The form's colour source, exactly the row Pyre's Shape section draws for a hosted form
        /// (<c>PyreWindow.cs:1263-1266</c>) — and, like Pyre, only meaningful for a form whose
        /// <see cref="PyreForm.UsesFill"/> is true. A form with its own ramps never reads it.
        /// </summary>
        public ZuiFill shapeFill = DefaultFill();

        /// <summary>
        /// Overall opacity over the form's life, multiplied into the form's own output alpha — Pyre's Alpha row
        /// for a hosted form (<c>PyreWindow.cs:1267</c>), resolved here through the same envelope funnel every
        /// other hosted dial goes through.
        /// </summary>
        public ZUIValue alpha = DefaultAlpha();

        /// <summary>
        /// How many frames the hosted form thinks its animation spans. A form that animates off the frame INDEX
        /// (Orb, Plasma Bloom, Arc Burst, the Jet family) needs a real count to move at all, and Shaper hands a
        /// source a continuous phase rather than a frame — so the phase is mapped onto this many frames with
        /// <see cref="ShaperClock"/>'s own conversion read backwards. Same dial, same wording and same default as
        /// <see cref="PyreLayerCompositeSource.frames"/>, because it answers the same question.
        /// </summary>
        [Min(1)]
        [Tooltip("How many frames this generator's own animation spans. Match the document's Frames for exact "
               + "one-to-one playback; a smaller number plays the generator's whole life out sooner.")]
        public int frames = DefaultFrames;

        /// <summary>What a source authored before these fields existed falls back to, so an old document animates
        /// and paints on load instead of staying frozen on a deserialized 0/null.</summary>
        public const int DefaultFrames = 16;

        /// <summary>Pyre's own factory Shape Fill for a fresh layer (<c>Pyre.cs:324</c>), read off a throwaway
        /// layer so this can never be a second, drifting copy of that gradient.</summary>
        public static ZuiFill DefaultFill() => new PyreLayer().shapeFill;

        /// <summary>Pyre's own factory Alpha envelope for a fresh layer (<c>Pyre.cs:325</c>), same reason.</summary>
        public static ZUIValue DefaultAlpha() => new PyreLayer().alpha;

        // A composite node hosts exactly one form — no sibling layer whose RNG stream this one could collide
        // with — so the salt is a fixed constant rather than a per-instance field. Kept as a named constant (not
        // inlined) because every Eval call below and ContentHash must agree on it.
        const int LayerSalt = 0;

        // PyreRenderer's field id for the Shape alpha envelope (PyreRenderer.cs:36). Private there, restated here
        // rather than widened in Pyre: the id only has to MATCH, and PyreShaperEval already duplicates the
        // evaluator's switch for the same reason. Wrong id would still evaluate — it would only decorrelate a
        // MinMax draw from the one a real hosted layer makes, which is exactly the parity this task measures.
        const int FldAlpha = 2;

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

            // The frame this phase lands on. ShaperClock maps frame i to i/(N-1); this reads that one conversion
            // backwards rather than inventing a second one, so a hosted form's frame 3 is the document's frame 3 —
            // the same derivation PyreLayerCompositeSource.Render already makes for a hosted layer. A form that
            // ignores the index (Inferno, Fork Blast, Torch) is unaffected; a form that animates off it (Orb,
            // Plasma Bloom) only moves at all because of this.
            int n = frames > 0 ? frames : DefaultFrames;
            int frameIndex = n <= 1 ? 0 : Mathf.RoundToInt(life * (n - 1));

            // The layer's Alpha envelope at this life, resolved through the same funnel and the same field id
            // PyreRenderer uses (PyreRenderer.cs:292). Null only on a source authored before the field existed;
            // 1 keeps that document rendering as it did until its card repairs the field.
            float layerAlpha = alpha != null
                ? Mathf.Clamp01(PyreShaperEval.Eval(alpha, life, sd, PyreRenderer.ModParticleIndex, FldAlpha, LayerSalt))
                : 1f;

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
                // Square: the form draws straight into the node's own buffer.
                var square = new PyreFormCtx(width, height, life, sd, LayerSalt, shapeFill, layerAlpha,
                                             null, null, null, 0f, frameIndex, n, evalAtLife);
                form.Render(square, target);
                return;
            }

            int px = canvas * canvas;
            if (_square == null || _square.Length < px) _square = new Color32[px];
            else Array.Clear(_square, 0, px);

            var ctx = new PyreFormCtx(canvas, canvas, life, sd, LayerSalt, shapeFill, layerAlpha,
                                      null, null, null, 0f, frameIndex, n, evalAtLife);
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
            var m = ShaperCacheMixer.Begin("shaper.pyreshaper.pyreformcompositesource.v2");
            m.MixBool(form != null);
            if (form != null) m.MixInt(form.ContentHash());
            // T-0202 — the fill, the alpha envelope and the frame count are authored on the SOURCE, not on the
            // form, so the form's own ContentHash cannot see them. Without them here, dragging a gradient stop or
            // an alpha curve point would repaint nothing until the phase happened to change — the same staleness
            // gap this method exists to close for the form's dials. Folded through Pyre's own reflective value
            // mixer (the basis PyreForm.ContentHash seeds with) so the two hashes stay one family.
            const int Basis = unchecked((int)2166136261u);
            m.MixInt(PyreForm.MixValue(Basis, shapeFill));
            m.MixInt(PyreForm.MixValue(Basis, alpha));
            m.MixInt(frames);
            return m.Key;
        }
    }
}
