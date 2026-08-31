var sb = new System.Text.StringBuilder();
const int W = 128, H = 128; const float Px = 1f;
float CHW = 0.5f*(W-1)*Px, CHH = 0.5f*(H-1)*Px;
System.Func<string,float,float,float,Laubrary.Shaper.ShaperCombineMode,Laubrary.Shaper.ShaperNode> Disc =
 (nm,r,x,y,m) => { var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
    kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r }, nm, m);
    n.transform.translate = new UnityEngine.Vector2(x,y); return n; };
System.Func<string,float,float,float,float,Laubrary.Shaper.ShaperCombineMode,Laubrary.Shaper.ShaperNode> Rect =
 (nm,hw,hh,x,y,m) => { var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
    kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh }, nm, m);
    n.transform.translate = new UnityEngine.Vector2(x,y); return n; };
System.Func<UnityEngine.Color,Laubrary.Shaper.ShaperFillDef> Solid = c =>
  new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid, solidColor = c };

// FC-3.8's tree, same geometry the implementer's FT-7 uses (Torso/Head overlap, Belt band on the lower half of Torso, Eye in Head, Iris concentric with Eye)
var body = Laubrary.Shaper.ShaperNode.Bag("Body");
body.fill = Solid(UnityEngine.Color.magenta);            // G_body
body.children.Add(Disc("Torso",30f,0f,-10f,Laubrary.Shaper.ShaperCombineMode.Add));
var head = Disc("Head",18f,0f,22f,Laubrary.Shaper.ShaperCombineMode.Add); head.fill = Solid(UnityEngine.Color.yellow);
body.children.Add(head);
var belt = Rect("Belt",44f,10f,0f,-20f,Laubrary.Shaper.ShaperCombineMode.Intersect); belt.fill = Solid(UnityEngine.Color.cyan);
body.children.Add(belt);
body.children.Add(Disc("Eye",5f,0f,22f,Laubrary.Shaper.ShaperCombineMode.Subtract));
var iris = Disc("Iris",3f,0f,22f,Laubrary.Shaper.ShaperCombineMode.Add); iris.fill = Solid(UnityEngine.Color.white);
body.children.Add(iris);

var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(body, 0f, 0u, CHW, CHH);
var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
var sheets = new Laubrary.Shaper.ShaperFillSheets { published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine };
Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, 0,0,W,H, buf, sheets);
int cap = buf.sampleCapacity; int k = doc.owners.Count;
sb.AppendLine("== B: FC-3.8's worked tree. owners: " + string.Join(" -> ", doc.owners.ConvertAll(o=>o.name).ToArray()) + " ==");

// per-owner painted-sample counts under the SHIPPED multiplicative rule
for(int o=0;o<k;o++){
  int nz=0; double area=0; float mx=0;
  for(int i=0;i<W*H;i++){ float p=buf.paint[o*cap+i]; if(p>1e-6f){nz++;area+=p; if(p>mx)mx=p;} }
  sb.AppendLine("  " + doc.owners[o].name.PadRight(6) + " paints " + nz.ToString().PadLeft(6) + " samples, area " + area.ToString("F2") + ", max per-sample " + mx.ToString("F4"));
}
// the SUBTRACTIVE alternative
var dc = new float[k*cap];
for (int o=k-1;o>=1;o--){ int a=doc.owners[o].ancestorOwner; if(a<0)continue;
  for(int i=0;i<W*H;i++){ float sub=UnityEngine.Mathf.Max(buf.claim[o*cap+i], dc[o*cap+i]); if(sub>dc[a*cap+i]) dc[a*cap+i]=sub; } }
sb.AppendLine("  --- the same, under SUBTRACTIVE exclusivity max(0, claim - descendantClaim) ---");
for(int o=0;o<k;o++){
  int nz=0; double area=0; float mx=0;
  for(int i=0;i<W*H;i++){ float p=UnityEngine.Mathf.Max(0f, buf.claim[o*cap+i]-UnityEngine.Mathf.Clamp01(dc[o*cap+i])); if(p>1e-6f){nz++;area+=p; if(p>mx)mx=p;} }
  sb.AppendLine("  " + doc.owners[o].name.PadRight(6) + " paints " + nz.ToString().PadLeft(6) + " samples, area " + area.ToString("F2") + ", max per-sample " + mx.ToString("F4"));
}
// where exactly does G_body leak? sample it
sb.AppendLine("  --- where G_body leaks (multiplicative), first 6 leaking samples ---");
{ int shown=0;
  for(int i=0;i<W*H && shown<6;i++){ float p=buf.paint[0*cap+i]; if(p>1e-6f){
    int iy=i/W, ix=i%W; float x=grid.X(ix), y=grid.Y(iy);
    sb.AppendLine("    (" + x.ToString("F1") + "," + y.ToString("F1") + ")  claimBody=" + buf.claim[0*cap+i].ToString("F4") +
      "  descClaim=" + dc[0*cap+i].ToString("F4") + "  -> mult " + p.ToString("F4") + " vs sub " +
      UnityEngine.Mathf.Max(0f, buf.claim[0*cap+i]-UnityEngine.Mathf.Clamp01(dc[0*cap+i])).ToString("F4"));
    shown++; } } }
// alpha deficit for the whole tree
double deficit=0; int nDef=0; float worst=0;
for(int i=0;i<W*H;i++){ float rc=buf.ownCoverage[i]; float al=buf.dst[i*4+3]; float d=rc-al; if(d>1e-3f){deficit+=d;nDef++; if(d>worst)worst=d;} }
sb.AppendLine("  ALPHA DEFICIT vs shape coverage: " + nDef + " samples, total " + deficit.ToString("F3") + ", worst " + worst.ToString("F4"));
// total area vs root coverage
double sMul=0,sSub=0,rootArea=0;
for(int i=0;i<W*H;i++){ rootArea+=buf.ownCoverage[i];
  for(int o=0;o<k;o++){ sMul+=buf.paint[o*cap+i]; sSub+=UnityEngine.Mathf.Max(0f, buf.claim[o*cap+i]-UnityEngine.Mathf.Clamp01(dc[o*cap+i])); } }
sb.AppendLine("  painted area: multiplicative " + sMul.ToString("F3") + "  subtractive " + sSub.ToString("F3") + "  root coverage " + rootArea.ToString("F3"));
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r12_tree.txt", sb.ToString());
return "ok";
