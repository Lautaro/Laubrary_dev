var sb=new System.Text.StringBuilder();
string wn = UnityEditor.EditorPrefs.GetString("T323.auditWin","LatheWindow");
var w = ZWin(wn); if (w==null) return "no "+wn;
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo sa=null, rb=null;
for (var t=w.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
sa.Invoke(w, new object[]{ null }); rb.Invoke(w,null); w.Repaint();
UnityEngine.UIElements.Button newBtn=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="New") newBtn=b; }
sb.Append("newBtn=").Append(newBtn!=null).Append("\n");
if (newBtn!=null) ZClick(newBtn);
return sb.ToString();
