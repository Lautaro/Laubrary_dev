foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>()) w.Close();
Laubrary.UISeparationPilot.PilotWindow.OpenPair(false);
return "Fresh pair opened";
