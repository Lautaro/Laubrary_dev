// A SEPARATE round trip from the rebuild — a rebuilt card's layout does not resolve until the next frame,
// so a ZDrawn check in the same eval sees NaN geometry and reports every control as "not drawn".
var w = ZWin("ShaperWindow"); if (w==null) return "no window";
var sb=new System.Text.StringBuilder();
foreach (var want in UnityEditor.EditorPrefs.GetString("T337.watch","").Split(';')) {
  if(want.Length==0) continue; bool found=false;
  foreach (var e in ZAll(w.rootVisualElement)) {
    if(ZCaption(e)!=want || !ZDrawn(e) || e is UnityEngine.UIElements.Label) continue;
    found=true;
    sb.Append(want.PadRight(14)).Append(e.enabledInHierarchy ? "LIVE   " : "GREYED ")
      .Append(e.enabledInHierarchy ? "" : ZTip(e)).Append('\n'); break; }
  if(!found) sb.Append(want.PadRight(14)).Append("<not drawn>\n"); }
return sb.ToString();
