var sb=new System.Text.StringBuilder();
string wn=UnityEditor.EditorPrefs.GetString("T323.pressWin","");
string bt=UnityEditor.EditorPrefs.GetString("T323.pressText","");
int nth=UnityEditor.EditorPrefs.GetInt("T323.pressNth",0);
var w=ZWin(wn); if (w==null) return "no window "+wn;
var b=ZFindBtn(w,bt,nth); if (b==null) return "NO BUTTON '"+bt+"'";
UnityEngine.UIElements.ScrollView sv=null;
for (var p=b.hierarchy.parent; p!=null; p=p.hierarchy.parent) { var s=p as UnityEngine.UIElements.ScrollView; if (s!=null) { sv=s; break; } }
if (sv==null) return "no scrollview for '"+bt+"' (rect "+b.worldBound+")";
sv.ScrollTo(b);
sb.Append("scrolled, offset=").Append(sv.scrollOffset).Append(" btn=").Append(b.worldBound).Append("\n");
return sb.ToString();
