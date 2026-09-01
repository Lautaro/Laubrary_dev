var sb = new System.Text.StringBuilder();
var IDN = Laubrary.Shaper.ShaperMatrix.Identity;
System.Func<Laubrary.Shaper.ShaperShellAlignment,float,bool,Laubrary.Shaper.ShaperBorderDef> mkB = (a,w,j) => {
    var b = new Laubrary.Shaper.ShaperBorderDef(); b.alignment=a; b.width=new ZUIValue(w); b.joinsCoverage=j; return b; };

// ---- C: BT-5's exact fixture (Disc R=34, ring |d|<halfBand) re-measured ----
{
    float hb = Laubrary.Shaper.ShaperField.HalfBand(0f,1f);
    var kinds = new Laubrary.Shaper.ShaperPrimitiveKind[] {
        Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, Laubrary.Shaper.ShaperPrimitiveKind.Rect,
        Laubrary.Shaper.ShaperPrimitiveKind.Diamond, Laubrary.Shaper.ShaperPrimitiveKind.Triangle,
        Laubrary.Shaper.ShaperPrimitiveKind.Star };
    foreach (var kind in kinds) {
        var pd = new Laubrary.Shaper.ShaperPrimitiveDef(); pd.kind=kind;
        pd.ellipseRx=34f; pd.ellipseRy=34f; pd.rectHalfW=34f; pd.rectHalfH=34f;
        pd.diamondRx=34f; pd.diamondRy=34f; pd.starRadius=34f;
        var node = Laubrary.Shaper.ShaperNode.Primitive(pd, "N");
        node.border = mkB(Laubrary.Shaper.ShaperShellAlignment.Outward, 6f, false);
        var prog = Laubrary.Shaper.ShaperCompiler.Compile(node, IDN, 0f, 0u);
        var rb = Laubrary.Shaper.ShaperBorder.Resolve(node.border, 0f, 0u);
        var strip = Laubrary.Shaper.ShaperBorder.CompileStrip(prog, rb, rb.Joins);
        var s1=prog.NewStack(); var s2=strip.NewStack();
        int band=0, bad=0; float worstD=0; double worst=0;
        for (int y=0;y<128;y++) for (int x=0;x<128;x++) {
            float cx=-0.5f*127+x, cy=-0.5f*127+y;
            float d = Laubrary.Shaper.ShaperEvaluator.Distance(prog,cx,cy,s1);
            if (UnityEngine.Mathf.Abs(d) >= hb) continue;
            band++;
            float cS = Laubrary.Shaper.ShaperField.Coverage(d,hb);
            float cT = Laubrary.Shaper.ShaperField.Coverage(Laubrary.Shaper.ShaperEvaluator.Distance(strip,cx,cy,s2),hb);
            float sum = cT + cS;
            if (System.BitConverter.SingleToInt32Bits(sum)!=System.BitConverter.SingleToInt32Bits(1f)) {
                bad++; double e=System.Math.Abs((double)sum-1.0); if(e>worst){worst=e;worstD=d;} }
        }
        sb.AppendLine("C "+kind+" R34 band|d|<0.5 samples "+band+"  non-bitwise-1 "+bad+"  worst "+worst.ToString("G9")+" at d="+worstD.ToString("G9"));
    }
}

// ---- D: BD-2.2 dilation. Interior of the DILATED silhouette measured against the ORIGINAL field ----
{
    float hb = Laubrary.Shaper.ShaperField.HalfBand(0f,1f);
    var kinds = new Laubrary.Shaper.ShaperPrimitiveKind[] {
        Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, Laubrary.Shaper.ShaperPrimitiveKind.Star,
        Laubrary.Shaper.ShaperPrimitiveKind.Triangle };
    foreach (var kind in kinds) {
        var pd = new Laubrary.Shaper.ShaperPrimitiveDef(); pd.kind=kind;
        pd.ellipseRx=34f; pd.ellipseRy=26f; pd.starRadius=40f; pd.triangleBase=70f; pd.triangleHeight=70f;
        // plain node, no border -> the ORIGINAL field d0
        var plain = Laubrary.Shaper.ShaperNode.Primitive(pd, "N");
        var progPlain = Laubrary.Shaper.ShaperCompiler.Compile(plain, IDN, 0f, 0u);
        var sp = progPlain.NewStack();
        // joined outward border
        var bn = Laubrary.Shaper.ShaperNode.Primitive(pd, "N");
        bn.border = mkB(Laubrary.Shaper.ShaperShellAlignment.Outward, 8f, true);
        var progJoin = Laubrary.Shaper.ShaperCompiler.Compile(bn, IDN, 0f, 0u);
        var sj = progJoin.NewStack();
        var rb = Laubrary.Shaper.ShaperBorder.Resolve(bn.border, 0f, 0u);
        var strip = Laubrary.Shaper.ShaperBorder.CompileStrip(progJoin, rb, rb.Joins);
        var ss = strip.NewStack();
        float reach = rb.reach;
        int interior=0, ruleBad=0, naiveBad=0; float ruleMin=1f, naiveMin=1f;
        int ring=0, ringRuleBad=0, ringNaiveBad=0;
        for (int y=0;y<160;y++) for (int x=0;x<160;x++) {
            float cx=-0.5f*159+x, cy=-0.5f*159+y;
            float d0 = Laubrary.Shaper.ShaperEvaluator.Distance(progPlain,cx,cy,sp);
            if (Laubrary.Shaper.ShaperField.IsEmpty(d0)) continue;
            float dJ = Laubrary.Shaper.ShaperEvaluator.Distance(progJoin,cx,cy,sj);
            float dS = Laubrary.Shaper.ShaperEvaluator.Distance(strip,cx,cy,ss);
            float cRule = Laubrary.Shaper.ShaperField.Coverage(dJ,hb);
            float cNaive = Laubrary.Shaper.ShaperField.Coverage(UnityEngine.Mathf.Min(d0,dS),hb);
            if (d0 <= reach - 1.0f) {                 // strictly inside the dilated silhouette
                interior++;
                if (cRule < 0.999f) ruleBad++;
                if (cNaive < 0.999f) naiveBad++;
                if (cRule < ruleMin) ruleMin = cRule;
                if (cNaive < naiveMin) naiveMin = cNaive;
            }
            if (UnityEngine.Mathf.Abs(d0) < 1.0f) {   // the ORIGINAL silhouette ring: where the seam would be
                ring++;
                if (cRule < 0.999f) ringRuleBad++;
                if (cNaive < 0.999f) ringNaiveBad++;
            }
        }
        sb.AppendLine("D "+kind+" reach "+reach+"  interior "+interior
            +"  rule below0.999 "+ruleBad+" (min "+ruleMin.ToString("F6")+")"
            +"  naive below0.999 "+naiveBad+" (min "+naiveMin.ToString("F6")+")"
            +"  || originalRing "+ring+"  rule bad "+ringRuleBad+"  naive bad "+ringNaiveBad);
    }
}
System.IO.File.AppendAllText("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/v_out.txt", sb.ToString());
return sb.ToString();
