int n=0; foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) { w.Close(); n++; }
return "closed shaper=" + n;
