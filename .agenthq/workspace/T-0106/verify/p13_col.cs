var sb = new System.Text.StringBuilder();
const int W = 64, H = 64; const float Px = 1f;
float CHW = 0.5f*(W-1)*Px, CHH = 0.5f*(H-1)*Px;
System.Func<float,Laubrary.Shaper.ShaperNode> Disc = r => {
  var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
    kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r }, "D"); return n; };

// ---------- F1: full round trip byte -> authored Color -> linear -> composite -> Color32 ----------
sb.AppendLine("== F: colour round trip through the WHOLE integrated path (Solid, veil 1, Over, opaque interior) ==");
int worstB = 0; int worstAt = -1;
var pxOut = new UnityEngine.Color32[W*H];
for (int v = 0; v < 256; v += 1)
{
  var root = Disc(20f);
  root.fill = new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid,
     solidColor = new UnityEngine.Color(v/255f, v/255f, v/255f, 1f) };
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, CHW, CHH);
  var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
  var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, 0,0,W,H, buf,
    new Laubrary.Shaper.ShaperFillSheets { published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine });
  Laubrary.Shaper.ShaperFillResolver.EncodePremultiplied(buf.dst, pxOut, W*H);
  int centre = (H/2)*W + (W/2);
  int d = UnityEngine.Mathf.Abs(pxOut[centre].r - v);
  if (d > worstB) { worstB = d; worstAt = v; }
}
sb.AppendLine("  all 256 grey levels, opaque interior sample: worst |out - in| = " + worstB + " codes (at input " + worstAt + ")  expected 0");

// ---------- F2: premultiplied vs straight encoders ----------
sb.AppendLine();
sb.AppendLine("== F: premultiplied (the default) vs straight-alpha encoder ==");
{
  var pre = new UnityEngine.Color32[8]; var str = new UnityEngine.Color32[8];
  var dst = new float[8*4];
  float[] alphas = {1f, 0.75f, 0.5f, 0.25f, 0.1f, 0.02f, 0.004f, 0f};
  // a white fill (linear 1.0) at each alpha, carried premultiplied as the compositor does
  for(int i=0;i<8;i++){ dst[i*4+0]=alphas[i]; dst[i*4+1]=alphas[i]; dst[i*4+2]=alphas[i]; dst[i*4+3]=alphas[i]; }
  Laubrary.Shaper.ShaperFillResolver.EncodePremultiplied(dst, pre, 8);
  Laubrary.Shaper.ShaperFillResolver.EncodeStraight(dst, str, 8);
  sb.AppendLine("  alpha | premult RGB | straight RGB | agree?   (source colour is linear white in every row)");
  for(int i=0;i<8;i++)
    sb.AppendLine("  " + alphas[i].ToString("F3").PadLeft(5) + " |     " + pre[i].r.ToString().PadLeft(3) +
      "     |     " + str[i].r.ToString().PadLeft(3) + "      | " + (pre[i].r==str[i].r ? "yes" : "NO  (delta " + (str[i].r-pre[i].r) + ")"));
  sb.AppendLine("  -> the two encoders agree ONLY at alpha == 1. FC-2.3 says albedo is NON-premultiplied; the");
  sb.AppendLine("     default writer emits sRGB(premultiplied-linear), which is neither straight nor a valid");
  sb.AppendLine("     premultiplied-sRGB pair for a gamma-space consumer.");
}
// ---------- F3: does Over stay correct? two opaque solids, second over first ----------
sb.AppendLine();
sb.AppendLine("== F: ordinary Over of a half-veiled white over an opaque mid-grey ==");
{
  var dst = new float[4];
  // dst = opaque linear 0.2158 (sRGB 128)
  float g = Laubrary.Shaper.ShaperSrgb.DecodeChannel(128f/255f);
  dst[0]=g; dst[1]=g; dst[2]=g; dst[3]=1f;
  float ce = 0.5f; float alb = 1f;
  for(int c=0;c<3;c++) dst[c] = alb*ce + dst[c]*(1f-ce);
  dst[3] = ce + dst[3]*(1f-ce);
  var o = new UnityEngine.Color32[1]; Laubrary.Shaper.ShaperFillResolver.EncodePremultiplied(dst, o, 1);
  float expectLinear = 1f*0.5f + g*0.5f;
  sb.AppendLine("  linear result " + dst[0].ToString("F6") + " (expected " + expectLinear.ToString("F6") + "), alpha " + dst[3].ToString("F4"));
  sb.AppendLine("  encoded byte  " + o[0].r + "   (sRGB of that linear value = " + UnityEngine.Mathf.RoundToInt(Laubrary.Shaper.ShaperSrgb.EncodeChannel(expectLinear)*255f) + ")");
  sb.AppendLine("  the same blend done in sRGB (what Pyre does) would be " + UnityEngine.Mathf.RoundToInt((255f*0.5f+128f*0.5f)) + " - the expected, documented divergence");
}

// ---------- G: the veil, adversarially ----------
sb.AppendLine();
sb.AppendLine("== G: veil invariants under adversarial dials ==");
System.Func<ZUIValue,string> Bake = zv => {
  var root = Disc(20f);
  root.fill = new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid,
     solidColor = UnityEngine.Color.white, veil = zv };
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, CHW, CHH);
  var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
  var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, 0,0,W,H, buf,
    new Laubrary.Shaper.ShaperFillSheets { published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine });
  int centre=(H/2)*W+(W/2);
  int nan=0, outside=0; float mxA=0;
  for(int i=0;i<W*H;i++){ for(int c=0;c<4;c++){ float f=buf.dst[i*4+c]; if(float.IsNaN(f)||float.IsInfinity(f)) nan++; }
    float a=buf.dst[i*4+3]; if(a>mxA)mxA=a;
    if(buf.ownCoverage[i]<=0f && a>1e-6f) outside++; }
  return "baked veil=" + doc.owners[0].fill.op.veil.ToString("F4") + "  centre alpha=" + buf.dst[centre*4+3].ToString("F4") +
         "  non-finite dst floats=" + nan + "  paint outside coverage=" + outside + "  max alpha=" + mxA.ToString("F4");
};
sb.AppendLine("  veil = -1.0      : " + Bake(new ZUIValue(-1f)));
sb.AppendLine("  veil = +2.0      : " + Bake(new ZUIValue(2f)));
sb.AppendLine("  veil = 1e30      : " + Bake(new ZUIValue(1e30f)));
sb.AppendLine("  veil = NaN       : " + Bake(new ZUIValue(float.NaN)));
sb.AppendLine("  veil = +Infinity : " + Bake(new ZUIValue(float.PositiveInfinity)));
sb.AppendLine("  veil = -Infinity : " + Bake(new ZUIValue(float.NegativeInfinity)));
// animated ZUIValue swinging outside [0,1]
{
  var curve = new ZUIValue { mode = ZUIValue.Mode.Curve, min = -3f, max = 4f };
  var sb2 = new System.Text.StringBuilder();
  for (float p = 0f; p <= 1.0001f; p += 0.25f) {
    var root = Disc(20f);
    root.fill = new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid, veil = curve };
    var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, p, 0u, CHW, CHH);
    sb2.Append(" p=" + p.ToString("F2") + "->" + doc.owners[0].fill.op.veil.ToString("F3"));
  }
  sb.AppendLine("  animated Curve veil min=-3 max=4 over phase:" + sb2.ToString());
}
// clamp expression against an unbounded fog coverage
sb.AppendLine("  fog coverage (BC-3.3 unbounded): clamp01(1.7)*clamp01(1.0) = " +
   (UnityEngine.Mathf.Clamp01(1.7f)*UnityEngine.Mathf.Clamp01(1f)).ToString("F4") + " (expected 1.0)");
sb.AppendLine("  Mathf.Clamp01(NaN) = " + UnityEngine.Mathf.Clamp01(float.NaN).ToString() + "   <- the veil clamp's behaviour on NaN");
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r13_col.txt", sb.ToString());
return "ok";
