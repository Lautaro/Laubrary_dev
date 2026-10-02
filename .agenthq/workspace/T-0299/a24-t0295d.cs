var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

System.Reflection.FieldInfo vbF = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { vbF = t.GetField("viewBar", BFi); if (vbF != null) break; }
var vb = vbF.GetValue(win);
sb.Append("viewBar=").Append(vb == null ? "NULL" : vb.GetType().Name).Append("\n");
foreach (var f in vb.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
    sb.Append("  vb field ").Append(f.Name).Append(" : ").Append(f.FieldType.Name).Append("\n");
return sb.ToString();
