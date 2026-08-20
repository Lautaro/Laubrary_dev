// PlusSupersample — render a form at k× and box-filter down with PREMULTIPLIED alpha.
//
// Kiln agents render at 2–3× and downsample; PyrePlus renders 1:1, which shows as jaggy cel edges and crunchy thin
// features. A form opts in per frame:
//
//   public override void Render(in PlusFormCtx ctx, Color32[] target)
//       => PlusSupersample.Render(ctx, 3, RenderAt, target);     // RenderAt(in PlusFormCtx big, Color32[] buf)
//
// The k× ctx is the same frame with W·k × H·k and swarm positions scaled (PlusFormCtx.WithSize) — a form that
// derives lengths from W/H (the PyrePlus convention) needs no other change. The down-filter averages premultiplied
// colour so transparent texels do not bleed their (meaningless) RGB into the edge — the straight-alpha average is the
// classic dark-fringe bug. k ≤ 1 renders straight into `target`.
using System;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    public delegate void PlusRenderAt(in PlusFormCtx ctx, Color32[] target);

    public static class PlusSupersample
    {
        [ThreadStatic] static Color32[] _scratch;   // reused across frames; sized up on demand

        public static void Render(in PlusFormCtx ctx, int k, PlusRenderAt render, Color32[] target)
        {
            if (k <= 1) { render(ctx, target); return; }
            int W2 = ctx.W * k, H2 = ctx.H * k;
            int n = W2 * H2;
            if (_scratch == null || _scratch.Length < n) _scratch = new Color32[n];
            else Array.Clear(_scratch, 0, n);
            var big = ctx.WithSize(W2, H2);
            render(big, _scratch);
            Downsample(_scratch, W2, H2, k, target, ctx.W, ctx.H);
        }

        /// Box-filter `big` (W2×H2, straight alpha) down by integer k into `target` (W×H), premultiplied average.
        /// Every target pixel is WRITTEN (a fully transparent block writes (0,0,0,0)).
        public static void Downsample(Color32[] big, int W2, int H2, int k, Color32[] target, int W, int H)
        {
            float inv = 1f / (k * k);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float pr = 0f, pg = 0f, pb = 0f, pa = 0f;
                    int y0 = y * k, x0 = x * k;
                    for (int sy = 0; sy < k; sy++)
                    {
                        int row = (y0 + sy) * W2 + x0;
                        for (int sx = 0; sx < k; sx++)
                        {
                            var c = big[row + sx];
                            if (c.a == 0) continue;
                            float a = c.a / 255f;
                            pr += c.r * a; pg += c.g * a; pb += c.b * a; pa += a;
                        }
                    }
                    if (pa <= 0f) { target[y * W + x] = new Color32(0, 0, 0, 0); continue; }
                    // un-premultiply the block average (colour is the alpha-weighted mean of the covered texels)
                    float r = pr / pa, g = pg / pa, b = pb / pa, a255 = pa * inv * 255f;
                    target[y * W + x] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(r), 0, 255),
                                                    (byte)Mathf.Clamp(Mathf.RoundToInt(g), 0, 255),
                                                    (byte)Mathf.Clamp(Mathf.RoundToInt(b), 0, 255),
                                                    (byte)Mathf.Clamp(Mathf.RoundToInt(a255), 0, 255));
                }
        }

        /// Box-filter FLOAT planes down by k with ONE quantisation at the end: `pr/pg/pb` are premultiplied colour
        /// (0..1 × alpha), `pa` straight alpha (0..1), all W2×H2. The Color32 overload above rounds every supersample
        /// to a byte first, which drops a sample whose alpha is under 0.5/255 before it can add up with its
        /// neighbours — measurable on a field made of faint specks (Plasma Bloom `ashfall`: silhouette IoU 0.854
        /// byte-first vs the source's float-first path). `flipY` reads the planes top row first (y-down sources).
        public static void Downsample(float[] pr, float[] pg, float[] pb, float[] pa, int W2, int H2, int k,
                                      Color32[] target, int W, int H, bool flipY = false)
        {
            float inv = 1f / (k * k);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float sr = 0f, sg = 0f, sb = 0f, sa = 0f;
                    int y0 = (flipY ? H - 1 - y : y) * k, x0 = x * k;
                    for (int sy = 0; sy < k; sy++)
                    {
                        int row = (y0 + sy) * W2 + x0;
                        for (int sx = 0; sx < k; sx++) { int i = row + sx; sr += pr[i]; sg += pg[i]; sb += pb[i]; sa += pa[i]; }
                    }
                    float ad = sa * inv;
                    if (ad <= 1e-4f) { target[y * W + x] = new Color32(0, 0, 0, 0); continue; }
                    float ip = inv / ad;   // un-premultiply the block average
                    target[y * W + x] = new Color32((byte)Mathf.Clamp((int)(sr * ip * 255f + 0.5f), 0, 255),
                                                    (byte)Mathf.Clamp((int)(sg * ip * 255f + 0.5f), 0, 255),
                                                    (byte)Mathf.Clamp((int)(sb * ip * 255f + 0.5f), 0, 255),
                                                    (byte)Mathf.Clamp((int)(ad * 255f + 0.5f), 0, 255));
                }
        }
    }
}
