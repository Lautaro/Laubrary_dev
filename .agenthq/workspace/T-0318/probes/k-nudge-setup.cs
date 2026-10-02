// T-0318 — a scratch COPY of the demo document, so the arrow-key nudge test never touches a committed asset.
const string dir = "Assets/Shaper/AuditT0318";
const string dst = dir + "/NudgeDoc.asset";
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "AuditT0318");
if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(dst) == null)
    UnityEditor.AssetDatabase.CopyAsset("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset", dst);
UnityEditor.AssetDatabase.Refresh();
var doc = UnityEditor.AssetDatabase.LoadMainAssetAtPath(dst);
if (doc == null) return "COPY FAILED";
var win = ZWin("ShaperWindow"); if (win == null) return "NO SHAPER";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo sa = null;
for (var t = win.GetType(); t != null && sa == null; t = t.BaseType) sa = t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
sa.Invoke(win, new object[]{ doc });
System.Reflection.FieldInfo sel = null;
for (var t = win.GetType(); t != null && sel == null; t = t.BaseType) sel = t.GetField("selectedLayer", BFi);
if (sel != null) sel.SetValue(win, 0);
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null && rb == null; t = t.BaseType) rb = t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly);
rb.Invoke(win, null); win.Repaint();
return "bound scratch copy " + dst + " dirty=" + UnityEditor.EditorUtility.IsDirty(doc);
