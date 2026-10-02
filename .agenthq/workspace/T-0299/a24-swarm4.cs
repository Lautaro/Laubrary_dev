var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var changeM = WT.GetMethod("Change", BFi);
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var node = doc.layers[0].root;
var sw = node.swarm;

// give the node's OWN content something that varies with phase: an animated star length
changeM.Invoke(win, new object[] { (System.Action)(() => {
    var lv = new ZUIValue(0.62f); lv.min = 0.15f; lv.max = 0.95f; lv.mode = ZUIValue.Mode.Steps;
    node.primitive.starLength = lv;
}) });
sb.Append("star length mode=").Append(node.primitive.starLength.mode).Append("\n");
System.Func<float, int> Try = v =>
{
    var before = new UnityEngine.Color32[doc.frameCount][];
    for (int i = 0; i < doc.frameCount; i++) before[i] = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, i);
    float orig = sw.lifetimeStagger;
    changeM.Invoke(win, new object[] { (System.Action)(() => sw.lifetimeStagger = v) });
    int max = 0;
    for (int i = 0; i < doc.frameCount; i++)
    { var now = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, i); int n = 0;
      for (int k = 0; k < now.Length; k++) if (before[i][k].a != now[k].a || before[i][k].r != now[k].r) n++;
      if (n > max) max = n; }
    changeM.Invoke(win, new object[] { (System.Action)(() => sw.lifetimeStagger = orig) });
    return max;
};
sb.Append("lifetimeStagger 1 -> 0 with an ANIMATED star length: max changed px over 16 frames = ").Append(Try(0f)).Append("\n");
sb.Append("lifetimeStagger 1 -> 0.5                            : ").Append(Try(0.5f)).Append("\n");
return sb.ToString();
