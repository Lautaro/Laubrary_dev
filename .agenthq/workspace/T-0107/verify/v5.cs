var sb = new System.Text.StringBuilder();
var IDN = Laubrary.Shaper.ShaperMatrix.Identity;
int W=160, H=160; float Px=1f;

System.Func<float,float,float,Laubrary.Shaper.ShaperCombineMode,Laubrary.Shaper.ShaperNode> disc = (r,x,y,m) => {
    var pd = new Laubrary.Shaper.ShaperPrimitiveDef();
    pd.kind=Laubrary.Shaper.ShaperPrimitiveKind.Ellipse; pd.ellipseRx=r; pd.ellipseRy=r;
    var n = Laubrary.Shaper.ShaperNode.Primitive(pd,"d",m);
    n.transform.translate = new UnityEngine.Vector2(x,y); return n; };
System.Func<UnityEngine.Color,Laubrary.Shaper.ShaperFillDef> solid = c => {
    var f = new Laubrary.Shaper.ShaperFillDef(); f.kind=Laubrary.Shaper.ShaperFillKind.Solid;
    f.solidColor=c; f.veil=new ZUIValue(1f); f.heightDelta=new ZUIValue(0f); return f; };
System.Func<Laubrary.Shaper.ShaperShellAlignment,float,bool,Laubrary.Shaper.ShaperFillDef,Laubrary.Shaper.ShaperBorderDef> bord =
  (a,w,j,f) => { var b=new Laubrary.Shaper.ShaperBorderDef(); b.alignment=a; b.width=new ZUIValue(w);
    b.joinsCoverage=j; b.fill=f; return b; };

var sheets = new Laubrary.Shaper.ShaperFillSheets();
sheets.published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine;
System.Func<Laubrary.Shaper.ShaperNode, object[]> build = root => {
    float chw=0.5f*(W-1)*Px, chh=0.5f*(H-1)*Px;
    var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, chw, chh);
    var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
    var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, UnityEngine.Mathf.Max(1,doc.owners.Count));
    Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, 0,0,W,H, buf, sheets);
    return new object[]{doc,grid,buf}; };
System.Func<object[],float,float,UnityEngine.Color> at = (rig,x,y) => {
    var grid=(Laubrary.Shaper.ShaperSampleGrid)rig[1]; var buf=(Laubrary.Shaper.ShaperFillBuffers)rig[2];
    int ix=UnityEngine.Mathf.RoundToInt((x-grid.originX)/grid.pixelSize);
    int iy=UnityEngine.Mathf.RoundToInt((y-grid.originY)/grid.pixelSize);
    int i=(UnityEngine.Mathf.Clamp(iy,0,H-1)*W+UnityEngine.Mathf.Clamp(ix,0,W-1))*4;
    float a=UnityEngine.Mathf.Clamp01(buf.dst[i+3]); float inv = a>1e-6f?1f/a:1f;
    return new UnityEngine.Color(buf.dst[i]*inv, buf.dst[i+1]*inv, buf.dst[i+2]*inv, a); };

// ---- 1: hostless border ordering. A (no own fill, magenta inward border) then B (own red fill), B ABOVE A.
System.Func<bool,object[]> mk1 = aOwnsFill => {
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag"); bag.fill = solid(UnityEngine.Color.white);
    var A = disc(30f,-10f,0f,Laubrary.Shaper.ShaperCombineMode.Add); A.name="A";
    A.border = bord(Laubrary.Shaper.ShaperShellAlignment.Inward, 6f, false, solid(UnityEngine.Color.magenta));
    if (aOwnsFill) A.fill = solid(new UnityEngine.Color(0f,0.4f,0f,1f));
    var B = disc(30f,12f,0f,Laubrary.Shaper.ShaperCombineMode.Add); B.name="B"; B.fill = solid(UnityEngine.Color.red);
    bag.children.Add(A); bag.children.Add(B);
    return build(bag); };
{
    // A's rim (inward, 6px inside A's edge) at A's RIGHT side x = -10+30-3 = 17, which is inside B (|17-12|<30).
    var r1 = mk1(false); var c1 = at(r1, 17f, 0f);
    var r2 = mk1(true);  var c2 = at(r2, 17f, 0f);
    sb.AppendLine("1 hostless(A has NO fill): colour at A's rim inside B = ("+c1.r.ToString("F3")+","+c1.g.ToString("F3")+","+c1.b.ToString("F3")+") a="+c1.a.ToString("F3"));
    sb.AppendLine("1 control (A OWNS a fill): colour at same point       = ("+c2.r.ToString("F3")+","+c2.g.ToString("F3")+","+c2.b.ToString("F3")+") a="+c2.a.ToString("F3"));
    var d1=(Laubrary.Shaper.ShaperFillDocument)r1[0]; var d2=(Laubrary.Shaper.ShaperFillDocument)r2[0];
    sb.Append("1 owners(noFill): "); foreach(var o in d1.owners) sb.Append(o.name+(o.isBorder?"[B host="+o.borderHost+" anc="+o.ancestorOwner+"]":"")+" ; ");
    sb.AppendLine();
    sb.Append("1 owners(fill):   "); foreach(var o in d2.owners) sb.Append(o.name+(o.isBorder?"[B host="+o.borderHost+" anc="+o.ancestorOwner+"]":"")+" ; ");
    sb.AppendLine();
}
// ---- 2: BD-3.4 ancestor clip. A bordered member, then a LATER Subtract member carving through A's rim.
{
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag"); bag.fill = solid(UnityEngine.Color.white);
    var A = disc(34f,0f,0f,Laubrary.Shaper.ShaperCombineMode.Add); A.name="A"; A.fill = solid(new UnityEngine.Color(0f,0.3f,0f,1f));
    A.border = bord(Laubrary.Shaper.ShaperShellAlignment.Inward, 6f, false, solid(UnityEngine.Color.magenta));
    var C = disc(24f,34f,0f,Laubrary.Shaper.ShaperCombineMode.Subtract); C.name="Hole";
    bag.children.Add(A); bag.children.Add(C);
    var rig = build(bag);
    // A's rim on the right runs near x = 31. The Subtract disc covers x in [10,58] at y=0. So the rim at (31,0)
    // is INSIDE the carved region and must be gone.
    var c = at(rig, 31f, 0f);
    var c2 = at(rig, -31f, 0f);   // the rim on the far side, untouched
    sb.AppendLine("2 ancestorClip: rim inside the carved hole (31,0) = ("+c.r.ToString("F3")+","+c.g.ToString("F3")+","+c.b.ToString("F3")+") a="+c.a.ToString("F3")+"   [expect a=0]");
    sb.AppendLine("2 ancestorClip: rim on the intact side (-31,0)    = ("+c2.r.ToString("F3")+","+c2.g.ToString("F3")+","+c2.b.ToString("F3")+") a="+c2.a.ToString("F3")+"   [expect magenta a=1]");
}
// ---- 3: transparent-seam / NaN scan over a bordered composite
{
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag"); bag.fill = solid(UnityEngine.Color.white);
    var A = disc(28f,-14f,0f,Laubrary.Shaper.ShaperCombineMode.Add); A.name="A"; A.fill = solid(new UnityEngine.Color(0f,0.35f,0f,1f));
    A.border = bord(Laubrary.Shaper.ShaperShellAlignment.Inward, 5f, false, solid(UnityEngine.Color.magenta));
    var B = disc(28f,14f,6f,Laubrary.Shaper.ShaperCombineMode.Add); B.name="B"; B.fill = solid(new UnityEngine.Color(0.2f,0.2f,0.6f,1f));
    B.border = bord(Laubrary.Shaper.ShaperShellAlignment.Outward, 4f, true, solid(UnityEngine.Color.cyan));
    bag.border = bord(Laubrary.Shaper.ShaperShellAlignment.Centred, 4f, true, solid(UnityEngine.Color.yellow));
    bag.children.Add(A); bag.children.Add(B);
    var rig = build(bag);
    var grid=(Laubrary.Shaper.ShaperSampleGrid)rig[1]; var buf=(Laubrary.Shaper.ShaperFillBuffers)rig[2];
    var rootProg = Laubrary.Shaper.ShaperCompiler.Compile(bag, IDN, 0f, 0u); var stk = rootProg.NewStack();
    int inside=0, seam=0, nan=0; float worstA=1f; float wx=0, wy=0;
    for (int y=0;y<H;y++) for (int x=0;x<W;x++) {
        float cx=grid.X(x), cy=grid.Y(y);
        float d = Laubrary.Shaper.ShaperEvaluator.Distance(rootProg,cx,cy,stk);
        int i=(y*W+x)*4;
        for (int k=0;k<4;k++) if (float.IsNaN(buf.dst[i+k])) { nan++; break; }
        if (Laubrary.Shaper.ShaperField.IsEmpty(d) || d > -1.0f) continue;   // strictly inside the finished silhouette
        inside++;
        float a = buf.dst[i+3];
        if (a < 0.99f) { seam++; if (a<worstA){worstA=a; wx=cx; wy=cy;} }
    }
    sb.AppendLine("3 seam scan: strictly-inside samples "+inside+"  alpha<0.99 "+seam+"  worst alpha "+worstA.ToString("F4")+" at ("+wx+","+wy+")   NaN samples "+nan);
}
System.IO.File.AppendAllText("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/v_out.txt", sb.ToString());
return sb.ToString();
