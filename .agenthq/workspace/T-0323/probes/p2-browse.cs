var sb=new System.Text.StringBuilder();
string wn=UnityEditor.EditorPrefs.GetString("T323.auditWin","ShaperWindow");
var w=ZWin(wn); if (w==null) return "no "+wn;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Browse") { ZClick(b); sb.Append("browse toggled\n"); break; } }
return sb.ToString();
