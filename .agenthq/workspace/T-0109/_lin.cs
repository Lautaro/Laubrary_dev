var sb = new System.Text.StringBuilder();
var node = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
    kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = 34f, rectHalfH = 26f, rectCornerRadius = 8f });
var prog = Laubrary.Shaper.ShaperCompiler.Compile(node);
sb.AppendLine("hasLocalSupport=" + prog.hasLocalSupport + " localHalfW=" + prog.localSupportHalfW +
              " localHalfH=" + prog.localSupportHalfH + " rootSigmaMin=" + prog.rootSigmaMin +
              " supportHalfW=" + prog.supportHalfW + " bound=" + prog.bound);
foreach (float ang in new float[]{0f,45f,135f}) {
  var def = new Laubrary.Shaper.ShaperHeightDef {
      technique = Laubrary.Shaper.ShaperExtrusionTechnique.Linear,
      bevel = Laubrary.Shaper.ShaperBevelTechnique.None,
      depth = new ZUIValue(16f), angle = new ZUIValue(ang) };
  var op = Laubrary.Shaper.ShaperHeightCompiler.Compile(def, prog, 1f, 0f);
  var stack = prog.NewStack();
  sb.Append("angle " + ang + " span=" + op.span.ToString("F3") + " supE=" + op.supE.ToString("F4") + "  h at corners:");
  foreach (var p in new UnityEngine.Vector2[]{ new UnityEngine.Vector2(-24,-18), new UnityEngine.Vector2(24,-18),
                                               new UnityEngine.Vector2(-24,18), new UnityEngine.Vector2(24,18),
                                               new UnityEngine.Vector2(0,0) }) {
    float d = Laubrary.Shaper.ShaperEvaluator.Distance(prog, p.x, p.y, stack);
    float t = Laubrary.Shaper.ShaperHeight.T(op, d);
    float nx, ny; Laubrary.Shaper.ShaperHeight.LocalNormalised(op, p.x, p.y, out nx, out ny);
    sb.Append("  (" + p.x + "," + p.y + ")=" + Laubrary.Shaper.ShaperHeight.Height(op, t, nx, ny).ToString("F3"));
  }
  sb.AppendLine();
}
// span check on a stepped case
var d2 = new Laubrary.Shaper.ShaperHeightDef { technique = Laubrary.Shaper.ShaperExtrusionTechnique.Stepped,
    depth = new ZUIValue(16f), steps = new ZUIValue(4f) };
var op2 = Laubrary.Shaper.ShaperHeightCompiler.Compile(d2, prog, 1f, 0f);
var bp = new float[Laubrary.Shaper.ShaperHeight.MaxBreakpoints];
int nb = Laubrary.Shaper.ShaperHeight.Breakpoints(op2, bp);
sb.Append("stepped breakpoints (" + nb + "):");
for (int i = 0; i < nb; i++) sb.Append(" " + bp[i].ToString("F4"));
sb.AppendLine();
var d3 = new Laubrary.Shaper.ShaperHeightDef { technique = Laubrary.Shaper.ShaperExtrusionTechnique.Stepped,
    bevel = Laubrary.Shaper.ShaperBevelTechnique.Stepped, depth = new ZUIValue(16f),
    steps = new ZUIValue(32f), bevelAmount = new ZUIValue(0.5f), bevelSteps = new ZUIValue(16f) };
var op3 = Laubrary.Shaper.ShaperHeightCompiler.Compile(d3, prog, 1f, 0f);
int nb3 = Laubrary.Shaper.ShaperHeight.Breakpoints(op3, bp);
sb.AppendLine("both-stepped worst case breakpoint count = " + nb3 + " (cap " + Laubrary.Shaper.ShaperHeight.MaxBreakpoints + ")");
return sb.ToString();
