var w=System.Array.Find(UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>(),x=>x.titleContent.text.Contains("Candidate"));
var env=UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiSkinEnvelope>(w.rootVisualElement);
env.RemoveFromClassList("zui-envelope--legacy-profile"); env.def.paddingLeft=11; env.Repaint();
Laubrary.UISeparationPilot.PilotWindow.SetOverride(true);
return "Legacy caller padding=11, no profile; scoped override applied";
