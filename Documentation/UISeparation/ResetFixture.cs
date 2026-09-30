var w=System.Array.Find(UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>(),x=>x.titleContent.text.Contains("Candidate"));
w.GetType().GetMethod("ResetFixture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(w,null);
return Laubrary.UISeparationPilot.PilotWindow.State();
