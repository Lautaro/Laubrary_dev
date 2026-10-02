// T-0313 — open any Laubrary window and put it in its FULLEST state, so the shared audit can see all of
// it.  Prefs: T313.win (window type short name), T313.menu (its menu item), T313.width, T313.bind
// ("auto" = bind the first existing asset of the window's own asset type — never creates one, never
// saves; "-" = leave it on its empty state).  Sections hidden by a toggle bar are forced visible by
// display, and every fold is opened through ZuiAudit.ExpandAll (the toolkit's own helper).
string wname = UnityEditor.EditorPrefs.GetString("T313.win", "ChunkWindow");
string menu = UnityEditor.EditorPrefs.GetString("T313.menu", "Laubrary/Chunks");
float width = float.Parse(UnityEditor.EditorPrefs.GetString("T313.width", "1400"));
string bind = UnityEditor.EditorPrefs.GetString("T313.bind", "auto");
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

var win = ZWin(wname);
if (win == null) { UnityEditor.EditorApplication.ExecuteMenuItem(menu); win = ZWin(wname); }
if (win == null) return "NO WINDOW " + wname + " (menu " + menu + ")";
win.position = new UnityEngine.Rect(5, 20, width, 1000);
win.Show();

string bound = "<none>";
if (bind != "-")
{
    System.Reflection.FieldInfo af = null;
    for (var t = win.GetType(); t != null && af == null; t = t.BaseType) af = t.GetField("asset", BFi);
    if (af != null)
    {
        var at = af.FieldType;
        string pick = bind == "auto" ? null : bind;
        if (pick == null)
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:" + at.Name);
            if (guids.Length > 0) pick = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
        }
        if (pick != null)
        {
            var obj = UnityEditor.AssetDatabase.LoadAssetAtPath(pick, at);
            if (obj != null)
            {
                System.Reflection.MethodInfo setAsset = null;
                for (var t = win.GetType(); t != null && setAsset == null; t = t.BaseType)
                    setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
                if (setAsset != null) { setAsset.Invoke(win, new object[] { obj }); bound = pick; }
                else { af.SetValue(win, obj); bound = pick + " (field set)"; }
            }
        }
        sb.Append("assetType=").Append(at.Name).Append("\n");
    }
    else sb.Append("no `asset` field — not an asset window\n");
}

System.Reflection.MethodInfo rebuild = null;
for (var t = win.GetType(); t != null && rebuild == null; t = t.BaseType)
    rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
if (rebuild != null) rebuild.Invoke(win, null);

// open every fold through the toolkit's own helper
int opened = -1;
var auditT = ZType("ZuiAudit");
if (auditT != null)
{
    var m = auditT.GetMethod("ExpandAll", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
    if (m != null) opened = (int)m.Invoke(null, new object[] { win });
}
// and make every section a toggle bar has hidden visible, so "every section" means every section
int shown = 0;
foreach (var e in ZAll(win.rootVisualElement))
    if (e.GetType().Name == "ZuiSection" && e.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None)
    { e.style.display = UnityEngine.UIElements.DisplayStyle.Flex; shown++; }
win.Repaint();

int sections = 0, boxes = 0, imgui = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    var n = e.GetType().Name;
    if (n == "ZuiSection") sections++;
    else if (n == "ZuiBox") boxes++;
    else if (e is UnityEngine.UIElements.IMGUIContainer) imgui++;
}
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath)
  .Append(" win=").Append(wname).Append(" ").Append(win.position.width.ToString("F0")).Append("x").Append(win.position.height.ToString("F0"))
  .Append(" bound=").Append(bound)
  .Append(" foldsOpened=").Append(opened).Append(" sectionsUnhidden=").Append(shown)
  .Append(" sections=").Append(sections).Append(" boxes=").Append(boxes).Append(" imguiContainers=").Append(imgui)
  .Append(" elements=").Append(ZAll(win.rootVisualElement).Count).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return sb.ToString();
