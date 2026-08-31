using System; using UnityEngine; using Laubrary.Shaper;
public static class F3 {
  static float NextAfter(float v,int steps){ int bits=BitConverter.SingleToInt32Bits(v); int ord=bits<0?int.MinValue-bits:bits;
    long mv=(long)ord+steps; if(mv>int.MaxValue)mv=int.MaxValue; if(mv<int.MinValue)mv=int.MinValue; int o2=(int)mv;
    int b2=o2<0?int.MinValue-o2:o2; float r=BitConverter.Int32BitsToSingle(b2); return float.IsNaN(r)?v:r; }
  public static void Run(){
  var def = new ShaperHeightDef{ technique=ShaperExtrusionTechnique.Flat, bevel=ShaperBevelTechnique.Rounded,
    depth=new ZUIValue(10f), bevelAmount=new ZUIValue(0.05f), bevelSteps=new ZUIValue(3f) };
  var op = ShaperHeightCompiler.Compile(def, null, 32f, 0f, 0f, 0u);
  Console.WriteLine("a="+op.a.ToString("R"));
  // (i) the audit's band-spread probe, exactly
  int drops=0; float worst=0; string where="";
  for(int q=0;q<=2000;q++){ float t0=op.a*q/2000f; float t1=NextAfter(t0,1);
    float g0=ShaperHeight.Composed(op,t0,0,0), g1=ShaperHeight.Composed(op,t1,0,0);
    if(g0-g1>0){drops++; if(g0-g1>worst){worst=g0-g1; where=t0.ToString("R")+"->"+t1.ToString("R")+" "+g0.ToString("R")+"->"+g1.ToString("R");}} }
  Console.WriteLine("audit band-spread probe: drops="+drops+" worst="+worst.ToString("E4")+" "+where);
  // (ii) exhaustive adjacent-float scan over the top 200000 ulps below a
  drops=0; worst=0; where="";
  for(int k=200000;k>=1;k--){ float t0=NextAfter(op.a,-k); float t1=NextAfter(t0,1);
    float g0=ShaperHeight.Composed(op,t0,0,0), g1=ShaperHeight.Composed(op,t1,0,0);
    if(g0-g1>0){drops++; if(g0-g1>worst){worst=g0-g1; where=t0.ToString("R")+"->"+t1.ToString("R")+" "+g0.ToString("R")+"->"+g1.ToString("R");}} }
  Console.WriteLine("exhaustive top-200k-ulp scan: drops="+drops+" worst="+worst.ToString("E4")+" "+where);
  // (iii) the pair the earlier probe reported
  float A=0.0499f, B=NextAfter(A,1);
  Console.WriteLine("0.0499 pair: G="+ShaperHeight.Composed(op,A,0,0).ToString("R")+" -> "+ShaperHeight.Composed(op,B,0,0).ToString("R"));
}}
