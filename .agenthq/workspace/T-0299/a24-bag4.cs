var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var node = doc.layers[0].root;
var changeM = WT.GetMethod("Change", BFi);
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }

// tidy the contamination from an earlier blanket TextField write, and drop to two members
changeM.Invoke(win, new object[] { (System.Action)(() => {
    doc.layers[0].name = "Bag layer";
    while (node.children.Count > 2) node.children.RemoveAt(node.children.Count - 1);
    node.children[0].name = "Member 1"; node.children[1].name = "Member 2";
    // offset member 2 so the three modes are actually distinguishable
    node.children[1].transform.translateX = new ZUIValue(10f);
}) });
var drillF = WT.GetField("drillPath", BFi);
(drillF.GetValue(win) as System.Collections.IList).Clear();
rb.Invoke(win, null);

System.Func<int> Lit = () => { var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 0); int n = 0; foreach (var c in px) if (c.a > 0) n++; return n; };
System.Func<UnityEngine.Color32[]> Px = () => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 0);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> Diff = (a, b) => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };

sb.Append("two members, member 2 offset +10x\n");
var prev = Px();
foreach (var m in new Laubrary.Shaper.ShaperCombineMode[] { Laubrary.Shaper.ShaperCombineMode.Add, Laubrary.Shaper.ShaperCombineMode.Subtract, Laubrary.Shaper.ShaperCombineMode.Intersect })
{
    changeM.Invoke(win, new object[] { (System.Action)(() => node.children[1].mode = m) });
    var now = Px();
    sb.Append("  mode ").Append(m).Append(" -> lit=").Append(Lit()).Append(" changedFromPrev=").Append(Diff(prev, now)).Append("\n");
    prev = now;
}
return sb.ToString();
