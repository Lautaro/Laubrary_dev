// T-0109 FIX F7 verification: for a given authored angle, which way does the Linear slab lean?
var def = new Laubrary.Shaper.ShaperHeightDef {
  technique = Laubrary.Shaper.ShaperExtrusionTechnique.Linear,
  bevel = Laubrary.Shaper.ShaperBevelTechnique.None,
  depth = new ZUIValue(40f), angle = new ZUIValue(45f) };
var node = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
  kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f });
var prog = Laubrary.Shaper.ShaperCompiler.Compile(node);
var op = Laubrary.Shaper.ShaperHeightCompiler.Compile(def, prog, 1f, 0f);
var sb = new System.Text.StringBuilder();
sb.AppendLine("angle=45, +Y-UP frame. Height at the four corners of the local box:");
foreach (var p in new[]{ new float[]{-1f,-1f}, new float[]{1f,-1f}, new float[]{-1f,1f}, new float[]{1f,1f} })
  sb.AppendLine("  (nx,ny)=(" + p[0] + "," + p[1] + ")  h=" + Laubrary.Shaper.ShaperHeight.Height(op, 0.5f, p[0], p[1]).ToString("F4"));
float gx, gy; Laubrary.Shaper.ShaperHeight.LinearGradient(op, out gx, out gy);
sb.AppendLine("LinearGradient = (" + gx.ToString("F5") + ", " + gy.ToString("F5") + ")  -- both POSITIVE at +45 in a +Y-up frame");
sb.AppendLine("supE=" + op.supE.ToString("R") + "  infE=" + op.infE.ToString("R") + "  linearEGrad=" + op.linearEGrad.ToString("R"));
// height sampled at real canvas points: highest should be top-right for +45 with +Y up
float hi = -1f, lo = 1e9f; float hx=0, hy=0, lx=0, ly=0;
for (int j = -40; j <= 40; j += 8) for (int i = -60; i <= 60; i += 10) {
  float nx, ny; Laubrary.Shaper.ShaperHeight.LocalNormalised(op, i, j, out nx, out ny);
  float h = Laubrary.Shaper.ShaperHeight.Height(op, 0.5f, nx, ny);
  if (h > hi) { hi = h; hx = i; hy = j; } if (h < lo) { lo = h; lx = i; ly = j; } }
sb.AppendLine("highest canvas point (" + hx + "," + hy + ") h=" + hi.ToString("F3") + "   lowest (" + lx + "," + ly + ") h=" + lo.ToString("F3"));
return sb.ToString();
