var sb=new System.Text.StringBuilder();
sb.Append(ZBind("SpriteFxStackWindow","Assets/SpriteFx/AuditT323Walk1.asset")).Append("\n");
var w=ZWin("SpriteFxStackWindow"); w.position=new Rect(20,20,900,880);
sb.Append(ZBind("TextSplashWindow","Assets/Shaper/AuditT323Splash.asset")).Append("\n");
return sb.ToString();
