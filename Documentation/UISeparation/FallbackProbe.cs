var w=System.Array.Find(UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>(),x=>x.titleContent.text.Contains("Candidate"));
var env=UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiSkinEnvelope>(w.rootVisualElement);
var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var p=env.GetType().GetProperty("Presentation",flags).GetValue(env);
float padding=(float)p.GetType().GetField("paddingLeft",flags).GetValue(p);
float expected=w.rootVisualElement.ClassListContains("lau-tool-pilot")?18:11;
if(padding!=expected) throw new System.Exception("Legacy fallback expected "+expected+", got "+padding);
return "PASS envelope caller fallback/scoped precedence: "+padding;
