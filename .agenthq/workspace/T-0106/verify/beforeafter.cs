if (UnityEditor.EditorUtility.scriptCompilationFailed) return "COMPILE FAILED";
System.Func<string,float,UnityEngine.Color,Laubrary.Shaper.ShaperNode> disc = (nm, r, col) => {
    var n = Laubrary.Shaper.ShaperNode.Primitive(
        new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r },
        nm, Laubrary.Shaper.ShaperCombineMode.Add);
    return n;
};
System.Func<UnityEngine.Color,Laubrary.Shaper.ShaperFillDef> solid = c => new Laubrary.Shaper.ShaperFillDef {
    kind = Laubrary.Shaper.ShaperFillKind.Solid, solidColor = c,
    veil = new ZUIValue(1f), heightDelta = new ZUIValue(0f),
    composite = Laubrary.Shaper.ShaperFillComposite.Over };

// FT-21's exact fixture: concentric r42 / r28 / r14, every pair ancestor-descendant.
var c3 = disc("Cd", 14f, UnityEngine.Color.blue); c3.fill = solid(new UnityEngine.Color(0.2f,0.4f,1f));
var b3 = Laubrary.Shaper.ShaperNode.Bag("B", Laubrary.Shaper.ShaperCombineMode.Add, disc("Bd", 28f, UnityEngine.Color.green), c3);
b3.fill = solid(new UnityEngine.Color(0.2f,0.9f,0.3f));
var a3 = Laubrary.Shaper.ShaperNode.Bag("A", Laubrary.Shaper.ShaperCombineMode.Add, disc("Ad", 42f, UnityEngine.Color.red), b3);
a3.fill = solid(new UnityEngine.Color(0.9f,0.2f,0.2f));

int W = 128, H = 128, n = W * H;
float chw = 0.5f * (W - 1) * 1f, chh = 0.5f * (H - 1) * 1f;
var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(a3, 0f, 0u, chw, chh);
var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W, H, 1f);
var buf = new Laubrary.Shaper.ShaperFillBuffers(n, doc.owners.Count);
var sheets = new Laubrary.Shaper.ShaperFillSheets { published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine };
Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, 0, 0, W, H, buf, sheets);

int k = doc.owners.Count, cap = buf.sampleCapacity;
// AFTER: what the shipped build produces.
int nAfter = 0; double totAfter = 0, worstAfter = 0;
// BEFORE: reconstruct the OLD accumulation - Over for every owner in forward paint order, same paint[] and
// same veil (every fill in this fixture is veil 1, so coverageEff == clamp01(paint)).
int nBefore = 0; double totBefore = 0, worstBefore = 0;
for (int i = 0; i < n; i++) {
    float cov = buf.ownCoverage[i];
    float dA = buf.dst[i*4+3];
    float d = cov - dA;
    if (d > 1e-3f) { nAfter++; totAfter += d; if (d > worstAfter) worstAfter = d; }
    float oldA = 0f;
    for (int o = 0; o < k; o++) {
        float ce = UnityEngine.Mathf.Clamp01(buf.paint[o*cap+i]);
        if (!(ce > 0f)) continue;
        oldA = ce + oldA * (1f - ce);
    }
    float d2 = cov - oldA;
    if (d2 > 1e-3f) { nBefore++; totBefore += d2; if (d2 > worstBefore) worstBefore = d2; }
}
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("owners=" + k);
sb.AppendLine("BEFORE (Over between exclusive owners, reconstructed from the SAME paint[] this run produced):");
sb.AppendLine("   samples alpha<coverage " + nBefore + "/" + n + ", total deficit " + totBefore.ToString("F3") + ", worst " + worstBefore.ToString("F4"));
sb.AppendLine("AFTER  (shipped FC-3.5a sum along the chain):");
sb.AppendLine("   samples alpha<coverage " + nAfter + "/" + n + ", total deficit " + totAfter.ToString("F3") + ", worst " + worstAfter.ToString("F4"));
return sb.ToString();
