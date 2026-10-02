var sb = new System.Text.StringBuilder();
foreach (var n in new string[] { "PropWindow", "TilesetBuilderWindow" })
{
    var w = ZWin(n);
    sb.Append("== ").Append(n).Append(" ==\n");
    foreach (var e in ZAll(w.rootVisualElement))
        if (ZDrawn(e) && ZIsLeafCtrl(e))
            sb.Append("  CTRL '").Append(ZCaption(e)).Append("' ").Append(e.GetType().Name)
              .Append(" enabled=").Append(e.enabledInHierarchy)
              .Append(" w=").Append(e.worldBound.width.ToString("F0"))
              .Append(" tip=").Append(ZTip(e).Length == 0 ? "<NONE>" : (ZTip(e).Length > 60 ? ZTip(e).Substring(0,60) : ZTip(e))).Append("\n");
}
return sb.ToString();
