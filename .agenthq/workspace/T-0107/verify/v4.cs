var sb = new System.Text.StringBuilder();
var IDN = Laubrary.Shaper.ShaperMatrix.Identity;

System.Func<Laubrary.Shaper.ShaperProgram,string,string> contain = (p, tag) => {
    var st = p.NewStack(); int n=0, bad=0; float wx=0, wy=0;
    for (int y=0;y<400;y++) for (int x=0;x<400;x++) {
        float cx=-0.5f*399+x, cy=-0.5f*399+y;
        float d = Laubrary.Shaper.ShaperEvaluator.Distance(p,cx,cy,st);
        if (Laubrary.Shaper.ShaperField.IsEmpty(d) || d > 0f) continue;
        n++;
        float ox = UnityEngine.Mathf.Abs(cx-p.supportCx)-p.supportHalfW;
        float oy = UnityEngine.Mathf.Abs(cy-p.supportCy)-p.supportHalfH;
        if (ox>0f||oy>0f){ bad++; if(ox>wx)wx=ox; if(oy>wy)wy=oy; } }
    return tag+" inside "+n+"  outsideBox "+bad+"  worst overshoot ("+wx.ToString("F3")+","+wy.ToString("F3")
        +")  box half ("+p.supportHalfW.ToString("F3")+","+p.supportHalfH.ToString("F3")+")"; };

var pdE = new Laubrary.Shaper.ShaperPrimitiveDef();
pdE.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse; pdE.ellipseRx=24f; pdE.ellipseRy=18f;

// E: bag (identity transform) whose CHILD is anisotropically scaled; joined outward border on the BAG.
{
    var child = Laubrary.Shaper.ShaperNode.Primitive(pdE, "child");
    child.transform.scale = new UnityEngine.Vector2(2.0f, 0.5f);
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag");
    bag.children.Add(child);
    var b = new Laubrary.Shaper.ShaperBorderDef();
    b.alignment = Laubrary.Shaper.ShaperShellAlignment.Outward; b.width = new ZUIValue(8f); b.joinsCoverage = true;
    bag.border = b;
    sb.AppendLine(contain(Laubrary.Shaper.ShaperCompiler.Compile(bag, IDN, 0f, 0u), "E bag+anisoChild joined"));
}
// F: the PRE-EXISTING Shell op on the same anisotropic member, for comparison.
{
    var n = Laubrary.Shaper.ShaperNode.Primitive(pdE, "N");
    n.transform.scale = new UnityEngine.Vector2(2.0f, 0.5f);
    n.shell.enabled = true; n.shell.thickness = 8f;
    n.shell.alignment = Laubrary.Shaper.ShaperShellAlignment.Outward;
    sb.AppendLine(contain(Laubrary.Shaper.ShaperCompiler.Compile(n, IDN, 0f, 0u), "F Shell(Outward,8) aniso2x"));
}
// F2: plain aniso member, no border/shell -> is the BASE box already tight?
{
    var n = Laubrary.Shaper.ShaperNode.Primitive(pdE, "N");
    n.transform.scale = new UnityEngine.Vector2(2.0f, 0.5f);
    sb.AppendLine(contain(Laubrary.Shaper.ShaperCompiler.Compile(n, IDN, 0f, 0u), "F2 plain aniso2x (no border)"));
}
// G: singular values of the accumulated map, to confirm the predicted factor
{
    var m = Laubrary.Shaper.ShaperMatrix.Mul(IDN, new Laubrary.Shaper.ShaperTransformBlock{ scale = new UnityEngine.Vector2(2.0f,0.5f) }.ToMatrix());
    float smin, smax; m.SingularValues(out smin, out smax);
    sb.AppendLine("G aniso2x sigmaMin "+smin+" sigmaMax "+smax+" ratio "+(smax/smin)+"  -> reach 8 needs growth "+(8f*smax/smin));
}
System.IO.File.AppendAllText("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/v_out.txt", sb.ToString());
return sb.ToString();
