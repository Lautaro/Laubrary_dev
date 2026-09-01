using System.Text;
using UnityEngine;
using UnityEditor;
using Laubrary.Shaper;

public static class HeightFieldScaleProbe
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var preset = AssetDatabase.LoadAssetAtPath<ShaperHeightFieldPreset>(
            "Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/lines_GEN8_006.asset");
        sb.AppendLine("preset loaded: " + (preset != null && preset.field != null));

        var defA = new ShaperFillDef { kind = ShaperFillKind.HeightField, heightField = preset.field, heightFieldScale = new ZUIValue(1f) };
        var defB = new ShaperFillDef { kind = ShaperFillKind.HeightField, heightField = preset.field, heightFieldScale = new ZUIValue(-1f) };
        var defC = new ShaperFillDef { kind = ShaperFillKind.HeightField, heightField = preset.field, heightFieldScale = new ZUIValue(2.5f) };

        // Identity map, local box = the full 128x128 square centred at origin, so Anchor() actually varies
        // with (x,y) instead of degenerating to (0,0) (FC-1.5b fires when localValid is false).
        var anchor = new ShaperFillAnchor
        {
            inverse = ShaperMatrix.Identity,
            localValid = true, localCx = 0f, localCy = 0f, localHalfW = 64f, localHalfH = 64f,
            canvasHalfW = 64f, canvasHalfH = 64f,
        };

        var progA = ShaperFillCompiler.Compile(defA, anchor, 0f, 0u);
        var progB = ShaperFillCompiler.Compile(defB, anchor, 0f, 0u);
        var progC = ShaperFillCompiler.Compile(defC, anchor, 0f, 0u);

        int mismatches = 0, mismatchesC = 0;
        int n = 12;
        for (int i = 0; i < n; i++)
        {
            float x = -60f + i * 10f;
            float y = 3f * (i % 5 - 2);

            ShaperFillOps.Sample(in progA.op, progA.bulk, x, y, 0f, 0f, out _, out _, out _, out _, out float hA);
            ShaperFillOps.Sample(in progB.op, progB.bulk, x, y, 0f, 0f, out _, out _, out _, out _, out float hB);
            ShaperFillOps.Sample(in progC.op, progC.bulk, x, y, 0f, 0f, out _, out _, out _, out _, out float hC);

            bool okB = Mathf.Approximately(hB, -hA);
            bool okC = Mathf.Approximately(hC, hA * 2.5f);
            if (!okB) mismatches++;
            if (!okC) mismatchesC++;
            sb.AppendLine($"  ({x:F0},{y:F0}) raw*1={hA:R} raw*-1={hB:R} (expect {-hA:R}) {(okB ? "OK" : "MISMATCH")}   " +
                          $"raw*2.5={hC:R} (expect {hA * 2.5f:R}) {(okC ? "OK" : "MISMATCH")}");
        }

        sb.AppendLine($"scale=-1 mismatches: {mismatches}/{n}   scale=2.5 mismatches: {mismatchesC}/{n}");
        sb.AppendLine("RESULT: " + ((mismatches == 0 && mismatchesC == 0) ? "PASS" : "FAIL"));
        Debug.Log(sb.ToString());
        return sb.ToString();
    }
}
