// T-0313 — read (never create) the draft LauminaryVersion assets and list their laumination names, so the
// Laumination Builder can be opened on a real one.  Nothing is written: LoadAssetAtPath only.
var sb = new System.Text.StringBuilder();
var vt = ZType("LauminaryVersion");
if (vt == null) return "NO LauminaryVersion TYPE";
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:" + vt.Name))
{
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var o = UnityEditor.AssetDatabase.LoadAssetAtPath(p, vt);
    if (o == null) continue;
    sb.Append("VERSION ").Append(p);
    var af = vt.GetField("animations", System.Reflection.BindingFlags.Instance
           | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
    var list = af == null ? null : af.GetValue(o) as System.Collections.IList;
    sb.Append(" animations=").Append(list == null ? -1 : list.Count);
    if (list != null)
        for (int i = 0; i < list.Count && i < 8; i++)
        {
            var a = list[i]; if (a == null) continue;
            var nf = a.GetType().GetField("name", System.Reflection.BindingFlags.Instance
                   | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            sb.Append(" '").Append(nf == null ? "?" : nf.GetValue(a)).Append("'");
        }
    sb.Append("\n");
}
return sb.ToString();
