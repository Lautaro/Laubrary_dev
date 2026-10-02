// Raise the named guard on the bound generator through the DOCUMENT (the same value the control writes),
// rebuild the card, and report the dials that were greyed a moment ago.
var w = ZWin("ShaperWindow"); if (w==null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for(var t=w.GetType();t!=null;t=t.BaseType) if(assetF==null) assetF=t.GetField("asset",BFi);
var d = assetF.GetValue(w) as UnityEngine.ScriptableObject; if(d==null) return "no asset";
// find the hosted form
object form=null;
var l = d.GetType().GetField("layers",BFi).GetValue(d) as System.Collections.IList;
var root = l[0].GetType().GetField("root",BFi).GetValue(l[0]);
var comp = root.GetType().GetField("composite",BFi).GetValue(root);
if (comp != null) foreach (var g in comp.GetType().GetFields(BFi)) {
  var v = g.GetValue(comp); if (v == null) continue;
  var ff = v.GetType().GetField("form", BFi);
  if (ff != null) { var fm = ff.GetValue(v); if (fm != null) { form = fm; break; } } }
if(form==null) return "no hosted form on the bound document";
var sb=new System.Text.StringBuilder(); sb.Append("form=").Append(form.GetType().Name).Append('\n');
string spec = UnityEditor.EditorPrefs.GetString("T337.raise","");   // "field=value;field=value"
foreach (var part in spec.Split(';')) { if(part.Length==0) continue; var kv=part.Split('=');
  var f = form.GetType().GetField(kv[0], BFi); if(f==null){ sb.Append("  no field ").Append(kv[0]).Append('\n'); continue; }
  var cur = f.GetValue(form);
  if (cur!=null && cur.GetType().Name=="ZUIValue") { var svP=cur.GetType().GetProperty("staticValue",BFi); svP.SetValue(cur, float.Parse(kv[1])); }
  else if (f.FieldType==typeof(int)) f.SetValue(form, int.Parse(kv[1]));
  else if (f.FieldType==typeof(float)) f.SetValue(form, float.Parse(kv[1]));
  else if (f.FieldType.IsEnum) f.SetValue(form, System.Enum.Parse(f.FieldType, kv[1]));
  sb.Append("  raised ").Append(kv[0]).Append(" -> ").Append(kv[1]).Append('\n'); }
UnityEditor.EditorUtility.SetDirty(d);
// rebuild the card the way an edit does
System.Reflection.MethodInfo rebuild=null; for(var t=w.GetType();t!=null;t=t.BaseType) if(rebuild==null) rebuild=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly);
if(rebuild!=null) rebuild.Invoke(w,null); else { var m=w.GetType().GetMethod("BuildUI",BFi); if(m!=null) m.Invoke(w,null); }
w.Repaint();
sb.Append("rebuilt=").Append(rebuild!=null).Append('\n');
string[] wants = UnityEditor.EditorPrefs.GetString("T337.watch","").Split(';');
foreach (var want in wants){ if(want.Length==0) continue; bool found=false;
  foreach (var e in ZAll(w.rootVisualElement)) { if(ZCaption(e)!=want || !ZDrawn(e)) continue;
    if(!(e is UnityEngine.UIElements.Label)){ found=true;
      sb.Append("  ").Append(want).Append(": enabled=").Append(e.enabledInHierarchy).Append("  tip=").Append(ZTip(e)).Append('\n'); break; } }
  if(!found) sb.Append("  ").Append(want).Append(": <not drawn>\n"); }
return sb.ToString();
