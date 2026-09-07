var sb = new System.Text.StringBuilder();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w.titleContent.text != "PyreProbeWin" && w.titleContent.text != "ZoeProbeWin") continue;
    Laubrary.Zui.ZuiAudit.ExpandAll(w);
    var findings = Laubrary.Zui.ZuiAudit.Audit(w, out int folded);
    sb.AppendLine(w.titleContent.text + ": findings=" + findings.Count + " foldedSkipped=" + folded);
    foreach (var f in findings) sb.AppendLine("   " + f);
}
return sb.ToString();
