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
d1.layers[0].height = new Laubrary.Shaper.ShaperHeightDef { technique = Laubrary.Shaper.ShaperExtrusionTechnique.Dome, depth = new ZUIValue(8f) };
var afterHeightOnly = new[] { render(d1,0), render(d1,7), render(d1,15) };
int d1diff = 0; for(int i=0;i<3;i++) d1diff += diff(before[i], afterHeightOnly[i]);
sb.Append("Height on, normalKind default(Constant): diffPx=").Append(d1diff).Append('\n');

d1.layers[0].response.normalKind = Laubrary.Shaper.ShaperNormalKind.Profile;
var afterProfile = new[] { render(d1,0), render(d1,7), render(d1,15) };
int d2diff = 0; for(int i=0;i<3;i++) d2diff += diff(before[i], afterProfile[i]);
sb.Append("Height on + normalKind=Profile: diffPx=").Append(d2diff).Append('\n');

return sb.ToString();
