// A12 — re-measure T-0277's ONE carve-out: heightFieldScale stays live on a Solid because a solid
// central-differences a HeightField fill to perturb its own normal. T-0277 reported 671 px.
// The archived probe loads "rfield0277.asset", which it never creates, so every heightField row it
// prints is a null-field row. This builds a REAL RFloat height field in memory instead.
var sb = new System.Text.StringBuilder();

var hf = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RFloat, false);
var px = new UnityEngine.Color[32 * 32];
for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
{
    float v = UnityEngine.Mathf.Abs(x - 16) / 16f;      // a ridge: non-zero gradient in X
    px[y * 32 + x] = new UnityEngine.Color(v, 0, 0, 1);
}
hf.SetPixels(px); hf.Apply();
sb.Append("hf readable=").Append(hf.isReadable).Append(" fmt=").Append(hf.format).Append('\n');

System.Func<bool, Laubrary.Shaper.ShaperDocument> mk = solid =>
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.canvasWidth = 96; d.canvasHeight = 64; d.frameCount = 4; d.seed = 7u;
    d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    var l = new Laubrary.Shaper.ShaperLayer { name = "A", enabled = true, id = 1,
        root = new Laubrary.Shaper.ShaperNode { name = "N" } };
    if (solid)
    {
        l.root.kind = Laubrary.Shaper.ShaperNodeKind.Solid;
        l.root.solid = new Laubrary.Shaper.ShaperSolidDef { form = Laubrary.Shaper.ShaperSolidForm.Pyramid };
    }
    else
    {
        l.root.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
        l.root.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse };
        l.root.primitive.ellipseRxDial = new ZUIValue(28f);
        l.root.primitive.ellipseRyDial = new ZUIValue(20f);
    }
    l.root.fill = Laubrary.Shaper.ShaperFillDef.DefaultRootFill();
    l.root.fill.kind = Laubrary.Shaper.ShaperFillKind.HeightField;
    l.root.fill.heightField = hf;
    d.layers.Add(l);
    return d;
};

System.Func<Laubrary.Shaper.ShaperDocument, int, UnityEngine.Color32[]> R =
    (d, fi) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, fi);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) =>
{ int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };

foreach (var solid in new[] { true, false })
{
    var d = mk(solid);
    var f = d.layers[0].root.fill;
    f.heightFieldScale = new ZUIValue(1f);
    var a0 = R(d, 0); var a1 = R(d, 2);
    f.heightFieldScale = new ZUIValue(6f);
    var b0 = R(d, 0); var b1 = R(d, 2);
    int lit = 0; foreach (var c in a0) if (c.a > 0) lit++;
    sb.Append(solid ? "SOLID Pyramid" : "PRIMITIVE ellipse")
      .Append("  scale 1->6: f0=").Append(diff(a0, b0))
      .Append(" f2=").Append(diff(a1, b1))
      .Append("   lit=").Append(lit).Append('\n');

    // and the tint on the same fill, as a control that the fill kind is genuinely compiled
    f.heightFieldScale = new ZUIValue(1f);
    var c0 = R(d, 0);
    f.heightFieldTint = UnityEngine.Color.magenta;
    var c1 = R(d, 0);
    sb.Append("    control heightFieldTint: ").Append(diff(c0, c1)).Append('\n');
    // what the compiler said about the field
    UnityEngine.Object.DestroyImmediate(d);
}
UnityEngine.Object.DestroyImmediate(hf);
return sb.ToString();
