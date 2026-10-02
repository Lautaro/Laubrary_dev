// The live window: every DISABLED control on the bound document's card, with the reason on its tooltip,
// plus a ZuiAudit over the whole window.
var w = ZWin("ShaperWindow"); if (w==null) return "no window";
var sb = new System.Text.StringBuilder();
int elements=0, controls=0, disabled=0, disabledNoReason=0;
var seen = new System.Collections.Generic.List<string>();
foreach (var e in ZAll(w.rootVisualElement)) {
  elements++;
  bool isCtl = e is UnityEngine.UIElements.BaseField<float> || e is UnityEngine.UIElements.BaseField<int>
            || e is UnityEngine.UIElements.Button || e.GetType().Name.StartsWith("Zui");
  if (!isCtl) continue;
  controls++;
  if (e.enabledInHierarchy) continue;
  bool parentDisabled=false; for (var p=e.hierarchy.parent;p!=null;p=p.hierarchy.parent) if(!p.enabledSelf){parentDisabled=true;break;}
  if (parentDisabled) continue;
  disabled++;
  string tip = ZTip(e);
  string cap = ZCaption(e);
  if (string.IsNullOrEmpty(tip)) disabledNoReason++;
  if (seen.Count < 40) seen.Add((string.IsNullOrEmpty(cap)?"<no caption>":cap) + "  ::  " + (string.IsNullOrEmpty(tip)?"<NO REASON>":tip));
}
sb.Append("elements=").Append(elements).Append(" controls=").Append(controls)
  .Append(" disabledLeaves=").Append(disabled).Append(" disabledWithNoReason=").Append(disabledNoReason).Append("\n\n");
foreach (var s in seen) sb.Append(s).Append('\n');
return sb.ToString();
