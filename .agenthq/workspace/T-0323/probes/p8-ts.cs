var sb=new System.Text.StringBuilder();
var w=ZWin("TextSplashWindow"); if (w==null) return "no window";
sb.Append("elements=").Append(ZAll(w.rootVisualElement).Count).Append(" pos=").Append(w.position).Append("\n");
sb.Append(ZBind("TextSplashWindow","Assets/Demos/TextSplashDemo/Splash Demo.asset")).Append("\n");
return sb.ToString();
