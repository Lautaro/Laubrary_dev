// T-0318 — T-0317's proposed two-state inert rule, actually run.  Samples the Lauminary Browser with
// nothing selected (its four action controls disabled) and with a lauminary selected (enabled), and flags
// any control whose tooltip is IDENTICAL across the flip — the signature of "describes the effect, not
// the reason".  Selection is set through the window's own selection field; nothing is created or saved.
var win = ZWin("LauminaryBrowserWindow");
if (win == null) { UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Lauminary Browser"); win = ZWin("LauminaryBrowserWindow"); }
if (win == null) return "NO BROWSER";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
// find the selection field and a lauminary to put in it
System.Reflection.FieldInfo selF = null;
foreach (var f in win.GetType().GetFields(BFi))
    if (f.FieldType.Name == "Lauminary" || (f.Name.ToLower().Contains("selected") && !f.FieldType.IsValueType)) { selF = f; break; }
sb.Append("selectionField=").Append(selF == null ? "<none>" : selF.Name + " : " + selF.FieldType.Name).Append("\n");
var lauT = ZType("Lauminary");
UnityEngine.Object pick = null;
if (lauT != null)
    foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:" + lauT.Name))
    { pick = UnityEditor.AssetDatabase.LoadAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(g), lauT); if (pick != null) break; }
sb.Append("pick=").Append(pick == null ? "<none>" : pick.name).Append("\n");
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null && rb == null; t = t.BaseType) rb = t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly);
if (selF != null) selF.SetValue(win, null);
if (rb != null) rb.Invoke(win, null);
UnityEditor.EditorPrefs.SetString("T318.sampleA", ZTipSample(win, "A"));
if (selF != null && pick != null) selF.SetValue(win, pick);
if (rb != null) rb.Invoke(win, null);
UnityEditor.EditorPrefs.SetString("T318.sampleB", ZTipSample(win, "B"));
sb.Append("sampleA lines=").Append(UnityEditor.EditorPrefs.GetString("T318.sampleA","").Split('\n').Length)
  .Append(" sampleB lines=").Append(UnityEditor.EditorPrefs.GetString("T318.sampleB","").Split('\n').Length).Append("\n");
ZDump("twostate-A.txt", UnityEditor.EditorPrefs.GetString("T318.sampleA",""));
ZDump("twostate-B.txt", UnityEditor.EditorPrefs.GetString("T318.sampleB",""));
if (selF != null) selF.SetValue(win, null);
if (rb != null) rb.Invoke(win, null);
return sb.ToString();
