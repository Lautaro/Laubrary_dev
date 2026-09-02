using System;
using Laubrary.Shaper;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0114 — runs ONE <see cref="PixelModifier"/> at an explicit <see cref="ShaperEffectStage"/> across N
    /// swarm instances of an <see cref="IShaperCompositeSource"/>, proving the pre/post-composite distinction is
    /// real rather than just asserting it. Both methods below share everything except WHEN the effect runs
    /// relative to the fold — same instances, same render calls, same fold rule — so any difference in their
    /// output is caused by stage order alone, not by an incidental implementation difference between two
    /// unrelated code paths.
    ///
    /// Deliberately standalone: it does NOT hook into <c>ShaperCompiler</c>'s production compile pass, which
    /// folds a Composite node's swarm instances through <c>ShaperBlend</c> in the SDF/pseudo-distance domain —
    /// wiring this stage split into that pass is real, separate surgery this task does not take on (SPEC.md
    /// Part 6, honest scope limits). The fold rule used HERE is plain alpha Over-compositing, back-to-front in
    /// list order — the same rule Pyre's own particle draw loop already uses when it composites one particle
    /// over the last (<c>PyreRenderer.cs:3089-3092</c>) — so this is a real, named proxy for "instances fold
    /// into one picture," not a claim about the production Composite-swarm compiler's own fold rule.
    ///
    /// What this DOES prove for real: SHAPER_THE_DESIGN.md C8's claim, and the exact opposite of T-0098 report
    /// E2's withdrawn claim ("move the recolour stage out of the draw loop — zero behavioural cost") — that a
    /// non-linear or pixel-dropping kernel produces a different picture depending on which side of the fold it
    /// runs, the moment instances overlap.
    /// </summary>
    public static class ShaperEffectStageRunner
    {
        public struct Instance
        {
            public IShaperCompositeSource source;
            public float phase01;
            public uint seed;
            /// <summary>Where this instance's own (0,0) lands on the shared canvas — the swarm's per-instance
            /// position jitter, already resolved to integer canvas pixels by the caller.</summary>
            public Vector2Int offset;
        }

        /// <summary>PRE-composite: apply <paramref name="effect"/> to EACH instance's own buffer — its native
        /// per-instance semantic — then fold all instances into one canvas.</summary>
        public static Color32[] RunPreComposite(Instance[] instances, int width, int height,
                                                 int canvasW, int canvasH, PixelModifier effect)
        {
            var canvas = new Color32[canvasW * canvasH];
            if (instances == null) return canvas;
            foreach (var inst in instances)
            {
                var buf = new Color32[width * height];
                inst.source.Render(width, height, inst.phase01, inst.seed, buf);
                if (effect != null && effect.enabled)
                    ApplyInPlace(buf, width, height, inst.phase01, inst.seed, effect);
                OverComposite(canvas, canvasW, canvasH, buf, width, height, inst.offset);
            }
            return canvas;
        }

        /// <summary>POST-composite: fold all instances into one canvas FIRST — the identical fold the Pre path
        /// above performs — then apply <paramref name="effect"/> ONCE to the finished result.</summary>
        public static Color32[] RunPostComposite(Instance[] instances, int width, int height,
                                                  int canvasW, int canvasH, PixelModifier effect)
        {
            var canvas = new Color32[canvasW * canvasH];
            if (instances == null) return canvas;
            uint mixSeed = 0; float lastPhase = 0f;
            foreach (var inst in instances)
            {
                var buf = new Color32[width * height];
                inst.source.Render(width, height, inst.phase01, inst.seed, buf);
                OverComposite(canvas, canvasW, canvasH, buf, width, height, inst.offset);
                mixSeed ^= inst.seed;
                lastPhase = inst.phase01;
            }
            if (effect != null && effect.enabled)
                ApplyInPlace(canvas, canvasW, canvasH, lastPhase, mixSeed, effect);
            return canvas;
        }

        /// <summary>Runs <paramref name="effect"/>'s per-pixel kernel over every pixel of <paramref name="buf"/>,
        /// using <see cref="SfxKernels.MakePixel"/> for the per-pixel <see cref="PixelInfo"/> — the SAME
        /// picture-local crossFrac/hash construction the standalone SpriteFx Stack already uses, rather than a
        /// second, parallel formula invented here. A pixel the effect drops (<c>ApplyPixel</c> returning
        /// <c>false</c>) is erased to fully transparent, matching <c>BlastRenderer.ApplyPix</c>'s own convention.</summary>
        static void ApplyInPlace(Color32[] buf, int W, int H, float life, uint seed, PixelModifier effect)
        {
            // T-0163 — resolve the effect's ZUIValue dials at THIS phase, not at their static value. The
            // previous static-only closure froze every animated dial, which made a stage comparison between two
            // phases show the same effect settings on both. SpriteFxStack.LifeEval is the same evaluator
            // RunStack itself Prepares with, so this proxy and the production path read a dial identically.
            int sd = unchecked((int)seed);
            effect.Prepare(SpriteFxStack.LifeEval(life, sd));
            for (int y = 0, idx = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++, idx++)
                {
                    var s = buf[idx];
                    if (s.a == 0) continue;   // nothing to recolour/drop on an already-empty pixel
                    var col = new Color(s.r / 255f, s.g / 255f, s.b / 255f, s.a / 255f);
                    float a = col.a;
                    var info = SfxKernels.MakePixel(x, y, W, H, 0, life, sd);
                    bool keep = effect.ApplyPixel(ref col, ref a, info);
                    buf[idx] = keep ? (Color32)new Color(col.r, col.g, col.b, a) : new Color32(0, 0, 0, 0);
                }
            }
        }

        /// <summary>Straight alpha Over: <c>dst = src·srcA + dst·(1-srcA)</c>, RGB premultiplied for the blend
        /// and un-premultiplied back out — textbook source-over, the same law <c>PyreRenderer</c>'s own
        /// particle composite already applies.</summary>
        static void OverComposite(Color32[] canvas, int canvasW, int canvasH, Color32[] src, int srcW, int srcH,
                                  Vector2Int offset)
        {
            for (int y = 0; y < srcH; y++)
            {
                int cy = y + offset.y;
                if (cy < 0 || cy >= canvasH) continue;
                for (int x = 0; x < srcW; x++)
                {
                    int cx = x + offset.x;
                    if (cx < 0 || cx >= canvasW) continue;
                    var s = src[y * srcW + x];
                    if (s.a == 0) continue;
                    int ci = cy * canvasW + cx;
                    var d = canvas[ci];
                    float sa = s.a / 255f, da = d.a / 255f;
                    float outA = sa + da * (1f - sa);
                    if (outA <= 0.0001f) { canvas[ci] = new Color32(0, 0, 0, 0); continue; }
                    byte r = (byte)Mathf.RoundToInt((s.r * sa + d.r * da * (1f - sa)) / outA);
                    byte g = (byte)Mathf.RoundToInt((s.g * sa + d.g * da * (1f - sa)) / outA);
                    byte b = (byte)Mathf.RoundToInt((s.b * sa + d.b * da * (1f - sa)) / outA);
                    canvas[ci] = new Color32(r, g, b, (byte)Mathf.RoundToInt(outA * 255f));
                }
            }
        }
    }
}
