var sb = new System.Text.StringBuilder();
const int W = 96, H = 96; const float Px = 1f;
float CHW = 0.5f*(W-1)*Px, CHH = 0.5f*(H-1)*Px;
var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
System.Func<float,float,float,float,Laubrary.Shaper.ShaperNode> RectN = (hw,hh,x,rot) => {
  var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
    kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW=hw, rectHalfH=hh }, "R");
  n.transform.translate = new UnityEngine.Vector2(x,0f); n.transform.rotation = rot; return n; };
System.Func<ZuiGradient> RG = () => { var g=new ZuiGradient(); g.gradient=new UnityEngine.Gradient();
  g.gradient.SetKeys(new[]{new UnityEngine.GradientColorKey(UnityEngine.Color.red,0f),new UnityEngine.GradientColorKey(UnityEngine.Color.blue,1f)},
                     new[]{new UnityEngine.GradientAlphaKey(1f,0f),new UnityEngine.GradientAlphaKey(1f,1f)}); g.EnsureTransformAnim(); return g; };
System.Func<UnityEngine.Texture2D> Checker = () => { var t=new UnityEngine.Texture2D(8,8,UnityEngine.TextureFormat.RGBA32,false);
  var p=new UnityEngine.Color32[64]; for(int y=0;y<8;y++)for(int x=0;x<8;x++) p[y*8+x]=((x+y)&1)==0?new UnityEngine.Color32(255,60,20,255):new UnityEngine.Color32(20,60,255,200);
  t.SetPixels32(p); t.Apply(); return t; };
System.Func<Laubrary.Shaper.ShaperFillDef,float[]> Run = def => {
  var root = RectN(30f,12f,18f,25f); root.fill = def;
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, CHW, CHH);
  var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,
    new Laubrary.Shaper.ShaperFillSheets{published=Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine});
  var c = new float[W*H*4]; System.Array.Copy(buf.dst,c,c.Length); return c; };
System.Func<float[],float[],string> Diff = (a,b) => { float mx=0; int n=0;
  for(int i=0;i<a.Length;i++){ float d=UnityEngine.Mathf.Abs(a[i]-b[i]); if(d>1e-6f){n++; if(d>mx)mx=d;} }
  return n + " floats differ, max " + mx.ToString("F4"); };

sb.AppendLine("== A: do the space / fit / mapping dials actually change the picture? (rotated 25deg, offset, non-square rect) ==");
{
 var g1 = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient, gradientMode=Laubrary.Shaper.ShaperGradientMode.Linear,
   gradient=RG(), space=Laubrary.Shaper.ShaperFillSpace.Stamped, fit=Laubrary.Shaper.ShaperFillFit.Uniform };
 var g2 = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient, gradientMode=Laubrary.Shaper.ShaperGradientMode.Linear,
   gradient=RG(), space=Laubrary.Shaper.ShaperFillSpace.Fixed, fit=Laubrary.Shaper.ShaperFillFit.Uniform };
 var g3 = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient, gradientMode=Laubrary.Shaper.ShaperGradientMode.Linear,
   gradient=RG(), space=Laubrary.Shaper.ShaperFillSpace.Stamped, fit=Laubrary.Shaper.ShaperFillFit.Stretch };
 var g4 = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient, gradientMode=Laubrary.Shaper.ShaperGradientMode.Radial,
   gradient=RG(), space=Laubrary.Shaper.ShaperFillSpace.Stamped, fit=Laubrary.Shaper.ShaperFillFit.Uniform };
 var g5 = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient, gradientMode=Laubrary.Shaper.ShaperGradientMode.Radial,
   gradient=RG(), space=Laubrary.Shaper.ShaperFillSpace.Stamped, fit=Laubrary.Shaper.ShaperFillFit.Stretch };
 sb.AppendLine("  Gradient Linear Stamped/Uniform vs Fixed/Uniform  : " + Diff(Run(g1),Run(g2)) + "   (must differ)");
 sb.AppendLine("  Gradient Linear Stamped/Uniform vs Stamped/Stretch: " + Diff(Run(g1),Run(g3)) + "   (must differ on a non-square node)");
 sb.AppendLine("  Gradient Radial Stamped/Uniform vs Stamped/Stretch: " + Diff(Run(g4),Run(g5)) + "   (Uniform=circle, Stretch=ellipse)");
 var t1 = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Texture, texture=Checker(), textureMapping=Laubrary.Shaper.ShaperTextureMapping.Fitted };
 var t2 = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Texture, texture=Checker(), textureMapping=Laubrary.Shaper.ShaperTextureMapping.Tiled, textureTilesX=new ZUIValue(3f), textureTilesY=new ZUIValue(3f) };
 var t3 = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Texture, texture=Checker(), textureMapping=Laubrary.Shaper.ShaperTextureMapping.Fitted, space=Laubrary.Shaper.ShaperFillSpace.Fixed };
 var t4 = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Texture, texture=Checker(), textureMapping=Laubrary.Shaper.ShaperTextureMapping.Fitted, textureAngleDegrees=new ZUIValue(37f) };
 sb.AppendLine("  Texture Fitted vs Tiled                           : " + Diff(Run(t1),Run(t2)) + "   (must differ)");
 sb.AppendLine("  Texture Fitted Stamped vs Fixed                   : " + Diff(Run(t1),Run(t3)) + "   (must differ)");
 sb.AppendLine("  Texture Fitted angle 0 vs 37deg                   : " + Diff(Run(t1),Run(t4)) + "   (must differ)");
}

sb.AppendLine();
sb.AppendLine("== A/FT-11 extended: tile independence of the INTEGRATED path (PaintTile), which FT-11 does not test ==");
{
 var root = RectN(30f,12f,18f,25f);
 root.fill = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Texture, texture=Checker(),
   textureMapping=Laubrary.Shaper.ShaperTextureMapping.Tiled, textureTilesX=new ZUIValue(3f), textureTilesY=new ZUIValue(3f),
   space=Laubrary.Shaper.ShaperFillSpace.Fixed };
 var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, CHW, CHH);
 var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
 var sh = new Laubrary.Shaper.ShaperFillSheets{published=Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine};
 Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,sh);
 var whole = new float[W*H*4]; System.Array.Copy(buf.dst,whole,whole.Length);
 var tiled = new float[W*H*4];
 var tbuf = new Laubrary.Shaper.ShaperFillBuffers(7*5, doc.owners.Count);
 for(int y=0;y<H;y+=5) for(int x=0;x<W;x+=7){
   int tw=UnityEngine.Mathf.Min(7,W-x), th=UnityEngine.Mathf.Min(5,H-y);
   Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, x,y,tw,th, tbuf, sh);
   for(int j=0;j<th;j++) for(int i=0;i<tw;i++){ int s=(j*tw+i)*4, d=(((y+j)*W)+(x+i))*4;
     tiled[d]=tbuf.dst[s]; tiled[d+1]=tbuf.dst[s+1]; tiled[d+2]=tbuf.dst[s+2]; tiled[d+3]=tbuf.dst[s+3]; } }
 int bad=0; float mx=0;
 for(int i=0;i<whole.Length;i++){ if(whole[i]!=tiled[i]){bad++; float d=UnityEngine.Mathf.Abs(whole[i]-tiled[i]); if(d>mx)mx=d;} }
 sb.AppendLine("  PaintTile whole-grid vs 7x5 decomposition (Texture.Tiled, Fixed space): " + bad + " floats differ (expected 0), max " + mx.ToString("E2"));
}

sb.AppendLine();
sb.AppendLine("== A/FT-12: instrumented read test. FT-12 measures BEHAVIOURALLY, so a read whose value is discarded is invisible to it. ==");
{
 var root = RectN(30f,12f,0f,0f);
 root.fill = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Solid };
 var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, CHW, CHH);
 var sheets = new Laubrary.Shaper.ShaperFillSheets{ coverage=new float[W*H], edgeDistance=new float[1],
   published=Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine };
 var emit = new Laubrary.Shaper.ShaperFillEmit{ albedo=new float[W*H*3], veil=new float[W*H], heightDelta=new float[W*H] };
 string got;
 try { Laubrary.Shaper.ShaperFillOps.FillTile(doc.owners[0].fill, grid,0,0,W,H,sheets,emit,0,W,0,W); got="no throw - the sheet was NOT touched"; }
 catch(System.IndexOutOfRangeException){ got="*** IndexOutOfRange: the Solid fill DID index the edgeDistance sheet it declared [None] for ***"; }
 catch(System.Exception e){ got="threw " + e.GetType().Name; }
 sb.AppendLine("  Solid (declared [None]) handed a 1-element edgeDistance array: " + got);
 root = RectN(30f,12f,0f,0f);
 root.fill = new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.RampByQuantity,
   rampQuantity=Laubrary.Shaper.ShaperQuantity.Coverage, rampGradient=RG() };
 doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, CHW, CHH);
 try { Laubrary.Shaper.ShaperFillOps.FillTile(doc.owners[0].fill, grid,0,0,W,H,sheets,emit,0,W,0,W); got="no throw"; }
 catch(System.IndexOutOfRangeException){ got="*** IndexOutOfRange: Ramp.Coverage (declared [Coverage]) DID index the edgeDistance sheet ***"; }
 catch(System.Exception e){ got="threw " + e.GetType().Name; }
 sb.AppendLine("  Ramp.Coverage (declared [Coverage]) handed a 1-element edgeDistance array: " + got);
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r17_misc.txt", sb.ToString());
return "ok";
