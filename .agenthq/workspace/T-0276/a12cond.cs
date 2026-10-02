// A12 — the conditional questions the fixture sweep raised, each measured in the state the CARD offers.
var SB = new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM  = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BFs);
var newMemberM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewBagMember", BFs);
string DIR = "Assets/Shaper/AuditA12";
var sheetTex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(DIR + "/a12sheet.png");

System.Func<ZuiGradient> Grad = () =>
{
    var g = new ZuiGradient(); g.stops.Clear();
    g.stops.Add(new ZuiGradientStop { pos = 0f, color = UnityEngine.Color.yellow });
    g.stops.Add(new ZuiGradientStop { pos = 1f, color = new UnityEngine.Color(0f, 0.2f, 1f, 1f) });
    return g;
};
System.Func<Laubrary.Shaper.ShaperDocument> Blank = () =>
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.canvasWidth = 96; d.canvasHeight = 64; d.frameCount = 4; d.seed = 7u;
    d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    return d;
};
Laubrary.Shaper.ShaperDocument DOC = null;
System.Func<UnityEngine.Color32[]> R = () => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(DOC, 0);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> Diff = (a, b) =>
{ int n = 0; for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) n++; return n; };

// ── (a) Fit and (b) Posterise, per fill kind, on a WIDE ellipse (34x13 — a non-square anchor box) ────
{
    var d = Blank();
    var l = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "N", d });
    l.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse;
    l.root.primitive.ellipseRxDial = new ZUIValue(34f);
    l.root.primitive.ellipseRyDial = new ZUIValue(13f);
    d.layers.Add(l); DOC = d;
    var f = l.root.fill;
    f.texture = sheetTex; f.gradient = Grad(); f.proceduralGradient = Grad();
    f.proceduralKind = Laubrary.Shaper.ShaperProceduralKind.Noise;

    SB.Append("kind\tfitPx\tposterisePx\n");
    foreach (var k in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperFillKind)))
    {
        f.kind = (Laubrary.Shaper.ShaperFillKind)k;
        f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
        f.quantiseLevels = new ZUIValue(0f);
        var b0 = R();
        f.fit = Laubrary.Shaper.ShaperFillFit.Stretch;
        int dFit = Diff(b0, R());
        f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
        f.quantiseLevels = new ZUIValue(3f);
        int dPost = Diff(b0, R());
        f.quantiseLevels = new ZUIValue(0f);
        SB.Append(k).Append('\t').Append(dFit).Append('\t').Append(dPost).Append('\n');
    }

    // the strip, per parameterisation — Fit is declared live for IndexedStrip
    f.kind = Laubrary.Shaper.ShaperFillKind.IndexedStrip;
    SB.Append("-- strip parameterisation x Fit --\n");
    foreach (var pz in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperStripParameterisation)))
    {
        f.stripParameterisation = (Laubrary.Shaper.ShaperStripParameterisation)pz;
        f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
        var b0 = R();
        f.fit = Laubrary.Shaper.ShaperFillFit.Stretch;
        SB.Append(pz).Append('\t').Append(Diff(b0, R())).Append('\n');
        f.fit = Laubrary.Shaper.ShaperFillFit.Uniform;
    }
    // and whether a strip SLOT's own colour reaches the picture
    f.stripParameterisation = Laubrary.Shaper.ShaperStripParameterisation.Angular;
    if (f.stripSlots != null && f.stripSlots.Count > 0)
    {
        var b0 = R();
        var keep = f.stripSlots[0].color;
        f.stripSlots[0].color = UnityEngine.Color.magenta;
        SB.Append("strip slot[0].color px=").Append(Diff(b0, R()))
          .Append("  (slots=").Append(f.stripSlots.Count).Append(")\n");
        f.stripSlots[0].color = keep;
    }
    else SB.Append("strip slots = ").Append(f.stripSlots == null ? "null" : "0").Append('\n');
    UnityEngine.Object.DestroyImmediate(d);
}

// ── (c) the three join dials x the three combine modes, on a real two-member bag ─────────────────────
{
    var d = Blank();
    var l = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "Bag", d });
    l.root.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
    l.root.children.Clear();
    l.root.children.Add((Laubrary.Shaper.ShaperNode)newMemberM.Invoke(null, new object[] { "M1", d }));
    var m2 = (Laubrary.Shaper.ShaperNode)newMemberM.Invoke(null, new object[] { "M2", d });
    m2.transform.translateX = new ZUIValue(14f);
    l.root.children.Add(m2);
    d.layers.Add(l); DOC = d;
    SB.Append("-- combine mode x join dial --\nmode\tBlendWidth\tSharpness\tCarveStrength\n");
    foreach (var cm in System.Enum.GetValues(typeof(Laubrary.Shaper.ShaperCombineMode)))
    {
        m2.mode = (Laubrary.Shaper.ShaperCombineMode)cm;
        m2.blend.widthDial = new ZUIValue(0f);
        m2.blend.sharpnessDial = new ZUIValue(0.5f);
        m2.blend.carveStrengthDial = new ZUIValue(1f);
        var b0 = R();
        m2.blend.widthDial = new ZUIValue(16f); int dW = Diff(b0, R());
        // sharpness only means anything with a band to shape, so measure it AT a real width
        var b1 = R();
        m2.blend.sharpnessDial = new ZUIValue(0.05f); int dS = Diff(b1, R());
        m2.blend.sharpnessDial = new ZUIValue(0.5f);
        m2.blend.widthDial = new ZUIValue(0f);
        m2.blend.carveStrengthDial = new ZUIValue(0.2f); int dC = Diff(b0, R());
        m2.blend.carveStrengthDial = new ZUIValue(1f);
        SB.Append(cm).Append('\t').Append(dW).Append('\t').Append(dS).Append('\t').Append(dC).Append('\n');
    }
    UnityEngine.Object.DestroyImmediate(d);
}
return SB.ToString();
