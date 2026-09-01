var sb = new System.Text.StringBuilder();
var IDN = Laubrary.Shaper.ShaperMatrix.Identity;
System.Func<Laubrary.Shaper.ShaperShellAlignment,float,bool,Laubrary.Shaper.ShaperBorderDef> mkB = (a,w,j) => {
    var b = new Laubrary.Shaper.ShaperBorderDef(); b.alignment=a; b.width=new ZUIValue(w); b.joinsCoverage=j; return b; };

System.Action<string,float,float,float,float,float,float,float> run = (tag, rot, sx, sy, kx, ky, tx, ty) => {
    var pd = new Laubrary.Shaper.ShaperPrimitiveDef();
    pd.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse; pd.ellipseRx=24f; pd.ellipseRy=18f;
    System.Func<bool,Laubrary.Shaper.ShaperNode> mk = withB => {
        var n = Laubrary.Shaper.ShaperNode.Primitive(pd, "N");
        n.transform.rotation = rot; n.transform.scale = new UnityEngine.Vector2(sx,sy);
        n.transform.skewDegrees = new UnityEngine.Vector2(kx,ky);
        n.transform.translate = new UnityEngine.Vector2(tx,ty);
        if (withB) n.border = mkB(Laubrary.Shaper.ShaperShellAlignment.Outward, 8f, true);
        return n; };
    var pA = Laubrary.Shaper.ShaperCompiler.Compile(mk(false), IDN, 0f, 0u);
    var nb = mk(true);
    var pB = Laubrary.Shaper.ShaperCompiler.Compile(nb, IDN, 0f, 0u);
    var rb = Laubrary.Shaper.ShaperBorder.Resolve(nb.border, 0f, 0u);
    var strip = Laubrary.Shaper.ShaperBorder.CompileStrip(pB, rb, rb.Joins);
    var sB = pB.NewStack(); var sS = strip.NewStack();

    // 1) anchor box untouched, culling box grew
    bool anchorSame = pA.localSupportCx==pB.localSupportCx && pA.localSupportCy==pB.localSupportCy
        && pA.localSupportHalfW==pB.localSupportHalfW && pA.localSupportHalfH==pB.localSupportHalfH;
    sb.AppendLine(tag+" anchorBox identical "+anchorSame
        +"  ("+pA.localSupportHalfW.ToString("F4")+","+pA.localSupportHalfH.ToString("F4")+") -> ("
        +pB.localSupportHalfW.ToString("F4")+","+pB.localSupportHalfH.ToString("F4")+")");
    sb.AppendLine(tag+" cullBox halfW "+pA.supportHalfW.ToString("F4")+" -> "+pB.supportHalfW.ToString("F4")
        +" (grew "+(pB.supportHalfW-pA.supportHalfW).ToString("F4")+"); halfH "+pA.supportHalfH.ToString("F4")
        +" -> "+pB.supportHalfH.ToString("F4")+" (grew "+(pB.supportHalfH-pA.supportHalfH).ToString("F4")
        +"); reach "+rb.reach);

    // 2) containment: does any real sample of the STRIP or of the JOINED node fall outside the declared box?
    float outNodeX=0, outNodeY=0, outStripX=0, outStripY=0; int badNode=0, badStrip=0, nStrip=0, nNode=0;
    for (int y=0;y<400;y++) for (int x=0;x<400;x++) {
        float cx=-0.5f*399+x, cy=-0.5f*399+y;
        float dJ = Laubrary.Shaper.ShaperEvaluator.Distance(pB,cx,cy,sB);
        float dS = Laubrary.Shaper.ShaperEvaluator.Distance(strip,cx,cy,sS);
        if (!Laubrary.Shaper.ShaperField.IsEmpty(dJ) && dJ <= 0f) {
            nNode++;
            float ox = UnityEngine.Mathf.Abs(cx-pB.supportCx)-pB.supportHalfW;
            float oy = UnityEngine.Mathf.Abs(cy-pB.supportCy)-pB.supportHalfH;
            if (ox>0f||oy>0f){ badNode++; if(ox>outNodeX)outNodeX=ox; if(oy>outNodeY)outNodeY=oy; } }
        if (!Laubrary.Shaper.ShaperField.IsEmpty(dS) && dS <= 0f) {
            nStrip++;
            float ox = UnityEngine.Mathf.Abs(cx-strip.supportCx)-strip.supportHalfW;
            float oy = UnityEngine.Mathf.Abs(cy-strip.supportCy)-strip.supportHalfH;
            if (ox>0f||oy>0f){ badStrip++; if(ox>outStripX)outStripX=ox; if(oy>outStripY)outStripY=oy; } }
    }
    sb.AppendLine(tag+" joined-node samples inside(d<=0) "+nNode+"  OUTSIDE declared box "+badNode
        +"  worst overshoot x "+outNodeX.ToString("F3")+" y "+outNodeY.ToString("F3"));
    sb.AppendLine(tag+" strip samples (s<=0) "+nStrip+"  OUTSIDE declared box "+badStrip
        +"  worst overshoot x "+outStripX.ToString("F3")+" y "+outStripY.ToString("F3"));
};
run("[identity]", 0f, 1f, 1f, 0f, 0f, 0f, 0f);
run("[rot37]",   37f, 1f, 1f, 0f, 0f, 0f, 0f);
run("[aniso2x]",  0f, 2.0f, 0.5f, 0f, 0f, 0f, 0f);
run("[full]",    37f, 1.7f, 0.6f, 20f, -13f, 12f, -9f);
System.IO.File.AppendAllText("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/v_out.txt", sb.ToString());
return sb.ToString();
