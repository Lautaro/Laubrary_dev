var win = ZWin("ShaperWindow"); if (win == null) return "NO SHAPER";
var BFa = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var sb = new System.Text.StringBuilder();
int n = 0, dead = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (e.GetType().Name != "ZuiMicroMinMax" || !ZDrawn(e)) continue;
    var t = e.GetType(); n++;
    float lo = (float)t.GetField("_low", BFa).GetValue(e), hi = (float)t.GetField("_high", BFa).GetValue(e);
    float mn = (float)t.GetField("_min", BFa).GetValue(e), mx = (float)t.GetField("_max", BFa).GetValue(e);
    int dec = (int)t.GetField("_decimals", BFa).GetValue(e);
    float step = (mx - mn) * 0.01f;
    double rounded = dec >= 0 ? System.Math.Round(lo + step, dec) : System.Math.Round(lo + step, 5);
    bool moves = !UnityEngine.Mathf.Approximately((float)rounded, lo);
    if (!moves) dead++;
    sb.Append(moves ? "  moves " : "  DEAD  ").Append("'").Append(ZCaption(e)).Append("' range=").Append(mn).Append("..").Append(mx)
      .Append(" decimals=").Append(dec).Append(" step=").Append(step.ToString("F4"))
      .Append(" low=").Append(lo).Append(" -> ").Append(rounded).Append("\n");
}
return "ZuiMicroMinMax drawn=" + n + " arrowDoesNothing=" + dead + "\n" + sb.ToString();
