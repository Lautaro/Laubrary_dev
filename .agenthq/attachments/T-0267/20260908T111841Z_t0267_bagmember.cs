var sb = new System.Text.StringBuilder();
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
var newMemberM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewBagMember", BF);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer =
    (n, d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { n, d });
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperNode> NewBagMember =
    (n, d) => (Laubrary.Shaper.ShaperNode)newMemberM.Invoke(null, new object[] { n, d });
System.Func<Laubrary.Shaper.ShaperDocument> fresh = () =>
{
    var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    d.layers.Add(NewLayer("Layer 1", d));
    d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
    return d;
};
System.Func<Laubrary.Shaper.ShaperDocument, int, UnityEngine.Color32[]> render = (d, f) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, f);
System.Func<UnityEngine.Color32[], int> opaque = (a) => { int n=0; foreach(var p in a) if (p.a>0) n++; return n; };

var d1 = fresh();
d1.layers[0].root.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
d1.layers[0].root.children = new System.Collections.Generic.List<Laubrary.Shaper.ShaperNode> { NewBagMember("Member 1", d1) };
var frame = render(d1, 8);
sb.Append("+Add member (FIXED, quarter-canvas): opaque=").Append(opaque(frame)).Append("/").Append(frame.Length).Append('\n');
return sb.ToString();
