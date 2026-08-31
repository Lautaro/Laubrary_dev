using System;
using UnityEngine;
using Laubrary.Shaper;
public static class ILB {
  public static void Run(){
    Console.WriteLine("=== ILB - InverseLowerBound must be a LOWER bound over the WHOLE canvas (HS-5.2 / impl 2.5) ===");
    Console.WriteLine("Property: for every (nx,ny) and every t < tauMin, G(t,nx,ny) < zeta. A violation means the");
    Console.WriteLine("containing prism does NOT contain, which is how a marcher steps through solid geometry.");
    long checks=0; int viol=0; double worst=0; string at="";
    foreach(var tech in Fixtures.Techs)
     foreach(var bev in Fixtures.Bevels)
      foreach(float ang in new float[]{-180f,-135f,-90f,-45f,0f,45f,90f,135f,180f})
       foreach(float am in new float[]{0.05f,0.25f,0.6f,1f}){
        if(tech!=ShaperExtrusionTechnique.Linear && ang!=45f) continue;
        var op = Fixtures.Make(tech,bev,4f,1f,1f,am,3f,1f,ang);
        for(int i=1;i<=400;i++){
          float zeta = (i/400f)*op.supG;
          float tau = ShaperHeight.InverseLowerBound(op, zeta);
          bool none = ShaperHeight.IsNoCrossSection(tau);
          for(int gx=-6;gx<=6;gx++) for(int gy=-6;gy<=6;gy++){
            float nx=gx/6f, ny=gy/6f;
            // scan t strictly below tau (or the whole range when "no cross-section" was claimed)
            float hi = none ? 1f : tau;
            for(int k=0;k<40;k++){
              float t = hi*(k/40f);
              if(!none && t>=tau) continue;
              float g = ShaperHeight.Composed(op,t,nx,ny);
              checks++;
              if(g >= zeta){ viol++; double m = g-zeta; if(m>worst){worst=m; at=tech+"+"+bev+" angle="+ang+" amount="+am+" zeta="+zeta+" tauMin="+tau+" t="+t+" G="+g+" (nx="+nx+",ny="+ny+")";} }
            }
            if(none){
              // also check the claim "nothing at this height" at t=1
              float g1 = ShaperHeight.Composed(op,1f,nx,ny); checks++;
              if(g1>=zeta){ viol++; double m=g1-zeta; if(m>worst){worst=m; at="FALSE-NOCROSS "+tech+"+"+bev+" angle="+ang+" zeta="+zeta+" G(1)="+g1;} }
            }
          }
        }
       }
    Console.WriteLine("  checks="+checks+"  violations="+viol+"  worst overshoot in zeta = "+worst.ToString("E3"));
    if(at!="") Console.WriteLine("  "+at);
  }
}
