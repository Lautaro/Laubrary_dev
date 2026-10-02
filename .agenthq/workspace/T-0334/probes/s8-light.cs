var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var a = assetF.GetValue(w);
var rigF = a.GetType().GetField("lightRig", BFi); var rig = rigF.GetValue(a);
var lightsF = rig.GetType().GetField("lights", BFi); var lights = lightsF.GetValue(rig) as System.Collections.ICollection;
var sb = new System.Text.StringBuilder();
sb.Append("lights BEFORE=").Append(lights.Count).Append("\n");
UnityEngine.UIElements.Button add = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == "+ Add light") { add = b; break; } }
if (add == null) return sb.Append("no + Add light button").ToString();
var wr = w.rootVisualElement.worldBound;
bool onScreen = add.worldBound.yMax <= wr.yMax && add.worldBound.yMin >= wr.yMin;
sb.Append("button ").Append(add.worldBound).Append(" onScreen=").Append(onScreen).Append("\n");
if (!onScreen) { UnityEngine.UIElements.ScrollView sv=null; for (var p=add.hierarchy.parent;p!=null;p=p.hierarchy.parent){var s=p as UnityEngine.UIElements.ScrollView; if(s!=null){sv=s;break;}} if(sv!=null){sv.ScrollTo(add); w.Repaint(); return sb.Append("scrolled - rerun").ToString();} }
ZClick(add);
lights = lightsF.GetValue(rig) as System.Collections.ICollection;
sb.Append("lights AFTER=").Append(lights.Count).Append("\n");
return sb.ToString();
