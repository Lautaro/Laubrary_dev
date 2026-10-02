var SB = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var WT = typeof(Laubrary.Shaper.Editor.ShaperWindow);
string DIR = "Assets/Shaper/AuditA21x";
if (!UnityEditor.AssetDatabase.IsValidFolder(DIR)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "AuditA21x");
string senPath = DIR + "/Sentinel.asset";
UnityEditor.AssetDatabase.DeleteAsset(senPath);
var sen = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
sen.canvasWidth=32; sen.canvasHeight=32; sen.frameCount=2;
UnityEditor.AssetDatabase.CreateAsset(sen, senPath);
UnityEditor.AssetDatabase.SaveAssetIfDirty(sen);
sen.canvasWidth = 199; UnityEditor.EditorUtility.SetDirty(sen);
SB.Append("sentinel: width=").Append(sen.canvasWidth).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(sen)).Append('\n');
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Reflection.PropertyInfo curP=null; for (var t=WT;t!=null;t=t.BaseType) { curP=t.GetProperty("Current",BFi); if (curP!=null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
SB.Append("bound=").Append(doc==null?"<none>":doc.name).Append('\n');
System.Action<UnityEngine.UIElements.VisualElement,System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> W=null;
W=(e,l)=>{ if(e==null) return; l.Add(e); for(int i=0;i<e.childCount;i++) W(e[i],l); };
var all=new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); W(win.rootVisualElement,all);
UnityEngine.UIElements.Button dupBtn=null; foreach (var v in all) if (v is UnityEngine.UIElements.Button b && b.text=="Duplicate") dupBtn=b;
using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target=dupBtn; dupBtn.SendEvent(ev); }
SB.Append("after Duplicate: sentinel width=").Append(sen.canvasWidth).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(sen)).Append(" now bound=").Append((curP.GetValue(win) as Laubrary.Shaper.ShaperDocument)?.name).Append('\n');
UnityEditor.EditorPrefs.SetString("A21.dup", UnityEditor.AssetDatabase.GetAssetPath(curP.GetValue(win) as UnityEngine.Object));
return SB.ToString();
