using System.Text;
using UnityEngine;
using Laubrary.Shaper;

public static class SteelQuantiseProbe
{
    public static string Run()
    {
        var sb = new StringBuilder();

        // ── Quantise: known inputs -> known discrete outputs ──────────────────────────────────────────
        sb.AppendLine("=== Quantise(levels=5) ===");
        float[] probes = { 0f, 0.1f, 0.24f, 0.26f, 0.5f, 0.74f, 0.76f, 0.99f, 1f };
        int badQ = 0;
        foreach (var p in probes)
        {
            float r = p, g = p, b = p;
            ShaperFillOps.Quantise(5, ref r, ref g, ref b);
            // 5 levels -> steps of 1/4: {0, 0.25, 0.5, 0.75, 1}. Verify the result is one of exactly those 5 values.
            bool onGrid = Mathf.Abs(r - Mathf.Round(r * 4f) / 4f) < 1e-5f;
            bool allEqual = r == g && g == b;
            if (!onGrid || !allEqual) badQ++;
            sb.AppendLine($"  in={p:F2} -> out={r:F4} onGrid={onGrid} allEqual={allEqual}");
        }
        sb.AppendLine("quantise mismatches: " + badQ + "/" + probes.Length);

        // ── TapestrySteel: sample a grid, check finiteness + rust responds to edge distance ───────────
        sb.AppendLine("=== TapestrySteel sample sweep ===");
        var def = new ShaperFillDef
        {
            kind = ShaperFillKind.TapestrySteel,
            steelCells = new ZUIValue(6f), steelOctaves = new ZUIValue(4f), steelSeed = new ZUIValue(7f),
            steelBaseLow = new Color(0.3f, 0.31f, 0.33f), steelBaseHigh = new Color(0.58f, 0.59f, 0.62f),
            steelRustColor = new Color(0.46f, 0.22f, 0.11f),
            steelRustAmount = new ZUIValue(0.8f), steelRustReachPixels = new ZUIValue(30f),
            steelGrain = new ZUIValue(0.06f), quantiseLevels = new ZUIValue(0f),
        };
        var anchor = new ShaperFillAnchor
        {
            inverse = ShaperMatrix.Identity,
            localValid = true, localCx = 0f, localCy = 0f, localHalfW = 64f, localHalfH = 64f,
            canvasHalfW = 64f, canvasHalfH = 64f,
        };
        var prog = ShaperFillCompiler.Compile(def, anchor, 0.3f, 11u);

        int nonFinite = 0, outOfRange = 0, n = 0;
        float sumRustNear = 0f, sumRustFar = 0f; int cntNear = 0, cntFar = 0;
        for (int yi = -50; yi <= 50; yi += 5)
        {
            for (int xi = -50; xi <= 50; xi += 5)
            {
                n++;
                // edge distance: negative inside; use a fake disc-like distance from origin, radius 50.
                float dist = Mathf.Sqrt(xi * xi + yi * yi) - 50f; // signed, negative inside
                ShaperFillOps.Sample(in prog.op, prog.bulk, xi, yi, dist, 0f,
                                     out float r, out float g, out float b, out float veil, out float h);
                if (!(r >= -1e-4f && r <= 1f + 1e-4f && g >= -1e-4f && g <= 1f + 1e-4f && b >= -1e-4f && b <= 1f + 1e-4f))
                    outOfRange++;
                if (float.IsNaN(r) || float.IsInfinity(r) || float.IsNaN(g) || float.IsInfinity(g) || float.IsNaN(b) || float.IsInfinity(b))
                    nonFinite++;

                // "near" = deep inside (dist << 0, close to centre); "far" = right at the edge (dist ~ 0)
                if (dist < -35f) { sumRustNear += r; cntNear++; }
                else if (dist > -5f && dist <= 0f) { sumRustFar += r; cntFar++; }
            }
        }
        sb.AppendLine($"  samples={n} nonFinite={nonFinite} outOfRange={outOfRange}");
        float avgNear = cntNear > 0 ? sumRustNear / cntNear : 0f;
        float avgFar = cntFar > 0 ? sumRustFar / cntFar : 0f;
        sb.AppendLine($"  avg R near centre (rust-biased, n={cntNear}) = {avgNear:F4}");
        sb.AppendLine($"  avg R near edge   (base tone,  n={cntFar}) = {avgFar:F4}");
        sb.AppendLine("  rust colour R = 0.46 (higher than base tone's ~0.3-0.6 grey R channel is ambiguous alone,");
        sb.AppendLine("  so the real signal is R-B: rust has R-B=0.35, base tone has R-B~=0 (grey).");
        // Recompute using R-B (redness) instead, which is discriminating regardless of luma.
        float sumRBNear = 0, sumRBFar = 0;
        int idx = 0;
        for (int yi = -50; yi <= 50; yi += 5)
            for (int xi = -50; xi <= 50; xi += 5)
            {
                float dist = Mathf.Sqrt(xi * xi + yi * yi) - 50f;
                ShaperFillOps.Sample(in prog.op, prog.bulk, xi, yi, dist, 0f, out float r, out float g, out float b, out float veil, out float h);
                float redness = r - b;
                if (dist < -35f) sumRBNear += redness;
                else if (dist > -5f && dist <= 0f) sumRBFar += redness;
            }
        float avgRBNear = cntNear > 0 ? sumRBNear / cntNear : 0f;
        float avgRBFar = cntFar > 0 ? sumRBFar / cntFar : 0f;
        sb.AppendLine($"  avg redness (R-B) near centre = {avgRBNear:F4}, near edge = {avgRBFar:F4}");
        bool rustBiasOk = avgRBNear > avgRBFar + 0.02f;

        sb.AppendLine("RESULT: " + ((nonFinite == 0 && outOfRange == 0 && badQ == 0 && rustBiasOk) ? "PASS" : "FAIL") +
                      $"  (rustBiasOk={rustBiasOk}: interior redness {avgRBNear:F4} > edge redness {avgRBFar:F4})");
        Debug.Log(sb.ToString());
        return sb.ToString();
    }
}
