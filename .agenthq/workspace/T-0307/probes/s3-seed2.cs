// Does the Canvas card's "Document seed" reach anything beyond the two consumers its tooltip names?
// Each case sets ONE dial to Min-Max and changes only doc.seed.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer =
    (n, d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { n, d });
System.Func<Laubrary.Shaper.ShaperDocument, int, UnityEngine.Color32[]> render = (d, f) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, f);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };
System.Func<float, float, ZUIValue> MM = (lo, hi) => { var v = new ZUIValue(lo); v.mode = ZUIValue.Mode.MinMax; v.min = lo; v.max = hi; return v; };

System.Func<Laubrary.Shaper.ShaperDocument> fresh = () =>
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.layers.Add(NewLayer("Layer 1", d));
    return d;
};
System.Func<Laubrary.Shaper.ShaperDocument, int> sweep = d =>
{
    int moved = 0;
    foreach (int f in new[] { 0, 4, 8, 12 })
    { d.seed = 1u; var a = render(d, f); d.seed = 777u; var b = render(d, f); moved += diff(a, b); }
    d.seed = 1u; return moved;
};

// A. a light's intensity as Min-Max — the tooltip's own first named consumer
var dA = fresh();
dA.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true, intensity = MM(0.2f, 3f) });
dA.layers[0].response.receiveLighting = true;
sb.Append("A  light intensity Min-Max 0.2..3            : ").Append(sweep(dA)).Append(" px\n");

// B. the layer's own Lighting response Intensity x as Min-Max (a LAYER dial, not a light dial)
var dB = fresh();
dB.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
dB.layers[0].response.receiveLighting = true;
dB.layers[0].response.intensityScale = MM(0.2f, 3f);
sb.Append("B  layer response Intensity x Min-Max        : ").Append(sweep(dB)).Append(" px\n");

// C. a SHAPE dial as Min-Max — the primitive's own size
var dC = fresh();
var pC = dC.layers[0].root.primitive;
sb.Append("C  primitive kind=").Append(pC == null ? "<null>" : pC.kind.ToString()).Append("\n");
if (pC != null) { pC.rectHalfWDial = MM(8f, 40f); pC.rectHalfHDial = MM(8f, 40f); }
sb.Append("C  primitive rect half-extents Min-Max 8..40 : ").Append(sweep(dC)).Append(" px\n");

// D. a TRANSFORM dial as Min-Max
var dD = fresh();
dD.layers[0].root.transform = new Laubrary.Shaper.ShaperTransformBlock();
dD.layers[0].root.transform.translateX = MM(-20f, 20f);
sb.Append("D  transform Translate X Min-Max -20..20     : ").Append(sweep(dD)).Append(" px\n");

// E. a FILL dial as Min-Max
var dE = fresh();
dE.layers[0].root.fill = new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid, solidColor = UnityEngine.Color.red, authored = true };
dE.layers[0].root.fill.veil = MM(0.1f, 1f);
sb.Append("E  fill Veil Min-Max 0.1..1                  : ").Append(sweep(dE)).Append(" px\n");

UnityEngine.Object.DestroyImmediate(dA); UnityEngine.Object.DestroyImmediate(dB);
UnityEngine.Object.DestroyImmediate(dC); UnityEngine.Object.DestroyImmediate(dD); UnityEngine.Object.DestroyImmediate(dE);
return sb.ToString();
