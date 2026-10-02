// The 820x520 floor: which ZuiAssetWindow subclasses declare it, and is each clean AT it?
var sb = new System.Text.StringBuilder();
var subs = new System.Collections.Generic.List<System.Type>();
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{ System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
  foreach (var t in ts) { for (var b = t.BaseType; b != null; b = b.BaseType)
      if (b.IsGenericType && b.GetGenericTypeDefinition().Name.StartsWith("ZuiAssetWindow")) { subs.Add(t); break; } } }
sb.Append("ZuiAssetWindow subclasses: ").Append(subs.Count).Append("\n");
foreach (var t in subs)
{
    UnityEditor.EditorWindow w = null;
    foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x != null && x.GetType() == t) w = x;
    sb.Append("  ").Append(t.Name).Append(" open=").Append(w != null)
      .Append(w != null ? " min=" + w.minSize : "").Append("\n");
}
return sb.ToString();
