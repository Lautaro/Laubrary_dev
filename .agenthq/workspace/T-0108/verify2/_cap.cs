string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/cap.txt";
var sb = new System.Text.StringBuilder();
System.Func<float,Laubrary.Shaper.ShaperLight> mk = (yaw) => new Laubrary.Shaper.ShaperLight {
  enabled=true, kind=Laubrary.Shaper.ShaperLightKind.Directional, colour=UnityEngine.Color.white,
  yaw=new ZUIValue(yaw), pitch=new ZUIValue(20f), intensity=new ZUIValue(1f), specular=new ZUIValue(1f) };

System.Action<string,int,int[]> test = (label, total, disabledIdx) => {
  var rig = new Laubrary.Shaper.ShaperLightRig();
  rig.ambientColour = UnityEngine.Color.white; rig.ambientIntensity = new ZUIValue(0.1f);
  for (int i=0;i<total;i++){ var l = mk(i*20f); rig.lights.Add(l); }
  foreach (int d in disabledIdx) rig.lights[d].enabled = false;
  var prog = Laubrary.Shaper.ShaperLightCompiler.Compile(rig, 0f, 0u);
  int enabled = total - disabledIdx.Length;
  int actuallyLit = prog.rig.count;
  int actuallyDropped = enabled - actuallyLit;
  sb.AppendLine(label);
  sb.AppendLine("   authored=" + total + " enabled=" + enabled + " compiled(count)=" + actuallyLit
                + "  TRULY dropped=" + actuallyDropped);
  sb.AppendLine("   hasTooManyLights=" + prog.hasTooManyLights + "  tooManyLightsCount=" + prog.tooManyLightsCount);
  sb.AppendLine("   reason: " + (prog.tooManyLightsReason ?? "(none)"));
  bool lies = prog.hasTooManyLights && actuallyDropped == 0;
  bool miscount = prog.hasTooManyLights && prog.tooManyLightsCount != actuallyDropped;
  sb.AppendLine("   >>> FALSE ALARM (claims a drop, none happened): " + lies
                + "   |  COUNT WRONG (says " + prog.tooManyLightsCount + ", truth " + actuallyDropped + "): " + miscount);
  sb.AppendLine();
};
test("A. 11 authored, all enabled  (the audit's LT-11 case)", 11, new int[]{});
test("B.  9 authored, the 9th DISABLED - nothing is actually dropped", 9, new int[]{8});
test("C. 11 authored, 5 disabled -> only 6 enabled, all 6 fit", 11, new int[]{2,4,6,8,10});
test("D. 12 authored, first 8 DISABLED -> 4 enabled, all 4 fit", 12, new int[]{0,1,2,3,4,5,6,7});
test("E.  8 authored, all enabled (control, no diagnostic expected)", 8, new int[]{});
System.IO.File.WriteAllText(o, sb.ToString());
return "ok";
