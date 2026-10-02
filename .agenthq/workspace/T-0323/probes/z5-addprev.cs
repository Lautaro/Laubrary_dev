var sb=new System.Text.StringBuilder();
var w=ZWin("MirageWindow");
int before=0; foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null) before++;
UnityEngine.UIElements.Button b=null;
foreach (var e in ZAll(w.rootVisualElement)) { var t=e as UnityEngine.UIElements.Button; if (t!=null && ZDrawn(t) && t.text==UnityEditor.EditorPrefs.GetString("T323.pressText","Add Previewable")) b=t; }
if (b==null) return "no button";
ZClick(b);
var names=new System.Text.StringBuilder(); int after=0;
foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null) { after++; names.Append(x.GetType().Name).Append(" "); }
sb.Append("windows ").Append(before).Append(" -> ").Append(after).Append("\n").Append(names).Append("\n");
return sb.ToString();
