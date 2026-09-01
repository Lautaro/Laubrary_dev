string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/deg.txt";
var sb = new System.Text.StringBuilder();

System.Func<float,float,float,float,float,float,string> atten = (posX,posY,posZ,range,px,py) => {
  var rig = new Laubrary.Shaper.ShaperLightRig();
  rig.ambientColour=UnityEngine.Color.black; rig.ambientIntensity=new ZUIValue(0f);
  rig.lights.Add(new Laubrary.Shaper.ShaperLight{ enabled=true, kind=Laubrary.Shaper.ShaperLightKind.Point,
    colour=UnityEngine.Color.white, posX=new ZUIValue(posX), posY=new ZUIValue(posY), posZ=new ZUIValue(posZ),
    range=new ZUIValue(range), intensity=new ZUIValue(1f), specular=new ZUIValue(1f)});
  var prog = Laubrary.Shaper.ShaperLightCompiler.Compile(rig,0f,0u);
  var l0 = prog.rig.At(0);
  var resp = new Laubrary.Shaper.ShaperLightResponse{ receiveLighting=true, intensityScale=new ZUIValue(1f),
    rimStrength=new ZUIValue(0f), rimPower=new ZUIValue(2.2f), specular=new ZUIValue(0f),
    specularPower=new ZUIValue(48f), specularTint=UnityEngine.Color.white };
  var rc = Laubrary.Shaper.ShaperLightCompiler.CompileResponse(resp,"L",0f,0u,prog);
  float lr,lg,lb,sr,sg,sbv;
  Laubrary.Shaper.ShaperLightLaw.Shade(prog.rig, rc, px,py,0f, 0f,0f,1f, 0f,0f,1f,
      out lr,out lg,out lb,out sr,out sg,out sbv);
  bool bad = float.IsNaN(lr)||float.IsInfinity(lr)||float.IsNaN(sr)||float.IsInfinity(sr);
  return "invRangeSq=" + l0.invRangeSq.ToString("E3") + "  L=" + lr.ToString("F6") + "  S=" + sr.ToString("F6")
       + (bad ? "   *** NON-FINITE" : "");
};
sb.AppendLine("POINT-LIGHT RANGE GUARD (ShaperLightCompiler.cs:143  range > 1e-6 ? 1/r^2 : 0)");
sb.AppendLine("  sample at canvas (0,0), light at (0,0,40):");
foreach (float r in new float[]{40f, 1f, 0.01f, 1e-4f, 1e-5f, 1e-6f, 1e-7f, 0f})
  sb.AppendLine("   range=" + r.ToString("E1").PadRight(9) + " -> " + atten(0f,0f,40f,r,0f,0f));
sb.AppendLine("  NOTE: atten = 1/(1+dist^2*invRangeSq); invRangeSq=0 means atten==1 at EVERY distance.");
sb.AppendLine();
sb.AppendLine("LIGHT EXACTLY AT THE SAMPLE POINT (dist = 0), range 40:");
sb.AppendLine("   " + atten(0f,0f,0f,40f,0f,0f));
sb.AppendLine("LIGHT AT THE SAMPLE POINT WITH range 0 (both degeneracies at once):");
sb.AppendLine("   " + atten(0f,0f,0f,0f,0f,0f));

// eight lights at the same position
{
  var rig = new Laubrary.Shaper.ShaperLightRig();
  rig.ambientColour=UnityEngine.Color.white; rig.ambientIntensity=new ZUIValue(0.1f);
  for (int i=0;i<8;i++) rig.lights.Add(new Laubrary.Shaper.ShaperLight{ enabled=true,
    kind=Laubrary.Shaper.ShaperLightKind.Point, colour=UnityEngine.Color.white,
    posX=new ZUIValue(0f), posY=new ZUIValue(0f), posZ=new ZUIValue(0f), range=new ZUIValue(0f),
    intensity=new ZUIValue(float.MaxValue), specular=new ZUIValue(1f)});
  var prog = Laubrary.Shaper.ShaperLightCompiler.Compile(rig,0f,0u);
  var resp = new Laubrary.Shaper.ShaperLightResponse{ receiveLighting=true, intensityScale=new ZUIValue(1f),
    rimStrength=new ZUIValue(1f), rimPower=new ZUIValue(2.2f), specular=new ZUIValue(0.9f),
    specularPower=new ZUIValue(0f), specularTint=UnityEngine.Color.white };
  var rc = Laubrary.Shaper.ShaperLightCompiler.CompileResponse(resp,"L",0f,0u,prog);
  float lr,lg,lb,sr,sg,sbv;
  Laubrary.Shaper.ShaperLightLaw.Shade(prog.rig, rc, 0f,0f,0f, 0f,0f,0f, 0f,0f,1f,
      out lr,out lg,out lb,out sr,out sg,out sbv);
  sb.AppendLine();
  sb.AppendLine("EIGHT COINCIDENT POINT LIGHTS, dist 0, range 0, intensity float.MaxValue, ZERO normal (0,0,0), specularPower 0:");
  sb.AppendLine("   L=" + lr + "  S=" + sr + (float.IsNaN(lr)||float.IsNaN(sr)?"  *** NaN":(float.IsInfinity(lr)||float.IsInfinity(sr)?"  *** Inf":"  finite")));
}
System.IO.File.WriteAllText(o, sb.ToString());
return "ok";
