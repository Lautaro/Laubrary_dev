var result = new System.Collections.Generic.List<string>();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.FoundationWindow>()) result.Add(w.titleContent.text + " " + w.position + " reference=" + typeof(Laubrary.UISeparationPilot.FoundationWindow).GetField("_reference", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(w));
return string.Join("\n", result);
