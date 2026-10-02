// ZAudit + the package's own ZuiAudit (which now carries the clipped-input check) on one window,
// every section and box forced open first.
string wn = UnityEditor.EditorPrefs.GetString("T328.win", "PyreWindow");
var win = ZWin(wn); if (win == null) return "no window " + wn;
var zaT = ZType("ZuiAudit");
int opened = (int)zaT.GetMethod("ExpandAll").Invoke(null, new object[]{ win });
win.rootVisualElement.MarkDirtyRepaint(); win.Repaint();
var args = new object[]{ win, 0 };
var findings = zaT.GetMethod("Audit", new System.Type[]{ typeof(UnityEditor.EditorWindow), typeof(int).MakeByRefType() }).Invoke(null, args);
int folded = (int)args[1];
var list = findings as System.Collections.IEnumerable;
var byKind = new System.Collections.Generic.Dictionary<string,int>();
var lines = new System.Text.StringBuilder();
int total = 0;
foreach (var f in list)
{
    total++;
    string k = (string)f.GetType().GetField("check").GetValue(f);
    byKind[k] = byKind.ContainsKey(k) ? byKind[k]+1 : 1;
    lines.AppendLine(f.ToString());
}
string zr = ZAudit(win, wn);
string p = ZDump("audit-" + wn, zr + "\n\n=== ZuiAudit (package) opened=" + opened + " folded=" + folded + " total=" + total + " ===\n" + lines);
var parts = new System.Collections.Generic.List<string>();
foreach (var kv in byKind) parts.Add(kv.Key + "=" + kv.Value);
return wn + " | " + ZSummary(wn) + " || ZuiAudit expanded=" + opened + " foldedSkipped=" + folded
    + " findings=" + total + " [" + string.Join(", ", parts) + "] -> " + p;
