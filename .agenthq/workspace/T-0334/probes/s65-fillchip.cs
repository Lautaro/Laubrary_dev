// Press the section bar's "Fill" chip on a Composite node (which HAS no Fill section) and see what changes.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
string key = "ZuiSectionToggleBar.ShaperWindow.userSel";
sb.Append("pref BEFORE ").Append(UnityEditor.EditorPrefs.GetString(key,"<unset>")).Append("\n");
System.Func<int> sections = () => { var secT = ZType("ZuiSection"); int n=0;
  foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e) && ZDrawn(e)) n++; return n; };
sb.Append("drawn sections BEFORE=").Append(sections()).Append(" elements=").Append(ZAll(w.rootVisualElement).Count).Append("\n");
UnityEngine.UIElements.Button chip = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)||b.text!="Fill") continue;
  bool inBar=false; for (var p=b.hierarchy.parent;p!=null;p=p.hierarchy.parent) if (p.GetType().Name=="ZuiSectionToggleBar") { inBar=true; break; }
  if (inBar) { chip = b; break; } }
if (chip == null) return sb.Append("no Fill chip").ToString();
sb.Append(ZPress(w, chip)).Append("\n");
sb.Append("drawn sections AFTER=").Append(sections()).Append(" elements=").Append(ZAll(w.rootVisualElement).Count).Append("\n");
sb.Append("pref AFTER  ").Append(UnityEditor.EditorPrefs.GetString(key,"<unset>")).Append("\n");
return sb.ToString();
