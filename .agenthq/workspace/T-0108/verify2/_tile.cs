string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/tile.txt";
var sb = new System.Text.StringBuilder();
float Px = 1f;
System.Func<float,float,float,float,UnityEngine.Color,float,Laubrary.Shaper.ShaperLight> pnt =
 (x,y,z,rng,c,inten) => new Laubrary.Shaper.ShaperLight { enabled=true, kind=Laubrary.Shaper.ShaperLightKind.Point,
   colour=c, posX=new ZUIValue(x), posY=new ZUIValue(y), posZ=new ZUIValue(z), range=new ZUIValue(rng),
   intensity=new ZUIValue(inten), specular=new ZUIValue(1f) };
System.Func<float,float,UnityEngine.Color,float,Laubrary.Shaper.ShaperLight> dir =
 (yaw,pit,c,inten) => new Laubrary.Shaper.ShaperLight { enabled=true, kind=Laubrary.Shaper.ShaperLightKind.Directional,
   colour=c, yaw=new ZUIValue(yaw), pitch=new ZUIValue(pit), intensity=new ZUIValue(inten), specular=new ZUIValue(1f) };

System.Func<Laubrary.Shaper.ShaperLightRig> mkRig = () => {
  var r = new Laubrary.Shaper.ShaperLightRig();
  r.ambientColour = UnityEngine.Color.white; r.ambientIntensity = new ZUIValue(0.12f);
  r.lights.Add(dir(-55f,36f,new UnityEngine.Color(1f,0.94f,0.85f),0.9f));
  r.lights.Add(pnt(1.5f,0.5f,6f,14f,new UnityEngine.Color(0.35f,0.7f,1f),1.2f));   // near a 3x2 tile seam
  r.lights.Add(pnt(-13.5f,11.5f,5f,12f,new UnityEngine.Color(1f,0.4f,0.2f),1.0f)); // near a 13x11 tile seam
  r.lights.Add(dir(120f,20f,new UnityEngine.Color(0.4f,0.55f,1f),0.5f));
  r.lights.Add(pnt(6.5f,-4.5f,4f,9f,new UnityEngine.Color(0.3f,1f,0.6f),0.9f));
  r.lights.Add(dir(0f,80f,new UnityEngine.Color(0.9f,0.9f,1f),0.3f));
  r.lights.Add(pnt(0f,0f,30f,40f,UnityEngine.Color.white,0.45f));
  r.lights.Add(dir(200f,-15f,new UnityEngine.Color(0.7f,0.3f,0.9f),0.35f));
  return r; };

System.Func<int,int,bool,object[]> build = (W,H,solids) => {
  var node = Laubrary.Shaper.ShaperNode.Primitive(
     new Laubrary.Shaper.ShaperPrimitiveDef { kind=Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW=W*0.5f, rectHalfH=H*0.5f },
     "N", Laubrary.Shaper.ShaperCombineMode.Add);
  if (!solids) {
    node = Laubrary.Shaper.ShaperNode.Primitive(
     new Laubrary.Shaper.ShaperPrimitiveDef { kind=Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx=13f, ellipseRy=13f },
     "D", Laubrary.Shaper.ShaperCombineMode.Add);
    node.border = new Laubrary.Shaper.ShaperBorderDef { enabled=true, width=new ZUIValue(2.5f) };
  }
  node.fill = new Laubrary.Shaper.ShaperFillDef { kind=Laubrary.Shaper.ShaperFillKind.Solid,
     solidColor=new UnityEngine.Color(0.9f,0.5f,0.2f), veil=new ZUIValue(1f), heightDelta=new ZUIValue(0f),
     composite=Laubrary.Shaper.ShaperFillComposite.Over };
  var resp = new Laubrary.Shaper.ShaperLightResponse { receiveLighting=true, intensityScale=new ZUIValue(1f),
     rimStrength=new ZUIValue(0.6f), rimPower=new ZUIValue(2.2f), specular=new ZUIValue(0.9f),
     specularPower=new ZUIValue(24f), specularTint=new UnityEngine.Color(0.9f,0.95f,1f),
     normalConstant=new UnityEngine.Vector3(0.6f,0f,0.8f) };
  var pub = solids ? Laubrary.Shaper.ShaperSolids.Published : Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine;
  float chw=0.5f*(W-1)*Px, chh2=0.5f*(H-1)*Px;
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(node,0.37f,5u,chw,chh2,pub);
  int k=UnityEngine.Mathf.Max(1,doc.owners.Count);
  var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
  var prog = Laubrary.Shaper.ShaperLightCompiler.Compile(mkRig(),0.37f,5u);
  var rc = Laubrary.Shaper.ShaperLightCompiler.CompileResponse(resp,"L",0.37f,5u,prog);
  var no = Laubrary.Shaper.ShaperLightCompiler.CompileNormal(resp);
  var sdef = new Laubrary.Shaper.ShaperSolidDef { form=Laubrary.Shaper.ShaperSolidForm.Gem,
     size=new ZUIValue(13f), yaw=new ZUIValue(24f), tilt=new ZUIValue(17f), roll=new ZUIValue(9f),
     lineWidth=new ZUIValue(1.1f), aspect=new ZUIValue(1f), depth=new ZUIValue(1f),
     edgeGlow=new ZUIValue(0f), innerGlow=new ZUIValue(0f) };
  return new object[]{ doc, grid, k, rc, no, prog, sdef, pub };
};

System.Func<int,int,bool,int,int,int> compare = (W,H,solids,TW,TH) => {
  var b = build(W,H,solids);
  var doc=(Laubrary.Shaper.ShaperFillDocument)b[0]; var grid=(Laubrary.Shaper.ShaperSampleGrid)b[1];
  int k=(int)b[2]; var rc=(Laubrary.Shaper.ShaperResponseCompiled)b[3]; var no=(Laubrary.Shaper.ShaperNormalOp)b[4];
  var prog=(Laubrary.Shaper.ShaperLightProgram)b[5]; var sdef=(Laubrary.Shaper.ShaperSolidDef)b[6];
  var pub=(Laubrary.Shaper.ShaperQuantitySet)b[7];
  var sheets = new Laubrary.Shaper.ShaperFillSheets { published = pub };
  // whole
  var wb = new Laubrary.Shaper.ShaperFillBuffers(W*H,k);
  var ws = new Laubrary.Shaper.ShaperLightScene(wb.sampleCapacity, wb.ownerCapacity);
  ws.rig=prog.rig; ws.SetAll(rc,no); if (solids) ws.SetSolid(0, Laubrary.Shaper.ShaperSolids.Compile(sdef,0.37f,5u));
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc,grid,0,0,W,H,wb,sheets,ws);
  var refPx = new UnityEngine.Color32[W*H];
  Laubrary.Shaper.ShaperFillResolver.Encode(wb.dst, refPx, W*H);
  // tiled - ONE buffer + ONE scene reused, tiles NOT dividing the canvas
  var tb = new Laubrary.Shaper.ShaperFillBuffers(TW*TH,k);
  var ts = new Laubrary.Shaper.ShaperLightScene(tb.sampleCapacity, tb.ownerCapacity);
  ts.rig=prog.rig; ts.SetAll(rc,no); if (solids) ts.SetSolid(0, Laubrary.Shaper.ShaperSolids.Compile(sdef,0.37f,5u));
  var outPx = new UnityEngine.Color32[W*H]; var tilePx = new UnityEngine.Color32[TW*TH];
  for (int ty=0; ty<H; ty+=TH) for (int tx=0; tx<W; tx+=TW) {
    Laubrary.Shaper.ShaperFillResolver.PaintTile(doc,grid,tx,ty,TW,TH,tb,sheets,ts);
    Laubrary.Shaper.ShaperFillResolver.Encode(tb.dst, tilePx, TW*TH);
    for (int j=0;j<TH;j++) for(int i=0;i<TW;i++){ int gx=tx+i, gy=ty+j; if (gx>=W||gy>=H) continue;
      outPx[gy*W+gx] = tilePx[j*TW+i]; }
  }
  int bad=0; for(int i=0;i<W*H;i++){ var a=refPx[i]; var c=outPx[i];
    if (a.r!=c.r||a.g!=c.g||a.b!=c.b||a.a!=c.a) bad++; }
  return bad;
};

sb.AppendLine("HARDER TILE INDEPENDENCE - 8 lights (4 point, incl. lights near tile seams), canvas 41x29 (prime-ish)");
int[][] tiles = new int[][]{ new int[]{7,5}, new int[]{3,2}, new int[]{13,11}, new int[]{1,1}, new int[]{41,29} };
foreach (var t in tiles) {
  int c1 = compare(41,29,false,t[0],t[1]);
  int c2 = compare(41,29,true, t[0],t[1]);
  sb.AppendLine("  tiles " + t[0] + "x" + t[1] + " : Constant+border differing px = " + c1 + "/1189   Solids Gem differing px = " + c2 + "/1189");
}
System.IO.File.WriteAllText(o, sb.ToString());
return "ok";
