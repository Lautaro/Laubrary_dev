// Every OPEN Laubrary window: audit + the clipped-input census in one pass.
var sb = new System.Text.StringBuilder();
var zaT = ZType("ZuiAudit");
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string n = w.GetType().Name;
    if (System.Array.IndexOf(unityOwn, n) >= 0) continue;
    zaT.GetMethod("ExpandAll").Invoke(null, new object[]{ w });
    var args = new object[]{ w, 0 };
    var f = zaT.GetMethod("Audit", new System.Type[]{ typeof(UnityEditor.EditorWindow), typeof(int).MakeByRefType() }).Invoke(null, args) as System.Collections.IEnumerable;
    int total = 0, clipped = 0; var det = new System.Text.StringBuilder();
    foreach (var x in f) { total++; string k = (string)x.GetType().GetField("check").GetValue(x); if (k == "clipped-input") { clipped++; det.Append("      ").Append(x.ToString()).Append("\n"); } }
    ZAudit(w, n);
    sb.Append(n).Append(": elements=").Append(ZCount["elements"]).Append(" drawn=").Append(ZCount["drawn"])
      .Append(" controls=").Append(ZCount["controls"])
      .Append(" captionShort=").Append(ZCount["captionShort"]).Append(" overflowX=").Append(ZCount["overflowParentX"])
      .Append(" offWindow=").Append(ZCount["overflowWindow"]).Append(" noTooltip=").Append(ZCount["noTooltip"])
      .Append(" inertNoReason=").Append(ZCount["inertNoReason"])
      .Append(" | ZuiAudit findings=").Append(total).Append(" of which clipped-input=").Append(clipped)
      .Append(" foldedSkipped=").Append((int)args[1]).Append("\n").Append(det);
}
return sb.ToString();
