string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/nanrender.txt";
var sb = new System.Text.StringBuilder();
int W = 128, H = 128; float Px = 1f;

System.Func<float,float,float,float, string> run = (ambI, rimS, rimP, specP) => {
  var n = Laubrary.Shaper.ShaperNode.Primitive(
      new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = 40f, rectHalfH = 40f },
      "Square", Laubrary.Shaper.ShaperCombineMode.Add);
  n.fill = new Laubrary.Shaper.ShaperFillDef {
      kind = Laubrary.Shaper.ShaperFillKind.Solid,
      solidColor = new UnityEngine.Color(0.8f,0.6f,0.4f,1f),
      veil = new ZUIValue(1f), heightDelta = new ZUIValue(0f),
      composite = Laubrary.Shaper.ShaperFillComposite.Over };

  var rig = new Laubrary.Shaper.ShaperLightRig();
  rig.ambientColour = UnityEngine.Color.white;
  rig.ambientIntensity = new ZUIValue(ambI);
  rig.lights.Add(new Laubrary.Shaper.ShaperLight {
      enabled = true, kind = Laubrary.Shaper.ShaperLightKind.Directional,
      colour = UnityEngine.Color.white, intensity = new ZUIValue(1f),
      yaw = new ZUIValue(-55f), pitch = new ZUIValue(36f), specular = new ZUIValue(1f) });

  var resp = new Laubrary.Shaper.ShaperLightResponse();
  resp.receiveLighting = true;
  resp.intensityScale = new ZUIValue(1f);
  resp.rimStrength = new ZUIValue(rimS);
  resp.rimPower = new ZUIValue(rimP);
  resp.specular = new ZUIValue(0.9f);
  resp.specularPower = new ZUIValue(specP);
  resp.normalConstant = new UnityEngine.Vector3(0f,0f,1f);

  float chw = 0.5f*(W-1)*Px, chh = 0.5f*(H-1)*Px;
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(n, 0f, 0u, chw, chh, Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine);
  int k = UnityEngine.Mathf.Max(1, doc.owners.Count);
  var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, k);
  var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
  var scene = new Laubrary.Shaper.ShaperLightScene(buf.sampleCapacity, buf.ownerCapacity);
  var prog = Laubrary.Shaper.ShaperLightCompiler.Compile(rig, 0f, 0u);
  scene.rig = prog.rig;
  var rc = Laubrary.Shaper.ShaperLightCompiler.CompileResponse(resp, "Layer", 0f, 0u, prog);
  var no = Laubrary.Shaper.ShaperLightCompiler.CompileNormal(resp);
  scene.SetAll(rc, no);
  Laubrary.Shaper.ShaperLightCompiler.Finish(prog);
  var sheets = new Laubrary.Shaper.ShaperFillSheets { published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine };
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, 0,0,W,H, buf, sheets, scene);

  int nan=0, inf=0, nanA=0;
  for (int i=0;i<W*H;i++){
    for(int c=0;c<4;c++){ float v=buf.dst[i*4+c];
      if (float.IsNaN(v)) { nan++; if(c==3) nanA++; }
      else if (float.IsInfinity(v)) inf++; }
  }
  var px = new UnityEngine.Color32[W*H];
  Laubrary.Shaper.ShaperFillResolver.Encode(buf.dst, px, W*H);
  int weird=0; for(int i=0;i<W*H;i++) if (px[i].r==0 && px[i].a!=0) weird++;
  return "dst NaN floats=" + nan + " (alpha NaN=" + nanA + ")  Inf floats=" + inf + "  encoded r==0&a!=0 px=" + weird;
};

sb.AppendLine("baseline  rimP=2.2 rimS=1 amb=0.2 specP=48 : " + run(0.2f, 1f, 2.2f, 48f));
sb.AppendLine("rimP=-1   rimS=1 amb=0.2            : " + run(0.2f, 1f, -1f, 48f));
sb.AppendLine("rimP=-1   rimS=1 amb=0.0 (NaN case) : " + run(0f,   1f, -1f, 48f));
sb.AppendLine("specP=-4  rimS=0 amb=0.2            : " + run(0.2f, 0f, 2.2f, -4f));
System.IO.File.WriteAllText(o, sb.ToString());
return "ok";
