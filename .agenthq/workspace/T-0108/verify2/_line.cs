string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/line.txt";
var sb = new System.Text.StringBuilder();
int W=96,H=96; float Px=1f;
var NS = "Laubrary.Shaper.";

System.Func<float,float,float,object[]> render = (specStrength, lightIntensity, rimS) => {
  var node = Laubrary.Shaper.ShaperNode.Primitive(
      new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = W*0.5f, rectHalfH = H*0.5f },
      "Solid", Laubrary.Shaper.ShaperCombineMode.Add);
  node.fill = new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid,
      solidColor = new UnityEngine.Color(0.85f,0.55f,0.3f,1f), veil = new ZUIValue(1f),
      heightDelta = new ZUIValue(0f), composite = Laubrary.Shaper.ShaperFillComposite.Over };

  var rig = new Laubrary.Shaper.ShaperLightRig();
  rig.ambientColour = UnityEngine.Color.white; rig.ambientIntensity = new ZUIValue(0.18f);
  rig.lights.Add(new Laubrary.Shaper.ShaperLight { enabled=true, kind=Laubrary.Shaper.ShaperLightKind.Directional,
      colour=UnityEngine.Color.white, intensity=new ZUIValue(lightIntensity),
      yaw=new ZUIValue(-55f), pitch=new ZUIValue(36f), specular=new ZUIValue(1f) });

  var resp = new Laubrary.Shaper.ShaperLightResponse();
  resp.receiveLighting = true; resp.intensityScale = new ZUIValue(1f);
  resp.rimStrength = new ZUIValue(rimS); resp.rimPower = new ZUIValue(2.2f);
  resp.specular = new ZUIValue(specStrength); resp.specularPower = new ZUIValue(48f);
  resp.specularTint = new UnityEngine.Color(0.9f,0.95f,1f);

  float chw=0.5f*(W-1)*Px, chh=0.5f*(H-1)*Px;
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(node,0f,0u,chw,chh, Laubrary.Shaper.ShaperSolids.Published);
  int k = UnityEngine.Mathf.Max(1, doc.owners.Count);
  var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H,k);
  var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
  var scene = new Laubrary.Shaper.ShaperLightScene(buf.sampleCapacity, buf.ownerCapacity);
  var prog = Laubrary.Shaper.ShaperLightCompiler.Compile(rig,0f,0u);
  scene.rig = prog.rig;
  scene.SetAll(Laubrary.Shaper.ShaperLightCompiler.CompileResponse(resp,"Layer",0f,0u,prog),
               Laubrary.Shaper.ShaperLightCompiler.CompileNormal(resp));
  Laubrary.Shaper.ShaperLightCompiler.Finish(prog);
  var def = new Laubrary.Shaper.ShaperSolidDef { form = Laubrary.Shaper.ShaperSolidForm.Gem,
      size=new ZUIValue(34f), yaw=new ZUIValue(0f), tilt=new ZUIValue(0f), roll=new ZUIValue(0f),
      lineWidth=new ZUIValue(1.1f), aspect=new ZUIValue(1f), depth=new ZUIValue(1f),
      edgeGlow=new ZUIValue(0f), innerGlow=new ZUIValue(0f) };
  scene.SetSolid(0, Laubrary.Shaper.ShaperSolids.Compile(def,0f,0u));
  var sheets = new Laubrary.Shaper.ShaperFillSheets { published = Laubrary.Shaper.ShaperSolids.Published };
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc,grid,0,0,W,H,buf,sheets,scene);
  return new object[]{ buf, scene };
};

// ---- 1. edge-line specular: count line samples where BLUE exceeds RED in dst ----
var A = render(0.9f, 1f, 0f);   // specular ON  (default)
var B = render(0.0f, 1f, 0f);   // specular OFF
var bufA = (Laubrary.Shaper.ShaperFillBuffers)A[0]; var scA = (Laubrary.Shaper.ShaperLightScene)A[1];
var bufB = (Laubrary.Shaper.ShaperFillBuffers)B[0];
int lineSamples=0, blueA=0, blueB=0; float worstA=0f, worstB=0f;
for (int i=0;i<W*H;i++){
  if (scA.lineMask[i] == 0f) continue;
  if (bufA.dst[i*4+3] <= 0f) continue;
  lineSamples++;
  float ra=bufA.dst[i*4+0], ba=bufA.dst[i*4+2];
  float rb=bufB.dst[i*4+0], bb=bufB.dst[i*4+2];
  if (ba > ra) { blueA++; if (ba-ra > worstA) worstA = ba-ra; }
  if (bb > rb) { blueB++; if (bb-rb > worstB) worstB = bb-rb; }
}
sb.AppendLine("EDGE-LINE SPECULAR (Gem, lineWidth 1.1, specTint (0.9,0.95,1))");
sb.AppendLine("  line samples with alpha>0: " + lineSamples);
sb.AppendLine("  specular ON  : blue-dominant (B>R) line samples = " + blueA + "  worst (B-R) = " + worstA.ToString("F5"));
sb.AppendLine("  specular OFF : blue-dominant (B>R) line samples = " + blueB + "  worst (B-R) = " + worstB.ToString("F5"));

// ---- 2. specular with ZERO diffuse: count samples where ndl==0 for every light yet S>0 ----
int specNoDiffuse = 0; float worstSND = 0f;
for (int i=0;i<W*H;i++){
  if (bufA.dst[i*4+3] <= 0f) continue;
  int t3=i*3;
  float nx=scA.normal[t3], ny=scA.normal[t3+1], nz=scA.normal[t3+2];
  var l0 = scA.rig.At(0);
  float ndl = nx*l0.dirX + ny*l0.dirY + nz*l0.dirZ;
  if (ndl > 0f) continue;
  float hx=l0.dirX+0f, hy=l0.dirY+0f, hz=l0.dirZ+1f;
  float hl=UnityEngine.Mathf.Sqrt(hx*hx+hy*hy+hz*hz);
  float nh=(nx*hx+ny*hy+nz*hz)/hl;
  if (nh > 0f) { specNoDiffuse++; float s=UnityEngine.Mathf.Pow(nh,48f)*0.9f; if (s>worstSND) worstSND=s; }
}
sb.AppendLine("SPECULAR ON UNLIT FACETS (N.L <= 0 yet N.H > 0): " + specNoDiffuse + " samples, worst spec term " + worstSND.ToString("E3"));

// ---- 3. ALPHA INVARIANCE under a light-intensity sweep 0..8 ----
float[] sweep = new float[]{0f,0.5f,1f,2f,4f,8f};
var refBuf = (Laubrary.Shaper.ShaperFillBuffers)render(0.9f, sweep[0], 1.5f)[0];
int alphaDiffTotal=0;
for (int s=1;s<sweep.Length;s++){
  var bb2 = (Laubrary.Shaper.ShaperFillBuffers)render(0.9f, sweep[s], 1.5f)[0];
  int d=0; for(int i=0;i<W*H;i++) if (bb2.dst[i*4+3] != refBuf.dst[i*4+3]) d++;
  alphaDiffTotal += d;
  sb.AppendLine("  ALPHA sweep intensity " + sweep[s].ToString("F1") + " vs 0.0 : differing alpha samples " + d + "/" + (W*H));
}
sb.AppendLine("ALPHA INVARIANCE total differing = " + alphaDiffTotal + " (expected 0)");
System.IO.File.WriteAllText(o, sb.ToString());
return "ok";
