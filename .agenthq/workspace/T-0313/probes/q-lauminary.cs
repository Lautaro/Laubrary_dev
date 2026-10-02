// T-0313 — what a Lauminary actually holds, so the Laumination Builder can be opened the way an author
// opens it (OpenForEdit(lauminary, animName)) rather than on its empty shell.
var sb = new System.Text.StringBuilder();
var lauT = ZType("Lauminary");
if (lauT == null) return "NO Lauminary TYPE";
sb.Append(lauT.FullName).Append(" fields:\n");
foreach (var f in lauT.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                               | System.Reflection.BindingFlags.NonPublic))
    sb.Append("  ").Append(f.FieldType.Name).Append(" ").Append(f.Name).Append("\n");
foreach (var p in lauT.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
    sb.Append("  prop ").Append(p.PropertyType.Name).Append(" ").Append(p.Name).Append("\n");

foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:" + lauT.Name))
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var o = UnityEditor.AssetDatabase.LoadAssetAtPath(path, lauT);
    sb.Append("\nASSET ").Append(path).Append("\n");
    if (o == null) continue;
    foreach (var f in lauT.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                   | System.Reflection.BindingFlags.NonPublic))
    {
        object v = null; try { v = f.GetValue(o); } catch { }
        var list = v as System.Collections.IList;
        if (list != null)
        {
            sb.Append("   ").Append(f.Name).Append(" count=").Append(list.Count);
            if (list.Count > 0 && list[0] != null)
            {
                sb.Append(" first=").Append(list[0].GetType().Name);
                foreach (var ff in list[0].GetType().GetFields(System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    if (ff.FieldType == typeof(string))
                        sb.Append(" ").Append(ff.Name).Append("='").Append(ff.GetValue(list[0])).Append("'");
            }
            sb.Append("\n");
        }
        else if (v is string s) sb.Append("   ").Append(f.Name).Append("='").Append(s).Append("'\n");
    }
    var subs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
    sb.Append("   subAssets=").Append(subs.Length);
    foreach (var s2 in subs) if (s2 != null) sb.Append(" [").Append(s2.GetType().Name).Append(":").Append(s2.name).Append("]");
    sb.Append("\n");
}
return ZDump("lauminary.txt", sb.ToString()) + "\n" + sb.ToString().Substring(0, System.Math.Min(3000, sb.Length));
