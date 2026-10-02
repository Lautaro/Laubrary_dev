// T-0313 — the cold walk.  One eval per step, driven by EditorPrefs "T313.step":
//   close   close every Shaper/Pyre window so the next open is a genuinely cold one (`asset` is a
//           [SerializeField] on the window, so a CLOSED window reopens on the browser — the empty state)
//   open    open Shaper from its real menu item, clear the console, and sample the tree IMMEDIATELY
//           (t0: what the first frame after the click contains)
//   sample  sample the tree again, tagged by "T313.t" — the temporal series
//   bind    bind the committed demo document through SetAsset (the browser card's own code path)
// Every sample reports: elements/drawn/controls, how many are laid out, console errors since the open,
// and the identity of the first 8 drawn top-level units, so an empty screen is visible AS an empty screen.
string step = UnityEditor.EditorPrefs.GetString("T313.step", "open");
string tsec = UnityEditor.EditorPrefs.GetString("T313.t", "0");
var sb = new System.Text.StringBuilder();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
System.Func<string> Counts = () =>
{
    int e = 0, w = 0, l = 0;
    var args = new object[] { e, w, l };
    lt.GetMethod("GetCountsByType", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
      .Invoke(null, args);
    return "errors=" + args[0] + " warnings=" + args[1];
};

if (step == "close")
{
    foreach (var n in new[] { "ShaperWindow", "PyreWindow" })
    {
        var t = ZType(n); if (t == null) continue;
        foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
            if (w != null && w.GetType() == t) { w.Close(); sb.Append("closed ").Append(n).Append("\n"); }
    }
    lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
    return sb.Append("console cleared\n").ToString();
}

if (step == "open")
{
    lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
    UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
}

var win = ZWin("ShaperWindow");
if (win == null) return "NO SHAPER WINDOW after step " + step;

if (step == "bind")
{
    var doc = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
    if (doc == null) return "NO DOCUMENT";
    var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
    System.Reflection.MethodInfo setAsset = null;
    for (var t = win.GetType(); t != null && setAsset == null; t = t.BaseType)
        setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
    setAsset.Invoke(win, new object[] { doc });
    win.Repaint();
    sb.Append("bound=").Append(doc.name).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
}

var all = ZAll(win.rootVisualElement);
int drawn = 0, ctrl = 0, laidOut = 0, noTip = 0;
foreach (var e in all)
{
    if (ZLaidOut(e)) laidOut++;
    if (!ZDrawn(e)) continue;
    drawn++;
    if (ZIsCtrl(e)) { ctrl++; if (string.IsNullOrEmpty(ZTip(e))) noTip++; }
}
sb.Append("step=").Append(step).Append(" t=").Append(tsec).Append("s")
  .Append(" window=").Append(win.position.width.ToString("F0")).Append("x").Append(win.position.height.ToString("F0"))
  .Append(" elements=").Append(all.Count).Append(" laidOut=").Append(laidOut)
  .Append(" drawn=").Append(drawn).Append(" controls=").Append(ctrl).Append(" noTooltip=").Append(noTip)
  .Append(" ").Append(Counts()).Append("\n");

// what is actually ON the screen, named — an empty state has to be visible as one
int shown = 0;
foreach (var e in all)
{
    if (!ZDrawn(e)) continue;
    var tn = e.GetType().Name;
    if (tn != "ZuiSection" && tn != "ZuiBox" && !(e is UnityEngine.UIElements.Button) && tn != "IMGUIContainer") continue;
    string cap = ZCaption(e);
    if (string.IsNullOrEmpty(cap)) cap = "<" + tn + ">";
    sb.Append("   on screen: ").Append(tn).Append(" '").Append(cap.Length > 40 ? cap.Substring(0, 40) : cap).Append("'\n");
    if (++shown >= 14) break;
}
return sb.ToString();
