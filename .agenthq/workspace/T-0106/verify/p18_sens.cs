var sb = new System.Text.StringBuilder();
const int W = 128, H = 128; const float Px = 1f;
float CHW = 0.5f*(W-1)*Px, CHH = 0.5f*(H-1)*Px;
var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
System.Func<float,Laubrary.Shaper.ShaperNode> Disc = r => Laubrary.Shaper.ShaperNode.Primitive(
  new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx=r, ellipseRy=r }, "D");

sb.AppendLine("== A/FT-5 sensitivity: can FT-5 fail if FC-2.4's BAKE-TIME clamp is removed? ==");
sb.AppendLine("   Method: bind normally, then overwrite the compiled op's veil with 2.0 (exactly what deleting the");
sb.AppendLine("   bake clamp would produce), and re-measure the thing FT-5 measures - the dst alpha from PaintTile.");
{
  var root = Disc(40f);
  root.fill = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Solid, veil=new ZUIValue(2f) };
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, CHW, CHH);
  sb.AppendLine("   baked veil as shipped                = " + doc.owners[0].fill.op.veil.ToString("F4"));
  var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
  var sh = new Laubrary.Shaper.ShaperFillSheets{published=Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine};
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,sh);
  // find the sample closest to half coverage
  int best=-1; float be=1f;
  for(int i=0;i<W*H;i++){ float e=UnityEngine.Mathf.Abs(buf.ownCoverage[i]-0.5f); if(e<be){be=e;best=i;} }
  float covA = buf.ownCoverage[best], ceA = buf.dst[best*4+3];
  // now simulate the clamp being deleted
  var op = doc.owners[0].fill.op; op.veil = 2f; doc.owners[0].fill.op = op;
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,sh);
  float ceB = buf.dst[best*4+3];
  sb.AppendLine("   half-covered sample coverage         = " + covA.ToString("F6"));
  sb.AppendLine("   coverageEff WITH the bake clamp      = " + ceA.ToString("F6") + "   (FT-5 asserts <= coverage)");
  sb.AppendLine("   coverageEff WITHOUT the bake clamp   = " + ceB.ToString("F6") + "   <- if this is still <= coverage, FT-5 CANNOT FAIL");
  sb.AppendLine("   FT-5 would report FAIL without the clamp? " + (ceB > covA + 1e-6f ? "YES" : "NO - the test is insensitive to the clause it is named for"));
  sb.AppendLine("   reason: ShaperFillResolver.PaintTile applies a SECOND Mathf.Clamp01(veil) at use, so removing");
  sb.AppendLine("   the bake-time clamp changes nothing that FT-5 looks at.");
  sb.AppendLine("   the audit's own sentence says 'without the clamp it would be 1.0'; with BOTH clamps removed it");
  sb.AppendLine("   would be coverage*2 = " + (covA*2f).ToString("F6") + ", not 1.0.");
}

sb.AppendLine();
sb.AppendLine("== A: FillFit at a NON-axis-aligned gradient angle (my earlier theta=0 test was degenerate) ==");
{
 System.Func<float,Laubrary.Shaper.ShaperFillFit,float[]> Run = (ang,fit) => {
   var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef{
     kind=Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW=30f, rectHalfH=12f }, "R");
   var g=new ZuiGradient(); g.gradient=new UnityEngine.Gradient();
   g.gradient.SetKeys(new[]{new UnityEngine.GradientColorKey(UnityEngine.Color.red,0f),new UnityEngine.GradientColorKey(UnityEngine.Color.blue,1f)},
                      new[]{new UnityEngine.GradientAlphaKey(1f,0f),new UnityEngine.GradientAlphaKey(1f,1f)}); g.EnsureTransformAnim();
   n.fill = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient,
     gradientMode=Laubrary.Shaper.ShaperGradientMode.Linear, gradient=g, fit=fit, gradientAngleDegrees=new ZUIValue(ang) };
   var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(n, 0f, 0u, CHW, CHH);
   var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
   Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,
     new Laubrary.Shaper.ShaperFillSheets{published=Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine});
   var c=new float[W*H*4]; System.Array.Copy(buf.dst,c,c.Length); return c; };
 System.Func<float[],float[],string> D = (a,b)=>{ float mx=0; int n=0;
   for(int i=0;i<a.Length;i++){ float d=UnityEngine.Mathf.Abs(a[i]-b[i]); if(d>1e-6f){n++; if(d>mx)mx=d;} } return n+" floats differ, max "+mx.ToString("F4"); };
 sb.AppendLine("   Linear theta=0   Uniform vs Stretch: " + D(Run(0f,Laubrary.Shaper.ShaperFillFit.Uniform), Run(0f,Laubrary.Shaper.ShaperFillFit.Stretch)) + "  (correctly identical: theta=0 only reads u, divided by 30 either way)");
 sb.AppendLine("   Linear theta=90  Uniform vs Stretch: " + D(Run(90f,Laubrary.Shaper.ShaperFillFit.Uniform), Run(90f,Laubrary.Shaper.ShaperFillFit.Stretch)) + "  (MUST differ - this is ZuiFill's 'middle 18% of the ramp' case)");
 sb.AppendLine("   Linear theta=45  Uniform vs Stretch: " + D(Run(45f,Laubrary.Shaper.ShaperFillFit.Uniform), Run(45f,Laubrary.Shaper.ShaperFillFit.Stretch)) + "  (must differ)");
}

sb.AppendLine();
sb.AppendLine("== A/FT-10 extended: determinism ACROSS a domain reload. This run's hashes are written to disk; ==");
sb.AppendLine("   a second run after a forced reload compares against them.");
{
 System.Func<Laubrary.Shaper.ShaperFillDef,uint,ulong> Hash = (def,seed) => {
   var root = Disc(40f); root.fill = def;
   var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0.37f, seed, CHW, CHH);
   var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
   Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,
     new Laubrary.Shaper.ShaperFillSheets{published=Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine});
   ulong h = 1469598103934665603UL;
   var bytes = new byte[4];
   for(int i=0;i<buf.dst.Length;i++){ System.BitConverter.GetBytes(buf.dst[i]).CopyTo(bytes,0);
     for(int b=0;b<4;b++){ h ^= bytes[b]; h *= 1099511628211UL; } }
   return h; };
 var g=new ZuiGradient(); g.gradient=new UnityEngine.Gradient();
 g.gradient.SetKeys(new[]{new UnityEngine.GradientColorKey(UnityEngine.Color.red,0f),new UnityEngine.GradientColorKey(UnityEngine.Color.blue,1f)},
                    new[]{new UnityEngine.GradientAlphaKey(1f,0f),new UnityEngine.GradientAlphaKey(1f,1f)}); g.EnsureTransformAnim();
 var t=new UnityEngine.Texture2D(8,8,UnityEngine.TextureFormat.RGBA32,false);
 var pxc=new UnityEngine.Color32[64]; for(int y=0;y<8;y++)for(int x=0;x<8;x++) pxc[y*8+x]=((x+y)&1)==0?new UnityEngine.Color32(255,60,20,255):new UnityEngine.Color32(20,60,255,128);
 t.SetPixels32(pxc); t.Apply();
 var lines = new System.Text.StringBuilder();
 lines.AppendLine("Solid|" + Hash(new Laubrary.Shaper.ShaperFillDef{kind=Laubrary.Shaper.ShaperFillKind.Solid, solidColor=new UnityEngine.Color(0.3f,0.6f,0.9f)},12345u));
 lines.AppendLine("Gradient|" + Hash(new Laubrary.Shaper.ShaperFillDef{kind=Laubrary.Shaper.ShaperFillKind.Gradient, gradientMode=Laubrary.Shaper.ShaperGradientMode.Radial, gradient=g},12345u));
 lines.AppendLine("Ramp|" + Hash(new Laubrary.Shaper.ShaperFillDef{kind=Laubrary.Shaper.ShaperFillKind.RampByQuantity, rampQuantity=Laubrary.Shaper.ShaperQuantity.Coverage, rampGradient=g},12345u));
 lines.AppendLine("Texture|" + Hash(new Laubrary.Shaper.ShaperFillDef{kind=Laubrary.Shaper.ShaperFillKind.Texture, texture=t},12345u));
 lines.AppendLine("MinMaxVeil|" + Hash(new Laubrary.Shaper.ShaperFillDef{kind=Laubrary.Shaper.ShaperFillKind.Solid, veil=new ZUIValue{mode=ZUIValue.Mode.MinMax,min=0.1f,max=0.9f}},12345u));
 string path = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\determinism.txt";
 if (System.IO.File.Exists(path)) {
   string prev = System.IO.File.ReadAllText(path);
   sb.AppendLine("   PREVIOUS RUN's hashes found. Comparing:");
   var a = prev.Replace("\r","").Split('\n'); var b = lines.ToString().Replace("\r","").Split('\n');
   for(int i=0;i<a.Length && i<b.Length;i++) if(a[i].Length>0)
     sb.AppendLine("     " + a[i].PadRight(28) + " vs " + b[i].PadRight(28) + (a[i]==b[i] ? "  IDENTICAL" : "  *** DIFFERS ***"));
 } else {
   sb.AppendLine("   first run; hashes recorded:");
   sb.AppendLine("     " + lines.ToString().Replace("\r\n"," | "));
   System.IO.File.WriteAllText(path, lines.ToString());
 }
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r18_sens.txt", sb.ToString());
return "ok";
