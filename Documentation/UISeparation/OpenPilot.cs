// The guard exists so this snippet can never drive an unrelated Unity editor. Since the foundation was
// reconciled into the canonical project (T-0550), both hosts are legitimate targets.
var here = UnityEngine.Application.dataPath.Replace('\\', '/');
if (!here.EndsWith("Laubrary Dev - UI Separation/Assets") && !here.EndsWith("Laubrary Dev/Assets")) throw new System.Exception("Wrong editor target");
Laubrary.UISeparationPilot.PilotWindow.OpenPair(false);
return UnityEngine.Application.dataPath;
