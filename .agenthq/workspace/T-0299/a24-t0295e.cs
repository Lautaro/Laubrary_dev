var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo vbF = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { vbF = t.GetField("viewBar", BFi); if (vbF != null) break; }
var vb = vbF.GetValue(win);
var vbT = vb.GetType();
// the DECLARED fields of ZuiViewBar itself, not the VisualElement base
foreach (var f in vbT.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
{
    sb.Append("FIELD ").Append(f.Name).Append(" : ").Append(f.FieldType.Name).Append("\n");
    var val = f.GetValue(vb);
    if (val is System.Delegate dg)
    {
        var tgt = dg.Target;
        sb.Append("   delegate target=").Append(tgt == null ? "static" : tgt.GetType().Name).Append("\n");
        if (tgt != null)
            foreach (var cf in tgt.GetType().GetFields())
            {
                var cv = cf.GetValue(tgt);
                sb.Append("     closure ").Append(cf.Name).Append(" = ").Append(cv == null ? "null" : cv.GetType().Name);
                if (cv is UnityEngine.UIElements.VisualElement ve)
                {
                    int boxes = 0;
                    System.Action<UnityEngine.UIElements.VisualElement> W = null;
                    W = e => { if (e.GetType().Name == "ZuiBox") boxes++; for (int i = 0; i < e.childCount; i++) W(e[i]); };
                    W(ve);
                    sb.Append(" [ZuiBox under it = ").Append(boxes).Append("]");
                }
                sb.Append("\n");
            }
    }
}
return sb.ToString();
