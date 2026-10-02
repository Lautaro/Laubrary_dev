var sb = new System.Text.StringBuilder();
System.Func<string, System.Type> T = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
  { System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; } foreach (var t in ts) if (t.Name == n) return t; } return null; };
foreach (var n in new string[] { "PropWindow", "TilesetBuilderWindow" })
{
    var t = T(n);
    var m = t.GetMethod("OpenFor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    var pt = m.GetParameters()[0].ParameterType;
    sb.Append(n).Append(".OpenFor(").Append(pt.Name).Append(")\n");
    // open with NO asset — the empty state the card asks for
    m.Invoke(null, new object[] { null });
    UnityEditor.EditorWindow w = null;
    foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x != null && x.GetType() == t) w = x;
    w.Focus();
    sb.Append(ZSummary(n + "-empty"));
    ZAudit(w, n + "-empty");
    sb.Append(ZSummary(n + "-empty"));
    sb.Append("  pos=").Append(w.position).Append(" min=").Append(w.minSize).Append(" title='").Append(w.titleContent.text).Append("'\n");
}
return sb.ToString();
