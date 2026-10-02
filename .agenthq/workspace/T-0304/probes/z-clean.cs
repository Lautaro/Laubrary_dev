var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

// Shaper: back to the empty state it was found in, at the size it was found at.
UnityEditor.EditorWindow sw = null, pw = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w0 == null) continue;
    if (w0.GetType().Name == "ShaperWindow") sw = w0;
    if (w0.GetType().Name == "PyreWindow") pw = w0;
}
if (sw != null)
{
    System.Reflection.MethodInfo setAsset = null;
    for (var t = sw.GetType(); t != null && setAsset == null; t = t.BaseType) setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
    setAsset.Invoke(sw, new object[] { null });
    sw.position = new UnityEngine.Rect(60f, 20f, 1600f, 1150f);
    sb.Append("Shaper unbound, pos=").Append(sw.position.ToString()).Append("\n");
}
// Pyre was not open when this task started.
if (pw != null) { pw.Close(); sb.Append("Pyre closed\n"); }

// Scratch document, with its .meta.
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/AuditT0305.asset") != null)
    sb.Append("deleted scratch=").Append(UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper/AuditT0305.asset")).Append("\n");

// Prefs: this task's own keys go; Shaper's split pref had no stored value; its toggle bar goes back verbatim.
foreach (var k in new[] { "T0304.mask", "T0304.cmask", "T0304.node", "T0304.unit", "T0304.reset", "T0304.tweak", "T0304.w", "T0304.win", "T0304.timing", "T0304.shape" })
    UnityEditor.EditorPrefs.DeleteKey(k);
UnityEditor.EditorPrefs.DeleteKey("ZUI.Split.shaper.window.split.v1");
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
    "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
sb.Append("prefs restored; split pref now=").Append(UnityEditor.EditorPrefs.GetFloat("ZUI.Split.shaper.window.split.v1", -1f)).Append("\n");
sb.Append("bar=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel", "<none>")).Append("\n");

int dirty = 0;
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{
    if (o == null) continue;
    var path = UnityEditor.AssetDatabase.GetAssetPath(o);
    if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
    if (UnityEditor.EditorUtility.IsDirty(o)) { dirty++; sb.Append("DIRTY ").Append(path).Append("\n"); }
}
sb.Append("dirtyAssets=").Append(dirty).Append("\n");
foreach (var p in System.IO.Directory.GetFiles(UnityEngine.Application.dataPath + "/Pyre", "*.asset"))
    sb.Append("FILE Assets/Pyre/").Append(System.IO.Path.GetFileName(p)).Append(" len=").Append(new System.IO.FileInfo(p).Length)
      .Append(" mtime=").Append(System.IO.File.GetLastWriteTimeUtc(p).ToString("s")).Append("\n");
if (System.IO.Directory.Exists(UnityEngine.Application.dataPath + "/Shaper"))
    foreach (var p in System.IO.Directory.GetFiles(UnityEngine.Application.dataPath + "/Shaper"))
        sb.Append("SHAPERFILE ").Append(System.IO.Path.GetFileName(p)).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
sb.Append("console cleared; compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
return sb.ToString();
