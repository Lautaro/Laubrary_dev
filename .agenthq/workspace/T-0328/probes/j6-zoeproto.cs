var sb = new System.Text.StringBuilder();
sb.Append(ZBind("ZoeWindow", "Assets/Demos/ProtoGuyDemo/ProtoGuy.asset")).Append("\n");
var w = ZWin("ZoeWindow"); w.position = new UnityEngine.Rect(40, 40, 1000, 900); w.Focus();
return sb.ToString();
