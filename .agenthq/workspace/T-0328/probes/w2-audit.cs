// ZuiAudit + the four-audit ZAudit over a named set of windows, every section/box forced open first.
string names = UnityEditor.EditorPrefs.GetString("T328.wins", "");
var zaT = ZType("ZuiAudit");
var sb = new System.Text.StringBuilder();
var big = new System.Text.StringBuilder();
foreach (var n in names.Split('|'))
{
    if (string.IsNullOrEmpty(n)) continue;
    var w = ZWin(n);
    if (w == null) { sb.Append(n).Append(": NOT OPEN\n"); continue; }
    int opened = 0, folded = 0, total = 0;
    var byKind = new System.Collections.Generic.Dictionary<string,int>();
    var det = new System.Text.StringBuilder();
    try {
        opened = (int)zaT.GetMethod("ExpandAll").Invoke(null, new object[]{ w });
        w.rootVisualElement.MarkDirtyRepaint(); w.Repaint();
        var args = new object[]{ w, 0 };
        var f = zaT.GetMethod("Audit", new System.Type[]{ typeof(UnityEditor.EditorWindow), typeof(int).MakeByRefType() }).Invoke(null, args) as System.Collections.IEnumerable;
        foreach (var x in f) { total++; string k = (string)x.GetType().GetField("check").GetValue(x); byKind[k] = byKind.ContainsKey(k)?byKind[k]+1:1; det.Append("   ").Append(x.ToString()).Append("\n"); }
        folded = (int)args[1];
    } catch (System.Exception ex) { det.Append("   EXCEPTION ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append("\n"); }
    string rep = ZAudit(w, n);
    big.Append(rep).Append("\n=== ZuiAudit(").Append(n).Append(") expanded=").Append(opened).Append(" folded=").Append(folded).Append(" total=").Append(total).Append(" ===\n").Append(det).Append("\n\n");
    var parts = new System.Collections.Generic.List<string>();
    foreach (var kv in byKind) parts.Add(kv.Key + "=" + kv.Value);
    sb.Append(n).Append(": size=").Append(w.position.width.ToString("F0")).Append("x").Append(w.position.height.ToString("F0"))
      .Append(" elements=").Append(ZCount["elements"]).Append(" drawn=").Append(ZCount["drawn"]).Append(" controls=").Append(ZCount["controls"])
      .Append(" captionShort=").Append(ZCount["captionShort"]).Append(" overflowX=").Append(ZCount["overflowParentX"])
      .Append(" offWindow=").Append(ZCount["overflowWindow"]).Append(" noTooltip=").Append(ZCount["noTooltip"])
      .Append(" inertNoReason=").Append(ZCount["inertNoReason"])
      .Append(" || ZuiAudit=").Append(total).Append(" [").Append(string.Join(", ", parts.ToArray())).Append("] folded=").Append(folded).Append("\n");
}
string p = ZDump("audit-" + UnityEditor.EditorPrefs.GetString("T328.tag","batch"), big.ToString());
return sb.ToString() + "-> " + p;
