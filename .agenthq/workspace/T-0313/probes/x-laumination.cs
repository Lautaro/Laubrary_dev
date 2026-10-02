// T-0313 — the Laumination Builder in its REAL state.  Its menu item opens an empty shell; the way an
// author actually reaches it is LauminationBuilderWindow.OpenForEdit(lauminary, animName) from the
// Lauminary Browser, so the audit goes in the same door.  The draft version asset beside each lauminary
// is READ (LoadAssetAtPath) to find a real laumination name — never LauminaryRepo.EnsureDraft, which
// creates.  Nothing is created and nothing is saved.
float width = float.Parse(UnityEditor.EditorPrefs.GetString("T313.width", "1400"));
var sb = new System.Text.StringBuilder();
var wt = ZType("LauminationBuilderWindow");
if (wt == null) return "NO LauminationBuilderWindow TYPE";
var lauT = ZType("Lauminary");
var verT = ZType("LauminaryVersion");
if (lauT == null || verT == null) return "NO Lauminary/LauminaryVersion TYPE";
string want = UnityEditor.EditorPrefs.GetString("T313.lauminary", "Hero");

UnityEngine.Object pick = null; string animName = null; int scanned = 0;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:" + lauT.Name))
{
    var ap = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var o = UnityEditor.AssetDatabase.LoadAssetAtPath(ap, lauT);
    if (o == null) continue;
    scanned++;
    if (want != "-" && ap.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
    var folder = System.IO.Path.GetDirectoryName(ap).Replace('\\', '/') + "/draft";
    if (!System.IO.Directory.Exists(folder)) continue;
    foreach (var fp in System.IO.Directory.GetFiles(folder, "*.asset"))
    {
        var vp = fp.Replace('\\', '/');
        var ver = UnityEditor.AssetDatabase.LoadAssetAtPath(vp, verT);
        if (ver == null) continue;
        var af = verT.GetField("animations", System.Reflection.BindingFlags.Instance
               | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var list = af == null ? null : af.GetValue(ver) as System.Collections.IList;
        if (list == null || list.Count == 0 || list[0] == null) continue;
        var nf = list[0].GetType().GetField("name", System.Reflection.BindingFlags.Instance
               | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var nm = nf == null ? null : nf.GetValue(list[0]) as string;
        if (string.IsNullOrEmpty(nm)) continue;
        pick = o; animName = nm;
        sb.Append("lauminary=").Append(ap).Append(" draft=").Append(vp).Append(" laumination='").Append(nm).Append("'\n");
        break;
    }
    if (pick != null) break;
}
sb.Append("lauminariesScanned=").Append(scanned).Append("\n");
if (pick == null) return sb.Append("NO draft laumination found — builder left on its empty shell\n").ToString();

var open = wt.GetMethod("OpenForEdit", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
if (open == null) return sb.Append("NO OpenForEdit\n").ToString();
open.Invoke(null, new object[] { pick, animName });
var win = ZWin("LauminationBuilderWindow");
if (win == null) return sb.Append("window did not open\n").ToString();
win.position = new UnityEngine.Rect(5, 20, width, 1000);
win.Show(); win.Repaint();

int opened = -1;
var auditT = ZType("ZuiAudit");
if (auditT != null)
{
    var m = auditT.GetMethod("ExpandAll", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
    if (m != null) opened = (int)m.Invoke(null, new object[] { win });
}
int shown = 0;
foreach (var e in ZAll(win.rootVisualElement))
    if (e.GetType().Name == "ZuiSection" && e.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None)
    { e.style.display = UnityEngine.UIElements.DisplayStyle.Flex; shown++; }
win.Repaint();

sb.Append("dataPath=").Append(UnityEngine.Application.dataPath)
  .Append(" window=").Append(win.position.width.ToString("F0"))
  .Append(" foldsOpened=").Append(opened).Append(" sectionsUnhidden=").Append(shown)
  .Append(" elements=").Append(ZAll(win.rootVisualElement).Count).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return sb.ToString();
