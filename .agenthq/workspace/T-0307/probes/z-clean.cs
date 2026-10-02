// Puts the editor back: every scratch asset this pass (and the archived probes it re-ran) created is
// deleted with its .meta, every T0307 pref and SessionState key is removed, the section toggle bar is
// restored verbatim to its session-start value, the split prefs are dropped, Undo is cleared, the tool
// windows are closed and the console is cleared.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

// close the tool windows first so nothing holds a reference to a deleted asset
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
foreach (var n in new[] { "ShaperWindow", "PyreWindow", "ChunkWindow", "LauminationBuilderWindow" })
{
    var t = FT(n); if (t == null) continue;
    foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
        if (w != null && w.GetType() == t) { w.Close(); sb.Append("closed ").Append(n).Append("\n"); }
}

// Assets/Shaper — keep only the two pre-existing scratch documents
var keep = new System.Collections.Generic.HashSet<string>
{ "Assets/Shaper/New Shaper.asset", "Assets/Shaper/New Shaper 1.asset" };
foreach (var e in System.IO.Directory.GetFileSystemEntries("Assets/Shaper"))
{
    var p = e.Replace('\\', '/');
    if (p.EndsWith(".meta")) continue;
    if (keep.Contains(p)) continue;
    if (UnityEditor.AssetDatabase.DeleteAsset(p)) sb.Append("deleted ").Append(p).Append("\n");
    else sb.Append("FAILED to delete ").Append(p).Append("\n");
}

// prefs
foreach (var k in new[] { "T0307.unit", "T0307.winw", "T0307.vstep", "T0307.vname", "T0307.w",
                          "T0304.w", "T0304.unit", "T0304.node", "T0304.cmask", "T0304.reset", "T0304.mask",
                          "ShaperCap.savedSel", "ShaperCap.name", "Shaper.lastView",
                          "ZUI.Split.shaper.window.split.v1", "ZUI.Split.pyre.window.split.v1",
                          "A25.sw", "A25.sh" })
    if (UnityEditor.EditorPrefs.HasKey(k)) { UnityEditor.EditorPrefs.DeleteKey(k); sb.Append("pref deleted ").Append(k).Append("\n"); }
UnityEditor.SessionState.EraseString("T0307.play");
UnityEditor.SessionState.EraseString("A25.samples");

// the section toggle bar, restored verbatim to the value a0-base recorded at session start
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
    "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
UnityEditor.EditorPrefs.SetBool("ZuiSectionToggleBar.ShaperWindow.barMode", true);
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.solo");
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.preSolo");

UnityEditor.Undo.ClearAll();
UnityEditor.AssetDatabase.Refresh();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
sb.Append("toggle bar restored, Undo cleared, console cleared\n");
return sb.ToString();
