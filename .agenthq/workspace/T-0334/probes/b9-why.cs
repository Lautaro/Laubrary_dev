var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var rigT = ZType("MirageRig");
var rig = UnityEngine.Object.FindFirstObjectByType(rigT) as UnityEngine.Component;
var live = (System.Collections.IDictionary)rigT.GetField("_live", BFi).GetValue(rig);
foreach (System.Collections.DictionaryEntry kv in live)
{
    var go = kv.Value as UnityEngine.GameObject;
    var subT = ZType("MirageSubject");
    var sub = go.GetComponent(subT);
    foreach (var f in subT.GetFields(BFi))
    {
        if (f.DeclaringType != subT) continue;
        var v = f.GetValue(sub);
        sb.Append("sub.").Append(f.Name).Append("=").Append(v == null ? "<null>" : v.ToString()).Append("\n");
    }
    foreach (var p in new string[]{ "Spawned", "Player", "Weapon", "Combatant", "Health" })
    { var pi = subT.GetProperty(p, BFi); if (pi != null) sb.Append("sub.").Append(p).Append("=").Append(pi.GetValue(sub) == null ? "<null>" : pi.GetValue(sub).ToString()).Append("\n"); }
    var up = subT.GetMethod("Update", BFi);
    if (up != null) { up.Invoke(sub, null); up.Invoke(sub, null); sb.Append("ticked Update x2\n"); }
    var pi2 = subT.GetProperty("Spawned", BFi);
    sb.Append("after tick Spawned=").Append(pi2.GetValue(sub) == null ? "<null>" : pi2.GetValue(sub).ToString()).Append("\n");
    sb.Append("children now=").Append(go.transform.childCount).Append("\n");
}
var view = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Mirage/AuditT334View.asset", ZType("MirageView"));
var pv = view.GetType().GetField("previewables", BFi).GetValue(view) as System.Collections.IList;
foreach (var e in pv) { sb.Append("ENTRY "); foreach (var f in e.GetType().GetFields(BFi)) sb.Append(f.Name).Append("=").Append(f.GetValue(e) == null ? "<null>" : f.GetValue(e).ToString()).Append(" "); sb.Append("\n"); }
return sb.ToString();
