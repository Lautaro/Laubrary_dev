var sb=new System.Text.StringBuilder();
var w=ZWin("ShaperWindow");
foreach (var e in ZAll(w.rootVisualElement)) { var im=e as UnityEngine.UIElements.Image; if (im==null||!ZDrawn(im)) continue;
  var t=im.image as UnityEngine.Texture2D;
  sb.Append("IMG ").Append(im.worldBound).Append(" cls=").Append(ZCls(im)).Append(" tex=").Append(t==null?"null":(t.width+"x"+t.height)).Append(" path=").Append(ZPath(im)).Append("\n"); }
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
foreach (var f in w.GetType().GetFields(BFi)) if (f.FieldType==typeof(UnityEngine.Texture2D)) sb.Append("fld ").Append(f.Name).Append("=").Append(f.GetValue(w)!=null?((UnityEngine.Texture2D)f.GetValue(w)).width+"x"+((UnityEngine.Texture2D)f.GetValue(w)).height:"null").Append("\n");
foreach (var f in w.GetType().GetFields(BFi)) if (f.FieldType==typeof(bool)&&(f.Name.Contains("play")||f.Name.Contains("Play"))) sb.Append("fld ").Append(f.Name).Append("=").Append(f.GetValue(w)).Append("\n");
return sb.ToString();
