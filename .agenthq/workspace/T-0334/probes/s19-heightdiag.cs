var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.Button btn=null, mask=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue; if (b.text=="Height") btn=b; if (b.text=="Mask") mask=b; }
if (btn==null) return "no Height button";
sb.Append("Height btn type=").Append(btn.GetType().Name).Append(" cls=").Append(ZCls(btn)).Append("\n");
sb.Append("  rect=").Append(btn.worldBound).Append(" enabled=").Append(btn.enabledInHierarchy).Append("\n");
// what is under the pointer at the button's centre?
var c = btn.worldBound.center;
var panel = w.rootVisualElement.panel;
var picked = panel.Pick(c);
sb.Append("  Pick(centre) = ").Append(picked==null?"<null>":picked.GetType().Name + " cls=" + ZCls(picked) + " rect=" + picked.worldBound).Append("\n");
// any popover / scrim alive?
int pops=0; foreach (var e in ZAll(panel.visualTree)) if (ZCls(e).Contains("zui-popover")||e.name=="zui-popover-scrim") pops++;
sb.Append("  popover/scrim elements=").Append(pops).Append("\n");
return sb.ToString();
