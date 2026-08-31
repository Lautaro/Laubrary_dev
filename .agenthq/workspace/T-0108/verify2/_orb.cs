string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/orb.txt";
var sb = new System.Text.StringBuilder();
int W=96,H=96;

System.Func<Laubrary.Shaper.ShaperSolidForm,float,float,float,UnityEngine.Color32[]> shot =
 (form, aspect, depth, size) => {
  var node = Laubrary.Shaper.ShaperNode.Primitive(
     new Laubrary.Shaper.ShaperPrimitiveDef { kind=Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW=W*0.5f, rectHalfH=H*0.5f },
     "S", Laubrary.Shaper.ShaperCombineMode.Add);
  node.fill = new Laubrary.Shaper.ShaperFillDef { kind=Laubrary.Shaper.ShaperFillKind.Solid,
     solidColor=new UnityEngine.Color(0.9f,0.5f,0.2f), veil=new ZUIValue(1f), heightDelta=new ZUIValue(0f),
     composite=Laubrary.Shaper.ShaperFillComposite.Over };
  var rig = new Laubrary.Shaper.ShaperLightRig();
  rig.ambientColour=UnityEngine.Color.white; rig.ambientIntensity=new ZUIValue(0.15f);
  rig.lights.Add(new Laubrary.Shaper.ShaperLight { enabled=true, kind=Laubrary.Shaper.ShaperLightKind.Directional,
     colour=UnityEngine.Color.white, yaw=new ZUIValue(-55f), pitch=new ZUIValue(36f),
     intensity=new ZUIValue(0.9f), specular=new ZUIValue(1f) });
  var resp = new Laubrary.Shaper.ShaperLightResponse { receiveLighting=true, intensityScale=new ZUIValue(1f),
     rimStrength=new ZUIValue(0f), rimPower=new ZUIValue(2.2f), specular=new ZUIValue(0.9f),
     specularPower=new ZUIValue(48f), specularTint=new UnityEngine.Color(0.9f,0.95f,1f) };
  float chw=0.5f*(W-1), chh=0.5f*(H-1);
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(node,0f,0u,chw,chh, Laubrary.Shaper.ShaperSolids.Published);
  int k=UnityEngine.Mathf.Max(1,doc.owners.Count);
  var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H,k);
  var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,1f);
  var scene = new Laubrary.Shaper.ShaperLightScene(buf.sampleCapacity, buf.ownerCapacity);
  var prog = Laubrary.Shaper.ShaperLightCompiler.Compile(rig,0f,0u);
  scene.rig=prog.rig;
  scene.SetAll(Laubrary.Shaper.ShaperLightCompiler.CompileResponse(resp,"L",0f,0u,prog),
               Laubrary.Shaper.ShaperLightCompiler.CompileNormal(resp));
  var def = new Laubrary.Shaper.ShaperSolidDef { form=form, size=new ZUIValue(size),
     yaw=new ZUIValue(0f), tilt=new ZUIValue(0f), roll=new ZUIValue(0f), lineWidth=new ZUIValue(0f),
     aspect=new ZUIValue(aspect), depth=new ZUIValue(depth),
     edgeGlow=new ZUIValue(0f), innerGlow=new ZUIValue(0f) };
  scene.SetSolid(0, Laubrary.Shaper.ShaperSolids.Compile(def,0f,0u));
  var sheets = new Laubrary.Shaper.ShaperFillSheets { published = Laubrary.Shaper.ShaperSolids.Published };
  Laubrary.Shaper.ShaperFillResolver.PaintTile(doc,grid,0,0,W,H,buf,sheets,scene);
  var px = new UnityEngine.Color32[W*H];
  Laubrary.Shaper.ShaperFillResolver.Encode(buf.dst,px,W*H);
  return px; };

System.Func<UnityEngine.Color32[],UnityEngine.Color32[],int> diff = (a,b) => {
  int d=0; for(int i=0;i<a.Length;i++) if(a[i].r!=b[i].r||a[i].g!=b[i].g||a[i].b!=b[i].b||a[i].a!=b[i].a) d++; return d; };
System.Func<UnityEngine.Color32[],int> cov = (a) => { int c=0; foreach(var p in a) if(p.a>0) c++; return c; };

sb.AppendLine("DIAL RESPONSIVENESS: does 'aspect' / 'depth' change anything, per solid form?");
foreach (var f in new Laubrary.Shaper.ShaperSolidForm[]{ Laubrary.Shaper.ShaperSolidForm.Orb,
        Laubrary.Shaper.ShaperSolidForm.Box, Laubrary.Shaper.ShaperSolidForm.Can,
        Laubrary.Shaper.ShaperSolidForm.Gem, Laubrary.Shaper.ShaperSolidForm.Ring,
        Laubrary.Shaper.ShaperSolidForm.Pyramid }) {
  var b1 = shot(f,1f,1f,30f);
  var bA = shot(f,0.4f,1f,30f);
  var bD = shot(f,1f,0.4f,30f);
  sb.AppendLine("  " + f.ToString().PadRight(8) + " covered px=" + cov(b1)
     + "  aspect 1->0.4 differing px = " + diff(b1,bA)
     + "   depth 1->0.4 differing px = " + diff(b1,bD));
}

// Directional equivalence: (Rot*N).L  vs  N.(Rot^-1 * L), over a dense sample
sb.AppendLine();
sb.AppendLine("D-6 DIRECTIONAL EQUIVALENCE  (Rot*N).L  vs  N.(Rot^-1*L)");
var q = UnityEngine.Quaternion.Euler(25f, 40f, 15f);
var qi = UnityEngine.Quaternion.Inverse(q);
var Lv = new UnityEngine.Vector3(-0.4696f, 0.5878f, 0.6591f).normalized;
double worst=0; int n=0; int bitIdentical=0;
for (int a=0;a<180;a+=3) for (int b2=0;b2<360;b2+=6) {
  float th=a*UnityEngine.Mathf.Deg2Rad, ph=b2*UnityEngine.Mathf.Deg2Rad;
  var N = new UnityEngine.Vector3(UnityEngine.Mathf.Sin(th)*UnityEngine.Mathf.Cos(ph),
                                  UnityEngine.Mathf.Sin(th)*UnityEngine.Mathf.Sin(ph),
                                  UnityEngine.Mathf.Cos(th));
  float lhs = UnityEngine.Vector3.Dot(q*N, Lv);
  float rhs = UnityEngine.Vector3.Dot(N, qi*Lv);
  if (lhs == rhs) bitIdentical++;
  double e = System.Math.Abs((double)lhs-(double)rhs); if (e>worst) worst=e; n++;
}
sb.AppendLine("  samples " + n + "  bit-identical " + bitIdentical + "/" + n
   + "  worst |difference| = " + worst.ToString("E3"));
sb.AppendLine("  (a rotation is an ISOMETRY, so it also preserves |lightPos - P| exactly:");
{
  var P = new UnityEngine.Vector3(7f,-3f,4f); var Lp = new UnityEngine.Vector3(30f,-22f,22f);
  float dPort = (Lp - (q*P)).magnitude;              // port: canvas frame
  float dPyre = ((qi*Lp) - P).magnitude;             // Pyre: inverse-rotated light, model frame
  sb.AppendLine("   point-light distance  port=" + dPort.ToString("F6") + "  Pyre-style=" + dPyre.ToString("F6")
     + "  |diff|=" + UnityEngine.Mathf.Abs(dPort-dPyre).ToString("E3") + ")");
}
System.IO.File.WriteAllText(o, sb.ToString());
return "ok";
