// T-0334 cleanup, part 2: prefs, dirty flags, undo, console, and the window set this session started with.
// The output-path pref (T0312.out) is restored LAST, in z4, after the final probe — round 14's lesson.
var sb = new System.Text.StringBuilder();
System.Action<string> show = k => sb.Append("  ").Append(k).Append(" = ")
    .Append(UnityEditor.EditorPrefs.GetString(k, "<unset>")).Append("\n");
sb.Append("BEFORE:\n");
foreach (var k in new string[]{ "T320.capWin", "T320.capOut", "T334.win", "T334.find",
                                "ZuiSectionToggleBar.ShaperWindow.userSel",
                                "ZuiSectionToggleBar.ZoeWindow.userSel" }) show(k);

// Shaper's section selection, restored VERBATIM to what this session found.
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
    "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
UnityEditor.EditorPrefs.SetBool("ZuiSectionToggleBar.ShaperWindow.barMode", true);
// Zoe's was UNSET at session start (measured), so deleting it — not writing one — is the restore.
foreach (var k in new string[]{ "ZuiSectionToggleBar.ZoeWindow.userSel",
                                "ZuiSectionToggleBar.ZoeWindow.barMode",
                                "ZuiSectionToggleBar.ZoeWindow.solo",
                                "ZuiSectionToggleBar.ZoeWindow.preSolo" })
    UnityEditor.EditorPrefs.DeleteKey(k);

UnityEditor.EditorPrefs.SetString("T320.capWin", "ShaperWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/shots/cap.png");
UnityEditor.EditorPrefs.SetString("T334.win", "ShaperWindow");
foreach (var k in new string[]{ "T334.find", "T334.findNth", "T334.t0", "T334.rigOut",
                                "T334.newName", "T334.walkWin", "T334.menu", "T334.line",
                                "T334.sprite", "T334.scrollY", "T334.secWin", "T334.deskOut" })
    UnityEditor.EditorPrefs.DeleteKey(k);

// The demo document: git shows the FILE was never written, and its serialized json is byte-identical to
// what it was before the two stray presses (measured, and again after a ForceUpdate reimport), so the
// in-memory dirty flag is the only residue — cleared so nobody else's SaveAssets can ever flush it.
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
if (doc != null) { UnityEditor.EditorUtility.ClearDirty(doc); sb.Append("docDirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n"); }

int dirty = 0;
foreach (var o in Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{
    if (o == null) continue;
    string p = UnityEditor.AssetDatabase.GetAssetPath(o);
    if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets/")) continue;
    if (UnityEditor.EditorUtility.IsDirty(o)) { dirty++; sb.Append("  STILL DIRTY: ").Append(p).Append("\n"); }
}
sb.Append("dirty ScriptableObjects under Assets/: ").Append(dirty).Append("\n");

UnityEditor.Undo.ClearAll();
var logT = ZType("LogEntries");
if (logT != null) { var m = logT.GetMethod("Clear", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic); if (m != null) m.Invoke(null, null); }

sb.Append("AFTER:\n");
foreach (var k in new string[]{ "T320.capWin", "T320.capOut", "T334.win",
                                "ZuiSectionToggleBar.ShaperWindow.userSel",
                                "ZuiSectionToggleBar.ZoeWindow.userSel" }) show(k);
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty)
  .Append(" playing=").Append(UnityEditor.EditorApplication.isPlaying)
  .Append(" compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
return sb.ToString();
