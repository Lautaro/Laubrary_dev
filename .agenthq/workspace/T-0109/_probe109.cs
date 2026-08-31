var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath = " + UnityEngine.Application.dataPath);
sb.AppendLine("compiling = " + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("scriptCompilationFailed = " + UnityEditor.EditorUtility.scriptCompilationFailed);

System.Type Find(string full) {
  foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var t = asm.GetType(full, false);
    if (t != null) return t;
  }
  return null;
}
var tRes = Find("Laubrary.Shaper.ShaperResolve");
var tH   = Find("Laubrary.Shaper.ShaperHeight");
var tOp  = Find("Laubrary.Shaper.ShaperHeightOp");
var tSc  = Find("Laubrary.Shaper.ShaperLightScene");
var tBuf = Find("Laubrary.Shaper.ShaperFillBuffers");
var tLay = Find("Laubrary.Shaper.ShaperLayer");
var tAud = Find("Laubrary.Shaper.Editor.ShaperHeightAudit");
sb.AppendLine("asm(ShaperResolve) = " + (tRes != null ? tRes.Assembly.GetName().Name : "?"));
sb.AppendLine("SurfaceResolution   = " + (tRes != null && tRes.GetField("SurfaceResolution") != null ? tRes.GetField("SurfaceResolution").GetValue(null).ToString() : "MISSING"));
sb.AppendLine("MaxBracketDepth     = " + (tRes != null && tRes.GetField("MaxBracketDepth") != null ? tRes.GetField("MaxBracketDepth").GetValue(null).ToString() : "MISSING"));
sb.AppendLine("MaxStepsPerSlab     = " + (tRes != null && tRes.GetField("MaxStepsPerSlab") != null ? tRes.GetField("MaxStepsPerSlab").GetValue(null).ToString() : "MISSING"));
sb.AppendLine("InverseUpperBound   = " + (tH != null && tH.GetMethod("InverseUpperBound") != null ? "present" : "MISSING"));
sb.AppendLine("InverseAtE          = " + (tH != null && tH.GetMethod("InverseAtE") != null ? "present" : "MISSING"));
sb.AppendLine("HeightOp.infE       = " + (tOp != null && tOp.GetField("infE") != null ? "present" : "MISSING"));
sb.AppendLine("HeightOp.linearEGrad= " + (tOp != null && tOp.GetField("linearEGrad") != null ? "present" : "MISSING"));
sb.AppendLine("Scene.heightOp      = " + (tSc != null && tSc.GetField("heightOp") != null ? "present" : "MISSING"));
sb.AppendLine("Scene.normalDegen   = " + (tSc != null && tSc.GetField("normalDegenerate") != null ? "present" : "MISSING"));
sb.AppendLine("Buffers.ownHeight   = " + (tBuf != null && tBuf.GetField("ownHeight") != null ? "present" : "MISSING"));
sb.AppendLine("Layer.height        = " + (tLay != null && tLay.GetField("height") != null ? "present" : "MISSING"));
sb.AppendLine("Audit.H11           = " + (tAud != null && tAud.GetMethod("H11_StageIsWired") != null ? "present" : "MISSING"));
return sb.ToString();
