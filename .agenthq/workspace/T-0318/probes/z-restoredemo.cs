var doc = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
for (int i = 0; i < 8; i++) UnityEditor.Undo.PerformUndo();
UnityEditor.Undo.FlushUndoRecordObjects();
var win = ZWin("ShaperWindow");
var BFa = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo rb = null;
if (win != null) for (var t = win.GetType(); t != null && rb == null; t = t.BaseType) rb = t.GetMethod("Rebuild", BFa|System.Reflection.BindingFlags.DeclaredOnly);
if (rb != null) rb.Invoke(win, null);
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement))
    if (e.GetType().Name == "ZuiMicroMinMax")
        sb.Append("Lifetime low=").Append(e.GetType().GetProperty("low", BFa).GetValue(e))
          .Append(" high=").Append(e.GetType().GetProperty("high", BFa).GetValue(e)).Append("\n");
UnityEditor.Undo.ClearAll();
UnityEditor.EditorUtility.ClearDirty(doc);
sb.Append("dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
return sb.ToString();
