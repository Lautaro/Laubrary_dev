var sb = new System.Text.StringBuilder();
const int W = 128, H = 128; const float Px = 1f;

System.Func<string,float,float,float,float,Laubrary.Shaper.ShaperNode> RectN = (nm,hw,hh,x,y) => {
  var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
    kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh }, nm);
  n.transform.translate = new UnityEngine.Vector2(x,y);
  return n;
};
System.Func<string,float,float,float,Laubrary.Shaper.ShaperNode> DiscN = (nm,r,x,y) => {
  var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
    kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r }, nm);
  n.transform.translate = new UnityEngine.Vector2(x,y);
  return n;
};

// ---------- D(i)/D(ii): rotate a non-square rect through a full turn ----------
sb.AppendLine("== D: anchor stability under rotation (rect hw=30 hh=12, Gradient.Linear Stamped/Stretch) ==");
sb.AppendLine("deg | localHW localHH  | canvasHW canvasHH | u,v at the shape-local point (15,6)");
float uMin=1e9f,uMax=-1e9f,vMin=1e9f,vMax=-1e9f, cwMin=1e9f,cwMax=-1e9f;
float lwMin=1e9f,lwMax=-1e9f;
for (int deg = 0; deg < 360; deg += 15)
{
  var root = RectN("R",30f,12f,0f,0f);
  root.transform.rotation = deg;
  root.fill = new Laubrary.Shaper.ShaperFillDef {
    kind = Laubrary.Shaper.ShaperFillKind.Gradient,
    gradientMode = Laubrary.Shaper.ShaperGradientMode.Linear,
    fit = Laubrary.Shaper.ShaperFillFit.Stretch,
    space = Laubrary.Shaper.ShaperFillSpace.Stamped };
  float chw = 0.5f*(W-1)*Px, chh = 0.5f*(H-1)*Px;
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, chw, chh);
  var ow = doc.owners[0];
  var op = ow.fill.op;
  // shape-local point (15,6) mapped forward into canvas
  float rad = deg * UnityEngine.Mathf.Deg2Rad;
  float cx = UnityEngine.Mathf.Cos(rad), sy = UnityEngine.Mathf.Sin(rad);
  float px = 15f*cx - 6f*sy, py = 15f*sy + 6f*cx;
  float u,v; Laubrary.Shaper.ShaperFillOps.Anchor(in op, px, py, out u, out v);
  if(deg%45==0) sb.AppendLine(deg.ToString().PadLeft(3) + " | " +
    ow.shape.localSupportHalfW.ToString("F5") + " " + ow.shape.localSupportHalfH.ToString("F5") + " | " +
    ow.shape.supportHalfW.ToString("F5") + " " + ow.shape.supportHalfH.ToString("F5") + " | " +
    u.ToString("F6") + ", " + v.ToString("F6"));
  uMin=UnityEngine.Mathf.Min(uMin,u); uMax=UnityEngine.Mathf.Max(uMax,u);
  vMin=UnityEngine.Mathf.Min(vMin,v); vMax=UnityEngine.Mathf.Max(vMax,v);
  cwMin=UnityEngine.Mathf.Min(cwMin,ow.shape.supportHalfW); cwMax=UnityEngine.Mathf.Max(cwMax,ow.shape.supportHalfW);
  lwMin=UnityEngine.Mathf.Min(lwMin,ow.shape.localSupportHalfW); lwMax=UnityEngine.Mathf.Max(lwMax,ow.shape.localSupportHalfW);
}
sb.AppendLine("  u range over 24 angles: [" + uMin.ToString("F7") + ", " + uMax.ToString("F7") + "]  span " + (uMax-uMin).ToString("E3"));
sb.AppendLine("  v range over 24 angles: [" + vMin.ToString("F7") + ", " + vMax.ToString("F7") + "]  span " + (vMax-vMin).ToString("E3"));
sb.AppendLine("  LOCAL  supportHalfW range: [" + lwMin.ToString("F5") + ", " + lwMax.ToString("F5") + "]  span " + (lwMax-lwMin).ToString("E3") + "  (expected 0 - it must not breathe)");
sb.AppendLine("  CANVAS supportHalfW range: [" + cwMin.ToString("F5") + ", " + cwMax.ToString("F5") + "]  span " + (cwMax-cwMin).ToString("E3") + "  (the rejected candidate: this is the breathing)");
sb.AppendLine("  breathing ratio canvas max/min = " + (cwMax/cwMin).ToString("F4") + "x");

// ---------- D(iii): the undocumented centre subtraction, on an offset bag ----------
sb.AppendLine();
sb.AppendLine("== D(iii): centre subtraction on an OFFSET bag (deviation #4) ==");
{
  var bag = Laubrary.Shaper.ShaperNode.Bag("Bag");
  bag.children.Add(DiscN("a",10f,50f,30f));
  bag.children.Add(DiscN("b",10f,70f,30f));
  bag.fill = new Laubrary.Shaper.ShaperFillDef {
    kind = Laubrary.Shaper.ShaperFillKind.Gradient,
    gradientMode = Laubrary.Shaper.ShaperGradientMode.Radial,
    fit = Laubrary.Shaper.ShaperFillFit.Uniform };
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(bag, 0f, 0u, 63.5f, 63.5f);
  var ow = doc.owners[0]; var op = ow.fill.op;
  sb.AppendLine("  localSupport centre = (" + ow.shape.localSupportCx.ToString("F3") + ", " + ow.shape.localSupportCy.ToString("F3") +
                ")  halfW/H = " + ow.shape.localSupportHalfW.ToString("F3") + "/" + ow.shape.localSupportHalfH.ToString("F3"));
  float u,v;
  Laubrary.Shaper.ShaperFillOps.Anchor(in op, 60f, 30f, out u, out v);
  sb.AppendLine("  u,v at the bag's own box CENTRE (60,30)  = (" + u.ToString("F6") + ", " + v.ToString("F6") + ")   [FC-1.5's literal formula would give (" +
                (60f*op.invHx).ToString("F4") + ", " + (30f*op.invHy).ToString("F4") + ") - off the ramp entirely]");
  Laubrary.Shaper.ShaperFillOps.Anchor(in op, 40f, 30f, out u, out v);
  sb.AppendLine("  u,v at the LEFT edge of the box (40,30) = (" + u.ToString("F6") + ", " + v.ToString("F6") + ")  (expect u=-1 under Uniform iff halfW>=halfH)");
  Laubrary.Shaper.ShaperFillOps.Anchor(in op, 80f, 30f, out u, out v);
  sb.AppendLine("  u,v at the RIGHT edge of the box (80,30)= (" + u.ToString("F6") + ", " + v.ToString("F6") + ")");
}

// ---------- D(i): is the local box derived from the canvas box? ----------
sb.AppendLine();
sb.AppendLine("== D(i): local box vs canvas-box-mapped-back-down (the FC-1.5a prohibition) ==");
{
  var root = RectN("R",30f,12f,0f,0f);
  root.transform.rotation = 30f;
  root.fill = new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid };
  var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, 63.5f, 63.5f);
  var p = doc.owners[0].shape;
  sb.AppendLine("  at 30deg: local halfW/H = " + p.localSupportHalfW.ToString("F5") + "/" + p.localSupportHalfH.ToString("F5") +
                "  (authored 30/12)");
  sb.AppendLine("  at 30deg: canvas halfW/H = " + p.supportHalfW.ToString("F5") + "/" + p.supportHalfH.ToString("F5"));
  // what "map the canvas box back down through the inverse" would have produced:
  var inv = p.rootInverse;
  float bhw = UnityEngine.Mathf.Abs(inv.m00)*p.supportHalfW + UnityEngine.Mathf.Abs(inv.m01)*p.supportHalfH;
  float bhh = UnityEngine.Mathf.Abs(inv.m10)*p.supportHalfW + UnityEngine.Mathf.Abs(inv.m11)*p.supportHalfH;
  sb.AppendLine("  the FORBIDDEN derivation (canvas box mapped down) would give " + bhw.ToString("F5") + "/" + bhh.ToString("F5") +
                "  -> inflation " + (bhw/p.localSupportHalfW).ToString("F4") + "x / " + (bhh/p.localSupportHalfH).ToString("F4") + "x");
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r10_anchor.txt", sb.ToString());
return "ok " + sb.Length.ToString();
