// Reload the demo scene from disk FIRST (this discards earlier probes' unsaved dirt and saves nothing),
// so the window's own "Open preview stage" button can be pressed without raising a modal save prompt —
// the prompt is exactly what the card forbids pressing into.
var sb = new System.Text.StringBuilder();
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("before scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append("\n");
UnityEditor.SceneManagement.EditorSceneManager.OpenScene(sc.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("reloaded scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append("\n");
var w = ZWin("MirageWindow");
if (w == null) return sb.Append("no Mirage window").ToString();
UnityEngine.UIElements.Button btn = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && b.text == "Open preview stage") btn = b; }
if (btn == null) return sb.Append("no stage button").ToString();
sb.Append("button enabled=").Append(btn.enabledInHierarchy).Append(" rect=").Append(btn.worldBound).Append("\n");
bool pressed = ZClick(btn);
sb.Append("pressed=").Append(pressed).Append("\n");
sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("after scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append("\n");
var rigT = ZType("MirageRig");
sb.Append("rigInScene=").Append(rigT == null ? "?" : (UnityEngine.Object.FindFirstObjectByType(rigT) == null ? "none" : "yes")).Append("\n");
return sb.ToString();
