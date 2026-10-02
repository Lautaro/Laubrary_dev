var sb=new System.Text.StringBuilder();
var t=ZType("MirageActiveView");
if (t!=null) { foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)) sb.Append("prop ").Append(p.Name).Append("=").Append(p.GetValue(null)).Append("\n"); }
var subj = ZType("MirageSubject");
sb.Append("subjects in scene=");
if (subj!=null) sb.Append(UnityEngine.Object.FindObjectsByType(subj, UnityEngine.FindObjectsSortMode.None).Length);
sb.Append("\nhud=");
var hud=ZType("MirageHud"); if (hud!=null) sb.Append(UnityEngine.Object.FindObjectsByType(hud, UnityEngine.FindObjectsSortMode.None).Length);
sb.Append("\nroots:");
foreach (var g in UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects()) sb.Append(" ").Append(g.name);
return sb.ToString();
