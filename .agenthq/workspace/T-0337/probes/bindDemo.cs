var w = ZWin("ShaperWindow"); if (w==null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo setAsset=null; System.Reflection.FieldInfo assetF=null;
for(var t=w.GetType();t!=null;t=t.BaseType){ if(setAsset==null) setAsset=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if(assetF==null) assetF=t.GetField("asset",BFi); }
string p=null;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("ShaperDemoDoc")) { var q=UnityEditor.AssetDatabase.GUIDToAssetPath(g); if(q.EndsWith(".asset")) p=q; }
if (p==null) return "ShaperDemoDoc not found";
var doc = UnityEditor.AssetDatabase.LoadMainAssetAtPath(p);
setAsset.Invoke(w, new object[]{ doc });
var a = assetF.GetValue(w) as UnityEngine.Object;
return "bound " + (a==null?"<null>":a.name) + " path=" + p + " dirty=" + UnityEditor.EditorUtility.IsDirty(a)
     + " sceneDirty=" + UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty;
