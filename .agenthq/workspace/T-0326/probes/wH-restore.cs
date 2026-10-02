// Put the windows I resized back, and bind Shaper to the shipped demo document.
var sizes = new System.Collections.Generic.Dictionary<string, UnityEngine.Rect>();
sizes["ShaperWindow"]   = new UnityEngine.Rect(40, 20, 1100, 900.22f);
sizes["PyreWindow"]     = new UnityEngine.Rect(2260.89f, 71.11f, 950.22f, 824.67f);
sizes["ZoeWindow"]      = new UnityEngine.Rect(2184, 20, 900, 875.78f);
sizes["LatheWindow"]    = new UnityEngine.Rect(40, 20, 900, 875.78f);
sizes["LarderWindow"]   = new UnityEngine.Rect(40, 20, 900, 875.78f);
sizes["SpriteFxStackWindow"] = new UnityEngine.Rect(40, 20, 900, 875.78f);
sizes["TextSplashWindow"]    = new UnityEngine.Rect(40, 20, 1000, 875.78f);
sizes["MirageWindow"]   = new UnityEngine.Rect(2128.89f, 135.56f, 900, 760.22f);
sizes["TapestryWindow"] = new UnityEngine.Rect(100, 95.56f, 1100, 800.22f);
sizes["ChoreographerWindow"] = new UnityEngine.Rect(185.78f, 92.89f, 987.56f, 802.89f);
sizes["BackSplashWindow"] = new UnityEngine.Rect(80, 60, 820, 620.22f);
var sb = new System.Text.StringBuilder();
foreach (var kv in sizes) { var w = ZWin(kv.Key); if (w != null) { w.position = kv.Value; w.Repaint(); sb.Append(kv.Key).Append(" -> ").Append(w.position).Append("\n"); } }
sb.Append(ZBind("ShaperWindow", "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset")).Append("\n");
return sb.ToString();
