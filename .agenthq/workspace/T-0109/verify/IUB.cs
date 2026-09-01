using System;
using UnityEngine;
using Laubrary.Shaper;

// T-0109 FIXER — the MIRROR of ILB, for the bound the fixed march's SOLID-space skip rests on.
//
// InverseUpperBound must be an UPPER bound on Ginv over the WHOLE canvas: for every (nx,ny) and
// every t >= tauMax, G(t,nx,ny) >= zeta. A violation means the CONTAINED prism is not contained,
// which is how a marcher skips forward through AIR believing it is solid and loses a crossing pair.
// The pre-fix code used the LOWER bound here, which is exactly this property failing on Linear.
public static class IUB {
  public static void Run(){
    Console.WriteLine("=== IUB - InverseUpperBound must be an UPPER bound over the WHOLE canvas (T-0109 FIX F1) ===");
    Console.WriteLine("Property: for every (nx,ny) and every t >= tauMax, G(t,nx,ny) >= zeta.");
    Console.WriteLine("Also run against the PRE-FIX choice (InverseLowerBound) to show the property genuinely fails there.");
    for(int pass=0; pass<2; pass++){
      bool useLower = pass==1;
      long checks=0; long viol=0; double worst=0; string at="";
      foreach(var tech in Fixtures.Techs)
       foreach(var bev in Fixtures.Bevels)
        foreach(float ang in new float[]{-180f,-135f,-90f,-45f,0f,45f,90f,135f,180f})
         foreach(float am in new float[]{0.05f,0.25f,0.6f,1f}){
          if(tech!=ShaperExtrusionTechnique.Linear && ang!=45f) continue;
          var op = Fixtures.Make(tech,bev,4f,1f,1f,am,3f,1f,ang);
          for(int i=1;i<=400;i++){
            float zeta = (i/400f)*op.supG;
            float tau = useLower ? ShaperHeight.InverseLowerBound(op, zeta)
                                 : ShaperHeight.InverseUpperBound(op, zeta);
            if(ShaperHeight.IsNoCrossSection(tau)) continue;   // no contained prism claimed: nothing to check
            for(int gx=-6;gx<=6;gx++) for(int gy=-6;gy<=6;gy++){
              float nx=gx/6f, ny=gy/6f;
              for(int k=0;k<=40;k++){
                float t = tau + (1f-tau)*(k/40f);
                if(t<tau) continue;
                float g = ShaperHeight.Composed(op,t,nx,ny);
                checks++;
                if(g < zeta){ viol++; double m = zeta-g;
                  if(m>worst){worst=m; at=tech+"+"+bev+" angle="+ang+" amount="+am+" zeta="+zeta+" tauMax="+tau+" t="+t+" G="+g+" (nx="+nx+",ny="+ny+")";} }
              }
            }
          }
         }
      Console.WriteLine((useLower ? "  PRE-FIX (InverseLowerBound used for the contained prism): "
                                  : "  SHIPPED (InverseUpperBound):                             ")
                        + "checks="+checks+"  violations="+viol+"  worst shortfall in zeta = "+worst.ToString("E3"));
      if(at!="") Console.WriteLine("      worst: "+at);
    }
  }
}
