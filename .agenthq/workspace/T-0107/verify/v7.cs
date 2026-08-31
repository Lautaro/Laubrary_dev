var sb = new System.Text.StringBuilder();
var IDN = Laubrary.Shaper.ShaperMatrix.Identity;
System.Action<Laubrary.Shaper.ShaperShellAlignment,float,string> t = (al,w,tag) => {
    var pd = new Laubrary.Shaper.ShaperPrimitiveDef();
    pd.kind=Laubrary.Shaper.ShaperPrimitiveKind.Ellipse; pd.ellipseRx=34f; pd.ellipseRy=34f;
    var n = Laubrary.Shaper.ShaperNode.Primitive(pd,"N");
    var b = new Laubrary.Shaper.ShaperBorderDef(); b.alignment=al; b.width=new ZUIValue(w); b.joinsCoverage=true;
    n.border=b;
    var p = Laubrary.Shaper.ShaperCompiler.Compile(n, IDN, 0f, 0u);
    var rb = Laubrary.Shaper.ShaperBorder.Resolve(b,0f,0u);
    var s = Laubrary.Shaper.ShaperBorder.CompileStrip(p, rb, rb.Joins);
    var an = Laubrary.Shaper.ShaperFillAnchor.From(p, 64f, 64f);
    var asx = Laubrary.Shaper.ShaperFillAnchor.From(s, 64f, 64f);
    sb.AppendLine(tag+" reach "+rb.reach+"  node anchor halfW "+an.localHalfW+"  strip anchor halfW "+asx.localHalfW
        +"  DIFFERENT "+(an.localHalfW!=asx.localHalfW)+"  program.supportSpread "+p.supportSpread+" rootSigmaMin "+p.rootSigmaMin);
};
t(Laubrary.Shaper.ShaperShellAlignment.Outward, 1f, "outward w1 ");
t(Laubrary.Shaper.ShaperShellAlignment.Outward, 20f, "outward w20");
t(Laubrary.Shaper.ShaperShellAlignment.Inward, 20f, "inward  w20");
t(Laubrary.Shaper.ShaperShellAlignment.Centred, 20f, "centred w20");
return sb.ToString();
