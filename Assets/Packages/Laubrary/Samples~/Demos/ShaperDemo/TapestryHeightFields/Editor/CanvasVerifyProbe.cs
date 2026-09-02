using UnityEngine;
using Laubrary.Shaper;

public static class CanvasVerifyProbe
{
    public static string Run()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== HashCell ===");
        sb.AppendLine("0,0,12345,0 -> " + ShaperTapestryCanvas.HashCell(0, 0, 12345u, 0u).ToString("R"));
        sb.AppendLine("3,7,12345,0 -> " + ShaperTapestryCanvas.HashCell(3, 7, 12345u, 0u).ToString("R"));
        sb.AppendLine("255,255,999,3 -> " + ShaperTapestryCanvas.HashCell(255, 255, 999u, 3u).ToString("R"));
        sb.AppendLine("7,3,4294967295,7 -> " + ShaperTapestryCanvas.HashCell(7, 3, 4294967295u, 7u).ToString("R"));
        sb.AppendLine("10,20,0,0 -> " + ShaperTapestryCanvas.HashCell(10, 20, 0u, 0u).ToString("R"));

        sb.AppendLine("=== WrappedValueNoiseAt ===");
        sb.AppendLine("0.12345,0.6789,6,42,0 -> " + ShaperTapestryCanvas.WrappedValueNoiseAt(0.12345f, 0.6789f, 6, 42u, 0u).ToString("R"));
        sb.AppendLine("0.5,0.5,8,7,1 -> " + ShaperTapestryCanvas.WrappedValueNoiseAt(0.5f, 0.5f, 8, 7u, 1u).ToString("R"));
        sb.AppendLine("0.999,0.001,4,123,2 -> " + ShaperTapestryCanvas.WrappedValueNoiseAt(0.999f, 0.001f, 4, 123u, 2u).ToString("R"));

        sb.AppendLine("=== Fbm ===");
        sb.AppendLine("0.3,0.7,6,4,42,0.5 -> " + ShaperTapestryCanvas.Fbm(0.3f, 0.7f, 6, 4, 42u, 0.5f).ToString("R"));
        sb.AppendLine("0.0,0.0,4,3,7,0.5 -> " + ShaperTapestryCanvas.Fbm(0.0f, 0.0f, 4, 3, 7u, 0.5f).ToString("R"));
        sb.AppendLine("0.5,0.5,10,5,123,0.6 -> " + ShaperTapestryCanvas.Fbm(0.5f, 0.5f, 10, 5, 123u, 0.6f).ToString("R"));

        Debug.Log(sb.ToString());
        return sb.ToString();
    }
}
