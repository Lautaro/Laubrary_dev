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
    return tag+"  inside "+n+"  outsideBox "+bad+"  worst overshoot ("+wx.ToString("F3")+","+wy.ToString("F3")
        +")  spread "+p.supportSpread+"  boxHalf ("+p.supportHalfW.ToString("F2")+","+p.supportHalfH.ToString("F2")+")"; };

System.Func<float,float,float,Laubrary.Shaper.ShaperCombineMode,string,Laubrary.Shaper.ShaperNode> disc = (r,x,y,m,nm) => {
    var pd = new Laubrary.Shaper.ShaperPrimitiveDef();
    pd.kind=Laubrary.Shaper.ShaperPrimitiveKind.Ellipse; pd.ellipseRx=r; pd.ellipseRy=r;
    var n = Laubrary.Shaper.ShaperNode.Primitive(pd,nm,m);
    n.transform.translate = new UnityEngine.Vector2(x,y); return n; };
System.Func<Laubrary.Shaper.ShaperShellAlignment,float,bool,Laubrary.Shaper.ShaperBorderDef> bord = (a,w,j) => {
    var b=new Laubrary.Shaper.ShaperBorderDef(); b.alignment=a; b.width=new ZUIValue(w); b.joinsCoverage=j; return b; };

// Does a WIDE SMOOTH-MIN BLEND push the joined silhouette outside the declared box?
// supportSpread bounds the MULTIPLICATIVE under-report a transform introduces. A soft Add also under-reports,
// but ADDITIVELY: smoothMin(a,b,k,n) <= min(a,b), by at most k/(2n). Not covered by the spread; measured here.
foreach (float k in new float[]{ 0f, 20f, 60f })
{
    var A = disc(28f,-18f,0f,Laubrary.Shaper.ShaperCombineMode.Add,"A");
    var B = disc(28f, 18f,0f,Laubrary.Shaper.ShaperCombineMode.Add,"B");
    B.blend.width = k; B.blend.sharpness = 0f;
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag");
    bag.children.Add(A); bag.children.Add(B);
    sb.AppendLine(contain(Laubrary.Shaper.ShaperCompiler.Compile(bag, IDN, 0f, 0u), "blend k=" + k + " no border    "));
    bag.border = bord(Laubrary.Shaper.ShaperShellAlignment.Outward, 8f, true);
    sb.AppendLine(contain(Laubrary.Shaper.ShaperCompiler.Compile(bag, IDN, 0f, 0u), "blend k=" + k + " joined reach8"));
}
System.IO.File.AppendAllText("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/v_out.txt", sb.ToString());
return sb.ToString();
