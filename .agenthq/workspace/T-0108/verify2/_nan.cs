string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/nan.txt";
var sb = new System.Text.StringBuilder();
var rigT = System.Type.GetType("Laubrary.Shaper.ShaperLightRigCompiled, com.Lautaro-Arino.Laubrary.Shaper");
var respT = System.Type.GetType("Laubrary.Shaper.ShaperResponseCompiled, com.Lautaro-Arino.Laubrary.Shaper");
var lawT = System.Type.GetType("Laubrary.Shaper.ShaperLightLaw, com.Lautaro-Arino.Laubrary.Shaper");
var lightT = System.Type.GetType("Laubrary.Shaper.ShaperLightCompiled, com.Lautaro-Arino.Laubrary.Shaper");
if (rigT==null||lawT==null) { System.IO.File.WriteAllText(o,"TYPE MISSING"); return "bad"; }
var shade = lawT.GetMethod("Shade");

System.Func<int,float,float,float,float,float,float,float,float,float,object> mkLight = (kind,dx,dy,dz,px,py,pz,r,invRsq,spec) => {
  object l = System.Activator.CreateInstance(lightT);
  lightT.GetField("kind").SetValue(l,kind);
  lightT.GetField("dirX").SetValue(l,dx); lightT.GetField("dirY").SetValue(l,dy); lightT.GetField("dirZ").SetValue(l,dz);
  lightT.GetField("posX").SetValue(l,px); lightT.GetField("posY").SetValue(l,py); lightT.GetField("posZ").SetValue(l,pz);
  lightT.GetField("r").SetValue(l,r); lightT.GetField("g").SetValue(l,r); lightT.GetField("b").SetValue(l,r);
  lightT.GetField("invRangeSq").SetValue(l,invRsq);
  lightT.GetField("specular").SetValue(l,spec);
  return l; };

System.Func<float,float,object> mkRig = (amb, dummy) => {
  object rg = System.Activator.CreateInstance(rigT);
  rigT.GetField("count").SetValue(rg,1);
  rigT.GetField("ambR").SetValue(rg,amb); rigT.GetField("ambG").SetValue(rg,amb); rigT.GetField("ambB").SetValue(rg,amb);
  var setM = rigT.GetMethod("Set");
  setM.Invoke(rg, new object[]{0, mkLight(0,0f,0f,1f,0f,0f,0f,1f,0f,1f)});
  return rg; };

System.Func<float,float,float,float,float,object> mkResp = (rimS,rimP,specV,specP,iscale) => {
  object rp = System.Activator.CreateInstance(respT);
  respT.GetField("receive").SetValue(rp,1);
  respT.GetField("intensityScale").SetValue(rp,iscale);
  respT.GetField("rimStrength").SetValue(rp,rimS);
  respT.GetField("rimPower").SetValue(rp,rimP);
  respT.GetField("specular").SetValue(rp,specV);
  respT.GetField("specularPower").SetValue(rp,specP);
  respT.GetField("specTintR").SetValue(rp,1f); respT.GetField("specTintG").SetValue(rp,1f); respT.GetField("specTintB").SetValue(rp,1f);
  return rp; };

System.Action<string,object,object,float,float,float> run = (label, rg, rp, nx,ny,nz) => {
  object[] args = new object[]{ rg, rp, 0f,0f,0f, nx,ny,nz, 0f,0f,1f, 0f,0f,0f, 0f,0f,0f };
  shade.Invoke(null, args);
  float lr=(float)args[11], sr=(float)args[14];
  string flag = "";
  for (int k=11;k<17;k++){ float v=(float)args[k]; if (float.IsNaN(v)) flag+="NaN "; else if (float.IsInfinity(v)) flag+="Inf "; }
  sb.AppendLine(label + " -> L=" + lr.ToString("R") + " S=" + sr.ToString("R") + (flag==""?"  finite":"  *** "+flag));
};

// baseline
run("A base rimP=2.2 rimS=1 amb=0.2 flatN", mkRig(0.2f,0f), mkResp(1f,2.2f,0.9f,48f,1f), 0f,0f,1f);
// rimPower NEGATIVE, ambient nonzero -> Inf?
run("B rimP=-1 rimS=1 amb=0.2 flatN", mkRig(0.2f,0f), mkResp(1f,-1f,0.9f,48f,1f), 0f,0f,1f);
// rimPower NEGATIVE, ambient ZERO -> NaN?
run("C rimP=-1 rimS=1 amb=0.0 flatN", mkRig(0f,0f), mkResp(1f,-1f,0.9f,48f,1f), 0f,0f,1f);
// rimPower 0 on FLAT normal -> rim should be 0 per LT-7 but pow(0,0)=1
run("D rimP=0  rimS=1 amb=0.2 flatN", mkRig(0.2f,0f), mkResp(1f,0f,0.9f,48f,1f), 0f,0f,1f);
// specularPower 0
run("E specP=0 flatN", mkRig(0.2f,0f), mkResp(0f,2.2f,0.9f,0f,1f), 0f,0f,1f);
// specularPower negative, near-grazing normal -> huge
run("F specP=-4 grazing", mkRig(0.2f,0f), mkResp(0f,2.2f,0.9f,-4f,1f), 0.9999995f,0f,0.001f);
// ambient ZERO kills rim entirely regardless of rimStrength (tilted normal)
run("G rimS=5 amb=0 tiltedN(0.7,0,0.714)", mkRig(0f,0f), mkResp(5f,2.2f,0f,48f,1f), 0.7f,0f,0.71414f);
run("H rimS=5 amb=0.2 tiltedN same", mkRig(0.2f,0f), mkResp(5f,2.2f,0f,48f,1f), 0.7f,0f,0.71414f);
System.IO.File.WriteAllText(o, sb.ToString());
return "ok";
