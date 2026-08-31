if (UnityEditor.EditorUtility.scriptCompilationFailed) return "COMPILE FAILED";
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("alpha | OLD EncodePremultiplied encode(rgb*a) | NEW Encode (straight) | conventional premult encode(rgb)*a | straight consumer reading OLD");
float[] alphas = { 1f, 0.75f, 0.5f, 0.25f, 0.1f, 0f };
var one = new float[4]; var px = new UnityEngine.Color32[1];
foreach (float a in alphas) {
    float lin = 1f;                       // linear white albedo
    one[0] = lin * a; one[1] = lin * a; one[2] = lin * a; one[3] = a;
    byte oldB = Laubrary.Shaper.ShaperSrgb.EncodeToByte(lin * a);                     // the deleted encoder
    Laubrary.Shaper.ShaperFillResolver.Encode(one, px, 1);
    byte newB = px[0].r;
    byte conv = (byte)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(Laubrary.Shaper.ShaperSrgb.EncodeChannel(lin) * a * 255f), 0, 255);
    sb.AppendLine("  " + a.ToString("F3") + " | " + oldB + " | " + newB + " | " + conv +
                  " | " + oldB + " (should be " + newB + ")");
}
// The round trip that matters: author -> linear -> premultiply -> Encode -> byte, all 256 codes at a=0.5.
int worstOld = 0, worstNew = 0;
for (int v = 0; v < 256; v++) {
    float lin = Laubrary.Shaper.ShaperSrgb.DecodeChannel(v / 255f);
    float a = 0.5f;
    one[0] = lin * a; one[1] = lin * a; one[2] = lin * a; one[3] = a;
    byte oldB = Laubrary.Shaper.ShaperSrgb.EncodeToByte(lin * a);
    Laubrary.Shaper.ShaperFillResolver.Encode(one, px, 1);
    worstOld = UnityEngine.Mathf.Max(worstOld, UnityEngine.Mathf.Abs(oldB - v));
    worstNew = UnityEngine.Mathf.Max(worstNew, UnityEngine.Mathf.Abs(px[0].r - v));
}
sb.AppendLine("all 256 codes at alpha 0.5, worst |byte-out - byte-in| read as STRAIGHT: OLD " + worstOld + " codes, NEW " + worstNew + " codes");
var fa = System.Type.GetType("Laubrary.Shaper.Editor.ShaperFieldAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
sb.AppendLine("fieldAuditType=" + (fa == null ? "NULL" : fa.FullName));
return sb.ToString();
