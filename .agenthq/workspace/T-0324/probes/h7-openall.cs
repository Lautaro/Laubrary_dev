foreach (var n in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow"})
{ var w = ZOpen(n); if (w != null) w.position = new Rect(40, 20, 900, 880); }
return "opened";
