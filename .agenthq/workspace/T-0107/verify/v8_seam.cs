var sb = new System.Text.StringBuilder();
var IDN = Laubrary.Shaper.ShaperMatrix.Identity;
int W=160, H=160; float Px=1f;

System.Func<float,float,float,Laubrary.Shaper.ShaperCombineMode,string,Laubrary.Shaper.ShaperNode> disc = (r,x,y,m,nm) => {
    var pd = new Laubrary.Shaper.ShaperPrimitiveDef();
    pd.kind=Laubrary.Shaper.ShaperPrimitiveKind.Ellipse; pd.ellipseRx=r; pd.ellipseRy=r;
    var n = Laubrary.Shaper.ShaperNode.Primitive(pd,nm,m);
    n.transform.translate = new UnityEngine.Vector2(x,y); return n; };
System.Func<UnityEngine.Color,float,Laubrary.Shaper.ShaperFillComposite,Laubrary.Shaper.ShaperFillDef> solid = (c,v,cm) => {
    var f = new Laubrary.Shaper.ShaperFillDef(); f.kind=Laubrary.Shaper.ShaperFillKind.Solid;
    f.solidColor=c; f.veil=new ZUIValue(v); f.heightDelta=new ZUIValue(0f); f.composite=cm; return f; };
System.Func<Laubrary.Shaper.ShaperShellAlignment,float,bool,Laubrary.Shaper.ShaperFillDef,Laubrary.Shaper.ShaperBorderDef> bord =
  (a,w,j,f) => { var b=new Laubrary.Shaper.ShaperBorderDef(); b.alignment=a; b.width=new ZUIValue(w);
    b.joinsCoverage=j; b.fill=f; return b; };

var sheets = new Laubrary.Shaper.ShaperFillSheets();
sheets.published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine;

System.Action<string,Laubrary.Shaper.ShaperNode> scan = (tag, root) => {
    float chw=0.5f*(W-1)*Px, chh=0.5f*(H-1)*Px;
    var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, chw, chh);
    var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
    var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, UnityEngine.Mathf.Max(1,doc.owners.Count));
    Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, 0,0,W,H, buf, sheets);
    var prog = Laubrary.Shaper.ShaperCompiler.Compile(root, IDN, 0f, 0u); var stk = prog.NewStack();
    int inside=0, seam=0, nan=0, neg=0; float worst=1f; float wx=0, wy=0;
    for (int y=0;y<H;y++) for (int x=0;x<W;x++) {
        int i=(y*W+x)*4;
        for (int k=0;k<4;k++) if (float.IsNaN(buf.dst[i+k]) || float.IsInfinity(buf.dst[i+k])) { nan++; break; }
        for (int k=0;k<4;k++) if (buf.dst[i+k] < -1e-6f) { neg++; break; }
        float cx=grid.X(x), cy=grid.Y(y);
        float d = Laubrary.Shaper.ShaperEvaluator.Distance(prog,cx,cy,stk);
        if (Laubrary.Shaper.ShaperField.IsEmpty(d) || d > -1.0f) continue;
        inside++;
        float a = buf.dst[i+3];
        if (a < 0.99f) { seam++; if (a<worst){worst=a; wx=cx; wy=cy;} }
    }
    sb.AppendLine(tag+"  owners "+doc.owners.Count+"  inside "+inside+"  alpha<0.99 "+seam
        +"  worst "+worst.ToString("F4")+" at ("+wx+","+wy+")  NaN/Inf "+nan+"  negative "+neg);
};

// F1 - three borders, three alignments, joined and unjoined, opaque fills
{
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag"); bag.fill = solid(UnityEngine.Color.white,1f,Laubrary.Shaper.ShaperFillComposite.Over);
    var A = disc(28f,-14f,0f,Laubrary.Shaper.ShaperCombineMode.Add,"A"); A.fill = solid(new UnityEngine.Color(0f,0.35f,0f),1f,Laubrary.Shaper.ShaperFillComposite.Over);
    A.border = bord(Laubrary.Shaper.ShaperShellAlignment.Inward,5f,false, solid(UnityEngine.Color.magenta,1f,Laubrary.Shaper.ShaperFillComposite.Over));
    var B = disc(28f,14f,6f,Laubrary.Shaper.ShaperCombineMode.Add,"B"); B.fill = solid(new UnityEngine.Color(0.2f,0.2f,0.6f),1f,Laubrary.Shaper.ShaperFillComposite.Over);
    B.border = bord(Laubrary.Shaper.ShaperShellAlignment.Outward,4f,true, solid(UnityEngine.Color.cyan,1f,Laubrary.Shaper.ShaperFillComposite.Over));
    bag.border = bord(Laubrary.Shaper.ShaperShellAlignment.Centred,4f,true, solid(UnityEngine.Color.yellow,1f,Laubrary.Shaper.ShaperFillComposite.Over));
    bag.children.Add(A); bag.children.Add(B); scan("F1 three borders          ", bag);
}
// F2 - an ADDITIVE border (FC-2.6b: Add must not raise alpha, so a fully additive rim over nothing stays clear)
{
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag"); bag.fill = solid(UnityEngine.Color.white,1f,Laubrary.Shaper.ShaperFillComposite.Over);
    var A = disc(30f,0f,0f,Laubrary.Shaper.ShaperCombineMode.Add,"A"); A.fill = solid(new UnityEngine.Color(0.1f,0.1f,0.4f),1f,Laubrary.Shaper.ShaperFillComposite.Over);
    A.border = bord(Laubrary.Shaper.ShaperShellAlignment.Inward,6f,false, solid(UnityEngine.Color.cyan,1f,Laubrary.Shaper.ShaperFillComposite.Add));
    bag.children.Add(A); scan("F2 additive border        ", bag);
}
// F3 - a TRANSLUCENT border (veil 0.35): BD-3.5's "a stroke lets the fill underneath show through"
{
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag"); bag.fill = solid(UnityEngine.Color.white,1f,Laubrary.Shaper.ShaperFillComposite.Over);
    var A = disc(30f,0f,0f,Laubrary.Shaper.ShaperCombineMode.Add,"A"); A.fill = solid(new UnityEngine.Color(0.1f,0.4f,0.1f),1f,Laubrary.Shaper.ShaperFillComposite.Over);
    A.border = bord(Laubrary.Shaper.ShaperShellAlignment.Centred,6f,true, solid(UnityEngine.Color.magenta,0.35f,Laubrary.Shaper.ShaperFillComposite.Over));
    bag.children.Add(A); scan("F3 translucent border     ", bag);
}
// F4 - a Subtract HOLE plus a bag border tracing it (BD-3.7's documented route)
{
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag"); bag.fill = solid(new UnityEngine.Color(0.7f,0.7f,0.2f),1f,Laubrary.Shaper.ShaperFillComposite.Over);
    var A = disc(40f,0f,0f,Laubrary.Shaper.ShaperCombineMode.Add,"A");
    var Hn = disc(16f,0f,0f,Laubrary.Shaper.ShaperCombineMode.Subtract,"Hole");
    Hn.border = bord(Laubrary.Shaper.ShaperShellAlignment.Inward,4f,false,null);   // must be REFUSED
    bag.border = bord(Laubrary.Shaper.ShaperShellAlignment.Inward,4f,false, solid(UnityEngine.Color.red,1f,Laubrary.Shaper.ShaperFillComposite.Over));
    bag.children.Add(A); bag.children.Add(Hn); scan("F4 hole + bag border      ", bag);
}
// F5 - a NaN width dial and a NaN veil, to prove nothing NaN reaches the destination
{
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag"); bag.fill = solid(UnityEngine.Color.white,1f,Laubrary.Shaper.ShaperFillComposite.Over);
    var A = disc(30f,0f,0f,Laubrary.Shaper.ShaperCombineMode.Add,"A"); A.fill = solid(new UnityEngine.Color(0.1f,0.4f,0.1f),1f,Laubrary.Shaper.ShaperFillComposite.Over);
    A.border = bord(Laubrary.Shaper.ShaperShellAlignment.Inward,6f,false, solid(UnityEngine.Color.magenta,float.NaN,Laubrary.Shaper.ShaperFillComposite.Over));
    bag.children.Add(A); scan("F5 NaN veil on the border ", bag);
}
// F6 - a hostless border with a TRANSLUCENT later sibling (the approximated case, reported not asserted)
{
    var bag = Laubrary.Shaper.ShaperNode.Bag("bag"); bag.fill = solid(UnityEngine.Color.white,1f,Laubrary.Shaper.ShaperFillComposite.Over);
    var A = disc(30f,-10f,0f,Laubrary.Shaper.ShaperCombineMode.Add,"A");
    A.border = bord(Laubrary.Shaper.ShaperShellAlignment.Inward,6f,false, solid(UnityEngine.Color.magenta,1f,Laubrary.Shaper.ShaperFillComposite.Over));
    var B = disc(30f,12f,0f,Laubrary.Shaper.ShaperCombineMode.Add,"B"); B.fill = solid(UnityEngine.Color.red,0.5f,Laubrary.Shaper.ShaperFillComposite.Over);
    bag.children.Add(A); bag.children.Add(B); scan("F6 hostless + veil-0.5 sib", bag);
}
System.IO.File.AppendAllText("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/v_out.txt", sb.ToString());
return sb.ToString();
