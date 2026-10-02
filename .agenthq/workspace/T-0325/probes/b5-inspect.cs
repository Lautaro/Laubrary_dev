var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var vt = ZType("MirageView");
sb.Append("MirageView fields: ");
foreach (var f in vt.GetFields(BFi)) sb.Append(f.Name).Append(":").Append(f.FieldType.Name).Append(" ");
sb.Append("\n");
var view = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Mirage/AuditT325View.asset", vt);
foreach (var f in vt.GetFields(BFi)) {
  var v = f.GetValue(view); var il = v as System.Collections.IList;
  if (il != null) sb.Append(f.Name).Append("=").Append(il.Count).Append(" ");
}
sb.Append("\n");
var rigT = ZType("MirageRig");
sb.Append("MirageRig methods: ");
foreach (var m in rigT.GetMethods(BFi)) if (m.DeclaringType == rigT) sb.Append(m.Name).Append(" ");
sb.Append("\nMirageRig fields: ");
foreach (var f in rigT.GetFields(BFi)) if (f.DeclaringType == rigT) sb.Append(f.Name).Append(" ");
sb.Append("\n");
var mav = ZType("MirageActiveView");
sb.Append("MirageActiveView members: ");
foreach (var m in mav.GetMembers(BFi|System.Reflection.BindingFlags.Static)) sb.Append(m.Name).Append(" ");
return sb.ToString();
