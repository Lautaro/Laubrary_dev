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

// a clean PRIMITIVE ellipse with a swarm of 5
changeM.Invoke(win, new object[] { (System.Action)(() => {
    node.kind = Laubrary.Shaper.ShaperNodeKind.Primitive;
    node.primitive = new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse };
    node.primitive.ellipseRxDial = new ZUIValue(8f);
    node.primitive.ellipseRyDial = new ZUIValue(8f);
    node.swarm = new Laubrary.Shaper.ShaperSwarmDef();
    node.swarm.enabled = true;
    node.swarm.count = 5;
}) });

System.Func<UnityEngine.Color32[]> Px = () => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 4);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> Diff = (a, b) =>
{ int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };
System.Func<int> Lit = () => { int n = 0; foreach (var c in Px()) if (c.a > 0) n++; return n; };

sb.Append("swarm on, count=5, ellipse r8: lit=").Append(Lit()).Append("\n");
var sw = node.swarm;
var swT = sw.GetType();
var baseline = Px();

// sweep EVERY public authored field of ShaperSwarmDef by reflection, not a hand list
foreach (var f in swT.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
{
    if (f.Name == "enabled") continue;
    object orig = f.GetValue(sw);
    object test = null;
    if (f.FieldType == typeof(ZUIValue)) test = new ZUIValue(((ZUIValue)orig).staticValue + 17f);
    else if (f.FieldType == typeof(int)) test = ((int)orig) == 9 ? 3 : 9;
    else if (f.FieldType == typeof(uint)) test = (uint)7777;
    else if (f.FieldType == typeof(float)) test = UnityEngine.Mathf.Approximately((float)orig, 0.5f) ? 0.9f : 0.5f;
    else if (f.FieldType == typeof(bool)) test = !((bool)orig);
    else if (f.FieldType.IsEnum)
    {
        var vals = System.Enum.GetValues(f.FieldType);
        foreach (var v in vals) if (!v.Equals(orig)) { test = v; break; }
    }
    if (test == null) { sb.Append("  SKIP ").Append(f.Name).Append(" (").Append(f.FieldType.Name).Append(")\n"); continue; }
    changeM.Invoke(win, new object[] { (System.Action)(() => f.SetValue(sw, test)) });
    int d = Diff(baseline, Px());
    changeM.Invoke(win, new object[] { (System.Action)(() => f.SetValue(sw, orig)) });
    sb.Append(d == 0 ? "  DEAD " : "  live ").Append(f.Name).Append(" (").Append(f.FieldType.Name).Append(" ").Append(orig).Append(" -> ").Append(test).Append(") changed=").Append(d).Append("\n");
}
return sb.ToString();
