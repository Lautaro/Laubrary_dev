var sb = new System.Text.StringBuilder();
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer =
    (n, d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { n, d });
System.Func<Laubrary.Shaper.ShaperDocument> fresh = () =>
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.layers.Add(NewLayer("Layer 1", d));
    d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    return d;
};
System.Func<Laubrary.Shaper.ShaperDocument, int, UnityEngine.Color32[]> render = (d, f) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, f);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };

var d1 = fresh();
var before = new[] { render(d1,0), render(d1,7), render(d1,15) };
// Replicates the FIXED toggle body verbatim (node.sweep.enabled = true; extentDegreesDial seeded to 270 since it starts Static/360).
d1.layers[0].root.sweep.enabled = true;
if (d1.layers[0].root.sweep.extentDegreesDial.mode == ZUIValue.Mode.Static && d1.layers[0].root.sweep.extentDegreesDial.staticValue >= 359.9f)
    d1.layers[0].root.sweep.extentDegreesDial.staticValue = 270f;
var after = new[] { render(d1,0), render(d1,7), render(d1,15) };
int total = 0; for(int i=0;i<3;i++) total += diff(before[i], after[i]);
sb.Append("Sweep on, FIXED (extent seeded 270): diffPx=").Append(total).Append('\n');
return sb.ToString();
