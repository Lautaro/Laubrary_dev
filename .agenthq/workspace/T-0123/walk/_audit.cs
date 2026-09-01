var wt = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == wt);
if (win == null) return "NO WINDOW";
var at = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Laubrary.Zui.ZuiAudit")).FirstOrDefault(x => x != null);
if (at == null) return "NO ZuiAudit TYPE";
var expand = at.GetMethod("ExpandAll");
int expanded = (int)expand.Invoke(null, new object[] { win });
win.Repaint();
var mi = at.GetMethods().FirstOrDefault(m => m.Name == "Audit" && m.GetParameters().Length == 2);
object[] args = new object[] { win, 0 };
var list = mi.Invoke(null, args) as System.Collections.IEnumerable;
var sb = new System.Text.StringBuilder();
sb.AppendLine("expanded=" + expanded + " foldedSkipped=" + args[1]);
int n = 0;
foreach (var f in list) { n++; sb.AppendLine("  " + f.ToString()); }
sb.AppendLine("findings=" + n);
return sb.ToString();
