var sb = new System.Text.StringBuilder();
const int W = 128, H = 128; const float Px = 1f;
float CHW = 0.5f*(W-1)*Px, CHH = 0.5f*(H-1)*Px;
System.Func<float,Laubrary.Shaper.ShaperNode> Disc = r => Laubrary.Shaper.ShaperNode.Primitive(
  new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx=r, ellipseRy=r }, "D");
System.Func<UnityEngine.Texture2D> Checker = () => { var t=new UnityEngine.Texture2D(8,8,UnityEngine.TextureFormat.RGBA32,false);
  var p=new UnityEngine.Color32[64]; for(int y=0;y<8;y++)for(int x=0;x<8;x++) p[y*8+x]=((x+y)&1)==0?new UnityEngine.Color32(255,60,20,255):new UnityEngine.Color32(20,60,255,128);
  t.SetPixels32(p); t.Apply(); return t; };
System.Func<ZuiGradient> BW = () => { var g=new ZuiGradient(); g.gradient=new UnityEngine.Gradient();
  g.gradient.SetKeys(new[]{new UnityEngine.GradientColorKey(UnityEngine.Color.black,0f),new UnityEngine.GradientColorKey(UnityEngine.Color.white,1f)},
                     new[]{new UnityEngine.GradientAlphaKey(1f,0f),new UnityEngine.GradientAlphaKey(1f,1f)}); g.EnsureTransformAnim(); return g; };

sb.AppendLine("== A/FT-9: allocation, measured with GC.GetAllocatedBytesForCurrentThread (precise, per-thread, counts every managed alloc) ==");
sb.AppendLine("   instrument self-test first, so a zero below means something.");
{
  long b0 = System.GC.GetAllocatedBytesForCurrentThread();
  var junk = new float[10];
  long b1 = System.GC.GetAllocatedBytesForCurrentThread();
  sb.AppendLine("   self-test: one new float[10] measured as " + (b1-b0) + " bytes (expected 40-72; a 0 here would mean the instrument is blind)");
  b0 = System.GC.GetAllocatedBytesForCurrentThread();
  object boxed = 1.5f; long b2 = System.GC.GetAllocatedBytesForCurrentThread();
  sb.AppendLine("   self-test: one boxed float measured as " + (b2-b0) + " bytes (expected >0)");
  if (junk.Length + (boxed==null?0:1) < 0) sb.AppendLine("unreachable");
}
var kinds = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,Laubrary.Shaper.ShaperFillDef>>();
kinds.Add(new System.Collections.Generic.KeyValuePair<string,Laubrary.Shaper.ShaperFillDef>("Solid",
  new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Solid }));
foreach(Laubrary.Shaper.ShaperGradientMode gm in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperGradientMode)))
  kinds.Add(new System.Collections.Generic.KeyValuePair<string,Laubrary.Shaper.ShaperFillDef>("Gradient."+gm,
    new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient, gradientMode=gm, gradient=BW() }));
kinds.Add(new System.Collections.Generic.KeyValuePair<string,Laubrary.Shaper.ShaperFillDef>("Ramp.coverage",
  new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.RampByQuantity, rampQuantity=Laubrary.Shaper.ShaperQuantity.Coverage, rampGradient=BW() }));
kinds.Add(new System.Collections.Generic.KeyValuePair<string,Laubrary.Shaper.ShaperFillDef>("Texture.Fitted",
  new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Texture, texture=Checker() }));
kinds.Add(new System.Collections.Generic.KeyValuePair<string,Laubrary.Shaper.ShaperFillDef>("Texture.Tiled",
  new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Texture, texture=Checker(), textureMapping=Laubrary.Shaper.ShaperTextureMapping.Tiled }));

var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
foreach(var kv in kinds){
  var root = Disc(40f); root.fill = kv.Value;
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, CHW, CHH);
  var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
  var sheets = Laubrary.Shaper.ShaperFillSheets.FromShapeStage(new float[W*H], new float[W*H]);
  var emit = new Laubrary.Shaper.ShaperFillEmit{ albedo=buf.albedo, veil=buf.veil, heightDelta=buf.heightDelta };
  var prog = doc.owners[0].fill;
  Laubrary.Shaper.ShaperFillOps.FillTile(prog, grid,0,0,W,H,sheets,emit,0,W,0,W);   // warm
  long a0 = System.GC.GetAllocatedBytesForCurrentThread();
  for(int i=0;i<40;i++) Laubrary.Shaper.ShaperFillOps.FillTile(prog, grid,0,0,W,H,sheets,emit,0,W,0,W);
  long a1 = System.GC.GetAllocatedBytesForCurrentThread();
  // and the INTEGRATED path
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,sheets);       // warm
  long b0 = System.GC.GetAllocatedBytesForCurrentThread();
  for(int i=0;i<10;i++) Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,sheets);
  long b1 = System.GC.GetAllocatedBytesForCurrentThread();
  sb.AppendLine("  " + kv.Key.PadRight(24) + " FillTile x40: " + (a1-a0).ToString().PadLeft(8) + " bytes   PaintTile x10: " + (b1-b0).ToString().PadLeft(8) + " bytes  (expected 0 / 0)");
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r14_alloc.txt", sb.ToString());
return "ok";
