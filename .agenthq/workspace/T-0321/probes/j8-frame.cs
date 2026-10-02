var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var tn = e.GetType().Name;
  if (tn=="Slider"||tn=="SliderInt"||tn=="MinMaxSlider"||tn=="IntegerField"||tn=="FloatField"||tn=="EnumField"||tn=="PopupField`1"||tn=="DropdownField")
    sb.Append("NATIVE ").Append(tn).Append(" label='").Append(ZOwnText(e)).Append("' cap='").Append(ZCaption(e)).Append("' cls=").Append(ZCls(e)).Append(" wb=").Append(e.worldBound).Append(" path=").Append(ZPath(e)).Append("\n");
}
return sb.ToString();
