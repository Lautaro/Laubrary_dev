if (UnityEditor.EditorUtility.scriptCompilationFailed) return "COMPILE FAILED";

System.Func<float,float,float,Laubrary.Shaper.ShaperNode> disc = (r, x, y) => {
    var n = Laubrary.Shaper.ShaperNode.Primitive(
        new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r },
        "d", Laubrary.Shaper.ShaperCombineMode.Add);
    n.transform.translate = new UnityEngine.Vector2(x, y);
    return n;
};
System.Func<UnityEngine.Color,float,Laubrary.Shaper.ShaperFillComposite,Laubrary.Shaper.ShaperFillDef> solid =
  (c, v, comp) => new Laubrary.Shaper.ShaperFillDef {
    kind = Laubrary.Shaper.ShaperFillKind.Solid, solidColor = c,
    veil = new ZUIValue(v), heightDelta = new ZUIValue(0f), composite = comp };

int W = 128, H = 128, n = W * H;
float chw = 0.5f * (W - 1) * 1f, chh = 0.5f * (H - 1) * 1f;
var grid = Laubrary.Shaper.ShaperSampleGrid.Centred(W, H, 1f);
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("fixture | owners | samples where NEW dst != OLD-algorithm dst | worst |delta| | worst channel");

System.Action<string, Laubrary.Shaper.ShaperNode> run = (label, root) => {
    var doc = Laubrary.Shaper.ShaperFillResolver.Resolve(root, 0f, 0u, chw, chh);
    var buf = new Laubrary.Shaper.ShaperFillBuffers(n, doc.owners.Count);
    var sheets = new Laubrary.Shaper.ShaperFillSheets { published = Laubrary.Shaper.ShaperQuantitySet.ShippedShapeEngine };
    Laubrary.Shaper.ShaperFillResolver.PaintTile(doc, grid, 0, 0, W, H, buf, sheets);

    int k = doc.owners.Count, cap = buf.sampleCapacity;
    // Reconstruct the PREVIOUS build's step 5/6: forward paint order, Over/Add straight into one destination.
    var old = new float[n * 4];
    var albedo = new float[n * 3]; var veil = new float[n]; var hd = new float[n];
    for (int o = 0; o < k; o++) {
        var ow = doc.owners[o];
        var own = sheets; own.coverage = buf.ownCoverage; own.edgeDistance = buf.ownDistance;
        Laubrary.Shaper.ShaperFillOps.FillTile(ow.fill, grid, 0, 0, W, H, own,
            new Laubrary.Shaper.ShaperFillEmit { albedo = albedo, veil = veil, heightDelta = hd },
            o * cap, W, 0, W);
        bool add = ow.fill.op.composite == Laubrary.Shaper.ShaperFillComposite.Add;
        for (int i = 0; i < n; i++) {
            float ce = UnityEngine.Mathf.Clamp01(buf.paint[o * cap + i]) * UnityEngine.Mathf.Clamp01(veil[i]);
            if (!(ce > 0f)) continue;
            int a3 = i * 3, d4 = i * 4;
            float r = albedo[a3], g = albedo[a3 + 1], b = albedo[a3 + 2];
            if (add) { old[d4] += r * ce; old[d4 + 1] += g * ce; old[d4 + 2] += b * ce; }
            else {
                float inv = 1f - ce;
                old[d4] = r * ce + old[d4] * inv;
                old[d4 + 1] = g * ce + old[d4 + 1] * inv;
                old[d4 + 2] = b * ce + old[d4 + 2] * inv;
                old[d4 + 3] = ce + old[d4 + 3] * inv;
            }
        }
    }
    int bad = 0; float worst = 0f; int worstCh = -1;
    for (int i = 0; i < n * 4; i++) {
        float d = UnityEngine.Mathf.Abs(buf.dst[i] - old[i]);
        if (d > 0f) { bad++; if (d > worst) { worst = d; worstCh = i & 3; } }
    }
    sb.AppendLine("  " + label.PadRight(34) + " | " + k + " | " + bad + "/" + (n * 4) +
                  " | " + worst.ToString("E3") + " | ch " + worstCh);
};

// A — one owner. Structurally cannot change: dst = 0 Over acc[0] = acc[0] = the own contribution.
var a = disc(34f, 0f, 0f); a.fill = solid(new UnityEngine.Color(0.95f, 0.45f, 0.12f), 1f, Laubrary.Shaper.ShaperFillComposite.Over);
run("A single Solid owner", a);
var a2 = disc(34f, 0f, 0f); a2.fill = solid(new UnityEngine.Color(1f, 0.8f, 0.4f), 0.7f, Laubrary.Shaper.ShaperFillComposite.Add);
run("A single Add owner, veil 0.7", a2);

// B — SIBLINGS only (the bag's own paint is 0 where the members cover). Over between siblings is unchanged.
{
    var lo = disc(30f, -10f, 0f); lo.fill = solid(new UnityEngine.Color(1f, 0f, 0f), 0.5f, Laubrary.Shaper.ShaperFillComposite.Over);
    var up = disc(30f, 10f, 0f); up.fill = solid(new UnityEngine.Color(0f, 0f, 1f), 0.5f, Laubrary.Shaper.ShaperFillComposite.Over);
    var bag = Laubrary.Shaper.ShaperNode.Bag("Pair", Laubrary.Shaper.ShaperCombineMode.Add, lo, up);
    bag.fill = solid(UnityEngine.Color.white, 1f, Laubrary.Shaper.ShaperFillComposite.Over);
    run("B two overlapping siblings", bag);
}
{
    var lo = disc(32f, -12f, 0f); lo.fill = solid(new UnityEngine.Color(0.12f, 0.20f, 0.62f), 1f, Laubrary.Shaper.ShaperFillComposite.Over);
    var up = disc(24f, 14f, 0f); up.fill = solid(new UnityEngine.Color(1f, 0.62f, 0.16f), 0.85f, Laubrary.Shaper.ShaperFillComposite.Add);
    var bag = Laubrary.Shaper.ShaperNode.Bag("Comp", Laubrary.Shaper.ShaperCombineMode.Add, lo, up);
    bag.fill = solid(new UnityEngine.Color(0.06f, 0.06f, 0.09f), 1f, Laubrary.Shaper.ShaperFillComposite.Over);
    run("B siblings, upper is Add (cell 17)", bag);
}

// C — NESTED ownership. This is the case that MUST change, and only at the internal boundary.
{
    var inner = disc(16f, 0f, 0f); inner.fill = solid(new UnityEngine.Color(0.98f, 0.85f, 0.15f), 1f, Laubrary.Shaper.ShaperFillComposite.Over);
    var outer = disc(34f, 0f, 0f);
    var bag = Laubrary.Shaper.ShaperNode.Bag("Excl", Laubrary.Shaper.ShaperCombineMode.Add, outer, inner);
    bag.fill = solid(new UnityEngine.Color(0.72f, 0.10f, 0.30f), 1f, Laubrary.Shaper.ShaperFillComposite.Over);
    run("C nested bag+child (cell 19)", bag);
}
{
    var c3 = disc(14f, 0f, 0f); c3.fill = solid(new UnityEngine.Color(0.2f, 0.4f, 1f), 1f, Laubrary.Shaper.ShaperFillComposite.Over);
    var b3 = Laubrary.Shaper.ShaperNode.Bag("B", Laubrary.Shaper.ShaperCombineMode.Add, disc(28f, 0f, 0f), c3);
    b3.fill = solid(new UnityEngine.Color(0.2f, 0.9f, 0.3f), 1f, Laubrary.Shaper.ShaperFillComposite.Over);
    var a3 = Laubrary.Shaper.ShaperNode.Bag("A", Laubrary.Shaper.ShaperCombineMode.Add, disc(42f, 0f, 0f), b3);
    a3.fill = solid(new UnityEngine.Color(0.9f, 0.2f, 0.2f), 1f, Laubrary.Shaper.ShaperFillComposite.Over);
    run("C three-deep chain (FT-21)", a3);
}
return sb.ToString();
