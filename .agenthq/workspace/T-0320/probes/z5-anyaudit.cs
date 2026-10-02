string wn = UnityEditor.EditorPrefs.GetString("T320.auditWin","ChunkWindow");
var win = ZWin(wn); if (win == null) return wn + ": not open";
var rep = ZAudit(win, wn);
ZDump("audit-t320-" + wn + "-" + UnityEditor.EditorPrefs.GetString("T320.auditW","820") + ".txt", rep);
return ZSummary(wn);
