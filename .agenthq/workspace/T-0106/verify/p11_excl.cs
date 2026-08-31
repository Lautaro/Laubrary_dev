var sb = new System.Text.StringBuilder();
const int W = 128, H = 128; const float Px = 1f;
float CHW = 0.5f*(W-1)*Px, CHH = 0.5f*(H-1)*Px;

System.Func<string,float,float,float,Laubrary.Shaper.ShaperCombineMode,Laubrary.Shaper.ShaperNode> Disc =
 (nm,r,x,y,m) => { var n = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
    kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r }, nm, m);
    n.transform.translate = new UnityEngine.Vector2(x,y); return n; };
System.Func<UnityEngine.Color,Laubrary.Shaper.ShaperFillDef> Solid = c =>
  new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid, solidColor = c };

// ---- three-deep concentric nesting, all owning fills, all Over, veil 1 ----
var C = Disc("C",14f,0f,0f,Laubrary.Shaper.ShaperCombineMode.Add); C.fill = Solid(UnityEngine.Color.blue);
var B = Laubrary.Shaper.ShaperNode.Bag("B"); B.fill = Solid(UnityEngine.Color.green);
B.children.Add(Disc("Bd",28f,0f,0f,Laubrary.Shaper.ShaperCombineMode.Add));
B.children.Add(C);
var A = Laubrary.Shaper.ShaperNode.Bag("A"); A.fill = Solid(UnityEngine.Color.red);
A.children.Add(Disc("Ad",42f,0f,0f,Laubrary.Shaper.ShaperCombineMode.Add));
A.children.Add(B);

var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(A, 0f, 0u, CHW, CHH);
var buf = new Laubrary.Shaper.ShaperFillBuffers(W*H, doc.owners.Count);
var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W,H,Px);
var sheets = new Laubrary.Shaper.ShaperFillSheets { published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine };
Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, 0,0,W,H, buf, sheets);

sb.AppendLine("== C: three-deep nesting A(r42,red) > B(r28,green) > C(r14,blue), all Over, veil 1 ==");
sb.AppendLine("owners in paint order: " + string.Join(" -> ", doc.owners.ConvertAll(o=>o.name).ToArray()));
sb.AppendLine();
sb.AppendLine("  r  | ownCovA ownCovB ownCovC | claimA claimB claimC | paintA paintB paintC | SUM  | dstAlpha | rootCov | alpha-rootCov");
System.Func<float,int> Ix = rr => { int ix = UnityEngine.Mathf.RoundToInt((rr - grid.originX)/grid.pixelSize); int iy = UnityEngine.Mathf.RoundToInt((0f - grid.originY)/grid.pixelSize); return iy*W+ix; };
float worstGap = 0f; float worstR = 0f;
for (float r = 0f; r <= 44f; r += 0.5f) {
  int i = Ix(r);
  int cap = buf.sampleCapacity;
  float pa = buf.paint[0*cap+i], pb = buf.paint[1*cap+i], pc = buf.paint[2*cap+i];
  float ca = buf.claim[0*cap+i], cb = buf.claim[1*cap+i], cc = buf.claim[2*cap+i];
  float oa = buf.ownCoverage[0*cap+i], ob = buf.ownCoverage[1*cap+i], oc = buf.ownCoverage[2*cap+i];
  float al = buf.dst[i*4+3];
  float gap = al - oa;
  if (UnityEngine.Mathf.Abs(gap) > UnityEngine.Mathf.Abs(worstGap)) { worstGap = gap; worstR = r; }
  bool interesting = (pa>1e-4f&&pb>1e-4f)||(pb>1e-4f&&pc>1e-4f)||(pa>1e-4f&&pc>1e-4f)||UnityEngine.Mathf.Abs(gap)>1e-3f;
  if (interesting || (r*2f)%8f==0f)
    sb.AppendLine(r.ToString("F1").PadLeft(5) + "|  " + oa.ToString("F3")+"  "+ob.ToString("F3")+"  "+oc.ToString("F3") +
      " |  "+ca.ToString("F3")+"  "+cb.ToString("F3")+"  "+cc.ToString("F3") +
      " | "+pa.ToString("F3")+"  "+pb.ToString("F3")+"  "+pc.ToString("F3") +
      " | "+(pa+pb+pc).ToString("F3")+" |  "+al.ToString("F4")+"  |  "+oa.ToString("F3")+"  | "+gap.ToString("F4"));
}
sb.AppendLine("  worst |dstAlpha - rootCoverage| = " + UnityEngine.Mathf.Abs(worstGap).ToString("F4") + " at r=" + worstR.ToString("F1"));

// energy: sum of paint vs root coverage over the whole tile
double sumErrPos=0, sumErrNeg=0, worstSum=0; int nOver=0;
for (int i=0;i<W*H;i++){
  int cap=buf.sampleCapacity;
  float s = buf.paint[0*cap+i]+buf.paint[1*cap+i]+buf.paint[2*cap+i];
  float rc = buf.ownCoverage[0*cap+i];
  float d = s - rc;
  if (d>1e-4f){sumErrPos+=d;nOver++;}
  if (d<-1e-4f) sumErrNeg+=-d;
  if (UnityEngine.Mathf.Abs(d)>UnityEngine.Mathf.Abs((float)worstSum)) worstSum=d;
}
sb.AppendLine("  ENERGY: sum(paint) vs rootCoverage -- samples over-counted " + nOver + ", total excess " + sumErrPos.ToString("F3") +
              ", total deficit " + sumErrNeg.ToString("F3") + ", worst per-sample " + worstSum.ToString("F4"));

// alpha deficit over the whole tile
double alphaDeficit=0; int nDef=0; float worstDef=0;
for (int i=0;i<W*H;i++){ float rc=buf.ownCoverage[i]; float al=buf.dst[i*4+3]; float d=rc-al;
  if (d>1e-3f){alphaDeficit+=d;nDef++; if(d>worstDef)worstDef=d;} }
sb.AppendLine("  ALPHA DEFICIT vs the shape's own coverage: " + nDef + " samples, total " + alphaDeficit.ToString("F3") + ", worst " + worstDef.ToString("F4"));

// ---- what the SUBTRACTIVE exclusivity would give (recompute offline) ----
sb.AppendLine();
sb.AppendLine("== C(ii): multiplicative claim*(1-dc) vs subtractive max(0, claim-dc) ==");
{
  int cap = buf.sampleCapacity; int k = doc.owners.Count;
  var dc = new float[k*cap];
  for (int o=k-1;o>=1;o--){ int a=doc.owners[o].ancestorOwner; if(a<0)continue;
    for(int i=0;i<W*H;i++){ float sub=UnityEngine.Mathf.Max(buf.claim[o*cap+i], dc[o*cap+i]); if(sub>dc[a*cap+i]) dc[a*cap+i]=sub; } }
  double sMul=0,sSub=0; float worstDiff=0; int nDiff=0;
  for(int o=0;o<k;o++) for(int i=0;i<W*H;i++){
    float c=buf.claim[o*cap+i], e=UnityEngine.Mathf.Clamp01(dc[o*cap+i]);
    float m=c*(1f-e), s=UnityEngine.Mathf.Max(0f,c-e);
    sMul+=m; sSub+=s; if(UnityEngine.Mathf.Abs(m-s)>worstDiff){worstDiff=UnityEngine.Mathf.Abs(m-s);} if(m-s>1e-4f)nDiff++; }
  sb.AppendLine("  total painted area: multiplicative " + sMul.ToString("F2") + "  subtractive " + sSub.ToString("F2") +
                "  (true area = sum of root coverage)");
  double rootArea=0; for(int i=0;i<W*H;i++) rootArea+=buf.ownCoverage[i];
  sb.AppendLine("  root coverage area = " + rootArea.ToString("F2") + "  -> multiplicative excess " + (sMul-rootArea).ToString("F2") +
                ", subtractive excess " + (sSub-rootArea).ToString("F2"));
  sb.AppendLine("  samples where multiplicative paints MORE than subtractive: " + nDiff + ", worst per-sample surplus " + worstDiff.ToString("F4"));
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r11_excl.txt", sb.ToString());
return "ok";
