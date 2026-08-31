System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperBorderAudit"); if (x != null) { t = x; break; } }
var m = t.GetMethod("BT12_SubtractRefusal");
string r = (string)m.Invoke(null, null);
return "hasAncestorClipLeg=" + r.Contains("BD-3.4 ancestor clip") + " || " + r;
