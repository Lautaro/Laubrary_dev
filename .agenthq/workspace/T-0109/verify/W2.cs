using System;
using UnityEngine;
using Laubrary.Shaper;
public static class W2 {
  public static void Run(){
    Console.WriteLine("=== WK - Wall/Cap classification and HS-6.1 edgeDistance==0 on a wall ===");
    var root = March.SolidPlate(150f, 100f);
    var prog = ShaperCompiler.Compile(root, 0f, 0u);
    foreach (var tech in new[]{ShaperExtrusionTechnique.Flat, ShaperExtrusionTechnique.Linear}){
      var def = new ShaperHeightDef{ technique=tech, bevel=ShaperBevelTechnique.None, depth=new ZUIValue(40f) };
      var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
      var scene = new ShaperResolveScene(); scene.Add(prog, op);
      var buf = new ShaperCrossing[16];
      // exactly horizontal ray at mid height: every crossing is a vertical side wall
      var r = ShaperResolve.Query(scene, -400f, 0f, op.baseZ + 20f, 1f, 0f, 0f, buf);
      Console.Write("  "+tech+" horizontal ray: count="+r.count+"  kinds=");
      for(int i=0;i<r.count;i++) Console.Write(buf[i].kind+"("+buf[i].edgeDistance.ToString("F3")+") ");
      Console.WriteLine();
      // a shallow tilt
      var r2 = ShaperResolve.Query(scene, -400f, 0f, op.baseZ + 30f, 1f, 0f, -0.02f, buf);
      Console.Write("  "+tech+" 1.1deg tilt   : count="+r2.count+"  kinds=");
      for(int i=0;i<r2.count;i++) Console.Write(buf[i].kind+"("+buf[i].edgeDistance.ToString("F3")+") ");
      Console.WriteLine();
    }
    Console.WriteLine();
    Console.WriteLine("=== LN - Linear profile: is height <= body? (HS-2.3 says NO) ===");
    var d2 = new ShaperHeightDef{ technique=ShaperExtrusionTechnique.Linear, bevel=ShaperBevelTechnique.None, depth=new ZUIValue(40f), angle=new ZUIValue(45f) };
    var op2 = ShaperHeightCompiler.Compile(d2, prog, 1f, 0f, 0f, 0u);
    float maxh=0;
    for(int i=-150;i<=150;i+=3) for(int j=-100;j<=100;j+=3){
      float nx,ny; ShaperHeight.LocalNormalised(op2, i, j, out nx, out ny);
      float h = ShaperHeight.Height(op2, 0.5f, nx, ny); if(h>maxh) maxh=h;
    }
    Console.WriteLine("  body=40  max height over the support box = "+maxh+"   supG*body = "+(op2.supG*op2.body));
  }
}
