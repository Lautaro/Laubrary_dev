var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
System.Func<string> st = () => { var d=assetF.GetValue(w); var ls=d.GetType().GetField("layers",BFi).GetValue(d) as System.Collections.IList;
  var h=ls[0].GetType().GetField("height",BFi).GetValue(ls[0]); return h==null?"<null>":h.GetType().Name; };
string want = UnityEditor.EditorPrefs.GetString("T334.pressText","Height");
UnityEngine.UIElements.Button btn=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text==want) { btn=b; break; } }
if (btn==null) return "no '" + want + "' button";
var sb=new System.Text.StringBuilder(); sb.Append("BEFORE height=").Append(st()).Append("\n");
sb.Append(ZPress(w, btn)).Append("\n");
sb.Append("AFTER  height=").Append(st()).Append("\n");
return sb.ToString();
