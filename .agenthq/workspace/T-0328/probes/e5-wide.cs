var sb = new System.Text.StringBuilder();
foreach (var n in new string[]{"LatheWindow","TextSplashWindow","BackSplashWindow"})
{
    var w = ZWin(n); if (w == null) continue;
    w.position = new UnityEngine.Rect(20, 20, 1500, 880);
}
return "widened to 1500";
