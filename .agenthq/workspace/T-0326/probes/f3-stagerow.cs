// The stage row's three states, read off the live window.
string wn = "MirageWindow";
var w = ZWin(wn); if (w == null) return "no " + wn;
foreach (var e in ZAll(w.rootVisualElement))
{
    var b = e as UnityEngine.UIElements.Button;
    if (b == null || b.text != "Open preview stage") continue;
    return "button '" + b.text + "' drawn=" + ZDrawn(b) + " enabled=" + b.enabledInHierarchy
      + " width=" + b.resolvedStyle.width.ToString("0.##")
      + "\n  own tooltip: " + b.tooltip
      + "\n  row tooltip: " + (b.hierarchy.parent != null ? b.hierarchy.parent.tooltip : "<none>")
      + "\n  effective (ZTip): " + ZTip(b);
}
return "NO 'Open preview stage' BUTTON";
