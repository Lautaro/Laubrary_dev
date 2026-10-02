// Open a batch of never-audited windows through their own menu items, size them, report what appeared.
string list = UnityEditor.EditorPrefs.GetString("T326.menus", "");
var sb = new System.Text.StringBuilder();
foreach (var m in list.Split('|'))
{
    if (string.IsNullOrEmpty(m)) continue;
    bool ok = false; string err = "";
    try { ok = UnityEditor.EditorApplication.ExecuteMenuItem(m); } catch (System.Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
    sb.Append(m).Append(" -> ok=").Append(ok).Append(err.Length > 0 ? " EX " + err : "").Append("\n");
}
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    sb.Append("WIN ").Append(w.GetType().Name).Append(" ").Append(w.position).Append(" hasRoot=")
      .Append(w.rootVisualElement != null && w.rootVisualElement.panel != null).Append("\n");
return sb.ToString();
