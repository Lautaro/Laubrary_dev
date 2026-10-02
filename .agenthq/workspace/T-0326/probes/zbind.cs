// ZBind(winTypeName, assetPath) — bind a ZuiAssetWindow to an asset via its SetAsset + Rebuild.
System.Func<string,string,string> ZBind = (winName, path) => {
  var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
  var w = ZWin(winName); if (w==null) return winName+": no window";
  System.Reflection.MethodInfo sa=null, rb=null; System.Reflection.FieldInfo af=null;
  for (var t=w.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (af==null) af=t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
  if (sa==null) return winName+": no SetAsset";
  var ty = sa.GetParameters()[0].ParameterType;
  var obj = UnityEditor.AssetDatabase.LoadAssetAtPath(path, ty);
  if (obj==null) return winName+": asset not found "+path;
  sa.Invoke(w, new object[]{ obj }); if (rb!=null) rb.Invoke(w,null); w.Repaint();
  return winName+" -> "+path;
};
// ZOpen(typeName) — open a window by its static Open()/ShowWindow(), return it.
System.Func<string,UnityEditor.EditorWindow> ZOpen = n => {
  var w = ZWin(n); if (w!=null) return w;
  var t = ZType(n); if (t==null) return null;
  var BFs = System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
  foreach (var m in t.GetMethods(BFs)) if (m.GetParameters().Length==0 && (m.Name=="Open"||m.Name=="ShowWindow"||m.Name=="Show"||m.Name.StartsWith("Open"))) { m.Invoke(null,null); break; }
  return ZWin(n);
};
