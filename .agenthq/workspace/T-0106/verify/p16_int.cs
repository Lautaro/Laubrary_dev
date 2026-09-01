var sb = new System.Text.StringBuilder();
const int W = 96, H = 96; const float Px = 1f;
float CHW = 0.5f*(W-1)*Px, CHH = 0.5f*(H-1)*Px;
var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
System.Func<float,Laubrary.Shaper.ShaperNode> Disc = r => Laubrary.Shaper.ShaperNode.Primitive(
  new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx=r, ellipseRy=r }, "D");
System.Func<UnityEngine.Texture2D> Checker = () => { var t=new UnityEngine.Texture2D(8,8,UnityEngine.TextureFormat.RGBA32,false);
  var p=new UnityEngine.Color32[64]; for(int y=0;y<8;y++)for(int x=0;x<8;x++) p[y*8+x]=((x+y)&1)==0?new UnityEngine.Color32(255,60,20,255):new UnityEngine.Color32(20,60,255,128);
  t.SetPixels32(p); t.Apply(); return t; };
System.Func<ZuiGradient> RG = () => { var g=new ZuiGradient(); g.gradient=new UnityEngine.Gradient();
  g.gradient.SetKeys(new[]{new UnityEngine.GradientColorKey(UnityEngine.Color.red,0f),new UnityEngine.GradientColorKey(UnityEngine.Color.green,0.5f),new UnityEngine.GradientColorKey(UnityEngine.Color.blue,1f)},
                     new[]{new UnityEngine.GradientAlphaKey(1f,0f),new UnityEngine.GradientAlphaKey(1f,1f)}); g.EnsureTransformAnim(); return g; };

var cases = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,Laubrary.Shaper.ShaperFillDef>>();
System.Action<string,Laubrary.Shaper.ShaperFillDef> Add = (n,d)=>cases.Add(new System.Collections.Generic.KeyValuePair<string,Laubrary.Shaper.ShaperFillDef>(n,d));
Add("Solid", new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Solid, solidColor=new UnityEngine.Color(0.8f,0.3f,0.1f) });
foreach(Laubrary.Shaper.ShaperGradientMode gm in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperGradientMode)))
 foreach(Laubrary.Shaper.ShaperFillSpace sp in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperFillSpace)))
  foreach(Laubrary.Shaper.ShaperFillFit ft in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperFillFit)))
   Add("Grad."+gm+"/"+sp+"/"+ft, new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Gradient,
      gradientMode=gm, gradient=RG(), space=sp, fit=ft, gradientSize=new ZUIValue(1f), gradientDepthPixels=new ZUIValue(10f) });
foreach(Laubrary.Shaper.ShaperQuantity q in new[]{Laubrary.Shaper.ShaperQuantity.Coverage, Laubrary.Shaper.ShaperQuantity.EdgeDistance})
   Add("Ramp."+q, new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.RampByQuantity, rampQuantity=q, rampGradient=RG(),
      rampInputLow=new ZUIValue(q==Laubrary.Shaper.ShaperQuantity.EdgeDistance?-30f:0f), rampInputHigh=new ZUIValue(q==Laubrary.Shaper.ShaperQuantity.EdgeDistance?0f:1f) });
foreach(Laubrary.Shaper.ShaperTextureMapping mp in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperTextureMapping)))
 foreach(Laubrary.Shaper.ShaperFillSpace sp in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperFillSpace)))
   Add("Tex."+mp+"/"+sp, new Laubrary.Shaper.ShaperFillDef{ kind=Laubrary.Shaper.ShaperFillKind.Texture, texture=Checker(),
      textureMapping=mp, space=sp, textureTilesX=new ZUIValue(3f), textureTilesY=new ZUIValue(3f) });

sb.AppendLine("== H: the INTEGRATED entry point (ShaperFillResolver.PaintTile) vs the DIRECT call (ShaperFillOps.FillTile) ==");
sb.AppendLine("   For each case: (1) does the integrated dst equal a hand-composite of the direct output?  (2) does the");
sb.AppendLine("   integrated output actually VARY across the shape, or is it flat (the 'Ramp rendered flat black' class)?");
sb.AppendLine();
sb.AppendLine("   case                                 maxDelta(int vs direct) | distinct RGB values inside the disc | flat?");
int nFlat=0, nDelta=0;
foreach(var kv in cases){
  foreach(Laubrary.Shaper.ShaperFillComposite comp in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperFillComposite))){
   var def = kv.Value; def.composite = comp;
   var root = Disc(36f); root.fill = def;
   var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, CHW, CHH);
   var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
   var sh = new Laubrary.Shaper.ShaperFillSheets{ published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine };
   Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid,0,0,W,H,buf,sh);
   // direct: re-run the fill against the SAME per-owner sheets the integrated path used, then hand-composite
   var dAl=new float[W*H*3]; var dVe=new float[W*H]; var dHt=new float[W*H];
   var ownSheets = Laubrary.Shaper.ShaperFillSheets.FromShapeStage(buf.ownCoverage, buf.ownDistance);
   Laubrary.Shaper.ShaperFillOps.FillTile(doc.owners[0].fill, grid,0,0,W,H, ownSheets,
      new Laubrary.Shaper.ShaperFillEmit{albedo=dAl,veil=dVe,heightDelta=dHt}, 0,W,0,W);
   float maxD=0;
   var seen = new System.Collections.Generic.HashSet<int>();
   for(int i=0;i<W*H;i++){
     float ce = UnityEngine.Mathf.Clamp01(buf.paint[i]) * UnityEngine.Mathf.Clamp01(dVe[i]);
     float er,eg,eb,ea;
     if(comp==Laubrary.Shaper.ShaperFillComposite.Add){ er=dAl[i*3]*ce; eg=dAl[i*3+1]*ce; eb=dAl[i*3+2]*ce; ea=0f; }
     else { er=dAl[i*3]*ce; eg=dAl[i*3+1]*ce; eb=dAl[i*3+2]*ce; ea=ce; }
     maxD = UnityEngine.Mathf.Max(maxD, UnityEngine.Mathf.Abs(er-buf.dst[i*4]));
     maxD = UnityEngine.Mathf.Max(maxD, UnityEngine.Mathf.Abs(eg-buf.dst[i*4+1]));
     maxD = UnityEngine.Mathf.Max(maxD, UnityEngine.Mathf.Abs(eb-buf.dst[i*4+2]));
     maxD = UnityEngine.Mathf.Max(maxD, UnityEngine.Mathf.Abs(ea-buf.dst[i*4+3]));
     if(buf.ownCoverage[i] > 0.999f){
       int key = (UnityEngine.Mathf.RoundToInt(buf.dst[i*4]*255f)<<16) | (UnityEngine.Mathf.RoundToInt(buf.dst[i*4+1]*255f)<<8) | UnityEngine.Mathf.RoundToInt(buf.dst[i*4+2]*255f);
       seen.Add(key);
     }
   }
   bool flat = seen.Count <= 1;
   bool expectFlat = kv.Key=="Solid";
   if(flat && !expectFlat) nFlat++;
   if(maxD > 1e-6f) nDelta++;
   if((flat && !expectFlat) || maxD > 1e-6f || comp==Laubrary.Shaper.ShaperFillComposite.Over)
     sb.AppendLine("   " + (kv.Key+"/"+comp).PadRight(36) + " " + maxD.ToString("E2").PadLeft(10) + "  |  " + seen.Count.ToString().PadLeft(5) +
        "  | " + (flat ? (expectFlat?"flat (expected)":"*** FLAT - UNEXPECTED ***") : "varies"));
  }
}
sb.AppendLine();
sb.AppendLine("   cases where integrated != hand-composite of direct: " + nDelta + " (expected 0)");
sb.AppendLine("   non-Solid cases rendering FLAT through the integrated path: " + nFlat + " (expected 0)");
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r16_int.txt", sb.ToString());
return "ok";
